[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180,
    [string]$PostPhase11Marker = '',
    [switch]$EnablePhase15Rx,
    [string]$Phase15InjectorPath = '',
    [ValidateSet('dgram', 'user')]
    [string]$Phase15NetworkBackend = 'dgram',
    [switch]$Phase15KeepDefaultNic,
    [switch]$Phase15AllowHarnessDeferral,
    [switch]$Phase15AcceptEitherOutcome,
    [switch]$EnablePhase16Protocol,
    [switch]$EnablePhase17Protocol,
    [switch]$EnablePhase18Protocol,
    [switch]$EnablePhase19Protocol,
    [switch]$EnablePhase20Protocol,
    [switch]$EnablePhase21Protocol,
    [switch]$Phase15EnableFilterDump,
    [switch]$Phase15EnableQemuReceiveTrace,
    [ValidateSet('all', 'rx', 'tx')]
    [string]$Phase15FilterDumpQueue = 'tx'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll'
$expectedHash = $PayloadSha256.ToUpperInvariant()
$phase15Injector = if ([string]::IsNullOrEmpty($Phase15InjectorPath)) {
    Join-Path $PSScriptRoot 'Inject-ManagedE1000Phase15Frame.ps1'
} else { [IO.Path]::GetFullPath($Phase15InjectorPath) }

function Require11([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-OwnedQemu11 {
    $scope = @([IO.Path]::GetFullPath($gate), [IO.Path]::GetFullPath($evidence))
    return @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
        Where-Object {
            $commandLine = [string]$_.CommandLine
            $scope | Where-Object {
                $commandLine.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
            } | Select-Object -First 1
        })
}

function Stop-OwnedQemu11([System.Diagnostics.Process]$process) {
    if ($null -eq $process) { return }
    try { $process.Refresh() } catch { }
    try {
        if (!$process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    } catch { }
    try { $process.WaitForExit(5000) | Out-Null } catch { }
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        throw "Owned QEMU process remained: $($process.Id)"
    }
}

function Connect-Tcp11([int]$port, [System.Diagnostics.Process]$process,
                       [datetime]$deadline, [string]$name) {
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "QEMU exited before $name connection on port $port." }
        $client = [Net.Sockets.TcpClient]::new()
        try {
            $attempt = $client.ConnectAsync('127.0.0.1', $port)
            if ($attempt.Wait(500) -and $client.Connected) { return $client }
        } catch { }
        $client.Dispose()
        Start-Sleep -Milliseconds 50
    }
    throw "Timed out connecting to QEMU $name TCP port $port."
}

function Get-FreeUdpPort11 {
    $probe = [Net.Sockets.UdpClient]::new([Net.IPAddress]::Loopback, 0)
    try { return ([Net.IPEndPoint]$probe.Client.LocalEndPoint).Port }
    finally { $probe.Dispose() }
}

function Pump-Serial11([System.IO.Stream]$stream, [IO.FileStream]$logStream,
                       [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ($true) {
        if ($null -eq $script:phase11ReadTask) {
            $script:phase11ReadTask = $stream.ReadAsync($buffer, 0, $buffer.Length)
        }
        if (!$script:phase11ReadTask.IsCompleted) { return }
        $count = $script:phase11ReadTask.Result
        $script:phase11ReadTask = $null
        if ($count -le 0) { return }
        $logStream.Write($buffer, 0, $count)
        $chunk = [Text.Encoding]::ASCII.GetString($buffer, 0, $count)
        $text.Append($chunk) | Out-Null
        $script:phase11Tail = $script:phase11Tail + $chunk
        if ($script:phase11Tail.Length -gt 8192) {
            $script:phase11Tail = $script:phase11Tail.Substring($script:phase11Tail.Length - 8192)
        }
    }
}

function Write-Timeline11([IO.StreamWriter]$timeline, [string]$event,
                          [string]$detail = '') {
    $suffix = if ([string]::IsNullOrEmpty($detail)) { '' } else { " $detail" }
    $timeline.WriteLine(('event={0} utc={1:o}{2}' -f $event,
        (Get-Date).ToUniversalTime(), $suffix))
    $timeline.Flush()
}

function Wait-Marker11([string]$marker, [datetime]$deadline,
                       [System.Diagnostics.Process]$process,
                       [System.IO.Stream]$stream, [IO.FileStream]$logStream,
                       [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ((Get-Date) -lt $deadline) {
        Pump-Serial11 $stream $logStream $text $buffer
        $transcript = $text.ToString()
        if ($transcript.Contains($marker)) {
            Write-Timeline11 $script:phase11Timeline 'GUEST_MARKER' "marker=$marker"
            return
        }
        if ($transcript.Contains('GXOS_NET10:FAIL:') -or
            $transcript.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
            $transcript.Contains('GXOS_NET10:PAGE_FAULT_')) {
            throw "QEMU reported a fault while waiting for $marker."
        }
        if ($process.HasExited) { throw "QEMU exited while waiting for $marker." }
        Start-Sleep -Milliseconds 25
    }
    throw "Timed out waiting for QEMU marker: $marker"
}

function Wait-Phase15Outcome11([datetime]$deadline,
                                [System.Diagnostics.Process]$process,
                                [System.IO.Stream]$stream, [IO.FileStream]$logStream,
                                [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ((Get-Date) -lt $deadline) {
        Pump-Serial11 $stream $logStream $text $buffer
        $transcript = $text.ToString()
        if ($transcript.Contains('MANAGED_KERNEL_PHASE15_PASS')) {
            Write-Timeline11 $script:phase11Timeline 'GUEST_MARKER' `
                'marker=MANAGED_KERNEL_PHASE15_PASS outcome=PASS'
            return 'PASS'
        }
        if ($transcript.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_HARNESS_DEFERRED')) {
            Write-Timeline11 $script:phase11Timeline 'GUEST_MARKER' `
                'marker=GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_HARNESS_DEFERRED outcome=DEFERRED'
            return 'DEFERRED'
        }
        if ($transcript.Contains('GXOS_NET10:FAIL:') -or
            $transcript.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
            $transcript.Contains('GXOS_NET10:PAGE_FAULT_')) {
            throw 'QEMU reported a fault while waiting for the Phase 15 outcome.'
        }
        if ($process.HasExited) { throw 'QEMU exited while waiting for the Phase 15 outcome.' }
        Start-Sleep -Milliseconds 25
    }
    throw 'Timed out waiting for a Phase 15 pass or bounded deferral.'
}

function Send-Serial11([Net.Sockets.TcpClient]$client, [System.IO.Stream]$stream,
                       [System.Diagnostics.Process]$process,
                       [IO.StreamWriter]$injectionLog, [string]$afterMarker,
                       [byte]$value) {
    Require11 ($client.Connected -and $stream.CanWrite -and !$process.HasExited) `
        'QEMU serial socket is not writable.'
    $client.Client.NoDelay = $true
    $stream.WriteByte($value)
    $stream.Flush()
    $injectionLog.WriteLine(('{0} utc={1:o} serial_byte=0x{2:X2}' -f
        $afterMarker, (Get-Date).ToUniversalTime(), $value))
    $injectionLog.Flush()
    Write-Timeline11 $script:phase11Timeline 'HOST_SERIAL_INJECT' `
        "after=$afterMarker byte=0x$('{0:X2}' -f $value)"
}

function Send-SerialBurst11([Net.Sockets.TcpClient]$client, [System.IO.Stream]$stream,
                            [System.Diagnostics.Process]$process,
                            [IO.StreamWriter]$injectionLog, [string]$afterMarker,
                            [byte[]]$values) {
    Require11 ($client.Connected -and $stream.CanWrite -and !$process.HasExited) `
        'QEMU serial socket is not writable.'
    $client.Client.NoDelay = $true
    $stream.Write($values, 0, $values.Length)
    $stream.Flush()
    $hex = (($values | ForEach-Object { '0x{0:X2}' -f $_ }) -join ',')
    $injectionLog.WriteLine(('{0} utc={1:o} serial_bytes={2}' -f
        $afterMarker, (Get-Date).ToUniversalTime(), $hex))
    $injectionLog.Flush()
    Write-Timeline11 $script:phase11Timeline 'HOST_SERIAL_BURST' "after=$afterMarker bytes=$hex"
}

function Send-Key11([Net.Sockets.TcpClient]$monitor, [System.Diagnostics.Process]$process,
                    [IO.StreamWriter]$injectionLog, [string]$afterMarker,
                    [string]$key) {
    Require11 ($monitor.Connected -and !$process.HasExited) 'QEMU monitor is not connected.'
    $monitor.Client.NoDelay = $true
    $bytes = [Text.Encoding]::ASCII.GetBytes("sendkey $key`n")
    $monitor.GetStream().Write($bytes, 0, $bytes.Length)
    $monitor.GetStream().Flush()
    $injectionLog.WriteLine(('{0} utc={1:o} monitor_command=sendkey {2}' -f
        $afterMarker, (Get-Date).ToUniversalTime(), $key))
    $injectionLog.Flush()
    Write-Timeline11 $script:phase11Timeline 'HOST_KEY_INJECT' "after=$afterMarker key=$key"
}

function New-Phase15Frame11([string]$destinationMac, [bool]$phase17 = $false,
                             [bool]$phase18 = $false, [bool]$phase19 = $false,
                             [bool]$phase20 = $false, [bool]$phase21 = $false) {
    $destination = New-Object byte[] 6
    for ($index = 0; $index -lt 6; $index++) {
        $destination[$index] = [Convert]::ToByte($destinationMac.Substring($index * 2, 2), 16)
    }
    $source = [byte[]](0x02, 0x15, 0x00, 0x00, 0x00, 0x01)
    $signature = [Text.Encoding]::ASCII.GetBytes('guideXOS ManagedKernel Phase15 RX')
    $frame = New-Object byte[] 60
    [Array]::Copy($destination, 0, $frame, 0, 6)
    [Array]::Copy($source, 0, $frame, 6, 6)
    $frame[12] = 0x88
    $frame[13] = 0xB5
    [Array]::Copy($signature, 0, $frame, 14, $signature.Length)
    $sequence = if ($phase21) { 0x21000001 } `
        elseif ($phase20) { 0x20000001 } `
        elseif ($phase19) { 0x19000001 } `
        elseif ($phase18) { 0x18000001 } `
        elseif ($phase17) { 0x17000001 } else { 0x15000001 }
    $sequenceOffset = 14 + $signature.Length
    $frame[$sequenceOffset] = [byte](($sequence -shr 24) -band 0xFF)
    $frame[$sequenceOffset + 1] = [byte](($sequence -shr 16) -band 0xFF)
    $frame[$sequenceOffset + 2] = [byte](($sequence -shr 8) -band 0xFF)
    $frame[$sequenceOffset + 3] = [byte]($sequence -band 0xFF)
    return $frame
}

function Send-Phase15DgramFrame11([Net.Sockets.UdpClient]$peerUdp,
                                  [int]$destinationPort,
                                  [string]$destinationMac,
                                  [string]$destinationHost = '127.0.0.1',
                                   [bool]$phase17 = $false,
                                    [bool]$phase18 = $false,
                                    [bool]$phase19 = $false,
                                    [bool]$phase20 = $false,
                                    [bool]$phase21 = $false) {
    $frame = New-Phase15Frame11 $destinationMac $phase17 $phase18 $phase19 $phase20 $phase21
    $sent = $peerUdp.Send($frame, $frame.Length, $destinationHost, $destinationPort)
    Require11 ($sent -eq $frame.Length) 'Phase 15 UDP injector sent a short Ethernet datagram.'
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $frameHash = ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '') }
    finally { $hash.Dispose() }
    $marker = if ($phase21) { 'MANAGED_E1000_PHASE21_INJECTED' } `
        elseif ($phase20) { 'MANAGED_E1000_PHASE20_INJECTED' } `
        elseif ($phase19) { 'MANAGED_E1000_PHASE19_INJECTED' } `
        elseif ($phase18) { 'MANAGED_E1000_PHASE18_INJECTED' } `
        elseif ($phase17) { 'MANAGED_E1000_PHASE17_INJECTED' } `
        else { 'MANAGED_E1000_PHASE15_INJECTED' }
    $sequenceText = if ($phase21) { '0x21000001' } `
        elseif ($phase20) { '0x20000001' } `
        elseif ($phase19) { '0x19000001' } `
        elseif ($phase18) { '0x18000001' } `
        elseif ($phase17) { '0x17000001' } else { '0x15000001' }
    return ('{0}=PASS transport=dgram length={1} destination={2} source=021500000001 ethertype=88B5 sequence={3} frame_sha256={4} udp_source_port={5} udp_destination_port={6}' -f `
        $marker, $frame.Length, $destinationMac.ToUpperInvariant(), $sequenceText,
        $frameHash, ([Net.IPEndPoint]$peerUdp.Client.LocalEndPoint).Port,
        $destinationPort)
}

function New-MacBytes16([string]$text) {
    if ($text -notmatch '^[0-9A-Fa-f]{12}$') { throw "Invalid MAC: $text" }
    $mac = New-Object byte[] 6
    for ($index = 0; $index -lt 6; $index++) {
        $mac[$index] = [Convert]::ToByte($text.Substring($index * 2, 2), 16)
    }
    return $mac
}

function Bytes-Equal16([byte[]]$left, [int]$offset, [byte[]]$right) {
    if ($offset -lt 0 -or $offset + $right.Length -gt $left.Length) { return $false }
    for ($index = 0; $index -lt $right.Length; $index++) {
        if ($left[$offset + $index] -ne $right[$index]) { return $false }
    }
    return $true
}

function New-Phase16ArpFrame11([byte[]]$destination, [byte[]]$source,
                                [byte]$operation, [byte[]]$senderIp,
                                [byte[]]$targetMac, [byte[]]$targetIp) {
    $frame = New-Object byte[] 60
    [Array]::Copy($destination, 0, $frame, 0, 6)
    [Array]::Copy($source, 0, $frame, 6, 6)
    $frame[12] = 0x08; $frame[13] = 0x06
    $frame[15] = 1; $frame[16] = 0x08; $frame[18] = 6; $frame[19] = 4
    $frame[21] = $operation
    [Array]::Copy($source, 0, $frame, 22, 6)
    [Array]::Copy($senderIp, 0, $frame, 28, 4)
    [Array]::Copy($targetMac, 0, $frame, 32, 6)
    [Array]::Copy($targetIp, 0, $frame, 38, 4)
    return $frame
}

function Hash-Phase16Frame11([byte[]]$frame) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '').ToUpperInvariant() }
    finally { $hash.Dispose() }
}

function Receive-ExpectedPhase16Frame11([Net.Sockets.UdpClient]$peerUdp,
                                         [byte[]]$expected,
                                         [string]$name,
                                         [int]$timeoutSeconds) {
    $peerUdp.Client.ReceiveTimeout = 1000
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $seen = 0
    while ((Get-Date) -lt $deadline) {
        try {
            $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
            $frame = $peerUdp.Receive([ref]$remote)
        } catch [Net.Sockets.SocketException] {
            continue
        }
        $seen++
        if ($seen -gt 64) { throw "Too many guest Ethernet frames while waiting for $name." }
        if ($frame.Length -eq $expected.Length) {
            $match = $true
            for ($index = 0; $index -lt $expected.Length; $index++) {
                if ($frame[$index] -ne $expected[$index]) { $match = $false; break }
            }
            if ($match) { return ,$frame }
        }
    }
    throw "Timed out waiting for exact guest $name Ethernet frame."
}

function Write-Phase16Frame11([IO.StreamWriter]$log, [string]$name,
                              [byte[]]$frame) {
    $hex = ([BitConverter]::ToString($frame)).Replace('-', '')
    $hash = Hash-Phase16Frame11 $frame
    $log.WriteLine(('MANAGED_PHASE16_{0}=PASS length={1} destination={2} source={3} ethertype=0806 operation={4} sender_mac={5} sender_ip={6} target_mac={7} target_ip={8} frame_sha256={9}' -f `
        $name.ToUpperInvariant(), $frame.Length, $hex.Substring(0, 12),
        $hex.Substring(12, 12), $frame[21], $hex.Substring(44, 12),
        $hex.Substring(56, 8), $hex.Substring(64, 12), $hex.Substring(76, 8), $hash))
    $log.WriteLine(('MANAGED_PHASE16_{0}_FRAME_HEX={1}' -f $name.ToUpperInvariant(), $hex))
    $log.Flush()
}

function Write-U16-Phase17([byte[]]$bytes, [int]$offset, [int]$value) {
    $bytes[$offset] = [byte](($value -shr 8) -band 0xFF)
    $bytes[$offset + 1] = [byte]($value -band 0xFF)
}

function Read-U16-Phase17([byte[]]$bytes, [int]$offset) {
    return (([int]$bytes[$offset] -shl 8) -bor [int]$bytes[$offset + 1])
}

function Compute-Checksum-Phase17([byte[]]$bytes, [int]$offset,
                                   [int]$length) {
    [uint32]$sum = 0
    $index = 0
    while ($index + 1 -lt $length) {
        $sum += ([uint32]([int]$bytes[$offset + $index] -shl 8) -bor
            [uint32]$bytes[$offset + $index + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $index += 2
    }
    if ($index -lt $length) {
        $sum += [uint32]([int]$bytes[$offset + $index] -shl 8)
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}

function New-IcmpEcho-Phase17([byte]$type, [int]$identifier,
                               [int]$sequence, [byte[]]$payload,
                               [byte]$code = 0) {
    $icmp = New-Object byte[] (8 + $payload.Length)
    $icmp[0] = $type
    $icmp[1] = $code
    Write-U16-Phase17 $icmp 4 $identifier
    Write-U16-Phase17 $icmp 6 $sequence
    [Array]::Copy($payload, 0, $icmp, 8, $payload.Length)
    Write-U16-Phase17 $icmp 2 (Compute-Checksum-Phase17 $icmp 0 $icmp.Length)
    return $icmp
}

function New-Ipv4Icmp-Phase17([byte[]]$destinationMac,
                               [byte[]]$sourceMac, [byte[]]$sourceIp,
                               [byte[]]$destinationIp, [byte[]]$icmp,
                               [int]$identification = 0x1701,
                               [int]$flagsOffset = 0) {
    $totalLength = 20 + $icmp.Length
    $wireLength = [Math]::Max(60, 14 + $totalLength)
    $frame = New-Object byte[] $wireLength
    [Array]::Copy($destinationMac, 0, $frame, 0, 6)
    [Array]::Copy($sourceMac, 0, $frame, 6, 6)
    $frame[12] = 0x08; $frame[13] = 0x00
    $ip = 14
    $frame[$ip] = 0x45
    Write-U16-Phase17 $frame ($ip + 2) $totalLength
    Write-U16-Phase17 $frame ($ip + 4) $identification
    Write-U16-Phase17 $frame ($ip + 6) $flagsOffset
    $frame[$ip + 8] = 64
    $frame[$ip + 9] = 1
    [Array]::Copy($sourceIp, 0, $frame, $ip + 12, 4)
    [Array]::Copy($destinationIp, 0, $frame, $ip + 16, 4)
    Write-U16-Phase17 $frame ($ip + 10) `
        (Compute-Checksum-Phase17 $frame $ip 20)
    [Array]::Copy($icmp, 0, $frame, $ip + 20, $icmp.Length)
    return $frame
}

function Hash-Phase17Frame([byte[]]$frame) {
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '').ToUpperInvariant() }
    finally { $hash.Dispose() }
}

function Receive-ExpectedPhase17Frame([Net.Sockets.UdpClient]$peerUdp,
                                       [byte[]]$expected,
                                       [string]$name, [int]$timeoutSeconds) {
    $peerUdp.Client.ReceiveTimeout = 1000
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $seen = 0
    while ((Get-Date) -lt $deadline) {
        try {
            $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
            $frame = $peerUdp.Receive([ref]$remote)
        } catch [Net.Sockets.SocketException] {
            continue
        }
        $seen++
        if ($seen -gt 64) { throw "Too many guest Ethernet frames while waiting for $name." }
        if ($frame.Length -eq $expected.Length) {
            $match = $true
            for ($index = 0; $index -lt $expected.Length; $index++) {
                if ($frame[$index] -ne $expected[$index]) { $match = $false; break }
            }
            if ($match) { return ,$frame }
        }
    }
    throw "Timed out waiting for exact guest $name Ethernet frame."
}

function Write-Phase17Frame([IO.StreamWriter]$log, [string]$name,
                             [byte[]]$frame) {
    $hex = ([BitConverter]::ToString($frame)).Replace('-', '')
    $etherType = ('{0:X4}' -f (Read-U16-Phase17 $frame 12))
    $detail = ''
    if ($frame.Length -ge 34 -and $etherType -eq '0800') {
        $ip = 14
        $ihl = ($frame[$ip] -band 0x0F) * 4
        if ($ihl -ge 20 -and $frame.Length -ge $ip + $ihl + 8) {
            $icmp = $ip + $ihl
            $sourceIp = ([BitConverter]::ToString($frame[($ip + 12)..($ip + 15)])).Replace('-', '')
            $destinationIp = ([BitConverter]::ToString($frame[($ip + 16)..($ip + 19)])).Replace('-', '')
            $icmpPayloadHex = if ($frame.Length -gt $icmp + 8) {
                ([BitConverter]::ToString(
                    [byte[]]$frame[($icmp + 8)..($frame.Length - 1)])).Replace('-', '')
            } else { '' }
            $detail = ' ipv4_source={0} ipv4_destination={1} ipv4_total_length={2} ipv4_flags_offset={3} ipv4_ttl={4} ipv4_protocol={5} ipv4_checksum={6} icmp_type={7} icmp_code={8} icmp_checksum={9} icmp_identifier={10} icmp_sequence={11} icmp_payload={12}' -f `
                $sourceIp, $destinationIp, (Read-U16-Phase17 $frame ($ip + 2)),
                ('{0:X4}' -f (Read-U16-Phase17 $frame ($ip + 6))), $frame[$ip + 8],
                $frame[$ip + 9], ('{0:X4}' -f (Read-U16-Phase17 $frame ($ip + 10))),
                $frame[$icmp], $frame[$icmp + 1],
                ('{0:X4}' -f (Read-U16-Phase17 $frame ($icmp + 2))),
                ('{0:X4}' -f (Read-U16-Phase17 $frame ($icmp + 4))),
                ('{0:X4}' -f (Read-U16-Phase17 $frame ($icmp + 6))),
                $icmpPayloadHex
        }
    }
    $log.WriteLine(('MANAGED_PHASE17_{0}=PASS length={1} destination={2} source={3} ethertype={4} frame_sha256={5}{6}' -f `
        $name.ToUpperInvariant(), $frame.Length, $hex.Substring(0, 12),
        $hex.Substring(12, 12), $etherType, (Hash-Phase17Frame $frame), $detail))
    $log.WriteLine(('MANAGED_PHASE17_{0}_FRAME_HEX={1}' -f $name.ToUpperInvariant(), $hex))
    $log.Flush()
}

function Compute-UdpChecksum18([byte[]]$sourceIp, [byte[]]$destinationIp,
                                [byte[]]$udp) {
    [uint32]$sum = 0
    foreach ($offset in @(0, 2)) {
        $sum += (([uint32]$sourceIp[$offset] -shl 8) -bor
            [uint32]$sourceIp[$offset + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += (([uint32]$destinationIp[$offset] -shl 8) -bor
            [uint32]$destinationIp[$offset + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 17
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $udp.Length
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $checksumBytes = [byte[]]$udp.Clone()
    $checksumBytes[6] = 0
    $checksumBytes[7] = 0
    $index = 0
    while ($index + 1 -lt $checksumBytes.Length) {
        $sum += (([uint32]$checksumBytes[$index] -shl 8) -bor
            [uint32]$checksumBytes[$index + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $index += 2
    }
    if ($index -lt $checksumBytes.Length) {
        $sum += [uint32]$checksumBytes[$index] -shl 8
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}

function New-UdpDatagram18([int]$sourcePort, [int]$destinationPort,
                           [byte[]]$sourceIp, [byte[]]$destinationIp,
                           [byte[]]$payload, [bool]$zeroChecksum = $false) {
    $udp = New-Object byte[] (8 + $payload.Length)
    Write-U16-Phase17 $udp 0 $sourcePort
    Write-U16-Phase17 $udp 2 $destinationPort
    Write-U16-Phase17 $udp 4 $udp.Length
    [Array]::Copy($payload, 0, $udp, 8, $payload.Length)
    if (-not $zeroChecksum) {
        $checksum = Compute-UdpChecksum18 $sourceIp $destinationIp $udp
        if ($checksum -eq 0) { $checksum = 0xFFFF }
        Write-U16-Phase17 $udp 6 $checksum
    }
    return $udp
}

function New-Ipv4Udp18([byte[]]$destinationMac, [byte[]]$sourceMac,
                       [byte[]]$sourceIp, [byte[]]$destinationIp,
                       [byte[]]$udp, [int]$identification = 0x1901) {
    $totalLength = 20 + $udp.Length
    $wireLength = [Math]::Max(60, 14 + $totalLength)
    $frame = New-Object byte[] $wireLength
    [Array]::Copy($destinationMac, 0, $frame, 0, 6)
    [Array]::Copy($sourceMac, 0, $frame, 6, 6)
    $frame[12] = 0x08; $frame[13] = 0x00
    $ip = 14
    $frame[$ip] = 0x45
    Write-U16-Phase17 $frame ($ip + 2) $totalLength
    Write-U16-Phase17 $frame ($ip + 4) $identification
    $frame[$ip + 8] = 64
    $frame[$ip + 9] = 17
    [Array]::Copy($sourceIp, 0, $frame, $ip + 12, 4)
    [Array]::Copy($destinationIp, 0, $frame, $ip + 16, 4)
    Write-U16-Phase17 $frame ($ip + 10) (Compute-Checksum-Phase17 $frame $ip 20)
    [Array]::Copy($udp, 0, $frame, $ip + 20, $udp.Length)
    return $frame
}

function Read-U32-Phase19([byte[]]$bytes, [int]$offset) {
    return [uint32](([int]$bytes[$offset] -shl 24) -bor
        ([int]$bytes[$offset + 1] -shl 16) -bor
        ([int]$bytes[$offset + 2] -shl 8) -bor [int]$bytes[$offset + 3])
}

function Get-DhcpPayload19([byte[]]$frame) {
    if ($frame.Length -lt 42 -or $frame[12] -ne 8 -or $frame[13] -ne 0 -or
        $frame[14] -ne 0x45 -or $frame[23] -ne 17) { return $null }
    $udpLength = Read-U16-Phase17 $frame 38
    if ($udpLength -lt 8 -or 34 + $udpLength -gt $frame.Length) { return $null }
    return ,([byte[]]$frame[42..(33 + $udpLength)])
}

function Get-DhcpOption19([byte[]]$payload, [byte]$wanted) {
    if ($payload.Length -lt 241 -or (Read-U32-Phase19 $payload 236) -ne 0x63825363) {
        return $null
    }
    $offset = 240
    while ($offset -lt $payload.Length) {
        $code = $payload[$offset++]
        if ($code -eq 0) { continue }
        if ($code -eq 255) { return $null }
        if ($offset -ge $payload.Length) { return $null }
        $length = [int]$payload[$offset++]
        if ($offset + $length -gt $payload.Length) { return $null }
        if ($code -eq $wanted) {
            return ,([byte[]]$payload[$offset..($offset + $length - 1)])
        }
        $offset += $length
    }
    return $null
}

function New-DhcpReply19([byte[]]$requestPayload, [byte]$messageType,
                          [byte[]]$leaseIp, [byte[]]$serverIp,
                          [bool]$includeMask = $true,
                          [bool]$includeLease = $true,
                          [bool]$includeDns = $false) {
    if ($requestPayload.Length -lt 240) { throw 'DHCP request payload is truncated.' }
    $payload = New-Object byte[] 512
    $payload[0] = 2
    $payload[1] = 1
    $payload[2] = 6
    [Array]::Copy($requestPayload, 4, $payload, 4, 4)
    [Array]::Copy($requestPayload, 10, $payload, 10, 2)
    [Array]::Copy($leaseIp, 0, $payload, 16, 4)
    [Array]::Copy($requestPayload, 28, $payload, 28, 6)
    [Array]::Copy($serverIp, 0, $payload, 20, 4)
    Write-U32-Phase19 $payload 236 0x63825363
    $offset = 240
    $payload[$offset++] = 53; $payload[$offset++] = 1; $payload[$offset++] = $messageType
    if ($includeMask) {
        $payload[$offset++] = 1; $payload[$offset++] = 4
        $payload[$offset++] = 255; $payload[$offset++] = 255
        $payload[$offset++] = 255; $payload[$offset++] = 0
    }
    $payload[$offset++] = 54; $payload[$offset++] = 4
    [Array]::Copy($serverIp, 0, $payload, $offset, 4); $offset += 4
    if ($includeLease) {
        $payload[$offset++] = 51; $payload[$offset++] = 4
        Write-U32-Phase19 $payload $offset 3600; $offset += 4
    }
    if ($includeDns) {
        $payload[$offset++] = 6; $payload[$offset++] = 4
        [Array]::Copy($serverIp, 0, $payload, $offset, 4); $offset += 4
    }
    $payload[$offset++] = 255
    return ,([byte[]]$payload[0..($offset - 1)])
}

function Get-DnsPayload20([byte[]]$frame) {
    if ($frame.Length -lt 42 -or $frame[12] -ne 8 -or $frame[13] -ne 0 -or
        $frame[14] -ne 0x45 -or $frame[23] -ne 17) { return $null }
    $totalLength = Read-U16-Phase17 $frame 16
    $udpLength = Read-U16-Phase17 $frame 38
    if ($totalLength -ne 20 + $udpLength -or $udpLength -lt 8 -or
        34 + $udpLength -gt $frame.Length -or
        (Compute-Checksum-Phase17 $frame 14 20) -ne 0) { return $null }
    $sourceIp = [byte[]]$frame[26..29]
    $destinationIp = [byte[]]$frame[30..33]
    $udp = [byte[]]$frame[34..(33 + $udpLength)]
    $checksum = Read-U16-Phase17 $udp 6
    if ($checksum -eq 0) {
        return $null
    }
    $checksumBytes = [byte[]]$udp.Clone()
    $checksumBytes[6] = 0
    $checksumBytes[7] = 0
    $computedChecksum = Compute-UdpChecksum18 $sourceIp $destinationIp $checksumBytes
    if ($computedChecksum -eq 0) { $computedChecksum = 0xFFFF }
    if ($checksum -ne $computedChecksum) { return $null }
    if ($udpLength -le 8) { return ,([byte[]]@()) }
    return ,([byte[]]$udp[8..($udpLength - 1)])
}

function Receive-AnyDns20Frame([Net.Sockets.UdpClient]$peerUdp,
                                [int]$timeoutSeconds, [string]$name) {
    $peerUdp.Client.ReceiveTimeout = 1000
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
            $frame = $peerUdp.Receive([ref]$remote)
            if ($frame.Length -ge 42 -and
                (Read-U16-Phase17 $frame 34) -eq 15200 -and
                (Read-U16-Phase17 $frame 36) -eq 53) {
                return ,$frame
            }
        } catch [Net.Sockets.SocketException] { }
    }
    throw "Timed out waiting for $name DNS query frame."
}

function New-DnsResponse20([byte[]]$queryPayload, [string]$mode = 'valid') {
    if ($queryPayload.Length -lt 17) { throw 'DNS query payload is truncated.' }
    $queryId = Read-U16-Phase17 $queryPayload 0
    $response = New-Object byte[] 512
    Write-U16-Phase17 $response 0 $queryId
    Write-U16-Phase17 $response 2 0x8180
    Write-U16-Phase17 $response 4 1
    Write-U16-Phase17 $response 6 1
    Write-U16-Phase17 $response 8 0
    Write-U16-Phase17 $response 10 0
    [Array]::Copy($queryPayload, 12, $response, 12, $queryPayload.Length - 12)
    $answerOffset = $queryPayload.Length
    $offset = $answerOffset
    $response[$offset++] = 0xC0; $response[$offset++] = 0x0C
    Write-U16-Phase17 $response $offset 1
    Write-U16-Phase17 $response ($offset + 2) 1
    Write-U32-Phase19 $response ($offset + 4) 300
    Write-U16-Phase17 $response ($offset + 8) 4
    $offset += 10
    [Array]::Copy([byte[]](10, 15, 0, 2), 0, $response, $offset, 4)
    $offset += 4
    switch ($mode) {
        'wrong-id' {
            Write-U16-Phase17 $response 0 (($queryId + 1) -band 0xFFFF)
        }
        'truncated' {
            return ,([byte[]]$response[0..10])
        }
        'pointer-out-of-range' {
            $response[$answerOffset] = 0xC0
            $response[$answerOffset + 1] = 0xFF
        }
        'pointer-loop' {
            $response[$answerOffset] = 0xC0
            $response[$answerOffset] = [byte](0xC0 -bor (($answerOffset -shr 8) -band 0x3F))
            $response[$answerOffset + 1] = [byte]($answerOffset -band 0xFF)
        }
        'bad-rdlength' {
            Write-U16-Phase17 $response ($answerOffset + 10) 5
        }
        'mismatched-question' {
            $response[13] = 0x78
        }
        'tc' {
            Write-U16-Phase17 $response 2 0x8380
        }
        'nxdomain' {
            Write-U16-Phase17 $response 2 0x8183
            Write-U16-Phase17 $response 6 0
            return ,([byte[]]$response[0..($queryPayload.Length - 1)])
        }
    }
    return ,([byte[]]$response[0..($offset - 1)])
}

function Write-Phase20Frame([IO.StreamWriter]$log, [string]$name,
                             [byte[]]$frame) {
    $hex = ([BitConverter]::ToString($frame)).Replace('-', '')
    $log.WriteLine(('MANAGED_PHASE20_{0}=PASS length={1} destination={2} source={3} ethertype={4} frame_sha256={5}' -f
        $name.ToUpperInvariant(), $frame.Length, $hex.Substring(0, 12),
        $hex.Substring(12, 12), ('{0:X4}' -f (Read-U16-Phase17 $frame 12)),
        (Hash-Phase17Frame $frame)))
    $log.WriteLine(('MANAGED_PHASE20_{0}_FRAME_HEX={1}' -f
        $name.ToUpperInvariant(), $hex))
    $log.Flush()
}

function Write-U32-Phase19([byte[]]$bytes, [int]$offset, [uint32]$value) {
    $bytes[$offset] = [byte](($value -shr 24) -band 0xFF)
    $bytes[$offset + 1] = [byte](($value -shr 16) -band 0xFF)
    $bytes[$offset + 2] = [byte](($value -shr 8) -band 0xFF)
    $bytes[$offset + 3] = [byte]($value -band 0xFF)
}

function Write-Phase19Frame([IO.StreamWriter]$log, [string]$name,
                             [byte[]]$frame) {
    $hex = ([BitConverter]::ToString($frame)).Replace('-', '')
    $log.WriteLine(('MANAGED_PHASE19_{0}=PASS length={1} destination={2} source={3} ethertype={4} frame_sha256={5}' -f `
        $name.ToUpperInvariant(), $frame.Length, $hex.Substring(0, 12),
        $hex.Substring(12, 12), ('{0:X4}' -f (Read-U16-Phase17 $frame 12)),
        (Hash-Phase17Frame $frame)))
    $log.WriteLine(('MANAGED_PHASE19_{0}_FRAME_HEX={1}' -f $name.ToUpperInvariant(), $hex))
    $log.Flush()
}

function Receive-AnyPhase19Frame([Net.Sockets.UdpClient]$peerUdp,
                                  [int]$timeoutSeconds, [string]$name) {
    $peerUdp.Client.ReceiveTimeout = 1000
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
            $frame = $peerUdp.Receive([ref]$remote)
            if ($frame.Length -ge 60 -and $null -ne (Get-DhcpPayload19 $frame)) {
                return ,$frame
            }
        } catch [Net.Sockets.SocketException] { }
    }
    throw "Timed out waiting for $name Ethernet frame."
}

function Test-UdpFrame18([byte[]]$frame, [byte[]]$destinationMac,
                          [byte[]]$sourceMac, [byte[]]$sourceIp,
                          [byte[]]$destinationIp, [int]$sourcePort,
                          [int]$destinationPort, [byte[]]$payload,
                          [bool]$allowZeroChecksum = $false) {
    $totalLength = 20 + 8 + $payload.Length
    $wireLength = [Math]::Max(60, 14 + $totalLength)
    if ($frame.Length -ne $wireLength -or
        !(Bytes-Equal16 $frame 0 $destinationMac) -or
        !(Bytes-Equal16 $frame 6 $sourceMac) -or $frame[12] -ne 8 -or
        $frame[13] -ne 0 -or $frame[14] -ne 0x45 -or
        (Read-U16-Phase17 $frame 16) -ne $totalLength -or
        (Read-U16-Phase17 $frame 20) -ne 0 -or
        $frame[22] -ne 64 -or $frame[23] -ne 17 -or
        !(Bytes-Equal16 $frame 26 $sourceIp) -or
        !(Bytes-Equal16 $frame 30 $destinationIp) -or
        (Compute-Checksum-Phase17 $frame 14 20) -ne 0) { return $false }
    $udp = 34
    $udpLength = Read-U16-Phase17 $frame ($udp + 4)
    if ($udpLength -ne 8 + $payload.Length -or
        (Read-U16-Phase17 $frame $udp) -ne $sourcePort -or
        (Read-U16-Phase17 $frame ($udp + 2)) -ne $destinationPort -or
        !(Bytes-Equal16 $frame ($udp + 8) $payload)) { return $false }
    $checksum = Read-U16-Phase17 $frame ($udp + 6)
    if ($checksum -eq 0) { return $allowZeroChecksum }
    $udpBytes = [byte[]]$frame[$udp..($udp + $udpLength - 1)]
    $wireChecksum = $checksum
    $udpBytes[6] = 0
    $udpBytes[7] = 0
    $computedChecksum = Compute-UdpChecksum18 $sourceIp $destinationIp $udpBytes
    if ($computedChecksum -eq 0) { $computedChecksum = 0xFFFF }
    return $wireChecksum -eq $computedChecksum
}

function Write-Phase18Frame([IO.StreamWriter]$log, [string]$name,
                            [byte[]]$frame) {
    $hex = ([BitConverter]::ToString($frame)).Replace('-', '')
    $etherType = ('{0:X4}' -f (Read-U16-Phase17 $frame 12))
    $detail = ''
    if ($frame.Length -ge 42 -and $etherType -eq '0800' -and
        $frame[14] -eq 0x45 -and $frame[23] -eq 17) {
        $sourceIp = ([BitConverter]::ToString($frame[26..29])).Replace('-', '')
        $destinationIp = ([BitConverter]::ToString($frame[30..33])).Replace('-', '')
        $udp = 34
        $payloadHex = if ($frame.Length -gt $udp + 8) {
            ([BitConverter]::ToString(
                [byte[]]$frame[($udp + 8)..($frame.Length - 1)])).Replace('-', '')
        } else { '' }
        $detail = ' ipv4_source={0} ipv4_destination={1} ipv4_total_length={2} ipv4_ttl={3} ipv4_protocol={4} ipv4_checksum={5} udp_source_port={6} udp_destination_port={7} udp_length={8} udp_checksum={9} udp_payload={10}' -f `
            $sourceIp, $destinationIp, (Read-U16-Phase17 $frame 16), $frame[22],
            $frame[23], ('{0:X4}' -f (Read-U16-Phase17 $frame 24)),
            (Read-U16-Phase17 $frame $udp),
            (Read-U16-Phase17 $frame ($udp + 2)),
            (Read-U16-Phase17 $frame ($udp + 4)),
            ('{0:X4}' -f (Read-U16-Phase17 $frame ($udp + 6))), $payloadHex
    }
    $log.WriteLine(('MANAGED_PHASE18_{0}=PASS length={1} destination={2} source={3} ethertype={4} frame_sha256={5}{6}' -f `
        $name.ToUpperInvariant(), $frame.Length, $hex.Substring(0, 12),
        $hex.Substring(12, 12), $etherType, (Hash-Phase17Frame $frame), $detail))
    $log.WriteLine(('MANAGED_PHASE18_{0}_FRAME_HEX={1}' -f $name.ToUpperInvariant(), $hex))
    $log.Flush()
}

Require11 ($RunCount -ge 3) 'Three fresh ManagedKernel Phase 11 boots are required.'
Require11 ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'ManagedKernel EFI or payload is missing.'
Require11 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
Require11 ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'ManagedKernel payload size changed.'
Require11 ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
    'ManagedKernel staged payload hash does not match the requested identity.'
Require11 (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } `
    else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
Require11 (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require11 ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) 'OVMF firmware is required.'
if ($EnablePhase15Rx) {
    Require11 (Test-Path -LiteralPath $phase15Injector) "Phase 15 injector is missing: $phase15Injector"
}
if ($Phase15EnableFilterDump) {
    Require11 ($EnablePhase15Rx -and $Phase15NetworkBackend -eq 'dgram') `
        'Phase 15 filter-dump requires the dgram RX backend.'
}
if ($Phase15EnableQemuReceiveTrace) {
    Require11 ($EnablePhase15Rx -and $Phase15NetworkBackend -eq 'dgram') `
        'Phase 15 QEMU receive tracing requires the dgram RX backend.'
}
Require11 (@(Get-OwnedQemu11).Count -eq 0) 'An owned QEMU process is already running.'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null
(& $qemu --version 2>&1) | Set-Content -LiteralPath (Join-Path $evidence 'qemu-version.log') -Encoding ascii

$requiredMarkers = @(
    'GXOS_NET10:NATIVEAOT_STARTUP_OK',
    'GXOS_NET10:MANAGED_KERNEL_PHASE8_PASS',
    'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_SERVICES_INSTALLED',
    'GXOS_NET10:MANAGED_KERNEL_INPUT_SERVICE_NATIVE_NEGATIVE_TESTS_OK',
    'GXOS_NET10:MANAGED_KERNEL_INPUT_SERVICES_INSTALLED',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_DEVICE_BOUND',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_DRIVER_INIT_OK',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_DRIVER_START_OK',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SUBSCRIBED',
    'GXOS_NET10:MANAGED_KERNEL_INPUT_READY',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_IRQ_CAPTURED',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_EVENT_DISPATCHED',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_EVENT_OK',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_RUNTIME_SURVIVAL_OK',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBE_OK',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_SERIAL_REMAINS_ACTIVE_OK',
    'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK',
    'GXOS_NET10:MANAGED_KERNEL_MULTI_DRIVER_ROUTING_OK',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_OK',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WAKE_COALESCE_OK',
    'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ACCOUNTING_RESTORED_NATIVE_OK',
    'GXOS_NET10:MANAGED_KERNEL_PHASE9_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE10_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS')

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require11 (@(Get-OwnedQemu11).Count -eq 0) "A QEMU process already owns boot $sequence."
        $run = Join-Path $evidence ("runs\run-{0}" -f $sequence)
        New-Item -ItemType Directory -Force -Path $run | Out-Null
        $code = Join-Path $run 'edk2-code.fd'
        $vars = Join-Path $run 'edk2-vars.fd'
        $serial = Join-Path $run 'serial.log'
        $injections = Join-Path $run 'injections.log'
        $timelinePath = Join-Path $run 'timeline.log'
        $commandLinePath = Join-Path $run 'qemu-commandline.log'
        $firmwareIdentityPath = Join-Path $run 'firmware-identity.log'
        $stdout = Join-Path $run 'qemu.stdout.log'
        $stderr = Join-Path $run 'qemu.stderr.log'
        $pcapPath = if ($Phase15EnableFilterDump) {
            Join-Path $run 'netdev.pcap'
        } else { '' }
        $tracePath = if ($Phase15EnableQemuReceiveTrace) {
            Join-Path $run 'qemu-trace.log'
        } else { '' }
        $traceEventsPath = if ($Phase15EnableQemuReceiveTrace) {
            Join-Path $run 'qemu-trace-events.txt'
        } else { '' }
        Copy-Item -LiteralPath $ovmf -Destination $code
        Copy-Item -LiteralPath $varsTemplate -Destination $vars
        $codeHash = (Get-FileHash -LiteralPath $code -Algorithm SHA256).Hash.ToUpperInvariant()
        $varsHash = (Get-FileHash -LiteralPath $vars -Algorithm SHA256).Hash.ToUpperInvariant()
        Set-Content -LiteralPath $firmwareIdentityPath -Value @(
            "qemu=$qemu", "ovmf_code=$code", "ovmf_code_sha256=$codeHash",
            "ovmf_vars=$vars", "ovmf_vars_sha256=$varsHash") -Encoding ascii
        $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $probe.Start(); $serialPort = ([Net.IPEndPoint]$probe.LocalEndpoint).Port; $probe.Stop()
        $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $probe.Start(); $monitorPort = ([Net.IPEndPoint]$probe.LocalEndpoint).Port; $probe.Stop()
        $rxPort = 0
        $peerPort = 0
        if ($EnablePhase15Rx -and $Phase15NetworkBackend -eq 'dgram') {
            $rxPort = Get-FreeUdpPort11
            $peerPort = Get-FreeUdpPort11
        }
        $peerUdp = $null
        if ($EnablePhase15Rx -and $Phase15NetworkBackend -eq 'dgram') {
            # QEMU's dgram backend requires a live remote endpoint.  Keep the
            # host peer bound before QEMU starts so e1000 TX cannot stall on a
            # missing peer; the one test frame is sent later after RX_READY.
            $peerUdp = [Net.Sockets.UdpClient]::new([Net.Sockets.AddressFamily]::InterNetwork)
            $peerUdp.Client.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, $peerPort))
        }
        $arguments = @(
            '-machine', 'q35', '-accel', 'tcg,thread=single', '-m', '128M',
            '-drive', "if=pflash,format=raw,readonly=on,file=$code",
            '-drive', "if=pflash,format=raw,file=$vars",
            '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
            '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
            '-chardev', "socket,id=serial0,host=127.0.0.1,port=$serialPort,server=on,wait=on,telnet=off,ipv4=on,nodelay=on",
            '-serial', 'none', '-device', 'isa-serial,chardev=serial0,iobase=0x3f8,irq=4,wakeup=on',
            '-monitor', "tcp:127.0.0.1:$monitorPort,server=on,wait=on",
            '-display', 'none', '-no-reboot', '-no-shutdown')
        if ($EnablePhase15Rx) {
            if (-not $Phase15KeepDefaultNic) { $arguments += @('-nic', 'none') }
            if ($Phase15NetworkBackend -eq 'dgram') {
                $arguments += @(
                    '-netdev', "dgram,id=net0,local.type=inet,local.host=127.0.0.1,local.port=$rxPort,remote.type=inet,remote.host=127.0.0.1,remote.port=$peerPort")
            } else {
                $arguments += @('-netdev', 'user,id=net0')
            }
            $arguments += @('-device', 'e1000e,netdev=net0,addr=2')
            if ($Phase15EnableFilterDump) {
                $arguments += @('-object',
                    "filter-dump,id=phase15dump,netdev=net0,file=$pcapPath,maxlen=65535,queue=$Phase15FilterDumpQueue")
            }
            if ($Phase15EnableQemuReceiveTrace) {
                Set-Content -LiteralPath $traceEventsPath -Value @(
                    'e1000e_rx_can_recv',
                    'e1000e_rx_can_recv_rings_full',
                    'e1000e_rx_has_buffers',
                    'e1000x_rx_link_down',
                    'e1000x_rx_disabled',
                    'e1000x_rx_oversized',
                    'e1000e_rx_flt_dropped',
                    'e1000e_rx_receive_iov',
                    'e1000e_rx_start_recv',
                    'e1000e_rx_descr',
                    'e1000e_rx_desc_buff_write',
                    'e1000e_rx_written_to_guest',
                    'e1000e_rx_not_written_to_guest',
                    'e1000e_rx_set_rctl',
                    'e1000e_rx_set_rdt',
                    'e1000e_core_write') -Encoding ascii
                $arguments += @('-trace',
                    "events=$traceEventsPath,file=$tracePath")
            }
        }
        Set-Content -LiteralPath $commandLinePath -Value ('"{0}" {1}' -f $qemu, ($arguments -join ' ')) -Encoding ascii
        $process = $null; $client = $null; $monitor = $null; $stream = $null
        $logStream = $null; $injectionLog = $null; $timeline = $null
        $phase15Outcome = ''
        try {
            $timeline = [IO.StreamWriter]::new($timelinePath, $false, [Text.Encoding]::ASCII)
            $script:phase11Timeline = $timeline
            $listenerDetail = "serial_port=$serialPort monitor_port=$monitorPort"
            if ($EnablePhase15Rx -and $Phase15NetworkBackend -eq 'dgram') {
                $listenerDetail += " rx_port=$rxPort peer_port=$peerPort"
            }
            Write-Timeline11 $timeline 'HOST_LISTENERS_READY' $listenerDetail
            Write-Timeline11 $timeline 'FIRMWARE_IDENTITY' "code_sha256=$codeHash vars_sha256=$varsHash"
            $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate `
                -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
            $owned += $process
            Write-Timeline11 $timeline 'QEMU_STARTED' "pid=$($process.Id)"
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            $client = Connect-Tcp11 $serialPort $process $deadline 'serial'
            $monitor = Connect-Tcp11 $monitorPort $process $deadline 'monitor'
            Write-Timeline11 $timeline 'SERIAL_AND_MONITOR_CONNECTED'
            $stream = $client.GetStream()
            $logStream = [IO.File]::Open($serial, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $injectionLog = [IO.StreamWriter]::new($injections, $false, [Text.Encoding]::ASCII)
            $text = [Text.StringBuilder]::new(); $script:phase11Tail = ''; $script:phase11ReadTask = $null
            $buffer = New-Object byte[] 4096
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY' $deadline $process $stream $logStream $text $buffer
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' $deadline $process $stream $logStream $text $buffer
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' $deadline $process $stream $logStream $text $buffer
            Send-Serial11 $client $stream $process $injectionLog 'SERIAL_READY' 0x52
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK' $deadline $process $stream $logStream $text $buffer
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' $deadline $process $stream $logStream $text $buffer
            Send-Serial11 $client $stream $process $injectionLog 'SERIAL_SECOND_READY' 0x53
            Start-Sleep -Milliseconds 25
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' $deadline $process $stream $logStream $text $buffer
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY' $deadline $process $stream $logStream $text $buffer
            Send-Key11 $monitor $process $injectionLog 'KEYBOARD_INPUT_READY' 'a'
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_EVENT_OK' $deadline $process $stream $logStream $text $buffer
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_RUNTIME_SURVIVAL_OK' $deadline $process $stream $logStream $text $buffer
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY' $deadline $process $stream $logStream $text $buffer
            Send-Key11 $monitor $process $injectionLog 'KEYBOARD_SECOND_INPUT_READY' 'b'
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_READY' $deadline $process $stream $logStream $text $buffer
            Send-Key11 $monitor $process $injectionLog 'KEYBOARD_UNSUBSCRIBED_READY' 'c'
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK' $deadline $process $stream $logStream $text $buffer
            Send-Serial11 $client $stream $process $injectionLog 'KEYBOARD_B_SENT' 0x44
            Start-Sleep -Milliseconds 50
            Send-Serial11 $client $stream $process $injectionLog 'KEYBOARD_B_SENT' 0x45
            Start-Sleep -Milliseconds 50
            Send-Serial11 $client $stream $process $injectionLog 'KEYBOARD_B_SENT' 0x46
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_DRAINED' $deadline $process $stream $logStream $text $buffer
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS' $deadline $process $stream $logStream $text $buffer
            if (-not [string]::IsNullOrEmpty($PostPhase11Marker)) {
                Wait-Marker11 $PostPhase11Marker $deadline $process $stream $logStream $text $buffer
            }
            if ($EnablePhase15Rx -and $Phase15NetworkBackend -eq 'dgram') {
                Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_READY' $deadline $process $stream $logStream $text $buffer
                $macMatch = [regex]::Match($text.ToString(),
                    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})\s*([0-9A-Fa-f]{8})')
                Require11 $macMatch.Success 'Phase 15 did not publish the runtime e1000 MAC.'
                $destinationMac = ($macMatch.Groups[1].Value + $macMatch.Groups[2].Value)
                $injectOutput = if ($EnablePhase21Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $false $false $true)
                } elseif ($EnablePhase20Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $false $true)
                } elseif ($EnablePhase19Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $true)
                } elseif ($EnablePhase18Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $true)
                } else {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort `
                        $destinationMac '127.0.0.1' ([bool]$EnablePhase17Protocol))
                }
                $networkDetail = "backend=dgram port=$rxPort source_port=$peerPort"
                foreach ($line in $injectOutput) {
                    $injectionLog.WriteLine([string]$line)
                }
                $injectionLog.Flush()
                Write-Timeline11 $timeline 'HOST_E1000_RX_INJECT' `
                    "$networkDetail destination=$destinationMac"
                if ($Phase15AllowHarnessDeferral) {
                    Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_HARNESS_DEFERRED' `
                        $deadline $process $stream $logStream $text $buffer
                    $phase15Outcome = 'DEFERRED'
                } elseif ($Phase15AcceptEitherOutcome) {
                    $phase15Outcome = Wait-Phase15Outcome11 `
                        $deadline $process $stream $logStream $text $buffer
                } elseif ($EnablePhase21Protocol -or $EnablePhase20Protocol -or $EnablePhase19Protocol -or $EnablePhase18Protocol -or $EnablePhase17Protocol -or
                          $EnablePhase16Protocol) {
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_COMPLETE' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_FRAME_OK' `
                        $deadline $process $stream $logStream $text $buffer
                    $guestMacBytes = New-MacBytes16 $destinationMac
                    $broadcastMac = [byte[]](0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
                    $hostMacBytes = [byte[]](0x02, 0x15, 0, 0, 0, 2)
                    $guestIpBytes = if ($EnablePhase21Protocol -or $EnablePhase20Protocol -or $EnablePhase19Protocol) {
                        [byte[]](10, 15, 0, 42)
                    } else { [byte[]](10, 15, 0, 1) }
                    $hostIpBytes = [byte[]](10, 15, 0, 2)
                    $broadcastIpBytes = [byte[]](255, 255, 255, 255)
                    if ($EnablePhase21Protocol) {
                        # Phase 21 uses the same bounded DHCP/DNS peer but the
                        # managed application consumer drives all post-DNS
                        # operations through ManagedNetworkService.
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $discoverFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds 'DHCPDISCOVER'
                        $discoverPayload = Get-DhcpPayload19 $discoverFrame
                        Require11 ($null -ne $discoverPayload) 'Phase 21 DHCPDISCOVER is not IPv4/UDP.'
                        Write-Phase20Frame $injectionLog 'dhcpdiscover' $discoverFrame
                        $xid = Read-U32-Phase19 $discoverPayload 4
                        $offerPayload = New-DhcpReply19 $discoverPayload 2 $guestIpBytes $hostIpBytes $true $true $true
                        $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes $offerPayload) 0x2D11
                        Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length, '127.0.0.1', $rxPort) -eq $offerFrame.Length) 'Phase 21 DHCPOFFER send was short.'
                        Write-Phase20Frame $injectionLog 'dhcpoffer' $offerFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' $deadline $process $stream $logStream $text $buffer
                        $requestFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds 'DHCPREQUEST'
                        $requestPayload = Get-DhcpPayload19 $requestFrame
                        Require11 ($null -ne $requestPayload) 'Phase 21 DHCPREQUEST is not IPv4/UDP.'
                        Write-Phase20Frame $injectionLog 'dhcprequest' $requestFrame
                        $ackPayload = New-DhcpReply19 $requestPayload 5 $guestIpBytes $hostIpBytes $true $true $true
                        $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes $ackPayload) 0x2D12
                        Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length, '127.0.0.1', $rxPort) -eq $ackFrame.Length) 'Phase 21 DHCPACK send was short.'
                        Write-Phase20Frame $injectionLog 'dhcpack' $ackFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_CONFIGURED' $deadline $process $stream $logStream $text $buffer

                        $zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
                        $arpRequest = New-Phase16ArpFrame11 $broadcastMac $guestMacBytes 1 $guestIpBytes $zeroMac $hostIpBytes
                        $observedArp = Receive-ExpectedPhase16Frame11 $peerUdp $arpRequest 'Phase 21 DNS ARP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'dns_arp_request' $observedArp
                        $arpReply = New-Phase16ArpFrame11 $guestMacBytes $hostMacBytes 2 $hostIpBytes $guestMacBytes $guestIpBytes
                        Require11 ($peerUdp.Send($arpReply, $arpReply.Length, '127.0.0.1', $rxPort) -eq $arpReply.Length) 'Phase 21 DNS ARP reply send was short.'
                        Write-Phase20Frame $injectionLog 'dns_arp_reply' $arpReply

                        $dnsQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds 'phase21.test'
                        $dnsQueryPayload = Get-DnsPayload20 $dnsQueryFrame
                        $expectedQuestion = [byte[]](7,112,104,97,115,101,50,49,4,116,101,115,116,0,0,1,0,1)
                        Require11 ($null -ne $dnsQueryPayload -and $dnsQueryPayload.Length -eq 30 -and
                            (Bytes-Equal16 $dnsQueryPayload 12 $expectedQuestion)) 'Phase 21 DNS query name was invalid.'
                        Write-Phase20Frame $injectionLog 'dns_phase21_query' $dnsQueryFrame
                        $dnsResponse = New-DnsResponse20 $dnsQueryPayload 'valid'
                        $dnsResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $dnsResponse) 0x2E11
                        Require11 ($peerUdp.Send($dnsResponseFrame, $dnsResponseFrame.Length, '127.0.0.1', $rxPort) -eq $dnsResponseFrame.Length) 'Phase 21 DNS response send was short.'
                        Write-Phase20Frame $injectionLog 'dns_phase21_response' $dnsResponseFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_DNS_SUCCESS' $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_RESOLVED_IPV4=0x000000000A0F0002' $deadline $process $stream $logStream $text $buffer

                        $icmpPayload = [Text.Encoding]::ASCII.GetBytes('guideXOS Phase17 ping payload')
                        $icmpRequest = New-IcmpEcho-Phase17 8 0x2101 1 $icmpPayload
                        $icmpRequestFrame = New-Ipv4Icmp-Phase17 $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes $icmpRequest 0x2E12
                        $observedIcmp = Receive-ExpectedPhase17Frame $peerUdp $icmpRequestFrame 'Phase 21 API ICMP request' $TimeoutSeconds
                        Require11 ((Bytes-Equal16 $observedIcmp 30 $hostIpBytes)) 'Phase 21 ICMP destination was not resolver output.'
                        Write-Phase20Frame $injectionLog 'phase21_icmp_request' $observedIcmp
                        $icmpReply = New-IcmpEcho-Phase17 0 0x2101 1 $icmpPayload
                        $icmpReplyFrame = New-Ipv4Icmp-Phase17 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes $icmpReply 0x2E13
                        Require11 ($peerUdp.Send($icmpReplyFrame, $icmpReplyFrame.Length, '127.0.0.1', $rxPort) -eq $icmpReplyFrame.Length) 'Phase 21 ICMP reply send was short.'
                        Write-Phase20Frame $injectionLog 'phase21_icmp_reply' $icmpReplyFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_ICMP_SUCCESS' $deadline $process $stream $logStream $text $buffer

                        $appRequestPayload = [Text.Encoding]::ASCII.GetBytes('PHASE21-API-HELLO')
                        $appRequest = New-UdpDatagram18 15210 15211 $guestIpBytes $hostIpBytes $appRequestPayload
                        $appRequestFrame = New-Ipv4Udp18 $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes $appRequest 0x2E14
                        $observedAppRequest = Receive-ExpectedPhase17Frame $peerUdp $appRequestFrame 'Phase 21 API UDP request' $TimeoutSeconds
                        Require11 ((Bytes-Equal16 $observedAppRequest 30 $hostIpBytes) -and
                            (Bytes-Equal16 $observedAppRequest 42 $appRequestPayload)) 'Phase 21 UDP request was not exact.'
                        Write-Phase20Frame $injectionLog 'phase21_udp_request' $observedAppRequest
                        $appReplyPayload = [Text.Encoding]::ASCII.GetBytes('PHASE21-API-ACK')
                        $appReply = New-UdpDatagram18 15211 15210 $hostIpBytes $guestIpBytes $appReplyPayload
                        $appReplyFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes $appReply 0x2E15
                        Require11 ($peerUdp.Send($appReplyFrame, $appReplyFrame.Length, '127.0.0.1', $rxPort) -eq $appReplyFrame.Length) 'Phase 21 UDP reply send was short.'
                        Write-Phase20Frame $injectionLog 'phase21_udp_reply' $appReplyFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_UDP_REPLY_VALID' $deadline $process $stream $logStream $text $buffer

                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_GC_SURVIVAL_PASSED' $deadline $process $stream $logStream $text $buffer
                        $postQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds 'phase21.test post-GC'
                        $postQueryPayload = Get-DnsPayload20 $postQueryFrame
                        Write-Phase20Frame $injectionLog 'phase21_post_gc_dns_query' $postQueryFrame
                        $postResponse = New-DnsResponse20 $postQueryPayload 'valid'
                        $postResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $postResponse) 0x2E16
                        Require11 ($peerUdp.Send($postResponseFrame, $postResponseFrame.Length, '127.0.0.1', $rxPort) -eq $postResponseFrame.Length) 'Phase 21 post-GC DNS response send was short.'
                        Write-Phase20Frame $injectionLog 'phase21_post_gc_dns_response' $postResponseFrame
                        $postIcmp = New-IcmpEcho-Phase17 8 0x2102 2 $icmpPayload
                        $postIcmpFrame = New-Ipv4Icmp-Phase17 $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes $postIcmp 0x2E17
                        $observedPostIcmp = Receive-ExpectedPhase17Frame $peerUdp $postIcmpFrame 'Phase 21 post-GC ICMP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'phase21_post_gc_icmp_request' $observedPostIcmp
                        $postIcmpReply = New-IcmpEcho-Phase17 0 0x2102 2 $icmpPayload
                        $postIcmpReplyFrame = New-Ipv4Icmp-Phase17 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes $postIcmpReply 0x2E18
                        Require11 ($peerUdp.Send($postIcmpReplyFrame, $postIcmpReplyFrame.Length, '127.0.0.1', $rxPort) -eq $postIcmpReplyFrame.Length) 'Phase 21 post-GC ICMP reply send was short.'
                        Write-Phase20Frame $injectionLog 'phase21_post_gc_icmp_reply' $postIcmpReplyFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_POST_GC_ICMP_SUCCESS' $deadline $process $stream $logStream $text $buffer
                        $postAppRequest = New-UdpDatagram18 15210 15211 $guestIpBytes $hostIpBytes $appRequestPayload
                        $postAppRequestFrame = New-Ipv4Udp18 $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes $postAppRequest 0x2E19
                        $observedPostApp = Receive-ExpectedPhase17Frame $peerUdp $postAppRequestFrame 'Phase 21 post-GC UDP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'phase21_post_gc_udp_request' $observedPostApp
                        Require11 ($peerUdp.Send($appReplyFrame, $appReplyFrame.Length, '127.0.0.1', $rxPort) -eq $appReplyFrame.Length) 'Phase 21 post-GC UDP reply send was short.'
                        Write-Phase20Frame $injectionLog 'phase21_post_gc_udp_reply' $appReplyFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_POST_GC_UDP_SUCCESS' $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_TEARDOWN_PASSED' $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_PHASE21_PASS' $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'MANAGED_KERNEL_PHASE21_PASS' $deadline $process $stream $logStream $text $buffer
                        $phase15Outcome = 'PASS_PHASE21'
                    } elseif ($EnablePhase20Protocol) {
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $discoverFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds `
                            'DHCPDISCOVER'
                        $discoverPayload = Get-DhcpPayload19 $discoverFrame
                        Require11 ($null -ne $discoverPayload -and
                                   (Get-DhcpOption19 $discoverPayload 53)[0] -eq 1 -and
                                   (Read-U16-Phase17 $discoverFrame 34) -eq 68 -and
                                   (Read-U16-Phase17 $discoverFrame 36) -eq 67 -and
                                   (Bytes-Equal16 $discoverFrame 26 ([byte[]](0,0,0,0))) -and
                                   (Bytes-Equal16 $discoverFrame 30 $broadcastIpBytes)) `
                                   'Phase 20 DHCPDISCOVER fields are invalid.'
                        Write-Phase20Frame $injectionLog 'dhcpdiscover' $discoverFrame
                        $offerPayload = New-DhcpReply19 $discoverPayload 2 $guestIpBytes `
                            $hostIpBytes $true $true $true
                        Require11 ((Get-DhcpOption19 $offerPayload 6).Length -eq 4 -and
                                   (Read-U32-Phase19 (Get-DhcpOption19 $offerPayload 6) 0) -eq 0x0A0F0002) `
                                   'DHCPOFFER did not advertise DHCP Option 6.'
                        $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $offerPayload) 0x2D01
                        Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length,
                            '127.0.0.1', $rxPort) -eq $offerFrame.Length) `
                            'Phase 20 DHCPOFFER send was short.'
                        Write-Phase20Frame $injectionLog 'dhcpoffer' $offerFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $requestFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds `
                            'DHCPREQUEST'
                        $requestPayload = Get-DhcpPayload19 $requestFrame
                        Require11 ($null -ne $requestPayload -and
                                   (Get-DhcpOption19 $requestPayload 53)[0] -eq 3 -and
                                   (Read-U16-Phase17 $requestFrame 34) -eq 68 -and
                                   (Read-U16-Phase17 $requestFrame 36) -eq 67) `
                                   'Phase 20 DHCPREQUEST fields are invalid.'
                        Write-Phase20Frame $injectionLog 'dhcprequest' $requestFrame
                        $ackPayload = New-DhcpReply19 $requestPayload 5 $guestIpBytes `
                            $hostIpBytes $true $true $true
                        Require11 ((Get-DhcpOption19 $ackPayload 6).Length -eq 4 -and
                                   (Read-U32-Phase19 (Get-DhcpOption19 $ackPayload 6) 0) -eq 0x0A0F0002) `
                                   'DHCPACK did not advertise DHCP Option 6.'
                        $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $ackPayload) 0x2D02
                        Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length,
                            '127.0.0.1', $rxPort) -eq $ackFrame.Length) `
                            'Phase 20 DHCPACK send was short.'
                        Write-Phase20Frame $injectionLog 'dhcpack' $ackFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_READY' `
                            $deadline $process $stream $logStream $text $buffer

                        $zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
                        $arpRequest = New-Phase16ArpFrame11 $broadcastMac $guestMacBytes 1 `
                            $guestIpBytes $zeroMac $hostIpBytes
                        $observedArp = Receive-ExpectedPhase16Frame11 $peerUdp `
                            $arpRequest 'DNS ARP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'dns_arp_request' $observedArp
                        $arpReply = New-Phase16ArpFrame11 $guestMacBytes $hostMacBytes 2 `
                            $hostIpBytes $guestMacBytes $guestIpBytes
                        Require11 ($peerUdp.Send($arpReply, $arpReply.Length,
                            '127.0.0.1', $rxPort) -eq $arpReply.Length) `
                            'DNS ARP reply send was short.'
                        Write-Phase20Frame $injectionLog 'dns_arp_reply' $arpReply

                        $dnsQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds `
                            'phase20.test'
                        $dnsQueryPayload = Get-DnsPayload20 $dnsQueryFrame
                        $expectedQuestion = [byte[]](7,112,104,97,115,101,50,48,4,116,101,115,116,0,0,1,0,1)
                        Require11 ($null -ne $dnsQueryPayload -and
                                   $dnsQueryPayload.Length -eq 30 -and
                                   (Read-U16-Phase17 $dnsQueryPayload 2) -eq 0x0100 -and
                                   (Read-U16-Phase17 $dnsQueryPayload 4) -eq 1 -and
                                   (Read-U16-Phase17 $dnsQueryPayload 6) -eq 0 -and
                                   (Read-U16-Phase17 $dnsQueryPayload 8) -eq 0 -and
                                   (Read-U16-Phase17 $dnsQueryPayload 10) -eq 0 -and
                                   (Bytes-Equal16 $dnsQueryPayload 12 $expectedQuestion)) `
                                   'Managed DNS query wire fields are invalid.'
                        Write-Phase20Frame $injectionLog 'dns_query' $dnsQueryFrame

                        $dnsModes = @('wrong-id', 'truncated',
                            'pointer-out-of-range', 'pointer-loop', 'bad-rdlength')
                        foreach ($dnsMode in $dnsModes) {
                            $malformedDns = New-DnsResponse20 $dnsQueryPayload $dnsMode
                            $malformedDnsFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                                $hostIpBytes $guestIpBytes `
                                (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes `
                                    $malformedDns) 0x2E00
                            Require11 ($peerUdp.Send($malformedDnsFrame,
                                $malformedDnsFrame.Length, '127.0.0.1', $rxPort) -eq
                                $malformedDnsFrame.Length) "DNS control $dnsMode send was short."
                            Write-Phase20Frame $injectionLog ('dns_malformed_{0}' -f $dnsMode) `
                                $malformedDnsFrame
                        }
                        $wrongPortDns = New-DnsResponse20 $dnsQueryPayload 'valid'
                        $wrongPortFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 54 15200 $hostIpBytes $guestIpBytes `
                                $wrongPortDns) 0x2E06
                        Require11 ($peerUdp.Send($wrongPortFrame, $wrongPortFrame.Length,
                            '127.0.0.1', $rxPort) -eq $wrongPortFrame.Length) `
                            'DNS wrong-source-port control send was short.'
                        Write-Phase20Frame $injectionLog 'dns_wrong_source_port' $wrongPortFrame
                        $dnsResponse = New-DnsResponse20 $dnsQueryPayload 'valid'
                        $dnsResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes `
                                $dnsResponse) 0x2E01
                        Require11 ($peerUdp.Send($dnsResponseFrame, $dnsResponseFrame.Length,
                            '127.0.0.1', $rxPort) -eq $dnsResponseFrame.Length) `
                            'DNS response send was short.'
                        Write-Phase20Frame $injectionLog 'dns_response' $dnsResponseFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_RESPONSE_VALID' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_RESOLVED_IPV4=0x000000000A0F0002' `
                            $deadline $process $stream $logStream $text $buffer

                        $icmpPayload = [Text.Encoding]::ASCII.GetBytes(
                            'guideXOS Phase17 ping payload')
                        $icmpRequest = New-IcmpEcho-Phase17 8 0x2001 1 $icmpPayload
                        $icmpRequestFrame = New-Ipv4Icmp-Phase17 $hostMacBytes `
                            $guestMacBytes $guestIpBytes $hostIpBytes $icmpRequest 0x1701
                        $observedIcmp = Receive-ExpectedPhase17Frame $peerUdp `
                            $icmpRequestFrame 'resolved ICMP request' $TimeoutSeconds
                        Require11 ((Bytes-Equal16 $observedIcmp 30 $hostIpBytes)) `
                            'Resolved ICMP destination was not the DNS A record.'
                        Write-Phase20Frame $injectionLog 'resolved_icmp_request' $observedIcmp
                        $icmpReply = New-IcmpEcho-Phase17 0 0x2001 1 $icmpPayload
                        $icmpReplyFrame = New-Ipv4Icmp-Phase17 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes $icmpReply 0x2E02
                        Require11 ($peerUdp.Send($icmpReplyFrame, $icmpReplyFrame.Length,
                            '127.0.0.1', $rxPort) -eq $icmpReplyFrame.Length) `
                            'Resolved ICMP reply send was short.'
                        Write-Phase20Frame $injectionLog 'resolved_icmp_reply' $icmpReplyFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_ICMP_FIRST_REPLY_VALID' `
                            $deadline $process $stream $logStream $text $buffer

                        $udpPayload = [Text.Encoding]::ASCII.GetBytes('PHASE18-MANAGED-HELLO')
                        $udpRequest = New-UdpDatagram18 15180 15181 $guestIpBytes `
                            $hostIpBytes $udpPayload
                        $udpRequestFrame = New-Ipv4Udp18 $hostMacBytes $guestMacBytes `
                            $guestIpBytes $hostIpBytes $udpRequest 0x1901
                        $observedUdp = Receive-ExpectedPhase17Frame $peerUdp $udpRequestFrame `
                            'resolved UDP request' $TimeoutSeconds
                        Require11 ((Bytes-Equal16 $observedUdp 30 $hostIpBytes)) `
                            'Resolved UDP destination was not the DNS A record.'
                        Write-Phase20Frame $injectionLog 'resolved_udp_request' $observedUdp
                        $udpAckPayload = [Text.Encoding]::ASCII.GetBytes('PHASE18-PEER-ACK')
                        $udpAck = New-UdpDatagram18 15181 15180 $hostIpBytes $guestIpBytes `
                            $udpAckPayload
                        $udpAckFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes $udpAck 0x2E03
                        Require11 ($peerUdp.Send($udpAckFrame, $udpAckFrame.Length,
                            '127.0.0.1', $rxPort) -eq $udpAckFrame.Length) `
                            'Resolved UDP reply send was short.'
                        Write-Phase20Frame $injectionLog 'resolved_udp_reply' $udpAckFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_RESOLVED_TRAFFIC_PASS' `
                            $deadline $process $stream $logStream $text $buffer

                        $missingQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds `
                            'missing.phase20.test'
                        $missingQueryPayload = Get-DnsPayload20 $missingQueryFrame
                        Require11 ($null -ne $missingQueryPayload -and
                                   $missingQueryPayload.Length -gt 30 -and
                                   (Bytes-Equal16 $missingQueryPayload 12 `
                                       ([byte[]](7,109,105,115,115,105,110,103,7,112,104,97,115,101,50,48,4,116,101,115,116,0,0,1,0,1)))) `
                                   'NXDOMAIN query name was invalid.'
                        Write-Phase20Frame $injectionLog 'dns_nxdomain_query' $missingQueryFrame
                        $nxResponse = New-DnsResponse20 $missingQueryPayload 'nxdomain'
                        $nxFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $nxResponse) 0x2E04
                        Require11 ($peerUdp.Send($nxFrame, $nxFrame.Length,
                            '127.0.0.1', $rxPort) -eq $nxFrame.Length) `
                            'NXDOMAIN response send was short.'
                        Write-Phase20Frame $injectionLog 'dns_nxdomain_response' $nxFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_NXDOMAIN_RECEIVED' `
                            $deadline $process $stream $logStream $text $buffer

                        $recoveryQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds `
                            'phase20.test recovery'
                        $recoveryQueryPayload = Get-DnsPayload20 $recoveryQueryFrame
                        Write-Phase20Frame $injectionLog 'dns_recovery_query' $recoveryQueryFrame
                        $recoveryResponse = New-DnsResponse20 $recoveryQueryPayload 'valid'
                        $recoveryFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $recoveryResponse) 0x2E05
                        Require11 ($peerUdp.Send($recoveryFrame, $recoveryFrame.Length,
                            '127.0.0.1', $rxPort) -eq $recoveryFrame.Length) `
                            'DNS recovery response send was short.'
                        Write-Phase20Frame $injectionLog 'dns_recovery_response' $recoveryFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_RESPONSE_VALID' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_GC_SURVIVAL_PASSED' `
                            $deadline $process $stream $logStream $text $buffer

                        $postGcQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds `
                            'phase20.test post-GC'
                        $postGcQueryPayload = Get-DnsPayload20 $postGcQueryFrame
                        Write-Phase20Frame $injectionLog 'dns_post_gc_query' $postGcQueryFrame
                        $postGcResponse = New-DnsResponse20 $postGcQueryPayload 'valid'
                        $postGcResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $postGcResponse) 0x2E07
                        Require11 ($peerUdp.Send($postGcResponseFrame,
                            $postGcResponseFrame.Length, '127.0.0.1', $rxPort) -eq
                            $postGcResponseFrame.Length) 'Post-GC DNS response send was short.'
                        Write-Phase20Frame $injectionLog 'dns_post_gc_response' $postGcResponseFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_RESPONSE_VALID' `
                            $deadline $process $stream $logStream $text $buffer
                        $postGcIcmp = New-IcmpEcho-Phase17 8 0x2002 2 $icmpPayload
                        $postGcIcmpFrame = New-Ipv4Icmp-Phase17 $hostMacBytes $guestMacBytes `
                            $guestIpBytes $hostIpBytes $postGcIcmp 0x1702
                        $observedPostGcIcmp = Receive-ExpectedPhase17Frame $peerUdp `
                            $postGcIcmpFrame 'post-GC resolved ICMP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'post_gc_resolved_icmp_request' `
                            $observedPostGcIcmp
                        $postGcIcmpReply = New-IcmpEcho-Phase17 0 0x2002 2 $icmpPayload
                        $postGcIcmpReplyFrame = New-Ipv4Icmp-Phase17 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes $postGcIcmpReply 0x2E08
                        Require11 ($peerUdp.Send($postGcIcmpReplyFrame,
                            $postGcIcmpReplyFrame.Length, '127.0.0.1', $rxPort) -eq
                            $postGcIcmpReplyFrame.Length) 'Post-GC ICMP reply send was short.'
                        Write-Phase20Frame $injectionLog 'post_gc_resolved_icmp_reply' $postGcIcmpReplyFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_ICMP_POST_GC_REPLY_VALID' `
                            $deadline $process $stream $logStream $text $buffer
                        $postGcUdp = New-UdpDatagram18 15180 15181 $guestIpBytes `
                            $hostIpBytes $udpPayload
                        $postGcUdpFrame = New-Ipv4Udp18 $hostMacBytes $guestMacBytes `
                            $guestIpBytes $hostIpBytes $postGcUdp 0x1905
                        $observedPostGcUdp = Receive-ExpectedPhase17Frame $peerUdp `
                            $postGcUdpFrame 'post-GC resolved UDP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'post_gc_resolved_udp_request' $observedPostGcUdp
                        Require11 ($peerUdp.Send($udpAckFrame, $udpAckFrame.Length,
                            '127.0.0.1', $rxPort) -eq $udpAckFrame.Length) `
                            'Post-GC UDP reply send was short.'
                        Write-Phase20Frame $injectionLog 'post_gc_resolved_udp_reply' $udpAckFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_POST_GC_TRAFFIC_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_DNS_PHASE20_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'MANAGED_KERNEL_PHASE20_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        $phase15Outcome = 'PASS_PHASE20'
                    } elseif ($EnablePhase19Protocol) {
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $discoverFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds `
                            'DHCPDISCOVER'
                        $discoverPayload = Get-DhcpPayload19 $discoverFrame
                        Require11 ($null -ne $discoverPayload) 'DHCPDISCOVER is not IPv4/UDP.'
                        $discoverType = Get-DhcpOption19 $discoverPayload 53
                        Require11 ($null -ne $discoverType -and $discoverType.Length -eq 1 -and
                                   $discoverType[0] -eq 1 -and
                                   (Read-U32-Phase19 $discoverPayload 236) -eq 0x63825363 -and
                                   (Read-U16-Phase17 $discoverFrame 34) -eq 68 -and
                                   (Read-U16-Phase17 $discoverFrame 36) -eq 67 -and
                                   (Bytes-Equal16 $discoverFrame 26 ([byte[]](0,0,0,0))) -and
                                   (Bytes-Equal16 $discoverFrame 30 $broadcastIpBytes)) `
                                   'DHCPDISCOVER fields are invalid.'
                        Write-Phase19Frame $injectionLog 'dhcpdiscover' $discoverFrame
                        $xid = Read-U32-Phase19 $discoverPayload 4
                        $offerPayload = New-DhcpReply19 $discoverPayload 2 $guestIpBytes `
                            $hostIpBytes $true $true
                        $badCookiePayload = [byte[]]$offerPayload.Clone()
                        $badCookiePayload[239] = $badCookiePayload[239] -bxor 1
                        $wrongXidPayload = [byte[]]$offerPayload.Clone()
                        $wrongXidPayload[7] = $wrongXidPayload[7] -bxor 1
                        $wrongMacPayload = [byte[]]$offerPayload.Clone()
                        $wrongMacPayload[33] = $wrongMacPayload[33] -bxor 1
                        $missingTypePayload = [byte[]]$offerPayload.Clone()
                        $missingTypePayload[240] = 255
                        $badLengthPayload = [byte[]]$offerPayload.Clone()
                        $badLengthPayload[241] = 12
                        foreach ($malformedPayload in @(
                            $badCookiePayload, $wrongXidPayload, $wrongMacPayload,
                            $missingTypePayload, $badLengthPayload)) {
                            $malformedFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                                $hostIpBytes $broadcastIpBytes `
                                (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                    $malformedPayload) 0x2D00
                            $peerUdp.Send($malformedFrame, $malformedFrame.Length,
                                '127.0.0.1', $rxPort) | Out-Null
                            Write-Phase19Frame $injectionLog 'malformed_dhcp' $malformedFrame
                        }
                        $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $offerPayload) 0x2D01
                        Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length,
                            '127.0.0.1', $rxPort) -eq $offerFrame.Length) `
                            'DHCPOFFER send was short.'
                        Write-Phase19Frame $injectionLog 'dhcpoffer' $offerFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $requestFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds `
                            'DHCPREQUEST'
                        $requestPayload = Get-DhcpPayload19 $requestFrame
                        Require11 ($null -ne $requestPayload -and
                                   (Get-DhcpOption19 $requestPayload 53)[0] -eq 3 -and
                                   (Get-DhcpOption19 $requestPayload 50).Length -eq 4 -and
                                   (Read-U32-Phase19 (Get-DhcpOption19 $requestPayload 50) 0) -eq 0x0A0F002A -and
                                   (Read-U32-Phase19 (Get-DhcpOption19 $requestPayload 54) 0) -eq 0x0A0F0002 -and
                                   (Read-U16-Phase17 $requestFrame 34) -eq 68 -and
                                   (Read-U16-Phase17 $requestFrame 36) -eq 67) `
                                   'DHCPREQUEST fields are invalid.'
                        Write-Phase19Frame $injectionLog 'dhcprequest' $requestFrame
                        $ackPayload = New-DhcpReply19 $requestPayload 5 $guestIpBytes `
                            $hostIpBytes $true $true
                        $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $ackPayload) 0x2D02
                        Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length,
                            '127.0.0.1', $rxPort) -eq $ackFrame.Length) `
                            'DHCPACK send was short.'
                        Write-Phase19Frame $injectionLog 'dhcpack' $ackFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' `
                            $deadline $process $stream $logStream $text $buffer
                    }
                    if (-not $EnablePhase20Protocol -and -not $EnablePhase21Protocol) {
                    $zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
                    $guestRequest = New-Phase16ArpFrame11 `
                        $broadcastMac $guestMacBytes 1 $guestIpBytes $zeroMac $hostIpBytes
                    $hostReply = New-Phase16ArpFrame11 `
                        $guestMacBytes $hostMacBytes 2 $hostIpBytes $guestMacBytes $guestIpBytes
                    $hostRequest = New-Phase16ArpFrame11 `
                        $broadcastMac $hostMacBytes 1 $hostIpBytes $zeroMac $guestIpBytes
                    $guestReply = New-Phase16ArpFrame11 `
                        $hostMacBytes $guestMacBytes 2 $guestIpBytes $hostMacBytes $hostIpBytes
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_RESOLUTION_STARTED' `
                        $deadline $process $stream $logStream $text $buffer
                    $observedGuestRequest = Receive-ExpectedPhase16Frame11 `
                        $peerUdp $guestRequest 'ARP request' $TimeoutSeconds
                    Write-Phase16Frame11 $injectionLog 'guest_arp_request' $observedGuestRequest
                    $sentReply = $peerUdp.Send($hostReply, $hostReply.Length,
                                               '127.0.0.1', $rxPort)
                    Require11 ($sentReply -eq $hostReply.Length) `
                        'Host ARP reply send was short.'
                    Write-Phase16Frame11 $injectionLog 'host_arp_reply' $hostReply
                    Write-Timeline11 $timeline 'HOST_ARP_REPLY_SENT' `
                        "destination=$destinationMac source=021500000002"
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_REPLY_VALID' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_RESOLUTION_COMPLETE' `
                        $deadline $process $stream $logStream $text $buffer
                    $sentRequest = $peerUdp.Send($hostRequest, $hostRequest.Length,
                                                 '127.0.0.1', $rxPort)
                    Require11 ($sentRequest -eq $hostRequest.Length) `
                        'Host ARP request send was short.'
                    Write-Phase16Frame11 $injectionLog 'host_arp_request' $hostRequest
                    Write-Timeline11 $timeline 'HOST_ARP_REQUEST_FOR_GUEST_SENT' `
                        ('source=021500000002 target_ipv4={0}' -f
                            ([BitConverter]::ToString($guestIpBytes)).Replace('-', ''))
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_REQUEST_FOR_LOCAL' `
                        $deadline $process $stream $logStream $text $buffer
                    $observedGuestReply = Receive-ExpectedPhase16Frame11 `
                        $peerUdp $guestReply 'ARP reply' $TimeoutSeconds
                    Write-Phase16Frame11 $injectionLog 'guest_arp_reply' $observedGuestReply
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_REPLY_SENT' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_RESPONDER_PASS' `
                        $deadline $process $stream $logStream $text $buffer
                    }
                    if ($EnablePhase21Protocol) {
                        $phase15Outcome = 'PASS_PHASE21'
                    } elseif ($EnablePhase20Protocol) {
                        $phase15Outcome = 'PASS_PHASE20'
                    } elseif ($EnablePhase17Protocol -or $EnablePhase18Protocol -or
                        $EnablePhase19Protocol) {
                        $peerPayload = [Text.Encoding]::ASCII.GetBytes(
                            'guideXOS Phase17 ping payload')
                        $peerRequestPayload = [Text.Encoding]::ASCII.GetBytes(
                            'peer-to-guideXOS Phase17 responder')
                        $guestIpOther = [byte[]](10, 15, 0, 9)
                        $guestEchoRequest = New-IcmpEcho-Phase17 8 0x1701 1 $peerPayload
                        $guestEchoReply = New-IcmpEcho-Phase17 0 0x1701 1 $peerPayload
                        $guestRequestFrame = New-Ipv4Icmp-Phase17 `
                            $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                            $guestEchoRequest 0x1701
                        $hostReplyFrame = New-Ipv4Icmp-Phase17 `
                            $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            $guestEchoReply 0x1801
                        Wait-Marker11 'GXOS_NET10:MANAGED_IPV4_READY' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_IPV4_FIRST_PING_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $observedGuestPing = Receive-ExpectedPhase17Frame `
                            $peerUdp $guestRequestFrame 'IPv4 echo request' $TimeoutSeconds
                        Write-Phase17Frame $injectionLog 'guest_ipv4_echo_request' `
                            $observedGuestPing
                        $sentReply = $peerUdp.Send($hostReplyFrame, $hostReplyFrame.Length,
                                                   '127.0.0.1', $rxPort)
                        Require11 ($sentReply -eq $hostReplyFrame.Length) `
                            'Host IPv4 echo reply send was short.'
                        Write-Phase17Frame $injectionLog 'host_ipv4_echo_reply' $hostReplyFrame
                        Write-Timeline11 $timeline 'HOST_IPV4_ECHO_REPLY_SENT' `
                            'identifier=1701 sequence=0001'
                        Wait-Marker11 'GXOS_NET10:MANAGED_ICMP_FIRST_REPLY_VALID' `
                            $deadline $process $stream $logStream $text $buffer

                        Wait-Marker11 'GXOS_NET10:MANAGED_IPV4_MALFORMED_READY' `
                            $deadline $process $stream $logStream $text $buffer
                        $badHeaderFrame = New-Ipv4Icmp-Phase17 `
                            $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            (New-IcmpEcho-Phase17 8 0x9001 1 ([byte[]](1, 2, 3))) 0x9001
                        $badHeaderFrame[24] = $badHeaderFrame[24] -bxor 1
                        $impossibleLengthFrame = New-Ipv4Icmp-Phase17 `
                            $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            (New-IcmpEcho-Phase17 8 0x9002 2 ([byte[]](4, 5, 6))) 0x9002
                        $impossibleLengthFrame[16] = 0x05
                        $impossibleLengthFrame[17] = 0xDC
                        $fragmentedFrame = New-Ipv4Icmp-Phase17 `
                            $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            (New-IcmpEcho-Phase17 8 0x9003 3 ([byte[]](7, 8, 9))) 0x9003 0x2000
                        $invalidCodeFrame = New-Ipv4Icmp-Phase17 `
                            $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            (New-IcmpEcho-Phase17 8 0x9004 4 ([byte[]](10, 11, 12)) 1) 0x9004
                        $wrongDestinationFrame = New-Ipv4Icmp-Phase17 `
                            $broadcastMac $hostMacBytes $hostIpBytes $guestIpOther `
                            (New-IcmpEcho-Phase17 8 0x9005 5 ([byte[]](13, 14, 15))) 0x9005
                        $malformedFrames = @(
                            $badHeaderFrame, $impossibleLengthFrame,
                            $fragmentedFrame, $invalidCodeFrame, $wrongDestinationFrame)
                        $malformedIndex = 0
                        foreach ($malformed in $malformedFrames) {
                            $sentMalformed = $peerUdp.Send($malformed, $malformed.Length,
                                                           '127.0.0.1', $rxPort)
                            Require11 ($sentMalformed -eq $malformed.Length) `
                                "Malformed IPv4 frame $malformedIndex send was short."
                            Write-Phase17Frame $injectionLog `
                                ('malformed_{0}' -f $malformedIndex) $malformed
                            Wait-Marker11 ('GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_{0}' -f $malformedIndex) `
                                $deadline $process $stream $logStream $text $buffer
                            $malformedIndex++
                        }
                        Write-Timeline11 $timeline 'HOST_MALFORMED_IPV4_CONTROLS_SENT' `
                            'count=5 bad_header=1 impossible_length=1 fragmented=1 invalid_icmp_code=1 wrong_destination=1'
                        Wait-Marker11 'GXOS_NET10:MANAGED_IPV4_MALFORMED_CONTROLS_PASS' `
                            $deadline $process $stream $logStream $text $buffer

                        $peerRequest = New-IcmpEcho-Phase17 8 0xBEEF 7 $peerRequestPayload
                        $peerReply = New-IcmpEcho-Phase17 0 0xBEEF 7 $peerRequestPayload
                        $peerRequestFrame = New-Ipv4Icmp-Phase17 `
                            $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            $peerRequest 0x1807
                        $peerReplyFrame = New-Ipv4Icmp-Phase17 `
                            $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                            $peerReply 0x1807
                        $sentPeerRequest = $peerUdp.Send($peerRequestFrame,
                                                         $peerRequestFrame.Length,
                                                         '127.0.0.1', $rxPort)
                        Require11 ($sentPeerRequest -eq $peerRequestFrame.Length) `
                            'Peer IPv4 echo request send was short.'
                        Write-Phase17Frame $injectionLog 'host_ipv4_echo_request' $peerRequestFrame
                        Write-Timeline11 $timeline 'HOST_IPV4_ECHO_REQUEST_SENT' `
                            'identifier=BEEF sequence=0007'
                        $observedPeerReply = Receive-ExpectedPhase17Frame `
                            $peerUdp $peerReplyFrame 'managed IPv4 echo reply' $TimeoutSeconds
                        Write-Phase17Frame $injectionLog 'guest_ipv4_echo_reply' $observedPeerReply
                        Wait-Marker11 'GXOS_NET10:MANAGED_ICMP_RESPONDER_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE17_GC_SURVIVAL_PASSED' `
                            $deadline $process $stream $logStream $text $buffer

                        $postRequest = New-IcmpEcho-Phase17 8 0x1702 2 $peerPayload
                        $postReply = New-IcmpEcho-Phase17 0 0x1702 2 $peerPayload
                        $postRequestFrame = New-Ipv4Icmp-Phase17 `
                            $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                            $postRequest 0x1702
                        $postReplyFrame = New-Ipv4Icmp-Phase17 `
                            $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                            $postReply 0x1802
                        Wait-Marker11 'GXOS_NET10:MANAGED_IPV4_POST_GC_PING_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $observedPostRequest = Receive-ExpectedPhase17Frame `
                            $peerUdp $postRequestFrame 'post-GC IPv4 echo request' $TimeoutSeconds
                        Write-Phase17Frame $injectionLog 'guest_post_gc_echo_request' `
                            $observedPostRequest
                        $sentPostReply = $peerUdp.Send($postReplyFrame, $postReplyFrame.Length,
                                                       '127.0.0.1', $rxPort)
                        Require11 ($sentPostReply -eq $postReplyFrame.Length) `
                            'Post-GC IPv4 echo reply send was short.'
                        Write-Phase17Frame $injectionLog 'host_post_gc_echo_reply' $postReplyFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_ICMP_POST_GC_REPLY_VALID' `
                            $deadline $process $stream $logStream $text $buffer
                        if ($EnablePhase18Protocol -or $EnablePhase19Protocol) {
                            Wait-Marker11 'GXOS_NET10:MANAGED_IPV4_POST_GC_EXCHANGE_PASS' `
                                $deadline $process $stream $logStream $text $buffer
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_READY' `
                                $deadline $process $stream $logStream $text $buffer

                            $udpManagedPayload = [Text.Encoding]::ASCII.GetBytes(
                                'PHASE18-MANAGED-HELLO')
                            $udpPeerAckPayload = [Text.Encoding]::ASCII.GetBytes(
                                'PHASE18-PEER-ACK')
                            $udpPeerRequestPayload = [Text.Encoding]::ASCII.GetBytes(
                                'PHASE18-PEER-HELLO')
                            $udpManagedAckPayload = [Text.Encoding]::ASCII.GetBytes(
                                'PHASE18-MANAGED-ACK')
                            $managedUdpRequest = New-UdpDatagram18 `
                                15180 15181 $guestIpBytes $hostIpBytes $udpManagedPayload
                            $peerUdpResponse = New-UdpDatagram18 `
                                15181 15180 $hostIpBytes $guestIpBytes $udpPeerAckPayload
                            $managedUdpRequestFrame = New-Ipv4Udp18 `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                $managedUdpRequest 0x1900
                            $peerUdpResponseFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $peerUdpResponse 0x1902

                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_MANAGED_REQUEST_SENT' `
                                $deadline $process $stream $logStream $text $buffer
                            $observedManagedUdpRequest = Receive-ExpectedPhase17Frame `
                                $peerUdp $managedUdpRequestFrame 'managed UDP request' $TimeoutSeconds
                            Require11 (Test-UdpFrame18 $observedManagedUdpRequest `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                15180 15181 $udpManagedPayload) `
                                'Managed UDP request failed independent peer validation.'
                            Write-Phase18Frame $injectionLog 'guest_udp_request' `
                                $observedManagedUdpRequest
                            $sentPeerUdpResponse = $peerUdp.Send($peerUdpResponseFrame,
                                $peerUdpResponseFrame.Length, '127.0.0.1', $rxPort)
                            Require11 ($sentPeerUdpResponse -eq $peerUdpResponseFrame.Length) `
                                'Peer UDP response send was short.'
                            Require11 (Test-UdpFrame18 $peerUdpResponseFrame `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                15181 15180 $udpPeerAckPayload) `
                                'Peer UDP response failed independent validation.'
                            Write-Phase18Frame $injectionLog 'host_udp_response' $peerUdpResponseFrame
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_MANAGED_RESPONSE_VALID' `
                                $deadline $process $stream $logStream $text $buffer
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_MANAGED_EXCHANGE_PASS' `
                                $deadline $process $stream $logStream $text $buffer

                            $peerUdpRequest = New-UdpDatagram18 `
                                15181 15180 $hostIpBytes $guestIpBytes $udpPeerRequestPayload
                            $managedUdpAck = New-UdpDatagram18 `
                                15180 15181 $guestIpBytes $hostIpBytes $udpManagedAckPayload
                            $peerUdpRequestFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $peerUdpRequest 0x1903
                            $managedUdpAckFrame = New-Ipv4Udp18 `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                $managedUdpAck 0x1901
                            $managedUdpZeroAckFrame = New-Ipv4Udp18 `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                $managedUdpAck 0x1902
                            $sentPeerUdpRequest = $peerUdp.Send($peerUdpRequestFrame,
                                $peerUdpRequestFrame.Length, '127.0.0.1', $rxPort)
                            Require11 ($sentPeerUdpRequest -eq $peerUdpRequestFrame.Length) `
                                'Peer-originated UDP request send was short.'
                            Write-Phase18Frame $injectionLog 'host_udp_request' $peerUdpRequestFrame
                            $observedManagedUdpAck = Receive-ExpectedPhase17Frame `
                                $peerUdp $managedUdpAckFrame 'managed UDP endpoint response' $TimeoutSeconds
                            Require11 (Test-UdpFrame18 $observedManagedUdpAck `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                15180 15181 $udpManagedAckPayload) `
                                'Managed UDP endpoint response failed validation.'
                            Write-Phase18Frame $injectionLog 'guest_udp_endpoint_response' `
                                $observedManagedUdpAck
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_PEER_RESPONSE_SENT' `
                                $deadline $process $stream $logStream $text $buffer

                            $zeroUdpRequest = New-UdpDatagram18 `
                                15181 15180 $hostIpBytes $guestIpBytes $udpPeerRequestPayload $true
                            $zeroUdpRequestFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $zeroUdpRequest 0x1905
                            $sentZeroUdpRequest = $peerUdp.Send($zeroUdpRequestFrame,
                                $zeroUdpRequestFrame.Length, '127.0.0.1', $rxPort)
                            Require11 ($sentZeroUdpRequest -eq $zeroUdpRequestFrame.Length) `
                                'Zero-checksum UDP request send was short.'
                            Write-Phase18Frame $injectionLog 'host_udp_zero_checksum_request' `
                                $zeroUdpRequestFrame
                            $observedZeroUdpAck = Receive-ExpectedPhase17Frame `
                                $peerUdp $managedUdpZeroAckFrame 'zero-checksum UDP response' $TimeoutSeconds
                            Require11 (Test-UdpFrame18 $observedZeroUdpAck `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                15180 15181 $udpManagedAckPayload) `
                                'Zero-checksum UDP response failed validation.'
                            Write-Phase18Frame $injectionLog 'guest_udp_zero_checksum_response' `
                                $observedZeroUdpAck
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_ZERO_CHECKSUM_ACCEPTED' `
                                $deadline $process $stream $logStream $text $buffer
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_ZERO_CHECKSUM_RESPONSE_SENT' `
                                $deadline $process $stream $logStream $text $buffer

                            $zeroSourceUdp = New-UdpDatagram18 `
                                0 15180 $hostIpBytes $guestIpBytes @()
                            $zeroSourceFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $zeroSourceUdp 0x9201
                            $zeroDestinationUdp = New-UdpDatagram18 `
                                15181 0 $hostIpBytes $guestIpBytes @()
                            $zeroDestinationFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $zeroDestinationUdp 0x9202
                            $invalidPayloadUdp = New-UdpDatagram18 `
                                15181 15180 $hostIpBytes $guestIpBytes ([byte[]](1, 2, 3))
                            $invalidPayloadFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $invalidPayloadUdp 0x9203
                            $unknownUdp = New-UdpDatagram18 `
                                15181 15182 $hostIpBytes $guestIpBytes ([byte[]](4, 5))
                            $unknownUdpFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $unknownUdp 0x9204
                            $oversizedPayload = New-Object byte[] 513
                            for ($index = 0; $index -lt $oversizedPayload.Length; $index++) {
                                $oversizedPayload[$index] = [byte](($index + 1) -band 0xFF)
                            }
                            $oversizedUdp = New-UdpDatagram18 `
                                15181 15180 $hostIpBytes $guestIpBytes $oversizedPayload
                            $oversizedUdpFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $oversizedUdp 0x9205
                            $udpMalformedFrames = @(
                                $zeroSourceFrame, $zeroDestinationFrame, $invalidPayloadFrame,
                                $unknownUdpFrame, $oversizedUdpFrame)
                            for ($index = 0; $index -lt $udpMalformedFrames.Count; $index++) {
                                $malformedUdp = $udpMalformedFrames[$index]
                                $sentMalformedUdp = $peerUdp.Send($malformedUdp,
                                    $malformedUdp.Length, '127.0.0.1', $rxPort)
                                Require11 ($sentMalformedUdp -eq $malformedUdp.Length) `
                                    "Malformed UDP frame $index send was short."
                                Write-Phase18Frame $injectionLog `
                                    ('malformed_udp_{0}' -f $index) $malformedUdp
                                Wait-Marker11 `
                                    ('GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_{0}' -f $index) `
                                    $deadline $process $stream $logStream $text $buffer
                            }
                            Write-Timeline11 $timeline 'HOST_MALFORMED_UDP_CONTROLS_SENT' `
                                'count=5 zero_source_port=1 zero_destination_port=1 invalid_payload=1 unknown_port=1 oversized_payload=1'
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_MALFORMED_CONTROLS_PASS' `
                                $deadline $process $stream $logStream $text $buffer

                            $postMalformedFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $peerUdpRequest 0x9301
                            $managedUdpPostMalformedAckFrame = New-Ipv4Udp18 `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                $managedUdpAck 0x1903
                            $sentPostMalformed = $peerUdp.Send($postMalformedFrame,
                                $postMalformedFrame.Length, '127.0.0.1', $rxPort)
                            Require11 ($sentPostMalformed -eq $postMalformedFrame.Length) `
                                'Post-malformed UDP request send was short.'
                            Write-Phase18Frame $injectionLog 'host_udp_post_malformed_request' `
                                $postMalformedFrame
                            $observedPostMalformed = Receive-ExpectedPhase17Frame `
                                $peerUdp $managedUdpPostMalformedAckFrame 'post-malformed UDP response' $TimeoutSeconds
                            Require11 (Test-UdpFrame18 $observedPostMalformed `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                15180 15181 $udpManagedAckPayload) `
                                'Post-malformed UDP response failed validation.'
                            Write-Phase18Frame $injectionLog 'guest_udp_post_malformed_response' `
                                $observedPostMalformed
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_POST_MALFORMED_RESPONSE_SENT' `
                                $deadline $process $stream $logStream $text $buffer
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_GC_SURVIVAL_PASSED' `
                                $deadline $process $stream $logStream $text $buffer

                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_POST_GC_REQUEST_SENT' `
                                $deadline $process $stream $logStream $text $buffer
                            $managedUdpPostGcRequestFrame = New-Ipv4Udp18 `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                $managedUdpRequest 0x1904
                            $managedUdpPostGcAckFrame = New-Ipv4Udp18 `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                $managedUdpAck 0x1905
                            $observedPostGcManaged = Receive-ExpectedPhase17Frame `
                                $peerUdp $managedUdpPostGcRequestFrame 'post-GC managed UDP request' $TimeoutSeconds
                            Require11 (Test-UdpFrame18 $observedPostGcManaged `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                15180 15181 $udpManagedPayload) `
                                'Post-GC managed UDP request failed validation.'
                            Write-Phase18Frame $injectionLog 'guest_udp_post_gc_request' `
                                $observedPostGcManaged
                            $sentPostGcPeerResponse = $peerUdp.Send($peerUdpResponseFrame,
                                $peerUdpResponseFrame.Length, '127.0.0.1', $rxPort)
                            Require11 ($sentPostGcPeerResponse -eq $peerUdpResponseFrame.Length) `
                                'Post-GC peer UDP response send was short.'
                            Write-Phase18Frame $injectionLog 'host_udp_post_gc_response' `
                                $peerUdpResponseFrame
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_POST_GC_RESPONSE_VALID' `
                                $deadline $process $stream $logStream $text $buffer

                            $postGcPeerRequestFrame = New-Ipv4Udp18 `
                                $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
                                $peerUdpRequest 0x9302
                            $sentPostGcPeerRequest = $peerUdp.Send($postGcPeerRequestFrame,
                                $postGcPeerRequestFrame.Length, '127.0.0.1', $rxPort)
                            Require11 ($sentPostGcPeerRequest -eq $postGcPeerRequestFrame.Length) `
                                'Post-GC peer-originated UDP request send was short.'
                            Write-Phase18Frame $injectionLog 'host_udp_post_gc_request' `
                                $postGcPeerRequestFrame
                            $observedPostGcPeerResponse = Receive-ExpectedPhase17Frame `
                                $peerUdp $managedUdpPostGcAckFrame 'post-GC UDP endpoint response' $TimeoutSeconds
                            Require11 (Test-UdpFrame18 $observedPostGcPeerResponse `
                                $hostMacBytes $guestMacBytes $guestIpBytes $hostIpBytes `
                                15180 15181 $udpManagedAckPayload) `
                                'Post-GC UDP endpoint response failed validation.'
                            Write-Phase18Frame $injectionLog 'guest_udp_post_gc_response' `
                                $observedPostGcPeerResponse
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_POST_GC_PEER_RESPONSE_SENT' `
                                $deadline $process $stream $logStream $text $buffer
                            Wait-Marker11 'GXOS_NET10:MANAGED_UDP_POST_GC_EXCHANGE_PASS' `
                                $deadline $process $stream $logStream $text $buffer
                            if ($EnablePhase19Protocol) {
                                Wait-Marker11 'MANAGED_KERNEL_PHASE19_PASS' `
                                    $deadline $process $stream $logStream $text $buffer
                                $phase15Outcome = 'PASS_PHASE19'
                            } else {
                                Wait-Marker11 'MANAGED_KERNEL_PHASE18_PASS' `
                                    $deadline $process $stream $logStream $text $buffer
                                $phase15Outcome = 'PASS_PHASE18'
                            }
                        } else {
                            Wait-Marker11 'MANAGED_KERNEL_PHASE17_PASS' `
                                $deadline $process $stream $logStream $text $buffer
                            $phase15Outcome = 'PASS_PHASE17'
                        }
                        Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                    }
                    else {
                        Wait-Marker11 'MANAGED_KERNEL_PHASE16_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        $phase15Outcome = 'PASS_PHASE16'
                    }
                } else {
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_COMPLETE' $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_FRAME_OK' $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'MANAGED_KERNEL_PHASE15_PASS' $deadline $process $stream $logStream $text $buffer
                    $phase15Outcome = 'PASS'
                }
            }
            Pump-Serial11 $stream $logStream $text $buffer
            $finalText = $text.ToString()
        } finally {
            if ($null -ne $injectionLog) { $injectionLog.Dispose() }
            if ($null -ne $logStream) { $logStream.Dispose() }
            if ($null -ne $stream) { $stream.Dispose() }
            if ($null -ne $client) { $client.Dispose() }
            if ($null -ne $peerUdp) { $peerUdp.Dispose() }
            if ($null -ne $monitor) { $monitor.Dispose() }
            Stop-OwnedQemu11 $process
            if ($null -ne $timeline) { $timeline.Dispose() }
            $script:phase11Timeline = $null
        }
        if ($Phase15EnableFilterDump) {
            Require11 (Test-Path -LiteralPath $pcapPath) `
                "QEMU filter-dump did not create the PCAP for boot $sequence."
        }
        if ($Phase15EnableQemuReceiveTrace) {
            Require11 (Test-Path -LiteralPath $tracePath) `
                "QEMU receive tracing did not create the trace for boot $sequence."
        }
        Require11 ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) "Payload hash changed on boot $sequence."
        Require11 ((!$finalText.Contains('GXOS_NET10:FAIL:') -and !$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:'))) "Boot $sequence reported a fault."
        foreach ($marker in $requiredMarkers) { Require11 $finalText.Contains($marker) "Boot $sequence missing marker: $marker" }
        Require11 (([regex]::Matches($finalText, 'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS')).Count -eq 1) 'Phase 11 pass marker count was not one.'
        Require11 ($finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ENQUEUED_COUNT=0x0000000000000009') -and $finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DRAINED_COUNT=0x0000000000000009') -and $finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DROPPED_COUNT=0x0000000000000000')) "Boot $sequence did not account for nine shared events without drops."
        $serialHash = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant()
        $injectionHash = (Get-FileHash -LiteralPath $injections -Algorithm SHA256).Hash.ToUpperInvariant()
        $timelineHash = (Get-FileHash -LiteralPath $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
        Write-Output ("MANAGED_KERNEL_PHASE11_QEMU_RUN_{0}=PASS outcome={1} bytes={2} serial_sha256={3} injections_sha256={4} timeline_sha256={5} serial={6}" -f $sequence, $phase15Outcome, ([Text.Encoding]::ASCII.GetByteCount($finalText)), $serialHash, $injectionHash, $timelineHash, $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu11 $process }
}
Require11 (@(Get-OwnedQemu11).Count -eq 0) 'Owned QEMU cleanup failed.'
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE11_QEMU_RUNS=$RunCount"

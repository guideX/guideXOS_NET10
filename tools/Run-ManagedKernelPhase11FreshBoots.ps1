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
    [switch]$EnablePhase22Protocol,
    [switch]$EnablePhase23Protocol,
    [switch]$EnablePhase32Protocol,
    [switch]$EnablePhase33NegativeControl,
    [switch]$EnablePhase33Protocol,
    [switch]$EnablePhase34NegativeControl,
    [switch]$EnablePhase34Protocol,
    [switch]$EnablePhase39Protocol,
    [switch]$EnableManagedKernelPhase35,
    [switch]$EnablePhase32NegativeControl,
    [switch]$Phase15EnableFilterDump,
    [switch]$Phase15EnableQemuReceiveTrace,
    [switch]$EnablePhase26VirtioRng,
    [ValidateSet('all', 'rx', 'tx')]
    [string]$Phase15FilterDumpQueue = 'tx'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($EnablePhase32NegativeControl -and -not $EnablePhase32Protocol) {
    throw '-EnablePhase32NegativeControl requires -EnablePhase32Protocol.'
}
if ($EnablePhase33NegativeControl -and -not $EnablePhase33Protocol) {
    throw '-EnablePhase33NegativeControl requires -EnablePhase33Protocol.'
}
if ($EnablePhase34NegativeControl -and -not $EnablePhase34Protocol) {
    throw '-EnablePhase34NegativeControl requires -EnablePhase34Protocol.'
}
if ($EnablePhase39Protocol -and
    (!$EnablePhase15Rx -or $Phase15NetworkBackend -ne 'dgram')) {
    throw '-EnablePhase39Protocol requires -EnablePhase15Rx -Phase15NetworkBackend dgram.'
}
if ($EnableManagedKernelPhase35 -and
    (!$EnablePhase15Rx -or $Phase15NetworkBackend -ne 'user')) {
    throw '-EnableManagedKernelPhase35 requires -EnablePhase15Rx -Phase15NetworkBackend user.'
}
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
    try {
        return @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
            Where-Object {
                $commandLine = [string]$_.CommandLine
                $scope | Where-Object {
                    $commandLine.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
                } | Select-Object -First 1
            })
    } catch {
        # Restricted Windows runners may deny process command-line inspection.
        # Treat any visible QEMU process as owned so a concurrent boot cannot
        # be mistaken for a clean fixture.
        return @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue)
    }
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
    while ($stream.DataAvailable) {
        $count = $stream.Read($buffer, 0, $buffer.Length)
        if ($count -le 0) { return }
        $logStream.Write($buffer, 0, $count)
        $logStream.Flush()
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

function Wait-Phase35Outcome11([datetime]$deadline,
                                [System.Diagnostics.Process]$process,
                                [System.IO.Stream]$stream, [IO.FileStream]$logStream,
                                [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ((Get-Date) -lt $deadline) {
        Pump-Serial11 $stream $logStream $text $buffer
        $transcript = $text.ToString()
        if ($transcript.Contains('GXOS_NET10:PUBLIC_HTTPS_COMPLETE')) {
            Write-Timeline11 $script:phase11Timeline 'GUEST_MARKER' `
                'marker=GXOS_NET10:PUBLIC_HTTPS_COMPLETE outcome=A'
            return 'A'
        }
        if ($transcript.Contains('GXOS_NET10:PUBLIC_HTTPS_OUTCOME=B')) {
            Write-Timeline11 $script:phase11Timeline 'GUEST_MARKER' `
                'marker=GXOS_NET10:PUBLIC_HTTPS_OUTCOME=B outcome=B'
            return 'B'
        }
        if ($transcript.Contains('GXOS_NET10:PUBLIC_HTTPS_OUTCOME=C')) {
            return 'C'
        }
        if ($transcript.Contains('GXOS_NET10:PUBLIC_HTTPS_OUTCOME=D')) {
            return 'D'
        }
        if ($transcript.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
            $transcript.Contains('GXOS_NET10:PAGE_FAULT_') -or
            $transcript.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
            throw 'QEMU reported a machine fault while waiting for the Phase 35 outcome.'
        }
        if ($process.HasExited) {
            throw 'QEMU exited while waiting for the Phase 35 outcome.'
        }
        Start-Sleep -Milliseconds 25
    }
    throw 'Timed out waiting for the Phase 35 public HTTPS outcome.'
}

function Wait-Phase32NegativeOutcome11([datetime]$deadline,
                                        [System.Diagnostics.Process]$process,
                                        [System.IO.Stream]$stream,
                                        [IO.FileStream]$logStream,
                                        [Text.StringBuilder]$text,
                                        [byte[]]$buffer) {
    while ((Get-Date) -lt $deadline) {
        Pump-Serial11 $stream $logStream $text $buffer
        if ($text.ToString().Contains(
                'GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START')) {
            Write-Timeline11 $script:phase11Timeline 'GUEST_MARKER' `
                'marker=GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START outcome=EXPECTED_FAILURE'
            return
        }
        if ($process.HasExited) {
            throw 'QEMU exited before the Phase 32 negative control outcome.'
        }
        Start-Sleep -Milliseconds 25
    }
    throw 'Timed out waiting for the Phase 32 negative control outcome.'
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
                             [bool]$phase20 = $false, [bool]$phase21 = $false,
                             [bool]$phase22 = $false,
                              [bool]$phase23 = $false,
                              [bool]$phase32 = $false,
                              [bool]$phase33 = $false,
                              [bool]$phase34 = $false) {
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
    $sequence = if ($phase34) { 0x34000001 } `
        elseif ($phase33) { 0x33000001 } `
        elseif ($phase32) { 0x32000001 } `
        elseif ($phase23) { 0x23000001 } `
        elseif ($phase22) { 0x22000001 } `
        elseif ($phase21) { 0x21000001 } `
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
                                     [bool]$phase21 = $false,
                                      [bool]$phase22 = $false,
                                      [bool]$phase23 = $false,
                                      [bool]$phase32 = $false,
                                      [bool]$phase33 = $false,
                                      [bool]$phase34 = $false) {
    $frame = New-Phase15Frame11 $destinationMac $phase17 $phase18 $phase19 $phase20 $phase21 $phase22 $phase23 $phase32 $phase33 $phase34
    $sent = $peerUdp.Send($frame, $frame.Length, $destinationHost, $destinationPort)
    Require11 ($sent -eq $frame.Length) 'Phase 15 UDP injector sent a short Ethernet datagram.'
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $frameHash = ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '') }
    finally { $hash.Dispose() }
    $marker = if ($phase34) { 'MANAGED_E1000_PHASE34_INJECTED' } `
        elseif ($phase33) { 'MANAGED_E1000_PHASE33_INJECTED' } `
        elseif ($phase32) { 'MANAGED_E1000_PHASE32_INJECTED' } `
        elseif ($phase23) { 'MANAGED_E1000_PHASE23_INJECTED' } `
        elseif ($phase22) { 'MANAGED_E1000_PHASE22_INJECTED' } `
        elseif ($phase21) { 'MANAGED_E1000_PHASE21_INJECTED' } `
        elseif ($phase20) { 'MANAGED_E1000_PHASE20_INJECTED' } `
        elseif ($phase19) { 'MANAGED_E1000_PHASE19_INJECTED' } `
        elseif ($phase18) { 'MANAGED_E1000_PHASE18_INJECTED' } `
        elseif ($phase17) { 'MANAGED_E1000_PHASE17_INJECTED' } `
        else { 'MANAGED_E1000_PHASE15_INJECTED' }
    $sequenceText = if ($phase34) { '0x34000001' } `
        elseif ($phase33) { '0x33000001' } `
        elseif ($phase32) { '0x32000001' } `
        elseif ($phase23) { '0x23000001' } `
        elseif ($phase22) { '0x22000001' } `
        elseif ($phase21) { '0x21000001' } `
        elseif ($phase20) { '0x20000001' } `
        elseif ($phase19) { '0x19000001' } `
        elseif ($phase18) { '0x18000001' } `
        elseif ($phase17) { '0x17000001' } else { '0x15000001' }
    return ('{0}=PASS transport=dgram length={1} destination={2} source=021500000001 ethertype=88B5 sequence={3} frame_sha256={4} udp_source_port={5} udp_destination_port={6}' -f `
        $marker, $frame.Length, $destinationMac.ToUpperInvariant(), $sequenceText,
        $frameHash, ([Net.IPEndPoint]$peerUdp.Client.LocalEndPoint).Port,
        $destinationPort)
}

$script:phase32FixtureCache = @{}

function Get-Phase32FixtureBytes11([string]$name) {
    if (!$script:phase32FixtureCache.ContainsKey($name)) {
        $fixturePath = Join-Path $PSScriptRoot '..\src\ManagedKernel\ManagedTls12Phase31Fixtures.cs'
        $fixtureText = Get-Content -LiteralPath $fixturePath -Raw
        $pattern = 'internal static readonly byte\[\] ' +
            [regex]::Escape($name) + '\s*=\s*\{(?<body>.*?)\};'
        $match = [regex]::Match($fixtureText, $pattern,
            [Text.RegularExpressions.RegexOptions]::Singleline)
        Require11 $match.Success "Phase 32 fixture array was not found: $name"
        $values = [regex]::Matches($match.Groups['body'].Value,
            '0x([0-9A-Fa-f]+)') | ForEach-Object {
            [Convert]::ToByte($_.Groups[1].Value, 16)
        }
        $script:phase32FixtureCache[$name] = [byte[]]$values
    }
    return ,([byte[]]$script:phase32FixtureCache[$name].Clone())
}

function New-Phase32PlainTlsRecord11([byte]$type, [byte[]]$plaintext) {
    $record = New-Object byte[] (5 + $plaintext.Length)
    $record[0] = $type; $record[1] = 3; $record[2] = 3
    Write-U16-Phase17 $record 3 $plaintext.Length
    [Array]::Copy($plaintext, 0, $record, 5, $plaintext.Length)
    return ,$record
}

function New-Phase32ApplicationRecord11([UInt64]$sequence,
                                         [byte[]]$plaintext,
                                         [byte[]]$key, [byte[]]$fixedIv) {
    $nonce = New-Object byte[] 12
    [Array]::Copy($fixedIv, 0, $nonce, 0, 4)
    for ($index = 0; $index -lt 8; ++$index) {
        $nonce[4 + $index] = [byte](($sequence -shr (56 - 8 * $index)) -band 0xFF)
    }
    $aad = New-Object byte[] 13
    for ($index = 0; $index -lt 8; ++$index) {
        $aad[$index] = [byte](($sequence -shr (56 - 8 * $index)) -band 0xFF)
    }
    $aad[8] = 23; $aad[9] = 3; $aad[10] = 3
    Write-U16-Phase17 $aad 11 $plaintext.Length
    $ciphertext = New-Object byte[] $plaintext.Length
    $tag = New-Object byte[] 16
    $aes = [Security.Cryptography.AesGcm]::new($key, 16)
    try { $aes.Encrypt($nonce, $plaintext, $ciphertext, $tag, $aad) }
    finally { $aes.Dispose() }
    $record = New-Object byte[] (5 + 8 + $ciphertext.Length + $tag.Length)
    $record[0] = 23; $record[1] = 3; $record[2] = 3
    Write-U16-Phase17 $record 3 (8 + $ciphertext.Length + $tag.Length)
    [Array]::Copy($nonce, 4, $record, 5, 8)
    [Array]::Copy($ciphertext, 0, $record, 13, $ciphertext.Length)
    [Array]::Copy($tag, 0, $record, 13 + $ciphertext.Length, $tag.Length)
    return ,$record
}

function New-Phase34TlsRecord11([UInt64]$sequence, [byte]$type,
                                 [byte[]]$plaintext, [byte[]]$key,
                                 [byte[]]$fixedIv) {
    $nonce = New-Object byte[] 12
    [Array]::Copy($fixedIv, 0, $nonce, 0, 4)
    for ($index = 0; $index -lt 8; ++$index) {
        $nonce[4 + $index] = [byte](($sequence -shr (56 - 8 * $index)) -band 0xFF)
    }
    $aad = New-Object byte[] 13
    for ($index = 0; $index -lt 8; ++$index) {
        $aad[$index] = [byte](($sequence -shr (56 - 8 * $index)) -band 0xFF)
    }
    $aad[8] = $type; $aad[9] = 3; $aad[10] = 3
    Write-U16-Phase17 $aad 11 $plaintext.Length
    $ciphertext = New-Object byte[] $plaintext.Length
    $tag = New-Object byte[] 16
    $aes = [Security.Cryptography.AesGcm]::new($key, 16)
    try { $aes.Encrypt($nonce, $plaintext, $ciphertext, $tag, $aad) }
    finally { $aes.Dispose() }
    $record = New-Object byte[] (5 + 8 + $ciphertext.Length + $tag.Length)
    $record[0] = $type; $record[1] = 3; $record[2] = 3
    Write-U16-Phase17 $record 3 (8 + $ciphertext.Length + $tag.Length)
    [Array]::Copy($nonce, 4, $record, 5, 8)
    [Array]::Copy($ciphertext, 0, $record, 13, $ciphertext.Length)
    [Array]::Copy($tag, 0, $record, 13 + $ciphertext.Length, $tag.Length)
    return ,$record
}

function Invoke-Phase34Prf11([byte[]]$secret, [byte[]]$label,
                              [byte[]]$seed, [int]$length) {
    $labelSeed = [byte[]]($label + $seed)
    $result = New-Object byte[] $length
    $a = $labelSeed
    $offset = 0
    while ($offset -lt $length) {
        $hmacA = [Security.Cryptography.HMACSHA256]::new($secret)
        try { $a = $hmacA.ComputeHash($a) } finally { $hmacA.Dispose() }
        $hmacBlock = [Security.Cryptography.HMACSHA256]::new($secret)
        try { $block = $hmacBlock.ComputeHash([byte[]]($a + $labelSeed)) }
        finally { $hmacBlock.Dispose() }
        $count = [Math]::Min($block.Length, $length - $offset)
        [Array]::Copy($block, 0, $result, $offset, $count)
        $offset += $count
    }
    return ,$result
}

function New-Phase34DynamicServerFlight11([byte[]]$clientHello,
                                            [byte[]]$clientFlight) {
    Require11 ($clientHello.Length -ge 38 -and $clientFlight.Length -ge 86) `
        'Phase 34 dynamic TLS input was truncated.'
    $clientKeyExchangeLength = Read-U16-Phase17 $clientFlight 3
    Require11 ($clientKeyExchangeLength -eq 70 -and
        $clientFlight.Length -ge 5 + $clientKeyExchangeLength + 6 + 5) `
        'Phase 34 client TLS flight shape was invalid.'
    $transcript = [byte[]]($clientHello +
        (Get-Phase32FixtureBytes11 'ServerHello') +
        (Get-Phase32FixtureBytes11 'CertificateMessage') +
        (Get-Phase32FixtureBytes11 'ServerKeyExchange') +
        (Get-Phase32FixtureBytes11 'ServerHelloDone') +
        [byte[]]$clientFlight[5..(4 + $clientKeyExchangeLength)])
    $sessionHash = [Security.Cryptography.SHA256]::HashData($transcript)
    $masterSecret = Invoke-Phase34Prf11 `
        (Get-Phase32FixtureBytes11 'PremasterSecret') `
        ([Text.Encoding]::ASCII.GetBytes('extended master secret')) $sessionHash 48
    $serverRandom = Get-Phase32FixtureBytes11 'ServerRandom'
    $clientRandom = [byte[]]$clientHello[6..37]
    $keySeed = [byte[]]($serverRandom + $clientRandom)
    $keyBlock = Invoke-Phase34Prf11 $masterSecret `
        ([Text.Encoding]::ASCII.GetBytes('key expansion')) $keySeed 40

    $encryptedOffset = 5 + $clientKeyExchangeLength + 6
    $encryptedLength = Read-U16-Phase17 $clientFlight ($encryptedOffset + 3)
    Require11 ($encryptedLength -ge 24 -and
        $encryptedOffset + 5 + $encryptedLength -le $clientFlight.Length) `
        'Phase 34 encrypted ClientFinished was invalid.'
    $encrypted = [byte[]]$clientFlight[$encryptedOffset..($encryptedOffset + 4 + $encryptedLength)]
    $plainLength = $encryptedLength - 8 - 16
    $nonce = New-Object byte[] 12
    [Array]::Copy($keyBlock, 32, $nonce, 0, 4)
    [Array]::Copy($encrypted, 5, $nonce, 4, 8)
    $aad = New-Object byte[] 13
    [Array]::Copy([byte[]](0,0,0,0,0,0,0,0,22,3,3,0,0), 0, $aad, 0, 13)
    Write-U64-Phase34 $aad 0 0
    Write-U16-Phase17 $aad 11 $plainLength
    $ciphertext = [byte[]]$encrypted[13..(12 + $plainLength)]
    $tag = [byte[]]$encrypted[(13 + $plainLength)..(28 + $plainLength)]
    $finished = New-Object byte[] $plainLength
    $aes = [Security.Cryptography.AesGcm]::new([byte[]]$keyBlock[0..15], 16)
    try { $aes.Decrypt($nonce, $ciphertext, $tag, $finished, $aad) }
    finally { $aes.Dispose() }
    Require11 ($finished.Length -eq 16) 'Phase 34 ClientFinished plaintext was invalid.'
    $finishedTranscript = [byte[]]($transcript + $finished)
    $transcriptHash = [Security.Cryptography.SHA256]::HashData($finishedTranscript)
    $verifyData = Invoke-Phase34Prf11 $masterSecret `
        ([Text.Encoding]::ASCII.GetBytes('server finished')) $transcriptHash 12
    $serverFinishedPlain = New-Object byte[] 16
    $serverFinishedPlain[0] = 20
    Write-U24-Phase34 $serverFinishedPlain 1 12
    [Array]::Copy($verifyData, 0, $serverFinishedPlain, 4, 12)
    $serverFinished = New-Phase34TlsRecord11 0 22 $serverFinishedPlain `
        ([byte[]]$keyBlock[16..31]) ([byte[]]$keyBlock[36..39])
    return [pscustomobject]@{
        KeyBlock = $keyBlock
        ServerFinished = $serverFinished
    }
}

function Write-U24-Phase34([byte[]]$bytes, [int]$offset, [int]$value) {
    $bytes[$offset] = [byte](($value -shr 16) -band 0xFF)
    $bytes[$offset + 1] = [byte](($value -shr 8) -band 0xFF)
    $bytes[$offset + 2] = [byte]($value -band 0xFF)
}

function Write-U64-Phase34([byte[]]$bytes, [int]$offset, [UInt64]$value) {
    for ($index = 0; $index -lt 8; ++$index) {
        $bytes[$offset + $index] = [byte](($value -shr (56 - 8 * $index)) -band 0xFF)
    }
}

function Get-Phase32TcpPayload11([byte[]]$frame) {
    if ($frame.Length -lt 54 -or $frame[14] -ne 0x45) { return ,([byte[]]@()) }
    $ipLength = Read-U16-Phase17 $frame 16
    $tcpHeaderLength = ($frame[46] -shr 4) * 4
    $payloadLength = $ipLength - 20 - $tcpHeaderLength
    if ($payloadLength -le 0 -or 54 + $payloadLength -gt $frame.Length) {
        return ,([byte[]]@())
    }
    return ,([byte[]]$frame[(34 + $tcpHeaderLength)..(33 + $tcpHeaderLength + $payloadLength)])
}

function Decrypt-Phase32ApplicationRecord11([byte[]]$record,
                                             [UInt64]$sequence,
                                             [byte[]]$key, [byte[]]$fixedIv) {
    if ($record.Length -lt 29 -or $record[0] -ne 23 -or
        $record[1] -ne 3 -or $record[2] -ne 3) { return $null }
    $recordLength = Read-U16-Phase17 $record 3
    if ($recordLength -ne $record.Length - 5 -or $recordLength -lt 24) { return $null }
    $plainLength = $recordLength - 8 - 16
    $nonce = New-Object byte[] 12
    [Array]::Copy($fixedIv, 0, $nonce, 0, 4)
    [Array]::Copy($record, 5, $nonce, 4, 8)
    $aad = New-Object byte[] 13
    for ($index = 0; $index -lt 8; ++$index) {
        $aad[$index] = [byte](($sequence -shr (56 - 8 * $index)) -band 0xFF)
    }
    $aad[8] = 23; $aad[9] = 3; $aad[10] = 3
    Write-U16-Phase17 $aad 11 $plainLength
    $ciphertext = [byte[]]$record[13..(12 + $plainLength)]
    $tag = [byte[]]$record[(13 + $plainLength)..(28 + $plainLength)]
    $plaintext = New-Object byte[] $plainLength
    $aes = [Security.Cryptography.AesGcm]::new($key, 16)
    try { $aes.Decrypt($nonce, $ciphertext, $tag, $plaintext, $aad) }
    catch { $aes.Dispose(); return $null }
    $aes.Dispose()
    return ,$plaintext
}

function Send-Phase32ServerTcpData11([Net.Sockets.UdpClient]$peerUdp,
                                      [int]$rxPort, [IO.StreamWriter]$log,
                                      [byte[]]$peerMac, [byte[]]$guestMac,
                                      [byte[]]$guestIp, [byte[]]$hostIp,
                                      [int]$clientPort, [int]$serverPort,
                                      [uint32]$serverSequence,
                                      [uint32]$clientSequence,
                                      [byte[]]$payload, [int]$tag,
                                      [int]$timeoutSeconds,
                                      [bool]$waitForAck = $true) {
    Require11 ($payload.Length -le 512) 'Phase 32 server TCP payload exceeded the managed MSS.'
    $frame = New-Ipv4Tcp22 $guestMac $peerMac $hostIp $guestIp `
        (New-TcpSegment22 $serverPort $clientPort $serverSequence $clientSequence 0x18 `
            $hostIp $guestIp $payload $false) $tag
    Require11 ($peerUdp.Send($frame, $frame.Length, '127.0.0.1', $rxPort) -eq $frame.Length) `
        'Phase 32 server TCP data send was short.'
    Write-Phase22Frame $log 'phase32_server_tcp_data' $frame
    if (-not $waitForAck) { return }
    $expectedAck = [uint32]($serverSequence + $payload.Length)
    $ack = Receive-ExpectedPhase22TcpFrame $peerUdp $timeoutSeconds `
        'Phase 32 server data ACK' $peerMac $guestMac $guestIp $hostIp `
        $clientPort $serverPort $clientSequence $expectedAck 0x10 `
        ([byte[]]@()) $false
    Write-Phase22Frame $log 'phase32_managed_server_data_ack' $ack
}

function Send-Phase32PeerAck11([Net.Sockets.UdpClient]$peerUdp,
                               [int]$rxPort, [IO.StreamWriter]$log,
                               [byte[]]$peerMac, [byte[]]$guestMac,
                               [byte[]]$guestIp, [byte[]]$hostIp,
                               [int]$clientPort, [int]$serverPort,
                               [uint32]$serverSequence,
                               [uint32]$clientSequence, [int]$tag) {
    $frame = New-Ipv4Tcp22 $guestMac $peerMac $hostIp $guestIp `
        (New-TcpSegment22 $serverPort $clientPort $serverSequence $clientSequence 0x10 `
            $hostIp $guestIp ([byte[]]@()) $false) $tag
    Require11 ($peerUdp.Send($frame, $frame.Length, '127.0.0.1', $rxPort) -eq $frame.Length) `
        'Phase 32 peer ACK send was short.'
    Write-Phase22Frame $log 'phase32_peer_ack' $frame
}

function Invoke-Phase32HttpsExchange11([Net.Sockets.UdpClient]$peerUdp,
                                       [int]$rxPort, [int]$timeoutSeconds,
                                       [System.Diagnostics.Process]$process,
                                       [System.IO.Stream]$stream,
                                       [IO.FileStream]$serialLog,
                                       [Text.StringBuilder]$text,
                                       [byte[]]$receiveBuffer,
                                       [IO.StreamWriter]$injectionLog,
                                       [byte[]]$guestMacBytes,
                                       [byte[]]$hostMacBytes,
                                       [byte[]]$guestIpBytes,
                                       [byte[]]$hostIpBytes,
                                       [bool]$negativeControl) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
    $discoverFrame = Receive-AnyPhase19Frame $peerUdp $timeoutSeconds 'Phase 32 DHCPDISCOVER'
    $discoverPayload = Get-DhcpPayload19 $discoverFrame
    Require11 ($null -ne $discoverPayload) 'Phase 32 DHCPDISCOVER is not IPv4/UDP.'
    Write-Phase20Frame $injectionLog 'phase32_dhcpdiscover' $discoverFrame
    $offerPayload = New-DhcpReply19 $discoverPayload 2 $guestIpBytes $hostIpBytes $true $true $true
    $broadcastMac = [byte[]](0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
    $broadcastIp = [byte[]](255, 255, 255, 255)
    $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIp `
        (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIp $offerPayload) 0x3F21
    Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length, '127.0.0.1', $rxPort) -eq $offerFrame.Length) 'Phase 32 DHCPOFFER send was short.'
    Write-Phase20Frame $injectionLog 'phase32_dhcpoffer' $offerFrame

    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
    $requestFrame = Receive-AnyPhase19Frame $peerUdp $timeoutSeconds 'Phase 32 DHCPREQUEST'
    $requestPayload = Get-DhcpPayload19 $requestFrame
    Require11 ($null -ne $requestPayload) 'Phase 32 DHCPREQUEST is not IPv4/UDP.'
    Write-Phase20Frame $injectionLog 'phase32_dhcprequest' $requestFrame
    $ackPayload = New-DhcpReply19 $requestPayload 5 $guestIpBytes $hostIpBytes $true $true $true
    $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIp `
        (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIp $ackPayload) 0x3F22
    Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length, '127.0.0.1', $rxPort) -eq $ackFrame.Length) 'Phase 32 DHCPACK send was short.'
    Write-Phase20Frame $injectionLog 'phase32_dhcpack' $ackFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_CONFIGURED' $deadline $process $stream $serialLog $text $receiveBuffer

    $zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
    $arpRequest = New-Phase16ArpFrame11 $broadcastMac $guestMacBytes 1 $guestIpBytes $zeroMac $hostIpBytes
    $observedArp = Receive-ExpectedPhase16Frame11 $peerUdp $arpRequest 'Phase 32 DNS ARP request' $timeoutSeconds
    Write-Phase20Frame $injectionLog 'phase32_dns_arp_request' $observedArp
    $arpReply = New-Phase16ArpFrame11 $guestMacBytes $hostMacBytes 2 $hostIpBytes $guestMacBytes $guestIpBytes
    Require11 ($peerUdp.Send($arpReply, $arpReply.Length, '127.0.0.1', $rxPort) -eq $arpReply.Length) 'Phase 32 DNS ARP reply send was short.'
    Write-Phase20Frame $injectionLog 'phase32_dns_arp_reply' $arpReply

    $dnsQueryFrame = Receive-AnyDns20Frame $peerUdp $timeoutSeconds 'www.example.com'
    $dnsQueryPayload = Get-DnsPayload20 $dnsQueryFrame
    $expectedQuestion = [byte[]](3,119,119,119,7,101,120,97,109,112,108,101,3,99,111,109,0,0,1,0,1)
    Require11 ($null -ne $dnsQueryPayload -and $dnsQueryPayload.Length -eq 33 -and
        (Bytes-Equal16 $dnsQueryPayload 12 $expectedQuestion)) 'Phase 32 DNS query name was invalid.'
    Write-Phase20Frame $injectionLog 'phase32_dns_query' $dnsQueryFrame
    $dnsResponse = New-DnsResponse20 $dnsQueryPayload 'valid'
    $dnsResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
        (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $dnsResponse) 0x3F23
    Require11 ($peerUdp.Send($dnsResponseFrame, $dnsResponseFrame.Length, '127.0.0.1', $rxPort) -eq $dnsResponseFrame.Length) 'Phase 32 DNS response send was short.'
    Write-Phase20Frame $injectionLog 'phase32_dns_response' $dnsResponseFrame

    $clientPort = 15221; $serverPort = 443
    [uint32]$clientIsn = 0x22000001; [uint32]$serverIsn = 0x32010001
    [uint32]$clientNext = $clientIsn + 1; [uint32]$serverNext = $serverIsn + 1
    $peerMac = $hostMacBytes; $guestMac = $guestMacBytes
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_REQUEST_STARTED' $deadline $process $stream $serialLog $text $receiveBuffer
    $syn = New-TcpSegment22 $clientPort $serverPort $clientIsn 0 2 $guestIpBytes $hostIpBytes ([byte[]]@()) $true
    $synFrame = New-Ipv4Tcp22 $peerMac $guestMac $guestIpBytes $hostIpBytes $syn 0x2A00
    $observedSyn = Receive-ExpectedPhase17Frame $peerUdp $synFrame 'Phase 32 managed SYN' $timeoutSeconds
    Require11 (Test-TcpFrame22 $observedSyn $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientIsn 0 2 ([byte[]]@()) $true) 'Phase 32 SYN validation failed.'
    Write-Phase22Frame $injectionLog 'phase32_managed_syn' $observedSyn
    $synAck = New-TcpSegment22 $serverPort $clientPort $serverIsn $clientNext 0x12 $hostIpBytes $guestIpBytes ([byte[]]@()) $true
    $synAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $synAck 0x3F31
    Require11 ($peerUdp.Send($synAckFrame, $synAckFrame.Length, '127.0.0.1', $rxPort) -eq $synAckFrame.Length) 'Phase 32 SYNACK send was short.'
    Write-Phase22Frame $injectionLog 'phase32_peer_synack' $synAckFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_TCP_CONNECTED' $deadline $process $stream $serialLog $text $receiveBuffer
    $observedHandshakeAck = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds 'Phase 32 managed handshake ACK'
    Require11 (Test-TcpFrame22 $observedHandshakeAck $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverNext 0x10 ([byte[]]@()) $false) 'Phase 32 handshake ACK validation failed.'
    Write-Phase22Frame $injectionLog 'phase32_managed_handshake_ack' $observedHandshakeAck
    $observedHello = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds 'Phase 32 ClientHello'
    $helloPayload = Get-Phase32TcpPayload11 $observedHello
    $expectedHello = New-Phase32PlainTlsRecord11 22 (Get-Phase32FixtureBytes11 'ClientHello')
    Require11 (Test-TcpFrame22 $observedHello $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverNext 0x18 $expectedHello $false) 'Phase 32 ClientHello/SNI validation failed.'
    Require11 (([Text.Encoding]::ASCII.GetString($helloPayload)).Contains('www.example.com')) 'Phase 32 SNI hostname was not present.'
    Write-Phase22Frame $injectionLog 'phase32_clienthello_sni' $observedHello
    [uint32]$clientNext = $clientNext + $helloPayload.Length
    Send-Phase32PeerAck11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverNext $clientNext 0x3F32

    $serverRecordNames = @('ServerHelloRecord', 'CertificateRecord0', 'CertificateRecord1', 'CertificateRecord2', 'CertificateRecord3', 'CertificateRecord4', 'CertificateRecord5', 'CertificateRecord6', 'CertificateRecord7', 'CertificateRecord8', 'CertificateRecord9', 'ServerKeyExchangeRecord', 'ServerHelloDoneRecord')
    $serverSequence = [uint32]$serverNext
    $recordIndex = 0
    foreach ($recordName in $serverRecordNames) {
        $record = Get-Phase32FixtureBytes11 $recordName
        $chunks = @()
        $firstEnd = [Math]::Min(1, $record.Length - 1)
        $chunks += ,([byte[]]$record[0..$firstEnd])
        if ($record.Length -gt 2) {
            $secondEnd = [Math]::Min(12, $record.Length - 1)
            $chunks += ,([byte[]]$record[2..$secondEnd])
        }
        if ($record.Length -gt 13) {
            $chunks += ,([byte[]]$record[13..($record.Length - 1)])
        }
        foreach ($chunk in $chunks) {
            Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext ([byte[]]$chunk) (0x4000 + $recordIndex) $timeoutSeconds
            [uint32]$serverSequence = $serverSequence + $chunk.Length
            ++$recordIndex
        }
    }

    $observedFlight = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds 'Phase 32 client TLS flight'
    $flightPayload = Get-Phase32TcpPayload11 $observedFlight
    $clientKeyExchange = New-Phase32PlainTlsRecord11 22 (Get-Phase32FixtureBytes11 'ClientKeyExchange')
    $changeCipherSpec = Get-Phase32FixtureBytes11 'ChangeCipherSpec'
    $clientFinishedRecord = Get-Phase32FixtureBytes11 'ClientFinishedRecord'
    $expectedFlight = New-Object byte[] ($clientKeyExchange.Length + $changeCipherSpec.Length + $clientFinishedRecord.Length)
    [Array]::Copy($clientKeyExchange, 0, $expectedFlight, 0, $clientKeyExchange.Length)
    [Array]::Copy($changeCipherSpec, 0, $expectedFlight, $clientKeyExchange.Length, $changeCipherSpec.Length)
    [Array]::Copy($clientFinishedRecord, 0, $expectedFlight, $clientKeyExchange.Length + $changeCipherSpec.Length, $clientFinishedRecord.Length)
    Require11 (Test-TcpFrame22 $observedFlight $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverSequence 0x18 $expectedFlight $false) 'Phase 32 client TLS flight validation failed.'
    Write-Phase22Frame $injectionLog 'phase32_client_tls_flight' $observedFlight
    [uint32]$clientNext = $clientNext + $flightPayload.Length

    $serverCcs = Get-Phase32FixtureBytes11 'ChangeCipherSpec'
    Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext $serverCcs 0x4100 $timeoutSeconds
    [uint32]$serverSequence = $serverSequence + $serverCcs.Length
    $serverFinished = Get-Phase32FixtureBytes11 'ServerFinishedRecord'
    if ($negativeControl) {
        $serverFinished = [byte[]]$serverFinished.Clone()
        $serverFinished[$serverFinished.Length - 1] =
            $serverFinished[$serverFinished.Length - 1] -bxor 1
    }
    Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext $serverFinished 0x4101 $timeoutSeconds
    [uint32]$serverSequence = $serverSequence + $serverFinished.Length

    if ($negativeControl) {
        Wait-Phase32NegativeOutcome11 `
            $deadline $process $stream $serialLog $text $receiveBuffer
        $negativeTranscript = $text.ToString()
        Require11 (-not $negativeTranscript.Contains(
            'GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_REQUEST_ENCRYPTED_SENT')) `
            'Phase 32 negative control emitted an encrypted HTTP request.'
        Require11 (-not $negativeTranscript.Contains(
            'GXOS_NET10:MANAGED_HTTPS_PHASE32_PASS')) `
            'Phase 32 negative control emitted an HTTPS pass marker.'
        Require11 (-not $negativeTranscript.Contains(
            'GXOS_NET10:MANAGED_KERNEL_PHASE32_PASS')) `
            'Phase 32 negative control emitted a kernel pass marker.'
        return 'NEGATIVE_PASS_PHASE32'
    }

    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_REQUEST_ENCRYPTED_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
    $observedRequest = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds 'Phase 32 encrypted HTTP request'
    $requestPayload = Get-Phase32TcpPayload11 $observedRequest
    $keyBlock = Get-Phase32FixtureBytes11 'KeyBlock'
    $clientKey = [byte[]]$keyBlock[0..15]; $clientIv = [byte[]]$keyBlock[32..35]
    $decryptedRequest = Decrypt-Phase32ApplicationRecord11 $requestPayload 1 $clientKey $clientIv
    $expectedRequestText = "GET /phase32 HTTP/1.1`r`nHost: www.example.com`r`nConnection: close`r`n`r`n"
    Require11 ($null -ne $decryptedRequest -and
        [Text.Encoding]::ASCII.GetString($decryptedRequest) -eq $expectedRequestText) 'Phase 32 encrypted HTTP request validation failed.'
    Require11 (Test-TcpFrame22 $observedRequest $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverSequence 0x18 $requestPayload $false) 'Phase 32 request TCP framing validation failed.'
    Write-Phase22Frame $injectionLog 'phase32_encrypted_http_request' $observedRequest
    [uint32]$clientNext = $clientNext + $requestPayload.Length
    Send-Phase32PeerAck11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext 0x4102

    $serverKey = [byte[]]$keyBlock[16..31]; $serverIv = [byte[]]$keyBlock[36..39]
    $responseTexts = @('HTTP/1.1 200', " OK`r`nContent-Length: 17`r`nConnection: close`r`n", "Content-Type: text/plain`r`n`r`nphase32-", 'http-pass')
    $responseRecords = New-Object byte[][] $responseTexts.Length
    for ($index = 0; $index -lt $responseTexts.Length; ++$index) {
        $responseRecords[$index] = New-Phase32ApplicationRecord11 ([UInt64]($index + 1)) ([Text.Encoding]::ASCII.GetBytes($responseTexts[$index])) $serverKey $serverIv
    }
    $responseChunks = @(
        [byte[]]$responseRecords[0][0..1],
        [byte[]]$responseRecords[0][2..12],
        [byte[]]$responseRecords[0][13..($responseRecords[0].Length - 1)],
        [byte[]]($responseRecords[1] + $responseRecords[2]),
        [byte[]]$responseRecords[3][0..1],
        [byte[]]$responseRecords[3][2..($responseRecords[3].Length - 1)]
    )
    $chunkIndex = 0
    foreach ($chunk in $responseChunks) {
        Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext ([byte[]]$chunk) (0x4200 + $chunkIndex) $timeoutSeconds
        [uint32]$serverSequence = $serverSequence + $chunk.Length
        ++$chunkIndex
    }
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_STATUS_PARSED=200' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_RECEIVED' $deadline $process $stream $serialLog $text $receiveBuffer

    $peerFin = New-TcpSegment22 $serverPort $clientPort $serverSequence $clientNext 0x11 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
    $peerFinFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $peerFin 0x3F50
    Require11 ($peerUdp.Send($peerFinFrame, $peerFinFrame.Length, '127.0.0.1', $rxPort) -eq $peerFinFrame.Length) 'Phase 32 peer FIN send was short.'
    Write-Phase22Frame $injectionLog 'phase32_peer_fin' $peerFinFrame
    [uint32]$finNext = $clientNext + 1; [uint32]$peerFinNext = $serverSequence + 1
    $managedFinAck = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds 'Phase 32 managed FIN ACK'
    Require11 (Test-TcpFrame22 $managedFinAck $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $peerFinNext 0x10 ([byte[]]@()) $false) 'Phase 32 managed FIN ACK validation failed.'
    Write-Phase22Frame $injectionLog 'phase32_managed_fin_ack' $managedFinAck
    $managedFin = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds 'Phase 32 managed FIN'
    Require11 (Test-TcpFrame22 $managedFin $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $peerFinNext 0x11 ([byte[]]@()) $false) 'Phase 32 managed FIN validation failed.'
    Write-Phase22Frame $injectionLog 'phase32_managed_fin' $managedFin
    $finalAck = New-TcpSegment22 $serverPort $clientPort $peerFinNext $finNext 0x10 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
    $finalAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $finalAck 0x3F51
    Require11 ($peerUdp.Send($finalAckFrame, $finalAckFrame.Length, '127.0.0.1', $rxPort) -eq $finalAckFrame.Length) 'Phase 32 final ACK send was short.'
    Write-Phase22Frame $injectionLog 'phase32_final_ack' $finalAckFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_VERIFIED' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_TEARDOWN_COMPLETE' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE32_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE32_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
    return 'PASS_PHASE32'
}

function Invoke-Phase33HttpsExchange11([Net.Sockets.UdpClient]$peerUdp,
                                       [int]$rxPort, [int]$timeoutSeconds,
                                       [System.Diagnostics.Process]$process,
                                       [System.IO.Stream]$stream,
                                       [IO.FileStream]$serialLog,
                                       [Text.StringBuilder]$text,
                                       [byte[]]$receiveBuffer,
                                       [IO.StreamWriter]$injectionLog,
                                       [byte[]]$guestMacBytes,
                                       [byte[]]$hostMacBytes,
                                       [byte[]]$guestIpBytes,
                                       [byte[]]$hostIpBytes,
                                       [bool]$negativeControl) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
    $discoverFrame = Receive-AnyPhase19Frame $peerUdp $timeoutSeconds 'Phase 33 DHCPDISCOVER'
    $discoverPayload = Get-DhcpPayload19 $discoverFrame
    Require11 ($null -ne $discoverPayload) 'Phase 33 DHCPDISCOVER is not IPv4/UDP.'
    Write-Phase20Frame $injectionLog 'phase33_dhcpdiscover' $discoverFrame
    $broadcastMac = [byte[]](0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
    $broadcastIp = [byte[]](255, 255, 255, 255)
    $offerPayload = New-DhcpReply19 $discoverPayload 2 $guestIpBytes $hostIpBytes $true $true $true
    $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIp `
        (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIp $offerPayload) 0x4F21
    Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length, '127.0.0.1', $rxPort) -eq $offerFrame.Length) 'Phase 33 DHCPOFFER send was short.'
    Write-Phase20Frame $injectionLog 'phase33_dhcpoffer' $offerFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
    $requestFrame = Receive-AnyPhase19Frame $peerUdp $timeoutSeconds 'Phase 33 DHCPREQUEST'
    $requestPayload = Get-DhcpPayload19 $requestFrame
    Require11 ($null -ne $requestPayload) 'Phase 33 DHCPREQUEST is not IPv4/UDP.'
    Write-Phase20Frame $injectionLog 'phase33_dhcprequest' $requestFrame
    $ackPayload = New-DhcpReply19 $requestPayload 5 $guestIpBytes $hostIpBytes $true $true $true
    $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIp `
        (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIp $ackPayload) 0x4F22
    Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length, '127.0.0.1', $rxPort) -eq $ackFrame.Length) 'Phase 33 DHCPACK send was short.'
    Write-Phase20Frame $injectionLog 'phase33_dhcpack' $ackFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_CONFIGURED' $deadline $process $stream $serialLog $text $receiveBuffer

    $zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
    $arpRequest = New-Phase16ArpFrame11 $broadcastMac $guestMacBytes 1 $guestIpBytes $zeroMac $hostIpBytes
    $observedArp = Receive-ExpectedPhase16Frame11 $peerUdp $arpRequest 'Phase 33 DNS ARP request' $timeoutSeconds
    Write-Phase20Frame $injectionLog 'phase33_dns_arp_request' $observedArp
    $arpReply = New-Phase16ArpFrame11 $guestMacBytes $hostMacBytes 2 $hostIpBytes $guestMacBytes $guestIpBytes
    Require11 ($peerUdp.Send($arpReply, $arpReply.Length, '127.0.0.1', $rxPort) -eq $arpReply.Length) 'Phase 33 DNS ARP reply send was short.'
    Write-Phase20Frame $injectionLog 'phase33_dns_arp_reply' $arpReply

    $keyBlock = Get-Phase32FixtureBytes11 'KeyBlock'
    $serverKey = [byte[]]$keyBlock[16..31]
    $serverIv = [byte[]]$keyBlock[36..39]
    $clientKey = [byte[]]$keyBlock[0..15]
    $clientIv = [byte[]]$keyBlock[32..35]
    $paths = @('/phase33-length', '/phase33-chunked', '/phase33-stream')
    for ($requestIndex = 0; $requestIndex -lt $paths.Length; ++$requestIndex) {
        $dnsQueryFrame = Receive-AnyDns20Frame $peerUdp $timeoutSeconds 'www.example.com'
        $dnsQueryPayload = Get-DnsPayload20 $dnsQueryFrame
        $expectedQuestion = [byte[]](3,119,119,119,7,101,120,97,109,112,108,101,3,99,111,109,0,0,1,0,1)
        Require11 ($null -ne $dnsQueryPayload -and $dnsQueryPayload.Length -eq 33 -and
            (Bytes-Equal16 $dnsQueryPayload 12 $expectedQuestion)) 'Phase 33 DNS query name was invalid.'
        Write-Phase20Frame $injectionLog ('phase33_dns_query_{0}' -f $requestIndex) $dnsQueryFrame
        $dnsResponse = New-DnsResponse20 $dnsQueryPayload 'valid'
        $dnsResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes $hostIpBytes $guestIpBytes `
            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $dnsResponse) (0x4F30 + $requestIndex)
        Require11 ($peerUdp.Send($dnsResponseFrame, $dnsResponseFrame.Length, '127.0.0.1', $rxPort) -eq $dnsResponseFrame.Length) 'Phase 33 DNS response send was short.'
        Write-Phase20Frame $injectionLog ('phase33_dns_response_{0}' -f $requestIndex) $dnsResponseFrame
        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_DNS_SUCCESS' $deadline $process $stream $serialLog $text $receiveBuffer

        $clientPort = 15221; $serverPort = 443
        [uint32]$clientIsn = 0x22000001 + ($requestIndex * 0x100)
        [uint32]$serverIsn = 0x33010001 + ($requestIndex * 0x100)
        [uint32]$clientNext = $clientIsn + 1; [uint32]$serverNext = $serverIsn + 1
        $peerMac = $hostMacBytes; $guestMac = $guestMacBytes
        $path = $paths[$requestIndex]
        $pathTag = 'Phase 33 {0}' -f $path
        $observedSyn = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' managed SYN')
        Require11 (Test-TcpFrame22 $observedSyn $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientIsn 0 2 ([byte[]]@()) $true) 'Phase 33 SYN validation failed.'
        Write-Phase22Frame $injectionLog ('phase33_managed_syn_{0}' -f $requestIndex) $observedSyn
        $synAck = New-TcpSegment22 $serverPort $clientPort $serverIsn $clientNext 0x12 $hostIpBytes $guestIpBytes ([byte[]]@()) $true
        $synAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $synAck (0x4F31 + $requestIndex)
        Require11 ($peerUdp.Send($synAckFrame, $synAckFrame.Length, '127.0.0.1', $rxPort) -eq $synAckFrame.Length) 'Phase 33 SYNACK send was short.'
        Write-Phase22Frame $injectionLog ('phase33_peer_synack_{0}' -f $requestIndex) $synAckFrame
        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_TCP_CONNECTED' $deadline $process $stream $serialLog $text $receiveBuffer
        $observedHandshakeAck = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' handshake ACK')
        Require11 (Test-TcpFrame22 $observedHandshakeAck $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverNext 0x10 ([byte[]]@()) $false) 'Phase 33 handshake ACK validation failed.'
        Write-Phase22Frame $injectionLog ('phase33_managed_handshake_ack_{0}' -f $requestIndex) $observedHandshakeAck
        $observedHello = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' ClientHello')
        $helloPayload = Get-Phase32TcpPayload11 $observedHello
        $expectedHello = New-Phase32PlainTlsRecord11 22 (Get-Phase32FixtureBytes11 'ClientHello')
        Require11 (Test-TcpFrame22 $observedHello $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverNext 0x18 $expectedHello $false) 'Phase 33 ClientHello/SNI validation failed.'
    Require11 (([Text.Encoding]::ASCII.GetString($helloPayload)).Contains('www.example.com')) 'Phase 33 SNI hostname was not present.'
        Write-Phase22Frame $injectionLog ('phase33_clienthello_{0}' -f $requestIndex) $observedHello
        [uint32]$clientNext = $clientNext + $helloPayload.Length
        Send-Phase32PeerAck11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverNext $clientNext (0x4F32 + $requestIndex)

        $serverRecordNames = @('ServerHelloRecord', 'CertificateRecord0', 'CertificateRecord1', 'CertificateRecord2', 'CertificateRecord3', 'CertificateRecord4', 'CertificateRecord5', 'CertificateRecord6', 'CertificateRecord7', 'CertificateRecord8', 'CertificateRecord9', 'ServerKeyExchangeRecord', 'ServerHelloDoneRecord')
        $serverSequence = [uint32]$serverNext
        $recordIndex = 0
        foreach ($recordName in $serverRecordNames) {
            $record = Get-Phase32FixtureBytes11 $recordName
            $chunks = @()
            $firstEnd = [Math]::Min(1, $record.Length - 1)
            $chunks += ,([byte[]]$record[0..$firstEnd])
            if ($record.Length -gt 2) {
                $secondEnd = [Math]::Min(12, $record.Length - 1)
                $chunks += ,([byte[]]$record[2..$secondEnd])
            }
            if ($record.Length -gt 13) { $chunks += ,([byte[]]$record[13..($record.Length - 1)]) }
            foreach ($chunk in $chunks) {
                Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext ([byte[]]$chunk) (0x5000 + $requestIndex * 0x100 + $recordIndex) $timeoutSeconds
                [uint32]$serverSequence = $serverSequence + $chunk.Length
                ++$recordIndex
            }
        }
        $observedFlight = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' client TLS flight')
        $flightPayload = Get-Phase32TcpPayload11 $observedFlight
        $clientKeyExchange = New-Phase32PlainTlsRecord11 22 (Get-Phase32FixtureBytes11 'ClientKeyExchange')
        $changeCipherSpec = Get-Phase32FixtureBytes11 'ChangeCipherSpec'
        $clientFinishedRecord = Get-Phase32FixtureBytes11 'ClientFinishedRecord'
        $expectedFlight = New-Object byte[] ($clientKeyExchange.Length + $changeCipherSpec.Length + $clientFinishedRecord.Length)
        [Array]::Copy($clientKeyExchange, 0, $expectedFlight, 0, $clientKeyExchange.Length)
        [Array]::Copy($changeCipherSpec, 0, $expectedFlight, $clientKeyExchange.Length, $changeCipherSpec.Length)
        [Array]::Copy($clientFinishedRecord, 0, $expectedFlight, $clientKeyExchange.Length + $changeCipherSpec.Length, $clientFinishedRecord.Length)
        Require11 (Test-TcpFrame22 $observedFlight $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverSequence 0x18 $expectedFlight $false) 'Phase 33 client TLS flight validation failed.'
        Write-Phase22Frame $injectionLog ('phase33_client_tls_flight_{0}' -f $requestIndex) $observedFlight
        [uint32]$clientNext = $clientNext + $flightPayload.Length
        $serverCcs = Get-Phase32FixtureBytes11 'ChangeCipherSpec'
        Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext $serverCcs (0x5100 + $requestIndex) $timeoutSeconds
        [uint32]$serverSequence = $serverSequence + $serverCcs.Length
        $serverFinished = Get-Phase32FixtureBytes11 'ServerFinishedRecord'
        Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext $serverFinished (0x5101 + $requestIndex) $timeoutSeconds
        [uint32]$serverSequence = $serverSequence + $serverFinished.Length

        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_HTTP_REQUEST_ENCRYPTED_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
        $observedRequest = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' encrypted HTTP request')
        $requestPayload = Get-Phase32TcpPayload11 $observedRequest
        $decryptedRequest = Decrypt-Phase32ApplicationRecord11 $requestPayload 1 $clientKey $clientIv
        $expectedRequestText = "GET $path HTTP/1.1`r`nHost: www.example.com`r`nConnection: close`r`n`r`n"
        Require11 ($null -ne $decryptedRequest -and [Text.Encoding]::ASCII.GetString($decryptedRequest) -eq $expectedRequestText) ($pathTag + ' encrypted HTTP request validation failed.')
        Require11 (Test-TcpFrame22 $observedRequest $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverSequence 0x18 $requestPayload $false) ($pathTag + ' request TCP framing validation failed.')
        Write-Phase22Frame $injectionLog ('phase33_encrypted_http_request_{0}' -f $requestIndex) $observedRequest
        [uint32]$clientNext = $clientNext + $requestPayload.Length
        Send-Phase32PeerAck11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext (0x5102 + $requestIndex)

        if ($requestIndex -eq 0) {
            $body = [Text.Encoding]::ASCII.GetBytes('phase33-content-length-pass')
            $response = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n" + [Text.Encoding]::ASCII.GetString($body))
        } elseif ($requestIndex -eq 1) {
            if ($negativeControl) {
                $padding = [string]::new('a', 256)
                $response = [Text.Encoding]::ASCII.GetBytes(
                    "HTTP/1.1 200 OK`r`nTransfer-Encoding: chunked`r`nConnection: close`r`n`r`n100`r`n$padding`r`nZZ`r`n")
            } else {
                $response = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nTransfer-Encoding: chunked`r`nConnection: close`r`n`r`n7`r`nphase33`r`n1`r`n-`r`n4`r`nhttp`r`n1`r`n-`r`n4`r`npass`r`n0`r`n`r`n")
            }
        } else {
            $body = New-Object byte[] 4097
            for ($index = 0; $index -lt $body.Length; ++$index) { $body[$index] = [byte]($index -band 0xFF) }
            $header = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Length: 4097`r`nConnection: close`r`n`r`n")
            $response = New-Object byte[] ($header.Length + $body.Length)
            [Array]::Copy($header, 0, $response, 0, $header.Length)
            [Array]::Copy($body, 0, $response, $header.Length, $body.Length)
        }
        $responseRecordCount = [int][Math]::Ceiling($response.Length / 180.0)
        $responseRecords = New-Object byte[][] $responseRecordCount
        $responseRecordIndex = 0
        for ($offset = 0; $offset -lt $response.Length;) {
            $count = [Math]::Min(180, $response.Length - $offset)
            $plain = [byte[]]$response[$offset..($offset + $count - 1)]
            $responseRecords[$responseRecordIndex] = New-Phase32ApplicationRecord11 ([UInt64]($responseRecordIndex + 1)) $plain $serverKey $serverIv
            ++$responseRecordIndex
            $offset += $count
        }
        $chunkIndex = 0
        foreach ($record in $responseRecords) {
            $tcpChunks = New-Object byte[][] 3
            $tcpChunkCount = 1
            $tcpChunks[0] = [byte[]]$record[0..1]
            if ($record.Length -gt 2) {
                $tcpChunks[$tcpChunkCount] =
                    [byte[]]$record[2..([Math]::Min(10, $record.Length - 1))]
                ++$tcpChunkCount
            }
            if ($record.Length -gt 11) {
                $tcpChunks[$tcpChunkCount] = [byte[]]$record[11..($record.Length - 1)]
                ++$tcpChunkCount
            }
            for ($tcpChunkIndex = 0; $tcpChunkIndex -lt $tcpChunkCount; ++$tcpChunkIndex) {
                $chunk = $tcpChunks[$tcpChunkIndex]
                $waitForResponseAck = -not ($negativeControl -and $requestIndex -eq 1)
                Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $serverSequence $clientNext ([byte[]]$chunk) (0x5200 + $requestIndex * 0x100 + $chunkIndex) $timeoutSeconds $waitForResponseAck
                [uint32]$serverSequence = $serverSequence + $chunk.Length
                ++$chunkIndex
            }
        }
        if ($negativeControl -and $requestIndex -eq 1) {
            Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE33_START_FAILED' $deadline $process $stream $serialLog $text $receiveBuffer
            $negativeTranscript = $text.ToString()
            Require11 (-not $negativeTranscript.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE33_PASS')) 'Phase 33 negative control emitted an HTTPS pass marker.'
            Require11 (-not $negativeTranscript.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE33_PASS')) 'Phase 33 negative control emitted a kernel pass marker.'
            return 'NEGATIVE_PASS_PHASE33'
        }
        if ($requestIndex -eq 1) {
            Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_CHUNKED_SELECTED' $deadline $process $stream $serialLog $text $receiveBuffer
        } elseif ($requestIndex -eq 0) {
            Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_CONTENT_LENGTH_SELECTED' $deadline $process $stream $serialLog $text $receiveBuffer
        } else {
            Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_CONTENT_LENGTH_SELECTED' $deadline $process $stream $serialLog $text $receiveBuffer
        }
        $peerFin = New-TcpSegment22 $serverPort $clientPort $serverSequence $clientNext 0x11 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
        $peerFinFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $peerFin (0x4F50 + $requestIndex)
        Require11 ($peerUdp.Send($peerFinFrame, $peerFinFrame.Length, '127.0.0.1', $rxPort) -eq $peerFinFrame.Length) ($pathTag + ' peer FIN send was short.')
        Write-Phase22Frame $injectionLog ('phase33_peer_fin_{0}' -f $requestIndex) $peerFinFrame
        [uint32]$finNext = $clientNext + 1; [uint32]$peerFinNext = $serverSequence + 1
        $firstCloseFrame = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' close response')
        $firstCloseFlags = $firstCloseFrame[47]
        $actualCloseSequence = Read-U32-Phase19 $firstCloseFrame 38
        $actualCloseAcknowledgment = Read-U32-Phase19 $firstCloseFrame 42
        if ($firstCloseFlags -eq 0x11) {
            if ($actualCloseAcknowledgment -eq $serverSequence) {
                Require11 (Test-TcpFrame22 $firstCloseFrame $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverSequence 0x11 ([byte[]]@()) $false) ($pathTag + ' managed FIN validation failed.')
                $managedFinAck = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' peer FIN ACK')
                Require11 (Test-TcpFrame22 $managedFinAck $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $finNext $peerFinNext 0x10 ([byte[]]@()) $false) ($pathTag + ' peer FIN ACK validation failed.')
                Write-Phase22Frame $injectionLog ('phase33_managed_fin_ack_{0}' -f $requestIndex) $managedFinAck
            } else {
                Require11 (Test-TcpFrame22 $firstCloseFrame $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $peerFinNext 0x11 ([byte[]]@()) $false) ($pathTag + " managed FIN validation failed (expected seq=$('{0:X8}' -f $clientNext) ack=$('{0:X8}' -f $peerFinNext), actual seq=$('{0:X8}' -f $actualCloseSequence) ack=$('{0:X8}' -f $actualCloseAcknowledgment)).")
            }
            Write-Phase22Frame $injectionLog ('phase33_managed_fin_{0}' -f $requestIndex) $firstCloseFrame
        } else {
            $firstSequence = Read-U32-Phase19 $firstCloseFrame 38
            Require11 (($firstSequence -eq $clientNext -or $firstSequence -eq $finNext) -and
                (Test-TcpFrame22 $firstCloseFrame $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $firstSequence $peerFinNext 0x10 ([byte[]]@()) $false)) ($pathTag + ' managed FIN ACK validation failed.')
            Write-Phase22Frame $injectionLog ('phase33_managed_fin_ack_{0}' -f $requestIndex) $firstCloseFrame
            if ($firstSequence -eq $clientNext) {
                $managedFin = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds ($pathTag + ' managed FIN')
                Require11 (Test-TcpFrame22 $managedFin $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $peerFinNext 0x11 ([byte[]]@()) $false) ($pathTag + ' managed FIN validation failed.')
                Write-Phase22Frame $injectionLog ('phase33_managed_fin_{0}' -f $requestIndex) $managedFin
            }
        }
        $finalAck = New-TcpSegment22 $serverPort $clientPort $peerFinNext $finNext 0x10 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
        $finalAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $finalAck (0x4F51 + $requestIndex)
        Require11 ($peerUdp.Send($finalAckFrame, $finalAckFrame.Length, '127.0.0.1', $rxPort) -eq $finalAckFrame.Length) ($pathTag + ' final ACK send was short.')
        Write-Phase22Frame $injectionLog ('phase33_final_ack_{0}' -f $requestIndex) $finalAckFrame
        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_RESPONSE_COMPLETE' $deadline $process $stream $serialLog $text $receiveBuffer
    }
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_BODY_VERIFIED' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE33_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE33_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
    return 'PASS_PHASE33'
}

function New-Phase34DnsQuestion11([string]$hostname) {
    $result = New-Object byte[] 256
    $offset = 0
    foreach ($label in $hostname.Split('.')) {
        $bytes = [Text.Encoding]::ASCII.GetBytes($label)
        Require11 ($bytes.Length -ge 1 -and $bytes.Length -le 63) `
            "Phase 34 DNS label was invalid: $hostname"
        $result[$offset++] = [byte]$bytes.Length
        [Array]::Copy($bytes, 0, $result, $offset, $bytes.Length)
        $offset += $bytes.Length
    }
    $result[$offset++] = 0
    $result[$offset++] = 0
    $result[$offset++] = 1
    $result[$offset++] = 0
    $result[$offset++] = 1
    return ,([byte[]]$result[0..($offset - 1)])
}

function Invoke-Phase34Hop11([Net.Sockets.UdpClient]$peerUdp,
                              [int]$rxPort, [int]$timeoutSeconds,
                              [System.Diagnostics.Process]$process,
                              [System.IO.Stream]$stream,
                              [IO.FileStream]$serialLog,
                              [Text.StringBuilder]$text,
                              [byte[]]$receiveBuffer,
                              [IO.StreamWriter]$injectionLog,
                              [byte[]]$guestMac,
                              [byte[]]$hostMac,
                              [byte[]]$guestIp,
                              [byte[]]$hostIp,
                              [int]$hop,
                              [string]$hostname,
                              [int]$serverPort,
                               [string]$path,
                               [bool]$negativeControl,
                               [bool]$resourceProof = $false) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $dnsQueryFrame = Receive-AnyDns20Frame $peerUdp $timeoutSeconds $hostname
    $dnsQueryPayload = Get-DnsPayload20 $dnsQueryFrame
    $expectedQuestion = New-Phase34DnsQuestion11 $hostname
    Require11 ($null -ne $dnsQueryPayload -and
        $dnsQueryPayload.Length -eq 12 + $expectedQuestion.Length -and
        (Bytes-Equal16 $dnsQueryPayload 12 $expectedQuestion)) `
        "Phase 34 DNS query was invalid for $hostname."
    Write-Phase20Frame $injectionLog ('phase34_dns_query_{0}' -f $hop) $dnsQueryFrame
    $dnsResponse = New-DnsResponse20 $dnsQueryPayload 'valid'
    $dnsResponseFrame = New-Ipv4Udp18 $guestMac $hostMac $hostIp $guestIp `
        (New-UdpDatagram18 53 15200 $hostIp $guestIp $dnsResponse) (0x5F20 + $hop)
    Require11 ($peerUdp.Send($dnsResponseFrame, $dnsResponseFrame.Length,
        '127.0.0.1', $rxPort) -eq $dnsResponseFrame.Length) `
        "Phase 34 DNS response send was short for $hostname."
    Write-Phase20Frame $injectionLog ('phase34_dns_response_{0}' -f $hop) $dnsResponseFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_DNS_SUCCESS' $deadline `
        $process $stream $serialLog $text $receiveBuffer

    $clientPort = 15221
    [uint32]$clientIsn = 0x22000001 + ($hop * 0x100)
    [uint32]$serverIsn = 0x34010001 + ($hop * 0x100)
    [uint32]$clientNext = $clientIsn + 1
    [uint32]$serverNext = $serverIsn + 1
    $peerMac = $hostMac; $guestMacLocal = $guestMac
    $observedSyn = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
        ('Phase 34 hop {0} managed SYN' -f $hop)
    $syn = New-TcpSegment22 $clientPort $serverPort $clientIsn 0 2 `
        $guestIp $hostIp ([byte[]]@()) $true
    Require11 (Test-TcpFrame22 $observedSyn $peerMac $guestMacLocal $guestIp $hostIp `
        $clientPort $serverPort $clientIsn 0 2 ([byte[]]@()) $true) `
        "Phase 34 SYN validation failed for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_managed_syn_{0}' -f $hop) $observedSyn
    $synAck = New-TcpSegment22 $serverPort $clientPort $serverIsn $clientNext 0x12 `
        $hostIp $guestIp ([byte[]]@()) $true
    $synAckFrame = New-Ipv4Tcp22 $guestMacLocal $peerMac $hostIp $guestIp $synAck (0x5F30 + $hop)
    Require11 ($peerUdp.Send($synAckFrame, $synAckFrame.Length,
        '127.0.0.1', $rxPort) -eq $synAckFrame.Length) `
        "Phase 34 SYNACK send was short for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_peer_synack_{0}' -f $hop) $synAckFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_TCP_CONNECTED' $deadline `
        $process $stream $serialLog $text $receiveBuffer
    $observedHandshakeAck = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
        ('Phase 34 hop {0} managed handshake ACK' -f $hop)
    Require11 (Test-TcpFrame22 $observedHandshakeAck $peerMac $guestMacLocal $guestIp $hostIp `
        $clientPort $serverPort $clientNext $serverNext 0x10 ([byte[]]@()) $false) `
        "Phase 34 handshake ACK validation failed for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_managed_handshake_ack_{0}' -f $hop) $observedHandshakeAck
    $observedHello = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
        ('Phase 34 hop {0} ClientHello' -f $hop)
    $helloPayload = Get-Phase32TcpPayload11 $observedHello
    Require11 ($helloPayload.Length -ge 43 -and $helloPayload[0] -eq 22) `
        "Phase 34 ClientHello was invalid for hop $hop."
    $helloLength = Read-U16-Phase17 $helloPayload 3
    Require11 ($helloLength + 5 -eq $helloPayload.Length) `
        "Phase 34 ClientHello record was truncated for hop $hop."
    $helloBody = [byte[]]$helloPayload[5..($helloPayload.Length - 1)]
    Require11 (([Text.Encoding]::ASCII.GetString($helloBody)).Contains($hostname)) `
        "Phase 34 SNI hostname was not present for hop $hop."
    $expectedHello = if ($hostname -eq 'www.example.com') {
        New-Phase32PlainTlsRecord11 22 (Get-Phase32FixtureBytes11 'ClientHello')
    } else { $null }
    if ($null -ne $expectedHello) {
        Require11 (Test-TcpFrame22 $observedHello $peerMac $guestMacLocal $guestIp $hostIp `
            $clientPort $serverPort $clientNext $serverNext 0x18 $expectedHello $false) `
            "Phase 34 ClientHello framing changed for hop $hop."
    }
    Write-Phase22Frame $injectionLog ('phase34_clienthello_{0}' -f $hop) $observedHello
    [uint32]$clientNext = $clientNext + $helloPayload.Length
    Send-Phase32PeerAck11 $peerUdp $rxPort $injectionLog $peerMac $guestMacLocal `
        $guestIp $hostIp $clientPort $serverPort $serverNext $clientNext (0x5F32 + $hop)

    $serverRecordNames = @('ServerHelloRecord', 'CertificateRecord0',
        'CertificateRecord1', 'CertificateRecord2', 'CertificateRecord3',
        'CertificateRecord4', 'CertificateRecord5', 'CertificateRecord6',
        'CertificateRecord7', 'CertificateRecord8', 'CertificateRecord9',
        'ServerKeyExchangeRecord', 'ServerHelloDoneRecord')
    $recordIndex = 0
    foreach ($recordName in $serverRecordNames) {
        $record = Get-Phase32FixtureBytes11 $recordName
        $chunks = @()
        $chunks += ,([byte[]]$record[0..([Math]::Min(1, $record.Length - 1))])
        if ($record.Length -gt 2) {
            $chunks += ,([byte[]]$record[2..([Math]::Min(12, $record.Length - 1))])
        }
        if ($record.Length -gt 13) {
            $chunks += ,([byte[]]$record[13..($record.Length - 1)])
        }
        foreach ($chunk in $chunks) {
            Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac `
                $guestMacLocal $guestIp $hostIp $clientPort $serverPort $serverNext `
                $clientNext ([byte[]]$chunk) (0x6000 + $hop * 0x100 + $recordIndex) `
                $timeoutSeconds (-not ($negativeControl -and $hostname -ne 'www.example.com'))
            [uint32]$serverNext = $serverNext + $chunk.Length
            if ($negativeControl -and $hostname -ne 'www.example.com') {
                Start-Sleep -Milliseconds 25
            }
            ++$recordIndex
        }
    }

    if ($negativeControl -and $hostname -ne 'www.example.com') {
        # The static www.example.com certificate is intentionally offered for
        # bad.example.net.  The guest must reject it before sending its TLS
        # flight for this hop.
        Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE34_START_FAILED' $deadline `
            $process $stream $serialLog $text $receiveBuffer
        return 'EXPECTED_FAILURE'
    }

    $observedFlight = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
        ('Phase 34 hop {0} client TLS flight' -f $hop)
    $flightPayload = Get-Phase32TcpPayload11 $observedFlight
    Require11 (Test-TcpFrame22 $observedFlight $peerMac $guestMacLocal $guestIp $hostIp `
        $clientPort $serverPort $clientNext $serverNext 0x18 $flightPayload $false) `
        "Phase 34 client TLS flight framing failed for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_client_tls_flight_{0}' -f $hop) $observedFlight
    [uint32]$clientNext = $clientNext + $flightPayload.Length

    $keyBlock = $null
    $serverFinished = $null
    if ($hostname -eq 'www.example.com') {
        $keyBlock = Get-Phase32FixtureBytes11 'KeyBlock'
        $clientKey = [byte[]]$keyBlock[0..15]
        $clientIv = [byte[]]$keyBlock[32..35]
        $clientKeyExchange = New-Phase32PlainTlsRecord11 22 (Get-Phase32FixtureBytes11 'ClientKeyExchange')
        $changeCipherSpec = Get-Phase32FixtureBytes11 'ChangeCipherSpec'
        $clientFinishedRecord = Get-Phase32FixtureBytes11 'ClientFinishedRecord'
        $expectedFlight = New-Object byte[] ($clientKeyExchange.Length +
            $changeCipherSpec.Length + $clientFinishedRecord.Length)
        [Array]::Copy($clientKeyExchange, 0, $expectedFlight, 0, $clientKeyExchange.Length)
        [Array]::Copy($changeCipherSpec, 0, $expectedFlight, $clientKeyExchange.Length, $changeCipherSpec.Length)
        [Array]::Copy($clientFinishedRecord, 0, $expectedFlight,
            $clientKeyExchange.Length + $changeCipherSpec.Length,
            $clientFinishedRecord.Length)
        Require11 (Bytes-Equal16 $flightPayload 0 $expectedFlight) `
            "Phase 34 static client TLS flight changed for hop $hop."
        $serverFinished = Get-Phase32FixtureBytes11 'ServerFinishedRecord'
    } else {
        $dynamic = New-Phase34DynamicServerFlight11 $helloBody $flightPayload
        $keyBlock = $dynamic.KeyBlock
        $serverFinished = $dynamic.ServerFinished
        $clientKey = [byte[]]$keyBlock[0..15]
        $clientIv = [byte[]]$keyBlock[32..35]
    }

    $serverCcs = Get-Phase32FixtureBytes11 'ChangeCipherSpec'
    Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMacLocal `
        $guestIp $hostIp $clientPort $serverPort $serverNext $clientNext $serverCcs `
        (0x6100 + $hop) $timeoutSeconds
    [uint32]$serverNext = $serverNext + $serverCcs.Length
    Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMacLocal `
        $guestIp $hostIp $clientPort $serverPort $serverNext $clientNext $serverFinished `
        (0x6101 + $hop) $timeoutSeconds
    [uint32]$serverNext = $serverNext + $serverFinished.Length

    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REQUEST_ENCRYPTED_SENT' `
        $deadline $process $stream $serialLog $text $receiveBuffer
    $observedRequest = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
        ('Phase 34 hop {0} encrypted HTTP request' -f $hop)
    $requestPayload = Get-Phase32TcpPayload11 $observedRequest
    $request = Decrypt-Phase32ApplicationRecord11 $requestPayload 1 $clientKey $clientIv
    $expectedHost = if ($serverPort -eq 443) { $hostname } else { "$hostname`:$serverPort" }
    $expectedRequestText = "GET $path HTTP/1.1`r`nHost: $expectedHost`r`nConnection: close`r`n`r`n"
    Require11 ($null -ne $request -and
        [Text.Encoding]::ASCII.GetString($request) -eq $expectedRequestText) `
        "Phase 34 encrypted request was invalid for hop $hop."
    Require11 (Test-TcpFrame22 $observedRequest $peerMac $guestMacLocal $guestIp $hostIp `
        $clientPort $serverPort $clientNext $serverNext 0x18 $requestPayload $false) `
        "Phase 34 request TCP framing failed for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_encrypted_http_request_{0}' -f $hop) $observedRequest
    [uint32]$clientNext = $clientNext + $requestPayload.Length
    Send-Phase32PeerAck11 $peerUdp $rxPort $injectionLog $peerMac $guestMacLocal `
        $guestIp $hostIp $clientPort $serverPort $serverNext $clientNext (0x6102 + $hop)

    if ($resourceProof) {
        $headerBytes = [Text.Encoding]::ASCII.GetBytes(
            "HTTP/1.1 200 OK`r`nContent-Length: 16884`r`nContent-Type: application/octet-stream`r`nConnection: close`r`n`r`n")
        $responseBytes = New-Object byte[] ($headerBytes.Length + 16884)
        $headerBytes.CopyTo($responseBytes, 0)
        for ($index = 0; $index -lt 16884; ++$index) {
            $responseBytes[$headerBytes.Length + $index] = [byte](($index * 31 + 7) -band 0xFF)
        }
    } elseif ($negativeControl -and $hop -eq 0) {
        $responseText = "HTTP/1.1 302 Found`r`nLocation: https://bad.example.net/final`r`nContent-Length: 8`r`nConnection: close`r`n`r`nredirect"
    } elseif ($hop -eq 0) {
        $responseText = "HTTP/1.1 302 Found`r`nLocation: /phase34/step2`r`nContent-Length: 8`r`nConnection: close`r`n`r`nredirect"
    } elseif ($hop -eq 1) {
        $responseText = "HTTP/1.1 301 Moved Permanently`r`nLocation: next`r`nContent-Length: 8`r`nConnection: close`r`n`r`nredirect"
    } elseif ($hop -eq 2) {
        $responseText = "HTTP/1.1 307 Temporary Redirect`r`nLocation: https://other.example.com:8443/phase34/final`r`nContent-Length: 8`r`nConnection: close`r`n`r`nredirect"
    } else {
        $responseText = "HTTP/1.1 200 OK`r`nContent-Length: 21`r`nConnection: close`r`n`r`nphase34-redirect-pass"
    }
    if (-not $resourceProof) {
        $responseBytes = [Text.Encoding]::ASCII.GetBytes($responseText)
    }
    $responseSequence = [UInt64]1
    $responseChunk = 0
    for ($offset = 0; $offset -lt $responseBytes.Length;) {
            # Keep the authoritative resource well above the 1 KiB delivery
            # window while using bounded TLS records that fit one TCP payload.
            # The HTTP parser still fragments the decoded resource into its
            # fixed delivery windows; this only avoids making the fixture's
            # application-record count dominate the proof.
        $count = if ($resourceProof) {
            [Math]::Min(480, $responseBytes.Length - $offset)
        } else {
            [Math]::Min(11, $responseBytes.Length - $offset)
        }
        $plain = [byte[]]$responseBytes[$offset..($offset + $count - 1)]
        $record = New-Phase34TlsRecord11 $responseSequence 23 $plain `
            ([byte[]]$keyBlock[16..31]) ([byte[]]$keyBlock[36..39])
        Send-Phase32ServerTcpData11 $peerUdp $rxPort $injectionLog $peerMac $guestMacLocal `
            $guestIp $hostIp $clientPort $serverPort $serverNext $clientNext $record `
            (0x6200 + $hop * 0x100 + $responseChunk) $timeoutSeconds
        [uint32]$serverNext = $serverNext + $record.Length
        if ($resourceProof) {
            $bodyProgress = [Math]::Min(16884,
                [Math]::Max(0, $offset + $count - $headerBytes.Length))
            if ($bodyProgress -gt 0) {
                Wait-Marker11 ('GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PROGRESS=0x{0:X16}' -f $bodyProgress) `
                    $deadline $process $stream $serialLog $text $receiveBuffer
            }
        }
        $offset += $count
        ++$responseSequence
        ++$responseChunk
    }
    if ($resourceProof) {
        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_BODY_RECEIVED' $deadline `
            $process $stream $serialLog $text $receiveBuffer
    } elseif ($hop -lt 3 -or $negativeControl) {
        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_LOCATION_PARSED' $deadline `
            $process $stream $serialLog $text $receiveBuffer
    } else {
        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_RECEIVED' $deadline `
            $process $stream $serialLog $text $receiveBuffer
    }

    $peerFin = New-TcpSegment22 $serverPort $clientPort $serverNext $clientNext 0x11 `
        $hostIp $guestIp ([byte[]]@()) $false
    $peerFinFrame = New-Ipv4Tcp22 $guestMacLocal $peerMac $hostIp $guestIp $peerFin (0x6300 + $hop)
    Require11 ($peerUdp.Send($peerFinFrame, $peerFinFrame.Length,
        '127.0.0.1', $rxPort) -eq $peerFinFrame.Length) `
        "Phase 34 peer FIN send was short for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_peer_fin_{0}' -f $hop) $peerFinFrame
    Start-Sleep -Milliseconds 10
    Require11 ($peerUdp.Send($peerFinFrame, $peerFinFrame.Length,
        '127.0.0.1', $rxPort) -eq $peerFinFrame.Length) `
        "Phase 34 peer FIN retry send was short for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_peer_fin_retry_{0}' -f $hop) $peerFinFrame
    [uint32]$finNext = $clientNext + 1
    [uint32]$peerFinNext = $serverNext + 1
    $firstCloseFrame = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
        ('Phase 34 hop {0} close response' -f $hop)
    $firstCloseFlags = $firstCloseFrame[47]
    if ($firstCloseFlags -eq 0x11) {
        $actualCloseSequence = Read-U32-Phase19 $firstCloseFrame 38
        $actualCloseAcknowledgment = Read-U32-Phase19 $firstCloseFrame 42
        if ($actualCloseAcknowledgment -eq $serverNext) {
            Require11 (Test-TcpFrame22 $firstCloseFrame $peerMac $guestMacLocal $guestIp $hostIp `
                $clientPort $serverPort $clientNext $serverNext 0x11 ([byte[]]@()) $false) `
                "Phase 34 managed FIN validation failed for hop $hop."
            $managedFinAck = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
                ('Phase 34 hop {0} peer FIN ACK' -f $hop)
            Require11 (Test-TcpFrame22 $managedFinAck $peerMac $guestMacLocal $guestIp $hostIp `
                $clientPort $serverPort $finNext $peerFinNext 0x10 ([byte[]]@()) $false) `
                "Phase 34 peer FIN ACK validation failed for hop $hop."
            Write-Phase22Frame $injectionLog ('phase34_managed_fin_ack_{0}' -f $hop) $managedFinAck
        } else {
            Require11 (Test-TcpFrame22 $firstCloseFrame $peerMac $guestMacLocal $guestIp $hostIp `
                $clientNext $peerFinNext 0x11 ([byte[]]@()) $false) `
                "Phase 34 managed FIN acknowledgement was invalid for hop $hop."
        }
        Write-Phase22Frame $injectionLog ('phase34_managed_fin_{0}' -f $hop) $firstCloseFrame
    } else {
        [uint32]$firstSequence = Read-U32-Phase19 $firstCloseFrame 38
        [uint32]$closeAcknowledgment = $peerFinNext
        Require11 (($firstSequence -eq $clientNext -or $firstSequence -eq $finNext) -and
            (Test-TcpFrame22 -frame $firstCloseFrame -destinationMac $peerMac `
                -sourceMac $guestMacLocal -sourceIp $guestIp -destinationIp $hostIp `
                -sourcePort $clientPort -destinationPort $serverPort `
                -sequence ([uint32]$firstSequence) `
                -acknowledgment ([uint32]$closeAcknowledgment) `
                -flags ([byte]0x10) -payload ([byte[]]@()) -requireMss $false)) `
            "Phase 34 managed FIN ACK validation failed for hop $hop."
        Write-Phase22Frame $injectionLog ('phase34_managed_fin_ack_{0}' -f $hop) $firstCloseFrame
        if ($firstSequence -eq $clientNext) {
            $managedFin = Receive-AnyPhase22TcpFrame $peerUdp $timeoutSeconds `
                ('Phase 34 hop {0} managed FIN' -f $hop)
            [uint32]$managedFinSequence = $clientNext
            [uint32]$managedFinAcknowledgment = $peerFinNext
            Require11 (Test-TcpFrame22 -frame $managedFin -destinationMac $peerMac `
                -sourceMac $guestMacLocal -sourceIp $guestIp -destinationIp $hostIp `
                -sourcePort $clientPort -destinationPort $serverPort `
                -sequence $managedFinSequence `
                -acknowledgment $managedFinAcknowledgment `
                -flags ([byte]0x11) -payload ([byte[]]@()) -requireMss $false) `
                "Phase 34 managed FIN was invalid for hop $hop."
            Write-Phase22Frame $injectionLog ('phase34_managed_fin_{0}' -f $hop) $managedFin
        }
    }
    $finalAck = New-TcpSegment22 $serverPort $clientPort $peerFinNext $finNext 0x10 `
        $hostIp $guestIp ([byte[]]@()) $false
    $finalAckFrame = New-Ipv4Tcp22 $guestMacLocal $peerMac $hostIp $guestIp $finalAck (0x6301 + $hop)
    Require11 ($peerUdp.Send($finalAckFrame, $finalAckFrame.Length,
        '127.0.0.1', $rxPort) -eq $finalAckFrame.Length) `
        "Phase 34 final ACK send was short for hop $hop."
    Write-Phase22Frame $injectionLog ('phase34_final_ack_{0}' -f $hop) $finalAckFrame
    if ($resourceProof) {
        # The resource proof has one final response and no redirect hop.
    } elseif ($hop -lt 3) {
        Wait-Marker11 ('GXOS_NET10:MANAGED_HTTPS_PHASE34_REDIRECT_FOLLOWED=0x{0:X16}' -f ($hop + 1)) `
            $deadline $process $stream $serialLog $text $receiveBuffer
    }
    return 'PASS'
}

function Invoke-Phase34HttpsExchange11([Net.Sockets.UdpClient]$peerUdp,
                                       [int]$rxPort, [int]$timeoutSeconds,
                                       [System.Diagnostics.Process]$process,
                                       [System.IO.Stream]$stream,
                                       [IO.FileStream]$serialLog,
                                       [Text.StringBuilder]$text,
                                       [byte[]]$receiveBuffer,
                                       [IO.StreamWriter]$injectionLog,
                                       [byte[]]$guestMacBytes,
                                       [byte[]]$hostMacBytes,
                                       [byte[]]$guestIpBytes,
                                       [byte[]]$hostIpBytes,
                                       [bool]$negativeControl,
                                       [bool]$resourceProof = $false) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
    $discoverFrame = Receive-AnyPhase19Frame $peerUdp $timeoutSeconds 'Phase 34 DHCPDISCOVER'
    $discoverPayload = Get-DhcpPayload19 $discoverFrame
    Require11 ($null -ne $discoverPayload) 'Phase 34 DHCPDISCOVER is not IPv4/UDP.'
    Write-Phase20Frame $injectionLog 'phase34_dhcpdiscover' $discoverFrame
    $broadcastMac = [byte[]](0xFF,0xFF,0xFF,0xFF,0xFF,0xFF)
    $broadcastIp = [byte[]](255,255,255,255)
    $offerPayload = New-DhcpReply19 $discoverPayload 2 $guestIpBytes $hostIpBytes $true $true $true
    $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIp `
        (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIp $offerPayload) 0x5F21
    Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length,'127.0.0.1',$rxPort) -eq $offerFrame.Length) 'Phase 34 DHCPOFFER send was short.'
    Write-Phase20Frame $injectionLog 'phase34_dhcpoffer' $offerFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' $deadline $process $stream $serialLog $text $receiveBuffer
    $requestFrame = Receive-AnyPhase19Frame $peerUdp $timeoutSeconds 'Phase 34 DHCPREQUEST'
    $requestPayload = Get-DhcpPayload19 $requestFrame
    Require11 ($null -ne $requestPayload) 'Phase 34 DHCPREQUEST is not IPv4/UDP.'
    Write-Phase20Frame $injectionLog 'phase34_dhcprequest' $requestFrame
    $ackPayload = New-DhcpReply19 $requestPayload 5 $guestIpBytes $hostIpBytes $true $true $true
    $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes $hostIpBytes $broadcastIp `
        (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIp $ackPayload) 0x5F22
    Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length,'127.0.0.1',$rxPort) -eq $ackFrame.Length) 'Phase 34 DHCPACK send was short.'
    Write-Phase20Frame $injectionLog 'phase34_dhcpack' $ackFrame
    Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_CONFIGURED' $deadline $process $stream $serialLog $text $receiveBuffer
    $zeroMac = [byte[]](0,0,0,0,0,0)
    $arpRequest = New-Phase16ArpFrame11 $broadcastMac $guestMacBytes 1 $guestIpBytes $zeroMac $hostIpBytes
    $observedArp = Receive-ExpectedPhase16Frame11 $peerUdp $arpRequest 'Phase 34 DNS ARP request' $timeoutSeconds
    Write-Phase20Frame $injectionLog 'phase34_dns_arp_request' $observedArp
    $arpReply = New-Phase16ArpFrame11 $guestMacBytes $hostMacBytes 2 $hostIpBytes $guestMacBytes $guestIpBytes
    Require11 ($peerUdp.Send($arpReply,$arpReply.Length,'127.0.0.1',$rxPort) -eq $arpReply.Length) 'Phase 34 DNS ARP reply send was short.'
    Write-Phase20Frame $injectionLog 'phase34_dns_arp_reply' $arpReply
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_REQUEST_STARTED' $deadline $process $stream $serialLog $text $receiveBuffer

    $hosts = @('www.example.com','www.example.com','www.example.com','other.example.com')
    $ports = @(443,443,443,8443)
    $paths = @('/phase34/start','/phase34/step2','/phase34/next','/phase34/final')
    $hopCount = if ($resourceProof) { 1 } elseif ($negativeControl) { 2 } else { 4 }
    if ($resourceProof) {
        $hosts = @('www.example.com'); $ports = @(443); $paths = @('/phase39/resource')
    } elseif ($negativeControl) { $hosts = @('www.example.com','bad.example.net'); $ports = @(443,443); $paths = @('/phase34/start','/final') }
    for ($hop = 0; $hop -lt $hopCount; ++$hop) {
        $hopResult = Invoke-Phase34Hop11 $peerUdp $rxPort $timeoutSeconds $process $stream $serialLog $text $receiveBuffer $injectionLog `
            $guestMacBytes $hostMacBytes $guestIpBytes $hostIpBytes $hop $hosts[$hop] $ports[$hop] $paths[$hop] $negativeControl $resourceProof
        if ($negativeControl -and $hopResult -eq 'EXPECTED_FAILURE') {
            $transcript = $text.ToString()
            Require11 (!$transcript.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS') -and
                !$transcript.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE34_PASS')) `
                'Phase 34 negative control emitted a success marker.'
            return 'NEGATIVE_PASS_PHASE34'
        }
        if ($hop -lt $hopCount - 1) {
            Wait-Marker11 ('GXOS_NET10:MANAGED_HTTPS_PHASE34_REDIRECT_FOLLOWED=0x{0:X16}' -f ($hop + 1)) `
                $deadline $process $stream $serialLog $text $receiveBuffer
        }
    }
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_VERIFIED' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_TEARDOWN_COMPLETE' $deadline $process $stream $serialLog $text $receiveBuffer
    Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
    if ($resourceProof) {
        Wait-Marker11 'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
        Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE39_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
    } else {
        Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE34_PASS' $deadline $process $stream $serialLog $text $receiveBuffer
    }
    return 'PASS_PHASE34'
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

function Compute-TcpChecksum22([byte[]]$sourceIp, [byte[]]$destinationIp,
                                [byte[]]$tcp) {
    [uint32]$sum = 0
    foreach ($offset in @(0, 2)) {
        $sum += (([uint32]$sourceIp[$offset] -shl 8) -bor [uint32]$sourceIp[$offset + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
        $sum += (([uint32]$destinationIp[$offset] -shl 8) -bor [uint32]$destinationIp[$offset + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum += 6
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum += $tcp.Length
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    for ($index = 0; $index + 1 -lt $tcp.Length; $index += 2) {
        $sum += (([uint32]$tcp[$index] -shl 8) -bor [uint32]$tcp[$index + 1])
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    if (($tcp.Length -band 1) -ne 0) {
        $sum += [uint32]$tcp[$tcp.Length - 1] -shl 8
        $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    }
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    $sum = ($sum -band 0xFFFF) + ($sum -shr 16)
    return [uint16]((-bnot [int]$sum) -band 0xFFFF)
}

function New-TcpSegment22([int]$sourcePort, [int]$destinationPort,
                           [uint32]$sequence, [uint32]$acknowledgment,
                           [byte]$flags, [byte[]]$sourceIp,
                           [byte[]]$destinationIp, [byte[]]$payload,
                           [bool]$advertiseMss = $false) {
    $optionLength = if ($advertiseMss) { 4 } else { 0 }
    $options = if ($advertiseMss) { [byte[]](2, 4, 2, 0) } else { $null }
    $payloadLength = if ($null -eq $payload) { 0 } else { $payload.Length }
    $tcp = New-Object byte[] (20 + $optionLength + $payloadLength)
    Write-U16-Phase17 $tcp 0 $sourcePort
    Write-U16-Phase17 $tcp 2 $destinationPort
    Write-U32-Phase19 $tcp 4 $sequence
    Write-U32-Phase19 $tcp 8 $acknowledgment
    $tcp[12] = [byte]((($tcp.Length - $payloadLength) / 4) -shl 4)
    $tcp[13] = $flags
    Write-U16-Phase17 $tcp 14 512
    if ($optionLength -ne 0) {
        [Array]::Copy($options, 0, $tcp, 20, $optionLength)
    }
    if ($payloadLength -ne 0) {
        [Array]::Copy($payload, 0, $tcp, 20 + $optionLength, $payloadLength)
    }
    Write-U16-Phase17 $tcp 16 (Compute-TcpChecksum22 $sourceIp $destinationIp $tcp)
    return $tcp
}

function New-Ipv4Tcp22([byte[]]$destinationMac, [byte[]]$sourceMac,
                        [byte[]]$sourceIp, [byte[]]$destinationIp,
                        [byte[]]$tcp, [int]$identification = 0x2A00) {
    $totalLength = 20 + $tcp.Length
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
    $frame[$ip + 9] = 6
    [Array]::Copy($sourceIp, 0, $frame, $ip + 12, 4)
    [Array]::Copy($destinationIp, 0, $frame, $ip + 16, 4)
    Write-U16-Phase17 $frame ($ip + 10) (Compute-Checksum-Phase17 $frame $ip 20)
    [Array]::Copy($tcp, 0, $frame, $ip + 20, $tcp.Length)
    return $frame
}

function Test-TcpFrame22([byte[]]$frame, [byte[]]$destinationMac,
                         [byte[]]$sourceMac, [byte[]]$sourceIp,
                         [byte[]]$destinationIp, [int]$sourcePort,
                         [int]$destinationPort, [uint32]$sequence,
                         [uint32]$acknowledgment, [byte]$flags,
                         [byte[]]$payload, [bool]$requireMss = $false) {
    $optionLength = if ($requireMss) { 4 } else { 0 }
    $totalLength = 20 + 20 + $payload.Length + $optionLength
    $wireLength = [Math]::Max(60, 14 + $totalLength)
    if ($frame.Length -ne $wireLength -or
        !(Bytes-Equal16 $frame 0 $destinationMac) -or
        !(Bytes-Equal16 $frame 6 $sourceMac) -or $frame[12] -ne 8 -or
        $frame[13] -ne 0 -or $frame[14] -ne 0x45 -or
        (Read-U16-Phase17 $frame 16) -ne $totalLength -or
        $frame[23] -ne 6 -or !(Bytes-Equal16 $frame 26 $sourceIp) -or
        !(Bytes-Equal16 $frame 30 $destinationIp) -or
        (Compute-Checksum-Phase17 $frame 14 20) -ne 0) { return $false }
    $tcp = 34
    $headerLength = ($frame[$tcp + 12] -shr 4) * 4
    if ($headerLength -lt 20 -or $headerLength -gt 60 -or
        $tcp + $headerLength + $payload.Length -gt $frame.Length -or
        (Read-U16-Phase17 $frame $tcp) -ne $sourcePort -or
        (Read-U16-Phase17 $frame ($tcp + 2)) -ne $destinationPort -or
        (Read-U32-Phase19 $frame ($tcp + 4)) -ne $sequence -or
        (Read-U32-Phase19 $frame ($tcp + 8)) -ne $acknowledgment -or
        $frame[$tcp + 13] -ne $flags -or
        (Read-U16-Phase17 $frame ($tcp + 14)) -ne 512) { return $false }
    $tcpBytes = [byte[]]$frame[$tcp..($tcp + $totalLength - 21)]
    if ((Compute-TcpChecksum22 $sourceIp $destinationIp $tcpBytes) -ne 0) { return $false }
    if ($requireMss -and ($headerLength -ne 24 -or $frame[$tcp + 20] -ne 2 -or
                           $frame[$tcp + 21] -ne 4 -or
                           (Read-U16-Phase17 $frame ($tcp + 22)) -ne 512)) { return $false }
    if ($payload.Length -ne 0 -and
        !(Bytes-Equal16 $frame ($tcp + $headerLength) $payload)) { return $false }
    return $true
}

function Repair-TcpChecksum22([byte[]]$frame, [byte[]]$sourceIp,
                               [byte[]]$destinationIp) {
    if ($frame.Length -lt 54 -or $frame[14] -ne 0x45) {
        throw 'Cannot repair TCP checksum on a non-minimal IPv4 frame.'
    }
    $ipLength = Read-U16-Phase17 $frame 16
    $tcpLength = $ipLength - 20
    if ($tcpLength -lt 20 -or 34 + $tcpLength -gt $frame.Length) {
        throw 'Cannot repair TCP checksum on a truncated frame.'
    }
    $tcp = [byte[]]$frame[34..(33 + $tcpLength)]
    $tcp[16] = 0; $tcp[17] = 0
    Write-U16-Phase17 $frame 50 (Compute-TcpChecksum22 $sourceIp $destinationIp $tcp)
}

function Receive-AnyPhase22TcpFrame([Net.Sockets.UdpClient]$peerUdp,
                                     [int]$timeoutSeconds, [string]$name) {
    $peerUdp.Client.ReceiveTimeout = 1000
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $remote = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
            $frame = $peerUdp.Receive([ref]$remote)
            if ($frame.Length -ge 54 -and $frame[12] -eq 8 -and
                $frame[13] -eq 0 -and $frame[23] -eq 6) {
                return ,$frame
            }
        } catch [Net.Sockets.SocketException] { }
    }
    throw "Timed out waiting for $name TCP frame."
}

function Receive-ExpectedPhase22TcpFrame([Net.Sockets.UdpClient]$peerUdp,
                                         [int]$timeoutSeconds, [string]$name,
                                         [byte[]]$destinationMac,
                                         [byte[]]$sourceMac,
                                         [byte[]]$sourceIp,
                                         [byte[]]$destinationIp,
                                         [int]$sourcePort,
                                         [int]$destinationPort,
                                         [uint32]$sequence,
                                         [uint32]$acknowledgment,
                                         [byte]$flags,
                                         [byte[]]$payload,
                                         [bool]$requireMss) {
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
        if ($frame.Length -ge 54 -and $frame[12] -eq 8 -and
            $frame[13] -eq 0 -and $frame[23] -eq 6) {
            if (Test-TcpFrame22 $frame $destinationMac $sourceMac $sourceIp `
                    $destinationIp $sourcePort $destinationPort $sequence `
                    $acknowledgment $flags $payload $requireMss) {
                return ,$frame
            }
            ++$seen
            if ($seen -gt 64) {
                throw "Too many unexpected TCP frames while waiting for $name."
            }
        }
    }
    throw "Timed out waiting for exact guest $name TCP frame."
}

function Write-Phase22Frame([IO.StreamWriter]$log, [string]$name,
                            [byte[]]$frame) {
    $hex = ([BitConverter]::ToString($frame)).Replace('-', '')
    $log.WriteLine(('MANAGED_PHASE22_{0}=PASS length={1} destination={2} source={3} ethertype={4} frame_sha256={5}' -f
        $name.ToUpperInvariant(), $frame.Length, $hex.Substring(0, 12),
        $hex.Substring(12, 12), ('{0:X4}' -f (Read-U16-Phase17 $frame 12)),
        (Hash-Phase17Frame $frame)))
    $log.WriteLine(('MANAGED_PHASE22_{0}_FRAME_HEX={1}' -f
        $name.ToUpperInvariant(), $hex))
    $log.Flush()
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
    Require11 ($EnablePhase15Rx -and
        ($Phase15NetworkBackend -eq 'dgram' -or $EnableManagedKernelPhase35)) `
        'Phase 15 QEMU receive tracing requires an active RX backend.'
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
            if (-not $Phase15KeepDefaultNic -and
                $Phase15NetworkBackend -eq 'dgram') {
                $arguments += @('-nic', 'none')
            }
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
        if ($EnablePhase26VirtioRng) {
            $arguments += @(
                '-object', 'rng-builtin,id=rng0',
                '-device', 'virtio-rng-pci-non-transitional,rng=rng0,addr=3,max-bytes=1024,period=1')
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
            if ($EnableManagedKernelPhase35) {
                $phase15Outcome = Wait-Phase35Outcome11 `
                    $deadline $process $stream $logStream $text $buffer
                if ($phase15Outcome -in @('A', 'B')) {
                    Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
                        $deadline $process $stream $logStream $text $buffer
                }
            } elseif ($EnablePhase15Rx -and $Phase15NetworkBackend -eq 'dgram') {
                Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_READY' $deadline $process $stream $logStream $text $buffer
                $macMatch = [regex]::Match($text.ToString(),
                    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})\s*([0-9A-Fa-f]{8})')
                Require11 $macMatch.Success 'Phase 15 did not publish the runtime e1000 MAC.'
                $destinationMac = ($macMatch.Groups[1].Value + $macMatch.Groups[2].Value)
                $injectOutput = if ($EnablePhase39Protocol -or $EnablePhase34Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $false $false $false $false $false $false $false $true)
                } elseif ($EnablePhase33Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $false $false $false $false $false $false $true)
                } elseif ($EnablePhase32Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $false $false $false $false $false $true)
                } elseif ($EnablePhase23Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $false $false $false $false $true)
                } elseif ($EnablePhase22Protocol) {
                    @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac `
                        '127.0.0.1' $false $false $false $false $false $true)
                } elseif ($EnablePhase21Protocol) {
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
                } elseif ($EnablePhase39Protocol -or $EnablePhase34Protocol -or $EnablePhase33Protocol -or $EnablePhase32Protocol -or $EnablePhase23Protocol -or $EnablePhase22Protocol -or $EnablePhase21Protocol -or $EnablePhase20Protocol -or $EnablePhase19Protocol -or $EnablePhase18Protocol -or $EnablePhase17Protocol -or
                          $EnablePhase16Protocol) {
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_COMPLETE' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_FRAME_OK' `
                        $deadline $process $stream $logStream $text $buffer
                    $guestMacBytes = New-MacBytes16 $destinationMac
                    $broadcastMac = [byte[]](0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
                    $hostMacBytes = [byte[]](0x02, 0x15, 0, 0, 0, 2)
                    $guestIpBytes = if ($EnablePhase39Protocol -or $EnablePhase34Protocol -or $EnablePhase33Protocol -or $EnablePhase32Protocol -or $EnablePhase23Protocol -or $EnablePhase22Protocol -or $EnablePhase21Protocol -or $EnablePhase20Protocol -or $EnablePhase19Protocol) {
                        [byte[]](10, 15, 0, 42)
                    } else { [byte[]](10, 15, 0, 1) }
                    $hostIpBytes = [byte[]](10, 15, 0, 2)
                    $broadcastIpBytes = [byte[]](255, 255, 255, 255)
                    if ($EnablePhase39Protocol) {
                        $phase15Outcome = Invoke-Phase34HttpsExchange11 `
                            $peerUdp $rxPort $TimeoutSeconds $process $stream `
                            $logStream $text $buffer $injectionLog `
                            $guestMacBytes $hostMacBytes $guestIpBytes $hostIpBytes `
                            $false $true
                    } elseif ($EnablePhase34Protocol) {
                        $phase15Outcome = Invoke-Phase34HttpsExchange11 `
                            $peerUdp $rxPort $TimeoutSeconds $process $stream `
                            $logStream $text $buffer $injectionLog `
                            $guestMacBytes $hostMacBytes $guestIpBytes $hostIpBytes `
                            ([bool]$EnablePhase34NegativeControl)
                    } elseif ($EnablePhase33Protocol) {
                        $phase15Outcome = Invoke-Phase33HttpsExchange11 `
                            $peerUdp $rxPort $TimeoutSeconds $process $stream `
                            $logStream $text $buffer $injectionLog `
                            $guestMacBytes $hostMacBytes $guestIpBytes $hostIpBytes `
                            ([bool]$EnablePhase33NegativeControl)
                    } elseif ($EnablePhase32Protocol) {
                        $phase15Outcome = Invoke-Phase32HttpsExchange11 `
                            $peerUdp $rxPort $TimeoutSeconds $process $stream `
                            $logStream $text $buffer $injectionLog `
                            $guestMacBytes $hostMacBytes $guestIpBytes $hostIpBytes `
                            ([bool]$EnablePhase32NegativeControl)
                    } elseif ($EnablePhase23Protocol) {
                        # Phase 23 uses the same bounded DHCP/DNS fixture as
                        # Phase 22, then validates one HTTP/1.1 GET and a
                        # deliberately segmented response over the same TCP
                        # tuple.
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $discoverFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds `
                            'Phase 23 DHCPDISCOVER'
                        $discoverPayload = Get-DhcpPayload19 $discoverFrame
                        Require11 ($null -ne $discoverPayload) 'Phase 23 DHCPDISCOVER is not IPv4/UDP.'
                        Write-Phase20Frame $injectionLog 'phase23_dhcpdiscover' $discoverFrame
                        $offerPayload = New-DhcpReply19 $discoverPayload 2 `
                            $guestIpBytes $hostIpBytes $true $true $true
                        $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $offerPayload) 0x2F21
                        Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length,
                            '127.0.0.1', $rxPort) -eq $offerFrame.Length) 'Phase 23 DHCPOFFER send was short.'
                        Write-Phase20Frame $injectionLog 'phase23_dhcpoffer' $offerFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $requestFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds 'Phase 23 DHCPREQUEST'
                        $requestPayload = Get-DhcpPayload19 $requestFrame
                        Require11 ($null -ne $requestPayload) 'Phase 23 DHCPREQUEST is not IPv4/UDP.'
                        Write-Phase20Frame $injectionLog 'phase23_dhcprequest' $requestFrame
                        $ackPayload = New-DhcpReply19 $requestPayload 5 `
                            $guestIpBytes $hostIpBytes $true $true $true
                        $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $ackPayload) 0x2F22
                        Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length,
                            '127.0.0.1', $rxPort) -eq $ackFrame.Length) 'Phase 23 DHCPACK send was short.'
                        Write-Phase20Frame $injectionLog 'phase23_dhcpack' $ackFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_CONFIGURED' `
                            $deadline $process $stream $logStream $text $buffer

                        $zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
                        $arpRequest = New-Phase16ArpFrame11 $broadcastMac $guestMacBytes 1 `
                            $guestIpBytes $zeroMac $hostIpBytes
                        $observedArp = Receive-ExpectedPhase16Frame11 $peerUdp $arpRequest `
                            'Phase 23 DNS ARP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'phase23_dns_arp_request' $observedArp
                        $arpReply = New-Phase16ArpFrame11 $guestMacBytes $hostMacBytes 2 `
                            $hostIpBytes $guestMacBytes $guestIpBytes
                        Require11 ($peerUdp.Send($arpReply, $arpReply.Length,
                            '127.0.0.1', $rxPort) -eq $arpReply.Length) 'Phase 23 DNS ARP reply send was short.'
                        Write-Phase20Frame $injectionLog 'phase23_dns_arp_reply' $arpReply

                        $dnsQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds 'phase23.test'
                        $dnsQueryPayload = Get-DnsPayload20 $dnsQueryFrame
                        $expectedQuestion = [byte[]](7,112,104,97,115,101,50,51,4,116,101,115,116,0,0,1,0,1)
                        Require11 ($null -ne $dnsQueryPayload -and $dnsQueryPayload.Length -eq 30 -and
                            (Bytes-Equal16 $dnsQueryPayload 12 $expectedQuestion)) 'Phase 23 DNS query name was invalid.'
                        Write-Phase20Frame $injectionLog 'phase23_dns_query' $dnsQueryFrame
                        $dnsResponse = New-DnsResponse20 $dnsQueryPayload 'valid'
                        $dnsResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes $dnsResponse) 0x2F23
                        Require11 ($peerUdp.Send($dnsResponseFrame, $dnsResponseFrame.Length,
                            '127.0.0.1', $rxPort) -eq $dnsResponseFrame.Length) 'Phase 23 DNS response send was short.'
                        Write-Phase20Frame $injectionLog 'phase23_dns_response' $dnsResponseFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_DNS_SUCCESS' `
                            $deadline $process $stream $logStream $text $buffer

                        $clientPort = 15221; $serverPort = 15222
                        # Phase 22 owns the bounded TCP connection's deterministic
                        # client ISN.  Phase 23 reuses that transport primitive.
                        [uint32]$clientIsn = 0x22000001; [uint32]$serverIsn = 0x23010001
                        $clientNext = [uint32]($clientIsn + 1); $serverNext = [uint32]($serverIsn + 1)
                        $expectedRequest = [Text.Encoding]::ASCII.GetBytes(
                            "GET /phase23 HTTP/1.1`r`nHost: phase23.test`r`nConnection: close`r`n`r`n")
                        $responseParts = New-Object object[] 3
                        $responseParts[0] = [byte[]]([Text.Encoding]::ASCII.GetBytes('HTTP/1.1 200'))
                        $responseParts[1] = [byte[]]([Text.Encoding]::ASCII.GetBytes(" OK`r`nContent-Length: 17`r`nConnection: close`r`nContent-Type: text/plain`r`n`r`nphase23-"))
                        $responseParts[2] = [byte[]]([Text.Encoding]::ASCII.GetBytes('http-pass'))
                        $peerMac = $hostMacBytes; $guestMac = $guestMacBytes
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_REQUEST_STARTED' $deadline $process $stream $logStream $text $buffer
                        $syn = New-TcpSegment22 $clientPort $serverPort $clientIsn 0 2 $guestIpBytes $hostIpBytes ([byte[]]@()) $true
                        $synFrame = New-Ipv4Tcp22 $peerMac $guestMac $guestIpBytes $hostIpBytes $syn 0x2A00
                        $observedSyn = Receive-ExpectedPhase17Frame $peerUdp $synFrame 'Phase 23 managed SYN' $TimeoutSeconds
                        Require11 (Test-TcpFrame22 $observedSyn $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientIsn 0 2 ([byte[]]@()) $true) 'Phase 23 SYN validation failed.'
                        Write-Phase22Frame $injectionLog 'phase23_managed_syn' $observedSyn
                        $synAck = New-TcpSegment22 $serverPort $clientPort $serverIsn $clientNext 0x12 $hostIpBytes $guestIpBytes ([byte[]]@()) $true
                        $synAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $synAck 0x2F31
                        Require11 ($peerUdp.Send($synAckFrame, $synAckFrame.Length, '127.0.0.1', $rxPort) -eq $synAckFrame.Length) 'Phase 23 SYNACK send was short.'
                        Write-Phase22Frame $injectionLog 'phase23_peer_synack' $synAckFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_TCP_CONNECTED' $deadline $process $stream $logStream $text $buffer
                        $handshakeAck = New-TcpSegment22 $clientPort $serverPort $clientNext $serverNext 0x10 $guestIpBytes $hostIpBytes ([byte[]]@()) $false
                        $observedHandshakeAck = Receive-AnyPhase22TcpFrame $peerUdp $TimeoutSeconds 'Phase 23 managed handshake ACK'
                        Require11 (Test-TcpFrame22 $observedHandshakeAck $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverNext 0x10 ([byte[]]@()) $false) 'Phase 23 handshake ACK validation failed.'
                        Write-Phase22Frame $injectionLog 'phase23_managed_handshake_ack' $observedHandshakeAck
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_REQUEST_SENT' $deadline $process $stream $logStream $text $buffer
                        $observedRequest = Receive-AnyPhase22TcpFrame $peerUdp $TimeoutSeconds 'Phase 23 HTTP request'
                        Require11 (Test-TcpFrame22 $observedRequest $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $clientNext $serverNext 0x18 $expectedRequest $false) 'Phase 23 HTTP request validation failed.'
                        Write-Phase22Frame $injectionLog 'phase23_http_request' $observedRequest
                        $requestNext = [uint32]($clientNext + $expectedRequest.Length)
                        $serverSequence = $serverNext
                        $peerAck = New-TcpSegment22 $serverPort $clientPort $serverSequence $requestNext 0x10 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
                        $peerAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $peerAck 0x2F32
                        Require11 ($peerUdp.Send($peerAckFrame, $peerAckFrame.Length, '127.0.0.1', $rxPort) -eq $peerAckFrame.Length) 'Phase 23 request ACK send was short.'
                        Write-Phase22Frame $injectionLog 'phase23_http_request_ack' $peerAckFrame
                        $partIndex = 0
                        for ($partIndex = 0; $partIndex -lt $responseParts.Length; ++$partIndex) {
                            [byte[]]$part = $responseParts[$partIndex]
                            $responseFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes `
                                (New-TcpSegment22 $serverPort $clientPort $serverSequence $requestNext 0x18 $hostIpBytes $guestIpBytes $part $false) `
                                (0x2F40 + $partIndex)
                            Require11 ($peerUdp.Send($responseFrame, $responseFrame.Length, '127.0.0.1', $rxPort) -eq $responseFrame.Length) 'Phase 23 response segment send was short.'
                            Write-Phase22Frame $injectionLog 'phase23_http_response_segment' $responseFrame
                            $serverSequence = [uint32]($serverSequence + $part.Length)
                            $managedAck = Receive-AnyPhase22TcpFrame $peerUdp $TimeoutSeconds 'Phase 23 response ACK'
                            Require11 (Test-TcpFrame22 $managedAck $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $requestNext $serverSequence 0x10 ([byte[]]@()) $false) 'Phase 23 response ACK validation failed.'
                            Write-Phase22Frame $injectionLog 'phase23_http_response_ack' $managedAck
                        }
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_STATUS_PARSED=200' $deadline $process $stream $logStream $text $buffer
                        # The managed client exposes body completion before it
                        # can verify teardown.  Send the peer FIN here; the
                        # final body verification marker follows the close.
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_BODY_RECEIVED' $deadline $process $stream $logStream $text $buffer
                        $peerFin = New-TcpSegment22 $serverPort $clientPort $serverSequence $requestNext 0x11 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
                        $peerFinFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $peerFin 0x2F50
                        Require11 ($peerUdp.Send($peerFinFrame, $peerFinFrame.Length, '127.0.0.1', $rxPort) -eq $peerFinFrame.Length) 'Phase 23 peer FIN send was short.'
                        Write-Phase22Frame $injectionLog 'phase23_peer_fin' $peerFinFrame
                        $finNext = [uint32]($requestNext + 1); $peerFinNext = [uint32]($serverSequence + 1)
                        $managedFinAck = Receive-AnyPhase22TcpFrame $peerUdp $TimeoutSeconds 'Phase 23 managed FIN ACK'
                        Require11 (Test-TcpFrame22 $managedFinAck $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $requestNext $peerFinNext 0x10 ([byte[]]@()) $false) 'Phase 23 managed FIN ACK validation failed.'
                        Write-Phase22Frame $injectionLog 'phase23_managed_fin_ack' $managedFinAck
                        $managedFin = Receive-AnyPhase22TcpFrame $peerUdp $TimeoutSeconds 'Phase 23 managed FIN'
                        Require11 (Test-TcpFrame22 $managedFin $peerMac $guestMac $guestIpBytes $hostIpBytes $clientPort $serverPort $requestNext $peerFinNext 0x11 ([byte[]]@()) $false) 'Phase 23 managed FIN validation failed.'
                        Write-Phase22Frame $injectionLog 'phase23_managed_fin' $managedFin
                        $finalAck = New-TcpSegment22 $serverPort $clientPort $peerFinNext $finNext 0x10 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
                        $finalAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes $guestIpBytes $finalAck 0x2F51
                        Require11 ($peerUdp.Send($finalAckFrame, $finalAckFrame.Length, '127.0.0.1', $rxPort) -eq $finalAckFrame.Length) 'Phase 23 final ACK send was short.'
                        Write-Phase22Frame $injectionLog 'phase23_final_ack' $finalAckFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_TEARDOWN_COMPLETE' $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_HTTP_PHASE23_PASS' $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE23_PASS' $deadline $process $stream $logStream $text $buffer
                        $phase15Outcome = 'PASS_PHASE23'
                    } elseif ($EnablePhase22Protocol) {
                        # Phase 22 uses the same DHCP/DNS peer and then acts as
                        # a deterministic raw Ethernet/IPv4/TCP server.  The
                        # controls below deliberately arrive before the valid
                        # SYNACK so the guest must reject them without moving
                        # the connection state.
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $discoverFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds `
                            'Phase 22 DHCPDISCOVER'
                        $discoverPayload = Get-DhcpPayload19 $discoverFrame
                        Require11 ($null -ne $discoverPayload) `
                            'Phase 22 DHCPDISCOVER is not IPv4/UDP.'
                        Write-Phase20Frame $injectionLog 'phase22_dhcpdiscover' $discoverFrame
                        $offerPayload = New-DhcpReply19 $discoverPayload 2 `
                            $guestIpBytes $hostIpBytes $true $true $true
                        $offerFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $offerPayload) 0x2D21
                        Require11 ($peerUdp.Send($offerFrame, $offerFrame.Length,
                            '127.0.0.1', $rxPort) -eq $offerFrame.Length) `
                            'Phase 22 DHCPOFFER send was short.'
                        Write-Phase20Frame $injectionLog 'phase22_dhcpoffer' $offerFrame

                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $requestFrame = Receive-AnyPhase19Frame $peerUdp $TimeoutSeconds `
                            'Phase 22 DHCPREQUEST'
                        $requestPayload = Get-DhcpPayload19 $requestFrame
                        Require11 ($null -ne $requestPayload) `
                            'Phase 22 DHCPREQUEST is not IPv4/UDP.'
                        Write-Phase20Frame $injectionLog 'phase22_dhcprequest' $requestFrame
                        $ackPayload = New-DhcpReply19 $requestPayload 5 `
                            $guestIpBytes $hostIpBytes $true $true $true
                        $ackFrame = New-Ipv4Udp18 $broadcastMac $hostMacBytes `
                            $hostIpBytes $broadcastIpBytes `
                            (New-UdpDatagram18 67 68 $hostIpBytes $broadcastIpBytes `
                                $ackPayload) 0x2D22
                        Require11 ($peerUdp.Send($ackFrame, $ackFrame.Length,
                            '127.0.0.1', $rxPort) -eq $ackFrame.Length) `
                            'Phase 22 DHCPACK send was short.'
                        Write-Phase20Frame $injectionLog 'phase22_dhcpack' $ackFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_DHCP_BOUND' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_CONFIGURED' `
                            $deadline $process $stream $logStream $text $buffer

                        $zeroMac = [byte[]](0, 0, 0, 0, 0, 0)
                        $arpRequest = New-Phase16ArpFrame11 $broadcastMac `
                            $guestMacBytes 1 $guestIpBytes $zeroMac $hostIpBytes
                        $observedArp = Receive-ExpectedPhase16Frame11 $peerUdp `
                            $arpRequest 'Phase 22 DNS ARP request' $TimeoutSeconds
                        Write-Phase20Frame $injectionLog 'phase22_dns_arp_request' $observedArp
                        $arpReply = New-Phase16ArpFrame11 $guestMacBytes $hostMacBytes `
                            2 $hostIpBytes $guestMacBytes $guestIpBytes
                        Require11 ($peerUdp.Send($arpReply, $arpReply.Length,
                            '127.0.0.1', $rxPort) -eq $arpReply.Length) `
                            'Phase 22 DNS ARP reply send was short.'
                        Write-Phase20Frame $injectionLog 'phase22_dns_arp_reply' $arpReply

                        $dnsQueryFrame = Receive-AnyDns20Frame $peerUdp $TimeoutSeconds `
                            'phase22.test'
                        $dnsQueryPayload = Get-DnsPayload20 $dnsQueryFrame
                        $expectedQuestion = [byte[]](
                            7,112,104,97,115,101,50,50,4,116,101,115,116,
                            0,0,1,0,1)
                        Require11 ($null -ne $dnsQueryPayload -and
                            $dnsQueryPayload.Length -eq 30 -and
                            (Bytes-Equal16 $dnsQueryPayload 12 $expectedQuestion)) `
                            'Phase 22 DNS query name was invalid.'
                        Write-Phase20Frame $injectionLog 'phase22_dns_query' $dnsQueryFrame
                        $dnsResponse = New-DnsResponse20 $dnsQueryPayload 'valid'
                        $dnsResponseFrame = New-Ipv4Udp18 $guestMacBytes $hostMacBytes `
                            $hostIpBytes $guestIpBytes `
                            (New-UdpDatagram18 53 15200 $hostIpBytes $guestIpBytes `
                                $dnsResponse) 0x2E21
                        Require11 ($peerUdp.Send($dnsResponseFrame,
                            $dnsResponseFrame.Length, '127.0.0.1', $rxPort) -eq
                            $dnsResponseFrame.Length) `
                            'Phase 22 DNS response send was short.'
                        Write-Phase20Frame $injectionLog 'phase22_dns_response' $dnsResponseFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_DNS_SUCCESS' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_RESOLVED_IPV4=0x000000000A0F0002' `
                            $deadline $process $stream $logStream $text $buffer

                        # The DNS ARP exchange populated the bounded ARP cache;
                        # the following TCP connect therefore emits its SYN
                        # directly and does not require a second ARP exchange.

                        $clientPort = 15221
                        $serverPort = 15222
                        [uint32]$clientIsn = 0x22000001
                        [uint32]$serverIsn = 0x22010001
                        $clientNext = [uint32]($clientIsn + 1)
                        $serverNext = [uint32]($serverIsn + 1)
                        $firstRequestPayload = [Text.Encoding]::ASCII.GetBytes(
                            'PHASE22-MANAGED-HELLO')
                        $firstReplyPayload = [Text.Encoding]::ASCII.GetBytes(
                            'PHASE22-PEER-ACK')
                        $secondRequestPayload = [Text.Encoding]::ASCII.GetBytes(
                            'PHASE22-POSTGC-HELLO')
                        $secondReplyPayload = [Text.Encoding]::ASCII.GetBytes(
                            'PHASE22-POSTGC-ACK')
                        $peerMac = $hostMacBytes
                        $guestMac = $guestMacBytes

                        Wait-Marker11 'GXOS_NET10:MANAGED_TCP_CONNECT_STARTED' `
                            $deadline $process $stream $logStream $text $buffer
                        $syn = New-TcpSegment22 $clientPort $serverPort $clientIsn 0 2 `
                            $guestIpBytes $hostIpBytes ([byte[]]@()) $true
                        $synFrame = New-Ipv4Tcp22 $peerMac $guestMac $guestIpBytes `
                            $hostIpBytes $syn 0x2A00
                        $observedSyn = Receive-ExpectedPhase17Frame $peerUdp $synFrame `
                            'Phase 22 managed SYN' $TimeoutSeconds
                        Require11 (Test-TcpFrame22 $observedSyn $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $clientIsn 0 2 ([byte[]]@()) $true) `
                            'Phase 22 managed SYN failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_syn' $observedSyn

                        $synAck = New-TcpSegment22 $serverPort $clientPort $serverIsn `
                            $clientNext 0x12 $hostIpBytes $guestIpBytes ([byte[]]@()) $true
                        $synAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes `
                            $guestIpBytes $synAck 0x2A01
                        $badChecksumFrame = [byte[]]$synAckFrame.Clone()
                        $badChecksumFrame[50] = [byte]($badChecksumFrame[50] -bxor 1)
                        $truncatedFrame = [byte[]]$synAckFrame.Clone()
                        Write-U16-Phase17 $truncatedFrame 16 30
                        $truncatedFrame[24] = 0; $truncatedFrame[25] = 0
                        Write-U16-Phase17 $truncatedFrame 24 `
                            (Compute-Checksum-Phase17 $truncatedFrame 14 20)
                        $badOffsetFrame = [byte[]]$synAckFrame.Clone()
                        $badOffsetFrame[46] = 0x40
                        Repair-TcpChecksum22 $badOffsetFrame $hostIpBytes $guestIpBytes
                        $offsetBeyondFrame = [byte[]]$synAckFrame.Clone()
                        $offsetBeyondFrame[46] = 0xF0
                        Repair-TcpChecksum22 $offsetBeyondFrame $hostIpBytes $guestIpBytes
                        $wrongSourceFrame = New-Ipv4Tcp22 $guestMac $peerMac `
                            $hostIpBytes $guestIpBytes `
                            (New-TcpSegment22 15223 $clientPort $serverIsn `
                                $clientNext 0x12 $hostIpBytes $guestIpBytes ([byte[]]@()) $true) 0x2A02
                        $wrongDestinationFrame = New-Ipv4Tcp22 $guestMac $peerMac `
                            $hostIpBytes $guestIpBytes `
                            (New-TcpSegment22 $serverPort 15224 $serverIsn `
                                $clientNext 0x12 $hostIpBytes $guestIpBytes ([byte[]]@()) $true) 0x2A03
                        $wrongAckFrame = New-Ipv4Tcp22 $guestMac $peerMac `
                            $hostIpBytes $guestIpBytes `
                            (New-TcpSegment22 $serverPort $clientPort $serverIsn `
                                ([uint32]($clientNext + 1)) 0x12 $hostIpBytes `
                                $guestIpBytes ([byte[]]@()) $true) 0x2A04
                        $staleRstFrame = New-Ipv4Tcp22 $guestMac $peerMac `
                            $hostIpBytes $guestIpBytes `
                            (New-TcpSegment22 $serverPort $clientPort 0 `
                                ([uint32]($clientNext + 1)) 0x14 $hostIpBytes `
                                $guestIpBytes ([byte[]]@()) $false) 0x2A05
                        foreach ($control in @(
                            [pscustomobject]@{ Name = 'tcp_bad_checksum'; Frame = $badChecksumFrame },
                            [pscustomobject]@{ Name = 'tcp_truncated'; Frame = $truncatedFrame },
                            [pscustomobject]@{ Name = 'tcp_bad_offset'; Frame = $badOffsetFrame },
                            [pscustomobject]@{ Name = 'tcp_offset_beyond_packet'; Frame = $offsetBeyondFrame },
                            [pscustomobject]@{ Name = 'tcp_wrong_source_port'; Frame = $wrongSourceFrame },
                            [pscustomobject]@{ Name = 'tcp_wrong_destination_port'; Frame = $wrongDestinationFrame },
                            [pscustomobject]@{ Name = 'tcp_wrong_synack_ack'; Frame = $wrongAckFrame },
                            [pscustomobject]@{ Name = 'tcp_stale_rst'; Frame = $staleRstFrame })) {
                            $controlFrame = [byte[]]$control.Frame
                            Require11 ($peerUdp.Send($controlFrame, $controlFrame.Length,
                                '127.0.0.1', $rxPort) -eq $controlFrame.Length) `
                                "Phase 22 $($control.Name) send was short."
                            Write-Phase22Frame $injectionLog $control.Name $controlFrame
                        }
                        Require11 ($peerUdp.Send($synAckFrame, $synAckFrame.Length,
                            '127.0.0.1', $rxPort) -eq $synAckFrame.Length) `
                            'Phase 22 valid SYNACK send was short.'
                        Write-Phase22Frame $injectionLog 'managed_synack' $synAckFrame
                        Wait-Marker11 'GXOS_NET10:MANAGED_TCP_HANDSHAKE_SUCCESS' `
                            $deadline $process $stream $logStream $text $buffer
                        $handshakeAck = New-TcpSegment22 $clientPort $serverPort `
                            $clientNext $serverNext 0x10 $guestIpBytes $hostIpBytes `
                            ([byte[]]@()) $false
                        $handshakeAckFrame = New-Ipv4Tcp22 $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $handshakeAck 0x2A06
                        $observedHandshakeAck = Receive-AnyPhase22TcpFrame $peerUdp `
                            $TimeoutSeconds 'Phase 22 managed handshake ACK'
                        Require11 (Test-TcpFrame22 $observedHandshakeAck $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $clientNext $serverNext 0x10 ([byte[]]@()) $false) `
                            'Phase 22 handshake ACK failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_handshake_ack' $observedHandshakeAck

                        Wait-Marker11 'GXOS_NET10:MANAGED_TCP_FIRST_REQUEST_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $observedFirstRequest = Receive-AnyPhase22TcpFrame $peerUdp `
                            $TimeoutSeconds 'Phase 22 first managed request'
                        Require11 (Test-TcpFrame22 $observedFirstRequest $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $clientNext $serverNext 0x18 $firstRequestPayload $false) `
                            'Phase 22 first managed request failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_first_request' $observedFirstRequest
                        $firstRequestNext = [uint32]($clientNext + $firstRequestPayload.Length)
                        $firstPeerAck = New-TcpSegment22 $serverPort $clientPort $serverNext `
                            $firstRequestNext 0x10 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
                        $firstPeerAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes `
                            $guestIpBytes $firstPeerAck 0x2A07
                        Require11 ($peerUdp.Send($firstPeerAckFrame,
                            $firstPeerAckFrame.Length, '127.0.0.1', $rxPort) -eq
                            $firstPeerAckFrame.Length) 'Phase 22 first ACK send was short.'
                        Write-Phase22Frame $injectionLog 'peer_first_ack' $firstPeerAckFrame
                        $firstPeerData = New-TcpSegment22 $serverPort $clientPort $serverNext `
                            $firstRequestNext 0x18 $hostIpBytes $guestIpBytes `
                            $firstReplyPayload $false
                        $firstPeerDataFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes `
                            $guestIpBytes $firstPeerData 0x2A08
                        Require11 ($peerUdp.Send($firstPeerDataFrame,
                            $firstPeerDataFrame.Length, '127.0.0.1', $rxPort) -eq
                            $firstPeerDataFrame.Length) 'Phase 22 first data send was short.'
                        Write-Phase22Frame $injectionLog 'peer_first_data' $firstPeerDataFrame
                        Wait-Marker11 'MANAGED_TCP_FIRST_EXCHANGE_SUCCESS' `
                            $deadline $process $stream $logStream $text $buffer
                        $firstPeerNext = [uint32]($serverNext + $firstReplyPayload.Length)
                        $firstManagedAck = New-TcpSegment22 $clientPort $serverPort `
                            $firstRequestNext $firstPeerNext 0x10 $guestIpBytes $hostIpBytes `
                            ([byte[]]@()) $false
                        $firstManagedAckFrame = New-Ipv4Tcp22 $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $firstManagedAck 0x2A09
                        $observedFirstManagedAck = Receive-AnyPhase22TcpFrame $peerUdp `
                            $TimeoutSeconds 'Phase 22 first managed response ACK'
                        Require11 (Test-TcpFrame22 $observedFirstManagedAck $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $firstRequestNext $firstPeerNext 0x10 ([byte[]]@()) $false) `
                            'Phase 22 first managed response ACK failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_first_response_ack' $observedFirstManagedAck

                        Wait-Marker11 'MANAGED_TCP_GC_WHILE_ESTABLISHED_PASSED' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'MANAGED_TCP_POST_GC_REQUEST_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $observedSecondRequest = Receive-AnyPhase22TcpFrame $peerUdp `
                            $TimeoutSeconds 'Phase 22 post-GC managed request'
                        Require11 (Test-TcpFrame22 $observedSecondRequest $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $firstRequestNext $firstPeerNext 0x18 $secondRequestPayload $false) `
                            'Phase 22 post-GC managed request failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_post_gc_request' $observedSecondRequest
                        $secondRequestNext = [uint32]($firstRequestNext + $secondRequestPayload.Length)
                        $secondPeerAck = New-TcpSegment22 $serverPort $clientPort $firstPeerNext `
                            $secondRequestNext 0x10 $hostIpBytes $guestIpBytes ([byte[]]@()) $false
                        $secondPeerAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes `
                            $guestIpBytes $secondPeerAck 0x2A0A
                        Require11 ($peerUdp.Send($secondPeerAckFrame,
                            $secondPeerAckFrame.Length, '127.0.0.1', $rxPort) -eq
                            $secondPeerAckFrame.Length) 'Phase 22 post-GC ACK send was short.'
                        Write-Phase22Frame $injectionLog 'peer_post_gc_ack' $secondPeerAckFrame
                        $secondPeerData = New-TcpSegment22 $serverPort $clientPort $firstPeerNext `
                            $secondRequestNext 0x18 $hostIpBytes $guestIpBytes `
                            $secondReplyPayload $false
                        $secondPeerDataFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes `
                            $guestIpBytes $secondPeerData 0x2A0B
                        Require11 ($peerUdp.Send($secondPeerDataFrame,
                            $secondPeerDataFrame.Length, '127.0.0.1', $rxPort) -eq
                            $secondPeerDataFrame.Length) 'Phase 22 post-GC data send was short.'
                        Write-Phase22Frame $injectionLog 'peer_post_gc_data' $secondPeerDataFrame
                        Wait-Marker11 'MANAGED_TCP_POST_GC_EXCHANGE_SUCCESS' `
                            $deadline $process $stream $logStream $text $buffer
                        $secondPeerNext = [uint32]($firstPeerNext + $secondReplyPayload.Length)
                        $secondManagedAck = New-TcpSegment22 $clientPort $serverPort `
                            $secondRequestNext $secondPeerNext 0x10 $guestIpBytes $hostIpBytes `
                            ([byte[]]@()) $false
                        $secondManagedAckFrame = New-Ipv4Tcp22 $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $secondManagedAck 0x2A0C
                        $observedSecondManagedAck = Receive-AnyPhase22TcpFrame $peerUdp `
                            $TimeoutSeconds 'Phase 22 post-GC response ACK'
                        Require11 (Test-TcpFrame22 $observedSecondManagedAck $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $secondRequestNext $secondPeerNext 0x10 ([byte[]]@()) $false) `
                            'Phase 22 post-GC response ACK failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_post_gc_response_ack' $observedSecondManagedAck

                        Wait-Marker11 'MANAGED_TCP_FIN_SENT' `
                            $deadline $process $stream $logStream $text $buffer
                        $finSequence = $secondRequestNext
                        $observedFin = Receive-AnyPhase22TcpFrame $peerUdp $TimeoutSeconds `
                            'Phase 22 managed FIN'
                        Require11 (Test-TcpFrame22 $observedFin $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $finSequence $secondPeerNext 0x11 ([byte[]]@()) $false) `
                            'Phase 22 managed FIN failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_fin' $observedFin
                        $finNext = [uint32]($finSequence + 1)
                        $peerFinalAck = New-TcpSegment22 $serverPort $clientPort `
                            $secondPeerNext $finNext 0x10 $hostIpBytes $guestIpBytes `
                            ([byte[]]@()) $false
                        $peerFinalAckFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes `
                            $guestIpBytes $peerFinalAck 0x2A0D
                        Require11 ($peerUdp.Send($peerFinalAckFrame,
                            $peerFinalAckFrame.Length, '127.0.0.1', $rxPort) -eq
                            $peerFinalAckFrame.Length) 'Phase 22 peer FIN ACK send was short.'
                        Write-Phase22Frame $injectionLog 'peer_fin_ack' $peerFinalAckFrame
                        $peerFin = New-TcpSegment22 $serverPort $clientPort `
                            $secondPeerNext $finNext 0x11 $hostIpBytes $guestIpBytes `
                            ([byte[]]@()) $false
                        $peerFinFrame = New-Ipv4Tcp22 $guestMac $peerMac $hostIpBytes `
                            $guestIpBytes $peerFin 0x2A0E
                        Require11 ($peerUdp.Send($peerFinFrame, $peerFinFrame.Length,
                            '127.0.0.1', $rxPort) -eq $peerFinFrame.Length) `
                            'Phase 22 peer FIN send was short.'
                        Write-Phase22Frame $injectionLog 'peer_fin' $peerFinFrame
                        $managedFinalAck = New-TcpSegment22 $clientPort $serverPort `
                            $finNext ([uint32]($secondPeerNext + 1)) 0x10 `
                            $guestIpBytes $hostIpBytes ([byte[]]@()) $false
                        $managedFinalAckFrame = New-Ipv4Tcp22 $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $managedFinalAck 0x2A0F
                        $observedManagedFinalAck = Receive-AnyPhase22TcpFrame $peerUdp `
                            $TimeoutSeconds 'Phase 22 managed final ACK'
                        Require11 (Test-TcpFrame22 $observedManagedFinalAck $peerMac $guestMac `
                            $guestIpBytes $hostIpBytes $clientPort $serverPort `
                            $finNext ([uint32]($secondPeerNext + 1)) 0x10 ([byte[]]@()) $false) `
                            'Phase 22 managed final ACK failed independent validation.'
                        Write-Phase22Frame $injectionLog 'managed_final_ack' $observedManagedFinalAck
                        Wait-Marker11 'MANAGED_TCP_GRACEFUL_CLOSE_SUCCESS' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'MANAGED_NETWORK_SERVICE_TCP_TEARDOWN_PASSED' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'MANAGED_NETWORK_SERVICE_PHASE22_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        Wait-Marker11 'MANAGED_KERNEL_PHASE22_PASS' `
                            $deadline $process $stream $logStream $text $buffer
                        $phase15Outcome = 'PASS_PHASE22'
                    } elseif ($EnablePhase21Protocol) {
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
                    if (-not $EnablePhase39Protocol -and -not $EnablePhase34Protocol -and -not $EnablePhase33Protocol -and -not $EnablePhase32Protocol -and -not $EnablePhase23Protocol -and -not $EnablePhase22Protocol -and -not $EnablePhase20Protocol -and -not $EnablePhase21Protocol) {
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
                    if ($EnablePhase39Protocol) {
                        # Phase 39 completed its single deterministic resource
                        # exchange and waits for its markers in the helper above.
                    } elseif ($EnablePhase34Protocol) {
                        # Phase 34 completed its bounded URL redirect chain and
                        # waits for its markers in the helper above.
                    } elseif ($EnablePhase33Protocol) {
                        # Phase 33 completed its three framed HTTPS exchanges
                        # and waits for its markers in the helper above.
                    } elseif ($EnablePhase32Protocol) {
                        # Phase 32 completed the full managed HTTPS exchange
                        # and its kernel proof waits in the helper above.
                    } elseif ($EnablePhase23Protocol) {
                        # Phase 23 completed its HTTP exchange, close, and
                        # kernel-pass waits above.
                    } elseif ($EnablePhase22Protocol) {
                        # Phase 22 completed its own authoritative TCP,
                        # teardown, and kernel-pass waits above.
                    } elseif ($EnablePhase21Protocol) {
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
        if ($EnablePhase34NegativeControl) {
            Require11 ($finalText.Contains(
                'GXOS_NET10:MANAGED_KERNEL_PHASE34_START_FAILED')) `
                "Boot $sequence did not reject the Phase 34 negative control."
            Require11 (!$finalText.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS') -and
                       !$finalText.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE34_PASS') -and
                       !$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                       !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                       !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
                "Boot $sequence reported an unexpected Phase 34 negative-control result."
        } elseif ($EnablePhase32NegativeControl) {
            Require11 ($finalText.Contains(
                'GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START')) `
                "Boot $sequence did not reject the Phase 32 negative control."
            Require11 ($finalText.Contains(
                'GXOS_NET10:FAIL:managed-kernel-phase14-driver-proof')) `
                "Boot $sequence did not report the expected negative-control failure."
            Require11 (!$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                       !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                       !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
                "Boot $sequence reported an unexpected machine fault."
        } elseif ($EnablePhase33NegativeControl) {
            Require11 ($finalText.Contains(
                'GXOS_NET10:MANAGED_KERNEL_PHASE33_START_FAILED')) `
                "Boot $sequence did not reject the Phase 33 negative control."
            Require11 (!$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                       !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                       !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
                "Boot $sequence reported an unexpected machine fault."
        } elseif ($EnableManagedKernelPhase35) {
            Require11 ($phase15Outcome -in @('A', 'B')) `
                "Boot $sequence reported unsupported Phase 35 outcome: $phase15Outcome."
            Require11 (!$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                       !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                       !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
                "Boot $sequence reported an unexpected machine fault."
        } elseif ($EnablePhase39Protocol) {
            Require11 ($phase15Outcome -eq 'PASS_PHASE34') `
                "Boot $sequence did not complete the Phase 39 resource proof: $phase15Outcome."
            Require11 (!$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                        !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                        !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
                "Boot $sequence reported an unexpected machine fault."
        } else {
            Require11 ((!$finalText.Contains('GXOS_NET10:FAIL:') -and
                        !$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                        !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                        !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:'))) `
                "Boot $sequence reported a fault."
        }
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

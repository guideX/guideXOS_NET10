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

function New-Phase15Frame11([string]$destinationMac) {
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
    $sequence = 0x15000001
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
                                  [string]$destinationHost = '127.0.0.1') {
    $frame = New-Phase15Frame11 $destinationMac
    $sent = $peerUdp.Send($frame, $frame.Length, $destinationHost, $destinationPort)
    Require11 ($sent -eq $frame.Length) 'Phase 15 UDP injector sent a short Ethernet datagram.'
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $frameHash = ([BitConverter]::ToString($hash.ComputeHash($frame))).Replace('-', '') }
    finally { $hash.Dispose() }
    return ('MANAGED_E1000_PHASE15_INJECTED=PASS transport=dgram length={0} destination={1} source=021500000001 ethertype=88B5 sequence=0x15000001 frame_sha256={2} udp_source_port={3} udp_destination_port={4}' -f `
        $frame.Length, $destinationMac.ToUpperInvariant(), $frameHash,
        ([Net.IPEndPoint]$peerUdp.Client.LocalEndPoint).Port, $destinationPort)
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
                $injectOutput = @(Send-Phase15DgramFrame11 $peerUdp $rxPort $destinationMac)
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
                } elseif ($EnablePhase16Protocol) {
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_COMPLETE' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_E1000_RX_FRAME_OK' `
                        $deadline $process $stream $logStream $text $buffer
                    $guestMacBytes = New-MacBytes16 $destinationMac
                    $broadcastMac = [byte[]](0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF)
                    $hostMacBytes = [byte[]](0x02, 0x15, 0, 0, 0, 2)
                    $guestIpBytes = [byte[]](10, 15, 0, 1)
                    $hostIpBytes = [byte[]](10, 15, 0, 2)
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
                        'source=021500000002 target_ipv4=0A0F0001'
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_REQUEST_FOR_LOCAL' `
                        $deadline $process $stream $logStream $text $buffer
                    $observedGuestReply = Receive-ExpectedPhase16Frame11 `
                        $peerUdp $guestReply 'ARP reply' $TimeoutSeconds
                    Write-Phase16Frame11 $injectionLog 'guest_arp_reply' $observedGuestReply
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_REPLY_SENT' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_ARP_RESPONDER_PASS' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'MANAGED_KERNEL_PHASE16_PASS' `
                        $deadline $process $stream $logStream $text $buffer
                    Wait-Marker11 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
                        $deadline $process $stream $logStream $text $buffer
                    $phase15Outcome = 'PASS_PHASE16'
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

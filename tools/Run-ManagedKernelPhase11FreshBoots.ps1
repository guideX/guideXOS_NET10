[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll'
$expectedHash = $PayloadSha256.ToUpperInvariant()

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
        Set-Content -LiteralPath $commandLinePath -Value ('"{0}" {1}' -f $qemu, ($arguments -join ' ')) -Encoding ascii
        $process = $null; $client = $null; $monitor = $null; $stream = $null
        $logStream = $null; $injectionLog = $null; $timeline = $null
        try {
            $timeline = [IO.StreamWriter]::new($timelinePath, $false, [Text.Encoding]::ASCII)
            $script:phase11Timeline = $timeline
            Write-Timeline11 $timeline 'HOST_LISTENERS_READY' "serial_port=$serialPort monitor_port=$monitorPort"
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
            Pump-Serial11 $stream $logStream $text $buffer
            $finalText = $text.ToString()
        } finally {
            if ($null -ne $injectionLog) { $injectionLog.Dispose() }
            if ($null -ne $logStream) { $logStream.Dispose() }
            if ($null -ne $stream) { $stream.Dispose() }
            if ($null -ne $client) { $client.Dispose() }
            if ($null -ne $monitor) { $monitor.Dispose() }
            Stop-OwnedQemu11 $process
            if ($null -ne $timeline) { $timeline.Dispose() }
            $script:phase11Timeline = $null
        }
        Require11 ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) "Payload hash changed on boot $sequence."
        Require11 ((!$finalText.Contains('GXOS_NET10:FAIL:') -and !$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:'))) "Boot $sequence reported a fault."
        foreach ($marker in $requiredMarkers) { Require11 $finalText.Contains($marker) "Boot $sequence missing marker: $marker" }
        Require11 (([regex]::Matches($finalText, 'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS')).Count -eq 1) 'Phase 11 pass marker count was not one.'
        Require11 ($finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ENQUEUED_COUNT=0x0000000000000009') -and $finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DRAINED_COUNT=0x0000000000000009') -and $finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DROPPED_COUNT=0x0000000000000000')) "Boot $sequence did not account for nine shared events without drops."
        $serialHash = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant()
        $injectionHash = (Get-FileHash -LiteralPath $injections -Algorithm SHA256).Hash.ToUpperInvariant()
        $timelineHash = (Get-FileHash -LiteralPath $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
        Write-Output ("MANAGED_KERNEL_PHASE11_QEMU_RUN_{0}=PASS bytes={1} serial_sha256={2} injections_sha256={3} timeline_sha256={4} serial={5}" -f $sequence, ([Text.Encoding]::ASCII.GetByteCount($finalText)), $serialHash, $injectionHash, $timelineHash, $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu11 $process }
}
Require11 (@(Get-OwnedQemu11).Count -eq 0) 'Owned QEMU cleanup failed.'
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE11_QEMU_RUNS=$RunCount"

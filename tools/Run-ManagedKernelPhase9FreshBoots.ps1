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

function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-OwnedQemu {
    $scope = @($gate, $evidence)
    return @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
        Where-Object {
            $commandLine = [string]$_.CommandLine
            $scope | Where-Object {
                $commandLine.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
            } | Select-Object -First 1
        })
}

function Stop-OwnedQemu([System.Diagnostics.Process]$process) {
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

function Pump-Serial(
    [System.IO.Stream]$stream,
    [IO.FileStream]$logStream,
    [Text.StringBuilder]$text,
    [byte[]]$buffer) {
    while ($true) {
        if ($null -eq $script:phase9ReadTask) {
            $script:phase9ReadTask = $stream.ReadAsync($buffer, 0, $buffer.Length)
        }
        if (!$script:phase9ReadTask.IsCompleted) { return }
        $count = $script:phase9ReadTask.Result
        $script:phase9ReadTask = $null
        if ($count -le 0) { break }
        $logStream.Write($buffer, 0, $count)
        $chunk = [Text.Encoding]::ASCII.GetString($buffer, 0, $count)
        $text.Append($chunk) | Out-Null
        $script:phase9Tail = $script:phase9Tail + $chunk
        if ($script:phase9Tail.Length -gt 4096) {
            $script:phase9Tail = $script:phase9Tail.Substring(
                $script:phase9Tail.Length - 4096)
        }
    }
}

function Write-Timeline([IO.StreamWriter]$timeline, [string]$event,
                        [string]$detail = '') {
    if ($null -eq $timeline) { return }
    $suffix = if ([string]::IsNullOrEmpty($detail)) { '' } else { " $detail" }
    $timeline.WriteLine(('event={0} utc={1:o}{2}' -f $event,
        (Get-Date).ToUniversalTime(), $suffix))
    $timeline.Flush()
}

function Connect-QemuSerial([int]$port, [System.Diagnostics.Process]$process,
                            [datetime]$deadline) {
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "QEMU exited before serial connection on port $port." }
        $client = [Net.Sockets.TcpClient]::new()
        try {
            $attempt = $client.ConnectAsync('127.0.0.1', $port)
            if ($attempt.Wait(500) -and $client.Connected) { return $client }
        } catch { }
        $client.Dispose()
        Start-Sleep -Milliseconds 50
    }
    throw "Timed out connecting to QEMU serial TCP port $port."
}

function Wait-Marker([string]$marker, [datetime]$deadline,
                     [System.Diagnostics.Process]$process,
                     [System.IO.Stream]$stream,
                     [IO.FileStream]$logStream,
    [Text.StringBuilder]$text,
                     [byte[]]$buffer) {
    while ((Get-Date) -lt $deadline) {
        Pump-Serial $stream $logStream $text $buffer
        $tail = [string]$script:phase9Tail
        $transcript = $text.ToString()
        if (-not $script:phase9DataReadyTimelineRecorded -and
            $transcript.Contains('GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_DATA_READY_OBSERVED')) {
            Write-Timeline $script:phase9Timeline 'GUEST_UART_DATA_READY'
            $script:phase9DataReadyTimelineRecorded = $true
        }
        if ($transcript.Contains($marker)) {
            Write-Timeline $script:phase9Timeline 'GUEST_MARKER' "marker=$marker"
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

function Send-SerialByte([Net.Sockets.TcpClient]$client,
                         [System.IO.Stream]$stream,
                         [System.Diagnostics.Process]$process,
                         [IO.StreamWriter]$injectionLog,
                         [string]$afterMarker, [byte]$value) {
    Require ($null -ne $client -and $client.Connected -and
             $null -ne $stream -and $stream.CanWrite) 'QEMU serial socket is not connected.'
    Require ($null -ne $process -and !$process.HasExited) 'QEMU exited before serial injection.'
    $client.Client.NoDelay = $true
    # NetworkStream.WriteByte is the proven raw-byte path for this QEMU 11
    # chardev. It avoids the prior Socket.Send acceptance ambiguity while
    # retaining the same full-duplex TCP connection used for serial output.
    $stream.WriteByte($value)
    $stream.Flush()
    $injectionLog.WriteLine(('{0} utc={1:o} byte=0x{2:X2}' -f $afterMarker, (Get-Date).ToUniversalTime(), $value))
    $injectionLog.Flush()
    Write-Timeline $script:phase9Timeline 'HOST_INJECT' "after=$afterMarker byte=0x$('{0:X2}' -f $value)"
}

Require ($RunCount -ge 3) 'Three fresh ManagedKernel Phase 9 boots are required.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'ManagedKernel EFI or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
Require ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'ManagedKernel payload size changed.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
    'ManagedKernel staged payload hash does not match the requested identity.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } `
    else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) 'OVMF firmware is required.'
Require (@(Get-OwnedQemu).Count -eq 0) 'An owned QEMU process is already running.'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

$requiredMarkers = @(
    'GXOS_NET10:NATIVEAOT_STARTUP_OK',
    'GXOS_NET10:MANAGED_KERNEL_PHASE1_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE2_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE3_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE4_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE5_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE6_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE7_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE8_PASS',
    'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_SERVICES_INSTALLED',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SUBSCRIBED',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_FROM_HARDWARE_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_UNSUBSCRIBE_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_UNSUBSCRIBED_READY',
    'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_NEGATIVE_TESTS_OK',
    'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ACCOUNTING_RESTORED_NATIVE_OK',
    'GXOS_NET10:MANAGED_KERNEL_PHASE9_PASS')

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require (@(Get-OwnedQemu).Count -eq 0) "A QEMU process already owns boot $sequence."
        $run = Join-Path $evidence ("runs\run-{0}" -f $sequence)
        New-Item -ItemType Directory -Force -Path $run | Out-Null
        $code = Join-Path $run 'edk2-code.fd'
        $vars = Join-Path $run 'edk2-vars.fd'
        $serial = Join-Path $run 'serial.log'
        $injections = Join-Path $run 'injections.log'
        $timelinePath = Join-Path $run 'timeline.log'
        $commandLinePath = Join-Path $run 'qemu-commandline.log'
        $stdout = Join-Path $run 'qemu.stdout.log'
        $stderr = Join-Path $run 'qemu.stderr.log'
        Copy-Item -LiteralPath $ovmf -Destination $code
        Copy-Item -LiteralPath $varsTemplate -Destination $vars

        $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $probe.Start()
        $port = ([Net.IPEndPoint]$probe.LocalEndpoint).Port
        $probe.Stop()
        $arguments = @(
            '-machine', 'q35', '-accel', 'tcg,thread=single', '-m', '128M',
            '-drive', "if=pflash,format=raw,readonly=on,file=$code",
            '-drive', "if=pflash,format=raw,file=$vars",
            '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
            '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
            '-chardev', "socket,id=serial0,host=127.0.0.1,port=$port,server=on,wait=on,telnet=off,ipv4=on,nodelay=on",
            '-serial', 'none',
            '-device', 'isa-serial,chardev=serial0,iobase=0x3f8,irq=4,wakeup=on',
            '-monitor', 'none', '-display', 'none', '-no-reboot', '-no-shutdown')
        $commandLine = '"{0}" {1}' -f $qemu, ($arguments -join ' ')
        Set-Content -LiteralPath $commandLinePath -Value $commandLine -Encoding ascii
        $process = $null
        $client = $null
        $stream = $null
        $logStream = $null
        $injectionLog = $null
        $timeline = $null
        try {
            $timeline = [IO.StreamWriter]::new($timelinePath, $false,
                [Text.Encoding]::ASCII)
            $script:phase9Timeline = $timeline
            Write-Timeline $timeline 'HOST_LISTENER_READY' "port=$port"
            $process = Start-Process -FilePath $qemu -ArgumentList $arguments `
                -WorkingDirectory $gate -RedirectStandardOutput $stdout `
                -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
            $owned += $process
            Write-Timeline $timeline 'QEMU_STARTED' "pid=$($process.Id)"
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            $client = Connect-QemuSerial $port $process $deadline
            Write-Timeline $timeline 'SERIAL_CONNECTED' "port=$port"
            $stream = $client.GetStream()
            $logStream = [IO.File]::Open($serial, [IO.FileMode]::Create,
                [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $injectionLog = [IO.StreamWriter]::new($injections, $false,
                [Text.Encoding]::ASCII)
            $text = [Text.StringBuilder]::new()
            $script:phase9Tail = ''
            $script:phase9ReadTask = $null
            $script:phase9DataReadyTimelineRecorded = $false
            $buffer = New-Object byte[] 4096

            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' $deadline $process $stream $logStream $text $buffer
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' $deadline $process $stream $logStream $text $buffer
            Require (!$process.HasExited) 'QEMU exited after RX_READY.'
            Start-Sleep -Milliseconds 50
            Send-SerialByte $client $stream $process $injectionLog 'RX_READY' 0x52
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_IRQ_CAPTURED' $deadline $process $stream $logStream $text $buffer
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_FROM_HARDWARE_OK' $deadline $process $stream $logStream $text $buffer
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_OK' $deadline $process $stream $logStream $text $buffer
            Require (!$process.HasExited) 'QEMU exited after runtime-survival marker.'
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' $deadline $process $stream $logStream $text $buffer
            Send-SerialByte $client $stream $process $injectionLog 'RX_RUNTIME_SURVIVAL_OK' 0x53
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_IRQ_CAPTURED' $deadline $process $stream $logStream $text $buffer
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' $deadline $process $stream $logStream $text $buffer
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_UNSUBSCRIBED_READY' $deadline $process $stream $logStream $text $buffer
            Require (!$process.HasExited) 'QEMU exited after unsubscribe-ready marker.'
            Send-SerialByte $client $stream $process $injectionLog 'RX_UNSUBSCRIBED_READY' 0x5A
            Wait-Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE9_PASS' $deadline $process $stream $logStream $text $buffer
            Pump-Serial $stream $logStream $text $buffer
            $finalText = $text.ToString()
        } finally {
            if ($null -ne $injectionLog) { $injectionLog.Dispose() }
            if ($null -ne $logStream) { $logStream.Dispose() }
            if ($null -ne $stream) { $stream.Dispose() }
            if ($null -ne $client) { $client.Dispose() }
            Stop-OwnedQemu $process
            if ($null -ne $timeline) { $timeline.Dispose() }
            $script:phase9Timeline = $null
        }

        Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
            "ManagedKernel payload hash changed on boot $sequence."
        Require (!$finalText.Contains('GXOS_NET10:FAIL:') -and
                 !$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                 !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
            "ManagedKernel boot $sequence reported a fault, page fault, or unresolved import."
        foreach ($marker in $requiredMarkers) {
            Require ($finalText.Contains($marker)) "Boot $sequence missing marker: $marker"
        }
        Require ($finalText.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE9_RUNTIME_ACTIVITY') -or
                 $finalText.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE10_RUNTIME_ACTIVITY')) `
            "Boot $sequence missing managed runtime activity marker."
        Require (([regex]::Matches($finalText, 'GXOS_NET10:MANAGED_KERNEL_SERIAL_IRQ_CAPTURED')).Count -eq 2) `
            "Boot $sequence did not capture exactly two native IRQ events."
        Require (([regex]::Matches($finalText, 'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_EVENT_ENQUEUED')).Count -eq 2) `
            "Boot $sequence did not record exactly two enqueued events."
        Require ($finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_IRQ_COUNT=0x0000000000000002')) `
            "Boot $sequence did not report exactly two IRQ entries."
        Require ($finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_SERIAL_ISR_COUNT=0x0000000000000002')) `
            "Boot $sequence did not report exactly two serial ISR captures."
        Require ($finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ENQUEUED_COUNT=0x0000000000000002') -and
                 $finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DRAINED_COUNT=0x0000000000000002') -and
                 $finalText.Contains('GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DROPPED_COUNT=0x0000000000000000')) `
            "Boot $sequence did not restore exact interrupt queue accounting."
        Require (([regex]::Matches($finalText, 'GXOS_NET10:MANAGED_KERNEL_PHASE9_PASS')).Count -eq 1) `
            "Boot $sequence repeated or omitted the Phase 9 pass marker."
        $serialHash = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant()
        $injectionHash = (Get-FileHash -LiteralPath $injections -Algorithm SHA256).Hash.ToUpperInvariant()
        $timelineHash = (Get-FileHash -LiteralPath $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
        $commandLineHash = (Get-FileHash -LiteralPath $commandLinePath -Algorithm SHA256).Hash.ToUpperInvariant()
        Write-Output ("MANAGED_KERNEL_PHASE9_QEMU_RUN_{0}=PASS bytes={1} serial_sha256={2} injections_sha256={3} timeline_sha256={4} commandline_sha256={5} serial={6}" -f `
            $sequence, ([Text.Encoding]::ASCII.GetByteCount($finalText)), $serialHash,
            $injectionHash, $timelineHash, $commandLineHash, $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
Require (@(Get-OwnedQemu).Count -eq 0) 'Owned QEMU cleanup failed.'
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE9_QEMU_RUNS=$RunCount"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [ValidateSet('None', 'AttachFailureDiscard', 'IdleStop')]
    [string]$Phase69FixtureMode = 'None',
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

function Require10([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-OwnedQemu10 {
    $scope = @($gate, $evidence)
    return @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
        Where-Object {
            $commandLine = [string]$_.CommandLine
            $scope | Where-Object {
                $commandLine.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
            } | Select-Object -First 1
        })
}

function Stop-OwnedQemu10([System.Diagnostics.Process]$process) {
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

function Pump-Serial10(
    [System.IO.Stream]$stream,
    [IO.FileStream]$logStream,
    [Text.StringBuilder]$text,
    [byte[]]$buffer) {
    while ($true) {
        if ($null -eq $script:phase10ReadTask) {
            $script:phase10ReadTask = $stream.ReadAsync($buffer, 0, $buffer.Length)
        }
        if (!$script:phase10ReadTask.IsCompleted) { return }
        $count = $script:phase10ReadTask.Result
        $script:phase10ReadTask = $null
        if ($count -le 0) { return }
        $logStream.Write($buffer, 0, $count)
        $chunk = [Text.Encoding]::ASCII.GetString($buffer, 0, $count)
        $text.Append($chunk) | Out-Null
        $script:phase10Tail = $script:phase10Tail + $chunk
        if ($script:phase10Tail.Length -gt 8192) {
            $script:phase10Tail = $script:phase10Tail.Substring(
                $script:phase10Tail.Length - 8192)
        }
    }
}

function Write-Timeline10([IO.StreamWriter]$timeline, [string]$event,
                          [string]$detail = '') {
    if ($null -eq $timeline) { return }
    $suffix = if ([string]::IsNullOrEmpty($detail)) { '' } else { " $detail" }
    $timeline.WriteLine(('event={0} utc={1:o}{2}' -f $event,
        (Get-Date).ToUniversalTime(), $suffix))
    $timeline.Flush()
}

function Connect-QemuSerial10([int]$port, [System.Diagnostics.Process]$process,
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

function Wait-Marker10([string]$marker, [datetime]$deadline,
                       [System.Diagnostics.Process]$process,
                       [System.IO.Stream]$stream, [IO.FileStream]$logStream,
                       [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ((Get-Date) -lt $deadline) {
        Pump-Serial10 $stream $logStream $text $buffer
        $transcript = $text.ToString()
        if ($transcript.Contains($marker)) {
            Write-Timeline10 $script:phase10Timeline 'GUEST_MARKER' "marker=$marker"
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

function Send-SerialByte10([Net.Sockets.TcpClient]$client,
                            [System.IO.Stream]$stream,
                            [System.Diagnostics.Process]$process,
                            [IO.StreamWriter]$injectionLog,
                            [string]$afterMarker, [byte]$value) {
    Require10 ($null -ne $client -and $client.Connected -and
               $null -ne $stream -and $stream.CanWrite) 'QEMU serial socket is not connected.'
    Require10 ($null -ne $process -and !$process.HasExited) 'QEMU exited before serial injection.'
    # NetworkStream.WriteByte is the proven raw-byte path for this QEMU
    # chardev. Keep the transport identical to the accepted Phase 9 runner.
    $client.Client.NoDelay = $true
    $stream.WriteByte($value)
    $stream.Flush()
    $injectionLog.WriteLine(('{0} utc={1:o} byte=0x{2:X2}' -f
        $afterMarker, (Get-Date).ToUniversalTime(), $value))
    $injectionLog.Flush()
    Write-Timeline10 $script:phase10Timeline 'HOST_INJECT' `
        "after=$afterMarker byte=0x$('{0:X2}' -f $value)"
}

function Send-SerialBurst10([Net.Sockets.TcpClient]$client,
                             [System.IO.Stream]$stream,
                             [System.Diagnostics.Process]$process,
                             [IO.StreamWriter]$injectionLog,
                             [string]$afterMarker, [byte[]]$values) {
    Require10 ($null -ne $client -and $client.Connected -and
               $null -ne $stream -and $stream.CanWrite) 'QEMU serial socket is not connected.'
    Require10 ($null -ne $process -and !$process.HasExited) 'QEMU exited before serial injection.'
    Require10 ($null -ne $values -and $values.Length -ne 0) 'Serial burst must not be empty.'
    $client.Client.NoDelay = $true
    $stream.Write($values, 0, $values.Length)
    $stream.Flush()
    $hex = (($values | ForEach-Object { '0x{0:X2}' -f $_ }) -join ',')
    $injectionLog.WriteLine(('{0} utc={1:o} bytes={2}' -f
        $afterMarker, (Get-Date).ToUniversalTime(), $hex))
    $injectionLog.Flush()
    Write-Timeline10 $script:phase10Timeline 'HOST_INJECT_BURST' `
        "after=$afterMarker bytes=$hex"
}

function Get-HexField10([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + '0x([0-9A-Fa-f]+)')
    if (!$match.Success) { throw "Missing numeric marker: $name" }
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

Require10 ($RunCount -ge 1) `
    'At least one fresh Phase 10 boot is required; use RunCount 3 for normal acceptance.'
Require10 ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'ManagedKernel EFI or payload is missing.'
Require10 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
Require10 ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'ManagedKernel payload size changed.'
Require10 ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
    'ManagedKernel staged payload hash does not match the requested identity.'
Require10 (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } `
    else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
Require10 (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require10 ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) 'OVMF firmware is required.'
Require10 (@(Get-OwnedQemu10).Count -eq 0) 'An owned QEMU process is already running.'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null
$qemuVersion = & $qemu --version 2>&1
$qemuVersion | Set-Content -LiteralPath (Join-Path $evidence 'qemu-version.log') -Encoding ascii

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
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_CREATED',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_TLS_READY',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_STARTED',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_SLEEPING',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY',
    'GXOS_NET10:PERSISTENT_SERVICE_OBJECT_SLOTS_BASELINE_FREE=',
    'GXOS_NET10:PERSISTENT_SERVICE_THREAD_SLOTS_BASELINE_FREE=',
    'GXOS_NET10:PERSISTENT_SERVICE_OBJECT_SLOTS_RUNNING_FREE=',
    'GXOS_NET10:PERSISTENT_SERVICE_THREAD_SLOTS_RUNNING_FREE=',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SUBSCRIBED',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_WAKE_OK',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORK_DISPATCH_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_FROM_HARDWARE_OK',
    'GXOS_NET10:MANAGED_KERNEL_PHASE10_RUNTIME_ACTIVITY',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_RUNTIME_SURVIVAL_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK',
    'GXOS_NET10:PERSISTENT_SERVICE_ROUTE_QUIESCED=1',
    'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_QUEUE_EMPTY=1',
    'GXOS_NET10:PHASE69_PERSISTENT_STATE_ACROSS_REAL_EVENTS=1',
    'GXOS_NET10:PHASE69_ONE_SHOT_SUBMITTED_LIVE_BEFORE_SERVICE_STOP=1',
    'GXOS_NET10:PHASE69_ONE_SHOT_HELD_PENDING_FOR_SERVICE_STOP=1',
    'GXOS_NET10:PHASE69_ONE_SHOT_STILL_SUBMITTED_AFTER_SERVICE_STOP=1',
    'GXOS_NET10:PHASE69_JOINT_ADMISSION_REJECTED_BEFORE_ALLOCATION=1',
    'GXOS_NET10:PHASE69_REJECTED_ADMISSION_NO_TCB_OR_WAKE_EVENT=1',
    'GXOS_NET10:PHASE69_ONE_SHOT_SURVIVED_SERVICE_STOP=1',
    'GXOS_NET10:PHASE69_ONE_SHOT_RECLAIMED_AFTER_SERVICE_STOP=1',
    'GXOS_NET10:PHASE69_ROUTE_ACCEPTANCE_CLOSED_BEFORE_HARDWARE_STOP=1',
    'GXOS_NET10:PHASE69_HARDWARE_ROUTES_DISABLED_BEFORE_WORKER_EXIT=1',
    'GXOS_NET10:PHASE69_THREADSTORE_BASELINE=',
    'GXOS_NET10:PHASE69_THREADSTORE_FINAL=',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_UNSUBSCRIBE_OK',
    'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_UNSUBSCRIBED_READY',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_STOPPING',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_STOP_OK',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_RECLAIMED',
    'GXOS_NET10:PERSISTENT_SERVICE_OBJECT_SLOTS_FINAL_FREE=',
    'GXOS_NET10:PERSISTENT_SERVICE_THREAD_SLOTS_FINAL_FREE=',
    'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ACCOUNTING_RESTORED_NATIVE_OK',
    'GXOS_NET10:MANAGED_KERNEL_PHASE9_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE10_PASS')
$requiredMarkers += switch ($Phase69FixtureMode) {
    'None' {
        @('GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_CAPTURED',
          'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_REQUESTED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_COMPLETE=1',
          'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_OK',
          'GXOS_NET10:MANAGED_KERNEL_DRIVER_WAKE_COALESCE_OK')
    }
    'IdleStop' {
        @('GXOS_NET10:PHASE69_IDLE_STOP_READY=1',
          'GXOS_NET10:PHASE69_IDLE_STOP_QUEUE_EMPTY=1',
          'GXOS_NET10:PHASE69_IDLE_STOP_WORKER_BLOCKED=1',
          'GXOS_NET10:PHASE69_IDLE_STOP_WAKE_DELIVERED=1',
          'GXOS_NET10:PHASE69_IDLE_STOP_WORKER_EXITED=1',
          'GXOS_NET10:PHASE69_IDLE_STOP_NO_BUSY_LOOP=1',
          'GXOS_NET10:PHASE69_IDLE_STOP_DETACHED_ONCE=1',
          'GXOS_NET10:PHASE69_IDLE_STOP_EVENT_AND_TCB_RECLAIMED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_COMPLETE=1')
    }
    'AttachFailureDiscard' {
        @('GXOS_NET10:PHASE69_ATTACH_FAILURE_FIXTURE_BEGIN=1',
          'GXOS_NET10:PHASE69_ATTACH_FAILURE_INJECTION_FIRED=1',
          'GXOS_NET10:PHASE69_FAILED_START_BASELINE_RESTORED=1',
          'GXOS_NET10:PHASE69_FAILED_RUNTIME_ACQUIRED=0',
          'GXOS_NET10:PHASE69_FAILED_RUNTIME_ATTACH_ATTEMPTED=1',
          'GXOS_NET10:PHASE69_FAILED_TCB_HANDLE_EVENT_VM_RECLAIMED=1',
          'GXOS_NET10:PHASE69_FAILED_OWNER_SLOT_RELEASED=1',
          'GXOS_NET10:PHASE69_SAME_SLOT_REUSED=1',
          'GXOS_NET10:PHASE69_STALE_FAILED_STOP_REJECTED=1',
          'GXOS_NET10:PHASE69_HEALTHY_REPLACEMENT_STARTED=1',
          'GXOS_NET10:PHASE69_HEALTHY_REPLACEMENT_RUNTIME_ATTACH=1',
          'GXOS_NET10:PHASE69_DISCARD_FIXTURE_EVENTS_ENQUEUED=3',
          'GXOS_NET10:PHASE69_DISCARD_QUEUE_EMPTY=1',
          'GXOS_NET10:PHASE69_DISCARD_MANAGED_DISPATCH_SUPPRESSED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_DISCARD_COMPLETE=1')
    }
}

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require10 (@(Get-OwnedQemu10).Count -eq 0) "A QEMU process already owns boot $sequence."
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
        $firmwareCodeHash = (Get-FileHash -LiteralPath $code -Algorithm SHA256).Hash.ToUpperInvariant()
        $firmwareVarsHash = (Get-FileHash -LiteralPath $vars -Algorithm SHA256).Hash.ToUpperInvariant()
        Set-Content -LiteralPath $firmwareIdentityPath -Value @(
            "qemu=$qemu",
            "ovmf_code=$code",
            "ovmf_code_sha256=$firmwareCodeHash",
            "ovmf_vars=$vars",
            "ovmf_vars_sha256=$firmwareVarsHash") -Encoding ascii

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
            $script:phase10Timeline = $timeline
            Write-Timeline10 $timeline 'HOST_LISTENER_READY' "port=$port"
            Write-Timeline10 $timeline 'FIRMWARE_IDENTITY' `
                "code_sha256=$firmwareCodeHash vars_sha256=$firmwareVarsHash"
            $process = Start-Process -FilePath $qemu -ArgumentList $arguments `
                -WorkingDirectory $gate -RedirectStandardOutput $stdout `
                -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
            $owned += $process
            Write-Timeline10 $timeline 'QEMU_STARTED' "pid=$($process.Id)"
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            $client = Connect-QemuSerial10 $port $process $deadline
            Write-Timeline10 $timeline 'SERIAL_CONNECTED' "port=$port"
            $stream = $client.GetStream()
            $logStream = [IO.File]::Open($serial, [IO.FileMode]::Create,
                [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $injectionLog = [IO.StreamWriter]::new($injections, $false,
                [Text.Encoding]::ASCII)
            $text = [Text.StringBuilder]::new()
            $script:phase10Tail = ''
            $script:phase10ReadTask = $null
            $buffer = New-Object byte[] 4096

            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' `
                $deadline $process $stream $logStream $text $buffer
            Start-Sleep -Milliseconds 50
            Send-SerialByte10 $client $stream $process $injectionLog 'RX_READY' 0x52
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_WAKE_OK' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORK_DISPATCH_OK' `
                $deadline $process $stream $logStream $text $buffer
            Write-Timeline10 $timeline 'HOST_NO_MANUAL_DRAIN' `
                'first_delivery=worker_dispatch'
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_RUNTIME_SURVIVAL_OK' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK' `
                $deadline $process $stream $logStream $text $buffer

            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' `
                $deadline $process $stream $logStream $text $buffer
            Send-SerialByte10 $client $stream $process $injectionLog `
                'RX_RUNTIME_SURVIVAL_NATIVE_OK' 0x53
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' `
                $deadline $process $stream $logStream $text $buffer

            if ($Phase69FixtureMode -eq 'IdleStop') {
                Wait-Marker10 'GXOS_NET10:PHASE69_IDLE_STOP_READY=1' `
                    $deadline $process $stream $logStream $text $buffer
            } else {
                Send-SerialBurst10 $client $stream $process $injectionLog `
                    'RX_AFTER_RUNTIME_OK_BURST' ([byte[]](0x41, 0x42, 0x43))
                Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_CAPTURED' `
                    $deadline $process $stream $logStream $text $buffer
            }
            if ($Phase69FixtureMode -eq 'AttachFailureDiscard') {
                Wait-Marker10 'GXOS_NET10:PERSISTENT_SERVICE_DISCARD_COMPLETE=1' `
                    $deadline $process $stream $logStream $text $buffer
            }
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_UNSUBSCRIBED_READY' `
                $deadline $process $stream $logStream $text $buffer
            Send-SerialByte10 $client $stream $process $injectionLog `
                'RX_UNSUBSCRIBED_READY' 0x5A
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_STOP_OK' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_RECLAIMED' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_PHASE10_PASS' `
                $deadline $process $stream $logStream $text $buffer
            Pump-Serial10 $stream $logStream $text $buffer
            $finalText = $text.ToString()
        } finally {
            if ($null -ne $injectionLog) { $injectionLog.Dispose() }
            if ($null -ne $logStream) { $logStream.Dispose() }
            if ($null -ne $stream) { $stream.Dispose() }
            if ($null -ne $client) { $client.Dispose() }
            Stop-OwnedQemu10 $process
            if ($null -ne $timeline) { $timeline.Dispose() }
            $script:phase10Timeline = $null
        }

        Require10 ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
            "ManagedKernel payload hash changed on boot $sequence."
        Require10 (!$finalText.Contains('GXOS_NET10:FAIL:') -and
                   !$finalText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                   !$finalText.Contains('GXOS_NET10:PAGE_FAULT_') -and
                   !$finalText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
            "ManagedKernel Phase 10 boot $sequence reported a fault, page fault, or unresolved import."
        foreach ($marker in $requiredMarkers) {
            Require10 ($finalText.Contains($marker)) "Boot $sequence missing marker: $marker"
        }
        Require10 (([regex]::Matches($finalText, 'GXOS_NET10:MANAGED_KERNEL_PHASE10_PASS')).Count -eq 1) `
            "Boot $sequence repeated or omitted the Phase 10 pass marker."
        $expectedEnqueued = switch ($Phase69FixtureMode) {
            'None' { 5 }
            'IdleStop' { 2 }
            'AttachFailureDiscard' { 8 }
        }
        $expectedDrained = if ($Phase69FixtureMode -eq 'AttachFailureDiscard') { 2 } else { $expectedEnqueued }
        $enqueued = Get-HexField10 $finalText 'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ENQUEUED_COUNT='
        $drained = Get-HexField10 $finalText 'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DRAINED_COUNT='
        $dropped = Get-HexField10 $finalText 'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_DROPPED_COUNT='
        $discarded = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_SHUTDOWN_DISCARDED='
        Require10 ($enqueued -eq $expectedEnqueued -and $drained -eq $expectedDrained -and $dropped -eq 0) `
            "Boot $sequence reported incorrect enqueue, drain, or overflow accounting."
        if ($Phase69FixtureMode -eq 'AttachFailureDiscard') {
            Require10 ($discarded -eq 6) "Boot $sequence did not discard the six pending records."
            Require10 ((Get-HexField10 $finalText 'GXOS_NET10:PHASE69_DISCARD_QUEUED_BEFORE=') -eq 6 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_DISCARD_RECORDS_COUNT=') -eq 6 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_DISCARD_DROPPED_BEFORE=') -eq
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_DISCARD_DROPPED_AFTER=') -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_DISCARD_DISPATCH_COUNT_BEFORE=') -eq
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_DISCARD_DISPATCH_COUNT_AFTER=')) `
                "Boot $sequence did not suppress dispatch and preserve overflow accounting during DISCARD."
        } else {
            Require10 ($discarded -eq 0) "Boot $sequence unexpectedly discarded queue records."
        }
        $timelineText = Get-Content -LiteralPath $timelinePath -Raw
        Require10 ($timelineText.Contains('event=HOST_NO_MANUAL_DRAIN')) `
            'Timeline did not record the scheduler worker as the first delivery path.'
        $wakeRequests = Get-HexField10 $finalText 'GXOS_NET10:MANAGED_KERNEL_INTERRUPT_WAKE_REQUEST_COUNT='
        Require10 ($wakeRequests -ge 1 -and $wakeRequests -le 5) `
            "Boot $sequence reported an invalid wake request count."
        $drainPending = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PENDING='
        $drainEvidenceValid = switch ($Phase69FixtureMode) {
            'None' { $drainPending -gt 0 -and $drainPending -le 3 -and
                $finalText.Contains('GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PRECONDITION_PENDING=1') }
            'IdleStop' { $drainPending -eq 0 -and
                $finalText.Contains('GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PRECONDITION_IDLE=1') }
            'AttachFailureDiscard' { $drainPending -eq 6 -and
                $finalText.Contains('GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PRECONDITION_PENDING=1') }
        }
        Require10 $drainEvidenceValid `
            "Boot $sequence reported inconsistent shutdown queue evidence."
        if ($Phase69FixtureMode -eq 'AttachFailureDiscard') {
            $failedSlot = Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_SERVICE_SLOT='
            $failedIdentity = Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_SERVICE_IDENTITY='
            $failedGeneration = Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_SERVICE_GENERATION='
            $replacementSlot = Get-HexField10 $finalText 'GXOS_NET10:PHASE69_REPLACEMENT_SERVICE_SLOT='
            $replacementIdentity = Get-HexField10 $finalText 'GXOS_NET10:PHASE69_REPLACEMENT_SERVICE_IDENTITY='
            $replacementGeneration = Get-HexField10 $finalText 'GXOS_NET10:PHASE69_REPLACEMENT_SERVICE_GENERATION='
            Require10 ($failedSlot -eq 0 -and $replacementSlot -eq $failedSlot -and
                       $failedIdentity -ne $replacementIdentity -and
                       $failedGeneration -ne $replacementGeneration -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_RUNTIME_OWNERSHIP=') -eq 1 -and
                       $finalText.Contains('GXOS_NET10:PHASE69_FAILED_RUNTIME_ACQUIRED=0') -and
                       $finalText.Contains('GXOS_NET10:PHASE69_FAILED_RUNTIME_ATTACH_ATTEMPTED=1') -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_RUNTIME_ATTACH_COUNT=') -eq 0 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_RUNTIME_DETACH_COUNT=') -eq 0 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_FINAL_THREADSTORE=') -eq
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_ATTACH_BASELINE_THREADSTORE=') -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_FINAL_THREAD_SLOTS_FREE=') -eq
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_ATTACH_BASELINE_THREAD_SLOTS_FREE=') -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_FAILED_FINAL_OBJECT_SLOTS_FREE=') -eq
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_ATTACH_BASELINE_OBJECT_SLOTS_FREE=')) `
                "Boot $sequence failed attach cleanup or replacement identity checks."
        }
        if ($Phase69FixtureMode -eq 'IdleStop') {
            Require10 ($finalText.Contains('GXOS_NET10:PHASE69_IDLE_STOP_WAKE_DELIVERED=1') -and
                       $finalText.Contains('GXOS_NET10:PHASE69_IDLE_STOP_NO_BUSY_LOOP=1')) `
                "Boot $sequence failed the idle stop wake/exit proof."
        }
        $objectBaseline = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OBJECT_SLOTS_BASELINE_FREE='
        $objectRunning = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OBJECT_SLOTS_RUNNING_FREE='
        $objectPeak = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_ONE_SHOT_OBJECTS_AFTER_CREATE='
        $objectFinal = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OBJECT_SLOTS_FINAL_FREE='
        Require10 ($objectBaseline - $objectRunning -eq 2 -and
                   $objectRunning - $objectPeak -eq 1 -and
                   $objectFinal -eq $objectBaseline) `
            "Boot $sequence did not restore the scheduler-object baseline."
        $threadBaseline = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_THREAD_SLOTS_BASELINE_FREE='
        $threadRunning = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_THREAD_SLOTS_RUNNING_FREE='
        $threadPeak = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_ONE_SHOT_THREADS_AFTER_CREATE='
        $threadFinal = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_THREAD_SLOTS_FINAL_FREE='
        Require10 ($threadBaseline - $threadRunning -eq 1 -and
                   $threadRunning - $threadPeak -eq 1 -and
                   $threadFinal -eq $threadBaseline) `
            "Boot $sequence did not restore the scheduler-thread baseline."
        $serialHash = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant()
        $injectionHash = (Get-FileHash -LiteralPath $injections -Algorithm SHA256).Hash.ToUpperInvariant()
        $timelineHash = (Get-FileHash -LiteralPath $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
        $commandLineHash = (Get-FileHash -LiteralPath $commandLinePath -Algorithm SHA256).Hash.ToUpperInvariant()
        $firmwareIdentityHash = (Get-FileHash -LiteralPath $firmwareIdentityPath -Algorithm SHA256).Hash.ToUpperInvariant()
        Write-Output ("MANAGED_KERNEL_PHASE10_QEMU_RUN_{0}=PASS bytes={1} serial_sha256={2} injections_sha256={3} timeline_sha256={4} commandline_sha256={5} firmware_identity_sha256={6} serial={7} wake_requests={8}" -f `
            $sequence, ([Text.Encoding]::ASCII.GetByteCount($finalText)), $serialHash,
            $injectionHash, $timelineHash, $commandLineHash, $firmwareIdentityHash,
            $serial, $wakeRequests)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu10 $process }
}
Require10 (@(Get-OwnedQemu10).Count -eq 0) 'Owned QEMU cleanup failed.'
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE10_QEMU_RUNS=$RunCount"

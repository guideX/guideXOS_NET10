[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [ValidateSet('None', 'AttachFailureDiscard', 'IdleStop')]
    [string]$Phase69FixtureMode = 'None',
    [ValidateSet('None', 'RecoverableRestart', 'ReplacementAdmissionFailure')]
    [string]$Phase70FixtureMode = 'None',
    [switch]$EnablePhase72ExplicitRestartFixture,
    [switch]$EnablePhase75Com2DiagnosticIngress,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($EnablePhase75Com2DiagnosticIngress) {
    Import-Module (Join-Path $PSScriptRoot 'Phase75DiagnosticClient.psm1') -Force
}
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll'
$expectedHash = $PayloadSha256.ToUpperInvariant()

if ($EnablePhase72ExplicitRestartFixture -and
    $Phase70FixtureMode -ne 'ReplacementAdmissionFailure') {
    throw 'Phase 72 acceptance requires the Phase 71 replacement-admission failure fixture.'
}
if ($EnablePhase75Com2DiagnosticIngress -and -not $EnablePhase72ExplicitRestartFixture) {
    throw 'Phase 75 acceptance requires the Phase 72 and Phase 71 fixtures.'
}

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
            while (!$attempt.IsCompleted -and (Get-Date) -lt $deadline) {
                if ($process.HasExited) {
                    throw "QEMU exited before serial connection on port $port."
                }
                Start-Sleep -Milliseconds 25
            }
            if ($attempt.IsCompleted) {
                [void]$attempt.GetAwaiter().GetResult()
                if ($client.Connected) { return $client }
            }
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

function Send-GxdcRunnerRequest([uint16]$commandId, [uint32]$requestId,
                                [uint32]$expectedIdentity,
                                [uint16]$expectedGeneration) {
    $frame = New-GxdcRequest -CommandId $commandId -RequestId $requestId `
        -ExpectedFailedIdentity $expectedIdentity `
        -ExpectedFailedGeneration $expectedGeneration
    $script:phase75DiagnosticTranscript.WriteLine((
        'request id={0} command={1} bytes={2}' -f $requestId, $commandId,
        [Convert]::ToHexString($frame)))
    $script:phase75DiagnosticTranscript.Flush()
    try {
        $response = Send-GxdcRequest -Stream $script:phase75DiagnosticStream `
            -Frame $frame -RequestId $requestId -TimeoutMilliseconds 45000
    } catch {
        if ($null -ne $script:phase10PumpStream) {
            Pump-Serial10 $script:phase10PumpStream $script:phase10PumpLogStream `
                $script:phase10PumpText $script:phase10PumpBuffer
        }
        throw
    }
    $logResponse = [ordered]@{}
    foreach ($key in $response.Keys) {
        if ($key -ne 'Bytes') { $logResponse[$key] = $response[$key] }
    }
    $script:phase75DiagnosticTranscript.WriteLine(
        ($logResponse | ConvertTo-Json -Depth 5 -Compress))
    $script:phase75DiagnosticTranscript.Flush()
    return $response
}

function Send-GxdcRunnerMalformed([byte[]]$frame, [string]$caseName) {
    Write-GxdcBytesPaced -Stream $script:phase75DiagnosticStream -Bytes $frame
    Wait-GxdcSilence -Stream $script:phase75DiagnosticStream -Milliseconds 150
    $script:phase75DiagnosticTranscript.WriteLine(
        "malformed case=$caseName bytes=$([Convert]::ToHexString($frame)) response=none")
    $script:phase75DiagnosticTranscript.Flush()
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
    'GXOS_NET10:PHASE69_ONE_SHOT_SUBMITTED_LIVE_BEFORE_SERVICE_STOP=1',
    'GXOS_NET10:PHASE69_ONE_SHOT_HELD_PENDING_FOR_SERVICE_STOP=1',
    'GXOS_NET10:PHASE69_JOINT_ADMISSION_REJECTED_BEFORE_ALLOCATION=1',
    'GXOS_NET10:PHASE69_REJECTED_ADMISSION_NO_TCB_OR_WAKE_EVENT=1',
    'GXOS_NET10:PHASE69_ROUTE_ACCEPTANCE_CLOSED_BEFORE_HARDWARE_STOP=1',
    'GXOS_NET10:PHASE69_HARDWARE_ROUTES_DISABLED_BEFORE_WORKER_EXIT=1',
    'GXOS_NET10:PHASE69_THREADSTORE_BASELINE=',
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
$requiredMarkers += switch ($Phase70FixtureMode) {
    'RecoverableRestart' {
        @('GXOS_NET10:PHASE70_RESTART_FIXTURE_BEGIN=1',
          'GXOS_NET10:PHASE70_FAILURE_POINT=AFTER_SECOND_MANAGED_DISPATCH_RETURN',
          'GXOS_NET10:PHASE70_RECOVERABLE_FAILURE_INJECTED=1',
          'GXOS_NET10:PHASE70_RECOVERABLE_FAILURE_CLASSIFIED=1',
          'GXOS_NET10:PHASE70_ROUTE_DISABLED_BEFORE_RECLAIM=1',
          'GXOS_NET10:PHASE70_RESTART_RECOVERY_DISCARDED=',
          'GXOS_NET10:PHASE70_OVERFLOW_COUNT_UNCHANGED=1',
          'GXOS_NET10:PHASE70_SHUTDOWN_DISCARD_COUNT_UNCHANGED=1',
          'GXOS_NET10:PHASE70_OLD_GENERATION_RECLAIMED=1',
          'GXOS_NET10:PHASE70_REPLACEMENT_RUNTIME_ATTACHED=1',
          'GXOS_NET10:PHASE70_ROUTE_REENABLED=1',
          'GXOS_NET10:PHASE70_STALE_OLD_HANDLE_REJECTED=1',
          'GXOS_NET10:PHASE70_STALE_OLD_STOP_REJECTED=1',
          'GXOS_NET10:PHASE70_ONE_SHOT_LIVE_BEFORE_FAILURE=1',
          'GXOS_NET10:PHASE70_REPLACEMENT_ADMITTED_WITH_ONE_SHOT_LIVE=1',
          'GXOS_NET10:PHASE70_ONE_SHOT_SURVIVED_RESTART_RESULT_42=1',
          'GXOS_NET10:PHASE70_REPLACEMENT_READY=1',
          'GXOS_NET10:PHASE70_POST_RESTART_REAL_EVENT_DISPATCHED=1',
          'GXOS_NET10:PHASE70_DEVICE_GLOBAL_RECEIVE_STATE_PRESERVED=1',
          'GXOS_NET10:PHASE70_NORMAL_DRAIN_NO_RESTART=1',
          'GXOS_NET10:PHASE70_THREADSTORE_FINAL=',
          'GXOS_NET10:PERSISTENT_SERVICE_FAILURE_CAUSE=',
          'GXOS_NET10:PERSISTENT_SERVICE_FAILURE_QUEUE_COUNT=',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_BUDGET_BEFORE=',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_BUDGET_AFTER=',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_DECISION=ATTEMPT',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_GENERATION_RECLAIMED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_RUNTIME_ATTACH_COUNT_AFTER=',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_RUNTIME_DETACH_COUNT_AFTER=',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_TCB_RECLAIMED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_STACK_RECLAIMED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_THREAD_HANDLE_RECLAIMED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_WAKE_EVENT_RECLAIMED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_OWNER_RECORD_RELEASED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_RUNTIME_ATTACH_COUNT=',
          'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_RUNTIME_DETACH_COUNT=',
          'GXOS_NET10:PERSISTENT_SERVICE_OBJECTS_FREE_AFTER_TEARDOWN=',
          'GXOS_NET10:PERSISTENT_SERVICE_OBJECTS_FREE_REPLACEMENT_RUNNING=',
          'GXOS_NET10:PERSISTENT_SERVICE_THREADS_FREE_AFTER_TEARDOWN=',
          'GXOS_NET10:PERSISTENT_SERVICE_THREADS_FREE_REPLACEMENT_RUNNING=',
          'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_IDENTITY=',
          'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_GENERATION=',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_SUCCEEDED=1',
          'GXOS_NET10:PHASE69_ONE_SHOT_HELD_PENDING_FOR_SERVICE_STOP=1')
    }
    'ReplacementAdmissionFailure' {
        @('GXOS_NET10:PHASE70_RESTART_FIXTURE_BEGIN=1',
          'GXOS_NET10:PHASE70_FAILURE_POINT=AFTER_SECOND_MANAGED_DISPATCH_RETURN',
          'GXOS_NET10:PHASE70_RECOVERABLE_FAILURE_INJECTED=1',
          'GXOS_NET10:PHASE71_GENERATION1_OPERATIONAL=1',
          'GXOS_NET10:PHASE71_GENERATION1_REAL_COM1_EVENT=1',
          'GXOS_NET10:PHASE71_ADMISSION_FAILURE_ARMED_AFTER_INITIAL_START=1',
          'GXOS_NET10:PHASE71_ADMISSION_FAILURE_INJECTION_FIRED=1',
          'GXOS_NET10:PHASE71_ADMISSION_NATURAL_RESULT=',
          'GXOS_NET10:PHASE71_ADMISSION_STATUS=',
          'GXOS_NET10:PHASE71_OLD_GENERATION_FULLY_RECLAIMED=1',
          'GXOS_NET10:PHASE71_REPLACEMENT_ADMISSION_ATTEMPTED=1',
          'GXOS_NET10:PHASE71_REPLACEMENT_ADMISSION_STATUS=',
          'GXOS_NET10:PHASE71_REPLACEMENT_IDENTITY_ALLOCATED=0',
          'GXOS_NET10:PHASE71_REPLACEMENT_TCB_ALLOCATED=0',
          'GXOS_NET10:PHASE71_REPLACEMENT_WAKE_EVENT_ALLOCATED=0',
          'GXOS_NET10:PHASE71_REPLACEMENT_RUNTIME_ATTACHED=0',
          'GXOS_NET10:PHASE71_ROUTE_REENABLED=0',
          'GXOS_NET10:PHASE71_NO_PARTIAL_REPLACEMENT=1',
          'GXOS_NET10:PHASE71_NO_SECOND_AUTOMATIC_RETRY=1',
          'GXOS_NET10:PHASE71_RESTART_BUDGET_EXHAUSTED=1',
          'GXOS_NET10:PHASE71_OWNER_STATE_RESTART_FAILED=1',
          'GXOS_NET10:PHASE71_ROUTE_DISABLED=1',
          'GXOS_NET10:PHASE71_OLD_HANDLE_REJECTED=1',
          'GXOS_NET10:PHASE71_OLD_STOP_REJECTED=1',
          'GXOS_NET10:PHASE71_OLD_WAKE_AUTHORITY_REJECTED=1',
          'GXOS_NET10:PHASE71_QUEUE_COUNT_AT_FAILURE=',
          'GXOS_NET10:PHASE71_RECOVERY_DISCARDED=',
          'GXOS_NET10:PHASE71_OVERFLOW_ACCOUNTING_UNCHANGED=1',
          'GXOS_NET10:PHASE71_SHUTDOWN_DISCARD_ACCOUNTING_UNCHANGED=1',
          'GXOS_NET10:PHASE71_ONE_SHOT_SURVIVED_FAILED_RESTART_RESULT_42=1',
          'GXOS_NET10:PHASE71_FAILED_RESTART_BASELINE_RESTORED=1',
          'GXOS_NET10:PHASE71_MANUAL_NEW_EPISODE=1',
          'GXOS_NET10:PHASE71_MANUAL_IDENTITY=',
          'GXOS_NET10:PHASE71_MANUAL_GENERATION=',
          'GXOS_NET10:PHASE71_MANUAL_RESTART_BUDGET=',
          'GXOS_NET10:PHASE71_MANUAL_COM1_ROUTE_ENABLED=1',
          'GXOS_NET10:PHASE71_MANUAL_SERVICE_READY=1',
          'GXOS_NET10:PHASE71_MANUAL_COM1_EVENT_DISPATCHED=1',
          'GXOS_NET10:PHASE71_MANUAL_MANAGED_DISPATCHES=',
          'GXOS_NET10:PHASE71_MANUAL_NORMAL_DRAIN_NO_RESTART=1',
          'GXOS_NET10:PHASE70_THREADSTORE_FINAL=',
          'GXOS_NET10:PERSISTENT_SERVICE_FAILURE_CAUSE=',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_BUDGET_BEFORE=',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_BUDGET_AFTER=',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_DECISION=ATTEMPT',
          'GXOS_NET10:PERSISTENT_SERVICE_OLD_GENERATION_RECLAIMED=1',
          'GXOS_NET10:PERSISTENT_SERVICE_RESTART_ADMISSION_RESULT=',
          'GXOS_NET10:PHASE69_ONE_SHOT_HELD_PENDING_FOR_SERVICE_STOP=1')
    }
}
if ($EnablePhase72ExplicitRestartFixture) {
    $requiredMarkers = @($requiredMarkers | Where-Object {
        $_ -ne 'GXOS_NET10:PHASE71_MANUAL_NEW_EPISODE=1'
    })
    $requiredMarkers += @(
        'GXOS_NET10:PHASE72_STATUS_WHILE_RUNNING=1',
        'GXOS_NET10:PHASE72_RESTART_WHILE_RUNNING_REJECTED=1',
        'GXOS_NET10:PHASE72_STATUS_BEFORE_EXPLICIT_RESTART=1',
        'GXOS_NET10:PHASE72_STALE_RESTART_REQUEST_REJECTED=1',
        'GXOS_NET10:PHASE72_EXPLICIT_ADMISSION_FAILURE_CONTAINED=1',
        'GXOS_NET10:PHASE72_EXPLICIT_RESTART_ACCEPTED=1',
        'GXOS_NET10:PHASE72_NEW_EPISODE=1',
        'GXOS_NET10:PHASE72_NEW_GENERATION_READY=1',
        'GXOS_NET10:PHASE72_STATUS_AFTER_RESTART_HEALTHY=1',
        'GXOS_NET10:PHASE72_OLD_HANDLE_STOP_REJECTED=1',
        'GXOS_NET10:PHASE72_OLD_RESTART_REQUEST_REJECTED=1',
        'GXOS_NET10:PHASE72_ONE_SHOT_REMAINS_PENDING_AFTER_RESTART=1',
        'GXOS_NET10:PHASE72_REAL_COM1_EVENT_MANAGED_DISPATCHED=1',
        'GXOS_NET10:PHASE72_STATUS_WHILE_STOPPING=1',
        'GXOS_NET10:PHASE72_STATUS_AFTER_DRAIN=1',
        'GXOS_NET10:PHASE72_STATUS_AFTER_NORMAL_STOP=1',
        'GXOS_NET10:PHASE72_RESOURCE_BASELINE_RESTORED=1',
        'GXOS_NET10:PHASE72_QUARANTINE_RESTART_REJECTED=1',
        'GXOS_NET10:PHASE72_ONE_SHOT_ADD_ONE_41_TO_42=1',
        'GXOS_NET10:PHASE72_PRE_STATE=',
        'GXOS_NET10:PHASE72_PRE_DEVICE=',
        'GXOS_NET10:PHASE72_PRE_CURRENT_VALID=',
        'GXOS_NET10:PHASE72_PRE_ROUTE_ENABLED=',
        'GXOS_NET10:PHASE72_PRE_FAILURE_REASON=',
        'GXOS_NET10:PHASE72_PRE_LAST_FAILED_IDENTITY=',
        'GXOS_NET10:PHASE72_PRE_LAST_FAILED_GENERATION=',
        'GXOS_NET10:PHASE72_PRE_BUDGET=',
        'GXOS_NET10:PHASE72_PRE_ELIGIBLE=',
        'GXOS_NET10:PHASE72_EXPLICIT_FAILURE_RESULT=',
        'GXOS_NET10:PHASE72_BUDGET_AFTER_EXPLICIT_FAILURE=',
        'GXOS_NET10:PHASE72_POST_CURRENT_IDENTITY=',
        'GXOS_NET10:PHASE72_POST_CURRENT_GENERATION=',
        'GXOS_NET10:PHASE72_POST_BUDGET=',
        'GXOS_NET10:PHASE72_POST_LAST_FAILURE=')
}
if ($EnablePhase75Com2DiagnosticIngress) {
    $requiredMarkers += @(
        'GXOS_NET10:PHASE75_DIAGNOSTIC_UART_PRESENT=1',
        'GXOS_NET10:PHASE75_DIAGNOSTIC_UART_ENABLED=1',
        'GXOS_NET10:PHASE75_RESTART_FAILED_DIAGNOSTIC_READY=1',
        'GXOS_NET10:PHASE75_RESTART_DISPATCHED_FROM_BOOT_THREAD=1',
        'GXOS_NET10:PHASE75_COM2_REMAINED_ENABLED_DURING_FAILURE=1',
        'GXOS_NET10:PHASE75_POST_RESTART_CONTROL_COMPLETE=1',
        'GXOS_NET10:PHASE75_DIAGNOSTIC_ALIVE_AFTER_COM1_STOP=1',
        'GXOS_NET10:PHASE75_IRQ_CAPTURE_ONLY=1',
        'GXOS_NET10:PHASE75_BOOT_THREAD_DISPATCH=1')
}
$requiredMarkers += switch ($Phase69FixtureMode) {
    'None' {
        $markers = @('GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_CAPTURED',
                     'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_REQUESTED=1',
                     'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_COMPLETE=1')
        if ($Phase70FixtureMode -eq 'None') {
            $markers += @('GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_OK',
                          'GXOS_NET10:MANAGED_KERNEL_DRIVER_WAKE_COALESCE_OK',
                          'GXOS_NET10:PHASE69_PERSISTENT_STATE_ACROSS_REAL_EVENTS=1',
                          'GXOS_NET10:PHASE69_ONE_SHOT_STILL_SUBMITTED_AFTER_SERVICE_STOP=1',
                          'GXOS_NET10:PHASE69_ONE_SHOT_SURVIVED_SERVICE_STOP=1',
                          'GXOS_NET10:PHASE69_ONE_SHOT_RECLAIMED_AFTER_SERVICE_STOP=1',
                          'GXOS_NET10:PHASE69_THREADSTORE_FINAL=')
        }
        $markers
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
          'GXOS_NET10:PERSISTENT_SERVICE_DRAIN_COMPLETE=1',
          'GXOS_NET10:PHASE69_PERSISTENT_STATE_ACROSS_REAL_EVENTS=1',
          'GXOS_NET10:PHASE69_ONE_SHOT_STILL_SUBMITTED_AFTER_SERVICE_STOP=1',
          'GXOS_NET10:PHASE69_ONE_SHOT_SURVIVED_SERVICE_STOP=1',
          'GXOS_NET10:PHASE69_ONE_SHOT_RECLAIMED_AFTER_SERVICE_STOP=1',
          'GXOS_NET10:PHASE69_THREADSTORE_FINAL=')
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
          'GXOS_NET10:PERSISTENT_SERVICE_DISCARD_COMPLETE=1',
          'GXOS_NET10:PHASE69_PERSISTENT_STATE_ACROSS_REAL_EVENTS=1',
          'GXOS_NET10:PHASE69_ONE_SHOT_STILL_SUBMITTED_AFTER_SERVICE_STOP=1',
          'GXOS_NET10:PHASE69_ONE_SHOT_SURVIVED_SERVICE_STOP=1',
          'GXOS_NET10:PHASE69_ONE_SHOT_RECLAIMED_AFTER_SERVICE_STOP=1',
          'GXOS_NET10:PHASE69_THREADSTORE_FINAL=')
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
        $diagnosticPort = 0
        if ($EnablePhase75Com2DiagnosticIngress) {
            do {
                $diagnosticProbe = [Net.Sockets.TcpListener]::new(
                    [Net.IPAddress]::Loopback, 0)
                $diagnosticProbe.Start()
                $diagnosticPort = ([Net.IPEndPoint]$diagnosticProbe.LocalEndpoint).Port
                $diagnosticProbe.Stop()
            } while ($diagnosticPort -eq $port)
        }
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
        if ($EnablePhase75Com2DiagnosticIngress) {
            $diagnosticArguments = @(
                '-chardev', "socket,id=diag0,host=127.0.0.1,port=$diagnosticPort,server=on,wait=on,telnet=off,ipv4=on,nodelay=on",
                '-device', 'isa-serial,chardev=diag0,iobase=0x2f8,irq=3,wakeup=on')
            $monitorIndex = [Array]::IndexOf($arguments, '-monitor')
            $arguments = @($arguments[0..($monitorIndex - 1)]) +
                $diagnosticArguments + @($arguments[$monitorIndex..($arguments.Count - 1)])
        }
        $commandLine = '"{0}" {1}' -f $qemu, ($arguments -join ' ')
        Set-Content -LiteralPath $commandLinePath -Value $commandLine -Encoding ascii
        $process = $null
        $client = $null
        $stream = $null
        $diagnosticClient = $null
        $diagnosticStream = $null
        $diagnosticTranscript = $null
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
            if ($EnablePhase75Com2DiagnosticIngress) {
                $diagnosticClient = Connect-QemuSerial10 $diagnosticPort $process $deadline
                $diagnosticClient.Client.NoDelay = $true
                $diagnosticStream = $diagnosticClient.GetStream()
                $diagnosticTranscript = [IO.StreamWriter]::new(
                    (Join-Path $run 'diagnostic-protocol.log'), $false,
                    [Text.Encoding]::ASCII)
                $script:phase75DiagnosticStream = $diagnosticStream
                $script:phase75DiagnosticTranscript = $diagnosticTranscript
                Write-Timeline10 $timeline 'DIAGNOSTIC_CONNECTED' `
                    "host=127.0.0.1 port=$diagnosticPort device=COM2"
            }
            $logStream = [IO.File]::Open($serial, [IO.FileMode]::Create,
                [IO.FileAccess]::Write, [IO.FileShare]::Read)
            $injectionLog = [IO.StreamWriter]::new($injections, $false,
                [Text.Encoding]::ASCII)
            $text = [Text.StringBuilder]::new()
            $script:phase10Tail = ''
            $script:phase10ReadTask = $null
            $buffer = New-Object byte[] 4096
            $script:phase10PumpStream = $stream
            $script:phase10PumpLogStream = $logStream
            $script:phase10PumpText = $text
            $script:phase10PumpBuffer = $buffer

            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' `
                $deadline $process $stream $logStream $text $buffer
            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' `
                $deadline $process $stream $logStream $text $buffer
            if ($EnablePhase75Com2DiagnosticIngress) {
                Wait-Marker10 'GXOS_NET10:PHASE75_DIAGNOSTIC_UART_ENABLED=1' `
                    $deadline $process $stream $logStream $text $buffer
            }
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
            if ($EnablePhase75Com2DiagnosticIngress) {
                Wait-Marker10 'GXOS_NET10:PHASE75_HEALTHY_CONTROL_READY=1' `
                    $deadline $process $stream $logStream $text $buffer
                $healthy = Send-GxdcRunnerRequest 1 1 0 0
                Require10 ($healthy.ApiResult -eq 0 -and $healthy.Length -eq 100 -and
                    $healthy.Status.DeviceIdentity -eq 1 -and
                    $healthy.Status.CurrentValid -eq 1 -and
                    $healthy.Status.CurrentIdentity -ne 0 -and
                    $healthy.Status.CurrentGeneration -ne 0 -and
                    $healthy.Status.RouteEnabled -eq 1 -and
                    $healthy.Status.RuntimeAttached -eq 1 -and
                    $healthy.Status.OwnerState -eq 5 -and
                    $healthy.Status.ExplicitRestartAllowed -eq 0) `
                    'COM2 healthy STATUS did not report the running COM1 generation.'
                $runningIdentity = [uint32]$healthy.Status.CurrentIdentity
                $runningGeneration = [uint16]$healthy.Status.CurrentGeneration
                $healthyRepeat = Send-GxdcRunnerRequest 1 2 0 0
                Require10 ($healthyRepeat.ApiResult -eq 0 -and
                    $healthyRepeat.Status.CurrentIdentity -eq $runningIdentity -and
                    $healthyRepeat.Status.CurrentGeneration -eq $runningGeneration) `
                    'COM2 did not process a second valid STATUS request.'
                $runningRestart = Send-GxdcRunnerRequest 2 3 `
                    $runningIdentity $runningGeneration
                Require10 ($runningRestart.ApiResult -eq 2 -and
                    $runningRestart.Length -eq 24) `
                    'COM2 RESTART while running was not rejected as INVALID_STATE.'

                $badCrc = New-GxdcRequest -CommandId 1 -RequestId 4
                $badCrc[28] = $badCrc[28] -bxor 1
                Send-GxdcRunnerMalformed $badCrc 'bad-crc'
                $badVersion = New-GxdcRequest -CommandId 1 -RequestId 5
                Set-GxdcU16 $badVersion 4 2
                Set-GxdcU32 $badVersion 28 (Get-GxdcCrc32 $badVersion 28)
                Send-GxdcRunnerMalformed $badVersion 'bad-version'
                $badSize = New-GxdcRequest -CommandId 1 -RequestId 6
                Set-GxdcU16 $badSize 6 31
                Set-GxdcU32 $badSize 28 (Get-GxdcCrc32 $badSize 28)
                Send-GxdcRunnerMalformed $badSize 'bad-size'
                $badTarget = New-GxdcRequest -CommandId 1 -RequestId 7
                Set-GxdcU32 $badTarget 16 2
                Set-GxdcU32 $badTarget 28 (Get-GxdcCrc32 $badTarget 28)
                Send-GxdcRunnerMalformed $badTarget 'wrong-target'
                [byte[]]$garbageFragment = @(0xD0, 0x47, 0x58, 0x44, 0x43, 0x01, 0x00)
                Write-GxdcBytesPaced -Stream $diagnosticStream -Bytes $garbageFragment
                $resynchronized = Send-GxdcRunnerRequest 1 8 0 0
                Require10 ($resynchronized.ApiResult -eq 0 -and
                    $resynchronized.Status.CurrentIdentity -eq $runningIdentity -and
                    $resynchronized.Status.CurrentGeneration -eq $runningGeneration) `
                    'GXDC parser did not resynchronize after garbage and a truncated frame.'
                Write-Timeline10 $timeline 'GXDC_GARBAGE_RESYNCHRONIZED' 'status=PASS'
                Wait-Marker10 'GXOS_NET10:PHASE75_HEALTHY_CONTROL_COMPLETE=1' `
                    $deadline $process $stream $logStream $text $buffer
            }

            Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' `
                $deadline $process $stream $logStream $text $buffer
            Send-SerialByte10 $client $stream $process $injectionLog `
                'RX_RUNTIME_SURVIVAL_NATIVE_OK' 0x53
            if ($Phase70FixtureMode -eq 'RecoverableRestart') {
                Wait-Marker10 'GXOS_NET10:PHASE70_REPLACEMENT_READY=1' `
                    $deadline $process $stream $logStream $text $buffer
                Send-SerialByte10 $client $stream $process $injectionLog `
                    'PHASE70_REPLACEMENT_READY' 0x54
                Wait-Marker10 'GXOS_NET10:PHASE70_POST_RESTART_REAL_EVENT_DISPATCHED=1' `
                    $deadline $process $stream $logStream $text $buffer
            } elseif ($Phase70FixtureMode -eq 'ReplacementAdmissionFailure') {
                if ($EnablePhase75Com2DiagnosticIngress) {
                    Wait-Marker10 'GXOS_NET10:PHASE75_RESTART_FAILED_DIAGNOSTIC_READY=1' `
                        $deadline $process $stream $logStream $text $buffer
                    $failed = Send-GxdcRunnerRequest 1 9 0 0
                    Require10 ($failed.ApiResult -eq 0 -and $failed.Length -eq 100 -and
                        $failed.Status.OwnerState -eq 14 -and
                        $failed.Status.DeviceIdentity -eq 1 -and
                        $failed.Status.CurrentValid -eq 0 -and
                        $failed.Status.CurrentIdentity -eq 0 -and
                        $failed.Status.CurrentGeneration -eq 0 -and
                        $failed.Status.RouteEnabled -eq 0 -and
                        $failed.Status.RuntimeAttached -eq 0 -and
                        $failed.Status.FailureReason -eq 3 -and
                        $failed.Status.LastFailedIdentity -eq $runningIdentity -and
                        $failed.Status.LastFailedGeneration -eq $runningGeneration -and
                        $failed.Status.LastFailedDeviceIdentity -eq 1 -and
                        $failed.Status.RestartBudgetRemaining -eq 0 -and
                        $failed.Status.AutomaticRestartAttempts -eq 1 -and
                        $failed.Status.RestartFailed -eq 1 -and
                        $failed.Status.ExplicitRestartAllowed -eq 1) `
                        'COM2 STATUS did not report the settled RESTART_FAILED COM1 service.'
                    $capacityRestart = Send-GxdcRunnerRequest 2 10 `
                        $failed.Status.LastFailedIdentity $failed.Status.LastFailedGeneration
                    Require10 ($capacityRestart.ApiResult -eq 6 -and
                        $capacityRestart.Length -eq 24) `
                        'Injected Phase 72 capacity result was not returned through COM2.'
                    $restart = Send-GxdcRunnerRequest 2 11 `
                        $failed.Status.LastFailedIdentity $failed.Status.LastFailedGeneration
                    Require10 ($restart.ApiResult -eq 0 -and $restart.Length -eq 36 -and
                        $restart.Handle.DeviceIdentity -eq 1 -and
                        $restart.Handle.Identity -ne $failed.Status.LastFailedIdentity -and
                        $restart.Handle.Generation -gt $failed.Status.LastFailedGeneration -and
                        $restart.Handle.Slot -eq 0) `
                        'COM2 explicit restart did not return a new COM1 service handle.'
                    Write-Timeline10 $timeline 'GXDC_RESTART_FAILED_STATUS' `
                        'route=0 runtime=0 budget=0 eligible=1'
                    Write-Timeline10 $timeline 'GXDC_EXPLICIT_RESTART' `
                        "result=OK identity=$($restart.Handle.Identity) generation=$($restart.Handle.Generation)"
                    Wait-Marker10 'GXOS_NET10:PHASE71_MANUAL_SERVICE_READY=1' `
                        $deadline $process $stream $logStream $text $buffer
                    $stale = Send-GxdcRunnerRequest 2 12 `
                        $failed.Status.LastFailedIdentity $failed.Status.LastFailedGeneration
                    Require10 (($stale.ApiResult -eq 2 -or $stale.ApiResult -eq 5) -and
                        $stale.Length -eq 24) `
                        'Stale Phase 75 RESTART did not return bounded STALE or INVALID_STATE.'
                    $runningAgain = Send-GxdcRunnerRequest 2 13 `
                        $restart.Handle.Identity $restart.Handle.Generation
                    Require10 ($runningAgain.ApiResult -eq 2 -and $runningAgain.Length -eq 24) `
                        'COM2 RESTART against the new running generation did not return INVALID_STATE.'
                    $newHealthy = Send-GxdcRunnerRequest 1 14 0 0
                    Require10 ($newHealthy.ApiResult -eq 0 -and
                        $newHealthy.Status.CurrentIdentity -eq $restart.Handle.Identity -and
                        $newHealthy.Status.CurrentGeneration -eq $restart.Handle.Generation -and
                        $newHealthy.Status.RouteEnabled -eq 1 -and
                        $newHealthy.Status.RuntimeAttached -eq 1) `
                        'COM2 STATUS did not confirm route/runtime restoration after restart.'
                    Wait-Marker10 'GXOS_NET10:PHASE75_POST_RESTART_CONTROL_COMPLETE=1' `
                        $deadline $process $stream $logStream $text $buffer
                    Send-SerialByte10 $client $stream $process $injectionLog `
                        'PHASE75_COM2_RESTARTED_COM1' 0x54
                    Wait-Marker10 'GXOS_NET10:PHASE71_MANUAL_COM1_EVENT_DISPATCHED=1' `
                        $deadline $process $stream $logStream $text $buffer
                    Write-Timeline10 $timeline 'COM1_REAL_EVENT_AFTER_DIAGNOSTIC_RESTART' `
                        'managed_dispatch=PASS'
                } else {
                Wait-Marker10 'GXOS_NET10:PHASE71_MANUAL_SERVICE_READY=1' `
                    $deadline $process $stream $logStream $text $buffer
                Send-SerialByte10 $client $stream $process $injectionLog `
                    'PHASE71_MANUAL_SERVICE_READY' 0x54
                Wait-Marker10 'GXOS_NET10:PHASE71_MANUAL_COM1_EVENT_DISPATCHED=1' `
                    $deadline $process $stream $logStream $text $buffer
                }
            } else {
                Wait-Marker10 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' `
                    $deadline $process $stream $logStream $text $buffer
            }

            if ($Phase69FixtureMode -eq 'IdleStop') {
                Wait-Marker10 'GXOS_NET10:PHASE69_IDLE_STOP_READY=1' `
                    $deadline $process $stream $logStream $text $buffer
            } else {
                Send-SerialBurst10 $client $stream $process $injectionLog `
                    $(if ($Phase70FixtureMode -eq 'RecoverableRestart') {
                        'PHASE70_POST_RESTART_EVENT' } elseif (
                        $Phase70FixtureMode -eq 'ReplacementAdmissionFailure') {
                        'PHASE71_MANUAL_COM1_EVENT' } else {
                        'RX_AFTER_RUNTIME_OK_BURST'}) ([byte[]](0x41, 0x42, 0x43))
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
            if ($EnablePhase75Com2DiagnosticIngress) {
                Wait-Marker10 'GXOS_NET10:PHASE72_STATUS_AFTER_NORMAL_STOP=1' `
                    $deadline $process $stream $logStream $text $buffer
                $stopped = Send-GxdcRunnerRequest 1 15 0 0
                Require10 ($stopped.ApiResult -eq 0 -and $stopped.Length -eq 100 -and
                    $stopped.Status.DeviceIdentity -eq 1 -and
                    $stopped.Status.CurrentValid -eq 0 -and
                    $stopped.Status.RouteEnabled -eq 0 -and
                    $stopped.Status.RuntimeAttached -eq 0) `
                    'COM2 STATUS did not remain available after COM1 service stop.'
                Wait-Marker10 'GXOS_NET10:PHASE75_DIAGNOSTIC_ALIVE_AFTER_COM1_STOP=1' `
                    $deadline $process $stream $logStream $text $buffer
            }
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
            if ($null -ne $diagnosticTranscript) { $diagnosticTranscript.Dispose() }
            if ($null -ne $diagnosticStream) { $diagnosticStream.Dispose() }
            if ($null -ne $diagnosticClient) { $diagnosticClient.Dispose() }
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
        if ($EnablePhase75Com2DiagnosticIngress) {
            Require10 ((Get-HexField10 $finalText 'GXOS_NET10:PHASE75_STATUS_API_COUNT=') -eq 6 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE75_RESTART_API_COUNT=') -eq 5 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE75_RX_OVERFLOW_COUNT=') -eq 0) `
                "Boot $sequence called service APIs for malformed requests or lost diagnostic bytes."
            Require10 (Test-Path -LiteralPath (Join-Path $run 'diagnostic-protocol.log')) `
                "Boot $sequence did not preserve its COM2 protocol transcript."
        }
        Require10 (([regex]::Matches($finalText, 'GXOS_NET10:MANAGED_KERNEL_PHASE10_PASS')).Count -eq 1) `
            "Boot $sequence repeated or omitted the Phase 10 pass marker."
        $expectedEnqueued = if ($Phase70FixtureMode -in @(
                'RecoverableRestart', 'ReplacementAdmissionFailure')) {
            6
        } else {
            switch ($Phase69FixtureMode) {
                'None' { 5 }
                'IdleStop' { 2 }
                'AttachFailureDiscard' { 8 }
            }
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
        } elseif ($Phase70FixtureMode -eq 'RecoverableRestart') {
            $failurePending = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_FAILURE_QUEUE_COUNT='
            $recoveryDiscarded = Get-HexField10 $finalText 'GXOS_NET10:PHASE70_RESTART_RECOVERY_DISCARDED='
            $oldIdentity = Get-HexField10 $finalText 'GXOS_NET10:PHASE70_OLD_SERVICE_IDENTITY='
            $oldGeneration = Get-HexField10 $finalText 'GXOS_NET10:PHASE70_OLD_SERVICE_GENERATION='
            $deviceIdentity = Get-HexField10 $finalText 'GXOS_NET10:PHASE70_DEVICE_IDENTITY='
            $replacementIdentity = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_IDENTITY='
            $replacementGeneration = Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_GENERATION='
            Require10 ($discarded -eq 0 -and $failurePending -eq $recoveryDiscarded -and
                       $failurePending -le 8 -and $deviceIdentity -eq 1 -and
                       $oldIdentity -ne $replacementIdentity -and
                       $replacementGeneration -gt $oldGeneration -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE70_RESTART_BUDGET_BEFORE_FAILURE=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_RESTART_BUDGET_BEFORE=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_RESTART_BUDGET_AFTER=') -eq 0 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OLD_RUNTIME_ATTACH_COUNT_AFTER=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OLD_RUNTIME_DETACH_COUNT_AFTER=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_RUNTIME_ATTACH_COUNT=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_REPLACEMENT_RUNTIME_DETACH_COUNT=') -eq 0 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE70_POST_RESTART_MANAGED_DISPATCHES=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE70_THREADSTORE_FINAL=') -eq
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_THREADSTORE_BASELINE=')) `
                "Boot $sequence failed restart budget, generation, device, runtime, or event checks."
        } elseif ($Phase70FixtureMode -eq 'ReplacementAdmissionFailure') {
            $failurePending = Get-HexField10 $finalText 'GXOS_NET10:PHASE71_QUEUE_COUNT_AT_FAILURE='
            $recoveryDiscarded = Get-HexField10 $finalText 'GXOS_NET10:PHASE71_RECOVERY_DISCARDED='
            $oldIdentity = Get-HexField10 $finalText 'GXOS_NET10:PHASE70_OLD_SERVICE_IDENTITY='
            $oldGeneration = Get-HexField10 $finalText 'GXOS_NET10:PHASE70_OLD_SERVICE_GENERATION='
            $deviceIdentity = Get-HexField10 $finalText 'GXOS_NET10:PHASE70_DEVICE_IDENTITY='
            $manualIdentity = Get-HexField10 $finalText 'GXOS_NET10:PHASE71_MANUAL_IDENTITY='
            $manualGeneration = Get-HexField10 $finalText 'GXOS_NET10:PHASE71_MANUAL_GENERATION='
            Require10 ($discarded -eq 0 -and $failurePending -eq 0 -and
                       $recoveryDiscarded -eq 0 -and $deviceIdentity -eq 1 -and
                       $manualIdentity -ne $oldIdentity -and
                       $manualGeneration -gt $oldGeneration -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE70_RESTART_BUDGET_BEFORE_FAILURE=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE71_RESTART_BUDGET_AFTER_FAILURE=') -eq 0 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE71_ADMISSION_STATUS=') -eq 2 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OLD_RUNTIME_ATTACH_COUNT_AFTER=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OLD_RUNTIME_DETACH_COUNT_AFTER=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE71_MANUAL_RESTART_BUDGET=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE71_MANUAL_MANAGED_DISPATCHES=') -eq 1 -and
                       (Get-HexField10 $finalText 'GXOS_NET10:PHASE70_THREADSTORE_FINAL=') -eq
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_THREADSTORE_BASELINE=')) `
                "Boot $sequence failed replacement admission containment or manual-episode checks."
            if ($EnablePhase72ExplicitRestartFixture) {
                $phase72Identity = Get-HexField10 $finalText 'GXOS_NET10:PHASE72_POST_CURRENT_IDENTITY='
                $phase72Generation = Get-HexField10 $finalText 'GXOS_NET10:PHASE72_POST_CURRENT_GENERATION='
                Require10 ((Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_STATE=') -eq 14 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_DEVICE=') -eq 1 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_CURRENT_VALID=') -eq 0 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_ROUTE_ENABLED=') -eq 0 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_FAILURE_REASON=') -eq 3 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_LAST_FAILED_IDENTITY=') -eq $oldIdentity -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_LAST_FAILED_GENERATION=') -eq $oldGeneration -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_BUDGET=') -eq 0 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_PRE_ELIGIBLE=') -eq 1 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_EXPLICIT_FAILURE_RESULT=') -eq 6 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_BUDGET_AFTER_EXPLICIT_FAILURE=') -eq 1 -and
                           $phase72Identity -ne $oldIdentity -and
                           $phase72Generation -gt $oldGeneration -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_POST_BUDGET=') -eq 1 -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE72_POST_LAST_FAILURE=') -eq 1 -and
                           $finalText.Contains('GXOS_NET10:PHASE70_ONE_SHOT_SURVIVED_RESTART_RESULT_42=1') -and
                           (Get-HexField10 $finalText 'GXOS_NET10:PHASE70_THREADSTORE_FINAL=') -eq
                               (Get-HexField10 $finalText 'GXOS_NET10:PHASE69_THREADSTORE_BASELINE=')) `
                    "Boot $sequence failed Phase 72 health, explicit restart, event, one-shot, or reclaim checks."
            }
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
        $drainEvidenceValid = if ($Phase70FixtureMode -in @(
                'RecoverableRestart', 'ReplacementAdmissionFailure')) {
            $drainPending -eq 3 -and
                $finalText.Contains('GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PRECONDITION_PENDING=1')
        } else { switch ($Phase69FixtureMode) {
            'None' { $drainPending -gt 0 -and $drainPending -le 3 -and
                $finalText.Contains('GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PRECONDITION_PENDING=1') }
            'IdleStop' { $drainPending -eq 0 -and
                $finalText.Contains('GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PRECONDITION_IDLE=1') }
            'AttachFailureDiscard' { $drainPending -eq 6 -and
                $finalText.Contains('GXOS_NET10:PERSISTENT_SERVICE_DRAIN_PRECONDITION_PENDING=1') }
        } }
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
        $objectDeltaValid = if ($Phase70FixtureMode -eq 'RecoverableRestart') {
            $objectBaseline - $objectRunning -eq 2 -and
                $objectRunning - $objectPeak -eq 1 -and
                (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OBJECTS_FREE_AFTER_TEARDOWN=') -eq
                    $objectBaseline - 1 -and
                (Get-HexField10 $finalText 'GXOS_NET10:PERSISTENT_SERVICE_OBJECTS_FREE_REPLACEMENT_RUNNING=') -eq
                    $objectBaseline - 3 -and
                $objectFinal -eq $objectBaseline
        } elseif ($Phase70FixtureMode -eq 'ReplacementAdmissionFailure') {
            $expectedManualStartObjectsFree = if ($EnablePhase72ExplicitRestartFixture) {
                $objectBaseline - 3
            } else { $objectBaseline - 2 }
            $objectBaseline - $objectRunning -eq 2 -and
                $objectRunning - $objectPeak -eq 1 -and
                (Get-HexField10 $finalText 'GXOS_NET10:PHASE71_OBJECTS_FREE_AFTER_FAILURE=') -eq
                    $objectBaseline - 1 -and
                (Get-HexField10 $finalText 'GXOS_NET10:PHASE71_OBJECTS_FREE_AFTER_MANUAL_START=') -eq
                    $expectedManualStartObjectsFree -and
                $objectFinal -eq $objectBaseline
        } else {
            $objectBaseline - $objectRunning -eq 2 -and
                $objectRunning - $objectPeak -eq 1 -and
                $objectFinal -eq $objectBaseline
        }
        Require10 $objectDeltaValid `
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
        $diagnosticHash = if ($EnablePhase75Com2DiagnosticIngress) {
            (Get-FileHash -LiteralPath (Join-Path $run 'diagnostic-protocol.log') `
                -Algorithm SHA256).Hash.ToUpperInvariant()
        } else { 'NA' }
        Write-Output ("MANAGED_KERNEL_PHASE10_QEMU_RUN_{0}=PASS bytes={1} serial_sha256={2} injections_sha256={3} timeline_sha256={4} commandline_sha256={5} firmware_identity_sha256={6} serial={7} wake_requests={8}" -f `
            $sequence, ([Text.Encoding]::ASCII.GetByteCount($finalText)), $serialHash,
            $injectionHash, $timelineHash, $commandLineHash, $firmwareIdentityHash,
            $serial, $wakeRequests)
        if ($EnablePhase75Com2DiagnosticIngress) {
            Write-Output ("PHASE75_DIAGNOSTIC_PROTOCOL_SHA256_{0}={1} path={2}" -f `
                $sequence, $diagnosticHash, (Join-Path $run 'diagnostic-protocol.log'))
        }
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu10 $process }
}
Require10 (@(Get-OwnedQemu10).Count -eq 0) 'Owned QEMU cleanup failed.'
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE10_QEMU_RUNS=$RunCount"

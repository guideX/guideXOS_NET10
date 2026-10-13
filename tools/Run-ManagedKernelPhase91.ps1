[CmdletBinding()]
param(
    [string]$BuildDirectory = '',
    [string]$EvidenceDirectory = '',
    [int]$TimeoutSeconds = 240,
    [switch]$EnablePhase91Diagnostics
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $root 'artifacts\phase91-build'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'artifacts\phase91-qemu-evidence'
}
$build = [IO.Path]::GetFullPath($BuildDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$managedDirectory = Join-Path $build 'managed-kernel'
$normalGate = Join-Path $build 'gate4-normal'
$phase91Gate = Join-Path $build 'gate4-phase91'
$payload = Join-Path $managedDirectory 'publish\gxos-managed-kernel.dll'
New-Item -ItemType Directory -Force -Path $build | Out-Null

function Require91([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}
function ReadCounter91([string]$serial, [string]$name) {
    $prefix = [regex]::Escape("GXOS_NET10:${name}=0x")
    $match = [regex]::Match($serial, $prefix + '([0-9A-Fa-f]{16})')
    Require91 $match.Success "Missing Phase 91 semantic counter: $name"
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

$managedBuildParameters = @{ OutputDirectory = $managedDirectory }
if ($EnablePhase91Diagnostics) {
    $managedBuildParameters.EnablePhase91Diagnostics = $true
}
$managedOutput = @(& (Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1') `
    @managedBuildParameters)
$managedOutput | Set-Content -LiteralPath `
    (Join-Path $build 'managed-kernel-build-output.log') -Encoding ascii
Require91 (Test-Path -LiteralPath $payload) 'ManagedKernel NativeAOT payload is missing.'
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

$normalOutput = @(& (Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1') `
    -OutputDirectory $normalGate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase11 `
    -EnableNativeAotStartup -AssumeUnspecifiedTimezoneUtc)
$normalOutput | Set-Content -LiteralPath `
    (Join-Path $build 'gate4-normal-build-output.log') -Encoding ascii
$phase91BuildParameters = @{
    OutputDirectory = $phase91Gate
    ManagedArtifact = $payload
    PayloadMode = 'ManagedKernel'
    Scenario = 'ManagedKernelPhase11'
    EnableNativeAotStartup = $true
    EnablePhase91AsyncTxLifecycle = $true
    AssumeUnspecifiedTimezoneUtc = $true
}
if ($EnablePhase91Diagnostics) {
    $phase91BuildParameters.EnablePhase91Diagnostics = $true
}
$phase91Output = @(& (Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1') `
    @phase91BuildParameters)
$phase91Output | Set-Content -LiteralPath `
    (Join-Path $build 'gate4-phase91-build-output.log') -Encoding ascii

$runnerOutput = @(& (Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1') `
    -GateDirectory $phase91Gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize $payloadSize `
    -RunCount 3 -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
    -EnablePhase80E1000 -EnablePhase91AsyncTxLifecycle)
$runnerOutput | Set-Content -LiteralPath `
    (Join-Path $build 'qemu-runner-output.log') -Encoding ascii

$summaries = @()
for ($boot = 1; $boot -le 3; ++$boot) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $boot)
    $serial = [IO.File]::ReadAllText((Join-Path $run 'serial.log'))
    $capture = Join-Path $run 'phase90-tx-capture.bin'
    $captureLog = Join-Path $run 'phase90-tx-capture.log'
    $injections = Join-Path $run 'injections.log'
    $timeline = Join-Path $run 'timeline.log'
    foreach ($marker in @(
        'GXOS_NET10:PHASE91_STOP_ADMISSION_CLOSED=1',
        'GXOS_NET10:PHASE91_STOP_LATE_TXDW_IGNORED=1',
        'GXOS_NET10:PHASE91_STOP_LEDGER_INVALIDATED=1',
        'GXOS_NET10:PHASE91_OLD_COMPLETED_LEDGER_INVALIDATED=1',
        'GXOS_NET10:PHASE91_SAME_SLOT_NEW_ID_NO_ALIAS=1',
        'GXOS_NET10:PHASE91_RESTART_OLD_IDS_REJECTED_WHILE_ACTIVE=1',
        'GXOS_NET10:PHASE91_RESTART_STALE_IDS_NO_MUTATION=1',
        'GXOS_NET10:PHASE91_RESTART_ASYNC_CAPACITY_RESTORED=1',
        'GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_COMPLETE=1',
        'GXOS_NET10:PHASE91_RESTART_SYNC_TX_DD_POLL_PASS=1',
        'GXOS_NET10:PHASE91_RESTART_TXDW_ROUTE_READY=1',
        'GXOS_NET10:PHASE91_TXDW_MASK_RESTORED=1',
        'GXOS_NET10:PHASE91_NO_INTERRUPT_STORM=1',
        'GXOS_NET10:PHASE91_COM1_ROUTE_ACTIVE=1',
        'GXOS_NET10:PHASE91_RESTART_TX_RX_LIVENESS=PASS',
        'GXOS_NET10:PHASE91_POST_RESTART_RX_PASS=1',
        'GXOS_NET10:PHASE91_NATIVE_COUNTERS_RECONCILED=1',
        'PHASE91_ASYNC_TX_RESET_CONTAINMENT_PASS=1',
        'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')) {
        Require91 $serial.Contains($marker) "Boot $boot missing required marker: $marker"
    }
    $preStopAsyncAccepted = ReadCounter91 $serial 'PHASE91_ACCEPTED_SUBMISSIONS'
    $preStopAsyncCompleted = ReadCounter91 $serial 'PHASE91_DD_COMPLETIONS_BEFORE_STOP'
    $preStopAsyncTransmitted = ReadCounter91 $serial 'PHASE91_ASYNC_TX_SUCCESSFUL_FRAMES_BEFORE_STOP'
    $stopOutstanding = ReadCounter91 $serial 'PHASE91_OUTSTANDING_UNCONSUMED_AT_STOP'
    $stopCompletedUnconsumed = ReadCounter91 $serial 'PHASE91_COMPLETED_UNCONSUMED_AT_STOP'
    $stopHardwareOwned = ReadCounter91 $serial 'PHASE91_HARDWARE_OWNED_NOT_DD_AT_STOP'
    $stopExposed = ReadCounter91 $serial 'PHASE91_COMPLETIONS_EXPOSED_BEFORE_STOP'
    $stopConsumed = ReadCounter91 $serial 'PHASE91_COMPLETIONS_CONSUMED_BEFORE_STOP'
    $stopInvalidated = ReadCounter91 $serial 'PHASE91_INVALIDATED_BY_STOP'
    $phase90SyncFrames = ReadCounter91 $serial 'PHASE90_SYNC_TX_DD_POLL_FRAMES'
    $restartAsyncAccepted = ReadCounter91 $serial 'PHASE91_RESTART_ACCEPTED_SUBMISSIONS'
    $restartAsyncCompleted = ReadCounter91 $serial 'PHASE91_RESTART_DD_COMPLETIONS'
    $restartAsyncTransmitted = ReadCounter91 $serial 'PHASE91_RESTART_ASYNC_TX_SUCCESSFUL_FRAMES'
    $restartExposed = ReadCounter91 $serial 'PHASE91_RESTART_COMPLETIONS_EXPOSED'
    $restartConsumed = ReadCounter91 $serial 'PHASE91_RESTART_COMPLETIONS_CONSUMED'
    $restartSyncFrames = ReadCounter91 $serial 'PHASE91_RESTART_SYNC_TX_DD_POLL_FRAMES'
    $restartSyncTxdwNotifications = ReadCounter91 $serial 'PHASE91_RESTART_SYNC_FRESH_TXDW_NOTIFICATIONS'
    $nativeInterruptEntries = ReadCounter91 $serial 'PHASE91_NATIVE_INTERRUPT_ENTRIES'
    Require91 ($preStopAsyncAccepted -eq $preStopAsyncCompleted -and
        $preStopAsyncCompleted -eq $preStopAsyncTransmitted) `
        "Boot $boot has inconsistent pre-stop async accounting."
    Require91 ($stopOutstanding -eq $stopCompletedUnconsumed + $stopHardwareOwned -and
        $stopInvalidated -eq $stopOutstanding -and
        $stopCompletedUnconsumed -eq $stopOutstanding -and $stopHardwareOwned -eq 0 -and
        $stopExposed -eq $preStopAsyncCompleted -and
        $stopConsumed + $stopOutstanding -eq $stopExposed) `
        "Boot $boot has inconsistent stop-boundary ownership accounting."
    Require91 ($restartAsyncAccepted -eq $restartAsyncCompleted -and
        $restartAsyncCompleted -eq $restartAsyncTransmitted -and
        $restartExposed -eq $restartAsyncCompleted -and
        $restartConsumed -eq $restartExposed) `
        "Boot $boot has inconsistent restart async accounting."
    Require91 ($restartSyncTxdwNotifications -gt 0) `
        "Boot $boot has no fresh post-submission TXDW notification proof."
    Require91 ($nativeInterruptEntries -gt 0 -and $nativeInterruptEntries -le 32) `
        "Boot $boot native interrupt entry count $nativeInterruptEntries exceeded the bounded no-storm envelope."
    $expectedCaptureCount = [int]($preStopAsyncTransmitted + $phase90SyncFrames +
        $restartAsyncTransmitted + $restartSyncFrames)
    $capturedBytes = (Get-Item -LiteralPath $capture).Length
    Require91 ($expectedCaptureCount -eq 28) `
        "Boot $boot request-state model expected $expectedCaptureCount TX captures; acceptance requires 28."
    Require91 (($capturedBytes % 64) -eq 0 -and
        ($capturedBytes / 64) -eq $expectedCaptureCount -and
        ($capturedBytes / 64) -eq 28) `
        "Boot $boot expected $expectedCaptureCount captured frames from the request-state model; capture contains $([int]($capturedBytes / 64))."
    $summaries += [pscustomobject]@{
        Boot = $boot
        ExpectedFrames = $expectedCaptureCount
        CapturedFrames = [int]($capturedBytes / 64)
        StopInvalidated = $stopInvalidated
        SerialSha256 = (Get-FileHash -LiteralPath (Join-Path $run 'serial.log') -Algorithm SHA256).Hash.ToUpperInvariant()
        CaptureSha256 = (Get-FileHash -LiteralPath $capture -Algorithm SHA256).Hash.ToUpperInvariant()
        CaptureLogSha256 = (Get-FileHash -LiteralPath $captureLog -Algorithm SHA256).Hash.ToUpperInvariant()
        InjectionSha256 = (Get-FileHash -LiteralPath $injections -Algorithm SHA256).Hash.ToUpperInvariant()
        TimelineSha256 = (Get-FileHash -LiteralPath $timeline -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

"PHASE91_PAYLOAD_SIZE=$payloadSize"
"PHASE91_PAYLOAD_SHA256=$payloadHash"
$summaries | Format-Table -AutoSize
'MANAGED_KERNEL_PHASE91_ACCEPTANCE=PASS boots=3'

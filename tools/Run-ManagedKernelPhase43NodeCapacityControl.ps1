[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 1) { throw 'At least one fresh Phase 43 capacity-control boot is required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase43-node-capacity-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
$compile = Join-Path $output 'managed-build'
$gate = Join-Path $output 'gate4'
$evidence = Join-Path $output 'evidence'
$buildManaged = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$buildGate = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$runFreshBoots = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$payload = Join-Path $compile 'publish\gxos-managed-kernel.dll'
New-Item -ItemType Directory -Force -Path $output | Out-Null

& $buildManaged -OutputDirectory $compile
if ($LASTEXITCODE -ne 0) { throw "ManagedKernel NativeAOT build failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $payload)) { throw "ManagedKernel payload was not emitted: $payload" }
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase43 `
    -EnableNativeAotStartup `
    -EnableManagedKernelPhase43Capacity -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 43 capacity-control build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase43Protocol -EnablePhase43CapacityControl `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 43 capacity-control fresh boots failed: $LASTEXITCODE" }

$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE43_CAPACITY_MODE_SELECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_CAPACITY_NEGATIVE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_CAPACITY_CONTROL_VALIDATED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE43_START_FAILED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START',
    'GXOS_NET10:FAIL:managed-kernel-phase14-driver-proof')
$reports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) {
        if (-not $text.Contains($marker)) { throw "Phase 43 capacity boot missing '$marker': $($serial.FullName)" }
    }
    if ($text.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_PASS') -or
        $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE43_PASS') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 43 capacity boot emitted an invalid success or machine-fault marker: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($reports.Count -ne $RunCount) { throw "Expected $RunCount capacity-control serial logs, found $($reports.Count)." }
$summary = @(
    'MANAGED_KERNEL_PHASE43_CAPACITY_CONTROL_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE43_CAPACITY_CONTROL_RUNS=$RunCount",
    'MANAGED_KERNEL_PHASE43_CAPACITY_CONTROL=NodeCapacityExceeded',
    "MANAGED_KERNEL_PHASE43_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE43_PAYLOAD_SIZE=$payloadSize",
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase43-capacity-summary.log') -Value $summary -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE43_CAPACITY_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE43_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE43_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE43_CAPACITY_RUNS=$RunCount"

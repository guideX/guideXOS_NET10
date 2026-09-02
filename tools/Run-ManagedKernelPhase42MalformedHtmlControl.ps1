[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 42 malformed-control boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase42-malformed-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "Output directory already exists: $output"
}

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
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase42 `
    -EnableNativeAotStartup -EnableManagedKernelPhase42 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 42 malformed-control build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase42Protocol -EnablePhase42MalformedControl `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 42 malformed-control fresh boots failed: $LASTEXITCODE" }

foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in @(
        'GXOS_NET10:MANAGED_HTTPS_PHASE42_TOKENIZER_FAILURE=0x0000000000000005',
        'GXOS_NET10:MANAGED_KERNEL_PHASE42_START_FAILED',
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START',
        'GXOS_NET10:FAIL:managed-kernel-phase14-driver-proof')) {
        if (-not $text.Contains($marker)) {
            throw "Malformed Phase 42 boot missing marker '$marker': $($serial.FullName)"
        }
    }
}

Set-Content -LiteralPath (Join-Path $output 'phase42-malformed-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE42_MALFORMED_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE42_MALFORMED_RUNS=$RunCount",
    'MANAGED_KERNEL_PHASE42_MALFORMED_CASE=ATTRIBUTE_VALUE_TOO_LONG',
    'MANAGED_KERNEL_PHASE42_MALFORMED_FAILURE=AttributeValueTooLong',
    "MANAGED_KERNEL_PHASE42_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE42_PAYLOAD_SIZE=$payloadSize") -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE42_MALFORMED_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE42_MALFORMED_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE42_MALFORMED_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE42_MALFORMED_RUNS=$RunCount"

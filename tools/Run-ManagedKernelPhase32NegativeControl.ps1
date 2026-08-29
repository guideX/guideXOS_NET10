[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 1) { throw 'At least one fresh Phase 32 negative-control boot is required.' }
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase32Protocol -EnablePhase32NegativeControl
if ($LASTEXITCODE -ne 0) {
    throw "Phase 32 negative-control boots failed: $LASTEXITCODE"
}

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    if (-not $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START') -or
        -not $text.Contains('GXOS_NET10:FAIL:managed-kernel-phase14-driver-proof')) {
        throw "Phase 32 negative control did not reject the corrupted Finished record: $($serial.FullName)"
    }
    if ($text.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_REQUEST_ENCRYPTED_SENT') -or
        $text.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE32_PASS') -or
        $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE32_PASS') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 32 negative control emitted success or an unexpected machine fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 32 negative-control serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase32-negative-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE32_NEGATIVE_CONTROL=PASS',
    "MANAGED_KERNEL_PHASE32_NEGATIVE_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE32_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE32_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE32_NEGATIVE_FAULT=CORRUPTED_SERVER_FINISHED_AEAD_TAG',
    'MANAGED_KERNEL_PHASE32_NEGATIVE_EXPECTED=DRIVER_START_REJECTED_NO_HTTP_SUCCESS',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase32-negative-summary.log')

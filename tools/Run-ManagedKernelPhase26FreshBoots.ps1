[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE26_PASS' `
    -EnablePhase15Rx -EnablePhase23Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 26 fresh boots failed: $LASTEXITCODE"
}

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_VIRTIO_RNG_PCI_DISCOVERED',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_TRANSPORT=MODERN_NON_TRANSITIONAL',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_QUEUE_CONFIGURED',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_PROVIDER_AVAILABLE',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_PROVIDER=VIRTIO_RNG',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_GC_SURVIVAL_PASS',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_QUEUE_RELEASED',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_REINITIALIZE_REUSE_PASS',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_TEARDOWN_PASS',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_REPORTS_SUCCESS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE26_PASS')

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            throw "Phase 26 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    if ($text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 26 boot reported a fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 26 serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase26-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE26_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE26_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE26_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE26_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE26_TRANSPORT=VIRTIO_RNG_MODERN_NON_TRANSITIONAL',
    'MANAGED_KERNEL_PHASE26_NEGATIVE_CONTROL=HOST_TESTS_PROVIDER_UNAVAILABLE_FAIL_CLOSED',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase26-summary.log')

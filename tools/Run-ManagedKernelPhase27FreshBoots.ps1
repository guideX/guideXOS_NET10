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
if ($RunCount -lt 3) { throw 'Three fresh Phase 27 boots are required.' }
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
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE27_PASS' `
    -EnablePhase15Rx -EnablePhase23Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 27 fresh boots failed: $LASTEXITCODE"
}

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_VIRTIO_RNG_PCI_DISCOVERED',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_TRANSPORT=MODERN_NON_TRANSITIONAL',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_QUEUE_CONFIGURED',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_PROVIDER_AVAILABLE',
    'GXOS_NET10:MANAGED_AES128_KAT_PASS',
    'GXOS_NET10:MANAGED_AES128_GC_SURVIVAL_PASS',
    'GXOS_NET10:MANAGED_GHASH_KAT_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_ENCRYPT_KAT_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_DECRYPT_KAT_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_INVALID_TAG_FAIL_CLOSED_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_NO_PLAINTEXT_ON_FAILURE_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_GC_SURVIVAL_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_RESET_REUSE_PASS',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_PROVIDER=VIRTIO_RNG',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE27_PASS')

$runReports = @()
$serialLogs = Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse
foreach ($serial in @($serialLogs)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            if ($marker -eq 'GXOS_NET10:MANAGED_AES128_GC_SURVIVAL_PASS' -or
                $marker -eq 'GXOS_NET10:MANAGED_AES_GCM_GC_SURVIVAL_PASS') {
                throw "Phase 27 Outcome B: direct crypto-state GC marker '$marker' is blocked by the current NativeAOT runtime: $($serial.FullName)"
            }
            throw "Phase 27 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE27_PASS')).Count -ne 1) {
        throw "Phase 27 boot did not report exactly one pass marker: $($serial.FullName)"
    }
    if ($text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 27 boot reported a fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 27 serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase27-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE27_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE27_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE27_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE27_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE27_PROFILE=AES128_GCM_96BIT_NONCE_128BIT_TAG',
    'MANAGED_KERNEL_PHASE27_NEGATIVE_CONTROL=INVALID_TAG_NO_PLAINTEXT',
    'MANAGED_KERNEL_PHASE27_ENTROPY_REGRESSION=VIRTIO_RNG',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase27-summary.log')

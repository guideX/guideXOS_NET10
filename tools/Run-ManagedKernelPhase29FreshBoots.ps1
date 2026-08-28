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
if ($RunCount -lt 3) { throw 'Three fresh Phase 29 boots are required.' }
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE29_PASS' `
    -EnablePhase15Rx -EnablePhase23Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 29 fresh boots failed: $LASTEXITCODE"
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
    'GXOS_NET10:MANAGED_KERNEL_PHASE26_PASS',
    'GXOS_NET10:MANAGED_P256_FIELD_SELF_TEST_PASS',
    'GXOS_NET10:MANAGED_P256_PRIVATE_PUBLIC_KAT_PASS',
    'GXOS_NET10:MANAGED_P256_PUBLIC_KEY_VALIDATION_PASS',
    'GXOS_NET10:MANAGED_P256_ECDH_KAT_PASS',
    'GXOS_NET10:MANAGED_P256_INVALID_PRIVATE_REJECTION_PASS',
    'GXOS_NET10:MANAGED_P256_INVALID_PUBLIC_REJECTION_PASS',
    'GXOS_NET10:MANAGED_P256_OUTPUT_UNCHANGED_ON_FAILURE_PASS',
    'GXOS_NET10:MANAGED_P256_ENTROPY_PROVIDER=VIRTIO_RNG',
    'GXOS_NET10:MANAGED_P256_ENTROPY_KEY_GENERATION_PASS',
    'GXOS_NET10:MANAGED_P256_GENERATED_PUBLIC_VALIDATION_PASS',
    'GXOS_NET10:MANAGED_P256_GENERATED_ECDH_PASS',
    'GXOS_NET10:MANAGED_P256_SCALAR_N_SELF_TEST_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_PUBLIC_KEY_VALIDATION_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_RAW_KAT_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_MODIFIED_DIGEST_REJECTION_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_MODIFIED_SIGNATURE_REJECTION_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_WRONG_PUBLIC_KEY_REJECTION_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_DER_KAT_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_MALFORMED_DER_REJECTION_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_SCALAR_REJECTION_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_POST_FAILURE_RECOVERY_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE27_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE28_PASS',
    'GXOS_NET10:MANAGED_P256_ECDSA_PHASE28_REGRESSION_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE29_PASS',
    'GXOS_NET10:MANAGED_AES128_KAT_PASS',
    'GXOS_NET10:MANAGED_GHASH_KAT_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_ENCRYPT_KAT_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_DECRYPT_KAT_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_INVALID_TAG_FAIL_CLOSED_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_NO_PLAINTEXT_ON_FAILURE_PASS',
    'GXOS_NET10:MANAGED_AES_GCM_RESET_REUSE_PASS',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_PROVIDER=VIRTIO_RNG',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_PASS')

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            throw "Phase 29 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE29_PASS')).Count -ne 1) {
        throw "Phase 29 boot did not report exactly one pass marker: $($serial.FullName)"
    }
    if ($text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 29 boot reported a fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 29 serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase29-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE29_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE29_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE29_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE29_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE29_PROFILE=P256_ECDSA_VERIFY_DER_SEC1_UNCOMPRESSED',
    'MANAGED_KERNEL_PHASE29_ENTROPY=VIRTIO_RNG_REJECTION_SAMPLING',
    'MANAGED_KERNEL_PHASE29_INHERITED_GC_LIMITATION=PHASE27_DIRECT_CRYPTO_GC_NOT_REQUIRED',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase29-summary.log')

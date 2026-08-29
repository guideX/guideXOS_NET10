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
if ($RunCount -lt 3) { throw 'Three fresh Phase 31 boots are required.' }
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE31_PASS' `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 31 fresh boots failed: $LASTEXITCODE"
}

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_VIRTIO_RNG_PCI_DISCOVERED',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_TRANSPORT=MODERN_NON_TRANSITIONAL',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_QUEUE_CONFIGURED',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_PROVIDER_AVAILABLE',
    'GXOS_NET10:MANAGED_SECURE_RANDOM_PROVIDER=VIRTIO_RNG',
    'GXOS_NET10:MANAGED_VIRTIO_RNG_QUEUE_RELEASED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE26_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE27_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE28_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE29_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE30_PASS',
    'GXOS_NET10:MANAGED_X509_CERTIFICATE_PARSE_PASS',
    'GXOS_NET10:MANAGED_X509_CHAIN_B_PASS',
    'GXOS_NET10:MANAGED_X509_HOSTNAME_RULES_PASS',
    'GXOS_NET10:MANAGED_TLS12_PRODUCTION_ENTROPY_INIT_PASS',
    'GXOS_NET10:MANAGED_TLS12_PRODUCTION_EPHEMERAL_INIT_PASS',
    'GXOS_NET10:MANAGED_TLS12_RECORD_PARSER_PASS',
    'GXOS_NET10:MANAGED_TLS12_CLIENTHELLO_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_SERVERHELLO_PASS',
    'GXOS_NET10:MANAGED_TLS12_CERTIFICATE_CHAIN_PASS',
    'GXOS_NET10:MANAGED_TLS12_HOSTNAME_PASS',
    'GXOS_NET10:MANAGED_TLS12_SERVER_KEY_EXCHANGE_PASS',
    'GXOS_NET10:MANAGED_TLS12_ECDH_PREMASTER_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_EMS_NEGOTIATION_PASS',
    'GXOS_NET10:MANAGED_TLS12_EMS_SESSION_HASH_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_MASTER_SECRET_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_TRAFFIC_KEY_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_CLIENT_ECDH_PUBLIC_PASS',
    'GXOS_NET10:MANAGED_TLS12_CLIENT_FINISHED_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_CLIENT_FINISHED_GCM_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_SERVER_FINISHED_DECRYPT_PASS',
    'GXOS_NET10:MANAGED_TLS12_SERVER_FINISHED_KAT_PASS',
    'GXOS_NET10:MANAGED_TLS12_ESTABLISHED_PASS',
    'GXOS_NET10:MANAGED_TLS12_APPLICATION_DATA_PASS',
    'GXOS_NET10:MANAGED_TLS12_MALFORMED_FINISHED_REJECTION_PASS',
    'GXOS_NET10:MANAGED_TLS12_MISSING_EMS_REJECTION_PASS',
    'GXOS_NET10:MANAGED_TLS12_FAILURE_RECOVERY_PASS')

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            throw "Phase 31 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE31_PASS')).Count -ne 1) {
        throw "Phase 31 boot did not report exactly one pass marker: $($serial.FullName)"
    }
    if ($text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 31 boot reported a fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 31 serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase31-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE31_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE31_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE31_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE31_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE31_PROFILE=TLS12_ECDHE_ECDSA_AES_128_GCM_SHA256_P256_EMS',
    'MANAGED_KERNEL_PHASE31_CERTIFICATE_MODEL=LEAF_INTERMEDIATE_TRUSTED_ROOT_OMITTED_ON_WIRE',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase31-summary.log')

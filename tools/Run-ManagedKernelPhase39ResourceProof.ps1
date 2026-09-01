[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 39 boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase39-resource-$stamp"
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
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel NativeAOT build failed: $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $payload)) {
    throw "ManagedKernel payload was not emitted: $payload"
}
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase39 `
    -EnableNativeAotStartup -EnableManagedKernelPhase39 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) {
    throw "Gate 4 Phase 39 build failed: $LASTEXITCODE"
}

$metadata = @(
    'MANAGED_KERNEL_PHASE39_RUN=DETERMINISTIC_HTTPS_RESOURCE',
    'MANAGED_KERNEL_PHASE39_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE39_TARGET_PATH=/phase39/resource',
    'MANAGED_KERNEL_PHASE39_BACKEND=QEMU_DGRAM_DETERMINISTIC_FIXTURE',
    'MANAGED_KERNEL_PHASE39_DEVICE=e1000e,addr=2',
    'MANAGED_KERNEL_PHASE39_RESOURCE_LENGTH=16884',
    'MANAGED_KERNEL_PHASE39_RESOURCE_PATTERN=(index*31+7)&255',
    'MANAGED_KERNEL_PHASE39_CONTENT_TYPE=application/octet-stream',
    'MANAGED_KERNEL_PHASE39_SHA256=0284CD23ED354023F0363678794905B285C104A2056189B36C23C0689924454F',
    "MANAGED_KERNEL_PHASE39_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE39_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE39_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE39_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE39_GATE=$gate",
    "MANAGED_KERNEL_PHASE39_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase39-run-metadata.log') `
    -Value $metadata -Encoding ascii

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase39Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 39 fresh boots failed: $LASTEXITCODE"
}

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE39_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE39_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_BEGIN',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_CONFIGURED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_DNS_SUCCESS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_TCP_CONNECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_TLS_HANDSHAKE_AUTHENTICATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_HTTP_REQUEST_ENCRYPTED_SENT',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_HTTP_STATUS_PARSED=0x',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PAUSED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PAUSED_POLLS=0x0000000000000004',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_RESUMED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE39_PASS')

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') `
                                      -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            throw "Phase 39 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    foreach ($field in @(
        'MANAGED_HTTPS_PHASE39_TOTAL_KNOWN=0x0000000000000001',
        'MANAGED_HTTPS_PHASE39_TOTAL_LENGTH=0x00000000000041F4',
        'MANAGED_HTTPS_PHASE39_RECEIVED_BYTES=0x00000000000041F4',
        'MANAGED_HTTPS_PHASE39_PROCESSED_BYTES=0x00000000000041F4',
        'MANAGED_HTTPS_PHASE39_PREFIX_BYTES=0x0000000000000020',
        'MANAGED_HTTPS_PHASE39_TRANSFER_MODE=0x0000000000000002',
        'MANAGED_HTTPS_PHASE39_CONTENT_TYPE_LENGTH=0x0000000000000018')) {
        if (-not $text.Contains($field)) {
            throw "Phase 39 boot missing proof field '$field': $($serial.FullName)"
        }
    }
    $segmentMatch = [regex]::Match(
        $text, 'MANAGED_HTTPS_PHASE39_SEGMENTS=0x([0-9A-Fa-f]+)')
    if (-not $segmentMatch.Success -or
        [Convert]::ToInt32($segmentMatch.Groups[1].Value, 16) -lt 17) {
        throw "Phase 39 boot did not prove multi-window delivery: $($serial.FullName)"
    }
    $peakMatch = [regex]::Match(
        $text, 'MANAGED_HTTPS_PHASE39_PEAK_BUFFER=0x([0-9A-Fa-f]+)')
    if (-not $peakMatch.Success -or
        [Convert]::ToInt32($peakMatch.Groups[1].Value, 16) -gt 1024) {
        throw "Phase 39 boot exceeded the 1024-byte delivery window: $($serial.FullName)"
    }
    $sha = '0284CD23ED354023F0363678794905B285C104A2056189B36C23C0689924454F'
    $words = @('0284CD23','ED354023','F0363678','794905B2','85C104A2','056189B3','6C23C068','9924454F')
    foreach ($word in $words) {
        $paddedWord = $word.PadLeft(16, '0')
        if (-not $text.Contains("MANAGED_HTTPS_PHASE39_SHA256_WORD=0x$paddedWord")) {
            throw "Phase 39 boot missing SHA-256 word ${word}: $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'MANAGED_HTTPS_PHASE39_RESOURCE_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'MANAGED_KERNEL_PHASE39_PASS')).Count -ne 1 -or
        $text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 39 boot did not finish cleanly: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 39 serial logs, found $($runReports.Count)."
}

$summary = @(
    'MANAGED_KERNEL_PHASE39_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE39_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE39_RESOURCE_LENGTH=16884",
    "MANAGED_KERNEL_PHASE39_RESOURCE_SHA256=0284CD23ED354023F0363678794905B285C104A2056189B36C23C0689924454F",
    "MANAGED_KERNEL_PHASE39_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE39_PAYLOAD_SIZE=$payloadSize",
    'MANAGED_KERNEL_PHASE39_CONTENT_TYPE=application/octet-stream',
    'MANAGED_KERNEL_PHASE39_TRANSFER=CONTENT_LENGTH',
    'MANAGED_KERNEL_PHASE39_PREFIX_LENGTH=32',
    'MANAGED_KERNEL_PHASE39_PAUSE_STABLE_POLLS=4',
    'MANAGED_KERNEL_PHASE39_DELIVERY_WINDOW=1024',
    'MANAGED_KERNEL_PHASE39_PARSER_STORAGE=3520',
    'MANAGED_KERNEL_PHASE39_HTTP_STAGING_APPROX=5312',
    'MANAGED_KERNEL_PHASE39_HTTPS_STAGING_APPROX=9408_EXCLUDING_LOWER_TLS_NETWORK',
    $runReports)
Set-Content -LiteralPath (Join-Path $output 'phase39-summary.log') `
    -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE39_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE39_RESOURCE_SHA256=0284CD23ED354023F0363678794905B285C104A2056189B36C23C0689924454F"
Write-Output "MANAGED_KERNEL_PHASE39_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE39_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE39_RUNS=$RunCount"

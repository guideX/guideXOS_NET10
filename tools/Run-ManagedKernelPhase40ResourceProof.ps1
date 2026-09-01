[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 40 boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase40-resource-$stamp"
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
if (-not (Test-Path -LiteralPath $payload)) { throw "ManagedKernel payload was not emitted: $payload" }
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase40 `
    -EnableNativeAotStartup -EnableManagedKernelPhase40 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 40 build failed: $LASTEXITCODE" }

$metadata = @(
    'MANAGED_KERNEL_PHASE40_RUN=DETERMINISTIC_HTTPS_GZIP_RESOURCE',
    'MANAGED_KERNEL_PHASE40_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE40_TARGET_PATH=/phase40/gzip',
    'MANAGED_KERNEL_PHASE40_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_FIXTURE',
    'MANAGED_KERNEL_PHASE40_DEVICE=e1000e,addr=2',
    'MANAGED_KERNEL_PHASE40_DECODED_RESOURCE_LENGTH=16384',
    'MANAGED_KERNEL_PHASE40_RESOURCE_PATTERN=(index*31+7)&255',
    'MANAGED_KERNEL_PHASE40_CONTENT_TYPE=application/octet-stream',
    'MANAGED_KERNEL_PHASE40_CONTENT_ENCODING=gzip',
    'MANAGED_KERNEL_PHASE40_SHA256=9038AC64E659335CCBFDD3F684F35A26A2C9E580D9AF6B4807AF3ADBE2C257E3',
    "MANAGED_KERNEL_PHASE40_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE40_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE40_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE40_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE40_GATE=$gate",
    "MANAGED_KERNEL_PHASE40_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase40-run-metadata.log') -Value $metadata -Encoding ascii

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase40Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 40 fresh boots failed: $LASTEXITCODE" }

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE40_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE40_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_BEGIN',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_CONFIGURED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_RESOURCE_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_RESOURCE_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_REQUEST_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_DNS_SUCCESS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_TCP_CONNECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_TLS_HANDSHAKE_AUTHENTICATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_HTTP_REQUEST_ENCRYPTED_SENT',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_HTTP_STATUS_PARSED=0x',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_RESOURCE_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_RESOURCE_PAUSED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_RESOURCE_RESUMED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_CRC_VALIDATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_ISIZE_VALIDATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE40_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE40_PASS')

$reports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) { throw "Phase 40 boot missing marker '$marker': $($serial.FullName)" }
    }
    foreach ($field in @(
        'MANAGED_HTTPS_PHASE40_STATUS=0x00000000000000C8',
        'MANAGED_HTTPS_PHASE40_TRANSFER_MODE=0x0000000000000002',
        'MANAGED_HTTPS_PHASE40_TOTAL_KNOWN=0x0000000000000001',
        'MANAGED_HTTPS_PHASE40_DECODED_BYTES_PRODUCED=0x0000000000004000',
        'MANAGED_HTTPS_PHASE40_DECODED_BYTES_CONSUMED=0x0000000000004000',
        'MANAGED_HTTPS_PHASE40_PAUSE_COUNT=0x0000000000000001',
        'MANAGED_HTTPS_PHASE40_RESUME_COUNT=0x0000000000000001',
        'MANAGED_HTTPS_PHASE40_DECODER_PAUSE_COUNT=0x0000000000000000',
        'MANAGED_HTTPS_PHASE40_DECODER_RESUME_COUNT=0x0000000000000000',
        'MANAGED_HTTPS_PHASE40_HISTORY_WINDOW=0x0000000000008000',
        'MANAGED_HTTPS_PHASE40_PREFIX_BYTES=0x0000000000000020')) {
        if (-not $text.Contains($field)) { throw "Phase 40 boot missing proof field '$field': $($serial.FullName)" }
    }
    $encoded = [regex]::Match($text, 'MANAGED_HTTPS_PHASE40_ENCODED_CONTENT_LENGTH=0x([0-9A-Fa-f]+)')
    if (-not $encoded.Success -or [Convert]::ToInt32($encoded.Groups[1].Value, 16) -ge 16384) {
        throw "Phase 40 boot did not prove encoded compression: $($serial.FullName)"
    }
    $peak = [regex]::Match($text, 'MANAGED_HTTPS_PHASE40_PEAK_DECODED_BUFFER=0x([0-9A-Fa-f]+)')
    if (-not $peak.Success -or [Convert]::ToInt32($peak.Groups[1].Value, 16) -gt 1024) {
        throw "Phase 40 boot exceeded the decoded output window: $($serial.FullName)"
    }
    $words = @('9038AC64','E659335C','CBFDD3F6','84F35A26','A2C9E580','D9AF6B48','07AF3ADB','E2C257E3')
    foreach ($word in $words) {
        if (-not $text.Contains("MANAGED_HTTPS_PHASE40_SHA256_WORD=0x$($word.PadLeft(16, '0'))")) {
            throw "Phase 40 boot missing SHA-256 word ${word}: $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'MANAGED_HTTPS_PHASE40_RESOURCE_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'MANAGED_KERNEL_PHASE40_PASS')).Count -ne 1 -or
        $text.Contains('GXOS_NET10:FAIL:') -or $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 40 boot did not finish cleanly: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($reports.Count -ne $RunCount) { throw "Expected $RunCount Phase 40 serial logs, found $($reports.Count)." }

$summary = @(
    'MANAGED_KERNEL_PHASE40_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE40_RUNS=$RunCount",
    'MANAGED_KERNEL_PHASE40_DECODED_RESOURCE_LENGTH=16384',
    'MANAGED_KERNEL_PHASE40_RESOURCE_SHA256=9038AC64E659335CCBFDD3F684F35A26A2C9E580D9AF6B4807AF3ADBE2C257E3',
    "MANAGED_KERNEL_PHASE40_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE40_PAYLOAD_SIZE=$payloadSize",
    'MANAGED_KERNEL_PHASE40_CONTENT_ENCODING=gzip',
    'MANAGED_KERNEL_PHASE40_TRANSFER=CONTENT_LENGTH',
    'MANAGED_KERNEL_PHASE40_PREFIX_LENGTH=32',
    'MANAGED_KERNEL_PHASE40_PAUSE_STABLE_POLLS=4',
    'MANAGED_KERNEL_PHASE40_DELIVERY_WINDOW=1024',
    'MANAGED_KERNEL_PHASE40_DECODER_HISTORY_WINDOW=32768',
    'MANAGED_KERNEL_PHASE40_DECODED_RESOURCE_LIMIT=4194304',
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase40-summary.log') -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE40_OUTPUT=$output"
Write-Output 'MANAGED_KERNEL_PHASE40_RESOURCE_SHA256=9038AC64E659335CCBFDD3F684F35A26A2C9E580D9AF6B4807AF3ADBE2C257E3'
Write-Output "MANAGED_KERNEL_PHASE40_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE40_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE40_RUNS=$RunCount"

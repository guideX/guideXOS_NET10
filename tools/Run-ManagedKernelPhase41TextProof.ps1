[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 41 boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase41-text-$stamp"
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

$resourceSha256 = '6D91A155D767AC7C0C1E2C5B49479CF1D7FDE8DF7C4F459A9BCECE43EF11DF79'
$resourceLength = 10496
$metadata = @(
    'MANAGED_KERNEL_PHASE41_RUN=BOUNDED_HTTPS_GZIP_TEXT_RESOURCE',
    'MANAGED_KERNEL_PHASE41_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE41_TARGET_PATH=/phase41/gzip',
    'MANAGED_KERNEL_PHASE41_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_UTF8_FIXTURE',
    'MANAGED_KERNEL_PHASE41_DEVICE=e1000e,addr=2',
    "MANAGED_KERNEL_PHASE41_DECODED_RESOURCE_LENGTH=$resourceLength",
    'MANAGED_KERNEL_PHASE41_RESOURCE_PATTERN=256x(GuideXOS 41 CRLF Ré sum λη Ж 中 ★ 🙂 LF)',
    'MANAGED_KERNEL_PHASE41_CONTENT_TYPE=text/plain; charset=utf-8',
    'MANAGED_KERNEL_PHASE41_CONTENT_ENCODING=gzip',
    "MANAGED_KERNEL_PHASE41_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE41_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE41_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE41_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE41_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE41_GATE=$gate",
    "MANAGED_KERNEL_PHASE41_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase41-run-metadata.log') -Value $metadata -Encoding ascii

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase41 `
    -EnableNativeAotStartup -EnableManagedKernelPhase41 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 41 build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase41Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 41 fresh boots failed: $LASTEXITCODE" }

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE41_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE41_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_BEGIN',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_CONFIGURED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_REQUEST_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE41_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE41_PASS')

$requiredFields = @(
    'MANAGED_HTTPS_PHASE41_STATUS=0x00000000000000C8',
    'MANAGED_HTTPS_PHASE41_MIME=0x0000000000000001',
    'MANAGED_HTTPS_PHASE41_CHARSET=0x0000000000000001',
    'MANAGED_HTTPS_PHASE41_CHARSET_SOURCE=0x0000000000000001',
    'MANAGED_HTTPS_PHASE41_CONTENT_TYPE_LENGTH=0x0000000000000019',
    'MANAGED_HTTPS_PHASE41_DECOMPRESSED_BYTES=0x0000000000002900',
    'MANAGED_HTTPS_PHASE41_TEXT_INPUT_BYTES=0x0000000000002900',
    'MANAGED_HTTPS_PHASE41_SCALARS_PRODUCED=0x0000000000001E00',
    'MANAGED_HTTPS_PHASE41_SCALARS_DELIVERED=0x0000000000001E00',
    'MANAGED_HTTPS_PHASE41_PAUSE_COUNT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE41_RESUME_COUNT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE41_STABLE_PAUSED_POLLS=0x0000000000000004',
    'MANAGED_HTTPS_PHASE41_PREFIX_LENGTH=0x0000000000000010')

$reports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) { throw "Phase 41 boot missing marker '$marker': $($serial.FullName)" }
    }
    foreach ($field in $requiredFields) {
        if (-not $text.Contains($field)) { throw "Phase 41 boot missing proof field '$field': $($serial.FullName)" }
    }
    $encoded = [regex]::Match($text, 'MANAGED_HTTPS_PHASE41_ENCODED_BYTES=0x([0-9A-Fa-f]+)')
    if (-not $encoded.Success -or [Convert]::ToInt32($encoded.Groups[1].Value, 16) -le 0 -or
        [Convert]::ToInt32($encoded.Groups[1].Value, 16) -ge $resourceLength) {
        throw "Phase 41 boot did not prove compressed transfer: $($serial.FullName)"
    }
    foreach ($prefix in @('47','75','69','64','65','58','4F','53','20','34','31','0D','0A','52','E9','73')) {
        if (-not $text.Contains("MANAGED_HTTPS_PHASE41_PREFIX_SCALAR=0x$($prefix.PadLeft(16, '0'))")) {
            throw "Phase 41 boot missing prefix scalar ${prefix}: $($serial.FullName)"
        }
    }
    $peak = [regex]::Match($text, 'MANAGED_HTTPS_PHASE41_PEAK_TEXT_BUFFER=0x([0-9A-Fa-f]+)')
    if (-not $peak.Success -or [Convert]::ToInt32($peak.Groups[1].Value, 16) -gt 1024) {
        throw "Phase 41 boot exceeded the text output window: $($serial.FullName)"
    }
    $words = @('6D91A155','D767AC7C','0C1E2C5B','49479CF1','D7FDE8DF','7C4F459A','9BCECE43','EF11DF79')
    foreach ($word in $words) {
        if (-not $text.Contains("MANAGED_HTTPS_PHASE41_RESOURCE_SHA256_WORD=0x$($word.PadLeft(16, '0'))")) {
            throw "Phase 41 boot missing SHA-256 word ${word}: $($serial.FullName)"
        }
    }
    $segments = [regex]::Match($text, 'MANAGED_HTTPS_PHASE41_TEXT_SEGMENTS=0x([0-9A-Fa-f]+)')
    if (-not $segments.Success -or [Convert]::ToInt32($segments.Groups[1].Value, 16) -le 0) {
        throw "Phase 41 boot did not deliver segmented text: $($serial.FullName)"
    }
    if (([regex]::Matches($text, 'MANAGED_HTTPS_PHASE41_RESOURCE_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'MANAGED_KERNEL_PHASE41_PASS')).Count -ne 1 -or
        $text.Contains('GXOS_NET10:FAIL:') -or $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 41 boot did not finish cleanly: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($reports.Count -ne $RunCount) { throw "Expected $RunCount Phase 41 serial logs, found $($reports.Count)." }

$summary = @(
    'MANAGED_KERNEL_PHASE41_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE41_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE41_DECODED_RESOURCE_LENGTH=$resourceLength",
    "MANAGED_KERNEL_PHASE41_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE41_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE41_PAYLOAD_SIZE=$payloadSize",
    'MANAGED_KERNEL_PHASE41_CONTENT_TYPE=text/plain; charset=utf-8',
    'MANAGED_KERNEL_PHASE41_CONTENT_ENCODING=gzip',
    'MANAGED_KERNEL_PHASE41_SCALAR_COUNT=7680',
    'MANAGED_KERNEL_PHASE41_PREFIX_LENGTH=16',
    'MANAGED_KERNEL_PHASE41_PAUSE_STABLE_POLLS=4',
    'MANAGED_KERNEL_PHASE41_TEXT_INPUT_WINDOW=1024',
    'MANAGED_KERNEL_PHASE41_TEXT_OUTPUT_WINDOW=1024',
    'MANAGED_KERNEL_PHASE41_DECODED_RESOURCE_LIMIT=4194304',
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase41-summary.log') -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE41_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE41_RESOURCE_SHA256=$resourceSha256"
Write-Output "MANAGED_KERNEL_PHASE41_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE41_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE41_RUNS=$RunCount"

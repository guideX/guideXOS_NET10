[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 42 boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase42-text-$stamp"
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

$resourceSha256 = 'FAC7D0EB02B0940018D627A86731E8754BD1010F10A825F91147D7908FFD3C44'
$resourceLength = 1894
$metadata = @(
    'MANAGED_KERNEL_PHASE42_RUN=BOUNDED_HTTPS_GZIP_HTML_TOKENIZER',
    'MANAGED_KERNEL_PHASE42_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE42_TARGET_PATH=/phase42/gzip',
    'MANAGED_KERNEL_PHASE42_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_UTF8_FIXTURE',
    'MANAGED_KERNEL_PHASE42_DEVICE=e1000e,addr=2',
    "MANAGED_KERNEL_PHASE42_DECODED_RESOURCE_LENGTH=$resourceLength",
    'MANAGED_KERNEL_PHASE42_RESOURCE_PATTERN=deterministic bounded HTML fixture',
    'MANAGED_KERNEL_PHASE42_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE42_CONTENT_ENCODING=gzip',
    "MANAGED_KERNEL_PHASE42_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE42_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE42_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE42_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE42_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE42_GATE=$gate",
    "MANAGED_KERNEL_PHASE42_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase42-run-metadata.log') -Value $metadata -Encoding ascii

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase42 `
    -EnableNativeAotStartup -EnableManagedKernelPhase42 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 42 build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase42Protocol `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 42 fresh boots failed: $LASTEXITCODE" }

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE42_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE42_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_BEGIN',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_CONFIGURED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_REQUEST_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE42_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE42_PASS')

$requiredFields = @(
    'MANAGED_HTTPS_PHASE42_STATUS=0x00000000000000C8',
    'MANAGED_HTTPS_PHASE42_MIME=0x0000000000000002',
    'MANAGED_HTTPS_PHASE42_CHARSET=0x0000000000000001',
    'MANAGED_HTTPS_PHASE42_DECOMPRESSED_BYTES=0x0000000000000766',
    'MANAGED_HTTPS_PHASE42_TEXT_INPUT_BYTES=0x0000000000000766',
    'MANAGED_HTTPS_PHASE42_SCALARS_RECEIVED=0x000000000000075B',
    'MANAGED_HTTPS_PHASE42_SCALARS_CONSUMED=0x000000000000075B',
    'MANAGED_HTTPS_PHASE42_TOKENS=0x0000000000000034',
    'MANAGED_HTTPS_PHASE42_TEXT_TOKENS=0x0000000000000013',
    'MANAGED_HTTPS_PHASE42_START_TAGS=0x0000000000000010',
    'MANAGED_HTTPS_PHASE42_END_TAGS=0x000000000000000E',
    'MANAGED_HTTPS_PHASE42_COMMENTS=0x0000000000000001',
    'MANAGED_HTTPS_PHASE42_DOCTYPES=0x0000000000000001',
    'MANAGED_HTTPS_PHASE42_ATTRIBUTES=0x0000000000000015',
    'MANAGED_HTTPS_PHASE42_TEXT_SCALARS=0x0000000000000552',
    'MANAGED_HTTPS_PHASE42_ENTITIES=0x0000000000000005',
    'MANAGED_HTTPS_PHASE42_PAUSE_COUNT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE42_RESUME_COUNT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE42_STABLE_PAUSED_POLLS=0x0000000000000004')

$reports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) { throw "Phase 42 boot missing marker '$marker': $($serial.FullName)" }
    }
    foreach ($field in $requiredFields) {
        if (-not $text.Contains($field)) { throw "Phase 42 boot missing proof field '$field': $($serial.FullName)" }
    }
    $encoded = [regex]::Match($text, 'MANAGED_HTTPS_PHASE42_ENCODED_BYTES=0x([0-9A-Fa-f]+)')
    if (-not $encoded.Success -or [Convert]::ToInt32($encoded.Groups[1].Value, 16) -le 0 -or
        [Convert]::ToInt32($encoded.Groups[1].Value, 16) -ge $resourceLength) {
        throw "Phase 42 boot did not prove compressed transfer: $($serial.FullName)"
    }
    $words = @('15967F70','BB89C5AC','00D73E4D','4D73B057','F6C48E67','0F3EE789','C0A0B945','705605B6')
    foreach ($word in $words) {
        if (-not $text.Contains("MANAGED_HTTPS_PHASE42_TOKEN_HASH_WORD=0x$($word.PadLeft(16, '0'))")) {
            throw "Phase 42 boot missing token hash word ${word}: $($serial.FullName)"
        }
    }
    $resourceWords = @('FAC7D0EB','02B09400','18D627A8','6731E875','4BD1010F','10A825F9','1147D790','8FFD3C44')
    foreach ($word in $resourceWords) {
        if (-not $text.Contains("MANAGED_HTTPS_PHASE42_RESOURCE_SHA256_WORD=0x$($word.PadLeft(16, '0'))")) {
            throw "Phase 42 boot missing resource SHA-256 word ${word}: $($serial.FullName)"
        }
    }
    foreach ($peakName in @('PEAK_HTTP_BUFFER', 'PEAK_DECOMPRESSION_BUFFER', 'PEAK_TEXT_BUFFER', 'PEAK_TOKENIZER_TEXT')) {
        $peakValue = [regex]::Match($text, "MANAGED_HTTPS_PHASE42_${peakName}=0x([0-9A-Fa-f]+)")
        if (-not $peakValue.Success) {
            throw "Phase 42 boot missing bounded peak '${peakName}': $($serial.FullName)"
        }
    }
    $peak = [regex]::Match($text, 'MANAGED_HTTPS_PHASE42_PEAK_TOKENIZER_TEXT=0x([0-9A-Fa-f]+)')
    if (-not $peak.Success -or [Convert]::ToInt32($peak.Groups[1].Value, 16) -gt 128) {
        throw "Phase 42 boot exceeded the tokenizer text window: $($serial.FullName)"
    }
    if (([regex]::Matches($text, 'MANAGED_HTTPS_PHASE42_RESOURCE_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'MANAGED_KERNEL_PHASE42_PASS')).Count -ne 1 -or
        $text.Contains('GXOS_NET10:FAIL:') -or $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 42 boot did not finish cleanly: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($reports.Count -ne $RunCount) { throw "Expected $RunCount Phase 42 serial logs, found $($reports.Count)." }

$summary = @(
    'MANAGED_KERNEL_PHASE42_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE42_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE42_DECODED_RESOURCE_LENGTH=$resourceLength",
    "MANAGED_KERNEL_PHASE42_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE42_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE42_PAYLOAD_SIZE=$payloadSize",
    'MANAGED_KERNEL_PHASE42_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE42_CONTENT_ENCODING=gzip',
    'MANAGED_KERNEL_PHASE42_TOKEN_COUNT=52',
    'MANAGED_KERNEL_PHASE42_TOKEN_HASH=15967F70BB89C5AC00D73E4D4D73B057F6C48E670F3EE789C0A0B945705605B6',
    'MANAGED_KERNEL_PHASE42_PAUSE_STABLE_POLLS=4',
    'MANAGED_KERNEL_PHASE42_TOKENIZER_TEXT_WINDOW=128',
    'MANAGED_KERNEL_PHASE42_TOKENIZER_INPUT_WINDOW=256',
    'MANAGED_KERNEL_PHASE42_DECODED_RESOURCE_LIMIT=4194304',
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase42-summary.log') -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE42_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE42_RESOURCE_SHA256=$resourceSha256"
Write-Output "MANAGED_KERNEL_PHASE42_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE42_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE42_RUNS=$RunCount"

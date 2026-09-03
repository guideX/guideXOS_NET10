[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 43 boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase43-document-$stamp"
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

$resourceSha256 = 'F5E393FF306737E41C5BD930642786870E733E8F1644BD75E28A138DFB82EB21'
$resourceLength = 2566
$metadata = @(
    'MANAGED_KERNEL_PHASE43_RUN=BOUNDED_HTTPS_GZIP_HTML_DOCUMENT',
    'MANAGED_KERNEL_PHASE43_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE43_TARGET_PATH=/phase43/gzip',
    'MANAGED_KERNEL_PHASE43_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_UTF8_FIXTURE',
    'MANAGED_KERNEL_PHASE43_DEVICE=e1000e,addr=2',
    "MANAGED_KERNEL_PHASE43_DECODED_RESOURCE_LENGTH=$resourceLength",
    'MANAGED_KERNEL_PHASE43_RESOURCE_PATTERN=deterministic bounded HTML document fixture',
    'MANAGED_KERNEL_PHASE43_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE43_CONTENT_ENCODING=gzip',
    "MANAGED_KERNEL_PHASE43_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE43_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE43_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE43_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE43_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE43_GATE=$gate",
    "MANAGED_KERNEL_PHASE43_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase43-run-metadata.log') -Value $metadata -Encoding ascii

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase43 `
    -EnableNativeAotStartup -EnableManagedKernelPhase43 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 43 build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase43Protocol `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 43 fresh boots failed: $LASTEXITCODE" }

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE43_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE43_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_BEGIN',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_CONFIGURED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_REQUEST_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE43_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE43_PASS')

$requiredFields = @(
    'MANAGED_HTTPS_PHASE43_STATUS=0x00000000000000C8',
    'MANAGED_HTTPS_PHASE43_MIME=0x0000000000000002',
    'MANAGED_HTTPS_PHASE43_CHARSET=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_DECOMPRESSED_BYTES=0x0000000000000A06',
    'MANAGED_HTTPS_PHASE43_TEXT_INPUT_BYTES=0x0000000000000A06',
    'MANAGED_HTTPS_PHASE43_SCALARS_RECEIVED=0x00000000000009BB',
    'MANAGED_HTTPS_PHASE43_SCALARS_CONSUMED=0x00000000000009BB',
    'MANAGED_HTTPS_PHASE43_TOKENS=0x00000000000000FB',
    'MANAGED_HTTPS_PHASE43_TEXT_TOKENS=0x000000000000003C',
    'MANAGED_HTTPS_PHASE43_START_TAGS=0x0000000000000061',
    'MANAGED_HTTPS_PHASE43_END_TAGS=0x000000000000005B',
    'MANAGED_HTTPS_PHASE43_COMMENTS=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_DOCTYPES=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_ATTRIBUTES=0x000000000000003E',
    'MANAGED_HTTPS_PHASE43_NODES=0x00000000000000A0',
    'MANAGED_HTTPS_PHASE43_NODE_ARENA_USED=0x00000000000000A0',
    'MANAGED_HTTPS_PHASE43_NODE_ARENA_PEAK=0x00000000000000A0',
    'MANAGED_HTTPS_PHASE43_NODE_ARENA_CAPACITY=0x0000000000000400',
    'MANAGED_HTTPS_PHASE43_ELEMENTS=0x0000000000000062',
    'MANAGED_HTTPS_PHASE43_TEXT_NODES=0x000000000000003C',
    'MANAGED_HTTPS_PHASE43_TEXT_SCALARS=0x0000000000000296',
    'MANAGED_HTTPS_PHASE43_TEXT_ARENA_USED=0x0000000000000296',
    'MANAGED_HTTPS_PHASE43_TEXT_ARENA_PEAK=0x0000000000000296',
    'MANAGED_HTTPS_PHASE43_TEXT_ARENA_CAPACITY=0x0000000000010000',
    'MANAGED_HTTPS_PHASE43_ATTRIBUTE_ARENA_USED=0x000000000000003E',
    'MANAGED_HTTPS_PHASE43_ATTRIBUTE_ARENA_CAPACITY=0x0000000000000800',
    'MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_SCALARS=0x00000000000000CA',
    'MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_ARENA_USED=0x00000000000000CA',
    'MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_ARENA_PEAK=0x00000000000000CA',
    'MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_ARENA_CAPACITY=0x0000000000004000',
    'MANAGED_HTTPS_PHASE43_PEAK_DEPTH=0x0000000000000007',
    'MANAGED_HTTPS_PHASE43_FINAL_DEPTH=0x0000000000000000',
    'MANAGED_HTTPS_PHASE43_STACK_CAPACITY=0x0000000000000080',
    'MANAGED_HTTPS_PHASE43_IMPLIED=0x0000000000000000',
    'MANAGED_HTTPS_PHASE43_UNMATCHED_END_TAGS=0x0000000000000000',
    'MANAGED_HTTPS_PHASE43_IMPLICIT_CLOSES=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_ROOT_HANDLE=0x0000000000000000',
    'MANAGED_HTTPS_PHASE43_HTML_HANDLE=0x0000000000000002',
    'MANAGED_HTTPS_PHASE43_HEAD_HANDLE=0x0000000000000003',
    'MANAGED_HTTPS_PHASE43_BODY_HANDLE=0x000000000000000C',
    'MANAGED_HTTPS_PHASE43_DOCTYPE_HANDLE=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_HTML_PRESENT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_HEAD_PRESENT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_BODY_PRESENT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_PREFIX_COUNT=0x0000000000000010',
    'MANAGED_HTTPS_PHASE43_PAUSE_COUNT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_RESUME_COUNT=0x0000000000000001',
    'MANAGED_HTTPS_PHASE43_STABLE_PAUSED_POLLS=0x0000000000000004')

$documentWords = @('E693068D','356DCA59','E61D57CD','387C261C','F452373B','98364F4B','1A6D3EFC','B281224A')
$resourceWords = @('F5E393FF','306737E4','1C5BD930','64278687','0E733E8F','1644BD75','E28A138D','FB82EB21')
$reports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) { throw "Phase 43 boot missing marker '$marker': $($serial.FullName)" }
    }
    foreach ($field in $requiredFields) {
        if (-not $text.Contains($field)) { throw "Phase 43 boot missing proof field '$field': $($serial.FullName)" }
    }
    $encoded = [regex]::Match($text, 'MANAGED_HTTPS_PHASE43_ENCODED_BYTES=0x([0-9A-Fa-f]+)')
    if (-not $encoded.Success -or [Convert]::ToInt32($encoded.Groups[1].Value, 16) -le 0 -or
        [Convert]::ToInt32($encoded.Groups[1].Value, 16) -ge $resourceLength) {
        throw "Phase 43 boot did not prove compressed transfer: $($serial.FullName)"
    }
    foreach ($word in $documentWords) {
        if (-not $text.Contains("MANAGED_HTTPS_PHASE43_DOCUMENT_HASH_WORD=0x$($word.PadLeft(16, '0'))")) {
            throw "Phase 43 boot missing document hash word ${word}: $($serial.FullName)"
        }
    }
    foreach ($word in $resourceWords) {
        if (-not $text.Contains("MANAGED_HTTPS_PHASE43_RESOURCE_SHA256_WORD=0x$($word.PadLeft(16, '0'))")) {
            throw "Phase 43 boot missing resource SHA-256 word ${word}: $($serial.FullName)"
        }
    }
    foreach ($peakName in @('PEAK_HTTP_BUFFER', 'PEAK_DECOMPRESSION_BUFFER', 'PEAK_TEXT_BUFFER', 'PEAK_TOKENIZER_TEXT')) {
        $peakValue = [regex]::Match($text, "MANAGED_HTTPS_PHASE43_${peakName}=0x([0-9A-Fa-f]+)")
        if (-not $peakValue.Success) {
            throw "Phase 43 boot missing bounded peak '${peakName}': $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'MANAGED_HTTPS_PHASE43_RESOURCE_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'MANAGED_KERNEL_PHASE43_PASS')).Count -ne 1 -or
        $text.Contains('GXOS_NET10:FAIL:') -or $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 43 boot did not finish cleanly: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($reports.Count -ne $RunCount) { throw "Expected $RunCount Phase 43 serial logs, found $($reports.Count)." }

$summary = @(
    'MANAGED_KERNEL_PHASE43_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE43_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE43_DECODED_RESOURCE_LENGTH=$resourceLength",
    "MANAGED_KERNEL_PHASE43_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE43_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE43_PAYLOAD_SIZE=$payloadSize",
    'MANAGED_KERNEL_PHASE43_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE43_CONTENT_ENCODING=gzip',
    'MANAGED_KERNEL_PHASE43_NODE_COUNT=160',
    'MANAGED_KERNEL_PHASE43_DOCUMENT_HASH=E693068D356DCA59E61D57CD387C261CF452373B98364F4B1A6D3EFCB281224A',
    'MANAGED_KERNEL_PHASE43_PAUSE_STABLE_POLLS=4',
    'MANAGED_KERNEL_PHASE43_NODE_ARENA_CAPACITY=1024',
    'MANAGED_KERNEL_PHASE43_TEXT_SCALAR_ARENA_CAPACITY=65536',
    'MANAGED_KERNEL_PHASE43_ATTRIBUTE_ARENA_CAPACITY=2048',
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase43-summary.log') -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE43_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE43_RESOURCE_SHA256=$resourceSha256"
Write-Output "MANAGED_KERNEL_PHASE43_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE43_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE43_RUNS=$RunCount"

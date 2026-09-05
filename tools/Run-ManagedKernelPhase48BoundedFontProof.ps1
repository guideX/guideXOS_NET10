[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 48 positive boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase48-font-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
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

# This is the Phase 46 fixture after the exact transformation performed by
# New-Phase48GzipBody11. Keeping the metadata copy here makes the served
# resource identity independently auditable from the boot harness.
$html = "<!doctype html><html><head><title>GuideX Phase 48</title><style>body{display:block;font-size:16px;color:#204060;margin:8px;padding:4px;overflow-x:hidden}#main{display:block;width:75%;min-width:320px;max-width:700px;margin:10px 12px 14px 16px;padding:8px 9px 10px 11px;border-width:2px;border-style:solid;border-color:#112233;position:relative;overflow:hidden;opacity:.5;z-index:1}article{display:block}.note{margin-top:5px;opacity:.5;background-color:#123456}.inline{display:inline;font-weight:bold}.hidden{visibility:hidden;background-color:red}.gone{display:none;background-color:blue}pre{display:block;white-space:pre-wrap}.neg{display:block;position:fixed;top:4px;left:6px;width:40px;height:12px;z-index:-1;background-color:blue;border-width:1px;border-style:solid;border-color:white}.pos{display:block;position:absolute;top:8px;left:10px;width:42px;height:12px;z-index:2;background-color:green}table{display:table}tr{display:table-row}td{display:table-cell}</style></head><body><main id=main><article><h1>Bounded display list</h1><p class=note>Phase 48 <span class=inline>semantic paint commands</span> stay bounded and deterministic.<br>Second line.</p><p>Unicode: R&#233;sum&#233; &#955;&#951; &#20013; &#9733; &#128578;.</p><pre id=pre>pre line one`r`npre line two with preserved spaces</pre><img id=logo width=32 height=16 alt=logo><div class=hidden><span>hidden descendant</span></div><div class=gone>must not produce a box</div><div class=neg>negative z</div><div class=pos>positive z</div><table><tr><td>A</td><td>B</td></tr></table></article></main></body></html>"
$resourceBytes = [Text.Encoding]::UTF8.GetBytes($html)
$resourceLength = $resourceBytes.Length
$resourceSha256 = (Get-FileHash -InputStream ([IO.MemoryStream]::new($resourceBytes)) -Algorithm SHA256).Hash.ToUpperInvariant()
$metadata = @(
    'MANAGED_KERNEL_PHASE48_RUN=BOUNDED_HTTPS_GZIP_HTML_CSS_LAYOUT_PAINT_FONT',
    'MANAGED_KERNEL_PHASE48_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE48_TARGET_PATH=/phase48/gzip',
    'MANAGED_KERNEL_PHASE48_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_UTF8_FIXTURE',
    'MANAGED_KERNEL_PHASE48_DEVICE=e1000e,addr=2',
    "MANAGED_KERNEL_PHASE48_DECODED_RESOURCE_LENGTH=$resourceLength",
    'MANAGED_KERNEL_PHASE48_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE48_CONTENT_ENCODING=gzip',
    "MANAGED_KERNEL_PHASE48_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE48_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE48_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE48_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE48_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE48_GATE=$gate",
    "MANAGED_KERNEL_PHASE48_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase48-run-metadata.log') -Value $metadata -Encoding ascii

$gateParameters = @{
    OutputDirectory = $gate
    ManagedArtifact = $payload
    PayloadMode = 'ManagedKernel'
    Scenario = 'ManagedKernelPhase48'
    EnableNativeAotStartup = $true
    EnableManagedKernelPhase48 = $true
    AssumeUnspecifiedTimezoneUtc = $true
}
& $buildGate @gateParameters
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 48 build failed: $LASTEXITCODE" }

$bootParameters = @{
    GateDirectory = $gate
    EvidenceDirectory = $evidence
    PayloadSha256 = $payloadHash
    PayloadSize = [long]$payloadSize
    RunCount = $RunCount
    TimeoutSeconds = $TimeoutSeconds
    EnablePhase15Rx = $true
    EnablePhase48Protocol = $true
    EnablePhase26VirtioRng = $true
}
& $runFreshBoots @bootParameters
if ($LASTEXITCODE -ne 0) { throw "Phase 48 fresh boots failed: $LASTEXITCODE" }

$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE48_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE48_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_BEGIN',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_CONFIGURED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_RESOURCE_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_RESOURCE_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_REQUEST_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_RESOURCE_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_CSS_TREE_VALIDATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_CSS_ENGINE_CREATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_LAYOUT_ENGINE_CREATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_PAINT_ENGINE_CREATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_FIXED_SCROLL_PROOF_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_NESTED_OPACITY_PROOF_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_FONT_VALIDATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_PAINT_VERIFIED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_RASTER_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE48_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE48_PASS')

$reports = @()
$serialLogs = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)
foreach ($serial in $serialLogs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) {
        if (-not $text.Contains($marker)) { throw "Phase 48 boot missing '$marker': $($serial.FullName)" }
    }
    $status = [regex]::Match($text, 'MANAGED_HTTPS_PHASE48_STATUS=0x([0-9A-Fa-f]+)')
    $decompressed = [regex]::Match($text, 'MANAGED_HTTPS_PHASE48_DECOMPRESSED_BYTES=0x([0-9A-Fa-f]+)')
    $paintHash = [regex]::Match($text, 'MANAGED_HTTPS_PHASE48_PAINT_HASH_WORD=0x([0-9A-Fa-f]+)')
    $fontHash = [regex]::Match($text, 'MANAGED_HTTPS_PHASE48_FONT_SEMANTIC_HASH_WORD=0x([0-9A-Fa-f]+)')
    $phasePassAt = $text.LastIndexOf('GXOS_NET10:MANAGED_KERNEL_PHASE48_PASS', [StringComparison]::Ordinal)
    $unexpectedImportAt = $text.IndexOf('GXOS_NET10:UNEXPECTED_IMPORT_CALL:', [StringComparison]::Ordinal)
    $resourceHashWords = @([regex]::Matches(
        $text, 'MANAGED_HTTPS_PHASE48_RESOURCE_SHA256_WORD=0x([0-9A-Fa-f]{16})') |
        ForEach-Object { $_.Groups[1].Value.ToUpperInvariant() })
    $expectedResourceHashWords = @()
    for ($offset = 0; $offset -lt $resourceSha256.Length; $offset += 8) {
        $expectedResourceHashWords += $resourceSha256.Substring($offset, 8).PadLeft(16, '0')
    }
    if (-not $status.Success -or [Convert]::ToInt32($status.Groups[1].Value, 16) -ne 200 -or
        -not $decompressed.Success -or [Convert]::ToInt32($decompressed.Groups[1].Value, 16) -ne $resourceLength -or
        -not $paintHash.Success -or -not $fontHash.Success -or
        $resourceHashWords.Count -ne $expectedResourceHashWords.Count -or
        (($resourceHashWords -join ',') -ne ($expectedResourceHashWords -join ',')) -or
        $text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        ($unexpectedImportAt -ge 0 -and ($phasePassAt -lt 0 -or $unexpectedImportAt -lt $phasePassAt))) {
        throw "Phase 48 boot did not prove the expected bounded font result: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($reports.Count -ne $RunCount) { throw "Expected $RunCount Phase 48 serial logs, found $($reports.Count)." }

$summary = @(
    'MANAGED_KERNEL_PHASE48_BOUNDED_FONT_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE48_BOUNDED_FONT_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE48_DECODED_RESOURCE_LENGTH=$resourceLength",
    "MANAGED_KERNEL_PHASE48_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE48_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE48_PAYLOAD_SIZE=$payloadSize",
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase48-summary.log') -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE48_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE48_RESOURCE_SHA256=$resourceSha256"
Write-Output "MANAGED_KERNEL_PHASE48_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE48_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE48_RUNS=$RunCount"

[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 50 positive boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase50-extended-unicode-$stamp"
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

$runnerSource = Get-Content -LiteralPath $runFreshBoots -Raw
$fixtureMatch = [regex]::Match($runnerSource, '\$html = "([^"\r\n]*Bounded display list[^"\r\n]*)"')
if (-not $fixtureMatch.Success) { throw 'Could not locate the authoritative Phase 46 fixture.' }
$html = $fixtureMatch.Groups[1].Value.Replace('`r`n', "`r`n").
    Replace('GuideX Phase 46', 'GuideX Phase 49').
    Replace('Phase 46', 'Phase 49').
    Replace('Bounded display list', 'guideXOS Managed Kernel').
    Replace('semantic paint commands', 'iiii WWWW 0123456789').
    Replace('</style>', '.phase49-red{display:block;position:fixed;top:28px;left:4px;width:20px;height:8px;background-color:red;z-index:3}.phase49-green{display:block;position:fixed;top:28px;left:28px;width:20px;height:8px;background-color:#00FF00;z-index:3}.phase49-blue{display:block;position:fixed;top:28px;left:52px;width:20px;height:8px;background-color:blue;z-index:3}.phase49-black{display:block;position:fixed;top:28px;left:76px;width:20px;height:8px;background-color:black;z-index:3}.phase49-nontrivial{display:block;position:fixed;top:28px;left:100px;width:20px;height:8px;background-color:#123456;z-index:3}.phase49-badge{display:block;position:fixed;top:44px;left:4px;width:34px;height:10px;background-color:#8385C7;border-width:1px;border-style:solid;border-color:white;z-index:3}</style>').
    Replace('</body>', '<div class=phase49-red></div><div class=phase49-green></div><div class=phase49-blue></div><div class=phase49-black></div><div class=phase49-nontrivial></div><div class=phase49-badge>PASS</div></body>').
    Replace('GuideX Phase 49', 'GuideX Phase 50').
    Replace('Phase 49', 'Phase 50').
    Replace('phase49-', 'phase50-').
    Replace('iiii WWWW 0123456789', 'cafe&#233; R&#233;sum&#233; &#8211; &#8220;guideXOS&#8482;&#8221;&#160;&#8364;&#8230;')
$resourceBytes = [Text.Encoding]::UTF8.GetBytes($html)
$resourceLength = $resourceBytes.Length
$sha = [Security.Cryptography.SHA256]::Create()
try { $resourceHash = [Convert]::ToHexString($sha.ComputeHash($resourceBytes)) }
finally { $sha.Dispose() }
$metadata = @(
    'MANAGED_KERNEL_PHASE50_RUN=BOUNDED_HTTPS_GZIP_HTML_CSS_LAYOUT_PAINT_EXTENDED_UNICODE_GOP_SCREEN',
    'MANAGED_KERNEL_PHASE50_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE50_TARGET_PATH=/phase50/gzip',
    'MANAGED_KERNEL_PHASE50_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_UTF8_FIXTURE',
    'MANAGED_KERNEL_PHASE50_DEVICE=e1000e,addr=2',
    "MANAGED_KERNEL_PHASE50_DECODED_RESOURCE_LENGTH=$resourceLength",
    'MANAGED_KERNEL_PHASE50_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE50_CONTENT_ENCODING=gzip',
    "MANAGED_KERNEL_PHASE50_RESOURCE_SHA256=$resourceHash",
    "MANAGED_KERNEL_PHASE50_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE50_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE50_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE50_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE50_GATE=$gate",
    "MANAGED_KERNEL_PHASE50_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase50-run-metadata.log') -Value $metadata -Encoding ascii

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase50 -EnableNativeAotStartup -EnableManagedKernelPhase50 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 50 build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx `
    -EnablePhase50Protocol -CaptureQemuScreen -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 50 fresh boots failed: $LASTEXITCODE" }

$serialLogs = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse | Sort-Object FullName)
if ($serialLogs.Count -ne $RunCount) { throw "Expected $RunCount serial logs, found $($serialLogs.Count)." }
$screenHashes = @()
foreach ($serial in $serialLogs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in @(
        'GXOS_NET10:MANAGED_KERNEL_PHASE50_MODE_SELECTED',
        'GXOS_NET10:MANAGED_KERNEL_PHASE50_STARTING',
        'GXOS_NET10:MANAGED_HTTPS_PHASE50_FONT_COVERAGE_FLAGS=',
        'GXOS_NET10:MANAGED_HTTPS_PHASE50_FONT_LATIN1_LOOKUPS=',
        'GXOS_NET10:MANAGED_HTTPS_PHASE50_FONT_NBSP_COUNT=',
        'GXOS_NET10:MANAGED_HTTPS_PHASE50_VISIBLE_PAGE_PASS',
        'GXOS_NET10:MANAGED_KERNEL_PHASE50_PASS')) {
        if (-not $text.Contains($marker)) { throw "Missing Phase 50 marker '$marker' in $($serial.FullName)." }
    }
    $screenPath = Join-Path (Split-Path -Parent $serial.FullName) 'qemu-screen.ppm'
    if (-not (Test-Path -LiteralPath $screenPath)) { throw "Missing QEMU screen capture: $screenPath" }
    $screenHashes += (Get-FileHash -LiteralPath $screenPath -Algorithm SHA256).Hash.ToUpperInvariant()
}
if (@($screenHashes | Select-Object -Unique).Count -ne 1) { throw 'QEMU screen hashes changed between Phase 50 boots.' }
$summary = @(
    'MANAGED_KERNEL_PHASE50_EXTENDED_UNICODE_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE50_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE50_DECODED_RESOURCE_LENGTH=$resourceLength",
    "MANAGED_KERNEL_PHASE50_RESOURCE_SHA256=$resourceHash",
    "MANAGED_KERNEL_PHASE50_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE50_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE50_SCREEN_SHA256=$($screenHashes[0])")
Set-Content -LiteralPath (Join-Path $output 'phase50-summary.log') -Value $summary -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE50_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE50_RESOURCE_SHA256=$resourceHash"
Write-Output "MANAGED_KERNEL_PHASE50_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE50_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE50_SCREEN_SHA256=$($screenHashes[0])"

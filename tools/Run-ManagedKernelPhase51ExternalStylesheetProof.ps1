[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 51 positive boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase51-external-stylesheet-$stamp"
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

$metadata = @(
    'MANAGED_KERNEL_PHASE51_RUN=BOUNDED_HTTPS_GZIP_HTML_EXTERNAL_CSS_DOC_ORDER_LAYOUT_PAINT_RASTER_GOP_SCREEN',
    'MANAGED_KERNEL_PHASE51_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE51_TARGET_PATH=/phase51/index.html',
    'MANAGED_KERNEL_PHASE51_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_HTML_AND_CSS_FIXTURE',
    'MANAGED_KERNEL_PHASE51_DEVICE=e1000e,addr=2',
    'MANAGED_KERNEL_PHASE51_EXTERNAL_STYLESHEET_LIMIT=2',
    'MANAGED_KERNEL_PHASE51_EXTERNAL_ARENA_CAPACITY=4',
    "MANAGED_KERNEL_PHASE51_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE51_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE51_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE51_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE51_GATE=$gate",
    "MANAGED_KERNEL_PHASE51_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase51-run-metadata.log') -Value $metadata -Encoding ascii

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase51 -EnableNativeAotStartup -EnableManagedKernelPhase51 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 51 build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx `
    -EnablePhase51Protocol -CaptureQemuScreen -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 51 fresh boots failed: $LASTEXITCODE" }

function Require51([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-Hex51([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    Require51 $match.Success "Missing $name in serial evidence."
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

function Read-Ppm51([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $prefix = [Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(4096, $bytes.Length))
    $match = [regex]::Match($prefix, '\AP6\s+(\d+)\s+(\d+)\s+(\d+)\s')
    Require51 $match.Success "QEMU screen is not a P6 PPM: $path"
    $width = [int]$match.Groups[1].Value
    $height = [int]$match.Groups[2].Value
    Require51 ($match.Groups[3].Value -eq '255') "QEMU screen max value is not 255: $path"
    $offset = $match.Length
    Require51 ($offset + ([long]$width * $height * 3) -eq $bytes.Length) "PPM length mismatch: $path"
    [pscustomobject]@{ Bytes = $bytes; Width = $width; Height = $height; PixelOffset = $offset }
}

function Find-Ppm51([object]$image, [string]$color) {
    $rgb = [byte[]]@(
        [Convert]::ToByte($color.Substring(0, 2), 16),
        [Convert]::ToByte($color.Substring(2, 2), 16),
        [Convert]::ToByte($color.Substring(4, 2), 16))
    for ($y = 0; $y -lt $image.Height; ++$y) {
        for ($x = 0; $x -lt $image.Width; ++$x) {
            $index = $image.PixelOffset + (($y * $image.Width + $x) * 3)
            if ($image.Bytes[$index] -eq $rgb[0] -and
                $image.Bytes[$index + 1] -eq $rgb[1] -and
                $image.Bytes[$index + 2] -eq $rgb[2]) {
                return "($x,$y)"
            }
        }
    }
    return $null
}

$serialLogs = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse | Sort-Object FullName)
Require51 ($serialLogs.Count -eq $RunCount) "Expected $RunCount Phase 51 serial logs, found $($serialLogs.Count)."
$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE51_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE51_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_GOP_PRESENT_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_VISIBLE_PAGE_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE51_PASS')
$screenHashes = @()
$screenReports = @()
foreach ($serial in $serialLogs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) { Require51 $text.Contains($marker) "Missing '$marker' in $($serial.FullName)." }
    Require51 (!$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:') -and
               !$text.Contains('GXOS_NET10:FAIL:')) "Phase 51 boot faulted: $($serial.FullName)"
    Require51 ((Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_ENCOUNTERED') -eq 2) 'Phase 51 did not encounter two external sheets.'
    Require51 ((Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_STARTED') -eq 2) 'Phase 51 did not start two external sheets.'
    Require51 ((Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_LOADED') -eq 2) 'Phase 51 did not load two external sheets.'
    Require51 ((Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_EMBEDDED_PARSED') -eq 1) 'Phase 51 embedded stylesheet count changed.'
    Require51 ((Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_STYLESHEET_A_CONTENT_ENCODING') -ne 0 -and
               (Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_STYLESHEET_B_CONTENT_ENCODING') -ne 0) 'Phase 51 CSS gzip telemetry missing.'
    Require51 ((Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_TARGET_COLOR') -eq [UInt64]4278190335) 'Phase 51 target did not resolve to external blue CSS.'
    Require51 ((Get-Hex51 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE51_SOURCE_UNCHANGED') -eq 1) 'Phase 51 presentation mutated the source framebuffer.'
    $screenPath = Join-Path (Split-Path -Parent $serial.FullName) 'qemu-screen.ppm'
    Require51 (Test-Path -LiteralPath $screenPath) "Missing QEMU screen capture: $screenPath"
    $screenHashes += (Get-FileHash -LiteralPath $screenPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $image = Read-Ppm51 $screenPath
    foreach ($color in @('0000FF', '123456', 'FFFFFF')) {
        $coordinate = Find-Ppm51 $image $color
        Require51 ($null -ne $coordinate) "QEMU screen lacks proof color #${color}: $screenPath"
        $screenReports += "serial=$($serial.FullName) color=#$color coordinate=$coordinate"
    }
}
Require51 (@($screenHashes | Select-Object -Unique).Count -eq 1) 'QEMU screen hashes changed between Phase 51 boots.'

$summary = @(
    'MANAGED_KERNEL_PHASE51_EXTERNAL_STYLESHEET_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE51_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE51_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE51_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE51_SCREEN_SHA256=$($screenHashes[0])",
    $screenReports)
Set-Content -LiteralPath (Join-Path $output 'phase51-summary.log') -Value $summary -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE51_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE51_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE51_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE51_SCREEN_SHA256=$($screenHashes[0])"

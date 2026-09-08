[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 52 positive boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase52-external-png-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
$compile = Join-Path $output 'managed-build'
$gate = Join-Path $output 'gate4'
$evidence = Join-Path $output 'evidence'
$badEvidence = Join-Path $output 'bad-crc-evidence'
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
    'MANAGED_KERNEL_PHASE52_RUN=BOUNDED_HTTPS_HTML_EXTERNAL_PNG_LAYOUT_PAINT_RASTER_GOP_SCREEN',
    'MANAGED_KERNEL_PHASE52_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE52_TARGET_PATH=/phase52/index.html',
    'MANAGED_KERNEL_PHASE52_IMAGE_PATH=/phase52/image.png',
    'MANAGED_KERNEL_PHASE52_BACKEND=QEMU_DGRAM_DETERMINISTIC_RGBA8_PNG_FIXTURE',
    'MANAGED_KERNEL_PHASE52_DEVICE=e1000e,addr=2',
    'MANAGED_KERNEL_PHASE52_IMAGE_LIMIT=4',
    'MANAGED_KERNEL_PHASE52_IMAGE_MAX_DIMENSION=256',
    'MANAGED_KERNEL_PHASE52_IMAGE_PIXEL_BUDGET=65536',
    "MANAGED_KERNEL_PHASE52_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE52_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE52_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE52_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE52_GATE=$gate",
    "MANAGED_KERNEL_PHASE52_EVIDENCE=$evidence",
    "MANAGED_KERNEL_PHASE52_BAD_CRC_EVIDENCE=$badEvidence")
Set-Content -LiteralPath (Join-Path $output 'phase52-run-metadata.log') -Value $metadata -Encoding ascii

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase52 -EnableNativeAotStartup -EnableManagedKernelPhase52 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 52 build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx `
    -Phase15NetworkBackend dgram -EnablePhase52Protocol -CaptureQemuScreen `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 52 fresh boots failed: $LASTEXITCODE" }

function Require52([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-Hex52([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    Require52 $match.Success "Missing $name in serial evidence."
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

function Read-Ppm52([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $prefix = [Text.Encoding]::ASCII.GetString($bytes, 0, [Math]::Min(4096, $bytes.Length))
    $match = [regex]::Match($prefix, '\AP6\s+(\d+)\s+(\d+)\s+(\d+)\s')
    Require52 $match.Success "QEMU screen is not a P6 PPM: $path"
    $width = [int]$match.Groups[1].Value
    $height = [int]$match.Groups[2].Value
    $offset = $match.Length
    Require52 ($match.Groups[3].Value -eq '255') "QEMU screen max value is not 255: $path"
    Require52 ($offset + ([long]$width * $height * 3) -eq $bytes.Length) "PPM length mismatch: $path"
    [pscustomobject]@{ Bytes = $bytes; Width = $width; Height = $height; PixelOffset = $offset }
}

function Find-PpmColor52([object]$image, [byte[]]$rgb) {
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
Require52 ($serialLogs.Count -eq $RunCount) "Expected $RunCount Phase 52 serial logs, found $($serialLogs.Count)."
$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE52_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE52_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_PNG_HEADER_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_PNG_DECODE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_RASTER_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_GOP_PRESENT_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_VISIBLE_IMAGE_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE52_PASS')
$screenHashes = @()
$screenReports = @()
foreach ($serial in $serialLogs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) { Require52 $text.Contains($marker) "Missing '$marker' in $($serial.FullName)." }
    Require52 (!$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:') -and
               !$text.Contains('GXOS_NET10:FAIL:')) "Phase 52 boot faulted: $($serial.FullName)"
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_NODES') -eq 1) 'Phase 52 image-node count changed.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_REQUESTS') -eq 1) 'Phase 52 image request count changed.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGES_LOADED') -eq 1) 'Phase 52 image load count changed.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_STATUS') -eq 200) 'Phase 52 image status was not 200.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_MIME') -eq 9) 'Phase 52 image MIME was not PNG.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_WIDTH') -eq 48 -and
               (Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_HEIGHT') -eq 32) 'Phase 52 PNG dimensions changed.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_IDAT_CHUNKS') -eq 3) 'Phase 52 IDAT chunk split changed.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_DECODED_PIXELS') -eq 1536) 'Phase 52 decoded pixel count changed.'
    foreach ($filter in @('NONE','SUB','UP','AVERAGE','PAETH')) {
        Require52 ((Get-Hex52 $text "GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_FILTER_$filter") -gt 0) "Phase 52 PNG filter $filter was not exercised."
    }
    Require52 (([regex]::Matches($text, 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_SAMPLE_SOURCE=0x')).Count -eq 16 -and
               ([regex]::Matches($text, 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_SAMPLE_EXPECTED=0x')).Count -eq 16 -and
               ([regex]::Matches($text, 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_SAMPLE_FRAMEBUFFER=0x')).Count -eq 16) `
        'Phase 52 image source-to-framebuffer sample count changed.'
    Require52 ((Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_LAYOUT_W') -eq 96 -and
               (Get-Hex52 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_LAYOUT_H') -eq 64) 'Phase 52 image layout dimensions changed.'
    $screenPath = Join-Path (Split-Path -Parent $serial.FullName) 'qemu-screen.ppm'
    Require52 (Test-Path -LiteralPath $screenPath) "Missing QEMU screen capture: $screenPath"
    $screenHashes += (Get-FileHash -LiteralPath $screenPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $image = Read-Ppm52 $screenPath
    foreach ($color in @(
        [byte[]]@(0xFF,0x00,0x00), [byte[]]@(0x00,0xFF,0x00),
        [byte[]]@(0x00,0x00,0xFF), [byte[]]@(0xFF,0xFF,0xFF))) {
        $coordinate = Find-PpmColor52 $image $color
        Require52 ($null -ne $coordinate) "QEMU screen lacks a Phase 52 image proof color: $screenPath"
        $screenReports += "serial=$($serial.FullName) rgb=$($color -join ',') coordinate=$coordinate"
    }
}
Require52 (@($screenHashes | Select-Object -Unique).Count -eq 1) 'QEMU screen hashes changed between Phase 52 boots.'

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $badEvidence `
    -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
    -RunCount 1 -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx `
    -Phase15NetworkBackend dgram -EnablePhase52Protocol `
    -EnablePhase52BadPngCrcControl -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 52 bad-CRC control failed: $LASTEXITCODE" }
$badSerialLogs = @(Get-ChildItem -LiteralPath (Join-Path $badEvidence 'runs') -Filter serial.log -Recurse | Sort-Object FullName)
Require52 ($badSerialLogs.Count -eq 1) 'Expected one Phase 52 bad-CRC serial log.'
$badText = Get-Content -LiteralPath $badSerialLogs[0].FullName -Raw
foreach ($marker in @(
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_BAD_PNG_CRC_REASON=0x0000000000000005',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_BAD_PNG_CRC_IMAGE_SLOT_COUNT=0x0000000000000000',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_BAD_PNG_CRC_CONTROL_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE52_START_FAILED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_ACCOUNTING_RESTORED')) {
    Require52 $badText.Contains($marker) "Missing '$marker' in bad-CRC evidence."
}
foreach ($marker in @(
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_PNG_DECODE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_RASTER_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_GOP_PRESENT_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE52_VISIBLE_IMAGE_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE52_PASS')) {
    Require52 (-not $badText.Contains($marker)) "Bad-CRC control emitted '$marker'."
}
Require52 (-not $badText.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
           -not $badText.Contains('GXOS_NET10:PAGE_FAULT_') -and
           -not $badText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
    'Bad-CRC control reported a machine fault.'

$summary = @(
    'MANAGED_KERNEL_PHASE52_EXTERNAL_PNG_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE52_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE52_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE52_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE52_SCREEN_SHA256=$($screenHashes[0])",
    'MANAGED_KERNEL_PHASE52_BAD_CRC_CONTROL=PASS',
    $screenReports)
Set-Content -LiteralPath (Join-Path $output 'phase52-summary.log') -Value $summary -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE52_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE52_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE52_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE52_SCREEN_SHA256=$($screenHashes[0])"

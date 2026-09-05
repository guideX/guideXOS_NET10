[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 49 positive boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase49-visible-$stamp"
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

$html = "<!doctype html><html><head><title>guideXOS Managed Kernel Phase 49</title><style>body{display:block;font-size:12px;color:white;margin:4px;padding:4px;background-color:#101820;overflow:hidden}#main{display:block;width:150px;margin:2px;padding:4px;border-width:2px;border-style:solid;border-color:#8385C7;background-color:#203040;position:relative;overflow:hidden}article{display:block}.note{display:block;margin-top:3px;padding:2px;background-color:#123456;color:white}.plain{display:block;margin-top:3px;color:white}.bold{font-weight:bold}.red{display:block;width:24px;height:8px;background-color:red}.green{display:block;width:24px;height:8px;background-color:green}.blue{display:block;width:24px;height:8px;background-color:blue}.black{display:block;width:24px;height:8px;background-color:black}.fixed{display:block;position:fixed;top:4px;left:118px;width:34px;height:10px;background-color:#8385C7;border-width:1px;border-style:solid;border-color:white}.image{display:block;width:22px;height:10px;background-color:#123456;border-width:1px;border-style:solid;border-color:white}table{display:table}tr{display:table-row}td{display:table-cell}</style></head><body><main id=main><article><h1>guideXOS</h1><p class=note>Managed Kernel <span class=bold>Phase 49</span></p><p class=plain>iiii WWWW 0123456789</p><p class=plain><span class=bold>Bold text</span> and ordinary text.</p><div class=red></div><div class=green></div><div class=blue></div><div class=black></div><div class=image id=image>image</div><div class=fixed id=badge>PASS</div><p class=plain>RGB <span class=bold>FF123456</span></p></article></main></body></html>"
$runnerSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1') -Raw
$fixtureMatch = [regex]::Match($runnerSource, '\$html = "([^"\r\n]*Bounded display list[^"\r\n]*)"')
if (-not $fixtureMatch.Success) { throw 'Could not locate the authoritative Phase 46 fixture in the fresh-boot runner.' }
$html = $fixtureMatch.Groups[1].Value.Replace('`r`n', "`r`n").
    Replace('GuideX Phase 46', 'GuideX Phase 49').
    Replace('Phase 46', 'Phase 49').
    Replace('Bounded display list', 'guideXOS Managed Kernel').
    Replace('semantic paint commands', 'iiii WWWW 0123456789').
    Replace('</style>', '.phase49-red{display:block;position:fixed;top:28px;left:4px;width:20px;height:8px;background-color:red;z-index:3}.phase49-green{display:block;position:fixed;top:28px;left:28px;width:20px;height:8px;background-color:#00FF00;z-index:3}.phase49-blue{display:block;position:fixed;top:28px;left:52px;width:20px;height:8px;background-color:blue;z-index:3}.phase49-black{display:block;position:fixed;top:28px;left:76px;width:20px;height:8px;background-color:black;z-index:3}.phase49-nontrivial{display:block;position:fixed;top:28px;left:100px;width:20px;height:8px;background-color:#123456;z-index:3}.phase49-badge{display:block;position:fixed;top:44px;left:4px;width:34px;height:10px;background-color:#8385C7;border-width:1px;border-style:solid;border-color:white;z-index:3}</style>').
    Replace('</body>', '<div class=phase49-red></div><div class=phase49-green></div><div class=phase49-blue></div><div class=phase49-black></div><div class=phase49-nontrivial></div><div class=phase49-badge>PASS</div></body>')
$resourceBytes = [Text.Encoding]::UTF8.GetBytes($html)
$resourceLength = $resourceBytes.Length
$hashAlgorithm = [Security.Cryptography.SHA256]::Create()
try { $resourceSha256 = [Convert]::ToHexString($hashAlgorithm.ComputeHash($resourceBytes)) }
finally { $hashAlgorithm.Dispose() }
$metadata = @(
    'MANAGED_KERNEL_PHASE49_RUN=BOUNDED_HTTPS_GZIP_HTML_CSS_LAYOUT_PAINT_FONT_GOP_SCREEN',
    'MANAGED_KERNEL_PHASE49_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE49_TARGET_PATH=/phase49/gzip',
    'MANAGED_KERNEL_PHASE49_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_UTF8_FIXTURE',
    'MANAGED_KERNEL_PHASE49_DEVICE=e1000e,addr=2',
    "MANAGED_KERNEL_PHASE49_DECODED_RESOURCE_LENGTH=$resourceLength",
    'MANAGED_KERNEL_PHASE49_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE49_CONTENT_ENCODING=gzip',
    "MANAGED_KERNEL_PHASE49_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE49_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE49_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE49_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE49_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE49_GATE=$gate",
    "MANAGED_KERNEL_PHASE49_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase49-run-metadata.log') -Value $metadata -Encoding ascii

$gateParameters = @{
    OutputDirectory = $gate
    ManagedArtifact = $payload
    PayloadMode = 'ManagedKernel'
    Scenario = 'ManagedKernelPhase49'
    EnableNativeAotStartup = $true
    EnableManagedKernelPhase49 = $true
    AssumeUnspecifiedTimezoneUtc = $true
}
& $buildGate @gateParameters
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 49 build failed: $LASTEXITCODE" }

$bootParameters = @{
    GateDirectory = $gate
    EvidenceDirectory = $evidence
    PayloadSha256 = $payloadHash
    PayloadSize = [long]$payloadSize
    RunCount = $RunCount
    TimeoutSeconds = $TimeoutSeconds
    EnablePhase15Rx = $true
    EnablePhase49Protocol = $true
    CaptureQemuScreen = $true
    EnablePhase26VirtioRng = $true
}
& $runFreshBoots @bootParameters
if ($LASTEXITCODE -ne 0) { throw "Phase 49 fresh boots failed: $LASTEXITCODE" }

function Require49([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Hash49([byte[]]$bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [Convert]::ToHexString($sha.ComputeHash($bytes)) }
    finally { $sha.Dispose() }
}

function Read-Ppm49([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $prefixLength = [Math]::Min(4096, $bytes.Length)
    $prefix = [Text.Encoding]::ASCII.GetString($bytes, 0, $prefixLength)
    $match = [regex]::Match($prefix, '\AP6\s+(\d+)\s+(\d+)\s+(\d+)\s')
    Require49 $match.Success "QEMU screen is not a P6 PPM: $path"
    $width = [int]$match.Groups[1].Value
    $height = [int]$match.Groups[2].Value
    $maxValue = [int]$match.Groups[3].Value
    Require49 ($maxValue -eq 255 -and $width -gt 0 -and $height -gt 0) "Invalid PPM dimensions or max value: $path"
    $offset = $match.Length
    $pixelBytes = [long]$width * $height * 3
    Require49 ($offset + $pixelBytes -eq $bytes.Length) "PPM length does not match its header: $path"
    [pscustomobject]@{ Path = $path; Bytes = $bytes; Width = $width; Height = $height; PixelOffset = $offset }
}

function Get-PpmRgb49($image, [int]$x, [int]$y) {
    Require49 ($x -ge 0 -and $y -ge 0 -and $x -lt $image.Width -and $y -lt $image.Height) "PPM coordinate out of bounds: $x,$y"
    $index = $image.PixelOffset + (($y * $image.Width + $x) * 3)
    '{0:X2}{1:X2}{2:X2}' -f $image.Bytes[$index], $image.Bytes[$index + 1], $image.Bytes[$index + 2]
}

function Find-PpmColor49($image, [string]$color) {
    $red = [Convert]::ToByte($color.Substring(0, 2), 16)
    $green = [Convert]::ToByte($color.Substring(2, 2), 16)
    $blue = [Convert]::ToByte($color.Substring(4, 2), 16)
    for ($y = 0; $y -lt $image.Height; ++$y) {
        for ($x = 0; $x -lt $image.Width; ++$x) {
            $index = $image.PixelOffset + (($y * $image.Width + $x) * 3)
            if ($image.Bytes[$index] -eq $red -and $image.Bytes[$index + 1] -eq $green -and $image.Bytes[$index + 2] -eq $blue) {
                return [pscustomobject]@{ X = $x; Y = $y; Color = $color }
            }
        }
    }
    return $null
}

function Get-Hex49([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    Require49 $match.Success "Missing $name in serial evidence."
    [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

$required = @(
    'GXOS_NET10:GOP_ACQUIRED_BEFORE_EXIT_BOOT_SERVICES=1',
    'GXOS_NET10:MANAGED_KERNEL_FRAMEBUFFER_DESCRIPTOR_ACCEPTED',
    'GXOS_NET10:MANAGED_KERNEL_FRAMEBUFFER_INSTALL_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE49_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE49_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_BEGIN',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_FONT_VALIDATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_PAINT_VERIFIED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_RASTER_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_RESOURCE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_RESOURCE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_SOURCE_UNCHANGED=1',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_GOP_PRESENT_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE49_VISIBLE_PAGE_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE49_PASS')

$serialLogs = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse | Sort-Object FullName)
Require49 ($serialLogs.Count -eq $RunCount) "Expected $RunCount Phase 49 serial logs, found $($serialLogs.Count)."
$reports = @()
$screenPixelReference = $null
$screenHashes = @()
$screenPixelHashes = @()
$sampleReport = @()
$colorReport = @()
foreach ($serial in $serialLogs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) { Require49 $text.Contains($marker) "Phase 49 boot missing '$marker': $($serial.FullName)" }
    Require49 (!$text.Contains('GXOS_NET10:FAIL:') -and
               !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) "Phase 49 boot faulted: $($serial.FullName)"
    $format = Get-Hex49 $text 'GXOS_NET10:MANAGED_KERNEL_FRAMEBUFFER_FORMAT'
    $width = Get-Hex49 $text 'GXOS_NET10:MANAGED_KERNEL_FRAMEBUFFER_WIDTH'
    $height = Get-Hex49 $text 'GXOS_NET10:MANAGED_KERNEL_FRAMEBUFFER_HEIGHT'
    $stride = Get-Hex49 $text 'GXOS_NET10:MANAGED_KERNEL_FRAMEBUFFER_STRIDE'
    $size = Get-Hex49 $text 'GXOS_NET10:MANAGED_KERNEL_FRAMEBUFFER_SIZE'
    Require49 ($width -gt 0 -and $height -gt 0 -and $stride -ge $width -and $size -ge ($stride * $height * 4)) "Invalid physical framebuffer telemetry: $($serial.FullName)"
    $presentationX = [int](Get-Hex49 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE49_PRESENTATION_X')
    $presentationY = [int](Get-Hex49 $text 'GXOS_NET10:MANAGED_HTTPS_PHASE49_PRESENTATION_Y')
    $sourceXs = @([regex]::Matches($text, 'MANAGED_HTTPS_PHASE48_RASTER_PIXEL_X=0x([0-9A-Fa-f]+)') | ForEach-Object { [int]([Convert]::ToUInt64($_.Groups[1].Value, 16)) })
    $sourceYs = @([regex]::Matches($text, 'MANAGED_HTTPS_PHASE48_RASTER_PIXEL_Y=0x([0-9A-Fa-f]+)') | ForEach-Object { [int]([Convert]::ToUInt64($_.Groups[1].Value, 16)) })
    $sourceColors = @([regex]::Matches($text, 'MANAGED_HTTPS_PHASE48_RASTER_PIXEL_COLOR=0x([0-9A-Fa-f]+)') | ForEach-Object { [uint64]([Convert]::ToUInt64($_.Groups[1].Value, 16)) })
    Require49 ($sourceXs.Count -ge 12 -and $sourceYs.Count -eq $sourceXs.Count -and $sourceColors.Count -eq $sourceXs.Count) "Insufficient raster sample telemetry: $($serial.FullName)"
    $screenPath = Join-Path (Split-Path -Parent $serial.FullName) 'qemu-screen.ppm'
    Require49 (Test-Path -LiteralPath $screenPath) "Missing QEMU screen capture: $screenPath"
    $image = Read-Ppm49 $screenPath
    $raw = [IO.File]::ReadAllBytes($screenPath)
    $rawHash = Hash49 $raw
    $pixelData = New-Object byte[] ($image.Width * $image.Height * 3)
    [Array]::Copy($image.Bytes, $image.PixelOffset, $pixelData, 0, $pixelData.Length)
    $pixelHash = Hash49 $pixelData
    $screenHashes += $rawHash
    $screenPixelHashes += $pixelHash
    if ($null -eq $screenPixelReference) { $screenPixelReference = $pixelData }
    else {
        Require49 ($screenPixelReference.Length -eq $pixelData.Length) 'QEMU screen dimensions changed between boots.'
        for ($index = 0; $index -lt $pixelData.Length; ++$index) { Require49 ($screenPixelReference[$index] -eq $pixelData[$index]) "QEMU screen pixels changed at byte $index." }
    }
    $sampleLines = @()
    for ($index = 0; $index -lt 12; ++$index) {
        $sx = $sourceXs[$index]; $sy = $sourceYs[$index]
        $px = $presentationX + $sx; $py = $presentationY + $sy
        $expected = ('{0:X6}' -f ($sourceColors[$index] -band 0xFFFFFF))
        $actual = Get-PpmRgb49 $image $px $py
        Require49 ($actual -eq $expected) "Mapped raster sample mismatch at source $sx,$sy -> $($px),$($py): expected $expected got $actual"
        $sampleLines += "source=($sx,$sy) physical=($px,$py) screenshot=$actual"
    }
    $sampleReport += "serial=$($serial.FullName)"; $sampleReport += $sampleLines
    foreach ($color in @('FF0000','00FF00','0000FF','FFFFFF','000000','123456','8385C7','101820')) {
        $found = Find-PpmColor49 $image $color
        Require49 ($null -ne $found) "QEMU screenshot did not contain exact proof color $($color): $screenPath"
        $colorReport += "serial=$($serial.FullName) color=$color coordinate=($($found.X),$($found.Y))"
    }
    $reports += "serial=$($serial.FullName) raw_sha256=$rawHash pixel_sha256=$pixelHash ppm_dimensions=$($image.Width)x$($image.Height) physical_format=0x$('{0:X}' -f $format) physical_size=0x$('{0:X}' -f $size) physical_stride=0x$('{0:X}' -f $stride)"
}

$summary = @(
    'MANAGED_KERNEL_PHASE49_VISIBLE_PAGE_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE49_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE49_DECODED_RESOURCE_LENGTH=$resourceLength",
    "MANAGED_KERNEL_PHASE49_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE49_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE49_PAYLOAD_SIZE=$payloadSize",
    'MANAGED_KERNEL_PHASE49_SCREEN_FORMAT=P6_PPM',
    $reports,
    'MANAGED_KERNEL_PHASE49_MAPPED_SAMPLES=',
    $sampleReport,
    'MANAGED_KERNEL_PHASE49_EXACT_COLOR_SAMPLES=',
    $colorReport)
Set-Content -LiteralPath (Join-Path $output 'phase49-summary.log') -Value $summary -Encoding ascii
Set-Content -LiteralPath (Join-Path $output 'phase49-mapped-samples.log') -Value $sampleReport -Encoding ascii
Set-Content -LiteralPath (Join-Path $output 'phase49-color-samples.log') -Value $colorReport -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE49_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE49_RESOURCE_SHA256=$resourceSha256"
Write-Output "MANAGED_KERNEL_PHASE49_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE49_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE49_SCREEN_HASH=$($screenHashes[0])"
Write-Output "MANAGED_KERNEL_PHASE49_SCREEN_PIXEL_HASH=$($screenPixelHashes[0])"
Write-Output "MANAGED_KERNEL_PHASE49_SCREEN_RUNS=$RunCount"

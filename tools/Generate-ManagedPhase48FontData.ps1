[CmdletBinding()]
param(
    [string]$SourceDirectory = 'D:\dev\guideXOSServer_NAVIGATOR_IMPROVEMENTS\assets\Fonts\roboto',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $root 'src\ManagedKernel\ManagedPhase48GeneratedFontData.g.cs'
}
$source = [IO.Path]::GetFullPath($SourceDirectory)
$output = [IO.Path]::GetFullPath($OutputPath)

$asciiWidth = 260
$asciiHeight = 160
$extendedWidth = 260
$extendedHeight = 180
$cellWidth = 20
$cellHeight = 20
$latin1CodePoints = @(0xA0..0xFF)
$sparseCodePoints = @(0x2013, 0x2014, 0x2018, 0x2019, 0x201C,
                      0x201D, 0x2022, 0x2026, 0x20AC, 0x2122)
$extendedCodePoints = @($latin1CodePoints + $sparseCodePoints)
$uniqueCodePoints = @($extendedCodePoints | Sort-Object -Unique)
if ($uniqueCodePoints.Count -ne $extendedCodePoints.Count) {
    throw 'Extended codepoint list contains duplicates.'
}
if ($extendedCodePoints.Count -gt [int](($extendedWidth / $cellWidth) * ($extendedHeight / $cellHeight))) {
    throw 'Extended codepoint list does not fit in the fixed atlas page.'
}

$faces = @(
    @{ Name = 'Roboto9Regular'; File = 'roboto_9pt_regular.png'; Ttf = 'Roboto-Regular.ttf'; Size = 9; Baseline = 12; Ascent = 8; Descent = 4; LineGap = 4; FontStyle = [Drawing.FontStyle]::Regular },
    @{ Name = 'Roboto9Bold'; File = 'roboto_9pt_bold.png'; Ttf = 'Roboto-Bold.ttf'; Size = 9; Baseline = 12; Ascent = 8; Descent = 4; LineGap = 4; FontStyle = [Drawing.FontStyle]::Bold },
    @{ Name = 'Roboto9Italic'; File = 'roboto_9pt_italic.png'; Ttf = 'Roboto-Italic.ttf'; Size = 9; Baseline = 12; Ascent = 8; Descent = 4; LineGap = 4; FontStyle = [Drawing.FontStyle]::Italic },
    @{ Name = 'Roboto9BoldItalic'; File = 'roboto_9pt_bolditalic.png'; Ttf = 'Roboto-BoldItalic.ttf'; Size = 9; Baseline = 12; Ascent = 8; Descent = 4; LineGap = 4; FontStyle = ([Drawing.FontStyle]::Bold -bor [Drawing.FontStyle]::Italic) },
    @{ Name = 'Roboto12Regular'; File = 'roboto_12pt_regular.png'; Ttf = 'Roboto-Regular.ttf'; Size = 12; Baseline = 13; Ascent = 9; Descent = 5; LineGap = 4; FontStyle = [Drawing.FontStyle]::Regular },
    @{ Name = 'Roboto12Bold'; File = 'roboto_12pt_bold.png'; Ttf = 'Roboto-Bold.ttf'; Size = 12; Baseline = 13; Ascent = 9; Descent = 5; LineGap = 4; FontStyle = [Drawing.FontStyle]::Bold },
    @{ Name = 'Roboto12Italic'; File = 'roboto_12pt_italic.png'; Ttf = 'Roboto-Italic.ttf'; Size = 12; Baseline = 13; Ascent = 9; Descent = 5; LineGap = 4; FontStyle = [Drawing.FontStyle]::Italic },
    @{ Name = 'Roboto12BoldItalic'; File = 'roboto_12pt_bolditalic.png'; Size = 12; Baseline = 13; Ascent = 9; Descent = 5; LineGap = 4; FontStyle = ([Drawing.FontStyle]::Bold -bor [Drawing.FontStyle]::Italic); Ttf = 'Roboto-BoldItalic.ttf' }
)

function Format-Bytes([byte[]]$bytes) {
    return [Convert]::ToBase64String($bytes)
}

function Format-Metadata($metadata) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($item in $metadata) {
        $flag = if ($item.HasPixels) { 1 } else { 0 }
        if ($item.Extended) { $flag = $flag -bor 2 }
        $format = '        new ManagedPhase48GlyphMetadata({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}),'
        $lines.Add(($format -f $item.X, $item.Y, $item.Width, $item.Height, $item.Advance, $item.BearingX, $item.BearingY, $flag))
    }
    if ($lines.Count -ne 0) { $lines[$lines.Count - 1] = $lines[$lines.Count - 1].TrimEnd(',') }
    return $lines -join "`n"
}

function Read-PngFace($face) {
    $path = Join-Path $source $face.File
    if (-not (Test-Path -LiteralPath $path)) { throw "Font atlas not found: $path" }
    $bitmap = [Drawing.Bitmap]::new($path)
    try {
        if ($bitmap.Width -ne $asciiWidth -or $bitmap.Height -ne $asciiHeight) {
            throw "Unexpected ASCII atlas geometry for ${path}: $($bitmap.Width)x$($bitmap.Height)"
        }
        $alpha = [byte[]]::new($bitmap.Width * $bitmap.Height)
        $metadata = [Collections.Generic.List[object]]::new()
        for ($codePoint = 32; $codePoint -le 126; ++$codePoint) {
            $cell = $codePoint - 32
            $cellX = ($cell % 13) * $cellWidth
            $cellY = [int]($cell / 13) * $cellHeight
            $left = $cellWidth; $top = $cellHeight; $right = -1; $bottom = -1
            for ($y = 0; $y -lt $cellHeight; ++$y) {
                for ($x = 0; $x -lt $cellWidth; ++$x) {
                    $value = [byte]$bitmap.GetPixel($cellX + $x, $cellY + $y).A
                    $alpha[($cellY + $y) * $bitmap.Width + $cellX + $x] = $value
                    if ($value -ne 0) {
                        if ($x -lt $left) { $left = $x }
                        if ($y -lt $top) { $top = $y }
                        if ($x -gt $right) { $right = $x }
                        if ($y -gt $bottom) { $bottom = $y }
                    }
                }
            }
            if ($right -lt 0) {
                $left = 0; $top = 0; $width = 0; $height = 0
                $advance = if ($face.Size -eq 9) { 4 } else { 5 }
                $bearingX = 0; $bearingY = 0; $hasPixels = $false
            } else {
                $width = $right - $left + 1
                $height = $bottom - $top + 1
                $advance = $width + 1
                $bearingX = $left
                $bearingY = $top - $face.Baseline
                $hasPixels = $true
            }
            $metadata.Add([pscustomobject]@{ X = $cellX; Y = $cellY; Width = $width; Height = $height; Advance = $advance; BearingX = $bearingX; BearingY = $bearingY; HasPixels = $hasPixels; Extended = $false; CodePoint = $codePoint })
        }
        return [pscustomobject]@{ Alpha = $alpha; Metadata = $metadata.ToArray() }
    } finally {
        $bitmap.Dispose()
    }
}

function Read-ExtendedFace($face) {
    $ttfPath = Join-Path $source $face.Ttf
    if (-not (Test-Path -LiteralPath $ttfPath)) { throw "Roboto TTF donor not found: $ttfPath" }
    $collection = [Drawing.Text.PrivateFontCollection]::new()
    $collection.AddFontFile($ttfPath)
    if ($collection.Families.Length -ne 1) { throw "Unexpected font-family count in $ttfPath" }
    $family = $collection.Families[0]
    $font = [Drawing.Font]::new($family, [float]$face.Size, $face.FontStyle, [Drawing.GraphicsUnit]::Pixel)
    $bitmap = [Drawing.Bitmap]::new($extendedWidth, $extendedHeight, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $format = [Drawing.StringFormat]::GenericTypographic
    $format.FormatFlags = $format.FormatFlags -bor [Drawing.StringFormatFlags]::MeasureTrailingSpaces
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear([Drawing.Color]::Transparent)
        $alpha = [byte[]]::new($extendedWidth * $extendedHeight)
        $metadata = [Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $extendedCodePoints.Count; ++$index) {
            $codePoint = [int]$extendedCodePoints[$index]
            $cellX = ($index % 13) * $cellWidth
            $cellY = [int]($index / 13) * $cellHeight
            $text = [char]$codePoint
            $graphics.DrawString($text, $font, [Drawing.Brushes]::White,
                [Drawing.PointF]::new($cellX, $cellY), $format)
            $left = $cellWidth; $top = $cellHeight; $right = -1; $bottom = -1
            for ($y = 0; $y -lt $cellHeight; ++$y) {
                for ($x = 0; $x -lt $cellWidth; ++$x) {
                    $value = [byte]$bitmap.GetPixel($cellX + $x, $cellY + $y).A
                    $alpha[($cellY + $y) * $extendedWidth + $cellX + $x] = $value
                    if ($value -ne 0) {
                        if ($x -lt $left) { $left = $x }
                        if ($y -lt $top) { $top = $y }
                        if ($x -gt $right) { $right = $x }
                        if ($y -gt $bottom) { $bottom = $y }
                    }
                }
            }
            $measure = $graphics.MeasureString($text, $font, [Drawing.PointF]::new(0, 0), $format)
            $advance = [int][Math]::Round($measure.Width, [MidpointRounding]::AwayFromZero)
            if ($advance -le 0) { $advance = if ($codePoint -eq 0xA0) { [Math]::Max(1, [int]($face.Size / 2)) } else { 1 } }
            if ($right -lt 0) {
                if ($codePoint -ne 0xA0 -and $codePoint -ne 0xAD) {
                    throw ('Roboto donor did not render required U+{0:X4} in {1}.' -f $codePoint, $face.Name)
                }
                $left = 0; $top = 0; $width = 0; $height = 0
                $bearingX = 0; $bearingY = 0; $hasPixels = $false
            } else {
                $width = $right - $left + 1
                $height = $bottom - $top + 1
                $bearingX = $left
                $bearingY = $top - $face.Baseline
                $hasPixels = $true
            }
            if ($width -gt $cellWidth -or $height -gt $cellHeight) {
                throw ('Required U+{0:X4} exceeds the fixed {1}x{2} cell in {3}.' -f $codePoint, $cellWidth, $cellHeight, $face.Name)
            }
            $metadata.Add([pscustomobject]@{ X = $cellX; Y = $cellY; Width = $width; Height = $height; Advance = $advance; BearingX = $bearingX; BearingY = $bearingY; HasPixels = $hasPixels; Extended = $true; CodePoint = $codePoint })
        }
        return [pscustomobject]@{ Alpha = $alpha; Metadata = $metadata.ToArray() }
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $font.Dispose()
        $collection.Dispose()
    }
}

$sourceHashes = [Collections.Generic.List[string]]::new()
$generated = [Text.StringBuilder]::new()
[void]$generated.AppendLine('// <auto-generated />')
[void]$generated.AppendLine('// Generated by tools/Generate-ManagedPhase48FontData.ps1 from the guideXOS Navigator Roboto bitmap/TTF donors.')
[void]$generated.AppendLine('// Guest code consumes only these static alpha bytes; System.Drawing is build-time tooling only.')
[void]$generated.AppendLine('using System;')
[void]$generated.AppendLine()
[void]$generated.AppendLine('namespace GuideXOS.Net10.ManagedKernel;')
[void]$generated.AppendLine()
[void]$generated.AppendLine('internal static class ManagedPhase48GeneratedFontData')
[void]$generated.AppendLine('{')
[void]$generated.AppendLine(('    internal const int AsciiAtlasWidth = {0};' -f $asciiWidth))
[void]$generated.AppendLine(('    internal const int AsciiAtlasHeight = {0};' -f $asciiHeight))
[void]$generated.AppendLine(('    internal const int ExtendedAtlasWidth = {0};' -f $extendedWidth))
[void]$generated.AppendLine(('    internal const int ExtendedAtlasHeight = {0};' -f $extendedHeight))
[void]$generated.AppendLine('    internal static readonly uint[] ExtendedCodePoints = new uint[]')
[void]$generated.AppendLine('    {')
for ($index = 0; $index -lt $extendedCodePoints.Count; ++$index) {
    $suffix = if ($index -eq $extendedCodePoints.Count - 1) { '' } else { ',' }
    [void]$generated.AppendLine(('        0x{0:X4}U{1}' -f $extendedCodePoints[$index], $suffix))
}
[void]$generated.AppendLine('    };')
[void]$generated.AppendLine()

foreach ($face in $faces) {
    $pngPath = Join-Path $source $face.File
    $ttfPath = Join-Path $source $face.Ttf
    $pngHash = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $ttfHash = (Get-FileHash -LiteralPath $ttfPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $sourceHashes.Add("$($face.Name)Png=$pngHash")
    $sourceHashes.Add("$($face.Name)Ttf=$ttfHash")
    $ascii = Read-PngFace $face
    $extended = Read-ExtendedFace $face
    [void]$generated.AppendLine(('    internal static readonly byte[] {0}Alpha = Convert.FromBase64String("{1}");' -f $face.Name, (Format-Bytes $ascii.Alpha)))
    [void]$generated.AppendLine(('    internal static readonly byte[] {0}ExtendedAlpha = Convert.FromBase64String("{1}");' -f $face.Name, (Format-Bytes $extended.Alpha)))
    [void]$generated.AppendLine()
    [void]$generated.AppendLine("    internal static readonly ManagedPhase48GlyphMetadata[] $($face.Name)Glyphs = new ManagedPhase48GlyphMetadata[]")
    [void]$generated.AppendLine('    {')
    [void]$generated.AppendLine((Format-Metadata $ascii.Metadata))
    [void]$generated.AppendLine('    };')
    [void]$generated.AppendLine()
    [void]$generated.AppendLine("    internal static readonly ManagedPhase48GlyphMetadata[] $($face.Name)ExtendedGlyphs = new ManagedPhase48GlyphMetadata[]")
    [void]$generated.AppendLine('    {')
    [void]$generated.AppendLine((Format-Metadata $extended.Metadata))
    [void]$generated.AppendLine('    };')
    [void]$generated.AppendLine()
}

[void]$generated.AppendLine('    internal static readonly string[] SourceHashes = new string[]')
[void]$generated.AppendLine('    {')
foreach ($hash in $sourceHashes) { [void]$generated.AppendLine(('        "{0}",' -f $hash)) }
[void]$generated.AppendLine('    };')
[void]$generated.AppendLine('}')

$parent = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $parent | Out-Null
[IO.File]::WriteAllText($output, $generated.ToString(), [Text.UTF8Encoding]::new($false))
Write-Output "GENERATED_MANAGED_PHASE50_FONT_DATA=$output"
Write-Output "MANAGED_PHASE50_FONT_SOURCE_HASHES=$($sourceHashes -join ';')"
Write-Output "MANAGED_PHASE50_FONT_EXTENDED_GLYPHS_PER_FACE=$($extendedCodePoints.Count)"

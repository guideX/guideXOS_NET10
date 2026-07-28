[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [string]$OutputPath = '',
    [string]$MapPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function U16([byte[]]$b, [int]$o) { [BitConverter]::ToUInt16($b, $o) }
function U32([byte[]]$b, [int]$o) { [BitConverter]::ToUInt32($b, $o) }
function U64([byte[]]$b, [int]$o) { [BitConverter]::ToUInt64($b, $o) }
function Hex([UInt64]$v) { '0x{0:X}' -f $v }
function Z([byte[]]$b, [int]$o) {
    $e = $o
    while ($e -lt $b.Length -and $b[$e] -ne 0) { $e++ }
    [Text.Encoding]::ASCII.GetString($b, $o, $e - $o)
}

$path = [IO.Path]::GetFullPath($InputPath)
$b = [IO.File]::ReadAllBytes($path)
if ($b.Length -lt 64 -or (U16 $b 0) -ne 0x5A4D) { throw 'Not a DOS PE image.' }
$nt = U32 $b 0x3C
if ($nt + 24 -gt $b.Length -or (U32 $b $nt) -ne 0x4550) { throw 'Invalid PE signature.' }
$fh = $nt + 4
$machine = U16 $b $fh
$sectionCount = U16 $b ($fh + 2)
$timestamp = U32 $b ($fh + 4)
$symbolOffset = U32 $b ($fh + 8)
$symbolCount = U32 $b ($fh + 12)
$optionalSize = U16 $b ($fh + 16)
$op = $fh + 20
if ((U16 $b $op) -ne 0x20B) { throw 'Only PE32+ is supported.' }
$entry = U32 $b ($op + 16)
$imageBase = U64 $b ($op + 24)
$sectionAlignment = U32 $b ($op + 32)
$fileAlignment = U32 $b ($op + 36)
$imageSize = U32 $b ($op + 56)
$headerSize = U32 $b ($op + 60)
$subsystem = U16 $b ($op + 68)
$dllCharacteristics = U16 $b ($op + 70)
$directoryCount = U32 $b ($op + 108)

$sections = @()
$sectionBase = $op + $optionalSize
for ($i = 0; $i -lt $sectionCount; $i++) {
    $o = $sectionBase + 40 * $i
    $chars = U32 $b ($o + 36)
    $perms = @()
    if (($chars -band 0x20000000) -ne 0) { $perms += 'execute' }
    if (($chars -band 0x40000000) -ne 0) { $perms += 'read' }
    if (($chars -band 0x80000000) -ne 0) { $perms += 'write' }
    $sections += [ordered]@{
        name = ([Text.Encoding]::ASCII.GetString($b, $o, 8).TrimEnd([char]0))
        virtual_size = (U32 $b ($o + 8)); virtual_address = (U32 $b ($o + 12))
        raw_size = (U32 $b ($o + 16)); raw_offset = (U32 $b ($o + 20))
        characteristics = (Hex $chars); permissions = $perms
    }
}
function RvaOffset([UInt32]$r, [UInt32]$size = 1) {
    if ([UInt64]$r + $size -le $headerSize) { return [int]$r }
    foreach ($s in $sections) {
        if ($r -ge $s.virtual_address -and [UInt64]$r + $size -le [UInt64]$s.virtual_address + $s.raw_size) {
            $o = [UInt64]$s.raw_offset + $r - $s.virtual_address
            if ($o + $size -le $b.Length) { return [int]$o }
        }
    }
    return -1
}

$dirNames = @('export','import','resource','exception','security','basereloc','debug','architecture','globalptr','tls','loadconfig','boundimport','iat','delayimport','clr','reserved')
$dirs = @()
for ($i = 0; $i -lt 16; $i++) {
    $o = $op + 112 + 8 * $i
    $dirs += [ordered]@{ index = $i; name = $dirNames[$i]; rva = (U32 $b $o); size = (U32 $b ($o + 4)) }
}

$imports = @()
$id = $dirs[1]
$io = RvaOffset $id.rva $id.size
if ($io -ge 0) {
    for ($c = 0; $c + 20 -le $id.size; $c += 20) {
        $o = $io + $c; $lookup = U32 $b $o; $nameRva = U32 $b ($o + 12); $first = U32 $b ($o + 16)
        if ($lookup -eq 0 -and $nameRva -eq 0 -and $first -eq 0) { break }
        $no = RvaOffset $nameRva; if ($no -lt 0) { throw 'Import name is outside the image.' }
        $thunkRva = if ($lookup -ne 0) { $lookup } else { $first }
        $symbols = @()
        for ($j = 0; $j -lt 16384; $j++) {
            $to = RvaOffset ([UInt32]($thunkRva + 8 * $j)) 8
            if ($to -lt 0) { throw 'Import thunk is outside the image.' }
            $thunk = U64 $b $to; if ($thunk -eq 0) { break }
            if (($thunk -band 0x8000000000000000) -ne 0) { $symbols += ('ordinal:{0}' -f ($thunk -band 0xFFFF)) }
            else { $ho = RvaOffset ([UInt32]$thunk) 2; $symbols += (Z $b ($ho + 2)) }
        }
        $imports += [ordered]@{ dll = (Z $b $no); symbols = $symbols }
    }
}

$exports = @()
$ed = $dirs[0]; $eo = RvaOffset $ed.rva $ed.size
if ($eo -ge 0 -and $ed.size -ge 40) {
    $names = U32 $b ($eo + 32); $ords = U32 $b ($eo + 36); $funcs = U32 $b ($eo + 28); $count = U32 $b ($eo + 24)
    for ($i = 0; $i -lt $count; $i++) {
        $no = RvaOffset ([UInt32]($names + 4 * $i)) 4; $oo = RvaOffset ([UInt32]($ords + 2 * $i)) 2
        $ord = U16 $b $oo; $fo = RvaOffset ([UInt32]($funcs + 4 * $ord)) 4
        $exports += [ordered]@{ name = (Z $b (RvaOffset (U32 $b $no))); ordinal = $ord; rva = (U32 $b $fo) }
    }
}

$rd = $dirs[5]; $ro = RvaOffset $rd.rva $rd.size; $relocBlocks = 0; $relocEntries = 0; $relocTypes = @{}
if ($ro -ge 0) {
    for ($c = 0; $c + 8 -le $rd.size;) {
        $blockSize = U32 $b ($ro + $c + 4); if ($blockSize -lt 8 -or $c + $blockSize -gt $rd.size) { throw 'Invalid relocation block.' }
        $relocBlocks++; $n = [int](($blockSize - 8) / 2)
        for ($i = 0; $i -lt $n; $i++) {
            $type = (U16 $b ($ro + $c + 8 + 2 * $i)) -shr 12; $relocEntries++
            $key = [string]$type; if (-not $relocTypes.ContainsKey($key)) { $relocTypes[$key] = 0 }; $relocTypes[$key]++
        }
        $c += $blockSize
    }
}

$map = [ordered]@{ path = ''; sha256 = ''; tokens = @() }
if (-not [string]::IsNullOrWhiteSpace($MapPath) -and (Test-Path -LiteralPath $MapPath)) {
    $mapFull = [IO.Path]::GetFullPath($MapPath); $text = [IO.File]::ReadAllText($mapFull)
    $tokens = @('ManagedMain','ModuleInitializerList','RuntimeConfigurationBlob','FieldRvaData','GCStatics','NonGCStatics','MethodExceptionHandlingInfo','ThreadStatic') | Where-Object { $text.Contains($_) }
    $map = [ordered]@{ path = $mapFull; sha256 = (Get-FileHash $mapFull -Algorithm SHA256).Hash; tokens = @($tokens) }
}

$result = [ordered]@{
    format_version = 1; input = $path; sha256 = (Get-FileHash $path -Algorithm SHA256).Hash; file_size = $b.Length
    pe = [ordered]@{ machine = (Hex $machine); pe32_plus = $true; section_count = $sectionCount; timestamp = (Hex $timestamp); symbol_table_offset = $symbolOffset; symbol_count = $symbolCount; entry_rva = $entry; image_base = (Hex $imageBase); section_alignment = $sectionAlignment; file_alignment = $fileAlignment; size_of_image = $imageSize; size_of_headers = $headerSize; subsystem = $subsystem; dll_characteristics = (Hex $dllCharacteristics) }
    sections = $sections; directories = $dirs; imports = $imports; exports = $exports
    relocations = [ordered]@{ directory_rva = $rd.rva; directory_size = $rd.size; blocks = $relocBlocks; entries = $relocEntries; types = $relocTypes }
    runtime_metadata = $map
}
$json = $result | ConvertTo-Json -Depth 16
if ([string]::IsNullOrWhiteSpace($OutputPath)) { Write-Output $json }
else { $out = [IO.Path]::GetFullPath($OutputPath); New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null; $json | Set-Content -LiteralPath $out -Encoding utf8; Write-Output "PE_MANIFEST=$out" }

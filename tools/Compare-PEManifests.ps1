[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceManifest,
    [Parameter(Mandatory = $true)][string]$StagedManifest,
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath $SourceManifest -Raw | ConvertFrom-Json
$staged = Get-Content -LiteralPath $StagedManifest -Raw | ConvertFrom-Json
$differences = @()

function Check([string]$Name, $Left, $Right)
{
    if (($Left | ConvertTo-Json -Compress -Depth 16) -ne ($Right | ConvertTo-Json -Compress -Depth 16)) {
        $script:differences += $Name
    }
}

Check 'sha256' $source.sha256 $staged.sha256
Check 'machine' $source.pe.machine $staged.pe.machine
Check 'entry_rva' $source.pe.entry_rva $staged.pe.entry_rva
Check 'image_base' $source.pe.image_base $staged.pe.image_base
Check 'section_alignment' $source.pe.section_alignment $staged.pe.section_alignment
Check 'file_alignment' $source.pe.file_alignment $staged.pe.file_alignment
Check 'size_of_image' $source.pe.size_of_image $staged.pe.size_of_image
Check 'sections' $source.sections $staged.sections
Check 'directories' $source.directories $staged.directories
Check 'imports' $source.imports $staged.imports
Check 'exports' $source.exports $staged.exports
Check 'relocations' $source.relocations $staged.relocations
$requiredSections = @('.text', '.rdata', '.data', '.pdata', '.reloc')
foreach ($required in $requiredSections) {
    if (-not @($staged.sections | Where-Object name -eq $required)) { $differences += "missing-section:$required" }
}
if (-not @($staged.exports | Where-Object name -eq 'ManagedMain')) { $differences += 'missing-export:ManagedMain' }

$result = [ordered]@{
    format_version = 1
    source_manifest = [IO.Path]::GetFullPath($SourceManifest)
    staged_manifest = [IO.Path]::GetFullPath($StagedManifest)
    source_sha256 = $source.sha256
    staged_sha256 = $staged.sha256
    differences = @($differences)
    pass = ($differences.Count -eq 0)
}
$json = $result | ConvertTo-Json -Depth 8
if ([string]::IsNullOrWhiteSpace($OutputPath)) { Write-Output $json }
else {
    $out = [IO.Path]::GetFullPath($OutputPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
    $json | Set-Content -LiteralPath $out -Encoding utf8
}
if (-not $result.pass) { throw ('PE manifest comparison failed: ' + ($differences -join ', ')) }
Write-Output 'GATE3_COMPARISON=PASS'

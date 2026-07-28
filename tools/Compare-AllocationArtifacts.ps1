[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceManifest,
    [Parameter(Mandatory = $true)][string]$StagedManifest,
    [Parameter(Mandatory = $true)][string]$SourceMap,
    [Parameter(Mandatory = $true)][string]$StagedMap,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Manifest not found: $Path" }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-ImportSet($Manifest) {
    $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($descriptor in @($Manifest.imports)) {
        foreach ($symbol in @($descriptor.symbols)) {
            [void]$set.Add("$($descriptor.dll)!$symbol")
        }
    }
    return $set
}

function Get-MapNames([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Map not found: $Path" }
    $xml = [xml](Get-Content -LiteralPath $Path -Raw)
    return @($xml.ObjectNodes.ChildNodes | Where-Object { $_.Name } | ForEach-Object { [string]$_.Name })
}

$source = Read-JsonFile $SourceManifest
$staged = Read-JsonFile $StagedManifest
$sourceImports = Get-ImportSet $source
$stagedImports = Get-ImportSet $staged
$addedImports = @($stagedImports | Where-Object { -not $sourceImports.Contains($_) } | Sort-Object)
$removedImports = @($sourceImports | Where-Object { -not $stagedImports.Contains($_) } | Sort-Object)
$sourceMapNames = Get-MapNames $SourceMap
$stagedMapNames = Get-MapNames $StagedMap
$newMapNames = @($stagedMapNames | Where-Object { $_ -notin $sourceMapNames } | Sort-Object -Unique)
$allocationMapNames = @($newMapNames | Where-Object { $_ -match 'AllocationProbeObject|ManagedEntry.*AllocateOne' })
$runtimeAllocationMapNames = @($stagedMapNames | Where-Object { $_ -match 'RhNew(Fast|Object)' } | Sort-Object -Unique)

$report = [ordered]@{
    format_version = 2
    source_manifest = [IO.Path]::GetFullPath($SourceManifest)
    staged_manifest = [IO.Path]::GetFullPath($StagedManifest)
    source_sha256 = $source.sha256
    staged_sha256 = $staged.sha256
    source_file_size = $source.file_size
    staged_file_size = $staged.file_size
    import_descriptors = [ordered]@{
        source = @($source.imports).Count
        staged = @($staged.imports).Count
    }
    import_symbols = [ordered]@{
        source = @($sourceImports).Count
        staged = @($stagedImports).Count
        added = $addedImports
        removed = $removedImports
    }
    new_map_names = $newMapNames
    allocation_map_names = $allocationMapNames
    runtime_allocation_map_names = $runtimeAllocationMapNames
    pass = ($addedImports.Count -eq 0 -and $removedImports.Count -eq 0 -and $allocationMapNames.Count -gt 0 -and $runtimeAllocationMapNames.Count -gt 0)
}

$parent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$report | ConvertTo-Json -Depth 8
if (-not $report.pass) { exit 2 }

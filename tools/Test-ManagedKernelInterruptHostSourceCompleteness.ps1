[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DotNetPath,
    [Parameter(Mandatory = $true)]
    [string]$MSBuildPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$productionProject = Join-Path $root 'src\ManagedKernel\ManagedKernel.csproj'
$hostProject = Join-Path $root 'src\ManagedKernelInterruptHostTests\ManagedKernelInterruptHostTests.csproj'

function Get-CompileItemPaths {
    param([string]$ProjectPath)
    $result = @(& $DotNetPath $MSBuildPath $ProjectPath '-getItem:Compile' 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "MSBuild could not inspect Compile items for $ProjectPath (exit $LASTEXITCODE): $($result -join ' ')"
    }
    try {
        $document = ($result -join [Environment]::NewLine) | ConvertFrom-Json
    } catch {
        throw "MSBuild returned invalid Compile-item JSON for ${ProjectPath}: $($_.Exception.Message)"
    }
    if ($null -eq $document.Items -or $null -eq $document.Items.Compile) {
        return @()
    }
    return @($document.Items.Compile | ForEach-Object { [IO.Path]::GetFullPath($_.FullPath) })
}

Push-Location (Split-Path -Parent $root)
try {
    $productionPaths = @(Get-CompileItemPaths -ProjectPath $productionProject)
    $hostPaths = @(Get-CompileItemPaths -ProjectPath $hostProject)
} finally {
    Pop-Location
}

$productionDuplicates = @($productionPaths | Group-Object | Where-Object { $_.Count -gt 1 })
$hostDuplicates = @($hostPaths | Group-Object | Where-Object { $_.Count -gt 1 })
if ($productionDuplicates.Count -gt 0) {
    throw "The authoritative ManagedKernel project includes duplicate Compile items: $($productionDuplicates.Name -join ', ')"
}
if ($hostDuplicates.Count -gt 0) {
    throw "The interrupt host project includes duplicate Compile items: $($hostDuplicates.Name -join ', ')"
}

$productionSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in $productionPaths) { [void]$productionSet.Add($path) }
$hostSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in $hostPaths) { [void]$hostSet.Add($path) }
$missing = @($productionPaths | Where-Object { -not $hostSet.Contains($_) })
$programPath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $hostProject) 'Program.cs'))
$unexpected = @($hostPaths | Where-Object { -not $productionSet.Contains($_) -and $_ -ne $programPath })
if ($missing.Count -gt 0) {
    throw "The interrupt host project omits authoritative ManagedKernel Compile items: $($missing -join ', ')"
}
if ($unexpected.Count -gt 0) {
    throw "The interrupt host project compiles unexpected source items: $($unexpected -join ', ')"
}

Write-Output "MANAGED_KERNEL_INTERRUPT_SOURCE_COMPLETENESS=PASSED production_sources=$($productionPaths.Count) host_compile_items=$($hostPaths.Count) duplicates=0"

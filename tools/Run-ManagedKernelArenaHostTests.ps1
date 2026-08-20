[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-arena-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $root 'src\ManagedKernelArenaHostTests\ManagedKernelArenaHostTests.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
$sdkDirectory = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk\10.0.400'
$msbuild = Join-Path $sdkDirectory 'MSBuild.dll'
if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "The installed .NET 10.0.400 MSBuild entry point is missing: $msbuild"
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
$parent = Split-Path -Parent $root
Push-Location $parent
try {
    $restore = @($msbuild, $project, '/t:Restore', '/p:Configuration=Release')
    & $dotnet.Source @restore 1> (Join-Path $out 'restore.stdout.log') `
        2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Arena host-test restore failed: $LASTEXITCODE" }
    $build = @($msbuild, $project, '/t:Build', '/p:Configuration=Release',
               "/p:OutputPath=$out\bin\")
    & $dotnet.Source @build 1> (Join-Path $out 'build.stdout.log') `
        2> (Join-Path $out 'build.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Arena host-test build failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
$assembly = Join-Path $out 'bin\ManagedKernelArenaHostTests.dll'
if (-not (Test-Path -LiteralPath $assembly)) {
    throw "Arena host-test assembly was not emitted: $assembly"
}
& $dotnet.Source $assembly 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Arena host tests failed: $LASTEXITCODE" }
Get-Content (Join-Path $out 'run.stdout.log')

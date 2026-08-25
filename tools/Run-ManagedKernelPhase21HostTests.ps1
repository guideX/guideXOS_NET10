[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ManagedKernelPhase21HostTests\ManagedKernelPhase21HostTests.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
$msbuild = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk\10.0.400\MSBuild.dll'
if (!(Test-Path -LiteralPath $msbuild)) {
    throw "The installed .NET 10.0.400 MSBuild entry point is missing: $msbuild"
}
$previousDotnetHostPath = $env:DOTNET_HOST_PATH
try {
    $env:DOTNET_HOST_PATH = $dotnet.Source
    & $dotnet.Source $msbuild $project '/t:Restore' '/p:Configuration=Release'
    if ($LASTEXITCODE -ne 0) { throw "Phase 21 host-test restore failed: $LASTEXITCODE" }
    & $dotnet.Source $msbuild $project '/t:Build' '/p:Configuration=Release' '/p:NoRestore=true'
    if ($LASTEXITCODE -ne 0) { throw "Phase 21 host-test build failed: $LASTEXITCODE" }
} finally {
    $env:DOTNET_HOST_PATH = $previousDotnetHostPath
}
$exe = Join-Path $root 'src\ManagedKernelPhase21HostTests\bin\Release\net10.0\ManagedKernelPhase21HostTests.exe'
if (!(Test-Path -LiteralPath $exe)) {
    $exe = Join-Path $root 'src\ManagedKernelPhase21HostTests\bin\Release\net10.0\ManagedKernelPhase21HostTests.dll'
}
& $exe
if ($LASTEXITCODE -ne 0) { throw "Phase 21 host tests failed: $LASTEXITCODE" }

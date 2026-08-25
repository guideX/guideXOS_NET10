[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\ManagedKernelPhase19HostTests\ManagedKernelPhase19HostTests.csproj'
$out = Join-Path $root 'artifacts\managed-kernel-phase19-host-tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$dotnet = Get-Command dotnet -ErrorAction Stop
$msbuild = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk\10.0.400\MSBuild.dll'
if (!(Test-Path -LiteralPath $msbuild)) {
    throw "The installed .NET 10.0.400 MSBuild entry point is missing: $msbuild"
}
$parent = Split-Path -Parent $root
Push-Location $parent
$previousDotnetHostPath = $env:DOTNET_HOST_PATH
try {
    $env:DOTNET_HOST_PATH = $dotnet.Source
    & $dotnet.Source $msbuild $project '/t:Restore' '/p:Configuration=Release'
    if ($LASTEXITCODE -ne 0) { throw "Phase 19 host-test restore failed: $LASTEXITCODE" }
    & $dotnet.Source $msbuild $project '/t:Build' '/p:Configuration=Release' '/p:NoRestore=true'
    if ($LASTEXITCODE -ne 0) { throw "Phase 19 host-test build failed: $LASTEXITCODE" }
} finally {
    $env:DOTNET_HOST_PATH = $previousDotnetHostPath
    Pop-Location
}
$exe = Join-Path $root 'src\ManagedKernelPhase19HostTests\bin\Release\net10.0\ManagedKernelPhase19HostTests.exe'
if (!(Test-Path -LiteralPath $exe)) {
    $exe = Join-Path $root 'src\ManagedKernelPhase19HostTests\bin\Release\net10.0\ManagedKernelPhase19HostTests.dll'
}
& $exe
if ($LASTEXITCODE -ne 0) { throw "Phase 19 host tests failed: $LASTEXITCODE" }

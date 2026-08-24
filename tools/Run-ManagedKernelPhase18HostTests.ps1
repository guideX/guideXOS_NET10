[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out = Join-Path $root 'artifacts\managed-kernel-phase18-host-tests'
$project = Join-Path $root 'src\ManagedKernelPhase18HostTests\ManagedKernelPhase18HostTests.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
$sdkDirectory = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk\10.0.400'
$msbuild = Join-Path $sdkDirectory 'MSBuild.dll'
if (-not (Test-Path -LiteralPath $msbuild)) { throw "Missing MSBuild: $msbuild" }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$parent = Split-Path -Parent $root
Push-Location $parent
try {
    & $dotnet.Source $msbuild $project '/t:Restore' '/p:Configuration=Release' `
        1> (Join-Path $out 'restore.stdout.log') 2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 18 host-test restore failed: $LASTEXITCODE" }
    & $dotnet.Source $msbuild $project '/t:Build' '/p:Configuration=Release' `
        "/p:OutputPath=$out\bin\" 1> (Join-Path $out 'build.stdout.log') `
        2> (Join-Path $out 'build.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 18 host-test build failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
$assembly = Join-Path $out 'bin\ManagedKernelPhase18HostTests.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw "Missing host-test assembly: $assembly" }
& $dotnet.Source $assembly 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Phase 18 host tests failed: $LASTEXITCODE" }
Get-Content (Join-Path $out 'run.stdout.log')

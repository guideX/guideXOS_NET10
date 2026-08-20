[CmdletBinding()]
param([string]$OutputDirectory = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-driver-binding-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $root 'src\ManagedKernelDriverBindingHostTests\ManagedKernelDriverBindingHostTests.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
$sdkDirectory = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk\10.0.400'
$msbuild = Join-Path $sdkDirectory 'MSBuild.dll'
if (-not (Test-Path -LiteralPath $msbuild)) { throw "MSBuild entry point missing: $msbuild" }
New-Item -ItemType Directory -Force -Path $out | Out-Null
Push-Location (Split-Path -Parent $root)
try {
    & $dotnet.Source $msbuild $project '/t:Restore' '/p:Configuration=Release' `
        1> (Join-Path $out 'restore.stdout.log') 2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "driver binding host restore failed: $LASTEXITCODE" }
    & $dotnet.Source $msbuild $project '/t:Build' '/p:Configuration=Release' `
        "/p:OutputPath=$out\bin\" 1> (Join-Path $out 'build.stdout.log') `
        2> (Join-Path $out 'build.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "driver binding host build failed: $LASTEXITCODE" }
} finally { Pop-Location }
$assembly = Join-Path $out 'bin\ManagedKernelDriverBindingHostTests.dll'
& $dotnet.Source $assembly 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "driver binding host tests failed: $LASTEXITCODE" }
Get-Content (Join-Path $out 'run.stdout.log')

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
. (Join-Path $PSScriptRoot 'Resolve-ManagedKernelHostToolchain.ps1')
$toolchain = Resolve-ManagedKernelHostToolchain -DotNetPath $dotnet.Source
Write-Output "MANAGED_KERNEL_DRIVER_BINDING_DOTNET_SDK=$($toolchain.SdkVersion) path=$($toolchain.SdkDirectory) selection=$($toolchain.SelectionSource)"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Push-Location (Split-Path -Parent $root)
try {
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Restore' '/p:Configuration=Release' `
        1> (Join-Path $out 'restore.stdout.log') 2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "driver binding host restore failed: $LASTEXITCODE" }
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Build' '/p:Configuration=Release' `
        "/p:OutputPath=$out\bin\" 1> (Join-Path $out 'build.stdout.log') `
        2> (Join-Path $out 'build.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "driver binding host build failed: $LASTEXITCODE" }
} finally { Pop-Location }
$assembly = Join-Path $out 'bin\ManagedKernelDriverBindingHostTests.dll'
& $toolchain.DotNetPath $assembly 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "driver binding host tests failed: $LASTEXITCODE" }
Get-Content (Join-Path $out 'run.stdout.log')

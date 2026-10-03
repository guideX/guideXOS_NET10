[CmdletBinding()]
param([string]$SdkDirectory = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$out = Join-Path $root 'artifacts\managed-kernel-phase14-host-tests'
$project = Join-Path $root 'src\ManagedKernelPhase14HostTests\ManagedKernelPhase14HostTests.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
. (Join-Path $PSScriptRoot 'Resolve-ManagedKernelHostToolchain.ps1')
$toolchain = Resolve-ManagedKernelHostToolchain `
    -DotNetPath $dotnet.Source -SdkDirectoryOverride $SdkDirectory
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Output "MANAGED_KERNEL_PHASE14_HOST_DOTNET_SDK=$($toolchain.SdkVersion) path=$($toolchain.SdkDirectory) selection=$($toolchain.SelectionSource)"

$parent = Split-Path -Parent $root
Push-Location $parent
try {
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Restore' '/p:Configuration=Release' `
        1> (Join-Path $out 'restore.stdout.log') 2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 14 host-test restore failed: $LASTEXITCODE" }
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Build' '/p:Configuration=Release' `
        "/p:OutputPath=$out\bin\" 1> (Join-Path $out 'build.stdout.log') `
        2> (Join-Path $out 'build.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 14 host-test build failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
$assembly = Join-Path $out 'bin\ManagedKernelPhase14HostTests.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw "Missing host-test assembly: $assembly" }
& $toolchain.DotNetPath $assembly 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Phase 14 host tests failed: $LASTEXITCODE" }
Get-Content (Join-Path $out 'run.stdout.log')

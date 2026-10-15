[CmdletBinding()]
param(
    [string]$SdkDirectory = '',
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-phase92-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $root 'src\ManagedKernelPhase92HostTests\ManagedKernelPhase92HostTests.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
. (Join-Path $PSScriptRoot 'Resolve-ManagedKernelHostToolchain.ps1')
$toolchain = Resolve-ManagedKernelHostToolchain `
    -DotNetPath $dotnet.Source -SdkDirectoryOverride $SdkDirectory
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Output "MANAGED_KERNEL_PHASE92_HOST_DOTNET_SDK=$($toolchain.SdkVersion) path=$($toolchain.SdkDirectory) selection=$($toolchain.SelectionSource)"

$parent = Split-Path -Parent $root
Push-Location $parent
try {
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Restore' '/p:Configuration=Release' `
        1> (Join-Path $out 'restore.stdout.log') 2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 92 host-test restore failed: $LASTEXITCODE" }
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Build' '/p:Configuration=Release' `
        "/p:OutputPath=$out\bin\" 1> (Join-Path $out 'build.stdout.log') `
        2> (Join-Path $out 'build.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 92 host-test build failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
$assembly = Join-Path $out 'bin\ManagedKernelPhase92HostTests.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw "Missing host-test assembly: $assembly" }
& $toolchain.DotNetPath $assembly 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Phase 92 host tests failed: $LASTEXITCODE" }
Get-Content (Join-Path $out 'run.stdout.log')

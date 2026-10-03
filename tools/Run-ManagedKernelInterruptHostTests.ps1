[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [string]$SdkDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-interrupt-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $root 'src\ManagedKernelInterruptHostTests\ManagedKernelInterruptHostTests.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
. (Join-Path $PSScriptRoot 'Resolve-ManagedKernelHostToolchain.ps1')
& (Join-Path $PSScriptRoot 'Test-ManagedKernelInterruptToolchainResolution.ps1')
$toolchain = Resolve-ManagedKernelHostToolchain `
    -DotNetPath $dotnet.Source -SdkDirectoryOverride $SdkDirectory
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Output "MANAGED_KERNEL_INTERRUPT_DOTNET=$($toolchain.DotNetPath)"
Write-Output "MANAGED_KERNEL_INTERRUPT_MSBUILD=$($toolchain.MSBuildPath)"
Write-Output "MANAGED_KERNEL_INTERRUPT_COMPILER=$($toolchain.CompilerPath)"
Write-Output "MANAGED_KERNEL_INTERRUPT_DOTNET_SDK=$($toolchain.SdkVersion) path=$($toolchain.SdkDirectory) selection=$($toolchain.SelectionSource)"
$windowsSdkDirectory = $null
if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
    $windowsSdkIncludeRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Include'
    $windowsSdkDirectory = Get-ChildItem -LiteralPath $windowsSdkIncludeRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1
}
if ($null -ne $windowsSdkDirectory) {
    Write-Output "MANAGED_KERNEL_INTERRUPT_WINDOWS_SDK=$($windowsSdkDirectory.Name) path=$($windowsSdkDirectory.FullName) (detected; managed host build does not consume native SDK headers)"
} else {
    Write-Output 'MANAGED_KERNEL_INTERRUPT_WINDOWS_SDK=not detected (not required by this managed host build)'
}
& (Join-Path $PSScriptRoot 'Test-ManagedKernelInterruptHostSourceCompleteness.ps1') `
    -DotNetPath $toolchain.DotNetPath -MSBuildPath $toolchain.MSBuildPath
Push-Location (Split-Path -Parent $root)
try {
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Restore' '/p:Configuration=Release' `
        1> (Join-Path $out 'restore.stdout.log') 2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "interrupt host restore failed: $LASTEXITCODE (see $out\restore.stdout.log and $out\restore.stderr.log)" }
    & $toolchain.DotNetPath $toolchain.MSBuildPath $project '/t:Build' '/p:Configuration=Release' `
        "/p:OutputPath=$out\bin\" 1> (Join-Path $out 'build.stdout.log') `
        2> (Join-Path $out 'build.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "interrupt host build failed: $LASTEXITCODE (see $out\build.stdout.log and $out\build.stderr.log)" }
} finally {
    Pop-Location
}
$assembly = Join-Path $out 'bin\ManagedKernelInterruptHostTests.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw "interrupt host assembly missing: $assembly" }
& $toolchain.DotNetPath $assembly 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "ManagedKernel interrupt host tests failed: $LASTEXITCODE (see $out\run.stdout.log and $out\run.stderr.log)" }
Get-Content (Join-Path $out 'run.stdout.log')

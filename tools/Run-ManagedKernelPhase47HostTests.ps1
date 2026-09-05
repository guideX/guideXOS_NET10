[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\ManagedKernelPhase47HostTests\ManagedKernelPhase47HostTests.csproj'
$output = Join-Path $root "artifacts\managed-kernel-phase47-host-$Configuration"
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log = Join-Path $output 'phase47-host-tests.log'
$dotnet = Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
$msbuild = Join-Path ${env:ProgramFiles} 'dotnet\sdk\10.0.400\MSBuild.dll'
if (-not (Test-Path -LiteralPath $dotnet)) { throw "dotnet host is missing: $dotnet" }
if (-not (Test-Path -LiteralPath $msbuild)) { throw "MSBuild entry point is missing: $msbuild" }
Push-Location 'C:\Windows\Temp'
try {
    & $dotnet $msbuild $project /t:Restore /p:Configuration=$Configuration /p:RestoreIgnoreFailedSources=true |
        Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) { throw "Phase 47 host restore failed: $LASTEXITCODE" }
    & $dotnet $msbuild $project /t:Build /p:Configuration=$Configuration /p:OutputPath=$output\ /p:RestoreIgnoreFailedSources=true |
        Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw "Phase 47 host build failed: $LASTEXITCODE" }
    $assembly = Join-Path $output 'ManagedKernelPhase47HostTests.dll'
    & $dotnet $assembly | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw "Phase 47 host tests failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
Write-Output "MANAGED_KERNEL_PHASE47_HOST_TESTS_OUTPUT=$output"

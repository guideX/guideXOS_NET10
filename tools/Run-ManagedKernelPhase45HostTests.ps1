[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\ManagedKernelPhase45HostTests\ManagedKernelPhase45HostTests.csproj'
$output = Join-Path $root "artifacts\managed-kernel-phase45-host-$Configuration"
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log = Join-Path $output 'phase45-host-tests.log'
$dotnet = Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe'
$msbuild = Join-Path ${env:ProgramFiles} 'dotnet\sdk\10.0.400\MSBuild.dll'
Push-Location 'C:\Windows\Temp'
try {
    & $dotnet $msbuild $project /t:Build /p:Configuration=$Configuration /p:OutputPath=$output\ /p:RestoreIgnoreFailedSources=true |
        Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) { throw "Phase 45 host build failed: $LASTEXITCODE" }
    $assembly = Join-Path $output 'ManagedKernelPhase45HostTests.dll'
    & $dotnet $assembly | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw "Phase 45 host tests failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
Write-Output "MANAGED_KERNEL_PHASE45_HOST_TESTS_OUTPUT=$output"

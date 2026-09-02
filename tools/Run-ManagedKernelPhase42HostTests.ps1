[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\ManagedKernelPhase42HostTests\ManagedKernelPhase42HostTests.csproj'
$output = Join-Path $root "artifacts\managed-kernel-phase42-host-$Configuration"
New-Item -ItemType Directory -Force -Path $output | Out-Null
$log = Join-Path $output 'phase42-host-tests.log'
Push-Location 'C:\Windows\Temp'
try {
    & dotnet build $project --configuration $Configuration --output $output |
        Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) { throw "Phase 42 host build failed: $LASTEXITCODE" }
    $assembly = Join-Path $output 'ManagedKernelPhase42HostTests.dll'
    & dotnet $assembly | Tee-Object -FilePath $log -Append
    if ($LASTEXITCODE -ne 0) { throw "Phase 42 host tests failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
Write-Output "MANAGED_KERNEL_PHASE42_HOST_TESTS_OUTPUT=$output"

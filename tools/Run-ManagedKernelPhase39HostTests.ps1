[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\ManagedKernelPhase39HostTests\ManagedKernelPhase39HostTests.csproj'
$output = Join-Path $root "artifacts\managed-kernel-phase39-host-$Configuration"

if (-not (Test-Path -LiteralPath $project)) {
    throw "Phase 39 host-test project not found: $project"
}
if (Test-Path -LiteralPath $output) {
    throw "Output directory already exists: $output"
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

Push-Location $root
try {
    dotnet restore $project --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "Phase 39 restore failed: $LASTEXITCODE" }
    dotnet build $project --configuration $Configuration --no-restore `
        --output (Join-Path $output 'bin')
    if ($LASTEXITCODE -ne 0) { throw "Phase 39 build failed: $LASTEXITCODE" }
    $assembly = Join-Path $output 'bin\ManagedKernelPhase39HostTests.dll'
    if (-not (Test-Path -LiteralPath $assembly)) {
        throw "Phase 39 host assembly not found: $assembly"
    }
    dotnet $assembly | Tee-Object -FilePath (Join-Path $output 'phase39-host-tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 39 host tests failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}

Write-Output "MANAGED_KERNEL_PHASE39_HOST_TESTS_OUTPUT=$output"

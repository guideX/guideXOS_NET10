[CmdletBinding()]
param([string]$Configuration = 'Release')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\ManagedKernelPhase40HostTests\ManagedKernelPhase40HostTests.csproj'
$output = Join-Path $root "artifacts\managed-kernel-phase40-host-$Configuration"
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
Push-Location 'C:\'
try {
    dotnet restore $project --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "Phase 40 restore failed: $LASTEXITCODE" }
    dotnet build $project --configuration $Configuration --no-restore --output (Join-Path $output 'bin')
    if ($LASTEXITCODE -ne 0) { throw "Phase 40 build failed: $LASTEXITCODE" }
    $assembly = Join-Path $output 'bin\ManagedKernelPhase40HostTests.dll'
    dotnet $assembly | Tee-Object -FilePath (Join-Path $output 'phase40-host-tests.log')
    if ($LASTEXITCODE -ne 0) { throw "Phase 40 host tests failed: $LASTEXITCODE" }
} finally { Pop-Location }
Write-Output "MANAGED_KERNEL_PHASE40_HOST_TESTS_OUTPUT=$output"

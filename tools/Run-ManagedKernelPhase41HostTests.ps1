[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\ManagedKernelPhase41HostTests\ManagedKernelPhase41HostTests.csproj'
$output = Join-Path $root "artifacts\managed-kernel-phase41-host-$Configuration"
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet restore $project
if ($LASTEXITCODE -ne 0) { throw "Phase 41 host restore failed: $LASTEXITCODE" }
dotnet build $project --configuration $Configuration --no-restore -p:OutDir="$output\"
if ($LASTEXITCODE -ne 0) { throw "Phase 41 host build failed: $LASTEXITCODE" }
$assembly = Join-Path $output 'ManagedKernelPhase41HostTests.dll'
dotnet $assembly | Tee-Object -FilePath (Join-Path $output 'phase41-host-tests.log')
if ($LASTEXITCODE -ne 0) { throw "Phase 41 host tests failed: $LASTEXITCODE" }
Write-Output "MANAGED_KERNEL_PHASE41_HOST_TESTS_OUTPUT=$output"

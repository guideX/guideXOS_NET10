[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root 'artifacts\phase61-managed-worker-api-host-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'phase61-managed-worker-api-host-tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-DGXOS_SCHEDULER_HOST_TEST', '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\phase61_managed_worker_api_host_tests.c'),
    (Join-Path $include 'nativeaot_managed_worker_api.c'),
    (Join-Path $include 'nativeaot_scheduler_thread_lifecycle.c'),
    (Join-Path $include 'scheduler_foundation.c'),
    (Join-Path $include 'nativeaot_callback_bridge.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Phase 61 API host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Phase 61 API host test failed: $LASTEXITCODE" }
Write-Output "PHASE61_MANAGED_WORKER_API_HOST_TEST_BINARY=$exe"

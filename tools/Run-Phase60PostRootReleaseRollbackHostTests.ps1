[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root 'artifacts\phase60-postrootrelease-rollback-host-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'phase60-postrootrelease-rollback-host-tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
& $gcc.Source -std=c11 -Wall -Wextra -Werror -O2 -fno-builtin -DGXOS_SCHEDULER_HOST_TEST `
    -DGXOS_ENABLE_PHASE56_FAILURE_INJECTION `
    -DGXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK -I $include -o $exe `
    (Join-Path $include 'nativeaot_scheduler_thread_lifecycle.c') `
    (Join-Path $include 'tests\phase60_postrootrelease_rollback_host_tests.c') `
    (Join-Path $include 'scheduler_foundation.c') `
    (Join-Path $include 'nativeaot_callback_bridge.c')
if ($LASTEXITCODE -ne 0) { throw "Phase 60 post-root-release host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Phase 60 post-root-release host test failed: $LASTEXITCODE" }
Write-Output "PHASE60_POSTROOTRELEASE_ROLLBACK_HOST_TEST_BINARY=$exe"

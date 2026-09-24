[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\phase58-managed-root-rollback-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'phase58-managed-root-rollback-host-tests.exe'
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-DGXOS_SCHEDULER_HOST_TEST', '-DGXOS_ENABLE_PHASE56_FAILURE_INJECTION',
    '-DGXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK', '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\phase58_managed_root_rollback_host_tests.c'),
    (Join-Path $include 'nativeaot_scheduler_thread_lifecycle.c'),
    (Join-Path $include 'scheduler_foundation.c'),
    (Join-Path $include 'nativeaot_callback_bridge.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Phase 58 managed-root host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Phase 58 managed-root host test failed: $LASTEXITCODE" }
Write-Output "PHASE58_MANAGED_ROOT_ROLLBACK_HOST_TEST_BINARY=$exe"

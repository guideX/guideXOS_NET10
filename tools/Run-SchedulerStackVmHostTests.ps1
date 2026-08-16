[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\scheduler-stack-vm-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'scheduler-stack-vm-tests.exe'
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-DGXOS_SCHEDULER_HOST_TEST', '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\scheduler_stack_vm_tests.c'),
    (Join-Path $include 'scheduler_foundation.c'),
    (Join-Path $include 'vm_substrate.c'),
    (Join-Path $include 'memory_accounting.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Scheduler stack VM host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Scheduler stack VM host test failed: $LASTEXITCODE" }
Write-Output "SCHEDULER_STACK_VM_HOST_TEST_BINARY=$exe"

[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-memory-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'managed_kernel_memory_tests.exe'
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\managed_kernel_memory_tests.c'),
    (Join-Path $include 'managed_kernel_memory.c'),
    (Join-Path $include 'vm_substrate.c'),
    (Join-Path $include 'memory_accounting.c'))
& $gcc.Source @arguments 1> (Join-Path $output 'build.stdout.log') `
    2> (Join-Path $output 'build.stderr.log')
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel memory host test compile failed: $LASTEXITCODE"
}
& $exe 1> (Join-Path $output 'run.stdout.log') `
    2> (Join-Path $output 'run.stderr.log')
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel memory host tests failed: $LASTEXITCODE"
}
Get-Content (Join-Path $output 'run.stdout.log')

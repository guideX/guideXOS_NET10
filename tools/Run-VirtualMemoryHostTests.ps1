[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\virtual-memory-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'virtual-memory-tests.exe'
$arguments = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\virtual_memory_tests.c'),
    (Join-Path $include 'virtual_memory.c'),
    (Join-Path $include 'vm_substrate.c'),
    (Join-Path $include 'memory_accounting.c'),
    (Join-Path $include 'global_memory_status_ex.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Virtual memory host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Virtual memory host tests failed: $LASTEXITCODE" }
Write-Output "VIRTUAL_MEMORY_HOST_TEST_BINARY=$exe"

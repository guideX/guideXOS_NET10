[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\global-memory-status-ex-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'global-memory-status-ex-tests.exe'
$arguments = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\global_memory_status_ex_tests.c'),
    (Join-Path $include 'global_memory_status_ex.c'),
    (Join-Path $include 'memory_accounting.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "GlobalMemoryStatusEx host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "GlobalMemoryStatusEx host tests failed: $LASTEXITCODE" }
Write-Output "GLOBAL_MEMORY_STATUS_EX_HOST_TEST_BINARY=$exe"

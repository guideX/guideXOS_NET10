[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\vm-substrate-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'vm-substrate-tests.exe'
$arguments = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\vm_substrate_tests.c'),
    (Join-Path $include 'vm_substrate.c'),
    (Join-Path $include 'memory_accounting.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "VM substrate host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "VM substrate host tests failed: $LASTEXITCODE" }
Write-Output "VM_SUBSTRATE_HOST_TEST_BINARY=$exe"

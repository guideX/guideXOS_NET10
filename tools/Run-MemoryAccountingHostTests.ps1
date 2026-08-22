[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\memory-accounting-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'memory-accounting-tests.exe'
$object = Join-Path $output 'memory-accounting-freestanding.o'
$arguments = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\memory_accounting_tests.c'),
    (Join-Path $include 'memory_accounting.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Memory accounting host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Memory accounting host tests failed: $LASTEXITCODE" }
$freestandingArguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-ffreestanding', '-I', $include, '-c',
    (Join-Path $include 'memory_accounting.c'), '-o', $object)
& $gcc.Source @freestandingArguments
if ($LASTEXITCODE -ne 0) { throw "Memory accounting freestanding compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "Memory accounting core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'MEMORY_ACCOUNTING_CORE_NO_EXTERNAL_REFERENCES=PASS'
Write-Output "MEMORY_ACCOUNTING_HOST_TEST_BINARY=$exe"

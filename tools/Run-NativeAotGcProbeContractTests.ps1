[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\nativeaot-gc-probe-contract-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$test = Join-Path $include 'tests\nativeaot_gc_probe_contract_tests.c'
$exe = Join-Path $output 'nativeaot-gc-probe-contract-tests.exe'
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-I', $include,
    '-o', $exe, $test)
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) {
    throw "NativeAOT GC probe contract test compile failed: $LASTEXITCODE"
}
& $exe
if ($LASTEXITCODE -ne 0) {
    throw "NativeAOT GC probe contract tests failed: $LASTEXITCODE"
}
Write-Output "NATIVEAOT_GC_PROBE_CONTRACT_TEST_BINARY=$exe"

[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\nativeaot-callback-bridge-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$include = Join-Path $root 'src\Gate4Harness'
$source = Join-Path $include 'nativeaot_callback_bridge.c'
$test = Join-Path $include 'tests\nativeaot_callback_bridge_tests.c'
$header = Join-Path $include 'nativeaot_callback_bridge.h'
$exe = Join-Path $output 'nativeaot-callback-bridge-tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-I', $include, '-o', $exe, $source, $test)
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) {
    throw "NativeAOT callback bridge host test compile failed: $LASTEXITCODE"
}
& $exe
if ($LASTEXITCODE -ne 0) {
    throw "NativeAOT callback bridge host tests failed: $LASTEXITCODE"
}
$object = Join-Path $output 'nativeaot_callback_bridge.o'
$undefined = Join-Path $output 'nativeaot_callback_bridge.undefined.txt'
& $gcc.Source '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-ffreestanding', '-c', '-I', $include, $source, '-o', $object
if ($LASTEXITCODE -ne 0) {
    throw "NativeAOT callback bridge freestanding compile failed: $LASTEXITCODE"
}
$nm = Get-Command nm -ErrorAction Stop
$undefinedLines = @(& $nm.Source '-u' $object)
New-Item -ItemType File -Force -Path $undefined | Out-Null
if ($undefinedLines.Count -ne 0) {
    $undefinedLines | Set-Content -LiteralPath $undefined
    throw "NativeAOT callback bridge has unexpected external references: $((Get-Content $undefined) -join ' ')"
}
Write-Output 'NATIVEAOT_CALLBACK_BRIDGE_NO_EXTERNAL_REFERENCES=PASS'
Write-Output "NATIVEAOT_CALLBACK_BRIDGE_HOST_TEST_BINARY=$exe"

[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\com-api-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'com-api-tests.exe'
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2',
    '-DGXOS_SCHEDULER_HOST_TEST', '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\com_api_tests.c'),
    (Join-Path $include 'com_api.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "COM API host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "COM API host tests failed: $LASTEXITCODE" }
Write-Output "COM_API_HOST_TEST_BINARY=$exe"

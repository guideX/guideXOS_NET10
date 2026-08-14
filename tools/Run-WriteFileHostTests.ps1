[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\write-file-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Get-Command gcc -ErrorAction Stop
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $output 'write-file-tests.exe'
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2',
    '-DGXOS_SCHEDULER_HOST_TEST', '-I', $include, '-o', $exe,
    (Join-Path $include 'tests\write_file_tests.c'),
    (Join-Path $include 'write_file.c'),
    (Join-Path $include 'standard_handle.c'),
    (Join-Path $include 'scheduler_foundation.c'))
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "WriteFile host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "WriteFile host tests failed: $LASTEXITCODE" }
Write-Output "WRITE_FILE_HOST_TEST_BINARY=$exe"

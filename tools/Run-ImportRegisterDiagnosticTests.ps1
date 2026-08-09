[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\import-register-diagnostic-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$include = Join-Path $root 'src\Gate4Harness'
$test = Join-Path $include 'tests\import_register_diagnostic_tests.c'
$assembly = Join-Path $include 'tests\import_register_probe.S'
$exe = Join-Path $output 'import-register-diagnostic-tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
$arguments = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-I', $include, '-o', $exe, $test, $assembly)
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Import register diagnostic test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Import register diagnostic test failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath $test,$assembly,$exe | Format-Table -AutoSize
Write-Output 'IMPORT_REGISTER_DIAGNOSTIC_TESTS=PASSED'

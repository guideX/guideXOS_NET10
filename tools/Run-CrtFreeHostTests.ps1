[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\crt-free-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'crt_free_tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)
$sources = @(
    (Join-Path $include 'crt_malloc.c'),
    (Join-Path $include 'memory_accounting.c'),
    (Join-Path $include 'platform_processor_topology.c'),
    (Join-Path $include 'tests\crt_free_tests.c'))
& $gcc.Source @common '-o' $exe @sources
if ($LASTEXITCODE -ne 0) { throw "CRT free host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "CRT free host tests failed: $LASTEXITCODE" }
Write-Output "CRT_FREE_HOST_TEST_BINARY=$exe"
Get-FileHash -Algorithm SHA256 -LiteralPath ($sources + $exe) | Format-Table -AutoSize

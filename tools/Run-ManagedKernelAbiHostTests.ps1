[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-abi-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$gcc = Get-Command gcc -ErrorAction Stop
$exe = Join-Path $out 'managed_kernel_abi_tests.exe'
New-Item -ItemType Directory -Force -Path $out | Out-Null
& $gcc.Source '-std=c11', '-Wall', '-Wextra', '-Werror',
    '-Isrc\Gate4Harness', 'src\Gate4Harness\tests\managed_kernel_abi_tests.c',
    '-o', $exe 1> (Join-Path $out 'build.stdout.log') 2> (Join-Path $out 'build.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "ManagedKernel ABI host test build failed (exit $LASTEXITCODE)." }
& $exe 1> (Join-Path $out 'run.stdout.log') 2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "ManagedKernel ABI host tests failed (exit $LASTEXITCODE)." }
Get-Content (Join-Path $out 'run.stdout.log')

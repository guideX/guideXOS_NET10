[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-boot-resource-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$exe = Join-Path $out 'managed_kernel_boot_resource_tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
New-Item -ItemType Directory -Force -Path $out | Out-Null
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-Isrc\Gate4Harness',
    'src\Gate4Harness\tests\managed_kernel_boot_resource_tests.c',
    'src\Gate4Harness\memory_accounting.c',
    'src\Gate4Harness\managed_kernel_boot_resources.c',
    '-o', $exe)
& $gcc.Source @arguments 1> (Join-Path $out 'build.stdout.log') `
    2> (Join-Path $out 'build.stderr.log')
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel boot-resource host test build failed (exit $LASTEXITCODE)."
}
& $exe 1> (Join-Path $out 'run.stdout.log') 2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel boot-resource host tests failed (exit $LASTEXITCODE)."
}
Get-Content (Join-Path $out 'run.stdout.log')

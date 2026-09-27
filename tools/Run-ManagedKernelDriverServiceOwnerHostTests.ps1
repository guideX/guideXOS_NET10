[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel-driver-service-owner-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$exe = Join-Path $out 'managed_kernel_driver_service_owner_tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
New-Item -ItemType Directory -Force -Path $out | Out-Null
$gccArguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror',
    '-Isrc\Gate4Harness',
    'src\Gate4Harness\tests\managed_kernel_driver_service_owner_tests.c',
    'src\Gate4Harness\managed_kernel_driver_service_owner.c', '-o', $exe)
& $gcc.Source @gccArguments
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel service owner host test build failed (exit $LASTEXITCODE)."
}
& $exe
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel service owner host tests failed (exit $LASTEXITCODE)."
}

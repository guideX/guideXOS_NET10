[CmdletBinding()]
param(
    [string]$PayloadPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $root 'artifacts\managed-kernel\publish\gxos-managed-kernel.dll'
}
$payload = [IO.Path]::GetFullPath($PayloadPath)
$out = Join-Path $root 'artifacts\managed-kernel-service-host-tests'
$exe = Join-Path $out 'managed_kernel_service_host_tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
if (-not (Test-Path -LiteralPath $payload)) {
    throw "ManagedKernel payload not found: $payload"
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
$arguments = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror',
    '-Isrc\Gate4Harness',
    'src\Gate4Harness\tests\managed_kernel_service_host_tests.c',
    '-lkernel32', '-o', $exe)
& $gcc.Source @arguments 1> (Join-Path $out 'build.stdout.log') `
    2> (Join-Path $out 'build.stderr.log')
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel service host test build failed (exit $LASTEXITCODE)."
}
& $exe $payload 1> (Join-Path $out 'run.stdout.log') `
    2> (Join-Path $out 'run.stderr.log')
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel service host tests failed (exit $LASTEXITCODE)."
}
Get-Content (Join-Path $out 'run.stdout.log')

[CmdletBinding()]
param([string]$OutputDirectory = '')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $env:TEMP 'gxos-phase75-diagnostic-host-tests'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$gcc = Get-Command gcc -ErrorAction Stop
New-Item -ItemType Directory -Force -Path $out | Out-Null

$diagnosticExe = Join-Path $out 'managed_kernel_diagnostic_tests.exe'
$diagnosticBuild = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-Isrc\Gate4Harness',
    'src\Gate4Harness\managed_kernel_diagnostic.c',
    'src\Gate4Harness\tests\managed_kernel_diagnostic_tests.c',
    '-o', $diagnosticExe)
& $gcc.Source @diagnosticBuild 1> (Join-Path $out 'diagnostic-build.stdout.log') `
    2> (Join-Path $out 'diagnostic-build.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Diagnostic protocol host build failed: $LASTEXITCODE" }
& $diagnosticExe 1> (Join-Path $out 'diagnostic-run.stdout.log') `
    2> (Join-Path $out 'diagnostic-run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Diagnostic protocol host tests failed: $LASTEXITCODE" }

$resourcesExe = Join-Path $out 'managed_kernel_device_resources_tests.exe'
$resourcesBuild = @(
    '-std=c11', '-Wall', '-Wextra', '-Werror', '-Isrc\Gate4Harness',
    'src\Gate4Harness\tests\managed_kernel_device_resources_tests.c',
    'src\Gate4Harness\managed_kernel_device_resources.c',
    '-o', $resourcesExe)
& $gcc.Source @resourcesBuild 1> (Join-Path $out 'resources-build.stdout.log') `
    2> (Join-Path $out 'resources-build.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Diagnostic resource host build failed: $LASTEXITCODE" }
& $resourcesExe 1> (Join-Path $out 'resources-run.stdout.log') `
    2> (Join-Path $out 'resources-run.stderr.log')
if ($LASTEXITCODE -ne 0) { throw "Diagnostic resource host tests failed: $LASTEXITCODE" }

Get-Content (Join-Path $out 'diagnostic-run.stdout.log')
Get-Content (Join-Path $out 'resources-run.stdout.log')

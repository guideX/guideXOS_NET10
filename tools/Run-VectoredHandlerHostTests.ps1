[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if (-not $gcc) { throw 'gcc is required for vectored-handler host tests.' }
$out = Join-Path $root 'artifacts\vectored-handler-host-tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe = Join-Path $out 'vectored_handler_tests.exe'
& $gcc.Source '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2',
    '-DGXOS_VEH_ENABLE_TEST_RESET',
    '-I', (Join-Path $root 'src\Gate4Harness'), '-o', $exe,
    (Join-Path $root 'src\Gate4Harness\exception_context.c'),
    (Join-Path $root 'src\Gate4Harness\vectored_handler.c'),
    (Join-Path $root 'src\Gate4Harness\tests\vectored_handler_tests.c')
if ($LASTEXITCODE -ne 0) { throw "vectored-handler host compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "vectored-handler host tests failed: $LASTEXITCODE" }

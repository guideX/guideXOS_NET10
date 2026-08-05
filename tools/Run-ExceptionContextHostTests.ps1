[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if (-not $gcc) { throw 'gcc is required for exception context host tests.' }
$out = Join-Path $root 'artifacts\exception-context-host-tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe = Join-Path $out 'exception_context_tests.exe'
$source = Join-Path $root 'src\Gate4Harness\exception_context.c'
$tests = Join-Path $root 'src\Gate4Harness\tests\exception_context_tests.c'
& $gcc.Source '-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-I', (Join-Path $root 'src\Gate4Harness'),
    '-o', $exe, $source, $tests
if ($LASTEXITCODE -ne 0) { throw "exception context host compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "exception context host tests failed: $LASTEXITCODE" }

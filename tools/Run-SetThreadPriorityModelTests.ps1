[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\set-thread-priority-model-tests'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if ($null -eq $gcc) { throw 'gcc is required for the SetThreadPriority model test.' }

$includeDirectory = Join-Path $root 'src\Gate4Harness'
$test = Join-Path $root 'src\Gate4Harness\set_thread_priority_model_tests.c'
$scheduler = Join-Path $root 'src\Gate4Harness\scheduler_foundation.c'
$priority = Join-Path $root 'src\Gate4Harness\set_thread_priority.c'
$binary = Join-Path $outputDirectory 'set-thread-priority-model-tests.exe'
$arguments = @(
    '-std=c11', '-O2', '-Wall', '-Wextra', '-Werror',
    '-DGXOS_SCHEDULER_HOST_TEST', '-I', $includeDirectory,
    '-o', $binary, $test, $scheduler, $priority
)

& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) {
    throw "SetThreadPriority model test compile failed: $LASTEXITCODE"
}
& $binary
if ($LASTEXITCODE -ne 0) {
    throw "SetThreadPriority model test failed: $LASTEXITCODE"
}

Write-Output "SET_THREAD_PRIORITY_MODEL_TEST_BINARY=$binary"

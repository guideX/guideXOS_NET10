[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\scheduler-model-tests'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if ($null -eq $gcc) { throw 'gcc is required for the scheduler model test.' }

$source = Join-Path $root 'src\Gate4Harness\scheduler_foundation.c'
$createEventSource = Join-Path $root 'src\Gate4Harness\create_event_w.c'
$test = Join-Path $root 'src\Gate4Harness\scheduler_model_tests.c'
$binary = Join-Path $outputDirectory 'scheduler-model-tests.exe'
$arguments = @(
    '-std=c11', '-O2', '-Wall', '-Wextra', '-Werror',
    '-DGXOS_SCHEDULER_HOST_TEST',
    '-I', (Join-Path $root 'src\Gate4Harness'),
    '-o', $binary, $test, $source, $createEventSource
)

& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "Scheduler model test compile failed: $LASTEXITCODE" }
& $binary
if ($LASTEXITCODE -ne 0) { throw "Scheduler model test failed: $LASTEXITCODE" }

Write-Output "SCHEDULER_MODEL_TEST_BINARY=$binary"

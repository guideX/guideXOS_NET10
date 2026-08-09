[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\resume-thread-model-tests'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if ($null -eq $gcc) { throw 'gcc is required for the ResumeThread model test.' }
$binary = Join-Path $outputDirectory 'resume-thread-model-tests.exe'
$arguments = @(
    '-std=c11', '-O2', '-Wall', '-Wextra', '-Werror',
    '-DGXOS_SCHEDULER_HOST_TEST',
    '-I', (Join-Path $root 'src\Gate4Harness'),
    '-o', $binary,
    (Join-Path $root 'src\Gate4Harness\resume_thread_model_tests.c'),
    (Join-Path $root 'src\Gate4Harness\set_thread_priority.c'),
    (Join-Path $root 'src\Gate4Harness\scheduler_foundation.c')
)
& $gcc.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "ResumeThread model test compile failed: $LASTEXITCODE" }
& $binary
if ($LASTEXITCODE -ne 0) { throw "ResumeThread model test failed: $LASTEXITCODE" }
Write-Output "RESUME_THREAD_MODEL_TEST_BINARY=$binary"

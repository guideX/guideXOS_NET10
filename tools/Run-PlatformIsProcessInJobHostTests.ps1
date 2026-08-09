[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-is-process-in-job-host-tests'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$include = Join-Path $root 'src\Gate4Harness'
$source = Join-Path $include 'platform_is_process_in_job.c'
$header = Join-Path $include 'platform_is_process_in_job.h'
$systemHeader = Join-Path $include 'platform_system_info.h'
$scheduler = Join-Path $include 'scheduler_foundation.c'
$schedulerHeader = Join-Path $include 'scheduler_foundation.h'
$test = Join-Path $include 'tests\platform_is_process_in_job_tests.c'
$exe = Join-Path $output 'platform_is_process_in_job_tests.exe'
$object = Join-Path $output 'platform_is_process_in_job.o'
$gcc = Get-Command gcc -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin',
    '-DGXOS_SCHEDULER_HOST_TEST', '-I', $include)

& $gcc.Source @common '-o' $exe $test $source $scheduler
if ($LASTEXITCODE -ne 0) { throw "IsProcessInJob model test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "IsProcessInJob model tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-ffreestanding' '-c' $source '-o' $object
if ($LASTEXITCODE -ne 0) { throw "IsProcessInJob core compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "IsProcessInJob core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'ISPROCESSINJOB_CORE_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $header,$systemHeader,$schedulerHeader,$source,$test,$exe,$object | Format-Table -AutoSize
Write-Output 'ISPROCESSINJOB_MODEL_TESTS=PASSED'

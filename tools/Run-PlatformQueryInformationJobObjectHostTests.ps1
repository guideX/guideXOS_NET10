[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-query-information-job-object-host-tests'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$source = Join-Path $root 'src\Gate4Harness\platform_query_information_job_object.c'
$header = Join-Path $root 'src\Gate4Harness\platform_query_information_job_object.h'
$systemHeader = Join-Path $root 'src\Gate4Harness\platform_system_info.h'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_query_information_job_object_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $outputDirectory 'platform_query_information_job_object_tests.exe'
$object = Join-Path $outputDirectory 'platform_query_information_job_object.o'
$gcc = Get-Command gcc -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)

& $gcc.Source @common '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "QueryInformationJobObject host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "QueryInformationJobObject host tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-ffreestanding', '-c', $source, '-o', $object
if ($LASTEXITCODE -ne 0) { throw "QueryInformationJobObject core compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "QueryInformationJobObject core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'QUERY_INFORMATION_JOB_OBJECT_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $header,$systemHeader,$source,$test,$exe,$object | Format-Table -AutoSize
Write-Output 'QUERY_INFORMATION_JOB_OBJECT_HOST_TESTS=PASSED'

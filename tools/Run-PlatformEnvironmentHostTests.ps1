[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-environment-host-tests-20260731'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$source = Join-Path $root 'src\Gate4Harness\platform_environment.c'
$header = Join-Path $root 'src\Gate4Harness\platform_environment.h'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_environment_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'platform_environment_tests.exe'
$object = Join-Path $OutputDirectory 'platform_environment.o'
$gcc = Get-Command gcc -ErrorAction Stop

$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)
& $gcc.Source @common '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "platform environment host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "platform environment host tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-ffreestanding' '-c' $source '-o' $object
if ($LASTEXITCODE -ne 0) { throw "platform environment core object compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "platform environment core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'PLATFORM_ENVIRONMENT_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $header,$source,$test,$exe,$object | Format-Table -AutoSize
Write-Output 'PLATFORM_ENVIRONMENT_HOST_TESTS=PASSED'

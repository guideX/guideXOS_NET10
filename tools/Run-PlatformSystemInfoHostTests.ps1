[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-system-info-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$source = Join-Path $root 'src\Gate4Harness\platform_system_info.c'
$header = Join-Path $root 'src\Gate4Harness\platform_system_info.h'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_system_info_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'platform_system_info_tests.exe'
$object = Join-Path $OutputDirectory 'platform_system_info.o'
$gcc = Get-Command gcc -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)

& $gcc.Source @common '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "GetSystemInfo host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "GetSystemInfo host tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-ffreestanding' '-c' $source '-o' $object
if ($LASTEXITCODE -ne 0) { throw "GetSystemInfo core object compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "GetSystemInfo core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'SYSTEM_INFO_TEST_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $header,$source,$test,$exe,$object | Format-Table -AutoSize
Write-Output 'SYSTEM_INFO_HOST_TESTS=PASSED'

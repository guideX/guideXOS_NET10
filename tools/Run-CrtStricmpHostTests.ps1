[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\crt-stricmp-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$source = Join-Path $root 'src\Gate4Harness\crt_stricmp.c'
$header = Join-Path $root 'src\Gate4Harness\crt_stricmp.h'
$test = Join-Path $root 'src\Gate4Harness\tests\crt_stricmp_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'crt_stricmp_tests.exe'
$object = Join-Path $OutputDirectory 'crt_stricmp.o'
$gcc = Get-Command gcc -ErrorAction Stop

$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)
& $gcc.Source @common '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "CRT stricmp host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "CRT stricmp host tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-ffreestanding' '-c' $source '-o' $object
if ($LASTEXITCODE -ne 0) { throw "CRT stricmp core object compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "CRT stricmp core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'CRT_STRICMP_TEST_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $header,$source,$test,$exe,$object | Format-Table -AutoSize
Write-Output 'CRT_STRICMP_HOST_TESTS=PASSED'

[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\crt-initterm-e-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$source = Join-Path $root 'src\Gate4Harness\crt_initterm_e.c'
$test = Join-Path $root 'src\Gate4Harness\tests\crt_initterm_e_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'crt_initterm_e_tests.exe'
$object = Join-Path $OutputDirectory 'crt_initterm_e.o'
$gcc = Get-Command gcc -ErrorAction Stop

& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-I' $include '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "CRT _initterm_e host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "CRT _initterm_e host tests failed: $LASTEXITCODE" }

& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-I' $include '-c' $source '-o' $object
if ($LASTEXITCODE -ne 0) { throw "CRT _initterm_e core object compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "CRT _initterm_e core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'CRT_INITTERM_E_TEST_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$test,$exe,$object | Format-Table -AutoSize

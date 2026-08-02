[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\crt-onexit-register-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$source = Join-Path $root 'src\Gate4Harness\crt_onexit.c'
$test = Join-Path $root 'src\Gate4Harness\tests\crt_onexit_register_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'crt_onexit_register_tests.exe'
$object = Join-Path $OutputDirectory 'crt_onexit_register.o'
$gcc = Get-Command gcc -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-I', $include)

& $gcc.Source @common '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "CRT _register_onexit_function host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "CRT _register_onexit_function host tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-c' $source '-o' $object
if ($LASTEXITCODE -ne 0) { throw "CRT on-exit core object compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "CRT on-exit core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'CRT_ONEXIT_REGISTER_TEST_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$test,$exe,$object | Format-Table -AutoSize

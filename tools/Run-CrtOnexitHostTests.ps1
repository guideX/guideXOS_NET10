[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\crt-onexit-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$exe = Join-Path $OutputDirectory 'crt_onexit_tests.exe'
$source = Join-Path $root 'src\Gate4Harness\crt_onexit.c'
$test = Join-Path $root 'src\Gate4Harness\tests\crt_onexit_tests.c'
$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if (-not $gcc) { throw 'gcc is required for CRT on-exit host tests.' }
& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-I' (Join-Path $root 'src\Gate4Harness') '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "CRT on-exit host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "CRT on-exit host tests failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$test,$exe | Format-Table -AutoSize

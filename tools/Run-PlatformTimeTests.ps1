[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [switch]$WrongEpoch,
    [switch]$ExpectFailure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-time-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$exe = Join-Path $OutputDirectory 'platform_time_tests.exe'
$source = Join-Path $root 'src\Gate4Harness\platform_time.c'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_time_tests.c'
$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if (-not $gcc) { throw 'gcc is required for platform time tests.' }
$defines = @()
if ($WrongEpoch) { $defines += '-DGXOS_TEST_WRONG_EPOCH' }
& $gcc.Source '-std=c11','-Wall','-Wextra','-Werror','-O2','-I', (Join-Path $root 'src\Gate4Harness'), $defines, '-o', $exe, $source, $test
if ($LASTEXITCODE -ne 0) { throw "Platform time test compile failed: $LASTEXITCODE" }
& $exe
if ($ExpectFailure) {
    if ($LASTEXITCODE -eq 0) { throw 'Expected the isolated negative conversion build to fail known vectors.' }
    Write-Output 'PLATFORM_TIME_NEGATIVE_WRONG_EPOCH=REJECTED_AS_EXPECTED'
} elseif ($LASTEXITCODE -ne 0) { throw "Platform time tests failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$test,$exe | Format-Table -AutoSize

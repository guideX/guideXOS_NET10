[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-performance-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$exe = Join-Path $OutputDirectory 'platform_performance_tests.exe'
$source = Join-Path $root 'src\Gate4Harness\platform_performance.c'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_performance_tests.c'
$gcc = Get-Command gcc -ErrorAction SilentlyContinue
if (-not $gcc) { throw 'gcc is required for platform performance tests.' }
$gccArguments = @('-std=c11','-Wall','-Wextra','-Werror','-O2','-I',(Join-Path $root 'src\Gate4Harness'),'-o',$exe,$source,$test)
& $gcc.Source @gccArguments
if ($LASTEXITCODE -ne 0) { throw "Platform performance test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Platform performance tests failed: $LASTEXITCODE" }
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$test,$exe | Format-Table -AutoSize

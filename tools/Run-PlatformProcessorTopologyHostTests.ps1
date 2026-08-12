[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-processor-topology-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$source = Join-Path $root 'src\Gate4Harness\platform_processor_topology.c'
$header = Join-Path $root 'src\Gate4Harness\platform_processor_topology.h'
$accounting = Join-Path $root 'src\Gate4Harness\memory_accounting.c'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_processor_topology_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'platform_processor_topology_tests.exe'
$gcc = Get-Command gcc -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)

& $gcc.Source @common '-o' $exe $source $accounting $test
if ($LASTEXITCODE -ne 0) { throw "Processor topology host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "Processor topology host tests failed: $LASTEXITCODE" }

Get-FileHash -Algorithm SHA256 -LiteralPath $header,$source,$test,$exe | Format-Table -AutoSize
Write-Output 'PROCESSOR_TOPOLOGY_HOST_TESTS=PASSED'

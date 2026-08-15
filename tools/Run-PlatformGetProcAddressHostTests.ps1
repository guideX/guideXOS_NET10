[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-get-proc-address-host-tests'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$source = Join-Path $root 'src\Gate4Harness\platform_get_proc_address.c'
$registrySource = Join-Path $root 'src\Gate4Harness\platform_module_registry.c'
$header = Join-Path $root 'src\Gate4Harness\platform_get_proc_address.h'
$crtHeader = Join-Path $root 'src\Gate4Harness\crt_initterm.h'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_get_proc_address_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $outputDirectory 'platform_get_proc_address_tests.exe'
$object = Join-Path $outputDirectory 'platform_get_proc_address.o'
$registryObject = Join-Path $outputDirectory 'platform_module_registry.o'
$gcc = Get-Command gcc -ErrorAction Stop
$ld = Get-Command ld -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)

& $gcc.Source @common '-o' $exe $source $registrySource $test
if ($LASTEXITCODE -ne 0) { throw "GetProcAddress host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "GetProcAddress host tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-ffreestanding', '-c', $source, '-o', $object
if ($LASTEXITCODE -ne 0) { throw "GetProcAddress core compile failed: $LASTEXITCODE" }
& $gcc.Source @common '-ffreestanding', '-c', $registrySource, '-o', $registryObject
if ($LASTEXITCODE -ne 0) { throw "Module registry core compile failed: $LASTEXITCODE" }
$combined = Join-Path $outputDirectory 'platform_get_proc_address_combined.o'
& $ld.Source '-r' $object $registryObject '-o' $combined
if ($LASTEXITCODE -ne 0) { throw "GetProcAddress core link check failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $combined
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "GetProcAddress core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'GETPROCADDRESS_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $header,$crtHeader,$source,$registrySource,$test,$exe,$object,$combined | Format-Table -AutoSize
Write-Output 'GETPROCADDRESS_HOST_TESTS=PASSED'

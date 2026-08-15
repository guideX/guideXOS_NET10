[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\platform-load-library-host-tests'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$source = Join-Path $root 'src\Gate4Harness\platform_load_library.c'
$registrySource = Join-Path $root 'src\Gate4Harness\platform_module_registry.c'
$moduleSource = Join-Path $root 'src\Gate4Harness\platform_get_module_handle.c'
$procSource = Join-Path $root 'src\Gate4Harness\platform_get_proc_address.c'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_load_library_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $outputDirectory 'platform_load_library_tests.exe'
$loadObject = Join-Path $outputDirectory 'platform_load_library.o'
$registryObject = Join-Path $outputDirectory 'platform_module_registry.o'
$moduleObject = Join-Path $outputDirectory 'platform_get_module_handle.o'
$procObject = Join-Path $outputDirectory 'platform_get_proc_address.o'
$combined = Join-Path $outputDirectory 'platform_load_library_combined.o'
$gcc = Get-Command gcc -ErrorAction Stop
$ld = Get-Command ld -ErrorAction Stop
$common = @('-std=c11', '-Wall', '-Wextra', '-Werror', '-O2', '-fno-builtin', '-I', $include)

& $gcc.Source @common '-o' $exe $source $registrySource $moduleSource $procSource $test
if ($LASTEXITCODE -ne 0) { throw "LoadLibraryExW host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "LoadLibraryExW host tests failed: $LASTEXITCODE" }

& $gcc.Source @common '-ffreestanding', '-c', $source, '-o', $loadObject
if ($LASTEXITCODE -ne 0) { throw "LoadLibraryExW core compile failed: $LASTEXITCODE" }
& $gcc.Source @common '-ffreestanding', '-c', $registrySource, '-o', $registryObject
if ($LASTEXITCODE -ne 0) { throw "Module registry core compile failed: $LASTEXITCODE" }
& $gcc.Source @common '-ffreestanding', '-c', $moduleSource, '-o', $moduleObject
if ($LASTEXITCODE -ne 0) { throw "GetModuleHandleW core compile failed: $LASTEXITCODE" }
& $gcc.Source @common '-ffreestanding', '-c', $procSource, '-o', $procObject
if ($LASTEXITCODE -ne 0) { throw "GetProcAddress core compile failed: $LASTEXITCODE" }
& $ld.Source '-r' $loadObject $registryObject $moduleObject $procObject '-o' $combined
if ($LASTEXITCODE -ne 0) { throw "LoadLibraryExW core link check failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $combined
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "LoadLibraryExW core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'LOADLIBRARYEXW_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$registrySource,$moduleSource,$procSource,$test,$exe,$combined | Format-Table -AutoSize
Write-Output 'LOADLIBRARYEXW_HOST_TESTS=PASSED'

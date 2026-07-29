[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\slist-host-tests'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$source = Join-Path $root 'src\Gate4Harness\platform_slist.c'
$test = Join-Path $root 'src\Gate4Harness\tests\platform_slist_tests.c'
$include = Join-Path $root 'src\Gate4Harness'
$exe = Join-Path $OutputDirectory 'platform_slist_tests.exe'
$object = Join-Path $OutputDirectory 'platform_slist.o'
$wrongObject = Join-Path $OutputDirectory 'platform_slist_wrong_layout.o'
$wrongLog = Join-Path $OutputDirectory 'wrong-layout.stderr.log'
$gcc = Get-Command gcc -ErrorAction Stop

& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-I' $include '-o' $exe $source $test
if ($LASTEXITCODE -ne 0) { throw "SLIST host test compile failed: $LASTEXITCODE" }
& $exe
if ($LASTEXITCODE -ne 0) { throw "SLIST host tests failed: $LASTEXITCODE" }

$savedErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-I' $include '-DGXOS_SLIST_TEST_WRONG_LAYOUT' '-c' $source '-o' $wrongObject 2> $wrongLog
$wrongExitCode = $LASTEXITCODE
$ErrorActionPreference = $savedErrorActionPreference
if ($wrongExitCode -eq 0) { throw 'The intentionally incorrect SLIST layout unexpectedly compiled.' }
Write-Output 'SLIST_TEST_INCORRECT_LAYOUT_CONTROL=PASS'

& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-I' $include '-c' $source '-o' $object
if ($LASTEXITCODE -ne 0) { throw "SLIST core object compile failed: $LASTEXITCODE" }
$nm = Get-Command nm -ErrorAction Stop
$undefined = & $nm.Source '-u' $object
if (-not [string]::IsNullOrWhiteSpace(($undefined -join "`n"))) {
    throw "SLIST core has unexpected external references: $($undefined -join ' ')"
}
Write-Output 'SLIST_TEST_NO_EXTERNAL_REFERENCES=PASS'
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$test,$exe,$object,$wrongLog | Format-Table -AutoSize

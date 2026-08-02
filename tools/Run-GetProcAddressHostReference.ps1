[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\getprocaddress-host-reference'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$source = Join-Path $root 'tools\GetProcAddressHostProbe.c'
$exe = Join-Path $outputDirectory 'GetProcAddressHostProbe.exe'
$result = Join-Path $outputDirectory 'reference-output.txt'
$gcc = Get-Command gcc -ErrorAction Stop

& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-D_WIN32_WINNT=0x0601' '-o' $exe $source '-lkernel32'
if ($LASTEXITCODE -ne 0) { throw "GetProcAddress Windows reference probe compile failed: $LASTEXITCODE" }
& $exe | Set-Content -LiteralPath $result -Encoding utf8
if ($LASTEXITCODE -ne 0) { throw "GetProcAddress Windows reference probe failed: $LASTEXITCODE" }
Get-Content -LiteralPath $result
Get-FileHash -Algorithm SHA256 -LiteralPath $source,$exe,$result | Format-Table -AutoSize
Write-Output 'GETPROCADDRESS_HOST_REFERENCE=CAPTURED'

[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root ('artifacts\gate1-run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $out | Out-Null
$project = Join-Path $root 'src\ManagedEntryProbe\ManagedEntryProbe.csproj'
$dotnet = Get-Command dotnet -ErrorAction Stop
& $dotnet.Source '--version' 1> (Join-Path $out 'dotnet-version.stdout.log') 2> (Join-Path $out 'dotnet-version.stderr.log')
& $dotnet.Source '--info' 1> (Join-Path $out 'dotnet-info.stdout.log') 2> (Join-Path $out 'dotnet-info.stderr.log')

function Publish([string]$Name, [string[]]$Extra)
{
    $publish = Join-Path $out $Name
    $binlog = Join-Path $out ($Name + '.binlog')
    New-Item -ItemType Directory -Force -Path $publish | Out-Null
    $arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishAot=true', "-p:PublishDir=$publish\", "-bl:$binlog") + $Extra
    & $dotnet.Source @arguments 1> (Join-Path $out ($Name + '.stdout.log')) 2> (Join-Path $out ($Name + '.stderr.log'))
    if ($LASTEXITCODE -ne 0) { throw "Gate 1 publish failed for $Name (exit $LASTEXITCODE)." }
    Get-FileHash -LiteralPath (Get-ChildItem -LiteralPath $publish -File | Select-Object -ExpandProperty FullName) -Algorithm SHA256 | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out ($Name + '.hashes.json')) -Encoding utf8
}

Publish 'exe' @('-p:OutputType=Exe')
Publish 'shared' @('-p:OutputType=Library', '-p:NativeLib=Shared')
Publish 'static' @('-p:OutputType=Library', '-p:NativeLib=Static')
Write-Output "GATE1_ARTIFACTS=$out"

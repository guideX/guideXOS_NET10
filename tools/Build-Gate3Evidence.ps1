[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [string]$SourceArtifact = '',
    [string]$StagedArtifact = '',
    [string]$MapPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root ('artifacts\gate3-evidence-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$out = [IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($SourceArtifact)) { $SourceArtifact = Join-Path $root 'artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll' }
if ([string]::IsNullOrWhiteSpace($StagedArtifact)) { $StagedArtifact = Join-Path $root 'artifacts\gate4\ESP\GXOS\gxos-managed-entry-probe.dll' }
if ([string]::IsNullOrWhiteSpace($MapPath)) { $MapPath = Join-Path $root 'src\ManagedEntryProbe\obj\Release\net10.0\win-x64\native\gxos-managed-entry-probe.map.xml' }
foreach ($required in @($SourceArtifact, $StagedArtifact, $MapPath)) { if (-not (Test-Path -LiteralPath $required)) { throw "Required Gate 3 input not found: $required" } }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$sourceManifest = Join-Path $out 'source.manifest.json'
$stagedManifest = Join-Path $out 'staged.manifest.json'
$comparison = Join-Path $out 'comparison.json'
& (Join-Path $PSScriptRoot 'Inspect-PE.ps1') -InputPath $SourceArtifact -OutputPath $sourceManifest -MapPath $MapPath
& (Join-Path $PSScriptRoot 'Inspect-PE.ps1') -InputPath $StagedArtifact -OutputPath $stagedManifest -MapPath $MapPath
& (Join-Path $PSScriptRoot 'Compare-PEManifests.ps1') -SourceManifest $sourceManifest -StagedManifest $stagedManifest -OutputPath $comparison

$objdump = Get-Command objdump -ErrorAction SilentlyContinue
if (-not $objdump) { throw 'objdump is required for the Gate 3 PE report.' }
& $objdump.Source '-p' $SourceArtifact 1> (Join-Path $out 'source-pe-report.txt') 2> (Join-Path $out 'source-pe-report.stderr.log')
if ($LASTEXITCODE -ne 0) { throw 'objdump failed for the Gate 3 source artifact.' }
Get-FileHash -Algorithm SHA256 -LiteralPath $SourceArtifact,$StagedArtifact | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'hashes.json') -Encoding utf8
Write-Output "GATE3_EVIDENCE=$out"
Write-Output 'GATE3_PROOF=PE_IDENTITY_AND_MANIFEST_PASS'

[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [string]$ManagedArtifact = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\phase53w-readable-range-build-tests'
}
if ([string]::IsNullOrWhiteSpace($ManagedArtifact)) {
    $ManagedArtifact = Join-Path $root 'artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$artifact = [IO.Path]::GetFullPath($ManagedArtifact)
$buildScript = Join-Path $root 'tools\Build-Gate4Harness.ps1'

if (-not (Test-Path -LiteralPath $artifact)) {
    throw "Managed artifact not found: $artifact"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach ($mode in @('Normal', 'SyntheticScheduler')) {
    $modeOutput = Join-Path $output $mode
    & $buildScript -OutputDirectory $modeOutput -Scenario $mode -ManagedArtifact $artifact
    if ($LASTEXITCODE -ne 0) {
        throw "Phase 53W $mode build regression failed: $LASTEXITCODE"
    }
    $efi = Join-Path $modeOutput 'ESP\EFI\BOOT\BOOTX64.EFI'
    $payload = Join-Path $modeOutput 'ESP\GXOS\gxos-managed-entry-probe.dll'
    Write-Output ("PHASE53W_{0}_EFI_SHA256={1}" -f $mode.ToUpperInvariant(),
        (Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash.ToUpperInvariant())
    Write-Output ("PHASE53W_{0}_PAYLOAD_SHA256={1}" -f $mode.ToUpperInvariant(),
        (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant())
}
Write-Output 'PHASE53W_READABLE_RANGE_BUILD_TESTS=PASSED modes=Normal,SyntheticScheduler'

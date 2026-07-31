[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$PositiveEvidenceRoot,
    [Parameter(Mandatory = $true)] [string]$DisabledEvidenceRoot,
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$positive = [IO.Path]::GetFullPath($PositiveEvidenceRoot)
$disabled = [IO.Path]::GetFullPath($DisabledEvidenceRoot)
if (-not (Test-Path -LiteralPath $positive) -or -not (Test-Path -LiteralPath $disabled)) { throw 'Both positive and disabled evidence roots are required.' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $root 'evidence\generated\stricmp-negative-controls-20260731' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$validator = Join-Path $root 'tools\Validate-CrtStricmpEvidence.ps1'

function Copy-Case([string]$name) {
    $path = Join-Path $OutputRoot $name
    if (Test-Path -LiteralPath $path) { throw "Negative-control path already exists: $path" }
    Copy-Item -LiteralPath $positive -Destination $path -Recurse
    return $path
}
function Expect-Rejection([string]$name, [string]$path, [string]$expectedText) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $path -Mode Positive -ExpectedRunCount 3 2>&1
    if ($LASTEXITCODE -eq 0) { throw "Negative control unexpectedly passed: $name" }
    $text = ($output -join "`n")
    if ($text.IndexOf($expectedText, [StringComparison]::Ordinal) -lt 0) { throw "Negative control failed for the wrong reason: $name; expected $expectedText; output $text" }
    Write-Output "CRT_STRICMP_NEGATIVE_${name}=PASS"
}

$disabledOutput = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $disabled -Mode Disabled -ExpectedRunCount 3 2>&1
if ($LASTEXITCODE -ne 0) { throw "Disabled routing control failed: $($disabledOutput -join "`n")" }
Write-Output 'CRT_STRICMP_NEGATIVE_DISABLED_ROUTING=PASS'

$case = Copy-Case 'marker-mutation'
$serial = Join-Path $case 'runs\run-1\serial.log'
$text = [IO.File]::ReadAllText($serial)
$text = $text.Replace('GXOS_NET10:CRT_STRICMP_OK', 'GXOS_NET10:CRT_STRICMP_OX')
[IO.File]::WriteAllText($serial, $text)
Expect-Rejection 'MARKER_MUTATION' $case 'call block count'

$case = Copy-Case 'stale-evidence'
$run = Join-Path $case 'runs\run-1\run.json'
$record = Get-Content -LiteralPath $run -Raw | ConvertFrom-Json
$record.EvidenceId = 'old-evidence'
$record | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $run -Encoding utf8
Expect-Rejection 'STALE_EVIDENCE' $case 'stale evidence ID'

$case = Copy-Case 'duplicate-pid'
$run1 = Join-Path $case 'runs\run-1\run.json'; $run2 = Join-Path $case 'runs\run-2\run.json'
$record1 = Get-Content -LiteralPath $run1 -Raw | ConvertFrom-Json; $record2 = Get-Content -LiteralPath $run2 -Raw | ConvertFrom-Json
$record2.QemuPid = $record1.QemuPid
$record2 | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $run2 -Encoding utf8
Expect-Rejection 'DUPLICATE_PID' $case 'duplicate QEMU process IDs'

$case = Copy-Case 'artifact-hash-mismatch'
$manifestPath = Join-Path $case 'artifact-manifest.json'; $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$artifact = @($manifest.Artifacts | Where-Object Kind -eq 'efi_loader')[0]
$copyPath = Join-Path $case 'mutated-loader.efi'; Copy-Item -LiteralPath $artifact.Path -Destination $copyPath
$bytes = [IO.File]::ReadAllBytes($copyPath); $bytes[0] = $bytes[0] -bxor 0x01; [IO.File]::WriteAllBytes($copyPath, $bytes)
$artifact.Path = $copyPath
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Expect-Rejection 'ARTIFACT_HASH_MISMATCH' $case 'artifact hash, length, or timestamp mismatch: efi_loader'

Write-Output 'CRT_STRICMP_NEGATIVE_EVIDENCE_PIPELINE=PASSED'

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PositiveEvidenceRoot,
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.Path]::GetFullPath($PositiveEvidenceRoot)
if (-not (Test-Path -LiteralPath $source)) { throw "Positive evidence root missing: $source" }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root 'evidence\generated\crt-strlen-negative-controls-20260731'
}
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$validator = Join-Path $root 'tools\Validate-CrtStrlenEvidence.ps1'

function Copy-Case([string]$name) {
    $path = Join-Path $OutputRoot $name
    if (Test-Path -LiteralPath $path) { throw "Negative-control path already exists: $path" }
    Copy-Item -LiteralPath $source -Destination $path -Recurse
    return $path
}
function Expect-Rejection([string]$name, [string]$path, [string]$expectedText) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $path -Mode Positive -ExpectedRunCount 3 2>&1
    if ($LASTEXITCODE -eq 0) { throw "Negative control unexpectedly passed: $name" }
    $text = ($output -join "`n")
    if ($text.IndexOf($expectedText, [StringComparison]::Ordinal) -lt 0) {
        throw "Negative control failed for the wrong reason: $name; expected $expectedText; output $text"
    }
    Write-Output "CRT_STRLEN_NEGATIVE_${name}=PASS"
}

$case = Copy-Case 'marker-mutation'
$serial = Join-Path $case 'runs\run-1\serial.log'
$text = [IO.File]::ReadAllText($serial)
$text = $text.Replace('GXOS_NET10:CRT_STRLEN_OK', 'GXOS_NET10:CRT_STRLEN_OX')
[IO.File]::WriteAllText($serial, $text)
Expect-Rejection 'MARKER_MUTATION' $case 'missing or out of order: GXOS_NET10:CRT_STRLEN_OK'

$case = Copy-Case 'truncated-evidence'
$serial = Join-Path $case 'runs\run-1\serial.log'
$lines = [IO.File]::ReadAllLines($serial)
$lines = @($lines | Where-Object { $_ -ne 'GXOS_NET10:CRT_STRLEN_OK' })
[IO.File]::WriteAllLines($serial, $lines)
Expect-Rejection 'TRUNCATED_EVIDENCE' $case 'missing or out of order: GXOS_NET10:CRT_STRLEN_OK'

$case = Copy-Case 'stale-run-id'
$run = Join-Path $case 'runs\run-1\run.json'
$record = Get-Content -LiteralPath $run -Raw | ConvertFrom-Json
$record.RunId = 'old-evidence-run1'
$record | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $run -Encoding utf8
Expect-Rejection 'STALE_RUN_ID' $case 'stale run ID'

$case = Copy-Case 'duplicate-qemu-process'
$run1 = Join-Path $case 'runs\run-1\run.json'
$run2 = Join-Path $case 'runs\run-2\run.json'
$record1 = Get-Content -LiteralPath $run1 -Raw | ConvertFrom-Json
$record2 = Get-Content -LiteralPath $run2 -Raw | ConvertFrom-Json
$record2.QemuPid = $record1.QemuPid
$record2 | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $run2 -Encoding utf8
Expect-Rejection 'DUPLICATE_QEMU_PROCESS' $case 'duplicate QEMU process IDs'

$case = Copy-Case 'artifact-hash-mismatch'
$manifestPath = Join-Path $case 'artifact-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$artifact = @($manifest.Artifacts | Where-Object Kind -eq 'efi_loader')[0]
$copyPath = Join-Path $case 'mutated-loader.efi'
Copy-Item -LiteralPath $artifact.Path -Destination $copyPath
$bytes = [IO.File]::ReadAllBytes($copyPath)
$bytes[0] = $bytes[0] -bxor 0x01
[IO.File]::WriteAllBytes($copyPath, $bytes)
$artifact.Path = $copyPath
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Expect-Rejection 'ARTIFACT_HASH_MISMATCH' $case 'artifact hash, length, or timestamp mismatch: efi_loader'

Write-Output 'CRT_STRLEN_NEGATIVE_EVIDENCE_PIPELINE=PASSED'

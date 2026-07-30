[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PositiveEvidenceRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$DisabledEvidenceRoot = '',
    [string]$MarkerMutationEvidenceRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.Path]::GetFullPath($PositiveEvidenceRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$validator = Join-Path $root 'tools\Validate-CrtInittermEvidence.ps1'
$sourceRun = Join-Path $source 'runs\run-1'
$sourceManifest = Join-Path $source 'artifact-manifest.json'
$sourceContext = Join-Path $source 'validation-context.json'
if (-not (Test-Path -LiteralPath $sourceRun) -or
    -not (Test-Path -LiteralPath $sourceManifest) -or
    -not (Test-Path -LiteralPath $sourceContext)) {
    throw 'A complete positive evidence root is required.'
}
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Read-Json([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
function Write-Json([string]$path, $value) { $value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8 }
function New-Control([string]$name) {
    $dir = Join-Path $output $name
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'runs\run-1') | Out-Null
    Copy-Item -LiteralPath $sourceManifest -Destination (Join-Path $dir 'artifact-manifest.json')
    Copy-Item -LiteralPath $sourceContext -Destination (Join-Path $dir 'validation-context.json')
    Copy-Item -Path (Join-Path $sourceRun '*') -Destination (Join-Path $dir 'runs\run-1') -Recurse
    $manifest = Read-Json (Join-Path $dir 'artifact-manifest.json')
    $context = Read-Json (Join-Path $dir 'validation-context.json')
    $manifest.EvidenceId = $name; $context.EvidenceId = $name; $context.RunCount = 1
    Write-Json (Join-Path $dir 'artifact-manifest.json') $manifest
    Write-Json (Join-Path $dir 'validation-context.json') $context
    $runPath = Join-Path $dir 'runs\run-1\run.json'
    $run = Read-Json $runPath
    $run.EvidenceId = $name; $run.RunId = "$name-run1"; $run.Sequence = 1
    Write-Json $runPath $run
    return $dir
}
function Refresh-Run([string]$dir) {
    $runPath = Join-Path $dir 'runs\run-1\run.json'
    $run = Read-Json $runPath
    $serialPath = Join-Path $dir ([string]$run.SerialLog)
    $run.FinalSerialLength = [int64](Get-Item -LiteralPath $serialPath).Length
    Write-Json $runPath $run
}
function Expect-Rejection([string]$name, [string]$dir) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $dir -Mode Positive -ExpectedRunCount 1 | Out-Null
    $exit = $LASTEXITCODE
    [PSCustomObject]@{ Control = $name; ExpectedOutcome = 'rejected'; Passed = ($exit -ne 0); ExitCode = $exit }
}
function Expect-Accept([string]$name, [string]$dir) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $dir -Mode Disabled -ExpectedRunCount 1 | Out-Null
    $exit = $LASTEXITCODE
    [PSCustomObject]@{ Control = $name; ExpectedOutcome = 'accepted-disabled-boundary'; Passed = ($exit -eq 0); ExitCode = $exit }
}

$results = New-Object System.Collections.Generic.List[object]

$control = New-Control 'truncated-evidence'
$run = Read-Json (Join-Path $control 'runs\run-1\run.json')
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content -LiteralPath $serialPath -Raw
[IO.File]::WriteAllText($serialPath, $text.Substring(0, [Math]::Max(0, $text.Length - 512)))
Refresh-Run $control
$results.Add((Expect-Rejection 'truncated-evidence' $control))

$control = New-Control 'missing-final-diagnostics'
$run = Read-Json (Join-Path $control 'runs\run-1\run.json')
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content -LiteralPath $serialPath -Raw
$text = [regex]::Replace($text, 'GXOS_NET10:QPC_REGRESSIONS=[^\r\n]*\r?\n?', '')
[IO.File]::WriteAllText($serialPath, $text)
Refresh-Run $control
$results.Add((Expect-Rejection 'missing-final-diagnostics' $control))

$control = New-Control 'stale-run-id'
$runPath = Join-Path $control 'runs\run-1\run.json'
$run = Read-Json $runPath; $run.RunId = 'old-evidence-run1'; Write-Json $runPath $run
$results.Add((Expect-Rejection 'stale-run-id' $control))

$control = New-Control 'duplicate-process'
New-Item -ItemType Directory -Force -Path (Join-Path $control 'runs\run-2') | Out-Null
Copy-Item -Path (Join-Path $control 'runs\run-1\*') -Destination (Join-Path $control 'runs\run-2') -Recurse
$results.Add((Expect-Rejection 'duplicate-process' $control))

$control = New-Control 'hash-mismatch'
$manifestPath = Join-Path $control 'artifact-manifest.json'
$manifest = Read-Json $manifestPath; $manifest.Artifacts[0].Sha256 = ('0' * 64); Write-Json $manifestPath $manifest
$results.Add((Expect-Rejection 'hash-mismatch' $control))

$control = New-Control 'marker-mutation'
$run = Read-Json (Join-Path $control 'runs\run-1\run.json')
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content -LiteralPath $serialPath -Raw
[IO.File]::WriteAllText($serialPath, $text.Replace('GXOS_NET10:CRT_INITTERM_OK', 'GXOS_NET10:CRT_INITTERM_OX'))
Refresh-Run $control
$results.Add((Expect-Rejection 'marker-mutation' $control))

if (-not [string]::IsNullOrWhiteSpace($DisabledEvidenceRoot)) {
    $results.Add((Expect-Accept 'disabled-implementation' ([IO.Path]::GetFullPath($DisabledEvidenceRoot))))
}
if (-not [string]::IsNullOrWhiteSpace($MarkerMutationEvidenceRoot)) {
    $results.Add((Expect-Rejection 'runtime-marker-mutation' ([IO.Path]::GetFullPath($MarkerMutationEvidenceRoot))))
}

$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'negative-control-summary.json') -Encoding utf8
$results | ConvertTo-Json -Depth 8 -Compress
if (@($results | Where-Object { -not $_.Passed }).Count -ne 0) { exit 2 }
Write-Output 'CRT_INITTERM_NEGATIVE_CONTROLS=PASSED'
exit 0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PositiveEvidenceRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$DisabledEvidenceRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.Path]::GetFullPath($PositiveEvidenceRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$validator = Join-Path $root 'tools\Validate-CrtInittermEEvidence.ps1'
$sourceRun = Join-Path $source 'runs\run-1'
$sourceManifest = Join-Path $source 'artifact-manifest.json'
$sourceContext = Join-Path $source 'validation-context.json'
if (-not (Test-Path $sourceRun) -or -not (Test-Path $sourceManifest) -or -not (Test-Path $sourceContext)) { throw 'A complete positive evidence root is required.' }
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Read-Json([string]$path) { Get-Content $path -Raw | ConvertFrom-Json }
function Write-Json([string]$path, $value) { $value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8 }
function New-Control([string]$name) {
    $dir = Join-Path $output $name
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'runs\run-1') | Out-Null
    Copy-Item $sourceManifest (Join-Path $dir 'artifact-manifest.json')
    Copy-Item $sourceContext (Join-Path $dir 'validation-context.json')
    Copy-Item (Join-Path $sourceRun '*') (Join-Path $dir 'runs\run-1') -Recurse
    $manifest = Read-Json (Join-Path $dir 'artifact-manifest.json')
    $context = Read-Json (Join-Path $dir 'validation-context.json')
    $manifest.EvidenceId = $name
    $context.EvidenceId = $name
    Write-Json (Join-Path $dir 'artifact-manifest.json') $manifest
    Write-Json (Join-Path $dir 'validation-context.json') $context
    $runPath = Join-Path $dir 'runs\run-1\run.json'
    $run = Read-Json $runPath
    $run.EvidenceId = $name
    $run.RunId = "$name-run1"
    $run.Sequence = 1
    Write-Json $runPath $run
    return $dir
}
function Refresh-Run([string]$dir) {
    $runPath = Join-Path $dir 'runs\run-1\run.json'
    $run = Read-Json $runPath
    $serialPath = Join-Path $dir ([string]$run.SerialLog)
    $run.FinalSerialLength = [int64](Get-Item $serialPath).Length
    $run.LastObservedGuestMarker = 'control-mutated'
    Write-Json $runPath $run
}
function Expect-Rejection([string]$name, [string]$dir) {
    $stdout = Join-Path $dir 'validator.stdout.log'
    $stderr = Join-Path $dir 'validator.stderr.log'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $dir -Mode Positive -ExpectedRunCount 1 1> $stdout 2> $stderr
    $exit = $LASTEXITCODE
    [PSCustomObject]@{ Control = $name; ExpectedOutcome = 'rejected'; Passed = ($exit -ne 0); ExitCode = $exit }
}
function Expect-Accept([string]$name, [string]$dir) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $dir -Mode Disabled -ExpectedRunCount 1 | Out-Null
    $exit = $LASTEXITCODE
    [PSCustomObject]@{ Control = $name; ExpectedOutcome = 'accepted-disabled-boundary'; Passed = ($exit -eq 0); ExitCode = $exit }
}

$results = New-Object System.Collections.Generic.List[object]

$control = New-Control 'truncated-log'
$run = Read-Json (Join-Path $control 'runs\run-1\run.json')
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content $serialPath -Raw
[IO.File]::WriteAllText($serialPath, $text.Substring(0, [Math]::Max(0, $text.Length - 64)))
Refresh-Run $control
$results.Add((Expect-Rejection 'truncated-evidence' $control))

$control = New-Control 'missing-final-summary'
$run = Read-Json (Join-Path $control 'runs\run-1\run.json')
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content $serialPath -Raw
$text = [regex]::Replace($text, 'GXOS_NET10:QPC_REGRESSIONS=[^\r\n]*\r?\n?', '')
[IO.File]::WriteAllText($serialPath, $text)
Refresh-Run $control
$results.Add((Expect-Rejection 'missing-final-summary' $control))

$control = New-Control 'stale-log'
$runPath = Join-Path $control 'runs\run-1\run.json'
$run = Read-Json $runPath
$run.EvidenceId = 'stale-evidence-id'
Write-Json $runPath $run
$results.Add((Expect-Rejection 'stale-log' $control))

$control = New-Control 'hash-mismatch'
$manifestPath = Join-Path $control 'artifact-manifest.json'
$manifest = Read-Json $manifestPath
$manifest.Artifacts[0].Sha256 = ('0' * 64)
Write-Json $manifestPath $manifest
$results.Add((Expect-Rejection 'hash-mismatch' $control))

$control = New-Control 'duplicate-process-evidence'
New-Item -ItemType Directory -Force -Path (Join-Path $control 'runs\run-2') | Out-Null
Copy-Item (Join-Path $control 'runs\run-1\*') (Join-Path $control 'runs\run-2') -Recurse
$results.Add((Expect-Rejection 'duplicate-process-evidence' $control))

$control = New-Control 'marker-mutation'
$run = Read-Json (Join-Path $control 'runs\run-1\run.json')
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content $serialPath -Raw
[IO.File]::WriteAllText($serialPath, $text.Replace('GXOS_NET10:CRT_INITTERM_E_OK', 'GXOS_NET10:CRT_INITTERM_E_OX'))
Refresh-Run $control
$results.Add((Expect-Rejection 'marker-mutation' $control))

if (-not [string]::IsNullOrWhiteSpace($DisabledEvidenceRoot)) {
    $results.Add((Expect-Accept 'disabled-implementation' ([IO.Path]::GetFullPath($DisabledEvidenceRoot))))
}

$results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'negative-control-summary.json') -Encoding utf8
$results | ConvertTo-Json -Depth 8 -Compress
if (@($results | Where-Object { -not $_.Passed }).Count -ne 0) { exit 2 }
Write-Output 'CRT_INITTERM_E_NEGATIVE_CONTROLS=PASSED'

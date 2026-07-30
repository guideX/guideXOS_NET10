[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot,
    [string]$OutputDirectory = '',
    [string]$DisabledEvidenceRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.Path]::GetFullPath($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $source 'negative-controls' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$validator = Join-Path $root 'tools\Validate-SlistEvidence.ps1'
$sourceRun = Join-Path $source 'runs\run-1'
$sourceManifest = Join-Path $source 'artifact-manifest.json'
if (-not (Test-Path -LiteralPath $sourceRun)) { throw 'A completed positive run-1 is required for evidence controls.' }

function Copy-Control([string]$name) {
    $dir = Join-Path $output $name
    New-Item -ItemType Directory -Force -Path (Join-Path $dir 'runs\run-1') | Out-Null
    Copy-Item -LiteralPath $sourceManifest -Destination (Join-Path $dir 'artifact-manifest.json') -Force
    Copy-Item -LiteralPath (Join-Path $source 'validation-context.json') -Destination (Join-Path $dir 'validation-context.json') -Force
    Copy-Item -Path (Join-Path $sourceRun '*') -Destination (Join-Path $dir 'runs\run-1') -Recurse -Force
    return $dir
}
function Read-Run([string]$dir) { Get-Content (Join-Path $dir 'runs\run-1\run.json') -Raw | ConvertFrom-Json }
function Write-Run([string]$dir, $run) { $run | ConvertTo-Json -Depth 16 | Set-Content (Join-Path $dir 'runs\run-1\run.json') -Encoding utf8 }
function Refresh-Length([string]$dir) {
    $run = Read-Run $dir
    $serial = Join-Path $dir ([string]$run.SerialLog)
    $run.FinalSerialLength = [int64](Get-Item $serial).Length
    $run.LastObservedGuestMarker = 'control-mutated'
    Write-Run $dir $run
}
function Expect-Rejection([string]$name, [string]$dir, [int]$count = 1) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $dir -ExpectedRunCount $count 1> (Join-Path $dir 'validator.stdout.log') 2> (Join-Path $dir 'validator.stderr.log')
    $exit = $LASTEXITCODE
    [PSCustomObject]@{ Control = $name; Rejected = ($exit -ne 0); ExitCode = $exit; Summary = (Join-Path $dir 'validation-summary.json') }
}

$results = New-Object System.Collections.Generic.List[object]

$control = Copy-Control 'truncated-log'
$run = Read-Run $control
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content $serialPath -Raw
[IO.File]::WriteAllText($serialPath, $text.Substring(0, [Math]::Max(0, $text.Length - 64)))
Refresh-Length $control
$results.Add((Expect-Rejection 'truncated-log' $control))

$control = Copy-Control 'missing-final-summary'
$run = Read-Run $control
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content $serialPath -Raw
$text = [regex]::Replace($text, 'GXOS_NET10:QPC_REGRESSIONS=[^\r\n]*\r?\n?', '')
[IO.File]::WriteAllText($serialPath, $text)
Refresh-Length $control
$results.Add((Expect-Rejection 'missing-final-summary' $control))

$control = Copy-Control 'stale-log'
$run = Read-Run $control
$run.EvidenceId = 'stale-evidence-id'
Write-Run $control $run
$results.Add((Expect-Rejection 'stale-log' $control))

$control = Copy-Control 'hash-mismatch'
$manifestPath = Join-Path $control 'artifact-manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$payloadEntry = $manifest.Artifacts | Where-Object Kind -eq 'nativeaot_payload' | Select-Object -First 1
$payloadEntry.Path = Join-Path $root 'artifacts\gate1-final-static\gxos-managed-entry-probe.lib'
$manifest | ConvertTo-Json -Depth 16 | Set-Content $manifestPath -Encoding utf8
$results.Add((Expect-Rejection 'hash-mismatch' $control))

$control = Copy-Control 'duplicate-process-evidence'
New-Item -ItemType Directory -Force -Path (Join-Path $control 'runs\run-2') | Out-Null
Copy-Item -Path (Join-Path $control 'runs\run-1\*') -Destination (Join-Path $control 'runs\run-2') -Recurse -Force
$results.Add((Expect-Rejection 'duplicate-process-evidence' $control 1))

$control = Copy-Control 'marker-mutation'
$run = Read-Run $control
$serialPath = Join-Path $control ([string]$run.SerialLog)
$text = Get-Content $serialPath -Raw
$text = $text.Replace('GXOS_NET10:SLIST_HEAD_INITIALIZED_OK', 'GXOS_NET10:SLIST_HEAD_INITIALIZED_OX')
[IO.File]::WriteAllText($serialPath, $text)
Refresh-Length $control
$results.Add((Expect-Rejection 'marker-mutation' $control))

if (-not [string]::IsNullOrWhiteSpace($DisabledEvidenceRoot)) {
    $disabled = [IO.Path]::GetFullPath($DisabledEvidenceRoot)
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $disabled -ExpectedRunCount 1 1> (Join-Path $output 'disabled-validator.stdout.log') 2> (Join-Path $output 'disabled-validator.stderr.log')
    $disabledExit = $LASTEXITCODE
    $results.Add([PSCustomObject]@{ Control = 'disabled-implementation'; Rejected = ($disabledExit -ne 0); ExitCode = $disabledExit; Summary = (Join-Path $disabled 'validation-summary.json') })
}

$results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'negative-control-summary.json') -Encoding utf8
$results | ConvertTo-Json -Depth 8 -Compress
if (@($results | Where-Object { -not $_.Rejected }).Count -ne 0) { exit 2 }
Write-Output 'SLIST_NEGATIVE_CONTROLS=PASSED'

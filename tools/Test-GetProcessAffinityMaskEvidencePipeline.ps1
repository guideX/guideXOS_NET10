[CmdletBinding()]
param(
    [string]$SourceEvidence = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($SourceEvidence)) { $SourceEvidence = Join-Path $repo 'evidence\generated\getprocessaffinity-final-20260801-immutable-v2' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repo 'evidence\generated\getprocessaffinity-negative-pipeline-20260801-v2' }
$source = [IO.Path]::GetFullPath($SourceEvidence); $output = [IO.Path]::GetFullPath($OutputRoot)
if (-not (Test-Path -LiteralPath $source)) { throw "Source evidence missing: $source" }
if (Test-Path -LiteralPath $output) { throw "Negative pipeline output already exists: $output" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$validator = Join-Path $repo 'tools\Validate-GetProcessAffinityMaskEvidence.ps1'
$cases = @('marker','truncated','stale-run','duplicate-pid','artifact-hash','process-mask','system-mask','caller-system-read','last-error','output-width')
function Clone-Case([string]$name) {
    $path = Join-Path $output $name
    Copy-Item -LiteralPath $source -Destination $path -Recurse
    return $path
}
function Rewrite-Text([string]$path, [scriptblock]$mutation) {
    $text = [IO.File]::ReadAllText($path)
    $text = & $mutation $text
    [IO.File]::WriteAllText($path, [string]$text)
}
foreach ($case in $cases) {
    $caseRoot = Clone-Case $case
    $serial = Join-Path $caseRoot 'runs\run-1\serial.log'
    switch ($case) {
        'marker' { Rewrite-Text $serial { param($t) $t.Replace('GXOS_NET10:GETPROCESSAFFINITYMASK_OK','GXOS_NET10:GETPROCESSAFFINITYMASK_OX') } }
        'truncated' { $t=[IO.File]::ReadAllText($serial); [IO.File]::WriteAllText($serial, $t.Substring(0, [Math]::Min(1000, $t.Length))) }
        'stale-run' { $j=Get-Content -Raw (Join-Path $caseRoot 'runs\run-1\run.json') | ConvertFrom-Json; $j.RunId='stale-run-id'; $j | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $caseRoot 'runs\run-1\run.json') -Encoding utf8 }
        'duplicate-pid' { $j1=Get-Content -Raw (Join-Path $caseRoot 'runs\run-1\run.json') | ConvertFrom-Json; $j2=Get-Content -Raw (Join-Path $caseRoot 'runs\run-2\run.json') | ConvertFrom-Json; $j2.QemuPid=$j1.QemuPid; $j2 | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $caseRoot 'runs\run-2\run.json') -Encoding utf8 }
        'artifact-hash' { $m=Get-Content -Raw (Join-Path $caseRoot 'artifact-manifest.json') | ConvertFrom-Json; $m.Artifacts[0].Sha256=('0'*64); $m | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $caseRoot 'artifact-manifest.json') -Encoding utf8 }
        'process-mask' { Rewrite-Text $serial { param($t) $t.Replace('GETPROCESSAFFINITYMASK_PROCESS_MASK=0x0000000000000001','GETPROCESSAFFINITYMASK_PROCESS_MASK=0x0000000000000002') } }
        'system-mask' { Rewrite-Text $serial { param($t) $t.Replace('GETPROCESSAFFINITYMASK_SYSTEM_MASK=0x0000000000000001','GETPROCESSAFFINITYMASK_SYSTEM_MASK=0x0000000000000002') } }
        'caller-system-read' { Rewrite-Text $serial { param($t) $t.Replace('GETPROCESSAFFINITYMASK_CALLER_SYSTEM_MASK_READ=0x0000000000000000','GETPROCESSAFFINITYMASK_CALLER_SYSTEM_MASK_READ=0x0000000000000001') } }
        'last-error' { Rewrite-Text $serial { param($t) $t.Replace('GETPROCESSAFFINITYMASK_LAST_ERROR_AFTER=0x00000000000000CB','GETPROCESSAFFINITYMASK_LAST_ERROR_AFTER=0x00000000000000CC') } }
        'output-width' { Rewrite-Text $serial { param($t) $t.Replace('GETPROCESSAFFINITYMASK_OUTPUT_WIDTH=0x8','GETPROCESSAFFINITYMASK_OUTPUT_WIDTH=0x4') } }
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $caseRoot -Mode Positive -ExpectedRunCount 3 *> (Join-Path $caseRoot 'negative-validator-output.txt')
    if ($LASTEXITCODE -eq 0) { throw "Negative case unexpectedly passed: $case" }
    Write-Output "GETPROCESSAFFINITYMASK_NEGATIVE_CASE=$case`tREJECTED=PASS"
}
Write-Output 'GETPROCESSAFFINITYMASK_NEGATIVE_PIPELINE=PASSED'
exit 0

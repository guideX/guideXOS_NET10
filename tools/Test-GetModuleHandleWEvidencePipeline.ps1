[CmdletBinding()]
param(
    [string]$EvidenceRoot = '',
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { $EvidenceRoot = Join-Path $root 'evidence\generated\getmodulehandlew-final-20260801-immutable' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'artifacts\getmodulehandlew-negative-controls-20260801' }
$sourceEvidence = [IO.Path]::GetFullPath($EvidenceRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$validator = Join-Path $root 'tools\Validate-GetModuleHandleWEvidence.ps1'
if (-not (Test-Path -LiteralPath $sourceEvidence)) { throw "Positive evidence root missing: $sourceEvidence" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$failures = [System.Collections.Generic.List[string]]::new()

function Update-Run([string]$runDirectory, [string]$serial) {
    $runPath = Join-Path $runDirectory 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $serialPath = Join-Path $runDirectory 'serial.log'
    Set-Content -LiteralPath $serialPath -Value $serial -Encoding utf8 -NoNewline
    $run.FinalSerialLength = (Get-Item -LiteralPath $serialPath).Length
    $run.SerialSha256 = (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $run | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $runPath -Encoding utf8
}
function Expect-Rejected([string]$name, [scriptblock]$mutate) {
    $case = Join-Path $output $name
    Copy-Item -LiteralPath $sourceEvidence -Destination $case -Recurse -Force
    & $mutate $case
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $case -Mode Positive -ExpectedRunCount 3 *> (Join-Path $case 'validator.log')
    if ($LASTEXITCODE -eq 0) { [void]$failures.Add("$name was accepted") }
    else { Write-Output "NEGATIVE_$($name.ToUpperInvariant())=REJECTED" }
}

Expect-Rejected 'marker-mutation' {
    param($case)
    $run = Join-Path $case 'runs\run-1'
    $serial = Get-Content -Raw -LiteralPath (Join-Path $run 'serial.log')
    Update-Run $run ($serial.Replace('GETMODULEHANDLEW_STATUS=MODULE_NOT_FOUND','GETMODULEHANDLEW_STATUS=MODULE_OX'))
}
Expect-Rejected 'truncated-evidence' {
    param($case)
    $run = Join-Path $case 'runs\run-1'
    $serial = Get-Content -Raw -LiteralPath (Join-Path $run 'serial.log')
    Update-Run $run $serial.Substring(0, [Math]::Max(0, $serial.Length - 120))
}
Expect-Rejected 'stale-run-id' {
    param($case)
    $path = Join-Path $case 'runs\run-1\run.json'
    $run = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    $run.RunId = 'stale-run-id'
    $run | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8
}
Expect-Rejected 'duplicate-pid' {
    param($case)
    $first = Get-Content -Raw -LiteralPath (Join-Path $case 'runs\run-1\run.json') | ConvertFrom-Json
    $path = Join-Path $case 'runs\run-2\run.json'
    $second = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    $second.QemuPid = $first.QemuPid
    $second | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8
}
Expect-Rejected 'artifact-hash-mismatch' {
    param($case)
    $path = Join-Path $case 'artifact-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    $manifest.Artifacts[0].Sha256 = ('0' * 64)
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8
}
if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 2
}
Write-Output 'GETMODULEHANDLEW_NEGATIVE_CONTROLS=PASSED'

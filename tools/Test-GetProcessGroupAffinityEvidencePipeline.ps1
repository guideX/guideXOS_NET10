[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.Path]::GetFullPath($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repo 'artifacts\getprocessgroup-negative-controls-20260801' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Negative-control output already exists: $output" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$validator = Join-Path $repo 'tools\Validate-GetProcessGroupAffinityEvidence.ps1'
$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { [void]$failures.Add($message) }
function New-Case([string]$name) {
    $case = Join-Path $output $name
    Copy-Item -LiteralPath $source -Destination $case -Recurse
    return $case
}
function Read-Run([string]$case, [int]$sequence = 1) {
    return Get-Content -Raw -LiteralPath (Join-Path $case "runs\run-$sequence\run.json") | ConvertFrom-Json
}
function Write-Run([string]$case, $run, [int]$sequence = 1) {
    $run | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $case "runs\run-$sequence\run.json") -Encoding utf8
}
function Replace-Serial([string]$case, [string]$old, [string]$new) {
    $path = Join-Path $case 'runs\run-1\serial.log'
    $text = [IO.File]::ReadAllText($path)
    if (-not $text.Contains($old)) { throw "Mutation marker missing: $old" }
    [IO.File]::WriteAllText($path, $text.Replace($old, $new))
}
function Expect-Rejection([string]$name, [scriptblock]$mutation) {
    $case = New-Case $name
    & $mutation $case
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $case -Mode Positive -ExpectedRunCount 3 *> (Join-Path $case 'validator.log')
    $exit = $LASTEXITCODE
    if ($exit -eq 0) { Fail "$name was accepted" } else { Write-Output "PROCESS_GROUP_NEGATIVE_CONTROL_$($name)=PASS" }
}

Expect-Rejection 'marker-mutation' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETPROCESSGROUPAFFINITY_INSUFFICIENT_BUFFER_OK' 'GXOS_NET10:GETPROCESSGROUPAFFINITY_OX'
}
Expect-Rejection 'truncated-evidence' {
    param($case)
    $path = Join-Path $case 'runs\run-1\serial.log'
    $text = [IO.File]::ReadAllText($path)
    [IO.File]::WriteAllText($path, $text.Substring(0, $text.Length - 500))
}
Expect-Rejection 'stale-run-id' {
    param($case)
    $run = Read-Run $case
    $run.RunId = 'stale-run-id'
    Write-Run $case $run
}
Expect-Rejection 'duplicate-pid' {
    param($case)
    $run1 = Read-Run $case 1
    $run2 = Read-Run $case 2
    $run2.QemuPid = $run1.QemuPid
    Write-Run $case $run2 2
}
Expect-Rejection 'artifact-hash-mismatch' {
    param($case)
    $manifestPath = Join-Path $case 'artifact-manifest.json'
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    $manifest.Artifacts[0].Sha256 = ('0' * 64)
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}
Expect-Rejection 'capacity-result-mutation' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT=0x0000000000000001' 'GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT=0x0000000000000002'
}
Expect-Rejection 'last-error-mutation' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETPROCESSGROUPAFFINITY_LAST_ERROR_AFTER=0x000000000000007A' 'GXOS_NET10:GETPROCESSGROUPAFFINITY_LAST_ERROR_AFTER=0x0000000000000000'
}

if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Output "PROCESS_GROUP_NEGATIVE_CONTROL_FAILURE=$_" }
    exit 2
}
Write-Output 'PROCESS_GROUP_NEGATIVE_CONTROLS=PASSED'

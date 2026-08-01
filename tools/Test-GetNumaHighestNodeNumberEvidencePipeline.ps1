[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.Path]::GetFullPath($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repo 'artifacts\getnumahighest-negative-controls-20260801' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Negative-control output already exists: $output" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$validator = Join-Path $repo 'tools\Validate-GetNumaHighestNodeNumberEvidence.ps1'
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
    if ($exit -eq 0) { Fail "$name was accepted" } else { Write-Output "NUMA_NEGATIVE_CONTROL_$($name)=PASS" }
}

Expect-Rejection 'marker-mutation' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETNUMAHIGHESTNODE_OK' 'GXOS_NET10:GETNUMAHIGHESTNODE_OX'
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
Expect-Rejection 'highest-node-count-confusion' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETNUMAHIGHESTNODE_HIGHEST_NODE=0x0000000000000000' 'GXOS_NET10:GETNUMAHIGHESTNODE_HIGHEST_NODE=0x0000000000000001'
}
Expect-Rejection 'zero-node-confusion' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETNUMAHIGHESTNODE_CALLER_BRANCH=SUCCESS_BOOLEAN_OUTPUT_ZERO_NON_NUMA_FALLBACK' 'GXOS_NET10:GETNUMAHIGHESTNODE_CALLER_BRANCH=ZERO_NODES'
}
Expect-Rejection 'success-without-output-write' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_AFTER=0x0000000000000000' 'GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_AFTER=0x00000000A5A5A5A5'
}
Expect-Rejection 'failure-with-claimed-output' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETNUMAHIGHESTNODE_BOOLEAN_RESULT=0x0000000000000001' 'GXOS_NET10:GETNUMAHIGHESTNODE_BOOLEAN_RESULT=0x0000000000000000'
}
Expect-Rejection 'wrong-output-width' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_WIDTH=0x0000000000000004' 'GXOS_NET10:GETNUMAHIGHESTNODE_OUTPUT_WIDTH=0x0000000000000008'
}
Expect-Rejection 'unexpected-last-error' {
    param($case)
    Replace-Serial $case 'GXOS_NET10:GETNUMAHIGHESTNODE_LAST_ERROR_AFTER=0x00000000000000CB' 'GXOS_NET10:GETNUMAHIGHESTNODE_LAST_ERROR_AFTER=0x0000000000000000'
}

if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Output "NUMA_NEGATIVE_CONTROL_FAILURE=$_" }
    exit 2
}
Write-Output 'NUMA_NEGATIVE_CONTROLS=PASSED'

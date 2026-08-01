[CmdletBinding()]
param(
    [string]$EvidenceRoot = '',
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { $EvidenceRoot = Join-Path $root 'evidence\generated\queryjobobject-final-20260801' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root 'artifacts\queryjobobject-negative-controls-20260801' }
$sourceEvidence = [IO.Path]::GetFullPath($EvidenceRoot)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$validator = Join-Path $root 'tools\Validate-QueryInformationJobObjectEvidence.ps1'
$source = Join-Path $root 'src\Gate4Harness\platform_query_information_job_object.c'
$include = Join-Path $root 'src\Gate4Harness'
$gcc = Get-Command gcc -ErrorAction Stop
if (-not (Test-Path -LiteralPath $sourceEvidence)) { throw "Positive evidence root missing: $sourceEvidence" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$failures = [System.Collections.Generic.List[string]]::new()
function Expect-Rejected([string]$name, [scriptblock]$mutate) {
    $case = Join-Path $output $name
    Copy-Item -LiteralPath $sourceEvidence -Destination $case -Recurse
    & $mutate $case
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $case -Mode Positive -ExpectedRunCount 3 *> (Join-Path $case 'validator.log')
    if ($LASTEXITCODE -eq 0) { [void]$failures.Add("$name was accepted") } else { Write-Output "NEGATIVE_$($name.ToUpperInvariant())=REJECTED" }
}
function Update-Run([string]$runDirectory, [string]$serial) {
    $runPath = Join-Path $runDirectory 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $serialPath = Join-Path $runDirectory 'serial.log'
    Set-Content -LiteralPath $serialPath -Value $serial -Encoding utf8 -NoNewline
    $run.FinalSerialLength = (Get-Item -LiteralPath $serialPath).Length
    $run.SerialSha256 = (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $run | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $runPath -Encoding utf8
}

$wrongLayoutLog = Join-Path $output 'wrong-layout-build.log'
$wrongLayoutErrorLog = Join-Path $output 'wrong-layout-build.stderr.log'
$savedErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
& $gcc.Source '-std=c11' '-Wall' '-Wextra' '-Werror' '-O2' '-fno-builtin' '-ffreestanding' '-DGXOS_QUERY_JOB_WRONG_LAYOUT' '-I' $include '-c' $source '-o' (Join-Path $output 'wrong-layout.o') 1> $wrongLayoutLog 2> $wrongLayoutErrorLog
$ErrorActionPreference = $savedErrorActionPreference
if ($LASTEXITCODE -eq 0) { [void]$failures.Add('wrong layout compiled') } else { Write-Output 'NEGATIVE_WRONG_LAYOUT=REJECTED' }

Expect-Rejected 'marker-mutation' {
    param($case)
    $runDirectory = Join-Path $case 'runs\run-1'
    $serial = Get-Content -Raw -LiteralPath (Join-Path $runDirectory 'serial.log')
    Update-Run $runDirectory ($serial.Replace('QUERYJOBOBJECT_EXPECTED_NO_ASSOCIATED_JOB_FAILURE','QUERYJOBOBJECT_OX'))
}
Expect-Rejected 'truncated-evidence' {
    param($case)
    $runDirectory = Join-Path $case 'runs\run-1'
    $serial = Get-Content -Raw -LiteralPath (Join-Path $runDirectory 'serial.log')
    Update-Run $runDirectory $serial.Substring(0, [Math]::Max(0, $serial.Length - 120))
}
Expect-Rejected 'stale-run-id' {
    param($case)
    $runPath = Join-Path $case 'runs\run-1\run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $run.RunId = 'stale-run-id'
    $run | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $runPath -Encoding utf8
}
Expect-Rejected 'duplicate-pid' {
    param($case)
    $first = Get-Content -Raw -LiteralPath (Join-Path $case 'runs\run-1\run.json') | ConvertFrom-Json
    $secondPath = Join-Path $case 'runs\run-2\run.json'
    $second = Get-Content -Raw -LiteralPath $secondPath | ConvertFrom-Json
    $second.QemuPid = $first.QemuPid
    $second | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $secondPath -Encoding utf8
}
Expect-Rejected 'artifact-hash-mismatch' {
    param($case)
    $manifestPath = Join-Path $case 'artifact-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $manifest.Artifacts[0].Sha256 = ('0' * 64)
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 2
}
Write-Output 'QUERYINFORMATIONJOBOBJECT_NEGATIVE_CONTROLS=PASSED'

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$source = [IO.Path]::GetFullPath($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\register-onexit-negative-controls-20260802'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$validator = Join-Path $root 'tools\Validate-RegisterOnexitEvidence.ps1'
if (-not (Test-Path -LiteralPath $source)) { throw "Positive evidence root missing: $source" }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$failures = [System.Collections.Generic.List[string]]::new()

function Refresh-Run([string]$runDirectory) {
    $runPath = Join-Path $runDirectory 'run.json'
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $serialPath = Join-Path $runDirectory 'serial.log'
    $run.FinalSerialLength = [int64](Get-Item -LiteralPath $serialPath).Length
    $run.SerialSha256 = (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $run | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $runPath -Encoding utf8
}
function New-Case([string]$name) {
    $case = Join-Path $output $name
    Copy-Item -LiteralPath $source -Destination $case -Recurse -Force
    return $case
}
function Expect-Rejected([string]$name, [scriptblock]$mutate) {
    $case = New-Case $name
    & $mutate $case
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $case -Mode Positive -ExpectedRunCount 3 *> (Join-Path $case 'validator.log')
    if ($LASTEXITCODE -eq 0) {
        [void]$failures.Add("$name was accepted")
    } else {
        Write-Output "REGISTER_ONEXIT_NEGATIVE_$($name.ToUpperInvariant())=REJECTED"
    }
}

Expect-Rejected 'status-mutation' {
    param($case)
    $serialPath = Join-Path $case 'runs\run-1\serial.log'
    $serial = Get-Content -Raw -LiteralPath $serialPath
    Set-Content -LiteralPath $serialPath -Value ($serial.Replace('REGISTER_ONEXIT_STATUS=OK', 'REGISTER_ONEXIT_STATUS=GROWTH_REQUIRED')) -Encoding utf8 -NoNewline
    Refresh-Run (Join-Path $case 'runs\run-1')
}
Expect-Rejected 'raw-field-mutation' {
    param($case)
    $serialPath = Join-Path $case 'runs\run-1\serial.log'
    $serial = Get-Content -Raw -LiteralPath $serialPath
    Set-Content -LiteralPath $serialPath -Value ($serial.Replace('REGISTER_ONEXIT_TABLE_UNCHANGED=0x0000000000000000', 'REGISTER_ONEXIT_TABLE_UNCHANGED=0x0000000000000001')) -Encoding utf8 -NoNewline
    Refresh-Run (Join-Path $case 'runs\run-1')
}
Expect-Rejected 'truncated-evidence' {
    param($case)
    $serialPath = Join-Path $case 'runs\run-1\serial.log'
    $serial = Get-Content -Raw -LiteralPath $serialPath
    Set-Content -LiteralPath $serialPath -Value $serial.Substring(0, [Math]::Min(1024, $serial.Length)) -Encoding utf8 -NoNewline
    Refresh-Run (Join-Path $case 'runs\run-1')
}
Expect-Rejected 'duplicate-pid' {
    param($case)
    $run1Path = Join-Path $case 'runs\run-1\run.json'
    $run2Path = Join-Path $case 'runs\run-2\run.json'
    $run1 = Get-Content -Raw -LiteralPath $run1Path | ConvertFrom-Json
    $run2 = Get-Content -Raw -LiteralPath $run2Path | ConvertFrom-Json
    $run2.QemuPid = $run1.QemuPid
    $run2 | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $run2Path -Encoding utf8
}
Expect-Rejected 'artifact-hash-mismatch' {
    param($case)
    $manifestPath = Join-Path $case 'artifact-manifest.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $manifest.Artifacts[0].Sha256 = ('0' * 64)
    $manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot=$source; Failures=@($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
Write-Output 'REGISTER_ONEXIT_NEGATIVE_CONTROLS=PASSED'

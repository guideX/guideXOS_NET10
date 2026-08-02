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
    $OutputDirectory = Join-Path $root 'artifacts\getprocaddress-negative-controls-20260801'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
$validator = Join-Path $root 'tools\Validate-GetProcAddressEvidence.ps1'
if (-not (Test-Path -LiteralPath $source)) { throw "Positive evidence root missing: $source" }
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
    Copy-Item -LiteralPath $source -Destination $case -Recurse -Force
    & $mutate $case
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validator -EvidenceRoot $case -Mode Positive -ExpectedRunCount 3 *> (Join-Path $case 'validator.log')
    if ($LASTEXITCODE -eq 0) { [void]$failures.Add("$name was accepted") }
    else { Write-Output "GETPROCADDRESS_NEGATIVE_$($name.ToUpperInvariant())=REJECTED" }
}

Expect-Rejected 'error-marker-mutation' {
    param($case)
    $run = Join-Path $case 'runs\run-1'
    $serial = Get-Content -Raw -LiteralPath (Join-Path $run 'serial.log')
    Update-Run $run ($serial.Replace('GETPROCADDRESS_LAST_ERROR_AFTER=0x000000000000007F', 'GETPROCADDRESS_LAST_ERROR_AFTER=0x0000000000000006'))
}
Expect-Rejected 'boundary-mutation' {
    param($case)
    $run = Join-Path $case 'runs\run-1'
    $serial = Get-Content -Raw -LiteralPath (Join-Path $run 'serial.log')
    Update-Run $run ($serial.Replace('UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function', 'UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetProcAddress'))
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
Expect-Rejected 'export-attempt-mutation' {
    param($case)
    $run = Join-Path $case 'runs\run-1'
    $serial = Get-Content -Raw -LiteralPath (Join-Path $run 'serial.log')
    Update-Run $run ($serial.Replace('GETPROCADDRESS_EXPORT_LOOKUP_ATTEMPTED=0x0000000000000000', 'GETPROCADDRESS_EXPORT_LOOKUP_ATTEMPTED=0x0000000000000001'))
}

if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 2
}
Write-Output 'GETPROCADDRESS_NEGATIVE_CONTROLS=PASSED'
exit 0

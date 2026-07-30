[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot,
    [int]$ExpectedRunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
$manifestPath = Join-Path $evidence 'artifact-manifest.json'
$contextPath = Join-Path $evidence 'validation-context.json'
$runsRoot = Join-Path $evidence 'runs'
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure([string]$message) { [void]$failures.Add($message) }
function Read-Json([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
function Has-Text([string]$text, [string]$value) { return $text.Contains($value) }
function Find-Next([string]$text, [string]$marker, [int]$start) { return $text.IndexOf($marker, $start, [StringComparison]::Ordinal) }
function Read-Hex([string]$text, [string]$marker) {
    $match = [regex]::Match($text, [regex]::Escape($marker) + '0x([0-9A-Fa-f]+)')
    if (-not $match.Success) { return $null }
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}
function Get-Relative([string]$path) { return $path.Substring($evidence.Length).TrimStart('\', '/') }

if (-not (Test-Path -LiteralPath $manifestPath)) { Add-Failure 'artifact-manifest.json is missing' }
if (-not (Test-Path -LiteralPath $contextPath)) { Add-Failure 'validation-context.json is missing' }
if (-not (Test-Path -LiteralPath $runsRoot)) { Add-Failure 'runs directory is missing' }
if ($failures.Count -eq 0) {
    $manifest = Read-Json $manifestPath
    $context = Read-Json $contextPath
    $runDirectories = @(Get-ChildItem -LiteralPath $runsRoot -Directory | Sort-Object Name)
    if ($runDirectories.Count -ne $ExpectedRunCount) { Add-Failure ("expected {0} run directories, found {1}" -f $ExpectedRunCount,$runDirectories.Count) }
    $runRecords = @()
    foreach ($entry in $manifest.Artifacts) {
        if (-not (Test-Path -LiteralPath $entry.Path)) { Add-Failure "artifact missing: $($entry.Kind)"; continue }
        $item = Get-Item -LiteralPath $entry.Path
        $hash = (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne $entry.Sha256) { Add-Failure "artifact hash mismatch: $($entry.Kind)" }
        if ([int64]$item.Length -ne [int64]$entry.Length) { Add-Failure "artifact length mismatch: $($entry.Kind)" }
        if ($item.LastWriteTimeUtc.ToString('o') -ne $entry.LastWriteTimeUtc) { Add-Failure "artifact timestamp mismatch: $($entry.Kind)" }
    }
    foreach ($directory in $runDirectories) {
        $runPath = Join-Path $directory.FullName 'run.json'
        if (-not (Test-Path -LiteralPath $runPath)) { Add-Failure "run metadata missing: $($directory.Name)"; continue }
        $run = Read-Json $runPath
        $runRecords += $run
        if ($run.EvidenceId -ne $manifest.EvidenceId -or $run.EvidenceId -ne $context.EvidenceId) { Add-Failure "stale evidence id: $($run.RunId)" }
        if ($run.Mode -ne $manifest.Mode) { Add-Failure "mode mismatch: $($run.RunId)" }
        foreach ($property in @('SerialLog','QemuStdoutLog','QemuStderrLog','HarnessEventLog','VarsPath')) {
            $relative = [string]$run.$property
            $path = Join-Path $evidence $relative
            if (-not (Test-Path -LiteralPath $path)) { Add-Failure "missing $property for $($run.RunId)" }
        }
        if (-not $run.CleanupComplete) { Add-Failure "QEMU cleanup incomplete: $($run.RunId)" }
        if ($null -eq $run.ArtifactSetAfterRun) {
            Add-Failure "per-run artifact snapshot is missing: $($run.RunId)"
        } else {
            foreach ($entry in $manifest.Artifacts) {
                $after = @($run.ArtifactSetAfterRun | Where-Object { $_.Kind -eq $entry.Kind } | Select-Object -First 1)
                if ($after.Count -ne 1) {
                    Add-Failure "per-run artifact snapshot is missing $($entry.Kind): $($run.RunId)"
                    continue
                }
                if ($after[0].Sha256 -ne $entry.Sha256 -or
                    [int64]$after[0].Length -ne [int64]$entry.Length -or
                    $after[0].LastWriteTimeUtc -ne $entry.LastWriteTimeUtc) {
                    Add-Failure "per-run artifact snapshot mismatch for $($entry.Kind): $($run.RunId)"
                }
            }
        }
        $serialPath = Join-Path $evidence ([string]$run.SerialLog)
        if (-not (Test-Path -LiteralPath $serialPath)) { continue }
        $text = Get-Content -LiteralPath $serialPath -Raw
        $item = Get-Item -LiteralPath $serialPath
        if ([int64]$item.Length -ne [int64]$run.FinalSerialLength) { Add-Failure "serial length changed after capture: $($run.RunId)" }
        $eventsPath = Join-Path $evidence ([string]$run.HarnessEventLog)
        if (@(Get-Content -LiteralPath $eventsPath).Count -lt 2) { Add-Failure "harness event log is incomplete: $($run.RunId)" }
        $mode = [string]$run.Mode
        $expectedFunctional = if ($mode -eq 'Positive') { 23 } else { 22 }
        $expectedFailfast = if ($mode -eq 'Positive') { 101 } else { 102 }
        if (-not (Has-Text $text "GXOS_NET10:PE_IMPORT_FUNCTIONAL=$expectedFunctional")) { Add-Failure "functional import count missing for $($run.RunId)" }
        if (-not (Has-Text $text "GXOS_NET10:PE_IMPORT_FAILFAST=$expectedFailfast")) { Add-Failure "fail-fast import count missing for $($run.RunId)" }
        if (-not (Has-Text $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=0')) { Add-Failure "unresolved import count missing for $($run.RunId)" }
        foreach ($marker in @('GXOS_NET10:GC_STARTUP_BEGIN','GXOS_NET10:NATIVEAOT_STARTUP_BEGIN','GXOS_NET10:TIME_API_ENTER','GXOS_NET10:FILETIME_CONVERSION_OK=0x','GXOS_NET10:QPC_CALL','GXOS_NET10:QPC_OK=0x','GXOS_NET10:PERF_SOURCE_INIT_OK','GXOS_NET10:PERF_FREQUENCY=0x','GXOS_NET10:TIME_API_RETURN=0x','GXOS_NET10:TIME_CONSUMER_PHASE=0x18','GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000','GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000','GXOS_NET10:MANAGED_THREAD_REGISTERED=0','GXOS_NET10:ALLOCATION_CONTEXT_VALID=0','GXOS_NET10:QPC_COUNT=0x','GXOS_NET10:QPC_FIRST=0x','GXOS_NET10:QPC_LAST=0x','GXOS_NET10:QPC_MIN_DELTA=0x','GXOS_NET10:QPC_MAX_DELTA=0x','GXOS_NET10:QPC_REGRESSIONS=0x0000000000000000')) {
            if (-not (Has-Text $text $marker)) { Add-Failure "required marker missing for $($run.RunId): $marker" }
        }
        if (Has-Text $text 'GXOS_NET10:GC_STARTUP_ADVANCED' -or Has-Text $text 'GXOS_NET10:FIRST_ALLOCATION_OK' -or Has-Text $text 'GXOS_NET10:FAULT_') { Add-Failure "forbidden startup marker present for $($run.RunId)" }
        $summaryStart = $text.IndexOf('GXOS_NET10:QPC_COUNT=', [StringComparison]::Ordinal)
        $regressionStart = $text.IndexOf('GXOS_NET10:QPC_REGRESSIONS=', [StringComparison]::Ordinal)
        if ($summaryStart -lt 0 -or $regressionStart -lt $summaryStart) { Add-Failure "final QPC summary is absent or out of order for $($run.RunId)" }
        $qpcCount = Read-Hex $text 'GXOS_NET10:QPC_COUNT='
        $qpcFirst = Read-Hex $text 'GXOS_NET10:QPC_FIRST='
        $qpcLast = Read-Hex $text 'GXOS_NET10:QPC_LAST='
        $qpcMin = Read-Hex $text 'GXOS_NET10:QPC_MIN_DELTA='
        $qpcMax = Read-Hex $text 'GXOS_NET10:QPC_MAX_DELTA='
        if ($null -eq $qpcCount -or $qpcCount -lt 1 -or $null -eq $qpcFirst -or $null -eq $qpcLast -or $null -eq $qpcMin -or $null -eq $qpcMax) { Add-Failure "QPC summary values are incomplete for $($run.RunId)" }
        if ($mode -eq 'Positive') {
            $ordered = @('GXOS_NET10:NATIVEAOT_STARTUP_BEGIN','GXOS_NET10:TIME_API_ENTER','GXOS_NET10:FILETIME_CONVERSION_OK=0x','GXOS_NET10:QPC_CALL','GXOS_NET10:QPC_OK=0x','GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK','GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK','GXOS_NET10:SLIST_IMPORT_FUNCTIONAL=1','GXOS_NET10:SLIST_HEAD_INIT_CALL=0x','GXOS_NET10:SLIST_HEAD_ADDRESS=0x','GXOS_NET10:SLIST_HEAD_ALIGNMENT=0x0000000000000000','GXOS_NET10:SLIST_HEAD_INITIALIZED_COUNT=0x0000000000000001','GXOS_NET10:SLIST_HEAD_INITIALIZED_OK','GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e')
            $cursor = -1
            foreach ($marker in $ordered) { $cursor = Find-Next $text $marker ($cursor + 1); if ($cursor -lt 0) { Add-Failure "marker sequence missing/out of order for $($run.RunId): $marker"; break } }
            $boundaryIndex = $text.IndexOf('GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e', [StringComparison]::Ordinal)
            if ($boundaryIndex -lt 0 -or $summaryStart -le $boundaryIndex) { Add-Failure "final QPC summary does not follow _initterm_e boundary for $($run.RunId)" }
            if (Has-Text $text 'GXOS_NET10:SLIST_HEAD_INITIALIZED_OX') { Add-Failure "mutated SLIST marker present for $($run.RunId)" }
        } else {
            if (Has-Text $text 'GXOS_NET10:SLIST_IMPORT_FUNCTIONAL=1' -or Has-Text $text 'GXOS_NET10:SLIST_HEAD_INITIALIZED_OK') { Add-Failure "disabled control reached functional SLIST marker: $($run.RunId)" }
            $ordered = @('GXOS_NET10:NATIVEAOT_STARTUP_BEGIN','GXOS_NET10:TIME_API_ENTER','GXOS_NET10:FILETIME_CONVERSION_OK=0x','GXOS_NET10:QPC_CALL','GXOS_NET10:QPC_OK=0x','GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK','GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK','GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!InitializeSListHead')
            $cursor = -1
            foreach ($marker in $ordered) { $cursor = Find-Next $text $marker ($cursor + 1); if ($cursor -lt 0) { Add-Failure "disabled marker sequence missing/out of order for $($run.RunId): $marker"; break } }
            if (Has-Text $text 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e') { Add-Failure "disabled control advanced beyond SLIST boundary: $($run.RunId)" }
        }
    }
    $runIds = @($runRecords | ForEach-Object { [string]$_.RunId })
    if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Add-Failure 'duplicate process/run evidence detected' }
    $processKeys = @($runRecords | ForEach-Object { "$( [int]$_.QemuPid )|$( [string]$_.QemuStartUtc )" })
    if (@($processKeys | Select-Object -Unique).Count -ne $processKeys.Count) { Add-Failure 'duplicate QEMU process evidence detected' }
    $sequences = @($runRecords | ForEach-Object { [int]$_.Sequence } | Sort-Object)
    $expectedSequences = @(1..$ExpectedRunCount)
    if (($sequences -join ',') -ne ($expectedSequences -join ',')) { Add-Failure 'run sequence is not exactly consecutive' }
}

$summary = [ordered]@{
    EvidenceRoot = $evidence
    ExpectedRunCount = $ExpectedRunCount
    RunCount = if (Test-Path -LiteralPath $runsRoot) { @(Get-ChildItem -LiteralPath $runsRoot -Directory).Count } else { 0 }
    Passed = ($failures.Count -eq 0)
    Failures = @($failures)
}
$summary | ConvertTo-Json -Depth 8 -Compress
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'validation-summary.json') -Encoding utf8
if ($failures.Count -ne 0) { exit 2 }
Write-Output 'SLIST_EVIDENCE_VALIDATION=PASSED'

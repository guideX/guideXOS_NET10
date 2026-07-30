[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled')]
    [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$evidence = [IO.Path]::GetFullPath($EvidenceRoot)
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

$manifestPath = Join-Path $evidence 'artifact-manifest.json'
$contextPath = Join-Path $evidence 'validation-context.json'
$runsRoot = Join-Path $evidence 'runs'
if (-not (Test-Path $manifestPath)) { Add-Failure 'artifact manifest missing' }
if (-not (Test-Path $contextPath)) { Add-Failure 'validation context missing' }
if (-not (Test-Path $runsRoot)) { Add-Failure 'runs directory missing' }

$runRecords = @()
if ($failures.Count -eq 0) {
    $manifest = Read-Json $manifestPath
    $context = Read-Json $contextPath
    if ($manifest.Mode -ne $Mode -or $context.Mode -ne $Mode) { Add-Failure 'evidence mode mismatch' }
    $runDirectories = @(Get-ChildItem -LiteralPath $runsRoot -Directory | Sort-Object Name)
    if ($runDirectories.Count -ne $ExpectedRunCount) { Add-Failure "expected $ExpectedRunCount run directories, found $($runDirectories.Count)" }

    foreach ($artifact in $manifest.Artifacts) {
        if (-not (Test-Path -LiteralPath $artifact.Path)) { Add-Failure "artifact missing: $($artifact.Kind)"; continue }
        $item = Get-Item -LiteralPath $artifact.Path
        $hash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne $artifact.Sha256) { Add-Failure "artifact hash mismatch: $($artifact.Kind)" }
        if ([int64]$item.Length -ne [int64]$artifact.Length) { Add-Failure "artifact length mismatch: $($artifact.Kind)" }
        if ($item.LastWriteTimeUtc.ToString('o') -ne $artifact.LastWriteTimeUtc) { Add-Failure "artifact timestamp mismatch: $($artifact.Kind)" }
    }

    foreach ($directory in $runDirectories) {
        $runPath = Join-Path $directory.FullName 'run.json'
        if (-not (Test-Path $runPath)) { Add-Failure "run metadata missing: $($directory.Name)"; continue }
        $run = Read-Json $runPath
        $runRecords += $run
        if ($run.EvidenceId -ne $manifest.EvidenceId -or $run.EvidenceId -ne $context.EvidenceId) { Add-Failure "stale evidence id: $($run.RunId)" }
        if ($run.Mode -ne $Mode -or -not $run.CleanupComplete) { Add-Failure "run lifecycle/mode invalid: $($run.RunId)" }
        foreach ($property in @('SerialLog','QemuStdoutLog','QemuStderrLog','HarnessEventLog','VarsPath')) {
            if (-not (Test-Path (Join-Path $evidence ([string]$run.$property)))) { Add-Failure "missing ${property}: $($run.RunId)" }
        }
        $serialPath = Join-Path $evidence ([string]$run.SerialLog)
        if (-not (Test-Path $serialPath)) { continue }
        $text = Get-Content -LiteralPath $serialPath -Raw
        $serialItem = Get-Item -LiteralPath $serialPath
        if ([int64]$serialItem.Length -ne [int64]$run.FinalSerialLength) { Add-Failure "serial changed after capture: $($run.RunId)" }
        $eventsPath = Join-Path $evidence ([string]$run.HarnessEventLog)
        if (@(Get-Content -LiteralPath $eventsPath).Count -lt 2) { Add-Failure "lifecycle log incomplete: $($run.RunId)" }
        $expectedFunctional = if ($Mode -eq 'Positive') { 24 } else { 23 }
        $expectedFailfast = if ($Mode -eq 'Positive') { 100 } else { 101 }
        if (-not (Has-Text $text "GXOS_NET10:PE_IMPORT_FUNCTIONAL=$expectedFunctional")) { Add-Failure "functional count invalid: $($run.RunId)" }
        if (-not (Has-Text $text "GXOS_NET10:PE_IMPORT_FAILFAST=$expectedFailfast")) { Add-Failure "fail-fast count invalid: $($run.RunId)" }
        foreach ($marker in @('GXOS_NET10:LOADER_START','GXOS_NET10:PE_READ_OK','GXOS_NET10:PE_RELOCATIONS_OK','GXOS_NET10:GC_STARTUP_BEGIN','GXOS_NET10:NATIVEAOT_STARTUP_BEGIN','GXOS_NET10:TIME_API_ENTER','GXOS_NET10:FILETIME_CONVERSION_OK=0x','GXOS_NET10:QPC_CALL','GXOS_NET10:QPC_OK=0x','GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK','GXOS_NET10:SLIST_HEAD_INITIALIZED_OK','GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=0','GXOS_NET10:MANAGED_THREAD_REGISTERED=0','GXOS_NET10:ALLOCATION_CONTEXT_VALID=0','GXOS_NET10:QPC_REGRESSIONS=0x0000000000000000')) {
            if (-not (Has-Text $text $marker)) { Add-Failure "required marker missing ($marker): $($run.RunId)" }
        }
        if (Has-Text $text 'GXOS_NET10:FAULT_' -or Has-Text $text 'GXOS_NET10:FIRST_ALLOCATION_OK' -or Has-Text $text 'GXOS_NET10:GC_STARTUP_ADVANCED') { Add-Failure "forbidden advanced marker: $($run.RunId)" }
        $summary = $text.IndexOf('GXOS_NET10:QPC_COUNT=', [StringComparison]::Ordinal)
        $regressions = $text.IndexOf('GXOS_NET10:QPC_REGRESSIONS=', [StringComparison]::Ordinal)
        if ($summary -lt 0 -or $regressions -lt $summary) { Add-Failure "QPC summary incomplete: $($run.RunId)" }
        $qpcCount = Read-Hex $text 'GXOS_NET10:QPC_COUNT='
        if ($null -eq $qpcCount -or $qpcCount -lt 1) { Add-Failure "QPC count invalid: $($run.RunId)" }

        if ($Mode -eq 'Positive') {
            $ordered = @('GXOS_NET10:SLIST_HEAD_INITIALIZED_OK','GXOS_NET10:CRT_INITTERM_E_BEGIN','GXOS_NET10:CRT_INITTERM_E_ENTRY_RAW=0x0000000000000000','GXOS_NET10:CRT_INITTERM_E_ENTRY_NULL','GXOS_NET10:CRT_INITTERM_E_ENTRY_COUNT=0x0000000000000001','GXOS_NET10:CRT_INITTERM_E_NULL_ENTRY_COUNT=0x0000000000000001','GXOS_NET10:CRT_INITTERM_E_NONNULL_ENTRY_COUNT=0x0000000000000000','GXOS_NET10:CRT_INITTERM_E_INVOCATION_COUNT=0x0000000000000000','GXOS_NET10:CRT_INITTERM_E_RESULT=0x0000000000000000','GXOS_NET10:CRT_INITTERM_E_OK','GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm')
            $cursor = -1
            foreach ($marker in $ordered) { $cursor = Find-Next $text $marker ($cursor + 1); if ($cursor -lt 0) { Add-Failure "positive marker order missing ($marker): $($run.RunId)"; break } }
            if (Has-Text $text 'GXOS_NET10:CRT_INITTERM_E_OX') { Add-Failure "mutated success marker present: $($run.RunId)" }
            if (Has-Text $text 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e') { Add-Failure "_initterm_e remained unresolved: $($run.RunId)" }
            $first = Read-Hex $text 'GXOS_NET10:CRT_INITTERM_E_FIRST='
            $last = Read-Hex $text 'GXOS_NET10:CRT_INITTERM_E_LAST='
            $size = Read-Hex $text 'GXOS_NET10:CRT_INITTERM_E_TABLE_SIZE_BYTES='
            if ($null -eq $first -or $null -eq $last -or $null -eq $size -or $last -le $first -or $last - $first -ne 8 -or $size -ne 8) { Add-Failure "_initterm_e concrete range invalid: $($run.RunId)" }
        } else {
            if (Has-Text $text 'GXOS_NET10:CRT_INITTERM_E_BEGIN' -or Has-Text $text 'GXOS_NET10:CRT_INITTERM_E_OK' -or Has-Text $text 'GXOS_NET10:CRT_INITTERM_E_RESULT=') { Add-Failure "disabled implementation executed: $($run.RunId)" }
            $ordered = @('GXOS_NET10:SLIST_HEAD_INITIALIZED_OK','GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e')
            $cursor = -1
            foreach ($marker in $ordered) { $cursor = Find-Next $text $marker ($cursor + 1); if ($cursor -lt 0) { Add-Failure "disabled boundary order missing ($marker): $($run.RunId)"; break } }
            if (Has-Text $text "GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm`r`n") { Add-Failure "disabled implementation advanced: $($run.RunId)" }
        }
    }
    $runIds = @($runRecords | ForEach-Object { [string]$_.RunId })
    if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Add-Failure 'duplicate run IDs' }
    $processKeys = @($runRecords | ForEach-Object { "$( [int]$_.QemuPid )|$( [string]$_.QemuStartUtc )" })
    if (@($processKeys | Select-Object -Unique).Count -ne $processKeys.Count) { Add-Failure 'duplicate QEMU process records' }
    $sequences = @($runRecords | ForEach-Object { [int]$_.Sequence } | Sort-Object)
    if (($sequences -join ',') -ne ((@(1..$ExpectedRunCount)) -join ',')) { Add-Failure 'run sequences are not consecutive' }
}

$summary = [ordered]@{
    EvidenceRoot = $evidence
    Mode = $Mode
    ExpectedRunCount = $ExpectedRunCount
    RunCount = if (Test-Path $runsRoot) { @(Get-ChildItem $runsRoot -Directory).Count } else { 0 }
    Passed = ($failures.Count -eq 0)
    Failures = @($failures)
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $evidence 'validation-summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 8 -Compress
if ($failures.Count -ne 0) { exit 2 }
Write-Output 'CRT_INITTERM_E_EVIDENCE_VALIDATION=PASSED'

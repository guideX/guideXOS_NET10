[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled')] [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure([string]$message) { $failures.Add($message) }
function Read-Json([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
function Has-Text([string]$text, [string]$value) { return $text.IndexOf($value, [StringComparison]::Ordinal) -ge 0 }
function Read-Hex([string]$text, [string]$prefix) {
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if (-not $match.Success) { return $null }
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}
function Read-Last-Hex([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if ($matches.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($matches[$matches.Count - 1].Groups[1].Value, 16)
}
function Read-Decimal([string]$text, [string]$prefix) {
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '([0-9]+)')
    if (-not $match.Success) { return $null }
    return [Convert]::ToUInt64($match.Groups[1].Value, 10)
}
function Require-Ordered([string]$text, [string[]]$tokens, [string]$label) {
    $position = -1
    foreach ($token in $tokens) {
        $next = $text.IndexOf($token, [Math]::Max(0, $position + 1), [StringComparison]::Ordinal)
        if ($next -lt 0) { Add-Failure "$label missing or out of order: $token"; return }
        $position = $next
    }
}
function Get-RunText($run) {
    $serial = Join-Path $root ([string]$run.SerialLog)
    if (-not (Test-Path -LiteralPath $serial)) { Add-Failure "serial log missing: $serial"; return '' }
    return Get-Content -LiteralPath $serial -Raw
}

$manifestPath = Join-Path $root 'artifact-manifest.json'
$contextPath = Join-Path $root 'validation-context.json'
$runsPath = Join-Path $root 'runs'
if (-not (Test-Path -LiteralPath $manifestPath) -or
    -not (Test-Path -LiteralPath $contextPath) -or
    -not (Test-Path -LiteralPath $runsPath)) {
    Add-Failure 'evidence root is incomplete'
}

$observedBoundary = $null
if ($failures.Count -eq 0) {
    $manifest = Read-Json $manifestPath
    $context = Read-Json $contextPath
    if ([string]$manifest.Mode -ne $Mode) { Add-Failure 'manifest mode mismatch' }
    if ([int]$context.RunCount -ne $ExpectedRunCount) { Add-Failure 'validation run count mismatch' }
    $runDirectories = @(Get-ChildItem -LiteralPath $runsPath -Directory | Sort-Object Name)
    if ($runDirectories.Count -ne $ExpectedRunCount) { Add-Failure "run directory count mismatch: $($runDirectories.Count)" }
    $runRecords = New-Object System.Collections.Generic.List[object]
    foreach ($directory in $runDirectories) {
        $runPath = Join-Path $directory.FullName 'run.json'
        if (-not (Test-Path -LiteralPath $runPath)) { Add-Failure "run metadata missing: $runPath"; continue }
        $runRecords.Add((Read-Json $runPath))
    }

    $ids = @($runRecords | ForEach-Object { [string]$_.RunId })
    $pids = @($runRecords | Where-Object { $null -ne $_.QemuPid } | ForEach-Object { [string]$_.QemuPid })
    if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { Add-Failure 'duplicate run IDs' }
    if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Add-Failure 'duplicate QEMU process IDs' }

    foreach ($artifact in @($manifest.Artifacts)) {
        if (-not (Test-Path -LiteralPath ([string]$artifact.Path))) { Add-Failure "artifact missing: $($artifact.Kind)"; continue }
        $current = Get-Item -LiteralPath ([string]$artifact.Path)
        $hash = (Get-FileHash -LiteralPath $current.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne [string]$artifact.Sha256 -or [int64]$current.Length -ne [int64]$artifact.Length -or
            $current.LastWriteTimeUtc.ToString('o') -ne [string]$artifact.LastWriteTimeUtc) {
            Add-Failure "artifact hash, length, or timestamp mismatch: $($artifact.Kind)"
        }
    }

    foreach ($run in $runRecords) {
        $runId = [string]$run.RunId
        if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId) { Add-Failure "stale evidence ID: $runId" }
        if ($runId -ne "$($manifest.EvidenceId)-run$([int]$run.Sequence)") { Add-Failure "stale run ID: $runId" }
        if (-not [bool]$run.CleanupComplete) { Add-Failure "QEMU cleanup incomplete: $runId" }
        if (-not [bool]$run.Pass) { Add-Failure "runner did not capture a complete process: $runId" }
        $text = Get-RunText $run
        if ([string]::IsNullOrEmpty($text)) { continue }

        $boundaryMatch = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
        if ($boundaryMatch.Count -eq 0) {
            Add-Failure "missing import boundary: $runId"
        } else {
            $boundary = $boundaryMatch[$boundaryMatch.Count - 1].Groups[1].Value
            if ($null -eq $observedBoundary) { $observedBoundary = $boundary }
            elseif ($observedBoundary -ne $boundary) { Add-Failure "boundary changed across runs: $runId" }
            if ($Mode -eq 'Disabled' -and $boundary -ne 'KERNEL32.dll!GetEnvironmentVariableW') {
                Add-Failure "disabled control reached unexpected boundary $boundary in $runId"
            }
            if ($Mode -eq 'Positive' -and $boundary -eq 'KERNEL32.dll!GetEnvironmentVariableW') {
                Add-Failure "positive route still stopped at GetEnvironmentVariableW: $runId"
            }
        }

        $expectedFunctional = if ($Mode -eq 'Positive') { 28 } else { 27 }
        $expectedFailfast = if ($Mode -eq 'Positive') { 96 } else { 97 }
        if ((Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -ne $expectedFunctional -or
            (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -ne $expectedFailfast -or
            (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') -ne 0) {
            Add-Failure "import census invalid: $runId"
        }
        if (Has-Text $text 'FAULT_VECTOR=' -or Has-Text $text 'GXOS_NET10:FAIL:') {
            Add-Failure "processor fault or fail marker observed: $runId"
        }
        if ((Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') -ne 2 -or
            (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') -ne 0) {
            Add-Failure "QPC summary invalid: $runId"
        }
        foreach ($marker in @(
            'GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000',
            'GXOS_NET10:MANAGED_THREAD_REGISTERED=0',
            'GXOS_NET10:ALLOCATION_CONTEXT_VALID=0',
            'GXOS_NET10:GC_CONTRACT_INITIALIZED=0',
            'GXOS_NET10:GC_HEAP_USABLE=0',
            'GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0',
            'GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) {
            if (-not (Has-Text $text $marker)) { Add-Failure "$runId missing state marker: $marker" }
        }

        if ($Mode -eq 'Disabled') {
            if (Has-Text $text 'GXOS_NET10:GETENV_BEGIN' -or Has-Text $text 'GXOS_NET10:GETENV_OK') {
                Add-Failure "disabled GetEnvironmentVariableW implementation executed: $runId"
            }
            Require-Ordered $text @(
                'GXOS_NET10:CRT_STRCMP_RESULT=0x0000000000000001',
                'GXOS_NET10:CRT_STRLEN_OK',
                'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetEnvironmentVariableW'
            ) $runId
            continue
        }

        Require-Ordered $text @(
            'GXOS_NET10:CRT_STRCMP_RESULT=0x0000000000000001',
            'GXOS_NET10:CRT_STRLEN_OK',
            'GXOS_NET10:GETENV_BEGIN',
            'GXOS_NET10:GETENV_RETURNED',
            'GXOS_NET10:GETENV_OK'
        ) $runId
        if ((Read-Last-Hex $text 'GXOS_NET10:GETENV_CALL_COUNT=0x') -ne 1 -or
            (Read-Last-Hex $text 'GXOS_NET10:GETENV_MISSING_COUNT=0x') -ne 1 -or
            (Read-Last-Hex $text 'GXOS_NET10:GETENV_SUCCESS_COUNT=0x') -ne 0 -or
            (Read-Last-Hex $text 'GXOS_NET10:GETENV_RETURN_VALUE=0x') -ne 0 -or
            (Read-Last-Hex $text 'GXOS_NET10:GETENV_LAST_ERROR_AFTER=0x') -ne 203 -or
            (Read-Hex $text 'GXOS_NET10:GETENV_NAME_LENGTH=0x') -ne 15 -or
            (Read-Hex $text 'GXOS_NET10:GETENV_N_SIZE=0x') -ne 0x11) {
            Add-Failure "GetEnvironmentVariableW result census invalid: $runId"
        }
        foreach ($marker in @(
            'GXOS_NET10:GETENV_LP_NAME_NULL=0',
            'GXOS_NET10:GETENV_LP_BUFFER_NULL=0',
            'GXOS_NET10:GETENV_N_SIZE_ZERO=0',
            'GXOS_NET10:GETENV_SIZE_PROBE=0',
            'GXOS_NET10:GETENV_NAME_UTF16=0044004F0054004E00450054005F00670063005300650072007600650072',
            'GXOS_NET10:GETENV_NAME_TEXT="DOTNET_gcServer"',
            'GXOS_NET10:GETENV_LAST_ERROR_CHANGED=1',
            'GXOS_NET10:GETENV_RETURN_EXPECTED_BY_CALLER=0x0000000000000000',
            'GXOS_NET10:GETENV_OUTPUT_WRITTEN=0')) {
            if (-not (Has-Text $text $marker)) { Add-Failure "$runId missing environment marker: $marker" }
        }
        $caller = Read-Hex $text 'GXOS_NET10:GETENV_CALLER=0x'
        $returnAddress = Read-Hex $text 'GXOS_NET10:GETENV_RETURN_ADDRESS=0x'
        if ($null -eq $caller -or $caller -eq 0 -or $caller -ne $returnAddress) {
            Add-Failure "environment caller/return address invalid: $runId"
        }
        $lastBoundaryMarker = "GXOS_NET10:UNEXPECTED_IMPORT_CALL:$([string]$run.Boundary)"
        if ([string]::IsNullOrWhiteSpace([string]$run.Boundary) -or
            -not (Has-Text $text $lastBoundaryMarker)) {
            Add-Failure "positive next boundary missing from run metadata: $runId"
        } else {
            Require-Ordered $text @('GXOS_NET10:GETENV_OK', $lastBoundaryMarker) $runId
        }
    }
}

if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; Failures = @($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; RunCount = $ExpectedRunCount; Boundary = $observedBoundary; Passed = $true; Failures = @() } | ConvertTo-Json -Depth 8
Write-Output 'GETENV_EVIDENCE_VALIDATION=PASSED'

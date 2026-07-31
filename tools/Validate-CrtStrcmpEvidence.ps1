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

if ($failures.Count -eq 0) {
    $manifest = Read-Json $manifestPath
    $context = Read-Json $contextPath
    if ([string]$manifest.Mode -ne $Mode) { Add-Failure 'manifest mode mismatch' }
    if ([int]$context.RunCount -ne $ExpectedRunCount) { Add-Failure 'validation run count mismatch' }

    $runDirectories = @(Get-ChildItem -LiteralPath $runsPath -Directory | Sort-Object Name)
    if ($runDirectories.Count -ne $ExpectedRunCount) {
        Add-Failure "run directory count mismatch: $($runDirectories.Count)"
    }
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
        if (-not (Test-Path -LiteralPath ([string]$artifact.Path))) {
            Add-Failure "artifact missing: $($artifact.Kind)"
            continue
        }
        $current = Get-Item -LiteralPath ([string]$artifact.Path)
        $hash = (Get-FileHash -LiteralPath $current.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne [string]$artifact.Sha256 -or [int64]$current.Length -ne [int64]$artifact.Length) {
            Add-Failure "artifact hash or length mismatch: $($artifact.Kind)"
        }
    }

    $observedBoundary = $null
    foreach ($run in $runRecords) {
        $runId = [string]$run.RunId
        if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId) { Add-Failure "stale evidence ID: $runId" }
        if ([string]$runId -ne "$( $manifest.EvidenceId )-run$([int]$run.Sequence)") { Add-Failure "stale run ID: $runId" }
        if (-not [bool]$run.CleanupComplete) { Add-Failure "QEMU cleanup incomplete: $runId" }
        if (-not [bool]$run.Pass) { Add-Failure "runner did not capture a complete process: $runId" }
        $text = Get-RunText $run
        if ([string]::IsNullOrEmpty($text)) { continue }

        $boundaryMatch = [regex]::Match($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
        if (-not $boundaryMatch.Success) {
            Add-Failure "missing import boundary: $runId"
        } else {
            $boundary = $boundaryMatch.Groups[1].Value
            if ($null -eq $observedBoundary) { $observedBoundary = $boundary }
            elseif ($observedBoundary -ne $boundary) { Add-Failure "boundary changed across runs: $runId" }
            $expectedBoundary = if ($Mode -eq 'Positive') {
                'api-ms-win-crt-string-l1-1-0.dll!strlen'
            } else {
                'api-ms-win-crt-string-l1-1-0.dll!strcmp'
            }
            if ($boundary -ne $expectedBoundary) { Add-Failure "unexpected boundary $boundary in $runId" }
        }

        if ($Mode -eq 'Disabled') {
            if (Has-Text $text 'GXOS_NET10:CRT_STRCMP_CALL_COUNT=') { Add-Failure "disabled strcmp implementation executed: $runId" }
            if ((Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -ne 25 -or
                (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -ne 99) {
                Add-Failure "disabled import census invalid: $runId"
            }
            continue
        }

        Require-Ordered $text @(
            'GXOS_NET10:CRT_INITTERM_OK',
            'GXOS_NET10:CRT_STRCMP_CALL_COUNT=0x0000000000000001',
            'GXOS_NET10:CRT_STRCMP_LHS_TEXT=gcServer',
            'GXOS_NET10:CRT_STRCMP_RHS_TEXT=gcConservative',
            'GXOS_NET10:CRT_STRCMP_RESULT=0x0000000000000001',
            'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-string-l1-1-0.dll!strlen'
        ) $runId
        if ((Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -ne 26 -or
            (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -ne 98 -or
            (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') -ne 0) {
            Add-Failure "positive import census invalid: $runId"
        }
        if ((Read-Hex $text 'GXOS_NET10:CRT_STRCMP_LHS_LENGTH=0x') -ne 8 -or
            (Read-Hex $text 'GXOS_NET10:CRT_STRCMP_RHS_LENGTH=0x') -ne 14 -or
            (Read-Decimal $text 'GXOS_NET10:CRT_STRCMP_LHS_NULL_TERMINATED=') -ne 1 -or
            (Read-Decimal $text 'GXOS_NET10:CRT_STRCMP_RHS_NULL_TERMINATED=') -ne 1) {
            Add-Failure "strcmp bounded diagnostics invalid: $runId"
        }
        if (-not (Has-Text $text 'GXOS_NET10:CRT_STRCMP_LHS_BYTES=6763536572766572') -or
            -not (Has-Text $text 'GXOS_NET10:CRT_STRCMP_RHS_BYTES=6763436F6E736572766174697665')) {
            Add-Failure "strcmp byte diagnostics invalid: $runId"
        }
    }
}

if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; Failures = @($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; RunCount = $ExpectedRunCount; Boundary = $observedBoundary; Passed = $true; Failures = @() } | ConvertTo-Json -Depth 8
Write-Output 'CRT_STRCMP_EVIDENCE_VALIDATION=PASSED'

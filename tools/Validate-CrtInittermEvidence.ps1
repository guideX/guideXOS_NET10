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
function Get-RunText($run, [string]$evidencePath) {
    $serial = Join-Path $evidencePath ([string]$run.SerialLog)
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
    if ([string]$manifest.Mode -ne $Mode -and $Mode -ne 'Positive') { Add-Failure 'manifest mode mismatch' }
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
    if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { Add-Failure 'duplicate run IDs' }
    $pids = @($runRecords | Where-Object { $null -ne $_.QemuPid } | ForEach-Object { [string]$_.QemuPid })
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

    foreach ($run in $runRecords) {
        $runId = [string]$run.RunId
        if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId) { Add-Failure "stale evidence ID: $runId" }
        if ([string]$runId -ne "$( $manifest.EvidenceId )-run$([int]$run.Sequence)") { Add-Failure "stale run ID: $runId" }
        if (-not [bool]$run.CleanupComplete) { Add-Failure "QEMU cleanup incomplete: $runId" }
        if (-not [bool]$run.Pass) { Add-Failure "runner did not capture a complete process: $runId" }
        $text = Get-RunText $run $root
        if ([string]::IsNullOrEmpty($text)) { continue }

        if ($Mode -eq 'Disabled') {
            if ((Has-Text $text 'GXOS_NET10:CRT_INITTERM_BEGIN') -or
                (Has-Text $text 'GXOS_NET10:CRT_INITTERM_OK')) {
                Add-Failure "disabled implementation executed: $runId"
            }
            if (-not (Has-Text $text 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm')) {
                Add-Failure "disabled boundary is not _initterm: $runId"
            }
            if (Has-Text $text 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!strcmp') {
                Add-Failure "disabled run advanced past original boundary: $runId"
            }
            continue
        }

        $required = @(
            'GXOS_NET10:SLIST_HEAD_INITIALIZED_OK',
            'GXOS_NET10:CRT_INITTERM_E_BEGIN',
            'GXOS_NET10:CRT_INITTERM_E_OK',
            'GXOS_NET10:CRT_INITTERM_BEGIN',
            'GXOS_NET10:CRT_INITTERM_FIRST=',
            'GXOS_NET10:CRT_INITTERM_LAST=',
            'GXOS_NET10:CRT_INITTERM_TABLE_SIZE_BYTES=0x0000000000000048',
            'GXOS_NET10:CRT_INITTERM_ENTRY_INDEX=0x0000000000000000',
            'GXOS_NET10:CRT_INITTERM_ENTRY_NULL',
            'GXOS_NET10:CRT_INITTERM_ENTRY_INDEX=0x0000000000000001',
            'GXOS_NET10:CRT_INITTERM_CALLBACK_BEGIN_INDEX=0x0000000000000001',
            'GXOS_NET10:CRT_INITTERM_CALLBACK_RETURN_INDEX=0x0000000000000001',
            'GXOS_NET10:CRT_INITTERM_ENTRY_INDEX=0x0000000000000008',
            'GXOS_NET10:CRT_INITTERM_CALLBACK_RETURN_INDEX=0x0000000000000008',
            'GXOS_NET10:CRT_INITTERM_ENTRY_COUNT=0x0000000000000009',
            'GXOS_NET10:CRT_INITTERM_NULL_COUNT=0x0000000000000001',
            'GXOS_NET10:CRT_INITTERM_NONNULL_COUNT=0x0000000000000008',
            'GXOS_NET10:CRT_INITTERM_INVOKED_COUNT=0x0000000000000008',
            'GXOS_NET10:CRT_INITTERM_RETURNED_COUNT=0x0000000000000008',
            'GXOS_NET10:CRT_INITTERM_STATUS=0x0000000000000000',
            'GXOS_NET10:CRT_INITTERM_COMPLETED=0x0000000000000001',
            'GXOS_NET10:CRT_INITTERM_OK',
            'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-string-l1-1-0.dll!strcmp'
        )
        Require-Ordered $text $required $runId
        if (Has-Text $text 'GXOS_NET10:CRT_INITTERM_OX') { Add-Failure "mutated completion marker present: $runId" }
        if (Has-Text $text 'GXOS_NET10:CRT_INITTERM_CALLBACK_UNRESOLVED_IMPORT=1') { Add-Failure "callback reached unresolved import: $runId" }
        if (Has-Text $text 'GXOS_NET10:CRT_INITTERM_CALLBACK_FAULT_ACTIVE=1') { Add-Failure "callback fault reported: $runId" }

        $first = Read-Hex $text 'GXOS_NET10:CRT_INITTERM_FIRST=0x'
        $last = Read-Hex $text 'GXOS_NET10:CRT_INITTERM_LAST=0x'
        $size = Read-Hex $text 'GXOS_NET10:CRT_INITTERM_TABLE_SIZE_BYTES=0x'
        if ($null -eq $first -or $null -eq $last -or $null -eq $size -or
            $last -le $first -or $last - $first -ne 0x48 -or $size -ne 0x48) {
            Add-Failure "_initterm concrete range invalid: $runId"
        }
        $functional = Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL='
        $failfast = Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST='
        $unresolved = Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS='
        if ($functional -ne 25 -or $failfast -ne 99 -or $unresolved -ne 0) { Add-Failure "import census invalid: $runId" }
        if ((Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') -ne 0 -or
            (Read-Decimal $text 'GXOS_NET10:MANAGED_THREAD_REGISTERED=') -ne 0 -or
            (Read-Decimal $text 'GXOS_NET10:ALLOCATION_CONTEXT_VALID=') -ne 0) {
            Add-Failure "runtime-state summary invalid: $runId"
        }
        if (-not (Has-Text $text 'GXOS_NET10:GC_CONTRACT_INITIALIZED=0') -or
            -not (Has-Text $text 'GXOS_NET10:GC_HEAP_USABLE=0') -or
            -not (Has-Text $text 'GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) {
            Add-Failure "negative GC/allocation state is incomplete: $runId"
        }

        $begins = [regex]::Matches($text, 'GXOS_NET10:CRT_INITTERM_CALLBACK_BEGIN_INDEX=0x([0-9A-Fa-f]+)\r?\nGXOS_NET10:CRT_INITTERM_CALLBACK_TARGET=0x([0-9A-Fa-f]+)')
        $returns = [regex]::Matches($text, 'GXOS_NET10:CRT_INITTERM_CALLBACK_RETURN_INDEX=0x([0-9A-Fa-f]+)\r?\nGXOS_NET10:CRT_INITTERM_CALLBACK_RETURN_TARGET=0x([0-9A-Fa-f]+)')
        if ($begins.Count -ne 8 -or $returns.Count -ne 8) { Add-Failure "callback begin/return count invalid: $runId" }
        else {
            for ($index = 0; $index -lt 8; $index++) {
                $beginIndex = [Convert]::ToUInt64($begins[$index].Groups[1].Value, 16)
                $returnIndex = [Convert]::ToUInt64($returns[$index].Groups[1].Value, 16)
                $beginTarget = [Convert]::ToUInt64($begins[$index].Groups[2].Value, 16)
                $returnTarget = [Convert]::ToUInt64($returns[$index].Groups[2].Value, 16)
                if ($beginIndex -ne ($index + 1) -or $returnIndex -ne ($index + 1) -or
                    $beginTarget -eq 0 -or $beginTarget -ne $returnTarget) {
                    Add-Failure "callback order/target mismatch at $index`: $runId"
                }
            }
        }
    }
}

if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; Failures = @($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; RunCount = $ExpectedRunCount; Passed = $true; Failures = @() } | ConvertTo-Json -Depth 8
Write-Output 'CRT_INITTERM_EVIDENCE_VALIDATION=PASSED'

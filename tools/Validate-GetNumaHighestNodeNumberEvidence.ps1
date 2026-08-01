[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled', 'SuccessExperiment', 'FailureExperiment')] [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { [void]$failures.Add($message) }
function Has([string]$text, [string]$value) { return $text.Contains($value) }
function Read-Hex([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if ($matches.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($matches[$matches.Count - 1].Groups[1].Value, 16)
}
function Read-Decimal([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9]+)')
    if ($matches.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($matches[$matches.Count - 1].Groups[1].Value, 10)
}
function Require-Ordered([string]$text, [string[]]$markers, [string]$runId) {
    $position = -1
    foreach ($marker in $markers) {
        $next = $text.IndexOf($marker, $position + 1, [StringComparison]::Ordinal)
        if ($next -lt 0) { Fail "$runId missing marker: $marker"; return }
        if ($next -lt $position) { Fail "$runId marker order invalid: $marker"; return }
        $position = $next
    }
}

$manifestPath = Join-Path $root 'artifact-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing manifest: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifestFingerprint = (($manifest.Artifacts | ForEach-Object { "$($_.Kind)=$($_.Sha256):$($_.Length)" }) -join '|')
foreach ($artifact in $manifest.Artifacts) {
    if (-not (Test-Path -LiteralPath $artifact.Path)) { Fail "manifest artifact missing: $($artifact.Path)"; continue }
    $item = Get-Item -LiteralPath $artifact.Path
    $hash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($hash -ne $artifact.Sha256 -or [int64]$item.Length -ne [int64]$artifact.Length) { Fail "manifest artifact changed: $($artifact.Path)" }
}
$runs = @(Get-ChildItem -LiteralPath (Join-Path $root 'runs') -Directory | Sort-Object Name)
if ($runs.Count -ne $ExpectedRunCount) { Fail "expected $ExpectedRunCount runs, found $($runs.Count)" }
$pfx = 'GXOS_NET10:GETNUMAHIGHESTNODE_'
$expectedBoundary = if ($Mode -eq 'Disabled') { 'KERNEL32.dll!GetNumaHighestNodeNumber' } else { 'KERNEL32.dll!GetProcessGroupAffinity' }
$expectedFunctional = if ($Mode -eq 'Disabled') { 30 } else { 31 }
$expectedFailfast = if ($Mode -eq 'Disabled') { 94 } else { 93 }
$expectedStatus = if ($Mode -eq 'FailureExperiment') { 12 } else { 0 }
$expectedBoolean = if ($Mode -eq 'FailureExperiment') { 0 } else { 1 }
$runIds = @(); $pids = @(); $fingerprints = @()
foreach ($runDirectory in $runs) {
    $runJsonPath = Join-Path $runDirectory.FullName 'run.json'
    $serialPath = Join-Path $runDirectory.FullName 'serial.log'
    if (-not (Test-Path -LiteralPath $runJsonPath) -or -not (Test-Path -LiteralPath $serialPath)) { Fail "incomplete run directory: $($runDirectory.Name)"; continue }
    $run = Get-Content -Raw -LiteralPath $runJsonPath | ConvertFrom-Json
    $text = Get-Content -Raw -LiteralPath $serialPath
    $runIds += [string]$run.RunId; $pids += [int]$run.QemuPid; $fingerprints += [string]$run.ArtifactFingerprint
    if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId -or
        [string]$run.RunId -ne "$($manifest.EvidenceId)-run$([int]$run.Sequence)") {
        Fail "$($run.RunId) stale or mismatched evidence identity"
    }
    if ([string]$run.ArtifactFingerprint -ne $manifestFingerprint) { Fail "$($run.RunId) artifact fingerprint changed" }
    if (-not $run.Pass -or -not $run.CleanupComplete) { Fail "$($run.RunId) lifecycle failed" }
    if ([int]$run.QemuPid -le 0 -or [string]::IsNullOrWhiteSpace([string]$run.RunId)) { Fail "$($runDirectory.Name) identity missing" }
    $boundaryMatches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $boundary = if ($boundaryMatches.Count -eq 0) { '' } else { $boundaryMatches[$boundaryMatches.Count - 1].Groups[1].Value }
    if ($boundary -ne $expectedBoundary) { Fail "$($run.RunId) boundary is $boundary, expected $expectedBoundary" }
    if ((Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -ne $expectedFunctional -or (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -ne $expectedFailfast -or (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') -ne 0) { Fail "$($run.RunId) import census invalid" }
    if ((Read-Hex $text 'GXOS_NET10:CRT_STRICMP_CALL_COUNT=0x') -ne 0x375 -or (Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') -ne 2 -or (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') -ne 0) { Fail "$($run.RunId) prior runtime summary invalid" }
    foreach ($marker in @('GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000', 'GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000', 'GXOS_NET10:MANAGED_THREAD_REGISTERED=0', 'GXOS_NET10:GC_CONTRACT_INITIALIZED=0', 'GXOS_NET10:GC_HEAP_USABLE=0', 'GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0', 'GXOS_NET10:ALLOCATION_CONTEXT_VALID=0', 'GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) { if (-not (Has $text $marker)) { Fail "$($run.RunId) missing state marker: $marker" } }
    if ($Mode -eq 'Disabled') {
        Require-Ordered $text @('GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE', 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetNumaHighestNodeNumber') $run.RunId
        if (Has $text ($pfx + 'BEGIN') -or Has $text ($pfx + 'OK')) { Fail "$($run.RunId) disabled route advanced" }
        continue
    }
    Require-Ordered $text @(($pfx + 'BEGIN'), ($pfx + 'STATUS='), ($pfx + 'RETURNED'), 'GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE', ("GXOS_NET10:UNEXPECTED_IMPORT_CALL:$expectedBoundary")) $run.RunId
    $pointer = Read-Hex $text ($pfx + 'OUTPUT_POINTER=0x')
    $range = Read-Hex $text ($pfx + 'WRITABLE_RANGE_SIZE=0x')
    if ($null -eq $pointer -or $pointer -eq 0 -or $null -eq $range -or $range -lt 4) { Fail "$($run.RunId) output destination missing or undersized" }
    if ((Read-Hex $text ($pfx + 'CALL_INDEX=0x')) -ne 0 -or (Read-Hex $text ($pfx + 'OUTPUT_WIDTH=0x')) -ne 4 -or (Read-Hex $text ($pfx + 'OUTPUT_ALIGNMENT=0x')) -ne 0) { Fail "$($run.RunId) output ABI/alignment invalid" }
    $status = Read-Hex $text ($pfx + 'STATUS=0x')
    $outputBefore = Read-Hex $text ($pfx + 'OUTPUT_BEFORE=0x')
    $outputAfter = Read-Hex $text ($pfx + 'OUTPUT_AFTER=0x')
    if ($status -ne $expectedStatus -or $outputBefore -ne 0 -or $outputAfter -ne 0) { Fail "$($run.RunId) result/output invalid" }
    if ((Read-Hex $text ($pfx + 'BOOLEAN_RESULT=0x')) -ne $expectedBoolean) { Fail "$($run.RunId) Boolean result invalid" }
    $lastErrorBefore = Read-Hex $text ($pfx + 'LAST_ERROR_BEFORE=0x')
    $lastErrorAfter = Read-Hex $text ($pfx + 'LAST_ERROR_AFTER=0x')
    $lastErrorValid = if ($Mode -eq 'FailureExperiment') { $lastErrorAfter -eq 0x32 } else { $lastErrorAfter -eq $lastErrorBefore }
    if ($null -eq $lastErrorBefore -or -not $lastErrorValid) { Fail "$($run.RunId) last-error policy invalid" }
    $expectedWrapperRead = if ($Mode -eq 'FailureExperiment') { 0 } else { 1 }
    if ((Read-Hex $text ($pfx + 'OUTPUT_READ_BY_WRAPPER=0x')) -ne $expectedWrapperRead) { Fail "$($run.RunId) wrapper output-read accounting invalid" }
    if ((Read-Hex $text ($pfx + 'USABLE_PROCESSORS=0x')) -ne 1 -or (Read-Hex $text ($pfx + 'DOMAIN_COUNT=0x')) -ne 1 -or (Read-Hex $text ($pfx + 'HIGHEST_NODE=0x')) -ne 0 -or (Read-Hex $text ($pfx + 'SYSTEM_INFO_PROCESSOR_COUNT=0x')) -ne 1 -or (Read-Hex $text ($pfx + 'SYSTEM_INFO_ACTIVE_MASK=0x')) -ne 1) { Fail "$($run.RunId) topology snapshot invalid" }
    if (-not (Has $text ($pfx + 'OUTPUT_WIDTH=0x0000000000000004')) -or -not (Has $text ($pfx + 'OUTPUT_ALIGNMENT=0x0000000000000000')) -or -not (Has $text ($pfx + 'DESTINATION_WRITABLE=1'))) { Fail "$($run.RunId) output validation incomplete" }
    if ($Mode -eq 'FailureExperiment') {
        if (-not (Has $text ($pfx + 'FAILED')) -or
            -not (Has $text ($pfx + 'CALLER_BRANCH=FAILURE_NON_NUMA_FALLBACK')) -or
            -not (Has $text ($pfx + 'OUTPUT_READ=0'))) { Fail "$($run.RunId) failure branch evidence invalid" }
    } else {
        if (-not (Has $text ($pfx + 'OK')) -or
            -not (Has $text ($pfx + 'CALLER_BRANCH=SUCCESS_BOOLEAN_OUTPUT_ZERO_NON_NUMA_FALLBACK')) -or
            -not (Has $text ($pfx + 'OUTPUT_READ=1'))) { Fail "$($run.RunId) success branch evidence invalid" }
    }
}
if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($fingerprints | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot=$root; Mode=$Mode; Failures=@($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{ EvidenceRoot=$root; Mode=$Mode; RunCount=$ExpectedRunCount; Boundary=$expectedBoundary; Passed=$true; Failures=@() } | ConvertTo-Json -Depth 8
Write-Output 'GETNUMAHIGHESTNODE_EVIDENCE_VALIDATION=PASSED'

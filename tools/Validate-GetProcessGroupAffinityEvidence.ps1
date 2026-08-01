[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled')] [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3,
    [int]$MaxPositiveSerialBytes = 524288
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
function Read-Text([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([^\r\n]+)')
    if ($matches.Count -eq 0) { return '' }
    return $matches[$matches.Count - 1].Groups[1].Value
}
function Require-Ordered([string]$text, [string[]]$markers, [string]$runId) {
    $position = -1
    foreach ($marker in $markers) {
        $next = $text.IndexOf($marker, $position + 1, [StringComparison]::Ordinal)
        if ($next -lt 0) { Fail "$runId missing marker: $marker"; return }
        $position = $next
    }
}
function Require-Equal([object]$actual, [object]$expected, [string]$label) {
    if ($null -eq $actual -or $actual -ne $expected) { Fail "$label expected $expected, got $actual" }
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
$expectedBoundary = if ($Mode -eq 'Disabled') { 'KERNEL32.dll!GetProcessGroupAffinity' } else { 'KERNEL32.dll!GetProcessAffinityMask' }
$expectedFunctional = if ($Mode -eq 'Disabled') { 31 } else { 32 }
$expectedFailfast = if ($Mode -eq 'Disabled') { 93 } else { 92 }
$runIds = @(); $pids = @(); $fingerprints = @(); $hashes = @()

foreach ($runDirectory in $runs) {
    $runJsonPath = Join-Path $runDirectory.FullName 'run.json'
    $serialPath = Join-Path $runDirectory.FullName 'serial.log'
    if (-not (Test-Path -LiteralPath $runJsonPath) -or -not (Test-Path -LiteralPath $serialPath)) {
        Fail "incomplete run directory: $($runDirectory.Name)"; continue
    }
    $run = Get-Content -Raw -LiteralPath $runJsonPath | ConvertFrom-Json
    $text = Get-Content -Raw -LiteralPath $serialPath
    $runId = [string]$run.RunId
    $runIds += $runId; $pids += [int]$run.QemuPid; $fingerprints += [string]$run.ArtifactFingerprint
    $hashes += [string]$run.SerialSha256
    if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId -or
        $runId -ne "$($manifest.EvidenceId)-run$([int]$run.Sequence)") { Fail "$runId stale or mismatched evidence identity" }
    if ([string]$run.ArtifactFingerprint -ne $manifestFingerprint) { Fail "$runId artifact fingerprint changed" }
    if (-not $run.Pass -or -not $run.CleanupComplete) { Fail "$runId lifecycle failed" }
    if ([int]$run.QemuPid -le 0 -or [string]::IsNullOrWhiteSpace($runId)) { Fail "$($runDirectory.Name) identity missing" }
    if ([int64]$run.FinalSerialLength -ne [int64]$text.Length) { Fail "$runId serial length is not immutable" }
    if ([string]$run.SerialSha256 -ne (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()) { Fail "$runId serial hash mismatch" }
    if ($Mode -eq 'Positive' -and $text.Length -gt $MaxPositiveSerialBytes) { Fail "$runId serial exceeds $MaxPositiveSerialBytes bytes" }
    if ($text.Length -eq 0) { Fail "$runId serial is empty"; continue }
    if (([regex]::Matches($text, 'GXOS_NET10:GETPROCESSGROUPAFFINITY_BEGIN').Count) -ne ($(if ($Mode -eq 'Positive') { 1 } else { 0 }))) { Fail "$runId process-group call count invalid" }
    $boundaryMatches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $boundary = if ($boundaryMatches.Count -eq 0) { '' } else { $boundaryMatches[$boundaryMatches.Count - 1].Groups[1].Value }
    if ($boundary -ne $expectedBoundary) { Fail "$runId boundary is $boundary, expected $expectedBoundary" }
    Require-Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') $expectedFunctional "$runId functional imports"
    Require-Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') $expectedFailfast "$runId failfast imports"
    Require-Equal (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') 0 "$runId unresolved imports"
    if ($Mode -eq 'Positive') {
        Require-Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_CALL_COUNT=0x') 0x375 "$runId prior _stricmp count"
        Require-Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_SUCCESS_COUNT=0x') 0x375 "$runId prior _stricmp successes"
        Require-Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_FAILURE_COUNT=0x') 0 "$runId prior _stricmp failures"
        Require-Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_UNIQUE_OPERAND_PAIR_COUNT=0x') 0x375 "$runId _stricmp unique pairs"
        Require-Equal (Read-Hex $text 'GXOS_NET10:PRIOR_VERBOSE_RECORDS_SUPPRESSED=0x') 0x375 "$runId _stricmp suppression count"
        Require-Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_PAIR_TABLE_OVERFLOW=0x') 0 "$runId _stricmp census overflow"
    } else {
        Require-Equal (Read-Hex $text 'GXOS_NET10:CRT_STRICMP_CALL_COUNT=0x') 0x375 "$runId prior _stricmp count"
    }
    Require-Equal (Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') 2 "$runId QPC count"
    Require-Equal (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') 0 "$runId QPC regressions"
    foreach ($marker in @(
        'GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000',
        'GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000',
        'GXOS_NET10:MANAGED_THREAD_REGISTERED=0',
        'GXOS_NET10:GC_CONTRACT_INITIALIZED=0',
        'GXOS_NET10:GC_HEAP_USABLE=0',
        'GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0',
        'GXOS_NET10:ALLOCATION_CONTEXT_VALID=0',
        'GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) { if (-not (Has $text $marker)) { Fail "$runId missing state marker: $marker" } }
    if ($Mode -eq 'Disabled') {
        if (Has $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_BEGIN' -or Has $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_OK') { Fail "$runId disabled route advanced" }
        Require-Ordered $text @('GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE', 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetProcessGroupAffinity') $runId
        continue
    }

    Require-Ordered $text @(
        'GXOS_NET10:GETPROCESSGROUPAFFINITY_BEGIN',
        'GXOS_NET10:GETPROCESSGROUPAFFINITY_STATUS_NAME=INSUFFICIENT_BUFFER',
        'GXOS_NET10:GETPROCESSGROUPAFFINITY_RETURNED',
        'GXOS_NET10:GETPROCESSGROUPAFFINITY_INSUFFICIENT_BUFFER_OK',
        'GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE',
        'GXOS_NET10:GETPROCESSGROUPAFFINITY_NEXT_BOUNDARY=KERNEL32.dll!GetProcessAffinityMask',
        'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetProcessAffinityMask') $runId
    $imageBase = Read-Hex $text 'GXOS_NET10:IMAGE_BASE=0x'
    $runtimeIat = Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_IAT_RUNTIME_ADDRESS=0x'
    $preferredIat = Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_IAT_PREFERRED_ADDRESS=0x'
    $staticIat = Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_IAT_RVA=0x'
    $staticCall = Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_STATIC_CALL_SITE=0x'
    $runtimeCall = Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_RUNTIME_CALL_SITE=0x'
    if ($null -eq $imageBase -or $null -eq $runtimeIat -or $runtimeIat -ne $imageBase + $staticIat -or $preferredIat -ne 0x180000000 + $staticIat) { Fail "$runId IAT address relocation invalid" }
    Require-Equal $staticIat 0x7D2A0 "$runId static IAT RVA"
    if ($null -eq $staticCall -or $null -eq $runtimeCall -or $runtimeCall -ne $imageBase + ($staticCall - 0x180000000)) { Fail "$runId call-site relocation invalid" }
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_IMPORT_DESCRIPTOR_INDEX=0x') 2 "$runId import descriptor index"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_CALL_INDEX=0x') 0 "$runId call index"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_PROCESS_HANDLE=0x') ([uint64]::MaxValue) "$runId process handle"
    if ((Read-Text $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_HANDLE_CLASS=') -ne 'CURRENT_PROCESS_PSEUDO_HANDLE') { Fail "$runId handle classification invalid" }
    $countPointer = Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_POINTER=0x'
    if ($null -eq $countPointer -or $countPointer -eq 0) { Fail "$runId count pointer missing" }
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_POINTER=0x') 0 "$runId null group array"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_NULL=') 1 "$runId array null flag"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_NULL=') 0 "$runId count null flag"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_ALIGNMENT=0x') 0 "$runId count alignment"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_INPUT_CAPACITY=0x') 0 "$runId input capacity"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_BEFORE=0x') 0 "$runId count before"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_REQUIRED_COUNT=0x') 1 "$runId required count"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT=0x') 1 "$runId output count"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_READABLE=0x') 1 "$runId count readable"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_COUNT_WRITABLE=0x') 1 "$runId count writable"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_CANONICAL=0x') 0 "$runId null array canonical flag"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_ARRAY_WRITABLE=0x') 0 "$runId null array writable flag"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUPS_WRITTEN=0x') 0 "$runId groups written"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_0_POLICY=0x') 0 "$runId Group 0 policy"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_0_WRITTEN=0x') 0 "$runId Group 0 written"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_ARRAY_OUTPUT_VALID=0x') 0 "$runId array output validity"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_BOOLEAN_RESULT=0x') 0 "$runId BOOL result"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_LAST_ERROR_BEFORE=0x') 0xCB "$runId last error before"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_LAST_ERROR_AFTER=0x') 0x7A "$runId last error after"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_RETRY=0x') 0 "$runId retry count"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT_READ_BY_CALLER=0x') 1 "$runId caller count read"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_ARRAY_READ_BY_CALLER=') 0 "$runId caller array read"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_OUTPUT_COUNT_CONSUMED=0x') 1 "$runId consumed count"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_GROUP_ARRAY_CONSUMED=') 0 "$runId consumed array"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSGROUPAFFINITY_SUBSEQUENT_GROUP_API_COUNT=') 0 "$runId subsequent group API count"
}
if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($fingerprints | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if (@($hashes | Select-Object -Unique).Count -ne $hashes.Count) { Fail 'duplicate serial hash across fresh runs' }
if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot=$root; Mode=$Mode; Failures=@($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{ EvidenceRoot=$root; Mode=$Mode; RunCount=$ExpectedRunCount; Boundary=$expectedBoundary; Passed=$true; Failures=@() } | ConvertTo-Json -Depth 8
Write-Output 'GETPROCESSGROUPAFFINITY_EVIDENCE_VALIDATION=PASSED'

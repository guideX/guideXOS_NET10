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
    $m = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if ($m.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($m[$m.Count - 1].Groups[1].Value, 16)
}
function Read-Decimal([string]$text, [string]$prefix) {
    $m = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9]+)')
    if ($m.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($m[$m.Count - 1].Groups[1].Value, 10)
}
function Read-Text([string]$text, [string]$prefix) {
    $m = [regex]::Matches($text, [regex]::Escape($prefix) + '([^\r\n]+)')
    if ($m.Count -eq 0) { return '' }
    return $m[$m.Count - 1].Groups[1].Value
}
function Equal([object]$actual, [object]$expected, [string]$label) {
    if ($null -eq $actual -or $actual -ne $expected) { Fail "$label expected $expected, got $actual" }
}
function Ordered([string]$text, [string[]]$markers, [string]$runId) {
    $position = -1
    foreach ($marker in $markers) {
        $next = $text.IndexOf($marker, $position + 1, [StringComparison]::Ordinal)
        if ($next -lt 0) { Fail "$runId missing ordered marker: $marker"; return }
        $position = $next
    }
}

$manifestPath = Join-Path $root 'artifact-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing manifest: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$fingerprint = (($manifest.Artifacts | ForEach-Object { "$($_.Kind)=$($_.Sha256):$($_.Length)" }) -join '|')
foreach ($artifact in $manifest.Artifacts) {
    if (-not (Test-Path -LiteralPath $artifact.Path)) { Fail "manifest artifact missing: $($artifact.Path)"; continue }
    $item = Get-Item -LiteralPath $artifact.Path
    $hash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($hash -ne $artifact.Sha256 -or [int64]$item.Length -ne [int64]$artifact.Length) { Fail "manifest artifact changed: $($artifact.Path)" }
}
$runs = @(Get-ChildItem -LiteralPath (Join-Path $root 'runs') -Directory | Sort-Object Name)
if ($runs.Count -ne $ExpectedRunCount) { Fail "expected $ExpectedRunCount runs, found $($runs.Count)" }
$expectedFunctional = if ($Mode -eq 'Disabled') { 33 } else { 34 }
$expectedFailfast = if ($Mode -eq 'Disabled') { 91 } else { 90 }
$runIds = @(); $pids = @(); $artifactFingerprints = @(); $serialHashes = @(); $boundaries = @()

foreach ($runDirectory in $runs) {
    $runJsonPath = Join-Path $runDirectory.FullName 'run.json'
    $serialPath = Join-Path $runDirectory.FullName 'serial.log'
    if (-not (Test-Path -LiteralPath $runJsonPath) -or -not (Test-Path -LiteralPath $serialPath)) { Fail "incomplete run: $($runDirectory.Name)"; continue }
    $run = Get-Content -Raw -LiteralPath $runJsonPath | ConvertFrom-Json
    $text = Get-Content -Raw -LiteralPath $serialPath
    $runId = [string]$run.RunId
    $runIds += $runId; $pids += [int]$run.QemuPid
    $artifactFingerprints += [string]$run.ArtifactFingerprint
    $serialHashes += [string]$run.SerialSha256
    if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId -or $runId -ne "$($manifest.EvidenceId)-run$([int]$run.Sequence)") { Fail "$runId identity mismatch" }
    if ([string]$run.ArtifactFingerprint -ne $fingerprint) { Fail "$runId artifact fingerprint changed" }
    if (-not $run.Pass -or -not $run.CleanupComplete) { Fail "$runId lifecycle failed" }
    if ([int64]$run.FinalSerialLength -ne [int64]$text.Length) { Fail "$runId serial length mismatch" }
    if ([string]$run.SerialSha256 -ne (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()) { Fail "$runId serial hash mismatch" }
    if ($text.Length -gt $MaxPositiveSerialBytes) { Fail "$runId serial exceeds $MaxPositiveSerialBytes bytes" }
    Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') $expectedFunctional "$runId functional imports"
    Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') $expectedFailfast "$runId failfast imports"
    Equal (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') 0 "$runId unresolved imports"
    Equal (Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') 2 "$runId QPC count"
    Equal (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') 0 "$runId QPC regressions"
    Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_CALL_COUNT=0x') 0x375 "$runId _stricmp count"
    foreach ($marker in @('GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000','GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000','GXOS_NET10:MANAGED_THREAD_REGISTERED=0','GXOS_NET10:GC_CONTRACT_INITIALIZED=0','GXOS_NET10:GC_HEAP_USABLE=0','GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0','GXOS_NET10:ALLOCATION_CONTEXT_VALID=0','GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) { if (-not (Has $text $marker)) { Fail "$runId missing state marker: $marker" } }
    $boundaryMatches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $boundary = if ($boundaryMatches.Count -eq 0) { '' } else { $boundaryMatches[$boundaryMatches.Count - 1].Groups[1].Value }
    if ($Mode -eq 'Disabled') {
        if ($boundary -ne 'KERNEL32.dll!QueryInformationJobObject') { Fail "$runId disabled boundary is $boundary" }
        if (Has $text 'GXOS_NET10:QUERYJOBOBJECT_BEGIN') { Fail "$runId disabled route advanced" }
        if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_NEXT_BOUNDARY=KERNEL32.dll!QueryInformationJobObject')) { Fail "$runId disabled affinity boundary missing" }
        Ordered $text @('GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_CONSUMPTION_COMPLETE','GXOS_NET10:GETPROCESSAFFINITYMASK_NEXT_BOUNDARY=KERNEL32.dll!QueryInformationJobObject','GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!QueryInformationJobObject') $runId
        continue
    }
    $next = Read-Text $text 'GXOS_NET10:QUERYJOBOBJECT_NEXT_BOUNDARY='
    $boundaries += $next
    if ([string]::IsNullOrWhiteSpace($next) -or $boundary -ne $next) { Fail "$runId next boundary does not match generic import" }
    if ($next -eq 'KERNEL32.dll!QueryInformationJobObject') { Fail "$runId did not advance past QueryInformationJobObject" }
    Equal ([regex]::Matches($text, 'GXOS_NET10:QUERYJOBOBJECT_BEGIN').Count) 1 "$runId query call count"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_IMPORT_DESCRIPTOR_INDEX=0x') 2 "$runId import descriptor"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_IAT_RVA=0x') 0x7D1F0 "$runId IAT RVA"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_PREFERRED_IAT=0x') 0x18007D1F0 "$runId preferred IAT"
    $imageBase = Read-Hex $text 'GXOS_NET10:IMAGE_BASE=0x'
    $runtimeIat = Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_RUNTIME_IAT=0x'
    $staticCall = Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_STATIC_CALL_SITE=0x'
    $runtimeCall = Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_RUNTIME_CALL_SITE=0x'
    if ($null -eq $imageBase -or $runtimeIat -ne $imageBase + 0x7D1F0) { Fail "$runId IAT relocation invalid" }
    if ($runtimeCall -ne $imageBase + ($staticCall - 0x180000000)) { Fail "$runId call relocation invalid" }
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_RETURN_ADDRESS=0x') ($runtimeCall + 6) "$runId return address"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_CALLER_START=0x') ($imageBase + 0x3CBE0) "$runId caller start"
    if (-not (Has $text 'GXOS_NET10:QUERYJOBOBJECT_CALLER=NativeAOT_processor_count_setup')) { Fail "$runId caller missing" }
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_RCX_HJOB=0x') 0 "$runId hJob"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_EDX_INFO_CLASS=0x') 0xF "$runId information class"
    Equal (Read-Text $text 'GXOS_NET10:QUERYJOBOBJECT_INFO_CLASS_NAME=') 'JobObjectCpuRateControlInformation' "$runId class name"
    $entryRsp = Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_ENTRY_RSP=0x'
    $fifthAddress = Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_FIFTH_ARGUMENT_STACK_ADDRESS=0x'
    if ($null -eq $entryRsp -or $fifthAddress -ne $entryRsp + 0x28) { Fail "$runId fifth stack location" }
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_FIFTH_ARGUMENT_STACK_VALUE=0x') 0 "$runId fifth stack value"
    Equal (Read-Text $text 'GXOS_NET10:QUERYJOBOBJECT_FIFTH_ARGUMENT_RELATION=') 'ENTRY_RSP_PLUS_0x28' "$runId fifth stack relation"
    $outputPointer = Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_R8_OUTPUT_POINTER=0x'
    if ($null -eq $outputPointer -or $outputPointer -eq 0) { Fail "$runId output pointer" }
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_R9D_OUTPUT_LENGTH=0x') 8 "$runId output length"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_STRUCTURE_SIZE=0x') 8 "$runId structure size"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_OUTPUT_ALIGNMENT=0x') 0 "$runId output alignment"
    foreach ($marker in @('OUTPUT_POINTER_CANONICAL=0x0000000000000001','OUTPUT_POINTER_WRITABLE=0x0000000000000001','OUTPUT_RANGE_VALID=0x0000000000000001')) { if (-not (Has $text "GXOS_NET10:QUERYJOBOBJECT_$marker")) { Fail "$runId missing output fact: $marker" } }
    Equal (Read-Text $text 'GXOS_NET10:QUERYJOBOBJECT_LP_RETURN_LENGTH_NULL=') '1' "$runId return-length null"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_BOOLEAN_RESULT=0x') 0 "$runId BOOL"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_LAST_ERROR_AFTER=0x') 5 "$runId last error"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_FIELD_READ_MASK=0x') 0 "$runId field-read mask"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_OUTPUT_WRITTEN=0x') 0 "$runId output writes"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_RETURN_LENGTH_WRITTEN=0x') 0 "$runId return-length writes"
    Equal (Read-Text $text 'GXOS_NET10:QUERYJOBOBJECT_JOB_ASSOCIATION=') 'NONE' "$runId job association"
    Equal (Read-Text $text 'GXOS_NET10:QUERYJOBOBJECT_CALLER_BRANCH=') 'FAILURE_NO_ASSOCIATED_JOB_FALLBACK' "$runId caller branch"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_PROCESSOR_COUNT_BEFORE=0x') 1 "$runId processor count before"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_PROCESSOR_COUNT_AFTER=0x') 1 "$runId processor count after"
    Equal (Read-Hex $text 'GXOS_NET10:QUERYJOBOBJECT_STATUS=0x') 1 "$runId status"
    Equal (Read-Text $text 'GXOS_NET10:QUERYJOBOBJECT_STATUS_NAME=') 'NO_ASSOCIATED_JOB' "$runId status name"
    Equal (Read-Decimal $text 'GXOS_NET10:QUERYJOBOBJECT_EXPECTED_NO_JOB_FAILURE_COUNT=0x') 1 "$runId expected failure count"
    if (Has $text 'GXOS_NET10:QUERYJOBOBJECT_OK') { Fail "$runId unexpected success marker" }
    Ordered $text @('GXOS_NET10:QUERYJOBOBJECT_BEGIN','GXOS_NET10:QUERYJOBOBJECT_RETURNED','GXOS_NET10:QUERYJOBOBJECT_EXPECTED_NO_ASSOCIATED_JOB_FAILURE','GXOS_NET10:QUERYJOBOBJECT_CALLER_CONSUMPTION_COMPLETE','GXOS_NET10:QUERYJOBOBJECT_NEXT_BOUNDARY=','GXOS_NET10:UNEXPECTED_IMPORT_CALL:') $runId
}
if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($artifactFingerprints | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if (@($serialHashes | Select-Object -Unique).Count -ne $serialHashes.Count) { Fail 'duplicate serial hash across fresh runs' }
if ($Mode -eq 'Positive' -and @($boundaries | Select-Object -Unique).Count -ne 1) { Fail 'positive next boundary changed across runs' }
if ($failures.Count -ne 0) {
    [PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;Failures=@($failures)} | ConvertTo-Json -Depth 8
    exit 2
}
$reportedBoundary = if ($Mode -eq 'Positive') { $boundaries[0] } else { 'KERNEL32.dll!QueryInformationJobObject' }
[PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;RunCount=$ExpectedRunCount;NextBoundary=$reportedBoundary;Passed=$true;Failures=@()} | ConvertTo-Json -Depth 8
Write-Output 'QUERYINFORMATIONJOBOBJECT_EVIDENCE_VALIDATION=PASSED'

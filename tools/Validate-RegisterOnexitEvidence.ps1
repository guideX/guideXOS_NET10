[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled')] [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$failures = [System.Collections.Generic.List[string]]::new()

function Fail([string]$message) { [void]$failures.Add($message) }
function Read-Hex([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if ($matches.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($matches[$matches.Count - 1].Groups[1].Value, 16)
}
function Read-HexAll([string]$text, [string]$prefix) {
    $values = [System.Collections.Generic.List[UInt64]]::new()
    foreach ($match in [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')) {
        [void]$values.Add([Convert]::ToUInt64($match.Groups[1].Value, 16))
    }
    return @($values)
}
function Read-Dec([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9]+)')
    if ($matches.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($matches[$matches.Count - 1].Groups[1].Value, 10)
}
function Read-Text([string]$text, [string]$prefix) {
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '([^\r\n]*)')
    if (-not $match.Success) { return $null }
    return $match.Groups[1].Value
}
function Equal([object]$actual, [object]$expected, [string]$label) {
    if ($null -eq $actual -or $actual.ToString() -ne $expected.ToString()) {
        Fail "$label expected '$expected' got '$actual'"
    }
}
function Ordered([string]$text, [string[]]$markers, [string]$label) {
    $position = -1
    foreach ($marker in $markers) {
        $next = $text.IndexOf($marker, $position + 1, [StringComparison]::Ordinal)
        if ($next -lt 0) { Fail "$label missing $marker"; return }
        $position = $next
    }
}
function Verify-ArtifactSet($artifacts, [string]$label) {
    foreach ($artifact in @($artifacts)) {
        if (-not (Test-Path -LiteralPath $artifact.Path)) { Fail "$label missing $($artifact.Path)"; continue }
        $item = Get-Item -LiteralPath $artifact.Path
        $hash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne $artifact.Sha256) { Fail "$label hash mismatch $($artifact.Path)" }
        if ([int64]$item.Length -ne [int64]$artifact.Length) { Fail "$label length mismatch $($artifact.Path)" }
    }
}

$manifestPath = Join-Path $root 'artifact-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Artifact manifest missing: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
Equal $manifest.Mode $Mode 'manifest mode'
Verify-ArtifactSet $manifest.Artifacts 'manifest'
$expectedPayloadHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
$payloadArtifact = @($manifest.Artifacts | Where-Object { $_.Kind -eq 'nativeaot_payload' })
if ($payloadArtifact.Count -ne 1) { Fail 'manifest nativeaot payload artifact count' }
elseif ([string]$payloadArtifact[0].Sha256 -ne $expectedPayloadHash) { Fail 'manifest payload hash mismatch' }
$runIds = [System.Collections.Generic.List[string]]::new()
$pids = [System.Collections.Generic.List[int]]::new()
$fingerprints = [System.Collections.Generic.List[string]]::new()
$serialHashes = [System.Collections.Generic.List[string]]::new()

for ($sequence = 1; $sequence -le $ExpectedRunCount; $sequence++) {
    $runDirectory = Join-Path $root ("runs\run-{0}" -f $sequence)
    $runPath = Join-Path $runDirectory 'run.json'
    $serialPath = Join-Path $runDirectory 'serial.log'
    if (-not (Test-Path -LiteralPath $runPath) -or -not (Test-Path -LiteralPath $serialPath)) {
        Fail "run $sequence metadata or serial missing"
        continue
    }
    $run = Get-Content -Raw -LiteralPath $runPath | ConvertFrom-Json
    $text = Get-Content -Raw -LiteralPath $serialPath
    $runId = [string]$run.RunId
    [void]$runIds.Add($runId)
    [void]$pids.Add([int]$run.QemuPid)
    [void]$fingerprints.Add([string]$run.ArtifactFingerprint)
    [void]$serialHashes.Add([string]$run.SerialSha256)
    Equal $runId ("$($manifest.EvidenceId)-run$sequence") "run $sequence ID"
    Equal $run.Mode $Mode "run $sequence mode"
    Equal $run.Pass $true "run $sequence pass"
    Equal $run.CleanupComplete $true "run $sequence cleanup"
    $actualLength = [int64](Get-Item -LiteralPath $serialPath).Length
    Equal $run.FinalSerialLength $actualLength "run $sequence serial length"
    Equal $run.SerialSha256 ((Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()) "run $sequence serial hash"
    if ($actualLength -gt 512KB) { Fail "run $sequence serial exceeds 512 KiB" }
    Equal (Read-Hex $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') 0 "run $sequence unresolved imports"
    Equal (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') 0 "run $sequence QPC regressions"
    Equal (Read-Hex $text 'GXOS_NET10:ALLOCATION_CONTEXT_VALID=') 0 "run $sequence allocation context"
    Equal (Read-Hex $text 'GXOS_NET10:GC_CONTRACT_INITIALIZED=') 0 "run $sequence GC contract"
    Equal (Read-Hex $text 'GXOS_NET10:MANAGED_ALLOCATION_COUNT=') 0 "run $sequence managed allocation count"

    if ($Mode -eq 'Disabled') {
        Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') 36 "run $sequence disabled functional imports"
        Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') 88 "run $sequence disabled fail-fast imports"
        if ($text.Contains('GXOS_NET10:REGISTER_ONEXIT_BEGIN')) { Fail "run $sequence disabled route advanced" }
        if (-not $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function')) {
            Fail "run $sequence disabled register boundary missing"
        }
        continue
    }

    Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') 37 "run $sequence functional imports"
    Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') 87 "run $sequence fail-fast imports"
    Equal ([regex]::Matches($text, 'GXOS_NET10:REGISTER_ONEXIT_BEGIN').Count) 1 "run $sequence register call count"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CALL_INDEX=0x') 0 "run $sequence register index"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_IMPORT_DESCRIPTOR_INDEX=0x') 8 "run $sequence descriptor index"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_IAT_RVA=0x') 0x7D358 "run $sequence IAT RVA"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_PREFERRED_IAT=0x') 0x18007D358 "run $sequence preferred IAT"
    $imageBase = Read-Hex $text 'GXOS_NET10:IMAGE_BASE=0x'
    $runtimeIat = Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_RUNTIME_IAT=0x'
    $staticCall = Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_STATIC_CALL_SITE=0x'
    $runtimeCall = Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_RUNTIME_CALL_SITE=0x'
    Equal $staticCall 0x180077E13 "run $sequence static call site"
    Equal $runtimeCall ($imageBase + ($staticCall - 0x180000000)) "run $sequence runtime call site"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_RETURN_ADDRESS=0x') ($runtimeCall + 6) "run $sequence return address"
    Equal $runtimeIat ($imageBase + 0x7D358) "run $sequence runtime IAT"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CALLER_START=0x') 0x180077DF0 "run $sequence caller start"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CALLER_END=0x') 0x180077E30 "run $sequence caller end"
    Equal (Read-Text $text 'GXOS_NET10:REGISTER_ONEXIT_CALLER=') 'NativeAOT_CRT_atexit_registration_helper' "run $sequence caller"
    $table = Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_POINTER=0x'
    $callback = Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CALLBACK_POINTER=0x'
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_RVA=0x') ($table - $imageBase) "run $sequence table RVA"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CALLBACK_RVA=0x') ($callback - $imageBase) "run $sequence callback RVA"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_REGION_READABLE=0x') 1 "run $sequence table readable"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_REGION_WRITABLE=0x') 1 "run $sequence table writable"
    Equal (Read-Text $text 'GXOS_NET10:REGISTER_ONEXIT_CALLBACK_OWNER=') 'MANAGED_IMAGE_TEXT' "run $sequence callback owner"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_INITIALIZED_TABLE_MATCH=0x') 1 "run $sequence initialized table match"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_INITIALIZED_TABLE_INDEX=0x') 0 "run $sequence initialized table index"

    $initRaw = @(Read-HexAll $text 'GXOS_NET10:CRT_ONEXIT_INIT_TABLE_FIRST_AFTER=0x')
    $registerRaw = @(Read-HexAll $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_RAW_BEFORE=0x')
    $registerAfterRaw = @(Read-HexAll $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_RAW_AFTER=0x')
    Equal $initRaw.Count 2 "run $sequence initialized table count"
    Equal $registerRaw.Count 1 "run $sequence registration raw-field count"
    Equal $registerAfterRaw.Count 1 "run $sequence registration after raw-field count"
    if ($initRaw.Count -ne 2 -or $registerRaw.Count -ne 1 -or $registerAfterRaw.Count -ne 1) {
        continue
    }
    Equal $registerRaw[0] $initRaw[0] "run $sequence encoded-null before"
    if ($registerAfterRaw[0] -eq $registerRaw[0]) { Fail "run $sequence raw first not updated" }
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_RAW_BEFORE=0x') $registerRaw[0] "run $sequence raw last before"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_END_RAW_BEFORE=0x') $registerRaw[0] "run $sequence raw end before"
    if ((Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_RAW_AFTER=0x') -eq $registerRaw[0]) { Fail "run $sequence raw last not updated" }
    if ((Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_END_RAW_AFTER=0x') -eq $registerRaw[0]) { Fail "run $sequence raw end not updated" }
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_UNCHANGED=0x') 0 "run $sequence table changed"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_BEFORE=0x') 0 "run $sequence first before"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_BEFORE=0x') 0 "run $sequence last before"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_END_BEFORE=0x') 0 "run $sequence end before"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_GROWTH_REQUIRED=0x') 1 "run $sequence growth required"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_ATTEMPTED=0x') 1 "run $sequence allocation attempted"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_COUNT_BEFORE=0x') 0 "run $sequence allocation count before"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_COUNT_AFTER=0x') 1 "run $sequence allocation count after"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATOR_CALL_COUNT_BEFORE=0x') 0 "run $sequence allocator calls before"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATOR_CALL_COUNT_AFTER=0x') 1 "run $sequence allocator calls after"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATED_BYTES_AFTER=0x') 0x100 "run $sequence allocated bytes"
    Equal (Read-Text $text 'GXOS_NET10:REGISTER_ONEXIT_INITIAL_TABLE_CLASSIFICATION=') 'DECODED_EMPTY' "run $sequence initial classification"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_SIZE=0x') 0x100 "run $sequence allocation size"
    $allocation = Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_ADDRESS=0x'
    if ($null -eq $allocation -or $allocation -eq 0) { Fail "run $sequence allocation address" }
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_BASE=0x') $allocation "run $sequence storage base"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_END=0x') ($allocation + 0x100) "run $sequence storage end"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_READABLE=0x') 1 "run $sequence storage readable"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_STORAGE_REGION_WRITABLE=0x') 1 "run $sequence storage writable"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_SLOT_COUNT=0x') 32 "run $sequence slot count"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_SLOT0_DECODED=0x') $callback "run $sequence slot 0 callback"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_SLOT0_CALLBACK_MATCH=0x') 1 "run $sequence slot 0 match"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_UNUSED_SLOTS_ALL_NULL=0x') 1 "run $sequence unused slots"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_STORAGE_DISJOINT_FROM_IMAGE=0x') 1 "run $sequence storage disjointness"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_UNUSED_SLOT_FIRST=0x') 0 "run $sequence unused slot first"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_UNUSED_SLOT_LAST=0x') 0 "run $sequence unused slot last"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_FIRST_AFTER=0x') $allocation "run $sequence decoded beginning"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_LAST_AFTER=0x') ($allocation + 8) "run $sequence decoded next"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_TABLE_END_AFTER=0x') ($allocation + 0x100) "run $sequence decoded end"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_USED_AFTER=0x') 1 "run $sequence used after"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CAPACITY_AFTER=0x') 32 "run $sequence capacity after"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_REMAINING_CAPACITY_AFTER=0x') 31 "run $sequence remaining after"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_RESULT=0x') 0 "run $sequence result"
    Equal (Read-Text $text 'GXOS_NET10:REGISTER_ONEXIT_STATUS=') 'OK' "run $sequence status"
    Equal (Read-Text $text 'GXOS_NET10:REGISTER_ONEXIT_CALLER_BRANCH=') 'RETURN_VALUE_MAPPED_TO_SUCCESS' "run $sequence caller branch"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CALLBACK_EXECUTED=0x') 0 "run $sequence callback execution"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_CALLBACK_EXECUTED_PROVEN=0x') 0 "run $sequence callback execution proof"
    Equal (Read-Text $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_PRIMITIVE=') 'UEFI_BOOT_SERVICES_ALLOCATE_POOL' "run $sequence allocation primitive"
    Equal (Read-Text $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_DEPENDENCY=') 'AllocatePool(EFI_LOADER_DATA,0x100)' "run $sequence allocation dependency"
    Equal (Read-Hex $text 'GXOS_NET10:REGISTER_ONEXIT_ALLOCATION_IMPLEMENTED=0x') 1 "run $sequence allocation implementation"
    if ($text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function')) { Fail "run $sequence positive route fail-fast" }
    Ordered $text @(
        'GXOS_NET10:CRT_ONEXIT_INIT_CALL=0x0000000000000001',
        'GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK',
        'GXOS_NET10:CRT_ONEXIT_INIT_CALL=0x0000000000000002',
        'GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK',
        'GXOS_NET10:GETPROCADDRESS_RETURNED',
        'GXOS_NET10:REGISTER_ONEXIT_BEGIN',
        'GXOS_NET10:REGISTER_ONEXIT_RETURNED',
        'GXOS_NET10:REGISTER_ONEXIT_CONTINUATION_BEYOND_CALL_SITE=1') "run $sequence contract order"
}

if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($fingerprints | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if (@($serialHashes | Select-Object -Unique).Count -ne $serialHashes.Count) { Fail 'duplicate serial hash across fresh runs' }
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) { Fail 'QEMU process remains' }
if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot=$root; Mode=$Mode; Failures=@($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{ EvidenceRoot=$root; Mode=$Mode; RunCount=$ExpectedRunCount; Passed=$true; Failures=@() } | ConvertTo-Json -Depth 8
Write-Output 'REGISTER_ONEXIT_EVIDENCE_VALIDATION=PASSED'

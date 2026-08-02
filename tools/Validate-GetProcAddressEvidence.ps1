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
function Has([string]$text, [string]$value) { return $text.Contains($value) }
function Read-Hex([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if ($matches.Count -eq 0) { return $null }
    return [Convert]::ToUInt64($matches[$matches.Count - 1].Groups[1].Value, 16)
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
        if ($next -lt 0) { Fail "$label missing ordered marker $marker"; return }
        if ($next -lt $position) { Fail "$label marker order $marker"; return }
        $position = $next
    }
}
function Verify-ArtifactSet($artifacts, [string]$label) {
    foreach ($artifact in @($artifacts)) {
        if (-not (Test-Path -LiteralPath $artifact.Path)) { Fail "$label missing artifact $($artifact.Path)"; continue }
        $item = Get-Item -LiteralPath $artifact.Path
        $hash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne $artifact.Sha256) { Fail "$label hash mismatch $($artifact.Path)" }
        if ([int64]$item.Length -ne [int64]$artifact.Length) { Fail "$label length mismatch $($artifact.Path)" }
    }
}

if (-not (Test-Path -LiteralPath $root)) { throw "Evidence root missing: $root" }
$manifestPath = Join-Path $root 'artifact-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Artifact manifest missing: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
Equal $manifest.Mode $Mode 'manifest mode'
Verify-ArtifactSet $manifest.Artifacts 'manifest'
$evidenceId = [string]$manifest.EvidenceId
$runIds = [System.Collections.Generic.List[string]]::new()
$pids = [System.Collections.Generic.List[int]]::new()
$fingerprints = [System.Collections.Generic.List[string]]::new()
$serialHashes = [System.Collections.Generic.List[string]]::new()
$boundaries = [System.Collections.Generic.List[string]]::new()

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
    Equal $runId "$evidenceId-run$sequence" "run $sequence ID"
    Equal $run.Mode $Mode "run $sequence mode"
    Equal $run.CleanupComplete $true "run $sequence cleanup"
    Equal $run.Pass $true "run $sequence pass"
    $actualLength = [int64](Get-Item -LiteralPath $serialPath).Length
    Equal $run.FinalSerialLength $actualLength "run $sequence serial length"
    Equal $run.SerialSha256 ((Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()) "run $sequence serial hash"
    if ($actualLength -gt 512KB) { Fail "run $sequence serial exceeds 512 KiB" }
    if (-not (Has $text 'GXOS_NET10:QPC_REGRESSIONS=')) { Fail "run $sequence QPC summary missing" }
    if ([regex]::Matches($text, 'GXOS_NET10:QPC_REGRESSIONS=').Count -lt 2) { Fail "run $sequence final QPC summary missing" }
    if ([regex]::Matches($text, 'GXOS_NET10:QPC_COUNT=').Count -lt 2) { Fail "run $sequence final QPC count missing" }
    if (-not (Has $text 'GXOS_NET10:ALLOCATION_CONTEXT_VALID=0')) { Fail "run $sequence allocation state missing" }
    $boundaryMatches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $boundary = if ($boundaryMatches.Count -eq 0) { '' } else { $boundaryMatches[$boundaryMatches.Count - 1].Groups[1].Value }
    [void]$boundaries.Add($boundary)

    if ($Mode -eq 'Disabled') {
        Equal $boundary 'KERNEL32.dll!GetProcAddress' "run $sequence disabled boundary"
        Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') 35 "run $sequence disabled functional imports"
        Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') 89 "run $sequence disabled fail-fast imports"
        if (Has $text 'GXOS_NET10:GETPROCADDRESS_BEGIN') { Fail "run $sequence disabled route advanced" }
        continue
    }

    Equal $boundary 'api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function' "run $sequence positive boundary"
    Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') 36 "run $sequence functional imports"
    Equal (Read-Dec $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') 88 "run $sequence fail-fast imports"
    Equal (Read-Dec $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') 0 "run $sequence unresolved imports"
    Equal ([regex]::Matches($text, 'GXOS_NET10:GETPROCADDRESS_BEGIN').Count) 1 "run $sequence live call count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_CALL_COUNT=0x') 1 "run $sequence aggregate call count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_SUCCESS_COUNT=0x') 0 "run $sequence success count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_EXPECTED_ABSENT_MODULE_FAILURE_COUNT=0x') 1 "run $sequence absent-module failure count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_MISSING_EXPORT_FAILURE_COUNT=0x') 0 "run $sequence missing-export failure count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_INVALID_HANDLE_FAILURE_COUNT=0x') 0 "run $sequence invalid-handle failure count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAMED_LOOKUP_COUNT=0x') 1 "run $sequence named count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_ORDINAL_LOOKUP_COUNT=0x') 0 "run $sequence ordinal count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_EXPORT_LOOKUP_ATTEMPTS=0x') 0 "run $sequence export attempt count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_IMPORT_DESCRIPTOR_INDEX=0x') 2 "run $sequence descriptor index"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_IAT_RVA=0x') 0x7D138 "run $sequence IAT RVA"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_PREFERRED_IAT=0x') 0x18007D138 "run $sequence preferred IAT"
    $imageBase = Read-Hex $text 'GXOS_NET10:IMAGE_BASE=0x'
    $runtimeIat = Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_RUNTIME_IAT=0x'
    $staticCall = Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_STATIC_CALL_SITE=0x'
    $runtimeCall = Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_RUNTIME_CALL_SITE=0x'
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_RETURN_ADDRESS=0x') ($runtimeCall + 6) "run $sequence return address"
    Equal $staticCall 0x180037C71 "run $sequence static call site"
    if ($null -ne $imageBase) { Equal $runtimeIat ($imageBase + 0x7D138) "run $sequence runtime IAT"; Equal $runtimeCall ($imageBase + ($staticCall - 0x180000000)) "run $sequence runtime call" }
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_CALLER_START=0x') 0x180037C40 "run $sequence caller start"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_CALLER=') 'NativeAOT_RtlDllShutdownInProgress_probe' "run $sequence caller"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_MODULE_HANDLE=0x') 0 "run $sequence module handle"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_MODULE_CLASS=') 'ABSENT_NULL' "run $sequence module classification"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_IDENTIFIER_KIND=') 'NAME' "run $sequence identifier kind"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_NAME_BYTES=') '52746C446C6C53687574646F776E496E50726F6772657373' "run $sequence exact name bytes"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_NAME_PREVIEW=') '"RtlDllShutdownInProgress"' "run $sequence exact name"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_LENGTH=0x') 24 "run $sequence name length"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_POINTER_CANONICAL=0x') 1 "run $sequence name canonical"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_READABLE=0x') 1 "run $sequence name readable"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_REGION_READABLE=0x') 1 "run $sequence name region readable"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_REGION_EXECUTABLE=0x') 0 "run $sequence name region executable"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_REGION_WRITABLE=0x') 0 "run $sequence name region writable"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_7BIT_ASCII=0x') 1 "run $sequence ASCII name"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_HIGH_BIT_COUNT=0x') 0 "run $sequence high-bit count"
    $namePointer = Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_POINTER=0x'
    $terminator = Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_NAME_TERMINATOR=0x'
    if ($null -ne $namePointer -and $null -ne $terminator) { Equal $terminator ($namePointer + 24) "run $sequence terminator" }
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_MODULE_VALID=0x') 0 "run $sequence module validation"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_EXPORT_LOOKUP_ATTEMPTED=0x') 0 "run $sequence export parsing"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_RESULT=0x') 0 "run $sequence result"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_LAST_ERROR_BEFORE=0x') 126 "run $sequence prior error"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_LAST_ERROR_AFTER=0x') 127 "run $sequence selected error"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_STATUS=') 'INVALID_MODULE_HANDLE' "run $sequence status"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_MODULE_HANDLE_PROVENANCE=') 'PRECEDING_GETMODULEHANDLEW_NULL_RESULT' "run $sequence handle provenance"
    if (-not (Has $text 'GXOS_NET10:GETPROCADDRESS_EXPECTED_ABSENT_MODULE_FAILURE')) { Fail "run $sequence expected failure marker" }
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_CALLER_NULL_TEST=') 1 "run $sequence null test"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_POINTER_STORED=0x') 0 "run $sequence pointer stored"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_POINTER_CALLED=0x') 0 "run $sequence pointer called"
    Equal (Read-Text $text 'GXOS_NET10:GETPROCADDRESS_CALLER_BRANCH=') 'FAILURE_NULL_OPTIONAL_FALLBACK' "run $sequence caller branch"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_SUBSEQUENT_CALL_COUNT=0x') 0 "run $sequence subsequent calls"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_POINTER_STORED_COUNT=0x') 0 "run $sequence stored count"
    Equal (Read-Hex $text 'GXOS_NET10:GETPROCADDRESS_POINTER_CALLED_COUNT=0x') 0 "run $sequence called count"
    if (Has $text 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetProcAddress') { Fail "run $sequence stopped at old boundary" }
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_LAST_ERROR_AFTER=0x') 126 "run $sequence GetModuleHandleW result"
    Equal (Read-Hex $text 'GXOS_NET10:CRT_STRICMP_CALL_COUNT=0x') 885 "run $sequence stricmp aggregate"
    Equal (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') 0 "run $sequence QPC regressions"
    Equal (Read-Hex $text 'GXOS_NET10:GC_CONTRACT_INITIALIZED=') 0 "run $sequence GC contract"
    Equal (Read-Hex $text 'GXOS_NET10:GC_HEAP_USABLE=') 0 "run $sequence GC heap"
    Equal (Read-Hex $text 'GXOS_NET10:ALLOCATION_CONTEXT_CREATED=') 0 "run $sequence allocation context"
    Equal (Read-Hex $text 'GXOS_NET10:MANAGED_THREAD_REGISTERED=') 0 "run $sequence managed thread"
    Equal (Read-Hex $text 'GXOS_NET10:MANAGED_ALLOCATION_COUNT=') 0 "run $sequence managed allocations"
    Ordered $text @(
        'GXOS_NET10:GETMODULEHANDLEW_RETURNED',
        'GXOS_NET10:GETPROCADDRESS_BEGIN',
        'GXOS_NET10:GETPROCADDRESS_RETURNED',
        'GXOS_NET10:GETPROCADDRESS_CALLER_CONSUMPTION_COMPLETE',
        'GXOS_NET10:GETPROCADDRESS_NEXT_BOUNDARY=api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function',
        'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function') "run $sequence order"
}

if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($fingerprints | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if (@($serialHashes | Select-Object -Unique).Count -ne $serialHashes.Count) { Fail 'duplicate serial hash across fresh runs' }
if (@($boundaries | Select-Object -Unique).Count -ne 1) { Fail 'boundary changed across runs' }
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) { Fail 'QEMU process remains' }
if ($failures.Count -ne 0) {
    [PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;Failures=@($failures)} | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;RunCount=$ExpectedRunCount;NextBoundary=$boundaries[0];Passed=$true;Failures=@()} | ConvertTo-Json -Depth 8
Write-Output 'GETPROCADDRESS_EVIDENCE_VALIDATION=PASSED'

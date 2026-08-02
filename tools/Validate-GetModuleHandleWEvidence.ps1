[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled')] [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3,
    [int]$MaxSerialBytes = 524288
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
$expectedFunctional = if ($Mode -eq 'Disabled') { 34 } else { 35 }
$expectedFailfast = if ($Mode -eq 'Disabled') { 90 } else { 89 }
$runIds = @(); $pids = @(); $fingerprints = @(); $serialHashes = @(); $boundaries = @()

foreach ($runDirectory in $runs) {
    $runJsonPath = Join-Path $runDirectory.FullName 'run.json'
    $serialPath = Join-Path $runDirectory.FullName 'serial.log'
    if (-not (Test-Path -LiteralPath $runJsonPath) -or -not (Test-Path -LiteralPath $serialPath)) {
        Fail "incomplete run: $($runDirectory.Name)"; continue
    }
    $run = Get-Content -Raw -LiteralPath $runJsonPath | ConvertFrom-Json
    $text = Get-Content -Raw -LiteralPath $serialPath
    $runId = [string]$run.RunId
    $runIds += $runId; $pids += [int]$run.QemuPid; $fingerprints += [string]$run.ArtifactFingerprint
    $serialHashes += [string]$run.SerialSha256
    if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId -or
        $runId -ne "$($manifest.EvidenceId)-run$([int]$run.Sequence)") { Fail "$runId identity mismatch" }
    if ([string]$run.ArtifactFingerprint -ne $fingerprint) { Fail "$runId artifact fingerprint changed" }
    if (-not $run.Pass -or -not $run.CleanupComplete) { Fail "$runId lifecycle failed" }
    if ([int64]$run.FinalSerialLength -ne [int64]$text.Length) { Fail "$runId serial length mismatch" }
    if ([string]$run.SerialSha256 -ne (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()) { Fail "$runId serial hash mismatch" }
    if ($text.Length -gt $MaxSerialBytes) { Fail "$runId serial exceeds $MaxSerialBytes bytes" }
    Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') $expectedFunctional "$runId functional imports"
    Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') $expectedFailfast "$runId failfast imports"
    Equal (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') 0 "$runId unresolved imports"
    Equal (Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') 2 "$runId QPC count"
    Equal (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') 0 "$runId QPC regressions"
    Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_CALL_COUNT=0x') 0x375 "$runId _stricmp count"
    foreach ($marker in @(
        'GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000',
        'GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000',
        'GXOS_NET10:MANAGED_THREAD_REGISTERED=0',
        'GXOS_NET10:GC_CONTRACT_INITIALIZED=0',
        'GXOS_NET10:GC_HEAP_USABLE=0',
        'GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0',
        'GXOS_NET10:ALLOCATION_CONTEXT_VALID=0',
        'GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) {
        if (-not (Has $text $marker)) { Fail "$runId missing state marker: $marker" }
    }
    $boundaryMatches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $boundary = if ($boundaryMatches.Count -eq 0) { '' } else { $boundaryMatches[$boundaryMatches.Count - 1].Groups[1].Value }
    $boundaries += $boundary

    if ($Mode -eq 'Disabled') {
        Equal $boundary 'KERNEL32.dll!GetModuleHandleW' "$runId disabled boundary"
        if (Has $text 'GXOS_NET10:GETMODULEHANDLEW_BEGIN') { Fail "$runId disabled route advanced" }
        if (-not (Has $text 'GXOS_NET10:QUERYJOBOBJECT_NEXT_BOUNDARY=KERNEL32.dll!GetModuleHandleW')) { Fail "$runId query boundary missing" }
        Ordered $text @(
            'GXOS_NET10:QUERYJOBOBJECT_CALLER_CONSUMPTION_COMPLETE',
            'GXOS_NET10:QUERYJOBOBJECT_NEXT_BOUNDARY=KERNEL32.dll!GetModuleHandleW',
            'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetModuleHandleW') $runId
        continue
    }

    Equal $boundary 'KERNEL32.dll!GetProcAddress' "$runId positive boundary"
    Equal ([regex]::Matches($text, 'GXOS_NET10:GETMODULEHANDLEW_BEGIN').Count) 1 "$runId module call count"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_CALL_COUNT=0x') 1 "$runId call count"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_SUCCESS_COUNT=0x') 0 "$runId success count"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_FAILURE_COUNT=0x') 1 "$runId failure count"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NULL_CALL_COUNT=0x') 0 "$runId null call count"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAMED_CALL_COUNT=0x') 1 "$runId named call count"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_IMPORT_DESCRIPTOR_INDEX=0x') 2 "$runId descriptor index"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_IAT_RVA=0x') 0x7D130 "$runId IAT RVA"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_PREFERRED_IAT=0x') 0x18007D130 "$runId preferred IAT"
    $imageBase = Read-Hex $text 'GXOS_NET10:IMAGE_BASE=0x'
    $mappedBase = Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_MAPPED_BASE=0x'
    $runtimeIat = Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_RUNTIME_IAT=0x'
    $staticCall = Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_STATIC_CALL_SITE=0x'
    $runtimeCall = Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_RUNTIME_CALL_SITE=0x'
    Equal $mappedBase $imageBase "$runId mapped image base"
    if ($null -eq $imageBase -or $runtimeIat -ne $imageBase + 0x7D130) { Fail "$runId IAT relocation invalid" }
    if ($staticCall -ne 0x180037C61 -or $runtimeCall -ne $imageBase + ($staticCall - 0x180000000)) { Fail "$runId call relocation invalid" }
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_RETURN_ADDRESS=0x') ($runtimeCall + 6) "$runId return address"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_CALLER_START=0x') 0x180037C40 "$runId caller start"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_CALLER=') 'NativeAOT_RtlDllShutdownInProgress_probe' "$runId caller"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_IMPORT_MODULE=') 'KERNEL32.dll' "$runId import module"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_IMPORT_SYMBOL=') 'GetModuleHandleW' "$runId import symbol"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_UTF16=') '006E00740064006C006C002E0064006C006C' "$runId UTF16 name"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_PREVIEW=') '"ntdll.dll"' "$runId bounded name preview"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_IS_NULL=') 0 "$runId non-null name"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_READABLE=') 1 "$runId name readable"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_EXECUTABLE=') 0 "$runId name non-executable"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_REGION_WRITABLE=') 0 "$runId name non-writable"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_LENGTH=0x') 9 "$runId name length"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_HAS_PATH=') 0 "$runId no path"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_HAS_EXTENSION=') 1 "$runId extension"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_NAME_EXACT_OBSERVED_FORM=') 1 "$runId exact name"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_RESULT=0x') 0 "$runId result"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_SELECTED_MODULE=') 'NONE' "$runId selected module"
    Equal (Read-Hex $text 'GXOS_NET10:GETMODULEHANDLEW_LAST_ERROR_AFTER=0x') 126 "$runId last error"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_STATUS=') 'MODULE_NOT_FOUND' "$runId status"
    Equal (Read-Text $text 'GXOS_NET10:GETMODULEHANDLEW_CALLER_BRANCH=') 'FAILURE_HANDLE_NULL' "$runId branch"
    if (-not (Has $text 'GXOS_NET10:GETMODULEHANDLEW_HANDLE_STORED=0')) { Fail "$runId handle storage" }
    if (-not (Has $text 'GXOS_NET10:GETMODULEHANDLEW_HANDLE_PASSED_TO=KERNEL32.dll!GetProcAddress')) { Fail "$runId handle consumer" }
    if (-not (Has $text 'GXOS_NET10:GETMODULEHANDLEW_CALLER_READS_DOS_HEADERS=0')) { Fail "$runId DOS header consumption" }
    if (-not (Has $text 'GXOS_NET10:GETMODULEHANDLEW_CALLER_READS_NT_HEADERS=0')) { Fail "$runId NT header consumption" }
    if (-not (Has $text 'GXOS_NET10:GETMODULEHANDLEW_CALLER_HEADER_FIELDS=NONE')) { Fail "$runId caller header field set" }
    if (-not (Has $text 'GXOS_NET10:GETMODULEHANDLEW_SUBSEQUENT_CALL_COUNT=0x0000000000000000')) { Fail "$runId subsequent calls" }
    Ordered $text @(
        'GXOS_NET10:GETMODULEHANDLEW_BEGIN',
        'GXOS_NET10:GETMODULEHANDLEW_RETURNED',
        'GXOS_NET10:QUERYJOBOBJECT_CALLER_CONSUMPTION_COMPLETE',
        'GXOS_NET10:GETMODULEHANDLEW_CALLER_CONSUMPTION_COMPLETE',
        'GXOS_NET10:GETMODULEHANDLEW_NEXT_BOUNDARY=KERNEL32.dll!GetProcAddress',
        'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetProcAddress') $runId
}

if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($fingerprints | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if (@($serialHashes | Select-Object -Unique).Count -ne $serialHashes.Count) { Fail 'duplicate serial hash across fresh runs' }
if (@($boundaries | Select-Object -Unique).Count -ne 1) { Fail 'boundary changed across runs' }
if ($failures.Count -ne 0) {
    [PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;Failures=@($failures)} | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;RunCount=$ExpectedRunCount;NextBoundary=$boundaries[0];Passed=$true;Failures=@()} | ConvertTo-Json -Depth 8
Write-Output 'GETMODULEHANDLEW_EVIDENCE_VALIDATION=PASSED'

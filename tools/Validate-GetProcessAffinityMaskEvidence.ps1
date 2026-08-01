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
function Require-Equal([object]$actual, [object]$expected, [string]$label) {
    if ($null -eq $actual -or $actual -ne $expected) { Fail "$label expected $expected, got $actual" }
}
function Require-Ordered([string]$text, [string[]]$markers, [string]$runId) {
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
$manifestFingerprint = (($manifest.Artifacts | ForEach-Object { "$($_.Kind)=$($_.Sha256):$($_.Length)" }) -join '|')
foreach ($artifact in $manifest.Artifacts) {
    if (-not (Test-Path -LiteralPath $artifact.Path)) { Fail "manifest artifact missing: $($artifact.Path)"; continue }
    $item = Get-Item -LiteralPath $artifact.Path
    $hash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($hash -ne $artifact.Sha256 -or [int64]$item.Length -ne [int64]$artifact.Length) { Fail "manifest artifact changed: $($artifact.Path)" }
}
$runs = @(Get-ChildItem -LiteralPath (Join-Path $root 'runs') -Directory | Sort-Object Name)
if ($runs.Count -ne $ExpectedRunCount) { Fail "expected $ExpectedRunCount runs, found $($runs.Count)" }
$expectedFunctional = if ($Mode -eq 'Disabled') { 32 } else { 33 }
$expectedFailfast = if ($Mode -eq 'Disabled') { 92 } else { 91 }
$runIds=@(); $pids=@(); $fingerprints=@(); $hashes=@(); $positiveBoundaries=@()

foreach ($runDirectory in $runs) {
    $runJsonPath = Join-Path $runDirectory.FullName 'run.json'
    $serialPath = Join-Path $runDirectory.FullName 'serial.log'
    if (-not (Test-Path -LiteralPath $runJsonPath) -or -not (Test-Path -LiteralPath $serialPath)) { Fail "incomplete run: $($runDirectory.Name)"; continue }
    $run = Get-Content -Raw -LiteralPath $runJsonPath | ConvertFrom-Json
    $text = Get-Content -Raw -LiteralPath $serialPath
    $runId=[string]$run.RunId; $runIds+=$runId; $pids+=[int]$run.QemuPid; $fingerprints+=[string]$run.ArtifactFingerprint; $hashes+=[string]$run.SerialSha256
    if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId -or $runId -ne "$($manifest.EvidenceId)-run$([int]$run.Sequence)") { Fail "$runId identity mismatch" }
    if ([string]$run.ArtifactFingerprint -ne $manifestFingerprint) { Fail "$runId artifact fingerprint changed" }
    if (-not $run.Pass -or -not $run.CleanupComplete) { Fail "$runId lifecycle failed" }
    if ([int64]$run.FinalSerialLength -ne [int64]$text.Length) { Fail "$runId serial length mismatch" }
    if ([string]$run.SerialSha256 -ne (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()) { Fail "$runId serial hash mismatch" }
    if ($Mode -eq 'Positive' -and $text.Length -gt $MaxPositiveSerialBytes) { Fail "$runId serial exceeds $MaxPositiveSerialBytes bytes" }
    Require-Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') $expectedFunctional "$runId functional imports"
    Require-Equal (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') $expectedFailfast "$runId failfast imports"
    Require-Equal (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') 0 "$runId unresolved imports"
    Require-Equal (Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') 2 "$runId QPC count"
    Require-Equal (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') 0 "$runId QPC regressions"
    Require-Equal (Read-Hex $text 'GXOS_NET10:PRIOR_STRICMP_CALL_COUNT=0x') 0x375 "$runId _stricmp count"
    foreach ($marker in @('GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000','GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000','GXOS_NET10:MANAGED_THREAD_REGISTERED=0','GXOS_NET10:GC_CONTRACT_INITIALIZED=0','GXOS_NET10:GC_HEAP_USABLE=0','GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0','GXOS_NET10:ALLOCATION_CONTEXT_VALID=0','GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) { if (-not (Has $text $marker)) { Fail "$runId missing state marker: $marker" } }
    $boundaryMatches=[regex]::Matches($text,'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $boundary=if($boundaryMatches.Count -eq 0){''}else{$boundaryMatches[$boundaryMatches.Count-1].Groups[1].Value}
    if ($Mode -eq 'Disabled') {
        if ($boundary -ne 'KERNEL32.dll!GetProcessAffinityMask') { Fail "$runId disabled boundary is $boundary" }
        if (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_BEGIN') { Fail "$runId disabled route advanced" }
        Require-Ordered $text @('GXOS_NET10:GETPROCESSGROUPAFFINITY_BEGIN','GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE','GXOS_NET10:GETPROCESSGROUPAFFINITY_NEXT_BOUNDARY=KERNEL32.dll!GetProcessAffinityMask','GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetProcessAffinityMask') $runId
        continue
    }
    $next=Read-Text $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_NEXT_BOUNDARY='
    $positiveBoundaries+=$next
    if ([string]::IsNullOrWhiteSpace($next) -or $boundary -ne $next) { Fail "$runId next boundary does not match generic import" }
    if ($next -eq 'KERNEL32.dll!GetProcessAffinityMask') { Fail "$runId did not advance past affinity" }
    if (([regex]::Matches($text,'GXOS_NET10:GETPROCESSAFFINITYMASK_BEGIN')).Count -ne 2) { Fail "$runId affinity call count invalid" }
    if (([regex]::Matches($text,'GXOS_NET10:GETPROCESSGROUPAFFINITY_BEGIN')).Count -ne 1) { Fail "$runId process-group call count invalid" }
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_IMPORT_DESCRIPTOR_INDEX=0x') 2 "$runId descriptor"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_IAT_RVA=0x') 0x7D208 "$runId IAT RVA"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_PREFERRED_IAT=0x') 0x18007D208 "$runId preferred IAT"
    $imageBase=Read-Hex $text 'GXOS_NET10:IMAGE_BASE=0x'; $runtimeIat=Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_RUNTIME_IAT=0x'; $staticCall=Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_STATIC_CALL_SITE=0x'; $runtimeCall=Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_RUNTIME_CALL_SITE=0x'
    if ($null -eq $imageBase -or $null -eq $runtimeIat -or $runtimeIat -ne $imageBase+0x7D208) { Fail "$runId IAT relocation invalid" }
    if ($null -eq $staticCall -or $null -eq $runtimeCall -or $runtimeCall -ne $imageBase+($staticCall-0x180000000)) { Fail "$runId call relocation invalid" }
    if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_STATIC_CALL_SITE=0x0000000180043793') -or -not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_STATIC_CALL_SITE=0x000000018003CC55')) { Fail "$runId live static call-site set incomplete" }
    if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER=NativeAOT_processor_bitmap_setup') -or -not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER=NativeAOT_processor_count_setup')) { Fail "$runId caller set incomplete" }
    if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALL_INDEX=0x0000000000000000') -or -not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALL_INDEX=0x0000000000000001')) { Fail "$runId call indexes missing" }
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_RCX_HANDLE=0x') ([uint64]::MaxValue) "$runId handle"
    Require-Equal (Read-Text $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_HANDLE_CLASS=') 'CURRENT_PROCESS_PSEUDO' "$runId handle class"
    $processPointer=Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_RDX_PROCESS_OUTPUT=0x'; $systemPointer=Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_R8_SYSTEM_OUTPUT=0x'
    if ($null -eq $processPointer -or $processPointer -eq 0 -or $null -eq $systemPointer -or $systemPointer -eq 0 -or $processPointer -eq $systemPointer) { Fail "$runId output pointers invalid" }
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_POINTER_ALIGNMENT=0x') 0 "$runId process alignment"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_POINTER_ALIGNMENT=0x') 0 "$runId system alignment"
    if ((Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_WRITABLE_RANGE=0x') -lt 8 -or (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_WRITABLE_RANGE=0x') -lt 8) { Fail "$runId writable range too small" }
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_OUTPUT_WIDTH=0x') 8 "$runId output width"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_AFTER=0x') 1 "$runId process output"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_AFTER=0x') 1 "$runId system output"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_MASK=0x') 1 "$runId process mask"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_MASK=0x') 1 "$runId system mask"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_PROCESS_POPCOUNT=0x') 1 "$runId process population"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_SYSTEM_POPCOUNT=0x') 1 "$runId system population"
    Require-Equal (Read-Hex $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_BOOLEAN_RESULT=0x') 1 "$runId BOOL"
    Require-Equal (Read-Text $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_STATUS_NAME=') 'OK' "$runId status"
    $affinityErrors=[regex]::Matches($text,'GXOS_NET10:GETPROCESSAFFINITYMASK_LAST_ERROR_BEFORE=0x([0-9A-Fa-f]+)')
    if ($affinityErrors.Count -ne 2 -or [Convert]::ToUInt64($affinityErrors[0].Groups[1].Value,16) -ne 0x7A -or [Convert]::ToUInt64($affinityErrors[1].Groups[1].Value,16) -ne 0xCB) { Fail "$runId per-call last-error trace incomplete" }
    foreach ($pair in @('PROCESS_POINTER_CANONICAL=0x0000000000000001','PROCESS_POINTER_WRITABLE=0x0000000000000001','PROCESS_RANGE_VALID=0x0000000000000001','SYSTEM_POINTER_CANONICAL=0x0000000000000001','SYSTEM_POINTER_WRITABLE=0x0000000000000001','SYSTEM_RANGE_VALID=0x0000000000000001','PROCESS_WRITTEN=0x0000000000000001','SYSTEM_WRITTEN=0x0000000000000001','CALLER_PROCESS_MASK_READ=0x0000000000000001','CALLER_SYSTEM_MASK_READ=0x0000000000000000','CALLER_PROCESS_READ_WIDTH=0x0000000000000008','CALLER_SYSTEM_READ_WIDTH=0x0000000000000000','CALLER_MASKS_INTERSECTED=0','CALLER_PROCESS_AND_SYSTEM=0','CALLER_GETLASTERROR=0','PROCESS_AFTER_READ_VALID=1','SYSTEM_AFTER_READ_VALID=1')) { if (-not (Has $text "GXOS_NET10:GETPROCESSAFFINITYMASK_$pair")) { Fail "$runId missing affinity fact: $pair" } }
    if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_BITS_COUNTED=0') -or -not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_BITS_COUNTED=1')) { Fail "$runId popcount trace incomplete" }
    if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_DERIVED_PROCESSOR_COUNT=NOT_DERIVED') -or -not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_DERIVED_PROCESSOR_COUNT=0x0000000000000001')) { Fail "$runId derived-count trace incomplete" }
    if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_PROCESSOR_BITMAP_UPDATE=1') -or -not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_PROCESSOR_BITMAP_UPDATE=0')) { Fail "$runId bitmap-consumption trace incomplete" }
    if (-not (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_SUBSEQUENT_API=KERNEL32.dll!QueryInformationJobObject')) { Fail "$runId subsequent API trace missing" }
    Require-Ordered $text @('GXOS_NET10:GETPROCESSAFFINITYMASK_BEGIN','GXOS_NET10:GETPROCESSAFFINITYMASK_RETURNED','GXOS_NET10:GETPROCESSAFFINITYMASK_OK','GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_CONSUMPTION_COMPLETE','GXOS_NET10:UNEXPECTED_IMPORT_CALL:') $runId
    if (Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_FAILED' -or Has $text 'GXOS_NET10:GETPROCESSAFFINITYMASK_OX') { Fail "$runId affinity success marker mutated" }
}
if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($fingerprints | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if (@($hashes | Select-Object -Unique).Count -ne $hashes.Count) { Fail 'duplicate serial hash across fresh runs' }
if ($Mode -eq 'Positive' -and @($positiveBoundaries | Select-Object -Unique).Count -ne 1) { Fail 'positive next boundary changed across runs' }
if ($failures.Count -ne 0) { [PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;Failures=@($failures)} | ConvertTo-Json -Depth 8; exit 2 }
$reportedBoundary = if ($Mode -eq 'Positive') { $positiveBoundaries[0] } else { 'KERNEL32.dll!GetProcessAffinityMask' }
[PSCustomObject]@{EvidenceRoot=$root;Mode=$Mode;RunCount=$ExpectedRunCount;NextBoundary=$reportedBoundary;Passed=$true;Failures=@()} | ConvertTo-Json -Depth 8
Write-Output 'GETPROCESSAFFINITYMASK_EVIDENCE_VALIDATION=PASSED'

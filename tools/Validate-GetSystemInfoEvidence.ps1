[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled', 'MarkerMutation')] [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { [void]$failures.Add($message) }
function Has([string]$text, [string]$value) { return $text.Contains($value) }
function Require-Ordered([string]$text, [string[]]$markers, [string]$runId) {
    $position = -1
    foreach ($marker in $markers) {
        $next = $text.IndexOf($marker, $position + 1, [StringComparison]::Ordinal)
        if ($next -lt 0) { Fail "$runId missing marker: $marker"; return }
        if ($next -lt $position) { Fail "$runId marker order invalid: $marker"; return }
        $position = $next
    }
}
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

$manifestPath = Join-Path $root 'artifact-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing artifact manifest: $manifestPath" }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifestFingerprint = (($manifest.Artifacts | ForEach-Object { "$($_.Kind)=$($_.Sha256):$($_.Length)" }) -join '|')
foreach ($artifact in $manifest.Artifacts) {
    if (-not (Test-Path -LiteralPath $artifact.Path)) { Fail "manifest artifact missing: $($artifact.Path)"; continue }
    $item = Get-Item -LiteralPath $artifact.Path
    $actualHash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -ne $artifact.Sha256 -or [int64]$item.Length -ne [int64]$artifact.Length) { Fail "manifest artifact changed: $($artifact.Path)" }
}
$runs = @(Get-ChildItem -LiteralPath (Join-Path $root 'runs') -Directory | Sort-Object Name)
if ($runs.Count -ne $ExpectedRunCount) { Fail "expected $ExpectedRunCount runs, found $($runs.Count)" }
$runIds = @(); $pids = @(); $hashes = @()
foreach ($runDirectory in $runs) {
    $runJsonPath = Join-Path $runDirectory.FullName 'run.json'
    $serialPath = Join-Path $runDirectory.FullName 'serial.log'
    if (-not (Test-Path -LiteralPath $runJsonPath) -or -not (Test-Path -LiteralPath $serialPath)) {
        Fail "incomplete run directory: $($runDirectory.Name)"; continue
    }
    $run = Get-Content -Raw -LiteralPath $runJsonPath | ConvertFrom-Json
    $text = Get-Content -Raw -LiteralPath $serialPath
    $runIds += [string]$run.RunId
    $pids += [int]$run.QemuPid
    $hashes += [string]$run.ArtifactFingerprint
    if ([string]$run.ArtifactFingerprint -ne $manifestFingerprint) { Fail "$($run.RunId) artifact fingerprint changed" }
    if (-not $run.Pass -or -not $run.CleanupComplete) { Fail "$($run.RunId) lifecycle failed" }
    if ($run.QemuPid -le 0 -or [string]::IsNullOrWhiteSpace($run.RunId)) { Fail "$($runDirectory.Name) identity missing" }
    $boundaryMatches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $boundary = if ($boundaryMatches.Count -eq 0) { '' } else { $boundaryMatches[$boundaryMatches.Count - 1].Groups[1].Value }
    $expectedBoundary = if ($Mode -eq 'Disabled') { 'KERNEL32.dll!GetSystemInfo' } else { 'KERNEL32.dll!GetNumaHighestNodeNumber' }
    if ($boundary -ne $expectedBoundary) { Fail "$($run.RunId) boundary is $boundary, expected $expectedBoundary" }
    $expectedFunctional = if ($Mode -eq 'Disabled') { 29 } else { 30 }
    $expectedFailfast = if ($Mode -eq 'Disabled') { 95 } else { 94 }
    if ((Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -ne $expectedFunctional -or
        (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -ne $expectedFailfast -or
        (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') -ne 0) {
        Fail "$($run.RunId) import census invalid"
    }
    if ((Read-Hex $text 'GXOS_NET10:CRT_STRICMP_CALL_COUNT=0x') -ne 0x375) { Fail "$($run.RunId) _stricmp count invalid" }
    if ((Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') -ne 2 -or (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') -ne 0) { Fail "$($run.RunId) QPC summary invalid" }
    if ($Mode -eq 'Disabled') {
        Require-Ordered $text @('GXOS_NET10:CRT_STRICMP_OK','GXOS_NET10:GETSYSTEMINFO_FAILFAST_RCX=0x','GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetSystemInfo') $run.RunId
        if (Has $text 'GXOS_NET10:GETSYSTEMINFO_OK' -or Has $text 'GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE') { Fail "$($run.RunId) disabled route advanced" }
    } else {
        $successMarker = if ($Mode -eq 'MarkerMutation') { 'GXOS_NET10:GETSYSTEMINFO_OX' } else { 'GXOS_NET10:GETSYSTEMINFO_OK' }
        Require-Ordered $text @('GXOS_NET10:CRT_STRICMP_OK','GXOS_NET10:GETSYSTEMINFO_BEGIN','GXOS_NET10:GETSYSTEMINFO_STATUS=0x0000000000000000','GXOS_NET10:GETSYSTEMINFO_RETURNED',$successMarker,'GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE','GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetNumaHighestNodeNumber') $run.RunId
        if ($Mode -eq 'MarkerMutation' -and (Has $text 'GXOS_NET10:GETSYSTEMINFO_OK')) { Fail "$($run.RunId) marker mutation leaked the positive marker" }
        if ((Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_STRUCTURE_SIZE=0x') -ne 0x30 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_DESTINATION_ALIGNMENT=0x') -ne 0 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_ARCHITECTURE=0x') -ne 9 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_RESERVED_ARCHITECTURE=0x') -ne 0 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_PAGE_SIZE=0x') -ne 0x1000 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_ACTIVE_MASK=0x') -ne 1 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_PROCESSOR_COUNT=0x') -ne 1 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_PROCESSOR_TYPE=0x') -ne 8664 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_ALLOCATION_GRANULARITY=0x') -ne 0x1000 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_PROCESSOR_LEVEL=0x') -ne 0 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_PROCESSOR_REVISION=0x') -ne 0 -or
            (Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_FIELD_READ_MASK=0x') -ne 0xA2) { Fail "$($run.RunId) system-info snapshot invalid" }
        $min = Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_MIN_ADDRESS=0x'; $max = Read-Hex $text 'GXOS_NET10:GETSYSTEMINFO_MAX_ADDRESS=0x'
        if ($null -eq $min -or $null -eq $max -or $min -eq 0 -or $min -gt $max) { Fail "$($run.RunId) application range invalid" }
        if (-not (Has $text 'GXOS_NET10:GETSYSTEMINFO_DESTINATION_WRITABLE=1')) { Fail "$($run.RunId) destination was not proven writable" }
        if (-not (Has $text 'GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000') -or
            -not (Has $text 'GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000') -or
            -not (Has $text 'GXOS_NET10:MANAGED_THREAD_REGISTERED=0') -or
            -not (Has $text 'GXOS_NET10:GC_CONTRACT_INITIALIZED=0') -or
            -not (Has $text 'GXOS_NET10:GC_HEAP_USABLE=0') -or
            -not (Has $text 'GXOS_NET10:ALLOCATION_CONTEXT_VALID=0') -or
            -not (Has $text 'GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) { Fail "$($run.RunId) runtime-state accounting invalid" }
    }
}
if (@($runIds | Select-Object -Unique).Count -ne $runIds.Count) { Fail 'duplicate run ID' }
if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Fail 'duplicate QEMU PID' }
if (@($hashes | Select-Object -Unique).Count -ne 1) { Fail 'artifact set changed across runs' }
if ($failures.Count -ne 0) {
    $failures | ForEach-Object { Write-Output "GETSYSTEMINFO_EVIDENCE_FAILURE=$_" }
    exit 2
}
Write-Output "GETSYSTEMINFO_EVIDENCE_ROOT=$root"
Write-Output "GETSYSTEMINFO_EVIDENCE_VALIDATION=PASSED"

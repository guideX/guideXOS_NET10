[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EvidenceRoot,
    [ValidateSet('Positive', 'Disabled')] [string]$Mode = 'Positive',
    [int]$ExpectedRunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($EvidenceRoot)
$failures = New-Object System.Collections.Generic.List[string]
function Add-Failure([string]$message) { [void]$failures.Add($message) }
function Read-Json([string]$path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
function Has-Text([string]$text, [string]$value) { return $text.IndexOf($value, [StringComparison]::Ordinal) -ge 0 }
function Read-Hex([string]$text, [string]$prefix) {
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if (-not $match.Success) { return $null }; return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}
function Read-Last-Hex([string]$text, [string]$prefix) {
    $matches = [regex]::Matches($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if ($matches.Count -eq 0) { return $null }; return [Convert]::ToUInt64($matches[$matches.Count - 1].Groups[1].Value, 16)
}
function Read-Decimal([string]$text, [string]$prefix) {
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '([0-9]+)')
    if (-not $match.Success) { return $null }; return [Convert]::ToUInt64($match.Groups[1].Value, 10)
}
function Require-Ordered([string]$text, [string[]]$tokens, [string]$label) {
    $position = -1
    foreach ($token in $tokens) {
        $next = $text.IndexOf($token, [Math]::Max(0, $position + 1), [StringComparison]::Ordinal)
        if ($next -lt 0) { Add-Failure "$label missing or out of order: $token"; return }; $position = $next
    }
}
function Read-RunText($run) {
    $path = Join-Path $root ([string]$run.SerialLog)
    if (-not (Test-Path -LiteralPath $path)) { Add-Failure "serial log missing: $path"; return '' }
    return Get-Content -LiteralPath $path -Raw
}
function Read-Int32FromHex([uint64]$value) {
    if ($value -gt 0x7FFFFFFF) { return [int](([int64]$value) - 0x100000000) }; return [int]$value
}
function Validate-CrtStricmpBlocks([string]$text, [string]$runId) {
    $matches = [regex]::Matches($text, '(?s)GXOS_NET10:CRT_STRICMP_BEGIN\r?\n.*?GXOS_NET10:CRT_STRICMP_OK\r?\n')
    if ($matches.Count -ne 885) { Add-Failure "$runId _stricmp call block count is $($matches.Count), expected 885" }
    $expectedIndex = [uint64]0
    foreach ($match in $matches) {
        $block = $match.Value
        $index = Read-Hex $block 'GXOS_NET10:CRT_STRICMP_CALL_INDEX=0x'
        if ($null -eq $index -or $index -ne $expectedIndex) { Add-Failure "$runId _stricmp call index is not consecutive at $expectedIndex"; break }
        $expectedIndex++
        foreach ($marker in @(
            'GXOS_NET10:CRT_STRICMP_LOCALE=C_DEFAULT_NO_LOCALE_CHANGE',
            'GXOS_NET10:CRT_STRICMP_STRING1_REGION_IMAGE_BACKED=1',
            'GXOS_NET10:CRT_STRICMP_STRING2_REGION_IMAGE_BACKED=1',
            'GXOS_NET10:CRT_STRICMP_STRING1_REGION_READABLE=0x0000000000000001',
            'GXOS_NET10:CRT_STRICMP_STRING2_REGION_READABLE=0x0000000000000001',
            'GXOS_NET10:CRT_STRICMP_STRING1_REGION_EXECUTABLE=0x0000000000000000',
            'GXOS_NET10:CRT_STRICMP_STRING2_REGION_EXECUTABLE=0x0000000000000000',
            'GXOS_NET10:CRT_STRICMP_STRING1_REGION_WRITABLE=0x0000000000000000',
            'GXOS_NET10:CRT_STRICMP_STRING2_REGION_WRITABLE=0x0000000000000000',
            'GXOS_NET10:CRT_STRICMP_STATUS=0x0000000000000000',
            'GXOS_NET10:CRT_STRICMP_CALLER_CONSUMES_SIGN_OR_ZERO=1',
            'GXOS_NET10:CRT_STRICMP_RETURNED')) {
            if (-not (Has-Text $block $marker)) { Add-Failure "$runId call $index missing marker: $marker" }
        }
        if ($block -match 'CRT_STRICMP_INVALID_INPUT|CRT_STRICMP_OX') { Add-Failure "$runId call $index contains invalid or mutated marker" }
        foreach ($prefix in @('GXOS_NET10:CRT_STRICMP_STRING1_POINTER=0x','GXOS_NET10:CRT_STRICMP_STRING2_POINTER=0x','GXOS_NET10:CRT_STRICMP_STRING1_TERMINATOR=0x','GXOS_NET10:CRT_STRICMP_STRING2_TERMINATOR=0x')) {
            if ((Read-Hex $block $prefix) -eq 0) { Add-Failure "$runId call $index has zero pointer field: $prefix" }
        }
        foreach ($prefix in @('GXOS_NET10:CRT_STRICMP_STRING1_LENGTH=0x','GXOS_NET10:CRT_STRICMP_STRING2_LENGTH=0x','GXOS_NET10:CRT_STRICMP_BYTES_EXAMINED=0x','GXOS_NET10:CRT_STRICMP_COMPARED_PREFIX=0x')) {
            if ($null -eq (Read-Hex $block $prefix)) { Add-Failure "$runId call $index missing census field: $prefix" }
        }
        foreach ($prefix in @('GXOS_NET10:CRT_STRICMP_STRING1_PREVIEW=','GXOS_NET10:CRT_STRICMP_STRING2_PREVIEW=')) {
            if ($block -notmatch ([regex]::Escape($prefix) + '"(?:[^"\\]|\\.)*"')) { Add-Failure "$runId call $index missing escaped preview" }
        }
        $resultValue = Read-Hex $block 'GXOS_NET10:CRT_STRICMP_RESULT=0x'
        $category = [regex]::Match($block, 'CRT_STRICMP_RESULT_CATEGORY=([^\r\n]+)').Groups[1].Value
        if ($null -eq $resultValue) { Add-Failure "$runId call $index missing result" }
        else {
            $result = Read-Int32FromHex $resultValue
            $expectedCategory = if ($result -lt 0) { 'LESS' } elseif ($result -gt 0) { 'GREATER' } else { 'EQUAL' }
            if ($category -ne $expectedCategory) { Add-Failure "$runId call $index result category mismatch" }
        }
    }
}

$manifestPath = Join-Path $root 'artifact-manifest.json'; $contextPath = Join-Path $root 'validation-context.json'; $runsPath = Join-Path $root 'runs'
if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $contextPath) -or -not (Test-Path -LiteralPath $runsPath)) { Add-Failure 'evidence root is incomplete' }
$observedBoundary = $null
if ($failures.Count -eq 0) {
    $manifest = Read-Json $manifestPath; $context = Read-Json $contextPath
    $expectedBoundary = if ($Mode -eq 'Positive') { 'KERNEL32.dll!GetSystemInfo' } else { 'api-ms-win-crt-string-l1-1-0.dll!_stricmp' }
    $manifestBoundary = if ($null -ne $manifest.PSObject.Properties['ExpectedBoundary']) { [string]$manifest.ExpectedBoundary } else { '' }
    $contextBoundary = if ($null -ne $context.PSObject.Properties['ExpectedBoundary']) { [string]$context.ExpectedBoundary } else { '' }
    if ([string]$manifest.Mode -ne $Mode -or $manifestBoundary -ne $expectedBoundary) { Add-Failure 'manifest mode or expected boundary mismatch' }
    if ([int]$context.RunCount -ne $ExpectedRunCount -or $contextBoundary -ne $expectedBoundary) { Add-Failure 'validation context mismatch' }
    $runDirectories = @(Get-ChildItem -LiteralPath $runsPath -Directory | Sort-Object Name)
    if ($runDirectories.Count -ne $ExpectedRunCount) { Add-Failure "run directory count mismatch: $($runDirectories.Count)" }
    $runRecords = New-Object System.Collections.Generic.List[object]
    foreach ($directory in $runDirectories) {
        $runPath = Join-Path $directory.FullName 'run.json'
        if (-not (Test-Path -LiteralPath $runPath)) { Add-Failure "run metadata missing: $runPath"; continue }
        [void]$runRecords.Add((Read-Json $runPath))
    }
    $ids = @($runRecords | ForEach-Object { [string]$_.RunId }); $pids = @($runRecords | ForEach-Object { [string]$_.QemuPid })
    if (@($ids | Select-Object -Unique).Count -ne $ids.Count) { Add-Failure 'duplicate run IDs' }
    if (@($pids | Select-Object -Unique).Count -ne $pids.Count) { Add-Failure 'duplicate QEMU process IDs' }
    foreach ($artifact in @($manifest.Artifacts)) {
        if (-not (Test-Path -LiteralPath ([string]$artifact.Path))) { Add-Failure "artifact missing: $($artifact.Kind)"; continue }
        $current = Get-Item -LiteralPath ([string]$artifact.Path); $hash = (Get-FileHash -LiteralPath $current.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne [string]$artifact.Sha256 -or [int64]$current.Length -ne [int64]$artifact.Length -or $current.LastWriteTimeUtc.ToString('o') -ne [string]$artifact.LastWriteTimeUtc) { Add-Failure "artifact hash, length, or timestamp mismatch: $($artifact.Kind)" }
    }
    foreach ($run in $runRecords) {
        $runId = [string]$run.RunId
        if ([string]$run.EvidenceId -ne [string]$manifest.EvidenceId) { Add-Failure "stale evidence ID: $runId" }
        if ($runId -ne "$($manifest.EvidenceId)-run$([int]$run.Sequence)") { Add-Failure "stale run ID: $runId" }
        if (-not [bool]$run.CleanupComplete -or -not [bool]$run.Pass) { Add-Failure "run incomplete: $runId" }
        $text = Read-RunText $run; if ([string]::IsNullOrEmpty($text)) { continue }
        if ((Has-Text $text 'FAULT_VECTOR=') -or (Has-Text $text 'GXOS_NET10:FAIL:')) { Add-Failure "processor fault or fail marker: $runId" }
        $boundaryMatches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
        if ($boundaryMatches.Count -eq 0) { Add-Failure "missing import boundary: $runId" }
        else {
            $boundary = $boundaryMatches[$boundaryMatches.Count - 1].Groups[1].Value
            if ($null -eq $observedBoundary) { $observedBoundary = $boundary } elseif ($observedBoundary -ne $boundary) { Add-Failure "boundary changed across runs: $runId" }
            if ($boundary -ne $expectedBoundary -or [string]$run.Boundary -ne $expectedBoundary) { Add-Failure "wrong boundary for ${runId}: $boundary" }
        }
        $expectedFunctional = if ($Mode -eq 'Positive') { 29 } else { 28 }
        $expectedFailfast = if ($Mode -eq 'Positive') { 95 } else { 96 }
        if ((Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -ne $expectedFunctional -or (Read-Decimal $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -ne $expectedFailfast -or (Read-Decimal $text 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=') -ne 0) { Add-Failure "import census invalid: $runId" }
        if ((Read-Hex $text 'GXOS_NET10:QPC_COUNT=0x') -ne 2 -or (Read-Hex $text 'GXOS_NET10:QPC_REGRESSIONS=0x') -ne 0) { Add-Failure "QPC summary invalid: $runId" }
        foreach ($marker in @('GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000','GXOS_NET10:MANAGED_THREAD_REGISTERED=0','GXOS_NET10:ALLOCATION_CONTEXT_VALID=0','GXOS_NET10:GC_CONTRACT_INITIALIZED=0','GXOS_NET10:GC_HEAP_USABLE=0','GXOS_NET10:ALLOCATION_CONTEXT_CREATED=0','GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) { if (-not (Has-Text $text $marker)) { Add-Failure "$runId missing state marker: $marker" } }
        if ($Mode -eq 'Disabled') {
            if ((Has-Text $text 'GXOS_NET10:CRT_STRICMP_BEGIN') -or (Has-Text $text 'GXOS_NET10:CRT_STRICMP_OK')) { Add-Failure "disabled route executed _stricmp: $runId" }
            Require-Ordered $text @('GXOS_NET10:CRT_STRLEN_OK','GXOS_NET10:GETENV_BEGIN','GXOS_NET10:GETENV_OK','GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-string-l1-1-0.dll!_stricmp') $runId
            continue
        }
        if (-not (Has-Text $text 'GXOS_NET10:CRT_STRICMP_VALIDATION_CONTEXT_OK')) { Add-Failure "$runId missing checked context marker" }
        Require-Ordered $text @('GXOS_NET10:CRT_STRLEN_OK','GXOS_NET10:GETENV_NAME_TEXT="DOTNET_gcServer"','GXOS_NET10:GETENV_OK','GXOS_NET10:CRT_STRICMP_BEGIN','GXOS_NET10:CRT_STRICMP_OK','GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetSystemInfo') $runId
        Validate-CrtStricmpBlocks $text $runId
        if ((Read-Last-Hex $text 'GXOS_NET10:CRT_STRICMP_CALL_COUNT=0x') -ne 0x375 -or (Read-Last-Hex $text 'GXOS_NET10:CRT_STRICMP_SUCCESS_COUNT=0x') -ne 0x375 -or (Read-Last-Hex $text 'GXOS_NET10:CRT_STRICMP_FAILURE_COUNT=0x') -ne 0 -or (Read-Last-Hex $text 'GXOS_NET10:CRT_STRICMP_TOTAL_BYTES=0x') -ne 0x362A -or (Read-Last-Hex $text 'GXOS_NET10:CRT_STRICMP_LONGEST_PREFIX=0x') -ne 0x15) { Add-Failure "_stricmp summary invalid: $runId" }
        if ((Read-Last-Hex $text 'GXOS_NET10:GETENV_CALL_COUNT=0x') -ne 0x49 -or (Read-Last-Hex $text 'GXOS_NET10:GETENV_MISSING_COUNT=0x') -ne 0x49 -or (Read-Last-Hex $text 'GXOS_NET10:GETENV_SUCCESS_COUNT=0x') -ne 0) { Add-Failure "environment census invalid after _stricmp route: $runId" }
        if ((Has-Text $text 'GXOS_NET10:CRT_STRICMP_OK') -and ($text.IndexOf('GXOS_NET10:CRT_STRICMP_OK', 0, [StringComparison]::Ordinal) -gt $text.IndexOf('GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetSystemInfo', 0, [StringComparison]::Ordinal))) { Add-Failure "_stricmp continued after next dependency: $runId" }
    }
}
if ($failures.Count -ne 0) {
    [PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; Failures = @($failures) } | ConvertTo-Json -Depth 8
    exit 2
}
[PSCustomObject]@{ EvidenceRoot = $root; Mode = $Mode; ExpectedRunCount = $ExpectedRunCount; RunCount = $ExpectedRunCount; Boundary = $observedBoundary; Passed = $true; Failures = @() } | ConvertTo-Json -Depth 8
Write-Output 'CRT_STRICMP_EVIDENCE_VALIDATION=PASSED'

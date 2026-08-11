[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$expectedPayloadHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
if (!(Test-Path -LiteralPath $efi) -or !(Test-Path -LiteralPath $payload)) {
    throw 'Build the VirtualMemory harness first.'
}
if ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -ne
    $expectedPayloadHash) { throw 'The staged payload hash is not immutable.' }
if (Test-Path -LiteralPath $evidence) { throw "Evidence directory already exists: $evidence" }
if ($RunCount -lt 3) { throw 'At least three fresh boots are required.' }
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (!(Test-Path -LiteralPath $qemu)) { throw 'qemu-system-x86_64.exe is required.' }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (!(Test-Path -LiteralPath $ovmf) -or !(Test-Path -LiteralPath $varsTemplate)) {
    throw 'OVMF firmware files are required.'
}
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}
function Read-Serial([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return '' }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            [IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object IO.StreamReader($stream)
            try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    } catch { return '' }
}
function Get-Hex([string]$text, [string]$prefix) {
    $key = $prefix.TrimEnd('=')
    $match = [regex]::Match($text, [regex]::Escape($key) + '=(?<value>0x[0-9A-Fa-f]+|[0-9]+)')
    if (!$match.Success) { throw "Missing field: $key" }
    $value = $match.Groups['value'].Value
    if ($value.StartsWith('0x')) { return [Convert]::ToUInt64($value.Substring(2), 16) }
    return [Convert]::ToUInt64($value, 10)
}
function Get-OptionalHex([string]$text, [string]$prefix) {
    $key = $prefix.TrimEnd('=')
    $match = [regex]::Match($text, [regex]::Escape($key) + '=(?<value>0x[0-9A-Fa-f]+|[0-9]+)')
    if (!$match.Success) { return $null }
    $value = $match.Groups['value'].Value
    if ($value.StartsWith('0x')) { return [Convert]::ToUInt64($value.Substring(2), 16) }
    return [Convert]::ToUInt64($value, 10)
}
function Get-Blocks([string]$text, [string]$begin, [string]$end) {
    return @([regex]::Matches($text,
        [regex]::Escape($begin) + '\r?\n(?<body>.*?' +
        [regex]::Escape($end) + '\r?\n)',
        [Text.RegularExpressions.RegexOptions]::Singleline) | ForEach-Object {
            $_.Groups['body'].Value
        })
}
function Stop-OwnedQemu([System.Diagnostics.Process]$process) {
    try { $process.Refresh() } catch { }
    try {
        if (!$process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    } catch { }
    try { $process.WaitForExit(5000) | Out-Null } catch { }
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        throw "Owned QEMU process remained: $($process.Id)"
    }
}

$ownedProcesses = @()
try {
    Require ((@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0)) `
        'A pre-existing QEMU process is present.'
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require ((@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0)) `
            "QEMU already exists before fresh boot $sequence."
        $run = Join-Path $evidence ("runs\run-{0}" -f $sequence)
        New-Item -ItemType Directory -Force -Path $run | Out-Null
        $code = Join-Path $run 'edk2-code.fd'; $vars = Join-Path $run 'edk2-vars.fd'
        $serialPath = Join-Path $run 'serial.log'
        $stdout = Join-Path $run 'qemu.stdout.log'; $stderr = Join-Path $run 'qemu.stderr.log'
        Copy-Item -LiteralPath $ovmf -Destination $code
        Copy-Item -LiteralPath $varsTemplate -Destination $vars
        $arguments = @(
            '-machine', 'q35', '-accel', 'tcg,thread=multi', '-m', '128M',
            '-drive', "if=pflash,format=raw,readonly=on,file=$code",
            '-drive', "if=pflash,format=raw,file=$vars",
            '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
            '-rtc', 'base=utc,clock=vm', '-boot', 'order=c', '-serial', "file:$serialPath",
            '-monitor', 'none', '-display', 'none', '-no-reboot', '-no-shutdown')
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
        $ownedProcesses += $process
        try {
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while ((Get-Date) -lt $deadline) {
                $text = Read-Serial $serialPath
                if (($text.Contains('GXOS_NET10:IMPORT_BLOCKER_DLL=') -and
                     $text.Contains('GXOS_NET10:VIRTUALALLOC_RETURNED')) -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
                    $text.Contains('GXOS_NET10:FAIL=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 100
            }
        } finally {
            Stop-OwnedQemu $process
        }
        Start-Sleep -Milliseconds 250
        $text = Read-Serial $serialPath
        $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
        Require ($hash -eq $expectedPayloadHash) "run $sequence payload hash changed"
        Require (!$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:FAIL=')) "run $sequence fault/fail marker"
        Require ($text.Contains('GXOS_NET10:FIRMWARE_MEASURED_MEMORY_MAP_VALID=1') -and
                 $text.Contains('GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=0')) `
            "run $sequence VM/accounting/import initialization"
        Require ((Get-Hex $text 'GXOS_NET10:PE_IMPORT_SYMBOLS=') -eq 124) `
            "run $sequence import symbol count"
        Require ((Get-Hex $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -eq 49) `
            "run $sequence functional import count"
        Require ((Get-Hex $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -eq 75) `
            "run $sequence failfast import count"
        Require ((Get-Hex $text 'GXOS_NET10:VIRTUALALLOC_IMPORT_DESCRIPTOR_INDEX=') -eq 2) `
            "run $sequence VirtualAlloc descriptor"
        Require ((Get-Hex $text 'GXOS_NET10:VIRTUALALLOC_IMPORT_SYMBOL_INDEX=') -eq 0x18) `
            "run $sequence VirtualAlloc symbol index"
        Require ((Get-Hex $text 'GXOS_NET10:VIRTUALALLOC_IMPORT_IAT_RVA=') -eq 0x7D0F8) `
            "run $sequence VirtualAlloc IAT"
        Require ((Get-Hex $text 'GXOS_NET10:VIRTUALFREE_IMPORT_DESCRIPTOR_INDEX=') -eq 2) `
            "run $sequence VirtualFree descriptor"
        Require ((Get-Hex $text 'GXOS_NET10:VIRTUALFREE_IMPORT_SYMBOL_INDEX=') -eq 0x19) `
            "run $sequence VirtualFree symbol index"
        Require ((Get-Hex $text 'GXOS_NET10:VIRTUALFREE_IMPORT_IAT_RVA=') -eq 0x7D100) `
            "run $sequence VirtualFree IAT"
        $allocBlocks = @(Get-Blocks $text 'GXOS_NET10:VIRTUALALLOC_BEGIN' 'GXOS_NET10:VIRTUALALLOC_RETURNED')
        $freeBlocks = @(Get-Blocks $text 'GXOS_NET10:VIRTUALFREE_BEGIN' 'GXOS_NET10:VIRTUALFREE_RETURNED')
        Require ($allocBlocks.Count -ge 1) "run $sequence reached VirtualAlloc"
        $first = $allocBlocks[0]
        Require ((Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_CALLER_RVA=') -eq 0x438A8) `
            "run $sequence first VirtualAlloc caller"
        Require ((Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_ADDRESS=') -eq 0 -and
                 (Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_SIZE=') -eq 0x1000 -and
                 (Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_ALLOCATION_TYPE=') -eq 0x202000 -and
                 (Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_PROTECTION=') -eq 4) `
            "run $sequence first write-watch arguments"
        Require ((Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_STATUS=') -eq 2 -and
                 (Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_RETURN=') -eq 0 -and
                 (Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_LAST_ERROR=') -eq 50 -and
                 (Get-Hex $first 'GXOS_NET10:VIRTUALALLOC_FAILURE_STATE_UNCHANGED=') -eq 1) `
            "run $sequence write-watch rejection"
        Require ($text.Contains('GXOS_NET10:VIRTUALALLOC_WRITE_WATCH_REJECTED=1') -and
                 $text.Contains('GXOS_NET10:VIRTUALALLOC_FALLBACK_RETURNED_NULL=1') -and
                 $text.Contains('GXOS_NET10:VIRTUALALLOC_WRITE_WATCH_STATE_UNCHANGED=1') -and
                 $text.Contains('GXOS_NET10:VIRTUALALLOC_FALLBACK_OBSERVED=1')) `
            "run $sequence fallback evidence"
        $supported = @($allocBlocks | Where-Object {
            (Get-Hex $_ 'GXOS_NET10:VIRTUALALLOC_SUPPORTED=') -eq 1
        })
        foreach ($block in $supported) {
            if ((Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_COMMITTED=') -eq 1) {
                Require ((Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_BACKING_PROOF=') -eq 1 -and
                         (Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_MAPPING_PROOF=') -eq 1 -and
                         (Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_NX_PROOF=') -eq 1) `
                    "run $sequence commit mapping proof"
                if ((Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_NEW_PAGES=') -ne 0) {
                    Require ((Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_ZERO_FILL_PROOF=') -eq 1) `
                        "run $sequence zero-fill proof"
                }
            } else {
                Require ((Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_NEW_PAGES=') -eq 0 -and
                         (Get-Hex $block 'GXOS_NET10:VIRTUALALLOC_FAILURE_STATE_UNCHANGED=') -eq 1) `
                    "run $sequence reserve-only proof"
            }
        }
        $boundary = if ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_DLL=')) {
            $dll = [regex]::Match($text, 'GXOS_NET10:IMPORT_BLOCKER_DLL=(?<v>[^\r\n]+)').Groups['v'].Value
            $symbol = [regex]::Match($text, 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL=(?<v>[^\r\n]+)').Groups['v'].Value
            "$dll!$symbol"
        } elseif ($text.Contains('GXOS_NET10:NATIVEAOT_STARTUP_RETURN=')) {
            'NativeAOT startup returned'
        } else {
            'No boundary marker before timeout/process exit'
        }
        $firstSupported = if ($text.Contains('GXOS_NET10:VIRTUALALLOC_FIRST_REAL_RESERVATION=1')) { 'reservation' } else { 'none' }
        if ($text.Contains('GXOS_NET10:VIRTUALALLOC_FIRST_REAL_COMMIT=1')) { $firstSupported += '+commit' }
        $markerNames = @('NATIVEAOT_STARTUP_OK', 'GC_STARTUP_ADVANCED',
            'MANAGED_THREAD_REGISTERED', 'ALLOCATION_CONTEXT_VALID',
            'GC_CONTRACT_INITIALIZED', 'GC_HEAP_USABLE',
            'ALLOCATION_CONTEXT_CREATED', 'MANAGED_ALLOCATION_COUNT')
        $markerState = ($markerNames | ForEach-Object {
            $match = [regex]::Match($text, 'GXOS_NET10:' + [regex]::Escape($_) + '=(?<v>[^\r\n]+)')
            if ($match.Success) {
                "$_=$($match.Groups['v'].Value)"
            } elseif ($text.Contains("GXOS_NET10:${_}`r`n") -or
                      $text.Contains("GXOS_NET10:${_}`n")) {
                "$_=1"
            } else {
                "$_=absent"
            }
        }) -join ','
        $schedulerState = if ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_SCHEDULER_THREAD=')) {
            $thread = [regex]::Match($text, 'GXOS_NET10:IMPORT_BLOCKER_SCHEDULER_THREAD=(?<v>[^\r\n]+)').Groups['v'].Value
            "thread=$thread;main=$((Get-OptionalHex $text 'GXOS_NET10:IMPORT_BLOCKER_MAIN_STATE='));worker=$((Get-OptionalHex $text 'GXOS_NET10:IMPORT_BLOCKER_WORKER_STATE='));runnable=$((Get-OptionalHex $text 'GXOS_NET10:IMPORT_BLOCKER_RUNNABLE_COUNT='));blocked=$((Get-OptionalHex $text 'GXOS_NET10:IMPORT_BLOCKER_BLOCKED_COUNT='))"
        } else { 'not-reached' }
        Write-Output "VIRTUAL_MEMORY_RUN_${sequence}=PASSED allocCalls=$($allocBlocks.Count) freeCalls=$($freeBlocks.Count) firstSupported=$firstSupported boundary=$boundary markers=$markerState scheduler=$schedulerState serial=$serialPath"
    }
} finally {
    foreach ($process in $ownedProcesses) { Stop-OwnedQemu $process }
}
Require ((@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0)) 'QEMU cleanup failed.'
Write-Output "VIRTUAL_MEMORY_RUNS=$RunCount"
Write-Output "VIRTUAL_MEMORY_PAYLOAD_SHA256=$expectedPayloadHash"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$expectedPayloadHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
if (!(Test-Path -LiteralPath (Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI')) -or
    !(Test-Path -LiteralPath $payload)) { throw 'Build the GlobalMemoryStatusEx harness first.' }
if ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -ne
    $expectedPayloadHash) { throw 'The staged payload hash is not the required immutable payload.' }
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
    $prefix = $prefix.TrimEnd('=')
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '=(?<value>0x[0-9A-Fa-f]+|[0-9]+)')
    if (!$match.Success) { throw "Missing field: $prefix" }
    $value = $match.Groups['value'].Value
    if ($value.StartsWith('0x')) { return [Convert]::ToUInt64($value.Substring(2), 16) }
    return [Convert]::ToUInt64($value, 10)
}
function Get-OptionalHex([string]$text, [string]$prefix) {
    $prefix = $prefix.TrimEnd('=')
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '=(?<value>0x[0-9A-Fa-f]+|[0-9]+)')
    if (!$match.Success) { return $null }
    $value = $match.Groups['value'].Value
    if ($value.StartsWith('0x')) { return [Convert]::ToUInt64($value.Substring(2), 16) }
    return [Convert]::ToUInt64($value, 10)
}
function Get-Blocks([string]$text) {
    return @([regex]::Matches($text,
        'GXOS_NET10:GLOBALMEMORYSTATUSEX_BEGIN\r?\n(?<body>.*?GXOS_NET10:GLOBALMEMORYSTATUSEX_RETURNED\r?\n)',
        [Text.RegularExpressions.RegexOptions]::Singleline) | ForEach-Object {
            $_.Groups['body'].Value
        })
}
function Stop-OwnedQemu([System.Diagnostics.Process]$process) {
    try { $process.Refresh() } catch { }
    try { if (!$process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } } catch { }
    try { $process.WaitForExit(5000) | Out-Null } catch { }
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        throw "Owned QEMU process remained: $($process.Id)"
    }
}

$ownedProcesses = @()
$runSummaries = @()
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
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
        $ownedProcesses += $process
        try {
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while ((Get-Date) -lt $deadline) {
                $text = Read-Serial $serialPath
                if (($text.Contains('GXOS_NET10:IMPORT_BLOCKER_DLL=') -and
                     $text.Contains('GXOS_NET10:GLOBALMEMORYSTATUSEX_INVOCATION_COUNT=')) -or
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
                 $text.Contains('GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=0')) "run $sequence accounting/import init"
        Require ((Get-Hex $text 'GXOS_NET10:PE_IMPORT_SYMBOLS=') -eq 124) "run $sequence import symbols"
        Require ((Get-Hex $text 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=') -eq 47) "run $sequence functional imports"
        Require ((Get-Hex $text 'GXOS_NET10:PE_IMPORT_FAILFAST=') -eq 77) "run $sequence failfast imports"
        Require ((Get-Hex $text 'GXOS_NET10:GLOBALMEMORYSTATUSEX_IMPORT_DESCRIPTOR_INDEX=') -eq 2) "run $sequence descriptor"
        Require ((Get-Hex $text 'GXOS_NET10:GLOBALMEMORYSTATUSEX_IMPORT_SYMBOL_INDEX=') -eq 0x44) "run $sequence symbol index"
        Require ((Get-Hex $text 'GXOS_NET10:GLOBALMEMORYSTATUSEX_IMPORT_IAT_RVA=') -eq 0x7D258) "run $sequence IAT RVA"
        $blocks = Get-Blocks $text
        Require ($blocks.Count -eq 3) "run $sequence natural GlobalMemoryStatusEx count"
        $callers = @()
        $generations = @()
        foreach ($block in $blocks) {
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_STRUCTURE_SIZE=') -eq 0x40) "run $sequence ABI size"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_DWLENGTH=') -eq 0x40) "run $sequence input length"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_INPUT_RANGE_VALID=') -eq 1) "run $sequence input range"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_WRITTEN=') -eq 1) "run $sequence output write"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_STATUS=') -eq 0) "run $sequence status"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_RETURN_VALUE=') -eq 1) "run $sequence BOOL"
            $returnAddress = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_RETURN_ADDRESS='
            $callSite = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_RUNTIME_CALL_SITE='
            Require ($returnAddress -eq $callSite + 6) "run $sequence return address"
            $callers += Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_CALLER_RVA='
            $generations += Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_ACCOUNTING_GENERATION='
            $totalPhys = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_TOTAL_PHYS='
            $availPhys = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_AVAIL_PHYS='
            $load = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_MEMORY_LOAD='
            $commitLimit = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_TOTAL_PAGEFILE='
            $availCommit = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_AVAIL_PAGEFILE='
            $virtualTotal = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_TOTAL_VIRTUAL='
            $virtualAvail = Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_AVAIL_VIRTUAL='
            Require ($totalPhys -gt 0 -and $availPhys -le $totalPhys) "run $sequence physical invariants"
            Require ($load -le 100) "run $sequence load bounds"
            Require ($availCommit -le $commitLimit) "run $sequence commit invariants"
            Require ($virtualAvail -le $virtualTotal) "run $sequence virtual invariants"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_DWLENGTH=') -eq 0x40) "run $sequence output length"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_AVAIL_EXTENDED_VIRTUAL=') -eq 0) "run $sequence extended virtual"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_TOTAL_PAGEFILE=') -eq (Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_COMMIT_LIMIT=')) "run $sequence pagefile/commit-limit mapping"
            Require ((Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_OUTPUT_AVAIL_PAGEFILE=') -eq (Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_AVAILABLE_COMMIT=')) "run $sequence pagefile/available-commit mapping"
            Require ($virtualTotal -eq (Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_VIRTUAL_TOTAL=') -and
                     $virtualAvail -eq (Get-Hex $block 'GXOS_NET10:GLOBALMEMORYSTATUSEX_VIRTUAL_AVAILABLE=')) "run $sequence bounded arena mapping"
            $expectedLoad = [math]::Floor((100.0 * ($totalPhys - $availPhys)) / $totalPhys)
            Require ($load -eq $expectedLoad) "run $sequence load formula"
        }
        Require ($callers.Count -eq 3 -and $callers[0] -eq 0x43361 -and
                 $callers[1] -eq 0x433A4 -and $callers[2] -eq 0x43430) "run $sequence natural caller order"
        Require (!$text.Contains('GLOBALMEMORYSTATUSEX_CALLER_RVA=0x000000000004313E')) "run $sequence did not reach 0x4313E"
        Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_DLL=KERNEL32.dll') -and
                 $text.Contains('GXOS_NET10:IMPORT_BLOCKER_SYMBOL=VirtualAlloc')) "run $sequence next boundary"
        Require ((Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX=') -eq 2) "run $sequence next descriptor"
        Require ((Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX=') -eq 0x18) "run $sequence next symbol"
        Require ((Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA=') -eq 0x7D0F8) "run $sequence next IAT"
        Require ((Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA=') -eq 0x438A8) "run $sequence next caller"
        Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_SCHEDULER_THREAD=main') -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_MAIN_STATE=') -eq 3 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_WORKER_STATE=') -eq 2 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_WORKER_PRIORITY=') -eq 2 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_WORKER_SUSPEND_COUNT=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_WORKER_RUNNABLE=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_WORKER_EXECUTION_COUNT=') -eq 0) "run $sequence scheduler state"
        Require ((Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_RUNNABLE_COUNT=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_BLOCKED_COUNT=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_LIVE_OBJECT_COUNT=') -eq 5 -and
                 (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_LIVE_PUBLIC_HANDLE_COUNT=') -eq 4) "run $sequence scheduler counts"
        Require ($text.Contains('GXOS_NET10:MANAGED_THREAD_REGISTERED=0') -and
                 $text.Contains('GXOS_NET10:GC_CONTRACT_INITIALIZED=0') -and
                 $text.Contains('GXOS_NET10:GC_HEAP_USABLE=0') -and
                 $text.Contains('GXOS_NET10:MANAGED_ALLOCATION_COUNT=0')) "run $sequence GC state"
        $runSummaries += [pscustomobject]@{Run=$sequence; Serial=$serialPath; PayloadSha256=$hash; Callers=($callers -join ','); Generations=($generations -join ','); NextBoundary='KERNEL32.dll!VirtualAlloc'}
        Write-Output "GLOBALMEMORYSTATUSEX_ENABLED_RUN_${sequence}=PASSED serial=$serialPath"
    }
} finally {
    foreach ($process in $ownedProcesses) { Stop-OwnedQemu $process }
}
Require ((@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0)) 'QEMU cleanup failed.'
Write-Output "GLOBALMEMORYSTATUSEX_ENABLED_RUNS=$RunCount"
Write-Output "GLOBALMEMORYSTATUSEX_PAYLOAD_SHA256=$expectedPayloadHash"
$runSummaries | Format-Table -AutoSize

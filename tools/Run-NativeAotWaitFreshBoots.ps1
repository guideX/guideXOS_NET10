[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$builtPayload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$sourcePayload = Join-Path $root 'artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll'
$expectedHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'

if ($RunCount -lt 3) { throw 'At least three fresh boots are required.' }
if (!(Test-Path -LiteralPath $efi) -or !(Test-Path -LiteralPath $builtPayload)) {
    throw 'NativeAOT harness EFI or payload is missing.'
}
if (!(Test-Path -LiteralPath $sourcePayload)) { throw 'Source payload is missing.' }
if ((Get-FileHash -LiteralPath $sourcePayload -Algorithm SHA256).Hash.ToUpperInvariant() -ne $expectedHash) {
    throw 'Source payload hash is not the required immutable value.'
}
if ((Get-FileHash -LiteralPath $builtPayload -Algorithm SHA256).Hash.ToUpperInvariant() -ne $expectedHash) {
    throw 'Staged payload hash is not the required immutable value.'
}
if (Test-Path -LiteralPath $evidence) { throw "Evidence directory already exists: $evidence" }

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) {
    [IO.Path]::GetFullPath($qemuCommand.Source)
} else {
    'C:\Program Files\qemu\qemu-system-x86_64.exe'
}
if (!(Test-Path -LiteralPath $qemu)) { throw 'qemu-system-x86_64.exe is required.' }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (!(Test-Path -LiteralPath $ovmf) -or !(Test-Path -LiteralPath $varsTemplate)) {
    throw 'OVMF firmware files are required.'
}
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'A pre-existing QEMU process is present.'
}
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

function Read-Serial([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return '' }
    try { return [IO.File]::ReadAllText($path) } catch { return '' }
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

function Get-Hex([string]$text, [string]$prefix, [bool]$required = $true) {
    $key = $prefix.TrimEnd('=')
    $match = [regex]::Match($text,
        [regex]::Escape($key) + '=(?<value>0x[0-9A-Fa-f]+|[0-9]+)')
    if (!$match.Success) {
        if ($required) { throw "Missing field: $key" }
        return $null
    }
    $value = $match.Groups['value'].Value
    if ($value.StartsWith('0x')) {
        return [Convert]::ToUInt64($value.Substring(2), 16)
    }
    return [Convert]::ToUInt64($value, 10)
}

function Get-Text([string]$text, [string]$prefix, [bool]$required = $true) {
    $match = [regex]::Match($text,
        [regex]::Escape($prefix) + '(?<value>[^\r\n]+)')
    if (!$match.Success) {
        if ($required) { throw "Missing field: $prefix" }
        return $null
    }
    return $match.Groups['value'].Value
}

function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
            "QEMU already exists before fresh boot $sequence."
        $run = Join-Path $evidence ("runs\run-{0}" -f $sequence)
        New-Item -ItemType Directory -Force -Path $run | Out-Null
        $code = Join-Path $run 'edk2-code.fd'
        $vars = Join-Path $run 'edk2-vars.fd'
        $serial = Join-Path $run 'serial.log'
        $stdout = Join-Path $run 'qemu.stdout.log'
        $stderr = Join-Path $run 'qemu.stderr.log'
        Copy-Item -LiteralPath $ovmf -Destination $code
        Copy-Item -LiteralPath $varsTemplate -Destination $vars
        $arguments = @(
            '-machine', 'q35', '-accel', 'tcg,thread=multi', '-m', '128M',
            '-drive', "if=pflash,format=raw,readonly=on,file=$code",
            '-drive', "if=pflash,format=raw,file=$vars",
            '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
            '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
            '-serial', "file:$serial", '-monitor', 'none', '-display', 'none',
            '-no-reboot', '-no-shutdown')
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments `
            -WorkingDirectory $gate -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
        $owned += $process
        try {
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while ((Get-Date) -lt $deadline) {
                $text = Read-Serial $serial
                if (($text.Contains('GXOS_NET10:WAITFORSINGLEOBJECTEX_BEGIN') -and
                     ($text.Contains('GXOS_NET10:WAIT_BLOCKED_PROOF=1') -or
                      $text.Contains('GXOS_NET10:WAITFORSINGLEOBJECTEX_RETURNED') -or
                      $text.Contains('GXOS_NET10:IMPORT_BLOCKER_DLL='))) -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
                    $text.Contains('GXOS_NET10:FAIL=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 100
            }
        } finally {
            Stop-OwnedQemu $process
        }
        Start-Sleep -Milliseconds 250
        $text = Read-Serial $serial
        $hash = (Get-FileHash -LiteralPath $builtPayload -Algorithm SHA256).Hash.ToUpperInvariant()
        Require ($hash -eq $expectedHash) "run $sequence payload hash changed"
        Require (!$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:FAIL=')) "run $sequence fault/fail marker"
        Require ($text.Contains('GXOS_NET10:WAITFORSINGLEOBJECTEX_BEGIN')) `
            "run $sequence did not reach WaitForSingleObjectEx"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_CALLER_RVA=') -eq 0x3539C) `
            "run $sequence caller RVA mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_RDX_MILLISECONDS=') -eq [uint64]4294967295) `
            "run $sequence timeout mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_R8_ALERTABLE=') -eq 0) `
            "run $sequence alertable mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_OBJECT_TYPE=') -eq 2) `
            "run $sequence waited object is not Event"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_EVENT_MANUAL_RESET=') -eq 0) `
            "run $sequence event reset mode mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_EVENT_SIGNALED_BEFORE=') -eq 0) `
            "run $sequence event was unexpectedly signaled"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_WILL_BLOCK=') -eq 1) `
            "run $sequence did not prove a scheduler block"
        Require ($text.Contains('GXOS_NET10:WAIT_BLOCKED_PROOF=1')) `
            "run $sequence has no blocked-wait proof"
        Require ((Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RECORD_VALID=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RECORD_ACTIVE=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RECORD_WAITING_IDENTITY=') -eq 2 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RECORD_OBJECT_SLOT=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RECORD_WAITER_LINKED=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RECORD_PIN_HELD=') -eq 1) `
            "run $sequence wait-record proof mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_EVENT_WAITER_COUNT=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_OBJECT_INTERNAL_REFS=') -eq 2 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_ACTIVE_WAIT_COUNT=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RUNNABLE_COUNT=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_BLOCKED_COUNT=') -eq 1) `
            "run $sequence scheduler/reference proof mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_LAST_ERROR_BEFORE=') -eq 0x7F) `
            "run $sequence LastError-before mismatch"
        $nextDll = Get-Text $text 'GXOS_NET10:IMPORT_BLOCKER_DLL=' $false
        $nextSymbol = Get-Text $text 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL=' $false
        $nextBoundary = if ($null -ne $nextDll -and $null -ne $nextSymbol) {
            "$nextDll!$nextSymbol"
        } else { 'WaitForSingleObjectEx remained blocked without a later boundary' }
        $returned = $text.Contains('GXOS_NET10:WAITFORSINGLEOBJECTEX_RETURNED')
        $summary = [ordered]@{
            boot = $sequence
            handle = ('0x{0:X}' -f (Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_RCX_HANDLE='))
            object_slot = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_OBJECT_SLOT='
            object_generation = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_OBJECT_GENERATION='
            object_type = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_OBJECT_TYPE='
            object_state_before = 'nonsignaled'
            timeout = 'INFINITE'
            alertable = $false
            result = if ($returned) { ('0x{0:X}' -f (Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_RETURN_VALUE=')) } else { 'not-returned-blocked' }
            last_error_before = ('0x{0:X}' -f (Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_LAST_ERROR_BEFORE='))
            last_error_after = if ($returned) {
                ('0x{0:X}' -f (Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_LAST_ERROR_AFTER='))
            } else {
                ('0x{0:X}' -f (Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_LAST_ERROR='))
            }
            scheduler_identity = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_SCHEDULER_THREAD_IDENTITY='
            state_before = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_CURRENT_STATE_BEFORE='
            state_after = if ($returned) {
                Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_CURRENT_STATE_AFTER='
            } else { Get-Hex $text 'GXOS_NET10:IMPORT_BLOCKER_MAIN_STATE=' }
            runnable_count = Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_RUNNABLE_COUNT='
            blocked_count = Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_BLOCKED_COUNT='
            worker_execution_count = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_WORKER_EXECUTION_COUNT='
            worker_com_initialized = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_WORKER_COM_INITIALIZED='
            worker_com_model = Get-Hex $text 'GXOS_NET10:WAITFORSINGLEOBJECTEX_WORKER_COM_MODEL='
            waiter_count_after = Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_EVENT_WAITER_COUNT='
            object_internal_refs_after = Get-Hex $text 'GXOS_NET10:WAIT_BLOCKED_OBJECT_INTERNAL_REFS='
            blocked = !$returned
            next_boundary = $nextBoundary
            payload_sha256 = $hash
            serial_log = $serial
        }
        Write-Output ('NATIVEAOT_WAIT_RUN_{0}=' -f $sequence)
        $summary | ConvertTo-Json -Compress | Write-Output
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'QEMU cleanup failed.'
}
Write-Output "NATIVEAOT_WAIT_PAYLOAD_SHA256=$expectedHash"
Write-Output "NATIVEAOT_WAIT_RUNS=$RunCount"

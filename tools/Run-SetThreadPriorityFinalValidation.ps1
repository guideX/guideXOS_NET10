[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EnabledBuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$DisabledBuildDirectory,
    [string]$PayloadPath = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $root 'artifacts\veh-final3-normal-gate\ESP\GXOS\gxos-managed-entry-probe.dll'
}
$enabled = [IO.Path]::GetFullPath($EnabledBuildDirectory)
$disabled = [IO.Path]::GetFullPath($DisabledBuildDirectory)
$payload = [IO.Path]::GetFullPath($PayloadPath)
$expectedHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
if ($RunCount -lt 3) { throw 'At least three enabled QEMU runs are required.' }
if (-not (Test-Path -LiteralPath $payload)) { throw "Payload not found: $payload" }
if ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -ne $expectedHash) {
    throw 'The requested exact payload hash does not match.'
}

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) {
    [IO.Path]::GetFullPath($qemuCommand.Source)
} else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw "QEMU not found: $qemu" }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf) -or -not (Test-Path -LiteralPath $varsTemplate)) {
    throw "OVMF files not found under $qemuShare"
}

$runRoot = Join-Path $root ('artifacts\set-thread-priority-runs-' +
    (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$taskQemuIds = [Collections.Generic.List[int]]::new()

function Get-Hex([string]$Serial, [string]$Name) {
    $match = [regex]::Match($Serial, [regex]::Escape($Name) + '0x([0-9A-Fa-f]+)')
    if (-not $match.Success) { throw "Missing hex marker: $Name" }
    return [UInt64]::Parse($match.Groups[1].Value,
        [Globalization.NumberStyles]::AllowHexSpecifier)
}

function Get-Text([string]$Serial, [string]$Name) {
    $match = [regex]::Match($Serial, [regex]::Escape($Name) + '([^\r\n]+)')
    if (-not $match.Success) { throw "Missing text marker: $Name" }
    return $match.Groups[1].Value
}

function Require([string]$Serial, [string]$Marker) {
    if (-not $Serial.Contains($Marker)) { throw "Missing marker: $Marker" }
}

function Invoke-Run([string]$BuildDirectory, [string]$Label, [int]$Sequence) {
    $esp = Join-Path $BuildDirectory 'ESP'
    $efi = Join-Path $esp 'EFI\BOOT\BOOTX64.EFI'
    $builtPayload = Join-Path $esp 'GXOS\gxos-managed-entry-probe.dll'
    if (-not (Test-Path -LiteralPath $efi) -or -not (Test-Path -LiteralPath $builtPayload)) {
        throw "Harness or payload missing: $BuildDirectory"
    }
    $sourceHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
    $builtHash = (Get-FileHash -LiteralPath $builtPayload -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($sourceHash -ne $expectedHash -or $builtHash -ne $expectedHash) {
        throw "Exact payload hash mismatch before $Label run ${Sequence}: source=$sourceHash built=$builtHash"
    }
    $runDirectory = Join-Path $runRoot ("$Label-run-$Sequence")
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $code = Join-Path $runDirectory 'edk2-code.fd'
    $vars = Join-Path $runDirectory 'edk2-vars.fd'
    $serial = Join-Path $runDirectory 'serial.log'
    $stdout = Join-Path $runDirectory 'qemu.stdout.log'
    $stderr = Join-Path $runDirectory 'qemu.stderr.log'
    Copy-Item -LiteralPath $ovmf -Destination $code
    Copy-Item -LiteralPath $varsTemplate -Destination $vars
    $arguments = @(
        '-machine', 'q35', '-accel', 'tcg,thread=multi', '-m', '128M',
        '-drive', "if=pflash,format=raw,readonly=on,file=$code",
        '-drive', "if=pflash,format=raw,file=$vars",
        '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
        '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
        '-serial', "file:$serial", '-monitor', 'none', '-display', 'none',
        '-no-reboot', '-no-shutdown'
    )
    $process = Start-Process -FilePath $qemu -ArgumentList $arguments `
        -WorkingDirectory $BuildDirectory -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    [void]$taskQemuIds.Add([int]$process.Id)
    $completed = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 250
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "QEMU run did not stop: $($process.Id)"
    }
    $serialText = if (Test-Path -LiteralPath $serial) {
        [IO.File]::ReadAllText($serial)
    } else { '' }
    [PSCustomObject]@{
        Label = $Label
        Sequence = $Sequence
        Classification = if ($completed) { 'EXITED' } else { 'TIMEOUT_STOPPED' }
        SerialLog = $serial
        Serial = $serialText
        PayloadSha256 = $sourceHash
        BuiltPayloadSha256 = $builtHash
    }
}

function Validate-Enabled([pscustomobject]$Run) {
    $serial = $Run.Serial
    Require $serial 'GXOS_NET10:SETTHREADPRIORITY_BEGIN'
    Require $serial 'GXOS_NET10:SETTHREADPRIORITY_RETURNED'
    Require $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_SUMMARY=READY'
    Require $serial 'GXOS_NET10:CREATETHREAD_FINAL_SUMMARY=READY'
    Require $serial 'GXOS_NET10:IMPORT_BLOCKER_DLL='
    if ($serial.Contains('GXOS_NET10:MANAGED_ENTRY_OK')) { throw 'Enabled run reached managed entry.' }
    $base = Get-Hex $serial 'GXOS_NET10:IMAGE_BASE='
    $threadHandle = Get-Hex $serial 'GXOS_NET10:CREATETHREAD_RETURNED_HANDLE='
    if ($threadHandle -eq 0) { throw 'CreateThread returned NULL.' }
    if ((Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_PAYLOAD_BASE=') -ne $base) {
        throw 'SetThreadPriority payload base mismatch.'
    }
    if ((Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_IMPORT_DESCRIPTOR_INDEX=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_IMPORT_SYMBOL_INDEX=') -ne 0x2F -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_IMPORT_IAT_RVA=') -ne 0x7D1B0) {
        throw 'SetThreadPriority import identity mismatch.'
    }
    if ((Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RUNTIME_IAT=') -ne $base + 0x7D1B0 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_CALLER_RVA=') -ne 0x3CFC1) {
        throw 'SetThreadPriority runtime call identity mismatch.'
    }
    if ((Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RCX=') -ne $threadHandle -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RDX_RAW=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_SIGNED_PRIORITY=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_R8=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_R9=') -ne 0) {
        throw 'SetThreadPriority arguments mismatch.'
    }
    if ((Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_HANDLE=') -ne $threadHandle -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_HANDLE_TYPE=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_OBJECT_SLOT=') -eq [UInt64]::MaxValue -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_GENERATION=') -eq 0 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_TCB_SLOT=') -eq [UInt64]::MaxValue -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_INTERNAL_IDENTITY=') -eq 0) {
        throw 'SetThreadPriority handle identity mismatch.'
    }
    if ((Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STORED_PRIORITY_BEFORE=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STORED_PRIORITY_AFTER=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RETURN_VALUE=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STATE_BEFORE=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STATE_AFTER=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_SUSPEND_COUNT_BEFORE=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_SUSPEND_COUNT_AFTER=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_EXECUTION_COUNT=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RUNNABLE=') -ne 0) {
        throw 'SetThreadPriority state transition mismatch.'
    }
    foreach ($field in @('SETTHREADPRIORITY_FINAL_STACK_BASE=',
                         'SETTHREADPRIORITY_FINAL_GS_BASE=',
                         'SETTHREADPRIORITY_FINAL_TEB_BASE=',
                         'SETTHREADPRIORITY_FINAL_TLS_VECTOR_BASE=',
                         'SETTHREADPRIORITY_FINAL_TLS_BLOCK_BASE=')) {
        if ((Get-Hex $serial ("GXOS_NET10:" + $field)) -eq 0) {
            throw "Missing thread ownership evidence: $field"
        }
    }
    if ((Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_STORED_PRIORITY=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_STATE=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_SUSPEND_COUNT=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_EXECUTION_COUNT=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_RUNNABLE=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_STACK_SIZE=') -ne 0x4000 -or
        (Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_FINAL_STACK_CANARIES=') -ne 1) {
        throw 'SetThreadPriority final state mismatch.'
    }
    if ((Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_THREAD_OBJECT_COUNT=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_EVENT_OBJECT_COUNT=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_OBJECT_COUNT=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_PUBLIC_HANDLE_COUNT=') -ne 4 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_OBJECT_COUNT=') -ne 5 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_RUNNABLE_COUNT=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_BLOCKED_COUNT=') -ne 0) {
        throw 'SetThreadPriority blocker object counts mismatch.'
    }
    return [PSCustomObject]@{
        BlockerDll = Get-Text $serial 'GXOS_NET10:IMPORT_BLOCKER_DLL='
        BlockerSymbol = Get-Text $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL='
        Descriptor = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX='
        SymbolIndex = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX='
        IatRva = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA='
        RuntimeIat = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_IAT='
        CallSite = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_CALL_SITE='
        CallerRva = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA='
        Rcx = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RCX='
        Rdx = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RDX='
        R8 = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_R8='
        R9 = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_R9='
        StackArg5 = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_STACK_ARG5='
        StackArg6 = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_STACK_ARG6='
        ThreadHandle = $threadHandle
        ObjectSlot = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_OBJECT_SLOT='
        Generation = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_GENERATION='
        TcbSlot = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_TCB_SLOT='
        InternalIdentity = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_INTERNAL_IDENTITY='
        PayloadBase = $base
        RuntimeSetThreadPriorityIat = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RUNTIME_IAT='
        SetThreadPriorityCallSite = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RUNTIME_CALL_SITE='
        SetThreadPriorityCallerRva = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_CALLER_RVA='
        RequestedPriority = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_SIGNED_PRIORITY='
        StoredPriorityBefore = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STORED_PRIORITY_BEFORE='
        StoredPriorityAfter = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STORED_PRIORITY_AFTER='
        SetThreadPriorityReturn = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RETURN_VALUE='
        StateBefore = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STATE_BEFORE='
        StateAfter = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_STATE_AFTER='
        SuspendBefore = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_SUSPEND_COUNT_BEFORE='
        SuspendAfter = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_SUSPEND_COUNT_AFTER='
        ExecutionCount = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_EXECUTION_COUNT='
        Runnable = Get-Hex $serial 'GXOS_NET10:SETTHREADPRIORITY_RUNNABLE='
    }
}

function Validate-Disabled([pscustomobject]$Run) {
    $serial = $Run.Serial
    Require $serial 'GXOS_NET10:IMPORT_BLOCKER_DLL=KERNEL32.dll'
    Require $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL=SetThreadPriority'
    Require $serial 'GXOS_NET10:CREATETHREAD_FINAL_SUMMARY=READY'
    if ($serial.Contains('GXOS_NET10:SETTHREADPRIORITY_BEGIN')) {
        throw 'Disabled SetThreadPriority route ran.'
    }
    $base = Get-Hex $serial 'GXOS_NET10:IMAGE_BASE='
    $threadHandle = Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_HANDLE='
    if ($threadHandle -eq 0 -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX=') -ne 0x2F -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA=') -ne 0x7D1B0 -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA=') -ne 0x3CFC1 -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RCX=') -ne $threadHandle -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RDX=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_R8=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_R9=') -ne 0) {
        throw 'Disabled SetThreadPriority boundary or arguments mismatch.'
    }
    if ((Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_STATE=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_SUSPEND_COUNT=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_EXECUTION_COUNT=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_RUNNABLE_COUNT=') -ne 0 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_THREAD_OBJECT_COUNT=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_EVENT_OBJECT_COUNT=') -ne 2 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_OBJECT_COUNT=') -ne 1 -or
        (Get-Hex $serial 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_PUBLIC_HANDLE_COUNT=') -ne 4) {
        throw 'Disabled route changed established thread/object state.'
    }
    return [PSCustomObject]@{
        BlockerDll = Get-Text $serial 'GXOS_NET10:IMPORT_BLOCKER_DLL='
        BlockerSymbol = Get-Text $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL='
        Descriptor = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX='
        SymbolIndex = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX='
        IatRva = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA='
        RuntimeIat = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_IAT='
        CallSite = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_CALL_SITE='
        CallerRva = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA='
        Rcx = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RCX='
        Rdx = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_RDX='
        R8 = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_R8='
        R9 = Get-Hex $serial 'GXOS_NET10:IMPORT_BLOCKER_R9='
        ThreadHandle = $threadHandle
        PayloadBase = $base
    }
}

$enabledResults = [Collections.Generic.List[object]]::new()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        $run = Invoke-Run $enabled 'enabled' $sequence
        $validation = Validate-Enabled $run
        [void]$enabledResults.Add([PSCustomObject]@{ Run = $run; Validation = $validation })
        Write-Output ("ENABLED_RUN_{0}=" -f $sequence)
        $validation | ConvertTo-Json -Compress | Write-Output
    }
    $first = $enabledResults[0].Validation
    foreach ($result in $enabledResults) {
        foreach ($property in @('BlockerDll','BlockerSymbol','Descriptor','SymbolIndex','IatRva',
                                'RuntimeIat','CallSite','CallerRva','Rcx','Rdx','R8','R9',
                                'StackArg5','StackArg6','ThreadHandle','ObjectSlot','Generation',
                                'TcbSlot','InternalIdentity','PayloadBase',
                                'RuntimeSetThreadPriorityIat','SetThreadPriorityCallSite',
                                'SetThreadPriorityCallerRva','RequestedPriority',
                                'StoredPriorityBefore','StoredPriorityAfter',
                                'SetThreadPriorityReturn','StateBefore','StateAfter',
                                'SuspendBefore','SuspendAfter','ExecutionCount','Runnable')) {
            if ($result.Validation.$property -ne $first.$property) {
                throw "Enabled run semantic disagreement: $property"
            }
        }
    }
    $disabledRun = Invoke-Run $disabled 'disabled' 1
    $disabledValidation = Validate-Disabled $disabledRun
    Write-Output 'DISABLED_RUN=PASSED'
    Write-Output ("DISABLED_RESULT=" + ($disabledValidation | ConvertTo-Json -Compress))
    Write-Output ("ENABLED_RUNS={0}" -f $enabledResults.Count)
    Write-Output ("ENABLED_NEXT_BLOCKER={0}!{1}" -f $first.BlockerDll, $first.BlockerSymbol)
}
finally {
    foreach ($id in @($taskQemuIds)) {
        if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $id -Timeout 3 -ErrorAction SilentlyContinue
        }
    }
}

$remaining = @($taskQemuIds | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
if ($remaining.Count -ne 0) { throw "Task-owned QEMU remains: $($remaining -join ',')" }
Write-Output 'TASK_QEMU_REMAINING=0'

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$EnabledBuildDirectory,
    [Parameter(Mandatory = $true)] [string]$DisabledBuildDirectory,
    [string]$PayloadPath = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 30
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
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash.ToUpperInvariant() -ne $expectedHash) {
    throw 'The exact source payload hash does not match.'
}

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw "QEMU not found: $qemu" }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf) -or -not (Test-Path -LiteralPath $varsTemplate)) {
    throw "OVMF files not found under $qemuShare"
}
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'A pre-existing QEMU process is present; refusing to touch unrelated work.'
}

$runRoot = Join-Path $root ('artifacts\resume-thread-runs-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$taskQemuIds = [Collections.Generic.List[int]]::new()

function Get-Hex([string]$Serial, [string]$Name) {
    $match = [regex]::Match($Serial, [regex]::Escape($Name) + '0x([0-9A-Fa-f]+)')
    if (-not $match.Success) { throw "Missing hex marker: $Name" }
    [UInt64]::Parse($match.Groups[1].Value, [Globalization.NumberStyles]::AllowHexSpecifier)
}
function Get-Text([string]$Serial, [string]$Name) {
    $match = [regex]::Match($Serial, [regex]::Escape($Name) + '([^\r\n]+)')
    if (-not $match.Success) { throw "Missing text marker: $Name" }
    $match.Groups[1].Value
}
function Require([string]$Serial, [string]$Marker) {
    if (-not $Serial.Contains($Marker)) { throw "Missing marker: $Marker" }
}
function Invoke-QemuRun([string]$BuildDirectory, [string]$Label, [int]$Sequence) {
    $esp = Join-Path $BuildDirectory 'ESP'
    $efi = Join-Path $esp 'EFI\BOOT\BOOTX64.EFI'
    $builtPayload = Join-Path $esp 'GXOS\gxos-managed-entry-probe.dll'
    if (-not (Test-Path -LiteralPath $efi) -or -not (Test-Path -LiteralPath $builtPayload)) {
        throw "Harness or payload missing: $BuildDirectory"
    }
    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash.ToUpperInvariant()
    $builtHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $builtPayload).Hash.ToUpperInvariant()
    if ($sourceHash -ne $expectedHash -or $builtHash -ne $expectedHash) {
        throw "Exact payload hash mismatch before $Label run ${Sequence}."
    }
    $runDirectory = Join-Path $runRoot ("$Label-run-$Sequence")
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $code = Join-Path $runDirectory 'edk2-code.fd'
    $vars = Join-Path $runDirectory 'edk2-vars.fd'
    $serial = Join-Path $runDirectory 'serial.log'
    Copy-Item -LiteralPath $ovmf -Destination $code
    Copy-Item -LiteralPath $varsTemplate -Destination $vars
    $p = Start-Process -FilePath $qemu -ArgumentList @(
        '-machine','q35','-accel','tcg,thread=multi','-m','128M',
        '-drive',"if=pflash,format=raw,readonly=on,file=$code",
        '-drive',"if=pflash,format=raw,file=$vars",
        '-drive','file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
        '-rtc','base=utc,clock=vm','-boot','order=c',
        '-serial',"file:$serial",'-monitor','none','-display','none',
        '-no-reboot','-no-shutdown') -WorkingDirectory $BuildDirectory `
        -RedirectStandardOutput (Join-Path $runDirectory 'qemu.stdout.log') `
        -RedirectStandardError (Join-Path $runDirectory 'qemu.stderr.log') `
        -WindowStyle Hidden -PassThru
    [void]$taskQemuIds.Add([int]$p.Id)
    $completed = $p.WaitForExit($TimeoutSeconds * 1000)
    if (-not $completed) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $p.Id -Timeout 3 -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 250
    if (Get-Process -Id $p.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        throw "Task-owned QEMU did not stop: $($p.Id)"
    }
    [PSCustomObject]@{
        Label = $Label
        Sequence = $Sequence
        SerialLog = $serial
        Serial = if (Test-Path -LiteralPath $serial) { [IO.File]::ReadAllText($serial) } else { '' }
        PayloadSha256 = $sourceHash
        BuiltPayloadSha256 = $builtHash
    }
}

function Validate-Enabled([pscustomobject]$Run) {
    $s = $Run.Serial
    Require $s 'GXOS_NET10:RESUMETHREAD_BEGIN'
    Require $s 'GXOS_NET10:RESUMETHREAD_RETURNED'
    Require $s 'GXOS_NET10:RESUMETHREAD_FINAL_SUMMARY=READY'
    Require $s 'GXOS_NET10:IMPORT_BLOCKER_DLL=KERNEL32.dll'
    Require $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL=IsProcessInJob'
    Require $s 'GXOS_NET10:IMPORT_BLOCKER_SCHEDULER_THREAD=main'
    if ($s.Contains('GXOS_NET10:MANAGED_ENTRY_OK')) { throw 'Enabled run reached managed code.' }
    $base = Get-Hex $s 'GXOS_NET10:IMAGE_BASE='
    $threadHandle = Get-Hex $s 'GXOS_NET10:CREATETHREAD_RETURNED_HANDLE='
    $eventOneHandle = Get-Hex $s 'GXOS_NET10:CREATEEVENTW_RETURNED_HANDLE='
    if ($threadHandle -eq 0) { throw 'CreateThread returned NULL.' }
    if ((Get-Text $s 'GXOS_NET10:RESUMETHREAD_IMPORT_DLL=') -ne 'KERNEL32.dll' -or
        (Get-Text $s 'GXOS_NET10:RESUMETHREAD_IMPORT_SYMBOL=') -ne 'ResumeThread' -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_IMPORT_DESCRIPTOR_INDEX=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_IMPORT_SYMBOL_INDEX=') -ne 0x31 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_IMPORT_IAT_RVA=') -ne 0x7D1C0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_PREFERRED_IAT=') -ne 0x18007D1C0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_RUNTIME_IAT=') -ne $base + 0x7D1C0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_RUNTIME_CALL_SITE=') -ne $base + 0x3CFCA -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_CALLER_RVA=') -ne 0x3CFCA) {
        throw 'ResumeThread import identity mismatch.'
    }
    if ((Get-Hex $s 'GXOS_NET10:RESUMETHREAD_RCX=') -ne $threadHandle) {
        throw 'ResumeThread consumed the wrong API argument.'
    }
    if ((Get-Hex $s 'GXOS_NET10:RESUMETHREAD_OBJECT_SLOT=') -eq [UInt64]::MaxValue -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_GENERATION=') -eq 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_TCB_SLOT=') -eq [UInt64]::MaxValue -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_INTERNAL_IDENTITY=') -eq 0) {
        throw 'Dynamic Thread identity evidence is incomplete.'
    }
    if ((Get-Hex $s 'GXOS_NET10:RESUMETHREAD_STATE_BEFORE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_SUSPEND_COUNT_BEFORE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_RUNNABLE_BEFORE=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_PREVIOUS_SUSPEND_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_RETURN_VALUE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_STATE_AFTER=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_SUSPEND_COUNT_AFTER=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_RUNNABLE_AFTER=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_QUEUE_POSITION=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_QUEUE_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_EXECUTION_COUNT_BEFORE=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_EXECUTION_COUNT_AFTER=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_PRIORITY=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_PUBLIC_REFERENCE_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_EXECUTION_REFERENCE_LIVE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_ENTRY_RVA=') -ne 0x35320 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_ENTRY_ARGUMENT=') -ne $eventOneHandle -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_EVENT_ARGUMENT_VALID=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_CONTEXT_VALID=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_STACK_CANARIES=') -ne 1) {
        throw 'ResumeThread transition or integrity evidence mismatch.'
    }
    foreach ($field in @('RESUMETHREAD_FINAL_STACK_BASE=','RESUMETHREAD_FINAL_GS_BASE=',
                         'RESUMETHREAD_FINAL_TEB_BASE=','RESUMETHREAD_FINAL_TLS_VECTOR_BASE=',
                         'RESUMETHREAD_FINAL_TLS_BLOCK_BASE=')) {
        if ((Get-Hex $s ("GXOS_NET10:" + $field)) -eq 0) { throw "Missing ownership evidence: $field" }
    }
    if ((Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_PRIORITY=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_STATE=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_SUSPEND_COUNT=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_RUNNABLE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_QUEUE_POSITION=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_QUEUE_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_EXECUTION_COUNT=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_PUBLIC_REFERENCE_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_EXECUTION_REFERENCE_LIVE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_ENTRY_ARGUMENT=') -ne $eventOneHandle -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_CONTEXT_ENTRY_ARGUMENT=') -ne $eventOneHandle -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_CONTEXT_RSP=') -ne
            (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_INITIAL_RSP=') -or
        ((Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_INITIAL_RSP=') % 16) -ne 8 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_FLS_SLOTS=') -ne 0x40 -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_CURRENT_IDENTITY_BEFORE=') -ne
            (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_CURRENT_IDENTITY_AFTER=') -or
        (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_CURRENT_GS_BEFORE=') -ne
            (Get-Hex $s 'GXOS_NET10:RESUMETHREAD_FINAL_CURRENT_GS_AFTER=')) {
        throw 'ResumeThread final state mismatch.'
    }
    if ((Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX=') -ne 0x4B -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA=') -ne 0x7D290 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA=') -ne 0x4328B -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_WORKER_STATE=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_MAIN_STATE=') -ne 3 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_RUNNABLE_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_BLOCKED_COUNT=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_PUBLIC_THREAD_HANDLE_REFS=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_WORKER_EXECUTION_REFERENCE_LIVE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_WORKER_EXECUTION_COUNT=') -ne 0) {
        throw 'Post-resume scheduler continuation mismatch.'
    }
    [PSCustomObject]@{
        NextDll = Get-Text $s 'GXOS_NET10:IMPORT_BLOCKER_DLL='
        NextSymbol = Get-Text $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL='
        NextDescriptor = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX='
        NextSymbolIndex = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX='
        NextIatRva = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA='
        NextRuntimeIat = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_IAT='
        NextCallSite = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_CALL_SITE='
        NextCallerRva = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA='
        NextRcx = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_RCX='
        NextRdx = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_RDX='
        NextR8 = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_R8='
        NextR9 = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_R9='
        NextStackArg5 = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_STACK_ARG5='
        NextStackArg6 = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_STACK_ARG6='
        ThreadHandle = $threadHandle
        ObjectSlot = Get-Hex $s 'GXOS_NET10:RESUMETHREAD_OBJECT_SLOT='
        Generation = Get-Hex $s 'GXOS_NET10:RESUMETHREAD_GENERATION='
        TcbSlot = Get-Hex $s 'GXOS_NET10:RESUMETHREAD_TCB_SLOT='
        Identity = Get-Hex $s 'GXOS_NET10:RESUMETHREAD_INTERNAL_IDENTITY='
        PayloadBase = $base
        IncomingRdx = Get-Hex $s 'GXOS_NET10:RESUMETHREAD_RDX_INCIDENTAL='
        IncomingR8 = Get-Hex $s 'GXOS_NET10:RESUMETHREAD_R8_INCIDENTAL='
        IncomingR9 = Get-Hex $s 'GXOS_NET10:RESUMETHREAD_R9_INCIDENTAL='
    }
}

function Validate-Disabled([pscustomobject]$Run) {
    $s = $Run.Serial
    Require $s 'GXOS_NET10:IMPORT_BLOCKER_DLL=KERNEL32.dll'
    Require $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL=ResumeThread'
    if ($s.Contains('GXOS_NET10:RESUMETHREAD_BEGIN') -or
        $s.Contains('GXOS_NET10:RESUMETHREAD_FINAL_SUMMARY=READY')) {
        throw 'Disabled ResumeThread route ran.'
    }
    $base = Get-Hex $s 'GXOS_NET10:IMAGE_BASE='
    $threadHandle = Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_HANDLE='
    if ($threadHandle -eq 0 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX=') -ne 0x31 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA=') -ne 0x7D1C0 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_IAT=') -ne $base + 0x7D1C0 -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA=') -ne 0x3CFCA -or
        (Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_RCX=') -ne $threadHandle -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_STATE=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_SUSPEND_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_EXECUTION_COUNT=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_RUNNABLE_COUNT=') -ne 0 -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_THREAD_OBJECT_COUNT=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_EVENT_OBJECT_COUNT=') -ne 2 -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_OBJECT_COUNT=') -ne 1 -or
        (Get-Hex $s 'GXOS_NET10:CREATETHREAD_FINAL_LIVE_PUBLIC_HANDLE_COUNT=') -ne 4) {
        throw 'Disabled ResumeThread boundary or pre-blocker state mismatch.'
    }
    [PSCustomObject]@{
        NextDll = Get-Text $s 'GXOS_NET10:IMPORT_BLOCKER_DLL='
        NextSymbol = Get-Text $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL='
        Descriptor = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX='
        SymbolIndex = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX='
        IatRva = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA='
        CallerRva = Get-Hex $s 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA='
    }
}

$enabledResults = [Collections.Generic.List[object]]::new()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        $run = Invoke-QemuRun $enabled 'enabled' $sequence
        $validation = Validate-Enabled $run
        [void]$enabledResults.Add([PSCustomObject]@{ Run = $run; Validation = $validation })
        Write-Output "ENABLED_RUN_$sequence=PASSED"
        $validation | ConvertTo-Json -Compress | Write-Output
    }
    $first = $enabledResults[0].Validation
    foreach ($result in $enabledResults) {
        foreach ($property in @('NextDll','NextSymbol','NextDescriptor','NextSymbolIndex','NextIatRva',
                                'NextCallerRva','ObjectSlot','Generation','TcbSlot','Identity')) {
            if ($result.Validation.$property -ne $first.$property) {
                throw "Enabled semantic disagreement: $property"
            }
        }
    }
    $disabledRun = Invoke-QemuRun $disabled 'disabled' 1
    $disabledValidation = Validate-Disabled $disabledRun
    Write-Output 'DISABLED_RUN=PASSED'
    $disabledValidation | ConvertTo-Json -Compress | Write-Output
    Write-Output "ENABLED_RUNS=$($enabledResults.Count)"
    Write-Output "ENABLED_NEXT_BLOCKER=$($first.NextDll)!$($first.NextSymbol)"
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

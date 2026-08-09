[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [ValidateSet('Enabled', 'Disabled')] [string]$Mode = 'Enabled',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$expectedPayloadHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
if (-not (Test-Path -LiteralPath $efi) -or -not (Test-Path -LiteralPath $payload)) {
    throw 'Build the IsProcessInJob harness first.'
}
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
if ($payloadHash -ne $expectedPayloadHash) { throw "Payload hash mismatch: $payloadHash" }
if (Test-Path -LiteralPath $evidence) { throw "Evidence directory already exists: $evidence" }
if ($RunCount -lt 1 -or ($Mode -eq 'Enabled' -and $RunCount -lt 3)) {
    throw 'Enabled validation requires at least three fresh runs.'
}
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw 'qemu-system-x86_64.exe is required.' }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf) -or -not (Test-Path -LiteralPath $varsTemplate)) {
    throw 'OVMF firmware files are required.'
}
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

function Read-Serial([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
            [IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object IO.StreamReader($stream)
            try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    } catch [IO.IOException] { return '' }
}
function Stop-OwnedQemu([System.Diagnostics.Process]$process) {
    try { $process.Refresh() } catch { }
    try {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    } catch { }
    try { $process.WaitForExit(5000) | Out-Null } catch { }
    Start-Sleep -Milliseconds 200
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        throw "Owned QEMU process remained after cleanup: $($process.Id)"
    }
}
function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$ownedProcesses = @()
try {
    Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) 'A pre-existing QEMU process is present.'
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) "QEMU already exists before run $sequence."
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
            '-rtc', 'base=utc,clock=vm', '-boot', 'order=c', '-serial', "file:$serial",
            '-monitor', 'none', '-display', 'none', '-no-reboot', '-no-shutdown')
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
        $ownedProcesses += $process
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            $text = Read-Serial $serial
            $complete = if ($Mode -eq 'Enabled') {
                $text.Contains('GXOS_NET10:ISPROCESSINJOB_RETURNED') -and
                $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GlobalMemoryStatusEx')
            } else {
                $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!IsProcessInJob')
            }
            if ($complete) { break }
            try { if ($process.HasExited) { break } } catch { break }
            Start-Sleep -Milliseconds 100
        }
        Stop-OwnedQemu $process
        $text = Read-Serial $serial
        if ($Mode -eq 'Enabled') {
            Require ($text.Contains('GXOS_NET10:PE_IMPORT_SYMBOLS=124')) "run $sequence import symbol count"
            Require ($text.Contains('GXOS_NET10:PE_IMPORT_FUNCTIONAL=46')) "run $sequence functional import count"
            Require ($text.Contains('GXOS_NET10:PE_IMPORT_FAILFAST=78')) "run $sequence fail-fast import count"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_IMPORT_DESCRIPTOR_INDEX=0x0000000000000002')) "run $sequence descriptor"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_IMPORT_SYMBOL_INDEX=0x000000000000004B')) "run $sequence symbol index"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_IMPORT_IAT_RVA=0x000000000007D290')) "run $sequence IAT RVA"
            Require ($text -match 'GXOS_NET10:ISPROCESSINJOB_CALL_SITE=0x(?!0000000000000000)[0-9A-F]{16}') "run $sequence call site"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_CALLER_RVA=0x000000000004328B')) "run $sequence caller RVA"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RCX=0xFFFFFFFFFFFFFFFF')) "run $sequence RCX"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RDX=0x0000000000000000')) "run $sequence RDX"
            Require ($text -match 'GXOS_NET10:ISPROCESSINJOB_R8=0x(?!0000000000141620)[0-9A-F]{16}') "run $sequence R8"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_R9=0x0000000000000000')) "run $sequence R9"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_PROCESS_HANDLE_CLASS=CURRENT_PROCESS_PSEUDO_HANDLE')) "run $sequence process token"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_JOB_HANDLE_CLASS=NULL')) "run $sequence null job"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RESULT_POINTER_CANONICAL=0x0000000000000001')) "run $sequence result canonical"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RESULT_POINTER_WRITABLE=0x0000000000000001')) "run $sequence result writable"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RESULT_BYTES_WRITTEN=0x0000000000000004')) "run $sequence result write width"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RETURN_VALUE=0x0000000000000001')) "run $sequence return"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RESULT_VALUE_AFTER=0x0000000000000000')) "run $sequence result after"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_CALLER_BRANCH=SUCCESS_RESULT_FALSE_FALLBACK')) "run $sequence caller branch"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_MAIN_IDENTITY=0x0000000000000001')) "run $sequence main identity"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_MAIN_STATE=0x0000000000000003')) "run $sequence main state"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_WORKER_IDENTITY=0x0000000000000002')) "run $sequence worker identity"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_WORKER_STATE=0x0000000000000002')) "run $sequence worker state"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_WORKER_PRIORITY=0x0000000000000002')) "run $sequence worker priority"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_WORKER_SUSPEND_COUNT=0x0000000000000000')) "run $sequence worker suspend"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_WORKER_RUNNABLE=0x0000000000000001')) "run $sequence worker runnable"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_WORKER_EXECUTION_COUNT=0x0000000000000000')) "run $sequence worker execution"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_LIVE_OBJECT_COUNT=0x0000000000000005')) "run $sequence object count"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_LIVE_PUBLIC_HANDLE_COUNT=0x0000000000000004')) "run $sequence public handle count"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_RUNNABLE_COUNT=0x0000000000000001')) "run $sequence runnable count"
            Require ($text.Contains('GXOS_NET10:ISPROCESSINJOB_BLOCKED_COUNT=0x0000000000000000')) "run $sequence blocked count"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_DLL=KERNEL32.dll')) "run $sequence next blocker DLL"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_SYMBOL=GlobalMemoryStatusEx')) "run $sequence next blocker symbol"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_SCHEDULER_THREAD=main')) "run $sequence next blocker thread"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX=0x0000000000000002')) "run $sequence next blocker descriptor"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX=0x0000000000000044')) "run $sequence next blocker symbol index"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_IAT_RVA=0x000000000007D258')) "run $sequence next blocker IAT"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_RUNTIME_CALL_SITE=0x00000000054BE361')) "run $sequence next blocker call site"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA=0x0000000000043361')) "run $sequence next blocker caller RVA"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_RCX=0x0000000007E64AD0')) "run $sequence next blocker RCX"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_RDX=0x00000000000003F8')) "run $sequence next blocker RDX"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_R8=0x0000000000000001')) "run $sequence next blocker R8"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_R9=0x0000000000000000')) "run $sequence next blocker R9"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_PRIORITY=0x0000000000000002')) "run $sequence next blocker priority"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_SUSPEND_COUNT=0x0000000000000000')) "run $sequence next blocker suspend"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_RUNNABLE=0x0000000000000001')) "run $sequence next blocker runnable"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_EXECUTION_COUNT=0x0000000000000000')) "run $sequence next blocker execution"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_LIVE_OBJECT_COUNT=0x0000000000000005')) "run $sequence next blocker objects"
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_LIVE_PUBLIC_HANDLE_COUNT=0x0000000000000004')) "run $sequence next blocker handles"
            Require ($text.Contains('GXOS_NET10:QUERYJOBOBJECT_CALLER_CONSUMPTION_COMPLETE')) "run $sequence preserved QueryInformationJobObject route"
        } else {
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_SYMBOL=IsProcessInJob')) "disabled boundary"
            Require (-not $text.Contains('GXOS_NET10:ISPROCESSINJOB_BEGIN')) 'disabled route executed'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_RCX=0xFFFFFFFFFFFFFFFF')) 'disabled RCX'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_RDX=0x0000000000000000')) 'disabled RDX'
            Require ($text -match 'GXOS_NET10:IMPORT_BLOCKER_R8=0x(?!0000000000141620)[0-9A-F]{16}') 'disabled R8 diagnostic'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_R9=0x0000000000000000')) 'disabled R9 diagnostic'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_MAIN_STATE=0x0000000000000003')) 'disabled main state'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_STATE=0x0000000000000002')) 'disabled worker state'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_PRIORITY=0x0000000000000002')) 'disabled worker priority'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_SUSPEND_COUNT=0x0000000000000000')) 'disabled suspend'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_RUNNABLE=0x0000000000000001')) 'disabled runnable status'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_WORKER_EXECUTION_COUNT=0x0000000000000000')) 'disabled execution'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_LIVE_OBJECT_COUNT=0x0000000000000005')) 'disabled live objects'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_LIVE_PUBLIC_HANDLE_COUNT=0x0000000000000004')) 'disabled public handles'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_RUNNABLE_COUNT=0x0000000000000001')) 'disabled runnable'
            Require ($text.Contains('GXOS_NET10:IMPORT_BLOCKER_BLOCKED_COUNT=0x0000000000000000')) 'disabled blocked'
            Require (-not $text.Contains('GXOS_NET10:ISPROCESSINJOB_RESULT_BYTES_WRITTEN=')) 'disabled result write'
            Require (-not $text.Contains('GXOS_NET10:ISPROCESSINJOB_RESULT_VALUE_AFTER=')) 'disabled result mutation'
        }
        Write-Output ("ISPROCESSINJOB_{0}_RUN_{1}=PASSED serial={2}" -f $Mode.ToUpperInvariant(), $sequence, $serial)
    }
} finally {
    foreach ($ownedProcess in $ownedProcesses) {
        Stop-OwnedQemu $ownedProcess
    }
}
if (@($ownedProcesses | Where-Object {
        Get-Process -Id $_.Id -ErrorAction SilentlyContinue
    }).Count -ne 0) {
    throw 'QEMU cleanup failed.'
}
Write-Output "ISPROCESSINJOB_$($Mode.ToUpperInvariant())_RUNS=$RunCount"
Write-Output "ISPROCESSINJOB_PAYLOAD_SHA256=$payloadHash"

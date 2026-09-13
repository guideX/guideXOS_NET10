[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$expectedHash = $PayloadSha256.ToUpperInvariant()

function Require([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Read-Serial([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return '' }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
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

function Count-Token([string]$text, [string]$token) {
    return [regex]::Matches($text, [regex]::Escape($token)).Count
}

function Stop-OwnedQemu([System.Diagnostics.Process]$process) {
    try { $process.Refresh() } catch { }
    try {
        if (!$process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    } catch { }
    try { $process.WaitForExit(5000) | Out-Null } catch { }
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) { throw "Owned QEMU process remained: $($process.Id)" }
}

Require ($RunCount -ge 3) 'At least three fresh boots are required.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) 'Phase 53O harness or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) 'Staged Phase 53O payload hash is not the captured identity.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) 'OVMF firmware is required.'
$baselineQemuIds = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue |
    ForEach-Object { $_.Id })
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        $run = Join-Path $evidence ("runs\run-{0}" -f $sequence)
        New-Item -ItemType Directory -Force -Path $run | Out-Null
        $code = Join-Path $run 'edk2-code.fd'
        $vars = Join-Path $run 'edk2-vars.fd'
        $serial = Join-Path $run 'serial.log'
        $stdout = Join-Path $run 'qemu.stdout.log'
        $stderr = Join-Path $run 'qemu.stderr.log'
        Copy-Item -LiteralPath $ovmf -Destination $code
        Copy-Item -LiteralPath $varsTemplate -Destination $vars
        $arguments = @('-machine', 'q35', '-accel', 'tcg,thread=multi', '-m', '128M', '-drive', "if=pflash,format=raw,readonly=on,file=$code", '-drive', "if=pflash,format=raw,file=$vars", '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk', '-rtc', 'base=utc,clock=vm', '-boot', 'order=c', '-serial', "file:$serial", '-monitor', 'none', '-display', 'none', '-no-reboot', '-no-shutdown')
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
        $owned += $process
        try {
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while ((Get-Date) -lt $deadline) {
                $text = Read-Serial $serial
                if ($text.Contains('GXOS_NET10:PHASE53O_COMPLETE=1') -or $text.Contains('GXOS_NET10:PHASE53O_FAILURE=1') -or $text.Contains('GXOS_NET10:FAIL:') -or $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 250
            }
        } finally { Stop-OwnedQemu $process }
        $text = Read-Serial $serial
        Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) "run $sequence payload hash changed"
        Require (!$text.Contains('GXOS_NET10:FAIL:') -and !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and !$text.Contains('GXOS_NET10:PHASE53O_FAILURE=1')) "run $sequence fault, fail, or lifecycle failure marker"
        foreach ($marker in @('GXOS_NET10:NATIVEAOT_STARTUP_OK', 'GXOS_NET10:GC_STARTUP_ADVANCED', 'GXOS_NET10:MANAGED_ENTRY_COMPLETE', 'GXOS_NET10:PHASE53O_BEGIN', 'GXOS_NET10:PHASE53O_ATTACH_PATH=GENERATED_REVERSE_PINVOKE', 'GXOS_NET10:PHASE53O_DETACH_PATH=RUNTIME_FLS_FIBER_DETACH_CALLBACK', 'GXOS_NET10:MANAGED_THREAD_ATTACH_OK=1', 'GXOS_NET10:PHASE53O_FLS_CALLBACK_RETURNED=1', 'GXOS_NET10:MANAGED_CALLBACK_RETURN_OK=1', 'GXOS_NET10:MANAGED_GC_ROOT_SURVIVED=1', 'GXOS_NET10:MANAGED_GC_COLLECTION_OBSERVED=1', 'GXOS_NET10:MANAGED_GC_WORKER_RETURN_OK=1', 'GXOS_NET10:MANAGED_THREAD_MANAGED_RETURN_OK=1', 'GXOS_NET10:MANAGED_THREAD_DETACH_OK=1', 'GXOS_NET10:MANAGED_THREAD_UNREGISTER_OK=1', 'GXOS_NET10:PHASE53O_SCHEDULER_RECLAIM_OK=1', 'GXOS_NET10:PHASE53O_REPEAT_OK=1', 'GXOS_NET10:PHASE53O_COMPLETE=1', 'GXOS_NET10:PHASE53O_PASS=1')) {
            Require ($text.Contains($marker)) "run $sequence missing marker: $marker"
        }
        foreach ($pair in @(@('GXOS_NET10:PHASE53O_THREAD_CYCLE_BEGIN', 2), @('GXOS_NET10:MANAGED_THREAD_ATTACH_OK=1', 2), @('GXOS_NET10:PHASE53O_FLS_CALLBACK_RETURNED=1', 2), @('GXOS_NET10:MANAGED_THREAD_UNREGISTER_OK=1', 2), @('GXOS_NET10:PHASE53O_SCHEDULER_RECLAIM_OK=1', 2))) {
            Require ((Count-Token $text $pair[0]) -eq $pair[1]) "run $sequence marker count mismatch: $($pair[0])"
        }
        Require ($text.Contains('GXOS_NET10:NATIVEAOT_DURABILITY_PASS=1') -and $text.Contains('GXOS_NET10:MANAGED_CALLBACK_PROCESS_INITIALIZATION_CALLS=0x0000000000000001')) "run $sequence main-thread durability proof is missing"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE53O_RUNTIME_STATE_BEFORE=') -eq 1 -and (Get-Hex $text 'GXOS_NET10:PHASE53O_RUNTIME_TRANSITION_FRAME=') -eq [UInt64]::MaxValue -and (Get-Hex $text 'GXOS_NET10:PHASE53O_RUNTIME_STATE_AFTER=') -eq 2) "run $sequence NativeAOT runtime state transition is incorrect"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE53O_RUNTIME_STACK_LOW=') -eq (Get-Hex $text 'GXOS_NET10:PHASE53O_STACK_BASE=') -and (Get-Hex $text 'GXOS_NET10:PHASE53O_RUNTIME_STACK_HIGH=') -eq (Get-Hex $text 'GXOS_NET10:PHASE53O_STACK_LIMIT=') -and (Get-Hex $text 'GXOS_NET10:PHASE53O_STACK_RSP=') -ge (Get-Hex $text 'GXOS_NET10:PHASE53O_STACK_BASE=') -and (Get-Hex $text 'GXOS_NET10:PHASE53O_STACK_RSP=') -le (Get-Hex $text 'GXOS_NET10:PHASE53O_STACK_LIMIT=')) "run $sequence runtime-reported stack bounds are incorrect"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE53O_ALLOC_CONTEXT=') -ne (Get-Hex $text 'GXOS_NET10:PHASE53O_MAIN_ALLOC_CONTEXT=') -and (Get-Hex $text 'GXOS_NET10:PHASE53O_FLS_AFTER_CLEAR=') -eq 0) "run $sequence allocation-context or FLS clear proof failed"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE53O_THREADSTORE_BEFORE=') -eq (Get-Hex $text 'GXOS_NET10:PHASE53O_THREADSTORE_AFTER=') + 1) "run $sequence ThreadStore census did not shrink by one"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE53O_CALLBACK_RESULT=') -eq 0x00030008 -and (Get-Hex $text 'GXOS_NET10:PHASE53O_GC_RESULT=') -ne 0) "run $sequence managed callback result or GC result is incorrect"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_COUNT=') -eq 4) "run $sequence main-thread callback count is incorrect"
        Write-Output ("NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE_RUN_{0}=PASS bytes={1} sha256={2} serial={3}" -f $sequence, ([Text.Encoding]::UTF8.GetByteCount($text)), (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant(), $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
$unexpectedQemu = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue |
    Where-Object { $baselineQemuIds -notcontains $_.Id })
Require ($unexpectedQemu.Count -eq 0) 'QEMU cleanup failed.'
Write-Output "NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE_PAYLOAD_SHA256=$expectedHash"
Write-Output "NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE_RUNS=$RunCount"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [string]$QemuPath = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 600
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
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object IO.StreamReader($stream)
            try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
        } finally { $stream.Dispose() }
    } catch { return '' }
}

function Get-Hex([string]$text, [string]$prefix) {
    $key = $prefix.TrimEnd('=')
    $match = [regex]::Match($text,
        [regex]::Escape($key) + '=(?<value>0x[0-9A-Fa-f]+|[0-9]+)')
    if (!$match.Success) { throw "Missing field: $key" }
    $value = $match.Groups['value'].Value
    if ($value.StartsWith('0x')) {
        return [Convert]::ToUInt64($value.Substring(2), 16)
    }
    return [Convert]::ToUInt64($value, 10)
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

Require ($RunCount -ge 3) 'Phase 65 requires at least three fresh boots.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Phase 65 harness or managed payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload hash must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged Phase 65 payload hash mismatch.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemu = if (![string]::IsNullOrWhiteSpace($QemuPath)) {
    [IO.Path]::GetFullPath($QemuPath)
} else {
    $qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
    if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) }
    else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
}
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$qemuProcessName = [IO.Path]::GetFileNameWithoutExtension($qemu)
$baselineQemuIds = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue |
    ForEach-Object { $_.Id })
Require ($baselineQemuIds.Count -eq 0) 'An unowned QEMU process is already running.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) `
    'OVMF firmware is required.'

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
                if ($text.Contains('GXOS_NET10:PHASE65_COMPLETE=1') -or
                    $text.Contains('GXOS_NET10:FAIL:') -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 250
            }
        } finally { Stop-OwnedQemu $process }

        $text = Read-Serial $serial
        Set-Content -LiteralPath (Join-Path $run 'validation-summary.txt') -Value @(
            "run=$sequence",
            "payload_sha256=$((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant())",
            "phase65_scenarios=$([regex]::Matches($text, 'GXOS_NET10:PHASE65_SCENARIO=').Count)",
            "phase65_pass=$($text.Contains('GXOS_NET10:PHASE65_PASS=1'))") -Encoding utf8
        Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
                 $expectedHash) "boot $sequence payload hash changed"
        Require (!$text.Contains('GXOS_NET10:FAIL:') -and
                 !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
                 $text.Contains('GXOS_NET10:PHASE65_PASS=1')) "boot $sequence failed"
        foreach ($marker in @(
            'GXOS_NET10:NATIVEAOT_STARTUP_OK',
            'GXOS_NET10:MANAGED_GC_MAIN_OK=1',
            'GXOS_NET10:PHASE53O_PASS=1',
            'GXOS_NET10:MANAGED_WORKER_API_OK=1',
            'GXOS_NET10:PHASE65_API=BOUNDED_COOPERATIVE_CANCELLATION',
            'GXOS_NET10:PHASE65_CHECKPOINT=ROOT_PUBLISHED_BEFORE_GC',
            'GXOS_NET10:PHASE65_B_CHECKPOINT_READY=1',
            'GXOS_NET10:PHASE65_CAPACITY_RECOVERY=1',
            'GXOS_NET10:PHASE65_SAME_SLOT_REUSE=1',
            'GXOS_NET10:PHASE65_PEER_ISOLATION=1',
            'GXOS_NET10:PHASE65_ROOT_CLEANUP_EXACTLY_ONCE=1',
            'GXOS_NET10:PHASE65_DETACH_EXACTLY_ONCE=1',
            'GXOS_NET10:PHASE65_STALE_ROOT_TOKEN_REJECTED=1',
            'GXOS_NET10:PHASE65_CANCELED_PAYLOAD_SUPPRESSED=1',
            'GXOS_NET10:PHASE65_C_GC_AND_ROOT_SURVIVAL=1',
            'GXOS_NET10:PHASE65_COMPLETE=1')) {
            Require ($text.Contains($marker)) "boot $sequence missing marker: $marker"
        }
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_CANCELLATION_SCENARIOS=') -eq 12) `
            "boot $sequence did not complete twelve cancellation scenarios"
        $completionOrders = @([regex]::Matches($text,
            'GXOS_NET10:PHASE65_COMPLETION_ORDER=0x(?<order>[0-9A-Fa-f]+)') |
            ForEach-Object { $_.Groups['order'].Value } | Sort-Object -Unique)
        Require ($completionOrders.Count -ge 2) `
            "boot $sequence did not vary healthy-worker completion order"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_COUNT=') -eq 43) `
            "boot $sequence managed callback count mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_CANCELED_WORKERS=') -eq 12) `
            "boot $sequence did not cancel twelve workers"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_THREE_LIVE_WORKERS=') -eq 3 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_THREE_LIVE_ROOTS=') -eq 2) `
            "boot $sequence did not prove three workers and two roots at overlap"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_ROOTS_LIVE_WORKERS=') -eq 3 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_ROOTS_LIVE_LEDGER=') -eq 2 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_CANCEL_REQUESTED_WORKERS=') -eq 3 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_CANCEL_REQUESTED_LEDGER=') -eq 2) `
            "boot $sequence did not retain both roots through the cancel request"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_B_ROOT_PUBLICATIONS=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_ROOT_OWNED=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_C_ROOT_SIMULTANEOUS=') -eq 1) `
            "boot $sequence did not prove both roots before cancellation"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_CANCEL_REQUEST_STATUS=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_DUPLICATE_CANCEL_STATUS=') -eq 15 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_INVALID_CANCEL_STATUS=') -eq 5 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_WRONG_GENERATION_STATUS=') -eq 6) `
            "boot $sequence cancellation request statuses mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_CANCEL_A_STATUS=') -eq 18 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_LATE_CANCEL_STATUS=') -eq 18 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_COMPLETED_CANCEL_STATUS=') -eq 16 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_CLOSED_CANCEL_STATUS=') -eq 17 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_STALE_B_CANCEL_STATUS=') -eq 6) `
            "boot $sequence cancellation terminal/stale statuses mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_B_CANCELLED_GC_COUNT=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_POST_GC_COUNT=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_ROOT_SURVIVAL_CALLBACKS=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_CANCEL_OBSERVED=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_ROOT_RELEASE_COUNT=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_DETACH_COUNT=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_FLS_CLEAN=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_SCHEDULER_TERMINATED=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_ROOTS_AFTER_B_RELEASE=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_CANCELED_WORKERS=') -eq 3 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_CANCELED_LEDGER=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_C_ROOT_STILL_LIVE=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_C_GC_COUNT=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_C_POST_GC_COUNT=') -eq 1) `
            "boot $sequence cancellation/root cleanup evidence mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_PEAK_API_WORKERS=') -eq 3 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_PEAK_ROOT_LEDGER=') -eq 2) `
            "boot $sequence worker/root peaks mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_B_RECLAIMED_WORKERS=') -eq 2 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_RECLAIMED_ROOTS=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_D_CREATED_WORKERS=') -eq 3 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_D_CREATED_LEDGER=') -eq 1) `
            "boot $sequence did not prove post-cancel reclaim and replacement capacity"
        foreach ($kind in @('VM', 'THREADS', 'OBJECTS')) {
            Require ((Get-Hex $text "GXOS_NET10:PHASE65_FINAL_$kind=") -eq
                     (Get-Hex $text "GXOS_NET10:PHASE65_BASELINE_$kind=")) `
                "boot $sequence $kind baseline did not restore"
        }
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_FINAL_API_WORKERS=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_FINAL_ROOT_LEDGER=') -eq 0) `
            "boot $sequence API/root baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE65_B_SLOT=') -eq
                 (Get-Hex $text 'GXOS_NET10:PHASE65_D_SLOT=') -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_IDENTITY=') -ne
                 (Get-Hex $text 'GXOS_NET10:PHASE65_D_IDENTITY=') -and
                 (Get-Hex $text 'GXOS_NET10:PHASE65_B_GENERATION=') -ne
                 (Get-Hex $text 'GXOS_NET10:PHASE65_D_GENERATION=')) `
            "boot $sequence replacement did not reuse B's slot with a new identity/generation"
        Write-Output ("PHASE65_CANCELLATION_RUN_{0}=PASS scenarios=12 serial={1}" -f
            $sequence, $serial)
    }
} finally { foreach ($process in $owned) { Stop-OwnedQemu $process } }

$unexpectedAfter = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue |
    Where-Object { $baselineQemuIds -notcontains $_.Id })
Require ($unexpectedAfter.Count -eq 0) 'QEMU cleanup failed.'
Write-Output "PHASE65_PAYLOAD_SHA256=$expectedHash"
Write-Output "PHASE65_FRESH_BOOTS=$RunCount"
Write-Output 'PHASE65_CANCELLATION_SCENARIOS_PER_BOOT=12'

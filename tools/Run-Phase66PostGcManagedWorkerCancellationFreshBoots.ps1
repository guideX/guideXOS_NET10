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

Require ($RunCount -ge 1) 'RunCount must be positive; acceptance uses three fresh boots.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Phase 66 harness or managed payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload hash must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged Phase 66 payload hash mismatch.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemu = if (![string]::IsNullOrWhiteSpace($QemuPath)) {
    [IO.Path]::GetFullPath($QemuPath)
} else {
    $qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
    if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) }
    else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
}
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$baselineQemuIds = @(Get-CimInstance Win32_Process -Filter `
    "Name='$(Split-Path -Leaf $qemu)'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and
        [IO.Path]::GetFullPath($_.ExecutablePath).Equals(
            $qemu, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { $_.ProcessId })
Require ($baselineQemuIds.Count -eq 0) `
    'A QEMU process using the selected executable is already running.'
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
                if ($text.Contains('GXOS_NET10:PHASE66_PASS=1') -or
                    $text.Contains('GXOS_NET10:FAIL:') -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 250
            }
            $text = Read-Serial $serial
            [IO.File]::WriteAllLines((Join-Path $run 'summary.txt'), @(
                "boot=$sequence",
                "phase66_scenarios=$([regex]::Matches($text, 'GXOS_NET10:PHASE66_SCENARIO=').Count)",
                "phase66_pass=$($text.Contains('GXOS_NET10:PHASE66_PASS=1'))"),
                [Text.Encoding]::UTF8)
            Require ($text.Contains('GXOS_NET10:PHASE66_PASS=1')) `
                "boot $sequence did not reach the Phase 66 pass marker. Evidence: $run"

            foreach ($marker in @(
                'GXOS_NET10:PHASE66_API=POST_GC_COOPERATIVE_CANCELLATION',
                'GXOS_NET10:PHASE66_CHECKPOINTS=2',
                'GXOS_NET10:PHASE66_CHECKPOINT1=ROOT_PUBLISHED_BEFORE_GC',
                'GXOS_NET10:PHASE66_CHECKPOINT2=GC_ROOT_SURVIVED_BEFORE_CONTINUATION',
                'GXOS_NET10:PHASE66_CANCELLATION=COOPERATIVE_NO_ASYNC_PREEMPTION',
                'GXOS_NET10:PHASE66_ROOT_TRANSITION=0>2>1>0',
                'GXOS_NET10:PHASE66_ORDERING_PATTERNS=A,B,C',
                'GXOS_NET10:PHASE66_CAPACITY_RECOVERY=1',
                'GXOS_NET10:PHASE66_SAME_SLOT_REUSE=1',
                'GXOS_NET10:PHASE66_PEER_ISOLATION=1',
                'GXOS_NET10:PHASE66_ROOT_CLEANUP_EXACTLY_ONCE=1',
                'GXOS_NET10:PHASE66_DETACH_EXACTLY_ONCE=1',
                'GXOS_NET10:PHASE66_MANAGED_ROOT_TOKENS_ONLY=1',
                'GXOS_NET10:PHASE66_DUPLICATE_B_ROOT_RELEASE_REJECTED=1',
                'GXOS_NET10:PHASE66_STALE_B_ROOT_CLEANUP_REJECTED=1',
                'GXOS_NET10:PHASE66_D_RESULT=ADD_ONE_OK',
                'GXOS_NET10:PHASE66_COMPLETE=1',
                'GXOS_NET10:MANAGED_GC_MAIN_OK=1',
                'GXOS_NET10:PHASE53O_PASS=1')) {
                Require ($text.Contains($marker)) "boot $sequence missing marker: $marker"
            }

            Require (([regex]::Matches($text, 'GXOS_NET10:PHASE66_SCENARIO=').Count) -eq 12) `
                "boot $sequence did not complete twelve scenarios"
            foreach ($pattern in @(1, 2, 3)) {
                Require (([regex]::Matches($text,
                    "GXOS_NET10:PHASE66_ORDER_PATTERN=0x0*$pattern(\r?\n|$)").Count) -eq 4) `
                    "boot $sequence ordering pattern $pattern did not run four times"
            }
            Require ((Get-Hex $text 'GXOS_NET10:PHASE66_CANCELLATION_SCENARIOS=') -eq 12 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_CANCELED_WORKERS=') -eq 12) `
                "boot $sequence did not pass all twelve cancellations"
            Require ((Get-Hex $text 'GXOS_NET10:PHASE66_ROOTS_LIVE=') -eq 2 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_CANCEL_REQUESTED_ROOTS=') -eq 2 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_CANCELED_ROOTS=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_RECLAIMED_ROOTS=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_FINAL_ROOT_LEDGER=') -eq 0) `
                "boot $sequence root ownership timeline mismatch"
            Require ((Get-Hex $text 'GXOS_NET10:PHASE66_ABC_LIVE_API_WORKERS=') -eq 3 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_CANCELED_API_WORKERS=') -eq 3 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_RECLAIMED_API_WORKERS=') -eq 2 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_D_CREATED_API_WORKERS=') -eq 3 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_FINAL_API_WORKERS=') -eq 0 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_PEAK_API_WORKERS=') -eq 3 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_PEAK_ROOT_LEDGER=') -eq 2) `
                "boot $sequence capacity/accounting mismatch"
            Require ((Get-Hex $text 'GXOS_NET10:PHASE66_B_GC_BEFORE_OBSERVE=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_ROOT_SURVIVAL=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_POST_GC_CONTINUATION=') -eq 0 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_CHECKPOINT2_OBSERVED=') -eq 2 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_ROOT_RELEASE_COUNT=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_DETACH_COUNT=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_FLS_CLEAN=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_SCHEDULER_TERMINATED=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_C_ROOT_STILL_LIVE=') -eq 1) `
                "boot $sequence B post-GC cancellation proof mismatch"
            Require ((Get-Hex $text 'GXOS_NET10:PHASE66_CANCEL_STATUS=') -eq 0 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_DUPLICATE_CANCEL_STATUS=') -eq 15 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_INVALID_CANCEL_STATUS=') -eq 5 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_WRONG_GENERATION_STATUS=') -eq 6 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_ADD_ONE_CANCEL_STATUS=') -eq 18 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_LATE_CANCEL_STATUS=') -eq 18 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_COMPLETED_CANCEL_STATUS=') -eq 16 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_CLOSED_CANCEL_STATUS=') -eq 17 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_STALE_B_CANCEL_STATUS=') -eq 6 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_STALE_B_POLL_STATUS=') -eq 6) `
                "boot $sequence cancellation status mismatch"
            Require ((Get-Hex $text 'GXOS_NET10:PHASE66_C_GC_COUNT=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_C_ROOT_SURVIVAL=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_C_POST_GC_CONTINUATION=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_C_ROOT_RELEASE_COUNT=') -eq 1 -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_C_DETACH_COUNT=') -eq 1) `
                "boot $sequence healthy C completion proof mismatch"
            foreach ($kind in @('VM', 'THREADS', 'OBJECTS')) {
                Require ((Get-Hex $text "GXOS_NET10:PHASE66_FINAL_$kind=") -eq
                         (Get-Hex $text "GXOS_NET10:PHASE66_BASELINE_$kind=")) `
                    "boot $sequence $kind baseline did not restore"
            }
            Require ((Get-Hex $text 'GXOS_NET10:PHASE66_B_SLOT=') -eq
                     (Get-Hex $text 'GXOS_NET10:PHASE66_D_SLOT=') -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_IDENTITY=') -ne
                     (Get-Hex $text 'GXOS_NET10:PHASE66_D_IDENTITY=') -and
                     (Get-Hex $text 'GXOS_NET10:PHASE66_B_GENERATION=') -ne
                     (Get-Hex $text 'GXOS_NET10:PHASE66_D_GENERATION=')) `
                "boot $sequence replacement did not advance the reused B slot generation"

        } finally {
            Stop-OwnedQemu $process
            $serialHash = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash
            Add-Content -LiteralPath (Join-Path $run 'summary.txt') `
                -Value "serial_sha256=$serialHash" -Encoding utf8
        }
        Write-Output ("PHASE66_POST_GC_CANCELLATION_RUN_{0}=PASS scenarios=12 serial_sha256={1}" -f
            $sequence, $serialHash)
    }
} finally {
    foreach ($process in $owned) {
        try { Stop-OwnedQemu $process } catch { }
    }
}
Write-Output "PHASE66_PAYLOAD_SHA256=$expectedHash"
Write-Output "PHASE66_FRESH_BOOTS=$RunCount"
Write-Output 'PHASE66_CANCELLATION_SCENARIOS_PER_BOOT=12'

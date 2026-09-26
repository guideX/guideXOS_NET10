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

function Count-Token([string]$text, [string]$token) {
    return [regex]::Matches($text, [regex]::Escape($token)).Count
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

Require ($RunCount -ge 3) 'Phase 64 requires at least three fresh boots.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Phase 64 harness or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload hash must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged Phase 64 payload hash mismatch.'
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
                if ($text.Contains('GXOS_NET10:PHASE64_COMPLETE=1') -or
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
            "phase64_scenarios=$(Count-Token $text 'GXOS_NET10:PHASE64_SCENARIO=')",
            "phase64_pass=$($text.Contains('GXOS_NET10:PHASE64_PASS=1'))") -Encoding utf8
        Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
                 $expectedHash) "boot $sequence payload hash changed"
        Require (!$text.Contains('GXOS_NET10:FAIL:') -and
                 !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
                 $text.Contains('GXOS_NET10:PHASE64_PASS=1')) "boot $sequence failed"
        foreach ($marker in @(
            'GXOS_NET10:NATIVEAOT_STARTUP_OK',
            'GXOS_NET10:MANAGED_GC_MAIN_OK=1',
            'GXOS_NET10:PHASE53O_PASS=1',
            'GXOS_NET10:MANAGED_WORKER_API_OK=1',
            'GXOS_NET10:PHASE64_API=BOUNDED_THREE_MANAGED_WORKERS',
            'GXOS_NET10:PHASE64_THREE_WORKER_OVERLAP=1',
            'GXOS_NET10:PHASE64_TWO_ROOT_OVERLAP=1',
            'GXOS_NET10:PHASE64_REQUEST_ISOLATION=1',
            'GXOS_NET10:PHASE64_RESULT_ISOLATION=1',
            'GXOS_NET10:PHASE64_FOURTH_WORKER_REJECTED=1',
            'GXOS_NET10:PHASE64_FOURTH_REJECT_NO_LOWER_ALLOCATION=1',
            'GXOS_NET10:PHASE64_ROOT_RELEASE_B_WHILE_C_LIVE=1',
            'GXOS_NET10:PHASE64_ROOT_RELEASE_C_WHILE_B_LIVE=1',
            'GXOS_NET10:PHASE64_CROSS_ROOT_STALE_REJECTED=1',
            'GXOS_NET10:PHASE64_ATTACH_FAILURE_ROLLBACK=1',
            'GXOS_NET10:PHASE64_ATTACH_FAILURE_CLEANUP_COMPLETE=1',
            'GXOS_NET10:PHASE64_ATTACH_FAILURE_STALE_HANDLE_REJECTED=1',
            'GXOS_NET10:PHASE64_CAPACITY_RECOVERED=1',
            'GXOS_NET10:PHASE64_SAME_SLOT_REUSE=1',
            'GXOS_NET10:PHASE64_COMPLETE=1')) {
            Require ($text.Contains($marker)) "boot $sequence missing marker: $marker"
        }
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_CAPACITY=') -eq 3) `
            "boot $sequence API capacity was not three"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_ROOT_CAPACITY=') -eq 2) `
            "boot $sequence root capacity was not two"
        Require ((Count-Token $text 'GXOS_NET10:PHASE64_SCENARIO=') -eq 12) `
            "boot $sequence did not complete 12 scenarios"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_MAX_ATTACHED_WORKERS=') -eq 3) `
            "boot $sequence did not attach three workers"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_MAX_ROOT_WORKERS=') -eq 2) `
            "boot $sequence did not overlap two roots"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_PEAK_API_WORKERS=') -eq 3) `
            "boot $sequence API peak was not three"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_PEAK_VM=') -gt
                 (Get-Hex $text 'GXOS_NET10:PHASE64_BASELINE_VM=')) `
            "boot $sequence VM peak did not exceed baseline"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_PEAK_THREADS=') -gt
                 (Get-Hex $text 'GXOS_NET10:PHASE64_BASELINE_THREADS=')) `
            "boot $sequence thread peak did not exceed baseline"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_PEAK_OBJECTS=') -gt
                 (Get-Hex $text 'GXOS_NET10:PHASE64_BASELINE_OBJECTS=')) `
            "boot $sequence object peak did not exceed baseline"
        foreach ($kind in @('VM', 'THREADS', 'OBJECTS')) {
            Require ((Get-Hex $text "GXOS_NET10:PHASE64_FINAL_$kind=") -eq
                     (Get-Hex $text "GXOS_NET10:PHASE64_BASELINE_$kind=")) `
                "boot $sequence $kind baseline did not restore"
        }
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_FINAL_API_WORKERS=') -eq 0) `
            "boot $sequence API worker baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_FINAL_ROOT_LEDGER=') -eq 0) `
            "boot $sequence root ledger baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_SCHEDULER_OBJECT_CAPACITY=') -eq 16) `
            "boot $sequence scheduler object capacity changed"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_COUNT=') -eq 19) `
            "boot $sequence managed callback count mismatch"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_A_SLOT=') -ne
                 (Get-Hex $text 'GXOS_NET10:PHASE64_B_SLOT=') -and
                 (Get-Hex $text 'GXOS_NET10:PHASE64_B_SLOT=') -ne
                 (Get-Hex $text 'GXOS_NET10:PHASE64_C_SLOT=')) `
            "boot $sequence worker slots were not distinct"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_B_ROOT_TOKEN=') -ne
                 (Get-Hex $text 'GXOS_NET10:PHASE64_C_ROOT_TOKEN=')) `
            "boot $sequence root tokens were not distinct"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE64_B_ROOT_SURVIVED=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE64_C_ROOT_SURVIVED=') -eq 1) `
            "boot $sequence root survival was not recorded"
        Write-Output ("PHASE64_CAPACITY_THREE_RUN_{0}=PASS scenarios=12 primary_lifecycles=36 serial={1}" -f
            $sequence, $serial)
    }
} finally { foreach ($process in $owned) { Stop-OwnedQemu $process } }

$unexpectedAfter = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue |
    Where-Object { $baselineQemuIds -notcontains $_.Id })
Require ($unexpectedAfter.Count -eq 0) 'QEMU cleanup failed.'
Write-Output "PHASE64_CAPACITY_THREE_PAYLOAD_SHA256=$expectedHash"
Write-Output "PHASE64_CAPACITY_THREE_RUNS=$RunCount"
Write-Output 'PHASE64_CAPACITY_THREE_SCENARIOS_PER_BOOT=12'
Write-Output 'PHASE64_CAPACITY_THREE_PRIMARY_LIFECYCLES=36_PER_BOOT'

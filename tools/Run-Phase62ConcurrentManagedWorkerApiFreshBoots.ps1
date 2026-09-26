[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [string]$QemuPath = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 300
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

Require ($RunCount -ge 3) 'Phase 62 requires at least three fresh boots.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Phase 62 harness or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload hash must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged Phase 62 payload hash is not authoritative.'
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
if ([string]::IsNullOrWhiteSpace($QemuPath)) {
    Require ($baselineQemuIds.Count -eq 0) 'An unowned QEMU process is already running.'
}
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
                if ($text.Contains('GXOS_NET10:PHASE62_COMPLETE=1') -or
                    $text.Contains('GXOS_NET10:FAIL:') -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 250
            }
        } finally { Stop-OwnedQemu $process }
        $text = Read-Serial $serial
        Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
                 $expectedHash) "boot $sequence payload hash changed"
        Require (!$text.Contains('GXOS_NET10:FAIL:') -and
                 !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
                 $text.Contains('GXOS_NET10:PHASE62_PASS=1')) "boot $sequence failed"
        foreach ($marker in @(
            'GXOS_NET10:NATIVEAOT_STARTUP_OK',
            'GXOS_NET10:MANAGED_GC_MAIN_OK=1',
            'GXOS_NET10:PHASE53O_PASS=1',
            'GXOS_NET10:MANAGED_WORKER_API_OK=1',
            'GXOS_NET10:PHASE62_API=BOUNDED_TWO_MANAGED_WORKERS',
            'GXOS_NET10:PHASE62_CAPACITY_REJECTED=1',
            'GXOS_NET10:PHASE62_REQUEST_ISOLATION=1',
            'GXOS_NET10:PHASE62_RESULT_ISOLATION=1',
            'GXOS_NET10:PHASE62_CROSS_HANDLE_REJECTED=1',
            'GXOS_NET10:PHASE62_STALE_CROSS_WORKER_REJECTED=1',
            'GXOS_NET10:PHASE62_RUNTIME_OVERLAP=1',
            'GXOS_NET10:PHASE62_SAME_SLOT_REUSE_TESTED=1',
            'GXOS_NET10:PHASE62_COMPLETE=1')) {
            Require ($text.Contains($marker)) "boot $sequence missing marker: $marker"
        }
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_CAPACITY=') -eq 2) `
            "boot $sequence API capacity was not two"
        Require ((Count-Token $text 'GXOS_NET10:PHASE62_CYCLE=') -eq 12) `
            "boot $sequence did not complete 12 pairs"
        Require ((Count-Token $text 'GXOS_NET10:PHASE62_RUNTIME_OVERLAP=1') -eq 12) `
            "boot $sequence did not prove 12 runtime overlaps"
        Require ((Count-Token $text 'GXOS_NET10:PHASE62_A_FIRST_RESULT=') -ge 1) `
            "boot $sequence did not prove A-first completion"
        Require ((Count-Token $text 'GXOS_NET10:PHASE62_B_FIRST_RESULT=') -ge 1) `
            "boot $sequence did not prove B-first completion"
        Require ((Count-Token $text 'GXOS_NET10:PHASE62_RECLAIM_A_WHILE_B_LIVE=1') -ge 1) `
            "boot $sequence did not reclaim A while B existed"
        Require ((Count-Token $text 'GXOS_NET10:PHASE62_RECLAIM_B_WHILE_A_LIVE=1') -ge 1) `
            "boot $sequence did not reclaim B while A existed"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_PEAK_API_WORKERS=') -eq 2) `
            "boot $sequence API peak was not two"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_PEAK_VM=') -gt
                 (Get-Hex $text 'GXOS_NET10:PHASE62_BASELINE_VM=')) `
            "boot $sequence VM peak did not exceed baseline"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_PEAK_THREADS=') -gt
                 (Get-Hex $text 'GXOS_NET10:PHASE62_BASELINE_THREADS=')) `
            "boot $sequence thread peak did not exceed baseline"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_PEAK_ROOT_LEDGER=') -ge 1) `
            "boot $sequence root peak was not observed"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_FINAL_VM=') -eq
                 (Get-Hex $text 'GXOS_NET10:PHASE62_BASELINE_VM=')) `
            "boot $sequence VM baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_FINAL_THREADS=') -eq
                 (Get-Hex $text 'GXOS_NET10:PHASE62_BASELINE_THREADS=')) `
            "boot $sequence thread baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_FINAL_OBJECTS=') -eq
                 (Get-Hex $text 'GXOS_NET10:PHASE62_BASELINE_OBJECTS=')) `
            "boot $sequence object baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_FINAL_API_WORKERS=') -eq 0) `
            "boot $sequence API worker baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE62_FINAL_ROOT_LEDGER=') -eq 0) `
            "boot $sequence root ledger baseline did not restore"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_COUNT=') -eq 17) `
            "boot $sequence managed callback count mismatch"
        Write-Output ("PHASE62_CONCURRENT_RUN_{0}=PASS pairs=12 serial={1}" -f
            $sequence, $serial)
    }
} finally { foreach ($process in $owned) { Stop-OwnedQemu $process } }
$unexpectedAfter = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue |
    Where-Object { $baselineQemuIds -notcontains $_.Id })
Require ($unexpectedAfter.Count -eq 0) 'QEMU cleanup failed.'
Write-Output "PHASE62_CONCURRENT_PAYLOAD_SHA256=$expectedHash"
Write-Output "PHASE62_CONCURRENT_RUNS=$RunCount"

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 120,
    [string]$SourcePayloadPath = '',
    [string]$PayloadSha256 = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$builtPayload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$sourcePayload = if ([string]::IsNullOrWhiteSpace($SourcePayloadPath)) {
    Join-Path $root 'artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll'
} else {
    [IO.Path]::GetFullPath($SourcePayloadPath)
}
$expectedHash = if ([string]::IsNullOrWhiteSpace($PayloadSha256)) {
    '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
} else {
    $PayloadSha256.ToUpperInvariant()
}

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
            try { return $reader.ReadToEnd() }
            finally { $reader.Dispose() }
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

Require ($RunCount -ge 3) 'At least three fresh boots are required.'
Require ((Test-Path -LiteralPath $efi) -and
         (Test-Path -LiteralPath $builtPayload)) 'Harness or payload is missing.'
Require (Test-Path -LiteralPath $sourcePayload) 'Source payload is missing.'
Require ((Get-FileHash $sourcePayload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Source payload hash changed.'
Require ((Get-FileHash $builtPayload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged payload hash changed.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) {
    [IO.Path]::GetFullPath($qemuCommand.Source)
} else {
    'C:\Program Files\qemu\qemu-system-x86_64.exe'
}
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require ((Test-Path -LiteralPath $ovmf) -and
         (Test-Path -LiteralPath $varsTemplate)) 'OVMF firmware is required.'
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
    'A pre-existing QEMU process is present.'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

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
                if ($text.Contains('GXOS_NET10:NATIVEAOT_DURABILITY_PASS=1') -or
                    $text.Contains('GXOS_NET10:FAIL:') -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 250
            }
        } finally {
            Stop-OwnedQemu $process
        }
        Start-Sleep -Milliseconds 250
        $text = Read-Serial $serial
        $hash = (Get-FileHash $builtPayload -Algorithm SHA256).Hash.ToUpperInvariant()
        Require ($hash -eq $expectedHash) "run $sequence payload hash changed"
        Require (!$text.Contains('GXOS_NET10:FAIL:') -and
                 !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) `
            "run $sequence fault or fail marker"
        foreach ($marker in @(
            'GXOS_NET10:NATIVEAOT_STARTUP_OK',
            'GXOS_NET10:GC_STARTUP_ADVANCED',
            'GXOS_NET10:WAITFORSINGLEOBJECTEX_WILL_BLOCK=0x0000000000000001',
            'GXOS_NET10:NATIVEAOT_DURABILITY_BASELINE_RUNNABLE_COUNT=0x0000000000000000',
            'GXOS_NET10:NATIVEAOT_DURABILITY_BASELINE_BLOCKED_COUNT=0x0000000000000001',
            'GXOS_NET10:NATIVEAOT_DURABILITY_BASELINE_ACTIVE_WAIT_COUNT=0x0000000000000001',
            'GXOS_NET10:NATIVEAOT_DURABILITY_BASELINE_VALID_WAIT_RECORDS=0x0000000000000001',
            'GXOS_NET10:MANAGED_ENTRY_OK',
            'GXOS_NET10:NATIVEAOT_DURABILITY_PASS=1',
            'GXOS_NET10:NATIVEAOT_DURABILITY_REPEATED_CALLBACKS=2',
            'GXOS_NET10:AFTER_MANAGED_RETURN=0x0000000000000000',
            'GXOS_NET10:MANAGED_ENTRY_COMPLETE')) {
            Require ($text.Contains($marker)) "run $sequence missing marker: $marker"
        }
        $mainIdentity = Get-Hex $text 'GXOS_NET10:NATIVEAOT_DURABILITY_MAIN_IDENTITY='
        $workerIdentity = Get-Hex $text 'GXOS_NET10:NATIVEAOT_DURABILITY_BLOCKED_WORKER_IDENTITY='
        $mainFls = Get-Hex $text 'GXOS_NET10:NATIVEAOT_DURABILITY_MAIN_FLS='
        $workerFls = Get-Hex $text 'GXOS_NET10:NATIVEAOT_DURABILITY_BLOCKED_WORKER_FLS='
        Require ($mainIdentity -ne $workerIdentity -and $mainFls -ne 0 -and
                 $workerFls -ne 0 -and $mainFls -ne $workerFls) `
            "run $sequence per-thread identity/FLS isolation mismatch"
        foreach ($field in @(
            'LIVE_OBJECTS', 'LIVE_PUBLIC_HANDLES', 'INTERNAL_REFERENCES',
            'RUNNABLE_COUNT', 'BLOCKED_COUNT', 'ACTIVE_WAIT_COUNT',
            'VALID_WAIT_RECORDS', 'LIVE_THREAD_COUNT', 'FLS_SLOT_COUNT',
            'VM_REGION_COUNT')) {
            $before = Get-Hex $text ("GXOS_NET10:NATIVEAOT_DURABILITY_BASELINE_{0}=" -f $field)
            $after = Get-Hex $text ("GXOS_NET10:NATIVEAOT_DURABILITY_AFTER_CLEANUP_{0}=" -f $field)
            Require ($before -eq $after) "run $sequence count drift: $field"
        }
        Write-Output ("NATIVEAOT_DURABILITY_RUN_{0}=PASS bytes={1} sha256={2} serial={3}" -f `
            $sequence, ([Text.Encoding]::UTF8.GetByteCount($text)),
            (Get-FileHash $serial -Algorithm SHA256).Hash.ToUpperInvariant(), $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
    'QEMU cleanup failed.'
Write-Output "NATIVEAOT_DURABILITY_PAYLOAD_SHA256=$expectedHash"
Write-Output "NATIVEAOT_DURABILITY_RUNS=$RunCount"

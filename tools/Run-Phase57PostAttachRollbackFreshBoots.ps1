[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [string]$QemuPath = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180
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

function Count-Token([string]$text, [string]$token) {
    return [regex]::Matches($text, [regex]::Escape($token)).Count
}

function Count-HexValue([string]$text, [string]$prefix, [uint64]$expected) {
    $key = $prefix.TrimEnd('=')
    $hex = $expected.ToString('X')
    return [regex]::Matches($text,
        [regex]::Escape($key) + '=0x0*' + $hex + '(?:\r?\n|$)').Count
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

Require ($RunCount -ge 1) 'At least one authoritative Phase 57 boot is required.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Phase 57 harness or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') `
    'Payload SHA-256 must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged Phase 57 payload hash is not authoritative.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if (![string]::IsNullOrWhiteSpace($QemuPath)) {
    [IO.Path]::GetFullPath($QemuPath)
} elseif ($null -ne $qemuCommand) {
    [IO.Path]::GetFullPath($qemuCommand.Source)
} else {
    'C:\Program Files\qemu\qemu-system-x86_64.exe'
}
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$qemuProcessName = [IO.Path]::GetFileNameWithoutExtension($qemu)
Require (@(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue).Count -eq 0) `
    "A pre-existing process for the selected QEMU executable is present: $qemuProcessName"
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) `
    'OVMF firmware is required.'

$baselineQemuIds = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue |
    ForEach-Object { $_.Id })
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null
$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        $unexpectedBefore = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue |
            Where-Object { $baselineQemuIds -notcontains $_.Id -and
                           $owned.Id -notcontains $_.Id })
        Require ($unexpectedBefore.Count -eq 0) `
            "An unowned QEMU process exists before fresh Phase 57 boot $sequence."
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
                if ($text.Contains('GXOS_NET10:PHASE57_COMPLETE=1') -or
                    $text.Contains('GXOS_NET10:PHASE57_FAILURE=1') -or
                    $text.Contains('GXOS_NET10:FAIL:') -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 250
            }
        } finally {
            Stop-OwnedQemu $process
        }
        $text = Read-Serial $serial
        Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
                 $expectedHash) "run $sequence payload hash changed"
        Require ($text.Contains('GXOS_NET10:PHASE57_PASS=1') -and
                 !$text.Contains('GXOS_NET10:PHASE57_FAILURE=1') -and
                 !$text.Contains('GXOS_NET10:PHASE56_INJECTION_FIRED=1') -and
                 !$text.Contains('GXOS_NET10:FAIL:') -and
                 !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:PAGE_FAULT_')) `
            "run $sequence did not complete a clean Phase 57 proof"
        foreach ($marker in @(
            'GXOS_NET10:PHASE53O_PASS=1',
            'GXOS_NET10:MANAGED_GC_MAIN_OK=1',
            'GXOS_NET10:PHASE57_BEGIN',
            'GXOS_NET10:PHASE57_INJECTION_POINT=AFTER_RUNTIME_ATTACH',
            'GXOS_NET10:PHASE57_DIAGNOSTIC_HOOK=GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK',
            'GXOS_NET10:PHASE57_EXACTLY_ONE_DETACH=1',
            'GXOS_NET10:PHASE57_NO_DOUBLE_CLEANUP=1',
            'GXOS_NET10:PHASE57_GENERATION_SAFE_SLOT_REUSE=1',
            'GXOS_NET10:PHASE57_LEAK_TREND_NONE=1',
            'GXOS_NET10:PHASE57_COMPLETE=1',
            'GXOS_NET10:PHASE57_PASS=1',
            'GXOS_NET10:MANAGED_WORKER_POSTATTACH_ROLLBACK_OK=1')) {
            Require ($text.Contains($marker)) "run $sequence missing marker: $marker"
        }
        foreach ($pair in @(
            @('GXOS_NET10:PHASE57_INJECTION_FIRED=1', 12),
            @('GXOS_NET10:PHASE57_RUNTIME_ATTACH_SUCCEEDED=1', 12),
            @('GXOS_NET10:PHASE57_DETACH_EXECUTED=1', 12),
            @('GXOS_NET10:PHASE57_RUNTIME_LOCAL_STATE_CLEARED=1', 12),
            @('GXOS_NET10:PHASE57_NO_POST_ATTACH_WORKLOAD=1', 12),
            @('GXOS_NET10:PHASE57_REPLACEMENT_WORKER_SUCCEEDED=1', 12),
            @('GXOS_NET10:PHASE57_REPLACEMENT_POST_GC_CONTINUATION=1', 12),
            @('GXOS_NET10:PHASE57_REPLACEMENT_RECLAIM=1', 12),
            @('GXOS_NET10:PHASE57_STALE_HANDLE_REJECTED=1', 12),
            @('GXOS_NET10:PHASE57_STALE_IDENTITY_REJECTED=1', 12),
            @('GXOS_NET10:PHASE57_STALE_GENERATION_REJECTED=1', 12),
            @('GXOS_NET10:PHASE57_STALE_DETACH_REJECTED=1', 12))) {
            Require ((Count-Token $text $pair[0]) -eq $pair[1]) `
                "run $sequence marker count mismatch: $($pair[0])"
        }
        Require ((Count-HexValue $text 'GXOS_NET10:PHASE57_ATTACH_COUNT_AFTER=' 1) -eq 12) `
            "run $sequence attach count was not one for every failure"
        Require ((Count-HexValue $text 'GXOS_NET10:PHASE57_DETACH_COUNT_AFTER=' 1) -eq 12) `
            "run $sequence detach count was not one for every failure"
        Require ((Count-HexValue $text 'GXOS_NET10:PHASE57_ATTACH_COUNT_BEFORE=' 0) -eq 12) `
            "run $sequence attach baseline was not zero"
        Require ((Count-HexValue $text 'GXOS_NET10:PHASE57_DETACH_COUNT_BEFORE=' 0) -eq 12) `
            "run $sequence detach baseline was not zero"
        $baselineVm = Get-Hex $text 'GXOS_NET10:PHASE57_RESOURCE_BASELINE_VM_REGIONS='
        $baselineThreads = Get-Hex $text 'GXOS_NET10:PHASE57_RESOURCE_BASELINE_THREADS='
        $baselineObjects = Get-Hex $text 'GXOS_NET10:PHASE57_RESOURCE_BASELINE_OBJECTS='
        $peakVm = Get-Hex $text 'GXOS_NET10:PHASE57_RESOURCE_PEAK_VM_REGIONS='
        $peakThreads = Get-Hex $text 'GXOS_NET10:PHASE57_RESOURCE_PEAK_THREADS='
        $peakObjects = Get-Hex $text 'GXOS_NET10:PHASE57_RESOURCE_PEAK_OBJECTS='
        Require ($peakVm -gt $baselineVm -and $peakThreads -gt $baselineThreads -and
                 $peakObjects -gt $baselineObjects) `
            "run $sequence did not observe the prepared/runtime-attached peak"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE57_INJECTED_FAILURE_CYCLES=') -eq 12 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE57_PASSED_FAILURE_CYCLES=') -eq 12) `
            "run $sequence did not pass all 12 failure cycles"
        Require ((Get-Hex $text 'GXOS_NET57_DUPLICATE_DETACH_REJECTIONS=') -eq 12 -and
                 (Get-Hex $text 'GXOS_NET57_STALE_DETACH_REJECTIONS=') -eq 12) `
            "run $sequence detach rejection counts are incorrect"
        $callbackFinal = Get-Hex $text 'GXOS_NET10:PHASE57_CALLBACK_FINAL='
        $callbackBaseline = Get-Hex $text 'GXOS_NET10:PHASE57_CALLBACK_BASELINE='
        $gcFinal = Get-Hex $text 'GXOS_NET10:PHASE57_GC_CALLBACK_FINAL='
        $gcBaseline = Get-Hex $text 'GXOS_NET10:PHASE57_GC_CALLBACK_BASELINE='
        Require ($callbackFinal -eq $callbackBaseline + 24 -and
                 $gcFinal -eq $gcBaseline + 12) `
            "run $sequence callback accounting is incorrect"
        Write-Output ("PHASE57_ROLLBACK_RUN_{0}=PASS cycles=12 peakVm=0x{1:X} peakThreads=0x{2:X} peakObjects=0x{3:X} serial={4}" -f `
            $sequence, $peakVm, $peakThreads, $peakObjects, $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
$unexpectedAfter = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue |
    Where-Object { $baselineQemuIds -notcontains $_.Id })
Require ($unexpectedAfter.Count -eq 0) 'QEMU cleanup failed.'
Write-Output "PHASE57_ROLLBACK_PAYLOAD_SHA256=$expectedHash"
Write-Output "PHASE57_ROLLBACK_RUNS=$RunCount"

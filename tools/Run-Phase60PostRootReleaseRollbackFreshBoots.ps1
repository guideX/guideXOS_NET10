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
    if ($value.StartsWith('0x')) { return [Convert]::ToUInt64($value.Substring(2), 16) }
    return [Convert]::ToUInt64($value, 10)
}

function Stop-OwnedQemu([System.Diagnostics.Process]$process) {
    try { $process.Refresh() } catch { }
    try {
        if (!$process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    } catch { }
    try { $process.WaitForExit(5000) | Out-Null } catch { }
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        throw "Owned QEMU process remained: $($process.Id)"
    }
}

Require ($RunCount -ge 1) 'At least one Phase 60 boot is required.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) 'Phase 60 harness or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) 'Staged Phase 60 payload hash is not authoritative.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if (![string]::IsNullOrWhiteSpace($QemuPath)) { [IO.Path]::GetFullPath($QemuPath) } elseif ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$qemuProcessName = [IO.Path]::GetFileNameWithoutExtension($qemu)
Require (@(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue).Count -eq 0) "A pre-existing process for the selected QEMU executable is present: $qemuProcessName"
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) 'OVMF firmware is required.'

$baselineQemuIds = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null
$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        $run = Join-Path $evidence ("runs\run-{0}" -f $sequence)
        New-Item -ItemType Directory -Force -Path $run | Out-Null
        $code = Join-Path $run 'edk2-code.fd'; $vars = Join-Path $run 'edk2-vars.fd'
        $serial = Join-Path $run 'serial.log'; $stdout = Join-Path $run 'qemu.stdout.log'; $stderr = Join-Path $run 'qemu.stderr.log'
        Copy-Item -LiteralPath $ovmf -Destination $code; Copy-Item -LiteralPath $varsTemplate -Destination $vars
        $arguments = @('-machine','q35','-accel','tcg,thread=multi','-m','128M',
            '-drive',"if=pflash,format=raw,readonly=on,file=$code",'-drive',"if=pflash,format=raw,file=$vars",
            '-drive','file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk','-rtc','base=utc,clock=vm','-boot','order=c',
            '-serial',"file:$serial",'-monitor','none','-display','none','-no-reboot','-no-shutdown')
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
        $owned += $process
        try {
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while ((Get-Date) -lt $deadline) {
                $text = Read-Serial $serial
                if ($text.Contains('GXOS_NET10:PHASE60_COMPLETE=1') -or $text.Contains('GXOS_NET10:PHASE60_FAILURE=1') -or $text.Contains('GXOS_NET10:FAIL:') -or $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 250
            }
        } finally { Stop-OwnedQemu $process }
        $text = Read-Serial $serial
        Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) "run $sequence payload hash changed"
        Require ($text.Contains('GXOS_NET10:PHASE60_PASS=1') -and !$text.Contains('GXOS_NET10:PHASE60_FAILURE=1') -and !$text.Contains('GXOS_NET10:FAIL:') -and !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and !$text.Contains('GXOS_NET10:PAGE_FAULT_')) "run $sequence did not complete a clean Phase 60 proof"
        foreach ($marker in @(
            'GXOS_NET10:PHASE53O_PASS=1','GXOS_NET10:MANAGED_GC_MAIN_OK=1',
            'GXOS_NET10:PHASE60_BEGIN','GXOS_NET10:PHASE60_INJECTION_POINT=AFTER_MANAGED_ROOT_RELEASE',
            'GXOS_NET10:PHASE60_DIAGNOSTIC_HOOK=GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE',
            'GXOS_NET10:PHASE60_COMPILE_GATE=GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK',
            'GXOS_NET10:PHASE60_MANAGED_ROOT_AUTHORITY=MANAGED_STATIC_REFERENCE',
            'GXOS_NET10:PHASE60_ROOT_CLEANUP_AUTHORITY=MANAGED_ROOT_RELEASE_EXPORT',
            'GXOS_NET10:PHASE60_PHYSICAL_RELOCATION=NOT_MEASURED',
            'GXOS_NET10:PHASE60_RAW_MANAGED_ADDRESS_RETAINED=0',
            'GXOS_NET10:PHASE60_ROOT_SURVIVAL_BEFORE_FAILURE=1',
            'GXOS_NET10:PHASE60_EXACTLY_ONE_ROOT_RELEASE=1',
            'GXOS_NET10:PHASE60_ROOT_OWNERSHIP_ENDED_BEFORE_INJECTION=1',
            'GXOS_NET10:PHASE60_RUNTIME_OWNERSHIP_ACTIVE_AT_INJECTION=1',
            'GXOS_NET10:PHASE60_DETACH_AFTER_INJECTION=1',
            'GXOS_NET10:PHASE60_FLS_CLEARED_AFTER_DETACH=1',
            'GXOS_NET10:PHASE60_EXACTLY_ONE_DETACH=1',
            'GXOS_NET10:PHASE60_NO_DOUBLE_CLEANUP=1',
            'GXOS_NET10:PHASE60_GENERATION_SAFE_SLOT_REUSE=1',
            'GXOS_NET10:PHASE60_LEAK_TREND_NONE=1',
            'GXOS_NET10:MANAGED_WORKER_POSTROOTRELEASE_ROLLBACK_OK=1',
            'GXOS_NET10:PHASE60_COMPLETE=1','GXOS_NET10:PHASE60_PASS=1')) {
            Require ($text.Contains($marker)) "run $sequence missing marker: $marker"
        }
        foreach ($marker in @(
            @('GXOS_NET10:PHASE60_INJECTION_FIRED=1',12),
            @('GXOS_NET10:PHASE60_ROOT_RELEASED=1',12),
            @('GXOS_NET10:PHASE60_ROOT_OWNERSHIP_ENDED_BEFORE_INJECTION=1',12),
            @('GXOS_NET10:PHASE60_RUNTIME_OWNERSHIP_ACTIVE_AT_INJECTION=1',12),
            @('GXOS_NET10:PHASE60_DETACH_AFTER_INJECTION=1',12),
            @('GXOS_NET10:PHASE60_FLS_CLEARED_AFTER_DETACH=1',12),
            @('GXOS_NET10:PHASE60_GC_COMPLETED=1',12),
            @('GXOS_NET10:PHASE60_ROOT_SURVIVAL_PROVEN=1',12),
            @('GXOS_NET10:PHASE60_NORMAL_POST_GC_CONTINUATION_SUPPRESSED=1',12),
            @('GXOS_NET10:PHASE60_ROOT_RELEASED=1',12),
            @('GXOS_NET10:PHASE60_DUPLICATE_ROOT_RELEASE_REJECTED=1',12),
            @('GXOS_NET10:PHASE60_DUPLICATE_DETACH_REJECTED=1',12),
            @('GXOS_NET10:PHASE60_FAILED_ROOT_SURVIVED_GC=1',12),
            @('GXOS_NET10:PHASE60_REPLACEMENT_POST_GC_CONTINUATION=1',12),
            @('GXOS_NET10:PHASE60_REPLACEMENT_ROOT_SURVIVED_GC=1',12),
            @('GXOS_NET10:PHASE60_REPLACEMENT_RECLAIM=1',12),
            @('GXOS_NET10:PHASE60_STALE_PRE_GC_OBJECT_IDENTITY_REJECTED=1',12))) {
            Require ((Count-Token $text $marker[0]) -eq $marker[1]) "run $sequence marker count mismatch: $($marker[0])"
        }
        Require ((Get-Hex $text 'GXOS_NET10:PHASE60_INJECTED_FAILURE_CYCLES=') -eq 12 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE60_PASSED_FAILURE_CYCLES=') -eq 12) "run $sequence did not pass all 12 failure cycles"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE60_FAILED_ROOT_RELEASES=') -eq 12 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE60_DUPLICATE_ROOT_RELEASE_REJECTIONS=') -eq 12) "run $sequence root release totals are incorrect"
        Require ((Get-Hex $text 'GXOS_NET10:PHASE60_ROOT_RELEASE_COUNT_AT_INJECTION=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE60_ROOT_LEDGER_AT_INJECTION=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE60_RUNTIME_ATTACH_COUNT_AT_INJECTION=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:PHASE60_RUNTIME_DETACH_COUNT_AT_INJECTION=') -eq 0) "run $sequence Phase 60 ordering snapshot is incorrect"
        $baselineVm = Get-Hex $text 'GXOS_NET10:PHASE60_RESOURCE_BASELINE_VM_REGIONS='; $peakVm = Get-Hex $text 'GXOS_NET10:PHASE60_RESOURCE_PEAK_VM_REGIONS='
        $baselineThreads = Get-Hex $text 'GXOS_NET10:PHASE60_RESOURCE_BASELINE_THREADS='; $peakThreads = Get-Hex $text 'GXOS_NET10:PHASE60_RESOURCE_PEAK_THREADS='
        $baselineObjects = Get-Hex $text 'GXOS_NET10:PHASE60_RESOURCE_BASELINE_OBJECTS='; $peakObjects = Get-Hex $text 'GXOS_NET10:PHASE60_RESOURCE_PEAK_OBJECTS='
        Require ($peakVm -gt $baselineVm -and $peakThreads -gt $baselineThreads -and $peakObjects -gt $baselineObjects) "run $sequence did not observe a resource peak"
        Write-Output ("PHASE60_POSTROOTRELEASE_ROLLBACK_RUN_{0}=PASS cycles=12 peakVm=0x{1:X} peakThreads=0x{2:X} peakObjects=0x{3:X} serial={4}" -f $sequence,$peakVm,$peakThreads,$peakObjects,$serial)
    }
} finally { foreach ($process in $owned) { Stop-OwnedQemu $process } }
$unexpectedAfter = @(Get-Process -Name $qemuProcessName -ErrorAction SilentlyContinue | Where-Object { $baselineQemuIds -notcontains $_.Id })
Require ($unexpectedAfter.Count -eq 0) 'QEMU cleanup failed.'
Write-Output "PHASE60_POSTROOTRELEASE_ROLLBACK_PAYLOAD_SHA256=$expectedHash"
Write-Output "PHASE60_POSTROOTRELEASE_ROLLBACK_RUNS=$RunCount"

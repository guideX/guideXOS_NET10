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
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open,
            [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object IO.StreamReader($stream)
            try {
                $text = $reader.ReadToEnd()
                return $(if ($null -eq $text) { '' } else { $text })
            } finally { $reader.Dispose() }
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

function Get-Count([string]$text, [string]$token) {
    return [regex]::Matches($text, [regex]::Escape($token)).Count
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
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Scheduler callback harness or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged scheduler callback payload hash is not authoritative.'
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
Require ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) `
    'OVMF firmware is required.'
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
    'A pre-existing QEMU process is present.'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
            "QEMU already exists before fresh scheduler callback boot $sequence."
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
                if ($text.Contains('GXOS_NET10:MANAGED_THREAD_REUSE_OK') -or
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
        Require (!$text.Contains('GXOS_NET10:FAIL:') -and
                 !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:PAGE_FAULT_')) `
            "run $sequence fault or fail marker"
        foreach ($marker in @(
            'GXOS_NET10:NATIVEAOT_STARTUP_OK',
            'GXOS_NET10:GC_STARTUP_ADVANCED',
            'GXOS_NET10:WAITFORSINGLEOBJECTEX_WILL_BLOCK=0x0000000000000001',
            'GXOS_NET10:MANAGED_ENTRY_COMPLETE',
            'GXOS_NET10:MANAGED_CALLBACK_1_OK',
            'GXOS_NET10:MANAGED_CALLBACK_2_OK',
            'GXOS_NET10:MANAGED_THREAD_ATTACH_OK=1',
            'GXOS_NET10:MANAGED_THREAD_CALLBACK_1_OK',
            'GXOS_NET10:MANAGED_THREAD_CALLBACK_2_OK',
            'GXOS_NET10:MANAGED_THREAD_SWITCH_BLOCKED=1',
            'GXOS_NET10:MANAGED_THREAD_SWITCH_RESUMED=1',
            'GXOS_NET10:MANAGED_THREAD_DETACH_OK=1',
            'GXOS_NET10:MANAGED_THREAD_RECLAIMED=1',
            'GXOS_NET10:MANAGED_THREAD_SECOND_FRESH=1',
            'GXOS_NET10:MANAGED_THREAD_REUSE_OK=1',
            'GXOS_NET10:NATIVEAOT_DURABILITY_PASS=1',
            'GXOS_NET10:MANAGED_CALLBACK_COUNT=0x0000000000000005',
            'GXOS_NET10:MANAGED_CALLBACK_PROCESS_INITIALIZATION_CALLS=0x0000000000000001')) {
            Require ($text.Contains($marker)) "run $sequence missing marker: $marker"
        }
        Require ((Get-Count $text 'GXOS_NET10:NATIVEAOT_STARTUP_OK') -eq 1) `
            "run $sequence repeated NativeAOT startup marker"
        Require ((Get-Count $text 'GXOS_NET10:GC_STARTUP_ADVANCED') -eq 1) `
            "run $sequence repeated GC startup marker"
        Require ((Get-Count $text 'GXOS_NET10:MANAGED_ENTRY_COMPLETE') -eq 1) `
            "run $sequence repeated managed-entry completion marker"
        Require ($text.IndexOf('GXOS_NET10:MANAGED_ENTRY_COMPLETE') -lt
                 $text.IndexOf('GXOS_NET10:MANAGED_CALLBACK_1_BEGIN')) `
            "run $sequence callback began before managed entry completion"
        Require ($text.IndexOf('GXOS_NET10:MANAGED_CALLBACK_2_OK') -lt
                 $text.IndexOf('GXOS_NET10:MANAGED_THREAD_CREATED')) `
            "run $sequence scheduler thread began before main callbacks completed"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_RESULT1=') -eq 0x0001002A -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_RESULT2=') -eq 0x00020064) `
            "run $sequence main callback results are incorrect"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_CALLBACK_1_RESULT=') -eq 0x00030008 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_CALLBACK_2_RESULT=') -eq 0x00040009 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_SECOND_RESULT=') -eq 0x0005000A) `
            "run $sequence scheduler callback results are incorrect"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_NATIVE_STATE_BEFORE=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_NATIVE_STATE_AFTER=') -eq [UInt64]::MaxValue -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_RESUMED_NATIVE_STATE=') -eq [UInt64]::MaxValue) `
            "run $sequence NativeAOT thread-state transition is incorrect"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_FLS_BEFORE=') -eq 0 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_FLS_AFTER=') -ne 0 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_FLS_AFTER=') -eq
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_RESUMED_FLS=') -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_FLS_AFTER=') -ne
                 (Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_MAIN_FLS_AFTER=')) `
            "run $sequence scheduler FLS isolation or switch preservation failed"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_CREATED_IDENTITY=') -ne
                 (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_SECOND_IDENTITY=')) `
            "run $sequence scheduler thread identities were reused unsafely"
        Require ((Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_FINALIZER_WAIT_RECORD=') -ne 0 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_ACTIVE_WAITS=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_VALID_WAIT_RECORDS=') -eq 1 -and
                 (Get-Hex $text 'GXOS_NET10:MANAGED_CALLBACK_STACK_VM_REGIONS=') -eq 2) `
            "run $sequence finalizer wait or stack VM state changed"
        Write-Output ("NATIVEAOT_SCHEDULER_CALLBACK_RUN_{0}=PASS bytes={1} sha256={2} serial={3}" -f `
            $sequence, ([Text.Encoding]::UTF8.GetByteCount($text)),
            (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant(), $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
    'QEMU cleanup failed.'
Write-Output "NATIVEAOT_SCHEDULER_CALLBACK_PAYLOAD_SHA256=$expectedHash"
Write-Output "NATIVEAOT_SCHEDULER_CALLBACK_RUNS=$RunCount"

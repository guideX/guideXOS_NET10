[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
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

Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Phase 53X callback harness or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') `
    'Payload SHA-256 must be 64 hex characters.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq
         $expectedHash) 'Staged callback payload hash is not authoritative.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
    'A pre-existing QEMU process is present.'

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

$run = Join-Path $evidence 'run-1'
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
try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $text = Read-Serial $serial
        if ($text.Contains('GXOS_NET10:MANAGED_GC_WORKER_RETURN_OK=1') -or
            $text.Contains('GXOS_NET10:FAIL:') -or
            $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) { break }
        if ($process.HasExited) { break }
        Start-Sleep -Milliseconds 250
    }
} finally {
    Stop-OwnedQemu $process
}

$text = Read-Serial $serial
$failures = @([regex]::Matches($text, 'GXOS_NET10:FAIL:[^\r\n]+') |
    ForEach-Object { $_.Value })
$unexpected_failures = @($failures | Where-Object {
    $_ -ne 'GXOS_NET10:FAIL:nativeaot-managed-callback-post-state'
})
Require (!$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
         !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
         $unexpected_failures.Count -eq 0) `
    'Phase 53X callback reclaim assertion or guest fault fired.'
foreach ($marker in @(
    'GXOS_NET10:MANAGED_THREAD_CALLBACK_2_OK',
    'GXOS_NET10:MANAGED_THREAD_RETURN_VALUE=',
    'GXOS_NET10:MANAGED_THREAD_DETACH_OK=1',
    'GXOS_NET10:MANAGED_THREAD_RECLAIMED=1',
    'GXOS_NET10:MANAGED_THREAD_REUSE_OK=1',
    'GXOS_NET10:MANAGED_GC_WORKER_RETURN_OK=1')) {
    Require ($text.Contains($marker)) "Missing Phase 53X marker: $marker"
}
$before = Get-Hex $text 'GXOS_NET10:MANAGED_GC_THREAD_RECLAIM_BEFORE_VM_REGIONS='
$created = Get-Hex $text 'GXOS_NET10:MANAGED_GC_THREAD_RECLAIM_AFTER_VM_REGIONS='
$after = Get-Hex $text 'GXOS_NET10:MANAGED_GC_THREAD_RECLAIM_AFTER_CLOSE_VM_REGIONS='
Require ($created -eq ($before + 2U) -and $after -eq $before) `
    'Callback stack VM region baseline was not restored by reclaim.'
Write-Output ("PHASE53X_CALLBACK_RECLAIM=PASS identity=0x{0:X} vmBefore=0x{1:X} vmCreated=0x{2:X} vmAfter=0x{3:X} serial={4}" -f `
    (Get-Hex $text 'GXOS_NET10:MANAGED_THREAD_IDENTITY='), $before, $created, $after, $serial)
if ($failures.Count -ne 0) {
    Write-Output ("PHASE53X_ISOLATED_DOWNSTREAM_FAILURE={0}" -f
        ([string]::Join(',', $failures)))
}
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
    'QEMU cleanup failed.'

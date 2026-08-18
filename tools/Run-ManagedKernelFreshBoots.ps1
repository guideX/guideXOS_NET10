[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll'
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

Require ($RunCount -ge 3) 'Three fresh ManagedKernel boots are required.'
Require ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'ManagedKernel EFI or payload is missing.'
Require ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
Require ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'ManagedKernel payload size changed.'
Require ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
    'ManagedKernel staged payload hash does not match the requested identity.'
Require (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } `
    else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
Require (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) 'OVMF firmware is required.'
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
    'A pre-existing QEMU process is present.'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) `
            "QEMU already exists before ManagedKernel boot $sequence."
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
            -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
            -PassThru -WindowStyle Hidden
        $owned += $process
        try {
            $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
            while ((Get-Date) -lt $deadline) {
                $text = Read-Serial $serial
                if ($text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE1_PASS') -or
                    $text.Contains('GXOS_NET10:FAIL:') -or
                    $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
                    $text.Contains('GXOS_NET10:PAGE_FAULT_')) { break }
                if ($process.HasExited) { break }
                Start-Sleep -Milliseconds 100
            }
        } finally {
            Stop-OwnedQemu $process
        }
        Start-Sleep -Milliseconds 250
        $text = Read-Serial $serial
        $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
        Require ($hash -eq $expectedHash) "ManagedKernel payload hash changed on boot $sequence."
        Require (!$text.Contains('GXOS_NET10:FAIL:') -and
                 !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
                 !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
                 !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
            "ManagedKernel boot $sequence reported a fault, fail-fast, page fault, or unresolved import."
        foreach ($marker in @(
            'GXOS_NET10:NATIVEAOT_STARTUP_OK',
            'GXOS_NET10:GC_STARTUP_ADVANCED',
            'GXOS_NET10:NATIVEAOT_DURABILITY_PASS=1',
            'GXOS_NET10:MANAGED_KERNEL_BOOTSTRAP_OK',
            'GXOS_NET10:MANAGED_ENTRY_COMPLETE',
            'GXOS_NET10:MANAGED_KERNEL_INIT_OK',
            'GXOS_NET10:MANAGED_KERNEL_ABI_V1_OK',
            'GXOS_NET10:MANAGED_KERNEL_SYSTEM_INFO_OK',
            'GXOS_NET10:MANAGED_KERNEL_REPEAT_QUERY_OK',
            'GXOS_NET10:MANAGED_KERNEL_BAD_VERSION_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_SMALL_BUFFER_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_PHASE1_PASS')) {
            Require ($text.Contains($marker)) "ManagedKernel boot $sequence missing marker: $marker"
        }
        Require (([regex]::Matches($text, 'GXOS_NET10:NATIVEAOT_STARTUP_OK')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated NativeAOT startup."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_INIT_OK')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated managed initialization."
        Write-Output ("MANAGED_KERNEL_QEMU_RUN_{0}=PASS bytes={1} sha256={2} serial={3}" -f `
            $sequence, ([Text.Encoding]::UTF8.GetByteCount($text)),
            (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant(), $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
Require (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) 'QEMU cleanup failed.'
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_QEMU_RUNS=$RunCount"

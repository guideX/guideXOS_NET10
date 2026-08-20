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

function Get-OwnedQemu {
    $scope = @($gate, $evidence)
    return @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
        Where-Object {
            $commandLine = [string]$_.CommandLine
            $scope | Where-Object {
                $commandLine.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0
            } | Select-Object -First 1
        })
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
Require (@(Get-OwnedQemu).Count -eq 0) `
    'A QEMU process already owns the ManagedKernel gate/evidence paths.'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null

$owned = @()
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        Require (@(Get-OwnedQemu).Count -eq 0) `
            "A QEMU process already owns the ManagedKernel gate/evidence paths before boot $sequence."
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
                if ($text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE6_PASS') -or
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
            'GXOS_NET10:MANAGED_KERNEL_BOOTSTRAP_OK',
            'GXOS_NET10:MANAGED_KERNEL_BOOT_RESOURCES_OK',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_REGION_OK',
            'GXOS_NET10:MANAGED_KERNEL_BOOT_RESOURCES_INSTALLED',
            'GXOS_NET10:MANAGED_KERNEL_BOOT_RESOURCE_NEGATIVE_TESTS_OK',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_MAP_MATCH',
            'GXOS_NET10:MANAGED_ENTRY_COMPLETE',
            'GXOS_NET10:MANAGED_KERNEL_INIT_OK',
            'GXOS_NET10:MANAGED_KERNEL_ABI_V1_OK',
            'GXOS_NET10:MANAGED_KERNEL_SYSTEM_INFO_OK',
            'GXOS_NET10:MANAGED_KERNEL_REPEAT_QUERY_OK',
            'GXOS_NET10:MANAGED_KERNEL_BAD_VERSION_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_SMALL_BUFFER_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_PHASE1_PASS',
            'GXOS_NET10:MANAGED_KERNEL_PHASE2_PASS',
            'GXOS_NET10:MANAGED_KERNEL_START_BEFORE_INIT_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_START_BEFORE_PUBLICATION_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_START_BEFORE_HOST_SERVICES_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_LIFECYCLE_NEGATIVE_TESTS_OK',
            'GXOS_NET10:MANAGED_KERNEL_HOST_SERVICES_INSTALLED',
            'GXOS_NET10:MANAGED_KERNEL_HOST_SERVICES_REPEAT_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_HOST_LOG_FROM_MANAGED',
            'GXOS_NET10:MANAGED_KERNEL_HOST_LOG_CALL_OK',
            'GXOS_NET10:MANAGED_KERNEL_MONOTONIC_TIME_OK',
            'GXOS_NET10:MANAGED_KERNEL_START_OK',
            'GXOS_NET10:MANAGED_KERNEL_START_ONCE_OK',
            'GXOS_NET10:MANAGED_KERNEL_PHASE3_PASS',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_NATIVE_SNAPSHOT_READY',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_DISCOVERY=PCI_CONFIG_READ_ONLY',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_QUERY_BEFORE_INSTALL_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_NEGATIVE_INSTALLS_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_INSTALL_REPEAT_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_QUERY_MATCH_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_INVENTORY_INSTALLED',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_COUNT_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_UNIQUENESS_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_ARENA_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_LOOKUP_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_CLASS_QUERY_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_RESOURCE_DATA_UNAVAILABLE',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_RUNTIME_SURVIVAL_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_NEGATIVE_TESTS_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_TEARDOWN_OK',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_ACCOUNTING_RESTORED',
            'GXOS_NET10:MANAGED_KERNEL_DEVICE_OPERATIONAL_SURVIVAL_OK',
            'GXOS_NET10:MANAGED_KERNEL_PHASE6_PASS',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_CONTEXT_INITIALIZED=1',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_SERVICES_INSTALLED',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_BEFORE_START_REJECTED',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_NEGATIVE_TESTS_OK',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_ALLOC_OWNER=MANAGED_KERNEL',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_PATTERN_OK',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_RUNTIME_SURVIVAL_OK',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_MULTI_ALLOC_OK',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_RELEASE_OK',
            'GXOS_NET10:MANAGED_KERNEL_MEMORY_ACCOUNTING_RESTORED',
            'GXOS_NET10:MANAGED_KERNEL_PHASE4_PASS',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_CREATED',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_ALLOC_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_ALIGNMENT_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_REUSE_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_FRAGMENTATION_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_COALESCE_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_GROWTH_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_RUNTIME_SURVIVAL_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_NEGATIVE_TESTS_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_DESTROY_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_NATIVE_FIRST_BACKING_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_NATIVE_MANAGED_ONLY_UNCHANGED',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_NATIVE_GROWTH_OK',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_NATIVE_NEGATIVE_UNCHANGED',
            'GXOS_NET10:MANAGED_KERNEL_ARENA_NATIVE_ACCOUNTING_RESTORED',
            'GXOS_NET10:MANAGED_KERNEL_PHASE5_PASS')) {
            Require ($text.Contains($marker)) "ManagedKernel boot $sequence missing marker: $marker"
        }
        Require (([regex]::Matches($text, 'GXOS_NET10:NATIVEAOT_STARTUP_OK')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated NativeAOT startup."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_INIT_OK')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated managed initialization."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_BOOTSTRAP_OK')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated ManagedMain bootstrap."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_HOST_LOG_FROM_MANAGED')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated managed startup logging."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_START_OK')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated successful start."
        Require ($text.Contains('GXOS_NET10:MANAGED_KERNEL_HOST_LOG_CALLBACK_COUNT=0x0000000000000003')) `
            "ManagedKernel boot $sequence did not record three host log callbacks."
        Require ($text.Contains('GXOS_NET10:MANAGED_KERNEL_HOST_TIME_CALLBACK_COUNT=0x0000000000000002')) `
            "ManagedKernel boot $sequence did not record two host time callbacks."
        $phase5Start = $text.IndexOf('GXOS_NET10:MANAGED_KERNEL_ARENA_BASELINE_OWNER_CHUNKS')
        Require ($phase5Start -gt 0) "ManagedKernel boot $sequence omitted the Phase 5 baseline marker."
        $phase4Text = $text.Substring(0, $phase5Start)
        Require (([regex]::Matches($phase4Text, 'GXOS_NET10:MANAGED_KERNEL_MEMORY_ALLOC_OK')).Count -eq 2) `
            "ManagedKernel boot $sequence did not complete exactly two managed allocations."
        Require (([regex]::Matches($phase4Text, 'GXOS_NET10:MANAGED_KERNEL_MEMORY_ALLOC_OWNER=MANAGED_KERNEL')).Count -eq 2) `
            "ManagedKernel boot $sequence did not prove both allocations use the ManagedKernel owner."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_MEMORY_RELEASE_OK')).Count -eq 2) `
            "ManagedKernel boot $sequence did not record both managed and native release confirmations."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE4_PASS')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated or omitted the Phase 4 pass marker."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE5_PASS')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated or omitted the Phase 5 pass marker."
        Require (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE6_PASS')).Count -eq 1) `
            "ManagedKernel boot $sequence repeated or omitted the Phase 6 pass marker."
        Write-Output ("MANAGED_KERNEL_QEMU_RUN_{0}=PASS bytes={1} sha256={2} serial={3}" -f `
            $sequence, ([Text.Encoding]::UTF8.GetByteCount($text)),
            (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant(), $serial)
    }
} finally {
    foreach ($process in $owned) { Stop-OwnedQemu $process }
}
Require (@(Get-OwnedQemu).Count -eq 0) 'Owned QEMU cleanup failed.'
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_QEMU_RUNS=$RunCount"

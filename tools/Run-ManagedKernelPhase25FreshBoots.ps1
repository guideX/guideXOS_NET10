[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require25([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-FreeTcpPort25 {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally {
        $listener.Stop()
    }
}

function Connect-Serial25([int]$port, [System.Diagnostics.Process]$process,
                           [datetime]$deadline) {
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "QEMU exited before serial connection on port $port." }
        $client = [Net.Sockets.TcpClient]::new()
        try {
            $attempt = $client.ConnectAsync('127.0.0.1', $port)
            if ($attempt.Wait(500) -and $client.Connected) { return $client }
        } catch { }
        $client.Dispose()
        Start-Sleep -Milliseconds 50
    }
    throw "Timed out connecting to QEMU serial port $port."
}

function Stop-OwnedQemu25([System.Diagnostics.Process]$process) {
    if ($null -eq $process) { return }
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

function Wait-Phase25Serial([System.IO.Stream]$stream, [IO.FileStream]$log,
                             [Text.StringBuilder]$text,
                             [System.Diagnostics.Process]$process,
                             [datetime]$deadline, [byte[]]$buffer) {
    $readTask = $null
    while ((Get-Date) -lt $deadline) {
        if ($null -eq $readTask) {
            $readTask = $stream.ReadAsync($buffer, 0, $buffer.Length)
        }
        if (!$readTask.Wait(250)) { continue }
        $count = $readTask.Result
        $readTask = $null
        if ($count -le 0) { break }
        $log.Write($buffer, 0, $count)
        $chunk = [Text.Encoding]::ASCII.GetString($buffer, 0, $count)
        $text.Append($chunk) | Out-Null
        $transcript = $text.ToString()
        if ($transcript.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE25_PASS')) { return }
        if ($transcript.Contains('GXOS_NET10:FAIL:') -or
            $transcript.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
            $transcript.Contains('GXOS_NET10:PAGE_FAULT_') -or
            $transcript.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
            throw 'QEMU reported a fault during the Phase 25 proof.'
        }
        if ($process.HasExited) { throw 'QEMU exited before the Phase 25 pass marker.' }
    }
    throw 'Timed out waiting for the Phase 25 pass marker.'
}

Require25 ($RunCount -ge 3) 'Three fresh Phase 25 boots are required.'
$expectedHash = $PayloadSha256.ToUpperInvariant()
Require25 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 is invalid.'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll'
Require25 ((Test-Path -LiteralPath $efi) -and (Test-Path -LiteralPath $payload)) `
    'Phase 25 EFI or payload is missing.'
Require25 ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) `
    'Phase 25 payload size changed.'
Require25 ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
    'Phase 25 staged payload hash does not match the requested identity.'
Require25 (!(Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } `
    else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
Require25 (Test-Path -LiteralPath $qemu) 'qemu-system-x86_64.exe is required.'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
Require25 ((Test-Path -LiteralPath $ovmf) -and (Test-Path -LiteralPath $varsTemplate)) `
    'OVMF firmware is required.'
try {
    $ownedQemu = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
        Where-Object { ([string]$_.CommandLine).IndexOf($gate,
            [StringComparison]::OrdinalIgnoreCase) -ge 0 })
} catch {
    $ownedQemu = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue)
}
Require25 ($ownedQemu.Count -eq 0) `
    'An owned Phase 25 QEMU process is already running.'

New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null
(& $qemu --version 2>&1) | Set-Content -LiteralPath (Join-Path $evidence 'qemu-version.log') -Encoding ascii

$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE3_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE25_BEGIN',
    'GXOS_NET10:MANAGED_KERNEL_ENTROPY_SERVICES_INSTALLED',
    'GXOS_NET10:MANAGED_ENTROPY_CPUID_MAX_BASIC=',
    'GXOS_NET10:MANAGED_ENTROPY_FEATURE_FLAGS=',
    'GXOS_NET10:MANAGED_CRYPTO_PHASE25_INITIALIZED',
    'GXOS_NET10:MANAGED_SHA256_KAT_PASS',
    'GXOS_NET10:MANAGED_HMAC_SHA256_KAT_PASS',
    'GXOS_NET10:MANAGED_CRYPTO_CONSTANT_TIME_COMPARISON_PASS',
    'GXOS_NET10:MANAGED_CRYPTO_GC_SURVIVAL_PASS',
    'GXOS_NET10:MANAGED_CRYPTO_RESET_TEARDOWN_COMPLETE',
    'GXOS_NET10:MANAGED_KERNEL_PHASE25_PASS')

for ($sequence = 1; $sequence -le $RunCount; ++$sequence) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $sequence)
    New-Item -ItemType Directory -Force -Path $run | Out-Null
    $code = Join-Path $run 'edk2-code.fd'
    $vars = Join-Path $run 'edk2-vars.fd'
    $serial = Join-Path $run 'serial.log'
    $commandLinePath = Join-Path $run 'qemu-commandline.log'
    $firmwareIdentityPath = Join-Path $run 'firmware-identity.log'
    $stdout = Join-Path $run 'qemu.stdout.log'
    $stderr = Join-Path $run 'qemu.stderr.log'
    Copy-Item -LiteralPath $ovmf -Destination $code
    Copy-Item -LiteralPath $varsTemplate -Destination $vars
    $codeHash = (Get-FileHash -LiteralPath $code -Algorithm SHA256).Hash.ToUpperInvariant()
    $varsHash = (Get-FileHash -LiteralPath $vars -Algorithm SHA256).Hash.ToUpperInvariant()
    Set-Content -LiteralPath $firmwareIdentityPath -Value @(
        "qemu=$qemu", "ovmf_code_sha256=$codeHash", "ovmf_vars_sha256=$varsHash") -Encoding ascii
    $serialPort = Get-FreeTcpPort25
    $arguments = @(
        '-machine', 'q35', '-accel', 'tcg,thread=single', '-m', '128M',
        '-drive', "if=pflash,format=raw,readonly=on,file=$code",
        '-drive', "if=pflash,format=raw,file=$vars",
        '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
        '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
        '-chardev', "socket,id=serial0,host=127.0.0.1,port=$serialPort,server=on,wait=on,telnet=off,ipv4=on,nodelay=on",
        '-serial', 'none', '-device', 'isa-serial,chardev=serial0,iobase=0x3f8,irq=4,wakeup=on',
        '-display', 'none', '-no-reboot', '-no-shutdown')
    Set-Content -LiteralPath $commandLinePath -Value ('"{0}" {1}' -f $qemu, ($arguments -join ' ')) -Encoding ascii

    $process = $null; $client = $null; $stream = $null; $log = $null
    try {
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
        $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
        $client = Connect-Serial25 $serialPort $process $deadline
        $stream = $client.GetStream()
        $log = [IO.File]::Open($serial, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $text = [Text.StringBuilder]::new()
        Wait-Phase25Serial $stream $log $text $process $deadline (New-Object byte[] 4096)
    } finally {
        if ($null -ne $log) { $log.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
        if ($null -ne $client) { $client.Dispose() }
        Stop-OwnedQemu25 $process
    }

    $transcript = [IO.File]::ReadAllText($serial)
    foreach ($marker in $required) {
        Require25 $transcript.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require25 (([regex]::Matches($transcript,
        'GXOS_NET10:MANAGED_KERNEL_PHASE25_PASS')).Count -eq 1) `
        "Boot $sequence did not report exactly one Phase 25 pass."
    $hardware = $transcript.Contains('GXOS_NET10:MANAGED_ENTROPY_PROVIDER_CAPABILITY_DETECTED=1')
    $unavailable = $transcript.Contains('GXOS_NET10:MANAGED_ENTROPY_PROVIDER_UNAVAILABLE=1')
    Require25 ($hardware -xor $unavailable) `
        "Boot $sequence did not report exactly one entropy capability outcome."
    if ($hardware) {
        foreach ($marker in @(
            'GXOS_NET10:MANAGED_SECURE_RANDOM_CAPABILITIES=',
            'GXOS_NET10:MANAGED_SECURE_RANDOM_FILL_PASS',
            'GXOS_NET10:MANAGED_SECURE_RANDOM_REPEATED_FILL_PASS')) {
            Require25 $transcript.Contains($marker) "Boot $sequence missing hardware marker: $marker"
        }
        $outcome = 'HARDWARE'
    } else {
        Require25 $transcript.Contains(
            'GXOS_NET10:MANAGED_SECURE_RANDOM_UNAVAILABLE_FAIL_CLOSED_PASS') `
            "Boot $sequence did not prove entropy failure is fail-closed."
        $outcome = 'ENTROPY_UNAVAILABLE'
    }
    Require25 (!$transcript.Contains('GXOS_NET10:FAIL:') -and
        !$transcript.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
        !$transcript.Contains('GXOS_NET10:PAGE_FAULT_') -and
        !$transcript.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
        "Boot $sequence reported a fault."
    $serialHash = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant()
    Write-Output ('MANAGED_KERNEL_PHASE25_BOOT_{0}=PASS outcome={1} serial_sha256={2} serial={3}' -f
        $sequence, $outcome, $serialHash, $serial)
}

$owned = @()
try {
    $owned = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
        Where-Object {
            ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            ([string]$_.CommandLine).IndexOf($evidence, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
} catch {
    $owned = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue)
}
Require25 ($owned.Count -eq 0) 'An owned QEMU process remains after Phase 25 boots.'
Write-Output "MANAGED_KERNEL_PHASE25_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE25_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE25_QEMU_RUNS=$RunCount"

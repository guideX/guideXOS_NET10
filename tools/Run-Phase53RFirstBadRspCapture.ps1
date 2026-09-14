[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [Parameter(Mandatory = $true)] [string]$EfiSha256,
    [Parameter(Mandatory = $true)] [long]$EfiSize,
    [Parameter(Mandatory = $true)] [string]$PdbPath,
    [long]$ImageBase = 0x4EAC000,
    [int]$TimeoutSeconds = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll'
$run = Join-Path $evidence 'run-1'
$qemu = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
$gdb = 'C:\mingw64\bin\gdb.exe'
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmfCode = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$ovmfVars = Join-Path $qemuShare 'edk2-i386-vars.fd'

function Require-R([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Require-R (Test-Path -LiteralPath $efi) 'EFI artifact is missing.'
Require-R (Test-Path -LiteralPath $payload) 'Managed payload is missing.'
Require-R (Test-Path -LiteralPath $PdbPath) 'Matching PDB is missing.'
Require-R (Test-Path -LiteralPath $qemu) 'QEMU is missing.'
Require-R (Test-Path -LiteralPath $gdb) 'GDB is missing.'
Require-R (Test-Path -LiteralPath $ovmfCode) 'OVMF code is missing.'
Require-R (Test-Path -LiteralPath $ovmfVars) 'OVMF vars template is missing.'
Require-R ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'Payload size mismatch.'
Require-R ((Get-Item -LiteralPath $efi).Length -eq $EfiSize) 'EFI size mismatch.'
Require-R ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $PayloadSha256.ToUpperInvariant()) 'Payload hash mismatch.'
Require-R ((Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash.ToUpperInvariant() -eq $EfiSha256.ToUpperInvariant()) 'EFI hash mismatch.'
Require-R (-not (Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"
New-Item -ItemType Directory -Force -Path $run | Out-Null

$faultRva = [UInt64]0x147B80
$faultAddress = $ImageBase + $faultRva
$workerVector = [UInt64]0x4CFD000
$workerGs = [UInt64]0x4CFE000
$workerTeb = [UInt64]0x4CFB000
$workerStackLow = [UInt64]0x4CFF000
$workerStackHigh = [UInt64]0x4D03000
$workerContext = [UInt64]0x1A9A30
$schedulerEntry = [UInt64]0x167080
$schedulerRspConsumer = [UInt64]0x16722E

function Get-FreeTcpPort-R {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally { $listener.Stop() }
}

function Connect-R([int]$port, [datetime]$deadline, [Diagnostics.Process]$process) {
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { throw "QEMU exited before port $port connected." }
        $client = [Net.Sockets.TcpClient]::new()
        try {
            $attempt = $client.ConnectAsync('127.0.0.1', $port)
            if ($attempt.Wait(500) -and $client.Connected) { return $client }
        } catch { }
        $client.Dispose()
        Start-Sleep -Milliseconds 50
    }
    throw "Timed out connecting to QEMU port $port."
}

function Read-R([Net.Sockets.NetworkStream]$stream, [IO.FileStream]$file,
                [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ($stream.DataAvailable) {
        $count = $stream.Read($buffer, 0, $buffer.Length)
        if ($count -le 0) { return }
        $file.Write($buffer, 0, $count)
        $file.Flush()
        $text.Append([Text.Encoding]::ASCII.GetString($buffer, 0, $count)) | Out-Null
    }
}

function Get-GdbText-R([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try { return [IO.File]::ReadAllText($path) } catch [IO.IOException] { return '' }
}

function Wait-R([string]$marker, [datetime]$deadline,
                [Diagnostics.Process]$process, [Net.Sockets.NetworkStream]$stream,
                [IO.FileStream]$file, [Text.StringBuilder]$text, [byte[]]$buffer,
                [string]$gdbPath) {
    while ((Get-Date) -lt $deadline) {
        Read-R $stream $file $text $buffer
        $gdbText = Get-GdbText-R $gdbPath
        if ($gdbText.Contains('PHASE53R_FIRST_BAD_RSP') -or
            $gdbText.Contains('PHASE53R_FIRST_BAD_RSP_AT_ENTRY') -or
            $gdbText.Contains('PHASE53R_FAULT_BREAKPOINT')) {
            throw 'Phase53R diagnostic boundary captured by GDB.'
        }
        if ($text.ToString().Contains($marker)) { return }
        if ($process.HasExited) { throw "QEMU exited while waiting for $marker." }
        if ($text.ToString().Contains('GXOS_NET10:FAIL:') -or
            $text.ToString().Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) {
            throw "Guest reported a non-target failure while waiting for $marker."
        }
        Start-Sleep -Milliseconds 25
    }
    throw "Timed out waiting for $marker."
}

function Send-Serial-R([Net.Sockets.NetworkStream]$stream, [byte]$value,
                       [IO.StreamWriter]$log, [string]$after) {
    $stream.WriteByte($value)
    $stream.Flush()
    $log.WriteLine(('after={0} serial_byte=0x{1:X2}' -f $after, $value))
    $log.Flush()
}

function Send-Key-R([Net.Sockets.NetworkStream]$stream, [string]$key,
                    [IO.StreamWriter]$log, [string]$after) {
    $bytes = [Text.Encoding]::ASCII.GetBytes("sendkey $key`n")
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $log.WriteLine(('after={0} monitor_command=sendkey {1}' -f $after, $key))
    $log.Flush()
}

function Write-Inventory-R([string]$path, [string]$phase) {
    $items = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe' OR Name = 'gdb.exe'")
    Add-Content -LiteralPath $path -Value ("phase={0} count={1}" -f $phase, $items.Count) -Encoding ascii
    foreach ($item in $items) {
        Add-Content -LiteralPath $path -Value ("pid={0} name={1} command_line={2}" -f $item.ProcessId, $item.Name, $item.CommandLine) -Encoding ascii
    }
}

$codeCopy = Join-Path $run 'edk2-code.fd'
$varsCopy = Join-Path $run 'edk2-vars.fd'
$serialPath = Join-Path $run 'serial.log'
$gdbCommands = Join-Path $run 'gdb-commands.txt'
$gdbStdout = Join-Path $run 'gdb.stdout.log'
$gdbStderr = Join-Path $run 'gdb.stderr.log'
$injectionPath = Join-Path $run 'injections.log'
$manifestPath = Join-Path $run 'capture-manifest.txt'
$inventoryPath = Join-Path $run 'diagnostic-process-inventory.log'
$serialPort = Get-FreeTcpPort-R
$monitorPort = Get-FreeTcpPort-R
$gdbPort = Get-FreeTcpPort-R

Copy-Item -LiteralPath $ovmfCode -Destination $codeCopy
Copy-Item -LiteralPath $ovmfVars -Destination $varsCopy
Write-Inventory-R $inventoryPath 'BEFORE'

$gdbLines = [Collections.Generic.List[string]]::new()
[void]$gdbLines.Add('set pagination off')
[void]$gdbLines.Add('set confirm off')
[void]$gdbLines.Add('set remotetimeout 30')
[void]$gdbLines.Add(('target remote 127.0.0.1:{0}' -f $gdbPort))

# Keep the known downstream failure as a stop condition, but make the RSP
# transition trace the primary diagnostic boundary.
[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $faultAddress))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $rcx != 0x4CFC030 && $rcx != 0x4EAA030')
[void]$gdbLines.Add('printf "PHASE53R_FAULT_BREAKPOINT\n"')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('info registers fs_base gs_base')
[void]$gdbLines.Add('printf "PHASE53R_FAULT_RSP="')
[void]$gdbLines.Add('p/x $rsp')
[void]$gdbLines.Add('printf "PHASE53R_FAULT_RIP="')
[void]$gdbLines.Add('p/x $rip')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('printf "PHASE53R_WORKER_GS_30="')
[void]$gdbLines.Add('x/gx $gs_base+0x30')
[void]$gdbLines.Add('printf "PHASE53R_WORKER_GS_58="')
[void]$gdbLines.Add('x/gx $gs_base+0x58')
[void]$gdbLines.Add('printf "PHASE53R_WORKER_VECTOR="')
[void]$gdbLines.Add(('x/8gx 0x{0:X}' -f $workerVector))
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# Observe the scheduler's input and the exact instruction which consumes the
# saved RSP.  Existing Q evidence showed this context valid; retain the
# observation in every R capture to make that conclusion reproducible.
[void]$gdbLines.Add(('break *0x{0:X}' -f $schedulerEntry))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('set $phase53r_ctx_old = *(unsigned long long*)$rcx')
[void]$gdbLines.Add('set $phase53r_ctx_new = $rdx')
[void]$gdbLines.Add('set $phase53r_ctx_rsp = *(unsigned long long*)($phase53r_ctx_new+0x40)')
[void]$gdbLines.Add('set $phase53r_ctx_rip = *(unsigned long long*)($phase53r_ctx_new+0x48)')
[void]$gdbLines.Add('set $phase53r_ctx_gs = *(unsigned long long*)($phase53r_ctx_new+0x60)')
[void]$gdbLines.Add('printf "PHASE53R_CONTEXT_SWITCH oldctx=%p newctx=%p newrsp=%p newrip=%p newgs=%p\n", $phase53r_ctx_old, $phase53r_ctx_new, $phase53r_ctx_rsp, $phase53r_ctx_rip, $phase53r_ctx_gs')
[void]$gdbLines.Add('x/16gx $phase53r_ctx_new')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $schedulerRspConsumer))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53R_SCHEDULER_RSP_CONSUMER rip=%p rsp_before=%p new_rsp_operand=%p\n", $rip, $rsp, $r10')
[void]$gdbLines.Add('x/6i $rip-8')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# First narrow boundary: true entry to the OVMF caller containing the indirect
# callback, before its push %rbp prologue. This breakpoint never single-steps;
# it only stops the run if the caller is entered with RSP already in the worker
# GS page.
[void]$gdbLines.Add('hbreak *0x6B0D67C')
[void]$gdbLines.Add('disable 4')
[void]$gdbLines.Add('commands 4')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('printf "PHASE53R_FIRST_BAD_RSP OVMF_CALLER_ENTRY_6B0D67C\n"')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('x/64i 0x6B0D680')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add('hbreak *0x7E6E6BC')
[void]$gdbLines.Add('disable 5')
[void]$gdbLines.Add('commands 5')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('printf "PHASE53R_FIRST_BAD_RSP OVMF_EVENT_CALL_6BC\n"')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('x/64i 0x6B0D680')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('else')
[void]$gdbLines.Add('printf "PHASE53R_LAST_GOOD_RSP OVMF_EVENT_CALL_6BC rip=%p rsp=%p rbp=%p return=" , $rip, $rsp, $rbp')
[void]$gdbLines.Add('x/gx $rsp')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add('hbreak *0x6B0D6B9')
[void]$gdbLines.Add('disable 6')
[void]$gdbLines.Add('commands 6')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('printf "PHASE53R_FIRST_BAD_RSP OVMF_INDIRECT_CALL_6B0D6B9\n"')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add('set $phase53r_armed = 0')
[void]$gdbLines.Add('break *0x167B40')
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $phase53r_armed == 0')
[void]$gdbLines.Add('set $phase53r_armed = 1')
[void]$gdbLines.Add('enable 4')
[void]$gdbLines.Add('enable 5')
[void]$gdbLines.Add('enable 6')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add('echo PHASE53R_GDB_READY\\n')
[void]$gdbLines.Add('continue')
[IO.File]::WriteAllLines($gdbCommands, $gdbLines, [Text.Encoding]::ASCII)

$arguments = @(
    '-machine', 'q35', '-accel', 'tcg,thread=single', '-m', '128M',
    '-drive', "if=pflash,format=raw,readonly=on,file=$codeCopy",
    '-drive', "if=pflash,format=raw,file=$varsCopy",
    '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
    '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
    '-chardev', "socket,id=serial0,host=127.0.0.1,port=$serialPort,server=on,wait=on,telnet=off,ipv4=on,nodelay=on",
    '-serial', 'none', '-device', 'isa-serial,chardev=serial0,iobase=0x3f8,irq=4,wakeup=on',
    '-monitor', "tcp:127.0.0.1:$monitorPort,server=on,wait=on",
    '-display', 'none', '-no-reboot', '-no-shutdown',
    '-gdb', "tcp:127.0.0.1:$gdbPort", '-S')

$gitHead = (& git -C $root rev-parse HEAD).Trim()
$manifestLines = @(
    "repository=$root", "git_head=$gitHead", "gate=$gate", "payload=$payload",
    "payload_size=$PayloadSize", "payload_sha256=$PayloadSha256", "efi=$efi",
    "efi_size=$EfiSize", "efi_sha256=$EfiSha256", "pdb=$PdbPath",
    "pdb_size=$((Get-Item -LiteralPath $PdbPath).Length)",
    "pdb_sha256=$((Get-FileHash -LiteralPath $PdbPath -Algorithm SHA256).Hash.ToUpperInvariant())",
    "ovmf_code=$ovmfCode", "ovmf_code_size=$((Get-Item -LiteralPath $ovmfCode).Length)",
    "ovmf_code_sha256=$((Get-FileHash -LiteralPath $ovmfCode -Algorithm SHA256).Hash.ToUpperInvariant())",
    "ovmf_vars=$ovmfVars", "ovmf_vars_size=$((Get-Item -LiteralPath $ovmfVars).Length)",
    "ovmf_vars_sha256=$((Get-FileHash -LiteralPath $ovmfVars -Algorithm SHA256).Hash.ToUpperInvariant())",
    "image_base=0x$('{0:X}' -f $ImageBase)", "fault_rva=0x147B80", "fault_address=0x$('{0:X}' -f $faultAddress)",
    "scheduler_entry=0x167080", "scheduler_rsp_consumer=0x16722E",
    "ovmf_event_call_site=0x7E6E6BC", "ovmf_event_entry=0x7E6E25E",
    "ovmf_caller_return=0x6B0D6BC", "ovmf_caller_call_site=0x6B0D6B9",
    "ovmf_caller_entry=0x6B0D67C",
    "worker_gs=0x4CFE000", "worker_teb=0x4CFB000", "worker_tls_vector=0x4CFD000",
    "worker_stack_low=0x4CFF000", "worker_stack_high=0x4D03000", "worker_context=0x1A9A30",
    "serial_port=$serialPort", "monitor_port=$monitorPort", "gdb_port=$gdbPort",
    "target_result=STARTED"
)
[IO.File]::WriteAllText($manifestPath, ($manifestLines -join [Environment]::NewLine), [Text.Encoding]::ASCII)
[IO.File]::AppendAllText($manifestPath, [Environment]::NewLine, [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $run 'qemu-commandline.log'), ('"{0}" {1}' -f $qemu, ($arguments -join ' ')), [Text.Encoding]::ASCII)

$process = $null; $gdbProcess = $null; $serialClient = $null; $monitorClient = $null
$serialStream = $null; $monitorStream = $null; $serialFile = $null; $injectionLog = $null
$text = [Text.StringBuilder]::new(); $buffer = New-Object byte[] 4096
$targetResult = $null
try {
    $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate `
        -RedirectStandardOutput (Join-Path $run 'qemu.stdout.log') `
        -RedirectStandardError (Join-Path $run 'qemu.stderr.log') -PassThru -WindowStyle Hidden
    Add-Content -LiteralPath $manifestPath -Value "qemu_pid=$($process.Id)" -Encoding ascii
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $gdbProcess = Start-Process -FilePath $gdb -ArgumentList @('--nx', '--batch', '-x', $gdbCommands) `
        -RedirectStandardOutput $gdbStdout -RedirectStandardError $gdbStderr -PassThru -WindowStyle Hidden
    Add-Content -LiteralPath $manifestPath -Value "gdb_pid=$($gdbProcess.Id)" -Encoding ascii
    $serialClient = Connect-R $serialPort $deadline $process
    $monitorClient = Connect-R $monitorPort $deadline $process
    $serialStream = $serialClient.GetStream(); $monitorStream = $monitorClient.GetStream()
    $serialFile = [IO.File]::Open($serialPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $injectionLog = [IO.StreamWriter]::new($injectionPath, $false, [Text.Encoding]::ASCII)
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-R $serialStream 0x52 $injectionLog 'SERIAL_READY'
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-R $serialStream 0x53 $injectionLog 'SERIAL_SECOND_READY'
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-R $monitorStream 'a' $injectionLog 'KEYBOARD_INPUT_READY'
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_EVENT_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_RUNTIME_SURVIVAL_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-R $monitorStream 'b' $injectionLog 'KEYBOARD_SECOND_INPUT_READY'
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-R $monitorStream 'c' $injectionLog 'KEYBOARD_UNSUBSCRIBED_READY'
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-R $serialStream 0x44 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-R $serialStream 0x45 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-R $serialStream 0x46 $injectionLog 'KEYBOARD_B_SENT'
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_DRAINED' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-R 'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    $targetResult = 'PASS_WITHOUT_FAULT'
} catch {
    $gdbText = Get-GdbText-R $gdbStdout
    $badMarker = ($gdbText -split "`r?`n" | Where-Object { $_ -like 'PHASE53R_FIRST_BAD_RSP *' } | Select-Object -First 1)
    if ($badMarker -match 'OVMF_CALLER_ENTRY_6B0D67C') {
        $targetResult = 'FIRST_BAD_RSP_AT_CALLER_ENTRY'
    } elseif ($badMarker -match 'OVMF_EVENT_CALL_6BC') {
        $targetResult = 'FIRST_BAD_RSP_AT_EVENT_CALL'
    } elseif ($badMarker -match 'OVMF_INDIRECT_CALL_6B0D6B9') {
        $targetResult = 'FIRST_BAD_RSP_AT_INDIRECT_CALL'
    } elseif ($badMarker -match 'PHASE53R_FIRST_BAD_RSP_AT_ENTRY') {
        $targetResult = 'FIRST_BAD_RSP_AT_EVENT_ENTRY'
    } elseif ($badMarker -match 'PHASE53R_FIRST_BAD_RSP') {
        $targetResult = 'FIRST_BAD_RSP_CAPTURED'
    } elseif ($gdbText.Contains('PHASE53R_FAULT_BREAKPOINT')) {
        $targetResult = 'DOWNSTREAM_FAULT_CAPTURED'
    } else {
        Add-Content -LiteralPath $manifestPath -Value ("capture_exception={0}" -f $_.Exception.Message) -Encoding ascii
        throw
    }
} finally {
    if ($null -ne $serialStream) { try { Read-R $serialStream $serialFile $text $buffer } catch { } }
    if ($null -ne $serialFile) { $serialFile.Dispose() }
    if ($null -ne $injectionLog) { $injectionLog.Dispose() }
    if ($null -ne $monitorClient) { $monitorClient.Dispose() }
    if ($null -ne $serialClient) { $serialClient.Dispose() }
    if ($null -ne $gdbProcess) {
        try {
            $gdbProcess.WaitForExit(5000) | Out-Null
            if (-not $gdbProcess.HasExited) { Stop-Process -Id $gdbProcess.Id -Force -ErrorAction SilentlyContinue }
        } catch { }
    }
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
            $process.WaitForExit(5000)
        } catch { }
    }
    if ($null -ne $targetResult) { Add-Content -LiteralPath $manifestPath -Value "target_result=$targetResult" -Encoding ascii }
    Write-Inventory-R $inventoryPath 'AFTER'
}

if ($targetResult -eq 'FIRST_BAD_RSP_CAPTURED' -or
    $targetResult -eq 'FIRST_BAD_RSP_AT_CALLER_ENTRY' -or
    $targetResult -eq 'FIRST_BAD_RSP_AT_EVENT_CALL' -or
    $targetResult -eq 'FIRST_BAD_RSP_AT_INDIRECT_CALL' -or
    $targetResult -eq 'FIRST_BAD_RSP_AT_EVENT_ENTRY' -or
    $targetResult -eq 'DOWNSTREAM_FAULT_CAPTURED') { return }

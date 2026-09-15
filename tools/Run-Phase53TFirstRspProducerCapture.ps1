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
$qemu = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
$gdb = 'C:\mingw64\bin\gdb.exe'
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmfCode = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$ovmfVars = Join-Path $qemuShare 'edk2-i386-vars.fd'
$run = Join-Path $evidence 'run-1'

function Require-T([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Require-T (Test-Path -LiteralPath $efi) 'EFI artifact is missing.'
Require-T (Test-Path -LiteralPath $payload) 'Managed payload is missing.'
Require-T (Test-Path -LiteralPath $PdbPath) 'Matching PDB is missing.'
Require-T (Test-Path -LiteralPath $qemu) 'QEMU is missing.'
Require-T (Test-Path -LiteralPath $gdb) 'GDB is missing.'
Require-T (Test-Path -LiteralPath $ovmfCode) 'OVMF code is missing.'
Require-T (Test-Path -LiteralPath $ovmfVars) 'OVMF vars template is missing.'
Require-T ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'Payload size mismatch.'
Require-T ((Get-Item -LiteralPath $efi).Length -eq $EfiSize) 'EFI size mismatch.'
Require-T ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $PayloadSha256.ToUpperInvariant()) 'Payload hash mismatch.'
Require-T ((Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash.ToUpperInvariant() -eq $EfiSha256.ToUpperInvariant()) 'EFI hash mismatch.'
Require-T (-not (Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"
New-Item -ItemType Directory -Force -Path $run | Out-Null

$workerGs = [UInt64]0x4CFE000
$workerStackLow = [UInt64]0x4CFF000
$workerStackHigh = [UInt64]0x4D03000
$schedulerEntry = [UInt64]0x167080
$schedulerRspConsumer = [UInt64]0x16722E
$workerEntry = [UInt64]0x167B40
$eventCallSite = [UInt64]0x7E6E6BC
$largeFrameEntry = [UInt64]0x501B330
$largeFrameSizeLoad = [UInt64]0x501B350
$largeFrameRspSubtract = [UInt64]0x501B35A
$largeFrameRspAfterSubtract = [UInt64]0x501B35D
$stackProbeGsRead = [UInt64]0x503865C
$faultAddress = $ImageBase + [UInt64]0x147B80

function Get-FreeTcpPort-T {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Connect-T([int]$port, [datetime]$deadline, [Diagnostics.Process]$process) {
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

function Read-T([Net.Sockets.NetworkStream]$stream, [IO.FileStream]$file,
                [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ($stream.DataAvailable) {
        $count = $stream.Read($buffer, 0, $buffer.Length)
        if ($count -le 0) { return }
        $file.Write($buffer, 0, $count)
        $file.Flush()
        $text.Append([Text.Encoding]::ASCII.GetString($buffer, 0, $count)) | Out-Null
    }
}

function GdbText-T([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try { return [IO.File]::ReadAllText($path) } catch [IO.IOException] { return '' }
}

function Wait-T([string]$marker, [datetime]$deadline,
                [Diagnostics.Process]$process, [Net.Sockets.NetworkStream]$stream,
                [IO.FileStream]$file, [Text.StringBuilder]$text, [byte[]]$buffer,
                [string]$gdbPath) {
    while ((Get-Date) -lt $deadline) {
        Read-T $stream $file $text $buffer
        $gdbText = GdbText-T $gdbPath
        if ($gdbText.Contains('PHASE53T_FIRST_RSP_PIVOT') -or
            $gdbText.Contains('PHASE53T_DOWNSTREAM_FAULT')) { return }
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

function Send-Serial-T([Net.Sockets.NetworkStream]$stream, [byte]$value,
                       [IO.StreamWriter]$log, [string]$after) {
    $stream.WriteByte($value)
    $stream.Flush()
    $log.WriteLine(('after={0} serial_byte=0x{1:X2}' -f $after, $value))
    $log.Flush()
}

function Send-Key-T([Net.Sockets.NetworkStream]$stream, [string]$key,
                    [IO.StreamWriter]$log, [string]$after) {
    $bytes = [Text.Encoding]::ASCII.GetBytes(("sendkey {0}{1}" -f $key, [Environment]::NewLine))
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $log.WriteLine(('after={0} monitor_command=sendkey {1}' -f $after, $key))
    $log.Flush()
}

function Inventory-T([string]$path, [string]$phase) {
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
$serialPort = Get-FreeTcpPort-T
$monitorPort = Get-FreeTcpPort-T
$gdbPort = Get-FreeTcpPort-T

Copy-Item -LiteralPath $ovmfCode -Destination $codeCopy
Copy-Item -LiteralPath $ovmfVars -Destination $varsCopy
Inventory-T $inventoryPath 'BEFORE'

$gdbLines = [Collections.Generic.List[string]]::new()
[void]$gdbLines.Add('set pagination off')
[void]$gdbLines.Add('set confirm off')
[void]$gdbLines.Add('set remotetimeout 45')
[void]$gdbLines.Add(('target remote 127.0.0.1:{0}' -f $gdbPort))
[void]$gdbLines.Add('set $phase53t_worker_seen = 0')
[void]$gdbLines.Add('set $phase53t_pivot_seen = 0')
[void]$gdbLines.Add('set $phase53t_sub_pre_seen = 0')
[void]$gdbLines.Add('set $phase53t_active_gs = 0')
[void]$gdbLines.Add('set $phase53t_active_stack_low = 0')
[void]$gdbLines.Add('set $phase53t_active_stack_high = 0')

[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $faultAddress))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $rcx == 0x4CFC030 || $rcx == 0x4EAA030')
[void]$gdbLines.Add('printf "PHASE53T_DOWNSTREAM_FAULT_IGNORED rip=%p rsp=%p rcx=%p gs=%p\n", $rip, $rsp, $rcx, $gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('else')
[void]$gdbLines.Add('printf "PHASE53T_DOWNSTREAM_FAULT rip=%p rsp=%p gs=%p\n", $rip, $rsp, $gs_base')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('info registers fs_base gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $schedulerEntry))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53T_CONTEXT newctx=%p newrsp=%p newrip=%p newgs=%p\n", $rdx, *(unsigned long long*)($rdx+0x40), *(unsigned long long*)($rdx+0x48), *(unsigned long long*)($rdx+0x60)')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $schedulerRspConsumer))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53T_SCHEDULER_CONSUMER rip=%p rsp_before=%p source=%p gs=%p\n", $rip, $rsp, $r10, $gs_base')
[void]$gdbLines.Add('x/6i $rip-8')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $eventCallSite))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base != 0')
[void]$gdbLines.Add('if $rsp >= $phase53t_active_stack_low && $rsp < $phase53t_active_stack_high')
[void]$gdbLines.Add('printf "PHASE53T_EVENT_VALID rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $workerEntry))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base != 0')
[void]$gdbLines.Add('set $phase53t_worker_seen = 1')
[void]$gdbLines.Add('set $phase53t_active_gs = $gs_base')
[void]$gdbLines.Add('set $phase53t_active_stack_low = $gs_base + 0x1000')
[void]$gdbLines.Add('set $phase53t_active_stack_high = $gs_base + 0x5000')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('printf "PHASE53T_WORKER_ENTRY rip=%p rsp=%p gs=%p\n", $rip, $rsp, $gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $largeFrameEntry))
[void]$gdbLines.Add('condition 6 $phase53t_worker_seen == 1 && $gs_base == $phase53t_active_gs && $rsp >= $phase53t_active_stack_low && $rsp < $phase53t_active_stack_high')
[void]$gdbLines.Add('commands 6')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53T_LARGE_FRAME_ENTRY rip=%p rsp_entry=%p gs=%p\n", $rip, $rsp, $gs_base')
[void]$gdbLines.Add('x/12i $rip')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $largeFrameSizeLoad))
[void]$gdbLines.Add('condition 7 $phase53t_worker_seen == 1 && $gs_base == $phase53t_active_gs')
[void]$gdbLines.Add('commands 7')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53T_FRAME_SIZE_SOURCE rip=%p rsp=%p rax_before_mov=%p gs=%p\n", $rip, $rsp, $rax, $gs_base')
[void]$gdbLines.Add('x/5i $rip')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $stackProbeGsRead))
[void]$gdbLines.Add('condition 8 $phase53t_worker_seen == 1 && $gs_base == $phase53t_active_gs && $rax == 0x41B0')
[void]$gdbLines.Add('commands 8')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53T_STACK_PROBE_GS_READ_PRE rip=%p rsp=%p rax=%p gs=%p effective=%p value=%p\n", $rip, $rsp, $rax, $gs_base, $gs_base+0x10, *(unsigned long long*)($gs_base+0x10)')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $largeFrameRspSubtract))
[void]$gdbLines.Add('condition 9 $phase53t_worker_seen == 1 && $gs_base == $phase53t_active_gs')
[void]$gdbLines.Add('commands 9')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('set $phase53t_old_rsp = $rsp')
[void]$gdbLines.Add('set $phase53t_frame_size = $rax')
[void]$gdbLines.Add('printf "PHASE53T_RSP_WRITER_PRE rip=%p rsp_old=%p rax_source=%p gs=%p\n", $rip, $rsp, $rax, $gs_base')
[void]$gdbLines.Add('x/8bx $rip')
[void]$gdbLines.Add('x/8i $rip-0x20')
[void]$gdbLines.Add('set $phase53t_sub_pre_seen = 1')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $largeFrameRspAfterSubtract))
[void]$gdbLines.Add('condition 10 $phase53t_sub_pre_seen == 1 && $phase53t_worker_seen == 1 && $gs_base == $phase53t_active_gs')
[void]$gdbLines.Add('commands 10')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53T_RSP_WRITER_POST rip=%p rsp_new=%p delta=%p rax_source=%p gs=%p\n", $rip, $rsp, $phase53t_old_rsp-$rsp, $phase53t_frame_size, $gs_base')
[void]$gdbLines.Add('if $rsp >= $phase53t_active_gs && $rsp < $phase53t_active_gs + 0x1000')
[void]$gdbLines.Add('set $phase53t_pivot_seen = 1')
[void]$gdbLines.Add('printf "PHASE53T_FIRST_RSP_PIVOT writer=0x501B35A mnemonic=sub_rsp_rax old_rsp=%p new_rsp=%p source_rax=%p gs_base=%p gs_plus_10=%p\n", $phase53t_old_rsp, $rsp, $phase53t_frame_size, $gs_base, $gs_base+0x10')
[void]$gdbLines.Add('info registers cr3 eflags rsp rip gs_base')
[void]$gdbLines.Add('x/32gx $rsp-0x20')
[void]$gdbLines.Add('x/20i $rip')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add('echo PHASE53T_GDB_READY\n')
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
    "image_base=0x$('{0:X}' -f $ImageBase)", "fault_address=0x$('{0:X}' -f $faultAddress)",
    "scheduler_rsp_consumer=0x16722E", "worker_entry=0x167B40", "event_call_site=0x7E6E6BC",
    "large_frame_entry=0x501B330", "large_frame_size_load=0x501B350",
    "large_frame_rsp_subtract=0x501B35A", "large_frame_rsp_after_subtract=0x501B35D", "stack_probe_gs_read=0x503865C",
    "worker_gs=0x4CFE000", "worker_stack_low=0x4CFF000", "worker_stack_high=0x4D03000",
    "serial_port=$serialPort", "monitor_port=$monitorPort", "gdb_port=$gdbPort",
    "target_result=STARTED")
[IO.File]::WriteAllLines($manifestPath, $manifestLines, [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $run 'qemu-commandline.log'), ('"{0}" {1}' -f $qemu, ($arguments -join ' ')), [Text.Encoding]::ASCII)

$process = $null; $gdbProcess = $null; $serialClient = $null; $monitorClient = $null
$serialStream = $null; $monitorStream = $null; $serialFile = $null; $injectionLog = $null
$text = [Text.StringBuilder]::new(); $buffer = New-Object byte[] 4096
$targetResult = $null
try {
    $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput (Join-Path $run 'qemu.stdout.log') -RedirectStandardError (Join-Path $run 'qemu.stderr.log') -PassThru -WindowStyle Hidden
    Add-Content -LiteralPath $manifestPath -Value "qemu_pid=$($process.Id)" -Encoding ascii
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $gdbProcess = Start-Process -FilePath $gdb -ArgumentList @('--nx', '--batch', '-x', $gdbCommands) -RedirectStandardOutput $gdbStdout -RedirectStandardError $gdbStderr -PassThru -WindowStyle Hidden
    Add-Content -LiteralPath $manifestPath -Value "gdb_pid=$($gdbProcess.Id)" -Encoding ascii
    $serialClient = Connect-T $serialPort $deadline $process
    $monitorClient = Connect-T $monitorPort $deadline $process
    $serialStream = $serialClient.GetStream(); $monitorStream = $monitorClient.GetStream()
    $serialFile = [IO.File]::Open($serialPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $injectionLog = [IO.StreamWriter]::new($injectionPath, $false, [Text.Encoding]::ASCII)
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-T $serialStream 0x52 $injectionLog 'SERIAL_READY'
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-T $serialStream 0x53 $injectionLog 'SERIAL_SECOND_READY'
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-T $monitorStream 'a' $injectionLog 'KEYBOARD_INPUT_READY'
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_EVENT_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_RUNTIME_SURVIVAL_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-T $monitorStream 'b' $injectionLog 'KEYBOARD_SECOND_INPUT_READY'
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-T $monitorStream 'c' $injectionLog 'KEYBOARD_UNSUBSCRIBED_READY'
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-T $serialStream 0x44 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-T $serialStream 0x45 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-T $serialStream 0x46 $injectionLog 'KEYBOARD_B_SENT'
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_DRAINED' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-T 'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    $targetResult = 'PASS_WITHOUT_PIVOT'
} catch {
    $gdbText = GdbText-T $gdbStdout
    if ($gdbText.Contains('PHASE53T_FIRST_RSP_PIVOT')) {
        $targetResult = 'FIRST_RSP_PIVOT_CAPTURED'
    } elseif ($gdbText.Contains('PHASE53T_DOWNSTREAM_FAULT')) {
        $targetResult = 'DOWNSTREAM_FAULT_CAPTURED'
    } else {
        Add-Content -LiteralPath $manifestPath -Value ("capture_exception={0}" -f $_.Exception.Message) -Encoding ascii
        throw
    }
} finally {
    if ($null -ne $serialStream) { try { Read-T $serialStream $serialFile $text $buffer } catch { } }
    if ($null -ne $serialFile) { $serialFile.Dispose() }
    if ($null -ne $injectionLog) { $injectionLog.Dispose() }
    if ($null -ne $monitorClient) { $monitorClient.Dispose() }
    if ($null -ne $serialClient) { $serialClient.Dispose() }
    if ($null -ne $gdbProcess) {
        try { $gdbProcess.WaitForExit(5000) | Out-Null; if (-not $gdbProcess.HasExited) { Stop-Process -Id $gdbProcess.Id -Force -ErrorAction SilentlyContinue } } catch { }
    }
    if ($null -ne $process) {
        try { if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }; $process.WaitForExit(5000) } catch { }
    }
    if ($targetResult -eq 'PASS_WITHOUT_PIVOT' -and (Test-Path -LiteralPath $gdbStdout)) {
        $finalGdbText = GdbText-T $gdbStdout
        if ($finalGdbText.Contains('PHASE53T_FIRST_RSP_PIVOT')) {
            $targetResult = 'FIRST_RSP_PIVOT_CAPTURED'
        } elseif ($finalGdbText.Contains('PHASE53T_DOWNSTREAM_FAULT')) {
            $targetResult = 'DOWNSTREAM_FAULT_CAPTURED'
        }
    }
    if ($null -ne $targetResult) { Add-Content -LiteralPath $manifestPath -Value "target_result=$targetResult" -Encoding ascii }
    Inventory-T $inventoryPath 'AFTER'
}

if ($targetResult -eq 'FIRST_RSP_PIVOT_CAPTURED' -or
    $targetResult -eq 'DOWNSTREAM_FAULT_CAPTURED') { return }
throw "Phase53T completed without a target boundary: $targetResult"

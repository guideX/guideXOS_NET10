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
    [int]$TimeoutSeconds = 240,
    [switch]$EnableSlotWatchpoint,
    [switch]$EnableContextAudit,
    [switch]$EnableWorkerContextWatchpoint
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

function Require-Q([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Require-Q (Test-Path -LiteralPath $efi) 'EFI artifact is missing.'
Require-Q (Test-Path -LiteralPath $payload) 'Managed payload is missing.'
Require-Q (Test-Path -LiteralPath $PdbPath) 'Matching PDB is missing.'
Require-Q (Test-Path -LiteralPath $qemu) 'QEMU is missing.'
Require-Q (Test-Path -LiteralPath $gdb) 'GDB is missing.'
Require-Q (Test-Path -LiteralPath $ovmfCode) 'OVMF code is missing.'
Require-Q (Test-Path -LiteralPath $ovmfVars) 'OVMF vars template is missing.'
Require-Q ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'Payload size mismatch.'
Require-Q ((Get-Item -LiteralPath $efi).Length -eq $EfiSize) 'EFI size mismatch.'
Require-Q ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $PayloadSha256.ToUpperInvariant()) 'Payload hash mismatch.'
Require-Q ((Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash.ToUpperInvariant() -eq $EfiSha256.ToUpperInvariant()) 'EFI hash mismatch.'

Require-Q (-not (Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"
New-Item -ItemType Directory -Force -Path $run | Out-Null

$faultRva = [UInt64]0x147B80
$faultAddress = $ImageBase + $faultRva
$workerVector = [UInt64]0x4CFD000
$workerTlsBlock = [UInt64]0x4CFC000
$workerGs = [UInt64]0x4CFE000
$workerTeb = [UInt64]0x4CFB000
$workerStackLow = [UInt64]0x4CFF000
$workerStackHigh = [UInt64]0x4D03000
$workerContext = [UInt64]0x1A9A30
$mainStackLow = [UInt64]0x4E9D000
$mainStackHigh = [UInt64]0x4EA1000
$mainVector = [UInt64]0x4EAB000
$mainTlsBlock = [UInt64]0x4EAA000
$slotWatchEnabled = [bool]$EnableSlotWatchpoint

function Get-FreeTcpPort-Q {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally { $listener.Stop() }
}

function Connect-Q([int]$port, [datetime]$deadline, [Diagnostics.Process]$process) {
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

function Read-Q([Net.Sockets.NetworkStream]$stream, [IO.FileStream]$file,
                [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ($stream.DataAvailable) {
        $count = $stream.Read($buffer, 0, $buffer.Length)
        if ($count -le 0) { return }
        $file.Write($buffer, 0, $count)
        $file.Flush()
        $text.Append([Text.Encoding]::ASCII.GetString($buffer, 0, $count)) | Out-Null
    }
}

function Wait-Q([string]$marker, [datetime]$deadline,
                [Diagnostics.Process]$process, [Net.Sockets.NetworkStream]$stream,
                [IO.FileStream]$file, [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ((Get-Date) -lt $deadline) {
        Read-Q $stream $file $text $buffer
        if ($text.ToString().Contains($marker)) { return }
        if (Test-Path -LiteralPath $script:phase53qGdbStdoutPath) {
            try {
                $gdbText = [IO.File]::ReadAllText($script:phase53qGdbStdoutPath)
                if ($gdbText.Contains('PHASE53Q_FAULT_BREAKPOINT')) {
                    throw 'Phase53Q target fault captured by GDB.'
                }
            } catch [IO.IOException] { }
        }
        if ($process.HasExited) { throw "QEMU exited while waiting for $marker." }
        if ($text.ToString().Contains('GXOS_NET10:FAIL:') -or
            $text.ToString().Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=')) {
            throw "Guest reported a non-target failure while waiting for $marker."
        }
        Start-Sleep -Milliseconds 25
    }
    throw "Timed out waiting for $marker."
}

function Send-Serial-Q([Net.Sockets.NetworkStream]$stream, [byte]$value,
                        [IO.StreamWriter]$log, [string]$after) {
    $stream.WriteByte($value)
    $stream.Flush()
    $log.WriteLine(('after={0} serial_byte=0x{1:X2}' -f $after, $value))
    $log.Flush()
}

function Send-Key-Q([Net.Sockets.NetworkStream]$stream, [string]$key,
                    [IO.StreamWriter]$log, [string]$after) {
    $bytes = [Text.Encoding]::ASCII.GetBytes("sendkey $key`n")
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $log.WriteLine(('after={0} monitor_command=sendkey {1}' -f $after, $key))
    $log.Flush()
}

function Write-Inventory-Q([string]$path, [string]$phase) {
    $items = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'")
    Add-Content -LiteralPath $path -Value ("phase={0} count={1}" -f $phase, $items.Count) -Encoding ascii
    foreach ($item in $items) {
        Add-Content -LiteralPath $path -Value ("pid={0} command_line={1}" -f $item.ProcessId, $item.CommandLine) -Encoding ascii
    }
}

$codeCopy = Join-Path $run 'edk2-code.fd'
$varsCopy = Join-Path $run 'edk2-vars.fd'
$serialPath = Join-Path $run 'serial.log'
$qemuStdout = Join-Path $run 'qemu.stdout.log'
$qemuStderr = Join-Path $run 'qemu.stderr.log'
$gdbCommands = Join-Path $run 'gdb-commands.txt'
$gdbStdout = Join-Path $run 'gdb.stdout.log'
$gdbStderr = Join-Path $run 'gdb.stderr.log'
$script:phase53qGdbStdoutPath = $gdbStdout
$injectionPath = Join-Path $run 'injections.log'
$manifestPath = Join-Path $run 'capture-manifest.txt'
$inventoryPath = Join-Path $run 'qemu-process-inventory.log'
$serialPort = Get-FreeTcpPort-Q
$monitorPort = Get-FreeTcpPort-Q
$gdbPort = Get-FreeTcpPort-Q

Copy-Item -LiteralPath $ovmfCode -Destination $codeCopy
Copy-Item -LiteralPath $ovmfVars -Destination $varsCopy
Write-Inventory-Q $inventoryPath 'BEFORE'

$gdbLines = [Collections.Generic.List[string]]::new()
[void]$gdbLines.Add('set pagination off')
[void]$gdbLines.Add('set confirm off')
[void]$gdbLines.Add('set remotetimeout 30')
[void]$gdbLines.Add(('target remote 127.0.0.1:{0}' -f $gdbPort))
[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $faultAddress))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $rcx != 0x4CFC030 && $rcx != 0x4EAA030')
[void]$gdbLines.Add('printf "PHASE53Q_FAULT_BREAKPOINT\n"')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('info registers fs_base gs_base')
[void]$gdbLines.Add('printf "PHASE53Q_GS_BASE_EXPR="')
[void]$gdbLines.Add('p/x $gs_base')
[void]$gdbLines.Add('printf "PHASE53Q_FS_BASE_EXPR="')
[void]$gdbLines.Add('p/x $fs_base')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('x/4gx $rsp-0x40')
[void]$gdbLines.Add('x/64bx $rsp-0x200')
[void]$gdbLines.Add('printf "PHASE53Q_RETURN_ADDRESS="')
[void]$gdbLines.Add('x/gx $rsp')
[void]$gdbLines.Add('printf "PHASE53Q_GS_30="')
[void]$gdbLines.Add('x/gx $gs_base+0x30')
[void]$gdbLines.Add('printf "PHASE53Q_GS_58="')
[void]$gdbLines.Add('x/gx $gs_base+0x58')
[void]$gdbLines.Add('set $fault_vector = *(unsigned long long*)($gs_base+0x58)')
[void]$gdbLines.Add('printf "PHASE53Q_FAULT_VECTOR="')
[void]$gdbLines.Add('p/x $fault_vector')
[void]$gdbLines.Add('x/16gx $fault_vector')
[void]$gdbLines.Add('printf "PHASE53Q_FAULT_VECTOR_SLOT0="')
[void]$gdbLines.Add('x/gx $fault_vector')
[void]$gdbLines.Add('printf "PHASE53Q_WORKER_VECTOR="')
[void]$gdbLines.Add(('x/8gx 0x{0:X}' -f $workerVector))
[void]$gdbLines.Add('printf "PHASE53Q_FAULT_DISASSEMBLY="')
[void]$gdbLines.Add('x/8i $rip')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')
if ($EnableContextAudit) {
    [void]$gdbLines.Add('break *0x167080')
    [void]$gdbLines.Add('commands')
    [void]$gdbLines.Add('silent')
    [void]$gdbLines.Add('set $ctx_old = *(unsigned long long*)$rcx')
    [void]$gdbLines.Add('set $ctx_new = $rdx')
    [void]$gdbLines.Add('set $ctx_rsp = *(unsigned long long*)($ctx_new+0x40)')
    [void]$gdbLines.Add('set $ctx_rip = *(unsigned long long*)($ctx_new+0x48)')
    [void]$gdbLines.Add('set $ctx_gs = *(unsigned long long*)($ctx_new+0x60)')
    [void]$gdbLines.Add('printf "PHASE53Q_CONTEXT_SWITCH oldctx=%p newctx=%p newrsp=%p newrip=%p newgs=%p\n", $ctx_old, $ctx_new, $ctx_rsp, $ctx_rip, $ctx_gs')
    [void]$gdbLines.Add(('if ($ctx_rsp < 0x{0:X} || $ctx_rsp >= 0x{1:X}) && ($ctx_rsp < 0x{2:X} || $ctx_rsp >= 0x{3:X})' -f $workerStackLow, $workerStackHigh, $mainStackLow, $mainStackHigh))
    [void]$gdbLines.Add('printf "PHASE53Q_BAD_CONTEXT_RSP\n"')
    [void]$gdbLines.Add('x/24gx $ctx_new')
    [void]$gdbLines.Add('x/16i $rip-16')
    [void]$gdbLines.Add('end')
    [void]$gdbLines.Add('continue')
    [void]$gdbLines.Add('end')
}
if ($EnableWorkerContextWatchpoint) {
    [void]$gdbLines.Add(('watch *0x{0:X}' -f ($workerContext + 0x40)))
    [void]$gdbLines.Add('commands')
    [void]$gdbLines.Add('silent')
    [void]$gdbLines.Add(('set $watched_context_rsp = *(unsigned long long*)0x{0:X}' -f ($workerContext + 0x40)))
    [void]$gdbLines.Add(('if $watched_context_rsp < 0x{0:X} || $watched_context_rsp >= 0x{1:X}' -f $workerStackLow, $workerStackHigh))
    [void]$gdbLines.Add('printf "PHASE53Q_BAD_WORKER_CONTEXT_RSP_WRITE\\n"')
    [void]$gdbLines.Add('info registers')
    [void]$gdbLines.Add(('x/24gx 0x{0:X}' -f $workerContext))
    [void]$gdbLines.Add('x/16i $pc-16')
    [void]$gdbLines.Add('end')
    [void]$gdbLines.Add('continue')
    [void]$gdbLines.Add('end')
}
if ($slotWatchEnabled) {
    [void]$gdbLines.Add('set $bad_worker_teb_count = 0')
    [void]$gdbLines.Add('set $bad_worker_tls_vector_count = 0')
    foreach ($watch in @(
            [pscustomobject]@{ Name = 'WORKER_TEB'; Address = $workerGs + 0x30; Expected = $workerTeb },
            [pscustomobject]@{ Name = 'WORKER_TLS_VECTOR'; Address = $workerGs + 0x58; Expected = $workerVector })) {
        [void]$gdbLines.Add(('watch *0x{0:X}' -f $watch.Address))
        [void]$gdbLines.Add('commands')
        [void]$gdbLines.Add('silent')
        [void]$gdbLines.Add(('set $watched_slot = *(unsigned long long*)0x{0:X}' -f $watch.Address))
        [void]$gdbLines.Add(('if $watched_slot != 0x{0:X}' -f $watch.Expected))
        [void]$gdbLines.Add(('set $bad_{0}_count = $bad_{0}_count + 1' -f $watch.Name.ToLowerInvariant()))
        [void]$gdbLines.Add(('printf "PHASE53Q_BAD_WORKER_GS_{0}_WRITE\n"' -f $watch.Name.Replace('WORKER_', '')))
        [void]$gdbLines.Add('info registers')
        [void]$gdbLines.Add('x/12i $pc-16')
        [void]$gdbLines.Add('x/16gx $pc-0x40')
        [void]$gdbLines.Add('x/64bx $pc-0x80')
        [void]$gdbLines.Add(('if $bad_{0}_count <= 1' -f $watch.Name.ToLowerInvariant()))
        [void]$gdbLines.Add('printf "PHASE53Q_WATCH_RSP=%p GS_BASE=%p\n", $rsp, $gs_base')
        [void]$gdbLines.Add('x/12gx $rsp-0x40')
        [void]$gdbLines.Add('set $watch_return = *(unsigned long long*)$rsp')
        [void]$gdbLines.Add('if $rsp == $gs_base+0x30')
        [void]$gdbLines.Add('set $watch_caller_return = *(unsigned long long*)($rsp+8)')
        [void]$gdbLines.Add('else')
        [void]$gdbLines.Add('set $watch_caller_return = $watch_return')
        [void]$gdbLines.Add('end')
        [void]$gdbLines.Add('printf "PHASE53Q_WATCH_STACK_TOP="')
        [void]$gdbLines.Add('p/x $watch_return')
        [void]$gdbLines.Add('printf "PHASE53Q_WATCH_CALLER_RETURN="')
        [void]$gdbLines.Add('p/x $watch_caller_return')
        [void]$gdbLines.Add('x/8i $watch_caller_return-16')
        [void]$gdbLines.Add('if $rsp == $gs_base+0x30')
        [void]$gdbLines.Add('x/96i $watch_caller_return-0x100')
        [void]$gdbLines.Add('end')
        [void]$gdbLines.Add('x/64i $pc-0x100')
        [void]$gdbLines.Add('end')
        [void]$gdbLines.Add('printf "PHASE53Q_BAD_SLOT_VALUE="')
        [void]$gdbLines.Add('p/x $watched_slot')
        [void]$gdbLines.Add('end')
        [void]$gdbLines.Add('continue')
        [void]$gdbLines.Add('end')
    }
}
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
[IO.File]::WriteAllText((Join-Path $run 'qemu-commandline.log'),
    ('"{0}" {1}' -f $qemu, ($arguments -join ' ')), [Text.Encoding]::ASCII)
$manifestLines = @(
    "repository=$root", "gate=$gate", "payload=$payload", "payload_size=$PayloadSize",
    "payload_sha256=$PayloadSha256", "efi=$efi", "efi_size=$EfiSize", "efi_sha256=$EfiSha256",
    "pdb=$PdbPath", "pdb_sha256=$((Get-FileHash -LiteralPath $PdbPath -Algorithm SHA256).Hash.ToUpperInvariant())",
    "image_base=0x$('{0:X}' -f $ImageBase)", "fault_rva=0x147B80", "fault_address=0x$('{0:X}' -f $faultAddress)",
    "worker_gs=0x4CFE000", "worker_teb=0x4CFB000",
    "worker_tls_vector=0x4CFD000", "worker_tls_block=0x4CFC000",
    "main_tls_vector=0x4EAB000", "main_tls_block=0x4EAA000",
    "slot_watchpoint_enabled=$slotWatchEnabled", "worker_context_watchpoint_enabled=$([bool]$EnableWorkerContextWatchpoint)",
    "worker_context=0x$('{0:X}' -f $workerContext)", "serial_port=$serialPort", "monitor_port=$monitorPort", "gdb_port=$gdbPort"
)
[IO.File]::WriteAllText($manifestPath,
    ($manifestLines -join [Environment]::NewLine), [Text.Encoding]::ASCII)

$process = $null; $gdbProcess = $null; $serialClient = $null; $monitorClient = $null
$serialStream = $null; $monitorStream = $null; $serialFile = $null; $injectionLog = $null
$text = [Text.StringBuilder]::new(); $buffer = New-Object byte[] 4096
$targetFaultCaptured = $false
try {
    $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate `
        -RedirectStandardOutput (Join-Path $run 'qemu.stdout.log') `
        -RedirectStandardError (Join-Path $run 'qemu.stderr.log') -PassThru -WindowStyle Hidden
    Add-Content -LiteralPath $manifestPath -Value "qemu_pid=$($process.Id)" -Encoding ascii
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $gdbProcess = Start-Process -FilePath $gdb -ArgumentList @('--nx', '--batch', '-x', $gdbCommands) `
        -RedirectStandardOutput $gdbStdout -RedirectStandardError $gdbStderr -PassThru -WindowStyle Hidden
    Add-Content -LiteralPath $manifestPath -Value "gdb_pid=$($gdbProcess.Id)" -Encoding ascii
    $serialClient = Connect-Q $serialPort $deadline $process
    $monitorClient = Connect-Q $monitorPort $deadline $process
    $serialStream = $serialClient.GetStream(); $monitorStream = $monitorClient.GetStream()
    $serialFile = [IO.File]::Open($serialPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $injectionLog = [IO.StreamWriter]::new($injectionPath, $false, [Text.Encoding]::ASCII)
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY' $deadline $process $serialStream $serialFile $text $buffer
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' $deadline $process $serialStream $serialFile $text $buffer
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' $deadline $process $serialStream $serialFile $text $buffer
    Send-Serial-Q $serialStream 0x52 $injectionLog 'SERIAL_READY'
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK' $deadline $process $serialStream $serialFile $text $buffer
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' $deadline $process $serialStream $serialFile $text $buffer
    Send-Serial-Q $serialStream 0x53 $injectionLog 'SERIAL_SECOND_READY'
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' $deadline $process $serialStream $serialFile $text $buffer
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer
    Send-Key-Q $monitorStream 'a' $injectionLog 'KEYBOARD_INPUT_READY'
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_EVENT_OK' $deadline $process $serialStream $serialFile $text $buffer
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_RUNTIME_SURVIVAL_OK' $deadline $process $serialStream $serialFile $text $buffer
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer
    Send-Key-Q $monitorStream 'b' $injectionLog 'KEYBOARD_SECOND_INPUT_READY'
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_READY' $deadline $process $serialStream $serialFile $text $buffer
    Send-Key-Q $monitorStream 'c' $injectionLog 'KEYBOARD_UNSUBSCRIBED_READY'
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK' $deadline $process $serialStream $serialFile $text $buffer
    Send-Serial-Q $serialStream 0x44 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-Q $serialStream 0x45 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-Q $serialStream 0x46 $injectionLog 'KEYBOARD_B_SENT'
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_DRAINED' $deadline $process $serialStream $serialFile $text $buffer
    Wait-Q 'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS' $deadline $process $serialStream $serialFile $text $buffer
    Add-Content -LiteralPath $manifestPath -Value 'target_result=PASS_WITHOUT_FAULT' -Encoding ascii
} catch {
    $gdbText = if (Test-Path -LiteralPath $gdbStdout) {
        try { [IO.File]::ReadAllText($gdbStdout) } catch { '' }
    } else { '' }
    if ($gdbText.Contains('PHASE53Q_FAULT_BREAKPOINT')) {
        $targetFaultCaptured = $true
        Add-Content -LiteralPath $manifestPath -Value 'target_result=FAULT_CAPTURED' -Encoding ascii
    } else {
        Add-Content -LiteralPath $manifestPath -Value ("capture_exception={0}" -f $_.Exception.Message) -Encoding ascii
        throw
    }
} finally {
    if ($null -ne $serialStream) { try { Read-Q $serialStream $serialFile $text $buffer } catch { } }
    if ($null -ne $serialFile) { $serialFile.Dispose() }
    if ($null -ne $injectionLog) { $injectionLog.Dispose() }
    if ($null -ne $monitorClient) { $monitorClient.Dispose() }
    if ($null -ne $serialClient) { $serialClient.Dispose() }
    if ($null -ne $gdbProcess) { try { $gdbProcess.WaitForExit(5000) | Out-Null } catch { } }
    if ($null -ne $process) {
        try {
            if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
            $process.WaitForExit(5000)
        } catch { }
    }
    Write-Inventory-Q $inventoryPath 'AFTER'
}
if ($targetFaultCaptured) { return }

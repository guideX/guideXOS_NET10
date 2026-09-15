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

function Require-S([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Require-S (Test-Path -LiteralPath $efi) 'EFI artifact is missing.'
Require-S (Test-Path -LiteralPath $payload) 'Managed payload is missing.'
Require-S (Test-Path -LiteralPath $PdbPath) 'Matching PDB is missing.'
Require-S (Test-Path -LiteralPath $qemu) 'QEMU is missing.'
Require-S (Test-Path -LiteralPath $gdb) 'GDB is missing.'
Require-S (Test-Path -LiteralPath $ovmfCode) 'OVMF code is missing.'
Require-S (Test-Path -LiteralPath $ovmfVars) 'OVMF vars template is missing.'
Require-S ((Get-Item -LiteralPath $payload).Length -eq $PayloadSize) 'Payload size mismatch.'
Require-S ((Get-Item -LiteralPath $efi).Length -eq $EfiSize) 'EFI size mismatch.'
Require-S ((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant() -eq $PayloadSha256.ToUpperInvariant()) 'Payload hash mismatch.'
Require-S ((Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash.ToUpperInvariant() -eq $EfiSha256.ToUpperInvariant()) 'EFI hash mismatch.'
Require-S (-not (Test-Path -LiteralPath $evidence)) "Evidence directory already exists: $evidence"
New-Item -ItemType Directory -Force -Path $run | Out-Null

$faultRva = [UInt64]0x147B80
$faultAddress = $ImageBase + $faultRva
$workerGs = [UInt64]0x4CFE000
$workerStackLow = [UInt64]0x4CFF000
$workerStackHigh = [UInt64]0x4D03000
$schedulerEntry = [UInt64]0x167080
$schedulerRspConsumer = [UInt64]0x16722E
$workerEntry = [UInt64]0x167B40
$eventCallSite = [UInt64]0x7E6E6BC
$ovmfEntry = [UInt64]0x6B0D67C
$finalTransferJmp = [UInt64]0x6B57E5E
$finalTargetLoad = [UInt64]0x6B57E3C
$interruptEntry = [UInt64]0x6B610E3
$interruptCommonEntry = [UInt64]0x6B60FE2
$interruptCommonAfterMov = [UInt64]0x6B60FE5
$interruptCommonAfterAlign = [UInt64]0x6B60FE9
$interruptDispatchEntry = [UInt64]0x6B61004
$interruptFrameEntry = [UInt64]0x6B6102B
$interruptedSite = [UInt64]0x105C26
$postTransferSite = [UInt64]0x6B61123
$serialFunctionEntry = [UInt64]0x105BC0
$serialReturnSite = [UInt64]0x111A6E
$serialCallerEntry = [UInt64]0x111870
$serialCallerUpstreamEntry = [UInt64]0x5004140
$serialCallerUpstreamFunctionEntry = [UInt64]0x501A5B0
$serialCallerUpstreamCallerEntry = [UInt64]0x501A800
# R19's entry stack word was 0x6B61123.  A five-byte near CALL therefore
# has this exact candidate callsite; the run proves or rejects it dynamically.
$candidateCallSite = [UInt64]0x6B6111E

function Get-FreeTcpPort-S {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    } finally { $listener.Stop() }
}

function Connect-S([int]$port, [datetime]$deadline, [Diagnostics.Process]$process) {
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

function Read-S([Net.Sockets.NetworkStream]$stream, [IO.FileStream]$file,
                [Text.StringBuilder]$text, [byte[]]$buffer) {
    while ($stream.DataAvailable) {
        $count = $stream.Read($buffer, 0, $buffer.Length)
        if ($count -le 0) { return }
        $file.Write($buffer, 0, $count)
        $file.Flush()
        $text.Append([Text.Encoding]::ASCII.GetString($buffer, 0, $count)) | Out-Null
    }
}

function Get-GdbText-S([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try { return [IO.File]::ReadAllText($path) } catch [IO.IOException] { return '' }
}

function Wait-S([string]$marker, [datetime]$deadline,
                [Diagnostics.Process]$process, [Net.Sockets.NetworkStream]$stream,
                [IO.FileStream]$file, [Text.StringBuilder]$text, [byte[]]$buffer,
                [string]$gdbPath) {
    while ((Get-Date) -lt $deadline) {
        Read-S $stream $file $text $buffer
        $gdbText = Get-GdbText-S $gdbPath
        if ($gdbText.Contains('PHASE53S_TARGET_HIT') -or
            $gdbText.Contains('PHASE53S_FAULT_BREAKPOINT')) {
            throw 'Phase53S diagnostic boundary captured by GDB.'
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

function Send-Serial-S([Net.Sockets.NetworkStream]$stream, [byte]$value,
                       [IO.StreamWriter]$log, [string]$after) {
    $stream.WriteByte($value)
    $stream.Flush()
    $log.WriteLine(('after={0} serial_byte=0x{1:X2}' -f $after, $value))
    $log.Flush()
}

function Send-Key-S([Net.Sockets.NetworkStream]$stream, [string]$key,
                    [IO.StreamWriter]$log, [string]$after) {
    $bytes = [Text.Encoding]::ASCII.GetBytes("sendkey $key`n")
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
    $log.WriteLine(('after={0} monitor_command=sendkey {1}' -f $after, $key))
    $log.Flush()
}

function Write-Inventory-S([string]$path, [string]$phase) {
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
$serialPort = Get-FreeTcpPort-S
$monitorPort = Get-FreeTcpPort-S
$gdbPort = Get-FreeTcpPort-S

Copy-Item -LiteralPath $ovmfCode -Destination $codeCopy
Copy-Item -LiteralPath $ovmfVars -Destination $varsCopy
Write-Inventory-S $inventoryPath 'BEFORE'

$gdbLines = [Collections.Generic.List[string]]::new()
[void]$gdbLines.Add('set pagination off')
[void]$gdbLines.Add('set confirm off')
[void]$gdbLines.Add('set remotetimeout 30')
[void]$gdbLines.Add(('target remote 127.0.0.1:{0}' -f $gdbPort))

# Keep the old fault as a bounded stop, but do not trace through its body.
[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $faultAddress))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $rcx != 0x4CFC030 && $rcx != 0x4EAA030')
[void]$gdbLines.Add('printf "PHASE53S_FAULT_BREAKPOINT\n"')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('info registers fs_base gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# Confirm the inherited valid scheduler restore without treating it as the
# producer.  The exact RSP-consuming instruction is logged as before.
[void]$gdbLines.Add(('break *0x{0:X}' -f $schedulerEntry))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('set $phase53s_ctx_new = $rdx')
[void]$gdbLines.Add('set $phase53s_ctx_rsp = *(unsigned long long*)($rdx+0x40)')
[void]$gdbLines.Add('set $phase53s_ctx_rip = *(unsigned long long*)($rdx+0x48)')
[void]$gdbLines.Add('set $phase53s_ctx_gs = *(unsigned long long*)($rdx+0x60)')
[void]$gdbLines.Add('printf "PHASE53S_CONTEXT newctx=%p newrsp=%p newrip=%p newgs=%p\n", $phase53s_ctx_new, $phase53s_ctx_rsp, $phase53s_ctx_rip, $phase53s_ctx_gs')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add(('break *0x{0:X}' -f $schedulerRspConsumer))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_SCHEDULER_CONSUMER rip=%p rsp_before=%p rsp_operand=%p\n", $rip, $rsp, $r10')
[void]$gdbLines.Add('x/6i $rip-8')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# Record one valid event call, and any later occurrence that is already in the
# GS page.  The event routine body is not traced.
[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $eventCallSite))
[void]$gdbLines.Add('disable 4')
[void]$gdbLines.Add('commands 4')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFF000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('if $phase53s_event_seen == 0')
[void]$gdbLines.Add('set $phase53s_event_seen = 1')
[void]$gdbLines.Add('printf "PHASE53S_EVENT_VALID rip=%p rsp=%p rbp=%p gs=%p return=", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/gx $rsp')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('else')
[void]$gdbLines.Add('printf "PHASE53S_EVENT_GS_PAGE rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# Candidate final transfer: R19's [RSP] return address proves or rejects this
# exact five-byte direct-call site.  Capture the caller state before executing
# the call, including bytes and the indirect/direct operand information.
[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $candidateCallSite))
[void]$gdbLines.Add('disable 5')
[void]$gdbLines.Add('commands 5')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('printf "PHASE53S_PRE_TRANSFER rip=%p rsp=%p rbp=%p gs=%p bytes=", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/8bx $rip')
[void]$gdbLines.Add('x/16i $rip-8')
[void]$gdbLines.Add('x/8gx $rsp-0x20')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# True OVMF entry, before push %rbp.  Capture the dynamic return site and a
# disassembly window around it, then stop.  The target breakpoint is enabled
# only after the target worker is scheduled so startup code is excluded.
[void]$gdbLines.Add(('hbreak *0x{0:X}' -f $ovmfEntry))
[void]$gdbLines.Add('disable 6')
[void]$gdbLines.Add('commands 6')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('set $phase53s_ret = *(unsigned long long*)$rsp')
[void]$gdbLines.Add('set $phase53s_inferred_pre_rsp = $rsp + 8')
[void]$gdbLines.Add('printf "PHASE53S_OVMF_ENTRY rip=%p rsp=%p inferred_pre_rsp=%p rbp=%p gs=%p return=%p\n", $rip, $rsp, $phase53s_inferred_pre_rsp, $rbp, $gs_base, $phase53s_ret')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('info registers fs_base gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/8bx $phase53s_ret-5')
[void]$gdbLines.Add('x/24i $phase53s_ret-0x40')
[void]$gdbLines.Add('printf "PHASE53S_THUNK_6B56DDA\\n"')
[void]$gdbLines.Add('x/1000i 0x6B56DDA')
[void]$gdbLines.Add('printf "PHASE53S_CALLER_WINDOW_6B6110E\\n"')
[void]$gdbLines.Add('x/80i 0x6B6110E')
[void]$gdbLines.Add('x/16i $rip-16')
[void]$gdbLines.Add('x/64i 0x6B0D680')
[void]$gdbLines.Add('printf "PHASE53S_SYMBOL_LOOKUP\\n"')
[void]$gdbLines.Add('info symbol 0x105C26')
[void]$gdbLines.Add('info symbol 0x6B61004')
[void]$gdbLines.Add('info symbol 0x6B0D67C')
[void]$gdbLines.Add('x/32i 0x105BE0')
[void]$gdbLines.Add('x/100i 0x105B00')
[void]$gdbLines.Add('x/200i 0x105000')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_STATIC_111A00\n"')
[void]$gdbLines.Add('x/160i 0x111A00')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_PROLOGUE_STATIC_111700\n"')
[void]$gdbLines.Add('x/256i 0x111700')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_UPSTREAM_STATIC_5004000\n"')
[void]$gdbLines.Add('x/256i 0x5004000')
[void]$gdbLines.Add('printf "PHASE53S_GS_PAGE base=%p offsets=", $gs_base')
[void]$gdbLines.Add('x/20gx $gs_base+0x1E8')
[void]$gdbLines.Add('printf "PHASE53S_ARITH entry_minus_gs=%p\n", $rsp-$gs_base')
[void]$gdbLines.Add('printf "PHASE53S_TARGET_HIT\n"')
[void]$gdbLines.Add('quit')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# The thunk's static tail restores its frame and then performs an indirect
# tail jump.  Capture the final transfer only when its target is the OVMF
# entry, so the transfer's RSP effect and target operand are explicit.
[void]$gdbLines.Add(('break *0x{0:X}' -f $finalTransferJmp))
[void]$gdbLines.Add('disable 7')
[void]$gdbLines.Add('commands 7')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rax == 0x6B0D67C')
[void]$gdbLines.Add('printf "PHASE53S_FINAL_TRANSFER_PRE rip=%p rsp=%p rax=%p target=%p rbp=%p gs=%p bytes=", $rip, $rsp, $rax, $rax, $rbp, $gs_base')
[void]$gdbLines.Add('x/8bx $rip')
[void]$gdbLines.Add('x/16i 0x6B57E45')
[void]$gdbLines.Add('x/8gx $rsp-0x20')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

# Capture the indirect target-table load that supplies RAX to the final jump.
[void]$gdbLines.Add(('break *0x{0:X}' -f $finalTargetLoad))
[void]$gdbLines.Add('disable 8')
[void]$gdbLines.Add('commands 8')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('printf "PHASE53S_FINAL_TARGET_LOAD rip=%p rsp=%p rdi=%p r14=%p effective=%p value=%p gs=%p bytes=", $rip, $rsp, $rdi, $r14, $rdi + $r14 * 8, *(unsigned long long*)($rdi + $r14 * 8), $gs_base')
[void]$gdbLines.Add('x/8bx $rip')
[void]$gdbLines.Add('x/8i 0x6B57E30')
[void]$gdbLines.Add('x/4gx $rdi+$r14*8')
[void]$gdbLines.Add('info registers rax rdi r14 rsp rbp gs_base')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $interruptEntry))
[void]$gdbLines.Add('disable 9')
[void]$gdbLines.Add('commands 9')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('if $gs_base == 0x4CFE000')
[void]$gdbLines.Add('if $rsp >= 0x4CFF000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('if $phase53s_irq_valid_seen == 0')
[void]$gdbLines.Add('set $phase53s_irq_valid_seen = 1')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_VALID_ENTRY rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/32gx $rbp-0x20')
[void]$gdbLines.Add('x/16i $rip-0x20')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('else')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_BAD_ENTRY rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/48gx $rbp-0x40')
[void]$gdbLines.Add('x/16i $rip-0x20')
[void]$gdbLines.Add('x/96i $rip-0x200')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('else')
[void]$gdbLines.Add('if $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_BAD_ENTRY rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/48gx $rbp-0x40')
[void]$gdbLines.Add('x/16i $rip-0x20')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $interruptCommonEntry))
[void]$gdbLines.Add('disable 10')
[void]$gdbLines.Add('condition 10 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 10')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_COMMON_ENTRY rip=%p rsp=%p gs=%p\n", $rip, $rsp, $gs_base')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $interruptCommonAfterMov))
[void]$gdbLines.Add('disable 11')
[void]$gdbLines.Add('condition 11 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('commands 11')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_COMMON_AFTER_MOV_BAD rip=%p rsp=%p rax=%p gs=%p\n", $rip, $rsp, $rax, $gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $interruptCommonAfterAlign))
[void]$gdbLines.Add('disable 12')
[void]$gdbLines.Add('condition 12 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('commands 12')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_COMMON_AFTER_ALIGN_BAD rip=%p rsp=%p rax=%p gs=%p\n", $rip, $rsp, $rax, $gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $interruptDispatchEntry))
[void]$gdbLines.Add('disable 13')
[void]$gdbLines.Add('condition 13 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 13')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_DISPATCH_ENTRY rip=%p rsp=%p rax=%p rcx=%p gs=%p\n", $rip, $rsp, $rax, $rcx, $gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/16i $rip-0x20')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $interruptFrameEntry))
[void]$gdbLines.Add('disable 14')
[void]$gdbLines.Add('condition 14 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 14')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPT_FRAME_ENTRY rip=%p rsp=%p rbp=%p rcx=%p gs=%p\n", $rip, $rsp, $rbp, $rcx, $gs_base')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('x/16i $rip-0x20')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $interruptedSite))
[void]$gdbLines.Add('disable 15')
[void]$gdbLines.Add('condition 15 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4CFF000')
[void]$gdbLines.Add('commands 15')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_INTERRUPTED_SITE rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/16i $rip-0x20')
[void]$gdbLines.Add('x/24gx $rsp-0x40')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('bt 16')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $postTransferSite))
[void]$gdbLines.Add('disable 16')
[void]$gdbLines.Add('condition 16 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 16')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_POST_TRANSFER rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/16i $rip-0x10')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $serialFunctionEntry))
[void]$gdbLines.Add('disable 17')
[void]$gdbLines.Add('condition 17 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 17')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_FUNCTION_ENTRY rip=%p rsp_before_push=%p rbp=%p gs=%p return=%p\n", $rip, $rsp, $rbp, $gs_base, *(unsigned long long*)$rsp')
[void]$gdbLines.Add('x/20i $rip')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $serialReturnSite))
[void]$gdbLines.Add('disable 18')
[void]$gdbLines.Add('condition 18 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 18')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_RETURN_SITE rip=%p rsp=%p rbp=%p gs=%p\n", $rip, $rsp, $rbp, $gs_base')
[void]$gdbLines.Add('x/24i $rip-0x20')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('info registers')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add('set $phase53s_event_seen = 0')
[void]$gdbLines.Add('set $phase53s_irq_valid_seen = 0')
[void]$gdbLines.Add('set $phase53s_common_valid_seen = 0')
[void]$gdbLines.Add(('break *0x{0:X}' -f $workerEntry))
[void]$gdbLines.Add('commands')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('enable 4')
[void]$gdbLines.Add('enable 5')
[void]$gdbLines.Add('enable 6')
[void]$gdbLines.Add('enable 7')
[void]$gdbLines.Add('enable 8')
[void]$gdbLines.Add('enable 9')
[void]$gdbLines.Add('enable 10')
[void]$gdbLines.Add('enable 11')
[void]$gdbLines.Add('enable 12')
[void]$gdbLines.Add('enable 13')
[void]$gdbLines.Add('enable 14')
[void]$gdbLines.Add('enable 15')
[void]$gdbLines.Add('enable 16')
[void]$gdbLines.Add('enable 17')
[void]$gdbLines.Add('enable 18')
[void]$gdbLines.Add('enable 19')
[void]$gdbLines.Add('enable 20')
[void]$gdbLines.Add('enable 21')
[void]$gdbLines.Add('enable 22')
[void]$gdbLines.Add('enable 23')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $serialCallerUpstreamCallerEntry))
[void]$gdbLines.Add('disable 20')
[void]$gdbLines.Add('condition 20 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 20')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_UPSTREAM_CALLER_ENTRY rip=%p rsp_before_frame=%p rbp=%p gs=%p return=%p\n", $rip, $rsp, $rbp, $gs_base, *(unsigned long long*)$rsp')
[void]$gdbLines.Add('x/64i $rip')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('info registers rax rbx rcx rdx rsi rdi rbp rsp r8 r9 r10 r11 r12 r13 r14 r15 gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $serialCallerUpstreamFunctionEntry))
[void]$gdbLines.Add('disable 21')
[void]$gdbLines.Add('condition 21 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 21')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_UPSTREAM_FUNCTION_ENTRY rip=%p rsp_before_frame=%p rbp=%p gs=%p return=%p\n", $rip, $rsp, $rbp, $gs_base, *(unsigned long long*)$rsp')
[void]$gdbLines.Add('x/48i $rip')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('info registers rax rbx rcx rdx rsi rdi rbp rsp r8 r9 r10 r11 r12 r13 r14 r15 gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $serialCallerUpstreamEntry))
[void]$gdbLines.Add('disable 22')
[void]$gdbLines.Add('condition 22 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 22')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('set $phase53s_upstream_ret = *(unsigned long long*)$rsp')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_UPSTREAM_ENTRY rip=%p rsp_before_sub=%p rbp=%p gs=%p return=%p\n", $rip, $rsp, $rbp, $gs_base, $phase53s_upstream_ret')
[void]$gdbLines.Add('x/48i $rip')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('x/80i $phase53s_upstream_ret-0x40')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_UPSTREAM_PROLOGUE_STATIC_501A300\n"')
[void]$gdbLines.Add('x/512i 0x501A300')
[void]$gdbLines.Add('info registers rax rbx rcx rdx rsi rdi rbp rsp r8 r9 r10 r11 r12 r13 r14 r15 gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')

[void]$gdbLines.Add(('break *0x{0:X}' -f $serialCallerEntry))
[void]$gdbLines.Add('disable 23')
[void]$gdbLines.Add('condition 23 $gs_base == 0x4CFE000 && $rsp >= 0x4CFE000 && $rsp < 0x4D03000')
[void]$gdbLines.Add('commands 23')
[void]$gdbLines.Add('silent')
[void]$gdbLines.Add('printf "PHASE53S_SERIAL_CALLER_ENTRY rip=%p rsp_before_push=%p rbp=%p gs=%p return=%p\n", $rip, $rsp, $rbp, $gs_base, *(unsigned long long*)$rsp')
[void]$gdbLines.Add('x/40i $rip')
[void]$gdbLines.Add('x/16gx $rsp-0x20')
[void]$gdbLines.Add('info registers rax rbx rcx rdx rsi rdi rbp rsp r8 r9 r10 r11 r12 r13 r14 r15 gs_base')
[void]$gdbLines.Add('continue')
[void]$gdbLines.Add('end')
[void]$gdbLines.Add('echo PHASE53S_GDB_READY\\n')
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
    "scheduler_entry=0x167080", "scheduler_rsp_consumer=0x16722E", "worker_entry=0x167B40",
    "ovmf_event_call_site=0x7E6E6BC", "ovmf_entry=0x6B0D67C", "candidate_call_site=0x6B6111E",
    "final_transfer_jmp=0x6B57E5E",
    "final_target_load=0x6B57E3C",
    "interrupt_entry=0x6B610E3",
    "interrupt_common_entry=0x6B60FE2",
    "interrupt_dispatch_entry=0x6B61004",
    "interrupt_frame_entry=0x6B6102B",
    "interrupted_site=0x105C26",
    "post_transfer_site=0x6B61123",
    "serial_function_entry=0x105BC0", "serial_return_site=0x111A6E",
    "serial_caller_entry=0x111870",
    "serial_caller_upstream_entry=0x5004140",
    "serial_caller_upstream_function_entry=0x501A5B0",
    "serial_caller_upstream_caller_entry=0x501A800",
    "worker_gs=0x4CFE000", "worker_stack_low=0x4CFF000", "worker_stack_high=0x4D03000",
    "serial_port=$serialPort", "monitor_port=$monitorPort", "gdb_port=$gdbPort",
    "target_result=STARTED")
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
    $serialClient = Connect-S $serialPort $deadline $process
    $monitorClient = Connect-S $monitorPort $deadline $process
    $serialStream = $serialClient.GetStream(); $monitorStream = $monitorClient.GetStream()
    $serialFile = [IO.File]::Open($serialPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $injectionLog = [IO.StreamWriter]::new($injectionPath, $false, [Text.Encoding]::ASCII)
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_DRIVER_WORKER_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-S $serialStream 0x52 $injectionLog 'SERIAL_READY'
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_NATIVE_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-S $serialStream 0x53 $injectionLog 'SERIAL_SECOND_READY'
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-S $monitorStream 'a' $injectionLog 'KEYBOARD_INPUT_READY'
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_EVENT_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_RUNTIME_SURVIVAL_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-S $monitorStream 'b' $injectionLog 'KEYBOARD_SECOND_INPUT_READY'
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_READY' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Key-S $monitorStream 'c' $injectionLog 'KEYBOARD_UNSUBSCRIBED_READY'
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Send-Serial-S $serialStream 0x44 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-S $serialStream 0x45 $injectionLog 'KEYBOARD_B_SENT'
    Start-Sleep -Milliseconds 50
    Send-Serial-S $serialStream 0x46 $injectionLog 'KEYBOARD_B_SENT'
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_DRIVER_BURST_DRAINED' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    Wait-S 'GXOS_NET10:MANAGED_KERNEL_PHASE11_PASS' $deadline $process $serialStream $serialFile $text $buffer $gdbStdout
    $targetResult = 'PASS_WITHOUT_BOUNDARY'
} catch {
    $gdbText = Get-GdbText-S $gdbStdout
    if ($gdbText.Contains('PHASE53S_TARGET_HIT')) {
        $targetResult = 'OVMF_ENTRY_PREDECESSOR_CAPTURED'
    } elseif ($gdbText.Contains('PHASE53S_FAULT_BREAKPOINT')) {
        $targetResult = 'DOWNSTREAM_FAULT_CAPTURED'
    } else {
        Add-Content -LiteralPath $manifestPath -Value ("capture_exception={0}" -f $_.Exception.Message) -Encoding ascii
        throw
    }
} finally {
    if ($null -ne $serialStream) { try { Read-S $serialStream $serialFile $text $buffer } catch { } }
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
    Write-Inventory-S $inventoryPath 'AFTER'
}

if ($targetResult -eq 'OVMF_ENTRY_PREDECESSOR_CAPTURED' -or
    $targetResult -eq 'DOWNSTREAM_FAULT_CAPTURED') { return }
throw "Phase53S completed without a target boundary: $targetResult"

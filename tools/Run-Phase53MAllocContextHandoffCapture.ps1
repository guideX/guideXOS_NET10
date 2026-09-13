[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\phase53m-alloc-context-handoff-capture-1',
    [int]$TimeoutMinutes = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = Join-Path $repo 'artifacts\phase53g-corrected-gate'
$out = Join-Path $repo $OutputDirectory
$qemu = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
$gdb = 'C:\mingw64\bin\gdb.exe'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$payloadPath = Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll'
$pdbPath = Join-Path $repo 'artifacts\phase53a-diagnostic-build\publish\gxos-managed-kernel.pdb'
$code = Join-Path $out 'edk2-code.fd'
$vars = Join-Path $out 'edk2-vars.fd'
$serialPath = Join-Path $out 'serial.log'
$stderrPath = Join-Path $out 'qemu.stderr.log'
$stdoutPath = Join-Path $out 'qemu.stdout.log'
$commandPath = Join-Path $out 'qemu-commandline.log'
$gdbScriptPath = Join-Path $out 'gdb-capture.txt'
$gdbOutPath = Join-Path $out 'gdb.stdout.log'
$gdbErrPath = Join-Path $out 'gdb.stderr.log'
$injectionPath = Join-Path $out 'host-injections.log'
$summaryPath = Join-Path $out 'capture-summary.txt'

$expectedPayloadHash = 'E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388'
$expectedPdbHash = '7270B65498976A8B531E04758C91E1A627E4A916508DB5FC411E9FB4CA8FCA9C'
$expectedWorkerEeType = [uint64]0x5694E68
$expectedWorkerRva = [uint64]0x675E68

if (!(Test-Path -LiteralPath $payloadPath)) { throw "Matching payload was not found: $payloadPath" }
if (!(Test-Path -LiteralPath $pdbPath)) { throw "Matching PDB was not found: $pdbPath" }
$payloadHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToUpperInvariant()
$pdbHash = (Get-FileHash -LiteralPath $pdbPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($payloadHash -ne $expectedPayloadHash) { throw "Payload hash mismatch: $payloadHash" }
if ($pdbHash -ne $expectedPdbHash) { throw "PDB hash mismatch: $pdbHash" }

New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item -LiteralPath (Join-Path $share 'edk2-x86_64-code.fd') -Destination $code -Force
Copy-Item -LiteralPath (Join-Path $share 'edk2-i386-vars.fd') -Destination $vars -Force

function Get-FreeTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try { $listener.Start(); return ([Net.IPEndPoint]$listener.LocalEndpoint).Port }
    finally { $listener.Stop() }
}

function Get-FreeUdpPort {
    $socket = [Net.Sockets.UdpClient]::new([Net.Sockets.AddressFamily]::InterNetwork)
    try {
        $socket.Client.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
        return ([Net.IPEndPoint]$socket.Client.LocalEndPoint).Port
    } finally { $socket.Dispose() }
}

function Connect-Tcp([int]$port, [int]$timeoutMs = 30000) {
    $client = [Net.Sockets.TcpClient]::new()
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $task = $client.ConnectAsync('127.0.0.1', $port)
            if ($task.Wait(250) -and $client.Connected) {
                $client.Client.NoDelay = $true
                return $client
            }
        } catch { }
        $client.Dispose()
        $client = [Net.Sockets.TcpClient]::new()
        Start-Sleep -Milliseconds 50
    }
    $client.Dispose()
    throw "Timed out connecting to TCP port $port."
}

function Send-Line([Net.Sockets.TcpClient]$client, [string]$line) {
    $bytes = [Text.Encoding]::ASCII.GetBytes($line + [Environment]::NewLine)
    $stream = $client.GetStream()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
}

function Send-RawByte([Net.Sockets.TcpClient]$client, [byte]$value) {
    $bytes = [byte[]]@($value)
    $stream = $client.GetStream()
    $stream.Write($bytes, 0, 1)
    $stream.Flush()
}

$serialPort = Get-FreeTcpPort
$monitorPort = Get-FreeTcpPort
$gdbPort = Get-FreeTcpPort
$rxPort = Get-FreeUdpPort
$peerPort = Get-FreeUdpPort
$peerUdp = [Net.Sockets.UdpClient]::new([Net.Sockets.AddressFamily]::InterNetwork)
$peerUdp.Client.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, $peerPort))

$arguments = @(
    '-machine', 'q35',
    '-accel', 'tcg,thread=single',
    '-m', '128M',
    '-drive', "if=pflash,format=raw,readonly=on,file=$code",
    '-drive', "if=pflash,format=raw,file=$vars",
    '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
    '-rtc', 'base=utc,clock=vm',
    '-boot', 'order=c',
    '-chardev', "socket,id=serial0,host=127.0.0.1,port=$serialPort,server=on,wait=on,telnet=off,ipv4=on,nodelay=on",
    '-serial', 'none',
    '-device', 'isa-serial,chardev=serial0,iobase=0x3f8,irq=4,wakeup=on',
    '-monitor', "tcp:127.0.0.1:$monitorPort,server=on,wait=on",
    '-display', 'none',
    '-no-reboot',
    '-no-shutdown',
    '-cpu', 'max',
    '-nic', 'none',
    '-netdev', "dgram,id=net0,local.type=inet,local.host=127.0.0.1,local.port=$rxPort,remote.type=inet,remote.host=127.0.0.1,remote.port=$peerPort",
    '-device', 'e1000e,netdev=net0,addr=2',
    '-object', 'rng-builtin,id=rng0',
    '-device', 'virtio-rng-pci-non-transitional,rng=rng0,addr=3,max-bytes=1024,period=1',
    '-S',
    '-gdb', "tcp:127.0.0.1:$gdbPort"
)
Set-Content -LiteralPath $commandPath -Value ('"{0}" {1}' -f $qemu, ($arguments -join ' ')) -Encoding ascii

$process = $null
$serialClient = $null
$monitorClient = $null
$serialStream = $null
$serialFile = $null
$gdbProcess = $null
$injectionFile = $null
$serialText = [Text.StringBuilder]::new()
$serialBuffer = New-Object byte[] 4096
$imageBase = [uint64]0
$captureStatus = 'NOT_STARTED'
$sentSerial52 = $false
$sentSerial53 = $false
$sentKeyA = $false
$sentKeyB = $false
$sentKeyC = $false
$sentBurst = $false

try {
    $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru -WindowStyle Hidden
    $serialClient = Connect-Tcp $serialPort
    $monitorClient = Connect-Tcp $monitorPort
    $serialStream = $serialClient.GetStream()
    $serialFile = [IO.File]::Open($serialPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    Send-Line $monitorClient 'cont'

    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "QEMU exited while waiting for IMAGE_BASE (exit $($process.ExitCode))." }
        while ($serialStream.DataAvailable) {
            $count = $serialStream.Read($serialBuffer, 0, $serialBuffer.Length)
            if ($count -le 0) { break }
            $serialFile.Write($serialBuffer, 0, $count)
            [void]$serialText.Append([Text.Encoding]::ASCII.GetString($serialBuffer, 0, $count))
        }
        $match = [regex]::Match($serialText.ToString(), 'GXOS_NET10:IMAGE_BASE=0x([0-9A-Fa-f]+)')
        if ($match.Success) { $imageBase = [Convert]::ToUInt64($match.Groups[1].Value, 16); break }
        Start-Sleep -Milliseconds 10
    }
    if ($imageBase -eq 0) { throw 'IMAGE_BASE was not observed.' }
    $workerEeTypeAddress = $imageBase + $expectedWorkerRva
    Send-Line $monitorClient 'stop'
    Start-Sleep -Milliseconds 250

    # Managed payload addresses are verified against the preserved Phase 53
    # payload. Native EFI addresses are its fixed link addresses.
    $rhpNewFast = $imageBase + 0x148A20
    $rhpContextReady = $imageBase + 0x148A3B
    $rhpAllocPtrStore = $imageBase + 0x148A54
    $rhpAfterAlloc = $imageBase + 0x148A58
    $rhpAssignRef = $imageBase + 0x148C80
    $rhpAssignRefAfterStore = $imageBase + 0x148C83
    $workerCallReturn = $imageBase + 0xF8455
    $workerPublishCall = $imageBase + 0xF8480
    $fixCallSite = $imageBase + 0x185DAB
    $fixAllocationContext = $imageBase + 0x172E20
    $fixInternalEntry = $imageBase + 0x172DF0
    $fixInternalCall = $imageBase + 0x172E12
    $fixInternalTargetSlot = $imageBase + 0x195448
    $fixContextEnumerationSelect = $imageBase + 0x152E73
    $fixContextEnumerationCall = $imageBase + 0x152E7A
    $fixContextPass = $imageBase + 0x15F42A
    $fixAllocationSetFree = $imageBase + 0x172E95
    $tlsIndexCell = $imageBase + 0x48A074
    $rootSlot = [uint64]0x4000000008C0

    $gdbText = @'
set pagination off
set confirm off
set architecture i386:x86-64
set can-use-hw-watchpoints 1
set print pretty off
set $event = 0
set $src_ptr_writes = 0
set $src_limit_writes = 0
set $dst_ptr_writes = 0
set $dst_limit_writes = 0
set $switch_count = 0
set $configure_seen = 0
set $source_block = 0
set $source_ctx = 0
set $destination_block = 0
set $destination_ctx = 0
set $worker_tcb = 0
set $worker = 0
set $worker_alloc_ctx = 0
set $worker_alloc_seen = 0
set $publication_seen = 0
set $fix_seen = 0
set $target_captured = 0
set $source_watch_installed = 0
set $fix_pre_call_count = 0
target remote 127.0.0.1:__GDB_PORT__

# initialize_nativeaot_tls entry: no TLS block exists yet.
break *0x105db0
commands
 silent
 printf "EVENT=%u NATIVE_TLS_INIT_ENTRY pc=%p gs_base=%p tls_block_cell=0x1968f8 tls_block=%p return=%p\n",$event,$pc,$gs_base,*(unsigned long long*)0x1968f8,*(unsigned long long*)$rsp
 bt 12
 continue
end

# The AllocatePages call for g_tls_block returns at 0x105e50. The global cell
# is now populated, before the block is copied into the GS/TLS structures.
break *0x105e50
commands
 silent
 if $source_watch_installed == 0 && *(unsigned long long*)0x1968f8 != 0
  set $source_block = *(unsigned long long*)0x1968f8
  set $source_ctx = $source_block + 0x38
  set $source_watch_installed = 1
  printf "EVENT=%u MAIN_TLS_BLOCK_CREATED pc=%p tls_block=%p source_context=%p ptr=%p limit=%p gs_base=%p return=%p\n",$event,$pc,$source_block,$source_ctx,*(unsigned long long*)$source_ctx,*(unsigned long long*)($source_ctx+8),$gs_base,*(unsigned long long*)$rsp
  printf "MAIN_TLS_BLOCK_CREATED_REGS rax=%p rbx=%p rcx=%p rdx=%p r8=%p r9=%p rsp=%p\n",$rax,$rbx,$rcx,$rdx,$r8,$r9,$rsp
  bt 16
  watch *(unsigned long long*)$source_ctx
  commands
   silent
   set $src_ptr_writes = $src_ptr_writes + 1
   if $src_ptr_writes <= 32 || *(unsigned long long*)$source_ctx == $worker
    set $event = $event + 1
    printf "EVENT=%u SOURCE_ALLOC_PTR_WRITE count=%u context=%p pc=%p new_ptr=%p limit=%p\n",$event,$src_ptr_writes,$source_ctx,$pc,*(unsigned long long*)$source_ctx,*(unsigned long long*)($source_ctx+8)
    x/6i $pc-8
    printf "SOURCE_ALLOC_PTR_WRITE_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
    bt 18
   end
   continue
  end
  watch *(unsigned long long*)($source_ctx+8)
  commands
   silent
   set $src_limit_writes = $src_limit_writes + 1
   if $src_limit_writes <= 32 || *(unsigned long long*)$source_ctx == $worker
    set $event = $event + 1
    printf "EVENT=%u SOURCE_ALLOC_LIMIT_WRITE count=%u context=%p pc=%p ptr=%p new_limit=%p\n",$event,$src_limit_writes,$source_ctx,$pc,*(unsigned long long*)$source_ctx,*(unsigned long long*)($source_ctx+8)
    x/6i $pc-8
    printf "SOURCE_ALLOC_LIMIT_WRITE_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
    bt 18
   end
   continue
  end
 end
 continue
end

# Native scheduler worker TCB/TLS creation.
break *0x162120
commands
 silent
 printf "EVENT=%u SCHED_CREATE_SUSPENDED_ENTRY pc=%p scheduler=%p entry=%p argument=%p handle_out=%p thread_out=%p g_scheduler=%p return=%p\n",$event,$pc,$rcx,$rdx,$r8,$r9,*(unsigned long long*)($rsp+0x28),*(unsigned long long*)0x2a2c00,*(unsigned long long*)$rsp
 printf "SCHED_CREATE_SUSPENDED_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11
 bt 18
 continue
end

break *0x161210
commands
 silent
 if *(unsigned int*)($rcx+0xc) == 1
  set $worker_tcb = $rcx
 end
 printf "EVENT=%u SCHED_THREAD_ENVIRONMENT_ALLOC_ENTRY pc=%p tcb=%p identity=%u state=%u live=%u gs=%p tls_vector=%p tls_block=%p return=%p\n",$event,$pc,$rcx,*(unsigned int*)($rcx+4),*(unsigned int*)($rcx+0xc),*(unsigned char*)$rcx,*(unsigned long long*)($rcx+0x1a8),*(unsigned long long*)($rcx+0x1b0),*(unsigned long long*)($rcx+0x1b8),*(unsigned long long*)$rsp
 bt 16
 continue
end

# Worker context configure entry. This captures source and destination before
# the destination zero/copy loops and arms first-write watches dynamically.
break *0x15d600
commands
 silent
 if $configure_seen == 0
  set $configure_seen = 1
  set $worker_tcb = *(unsigned long long*)($rcx+0x40)
  set $source_block = $r8
  set $source_ctx = $source_block + 0x38
  set $destination_block = *(unsigned long long*)($worker_tcb+0x1b8)
  set $destination_ctx = $destination_block + 0x38
  set $event = $event + 1
  printf "EVENT=%u WORKER_TLS_CONFIG_ENTRY pc=%p worker_context=%p worker_tcb=%p worker_identity=%u worker_state=%u source_block=%p source_context=%p source_ptr=%p source_limit=%p source_is_main=%d destination_block=%p destination_context=%p destination_ptr=%p destination_limit=%p tls_index=%u source_return=%p\n",$event,$pc,$rcx,$worker_tcb,*(unsigned int*)($worker_tcb+4),*(unsigned int*)($worker_tcb+0xc),$source_block,$source_ctx,*(unsigned long long*)$source_ctx,*(unsigned long long*)($source_ctx+8),($source_block == *(unsigned long long*)0x1968f8),$destination_block,$destination_ctx,*(unsigned long long*)$destination_ctx,*(unsigned long long*)($destination_ctx+8),*(unsigned int*)0x__TLS_INDEX_CELL__,*(unsigned long long*)$rsp
  printf "WORKER_TLS_CONFIG_ENTRY_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11
  x/12gx $source_ctx-8
  x/12gx $destination_ctx-8
  bt 20
  watch *(unsigned long long*)$destination_ctx
  commands
   silent
   set $dst_ptr_writes = $dst_ptr_writes + 1
   if $dst_ptr_writes <= 16
    set $event = $event + 1
    printf "EVENT=%u DESTINATION_ALLOC_PTR_WRITE count=%u context=%p pc=%p new_ptr=%p limit=%p source_ptr=%p source_limit=%p\n",$event,$dst_ptr_writes,$destination_ctx,$pc,*(unsigned long long*)$destination_ctx,*(unsigned long long*)($destination_ctx+8),*(unsigned long long*)$source_ctx,*(unsigned long long*)($source_ctx+8)
    x/8i $pc-8
    printf "DESTINATION_ALLOC_PTR_WRITE_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
    bt 18
   end
   continue
  end
  watch *(unsigned long long*)($destination_ctx+8)
  commands
   silent
   set $dst_limit_writes = $dst_limit_writes + 1
   if $dst_limit_writes <= 16
    set $event = $event + 1
    printf "EVENT=%u DESTINATION_ALLOC_LIMIT_WRITE count=%u context=%p pc=%p ptr=%p new_limit=%p source_ptr=%p source_limit=%p\n",$event,$dst_limit_writes,$destination_ctx,$pc,*(unsigned long long*)$destination_ctx,*(unsigned long long*)($destination_ctx+8),*(unsigned long long*)$source_ctx,*(unsigned long long*)($source_ctx+8)
    x/8i $pc-8
    printf "DESTINATION_ALLOC_LIMIT_WRITE_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
    bt 18
   end
   continue
  end
 end
 continue
end

# The explicit byte-copy loop stores destination TLS bytes at 0x15d6f0 and
# advances the source/destination index at 0x15d6f4. These two indices finish
# the ptr and limit qwords at block offsets 0x3f and 0x47.
break *0x15d6f4
commands
 silent
 if $configure_seen && ($rdx == 0x3f || $rdx == 0x47)
  set $event = $event + 1
  printf "EVENT=%u WORKER_TLS_COPY_FIELD_COMPLETE pc=%p index=0x%X source_block=%p destination_block=%p source_context=%p destination_context=%p source_ptr=%p source_limit=%p destination_ptr=%p destination_limit=%p copied_byte=%p\n",$event,$pc,$rdx,$r8,$rcx,$source_ctx,$destination_ctx,*(unsigned long long*)$source_ctx,*(unsigned long long*)($source_ctx+8),*(unsigned long long*)$destination_ctx,*(unsigned long long*)($destination_ctx+8),$r9
  x/8i $pc-8
  printf "WORKER_TLS_COPY_FIELD_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11
  bt 16
 end
 continue
end

break *0x162b40
commands
 silent
 printf "EVENT=%u SCHED_RESUME_THREAD_ENTRY pc=%p handle=%p previous_suspend_out=%p scheduler=%p current_tcb=%p worker_tcb=%p worker_tls=%p return=%p\n",$event,$pc,$rcx,$rdx,*(unsigned long long*)0x2a2c00,*(unsigned long long*)(*(unsigned long long*)0x2a2c00+0x20e8),$worker_tcb,*(unsigned long long*)($worker_tcb+0x1b8),*(unsigned long long*)$rsp
 bt 16
 continue
end

# Every scheduler context switch logs both the old save slot and new context,
# with the pre-switch GS/TLS identity and the scheduler's current/boot TCB.
break *0x164500
commands
 silent
 set $switch_count = $switch_count + 1
 if $switch_count <= 80
  set $event = $event + 1
  set $pre_vector = *(unsigned long long*)($gs_base+0x58)
  set $pre_tls = *(unsigned long long*)$pre_vector
  set $pre_context = $pre_tls + 0x38
  set $sched = *(unsigned long long*)0x2a2c00
  set $current_tcb = *(unsigned long long*)($sched+0x20e8)
  set $boot_tcb = *(unsigned long long*)($sched+0x20f0)
  set $new_tcb = $rdx - 0x40
  set $new_identity = *(unsigned int*)($new_tcb+4)
  set $new_state = *(unsigned int*)($new_tcb+0xc)
  set $new_gs = *(unsigned long long*)($new_tcb+0x1a8)
  set $new_tls = *(unsigned long long*)($new_tcb+0x1b8)
  set $current_identity = *(unsigned int*)($current_tcb+4)
  set $current_state = *(unsigned int*)($current_tcb+0xc)
  set $boot_identity = *(unsigned int*)($boot_tcb+4)
  printf "EVENT=%u SCHED_CONTEXT_SWITCH count=%u pc=%p old_slot=%p old_saved=%p new_context=%p new_tcb=%p new_identity=%u new_state=%u new_gs=%p new_tls=%p current_tcb=%p current_identity=%u current_state=%u boot_tcb=%p boot_identity=%u pre_gs=%p pre_vector=%p pre_tls=%p pre_context=%p return=%p\n",$event,$switch_count,$pc,$rcx,*(unsigned long long*)$rcx,$rdx,$new_tcb,$new_identity,$new_state,$new_gs,$new_tls,$current_tcb,$current_identity,$current_state,$boot_tcb,$boot_identity,$gs_base,$pre_vector,$pre_tls,$pre_context,*(unsigned long long*)$rsp
  printf "SCHED_CONTEXT_SWITCH_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
  bt 18
 end
 continue
end

# After context_switch writes the new GS base, this is the worker's first
# execution point. It records the active scheduler/TLS context identity.
break *0x164fc0
commands
 silent
 set $event = $event + 1
 set $active_vector = *(unsigned long long*)($gs_base+0x58)
 set $active_tls = *(unsigned long long*)$active_vector
 set $active_context = $active_tls + 0x38
 set $sched = *(unsigned long long*)0x2a2c00
 set $current_tcb = *(unsigned long long*)($sched+0x20e8)
 set $current_identity = *(unsigned int*)($current_tcb+4)
 printf "EVENT=%u SCHED_WORKER_START pc=%p worker_tcb=%p worker_identity=%u gs_base=%p vector=%p tls_block=%p tls_context=%p ptr=%p limit=%p current_tcb=%p current_identity=%u return=%p\n",$event,$pc,$worker_tcb,*(unsigned int*)($worker_tcb+4),$gs_base,$active_vector,$active_tls,$active_context,*(unsigned long long*)$active_context,*(unsigned long long*)($active_context+8),$current_tcb,$current_identity,*(unsigned long long*)$rsp
 bt 18
 continue
end

# Worker managed allocation/publication correlation.
break *0x__RHP_NEW_FAST__
commands
 silent
 if $rcx == 0x__WORKER_EETYPE__
  set $event = $event + 1
  printf "EVENT=%u WORKER_ALLOCATION_ENTRY pc=%p rva=0x148a20 eetype=%p return=%p rsp=%p active_gs=%p\n",$event,$pc,$rcx,*(unsigned long long*)$rsp,$rsp,$gs_base
  printf "WORKER_ALLOCATION_ENTRY_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
  bt 20
 end
 continue
end

break *0x__RHP_CONTEXT_READY__
commands
 silent
 if $rcx == 0x__WORKER_EETYPE__
  set $worker_alloc_ctx = $rdx + 8
  set $event = $event + 1
  printf "EVENT=%u WORKER_CONTEXT_BEFORE pc=%p context=%p context_minus_8=%p tls_block=%p ptr_before=%p limit_before=%p expected_after=%p worker_tcb=%p worker_tls=%p active_gs=%p return=%p\n",$event,$pc,$worker_alloc_ctx,$rdx,$rax,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),*(unsigned long long*)$worker_alloc_ctx+*(unsigned int*)($rcx+4),$worker_tcb,*(unsigned long long*)($worker_tcb+0x1b8),$gs_base,*(unsigned long long*)$rsp
  x/8gx $worker_alloc_ctx-8
  bt 18
 end
 continue
end

break *0x__RHP_ALLOC_PTR_STORE__
commands
 silent
 if $worker_alloc_ctx && $rcx == 0x__WORKER_EETYPE__
  set $worker = $rax
  set $event = $event + 1
  printf "EVENT=%u WORKER_ALLOC_PTR_COMMIT pc=%p rva=0x148a54 worker=%p old_ptr=%p new_ptr=%p limit=%p context=%p\n",$event,$pc,$rax,*(unsigned long long*)$worker_alloc_ctx,$r8,*(unsigned long long*)($worker_alloc_ctx+8),$worker_alloc_ctx
  continue
 end
 continue
end

break *0x__RHP_AFTER_ALLOC__
commands
 silent
 if $worker && $rax == $worker
  set $worker_alloc_seen = 1
  set $event = $event + 1
  printf "EVENT=%u WORKER_ALLOCATION_AFTER pc=%p worker=%p worker_context=%p ptr_after=%p limit_after=%p destination_context=%p destination_ptr=%p destination_limit=%p\n",$event,$pc,$worker,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),$destination_ctx,*(unsigned long long*)$destination_ctx,*(unsigned long long*)($destination_ctx+8)
  x/8gx $worker
  bt 20
 end
 continue
end

break *0x__WORKER_CALL_RETURN__
commands
 silent
 if $worker_alloc_seen && $rax == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_MANAGED_CALL_RETURN pc=%p worker=%p worker_context=%p ptr=%p limit=%p\n",$event,$pc,$worker,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8)
  bt 16
 end
 continue
end

break *0x__RHP_ASSIGN_REF__
commands
 silent
 if $worker_alloc_seen && $rcx == 0x__ROOT_SLOT__ && $rdx == $worker
  set $publication_seen = 1
  set $event = $event + 1
  printf "EVENT=%u WORKER_PUBLICATION_BEFORE pc=%p destination=%p value=%p worker=%p worker_context=%p ptr=%p limit=%p root_before=%p\n",$event,$pc,$rcx,$rdx,$worker,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),*(unsigned long long*)0x__ROOT_SLOT__
  bt 18
 end
 continue
end

break *0x__RHP_ASSIGN_REF_AFTER_STORE__
commands
 silent
 if $publication_seen && *(unsigned long long*)0x__ROOT_SLOT__ == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_PUBLICATION_AFTER pc=%p root=%p worker=%p worker_header=%p worker_context=%p ptr=%p limit=%p\n",$event,0x__ROOT_SLOT__,*(unsigned long long*)0x__ROOT_SLOT__,$worker,*(unsigned long long*)$worker,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8)
  bt 18
 end
 continue
end

# Candidate static direct site retained for comparison with the live indirect
# path. It was not reached in the authoritative handoff boots.
break *0x__FIX_CALL_SITE__
commands
 silent
 if $worker_alloc_seen
  set $event = $event + 1
  set $active_vector = *(unsigned long long*)($gs_base+0x58)
  set $active_tls = *(unsigned long long*)$active_vector
  set $active_context = $active_tls + 0x38
  set $sched = *(unsigned long long*)0x2a2c00
  set $current_tcb = *(unsigned long long*)($sched+0x20e8)
  set $current_identity = *(unsigned int*)($current_tcb+4)
  set $current_state = *(unsigned int*)($current_tcb+0xc)
  printf "EVENT=%u FIX_CONTEXT_SELECTION_CALLSITE pc=%p rva=0x185dab context_arg_rcx=%p source_rsi=%p ptr=%p limit=%p worker=%p root=%p current_gs=%p current_vector=%p current_tls=%p current_context=%p current_tcb=%p current_identity=%u current_state=%u return=%p\n",$event,$pc,$rcx,$rsi,*(unsigned long long*)$rcx,*(unsigned long long*)($rcx+8),$worker,*(unsigned long long*)0x__ROOT_SLOT__,$gs_base,$active_vector,$active_tls,$active_context,$current_tcb,$current_identity,$current_state,*(unsigned long long*)$rsp
  x/8i $pc-8
  bt 24
 end
 continue
end

# NativeAOT's allocation-context enumeration helper derives the callback
# context from the current registration record. Capture both the record+8
# selection and the indirect callback call before the wrapper receives it.
break *0x__FIX_CONTEXT_ENUMERATION_SELECT__
commands
 silent
 if $worker_alloc_seen
  set $event = $event + 1
  printf "EVENT=%u FIX_CONTEXT_ENUMERATION_SELECT pc=%p record_base=%p selected_context=%p record_qword0=%p record_qword1=%p ptr=%p limit=%p callback=%p arg_record=%p worker=%p root=%p return=%p\n",$event,$pc,$rbx,$rbx+8,*(unsigned long long*)$rbx,*(unsigned long long*)($rbx+8),*(unsigned long long*)($rbx+0x10),$rbp,$rsi,$worker,*(unsigned long long*)0x__ROOT_SLOT__,$rsp
  x/8i $pc-8
  bt 24
 end
 continue
end

break *0x__FIX_CONTEXT_ENUMERATION_CALL__
commands
 silent
 if $worker_alloc_seen
  set $event = $event + 1
  printf "EVENT=%u FIX_CONTEXT_ENUMERATION_CALL pc=%p record_base=%p selected_context=%p callback=%p arg_record=%p context_ptr=%p context_limit=%p target_slot=%p target=%p return=%p\n",$event,$pc,$rbx,$rcx,$rbp,$rsi,*(unsigned long long*)$rcx,*(unsigned long long*)($rcx+8),0x__FIX_INTERNAL_TARGET_SLOT__,*(unsigned long long*)0x__FIX_INTERNAL_TARGET_SLOT__,$rsp
  x/8i $pc-8
  bt 24
 end
 continue
end

# The indirect dispatch target lands in the small NativeAOT wrapper at RVA
# 0x15f420. Its mov rcx,rax at RVA 0x15f42a is the exact context pass into the
# tail-jump to FixAllocContext.
break *0x__FIX_CONTEXT_PASS__
commands
 silent
 if $worker_alloc_seen
  set $event = $event + 1
  printf "EVENT=%u FIX_CONTEXT_PASS pc=%p rax=%p source_rdx=%p prior_rcx=%p r8=%p r9=%p worker=%p root=%p return=%p\n",$event,$pc,$rax,$rdx,$rcx,$r8,$r9,$worker,*(unsigned long long*)0x__ROOT_SLOT__,*(unsigned long long*)$rsp
  x/8i $pc-16
  printf "FIX_CONTEXT_PASS_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
  bt 24
 end
 continue
end

# The internal wrapper receives the selected context in RCX and a separate
# argument record in RDX. Capture this boundary before the wrapper
# loads the fields and moves the context into the indirect-call ABI registers.
break *0x__FIX_INTERNAL_ENTRY__
commands
 silent
 if $worker_alloc_seen
  set $event = $event + 1
  set $active_vector = *(unsigned long long*)($gs_base+0x58)
  set $active_tls = *(unsigned long long*)$active_vector
  set $active_context = $active_tls + 0x38
  set $sched = *(unsigned long long*)0x2a2c00
  set $current_tcb = *(unsigned long long*)($sched+0x20e8)
  set $current_identity = *(unsigned int*)($current_tcb+4)
  set $current_state = *(unsigned int*)($current_tcb+0xc)
  printf "EVENT=%u FIX_INTERNAL_ENTRY pc=%p incoming_context_rcx=%p incoming_arg_rdx=%p incoming_ptr=%p incoming_limit=%p arg_record_dword0=0x%X arg_record_qword1=%p worker=%p root=%p active_gs=%p active_vector=%p active_tls=%p active_context=%p current_tcb=%p current_identity=%u current_state=%u return=%p\n",$event,$pc,$rcx,$rdx,*(unsigned long long*)$rcx,*(unsigned long long*)($rcx+8),*(unsigned int*)$rdx,*(unsigned long long*)($rdx+8),$worker,*(unsigned long long*)0x__ROOT_SLOT__,$gs_base,$active_vector,$active_tls,$active_context,$current_tcb,$current_identity,$current_state,*(unsigned long long*)$rsp
  x/12i $pc
  printf "FIX_INTERNAL_ENTRY_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
  bt 24
 end
 continue
end

# The actual Phase 53K/L endpoint is reached through the indirect runtime call
# at RVA 0x172e12. The preceding instructions move the selected context into
# the call argument registers. Capture that boundary, including the IAT target,
# before FixAllocContext receives the context.
break *0x__FIX_INTERNAL_CALL__
commands
 silent
 if $worker_alloc_seen
  set $fix_pre_call_count = $fix_pre_call_count + 1
  set $event = $event + 1
  printf "EVENT=%u FIX_INTERNAL_CALL_PRE count=%u pc=%p target_slot=%p target=%p rcx=%p rdx=%p r8=%p r9=%p r10=%p r11=%p rsi=%p rdi=%p worker=%p root=%p return=%p\n",$event,$fix_pre_call_count,$pc,0x__FIX_INTERNAL_TARGET_SLOT__,*(unsigned long long*)0x__FIX_INTERNAL_TARGET_SLOT__,$rcx,$rdx,$r8,$r9,$r10,$r11,$rsi,$rdi,$worker,*(unsigned long long*)0x__ROOT_SLOT__,*(unsigned long long*)$rsp
  x/8i $pc-32
  printf "FIX_INTERNAL_CALL_PRE_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
  bt 24
 end
 continue
end

break *0x__FIX_ALLOCATION_CONTEXT__
commands
 silent
 if $worker_alloc_seen
  set $fix_seen = 1
  set $event = $event + 1
  set $active_vector = *(unsigned long long*)($gs_base+0x58)
  set $active_tls = *(unsigned long long*)$active_vector
  set $active_context = $active_tls + 0x38
  set $sched = *(unsigned long long*)0x2a2c00
  set $current_tcb = *(unsigned long long*)($sched+0x20e8)
  set $current_identity = *(unsigned int*)($current_tcb+4)
  set $current_state = *(unsigned int*)($current_tcb+0xc)
  printf "EVENT=%u FIX_ALLOCATION_CONTEXT_ENTRY pc=%p rva=0x172e20 context=%p ptr=%p limit=%p worker=%p source_context=%p destination_context=%p caller_return=%p active_gs=%p active_vector=%p active_tls=%p active_context=%p current_tcb=%p current_identity=%u current_state=%u root=%p\n",$event,$pc,$rcx,*(unsigned long long*)$rcx,*(unsigned long long*)($rcx+8),$worker,$source_ctx,$destination_ctx,*(unsigned long long*)$rsp,$gs_base,$active_vector,$active_tls,$active_context,$current_tcb,$current_identity,$current_state,*(unsigned long long*)0x__ROOT_SLOT__
  x/12i $pc
  bt 24
 end
 continue
end

# At this instruction RDI is the original context retained by
# fix_allocation_context and RBX is the object being passed to SetFree.
break *0x__FIX_ALLOCATION_SETFREE__
commands
 silent
  if $worker_alloc_seen && $rbx == $worker
   set $target_captured = 1
   set $event = $event + 1
   set $active_vector = *(unsigned long long*)($gs_base+0x58)
   set $active_tls = *(unsigned long long*)$active_vector
   set $active_context = $active_tls + 0x38
   set $sched = *(unsigned long long*)0x2a2c00
   set $current_tcb = *(unsigned long long*)($sched+0x20e8)
   set $current_identity = *(unsigned int*)($current_tcb+4)
   set $current_state = *(unsigned int*)($current_tcb+0xc)
  printf "EVENT=%u FIX_PRE_SETFREE pc=%p rva=0x172e95 consumer_context=%p original_context=%p same_as_source=%d same_as_destination=%d ptr=%p limit=%p worker=%p object=%p size=%p caller_return=%p active_gs=%p active_vector=%p active_tls=%p active_context=%p current_tcb=%p current_identity=%u current_state=%u root=%p\n",$event,$pc,$rdi,$rdi,$rdi == $source_ctx,$rdi == $destination_ctx,*(unsigned long long*)$rdi,*(unsigned long long*)($rdi+8),$worker,$rbx,$r15,*(unsigned long long*)$rsp,$gs_base,$active_vector,$active_tls,$active_context,$current_tcb,$current_identity,$current_state,*(unsigned long long*)0x__ROOT_SLOT__
  x/24gx $rdi
  x/32gx $rsp-0x20
  x/16i $pc-16
  bt 28
  printf "TARGETED_PHASE53M_FIX_PRE_SETFREE_CAPTURED context=%p worker=%p ptr=%p source_context=%p destination_context=%p\n",$rdi,$worker,*(unsigned long long*)$rdi,$source_ctx,$destination_ctx
  detach
  quit
 end
 continue
end

continue
printf "PHASE53M_NO_TARGETED_FIX_PRE_SETFREE\n"
detach
quit
'@

    foreach ($item in @(
        @('__GDB_PORT__', $gdbPort),
        @('__RHP_NEW_FAST__', $rhpNewFast),
        @('__RHP_CONTEXT_READY__', $rhpContextReady),
        @('__RHP_ALLOC_PTR_STORE__', $rhpAllocPtrStore),
        @('__RHP_AFTER_ALLOC__', $rhpAfterAlloc),
        @('__RHP_ASSIGN_REF__', $rhpAssignRef),
        @('__RHP_ASSIGN_REF_AFTER_STORE__', $rhpAssignRefAfterStore),
        @('__WORKER_CALL_RETURN__', $workerCallReturn),
        @('__WORKER_EETYPE__', $workerEeTypeAddress),
        @('__TLS_INDEX_CELL__', $tlsIndexCell),
        @('__ROOT_SLOT__', $rootSlot),
        @('__FIX_CALL_SITE__', $fixCallSite),
        @('__FIX_INTERNAL_ENTRY__', $fixInternalEntry),
        @('__FIX_INTERNAL_CALL__', $fixInternalCall),
        @('__FIX_INTERNAL_TARGET_SLOT__', $fixInternalTargetSlot),
        @('__FIX_CONTEXT_ENUMERATION_SELECT__', $fixContextEnumerationSelect),
        @('__FIX_CONTEXT_ENUMERATION_CALL__', $fixContextEnumerationCall),
        @('__FIX_CONTEXT_PASS__', $fixContextPass),
        @('__FIX_ALLOCATION_CONTEXT__', $fixAllocationContext),
        @('__FIX_ALLOCATION_SETFREE__', $fixAllocationSetFree)
    )) {
        $replacement = if ($item[0] -eq '__GDB_PORT__') {
            [string][int]$item[1]
        } elseif ($item[1] -is [string]) {
            [string]$item[1]
        } else {
            '{0:X}' -f [uint64]$item[1]
        }
        $gdbText = $gdbText.Replace($item[0], $replacement)
    }
    [IO.File]::WriteAllText($gdbScriptPath, $gdbText, [Text.Encoding]::ASCII)
    @(
        ('IMAGE_BASE=0x{0:X}' -f $imageBase),
        ('WORKER_EETYPE=0x{0:X}' -f $workerEeTypeAddress),
        ('WORKER_EETYPE_HISTORICAL_PHASE53=0x{0:X}' -f $expectedWorkerEeType),
        ('WORKER_EETYPE_RVA=0x{0:X}' -f $expectedWorkerRva),
        'WORKER_MANAGED_SIZE=0x38',
        'WORKER_NATIVEAOT_BASE_SIZE=0x40',
        'WORKER_ALIGNMENT=0x8',
        'NATIVE_CONFIGURE=0x15D600',
        'NATIVE_TLS_COPY_STORE=0x15D6F0',
        'NATIVE_SCHEDULER_CREATE=0x162120',
        'NATIVE_SCHEDULER_ENVIRONMENT=0x161210',
        'NATIVE_SCHEDULER_RESUME=0x162B40',
        'NATIVE_SCHEDULER_CONTEXT_SWITCH=0x164500',
        'NATIVE_SCHEDULER_WORKER_START=0x164FC0',
        ('RHP_NEW_FAST=0x{0:X} RVA=0x148A20' -f $rhpNewFast),
        ('RHP_ALLOC_PTR_STORE=0x{0:X} RVA=0x148A54' -f $rhpAllocPtrStore),
        ('RHP_ASSIGN_REF=0x{0:X} RVA=0x148C80' -f $rhpAssignRef),
        ('FIX_CONTEXT_SELECTION_CALLSITE=0x{0:X} RVA=0x185DAB' -f $fixCallSite),
        ('FIX_INTERNAL_ENTRY=0x{0:X} RVA=0x172DF0' -f $fixInternalEntry),
        ('FIX_INTERNAL_CALL=0x{0:X} RVA=0x172E12' -f $fixInternalCall),
        ('FIX_INTERNAL_TARGET_SLOT=0x{0:X} RVA=0x195448' -f $fixInternalTargetSlot),
        ('FIX_CONTEXT_ENUMERATION_SELECT=0x{0:X} RVA=0x152E73' -f $fixContextEnumerationSelect),
        ('FIX_CONTEXT_ENUMERATION_CALL=0x{0:X} RVA=0x152E7A' -f $fixContextEnumerationCall),
        ('FIX_CONTEXT_PASS=0x{0:X} RVA=0x15F42A' -f $fixContextPass),
        ('FIX_ALLOCATION_CONTEXT=0x{0:X} RVA=0x172E20' -f $fixAllocationContext),
        ('FIX_ALLOCATION_SETFREE=0x{0:X} RVA=0x172E95' -f $fixAllocationSetFree),
        ('ROOT_SLOT=0x{0:X}' -f $rootSlot),
        ('TLS_INDEX_CELL=0x{0:X} RVA=0x48A074' -f $tlsIndexCell),
        ('PAYLOAD_PATH={0}' -f $payloadPath),
        ('PAYLOAD_SHA256={0}' -f $payloadHash),
        ('PDB_PATH={0}' -f $pdbPath),
        ('PDB_SHA256={0}' -f $pdbHash),
        'CAPTURE_MODE=single deterministic QEMU boot; native EFI + historical managed payload'
    ) | Set-Content -LiteralPath (Join-Path $out 'breakpoints.txt') -Encoding ascii

    $gdbProcess = Start-Process -FilePath $gdb -ArgumentList @('-q', '-batch', '-x', $gdbScriptPath) -RedirectStandardOutput $gdbOutPath -RedirectStandardError $gdbErrPath -PassThru -WindowStyle Hidden
    $injectionFile = [IO.StreamWriter]::new($injectionPath, $false, [Text.Encoding]::ASCII)
    function Record-Injection([string]$name) {
        $injectionFile.WriteLine(('{0:o} {1}' -f [DateTime]::UtcNow, $name))
        $injectionFile.Flush()
    }

    $gdbDeadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    while (!$gdbProcess.HasExited -and [DateTime]::UtcNow -lt $gdbDeadline) {
        while ($serialStream.DataAvailable) {
            $count = $serialStream.Read($serialBuffer, 0, $serialBuffer.Length)
            if ($count -le 0) { break }
            $serialFile.Write($serialBuffer, 0, $count)
            [void]$serialText.Append([Text.Encoding]::ASCII.GetString($serialBuffer, 0, $count))
        }
        $currentSerial = $serialText.ToString()
        if (!$sentSerial52 -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY')) {
            Send-RawByte $serialClient 0x52
            $sentSerial52 = $true
            Record-Injection 'SERIAL_READY byte=0x52'
        }
        if (!$sentSerial53 -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY')) {
            Send-RawByte $serialClient 0x53
            $sentSerial53 = $true
            Record-Injection 'SERIAL_SECOND_READY byte=0x53'
        }
        if (!$sentKeyA -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY')) {
            Send-Line $monitorClient 'sendkey a'
            $sentKeyA = $true
            Record-Injection 'KEYBOARD_INPUT_READY key=a'
        }
        if (!$sentKeyB -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY')) {
            Send-Line $monitorClient 'sendkey b'
            $sentKeyB = $true
            Record-Injection 'KEYBOARD_SECOND_INPUT_READY key=b'
        }
        if (!$sentKeyC -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_READY')) {
            Send-Line $monitorClient 'sendkey c'
            $sentKeyC = $true
            Record-Injection 'KEYBOARD_UNSUBSCRIBED_READY key=c'
        }
        if (!$sentBurst -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK')) {
            Send-RawByte $serialClient 0x44
            Start-Sleep -Milliseconds 50
            Send-RawByte $serialClient 0x45
            Start-Sleep -Milliseconds 50
            Send-RawByte $serialClient 0x46
            $sentBurst = $true
            Record-Injection 'KEYBOARD_B_SENT bytes=0x44,0x45,0x46'
        }
        Start-Sleep -Milliseconds 25
    }
    if (!$gdbProcess.HasExited) {
        $partial = if (Test-Path -LiteralPath $gdbOutPath) { Get-Content -LiteralPath $gdbOutPath -Raw } else { '' }
        if ($partial -match '(?m)^EVENT=.*WORKER_TLS_CONFIG_ENTRY') { $captureStatus = 'INCOMPLETE_TLS_CONFIG_WITHOUT_FIX' }
        elseif ($partial -match '(?m)^EVENT=.*WORKER_ALLOCATION_ENTRY') { $captureStatus = 'INCOMPLETE_WORKER_ENTRY_WITHOUT_FIX' }
        else { $captureStatus = 'INCOMPLETE_GDB_TIMEOUT' }
        Stop-Process -Id $gdbProcess.Id -Force -ErrorAction SilentlyContinue
        throw "GDB timed out before Phase 53M completed (status $captureStatus)."
    }
    $gdbProcess.WaitForExit()
    $gdbOutput = Get-Content -LiteralPath $gdbOutPath -Raw
    if ($gdbOutput -match '(?m)^TARGETED_PHASE53M_FIX_PRE_SETFREE_CAPTURED') {
        $captureStatus = 'PHASE53M_HANDOFF_AND_FIX_CAPTURED'
    } elseif ($gdbOutput -match '(?m)^PHASE53M_NO_TARGETED_FIX_PRE_SETFREE') {
        $captureStatus = 'PHASE53M_NO_TARGETED_FIX_PRE_SETFREE'
        throw 'GDB completed without the targeted FixAllocContext/SetFree endpoint.'
    } else {
        $captureStatus = 'GDB_COMPLETED_WITHOUT_PHASE53M_CLASSIFICATION'
        throw 'GDB completed without a Phase 53M completion marker.'
    }
} catch {
    if ($captureStatus -eq 'NOT_STARTED') { $captureStatus = 'ERROR' }
    Set-Content -LiteralPath (Join-Path $out 'capture-error.txt') -Value ($_ | Out-String) -Encoding utf8
    throw
} finally {
    if ($null -ne $serialStream) {
        while ($serialStream.DataAvailable) {
            $count = $serialStream.Read($serialBuffer, 0, $serialBuffer.Length)
            if ($count -le 0) { break }
            $serialFile.Write($serialBuffer, 0, $count)
        }
    }
    if ($null -ne $injectionFile) { $injectionFile.Dispose() }
    if ($null -ne $serialFile) { $serialFile.Dispose() }
    if ($null -ne $serialStream) { $serialStream.Dispose() }
    if ($null -ne $serialClient) { $serialClient.Dispose() }
    if ($null -ne $monitorClient) {
        try { Send-Line $monitorClient 'quit' } catch { }
        $monitorClient.Dispose()
    }
    if ($null -ne $peerUdp) { $peerUdp.Dispose() }
    if ($null -ne $process) {
        try { if (!$process.HasExited) { $process.WaitForExit(5000) | Out-Null } } catch { }
        try { if (!$process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } } catch { }
        try { $process.WaitForExit(5000) | Out-Null } catch { }
    }
    @(
        "status=$captureStatus",
        ('qemu_pid={0}' -f $(if ($null -eq $process) { 0 } else { $process.Id })),
        ('gdb_pid={0}' -f $(if ($null -eq $gdbProcess) { 0 } else { $gdbProcess.Id })),
        ('image_base=0x{0:X}' -f $imageBase),
        ('payload_sha256={0}' -f $payloadHash),
        ('payload_size={0}' -f (Get-Item -LiteralPath $payloadPath).Length),
        ('pdb_sha256={0}' -f $pdbHash),
        ('pdb_size={0}' -f (Get-Item -LiteralPath $pdbPath).Length),
        ('ovmf_code_sha256={0}' -f (Get-FileHash -LiteralPath $code -Algorithm SHA256).Hash.ToUpperInvariant()),
        ('ovmf_vars_sha256={0}' -f (Get-FileHash -LiteralPath $vars -Algorithm SHA256).Hash.ToUpperInvariant()),
        ('serial_port={0}' -f $serialPort),
        ('monitor_port={0}' -f $monitorPort),
        ('gdb_port={0}' -f $gdbPort),
        ('rx_port={0}' -f $rxPort),
        ('peer_port={0}' -f $peerPort)
    ) | Set-Content -LiteralPath $summaryPath -Encoding ascii
}

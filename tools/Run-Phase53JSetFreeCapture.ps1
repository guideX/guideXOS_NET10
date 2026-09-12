[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\phase53j-setfree-capture-1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = Join-Path $repo 'artifacts\phase53g-corrected-gate'
$out = Join-Path $repo $OutputDirectory
$qemu = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
$gdb = 'C:\mingw64\bin\gdb.exe'
$share = Join-Path (Split-Path -Parent $qemu) 'share'
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
    try { $socket.Client.Bind([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0)); return ([Net.IPEndPoint]$socket.Client.LocalEndPoint).Port }
    finally { $socket.Dispose() }
}

function Connect-Tcp([int]$port, [int]$timeoutMs = 30000) {
    $client = [Net.Sockets.TcpClient]::new()
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $task = $client.ConnectAsync('127.0.0.1', $port)
            if ($task.Wait(250) -and $client.Connected) { $client.Client.NoDelay = $true; return $client }
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
$serialText = [Text.StringBuilder]::new()
$serialBuffer = New-Object byte[] 4096
$gdbProcess = $null
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
            $serialFile.Flush()
            [void]$serialText.Append([Text.Encoding]::ASCII.GetString($serialBuffer, 0, $count))
        }
        $match = [regex]::Match($serialText.ToString(), 'GXOS_NET10:IMAGE_BASE=0x([0-9A-Fa-f]+)')
        if ($match.Success) { $imageBase = [Convert]::ToUInt64($match.Groups[1].Value, 16); break }
        Start-Sleep -Milliseconds 10
    }
    if ($imageBase -eq 0) { throw 'IMAGE_BASE was not observed.' }
    Send-Line $monitorClient 'stop'
    Start-Sleep -Milliseconds 250

    $runEntry = $imageBase + 0x619DC
    $runEnd = $imageBase + 0x61B24
    $gcCall = $imageBase + 0x61A4A
    $gcReturn = $imageBase + 0x61A4F
    $workerEntry = $imageBase + 0x5E7C0
    $workerCall = $imageBase + 0x5E809
    $runWorkerExport = $imageBase + 0x894A0
    $runWorkerHelper = $imageBase + 0xF8138
    $staticStateSlot = $imageBase + 0x47C410

    $setFree = $imageBase + 0x1617B0
    $setFreeAfterHeader = $imageBase + 0x1617C0
    $setFreeAfterPayload = $imageBase + 0x1617D3
    $sohTryFit = $imageBase + 0x185B80
    $fixAllocContext = $imageBase + 0x172DF0
    $fixAllocationContext = $imageBase + 0x172E20
    $fixAllocationSetFree1 = $imageBase + 0x172E95
    $fixAllocationSetFree2 = $imageBase + 0x172F73
    $memcopy = $imageBase + 0x17D3A0
    $memcopyHeaderInstruction = $imageBase + 0x17D3BA
    $memcopyDispatcherInstruction = $imageBase + 0x17D3C2
    $memcopyAfter = $imageBase + 0x17D408
    $relocateAddress = $imageBase + 0x1826A0
    $relocateAddressAfter1 = $imageBase + 0x1827AF
    $relocateAddressAfter2 = $imageBase + 0x1827C4
    $garbageCollect = $imageBase + 0x173130
    $gc1 = $imageBase + 0x173920
    $markPhase = $imageBase + 0x17ACC0
    $planPhase = $imageBase + 0x17E0D0
    $relocateSurvivors = $imageBase + 0x1840D0
    $compactPhase = $imageBase + 0x16B990
    $sweepRegion = $imageBase + 0x1861E0
    $makeFreeLists = $imageBase + 0x179710
    $findFirstValidRegion = $imageBase + 0x171FC0
    $compactPlug = $imageBase + 0x16BD20

    $rootSlot = [uint64]0x4000000008C0
    $destination = [uint64]0x400004C00118
    $runtimeGlobalBase = $imageBase + 0x486D00
    $freeEeType = $imageBase + 0x484E08

    $gdbText = @'
set pagination off
set confirm off
set architecture i386:x86-64
set can-use-hw-watchpoints 1
set $event = 0
set $worker = 0
set $worker_seen = 0
set $targeted_setfree = 0
set $copy_seen = 0
set $root_relocated = 0
set $context_watch_set = 0
set $memcopy_count = 0
set $soh_count = 0
set $last_phase = 0
set $setfree_count = 0
target remote 127.0.0.1:__GDB_PORT__

watch *(unsigned long long*)0x4000000008c0
commands
 silent
 set $event = $event + 1
 printf "EVENT=%u ROOT_WRITE pc=%p old_or_new=%p root_now=%p phase=%u\n",$event,$pc,$rax,*(unsigned long long*)0x4000000008c0,$last_phase
 x/4gx 0x4000000008b8
 if $worker != 0
  x/16gx $worker
 end
 x/16gx 0x400004c00118
 if $targeted_setfree && $copy_seen && *(unsigned long long*)0x4000000008c0 == 0x400004c00118
  set $root_relocated = 1
  printf "TARGETED_SEQUENCE_COMPLETE event=%u\n",$event
  detach
  quit
 end
 if $targeted_setfree && $copy_seen == 0 && *(unsigned long long*)0x4000000008c0 == 0x400004c00118
  set $root_relocated = 1
  printf "ROOT_RELOCATED_BEFORE_COPY_WAITING event=%u\n",$event
  disable 1
 end
 continue
end

break *0x__WORKER_ENTRY__
commands
 silent
 set $event = $event + 1
 set $worker = $rcx
 set $worker_seen = 1
 printf "EVENT=%u WORKER_ENTRY worker=%p rip=%p rsp=%p phase=%u root=%p\n",$event,$worker,$pc,$rsp,$last_phase,*(unsigned long long*)0x4000000008c0
 x/24gx $worker
 set $worker_eetype = *(unsigned long long*)$worker
 printf "WORKER_EETYPE address=%p descriptor_window\n",$worker_eetype
 x/32gx $worker_eetype-0x40
 x/96bx $worker_eetype-0x40
 x/8gx 0x4000000008b8
 continue
end

break *0x__SOH_TRY_FIT__
commands
 silent
 set $soh_count = $soh_count + 1
 if $soh_count <= 48 && $r8 >= 0x400000000000 && $r8 < 0x400010000000
  printf "SOH_TRY_FIT_ENTRY n=%u ctx_arg=%p ptr=%p limit=%p gen=%d request=%p\n",$soh_count,$r8,*(unsigned long long*)$r8,*(unsigned long long*)($r8+8),$ecx,$rdx
 end
 if $worker_seen && $r8 >= 0x400000000000 && $r8 < 0x400010000000 && *(unsigned long long*)$r8 == $worker
  set $event = $event + 1
  set $target_ctx = $r8
  printf "EVENT=%u SOH_TRY_FIT_TARGET ctx=%p worker=%p gen=%d request=%p alloc_ptr=%p alloc_limit=%p field10=%p field18=%p\n",$event,$target_ctx,$worker,$ecx,$rdx,$r8 == 0 ? 0 : *(unsigned long long*)$r8,*(unsigned long long*)($r8+8),*(unsigned long long*)($r8+0x10),*(unsigned long long*)($r8+0x18)
  x/16gx $r8
  if $context_watch_set == 0
   set $context_watch_set = 1
   watch *(unsigned long long*)$target_ctx
   commands
    silent
    set $event = $event + 1
    printf "EVENT=%u ALLOC_CONTEXT_PTR_WRITE ctx=%p pc=%p ptr_now=%p limit_now=%p field10=%p field18=%p\n",$event,$target_ctx,$pc,*(unsigned long long*)$target_ctx,*(unsigned long long*)($target_ctx+8),*(unsigned long long*)($target_ctx+0x10),*(unsigned long long*)($target_ctx+0x18)
    bt 12
    continue
   end
   watch *(unsigned long long*)($target_ctx+8)
   commands
    silent
    set $event = $event + 1
    printf "EVENT=%u ALLOC_CONTEXT_LIMIT_WRITE ctx=%p pc=%p ptr_now=%p limit_now=%p field10=%p field18=%p\n",$event,$target_ctx,$pc,*(unsigned long long*)$target_ctx,*(unsigned long long*)($target_ctx+8),*(unsigned long long*)($target_ctx+0x10),*(unsigned long long*)($target_ctx+0x18)
    bt 12
    continue
   end
  end
 end
 continue
end

break *0x__FIX_ALLOC_CONTEXT__
commands
 silent
 if $worker_seen && $rcx >= 0x400000000000 && $rcx < 0x400010000000 && *(unsigned long long*)$rcx == $worker
  set $event = $event + 1
  printf "EVENT=%u FIX_ALLOC_CONTEXT_TARGET ctx=%p ptr=%p limit=%p arg1=%p arg2=%p return=%p\n",$event,$rcx,*(unsigned long long*)$rcx,*(unsigned long long*)($rcx+8),$rdx,$r8,*(unsigned long long*)$rsp
  x/16gx $rcx
  bt 16
 end
 continue
end

break *0x__FIX_ALLOCATION_CONTEXT__
commands
 silent
 if $worker_seen && $rcx >= 0x400000000000 && $rcx < 0x400010000000 && *(unsigned long long*)$rcx == $worker
  set $event = $event + 1
  printf "EVENT=%u FIX_ALLOCATION_CONTEXT_TARGET ctx=%p ptr=%p limit=%p gen=%d flags=%d return=%p\n",$event,$rcx,*(unsigned long long*)$rcx,*(unsigned long long*)($rcx+8),$edx,$r8d,*(unsigned long long*)$rsp
  x/20gx $rcx
  bt 20
 end
 continue
end

break *0x__FIX_ALLOC_SETFREE_1__
commands
 silent
 if $worker_seen && $rbx == $worker
  set $event = $event + 1
  printf "EVENT=%u FIX_ALLOC_PRE_SETFREE site=1 pc=%p ctx=%p ptr=%p limit=%p size=%p rbx=%p rcx=%p rdx=%p r8=%p r15=%p phase=%u return=%p\n",$event,$pc,$rdi,*(unsigned long long*)$rdi,*(unsigned long long*)($rdi+8),$r15,$rbx,$rcx,$rdx,$r8,$r15,$last_phase,*(unsigned long long*)$rsp
  x/24gx $rdi
  x/32gx $rsp-0x20
  bt 24
 end
 continue
end

break *0x__FIX_ALLOC_SETFREE_2__
commands
 silent
 if $worker_seen && $rbx == $worker
  set $event = $event + 1
  printf "EVENT=%u FIX_ALLOC_PRE_SETFREE site=2 pc=%p ctx=%p ptr=%p limit=%p size=%p rbx=%p rcx=%p rdx=%p r8=%p r15=%p phase=%u return=%p\n",$event,$pc,$rdi,*(unsigned long long*)$rdi,*(unsigned long long*)($rdi+8),$r15,$rbx,$rcx,$rdx,$r8,$r15,$last_phase,*(unsigned long long*)$rsp
  x/24gx $rdi
  x/32gx $rsp-0x20
  bt 24
 end
 continue
end

break *0x__SETFREE__
commands
 silent
 set $setfree_count = $setfree_count + 1
 if $worker_seen && $rcx == $worker
  set $event = $event + 1
  set $targeted_setfree = 1
  printf "EVENT=%u TARGETED_SET_FREE_ENTRY this=%p size=%p payload=%p free_end=%p rip=%p rsp=%p rbp=%p rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p phase=%u return=%p call_site=%p\n",$event,$rcx,$rdx,$rdx-0x18,$rcx+$rdx,$pc,$rsp,$rbp,$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15,$last_phase,*(unsigned long long*)$rsp,*(unsigned long long*)$rsp-5
  printf "TARGETED_SET_FREE_PRE_BYTES\n"
  x/24gx $rcx-0x10
  printf "TARGETED_ROOT_AND_GLOBALS root_slot=%p root_value=%p free_eetype_cell=%p free_eetype=%p heap_global=%p heap_words\n",0x4000000008c0,*(unsigned long long*)0x4000000008c0,0x__FREE_EETYPE__,*(unsigned long long*)0x__FREE_EETYPE__,0x__RUNTIME_GLOBAL_BASE__
  x/16gx 0x__RUNTIME_GLOBAL_BASE__
  printf "TARGETED_STACK\n"
  x/96gx $rsp-0x40
  printf "TARGETED_BACKTRACE\n"
  bt 32
  printf "TARGETED_CALLER_CODE\n"
  x/20i (*(unsigned long long*)$rsp)-0x30
  monitor info cpus
 end
 continue
end

break *0x__SETFREE_AFTER_HEADER__
commands
 silent
 if $targeted_setfree && $rbx == $worker
  set $event = $event + 1
  printf "EVENT=%u TARGETED_SET_FREE_AFTER_HEADER pc=%p this=%p saved_rsi=%p\n",$event,$pc,$rbx,$rsi
  x/8gx $rbx
 end
 continue
end

break *0x__SETFREE_AFTER_PAYLOAD__
commands
 silent
 if $targeted_setfree && $rbx == $worker
  set $event = $event + 1
  printf "EVENT=%u TARGETED_SET_FREE_AFTER_PAYLOAD pc=%p this=%p header=%p payload=%p\n",$event,$pc,$rbx,*(unsigned long long*)$rbx,*(unsigned long long*)($rbx+8)
  x/24gx $rbx-0x10
 end
 continue
end

break *0x__MEMCOPY__
commands
 silent
 set $memcopy_count = $memcopy_count + 1
 if $targeted_setfree && $memcopy_count <= 24
  set $event = $event + 1
  printf "EVENT=%u MEMCOPY_ENTRY n=%u dest=%p src=%p size=%p rip=%p rsp=%p phase=%u return=%p\n",$event,$memcopy_count,$rcx,$rdx,$r8,$pc,$rsp,$last_phase,*(unsigned long long*)$rsp
  if ($rcx == 0x400004c00118) || ($rdx == $worker)
   set $copy_seen = 1
   printf "MEMCOPY_TARGETED\n"
   printf "MEMCOPY_SOURCE_BYTES\n"
   x/24gx $rdx-0x10
   printf "MEMCOPY_DEST_BEFORE\n"
   x/24gx $rcx-0x10
   bt 24
  end
 end
 continue
end

break *0x__MEMCOPY_HEADER_INSTRUCTION__
commands
 silent
 if $targeted_setfree && (($rcx == 0x400004c00118) || ($rdx == $worker))
  set $event = $event + 1
  set $copy_seen = 1
  printf "EVENT=%u MEMCOPY_HEADER_INSTRUCTION pc=%p dest=%p src=%p size=%p\n",$event,$pc,$rcx,$rdx,$r8
  x/8gx $rcx
  x/8gx $rdx
 end
 continue
end

break *0x__MEMCOPY_DISPATCHER_INSTRUCTION__
commands
 silent
 if $targeted_setfree && (($rcx == 0x400004c00118) || ($rdx == $worker))
  set $event = $event + 1
  set $copy_seen = 1
  printf "EVENT=%u MEMCOPY_DISPATCHER_INSTRUCTION pc=%p dest=%p src=%p remaining=%p loaded_src_plus8=%p dest_plus8_after=%p\n",$event,$pc,$rcx,$rdx,$r8,$rax,*(unsigned long long*)($rcx+8)
  x/8gx $rcx
  x/8gx $rdx
  if $root_relocated
   printf "TARGETED_SEQUENCE_COMPLETE event=%u\n",$event
   detach
   quit
  end
 end
 continue
end

break *0x__MEMCOPY_AFTER__
commands
 silent
 if $copy_seen
  set $event = $event + 1
  printf "EVENT=%u MEMCOPY_TARGETED_AFTER pc=%p source_head=%p dest_head=%p\n",$event,$pc,$worker,0x400004c00118
  x/24gx $worker-0x10
  x/24gx 0x400004c00118-0x10
 end
 continue
end

break *0x__RELOCATE_ADDRESS__
commands
 silent
 if $copy_seen && $rcx >= 0x400000000000 && $rcx < 0x400010000000 && *(unsigned long long*)$rcx == $worker
  set $event = $event + 1
  printf "EVENT=%u RELOCATE_ADDRESS_TARGET location=%p old_value=%p rip=%p rsp=%p return=%p\n",$event,$rcx,*(unsigned long long*)$rcx,$pc,$rsp,*(unsigned long long*)$rsp
  x/12gx $rcx-0x10
  bt 24
 end
 continue
end

break *0x__GC__
commands
 silent
 set $last_phase = 1
 printf "GC_PHASE garbage_collect rip=%p arg=%d\n",$pc,$ecx
 continue
end
break *0x__GC1__
commands
 silent
 set $last_phase = 2
 printf "GC_PHASE gc1 rip=%p\n",$pc
 continue
end
break *0x__MARK__
commands
 silent
 set $last_phase = 3
 printf "GC_PHASE mark_phase rip=%p arg=%d\n",$pc,$ecx
 continue
end
break *0x__PLAN__
commands
 silent
 set $last_phase = 4
 printf "GC_PHASE plan_phase rip=%p arg=%d\n",$pc,$ecx
 continue
end
break *0x__RELOCATE__
commands
 silent
 set $last_phase = 5
 printf "GC_PHASE relocate_survivors rip=%p arg=%d dest=%p\n",$pc,$ecx,$rdx
 continue
end
break *0x__COMPACT__
commands
 silent
 set $last_phase = 6
 printf "GC_PHASE compact_phase rip=%p arg=%d dest=%p\n",$pc,$ecx,$rdx
 continue
end
break *0x__SWEEP__
commands
 silent
 set $last_phase = 7
 printf "GC_PHASE sweep_region_in_plan rip=%p\n",$pc
 continue
end
break *0x__FREE_LISTS__
commands
 silent
 set $last_phase = 8
 printf "GC_PHASE make_free_lists rip=%p arg=%d\n",$pc,$ecx
 continue
end
break *0x__FIND_REGION__
commands
 silent
 set $last_phase = 9
 printf "GC_PHASE find_first_valid_region rip=%p\n",$pc
 continue
end
break *0x__COMPACT_PLUG__
commands
 silent
 set $last_phase = 10
 printf "GC_PHASE compact_plug rip=%p\n",$pc
 continue
end

break *0x__RUN_WORKER_EXPORT__
commands
 silent
 printf "RUN_WORKER_EXPORT rip=%p rsp=%p stage=%d\n",$pc,$rsp,$ecx
 continue
end
break *0x__RUN_WORKER_HELPER__
commands
 silent
 printf "RUN_WORKER_HELPER rip=%p rsp=%p stage=%d static_slot=%p static_state=%p root=%p\n",$pc,$rsp,$ecx,0x__STATIC_STATE_SLOT__,*(unsigned long long*)0x__STATIC_STATE_SLOT__,*(unsigned long long*)0x4000000008c0
 x/16gx *(unsigned long long*)0x__STATIC_STATE_SLOT__
 continue
end
break *0x__GC_CALL__
commands
 silent
 printf "MANAGED_GC_CALL rip=%p rsp=%p\n",$pc,$rsp
 continue
end
break *0x__GC_RETURN__
commands
 silent
 printf "MANAGED_GC_RETURN rip=%p rsp=%p\n",$pc,$rsp
 continue
end
break *0x__WORKER_CALL__
commands
 silent
 if $worker_seen
  printf "WORKER_DISPATCH_CALL rip=%p rsp=%p rbx=%p rcx=%p rdx=%p root=%p\n",$pc,$rsp,$rbx,$rcx,$rdx,*(unsigned long long*)0x4000000008c0
  x/12gx $worker
 end
 continue
end

continue
printf "GDB_NO_TARGETED_SET_FREE\n"
detach
quit
'@

    foreach ($item in @(
        @('__GDB_PORT__', $gdbPort),
        @('__WORKER_ENTRY__', $workerEntry),
        @('__SOH_TRY_FIT__', $sohTryFit),
        @('__FIX_ALLOC_CONTEXT__', $fixAllocContext),
        @('__FIX_ALLOCATION_CONTEXT__', $fixAllocationContext),
        @('__FIX_ALLOC_SETFREE_1__', $fixAllocationSetFree1),
        @('__FIX_ALLOC_SETFREE_2__', $fixAllocationSetFree2),
        @('__SETFREE__', $setFree),
        @('__SETFREE_AFTER_HEADER__', $setFreeAfterHeader),
        @('__SETFREE_AFTER_PAYLOAD__', $setFreeAfterPayload),
        @('__MEMCOPY__', $memcopy),
        @('__MEMCOPY_HEADER_INSTRUCTION__', $memcopyHeaderInstruction),
        @('__MEMCOPY_DISPATCHER_INSTRUCTION__', $memcopyDispatcherInstruction),
        @('__MEMCOPY_AFTER__', $memcopyAfter),
        @('__RELOCATE_ADDRESS__', $relocateAddress),
        @('__RELOCATE_ADDRESS_AFTER_1__', $relocateAddressAfter1),
        @('__RELOCATE_ADDRESS_AFTER_2__', $relocateAddressAfter2),
        @('__GC__', $garbageCollect),
        @('__GC1__', $gc1),
        @('__MARK__', $markPhase),
        @('__PLAN__', $planPhase),
        @('__RELOCATE__', $relocateSurvivors),
        @('__COMPACT__', $compactPhase),
        @('__SWEEP__', $sweepRegion),
        @('__FREE_LISTS__', $makeFreeLists),
        @('__FIND_REGION__', $findFirstValidRegion),
        @('__COMPACT_PLUG__', $compactPlug),
        @('__RUN_WORKER_EXPORT__', $runWorkerExport),
        @('__RUN_WORKER_HELPER__', $runWorkerHelper),
        @('__STATIC_STATE_SLOT__', $staticStateSlot),
        @('__GC_CALL__', $gcCall),
        @('__GC_RETURN__', $gcReturn),
        @('__WORKER_CALL__', $workerCall),
        @('__FREE_EETYPE__', $freeEeType),
        @('__RUNTIME_GLOBAL_BASE__', $runtimeGlobalBase)
    )) {
        $replacement = if ($item[0] -eq '__GDB_PORT__') { [string][int]$item[1] } else { ('{0:X}' -f [uint64]$item[1]) }
        $gdbText = $gdbText.Replace($item[0], $replacement)
    }
    [IO.File]::WriteAllText($gdbScriptPath, $gdbText, [Text.Encoding]::ASCII)
    Set-Content -LiteralPath (Join-Path $out 'actual-image-base.txt') -Value ('0x{0:X}' -f $imageBase) -Encoding ascii
    Set-Content -LiteralPath (Join-Path $out 'breakpoints.txt') -Value @(
        ('IMAGE_BASE=0x{0:X}' -f $imageBase),
        ('SETFREE=0x{0:X} RVA=0x1617B0' -f $setFree),
        ('FIX_ALLOCATION_CONTEXT=0x{0:X} RVA=0x172E20' -f $fixAllocationContext),
        ('FIX_ALLOC_PRE_SETFREE_1=0x{0:X} RVA=0x172E95' -f $fixAllocationSetFree1),
        ('SOH_TRY_FIT=0x{0:X} RVA=0x185B80' -f $sohTryFit),
        ('MEMCOPY=0x{0:X} RVA=0x17D3A0' -f $memcopy),
        ('RELOCATE_ADDRESS=0x{0:X} RVA=0x1826A0' -f $relocateAddress),
        ('ROOT_SLOT=0x{0:X}' -f $rootSlot),
        ('DESTINATION=0x{0:X}' -f $destination)
    ) -Encoding ascii

    $gdbProcess = Start-Process -FilePath $gdb -ArgumentList @('-q', '-batch', '-x', $gdbScriptPath) -RedirectStandardOutput $gdbOutPath -RedirectStandardError $gdbErrPath -PassThru -WindowStyle Hidden
    $injectionFile = [IO.StreamWriter]::new($injectionPath, $false, [Text.Encoding]::ASCII)
    function Record-Injection([string]$name) {
        $injectionFile.WriteLine(('{0:o} {1}' -f [DateTime]::UtcNow, $name))
        $injectionFile.Flush()
    }
    $gdbDeadline = [DateTime]::UtcNow.AddMinutes(10)
    while (!$gdbProcess.HasExited -and [DateTime]::UtcNow -lt $gdbDeadline) {
        while ($serialStream.DataAvailable) {
            $count = $serialStream.Read($serialBuffer, 0, $serialBuffer.Length)
            if ($count -le 0) { break }
            $serialFile.Write($serialBuffer, 0, $count)
            $serialFile.Flush()
            [void]$serialText.Append([Text.Encoding]::ASCII.GetString($serialBuffer, 0, $count))
        }
        $currentSerial = $serialText.ToString()
        if (!$sentSerial52 -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_WORKER_UART_READY')) {
            Send-RawByte $serialClient 0x52; $sentSerial52 = $true; Record-Injection 'SERIAL_READY byte=0x52'
        }
        if (!$sentSerial53 -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SECOND_WAIT_READY')) {
            Send-RawByte $serialClient 0x53; $sentSerial53 = $true; Record-Injection 'SERIAL_SECOND_READY byte=0x53'
        }
        if (!$sentKeyA -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_INPUT_READY')) {
            Send-Line $monitorClient 'sendkey a'; $sentKeyA = $true; Record-Injection 'KEYBOARD_INPUT_READY key=a'
        }
        if (!$sentKeyB -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_SECOND_INPUT_READY')) {
            Send-Line $monitorClient 'sendkey b'; $sentKeyB = $true; Record-Injection 'KEYBOARD_SECOND_INPUT_READY key=b'
        }
        if (!$sentKeyC -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_UNSUBSCRIBED_READY')) {
            Send-Line $monitorClient 'sendkey c'; $sentKeyC = $true; Record-Injection 'KEYBOARD_UNSUBSCRIBED_READY key=c'
        }
        if (!$sentBurst -and $currentSerial.Contains('GXOS_NET10:MANAGED_KERNEL_KEYBOARD_NO_DELIVERY_AFTER_UNSUBSCRIBE_OK')) {
            Send-RawByte $serialClient 0x44; Start-Sleep -Milliseconds 50
            Send-RawByte $serialClient 0x45; Start-Sleep -Milliseconds 50
            Send-RawByte $serialClient 0x46; $sentBurst = $true
            Record-Injection 'KEYBOARD_B_SENT bytes=0x44,0x45,0x46'
        }
        Start-Sleep -Milliseconds 25
    }
    if ($null -ne $injectionFile) { $injectionFile.Dispose() }
    if (!$gdbProcess.HasExited) {
        Stop-Process -Id $gdbProcess.Id -Force -ErrorAction SilentlyContinue
        throw 'GDB timed out before the targeted SetFree sequence completed.'
    }
    $gdbProcess.WaitForExit()
    while ($serialStream.DataAvailable) {
        $count = $serialStream.Read($serialBuffer, 0, $serialBuffer.Length)
        if ($count -le 0) { break }
        $serialFile.Write($serialBuffer, 0, $count)
        $serialFile.Flush()
    }
    $gdbOutput = Get-Content -LiteralPath $gdbOutPath -Raw
    $captureStatus = if ($gdbOutput -match '(?m)^TARGETED_SEQUENCE_COMPLETE') { 'TARGETED_SEQUENCE_CAPTURED' } elseif ($gdbOutput -match '(?m)^EVENT=.*TARGETED_SET_FREE_ENTRY') { 'TARGETED_SET_FREE_ONLY' } elseif ($gdbOutput -match '(?m)^GDB_NO_TARGETED_SET_FREE') { 'NO_TARGETED_SET_FREE' } else { 'GDB_COMPLETED_WITHOUT_CLASSIFICATION' }
} catch {
    $captureStatus = 'ERROR'
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
        ('payload_sha256={0}' -f (Get-FileHash (Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll') -Algorithm SHA256).Hash.ToUpperInvariant()),
        ('payload_size={0}' -f (Get-Item (Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll')).Length),
        ('ovmf_code_sha256={0}' -f (Get-FileHash $code -Algorithm SHA256).Hash.ToUpperInvariant()),
        ('ovmf_vars_sha256={0}' -f (Get-FileHash $vars -Algorithm SHA256).Hash.ToUpperInvariant()),
        ('serial_port={0}' -f $serialPort),
        ('monitor_port={0}' -f $monitorPort),
        ('gdb_port={0}' -f $gdbPort),
        ('rx_port={0}' -f $rxPort),
        ('peer_port={0}' -f $peerPort)
    ) | Set-Content -LiteralPath $summaryPath -Encoding ascii
}


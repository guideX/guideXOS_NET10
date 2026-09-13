[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts\phase53l-worker-allocation-capture-1',
    [int]$TimeoutMinutes = 10,
    [switch]$SkipOriginalContextWatch
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
            $serialFile.Flush()
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

    $rhpNewFast = $imageBase + 0x148A20
    $rhpContextReady = $imageBase + 0x148A3B
    $rhpHeaderStore = $imageBase + 0x148A51
    $rhpAllocPtrStore = $imageBase + 0x148A54
    $rhpAfterAlloc = $imageBase + 0x148A58
    $rhpAssignRef = $imageBase + 0x148C80
    $rhpAssignRefAfterStore = $imageBase + 0x148C83
    $runWorkerExport = $imageBase + 0x894A0
    $runWorkerHelper = $imageBase + 0xF8138
    $workerCallReturn = $imageBase + 0xF8455
    $workerKeyboardStore = $imageBase + 0xF8472
    $workerStateCreatedStore = $imageBase + 0xF8476
    $workerPublishCall = $imageBase + 0xF8480
    $workerStateRunningStore = $imageBase + 0xF8489
    $fixAllocationContext = $imageBase + 0x172E20
    $fixAllocationSetFree1 = $imageBase + 0x172E95
    $staticStateSlot = $imageBase + 0x47C410
    $tlsIndexCell = $imageBase + 0x48A074
    $rootSlot = [uint64]0x4000000008C0

    $gdbText = @'
set pagination off
set confirm off
set architecture i386:x86-64
set can-use-hw-watchpoints 1
set print pretty off
set $event = 0
set $worker_alloc_entry_seen = 0
set $worker_context_seen = 0
set $worker_header_seen = 0
set $worker_alloc_seen = 0
set $worker = 0
set $worker_alloc_ctx = 0
set $worker_tls_block = 0
set $publication_seen = 0
set $lineage_watch_armed = 0
set $lineage_count = 0
target remote 127.0.0.1:__GDB_PORT__

break *0x__RHP_NEW_FAST__
commands
 silent
 if $rcx == 0x__WORKER_EETYPE__
  set $event = $event + 1
  set $worker_alloc_entry_seen = 1
  printf "EVENT=%u WORKER_ALLOCATION_ENTRY helper=RhpNewFast rva=0x148a20 pc=%p eetype=%p return=%p rsp=%p\n",$event,$pc,$rcx,*(unsigned long long*)$rsp,$rsp
  printf "WORKER_ALLOCATION_ENTRY_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
  bt 20
 end
 continue
end

break *0x__RHP_CONTEXT_READY__
commands
 silent
 if $worker_alloc_entry_seen && $rcx == 0x__WORKER_EETYPE__
  set $event = $event + 1
  set $worker_context_seen = 1
  set $worker_alloc_ctx = $rdx + 8
  set $worker_tls_block = $rax
  set $worker_alloc_ptr_before = *(unsigned long long*)($rdx + 8)
  set $worker_alloc_limit_before = *(unsigned long long*)($rdx + 16)
  set $worker_combined_limit_before = *(unsigned long long*)$rdx
  set $worker_size = *(unsigned int*)($rcx + 4)
  set $worker_expected_after = $worker_alloc_ptr_before + $worker_size
  printf "EVENT=%u WORKER_ALLOCATION_CONTEXT_BEFORE helper=RhpNewFast pc=%p eetype=%p size=0x%x tls_block=%p tls_index_cell=%p tls_index=%u context=%p context_minus_8=%p alloc_ptr_before=%p alloc_limit_before=%p combined_limit_before=%p expected_after=%p return=%p\n",$event,$pc,$rcx,$worker_size,$worker_tls_block,0x__TLS_INDEX_CELL__,*(unsigned int*)0x__TLS_INDEX_CELL__,$worker_alloc_ctx,$rdx,$worker_alloc_ptr_before,$worker_alloc_limit_before,$worker_combined_limit_before,$worker_expected_after,*(unsigned long long*)$rsp
  printf "WORKER_ALLOCATION_CONTEXT_BEFORE_REGS rax_tls_block=%p rdx_context_minus_8=%p rcx_eetype=%p r8=%p r9=%p rsp=%p\n",$rax,$rdx,$rcx,$r8,$r9,$rsp
  info registers gs_base
  info registers fs_base
  x/8gx $worker_alloc_ctx - 8
  bt 20
 end
 continue
end

break *0x__RHP_HEADER_STORE__
commands
 silent
 if $worker_context_seen && $rcx == 0x__WORKER_EETYPE__
  set $event = $event + 1
  set $worker_header_seen = 1
  printf "EVENT=%u WORKER_HEADER_WRITER pc=%p rva=0x148a51 instruction=mov_[rax],rcx object_base=%p eetype_written=%p pre_header=%p context=%p alloc_ptr_before=%p alloc_limit=%p\n",$event,$pc,$rax,$rcx,*(unsigned long long*)$rax,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8)
  printf "WORKER_HEADER_WRITER_REGS rax_object=%p rcx_eetype=%p rdx_context_minus_8=%p r8_new_boundary=%p rsp=%p return=%p\n",$rax,$rcx,$rdx,$r8,$rsp,*(unsigned long long*)$rsp
  x/8i $pc
  bt 20
 end
 continue
end

break *0x__RHP_ALLOC_PTR_STORE__
commands
 silent
 if $worker_header_seen && $rcx == 0x__WORKER_EETYPE__
  set $event = $event + 1
  set $worker = $rax
  printf "EVENT=%u WORKER_ALLOCATION_BOUNDARY_STORE_BEFORE pc=%p rva=0x148a54 object_base=%p new_alloc_ptr=%p context=%p alloc_ptr_before=%p alloc_limit=%p header_now=%p\n",$event,$pc,$rax,$r8,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),*(unsigned long long*)$rax
  printf "WORKER_ALLOCATION_BOUNDARY_STORE_REGS rax_object=%p rcx_eetype=%p rdx_context_minus_8=%p r8_new_boundary=%p rsp=%p return=%p\n",$rax,$rcx,$rdx,$r8,$rsp,*(unsigned long long*)$rsp
  continue
 end
 continue
end

break *0x__RHP_AFTER_ALLOC__
commands
 silent
 if $worker_header_seen && $rcx == 0x__WORKER_EETYPE__
  set $event = $event + 1
  set $worker_alloc_seen = 1
  set $lineage_watch_armed = 1
  set $lineage_prev_ptr = *(unsigned long long*)$worker_alloc_ctx
  printf "EVENT=%u WORKER_ALLOCATION_AFTER pc=%p rva=0x148a58 object_base=%p returned_value=%p context=%p alloc_ptr_after=%p alloc_limit_after=%p expected_after=%p\n",$event,$pc,$rax,$rax,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),$worker_expected_after
  x/8gx $worker
  bt 20
  __WORKER_LINEAGE_WATCH__
 end
 continue
end

break *0x__WORKER_CALL_RETURN__
commands
 silent
 if $worker_alloc_seen && $rax == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_ALLOCATION_RETURN_TO_MANAGED_CALLSITE pc=%p rva=0xf8455 returned_worker=%p context=%p alloc_ptr=%p alloc_limit=%p\n",$event,$pc,$rax,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8)
  bt 20
 end
 continue
end

break *0x__RHP_ASSIGN_REF__
commands
 silent
 if $worker_alloc_seen && $rdx == $worker
  if $rcx == $worker + 8
   set $event = $event + 1
   printf "EVENT=%u WORKER_FIELD_WRITE dispatcher_dest=%p value=%p pc=%p context=%p alloc_ptr=%p\n",$event,$rcx,$rdx,$pc,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx
  end
  if $rcx == $worker + 16
   set $event = $event + 1
   printf "EVENT=%u WORKER_FIELD_WRITE serial_driver_dest=%p value=%p pc=%p context=%p alloc_ptr=%p\n",$event,$rcx,$rdx,$pc,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx
  end
  if $rcx == 0x__ROOT_SLOT__
   set $event = $event + 1
   set $publication_seen = 1
   printf "EVENT=%u WORKER_PUBLICATION_STORE_BEFORE pc=%p rva=0x148c80 destination=%p value=%p worker=%p context=%p alloc_ptr=%p alloc_limit=%p expected_after=%p root_before=%p return=%p\n",$event,$pc,$rcx,$rdx,$worker,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),$worker_expected_after,*(unsigned long long*)0x__ROOT_SLOT__,*(unsigned long long*)$rsp
   printf "WORKER_PUBLICATION_STORE_REGS rcx_destination=%p rdx_worker=%p rax=%p rbx=%p rbp=%p rsp=%p r8=%p r9=%p\n",$rcx,$rdx,$rax,$rbx,$rbp,$rsp,$r8,$r9
   bt 20
  end
 end
 continue
end

break *0x__RHP_ASSIGN_REF_AFTER_STORE__
commands
 silent
 if $publication_seen && *(unsigned long long*)0x__ROOT_SLOT__ == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_PUBLICATION_STORE_AFTER pc=%p rva=0x148c83 destination=%p root_now=%p value=%p context=%p alloc_ptr=%p alloc_limit=%p worker_header=%p\n",$event,$pc,0x__ROOT_SLOT__,*(unsigned long long*)0x__ROOT_SLOT__,$worker,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),*(unsigned long long*)$worker
  x/12gx $worker
  printf "TARGETED_WORKER_PUBLICATION_COMPLETE events=%u worker=%p\n",$event,$worker
  printf "TARGETED_WORKER_PUBLICATION_CAPTURED events=%u worker=%p\n",$event,$worker
 end
 continue
end

break *0x__WORKER_KEYBOARD_STORE__
commands
 silent
 if $worker_alloc_seen && $rbp == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_FIELD_WRITE keyboard_dest=%p value=%p pc=%p rva=0xf8472 context=%p alloc_ptr=%p\n",$event,$rbp+24,$rcx,$pc,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx
 end
 continue
end

break *0x__WORKER_STATE_CREATED_STORE__
commands
 silent
 if $worker_alloc_seen && $rbp == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_FIELD_WRITE state_created_dest=%p value=0 pc=%p rva=0xf8476 context=%p alloc_ptr=%p\n",$event,$rbp+32,$pc,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx
 end
 continue
end

break *0x__WORKER_PUBLISH_CALL__
commands
 silent
 if $worker_alloc_seen && $rbp == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_PUBLICATION_CALLSITE pc=%p rva=0xf8480 destination=%p value=%p rbx_static_state=%p static_slot=%p context=%p alloc_ptr=%p alloc_limit=%p expected_after=%p\n",$event,$pc,$rbx+40,$rbp,$rbx,0x__STATIC_STATE_SLOT__,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),$worker_expected_after
  x/8gx $rbx
  bt 20
 end
 continue
end

break *0x__WORKER_STATE_RUNNING_STORE__
commands
 silent
 if $worker_alloc_seen && $rcx == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_FIELD_WRITE state_running_dest=%p value=2 pc=%p rva=0xf8489 context=%p alloc_ptr=%p\n",$event,$rcx+32,$pc,$worker_alloc_ctx,*(unsigned long long*)$worker_alloc_ctx
 end
 continue
end

break *0x__FIX_ALLOCATION_CONTEXT__
commands
 silent
 if $worker_alloc_seen && $rcx >= 0x400000000000 && $rcx < 0x400010000000
  if $rcx == $worker_alloc_ctx || *(unsigned long long*)$rcx == $worker
   set $event = $event + 1
   printf "EVENT=%u WORKER_FIX_ALLOCATION_CONTEXT context=%p ptr=%p alloc_limit=%p worker=%p pc=%p rva=0x172e20 return=%p\n",$event,$rcx,*(unsigned long long*)$rcx,*(unsigned long long*)($rcx+8),$worker,$pc,*(unsigned long long*)$rsp
   x/16gx $rcx
   bt 20
   printf "TARGETED_WORKER_FIX_CONTEXT_CAPTURED context=%p worker=%p ptr=%p\n",$rcx,$worker,*(unsigned long long*)$rcx
   set $lineage_watch_armed = 0
   detach
   quit
  end
 end
 continue
end

break *0x__FIX_ALLOCATION_SETFREE_1__
commands
 silent
 if $worker_alloc_seen && $rdi >= 0x400000000000 && $rdi < 0x400010000000 && $rbx == $worker
  set $event = $event + 1
  printf "EVENT=%u WORKER_FIX_PRE_SETFREE site=1 pc=%p rva=0x172e95 consumer_context=%p original_context=%p same_as_original=%d ptr=%p alloc_limit=%p size=%p worker=%p rbx=%p rcx=%p rdx=%p r8=%p r15=%p return=%p\n",$event,$pc,$rdi,$worker_alloc_ctx,($rdi == $worker_alloc_ctx),*(unsigned long long*)$rdi,*(unsigned long long*)($rdi+8),$r15,$worker,$rbx,$rcx,$rdx,$r8,$r15,*(unsigned long long*)$rsp
  x/24gx $rdi
  x/32gx $rsp-0x20
  bt 24
  printf "TARGETED_WORKER_FIX_PRE_SETFREE_CAPTURED consumer_context=%p original_context=%p same_as_original=%d worker=%p ptr=%p\n",$rdi,$worker_alloc_ctx,($rdi == $worker_alloc_ctx),$worker,*(unsigned long long*)$rdi
  detach
  quit
 end
 continue
end

break *0x__RUN_WORKER_EXPORT__
commands
 silent
 printf "RUN_WORKER_EXPORT pc=%p rsp=%p stage=%d\n",$pc,$rsp,$ecx
 continue
end

break *0x__RUN_WORKER_HELPER__
commands
 silent
 printf "RUN_WORKER_HELPER pc=%p rsp=%p stage=%d static_slot=%p static_state=%p root=%p\n",$pc,$rsp,$ecx,0x__STATIC_STATE_SLOT__,*(unsigned long long*)0x__STATIC_STATE_SLOT__,*(unsigned long long*)0x__ROOT_SLOT__
 continue
end

continue
printf "GDB_NO_TARGETED_WORKER_PUBLICATION\n"
detach
quit
'@

    $lineageWatchText = if ($SkipOriginalContextWatch) {
        '# original-context hardware watchpoint disabled for low-perturbation FixAllocContext correlation'
    } else {
        @'
  watch *(unsigned long long*)$worker_alloc_ctx
  commands
   silent
   if $lineage_watch_armed
    set $lineage_count = $lineage_count + 1
    if $lineage_count <= 64 || *(unsigned long long*)$worker_alloc_ctx == $worker
     set $event = $event + 1
     printf "EVENT=%u WORKER_CONTEXT_LINEAGE_WRITE context=%p pc=%p old_ptr=%p new_ptr=%p alloc_limit=%p worker=%p count=%u\n",$event,$worker_alloc_ctx,$pc,$lineage_prev_ptr,*(unsigned long long*)$worker_alloc_ctx,*(unsigned long long*)($worker_alloc_ctx+8),$worker,$lineage_count
     printf "WORKER_CONTEXT_LINEAGE_WRITE_REGS rax=%p rbx=%p rcx=%p rdx=%p rsi=%p rdi=%p rbp=%p rsp=%p r8=%p r9=%p r10=%p r11=%p r12=%p r13=%p r14=%p r15=%p\n",$rax,$rbx,$rcx,$rdx,$rsi,$rdi,$rbp,$rsp,$r8,$r9,$r10,$r11,$r12,$r13,$r14,$r15
     bt 20
    end
    set $lineage_prev_ptr = *(unsigned long long*)$worker_alloc_ctx
    if *(unsigned long long*)$worker_alloc_ctx == $worker
     printf "TARGETED_WORKER_CONTEXT_RECURRENCE context=%p worker=%p ptr=%p\n",$worker_alloc_ctx,$worker,*(unsigned long long*)$worker_alloc_ctx
     set $lineage_watch_armed = 0
     detach
     quit
    end
   end
   continue
  end
'@
    }

    foreach ($item in @(
        @('__GDB_PORT__', $gdbPort),
        @('__RHP_NEW_FAST__', $rhpNewFast),
        @('__RHP_CONTEXT_READY__', $rhpContextReady),
        @('__RHP_HEADER_STORE__', $rhpHeaderStore),
        @('__RHP_ALLOC_PTR_STORE__', $rhpAllocPtrStore),
        @('__RHP_AFTER_ALLOC__', $rhpAfterAlloc),
        @('__RHP_ASSIGN_REF__', $rhpAssignRef),
        @('__RHP_ASSIGN_REF_AFTER_STORE__', $rhpAssignRefAfterStore),
        @('__RUN_WORKER_EXPORT__', $runWorkerExport),
        @('__RUN_WORKER_HELPER__', $runWorkerHelper),
        @('__WORKER_CALL_RETURN__', $workerCallReturn),
        @('__WORKER_KEYBOARD_STORE__', $workerKeyboardStore),
        @('__WORKER_STATE_CREATED_STORE__', $workerStateCreatedStore),
        @('__WORKER_PUBLISH_CALL__', $workerPublishCall),
        @('__WORKER_STATE_RUNNING_STORE__', $workerStateRunningStore),
        @('__FIX_ALLOCATION_CONTEXT__', $fixAllocationContext),
        @('__FIX_ALLOCATION_SETFREE_1__', $fixAllocationSetFree1),
        @('__WORKER_EETYPE__', $workerEeTypeAddress),
        @('__TLS_INDEX_CELL__', $tlsIndexCell),
        @('__ROOT_SLOT__', $rootSlot),
        @('__STATIC_STATE_SLOT__', $staticStateSlot),
        @('__WORKER_LINEAGE_WATCH__', $lineageWatchText)
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
        ('WORKER_EETYPE_HISTORICAL_PHASE53K=0x{0:X}' -f $expectedWorkerEeType),
        ('WORKER_EETYPE_RVA=0x{0:X}' -f $expectedWorkerRva),
        'WORKER_MANAGED_SIZE=0x38',
        'WORKER_NATIVEAOT_BASE_SIZE=0x40',
        'WORKER_ALIGNMENT=0x8',
        ('ORIGINAL_CONTEXT_WATCH_SKIPPED={0}' -f $SkipOriginalContextWatch.IsPresent),
        ('RHP_NEW_FAST=0x{0:X} RVA=0x148A20' -f $rhpNewFast),
        ('RHP_HEADER_STORE=0x{0:X} RVA=0x148A51' -f $rhpHeaderStore),
        ('RHP_ALLOC_PTR_STORE=0x{0:X} RVA=0x148A54' -f $rhpAllocPtrStore),
        ('RHP_ASSIGN_REF=0x{0:X} RVA=0x148C80' -f $rhpAssignRef),
        ('RUN_WORKER_HELPER=0x{0:X} RVA=0xF8138' -f $runWorkerHelper),
        ('RUN_WORKER_PUBLICATION_CALL=0x{0:X} RVA=0xF8480' -f $workerPublishCall),
        ('ROOT_SLOT=0x{0:X}' -f $rootSlot),
        ('TLS_INDEX_CELL=0x{0:X} RVA=0x48A074' -f $tlsIndexCell),
        ('PAYLOAD_PATH={0}' -f $payloadPath),
        ('PAYLOAD_SHA256={0}' -f $payloadHash),
        ('PDB_PATH={0}' -f $pdbPath),
        ('PDB_SHA256={0}' -f $pdbHash)
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
            $serialFile.Flush()
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
        $partialGdbOutput = if (Test-Path -LiteralPath $gdbOutPath) {
            Get-Content -LiteralPath $gdbOutPath -Raw
        } else {
            ''
        }
        if ($partialGdbOutput -match '(?m)^TARGETED_WORKER_PUBLICATION_CAPTURED') {
            $captureStatus = 'INCOMPLETE_AFTER_WORKER_PUBLICATION'
        } elseif ($partialGdbOutput -match '(?m)^EVENT=.*WORKER_ALLOCATION_AFTER') {
            $captureStatus = 'INCOMPLETE_WORKER_ALLOCATION_WITHOUT_PUBLICATION'
        } elseif ($partialGdbOutput -match '(?m)^EVENT=.*WORKER_ALLOCATION_ENTRY') {
            $captureStatus = 'INCOMPLETE_WORKER_ENTRY_ONLY'
        } else {
            $captureStatus = 'INCOMPLETE_GDB_TIMEOUT'
        }
        Stop-Process -Id $gdbProcess.Id -Force -ErrorAction SilentlyContinue
        throw "GDB timed out before the requested capture completed (status $captureStatus)."
    }
    $gdbProcess.WaitForExit()
    while ($serialStream.DataAvailable) {
        $count = $serialStream.Read($serialBuffer, 0, $serialBuffer.Length)
        if ($count -le 0) { break }
        $serialFile.Write($serialBuffer, 0, $count)
        $serialFile.Flush()
        [void]$serialText.Append([Text.Encoding]::ASCII.GetString($serialBuffer, 0, $count))
    }
    $gdbOutput = Get-Content -LiteralPath $gdbOutPath -Raw
    if ($gdbOutput -match '(?m)^TARGETED_WORKER_CONTEXT_RECURRENCE') {
        $captureStatus = 'TARGETED_WORKER_ALLOCATION_PUBLICATION_AND_CONTEXT_RECURRENCE_CAPTURED'
    } elseif ($gdbOutput -match '(?m)^TARGETED_WORKER_FIX_PRE_SETFREE_CAPTURED') {
        $captureStatus = 'TARGETED_WORKER_ALLOCATION_PUBLICATION_AND_FIX_PRE_SETFREE_CAPTURED'
    } elseif ($gdbOutput -match '(?m)^TARGETED_WORKER_FIX_CONTEXT_CAPTURED') {
        $captureStatus = 'TARGETED_WORKER_ALLOCATION_PUBLICATION_AND_FIX_CONTEXT_CAPTURED'
    } elseif ($gdbOutput -match '(?m)^TARGETED_WORKER_PUBLICATION_CAPTURED') {
        $captureStatus = 'TARGETED_WORKER_ALLOCATION_AND_PUBLICATION_CAPTURED_WITHOUT_LINEAGE'
    } elseif ($gdbOutput -match '(?m)^EVENT=.*WORKER_ALLOCATION_AFTER') {
        $captureStatus = 'INCOMPLETE_WORKER_ALLOCATION_WITHOUT_PUBLICATION'
        throw 'Worker allocation was captured but publication was not reached.'
    } elseif ($gdbOutput -match '(?m)^EVENT=.*WORKER_ALLOCATION_ENTRY') {
        $captureStatus = 'INCOMPLETE_WORKER_ENTRY_ONLY'
        throw 'Worker allocation helper entry was captured but completion was not reached.'
    } else {
        $captureStatus = 'NO_WORKER_ALLOCATION_MATCH'
        throw 'GDB completed without a worker EEType allocation match.'
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

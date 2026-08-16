# NativeAOT FLS Thread-Affinity Prerequisite

## Result

The fail-fast at payload RVA `0x3C6A4` was caused by the guideXOS FLS adapter
using one process-global value table after the cooperative scheduler had
started. NativeAOT uses the FLS slot as thread-local state. The worker stored a
nonzero value in slot 1, and the main thread subsequently observed that same
value, selecting NativeAOT's intentional fatal path.

The bounded fix keeps FLS slot allocation and cleanup callbacks process-wide,
but routes slot values through the scheduler's existing per-TCB FLS arrays
after `gxos_scheduler_adopt_boot_environment`. The boot-time value is migrated
into the boot TCB before scheduler mode is enabled.

## Causal trace

The exact payload is stripped, but `.pdata` identifies the enclosing helper as
`RVA 0x3C680..0x3C6BF`. Its best available identity is an internal NativeAOT
thread-state/FLS attach helper. It is called from the NativeAOT thread
initialization paths beginning at RVAs `0x375D0` and `0x37680`.

The relevant instructions are:

```asm
; RVA 0x3C680
call    FlsGetValue                 ; index = [payload+0xAA280]
test    rax, rax
je      0x3C6AA
; rax != 0 is the fatal condition
xor     ecx, ecx                    ; RCX = 0
xor     edx, edx                    ; RDX = 0
mov     r8d, 1                      ; R8  = 1
call    RaiseFailFastException     ; RVA 0x3C6A4

; zero FLS value path
mov     rdx, rbx                    ; original helper argument
jmp     FlsSetValue                 ; IAT RVA 0x7D170
```

The FLS import is `FlsGetValue`, descriptor 2, symbol index 0x0D, IAT RVA
`0x7D168`. The fatal import is `KERNEL32.dll!RaiseFailFastException`,
descriptor 2, symbol index `0x14`, IAT RVA `0x7D0D8`. No Win32 error or status
is returned by the failed operation: the meaningful result is the nonzero FLS
pointer.

The baseline worker trace established the exact value written by the worker
to slot 1: `0x5469030`, under scheduler identity 2. With the old adapter this
was `g_fls_values[1]`, so the main thread's later `FlsGetValue(1)` returned the
same nonzero pointer. In the fixed trace, identity 2 writes `0x5469030`, while
identity 1 reads `0` and then writes its own `0x5479030`. This is the first
meaningful failed condition; the final fail-fast is not an event, wait, COM,
VM, allocation, or `LastError` failure.

## Fail-fast arguments and state

At the fatal call the payload explicitly sets `RCX=0`, `RDX=0`, and `R8=1`.
`R9` is not written by this helper and was captured as `0x5511E10` in the
baseline. Baseline stack arguments were `arg5=0x5479030` and
`arg6=0x54B262B`. These arguments are a generic no-context fatal invocation;
they do not identify an exception or context structure.

The baseline state was coherent:

- main identity 1, state Running (3), COM uninitialized;
- worker identity 2, state Blocked (4), COM initialized MTA;
- runnable count 0, blocked count 1, worker execution count 1;
- finalizer startup event: slot 1, generation 1, auto-reset, nonsignaled,
  waiter count 1;
- live objects 13, live public handles 12, worker execution reference live;
- main stack allocation `[0x7E64000,0x7F64000)`; the fatal-site telemetry did
  not capture main RSP. `0x5479030` was stack argument 5, not RSP;
- `LastError=0x7F` at the import blocker.

The worker's blocked state is therefore an expected consequence of its
truthful infinite wait and is independent of the FLS invariant. The main
thread's `SetEvent` path had already completed the separate main wait record;
the finalizer event was not manually signaled.

## Marker and boot evidence

In the baseline, the order was:

1. `NATIVEAOT_STARTUP_OK`;
2. `GC_STARTUP_ADVANCED`;
3. the worker's `WaitForSingleObjectEx` with `WILL_BLOCK=1`;
4. `RaiseFailFastException` at caller RVA `0x3C6A4`.

The markers are emitted by the NativeAOT startup/GC probe paths; they do not
mean that managed execution has begun. The failure occurred after GC startup,
during later runtime thread-state initialization.

After the FLS fix, three independent fresh QEMU boots preserved the same
wait telemetry and reached `MANAGED_ENTRY_OK`, `AFTER_MANAGED_RETURN=0`, and
`MANAGED_ENTRY_COMPLETE`. None emitted the old fail-fast at RVA `0x32C21`, the
new import blocker at `0x3C6A4`, a CPU/page fault, or a corruption marker.

Payload SHA-256 was unchanged in source and staged ESP for every run:

`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`

## Deliberately deferred

`RaiseFailFastException` remains a terminating boundary. No APC,
`WaitForMultipleObjectsEx`, thread-description, guard-page, arbitrary mapping,
or other unrelated runtime feature was added.

# `KERNEL32.dll!CreateThread` payload boundary

This milestone routes only the exact import pair `KERNEL32.dll!CreateThread`
(descriptor `2`, symbol index `0x2D`, IAT RVA `0x7D1A0`) for the required
payload `artifacts/veh-final3-normal-gate/ESP/GXOS/gxos-managed-entry-probe.dll`.
The required SHA-256 is
`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` and is
checked by the build and before every real-payload execution.

## Exact supported contract

The Microsoft x64 adapter captures the four register arguments and reads
arguments 5 and 6 from the true import-call entry RSP:

```text
RCX = lpThreadAttributes = NULL
RDX = dwStackSize        = 0
R8  = lpStartAddress     = payload base + 0x35320
R9  = lpParameter        = Event #1's opaque handle
[RSP+0x28] = dwCreationFlags = CREATE_SUSPENDED / 0x4
[RSP+0x30] = lpThreadId     = NULL
```

Only NULL attributes, zero stack size, a non-NULL start inside a validated
executable payload section, `CREATE_SUSPENDED` alone, and NULL `lpThreadId`
are accepted. Unsupported attributes, stack sizes, flags, start addresses,
and thread-ID buffers return NULL without creating a TCB, object, stack, or
handle. `lpParameter` is opaque to the generic contract and is copied without
derefencing it. The exact payload adapter additionally proves that it is the
typed, auto-reset, initially nonsignaled Event #1 handle and that its public
reference count is unchanged.

## Scheduler object

Success reuses `gxos_scheduler_create_suspended_thread` and the established
fixed foundation. The result is a generation-checked opaque Thread handle,
not a TCB pointer or raw slot. The object has one public reference and one
internal execution reference. The TCB has a stable identity, an independent
16 KiB owned stack with canaries, a normal scheduler bootstrap context, entry
routine `payload + 0x35320`, and the exact Event #1 entry argument. The
initial state is `CreatedSuspended`, suspend count is `1`, the worker is absent
from the runnable queue, and execution count is `0`.

`dwStackSize == 0` maps to the current guideXOS scheduler default of 16 KiB.
This is a bounded bootstrap policy, not a claim that 16 KiB is sufficient for
arbitrary NativeAOT threads. Before any future ResumeThread milestone runs
this worker, its actual path must be stack-validated.

The worker receives independent GS/TEB-like storage, TLS vector and block,
64-slot FLS storage, and last-error storage. No worker instruction executes
in this milestone. Public-handle lifetime and execution lifetime remain
distinct so a future CloseHandle route can release the public reference while
the execution reference remains live.

## Validation and proof

The loaded PE image supplies the payload bounds and executable section list;
the route does not hardcode only RVA `0x35320` for validation. For the exact
artifact, the observed runtime start is nevertheless required to have RVA
`0x35320`. The adapter records the direct stack capture, all six decoded
arguments, return handle, object slot/generation, TCB slot/identity, stack
bounds/RSP/alignment, entry argument, environment allocations, references,
state, suspend count, runnable status, and execution count.

Focused model coverage is in `src/Gate4Harness/create_thread_model_tests.c`
and covers successful suspended creation, opaque parameters, invalid forms,
TCB/object/stack exhaustion, independent state, no execution, no caller-memory
write for `lpThreadId`, and deterministic teardown of an unresumed synthetic
worker. The complete scheduler model suite remains separate and continues to
run.

Three fresh enabled QEMU executions using the exact payload agree on the next
honest blocker:

```text
KERNEL32.dll!SetThreadPriority
descriptor  2
symbol      0x2F / 47
IAT RVA     0x7D1B0
caller RVA  0x3CFC1
RCX         returned Thread handle
RDX         0x2
R8/R9       0x0 / 0x0
stack arg5  0x4
stack arg6  0x0
```

The worker remains `CreatedSuspended`, with suspend count `1` and execution
count `0`, at this blocker. No SetThreadPriority, ResumeThread, SuspendThread,
CloseHandle, wait, event mutation, or other unresolved payload import is
routed.

The disabled control is `CreateThreadDisabled`. It leaves the preceding two
Event objects and one nonsignaled MemoryResourceNotification intact, keeps
only the boot/main Thread object, and returns to unresolved CreateThread at
caller RVA `0x3CFA0` without a worker TCB, stack, public Thread handle, or
execution lifetime.

## Explicit non-claims

This milestone does not prove arbitrary stack sizes, SECURITY_ATTRIBUTES,
thread-ID output, immediately runnable creation, real ResumeThread or worker
execution, SetThreadPriority, SuspendThread, CloseHandle, preemptive
scheduling, or SMP.

# NativeAOT cooperative durability checkpoint

The NativeAOT event/wait harness now has a bounded durability probe. It runs
after the single legal managed entry returns and exercises two scheduler-owned
callback generations around a nonsignaled wait. The first callback blocks,
the main thread signals its test event, the callback resumes and terminates,
and a second callback is created and reclaimed.

## Post-return ownership map

| Owner | State after `MANAGED_ENTRY_COMPLETE` |
| --- | --- |
| Main scheduler TCB, identity 1 | Live, current/running; runtime FLS value remains on the main TCB; COM uninitialized |
| Finalizer scheduler TCB, identity 2 | Live, blocked; one active wait record remains linked to the finalizer event; COM MTA, nesting 1; worker FLS remains on the worker TCB |
| Finalizer event | Process-lifetime runtime object; public handle remains open; the wait pin accounts for the additional internal reference |
| NativeAOT image and runtime | Still mapped and initialized; GC/runtime globals, CRT state, loader tables, and on-exit state are intentionally process-lifetime state |
| Scheduler stack VM ledger | Main/loader and finalizer-worker stack regions remain registered; temporary test-thread regions are removed before the probe completes |

The fresh baseline is 13 live scheduler objects, 12 public handles, 14
internal references, runnable count 0, blocked count 1, active waits 1,
valid wait records 1, live scheduler threads 2, allocated FLS slots 2, and
live scheduler VM regions 2. The same values are emitted after temporary
event/thread cleanup. The finalizer worker is expected to remain alive and
blocked: managed return ends the entry point, not the process runtime.

## Entry contract boundary

A second call to the NativeAOT DLL entry/export was probed once and stopped at
the first meaningful boundary. It reaches the existing
`KERNEL32!RaiseFailFastException` import at payload caller RVA `0x3C6A4`
because the main NativeAOT FLS slot is already nonzero after the first
initialization. Therefore repeated top-level NativeAOT entry is not legal for
this payload/runtime contract and is not used as a success criterion.

The durable equivalent is the repeated scheduler callback path: two distinct
temporary scheduler identities receive fresh FLS/COM state, switch around a
wait, terminate, reclaim their stacks, and leave no stale handle, wait, FLS,
COM, object, or VM-ledger state. The unchanged managed payload is invoked
once, legally, and still reaches managed return.

Source and staged payload SHA-256:

`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`

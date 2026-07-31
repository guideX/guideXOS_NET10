# First managed allocation status

Status: not proven. This document is the current allocation boundary summary; the detailed artifact differential and negative probe remain in [ALLOCATION_GC_PROBE.md](ALLOCATION_GC_PROBE.md).

The allocation-enabled PE (`6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`) contains `ManagedMain -> AllocateOne -> RhpNewFast`. The clean pre-startup allocation probe returns managed status `-10` because the loader-created TLS allocation limit and allocation pointer are both zero. It emits no `FIRST_ALLOCATION_OK` marker. No custom heap, object header, EEType ownership, write barrier, or collection mechanism is used to turn this into a success.

The separate authentic NativeAOT startup path now proves FILETIME, QPC, and QPF, initializes the two empty CRT on-exit tables, initializes one aligned x64 SLIST header, completes the actual one-null `_initterm_e` range, completes the nine-entry `_initterm` range, completes `strcmp("gcServer", "gcConservative")`, and completes `strlen("gcServer") = 8`. The path then stops at `KERNEL32.dll!GetEnvironmentVariableW`. These contracts are allocation-free and do not initialize a GC heap, allocation context, or managed thread. The first-allocation probe therefore remains `-10`; the next experiment must close the environment-variable dependency and remaining NativeAOT PAL ownership contracts before retrying one allocation.

The SLIST scope is deliberately initialization-only. The current payload imports `InterlockedFlushSList`, but no push, pop, flush, depth query, or compiler-generated atomic operation on the initialized header was reached. See [PLATFORM_SLIST_CONTRACT.md](PLATFORM_SLIST_CONTRACT.md) for the caller, x64 layout, host vectors, and fresh-process evidence.

The no-allocation control remains independently positive across three fresh QEMU runs. See [MANAGED_ENTRY_PROOF.md](MANAGED_ENTRY_PROOF.md), [PLATFORM_PERFORMANCE_COUNTER.md](PLATFORM_PERFORMANCE_COUNTER.md), and [NEXT_STAGE_BLOCKERS.md](NEXT_STAGE_BLOCKERS.md) for the evidence and blocker separation.

The 2026-07-29 SLIST evidence-closure pass does not change this allocation status. The SLIST implementation is allocation-free, and all three complete final-hash traces report zero allocation-context pointer/limit, `ALLOCATION_CONTEXT_VALID=0`, no GC-advanced marker, and no managed-thread registration. The SLIST milestone is closed only for initialization.

The 2026-07-30 `_initterm_e`, `_initterm`, and `strcmp` passes, followed by the 2026-07-31 `strlen` pass, leave allocation unproven. `_initterm_e` skipped its one null entry; `_initterm` invoked and returned from all eight actual non-null callbacks; `strcmp` returned `+1`; and `strlen` returned `8`. All three final QEMU summaries still report allocation-context pointer/limit `0/0`, `ALLOCATION_CONTEXT_VALID=0`, `MANAGED_THREAD_REGISTERED=0`, and no observable GC advancement. The new deepest boundary is `KERNEL32.dll!GetEnvironmentVariableW`.

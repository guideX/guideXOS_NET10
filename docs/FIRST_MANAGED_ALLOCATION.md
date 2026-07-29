# First managed allocation status

Status: not proven. This document is the current allocation boundary summary; the detailed artifact differential and negative probe remain in [ALLOCATION_GC_PROBE.md](ALLOCATION_GC_PROBE.md).

The allocation-enabled PE (`6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`) contains `ManagedMain -> AllocateOne -> RhpNewFast`. The clean pre-startup allocation probe returns managed status `-10` because the loader-created TLS allocation limit and allocation pointer are both zero. It emits no `FIRST_ALLOCATION_OK` marker. No custom heap, object header, EEType ownership, write barrier, or collection mechanism is used to turn this into a success.

The separate authentic NativeAOT startup path now proves FILETIME, QPC, and QPF, initializes the two empty CRT on-exit tables, initializes one aligned x64 SLIST header, and then stops at `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. The SLIST initializer is allocation-free and does not initialize a GC heap, allocation context, managed thread, or callback lifecycle. The first-allocation probe therefore remains `-10`; the next experiment must close the `_initterm_e`/CRT startup dependency and the remaining NativeAOT PAL ownership contracts before retrying one allocation.

The SLIST scope is deliberately initialization-only. The current payload imports `InterlockedFlushSList`, but no push, pop, flush, depth query, or compiler-generated atomic operation on the initialized header was reached. See [PLATFORM_SLIST_CONTRACT.md](PLATFORM_SLIST_CONTRACT.md) for the caller, x64 layout, host vectors, and fresh-process evidence.

The no-allocation control remains independently positive across three fresh QEMU runs. See [MANAGED_ENTRY_PROOF.md](MANAGED_ENTRY_PROOF.md), [PLATFORM_PERFORMANCE_COUNTER.md](PLATFORM_PERFORMANCE_COUNTER.md), and [NEXT_STAGE_BLOCKERS.md](NEXT_STAGE_BLOCKERS.md) for the evidence and blocker separation.

# First managed allocation status

Status: not proven. This document is the current allocation boundary summary; the detailed artifact differential and negative probe remain in [ALLOCATION_GC_PROBE.md](ALLOCATION_GC_PROBE.md).

The allocation-enabled PE (`6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`) contains `ManagedMain -> AllocateOne -> RhpNewFast`. The clean pre-startup allocation probe returns managed status `-10` because the loader-created TLS allocation limit and allocation pointer are both zero. It emits no `FIRST_ALLOCATION_OK` marker. No custom heap, object header, EEType ownership, write barrier, or collection mechanism is used to turn this into a success.

The separate authentic NativeAOT startup path now proves FILETIME, QPC, and QPF, then stops at `api-ms-win-crt-runtime-l1-1-0.dll!_initialize_onexit_table`. The current performance contracts therefore advance startup, but they do not initialize a GC heap or allocation context. The next experiment must close the CRT/bootstrap and NativeAOT PAL ownership contracts before retrying one allocation.

The no-allocation control remains independently positive across three fresh QEMU runs. See [MANAGED_ENTRY_PROOF.md](MANAGED_ENTRY_PROOF.md), [PLATFORM_PERFORMANCE_COUNTER.md](PLATFORM_PERFORMANCE_COUNTER.md), and [NEXT_STAGE_BLOCKERS.md](NEXT_STAGE_BLOCKERS.md) for the evidence and blocker separation.

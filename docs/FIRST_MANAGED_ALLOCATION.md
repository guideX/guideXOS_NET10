# First managed allocation status

Status: not proven. This document is the current allocation boundary summary; the detailed artifact differential and negative probe remain in [ALLOCATION_GC_PROBE.md](ALLOCATION_GC_PROBE.md).

The allocation-enabled PE (`6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`) contains `ManagedMain -> AllocateOne -> RhpNewFast`. The clean pre-startup allocation probe returns managed status `-10` because the loader-created TLS allocation limit and allocation pointer are both zero. It emits no `FIRST_ALLOCATION_OK` marker. No custom heap, object header, EEType ownership, write barrier, or collection mechanism is used to turn this into a success.

The separate authentic NativeAOT startup path now proves FILETIME, QPC, and QPF, initializes the two empty CRT on-exit tables, then stops at `KERNEL32.dll!InitializeSListHead`. The CRT initializer is allocation-free and does not initialize a GC heap, allocation context, managed thread, or callback lifecycle. The first-allocation probe therefore remains `-10`; the next experiment must census the SList/runtime dependency and the remaining NativeAOT PAL ownership contracts before retrying one allocation.

The no-allocation control remains independently positive across three fresh QEMU runs. See [MANAGED_ENTRY_PROOF.md](MANAGED_ENTRY_PROOF.md), [PLATFORM_PERFORMANCE_COUNTER.md](PLATFORM_PERFORMANCE_COUNTER.md), and [NEXT_STAGE_BLOCKERS.md](NEXT_STAGE_BLOCKERS.md) for the evidence and blocker separation.

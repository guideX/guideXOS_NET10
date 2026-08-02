# First managed allocation status

Status: not proven. This document is the current allocation boundary summary; the detailed artifact differential and negative probe remain in [ALLOCATION_GC_PROBE.md](ALLOCATION_GC_PROBE.md).

The allocation-enabled PE (`6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`) contains `ManagedMain -> AllocateOne -> RhpNewFast`. The clean pre-startup allocation probe returns managed status `-10` because the loader-created TLS allocation limit and allocation pointer are both zero. It emits no `FIRST_ALLOCATION_OK` marker. No custom heap, object header, EEType ownership, write barrier, or collection mechanism is used to turn this into a success.

The separate authentic NativeAOT startup path now proves FILETIME, QPC, and QPF, initializes the two empty CRT on-exit tables, initializes one aligned x64 SLIST header, completes the actual one-null `_initterm_e` range, completes the nine-entry `_initterm` range, completes `strcmp("gcServer", "gcConservative")`, completes `strlen("gcServer") = 8`, completes the observed missing `GetEnvironmentVariableW("DOTNET_gcServer")` lookup, and completes 885 checked `_stricmp` calls. The path then stops at `KERNEL32.dll!GetSystemInfo`. These contracts are allocation-free and do not initialize a GC heap, allocation context, or managed thread. The first-allocation probe therefore remains `-10`; the next experiment must close the remaining NativeAOT PAL ownership contracts before retrying one allocation.

The SLIST scope is deliberately initialization-only. The current payload imports `InterlockedFlushSList`, but no push, pop, flush, depth query, or compiler-generated atomic operation on the initialized header was reached. See [PLATFORM_SLIST_CONTRACT.md](PLATFORM_SLIST_CONTRACT.md) for the caller, x64 layout, host vectors, and fresh-process evidence.

The no-allocation control remains independently positive across three fresh QEMU runs. See [MANAGED_ENTRY_PROOF.md](MANAGED_ENTRY_PROOF.md), [PLATFORM_PERFORMANCE_COUNTER.md](PLATFORM_PERFORMANCE_COUNTER.md), and [NEXT_STAGE_BLOCKERS.md](NEXT_STAGE_BLOCKERS.md) for the evidence and blocker separation.

## Query-information follow-on (2026-08-01)

The current startup trace now closes the exact `QueryInformationJobObject` call after process-affinity setup. It returns the no-associated-job failure for `hJob=NULL`, publishes no job structure, and leaves the first-allocation state unchanged. Three fresh immutable runs reach `KERNEL32.dll!GetModuleHandleW`; all preserve zero allocation context, zero managed-thread registration, zero GC-heap usability, and zero managed allocations. A job-information success route is not evidence of a heap or allocator, and the synthetic success experiments are retained only as caller-branch reachability tests.

The 2026-07-29 SLIST evidence-closure pass does not change this allocation status. The SLIST implementation is allocation-free, and all three complete final-hash traces report zero allocation-context pointer/limit, `ALLOCATION_CONTEXT_VALID=0`, no GC-advanced marker, and no managed-thread registration. The SLIST milestone is closed only for initialization.

The 2026-07-30 `_initterm_e`, `_initterm`, and `strcmp` passes, followed by the 2026-07-31 `strlen`, `GetEnvironmentVariableW`, and `_stricmp` passes, leave allocation unproven. `_initterm_e` skipped its one null entry; `_initterm` invoked and returned from all eight actual non-null callbacks; `strcmp` returned `+1`; `strlen` returned `8`; the environment query returned missing-variable status `0` with last error `203`; and `_stricmp` completed 885 checked calls before `GetSystemInfo`. All three final QEMU summaries still report allocation-context pointer/limit `0/0`, `ALLOCATION_CONTEXT_VALID=0`, `MANAGED_THREAD_REGISTERED=0`, and no observable GC advancement. The new deepest boundary is `KERNEL32.dll!GetSystemInfo`.

## `GetSystemInfo` dependency closure (2026-07-31)

The startup path now passes the exact `KERNEL32.dll!GetSystemInfo` call required before the current allocation probe. The implementation is intentionally limited to the current one-CPU, image-backed, 4 KiB loader facts and complete x64 `SYSTEM_INFO` initialization; it does not initialize a GC heap or allocation context. Three immutable positive QEMU runs are recorded under `evidence/generated/getsysteminfo-final-20260731-immutable-v3` and advance to `KERNEL32.dll!GetNumaHighestNodeNumber`. The final serial summaries still report `ALLOCATION_CONTEXT_VALID=0`, `MANAGED_THREAD_REGISTERED=0`, `GC_HEAP_USABLE=0`, and `MANAGED_ALLOCATION_COUNT=0`.

The first managed allocation remains unproven. The next experiment must census the new authentic NUMA dependency and preserve the allocation gate's explicit heap/segment/EEType/object-header evidence requirements; `GetSystemInfo` success is not evidence of GC readiness.

## `GetNumaHighestNodeNumber` does not advance allocation

The bounded `KERNEL32.dll!GetNumaHighestNodeNumber` contract now completes the next NativeAOT startup dependency after `GetSystemInfo`, but it does not change the first-allocation conclusion. The live route publishes highest node `0` from the one-processor/one-locality-domain fact snapshot, and the caller takes its non-NUMA fallback. The positive runs still record zero TLS allocation context, zero managed-thread registration, zero GC heap usability, and zero managed allocations.

The current allocation blocker remains heap ownership and initialization of a real NativeAOT allocation context, including segment reservation/commit, object/EEType publication, roots, write barriers, and lifecycle/GC policy. No NUMA allocator, node-targeted allocation, SMP scheduler, or general topology service was added as part of this contract.

## Process-group capacity probe (2026-08-01)

The exact `KERNEL32.dll!GetProcessGroupAffinity` call is now closed for the current startup path. It is a one-call capacity probe after the one-domain NUMA fallback: current-process pseudo-handle, zero `USHORT` capacity, and null group array. The checked result publishes required count `1` and returns `ERROR_INSUFFICIENT_BUFFER`; the caller reads that count, performs no retry, and does not read group storage. This proves no allocation or GC transition. The next authentic dependency is `KERNEL32.dll!GetProcessAffinityMask`, which remains outside this milestone. See [KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md](KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md).

## `GetProcessAffinityMask` evidence-closure result (2026-08-01)

The exact `GetProcessAffinityMask` contract now completes the next NativeAOT startup dependency, but it does not advance first allocation. Both live calls return process/system masks `0x1`/`0x1`; the bitmap caller updates a processor bitmap, and the processor-count caller derives a one-bit population count before reaching `QueryInformationJobObject`. Final summaries still report zero allocation context, zero managed-thread registration, zero GC heap usability, and zero managed allocations.

## `GetProcAddress` does not advance allocation (2026-08-01)

The current `GetProcAddress(NULL, "RtlDllShutdownInProgress")` closure is a module/export lookup boundary only. The truthful result is `NULL`/`127`, the NativeAOT caller takes its optional fallback, and startup advances to `_register_onexit_function`. The three positive traces still report `GC_CONTRACT_INITIALIZED=0`, `GC_HEAP_USABLE=0`, `ALLOCATION_CONTEXT_VALID=0`, `ALLOCATION_CONTEXT_CREATED=0`, `MANAGED_THREAD_REGISTERED=0`, and `MANAGED_ALLOCATION_COUNT=0`.

No PE export directory, module registry, DLL loader, heap segment, object/EEType publication, write barrier, or collection lifecycle was added. A fabricated non-null function pointer was exercised only as a separately marked investigation control and is not evidence of a first allocation or of a valid Windows export resolution.

## `GetModuleHandleW` does not advance allocation

The current NativeAOT path reaches one non-null `GetModuleHandleW(&L"ntdll.dll")` call after the no-associated-job fallback. guideXOS has no mapped ntdll image, so the truthful narrow contract returns `NULL` with `ERROR_MOD_NOT_FOUND` and stops at `GetProcAddress`. The positive runs still report zero TLS allocation pointer/limit, zero GC contract initialization, zero usable GC heap, zero allocation context, zero managed-thread registration, and zero managed allocations. A module-handle result—even the actual relocated payload base in the null-name checked policy—would not by itself prove heap or GC readiness. The first-allocation blocker remains unchanged.

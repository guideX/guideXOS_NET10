# Next-stage blockers

Gate 4 still proves the first no-allocation managed handoff. The allocation follow-on now builds and traces the generated allocation helper, and the verified FILETIME, monotonic performance, minimal CRT on-exit initialization, x64 SLIST-head initialization, error-returning initializer-table contract, void initializer-table contract, narrow `strcmp`, `strlen`, `GetEnvironmentVariableW`, `_stricmp`, `GetSystemInfo`, `GetNumaHighestNodeNumber`, and `GetProcessGroupAffinity` contracts advance authentic NativeAOT startup beyond QPC. The next boundary is `KERNEL32.dll!GetProcessAffinityMask`; the loader's 32 functional imports remain bounded platform contracts rather than broad implementation of the following runtime services.

| Feature | Gate 4 boundary | Next blocker / smallest next experiment |
| --- | --- | --- |
| First managed allocation | The allocation PE contains `ManagedMain -> AllocateOne -> RhpNewFast`, but the clean pre-startup run sees zero TLS allocation-context slots and returns `-10`; no first-allocation marker is emitted. | Close the standard NativeAOT startup/PAL contract, then run one fixed allocation with explicit heap ownership and object-header/EEType evidence. |
| Repeated allocation | Gate G is correctly gated because Gate F did not pass. | Stress a bounded allocation loop only after first allocation, roots, write barriers, and collection behavior are proven. |
| Virtual memory | `VirtualQuery` is functional only for the active loader stack; no GC segment reservation/commit/release contract is implemented. | Define page ownership, protection, reservation/commit, and release semantics for a real UEFI substrate. |
| GC initialization | `Runtime.WorkstationGC.lib` and its `RhpNewFast` path are physically linked, but the startup now stops at `KERNEL32.dll!GetProcessAffinityMask` after the bounded `GetSystemInfo`, NUMA, and process-group routes; no heap/segments/allocation context are initialized. The observed `DOTNET_gcServer` lookup was absent and was not parsed. | Census only the next authentic process-affinity dependency; do not add dummy success shims or infer GC readiness from the process-group result. |
| Process-time startup | `GetSystemTimeAsFileTime`, `QueryPerformanceCounter`, and `QueryPerformanceFrequency` are functional. The CRT opt-in initializes both empty on-exit tables, and the SLIST opt-in initializes one x64 header. | No longer a blocker for this pass. Preserve the exact time, CRT, and initialization-only SLIST contracts; keep registration/execution and SLIST companions separate. |
| CRT on-exit lifecycle | `_initialize_onexit_table` is proven for two empty tables. The current register-enabled trace reaches `_register_onexit_function`, proves the initialized encoded-null table, and returns `GROWTH_REQUIRED` / `-1` at `_recalloc_crt_t(_PVFV,NULL,0x20)` without allocation or callback execution. Execution, shutdown, and callback ownership remain unimplemented. | Keep `_recalloc_crt_t` as a separate allocator milestone. Do not add `_execute_onexit_table`, shutdown, or general CRT teardown to this contract. |
| Error-returning CRT initializers | The actual range is one null `.rdata` entry; `_initterm_e` validates, skips it, returns zero, and reaches the now-closed `_initterm` range. No actual NativeAOT callback was present in this family. | Keep other `.CRT` families separate. Do not generalize these table results into C++ processing or implement initializer entries not present in a traced artifact. |
| Monotonic performance counter | QPC returns normalized signed-64 units backed by the ACPI PM timer at port `0x608`, width 24, frequency `0x369E99`; QPF returns the same positive frequency. CPUID invariant TSC/leaf 15 is supported in code but unavailable on the default QEMU CPU. | No longer a blocker. Preserve the host vectors, source-selection negative, and QEMU Stall probe as regression tests. |
| Runtime thread state | One boot CPU has a synthesized TLS vector, TLS block, TEB-like stack fields, FLS slots, identity, pseudo handles, and one-thread lock state. The deeper runtime trace reaches `CreateThread`, which is not implemented. | Prove actual thread creation, context/TLS ownership, scheduler interaction, and lifecycle teardown. |
| TLS | The PE TLS template and `_tls_index` are initialized for one thread; the image has no TLS callback work in this probe. | Prove TLS allocation/reclamation and callbacks across actual thread creation. |
| Exceptions | No managed exception path is present; `RhpThrowEx`, `RaiseException`, and broad diagnostics are fail-fast. | Decide whether to implement an exception ABI or keep exceptions outside the freestanding profile. |
| Unwinding | `.pdata` is loaded as image data, but Windows `RtlVirtualUnwind`/context services and registration are not implemented. | Build a separately verified x64 unwind registration/lookup experiment before any exception or stack walk. |
| `finally` | No `try/finally` is in the probe; `RhpCallFinallyFunclet` is only linked runtime composition. | Test only after exceptions/unwinding have a proven contract. |
| Synchronization | The functional critical-section implementation supports one-thread recursion and deterministic contention failure solely for startup; events, waits, thread suspension, scheduler services, and SLIST push/pop/flush/depth operations fail fast or remain absent. | Define a scheduler/locking contract before exposing synchronization to managed code. Do not infer general lock-free list support from head initialization. |
| Static constructor behavior | This artifact has no reachable user static constructor; NativeAOT still contains module-initializer metadata. | Build one controlled static-constructor variant and trace whether legitimate startup requires module initialization. |

Other deferred areas are CRT startup/termination, process time/environment, COM, diagnostics, networking, filesystem, globalization, and broad Windows compatibility. The current 95 fail-fast imports make accidental entry into those areas visible. Do not port them as no-op shims.

## `CreateMemoryResourceNotification` payload boundary (2026-08-07)

The exact `KERNEL32.dll!CreateMemoryResourceNotification` route is now closed
for raw type `0` (`LowMemoryResourceNotification`). It creates a typed,
generation-checked opaque `MemoryResourceNotification` handle backed by the
common scheduler waitable foundation, initialized nonsignaled with zero
waiters and one public reference. The fixed 16-entry object registry, 12 Event
records, and 6 TCBs were not expanded; one separate notification record slot
is used. No pressure model, query, close, duplicate, wait, signal, reset, UEFI
event, worker, or additional payload import was added.

Three fresh enabled QEMU runs using the required payload hash agreed on one
successful notification creation, storage at `base + 0xADA28`, and the next
honest blocker:
`KERNEL32.dll!CreateThread` (descriptor `2`, symbol index `0x2D`, IAT RVA
`0x7D1A0`, caller RVA `0x3CFA0`). The disabled control retains the exact
CreateMemoryResourceNotification blocker after the two established Event
calls. See [KERNEL32_CREATEMEMORYRESOURCENOTIFICATION_BOOTSTRAP.md](KERNEL32_CREATEMEMORYRESOURCENOTIFICATION_BOOTSTRAP.md).

## `CreateEventW` payload boundary (2026-08-07)

The exact `KERNEL32.dll!CreateEventW` route is implemented only for unnamed
events with NULL `SECURITY_ATTRIBUTES`. It creates real guideXOS Event
objects and generation/type-checked opaque handles. Three fresh enabled QEMU
runs agree on two successful calls (`FALSE/FALSE`, then `TRUE/FALSE`), two
live Event objects, two live public handles, zero waiters, and no added thread.
The disabled route restores the original unresolved CreateEventW boundary
without creating an object.

The exact payload next reaches `CreateMemoryResourceNotification` at descriptor
`2`, symbol index `0x36`, and IAT RVA `0x7D1E8`. That import is handled only by
the separately scoped notification milestone above. Do not infer the later
CreateEventW oracle or implement named events, security attributes, waits,
signaling/reset, close, duplicate, thread creation, or timeout scheduling from
this Event boundary.

## `strcmp` evidence-closure result (2026-07-30)

The narrow Microsoft x64 `strcmp` contract is closed by [CRT_STRCMP_BOOTSTRAP.md](CRT_STRCMP_BOOTSTRAP.md). Three immutable fresh QEMU runs compare `gcServer` with `gcConservative`, return `+1`, preserve zero TLS allocation context/managed-thread/GC state, and reach `strlen`. The subsequent narrow `strlen` contract is closed by [CRT_STRLEN_BOOTSTRAP.md](CRT_STRLEN_BOOTSTRAP.md), and the missing `DOTNET_gcServer` lookup is closed by [KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md](KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md); the next boundary is `api-ms-win-crt-string-l1-1-0.dll!_stricmp`.

## Recommended next milestone

Investigate the exact `KERNEL32.dll!GetProcessAffinityMask` dependency reached after the bounded `GetSystemInfo`, NUMA, and process-group routes only if that API is explicitly brought into scope. Capture imports, undefined symbols, TLS, module initializer behavior, and transition-helper changes before writing any additional UEFI platform code.

## SLIST evidence-closure result (2026-07-29)

The narrow `InitializeSListHead` implementation is complete and host-tested, and the requested three consecutive complete final-hash QEMU runs are closed by `evidence\generated\slist-final-20260730-immutable`. The three fresh processes used one immutable artifact set, proved the exact 16-byte empty-header contract, retained complete summaries, and reached `_initterm_e`. The prior QEMU shutdowns were guest triple faults from the bounded diagnostic IDT; packing `IDTR` and preserving firmware IRQ vectors corrected the harness. No allocation, GC initialization, managed-thread registration, or general SLIST mutation is implied.

## `_initterm_e` evidence-closure result (2026-07-30)

The one-entry error-returning initializer range is now closed for the actual artifact. Three fresh immutable-hash QEMU runs validated the exclusive bounds, skipped the sole null entry, returned zero, and reached the now-closed `_initterm` range. The host suite proves the non-empty callback cases, exact first-error propagation, exclusive-end behavior, and malformed-range/target rejection. No callback ran in QEMU because no non-null initializer exists in the traced table. The next milestone is the separately scoped `strcmp` dependency, not GC, allocation, managed-thread registration, or general CRT startup.

## `strlen` evidence-closure result (2026-07-31)

The bounded Microsoft x64 `strlen` contract is closed for the actual NativeAOT call. Host vectors cover empty, ordinary, embedded-null, high-bit, long, maximum-scan, null, noncanonical, out-of-image, unreadable, gap, unterminated, overflow, guard, unchanged-input, approved-region, and mutation-control cases. Three immutable QEMU runs report one successful `strlen("gcServer")` call with result `8`, preserve zero allocation context/managed-thread/GC state, and advance to `KERNEL32.dll!GetEnvironmentVariableW`. The disabled profile preserves the original `strlen` boundary, and the evidence pipeline rejects marker, truncation, stale-run, duplicate-process, and artifact-hash mutations.

## `GetEnvironmentVariableW` evidence-closure result (2026-07-31)

The narrow Microsoft x64 `GetEnvironmentVariableW` contract is closed by [KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md](KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md). Three immutable fresh QEMU runs made one missing-variable lookup each for `DOTNET_gcServer`, returned `0`, changed last error to `203`, and advanced to `api-ms-win-crt-string-l1-1-0.dll!_stricmp`. The enabled treatment is 28 functional / 96 fail-fast / 0 unresolved, while the disabled route retains the original GetEnvironmentVariableW boundary. Host vectors, regression suites, and disabled/stale/marker/duplicate/hash controls passed.

The next milestone is the exact `_stricmp` call reached after the absent GC-configuration lookup. Keep the environment contract narrow; do not add process-wide environment management, registry integration, expansion, allocation, or GC behavior as part of that investigation.

## `_stricmp` evidence-closure result (2026-07-31)

The narrow Microsoft x64 `_stricmp` contract is closed by [CRT_STRICMP_BOOTSTRAP.md](CRT_STRICMP_BOOTSTRAP.md). Three fresh positive QEMU runs completed 885 checked calls with zero failures, preserved zero allocation/GC state, and advanced to `KERNEL32.dll!GetSystemInfo`. The disabled control retained the exact `_stricmp` fail-fast boundary. The next experiment is `GetSystemInfo`; do not infer GC readiness or implement broader PAL services from this CRT closure.

## `GetSystemInfo` evidence-closure result (2026-07-31)

The next experiment is complete. Three immutable positive QEMU runs under `evidence/generated/getsysteminfo-final-20260731-immutable-v3` fill and return the exact x64 `SYSTEM_INFO` structure, prove the observed `0xA2` consumer mask, preserve zero allocation/GC state, and advance to `KERNEL32.dll!GetNumaHighestNodeNumber`. The disabled control preserves the original GetSystemInfo boundary, and the marker-mutation control proves that `GETSYSTEMINFO_OX` is not accepted as positive success.

The smallest next dependency is the exact `GetNumaHighestNodeNumber` contract. Keep the image-backed address-range and one-bootstrap-processor policies explicit; do not broaden this result into a general NUMA, processor-topology, virtual-memory, or GC implementation. First allocation remains blocked until heap ownership, segment reservation, allocation context, thread registration, and object/EEType evidence are separately proven.

## `GetNumaHighestNodeNumber` evidence-closure result (2026-08-01)

The exact Microsoft x64 `KERNEL32.dll!GetNumaHighestNodeNumber` contract is now closed for the current startup path. The checked output is a four-byte `ULONG`; the final one-domain policy publishes highest node `0`, and the caller selects its non-NUMA fallback. Three immutable positive runs under `evidence\generated\getnumahighest-final-20260801-immutable-v2` reach the next authentic `KERNEL32.dll!GetProcessGroupAffinity` boundary. The enabled census is `31 / 93 / 0`; the disabled control remains `30 / 94 / 0` at the original NUMA boundary.

This closure does not implement or infer `GetLogicalProcessorInformation`, `GetLogicalProcessorInformationEx`, `VirtualAllocExNuma`, node-targeted allocation, SMP scheduling, ACPI NUMA discovery, or GC initialization. The controlled failure branch proves that a failed BOOL leaves the output unread and takes the caller's failure fallback; its forced `0x32` last-error value is test policy, not a universal Windows error claim.

The next authentic dependency is `GetProcessGroupAffinity`. The first-allocation blocker is unchanged: no heap segments, allocation context, managed-thread registration, object/EEType publication, write barriers, or GC lifecycle have been proven.

## `GetModuleHandleW` closure (2026-08-01)

The exact live call is `GetModuleHandleW(&L"ntdll.dll")`. The named module is not mapped in the guideXOS process model, so the smallest truthful implementation returns `NULL` with `ERROR_MOD_NOT_FOUND` and leaves `GetProcAddress` as the next authentic dependency. The null-name current-executable query is checked against the actual relocated payload base but is not substituted for the observed named call. No general loader, DLL search, module registry, enumeration, reference counting, unloading, or cross-process handle model was added.

The first-allocation blocker remains unchanged: no GC heap, allocation context, managed-thread registration, object publication, write barriers, or collection lifecycle is proven. The recommended next milestone is the exact `KERNEL32.dll!GetProcAddress` call reached after this bounded module-name failure.

## `GetProcessGroupAffinity` evidence-closure result (2026-08-01)

The exact current-path contract is closed under [KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md](KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md). The caller makes one `GroupCount=0`/`GroupArray=NULL` capacity probe, receives required count `1` with `ERROR_INSUFFICIENT_BUFFER`, consumes the count, and does not retry. The next authentic dependency is `KERNEL32.dll!GetProcessAffinityMask`; it is intentionally not implemented here. Do not infer process-affinity, processor-group topology, NUMA allocation, thread scheduling, heap initialization, or GC readiness from this probe.

## `GetProcessAffinityMask` evidence-closure result (2026-08-01)

The exact Microsoft x64 process-affinity contract is closed under [KERNEL32_GETPROCESSAFFINITYMASK_BOOTSTRAP.md](KERNEL32_GETPROCESSAFFINITYMASK_BOOTSTRAP.md). The current-process pseudo-handle returns the one initialized bootstrap processor as both process and system mask `0x1`. The two callers consume only the process mask: one updates a bitmap, and one performs a manual population count before `QueryInformationJobObject`. The final QEMU evidence preserves zero GC/allocation state. The next authentic dependency is `KERNEL32.dll!QueryInformationJobObject`; no other affinity or topology API was implemented.

## `GetProcAddress` closure (2026-08-01)

The exact `KERNEL32.dll!GetProcAddress` call is now closed for the current startup path. The previous `GetModuleHandleW(&L"ntdll.dll")` failure supplies `NULL`; the exact name is `RtlDllShutdownInProgress`; and the checked route returns `NULL`/`ERROR_PROC_NOT_FOUND` (`127`) without export parsing. The caller takes its optional fallback and the next authentic dependency is `api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function`.

Do not broaden this result into a PE export resolver, forwarded-export resolver, DLL search/load service, module registry, or `ntdll` alias. The first-allocation blocker is unchanged: no GC heap, allocation context, managed-thread registration, object publication, write barriers, or collection lifecycle is proven.

## Query-information closure (2026-08-01)

`QueryInformationJobObject` is now closed only for the live `hJob=NULL` / class-15 / eight-byte CPU-rate query. The guideXOS snapshot has no associated job, so the caller's no-job fallback is proven and the next authentic dependency is `KERNEL32.dll!GetModuleHandleW`. The dormant class-9 static reference remains a future census item, not a live startup blocker. This closure does not change the first-allocation blocker: the allocation context, GC heap, managed-thread registration, object publication, write barriers, and collection lifecycle remain unproven.

## `_register_onexit_function` evidence-closure result (2026-08-02)

The exact Microsoft x64 `api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function` contract is now reached and bounded. Three immutable positive runs preserve the two earlier initialization results, match the first initialized table, decode all three fields as zero, and stop at the UCRT initial growth request `_recalloc_crt_t(_PVFV,NULL,0x20)`. The profile returns `-1` / `GROWTH_REQUIRED`, makes no allocation attempt, leaves the encoded-null table unchanged, and executes no callback. The disabled control retains the authentic register fail-fast boundary. This does not prove the allocator, callback execution, shutdown, GC readiness, or first managed allocation. See [CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md](CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md).

# Next-stage blockers

Gate 4 still proves the first no-allocation managed handoff. The allocation follow-on now builds and traces the generated allocation helper, and the verified FILETIME, monotonic performance, minimal CRT on-exit initialization, x64 SLIST-head initialization, error-returning initializer-table contract, void initializer-table contract, and narrow `strcmp` contract advance authentic NativeAOT startup beyond QPC. The next boundary is `api-ms-win-crt-string-l1-1-0.dll!strlen`; the loader's 26 functional imports remain bounded platform contracts rather than broad implementation of the following runtime services.

| Feature | Gate 4 boundary | Next blocker / smallest next experiment |
| --- | --- | --- |
| First managed allocation | The allocation PE contains `ManagedMain -> AllocateOne -> RhpNewFast`, but the clean pre-startup run sees zero TLS allocation-context slots and returns `-10`; no first-allocation marker is emitted. | Close the standard NativeAOT startup/PAL contract, then run one fixed allocation with explicit heap ownership and object-header/EEType evidence. |
| Repeated allocation | Gate G is correctly gated because Gate F did not pass. | Stress a bounded allocation loop only after first allocation, roots, write barriers, and collection behavior are proven. |
| Virtual memory | `VirtualQuery` is functional only for the active loader stack; no GC segment reservation/commit/release contract is implemented. | Define page ownership, protection, reservation/commit, and release semantics for a real UEFI substrate. |
| GC initialization | `Runtime.WorkstationGC.lib` and its `RhpNewFast` path are physically linked, but the `strcmp`-enabled startup now stops at `api-ms-win-crt-string-l1-1-0.dll!strlen`; no heap/segments/allocation context are initialized. | Census only the next authentic `strlen` dependency; do not add dummy success shims or infer GC readiness from CRT progress. |
| Process-time startup | `GetSystemTimeAsFileTime`, `QueryPerformanceCounter`, and `QueryPerformanceFrequency` are functional. The CRT opt-in initializes both empty on-exit tables, and the SLIST opt-in initializes one x64 header. | No longer a blocker for this pass. Preserve the exact time, CRT, and initialization-only SLIST contracts; keep registration/execution and SLIST companions separate. |
| CRT on-exit lifecycle | `_initialize_onexit_table` is proven for two empty tables; registration, execution, shutdown, and callback ownership were not reached. | Do not implement registration or shutdown until a fresh trace reaches them; treat `_register_onexit_function` and `_execute_onexit_table` as separate contracts. |
| Error-returning CRT initializers | The actual range is one null `.rdata` entry; `_initterm_e` validates, skips it, returns zero, and reaches the now-closed `_initterm` range. No actual NativeAOT callback was present in this family. | Keep other `.CRT` families separate. Do not generalize these table results into C++ processing or implement initializer entries not present in a traced artifact. |
| Monotonic performance counter | QPC returns normalized signed-64 units backed by the ACPI PM timer at port `0x608`, width 24, frequency `0x369E99`; QPF returns the same positive frequency. CPUID invariant TSC/leaf 15 is supported in code but unavailable on the default QEMU CPU. | No longer a blocker. Preserve the host vectors, source-selection negative, and QEMU Stall probe as regression tests. |
| Runtime thread state | One boot CPU has a synthesized TLS vector, TLS block, TEB-like stack fields, FLS slots, identity, pseudo handles, and one-thread lock state. The deeper runtime trace reaches `CreateThread`, which is not implemented. | Prove actual thread creation, context/TLS ownership, scheduler interaction, and lifecycle teardown. |
| TLS | The PE TLS template and `_tls_index` are initialized for one thread; the image has no TLS callback work in this probe. | Prove TLS allocation/reclamation and callbacks across actual thread creation. |
| Exceptions | No managed exception path is present; `RhpThrowEx`, `RaiseException`, and broad diagnostics are fail-fast. | Decide whether to implement an exception ABI or keep exceptions outside the freestanding profile. |
| Unwinding | `.pdata` is loaded as image data, but Windows `RtlVirtualUnwind`/context services and registration are not implemented. | Build a separately verified x64 unwind registration/lookup experiment before any exception or stack walk. |
| `finally` | No `try/finally` is in the probe; `RhpCallFinallyFunclet` is only linked runtime composition. | Test only after exceptions/unwinding have a proven contract. |
| Synchronization | The functional critical-section implementation supports one-thread recursion and deterministic contention failure solely for startup; events, waits, thread suspension, scheduler services, and SLIST push/pop/flush/depth operations fail fast or remain absent. | Define a scheduler/locking contract before exposing synchronization to managed code. Do not infer general lock-free list support from head initialization. |
| Static constructor behavior | This artifact has no reachable user static constructor; NativeAOT still contains module-initializer metadata. | Build one controlled static-constructor variant and trace whether legitimate startup requires module initialization. |

Other deferred areas are CRT startup/termination, process time/environment, COM, diagnostics, networking, filesystem, globalization, and broad Windows compatibility. The current 103 fail-fast imports make accidental entry into those areas visible. Do not port them as no-op shims.

## `strcmp` evidence-closure result (2026-07-30)

The narrow Microsoft x64 `strcmp` contract is closed by [CRT_STRCMP_BOOTSTRAP.md](CRT_STRCMP_BOOTSTRAP.md). Three immutable fresh QEMU runs compare `gcServer` with `gcConservative`, return `+1`, preserve zero TLS allocation context/managed-thread/GC state, and reach `api-ms-win-crt-string-l1-1-0.dll!strlen`. `strlen` remains intentionally unimplemented.

## Recommended next milestone

Investigate the exact `api-ms-win-crt-string-l1-1-0.dll!strlen` dependency reached after the proven `strcmp` call, then reassess the first allocation/GC boundary. Capture imports, undefined symbols, TLS, module initializer behavior, and transition-helper changes before writing any additional UEFI platform code.

## SLIST evidence-closure result (2026-07-29)

The narrow `InitializeSListHead` implementation is complete and host-tested, and the requested three consecutive complete final-hash QEMU runs are closed by `evidence\generated\slist-final-20260730-immutable`. The three fresh processes used one immutable artifact set, proved the exact 16-byte empty-header contract, retained complete summaries, and reached `_initterm_e`. The prior QEMU shutdowns were guest triple faults from the bounded diagnostic IDT; packing `IDTR` and preserving firmware IRQ vectors corrected the harness. No allocation, GC initialization, managed-thread registration, or general SLIST mutation is implied.

## `_initterm_e` evidence-closure result (2026-07-30)

The one-entry error-returning initializer range is now closed for the actual artifact. Three fresh immutable-hash QEMU runs validated the exclusive bounds, skipped the sole null entry, returned zero, and reached the now-closed `_initterm` range. The host suite proves the non-empty callback cases, exact first-error propagation, exclusive-end behavior, and malformed-range/target rejection. No callback ran in QEMU because no non-null initializer exists in the traced table. The next milestone is the separately scoped `strcmp` dependency, not GC, allocation, managed-thread registration, or general CRT startup.

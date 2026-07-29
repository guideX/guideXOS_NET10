# Next-stage blockers

Gate 4 still proves the first no-allocation managed handoff. The allocation follow-on now builds and traces the generated allocation helper, and the verified time contract advances authentic NativeAOT startup beyond `GetSystemTimeAsFileTime`. The next boundary is `KERNEL32.dll!QueryPerformanceCounter`; the loader's 19 functional imports remain a bounded platform contract rather than broad implementation of the following runtime services.

| Feature | Gate 4 boundary | Next blocker / smallest next experiment |
| --- | --- | --- |
| First managed allocation | The allocation PE contains `ManagedMain -> AllocateOne -> RhpNewFast`, but the clean pre-startup run sees zero TLS allocation-context slots and returns `-10`; no first-allocation marker is emitted. | Close the standard NativeAOT startup/PAL contract, then run one fixed allocation with explicit heap ownership and object-header/EEType evidence. |
| Repeated allocation | Gate G is correctly gated because Gate F did not pass. | Stress a bounded allocation loop only after first allocation, roots, write barriers, and collection behavior are proven. |
| Virtual memory | `VirtualQuery` is functional only for the active loader stack; no GC segment reservation/commit/release contract is implemented. | Define page ownership, protection, reservation/commit, and release semantics for a real UEFI substrate. |
| GC initialization | `Runtime.WorkstationGC.lib` and its `RhpNewFast` path are physically linked, but standard startup now stops at the unprovided `QueryPerformanceCounter` import and no heap/segments/allocation context are initialized. | Implement the required NativeAOT PAL services or identify a supported freestanding runtime configuration; do not add dummy success shims. |
| Process-time startup | The exact `GetSystemTimeAsFileTime` call now returns a verified FILETIME once through the security-cookie consumer. The next fail-fast import is `KERNEL32.dll!QueryPerformanceCounter` at IAT RVA `0x7e0c8`, call site `0x1800782f9`. | Trace and define the exact counter contract in a separate milestone; do not implement it as part of the time contract. |
| Runtime thread state | One boot CPU has a synthesized TLS vector, TLS block, TEB-like stack fields, FLS slots, identity, pseudo handles, and one-thread lock state. The deeper runtime trace reaches `CreateThread`, which is not implemented. | Prove actual thread creation, context/TLS ownership, scheduler interaction, and lifecycle teardown. |
| TLS | The PE TLS template and `_tls_index` are initialized for one thread; the image has no TLS callback work in this probe. | Prove TLS allocation/reclamation and callbacks across actual thread creation. |
| Exceptions | No managed exception path is present; `RhpThrowEx`, `RaiseException`, and broad diagnostics are fail-fast. | Decide whether to implement an exception ABI or keep exceptions outside the freestanding profile. |
| Unwinding | `.pdata` is loaded as image data, but Windows `RtlVirtualUnwind`/context services and registration are not implemented. | Build a separately verified x64 unwind registration/lookup experiment before any exception or stack walk. |
| `finally` | No `try/finally` is in the probe; `RhpCallFinallyFunclet` is only linked runtime composition. | Test only after exceptions/unwinding have a proven contract. |
| Synchronization | The functional critical-section implementation supports one-thread recursion and deterministic contention failure solely for startup; events, waits, thread suspension, and scheduler services fail fast. | Define a scheduler/locking contract before exposing synchronization to managed code. |
| Static constructor behavior | This artifact has no reachable user static constructor; NativeAOT still contains module-initializer metadata. | Build one controlled static-constructor variant and trace whether legitimate startup requires module initialization. |

Other deferred areas are CRT startup/termination, process time/environment, COM, diagnostics, networking, filesystem, globalization, and broad Windows compatibility. The 106 fail-fast imports make accidental entry into those areas visible. Do not port them as no-op shims.

## Recommended next milestone

Create a one-variable-at-a-time NativeAOT runtime-startup reproducer for the first allocation/GC boundary. Capture imports, undefined symbols, TLS, module initializer behavior, and transition-helper changes before writing any additional UEFI platform code.

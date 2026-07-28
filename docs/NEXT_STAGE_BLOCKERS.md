# Next-stage blockers

Gate 4 proves only the first no-allocation managed handoff. The loader's 18 functional imports are a bounded startup contract, not implementation of the following runtime services. None of these items is started by this milestone.

| Feature | Gate 4 boundary | Next blocker / smallest next experiment |
| --- | --- | --- |
| First managed allocation | The probe uses only primitive fields, `stackalloc`, and a raw callback. `RhpNew*` and CRT heap imports remain fail-fast. | Establish a supported NativeAOT heap/GC initialization contract, then run one fixed allocation with explicit ownership evidence. |
| Repeated allocation | No object allocation or collection is exercised. | Stress a bounded allocation loop only after first allocation and collection are proven. |
| Virtual memory | `VirtualQuery` is functional only for the active loader stack; `VirtualAlloc`, `VirtualFree`, `VirtualAllocExNuma`, `VirtualUnlock`, and related imports fail fast. | Define page ownership, protection, reservation/commit, and release semantics for a real UEFI substrate. |
| GC initialization | `Runtime.WorkstationGC.lib` is physically linked, but no heap, segments, frozen roots, or collection are initialized. | Determine whether the toolchain exposes a supported no-GC startup or requires a bounded GC port. |
| Runtime thread state | One boot CPU has a synthesized TLS vector, TLS block, TEB-like stack fields, FLS slots, identity, pseudo handles, and one-thread lock state. | Prove the supported representation for additional threads and lifecycle teardown. |
| TLS | The PE TLS template and `_tls_index` are initialized for one thread; the image has no TLS callback work in this probe. | Prove TLS allocation/reclamation and callbacks across actual thread creation. |
| Exceptions | No managed exception path is present; `RhpThrowEx`, `RaiseException`, and broad diagnostics are fail-fast. | Decide whether to implement an exception ABI or keep exceptions outside the freestanding profile. |
| Unwinding | `.pdata` is loaded as image data, but Windows `RtlVirtualUnwind`/context services and registration are not implemented. | Build a separately verified x64 unwind registration/lookup experiment before any exception or stack walk. |
| `finally` | No `try/finally` is in the probe; `RhpCallFinallyFunclet` is only linked runtime composition. | Test only after exceptions/unwinding have a proven contract. |
| Synchronization | The functional critical-section implementation supports one-thread recursion and deterministic contention failure solely for startup; events, waits, thread suspension, and scheduler services fail fast. | Define a scheduler/locking contract before exposing synchronization to managed code. |
| Static constructor behavior | This artifact has no reachable user static constructor; NativeAOT still contains module-initializer metadata. | Build one controlled static-constructor variant and trace whether legitimate startup requires module initialization. |

Other deferred areas are CRT startup/termination, COM, process environment, diagnostics, networking, filesystem, globalization, and broad Windows compatibility. The 106 fail-fast imports make accidental entry into those areas visible. Do not port them as no-op shims.

## Recommended next milestone

Create a one-variable-at-a-time NativeAOT runtime-startup reproducer for the first allocation/GC boundary. Capture imports, undefined symbols, TLS, module initializer behavior, and transition-helper changes before writing any additional UEFI platform code.

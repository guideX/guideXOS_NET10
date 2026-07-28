# Next-stage blockers

Managed entry has not yet been reached. The table records what is known and avoids turning a successful host build into a claim about later runtime features.

| Feature | Current evidence | Status / smallest next experiment |
| --- | --- | --- |
| Static initialization | ILC contains `--initassembly` for CoreLib, TypeLoader, and Reflection.Execution; the map contains `ModuleInitializerList` and runtime configuration data. | Unproven. First isolate the minimum NativeAOT startup sequence and identify which initialization is needed before `ManagedMain`. |
| Strings | The method emits bytes from stack memory and contains no managed string literal. Runtime composition still contains string/format helpers and CRT imports. | Method-level use is avoided; runtime string support remains unresolved. Build a no-string static experiment only after runtime startup is understood. |
| First managed allocation | The object has `RhpNewFast`, `RhpNewArrayFast`, `RhAllocateNewObject`, `RhAllocateNewArray`, and CRT heap imports. | Blocked. Do not add allocation until a real GC/heap initialization contract exists. |
| Repeated allocation | `RhpCollect`, handles, finalization, and GC helpers are present in the runtime composition. | Blocked. Requires stress/repetition tests after first allocation, not inferred from entry success. |
| Memory reclamation | Workstation GC is physically linked and imports Windows virtual memory APIs. | Blocked. Need page/heap ownership, GC segment setup, protection, and reclamation evidence. |
| Runtime thread state | `_tls_index`, `RhpReversePInvoke`, `RhpPInvoke`, FLS/thread APIs, and thread-related runtime helpers are present. | Current direct PE handoff stops before these execute. Establish one documented boot CPU thread state before attempting transfer. |
| Exceptions | `RhpThrowEx`, catch/filter/finally helpers, `RaiseException`, and fail-fast support are present. | Explicit non-goal for this milestone. Keep exceptions disabled in the proof and document every startup assumption. |
| Stack unwinding | `.pdata` and PE exception metadata are present; the static experiment needs Windows unwind/context APIs. | Blocked. Register or replace unwind support only through a separately verified ABI experiment. |
| `finally` | `RhpCallFinallyFunclet` is present in the object even though the source has no `try/finally`. | Unproven and out of scope. No `finally` claim follows from the no-exception entry. |
| Synchronization | Event, wait, critical-section, interlocked, and thread helpers are present in the runtime library/import set. | Blocked. No locks, events, waits, or synchronization should be introduced. |
| GC integration | `Runtime.WorkstationGC.lib` is selected by the standard Windows NativeAOT link response; static linking leaves broad platform dependencies. | Blocked. The next milestone should first determine whether a supported no-GC startup configuration exists; do not port a full GC here. |

Additional blockers are PE TLS initialization, `.pdata` unwind registration, module/DLL initialization, import resolution, and a documented termination path. The current harness deliberately halts after reporting the import boundary.

## Recommended next milestone

Create a minimal NativeAOT runtime-startup reproducer that differs only by one controlled variable at a time: shared PE versus static object, startup initialization enabled versus isolated, and standard GC runtime versus a supported no-GC configuration if the toolchain exposes one. Capture the resulting imports, undefined symbols, TLS, module initializer, and transition-helper changes before writing any UEFI platform replacement.

The next milestone should not port guideXOS kernel or desktop code, copy the legacy runtime, add broad stubs, or claim managed execution from a native pre-print.

# Dependency census

This document records the exact imports of the Gate 1 shared NativeAOT artifact, the Gate 4 reachability experiment, and the allocation differential. The current no-allocation control is the reproducible `win-x64` shared image whose SHA-256 is `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`.

The initial Gate 4 stop was a correct ten-descriptor/124-symbol census. The current `_stricmp` control patches every IAT slot: 29 imports have bounded functional implementations, including the exact `GetSystemTimeAsFileTime`, `QueryPerformanceCounter`, `QueryPerformanceFrequency`, `_initialize_onexit_table`, `InitializeSListHead`, `_initterm_e`, `_initterm`, `strcmp`, `strlen`, `GetEnvironmentVariableW`, and `_stricmp` contracts, and the other 95 receive deterministic guideXOS-owned fail-fast stubs. The current `GetSystemInfo` opt-in adds one exact `KERNEL32.dll!GetSystemInfo` route for 30 functional / 94 fail-fast imports. A fail-fast stub is not support for the service; it proves that the symbol is not reached by the legitimate path. The allocation-enabled variant keeps the same 10/124 import set and adds only managed allocation code and metadata; it does not make the remaining services functional.

Final control treatment totals for the performance-enabled build are A=functional `21`, B=deterministic fail-fast `103`, C=import elimination `0`, and D=deferred required symbols `0`. The CRT opt-in follow-on adds `_initialize_onexit_table`, for A=`22` / B=`102`; the SLIST opt-in adds `InitializeSListHead`, for A=`23` / B=`101`; the `_initterm_e` opt-in adds one more, for A=`24` / B=`100`; the `_initterm` opt-in adds one more, for A=`25` / B=`99`; the `strcmp` opt-in adds one more, for A=`26` / B=`98`; the `strlen` opt-in adds one more, for A=`27` / B=`97`; the `GetEnvironmentVariableW` opt-in adds one more, for A=`28` / B=`96`; and the `_stricmp` opt-in adds one more, for A=`29` / B=`95`. The separate `GetSystemInfo` opt-in is A=`30` / B=`94`. All other deferred symbols retain their fail-fast treatment. The no-allocation control still passes managed entry. The current allocation/startup trace initializes both empty on-exit tables, initializes one x64 SLIST header, completes the one-entry `_initterm_e` range, completes the nine-entry `_initterm` range, compares `gcServer` with `gcConservative`, computes `strlen("gcServer") = 8`, queries missing `DOTNET_gcServer`, completes 885 checked `_stricmp` calls, completes the bounded `GetSystemInfo` contract, and stops at `KERNEL32.dll!GetNumaHighestNodeNumber`; the first allocation probe remains `-10` without a GC allocation context.

## `QueryInformationJobObject` addendum (2026-08-01)

The query-enabled artifact is a separate exact-import closure over the current process-affinity endpoint. It changes the census to `34` functional / `90` deterministic fail-fast / `0` unresolved required imports. The new functional symbol is only `KERNEL32.dll!QueryInformationJobObject` at IAT RVA `0x7d1f0`; its preferred live call is `0x18003cca1`. The disabled control remains `33 / 91 / 0` and stops at the query import.

Static disassembly contains a second reference at `0x1800432bd` for class `9` and a `0x90`-byte buffer. It is retained in the inventory as dormant static reachability. The actual startup path records one class-15 call per QEMU run, returns the modeled no-associated-job failure, and advances to `KERNEL32.dll!GetModuleHandleW`. The exact call, class, structure, fifth stack argument, branch, output mutation, and next-boundary evidence is in [KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md](KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md).

## Exact descriptor and symbol inventory

IAT RVAs below are relative to the preferred image base `0x180000000`. Each `symbol (RVA)` pair is one imported symbol; no ordinal-only import exists in this artifact. “Before” means before the first instruction of the exported `ManagedMain` thunk. “Probe” means the legitimate path from that thunk through validation, the borrowed callback, and return.

| Module | Symbol or ordinal and IAT RVA | Referenced by / earliest possible call site | Required before ManagedMain | Required during probe | Proposed treatment | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| `ADVAPI32.dll` | `RegisterEventSourceW (0x7d000)`; `ReportEventW (0x7d008)`; `OpenProcessToken (0x7d010)`; `AdjustTokenPrivileges (0x7d018)`; `LookupPrivilegeValueW (0x7d020)`; `DeregisterEventSource (0x7d028)` | NativeAOT diagnostics and Windows privilege/large-page support retained by `aotminipal`/runtime composition; no reachable call in the successful export path; PE thunks are in the `.text` import-thunk region near `0x1800772xx` | No call; IAT must be patched | No | B: unique `UNEXPECTED_IMPORT_CALL:ADVAPI32.dll!<symbol>` stub, halt | `objdump -p`; `link.rsp`; fail-fast negative control invoked `RegisterEventSourceW` and halted before managed entry |
| `bcrypt.dll` | `BCryptGenRandom (0x7d3f8)` | NativeAOT PAL random-byte support; no reachable call in the successful path | No call; IAT must be patched | No | B: fail-fast stub | `objdump -p`; `link.rsp`; no call in three positive traces |
| `KERNEL32.dll` | `InitializeCriticalSectionEx (0x7d038)`; `CloseHandle (0x7d048)`; `DuplicateHandle (0x7d058)`; `GetCurrentProcess (0x7d070)`; `GetCurrentThread (0x7d080)`; `GetLastError (0x7d090)`; `SetLastError (0x7d0e8)`; `FlsAlloc (0x7d160)`; `FlsGetValue (0x7d168)`; `FlsSetValue (0x7d170)`; `GetCurrentProcessId (0x7d190)`; `GetCurrentThreadId (0x7d1a8)`; `VirtualQuery (0x7d238)`; `InitializeCriticalSection (0x7d2b8)`; `EnterCriticalSection (0x7d2c0)`; `LeaveCriticalSection (0x7d2c8)`; `DeleteCriticalSection (0x7d2d0)`; `FlsFree (0x7d2d8)` | Reverse-P/Invoke/thread-state helpers, NativeAOT PAL handle identity, stack-boundary discovery, and one-thread runtime synchronization. Observed call sites include `FlsGetValue` at `0x18003c68f`, `FlsSetValue` thunk at `0x18003c6b8`, `FlsAlloc` at `0x18003cd40`, `GetCurrentThreadId` at `0x18003c8b4`, `GetCurrentProcess/GetCurrentThread/DuplicateHandle` at `0x180032bc9/0x180032bd2/0x180032bfa` and `0x180033579/0x180033582/0x1800335aa`, `VirtualQuery` at `0x18003d102`, and critical-section calls through `0x180077270/0x1800772a0` | No call before export; the export transition reaches these after entry | Yes: all 18 are reached by the current probe's runtime initialization | A: guideXOS one-thread contract. FLS has 64 bounded slots; pseudo process/thread identity is explicit; handles are validated; `VirtualQuery` describes only the loader stack; critical sections honor ownership, recursion, and contention failure | `objdump -d -Mintel`; controlled fail-fast sequence: `FlsGetValue -> GetCurrentThreadId -> DuplicateHandle -> VirtualQuery -> EnterCriticalSection`; success after the 18-symbol layer |
| `KERNEL32.dll` | `EncodePointer (0x7d040)`; `CreateEventExW (0x7d050)`; `FormatMessageW (0x7d060)`; `GetConsoleOutputCP (0x7d068)`; `GetCurrentProcessorNumberEx (0x7d078)`; `GetModuleFileNameW (0x7d098)`; `GetStdHandle (0x7d0a0)`; `GetThreadPriority (0x7d0a8)`; `GetTickCount64 (0x7d0b0)`; `LocalFree (0x7d0b8)`; `MultiByteToWideChar (0x7d0c0)`; `QueryPerformanceCounter (0x7d0c8)`; `QueryPerformanceFrequency (0x7d0d0)`; `RaiseFailFastException (0x7d0d8)`; `SetEvent (0x7d0e0)`; `Sleep (0x7d0f0)`; `VirtualAlloc (0x7d0f8)`; `VirtualFree (0x7d100)`; `WaitForMultipleObjectsEx (0x7d108)`; `WideCharToMultiByte (0x7d110)`; `WriteFile (0x7d118)`; `RaiseException (0x7d120)`; `AddVectoredExceptionHandler (0x7d128)`; `GetModuleHandleW (0x7d130)`; `GetProcAddress (0x7d138)`; `RtlVirtualUnwind (0x7d140)`; `RtlCaptureContext (0x7d148)`; `RtlRestoreContext (0x7d150)`; `VerSetConditionMask (0x7d158)`; `ResetEvent (0x7d178)`; `WaitForSingleObjectEx (0x7d180)`; `CreateEventW (0x7d188)`; `SwitchToThread (0x7d198)`; `CreateThread (0x7d1a0)`; `SetThreadPriority (0x7d1b0)`; `SuspendThread (0x7d1b8)`; `ResumeThread (0x7d1c0)`; `FlushProcessWriteBuffers (0x7d1c8)`; `GetThreadContext (0x7d1d0)`; `SetThreadContext (0x7d1d8)`; `GetSystemTimeAsFileTime (0x7d1e0)`; `CreateMemoryResourceNotification (0x7d1e8)`; `QueryInformationJobObject (0x7d1f0)`; `GetModuleHandleExW (0x7d1f8)`; `LoadLibraryExW (0x7d200)`; `GetProcessAffinityMask (0x7d208)`; `VerifyVersionInfoW (0x7d210)`; `InitializeContext (0x7d218)`; `GetEnabledXStateFeatures (0x7d220)`; `LocateXStateFeature (0x7d228)`; `SetXStateFeaturesMask (0x7d230)`; `DebugBreak (0x7d240)`; `WaitForSingleObject (0x7d248)`; `SleepEx (0x7d250)`; `GlobalMemoryStatusEx (0x7d258)`; `GetSystemInfo (0x7d260)`; `GetLogicalProcessorInformation (0x7d268)`; `GetLogicalProcessorInformationEx (0x7d270)`; `GetLargePageMinimum (0x7d278)`; `VirtualUnlock (0x7d280)`; `VirtualAllocExNuma (0x7d288)`; `IsProcessInJob (0x7d290)`; `GetNumaHighestNodeNumber (0x7d298)`; `GetProcessGroupAffinity (0x7d2a0)`; `K32GetProcessMemoryInfo (0x7d2a8)`; `IsDebuggerPresent (0x7d2b0)`; `RtlPcToFileHeader (0x7d2e0)`; `InterlockedFlushSList (0x7d2e8)`; `RtlUnwindEx (0x7d2f0)`; `InitializeSListHead (0x7d2f8)` | Windows PAL, GC, thread, wait, diagnostics, unwind, NUMA, and process-support components retained by the broad runtime image; only PE import thunks observed, no call in the successful path | No | No | B: per-symbol unique fail-fast stub; intentionally unsupported, not a no-op compatibility layer | `objdump -p`, `objdump -d -Mintel`, `link.rsp`; three positive traces and the fail-fast control |
| `KERNEL32.dll` | `GetEnvironmentVariableW (0x7d088)` | NativeAOT GC-configuration helper, live call at preferred `0x18003e196`, return site `0x18003e19b` | No | One missing-variable lookup per run | A: exact bounded missing-variable route; host-only table core covers additional contract vectors | QEMU caller probe and three immutable runs under `evidence\generated\getenv-final-20260731-immutable`; [bootstrap contract](KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md) |
| `ole32.dll` | `CoGetApartmentType (0x7d408)`; `CoInitializeEx (0x7d410)`; `CoUninitialize (0x7d418)`; `CoWaitForMultipleHandles (0x7d420)` | COM apartment/wait support retained by PAL/runtime; no reachable call in the successful path | No | No | B: fail-fast stubs | `objdump -p`; no calls in positive traces |
| `api-ms-win-crt-math-l1-1-0.dll` | `log (0x7d340)` | Workstation GC/runtime configuration math; no reachable call in the successful path | No | No | B: fail-fast stub | `objdump -p`; runtime-pack/link response; no calls in positive traces |
| `api-ms-win-crt-string-l1-1-0.dll` | `strcmp (0x7e3c8)`; `strcpy_s (0x7e3d0)`; `strcpy (0x7e3d8)`; `_stricmp (0x7e3e0)`; `strlen (0x7e3e8)` | CRT/PAL configuration and diagnostics support; `strcmp`, `strlen`, and `_stricmp` are reached in order after `_initterm`; the next boundary after the `_stricmp` closure is `KERNEL32.dll!GetSystemInfo` | No | `strcmp`, `strlen`, `_stricmp` | A for `strcmp`, `strlen`, and `_stricmp`; B: fail-fast stubs for the other two | `objdump -p`; bounded call diagnostics; three immutable `_stricmp` runs |
| `api-ms-win-crt-convert-l1-1-0.dll` | `strtoull (0x7d308)` | Runtime configuration parsing; no reachable call in the successful path | No | No | B: fail-fast stub | `objdump -p`; no calls in positive traces |
| `api-ms-win-crt-stdio-l1-1-0.dll` | `__stdio_common_vsnprintf_s (0x7d3b8)` | CRT diagnostic formatting; no reachable call in the successful path | No | No | B: fail-fast stub | `objdump -p`; no calls in positive traces |
| `api-ms-win-crt-runtime-l1-1-0.dll` | `abort (0x7d350)`; `_register_onexit_function (0x7d358)`; `terminate (0x7d360)`; `_cexit (0x7d368)`; `_crt_atexit (0x7d370)`; `_execute_onexit_table (0x7d378)`; `_initterm_e (0x7d380)`; `_initialize_onexit_table (0x7d388)`; `_initterm (0x7d390)`; `_initialize_narrow_environment (0x7d398)`; `_seh_filter_dll (0x7d3a0)`; `_configure_narrow_argv (0x7d3a8)` | CRT/DLL bootstrap and termination objects (`dllmain.obj`, `bootstrapperdll.obj`); the init-only attach helper calls `_initialize_onexit_table` twice, and the register-enabled follow-on calls `_register_onexit_function` once | `_initialize_onexit_table` twice; `_register_onexit_function` once in the register-enabled profile | No | A for `_initialize_onexit_table` and the bounded register route; B for the other 10 symbols | `link.rsp`; PE entry RVA `0x77840`; three immutable register traces, no execution or shutdown |
| `api-ms-win-crt-heap-l1-1-0.dll` | `free (0x7d318)`; `_callnewh (0x7d320)`; `calloc (0x7d328)`; `malloc (0x7d330)` | CRT allocation and NativeAOT GC/runtime support; no allocation is attempted by this probe | No | No | B: fail-fast stubs; allocation is a later blocker | `objdump -p`; `Runtime.WorkstationGC.lib`; no calls in positive traces |

The table contains all ten descriptors and all 124 symbols. It is intentionally grouped into functional and fail-fast KERNEL32 rows so the 18/106 treatment split is visible without hiding any symbol.

The inventory above is the baseline symbol census. In the current `GetSystemInfo` opt-in, only the exact `KERNEL32.dll!GetSystemInfo` entry at IAT RVA `0x7d260` changes from the baseline fail-fast treatment; `GetNumaHighestNodeNumber` and all other NUMA/virtual-memory symbols remain fail-fast.

For the current time-enabled census, the single exception to that historical grouping is:

| Module | Symbol | IAT | Current treatment | Exact caller | Current reachability |
| --- | --- | ---: | --- | --- | --- |
| `KERNEL32.dll` | `GetSystemTimeAsFileTime` | `0x7e1e0` | A: isolated UEFI-backed `FILETIME` implementation | security-cookie initializer `0x180078290`, direct call `0x1800782ca` | returns once; QPC is reached next |
| `KERNEL32.dll` | `QueryPerformanceCounter` | `0x7e0c8` | A: allocation-free UEFI-backed monotonic counter wrapper | security-cookie initializer `0x1800782f9` | returns one normalized reading; next boundary is CRT on-exit initialization |
| `KERNEL32.dll` | `QueryPerformanceFrequency` | `0x7e0d0` | A: paired positive-frequency query for the selected source | QPF diagnostic path at the NativeAOT startup boundary | returns `0x369e99` on the ACPI PM profile; not reached by the default startup trace |

The resulting current allocation-startup treatment is 21 functional / 103 fail-fast before the CRT opt-in. The CRT-enabled treatment is 22 functional / 102 fail-fast, the SLIST-enabled treatment is 23 functional / 101 fail-fast, `_initterm_e` is 24 / 100, `_initterm` is 25 / 99, `strcmp` is 26 / 98, and `strlen` is 27 / 97. The historical 18/106 and 19/105 tables remain in earlier evidence to preserve the prior Gate 4 sequence.

## `GetProcessGroupAffinity` closure (2026-08-01)

The exact process-group import at IAT RVA `0x7d2a0` changes from fail-fast to one narrow functional route in the enabled image: `32` functional / `92` fail-fast / `0` unresolved. The disabled control remains `31` / `93` / `0` and stops at the original import. The exact checked route is limited to the current-process pseudo-handle, the one-processor `FACT_SNAPSHOT`/`SINGLE_GROUP_ZERO` facts, and the observed capacity probe. `GetProcessAffinityMask`, `GetLogicalProcessorInformation`, `GetLogicalProcessorInformationEx`, `VirtualAllocExNuma`, and all other processor-group/affinity companions remain fail-fast.

The final enabled evidence is `evidence\generated\getprocessgroup-final3-20260801-immutable-v4`; all three runs reach `KERNEL32.dll!GetProcessAffinityMask`. See [KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md](KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md).

## `GetProcessAffinityMask` closure (2026-08-01)

The exact `KERNEL32.dll!GetProcessAffinityMask` import is IAT RVA `0x7d208`, preferred IAT `0x18007d208`, and descriptor index `2`. Enabling the narrow route changes the observed census from `32 / 92 / 0` to `33 / 91 / 0` functional/fail-fast/unresolved imports. The route reuses the one-processor `GetSystemInfo` facts and one-group Group-0 facts, returning process/system masks `0x1`/`0x1` only for the current-process pseudo-handle.

Two live calls are proven: preferred `0x180043793` reads only the process mask to update a processor bitmap, and preferred `0x18003cc55` reads only the process mask, manually counts its bits, and then calls `QueryInformationJobObject`. The system-mask output is written and validated but is not read by either caller. The next authentic dependency is `KERNEL32.dll!QueryInformationJobObject`; no other affinity API was aliased.

## CRT on-exit bootstrap census

The current NativeAOT attach helper at preferred address `0x180077c70` calls `_initialize_onexit_table` twice, with table addresses `0x1800b5e98` and `0x1800b5eb0`. Both calls returned zero in the complete CRT-enabled traces, and both tables ended with equal `first`, `last`, and `end` fields. The next observed import was `KERNEL32.dll!InitializeSListHead`.

In the historical init-only profile, `_register_onexit_function`, `_execute_onexit_table`, `_crt_atexit`, and `_cexit` were imported or statically present but were not dynamically reached. The later register-enabled profile reaches only `_register_onexit_function` and stops before its allocator. `atexit` and `_c_exit` are not present in this PE's import census. Static disassembly shows the nearby `_crt_atexit` helper can reference registration and `_cexit`; that is a reachability fact, not evidence that startup shutdown or callback execution occurred. The complete register contract and negative controls are in [CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md](CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md); the initialization-only history remains in [CRT_ONEXIT_BOOTSTRAP.md](CRT_ONEXIT_BOOTSTRAP.md).

## SLIST family census and current reachability

The current allocation payload has two relevant KERNEL32 imports in its actual PE import table: `InterlockedFlushSList` at IAT RVA `0x7e2e8` and `InitializeSListHead` at IAT RVA `0x7e2f8`. `InitializeSListHead` is the only SLIST-family operation reached by the current startup path. It is routed functionally in the SLIST-enabled harness, while the other imported `InterlockedFlushSList` remains a unique fail-fast target. The reachability trace is:

```text
0x180077550  NativeAOT attach/bootstrap helper
  -> 0x180078350  lea rcx, [0x1800b5ed0]; tail-jump through InitializeSListHead IAT
  -> 0x180078380  post-initialization static state helper
  -> api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e  next boundary
```

The header's relocated address in the fresh QEMU profile was `0x552eed0`, with low alignment bits `0`. It is a static writable image location at preferred RVA `0xb5ed0`, not TLS, stack, heap, or loader scratch. The current trace initializes one header once; no push, pop, flush, depth query, or compiler-generated atomic operation on that header is reached.

| Operation | Current classification | Evidence / scope |
| --- | --- | --- |
| `InitializeSListHead` | Imported and currently reached; functional in the SLIST-enabled harness | Exact caller/helper above; one aligned header; next boundary is `_initterm_e` |
| `InterlockedFlushSList` | Imported but not reached; later-runtime or shutdown-only is possible but unproven | IAT RVA `0x7e2e8`; static helper at preferred `0x180079430`; no positive marker |
| `InterlockedPushEntrySList` | Absent from current PE import census | No import, symbol, or startup call found |
| `InterlockedPopEntrySList` | Absent from current PE import census | No import, symbol, or startup call found |
| `QueryDepthSList` | Absent from current PE import census | No import, symbol, or startup call found |
| `RtlInitializeSListHead` | Absent from current PE import census | SDK declaration only; no current PE import or call |
| `RtlInterlockedPushEntrySList` | Absent from current PE import census | SDK declaration only; no current PE import or call |
| `RtlInterlockedPopEntrySList` | Absent from current PE import census | SDK declaration only; no current PE import or call |
| `RtlInterlockedFlushSList` | Absent from current PE import census | SDK declaration only; no current PE import or call |
| `RtlQueryDepthSList` | Absent from current PE import census | SDK declaration only; no current PE import or call |
| Direct atomic header manipulation | Not identified on the current startup path | Focused disassembly found no compiler intrinsic or direct atomic operation on `0x1800b5ed0`; later code remains unclaimed |

This census does not implement or imply general lock-free SLIST support. The exact x64 layout, initialization-only contract, and validation results are in [PLATFORM_SLIST_CONTRACT.md](PLATFORM_SLIST_CONTRACT.md).

## Proven time reachability transition

Before this pass, `GetSystemTimeAsFileTime` was the first allocation-enabled startup boundary. The exact caller is the compiler/CRT security-cookie initializer at `0x180078290`, with a direct IAT call at `0x1800782ca` through allocation-PE IAT slot RVA `0x7e1e0` (`0x18007e1e0`). The normal import thunk also exists at RVA `0x3ca70` (`0x18003ca70`), but this call site uses the direct IAT slot. The consumer writes a security-cookie input through local `[rsp+0x40]`, then continues to `GetCurrentThreadId`, `GetCurrentProcessId`, and the next fail-fast import, `QueryPerformanceCounter` at IAT RVA `0x7e0c8`.

The guideXOS implementation is `src/Gate4Harness/platform_time.c`, backed by UEFI `RuntimeServices->GetTime`, with explicit checked Gregorian conversion and little-endian `FILETIME` output. In the QEMU profile, `-rtc base=utc,clock=vm` plus `GXOS_ASSUME_UNSPECIFIED_TIMEZONE_UTC` handles firmware `TimeZone=2047` explicitly; strict builds reject unspecified timezone data. The performance implementation is `src/Gate4Harness/platform_performance.c`; it selects the ACPI PM timer on the default QEMU CPU and exposes paired QPC/QPF wrappers. Three fresh positive runs reached `TIME_API_RETURN`, one QPC result, phase `0x18`, and zero TLS allocation limit/pointer. The reachability transition is therefore:

```text
GetSystemTimeAsFileTime: fail-fast boundary
  -> verified guideXOS UEFI time contract
  -> verified guideXOS QueryPerformanceCounter / QueryPerformanceFrequency contract
  -> api-ms-win-crt-runtime-l1-1-0.dll!_initialize_onexit_table: next authentic boundary
```

The returned value is consumed by security-cookie initialization, not GC heuristics, and is read once during process attach. No GC singleton, managed-thread registration, or allocation context is proven by this transition.

## Reachability experiments

The resolver first installed a unique fail-fast stub in every IAT slot and then ran the actual exported method. Rebuilding the harness with one functional target at a time produced this sequence:

| Experiment | Deepest marker / first observed target | Conclusion |
| --- | --- | --- |
| TLS only | `KERNEL32.dll!FlsGetValue`, IAT `0x7d168`, call `0x18003c68f` | NativeAOT reverse-P/Invoke thread setup reads FLS before managed validation. |
| TLS + FLS | `KERNEL32.dll!GetCurrentThreadId`, IAT `0x7d1a8`, call `0x18003c8b4` | Runtime thread identity is needed for the first transition. |
| TLS + FLS + identity | `KERNEL32.dll!DuplicateHandle`, IAT `0x7d058`, calls `0x180032bfa`/`0x1800335aa` | NativeAOT creates/records a current-thread handle. |
| Add bounded handles | `KERNEL32.dll!VirtualQuery`, IAT `0x7d238`, call `0x18003d102` | Stack limits are queried through the PAL and must describe the active loader stack. |
| Add bounded stack query/TEB | `KERNEL32.dll!EnterCriticalSection`, thunk `0x180077270` | One-thread runtime initialization enters a lock. |
| Add the 18-symbol layer | `MANAGED_ENTRY_OK`, then return `0` | The no-allocation managed probe is reachable and deterministic. |
| Add the exact time contract | `TIME_API_RETURN`, then QPC returns a normalized reading | FILETIME and monotonic performance contracts are proven without making unrelated imports functional. |
| Add the performance counter contract | `QPC_OK`, then `api-ms-win-crt-runtime-l1-1-0.dll!_initialize_onexit_table` | The next dependency is the CRT on-exit table, not GC allocation. |
| Add the CRT empty-table contract | Two `CRT_ONEXIT_INITIALIZED_OK` markers, then `KERNEL32.dll!InitializeSListHead` | `_initialize_onexit_table` is closed for the two startup tables; registration, execution, and GC remain unproven. |
| Add the x64 SLIST-head initialization contract | One `SLIST_HEAD_INITIALIZED_OK` marker, then `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e` | Only the 16-byte empty header is closed; SLIST companions, registration, execution, and GC remain unproven. |
| Add the narrow `_initterm_e` contract | One validated eight-byte range containing one null entry; `CRT_INITTERM_E_OK`, then `api-ms-win-crt-runtime-l1-1-0.dll!_initterm` | The error-returning table iterator is closed for this artifact; no non-null callback was present, and `_initterm` remains the next boundary. |

The remaining 103 current imports were not declared unused merely because they were absent from one trace: the link response and disassembly identify their retaining components, and the negative fail-fast stubs prove that any accidental reachability is detected. The historical 106-symbol fail-fast set is retained in prior evidence. These are deferred runtime services, not silently supported services.

## NativeAOT components and deferred boundaries

## Allocation differential and startup trace

The allocation-enabled shared artifact has SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` and remains 10 descriptors / 124 symbols. `tools\Compare-AllocationArtifacts.ps1` compares the two manifests and map XML files; the retained report is `artifacts\allocation-enabled-final-20260728-060439-726\allocation-differential.json` and passes because the import sets are identical while the allocation probe's EEType, constructor, and `AllocateOne` appear only in the staged map.

The pre-change clean opt-in startup trace called the allocation PE's actual entry RVA `0x77840` with process-attach arguments after the existing loader TLS setup and stopped at `KERNEL32.dll!GetSystemTimeAsFileTime`. The current SLIST-enabled trace passes FILETIME, QPC, and QPF, initializes both on-exit tables, initializes one x64 SLIST header, and reaches `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. Temporary exploratory shims were not retained; no dummy CRT, virtual-memory, event, or thread implementation is counted as support.

The separate pre-startup allocation run reaches the generated `RhpNewFast` path with TLS allocation limit and pointer both zero, and the managed probe returns `-10`. This is the exact current first-allocation blocker, not evidence of a successful allocation.

`link.rsp` retains `dllmain.obj`, `bootstrapperdll.obj`, `Runtime.WorkstationGC.lib`, `aotminipal.lib`, disabled EventPipe/standalone-GC components, compression/native support, and Windows import libraries. The map contains `ModuleInitializerList`, `RuntimeConfigurationBlob`, TLS, GC statics, exception metadata, and thread-static metadata. The current proof provides only the minimum one-thread transition state; it does not provide a GC heap, virtual-memory allocator, process/threading system, COM, CRT, exceptions, or unwinding.

The prior static-link attempt remains evidence that removing the import directory by linking the standard static runtime is not a clean solution: it produced 158 unresolved externals spanning memory, threads/FLS, COM, unwind/context, TLS, CRT, stack probing, and allocation operators.

## SLIST evidence-closure result (2026-07-29)

The current census remains 23 functional / 101 fail-fast imports with `UNRESOLVED_REQUIRED_IMPORTS=0`. The final immutable positive artifact set is `artifacts\slist-final-validation-20260729-corrected3`, with loader `2EEBCD284F6D2E5AD1526EB15FA4AF6483E7B1FE9D17A448720A289FF64B0362` and payload `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. Three consecutive fresh QEMU runs completed under `evidence\generated\slist-final-20260730-immutable`; each executed the functional `InitializeSListHead` contract and advanced to `_initterm_e` with complete summaries.

This result does not add `_initterm_e`, allocation, GC, thread registration, or SLIST companion support. The next dependency remains `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`.

## `_initterm_e` evidence-closure result (2026-07-30)

The final allocation/startup payload remains `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. Its `_initterm_e` import is at IAT RVA `0x7e380`; the exact static call is at preferred `0x1800775bb` in the NativeAOT attach/bootstrap helper beginning at `0x180077550`. The MS x64 caller places the first table pointer in RCX and the exclusive end pointer in RDX. In the relocated QEMU image (`IMAGE_BASE=0x0000000005479000`) the wrapper observed caller return `0x00000000054F05C0`, first `0x00000000054F74D0`, and last `0x00000000054F74D8`.

The range is one eight-byte pointer in the image `.rdata` section (RVA `0x7e4d0` through `0x7e4d8`), not writable `.CRT` data. The stored value is zero; there are zero non-null entries, zero callback invocations, and zero callback failures. The table contains no relocation entry because the only stored value is null. The iterator follows the Microsoft `[first,last)` order and returns zero after skipping the null slot. The next call site is `_initterm` at preferred `0x1800775db`; `_initterm` remains intentionally fail-fast and unimplemented.

The current import treatment is 24 functional / 100 fail-fast with zero unresolved required imports. Three fresh immutable-hash QEMU runs are retained under `evidence\generated\crt-initterm-e-final-20260730-immutable-v4`; each has a complete 4,320-byte serial log, exit `0`, unique PID, fresh OVMF vars, `CRT_INITTERM_E_RESULT=0x00000000`, `CRT_INITTERM_E_OK`, and the `_initterm` boundary. The focused host suite and evidence-pipeline controls also passed. This addendum does not claim general CRT initialization, callback execution for other artifacts, allocation, GC startup, managed-thread registration, or C++ initializer support.

## `_initterm` evidence-closure result (2026-07-30)

The exact `api-ms-win-crt-runtime-l1-1-0.dll!_initterm` import is at IAT RVA `0x7e390`; the static call is preferred `0x1800775db` in the attach/bootstrap helper beginning at `0x180077550`. The relocated QEMU wrapper observed return address `0x00000000054F05E0`, first `0x00000000054F7468`, and last `0x00000000054F74B0`. The exclusive range is `0x48` bytes, nine eight-byte entries in `.rdata` (RVA `0x7e468` through `0x7e4b0`), readable and non-executable/non-writable. Eight non-null entries are relocated direct pointers into `.text`; one entry at index zero is null, and no targets are duplicated.

The eight actual callback targets were invoked in table order at relocated addresses `0x00000000054AAD50`, `0x00000000054AADA0`, `0x00000000054AAD90`, `0x00000000054AADC0`, `0x00000000054AADB0`, `0x00000000054AADD0`, `0x00000000054AADE0`, and `0x00000000054AADF0`. Every callback emitted a begin marker and a matching return marker; no callback fault, unresolved import, CPU exception, triple fault, hang, allocation, GC transition, managed-thread registration, or allocation-context change occurred. The callbacks performed bounded internal static-state writes and introduced no direct imported API call. After `_initterm` completed, the next authentic dependency was `api-ms-win-crt-string-l1-1-0.dll!strcmp`.

The final immutable positive evidence is `evidence\generated\crt-initterm-final-20260730-immutable-v2`: three fresh processes completed with unique PIDs, exit `0`, and identical artifact hashes. The final import treatment is `25` functional / `99` fail-fast with zero unresolved required imports; final QPC regressions remained zero and the final allocation-context, managed-thread, and explicit GC usability negatives remained zero. The negative-control bundle `evidence\generated\crt-initterm-negative-controls-20260730-v2` passed disabled-routing, marker, evidence-integrity, range, target-validation, duplicate, ordering, and callback-return controls. This closure is limited to the observed table and does not claim general CRT, `.CRT` family, or C++ initialization support.

## `strcmp` evidence-closure result (2026-07-30)

The narrow Microsoft x64 `strcmp` contract is closed in [CRT_STRCMP_BOOTSTRAP.md](CRT_STRCMP_BOOTSTRAP.md). The exact import is IAT RVA `0x7d3c8`; the live call is preferred `0x18003eb1f` in the NativeAOT GC-configuration helper, with return site `0x18003eb24`. The runtime call compares immutable `.rdata` strings `gcServer` and `gcConservative`, once per run, and returns `+1` under unsigned-byte ordinal comparison.

The enabled import treatment is `26` functional / `98` fail-fast / `0` unresolved. Three immutable QEMU runs under `evidence\generated\crt-strcmp-final-20260730-immutable` completed with identical loader/payload/runtime hashes, PIDs `23404`, `2376`, and `21892`, serial length `12137`, exit `0`, QPC count `2`, zero QPC regressions, zero TLS allocation context, zero managed-thread registration, and zero GC heap usability. Each advanced exactly to `api-ms-win-crt-string-l1-1-0.dll!strlen`. The disabled control retained `25` / `99` and stopped at `strcmp`; `strlen` was intentionally unimplemented in that historical profile.

## `strlen` evidence-closure result (2026-07-31)

The narrow Microsoft x64 `strlen` contract is closed in [CRT_STRLEN_BOOTSTRAP.md](CRT_STRLEN_BOOTSTRAP.md). The exact import is IAT RVA `0x7d3e8`; the live call is preferred `0x18003dba0` in the NativeAOT GC-configuration startup path, with runtime return site `0x00000000054B8BA5`. The call scans the immutable `.rdata` string `gcServer`, returns `8`, and reports the terminating byte at runtime address `0x00000000055134A0`.

The enabled import treatment is `27` functional / `97` fail-fast / `0` unresolved. Three immutable QEMU runs under `evidence\generated\crt-strlen-final-20260731-immutable-v3` completed with identical loader/payload/runtime/firmware/QEMU hashes, unique PIDs, exit `0`, serial length `13704`, QPC count `2`, zero QPC regressions, zero TLS allocation context, zero managed-thread registration, and zero GC heap usability. Each advanced exactly to `KERNEL32.dll!GetEnvironmentVariableW`. The disabled control retained `26` / `98` and stopped at the original `strlen` boundary; it emitted no `CRT_STRLEN_*` implementation markers.

## `GetEnvironmentVariableW` evidence-closure result (2026-07-31)

The narrow Microsoft x64 `GetEnvironmentVariableW` contract is closed in [KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md](KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md). The live NativeAOT caller is the GC-configuration helper at preferred `0x18003e150`; the direct call is `0x18003e196` and the return site is `0x18003e19b`. All three runs made one call with `lpName` pointing to the null-terminated UTF-16 name `DOTNET_gcServer`, `lpBuffer` non-null, and `nSize=17`. The result was `0`, `GetLastError()` was `ERROR_ENVVAR_NOT_FOUND` (`203`), and the caller immediately selected its absent/fallback path. No second call or value parse occurred.

The enabled treatment is `28` functional / `96` fail-fast / `0` unresolved. Three fresh QEMU runs under `evidence\generated\getenv-final-20260731-immutable` used PIDs `8648`, `13476`, and `7100`, each with serial length `15500`, QPC count `2`, zero QPC regressions, zero allocation context, zero managed-thread registration, and zero GC heap usability. The next authentic dependency is `api-ms-win-crt-string-l1-1-0.dll!_stricmp`, which remains fail-fast. The disabled control under `evidence\generated\getenv-disabled-20260731` retained `27` / `97` and stopped at `GetEnvironmentVariableW`. The focused host suite and negative evidence pipeline passed. This closure is limited to the observed missing-variable startup request and does not claim a complete environment subsystem or GC initialization.

## `_stricmp` evidence closure (2026-07-31)

The exact `_stricmp` import is now routed under the separate `GXOS_ENABLE_CRT_STRICMP` opt-in. Static inspection records IAT RVA `0x7e3e0`, preferred import thunk `0x1800774cb`, and executed call sites `0x18003df6b` and `0x18003e0ab`; both callers test EAX for zero/sign and do not consume an exact magnitude. The checked route is documented in [CRT_STRICMP_BOOTSTRAP.md](CRT_STRICMP_BOOTSTRAP.md).

Three fresh positive runs under `evidence\generated\crt-stricmp-final-20260731-immutable-v4` prove `29 / 95 / 0` imports, 885 successful checked calls, zero failures, and the next authentic `KERNEL32.dll!GetSystemInfo` boundary. The disabled three-run control under `evidence\generated\crt-stricmp-disabled-20260731-v3` retains `_stricmp` fail-fast. No `GetSystemInfo` route was added.

## `GetSystemInfo` evidence-closure result (2026-07-31)

The exact `KERNEL32.dll!GetSystemInfo` import is at IAT RVA `0x7e260`; the static call is preferred `0x18004379f`, and the caller's destination is `lea rcx,[rsp+0x20]`. The checked implementation is in [KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md](KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md). It publishes only facts already established by the loader: AMD64, 4 KiB pages, one bootstrap processor, a one-bit active mask, `PROCESSOR_AMD_X8664`, and the relocated loaded-image address range. The stack is approved as writable destination memory but is not advertised as application range, and the `4096` allocation granularity is explicitly the loader page unit rather than a general Windows VM claim.

The positive treatment is `30 / 94 / 0` functional/fail-fast/unresolved imports. Three immutable runs under `evidence\generated\getsysteminfo-final-20260731-immutable-v3` complete the exact `SYSTEM_INFO` structure, the static `0xA2` field-read mask, and the next authentic `KERNEL32.dll!GetNumaHighestNodeNumber` boundary. The disabled control under `evidence\generated\getsysteminfo-disabled-20260731-immutable-v2` retains `29 / 95 / 0` and the GetSystemInfo fail-fast boundary. The next census target is `GetNumaHighestNodeNumber`; GC heap initialization, process-wide virtual memory, additional CPUs, and allocation remain unproven.

## `GetNumaHighestNodeNumber` closure (2026-08-01)

The exact `KERNEL32.dll!GetNumaHighestNodeNumber` import is at IAT RVA `0x7e298` (preferred address `0x18007e298`), with static call `0x1800437dd`. Enabling only this exact route changes the observed census from `30` functional / `94` fail-fast / `0` unresolved to `31` / `93` / `0`. The disabled control remains `30` / `94` / `0` and stops at the original NUMA boundary.

The checked wrapper is a narrow `BOOL (PULONG)` Microsoft x64 contract. The actual caller passes a writable four-byte stack output at `rsp+0x60`, tests the Boolean return, and reads the output only on the success path. For the current one-domain policy, a successful output of zero selects the caller's non-NUMA fallback; a successful nonzero value would be transformed by the caller into `highest + 1` for its node-table setup. No subsequent NUMA API is reached; both caller branches converge at the next authentic `GetProcessGroupAffinity` fail-fast boundary.

## `GetModuleHandleW` closure (2026-08-01)

The fresh baseline reproduced `KERNEL32.dll!GetModuleHandleW` as the immediate boundary after the exact job-object closure. The positive route changes the census from `34 / 90 / 0` to `35 / 89 / 0`; the disabled control preserves `34 / 90 / 0`. The importing IAT is RVA `0x7d130`, descriptor `0x2`, and the live preferred call is `0x180037c61` in `NativeAOT_RtlDllShutdownInProgress_probe`.

The live call supplies `&L"ntdll.dll"`. The bounded wrapper records the UTF-16 argument, read-only payload `.rdata` ownership, actual relocated image base, preferred base, relocation delta, and `NULL`/`ERROR_MOD_NOT_FOUND`. No ntdll image is mapped, so the payload is not returned under the ntdll name. The next authentic dependency is `KERNEL32.dll!GetProcAddress`; it remains fail-fast. See [KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md](KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md).

The complete import and call evidence is retained in `evidence\generated\getnumahighest-final-20260801-immutable-v2`. The contract does not make `GetLogicalProcessorInformation`, `GetLogicalProcessorInformationEx`, `VirtualAllocExNuma`, or any other topology/allocation import functional. They remain deterministic fail-fast dependencies.

## `GetProcAddress` dependency closure (2026-08-01)

The current NativeAOT startup path imports `KERNEL32.dll!GetProcAddress` at IAT RVA `0x7d138` (`0x18007d138` preferred). The only live call is preferred `0x180037c71` in `NativeAOT_RtlDllShutdownInProgress_probe`, beginning at `0x180037c40`; the caller passes the preceding null `GetModuleHandleW` result and the exact read-only `.rdata` name `RtlDllShutdownInProgress`. The checked route returns `NULL`/`127`, records no export-lookup attempt, and the caller advances to `api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function`.

The final positive treatment is `36` functional / `88` fail-fast / `0` unresolved; the disabled GetProcAddress control is `35` / `89` / `0` and stops at the same import as an authentic fail-fast boundary. The route is name-safe and ordinal-aware at the ABI boundary, but it intentionally does not parse PE export directories or load/resolve modules. See [KERNEL32_GETPROCADDRESS_BOOTSTRAP.md](KERNEL32_GETPROCADDRESS_BOOTSTRAP.md).

## `_register_onexit_function` dependency closure (2026-08-02)

The register-enabled treatment adds exactly the requested `api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function` route at IAT RVA `0x7d358`, changing the enabled census to `37` functional / `87` fail-fast / `0` unresolved. The disabled control remains `36 / 88 / 0` and stops at the register import. The historical CRT-on-exit table above records the earlier initialization-only boundary; this addendum records the later, separately scoped register call.

The live call is preferred `0x180077e13`, in the bounded NativeAOT helper `0x180077df0..0x180077e30`. It passes the first initialized table at RVA `0xb3e78` and a callback at RVA `0x37bd0`. The checked route decodes the three raw fields as zero, proves the table is initialized, readable, and writable, and proves the callback belongs to executable managed image text. The empty table requires the UCRT initial growth request `_recalloc_crt_t(_PVFV,NULL,0x20)`; that allocator is not routed. The return is `-1` / `GROWTH_REQUIRED`, the raw fields remain unchanged, no callback executes, and all GC/allocation markers remain zero. See [CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md](CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md).

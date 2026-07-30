# Dependency census

This document records the exact imports of the Gate 1 shared NativeAOT artifact, the Gate 4 reachability experiment, and the allocation differential. The current no-allocation control is the reproducible `win-x64` shared image whose SHA-256 is `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`.

The initial Gate 4 stop was a correct ten-descriptor/124-symbol census. The current control experiment patches every IAT slot: 21 imports now have bounded functional implementations, including the exact `GetSystemTimeAsFileTime`, `QueryPerformanceCounter`, and `QueryPerformanceFrequency` contracts, and the other 103 receive deterministic guideXOS-owned fail-fast stubs. A fail-fast stub is not support for the service; it proves that the symbol is not reached by the legitimate path. The allocation-enabled variant keeps the same 10/124 import set and adds only managed allocation code and metadata; it does not make the remaining services functional.

Final control treatment totals for the performance-enabled build are A=functional `21`, B=deterministic fail-fast `103`, C=import elimination `0`, and D=deferred required symbols `0`. The CRT opt-in follow-on adds exactly one functional import, `_initialize_onexit_table`, for A=`22` / B=`102`; the current SLIST opt-in adds exactly one more, `InitializeSListHead`, for A=`23` / B=`101`. All other deferred symbols retain their fail-fast treatment. The no-allocation control still passes managed entry. The current allocation/startup trace initializes both empty on-exit tables, initializes one x64 SLIST header, and stops at the next authentic import, `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`; the first allocation probe remains `-10` without a GC allocation context.

## Exact descriptor and symbol inventory

IAT RVAs below are relative to the preferred image base `0x180000000`. Each `symbol (RVA)` pair is one imported symbol; no ordinal-only import exists in this artifact. “Before” means before the first instruction of the exported `ManagedMain` thunk. “Probe” means the legitimate path from that thunk through validation, the borrowed callback, and return.

| Module | Symbol or ordinal and IAT RVA | Referenced by / earliest possible call site | Required before ManagedMain | Required during probe | Proposed treatment | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| `ADVAPI32.dll` | `RegisterEventSourceW (0x7d000)`; `ReportEventW (0x7d008)`; `OpenProcessToken (0x7d010)`; `AdjustTokenPrivileges (0x7d018)`; `LookupPrivilegeValueW (0x7d020)`; `DeregisterEventSource (0x7d028)` | NativeAOT diagnostics and Windows privilege/large-page support retained by `aotminipal`/runtime composition; no reachable call in the successful export path; PE thunks are in the `.text` import-thunk region near `0x1800772xx` | No call; IAT must be patched | No | B: unique `UNEXPECTED_IMPORT_CALL:ADVAPI32.dll!<symbol>` stub, halt | `objdump -p`; `link.rsp`; fail-fast negative control invoked `RegisterEventSourceW` and halted before managed entry |
| `bcrypt.dll` | `BCryptGenRandom (0x7d3f8)` | NativeAOT PAL random-byte support; no reachable call in the successful path | No call; IAT must be patched | No | B: fail-fast stub | `objdump -p`; `link.rsp`; no call in three positive traces |
| `KERNEL32.dll` | `InitializeCriticalSectionEx (0x7d038)`; `CloseHandle (0x7d048)`; `DuplicateHandle (0x7d058)`; `GetCurrentProcess (0x7d070)`; `GetCurrentThread (0x7d080)`; `GetLastError (0x7d090)`; `SetLastError (0x7d0e8)`; `FlsAlloc (0x7d160)`; `FlsGetValue (0x7d168)`; `FlsSetValue (0x7d170)`; `GetCurrentProcessId (0x7d190)`; `GetCurrentThreadId (0x7d1a8)`; `VirtualQuery (0x7d238)`; `InitializeCriticalSection (0x7d2b8)`; `EnterCriticalSection (0x7d2c0)`; `LeaveCriticalSection (0x7d2c8)`; `DeleteCriticalSection (0x7d2d0)`; `FlsFree (0x7d2d8)` | Reverse-P/Invoke/thread-state helpers, NativeAOT PAL handle identity, stack-boundary discovery, and one-thread runtime synchronization. Observed call sites include `FlsGetValue` at `0x18003c68f`, `FlsSetValue` thunk at `0x18003c6b8`, `FlsAlloc` at `0x18003cd40`, `GetCurrentThreadId` at `0x18003c8b4`, `GetCurrentProcess/GetCurrentThread/DuplicateHandle` at `0x180032bc9/0x180032bd2/0x180032bfa` and `0x180033579/0x180033582/0x1800335aa`, `VirtualQuery` at `0x18003d102`, and critical-section calls through `0x180077270/0x1800772a0` | No call before export; the export transition reaches these after entry | Yes: all 18 are reached by the current probe's runtime initialization | A: guideXOS one-thread contract. FLS has 64 bounded slots; pseudo process/thread identity is explicit; handles are validated; `VirtualQuery` describes only the loader stack; critical sections honor ownership, recursion, and contention failure | `objdump -d -Mintel`; controlled fail-fast sequence: `FlsGetValue -> GetCurrentThreadId -> DuplicateHandle -> VirtualQuery -> EnterCriticalSection`; success after the 18-symbol layer |
| `KERNEL32.dll` | `EncodePointer (0x7d040)`; `CreateEventExW (0x7d050)`; `FormatMessageW (0x7d060)`; `GetConsoleOutputCP (0x7d068)`; `GetCurrentProcessorNumberEx (0x7d078)`; `GetEnvironmentVariableW (0x7d088)`; `GetModuleFileNameW (0x7d098)`; `GetStdHandle (0x7d0a0)`; `GetThreadPriority (0x7d0a8)`; `GetTickCount64 (0x7d0b0)`; `LocalFree (0x7d0b8)`; `MultiByteToWideChar (0x7d0c0)`; `QueryPerformanceCounter (0x7d0c8)`; `QueryPerformanceFrequency (0x7d0d0)`; `RaiseFailFastException (0x7d0d8)`; `SetEvent (0x7d0e0)`; `Sleep (0x7d0f0)`; `VirtualAlloc (0x7d0f8)`; `VirtualFree (0x7d100)`; `WaitForMultipleObjectsEx (0x7d108)`; `WideCharToMultiByte (0x7d110)`; `WriteFile (0x7d118)`; `RaiseException (0x7d120)`; `AddVectoredExceptionHandler (0x7d128)`; `GetModuleHandleW (0x7d130)`; `GetProcAddress (0x7d138)`; `RtlVirtualUnwind (0x7d140)`; `RtlCaptureContext (0x7d148)`; `RtlRestoreContext (0x7d150)`; `VerSetConditionMask (0x7d158)`; `ResetEvent (0x7d178)`; `WaitForSingleObjectEx (0x7d180)`; `CreateEventW (0x7d188)`; `SwitchToThread (0x7d198)`; `CreateThread (0x7d1a0)`; `SetThreadPriority (0x7d1b0)`; `SuspendThread (0x7d1b8)`; `ResumeThread (0x7d1c0)`; `FlushProcessWriteBuffers (0x7d1c8)`; `GetThreadContext (0x7d1d0)`; `SetThreadContext (0x7d1d8)`; `GetSystemTimeAsFileTime (0x7d1e0)`; `CreateMemoryResourceNotification (0x7d1e8)`; `QueryInformationJobObject (0x7d1f0)`; `GetModuleHandleExW (0x7d1f8)`; `LoadLibraryExW (0x7d200)`; `GetProcessAffinityMask (0x7d208)`; `VerifyVersionInfoW (0x7d210)`; `InitializeContext (0x7d218)`; `GetEnabledXStateFeatures (0x7d220)`; `LocateXStateFeature (0x7d228)`; `SetXStateFeaturesMask (0x7d230)`; `DebugBreak (0x7d240)`; `WaitForSingleObject (0x7d248)`; `SleepEx (0x7d250)`; `GlobalMemoryStatusEx (0x7d258)`; `GetSystemInfo (0x7d260)`; `GetLogicalProcessorInformation (0x7d268)`; `GetLogicalProcessorInformationEx (0x7d270)`; `GetLargePageMinimum (0x7d278)`; `VirtualUnlock (0x7d280)`; `VirtualAllocExNuma (0x7d288)`; `IsProcessInJob (0x7d290)`; `GetNumaHighestNodeNumber (0x7d298)`; `GetProcessGroupAffinity (0x7d2a0)`; `K32GetProcessMemoryInfo (0x7d2a8)`; `IsDebuggerPresent (0x7d2b0)`; `RtlPcToFileHeader (0x7d2e0)`; `InterlockedFlushSList (0x7d2e8)`; `RtlUnwindEx (0x7d2f0)`; `InitializeSListHead (0x7d2f8)` | Windows PAL, GC, thread, wait, diagnostics, unwind, NUMA, and process-support components retained by the broad runtime image; only PE import thunks observed, no call in the successful path | No | No | B: per-symbol unique fail-fast stub; intentionally unsupported, not a no-op compatibility layer | `objdump -p`, `objdump -d -Mintel`, `link.rsp`; three positive traces and the fail-fast control |
| `ole32.dll` | `CoGetApartmentType (0x7d408)`; `CoInitializeEx (0x7d410)`; `CoUninitialize (0x7d418)`; `CoWaitForMultipleHandles (0x7d420)` | COM apartment/wait support retained by PAL/runtime; no reachable call in the successful path | No | No | B: fail-fast stubs | `objdump -p`; no calls in positive traces |
| `api-ms-win-crt-math-l1-1-0.dll` | `log (0x7d340)` | Workstation GC/runtime configuration math; no reachable call in the successful path | No | No | B: fail-fast stub | `objdump -p`; runtime-pack/link response; no calls in positive traces |
| `api-ms-win-crt-string-l1-1-0.dll` | `strcmp (0x7d3c8)`; `strcpy_s (0x7d3d0)`; `strcpy (0x7d3d8)`; `_stricmp (0x7d3e0)`; `strlen (0x7d3e8)` | CRT/PAL configuration and diagnostics support; no reachable call in the successful path | No | No | B: fail-fast stubs | `objdump -p`; `link.rsp`; no calls in positive traces |
| `api-ms-win-crt-convert-l1-1-0.dll` | `strtoull (0x7d308)` | Runtime configuration parsing; no reachable call in the successful path | No | No | B: fail-fast stub | `objdump -p`; no calls in positive traces |
| `api-ms-win-crt-stdio-l1-1-0.dll` | `__stdio_common_vsnprintf_s (0x7d3b8)` | CRT diagnostic formatting; no reachable call in the successful path | No | No | B: fail-fast stub | `objdump -p`; no calls in positive traces |
| `api-ms-win-crt-runtime-l1-1-0.dll` | `abort (0x7d350)`; `_register_onexit_function (0x7d358)`; `terminate (0x7d360)`; `_cexit (0x7d368)`; `_crt_atexit (0x7d370)`; `_execute_onexit_table (0x7d378)`; `_initterm_e (0x7d380)`; `_initialize_onexit_table (0x7d388)`; `_initterm (0x7d390)`; `_initialize_narrow_environment (0x7d398)`; `_seh_filter_dll (0x7d3a0)`; `_configure_narrow_argv (0x7d3a8)` | CRT/DLL bootstrap and termination objects (`dllmain.obj`, `bootstrapperdll.obj`); the current attach helper calls only `_initialize_onexit_table` | `_initialize_onexit_table` only | No | A for `_initialize_onexit_table` in the CRT opt-in build; B for the other 11 symbols | `link.rsp`; PE entry RVA `0x77840`; two dynamic init calls, no registration or execution |
| `api-ms-win-crt-heap-l1-1-0.dll` | `free (0x7d318)`; `_callnewh (0x7d320)`; `calloc (0x7d328)`; `malloc (0x7d330)` | CRT allocation and NativeAOT GC/runtime support; no allocation is attempted by this probe | No | No | B: fail-fast stubs; allocation is a later blocker | `objdump -p`; `Runtime.WorkstationGC.lib`; no calls in positive traces |

The table contains all ten descriptors and all 124 symbols. It is intentionally grouped into functional and fail-fast KERNEL32 rows so the 18/106 treatment split is visible without hiding any symbol.

For the current time-enabled census, the single exception to that historical grouping is:

| Module | Symbol | IAT | Current treatment | Exact caller | Current reachability |
| --- | --- | ---: | --- | --- | --- |
| `KERNEL32.dll` | `GetSystemTimeAsFileTime` | `0x7e1e0` | A: isolated UEFI-backed `FILETIME` implementation | security-cookie initializer `0x180078290`, direct call `0x1800782ca` | returns once; QPC is reached next |
| `KERNEL32.dll` | `QueryPerformanceCounter` | `0x7e0c8` | A: allocation-free UEFI-backed monotonic counter wrapper | security-cookie initializer `0x1800782f9` | returns one normalized reading; next boundary is CRT on-exit initialization |
| `KERNEL32.dll` | `QueryPerformanceFrequency` | `0x7e0d0` | A: paired positive-frequency query for the selected source | QPF diagnostic path at the NativeAOT startup boundary | returns `0x369e99` on the ACPI PM profile; not reached by the default startup trace |

The resulting current allocation-startup treatment is 21 functional / 103 fail-fast before the CRT opt-in. The CRT-enabled treatment is 22 functional / 102 fail-fast, and the SLIST-enabled treatment is 23 functional / 101 fail-fast. The historical 18/106 and 19/105 tables remain in earlier evidence to preserve the prior Gate 4 sequence.

## CRT on-exit bootstrap census

The current NativeAOT attach helper at preferred address `0x180077c70` calls `_initialize_onexit_table` twice, with table addresses `0x1800b5e98` and `0x1800b5eb0`. Both calls returned zero in the complete CRT-enabled traces, and both tables ended with equal `first`, `last`, and `end` fields. The next observed import was `KERNEL32.dll!InitializeSListHead`.

`_register_onexit_function`, `_execute_onexit_table`, `_crt_atexit`, and `_cexit` are imported or statically present but were not dynamically reached. `atexit` and `_c_exit` are not present in this PE's import census. Static disassembly shows the nearby `_crt_atexit` helper can reference registration and `_cexit`; that is a reachability fact, not evidence that startup registration or shutdown occurred. The complete routine-by-routine contract and negative controls are in [CRT_ONEXIT_BOOTSTRAP.md](CRT_ONEXIT_BOOTSTRAP.md).

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

The remaining 103 current imports were not declared unused merely because they were absent from one trace: the link response and disassembly identify their retaining components, and the negative fail-fast stubs prove that any accidental reachability is detected. The historical 106-symbol fail-fast set is retained in prior evidence. These are deferred runtime services, not silently supported services.

## NativeAOT components and deferred boundaries

## Allocation differential and startup trace

The allocation-enabled shared artifact has SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` and remains 10 descriptors / 124 symbols. `tools\Compare-AllocationArtifacts.ps1` compares the two manifests and map XML files; the retained report is `artifacts\allocation-enabled-final-20260728-060439-726\allocation-differential.json` and passes because the import sets are identical while the allocation probe's EEType, constructor, and `AllocateOne` appear only in the staged map.

The pre-change clean opt-in startup trace called the allocation PE's actual entry RVA `0x77840` with process-attach arguments after the existing loader TLS setup and stopped at `KERNEL32.dll!GetSystemTimeAsFileTime`. The current SLIST-enabled trace passes FILETIME, QPC, and QPF, initializes both on-exit tables, initializes one x64 SLIST header, and reaches `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. Temporary exploratory shims were not retained; no dummy CRT, virtual-memory, event, or thread implementation is counted as support.

The separate pre-startup allocation run reaches the generated `RhpNewFast` path with TLS allocation limit and pointer both zero, and the managed probe returns `-10`. This is the exact current first-allocation blocker, not evidence of a successful allocation.

`link.rsp` retains `dllmain.obj`, `bootstrapperdll.obj`, `Runtime.WorkstationGC.lib`, `aotminipal.lib`, disabled EventPipe/standalone-GC components, compression/native support, and Windows import libraries. The map contains `ModuleInitializerList`, `RuntimeConfigurationBlob`, TLS, GC statics, exception metadata, and thread-static metadata. The current proof provides only the minimum one-thread transition state; it does not provide a GC heap, virtual-memory allocator, process/threading system, COM, CRT, exceptions, or unwinding.

The prior static-link attempt remains evidence that removing the import directory by linking the standard static runtime is not a clean solution: it produced 158 unresolved externals spanning memory, threads/FLS, COM, unwind/context, TLS, CRT, stack probing, and allocation operators.

## SLIST evidence-closure qualification (2026-07-29)

The current census remains 23 functional / 101 fail-fast imports with `UNRESOLVED_REQUIRED_IMPORTS=0`. The final positive artifact set was frozen at loader `333F110626390045D8E9DB5081A99D198BB84720F5519CDCB4FE3B74B3C2CE9C` and payload `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. One fresh run proved the functional `InitializeSListHead` call and advanced to `_initterm_e`; the required three consecutive complete final-hash runs were not proven because all three final-sequence logs were incomplete.

This qualification does not change the dependency census or add `_initterm_e`, allocation, GC, thread registration, or SLIST companion support. The next dependency remains `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`.

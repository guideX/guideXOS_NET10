# Dependency census

This census explains why the minimal source still produces a platform-bound artifact. It separates what is physically linked from what the PE expects from Windows/CRT, and it records the negative static-link experiment.

## Causal summary

The causal chain is:

```text
ManagedMain with an unmanaged callback
  -> NativeAOT transition helpers and CoreLib metadata
  -> standard NativeAOT startup/runtime-pack composition
  -> workstation GC, minipal, unwind/exception, TLS, diagnostics support
  -> Windows and Universal CRT imports
  -> PE/COFF shared image is not freestanding
```

The managed body does not call allocation, exceptions, threads, or host I/O. The NativeAOT composition still carries support for those runtime paths and its Windows implementation imports their platform primitives. This is composition evidence, not evidence that all those paths are executed by `ManagedMain`.

## PE import census

The Gate 3 manifest records ten import descriptors. In the table, “required” means required to load this shared PE through the current direct-PE contract, not necessarily reached by the method body.

| Dependency | Emitted because | NativeAOT-provided | Platform-provided | Required for first proof | Proposed treatment | Evidence |
| --- | --- | :-: | :-: | :-: | --- | --- |
| `ADVAPI32.dll`: `RegisterEventSourceW`, `ReportEventW`, `OpenProcessToken`, `AdjustTokenPrivileges`, `LookupPrivilegeValueW`, `DeregisterEventSource` | diagnostics and large-page/runtime support in Windows runtime composition | No | Yes | Yes for this PE loader; the current proof stops before resolution | Do not stub. Either use a platform support layer with proven reachability or change artifact/composition | shared PE import directory; `objdump -p` |
| `bcrypt.dll`: `BCryptGenRandom` | NativeAOT/runtime random-byte support | No | Yes | Yes for this PE loader | Provide a real entropy contract later, or remove the dependency through a measured configuration change | shared PE import directory; `nm -u` |
| `KERNEL32.dll` | Windows PAL, GC, threads, waits, memory, timing, process and error paths | No | Yes | Yes for this PE loader | No broad shim set. Isolate and replace only a proven path in a later experiment | shared PE import directory; `link.rsp` |
| `ole32.dll`: `CoGetApartmentType`, `CoInitializeEx`, `CoUninitialize`, `CoWaitForMultipleHandles` | Windows PAL COM/FLS initialization and wait support | No | Yes | Yes for this PE loader | Must be removed or replaced by a real UEFI/runtime contract before entry | shared PE import directory; static link attempt |
| `api-ms-win-crt-math-l1-1-0.dll`: `log` | GC/runtime configuration math | No | Yes | Yes for this PE loader | Do not add a CRT wholesale; determine whether a no-GC startup configuration can eliminate it | PE import directory; `Runtime.WorkstationGC.lib` |
| `api-ms-win-crt-string-l1-1-0.dll`: `strcmp`, `strcpy_s`, `strcpy`, `_stricmp`, `strlen` | runtime configuration/PAL support | No | Yes | Yes for this PE loader | Replace only with audited freestanding routines if still reachable | PE import directory; static link attempt |
| `api-ms-win-crt-convert-l1-1-0.dll`: `strtoull` | GC/runtime configuration parsing | No | Yes | Yes for this PE loader | Remove configuration path or provide a deliberate parser later | PE import directory; static link attempt |
| `api-ms-win-crt-stdio-l1-1-0.dll`: `__stdio_common_vsnprintf_s` | diagnostic/fail-fast formatting | No | Yes | Yes for this PE loader | Keep diagnostics out of the first proof; do not fake formatting | PE import directory; static link attempt |
| `api-ms-win-crt-runtime-l1-1-0.dll` | CRT startup/termination/on-exit support | No | Yes | Yes for this PE loader | Do not invoke CRT startup from the freestanding contract | PE import directory; static link attempt |
| `api-ms-win-crt-heap-l1-1-0.dll`: `free`, `_callnewh`, `calloc`, `malloc` | CRT/runtime allocation support | No | Yes | Yes for this PE loader; also a direct signal of future allocation work | Keep allocation out of the first proof; later replace through a deliberate allocator contract | PE import directory; `RhpNew*`/`RhAllocate*` symbols |

The current UEFI loader proves that the import directory is present and counts exactly ten descriptors. It refuses to call `ManagedMain` when that count is nonzero. No import has been hidden with a no-op implementation.

## Physically linked NativeAOT components

The linker response physically brings in these standard runtime-pack components:

| Component | Category | Why it is present | First-proof status |
| --- | --- | --- | --- |
| `dllmain.obj`, `bootstrapperdll.obj` | NativeAOT startup | DLL/process startup and runtime initialization | Not executed by the UEFI loader |
| `Runtime.WorkstationGC.lib` | GC, memory, thread state, synchronization | Runtime-pack default workstation GC implementation | Physically linked; platform dependent |
| `eventpipe-disabled.lib`, `standalonegc-disabled.lib`, `Runtime.VxsortDisabled.lib` | diagnostics/GC composition | Standard NativeAOT composition with optional paths disabled | Physically linked or selected by response; not evidence of freestanding support |
| `aotminipal.lib` | platform abstraction, timing, CPU/debug | NativeAOT PAL and support routines | Physically linked; Windows imports remain |
| compression and globalization native libraries | runtime-pack composition | Standard NativeAOT link response | Present in the response; not called by the managed body |
| Windows import libraries | platform abstraction | Resolve the PAL’s Windows API references | Explicitly incompatible with the current UEFI loader |

The exact linker response is retained under `src\ManagedEntryProbe\obj\Release\net10.0\win-x64\native\link.rsp`.

## NativeAOT helper census

`nm -u` on the NativeAOT object reported 115 unique undefined symbols. The complete set is grouped below so later experiments can identify what changed:

### Runtime helpers and metadata

```text
RhAllocateNewArray RhAllocateNewObject RhBulkMoveWithWriteBarrier
RhCompatibleReentrantWaitAny RhCurrentOSThreadId RhFindBlob
RhFindMethodStartAddress RhGetCrashInfoBuffer RhGetGcCollectionCount
RhGetKnobValues RhGetMaxGcGeneration RhGetMemoryInfo RhGetModuleFileName
RhGetOSModuleFromPointer RhGetProcessCpuCount RhGetRuntimeVersion
RhHandleFree RhHandleGetDependent RhHandleSet RhNewString
RhpAssignRef RhpByRefAssignRef RhpCallCatchFunclet RhpCallFilterFunclet
RhpCallFinallyFunclet RhpCheckedAssignRef RhpCheckedLockCmpXchg
RhpCheckedXchg RhpCollect RhpCopyContextFromExInfo RhpCreateTypeManager
RhpEHEnumInitFromStackFrameIterator RhpEHEnumNext RhpEndNoGCRegion
RhpFallbackFailFast RhpFirstChanceExceptionNotification RhpGcPoll
RhpGcSafeZeroMemory RhpGetClasslibFunctionFromCodeAddress
RhpGetClasslibFunctionFromEEType RhpGetCurrentThreadStackTrace
RhpGetDispatchCellInfo RhpGetGcTotalMemory RhpGetModuleSection
RhpGetNextFinalizableObject RhpGetThreadAbortException RhpHandleAlloc
RhpHandleAllocDependent RhpInitialDynamicInterfaceDispatch RhpNewArrayFast
RhpNewFast RhpNewFinalizable RhpNewPtrArrayFast RhpPInvoke RhpPInvokeReturn
RhpRegisterOsModule RhpRethrow RhpReversePInvoke RhpReversePInvokeReturn
RhpSearchDispatchCellCache RhpSetThreadDoNotTriggerGC RhpSfiInit RhpSfiNext
RhpSignalFinalizationComplete RhpStartNoGCRegion RhpThrowEx RhpTrapThreads
RhpUpdateDispatchCellCache RhpWaitForFinalizerRequest RhRegisterFrozenSegment
RhRegisterInlinedThreadStaticRoot RhReRegisterForFinalize RhSetThreadExitCallback
RhSpinWait RhSuppressFinalize RhUpdateFrozenSegment
```

These symbols fall into compiler-generated transitions, GC/allocation, handles, finalization, exception handling, type/module registration, synchronization, and thread-state categories. Presence does not prove reachability from this method; it does prove that the standard object/runtime composition is not a tiny method-only library.

### Platform, CRT, TLS, and diagnostics

```text
__security_cookie _tls_index DebugDebugger_IsNativeDebuggerAttached
BCryptGenRandom CloseHandle CoGetApartmentType CoInitializeEx CoUninitialize
CreateEventExW DeregisterEventSource DuplicateHandle FormatMessageW
GetConsoleOutputCP GetCurrentProcess GetCurrentProcessorNumberEx
GetCurrentThread GetEnvironmentVariableW GetLastError GetModuleFileNameW
GetStdHandle GetThreadPriority GetTickCount64 LocalFree memmove memset
MultiByteToWideChar QueryPerformanceCounter QueryPerformanceFrequency
RaiseFailFastException RegisterEventSourceW ReportEventW SetEvent SetLastError
Sleep VirtualAlloc VirtualFree WaitForMultipleObjectsEx WideCharToMultiByte
WriteFile
```

The unresolved `Rhp*`/`Rh*` names are expected from NativeAOT runtime support libraries. The Windows/CRT names are expected from platform support libraries and are not supplied by UEFI. `_tls_index` is a concrete thread-local-state assumption. `__security_cookie`, memory routines, and fail-fast/diagnostic symbols are startup or safety support.

## Static-link experiment

The static form was linked with the standard NativeAOT runtime-pack libraries and no Windows import libraries. The attempt failed with 158 unresolved externals, including `VirtualAlloc`, `VirtualFree`, `CreateThread`, `FlsAlloc`, `CoInitializeEx`, `RtlCaptureContext`, `_tls_index`, CRT string/math/heap functions, `__chkstk`, and C++ runtime allocation operators. The full stdout is in `artifacts\gate4\static-link-attempt\link.stdout.log`.

This is the decisive reason the current milestone does not add a large platform shim. The missing set spans memory management, thread-local state, synchronization, exception/unwind, timing, process/diagnostic services, and CRT support. Adding no-op stubs would hide the actual runtime contract and violate the failure policy.

## Feature-removal observations

The ILC response confirms that the following feature switches changed the NativeAOT composition as intended: invariant globalization/timezone, disabled EventPipe, disabled debugger, disabled stack-trace support, non-server/non-concurrent GC, and reflection scan disabled. They did not make the runtime freestanding; the Windows PAL and GC runtime still required the platform set above. The response also contains `--initassembly` entries, so module initialization remains an unproven startup requirement.

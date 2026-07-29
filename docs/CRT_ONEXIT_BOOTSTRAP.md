# CRT on-exit bootstrap contract

Status: the smallest startup contract for `_initialize_onexit_table` is implemented and verified. The implementation initializes the two empty NativeAOT on-exit tables used by the current attach path. It does not implement callback registration, callback execution, heap growth, shutdown, or process teardown.

## Baseline and scope

The pass began on branch `main`, HEAD `52bdc9cad93bfd4404e11c07defa11db955f4afa`, tracking `origin/main`, with a clean worktree. The pre-change timing evidence remains under `artifacts\qpc-final-20260729-allocation`; the managed payload SHA-256 is `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`.

The current NativeAOT attach path is:

```text
PE loader
  -> relocation
  -> TLS / GS / TEB / FLS initialization
  -> NativeAOT entry RVA 0x77840
  -> security-cookie initializer 0x180078290
     -> GetSystemTimeAsFileTime
     -> QueryPerformanceCounter
  -> attach helper 0x180077c70
     -> _initialize_onexit_table(table at 0x1800b5e98)
     -> _initialize_onexit_table(table at 0x1800b5eb0)
  -> KERNEL32.dll!InitializeSListHead: next boundary
```

The two calls are visible in the PE disassembly at `0x180077c8d` and `0x180077c9d`. The first fresh CRT-enabled QEMU trace reached both calls, returned zero from both, and reached `InitializeSListHead` with no unresolved required imports. Complete positive logs are retained in `artifacts\crt-onexit-init-final` and `artifacts\crt-onexit-init-final-v2`.

## Reachable bootstrap census

| Routine | Purpose | Caller / evidence | Reached now | Allocation | Synchronization | Registration | Execution | Lifetime role |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `_initialize_onexit_table` | Establish an empty opaque on-exit table. | Attach helper `0x180077c70`; calls at `0x180077c8d` and `0x180077c9d`. | Yes, twice | No | No in this profile | No | No | Startup initialization |
| `_initterm_e` | Invoke a C initializer array and stop on the first nonzero result. | Imported at IAT RVA `0x7e380`; no call before the current boundary. | No | No intrinsic allocation in the routine; initializer-specific | No intrinsic synchronization | No | Executes initializer functions, not on-exit callbacks | Startup-only |
| `_initterm` | Invoke a C initializer array without error returns. | Imported at IAT RVA `0x7e390`; no call before the current boundary. | No | No intrinsic allocation in the routine; initializer-specific | No intrinsic synchronization | No | Executes initializer functions, not on-exit callbacks | Startup-only |
| `_register_onexit_function` | Append one encoded callback to an initialized table. | Imported thunk `0x18007be6b`; referenced by the static `_crt_atexit` helper at `0x180077f54`. | No | Yes when the table needs storage/growth | Yes in the UCRT implementation | Yes | No | Registration, normally startup or library use |
| `_execute_onexit_table` | Run registered callbacks and release table storage. | Imported thunk `0x18007be71`; no call in the current static or dynamic attach path. | No | Frees/grows-owned storage; no current allocation | Yes in the UCRT implementation | No | Yes, LIFO | Shutdown |
| `_crt_atexit` | CRT-facing registration wrapper. | Imported thunk `0x18007be77`; referenced at `0x180077f43` by a nearby registration wrapper at `0x180077f30`. | No | Inherits registration behavior | Inherits registration behavior | Yes if called | No | Registration |
| `atexit` | Standard C registration wrapper. | No `atexit` import exists; nearby helper `0x180077f30` branches to `_crt_atexit` or `_register_onexit_function`, but is not reached from the current attach path. | No | If present, inherits CRT registration behavior | If present, inherits CRT registration behavior | Yes if called | No | Registration |
| `_cexit` | Execute registered CRT callbacks and terminate CRT cleanup without terminating the process directly. | Imported thunk `0x18007be7d`; no static or dynamic call in the current attach path. | No | Shutdown cleanup may free storage | Shutdown synchronization may occur | No | Yes | Shutdown |
| `_c_exit` | Perform CRT termination state transition without executing registered callbacks. | Not present in the current import census. | No | No current evidence | No current evidence | No | No | Shutdown-only |

The static helper references around `_crt_atexit` are nearby CRT code, not proof that registration or shutdown is reachable from the current attach path. Dynamic execution stops at `InitializeSListHead` before any of those routines can run. The current positive traces contain two `CRT_ONEXIT_INIT_CALL`, two zero returns, no registration marker, no execution marker, and no allocation context.

## Exact `_initialize_onexit_table` contract

The public Microsoft contract describes `table` as an in/out pointer and requires initialization before registration or execution. It returns zero for success and a negative value for failure; registration appends callbacks, while execution invokes and clears them. See [Microsoft's CRT on-exit table documentation](https://learn.microsoft.com/en-us/cpp/c-runtime-library/execute-onexit-table-initialize-onexit-table-register-onexit-function?view=msvc-170).

For the current UCRT-compatible table representation, the exact fields are three opaque pointers: `first`, `last`, and `end`. The UCRT implementation initializes an empty table as follows; the source is preserved in the [ReactOS Microsoft-derived onexit implementation](https://doxygen.reactos.org/d2/d7c/onexit_8cpp_source.html):

1. A null `table` pointer fails with a negative result.
2. If `table->first != table->end`, the table is treated as already initialized and the function returns zero without changing it. This is the idempotent/non-empty-state rule; the routine does not validate arbitrary contents as a callback list.
3. Otherwise, the implementation obtains the encoded representation of a null callback and writes the same encoded-null value to `first`, `last`, and `end`, then returns zero.
4. A legal empty initialized state is therefore `first == last == end == encoded-null`. A populated state is an opaque encoded pointer range owned by the CRT table implementation.
5. The caller owns the table object and its lifetime. Initialization does not take ownership of the caller's storage, register a callback, execute a callback, allocate heap storage, or establish shutdown behavior.

The profile implementation in `src\Gate4Harness\crt_onexit.c` is bounded and allocation-free. Because this freestanding loader does not contain the UCRT cookie global, the loader supplies the NativeAOT image's post-entry security-cookie address as the profile's fast-encoded-null source. This is intentionally a narrow profile bridge, not a claim to provide general UCRT pointer encoding for unrelated images. A zero encoded-null source is treated as failure. Success markers are emitted only when the return value is zero and all three table fields are equal.

The host negative tests cover null arguments, empty initialization, repeated initialization, marker mutation, opaque non-empty state, and a disabled/zero encoding source. The non-empty-state test is deliberately not reported as a detected corruption: the Windows contract treats `first != end` as already initialized and preserves it.

## What was not implemented

No `_register_onexit_function`, `_execute_onexit_table`, `_crt_atexit`, `atexit`, `_cexit`, or `_c_exit` behavior was added. No callback was registered or executed, no dynamic table growth was attempted, no heap allocation occurred, and no GC initialization occurred. The downstream SLIST initialization boundary is now separately closed for one aligned empty x64 header; no SLIST companion operation is implied. The next authentic dependency after that bounded transition is `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`.

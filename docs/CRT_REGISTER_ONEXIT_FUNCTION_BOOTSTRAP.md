# Microsoft x64 `_register_onexit_function` bootstrap contract

This task implements only the Microsoft x64 `_register_onexit_function` contract required by the current NativeAOT startup path.

Status: the imported register call is now reached, identified, and bounded. The implementation closes the ABI, table-layout, pointer-encoding, validation, existing-capacity append, and truthful allocation-boundary contract. The current NativeAOT call has an empty initialized table, so the profile returns the required negative result at the first UCRT growth dependency. It does not implement a general allocator, callback execution, shutdown, `_execute_onexit_table`, `_cexit`, `_crt_atexit`, or managed allocation.

## Baseline and evidence scope

Before the change, the repository was on branch `main`, at HEAD `034c04a15c6dab8c824716ef8b8d56c8a6e0ebee`, tracking `origin/main`, with a clean worktree. That commit is the preceding `GetProcAddress` milestone. The fresh baseline was rerun with the prior immutable GetProcAddress artifact and stopped at:

```text
api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function
```

The baseline evidence is retained under `artifacts\register-onexit-boundary-baseline-20260801-fresh`; its serial SHA-256 is `71B841D9B43406E970A74875E85C2824CCA92362FB7CC9DEC415C037E84C3B11`. It records the preceding `GetProcAddress(NULL, "RtlDllShutdownInProgress")` result as `NULL` with last error `127`, followed immediately by the register import boundary.

The enabled implementation evidence is immutable under `artifacts\register-onexit-final-evidence-v5-20260802`. Its loader SHA-256 is `4B8F505AE86A2FF6232CB8C570CB499F6439ED3068E915600A1E5D57836971A2`; the unchanged NativeAOT payload SHA-256 is `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`. Three fresh QEMU processes passed with PIDs `15740`, `27740`, and `29220`; each serial log is `258443` bytes, and each has a distinct per-run serial hash. The disabled routing control is under `artifacts\register-onexit-disabled-evidence-v3-20260802` and passed three fresh runs at the original register fail-fast boundary with census `36 / 88 / 0`.

## Authoritative Microsoft contract

The public Microsoft CRT documentation gives the `_register_onexit_function` prototype and states that the table must be initialized first, the function appends the callback, and zero means success while a negative value means failure: [Microsoft CRT on-exit table documentation](https://learn.microsoft.com/en-us/cpp/c-runtime-library/execute-onexit-table-initialize-onexit-table-register-onexit-function?view=msvc-170).

The installed Windows SDK `10.0.26100.0` provides the matching declarations in `ucrt\corecrt_startup.h` and the implementation in `ucrt\startup\onexit.cpp`:

```c
typedef void (__cdecl* _PVFV)(void);
typedef int (__cdecl* _onexit_t)(void);

typedef struct _onexit_table_t {
    _PVFV* _first;
    _PVFV* _last;
    _PVFV* _end;
} _onexit_table_t;

int __cdecl _register_onexit_function(
    _onexit_table_t* _Table,
    _onexit_t _Function);
```

On Microsoft x64, `__cdecl` uses the normal Microsoft x64 register ABI: `_Table` is in `RCX`, `_Function` is in `RDX`, and the 32-bit `int` result is returned in `EAX`. The three table fields are 8-byte pointers at offsets `0`, `8`, and `16`; the structure is 8-byte aligned and 24 bytes wide. `src\Gate4Harness\crt_onexit.h` has compile-time assertions for those facts.

The function argument is nullable in the SDK annotation. A null callback is therefore a valid input to the narrow contract; if storage exists, its encoded representation is appended like any other callback. The current startup call passes a non-null callback in the relocated NativeAOT `.text` region.

## UCRT growth and encoding facts

The authoritative UCRT source uses an encoded pointer for each table field and callback slot. The empty initialized state is three equal encoded-null values. The relevant growth policy is:

```text
initial_table_count       = 32 elements (0x20)
minimum_table_increment   = 4 elements (0x4)
maximum_table_increment   = 512 elements (0x200)
```

For the current empty table, `old_count = end - first = 0`, so the source selects the initial count and reaches the allocation dependency:

```text
_recalloc_crt_t(_PVFV, NULL, 0x20)
```

The profile does not guess or replace that allocator. It reports `GROWTH_REQUIRED`, returns `-1`, leaves all three raw table fields unchanged, and marks allocation as not attempted. This is the genuine stopping boundary for this task.

The Microsoft x64 fast pointer encoding used by the UCRT source is represented as:

```text
shift  = security_cookie % 64
encode = ROR64(pointer, 64 - shift) ^ security_cookie
decode = ROR64(encoded ^ security_cookie, shift)
```

The implementation reads the relocated NativeAOT image’s post-entry security-cookie address as the narrow encoding source. It uses the current cookie value for both encoding and decoding because the NativeAOT startup path refreshes the cookie before the register call. This is a profile bridge for the one mapped image, not a general UCRT cookie or pointer-encoding service.

## Implemented boundary

The exact `api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function` IAT slot is descriptor `8`, IAT RVA `0x7D358`, preferred IAT `0x18007D358`. In the final positive traces:

```text
static call site       = 0x180077E13
caller start/end       = 0x180077DF0 .. 0x180077E30
caller                  = NativeAOT_CRT_atexit_registration_helper
table RVA               = 0xB3E78
callback RVA            = 0x37BD0
```

The loader emits both preferred static addresses and relocated runtime addresses, checks `return_address = runtime_call_site + 6`, and records the table and callback regions. The final trace proves the table is writable and readable in the mapped NativeAOT image, while the callback belongs to the executable `MANAGED_IMAGE_TEXT` region.

Before the call, all three decoded fields are zero and all three raw fields equal the encoded-null value written by the earlier `_initialize_onexit_table` call for table index `0`. The registration report proves `initialized_table_match=1`, `initialized_table_index=0`, `used=0`, `capacity=0`, and `remaining=0`. Since `last == end`, growth is required. After the bounded return, all three raw fields still equal their before values and `TABLE_UNCHANGED=1`; no callback slot was written.

The loader-side checked core performs only bounded validation and an existing-capacity append path. It verifies canonical and image-backed table addresses, readable/writable table storage, initialized-table identity, decoded range ordering/alignment, callback executability, and pointer encoding. It does not call an allocator, invoke a callback, acquire a general CRT lock, execute shutdown, or claim successful registration for the current empty-table call.

## Evidence and controls

The three positive runs pass `tools\Validate-RegisterOnexitEvidence.ps1` through `tools\Run-RegisterOnexitFinalValidation.ps1`. The validator checks immutable artifact hashes and lengths before and after every run, unique QEMU PIDs, distinct fresh serial hashes, complete marker ordering, the exact import census (`37` functional / `87` fail-fast / `0` unresolved), raw-field preservation, the growth policy, the allocation dependency, callback non-execution, and zero GC/managed-allocation state.

The disabled three-run control uses `RegisterOnexitDisabled`. It emits no register-route marker and stops at `UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function`, proving that the enabled path changes reachability only through the requested import route.

Focused host validation is available through:

```text
tools\Run-CrtOnexitHostTests.ps1
tools\Run-CrtOnexitRegisterHostTests.ps1
```

The register-focused vectors cover layout/ABI compilation, encoded empty state, empty-table growth detection without mutation, existing-capacity callback append, encoded callback storage, nullable callback input, full-table growth detection without mutation, initialized-table refresh, and no callback execution. The core object has no unexpected external references.

## Explicit non-claims

This milestone does not prove or implement `_recalloc_crt_t`, heap ownership, memory reclamation, CRT locking, `_execute_onexit_table`, reverse-order callback execution, callback lifetime, shutdown, process teardown, GC initialization, managed-thread registration, or managed allocation. The correct next experiment is the allocator contract itself, only if it is explicitly brought into scope.

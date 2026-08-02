# Microsoft x64 `_register_onexit_function` bootstrap contract

This task implements only the Microsoft x64 `_register_onexit_function` contract required by the current NativeAOT startup path.

Status: the imported register call is now reached, identified, and closed for the artifact-specific initial empty-table storage path. The implementation validates the ABI and encoded table state, allocates exactly `0x100` bytes through the already-proven UEFI `BootServices->AllocatePool(EFI_LOADER_DATA, 0x100)` primitive, initializes 32 encoded slots, publishes encoded bounds, and returns zero without executing the callback. It does not implement nonempty growth, a general allocator, callback execution, shutdown, `_execute_onexit_table`, `_cexit`, `_crt_atexit`, or managed allocation.

## Baseline and evidence scope

Before the change, the repository was on branch `main`, at HEAD `4604b74a4f4f0736bcbb996e8b015aba17dc1824`, tracking `origin/main`, with a clean worktree. The fresh baseline was rerun with the prior immutable GetProcAddress artifact and stopped at:

```text
api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function
```

The baseline evidence is retained under `artifacts\register-onexit-boundary-baseline-20260801-fresh`; its serial SHA-256 is `71B841D9B43406E970A74875E85C2824CCA92362FB7CC9DEC415C037E84C3B11`. It records the preceding `GetProcAddress(NULL, "RtlDllShutdownInProgress")` result as `NULL` with last error `127`, followed immediately by the register import boundary.

The enabled implementation evidence is under `artifacts\register-onexit-storage-final-v3-20260802`. Its loader SHA-256 is `5C607C3120803FBBE6D706315F1A19C2E92F678A58696B6BDB9257F683A074F`; the unchanged NativeAOT payload SHA-256 is `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`. Three fresh QEMU processes passed with PIDs `12432`, `15920`, and `13048`; each serial log is `259822` bytes with a distinct per-run serial hash, and all QEMU processes were cleaned up. The disabled routing control is under `artifacts\register-onexit-storage-disabled-final-v3-20260802` and passed three fresh runs at the original register fail-fast boundary with census `36 / 88 / 0`.

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

For the current empty table, the observed initial storage contract is:

```text
AllocatePool(EFI_LOADER_DATA, 0x100)
```

The guideXOS profile uses only this already-proven pool primitive for the exact first block. It does not add `_recalloc`, infer a private CRT allocator, resize existing nonempty storage, or claim generalized CRT heap ownership.

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

Before the call, all three decoded fields are zero and all three raw fields equal the encoded-null value written by the earlier `_initialize_onexit_table` call for table index `0`. The registration report proves `initialized_table_match=1`, `initialized_table_index=0`, `used=0`, `capacity=0`, and `remaining=0`, classifies the table as decoded empty, and records zero allocation calls before registration. Each positive run then records exactly one successful `AllocatePool` call and one `0x100`-byte block. The decoded post-call state is `beginning=block`, `next=block+8`, and `end=block+0x100`; slot 0 decodes to the callback, slots 1 through 31 pass the encoded-null validator, the result is zero, and the callback execution count remains zero.

The loader-side checked core performs only bounded validation, the initial empty-table allocation path, and the existing-capacity append path. It verifies canonical and image-backed table addresses, readable/writable table storage, initialized-table identity, decoded range ordering/alignment, callback executability, allocation bounds, slot bounds, and pointer-encoding round trips. Allocation and table publication are transactional; any failed post-allocation check frees the block and leaves the table unchanged. It does not invoke a callback, acquire a general CRT lock, execute shutdown, or claim a generalized allocator contract.

## Evidence and controls

The three positive runs pass `tools\Validate-RegisterOnexitEvidence.ps1` through `tools\Run-RegisterOnexitFinalValidation.ps1`. The validator checks the exact payload hash, immutable artifact hashes and lengths before and after every run, unique QEMU PIDs, distinct fresh serial hashes, complete marker ordering, the exact import census (`37` functional / `87` fail-fast / `0` unresolved), encoded before/after fields, one `0x100` allocation, decoded bounds, slot 0, all unused slots, callback non-execution, continuation beyond the register call, and zero GC/managed-allocation state.

The disabled three-run control uses `RegisterOnexitDisabled`. It emits no register-route marker and stops at `UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function`, proving that the enabled path changes reachability only through the requested import route.

Focused host validation is available through:

```text
tools\Run-CrtOnexitHostTests.ps1
tools\Run-CrtOnexitRegisterHostTests.ps1
```

The register-focused vectors cover layout/ABI compilation, encoded empty state, initial 32-slot storage, exactly one bounded test allocation, existing-capacity callback append, encoded callback storage, nullable callback input, full-table growth detection without mutation, initialized-table refresh, and no callback execution. The core object has no unexpected external references.

## Explicit non-claims

This milestone does not prove or implement `_recalloc`, nonempty table growth, heap ownership beyond the one UEFI pool call, memory reclamation beyond transactional rollback, CRT locking, `_execute_onexit_table`, reverse-order callback execution, callback lifetime, shutdown, process teardown, GC initialization, managed-thread registration, or managed allocation.

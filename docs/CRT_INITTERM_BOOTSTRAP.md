# Windows x64 `_initterm` bootstrap contract

Status: CLOSED for the narrow `api-ms-win-crt-runtime-l1-1-0.dll!_initterm` void-initializer table contract exercised by the current NativeAOT allocation/startup artifact. This document does not claim general CRT startup, general `.CRT$X??` processing, C++ constructor support, teardown, exceptions, GC readiness, managed-thread registration, or allocation.

## Milestone and baseline

The requested milestone was: implement and validate only the Microsoft x64 `_initterm` void-initializer table contract, then stop at the next authentic dependency.

The initial repository state was branch `main`, HEAD `a54b64eb07808b50ace4ee7c54ee655a6e90bc27`, upstream `origin/main`, and a clean worktree. The committed `_initterm_e` milestone is present at that HEAD; its source, host tests, evidence manifests, and QEMU logs were preserved. No commit or push was performed for this pass.

A fresh disposable pre-routing QEMU process was captured under `evidence\generated\initterm-baseline-20260730-fresh`. It reproduced the prior path through `_initterm_e` and stopped at:

```text
GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm
```

## Gate A: caller, import, and runtime arguments

The exact import is `api-ms-win-crt-runtime-l1-1-0.dll!_initterm`. In the NativeAOT payload its IAT slot is RVA `0x7e390`, preferred address `0x18007e390`, and runtime address `0x00000000054F7390` in the counted QEMU runs (`IMAGE_BASE=0x0000000005479000`). The static import thunk is at preferred `0x18007be47`.

The nearest identifiable caller is the NativeAOT attach/bootstrap helper beginning at preferred `0x180077550`. Its relevant sequence is:

```text
0x180077550  NativeAOT attach/bootstrap helper
0x1800775ad  lea rdx, [rip+...]       ; last = 0x18007e4b0
0x1800775b4  lea rcx, [rip+...]       ; first = 0x18007e468
0x1800775db  call 0x18007be47         ; _initterm IAT thunk
0x1800775e0  return site
```

The call uses the Microsoft x64 ABI: RCX is `first`, RDX is `last`, and the Windows-facing function returns no value. The runtime wrapper recorded return address `0x00000000054F05E0`.

| Bound | Preferred address / RVA | Runtime address |
| --- | ---: | ---: |
| `first` | `0x18007E468` / `0x7e468` | `0x00000000054F7468` |
| `last` (exclusive) | `0x18007E4B0` / `0x7e4b0` | `0x00000000054F74B0` |

The table is `0x48` bytes, or nine eight-byte pointer entries. The bounds came from the real call arguments, not from section names or symbols. The range lies in `.rdata`, whose PE characteristics are read-only data (`READONLY, DATA`; section RVA `0x7e000`). The table is readable, non-executable, and non-writable. The non-null slots contain direct relocated image pointers, not thunks or encoded pointers; slot zero is null. The non-null pointer slots have base-relocation entries at RVAs `0x7e470` through `0x7e4a8`; the null slot requires no relocation. One `_initterm` table is processed on this path, once per startup run.

## Gate B: authoritative Microsoft contract

Microsoft documents the two routines in [`_initterm, _initterm_e`](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/initterm-initterm-e?view=msvc-170). The installed Microsoft-derived UCRT source was also read directly from:

```text
C:\Program Files (x86)\Windows Kits\10\Source\10.0.26100.0\ucrt\startup\initterm.cpp
```

The authoritative void routine is equivalent to:

```cpp
extern "C" void __cdecl _initterm(_PVFV* const first, _PVFV* const last)
{
    for (_PVFV* it = first; it != last; ++it)
    {
        if (*it == nullptr)
            continue;
        (**it)();
    }
}
```

`_PVFV` is `void (__cdecl *)(void)`. On Microsoft x64, `__cdecl` uses the unified x64 calling convention; the guideXOS declaration explicitly uses GCC `ms_abi` so the callback ABI is unambiguous. `last` is exclusive, iteration is forward, null entries are skipped, `first == last` performs no calls, and the routine has no return channel for callback failures. The Microsoft source requires a valid range and does not define useful behavior for reversed, misaligned, noncanonical, unmapped, or otherwise malformed pointers. The guideXOS layer rejects those cases before dereferencing the table.

The source performs no allocation and does not catch exceptions. A callback can mutate later table slots under ordinary C memory rules because each slot is read as it is reached; duplicate function pointers remain separate entries and execute once per occurrence. `_initterm_e` is distinct: it uses `_PIFV`, returns the first nonzero callback result, and can report callback failure through that integer. `_initterm` cannot report success or failure beyond reaching the exclusive end without a detected fault or fail-fast boundary.

Microsoft's broader [CRT initialization](https://learn.microsoft.com/en-us/cpp/c-runtime-library/crt-initialization?view=msvc-170) documentation describes `.CRT$XCA`, `.CRT$XCU`, and `.CRT$XCZ` tables used for C++ dynamic initialization. The current table is in the NativeAOT image's `.rdata`, is called by a NativeAOT attach helper, and contains NativeAOT runtime static-state helpers. Therefore this evidence proves the `_initterm` table contract, not general C++ initialization and not a claim that this actual table is a C++ constructor table.

## Gate C: actual table census

The static preferred targets and their relocated runtime values are:

| Index | Table address (RVA) | Raw preferred value | Relocated runtime target | Section / executable | Size | Disassembly and observed role |
| ---: | ---: | ---: | ---: | --- | ---: | --- |
| 0 | `0x18007e468` (`0x7e468`) | `0x0000000000000000` | null | `.rdata`, n/a | n/a | skipped |
| 1 | `0x18007e470` | `0x180031d50` | `0x54aad50` | `.text`, yes | 16 bytes | loads code address `0x180031e10` and stores it in image static state through `0x180033870` |
| 2 | `0x18007e478` | `0x180031da0` | `0x54aada0` | `.text`, yes | 16 bytes | clears image static state at `0x1800b5c68` through `0x180042f10` |
| 3 | `0x18007e480` | `0x180031d90` | `0x54aad90` | `.text`, yes | 16 bytes | clears image static state at `0x1800b5650` |
| 4 | `0x18007e488` | `0x180031dc0` | `0x54aadc0` | `.text`, yes | 16 bytes | clears image static state at `0x1800b5cb0` |
| 5 | `0x18007e490` | `0x180031db0` | `0x54aadb0` | `.text`, yes | 16 bytes | clears image static state at `0x1800b57d0` |
| 6 | `0x18007e498` | `0x180031dd0` | `0x54aadd0` | `.text`, yes | 16 bytes | clears image static state at `0x1800b5ca8` |
| 7 | `0x18007e4a0` | `0x180031de0` | `0x54aade0` | `.text`, yes | 16 bytes | clears image static state at `0x1800b57c8` |
| 8 | `0x18007e4a8` | `0x180031df0` | `0x54aadf0` | `.text`, yes | 16 bytes | clears image static state at `0x1800b5cb8` |

Totals: nine entries, one null, eight non-null, eight invocations, eight returns, zero duplicate targets. The actual order is index `1` through `8` after the null slot. All targets are direct pointers into the loaded image's executable `.text`; no target is a writable-data pointer, external address, thunk, or encoded pointer. None of the eight functions directly references an imported API. Their concise side effects are bounded writes to NativeAOT image static state. No callback touched the security cookie, on-exit tables, environment, exception registration, thread registration, TLS allocation pointer, GC heap, allocation context, module constructor registration, or shutdown registration. The first new platform dependency was not inside a callback: after `_initterm` completed, managed startup reached `api-ms-win-crt-string-l1-1-0.dll!strcmp`.

## Gates D–F: guideXOS contract and validation

The internal contract is defined in `src/Gate4Harness/crt_initterm.h`:

```c
typedef void (GXOS_CRT_INITTERM_MS_ABI *GXOS_VOID_INITIALIZER)(void);

int gxos_crt_initterm_configure(const GXOS_CRT_INITTERM_CONTEXT *context);
int gxos_crt_initterm(GXOS_VOID_INITIALIZER *first,
                      GXOS_VOID_INITIALIZER *last,
                      GXOS_CRT_INITTERM_REPORT *report,
                      GXOS_CRT_INITTERM_TRACE trace);
```

The contract is intentionally local to this image/table boundary:

- Microsoft x64 callback ABI, `void (void)` callback type.
- Exclusive `last`, forward iteration, null skip, and duplicate-entry execution.
- Equal pointers accepted as an empty range; malformed or reversed ranges rejected.
- Canonical, aligned, image-contained bounds required.
- The table range must be wholly contained in one mapped readable PE section; headers and gaps are rejected.
- The loaded image must report relocations applied before the range is used.
- Non-null targets must be canonical, image-contained, and in a configured executable PE region; writable or non-executable data is rejected.
- Pointer-sized byte counts and slot addresses use checked arithmetic and a 4,096-entry bound.
- The iterator performs no allocation and assumes the current single startup thread.
- Processor faults and fail-fast imports are not swallowed. The pre-call marker identifies the callback; the post-call marker is emitted only after the callback returns.
- `CRT_INITTERM_OK` is emitted only when validation passed, every non-null entry was invoked, every invoked callback returned, and the exclusive end was reached.

The PE-facing wrapper routes only the exact DLL/symbol pair and preserves the Windows `void` ABI. Its diagnostic report includes table bounds, section permissions, relocations-applied state, entry/null/non-null/invoked/returned counts, current callback identity, completion status, and before/after TLS/QPC/allocation state.

## Gates G–I: controlled startup advancement

The counted trace sequence is:

```text
PE loader -> relocations -> TLS / GS / TEB / FLS
  -> NativeAOT entry -> FILETIME -> QPC / QPF
  -> _initialize_onexit_table twice -> InitializeSListHead
  -> _initterm_e: one null entry, result zero
  -> _initterm: nine entries, one null, eight callbacks returned
  -> api-ms-win-crt-string-l1-1-0.dll!strcmp
```

Every real callback has a `CRT_INITTERM_CALLBACK_BEGIN_INDEX` marker followed by a matching `CRT_INITTERM_CALLBACK_RETURN_INDEX` marker. No callback reached an unresolved import. No CPU exception, triple fault, hang, or callback non-return occurred. The terminal `strcmp` fail-fast is reached after `CRT_INITTERM_OK`, during the subsequent managed startup path; it is not a callback failure.

The callbacks caused no observable QPC change: state was `QPC_COUNT=1` before and after `_initterm`. Final startup later reports `QPC_COUNT=2`, zero regressions, and the `strcmp` boundary. TLS allocation pointer/limit remained `0/0`; managed-thread registration remained `0`; allocation context remained invalid; no managed allocation occurred. `GC_STARTUP_BEGIN` and the existing `GC_STARTUP_ADVANCED` labels are loader trace phases only. The explicit state markers report `GC_CONTRACT_INITIALIZED=0`, `GC_HEAP_USABLE=0`, `ALLOCATION_CONTEXT_CREATED=0`, and `MANAGED_ALLOCATION_COUNT=0`.

## Gate J: host tests

`tools\Run-CrtInittermHostTests.ps1` passed deterministic vectors for empty/equal ranges, null-only tables, one and multiple callbacks, null gaps, duplicates, exclusive-end poison, forward order, reversed and misaligned ranges, pointer overflow, noncanonical/out-of-image/non-executable targets, adjacent guards, callback state mutation, Microsoft x64 callback ABI, void-return-not-read behavior, and injected callback-fault accounting. The core object had no unresolved external references, so no external CRT implementation was linked accidentally. The unchanged `_initterm_e` host suite also passed all prior vectors and its no-external-reference check.

## Gate K: runtime state accounting

The positive runs report `PE_IMPORT_FUNCTIONAL=25`, `PE_IMPORT_FAILFAST=99`, `UNRESOLVED_REQUIRED_IMPORTS=0`, `_initterm` entry count `9`, null count `1`, non-null count `8`, invoked count `8`, returned count `8`, completion `1`, `QPC_REGRESSIONS=0`, `MANAGED_THREAD_REGISTERED=0`, `ALLOCATION_CONTEXT_VALID=0`, and the explicit negative GC/allocation state above. The import transition is exactly one new functional target: prior `_initterm_e` profile `24 / 100`, current `_initterm` profile `25 / 99`.

## Gate L: immutable QEMU evidence

The final counted evidence is `evidence\generated\crt-initterm-final-20260730-immutable-v2`. One artifact set was frozen before three fresh QEMU processes; each run used a new OVMF vars file, a unique PID, and the same hashes.

| Run ID | PID | Serial bytes | Exit | Result |
| --- | ---: | ---: | ---: | --- |
| `crt-initterm-final-20260730-immutable-v2-run1` | `17088` | `11482` | `0` | passed; `_initterm` completed; `strcmp` boundary |
| `crt-initterm-final-20260730-immutable-v2-run2` | `4252` | `11482` | `0` | passed; `_initterm` completed; `strcmp` boundary |
| `crt-initterm-final-20260730-immutable-v2-run3` | `22780` | `11482` | `0` | passed; `_initterm` completed; `strcmp` boundary |

Important hashes are:

| Artifact | SHA-256 |
| --- | --- |
| EFI loader | `7FF2C0082E570D4021CA6B63AFA0132222AD46DBCBDEFE7A833AD6C7DEBEA655` |
| NativeAOT payload and source copy | `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` |
| Runtime archive | `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` |
| OVMF code / vars template | `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` / `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E` |
| QEMU executable | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |
| Runner / validator | `D00B998A03E7F9135BD75A6B1A4E451B752844840E43E9EFE65384ECFA4A9D43` / `9E008DC5B6E13F722E50C5FB0F3199968F1453F347E39CF6990F5594A7FC3C66` |

## Gate M: negative controls

The disabled-routing QEMU control is `evidence\generated\crt-initterm-disabled-20260730-v2`; it passed in Disabled mode, emitted no `_initterm` begin or success marker, and stopped at the original `_initterm` import with `24 / 100` imports. Its loader hash is `A6A670D2B2F0A56B07E1A163E7802B212AC04027153E3AFDD6D9DA2C48E9B923`.

The marker-mutation loader hash is `3A04F5B704F1543CEC1CFD8A5D7EBDF109A33C9A92A12C05E1F51788231EB8E`; its `CRT_INITTERM_OX` evidence was rejected. The evidence pipeline `evidence\generated\crt-initterm-negative-controls-20260730-v2` passed the intended rejection/acceptance controls: truncated evidence, missing final diagnostics, stale run ID, duplicate process evidence, manifest hash mismatch, marker mutation, disabled implementation, and runtime marker mutation. Host vectors supplied the empty-range, null, order, duplicate, exclusive-end, reversed-range, target-validation, no-allocation, callback-fault, and ABI controls.

## New deepest boundary and recommendation

The deepest supported path is now:

```text
... -> _initterm_e -> _initterm: complete for the actual nine-entry table
  -> api-ms-win-crt-string-l1-1-0.dll!strcmp
```

The next milestone is the exact `strcmp` dependency reached after `_initterm`. Do not implement it in this pass. The `_initterm` callbacks did not materially advance GC, allocation context, managed-thread registration, or managed allocation, and no general C++/CRT initialization claim is made.

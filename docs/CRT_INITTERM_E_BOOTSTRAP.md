# Windows x64 `_initterm_e` bootstrap contract

Status: CLOSED for the narrow error-returning CRT initializer-table contract exercised by the current NativeAOT allocation/startup artifact. This document does not claim general CRT startup, C++ initializer execution, `_initterm`, teardown, allocation, GC startup, or managed-thread registration.

## Scope and baseline

This pass began on branch `main`, at HEAD `c66dcedb5a15fd832965712e0adb7cff4be74cf5`, upstream `origin/main`, with a clean worktree. The committed SLIST evidence-closure changes were present and were not rewritten. A fresh pre-change QEMU reproduction was retained under `evidence\generated\initterm-e-baseline-20260730`; it stopped at:

```text
GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e
```

The allocation/startup NativeAOT payload is SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. The final positive build and immutable evidence are `artifacts\crt-initterm-e-build-20260730` and `evidence\generated\crt-initterm-e-final-20260730-immutable-v4`.

## Gate A: caller and arguments

The import is exact: `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`, IAT RVA `0x7e380` (`0x18007e380` at the preferred image base). The NativeAOT attach/bootstrap helper begins at preferred `0x180077550`. Its exact call sequence is:

```text
0x1800775a?  lea rdx, [rip+...]  ; end = 0x18007e4d8
0x1800775a?  lea rcx, [rip+...]  ; first = 0x18007e4d0
0x1800775bb  call _initterm_e IAT thunk
0x1800775c0  return site
0x1800775db  call _initterm IAT thunk (next boundary)
```

The static disassembly identifies the caller routine; the runtime marker records the relocated return address `0x00000000054F05C0`. The call uses the Microsoft x64 ABI: the first pointer is in RCX, the second pointer is in RDX, and the integer result is returned in EAX. It occurs once per startup run.

The QEMU image base is `0x0000000005479000`, so the concrete arguments are:

| Value | Preferred address / RVA | Relocated QEMU address |
| --- | ---: | ---: |
| `first` | `0x18007E4D0` / `0x7e4d0` | `0x00000000054F74D0` |
| `last` (exclusive) | `0x18007E4D8` / `0x7e4d8` | `0x00000000054F74D8` |

The range is eight bytes, one pointer-sized entry. It belongs to the loaded NativeAOT image's `.rdata` section (`RVA 0x7e000`, read-only), not writable `.CRT`, `.data`, loader scratch, TLS, stack, or heap memory. The stored eight-byte value is zero. No base relocation targets either table slot; a null entry requires no relocation. The table bounds were captured from the concrete RCX/RDX values, not inferred from symbols.

## Gate B: authoritative contract

Microsoft documents `_initterm_e` alongside `_initterm` in [`_initterm, _initterm_e`](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/initterm-initterm-e?view=msvc-170) and describes the broader initializer ordering in [CRT initialization](https://learn.microsoft.com/en-us/cpp/c-runtime-library/crt-initialization?view=msvc-170). The installed Microsoft-derived UCRT source was also read directly from:

```text
C:\Program Files (x86)\Windows Kits\10\Source\10.0.26100.0\ucrt\startup\initterm.cpp
```

The source contract is:

```cpp
extern "C" int __cdecl _initterm_e(_PIFV* const first, _PIFV* const last)
{
    for (_PIFV* it = first; it != last; ++it)
    {
        if (*it == nullptr)
            continue;

        int const result = (**it)();
        if (result != 0)
            return result;
    }
    return 0;
}
```

The installed UCRT typedef is `_PIFV = int (__cdecl *)(void)`. On Microsoft x64, `__cdecl` uses the unified x64 calling convention; the guideXOS GCC-side declaration explicitly uses `ms_abi` so the callback ABI is unambiguous. The range is `[first,last)`: `last` is exclusive, entries are visited in increasing address order, null entries are skipped, the first nonzero callback result is returned immediately, and zero means that every invoked callback succeeded. An empty range (`first == last`) returns zero. The Microsoft implementation assumes a valid pointer range; it does not define a separate error result for reversed, misaligned, noncanonical, or out-of-image pointers. The guideXOS loader adds controlled validation before entering that loop and rejects malformed input deterministically.

The loop itself performs no heap allocation. Initializers are ordinary callbacks and may themselves allocate or call platform APIs if their own contracts require that; `_initterm_e` neither promises nor prevents those effects. The normal C loop reloads each table slot as it reaches it, so a callback can affect later table values under ordinary C memory rules. Duplicate function pointers are still separate entries and execute once per entry. `_initterm` is different: it uses the void callback type `_PVFV = void (__cdecl *)(void)` and has no error return. This task did not implement `_initterm`, `_initterm_m`, or general `.CRT$X??` processing.

## Gate C: actual table census

| Index | Raw stored value | Relocated target | Classification | Section / symbol | Result |
| ---: | ---: | ---: | --- | --- | --- |
| `0` | `0x0000000000000000` | none | null; skipped | `.rdata`, no target symbol | not invoked |

Totals: one entry, one null entry, zero non-null entries, zero invocations, zero initializer failures. There are no imported APIs, allocation operations, GC operations, thread operations, security-cookie operations, or CRT operations attributable to an invoked initializer because the actual table contains no callback. The expected order is the single slot in increasing address order; the first initializer likely to run is therefore “none” for this artifact. Static `.rdata` inspection and the runtime RCX/RDX trace agree.

The implementation does not synthesize entries and does not execute unknown pointers. If a future artifact supplies a non-null entry, the loader validates its canonical address, image membership, executable-section membership, and relocated-image context before invocation.

## Gate D/E/F: guideXOS contract and implementation

The internal contract is defined in `src/Gate4Harness/crt_initterm_e.h`:

```c
typedef int (GXOS_MS_ABI *GXOS_C_INITIALIZER)(void);

int gxos_crt_initterm_e(
    GXOS_C_INITIALIZER *first,
    GXOS_C_INITIALIZER *last,
    GXOS_CRT_INITTERM_E_REPORT *report,
    GXOS_CRT_INITTERM_E_TRACE trace);
```

The contract is intentionally narrow:

- Microsoft x64 ABI for both the PE-facing wrapper and callbacks.
- Exclusive `last`, forward order, null skip, and immediate exact first-error return.
- Equal pointers accepted as an empty range.
- Canonical, pointer-aligned, image-contained range required.
- Range byte count must be pointer-sized, must not overflow, and is bounded to 4,096 entries.
- The configured image must have relocations applied, and approved executable regions must be inside the loaded image.
- Each non-null target must be canonical, inside the loaded image, and inside an approved executable PE section before invocation.
- No allocation, logging allocation, managed call, thread transition, or general callback registration is performed by the iterator.
- Malformed loader-side ranges and targets return the narrow validation failure (`-1`) and do not invoke a callback; the PE wrapper preserves the CRT callback result for valid execution.

The wrapper routes only the exact DLL and symbol pair through the existing resolver. It emits bounded begin, range, entry-count, callback, result, and final-success markers. `CRT_INITTERM_E_OK` is emitted only after range validation, all entries have been processed, and the result is zero. The implementation does not modify `_initterm` routing.

## Host tests

`tools\Run-CrtInittermEHostTests.ps1` compiles the iterator and focused vectors with warnings-as-errors, runs them, and checks that the iterator object has no unresolved external references. The deterministic suite passed:

```text
CRT_INITTERM_E_TEST_EMPTY_RANGE
CRT_INITTERM_E_TEST_NULL_ENTRIES
CRT_INITTERM_E_TEST_ONE_SUCCESS
CRT_INITTERM_E_TEST_FORWARD_ORDER
CRT_INITTERM_E_TEST_FAILURE_PROPAGATION
CRT_INITTERM_E_TEST_CALLBACK_ABI
CRT_INITTERM_E_TEST_EQUAL_POINTERS
CRT_INITTERM_E_TEST_REVERSED_RANGE
CRT_INITTERM_E_TEST_MISALIGNED_RANGE
CRT_INITTERM_E_TEST_POINTER_OVERFLOW
CRT_INITTERM_E_TEST_NONCANONICAL_TARGET
CRT_INITTERM_E_TEST_OUT_OF_IMAGE
CRT_INITTERM_E_TEST_GUARDS
CRT_INITTERM_E_TEST_UNRELATED_MUTATION
CRT_INITTERM_E_TEST_DUPLICATES
CRT_INITTERM_E_TEST_EXCLUSIVE_END
CRT_INITTERM_E_TEST_NO_ALLOCATION
CRT_INITTERM_E_HOST_TESTS=PASSED
TEST_NO_EXTERNAL_REFERENCES=PASS
```

These vectors cover the requested empty/null/order/failure/ABI/range/target/guard/mutation/duplicate/exclusive-end cases. The deliberately failing callback returns its exact nonzero value, prevents later callbacks, and emits no overall success marker.

## Gate G/H/J: immutable QEMU evidence

The final positive evidence is `evidence\generated\crt-initterm-e-final-20260730-immutable-v4`. It freezes the EFI loader, NativeAOT payload and source copy, runtime archive, OVMF code and vars template, startup script, QEMU executable, runner, validator, and per-run lifecycle/hash data. Important hashes are:

| Artifact | SHA-256 |
| --- | --- |
| EFI loader | `DCC5A21797FDA0F5FB0470EBD51D9A93387436E6E278CDEE587FFA03C2E615C4` |
| NativeAOT payload and source copy | `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` |
| Runtime archive | `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` |
| OVMF code / vars template | `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` / `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E` |
| Runner / validator | `D0157ABA681864810ECF4432C79D415474DE0A7B9B367B6AFB049A5148FC2B98` / `0C4D297A7990A59C46792BC7ACDEAEB7CF2116307900373719D2F930A7770126` |

| Run | PID | Exit | Serial bytes | Imports | `_initterm_e` result | Initializers | Next boundary |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| `crt-initterm-e-final-20260730-immutable-v4-run1` | `13460` | `0` | `4320` | `24 / 100`, unresolved `0` | `0` | `1` entry, `0` invoked, `0` failures | `_initterm` |
| `crt-initterm-e-final-20260730-immutable-v4-run2` | `18140` | `0` | `4320` | `24 / 100`, unresolved `0` | `0` | `1` entry, `0` invoked, `0` failures | `_initterm` |
| `crt-initterm-e-final-20260730-immutable-v4-run3` | `18128` | `0` | `4320` | `24 / 100`, unresolved `0` | `0` | `1` entry, `0` invoked, `0` failures | `_initterm` |

Every counted run contains PE loading, relocation, TLS/GS/TEB/FLS setup, NativeAOT startup, FILETIME, QPC/QPF, both on-exit initializations, SLIST initialization, `_initterm_e` begin/context/range/table/result/success markers, and the final diagnostic summary. The QPC count is `1` with zero regressions. The allocation-context limit/pointer are `0/0`, `ALLOCATION_CONTEXT_VALID=0`, `MANAGED_THREAD_REGISTERED=0`, and no GC-advanced marker is present. The iterator itself performs no allocation and the empty actual table causes no callback side effects.

“Legitimate callback execution” is vacuous for this artifact: the table has no non-null callback. The host suite executes controlled valid callbacks to prove the ABI and order, while QEMU proves that the actual table is not augmented with invented entries.

## Gate K: negative controls

`tools\Test-CrtInittermEEvidencePipeline.ps1` passed all intended controls:

| Control | Expected rejection / result | Outcome |
| --- | --- | --- |
| Disabled implementation | Original `_initterm_e` boundary; no iterator markers or success | passed |
| Marker mutation | Altered success marker rejected | passed |
| Empty table host control | Zero, no callback | passed |
| Null-entry host control | Only valid callback executes | passed |
| Failing initializer | Exact nonzero, later callbacks skipped, no success | passed |
| Reversed range | Deterministic validation rejection | passed |
| Out-of-image callback | Rejected before invocation | passed |
| Noncanonical target | Rejected before invocation | passed |
| Inclusive-end poison | Poison callback not invoked | passed |
| Truncated / missing summary | Evidence validator rejects | passed |
| Hash mismatch | Evidence validator rejects | passed |

The disabled QEMU evidence is `evidence\generated\crt-initterm-e-disabled-20260730-v2`; it used functional `23` / fail-fast `101`, emitted no `_initterm_e` markers, and stopped at the original `_initterm_e` boundary. No commit or push was performed during this pass.

## New deepest boundary and next milestone

The deepest supported startup path is now:

```text
PE loader -> relocations -> TLS / GS / TEB / FLS -> NativeAOT entry
  -> FILETIME -> QPC / QPF -> two CRT on-exit tables
  -> InitializeSListHead -> _initterm_e: validated one-null range, result 0
  -> _initterm: validated nine-entry range, eight callbacks returned
  -> api-ms-win-crt-string-l1-1-0.dll!strcmp: next boundary
```

The `_initterm` boundary described above was subsequently closed as a separate void-initializer contract. The new deepest boundary is `api-ms-win-crt-string-l1-1-0.dll!strcmp`. Any follow-on milestone should preserve the separation between `_initterm_e` and `_initterm`, and should not treat either result as general C++ initialization or evidence of GC or allocation readiness.

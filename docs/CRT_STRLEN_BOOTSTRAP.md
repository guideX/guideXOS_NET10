# Microsoft x64 `strlen` bootstrap contract

Status: CLOSED for the one exact `api-ms-win-crt-string-l1-1-0.dll!strlen` call reached by the current NativeAOT/managed-entry artifact. This document claims only the byte-string length contract required by this path. It does not implement or claim `strnlen`, `strnlen_s`, `wcslen`, multibyte routines, `strcmp` beyond the prior milestone, copying, locale processing, Unicode support, SIMD, allocation, GC readiness, managed-thread registration, or a general CRT string package.

The exact milestone stated before work was:

> This task implements only the Microsoft x64 `strlen` platform contract required by the current NativeAOT startup path.

## Baseline and repository state

The fresh baseline was branch `main`, HEAD `eaddb75a920dfa3abbcba512cfabcf854cc822fd`, upstream `origin/main`, with a clean worktree. HEAD is the committed narrow `strcmp` milestone (`Implement bounded CRT strcmp startup contract`). The prior `strcmp` milestone was confirmed committed; no commit or push was performed in this pass. Reference repositories were not modified.

The fresh disposable baseline capture is `evidence\generated\crt-strlen-baseline-fresh-20260731`. It reached the original fail-fast marker `api-ms-win-crt-string-l1-1-0.dll!strlen` after the proven `strcmp` call. The first baseline runner recorded a cleanup-check defect even though QEMU exited with code `0` and no QEMU process remained; the serial capture itself is retained. The final `strlen` runner uses an explicit process-ID exit wait and its three-run evidence validates cleanly.

## Gate A: exact import and caller investigation

The exact imported identity is:

```text
DLL:     api-ms-win-crt-string-l1-1-0.dll
Symbol:  strlen
IAT RVA: 0x7D3E8
Preferred IAT address: 0x000000018007D3E8
```

The normal import thunk is preferred `0x000000018007737F`. The static call is:

```text
preferred 0x000000018003DB70  bounded string conversion helper entry
preferred 0x000000018003DBA0  call strlen thunk
preferred 0x000000018003DBA5  return address
```

For the counted positive artifact, `IMAGE_BASE=0x000000000547B000`, so the runtime IAT address is `0x00000000054F83E8`, the runtime thunk is `0x00000000054F237F`, the runtime call is `0x00000000054B8BA0`, and the runtime return address is `0x00000000054B8BA5`.

The bounded call chain proven by disassembly and the serial sequence is:

```text
guideXOS PE loader
  -> relocated NativeAOT payload
  -> exported managed-entry transition
  -> GC-configuration classifier
  -> strcmp("gcServer", "gcConservative") = +1
  -> subsequent configuration string conversion helper
  -> strlen("gcServer")
  -> KERNEL32.dll!GetEnvironmentVariableW fail-fast boundary
```

The current harness serial sequence records `NATIVEAOT_STARTUP_RETURN=1` and `NATIVEAOT_STARTUP_OK` before `BEFORE_MANAGED_CALL`; the `strcmp` and `strlen` calls are then reached during the managed-entry call in the same staged NativeAOT artifact. This is recorded as observed ordering, not a claim that the earlier loader-side attach entry itself contains the `strlen` call.

The Microsoft x64 ABI is preserved: the string pointer is in `RCX`, and the `size_t` result is returned in `RAX`. The positive runtime census has exactly one live `strlen` call per process. It is sequential, not nested, and no second call occurs before the next boundary.

| Item | Observed value |
| --- | --- |
| Input pointer | `0x0000000005513498` in each positive run |
| Equal prior `strcmp` operand | `strcmp` LHS pointer, yes |
| Preferred input RVA | `0x98498` |
| Storage | loaded image `.rdata` |
| Runtime region | `0x00000000054F8000` through `0x0000000005524E00` |
| Region permissions | readable `1`, executable `0`, writable `0` |
| Relocations | applied `1` |
| Bytes | `67 63 53 65 72 76 65 72` / `gcServer` |
| High-bit bytes | none |
| Embedded null | none before the terminator |
| Terminator | `0x00000000055134A0` |
| Observed length | `8` |
| Bytes examined | 9, including the terminating null |
| Configured hard maximum | `0x10000` bytes examined |

The actual string is immutable `.rdata` storage for this call. The input is ASCII-compatible, but the implementation treats it only as bytes.

## Gate B: actual NativeAOT purpose

The return value is consumed in the helper beginning at preferred `0x000000018003DB70`:

```text
0x18003DBA0  call strlen
0x18003DBA5  xor  r9d,r9d
0x18003DBA8  mov  r8,rax
0x18003DBAE  test rax,rax
0x18003DBB1  je   empty-path
0x18003DBB7  cmp  rax,0x4
0x18003DBC9  add  rdx,rax
0x18003DBCC  lea  r10,[r10+rax*2]
0x18003DBE2  cmp  r8,0x20
0x18003DCA0  cmp  rcx,r8
0x18003DCB0  scalar byte-to-16-bit copy for the tail
0x18003DCC3  store a terminating 16-bit zero at destination + length*2
```

The length is not truncated to 32 bits. Zero has a special branch. The observed length is used for empty/short checks, source/destination bounds and pointer arithmetic, and a byte-to-UTF-16 conversion into caller-provided storage. The observed code does not use this `strlen` result as an allocation size, add it to an allocation request, or request a copy from guideXOS. No overflow check is attributable to the `strlen` result itself in the bounded call-site slice; the guideXOS implementation therefore performs its own checked accounting and hard bound. The call remains in the pre-GC configuration/managed-entry path: no GC heap, allocation context, managed-thread registration, or managed allocation became observable.

## Gate C: Microsoft contract

Microsoft documents the declaration as:

```c
size_t strlen(const char *str);
```

The authoritative [`strlen` Microsoft CRT reference](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/strlen-wcslen-mbslen-mbslen-l-mbstrlen-mbstrlen-l?view=msvc-170) specifies that the return value is the number of characters in the string excluding the terminal null, and that `strlen` treats the input as a single-byte character string, so its result is the number of bytes. Therefore:

* an empty string returns `0`;
* a one-character string returns `1`;
* an embedded `0x00` terminates the observed value and is excluded;
* every nonzero byte, including a byte above `0x7F`, contributes one;
* no locale conversion, multibyte decoding, or Unicode interpretation is performed by `strlen`;
* the terminating null is not counted;
* the function has no allocation or output-buffer behavior and does not modify the input;
* a valid pointer to a null-terminated byte string is part of the contract; a null pointer, an unterminated sequence, or an unreadable sequence is outside the contract and must not be treated as a standard error result;
* the standard contract supplies no maximum scan bound and no memory-safety guarantee for an invalid or unterminated pointer.

The Microsoft x64 ABI uses `RCX` for the first integer/pointer argument and `RAX` for a scalar result that fits in 64 bits, as specified by the official [x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170). The installed Microsoft UCRT source at `C:\Program Files (x86)\Windows Kits\10\Source\10.0.26100.0\ucrt\string\amd64\strlen.asm` documents the same null-terminated byte count and implements an optimized word-at-a-time scan. This guideXOS milestone deliberately uses a scalar byte loop because its controlled readability contract does not permit speculative reads.

Microsoft also lists `strlen` among functions with intrinsic forms in the [`intrinsic` pragma documentation](https://learn.microsoft.com/en-us/cpp/preprocessor/intrinsic?view=msvc-170). An intrinsic or inlined implementation may remove a conventional call, but it does not change the C contract. This artifact has an actual import, so the exact import resolver route remains required here.

## Gate D: guideXOS checked contract

The internal layer is:

```c
typedef enum {
    GXOS_CRT_STRLEN_STATUS_OK = 0,
    GXOS_CRT_STRLEN_STATUS_NULL_POINTER,
    GXOS_CRT_STRLEN_STATUS_NONCANONICAL_POINTER,
    GXOS_CRT_STRLEN_STATUS_UNREADABLE_POINTER,
    GXOS_CRT_STRLEN_STATUS_UNTERMINATED,
    GXOS_CRT_STRLEN_STATUS_OVERFLOW,
    GXOS_CRT_STRLEN_STATUS_INVALID_CONTEXT,
    GXOS_CRT_STRLEN_STATUS_INVALID_OUTPUT
} GXOS_CRT_STRLEN_STATUS;

GXOS_CRT_STRLEN_STATUS gxos_crt_strlen_checked(
    const char *string,
    const GXOS_READABLE_IMAGE *image,
    size_t maximum_scan,
    size_t *length_out);
```

The checked function preserves `*length_out` on failure. It requires a bounded loaded-image context with canonical image/region ranges, an approved readable-region list, and `relocations_applied != 0`. It allows an explicitly approved readable non-image region for host validation but does not probe arbitrary virtual memory. The PE-facing `platform_strlen` wrapper has the exact one-argument Microsoft x64 shape `size_t platform_strlen(const char *string)` with `RCX` input and `RAX` result; it calls the checked layer with the configured image context and `GXOS_CRT_STRLEN_DEFAULT_MAX_SCAN` (`0x10000`).

The checked layer validates the initial pointer, canonical form, context, image relocation state, each byte's containing readable region, the maximum scan, and integer address arithmetic. It reads one byte at a time, stops only on `0x00`, and detects an absent terminator as `UNTERMINATED`. A missing region, non-readable region, or relocation/context failure is a deterministic checked failure. The PE wrapper emits the status and fails visibly with `FAIL:crt-strlen-invalid`; it never silently returns zero for invalid input. This wrapper assumes the current one-thread harness for diagnostic counters and serial output. The checked core has no allocation, recursion, shared context mutation, or input mutation.

## Gates E-I: implementation, routing, and diagnostics

The implementation is only `src/Gate4Harness/crt_strlen.c`, an auditable byte loop. It has no SIMD, no word-sized read, no speculative over-read, no locale, no Unicode handling, no external CRT call, no heap allocation, and no mutation. The exact route added to `platform_import_target` is only:

```text
api-ms-win-crt-string-l1-1-0.dll!strlen
```

The import census changed as follows:

| Profile | Functional | Fail-fast | Unresolved required |
| --- | ---: | ---: | ---: |
| `CrtStrlenDisabled` | 26 | 98 | 0 |
| `CrtStrlen` | 27 | 97 | 0 |

The disabled three-run control stopped at the original `strlen` import and emitted no `CRT_STRLEN_BEGIN` or success marker. Existing `strcmp`, `_initterm`, `_initterm_e`, SLIST, time, QPC, allocation-context, managed-thread, and GC-state behavior was preserved.

The enabled wrapper emits bounded fields for entry, call index, caller/return address, pointer, region bounds and permissions, relocation state, maximum scan, status, byte preview, escaped text preview, exact length, terminator, return value, returned marker, and success marker. No unbounded input is printed.

## Gate J: focused host tests

`tools\Run-CrtStrlenHostTests.ps1` passed. The vectors cover empty, one-character, ordinary ASCII, terminator exclusion, embedded null (`a`, `b`, `0`, `c`, `0` -> `2`), high-bit bytes, equal contents in different buffers, long strings, the maximum permitted terminated string, null/noncanonical/out-of-region/unreadable/unterminated/gap/overflow rejection, a terminator at the final readable byte, unchanged guards and input, no allocation, no external references, and the Microsoft x64 function-pointer ABI. The standalone core object has no undefined external symbols. The prior `strcmp`, `_initterm`, and `_initterm_e` host suites also passed unchanged.

The core object disassembly contains only scalar byte loads for the scan; it contains no wide or vectorized speculative read. The checked layer validates the region before every byte load.

## Gate K: negative controls

Passed controls are retained in `evidence\generated\crt-strlen-negative-controls-20260731-final-v2`:

* disabled routing rejected the implementation and stayed at `strlen`;
* off-by-one, early-termination, and forced-zero result mutations were rejected by host result checks;
* embedded-null and high-bit controls returned `2` and one count per byte;
* null, noncanonical, unreadable, unterminated, boundary, gap, and overflow controls failed for the intended checked status;
* marker mutation, truncated success evidence, stale run ID, duplicate QEMU PID, and artifact hash mutation were each rejected by the evidence validator for the intended reason.

## Gate H/L/M: immutable runtime evidence

The final positive immutable evidence is `evidence\generated\crt-strlen-final-20260731-immutable-v3`. One immutable artifact set was used for three fresh QEMU processes, each with a fresh OVMF vars file, unique PID, complete serial/stdout/stderr/lifecycle records, and exit code `0` after controlled cleanup.

| Run | PID | Serial bytes | `strlen` calls / returns | Length / total / longest | QPC first -> last | Next boundary |
| --- | ---: | ---: | ---: | --- | --- | --- |
| `crt-strlen-final-20260731-immutable-v3-run1` | 26236 | 13704 | 1 / 1 | `8 / 8 / 8` | `0x1D37F -> 0x39F0A`, delta `0x1CB8B` | `KERNEL32.dll!GetEnvironmentVariableW` |
| `crt-strlen-final-20260731-immutable-v3-run2` | 20464 | 13704 | 1 / 1 | `8 / 8 / 8` | `0x1D412 -> 0x38C70`, delta `0x1B85E` | `KERNEL32.dll!GetEnvironmentVariableW` |
| `crt-strlen-final-20260731-immutable-v3-run3` | 23172 | 13704 | 1 / 1 | `8 / 8 / 8` | `0x1E0F9 -> 0x39D33`, delta `0x1BC3A` | `KERNEL32.dll!GetEnvironmentVariableW` |

All enabled runs recorded caller `0x00000000054B8BA5`, input `0x0000000005513498`, read-only `.rdata` region `0x54F8000..0x5524E00`, bytes `gcServer`, length and return `8`, and the same prior `strcmp` LHS pointer. Each had QPC count `2`, zero QPC regressions, zero TLS allocation pointer/limit, zero GC contract/heap state, zero allocation context, zero managed-thread registration, and zero managed allocations. No processor fault, hang, or triple fault occurred. The next dependency is an environment API, not another CRT string routine.

The final positive artifact hashes are recorded in `artifact-manifest.json`:

```text
EFI loader:       B0FA9D7587D73154DF52F769205B6F4B632698ECF90CDFC246BBA4257023B191
NativeAOT payload:2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837
Runtime archive:  DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311
QEMU:             A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02
```

The disabled immutable control is `evidence\generated\crt-strlen-disabled-20260731-immutable`; all three runs used 26/98 imports, no `CRT_STRLEN_*` implementation marker, and stopped at `api-ms-win-crt-string-l1-1-0.dll!strlen`.

## Runtime-state conclusion and next milestone

`strlen` materially advanced the deepest import boundary but did not materially advance GC. It did not make a GC heap usable, create or validate an allocation context, register a managed thread, or perform a managed allocation. It caused no new string comparison, copy import, allocation request, environment call, or processor exception before the next boundary; the next boundary itself is the first environment-access request `KERNEL32.dll!GetEnvironmentVariableW`.

Files changed for this milestone are:

```text
src/Gate4Harness/crt_strlen.c
src/Gate4Harness/crt_strlen.h
src/Gate4Harness/tests/crt_strlen_tests.c
src/Gate4Harness/gate4_loader.c
tools/Build-Gate4Harness.ps1
tools/Run-CrtStrlenHostTests.ps1
tools/Run-CrtStrlenFinalValidation.ps1
tools/Validate-CrtStrlenEvidence.ps1
tools/Test-CrtStrlenEvidencePipeline.ps1
README.md
docs/CRT_STRLEN_BOOTSTRAP.md
docs/DEPENDENCY_CENSUS.md
docs/EVIDENCE_LEDGER.md
docs/FIRST_MANAGED_ALLOCATION.md
docs/NATIVEAOT_ARTIFACT_ANATOMY.md
docs/NEXT_STAGE_BLOCKERS.md
docs/CRT_STRCMP_BOOTSTRAP.md
```

The recommended next milestone is the exact `KERNEL32.dll!GetEnvironmentVariableW` contract reached after `strlen`. Do not implement it in this pass, and do not infer environment, GC, allocation-context, thread, or managed-allocation readiness from the completed `strlen` contract.

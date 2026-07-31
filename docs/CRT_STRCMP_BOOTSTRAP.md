# Microsoft x64 `strcmp` bootstrap contract

Status: CLOSED for the one exact `api-ms-win-crt-string-l1-1-0.dll!strcmp` call reached by the current NativeAOT startup artifact. This milestone does not implement or claim `strncmp`, `_stricmp`, `wcscmp`, `memcmp`, `strcmpi`, `strlen`, locale processing, Unicode conversion, UTF processing, security variants, vectorization, SIMD, allocation, GC, managed-thread registration, or general string support.

The required milestone was to implement only Microsoft x64 `strcmp`, prove the actual caller and arguments, run the host vectors, route only the exact import, and stop at the next authentic dependency. The next dependency is `api-ms-win-crt-string-l1-1-0.dll!strlen`; it remains fail-fast and intentionally unimplemented.

## Baseline and repository state

The pre-change baseline was branch `main`, HEAD `1692ccdca007edc5bb5f9365513bc9bceaa6ae99`, upstream `origin/main`, with a clean worktree. HEAD is the committed narrow `_initterm` milestone. No duplicate `strcmp` implementation existed: the dependency census and fail-fast resolver contained the import, but no `strcmp` implementation or route.

The implementation was added without committing or pushing. The final worktree is intentionally uncommitted so the user can review it.

## Gate A: exact caller and bounded runtime diagnostics

The exact import is `api-ms-win-crt-string-l1-1-0.dll!strcmp`, IAT RVA `0x7d3c8`, preferred IAT address `0x18007d3c8`. Its normal import thunk is preferred `0x1800774d1`. The live call is in the NativeAOT GC-configuration classification helper:

```text
preferred 0x18003EB00  helper entry
preferred 0x18003EB15  load literal/config state
preferred 0x18003EB1F  call strcmp thunk
preferred 0x18003EB24  return site
```

The helper is reached from the surrounding GC configuration initializer at preferred `0x180044E10`, whose call is at `0x180044E29`. The direct runtime return marker is `0x00000000054B7B24`, which relocates to preferred `0x18003EB24` for the counted image base `0x0000000005479000`. The call uses the Microsoft x64 ABI: RCX is the first pointer, RDX is the second pointer, and the integer result is returned in EAX.

The final enabled runs emitted the following bounded diagnostics on every run:

```text
CRT_STRCMP_CALL_COUNT=0x0000000000000001
CRT_STRCMP_CALLER=0x00000000054B7B24
CRT_STRCMP_LHS_POINTER=0x0000000005512908
CRT_STRCMP_RHS_POINTER=0x00000000055112E0
CRT_STRCMP_LHS_LENGTH=0x0000000000000008
CRT_STRCMP_RHS_LENGTH=0x000000000000000E
CRT_STRCMP_LHS_NULL_TERMINATED=1
CRT_STRCMP_RHS_NULL_TERMINATED=1
CRT_STRCMP_LHS_BYTES=6763536572766572
CRT_STRCMP_RHS_BYTES=6763436F6E736572766174697665
CRT_STRCMP_LHS_TEXT=gcServer
CRT_STRCMP_RHS_TEXT=gcConservative
CRT_STRCMP_RESULT=0x0000000000000001
```

Both pointers are inside the loaded image's read-only `.rdata` section. Their preferred addresses are `0x180099908` (`gcServer`) and `0x1800982E0` (`gcConservative`); neither storage is mutable during this call. The strings are ASCII-only byte strings and therefore also valid UTF-8 byte sequences, but the call is an ordinal byte comparison and does not interpret UTF-8. The maximum observed content length is 14 bytes, excluding the terminating null. The actual startup path calls `strcmp` once per process run. The payload has other static potential `strcmp` call sites, but none is reached before the next boundary in this path.

## Gate B: Microsoft CRT contract

Microsoft's authoritative [`strcmp`, `wcscmp`, `_mbscmp`, `_mbscmp_l` documentation](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/strcmp-wcscmp-mbscmp?view=msvc-170) specifies:

```c
int strcmp(const char *string1, const char *string2);
```

The parameters are valid null-terminated strings. `strcmp` performs an ordinal, case-sensitive comparison and returns a value whose sign describes the relationship: negative when the first string is less, zero when the strings are identical, and positive when the first string is greater. The documentation explicitly distinguishes this from locale-sensitive `strcoll`; `strcmp` is locale-independent. It also states that `strcmp` does not validate null pointers, so null pointer arguments are outside the valid contract and are not converted into a special result.

The Microsoft UCRT x64 source at `C:\Program Files (x86)\Windows Kits\10\Source\10.0.26100.0\ucrt\string\amd64\strcmp.asm` defines the byte comparison as unsigned, with the source algorithm using `unsigned char`, treating null (`0`) as less than every nonzero byte (`1` through `255`), and returning normalized `-1`, `0`, or `1`. The implementation therefore has these exact observable rules:

- Compare bytes from left to right, without locale, case folding, multibyte decoding, or Unicode interpretation.
- Interpret every byte as `unsigned char`; a high-bit byte such as `0x80` is greater than `0x7F`, not less because plain `char` is signed.
- Stop at the first differing byte and return a negative, zero, or positive result according to that unsigned-byte ordering.
- If equal bytes reach `0x00` in both strings, return zero. Empty strings are equal.
- If one string ends at `0x00` while the other has a nonzero byte, the shorter prefix is less.
- Identical pointers compare equal because the same null-terminated sequence is read.
- On Microsoft x64, the first argument is in RCX, the second in RDX, and the `int` result is in EAX. The x64 ABI uses the unified Microsoft calling convention for the C entry point.
- The routine allocates no memory, writes neither input, and has no locale or global-state dependency.

The implementation deliberately follows the source's normalized sign result. It does not promise an undocumented exact magnitude beyond `-1`, `0`, or `1` to callers that only require the documented sign.

## Gate C: actual NativeAOT use

The actual comparison is a GC configuration-name classification. The surrounding NativeAOT data contains adjacent GC configuration names and boolean output slots; the helper compares the name `gcServer` with the classification token `gcConservative`. The observed result is positive (`+1`) because the first differing byte is `0x53` (`S`) versus `0x43` (`C`). The nonzero result takes the helper's fallback configuration path at preferred `0x18003EB3D`, which calls the next internal parser at preferred `0x18003DE70`.

This is runtime option/configuration selection. It is not a module lookup, environment API call, locale operation, Unicode conversion, or invariant check. The comparison occurs after the proven `_initterm` completion and before the next import boundary. The NativeAOT entry returns `1` after the comparison and the loader records `NATIVEAOT_STARTUP_OK`; the next unresolved import is then `strlen`.

## Gate D: narrow implementation

The core is `src/Gate4Harness/crt_strcmp.c` with its ABI declaration in `src/Gate4Harness/crt_strcmp.h`:

```c
int GXOS_CRT_STRCMP_MS_ABI gxos_crt_strcmp(const char *lhs, const char *rhs)
{
    for (;;) {
        unsigned char left = (unsigned char)*lhs;
        unsigned char right = (unsigned char)*rhs;
        if (left != right) return left < right ? -1 : 1;
        if (left == 0) return 0;
        lhs++;
        rhs++;
    }
}
```

It is allocation-free, recursion-free, byte-wise, null-terminated, deterministic, locale-independent, scalar-only, and has no CRT or platform dependencies. The loader wrapper adds only bounded serial diagnostics and routes only the exact DLL/symbol pair.

## Gate E: host tests and negative controls

`tools\Run-CrtStrcmpHostTests.ps1` passed:

| Vector | Result |
| --- | --- |
| Equal strings and identical pointers | pass |
| Unequal strings, differing first byte, differing final byte | pass |
| Empty strings and one-character strings | pass |
| Both prefix directions | pass |
| Embedded high-bit bytes with unsigned ordering | pass |
| Long strings | pass |
| Embedded bytes after the terminating null | pass |
| Core object has no unresolved external references | `CRT_STRCMP_TEST_NO_EXTERNAL_REFERENCES=PASS` |
| Complete host suite | `CRT_STRCMP_HOST_TESTS=PASSED` |

The negative controls intentionally modeled and rejected a mutated/inverted comparison, signed-byte comparison, incorrect prefix handling, a two-byte truncated comparison, and forced equality. They are in `src/Gate4Harness/tests/crt_strcmp_tests.c` and are reported as part of the passing host suite.

## Gate F: exact import routing

Only this resolver pair was made functional:

```text
api-ms-win-crt-string-l1-1-0.dll!strcmp
```

The import census transition is:

| Profile | Functional | Fail-fast | Unresolved |
| --- | ---: | ---: | ---: |
| Proven `_initterm` baseline | 25 | 99 | 0 |
| `strcmp` enabled | 26 | 98 | 0 |

The disabled-routing control remained at 25/99 and stopped at `strcmp`, with no `CRT_STRCMP_*` markers. No `strlen` or other string API was routed.

## Gate G: three immutable QEMU runs

The final frozen artifact manifest is `evidence\generated\crt-strcmp-final-20260730-immutable\artifact-manifest.json`. The enabled loader hash is `585EDDB8D7A16F1CE98B881E4BCD124C0C4B6E001AA3BDA89FB2DF9D99229AEC`; the NativeAOT payload/source hash is `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`; the runtime archive hash is `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`; the QEMU hash is `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`. All three processes used identical artifact hashes, fresh OVMF variables, unique PIDs, and exit code zero.

| Run | PID | Serial bytes | Compared strings / result | Imports | QPC summary | Next boundary |
| --- | ---: | ---: | --- | --- | --- | --- |
| `crt-strcmp-final-20260730-immutable-run1` | 23404 | 12137 | `gcServer` / `gcConservative` / `+1` | 26 / 98 / unresolved 0 | count 2, first `0x278D9`, last `0x44BBF`, min/max delta `0x1D2E6` / `0x1D2E6`, regressions 0 | `strlen` |
| `crt-strcmp-final-20260730-immutable-run2` | 2376 | 12137 | `gcServer` / `gcConservative` / `+1` | 26 / 98 / unresolved 0 | count 2, first `0x1F417`, last `0x3C8F3`, min/max delta `0x1D4DC` / `0x1D4DC`, regressions 0 | `strlen` |
| `crt-strcmp-final-20260730-immutable-run3` | 21892 | 12137 | `gcServer` / `gcConservative` / `+1` | 26 / 98 / unresolved 0 | count 2, first `0x1D07B`, last `0x39A24`, min/max delta `0x1C9A9` / `0x1C9A9`, regressions 0 | `strlen` |

Every enabled run also recorded `NATIVEAOT_STARTUP_RETURN=1`, TLS allocation limit/pointer `0/0`, `MANAGED_THREAD_REGISTERED=0`, `ALLOCATION_CONTEXT_VALID=0`, `GC_CONTRACT_INITIALIZED=0`, `GC_HEAP_USABLE=0`, `ALLOCATION_CONTEXT_CREATED=0`, and `MANAGED_ALLOCATION_COUNT=0`. The final boundary was deliberately left fail-fast; `strlen` was not implemented.

The disabled control is `evidence\generated\crt-strcmp-disabled-20260730`, PID `19500`, exit `0`, 11482 serial bytes, 25/99 imports, and the original `api-ms-win-crt-string-l1-1-0.dll!strcmp` boundary. Its validator passed and it emitted no `CRT_STRCMP_CALL_COUNT` marker.

## Startup chain and new deepest boundary

```text
PE loader
  -> relocations
  -> TLS / GS / TEB / FLS
  -> NativeAOT entry
  -> GetSystemTimeAsFileTime
  -> QueryPerformanceCounter / QueryPerformanceFrequency
  -> _initialize_onexit_table
  -> InitializeSListHead
  -> _initterm_e
  -> _initterm
  -> strcmp("gcServer", "gcConservative") = +1
  -> api-ms-win-crt-string-l1-1-0.dll!strlen
```

This closes exactly one new CRT dependency and advances startup to exactly one new authentic dependency. `strlen` is the next milestone and is intentionally outside this task.

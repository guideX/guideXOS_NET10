# Microsoft x64 `_stricmp` bootstrap contract

Status: closed for the exact Microsoft x64 `_stricmp` dependency reached by the current NativeAOT startup artifact. This is a bounded guideXOS contract, not a general CRT, locale, Unicode, or environment implementation.

This task implements only the Microsoft x64 `_stricmp` contract required by the current NativeAOT startup path.

## Baseline and scope

The preceding `GetEnvironmentVariableW` milestone was committed at the start of this pass. A fresh disposable pre-change run under `evidence\generated\stricmp-baseline-20260731-v2` reproduced the fail-fast boundary before any `_stricmp` route was enabled:

```text
... -> GetEnvironmentVariableW("DOTNET_gcServer") = missing
    -> api-ms-win-crt-string-l1-1-0.dll!_stricmp
    -> GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-string-l1-1-0.dll!_stricmp
```

The baseline used a fresh QEMU process (PID `15880`), runtime image base `0x0000000005479000`, the shared NativeAOT payload SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, and loader SHA-256 `EB4B72ECDD92A3F01B2B4F5091AE8F347A8A9E9C774E5DB1836BC315A4F10809`. The old evidence was preserved; no reference repository was modified.

Only `api-ms-win-crt-string-l1-1-0.dll!_stricmp` is in scope. This pass does not add `_strnicmp`, `_wcsicmp`, `_mbsicmp`, locale-explicit variants, `setlocale`, Unicode collation, environment storage, GC initialization, allocation, or the next `GetSystemInfo` import.

## Gate A: exact import and live call sites

Static PE inspection of the staged NativeAOT payload records:

| Item | Value |
| --- | --- |
| import descriptor table | preferred `0x1800aa280` |
| import lookup table | preferred `0x1800aa6c0` |
| first thunk / IAT | preferred `0x18007e3c8` |
| `_stricmp` IAT slot | RVA `0x7e3e0`, preferred `0x18007e3e0` |
| normal import thunk | preferred `0x1800774cb` |
| import descriptor | `api-ms-win-crt-string-l1-1-0.dll` |
| symbol | `_stricmp` |

The enabled PE census is exactly `29` functional / `95` deterministic fail-fast / `0` unresolved. The disabled control is `28` / `96` / `0`; its `_stricmp` slot remains the intentional fail-fast boundary. No unrelated CRT alias is routed.

Runtime return addresses map to two executed direct call sites in the preferred payload:

| Runtime return | Preferred return | Direct call | Nearest identifiable helper | Caller consumption |
| --- | --- | --- | --- | --- |
| `0x00000000054B6F70` | `0x18003df70` | `0x18003df6b` | starts `0x18003df30` | `test eax,eax`; `je` |
| `0x00000000054B70B0` | `0x18003e0b0` | `0x18003e0ab` | starts `0x18003e07a` | `test eax,eax`; `je` |

The first helper accounted for 149 observed calls and the second for 736. The caller consumes only equality/sign through the zero test; it does not consume an exact magnitude. The wrapper preserves the Microsoft result magnitude nevertheless, and the host suite verifies sign, equality, and prefix behavior.

## Gate B: operand, purpose, and data-flow census

The live sequence is:

```text
NativeAOT startup/configuration helper
  -> bounded CRT and environment setup
  -> helper 0x18003df30 or 0x18003e07a
  -> RCX/RDX operands
  -> call 0x1800774cb (_stricmp thunk)
  -> EAX test / conditional branch
  -> configuration/name-table loop
  -> KERNEL32.dll!GetSystemInfo
```

The observed purpose is a case-insensitive configuration/name matcher. The first call compared `System.GC.Server` with `Microsoft.Extensions.DependencyInjection.VerifyOpenGenericService...`, returned arithmetic result `+6`, and branched on `EAX != 0`. Two calls returned equality for identical `System.GC.*` strings. The operands were not environment values: all 885 calls used image-backed read-only regions, and none of their pointers matched the `GetEnvironmentVariableW` buffer at `0x0000000007E64B10`. The missing `DOTNET_gcServer` result remained zero and was not parsed into these operands.

Across the positive probe, the bounded runtime census recorded 885 successful calls, 29 distinct first-string previews, 32 distinct second-string previews, 2 equal results, 566 less-than results, and 317 greater-than results. Results ranged from `-16` to `+18`; the caller used only sign/zero. The core examined `0x362A` bytes in total and had a longest compared prefix of `0x15` bytes. Each trace emits both operands as bounded escaped bytes/text, lengths, terminator addresses, approved-region bounds, permissions, status, compared prefix, result, and post-return marker.

The runtime strings were in the relocated `.rdata` region `0x00000000054F7000..0x0000000005524200`, with `readable=1`, `executable=0`, and `writable=0`. Observed operand lengths were 16–34 bytes for string 1 and 16–102 bytes for string 2. The trace diagnostics scan each operand separately to record its terminator; that bounded census is distinct from the core comparison loop, which stops at the first decisive byte or the shared terminator.

## Gate C: Microsoft contract

The authoritative Microsoft reference is [`_stricmp, _wcsicmp, _mbsicmp`](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/stricmp-wcsicmp-mbsicmp-stricmp-l-wcsicmp-l-mbsicmp-l?view=msvc-170). The relevant contract is:

- signature: `int _stricmp(const char *string1, const char *string2)`;
- both inputs are null-terminated byte strings;
- the result is negative, zero, or positive when the first string is less than, equal to, or greater than the second after case-insensitive comparison;
- ASCII `A`–`Z` are compared case-insensitively; punctuation remains in ASCII order, so punctuation between `Z` and `a` is not folded into a letter;
- when no locale has been set, the C locale is used; the locale-explicit `_stricmp_l` variant is a separate contract;
- null arguments invoke Microsoft's invalid-parameter behavior rather than defining a normal comparison result.

Microsoft documents the caller-facing sign/equality contract, not a portable exact magnitude requirement. The maintained UCRT implementation used as a source-level cross-check applies ASCII folding to unsigned bytes in its no-locale-change path and returns the folded-byte arithmetic difference. The checked guideXOS route uses the same deterministic arithmetic difference while treating the caller's observed sign/zero use as the compatibility requirement.

For Microsoft x64, the two pointers are passed in `RCX` and `RDX`, and the `int` result is returned in `EAX`; the route declaration uses the x64 Microsoft ABI attribute. The implementation does not mutate either input, allocate, call another import, or depend on ambient locale state.

No locale initialization, `setlocale`, locale-explicit CRT import, or locale mutation was observed in the startup census. The route therefore exposes only the default C-locale behavior and emits `C_DEFAULT_NO_LOCALE_CHANGE`. It does not claim support for a later active-locale configuration.

## Gate D: checked guideXOS contract

The core is `src/Gate4Harness/crt_stricmp.c`, declared in `src/Gate4Harness/crt_stricmp.h`, and is linked only when `GXOS_ENABLE_CRT_STRICMP` is selected. It validates:

- non-null canonical x64 pointers;
- a valid relocated image context with bounded approved memory regions and relocations applied;
- readable-region membership for every byte before reading it;
- a positive per-string maximum scan (`0x10000` in the runtime route);
- overflow-safe pointer addition and null termination within the bound;
- default-C ASCII folding only: `A`–`Z` map to `a`–`z`, while punctuation, digits, NUL, and bytes `0x80`–`0xFF` remain unchanged;
- deterministic checked failures for null, noncanonical, unreadable, unterminated, scan-limit, pointer-overflow, invalid-context, and invalid-output cases.

The checked layer rejects null or unreadable pointers deterministically for this freestanding profile. This is an intentional safety boundary around Microsoft's documented invalid-parameter/undefined-input edge, not a claim that the Windows CRT defines a normal result for invalid memory. It performs no allocation, diagnostic call, external reference, mutation, SIMD over-read, or locale lookup.

## Gate E: host tests and negative controls

`tools\Run-CrtStricmpHostTests.ps1` passed the focused C suite and verified that the freestanding core object has no undefined external references. The vectors cover ordinary equality/order, empty strings, prefixes, identical and separate buffers, ASCII `A/a` and `Z/z`, punctuation, digits, high-bit unsigned ordering, embedded NUL, long and maximum-bound strings, decisive-byte guard boundaries, terminator canaries, input preservation, invalid pointers, unreadable regions, unterminated strings, scan limits, pointer overflow, and Microsoft x64 ABI shape.

The suite also executes intended-failure mutation models and rejects case-sensitive comparison, overbroad folding, forced equality, reversed sign, and incorrect prefix handling. `tools\Test-CrtStricmpEvidencePipeline.ps1` rejects disabled-route substitution, marker mutation, stale evidence IDs, duplicate QEMU PIDs, and artifact hash mutation.

## Gates F and I: immutable runtime evidence

The final positive evidence is under `evidence\generated\crt-stricmp-final-20260731-immutable-v4`; it contains three consecutive fresh QEMU runs with fresh OVMF variable copies, unique QEMU PIDs, identical artifact snapshots, complete serial logs, clean cleanup, and the exact next boundary `KERNEL32.dll!GetSystemInfo`. The disabled three-run control is under `evidence\generated\crt-stricmp-disabled-20260731-v3`; it retains the `_stricmp` fail-fast boundary and emits no checked `_stricmp` success marker.

Every positive run records `29 / 95 / 0` imports, 885 successful `_stricmp` calls, zero failures, `QPC_COUNT=2`, `QPC_REGRESSIONS=0`, and zero TLS allocation context, managed-thread registration, GC initialization, GC heap usability, and managed allocation. The final summary is not a claim that `GetSystemInfo` works: the harness stops immediately at that authentic unresolved dependency.

| Run | PID | Serial bytes | Boundary |
| --- | ---: | ---: | --- |
| `crt-stricmp-final-20260731-immutable-v4-run1` | 25028 | 2,113,224 | `KERNEL32.dll!GetSystemInfo` |
| `crt-stricmp-final-20260731-immutable-v4-run2` | 24284 | 2,113,224 | `KERNEL32.dll!GetSystemInfo` |
| `crt-stricmp-final-20260731-immutable-v4-run3` | 26960 | 2,113,224 | `KERNEL32.dll!GetSystemInfo` |

The immutable positive snapshot records these execution hashes:

```text
EFI loader:       64E46560A00EA3C04F59E1BBC239991262D02AB6F2E486992ADBC1CD3B470DBC
NativeAOT image:  6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379
Runtime archive:  DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311
OVMF code:        33090CC07675BA519D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A
OVMF vars:        5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E
QEMU:             A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02
Runner:           03BCDA7D2D835C3CA2F7C724D6D5E206BFA6B6DA10EF6971F0D1DBBF8C70A6D1
Validator:        9F481DEEFFEC827BF7238534F96F5E7CEA68B4D7957FF57836491BAB3E7F84C2
```

The disabled control runs were `13764`, `25892`, and `27332`, each with 15,500 serial bytes and the `_stricmp` fail-fast boundary. The negative-control bundle is `evidence\generated\crt-stricmp-negative-controls-20260731-v2`; all intended-rejection cases passed.

## Startup chain and next boundary

```text
PE loader
  -> relocations / TLS / one-thread NativeAOT state
  -> FILETIME / QPC / QPF
  -> CRT on-exit tables / SLIST head / _initterm_e / _initterm
  -> strcmp / strlen
  -> GetEnvironmentVariableW("DOTNET_gcServer") = 0, ERROR_ENVVAR_NOT_FOUND
  -> _stricmp: 885 checked calls, all successful
  -> KERNEL32.dll!GetSystemInfo (next authentic boundary; not implemented)
```

No allocation, GC startup, managed-thread registration, general SLIST operation, locale subsystem, environment table, or later API support follows from this closure.

## Continuation: `GetSystemInfo` (2026-07-31)

The next exact dependency was subsequently closed in [KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md](KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md). Three positive runs complete the observed `SYSTEM_INFO` consumer and advance to `KERNEL32.dll!GetNumaHighestNodeNumber`; the disabled control retains the `_stricmp` boundary before any GetSystemInfo implementation runs. This continuation does not change the `_stricmp` contract or imply allocation/GC readiness.

## Sources

- Microsoft, [`_stricmp` reference](https://learn.microsoft.com/en-us/cpp/c-runtime-library/reference/stricmp-wcsicmp-mbsicmp-stricmp-l-wcsicmp-l-mbsicmp-l?view=msvc-170)
- Microsoft, [`x64 calling convention`](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170)
- Microsoft-maintained UCRT source cross-check, [`string/stricmp.cpp`](https://github.com/huangqinjin/ucrt/blob/master/string/stricmp.cpp)
- Microsoft-maintained UCRT source cross-check, [`ctype.h` ASCII folding](https://github.com/huangqinjin/ucrt/blob/master/inc/corecrt_ctype.h)

No commit or push was performed for this pass.

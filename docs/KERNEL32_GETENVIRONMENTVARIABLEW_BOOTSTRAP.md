# Microsoft x64 `GetEnvironmentVariableW` bootstrap contract

Status: closed for the one live NativeAOT startup call observed in this artifact. This document records a narrow platform contract, not a process-environment implementation.

This task implements only the Microsoft x64 GetEnvironmentVariableW platform contract required by the current NativeAOT startup path.

## Baseline and scope

The pass began on branch `main`, HEAD `69dfcfd70b4ce6bae35672e4539eaaac0b31774d`, upstream `origin/main`, with a clean worktree. The preceding `strlen` contract was already committed. A fresh pre-change run under `evidence\generated\getenv-baseline-prechange-20260731` reproduced the boundary:

```text
... -> strcmp("gcServer", "gcConservative")
    -> strlen("gcServer") = 8
    -> KERNEL32.dll!GetEnvironmentVariableW
```

Only the workspace repository was changed. The reference repositories remained read-only. No commit or push was performed for this pass.

The implementation is intentionally limited to the exact observed request. It does not provide a process environment block, inheritance, expansion, registry integration, PATH handling, user/system separation, or any other environment API.

## Gate A: live caller investigation

The imported symbol is `KERNEL32.dll!GetEnvironmentVariableW`, IAT RVA `0x7d088`, with preferred IAT address `0x18007d088`. Its normal import thunk is at preferred `0x1800772ef`. The live call is reached from the NativeAOT GC-configuration helper at preferred `0x18003e150`:

```text
preferred 0x18003e196: call 0x18003c8d0
preferred 0x18003e19b: return address after the call
runtime   0x00000000054B9196: call address
runtime   0x00000000054B919B: return address
```

The surrounding sequence converts the source string, places `R8D=0x11`, `RDX=&rsp+0x20`, and `RCX=&rsp+0x50`, then calls the imported function. On return it copies `EAX`, computes `EAX-1`, compares that value with `0xf`, and takes the absent/fallback path when the result is zero. No value parsing follows the observed zero result.

Each of the three final fresh QEMU runs recorded exactly one call:

| Field | Observed value |
| --- | --- |
| `lpName` | `0x0000000007E64B40`, non-null |
| decoded UTF-16 name | `DOTNET_gcServer` |
| UTF-16 code units | 15 |
| UTF-16 hex | `0044004F0054004E00450054005F00670063005300650072007600650072` |
| `lpBuffer` | `0x0000000007E64B10`, non-null |
| `nSize` | `0x11` = 17 characters |
| size zero | no |
| NULL buffer | no |
| size probe | no |
| caller return address | `0x00000000054B919B` |
| result | `0` |
| last error before / after | `0x00000000` / `0x000000CB` (203) |
| last-error changed | yes |
| output written | no |
| immediate caller expectation | zero means absent/fallback |
| second call | no |
| calls per process | one |

The only variable queried by the live path was `DOTNET_gcServer`. Static call-site inspection found other potential environment calls in the broad image, but none was reached by these runs and none is claimed as a live startup query.

## Gate B: observed NativeAOT behavior

`DOTNET_gcServer` is queried by the helper that has just compared `gcServer` with `gcConservative`; the execution evidence therefore identifies this as a GC-configuration lookup. The observed variable is absent. The caller accepts a zero return and immediately takes its fallback/absent path.

The evidence proves:

- absence is expected for this run;
- the return value is consumed as an absent-value test;
- no value parsing follows the zero result;
- no environment value changes the observed runtime mode;
- this lookup did not initialize GC, create an allocation context, make the heap usable, register a managed thread, or allocate an object.

The evidence does not prove behavior for a present GC variable, a parsed value, or any other NativeAOT configuration variable.

## Gate C: Microsoft contract

The authoritative Microsoft reference is [`GetEnvironmentVariableW`](https://learn.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-getenvironmentvariablew). Its signature is:

```c
DWORD GetEnvironmentVariableW(
    LPCWSTR lpName,
    LPWSTR  lpBuffer,
    DWORD   nSize
);
```

The documented behavior relevant to this milestone is:

- `lpName` identifies a null-terminated UTF-16 environment-variable name.
- `nSize` is the output-buffer capacity in characters, including space for the terminating null.
- On success, the return value is the number of characters copied, excluding the terminating null.
- If the variable is absent, the return value is zero and `GetLastError()` reports `ERROR_ENVVAR_NOT_FOUND` (203).
- If the variable exists but the buffer is too small, the return value is the required size in characters, including the terminating null; the output buffer contents are undefined.
- If the variable exists and the value is empty, a buffer of size one receives only the terminator and the return value is zero. A zero-capacity query reports the required size one.
- A size query is represented by an insufficient or zero-capacity output request; the required count includes the terminator. The live NativeAOT call was not a size query.
- On a successful copy, the value is null-terminated. The returned count excludes that terminator.
- Unicode counts are UTF-16 code-unit counts, not Unicode scalar-value counts. For example, a value containing one supplementary-plane character consumes two UTF-16 units.
- Microsoft documents a maximum user-defined environment-variable length of 32,767 characters. The checked guideXOS core bounds names and table values rather than allocating to support an unbounded block.

For Microsoft x64, the first three integer/pointer arguments are passed in `RCX`, `RDX`, and `R8`; the scalar `DWORD` result is returned in `EAX`/`RAX`, with the normal x64 shadow-space and stack-alignment rules. See Microsoft's [x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170).

`GetEnvironmentVariableW` is documented to report `ERROR_ENVVAR_NOT_FOUND` for a missing variable. It is not documented here as a general last-error-reset function: for successful, empty, and insufficient-buffer cases the Windows host reference preserved a caller-installed sentinel. Microsoft's [`GetLastError` documentation](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-getlasterror) also warns that callers must use the individual API contract rather than infer a universal success reset.

The Microsoft API may use system-managed environment storage and is not an allocation-free contract. The guideXOS implementation below deliberately performs no allocation and no external call because the observed startup request is a missing-variable lookup against no supplied environment table. “No allocation” is therefore a guideXOS implementation property, not a claim about all Windows implementations.

## Gate D: minimal guideXOS contract

The checked core is `src/Gate4Harness/platform_environment.c` with its declaration in `src/Gate4Harness/platform_environment.h`. It supports a bounded table of UTF-16 name/value pairs for host verification and validates:

- readable, terminated UTF-16 names;
- writable output spans when a copy is required;
- exact UTF-16 name matching;
- overflow-safe required-size calculation;
- missing, empty, exact-size, too-small, and size-query behavior;
- checked invalid-pointer and malformed-table rejection.

The runtime import route is narrower than the host core. `GXOS_ENABLE_GETENV` routes only `KERNEL32.dll!GetEnvironmentVariableW` to a table-free missing-variable function. For the actual NativeAOT call it returns zero, changes last error to 203, does not write the caller's buffer, and emits bounded diagnostics. No process-wide table is installed.

The route does not implement `SetEnvironmentVariable`, `ExpandEnvironmentStrings`, process management, inheritance, registry lookup, user/system environments, PATH handling, allocation, GC, thread registration, scheduler behavior, or any other import. The subsequent `_stricmp` route is a separate milestone documented in [CRT_STRICMP_BOOTSTRAP.md](CRT_STRICMP_BOOTSTRAP.md); this document does not absorb that contract into the environment API.

## Gate E: host tests and regressions

`tools\Run-PlatformEnvironmentHostTests.ps1` passed the standalone checked suite. It covered:

- missing variable;
- existing `abc` value;
- empty value;
- NULL output with size zero;
- exact-size buffer and terminating null;
- too-small buffer with unchanged sentinel bytes;
- Unicode variable name and Unicode value, including a surrogate pair;
- repeated queries;
- invalid pointers in the checked layer;
- Microsoft x64 function-pointer ABI shape;
- no allocation and no unresolved external references in the standalone object.

The regression suites for `strlen`, `strcmp`, `_initterm`, and `_initterm_e` all passed. Their prior immutable evidence remains preserved; this pass did not change their contracts.

## Gates F and I: immutable runtime evidence

The final enabled artifact set is `evidence\generated\getenv-final-20260731-immutable`. Three consecutive fresh QEMU runs passed the validator with one immutable artifact snapshot, fresh OVMF variables, unique PIDs, clean QEMU exit, and complete serial logs.

| Run | PID | Serial bytes | GetEnv calls | Name / result / last error | Imports | QPC | Next boundary |
| --- | ---: | ---: | ---: | --- | --- | --- | --- |
| run1 | 8648 | 15500 | 1 | `DOTNET_gcServer` / `0` / `203` | 28 / 96 / 0 | 2, regressions 0 | `api-ms-win-crt-string-l1-1-0.dll!_stricmp` |
| run2 | 13476 | 15500 | 1 | `DOTNET_gcServer` / `0` / `203` | 28 / 96 / 0 | 2, regressions 0 | `api-ms-win-crt-string-l1-1-0.dll!_stricmp` |
| run3 | 7100 | 15500 | 1 | `DOTNET_gcServer` / `0` / `203` | 28 / 96 / 0 | 2, regressions 0 | `api-ms-win-crt-string-l1-1-0.dll!_stricmp` |

The three QPC first/last/delta summaries were `0x1E17F -> 0x3C47E` / `0x1E2FF`, `0x1DCE5 -> 0x398BD` / `0x1BBD8`, and `0x1F828 -> 0x3C816` / `0x1CFEE`. All runs retained `UNRESOLVED_REQUIRED_IMPORTS=0` and no CPU fault, hang, or premature termination.

The disabled-routing control is `evidence\generated\getenv-disabled-20260731`. Its three runs retained 27 functional / 97 fail-fast imports and stopped at the original `KERNEL32.dll!GetEnvironmentVariableW` boundary without `GETENV_*` implementation markers.

The final artifact snapshot records these hashes:

```text
EFI loader:       968F09CC3B2D44D8FB242FE556BC59214A64F659893940B664D1ECEFD20789D3
NativeAOT image:  2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837
Runtime archive:  DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311
OVMF code:        33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A
OVMF vars:        5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E
QEMU:             A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02
Runner:           F5BEBE70AACEA13168F7633CDAF0293F11448D5ACD42FDD5AF11D35F0DC2AE44
Validator:        307598AC72876E7C4C29A86F3D3DF64BFC5D4C62A331D7CE5D704E87FB74012E
```

## Gate G: runtime-state accounting

The final positive runs reported:

```text
GC_CONTRACT_INITIALIZED=0
GC_HEAP_USABLE=0
ALLOCATION_CONTEXT_CREATED=0
ALLOCATION_CONTEXT_VALID=0
MANAGED_THREAD_REGISTERED=0
MANAGED_ALLOCATION_COUNT=0
TLS_ALLOC_LIMIT=0
TLS_ALLOC_PTR=0
QPC_COUNT=2
QPC_REGRESSIONS=0
```

The path queried GC configuration; it did not parse a configuration value, initialize GC, make a heap usable, create an allocation context, register a managed thread, or perform an allocation. The existing `GC_STARTUP_BEGIN` trace marker remains only a loader/startup trace boundary.

## Gate H: negative controls

`evidence\generated\getenv-negative-controls-20260731-final` passed all required intended-failure checks:

```text
GETENV_NEGATIVE_DISABLED_ROUTING=PASS
GETENV_NEGATIVE_MARKER_MUTATION=PASS
GETENV_NEGATIVE_STALE_EVIDENCE=PASS
GETENV_NEGATIVE_DUPLICATE_PID=PASS
GETENV_NEGATIVE_ARTIFACT_HASH_MISMATCH=PASS
GETENV_NEGATIVE_EVIDENCE_PIPELINE=PASSED
```

The focused host suite also passed existing, empty, size-probe, insufficient-buffer, Unicode, invalid-pointer, repeated-query, no-allocation, and no-external-reference controls. The disabled route failed only at the intended import boundary; no mutation was accepted as a valid positive run.

## Startup chain and next boundary

The current proven chain is:

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
  -> strcmp("gcServer", "gcConservative")
  -> strlen("gcServer") = 8
  -> GetEnvironmentVariableW("DOTNET_gcServer") = 0, ERROR_ENVVAR_NOT_FOUND
  -> api-ms-win-crt-string-l1-1-0.dll!_stricmp
```

At the close of this environment milestone, the next authentic startup boundary was `api-ms-win-crt-string-l1-1-0.dll!_stricmp`. That separate contract is now closed; its three-run evidence advances to `KERNEL32.dll!GetSystemInfo`. No environment subsystem or later phase is inferred here.

## Files changed for this milestone

```text
src/Gate4Harness/platform_environment.h
src/Gate4Harness/platform_environment.c
src/Gate4Harness/gate4_loader.c
tools/Build-Gate4Harness.ps1
tools/Run-PlatformEnvironmentHostTests.ps1
src/Gate4Harness/tests/platform_environment_tests.c
tools/Run-GetEnvironmentVariableWFinalValidation.ps1
tools/Validate-GetEnvironmentVariableWEvidence.ps1
tools/Test-PlatformEnvironmentEvidencePipeline.ps1
README.md
docs/DEPENDENCY_CENSUS.md
docs/EVIDENCE_LEDGER.md
docs/NATIVEAOT_ARTIFACT_ANATOMY.md
docs/NEXT_STAGE_BLOCKERS.md
docs/FIRST_MANAGED_ALLOCATION.md
docs/KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md
```

No commit or push occurred. This closure makes no claim of a complete Windows environment subsystem or GC initialization.

## Follow-on `_stricmp` boundary (2026-07-31)

The missing-variable result documented here is the first input to the separately scoped `_stricmp` startup milestone. The follow-on observed 73 bounded missing environment queries while comparing static image-backed configuration/name strings; none of the `_stricmp` operands came from an environment value. The environment implementation remained unchanged and no process environment table was introduced. See [CRT_STRICMP_BOOTSTRAP.md](CRT_STRICMP_BOOTSTRAP.md) for the exact route, call-site census, and next `GetSystemInfo` boundary.

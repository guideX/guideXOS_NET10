# `KERNEL32.dll!GetModuleHandleW` bootstrap contract

Status: closed as a narrow, evidence-backed failure contract for the current NativeAOT startup path (2026-08-01).

This milestone implements only the Microsoft x64 `GetModuleHandleW` contract required by the current NativeAOT startup path. It does not implement `GetModuleHandleA`, `GetModuleHandleEx*`, `GetProcAddress`, DLL loading, module enumeration, reference counting, unloading, or a general process-module registry.

## Boundary and live call

The fresh pre-change baseline is preserved under `evidence\generated\getmodulehandlew-baseline-20260801`. It used a fresh QEMU process and reproduced the immediate boundary:

```text
34 functional / 90 fail-fast / 0 unresolved
KERNEL32.dll!GetModuleHandleW
```

The importing identity is stable in the staged NativeAOT payload:

| Fact | Value |
| --- | --- |
| Importing module | `KERNEL32.dll` |
| Symbol | `GetModuleHandleW` |
| Import descriptor | `0x2` |
| Preferred IAT slot | `0x18007D130` |
| IAT RVA | `0x7D130` |
| Static call site | `0x180037C61` |
| Caller start | `0x180037C40` |
| Caller | `NativeAOT_RtlDllShutdownInProgress_probe` |

The runtime call site is the image base plus the static RVA. In the validation artifact this is `0x00000000054B2C61`, with return address `0x00000000054B2C67`. The first call is live. A second static `GetModuleHandleW` reference for `kernel32.dll` exists at preferred call site `0x18003C553`, but it is dormant because the first call's downstream `GetProcAddress` boundary is reached first.

The bounded instruction trace is:

```text
NativeAOT startup
  -> runtime helper around 0x180031CD0
  -> helper 0x180037C40, CL=1
  -> GetModuleHandleW(&L"ntdll.dll")
  -> returned RAX is immediately supplied as the HMODULE argument to GetProcAddress
  -> ASCII export name "RtlDllShutdownInProgress"
  -> KERNEL32.dll!GetProcAddress fail-fast boundary
```

The observed `lpModuleName` is non-null, points into the NativeAOT payload's read-only `.rdata`, and is exactly nine UTF-16 code units:

```text
ntdll.dll
```

The input pointer is canonical and readable; the containing region is readable, non-executable, and non-writable. The diagnostics use a bounded 256-code-unit scan, record the terminator, distinguish empty from null input, and never use a processor fault as normal validation flow.

## Module identity

The loader manually maps one NativeAOT PE payload. The current process-like execution context is the payload, because its relocated entry point establishes NativeAOT execution, its import table owns the `GetModuleHandleW` IAT slot, and the observed caller executes in that payload. The UEFI application, harness code, OVMF firmware, runtime archive, synthetic import stubs, stack, and heap are not interchangeable Windows process modules.

The staged payload is:

```text
artifacts\getmodulehandlew-final-20260801\ESP\GXOS\gxos-managed-entry-probe.dll
SHA-256: 2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837
```

The final UEFI loader in that artifact set is `ESP\EFI\BOOT\BOOTX64.EFI`, SHA-256 `4CF43A27709582029E3A9EEA15D574B12B99A3E5052140C52E28D26D708D06D5` (165,852 bytes). The runtime archive is SHA-256 `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` (2,488,386 bytes). The complete execution-relevant hash set, including OVMF, QEMU, startup script, source files, runner, and validator, is immutable in `artifact-manifest.json`.

Its relevant PE facts are:

| Fact | Value |
| --- | --- |
| Machine | `0x8664` (x64) |
| Optional header | PE32+ (`0x20B`) |
| Preferred image base | `0x180000000` |
| Actual mapped base | `0x000000000547B000` |
| Relocation delta | `0xFFFFFFFE8547B000` |
| `SizeOfImage` | `0xD3000` |
| Mapped range | `[0x547B000, 0x554E000)` |
| Entry-point RVA | `0x77700` |
| Runtime entry point | `0x54F2700` |
| Import directory | RVA `0xA8D4C`, size `0xDC` |
| Importing IAT range | RVA `0x7D130`, eight bytes |

The mapped section permissions are `.text` RX, `.rdata` R, `.data` RW, `.pdata` R, `.rsrc` R, and `.reloc` R. Headers remain mapped and are treated as readable by the checked image-facts contract. The payload—not the UEFI loader or compatibility wrapper—owns the importing IAT and the NativeAOT entry point.

The exact live name is `ntdll.dll`, but no ntdll PE image is mapped in this guideXOS process. Returning the payload base for that name would violate the Microsoft rule that a named module must already be loaded and would pass an unrelated image to `GetProcAddress`. Therefore the truthful positive policy returns `NULL` and `ERROR_MOD_NOT_FOUND` (`126`) for this observed name. The null-name current-executable policy is implemented and host-tested, but it is not claimed as the live QEMU result.

## Microsoft contract

The authoritative reference is Microsoft’s [`GetModuleHandleW` documentation](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandlew). It specifies an optional UTF-16 module name, a null-name query for the executable used to create the calling process, named lookup only for a module already loaded by the calling process, case-independent comparison, a null failure return with `GetLastError`, and no reference-count increment. The returned handle must not be treated as an ownership token for `FreeLibrary`; it is a non-owning module handle. Named handles are process-local and are not cross-process or inheritable objects. Path and extension rules are not generalized here because the live call supplies a base name with an extension and the named image is absent.

Microsoft defines `HMODULE` as the module base address; the [Windows data type definition](https://learn.microsoft.com/en-us/windows/win32/winprog/windows-data-types) derives it from a pointer-sized handle. The [Microsoft x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170) places the first pointer argument in `RCX` and returns a pointer-sized scalar in `RAX`. The [GetProcAddress contract](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getprocaddress) requires an `HMODULE` identifying the DLL whose exports are queried. The [run-time dynamic linking guidance](https://learn.microsoft.com/en-us/windows/win32/dlls/run-time-dynamic-linking) likewise describes `GetModuleHandleW` as finding an already mapped module without increasing its reference count.

The implementation does not allocate, mutate PE headers, change a reference count, acquire a loader lock, search a filesystem, load a DLL, or change last error on success. It preserves the caller's prior last-error value on success. Invalid controlled inputs use deterministic internal rejection; the live absent named module uses `ERROR_MOD_NOT_FOUND` (`126`).

## Checked contract and ABI

`platform_get_module_handle.h/.c` define a small checked core and a thin loader wrapper. The core supports:

- `NULL` as a current-executable query, with no string read;
- exact observed `ntdll.dll` and `kernel32.dll` recognition, returning `MODULE_NOT_FOUND` when those named PE images are not in the mapped-module set;
- deterministic rejection of other names, paths, malformed input, unreadable or noncanonical pointers, and bounded unterminated names;
- immutable loader-supplied facts for the preferred base, actual base, entry point, image size, import directory, IAT ownership, mapped regions, and relocations;
- success publication only after canonical-base, header, DOS, NT, x64 machine, PE32+, image-range, entry-point, import-ownership, and relocation checks pass.

The PE-facing wrapper has the exact Microsoft shape `HMODULE GetModuleHandleW(LPCWSTR)`, uses the Microsoft x64 ABI, returns only a pointer-sized virtual address, and leaves the output conceptually unchanged on checked failure. Host static assertions prove 8-byte `HMODULE`, 8-byte `LPCWSTR`, and 2-byte Windows UTF-16 code units. The core object has no undefined external references and no external CRT dependency.

## Caller consumption and result policy

For the live call, the caller tests the returned `RAX` for null through the downstream probe path and takes the failure path. It does not read DOS or NT headers from the failed result, store a module handle in runtime-global state, perform pointer arithmetic on a returned handle, or make another `GetModuleHandleW` call. The returned value is passed to the next loader API boundary, `GetProcAddress`, together with `RtlDllShutdownInProgress`. That API is intentionally not implemented.

The successful null-name checked path returns the actual mapped payload base, never the preferred base, an RVA, the UEFI loader address, or the compatibility wrapper address. It validates the complete initial header range, `MZ`, `e_lfanew`, `PE`, machine, PE32+, `SizeOfImage`, entry point, importing IAT, and relocation delta before publishing the base. The live named failure does not claim that an unrelated payload is a valid ntdll image.

## Controlled experiments and negative controls

The counted positive policy is not inferred from an arbitrary nonzero return. The following bounded experiments were run outside the final three-run sequence:

| Experiment | Returned value | Observation | Treatment |
| --- | --- | --- | --- |
| Actual mapped payload base | `0x547B000` | Caller takes nonzero branch and reaches `GetProcAddress`; this is not proof that the payload is ntdll. | Investigation only; not positive policy |
| Forced failure | `NULL`, error `0x57` | Caller takes null branch and still reaches the next loader boundary. | Negative control |
| Preferred-base substitution | `0x180000000` | Differs from actual relocated base. | Rejected |
| RVA substitution | `0x77700` | Not a virtual module address. | Rejected |
| Wrong-image substitution | `0x1053D0` | Compatibility-wrapper/loader address, not payload base. | Rejected |
| Invalid DOS/NT/machine/optional/range facts | no publication | Host checked core rejects each mutation. | Passed negative controls |

The disabled-routing control leaves the import census at `34 / 90 / 0` and stops at `KERNEL32.dll!GetModuleHandleW`; no module marker appears. The enabled route changes the census to `35 / 89 / 0` and stops at `KERNEL32.dll!GetProcAddress`. `tools\Test-GetModuleHandleWEvidencePipeline.ps1` was run against disposable copies and rejected marker mutation, truncated evidence, stale run ID, duplicate PID, and artifact-hash mutations.

## Validation and state boundary

The focused host suite is `tools\Run-PlatformGetModuleHandleHostTests.ps1`. It covers ABI widths and argument/result declaration, null and exact-name behavior, actual-vs-preferred base, RVA and wrong-image rejection, canonical and mapped ranges, header signatures, PE32+, image size, entry point, import ownership, relocation facts, bounded UTF-16 validation, output preservation, and the no-external-reference check. Existing time, QPC, CRT, environment, `_stricmp`, topology, affinity, and job-object suites remain unchanged and are included in the QEMU regression path.

The immutable final QEMU evidence is recorded under `evidence\generated\getmodulehandlew-final-20260801-immutable`. QEMU version is `11.0.0`; every run uses a fresh OVMF variable file, a unique run ID and PID, identical hashed artifacts, 249,669 serial bytes, lifecycle logs, and cleanup verification. The validator passed the complete prior startup path, one named `GetModuleHandleW` call, its exact argument and mapped-region facts, `NULL`/`126`, the downstream caller boundary, zero QPC regressions, and zero allocation/GC progress.

| Run | QEMU PID | Serial SHA-256 | Lifecycle |
| --- | ---: | --- | --- |
| `getmodulehandlew-final-20260801-immutable-run1` | `22316` | `956668F9BCB5D8BD3F3DF6D5F7E1E3661383E3A0B7E551E9FD6F8C9168148D7C` | pass; cleaned after guest no-progress |
| `getmodulehandlew-final-20260801-immutable-run2` | `28856` | `3B518A17E8E052A7478F6E00F0A470DCC65CEAB41F7CD5F58D81E85DDA700346` | pass; cleaned after guest no-progress |
| `getmodulehandlew-final-20260801-immutable-run3` | `22792` | `B721772408824D674E6277AD68BCB860F66290A52DCC78D5A257EC21A586B5B1` | pass; cleaned after guest no-progress |

The disabled three-run control is under `evidence\generated\getmodulehandlew-final-disabled-20260801-immutable`; it retains the `GetModuleHandleW` boundary with 245,966-byte logs and PIDs `18092`, `13072`, and `26620`. No processor fault, hang, triple fault, GC heap, allocation context, managed-thread registration, or managed allocation is claimed; the guest is deliberately stopped after deterministic bounded evidence capture rather than allowed to run indefinitely.

The next authentic dependency is:

```text
KERNEL32.dll!GetProcAddress
```

That is the next milestone. It is not implemented here.

# `KERNEL32.dll!GetProcAddress` bootstrap contract

Status: closed for the one live Microsoft x64 NativeAOT startup call observed in the current artifact. This task implements only the Microsoft x64 GetProcAddress platform contract required by the current NativeAOT startup path.

The implementation is deliberately not a general Windows loader, export resolver, DLL loader, or `ntdll` compatibility layer. It closes the observed null-module failure path and records the exact pointer/name evidence needed to show that the caller takes its optional fallback.

## Windows contract reference

The Microsoft [`GetProcAddress` contract](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getprocaddress) takes an `HMODULE` returned by a module-loading or module-handle API and an ANSI procedure identifier. The identifier can be an export name or an ordinal represented by a low-order word with a zero high-order word. A successful call returns the exported address; failure returns `NULL`, after which the caller may inspect `GetLastError()`. Export-name matching is exact and case-sensitive.

The wrapper uses the Microsoft x64 register convention: `RCX = hModule`, `RDX = lpProcName`, and `RAX = FARPROC`/pointer result. The ABI declaration uses `ms_abi` on x86-64; pointer-width assertions require eight-byte `HMODULE`, `LPCSTR`, and `FARPROC` values. See Microsoft's [x64 software conventions](https://learn.microsoft.com/en-us/cpp/build/x64-software-conventions?view=msvc-170).

## Actual startup reachability

The unchanged NativeAOT payload imports `KERNEL32.dll!GetProcAddress` at IAT RVA `0x7d138` (`0x18007d138` preferred). The only live call in the final positive traces is:

```text
NativeAOT_RtlDllShutdownInProgress_probe
  GetModuleHandleW(&L"ntdll.dll")
    -> NULL, ERROR_MOD_NOT_FOUND (126)
  GetProcAddress(NULL, "RtlDllShutdownInProgress")
    -> NULL, ERROR_PROC_NOT_FOUND (127)
  -> FAILURE_NULL_OPTIONAL_FALLBACK
  -> api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function
```

The preferred direct `GetProcAddress` call is `0x180037c71`; the runtime call is image-base-relative and the return address is exactly six bytes after the call. The caller begins at preferred `0x180037c40`. The live argument is a null module handle and an ANSI pointer into relocated read-only `.rdata` at runtime `0x5512178`; the exact bytes are `52746C446C6C53687574646F776E496E50726F6772657373`, or `RtlDllShutdownInProgress`, length `0x18`, with its terminator at `0x5512190`.

The final positive traces prove one live call per run, named lookup `1`, ordinal lookup `0`, export-lookup attempts `0`, result `0`, pointer stored `0`, pointer called `0`, and the caller's null optional-fallback branch. Other static `GetProcAddress` references at preferred `0x18003c568`, `0x18003c9b1`, `0x18003ca92`, `0x18003cada`, and `0x18003ce77` are dormant for this startup path.

## Checked implementation boundary

The core is split between `src/Gate4Harness/platform_get_proc_address.h` and `src/Gate4Harness/platform_get_proc_address.c`. It has no allocation, CRT, external symbol, module-loading, or export-table dependency. The loader wrapper in `src/Gate4Harness/gate4_loader.c` is routed only for the exact `KERNEL32.dll!GetProcAddress` import.

The checked path:

- classifies the raw identifier before dereferencing it; a value with zero high-order bits and a low word in the pointer-sized ordinal form is treated as an ordinal and never scanned as a string;
- validates canonical x64 pointers, approved readable mapped regions, bounded termination, region permissions, exact bytes, and high-bit/7-bit facts for name identifiers;
- preserves the prior error for the tested successful-pointer policy and reports the selected error for the tested failure policy;
- returns `NULL` with `ERROR_PROC_NOT_FOUND` (`127`) for the observed null module and does not attempt export lookup;
- rejects noncanonical or unapproved non-null module handles without pretending that a fabricated handle identifies a loaded image;
- keeps ordinal resolution out of scope under the current startup policy; and
- does not parse PE export directories, resolve forwarded exports, search DLLs, load DLLs, enumerate modules, or alias the current payload to `ntdll`.

The status enum contains room for future checked outcomes, but the current implementation does not claim that a valid mapped module can be resolved. In particular, `EXPORT_NOT_FOUND` is not evidence that a general export resolver exists. A bounded unterminated name is rejected by the current scan-limit policy rather than being read speculatively.

The chosen `127` value is evidence-backed rather than inferred from the Windows header alone. `tools/GetProcAddressHostProbe.c`, run by `tools/Run-GetProcAddressHostReference.ps1` on Windows, observed `ERROR_PROC_NOT_FOUND` for both `GetProcAddress(NULL, "RtlDllShutdownInProgress")` and a missing name. The exact-name Windows reference preserved a sentinel last error (`0xA5A5A5A5`), while a case mismatch returned `NULL`/`127`; those observations are retained under `artifacts/getprocaddress-host-reference-20260801/reference-output.txt`.

## Tests and evidence

The focused host suite is `tools/Run-PlatformGetProcAddressHostTests.ps1`. It checks the Microsoft x64 ABI, pointer widths, name/ordinal classification, canonical and mapped memory checks, bounded strings, null/invalid handles, output publication, last-error behavior, negative controls, and the no-external-reference property. The final focused run reports `GETPROCADDRESS_HOST_TESTS_OK`, `GETPROCADDRESS_NO_EXTERNAL_REFERENCES=PASS`, and `GETPROCADDRESS_HOST_TESTS=PASSED`.

The final immutable positive evidence is `artifacts/getprocaddress-final-v3-20260801-immutable-v2`. It uses QEMU 11.0.0, an unchanged 729,600-byte NativeAOT payload (`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`), a 172,235-byte loader (`C692F38E990ACB0A9A69E5F059520ABC6FB43B23E50EF5F848A2F71CA2845593`), three unique PIDs, 253,581-byte serial logs, unique serial hashes, identical artifact fingerprints, complete cleanup, and the `36 / 88 / 0` functional/fail-fast/unresolved census. The validator and final gate both pass.

The disabled immutable control is `artifacts/getprocaddress-final-disabled-v7-20260801-immutable-v2`. It retains the prior `GetModuleHandleW` route, emits no `GETPROCADDRESS_BEGIN`, preserves the `35 / 89 / 0` census, and stops at the authentic `KERNEL32.dll!GetProcAddress` fail-fast boundary. It also passes three fresh runs with unique PIDs, 249,669-byte serial logs, unique hashes, identical artifact fingerprints, and complete cleanup.

The evidence pipeline `tools/Test-GetProcAddressEvidencePipeline.ps1` passed seven rejection controls: wrong last error, wrong boundary, truncated final diagnostics, stale run ID, duplicate QEMU PID, artifact hash mismatch, and fabricated export-lookup attempt. The final control output is under `artifacts/getprocaddress-negative-controls-20260801-final`.

Two investigation-only QEMU experiments are retained separately. The synthetic-pointer experiment stores a non-null synthetic address, takes the success-pointer branch, does not call the stub, and still advances; the deliberately wrong-error experiment changes the observed `127` to `6` but still advances. Their summaries explicitly set `PositiveContractEligible=false` under `artifacts/getprocaddress-synthetic-pointer-experiment-20260801-v2` and `artifacts/getprocaddress-wrong-error-experiment-20260801`.

All final traces report zero QPC regressions, zero GC contract initialization, zero usable GC heap, zero allocation context, zero managed-thread registration, and zero managed allocations. This contract advances startup reachability only; it does not advance the first managed allocation.

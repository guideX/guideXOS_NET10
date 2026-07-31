# guideXOS_NET10

Experimental guideXOS reimagining on .NET 10 NativeAOT.

This repository is an archaeology and bootstrap project. It is not a direct upgrade of `guideXOS` Legacy, and it does not replace `guideXOSUEFI` or guideXOS Server. Those repositories remain read-only references and experiments.

## Current state

The repository contains a small .NET 10 NativeAOT managed-entry probe and a narrowly scoped UEFI PE loader harness. The four-gate result is:

- Gate 1: passed — reproducible standard .NET 10 NativeAOT PE artifacts.
- Gate 2: passed — the linked runtime and platform dependency census is recorded.
- Gate 3: passed — PE/COFF anatomy and byte-for-byte staging are machine-checked.
- Gate 4: passed — all 124 imported symbols are satisfied; the 18-symbol bounded platform boundary establishes the one-thread NativeAOT state needed by this probe; three fresh QEMU processes entered managed code and returned deterministically.

The earlier ten-descriptor import stop is retained as historical evidence. `GXOS_NET10:MANAGED_ENTRY_OK` is emitted by managed execution, not by the native loader.

The allocation/GC follow-on remains bounded negative for allocation: the allocation artifact and differential census pass, and the exact FILETIME, monotonic performance, CRT on-exit, x64 SLIST-head, `_initterm_e`, `_initterm`, `strcmp`, and `strlen` contracts advance authentic startup. The current deepest boundary is `KERNEL32.dll!GetEnvironmentVariableW`; no allocation, GC startup, managed-thread registration, general SLIST operation, or general CRT/C++ initialization is claimed. The first allocation remains unproven. See [the `strlen` bootstrap contract](docs/CRT_STRLEN_BOOTSTRAP.md) for the closed milestone.

## Provisional first-image path

```text
UEFI firmware
  -> guideXOS-owned PE/COFF loader
  -> NativeAOT PE validation and relocation
  -> exact import resolution
  -> bounded TLS, stack, FLS, handle, virtual-query, and one-thread lock state
  -> exported ManagedMain reverse-P/Invoke entry
  -> managed serial callback and deterministic return
```

This milestone deliberately excludes allocation, garbage collection, exceptions, unwinding, threads, synchronization beyond the startup contract, reflection, dynamic loading, networking, globalization, filesystem support, and broad framework compatibility.

## NativeAOT feasibility documents

- [Build and toolchain record](docs/BUILD_TOOLCHAIN_RECORD.md)
- [NativeAOT artifact anatomy](docs/NATIVEAOT_ARTIFACT_ANATOMY.md)
- [Dependency census](docs/DEPENDENCY_CENSUS.md)
- [Image-format decision](docs/IMAGE_FORMAT_DECISION.md)
- [Boot ABI](docs/BOOT_ABI.md)
- [Managed-entry proof procedure](docs/MANAGED_ENTRY_PROOF.md)
- [NativeAOT allocation and GC probe](docs/ALLOCATION_GC_PROBE.md)
- [NativeAOT platform time contract](docs/PLATFORM_TIME_CONTRACT.md)
- [NativeAOT platform performance counter](docs/PLATFORM_PERFORMANCE_COUNTER.md)
- [Windows x64 SLIST initialization contract](docs/PLATFORM_SLIST_CONTRACT.md)
- [Windows x64 `_initterm_e` bootstrap contract](docs/CRT_INITTERM_E_BOOTSTRAP.md)
- [Windows x64 `_initterm` bootstrap contract](docs/CRT_INITTERM_BOOTSTRAP.md)
- [Windows x64 `strcmp` bootstrap contract](docs/CRT_STRCMP_BOOTSTRAP.md)
- [Windows x64 `strlen` bootstrap contract](docs/CRT_STRLEN_BOOTSTRAP.md)
- [Evidence ledger](docs/EVIDENCE_LEDGER.md)
- [Next-stage blockers](docs/NEXT_STAGE_BLOCKERS.md)

## Audit documents

- [Project direction](docs/PROJECT_DIRECTION.md)
- [Reference repositories](docs/REFERENCE_REPOSITORIES.md)
- [.NET 7 CoreLib/runtime delta](docs/DOTNET7_CORELIB_DELTA.md)
- [NativeAOT and ILCompiler strategy](docs/NATIVEAOT_ILCOMPILER_STRATEGY.md)
- [Bootloader comparison](docs/BOOTLOADER_COMPARISON.md)
- [Kernel reuse matrix](docs/KERNEL_REUSE_MATRIX.md)
- [Userland reuse matrix](docs/USERLAND_REUSE_MATRIX.md)
- [App Model direction](docs/APP_MODEL_DIRECTION.md)
- [First managed entry plan](docs/FIRST_MANAGED_ENTRY_PLAN.md)
- [Audit evidence ledger](docs/AUDIT_EVIDENCE.md)

## Working rules

`D:\dev\guideXOS_NET10` is the only writable repository for this effort. Legacy, UEFI, and Server are read-only code vaults. Audit documents distinguish confirmed observations from recommendations; a recommendation is not evidence that a later runtime feature already works.

## SLIST initialization evidence status (2026-07-29)

The narrow Windows x64 `InitializeSListHead` implementation and host contract suite are complete: one writable 16-byte, 16-byte-aligned NativeAOT-owned header is reset to two zero 64-bit words, with no allocation, GC initialization, managed-thread registration, or general SLIST mutation support. The final immutable artifact set is under `artifacts\slist-final-validation-20260729-corrected3`; its loader hash is `2EEBCD284F6D2E5AD1526EB15FA4AF6483E7B1FE9D17A448720A289FF64B0362` and its NativeAOT payload hash is `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`.

The required three-run QEMU gate is closed by `evidence\generated\slist-final-20260730-immutable`: fresh PIDs `17256`, `660`, and `15344` used identical artifact hashes, emitted the complete marker sequence and final summaries, and advanced to `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. The prior apparent stalls were guest triple faults caused by the bounded loader's un-packed IDTR and 32-entry replacement IDT; the harness now preserves the packed firmware IDTR and all IRQ vectors. The subsequent `_initterm_e` and `_initterm` closures are documented separately.

## `_initterm_e` bootstrap evidence status (2026-07-30)

The narrow Microsoft x64 `_initterm_e` contract is implemented and host-tested. The actual NativeAOT call passes the relocated `.rdata` range `0x00000000054F74D0` to `0x00000000054F74D8`, an exclusive eight-byte range containing one null entry. The iterator validates the range and executable callback targets, skips nulls, invokes non-null entries in forward order, and returns the first nonzero callback result without allocation. Because this artifact's table is empty, the three real QEMU runs invoke zero callbacks and correctly return zero; callback ABI, ordering, failure, bounds, and target-validation behavior are proven by the focused host vectors.

The immutable evidence is under `evidence\generated\crt-initterm-e-final-20260730-immutable-v4`. Three fresh QEMU processes complete the iterator and advance to the next authentic boundary, `_initterm`; this historical result is preserved unchanged.

## `_initterm` bootstrap evidence status (2026-07-30)

The narrow Microsoft x64 `_initterm` contract is implemented, host-tested, and routed only for the exact `api-ms-win-crt-runtime-l1-1-0.dll!_initterm` import. The actual NativeAOT call passes relocated `.rdata` bounds `0x00000000054F7468` to `0x00000000054F74B0`: nine entries, one null, and eight relocated direct `.text` callbacks. All eight valid callbacks entered and returned in forward order. Three immutable fresh QEMU runs are recorded under `evidence\generated\crt-initterm-final-20260730-immutable-v2` and stop at `api-ms-win-crt-string-l1-1-0.dll!strcmp`. GC heap usability, allocation context, managed-thread registration, managed allocation, broad CRT startup, and general C++ initialization remain unproven.

## `strcmp` bootstrap evidence status (2026-07-30)

The narrow Microsoft x64 `strcmp` implementation is complete, host-tested, and routed only for the exact `api-ms-win-crt-string-l1-1-0.dll!strcmp` import. The actual NativeAOT call compares `gcServer` with `gcConservative`, returns `+1`, and advances to `strlen`. The follow-on `strlen` milestone is closed separately; the historical `strcmp` evidence remains unchanged under `evidence\generated\crt-strcmp-final-20260730-immutable`.

## `strlen` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `strlen` implementation is complete, host-tested, and routed only for the exact `api-ms-win-crt-string-l1-1-0.dll!strlen` import. The actual NativeAOT call scans the read-only `.rdata` string `gcServer`, returns length `8`, and advances to `KERNEL32.dll!GetEnvironmentVariableW`. Three immutable fresh QEMU runs are recorded under `evidence\generated\crt-strlen-final-20260731-immutable-v3`; the disabled control retains the original `strlen` boundary. The enabled profile is 27 functional / 97 fail-fast / 0 unresolved imports, with zero allocation-context, managed-thread, and GC-heap evidence.

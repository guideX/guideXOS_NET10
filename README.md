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

The allocation/GC follow-on remains bounded negative for allocation: the allocation artifact and differential census pass, and the exact FILETIME, monotonic performance, CRT on-exit, x64 SLIST-head, `_initterm_e`, `_initterm`, `strcmp`, `strlen`, `GetEnvironmentVariableW`, and Microsoft x64 `_stricmp` contracts advance authentic startup. Three fresh positive QEMU runs now stop at the next authentic `KERNEL32.dll!GetSystemInfo` import after 885 checked `_stricmp` calls. No allocation, GC startup, managed-thread registration, general SLIST operation, or general CRT/C++ initialization is claimed. The first allocation remains unproven. See [the `_stricmp` bootstrap contract](docs/CRT_STRICMP_BOOTSTRAP.md) and [the preceding `GetEnvironmentVariableW` bootstrap contract](docs/KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md).

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
- [`GetEnvironmentVariableW` bootstrap contract](docs/KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md)
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
- [Windows x64 `_stricmp` bootstrap contract](docs/CRT_STRICMP_BOOTSTRAP.md)
- [`GetSystemInfo` bootstrap contract](docs/KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md)
- [`GetNumaHighestNodeNumber` bootstrap contract](docs/KERNEL32_GETNUMAHIGHESTNODENUMBER_BOOTSTRAP.md)
- [`GetProcessGroupAffinity` bootstrap contract](docs/KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md)
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

## `GetEnvironmentVariableW` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `GetEnvironmentVariableW` implementation is complete, host-tested, and routed only for the exact `KERNEL32.dll!GetEnvironmentVariableW` import. The live NativeAOT path queries `DOTNET_gcServer` once with a non-null 17-character buffer, receives missing-variable result `0`, and observes `ERROR_ENVVAR_NOT_FOUND` (`203`). The value is not parsed; the caller takes its fallback path. Three immutable fresh QEMU runs are recorded under `evidence\generated\getenv-final-20260731-immutable`; the enabled profile is 28 functional / 96 fail-fast / 0 unresolved imports and advances to `api-ms-win-crt-string-l1-1-0.dll!_stricmp`. The disabled control retains the original GetEnvironmentVariableW boundary. This is a narrow lookup contract, not a complete Windows environment subsystem or GC initialization.

## `_stricmp` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `_stricmp` implementation is complete, host-tested, and routed only for the exact `api-ms-win-crt-string-l1-1-0.dll!_stricmp` import. The checked route validates image-backed readable operands, bounded null termination, canonical pointers, overflow, and default-C-locale ASCII folding. Three fresh positive QEMU runs complete 885 calls and stop at the next authentic `KERNEL32.dll!GetSystemInfo` import; the disabled route stops at `_stricmp`. No later API is routed.

## `GetSystemInfo` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `KERNEL32.dll!GetSystemInfo` contract is complete, host-tested, and routed only for that exact import. The checked route validates the x64 `SYSTEM_INFO` destination and approved writable range, initializes the complete `0x30`-byte structure, and publishes the current image-backed/page-backed bootstrap facts. Three immutable positive QEMU runs complete the observed consumer's `0xA2` field-read mask and advance to `KERNEL32.dll!GetNumaHighestNodeNumber`; the disabled three-run control retains the original GetSystemInfo boundary. See [KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md](docs/KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md). This does not claim GC initialization, a general virtual-memory allocator, or processor discovery beyond the one bootstrap processor.

## `GetNumaHighestNodeNumber` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetNumaHighestNodeNumber` contract is complete and routed only for that exact import pair. The checked four-byte `ULONG` output contract publishes highest node `0` from the explicit one-processor/one-locality-domain `GetSystemInfo` snapshot, preserves the output on rejected inputs, and does not claim general NUMA, SMP, node-targeted allocation, scheduler locality, or GC support. Three immutable positive QEMU runs are recorded under `evidence\generated\getnumahighest-final-20260801-immutable-v2`; each reaches the next authentic `KERNEL32.dll!GetProcessGroupAffinity` boundary. See [KERNEL32_GETNUMAHIGHESTNODENUMBER_BOOTSTRAP.md](docs/KERNEL32_GETNUMAHIGHESTNODENUMBER_BOOTSTRAP.md).

## `GetProcessGroupAffinity` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetProcessGroupAffinity` contract is complete and routed only for that exact import pair. The observed caller performs one capacity probe with the current-process pseudo-handle, `GroupCount=0`, and `GroupArray=NULL`; the checked route returns `FALSE`, publishes required count `1`, sets `ERROR_INSUFFICIENT_BUFFER` (`122`), and the caller consumes the count without retrying or reading a group array. Three fresh immutable QEMU runs are recorded under `evidence\generated\getprocessgroup-final3-20260801-immutable-v4` and advance to the next authentic `KERNEL32.dll!GetProcessAffinityMask` boundary. The disabled control retains the original process-group boundary. See [KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md](docs/KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md).

This pass does not implement `GetProcessAffinityMask`, other processor-group APIs, NUMA/topology discovery, allocation, or GC behavior. The first-allocation blocker is unchanged.

## `QueryInformationJobObject` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!QueryInformationJobObject` contract is complete and routed only for the exact import pair. The live NativeAOT call uses `hJob=NULL`, class `JobObjectCpuRateControlInformation` (`15`), an eight-byte `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION` destination, `cb=8`, and `lpReturnLength=NULL`. The checked route returns the no-associated-job failure with `ERROR_ACCESS_DENIED` (`5`), preserves the sentinel output, and the caller takes `FAILURE_NO_ASSOCIATED_JOB_FALLBACK`. A second static class-9 reference exists in the payload but is dormant; each bounded positive run proves one live call.

Three immutable QEMU runs are recorded under `evidence\generated\queryjobobject-final-20260801`; the enabled census is `34 / 90 / 0` and the next authentic boundary is `KERNEL32.dll!GetModuleHandleW`. Host reference, focused host vectors, success/active-limit fact experiments, disabled routing, negative evidence controls, and prior regressions pass. See [KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md](docs/KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md). This does not claim a general job-object subsystem, CPU-rate enforcement, allocation, GC readiness, or managed-thread registration.

## `GetProcessAffinityMask` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetProcessAffinityMask` contract is now routed only for the exact import pair. The artifact makes two live calls: bitmap setup reads the eight-byte process mask and updates a processor bitmap; processor-count setup reads the process mask, counts its bits, and then reaches `KERNEL32.dll!QueryInformationJobObject`. Neither caller reads the system mask or calls `GetLastError`. The checked route publishes process/system masks `0x1`/`0x1` from the existing one-bootstrap-processor snapshot and supports only the current-process pseudo-handle.

## `GetModuleHandleW` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetModuleHandleW` contract is implemented and routed only for that exact import. The live call is non-null `L"ntdll.dll"`, not a null-name query. Because no ntdll PE is mapped in this guideXOS process, the truthful route returns `NULL` with `ERROR_MOD_NOT_FOUND` (`126`) and advances to the next authentic `KERNEL32.dll!GetProcAddress` boundary. The current-executable null-name policy is separately checked and returns the actual relocated NativeAOT payload base only after PE-facts validation; no general named-module registry or DLL loader is claimed.

The final three-run immutable evidence is under `evidence\generated\getmodulehandlew-final-20260801-immutable`; the positive profile is `35 / 89 / 0` functional/fail-fast/unresolved imports and the disabled control remains `34 / 90 / 0` at `GetModuleHandleW`. Host ABI/contract tests and preferred-base, RVA, wrong-image, forced-failure, named-main, disabled, invalid-header, and bounded-name controls pass. See [KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md](docs/KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md). GC initialization, usable heap, allocation context, managed-thread registration, and managed allocation remain unproven.

Three immutable positive QEMU runs are recorded under `evidence\generated\getprocessaffinity-final-20260801-immutable-v2`; each has bounded 241,507-byte serial evidence and reaches `KERNEL32.dll!QueryInformationJobObject`. The disabled control retains the `GetProcessAffinityMask` fail-fast boundary. Host vectors pass all 57 focused cases, the forced-failure experiment proves both caller fallbacks with unchanged outputs, and the negative evidence pipeline rejects mutation controls. See [KERNEL32_GETPROCESSAFFINITYMASK_BOOTSTRAP.md](docs/KERNEL32_GETPROCESSAFFINITYMASK_BOOTSTRAP.md). GC heap usability, allocation context, managed-thread registration, managed allocation, SMP, and general process-handle support remain unproven and out of scope.

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

The allocation/GC follow-on remains bounded negative for allocation: the allocation artifact and differential census pass, the exact UEFI-backed FILETIME, monotonic performance, CRT on-exit initialization, and x64 SLIST-head initialization contracts advance authentic startup. The current deepest boundary is `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`; no allocation, GC startup, managed-thread registration, or general SLIST operation is claimed. The first allocation remains unproven.

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

The narrow Windows x64 `InitializeSListHead` implementation and host contract suite are complete: one writable 16-byte, 16-byte-aligned NativeAOT-owned header is reset to two zero 64-bit words, with no allocation, GC initialization, managed-thread registration, or general SLIST mutation support. The fresh final artifact set is under `artifacts\slist-final-validation-20260729-195900`; its loader hash is `333F110626390045D8E9DB5081A99D198BB84720F5519CDCB4FE3B74B3C2CE9C` and its NativeAOT payload hash is `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`.

The required immutable three-run QEMU gate is not closed. Runs `slist-initialize-final-20260729-195900-run1`, `run2`, and `run3` used the same hashes but were rejected as incomplete: they stopped at varying pre-summary markers, with run 3 reaching `_initterm_e` but losing the final QPC summary. A separate fresh probe completed the exact SLIST contract and reached `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`; it is retained as `evidence\generated\slist-final-single-probe-20260729-200400`, not counted as three-run closure. File-backed lifecycle and QEMU-monitor diagnostics classify the failures as guest/firmware shutdowns (`paused (shutdown)`, `HLT=0`, reset-vector RIP), not terminal rendering loss. The next milestone remains the `_initterm_e` CRT startup contract.

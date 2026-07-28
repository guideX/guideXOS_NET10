# guideXOS_NET10

Experimental guideXOS reimagining on .NET 10 NativeAOT.

This repository is intentionally starting as an archaeology and bootstrap project. It is not a direct upgrade of `guideXOS` Legacy, and it does not replace `guideXOSUEFI` or guideXOS Server. Those repositories remain active references and experiments.

## Current state

The repository now contains a small .NET 10 NativeAOT managed-entry probe and a narrowly scoped UEFI PE loader harness. The four-gate result is:

- Gate 1: passed — standard .NET 10 NativeAOT PE artifacts build reproducibly.
- Gate 2: passed — the linked runtime and platform dependency census is recorded.
- Gate 3: passed — PE/COFF anatomy and byte-for-byte staging are machine-checked; no PE-to-ELF converter is adopted.
- Gate 4: not passed — the fresh QEMU runs load, relocate, and inspect the PE, then stop at ten unresolved Windows/CRT import descriptors before managed transfer.

The final marker `GXOS_NET10:MANAGED_ENTRY_OK` was therefore not claimed. This is an intentional, documented negative result rather than a native shim pretending to be managed execution.

## Provisional first-image path

```text
UEFI firmware
  -> minimal guideXOS-owned PE loader
  -> NativeAOT PE validation and relocation
  -> platform/runtime dependency resolution (not yet available)
  -> managed ManagedMain entry
  -> deterministic serial marker
```

The first milestone deliberately excludes the desktop, filesystem, networking, allocation, exceptions, threading, synchronization, reflection, dynamic loading, and broad framework compatibility.

## NativeAOT feasibility documents

- [Build and toolchain record](docs/BUILD_TOOLCHAIN_RECORD.md)
- [NativeAOT artifact anatomy](docs/NATIVEAOT_ARTIFACT_ANATOMY.md)
- [Dependency census](docs/DEPENDENCY_CENSUS.md)
- [Image-format decision](docs/IMAGE_FORMAT_DECISION.md)
- [Boot ABI](docs/BOOT_ABI.md)
- [Managed-entry proof procedure](docs/MANAGED_ENTRY_PROOF.md)
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

`D:\dev\guideXOS_NET10` is the only writable repository for this effort. Legacy, UEFI, and Server are read-only code vaults. Audit documents distinguish confirmed observations from recommendations; a recommendation is not evidence that the corresponding implementation already works.

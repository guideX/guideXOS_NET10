# guideXOS_NET10

Experimental guideXOS reimagining on .NET 10 NativeAOT.

This repository is intentionally starting as an archaeology and bootstrap project. It is not a direct upgrade of `guideXOS` Legacy, and it does not replace `guideXOSUEFI` or guideXOS Server. Those repositories remain active references and experiments.

## Current state

The local checkout was empty at the start of the audit:

- branch: `main`
- commits: none (`HEAD` is unborn)
- tracked files: none
- untracked files: none before this audit
- configured remote: `git@github.com:guideX/guideXOS_NET10.git`
- remote heads: none observed through a read-only HTTPS `ls-remote` probe
- SSH connectivity: not verified because the local SSH client rejected the GitHub host key

This audit adds documentation only. No solution, project, bootloader, runtime, or kernel source has been copied into the new repository yet.

## Intended first milestone

```text
UEFI firmware
  -> guideXOS-owned bootloader
  -> native bootstrap
  -> .NET 10 NativeAOT module/runtime initialization
  -> managed KernelMain entry
  -> one deterministic serial or framebuffer diagnostic
```

The first milestone deliberately excludes the desktop, filesystem, networking, full GC, and application loading.

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

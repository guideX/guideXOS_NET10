# Image-format decision

Status: **provisional**. This is a decision for the first controlled handoff experiment, not a permanent guideXOS image ABI.

## Options considered

| Option | Evidence | Current assessment |
| --- | --- | --- |
| Direct NativeAOT PE/COFF loading | Standard `win-x64` NativeAOT emits PE32+. The shared form exports `ManagedMain`, contains relocations, and can be parsed/relocated by a small UEFI harness. | Best current experiment because every transformation is visible and the loader can fail closed. Not yet freestanding because imports/runtime startup remain unresolved. |
| Historical PE-to-ELF conversion | The read-only UEFI evidence contains a prior converter and boot flow. It was designed for an older runtime and was not assumed correct for .NET 10. | Not adopted. No conversion is allowed to silently discard PE imports, TLS, unwind, metadata, BSS, or relocations. |
| Direct ELF NativeAOT | The Windows SDK invocation with `-r linux-x64` stopped with `Cross-OS native compilation is not supported`; no Linux build environment was available in this checkout. | Not proven in this environment. Revisit only with an independently pinned Linux toolchain. |
| Flat/custom image | Could avoid a general executable loader but would require a new permanent ABI and an understood treatment for relocations, data, unwind, TLS, metadata, and runtime initialization. | Premature and likely to hide the same dependencies. |

## Current provisional path

```text
NativeAOT shared PE/COFF DLL
  -> copy byte-for-byte into the ESP
  -> validate PE32+, sections, BSS extent, relocations, and ManagedMain export
  -> apply PE DIR64 relocations into an allocated image
  -> inspect import descriptors
  -> stop before managed transfer while imports are nonzero
```

The direct PE path is deliberately a loader experiment, not a claim that PE is the final guideXOS kernel image format. The next positive experiment must either supply a real, narrowly scoped runtime/platform contract or prove that a different NativeAOT composition has no such imports.

## Independent verification

[Inspect-PE.ps1](../tools/Inspect-PE.ps1) produces a machine-readable manifest containing:

- PE/COFF machine, image base, entry RVA, subsystem, file/section alignment, and image/header sizes;
- section names, virtual/file extents, raw offsets, characteristics, and R/W/X permissions;
- all data directories, including import, export, TLS, exception, load-config, IAT, and base relocation directories;
- all import DLLs and symbols;
- all named exports and RVAs;
- relocation block, entry, and type counts;
- NativeAOT map tokens and hashes when the map XML is supplied.

[Compare-PEManifests.ps1](../tools/Compare-PEManifests.ps1) fails if important sections, data directories, imports, exports, relocations, entry information, or the `ManagedMain` export differ. [Build-Gate3Evidence.ps1](../tools/Build-Gate3Evidence.ps1) ran it against the original shared artifact and the ESP-staged payload and produced `GATE3_COMPARISON=PASS` with identical SHA-256 values.

## Loader transformations verified in Gate 4

The UEFI loader in [gate4_loader.c](../src/Gate4Harness/gate4_loader.c) performs and reports these transformations:

1. Reads DOS and PE32+ headers from the file.
2. Allocates `SizeOfImage` pages through UEFI Boot Services.
3. Zeros the entire virtual image, which gives the `.data` virtual tail its required BSS behavior.
4. Copies headers and each section’s raw bytes to its virtual address.
5. Applies every PE `DIR64` relocation when the actual allocation differs from the preferred base.
6. Finds the exact `ManagedMain` export and reports its RVA.
7. Counts import descriptors and stops with `GXOS_NET10:GATE4_BLOCKED_IMPORTS` when any remain.

The loader does not yet apply page permissions, initialize the PE TLS directory, register `.pdata` unwind information, run DLL/module initialization, resolve imports, or call the managed export. These omissions are explicit blockers, not silent converter behavior.

## Determinism

The `/Brepro` executable repeated with identical SHA-256. The shared artifact used by Gate 4 has SHA-256 `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`; the staged payload has the same hash. Gate 4 run directories contain a fresh variables copy and fresh serial/stdout/stderr logs for each QEMU process.

## Decision boundary

Do not promote direct PE to a durable OS ABI until a positive managed-entry run exists and the loader has an explicit policy for imports, TLS, unwind, module initialization, and runtime state. Do not revive the historical converter merely because it produced a bootable older image.

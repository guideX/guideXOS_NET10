# Audit Evidence Ledger

This ledger records the concrete local evidence used by the architecture documents. Paths are relative to each named repository unless stated otherwise. Reference repositories are read-only and were not changed by this task.

## Repository identity

| Repository | Local path | Branch / HEAD observed | Remote observed | Working tree at audit |
|---|---|---|---|---|
| New target | `D:\dev\guideXOS_NET10` | `main`, unborn `HEAD` | `origin git@github.com:guideX/guideXOS_NET10.git` | Initially empty except `.git`; now documentation is untracked |
| Legacy | `D:\dev\guideXOS` | `main`, `4ff1ac4026deeb0799a6b66df3d9998a2438d46b` | `github git@github.com:guideX/guideXOS-Legacy.git`; `origin https://gitlab.com/guideX/guideXOS` | Clean |
| UEFI | `D:\dev\guideXOSUEFI` | `main`, `65178e81d8eb06ffc146185f7dd440a0f00c89b9` | `origin git@github.com:guideX/guideXOS-Legacy-UEFI.git` | Clean |
| Server | `D:\dev\guideXOSServer` | `main`, `4933cd142781473c2317ed926124c088aa0cbbdc` | `origin git@github.com:guideX/guideXOS-Server.git` | Pre-existing modifications and deletion; preserved |

The target remote URL matches the requested GitHub repository. An SSH `ls-remote` was blocked by local host-key verification, and an HTTPS `ls-remote` returned no heads during the audit. Therefore the configured URL is confirmed, but remote branch existence was not independently verified.

## Target inventory evidence

- Before this task, `rg --files` in the target returned no working-tree files.
- `git status --short --branch` initially reported `No commits yet on main...origin/main [gone]`.
- No solution, project, source, asset, or build files existed initially.
- Final target inventory consists of `README.md` and the documents under `docs/` listed in the root README.
- Final checks found no trailing whitespace. No commit, stage, push, reset, clean, or branch switch was performed.

## Legacy evidence

- Solution: `guideXOS.Legacy.sln`.
- Shared project files: `Kernel/Kernel.shproj` and `Corlib/Corlib.shproj`.
- Managed kernel/application project: `guideXOS/guideXOS.csproj`.
- Current project settings include `TargetFramework=net9.0`, `RuntimeIdentifier=win-x64`, `IlcSystemModule=guideXOS`, `EntryPointSymbol=Entry`, custom NativeAOT reference pruning, `/fixed`, a fixed image base, and a map file.
- `Tools/EntryPoint.asm` is a Multiboot/NASM 32-bit-to-long-mode loader with an initial identity map and a jump to the native entry.
- `Kernel/Misc/EntryPoint.cs` exports `Entry`, initializes modules and low-level kernel services, and calls `KMain`.
- `guideXOS/Program.cs` provides the managed `KMain` export and starts the desktop/application path.
- `BuildISO` creates a ramdisk, assembles a loader/trampoline, concatenates native payload pieces, and builds a GRUB ISO.

CoreLib history evidence includes the initial Corlib addition at `5af2040`, a broad Corlib edit snapshot at `d7deb29` on 2024-08-04, added I/O helper files at `9966778`, their removal and additional runtime edits at `7439ab4` on 2025-11-30, and an App Model project migration at `4ff1ac4`.

## UEFI evidence

- Solution: `guideXOS.sln`.
- Native loader project: `guideXOSBootLoader/guideXOSBootLoader.vcxproj`.
- Managed project targets `net7.0`, uses the same `7.0.0-alpha.1.22074.1` ILCompiler package, exports `KMainWrapper`, and links with a fixed base/map configuration.
- `guideXOSBootLoader/main.cpp` loads `kernel.elf`, obtains GOP and optional ramdisk data, allocates a stack/trampoline, builds mappings, retries `ExitBootServices`, validates/fills BootInfo, and transfers through the handoff trampoline.
- `guidexOSBootInfo.h` defines the packed v1 `GXBI` record with magic, version, checksum, memory-map, framebuffer, ACPI, ramdisk, and optional firmware-input fields.
- `Kernel/Misc/EntryPoint.cs` accepts `UefiBootInfo*`, emits raw serial/framebuffer markers, validates BootInfo, performs an allocation probe, and initializes selected managed kernel services.
- Commit `2847cfc` documents a working loader/trampoline path and staged markers while also recording that managed module initialization and managed page-table initialization were not yet working.
- `build_step.log` records NativeAOT PE output, map-file entry lookup, PE-to-ELF conversion, ESP placement, and QEMU launch steps.

## Server evidence

- The bootloader is a modified UEFI descendant; normalized comparison showed 77 changed files against UEFI, with `fb_console.cpp` and `trampoline.asm` unchanged in that comparison.
- `build-uefi.ps1` builds the UEFI loader and a freestanding C++ kernel, then places `BOOTX64.EFI` and `kernel.elf` on the ESP.
- The current managed-facing project targets `net9.0` but is a hosted shell/project rather than proof of a managed NativeAOT kernel.
- `build-native-experimental.bat` gates the native ELF experiment with `GX_ENABLE_EXPERIMENTAL_NATIVE_ELF_EXECUTION`.
- The experimental loader accepts static amd64 `ET_EXEC` images without `PT_INTERP`, dynamic linking, or relocations and requires `guidexos-c-abi-v1`.
- `app_manifest.h`, `app_launch_target.h`, `docs/GXAPP_FORMAT_SPEC.md`, and `docs/SDK_ARCHITECTURE.md` provide the App Model and package evidence summarized in `APP_MODEL_DIRECTION.md`.

## Toolchain evidence

- Local SDK: `dotnet 10.0.302`; installed shared runtimes include .NET 10.0.10.
- No `Microsoft.NETCore.App.Runtime.AOT.win-x64` pack was found under `C:\Program Files\dotnet\packs`.
- Local package evidence includes `Microsoft.DotNet.ILCompiler` `7.0.0-alpha.1.22074.1` and a matching `runtime.win-x64` package; the alpha package nuspec identifies runtimelab commit `04b092003db5ba207d4fa3f3becb7f01828bf16c`.
- The local NuGet cache contained .NET 7 alpha and .NET 9 ILCompiler/runtime packages, but no .NET 10 ILCompiler package.
- No complete `System.Private.CoreLib` source tree, CoreCLR/runtime source copy, linker descriptor set, or dedicated patch directory was found in Legacy or UEFI.
- Both Corlib shared projects contain 119 files and are curated source subsets rather than complete upstream CoreLib trees.

## Interpretation rule

The documents distinguish observed code/history from recommendations. A recommendation is not evidence that a subsystem is production-ready. In particular, a successful old marker, a copied runtime helper, or a package-format document is not treated as proof of .NET 10 compatibility.

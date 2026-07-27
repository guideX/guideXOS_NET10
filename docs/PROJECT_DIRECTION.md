# Project direction

Audit date: 2026-07-27

## Executive conclusion

`guideXOS_NET10` should be a clean, x64-first NativeAOT platform experiment with explicit contracts between four layers:

1. UEFI bootloader and boot contract.
2. Tiny native bootstrap and architecture-specific ABI glue.
3. .NET 10 NativeAOT runtime/CoreLib integration.
4. Managed kernel and, later, OS/App Model services.

The older repositories prove useful behaviors and several pieces of boot engineering, but they do not provide a drop-in .NET 10 runtime. Legacy and UEFI each compile a curated CoreLib-shaped source subset with a modified 7.0-alpha ILCompiler package. That is valuable archaeology, not a safe baseline for a new runtime.

## Confirmed repository map

### Legacy C#

`D:\dev\guideXOS` is a large shared-project solution:

- `guideXOS.Legacy.sln` contains the managed executable, shared `Kernel` and `Corlib` projects, and application packaging extras.
- `guideXOS\guideXOS.csproj` currently targets `net9.0`, but references `Microsoft.DotNet.ILCompiler` version `7.0.0-alpha.1.22074.1` from the repository package directory.
- `Kernel\Kernel.projitems` and `Corlib\Corlib.projitems` are imported into the managed project.
- `GXM.Apps`, `Ramdisk`, `Tools`, `Scripts`, and `Extras` contain applications, assets, image creation, and host utilities.
- Boot is primarily GRUB/Multiboot. `Tools\EntryPoint.asm` creates the Multiboot header, switches to long mode, establishes initial identity mappings, and jumps to the NativeAOT image.

### UEFI C# experiment

`D:\dev\guideXOSUEFI` keeps the same managed shared-project structure but adds a native UEFI path:

- `guideXOS.sln` contains the managed project, shared `Kernel`/`Corlib`, and `guideXOSBootLoader\guideXOSBootLoader.vcxproj`.
- `guideXOS\guideXOS.csproj` targets `net7.0`, uses the same 7.0-alpha ILCompiler package, and exports `KMain`.
- `guideXOSBootLoader` is a freestanding MSVC UEFI application with an ELF64 loader, GOP/framebuffer support, memory-map capture, page-table construction, diagnostics, and a handoff trampoline.
- `build.ps1` publishes the managed project as a NativeAOT PE, converts the PE to ELF64 using a map-file-selected `KMain` entry, and assembles `ESP\EFI\BOOT\BOOTX64.EFI` plus `ESP\kernel.elf`.
- The UEFI kernel entry contains explicit staged diagnostics for framebuffer, allocator, module/static initialization, and managed allocation.

### Server C++/Native ELF experiment

`D:\dev\guideXOSServer` is a separate, currently dirty checkout. Its current tree is primarily a freestanding C++ kernel plus a hosted .NET 9 shell/project and an experimental hosted Native ELF runtime. It is not evidence of a finished modern .NET NativeAOT kernel pipeline.

- `guideXOSBootLoader` is a modified descendant of the UEFI bootloader.
- `kernel\Makefile` and `kernel\arch\amd64` build a freestanding native kernel and ELF image.
- The hosted experiment validates a narrow static amd64 ELF ABI and a versioned native app host-call surface.
- The App Model has useful manifest, launch-target, and `.gxapp` format work, but hosted and bare-metal launch paths remain separate.

## Architectural recommendations

### Keep the first boundary small

The initial public boot contract should be a versioned, packed `BootInfo` record containing only the data that must survive `ExitBootServices`: memory map, framebuffer, ACPI pointer, ramdisk, payload range, and boot flags. Firmware protocol pointers must not be treated as valid after `ExitBootServices`.

### Treat CoreLib as a runtime product, not a source folder

The new project should start from the .NET 10 NativeAOT/ILCompiler/runtime-pack contract and add guideXOS-specific runtime hooks only where a proof requires them. The old 119-file source subset should be mined for intent and tests, not copied wholesale.

### Establish an ABI before an App Model

The future App Model should expose stable, target-neutral abstractions over a private kernel/runtime implementation. A small C-compatible host ABI should underlie managed wrappers and native payloads. Application identity and lifecycle should be designed before UI or package-manager ports.

### Defer project fragmentation until the first build is proven

The recommended future shape is:

```text
src/
  Bootloader/       native UEFI application
  NativeBootstrap/  tiny post-firmware handoff and ABI glue
  CoreLib/          .NET 10 runtime source overlays, only when required
  Runtime/          guideXOS NativeAOT/runtime integration
  Kernel/           managed kernel entry and early services
  OS/               later OS services and device abstractions
  AppHost/          later application host
sdk/
  AppModel.Abstractions/
  AppModel.SDK/
apps/
  Diagnostics/
tools/
  ImageBuilder/
docs/
```

For the next implementation turn, only `Bootloader`, `NativeBootstrap`, `Runtime`, `Kernel`, one diagnostics app, and the image tool need concrete projects. `OS`, `AppHost`, and the full SDK should wait until the managed entry proof is repeatable.

## Confirmed versus recommended

Confirmed: the older UEFI experiment has reached a managed `KMain` path in historical build evidence and has extensive staged diagnostics.

Recommended: rebuild that proof around a new .NET 10 toolchain and a clean boot contract, because the old path depends on a modified 7.0-alpha package, a PE-to-ELF conversion step, fixed address assumptions, and partially stubbed runtime services.


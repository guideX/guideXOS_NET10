# NativeAOT and ILCompiler strategy

## Existing production of native code

### Legacy

Confirmed from `guideXOS\guideXOS.csproj`, `Scripts\build.ps1`, `Tools\EntryPoint.asm`, and the local build targets:

1. The managed project targets `net9.0` today but references `Microsoft.DotNet.ILCompiler` `7.0.0-alpha.1.22074.1`.
2. The runtime identifier is `win-x64`, and the linker is configured with a fixed image base and `EntryPointSymbol=Entry`.
3. `dotnet publish` produces a NativeAOT Windows PE image.
4. NASM creates a GRUB/Multiboot loader object. The build concatenates loader and NativeAOT output into `Tools\grub2\boot\kernel.bin` and creates a hybrid ISO.
5. The native image is consumed in the old boot path as a PE/COFF-shaped NativeAOT artifact, not as a clean ELF kernel contract.

### UEFI

Confirmed from `guideXOSUEFI\guideXOS.csproj`, `build.ps1`, `convert_kernel_manual.ps1`, `check_elf*.py`, and `build_step.log`:

1. The managed project targets `net7.0`, uses the same 7.0-alpha ILCompiler package, and exports `KMain`.
2. `dotnet publish` produces `guideXOS.exe` under the NativeAOT output directory.
3. `build.ps1` uses `Kernel.map` to find `KMain`, converts the PE image into ELF64, and places it at `ESP\kernel.elf`.
4. The UEFI bootloader loads ELF64 `PT_LOAD` segments, maps virtual addresses to allocated physical memory, exits boot services, and jumps to the selected virtual entry.
5. The historical build log records the sequence: native stubs, NativeAOT PE, map-selected `KMain`, PE-to-ELF conversion, `BOOTX64.EFI`, and `kernel.elf`.

### Server

The Server checkout does not currently prove a modern .NET NativeAOT kernel pipeline. `guideXOSServer.csproj` is a .NET 9 hosted project. The proven modern native work is a freestanding C++ kernel and an experimental hosted Native ELF application runtime. That work is valuable for ABI gates, ELF validation, executable-memory policy, lifecycle diagnostics, and app host calls, but it is not a replacement for the .NET 10 compiler/runtime integration requested here.

## Current versions and formats

| Area | Existing evidence | Assessment |
|---|---|---|
| .NET target | Legacy `net9.0`; UEFI `net7.0`; Server host `net9.0` | None is the new target; use installed .NET SDK `10.0.302` as the development baseline. |
| ILCompiler | `7.0.0-alpha.1.22074.1` local package | Old runtimelab snapshot; modified package. Retire for .NET 10. |
| Runtime pack | Local `runtime.win-x64.Microsoft.DotNet.ILCompiler` package | Contains broad framework DLLs plus NativeAOT build/runtime payloads; it is not a guideXOS-specific source runtime. |
| Legacy output | NativeAOT PE/COFF plus Multiboot loader concatenation | Tightly coupled to GRUB and fixed-image assumptions. |
| UEFI output | NativeAOT PE converted to ELF64 | Proven experiment, but conversion is a compatibility seam that must be revalidated for .NET 10. |
| Server native output | Freestanding C++ ELF/PE build and static ELF app loader | Strong native ABI evidence, not managed NativeAOT evidence. |
| ABI | UEFI bootloader and NativeAOT Windows build use MS x64 register conventions; Server's Windows MinGW kernel also uses the platform ABI | Make ABI explicit in the new native bootstrap; do not assume an ELF file implies SysV calling convention. |

## Recommended .NET 10 approach

Use the current .NET 10 NativeAOT/ILCompiler and runtime-pack targets as the source of truth. Pin the exact package/runtime-pack version during implementation, keep it in a central package-management file, and record the SDK and package hashes in the build evidence.

The initial work should be an isolated `ManagedEntryProbe` with:

- one `UnmanagedCallersOnly` or compiler-supported exported function;
- no desktop framework, networking, filesystem, or dynamic loading;
- an explicit native entry symbol and a controlled linker configuration;
- a freestanding native dependency set;
- a serial-only diagnostic before any managed allocation;
- separate probes for module initialization, static constructors, strings/value types, and the first allocation.

Do not begin by forking ILCompiler. First determine whether the .NET 10 package can be used with a custom runtime pack, a custom native bootstrap, or an ELF conversion step. Forking is justified only when a required bare-metal contract cannot be expressed through the current compiler/runtime-pack extension points and a minimal reproducer has been recorded.

## Smallest credible path to managed entry

1. Build a freestanding UEFI bootloader that can print serial diagnostics and load a static ELF test payload.
2. Replace the test payload with a native bootstrap object that owns the post-`ExitBootServices` stack and calls a defined payload entry.
3. Publish a tiny .NET 10 NativeAOT module with all host dependencies visible in the link map.
4. Either make the compiler produce the chosen payload format directly or keep the PE-to-ELF conversion as a narrowly tested, temporary tool. Do not hide format conversion inside the bootloader.
5. Define `KernelMain(BootInfo*)` as an exported, versioned managed entry only after the first no-allocation export is stable.

## Runtime choices that must be explicit

- GC: first allocation proof may use a minimal runtime allocation path; full GC is out of scope for milestone one.
- Exceptions: early panic/fail-fast is separate from ordinary managed exception semantics.
- P/Invoke: use a small, versioned host ABI rather than the Legacy `int 0x80` placeholder.
- Static initialization: prove generated module/cctor support instead of copying Legacy `InitializeModules`.
- Threading: no scheduler or monitor implementation is required for the first entry.
- Native libraries: default Windows, libc, compression, and desktop libraries must not leak into the bare-metal module.


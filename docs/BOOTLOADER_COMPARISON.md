# Bootloader comparison

## Common ancestry

The UEFI C# repository introduced the freestanding `guideXOSBootLoader` project. Server contains a modified descendant of that directory. A normalized comparison found the same broad `main.cpp`, `elf.cpp`, paging, framebuffer, BootInfo, and trampoline design, but also significant divergence:

- Server removes the UEFI boot-splash sources and adds PCI/NIC discovery.
- Server modifies `main.cpp`, `elf.cpp`, `paging.cpp`, `guidexOSBootInfo.h`, `bootinfo.h`, diagnostics, and the handoff assembly.
- `fb_console.cpp` and `trampoline.asm` are shared at the byte level in the checked-out trees.
- UEFI includes the richer managed-kernel test path; Server is paired with a native C++ kernel and current App Model work.

Legacy is the older GRUB/Multiboot path, not the ancestor of the current UEFI implementation in the sense of a shared executable. Its `Tools\EntryPoint.asm` is still useful for the x86 long-mode and initial page-table history.

## Comparison

| Capability | Legacy | UEFI C# | Server | Assessment |
|---|---|---|---|---|
| Firmware entry | GRUB/Multiboot binary loader | `efi_main` MSVC UEFI application | `efi_main` descendant | UEFI/Server are the relevant donors. |
| Startup format | Flat loader plus NativeAOT PE/COFF | ELF64 kernel loaded from the ESP after PE-to-ELF conversion | Native ELF kernel built by Makefile | Server has the cleanest native ELF input; UEFI has the managed conversion proof. |
| Memory map | Multiboot memory info | `GetMemoryMap` with growth retry and `ExitBootServices` retry | Same design, modified | UEFI/Server are stronger; keep the retry algorithm and simplify it behind a testable interface. |
| `ExitBootServices` | Not applicable | Stable EfiLoaderData memory-map buffer and no firmware calls after exit | Same contract, with more native-kernel mapping | UEFI path is the strongest managed evidence; Server is the stronger native-kernel donor. |
| Framebuffer | Multiboot VBE/VBEInfo and direct framebuffer | GOP, pitch/format in `BootInfo`, uncached mapping, staged pixels | Same GOP/identity-map path, plus native kernel use | UEFI and Server share the strongest framebuffer foundation. |
| ELF loading | None in the normal path | Validates ELF64, loads `PT_LOAD`, checks entry, maps virtual range | Same lineage and native ELF consumer | Server’s native ELF packaging is simpler; reuse UEFI loader logic with a new contract. |
| PE/NativeAOT loading | NativeAOT PE is concatenated behind loader | NativeAOT PE converted to ELF64 by build tooling | Not a managed NativeAOT loader | The conversion must be a temporary, independently tested tool or replaced. |
| Diagnostics | Assembly/serial and kernel console | Serial, framebuffer stage markers, BootInfo/checksum diagnostics, managed `KMain` markers | Serial/framebuffer diagnostics, native-kernel logs | UEFI has the strongest managed-entry diagnostics. |
| Handoff ABI | Legacy C# export `Entry(MultibootInfo*, ...)` | `KMain(UefiBootInfo*)` via MS x64 trampoline | Native `kernel_main`/BootInfo contract | New project must define one versioned ABI rather than inherit both. |
| Coupling | GRUB addresses, VBE, ramdisk layout | UEFI file names, fixed allocator/stack ranges, PE-to-ELF map selection | Native kernel layout, PCI/NIC assumptions | All require extraction; none should be copied wholesale. |

## Reuse recommendation

Use a new clean interface assembled from selected pieces:

- donor for UEFI protocol handling, ELF `PT_LOAD` loading, page-table construction, stable memory-map capture, and post-EBS diagnostics: UEFI C#;
- donor for native ELF packaging, BootInfo use by a freestanding native kernel, PCI/MMIO mapping, and current diagnostic discipline: Server;
- donor for historical Multiboot and long-mode assembly only: Legacy.

Do not copy either existing bootloader directory as the initial implementation. Extract a small `BootInfo v1` contract and build a minimal loader around it. Keep bootloader-side UEFI types and post-EBS kernel-visible types in separate headers. The kernel must not dereference firmware protocol pointers after `ExitBootServices`.

## Known hazards to carry forward explicitly

- The UEFI code has both a legacy `BootInfo` and a newer v1 structure; the new project should have one canonical structure.
- The UEFI code maps fixed ranges such as the allocator region and low memory; these are proof conveniences, not a portable memory manager.
- The UEFI code contains a current path that skips BootInfo validation in Server-era revisions; the new loader must validate the contract before handoff.
- A PE-to-ELF conversion changes file format but does not automatically reconcile object layout, relocations, imports, or calling convention. The conversion must be covered by inspection tests.
- The handoff trampoline is position- and stack-sensitive. It must be kept small, assembled once, and tested with the exact compiler ABI.


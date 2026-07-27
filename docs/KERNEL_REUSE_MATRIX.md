# Kernel reuse matrix

Legend: `Copy nearly unchanged` means the code is mostly policy-independent; `Adapt` means retain algorithms but replace contracts; `Reimplement` means preserve behavior while changing architecture; `Defer` means it should not influence the first managed-entry proof; `Retire` means the old assumption is actively harmful.

| Subsystem | Best donor | Strategy | Evidence and reason |
|---|---|---|---|
| CPU/long-mode initialization | UEFI bootloader plus Legacy `Tools\EntryPoint.asm` | Adapt | Bootloader owns firmware-to-long-mode transition; managed kernel must receive a stable post-EBS state. |
| BootInfo and memory map | UEFI `main.cpp`, `guidexOSBootInfo.h`, `ExitBootServicesWithMemoryMapInBuffer` | Reimplement behind a new contract | This is the strongest memory-map/EBS evidence, but the existing record has legacy fields and assumptions. |
| Physical allocator | UEFI/Legacy `Kernel\Misc\Allocator.cs` | Adapt after entry proof | The allocator is substantial and tagged, but initializes at fixed addresses and assumes mapped space. First use a bounded boot allocator. |
| Virtual memory/page tables | UEFI bootloader `paging.cpp`; managed `Kernel\Misc\PageTable.cs` | Adapt/reimplement | Bootloader identity maps the handoff ranges and maps kernel virtual addresses. Managed PageTable allocation depends on the old runtime allocator. |
| Interrupt descriptor table | Legacy/UEFI `Kernel\Misc\IDT.cs` | Adapt | The IDT code is an x64 managed implementation with exported interrupt handlers, but it assumes the old runtime and global state. |
| GDT and kernel stack | Legacy/UEFI `Kernel\Misc\GDT.cs`, `EntryPoint.cs` | Adapt | The setup sequence is useful after managed entry, not before it. |
| Exceptions/panic | Legacy `IDT.cs`, `Panic.cs`, UEFI serial/framebuffer diagnostics | Reimplement | Early panic must be allocation-free and independent of firmware services. Preserve deterministic output, not the old exception routing. |
| APIC/PIC/interrupt routing | UEFI/Legacy `LocalAPIC.cs`, `IOAPIC.cs`, `PIC` paths | Defer, then adapt | It is not needed for a single deterministic managed entry and requires stable page tables and interrupt handlers. |
| Timer | UEFI/Legacy `Timer.cs`, `LocalAPICTimer.cs`, `ACPITimer.cs` | Defer | Keep the API requirement in the architecture notes; do not bring timer interrupts into milestone one. |
| Scheduler | Legacy `Kernel\Misc\Threading.cs`, `SchedulerExtensions.cs`; Server `scheduler.cpp` for behavior comparison | Reimplement | The managed implementation is tied to the old stack/IDT/runtime, while Server offers policy vocabulary but not a managed implementation. |
| Threads | Legacy `Threading.cs`, `Process.cs` | Defer | Thread creation and context switching depend on runtime allocation, interrupts, and a proven ABI. |
| Synchronization | Legacy `Threading.cs` plus CoreLib `Monitor.cs` | Reimplement | CoreLib synchronized-method helpers are stubs; no existing implementation is safe to copy as the runtime primitive. |
| Input | UEFI `Kernel\Drivers\Input\*`; Legacy PS/2/USB drivers | Adapt behind providers | UEFI adds explicit post-EBS capability rules; old drivers directly touch ports/devices and global mouse state. |
| Storage/block devices | Legacy `Kernel\Drivers`, Server `kernel\core\block_device.cpp`/`ata.cpp`/`nvme.cpp` | Defer, then reimplement | Server has broader native coverage; neither path is relevant before managed entry. |
| Filesystem | Legacy FAT/EXT2/NTFS; Server FAT/EXT4/NTFS/XFS/UFS/VFS | Reimplement from behavior | Preserve mount, read, and path semantics, but establish an OS service boundary instead of importing direct disk globals. |
| Display/framebuffer | UEFI GOP/`fb_console.cpp` and managed `Framebuffer.cs` | Adapt | UEFI is strongest for GOP metadata, pitch, and post-EBS mapping; managed drawing can be ported later. |
| Debug output | UEFI `debug_helpers.h`, `fb_console.cpp`, `BootConsole`; Legacy `Console.cs` | Copy protocol, reimplement implementation | Make serial output available before runtime initialization; framebuffer output is a second channel. |
| NativeAOT runtime hooks | Legacy/UEFI `Corlib\Internal\Runtime\CompilerHelpers` | Retire as implementation; preserve as requirements | These hooks use old package symbols, direct object layout, and stubs. Rebuild for .NET 10 only after symbol-level proofs. |
| ACPI | UEFI bootloader RSDP handoff plus managed `ACPI` | Adapt | Bootloader-provided RSDP is more reliable than legacy scanning after EBS. |
| SMP | Legacy `SMP.cs` and trampoline | Defer | No need for multiple CPUs in the first entry milestone. |

## Kernel implementation order

1. BootInfo reader and allocation-free serial diagnostic.
2. Runtime/bootstrap initialization and managed entry.
3. Bounded allocation proof.
4. Early panic/exception contract.
5. Framebuffer view from BootInfo.
6. Physical/virtual memory services.
7. Interrupts and timer.
8. Scheduler and synchronization.
9. Input, storage, and filesystem.


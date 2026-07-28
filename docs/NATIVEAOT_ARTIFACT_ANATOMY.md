# NativeAOT artifact anatomy

The first artifact is ordinary .NET 10 NativeAOT output. It is a PE32+ image, not an ELF image and not a flat freestanding blob.

## Managed entry source

`ManagedMain(nint)` is marked `[UnmanagedCallersOnly(EntryPoint = "ManagedMain")]`. It validates a raw boot pointer, builds the marker in a `stackalloc` buffer, calls the raw serial callback, and returns `0`. The source has no managed strings, explicit allocation, exception handling, reflection, dynamic loading, threading, synchronization, file I/O, networking, or host console API.

The `Main` method exists only so the executable form remains independently runnable. The shared NativeAOT form exports `ManagedMain`; the executable form does not expose it in its export table.

## PE headers and sections

The shared artifact is `PE32+`, machine `0x8664`, image base `0x180000000`, section alignment `0x1000`, file alignment `0x200`, image size `0xD3000`, and header size `0x400`. The six sections are:

| Section | RVA | Virtual size | File offset | Raw size | Permissions |
| --- | ---: | ---: | ---: | ---: | --- |
| `.text` | `0x1000` | 507,832 | `0x400` | 507,904 | RX |
| `.rdata` | `0x7D000` | 183,516 | `0x7C400` | 183,808 | R |
| `.data` | `0xAA000` | 123,136 | `0xA9200` | 4,096 | RW |
| `.pdata` | `0xC9000` | 28,740 | `0xAA200` | 29,184 | R |
| `.rsrc` | `0xD1000` | 1,598 | `0xB1400` | 2,048 | R |
| `.reloc` | `0xD2000` | 1,124 | `0xB1C00` | 1,536 | R |

The `.data` virtual extent is much larger than its raw extent. A loader must zero the remainder; copying only raw bytes is incorrect BSS behavior. The `.pdata` directory is the Windows x64 unwind-function table. The `.reloc` directory contains ten blocks and 522 entries: 515 `DIR64` entries and seven no-op entries.

The executable form has image base `0x140000000`, entry RVA `0x77B30`, subsystem `3` (Windows CUI), and image size 872,448. Its entry point is NativeAOT/Windows startup, not `ManagedMain`.

## Data directories

The shared PE has nonzero directories for export, import, resource, exception, base relocation, debug, TLS, load configuration, and IAT. The NativeAOT map also contains `ManagedMain`, `ModuleInitializerList`, `RuntimeConfigurationBlob`, `FieldRvaData`, `GCStatics`, `NonGCStatics`, `MethodExceptionHandlingInfo`, and `ThreadStatic` metadata references.

The export table contains exactly:

```text
ManagedMain  RVA 0x24724
```

The PE entry-point RVA is the DLL startup entry supplied by the NativeAOT bootstrap objects. It must not be confused with the exported managed method RVA. The Gate 4 loader intentionally locates the export and does not call it until its import/runtime preconditions are satisfied.

## Imports

The shared PE has ten import descriptors:

```text
ADVAPI32.dll
bcrypt.dll
KERNEL32.dll
ole32.dll
api-ms-win-crt-math-l1-1-0.dll
api-ms-win-crt-string-l1-1-0.dll
api-ms-win-crt-convert-l1-1-0.dll
api-ms-win-crt-stdio-l1-1-0.dll
api-ms-win-crt-runtime-l1-1-0.dll
api-ms-win-crt-heap-l1-1-0.dll
```

These imports are physically represented in the PE IAT. They are not supplied by UEFI and are not resolved by the current harness. The complete per-DLL symbol list is machine-readable in the Gate 3 manifest produced by [Inspect-PE.ps1](../tools/Inspect-PE.ps1).

## NativeAOT composition and startup

The ILC response includes standard runtime-pack inputs and `--initassembly` entries for `System.Private.CoreLib`, `System.Private.TypeLoader`, and `System.Private.Reflection.Execution`. The linker response includes `dllmain.obj`, `bootstrapperdll.obj`, `Runtime.WorkstationGC.lib`, disabled EventPipe/standalone-GC components, `aotminipal.lib`, compression/native support libraries, and Windows platform import libraries.

This explains why a tiny managed method still has runtime composition, module initialization records, exception/unwind metadata, TLS data, GC metadata, and platform imports. “No allocation in the method” does not mean “no runtime startup dependency.”

The managed method itself contains calls to NativeAOT reverse-P/Invoke and P/Invoke transition helpers around the managed-to-unmanaged callback. Those helpers are evidence that the first successful freestanding handoff must establish the runtime thread/transition state; directly jumping to the export has not yet been proven safe.

## Evidence

`tools\Inspect-PE.ps1` parses headers, sections, directories, imports, exports, relocations, hashes, and map metadata without relying on a Windows loader. [Build-Gate3Evidence.ps1](../tools/Build-Gate3Evidence.ps1) compares the source DLL with the exact payload staged in the ESP. The comparison passed with identical SHA-256 values and preserved the `ManagedMain` export, imports, directories, sections, and relocations.

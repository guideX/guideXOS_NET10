# NativeAOT artifact anatomy

The Gate 1 shared artifact is an ordinary .NET 10 NativeAOT PE32+ image, not an ELF image or flat freestanding blob. Gate 4 now loads it directly, applies relocations, patches all IAT slots, establishes the minimum one-thread NativeAOT transition state, and enters the exported method.

## Image facts

The shared artifact is machine `0x8664`, preferred image base `0x180000000`, section alignment `0x1000`, file alignment `0x200`, image size `0xD3000`, and header size `0x400`. Its six sections are:

| Section | RVA | Virtual size | File offset | Raw size | Permissions |
| --- | ---: | ---: | ---: | ---: | --- |
| `.text` | `0x1000` | 507,832 | `0x400` | 507,904 | RX |
| `.rdata` | `0x7D000` | 183,516 | `0x7C400` | 183,808 | R |
| `.data` | `0xAA000` | 123,136 | `0xA9200` | 4,096 | RW |
| `.pdata` | `0xC9000` | 28,740 | `0xAA200` | 29,184 | R |
| `.rsrc` | `0xD1000` | 1,598 | `0xB1400` | 2,048 | R |
| `.reloc` | `0xD2000` | 1,124 | `0xB1C00` | 1,536 | R |

The loader zeroes the complete virtual image before copying raw section data, preserving the `.data` BSS tail. The relocation directory has ten blocks and 522 entries: 515 `DIR64` entries and seven no-op entries. The PE has nonzero export, import, exception, TLS, load-config, IAT, debug, resource, and relocation directories.

## Managed export and startup path

The source declares `[UnmanagedCallersOnly(EntryPoint = "ManagedMain")] public static int ManagedMain(nint bootInfoAddress)`. The shared image exports exactly `ManagedMain` at RVA `0x24724`; the Gate 4 target is therefore `actual_image_base + 0x24724`. A representative allocation is:

```text
preferred image base: 0x0000000180000000
actual image base:    0x000000000547B000
export RVA:           0x0000000000024724
target VA:            0x000000000549F724
```

The PE entry RVA is `0x77700`. It is the NativeAOT DLL/CRT attach-detach path supplied by `dllmain.obj`/`bootstrapperdll.obj`, not the managed export. The loader does not pretend that its UEFI image is a Windows process, so it does not invoke that DLL lifecycle. It invokes the export only after resolving the IAT and setting up the state that the export thunk actually reads.

Disassembly of the export shows the relevant order:

```text
0x180024724  ManagedMain export thunk
0x180024724  call 0x1800337a0       reverse-P/Invoke/thread transition
0x1800337a0  read gs:0x58 and _tls_index; locate TLS block + 0x30
0x1800375d0  initialize one-thread NativeAOT state; uses FLS/identity/handles/stack/lock imports
...          validate GuideXBootInfo
0x180035eb0  publish reverse-P/Invoke frame
0x180024866  call rbx (the borrowed serial callback)
0x180035f00  cleanup transition state
...          return EAX=0
```

The generated export thunk does initialize reverse-P/Invoke state, but only after the caller supplies the TLS vector, TLS block, GS/TEB values, stack limits, and import targets that its first transition requires. No module initializer was needed by this exact probe: the successful run has no static field initializer, no module initializer body, no allocation, and no exception. The image still contains `ModuleInitializerList` and runtime metadata, so this conclusion is scoped to this artifact and probe, not to NativeAOT generally.

The loader creates a zeroed TLS vector and copies the PE TLS template. `_tls_index` is zero, `GS+0x58` points at the vector, and vector slot zero points at a TLS block whose `+0x30` is the NativeAOT thread-state storage. A loader-created TEB-like block at `GS+0x30` contains the active stack base and limit. GS and the interrupt flag are restored after the managed call. No functional GC heap, second thread, exception dispatcher, unwind registration, CRT termination, or process environment is claimed.

## Imports and thunks

The exact ten descriptors and 124 symbols, with IAT RVAs and per-symbol treatment, are in [DEPENDENCY_CENSUS.md](DEPENDENCY_CENSUS.md). The positive loader serializes:

```text
PE_IMPORT_DESCRIPTORS=10
PE_IMPORT_SYMBOLS=124
PE_IMPORT_RESOLVED=124
PE_IMPORT_FUNCTIONAL=18
PE_IMPORT_FAILFAST=106
UNRESOLVED_REQUIRED_IMPORTS=0
```

Each of the 106 unreachable symbols is patched to a guideXOS-owned stub that emits `GXOS_NET10:UNEXPECTED_IMPORT_CALL:<module>!<symbol>` and halts. This is deterministic failure, not a broad Windows compatibility layer. The 18 functional targets are the narrowly demonstrated FLS, current identity, pseudo-handle, bounded stack query, and one-thread critical-section operations required by the observed transition path.

## TLS, unwind, and initialization answers

- The direct export is safe only after PE relocation, complete IAT patching, and the bounded NativeAOT TLS/thread-state setup; “export exists” alone is insufficient.
- The export thunk performs reverse-P/Invoke initialization, but does not run the full CRT/DLL startup lifecycle.
- The PE TLS directory is honored for the template and `_tls_index`; its callback array is empty for this artifact.
- The current probe has no hidden user static constructor. NativeAOT runtime metadata remains present and later static-constructor experiments are separate blockers.
- A functional GC heap is not needed for this proven path because no object or array allocation is executed. This does not prove allocation or GC support.
- `.pdata` and exception metadata remain in the image, but no Windows unwind registration or exception path is implemented. The loader installs only bounded CPU fault diagnostics and halts on faults.
- The exact native call target is the relocated `ManagedMain` export VA, not PE entry RVA `0x77700`.

The current path is therefore a legitimate direct exported NativeAOT entry for this no-allocation artifact, with its required transition state explicitly supplied by the loader. It is not a general NativeAOT process-start contract.

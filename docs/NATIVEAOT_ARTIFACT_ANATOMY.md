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
PE_IMPORT_FUNCTIONAL=23
PE_IMPORT_FAILFAST=101
UNRESOLVED_REQUIRED_IMPORTS=0
```

Each of the 103 currently unreachable symbols is patched to a guideXOS-owned stub that emits `GXOS_NET10:UNEXPECTED_IMPORT_CALL:<module>!<symbol>` and halts. This is deterministic failure, not a broad Windows compatibility layer. The historical 18 functional targets are the narrowly demonstrated FLS, current identity, pseudo-handle, bounded stack query, and one-thread critical-section operations required by the observed transition path. The current allocation-startup build adds `GetSystemTimeAsFileTime`, `QueryPerformanceCounter`, and `QueryPerformanceFrequency`, for 21 functional / 103 fail-fast. The CRT opt-in adds `_initialize_onexit_table`, for 22 / 102, and the current SLIST opt-in adds `InitializeSListHead`, for 23 / 101. The historical 18/106, 19/105, and 21/103 counts remain audit evidence.

## TLS, unwind, and initialization answers

- The direct export is safe only after PE relocation, complete IAT patching, and the bounded NativeAOT TLS/thread-state setup; “export exists” alone is insufficient.
- The export thunk performs reverse-P/Invoke initialization, but does not run the full CRT/DLL startup lifecycle.
- The PE TLS directory is honored for the template and `_tls_index`; its callback array is empty for this artifact.
- The current probe has no hidden user static constructor. NativeAOT runtime metadata remains present and later static-constructor experiments are separate blockers.
- A functional GC heap is not needed for this proven path because no object or array allocation is executed. This does not prove allocation or GC support.
- `.pdata` and exception metadata remain in the image, but no Windows unwind registration or exception path is implemented. The loader installs only bounded CPU fault diagnostics and halts on faults.
- The exact native call target is the relocated `ManagedMain` export VA, not PE entry RVA `0x77700`.

The current path is therefore a legitimate direct exported NativeAOT entry for this no-allocation artifact, with its required transition state explicitly supplied by the loader. It is not a general NativeAOT process-start contract.

## Allocation-enabled variant

The follow-on allocation artifact is intentionally a differential build, not a replacement for the Gate 4 control. Its shared PE is 731,136 bytes with SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`; the no-allocation control used for the fresh baseline is 729,600 bytes with SHA-256 `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`. The two manifests have identical 10 import descriptors and 124 import symbols. The allocation map adds the probe EEType, writable data, constructor, and `ManagedEntry__AllocateOne`; it does not add a platform import.

In the allocation variant the managed path is `ManagedMain -> AllocateOne -> RhpNewFast`, with the helper reading the current TLS allocation context at TLS block `+0x30` (`limit`) and `+0x38` (`allocation pointer`). The probe validates a non-null object and a fixed field value before emitting its first-allocation marker. The pre-startup run returned managed status `-10` because those TLS slots remained zero; no object or GC success is claimed.

The allocation variant's PE entry RVA is `0x77840`, distinct from the no-allocation control's `0x77700` because adding the managed allocation path changes the linked image. An opt-in harness mode calls that actual PE entrypoint with DLL process-attach arguments after the existing relocation/IAT/TLS work. The first call is the compiler/CRT security-cookie initializer at `0x180078290`, whose direct call at `0x1800782ca` reads IAT slot RVA `0x7e1e0` (`GetSystemTimeAsFileTime`) into `[rsp+0x40]`. The normal thunk at `0x18003ca70` points to the same slot. The verified UEFI-backed implementation returns a valid 64-bit FILETIME, after which the same initializer reaches `QueryPerformanceCounter` at `0x1800782f9`, IAT RVA `0x7e0c8`; QPC returns, both empty on-exit tables initialize at the current payload's attach sites `0x180077595`/`0x1800775a3`, and the following helper calls `KERNEL32.dll!InitializeSListHead` through IAT RVA `0x7e2f8`. The guideXOS wrapper validates one aligned header at preferred RVA `0xb5ed0`, then startup reaches `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. Full DLL/CRT startup and the GC contract remain unclosed at that next boundary.

The current SLIST caller is the NativeAOT attach/bootstrap helper at preferred address `0x180077550`. It calls the helper at `0x180078350`, which loads the static image address `0x1800b5ed0` and tail-jumps through the `InitializeSListHead` IAT slot. The header is in the loaded image's writable zero-filled static-data region, not TLS, stack, loader-owned scratch, or heap memory. The next helper at `0x180078380` and the `_initterm_e` call at `0x1800775bb` are reached only after the guideXOS function returns. The initialization-only contract and family census are recorded in [PLATFORM_SLIST_CONTRACT.md](PLATFORM_SLIST_CONTRACT.md).

## Monotonic performance contract

The current allocation-startup build maps the two performance imports to `src/Gate4Harness/platform_performance.c`:

| Import | IAT RVA | Contract | Current QEMU source |
| --- | ---: | --- | --- |
| `QueryPerformanceCounter` | `0x7e0c8` | Microsoft x64 ABI, writable `int64_t*`, return `1` on success and `0` on null/unavailable/regression; normalized signed-64 counter units | ACPI PM timer port `0x608`, 24-bit raw counter |
| `QueryPerformanceFrequency` | `0x7e0d0` | Microsoft x64 ABI, writable `int64_t*`, positive stable frequency | `0x369E99` = 3,579,545 Hz |

The source-selection order is deliberate. The implementation first checks CPUID for invariant TSC and an exact CPUID leaf `0x15` ratio; the default QEMU CPU reports max basic leaf `0xD`, invariant-TSC bit `0`, and zero leaf-15 metadata, so it is rejected. It then reads the ACPI configuration-table pointer from the UEFI system table, validates RSDP/root/FADT checksums and lengths, reads the FADT legacy PM-timer block at offset `76`, and honors the 24-bit/32-bit flag at offset `112`. OVMF exposes port `0x608`, width 24, and the standard ACPI PM frequency `3,579,545` Hz. HPET, PIT, and APIC timers are not substituted: they were not required by the observed source path and would add interrupt or scheduler dependencies.

The PM raw counter is extended across its 24-bit wrap with checked delta arithmetic; regressions, ambiguous half-range deltas, invalid metadata, and signed-output overflow return failure. Initialization records a raw observation and QPC records a normalized observation. In the authoritative startup trace the authentic security-cookie call makes one QPC call (`QPC_COUNT=1`); the separate `PerfStallProbe` makes two immediate/after-stall reads and verifies a positive post-`Stall(1)` delta. No allocation, libc, host OS, Boot Services, events, or threads are used by the wrappers after source initialization. The harness does not call `ExitBootServices`; the retained ACPI table metadata and hardware PM port are the source lifetime for this bounded UEFI profile.

The exact current path is:

```text
GetSystemTimeAsFileTime -> QPC -> _initialize_onexit_table (twice)
  -> KERNEL32.dll!InitializeSListHead
  -> api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e
```

The three selected fresh positive logs are under `artifacts\qpc-final-20260729-allocation\time-contract-runs-*`; each has source `ACPI_PM_TIMER`, frequency `0x369E99`, two source/normalized observations, one QPC call, zero regressions, and zero TLS allocation context. The first allocation remains unproven.

## SLIST evidence-closure result (2026-07-29)

The artifact anatomy and call-site conclusion are unchanged: the attach/bootstrap helper initializes one aligned writable static header and then reaches `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. The final immutable artifact set `artifacts\slist-final-validation-20260729-corrected3` produced three complete fresh QEMU traces in `evidence\generated\slist-final-20260730-immutable`; all three contain the exact header marker and terminal summary under identical loader/payload/runtime/firmware/QEMU/runner/validator hashes. The narrow SLIST initialization milestone is therefore closed.

The three traces retain zero allocation context, no GC initialization marker, no managed-thread registration, no first allocation, and no general SLIST operation. The next-stage blocker is `_initterm_e`; do not advance to initializer execution or implement SLIST mutations in this milestone.

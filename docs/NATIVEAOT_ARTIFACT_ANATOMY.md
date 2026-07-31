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

The exact ten descriptors and 124 symbols, with IAT RVAs and per-symbol treatment, are in [DEPENDENCY_CENSUS.md](DEPENDENCY_CENSUS.md). The current `_stricmp` positive loader serializes 29 functional and 95 fail-fast imports:

```text
PE_IMPORT_DESCRIPTORS=10
PE_IMPORT_SYMBOLS=124
PE_IMPORT_RESOLVED=124
PE_IMPORT_FUNCTIONAL=29
PE_IMPORT_FAILFAST=95
UNRESOLVED_REQUIRED_IMPORTS=0
```

Each of the 95 currently unreachable symbols is patched to a guideXOS-owned stub that emits `GXOS_NET10:UNEXPECTED_IMPORT_CALL:<module>!<symbol>` and halts. This is deterministic failure, not a broad Windows compatibility layer. The historical 18 functional targets are the narrowly demonstrated FLS, current identity, pseudo-handle, bounded stack query, and one-thread critical-section operations required by the observed transition path. The current allocation-startup build adds `GetSystemTimeAsFileTime`, `QueryPerformanceCounter`, and `QueryPerformanceFrequency`, for 21 functional / 103 fail-fast. The CRT opt-in adds `_initialize_onexit_table`, for 22 / 102, the SLIST opt-in adds `InitializeSListHead`, for 23 / 101, `_initterm_e` adds one for 24 / 100, `_initterm` adds one for 25 / 99, `strcmp` adds one for 26 / 98, `strlen` adds one for 27 / 97, `GetEnvironmentVariableW` adds one for 28 / 96, and `_stricmp` adds one for 29 / 95. The historical 18/106, 19/105, 21/103, and 26/98 counts remain audit evidence.

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

The exact current path through the `_stricmp` closure is:

```text
GetSystemTimeAsFileTime -> QPC -> _initialize_onexit_table (twice)
  -> KERNEL32.dll!InitializeSListHead
  -> api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e
  -> api-ms-win-crt-runtime-l1-1-0.dll!_initterm
  -> api-ms-win-crt-string-l1-1-0.dll!strcmp
  -> api-ms-win-crt-string-l1-1-0.dll!strlen
  -> KERNEL32.dll!GetEnvironmentVariableW("DOTNET_gcServer") = missing
  -> api-ms-win-crt-string-l1-1-0.dll!_stricmp (885 checked calls)
  -> KERNEL32.dll!GetSystemInfo (next authentic boundary; fail-fast)
```

The three selected fresh positive logs are under `artifacts\qpc-final-20260729-allocation\time-contract-runs-*`; each has source `ACPI_PM_TIMER`, frequency `0x369E99`, two source/normalized observations, one QPC call, zero regressions, and zero TLS allocation context. The first allocation remains unproven.

## SLIST evidence-closure result (2026-07-29)

The artifact anatomy and call-site conclusion are unchanged: the attach/bootstrap helper initializes one aligned writable static header and then reaches `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. The final immutable artifact set `artifacts\slist-final-validation-20260729-corrected3` produced three complete fresh QEMU traces in `evidence\generated\slist-final-20260730-immutable`; all three contain the exact header marker and terminal summary under identical loader/payload/runtime/firmware/QEMU/runner/validator hashes. The narrow SLIST initialization milestone is therefore closed.

The three traces retain zero allocation context, no GC-advanced marker, no managed-thread registration, no first allocation, and no general SLIST operation. The loader's `GC_STARTUP_BEGIN` line is only the existing trace boundary; it is not evidence of a functional GC heap or initialization. The `_initterm_e` and `_initterm` follow-ons are recorded below; SLIST mutations remain out of scope.

## Error-returning CRT initializer table

The allocation/startup artifact imports `_initterm_e` from `api-ms-win-crt-runtime-l1-1-0.dll` at IAT RVA `0x7e380`. The exact preferred call is `0x1800775bb` in the attach/bootstrap helper beginning at `0x180077550`; the following preferred call at `0x1800775db` is `_initterm` and remains deliberately unsupported. The call uses the Microsoft x64 ABI: RCX is the first table pointer and RDX is the exclusive end pointer. The relocated QEMU call observed the return address `0x00000000054F05C0`.

The concrete range is preferred `0x18007e4d0` to `0x18007e4d8`, relocated to `0x00000000054F74D0` to `0x00000000054F74D8`. It occupies one eight-byte slot in `.rdata` (`RVA 0x7e4d0`), with one stored null pointer and no relocation entry. There is no non-null initializer to classify or invoke in this artifact; the bounded iterator validates the range, skips the null, returns zero, and emits its success marker. This is the NativeAOT artifact's actual table census, not a claim that every NativeAOT image has an empty table.

The loader records executable PE regions and configures a narrow iterator context after relocation. Before a non-null callback would be invoked, the wrapper checks canonical x64 form, image membership, executable-section membership, and the configured relocated state. The context is bounded to 4,096 entries and uses overflow-safe integer indexing. No heap or managed allocation is performed by the iterator. The host vectors cover non-null callback ABI/order/failure behavior and malformed ranges; QEMU proves the concrete one-null table. The three-run immutable evidence is `evidence\generated\crt-initterm-e-final-20260730-immutable-v4`, whose next authentic boundary was `_initterm`; that boundary is now closed by the evidence below.

## Void-returning CRT initializer table

The allocation-enabled artifact imports `_initterm` from `api-ms-win-crt-runtime-l1-1-0.dll` at IAT RVA `0x7e390`. The exact preferred call is `0x1800775db` in the attach/bootstrap helper beginning at `0x180077550`; the relocated wrapper observed return address `0x00000000054F05E0`. RCX carries first and RDX carries the exclusive end pointer.

The actual range is `0x00000000054F7468` through `0x00000000054F74B0`, `0x48` bytes in `.rdata`, for nine pointer entries. Index zero is null; indexes one through eight are relocated direct `.text` targets at `0x00000000054AAD50`, `0x00000000054AADA0`, `0x00000000054AAD90`, `0x00000000054AADC0`, `0x00000000054AADB0`, `0x00000000054AADD0`, `0x00000000054AADE0`, and `0x00000000054AADF0`. All eight callbacks entered and returned in order. They performed internal static-state writes and caused no direct imported API call; the next dependency after completion was `api-ms-win-crt-string-l1-1-0.dll!strcmp`.

The narrow iterator validates canonical x64 targets, loaded-image membership, readable/aligned table storage, executable target regions, relocated state, range ordering, and overflow-safe bounded pointer arithmetic. It skips null entries, invokes each non-null entry exactly once per occurrence, emits a post-call marker only after return, performs no allocation, and does not interpret a callback return register. This proves the actual artifact's void-initializer range only; it does not prove general `.CRT` or C++ initialization.

## `strlen` evidence-closure result (2026-07-31)

The current payload import table contains `strlen` at IAT RVA `0x7d3e8`, with preferred IAT address `0x18007d3e8` and the normal import thunk at preferred `0x18007737f`. The exact static call is preferred `0x18003dba0`, returning at `0x18003dba5`; in the relocated QEMU image (`IMAGE_BASE=0x000000000547B000`) the wrapper observed return address `0x00000000054B8BA5`. The call is made after `NATIVEAOT_STARTUP_OK` and `BEFORE_MANAGED_CALL`, and its return value is consumed by the next NativeAOT startup helper before the next import boundary.

The first argument is relocated `.rdata` address `0x0000000005513498`, the read-only `.rdata` mapping is `0x00000000054F8000..0x0000000005524E00`, and the bounded byte preview is `gcServer` followed by the terminator. The checked wrapper returns `8` and reports terminator address `0x00000000055134A0`; it performs no allocation, external call, write, SIMD read, or speculative access beyond the validated image-region context. The import treatment is 27 functional / 97 fail-fast / 0 unresolved, and three immutable fresh QEMU runs reach `KERNEL32.dll!GetEnvironmentVariableW`. The disabled profile preserves the prior `strlen` fail-fast boundary.

## `GetEnvironmentVariableW` evidence-closure result (2026-07-31)

The current allocation-enabled image imports `KERNEL32.dll!GetEnvironmentVariableW` at IAT RVA `0x7d088`, preferred IAT address `0x18007d088`, and normal thunk `0x1800772ef`. The live NativeAOT call is in the GC-configuration helper at preferred `0x18003e150`; its direct call is `0x18003e196`, with return at `0x18003e19b`. In the relocated QEMU image (`IMAGE_BASE=0x000000000547B000`), the call and return are `0x00000000054B9196` and `0x00000000054B919B`.

The Microsoft x64 arguments observed in all three immutable runs were `RCX=lpName=0x0000000007E64B40`, `RDX=lpBuffer=0x0000000007E64B10`, and `R8D=nSize=0x11`. The name is the terminated UTF-16 string `DOTNET_gcServer` (15 code units). The buffer is non-null, the size is nonzero, and this is not a size probe. The function returns `0`, changes last error from `0` to `203` (`ERROR_ENVVAR_NOT_FOUND`), writes no value, and the caller immediately takes its absent/fallback path. Each process makes exactly one call; no second variable query or value parse is reached.

The enabled treatment is 28 functional / 96 fail-fast / 0 unresolved. The three immutable runs under `evidence\generated\getenv-final-20260731-immutable` all have exit `0`, unique PIDs, complete serial output, QPC count `2`, zero QPC regressions, and zero TLS allocation context, managed-thread registration, and GC heap usability. The disabled control preserves the original GetEnvironmentVariableW boundary. The next authentic import is `_stricmp`, and this document does not claim a complete environment subsystem or GC initialization.

## `_stricmp` evidence-closure result (2026-07-31)

The exact imported `_stricmp` slot is RVA `0x7e3e0`, with preferred thunk `0x1800774cb`. The two executed direct call sites are preferred `0x18003df6b` and `0x18003e0ab`; each tests EAX for zero/sign after return. The checked Microsoft x64 route validates both relocated `.rdata` operands, completes 885 calls, and advances to `KERNEL32.dll!GetSystemInfo`. See [CRT_STRICMP_BOOTSTRAP.md](CRT_STRICMP_BOOTSTRAP.md) for the contract and immutable evidence paths.

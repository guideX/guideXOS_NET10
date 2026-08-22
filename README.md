# guideXOS_NET10

Experimental guideXOS reimagining on .NET 10 NativeAOT.

This repository is an archaeology and bootstrap project. It is not a direct upgrade of `guideXOS` Legacy, and it does not replace `guideXOSUEFI` or guideXOS Server. Those repositories remain read-only references and experiments.

## Current state

The cooperative NativeAOT scheduler/runtime foundation is complete and remains
covered by the small .NET 10 NativeAOT managed-entry probe and narrowly scoped
UEFI PE loader harness. Managed-kernel integration is now active: the new
`src/ManagedKernel` project is the first real managed guideXOS system layer and
uses the versioned service contract documented in
[MANAGED_KERNEL_ABI.md](docs/MANAGED_KERNEL_ABI.md). The four-gate result is:

- Gate 1: passed — reproducible standard .NET 10 NativeAOT PE artifacts.
- Gate 2: passed — the linked runtime and platform dependency census is recorded.
- Gate 3: passed — PE/COFF anatomy and byte-for-byte staging are machine-checked.
- Gate 4: passed — all 124 imported symbols are satisfied; the 18-symbol bounded platform boundary establishes the one-thread NativeAOT state needed by this probe; three fresh QEMU processes entered managed code and returned deterministically.

The earlier ten-descriptor import stop is retained as historical evidence. `GXOS_NET10:MANAGED_ENTRY_OK` is emitted by managed execution, not by the native loader.

The post-initialization managed callback bridge is now proven: the loaded PE export table discovers `ManagedCallback`, the one-shot NativeAOT process entry returns, and three fresh QEMU boots execute later native-to-managed-to-native calls with persistent managed state. See [the NativeAOT managed callback bridge contract](docs/NATIVEAOT_MANAGED_CALLBACK.md).

The NativeAOT/runtime foundation now includes bounded managed allocation and real GC participation on the main and scheduler-attached worker threads, including survival of a stack-local managed root across collection. See [the scheduler-thread GC proof](docs/NATIVEAOT_GC_SCHEDULER_THREAD.md). The older pre-heap and no-allocation artifacts remain historical controls; they do not define the current merge-gate payload.

ManagedKernel Phase 2 adds the first real native-to-managed machine-state
service: a bounded, versioned snapshot of the normalized boot-time physical
resource map. Native `g_memory_map` and `GXOS_MEMORY_CLASSIFICATION` remain
authoritative; ManagedKernel receives copied fixed-layout summary/region values
and never owns the native allocator or raw UEFI descriptors. The acceptance
path validates publication, summary totals, first/middle/final descriptors,
failure sentinels, repeat-query stability, and three fresh QEMU boots. See
[the ManagedKernel ABI contract](docs/MANAGED_KERNEL_ABI.md).

ManagedKernel Phase 3 adds the explicit lifecycle
`Initialize -> InstallBootResources -> InstallHostServices -> Start`, with
out-of-order and duplicate-call rejection. Host Services v1 provides only a
bounded UTF-8 logger and an optional normalized monotonic-time query through
fixed-size, capability-negotiated native callbacks. The normal path calls
`ManagedMain` exactly once, preserves the immutable Phase 2 snapshot, and
proves three fresh EventWait-profile QEMU boots. The ManagedEntryProbe remains
the separate foundation control payload.

ManagedKernel Phase 4 adds a separate 72-byte versioned memory-services table
and a native authoritative page allocator. The service uses the existing VM
arena, paging, physical-page ledger, and VM-region ledger with explicit
`MANAGED_KERNEL` owner/class accounting. Allocation is bounded to 256 pages
per request, 16 live allocations, and 1024 total live pages; every allocation
returns a native opaque ID and requires an exact explicit release. Host tests
cover transactional rollback and accounting restoration, while the managed
proof covers full-region patterns, boundary writes, GC/runtime survival,
multiple allocations, ownership-mismatch rejection, and double-release
rejection. The Phase 4 acceptance path requires three fresh QEMU boots.
The acceptance baseline is exact for all ManagedKernel-owned registry, physical,
arena, commitment, and VM-region state; whole-process counters remain
diagnostic because the EventWait runtime/GC profile may add unrelated live
runtime state during the proof.

ManagedKernel Phase 5 adds the bounded managed `KernelArena` policy above the
Phase 4 native page mechanism. Arena metadata is kept in fixed managed control
arrays outside the unmanaged kernel buffers; allocations use first-fit,
alignment-aware splitting, same-chunk coalescing, bounded growth, exact
descriptor validation, rollback on growth failure, and explicit destroy.
The default policy is 2-page initial backing, 2-page growth, at most 4 chunks,
8 total pages, 24 live allocations, and 64 block records. Arena buffers are
not NativeAOT GC objects and do not participate in the managed heap. Host tests
and three fresh QEMU boots prove reuse, fragmentation, growth, GC/runtime
survival, negative paths, arena isolation, and exact ManagedKernel-owned native
accounting restoration. The native harness intentionally does not require
unrelated whole-process runtime/GC counters to return to their earlier values.

ManagedKernel Phase 6 adds the first managed platform/device inventory. The
native loader is authoritative for a read-only PCI configuration-space snapshot
of segment-0 BDF identity, vendor/device/revision, class data, and header type;
no synthetic devices are introduced and no BAR probing writes hardware. A
40-byte summary, 48-byte device descriptor, and 48-byte publication record are
versioned and bounded to 256 devices. ManagedKernel copies the immutable native
snapshot into three arena-backed buffers, validates BDF uniqueness, supports
index/BDF/class queries, and explicitly reports resource data unavailable in
v1 because reliable BAR lengths are not yet obtained read-only. Native and
managed host vectors plus three fresh EventWait-profile QEMU boots prove
negative installs, byte-for-byte query parity, GC/runtime survival, temporary
teardown, accounting restoration, and persistent inventory operability. See
[the Phase 6 ABI and inventory contract](docs/MANAGED_KERNEL_ABI.md#phase-6-managed-platform-and-device-inventory).

ManagedKernel Phase 7 adds managed driver-selection policy plus a separately
versioned, strictly read-only PCI configuration service. Native guideXOS keeps
the CF8/CFC hardware mechanism and validates every request against the
immutable Phase 6 inventory; ManagedKernel owns bounded driver definitions,
deterministic specificity/priority/registration-order precedence, freeze and
binding state, and arena-backed policy tables. Real QEMU devices are matched,
read through the managed wrapper, compared against inventory truth, exercised
across runtime/GC activity, and left with `ResourceCount == 0`; no PCI write,
BAR probe, MMIO, interrupt, DMA, or hardware initialization path is present.
See [the Phase 7 driver-binding contract](docs/MANAGED_KERNEL_DRIVER_BINDING.md).

ManagedKernel Phase 8 crosses the next boundary with the first real managed
hardware driver: `ManagedSerialDriver` owns policy and operational state for
the native-authoritative COM1 platform device. A separate 72-byte Serial
Services v1 table exposes only bounded transmit and normalized readiness; the
native side retains all raw port-I/O authority. The managed driver uses a
two-page KernelArena, emits independent raw COM1 markers before and after
GC/scheduler/time/memory/inventory/PCI activity, runs deterministic negative
paths, and proves controlled teardown/accounting restoration before retaining
one operational instance. Three fresh ManagedKernel QEMU boots and the
ManagedEntryProbe durability control are covered by the Phase 8 acceptance
scripts. See [the first managed hardware driver contract](docs/MANAGED_KERNEL_SERIAL_DRIVER.md).

ManagedKernel Phase 9 adds the first native interrupt-capture implementation,
still using COM1 as the narrowly scoped platform device. The native substrate
audit found no guideXOS-owned generic IRQ/APIC layer, so the implementation
uses the existing firmware/legacy PIC layout: COM1 IRQ4 is temporarily gated
at vector `0x24`, the native ISR captures into a static eight-record queue, and
managed code drains only at explicit safe points. The 88-byte Interrupt
Services v1 ABI exposes subscribe, unsubscribe, bounded drain, and stats; it
does not expose raw IRQ, PIC, IDT, or port-I/O authority. The raw gate performs
no managed call, allocation, or logging. The original Phase 8 `-serial file:`
backend remains the output-only control path; the Phase 9 acceptance runner
uses a bidirectional TCP chardev socket and writes raw `R`, then `S`, and `Z`
after unsubscribe. The transport proof observed UART `LSR.DATA_READY` for
`0x52`, and the hardware proof reached `PHASE9_PASS` on three fresh QEMU
boots. During acceptance, legacy PIC IRQ4 is masked so the existing IOAPIC
route owns vector `0x24`; the saved PIC/IOAPIC/IDT state is restored on
unsubscribe. See [the Phase 9 interrupt and deferred-delivery contract](docs/MANAGED_KERNEL_INTERRUPT_DRIVER.md).

The Phase 9 host vectors are `Run-ManagedKernelInterruptNativeHostTests.ps1`
and `Run-ManagedKernelInterruptHostTests.ps1`. The real-hardware acceptance
runner is `Run-ManagedKernelPhase9FreshBoots.ps1`; it requires three fresh
QEMU boots, records serial, injection, timeline, and command-line hashes, and
requires exact native IRQ/queue/accounting counters on every boot. The final
acceptance evidence is under
`artifacts/phase9-final-acceptance-evidence-20260822-final4`.

Native guideXOS owns physical-memory truth. ManagedKernel receives a bounded,
versioned view of that truth through the managed-kernel ABI.

## Provisional first-image path

```text
UEFI firmware
  -> guideXOS-owned PE/COFF loader
  -> NativeAOT PE validation and relocation
  -> exact import resolution
  -> bounded TLS, stack, FLS, handle, virtual-query, and one-thread lock state
  -> exported ManagedMain reverse-P/Invoke entry
  -> managed serial callback and deterministic return
  -> native publication of the immutable boot-resource snapshot
  -> bounded ManagedKernel summary/region queries
  -> native ManagedKernel memory-services installation
  -> native Host Services v1 installation
  -> managed kernel start with bounded host logging and monotonic time
  -> managed page allocation and Phase 5 arena policy, pattern, GC, growth,
     multi-arena, negative-path, and destroy proof
  -> native read-only PCI identity snapshot
  -> managed Phase 6 device inventory, indexed queries, and teardown proof
  -> native PCI Services v1 installation with immutable-inventory BDF checks
  -> managed driver registry freeze, deterministic binding, and read-only
     config truth comparison
  -> managed COM1 serial driver through Serial Services v1
  -> native COM1 IRQ4 capture at vector 0x24
  -> fixed native receive queue and managed safe-point drain
  -> managed serial receive validation, runtime proof, and unsubscribe
  -> post-initialization ManagedCallback export with existing runtime state
  -> scheduler-thread reverse-P/Invoke attach
  -> managed allocation and real GC probe
```

This foundation deliberately excludes managed thread-pool/`Task`/async support, arbitrary managed `Thread` creation, heavy concurrent GC stress, finalizer stress, broad exception propagation, reflection-heavy features, dynamic assembly loading, full COM interop, APCs, `WaitForMultipleObjectsEx`, broad Win32 coverage, thread descriptions, full NativeAOT thread-store destruction semantics, advanced stack growth/guard behavior, networking, globalization, filesystem support, and broad framework compatibility.

## NativeAOT feasibility documents

- [Build and toolchain record](docs/BUILD_TOOLCHAIN_RECORD.md)
- [NativeAOT artifact anatomy](docs/NATIVEAOT_ARTIFACT_ANATOMY.md)
- [Dependency census](docs/DEPENDENCY_CENSUS.md)
- [`GetEnvironmentVariableW` bootstrap contract](docs/KERNEL32_GETENVIRONMENTVARIABLEW_BOOTSTRAP.md)
- [Image-format decision](docs/IMAGE_FORMAT_DECISION.md)
- [Boot ABI](docs/BOOT_ABI.md)
- [Managed-entry proof procedure](docs/MANAGED_ENTRY_PROOF.md)
- [NativeAOT allocation and GC probe](docs/ALLOCATION_GC_PROBE.md)
- [NativeAOT scheduler-thread attach](docs/NATIVEAOT_SCHEDULER_THREAD_ATTACH.md)
- [NativeAOT scheduler-thread GC proof](docs/NATIVEAOT_GC_SCHEDULER_THREAD.md)
- [NativeAOT platform time contract](docs/PLATFORM_TIME_CONTRACT.md)
- [NativeAOT platform performance counter](docs/PLATFORM_PERFORMANCE_COUNTER.md)
- [Windows x64 SLIST initialization contract](docs/PLATFORM_SLIST_CONTRACT.md)
- [Windows x64 `_initterm_e` bootstrap contract](docs/CRT_INITTERM_E_BOOTSTRAP.md)
- [Windows x64 `_initterm` bootstrap contract](docs/CRT_INITTERM_BOOTSTRAP.md)
- [Windows x64 `strcmp` bootstrap contract](docs/CRT_STRCMP_BOOTSTRAP.md)
- [Windows x64 `strlen` bootstrap contract](docs/CRT_STRLEN_BOOTSTRAP.md)
- [Windows x64 `_stricmp` bootstrap contract](docs/CRT_STRICMP_BOOTSTRAP.md)
- [Microsoft x64 `_register_onexit_function` bootstrap contract](docs/CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md)
- [Microsoft x64 bounded `malloc` bootstrap contract](docs/CRT_MALLOC_BOOTSTRAP.md)
- [`GetSystemInfo` bootstrap contract](docs/KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md)
- [`GetNumaHighestNodeNumber` bootstrap contract](docs/KERNEL32_GETNUMAHIGHESTNODENUMBER_BOOTSTRAP.md)
- [`GetProcessGroupAffinity` bootstrap contract](docs/KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md)
- [`CreateMemoryResourceNotification` bootstrap contract](docs/KERNEL32_CREATEMEMORYRESOURCENOTIFICATION_BOOTSTRAP.md)
- [`ResumeThread` bootstrap contract](docs/KERNEL32_RESUMETHREAD_BOOTSTRAP.md)
- [`IsProcessInJob` bootstrap contract](docs/KERNEL32_ISPROCESSINJOB_BOOTSTRAP.md)
- [`SetThreadPriority` bootstrap contract](docs/KERNEL32_SETTHREADPRIORITY_BOOTSTRAP.md)
- [Evidence ledger](docs/EVIDENCE_LEDGER.md)
- [Next-stage blockers](docs/NEXT_STAGE_BLOCKERS.md)

## Audit documents

- [Project direction](docs/PROJECT_DIRECTION.md)
- [Reference repositories](docs/REFERENCE_REPOSITORIES.md)
- [.NET 7 CoreLib/runtime delta](docs/DOTNET7_CORELIB_DELTA.md)
- [NativeAOT and ILCompiler strategy](docs/NATIVEAOT_ILCOMPILER_STRATEGY.md)
- [Bootloader comparison](docs/BOOTLOADER_COMPARISON.md)
- [Kernel reuse matrix](docs/KERNEL_REUSE_MATRIX.md)
- [Userland reuse matrix](docs/USERLAND_REUSE_MATRIX.md)
- [App Model direction](docs/APP_MODEL_DIRECTION.md)
- [First managed entry plan](docs/FIRST_MANAGED_ENTRY_PLAN.md)
- [Audit evidence ledger](docs/AUDIT_EVIDENCE.md)

## Working rules

`D:\dev\guideXOS_NET10` is the only writable repository for this effort. Legacy, UEFI, and Server are read-only code vaults. Audit documents distinguish confirmed observations from recommendations; a recommendation is not evidence that a later runtime feature already works.

## SLIST initialization evidence status (2026-07-29)

The narrow Windows x64 `InitializeSListHead` implementation and host contract suite are complete: one writable 16-byte, 16-byte-aligned NativeAOT-owned header is reset to two zero 64-bit words, with no allocation, GC initialization, managed-thread registration, or general SLIST mutation support. The final immutable artifact set is under `artifacts\slist-final-validation-20260729-corrected3`; its loader hash is `2EEBCD284F6D2E5AD1526EB15FA4AF6483E7B1FE9D17A448720A289FF64B0362` and its NativeAOT payload hash is `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`.

The required three-run QEMU gate is closed by `evidence\generated\slist-final-20260730-immutable`: fresh PIDs `17256`, `660`, and `15344` used identical artifact hashes, emitted the complete marker sequence and final summaries, and advanced to `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. The prior apparent stalls were guest triple faults caused by the bounded loader's un-packed IDTR and 32-entry replacement IDT; the harness now preserves the packed firmware IDTR and all IRQ vectors. The subsequent `_initterm_e` and `_initterm` closures are documented separately.

## `_initterm_e` bootstrap evidence status (2026-07-30)

The narrow Microsoft x64 `_initterm_e` contract is implemented and host-tested. The actual NativeAOT call passes the relocated `.rdata` range `0x00000000054F74D0` to `0x00000000054F74D8`, an exclusive eight-byte range containing one null entry. The iterator validates the range and executable callback targets, skips nulls, invokes non-null entries in forward order, and returns the first nonzero callback result without allocation. Because this artifact's table is empty, the three real QEMU runs invoke zero callbacks and correctly return zero; callback ABI, ordering, failure, bounds, and target-validation behavior are proven by the focused host vectors.

The immutable evidence is under `evidence\generated\crt-initterm-e-final-20260730-immutable-v4`. Three fresh QEMU processes complete the iterator and advance to the next authentic boundary, `_initterm`; this historical result is preserved unchanged.

## `_initterm` bootstrap evidence status (2026-07-30)

The narrow Microsoft x64 `_initterm` contract is implemented, host-tested, and routed only for the exact `api-ms-win-crt-runtime-l1-1-0.dll!_initterm` import. The actual NativeAOT call passes relocated `.rdata` bounds `0x00000000054F7468` to `0x00000000054F74B0`: nine entries, one null, and eight relocated direct `.text` callbacks. All eight valid callbacks entered and returned in forward order. Three immutable fresh QEMU runs are recorded under `evidence\generated\crt-initterm-final-20260730-immutable-v2` and stop at `api-ms-win-crt-string-l1-1-0.dll!strcmp`. GC heap usability, allocation context, managed-thread registration, managed allocation, broad CRT startup, and general C++ initialization remain unproven.

## `strcmp` bootstrap evidence status (2026-07-30)

The narrow Microsoft x64 `strcmp` implementation is complete, host-tested, and routed only for the exact `api-ms-win-crt-string-l1-1-0.dll!strcmp` import. The actual NativeAOT call compares `gcServer` with `gcConservative`, returns `+1`, and advances to `strlen`. The follow-on `strlen` milestone is closed separately; the historical `strcmp` evidence remains unchanged under `evidence\generated\crt-strcmp-final-20260730-immutable`.

## `strlen` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `strlen` implementation is complete, host-tested, and routed only for the exact `api-ms-win-crt-string-l1-1-0.dll!strlen` import. The actual NativeAOT call scans the read-only `.rdata` string `gcServer`, returns length `8`, and advances to `KERNEL32.dll!GetEnvironmentVariableW`. Three immutable fresh QEMU runs are recorded under `evidence\generated\crt-strlen-final-20260731-immutable-v3`; the disabled control retains the original `strlen` boundary. The enabled profile is 27 functional / 97 fail-fast / 0 unresolved imports, with zero allocation-context, managed-thread, and GC-heap evidence.

## `GetEnvironmentVariableW` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `GetEnvironmentVariableW` implementation is complete, host-tested, and routed only for the exact `KERNEL32.dll!GetEnvironmentVariableW` import. The live NativeAOT path queries `DOTNET_gcServer` once with a non-null 17-character buffer, receives missing-variable result `0`, and observes `ERROR_ENVVAR_NOT_FOUND` (`203`). The value is not parsed; the caller takes its fallback path. Three immutable fresh QEMU runs are recorded under `evidence\generated\getenv-final-20260731-immutable`; the enabled profile is 28 functional / 96 fail-fast / 0 unresolved imports and advances to `api-ms-win-crt-string-l1-1-0.dll!_stricmp`. The disabled control retains the original GetEnvironmentVariableW boundary. This is a narrow lookup contract, not a complete Windows environment subsystem or GC initialization.

## `_stricmp` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `_stricmp` implementation is complete, host-tested, and routed only for the exact `api-ms-win-crt-string-l1-1-0.dll!_stricmp` import. The checked route validates image-backed readable operands, bounded null termination, canonical pointers, overflow, and default-C-locale ASCII folding. Three fresh positive QEMU runs complete 885 calls and stop at the next authentic `KERNEL32.dll!GetSystemInfo` import; the disabled route stops at `_stricmp`. No later API is routed.

## `GetSystemInfo` bootstrap evidence status (2026-07-31)

The narrow Microsoft x64 `KERNEL32.dll!GetSystemInfo` contract is complete, host-tested, and routed only for that exact import. The checked route validates the x64 `SYSTEM_INFO` destination and approved writable range, initializes the complete `0x30`-byte structure, and publishes the current image-backed/page-backed bootstrap facts. Three immutable positive QEMU runs complete the observed consumer's `0xA2` field-read mask and advance to `KERNEL32.dll!GetNumaHighestNodeNumber`; the disabled three-run control retains the original GetSystemInfo boundary. See [KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md](docs/KERNEL32_GETSYSTEMINFO_BOOTSTRAP.md). This does not claim GC initialization, a general virtual-memory allocator, or processor discovery beyond the one bootstrap processor.

## `GetNumaHighestNodeNumber` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetNumaHighestNodeNumber` contract is complete and routed only for that exact import pair. The checked four-byte `ULONG` output contract publishes highest node `0` from the explicit one-processor/one-locality-domain `GetSystemInfo` snapshot, preserves the output on rejected inputs, and does not claim general NUMA, SMP, node-targeted allocation, scheduler locality, or GC support. Three immutable positive QEMU runs are recorded under `evidence\generated\getnumahighest-final-20260801-immutable-v2`; each reaches the next authentic `KERNEL32.dll!GetProcessGroupAffinity` boundary. See [KERNEL32_GETNUMAHIGHESTNODENUMBER_BOOTSTRAP.md](docs/KERNEL32_GETNUMAHIGHESTNODENUMBER_BOOTSTRAP.md).

## `GetProcessGroupAffinity` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetProcessGroupAffinity` contract is complete and routed only for that exact import pair. The observed caller performs one capacity probe with the current-process pseudo-handle, `GroupCount=0`, and `GroupArray=NULL`; the checked route returns `FALSE`, publishes required count `1`, sets `ERROR_INSUFFICIENT_BUFFER` (`122`), and the caller consumes the count without retrying or reading a group array. Three fresh immutable QEMU runs are recorded under `evidence\generated\getprocessgroup-final3-20260801-immutable-v4` and advance to the next authentic `KERNEL32.dll!GetProcessAffinityMask` boundary. The disabled control retains the original process-group boundary. See [KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md](docs/KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md).

This pass does not implement `GetProcessAffinityMask`, other processor-group APIs, NUMA/topology discovery, allocation, or GC behavior. The first-allocation blocker is unchanged.

## `QueryInformationJobObject` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!QueryInformationJobObject` contract is complete and routed only for the exact import pair. The live NativeAOT call uses `hJob=NULL`, class `JobObjectCpuRateControlInformation` (`15`), an eight-byte `JOBOBJECT_CPU_RATE_CONTROL_INFORMATION` destination, `cb=8`, and `lpReturnLength=NULL`. The checked route returns the no-associated-job failure with `ERROR_ACCESS_DENIED` (`5`), preserves the sentinel output, and the caller takes `FAILURE_NO_ASSOCIATED_JOB_FALLBACK`. A second static class-9 reference exists in the payload but is dormant; each bounded positive run proves one live call.

Three immutable QEMU runs are recorded under `evidence\generated\queryjobobject-final-20260801`; the enabled census is `34 / 90 / 0` and the next authentic boundary is `KERNEL32.dll!GetModuleHandleW`. Host reference, focused host vectors, success/active-limit fact experiments, disabled routing, negative evidence controls, and prior regressions pass. See [KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md](docs/KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md). This does not claim a general job-object subsystem, CPU-rate enforcement, allocation, GC readiness, or managed-thread registration.

## `GetProcessAffinityMask` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetProcessAffinityMask` contract is now routed only for the exact import pair. The artifact makes two live calls: bitmap setup reads the eight-byte process mask and updates a processor bitmap; processor-count setup reads the process mask, counts its bits, and then reaches `KERNEL32.dll!QueryInformationJobObject`. Neither caller reads the system mask or calls `GetLastError`. The checked route publishes process/system masks `0x1`/`0x1` from the existing one-bootstrap-processor snapshot and supports only the current-process pseudo-handle.

## `GetModuleHandleW` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetModuleHandleW` contract is implemented and routed only for that exact import. The live call is non-null `L"ntdll.dll"`, not a null-name query. Because no ntdll PE is mapped in this guideXOS process, the truthful route returns `NULL` with `ERROR_MOD_NOT_FOUND` (`126`) and advances to the next authentic `KERNEL32.dll!GetProcAddress` boundary. The current-executable null-name policy is separately checked and returns the actual relocated NativeAOT payload base only after PE-facts validation; no general named-module registry or DLL loader is claimed.

The final three-run immutable evidence is under `evidence\generated\getmodulehandlew-final-20260801-immutable`; the positive profile is `35 / 89 / 0` functional/fail-fast/unresolved imports and the disabled control remains `34 / 90 / 0` at `GetModuleHandleW`. Host ABI/contract tests and preferred-base, RVA, wrong-image, forced-failure, named-main, disabled, invalid-header, and bounded-name controls pass. See [KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md](docs/KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md). GC initialization, usable heap, allocation context, managed-thread registration, and managed allocation remain unproven.

Three immutable positive QEMU runs are recorded under `evidence\generated\getprocessaffinity-final-20260801-immutable-v2`; each has bounded 241,507-byte serial evidence and reaches `KERNEL32.dll!QueryInformationJobObject`. The disabled control retains the `GetProcessAffinityMask` fail-fast boundary. Host vectors pass all 57 focused cases, the forced-failure experiment proves both caller fallbacks with unchanged outputs, and the negative evidence pipeline rejects mutation controls. See [KERNEL32_GETPROCESSAFFINITYMASK_BOOTSTRAP.md](docs/KERNEL32_GETPROCESSAFFINITYMASK_BOOTSTRAP.md). GC heap usability, allocation context, managed-thread registration, managed allocation, SMP, and general process-handle support remain unproven and out of scope.

## `GetProcAddress` evidence status (2026-08-01)

The narrow Microsoft x64 `KERNEL32.dll!GetProcAddress` contract is now complete for the one live NativeAOT startup call. The preceding `GetModuleHandleW(&L"ntdll.dll")` result is `NULL`/`ERROR_MOD_NOT_FOUND` (`126`); the observed `GetProcAddress(NULL, "RtlDllShutdownInProgress")` result is `NULL`/`ERROR_PROC_NOT_FOUND` (`127`), and the caller takes its null optional fallback. The positive route changes the census to `36 / 88 / 0` and advances to `api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function`; the disabled control retains `35 / 89 / 0` and stops at `KERNEL32.dll!GetProcAddress`.

The final immutable evidence is under `artifacts\getprocaddress-final-v3-20260801-immutable-v2`; the disabled three-run control is under `artifacts\getprocaddress-final-disabled-v7-20260801-immutable-v2`. The focused host suite, Windows reference probe, seven evidence-tamper rejection controls, synthetic-pointer/wrong-error investigation controls, and prior host regression suites pass. See [KERNEL32_GETPROCADDRESS_BOOTSTRAP.md](docs/KERNEL32_GETPROCADDRESS_BOOTSTRAP.md). No export-table resolver, DLL loading, forwarded-export support, general module registry, GC initialization, allocation context, managed-thread registration, or managed allocation is claimed.

## `_register_onexit_function` evidence status (2026-08-02)

This task implements only the Microsoft x64 `_register_onexit_function` initial-storage success path required by the current NativeAOT startup path. Three fresh QEMU runs prove the exact payload hash, live import routing, decoded empty input, one `0x100`-byte UEFI `AllocatePool` block, 32 encoded slots, callback storage in slot 0, encoded-null slots 1 through 31, return `0`, callback non-execution, and continuation to the later `KERNEL32.dll!GetModuleHandleExW` blocker. Nonempty growth, `_recalloc`, callback execution, shutdown, GC initialization, and managed allocation remain out of scope. The enabled census is `37 / 87 / 0`, and the disabled control retains the register fail-fast boundary. See [CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md](docs/CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md).

## Bounded `malloc` bootstrap evidence status (2026-08-04)

The narrow Microsoft x64 `api-ms-win-crt-heap-l1-1-0.dll!malloc` bridge is implemented only for the exact NativeAOT payload. It accepts nonzero requests through `0xC8000`, calls `AllocatePool(EFI_LOADER_DATA, requestedSize, &pointer)`, returns the direct pool pointer without a hidden header or zeroing, and records ownership in a fixed 64-slot external registry. The deterministic host suite covers the verified 39-entry Windows oracle replay plus positive, rollback, overlap, duplicate-pointer, malformed-state, exhaustion, accounting, and isolation vectors.

Three fresh QEMU runs with the required payload hash reached `malloc(88)`, `malloc(72)`, and `malloc(56)` with identical semantic sequences, direct non-null 8-byte-aligned pointers, registry slots `0,1,2`, live counts `1,2,3`, and `_callnewh` count `0`. They then stopped at the next authentic unresolved import, `KERNEL32.dll!AddVectoredExceptionHandler`. `free`, `calloc`, `realloc`, `_recalloc`, `_callnewh`, and generalized heap behavior remain unimplemented. See [CRT_MALLOC_BOOTSTRAP.md](docs/CRT_MALLOC_BOOTSTRAP.md).

## `ResumeThread` evidence status (2026-08-08)

The exact `KERNEL32.dll!ResumeThread` route is closed for the required NativeAOT payload. A generation-checked genuine Thread handle returns previous suspend count `1`, changes the worker from `CreatedSuspended` to `Runnable`, changes suspend count `1` to `0`, and inserts exactly one runnable-queue entry. It preserves priority `2`, the public handle, execution lifetime, stack, GS/TLS/FLS state, and execution count `0`. The route does not force a cooperative context switch, so runnable eligibility is not conflated with worker execution.

Three fresh enabled QEMU runs agree on the immediate transition and naturally stop at the main-thread unresolved `KERNEL32.dll!IsProcessInJob` import. The disabled control omits only ResumeThread and restores the unresolved ResumeThread boundary. See [KERNEL32_RESUMETHREAD_BOOTSTRAP.md](docs/KERNEL32_RESUMETHREAD_BOOTSTRAP.md).

# First Managed Entry Plan

## Required milestone

The first technical milestone is precisely:

```text
UEFI firmware
→ guideXOS-owned bootloader
→ native bootstrap
→ .NET 10 NativeAOT module/runtime initialization
→ managed KernelMain entry
→ one deterministic serial or framebuffer diagnostic
```

This is a proof of control transfer and the minimum runtime contract. It is not an operating-system release. No desktop, filesystem, networking, full GC, or application loading is required.

## Evidence-based starting point

The UEFI repository is the strongest starting point for this proof because it already demonstrates UEFI file access, ELF segment loading, a packed BootInfo handoff, GOP discovery, memory-map buffering, `ExitBootServices` retry logic, page-table construction, a handoff trampoline, and staged serial/framebuffer markers. Its managed path also contains useful allocation and static initialization probes.

The existing path is not a drop-in .NET 10 solution. It uses a .NET 7-era alpha ILCompiler package, converts a NativeAOT PE image to ELF, and has known limitations around managed module initialization and page-table initialization. The new proof must isolate those limitations and record each ABI assumption.

## Bounded intermediate proofs

Each phase must have one deterministic success marker, one bounded failure path, and a captured artifact or log. Do not turn the phase into an unbounded QEMU or rebuild loop.

| Phase | Proof | Success condition |
|---|---|---|
| 1. Native UEFI entry | Firmware starts the guideXOS-owned loader | Serial marker identifies loader and target architecture |
| 2. Reliable debug output | Serial output works before and after the critical loader operations | Ordered markers survive normal and failure paths |
| 3. Memory-map capture | Loader obtains a final map and retries stale `MapKey`/buffer cases | BootInfo contains validated map pointer, count, descriptor size, and checksum after `ExitBootServices` |
| 4. Payload loading | A bounded test payload is read and its load segments are placed | ELF class/type/segments/entry are validated and the entry address is printed |
| 5. Native bootstrap | Control reaches a small architecture-specific entry with a known stack and handoff record | Bootstrap prints its marker and does not call UEFI services after `ExitBootServices` |
| 6. NativeAOT initialization | The .NET 10 NativeAOT module/runtime initialization contract is exercised without assuming the old helper behavior | Runtime/module initialization returns or reaches the documented entry contract |
| 7. Managed entry | Control reaches `KernelMain`/`ManagedEntry` with the versioned boot record | Managed code emits a deterministic marker |
| 8. Static constructor proof | A deliberately small static initializer runs | Static marker/value is observed in the expected order |
| 9. String/value-type proof | A string literal and a value type are read or formatted without extra subsystems | Deterministic value marker is emitted |
| 10. First allocation proof | One bounded object or array allocation succeeds | Allocation marker is emitted; failure is reported and execution stops |

The first allocation proof must be explicit about whether it demonstrates a functioning GC, a NativeAOT allocation helper backed by a fixed bootstrap allocator, or only the minimum runtime allocation path. Do not claim “full GC” from a single successful allocation.

## Proposed marker sequence

Use a short, stable serial vocabulary so logs can be compared across firmware, QEMU, and later hardware:

```text
GXOS:UEFI
GXOS:DEBUG
GXOS:MAP
GXOS:PAYLOAD
GXOS:BOOTSTRAP
GXOS:NATIVEAOT
GXOS:MANAGED
GXOS:STATIC
GXOS:VALUE
GXOS:ALLOC
```

The exact transport may be serial or a framebuffer marker, but serial should be the primary acceptance channel because it is easier to capture and does not depend on GOP format or pitch. A framebuffer marker can be a secondary confirmation once GOP setup is known to be correct.

## Implementation boundaries

Keep the first proof split into four independently testable contracts:

1. **Boot contract:** UEFI loader produces a versioned, checksummed BootInfo record and transfers control after `ExitBootServices`.
2. **Image contract:** loader and native bootstrap agree on payload format, load address/virtual address policy, entry address, stack, ABI, and relocation policy.
3. **Runtime contract:** the selected .NET 10 NativeAOT output has a documented module initialization and entry-export contract.
4. **Managed contract:** managed code receives only the versioned boot record and emits the diagnostic; it does not initialize desktop, filesystem, networking, or scheduler services.

Start by preserving the existing UEFI ELF handoff shape where it is useful, but replace the PE-to-ELF conversion and old package assumptions with a small, reproducible .NET 10 probe. If the current compiler emits PE for the selected configuration, keep conversion as an isolated experiment with an explicit validation step; do not let conversion details leak into the kernel or App Model ABI.

## Acceptance criteria

The milestone is complete only when:

- the target repository contains a reproducible bounded build command or documented manual command;
- the bootloader, payload, and runtime versions are recorded;
- the final memory map and BootInfo validation occur after `ExitBootServices` without UEFI calls;
- the managed entry marker is observed from a clean boot;
- the static constructor, string/value-type, and first allocation proofs are separately identifiable;
- a failure in any phase stops with a phase-specific marker;
- no desktop, filesystem, network, full GC, or app package behavior is required to pass.

## Staged implementation roadmap

### Stage A: toolchain and contract lock

Record the .NET 10 SDK, NativeAOT compiler/runtime pack, target architecture, output format, linker flags, export mechanism, and exact unmanaged ABI. Add a tiny host-side validator for the produced payload and BootInfo structure.

### Stage B: loader-only proof

Create the smallest guideXOS-owned UEFI loader surface needed to print diagnostics, load a known test image, capture the final memory map, and transfer to a native test entry. Keep page-table and stack policy visible in the handoff record.

### Stage C: native bootstrap proof

Add the architecture-specific stack/trampoline and a native bootstrap that validates the handoff, emits `GXOS:BOOTSTRAP`, and calls the selected NativeAOT entry contract. Test malformed magic, version, checksum, segment bounds, and unsupported ABI as bounded failures.

### Stage D: .NET 10 managed probe

Build a minimal NativeAOT module containing only the entry method and the proofs in the marker table. Start with no filesystem or platform libraries beyond what the compiler/runtime requires. Add runtime hooks only when a failing proof identifies a concrete missing contract.

### Stage E: first kernel boundary

Once managed entry and allocation are proven, define the kernel-owned services behind narrow interfaces: debug output, physical memory discovery, page allocation, and a stop/panic path. Defer interrupts, scheduler, storage, and graphics initialization until this boundary is stable.

### Stage F: later expansion

Bring up interrupts/timer, allocator/page tables, scheduler, display/input, storage/filesystem, and finally AppHost in that order as separate proofs. The App Model remains a contract exercise until the managed payload can launch deterministically.

## Open technical questions for the next experiment

- Does the current .NET 10 NativeAOT toolchain support the required unmanaged export and target output format without a private compiler fork?
- Is the first payload contract best expressed as an ELF ET_EXEC image, a relocatable image, or a PE image converted in a separately validated packaging step?
- Which runtime initialization symbols and generated sections are actually required by the selected .NET 10 output?
- Is a real GC required for the first allocation proof, or can the first proof use a deliberately constrained allocation path while GC support is developed separately?
- Which page-table ownership model is required by the .NET 10 runtime and by the kernel after handoff?

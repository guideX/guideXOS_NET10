# ManagedKernel ABI v1

Phase 1 introduced the first real managed system layer without changing the
accepted NativeAOT proof payload. Phase 2 adds the first machine-state service:
a bounded, immutable view of the normalized boot-time physical-resource map.
The native guideXOS bootstrap/runtime loads the separate
`gxos-managed-kernel.dll` image, starts NativeAOT once, calls the bootstrap
`ManagedMain` export, and then invokes ordinary managed-kernel services through
fixed-layout, versioned contracts.

```text
Native guideXOS bootstrap/runtime
                |
                | GX_MANAGED_KERNEL_ABI_V1
                v
          ManagedKernel
                |
                +-- system initialization
                +-- system-information service
                +-- boot-resource snapshot service
```

The governing design principle is:

> Native guideXOS owns physical-memory truth. ManagedKernel receives a bounded,
> versioned view of that truth through the managed-kernel ABI.

`src/ManagedEntryProbe` remains the foundation control. It is retained for
runtime, scheduler, callback, allocation, and GC regressions. It is not the
managed system project. The new `src/ManagedKernel` project produces the
separate `gxos-managed-kernel` NativeAOT payload.

## Versioning policy

ABI v1 is identified by `GX_MANAGED_KERNEL_ABI_V1 == 1`. Every public
structure starts with a 32-bit `Size` and a 32-bit `AbiVersion` where
applicable. Callers must send a supported version and a structure size at
least as large as the v1 structure. Unknown versions return
`GX_MANAGED_UNSUPPORTED_ABI` before reading or writing versioned payload data.

Future revisions may extend a structure by increasing `Size`; implementations
must only read or write fields covered by the negotiated size. Capability bits
are bounded and additive. Unknown capability bits must be ignored.

The ABI uses Microsoft x64 calling convention entry points, fixed-width C
fields, one-byte packing, and no C++ ABI, reflection marshaling, JSON,
serialization framework, CLR object reference, or managed handle. A managed
object identity never crosses this boundary.

## ABI v1 structures

The authoritative native declarations are in
`src/Gate4Harness/managed_kernel_abi.h`. Managed definitions in
`src/ManagedKernel/ManagedKernel.cs` use `StructLayout(Pack = 1)` and verify
the same sizes and offsets during the bootstrap path.

`GX_MANAGED_KERNEL_INIT_REQUEST_V1` is 16 bytes:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Bytes available for this request; v1 requires 16 or more. |
| 4 | `AbiVersion` | `uint32_t` | Must be 1. |
| 8 | `Architecture` | `uint32_t` | Must be `0x8664` for x64. |
| 12 | `Flags` | `uint32_t` | Must be zero in v1. |

`GX_MANAGED_KERNEL_SYSTEM_INFO_V1` is 32 bytes:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Bytes written; v1 returns 32. |
| 4 | `AbiVersion` | `uint32_t` | Returned interface version; 1. |
| 8 | `ServiceVersion` | `uint32_t` | System-information service version; 1. |
| 12 | `Architecture` | `uint32_t` | Returned architecture; x64 (`0x8664`). |
| 16 | `Capabilities` | `uint64_t` | Bounded capability mask. |
| 24 | `Reserved` | `uint64_t` | Zero in v1; reserved for future use. |

Native `_Static_assert` declarations cover sizes and offsets. The managed
layout check covers sizes and offsets with `sizeof` and `Marshal.OffsetOf`.
The native host ABI test also checks alignment, constants, status values, and
overflow-safe output-buffer validation.

## Status codes

These are guideXOS managed-kernel service statuses, not Win32 last-error
values:

| Name | Value | Meaning |
|---|---:|---|
| `GX_MANAGED_OK` | 0 | Operation succeeded. |
| `GX_MANAGED_INVALID_ARGUMENT` | 1 | A pointer, structure field, or required argument is invalid. |
| `GX_MANAGED_UNSUPPORTED_ABI` | 2 | The requested ABI version is not supported. |
| `GX_MANAGED_BUFFER_TOO_SMALL` | 3 | The caller supplied less than the v1 output size. |
| `GX_MANAGED_NOT_INITIALIZED` | 4 | A service was called before managed-kernel initialization. |
| `GX_MANAGED_ALREADY_INITIALIZED` | 5 | Initialization was requested after readiness was established. |
| `GX_MANAGED_OUT_OF_RANGE` | 6 | A requested normalized region index is not less than `RegionCount`. |

The native caller receives the status directly. Last-error state is not used
to communicate managed-kernel service results.

## Initialization contract

`GxManagedKernelInitialize(uint32 requestedAbiVersion, uintptr requestAddress)`
is an `[UnmanagedCallersOnly]` export. The native bridge resolves it through
the PE export directory and never assumes an RVA. It validates its local
request buffer before crossing the boundary, while managed code validates the
requested version, pointer, size, architecture, and flags before setting the
one-time readiness state.

The valid sequence is:

1. NativeAOT runtime startup occurs once.
2. `ManagedMain` performs bootstrap entry and returns.
3. Native code resolves and calls `GxManagedKernelInitialize`.
4. A valid first call returns `GX_MANAGED_OK` and makes ManagedKernel ready.
5. A valid later call returns `GX_MANAGED_ALREADY_INITIALIZED`.

For Phase 2, the native loader performs the first `ManagedMain` entry before
calling reverse-P/Invoke service exports. This preserves the established
NativeAOT entry/runtime ordering. After that entry returns, native code
initializes and publishes the boot-resource snapshot, and makes a second
bounded `ManagedMain` entry so managed code can consume the installed view.

Initialization is deliberately separate from NativeAOT process startup; the
initialization export itself does not call `ManagedMain`. The current scheduler
is cooperative and single-instance, so initialization is a one-time system
lifecycle transition; future concurrent callers must be serialized by the
native kernel service dispatcher before entering this contract.

## System-information service

`GxManagedQuerySystemInfo(uint32 requestedAbiVersion, uintptr outputAddress,
uintptr outputCapacity)` is an `[UnmanagedCallersOnly]` export. It returns
`GX_MANAGED_NOT_INITIALIZED` before initialization, rejects unknown ABI
versions, rejects null output, and requires at least 32 bytes before writing.
The managed implementation constructs a local fixed-layout result, writes
exactly the v1 structure, and retains no native pointer after return.

The Phase 1 result is intentionally limited to truthful interface data:

```text
ABI=1
SERVICE_VERSION=1
ARCH=X64
CAPABILITIES=0x0000000000000003
```

No canonical guideXOS OS version/build field exists yet, so none is invented.

## Phase 2 boot-resource snapshot service

Native ownership and source of truth

The service is backed by the native `g_memory_map` / `GXOS_UEFI_MEMORY_MAP`
snapshot and its validated `GXOS_MEMORY_CLASSIFICATION`. The final UEFI map is
copied into native retained storage by the existing memory-map acquisition
path; `gxos_uefi_memory_map_parse` validates descriptor size and count, and
`gxos_uefi_memory_map_classify` supplies normalized class totals. The separate
`g_memory_ledger` and physical/accounting snapshots describe current native
allocator/accounting state and are not substituted for the immutable boot map.

Native normalization copies each validated firmware descriptor into the fixed
native `GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1` array. No UEFI descriptor
pointer is retained by ManagedKernel. The normalized type is the stable
guideXOS enum below, not a raw UEFI numeric value:

| Value | Type | Source meaning |
|---:|---|---|
| 1 | `CONVENTIONAL` | `GXOS_MEMORY_CLASS_CONVENTIONAL` |
| 2 | `LOADER_CODE` | `GXOS_MEMORY_CLASS_LOADER_CODE` |
| 3 | `LOADER_DATA` | `GXOS_MEMORY_CLASS_LOADER_DATA` |
| 4 | `BOOT_SERVICES_CODE` | `GXOS_MEMORY_CLASS_BOOT_SERVICES_CODE` |
| 5 | `BOOT_SERVICES_DATA` | `GXOS_MEMORY_CLASS_BOOT_SERVICES_DATA` |
| 6 | `RUNTIME_SERVICES_CODE` | `GXOS_MEMORY_CLASS_RUNTIME_SERVICES_CODE` |
| 7 | `RUNTIME_SERVICES_DATA` | `GXOS_MEMORY_CLASS_RUNTIME_SERVICES_DATA` |
| 8 | `ACPI_RECLAIM` | `GXOS_MEMORY_CLASS_ACPI_RECLAIM` |
| 9 | `ACPI_NVS` | `GXOS_MEMORY_CLASS_ACPI_NVS` |
| 10 | `RESERVED` | `GXOS_MEMORY_CLASS_RESERVED` |
| 11 | `UNUSABLE` | `GXOS_MEMORY_CLASS_UNUSABLE` |
| 12 | `MMIO` | `GXOS_MEMORY_CLASS_MMIO` |
| 13 | `MMIO_PORT_SPACE` | `GXOS_MEMORY_CLASS_MMIO_PORT_SPACE` |
| 14 | `PERSISTENT` | `GXOS_MEMORY_CLASS_PERSISTENT` |
| 15 | `PAL_CODE` | `GXOS_MEMORY_CLASS_PAL_CODE` |
| 16 | `UNKNOWN` | `GXOS_MEMORY_CLASS_UNKNOWN` |

The summary reports the exact descriptor count, the verified native
`total_ram_like_bytes`, the verified conventional/usable total, x64 identity,
and capabilities `SUMMARY | REGIONS | TOTALS` (`0x7`).

Boot-resource summary layout

`GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1` is 56 bytes, packed to one byte:

| Offset | Field | Type |
|---:|---|---|
| 0 | `Size` | `uint32_t` |
| 4 | `AbiVersion` | `uint32_t` |
| 8 | `ServiceVersion` | `uint32_t` |
| 12 | `Architecture` | `uint32_t` |
| 16 | `RegionCount` | `uint32_t` |
| 20 | `ResourceMapIdentity` | `uint32_t` |
| 24 | `TotalPhysicalBytes` | `uint64_t` |
| 32 | `UsablePhysicalBytes` | `uint64_t` |
| 40 | `Capabilities` | `uint64_t` |
| 48 | `Reserved` | `uint64_t` |

Memory-region layout

`GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1` is 32 bytes:

| Offset | Field | Type |
|---:|---|---|
| 0 | `Size` | `uint32_t` |
| 4 | `AbiVersion` | `uint32_t` |
| 8 | `BaseAddress` | `uint64_t` |
| 16 | `Length` | `uint64_t` |
| 24 | `Type` | `uint32_t` |
| 28 | `Flags` | `uint32_t` |

Publication request layout

`GX_MANAGED_KERNEL_BOOT_RESOURCE_PUBLICATION_V1` is 48 bytes. It is a
caller-owned request containing only primitive ABI data; it is not the public
resource array:

| Offset | Field | Type |
|---:|---|---|
| 0 | `Size` | `uint32_t` |
| 4 | `AbiVersion` | `uint32_t` |
| 8 | `SummaryAddress` | `uint64_t` |
| 16 | `DescriptorAddress` | `uint64_t` |
| 24 | `DescriptorCount` | `uint32_t` |
| 28 | `DescriptorSize` | `uint32_t` |
| 32 | `DescriptorByteLength` | `uint64_t` |
| 40 | `Reserved` | `uint64_t` |

All three Phase 2 structures use `Pack = 1`, fixed-width fields, and the
Microsoft x64 calling convention. Native `_Static_assert` checks and managed
`sizeof`/`Marshal.OffsetOf` checks cover every field shown above.

Publication ABI and lifetime

`GxManagedKernelInstallBootResources(uint32 requestedAbiVersion, uintptr
publicationAddress)` rejects null requests, unsupported versions, short request
sizes, zero or over-limit counts, a descriptor size other than 32, mismatched
byte length, reserved bits, invalid summary fields, invalid region types or
flags, zero lengths, `BaseAddress + Length` overflow, descriptor-count
multiplication overflow, and address-plus-length overflow. The maximum is
`GX_MANAGED_KERNEL_BOOT_RESOURCE_MAX_REGIONS == 2048`, a conservative bound
well above realistic x64 firmware maps without being an unbounded trust
surface. Validation completes for every region before any managed publication
state is changed. A second publication returns
`GX_MANAGED_ALREADY_INITIALIZED`; it never replaces the first snapshot.

The published arrays and summary are native static storage and remain valid for
the lifetime of the managed kernel. They are copied values, immutable after
successful normalization/publication, never stack addresses, never reclaimed,
and never pointers into transient UEFI boot-services memory. ManagedKernel
retains only validated native addresses, count, and byte length. It does not
own or allocate physical pages and does not participate in the native physical
allocator.

Query ABI and state machine

`GxManagedQueryBootResources(uint32 requestedAbiVersion, uintptr outputAddress,
uintptr outputCapacity)` returns one complete 56-byte summary. Before
initialization/publication it returns `GX_MANAGED_NOT_INITIALIZED`; null,
undersized, unsupported-ABI, or overflowing output arguments fail without
changing the caller's buffer.

`GxManagedQueryMemoryRegion(uint32 requestedAbiVersion, uint32 index, uintptr
outputAddress, uintptr outputCapacity)` returns one complete 32-byte descriptor
only when `index < RegionCount`. Equal-to-count and greater indices return
`GX_MANAGED_OUT_OF_RANGE`; all failure paths preserve the output sentinel.
Repeated summary and descriptor queries read the same native snapshot and are
stable. No mutable backing pointer or managed array is exposed.

Raw UEFI descriptors are intentionally not the managed ABI: their firmware
layout, descriptor size, attribute bits, and lifetime are firmware contracts,
not stable guideXOS service semantics. The public contract therefore carries a
small, explicitly versioned guideXOS normalization with explicit bounds and
overflow rules.

## Capability bits

| Bit | Name | Meaning |
|---:|---|---|
| 0 | `GX_MANAGED_CAPABILITY_SERVICE_ABI` | The versioned managed service ABI is available. |
| 1 | `GX_MANAGED_CAPABILITY_SYSTEM_INFORMATION` | The v1 system-information service is available. |

These bits describe usable interfaces, not internal test markers. Scheduler
attachment, GC, finalizer behavior, and other runtime facts remain runtime
acceptance evidence and are not advertised as public system-service
capabilities by this phase.

## Pointer, buffer, and lifetime rules

Pointers are opaque native addresses used only for the duration of a call.
They point to caller-owned unmanaged storage, never to a managed object. The
native bridge validates the output address, minimum capacity, and address-plus-
capacity overflow before the call. The managed side repeats null and capacity
checks at the ABI boundary. Failure paths do not partially initialize or
partially populate the output structure; the native acceptance path uses
sentinel-filled buffers to verify this property.

Each v1 query writes only its declared fixed-size output structure (32 bytes
for system information or a region, 56 bytes for the boot-resource summary),
does not retain the output pointer, and returns only fixed-width values. The
native caller owns the storage and may reuse it after return.

## Payload roles and selection

The two payload roles are explicit:

| Mode | Project/payload | Purpose |
|---|---|---|
| `ManagedEntryProbe` | `src/ManagedEntryProbe`, `gxos-managed-entry-probe.dll` | Foundation regression/control, including callback and GC controls. |
| `ManagedKernel` | `src/ManagedKernel`, `gxos-managed-kernel.dll` | Real managed system initialization and services. |

`tools/Build-Gate4Harness.ps1` accepts `-Payload ManagedEntryProbe` or
`-Payload ManagedKernel`. It logs source and staged paths, sizes, SHA-256
identities, and verifies byte identity. It never selects a payload by
directory ordering or modification time. The control payload's authoritative
identity remains governed by its existing acceptance path.

## Current limitations and deferred work

Phase 2 does not add general managed physical-page allocation, virtual-memory
ownership, paging policy, user processes, device drivers, filesystems, GUI
services, full corlib integration,
filesystem, networking, sound, drivers, process management, application
model, thread pool, `Task`, async/await, managed `Thread`, reflection,
dynamic assemblies, rich marshaling, object RPC, or a full managed-kernel
port. The ABI currently exposes the bounded Phase 2 resource-map service and
Phase 3 Host Services v1 (logger plus optional monotonic time), and remains
x64 only. Additional services should follow this versioned, size-checked,
capability-negotiated pattern.

## Phase 3: Managed kernel lifecycle and Host Services v1

Phase 3 keeps the Phase 2 boot-resource ABI intact and adds an explicit
managed-kernel lifecycle plus a small native-owned host-service table. The
normal managed path is exactly one `ManagedMain` bootstrap call followed by
the ordered exports below:

```text
BootstrapAvailable
        --GxManagedKernelInitialize--> Initialized
        --GxManagedKernelInstallBootResources--> EnvironmentInstalling
        --GxManagedKernelInstallHostServices--> Ready
        --GxManagedKernelStart--> Started
```

`GxManagedKernelInitialize` is the only operation that leaves
`BootstrapAvailable`. Boot-resource publication is accepted only from
`Initialized`, Host Services installation only from
`EnvironmentInstalling`, and start only from `Ready`. An out-of-order
operation returns `GX_MANAGED_INVALID_STATE`; a repeated initialize,
resource publication, or Host Services installation returns
`GX_MANAGED_ALREADY_INITIALIZED`. A repeated start also returns
`GX_MANAGED_ALREADY_INITIALIZED`, without re-running callbacks. The loader's
negative vectors exercise these boundaries before the successful transition.

`ManagedMain` is bootstrap-only in the Phase 3 path. It validates the boot
information and ABI layout, emits `MANAGED_KERNEL_BOOTSTRAP_OK`, and returns.
It does not publish resources, install callbacks, start services, or get
called a second time. Phase 2's second-entry experiment remains historical
evidence only and is not part of the normal acceptance path.

### Host Services v1 table

`GX_MANAGED_KERNEL_HOST_SERVICES_V1` is a packed, fixed-width, 56-byte table.
All fields are primitive values; the two callback fields are opaque native
function addresses.

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Must equal 56. |
| 4 | `AbiVersion` | `uint32_t` | Must equal Host Services ABI v1. |
| 8 | `ServiceVersion` | `uint32_t` | Must equal service version v1. |
| 12 | `Architecture` | `uint32_t` | Must equal the x64 architecture value. |
| 16 | `Capabilities` | `uint64_t` | Negotiated interface bits. |
| 24 | `LogUtf8Address` | `uintptr_t` | `ms_abi` UTF-8 logger address. |
| 32 | `MonotonicTimeAddress` | `uintptr_t` | Optional `ms_abi` monotonic-time query address. |
| 40 | `Reserved0` | `uint64_t` | Must be zero. |
| 48 | `Reserved1` | `uint64_t` | Must be zero. |

The managed side requires `SERVICE_ABI` (`1 << 0`) and `LOG_UTF8`
(`1 << 1`). `MONOTONIC_TIME` (`1 << 2`) is optional: the loader advertises it
only when the existing platform-performance source is initialized. Unknown
capability bits, inconsistent optional-time fields, nonzero reserved fields,
wrong size/version/architecture, and null required callback addresses are
rejected before the table is installed. ManagedKernel copies the validated
primitive addresses and capability mask; it does not retain the caller's
table pointer.

The table is caller-owned input. In the loader it is static native storage
that remains valid for the process lifetime, while the managed kernel's
copied callback addresses remain valid for the lifetime of the loaded image.
No managed object, managed delegate, stack address, or transient UEFI buffer
is used as a callback target.

### Host callback contracts

The logger has this Microsoft x64 signature:

```text
uint32_t GXOS_MS_ABI log_utf8(uintptr_t bytes,
                              uintptr_t length,
                              uint32_t flags);
```

`flags` must be zero and `length` must be at most 1024 bytes. A zero-length
call is valid with a null byte address and does not invoke the sink. A
nonzero call requires a non-null, overflow-safe range. The native bridge
validates that the range belongs to a known loader or managed-image range,
then forwards the exact byte range to the existing COM1 serial sink. The
contract is length-delimited UTF-8 bytes; it does not require a trailing NUL,
perform an unbounded scan, allocate, or retain the pointer.

The optional time callback has this signature:

```text
uint32_t GXOS_MS_ABI query_monotonic_time(uint32_t requested_abi_version,
                                          uintptr_t output_address,
                                          uintptr_t output_capacity);
```

The output is `GX_MANAGED_KERNEL_MONOTONIC_TIME_V1`, a packed 40-byte
structure:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Must equal 40. |
| 4 | `AbiVersion` | `uint32_t` | Monotonic-time ABI v1. |
| 8 | `Ticks` | `uint64_t` | Normalized monotonic counter. |
| 16 | `FrequencyHz` | `uint64_t` | Counter frequency; nonzero. |
| 24 | `Flags` | `uint64_t` | Known flags only. |
| 32 | `Reserved` | `uint64_t` | Must be zero. |

The current flag is `NORMALIZED_FROM_START` (`1 << 0`). The value is a
monotonic counter normalized from the native platform-performance start
point; it is not UTC, wall-clock time, or a calendar timestamp. The loader's
implementation uses the already audited invariant-TSC or ACPI PM-timer
source selected by `platform_performance.c`, and the managed start proof
queries it twice and checks nondecreasing ticks with a stable frequency.

### Transition and call-boundary proof

`GxManagedKernelStart` first verifies that the immutable Phase 2 resource
snapshot is still stable, that required Host Services are installed, and
that the optional time contract is internally consistent. It then performs
the managed-to-native logger calls and, when offered, the two monotonic-time
queries. Only after every callback returns `GX_MANAGED_OK` does it enter
`Started` and emit `MANAGED_KERNEL_START_OK`. The native loader records the
callback totals and the final Phase 3 marker.

All Host Services callbacks are normal generated unmanaged function-pointer
calls. They do not use `SuppressGCTransition`, and the loader invokes the
managed exports only after the existing NativeAOT TLS/GS and one-thread
runtime state has been activated. The native logger path is the existing
`serial_write`/COM1 bridge; the managed-to-native route is the direct
`delegate* unmanaged` call from the loaded NativeAOT image. This preserves
the established callback ABI and monotonic-source decisions instead of
introducing a second bridge or clock.

The Phase 3 acceptance rule is deliberately narrow: add the smallest
versioned Host Services v1 surface, keep the normal transition deterministic,
and preserve every previously proven behavior. “Do not reinterpret existing
proof as permission to rewrite the loader's established contracts.”

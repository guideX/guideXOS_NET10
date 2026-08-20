# ManagedKernel Phase 7: Driver Binding and Safe PCI Reads

Phase 7 adds managed driver-selection policy and a narrow read-only PCI
configuration service. It does not add a hardware driver. The ownership rule
is strict:

> ManagedKernel owns matching and binding policy. Native guideXOS owns PCI
> configuration access and all hardware mutation authority.

## PCI Services v1

`GxManagedKernelInstallPciServices` installs one `GX_MANAGED_KERNEL_PCI_SERVICES_V1`
table after the existing ManagedKernel lifecycle has reached `Started`.
Installation validates the requested ABI, table size, service version,
architecture, required/unknown capability bits, callback address, and zeroed
reserved fields. A valid table is accepted once; later installation attempts
return `GX_MANAGED_ALREADY_INITIALIZED` and cannot replace the callback.

The packed table is 48 bytes:

| Offset | Field | Type |
|---:|---|---|
| 0 | `Size` | `uint32_t` |
| 4 | `AbiVersion` | `uint32_t` |
| 8 | `ServiceVersion` | `uint32_t` |
| 12 | `Architecture` | `uint32_t` |
| 16 | `Capabilities` | `uint64_t` |
| 24 | `ConfigReadAddress` | `uint64_t` |
| 32 | `Reserved0` | `uint64_t` |
| 40 | `Reserved1` | `uint64_t` |

The v1 capability is `GX_MANAGED_PCI_CAPABILITY_CONFIG_READ`. The callback is
a Microsoft x64 ABI function with this signature:

```text
uint32_t PciConfigRead(
    uint32_t segment,
    uint32_t bus,
    uint32_t device,
    uint32_t function,
    uint32_t offset,
    uint32_t width,
    uintptr_t resultAddress,
    uintptr_t resultCapacity);
```

The fixed result structure is 32 bytes:

| Offset | Field | Type |
|---:|---|---|
| 0 | `Size` | `uint32_t` |
| 4 | `AbiVersion` | `uint32_t` |
| 8 | `Width` | `uint32_t` |
| 12 | `Reserved0` | `uint32_t` |
| 16 | `Value` | `uint64_t` |
| 24 | `Reserved1` | `uint64_t` |

Supported widths are 1, 2, and 4 bytes. The supported range is the
conventional 256-byte PCI configuration header. Word reads require an even
offset; dword reads require a four-byte offset. Offset-plus-width overflow and
out-of-range requests are rejected. Rejected calls return a managed-kernel
status before writing the result, so caller sentinel bytes remain unchanged.

The service accepts coordinates and an offset only. It never accepts a native
address for the target device or configuration space. Before the hardware
read, native code checks the requested segment/BDF against the immutable,
native-authoritative Phase 6 discovery snapshot. An unknown BDF returns
`GX_MANAGED_NOT_FOUND`; the service is therefore not a general bus scanner.

The current native mechanism is the existing x86 legacy PCI CF8/CFC path:
native code writes the aligned configuration address to port `0xCF8` and reads
one dword from `0xCFC`, then extracts the requested byte or word. Phase 6
discovery is segment-0 only and uses no ECAM, UEFI PCI protocol, or PCI
configuration writes. Phase 7 reuses this mechanism and does not add a write
callback, BAR sizing, capability traversal, MMIO mapping, interrupts, DMA, or
device initialization.

`PciConfiguration` is the managed wrapper. It requires an inventory-owned
`ManagedDevice`, checks the device ownership tag and wrapper-side bounds, calls
the retained native callback, validates the returned result ABI, and exposes
only `TryRead8`, `TryRead16`, and `TryRead32` to managed policy code. Ordinary
managed driver policy does not manipulate a function pointer or arbitrary BDF.

## Managed driver registry

`ManagedDriverRegistry` is a bounded, arena-backed policy object. Registration
is compile-time/static for this phase; no dynamic assembly loading or string
heavy driver framework is involved.

| Limit | Value |
|---|---:|
| Maximum drivers | 8 |
| Maximum rules per driver | 4 |
| Maximum total rules | 16 |
| Priority range | -1000 through 1000 |
| Arena initial backing | 2 pages |
| Arena growth | 2 pages, bounded by safe request size |
| Maximum arena chunks | 4 |
| Maximum arena pages | 8 |
| Persistent arena allocations | driver, rule, and binding tables |

Driver IDs and name tokens are nonzero, stable `uint32_t` values unique within
the registry and immutable after registration. The registry supports these
rule types:

1. exact vendor/device;
2. class/subclass/programming-interface;
3. class/subclass;
4. class-only.

Specificity is deterministic: exact vendor/device (4) wins over
class/subclass/programming-interface (3), which wins over class/subclass (2),
which wins over class-only (1). Equal specificity is resolved by explicit
priority, then earlier registration order. Hash-table or object-reference
iteration order is not part of the decision.

The lifecycle is `register -> freeze -> bind`. Registration after freeze,
binding before freeze, and a second binding pass are rejected. Each inventory
device receives at most one binding. A device without a matching rule remains
`Unbound`. `Matched` is the transient candidate-selection state; the stored
operational result is either `Bound` with one immutable policy owner or
`Unbound` with no stale driver fields. Bound means managed ownership policy,
not hardware initialization.

The invariant checker validates arena ownership and live allocations, driver
ID uniqueness, contiguous rule ranges, rule validity, binding counts, one
owner per device, winner consistency with the precedence algorithm, valid
inventory indices, and zeroed fields for unbound records. Lookup APIs provide
the bound driver, bound-device test, devices for a driver, and aggregate counts.

The operational registry remains alive for the successful boot. Separate
host/QEMU teardown proofs create an unbound registry, release its three arena
allocations and backing chunks, and verify Phase 4/Phase 5 accounting returns
to its instance baseline. The native acceptance check compares managed
kernel-owned accounting state around the non-allocating operational accounting
pass; unrelated NativeAOT/GC heap growth is not classified as a registry leak.

## Acceptance path

Phase 7 installs PCI Services after Phase 6 inventory publication, runs a
managed binding pass over the real QEMU inventory, leaves at least one narrow
nonmatching device unbound, and reads vendor, device, revision,
class/subclass/programming-interface, and header fields for one bound device.
The values must match the immutable Phase 6 descriptor before the managed
success markers are emitted. It then performs Phase 4 memory activity, GC and
runtime activity, repeats a safe read, and validates that the binding and
inventory remain unchanged.

Native and managed negative vectors cover pre-install access, malformed service
tables, unsupported capabilities, reserved fields, duplicate installation,
null/short results, unsupported widths, alignment/range errors, unknown BDFs,
invalid driver IDs/rules/priorities, registry freeze, repeat binding, invalid
lookups, invariant preservation, and teardown. Phase 6 `ResourceCount == 0`
remains truthful because Phase 7 never sizes or maps BARs.

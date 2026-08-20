# ManagedKernel Phase 6: Platform and Device Inventory

Phase 6 adds the first managed platform inventory service. It is deliberately
limited to device identity and class data that the current native bootstrap can
authoritatively read without changing hardware state. The service is installed
after the Phase 4 and Phase 5 proofs, while ManagedKernel is `Started` and the
native page-backed memory service is operational:

```text
Initialize -> boot resources -> memory services -> Host Services -> Start
    -> Phase 4 proof -> Phase 5 arena proof
    -> native PCI snapshot -> InstallDeviceInventory -> query/proof
```

## Authoritative native source

The native loader scans PCI configuration space through the x86 CF8/CFC
mechanism. The scan is read-only: segment 0, buses 0-255, devices 0-31, and
functions 0-7 when the multifunction bit is present. For each present
function, native code records only the segment and BDF, vendor/device/revision
IDs, PCI class/subclass/programming-interface, header type, and the bounded
multifunction flag.

This is a genuine hardware-derived source in the current UEFI/QEMU bootstrap,
not a synthetic device list and not a managed driver database. The source is
kept in native retained storage, normalized before publication, and never
exposes a raw PCI-config pointer to managed code. Duplicate BDFs, absent vendor
IDs, unsupported header layouts, unknown flags, nonzero segments, and capacity
overflow are rejected.

BAR/resource ranges are intentionally not published in v1. Obtaining reliable
BAR lengths normally requires writing all ones to a BAR and restoring it; that
would violate the read-only discovery boundary and this bootstrap does not yet
retain a firmware resource-descriptor source for PCI resources. Therefore v1
publishes `ResourceCount == 0`, and resource queries return unavailable. This is
an explicit capability limitation, not a claim that devices have no hardware
resources.

## Device inventory ABI v1

The public declarations are in `src/Gate4Harness/managed_kernel_abi.h`, with
matching packed managed definitions in `src/ManagedKernel/ManagedKernel.cs`.
Native `_Static_assert` and managed layout checks cover the following sizes and
field offsets.

`GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1` is 40 bytes:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Must equal 40. |
| 4 | `AbiVersion` | `uint32_t` | Device inventory ABI v1. |
| 8 | `ServiceVersion` | `uint32_t` | Service version v1. |
| 12 | `Architecture` | `uint32_t` | x64 (`0x8664`). |
| 16 | `DeviceCount` | `uint32_t` | Present normalized descriptors, max 256. |
| 20 | `ResourceCount` | `uint32_t` | Zero in v1. |
| 24 | `Capabilities` | `uint64_t` | Summary, devices, immutable-snapshot bits. |
| 32 | `Reserved` | `uint64_t` | Must be zero. |

`GX_MANAGED_KERNEL_DEVICE_V1` is 48 bytes:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Must equal 48. |
| 4 | `AbiVersion` | `uint32_t` | Device inventory ABI v1. |
| 8 | `DeviceKind` | `uint32_t` | `PCI` (`1`) in v1. |
| 12 | `Flags` | `uint32_t` | Only the multifunction bit is defined. |
| 16 | `Segment` | `uint16_t` | PCI segment; v1 requires 0. |
| 18 | `Bus` | `uint8_t` | PCI bus number. |
| 19 | `Device` | `uint8_t` | PCI device number. |
| 20 | `Function` | `uint8_t` | PCI function number, less than 8. |
| 21 | `ReservedLocation` | `uint8_t` | Must be zero. |
| 22 | `VendorId` | `uint16_t` | PCI vendor ID; 0 and `0xFFFF` are absent. |
| 24 | `DeviceId` | `uint16_t` | PCI device ID. |
| 26 | `RevisionId` | `uint8_t` | PCI revision ID. |
| 27 | `ClassCode` | `uint8_t` | PCI base class. |
| 28 | `Subclass` | `uint8_t` | PCI subclass. |
| 29 | `ProgrammingInterface` | `uint8_t` | PCI programming interface. |
| 30 | `HeaderType` | `uint8_t` | PCI header type; layout 0-2 supported. |
| 31 | `ReservedClass` | `uint8_t` | Must be zero. |
| 32 | `ResourceStartIndex` | `uint32_t` | Zero in v1. |
| 36 | `ResourceCount` | `uint32_t` | Zero in v1. |
| 40 | `Reserved` | `uint64_t` | Must be zero. |

`GX_MANAGED_KERNEL_DEVICE_INVENTORY_PUBLICATION_V1` is 48 bytes and is a
caller-owned native publication record:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Must equal 48. |
| 4 | `AbiVersion` | `uint32_t` | Device inventory ABI v1. |
| 8 | `SummaryAddress` | `uintptr_t` | Native immutable summary. |
| 16 | `DescriptorAddress` | `uintptr_t` | Native immutable descriptor array. |
| 24 | `DescriptorCount` | `uint32_t` | Must match summary and be 1-256. |
| 28 | `DescriptorSize` | `uint32_t` | Must equal 48. |
| 32 | `DescriptorByteLength` | `uintptr_t` | Exactly count times 48. |
| 40 | `Reserved` | `uint64_t` | Must be zero. |

`GxManagedKernelInstallDeviceInventory` validates every publication field,
pointer range, summary, descriptor, BDF uniqueness, resource absence, and
arena capacity before making the managed inventory visible. A failed install
leaves the not-initialized state intact; a second successful install returns
`GX_MANAGED_ALREADY_INITIALIZED`.

`GxManagedQueryDeviceInventorySummary` returns the copied 40-byte summary.
`GxManagedQueryDevice` returns one copied 48-byte descriptor for an index less
than `DeviceCount`; equal-to-count and larger values return
`GX_MANAGED_OUT_OF_RANGE`. Unsupported ABI, null, short, and overflowing output
arguments preserve the caller's sentinel. Repeated queries remain stable.

## Managed inventory and arena ownership

ManagedKernel copies the native snapshot into a bounded `ManagedDeviceInventory`
backed by the Phase 5 `KernelArena`. The inventory reserves three persistent
arena allocations: 12,288 bytes for up to 256 descriptors, 2,048 bytes for a
BDF index, and 1,024 bytes for a class/subclass/programming-interface index.
The default bound is two initial pages, two-page growth, four chunks, eight
total pages, eight live allocations for this subsystem, and 4096-byte maximum
alignment. Creation is transactional; allocation failures release all partial
state, and `Destroy` requires exact frees before releasing native backing.
BDF lookup, first-class lookup, index lookup, duplicate detection, invariant
checks, GC activity, and temporary-copy teardown are all covered. The
persistent inventory remains operational after the temporary proof is
destroyed; its accounting is the Phase 6 baseline so only temporary
allocations must restore exactly.

## Acceptance and host tests

Native vectors in `src/Gate4Harness/tests/managed_kernel_device_inventory_tests.c`
cover synthetic callback discovery, multifunction scanning, normalization,
capacity, duplicate BDFs, absent vendor IDs, unsupported header layouts, and
unknown flags. Managed vectors in
`src/ManagedKernelDeviceInventoryHostTests/Program.cs` cover arena growth,
index/BDF/class queries, resource unavailability, GC survival, rollback, and
destroy/release behavior. Run them with:

```powershell
.\tools\Run-ManagedKernelDeviceInventoryHostTests.ps1
.\tools\Run-ManagedKernelDeviceInventoryManagedHostTests.ps1
```

The full gate uses the EventWait-profile harness with the QEMU RTC UTC policy
and three fresh firmware-variable copies. It requires native snapshot
readiness, read-only PCI discovery, negative installs, byte-for-byte
native/managed descriptor equality, uniqueness, arena ownership, BDF and class
lookup, explicit resource unavailability, runtime survival, temporary-copy
negative tests, teardown, accounting restoration, operational survival, and
exactly one `MANAGED_KERNEL_PHASE6_PASS` per boot.

## Delivered verification record

The delivered rebuild used the installed .NET 10.0.400 fallback SDK through
the repository's direct-MSBuild workaround. The ManagedKernel payload is
873,984 bytes with SHA-256
`2F4AA52B3A235F807C30DE26875566A011DC8CBBF513ABA47957817C94C666CD`.
The staged EventWait-profile UEFI harness is
`artifacts/managed-kernel-gate4/ESP/EFI/BOOT/BOOTX64.EFI` with SHA-256
`35B25FC6447C6D7A71B552BE56E40D229319C45847667185C426C83429EFCB0D`.

Three fresh QEMU boots passed using
`artifacts/managed-kernel-phase6-evidence-delivered`:

| Run | Serial bytes | Serial SHA-256 |
|---:|---:|---|
| 1 | 523,884 | `593C6B77914749407BCA3872C15B37205819FBF7C9A15C40B50585250B338043` |
| 2 | 523,884 | `58F31DC1691A3CEA73CA33FED03D71874734D9585C71D3ADCF75D7BCE6A81F1C` |
| 3 | 525,407 | `2AD7083782B9E030CA1BD81BD76E781E8AA7E5286641FB1FA3596CA1B95CF159` |

Each run discovered six native PCI devices, published zero resource records,
matched every managed descriptor byte-for-byte, and emitted exactly one Phase
6 pass marker. The ABI, arena, native inventory, and managed inventory host
suites also passed. No commit, push, merge, rebase, amend, branch switch, or
stash operation was performed.

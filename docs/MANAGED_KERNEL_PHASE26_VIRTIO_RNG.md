# Phase 26 — Managed virtio-rng entropy provider

Phase 26 adds a bounded managed secure-random provider for the modern,
non-transitional QEMU virtio-rng PCI device. The production path is
hardware-first: the existing RDSEED/RDRAND service is tried first, followed by
virtio-rng. There is no timing, deterministic, or zero-filled success
fallback. If no provider succeeds, the request fails closed.

## Device and authority boundary

The QEMU device is discovered at `0000:00:03.0`, vendor/device
`1AF4:1044`, with subsystem `1AF4:1100`. Transitional virtio-rng is rejected;
only the modern PCI transport is accepted. The native harness publishes BAR
resources and owns PCI configuration, MMIO mapping, DMA allocation, physical
addresses, and native mapping/claim generations. Managed code receives only
validated device descriptors, opaque native handles, bus addresses, and
bounded accessors.

The capability parser validates the modern common-configuration and notify
capabilities, BAR bounds, offsets, lengths, and notify multiplier. QEMU emits
a zero-length vendor-specific PCI capability; that known capability type is
accepted without weakening the validation of the capabilities used by the
driver.

## Queue and request contract

The driver uses one split virtqueue with queue size one. A page-sized native
DMA allocation holds the descriptor table and rings; a separate 1,024-byte
native DMA allocation holds entropy bytes. Each request is at most 1,024
bytes. The descriptor is device-writable, the queue notification is issued
through the validated notify mapping, and completion is observed by a finite
used-ring poll. Interrupts, indirect descriptors, chained buffers, and
unbounded waits are outside this phase.

The provider stores transport state as scalar native mapping/DMA handles and
validated lengths. The mapping catalog records native mapping handles so an
owner-scoped abort can close every mapping and claim after an early proof
failure. This is important for the NativeAOT GC boundary: no heap-backed
mapping or DMA wrapper is retained by the long-lived production transport.

## Lifecycle proof

The exercised lifecycle is:

`Created -> Claimed -> Mapped -> QueueReady -> Running -> Stopped`

The boot proof verifies discovery, modern transport selection, queue setup,
provider availability, a 64-byte fill, explicit GC, a second 64-byte fill,
queue/DMA/PCI/MMIO teardown, a 16-byte reinitialization fill, and a second
complete teardown. It also verifies that only the virtio driver’s claims are
released; the shared catalog may contain unrelated driver claims.

The Phase 13 MMIO proof now aborts its owner-scoped mappings and claims on
blocked early exits. This prevents an intentionally tolerated MMIO control
path from poisoning the Phase 26 and Phase 14 claim preconditions.

## Evidence

The clean canonical payload was built with the installed .NET 10.0.400
fallback toolchain:

- payload: `artifacts/managed-kernel/publish/gxos-managed-kernel.dll`
- size: `1,288,704` bytes
- SHA-256: `7778FFE7E4A46EC280F3ED31CEEEBC9B3DFA8EF5F4AB5151F52A599481F17427`
- Gate 4 image: `artifacts/gate4-phase26-final-clean/ESP/EFI/BOOT/BOOTX64.EFI`
- three-boot evidence: `artifacts/evidence-phase26-final-clean-rerun/`
- host suite: `MANAGED_KERNEL_PHASE26_HOST_TESTS_PASS cases=70`

All three final fresh boots passed the Phase 26 markers and continued through
the Phase 14/15/23 harness checks. The PE audit still shows the historical
NativeAOT `bcrypt.dll!BCryptGenRandom` import; the managed-kernel entropy path
does not call it. The virtio provider uses only the declared native PCI/MMIO/
DMA services and the QEMU device described above.

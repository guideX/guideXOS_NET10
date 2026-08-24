# Phase 14 — First Managed PCI Driver

Phase 14 adds the first operational managed PCI driver to the NativeAOT
ManagedKernel path.  The target is the QEMU e1000e/82574L-compatible Ethernet
controller exposed at `0000:00:02.0`, PCI ID `8086:10D3`, class `02/00/00`.

## Boundary and ownership

The native Gate 4 harness remains the authority for hardware capabilities.  It
publishes the PCI device resources, validates claims and generations, maps the
BAR, performs the final volatile MMIO access, performs PCI command
read/modify/write operations for the claimed device, and owns DMA allocation
and physical-address discovery.  Managed C# owns the e1000 policy: device
validation, lifecycle, register order, ring indices, descriptor bytes, packet
construction and validation, bounded polling, diagnostics, and teardown.

There are no raw MMIO pointers, physical-address mapping inputs, BAR spans, or
unrestricted PCI configuration writes in the managed surface.  MMIO writes
are currently limited to validated 32-bit aligned accesses through an opaque
mapping with writable permission.  PCI command access is restricted to the
claimed `8086:10D3` PCI resource and the two command bits needed here.

## PCI command handling

The driver reads the original 16-bit PCI command value and requests Memory
Space Enable (`0x0002`) and Bus Master Enable (`0x0004`).  The native callback
validates the live resource claim and BDF/identity, reads configuration space,
ORs only the requested bits, and writes the resulting lower 16 bits while
preserving unrelated command bits.  The original and resulting values are
logged.  On teardown the original command value is restored through the same
validated capability, and bus mastering is confirmed disabled before the
claim is released.

## DMA model and coherence

DMA allocations are native-authoritative opaque capabilities.  The bounded
Phase 14 service has eight allocation slots, at most 32 pages per allocation,
and at most 64 live pages in total.  Each allocation records its generation,
claim owner, driver owner, size, alignment, page count, virtual alias, and
device-visible bus address.  QEMU's 82574L model is used on an x86-64 machine;
the allocation source is contiguous UEFI `AllocatePages` memory and the
physical ledger records every page.  The managed driver receives the bus
address only as data needed for e1000 registers and cannot nominate an
arbitrary managed pointer as DMA memory.

The QEMU x86-64 DMA path is cache-coherent ordinary RAM.  DMA pages are mapped
as normal write-back RAM, not UC/MMIO.  Physical contiguity is provided for
each bounded allocation, alignment is page-based, and the device supports the
full 64-bit bus address accepted by the service.  No explicit cache flush or
invalidate is required for this coherent platform; the driver uses descriptor
ownership and bounded polling as the ordering protocol.  A future
non-coherent platform must add an explicit cache-maintenance capability rather
than silently reusing this assumption.

## Rings, buffers, and device initialization

The driver uses eight TX descriptors and eight RX descriptors.  Each legacy
e1000 descriptor is exactly 16 bytes.  TX and RX rings are each 128 bytes and
are page-aligned.  Eight 2048-byte TX buffers and eight 2048-byte RX buffers
are held in separate native DMA allocations.  Descriptor fields are written
explicitly as little-endian bytes; the implementation does not depend on C#
struct packing.

The sequence is deliberately small and follows the QEMU e1000e register
model: validate `STATUS`, disable RX/TX while configuring, read `RAL/RAH`,
program ring base/length/head/tail registers, set `RCTL` and `TCTL`, and use
the minimum transmit inter-packet gap.  Receive broadcast is enabled for the
proof frame.  EEPROM access, reset, checksum offload, VLANs, jumbo frames,
interrupts, and MSI/MSI-X are not used.

## TX and RX proof

Managed code constructs a 60-byte Ethernet broadcast frame with the actual NIC
MAC as source and experimental EtherType `0x88B5`.  The payload contains the
Phase 14 signature.  It writes the packet through the DMA capability, creates
the TX descriptor with EOP/IFCS/RS, advances the tail, and polls for the DD
completion bit with a finite limit.  The `TX_SUBMITTED` and `TX_COMPLETED`
markers therefore represent descriptor progress observed through the device's
DMA memory, not a synthetic native completion.

RX is enabled and the RX ring is fully configured.  If a deterministic frame
is injected by a future QEMU netdev peer, managed code consumes and validates
the descriptor and recycles the buffer.  The current Phase 14 harness does
not attach an external netdev peer, so production boots truthfully classify
RX as `RX_HARNESS_DEFERRED`; no bytes are copied into the RX buffer by native
test code and no public network dependency is used.

## Lifecycle and teardown

The effective lifecycle is:

`Created -> Claimed -> Mapped -> DmaReady -> Initialized -> Running -> Stopping -> Stopped`

Teardown disables RX/TX, verifies the submitted descriptor is no longer owned
by hardware, releases the managed DMA references and allocation handles,
restores the original PCI command, unmaps the BAR, releases the resource
claim, and verifies zero active claims.  Native DMA service teardown then
rejects further allocations and confirms zero live allocations before the
MMIO service is torn down.  This ordering prevents freeing descriptor or
packet memory while the NIC can still access it.

The driver performs `GC.Collect()` while rings and buffers are live.  The
native DMA allocations remain stable because they are native-authoritative;
the managed object retains only opaque handles and validated bus-address
data.  The driver re-reads `STATUS` after collection and checks that both ring
bus addresses are unchanged.

## Negative tests and accounting

Managed host tests cover PCI command planning, MMIO write bounds/alignment and
permissions, DMA request limits, generation-independent ring wraparound,
descriptor ownership/completion, MAC validation, and proof-frame validation.
Boot-time negative tests cover forged/wrong-owner/read-only/stale MMIO writes
and zero/oversized/wrong-owner/double-free/retained/stale DMA handles.  Native
capabilities reject invalid handles before touching hardware.  Resource,
mapping, claim, DMA slot/page, and ring/buffer accounting is checked at
teardown and returns to zero.

## Deliberate limitations and next phase

Phase 14 is not a networking stack.  It does not provide ARP, IPv4/IPv6,
DHCP, UDP/TCP, DNS, sockets, routing, firewalling, multiple NIC support, or
production network configuration.  Interrupt-driven operation, MSI/MSI-X,
checksum offload, VLANs, jumbo frames, scatter/gather, and zero-copy packet
frameworks are deferred.  The next phase can add a deterministic QEMU netdev
peer for RX proof and then build higher-level networking on top of this bounded
device capability.

## Phase 15: deterministic managed e1000 receive

Phase 15 extends the Phase 14 RX setup into a single-buffer, bounded receive
proof.  The guest emits `MANAGED_E1000_RX_READY` only after the native-owned
descriptor and packet-buffer allocations, RDBAL/RDBAH/RDLEN/RDH/RDT, and RCTL
configuration are live.  The host harness then sends exactly one 60-byte
Ethernet frame through QEMU's documented local UDP `dgram` backend.  The
backend was selected because it is host-only, needs no Internet, TAP device,
administrator privilege, or external infrastructure.  The installed Windows
QEMU 11.0.0 dgram backend was also tested directly; it accepted the local
configuration and host datagram but did not produce an RX descriptor
completion in this environment, so the authoritative hardware result remains
deferred (Outcome B) until a compatible local peer is available.

The test frame is deterministic except for its destination, which is the
runtime RAL/RAH MAC discovered by the managed driver:

* destination: runtime e1000 MAC (`52:54:00:12:34:56` in the proof image)
* source: `02:15:00:00:00:01`
* EtherType: `0x88B5`
* payload signature: `guideXOS ManagedKernel Phase15 RX`
* sequence: big-endian `0x15000001`
* total length: 60 bytes

RCTL enables receive, broadcast filtering, 2048-byte buffers, and strip-CRC
behavior.  Therefore the descriptor length expected by this proof is 60
bytes: the emulated Ethernet FCS is not included in the DMA buffer.  The
managed path validates DD, EOP, nonzero length, buffer capacity, receive
errors, descriptor index, ring wraparound, the complete Ethernet header, the
signature/sequence, and zero padding.  It copies only through the bounded
opaque DMA capability.  A successful frame is recycled by clearing the
descriptor status and advancing RDT; duplicate or out-of-order ownership is
rejected.

The synchronization is bounded: Phase 15 readiness is observed in serial
evidence, the host sends one frame, and the guest must report
`MANAGED_E1000_RX_COMPLETE`, `MANAGED_E1000_RX_FRAME_OK`, and
`MANAGED_KERNEL_PHASE15_PASS`.  A missing or unsupported peer produces
`MANAGED_E1000_RX_HARNESS_DEFERRED` and
`MANAGED_KERNEL_PHASE15_RX_HARNESS_DEFERRED` after finite polling; it is never
converted into a manufactured completion.  GC pressure runs while the native
DMA resources remain live, and the existing stop/quiesce, bus-master restore,
DMA release, MMIO unmap, claim release, and accounting checks run on both the
receive-success and bounded-failure paths.

This milestone's successful acceptance statement is:

`REAL MANAGED E1000 RECEIVE DMA`

It does not prove interrupt-driven NIC operation, an Ethernet/network stack,
IP networking, or Internet connectivity.  ARP, IPv4, IPv6, ICMP, UDP, TCP,
DHCP, DNS, sockets, MSI/MSI-X, legacy NIC interrupts, jumbo/scatter-gather
receive, checksum/segmentation offload, and multiple-NIC support remain
outside Phase 15.

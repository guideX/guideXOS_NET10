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

## Phase 26 shared PCI resource boundary

The modern virtio-rng provider added in Phase 26 uses the same native resource
catalog and claim-generation rules. Its QEMU identity is `1AF4:1044` at
`0000:00:03.0`; it is intentionally separate from the e1000e owner
`8086:10D3` at `0000:00:02.0`. A virtio driver claim is released before the
Phase 14 e1000 claim is attempted. Claim assertions are owner-scoped because
the catalog can legitimately contain claims belonging to another live driver.

The resource catalog also records each native mapping handle. An early
driver-proof exit can therefore unmap and release only the failed owner’s
resources, preserving the e1000 lifecycle and the shared native accounting.

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
backend is host-only, needs no Internet, TAP device, administrator privilege,
or external infrastructure.

### Phase 15 Outcome B and Phase 15B closure

The first unchanged Phase 15 reproduction was truthful Outcome B: the host
sent one frame and the guest reached its bounded RX deferral without claiming
completion.  Phase 15B added an independent QEMU `filter-dump` PCAP on the
actual `net0` dgram backend and installed-QEMU receive trace events.  The
PCAP parser requires the destination MAC, source MAC, EtherType, signature,
sequence, length, exact bytes, and SHA-256; it does not accept packet count
alone.

The initial all-direction capture proved the Phase 15 frame entered QEMU's
netdev, but the receive trace showed no e1000e callback.  The decisive failure
logs also showed that the serial `RX_READY` observation and Windows UDP send
could occur after the original 50-million-iteration guest poll had already
timed out.  Thus the frame was independently present at the QEMU dgram
frontier, while the guest had already emitted the truthful deferred result.
The guest snapshot ruled out the ring hypotheses: before injection the
hardware-visible values were `STATUS=0x00080283` (link up),
`RCTL=0x04008002` (receive enabled, broadcast accepted, 2048-byte buffers,
strip CRC), `RDBAL=0x05289000` (run-dependent native DMA address),
`RDBAH=0`, `RDLEN=0x80`, `RDH=0`, and `RDT=7`.  The eight-descriptor ring was
therefore nonempty; `RDH==RDT` was not the defect.

The narrow fix is a bounded receive-readiness barrier.  The driver validates
link-up, RCTL enable, descriptor bounds, and `RDH != RDT`, re-posts the
hardware-visible tail, and emits `MANAGED_E1000_RX_CONFIGURED` before
`MANAGED_E1000_RX_READY`.  While waiting for this single frame it periodically
re-posts the unchanged owned tail, which is safe e1000 ownership maintenance
and causes QEMU e1000e to flush a packet held during a transient
`can_receive=false` window.  The bounded poll window is one billion guest
iterations, not a wall-clock sleep; an absent frame still fails closed and
tears down.  The authoritative harness uses
`filter-dump,...,queue=tx`, the installed QEMU direction that captures
host-to-guest datagrams.  This avoids the observed all-direction diagnostic
perturbation while preserving an independent PCAP boundary.

Three fresh Phase 15B boots now prove the complete path:

`REAL HOST -> QEMU DGRAM -> E1000 -> PCI DMA -> MANAGED C# RECEIVE`

Each produced exactly one 60-byte PCAP match with SHA-256
`CAFF6094F057FBBFE83BF82A83072CE36D03C40EFAF23C1F24E50D490445D68E`, one
hardware DD/EOP completion, one managed frame validation, one recycle, GC
survival, teardown, and accounting restoration.  Phase 14 authentic TX
remains green.  This project now proves:

`REAL MANAGED E1000 TX AND RX DMA`

It still does not prove interrupt-driven NIC operation, INTx/MSI/MSI-X,
Ethernet protocol handling, ARP, IP networking, sockets, or Internet
connectivity.  Scatter/gather, jumbo frames, checksum/segmentation offload,
and multiple-NIC support remain outside Phase 15B.

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

`REAL MANAGED E1000 TX AND RX DMA`

It does not prove interrupt-driven NIC operation, an Ethernet/network stack,
IP networking, or Internet connectivity.  ARP, IPv4, IPv6, ICMP, UDP, TCP,
DHCP, DNS, sockets, MSI/MSI-X, legacy NIC interrupts, jumbo/scatter-gather
receive, checksum/segmentation offload, and multiple-NIC support remain
outside Phase 15.

## Phase 16: bounded managed Ethernet and ARP

Phase 16 adds `ManagedEthernetLayer` and `ManagedArpLayer` above the existing
e1000 transport.  The ownership boundary is strict:

* `ManagedE1000Driver` owns PCI/MMIO/DMA, descriptors, device completion,
  packet-buffer transport, and hardware teardown.
* `ManagedEthernetLayer` owns bounded Ethernet II headers, destination policy,
  EtherType dispatch, minimum-frame padding, and local-MAC handling.
* `ManagedArpLayer` owns Ethernet/IPv4 ARP parsing and construction, one
  pending resolution, the configured test addresses, responder policy, and
  the bounded cache.

Only EtherType `0x0806` is interpreted by the protocol layer.  Ethernet frames
must be at least 60 bytes, no larger than the 2048-byte e1000 packet buffer,
and addressed to the local MAC or broadcast.  ARP accepts only Ethernet/IPv4
  packets with HLEN 6, PLEN 4, and Request or Reply opcodes.  Source-MAC and
  ARP sender-MAC agreement is checked before dispatch.

The deterministic Phase 16 topology is deliberately not IP networking:

* guest IPv4: `10.15.0.1`
* host IPv4: `10.15.0.2`
* host test MAC: `02:15:00:00:00:02`
* guest MAC: runtime `RAL/RAH` discovery (`52:54:00:12:34:56` in QEMU)

The ARP cache has eight fixed entries.  It uses an empty slot first and then
replaces the lowest generation; lookup refreshes generation.  The runtime
learns only a validated Reply that satisfies the one pending host resolution,
so malformed, unrelated, or unsolicited traffic cannot populate policy state.
All protocol receive loops are finite.  Teardown stops protocol acceptance,
clears the pending operation and cache, then follows the established e1000
quiesce, DMA release, PCI restore, MMIO unmap, and accounting checks.

The authoritative host peer uses QEMU's local UDP `dgram` netdev and no TAP,
administrator privilege, Internet, or external NIC.  Each Phase 16 run
validates these four exact 60-byte frames through QEMU `filter-dump`:

`guest Request -> host Reply -> host Request -> guest Reply`

`tools/Parse-ManagedE1000Phase16Pcap.ps1` checks Ethernet and ARP fields,
padding, packet lengths, frame order/count, and SHA-256 values.  The focused
host suite is `tools/Run-ManagedKernelPhase16HostTests.ps1`; the authoritative
three-boot runner is `tools/Run-ManagedKernelPhase16FreshBoots.ps1`.

The runtime proof markers are intentionally limited to the protocol milestones
`MANAGED_ETHERNET_READY`, `MANAGED_ARP_READY`,
`MANAGED_ARP_RESOLUTION_STARTED`, `MANAGED_ETHERNET_TX_ARP_REQUEST`,
`MANAGED_ETHERNET_RX_ARP`, `MANAGED_ARP_REPLY_VALID`,
`MANAGED_ARP_CACHE_LEARNED`, `MANAGED_ARP_RESOLUTION_COMPLETE`,
`MANAGED_ARP_REQUEST_FOR_LOCAL`, `MANAGED_ARP_REPLY_SENT`,
`MANAGED_ARP_RESPONDER_PASS`, `MANAGED_KERNEL_PHASE16_GC_SURVIVAL_PASSED`,
and `MANAGED_KERNEL_PHASE16_PASS`.  The last marker is emitted only after
both ARP directions pass and normal Phase 14 teardown samples that proof.

Phase 16 proves Ethernet/ARP framing over the authentic managed e1000 TX/RX
path.  It does not implement IPv4 packet processing, checksums, ICMP, ping,
UDP, TCP, DHCP, DNS, sockets, routing, IPv6/ND, VLANs, promiscuous mode,
interrupt-driven networking, multiple interfaces, jumbo/scatter-gather
frames, offloads, TAP networking, or Internet access.

The finalized acceptance evidence is under
`evidence/phase16-authoritative-final-v2-20260824` (three fresh boots) with
payload size `1,060,864` and SHA-256
`BA70A8D58232A1F4489DBAAE1C247880D6C6FE61F6949A3D7A1A25711CA5A6B5`.
The focused Phase 15 control also passes three fresh boots under
`evidence/phase15-control-final-v2-20260824`.

## Phase 17: bounded managed IPv4 and ICMP echo

Phase 17 makes the managed kernel the owner of the first bounded IPv4 data
path.  The pipeline is:

`E1000 RX -> managed Ethernet II -> managed IPv4 -> managed ICMPv4`

and, for transmit:

`managed ICMPv4 -> managed IPv4 -> managed ARP/Ethernet II -> E1000 TX`.

Native code remains limited to PCI, MMIO, DMA, descriptor, and ABI mechanics.
`ManagedE1000Driver` still owns hardware and DMA buffers.  It copies a
completed descriptor into a rooted managed RX buffer and recycles the
descriptor before `ManagedEthernetLayer` dispatches the bounded copy.  The
Ethernet layer owns EtherType dispatch; `ManagedIpv4Layer` owns IPv4 policy,
ICMP echo policy, and the fixed diagnostic state.  No span or pointer into an
RX descriptor survives ownership return to the E1000 ring.

The driver uses a fixed 16-entry RX/TX descriptor ring and the existing
2048-byte packet buffers.  The larger fixed ring leaves room for the
Phase 17 proof sequence to drain deterministically without changing the
ownership model.  ARP remains an eight-entry fixed cache.  IPv4 has one
fixed-capacity pending transmission slot; a second pending packet fails
closed and emits `MANAGED_IPV4_PENDING_OVERFLOW`.

### Supported IPv4 subset

The deterministic configuration is:

* local IPv4: `10.15.0.1`
* peer IPv4: `10.15.0.2`
* subnet mask: `255.255.255.0`
* peer MAC: `02:15:00:00:00:02`
* guest MAC: runtime E1000 RAL/RAH (`52:54:00:12:34:56` in the proof image)

Only same-subnet destinations are directly reachable.  No gateway or DHCP
configuration exists.  IPv4 accepts version 4, IHL 5, a complete 20-byte
header, a valid total length within the received Ethernet payload, and a
valid one's-complement header checksum.  DSCP/ECN is zero, TTL is 64, and the
identification is deterministic (`0x1700 + sequence` for managed pings and
`0x1800 + sequence` for managed echo replies).  The only dispatched protocol
is ICMP (`1`).

IPv4 options (`IHL > 5`), IHL below 5, truncated headers, impossible lengths,
bad checksums, nonlocal destinations, and unsupported protocols are rejected
or ignored within the bounded dispatcher.  More Fragments and nonzero
fragment offsets are rejected; Don't Fragment alone is accepted.  No
fragment reassembly is implemented.

### ICMPv4 echo behavior

The managed ICMP layer supports only Echo Request (`type 8, code 0`) and Echo
Reply (`type 0, code 0`).  It validates the eight-byte header, one's-
complement checksum, code, and bounded payload length before dispatch.  Echo
requests addressed to `10.15.0.1` receive a managed reply preserving the
identifier, sequence, and payload.  Managed-originated pings use identifier
and sequence pairs `(0x1701, 1)` and `(0x1702, 2)`.  The peer uses
`(0xBEEF, 7)` for the responder proof.  Malformed requests never generate a
reply.

### GC, teardown, and malformed-input proof

Protocol state is held in fixed arrays, fixed packet buffers, one fixed
pending IPv4 copy, and bounded counters.  `TryStop` clears IPv4 configuration
activity, request tracking, responder state, diagnostics, and pending bytes
before the established E1000 stop path disables engines, releases DMA,
restores PCI state, unmaps MMIO, and releases the device claim.

Each authoritative boot performs valid traffic, five malformed wire controls
(bad IPv4 header checksum, impossible total length, fragmentation, invalid
ICMP Echo code, and a nonlocal destination), managed GC survival, and a
second valid ping/reply after GC.  The malformed controls produce no replies
and later traffic remains valid.  The host suite additionally covers a bad
ICMP checksum, truncated IPv4/ICMP inputs, odd checksums, zero payload, and
maximum bounded payload.  All parsing rejects before attacker-controlled
lengths are used and no malformed packet exception escapes dispatch.

### Authoritative boot and PCAP proof

The Phase 17 host tests are run with
`tools/Run-ManagedKernelPhase17HostTests.ps1`; the three-boot authoritative
runner is `tools/Run-ManagedKernelPhase17FreshBoots.ps1`.  Each boot is a new
QEMU process using the local UDP `dgram` peer and QEMU `filter-dump` with
`queue=all`.  The independent parser is
`tools/Parse-ManagedE1000Phase17Pcap.ps1`; serial or transmit logs alone are
not accepted as wire proof.

The final evidence is under
`evidence/phase17-authoritative-final-v9-20260824`.  Every one of the three
PCAPs contains 17 packets: two Phase 15 proof frames, then the following
exact logical sequence:

`guest ARP request -> host ARP reply -> host ARP request -> guest ARP reply ->`
`guest Echo Request 0x1701/1 -> host Echo Reply ->`
`bad-header-checksum -> impossible-length -> fragmented -> invalid-ICMP-code -> wrong-destination ->`
`host Echo Request 0xBEEF/7 -> guest Echo Reply ->`
`guest post-GC Echo Request 0x1702/2 -> host Echo Reply`.

The validator independently checks Ethernet MACs and EtherTypes, ARP
operation and addresses, IPv4 version/IHL/length/flags/offset/TTL/protocol/
checksum/source/destination, ICMP type/code/checksum/identifier/sequence,
exact payload bytes, packet order, and exact frame counts.  It reports
`packets=17 arp=4 ipv4_icmp=6 malformed=5` for each fresh boot.

The focused Phase 17 host suite passes 48 cases.  The Phase 15 host controls
pass 28 cases, the Phase 16 host controls pass 57 cases, and the final
Phase 16 three-boot regression passes under
`evidence/phase16-regression-phase17-20260824`.  The Phase 17 payload is
`1,083,392` bytes with SHA-256
`7F39C6D082B2579BAD7867A29FD7F3C840E3A843D6419607A6F3B4F87409984A`.

Phase 17 intentionally defers UDP, TCP, DHCP, DNS, IPv4 reassembly, IPv4
options, IPv6/ND, routing, sockets, and a public network configuration/API
surface.  Interrupt-driven networking, offloads, VLANs, jumbo/scatter-
gather frames, multiple interfaces, and Internet access also remain outside
this phase.

## Phase 18: bounded managed UDP foundation

Phase 18 extends the existing managed path by one protocol layer:

`E1000 RX -> managed Ethernet II -> managed IPv4 -> managed UDP`

and for transmit:

`managed UDP -> managed IPv4 -> managed ARP/Ethernet II -> E1000 TX`.

There is no sockets API, port-thread abstraction, delegate callback, dynamic
dictionary, or public network configuration surface.  `ManagedE1000Driver`
continues to own PCI/MMIO/DMA, descriptor ownership, and the packet buffers;
`ManagedEthernetLayer` continues to own Ethernet framing and dispatch;
`ManagedIpv4Layer` owns the fixed UDP buffers, protocol state, endpoint table,
and the one existing pending IPv4 transmission slot.

### UDP wire subset

The deterministic topology remains the Phase 17 topology.  Phase 18 uses
local UDP port `15180` and peer UDP port `15181`, with the fixed endpoint table
registering only the local port and the `Phase18Echo` handler identity.  The
table has four bounded slots and supports register, lookup, unregister, full,
duplicate, and teardown-clear behavior without retaining executable targets.

`ManagedUdpProtocol` accepts the eight-byte UDP header, nonzero source and
destination ports, a declared length from 8 through 520 bytes, and at most a
512-byte payload.  The declared UDP length bounds the datagram view; trailing
IPv4 padding is not treated as UDP data.  Nonzero checksums validate the IPv4
pseudo-header (`source`, `destination`, protocol 17, UDP length) plus the
complete datagram.  A received checksum of zero is accepted as checksum
disabled.  A computed transmit checksum of numeric zero is encoded on the
wire as `0xFFFF`.

The managed proof payloads are `PHASE18-MANAGED-HELLO`,
`PHASE18-PEER-ACK`, `PHASE18-PEER-HELLO`, and `PHASE18-MANAGED-ACK`.
Managed-originated traffic proves the existing ARP/Ethernet/IPv4/E1000 TX
path; peer-originated traffic proves endpoint dispatch and managed response.
The Phase 18 path reuses the validated Phase 17 ARP cache rather than adding a
second resolution state machine.  Five live malformed controls cover zero
source port, zero destination port, invalid application payload, unknown
destination port, and an over-limit UDP length.  Short/long declared lengths,
bad checksums, odd payloads, pseudo-header mutation, zero-checksum receive, and
computed-zero-to-FFFF transmit behavior are covered by the independent host
suite.

The E1000 receive polling rearm now posts the descriptor immediately before
the current software cursor rather than unconditionally posting descriptor
15.  This preserves the current descriptor at the 16-entry ring boundary
when a packet arrives during a bounded polling interval.  The change keeps
the existing ownership model and does not enlarge the ring or expose a raw
buffer to managed protocol code.

### GC, teardown, and authoritative wire proof

Phase 18 runs the managed UDP exchange, peer response, zero-checksum receive,
five malformed controls, a post-malformed peer exchange, managed GC survival,
and a post-GC managed/peer exchange.  Teardown clears the endpoint table,
fixed UDP buffers, counters, pending state, and protocol acceptance before
the existing NIC quiesce, DMA release, PCI restore, MMIO unmap, claim release,
and accounting restoration.

The focused suite is `tools/Run-ManagedKernelPhase18HostTests.ps1` and passes
55 cases.  Phase 15, Phase 16, and Phase 17 focused controls pass 28, 57, and
48 cases respectively.  The authoritative three-boot runner is
`tools/Run-ManagedKernelPhase18FreshBoots.ps1`; its independent wire parser is
`tools/Parse-ManagedE1000Phase18Pcap.ps1`.  Each final PCAP contains 34
packets, four ARP frames, six ICMP frames, twelve valid UDP frames, and five
malformed UDP controls.  The final fresh-boot evidence is under
`evidence/phase18-authoritative-v9-20260824`; all three boots report
`PASS_PHASE18`, and all three PCAPs pass exact field, checksum, order, and
count validation.

The Phase 18 payload is `1,100,800` bytes with SHA-256
`BA5ECCD1933EC8DE6DD0D49A086A94BBB22EB4D6D1F7AE64779E2CF6EC37DFE9`.
The same payload passes three fresh Phase 17 regression boots under
`evidence/phase17-regression-phase18-20260824`, three fresh Phase 16
regression boots under `evidence/phase16-regression-phase18-v2-20260824`,
and three fresh Phase 15 regression boots under
`evidence/phase15-regression-phase18-v3-20260824`.
UDP receive/transmit is now proven over the managed E1000 path, but UDP
fragmentation/reassembly, TCP, DNS, routing, IPv6/ND, interrupts,
offloads, VLANs, jumbo/scatter-gather frames, multiple interfaces, sockets,
and Internet access remain deferred.

## Phase 19: bounded managed DHCPv4 bootstrap

Phase 19 adds a DHCPv4 client without creating a second packet path:

`E1000 RX/TX -> managed Ethernet II -> managed IPv4 -> managed UDP -> DHCPv4`

`ManagedDhcpv4Client` owns the fixed DHCP state and candidate/leased
configuration.  `ManagedIpv4Layer` owns the DHCP endpoint and uses the
existing bounded UDP endpoint table, UDP builder/parser, IPv4 builder/parser,
and Ethernet transmit path.  There are no sockets, timers, dictionaries,
option maps, unbounded queues, or retained RX spans.

### Bootstrap wire policy and state machine

Before binding, the only exceptional IPv4 receive case is UDP destined for
`255.255.255.255` while the DHCP client has no lease.  The DHCP endpoint still
requires server port `67`, client port `68`, a valid IPv4/UDP packet, the DHCP
cookie, and a matching transaction before state can change.  Normal Ethernet
unicast transmit continues to require a learned ARP destination; DHCP uses an
explicit Ethernet-broadcast primitive and never ARPs for the server during
DORA.

The client state is `Disabled -> Init -> Selecting -> Requesting -> Bound`.
The first deterministic transaction ID is `0x19000001`; subsequent attempts
advance a bounded monotonic counter.  There are at most three DISCOVER
attempts and three REQUEST attempts.  A valid OFFER copies only candidate
fields and enters `Requesting`; only a matching ACK atomically copies the
leased fields and enters `Bound`.  A matching NAK clears the candidate and
returns to `Init`.  Wrong xid, hardware type/length, runtime MAC, server,
ports, message type, cookie, or fixed-width/option data is ignored or
rejected without changing the active IPv4 identity.

The client emits BOOTP `REQUEST`, Ethernet hardware type `1`, hardware length
`6`, hops `0`, zero `ciaddr`, the broadcast flag, the runtime E1000 MAC, the
DHCP cookie `63 82 53 63`, and a bounded parameter request list for subnet
mask, router, DNS, and lease time.  The parser supports PAD, END, subnet mask,
router, one or two DNS addresses, requested IP, lease time, message type,
server identifier, and parameter request list.  Unknown options are skipped
only after their declared length is proven in bounds; conflicting duplicate
critical values are rejected, while identical duplicates are deterministic.

The deterministic peer is raw-frame based and uses server `10.15.0.2` with
MAC `02:15:00:00:00:02`.  It offers and ACKs `10.15.0.42` with mask
`255.255.255.0` and lease duration `3600` seconds.  Router and DNS are not
advertised in the authoritative ACK; the client can parse and retain bounded
values for future use.  DHCP renewal T1, rebinding T2, expiration processing,
persistent leases, DNS resolution, relays, APIPA, routing, and DHCPv6 remain
deferred.

After ACK, the leased address is installed into ARP/IPv4 ownership.  The
existing ARP proof then resolves the peer, and the Phase 17/18 ICMP and UDP
proofs run with source `10.15.0.42`; the independent PCAP check rejects any
post-bind guest frame using the old static `10.15.0.1` identity.  GC survival
and post-GC ICMP/UDP exchanges preserve the leased source.  DHCP teardown
clears the endpoint, transaction, candidate, lease, counters, stored option
state, IPv4 state, and ARP state before the established NIC teardown.  The
host suite also performs client re-init and verifies a fresh transaction
rejects a stale reply; the authoritative runtime repeats the complete fresh
boot three times.

### Host, wire, and fresh-boot evidence

`tools/Run-ManagedKernelPhase19HostTests.ps1` passes 39 bounded DHCP cases,
including parser/options, exact DISCOVER/REQUEST construction, candidate vs
ACK commit, stale and wrong-peer rejection, retry limits, NAK, teardown, and
re-init.  The prior focused suites remain green: Phase 15 `28/28`, Phase 16
`57/57`, Phase 17 `48/48`, and Phase 18 `55/55`.

The authoritative runner is `tools/Run-ManagedKernelPhase19FreshBoots.ps1`
with independent validation from `tools/Parse-ManagedE1000Phase19Pcap.ps1`.
All three boots pass with payload size `1,127,936` bytes and SHA-256
`CA0C47D1C7CB6979C8A49B2D532BF18F120122DDD2B7D8A71253AD131A8B0EF2` under
`evidence/phase19-dev7-20260824`.  Each PCAP contains 43 packets, including
the DHCPDISCOVER/OFFER/REQUEST/ACK sequence, five malformed DHCP controls,
four ARP frames, three leased-source ICMP frames, and six leased-source UDP
frames.  The malformed controls are bad cookie, wrong xid, wrong chaddr,
missing message type, and malformed option length; valid DORA and ordinary
traffic continue afterward.  The three boots report `PASS_PHASE19`, complete
GC and teardown checks, and leave no owned QEMU process.

Phase 19 is Outcome A for the bounded DHCPv4 scope.  Full lease renewal and
rebinding, lease persistence, DNS, TCP, sockets, IPv6/ND, relays, routing,
APIPA, multiple interfaces, offloads, VLANs, jumbo/scatter-gather frames,
interrupt-driven networking, and Internet access are intentionally deferred.

## Phase 20: bounded managed DNS resolver

Phase 20 extends the same path without adding a socket or DNS-specific packet
bypass:

`managed DNS -> managed UDP -> managed IPv4 -> managed ARP/Ethernet II -> E1000 TX/RX`

`ManagedDnsResolver` owns one fixed server address, one fixed encoded query
name, one resolved IPv4 result, one TTL, and one active transaction.  The
resolver uses UDP destination port `53` and deterministic client source port
`15200`.  It permits exactly one outstanding query; a second query is rejected
deterministically.  Transaction IDs start at `0x2001`, are nonzero, and advance
for every new query or retry.  There is no persistent DNS cache in this phase.

### DHCP Option 6 and supported DNS subset

The bounded DHCP parser stores one or two Option 6 addresses in fixed candidate
and leased slots.  Only an ACK-committed lease can install the DNS server into
the resolver.  The authoritative peer advertises `10.15.0.2` through DHCP
Option 6, while the resolver itself contains no authoritative server address.
The managed client receives `10.15.0.42/24`, installs the DHCP-provided DNS
server, and uses ordinary ARP when the DNS ARP cache is cold.

The supported DNS wire subset is one standard query with one A/IN question,
bounded QNAME encoding up to 253 characters and 255 encoded bytes, and a
maximum 512-byte message.  Responses require a matching transaction ID, QR,
standard opcode, one validated question, zero authority/additional records,
and at most eight answers.  The name decoder accepts ordinary labels and
compression pointers with a maximum of 16 pointer hops, validates pointer
targets, rejects self/cyclic pointers, and keeps consumed stream bytes
separate from followed pointer bytes.  Only a direct A/IN answer with
RDLENGTH 4 is resolved; the TTL is retained with the result and the fixed
authoritative TTL is 300 seconds.  Well-formed unsupported records are
skipped, but CNAME chains are not followed.  NXDOMAIN is a bounded negative
result, TC is rejected without TCP fallback, and malformed or unsupported
RCODE responses fail closed.  DNS-over-UDP checksum handling continues to use
the Phase 18 policy, including acceptance of an IPv4 zero checksum on receive.

### Resolved-address, negative, GC, and teardown proof

The authoritative query is `phase20.test A IN`, encoded as
`07 phase20 04 test 00 00 01 00 01`; the deterministic peer returns a direct
compressed-owner A answer (`C0 0C`) for `10.15.0.2` with TTL 300.  The managed
kernel then sends both ICMP Echo and the Phase 18 UDP exchange to the address
returned by the resolver.  It also queries `missing.phase20.test`, accepts
NXDOMAIN without retaining an address, and proves a later valid query still
works.  Five malformed wire controls are injected (wrong ID, truncated
response, pointer out of range, pointer loop, and invalid A RDLENGTH), plus a
wrong-source-port response.  None produces traffic from a bogus result.

After the first resolution and resolved-address exchanges, the existing
managed GC-survival mechanism runs.  DHCP state and the DNS server remain
authoritative, a fresh transaction resolves `phase20.test` again, and the
post-GC ICMP/UDP exchanges again use the returned A record.  Teardown clears
the DNS endpoint, active query, transaction/result state, DNS server, DHCP
state, UDP state, IPv4 state, ARP state, and the bounded pending transmission
before the established E1000 quiesce, DMA release, PCI restore, MMIO unmap,
and claim release sequence.

### Host, PCAP, and fresh-boot evidence

The focused suite is
`tools/Run-ManagedKernelPhase20HostTests.ps1`; it passes 123 cases covering
query/header/name/RR parsing, compression bounds and loops, NXDOMAIN/TC,
one-outstanding-query state, DHCP Option 6 commit timing, ports/checksums,
resolved-address data flow, GC, teardown, and reinitialization.  The dedicated
fresh-boot runner is `tools/Run-ManagedKernelPhase20FreshBoots.ps1`, and the
independent validator is `tools/Parse-ManagedE1000Phase20Pcap.ps1`.

The authoritative evidence is under
`evidence/phase20-final-20260824`.  Three independent QEMU processes
completed `PASS_PHASE20`; each PCAP independently validates 30 packets: two
E1000 proof frames, four DHCP messages including Option 6, two DNS ARP frames,
four DNS queries, nine valid-checksummed DNS responses including the malformed
controls and NXDOMAIN, two resolved-destination ICMP requests, and two
resolved-destination UDP requests.  The validator reports
`dns_queries=4 dns_responses=9 resolved_icmp=2 resolved_udp=2
resolved_ipv4=0A0F0002` for every boot.  The same payload passes three Phase 19
regression boots under
`evidence/phase19-regression-phase20-20260824`.  Phase 15 through Phase 19
host suites remain green at `28/28`, `57/57`, `48/48`, `55/55`, and `39/39`.

The Phase 20 payload is `1,147,392` bytes with SHA-256
`846CA6887E569FE113E766A473C88F5A51D340F94359DC626376AD5D3A352EEB`.
TCP DNS fallback, DNSSEC, EDNS, AAAA/IPv6, full CNAME recursion, SRV, TXT,
MX, PTR, mDNS, LLMNR, search domains, full cache infrastructure, retries
beyond the fixed three-attempt bound, sockets, routing, and multiple
interfaces remain deferred.

## Phase 21: bounded managed network service boundary

Phase 21 adds the first deliberate application-facing service boundary without
adding a protocol:

`managed application -> ManagedNetworkService -> DNS/ICMP/UDP -> IPv4/ARP/Ethernet -> E1000`

The existing ownership remains authoritative. `ManagedE1000Driver` owns PCI,
MMIO, DMA, descriptors, and hardware teardown. `ManagedEthernetLayer` owns
Ethernet dispatch and RX-frame lifetime. `ManagedIpv4Layer` continues to own
IPv4 policy, DHCP commit state, DNS resolver state, ICMP validation, UDP
construction/parsing, the single pending IPv4 transmit, and the fixed UDP
endpoint table. `ManagedNetworkServiceBackend` is the only adapter that knows
those implementation types. `ManagedPhase21TestConsumer` uses only
`ManagedNetworkService`; it does not reference `ManagedDnsResolver`,
`ManagedUdp`, `ManagedIpv4Layer`, `ManagedArpLayer`, `ManagedEthernetLayer`, or
`ManagedE1000Driver`.

### Service API and bounds

The consumer-facing API consists of value/result types and bounded operations:

* `NetworkStatus GetStatus()` returns a copied snapshot containing link/driver
  readiness, DHCP-bound/configured state, the six-byte MAC in a low-48-bit
  value, IPv4 address, subnet mask, and DHCP-provided DNS server.
* `BeginResolveIpv4` plus cooperative `Poll` exposes `Idle`, `Pending`,
  `Success`, `NxDomain`, and `Failed`. There is exactly one active DNS query;
  the underlying encoded hostname storage remains bounded at 253 characters.
* `BeginPingIpv4` plus `Poll` exposes one active bounded ping operation.
  ICMP headers, identifiers, and payload ownership remain internal.
* `BindUdpEndpoint`, `UnregisterUdpEndpoint`, and `SendUdp` expose the existing
  fixed endpoint model. The endpoint table remains capacity four, payloads are
  limited to 512 bytes, and one pending IPv4 transmit remains the only staged
  transmit slot. Resource contention returns explicit `Busy`, `NoResource`,
  or `Rejected` results rather than exceptions.
* `TryReceiveUdp` copies a validated datagram into the caller's buffer from one
  owned service receive slot. The slot has capacity one and 512 bytes; a second
  arrival is rejected as overflow and cannot silently overwrite the first.

`Ipv4Address` is a heap-free value type storing a network-order `uint` with
deterministic equality. The service never exposes Ethernet frames, ARP entries,
packet builders, checksums, DNS transaction IDs, descriptor state, DMA buffers,
or RX spans. No `Task`, `async`, socket, file-descriptor, callback, or general
async framework was added. The service boundary is operation-level, so packet
hot paths remain fixed-buffer and allocation-conscious.

### Consumer, GC, teardown, and reinitialization

The deterministic Phase 21 consumer reads DHCP status, resolves
`phase21.test`, feeds the returned `10.15.0.2` value into the service ping and
UDP calls, binds local port `15210`, and validates the peer response on port
`15211`. The exact payload is `PHASE21-API-HELLO`; the exact reply is
`PHASE21-API-ACK`. The consumer contains no `10.15.0.2` destination
substitution. The peer validates the actual packet on the dgram wire.

The service holds no native pointers or borrowed RX spans. An explicit GC
collection occurs after the first DNS/ICMP/UDP exchange; the same service then
performs a second DNS/ICMP/UDP exchange. `Teardown()` clears operation state,
bound service endpoints, and the receive slot and makes all subsequent service
operations unavailable. The established driver stop path then clears DHCP,
DNS, UDP, IPv4, ARP, Ethernet, and E1000 state and restores PCI/DMA/MMIO
accounting. Service, consumer, and receive storage are constructed before the
managed-kernel baseline collection; the runtime adapter rebinds the live
protocol objects and publishes a copied status snapshot at protocol start so
the post-GC path does not allocate or retain borrowed protocol state. A newly
constructed service begins a fresh generation; no prior result or registration
is reused.

### Phase 21 evidence

The focused host suite is
`tools/Run-ManagedKernelPhase21HostTests.ps1`; it passes 42 cases covering
status snapshots, DHCP/configuration gating, DNS success/NXDOMAIN/busy and
hostname validation, ICMP busy/not-configured/result propagation, endpoint
duplicates/capacity/unregister, UDP payload and pending-send bounds, copied
receive delivery, GC, teardown, and fresh-generation reset. The dedicated
fresh-boot runner is `tools/Run-ManagedKernelPhase21FreshBoots.ps1`; its
independent validator is `tools/Parse-ManagedE1000Phase21Pcap.ps1`.

Each authoritative Phase 21 PCAP validates DHCP DORA with Option 6, two
`phase21.test` DNS queries and A responses for `10.15.0.2`, two ICMP requests
and replies whose destination is the resolver result, and two exact UDP
application exchanges. All captured IPv4/UDP checksums and lengths are
validated, and the parser rejects the pre-DHCP static identity on the wire.
Three fresh QEMU processes are required. Phase 20 remains independently
available through its existing runner and parser; the Phase 15–20 host suites
remain regression gates. The final AOT payload is `1,182,720` bytes with
SHA-256
`9FF5C4428395CBC342E185735F6D23FCCD0A8785B4105EF1F819BBBE08436868`.
The authoritative evidence is under
`evidence/phase21-final-20260825/`; all three fresh boots and all three PCAP
parser runs report PASS. This is Outcome A for the bounded Phase 21 scope.
The final payload also passes three fresh Phase 20 regression boots recorded
under `evidence/phase20-regression-20260825/`. Host regression counts remain
Phase 15 `28`, Phase 16 `57`, Phase 17 `48`, Phase 18 `55`, and Phase 19 `39`,
with Phase 20 `123` and Phase 21 `42`.

TCP, sockets, System.Net.Sockets compatibility, multiple concurrent DNS
requests, general async APIs, blocking network APIs, select/poll, streaming,
TLS, HTTP, IPv6, routing, gateways, and multiple interfaces are explicitly
deferred to later phases.

## Phase 22: bounded managed TCPv4 client

Phase 22 adds one managed TCPv4 connection below the Phase 21 service boundary:

`managed application -> ManagedNetworkService -> ManagedTcpConnection -> managed IPv4/ARP/Ethernet -> E1000`

`ManagedTcpProtocol` owns strict TCP parsing, construction, options, and the
RFC-style IPv4 pseudo-header checksum.  `ManagedTcpConnection` owns the tuple,
wrap-aware sequence arithmetic, handshake and close state, one in-flight
application record, receive delivery, retry accounting, and RST/FIN policy.
`ManagedIpv4Layer` is the only TCP packet sender/parser integration point;
E1000 and Ethernet remain responsible only for their existing frame and
descriptor ownership.  The Phase 22 consumer uses only
`ManagedNetworkService`, just as the Phase 21 consumer does.

### TCP wire subset and fixed bounds

The client uses local port `15221`, peer port `15222`, and a deterministic
first client ISN of `0x22000001`.  Each connection generation advances the
client ISN by `0x100`.  The peer advertises MSS `512`; the client advertises
the same MSS on SYN and clamps application records to the negotiated bound.
TCP headers are limited to 20–60 bytes, options are bounded and length-checked
(MSS, NOP, EOL, and safely skippable well-formed options), payloads are capped
at 512 bytes, and reserved/unsupported control bits are rejected.  Every
segment is checked against the source/destination IPv4 pseudo-header checksum;
the host suite includes independent checksum vectors and source/destination
mutation tests.

The connection state is `Closed`, `SynSent`, `Established`, `FinWait1`,
`FinWait2`, `CloseWait`, `LastAck`, `TimeWait`, or `Failed`.  A valid SYNACK
must acknowledge the exact SYN sequence, and the third ACK consumes no
sequence space.  The active side permits one application segment in flight;
only the exact cumulative ACK releases it.  Duplicate payload is ACKed without
redelivery, future/out-of-order payload is not buffered, and the service owns
one 512-byte copied receive slot.  Matching RST fails the active connection;
stale RST and tuple mismatches are ignored.  FIN consumes one sequence number,
drives the bounded close handshake, and leaves the tuple unavailable in
TimeWait until explicit teardown.  GC survival relies on fixed managed arrays
and copied service data, with no borrowed RX span crossing the service boundary.

The connection exposes a deterministic `TryRetryPending` path with a maximum
of three retransmissions and no double sequence advancement.  A wall-clock
retransmission timer, congestion control, streaming, multiple connections,
listen/accept, urgent data, SACK, window scaling, offloads, sockets,
System.Net.Sockets compatibility, TLS, IPv6, routing, gateways, and multiple
interfaces remain deferred.

### Service API and deterministic consumer

`ManagedNetworkService` exposes `TcpConnectionCapacity = 1`,
`TcpReceiveSlotCapacity = 1`, `MaximumTcpPayloadLength = 512`, and
`TcpMaximumRetries = 3`.  `BeginTcpConnect` consumes the resolver result;
`SendTcp` reports `Busy` while the single in-flight record is outstanding;
`TryReceiveTcp` copies a validated peer payload; `CloseTcp` starts the bounded
FIN exchange; `Teardown` clears the connection and receive slot.  No packet
builder, checksum, tuple, sequence number, ARP entry, descriptor, or native
pointer is exposed by the public boundary.

The authoritative peer resolves `phase22.test` to `10.15.0.2`, completes the
managed SYN/SYNACK/ACK exchange, validates `PHASE22-MANAGED-HELLO`, returns
`PHASE22-PEER-ACK`, forces GC while the connection is established, validates
`PHASE22-POSTGC-HELLO`, returns `PHASE22-POSTGC-ACK`, and completes FIN/ACK
close and teardown.  Before the valid SYNACK it injects checksum-invalid,
truncated, invalid-offset, tuple-mismatch, wrong-ACK, and stale-RST controls;
the connection remains in `SynSent` until the exact valid peer response.

### Phase 22 evidence

The focused suite is `tools/Run-ManagedKernelPhase22HostTests.ps1`; it passes
56 cases covering parser/header/options/checksum bounds, wrap arithmetic,
handshake, exact ACK/data sequencing, one-in-flight policy, duplicate and
future data, GC, RST, FIN/TimeWait, bounded retries, service gating, copied
receive delivery, and teardown.  The dedicated fresh-boot runner is
`tools/Run-ManagedKernelPhase22FreshBoots.ps1`, with independent validation
from `tools/Parse-ManagedE1000Phase22Pcap.ps1`.

The authoritative three-boot evidence is under
`evidence/phase22-final4-20260825`.  All three fresh QEMU boots report
`PASS_PHASE22`; each independent PCAP parser reports 33 packets, four DHCP
messages, one DNS query/response, 19 valid TCP packets, all 15 expected TCP
flow transitions, and 14 malformed or rejected TCP controls.  The payload is
`1,218,048` bytes with SHA-256
`E456E1A514E5DAF281EE1D31BBC45728F572D51FDAFC6F0634DADD90FA58B4D9`.
Phase 21 remains available as a regression path on the same payload; the
three current-payload regression boots and independent PCAP checks are under
`evidence/phase21-regression-phase22-final-20260825`.  The Phase 15–21 host
suites remain required gates.

## Phase 23: bounded managed HTTP/1.1 client

Phase 23 adds the first managed application protocol above the bounded Phase
22 TCPv4 service:

`managed application -> ManagedHttpClient -> ManagedNetworkService -> bounded TCPv4 -> IPv4/ARP/Ethernet -> E1000`

`ManagedHttpClient` owns one HTTP operation.  The application supplies a
hostname and origin-form path and observes only HTTP request state, DNS/TCP
progress, status, body length/data, failure state, cancel, reset, and
completion.  It does not reference Ethernet, ARP, IPv4, TCP packets,
sequence numbers, checksums, PCI, descriptor rings, or E1000 state.
`ManagedHttpRequestBuilder` and `ManagedHttpResponseParser` are fixed-storage
helpers behind that API; no general HTTP or socket stack was added.

### Supported subset and exact bounds

The client supports one client-side HTTP/1.1 `GET` per generation, hostname
resolution through managed DNS, an origin-form target, `Host`, `Connection:
close`, deterministic request serialization, a numeric 100–599 status code,
bounded response headers, `Content-Length`, optional retained `Content-Type`,
response body delivery, peer connection close, and deterministic teardown.
TLS/HTTPS, redirects, cookies, authentication, proxies, compression, chunking,
keep-alive pooling, concurrent requests, server/listener HTTP, IPv6, and a
general URL parser are not implemented.

Fixed capacities are: hostname 253 bytes with labels no longer than 63;
origin-form path 128; serialized request 512; status line 64; header line 96;
header count 16; aggregate response-header bytes 512; retained header name
32; retained `Content-Type` 64; response body 256; and receive/parser staging
512 bytes.  Phase 22 bounds remain one TCP connection, one copied receive
slot, one application record in flight, a 512-byte TCP payload cap, and three
bounded retries.

### Request grammar and rejection policy

The request is serialized exactly as:

`GET <origin-form-path> HTTP/1.1\r\nHost: <hostname>\r\nConnection: close\r\n\r\n`

Hostnames use printable DNS label characters with labels separated by dots;
the path must begin with `/` and contain printable ASCII without controls or
spaces.  The response parser accepts only `HTTP/1.1`, a three-digit status in
100–599, CRLF-terminated status/header lines, header names made from ASCII
letters, digits, and `-`, and a blank CRLF line ending the header block.  Names
are case-insensitive.  `Content-Length` must be decimal, fit the 256-byte
body capacity, and agree across duplicates; matching duplicates are accepted
and conflicting duplicates fail.  `Connection` must contain the exact value
`close`.  `Content-Length` and `Connection: close` are required before body
delivery.  Unknown headers are ignored after all line/count/aggregate limits
are checked.  `Transfer-Encoding`, including `chunked`, is explicitly
rejected; connection close is never treated as unlimited body framing.

Malformed status/version/code, line overflow, bare-LF framing, header syntax,
header-count or aggregate overflow, malformed or conflicting content length,
unsupported transfer encoding, missing required framing, body overflow,
excess bytes beyond the declared length, and premature close fail closed.
The parser is incremental across TCP deliveries and uses no recursion,
dynamic dictionary, unbounded accumulation, or exception-based protocol
control flow.

### DNS, TCP, peer, and segmentation proof

The Phase 23 application resolves `phase23.test` through managed DNS and
consumes the returned `10.15.0.2` address.  The deterministic raw peer then
performs DHCP/DNS as required, completes the existing TCP handshake on ports
15221/15222, validates the exact 64-byte request

`GET /phase23 HTTP/1.1\r\nHost: phase23.test\r\nConnection: close\r\n\r\n`

and returns the authoritative 17-byte body `phase23-http-pass` with
`Content-Length: 17`.  The response is emitted as three TCP payloads:
`HTTP/1.1 200`, a second payload containing the remainder of the status line,
headers, header terminator, and `phase23-`, and a final `http-pass` payload.
This crosses status, header/body framing, and body boundaries.  The peer
validates ACK progression for all three segments, sends FIN, receives the
managed FIN/ACK sequence, and validates the final ACK.  The TCP client ISN
remains Phase 22's deterministic `0x22000001`; HTTP reuses that transport
contract.

### Application proof, GC, and teardown

`ManagedPhase23TestConsumer` uses only `ManagedNetworkService` and
`ManagedHttpClient`.  Its markers prove network readiness, request start,
DNS success and resolved address, TCP connection, request send, status 200,
GC survival while parsing, body completion, exact body verification, and
HTTP/TCP teardown.  A collection is forced after status parsing; parser
state, response bytes, service ownership, and final completion remain valid.
Completion, failure, cancellation, and reset clear or invalidate HTTP parser,
request, receive, and response state; service teardown releases TCP/DNS
resources and a fresh generation can be created.

### Phase 23 evidence

The focused suite is `tools/Run-ManagedKernelPhase23HostTests.ps1`; it passes
60 cases covering canonical and boundary request construction, segmented and
malformed status/header parsing, line/count/aggregate limits, duplicate and
conflicting content lengths, unsupported chunking, body fragmentation and
capacity, DNS/TCP/HTTP lifecycle failures, cancellation, reset/reuse, GC,
and teardown.  Phase 15–23 host regression totals are respectively `28`,
`57`, `48`, `55`, `39`, `123`, `42`, `56`, and `60`.

The authoritative wrapper is `tools/Run-ManagedKernelPhase23FreshBoots.ps1`.
It returned exit code 0 for three independent fresh QEMU processes under
`evidence/phase23-final7-20260825/`; each boot reported the full
network-ready -> DNS -> TCP -> HTTP status/body -> teardown marker sequence,
exactly one Phase 23 E1000 proof injection, no guest fault markers, and no
owned QEMU process left running.  The independent parser is
`tools/Parse-ManagedE1000Phase23Pcap.ps1`.  Each of its three PCAP runs
reports:

`packets=25 dhcp=4 dns_queries=1 dns_responses=1 tcp_valid=15 tcp_flow=15 tcp_malformed=0 response_segments=3 request_bytes=64 response_body=phase23-http-pass`

This independently proves DHCP/DNS, SYN/SYN-ACK/ACK, request bytes, all three
response segments, cumulative ACKs, FIN/close, exact sequence progression,
and no retransmission/control storm.  Host controls cover invalid
status/version, line overflow, malformed/conflicting content length,
oversized body, premature close, and unsupported chunking; none produces
false success or blocks reset/reuse.

The final NativeAOT payload is `1,237,504` bytes with SHA-256
`D936958D695D970C63920885FECB6CEFBAF7C4AAB78EFE495DF93FB46E16CA35`.
This is Outcome A for bounded HTTP/1.1 over managed TCP.  HTTPS is not
implied: TLS, certificate handling, HTTP/2, HTTP/3, QUIC, and general HTTP
features remain later boundaries.

## Phase 24: managed TLS transport foundation — Outcome C

Phase 24 begins with the required cryptographic capability audit. The audit is
retained in [MANAGED_TLS_PHASE24_CRYPTO_AUDIT.md](MANAGED_TLS_PHASE24_CRYPTO_AUDIT.md)
and can be reproduced with
`tools/Invoke-ManagedTlsPhase24CryptoAudit.ps1`. It intentionally stops before
adding a TLS-shaped protocol: the current NativeAOT bare-metal boundary has no
cryptographically credible client entropy source and no proven asymmetric
primitive for server authentication and key exchange.

### Audit result and exact boundary

The repository contains no owned SHA-256, HMAC-SHA256, AES/AES-GCM/AES-CBC,
RSA, ECDSA, ECDH/P-256, constant-time comparison, or big-integer
implementation. The host runtime exposes type names for these APIs, but host
availability is not evidence that the implementation is self-contained in the
freestanding NativeAOT payload. It may depend on Windows CNG, OpenSSL, a native
PAL, OS certificate services, or other unavailable runtime facilities, so no
host cryptographic call was admitted into the kernel.

The current payload imports `bcrypt.dll!BCryptGenRandom`, but the existing
dependency census classifies that PAL entry as fail-fast/unimplemented. No
successful bare-metal trace reaches it. The proven UEFI time path is used by
startup security-cookie initialization only; it is not a CSPRNG and cannot
provide TLS ClientHello randomness, ephemeral private material, or nonce
material. This is the first exact blocker. The independent second blocker is
the absence of a bare-metal-proven RSA/ECDSA/ECDH implementation for
authenticating a pinned deterministic peer.

### Intended protocol and architecture

TLS 1.2 is the intended next protocol because a bounded handshake is a better
fit for the current Phase 23 TCP contract than prematurely adding TLS 1.3. No
cipher suite was selected in Outcome C. `TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256`
(`0xC02F`) remains an audit candidate only; it is not implemented or claimed
as supported. The intended future stack is:

```text
managed application -> ManagedTlsClient -> ManagedNetworkService
                    -> TCPv4 -> IPv4 / ARP / Ethernet -> E1000
```

No `ManagedTlsClient`, TLS record parser, handshake state machine, key
schedule, TLS API, deterministic TLS peer, TLS PCAP parser, Phase 24 fresh-
boot runner, or encrypted application-data exchange was added. Therefore no
TLS capacities, randomness source, traffic-key ownership, secret-clearing
evidence, TLS host-test total, Phase 24 fresh-boot result, or Phase 24 PCAP
result is claimed. Existing Phase 23 DNS/TCP/HTTP behavior remains unchanged.

### Outcome C and next logical boundary

This is Outcome C rather than a partial TLS success. Adding fixed TLS
capacities or plaintext/TLS-shaped records before the entropy and asymmetric
boundaries are proven would create false security evidence. The next logical
boundary is a genuine firmware/CPU entropy contract, followed by a small
managed or fully proven NativeAOT cryptographic substrate with independent
known-answer vectors. Only after those prerequisites pass should one TLS 1.2
cipher suite, pinned-peer authentication, bounded record protection, and the
DNS -> TCP -> TLS acceptance path be implemented.

## Phase 25: Managed Cryptographic Foundation I — Outcome C

Phase 25 adds the first independently testable cryptographic substrate without
adding TLS records, a handshake, AES-GCM, RSA, ECDH, ECDSA, X.509, or HTTPS.
The owned managed implementation is `ManagedSha256`: incremental SHA-256 with
fixed 64-byte block storage, an eight-word state, a 64-word schedule, correct
FIPS padding, and 64-bit big-endian message-length encoding. It accepts
arbitrarily segmented input, including zero length, and returns exactly 32
digest bytes without unbounded allocation, reflection, dynamic code, or
`System.Security.Cryptography`.

`ManagedHmacSha256` composes that primitive with the RFC HMAC construction: a
64-byte block, 32-byte digest, short/exact/long key handling, long-key
pre-hashing, incremental message updates, reset/reuse, and explicit clearing
of pads and temporary key blocks. `ManagedCryptoComparison.FixedTimeEquals`
handles equal and unequal lengths with an accumulated difference and no
early mismatch return; the implementation makes no claim beyond that
observable bounded structure.

The independent vectors are FIPS 180-4 SHA-256 vectors and RFC 4231 HMAC
vectors. SHA tests cover empty input, `abc`, a standard multi-block message,
55/56/63/64/65-byte boundaries, segmented and byte-at-a-time updates,
reset/reuse, finalized-state rejection, short-output rejection, and GC
survival. HMAC tests cover short, repeated-byte, exact/long, empty, segmented,
reset/reuse, corrupted-MAC mismatch, and GC lifecycle behavior. The dedicated
host suite is `tools/Run-ManagedKernelPhase25HostTests.ps1` and passes exactly
113 cases, including explicit deterministic test entropy, unavailable and
partial/failure providers, the 1,024-byte maximum, max-plus-one rejection,
and production-provider separation.

### Entropy boundary and policy

Production `ManagedSecureRandom` exposes `IsAvailable` and bounded
`TryFill(Span<byte>)` through the existing managed/native ABI conventions. It
does not use time, TSC, MAC addresses, PCI enumeration, stack addresses, ASLR,
fixed seeds, or QEMU timing. The native x64 service performs CPUID leaf 1
ECX bit 30 detection for RDRAND and leaf 7 EBX bit 18 detection for RDSEED.
Each 64-bit word prefers RDSEED and falls back to RDRAND when available; the
carry flag is checked, retries are bounded at 10, and exhaustion returns an
explicit failure after clearing the destination. Maximum fill size is 1,024
bytes. No DRBG was introduced because the target currently has no proven
hardware seed source, and no UEFI RNG or virtio-rng protocol is assumed after
the boot boundary.

The host deterministic provider is injected only by the Phase 25 host-test
project. Production construction points to `NativeHardwareEntropy` and has no
fallback to the deterministic provider. An unavailable provider is surfaced
to consumers and the kernel proof records a fail-closed marker without
emitting random bytes or secret state.

### NativeAOT and fresh-boot proof

The native ABI is `GX_MANAGED_KERNEL_ENTROPY_SERVICES_V1`; installation checks
size, ABI/version, architecture, capabilities, function address, capacity,
retry policy, and reserved fields. The authoritative runner is
`tools/Run-ManagedKernelPhase25FreshBoots.ps1`. It uses the real NativeAOT
payload and existing QEMU/OVMF conventions, but halts after the narrow Phase
25 proof so unrelated Phase 11 keyboard/interrupt timing cannot obscure the
crypto result. Three fresh boots passed all managed SHA-256, HMAC, constant-
time, GC-survival, reset, and teardown markers. The QEMU command line uses
q35, single-threaded TCG, 128 MiB, and no explicit `-cpu` override.

All three boots reported maximum basic CPUID leaf `0xD`, leaf-1 ECX
`0x80002001`, leaf-7 EBX `0x0`, and entropy feature flags `0x0`: neither
RDRAND nor RDSEED is exposed by the authoritative environment. Consequently
the production random provider was unavailable and proved fail-closed on all
three boots. This is Outcome C because the platform lacks a credible runtime
entropy source; uniqueness of logs or any statistical property is not treated
as a security proof.

The Phase 25 managed payload is 1,253,888 bytes with SHA-256
`98D945E9508FF83ADC9C536D68CE59072F113435210DFF664DE539B260061735`.
The import audit compares normalized import names against the Phase 23
payload and finds no new OS crypto import. `bcrypt.dll!BCryptGenRandom`
remains the existing NativeAOT runtime/PAL import identified in
`docs/DEPENDENCY_CENSUS.md`; Phase 25 source does not reference it, the
service does not route through it, and none of the three boots reached the
loader fail-fast unexpected-import marker. Audit output and boot logs are
under `evidence/phase25-crypto-foundation-20260825-final4/`.

### Remaining TLS prerequisite matrix after Phase 25

| TLS prerequisite | Status after Phase 25 |
| --- | --- |
| Secure entropy | Blocked: target CPUID exposes neither RDRAND nor RDSEED; service fails closed |
| SHA-256 | Proven: owned managed implementation, standardized vectors, three bare-metal boots |
| HMAC-SHA256 | Proven: owned managed implementation, RFC 4231 vectors, three bare-metal boots |
| Constant-time equality | Proven for bounded byte comparison; no broader timing claim |
| TLS 1.2 PRF building blocks | Available as primitives; integration deferred |
| AES-128 | Missing |
| GCM | Missing |
| ECDH P-256 | Missing |
| RSA/ECDSA verification | Missing |
| X.509 narrow parser | Missing |
| TLS state machine and records | Deferred |

## Phase 26 entropy follow-up

Phase 26 subsequently added the hardware-first entropy router and modern
non-transitional virtio-rng driver. Its host suite passed 70 cases, and its
three fresh QEMU boots proved PCI discovery, queue ownership, provider
selection, GC survival, teardown, and reinitialization. The Phase 25
no-provider regression remains fail-closed with the expected
`ENTROPY_UNAVAILABLE` result.

## Phase 27: managed AES-128 and AES-GCM — Outcome B

Phase 27 adds the narrow managed symmetric foundation in
`ManagedAes128.cs`, `ManagedGhash.cs`, and `ManagedAesGcm.cs`. It is not a TLS
implementation. AES-128 follows FIPS-197 encryption semantics with a
primitive-only 176-byte expanded-key layout and a computed algebraic S-box;
GHASH uses fixed-state MSB-first GF(2^128) multiplication, zero-padded partial
blocks, and the SP 800-38D length block. GCM accepts exactly 16-byte keys,
12-byte nonces, and 16-byte tags, with a 256-byte AAD cap and 16 KiB
plaintext/ciphertext cap. GCM counter exhaustion is rejected before wrap.

The one-shot API uses caller-owned bounded buffers and rejects overlaps. It
authenticates ciphertext/AAD before decrypting or writing plaintext. On tag,
ciphertext, AAD, nonce, or key failure the plaintext output remains untouched;
temporary keys, GHASH state, counters, tags, and authentication intermediates
are cleared. Nonce uniqueness remains the caller's responsibility: reusing a
GCM nonce with the same key is catastrophic and prohibited.

The Phase 27 host suite passes exactly 100 cases using FIPS/NIST vectors,
incremental GHASH tests, corruption controls, exact capacities, recovery after
failed authentication, GC/lifecycle tests, and an independent host-only
`AesGcm` oracle. Three fresh NativeAOT QEMU boots execute the AES, GHASH, GCM,
fail-closed, no-plaintext, reset/reuse, and Phase 26 virtio-rng nonce proofs.
The direct Phase 27 crypto-state `GC.Collect()` checkpoint is a characterized
NativeAOT runtime limitation: the existing Phase 26 GC proof succeeds, while
the new checkpoint exits after its begin marker. The strict Phase 27 runner
therefore reports Outcome B rather than overstating Outcome A.

The final Phase 27 payload is 1,314,816 bytes with SHA-256
`124B02BF07966654AC08D578F6BC07EB252EAD8BE28846D0EB1D1153F55C26A2`. Its
imports retain only the existing `bcrypt.dll!BCryptGenRandom` runtime/PAL
surface among crypto-related imports; no BCrypt AES APIs, OpenSSL, libcrypto,
CommonCrypto, or hosted crypto PAL became reachable. Complete evidence is in
`evidence/managed-kernel-phase27-authoritative-final24/` and the detailed
design is in `docs/MANAGED_KERNEL_PHASE27_AES_GCM.md`.

### TLS prerequisite matrix after Phase 27

| TLS prerequisite | Status |
| --- | --- |
| Secure entropy | Proven |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| TLS 1.2 PRF building blocks | Available |
| AES-128 | Proven functional; Outcome B crypto-GC caveat |
| GCM | Proven functional; Outcome B crypto-GC caveat |
| ECDH P-256 | Missing |
| RSA/ECDSA verification | Missing |
| X.509 parser | Missing |
| TLS state machine | Deferred |

## Phase 28: managed P-256 ECDH — Outcome A

Phase 28 adds a narrow managed NIST P-256/secp256r1 ECDH primitive in
`ManagedP256.cs`. It uses fixed-width eight-`uint` field elements, Jacobian
point arithmetic, SEC1 uncompressed public keys, fixed 256-step scalar
multiplication, and rejection-sampled private keys from the Phase 26
virtio-rng-backed `ManagedSecureRandom`. It does not add certificates, ECDSA,
TLS records, or a TLS handshake.

The dedicated host suite passes 188/188 cases, including independent field and
point checks, RFC 5903 and NIST CAVP ECC CDH vectors, malformed scalar/point
rejection, output preservation on failure, supported overlap, entropy
integration, teardown/reuse, GC survival, and Phase 26/27 regressions. The
three authoritative fresh QEMU boots in
`evidence/managed-kernel-phase28-authoritative-final7/` each reached
`MANAGED_KERNEL_PHASE28_PASS` and retained the Phase 26 entropy, Phase 27
AES/GHASH/GCM, and Phase 23 regression markers.

The final Phase 28 payload is 1,341,952 bytes with SHA-256
`DC431B422D1D8B53690A30882F24CA215A85A5FAC7D558C54AEA0984BA248211`.
The combined Phase 15–27 host regression total remains 791/791 (the retained
Phase 15–26 result is 691/691 and Phase 27 is 100/100). The import audit finds
only the pre-existing `bcrypt.dll!BCryptGenRandom` runtime/PAL boundary among
crypto-related imports; Phase 28 adds no BCrypt ECC/secret-agreement,
NCrypt, OpenSSL, libcrypto, CommonCrypto, or hosted ECDH dependency.

The inherited Phase 27 direct crypto-state `GC.Collect()` NativeAOT boundary
remains documented and is not a Phase 28 acceptance failure. The Phase 28
proof does not claim formal constant-time behavior, and its ECDH result is the
raw 32-byte affine X coordinate. Detailed design, limitations, vectors, and
boot evidence are in
`docs/MANAGED_KERNEL_PHASE28_P256_ECDH.md` and the evidence directory above.

### TLS prerequisite matrix after Phase 28

| TLS prerequisite | Status |
| --- | --- |
| Secure entropy | Proven |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| AES-128 | Proven |
| GHASH | Proven |
| AES-GCM | Proven |
| TLS PRF building blocks | Available |
| P-256 ECDH | Proven |
| RSA verification | Missing |
| ECDSA verification | Missing |
| X.509 | Missing |
| TLS handshake/state machine | Deferred |

## Phase 29: managed P-256 ECDSA verification — Outcome A

Phase 29 adds digest-oriented managed ECDSA verification for NIST P-256. The
implementation separates field modulus `p` from subgroup order `n`, uses
bounded eight-limb scalar arithmetic and fixed 512-bit reduction, inverts by
the fixed exponent `n-2`, and reuses the Phase 28 strict SEC1 public-key and
Jacobian point paths. It also provides a narrow canonical DER parser for
`SEQUENCE { INTEGER r, INTEGER s }` with a 72-byte maximum. Signing, X.509,
certificate chains, RSA, and TLS handshake logic remain deferred.

The Phase 29 host suite passes 209/209, with Phase 15–26 691/691, Phase 27
100/100, and Phase 28 188/188 retained. Three fresh authoritative NativeAOT
boots reached `MANAGED_KERNEL_PHASE29_PASS`; exact payload, serial, firmware,
and import evidence is retained in
`evidence/managed-kernel-phase29-authoritative-final5/`.

### TLS prerequisite matrix after Phase 29

| TLS prerequisite | Status |
| --- | --- |
| Secure entropy | Proven |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| AES-128 | Proven |
| GHASH | Proven |
| AES-GCM | Proven |
| TLS PRF building blocks | Available |
| P-256 ECDH | Proven |
| P-256 ECDSA verification | Proven |
| RSA verification | Missing |
| X.509 certificate validation | Missing |
| TLS handshake/state machine | Deferred |

ECDSA verification alone does not claim certificate authentication.

## Phase 30: narrow X.509 / DER certificate validation — Outcome A

Phase 30 adds a bounded managed DER/X.509 parser and validator for a narrow
P-256/ECDSA-SHA256 certificate profile. It supports exact-TBS signature
verification, SPKI extraction, validity, basic constraints, key usage, EKU,
SAN/CN hostname rules, bounded chains, exact configured-root trust, path
length, and critical-extension rejection. It deliberately omits RSA, general
X.509 policy processing, revocation, OS trust stores, and the TLS handshake.

The Phase 30 host suite passes 91/91. Retained suites pass Phase 15–29
1,188/1,188. Three fresh authoritative NativeAOT boots pass
`MANAGED_KERNEL_PHASE30_PASS`; serial and firmware evidence is in
`artifacts/managed-kernel-phase30-qemu-20260828-authoritative-final/`, while
payload and import evidence is in `artifacts/managed-kernel-phase30-final/`
and `artifacts/managed-kernel-phase30-gate/`. The design record is
`docs/MANAGED_KERNEL_PHASE30_X509_VALIDATION.md`.

### TLS prerequisite matrix after Phase 30

| TLS prerequisite | Status |
| --- | --- |
| Secure entropy | Proven |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| AES-128 | Proven |
| GHASH | Proven |
| AES-GCM | Proven |
| TLS PRF building blocks | Available |
| P-256 ECDH | Proven |
| P-256 ECDSA verification | Proven |
| Bounded DER reader | Proven |
| Narrow X.509 certificate validation | Proven |
| ECDSA P-256 certificate signature validation | Proven |
| Bounded certificate-chain validation | Proven |
| TLS server hostname validation | Proven |
| RSA verification | Missing |
| General PKI/path building | Unsupported |
| Revocation/OCSP/CRL | Unsupported |
| TLS handshake/state machine | Deferred |

## Phase 31: narrow TLS 1.2 ECDHE-ECDSA handshake — Outcome A

Phase 31 closes the first transport-independent managed TLS state machine for
TLS 1.2 / `0x0303`, suite `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256`
(`0xC02B`), P-256, SHA-256/ECDSA, AES-128-GCM, and mandatory RFC 7627 EMS.
It consumes arbitrary caller-buffer fragments, validates the ServerHello,
Certificate, and signed ServerKeyExchange through the Phase 30/29/28 paths,
derives the EMS master and traffic keys, authenticates both Finished messages,
reaches Established, and proves small protected PING/PONG data.

The Phase 31 host suite passes 33 cases. Retained Phase 15–29 regressions pass
1,188/1,188, Phase 30 passes 91/91, and three fresh authoritative NativeAOT
boots pass `MANAGED_KERNEL_PHASE31_PASS`. The final payload and boot evidence
are recorded under `artifacts/managed-kernel-phase31-final/`,
`artifacts/gate4-phase31-final/`, and
`artifacts/managed-kernel-phase31-boots-authoritative4/`. Full design and
limitations are in [MANAGED_KERNEL_PHASE31_TLS12_HANDSHAKE.md](MANAGED_KERNEL_PHASE31_TLS12_HANDSHAKE.md).

### TLS capability matrix after Phase 31

| Capability | Status |
| --- | --- |
| Secure entropy / SHA-256 / HMAC-SHA256 | Proven |
| AES-128 / GHASH / AES-GCM | Proven |
| P-256 ECDH / P-256 ECDSA verification | Proven |
| Bounded DER / narrow X.509 / chain / hostname | Proven; Phase 30 reused |
| TLS 1.2 PRF / mandatory EMS | Proven |
| TLS 1.2 AES-GCM records / encrypted Finished | Proven |
| ECDHE-ECDSA handshake state machine | Proven for `0xC02B`/P-256 |
| Established transport-independent TLS session | Proven |
| TCP/TLS, DNS, remote HTTPS, HTTP | Deferred to Phase 32 |
| TLS 1.3, RSA, general PKI/revocation | Unsupported |

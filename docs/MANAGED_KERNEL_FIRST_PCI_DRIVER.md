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

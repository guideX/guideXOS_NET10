# Phase 84 — Managed Ethernet Receive Integration and EtherType Dispatch

**Date:** 2026-10-03
**Prompt phase:** 84
**Outcome:** B — hardware receive was integrated into the normal managed Ethernet layer, and the skipped Ethernet MAC initialization was repaired.

## Result

Three final fresh QEMU boots each injected four local 60-byte Ethernet frames through QEMU `e1000e`. The real E1000 RXT0 MSI event reached `ManagedE1000Driver`, which copied the completed RX buffer into the fixed receive buffer owned by `ManagedEthernetLayer`. The driver then called the same synchronous parser and EtherType demultiplexer used by the layer's ordinary receive path. The Phase 84 `0x88B5` test consumer accepted each payload exactly once, after which the driver recycled its descriptor.

Every final boot recorded four completed descriptors, four Ethernet receives, four successfully parsed frames, and four Phase 84 consumer deliveries. Each had zero malformed drops, unsupported-type drops, or consumer failures. The frame sentinels arrived in order `1, 2, 3, 4`; destination, source, EtherType, and payload length matched on all four frames. All three boots continued to the Phase 12 progress marker.

Phase 83's acceptance path previously compared frames directly inside `ManagedE1000Driver` with `TryValidatePhase83RxTestFrame`. It did not call `ManagedEthernetLayer`. The normal Ethernet layer already had a polling receive API, but the E1000 typed RX event had no route to it. In addition, Phase 83 skipped the Phase 15 polling proof that had been initializing the Ethernet layer's local MAC. A fresh Phase 83 boot therefore left that layer MAC unset. The narrow repair initializes the layer MAC immediately after the driver reads the NIC MAC; the event handler now enters the normal Ethernet parser and demultiplexer.

The `0x88B5` consumer remains acceptance-only. Ethernet II parsing and the ARP/IPv4/unsupported EtherType dispatch boundary are normal managed Ethernet code. This proof does not claim that `0x88B5` is an Internet protocol.

## Receive boundary and ownership

The event path is:

```text
E1000 RXT0 typed event
  → ManagedE1000Driver.TryHandleInterrupt
  → read the expected descriptor; require DD and EOP, zero error flags, and a bounded length
  → copy DMA bytes once into ManagedEthernetLayer's fixed 2048-byte receive buffer
  → ManagedEthernetLayer.ProcessDriverReceivedFrame
  → shared Ethernet II parse and EtherType demultiplexer
  → ARP, IPv4, acceptance-only 0x88B5 consumer, or unsupported-type result
  → return synchronously to the driver
  → reset the descriptor and advance RDT
```

The receive buffer is preallocated by `ManagedEthernetLayer`; frames are not allocated as unbounded per-packet objects. E1000 DMA bytes are copied directly into that fixed managed buffer. The Ethernet header, payload span, and parser view are consumed synchronously and are not stored by a consumer. The test consumer records scalar metadata (six-byte MAC values in host numeric form, EtherType, payload length, and sentinel), not a span into the receive buffer. The descriptor remains software-owned during event-driven layer processing and is returned to hardware only after dispatch returns. Unsupported EtherTypes, malformed frames, and consumer rejection all take the descriptor recycle path. A typed RX event with no DD completion does not invoke the Ethernet layer or fabricate a frame.

The older polling API also copies DMA into caller-owned managed storage before recycling its descriptor; its subsequent parser work is against that managed copy. Both entry points use the same parser and EtherType demultiplexer.

## Ethernet policy and results

| Item | Phase 84 behavior |
|---|---|
| Header | Ethernet II, 14 bytes minimum: destination 6, source 6, EtherType 2. |
| Receive length | `14..2048` bytes at the Ethernet parser. E1000 descriptor length is checked against the owned 2048-byte buffer before upper-layer access. |
| Destination | Accept the configured guest MAC or broadcast. Reject other destinations. No promiscuous behavior is enabled. The existing E1000 `RCTL=0x04008002` accepts broadcast frames; QEMU acceptance targeted the guest MAC. |
| Source | Extract the six-byte source MAC. Existing basic source policy rejects invalid and group addresses; the test consumer records it for downstream use. |
| EtherType | Read as network byte order into a host-representation `ushort`; e.g. bytes `88 B5` become `0x88B5`. |
| Dispatch | Bounded classification to ARP, IPv4, acceptance-only Phase 84 consumer, or unsupported. Unknown EtherType is a valid Ethernet frame and is returned as `UnsupportedEtherType`, not malformed Ethernet. |
| `0x88B5` consumer | Opt-in acceptance fixture only. It checks the `guideXOS Phase83 RX` signature, a big-endian sequence sentinel, and zero padding, then records bounded frame metadata. |
| `<14` bytes | Malformed/truncated; no EtherType access or dispatch. |
| Exactly 14 bytes | Header parsing succeeds with an empty payload. The Phase 84 sentinel consumer rejects it for lacking its payload; Ethernet parsing itself remains valid. |
| Over 2048 bytes | Rejected before payload access. |
| Descriptor errors | Any nonzero RX descriptor error byte is rejected before Ethernet dispatch. The descriptor is recycled. |
| Completion | DD and EOP are both required before dispatch. A DD descriptor without EOP is dropped and recycled. Multi-descriptor/scatter-gather receive is unsupported. |
| Driver to layer result | `Consumed`, `UnsupportedEtherType`, `MalformedFrame`, `ConsumerRejected`, `DispatchFailed`, or `DriverError`. A missing DD is handled before layer invocation. |
| Consumer failure | Bounded rejection and counter increment; the driver still recycles the completed descriptor. No exception is used as a packet result. |

The NIC is configured to accept broadcast frames, and the layer accepts broadcast or the guest unicast MAC. No wrong-destination frame was sent through QEMU; host tests cover the layer's rejection policy. Existing Phase 83 descriptor error handling remains authoritative: DD+EOP is required, and any set error byte bit rejects the frame. Other descriptor status flags are not separately filtered.

## QEMU evidence

The Phase 83 local `dgram` injector and RXT0 fixture were retained. The fresh-boot runner now also asserts the Phase 84 Ethernet-layer markers and metadata on every boot. QEMU used `e1000e`, PCI `8086:10D3`, MSI vector `0x51`, and the configured guest MAC `52:54:00:12:34:56`. The four injected frames had source `02:15:00:00:00:01`, destination `52:54:00:12:34:56`, EtherType `0x88B5`, and 46 bytes of payload. E1000 descriptor status was DD+EOP (`0x03`), the error byte was zero, and the 16-entry ring advanced through descriptor indices 0–3 with RDT ending at 3.

| Boot | RX descriptors / parsed frames / consumer deliveries | Malformed / unsupported / consumer failures | Serial SHA-256 | Injection SHA-256 | Timeline SHA-256 |
|---:|---:|---:|---|---|---|
| 1 | 4 / 4 / 4 | 0 / 0 / 0 | `BC7A53F4B54F1BBE747976C0F20F7C62B0EE841BC7F219CABC17C892A59556B3` | `7704AC73A782DBAD63414124C1A2A05EBFD62DDFEFA42963A669353251F04117` | `9DD6179546EE5F2A119E7C4552B6A343E6BEDA9AFD85E5AE815EA324ABA6E6DC` |
| 2 | 4 / 4 / 4 | 0 / 0 / 0 | `4CC53D8AFCE01958806D12C39867D8D4906B82E84B01BE434224D9F48DB6B128` | `CE5F1458893264DB47B0C13E8FC1BEBF9640CCAA004163F18865C5F9C35364FF` | `238F742C1CE8888DC210663BA8C4C74985B170E37F23FA057322F3DB1573CE6D` |
| 3 | 4 / 4 / 4 | 0 / 0 / 0 | `618B8574B92F7E70C251D57A25078F8D94D3F92FEA729161F5623008D970D38D` | `69E9F145AC4BBE189A5295BA6AD2B897AFC97F66203A73833B8E07ADE6AA4907` | `17E68C7275FC6FFB19289EA56532CC37757E7006EF709847779F8DB0BE361921` |

The full ignored evidence is under `artifacts/phase84-final-qemu-evidence/runs/run-{1,2,3}/`, including serial output, injections, timeline, QEMU command line, firmware identity, and QEMU stdout/stderr. The exact marker `PHASE84_NORMAL_ETHERNET_PATH=1` and consumer marker appeared four times per boot. Metadata on every delivery was destination `0x525400123456`, source `0x021500000001`, EtherType `0x88B5`, and payload length `0x2E`; sentinels were exactly `1, 2, 3, 4` in order. The final recorded sentinel was `4`, showing no cross-frame stale payload state.

COM1 remained registered and active during RX, with its delivery count isolated. The fixture did not inject a COM1 byte in the RX window. Four Phase 80 TXDW events and the Phase 83 TXDW regression marker remained green per boot. The guest reached `MANAGED_KERNEL_PHASE12_PASS` after the RX proof. RX event sequence validation rejects duplicate/stale typed events; host tests cover stale sequence and recycled-descriptor duplicate protection.

## Builds and tests

| Check | Result |
|---|---|
| ManagedKernel NativeAOT build | Passed; 4,822,016-byte payload, SHA-256 `11C524E424A18FA8AC0F52D306498F60AB26B61CD1E2A4C73A75ADC5C951A431`. |
| Ordinary Gate4 build | Passed without the opt-in Phase 83 RX acceptance define; EFI image SHA-256 `29AC1CB3B22B6638351EB1240DC0A943B68914145B43D95AF9F65297F4062DF5`. The production parser/demux remains present. |
| Phase 84 acceptance Gate4 build | Passed with the acceptance define; EFI image SHA-256 `E32009215FC1E65C6F4162AF097B73BA92B47404BB17995261931C09A7CB8A38`. |
| Managed interrupt host harness | Passed; SDK 10.0.401 discovered automatically, 84 production sources / 85 host compile items / 0 duplicates, 15/15 assertions. |
| Native interrupt host tests | Passed. |
| Phase 14 E1000 host tests | Passed, including DD/EOP, error byte, descriptor length, duplicate/stale sequence, recycle, and ring-wrap checks. |
| Phase 16 Ethernet/ARP host tests | Passed: 69 cases, including exact 14-byte parsing, truncated/oversized frames, network-order EtherType, unsupported dispatch classification, destination policy, consumer failure, metadata ownership, and cross-frame sentinel separation. |
| Driver binding host tests | Passed. |
| Driver service owner/status host tests | Passed. |
| Driver worker host tests | Passed. |
| Network service host tests | Passed. |
| QEMU | Final strict acceptance passed: 3/3 fresh boots. An earlier exploratory set also passed 3/3 before the runner asserted the added Phase 84 markers. |
| `git diff --check` | Passed. |
| SyntheticScheduler | Not required; scheduler and worker wait code were not changed. |

The Phase 16 host runner previously selected a hard-coded SDK 10.0.400 path, which is absent on this machine. It now uses the repository's shared host-toolchain discovery and selected installed SDK 10.0.401. The Phase 16 host project also links `ManagedInterruptEvent.cs`, required by the E1000 protocol source it compiles.

## Phase status and limits

Phase 84 integrates hardware-backed E1000 RX into the managed Ethernet layer; it does not implement ARP, IP, DHCP, sockets, or interrupt-driven synchronous TX. Existing ARP and IPv4 handlers remain connected to their existing EtherTypes; no new network protocol handler was added by this phase.

Phase 81 accepted: **No.** Phase 81R2 accepted: **No.** Normal E1000 TX still polls descriptor DD. No scheduler waits, receive worker threads, packet queues, or Phase 81 TX completion changes were introduced. Multi-descriptor RX, VLAN, jumbo frames, physical NICs, wrong-MAC QEMU filtering, and simultaneous RX/TX hardware timing remain unvalidated.

The smallest follow-up objective is one local QEMU ARP request delivered through this now-normal Ethernet receive boundary to the existing ARP handler, with no IPv4 or higher protocol expansion. Phase 85 has not been started.

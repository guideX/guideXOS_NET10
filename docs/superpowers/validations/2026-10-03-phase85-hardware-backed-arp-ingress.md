# Phase 85 — Hardware-Backed ARP Request Integration Through the Managed Ethernet Layer

**Date:** 2026-10-03
**Prompt phase:** 85
**Outcome:** A — hardware-backed ARP ingress validated.

> Phase 85 proves hardware-backed ARP ingress through the normal managed Ethernet and ARP layers. It does not add DHCP, general IPv4 networking, sockets, or interrupt-driven synchronous TX.

## Repository preflight

- Repository: `D:/dev/guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `81a8b52d9768de5236cd3e4c7fccd2237689dad0` — `Integrate E1000 receive with managed Ethernet layer`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Live starting divergence: 0 ahead / 0 behind. This differs from the prompt's expected 2 ahead / 0 behind; the live repository state was used.
- Starting worktree: clean.
- `.phase`: absent before and after. No phase file was introduced.
- Phase 83 and Phase 84 reports said Phase 85 had not started; no equivalent hardware-backed ARP acceptance was present.

## Existing architecture and address authority

The existing production entry point is `ManagedArpLayer.TryHandleEthernetArp`. The hardware receive path remains:

```text
QEMU local dgram endpoint
  → e1000e RX DMA descriptor
  → native RXT0 MSI capture and acknowledgement
  → typed managed E1000 event
  → ManagedE1000Driver
  → bounded copy into ManagedEthernetLayer's fixed receive buffer
  → Ethernet II parse and EtherType 0x0806 dispatch
  → ManagedArpLayer.TryHandleEthernetArp
  → synchronous return
  → RX descriptor recycle
```

`ManagedEthernetLayer` now passes the already-parsed `ManagedEthernetFrame` to ARP. That preserves Ethernet source and destination, the payload boundary, and the parsed EtherType. `ManagedArpReceiveMetadata` copies validated fields into scalar values while the receive buffer is owned; neither the handler nor its result retains a span into DMA storage.

The existing local IPv4 authority is `ManagedArpLayer._localIpv4`. Its existing static/default value is `10.15.0.1`; `ManagedIpv4Layer` copies that value when it is created, and the existing DHCP lease installation path can update it through `ManagedArpLayer.TryInstallLocalIpv4`. The Phase 85 acceptance mode activated the current address through that existing method. It did not add a new address, DHCP dependency, or production IP default. There is no separate `ManagedNetworkService` local-address owner.

The ARP parser accepts only Ethernet/IPv4 ARP: hardware type 1, protocol type `0x0800`, HLEN 6, PLEN 4, and opcode 1 or the already-supported reply opcode 2. It rejects payloads shorter than 28 bytes before reading fields. The 60-byte Ethernet requests have 46 payload bytes because Ethernet minimum-frame padding follows the 28-byte ARP structure.

Requests targeting another IPv4 address return the bounded `RequestNotForUs` result and are not malformed. A request for the local IPv4 address must have broadcast Ethernet destination and zero ARP target hardware address. Valid ARP requests do not populate the existing cache. The cache has capacity 8, keys on IPv4, stores six-byte MAC values, updates a matching key, and replaces the least-recently-used generation when full. Existing accepted ARP replies can learn sender mappings; request learning was not added.

The ARP responder already existed. For a request for the local address, it builds an opcode-2 ARP reply and submits it through `ManagedEthernetLayer.TryTransmit`, then `ManagedE1000Driver.TryTransmitFrame`. Normal TX still polls descriptor DD.

## Deterministic QEMU acceptance

Three fresh QEMU 11.0.0 boots used the existing private loopback `-netdev dgram` and QEMU `e1000e` mechanism. No external network was used. The guest MAC on each boot was `52:54:00:12:34:56`; its existing local IPv4 address was `10.15.0.1`.

Each boot injected four 60-byte broadcast frames with EtherType `0x0806`, hardware type 1, protocol type `0x0800`, HLEN 6, PLEN 4, opcode 1, and zero ARP target MAC:

| Request | Requester MAC | Requester IPv4 | Target IPv4 | Guest result | Captured reply |
|---:|---|---|---|---|---|
| 1 | `02:15:00:00:00:01` | `10.15.0.2` | `10.15.0.1` | Request for local address | Yes, opcode 2 |
| 2 | `02:15:00:00:00:02` | `10.15.0.3` | `10.15.0.1` | Request for local address | Yes, opcode 2 |
| 3 | `02:15:00:00:00:03` | `10.15.0.4` | `10.15.0.1` | Request for local address | Yes, opcode 2 |
| 4 | `02:15:00:00:00:04` | `10.15.0.5` | `10.15.0.99` | Valid, not for us | No |

Each local reply was captured as a raw datagram from QEMU's E1000 TX endpoint. The host runner checked Ethernet destination=requester MAC, Ethernet source=guest MAC, EtherType `0x0806`, ARP opcode 2, sender MAC/IP=guest, and target MAC/IP=requester. The fourth request had a 500 ms no-reply capture window. Its guest delivery and not-for-us classification were observed, with no reply emitted.

Per accepted boot:

- Four RXT0 causes reached typed managed E1000 events and four completed descriptors were consumed exactly once.
- Ethernet received and parsed counts were both 4; every EtherType was `0x0806` and ARP payload boundary was 46 bytes.
- Descriptor indices were 0, 1, 2, 3; each descriptor had DD+EOP (`0x03`), zero errors, and length 60. All four recycled; final RDT was 3.
- ARP handler results were three local requests/replies and one not-for-us request. Four distinct sender MAC/IP pairs and target IPs were recorded.
- Three replies were captured from QEMU E1000 TX. Reply opcode was 2; reply sender was guest MAC/`10.15.0.1`; reply targets matched the three requester MAC/IP pairs.
- Native capture recorded seven causes: four RXT0 and three TXDW. All seven were acknowledged, with no interrupt storm or drops. The E1000 route and COM1 route remained healthy; COM1 event count stayed isolated.
- Phase 80's four-of-four TXDW proof remained green, and the guest reached `MANAGED_KERNEL_PHASE12_PASS` after ARP processing.

No duplicate typed event reprocessed a descriptor. Existing monotonic event-sequence validation remains unchanged. Phase 14 host coverage continues to reject stale/duplicate RX sequences and already-recycled descriptors. Hardware ARP frames were valid; malformed ARP behavior was covered by the authoritative parser/handler host tests.

## Reply bytes and evidence

The first QEMU-captured reply was:

```text
021500000001525400123456080600010800060400025254001234560A0F0001
0215000000010A0F0002000000000000000000000000000000000000
```

Its SHA-256 was `E7259DD0E4AB1EA1DD90FF5CE0F3B277FEEF60BB40C282ECD23027FD8933F7A7`. The other reply hashes were `906AE4F56182F96058BCD562698737AFE8FEF8994BEB8A30338D58460F4A0083` and `BE675996B099D11E538D91D5DA5A0F33A979AA9F08D91165E05FFC1CC351C010`.

Accepted boot evidence is under `artifacts/phase85-qemu-evidence-accepted/runs/run-{1,2,3}/`. Each run includes serial output, injected frame descriptions and hashes, raw QEMU TX datagrams, a length-prefixed TX capture binary, timeline, QEMU command line, firmware identity, and QEMU stdout/stderr. These generated evidence files remain outside the source commit.

| Boot | Serial SHA-256 | Injection SHA-256 | TX capture binary SHA-256 | Timeline SHA-256 |
|---:|---|---|---|---|
| 1 | `E31A65CA5B828BFAF81C1FE6C3231517494B5F4CF69F76D2C87AC3D42D79D29F` | `D5AE285EB7740C3951730B090266B72873D9C8148347D9F0CAB59A1FA7E3C98F` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` | `C6384F673CF68DA9123C51E4FDA6251F4623A8B09217AEC028743F68BD5FD358` |
| 2 | `3C3AD8A4124C7D9478BB3B46C0BB184DFD406831A4A4A20F4D01ADC06E85F974` | `AE282D90D27C356B46C0FB740E838A5334F86C31BF3C11E3AB91A835E9A9EF6B` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` | `10F37C3D0BBE36EF38666BD4E58A507C58ACABA3458C27FCCFA4F46DC2C00D93` |
| 3 | `64FB067294CAAAE3E87372DAEC1EE87989109D2E6823ADF2EA70D0B7A98A5F3E` | `AD5E8E3F372BEF0700A856587931B022A196894580C09EBF8937459094212F4E` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` | `D32BF0E36A6CBAFB70C5DA5E0363F3B42776B2193DE64472AEEF659BA4A5CB9B` |

The guest E1000 MAC was `525400123456` on all three boots. RX descriptor status/errors/length and ARP metadata were recorded per request in each serial log. The Phase 85 acceptance runner is `tools/Run-ManagedKernelPhase85FreshBoots.ps1`; its ordinary Gate4 build omits the Phase 85 compile-time fixture, while only the separate acceptance image enables it.

## Host tests and builds

| Check | Result |
|---|---|
| ManagedKernel NativeAOT build | Passed; 4,833,792-byte payload, SHA-256 `C2772EF9232CA0B76EC6FC6AFDC36B3AD5D696F68F9A9BCDEDB168F20F28B008`. |
| Ordinary Gate4 build | Passed without the Phase 85 injection define; EFI SHA-256 `D53E21539286F90208850A3450CBDC6830070200A856E5DBEC6ABE2C4682CD98`. |
| Phase 85 acceptance Gate4 build | Passed; EFI SHA-256 `39142D652C30932BFB1DFCC76CEEE4AF3458DB645136026A770DA206A372E5FC`. |
| Phase 16 Ethernet/ARP host tests | Passed, 94 assertions. Covers Ethernet destination/EtherType policy, local and not-for-us requests, sender metadata, truncated payload, HLEN/PLEN, hardware/protocol type, opcode, reply contents, no request cache learning, and bounded cache behavior. |
| Phase 14 E1000 host tests | Passed. Covers DD/EOP, errors, descriptor bounds, recycle, ring wrap, RXT0/TXDW causes, stale/duplicate events, and Phase 83 RX checks. |
| Managed interrupt host harness | Passed; SDK 10.0.401 discovered, 84 production sources / 85 compile items / 0 duplicates, 15/15 assertions. |
| Native interrupt host tests | Passed. |
| Driver binding host tests | Passed. |
| Network service host tests | Passed. |
| Driver service owner and status tests | Passed. |
| Driver worker host tests | Passed. |
| QEMU | Three fresh boots passed, 3/3. |
| `git diff --check` | Passed. |
| SyntheticScheduler | Not required; scheduler and worker wait code were not changed. |

## Scope, status, and limitations

The existing reply path is active and was exercised. Normal E1000 TX still DD-polls. This does not accept Phase 81 or Phase 81R2. No scheduler wait, timeout servicing, timer ownership, DHCP, DNS, ICMP, TCP, UDP, sockets, general neighbor discovery, IPv6 Neighbor Discovery, or external connectivity was added.

The production change carries parsed Ethernet metadata into ARP, adds a bounded `RequestNotForUs` result, and exposes the existing ARP handler through the hardware acceptance path. No unrelated production defect was found. The QEMU acceptance host setup initially lacked the COM1 route for the Phase 85 macro branch; that harness conditional was corrected so Phase 85 keeps the Phase 83 COM1 coexistence setup.

Phase 81 accepted: **No.**
Phase 81R2 accepted: **No.**
Normal E1000 TX still DD-polls: **Yes.**

The smallest useful Phase 86 objective is one deterministic IPv4 ingress frame delivered through the same hardware-backed Ethernet boundary into the existing IPv4 parser, without adding sockets or transport protocols.

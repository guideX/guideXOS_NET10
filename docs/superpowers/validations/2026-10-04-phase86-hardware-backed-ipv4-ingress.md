# Phase 86 — Hardware-Backed IPv4 Ingress Through the Managed Ethernet and IPv4 Layers

**Date:** 2026-10-04
**Prompt phase:** 86
**Outcome:** B — hardware-backed IPv4 ingress passed after repairing the inactive IPv4 receive gate.

> Phase 86 proves hardware-backed IPv4 ingress through the normal managed Ethernet and IPv4 parser. It does not add ICMP, UDP, TCP, sockets, DHCP, routing, or interrupt-driven synchronous TX.

## Repository and preflight

- Repository: `D:/dev/guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `e3a11f9c5b6de0c2ea9c8176e58ec9ce036707af` — `Validate hardware-backed managed ARP ingress`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: 1 ahead / 0 behind.
- Starting worktree: clean.
- `.phase`: absent before work; no `.phase` file was introduced.
- Phase 84 and Phase 85 reports were read. Neither contained a Phase 86 implementation or equivalent hardware-backed IPv4 acceptance.

## Existing production architecture

The Ethernet ingress boundary is in `ManagedEthernetLayer.TryDispatchFrame`. Its production EtherType classifier maps `0x0800` to IPv4 and synchronously calls `ManagedIpv4Layer.TryHandle(parsed.Payload)`. On hardware RX, `ManagedE1000Driver` validates the completed E1000 descriptor, copies DMA bytes into `ManagedEthernetLayer`'s fixed 2048-byte buffer, synchronously invokes that same dispatcher, and recycles the descriptor after dispatch returns.

`ManagedIpv4Protocol.TryParse` is the existing authoritative parser. It validates IPv4 version, a 20-byte header (IHL 5; options remain unsupported), total length, fragmentation policy, TTL, and the one's-complement header checksum over network-order bytes. Its packet view is a `ref struct`; source/destination addresses and payload are spans into the current receive buffer and are used synchronously. The Phase 86 observer stores only scalar values, never a payload span.

The existing address authority is `ManagedArpLayer._localIpv4`, also used by `ManagedIpv4Layer`. It defaults to `10.15.0.1`; the acceptance mode reinstalled that same address via the existing `TryInstallLocalIpv4` boundary. No second owner or DHCP dependency was added. The E1000 guest MAC was `52:54:00:12:34:56`.

The parser previously coupled structural validity to destination ownership, and `ManagedIpv4Layer.TryHandle` returned `Failed` whenever transport state was inactive. Hardware acceptance exposed that normal ingress could not reach the IPv4 parser until a transport-facing phase had activated it. The repair parses and validates IPv4 independently of transport activation, classifies valid nonlocal packets as ignored before protocol dispatch, counts unsupported local protocol numbers, and keeps ICMP/UDP/TCP handlers gated by their existing activation state. The owner-aware `TryParse(packet, localAddress, ...)` overload remains available for existing consumers.

## Packet format and IPv4 behavior

QEMU used its local datagram injector and the `e1000e` device. Every injected Ethernet frame was 60 bytes, addressed to the guest MAC, and had EtherType `0x0800`. The source MAC changed per frame from `02:15:00:00:00:01` through `02:15:00:00:00:04`. Source IPv4 changed from `10.15.0.2` through `10.15.0.5`.

| Sequence | Destination IPv4 | ID | Header checksum | Result |
|---:|---|---:|---:|---|
| 1 | `10.15.0.1` | `0x8601` | `0xE0C7` | Local; post-parser observer accepted sentinel 1 |
| 2 | `10.15.0.1` | `0x8602` | `0xE0C5` | Local; post-parser observer accepted sentinel 2 |
| 3 | `10.15.0.1` | `0x8603` | `0xE0C3` | Local; post-parser observer accepted sentinel 3 |
| 4 | `10.15.0.99` | `0x8604` | `0xE05F` | Valid not-for-us; no local protocol observer delivery |

All packets used version 4, IHL 5 / 20-byte header, total length 24, flags 0, fragment offset 0, TTL 63, and protocol 253. The four-byte payload was `86 <sequence> A5 5A`. The IP total length, rather than the received Ethernet payload length, bounded the payload view: the 46-byte Ethernet payload held 24 bytes of IPv4 and 22 bytes of Ethernet padding. The parser validated each header checksum before producing metadata.

Protocol 253 is treated as unsupported. The acceptance-only observer runs after the production parser, validates the bounded sentinel for local packets, and records only scalar metadata. It is enabled only by the Phase 86 acceptance mode and is absent from ordinary acceptance builds. The three local packets were classified unsupported after parsing; the not-for-us packet was filtered before protocol dispatch and did not increment that count.

IHL values above 5 (IPv4 options) are rejected. Packets with MF or nonzero fragment offset are rejected; reassembly is unsupported. DF is accepted. TTL zero is rejected before upper-layer delivery. Maximum IPv4 packet length is 2034 bytes, respecting the 2048-byte managed Ethernet receive bound less the 14-byte Ethernet header.

## Hardware evidence

Three fresh QEMU boots each injected four packets. On every boot, all four frames traversed E1000 RX DMA, native RXT0 MSI capture, typed managed E1000 event delivery, descriptor validation, the managed Ethernet parser, EtherType `0x0800`, and the production IPv4 parser. The per-boot results were:

- Four RX descriptors completed and four managed deliveries were logged in order, with no duplicate or stale delivery.
- Four IPv4 frames parsed; three were classified local and one was valid but not for this host.
- The three local packets reached the acceptance-only bounded observer; the fourth reached the valid not-for-us result without local delivery.
- Protocol 253 was classified unsupported three times, only after local-address filtering.
- Version, IHL, total length, identification, TTL, protocol, checksum-validated result, and payload length matched the injected bytes on every frame.
- The per-frame sentinels remained distinct and ordered. Ethernet padding was excluded from IPv4 payload length.
- All four descriptors were recycled; RX ring indices were 0–3 and final RDT was 3.
- COM1 remained active and isolated. Phase 80 TXDW polling proof remained green; the Phase 83/86 log retained `PHASE83_TXDW_REGRESSION=4`.
- Every boot progressed to `MANAGED_KERNEL_PHASE12_PASS`.

The Phase 85 hardware ARP acceptance was rerun as a regression. It continued to deliver four ARP requests through E1000 RX and the existing ARP handler, captured three correct local-address replies from QEMU E1000 TX, classified one request not-for-us, and recycled every descriptor. Normal E1000 TX continues to poll descriptor DD.

## Host coverage, builds, and regressions

- Phase 16 Ethernet/ARP/IPv4 host suite: **111 cases passed**. New cases cover the Ethernet-to-IPv4 boundary, field extraction, total-length versus Ethernet padding, local/not-for-us structural parsing, exact 20-byte packets, bad version, IHL values below 5, options rejection, truncated header, short/truncated total length, network-order checksum rejection, fragment rejection, zero TTL, unsupported protocol validity, and the maximum bounded packet.
- Phase 14 E1000 host suite: passed, including RX descriptor ownership, DD/EOP, error handling, bounds, recycle, ring wrap, RXT0, TXDW, and stale/duplicate event checks.
- Managed interrupt harness: passed SDK discovery and source completeness; 84 production sources / 85 host compile items / 0 duplicates; 15/15 assertions. Toolchain checks: 6/6.
- Native interrupt host tests: passed.
- Driver-binding host tests: passed.
- Network service host tests: passed.
- Driver service owner/status host tests: passed.
- Driver worker host tests: passed.
- The optional Phase 17 host runner was attempted but could not run: its script hardcodes the unavailable .NET SDK 10.0.400, and building its project with installed SDK 10.0.401 exposes a missing linked `GxManagedKernelInterruptEventV1` source. The mandatory Phase 16 suite was extended and passed.
- ManagedKernel NativeAOT build: passed; final payload SHA-256 `226C86649C3DAD4F15668A5B8710B6BE1BBDCA2A12CCC922A27A9A2BA8087A07`.
- Ordinary Gate4 build: passed with the Phase 86 fixture disabled.
- Phase 86 Gate4 build: passed with the opt-in fixture enabled.
- QEMU Phase 86: **3/3 fresh boots passed**.
- QEMU Phase 85 ARP regression: passed on three fresh boots, with three captured local replies per boot.
- `git diff --check`: passed; Git only reported LF/CRLF normalization notices.
- SyntheticScheduler: not required; no scheduler code changed.

Evidence directories:

- Phase 86 accepted boots: `artifacts/phase86-qemu-evidence-final/runs/run-{1,2,3}/`
- Phase 86 summary: `artifacts/phase86-qemu-evidence-final/phase86-summary.txt`
- Phase 85 ARP regression: `artifacts/phase86-arp-regression/runs/run-{1,2,3}/`

SHA-256 evidence hashes:

| Run | Serial | Injection log | Timeline | ARP TX capture |
|---|---|---|---|---|
| 86/1 | `D65CC1EBEFE7CA8ACE14B6D0534022F498122386AA5C2F0F19BEAB91E17C42A0` | `007C3DDF954E16C5189B6553B593054C9F495FCEEFD58B9B828372A81FEC8A13` | `8EC58F7B19F3F47CC2CDBDC0700EF8AB50241673B0CFD98324E20F722948D900` | — |
| 86/2 | `96A246138903D1D121D4167A2BEFB7FF333CC0E20F535C3D50D2CB8871230D17` | `EA283E00A4A8CC6F448280359EC05E8BC8EA62378F369C70C65ADEA6DFC7BC4C` | `8E32B2C75B2859633AFFA9D70EBE1AD98026FA7C4FC49A6998B1D0867E278C36` | — |
| 86/3 | `86ACA6C43F9A03963F980C2E3A69B089EF072890A1174F318228DA45285F2CBA` | `F12E7ED7D2378A96C6C3EC01D54100210E9F6B37E867B5590E86EAEDC03FB01F` | `4A3CBF9E5BF521777FC8753D29FE361800F860DB88333874D67CAA6EB8E821F8` | — |
| 85 regression/1 | `85BCC4484FF7DAED92A348F0355959D192FC074498D2984138569F67569C9170` | `B086CFEE99EE1AA1EAD7C6F84DAB1AE9CD3468F38CC263AD8015BA17A0223776` | `FC6640EE8A0413F66AE3E991C9F5EE1678A0D40974A51F249186CE074C9DA1A7` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` |
| 85 regression/2 | `7D66ED45B5E3EF19FB794B5E2484BF0F919C527073DB3CC6DC115F9C2C51F503` | `F19A0B81968A6426AA83FEF859FDF4A761A6B27D2619D022D1DFD4D9B0930CE1` | `759CC53AEB986FD8D65CE2DE6F8CDE6C93AA9E34ABDB4A783325DF9925AA2183` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` |
| 85 regression/3 | `6CFC81CC89745BDF83C9D2427391A1ABD4E2A8D6C4BA8D2983BC4C77AF4A6BF6` | `A91C3EEF3640C805E92960CFB4D28F0CFD029B426150C8899FE0AD1AF21874D4` | `A106E7B7E08EA332EA2C00D884F4BCA187A16CBF3C34003FE48C76B3AC62163B` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` |

The Phase 86 normal Gate4 EFI SHA-256 is `D53E21539286F90208850A3450CBDC6830070200A856E5DBEC6ABE2C4682CD98`; the Phase 86 fixture EFI is `F7B64AF8DBFE949B6A0454F27ED6342929B737FA05DFC02F336DA19A706A6E8A`. The final ManagedKernel payload SHA-256 is `226C86649C3DAD4F15668A5B8710B6BE1BBDCA2A12CCC922A27A9A2BA8087A07`.

## Scope and phase status

No ICMP, UDP, TCP, sockets, DNS, DHCP, routing, NAT, forwarding, IPv6, fragmentation reassembly, scheduler waits, or interrupt-driven synchronous TX was added. The Phase 86 observer is acceptance-only and does not implement protocol 253 as a transport.

Phase 81 accepted: **No.**
Phase 81R2 accepted: **No.**
Normal E1000 TX still polls DD: **Yes.**

Suggested smallest Phase 87 objective: validate one deterministic ICMPv4 echo request over the same hardware receive path using the existing ICMPv4 handler and bounded transmit path, without adding a socket API or changing TX completion semantics.

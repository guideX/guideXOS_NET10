# Phase 87 — Hardware-Backed ICMPv4 Echo Request and Reply

**Date:** 2026-10-04  
**Prompt phase:** 87  
**Outcome:** A — hardware-backed ICMPv4 Echo Request/Reply validated.

> Phase 87 proves a hardware-backed ICMPv4 Echo Request/Reply exchange through the managed Ethernet, IPv4, and existing ICMPv4 layers. It does not add UDP, TCP, sockets, DHCP, routing, or interrupt-driven synchronous TX.

## Repository and preflight

- Repository: `D:/dev/guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `a88fc3491b9aad108f56cc454b39bb1f4aa56326` — `Validate hardware-backed managed IPv4 ingress`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: 0 ahead / 0 behind.
- Starting worktree: clean.
- `.phase`: absent before work and remains absent.
- Phase 85 ARP and Phase 86 IPv4 validation reports were read. No Phase 87 or equivalent hardware-backed ICMP echo acceptance was present.

## Production path and repair

The E1000 driver's completed RX descriptor is validated, copied into the managed Ethernet receive buffer, and dispatched synchronously. `ManagedEthernetLayer.TryDispatchFrame` applies the existing destination-MAC policy and EtherType `0x0800` dispatch, then passes the parsed Ethernet payload and source MAC to `ManagedIpv4Layer.TryHandle`. The production `ManagedIpv4Protocol.TryParse` validates the bounded IPv4 packet, header checksum, total length, TTL, fragmentation policy, and destination ownership before protocol dispatch. Protocol number 1 reaches `ManagedIcmpv4Protocol.TryParse` and the existing ICMPv4 handler.

The ICMPv4 builder and request/reply state already existed, but the normal hardware Phase 86 fixture left the IPv4 protocol handlers inactive. The narrow integration repair activates the existing handler only in the opt-in Phase 87 acceptance mode. It also carries the already-parsed Ethernet source MAC through IPv4 dispatch. A valid same-subnet Echo Request can therefore reply directly to the ingress peer MAC through the normal Ethernet/E1000 transmit boundary, without ARP cache learning or a new neighbor table. This MAC is not hard-coded in production.

ICMP parsing requires at least the eight-byte Echo header, bounds the message to the existing 256-byte payload cap, and validates the ICMP checksum over exactly the IPv4-bounded ICMP message. Valid Type 8 Code 0 requests enter reply construction. Type 0 Code 0 replies use existing correlation and never generate a reply. Other valid types/codes return `IcmpUnsupported`; invalid checksums or truncated headers are malformed ICMP packets. Nonlocal IPv4 packets are filtered before ICMP dispatch.

## Request and reply

The acceptance injector uses the QEMU local datagram endpoint and E1000 `e1000e`, not a LAN connection. Each Ethernet request is 60 bytes, with a 45-byte IPv4 packet and 1 byte of Ethernet padding after IPv4. Requester is `10.15.0.2` / `02:15:00:00:00:02`; guest is `10.15.0.1` / `52:54:00:12:34:56`. Each request has IPv4 TTL 63, valid IPv4 checksum, protocol 1, ICMP Type 8 Code 0, identifier `0x8701`, and a unique 17-byte payload `GXOS-PHASE87-0001`, `...0002`, or `...0003`.

| Sequence | IPv4 ID | IPv4 checksum | ICMP checksum | Payload |
|---:|---:|---:|---:|---|
| 1 | `0x8701` | `0xE0AE` | `0x4AE3` | `GXOS-PHASE87-0001` |
| 2 | `0x8701` | `0xE0AD` | `0x49E2` | `GXOS-PHASE87-0002` |
| 3 | `0x8701` | `0xE0AC` | `0x48E1` | `GXOS-PHASE87-0003` |

The production reply reverses source/destination IPv4 addresses, retains protocol 1 and identifier/sequence/payload, changes ICMP to Type 0 Code 0, computes a fresh ICMP checksum and IPv4 header checksum, and uses the existing outbound TTL 64 and identification behavior (`0x1800 + sequence`). The E1000 TX path submits each frame and completes using descriptor DD polling; each completion also naturally produced and delivered a typed TXDW event. QEMU's host endpoint captured each actual transmitted frame. The host validator checked source/destination MAC and IP, EtherType, IPv4 length and checksum, ICMP type/code and checksum, identifier, sequence, and exact payload bytes. It accepted exactly one reply per request and reported a zero-duplicate quiet window. The unused final Ethernet padding byte is excluded from ICMP checksum and payload by IPv4 total length.

## QEMU evidence

Three fresh QEMU boots passed. Each boot injected three sequential requests and captured three matching replies. All boots logged three managed ICMP deliveries, three submit/DD-poll completions, three post-baseline TXDW deliveries, three recycled RX descriptors, COM1 active during RX and isolated afterward, and `MANAGED_KERNEL_PHASE12_PASS`. The normal TX completion model remains descriptor polling; TXDW remains an interrupt observation, not a synchronous completion signal.

Accepted evidence: `artifacts/phase87-qemu-evidence-final2/runs/run-{1,2,3}/`. Each run includes serial output, injections and validated captured reply bytes, raw QEMU TX datagrams, capture log, timeline, command line, firmware identity, and QEMU stdout/stderr. Capture binary SHA-256 is identical for all three runs because each contains the same three deterministic reply frames: `CE4F5E4B168695B3D38862F9B19EB758CC3E1BB756D3E463C0C7BDDB51DA07CB`.

| Run | Serial SHA-256 | Injection SHA-256 | TX capture SHA-256 | Timeline SHA-256 |
|---|---|---|---|---|
| 1 | `5686FBCB780A58F6832F87825366158DC7CADD9A59B64CE2A1D68D6E4F6D7346` | `54BDA95510BF284252CA5948DE0E92FA41A8D9B8284F74B57AA265659652C793` | `CE4F5E4B168695B3D38862F9B19EB758CC3E1BB756D3E463C0C7BDDB51DA07CB` | `C5F7F35A4E702BEA03646DBEFA0AA42090FA3D83383E80EED5C36E09301DBBB4` |
| 2 | `06C69673FC4249BA6812D2EC66520A40645EDFC7E71C051C2AB5EE2AE1BA846E` | `A452327EFF7AA5F791B2153EB31AE15D9E359E7DC04FE6CC7CCC0B3EA97A2165` | `CE4F5E4B168695B3D38862F9B19EB758CC3E1BB756D3E463C0C7BDDB51DA07CB` | `E11413F2D3C2F220BB2588F7421225DB0EDAF122A731A150CD6E1B92AB110E8F` |
| 3 | `EFD9D48FEEDF5EC512CE3E929DABD7E6451A3CA90265DA93B4CE627226F4B447` | `0916DD5F30BDB3C600B75513F06FA15DDB4E3B63F6482831317001A9E9234207` | `CE4F5E4B168695B3D38862F9B19EB758CC3E1BB756D3E463C0C7BDDB51DA07CB` | `379FF9736E4FE2F8640A19543BBC8AA6974C3AFE75D4CD615F1BD79CE16FC350` |

Payload and image:

- ManagedKernel NativeAOT payload: 4,856,832 bytes; SHA-256 `AB4CC7178AE9442AEC378B75CBB2528CB32C3B9D36844758D368A6BE01C00F30`.
- Ordinary Gate4 image was built with the Phase 87 fixture disabled.
- Separate Phase 87 Gate4 acceptance image enabled the fixture.
- Build logs and images are under `artifacts/phase87-build-final/` and were not added to the source commit.

The Phase 85 hardware ARP regression was also attempted. Three local requests produced three valid captured ARP replies matching the prior Phase 85 reply byte hashes. The fourth not-for-us request did not reach its expected guest delivery marker before that focused run stopped. Therefore the focused ARP responder request/reply remains positively demonstrated, but that regression run did not complete its fourth-frame not-for-us check. The independent Phase 86 IPv4 hardware regression passed 3/3 boots before the final TXDW reporting-only adjustment; the adjustment changes Phase 87 proof accounting and not IPv4 parsing.

## Host tests, regressions, and limits

- Phase 16 Ethernet/ARP/IPv4/ICMP host suite: **122 cases passed**, including ICMP request/reply construction, network-order checksums and odd-length payload checksum, ID/sequence/payload preservation, bad checksum, wrong code, truncated header, unsupported type, incoming reply disposition, Ethernet padding exclusion, and nonlocal IPv4 suppression.
- Phase 14 E1000 host tests: passed.
- Managed interrupt harness: passed toolchain discovery 6/6, source completeness 84 production sources / 85 host compile items / 0 duplicates, and 15/15 host assertions.
- Native interrupt host tests: passed.
- Driver-binding tests: passed.
- Network service tests: passed.
- Driver service owner/status tests: passed.
- Driver worker tests: passed.
- ManagedKernel NativeAOT, ordinary Gate4, and Phase 87 Gate4 builds: passed.
- QEMU Phase 87: **3/3 fresh boots**, three hardware requests and three hardware-captured replies on each.
- QEMU Phase 86: prior accepted regression **3/3** fresh boots.
- QEMU Phase 85: three valid hardware ARP replies were captured in the focused regression attempt; its fourth not-for-us fixture did not complete as described above.
- `git diff --check`: passed before documentation was added; rerun before commit.
- SyntheticScheduler: not required; scheduler code was not changed.
- Phase 17 optional runner: not attempted; it is unrelated and known stale.

No UDP, TCP, sockets, DNS, DHCP, routing, NAT, forwarding, IPv4 reassembly, IPv6, general ICMP error generation, scheduler wait changes, or interrupt-driven synchronous TX was added. Echo replies are restricted to directly reachable sources with a usable Ethernet source MAC. Phase 81 accepted: **No**. Phase 81R2 accepted: **No**. Phase 88 was not started.

No remotes, credentials, Git identity, branches, worktrees, or history were altered before the final Phase 87 commit. No force push was used.

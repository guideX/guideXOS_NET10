# Phase 89 Hardware-Backed UDP Ingress Validation

Date: 2026-10-05
Prompt phase: 89R — Recover UDP QEMU Acceptance Harness and Close Phase 89
Outcome: **B — harness recovery exposed narrow acceptance integration defects; repairs passed full acceptance**

## Continuation and original blocker

Phase 89 began with its production UDP parser, IPv4 protocol 17 dispatch, fixed endpoint registration, checksum validation, copied bounded receive storage, host tests, Gate4 wiring, and acceptance observer already present in a dirty worktree. The prior Outcome E blocker was the hardware acceptance harness: the fresh-boot runner was damaged during an attempted injector integration and restored from the starting commit. The restored runner parsed cleanly but had no Phase 89 injection wiring, and no authoritative QEMU boot had reached UDP injection.

Phase 89R preserved that dirty implementation. The runner was verified byte-identical to the starting commit before this continuation's edits; the broken attempt had been removed by restoration. A small opt-in Phase 89 mode was then added to the generic runner, with packet construction kept in a dedicated injector and wrapper. There was no broad runner rewrite.

**Phase 89 was initially blocked by its acceptance harness; Phase 89R recovered the harness and supplied the missing authoritative hardware proof without replacing the existing UDP production implementation.**

## Repairs found during diagnostics

The first focused boot stopped at driver startup. The UDP service endpoint registration required DHCP state `Bound`, although Phase 89 deliberately establishes fixed local IPv4 `10.15.0.1`. Registration now requires an active IPv4 layer and a usable local address. This permits the already-supported fixed-address configuration while retaining the usable-address check.

The next diagnostics proved that QEMU accepted the four frames and E1000 DMA wrote their descriptors, but the guest rejected the typed RXT0 events. Phase 89 mode had not armed the shared Phase 83 RX event route. `EnablePhase89UdpMode` now enables that route.

The first successful UDP proof then stopped at teardown because the shared Phase 83 standalone-proof gate was also active. Phase 89 performs its own RX descriptor, RXT0, dispatch, parser, consumer, unbound-port, and recycle checks, so teardown now uses the Phase 89 proof for that combined mode rather than requiring a separate Phase 83 proof flag.

These were narrow production/mode integration repairs. The receive-slot copied-storage lifetime and UDP checksum policy were not changed.

## Harness and packet design

- Generic runner: `tools/Run-ManagedKernelPhase11FreshBoots.ps1`. It remained a common fresh-boot runner with optional Phase 89 arguments and a small injection callback at the existing dgram peer socket.
- Dedicated wrapper: `tools/Run-ManagedKernelPhase89Udp.ps1`. It runs injector byte validation, builds ManagedKernel NativeAOT plus normal and Phase 89 Gate4 harnesses, runs the requested boots, and checks the guest markers and counters.
- Dedicated injector: `tools/Inject-ManagedKernelPhase89Udp.ps1`. It builds four deterministic frames and sends them using the already-bound peer UDP socket. It opens no new socket and adds no guest socket, blocking receive, receive queue, or poll/select behavior.
- Runner syntax was checked before modification and after the narrow integration; PowerShell parsing succeeded. Runner arguments remain optional, preserving existing modes.
- QEMU uses the existing local-only `-netdev dgram,id=net0` backend and `e1000e,netdev=net0,addr=2`. Guest MAC: `52:54:00:12:34:56`; local IPv4: `10.15.0.1`. The dgram host endpoint is `127.0.0.1:<per-boot-port>`; run 1 used local port 51253 and peer source port 51254. Runs 2 and 3 used 64765/64766 and 55620/55621.
- Injector byte validation passed for destination/source Ethernet MAC, EtherType `0x0800`, IPv4 version/IHL `0x45`, total length, source and destination, protocol 17, IPv4 header checksum, UDP ports/length/checksum, and payload sentinel. The injector reports every UDP checksum nonzero and independently valid.

The four datagrams per boot were:

| Sequence | Source IPv4 | Source port | Destination port | UDP checksum | Expected result |
|---:|---|---:|---:|---:|---|
| 1 | 10.15.0.2 | 24001 | 15890 bound | `0x5E4E` | One bound consumer delivery |
| 2 | 10.15.0.3 | 24002 | 15890 bound | `0x5E4A` | One bound consumer delivery |
| 3 | 10.15.0.4 | 24003 | 15890 bound | `0x5E46` | One bound consumer delivery |
| 4 | 10.15.0.5 | 24004 | 15891 unbound | `0x5E41` | Valid parse, unbound classification, no consumer delivery |

Every frame had destination IPv4 `10.15.0.1`, IPv4 total length 34, UDP length 14, a 6-byte `89 <sequence> A5 <sequence marker> C3 3C` payload, and a 60-byte Ethernet frame including padding.

## Production path and proof

The production parser entry is `ManagedUdpProtocol.TryParse` in `src/ManagedKernel/ManagedUdp.cs`; IPv4 protocol 17 reaches it through `ManagedIpv4Layer.TryHandleUdp` in `src/ManagedKernel/ManagedIpv4Layer.cs`. The fixed endpoint is registered at UDP port 15890 and delivered to the existing bounded `ManagedNetworkService` receive slot. Each bound callback copies its metadata and payload into bounded storage; the E1000 proof consumes the slot once and checks it does not retain a frame view. Port 15891 has no endpoint and is classified as unknown/unbound.

Three diagnostic boots were needed to identify the startup route and teardown gate issues. The final one-boot diagnostic completed through the later `GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS` marker. The final acceptance then passed on **3/3 fresh QEMU boots**. Each boot emitted `PHASE89_UDP_HARDWARE_INGRESS_PASS=1`, completed all four RX descriptors, and reached the later guest progress marker.

Per boot, results were identical:

- injected Ethernet frames: 4; RX DMA completions: 4; managed RXT0 events: 4;
- UDP parsed: 4; valid: 4; malformed drops: 0; checksum failures: 0;
- bound classifications and consumer callbacks: 3; observed sequences: 1, 2, 3 exactly once;
- unbound classifications: 1; unknown-port count: 1; unbound consumer callbacks: 0;
- descriptor recycle count: 4; final RDT: 3; no duplicate or stale delivery;
- native MSI entries: 4; native interrupt captures: 4; native RXT0 captures: 4; native TXDW captures: 0;
- the separate managed typed-event TXDW cause counter was 4. RXT0 and TXDW remain independent counters; acceptance does not use an aggregate interrupt total;
- interrupt storm: 0; COM1 isolation marker passed; NIC quiesced and DMA released; later guest progress passed.

The unbound frame proof checks valid Ethernet and IPv4 metadata, valid UDP parse and checksum, the unbound destination port, the incremented unknown-port count, no pending bound consumer receive, and descriptor recycle. Absence of a callback alone is not used as proof.

## Regressions and builds

- Phase 16 networking suite: **139 cases passed**, including **55 UDP cases**.
- Phase 14 E1000 host suite: passed.
- Managed interrupt harness: passed, 15 assertions; toolchain and source completeness checks passed.
- Native interrupt host suite: passed.
- Driver-binding suite: passed.
- Network-service suite: passed.
- Service-owner and service-status suites: passed.
- Worker suite: passed.
- ManagedKernel NativeAOT build: passed with .NET SDK 10.0.401.
- Normal Gate4 build: passed.
- Phase 89 Gate4 build: passed.
- ARP regression: Phase 88 real QEMU request/reply negative-path acceptance passed 3/3; 4 requests per run, 3 local, 1 not-for-us, 3 captured replies, 4 descriptors recycled.
- ICMP regression: Phase 87 real QEMU ICMP Echo round trip passed 3/3; 3 requests/replies per run.
- Optional Phase 17 runner was not attempted. Scheduler work and Phase 81/81R2 were not changed.
- `git diff --check` passed. Temporary event-shape instrumentation used in diagnosis was removed before the final build and acceptance.

## Evidence and hashes

Final acceptance evidence is in `artifacts/phase89-acceptance-final-evidence`; generated evidence and build artifacts are ignored and are not included in the source commit. Run summaries:

| Boot | QEMU dgram local/peer ports | Serial SHA-256 | Injector log SHA-256 | Timeline SHA-256 |
|---:|---|---|---|---|
| 1 | 51253 / 51254 | `423C4EDE4F00D16D6A57F4F9A2716385471D8D0F35947CE7FA0E03B3594D73CB` | `5AC13DCE1AA46CDE343266B0D7A3E57D447225E7338544D6B25A02BFD0730997` | `E9065A9E4B19D332BB621E83CFD939CD3CED78F22164E05E2A739F632CF40E8F` |
| 2 | 64765 / 64766 | `383B0F15AFBE8471325F0C174B16F9F820B98444833790B1976B2B68E4B70D6A` | `0A10E975A9D69394BD3A8681269E6AA7E08F3BB73A22AE298692EEA13F09D2C1` | `C69275806A1FE4E6F63C529D007FFBF9DF0C04FB93C48925549518EA2A227447` |
| 3 | 55620 / 55621 | `AA1B6B06976494698FC668E4CBBDBA4B8161911E0FB4A5204C63EFB776B747B7` | `5B751FABF0365EA44BE1E98E0ECD691FFC268DA595DDF1B3C33F8A4F96157280` | `D9F0EDB5D37DAB55B41F1A44EB08D56B4F73E97605EA0A4EC542800094A87515` |

Final ManagedKernel NativeAOT payload SHA-256: `9053B4C1D1F0AD976B24EB8CDBA383956031507605D78ED0EC36D6ADF983C419`.
Final Phase 89 summary SHA-256: `756423A24B0FB9960507F74DCDE697704B69A447040ECCD7852509E5C5FD0ABE`.
Injector validation SHA-256: `D713CCC25B75054C061EB16430047E0AA49FDDE0F0A93CAA93828BF280E9D5A9`.

## Scope and remaining limits

Phase 89 is accepted for the deterministic local QEMU `e1000e` ingress matrix. This proves the bounded receive/parse/dispatch path and does not add general socket semantics. Normal E1000 TX completion remains DD polling. Phase 81 and Phase 81R2 remain unresolved and unaccepted. No Phase 90 work was started.

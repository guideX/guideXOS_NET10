# Phase 88 — Hardware-Backed ARP Negative-Path Regression Closure

**Date:** 2026-10-04 (client date)  
**Prompt phase:** 88  
**Outcome:** B — a Phase 85 acceptance-accounting defect was found and narrowly repaired; the production ARP negative path remained correct.

> **Phase 88 closes the incomplete ARP not-for-us regression observed during Phase 87; it does not add a new network protocol.**

## Preflight and prior contract

- Repository: `D:/dev/guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `36da7c36c7ed8d95d6dd6ebf01551961bdecd22e` — `Validate hardware-backed ICMPv4 echo`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: 1 ahead / 0 behind; starting worktree clean.
- `.phase`: absent before and after; no phase file was introduced.
- Phase 85 established the contract: three valid local ARP replies, a fourth valid request for `10.15.0.99` classified not-for-us, no reply, descriptor recycle, and normal later progress. Local IPv4 is `10.15.0.1`; guest MAC is `52:54:00:12:34:56`.
- Phase 87 reported that the focused ARP fourth fixture did not reach its expected marker. Its saved evidence contains only the three-request ICMP fixture. The Phase 85 accepted evidence was available and shows its not-for-us request reached delivery 4 and the hardware pass marker. The Phase 87 runner's ICMP fixture path is exclusive with the Phase 85 ARP mode, so it cannot serve as the Phase 85 regression runner.

## Root cause and repair

The production behavior was correct. `ManagedArpLayer.TryHandleEthernetArp` validated Ethernet/IPv4 ARP, recorded target `10.15.0.99`, returned `RequestNotForUs`, and generated no reply. `ManagedE1000Driver` consumed the real E1000 RXT0 event, parsed via Ethernet and ARP, and recycled descriptor index 3 with DD+EOP and zero errors; RDT advanced to 3.

The intermittent product proof failure was a fixture accounting defect. Phase 85 aggregated baseline Phase 80 traffic and required `_phase80DeliveryCount >= 8`, implicitly treating the difference from 4 as TXDW count. TXDW observations vary with timing and can arrive coalesced with RXT0; the count may be 7 even when all four ARP RXT0 events were handled. A three-boot diagnostic reproduced that: fourth delivery, not-for-us classification, and recycled descriptor were logged before `FAIL:managed-kernel-phase83-managed-rx-proof`. This is not an ARP classification, injection, capture, or descriptor defect.

The narrow repair counts Phase 85 RXT0 and TXDW event causes independently and reports both. It removes the arbitrary combined-event minimum while preserving exact per-frame parsing/recycle checks. The new Phase 88 wrapper runs the Phase 85 fixture directly, requires four guest deliveries and recycle markers, the explicit guest pass marker, exactly three known ARP reply hashes, and later progress. The no-reply window remains bounded at 500 ms and secondary to guest completion.

The Phase 87 non-completion symptom itself was not reproduced from a saved failed run; the report's attempt was not preserved. Available Phase 85 accepted evidence resolves the ambiguity. The separate intermittent aggregate proof failure was reproduced and repaired.

## Hardware acceptance

Three final fresh QEMU boots passed. Injection path: host loopback datagram → QEMU `e1000e` → guest RX DMA. Every boot used positive-first order:

`local ARP → local ARP → local ARP → not-for-us ARP`

The production handler and Phase 85 proof then emitted the explicit completion marker. A subsequent `MANAGED_KERNEL_PHASE12_PASS` established later system progress. The host observed no ARP reply during the fourth request's 500 ms quiet window. Captured ARP replies matched the three known positive reply hashes. Four Phase 80 fixture datagrams are also present in the capture.

| Per boot | Count |
|---|---:|
| Requests injected / received / parsed | 4 / 4 / 4 |
| Local / not-for-us | 3 / 1 |
| ARP replies attempted / captured | 3 / 3 |
| Replies for negative request | 0 |
| RX descriptors completed / recycled | 4 / 4 |
| RX RDT after recycle | 3 |
| Phase 85 RXT0 deliveries | 4 |
| Phase 85 TXDW deliveries | timing-dependent; separately counted |

Negative target: `10.15.0.99`. Sender MAC/IP: `02:15:00:00:00:04` / `10.15.0.5`. It was not learned into the ARP cache; existing policy does not learn valid requests. Sequence B (negative-first, then local) passed in the Phase 16 host regression; the three-boot QEMU matrix used sequence A. Negative-first hardware boot was not run.

COM1 coexistence and isolation markers passed on all final boots. No RX ring stall was observed. Interrupt storm marker remained zero. No post-negative ICMP probe was run; Phase 12 later progress is the guest's subsequent liveness proof.

## Regression and build results

- Phase 16 Ethernet/ARP/IPv4/ICMP host suite: **128 cases passed**, including not-for-us zero-reply and both ordering/liveness cases.
- Phase 14 E1000 host suite: passed; RX DD/EOP validation, RXT0 event classification, recycle, ring wrap, stale/duplicate rejection retained.
- Managed interrupt harness: passed toolchain discovery, source completeness (84 production sources / 85 compile items / 0 duplicates), and 15/15 assertions.
- Native interrupt suite: passed.
- Driver binding suite: passed.
- Network service suite: passed.
- Service owner/status suite: passed. Worker service suite was not separately rerun; no service code changed.
- ManagedKernel NativeAOT build: passed; 4,856,832-byte payload, SHA-256 `E484CE100824A70E7B81882F94CEC7EFF616912C6D8ABEE1C677B78CB8BF9439`.
- Ordinary Gate4 build: passed with Phase 88 acceptance define disabled.
- Phase 88 opt-in Gate4 build: passed.
- QEMU: 3/3 final boots passed.
- Phase 87 ICMP: no rerun; shared Ethernet/IPv4/ICMP production code was not changed.
- SyntheticScheduler: not required; scheduler code was not changed.

The Phase 87 report's statement that Phase 88 had not started is historically accurate at that report's commit and is not edited here.

## Final evidence

Evidence directory: `artifacts/phase88-qemu-evidence-final/runs/run-{1,2,3}/`. Build logs/images: `artifacts/phase88-build-final/`. Summary: `artifacts/phase88-qemu-evidence-final/phase88-summary.txt`.

| Run | Serial SHA-256 | Injection SHA-256 | Capture SHA-256 | Timeline SHA-256 |
|---:|---|---|---|---|
| 1 | `FB31EAA4E0EC8D6AF318CD4904F82EB73A9249DB8361FE727F7322D8B61A82DC` | `0781A7ABBD7EEC0B497AF37008896BF00CD6B75CCF5E305BF214918296940463` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` | `D1FF348378A66B4DD43BB16982239927E0EC8C36DFF20824FD97EAE405CC6023` |
| 2 | `1B8483DD5125603E51DDE9B829DFF1CF6409E68A98864200E25A43C1E8D071BB` | `81C5140888EBDA0DD2FBC5FABC27989A382D7541FB9AF200903C448CC300BAD2` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` | `F71F6EC7D018CA0ED5A2E0A68DEC897CAA244FAA984A652DE5B7B28CD6439EED` |
| 3 | `1F0FA030F7BA109701F39F45B49CE033F562D03D764EEAE80368B98E68575CA6` | `1419D18E860C853567BC05A98EFFAEC0C334242B0B0561B575348430C9D5C457` | `DAA5B1877F537D57BEA2D1D2CDF1E62AF3B22B658C28E8BB957A1857AB33E03D` | `A26058143EC228E29C6534954542B545E3136014C824AB951294928DB0244755` |

Other captured hashes are recorded in each run's `phase88-summary.txt` and the root summary.

## Scope and status

No new network protocol was added. No ICMP production change, scheduler wait, timer ownership, Phase 81 TX integration, Phase 81R2, UDP, TCP, socket, DHCP, or DNS work was included. Normal E1000 TX still DD-polls. Phase 81 accepted: **No**. Phase 81R2 accepted: **No**. Phase 87 remains accepted.

Outcome B applies to the Phase 85 aggregate acceptance proof/accounting defect. The ARP negative-path product behavior was correct; no product ARP classification repair was needed. The main Phase 88 result is the restored authoritative proof with explicit bounded no-reply completion.

Recommended smallest Phase 89 objective: one focused hardware-backed validation of the next requested network boundary, selected only after review of current phase status; do not infer or begin a protocol from this report.


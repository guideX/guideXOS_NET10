# Phase 93 — Managed Service Async TX Integration

## Preflight and architecture audit (before production changes)

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`  
Branch: `nativeaot-managed-kernel-integration`  
Starting HEAD: `01a5d2e0b6aeec0133591bee27290d2e86440eb9`  
Upstream: `origin/nativeaot-managed-kernel-integration`, ahead/behind `0/0`  
Initial worktree: clean. `.phase` and `AGENTS.md` are absent.

The live Phase 92 report, `docs/superpowers/validations/2026-10-13-phase92-async-tx-endurance-reclamation.md`, closes in its R5 section with Outcome B acceptance. Its retained candidate and QAC per-boot evidence record three Phase 92 endurance boots, 10,000 hosted async lifecycles, and the accepted 90–92 E1000 ownership contract. No prior Phase 93 or equivalent service-facing async TX integration exists in the checkout.

### Existing ownership path

- `ManagedE1000Driver` owns the hardware TX ring, DMA buffers, four-slot async ledger, request identity, and driver episode. Its live signatures are `TrySubmitFrame(byte[]? frame, int length, out ulong requestId)`, `TryGetTransmitCompletion(ulong requestId)`, and `TryConsumeTransmitCompletion(ulong requestId)`. `AsyncTxCapacity` is four.
- `TrySubmitFrame` copies `frame.AsSpan(0, length)` into the selected driver-owned TX DMA buffer before publishing TDT. The caller may reuse its managed frame after an `Accepted` return; the driver retains no managed frame reference.
- `ManagedEthernetLayer` constructs Ethernet frames in its fixed `_txFrame` buffer and currently sends them through the existing synchronous `TryTransmit` path. `ManagedIpv4Layer` owns IPv4/UDP reply construction, while `ManagedNetworkServiceBackend.SendUdp` delegates application sends to that synchronous path.
- On ingress, Ethernet validates and parses the frame, IPv4 validates local delivery and protocol bounds, UDP validates its header/checksum, and `ManagedNetworkService` copies bound datagrams into one fixed 1,024-byte service receive slot. The service retains source/destination IPv4 and UDP ports but currently does not retain the validated Ethernet source MAC.
- `ManagedNetworkService.Poll()` calls the backend `Poll()`. The production backend tries one RX dispatch and treats an idle RX poll as success. This existing cooperative boundary can pump bounded TX completions before trying RX, so reclamation continues on service polls with no new packets.
- `ManagedE1000Driver.TryStop()` closes async admission, stops Ethernet, disables engines and bus mastering, and invalidates the async ledger only after DMA is quiesced. The service adapter must retire records only after that existing teardown authority has completed. A replacement Ethernet/driver episode gets a new adapter; old IDs are rejected by the new ledger and adapter membership check.

### Answers to the required ownership questions

1. `ManagedEthernetLayer` owns the current outgoing frame buffer and synchronous Ethernet handoff; `ManagedE1000Driver` owns accepted asynchronous DMA copies.
2. `ManagedIpv4Layer` builds IPv4/UDP packets. `ManagedNetworkService` owns the UDP endpoint registration and copied datagram delivery; a service-level consumer should own echo policy.
3. Yes. The async driver copies the entire submitted frame before returning. A fixed Ethernet TX buffer is safe if the adapter submits synchronously into that copy and retains only the request ID.
4. `ManagedNetworkServiceBackend.Poll()` is the existing bounded cooperative update hook. A completion pump there runs regardless of RX activity.
5. When four requests are outstanding, return Busy, increment a bounded rejection counter, and drop the diagnostic echo; do not queue or retry it.
6. The driver episode is authoritative. Teardown records may only be cleared after the existing DMA quiescence and ledger invalidation succeed. Restart binds a fresh adapter to the new driver episode.
7. The completion pump can run at the start of backend `Poll()`, before its one RX attempt; work is bounded by the four service-owned records.

### Planned narrow integration

The service adapter will own at most four accepted request records, submit complete validated Ethernet frames to the existing E1000 async API, and retain only request IDs and episode identity. A successful bounded pump will query each accepted request, record terminal success/error counters, consume it exactly once, and release its slot. It will not add a send queue or scheduler work. The UDP diagnostic consumer will use the existing bound endpoint and service receive slot. A small receive-context extension will preserve the source MAC only after Ethernet/IPv4/UDP validation, allowing the reply builder to target the validated ingress peer without introducing route or neighbor-cache changes.

### QAC preflight

The installed CLI reports QAC 2.0.0. Its supported contract is `qac run --project <name> --mode acceptance -- <qemu-system-*.exe> [arguments...]`; QAC owns exactly one direct QEMU child per reservation, so each cold boot will obtain and release its own acceptance reservation. At preflight the coordinator is online, with zero reservations, sessions, queued requests, QEMU processes, and foreign processes. No reservation has been requested.

## Implemented integration and hosted qualification

The production adapter is `ManagedNetworkServiceAsyncTxAdapter`, with a fixed capacity of four accepted request records. It stores only the driver episode and request IDs. The driver's `TrySubmitFrame` copies the complete frame into DMA-owned storage before returning `Accepted`, so the Ethernet scratch frame may be reused immediately. The adapter reports Accepted, Busy, Invalid, or Unavailable, and its bounded pump visits no more than four records at the start of the existing backend `Poll()` call. It queries completion, consumes terminal results once, records success/failure, and frees the corresponding record. Driver records are cleared only from the existing stop path after bus mastering is disabled and DMA is quiescent.

The internal service-facing call is `ManagedNetworkService.TrySendReceivedUdpEchoAsync(out ulong requestId)`. It only submits the currently owned validated, bound UDP receive slot, copies no caller reference into pending state, and clears the receive slot after the submit result. The backend seam is `IManagedNetworkServiceAsyncTxBackend.SendReceivedUdpEchoAsync(...)`, with `GetAsyncTxStatistics()`, `TryGetServiceTxCompletion(ulong)`, and `TryConsumeServiceTxCompletion(ulong)`. The adapter entry point is `TrySubmitFrame(byte[]? frame, int length, out ulong requestId)`; it also provides `PumpCompletions(int maximumRequests = 4)`, `TryGetCompletion(ulong)`, and `TryConsumeCompletion(ulong)`. Completion values are Pending, Success, HardwareError, InvalidOrStale, or AlreadyConsumed.

The opt-in UDP echo endpoint is `10.15.0.1:15930`. The responder reuses the existing validated UDP receive slot, retains the accepted ingress MAC only after local UDP delivery validation, builds IPv4/UDP/Ethernet using production builders, and submits through the adapter. The QEMU fixture sends 32 paced valid requests with distinct identifiers plus wrong-port, bad-UDP-checksum, and wrong-destination controls. The request and reply binary captures use length-prefixed Ethernet frames.

Hosted Phase 93 suite result: **PASS**, 3,705 assertions, fixed seed `0x93C0FFEE`, 1,515 service request lifecycles across capacity churn and restart transitions, five rejected submissions accounted, and zero final service/driver outstanding records. The suite exercises the production service, adapter, packet builder, and real four-slot async ledger contract; its fake transport copies into a DMA-style owned frame and observes descriptor DD before deferred TXDW dispatch. The injector's 32-request fixture validation also passes.

### Hosted regression matrix

| Gate | Result |
|---|---|
| Phase 14 E1000 host tests | PASS; includes Phase 90 frame/async request checks |
| Phase 16 network protocol host tests | PASS, 139 cases; includes UDP parsing/checksum/build tests |
| Phase 21 network service host tests | PASS, 42 cases |
| Phase 23 service client host tests | PASS, 60 cases |
| Phase 91 lifecycle host tests | PASS, 60 checks |
| Phase 92 endurance host tests | PASS, 178,774 checks; 10,000 driver lifecycles |
| Phase 93 service integration host tests | PASS, 3,705 checks; 1,515 service lifecycles |
| Managed interrupts | PASS, 36/36 assertions |
| Native interrupts | PASS |
| Driver binding | PASS |
| Network service owner/status | PASS |
| Driver worker | PASS |
| Service host | PASS |
| Host services | PASS |

The Phase 23 host project omitted `ManagedInterruptEvent.cs`, which `ManagedE1000Protocol` already references. Added the same linked source used by the Phase 21 host project; Phase 23 then compiled and passed all 60 cases. The Phase 21/23 wrapper scripts also pin SDK 10.0.400, which is absent on this host. I ran both projects directly with the bundled supported .NET 10.0.302 toolchain. The other host runners discovered .NET 10.0.402 or used the same explicit bundled SDK.

The first local binary-capture self-check found that the Phase 93 injector attempted to cast its `IncludeNegativeControls` switch value directly to an integer when producing request labels. Replaced that with an explicit bounded count. The injector then sent and persisted all 35 initial fixture frames; the runner independently parsed and validated all captured frames, including the deliberately bad UDP checksum, wrong-port, and wrong-destination controls. This was corrected before the queued QEMU run used the injector.

Runner review also found that its three injector calls did not pass the host's already-bound UDP peer socket. Added that argument at the initial, stop-boundary, and post-restart injection points before QEMU acceptance started.

The complete host-side fixture-capture self-check then persisted and independently revalidated all 37 inbound frames (35 initial fixtures, one stop-boundary request, one post-restart request), SHA-256 `51A8B17545488D3A1297FA063FAFA9E8643E3ED2F66A4DD22BC07158DE4699F5`.

## Initial candidate build identity (historical)

| Artifact | Size | SHA-256 |
|---|---:|---|
| NativeAOT source payload | 4,979,712 bytes | `53B8007D337EB19854C5133EE378FF7F0E90FB6BC560F8DE7C5A5BEF877D3C40` |
| Phase 93 staged payload | 4,979,712 bytes | `53B8007D337EB19854C5133EE378FF7F0E90FB6BC560F8DE7C5A5BEF877D3C40` |
| Normal Gate4 EFI | 746,194 bytes | `AA07FA905FE6C284989C735DFC7A07A59CCB2AF56AAEBF7F43E464AB7DD7470B` |
| Phase 93 Gate4 EFI | 749,887 bytes | `B08B0FCC8777413A0DD94DC56C317965DBB7E4329BFC0A615CF204926A68CA57` |

Source and staged payload sizes and hashes match. The QAC CLI identifies as QAC 2.0.0 and supports `qac run --project <name> --mode acceptance -- <qemu> ...`, which owns the direct QEMU child. Another project held the active QEMU reservation and one unrelated acceptance request was ahead in the queue. The first Phase 93 request (reservation `09a8a056e201455f9a68ca3bc72d5457`) queued at position 2, then was canceled by its verified owner before QEMU launch when runner review found the missing host socket argument. That attempt produced no QEMU child; its retained logs are in `artifacts/phase93-qemu-diagnostic`.

The corrected diagnostic runner requested reservation `54c22869290b47d8bb84977ca81aa50e` at `2026-10-24T18:28:33Z`. QAC 2.0.0 reported it as owner-verified and waiting behind the other project's active acceptance QEMU and one unrelated queued request. It remained queued for nearly 15 minutes with no admission. The owner then canceled this reservation through the supported QAC `cancel` command; QAC recorded `Cancelled by client`, with no grant time, process ID, or child identity. The runner exited with code 1 after QAC returned protocol exit 3 and confirmed that it had not launched a direct QEMU child. The final QAC list contains only the unrelated project's running reservation and child; the Phase 93 reservation and runner are gone. Process inspection confirmed the Phase 93 runner owner PIDs exited and only the unrelated QEMU PID 24052 remains. That QEMU was not touched. The second attempt's retained diagnostics are under `artifacts/phase93-qemu-diagnostic-attempt2`.

## Initial hardware qualification status (historical)

No Phase 93 QEMU boot was admitted or launched. Therefore this phase has no actual UDP reply capture, guest success marker, service restart hardware result, or per-boot reservation release to claim. In particular, the following required hardware items remain **not run**: three formal Phase 93 boots; 32/32 injected and host-validated UDP replies on each boot; the expected invalid-request silence check; service-level async DMA handoff and completion consumption; final service/driver/hardware drains; post-restart reply; TXDW/COM1/RX and synchronous-TX hardware checks; and Phase 14, 16, 85, 87, 89, 91, and 92 QAC-managed hardware regressions against this candidate. Hosted packet fixture self-checks are not hardware evidence.

| Hardware gate | Result |
|---|---|
| Phase 14 E1000, QAC-managed | NOT RUN — no Phase 93 reservation admitted |
| Phase 16 networking, QAC-managed | NOT RUN — no Phase 93 reservation admitted |
| Phase 85 ARP, QAC-managed | NOT RUN — no Phase 93 reservation admitted |
| Phase 87 ICMP, QAC-managed | NOT RUN — no Phase 93 reservation admitted |
| Phase 89 UDP, QAC-managed | NOT RUN — no Phase 93 reservation admitted |
| Phase 91 lifecycle, QAC-managed | NOT RUN — no Phase 93 reservation admitted |
| Phase 92 endurance, QAC-managed | NOT RUN — no Phase 93 reservation admitted |
| Phase 93 managed UDP async TX, 3 boots | NOT RUN — no Phase 93 reservation admitted |

## Initial Phase 93 closeout (historical)

**Outcome E — Environment blocker.** Hosted adapter/service qualification and candidate builds pass, but QAC did not admit the Phase 93 run within the bounded 15-minute wait. No hardware acceptance was manufactured from hosted evidence, and the work remains uncommitted. The exact hardware gates above remain incomplete.

Phase 90, Phase 91, and Phase 92 remain previously accepted. Phase 81 and Phase 81R2 remain unaccepted. Synchronous TX still polls DD. This phase adds no scheduler waits, async timeout infrastructure, or Phase 94 work. QAC 2.0.0 is online; our reservation and owner process are released; the unrelated project's managed QEMU remains active and untouched. Starting and ending HEAD are the same; the Phase 93 changes and report are preserved in the worktree, with no commit or push because the outcome is not accepted.

## Phase 93R1 hardware qualification follow-up — 2026-10-24

This follow-up preserves the prior Outcome E history above and records the later, bounded QAC attempt. The starting HEAD remained `01a5d2e0b6aeec0133591bee27290d2e86440eb9`; the branch remained `nativeaot-managed-kernel-integration`, tracking `origin/nativeaot-managed-kernel-integration` at `0/0`. The expected Phase 93 files stayed uncommitted. No reset, clean, stash, branch switch, history rewrite, or `.phase` file creation occurred.

### Candidate correction and identity

The first QAC-admitted diagnostic boot used a Gate4 image that did not enable the harness's `AssumeUnspecifiedTimezoneUtc` policy. The guest reached `TIME_INVALID_TIMEZONE` and halted before E1000 initialization, UDP injection, or Phase 93 service activity. This was a fixture build configuration defect. The existing NativeAOT payload and staged payload were unchanged; both Gate4 images were rebuilt with `-AssumeUnspecifiedTimezoneUtc` before the bounded Phase 93 admission request. No production source was changed for this correction.

| Artifact | Size | SHA-256 |
|---|---:|---|
| NativeAOT source payload | 4,979,712 bytes | `53B8007D337EB19854C5133EE378FF7F0E90FB6BC560F8DE7C5A5BEF877D3C40` |
| Phase 93 staged payload | 4,979,712 bytes | `53B8007D337EB19854C5133EE378FF7F0E90FB6BC560F8DE7C5A5BEF877D3C40` |
| Normal Gate4 EFI, UTC policy | 746,194 bytes | `CE5011C4D226251EE87D1EE57D50B187EF2ABDF4443E23C6E88E41ACCB3BBAF0` |
| Phase 93 Gate4 EFI, UTC policy | 749,887 bytes | `E54004293BF1BB96B88A2F402C2B581E80E23DDC83EAE24FD12B9B9AB3CD6290` |

The Phase 93 ESP manifest SHA-256 is `4A96A1F34AD9F4223F3EB23400B8A214DADD536DCABAD3369318473286585941`. The source/staged payload identity and all candidate hashes were rechecked after the QAC attempt. QEMU 11.0.0 and OVMF firmware hashes are recorded in `artifacts/phase93-acceptance-candidate-utc.txt`. The source input manifest is `artifacts/phase93-candidate-source-manifest.txt`, SHA-256 `CD3CE5047BD68567B0A7A172EBA8231EF98417EAD54D0F08A4EEEB2D07F124F5`.

### QAC attempts and ownership

The first admitted diagnostic run obtained reservation `e4d9a8d038dd416293815de3f99c0271`, with QAC owner PID `16752` and direct QEMU child PID `16936`. QAC detected an additional unmanaged QEMU PID `15664`, marked the reservation degraded, and the runner requested a graceful QEMU quit. This run did not reach the Phase 93 fixture; the unrelated QEMU was not terminated or modified. QAC released the reservation.

The timezone-misconfigured diagnostic run obtained reservation `a6a2bc3cadf14888907ab67dc833b307`, with QAC owner PID `14340` and direct QEMU child PID `15084`. Serial evidence ends at `TIME_INVALID_TIMEZONE`; the guest never initialized the Phase 93 network endpoint. The runner then stopped its own QEMU through the monitor and released its QAC reservation. These two diagnostic boots are retained in `artifacts/phase93-hardware-acceptance-20261024/runs/run-1` and `artifacts/phase93-hardware-acceptance-20261024-rerun1/runs/run-1`; neither is a Phase 93 acceptance boot.

For the corrected candidate, QAC 2.0.0 had another project's verified acceptance reservation `8b30e94f21e44394808624e6bf192733` running, owner PID `22776`, child PID `25196`. The single Phase 93 request was reservation `5f8b3867de034c4fb39ed76e4a8f75aa`, owner PID `26060`, requested at `2026-10-24T22:14:21Z`. It remained `Waiting` at queue position 1 with no grant time or process ID through the bounded three-minute admission window. At `2026-10-24T22:17:30Z`, only this Phase 93 reservation was canceled through QAC. `qac inspect` confirmed state `Cancelled`, reason `Cancelled by client`, `processId=null`; the runner recorded `childLaunched=false` and exited with QAC protocol exit 3. QAC then reported one managed session, zero queued requests, and zero foreign QEMU processes. The other project's VM remained active and untouched. Evidence is retained in `artifacts/phase93-hardware-acceptance-utc-20261024/runs/run-1`.

No repeated request was submitted to obtain a different queue position. The formal corrected-candidate series did not launch a VM: **Phase 93 QAC boots passed: 0/3; UDP requests injected into the guest: 0; async service replies accepted: 0; replies captured and validated: 0.** The existing fixture-only injector validation remains PASS for 32 valid packets and 3 negative controls, but it is not hardware evidence.

### Regression and final checks

The hosted regression results recorded above remain PASS; no source changed after that hosted qualification, so the full hosted suite was not rerun. QAC hardware regressions were not started because Phase 93 hardware acceptance did not pass.

| Hardware gate | Phase 93R1 result |
|---|---|
| Phase 14 E1000, QAC-managed | NOT RUN — Phase 93 acceptance was not admitted |
| Phase 16 networking, QAC-managed | NOT RUN — Phase 93 acceptance was not admitted |
| Phase 85 ARP, QAC-managed | NOT RUN — Phase 93 acceptance was not admitted |
| Phase 87 ICMP, QAC-managed | NOT RUN — Phase 93 acceptance was not admitted |
| Phase 89 UDP, QAC-managed | NOT RUN — Phase 93 acceptance was not admitted |
| Phase 91 lifecycle, QAC-managed | NOT RUN — Phase 93 acceptance was not admitted |
| Phase 92 endurance, QAC-managed | NOT RUN — Phase 93 acceptance was not admitted |
| Phase 93 managed service async TX, three boots | NOT RUN — request canceled after bounded admission wait; no QEMU child |

The candidate PowerShell scripts parsed successfully: `Build-Gate4Harness.ps1`, `Run-ManagedKernelPhase11FreshBoots.ps1`, `Inject-ManagedKernelPhase93Udp.ps1`, and `Run-ManagedKernelPhase93HostTests.ps1`. `git diff --check` passed. UDP injector validation passed. Final source/staged payload hashes match. No production feature expansion, scheduler wait, async timeout, or background networking worker was added.

### Current disposition

**Outcome E — Environment blocker.** The corrected immutable candidate is ready, but QAC did not admit its single Phase 93 acceptance reservation within the requested three-minute window. The two earlier admitted diagnostics exposed an external QEMU isolation conflict and a corrected harness timezone flag; neither produced Phase 93 network evidence. All mandatory QAC hardware regressions therefore remain incomplete. The implementation is preserved without an acceptance commit or push.

Phase 90, Phase 91, and Phase 92 remain accepted. Phase 81 and Phase 81R2 remain unaccepted. Synchronous TX continues DD polling. There are no scheduler waits or async timeouts in this phase. Phase 94 was not started. Starting and ending HEAD are both `01a5d2e0b6aeec0133591bee27290d2e86440eb9`; the Phase 93 implementation and updated report remain in the dirty worktree.

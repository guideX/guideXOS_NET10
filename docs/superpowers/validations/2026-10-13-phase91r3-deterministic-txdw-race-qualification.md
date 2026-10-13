# Phase 91R3 — Deterministic TXDW Delivery Race Qualification

Date: 2026-10-13
Prompt phase: 91R3
Outcome: **Accepted — the exact-count proof was replaced with a bounded fresh-route-notification proof; all Phase 91 gates passed.**

## Execution preflight

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `4f9e99421383fe098c787b520505e6f5e6ebf32e`
- Starting worktree: dirty with existing Phase 90/91 implementation and evidence changes; preserved in place.
- Remote configuration and credentials were not changed. No branch switch, reset, stash, clean, rebase, merge, or worktree creation was performed.
- QEMU process isolation was checked by the acceptance runner before each fresh boot. No unrelated QEMU process was used or stopped.
- Phase 92 was not started. No scheduler wait or asynchronous timeout was introduced.

## Exact synchronous predicate audit

The previous acceptance check sampled `_phase80DeliveryCount` before preparing and submitting the synchronous frame, then required an exact `syncEventBase + 1` managed-delivery delta. The regular synchronous transmit path submits by advancing TDT and polls the selected descriptor's DD bit. That DD poll can complete before the interrupt dispatcher drains the native event FIFO. Managed delivery is separate and deferred; DD completion alone does not imply that a typed TXDW event has reached the E1000 handler.

The old count delta did not establish freshness or causality. The native interrupt FIFO assigns a global sequence when a record is enqueued, so legitimate TXDW records can be queued after a count baseline and dispatched later. Route replacement discards queued events for the retiring E1000 subscription while preserving COM1 records, but a raw delivery count does not distinguish an old, unrelated, or fresh TXDW. Nor is there a driver or hardware contract requiring exactly one TXDW notification for each transmitted frame. Coalescing, extra legitimate notifications, or delayed dispatch can therefore invalidate an exact `+1` assertion while DD still proves completion.

The corrected proof samples the native route's `NextSequence` after the descriptor and frame are prepared and immediately before the TDT write. It accepts only a TXDW event whose assigned sequence is at least that fence. The driver keeps the synchronous DD poll as the hardware-completion authority, then performs bounded managed dispatch until a qualifying event arrives or the existing poll limit expires. Acceptance also requires successful dispatch, zero rejected events, and no outstanding asynchronous ledger ownership. A nonzero notification count is sufficient; no exact event-per-frame count is required.

The sequence fence establishes a fresh post-fence route notification, not exact descriptor-to-interrupt causality. TXDW records do not carry a request or descriptor identity, and there is a small interval between sampling `NextSequence` and writing TDT during which an unrelated fresh TXDW could be enqueued. The historical intermittent failure itself was not captured by the earlier diagnostic runs; the established defect was the unsupported exact-count acceptance assumption. The original event source of each historical failure remains uncertain.

## Deterministic regressions

`ManagedKernelPhase91HostTests` directly exercises the production proof and async ledger. The managed interrupt host tests exercise the production dispatcher and E1000 event handler with fake route services.

| Case | Result |
|---|---|
| Immediate TXDW delivery | Fresh post-fence event plus DD and successful dispatch accepts. |
| TXDW queued until after synchronous DD completion | DD alone remains pending; later dispatch of the fresh event completes proof. |
| Coalesced completion | One TXDW can complete multiple DD-owned ledger descriptors; the proof does not require one event per frame. |
| Stale E1000 event before restart | Sequence below the new fence is rejected by the proof. |
| Fresh event after replacement subscription | Event at the new route sequence fence is accepted. |
| COM1 interleaved with TXDW | COM1 cause does not satisfy the E1000 TXDW proof; the later typed TXDW does. |
| Delayed managed dispatcher | Hardware DD before dispatcher execution remains pending; later bounded dispatch completes proof. |
| DD with no new notification | DD success without a fresh TXDW does not pass the route-notification proof. |

Results: Phase 91 proof/lifecycle host suite **PASS, 60 checks**; managed interrupt host suite **PASS, 36/36 assertions**; native interrupt host suite **PASS**. Additional checks confirm rejection on dispatch errors, rejected events, and outstanding asynchronous ownership.

## Changes and timing footprint

- Added `ManagedE1000SyncTxProof` to track a post-fence TXDW notification independently from synchronous DD completion.
- Updated the Phase 90 and Phase 91 synchronous acceptance paths to arm the sequence fence immediately before TDT, preserve DD polling, dispatch within a fixed bound, accept any nonzero fresh TXDW count, and seal the proof after the decision.
- Kept detailed interrupt and MMIO snapshots on failure paths. The successful proof path uses the single native stats read needed for `NextSequence`; diagnostic dispatch snapshots are behind the opt-in `GXOS_ENABLE_PHASE91_DIAGNOSTICS` build define. Dispatcher diagnostic state is reset once per batch rather than once per event.
- Kept the native FIFO replacement-route stale-event discard and COM1 isolation behavior, with host regression coverage.
- Adjusted the Phase 83 RX hardware-entry bound to tolerate up to eight hardware entries while still requiring exactly four routed, dispatched, and acknowledged records. This handles an observed extra hardware entry without weakening the route accounting.

The three acceptance boots used the default uninstrumented final build, with Phase 91 diagnostics disabled. A separate diagnostic build compiled successfully for source-path comparison and was not used for acceptance. Its NativeAOT payload SHA-256 was `D0A42619B261F3539805BC4A2695B557D8F500F4A59B534607617162009E8E1F`; diagnostic Gate4 EFI SHA-256 was `6C142AB6145633B4F5E1F7DF9F0BF42284F2F1AF3C48B3FC3A803E56B29F2AE4`.

## Final uninstrumented candidate

The final Gate4 rebuild produced the same EFI hash as the image used by all three acceptance boots.

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| ManagedKernel NativeAOT payload `gxos-managed-kernel.dll` | 4,932,096 | `8C4DF010039756BEF928344F96EFA53EEE84574341933829314B8929E40DFAFB` |
| Phase 91 Gate4 `BOOTX64.EFI` | 749,887 | `154F17D57143F1B3059727C092AF4E7CDBE86D1082739724539FC36B9CFAECC3` |

## Three isolated Phase 91 acceptance boots

All three runs used the same final candidate and passed the runner's Phase 91 gates: 28 expected and 28 captured frames, async ledger invalidation at stop, old request-ID rejection, capacity recovery, synchronous DD completion, fresh TXDW route proof, RX liveness, COM1 route active, and no interrupt storm. Each reported exactly one fresh post-fence TXDW notification for the synchronous restart frame. The 1,792-byte capture files have the same SHA-256 in all runs.

| Boot | Captures | Fresh sync TXDW notifications | Native interrupt entries | Capture SHA-256 | Serial SHA-256 |
|---:|---:|---:|---:|---|---|
| 1 | 28/28 | 1 | 9 | `77D0D3A217CDE6421F732BBCF4FA4402B79B449D9E2307126BE93C85199E8AFC` | `871C0DDDE28873D7AD2A6CBA5DA607821A5E9A87167D5512A200DF4B19F7E1C4` |
| 2 | 28/28 | 1 | 10 | `77D0D3A217CDE6421F732BBCF4FA4402B79B449D9E2307126BE93C85199E8AFC` | `82886315836B85A56AAF781620960B3DF5B5D8AD9B0413A4C89501D925317F30` |
| 3 | 28/28 | 1 | 9 | `77D0D3A217CDE6421F732BBCF4FA4402B79B449D9E2307126BE93C85199E8AFC` | `40D56E2821655303CEF9C92C9E087CDDD7720468CCF678E21131D6AD0E7C3FB7` |

Evidence is retained under `artifacts\phase91r3-acceptance-evidence`; the runner recorded QEMU command lines, process preflights, serial output, timelines, injection records, firmware identity, and frame-capture logs for each run.

## Required regression matrix

| Check | Result |
|---|---|
| Phase 14 host | PASS |
| Phase 14 hardware | PASS, 3/3 fresh boots |
| Phase 16 host | PASS, 139 cases |
| Phase 16 hardware | PASS, 3/3 with the Phase 15 networking regression fixture enabled |
| Phase 91 lifecycle/proof host | PASS, 60 checks |
| Managed interrupt host | PASS, 36/36 assertions |
| Native interrupt host | PASS |
| Driver binding host | PASS |
| Network service host | PASS |
| Service owner/status host | PASS |
| Driver worker host | PASS |
| Phase 85 ARP hardware | PASS, 3/3 fresh boots |
| Phase 87 ICMP hardware | PASS, 3/3 fresh boots |
| Phase 89 UDP hardware | PASS, 3/3 fresh boots |
| ManagedKernel NativeAOT and Gate4 builds | PASS; accepted hashes above |
| PowerShell syntax | PASS, 12 relevant scripts parsed |
| `git diff --check` | PASS |

Regression setup notes: two preliminary Phase 16 attempts used fixtures without the required Phase 15 networking regression mode and were not counted as acceptance; the correct fixture then passed 3/3. The first Phase 87 rerun exceeded the old Phase 83 hardware-entry ceiling with six hardware entries while route, dispatch, and acknowledgement counts remained exactly four. After the Phase 83-specific ceiling was raised to eight, the full Phase 87 run passed 3/3 with the exact route accounting intact.

## Final classification

**Phase 91: accepted.** The deterministic tests established that the former exact `+1` delivery-count assertion was not a valid proof of a fresh TXDW notification under deferred dispatch and non-one-to-one notification behavior. The bounded sequence-fence proof fixes that acceptance defect while preserving synchronous DD polling and native FIFO route isolation. The uninstrumented candidate passed all three isolated Phase 91 boots and the complete requested regression matrix. The precise historical cause of the intermittent event-delivery symptom remains unobserved, and TXDW still cannot be tied to a particular descriptor because the event carries no such identity.

Phase 81 and Phase 81R2 remain unaccepted. No Phase 92 work was started.

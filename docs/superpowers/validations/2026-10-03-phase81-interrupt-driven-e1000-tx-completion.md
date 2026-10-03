# guideXOS C# .NET 10 — Phase 81 E1000 TX Completion Investigation

**Date:** 2026-10-03

**Outcome:** C — the existing synchronous TX ownership path cannot safely consume the typed interrupt without a scheduler-aware wait/yield mechanism.

This is a bounded investigation. Phase 81 was not accepted, and no Phase 82 work was started.

## Repository preflight

| Field | Starting state |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| HEAD | `c30d2e05bc3588647cd3db1a41f86d9678309563` — `Repair managed E1000 interrupt binding` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Divergence | 1 ahead / 0 behind |
| Worktree | Clean |

The live state matched the supplied preflight. No `AGENTS.md` was present in the repository or its parent. The Phase 79 host-harness report and Phase 80 hardware-interrupt report were read before inspection.

## Existing TX path

The normal stack is `ManagedEthernetLayer.TryTransmit` or `TryTransmitBroadcast` → `ManagedE1000Driver.TryTransmitFrame` → DMA buffer write → descriptor write → TDT update → descriptor completion polling. `TryTransmitFrame` reads the descriptor DD bit through `PollTxCompletionCoreLoop`, up to `PollLimit` (100,000) iterations, and advances `_txIndex` only after DD is observed. The ring has 16 descriptors, but this synchronous method allows one transmit to be outstanding per call.

The initial Phase 14 frame uses a separate `SubmitProofFrame`/`PollTxCompletion` path. Phase 80's typed handler is gated by `_phase80Requested`; the Phase 80 proof explicitly drains the managed dispatcher with `TryDispatchE1000InterruptBatch`. Its repeat transmissions still call the normal method above, which polls DD before the proof separately drains the resulting interrupt event. That accepted hardware proof demonstrates delivery, but does not make the event the normal TX completion mechanism.

Existing state records only `_txIndex`, `_lastSubmittedTxIndex`, and Phase 80 delivery diagnostics. There is no per-request ownership state, generation, completion result record, timeout-owned state, or unmatched-TXDW counter. The descriptor status bit is authoritative evidence in the current polling path. TXDW is not currently required for `TryTransmitFrame` success.

## Interrupt and wait boundary

The Phase 80 binding subscribes the managed E1000 event type (3) through `ManagedInterruptDispatcher`. The native route's hardware-enable callback enables IMS for TXDW (ICR bit 0); unsubscribe disables the device mask and restores the MSI route. The typed event carries the authoritative ICR cause and a monotonic sequence. The managed event queue is bounded and deferred; the managed dispatcher drains it only when a caller explicitly invokes a dispatch method.

The current interrupt-service ABI exposes subscribe, unsubscribe, drain, and stats callbacks. It has no wait, scheduler-yield, or completion-wakeup callback. `ManagedDriverWorker` dispatches when the native scheduler invokes it, and its current routing is for serial and keyboard events; it is not an E1000 completion worker. The E1000 synchronous transmit call has no way to suspend and resume through that worker.

Consequently, replacing DD polling with a loop that repeatedly calls the dispatcher would still busy-wait at full CPU while waiting for the queue. Calling the dispatcher once and returning cannot report the final synchronous TX result. No safe integration was made on this turn.

## Required behavior not yet implemented

The Phase 81 code change remains blocked on a scheduler-aware bounded wait/wakeup path for the existing synchronous caller. Once available, the transmit lifecycle should arm one descriptor before TDT, consume only a valid typed TXDW event, and use DD as the per-descriptor completion authority. It should retain a timed-out descriptor until eventual DD or device reset, drain queued prior events before slot reuse, and release each request once. Event processing should scan every bounded outstanding descriptor if the contract later permits more than one.

Current Phase 80 behavior does not establish several Phase 81 guarantees: it correlates an event to `_lastSubmittedTxIndex` rather than a request ledger; it does not scan a bounded outstanding window; it has no explicit duplicate-release or generation protection; and a timeout does not create a failed-but-still-owned request state. Cause bits accompanying TXDW are preserved, but a cause without TXDW does not classify as a TX completion. The current event handler is not a normal-path spurious-event policy.

## Validation run

These checks ran against the unchanged production tree. They validate the accepted foundation; they do not validate Phase 81 TX integration.

| Check | Result |
|---|---|
| `Run-ManagedKernelInterruptHostTests.ps1` | Pass — SDK discovery, source completeness (84 production sources / 85 host compile items / 0 duplicates), 15/15 assertions |
| `Run-ManagedKernelInterruptNativeHostTests.ps1` | Pass |
| `Run-ManagedKernelPhase14HostTests.ps1` | Pass — 33/33 assertions |
| `Run-ManagedKernelDriverBindingHostTests.ps1` | Pass |
| `Run-ManagedKernelDriverServiceOwnerHostTests.ps1` | Pass — service owner and status suites |
| `Run-ManagedKernelServiceHostTests.ps1` | Pass |
| `Run-ManagedKernelDriverWorkerHostTests.ps1` | Pass — repeated wake cycles and existing dispatch totals |
| `Build-ManagedKernel.ps1` | Pass — SDK 10.0.401; NativeAOT payload built |
| `Build-Gate4Harness.ps1` | Pass |
| QEMU Phase 81 boots | Not attempted; the existing Phase 80 fixture does not exercise normal event-driven completion |
| SyntheticScheduler | Not run; no scheduler source changed |

No Phase 81-specific host tests were added because there is no production ledger/wait implementation to test. Phase 80's prior QEMU evidence remains the accepted 3/3 hardware-delivery proof in [the Phase 80 report](2026-10-03-phase80-hardware-backed-managed-e1000-interrupt.md); it is not counted as a Phase 81 boot.

## Evidence hashes

Generated build outputs are ignored by repository policy and are not committed.

| Artifact | SHA-256 |
|---|---|
| `artifacts/managed-kernel/publish/gxos-managed-kernel.dll` | `9FE746D9D5346C059EA6A87B4838EBAF85846EB2E55FC18D43B48ADAD12A6F30` |
| `artifacts/gate4/ESP/EFI/BOOT/BOOTX64.EFI` | `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` |
| `artifacts/managed-kernel-interrupt-host-tests/bin/ManagedKernelInterruptHostTests.dll` | `DCE199079F7DEA774C55F0C6F8E25E2417A80FA9ED36947CE0565E3FC12742F6` |
| `artifacts/managed-kernel-phase14-host-tests/bin/ManagedKernelPhase14HostTests.dll` | `63A3DA6FE48E6122FBBB7677959A18CAD9CEEE732C16799C18F9D2907B9604E6` |

## Scope and next step

No production source, test source, harness, remote, credential, Git identity, branch, worktree, or history was changed. Only this investigation report is added. The narrow prerequisite is a scheduler-aware, bounded wait/wakeup facility that can safely resume the current synchronous TX caller after the existing managed interrupt dispatcher observes the queued event. Then the same one-descriptor request can be integrated and tested without introducing a general asynchronous networking framework.

**Phase 81 integrates real TXDW delivery into normal transmit completion; it does not add RX networking, DHCP, IP, or physical NIC support.** This criterion is not yet satisfied. Phase 82 is not recommended until Phase 81's wait/wakeup blocker is resolved and Phase 81 is accepted.

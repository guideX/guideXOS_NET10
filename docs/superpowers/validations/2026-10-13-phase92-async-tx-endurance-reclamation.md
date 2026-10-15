# Phase 92 — Asynchronous E1000 TX Endurance and Reclamation

**Outcome: E — environment blocker. Phase 92 is not accepted.**

The deterministic hosted endurance qualification passed. The managed source and both Gate4 C fixtures compile. A final NativeAOT payload could not be produced because this machine has no Windows native linker (link.exe), so the Phase 92 Gate4 image could not be built from the current source. No Phase 92 QEMU boot or host packet capture was started. The worktree is preserved and uncommitted.

## Starting repository state

- Repository: D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration.
- Branch: nativeaot-managed-kernel-integration.
- Starting HEAD: fea0d44477be4787399d8ba1f7ec50f02c6cea57, matching the requested expected HEAD.
- Upstream: origin/nativeaot-managed-kernel-integration; starting state was ahead 1, behind 0.
- Worktree was clean at preflight. It is now dirty with the Phase 92 fixture and tests; no existing changes were reset or overwritten.
- Phase 91 was already committed and accepted. Its qualification is [2026-10-13-phase91r3-deterministic-txdw-race-qualification.md](2026-10-13-phase91r3-deterministic-txdw-race-qualification.md).
- No .phase file or AGENTS.md was present. Existing PowerShell host-test and Gate4 conventions were followed.
- QEMU inventory was empty before work. The final inventory is recorded at artifacts/phase92-build/qemu-inventory-no-acceptance-boots.txt; no QEMU process or acceptance boot was started during this phase.

## Ownership-model audit

The production implementation in ManagedE1000Driver and ManagedE1000AsyncTxLedger retains separate physical descriptor and logical request ownership:

1. A free physical descriptor is usable only when its ledger ownership is Free; the async ledger has four fixed logical slots while the TX ring has sixteen descriptors.
2. Submission prepares a ledger slot with generation and driver-episode identity, writes the DMA buffer and descriptor, marks ownership HardwareOwned, then publishes by advancing TDT. A failed final TDT publication rolls the prepared ledger state back.
3. Hardware owns the descriptor after TDT publication. A request remains outstanding even if a descriptor’s DD bit changes.
4. DD is the completion authority. A bounded TXDW-driven descriptor scan may process several DD descriptors; TXDW is only a possibly coalesced notification.
5. A DD result is published to the ledger as Completed, but the request slot and descriptor are still occupied until explicit consumption.
6. Completion query is nonblocking and does not consume. Successful or hardware-error completion is released by one explicit consume; a second consume sees a stale identity.
7. Reuse requires both hardware completion and consumption. Request generation and driver-episode identity prevent a reused slot or restarted driver from aliasing an old request.
8. Teardown closes admission, removes the interrupt route, quiesces/reset hardware and bus mastering, and only then invalidates remaining requests. Phase 91 route-specific stale-event containment remains in force.

Resources can remain occupied while the descriptor is hardware-owned, while DD has completed but the request has not been consumed, or while teardown has not yet established DMA quiescence. The Phase 92 tests do not treat DD completion as logical reclamation. Synchronous TX continues to use its existing DD polling contract. No scheduler wait, wakeup, asynchronous timeout, background completion thread, or new pending queue was introduced.

## Hosted endurance results

The dedicated host suite is src/ManagedKernelPhase92HostTests/Program.cs, run by tools/Run-ManagedKernelPhase92HostTests.ps1.

- Result: MANAGED_KERNEL_PHASE92_HOST_TESTS=PASS checks=178774.
- Fixed seed: 0x92C0FFEE.
- Successful submit, DD completion, publication, single consumption, and reuse lifecycles: 10,000.
- Fifth-request Busy probes: 2,500.
- The schedule cycles through initial occupancies of one, two, three, and four; it tests partial DD completion, DD-before-dispatch, one scan processing multiple DD descriptors after a coalesced-notification model, out-of-order completion and consumption, and consuming one request while others remain hardware-owned.
- Busy probes preserve cursor, ledger occupancy and ownership, completion counts, and descriptor statuses. Request IDs are checked across repeated same-slot reuse.
- The sixteen-entry descriptor ring repeatedly wraps; the suite requires at least 600 physical wrap transitions. All sixteen ledger descriptor entries are free after the final drain.
- A focused restart-boundary case rejects old episode IDs without changing the new request’s ownership, then completes and consumes the new request. DD error completion is also consumed without leaving an occupied slot.

The host suite drives the production protocol descriptor builder and async ledger. Its DD bits are simulated host inputs; this is hosted ownership qualification, not evidence of hardware transmission.

## Phase 92 Gate4 fixture and hardware status

The existing Phase 11 Gate4 builder and fresh-boot runner now accept the exclusive EnablePhase92AsyncTxEndurance mode. It composes the Phase 90 asynchronous proof with the Phase 91 teardown/restart lifecycle and two 128-frame Phase 92 episodes. Each episode submits four requests per batch, probes the fifth request for Busy, drains and consumes, and checks bounded resource counters. The host runner expects 284 total Phase 90 signature frames per boot: 256 Phase 92 endurance frames plus the existing Phase 90/91 lifecycle frames. Four DD-complete but unconsumed requests at the teardown boundary are recorded as invalidated after DMA quiescence, separately from successful consumed requests.

The host capture path validates unique sequence identifiers, Ethernet addresses, exact 60-byte frames, signature and payload padding, and independently reparses the persisted length-prefixed capture file. It probes for late duplicates after capture and guest liveness. The exact PHASE92_ASYNC_TX_ENDURANCE_PASS=1 marker is emitted only after host capture, serial accounting, guest resource drain, and route/liveness checks succeed.

Hardware acceptance did not start:

- The staged Phase 92 NativeAOT publish compiled the managed C# sources but stopped at Microsoft.NETCore.Native.Windows.targets: Platform linker not found.
- The installed Visual Studio 18 Community tree and Windows SDK were inspected; link.exe is absent. The repository-pinned .NET SDK 10.0.302 was staged under ignored artifacts and its archive SHA-512 was verified against Microsoft release metadata. No machine-wide installation was changed.
- The Phase 92 Gate4 C loader was built as a syntax/build smoke using the accepted Phase 91 NativeAOT payload, not the current Phase 92 source. This smoke image is not an acceptance candidate and was not booted.
- Phase 92 acceptance boots: 0 of 3. Per-boot accepted transmissions and captured frames: not measured. Duplicate capture count, guest ledger drain, native Phase 92 TXDW/COM1 observations, RX liveness, and post-endurance synchronous TX: not measured.

Non-candidate C smoke artifacts:

| Smoke artifact | Bytes | SHA-256 | Embedded payload |
|---|---:|---|---|
| Normal Gate4 C smoke BOOTX64.EFI | 746,194 | AA07FA905FE6C284989C735DFC7A07A59CCB2AF56AAEBF7F43E464AB7DD7470B | Accepted Phase 91 payload, 8C4DF010039756BEF928344F96EFA53EEE84574341933829314B8929E40DFAFB |
| Phase 92 Gate4 C smoke BOOTX64.EFI | 749,887 | 73F3B545CD5A6E449BDD67D89B5A96919644461B383A44FCBE1E59D4BA6E3267 | Accepted Phase 91 payload, 8C4DF010039756BEF928344F96EFA53EEE84574341933829314B8929E40DFAFB |

No current-source Phase 92 NativeAOT payload or final Gate4 EFI hash exists. These smoke hashes must not be used as acceptance artifact identities.

## Host regression matrix

These hosted regressions passed in fresh Phase 92 evidence directories:

- Phase 14 E1000: PASS.
- Phase 16 networking: PASS, 139 cases.
- Phase 90 standalone host runner: none available; Phase 14 covers the Phase 90 frame and TX protocol helpers, and Phase 92/91 host suites cover async ledger behavior.
- Phase 91 lifecycle host suite: PASS, 60 checks.
- Phase 92 endurance host suite: PASS, 178,774 checks / 10,000 lifecycles.
- Managed interrupts: PASS, 36/36 assertions; source completeness check passed.
- Native interrupts: PASS.
- Driver binding: PASS.
- Network service (Phase 23 HTTP/service host suite): PASS, 60 cases. The existing Phase 23 project omits `ManagedInterruptEvent.cs` even though the current `ManagedE1000Protocol.cs` references that type, so its runner fails to compile. The suite was run with an isolated project under ignored `artifacts/` that included this existing source dependency; no tracked test or production file was changed for the adaptation.
- Managed host services: PASS.
- Service host: PASS.
- Service owner and status: PASS.
- Service worker: PASS.

The hardware regression matrix (Phases 14, 16, 91, 92, 85, 87, and 89) was not run because the final NativeAOT candidate could not be linked. The three-boot hardware acceptance and all per-boot packet reconciliation remain incomplete.

## Builds, checks, repairs, and remaining limits

- ManagedKernel Release C# source build with NativeAOT disabled: PASS. This verified managed compilation only; it is not a NativeAOT payload build.
- ManagedKernel NativeAOT Release build: BLOCKED at the missing Windows native linker prerequisite.
- Normal Gate4 C smoke build and Phase 92 Gate4 C smoke build: PASS with the prior Phase 91 payload, for C fixture compilation only.
- Phase 92 host harness build and execution: PASS.
- PowerShell parser validation for the modified build and runner scripts: PASS.
- git diff --check: PASS; Git only reported its existing LF-to-CRLF working-copy notices.
- Initial managed compilation found two uint array-index errors in the new fixture code; they were corrected to ring-array indices. No async ownership, DMA, completion, or synchronous-TX behavioral defect was found or repaired.
- New production-source changes add a gated Phase 92 qualification path and counters. The accepted asynchronous ownership implementation and synchronous DD-polling behavior were not changed.
- No final hardware result, host capture evidence, final NativeAOT payload, or Phase 92 candidate EFI was produced. Phase 92 is therefore not accepted.

## Final repository state

- HEAD remains fea0d44477be4787399d8ba1f7ec50f02c6cea57; branch and upstream are unchanged.
- Current ahead/behind remains 1/0. No Phase 92 commit was made and no push was attempted because the result is Outcome E.
- The worktree remains dirty with the Phase 92 fixture, host suite, runner/build updates, and this report. Generated evidence and smoke artifacts remain under artifacts/; they were not staged.

Phase 81 accepted: No. Phase 81R2 accepted: No. Scheduler waits added: No. Async timeouts introduced: No. Phase 93 started: No.

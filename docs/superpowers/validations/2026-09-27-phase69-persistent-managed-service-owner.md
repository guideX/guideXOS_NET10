# Phase 69 — Persistent Managed Service Owner

Date: 2026-09-27  
Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`  
Branch: `nativeaot-managed-kernel-integration`

## Result

**Outcome C — Phase 69 acceptance is not claimed.** The ownership implementation now distinguishes runtime ownership from attach callback success and quarantines ambiguous state. Normal attach, repeated real serial dispatch, one-shot coexistence, pending-event DRAIN, detach, and resource restoration passed. The service-specific injected attach-failure/replacement path and guest DISCARD path were not demonstrated in this run, so the full acceptance criterion remains open.

No commit or push was made. The requested Outcome A/B gate for those operations was not met. No Phase 70 work was started.

## Owner and handle

`managed_kernel_driver_service_owner.c` contains a bounded static owner slot with capacity one. The owner slot retains only its native owner context and current value handle; callers receive no TCB, runtime `Thread*`, wake-event pointer, or managed object address.

The internal service handle contains `{slot:uint16, identity:uint32, generation:uint16, device_identity:uint32}`. Slot 0 is the COM1 device identity 1. A handle is accepted only when all values match the live singleton owner. Release invalidates it; reusing the slot advances identity and generation. The owner host test passed same-slot reuse and stale-handle rejection.

The worker context owns the scheduler TCB and stack, scheduler thread handle, wake event and its public handle, interrupt route relationship, managed callback bridge, stop policy, and NativeAOT lifecycle evidence. The queue ring remains static storage in the interrupt context: IRQ handlers produce, the persistent worker consumes, and the interrupt shutdown operation clears it only for DISCARD.

Lifecycle values are `Free`, `Allocated`, `Starting`, `RuntimeAttached`, `Running`, `Waiting`, `StopRequested`, `Stopping`, `RuntimeDetached`, `Reclaimable`, `Reclaimed`, `StartFailed`, and `Quarantined`.

## Admission and coexistence

Creation reserves the owner slot and checks `gxos_scheduler_can_admit(scheduler, 1, 2)` before publishing an identity or allocating the TCB and event. The known cost is one TCB and two scheduler objects: the thread handle and wake event. The VM stack reservation is acquired by the scheduler's existing thread-creation path; this change does not add a separate VM forecast.

QEMU measured the scheduler-object budget as follows:

| State | Free objects | Used of 16 | Free TCB slots |
| --- | ---: | ---: | ---: |
| Baseline | 3 | 13 | 4 |
| Persistent service | 1 | 15 | 3 |
| Service plus one-shot worker | 0 | 16 | 2 |
| After reclaim | 3 | 13 | 4 |

The service plus one-shot `ADD_ONE(41)` returned 42. A second one-shot creation was rejected for capacity before identity allocation. The live service TCB and wake event remained valid during the one-shot operation. The persistent service is not the one-shot managed-worker API and does not consume one of its logical API slots unless the implementation deliberately shares lower-level capacity. Here, it shares scheduler TCB/object capacity only.

## Attach ownership and reclamation

The lifecycle layer records `NOT_ATTEMPTED`, `NOT_ACQUIRED`, `ACQUIRED`, or `AMBIGUOUS` from worker FLS, NativeAOT runtime-thread state, and ThreadStore membership. A failed callback return is not treated as proof that runtime ownership was absent. Evidence of acquisition follows the normal exactly-once detach path; ambiguous evidence is quarantined and is not reclaimed by pretending it detached. Pre-runtime reclaim is allowed only when the authoritative lifecycle state is `NOT_ATTEMPTED` or `NOT_ACQUIRED` and scheduler collection has zeroed the TCB.

In the passing normal QEMU boots, the worker attached once, ThreadStore count changed from 2 to 3, detach restored it to 2, and reclaim preserved 2. The Phase 56 and Phase 57 host tests passed. Dedicated Phase 56/57 guest gates were built from the current source, but their fresh-boot runners stopped before launch because another QEMU process was active; that process belonged to a different workspace and was left untouched. Therefore the service's own startup-failure injection and healthy replacement remain unproven in guest execution.

## Event and shutdown behavior

The QEMU input source was the real COM1 serial IRQ4 path. Each of three fresh boots observed the initial RX event, another RX event after managed runtime/GC activity, and a three-byte burst. The same service stayed attached while idle and dispatched later events through the same managed subsystem.

The queue capacity is 8. Worker dispatch drains up to 4 events per batch and yields after at most 4 batches. Full-queue overflow increments `dropped_count`. Shutdown DISCARD increments the separate `shutdown_discarded_count`; it does not change overflow accounting.

Normal stop closes route acceptance under the interrupt critical section, disables hardware routes, signals the wake event, drains queued records, invokes managed unsubscribe/stop, detaches NativeAOT, collects the TCB, closes its thread handle, and destroys the wake event. Each fresh QEMU boot requested DRAIN with 3 records pending and finished with 5 enqueued, 5 drained, 0 overflow drops, an empty queue, and 0 shutdown discards. Post-stop route quiescence and no later IRQ delivery were checked. This demonstrates pending-event DRAIN, not a separate stop-while-idle trial.

The native interrupt host test passed both DRAIN ordering and DISCARD clearing/accounting; its discard fixture cleared 4 queued records while leaving the drop counter unchanged. A guest DISCARD lifecycle run was not completed, so guest-level worker exit/detach after DISCARD remains open.

## Resource evidence

The guest teardown baseline immediately before service shutdown was:

- Memory-ledger live allocations: 254
- Physical bytes: `0x1C9C80`
- Committed bytes: `0x1BCC80`
- Virtual reservation bytes: `0xA000`
- VM reservations / commitments: 10 / 193
- Total reserved / committed bytes: `0xFEB8000` / `0xC1000`
- VM regions: 10

After the managed-driver and worker teardown, the guest reported 231 live allocations, `0x1B2C80` physical bytes, `0x1A5C80` committed bytes, `0x5000` virtual reservation bytes, 8 reservations, 175 commitments, `0xFEA5000` total reserved bytes, `0xAF000` total committed bytes, and 7 regions. This is the expected release of the 23-page driver arena/worker-stack footprint (18 commitments, 2 reservations, 3 regions). It is not a peak-only-service measurement.

## Validation

Passed:

- `Run-ManagedKernelDriverServiceOwnerHostTests.ps1`
- `Run-ManagedKernelInterruptNativeHostTests.ps1`
- `Run-ManagedKernelDriverWorkerHostTests.ps1`
- Phase 56, 57, 59, and 60 host rollback tests
- Phase 61 host runner, including its Phase 61/62/64/65/66 cases
- C11 `-Wall -Wextra -Werror -fsyntax-only` on the changed C owner, worker, interrupt, lifecycle, API, and scheduler sources
- Normal ManagedKernel EFI build and 3 fresh QEMU boots
- SyntheticScheduler build and 3 fresh scheduler-proof QEMU boots

The Phase 69 guest emitted the production-worker GC-survival/post-GC dispatch markers. It did not emit `MANAGED_GC_MAIN_OK=1` or `PHASE53O_PASS=1` in this configuration; those exact markers are not claimed here. The SyntheticScheduler proof establishes the independent scheduler-core path, where the persistent ManagedKernel service is not enabled.

The worker host-test runner originally hard-coded SDK 10.0.400, absent on this host. It now selects the highest installed .NET 10 SDK, matching `Build-ManagedKernel.ps1`; the runner then passed.

### Build and guest artifacts

- ManagedKernel payload: 4,790,784 bytes, SHA-256 `70978459A4BF07F21C5523CF07D78B92E2E8514D084501EB673D40E367A235E2`
- Normal EFI image: `artifacts/phase69-normal-final11/ESP/EFI/BOOT/BOOTX64.EFI`, SHA-256 `65AFBDDDC691EB87ACCB9643900D52B5A5FCC543509DFC6B55F9C4428420E882`
- Three normal boot logs: `artifacts/phase69-qemu-normal-final11/runs/run-{1,2,3}/serial.log`
- Normal serial-log SHA-256 values: run 1 `2510BFB0604878706AF07D205861DD42C9AB6F0E61C895E29BB3BBD1B40DCBD2`; run 2 `E60041D3663D66C39EBCC458B3A421F8506E07F3FE82E15FABFB6A7E4E29120E`; run 3 `A2A97BC61CC1244BAD6A8DBA971D27D32A29C416CD590C96A6E7C99E0AFADAF5`
- SyntheticScheduler EFI image: `artifacts/phase69-synthetic-build-final2/ESP/EFI/BOOT/BOOTX64.EFI`, SHA-256 `26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946`
- Three scheduler-proof logs: `artifacts/phase69-synthetic-build-final2/synthetic-runs-20260927-080625-802/run-{1,2,3}/serial.log`

## Regressions and remaining work

Host regression status: Phase 66, 65, 64, 62, and 61 passed in the common API host runner; Phases 60 and 59 passed; Phase 57 and Phase 56 passed; owner, interrupt, and worker host suites passed. Phase 54 and Phase 53O were not separately rerun as guest diagnostics in this turn. Phase 65/66 guest suites were not rebuilt or rerun in this turn.

The next bounded Phase 69 validation step is to rerun the prepared Phase 56 and Phase 57 guest gates after the unrelated QEMU process exits, then add a service-boundary fixture for injected attach failure, replacement, and guest DISCARD. Until those pass, this report does not accept Phase 69.

## Git state

Starting state was clean at `c6c6fe878de11c99d088033077c88fa7e57f0366` (`Document driver dispatch service boundary`), on `nativeaot-managed-kernel-integration`, tracking `origin/nativeaot-managed-kernel-integration`, with live starting divergence 0 ahead / 0 behind. The pasted prompt expected 2 ahead / 0 behind; live repository state was treated as authoritative. No remotes, credentials, branches, worktrees, stash, reset, rebase, history rewrite, or force-push operations were performed. No commit or push was attempted because the validation outcome is not A or B.

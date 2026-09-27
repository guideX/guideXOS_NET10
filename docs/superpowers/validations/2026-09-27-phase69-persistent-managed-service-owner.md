# Phase 69 - Persistent Managed Service Owner

Date: 2026-09-27
Repository: D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration
Branch: nativeaot-managed-kernel-integration
Starting HEAD: 8a537465023127d04936f2cb70760d58f2470c46 (Add persistent service owner)

## Result

**Outcome A - Phase 69 fully validated.** The service-boundary attach-failure cleanup, healthy replacement, stale-handle rejection, guest DISCARD, idle stop, normal pending-event DRAIN, one-shot survival across service shutdown, and final resource restoration all passed. Relevant host and guest regressions passed. No Phase 70 work was started.

The attach-failure fixture proves Case A only: the runtime attach wrapper was entered, the callback bridge was deliberately unregistered, and authoritative FLS, runtime-thread, and ThreadStore evidence showed that runtime ownership was not acquired. The service owner safely reclaimed that known pre-runtime state. This build does not invent a post-acquire/pre-running failure point; Phase 57 guest rollback and the Phase 64 host suite cover the lower-layer acquired-runtime detach behavior.

The closeout also corrected a nondeterministic proof setup. The optional-burst wait previously dispatched the runnable worker while waiting for the final IRQs, so one run could drain the queue before the stop request. For Phase 69 owner builds, the wait now leaves that worker runnable but undispatched until stop; other configurations retain their prior dispatch behavior. The final normal proof then passed 3/3 with three records pending at shutdown on every boot. An extra ThreadStore peak counter was removed because it did not represent the service attach peak; the direct 2-to-3-to-2 service markers and final baseline check remain.

## Owner and handle

The bounded static owner has capacity one. It retains its native owner context and current value handle; callers receive no TCB, runtime Thread pointer, wake-event pointer, or managed object address. The handle contains slot, identity, generation, and device identity. Slot reuse advances identity and generation, invalidating stale handles.

The worker context owns the scheduler TCB and stack, scheduler thread handle, wake event and public handle, route relationship, managed callback bridge, stop policy, and NativeAOT lifecycle evidence. The interrupt queue remains static storage: IRQ handlers produce records, the persistent worker consumes them, and shutdown DISCARD clears only records still pending.

Lifecycle evidence distinguishes NOT_ATTEMPTED, NOT_ACQUIRED, ACQUIRED, and AMBIGUOUS. A failed callback result alone does not determine ownership. Known acquired runtime state uses exactly-once detach; ambiguous state is quarantined; only known NOT_ATTEMPTED or NOT_ACQUIRED state may use pre-runtime reclaim.

## Service-boundary attach failure and replacement

The AttachFailureDiscard fixture runs through the actual persistent-service owner. It arms the failed service identity/generation and supplies a ready but unregistered callback bridge. The worker reaches the attach wrapper, which reports CALLBACK_NOT_REGISTERED before runtime Thread/FLS/ThreadStore publication.

Guest evidence:

- Failed service: slot 0, identity 1, generation 1; runtime attach attempted, ownership NOT_ACQUIRED, attach count 0, detach count 0.
- During the failed start, the service TCB, thread handle, wake event, stack, and VM resources remained owned; ThreadStore stayed at 2.
- Cleanup restored the captured owner, scheduler object/thread, ThreadStore, VM, reservation, commitment, and memory-ledger baselines. The failed handle was invalidated.
- Replacement reused slot 0 with identity 2 and generation 2. Stop through the stale failed handle was rejected without stopping or reclaiming the replacement.
- The replacement attached successfully once and dispatched later real serial IRQ events.

The injection is intentionally limited to the known pre-runtime case. The attach implementation has no authoritative guest seam between runtime ownership publication and service Running state, so this report makes no service-boundary Case B claim. Current-source Phase 57 guest rollback and Phase 64 host coverage passed for lower-layer acquired ownership and detach.

## DRAIN, DISCARD, idle stop, and one-shot coexistence

Normal DRAIN used the real COM1 serial IRQ4 route. Across three fresh boots, the same service identity/generation handled repeated events, including the post-GC event and a three-byte burst. Each boot reached stop with 3 records pending, then completed with 5 enqueued, 5 drained, 0 overflow drops, 0 shutdown discards, and an empty queue. Route acceptance closed and hardware routes were disabled before worker exit; no post-stop IRQ delivery was observed.

Guest DISCARD had 6 records queued at stop: the pending serial burst plus three bounded fixture-captured records. It discarded all 6, drained no additional records, left overflow drops at 0, kept managed dispatch count at 2 before and after stop, and ended with an empty queue. Total accounting was 8 enqueued, 2 drained, 6 shutdown-discarded, 0 dropped.

IdleStop began with an empty queue and a blocked service worker waiting on its unsignaled auto-reset event. Stop woke the waiter; the worker exited and detached once without a busy loop. The event and TCB were reclaimed and the scheduler returned to baseline.

The service plus a pending ADD_ONE(41) one-shot occupied all 16 scheduler objects. A second one-shot create was rejected for capacity before identity, TCB, or wake-event allocation. The first remained SUBMITTED after service stop and completed as 42 only after service shutdown/reclaim. Final scheduler free counts returned to 3 objects and 4 TCB slots; ThreadStore returned to its baseline count of 2. The one-shot API contract outside the Phase 69 build remains unchanged.

Normal service teardown resource comparison:

| Metric | Before service stop | After reclaim |
| --- | ---: | ---: |
| Memory-ledger live allocations | 254 | 231 |
| Physical bytes | 0x1C9C80 | 0x1B2C80 |
| Committed bytes | 0x1BCC80 | 0x1A5C80 |
| Virtual reservation bytes | 0xA000 | 0x5000 |
| VM reservations | 10 | 8 |
| VM commitments | 193 | 175 |
| Total reserved bytes | 0xFEB8000 | 0xFEA5000 |
| Total committed bytes | 0xC1000 | 0xAF000 |
| VM regions | 10 | 7 |

This is a 23-page release of the driver arena/worker-stack footprint, including 2 reservations and 3 regions. The attach-failure fixture separately measured its own startup baseline and exact restoration.

## Validation

Passed:

- ManagedKernel normal, IdleStop, and AttachFailureDiscard EFI builds from the final source and payload.
- Normal ManagedKernel guest: 3/3 fresh boots with pending-event DRAIN.
- IdleStop guest: 1/1 fresh boot.
- Service attach-failure, same-slot replacement, stale-handle rejection, and DISCARD guest: 1/1 fresh boot.
- SyntheticScheduler build and scheduler proof: 3/3 fresh boots, EXPECTED_HALT.
- Current-source Phase 56 pre-attach rollback guest: 3/3 fresh boots, 12 cycles each.
- Current-source Phase 57 post-attach rollback guest: 3/3 fresh boots, 12 cycles each.
- Host suites: Phase 56, 57, 59, and 60 rollback; service owner; native interrupt; worker; and the Phase 61 host runner covering Phases 61, 62, 64, 65, and 66.
- PowerShell parse for Build-Gate4Harness.ps1 and Run-ManagedKernelPhase10FreshBoots.ps1; final gate builds compile the current C source; git diff --check passed.

Two unrelated QEMU sessions were left running and untouched. The project private QEMU copies were used for Phase 56/57, and a private temporary QEMU copy was used for SyntheticScheduler; each runner cleaned up only the process it started. The ordinary Phase 69 image was built without the attach-failure or DISCARD fixture macros. Those hooks are enabled only by the AttachFailureDiscard fixture mode. The SyntheticScheduler proof covers shared scheduler-core behavior; it does not enable the persistent ManagedKernel service. This closeout does not claim standalone MANAGED_GC_MAIN_OK or PHASE53O_PASS markers.

The private temporary QEMU copy is outside the repository and has no active process. Its removal command was blocked by automatic review policy, so it remains at C:\Users\guideX\AppData\Local\Temp\gxos-phase69-private-qemu-4eee673f726d4788babd5cfec539b6df.

## Final build artifacts

| Artifact | Size | SHA-256 |
| --- | ---: | --- |
| ManagedKernel payload | 4,790,784 | 864478D68989FB5596C0B81B0DF24E27B37DF6C472AB96FF0811084E6ECAAFD2 |
| Normal Phase 69 EFI | 746,015 | 4E4C58F51911387C33405CBD0C82120ABA4317F217F7146A4B135E0B430B9F75 |
| IdleStop EFI | 747,039 | B8B6A7262763DAAB8AD8DDB00B652166610F64B9781D66B0C717869CF6E46A6F |
| AttachFailureDiscard EFI | 759,283 | D261F3A73622E03BB748BA555A296589CA94BEA095E534D5628C35CE3BBF45E2 |
| SyntheticScheduler EFI | 155,658 | 26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946 |
| Phase 56 rollback EFI | 576,384 | F940FA9A434AFB2C1D6A9F9AF17B965FC6791D53CE01E2D2285EA9B46D2C49EF |
| Phase 57 rollback EFI | 579,587 | A22A98F7FCB12A6B90BAEC086519BB20059D37356A7B6DE7AD33818F7B7660A2 |

## Guest evidence

All paths below are under artifacts/:

- Normal 3/3 logs: phase69-closeout-normal-boots-final3/runs/run-{1,2,3}/serial.log. SHA-256: 1448FE8AC5FF0E91FFAD21F97FF665A0319BA4E735CD54EC40E5BF5C35FA217C; 8CA8B07D269E9792AF584FA68AF666E46E52617ACB04149F84254B36B805971E; BB473DA644849F9CC7758963BD521F7680D96DF2EE1C9C6EB03C305D6E06301E.
- IdleStop log: phase69-closeout-idle-boots-final3/runs/run-1/serial.log. SHA-256 88CDA6F0E4BC44F09F50639EB2D504F21CF7339A9BA6A6BCA454CE8D4DC5B225.
- AttachFailureDiscard log: phase69-closeout-attach-discard-boots-final3/runs/run-1/serial.log. SHA-256 30A7DEA2939AFBC55334FFC4729D0A822FCADB64DA631BB01D88D86E064089F6.
- SyntheticScheduler logs: phase69-closeout-synthetic/synthetic-runs-20260927-125413-670/run-{1,2,3}/serial.log. Each SHA-256 29FCA73B1A46099CA212D1CFB9CD344A21D6AD490DDC620404108B55D59524F1.
- Phase 56 logs: phase69-closeout-phase56-boots/runs/run-{1,2,3}/serial.log.
- Phase 57 logs: phase69-closeout-phase57-boots/runs/run-{1,2,3}/serial.log.

## Git state

The live checkout began clean at 8a537465023127d04936f2cb70760d58f2470c46 on nativeaot-managed-kernel-integration, tracking origin/nativeaot-managed-kernel-integration with 0 ahead and 0 behind. The implementation, validation report, and this closeout are to be committed together under the Outcome A workflow. No reset, stash, discard, revert, branch switch, worktree creation, rebase, history rewrite, or unrelated QEMU termination was used.

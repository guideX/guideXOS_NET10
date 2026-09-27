# guideXOS C# .NET 10 — Phase 70 Bounded Persistent Service Restart

Date: 2026-09-27

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

## Outcome

**Outcome A — one bounded automatic restart validated.** A running COM1 managed service failed at a deterministic post-dispatch boundary, closed its route, discarded the old queue generation, detached and reclaimed its resources, and automatically started one replacement generation. The replacement processed a real COM1 IRQ event. Old handles were rejected, the one-shot request remained valid, scheduler and ThreadStore counts returned to baseline, and all three fresh QEMU boots passed.

**Phase 70 provides one bounded automatic restart, not a general supervisor or infinite retry mechanism.**

Phase 68's internal persistent-service boundary and Phase 69's one-slot owner remain the accepted architecture. The Phase 69 Case B limitation remains: Phase 70 does not create an ambiguous post-acquire/pre-running failure seam. Ambiguous runtime ownership remains quarantined and is not eligible for automatic restart.

## Git preflight

| Field | Live value |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `37ae1a8f015077478b7d6c54ce9d2a8bc6f17099` — `Add persistent managed service ownership` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | `0 ahead / 0 behind` against the current local tracking ref |
| Starting worktree | Clean |
| Remote access | Existing SSH authentication failed in earlier attempts; HTTPS push permission is unavailable per the accepted Phase 69 access state. No remote, credential, identity, ownership, or history workaround was made. |

The live tracking ref differed from the expected starting divergence in the Phase 70 request; the live Git state was used. A normal push was attempted after the commit and its exact result is recorded in the final task report.

Read before implementation:

- `docs/superpowers/validations/2026-09-27-phase68-driver-dispatch-service-boundary.md`
- `docs/superpowers/validations/2026-09-27-phase69-persistent-managed-service-owner.md`

## Failure class and boundary

The supported failure is `GXOS_MANAGED_KERNEL_DRIVER_RECOVERABLE_DISPATCH_FAILURE`: a bounded service-level failure returned immediately after a managed dispatch attempt, after the service has reached Running and processed a real event. It is not an attach failure, stop request, cancellation, CPU exception, stack corruption, GC failure, or device removal.

The diagnostic fixture triggers after the second managed dispatch attempt returns. The first real COM1 event had already been processed by generation 1. The forced failure macro is compiled only for `-Phase70FixtureMode RecoverableRestart`; the ordinary production-capable build does not enable this injection.

At failure, the persistent-service owner classifies the cause. It starts the one-restart budget transition only for the recoverable dispatch cause. DRAIN, DISCARD, idle stop, and Phase 69 attach failure retain their existing paths and do not spend or trigger this restart policy.

## Restart policy and generation boundary

The owner gives each service episode an automatic restart budget of one. It decrements the budget before replacement admission, and records `NOT_ATTEMPTED`, `ATTEMPTED`, `SUCCEEDED`, `FAILED`, or `EXHAUSTED`. A second recoverable failure after the successful restart cannot start another attempt. Host coverage verifies that an explicit later manual owner claim begins a new episode with budget one.

| Property | Generation 1 | Replacement |
|---|---:|---:|
| Owner slot | 0 | 0 |
| Service identity | 1 | 2 |
| Generation | 1 | 2 |
| Device identity | COM1 / 1 | COM1 / 1 |
| Runtime attach count while running | 1 | 1 |
| Runtime detach count | 1 before reclaim | 0 while replacement is running |

The old handle and old stop request both fail validation after replacement. Identity and generation advance; device identity remains COM1 / device 1. The old thread handle and wake-event handle resolve to no live scheduler objects after teardown. The replacement receives a new thread and wake event and attaches NativeAOT independently.

Replacement admission goes through the normal owner, TCB, scheduler-object, VM/stack, runtime, and route checks. No generations overlap. A failed admission/start records restart failure and does not loop. The owner model and host coverage exercise budget failure/exhaustion; an injected guest replacement-admission failure was not added because it would widen the phase fixture without changing the accepted stop/reclaim contract.

## Route, queue, and accounting policy

Failure ordering is:

1. close event acceptance and disable the configured IRQ route;
2. settle and discard queued old-generation records;
3. run the managed failure cleanup and detach the old runtime attachment once;
4. reclaim the old TCB, stack, thread handle, wake event, and owner record;
5. verify ThreadStore and scheduler capacity;
6. create and attach the replacement, publish its new route subscription, and resume event acceptance.

The interrupt ring and route/device configuration live outside the service generation. Recovery pending records use `recovery_discarded_count`; they do not increment overflow drops or normal shutdown discards. QEMU observed queue count 0 at failure on each run, so recovery-discarded was 0 in those runs. Host interrupt coverage verifies the separate counter and that recovery quiesce does not classify loss as normal shutdown DISCARD. No queued record was replayed to the replacement.

COM1 receive state and device identity are device-global and preserved. Worker dispatch counters and managed worker lifecycle state are generation-local and reset for the replacement. The replacement's first real event validates its fresh managed worker state while checking that the COM1 device identity and receive sequence state remain valid.

## Runtime and scheduler ownership evidence

Each QEMU boot recorded the old runtime attach and detach exactly once. After old-generation teardown, ThreadStore returned to 2, its scenario baseline. TCB, stack, thread handle, event handle, owner record, and service VM/arena ownership were released before the new generation was allocated. Scheduler availability after failed-generation teardown was 2 free objects and 3 free thread slots while the one-shot worker remained live; replacement admission used the remaining 2 objects and 1 thread slot. No second service slot or capacity increase was added.

The one-shot `ADD_ONE(41)` worker was submitted before failure and remained pending and valid across teardown and restart. Replacement was admitted while it occupied one scheduler object. It completed with result 42 after the replacement service stopped, matching the Phase 69 serialized runtime lifecycle. This proves admission and handle survival while the one-shot is live; it does not claim two simultaneously attached managed workers.

Scheduler capacity returned to 3 free objects and 4 free TCB slots. ThreadStore returned to 2. VM accounting returned to the teardown comparison after subtracting the measured one-shot delta and the released service/driver arena. NativeAOT GC/runtime allocations retained after the earlier GC checkpoint are included in the measured teardown baseline, so the raw process-wide VM ledger is not asserted equal to the pre-runtime Phase 9 snapshot.

Run 1 example: Phase 9 accounting baseline was 195 live pages (`0xC3`), 7 VM regions, and 8 reservations. The measured teardown baseline while the service and one-shot were live was 275 pages (`0x113`); final raw accounting after teardown was 233 pages (`0xE9`) and 7 regions. The teardown comparison passed after accounting for the one-shot and released driver/service allocations. `MANAGED_KERNEL_INTERRUPT_ACCOUNTING_RESTORED_NATIVE_OK`, final scheduler capacity, and final ThreadStore markers all passed.

## Validation

### Phase 70 guest acceptance

Three fresh ManagedKernel QEMU boots passed. Each boot proved generation 1 start and runtime attach, real COM1 IRQ4/FIFO/wake/managed dispatch, the bounded failure, route closure, old-generation reclaim, replacement attach and route publication, a real post-restart COM1 event dispatched by generation 2, stale handle and stop rejection, normal DRAIN without restart, one-shot survival, and final cleanup.

| Boot | Serial SHA-256 | Injection SHA-256 | Timeline SHA-256 |
|---|---|---|---|
| 1 | `30C35FF09B48015AE456594AF14F691E52281B65A1C313F8611DAD70DE289D00` | `7FED9387EA989485746DBA6F2B6F9BA4939DE04A5F4B90D9511F4E6C3B9F5375` | `6EBF1CA54AB7E53A176EB35E7011953BB7FA9883E688F80F8BD87AB7D250816D` |
| 2 | `0F8DD111059EFC6FE50D7DCBE7397559DC28A71362E38AC0C65F44A610E71DA1` | `C58690C0D620D7AC01A763B2EFF64D2974CA1E4B563AAB8308E102CD32FDB2A1` | `C060E38A67FFBEF3B7FB91A2DF27431D12F5B1843579BD957CB5199640D37A0C` |
| 3 | `4BF2B0233E3BED96CBDBF39A0199F9115B1D398BDD8F2F9D5944511403DE5FDE` | `FCDAEF150687D95F479807BF520230CD9AFB24EEBE6851DA65056E7AE2FD6C55` | `ED46D645DB3B1D1FFCC10E7A463A3E15CEC05EFDF19596723E699A6C77117919` |

Evidence: `artifacts/phase70-restart-qemu-20260927-final/runs/run-{1,2,3}/{serial.log,injections.log,timeline.log}`.

### Regressions and host coverage

- Phase 69 DRAIN, DISCARD, AttachFailureDiscard, and IdleStop guest fixtures each passed 3/3 fresh boots.
- Host suites passed: persistent service owner; native interrupt; managed driver worker; Phase 56 pre-attach rollback; Phase 57 post-attach rollback; Phase 58 root rollback; Phase 59 post-GC rollback; Phase 60 post-root-release rollback; and the Phase 61 runner covering Phases 61, 62, 64, 65, and 66.
- Host owner coverage includes one-budget initialization, recoverable-cause classification, budget consumption, generation/device continuity, stale-handle rejection, second-failure no-retry/exhaustion, and new manual-episode budget reset.
- Host interrupt coverage verifies failure queue accounting separately from overflow and normal shutdown DISCARD.
- SyntheticScheduler build and proof passed 3/3 fresh boots (`EXPECTED_HALT`), preserving the project scheduler proof convention. This configuration does not emit `MANAGED_GC_MAIN_OK=1` or `PHASE53O_PASS=1`; no claim is made that those markers were emitted.
- Phase 56/57/58/59/60 rollback coverage was host-side in this run. The shared scheduler/runtime lifecycle implementation was not modified; the worker uses the accepted detach/reclaim helpers.
- Phase 69 Case A attach-failure cleanup and healthy replacement passed in the current AttachFailureDiscard guest fixture and host regressions. Phase 69 Case B remains unresolved and unchanged.

### Builds and hashes

| Artifact | Size | SHA-256 |
|---|---:|---|
| Ordinary ManagedKernel payload | 4,791,808 | `3F2C49A25C4FF72C8876CED23C7C9C67EF930D3F84C8FC4013D00F6CF9D462BC` |
| Ordinary owner-enabled EFI, without Phase 70 failure injection | 753,894 | `19E673608726D8C4B5D0C75E5D546030FC117F93B639149A9254FFA472378CCB` |
| Phase 70 recoverable-restart fixture EFI | 759,695 | `A7EE2F58B04106A51FEA74174FB9B24266BE5E9711714CC50BE4E9F8C1755190` |
| SyntheticScheduler EFI | 155,658 | `26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946` |
| SyntheticScheduler managed payload | 730,112 | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |

The fixture macro is added only by `Build-Gate4Harness.ps1` when `Phase70FixtureMode` is `RecoverableRestart`. The ordinary EFI was built with the Phase 69 owner enabled and no Phase 70 fixture mode.

## Limitations and remaining boundaries

- The accepted Phase 69 Case B ownership gap remains. Automatic restart is allowed only after known attached/running state and authoritative detach; ambiguous ownership stays quarantined.
- The QEMU failure queue was empty in all three runs. Nonzero recovery-discard behavior is covered by host/model counter tests, not by a queued-at-failure guest case.
- A failed replacement-admission guest injection was not run. The owner restart failure state, budget exhaustion, and no-second-retry policy are host-tested.
- The one-shot remained pending throughout restart and completed after generation 2 stopped. Simultaneous managed execution/attachment is outside this evidence.
- Raw VM accounting includes retained NativeAOT GC/runtime pages; service, driver, scheduler, ThreadStore, and VM arena teardown checks use the measured post-GC comparison.
- No backoff, timer, repeated retry, multi-service registry, or supervisor framework was added.

The existing Phase 69 private temporary QEMU copy was not deleted or modified. It was used as the executable for the SyntheticScheduler proof; all proof output was written under the repository's `artifacts/phase70-synthetic-build-exact` directory.

## Acceptance

The bounded scenario passed 3/3 fresh boots. Each failed generation was fully torn down before replacement; the replacement had a distinct identity and generation, preserved COM1 device identity, handled a real event, rejected stale references, and stopped without triggering another restart. Scheduler objects, TCBs, ThreadStore membership, and service-owned VM allocations returned to the measured baseline. The one-shot request remained valid across the restart, and no unbounded retry path exists.

**Phase 70 is accepted as Outcome A. Do not begin Phase 71 in this validation.**

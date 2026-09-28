# guideXOS C# .NET 10 — Phase 71 Replacement-Admission Failure

Date: 2026-09-27

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

## Outcome

**Outcome A — one failed automatic replacement admission was contained.** Three final-image fresh QEMU boots proved that a running generation 1 handled a real COM1 event, failed at the existing recoverable dispatch boundary, detached and reclaimed its resources, and then received exactly one deliberately rejected replacement admission. No replacement identity or owned resource was published, the restart budget reached zero, the owner entered `RESTART_FAILED`, and the COM1 route stayed disabled. A pending `ADD_ONE(41)` completed as 42, returning scheduler and ThreadStore counts to baseline. A later explicit manual start formed a new episode as identity 2 / generation 2, reset its budget to one, and handled another real COM1 event.

**Phase 71 does not add additional automatic retries.** No production lifecycle defect was found, so no production policy repair was needed.

## Git preflight

| Field | Starting value |
|---|---|
| HEAD | `e3745321d3bf3250babf53dede4b90dec95aeb88` — `Clarify Phase 70 VM accounting evidence` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Divergence | 0 ahead / 0 behind against the live tracking ref |
| Worktree | Clean |

The live upstream tracking ref matched the starting HEAD; the prompt's historical expected divergence did not match live Git state. No remote, credentials, account identity, branch, worktree, or history were changed. No force push or other prohibited Git operation was used.

Read before editing:

- `docs/superpowers/validations/2026-09-27-phase69-persistent-managed-service-owner.md`
- `docs/superpowers/validations/2026-09-27-phase70-bounded-persistent-service-restart.md`

## Failure seam and result

The diagnostic-only `ReplacementAdmissionFailure` mode arms one single-shot check after generation 1 has started, attached its runtime, enabled the route, and drained a real COM1 event. The fixture waits for the existing Phase 70 recoverable dispatch failure, code `0xF0700001`, and lets the normal owner teardown path run. Only when the real worker initializer reaches its normal `gxos_scheduler_can_admit(scheduler, 1, 2)` decision does the fixture convert the available result into the existing bounded `CAPACITY` rejection.

At that admission point the natural scheduler result was available (`1`); 2 scheduler objects and 3 thread slots were free while the one-shot remained live. The fixture forced the decision to `CAPACITY` (`2`) before replacement identity publication or TCB, stack, handle, event, runtime, and route allocation. It does not corrupt owner state, consume unrelated scheduler capacity, or change the generation-1 startup result. The arm is consumed on this one attempt.

| Evidence | Result on all three accepted boots |
|---|---|
| Owner slot / old service | Slot 0; identity 1, generation 1 |
| Device | COM1 device identity 1 preserved |
| Generation 1 before failure | Running, runtime attached, route enabled, at least one real COM1 event dispatched |
| Recoverable failure | `0xF0700001`, after the second managed dispatch returns |
| Restart budget | 1 before the automatic attempt; 0 after it |
| Old runtime | Attach 1, detach 1; ownership marked reclaimed |
| Old resources | TCB, stack/VM, thread handle, wake event, and owner record reclaimed |
| ThreadStore after old teardown | 2, equal to scenario baseline |
| Scheduler after old teardown | 2 objects and 3 thread slots free; one-shot still occupies its relative share |
| Replacement admission | Attempted once; forced status `CAPACITY` (`2`) despite natural capacity being available |
| Replacement publication | No identity, TCB, stack, thread handle, wake event, runtime attachment, or route re-enable |
| Restart owner state | `RESTART_FAILED` (`4`); worker stopped/destroyed and current service handle empty |
| Automatic retries | None after the failed admission; attempt counter stayed at 1 and a second `restart_begin` was rejected |
| Route / device | Route stayed closed and disabled; device identity remained 1 |
| Queue at failure | 0 records; recovery discard 0; overflow and normal shutdown-discard counters unchanged |
| Old references | Old service handle, stop request, and wake-event handle all rejected/reclaimed |

The injected admission failure publishes no replacement identity and does not advance the owner's identity/generation. The later explicit manual start occurs only after the failed episode has settled and the one-shot worker has completed. That start is a caller-created new episode, identity 2 / generation 2, with budget 1 and restart state `NOT_ATTEMPTED`; it is not automatic retry number two. The manual generation re-enabled the route and dispatched another real COM1 event. Its later DRAIN stop left its one-budget episode intact and triggered no automatic restart.

## Resource evidence

The one-shot `ADD_ONE(41)` was submitted before generation 1's recoverable failure and remained valid through old-generation teardown and failed replacement admission. It returned 42 and reclaimed normally. Before it completed, the failure-boundary relative counts were 2 free scheduler objects, 3 free thread slots, and ThreadStore 2. After it completed, objects and thread slots returned to the scenario baseline of 3 and 4 free, with ThreadStore 2. The manual generation temporarily used scheduler capacity and final stop restored 3 objects and 4 thread slots free and ThreadStore 2.

The guest's Phase 9 memory comparison baseline was 195 live ledger pages, 7 VM regions, 8 reservations, physical `0x18EB40`, committed `0x183B40`, and virtual reservations `0x5000`. The highest recorded lifecycle comparison snapshot was 275 live pages with generation 1 and the one-shot alive (physical `0x1DEC80`, committed `0x1D1C80`, virtual `0xF000`, 11 reservations, 12 regions). The final pre-stop comparison after the manual event was 256 pages; final raw accounting after stop was 233 pages, physical `0x1B4C80`, committed `0x1A6C80`, virtual `0x5000`, 8 reservations, and 7 regions.

The final raw ledger retains 38 pages above the early Phase 9 baseline from the already-understood NativeAOT/runtime/GC proof footprint. The scenario's service-owned VM/stack, scheduler, owner, ThreadStore, and arena teardown checks passed against the post-runtime comparison. This report does not interpret retained process-wide runtime allocations as a service leak. No leak trend appeared across the three matching final guest logs.

## Validation

### Phase 71 fresh boots

The final Phase71 fixture EFI passed three complete fresh QEMU boots. The serial streams reported IRQ, ISR, enqueue, and drain counts of 6, with 0 drops; each completed the failed-restart containment proof, manual new episode, real post-manual COM1 dispatch, DRAIN, and final reclaim.

| Accepted run | Serial SHA-256 | Injection SHA-256 | Timeline SHA-256 |
|---|---|---|---|
| attempt 6 / run 1 | `C0D6C6286FDE864EAF4ECBB7BDF037F3E8DF190C1CD0BC63EAEB39DD6597ED6F` | `6CD9213B2C3C18DC35F4FBF890DD6909236AF59EDBD526FB4205669CB909314C` | `5F747ADC58CAB72ECF0C2FD638A1160AEDEE5213A3A871C3CE89F295AF7564C4` |
| attempt 14 / run 1 | `9A4F6E5461EE8AF96F01035CB6688CE3A8314B22FAF333724BAAE5D464FA5F7F` | `C436F9F9F5657F618E3C6A3D7D4F2C22529FA39F02B68FC7E4F70400E9C48227` | `8AA2A543BAB8F6678A815921E1DF530048769D26C772084C2E87C63B3F4D3642` |
| attempt 15 / run 1 | `65E209C1BA522AC794C1814DE4BD52B2DA2C6A5BC47CB1C4F4E923F7B062549D` | `3978ABD6171E2DA8AB346BD374CAF2F3146BF61808BFD0A0843BDFB66B1EF02D` | `E1C7940F3ACB8DA0BFD6431BFC5A35CCB53C3DB5CE648F64466CCD3DD0A800A5` |

Evidence paths are under `artifacts/phase71-final-qemu/attempt-{6,14,15}/runs/run-1/`. There were 15 fixture boot attempts to collect the three accepted boots: 12 stopped before the scenario because the runner's first raw serial byte did not produce a guest IRQ; those serial logs showed zero IRQ, enqueue, and dispatch counts. The QEMU runner cleaned up only the QEMU processes it started. Those startup failures were not counted as lifecycle passes or product failures.

### Regressions and host coverage

- Phase 70 successful automatic restart: three complete fresh boots passed. The first 3-run invocation completed two boots before a third first-byte startup failure; a separate fresh boot completed the set. Generation 1 → generation 2, real post-restart COM1 event, stale-reference rejection, one-shot survival, and normal DRAIN all passed. Evidence serial logs: `artifacts/phase71-phase70-regression-qemu/runs/run-{1,2}/serial.log` and `artifacts/phase71-phase70-regression-extra/attempt-1/runs/run-1/serial.log`.
- Phase 69 normal DRAIN: covered in Phase 71 manual episode and Phase 70 successful restart regression.
- Phase 69 DISCARD plus attach-failure Case A and healthy replacement: guest passed 1/1 at `artifacts/phase71-attach-discard-qemu/attempt-1/runs/run-1/serial.log`; six pending records were discarded with distinct overflow and recovery accounting.
- Phase 69 idle stop: guest passed 1/1 at `artifacts/phase71-idle-stop-qemu/attempt-1/runs/run-1/serial.log`; wake was delivered, the worker exited, and no busy loop was observed.
- Persistent owner host tests passed, including failed replacement state, consumed budget, no second automatic restart, stale old handle, and manual new-episode reset.
- Managed driver worker host tests passed.
- Native interrupt host tests passed. Phase 58 managed-root rollback host tests passed.
- `Run-Phase61ManagedWorkerApiHostTests.ps1` passed Phase 61, 62, 64, 65, and 66 host coverage.
- Phase 56, 57, 59, and 60 rollback host tests passed.
- Phase 56–60 guest rollback suites were not rerun; their shared scheduler/runtime lifecycle code was not changed in this phase.
- The separate `Run-ManagedKernelInterruptHostTests.ps1` runner could not execute: it hard-codes an absent .NET SDK 10.0.400 MSBuild path. The repository pins 10.0.302 while only SDK 10.0.401 is installed. A direct 10.0.401 MSBuild attempt restored but its standalone host-test project failed compilation because it omits current linked `ManagedEntropyService`, `ManagedSecureRandom`, and `ManagedE1000Driver` source files. The native interrupt and relevant owner/driver host suites passed.

### Production and SyntheticScheduler builds

| Artifact | Size | SHA-256 |
|---|---:|---|
| ManagedKernel payload | 4,791,808 | `3F2C49A25C4FF72C8876CED23C7C9C67EF930D3F84C8FC4013D00F6CF9D462BC` |
| Ordinary owner-enabled EFI, no Phase71 fixture | 753,894 | `19E673608726D8C4B5D0C75E5D546030FC117F93B639149A9254FFA472378CCB` |
| Phase71 admission-failure EFI | 763,279 | `E76386411212095413D35EBB22C9EE1B96E14FDE52C24547E81464E999B8BF67` |
| SyntheticScheduler EFI | 155,658 | `26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946` |
| SyntheticScheduler payload | 730,112 | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |
| Phase70 successful-restart fixture EFI | 759,695 | `A7EE2F58B04106A51FEA74174FB9B24266BE5E9711714CC50BE4E9F8C1755190` |
| Phase69 attach/discard fixture EFI | 767,162 | `A1D9D07CEA96F875987BF592EC8010365433DCF0AB2B803443759D0574702DCE` |
| Phase69 idle-stop fixture EFI | 754,918 | `A4030B59FE1314D0367480C2C0F91F0ED88F8C95D0353AD1D327E483731F8B9C` |

The final ordinary EFI was rebuilt after cleanup. A binary string check confirmed that `PHASE71_ADMISSION_FAILURE` is absent. The Phase71 hook is compiled only by the explicit diagnostic fixture build mode. SyntheticScheduler proof passed 3/3 fresh boots with `EXPECTED_HALT`; each 4,137-byte serial log has SHA-256 `29FCA73B1A46099CA212D1CFB9CD344A21D6AD490DDC620404108B55D59524F1` under `artifacts/phase71-synthetic-build/synthetic-runs-20260927-180540-411/run-{1,2,3}/serial.log`.

## Changed files

- `src/Gate4Harness/gate4_loader.c` — Phase71 guest assertions and explicit manual new-episode path.
- `src/Gate4Harness/managed_kernel_driver_worker.c` and `.h` — one-shot diagnostic admission rejection and evidence fields, macro-gated.
- `src/Gate4Harness/tests/managed_kernel_driver_service_owner_tests.c` — failed-attempt/budget/manual-episode owner coverage.
- `tools/Build-Gate4Harness.ps1` — Phase71 fixture mode and macro selection.
- `tools/Run-ManagedKernelPhase10FreshBoots.ps1` — Phase71 markers, event flow, and count assertions. Marker matching accepts the runner's zero-padded 64-bit hex fields.
- `docs/superpowers/validations/2026-09-27-phase71-persistent-service-restart-admission-failure.md` — this report.

Generated firmware and QEMU evidence remain ignored under `artifacts/`.

## Limitations and next step

Phase 69's accepted ambiguous-runtime-ownership Case B remains quarantined and unchanged. The authoritative failure queue was empty in all three accepted Phase71 boots, so recovery-discard count was zero; separate host accounting coverage keeps recovery discards distinct from overflow and normal shutdown DISCARD. This phase does not add backoff, retry loops, timers, capacity, or supervisor behavior.

The smallest Phase 72 step is to review whether the existing `RESTART_FAILED` terminal state needs an operator-facing observability/explicit restart entry point beyond the diagnostic manual-start proof. Do not infer permission to implement that step from this Phase71 report.

# guideXOS .NET 10 NativeAOT Managed Worker Ownership — Phase 54

Status: Outcome A — second managed worker and ownership ledger validated.

Date: 2026-09-20

## Scope and live starting state

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

Starting HEAD: `deb67ad59900769d11cd3a41332690428ec9ea90` — `Revalidate NativeAOT managed-kernel Phase 53 closure`

The live repository matched the requested path, branch, HEAD, clean worktree, and subject. The live upstream was already at the starting HEAD, so the actual starting divergence was `0 ahead / 0 behind`, rather than the prompt's expected `1 ahead / 0 behind`. No remote, credential, SSH, branch, topology, reset, rebase, stash, or history repair was performed.

Phase 53 closure documentation was present at `docs/superpowers/validations/2026-09-20-phase53-final-acceptance-closure.md` and recorded Outcome A.

## Existing worker-A architecture

Phase 53O's scheduler-thread lifecycle is the authoritative existing worker path. It creates a suspended scheduler TCB, reserves a stack with a dedicated guard page, installs the scheduler-owned GS/TEB/TLS environment, prepares the lifecycle record, resumes the TCB, and enters `phase53o_worker`. The first managed callback uses the existing NativeAOT callback bridge to attach the runtime `Thread*`; a second managed callback exercises GC; the worker then runs the proven FLS cleanup detach path, becomes terminated and reclaimable, closes its handle, is collected, and is returned to the reusable scheduler slot.

The production managed driver worker uses the same lifecycle abstraction. Phase 54 extends that existing record and hooks its runnable/running and reclaim transitions; it does not create a parallel worker runtime.

## Phase 54 worker-B creation path

Each bounded cycle creates two suspended scheduler TCBs so the new worker can be compared against a simultaneously live peer:

1. Worker A follows the existing Phase 53 lifecycle.
2. Worker B is created by the same scheduler `create_suspended_thread` path but receives its own TCB, scheduler handle, stack reservation, GS/TEB/TLS environment, lifecycle record, runtime attachment, managed callback, GC callback, detach, and reclaim path.
3. Both workers are resumed and dispatched independently.
4. Each invokes a proven managed callback input, invokes the proven GC probe, verifies post-GC continuation, detaches, and returns a result.
5. The main scheduler verifies termination, marks each ledger reclaimable, closes handles, collects the scheduler, marks each ledger reclaimed, checks stale handle lookup, and verifies baseline restoration.

Worker B is therefore not a second callback on worker A; it has an independent scheduler identity, generation, slot, stack, runtime thread, TLS block/vector, allocation context, managed-root evidence, and teardown record.

## Ownership ledger

The ledger is an extension of `GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE`, not duplicate metadata. It contains:

- worker identity, generation, and scheduler slot;
- bounded ownership state, transition count, rejected-transition count, and history;
- scheduler, stack, environment, TLS/FLS, runtime-thread, managed-object, callback-registration, and VM ownership flags;
- stack reservation, guard, usable low/high bounds, saved RSP, GS/TEB, TLS vector/block, guard/usable VM identities;
- existing runtime `Thread*`, allocation-context, attach, detach, callback, and GC evidence.

The legal state sequence is:

`Free → Allocated → Runnable → Running → RuntimeAttached → DetachPending → RuntimeDetached → Reclaimable → Reclaimed`

The transition helper accepts only those adjacent edges. Invalid detach/reclaim requests during construction, and duplicate teardown requests after reclaim, are rejected and counted. Reuse is legal only after `Reclaimed`; the next generation is then required to differ from the prior generation while the slot remains reusable.

## Shared and worker-local state

Process-global state is the scheduler storage, VM accounting substrate, callback bridge registration, managed image, and shared runtime/GC bridge objects. Runtime-global state includes the main NativeAOT `Thread*`, the shared ThreadStore, the GC service, and the single runtime FLS slot. Scheduler-global state includes the runnable queue, TCB array, handle table, and generation allocator.

Worker-local state is the TCB, slot/generation identity, reservation/guard/usable stack records, saved RSP, GS/TEB, TLS vector/block, runtime `Thread*`, worker allocation context, managed-root/object evidence, and lifecycle ledger. Lifecycle-local state is the bounded state history, ownership flags, transition counters, and stale-generation checks. The shared FLS slot is intentional; its per-worker value and cleanup are worker-specific.

## Validation results

The final Phase 54 gate ran 3 fresh QEMU boots. Each boot completed 12 worker-A/worker-B cycles; final acceptance therefore covered 36 attempted and 36 passed cycles.

Resource baseline and peak from every final boot:

| Resource | Baseline | Peak | After reclaim |
| --- | ---: | ---: | ---: |
| VM regions | `0x3` | `0x7` | `0x3` |
| live scheduler threads | `0x2` | `0x4` | `0x2` |
| live scheduler objects | `0xD` | `0xF` | `0xD` |

The durable main/runtime resources are included in the baseline. The two temporary workers add two live TCBs and two live objects, and each stack's guard/usable records account for the four temporary VM regions. No monotonic growth was observed.

The final serial evidence recorded 12 identity, generation, slot, state, transition, and RSP records for each worker. In run 1, worker A identities advanced `0x7 → 0x1D`, worker B identities `0x8 → 0x1E`; A remained in slot `0x2`, B in slot `0x3`; generations advanced every cycle; both final ledger states were `0x8` (`Reclaimed`) with eight legal transitions. The aggregate markers proved worker independence, stack/RSP bounds, runtime-thread independence, allocation-context independence, root survival, slot ledger validity, invalid-transition rejection, and duplicate-teardown rejection.

The managed callback total was `0x1C` (28), including the existing main-thread and Phase 53O calls plus the 24 Phase 54 worker calls. `MANAGED_GC_MAIN_OK=1` was present. Worker A and B each reached managed entry 12 times, reached post-GC continuation 12 times, detached 12 times, reclaimed 12 times, and restored the baseline 12 times per boot.

## Regression and build contract

Passed:

- `Run-SchedulerModelTests.ps1` — 256 checks;
- `Run-SchedulerDurabilityHostTests.ps1`;
- `Run-SchedulerStackVmHostTests.ps1`;
- `Run-NativeAotGcProbeContractTests.ps1` — 8 checks;
- `Run-NativeAotCallbackBridgeHostTests.ps1`, including no-external-references;
- `Run-VMSubstrateHostTests.ps1`;
- `Run-ManagedKernelBootResourceHostTests.ps1`;
- current-source Phase 53O scheduler/context lifecycle regression — 3/3 boots;
- current-source guard regression — 1/1 owned boot, with `PHASE53V_GUARD_FAULT=1`, `PHASE53V_GUARD_NONPRESENT=1`, `PHASE53V_GUARD_GS_TEB_INTACT=1`, and `PHASE53V_GUARD_UNRELATED_STATE_INTACT=1`;
- current-source Phase 53X callback reclamation regression — 1/1 owned boot, with managed-thread reclaim, slot reuse, and GC-worker-return markers;
- `Run-Phase53WReadableRangeBuildTests.ps1` — Normal and SyntheticScheduler;
- final normal NativeAOT managed-worker-ownership build;
- final SyntheticScheduler build.

The final normal gate EFI hash was `7D29906A14D10F6B33A9B5EC8E387EA76DB07E2E4A793260023BCD082DC70630`. The authoritative staged managed payload was 730,112 bytes with SHA-256 `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`. The final SyntheticScheduler EFI hash was `0255209EDCD0276250D76D5917ECB4874F26897F0D7E0A8ECD435D84F96AA175`.

Final Phase 54 serial run hashes, all 940,785 bytes:

- run 1: `423B56E53B0F9B4352C8A339420853306E2D96795AE8C84F02983E4E22D68F45`;
- run 2: `DF872FDC27390344FE6AA1C07CCF425F73AB20030E9422DD0ECD8C84DAA3BE1D`;
- run 3: `ADC1A78489B275D4156EDA9EBC942A264BA884CC1B5B3B45F7262BB78B512DE6`.

## Bounded implementation repairs

No pre-existing Phase 53 ownership defect was found. During implementation, two bounded Phase 54 defects were caught and repaired:

1. The first draft duplicated the Phase 53O probe record immediately after a managed lifecycle. On the freestanding x64 build, the compiler kept the duplicate callback/log function pointers in callee-saved XMM registers across the first managed call, and the second record could enter through a corrupted log callback. The root cause was redundant Phase 54 probe metadata; the repair reuses the already-authoritative Phase 53O probe record.
2. Invalid detach did not initially increment the ledger's rejected-transition counter, so the controlled invalid-transition fixture could not prove both rejects. The detach guard now counts the rejected request without changing scheduler state.

The new code does not suppress scheduler, stack, GS, TLS/FLS, runtime, or reclamation assertions.

## Limitations and next step

Phase 54 still provides only one bounded additional managed worker proof. It does not provide a general managed threading API, `System.Threading.Thread`, a thread pool, `Task`, async/await scheduling, work stealing, arbitrary application-created threads, reflection, dynamic loading, broad exception propagation, APC, or COM support.

The smallest Phase 55 step should be a design-only review of the now-explicit worker ownership contract and failure-injection surface; do not broaden to a general thread API until that contract is reviewed.

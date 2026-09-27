# guideXOS .NET 10 — Phase 66 Post-GC Managed-Worker Cancellation

Date: 2026-09-26
Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
Branch: `nativeaot-managed-kernel-integration`

## Outcome

**Outcome B — post-GC cooperative cancellation validated after a narrow stale-handle API repair.**

The production API now supports two cooperative `GC_CHECK` cancellation checkpoints. A caller request made after checkpoint 1 stays sticky through GC and root-survival validation, and is consumed at checkpoint 2 before ordinary post-GC continuation. The canceled worker releases its own root, detaches, terminates, and is reclaimed through the existing lifecycle. Three fresh boots passed all 36 requested scenarios; Phase 65 early cancellation remains green.

**Phase 66 still does not provide asynchronous/preemptive cancellation.** It does not interrupt GC, inject exceptions, hijack a context, abort a runtime thread, or interrupt arbitrary managed instructions.

## Git preflight

| Field | Live value |
|---|---|
| Starting HEAD | `66e187ee29aa0a309f42279387589743c07ca475` |
| Starting subject | `Add bounded managed worker cancellation` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | `0 ahead / 0 behind` |
| Starting worktree | Clean |
| Prompt's expected divergence | `1 ahead / 0 behind` |

The branch, starting HEAD, subject, upstream, and clean worktree matched the request. Live Git state differed only in the expected divergence: it was even with upstream, not one commit ahead. No remote, credential, branch, worktree, or history settings were changed.

During validation, the shared branch advanced to `39c05c62cb352732676c959a249942869267e342` (`Phase 32`, authored as Leon Aiossa); that commit contains the Phase 66 implementation/build/runner changes and is already the upstream tip. Its appearance is a live-workspace change, not a commit created by this validation. The commit subject is preserved; no amend, reset, rebase, or other history rewrite is used. This validation record is committed separately because the implementation commit is already upstream.

## API contract and checkpoints

The caller API remains `gxos_nativeaot_managed_worker_api_request_cancel(api, handle)`. Handles carry scheduler slot, worker identity, and generation. The internal `gxos_nativeaot_managed_worker_api_consume_cancel_checkpoint(record, checkpoint_id)` accepts checkpoint IDs 1 and 2 only.

| Checkpoint | Boundary | Request behavior |
|---|---|---|
| 1 | Managed root published; before GC | A sticky request is consumed here and GC count stays zero. With no request, checkpoint 1 is marked passed and the request window remains open. |
| 2 | GC complete; logical root survival validated; before ordinary continuation | A request pending since checkpoint 1 is consumed here. The worker skips normal continuation and enters ordinary root-release/detach cleanup. |

Before checkpoint 1, requests are accepted and sticky. Between checkpoints 1 and 2, requests are accepted and sticky, including while GC is running; GC is never interrupted. Checkpoint 2 closes the window. Requests after it return `CANCEL_UNSUPPORTED_STATE` (`18`). There is no checkpoint 3. A completed worker remains completed; cancellation cannot relabel its result.

For the Phase 66 B target, the deterministic fixture proves checkpoint 1 was passed with no pending request (`B_GC_BEFORE_OBSERVE=1`, `B_CONTINUATION_BEFORE_CANCEL=0`, and `cancel_requested==0` at the post-GC hold). It then calls the public request API while B is parked after GC/root validation. The first request returns `OK` (`0`); the duplicate returns `CANCEL_ALREADY_REQUESTED` (`15`).

Other observed statuses: invalid handle `5`, wrong generation `6`, stale B cancel `6`, stale B poll/result access `6`, unsupported `ADD_ONE` cancellation `18`, post-checkpoint C cancellation `18`, cancellation after normal completion `16`, and cancellation after close `17`.

## B/C/D identity, results, and cleanup

Representative first-scenario values from the final clean boot:

| Item | Value |
|---|---|
| B handle | scheduler slot `3`, identity `8`, generation `3` |
| B scheduler TCB | `0x1A2F40` |
| B runtime `Thread*` | `0x52D2030` |
| B logical managed-root token | `0x6708` |
| C logical managed-root token | `0x6809` |
| D replacement | slot `3`, identity `10`, generation `4` |

B and C roots were simultaneously owned at the post-GC cancellation point: root ledger `2`. B had completed exactly one GC, passed the root-survival callback, retained its valid logical root token, and had not run ordinary continuation. The proof uses managed root tokens and the managed root abstraction; native code does not retain or dereference a raw managed pointer. Physical GC relocation was not measured and is not claimed.

The B completion is distinct from successful `GC_CHECK`: state `CANCELED`, result status `14`, operation ID `GC_CHECK`, and original B identity/generation. Poll/close result assertions require `output0`, `output1`, and `managed_result` to be zero. B's GC succeeded, but its operation did not complete successfully.

B root release count was exactly `1`; a duplicate release was rejected. The root ledger went from `2` to `1`, and C's token remained owned and valid. B detach count was exactly `1`, FLS was clear, and its scheduler TCB was terminated. After close/reclaim, B's API slot was available and resource counts fell to VM `7`, scheduler threads `4`, scheduler objects `15`, API workers `2`, root ledger `1`.

A remained a valid healthy peer and completed its `ADD_ONE` result. C retained its runtime thread/root through B cleanup, then completed one GC, one root-survival validation, one ordinary post-GC continuation, one root release, and one detach. A stale B token cleanup was rejected while C's root remained live. Scheduler/resource counts returned to baseline after all peers and D were reclaimed. Runtime detach/FLS/ThreadStore lifecycle invariants are checked by the lifecycle implementation; this fixture does not emit a standalone numeric ThreadStore counter.

After B reclaim, D reused B's slot with a new identity and generation. Stale B submit, cancel, poll/result access, and stale-root cleanup are rejected; the runner explicitly checks stale cancel and poll statuses are both `6`. D's `ADD_ONE` completes successfully. API worker accounting is `0 → 3 → 2 → 3 → 0`.

## Resource accounting

Values are from the first final clean Phase 66 boot; the runner requires final baseline restoration on every boot.

| Snapshot | VM regions | Scheduler threads | Scheduler objects | API workers | Root ledger |
|---|---:|---:|---:|---:|---:|
| Baseline | 3 | 2 | 13 | 0 | 0 |
| A/B/C live, B+C roots live | 9 | 5 | 16 | 3 | 2 |
| B post-GC/root survived; cancel requested | 9 | 5 | 16 | 3 | 2 |
| B canceled/root released, before reclaim | 9 | 5 | 16 | 3 | 1 |
| B reclaimed | 7 | 4 | 15 | 2 | 1 |
| D replacement created | 9 | 5 | 16 | 3 | 1 |
| Peak | 9 | 5 | 16 | 3 | 2 |
| Final | 3 | 2 | 13 | 0 | 0 |

Root accounting is `0 → 2 → 1 → 0`. No leak trend was observed: VM, scheduler thread/object, API worker, and root counts returned to baseline in all three boots.

## Deterministic guest acceptance

The final boot image was rebuilt after adding an explicit stale-poll serial marker and runner assertion. The runner uses isolated OVMF variable copies and an exact-path private QEMU executable; it did not stop or alter the unrelated installed-QEMU process.

- `tools/Run-Phase66PostGcManagedWorkerCancellationFreshBoots.ps1`: **3 fresh boots passed; 12/12 scenarios per boot; 36/36 canceled B workers**.
- Ordering patterns A, B, and C each ran four times per boot, without sleeps or timing races.
- Every boot required the Phase 66 pass marker, all key B/C lifecycle and status assertions, root/API accounting, same-slot generation change, stale B cancel/poll rejection, and baseline restoration.
- `MANAGED_GC_MAIN_OK=1` and `PHASE53O_PASS=1` were required and present.

Evidence: `artifacts/phase66-final-accepted-3boots/runs/run-1..3/serial.log`.

| Boot | Serial SHA-256 |
|---|---|
| 1 | `45D072551899FD16E2156B3E0EEB7F8E780A8BC7815C2D72605821485ACBD3B1` |
| 2 | `31767E6211C96A4AFF0DB7EDB51D7975A4B6EAA5CF04A24D3ED06BDE738891A9` |
| 3 | `EC349BF0548DFF5DB4798560AA185BDC2CCA2A18E2B34D4E23A7490EF5E2F409` |

## Host and guest regressions

`tools/Run-Phase61ManagedWorkerApiHostTests.ps1` passed all five markers: Phase 61, 62, 64, 65, and 66. Phase 66 host cases cover sticky requests, checkpoint 1 not consuming an absent request, checkpoint 2 consuming a pending request, rejection of checkpoint 3, canceled result state/payload rules, duplicate and stale requests, late/completed behavior, and capacity recovery. Host tests do not prove an actual GC.

| Coverage | Result and evidence |
|---|---|
| Phase 65 early/pre-GC cancellation | 3 fresh boots, 12 scenarios each; 36/36 canceled workers; B GC count `0`. `artifacts/phase65-normal-accepted-3boots/runs/run-1..3/serial.log`. |
| Phase 64 capacity-three ordinary completion | 3 fresh boots; 12 scenarios per boot. `artifacts/phase64-regression-final-3boots/runs/run-1..3/serial.log`. |
| Phase 62 two-worker concurrency | 3 fresh boots; 12 pairs per boot. `artifacts/phase62-regression-final2-3boots/runs/run-1..3/serial.log`. |
| Phase 61 sequential API | 3 fresh boots; 12 cycles per boot. `artifacts/phase61-regression-final2-3boots/runs/run-1..3/serial.log`. |
| Phase 60 post-root-release/pre-detach rollback | 3 fresh boots; 12 cycles per boot. `artifacts/phase60-regression-final-3boots/runs/run-1..3/serial.log`. |
| Phase 59 post-GC/root-survival rollback | 3 fresh boots; 12 cycles per boot. `artifacts/phase59-regression-final-3boots/runs/run-1..3/serial.log`. |
| Phase 58, 57, 56 guest contracts | 3 fresh boots each; respective `PHASE58_PASS`, `PHASE57_PASS`, and `PHASE56_PASS` markers. Evidence: `artifacts/phase58-evidence-final-r3`, `artifacts/phase57-evidence-final`, and `artifacts/phase56-evidence-final`. |
| Phase 56–60 host failure-seam suites | `Run-Phase56FailureInjectionHostTests.ps1`, `Run-Phase57PostAttachRollbackHostTests.ps1`, `Run-Phase58ManagedRootRollbackHostTests.ps1`, `Run-Phase59PostGcRollbackHostTests.ps1`, and `Run-Phase60PostRootReleaseRollbackHostTests.ps1` passed. |
| Phase 54 ownership | 3 fresh boots; 12 cycles each. `artifacts/phase54-regression-final2-3boots/runs/run-1..3/serial.log`; authoritative payload SHA-256 `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`. |
| Phase 53O / managed GC | `MANAGED_GC_MAIN_OK=1` and `PHASE53O_PASS=1` present in Phase 66 and Phase 54 guest runs. |

Normal build passed at `artifacts/phase66-normal-build`. SyntheticScheduler build passed at `artifacts/phase66-synthetic-build`, followed by three SyntheticScheduler guest proofs with expected halt using `tools/Run-SyntheticSchedulerProof.ps1`.

## Repair notes and changed files

- Added the second bounded cancellation checkpoint after managed GC and root-survival validation. Checkpoint coordination lives in the deterministic fixture; cancellation is still requested through the production API and consumed by the same worker cleanup lifecycle.
- Tightened active-handle validation so a handle aimed at a slot occupied by another generation/identity reports `STALE_HANDLE`, including poll and cancellation after D reuses B's slot.
- Corrected the Phase 65 wrong-generation test to mutate the Phase 65 target handle itself.
- Moved the Phase 65 healthy C hold until after root validation and successful post-GC continuation, preserving the accepted peer proof.
- Kept D's request in persistent fixture storage because a stack-local request did not remain stable across scheduler context switches.
- Diagnostic failure injection is not used to trigger cancellation. Phase 66 uses checkpoint hold/release fields in the fixture and an ordinary caller request; the production cancellation path is independent of the Phase 56–60 failure trigger.

Changed files:

- `src/Gate4Harness/gate4_loader.c`
- `src/Gate4Harness/nativeaot_managed_worker_api.c`
- `src/Gate4Harness/nativeaot_managed_worker_api.h`
- `src/Gate4Harness/nativeaot_managed_worker_api_internal.h`
- `src/Gate4Harness/tests/phase61_managed_worker_api_host_tests.c`
- `tools/Build-Gate4Harness.ps1`
- `tools/Run-SyntheticSchedulerProof.ps1`
- `tools/Run-Phase66PostGcManagedWorkerCancellationFreshBoots.ps1`
- `docs/superpowers/validations/2026-09-26-phase66-post-gc-managed-worker-cancellation.md`

No temporary debug instrumentation remains. Build/evidence artifacts are ignored under `artifacts/` and are not part of the source change.

## Build hashes and limitations

| Artifact | SHA-256 |
|---|---|
| Final Phase 66 `BOOTX64.EFI` | `DA039130D590FBD47E49EDF56A850A11A6EA6F02C0D701A2F0DBD486D8F0F87A` |
| Normal `BOOTX64.EFI` | `660A6D7277C5BE37BD012702AA9560B66EBBCC7817433E6976CD2AC3CEF87F8F` |
| SyntheticScheduler `BOOTX64.EFI` | `F6D26C97D4DAA1FC4188C8DCCC531460091C421C8F3198648191BE7401187568` |
| Phase 66 staged managed payload | `D818513EA4C8308CFFF7C1F570C2744762DBC3225B3E18D3733F0ED0DE5DA7F0` |
| Synthetic proof payload | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |
| Private QEMU executable | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |

Cancellation remains limited to `GC_CHECK` at the two documented cooperative points. No arbitrary instruction interruption, cancellation during GC, or asynchronous/preemptive mechanism is supported. The smallest Phase 67 step should be to wire the proven request/result contract into one real caller while keeping the two checkpoint boundaries unchanged.

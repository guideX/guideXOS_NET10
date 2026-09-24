# guideXOS .NET 10 — Phase 59 Post-GC Worker Failure Rollback

Date: 2026-09-23
Outcome: **Outcome A — post-GC rollback validated**

## Repository state

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `057c7d9249f9b7ef8d41bc78de44f7fa6f81de33`
- Starting subject: `Clarify Phase 58 resource timeline`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: `0 ahead / 0 behind` (the prompt expected `2 ahead / 0 behind`; live Git state was authoritative)
- Starting worktree: clean
- Ending worktree before commit: modified only by the Phase 59 source, test, runner, and this report

No credentials, remotes, branches, worktrees, or existing history were changed. An already-running QEMU process was left untouched. Fresh boots used a private copied QEMU executable/share because the selected system QEMU process was already in use.

## Exact boundary

The Phase 59 executable hook is:

`GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_GC_ROOT_SURVIVAL`

It is gated by:

`GXOS_ENABLE_PHASE59_POSTGC_ROLLBACK`

The hook uses the existing Phase 56 single-shot injection record and is selected exclusively with Phase 56, 57, and 58. The worker sequence is:

```text
prepare → resume → attach → publish managed static root
→ validate root → one real ManagedGcProbe collection
→ validate the same logical root token
→ arm/fire Phase 59
→ suppress ordinary post-GC continuation
→ managed root release → runtime detach → terminate → reclaim
```

The post-GC proof includes a small diagnostic managed `ManagedRootValidate` call. The hook runs after that proof and before the ordinary successful post-GC continuation workload. The failed worker never emits the normal continuation marker; replacement workers do.

`ManagedRootValidate` checks the managed static `Phase58ManagedRoot` reference and token. It does not expose a managed pointer, use a `GCHandle`, or transfer root ownership to native code.

## Root identity and relocation

- Managed root authority: `ManagedEntry.s_phase58ManagedRoot` (`Phase58ManagedRoot`).
- Native root representation: bounded logical token only.
- Representative failed worker slot: `0x2`.
- Representative failed identity/generation: identity `0x7`, generation `0x4`.
- Representative root token: `0x5A01`.
- Pre-GC logical root identity: `0x5A01`.
- Post-GC logical root identity: `0x5A01`.
- Root publication count: one per failed worker and one per replacement; 24 total in the final run.
- Root release count: one per failed worker and one per replacement; 24 total in the final run.
- Physical relocation: **not measured**. No compaction claim is made.
- Raw managed address retained natively: `0`.

Survival is proven by the real managed GC result plus the post-GC managed validation returning the expected token and by the lifecycle root-ownership ledger. A pre-GC address is never dereferenced or used for cleanup. The stale pre-GC identity test is structural and rejects the stale token/lifecycle combination.

## Failed-worker evidence

Representative values from `artifacts/phase59-evidence-final4/runs/run-1/serial.log`:

- Runtime `Thread*`: `0x00000000052F6030`.
- FLS after attach: `0x00000000052F6030`.
- FLS after detach: `0`.
- Runtime attach count: `1`.
- Runtime detach count: `1`.
- Allocation context before GC: `0x00000000052F6038`.
- Observable post-GC allocation cursor: pointer `0`, limit `0`.
- Allocation context after root release: `0x00000000052F6038`; pointer `0`, limit `0`.
- Allocation context after detach: `0x00000000052F6038`; pointer `0`, limit `0`.
- GC count: exactly one per failed worker.
- Root survival: proven before injection.
- Normal post-GC continuation: suppressed.
- Phase 59 injection: exactly one per failed worker.
- Root release: exactly one per failed worker.
- Duplicate root release: rejected once per failed worker.
- Wrong-generation root release: rejected.
- Runtime detach: exactly once per failed worker.
- FLS/runtime cleanup: passed; runtime state became detached and FLS was cleared.
- ThreadStore: restored to the baseline count before reclaim.
- Scheduler termination: passed.
- Scheduler reclaim: passed.

The nonzero allocation-context value is a diagnostic cursor associated with the worker TLS block. It is not treated as a live ownership edge. Detach authority is FLS clearing, detached runtime state, ThreadStore restoration, lifecycle state, and the exactly-once detach count, consistent with the accepted Phase 58 managed-allocation rule.

## Resource timeline

Scheduler-object counts are reported separately from managed-heap state.

| State | VM regions | live threads/TCBs | scheduler objects | root ledger |
|---|---:|---:|---:|---:|
| Baseline | `0x3` | `0x2` | `0xD` | `0` |
| Prepared | `0x5` | `0x3` | `0xE` | `0` |
| Runtime attached | `0x5` | `0x3` | `0xE` | `0` |
| Root published | `0x5` | `0x3` | `0xE` | `1` |
| Post-GC/root survived | `0x5` | `0x3` | `0xE` | `1` |
| Root released | `0x5` | `0x3` | `0xE` | `0` |
| Final reclaimed | `0x3` | `0x2` | `0xD` | `0` |

Peak resource markers were VM `0x5`, threads `0x3`, objects `0xE`. No monotonic leak trend was observed. The managed heap was not inferred from the scheduler-object census.

## Stale-state and replacement proof

After each failed worker was reclaimed, the following were rejected:

- stale handle,
- stale worker identity,
- stale generation,
- stale root cleanup,
- stale detach,
- stale reclaim,
- stale pre-GC object identity.

The replacement reused slot `0x2` with a new identity and generation. In cycle 1 the replacement was identity `0x8`, generation `0x5`, with root token `0x5B01`; subsequent cycles advanced the identity/generation. Replacement workers published a root, completed one GC, proved root survival, completed normal post-GC continuation, released the root, detached once, reclaimed, and restored baseline.

## Repeated validation

- Failure cycles per boot: 12.
- Fresh QEMU boots: 3.
- Failed lifecycles attempted/passed: `36 / 36`.
- Failed GC/root-survival proofs: `36 / 36`.
- Failed root releases: `36 / 36` exactly once.
- Failed detaches: `36 / 36` exactly once.
- Replacement lifecycles: `36 / 36`.
- Replacement normal post-GC continuations: `36 / 36`.
- Baseline restorations: `36 / 36`.
- Leak trend: none.
- `MANAGED_GC_MAIN_OK=1`: present.

Final Phase 59 harness/payload:

- EFI SHA-256: `1F963C5926E49842575820000C27548914DC447B56F8A0BE2D56004A2FE78712`.
- Managed payload: 732160 bytes.
- Managed payload SHA-256: `2E25F807194864CF667FA2B10D72D608247518D3C4A2C02DF691A9D7FCB16FCC`.
- Serial SHA-256 values for runs 1–3: `ECEAED648A84EB984B84F6E2192BA8565A2859C97D15E6D73E90B74DAC434363`, `2224769313D16AE5F7F1F73AC36D7924CA337C10DE09C598164FDCCA4441A788`, `F51B6F343127795EB575AD1861982E4717BC06FCBDBEC173CA3E21065C46888A`.

## Regression isolation

- Phase 58 post-root/pre-GC: 3/3 fresh boots ×12 cycles; root published, GC not entered by failed workers, one release, one detach, reclaim, baseline restored, replacement success.
- Phase 57 post-attach/pre-root: 3/3 fresh boots ×12 cycles; attach once, no root publication, detach once, baseline restored, replacement success.
- Phase 56 pre-attach: 3/3 fresh boots ×12 cycles; no attach/root/detach, close → discard → collect, baseline restored.
- Phase 54 ordinary successful lifecycle: 3/3 fresh boots ×12 cycles; GC, root survival, post-GC continuation, detach, reclaim, baseline restoration.
- Phase 53 relevant scheduler/thread lifecycle: 3/3 fresh boots; scheduler/context, stack/VM, runtime lifecycle, GC/root, reclaim/reuse, and `MANAGED_GC_MAIN_OK=1` passed.
- Normal build: passed; EFI SHA-256 `C7A24B70208ACFCA386A88698FE6D5361F4DF5CE68952A6B6EFFF4587B0E0843`.
- SyntheticScheduler build: passed; EFI SHA-256 `0255209EDCD0276250D76D5917ECB4874F26897F0D7E0A8ECD435D84F96AA175`.
- Hook isolation: normal build has no diagnostic injection; Phase 56/57/58/59 are compile-time mutually exclusive; the Phase 59 image reports only the post-GC/root-survival point; SyntheticScheduler remains supported.

## Host coverage

Host tests passed for Phase 59 hook selection, single-shot behavior, incomplete-state rejection, token/generation matching, duplicate cleanup rejection, stale-generation rejection, and post-GC root ownership. Existing Phase 56, 57, and 58 host tests also passed.

## Files changed

- `src/Gate4Harness/gate4_loader.c`
- `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.c`
- `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.h`
- `src/Gate4Harness/tests/phase59_postgc_rollback_host_tests.c`
- `src/ManagedEntryProbe/ManagedEntry.cs`
- `tools/Build-Gate4Harness.ps1`
- `tools/Run-Phase59PostGcRollbackFreshBoots.ps1`
- `tools/Run-Phase59PostGcRollbackHostTests.ps1`
- `docs/superpowers/validations/2026-09-23-phase59-post-gc-worker-failure-rollback.md`

## Commands and evidence

Executed the focused C compile, Phase 59/56/57/58 host runners, the Phase 59 three-boot runner, Phase 56/57/58/54 regression runners, the Phase 53 lifecycle runner, and normal/SyntheticScheduler builds. Primary evidence is under:

- `artifacts/phase59-evidence-final4/`
- `artifacts/phase59-harness-final4/`
- `artifacts/phase59-regression-phase56-evidence/`
- `artifacts/phase59-regression-phase57-evidence/`
- `artifacts/phase59-regression-phase58-evidence/`
- `artifacts/phase59-regression-phase54-evidence/`
- `artifacts/phase59-regression-phase53-evidence/`

No production ownership defect was found. Therefore no production repair beyond the bounded Phase 59 diagnostic/evidence path was required.

## Limitations and next step

This phase still covers one failed worker at a time, one deterministic GC per worker, and the existing managed static-root abstraction. It does not measure physical object relocation and does not generalize managed roots, exception propagation, async scheduling, thread pools, or arbitrary managed-thread APIs.

Phase 59 is accepted. Do not begin Phase 60. If a future phase is approved, the smallest next boundary would be a separately gated failure immediately after root release and before runtime detach, retaining the same single-worker and single-GC constraints.

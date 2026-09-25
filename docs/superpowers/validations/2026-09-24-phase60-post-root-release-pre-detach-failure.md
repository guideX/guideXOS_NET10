# guideXOS .NET 10 — Phase 60 Post-Root-Release / Pre-Detach Failure

Date: 2026-09-24
Outcome: **Outcome A — post-root-release/pre-detach rollback validated**

## Repository state

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `ffb6001a0abfa93e42772ddf8c532975fdcad91b`
- Starting subject: `Validate managed worker post-GC rollback`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: `0 ahead / 0 behind` (live Git state was authoritative; the request expected `1 ahead / 0 behind`)
- Starting worktree: clean
- Ending commit: recorded by the final Git state for this validation unit

No remotes, credentials, branches, worktrees, or pre-existing history were changed. The already-running system QEMU processes were left untouched. Fresh boots used `artifacts\phase57-qemu-private\qemu-phase57-private.exe` and its adjacent OVMF share.

## Exact boundary

Phase 60 adds one compile-time-gated diagnostic point:

```text
GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT_RELEASE
GXOS_ENABLE_PHASE60_POSTROOTRELEASE_ROLLBACK
```

The failed-worker sequence is:

```text
prepare → resume → runtime attach → managed-root publication
→ GC → root-survival validation → managed-root release
→ root ledger zero / root ownership ended
→ Phase 60 injects → runtime/FLS detach → terminate → reclaim
```

The release and detach operations are separate observable lifecycle steps. `gxos_nativeaot_scheduler_worker_note_managed_root_released` records the successful managed release and clears native root ownership. The existing `gxos_nativeaot_scheduler_worker_detach` path then refuses an owned or unbalanced root, performs the established FLS cleanup callback exactly once, and clears runtime ownership. Phase 60 injects between those two operations.

The defining ordering is therefore:

**root ownership ended before runtime ownership ended.**

The hook is diagnostic-build only, deterministic, single-shot, generation-aware, allocation-free, and mutually exclusive with Phase 56, 57, 58, and 59. No managed feature, concurrent failure path, root API, GC design, or thread-pool behavior was added.

## Representative failed-worker evidence

Values below are from `artifacts\phase60-evidence-final\runs\run-1\serial.log`; the runner checked the same invariants in all three fresh boots.

| Field | Evidence |
|---|---|
| Failed slot | `0x2` |
| Failed identity / generation | `0x7` / `0x4` |
| Runtime `Thread*` | `0x00000000052F6030` |
| FLS after attach | `0x00000000052F6030` |
| FLS at injection | `0x00000000052F6030` (nonzero) |
| FLS after detach | `0x0` |
| Allocation context before GC | `0x00000000052F6038` |
| Allocation context after root release | `0x00000000052F6038` |
| Allocation context after detach | `0x00000000052F6038` |
| Observable allocation cursor after GC/release/detach | pointer `0`, limit `0` |
| Failed root token | `0x5A01` |
| Pre-GC / post-GC logical root identity | `0x5A01` / `0x5A01` |
| Root publication count | `1` |
| Failed-worker GC count | `1` |
| Root survival | proven before injection |
| Root release count at injection | `1` |
| Root ledger at injection | `0` |
| Runtime attach count at injection | `1` |
| Runtime detach count at injection | `0` |
| Runtime state at injection | attached |

The nonzero allocation-context value is retained diagnostic evidence under the accepted Phase 58/59 interpretation. Detach authority remains FLS clearing, detached runtime state, ThreadStore restoration, lifecycle/generation state, and the exactly-once detach count; no stricter cursor rule was introduced.

## Cleanup and negative assertions

After the intentional failure, the real remaining cleanup path proved:

- duplicate managed-root release rejected once per failed worker;
- root release count remained one and root ownership remained ended;
- wrong-generation root release rejected;
- runtime detach count became exactly one;
- FLS and worker-local runtime state cleared;
- ThreadStore/runtime attachment baseline restored;
- scheduler termination and reclaim completed;
- duplicate detach rejected;
- stale handle, identity, generation, root, pre-GC object identity, detach, and reclaim operations rejected;
- no root was republished during detach or teardown;
- no double handle close, stack free, VM release, or reclaim was accepted;
- reclaim while runtime ownership remained active was rejected by lifecycle validation.

The failed worker's root token was retained only for bounded structural negative checks. No raw managed address was retained or dereferenced. The replacement uses a distinct root token sequence (`0x5B01` for cycle 1) and cannot be affected by stale failed-worker cleanup.

## Resource timeline

Scheduler-object counts are not managed-heap counts. Runtime attachment and root ledger are shown separately because their ordering is the proof under test.

| Checkpoint | VM regions | scheduler threads/TCBs | scheduler objects | runtime attachments | root ledger |
|---|---:|---:|---:|---:|---:|
| Baseline | `0x3` | `0x2` | `0xD` | `0` | `0` |
| Prepared | `0x5` | `0x3` | `0xE` | `0` | `0` |
| Runtime attached | `0x5` | `0x3` | `0xE` | `1` | `0` |
| Root published | `0x5` | `0x3` | `0xE` | `1` | `1` |
| Post-GC/root survived | `0x5` | `0x3` | `0xE` | `1` | `1` |
| Root released | `0x5` | `0x3` | `0xE` | `1` | `0` |
| Phase 60 injection | `0x5` | `0x3` | `0xE` | `1` | `0` |
| Runtime detached / final reclaim | `0x3` | `0x2` | `0xD` | `0` | `0` |

The failed-worker handle/TCB/lifecycle record remains live through injection, then terminates and is reclaimed. The final numeric handle census is represented by the lifecycle state and reclaim marker; no separate process-handle table count is exposed by this harness.

## Replacement and ABA safety

After every failed worker, the replacement reused slot `0x2` with a new identity and generation. In cycle 1 the replacement identity/generation were `0x8` / `0x5`; the replacement runtime attach and detach counts were each `1`. The replacement published its distinct root, completed managed callback and GC, proved root survival, completed normal post-GC continuation, released its root, detached once, reclaimed, and restored baseline.

The serial markers proved same-slot reuse plus rejection of stale handle, identity, generation, root, pre-GC object identity, detach, and reclaim operations. Across the representative run, failed identities advanced `0x7, 0x9, ...`, while replacement identities advanced `0x8, 0xA, ...`; generations advanced with every allocation.

## Repeated validation

- Failure cycles per boot: `12`.
- Fresh QEMU boots: `3`.
- Failed-worker lifecycles attempted/passed: `36 / 36`.
- Failed GC/root-survival proofs: `36 / 36`.
- Failed root releases: `36 / 36`, exactly once.
- Phase 60 injections: `36 / 36`, exactly once.
- Failed detaches: `36 / 36`, exactly once after injection.
- Replacement lifecycles: `36 / 36`.
- Replacement normal post-GC continuations: `36 / 36`.
- Baseline restorations: `36 / 36`.
- Peak resources: VM `0x5`, threads `0x3`, scheduler objects `0xE`.
- `PHASE60_LEAK_TREND_NONE=1`.
- `MANAGED_GC_MAIN_OK=1`: present.

Final per-boot counters were stable:

```text
PHASE60_INJECTED_FAILURE_CYCLES=0xC
PHASE60_PASSED_FAILURE_CYCLES=0xC
PHASE60_FAILED_ROOT_RELEASES=0xC
PHASE60_DUPLICATE_ROOT_RELEASE_REJECTIONS=0xC
PHASE60_ROOT_PUBLICATION_TOTAL=0x18
PHASE60_ROOT_RELEASE_TOTAL=0x18
PHASE60_EXACTLY_ONE_ROOT_RELEASE=1
PHASE60_EXACTLY_ONE_DETACH=1
PHASE60_NO_DOUBLE_CLEANUP=1
PHASE60_GENERATION_SAFE_SLOT_REUSE=1
PHASE60_COMPLETE=1
PHASE60_PASS=1
```

## Regression and isolation results

- Phase 59 post-GC/pre-root-release regression: `3 / 3` fresh boots × `12` cycles; root remained live at injection, then released once and detached once. Replayed with `Run-Phase59PostGcRollbackFreshBoots.ps1`.
- Phase 58 post-root/pre-GC regression: `3 / 3` fresh boots × `12` cycles; root published, failed worker did not GC, release/detach/reclaim passed. Replayed with `Run-Phase58ManagedRootRollbackFreshBoots.ps1`.
- Phase 57 post-attach regression: `3 / 3` fresh boots × `12` cycles; runtime attached, no root publication, detach/reclaim passed. Replayed with `Run-Phase57PostAttachRollbackFreshBoots.ps1`.
- Phase 56 pre-attach regression: `3 / 3` fresh boots × `12` cycles; no runtime attach, root, or detach; close → discard → collect passed. Replayed with `Run-Phase56PreAttachWorkerFailureFreshBoots.ps1`.
- Phase 54 successful ownership lifecycle: `3 / 3` fresh boots × `12` cycles; managed root, GC, root survival, post-GC continuation, release, detach, reclaim, and baseline restoration passed. Replayed with `Run-Phase54ManagedWorkerOwnershipFreshBoots.ps1`.
- Phase 53 scheduler/context, runtime lifecycle, stack/VM, reclaim/reuse, GC/root, and `MANAGED_GC_MAIN_OK` contracts remained covered by the authoritative Phase 53/54/59 fresh-boot evidence. Phase 60 changes are behind the new compile-time gate and do not alter those production paths.

Hook isolation is compile-time and runner-visible: the normal image has no Phase 56–60 injection gate; Phase 56–59 regression images report only their own injection point; Phase 60 reports only `AFTER_MANAGED_ROOT_RELEASE`; and the build script rejects more than one Phase 56–60 switch. The current normal and SyntheticScheduler images were built without any diagnostic switch. The repository’s established SyntheticScheduler proof remains green; the current SyntheticScheduler image also compiled and linked successfully.

## Host coverage

The Phase 60 host test passed legal intermediate-state coverage, single-shot selection, duplicate root-release rejection, duplicate detach rejection, stale-generation rejection, and invalid earlier injection-point rejection. Existing Phase 56, 57, 58, and 59 host tests also passed. These tests validate lifecycle bookkeeping only; guest QEMU/NativeAOT evidence remains authoritative for real GC, FLS cleanup, ThreadStore restoration, and runtime detach.

## Production change discipline

No production cleanup defect was found. The existing separation between managed-root release bookkeeping and runtime detach was sufficient. No repair to root APIs, GC, or runtime cleanup was made.

The implementation adds only the bounded Phase 60 diagnostic/evidence path: one failure enum, a compile-time-gated lifecycle probe, loader/build wiring, a fresh-boot runner, deterministic host tests, and this report. The ordinary Phase 56–59 paths remain unchanged when the Phase 60 macro is absent.

## Files changed

- `src/Gate4Harness/gate4_loader.c`
- `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.c`
- `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.h`
- `src/Gate4Harness/tests/phase60_postrootrelease_rollback_host_tests.c`
- `tools/Build-Gate4Harness.ps1`
- `tools/Run-Phase60PostRootReleaseRollbackFreshBoots.ps1`
- `tools/Run-Phase60PostRootReleaseRollbackHostTests.ps1`
- `docs/superpowers/validations/2026-09-24-phase60-post-root-release-pre-detach-failure.md`

## Commands and evidence

Focused and build checks executed:

- PowerShell parser checks for the updated build and runner scripts.
- `Run-Phase56FailureInjectionHostTests.ps1`.
- `Run-Phase57PostAttachRollbackHostTests.ps1`.
- `Run-Phase58ManagedRootRollbackHostTests.ps1`.
- `Run-Phase59PostGcRollbackHostTests.ps1`.
- `Run-Phase60PostRootReleaseRollbackHostTests.ps1`.
- Phase 60 full UEFI build with `NativeAotEventWait`, startup, managed callback, scheduler callback, managed GC probe, scheduler lifecycle, and `-EnablePhase60PostRootReleaseRollback`.
- Phase 60 fresh-boot runner: `Run-Phase60PostRootReleaseRollbackFreshBoots.ps1`, 3 boots × 12 cycles.
- Phase 54/56/57/58/59 fresh-boot regression runners, each 3 boots × 12 cycles.
- Current `Normal` and `SyntheticScheduler` builds.
- `git diff --check`.

Primary evidence paths:

- `artifacts\phase60-evidence-final\runs\run-1` through `run-3`
- `artifacts\phase60-harness-dev2\ESP\EFI\BOOT\BOOTX64.EFI`
- `artifacts\phase60-regression-phase54-evidence\runs\run-1` through `run-3`
- `artifacts\phase60-regression-phase56-evidence\runs\run-1` through `run-3`
- `artifacts\phase60-regression-phase57-evidence\runs\run-1` through `run-3`
- `artifacts\phase60-regression-phase58-evidence\runs\run-1` through `run-3`
- `artifacts\phase60-regression-phase59-evidence\runs\run-1` through `run-3`
- `artifacts\phase60-postrootrelease-rollback-host-tests\phase60-postrootrelease-rollback-host-tests.exe`

Artifact hashes:

- Phase 60 harness EFI: 583,965 bytes, SHA-256 `5D2204E851F8E559DBD99A39A94B75A13EFD584240B29B5F172C0D533DCDF492`.
- Managed payload: 732,160 bytes, SHA-256 `2E25F807194864CF667FA2B10D72D608247518D3C4A2C02DF691A9D7FCB16FCC`.
- Current normal EFI: 155,051 bytes, SHA-256 `660A6D7277C5BE37BD012702AA9560B66EBBCC7817433E6976CD2AC3CEF87F8F`.
- Current SyntheticScheduler EFI: 154,425 bytes, SHA-256 `0255209EDCD0276250D76D5917ECB4874F26897F0D7E0A8ECD435D84F96AA175`.
- Phase 60 serial run 1: 996,665 bytes, SHA-256 `0C00410DBEC7F1BE045E7D8799EE63F99DB5A7E07C7FB7B1E1B794515171F1B7`.
- Phase 60 serial run 2: 995,142 bytes, SHA-256 `B69C0FE79B3598864C1FC6EA19556DFB0D46D74D96A6EDBC49F6475C28AFB357`.
- Phase 60 serial run 3: 995,142 bytes, SHA-256 `E32D8092C3B5C35DF1D31E2BAF38A9C6E366F06F5BFFC4C4465F38819A0CCF94`.

## Git result

- Commit: one coherent Phase 60 unit with subject `Validate post-root-release worker rollback`.
- Push: attempted against the configured upstream without changing remotes or credentials; final result is recorded in the task completion message.
- No prohibited branch switching, worktree creation, stash, reset, rebase, history rewrite, or force-push was performed.

## Limitations and next step

This phase still covers one failed worker at a time, one deterministic GC per worker, the existing managed static-root abstraction, and the existing scheduler. Physical object relocation is not measured. The proof does not generalize arbitrary managed roots, exception propagation, async scheduling, thread pools, or arbitrary managed-thread APIs. Scheduler-object counts are not managed-heap counts.

Phase 60 is accepted. Do not begin Phase 61. If a future phase is explicitly approved, the smallest next step would be a separately gated observation-only contract around the already-proven detached/reclaimed worker state, without adding another managed feature or a new ownership boundary.

# Phase 57 — Post-Attach Managed Worker Failure Rollback

Date: 2026-09-22

Outcome: **Outcome A — post-attach rollback validated**

## Preflight

| Item | Result |
| --- | --- |
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `977a18377b942a8208ebcf7d73bd1be7b5a50e11` — `Validate managed worker pre-attach rollback` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | `1 ahead / 0 behind` |
| Starting worktree | Clean |
| Live-state comparison | Matched the supplied repository, branch, HEAD, subject, upstream, divergence, and clean-worktree expectations. |

Phase 53 closure, Phase 54 ownership, Phase 55 failure-ownership, and Phase
56 pre-attach rollback reports were read before editing. The existing worker
prepare, resume, NativeAOT attach, FLS cleanup detach, scheduler termination,
collection, generation validation, and Phase 56 injection implementation were
retained.

## Exact injection point

Phase 57 reuses the Phase 56 single-shot, generation-aware injection record.
The new point is:

`GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH`

The executable hook is `GXOS_ENABLE_PHASE57_POSTATTACH_ROLLBACK`, selected by
the `-EnablePhase57PostAttachRollback` build switch. It is additionally
compiled with the existing `GXOS_ENABLE_PHASE56_FAILURE_INJECTION` mechanism;
that macro is the shared framework, not simultaneous Phase 56 activation.
The build script rejects selecting Phase 56 and Phase 57 diagnostic modes at
the same time.

The hook fires in `phase57_failed_worker_entry`, immediately after the first
`gxos_nativeaot_scheduler_worker_invoke` has returned successfully and
`gxos_nativeaot_scheduler_worker_attach` has captured the NativeAOT runtime
thread and entered `RuntimeAttached`. It fires before the later GC bridge
invocation, managed-root survival proof, or post-GC continuation.

The existing architecture performs runtime attach through the generated
managed callback/reverse-P/Invoke trampoline. Therefore the attach callback
frame necessarily executes once. The proof does not claim that no managed
code ran: it records one attach callback invocation per failed worker, zero GC
bridge invocations, and `PHASE57_NO_POST_ATTACH_WORKLOAD=1`. The ordinary
Phase 54 workload begins only after this boundary.

Arming and firing require the same slot, worker identity, generation, live
running TCB, attached runtime state, runtime `Thread*`, FLS value, stack/guard,
GS/TEB, TLS, and allocation-context evidence. A second fire is rejected.

## Worker state at failure

At the injection boundary each failed worker had:

- an owned scheduler slot and live TCB in `Running` / `RuntimeAttached`;
- a distinct worker identity and generation;
- a reserved stack, non-present guard page, usable stack bounds, and valid RSP;
- matching GS, TEB, TLS vector, TLS block, and worker-local FLS value;
- scheduler-owned environment and VM/stack resources;
- an initialized worker-local allocation context;
- a nonzero NativeAOT runtime `Thread*` with attached runtime state;
- `runtime_attach_count = 1` and `runtime_detach_count = 0`;
- a managed worker-object ownership flag, but no managed-root-survival flag;
- no GC callback and no later managed workload.

The failed worker's runtime evidence pointer is retained for post-detach audit,
but `runtime_thread_owned` is cleared only after the established runtime FLS
cleanup callback has completed. This distinguishes evidence retention from
runtime ownership.

## Cleanup contract and exactly-once detach

The intentional failure follows the existing authority chain:

```text
runtime attach
  -> Phase 57 arm/fire
  -> runtime FLS/fiber detach callback
  -> RuntimeDetached
  -> scheduler termination
  -> Reclaimable
  -> public handle close
  -> scheduler collect
  -> Reclaimed
```

`runtime_attach_count` is recorded after successful attach capture and
`runtime_detach_count` is incremented at the actual established FLS cleanup
callback call. The callback must then leave detached runtime state, restore the
ThreadStore census, clear allocation pointers, clear the scheduler FLS value,
and clear runtime-owned lifecycle flags.

For every failed worker the authoritative lifecycle evidence was:

```text
attach count: 0 -> 1
detach count: 0 -> 1
runtime state: ATTACHED -> DETACHED
ThreadStore: baseline + 1 -> baseline
FLS: worker Thread* -> 0
allocation pointers after detach: 0 / 0
```

Duplicate detach was rejected for all 12 cycles. No detach-before-attach,
duplicate FLS cleanup, detach-after-reclaim, wrong-generation detach, double
handle close, double stack release, double VM release, or reclaim while the
runtime attachment remained live was observed.

## Managed-root/object boundary

The attach trampoline publishes the worker-object ownership evidence required
by the existing lifecycle record. No Phase 54 managed root/object workload is
created by the failed path, `managed_root_survived` remains zero, and the GC
bridge invocation delta is zero for every failed worker. Root survival and GC
continuation are exercised only by the healthy replacement worker.

## Resource timeline

Values are measured from the authoritative fresh-boot serial evidence, not
hard-coded architecture constants. Run 1 is representative; runs 2 and 3
reported the same resource peaks and final restoration.

| Resource | Baseline | Prepared | Runtime-attached peak | Post-detach before reclaim | Final |
| --- | ---: | ---: | ---: | ---: | ---: |
| VM regions | `0x3` | `0x5` | `0x5` | `0x5` | `0x3` |
| live scheduler threads/TCBs | `0x2` | `0x3` | `0x3` | `0x3` | `0x2` |
| live scheduler objects | `0xD` | `0xE` | `0xE` | `0xE` | `0xD` |

The final baseline was restored after every failed-worker/replacement pair.
The callback bridge moved from `0x4` to `0x1C` (`+24`: failed attach
trampolines plus 12 replacements). The GC bridge moved from `0x3` to `0xF`
(`+12`: replacements only).

## ABA and stale-identity proof

Each cycle retained the failed slot, identity, generation, and public handle.
After detach, handle close, collect, and `Reclaimed`, the following stale
operations were rejected:

- stale resume and stale handle lookup;
- stale handle close;
- stale detach;
- stale identity/generation runnable mutation;
- stale reclaimable/reclaimed lifecycle notification.

The replacement reused the failed slot on every cycle, while its identity and
generation differed. The replacement remained `Allocated` and
`CreatedSuspended` while stale operations were attempted, then completed its
own attach, managed callback, GC/root survival, post-GC continuation, detach,
and reclaim path. No stale Phase 57 detach affected the replacement runtime
thread.

## Repeated-cycle result

The authoritative Phase 57 runner completed 3 fresh boots × 12 complete
failure/rollback/reuse cycles:

```text
PHASE57_ROLLBACK_RUN_1=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
PHASE57_ROLLBACK_RUN_2=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
PHASE57_ROLLBACK_RUN_3=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
```

Aggregate serial evidence recorded:

```text
injected failure cycles       = 0xC (12)
passed failure cycles         = 0xC (12)
duplicate detach rejections  = 0xC (12)
stale detach rejections      = 0xC (12)
```

No leak trend was observed. Every cycle emitted baseline restoration and
replacement-success markers.

## Regression results

| Validation | Result |
| --- | --- |
| Phase 57 host test | Pass; hook selection rejects incomplete post-attach state and duplicate detach bookkeeping remains rejected. |
| Phase 56 host test | Pass. |
| Scheduler model | `PASSED checks=256`. |
| Scheduler durability | Pass. |
| Scheduler stack/VM | Pass. |
| Resume-thread model | `PASSED checks=57`. |
| Callback bridge host test | Pass; zero external references. |
| Phase 53 current-source fresh boots | 3/3 pass; two lifecycle cycles per boot; `MANAGED_GC_MAIN_OK=1`. |
| Phase 54 current-source fresh boots | 3/3 pass; 12 ownership cycles per boot; baseline `0x3/0x2/0xD`, peak `0x7/0x4/0xF`. |
| Phase 56 current-source fresh boots | 3/3 pass; 12 pre-attach rollback cycles per boot; runtime attach unchanged and failed-worker detach total zero. |
| Normal build | Pass; no Phase 57 compile-time hook. |
| SyntheticScheduler build | Pass; 3/3 fresh synthetic boots pass with expected halt and zero scheduler failures. |

The Phase 56 configuration was built and run separately, so the pre-attach
hook remained isolated. The Phase 57 configuration was the only one that
armed `AFTER_RUNTIME_ATTACH`.

## Artifact hashes and evidence

All diagnostic builds used the authoritative 730112-byte managed payload:

`AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`

| Artifact | SHA-256 |
| --- | --- |
| Phase 57 EFI | `8D6ACA8CD49EA41CF381C507D018A3F38A11FBB0CFD8988296761B92F75D7BB1` |
| Phase 56 regression EFI | `DCE730184CE11683990EEF2C479832743E1D7ECA793DF18569093C58EFE34E93` |
| Phase 54 regression EFI | `EF56DE967CE9A8DFC1CD33E5C2BFE0AE536F692EC6B33A2858701220FB067CEC` |
| Phase 53 regression EFI | `43548B6607AFD0129E10ACEBC0AA9EB0BC185F2A645455EC1F788B89CE98A9FC` |
| Normal EFI | `660A6D7277C5BE37BD012702AA9560B66EBBCC7817433E6976CD2AC3CEF87F8F` |
| SyntheticScheduler EFI | `0255209EDCD0276250D76D5917ECB4874F26897F0D7E0A8ECD435D84F96AA175` |

Evidence roots:

- Phase 57: `artifacts\phase57-final-evidence\runs\run-1` through `run-3`;
- Phase 56: `artifacts\phase56-current-evidence\runs\run-1` through `run-3`;
- Phase 54: `artifacts\phase54-current-evidence\runs\run-1` through `run-3`;
- Phase 53: `artifacts\phase53-current-evidence\runs\run-1` through `run-3`.

Phase 57 serial-log hashes were:

```text
run-1  62B96A332913F31F1A4170D69F8E5B9F6939EA75201644729A1EBA8F0593B946
run-2  41944D138B45BF8F2446F91AEA321E65753A016E6215BD61F997A4969D30E8CD
run-3  EA276533AC6A745C273B4ACD91127FC94163B593908DDBA1817EF09E67C726BF
```

## Changed files

- `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.h/.c`
- `src/Gate4Harness/gate4_loader.c`
- `src/Gate4Harness/tests/phase57_postattach_rollback_host_tests.c`
- `tools/Build-Gate4Harness.ps1`
- `tools/Run-Phase57PostAttachRollbackHostTests.ps1`
- `tools/Run-Phase57PostAttachRollbackFreshBoots.ps1`
- this validation report

No general exception propagation, runtime attach redesign, GC redesign,
thread pooling, arbitrary managed-thread API, post-GC failure point, or
concurrent faulted-worker path was added.

## Limitations and next step

This phase does not test failure after managed-root publication, after a
managed object is intentionally retained by the application workload, or
after GC/post-GC continuation. Those later boundaries remain untested and
Phase 58 was not started.

The smallest Phase 58 step is one single-worker diagnostic point immediately
after the managed-root/object publication boundary, followed by the same
detach/reclaim and replacement proof. It should remain a single boundary and
must not add post-GC or concurrent failure injection in the first iteration.

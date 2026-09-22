# Phase 56 — Pre-Attach Managed Worker Failure Rollback

Date: 2026-09-21
Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
Branch: `nativeaot-managed-kernel-integration`
Outcome: **Outcome A — deterministic pre-attach rollback proof passed; no new production defect found**

## Executive result

The Phase 56 failure boundary is now executable and verified. A prepared
managed worker is failed immediately after `gxos_nativeaot_scheduler_worker_prepare`
and before resume, scheduler dispatch, runtime attach, managed callback, or
runtime FLS publication. The rollback is explicit and ordered:

```text
close public handle → discard CreatedSuspended TCB → scheduler collect
```

The lifecycle record then records the scheduler-owned partial-construction
result with a bounded `Allocated → Reclaimed` evidence edge, but only after
the scheduler has zeroed the TCB and released its resources. No runtime detach
is attempted because runtime ownership was never acquired.

All 12 injected cycles passed in each of three fresh QEMU boots. Each cycle
reused the scheduler slot only after complete reclaim, rejected stale
handle/identity/generation operations, created a replacement worker, and
proved managed callback, managed GC, runtime detach and normal reclaim on the
replacement path.

No new production ownership defect was found. Two diagnostic-only defects were
found and repaired before acceptance: public handle reconstruction used the
raw zero-based object slot instead of the scheduler's one-based encoding, and
the managed callback count incorrectly included the separate GC bridge.

## Preflight

| Item | Value |
| --- | --- |
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `27d6020dee7f62eff23cf0f1b1623fc3f1e531b2` |
| Starting subject | `Document managed worker failure ownership contract` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | `0 ahead / 0 behind` |
| Starting worktree | Clean |

The request's expected divergence was `1 ahead / 0 behind`; the live
repository was `0 ahead / 0 behind`. Phase 53 closure, Phase 54 ownership and
Phase 55 failure-audit reports were read before editing.

## Implemented mechanism

The diagnostic hook is compile-time gated by
`GXOS_ENABLE_PHASE56_FAILURE_INJECTION`. The executable probe is additionally
gated by `GXOS_ENABLE_PHASE56_PREATTACH_ROLLBACK`; ordinary builds contain no
Phase 56 failure-injection path.

The exact injection point is `AFTER_WORKER_PREPARE`. The record stores the
point, `DISARMED/ARMED/FIRED` state, arm/fire/mismatch counts, scheduler slot,
worker identity and worker generation. Arming and firing require the prepared
record to remain `Allocated`, the TCB to remain `CreatedSuspended`, the public
generation-bearing handle to resolve to the same TCB, and all runtime attach
evidence to remain zero. The hook is one-shot: the first fire succeeds and a
second fire is rejected.

The production lifecycle addition is
`gxos_nativeaot_scheduler_worker_note_pre_runtime_reclaimed`. It verifies the
scheduler has fully zeroed the TCB before publishing `Reclaimed`; it rejects
attached, detached, runtime-owned, live, current, queued, referenced or
partially-reclaimed TCBs. Scheduler creation, termination, discard, collect,
stack release, environment release and object-table generation remain the
authoritative resource operations.

## Failure and replacement contract

For every failed worker:

1. `prepare` succeeds and publishes `Free → Allocated`.
2. The hook arms and fires once at `AFTER_WORKER_PREPARE`.
3. The worker has no runtime `Thread*`, no managed callback, no managed root,
   no worker ThreadStore entry, and no detach count.
4. The public handle is closed.
5. `gxos_scheduler_discard_created_thread` terminates and reclaims the
   `CreatedSuspended` TCB.
6. `gxos_scheduler_collect` completes scheduler collection.
7. The lifecycle record publishes the bounded pre-runtime `Allocated →
   Reclaimed` evidence edge.

Duplicate close, duplicate discard, duplicate pre-runtime reclaim, stale
resume, stale close and stale handle lookup are all rejected. A failed detach
attempt is rejected on every cycle; the final failed-worker detach total is
zero.

The next allocation reuses the same scheduler slot but has a different worker
identity and generation. A copied stale lifecycle record cannot mark the new
worker runnable or record it as pre-runtime reclaimed. The replacement worker
then runs the normal path: managed callback, managed GC allocation/collection
and root-survival evidence, runtime FLS detach, scheduler termination, close,
collect and normal `Reclaimed` publication.

## Resource evidence

Values below are from `artifacts\phase56-evidence-final\runs\run-1\serial.log`;
the runner independently checked the same invariants in all three boots.

| Resource | Baseline | Prepared peak | Final |
| --- | ---: | ---: | ---: |
| VM regions | `0x3` | `0x5` | `0x3` |
| Live threads | `0x2` | `0x3` | `0x2` |
| Live objects | `0xD` | `0xE` | `0xD` |
| Environments | `0x1` | `0x2` | `0x1` |
| Stacks | `0x1` | `0x2` | `0x1` |
| Open thread handles | `0x1` | `0x2` | `0x1` |

The runtime ThreadStore baseline was `0x2`; failed-worker deltas stayed zero.
The managed callback baseline was `0x4` and final was `0x10`, exactly one
managed callback per successful replacement cycle. GC callbacks are tracked by
the separate GC bridge and passed all 12 cycles.

Final counters:

```text
injected failure cycles       = 0xC (12)
passed failure cycles          = 0xC (12)
failed runtime detach total   = 0
duplicate cleanup rejections  = 0x24 (36)
```

## Fresh-boot evidence

The authoritative Phase 56 EFI was built at:

`artifacts\phase56-diagnostic-gate\ESP\EFI\BOOT\BOOTX64.EFI`

SHA-256: `53965B0664B8E36A4253A06D4A512E085C9B62F4ABB59F76BC22F014BA821D30`
Managed payload SHA-256: `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`
Evidence: `artifacts\phase56-evidence-final\runs\run-1` through `run-3`

Each boot passed `PHASE56_PASS=1`, `MANAGED_GC_MAIN_OK=1`, `PHASE53O_PASS=1`,
12 injection fires, 12 replacements, 12 GC successes, 12 stale-handle/
identity/generation rejections, complete baseline restoration and zero QEMU
leaks. The three runner outputs are:

```text
PHASE56_ROLLBACK_RUN_1=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
PHASE56_ROLLBACK_RUN_2=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
PHASE56_ROLLBACK_RUN_3=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
```

## Regression matrix

| Validation | Result |
| --- | --- |
| Phase 56 focused host test | `PHASE56_FAILURE_INJECTION_HOST_TEST=PASS` |
| Scheduler model host test | `PASSED checks=256` |
| Scheduler durability host test | `PASS` |
| Scheduler stack/VM host test | `PASS` |
| Resume-thread model host test | `PASSED checks=57` |
| Phase 53 dedicated fresh boots | 3/3 pass; 2 lifecycle worker cycles per boot |
| Phase 54 dedicated fresh boots | 3/3 pass; 12 ownership cycles per boot |
| Phase 56 dedicated fresh boots | 3/3 pass; 12 failure cycles per boot |
| Normal harness build | pass; EFI SHA-256 `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` |
| Synthetic scheduler build | pass; EFI SHA-256 `0255209EDCD0276250D76D5917ECB4874F26897F0D7E0A8ECD435D84F96AA175` |
| Changed PowerShell scripts parse | pass |
| `git diff --check` | pass |

The Phase 53 and Phase 54 runners were given an optional `QemuPath` so the
tests could use an isolated private copy while an unrelated pre-existing
`qemu-system-x86_64` process remained untouched.

## Scope and limitations

This phase proves the pre-attach failure boundary only. Runtime-attached
failure injection remains deferred: once NativeAOT attach has succeeded, the
only legal cleanup authority is the established runtime FLS detach callback,
followed by scheduler reclaim. No new runtime-attached fault path was added or
claimed here.

## Changed files

- `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.h/.c`
- `src/Gate4Harness/gate4_loader.c`
- `src/Gate4Harness/tests/phase56_failure_injection_host_tests.c`
- `tools/Build-Gate4Harness.ps1`
- `tools/Run-Phase56FailureInjectionHostTests.ps1`
- `tools/Run-Phase56PreAttachWorkerFailureFreshBoots.ps1`
- `tools/Run-NativeAotSchedulerThreadLifecycleFreshBoots.ps1`
- `tools/Run-Phase54ManagedWorkerOwnershipFreshBoots.ps1`

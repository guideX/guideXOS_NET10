# Phase 53X — Scheduler Callback Thread Reclamation

Date: 2026-09-20

Outcome: **B — assertion/contract evaluation defect proven**.

The NativeAOT callback worker lifecycle was already safe and complete. The
failing predicate counted one scheduler-stack VM ledger record, while the
Phase 53V stack contract owns two records: the non-present guard and the
committed usable stack. The bounded repair changes the expected delta from
`+1` to `+2`. It does not reclaim a current stack, change scheduler states, or
weaken deferred teardown.

The assertion executes at the correct lifecycle boundary after the main thread
has closed the worker handle and collected the terminated worker. This is not
an early-yield defect; it is an assertion cardinality defect found at the
valid reclamation boundary. Outcome B is the closest phase classification
because the runtime lifecycle is correct and the validation contract was
wrong.

## Live preflight

Repository:

```text
D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration
```

Starting state:

```text
branch:       nativeaot-managed-kernel-integration
HEAD:         ed0f5c0e74751f9b18f1f1376f3476af7eecfa95
subject:      Fix Phase 53W readable-range guard scope
upstream:     origin = git@github.com:guideX/guideXOS_NET10.git
tracking:     nativeaot-managed-kernel-integration
ahead/behind: 0 / 0
status:       clean
```

Phase 53V completion is present:

```text
f7af5a47f0a502bca25c966f966953985aa801f0
Complete Phase 53V worker stack validation
```

Phase 53W completion is the starting HEAD:

```text
ed0f5c0e74751f9b18f1f1376f3476af7eecfa95
Fix Phase 53W readable-range guard scope
```

Read-only `git push --dry-run` and `git ls-remote` were blocked by:

```text
git@github.com: Permission denied (publickey).
```

No reset, clean, stash, discard, amend, rebase, or history rewrite was used.

Authoritative NativeAOT payload:

```text
size: 730112
SHA-256: AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012
```

The exact runtime mode was `NativeAotEventWait` with
`EnableNativeAotStartup`, `EnableNativeAotManagedCallback`,
`EnableNativeAotSchedulerCallback`, `EnableNativeAotManagedGcProbe`, and
`AssumeUnspecifiedTimezoneUtc`.

The callback EFI hash was
`3DC39742B27BCF8FC624B0D41AAEA4D0513D024D22563B05C2D5336F861C5055`.
The Phase 53V regression EFI hash was
`F0C9AD71E37678D331E568837870EDF11CD68A8315D6A5AE51B5ED9FCBC630BC`.

At preflight there were no QEMU, GDB, or LLDB processes owned by this task.
Each focused boot used a fresh copied OVMF variable file. A separate QEMU
process later appeared in another workspace and was not touched.

## Assertion definition

The source is `src/Gate4Harness/gate4_loader.c:19920`, inside
`nativeaot_scheduler_callback_probe()`, after `close_handle()` and
`gxos_scheduler_collect()`. The original predicate was:

```c
close_result && collect_result &&
thread->live == 0 &&
thread->fls_values[g_nativeaot_runtime_fls_slot] == 0 &&
thread->tls_block_base == 0 &&
gxos_scheduler_thread_from_handle(handle) == 0 &&
vm_regions_after_gc != 0 &&
g_memory_vm_regions.live_count + 1U == vm_regions_after_gc
```

The callback worker is identity `0x5`, TCB scheduler slot 2. Slot 0 is the
boot thread and slot 1 is the durable blocked worker. The first worker's
object, FLS, TLS, stack, and callback event are the resources under test.

The red serial values were:

```text
before worker:        0x3
after worker create:  0x5
after close/collect:  0x3
```

The original comparison therefore evaluated `3 + 1 == 5`, which is false.
Every lifecycle predicate before the VM comparison was already true: the
worker was terminated, non-current, non-runnable, had zero execution/public
references after close, had cleared FLS/TLS, and handle lookup returned zero.

## Callback lifecycle

The actual transition sequence is:

```text
create_suspended_thread
  -> CREATED_SUSPENDED, live=1, public_handle_refs=1, execution_refs=1
  -> stack VM contract and worker environment allocated
  -> scheduler admission / resume -> RUNNABLE -> RUNNING
  -> callback input 7 executes
  -> BLOCKED on the event
  -> event signal -> RUNNABLE -> RUNNING
  -> callback input 8 returns 0x40009
  -> gxos_scheduler_prepare_terminate()
  -> TERMINATED, deferred_reclaim=1, execution_refs=0
  -> scheduler switches current to the valid main-thread context
  -> close_handle() drops public handle refs
  -> collect() calls maybe_reclaim_thread()
  -> resources are released and TCB slot 2 is zeroed/reusable
```

Relevant implementation ownership:

| Transition | Function/path | State/ownership evidence |
| --- | --- | --- |
| Admission | `gxos_scheduler_create_suspended_thread()` | TCB slot 2, identity `0x5`, object and handle live |
| Execution | `gxos_scheduler_start_worker` and callback entry | current worker, `RUNNING` |
| Wait | scheduler wait path | worker `BLOCKED`, event wait record linked |
| Wake | event signal/dispatch | `RUNNABLE`, then `RUNNING` |
| Exit | `gxos_scheduler_prepare_terminate()` | `TERMINATED`, deferred reclaim, execution refs zero |
| Safe handoff | scheduler dispatch | main thread current before teardown |
| Reclaim | `gxos_scheduler_collect()` / `maybe_reclaim_thread()` | not current, not runnable, no refs |
| Reuse | `find_free_thread()` | slot 2 reused by identity `0x6` |

The first incorrect transition was not a scheduler state transition. It was
the VM-count assertion's assumption that one worker stack equals one VM ledger
record.

## Reclamation semantics and safety

The implementation distinguishes callback return, logical exit, no longer
runnable, no longer current, `deferred_reclaim`, TCB-slot reuse, VM-reservation
release, physical-page release, GS/TEB/TLS release, handle retirement, and
counter/reference decrement. They intentionally occur at different points.

The safe boundary is:

```text
callback returns
  -> TCB becomes TERMINATED/deferred
  -> scheduler switches away
  -> close removes public handle ownership
  -> collect verifies no current/runnable/reference ownership
  -> stack and environment are freed
  -> TCB/object slot is reusable
```

No self-reclamation path was found. The callback never frees its current
stack, GS base, TEB, or TLS while executing on them. The reaper is the main
scheduler context after the switch.

Resource ownership and result:

| Resource | Allocated/owned by | Released by/condition | Result |
| --- | --- | --- | --- |
| Guard reservation | `memory_allocate_scheduler_stack()` / TCB contract | `memory_free_scheduler_stack()` after not-current | released; non-present throughout |
| Usable 64 KiB stack | same | same deferred free path | released; VM count returns to baseline |
| Stack ledger | loader VM ledger | unregister guard and usable records | two records removed |
| Stack canary page | page allocator / TCB | collect after canary check | intact before free |
| GS page | `allocate_thread_environment()` | `free_thread_environment()` after switch | released |
| TEB page | same | same | released |
| TLS vector/block | same | same | bases zero after collect |
| FLS value | callback runtime | detach/reclaim | zero after collect |
| TCB slot | scheduler table | `maybe_reclaim_thread()` | `live=0`, slot reusable |
| Object/handle | scheduler object table | close + generation-checked lookup | lookup returns zero |
| Event/callback metadata | callback probe | completion and event destruction | no stale waiter/metadata |

The Phase 53V contract remains:

```text
usable stack: 0x10000 / 64 KiB
guard:        0x1000 / 4 KiB, non-present
GS+0x10:      usableStackLow
TEB+0x10:     usableStackLow
```

Generation safety is already present: object handles encode type, generation,
and slot, and lookup validates all three. The scheduler identity changes from
`0x5` to `0x6` on reuse. No ABA, stale-reference, queue, wait-object, current,
or generation defect was found.

## TDD evidence

### Red

Before the source repair,
`tools/Run-Phase53XCallbackReclaimFreshBoot.ps1` failed with:

```text
Phase 53X callback reclaim assertion or guest fault fired.
```

Evidence:

```text
evidence\phase53x-callback-reclaim-red\run-1\serial.log
SHA-256 B663F79E4EA43762CBF7359C049B668B214821792FADA21515D7F0A03DE1E9C3
```

The guest reached callback completion, `TERMINATED`, close, collect, zero
live/FLS/TLS/handle/reference state, then emitted:

```text
FAIL:nativeaot-scheduler-callback-thread-reclaim
```

### Repair

The only runtime assertion change is at `gate4_loader.c:19920`:

```diff
- g_memory_vm_regions.live_count + 1U == vm_regions_after_gc
+ g_memory_vm_regions.live_count + 2U == vm_regions_after_gc
```

The comment records that the worker stack contract has separate guard and
usable VM ledger records. No thread limit, scheduler state, stack lifetime,
or identity rule changed.

The existing Phase 53V runner also received a narrow parser fix: when the
integrated boot emits multiple `PHASE53V_GUARD_BASE` records, it selects the
final dedicated guard marker instead of an earlier unrelated marker.

### Green

Three fresh callback boots passed through the second-worker reuse boundary:

```text
evidence\phase53x-callback-reclaim-green-run4\run-1\serial.log
evidence\phase53x-callback-reclaim-green-run5\run-1\serial.log
evidence\phase53x-callback-reclaim-green-run6\run-1\serial.log
```

Each produced:

```text
PHASE53X_CALLBACK_RECLAIM=PASS identity=0x5 vmBefore=0x3 vmCreated=0x5 vmAfter=0x3
MANAGED_THREAD_DETACH_OK=1
MANAGED_GC_THREAD_RECLAIM_OK=1
MANAGED_THREAD_REUSE_OK=1
MANAGED_GC_WORKER_RETURN_OK=1
MANAGED_GC_MAIN_OK=1
```

The only later failure was the explicitly isolated downstream marker:

```text
FAIL:nativeaot-managed-callback-post-state
```

The focused runner rejects the Phase 53X assertion, CPU exceptions, page
faults, and every other failure.

## Bounded reuse and neighboring tests

The host stack-VM suite passed twelve bounded create/reclaim cycles. Each
worker temporarily owned two VM records, then the count returned to baseline.
The durability suite passed terminate/close/collect, TCB clear, generation,
and identity reuse checks.

```text
SCHEDULER_STACK_VM_HOST_TEST=PASS
SCHEDULER_DURABILITY_HOST_TEST=PASS
SCHEDULER_MODEL_TESTS=PASSED checks=256
CREATETHREAD_MODEL_TESTS=PASSED checks=134
RESUMETHREAD_MODEL_TESTS=PASSED checks=57
NATIVEAOT_GC_PROBE_CONTRACT_TESTS=PASSED checks=8
```

The NativeAOT path itself performs a first callback reclaim followed by a
second callback worker reuse. The twelve-cycle host test supplies the bounded
pool repetition without creating an unbounded stress test.

## Regression gates

### Phase 53V

Three fresh guard boots were captured under:

```text
evidence\phase53x-phase53v-regression-manual\runs\run-1\serial.log
evidence\phase53x-phase53v-regression-manual\runs\run-2\serial.log
evidence\phase53x-phase53v-regression-manual\runs\run-3\serial.log
```

All three showed:

```text
PHASE53V_GUARD_FAULT=1
PHASE53V_GUARD_NONPRESENT=1
PHASE53V_GUARD_GS_TEB_INTACT=1
PHASE53V_GUARD_UNRELATED_STATE_INTACT=1
PHASE53V_GUARD_BASE == PHASE53V_GUARD_FAULT_CR2
PHASE53V_WORKER_GS_LOWER == PHASE53V_USABLE_STACK_LOW
PHASE53V_WORKER_TEB_LOWER == PHASE53V_USABLE_STACK_LOW
PHASE53V_USABLE_STACK_BYTES=0x10000
```

No guard, GS, TEB, or current-stack regression occurred.

### Phase 53W

Post-repair Normal and SyntheticScheduler builds passed:

```text
PHASE53W_READABLE_RANGE_BUILD_TESTS=PASSED modes=Normal,SyntheticScheduler
Normal EFI:            04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964
SyntheticScheduler EFI:4681AC05E641A1305817730ED712BB487C6BBAD943D11C2DE13B332AA7AC7D9A
```

The `nativeaot_gc_readable_range` predicate and Phase 53W compile-time guard
were not changed. Focused NativeAOT boots reached:

```text
MANAGED_GC_MAIN_OK=1
MANAGED_GC_WORKER_BEFORE_UNWIND_FAILURES=0
MANAGED_GC_WORKER_AFTER_UNWIND_FAILURES=0
```

No readable-range failure or repaired-range page fault returned.

Additional post-repair one-boot smoke checks also passed:

```text
Normal:            PASS EFI_SHA256=04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964
SyntheticScheduler:PASS EFI_SHA256=4681AC05E641A1305817730ED712BB487C6BBAD943D11C2DE13B332AA7AC7D9A
PHASE53W_POST_REPAIR_SMOKE=PASSED
```

## QEMU result and Phase 53Y boundary

Phase 53X performed three fresh QEMU callback boots. Each created the first
callback worker, completed both callbacks, reclaimed identity `0x5`, reused
the TCB slot with identity `0x6`, restored VM baseline, and passed the
callback-reclaim assertion. This is the exact callback path required by the
phase. The integrated NativeAOT path also reached `MANAGED_GC_MAIN_OK=1`.

The next independent failure is:

```text
nativeaot-managed-callback-post-state
src/Gate4Harness/gate4_loader.c:22588
```

That predicate currently requires `callback_counts.vm_region_count == 2`,
while the durable baseline and serial evidence report `0x3`. It is not a
callback-thread reclaim failure and was not changed here. This is the exact
Phase 53Y target.

## Final determination

The exact safe transition is:

```text
callback returns
  -> TCB TERMINATED and deferred_reclaim=1
  -> scheduler dispatches to a different current context
  -> close removes public handle ownership
  -> collect verifies no current/runnable/reference ownership
  -> free two stack VM ledger records plus worker environment
  -> zero TCB/object state
  -> reuse slot with a new identity/generation
```

That transition was already present and is now validated. The original
assertion expected one stack VM record to disappear while the worker contract
correctly releases two. guideXOS can reclaim and reuse the worker slot and all
worker-local resources without freeing active execution state.

# guideXOS C# .NET 10 — Phase 81R2 Atomic Wait, Timeout, and GC Audit

**Date:** 2026-10-03

**Prompt phase:** `81R2`
**Outcome:** C — no scheduler-owned production timer currently guarantees timeout servicing without integrating or replacing an exclusive platform timer owner. The requested atomic wait and managed-GC acceptance is therefore not established.

**Phase 81R2 repairs the scheduler/runtime prerequisite. It does not itself accept Phase 81 E1000 TX integration.** This investigation did not complete that repair. Phase 81 remains unaccepted, and Phase 82 was not started.

## Live phase and Git preflight

The stale-prompt gate passed. `.phase` is absent; the live HEAD is the documented Phase 81R blocker; the Phase 81 and Phase 81R reports both exist; and no Phase 81R2 implementation or later accepted phase is committed. No `.phase` mechanism was created.

| Field | Live starting state | Prompt expectation |
|---|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` | Same |
| Branch | `nativeaot-managed-kernel-integration` | Same |
| HEAD | `8a1ab9dc666ea7b65bf687c51080ff9c137d8872` — `Document Phase 81R scheduler wait blocker` | Same |
| Upstream | `origin/nativeaot-managed-kernel-integration` | Same |
| Divergence | 0 ahead / 0 behind | 1 ahead / 0 behind |
| Worktree | Clean | Clean |
| `.phase` | Absent | Not specified |

The live repository state was authoritative. The divergence mismatch was not corrected by changing branch or history. At the time this report was prepared, only this report was staged; `.phase` remained absent and HEAD remained the starting commit. The documentation commit and push result are recorded in the task closeout.

## Production timer and timeout clock audit

`gxos_scheduler_service_timeouts(now_ms)` scans the fixed six-entry wait-record array and calls `timeout_waiter` for due records. `gxos_scheduler_poll_timeouts()` obtains time from the configured callback and invokes that scan. The production call-site search found no scheduler tick, timer IRQ handler, or periodic service that invokes either function after a waiter blocks. `event_api.c` calls the poller before arming a finite wait; that one pre-wait call does not service the wait later.

The Gate4 loader deliberately preserves the firmware IDT so unowned firmware timer and other IRQ vectors remain valid. It also marks IRQ0 as an exclusive platform-owned interrupt. The kernel image installs serial, keyboard, E1000, and optional diagnostic routes, but no scheduler timer interrupt or timeout-service hook. No owned IRQ0 frequency or tick resolution is available to the scheduler. Reusing that route safely requires a timer-owner handoff/integration that is outside a bounded wait-record change.

The configured scheduler clock is `scheduler_now_ms`, which calls EFI Runtime Services `GetTime`, converts the returned civil time to FILETIME, divides by 10,000, and uses those milliseconds for deadlines. This is wall-clock time and can change independently of elapsed time. The source does not establish that calling firmware `GetTime` from interrupt context is safe. It is not called by an existing interrupt path.

The loader separately exposes a monotonic performance-counter service when the initialized source is invariant TSC with CPUID leaf 0x15. That service returns normalized counter ticks and the calibrated/source frequency. It is not configured as the scheduler's wait clock, and it has no periodic caller that would make a blocked timeout complete. The ACPI PM-timer path is explicitly not advertised as monotonic when it may be idle beyond its safe wrap interval. No fake millisecond precision was inferred from either source.

The timeout scan itself is bounded: at most six wait records are checked per call. Each due record causes one constant-time waiter detach and wake transition, so the scan performs at most six record checks and six completion transitions, excluding constant-sized state updates and runnable enqueue. This bound does not provide a service deadline when no production timer owner calls the scan.

## Scheduler exclusion and wait transition audit

There is no scheduler-owned critical-section callback or lock in `scheduler_foundation.c`. The loader has a local-IRQ exclusion pair used by the managed interrupt subsystem: enter saves RFLAGS and executes `cli`; leave executes `sti` only when the saved IF bit was set, otherwise it leaves interrupts disabled. The functions do not keep a nesting counter; properly nested calls preserve the outer disabled state through the saved flags. On the current single-CPU path, interrupt gates enter with IF clear, so a critical section entered in IRQ context restores IF clear and does not enable nested maskable interrupts. This primitive is private to the loader and is not wired around scheduler registration, signal, timeout, or teardown operations. No scheduler-lock ordering contract exists.

Current wait preparation in `gxos_scheduler_prepare_wait_record` validates the event, checks its signal state, pins the event, reserves a record, attempts to link the waiter, marks the record active, sets the TCB blocked, and selects a runnable replacement. Those operations are not protected from IRQ signaling. A concrete lost-wakeup ordering remains possible: registration observes an unsignaled event, an IRQ sets the event while no active waiter is visible (or removes a just-linked inactive record), and registration then publishes the caller as blocked without a final serialized signal-state check. The second state check in the function does not make the check/link/activate/block sequence atomic.

`SetEvent` reaches `gxos_scheduler_signal_event` directly through the configured native work-notification callback. The path uses bounded native state changes, allocates nothing, calls no managed code, and does not context-switch in the IRQ. An auto-reset event handles at most one waiter; a manual-reset event drains at most six. However, event state, wait-record lifetime, TCB state, and runnable-queue mutation have no scheduler exclusion. IRQ signaling does happen in production, but it cannot be certified IRQ-safe in the present implementation.

Wait records use `valid`, `active`, `completed`, and `completion_result` fields rather than an explicit completion enum. Signal and timeout both call `complete_waiter`; that routine removes the wait link, records a result, changes the thread state, and enqueues it. Timeout service skips records already marked completed, and sequential host calls preserve whichever completion runs first. There is no lock/IRQ exclusion around that test-and-transition, so signal and timeout have no proven production first-transition-wins authority. The eventual design must serialize the completion transition and allow exactly one runnable enqueue.

## Existing event and lifetime semantics

| Case | Existing source/host behavior | Phase 81R2 acceptance |
|---|---|---|
| Signal before wait | Manual-reset remains latched; auto-reset consumes its one token on the immediate wait. | Existing semantics pass; no source change. |
| Signal during registration | Race window can leave the caller blocked after the event is set. | Not safe. |
| Signal after a completed block | Signal path removes the linked waiter and marks it runnable. | Sequential behavior exists; IRQ serialization is not proven. |
| Timeout | Explicit `gxos_scheduler_service_timeouts` can produce `GXOS_WAIT_TIMEOUT`. | Host path only; no automatic production owner. |
| Signal then timeout | Completed record is skipped by a later sequential timeout scan. | Ordered host behavior only; no concurrent race proof. |
| Timeout then signal | Timeout unlinks the waiter; later auto-reset signal latches one token for a future waiter. | Ordered host behavior only; no concurrent race proof. |
| Duplicate auto-reset signals without a waiter | `signaled` is a single latched bit, so signals coalesce. | Existing behavior passes. |
| Manual-reset signal/reset | Signal stays latched and `ResetEvent` clears it; future waits complete while latched. | Existing behavior passes. |
| Stale handle | Object lookup checks type, slot, and generation. A reused slot increments generation. | Existing stale-generation rejection remains. |
| Wait-record reuse | A record stays valid while active/completed and is released by `gxos_scheduler_finish_wait` after result consumption. The record pins its event. | Lifetime behavior exists; no late-IRQ race proof. |
| Blocked-thread teardown | `gxos_scheduler_try_reclaim_thread` refuses a blocked TCB. Closing a public handle does not asynchronously cancel it; the event pin remains until completion. | Restriction remains; missing timeout service can leave a finite waiter pinned. |

The record table and waiter arrays are fixed capacity. With six threads, one caller can occupy one of six wait records and one event waiter entry; the event signal path performs no allocation. Existing stale-generation checks are retained, but a signal/timeout/teardown race over a live record is not serialized.

## Invalid blocking state and scheduler progress

If `gxos_scheduler_prepare_wait` returns a negative status, both the main-thread and worker assembly wait wrappers branch to `gxos_scheduler_switch_failure`, which executes `ud2`. One expected source of failure is the absence of another runnable thread: the preparation routine restores the caller to Running and returns failure, then the wrapper reaches `ud2`. No idle-thread path makes this case valid. The public event API has no named invalid-state error; its generic scheduler failure maps to `GXOS_WAIT_FAILED` with `GXOS_EVENT_ERROR_NOT_ENOUGH_MEMORY`, which is not an accurate invalid-state result. No IRQ-nesting state is tracked to reject an attempted blocking wait from IRQ context. These paths are not repaired here; internal corruption assertions should remain fail-stop.

The normal wait wrapper performs a bounded setup, enters the native block routine, context-switches to a selected runnable TCB, and consumes its completion result only when scheduled again. It does not spin on event state or repeatedly dispatch descriptors while blocked. Existing SyntheticScheduler proofs demonstrate unrelated scheduler activity and event wake in their own fixture. They do not inject the Phase 81R2 registration interleavings, service timeouts automatically, or attribute absence of execution to a managed waiter in the requested scenario.

## Runtime attachment and managed-GC boundary

The scheduler context retains each TCB's GS base, TEB, TLS vector/block, stack, and thread identity across a context switch. The managed worker lifecycle has explicit runtime attach/detach tracking, and the persistent service uses `WaitForSingleObject` while attached. The wait implementation itself contains no detach call.

The NativeAOT-to-native import path was not instrumented to establish the requested blocked-caller proof. Existing evidence does not show one synchronous managed caller with a live object reference blocked in `WaitForSingleObject` while a second managed context triggers a real GC. The scheduler TCB identity is stable in the scheduler data model, but no Phase 81R2 trace records the same runtime thread identity before block, after signal wake, and after timeout. A native pointer or retained object address would not establish a managed stack root; no such substitute was used.

Consequently, this report makes no claim that a managed reference survived GC across either a signal wait or timeout wait, and makes no claim that an object physically relocated. No focused GC guest fixture exists in the current tree. Runtime attachment is preserved structurally by the current wait path, but valid managed stack-root enumeration while another managed context collects remains unproven.

## Validation executed

All listed host suites ran against the unchanged production source. The ordinary builds also passed. These results validate the existing foundation, not the missing atomicity/automatic-timeout/managed-GC contract.

| Check | Result |
|---|---|
| `Run-EventApiHostTests.ps1` | PASS — 245 checks. Timeout coverage calls the timeout service explicitly. |
| `Run-ManagedKernelInterruptNativeHostTests.ps1` | PASS. |
| `Run-ManagedKernelInterruptHostTests.ps1` | PASS — toolchain discovery; 84 production sources / 85 host compile items / 0 duplicates; 15/15 assertions. |
| `Run-ManagedKernelDriverServiceOwnerHostTests.ps1` | PASS — service owner and service status suites. |
| `Run-ManagedKernelDriverWorkerHostTests.ps1` | PASS — 3 repeated wake cycles; dispatch and delivery checks passed. |
| `Run-ManagedKernelServiceHostTests.ps1` | PASS. |
| `Run-Phase61ManagedWorkerApiHostTests.ps1` | PASS — Phase 61, 62, 64, 65, and 66 host regressions. |
| `Build-ManagedKernel.ps1` | PASS — .NET SDK 10.0.401; 4,799,488-byte NativeAOT payload. |
| `Build-Gate4Harness.ps1` | PASS — ordinary `Normal` EFI build. |
| SyntheticScheduler build and `Run-SyntheticSchedulerProof.ps1 -RunCount 3` | PASS — three fresh QEMU boots of the existing SyntheticScheduler fixture. This fixture does not test finite wait timeout or managed GC across a block. The first default-path invocation found no default fixture, so a fresh build was made under `artifacts/phase81r2-synthetic-build` and the explicit run passed 3/3. |
| Phase 81R2 wait/race host fixture | Not added; no wait transition changed. |
| Phase 81R2 target QEMU matrix | 0/3 attempted; no managed wait+GC+timeout fixture exists. |
| IRQ-origin wake of the Phase 81R2 managed waiter | Not proven. Existing serial/service IRQ wake history is adjacent evidence only. |
| Phase 56–60 rollback regressions | Not run; no scheduler/runtime lifecycle source changed. |
| Production timer regression | Not run; no timer interrupt or timekeeping source changed. |

The existing event API host suite covers pre-signaled event behavior, sequential signal and timeout completion, stale handles, event modes, and error paths. It has no deterministic interleaving hooks at waiter publication and cannot prove actual IRQ races. No new race-injection tests were added. The three SyntheticScheduler runs have serial logs, but their passing markers do not cover the requested wait timeout or managed GC scenarios.

## Outcome and resume point

**Outcome C — the current timer architecture cannot safely service scheduler deadlines without a timer-owner integration.** EFI `GetTime` supplies wall-clock milliseconds only when a caller asks for it; the invariant-TSC performance counter supplies an elapsed-time source but is not a timeout tick. IRQ0 is platform-exclusive and the current scheduler has no registered periodic timer owner. Adding polling would not meet the requirement and was not introduced.

No production defect was repaired in this documentation-only investigation. The confirmed root cause is that wait registration, signal, and timeout mutate shared scheduler state without the existing local-IRQ exclusion, while no production owner guarantees timeout servicing. The independent NativeAOT stack-root/GC proof is also absent. Phase 81R2 is not accepted.

The exact resume point is to define scheduler-owned critical-section entry/leave and IRQ-context rejection; serialize event validation, publication, signal, timeout, and teardown; choose and integrate a monotonic clock with an authorized periodic timer owner; replace expected invalid blocking failures with a named wait result while retaining internal fail-stop assertions; then add deterministic interleaving tests and a real NativeAOT managed waiter/GC guest fixture. That fixture must prove signal wake from a native IRQ and automatic timeout, stable runtime attachment, live managed reference access after both completions, and unrelated runnable progress. Only after Phase 81R2 is accepted should the original Phase 81 E1000 TX ownership/correlation work resume. Phase 81 TX must be rerun separately before calling Phase 81 accepted.

## Capacity and remaining claims

The fixed scheduler capacities are six TCBs, six wait records, twelve scheduler event slots, and sixteen scheduler objects. The new guest scenario was not run, so no actual event/object/TCB usage during that scenario is claimed. The timeout scan's worst-case bounded work is 42 record/slot inspections as detailed above. The current direct IRQ signal callback does no allocation and no managed work, but its scheduler mutation is not protected. Busy polling is not present in the ordinary wait caller, though this does not compensate for missing autonomous timeout service.

## Evidence paths and SHA-256

Generated artifacts are ignored by repository policy and are not committed. The following hashes identify the host/build artifacts and three existing-fixture QEMU serial logs used during this audit:

| Artifact | SHA-256 |
|---|---|
| `artifacts/event-api-host-tests/event-api-tests.exe` | `5A4FFC8FB6A1E4834E8C9EC1211E9063075ABCE159DBC07FD41943D133556B66` |
| `artifacts/managed-kernel-interrupt-native-host-tests/managed_kernel_interrupt_tests.exe` | `7BD1CFD56F18D8F9B929375C633FA23E65628B47A4A8AEB34C3B5D15FACBBCB8` |
| `artifacts/managed-kernel-interrupt-host-tests/bin/ManagedKernelInterruptHostTests.dll` | `48CCFB2C33A03992FEF0DEB476CEEEC6C74A118050C07D0C502C516BA56456F2` |
| `artifacts/managed-kernel-driver-service-owner-host-tests/managed_kernel_driver_service_owner_tests.exe` | `16A47B3E29B15B2ABCDE032BDD2F625DD43E46C9BE7D880BDEC14FD019C67337` |
| `artifacts/managed-kernel-driver-service-owner-host-tests/managed_kernel_driver_service_status_tests.exe` | `4B8C4A1FA2375FD3C4649E854491CA3EFE174882FEE1DB7794B91AD42B10241A` |
| `artifacts/managed-kernel-driver-worker-host-tests/bin/ManagedKernelDriverWorkerHostTests.dll` | `2DF97CC0B011DA8B2BD2200BD0315A70EE8F82D2960BE458DDBED9BEB10C4B92` |
| `artifacts/phase61-managed-worker-api-host-tests/phase61-managed-worker-api-host-tests.exe` | `CB8CAEA5F0C0064E21AF62D0B03A88FE5EF773FE4E1992148C1909E436D5BC32` |
| `artifacts/managed-kernel/publish/gxos-managed-kernel.dll` | `5219417EA53C869B2F34D9D7DE72C43B836EA78AC33A549EC0C0880B25A818A9` |
| `artifacts/gate4/ESP/EFI/BOOT/BOOTX64.EFI` | `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` |
| `artifacts/phase81r2-synthetic-build/ESP/EFI/BOOT/BOOTX64.EFI` | `26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946` |
| `artifacts/phase81r2-synthetic-build/synthetic-runs-20261003-152923-282/run-{1,2,3}/serial.log` | `29FCA73B1A46099CA212D1CFB9CD344A21D6AD490DDC620404108B55D59524F1` each |

## Git closeout

This is a coherent documentation-only Outcome C. The report was reviewed, `git diff --check` passed, and it was committed automatically under the requested workflow; the task closeout records the commit and one normal push result. No remotes, credentials, Git identity, branches, worktrees, or history were altered. No prohibited Git operation occurred during this audit. No production source, test source, QEMU fixture, branch, or worktree was changed.

**Phase 81R2 — Atomic Scheduler Wait Transitions, Timer-Driven Timeouts, and Managed GC Safety** remains unresolved. Phase 81 is not accepted; Phase 82 was not started.

# Phase 81R — Scheduler-Aware Managed Wait/Wakeup Prerequisite

**Date:** 2026-10-03

**Outcome:** C — the existing event can block and wake an eligible scheduler thread, but the current scheduler cannot provide the required race-safe, bounded managed wait without a timer service and an IRQ-safe scheduler transition.

> **Phase 81R supplies the blocking/wakeup prerequisite only; it does not by itself complete Phase 81 E1000 TX integration.** Phase 81 remains unaccepted. Phase 82 was not started.

## Preflight and phase state

| Field | Starting state |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| HEAD | `b9bdc0128987cec4fce4a68545fb20438b9b530d` — `Document Phase 81 TX wait-path blocker` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Divergence | 1 ahead / 0 behind |
| Worktree | Clean |
| `.phase` | Absent |

The Phase 80 and Phase 81 reports and recent scheduler/service validation records were inspected. They show Phase 80 accepted, Phase 81 unaccepted, and no completed Phase 81R implementation. No authoritative `.phase` marker exists, so no marker was created or advanced. This investigation leaves Phase 81R unresolved and Phase 81 TX integration pending.

## Existing scheduler event and managed ABI

The repository already has one scheduler-owned event implementation. `GXOS_SCHEDULER_EVENT` aliases the bounded `GXOS_SCHEDULER_WAITABLE`; event identity is an opaque 64-bit generation-tagged scheduler handle. The Gate4 import resolver exposes the generic `CreateEventW`, `SetEvent`, `ResetEvent`, `WaitForSingleObject`, `WaitForSingleObjectEx`, and `CloseHandle` surface to the NativeAOT image. The managed caller sees an opaque handle, not a TCB, object address, or scheduler structure. No E1000-specific wait ABI is present or needed.

The ordinary wait contract is `WaitForSingleObject(HANDLE, milliseconds)` and the corresponding signal contract is `SetEvent(HANDLE)`. Result constants are named `GXOS_WAIT_OBJECT_0`, `GXOS_WAIT_TIMEOUT`, and `GXOS_WAIT_FAILED`; failures use the named last-error values `GXOS_EVENT_ERROR_INVALID_PARAMETER`, `GXOS_EVENT_ERROR_INVALID_HANDLE`, and `GXOS_EVENT_ERROR_NOT_ENOUGH_MEMORY`. The current ABI has no distinct invalid-state result. Some invalid blocking states reach the assembly fail-stop path instead of returning a named error.

Ownership follows the existing event contract:

- The event owner creates the event through `CreateEventW` and keeps the opaque handle.
- A caller with that handle may wait; an authorized producer may signal it with `SetEvent`.
- The owner resets a manual-reset event before reuse when it needs a fresh signal episode.
- The owner closes its public handle with `CloseHandle`; destruction is allowed only when no waiter remains and internal references are released.
- A blocked wait record pins the event object until the waiter consumes its completion.

Manual-reset events latch one signaled state and wake eligible waiters once; repeated sets coalesce while already signaled. Auto-reset events release one waiter per set or retain one latched token when there is no waiter. Manual-reset events are reusable after `ResetEvent`; auto-reset waits consume the prior signal. Scheduler waiters and records are fixed arrays, not dynamically growing queues.

## Capacity

No new scheduler abstraction or object type was added. A caller-created wait event consumes one of 16 scheduler-object slots and one of 12 event slots. A blocked caller consumes one of the six fixed wait records and occupies one of the six TCBs already in use for that thread; the wait does not allocate another TCB. No heap allocation is performed by the event signal path.

Capacity is material: Phase 69 measured all 16 scheduler objects occupied when its persistent service and a pending one-shot were admitted together. In that state, one additional caller-owned wait event cannot be admitted. The scheduler event table, object table, waiter arrays, and wait-record table are all bounded.

## Blocking and wake behavior found

For an unsignaled event, the scheduler records the caller in a fixed wait record, changes its state from `Running` to `Blocked`, selects another runnable TCB, and later changes the waiter from `Blocked` to `Runnable` when signaled or timed out. The caller returns to `Running` when selected again. A pre-signaled event returns immediately; auto-reset state is consumed by the wait, while manual-reset state remains latched.

The existing Phase 69 service attaches its NativeAOT runtime thread before entering its service loop and uses `WaitForSingleObject` on its wake event between dispatch batches. The service is awakened by its event producer or shutdown path, continues bounded dispatch, and detaches only during service exit. The Phase 69 reports also document real queue wake, DRAIN/DISCARD, stop, and resource reclamation. Phase 72's three fresh QEMU boots prove the existing service can fail admission, restart in a new generation, attach, re-enable its route, and process a real event. These proofs establish useful event and lifecycle behavior; they do not establish the required managed TX caller wait contract.

## Why this event cannot yet satisfy Phase 81R

### Lost-wakeup window

The event state is checked before entering the scheduler block path and checked again while preparing the wait. That second check alone does not close the interrupt race. `gxos_scheduler_prepare_wait_record` links the wait record, marks it active, updates the TCB state, and selects the next thread without disabling local interrupts or taking a scheduler exclusion. The interrupt capture path can call `gxos_scheduler_signal_event` during those updates. In particular, a signal can arrive after the event's final signaled check but before its waiter becomes active and linked; the signal path can miss or discard that not-yet-eligible waiter, after which the caller is marked `Blocked`. The event signal routine also changes the runnable queue and TCB state without scheduler exclusion.

This is a race by source ordering, not a demonstrated failure on a particular timing run. The existing tests do not inject an IRQ at each wait-registration instruction, so they cannot close this proof gap. A local-IRQ critical section around the entire arm/block transition and the signal/timeout state transitions, with a documented single-CPU restriction, is required before claiming lost-wakeup safety.

### Timeout has a deadline but no guaranteed service

Finite waits capture an overflow-saturating millisecond deadline using the configured clock. The NativeAOT loader obtains that clock from EFI `GetTime` and converts it to milliseconds. A timeout becomes authoritative only when `gxos_scheduler_service_timeouts` or `gxos_scheduler_poll_timeouts` runs.

The runtime call-site audit found no timer interrupt or scheduler tick that invokes either timeout service. `event_api.c` calls `gxos_scheduler_poll_timeouts` before arming the current wait, not after the caller blocks. Therefore an armed waiter is not guaranteed to resume at its deadline. The host timeout shim manually advances the clock and calls `gxos_scheduler_service_timeouts`; that proves the record transition, not a production timer wake. The current Gate4 loader also documents IRQ0 as having an exclusive platform owner, so adding a timer tick requires integration with that owner or a scheduler timer architecture change.

The current millisecond field is a deadline representation, not evidence of an autonomous timeout bound. A scheduler pass or a repeated dispatcher/yield loop would not provide a bounded timeout when no later scheduler transition or time-service call is guaranteed.

### Invalid blocking state and context

`gxos_scheduler_prepare_wait_record` fails if it cannot find another runnable TCB. The assembly wait wrappers route negative preparation results to `ud2`, so the managed caller does not receive an explicit invalid-state result. The event API does not track IRQ nesting. Although current IRQ code does not call a managed wait API, the primitive has no enforceable rejection for an attempted wait from IRQ context. The boot thread can block only when a different runnable scheduler entity is available.

### IRQ work and runtime/GC boundary

The interrupt capture path reads and acknowledges the device, publishes a bounded native event record, and calls the configured native work-notify callback. It does not allocate, attach NativeAOT, invoke arbitrary managed code, or context-switch in IRQ context. The configured notify callback signals the existing scheduler wake event. That signal operation is bounded, but its scheduler-state writes are not protected against an IRQ arriving during a normal scheduler transition, so IRQ signaling safety is not established.

The scheduler retains each TCB's stack, GS/TLS state, and runtime attachment across a block. Phase 69 proves the service's attach/wait/stop/detach lifecycle and ThreadStore counts. Existing scheduler and managed-worker validations exercise thread state and GC lifecycle. This turn did not prove that a managed synchronous caller can block with live managed references on its stack while a GC runs and then resume with those roots still visible. That NativeAOT caller/GC proof remains required.

## Deferred managed dispatcher contract

The intended later integration is: a managed caller arms a generic scheduler event and blocks; the native IRQ path captures and queues the typed event, then signals the scheduler event; after normal scheduling resumes, managed code drains the typed interrupt queue through `ManagedInterruptDispatcher` and inspects driver-owned completion state. Managed code must not run from the hardware IRQ. Waking the caller alone does not establish descriptor completion or ownership release.

Phase 80 proves the real E1000 TXDW event reaches `ManagedE1000Driver` when the managed dispatcher is explicitly drained. The normal TX method still polls descriptor DD for up to 100,000 iterations. Phase 81 still needs a request/descriptor ownership record, typed-event correlation, authoritative DD inspection, timeout ownership retention, and a race-safe release policy.

## Validation performed on the unchanged production tree

| Check | Result |
|---|---|
| Focused event API host suite (`Run-EventApiHostTests.ps1`) | PASS — 245 assertions. The timeout path is serviced by the host shim. |
| Managed interrupt host harness (`Run-ManagedKernelInterruptHostTests.ps1`) | PASS — toolchain discovery green; 84 production sources, 85 compile items, 0 duplicates; 15/15 tests. |
| Native interrupt host tests (`Run-ManagedKernelInterruptNativeHostTests.ps1`) | PASS. |
| Driver service owner host tests | PASS. |
| Driver service status host tests | PASS. |
| Driver worker host tests | PASS — repeated wake cycles passed. |
| Phase 61–66 managed-worker API host tests | PASS — Phases 61, 62, 64, 65, and 66. |
| ManagedKernel NativeAOT build | PASS — SDK 10.0.401 fallback; ordinary payload built without a wait fixture. |
| Gate4 build | PASS — ordinary `Normal` scenario; no wait fixture enabled. |
| New Phase 81R QEMU proof | Not run — no scheduler changes were made, and existing code cannot prove a timer-driven timeout or the missing IRQ atomicity. |
| New timeout guest proof | Not run. |
| SyntheticScheduler rerun | Not run — production scheduler semantics were not changed. Prior Phase 69/72 reports record 3/3 SyntheticScheduler proofs. |

This is a documentation-only blocker result, not a Phase 81R acceptance. No tests or fixtures were added or enabled.

## Outcome and resume point

**Outcome C — existing scheduler event semantics can suspend an eligible TCB and wake it, but the current runtime cannot safely guarantee this phase's bounded, lost-wakeup-safe managed wait.** Supporting the contract requires, at minimum, an IRQ-safe atomic scheduler transition and a guaranteed timeout service integrated with the existing timer owner, plus a NativeAOT managed-stack/GC proof. The available scheduler surface must not be represented as a completed prerequisite until those are implemented and validated. No busy drain loop is an acceptable substitute.

The exact resume point is Phase 81R: define the generic wait result contract; serialize event registration, signal, timeout, and teardown against local IRQ capture; connect deadline service to a guaranteed timer source without taking IRQ0 from its owner; make invalid blocking context return a named status; then add a guest proof where a runtime-attached managed caller blocks, unrelated work runs, a native event wakes it, timeout resumes a second wait, and the managed caller survives a GC with stack references intact. Once Phase 81R is accepted, resume the original Phase 81 E1000 TX ownership/correlation work. Phase 81 remains unaccepted, and Phase 82 remains out of scope.

## Evidence and artifacts

New evidence was generated under `artifacts/` and remains ignored by repository policy:

- Event API host executable: `artifacts/event-api-host-tests/event-api-tests.exe` — SHA-256 `02D08DFD5245199EEE91A134055293E8E19E8BE018D7B7BC02CBDE8B2467926C`.
- Managed interrupt host assembly: `artifacts/managed-kernel-interrupt-host-tests/bin/ManagedKernelInterruptHostTests.dll` — SHA-256 `E0CA4933ACD540EBEE418B55540840766CB40F7DCB67A0187953479D65A909A7`.
- Service owner host executable — SHA-256 `A7A8A3280E26E0DFBF65FB28E032BFA5CCDC0B7066170CFD35A87E57CB06CF68`.
- Service status host executable — SHA-256 `3B2BCD88E8939C87B1B7FE5F7C7698F1B2BEE26A58123B4EB03A06B98D280883`.
- Phase 61–66 host executable — SHA-256 `C13E4A82A53CF08A7D9059095A7A5F8E3520E962B40FE19B2937CA7FFF7A9BC1`.
- ManagedKernel NativeAOT payload: 4,799,488 bytes — SHA-256 `47FE7D805896E5044D5FFB01BBF127A4C128A8814D680F3FBE4A1AFD88251B92`.
- Gate4 ordinary EFI: SHA-256 `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964`.

Prior lifecycle evidence remains in the Phase 69 and Phase 72 validation reports; it is not counted as a Phase 81R wait/timeout proof. No production source, managed source, test source, fixture configuration, branch, worktree, remote, credential, Git identity, or history was changed in this investigation. No prohibited Git operation was performed.

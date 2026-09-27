# guideXOS C# .NET 10 — Phase 68 Driver-Dispatch Service Boundary

Date: 2026-09-27

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

## Outcome

**Outcome A — persistent service boundary defined.**

The serial/interrupt path is a persistent managed service, not a sequence of independent managed jobs. Keep its event loop and managed driver state alive across hardware events. Keep the service boundary driver-specific and internal. The Phase 61 one-shot API remains a separate abstraction for bounded one-shot operations; its lower-level scheduler/runtime lifecycle functions can be shared internally.

This is an architecture decision only. No production source or test code changed, and no Phase 69 work was started.

## Git preflight and Phase 67 preservation

| Field | Live value |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD before Phase 67 preservation | `d3a8644041dda603fd82fa0aad696e4e89945c34` — `Document Phase 66 validation` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | `0 ahead / 0 behind` |
| Starting worktree | Only the untracked Phase 67 audit report |
| Phase 67 report | `docs/superpowers/validations/2026-09-26-phase67-first-managed-worker-api-caller.md` |

The Phase 67 report was the only worktree change. I read and reviewed it without editing it, staged only that report, and committed it as `79654dc5ff33f04f4e78ab581bc3294c29076003` (`Document managed worker caller audit`). A normal push was attempted and failed with `Permission denied (publickey)`. The local commit was retained; remotes and credentials were not changed. Phase 68 began at that preserved commit, one commit ahead of upstream.

Read in full before selecting the architecture:

- `docs/superpowers/validations/2026-09-25-phase61-bounded-managed-worker-api.md`
- `docs/superpowers/validations/2026-09-26-phase64-capacity-three-managed-worker-api.md`
- `docs/superpowers/validations/2026-09-26-phase65-bounded-managed-worker-cancellation.md`
- `docs/superpowers/validations/2026-09-26-phase66-post-gc-managed-worker-cancellation.md`
- `docs/superpowers/validations/2026-09-26-phase67-first-managed-worker-api-caller.md`

The audit also read `src/Gate4Harness/managed_kernel_driver_worker.c` and `src/ManagedKernel/ManagedDriverWorker.cs` in full, then traced the interrupt queue, scheduler event/wait, dispatch and teardown callers.

## Source-backed current architecture

| Responsibility | Source |
|---|---|
| Native worker loop/init/pump/stop/destroy | `src/Gate4Harness/managed_kernel_driver_worker.c`: `worker_entry`, `gxos_managed_kernel_driver_worker_initialize`, `gxos_managed_kernel_driver_worker_pump`, `gxos_managed_kernel_driver_worker_stop`, `gxos_managed_kernel_driver_worker_destroy` |
| Native worker state and limits | `src/Gate4Harness/managed_kernel_driver_worker.h` |
| Event queue/sequence/wake/drain | `src/Gate4Harness/managed_kernel_interrupt.c`: `enqueue_from_route`, `gxos_managed_kernel_interrupt_capture_route`, `gxos_managed_kernel_interrupt_rearm_work` |
| UART, keyboard, IDT, PIC/IOAPIC, source capture and EOI | `src/Gate4Harness/gate4_loader.c`: `managed_kernel_serial_interrupt_*`, `managed_kernel_keyboard_interrupt_*` |
| Wake callback and scheduler pump | `src/Gate4Harness/gate4_loader.c`: `managed_kernel_driver_worker_notify`, `managed_kernel_interrupt_wait_for_enqueued` |
| Managed persistent state | `src/ManagedKernel/ManagedDriverWorker.cs` |
| Driver service stages/state | `src/ManagedKernel/ManagedSerialDriver.cs`: `ManagedSerialDriverSubsystem.RunDriverWorker`, `RunPhase10` |
| Event validation/routing | `src/ManagedKernel/ManagedInterruptDispatcher.cs`: `TryDispatchBatch` |
| Shared TCB/stack/runtime lifecycle | `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.c` |
| Scheduler limits | `src/Gate4Harness/scheduler_foundation.h` |

### Current lifecycle

~~~text
native service owner initializes once
  -> create auto-reset wake event
  -> create suspended scheduler TCB/thread handle
  -> allocate guarded stack and GS/TEB/TLS environment
  -> prepare lifecycle and resume TCB
  -> first scheduler activation attaches NativeAOT once
  -> managed START creates persistent ManagedDriverWorker
  -> wait indefinitely on wake event
  -> hardware IRQ publishes records into native ring
  -> first pending record signals the wake event
  -> scheduler owner pumps the runnable worker
  -> managed dispatch drains/routes bounded batches
  -> worker rearms or yields, then waits again
  -> hardware unsubscribe and service stop request
  -> wake blocked worker, exit loop, detach once
  -> terminate TCB, close handle, collect TCB/stack, destroy event
  -> managed stop/destroy releases driver-owned allocations
~~~

The native context is initialized once for this service lifetime, not once per event. The boot-side owner creates it before subscribing serial. The worker is resumed before subscription; in the audited path, the first hardware notification causes the owner to pump the scheduler and the worker performs its first attach/start activation. Later events reuse that same worker. Initialization requires `FREE`; destroy ends in `DESTROYED`, with no reinitialize-after-destroy path.

The scheduler owner is the boot thread. IRQ handlers publish and signal; they do not run managed code or switch into the worker. `gxos_managed_kernel_driver_worker_pump` invokes `gxos_scheduler_main_dispatch` only from the boot thread. The worker waits with an infinite timeout and becomes runnable when the scheduler event is signaled.

### Native and runtime lifetime

| Resource/state | Lifetime |
|---|---|
| Worker context | One static service context, initialized/destroyed once |
| Scheduler TCB/thread handle | Created once; persists while waiting and dispatching; terminated/collected on shutdown |
| Stack | Guarded 64 KiB usable stack reservation persists while TCB is live |
| GS/TEB/TLS environment | Scheduler-owned per-TCB environment retained while waiting |
| Runtime `Thread*` / ThreadStore entry | Attached on first worker activation; stays attached across idle waits/events; FLS cleanup detaches on exit |
| FLS value/allocation context | Per-worker value/context remains associated with attached thread; cleanup clears FLS before reclaim |
| Managed driver graph | Static subsystem fields hold worker, dispatcher, serial driver and optional keyboard driver; static references root them across idle periods |
| Native queue | Fixed storage in interrupt context, independent of a dispatch |
| Explicit root ledger | Not used for driver objects. Phase 58/64 logical roots prove specific GC_CHECK operations, not persistent-object ownership |

No `ThreadStatic`, `ThreadLocal` or `AsyncLocal` state appears in the audited classes. NativeAOT TCB TLS/FLS is still required by the runtime lifecycle and differs from the explicit test root ledger.

### State that crosses events

| State | Owner | Classification | Reason |
|---|---|---|---|
| Worker state and dispatch/delivery/rejection counters | `ManagedDriverWorker` | Driver-session state | State transitions and counts span activations |
| Dispatcher, serial and optional keyboard references | Worker plus subsystem statics | Driver-session state | Every event routes through the same live objects; keyboard can join/leave while serial continues |
| Serial state, subscription, last sequence, receive count/byte | `ManagedSerialDriver` | Hardware/session state | Maintains subscription and monotonic receive tracking |
| Keyboard state, subscription, last sequence/scancode, make count/history | `ManagedKeyboardDriver` | Hardware/session state | Maintains scancode history and state across IRQs |
| Route subscriptions, queue indices, event sequence, pending/drop counters | Native interrupt context | Hardware/event-service state | Coordinates ISR producers and one managed consumer |
| Wake event, stop flag, native state/lifecycle/counters | Native worker context | Service-session state | Owns wake, stop, detach and reclaim |
| ABI descriptors/device identity | Subsystem/owner | Safely reconstructible config | Recreate at new service start, not per event |
| Four-record stack span and per-drain temporaries | Dispatcher call | Event-local | Rebuilt for each bounded drain |

Dispatch is fire-and-forget at the hardware boundary. Managed policy updates session state and counters; the IRQ caller receives no per-event result. The native callback status tells the persistent worker whether to continue or fail.

## Hardware event and wakeup contract

~~~text
UART COM1 IRQ4 / optional i8042 keyboard IRQ1
  -> native IDT handler and route source callback
  -> read bounded source bytes and publish fixed records in native ring
  -> assign sequence/update queue counters
  -> signal auto-reset scheduler event once while work_pending is set
  -> send PIC/APIC EOI
  -> scheduler owner pumps main dispatch
  -> attached worker drains at most four records per managed batch
  -> route records to serial or keyboard driver
~~~

| Question | Source-backed answer |
|---|---|
| Record format | 48-byte fixed event: ABI/version, kind/device, sequence, flags, one-byte payload, status and timestamp |
| Queue | One native ring shared by configured routes; capacity 8 |
| Capture/drain bounds | ISR attempts up to 4 source reads per route; managed drain is at most 4; worker allows up to 4 batches then yields |
| Accumulation | Events accumulate until drain, final-route unsubscribe clears them, or queue fills |
| Ordering | FIFO of successful enqueues; shared sequence identifies enqueue order across routes |
| Wake coalescing | Yes. `work_pending` changes clear-to-set once per work interval; auto-reset event can represent multiple queued records |
| Normal lost-wakeup defense | Consumer checks indices and clears `work_pending` under interrupt critical section; a later producer can signal, while earlier pending work is seen by the consumer |
| Notification failure | Failed signal clears `work_pending` but leaves the queued record; no independent retry guarantees a wake absent another notification/owner action |
| Backpressure | None to hardware |
| Overflow/loss | At depth 8 the byte is already read; enqueue increments `dropped_count` and drops the payload. No lossless guarantee |
| Acknowledgement | Capture and enqueue/drop happen before PIC/APIC EOI; managed dispatch is later |
| Duplicate dispatch | Drain advances the read index before routing; each drained record is delivered or rejected once, never replayed |
| Malformed record | Rejected and consumed; per-session rejected counter increments |
| Concurrent event | IRQ can publish while managed code runs; one managed worker serializes dispatch; critical sections protect queue indices |

Capacity 1 is insufficient: an IRQ can capture several records, serial and keyboard share the ring, and dispatch is bounded. Capacity 8 is the current bounded contract, not a no-loss promise. The service owner must surface overflow as a device/session error or expose loss accounting.

## Model comparison and selection

| Concern | Persistent managed worker (A) | Native service plus one-shot jobs (B) |
|---|---|---|
| Lifecycle | One TCB, runtime attachment and managed driver graph span events | New bounded request/worker lifecycle per event or batch |
| Latency path | Wake and scheduler-pump an already-attached worker | Adds admission, worker creation/attach, completion/result/close/reclaim |
| Managed state | Preserves driver sequences, subscriptions, keyboard history and service state | Recreating the managed object loses state; keeping it static still needs serialized ownership |
| Ordering | One FIFO consumer | Requires native serialization and waiting for each result; overlapping jobs risk reordered effects |
| Hardware ownership | Native side owns registers/queue/EOI; managed driver owns policy | Same native owner/queue is still required |
| Failure | Service-level failure, requiring owner stop/reclaim/restart policy | Per-job failures still need service retry/drop mapping |
| Resources | One permanent TCB, event, stack/environment and runtime attachment | Repeated setup/reclaim; one-shot API is capacity 3 and has no production DRIVER_DISPATCH operation |
| Cancellation | Permanent stop is separate from work cancellation | Phase 65/66 cancellation only applies at GC_CHECK checkpoints |

**Selected: Model A, kept as a driver-specific internal service.**

The source already has the appropriate lifetime shape: attach once, retain TCB/stack/runtime thread and managed state while idle, dispatch bounded batches, then stop/detach/reclaim. Per-event one-shot jobs add setup and result semantics that normal dispatch does not need while leaving the native ring and wake contract in place. No lifecycle timing benchmark exists; the selection follows source state ownership, not an invented latency number.

Expose only narrow internal start/status/stop operations and an opaque service identity. Never expose TCB, runtime `Thread*`, allocation context, logical root token or managed object pointer. A future identity can use bounded service slot + generation + device identity; keep the scheduler handle private.

## Phase 61–66 API reuse matrix

| Feature | Disposition | Reason |
|---|---|---|
| Request copy | Not used per event in Model A; leave unchanged for one-shot callers | Hardware events already enter the native ring as fixed records |
| Result copy | Not applicable to normal dispatch | Fire-and-forget processing needs service health, not a synchronous event result |
| Opaque handle | Reuse principle, not one-shot type | Service needs its own slot/generation/device identity if addressed externally |
| Generation | Reuse internally | Scheduler identity/generation protects TCB lifecycle; service generation rejects stale service commands after restart |
| Capacity 3 | Keep as one-shot policy | Persistent service owns a distinct bounded slot; both still share lower-level resources |
| Cancellation | Not reused for shutdown | GC_CHECK checkpoints do not stop an event loop |
| Runtime lifecycle | Reuse internally once per service lifetime | Existing helpers own stack, environment, attach, FLS/ThreadStore validation and detach |
| Root/GC | Keep static references; do not use diagnostic ledger | Two-slot ledger is not a persistent-object registry |
| Reclaim | Reuse internally at final stop | Reclaim occurs after service termination, not per event |
| Failure rollback | Reuse proven pieces and extend service ownership | Attach/wake/dispatch failure need owner policy and focused coverage |

The Phase 61–66 API is not itself the persistent-service primitive. The shareable layer is below it: stack allocation, TCB setup, GS/TEB/TLS/FLS, NativeAOT attach, ThreadStore/GC integration, detach and reclaim.

## Service identity, queue, shutdown and recovery

### Identity and queue

The current singleton has no caller-facing service handle. Internally it owns a scheduler handle and lifecycle slot/identity/generation, while the dispatcher owns route subscription IDs. Keep those private. A service value should include service slot, nonzero generation and device identity, never a raw TCB/runtime/managed pointer.

The event ring contract is fixed at capacity 8, one shared queue, FIFO order, one-byte payload, four-item managed drains, and explicit overflow count. No general queue framework is needed. The current software boundary cannot guarantee every UART/keyboard byte survives queue or hardware-buffer overflow.

### Shutdown

The current successful teardown order is: managed code unsubscribes/disables device route; if the final route, native unsubscribe clears queued work; managed worker enters Stopping; owner removes work-notification callback; native owner sets stop flag and signals; boot scheduler pumps up to 32 iterations; worker exits and detaches; owner closes/collects TCB and destroys event; managed stage destroys driver state and clears static references.

This is immediate discard after unsubscribe, not drain-before-stop. Final-route unsubscribe advances read to write and clears pending without increasing `dropped_count`. Future service semantics must select drain in FIFO order or discard with explicit accounting/reason. One-shot cancellation remains unrelated.

### Failure and recovery

| Condition | Current behavior | Required boundary rule |
|---|---|---|
| Malformed event | Rejected/consumed; worker continues | Count and report; never replay |
| Queue full | Drop payload after source read; increment drop counter | Surface overflow and choose device-specific recovery |
| Managed dispatch non-OK | Record failure, exit loop, detach if attached | Stop hardware, reclaim, owner decides restart |
| Wait/rearm/signal failure | Terminal worker failure; no independent supervisor | Report failure and reclaim only after termination/detach |
| Runtime attach failure | `worker_entry` records failure and skips detach when lifecycle `attached` is false; `destroy` then rejects because `detached` is false, so the current path cannot reclaim the terminated TCB/event | Add a lifecycle-owned cleanup path that distinguishes no-runtime attach from partial attach and reclaims exactly once |
| Device disappearance | No explicit hot-removal path; no source data simply yields no event | Add status/removal path before claiming hotplug |
| Shutdown while pending | Unsubscribe clears final queue; worker exits without drain | Define drain or accounted discard |
| Worker crash | No automatic restart/generation takeover | Disable route, join/reclaim, reconstruct managed state, advance generation, re-enable |

The attached-dispatch failure path has one detach owner and can reclaim after termination. There is a concrete pre-attach failure gap: if `gxos_nativeaot_scheduler_worker_attach` returns false before setting lifecycle `attached`, `worker_entry` reaches STOPPED without detach, and `gxos_managed_kernel_driver_worker_destroy` refuses to reclaim because `detached` is false. This leaves the scheduler TCB/stack and wake event allocated. The correct repair must account for a callback that may have partially changed runtime state; blindly freeing scheduler resources is not safe. This report records the defect but does not change production code because the ownership transition and rollback need a focused design and proof.

Automatic restart is not safe today: the native context cannot be initialized after DESTROYED, managed state may be partly advanced, pending records may have been consumed/rejected/discarded, and hardware/subscription state needs explicit teardown and reinitialization. Recovery must be owner-controlled with a new generation.

No concrete production repair was made. These are boundary limitations, not a reproduced dispatch defect.

## Capacity and runtime cost

The live contracts are baseline 13 scheduler objects of 16, 6 TCB slots, and 2 baseline threads. One persistent driver worker needs one TCB/thread object plus one separate event object: two scheduler-object slots total. It also holds a guarded 64 KiB stack reservation (17 pages including guard), two VM-region descriptors, a canary page, four GS/TLS/TEB environment pages and one attached runtime ThreadStore entry while active. The serial/keyboard managed arenas have their own driver-owned allocations.

Phase 64 proved the three one-shot workers fill the three scheduler-object slots free at the 13-object baseline. Arithmetic shows service + three jobs would require 18 objects, above the 16-object table. Service + one additional one-shot worker reaches 16; TCB arithmetic for service + three jobs is exactly 6. This coexistence has not been tested and is not a supported admission promise. The persistent service does not consume one of the API's three records but shares TCB/object/VM/runtime capacity.

Track persistent-service slots separately from one-shot API records and enforce joint lower-layer admission or reserve resources. Do not promise three simultaneous one-shot workers alongside this service without increasing capacity and proving it. The current combined serial/keyboard queue warrants one persistent service slot; no general-N service count is justified. The worker remains blocked while idle, not runnable or recreated. Phase 68 changes no scheduler policy.

## Result semantics and Phase 69 recommendation

Normal dispatch does not need an event-specific result. It needs bounded records, ordered consumption, state updates and a service health outcome. A one-shot DRIVER_DISPATCH operation is not recommended for this worker. A future stateless caller, if found, would need a fixed by-value event payload (ABI version, device/source, kind, sequence, flags and bounded data), with no callbacks or long-lived pointers.

The smallest Phase 69 step is an internal persistent service owner around the current worker: add an opaque slot/generation/device identity and bounded service slot; centralize start, terminal status, stop, unsubscribe plus drain/discard, detach and reclaim; then add focused failure-path coverage for startup/attach, wake, dispatch, overflow and pending-work shutdown. Preserve the existing ring, managed state graph and low-level lifecycle helpers. Keep one-shot records separate while enforcing shared lower-layer limits. Do not begin this in Phase 68.

## Validation

Documentation only; no production code, tests, runners, scheduler policy, ABI or worker API behavior changed.

- `tools/Run-Phase61ManagedWorkerApiHostTests.ps1` passed Phase 61, 62, 64, 65 and 66 host markers.
- `tools/Build-Gate4Harness.ps1 -PayloadMode ManagedKernel -EnableNativeAotStartup` passed. Managed payload: 4,781,568 bytes, SHA-256 `96CA2273FB1D14A13596858A2D536EF8622469595883B61BE2EEBB75324A7A4C`.
- QEMU not run; no production behavior changed. Prior accepted guest regressions are recorded in Phases 61–67.

No branch switch, worktree creation, reset, stash, rebase, history rewrite or force-push occurred. Phase 67's preservation commit is `79654dc5ff33f04f4e78ab581bc3294c29076003`; push failed with SSH `Permission denied (publickey)`, with remotes/credentials unchanged. Commit Phase 68 separately as `Document driver dispatch service boundary` and attempt push once; retain local commits on the same SSH error.

Remaining unproven areas: simultaneous service plus three one-shot workers, overflow recovery, a safe repair for pre-attach failure reclamation, hotplug, bounded restart, and timing-based latency. The proposed boundary is limited to the existing single-service topology and scheduler capacities.

# Phase 55 — Managed Worker Ownership Contract and Controlled Failure-Injection Design Audit

Date: 2026-09-20  
Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`  
Branch: `nativeaot-managed-kernel-integration`  
Outcome: **Outcome B — concrete ownership defect found and repaired**

## Executive conclusion

The live implementation has one authoritative ownership record:
`GXOS_NATIVEAOT_SCHEDULER_THREAD_LIFECYCLE`. It records the scheduler slot,
worker identity and generation, scheduler/stack/environment/TLS-FLS/VM
ownership flags, runtime `Thread*`, allocation context, callback evidence and
the published lifecycle state. The scheduler remains the authority that
creates, terminates, reclaims and reuses the TCB and its scheduler-owned
resources. The NativeAOT runtime remains the authority for its runtime
`Thread*`, ThreadStore membership, managed execution and runtime FLS cleanup.

The Phase 54 success path is coherent. Its public ledger sequence is accurate:

```text
Free → Allocated → Runnable → Running → RuntimeAttached →
DetachPending → RuntimeDetached → Reclaimable → Reclaimed
```

There are two bounded production repairs in this phase:

1. Lifecycle operations now validate the recorded scheduler slot, identity and
   generation against the current TCB before mutating or reclaiming through a
   lifecycle record. Before this repair, a stale raw lifecycle record could
   address a reused TCB slot without an equivalent generation check.
2. Managed worker initialization now uses the existing
   `gxos_scheduler_discard_created_thread` authority when `prepare`, resume,
   or runnable publication fails. Closing the public handle and calling
   `collect` alone cannot reclaim a `CreatedSuspended` or `Runnable` TCB.

No failure-injection framework was implemented. The smallest safe Phase 56
surface is defined below.

## Preflight and source authority

The requested repository, branch, starting HEAD and subject matched the live
repository. The requested divergence did not: the prompt expected `1 ahead /
0 behind`, while the live repository was `0 ahead / 0 behind` at preflight.
The worktree was clean.

| Item | Live value at preflight |
| --- | --- |
| Starting HEAD | `92e4c7aeb6dc45d9c6b5847c51d05b458c64d3d2` |
| Starting subject | `Add managed worker ownership ledger` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | `0 ahead / 0 behind` |
| Starting worktree | Clean |

The Phase 53 closure report and Phase 54 ownership report were read before
editing. Source authority was traced in:

- `src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.h/.c`
- `src/Gate4Harness/scheduler_foundation.h/.c`
- `src/Gate4Harness/gate4_loader.c`
- `src/Gate4Harness/managed_kernel_driver_worker.c`
- `src/Gate4Harness/nativeaot_callback_bridge.c/.h`
- `docs/NATIVEAOT_GC_SCHEDULER_THREAD.md`

## Current architecture

`gxos_scheduler_create_suspended_thread` owns the initial construction. It
finds a free scheduler TCB slot, zeros the TCB, creates a generation-bearing
thread object record, assigns a monotonic worker identity, allocates the
scheduler stack reservation and usable pages, establishes the non-present
guard page, allocates the GS/TEB/TLS environment and returns a public thread
handle in `CreatedSuspended` state.

`gxos_nativeaot_scheduler_worker_prepare` then snapshots those resources into
the lifecycle record and installs the PE TLS block at the loaded TLS index. It
publishes `Free → Allocated`. Scheduler resume changes the TCB to `Runnable`,
after which the ledger publishes `Allocated → Runnable`. Dispatch changes the
TCB to `Running`, after which the worker entry publishes `Runnable → Running`.

The first managed callback performs the actual NativeAOT attach. On return,
the ledger observes the worker FLS value, runtime state, transition frame,
runtime stack bounds and ThreadStore count, then publishes
`Running → RuntimeAttached`. This means the abstract state sequence is
accurate as a ledger-publication sequence, but `RuntimeAttached` is committed
after the callback returns and the attach evidence is captured; it is not a
state published before entering the callback.

The worker may perform the managed GC probe and a subsequent managed callback
while `RuntimeAttached`. Detach first publishes `RuntimeAttached →
DetachPending`, invokes the established runtime FLS cleanup callback, verifies
the detached runtime state and zero allocation pointers, clears the scheduler
FLS value, and publishes `DetachPending → RuntimeDetached`.

The scheduler terminates the worker. The ledger accepts `RuntimeDetached →
Reclaimable` only when the TCB is terminated, not current, has zero execution
references, is not queued and has no wait record. The scheduler closes the
public handle, collects the terminated TCB, frees the stack VM and environment,
releases the object record and zeros the TCB. The ledger then publishes
`Reclaimable → Reclaimed`.

## Authoritative ownership table

| Resource | Creator and initial owner | Ownership transfer / active owner | Cleanup authority and reclaim condition | Reuse, locality and partial-failure rule |
| --- | --- | --- | --- | --- |
| Scheduler slot | Scheduler free-slot search; scheduler owns the slot | TCB becomes live and the slot is owned by the worker generation | Scheduler `maybe_reclaim_thread` zeros the TCB only after termination, zero refs, no queue/wait record, intact canaries and successful stack release | Slot is scheduler-global but the active contents are generation-local; reuse is legal only after TCB zeroing |
| TCB | `create_suspended_thread` zeros and initializes it | Scheduler owns it for the complete TCB lifetime | Scheduler reclamation zeros the TCB; lifecycle `note_reclaimed` verifies the zeroed fields | Raw TCB pointers are unsafe after reclaim; all handle-based destructive operations use object generation |
| Worker identity | Scheduler `next_identity` counter | Immutable identity for the TCB lifetime | No separate free; it becomes invalid when the TCB is zeroed | Monotonic diagnostic identity; paired with generation for lifecycle validation |
| Generation | Thread object allocation preserves and increments the object generation, skipping zero | The TCB and object record carry the same generation | Object record is released only during TCB reclaim | Generation-local and generation-safe across slot reuse; stale handles reject |
| VM reservation | `memory_allocate_scheduler_stack` reserves from the scheduler VM arena | Scheduler stack allocator owns the reservation | `memory_free_scheduler_stack` must unmap/decommit pages, unregister usable/guard regions and release the reservation before TCB reclaim | VM arena/region ledger is process/kernel-global; reservation and region identities are worker-generation-local |
| Usable stack | Stack allocator commits the usable pages | Scheduler owns the usable range and TCB stack contract | Same stack-free operation; reclaim stops if it returns failure | Worker-local; no runtime or managed component frees it |
| Guard page | Stack allocator reserves the first page and verifies it non-present | Scheduler owns the guard mapping and diagnostic region record | Unregister occurs during stack release; the page must remain non-present while active | Worker-generation-local; not a separately reclaimable allocation |
| Saved RSP and context | Scheduler initializes the TCB context after stack/environment construction | Scheduler owns the switch context; lifecycle records the initial RSP for evidence | TCB zeroing removes it; no independent free | Worker-local and invalid after TCB reuse |
| GS state and TEB | Scheduler environment allocator creates GS, TLS vector, TLS block and TEB pages | Scheduler owns all four pages and initializes identity/stack fields | `free_thread_environment` frees the pages and clears the fields | Worker-local; repeat clearing of the same live TCB is harmless, but stale raw pointers after reuse are not valid |
| TLS vector/block | Scheduler environment allocator creates them; prepare writes the PE TLS block into the loaded TLS slot | Per-TCB scheduler state is active for the worker generation | Environment cleanup frees both pages; vector values must be irrelevant before TCB reuse | Vector/block are worker-local; the PE TLS index is shared loader/runtime metadata |
| FLS slot ID | NativeAOT/runtime FLS allocator and callback table allocate it globally | The slot ID is process/runtime-global; scheduler stores per-TCB FLS values | Runtime callback table and `platform_fls_free` own slot destruction | Slot ID is shared, not worker-local; it must not be treated as a per-worker allocation |
| FLS value | NativeAOT attach publishes the runtime `Thread*` into the current TCB's FLS value | Runtime owns the value while attached; scheduler owns the per-TCB storage | Runtime FLS cleanup detaches runtime state; scheduler then sets the per-TCB value to zero | Value is worker-generation-local even though slot ID and callbacks are global |
| NativeAOT runtime `Thread*` | Created/attached by the generated reverse-P/Invoke path | NativeAOT runtime owns ThreadStore membership, runtime state and detach semantics | `runtime_fls_cleanup` is the only detach authority; ledger never frees the pointer | Runtime-global structures point at a worker-local runtime thread; its FLS value and lifecycle generation must not cross reuse |
| Allocation context | NativeAOT stores allocation limit/pointer fields in the worker runtime thread/TLS block | Runtime owns bump-pointer state; ledger records addresses and before/after values | Runtime detach clears the worker allocation fields; scheduler later frees the backing TLS/stack | Worker-generation-local pointer; main allocation context is shared baseline evidence, not worker ownership |
| Managed worker object/root | Managed callback/runtime creates or keeps the managed object/root; no native root pointer is persisted by this harness | Managed runtime/GC owns the managed object; the ledger records callback and GC survival evidence only | Managed callback return and runtime detach end the worker-local execution/root scope; no kernel `free` exists for it | Managed object is runtime-global in GC machinery but the active root/evidence is worker-local; never equate `managed_worker_object_owned` with a native allocation handle |
| Callback bridge/registration | Loader registers global callback bridges and marks them ready | Callback function pointers, readiness and invocation counts are shared runtime/process state | No per-worker deregistration; worker teardown only detaches runtime FLS state | Shared callback table; ledger bit `callback_registration_observed` is evidence, not ownership of the table |
| Lifecycle/ownership record | Probe or driver context owns the record and calls `prepare` | Record is the authoritative snapshot for one worker generation while state is not `Free`/`Reclaimed` | Record transitions to `Reclaimed`; the next `prepare` may reset it only for a new generation | Record is worker-generation-local diagnostic state; slot/identity/generation matching prevents stale mutation |
| Handle and reclaim metadata | Scheduler object table creates the public generation-bearing handle; TCB tracks public/execution refs, queue, wait and state | Scheduler owns object-table and refcount metadata | Close drops public refs; termination drops execution refs; collect reclaims only when all conditions hold | Handle lookup is generation-safe; raw TCB/reclaim metadata is unsafe after reuse |

## Point of no return and exact ownership changes

| Stage | Point of no return | Required owner after the point | Failure consequence and authority |
| --- | --- | --- | --- |
| Slot acquisition | TCB is zeroed and `live` is set | Scheduler owns the slot/TCB | Before object publication, clear the TCB. After object publication, release the object record as well |
| Object/generation assignment | Object record is live and the handle/generation are assigned | Scheduler owns object record, handle refs and TCB | Any later create failure must release the object and return the identity counter to its prior value, as current create rollback does |
| VM reservation | `reservation_base` and reservation slot are recorded | Scheduler stack allocator owns the reservation | Free through the stack contract; do not clear the contract before the allocator has released its resources |
| Usable/guard establishment | Usable pages are committed or a VM region identity is registered | Scheduler VM allocator owns each resource reached | The allocator has internal rollback for commit/query/register failures; a failure in `memory_free_scheduler_stack` is not currently resumable/idempotent and must block reclaim |
| Environment | All four environment pages are allocated and `environment_owned` is set | Scheduler owns GS, TLS vector, TLS block and TEB | Free the environment pages and clear all pointers/flag; no NativeAOT detach is needed |
| Lifecycle prepare | `Free/Reclaimed → Allocated` is published | Lifecycle record and scheduler still own all pre-runtime resources | A failed prepare after this point must discard the created TCB, not merely close the handle and collect |
| Runnable publication | Scheduler resume has made TCB `Runnable` and queued | Scheduler owns the queued TCB; runtime is not attached | Remove/terminate through `discard_created_thread` if publication fails; `collect` alone is insufficient |
| Running publication | Dispatch makes TCB `Running` and current | Scheduler owns the currently executing worker; it cannot be reclaimed in place | Return a failure to the scheduler, terminate the worker, then reclaim from the main/other thread |
| Runtime attach | Callback returns OK, current FLS is nonzero and capture validates runtime state/stack/ThreadStore | NativeAOT owns runtime thread/ThreadStore state; scheduler owns TCB and FLS storage | Detach exactly once from the current FLS value before scheduler reclaim |
| Managed-root/GC evidence | GC callback returns with nonzero collection delta and root-survival evidence | Managed runtime/GC owns managed object state; no native root pointer is added | End the callback scope, then perform runtime detach; do not free native worker storage while a callback is executing |
| Detach | `RuntimeAttached → DetachPending` is published before calling runtime cleanup | Runtime cleanup is in flight; duplicate detach is illegal | Finish the established runtime callback, verify state 2/zero allocation pointers/ThreadStore baseline, clear scheduler FLS, then publish RuntimeDetached |
| Reclaimable | TCB is terminated and all scheduler preconditions are true | Scheduler is the only reclaim authority | Close handle, collect, verify TCB/VM/environment zero, then publish Reclaimed |
| Reuse | TCB is zeroed and object generation is preserved for the next allocation | Scheduler owns the new generation | Old handle or lifecycle record must fail slot/identity/generation validation |

## Lifecycle state contract and rollback

| State | Definitely exists | May exist | Must not exist | Legal cleanup / duplicate rule |
| --- | --- | --- | --- | --- |
| `Free` | No live lifecycle ownership | None | No worker-owned TCB resources | No cleanup; prepare is the only entry |
| `Allocated` | Live TCB, object/handle, stack VM, environment, TLS vector/block and ledger snapshot | Per-TCB FLS placeholder/TLS values | Runtime `Thread*`, managed callback attachment and managed root evidence | Close handle, discard created TCB and collect; a second prepare is rejected unless the record is reset |
| `Runnable` | All `Allocated` resources plus runnable queue membership | Scheduler event/wait metadata elsewhere in the scheduler | Runtime `Thread*` | Remove/terminate through `discard_created_thread`; duplicate resume is a no-op only for the same valid handle, while duplicate lifecycle publication is rejected |
| `Running` | All pre-runtime resources and current TCB | Runtime attach may be in progress inside the callback | Reclaimable storage | Worker must finish or report failure to scheduler; main must not free the current TCB |
| `RuntimeAttached` | Runtime `Thread*`, nonzero worker FLS value, ThreadStore membership and captured runtime stack/allocation evidence | Managed root/object and GC state | Detached runtime state or zero worker FLS | Runtime detach is mandatory and exactly once; duplicate detach is rejected |
| `DetachPending` | TCB and runtime cleanup context; detach has already begun | Runtime fields may be transitioning | A second detach request | Only the in-flight runtime cleanup completion path is legal; current code has no generic retry transition |
| `RuntimeDetached` | TCB, stack VM, environment, TLS storage and scheduler object/handle | Cleared runtime metadata may remain in the ledger as evidence | Runtime `Thread*` ownership, nonzero worker FLS, attached runtime state | Terminate/reclaim; duplicate detach is rejected |
| `Reclaimable` | Terminated TCB and all scheduler resources still awaiting reclaim | Ledger retains addresses/identities for verification | Current/queued/waiting/execution-referenced TCB | Close handle, collect and verify; a second note-reclaimable is rejected |
| `Reclaimed` | Ledger history and generation evidence only | The record can retain old diagnostic values | Live TCB, stack/environment/VM/object/handle | Prepare may reset for a new generation; old handles and old-generation destructive operations must fail |

The source has additional internal partial-construction states that are not
published as lifecycle states: reservation-only stack, committed usable stack,
guard-verified stack, guard-region-registered stack, usable-region-registered
stack, partially allocated environment, live TCB before object completion and
`DetachPending` with runtime cleanup incomplete. The stack allocator attempts
to roll these back internally and zeros the contract on its own failure paths.
The caller must still treat a false stack-free result as “not reclaimed.”

## Rollback findings

The original managed-driver initialization cleanup was incomplete. On failure
after a TCB was created, it performed `close_handle` and `collect`. Scheduler
`maybe_reclaim_thread` requires `Terminated`, so this did not reclaim a TCB in
`CreatedSuspended` or `Runnable`. The repair adds a small helper that closes
the handle, calls `discard_created_thread` for exactly those two states, and
then collects. It does not discard a running worker.

The lifecycle probe itself has diagnostic early returns without a production
rollback helper. That is acceptable for the current successful probe but is a
reason Phase 56 must inject only at a boundary with an explicit caller-owned
rollback path. The first executable failure test must use the repaired
discard path rather than relying on `collect`.

## Cleanup idempotence requirements

| Operation | Current semantics | Required Phase 56 treatment |
| --- | --- | --- |
| Lifecycle transition | Exact adjacent transition only; invalid or duplicate transition increments failure and returns false | Call once; never “retry” by forcing a state |
| Runtime detach | One successful attach requires one runtime FLS cleanup; duplicate detach is explicitly rejected | Exactly once, generation/current-thread checked |
| FLS value clear | Scheduler per-TCB value is cleared after runtime cleanup | Safe to repeat only as a defensive zeroing operation; never use it as a substitute for runtime detach |
| FLS slot free/callback table | Shared runtime operation; callback can run when a global slot is freed | Not a worker-local cleanup and must not be freed per worker |
| TLS/environment free | `free_thread_environment` clears the four pointers and ownership flag | Once per TCB generation; same-TCB repeated clearing is harmless, stale raw TCB use after reuse is not |
| Stack/VM release | `memory_free_scheduler_stack` performs page-by-page unmap/decommit and region/reservation release | Treat as once-only until it returns success; a partial failure currently leaves no safe generic retry contract |
| Managed-root removal | No native root pointer or native free exists in this harness | Managed callback scope/GC/runtime detach owns it; do not invent a native free |
| Handle close | First close drops public refs; duplicate and stale handles are rejected by generation-aware lookup | Once only; do not close an old handle after slot reuse |
| Created/runnable TCB discard | `discard_created_thread` removes queue membership, terminates and attempts reclaim | Use once after handle close; do not use after the TCB has run or after slot reuse |
| Scheduler collect | Repeated scans are safe, but it cannot reclaim non-terminated TCBs | Call after the correct state transition, not as a universal rollback |
| Ledger reset | `prepare` may reset only `Free` or `Reclaimed` records | Do not reset an `Allocated`/attached record to hide leaked ownership |
| Stale-generation invalidation | Object handle lookup rejects old generation; patched lifecycle paths reject slot/identity/generation mismatch | Required before any lifecycle mutation, detach, or reclaim evidence is accepted |

## Generation and ABA audit

Scheduler object handles already encode object type, slot and generation.
`lookup_object` validates the generation and live bit, and object release
preserves the generation so a subsequent allocation increments it. This
protects normal handle close, resume, event and thread lookup operations.

The lifecycle record separately stored `scheduler_slot`, `worker_identity` and
`worker_generation`, but its transition/attach/invoke/reclaim paths previously
trusted the stored raw TCB pointer. After a slot was reclaimed and reused, a
stale record could therefore be pointed at a new live TCB. The new
`lifecycle_matches_current_thread` check requires all three values to match
the current TCB before lifecycle mutation or destructive observation. This is
the concrete ABA defect repaired in Phase 55.

The audit found no remaining demonstrated path in which a valid generation-
checked handle detaches a newer worker, frees its stack, clears its FLS value,
removes its managed-root evidence, releases its VM allocation or reports a
false handle success. Raw TCB pointers and copied lifecycle records remain
unsafe by design after reclaim; the generation check is the required boundary.

## Shared versus worker-local state

### Runtime-global

- NativeAOT callback entry points and managed callback bridge readiness.
- NativeAOT ThreadStore and GC state.
- The runtime FLS slot ID and its callback table.
- Runtime TLS field layout and the fixed fields used for attach evidence.

### Process/kernel-global

- The scheduler instance, TCB array and object table.
- The VM arena, paging state and VM region ledger.
- Global FLS slot allocation state and callback arrays.
- Global callback bridge records and invocation counters.
- Scheduler-wide resource counts and baseline accounting.

### Scheduler-global

- Current/boot thread pointers.
- Runnable queue and runnable count.
- TCB/object lookup and public/execution reference accounting.
- Scheduler stack allocator callbacks and reclamation scan.

### Worker-local

- One TCB's stack reservation, usable stack, guard contract and saved context.
- GS, TEB, TLS vector, TLS block and per-TCB FLS values.
- One worker's NativeAOT runtime `Thread*`, allocation context and captured
  runtime stack bounds.
- One lifecycle record, worker handle identity and managed callback evidence.

### Generation-local

- TCB contents, object handle generation, stack/guard/usable VM identities,
  GS/TEB/TLS addresses, FLS value, runtime `Thread*`, allocation context and
  lifecycle record snapshot.
- Any managed worker execution/root evidence associated with the active
  callback generation.

FLS slot IDs, callback tables, VM allocator state, GC globals and run queues are
not worker-local. FLS values, TLS storage, runtime thread pointers and
allocation contexts are worker-local even though they are reached through
shared runtime or scheduler structures.

## Selected failure-injection boundaries

The minimum useful diagnostic partition is four boundaries. The stack
allocator's internal commit/guard/region failures are not separate hooks in
this phase because that allocator already attempts transactional rollback and
faulting NativeAOT internals would add unrelated branches.

| Proposed stable point | Why it is selected | Owner at injection |
| --- | --- | --- |
| `AFTER_WORKER_PREPARE` | Covers slot/TCB/object/VM/stack/environment/TLS ownership before any runtime attach | Scheduler/kernel only; runtime `Thread*` must not exist |
| `AFTER_RUNTIME_ATTACH` | Separates successful NativeAOT attach, worker FLS and ThreadStore ownership from pre-runtime construction | NativeAOT owns runtime thread state; scheduler owns the TCB and per-TCB FLS storage |
| `AFTER_MANAGED_ROOT_EVIDENCE` | Exercises the GC/root-survival observation without inventing a native root pointer | Managed runtime/GC owns managed object state; kernel still owns native worker storage |
| `AFTER_RUNTIME_DETACH` | Separates runtime cleanup from final scheduler reclaim and slot reuse | Scheduler owns the remaining terminated worker resources; runtime ownership is gone |

The first and fourth points are sufficient to test the broad rollback split.
The middle two are retained because attach and managed-root lifetime are the
highest-risk ownership handoff and must not be conflated in the first failure
experiment.

## Proposed diagnostic hook design

Implement later, only in a diagnostic Gate4 build, as a small lifecycle-local
surface:

- Compile-time gate: `GXOS_ENABLE_PHASE55_FAILURE_INJECTION`.
- Stable enum IDs for the four selected boundaries, with `NONE` and a single
  `ARMED`/`FIRED` diagnostic record.
- Static storage only; no heap allocation and no NativeAOT runtime changes.
- Configuration includes the requested injection ID and, once armed, the
  expected scheduler slot, worker identity and generation.
- The hook fires once only when all generation fields match the active
  lifecycle record. A second matching opportunity reports “already fired” and
  does not fail again.
- The hook returns an explicit diagnostic status distinct from a genuine
  callback/runtime failure.
- It emits a serial marker containing the stable ID, slot, identity and
  generation before returning failure.
- It is placed immediately before or after the kernel-owned operation named by
  the ID. It never changes NativeAOT internals, timing, scheduling or the
  runtime callback implementation.

No hook, enum or diagnostic build switch was added in Phase 55. The design is
intentionally small enough that each failure has one caller-owned rollback
path and cannot be confused with a spontaneous runtime error.

## Expected cleanup at each selected boundary

### `AFTER_WORKER_PREPARE`

Expected failure state and evidence:

- No runtime `Thread*`, ThreadStore membership, managed root or worker FLS
  value.
- TCB is discarded using `close_handle` followed by
  `discard_created_thread`, then collected.
- Stack reservation, usable pages, guard VM record, GS/TEB/TLS pages and
  scheduler object record are released.
- VM region count, live-thread count and live-object count return to the
  captured baseline.
- Old handle lookup rejects; the next successful allocation receives a new
  generation.

### `AFTER_RUNTIME_ATTACH`

Expected failure state and evidence:

- The active worker remains current until it returns the controlled failure;
  no in-place free is permitted.
- `DetachPending` is entered once, the runtime FLS cleanup callback runs once,
  and runtime state becomes detached.
- Worker FLS is zero, allocation pointers are zero, and ThreadStore count
  returns to the pre-attach baseline.
- Managed callback/root scope has ended; no kernel root pointer remains.
- The worker then terminates and follows the normal `RuntimeDetached →
  Reclaimable → Reclaimed` path.
- Duplicate detach is rejected, not retried as a second runtime detach.

### `AFTER_MANAGED_ROOT_EVIDENCE`

Expected failure state and evidence:

- GC collection delta and root-survival evidence have already been observed.
- The managed root is not separately freed by the kernel; callback return and
  runtime detach end the managed execution scope.
- Runtime detach occurs exactly once, followed by zero worker FLS, zero
  allocation pointers, restored ThreadStore count, scheduler termination and
  complete native reclaim.
- No post-reclaim managed-root evidence is accepted for the reused generation.

### `AFTER_RUNTIME_DETACH`

Expected failure state and evidence:

- Runtime state is already detached, worker FLS is zero and no second detach
  is attempted.
- The remaining scheduler-owned TCB/stack/environment/object resources are
  terminated and reclaimed through the ordinary reclaim path.
- `note_reclaimable` succeeds only after the exact termination invariants are
  true; `note_reclaimed` succeeds only after TCB fields and VM identities are
  zero.
- Baseline resource counts return and stale-handle lookup rejects before a
  new generation can reuse the slot.

## Assertion audit

| Assertion class | Current examples | Audit result |
| --- | --- | --- |
| Architectural invariant | Guard page non-present; stack/RSP bounds; GS/TEB/TLS coherence; worker FLS equals runtime thread; exact lifecycle adjacency; termination/reclaim preconditions | Retain. These describe ownership and safety, not current worker count |
| Diagnostic assumption | NativeAOT TLS offsets, runtime thread at TLS block plus `0x30`, allocation context at plus `0x38`, ThreadStore list shape and bounded traversal | Retain as version-matched diagnostic assumptions; do not generalize them into a portable runtime ABI |
| Test-fixture expectation | Two workers A/B, 12 cycles, inputs 7/9 and 9/9 seed conventions, fixed harness capacities | Retain in Phase 54 fixture; parameterize before general threading |
| Baseline-relative resource assertion | Capture VM/thread/object baseline, permit peak growth, require final equality | Preferred ownership/leak assertion; retain |
| Temporary forensic assertion | Raw addresses, high-water RSP, VM ledger record dumps and callback invocation counts | Useful evidence, but not architecture; do not use as expansion blockers |

The accepted absolute baseline values `VM=0x3`, live threads `0x2` and live
objects `0xD` are durable values for this harness configuration, not universal
threading invariants. The Phase 54 peak values `0x7/0x4/0xF` are likewise
fixture-relative. A future multi-worker phase must compare to a captured
baseline and expected delta rather than hard-code two-worker totals.

Two-worker-specific checks (`worker_a`, `worker_b`, pairwise address
inequality, fixed two dispatch targets and fixed reuse comparison) are valid
for Phase 54 but will need a loop/table form for more workers. They do not
indicate that shared FLS slot IDs, callback tables, VM allocator state or GC
globals are erroneous aliases.

## Concrete defects and production changes

### Defect 1 — stale lifecycle record could cross a reused slot

The lifecycle record already carried slot, identity and generation, but the
transition, attach, invoke and reclaim checks trusted the raw TCB pointer. A
reused slot could therefore be addressed through a stale lifecycle record.
The bounded repair adds slot/identity/generation matching to lifecycle
mutation and active-thread checks, plus reclaim/destructive observation paths.

### Defect 2 — early worker initialization cleanup could leak a pre-runnable TCB

`managed_kernel_driver_worker_initialize` closed the worker handle and called
`collect` when `prepare`, resume or runnable publication failed. The scheduler
collector deliberately refuses non-terminated TCBs, so a `CreatedSuspended` or
`Runnable` TCB could retain its object, VM, stack and environment. The bounded
repair invokes the existing `discard_created_thread` transition after closing
the public handle, then collects. It is limited to failure before worker
execution begins.

No other production repair was made. In particular, stack-free partial failure,
runtime attach partial failure and detach-callback failure remain documented
design boundaries for controlled diagnostics; no speculative NativeAOT or VM
refactor was introduced.

## Validation

The final patched source used the accepted managed payload:

```text
Payload size: 730112
Payload SHA-256: AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012
```

Focused validation passed:

- Changed C sources compiled with `gcc -std=c11 -Wall -Wextra -Werror`:
  `nativeaot_scheduler_thread_lifecycle.c` and
  `managed_kernel_driver_worker.c`.
- Scheduler model host tests: `PASSED checks=256`.
- Scheduler durability host test: `PASS`.
- Scheduler stack/VM host test: `PASS`.
- VM substrate host tests: `PASSED`.
- Managed kernel driver worker host tests: `PASSED`.
- Phase 53 lifecycle regression: 3/3 fresh boots passed with the final
  patched lifecycle-only Gate4 build.
- Phase 54 ownership regression: 3/3 fresh boots passed, 12 cycles per boot,
  with `PHASE54_PASS=1`, `MANAGED_WORKER_OWNERSHIP_OK=1`, peak
  `VM/threads/objects = 0x7/0x4/0xF`, and final baseline
  `0x3/0x2/0xD` in every serial log.

The final Gate4 EFI hashes were:

- Phase 53 lifecycle-only gate:
  `6D0F45F93B9936ACB800A14E4B5A14CD6F1D06321BB01ABACE62990054DEE482`.
- Phase 54 ownership gate:
  `8F8C0A7A90D5563E8CE6D66BDFF1238034C3A4FD8F4BEECC289F3E8FC99A827F`.

The initial exploratory build without `-AssumeUnspecifiedTimezoneUtc` stopped
at the known `TIME_INVALID_TIMEZONE` harness boundary and was not counted.
The final validation used the accepted timezone setting. No broad boot soak
was run.

## Recommended smallest Phase 56 experiment

Inject one single-shot failure at `AFTER_WORKER_PREPARE`, before resume or
runtime attachment. Use the repaired `close_handle → discard_created_thread →
collect` path, then prove all of the following against a captured baseline:

1. no runtime `Thread*`, worker FLS value, managed-root evidence or ThreadStore
   increment exists;
2. VM, live-thread and live-object counts return to baseline;
3. the TCB, stack/guard, GS/TEB/TLS and object record are cleared;
4. the failed generation's handle is rejected; and
5. a subsequent worker reuses the slot only with a new generation.

Do not begin the runtime-attach failure until this pre-runtime rollback test
is green. Do not begin Phase 56 in this Phase 55 change.

## Acceptance

Phase 55 is accepted as Outcome B. Before intentional managed-worker failure is
introduced, every resource now has a documented owner, cleanup authority,
reclaim condition and generation-safe path back to the captured baseline. The
remaining failure hooks are explicitly designed for a later diagnostic build,
not ordinary production behavior.

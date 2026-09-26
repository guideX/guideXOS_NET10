# guideXOS .NET 10 — Phase 63 managed-worker capacity contract

Date: 2026-09-26
Outcome: **Outcome C — capacity cannot safely be generalized yet**

## Decision

The live repository remains a bounded two-worker implementation. The audit found
no need to change production behavior for the accepted Phase 62 workload, but it
also found that the API capacity constant is not the only capacity boundary.
The current lower-layer baseline leaves room for a third scheduler worker, not a
general configurable worker count. A fourth worker would exceed the current
scheduler object table at the Phase 62 baseline. Runtime/root behavior above two
has not been proved.

**Capacity 2 remains the only currently validated concurrent API configuration.**

The smallest useful future experiment is a bounded capacity-three validation,
with an explicit two-`GC_CHECK` root/lifetime case included before treating the
third worker as supported. Cancellation should follow this capacity work; it
introduces new lifecycle ownership and does not remove any of these limits.

## Repository state and evidence

The live repository, rather than the request's expected state, is authoritative:

| Item | Observed |
| --- | --- |
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `b94f5b893f7a19433159e7b75c5b04a3661cc8ae` — `Validate concurrent managed worker API` |
| Upstream | `origin/nativeaot-managed-kernel-integration` at the configured SSH URL |
| Starting divergence | `0 ahead / 0 behind`; the request stated `3 ahead / 0 behind` |
| Starting worktree | Clean |
| Phase 61/62 evidence | Accepted reports and artifacts under `docs/superpowers/validations` and `artifacts` |

The Phase 61 and Phase 62 reports were read before this audit. Phase 62 proves
36 concurrent pairs / 72 paired worker lifecycles, both completion orders, slot
reuse, stale-handle rejection, a third-create rejection, and full baseline
restoration. Its QEMU baseline is VM regions `3`, live scheduler TCBs `2`, and
live scheduler objects `13`; its two-worker peak is VM regions `7`, TCBs `4`, and
objects `15`.

The implementation points audited were:

- [`nativeaot_managed_worker_api.h`](../../../src/Gate4Harness/nativeaot_managed_worker_api.h)
  and [`nativeaot_managed_worker_api.c`](../../../src/Gate4Harness/nativeaot_managed_worker_api.c)
  for records, requests, results, handles, capacity checks, close/reclaim, and
  the Phase 61/62 fixtures.
- [`scheduler_foundation.h`](../../../src/Gate4Harness/scheduler_foundation.h)
  and [`scheduler_foundation.c`](../../../src/Gate4Harness/scheduler_foundation.c)
  for TCBs, scheduler objects, stack/environment allocation, handles, TLS/FLS,
  and reclamation.
- [`nativeaot_scheduler_thread_lifecycle.h`](../../../src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.h)
  and [`nativeaot_scheduler_thread_lifecycle.c`](../../../src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.c)
  for attach/detach, ThreadStore validation, root evidence, and rollback states.
- [`vm_substrate.h`](../../../src/Gate4Harness/vm_substrate.h),
  [`memory_accounting.h`](../../../src/Gate4Harness/memory_accounting.h), and
  the scheduler-stack callbacks in `gate4_loader.c` for VM/physical limits.
- [`ManagedEntry.cs`](../../../src/ManagedEntryProbe/ManagedEntry.cs) for the
  managed GC probe and the Phase 58 static-root boundary.

## What “capacity” means

These values are distinct and must not be silently substituted for one another.

| Capacity | Meaning in this repository | Current result |
| --- | --- | --- |
| API capacity | Maximum simultaneously live API worker records accepted by `create` | Fixed policy `2` |
| Scheduler capacity | Maximum live TCBs in one `GXOS_SCHEDULER` | Fixed `6`, including the boot TCB |
| Managed runtime capacity | Maximum simultaneously attached NativeAOT workers under the current ThreadStore/attach contract | No general N proof; Phase 62 proves two API workers with shared-ThreadStore validation |
| Stack/VM capacity | Stack reservations, committed pages, guard/usable VM descriptors, and backing pages available to workers | Several independent fixed ledgers; not equal to API capacity |
| Root capacity | Simultaneous managed-root ownership or GC-root evidence, depending on the operation | Per-lifecycle proof state exists; two simultaneous root owners are not validated |
| Handle capacity | Valid opaque slot/identity/generation combinations | Field widths are much larger than current slots, but reuse and wrap safety still apply |
| Validation capacity | The number and mix of workers actually exercised by the harness | Two concurrent API workers; A/B fixture; one `GC_CHECK` worker per pair |

The durable architectural definition should therefore be:

```text
safe API capacity <= min(
    API record slots,
    scheduler TCB slots available after reserved workers,
    scheduler object slots available after baseline objects,
    VM region/reservation/commitment capacity,
    backing-page capacity,
    ThreadStore/runtime attach capacity,
    root-operation capacity,
    handle/generation safety,
    validated operation/lifecycle coverage)
```

The terms in this expression are resource contracts, not automatically equal
integer constants.

## Per-worker resource ledger

One additional live worker consumes the following incremental resources.

| Resource | Kind and current limit | Increment for one worker | Exhaustion / reclaim | Can block a future API increase? |
| --- | --- | --- | --- | --- |
| API worker record | Fixed `GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY = 2`; records are an embedded array | One record, including one lifecycle record | API `create` returns status `13` before scheduler construction; close clears `active` after reclaim | Yes; current policy boundary |
| Request storage | Fixed, embedded in each record; request is 32 bytes | One copied request | Record close/reset | Yes only if the record array/storage is not resized together |
| Result storage | Fixed, embedded in each record; result is 40 bytes; `last_results` is also capacity-sized | One live result plus one close-history copy slot | Result is copied before record clear; active record then becomes inactive | Yes if opaque context storage or arrays are not resized |
| Handle identity | Embedded `slot + identity + generation` value | One published API handle | Published only after lower-layer construction and prepare succeed | Yes if field widths or publication ordering change |
| Scheduler TCB | Fixed `GXOS_SCHEDULER_MAX_THREADS = 6` | One non-boot TCB | `collect` frees TCB after close, termination, zero execution refs, and stack cleanup | Yes; workers compete with system/kernel TCBs |
| Scheduler object | Fixed `GXOS_SCHEDULER_MAX_OBJECTS = 16` | One thread object/handle record | Close drops public refs; reclaim releases object record | Yes; this is the first measured lower-layer ceiling at the Phase 62 baseline |
| Scheduler slot | TCB array index | One non-boot slot | Slot becomes free only after full TCB zeroing | Yes; current Phase 62 baseline leaves four free TCB slots |
| Stack reservation | Dynamic from the configured VM arena; reservation granularity is `0x10000`, worker reservation is `0x11000` | One reservation, 17 pages of virtual interval | `free_stack_vm` decommits/unmaps and releases reservation | Yes, through VM arena and address-space availability |
| Guard region | One non-present 4 KiB page inside the reservation | One guard page; no committed backing page | VM region descriptors are unregistered on stack free | Yes; guard and usable regions consume descriptors |
| Usable stack | Fixed `65536` bytes, 16 committed pages | 16 committed stack pages | Unmap/decommit on reclaim | Yes; commitments and physical pages are finite |
| Stack canary page | Dynamic scheduler page allocation | One 4 KiB diagnostic page | `page_free` during reclaim | Yes; uses the scheduler page/physical ledger |
| GS/TEB/TLS environment | Dynamic page allocations in `allocate_thread_environment` | Four 4 KiB pages: GS area, TLS vector, TLS block, and TEB-like area | `free_thread_environment` returns all four pages | Yes; per-worker backing pages are finite |
| VM region descriptors | Fixed `GXOS_VM_REGION_LEDGER_CAPACITY = 64` | Two: guard reservation plus committed usable stack | `gxos_vm_region_unregister` on free | Yes; measured baseline `3` leaves `30` region-pair increments mathematically, but other resources bind first |
| VM reservation slots | Fixed `GXOS_VM_MAX_RESERVATIONS = 256` | One arena reservation | `gxos_vm_arena_release` | Yes, but not the current first bottleneck |
| VM commitment slots | Fixed `GXOS_VM_MAX_COMMITMENTS = 4096` | 16 stack commitments, plus any allocator-specific bookkeeping | Decommit each usable page | Yes, but not the current first bottleneck |
| Physical/page ledger | Fixed `GXOS_PHYSICAL_LEDGER_CAPACITY = 4096` entries | At least 21 worker pages in the scheduler path: 16 stack + 1 canary + 4 environment; exact ledger-entry accounting remains allocator-owned | Page-free/decommit paths | Yes; no operational worker count is derived from the raw total |
| TLS vector | Per-TCB fixed vector of `GXOS_SCHEDULER_TLS_VECTOR_SLOTS = 512` pointer slots | One vector page and one image TLS index entry | Environment reclaim | The global TLS index must be below 512; values scale per TCB |
| FLS value | Per-TCB fixed array of `GXOS_SCHEDULER_FLS_SLOTS = 64` values | One value in the shared runtime FLS slot | Runtime cleanup clears the value before scheduler reclaim | Slot IDs are global/per-runtime; values are per worker |
| Runtime `Thread*` | Dynamic NativeAOT runtime attachment | One runtime thread object while attached | Runtime FLS cleanup detaches it and removes it from ThreadStore | Yes; current ThreadStore contract is only bounded and validated for the Phase 62 mix |
| Allocation context | Per-runtime-thread NativeAOT TLS fields | One worker allocation-context pair | Runtime detach owns cleanup; a post-root cursor may remain diagnostic evidence | Yes; runtime attach/GC semantics must be proved for more workers |
| Root evidence | Per-lifecycle `managed_root_*` fields; no separate N-root table in the API | One proof/ownership state per record when used | Release must balance publication before detach/reclaim | Yes; current two-root case is not validated |
| Completion state | Capacity-sized completion-order array and counters in the opaque context | One completion record | Reset on API initialization; close history persists per API slot | Yes if storage is not expanded coherently |

The source-level scheduler path therefore costs approximately 21 backing pages
per additional live worker, two VM-region descriptors, one VM reservation, one
TCB, one thread object, one TLS/FLS environment, one runtime attachment, and one
API record. The 21-page figure is a scheduler-path accounting figure, not a
promise that all page allocations consume one identical physical-ledger entry.

## The true current bottlenecks

At the Phase 62 baseline:

```text
API records:             2 policy slots
free scheduler TCBs:     6 - 2 = 4
free scheduler objects:  16 - 13 = 3
free VM regions:         64 - 3 = 61, or floor(61 / 2) = 30 workers by this ledger alone
```

The first lower-layer boundary is the scheduler object table: three additional
thread objects fit at that measured baseline, while a fourth would require 17
live objects. The TCB table would still have one slot left at four additional
workers, but the object table would already be exhausted. This does not make
three a supported API configuration; it identifies the first source-level
candidate boundary under the exact accepted baseline.

There is no source evidence that the runtime, managed GC operation mix, or root
ownership is an arbitrary-N resource. The practical chain is therefore:

```text
validated API capacity (2)
  <= fixed API record capacity (2)
  <= measured scheduler-object headroom (3)
  <= measured scheduler-TCB headroom (4)
  <= ThreadStore/runtime/root proof still required
```

The current API constant is a bounded policy and validation gate, not proof that
the scheduler or VM is full.

## Scheduler slots and system reservation

`GXOS_SCHEDULER_MAX_THREADS` is six. Slot zero is the boot TCB. The Phase 62
managed-worker call observes two live TCBs at baseline: the boot/main worker and
the persistent blocked finalizer/system peer. The two API workers occupy two
additional non-boot slots at overlap. Thus:

- total scheduler TCB slots: `6`;
- boot/system reservation: at least slot zero, plus the persistent finalizer
  peer in the Phase 62 configuration;
- candidate API scheduler slots at the Phase 62 baseline: `4`;
- candidate API scheduler slots in a scheduler with only boot live: `5`;
- API workers compete with all other scheduler/kernel workers.

A future API limit should be a separately bounded compile-time constant that is
also statically checked against the intended scheduler/object baseline. It should
not dynamically consume every currently free TCB or object slot.

## VM and stack ceiling

Each worker reserves `65536 + 4096 = 69632` bytes (`0x11000`) of virtual stack
interval. The first 4096 bytes are a non-present guard region; the remaining
65536 bytes are committed usable stack. The stack allocator registers exactly
two VM-region descriptors: one reserve/guard descriptor and one committed/usable
descriptor. Phase 62's `0x3 -> 0x7` peak confirms two descriptors per live worker
for two workers.

The raw VM-region ledger ceiling at the Phase 62 baseline is 30 additional
worker-sized pairs. The raw reservation ledger is 256 slots and the commitment
ledger is 4096 page records, so neither is the first measured limit. The VM
region count is not an operational capacity: scheduler objects, TCBs, page
backing, ThreadStore state, root semantics, and validation coverage bind first.

The mathematical VM ceiling is therefore not the architectural or validated
ceiling:

| Classification | Result |
| --- | --- |
| Validated | 2 concurrent API workers |
| Source-level candidate at the measured baseline | 3 workers before the 16-object table is full |
| Raw VM-region mathematical ceiling | 30 additional workers from the region ledger alone |
| Supported | Only 2; anything above requires new focused validation and a contract update |

## ThreadStore, runtime, TLS, and FLS

The lifecycle helper walks a bounded linked ThreadStore with
`PHASE53O_THREADSTORE_MAX = 16`. It is naturally iterative rather than A/B
indexed. Sequential validation retains the exact `baseline + 1` attach and
baseline detach invariant. Concurrent API validation opts into shared-ThreadStore
mode: attach requires the count to increase, and detach requires the detached
worker's runtime thread to be absent from the remaining chain rather than
requiring the global count to return to zero.

No remaining production comparison against exactly two attached API workers was
found. The remaining ThreadStore limit is the fixed walk bound of 16 and the
runtime's own attached-thread contract. A future three-worker test must validate
the full attach/detach chain with two peers remaining, not just the API array.

TLS is per TCB: each worker gets a GS area, a 512-entry TLS vector, a TLS block,
and a TEB-like page. The NativeAOT image's TLS index is global and must be below
512, but each worker writes its own vector entry. FLS slot IDs are global within
the scheduler (`64` slots); the runtime slot is shared, while each TCB stores an
independent value and cleanup state. These structures scale per TCB and do not
create a hidden two-worker array.

## Managed-root audit

The API's Phase 62 `root_ledger` is a count of active records whose lifecycle has
`managed_root_survived` set. `GC_CHECK` invokes the managed `ManagedGcProbe`,
which retains a local array across one collection and validates its checksum. It
does not publish a persistent managed static root through the Phase 58
`ManagedRootPublish` export. Consequently, the Phase 62 root count is proof
state, not evidence that two persistent managed static roots were simultaneously
owned.

The lifecycle structure can hold one root identity/publication-release balance
per worker record, and its release/detach/reclaim checks are generation- and
ownership-aware. However, the Phase 58 managed static-root implementation itself
has one singleton field (`s_phase58ManagedRoot`) and rejects a second publish
while occupied. That path is not a generalized multi-root store and was not
used by the Phase 62 API probe.

Before increasing capacity, the project must choose and test one explicit rule:

1. prove that `GC_CHECK` only needs callback-local roots and validate two or more
   root-bearing callbacks with independent lifecycle evidence; or
2. add a bounded per-worker managed-root/token contract; or
3. reject a second persistent-root operation before expensive worker construction
   with a durable capacity/status result.

Until that choice is made, root capacity is not equal to API record capacity.

## Handles and generations

The public API handle is a 12-byte structure, not a packed bitfield:

| Field | Width | Source/sentinel | Assessment |
| --- | ---: | --- | --- |
| scheduler slot | 32 bits | `UINT32_MAX` is invalid at the API boundary; scheduler currently has 6 TCB slots | No truncation at current or candidate scheduler sizes |
| worker identity | 32 bits | zero is invalid; scheduler allocates monotonically from `next_identity` | 4,294,967,295 nonzero values in principle; source has no explicit wrap guard |
| worker generation | 16 bits | zero is invalid; scheduler object generation skips zero | 65,535 usable generations per object slot before wrap |
| reserved | 16 bits | ignored by equality/validation | Does not participate in identity; canonical callers should publish zero |

Slot reuse advances the scheduler-object generation and allocates a new worker
identity. API equality uses slot, identity, and generation. Stale/cross-worker
operations are rejected, and the retained close history rejects later stale
operations by slot even when the exact old handle is not the last one observed.

Generation wrap is theoretically possible after 65,535 reuse cycles of one
scheduler object slot. Identity wrap is theoretically possible after roughly
4.29 billion scheduler identities; identity zero is not valid and the source
does not explicitly stop before that wrap. A generation-only wrap does not
recreate a full API handle while identity remains different. These are not
practical limits for the current bounded service, but they must be part of a
future long-lived handle contract.

No handle field truncation or ambiguity is caused merely by changing the API
record count from two to three. The real limits remain scheduler/object slots,
runtime/root semantics, and validation.

## Request/result and shared-state audit

The production API indexes every live record, request, result, close-history,
and completion-order array by `GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY`.
There is no production lookup tied to worker A/B ordering. The 1536-byte opaque
storage has a compile-time assertion against the private context; any capacity
change must re-check this storage rather than assuming the macro alone is enough.

The A/B names, `record_a`/`record_b`, pair ordering, twelve-pair count, and
completion-order fixture markers are Phase 62 validation code. They are not the
production create/submit/drive/poll/close path.

Shared state includes the scheduler/global current thread, VM and scheduler
ledgers, callback bridge registration and invocation counters, the shared
runtime FLS slot ID, and the managed runtime's global ThreadStore. Per-worker
state includes the API record, copied request/result, handle, TCB, object/slot,
stack/guard contract, GS/TEB/TLS environment, FLS value, runtime `Thread*`,
allocation context, lifecycle history, and root evidence.

## Exhaustion and rollback contract

When both API records are active, `gxos_nativeaot_managed_worker_api_create`
returns `GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY` (`13`) before calling
`gxos_scheduler_create_suspended_thread`. The rejected call publishes no API
record, handle, identity, generation, TCB, object, stack, guard, TLS, FLS, or
runtime state. Existing workers are untouched. Phase 62 proves this third-create
behavior.

If the API record is available but a lower layer fails, the durable contract
must be the same terminal result: no partially published worker and no leaked
ownership. The current source provides the following rollback path for creation:

- no free TCB or scheduler object: scheduler creation fails before a live worker
  is returned;
- stack reservation/commitment/VM-region failure: the scheduler stack allocator
  frees partial commitments, unregisters any registered regions, and releases
  the reservation;
- canary-page or environment-page failure: already allocated pages and stack VM
  are freed and the object/TCB are zeroed;
- lifecycle prepare failure after scheduler creation: the API closes the
  scheduler handle, discards the created suspended TCB, collects it, and clears
  the API record;
- identity allocation during a failed create is decremented by the scheduler's
  existing construction rollback.

The public API maps these lower-layer failures to
`GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_INTERNAL_FAILURE` (`12`), while reserved
capacity exhaustion remains status `13`. Close/reclaim requires runtime detached,
root-balanced, terminated, reclaimable state before it clears the API record.

Runtime attach failure and post-attach failure use the accepted Phase 55–60
ownership rules: a failed worker must not be reclaimed while runtime ownership,
FLS, ThreadStore membership, allocation context, or root ownership remains. The
accepted Phase 62 workload proves the normal attach → callback/GC → detach →
reclaim path. It does not inject a new runtime-attach failure into the API probe;
that remains a required future negative test before capacity growth.

## Fixed-two assumptions and limits

Production capacity assumptions that intentionally remain:

- `GXOS_NATIVEAOT_MANAGED_WORKER_API_CAPACITY` is exactly `2`.
- The opaque API storage and all capacity-sized arrays are compiled for that
  bounded count.
- Phase 62's public validation fixture uses exactly two submitted records and
  one A/B pair at a time.
- No cancellation, timeout, pool, priority, affinity, arbitrary delegate, or
  persistent worker semantics exist.
- The managed static-root validation facility is singleton and is not a general
  multi-root API.

The first three are current bounded policy or fixture assumptions. The A/B
labels and twelve-pair count are fixture-only. No explicit `worker[0]` /
`worker[1]` branching remains in the production API operation path.

## Three workers, four workers, and cancellation

A three-worker test would add meaningful evidence:

- it would exercise the first unused API record and completion/result loops;
- it would consume the last measured scheduler-object slot;
- it would test ThreadStore baseline-plus-two-peer attach/detach accounting;
- it would expose any remaining pair-shaped fixture or cleanup assumptions;
- it would distinguish API capacity admission from lower-layer exhaustion.

It would not, by itself, prove arbitrary N, multi-root behavior, cancellation,
or runtime thread-pool compatibility.

A four-worker test is not preferable under the current source: at the Phase 62
baseline it would need 17 scheduler objects against a 16-object table. It would
first test a known lower-layer exhaustion condition rather than a useful managed
concurrency configuration. Four becomes meaningful only after an object-table
and baseline-resource redesign.

Cancellation should not precede the capacity contract. Cancellation adds new
states and rollback edges while relying on the same scheduler, runtime detach,
FLS, root, stack, and handle ownership rules. The smallest Phase 64 direction
should be a bounded capacity-three experiment, preceded or coupled with an
explicit two-root/`GC_CHECK` contract check and an injected runtime-attach
failure test. It must keep the public capacity compile-time bounded and must not
claim more than the tested count.

## Validation performed for Phase 63

This phase made documentation only; no production C, managed payload, harness,
branch, remote, credential, worktree, or history repair was performed.

Focused host/static validation rerun:

```text
PHASE61_MANAGED_WORKER_API_HOST_TEST=PASS
PHASE62_MANAGED_WORKER_API_HOST_TEST=PASS
```

The existing host test checks API sizes, the capacity constant, status values,
request validation, handle equality, state transitions, and an A/B storage
model. It is not a substitute for real scheduler/runtime/reclaim validation.

The accepted Phase 62 evidence remains the authoritative QEMU/build evidence:

- 3 fresh QEMU boots;
- 36 concurrent pairs / 72 paired worker lifecycles, plus reuse lifecycles;
- capacity-full rejection, both completion orders, request/result isolation,
  stale/cross-worker rejection, VM/TCB/object/root baseline restoration;
- normal build passed;
- SyntheticScheduler build and fresh boots passed.

No new QEMU run was needed for this documentation-only phase. No new normal or
SyntheticScheduler build was needed because production source was unchanged.

## Recommendation and acceptance

Keep the current representation compile-time bounded. The existing macro is the
right style for this freestanding service, but a future value must be checked
against opaque storage, scheduler-object/TCB headroom, VM ledgers, ThreadStore,
runtime attachment, root semantics, and focused validation. Runtime-configurable
capacity is not warranted: it would make failure behavior and proof boundaries
less deterministic without removing any fixed lower-layer limits.

Phase 63 is accepted as an audit/documentation unit with Outcome C. No
production defect requiring a Phase 63 repair was found in the accepted
capacity-two path, so no production repair was made. The remaining limitations
are the unvalidated runtime-attach failure path in the API fixture, the lack of
multi-root validation/contract, the fixed scheduler object baseline, and the
fact that any count above two is unsupported and unvalidated.

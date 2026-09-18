# Phase 53V NativeAOT worker-stack contract

Status: approved architecture, design under implementation review

Date: 2026-09-18

## Purpose

Phase 53T proved that the first invalid architectural RSP is produced by the
legitimate generated `sub rsp, rax` for a `0x41B0` frame. Phase 53U established
that the shared scheduler worker contract is the cause: the worker has only
four committed pages, `GS+0x10` is zero, and a corrected stack limit would
currently probe the unrelated GS page below the worker. Phase 53V repairs the
shared scheduler-managed NativeAOT worker stack contract and validates the
repair at the VM, scheduler, managed-worker, and fresh-QEMU levels.

The repair is deliberately platform-level. It does not patch the generated
prologue, disable stack probing, shrink the managed workload, or special-case
the historical RIP or context address.

## Chosen architecture

Each scheduler-managed worker receives one sparse, page-aligned virtual
reservation with this fixed initial production geometry:

```text
high addresses

usableStackHigh
    <- 64 KiB committed, writable, non-executable usable worker stack ->
usableStackLow == GS+0x10 == TEB+0x10
    <- 4 KiB reserved, non-present, never-mapped guard page ->
reservationBase

low addresses
```

The reservation is therefore `0x11000` bytes, with the guard immediately
below the `0x10000`-byte usable range. The 64 KiB value is the approved initial
production policy for this phase. It is not a claim that NativeAOT can never
need more. Each worker records actual minimum RSP and high-water measurements;
future policy changes must be based on those measurements and a new contract
decision.

The TCB owns the authoritative stack contract. Its stack fields describe the
reservation base/size, guard base/size, usable low/high/size, initial RSP,
minimum observed RSP, bounded high-water bytes, committed page count, and the
reservation/guard/usable VM identities. Existing `stack_base` and
`stack_limit` consumers remain the usable low and exclusive usable high
compatibility names, but creation, validation, diagnostics, context setup, and
cleanup use the TCB contract as the source of truth and verify those aliases.

The context-switch record remains unchanged. A restored context is valid only
when all of these pairings agree with the same TCB:

* restored RSP is inside that TCB's usable interval;
* restored GS base is that TCB's GS page;
* GS+0x30 points to that TCB's TEB and GS+0x58 points to its TLS vector;
* GS+0x08 and TEB+0x08 equal usableStackHigh;
* GS+0x10 and TEB+0x10 equal usableStackLow;
* TLS vector slot zero, TLS block, FLS, last-error, context GS base, VM
  identities, and TCB identity are all worker-local.

## Components and ownership

### Scheduler stack contract

`scheduler_foundation.h` adds the constants and contract fields needed to
express the geometry without relying on a canary. The scheduler requests a
stack allocation through a stack-VM callback that returns the complete
contract, including the usable virtual address used as the real RSP domain.
The ordinary page allocator remains responsible for the worker's GS, TEB,
TLS-vector, and TLS-block pages; those pages are not allocated adjacent to the
dynamic worker reservation.

The callback owns the VM substrate transaction. On allocation it reserves the
full `guard + usable` span, commits only the usable interval, confirms that the
guard query is non-present, and returns the VM identities. On failure it rolls
back all committed pages and the reservation. On cleanup it unmaps and
decommits only committed usable pages, frees their physical pages, removes the
usable/reserved region descriptions, and releases the reservation. It never
touches the guard as a committed page.

Host tests use a contract-preserving fake callback with a real writable backing
buffer for the usable range. Production uses the existing `GXOS_VM_ARENA`,
`GXOS_VM_PAGING`, and UEFI page allocator, so the guard is genuinely absent
from the owned page tables rather than represented by a writable marker.

### GS/TEB/TLS initialization

Worker environment initialization writes both the generated-code stack-limit
slot and the existing TEB-like fields from the same `usableStackLow` value:

```text
GS+0x08  = usableStackHigh
GS+0x10  = usableStackLow
GS+0x30  = TEB base
GS+0x58  = TLS vector base
TEB+0x08 = usableStackHigh
TEB+0x10 = usableStackLow
```

The boot/main environment remains a separate non-worker contract with its
existing stack bounds. No scheduler-managed worker inherits or copies the main
worker's GS, TEB, TLS, FLS, or stack values.

### Initial RSP and high-water state

Initial RSP is derived from the contract's usable high boundary, aligned for
the existing Windows x64 entry convention, and stored in both the contract and
the unchanged context record. The synthetic return slot is written inside the
usable range. No bytes at the usable low boundary are reserved for a canary;
the complete 64 KiB is usable stack capacity.

The register-capture path reports the current RSP to scheduler diagnostics.
For a worker, an in-range RSP updates `minimumRsp` and
`highWaterBytes = usableStackHigh - minimumRsp`. Out-of-range observations set
a bounded contract-violation bit and do not fabricate a high-water value.
Diagnostics expose the minimum RSP, high-water bytes, usable capacity, guard
address, and sample count. These values are bounded integers owned by the TCB,
not an unbounded trace or a second stack authority.

## Context-switch and identity proof

The existing `GXOS_SCHEDULER_CONTEXT` layout and assembly switch ABI are
preserved. The switch still saves the old context's post-call RSP and RIP,
restores the complete nonvolatile state, writes the target GS base, and jumps
to the target RIP.

Phase 53V adds a two-worker proof to the scheduler validation path. Each worker
executes a shared probe that captures RSP, GS base, TEB, TLS vector/block, FLS,
identity, and stack metadata before and after a yield/block/resume sequence.
Every sample is checked against the worker's own TCB. The proof fails if any
transition produces:

```text
RSP(worker A) with GS+0x10(worker B)
RSP(worker A) with GS+0x10 == 0
RSP outside A's usable range
GS/TEB/TLS/FLS state from another worker
```

The managed-worker path uses the same shared scheduler creation path and the
same callback; it receives no historical-context or RIP-specific branch.

## Guard-fault proof

The focused VM/scheduler proof deliberately attempts a write at
`usableStackLow - 1`, which is inside the dedicated guard page. Before the
attempt it proves:

* the guard is within the reservation and immediately below usable low;
* the guard has no arena commitment;
* page-table query reports not-present;
* the usable low page is present and writable;
* the worker GS/TEB/TLS metadata and an unrelated kernel sentinel are intact.

The QEMU proof installs the existing page-fault diagnostic path and performs
the same controlled boundary hit in an isolated worker. The expected result is
a page fault whose CR2 lies in the guard page. The proof records and verifies
the worker identity, GS base, TEB stack fields, and unrelated sentinel after
the controlled handler boundary. It does not treat a writable canary or a
post-fault memory write as a successful guard test.

The full managed path is also run with the legitimate generated `0x41B0`
frame. The frame must remain in the usable range and the generated prologue and
probe bytes must remain unchanged. If the repaired stack contract exposes an
independent failure after the frame succeeds, that observation is retained and
classified as Phase 53W; Phase 53V does not widen into unrelated runtime,
interrupt, allocator, or page-table redesign work.

## Diagnostics and serial evidence

The scheduler and loader emit contract fields for each fresh managed worker:

```text
STACK_RESERVATION_BASE
STACK_RESERVATION_BYTES
STACK_GUARD_BASE
STACK_GUARD_BYTES
STACK_USABLE_LOW
STACK_USABLE_HIGH
STACK_USABLE_BYTES
STACK_INITIAL_RSP
STACK_MINIMUM_RSP
STACK_HIGH_WATER_BYTES
STACK_COMMITTED_PAGES
STACK_VM_IDENTITY
STACK_GUARD_VM_IDENTITY
GS_BASE
GS_PLUS_08
GS_PLUS_10
TEB_PLUS_08
TEB_PLUS_10
```

The evidence decoder requires the equality and range relationships rather than
only checking that a crash marker disappeared. It also requires the guard
page query and cleanup counters to agree with the TCB contract.

## Failure handling and cleanup

Allocation is transactional. Any failure after reservation, commit, environment
allocation, region registration, or initial-frame setup unwinds in reverse
ownership order. A failed creation leaves no live TCB, object, region, page
commitment, physical page, or reservation.

Reclamation is refused while the worker is current, running, referenced,
queued, or has an invalid contract. Once eligible, cleanup validates the
contract, unmaps/decommits exactly the committed usable pages, frees their
physical backing, unregisters the region descriptors, releases the full
reservation, frees GS/TEB/TLS pages, and clears every TCB VM/stack field. The
guard page is not passed to the unmap, decommit, or free operations.

## Alternatives rejected

* A larger contiguous physical allocation would fix the immediate arithmetic
  but would not provide a real non-present guard or the sparse VM ownership
  proof. It also preserves the accidental adjacency risk with GS/TLS pages.
* A writable lower canary would not fault at the boundary and is explicitly
  insufficient for this acceptance gate.
* Splitting direct-identity large pages or redesigning page tables is out of
  scope. The existing private sparse arena and 4 KiB mapping operations are
  sufficient unless implementation evidence proves otherwise.

## Acceptance matrix

1. `GS+0x10 == usableStackLow`; it never points to the guard or reservation
   start.
2. The guard is reserved but non-present/unmapped; no writable canary replaces
   it.
3. Cleanup unmaps/frees committed usable pages and releases the reservation
   without touching the guard.
4. 64 KiB is the initial production policy only; real minimum-RSP/high-water
   measurements are recorded.
5. The repair is shared by scheduler-managed NativeAOT workers; no historical
   context or RIP special case exists.
6. Two-worker transitions prove that RSP, GS+0x10, GS/TEB/TLS ownership, and
   stack metadata remain one-worker coherent; no cross-worker or zero limit is
   accepted.
7. A controlled overflow page-faults in the dedicated guard while GS/TEB and
   unrelated kernel state remain intact.
8. The legitimate `0x41B0` frame remains unchanged and succeeds without
   prologue/probe/workload changes.
9. No direct-identity large-page split or page-table redesign is introduced.
10. Any independent post-contract failure is isolated as Phase 53W rather
    than widening Phase 53V.

## Validation sequence

The implementation is gated by focused red/green tests for contract geometry,
GS/TEB equality, guard non-presence, allocation rollback, cleanup, and
high-water updates. Existing scheduler model, durability, and VM suites remain
green. The managed harness is then rebuilt without changing the NativeAOT
payload or its generated prologue. The Phase 53V runner validates one complete
equivalent `0x41B0` path, the two-worker proof, the deliberate guard fault, and
three independent isolated QEMU boots. Raw serial logs, hashes, and the final
Phase 53V summary are retained under a fresh evidence directory.


# Phase 53V NativeAOT Worker-Stack Implementation Plan

## Goal

Implement and validate the approved sparse dynamic-VM worker-stack contract for every scheduler-managed NativeAOT worker:

```text
high addresses

usableStackHigh
  <- 64 KiB committed usable stack
usableStackLow == GS+0x10 == TEB+0x10
  <- 4 KiB reserved, non-present guard page
unrelated allocations / GS elsewhere

low addresses
```

The TCB-owned contract is authoritative for all allocation, guard, usable, initial-RSP, VM-identity, and bounded high-water state. The existing context-switch ABI remains intact unless a narrowly-scoped helper is required to observe the restored state.

## Files and responsibilities

- `src/Gate4Harness/scheduler_foundation.h`
  - Replace the fixed 16 KiB/canary allocation contract with the 64 KiB usable plus 4 KiB guard policy constants.
  - Add the TCB-owned `GXOS_SCHEDULER_STACK_CONTRACT` containing reservation, guard, usable bounds, VM identities, backing information for host fallback, initial RSP, minimum RSP, high-water bytes, and bounded diagnostic state.
  - Change stack VM callbacks from register/unregister semantics to allocate/free contract semantics.
  - Declare contract validation, snapshot validation, register-sample accounting, and diagnostic accessors used by tests and proof code.

- `src/Gate4Harness/scheduler_foundation.c`
  - Implement host-test allocation/free callbacks that model one reservation, one non-present guard, and the complete 64 KiB usable region.
  - Make worker creation transactional: allocate the contract, zero only the committed usable bytes, initialize `initial_rsp`, install the invalid return stub, allocate the TEB/TLS environment, and publish the worker only after every identity/bound check succeeds.
  - Set `GS+0x08` and `TEB+0x08` to `usableStackHigh`; set `GS+0x10` and `TEB+0x10` to the same `usableStackLow`.
  - Remove boundary canaries as a stack-safety mechanism. If host diagnostics retain canary storage, keep it outside the reservation and mark it diagnostic-only; no writable data may occupy the guard or reduce usable capacity.
  - Add `minimum_rsp`/high-water updates for captured or explicitly validated worker snapshots, with overflow/underflow bounded rather than wrapping.
  - Validate allocation/guard/usable geometry, exact aliasing of GS/TEB lower bounds, initial RSP placement, VM identity ownership, and cleanup ordering.
  - Free committed pages through the stack callback, then release the reservation without touching the guard as a committed page.

- `src/Gate4Harness/scheduler_context.S`
  - Preserve the existing save/restore frame layout and context-switch ABI.
  - Keep the exact captured RSP/GS values, then call a C diagnostic hook after the snapshot stores so high-water accounting observes the captured state without changing the saved ABI.

- `src/Gate4Harness/scheduler_proof.c`
  - Extend the proof output to include the authoritative stack bounds, guard bounds, VM identities, restored RSP, restored GS base, and bounded high-water fields.
  - Add a two-worker coherency proof that rejects a worker-A RSP paired with worker-B GS/TEB lower-bound metadata or zero GS.
  - Add an intentional guard-boundary proof path whose expected result is a page fault at the dedicated guard page while GS/TEB and unrelated kernel markers remain valid.

- `src/Gate4Harness/tests/scheduler_stack_vm_tests.c`
  - Convert the host VM tests to the allocate/free callback contract.
  - Add tests for exact `0x11000` reservation geometry, 4 KiB guard identity, 64 KiB usable range, initial RSP, `GS+0x10 == TEB+0x10 == usableStackLow`, `TEB+0x08 == usableStackHigh`, and no writable guard bytes.
  - Add two-worker identity/coherency tests, cleanup tests proving the committed usable pages and reservation are released while the guard is never treated as committed, and bounded high-water tests.

- `src/Gate4Harness/tests/scheduler_durability_tests.c`
  - Update callback names and lifecycle expectations.
  - Add repeated create/reclaim coverage that checks the sparse contract does not leak reservations, usable pages, guard mappings, or VM identities.

- `src/Gate4Harness/scheduler_model_tests.c` and `src/Gate4Harness/create_thread_model_tests.c`
  - Update any fixed 16 KiB/canary assumptions to the contract policy.
  - Preserve the legitimate `0x41B0` frame expectation and reject any implementation that changes generated-prologue probing or shrinks the workload.

- `src/Gate4Harness/gate4_loader.c`
  - Replace the production single-range stack registration callback with sparse reservation/commit/decommit/release callbacks using the existing VM arena, paging, and region-ledger primitives.
  - Reserve exactly guard plus usable bytes, commit exactly the 64 KiB usable range, leave the guard non-present/unmapped, record separate guard/usable identities under one reservation identity, and reverse that transaction during cleanup.
  - Emit Phase 53V markers for contract geometry, per-worker identity, minimum RSP/high-water, guard fault, and cleanup results.
  - Keep this shared for all scheduler-managed NativeAOT workers; do not branch on historical RIP/context values.

- `tools/Run-SchedulerStackVmHostTests.ps1` and `tools/Run-SchedulerDurabilityHostTests.ps1`
  - Compile and run the new contract tests, including the expected guard/non-present and two-worker checks.

- `tools/Run-NativeAotSchedulerGcFreshBoots.ps1`, `tools/Run-NativeAotSchedulerThreadLifecycleFreshBoots.ps1`, and the relevant Phase 53 runner
  - Add explicit marker assertions for contract geometry, no mixed worker identity, guard-fault preservation, high-water measurements, and clean teardown.
  - Preserve the existing `0x41B0` frame and generated-prologue workload assertions.

- `docs/superpowers/specs/2026-09-18-phase53v-nativeaot-worker-stack-design.md`
  - Update only if implementation review discovers a contract detail that must be clarified; do not broaden the approved architecture.

## TDD-gated execution

### 1. Contract red phase

Add the new host-test assertions and callback contract references before production implementation. Run the focused stack VM suite and record the expected compile/test failure caused by missing 64 KiB/guard/contract fields. This is the required red proof that the tests exercise the new behavior rather than the old canary design.

### 2. Contract green phase

Implement the public contract, host allocator/free callbacks, worker creation transaction, environment aliases, validation, cleanup, and bounded diagnostics. Re-run the focused stack VM suite until it passes. Then run the durability and model suites to catch fixed-size assumptions.

### 3. Production VM green phase

Implement the gate4 loader sparse callbacks against the real VM arena and paging ledger. Compile the full harness. Validate that reservation, usable commits, and guard non-presence are observable through the existing VM query paths and that cleanup reverses the exact transaction.

### 4. Proof green phase

Add the two-worker context-switch checks and intentional guard-fault checks. Confirm the saved/restored RSP and GS values identify the same worker, no sample is paired with zero or another worker's lower bound, and high-water values remain within 64 KiB. Confirm unrelated GS/TEB/kernel markers survive the guard fault.

### 5. Fresh-boot validation

Run, at minimum:

1. Focused host stack VM tests.
2. Focused host durability tests.
3. Scheduler model tests and the full harness build.
4. The equivalent Phase 53T large-frame NativeAOT path, preserving `0x41B0` and generated probing.
5. The two-worker lifecycle/context-switch path.
6. The deliberate guard-boundary path.
7. At least three isolated fresh QEMU boots for the production path, with distinct output directories.

For each fresh boot, require all of the following: no old stack crash, contract marker present, exact guard/usable geometry, per-worker coherency marker, nonzero bounded high-water samples, guard fault at the guard page, unrelated-state preservation, and clean VM cleanup. Any independent failure exposed after the stack contract is fixed is recorded and isolated as Phase 53W; it is not folded into this repair.

## Review checklist before declaring complete

- `GS+0x10 == usableStackLow`; it never equals guard base or reservation base.
- `TEB+0x10 == usableStackLow` and `TEB+0x08 == usableStackHigh`.
- Guard is reserved but non-present/unmapped; no writable canary substitutes for it.
- Usable capacity is a full 64 KiB, with real high-water measurements recorded.
- Cleanup unmaps/frees only committed usable pages and releases the reservation without touching the guard as committed memory.
- One shared implementation covers all scheduler-managed NativeAOT workers.
- Two-worker validation rejects RSP/GS+0x10 mixes and zero GS.
- Controlled overflow faults inside the dedicated guard and leaves GS/TEB/unrelated kernel state intact.
- Legitimate `0x41B0` frame, probing, and workload remain unchanged.
- No direct-identity large-page splitting or page-table redesign was added.
- Any independent Phase 53 failure is isolated for Phase 53W.

# guideXOS .NET 10 — Phase 58 managed-root failure rollback

Date: 2026-09-23
Branch: `nativeaot-managed-kernel-integration`
Starting HEAD: `7b34506118d6c3f11ab0eae9db26b27836efcf02` — `Validate managed worker post-attach rollback`

## Result

Outcome B: a bounded runtime-contract defect was found and repaired. The Phase 58 managed-root rollback proof then passed 36/36 failed-worker lifecycles across three fresh boots, with a healthy replacement worker after every failure.

The tested boundary is:

`runtime attach → managed root publication → Phase 58 injection → managed root release → runtime detach → scheduler reclaim`

The failed worker never invokes the GC probe and never reaches post-GC continuation. The replacement worker publishes a new root, runs the existing GC probe, proves root survival across that GC, releases its root, detaches, and reclaims cleanly.

## Ownership model

The diagnostic object is `Phase58ManagedRoot`, a managed object containing a generation-scoped `uint Token`. It is allocated by the managed `ManagedRootPublish` export and stored in the managed static field `s_phase58ManagedRoot`.

That static managed reference is the actual GC root. The kernel does not retain a managed pointer, use a raw object address, or create a `GCHandle`. Native lifecycle state stores only the bounded token as evidence, plus publication/release counts and an ownership bit. The root owner is therefore the managed runtime static field; the cleanup authority is the managed `ManagedRootRelease` export, which validates the token and clears the static field.

Managed object evidence, object identity, and GC-root publication are distinct concepts here. The native token is evidence of the managed object identity. Root publication is authoritative only after `ManagedRootPublish` returns success and the native lifecycle ledger records the matching token. The scheduler object census is not a managed heap census; the root count reported by Phase 58 is the bounded managed static-root ledger (`0 → 1 → 0`).

## Diagnostic hook

Hook identifier: `GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_MANAGED_ROOT`
Compile-time gate: `GXOS_ENABLE_PHASE58_POSTROOT_ROLLBACK`
Framework: the existing Phase 56/57 single-shot, generation-aware injection record
Executable point: immediately after successful `ManagedRootPublish` and native root-publication bookkeeping, before the GC bridge and before post-GC continuation.

The hook requires prepared, resumed, running, runtime-attached state; a live NativeAOT `Thread*`; an allocation context; worker FLS/runtime state; a published root token; publication count one; release count zero; and no root-survival result. It captures slot, identity, and generation, fires once, rejects a second fire, and rejects mismatched lifecycle identity/generation.

Managed instructions necessarily executed before the hook are the callback attach trampoline, `ManagedCallback`, the `ManagedRootPublish` trampoline, and construction of the `Phase58ManagedRoot` object. No `ManagedGcProbe` instruction is executed for the failed worker.

## Cleanup authority and ordering

The failed-worker sequence is:

1. `ManagedRootPublish` creates and publishes the static managed root.
2. Phase 58 fires once.
3. `ManagedRootRelease` validates the token and clears the static root.
4. Native root bookkeeping records one release; duplicate release and duplicate publication are rejected.
5. The existing runtime FLS cleanup callback performs the sole runtime detach.
6. Scheduler termination, handle close, collection, and lifecycle reclaim release TCB, stack, VM, and scheduler object ownership.

`gxos_nativeaot_scheduler_worker_detach` refuses to run while a root is owned or while publication/release counts differ. Reclaim also refuses an owned or unbalanced root. No evidence field is nulled as a substitute for release.

## Reproduced defect and bounded repair

The first QEMU run reproduced a real post-allocation runtime contract difference: after the managed root was released, authoritative FLS cleanup reported detached runtime state, restored the ThreadStore baseline, and cleared worker FLS, but the detached `Thread*` retained a nonzero allocation cursor. Phase 57 did not expose this because its callback allocated nothing.

The repair preserves the strict zero allocation-cursor requirement for non-allocating Phase 53–57 paths. For a lifecycle that has published a managed root, detach accepts the runtime’s authoritative detached state only after FLS clearing and ThreadStore restoration; the cursor remains diagnostic evidence on the detached `Thread*`, not a live ownership edge. QEMU then proves complete scheduler/VM/TCB baseline restoration. No runtime-wide GC or root API was added.

## Resource evidence

Observed Phase 58 values were stable on all three boots:

| Point | VM regions | live scheduler threads | live scheduler objects | managed static-root ledger |
|---|---:|---:|---:|---:|
| Baseline | 3 | 2 | D | 0 |
| Prepared | 5 | 3 | E | 0 |
| Root published | 5 | 3 | E | 1 |
| Root released / detached | 3 | 2 | D | 0 |
| Final reclaim | 3 | 2 | D | 0 |

The `E` scheduler-object peak is not a managed heap object count. Managed-root identity and publication/release counts are the authoritative root evidence.

## ABA and replacement proof

Every failed worker was reclaimed before a replacement was created. The replacement reused the same scheduler slot, but received a new identity and generation. A stale failed lifecycle could not publish or release the replacement root, stale old-token release against the replacement managed static root returned the invalid-token result, stale handle operations were rejected, and stale detach/reclaim operations were rejected. The replacement root had a distinct token, survived its real GC probe, was released once, and the replacement then detached and reclaimed.

## Repeated results

- Phase 58: 3 fresh boots × 12 cycles = 36 attempted, 36 passed.
- Each cycle: one failed root publication, one Phase 58 fire, one failed root release, one detach, one reclaim, one same-slot replacement, one replacement root publication, one replacement GC/root-survival proof, one replacement root release, one replacement detach, one replacement reclaim.
- Failed-worker GC bridge delta: zero on every cycle.
- Failed-worker post-GC continuation: zero on every cycle.
- Leak trend: none; every boot returned to the baseline VM/thread/object/threadstore counts.
- Managed GC main proof: `MANAGED_GC_MAIN_OK=1`.

## Regressions and isolation

- Phase 57 post-attach/pre-root: 3 fresh boots × 12 cycles passed; failed worker had no root publication, one attach, one detach, baseline restoration, and healthy replacement.
- Phase 56 pre-attach: 3 fresh boots × 12 cycles passed; no attach, no root publication, no detach, and baseline restoration.
- Phase 54 two-worker ownership: 3 fresh boots × 12 cycles passed, including ordinary GC/root survival and post-GC continuation.
- Phase 53O evidence was present in every authoritative managed-runtime run, including scheduler/context, attach/detach, GC/root, reclaim/reuse, and `MANAGED_GC_MAIN_OK=1` contracts.
- SyntheticScheduler: 3 fresh QEMU proofs passed.
- Normal build: passed with no Phase 56/57/58 injection gate.
- Phase 58 build: only the Phase 58 gate was enabled; the hook record fired once per boot and no other phase hook fired.

## Artifacts

Managed payload: `artifacts/phase58-managed-root-payload/shared/gxos-managed-entry-probe.dll`
Payload size: 732672 bytes
Payload SHA-256: `1214CB1178376ED4AC43B2F178B81B6CD3E809C18B33CABC7B6293AEEDD60300`
Phase 58 harness: `artifacts/phase58-harness-final2/ESP/EFI/BOOT/BOOTX64.EFI`
Harness SHA-256: `8A020D7C511A8DDA31A0594084C5A3116885AAE8F91FFD7F627637C4A04E5D02`
Phase 58 evidence: `artifacts/phase58-evidence-final-r3/runs/run-1` through `run-3`
Phase 57 regression evidence: `artifacts/phase58-regression-phase57-evidence/runs/run-1` through `run-3`
Phase 56 regression evidence: `artifacts/phase58-regression-phase56-evidence/runs/run-1` through `run-3`
Phase 54 regression evidence: `artifacts/phase58-regression-phase54-evidence-r2/runs/run-1` through `run-3`
Host test binary: `artifacts/phase58-managed-root-rollback-host-tests/phase58-managed-root-rollback-host-tests.exe`
Host test SHA-256: `7E1BF9BFA39FF9E2AC4E265FD170FB3CE70257C034953A606A4E3C099AC7FC4A`

## Limitations

Post-GC failure remains untested. Phase 58 intentionally stops before the failed worker’s GC workload. It does not add concurrent failed workers, thread pools, arbitrary managed threading, async/Task support, generalized exception propagation, or a general managed-root API.

The smallest Phase 59 step would be a separately gated, single-worker post-GC diagnostic point after the existing root-survival proof, with a new ownership audit before any implementation change.

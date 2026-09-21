# Phase 53 final NativeAOT managed-kernel acceptance and closure

Date: 2026-09-20

Outcome: **Outcome A — current final source revalidated; Phase 53 accepted and closed**

The preceding narrow Outcome B repairs are already present in the starting
`d844cf72` closure commit. This audit made no new production repair; it
revalidated that final source and preserved those repairs.

## Scope and original problem

Phase 53 began as the bounded managed-kernel CSS image-resource phase. Its
acceptance boundary expanded when the same NativeAOT path exposed failures in
managed entry, GC relocation, worker-object allocation, scheduler context
switching, GS/TLS ownership, runtime thread state, and callback/VM lifetime.
The final NativeAOT boundary was to prove a real managed execution context can
enter from the kernel, run through scheduler-created workers, survive GC, and
return through callback teardown without stale runtime or VM ownership.

This record is the integrated closure audit of the Phase 53A–Y evidence chain.
It does not start Phase 54.

## Live preflight

| Item | Result |
| --- | --- |
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `d844cf72f8091586d732243867cb2301058c0ec8` — `Close NativeAOT managed-kernel Phase 53` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Live starting ahead/behind | `0 / 0` |
| Starting worktree | Clean |
| Expected-state comparison | The supplied `2f711cc` / `2 ahead` state was stale. The live branch already contained the coherent closure commit at `d844cf72`, and its configured upstream pointed at that same commit. |

No source change appeared after the live starting HEAD. Relative to the
supplied `2f711cc` parent, only the known closure document, stack-bound
diagnostic repair, and two bounded acceptance-runner repairs were present.
Existing 53V–53Y evidence and artifacts were retained and reused.

## Current final-source revalidation

The following fresh matrix was run from the live starting HEAD above. The
Phase 53 source contract was taken from `MANAGED_KERNEL_PHASE53_CSS_IMAGE_RESOURCES.md`,
`NATIVEAOT_DURABILITY.md`, `NATIVEAOT_MANAGED_CALLBACK.md`,
`NATIVEAOT_SCHEDULER_THREAD_ATTACH.md`, `NATIVEAOT_GC_SCHEDULER_THREAD.md`,
and the 53V–53Y validation records.

### Fresh host and build evidence

- `Run-SchedulerModelTests.ps1`: `SCHEDULER_MODEL_TESTS=PASSED checks=256`.
- `Run-SchedulerDurabilityHostTests.ps1`: `SCHEDULER_DURABILITY_HOST_TEST=PASS`.
- `Run-SchedulerStackVmHostTests.ps1`: `SCHEDULER_STACK_VM_HOST_TEST=PASS`.
- `Run-NativeAotGcProbeContractTests.ps1`: `PASSED checks=8`.
- `Run-NativeAotCallbackBridgeHostTests.ps1`: host pass and zero external
  references.
- `Run-Phase53WReadableRangeBuildTests.ps1`: Normal EFI
  `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` and
  SyntheticScheduler EFI
  `4681AC05E641A1305817730ED712BB487C6BBAD943D11C2DE13B332AA7AC7D9A`;
  both staged payloads were 730112 bytes with SHA-256
  `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`.
- Dedicated guard Gate 4 EFI:
  `BF234DCF8F903D3900A64F0BE1B151A1C24A3CAA7AF9D104CB3CD38507F96B09`.
- Dedicated lifecycle Gate 4 EFI:
  `B3D192AC3D4B261BCB9C283F1D97914A286C74E826646DDA0421A87AB1699D4F`.
- Dedicated callback/GC/reclaim Gate 4 EFI:
  `71C0F5D95649CAC4156B8E332FC2B45D33CA04649D37CCEB2E652E5E42ACD054`.

The repository pins SDK `10.0.302` with roll-forward disabled, while this
host has only SDK `10.0.401`. The normal `Build-Gate1.ps1` entry point
therefore stopped before compilation with the exact SDK-resolution error. An
explicit out-of-repository SDK 10.0.401 publish proved the toolchain can
publish all exe/shared/static forms, but produced a different 730624-byte
payload (`2969D31849641F38F7EE61453B5E0F808A0B67889B328FF22EC4EEC71B2C2049`),
so it was not substituted. The accepted 730112-byte payload is unchanged
since 53Y and was identity-checked and staged into every fresh Gate 4 build.

### Fresh QEMU evidence

- `Run-Phase53VGuardFreshBoots.ps1`: 3/3 fresh boots passed.
- `Run-NativeAotSchedulerThreadLifecycleFreshBoots.ps1`: 3/3 fresh boots
  passed; serial hashes are retained under
  `artifacts\phase53-final-rerun-lifecycle-boots-utc`.
- `Run-Phase53XCallbackReclaimFreshBoot.ps1`: pass with
  `vmBefore=0x3`, `vmCreated=0x5`, `vmAfter=0x3`, identity `0x5`.
- `Run-NativeAotSchedulerCallbackFreshBoots.ps1`: 3/3 passed.
- `Run-NativeAotSchedulerGcFreshBoots.ps1`: 3/3 passed, including
  `MANAGED_GC_MAIN_OK=1` on each boot.
- `Run-NativeAotManagedCallbackFreshBoots.ps1`: 25/25 fresh final-source
  boots passed under `artifacts\phase53-final-rerun-managed-callback-25`.
  Every boot recorded callback count `5`, baseline `3`, post-state `3`,
  `MANAGED_CALLBACK_POST_STATE_OK=1`, `MANAGED_GC_MAIN_OK=1`, and the
  Phase 53Y pattern `3 -> 5 -> 3` at B1/B2, B3–B5, and B6–B8.
- `Run-SyntheticSchedulerProof.ps1`: 3/3 passed with
  `classification=EXPECTED_HALT`, zero scheduler failures, intact canaries,
  teardown, and restored GS.

Fresh evidence roots are `artifacts\phase53-final-rerun-*`; no generated
logs or binaries from those ignored evidence directories are staged.

Two configuration/environment divergences were reproduced and separated from
the product path: a Gate 4 build without `-AssumeUnspecifiedTimezoneUtc`
stopped at the QEMU `TIME_INVALID_TIMEZONE` diagnostic, and combining the
intentional Phase 53V guard-fault probe with the Phase 53O lifecycle caused
the expected guard-page fault before lifecycle markers. The established
separate guard, lifecycle, and callback/GC fixtures passed. An initial X
runner invocation used the lifecycle fixture, which correctly lacked the X
callback-2 marker; the dedicated callback/GC fixture passed immediately.
These were fixture selection/environment issues, not production defects.

## Contract checklist

The Phase 53 documentation chain (`MANAGED_KERNEL_PHASE53_CSS_IMAGE_RESOURCES.md`,
the NativeAOT durability/GC/callback documents, and the 53V–53Y validations)
defines the following proven boundary. The final matrix below revalidated it:

- NativeAOT managed entry and continued managed execution.
- Scheduler ownership, saved/restored context, worker creation, and worker lifetime.
- Usable worker stack ownership, RSP bounds, and the separate non-present guard page.
- GS/TLS/FLS ownership and isolation, including post-switch restoration.
- NativeAOT `Thread*` state, allocation-context, and managed-worker-object integrity.
- GC root/relocation, post-GC worker usability, and `MANAGED_GC_MAIN_OK=1`.
- Runtime callback registration, callback execution, deregistration, and detach.
- VM allocation lifetime, reclamation, safe slot reuse, and no stale ownership.
- Baseline-relative acceptance with no stale absolute VM count.
- Normal and SyntheticScheduler build/proof behavior, guard behavior, repeated lifecycle, repeated final-source boots, and clean downstream managed execution.

## Narrow repairs preserved from the prior closure audit

The starting `d844cf72` source already contains the prior audit's repairs. The
first lifecycle boot in that audit exposed a stale diagnostic contract, not a
new scheduler failure. NativeAOT reported the complete reserved stack interval
(`runtime_stack_low`), while the scheduler compatibility `stack_base` denotes
the usable interval low, exactly one 4 KiB guard page above the reservation
base. The first divergence was:

```text
runtime_stack_low = 0x000040000FEC0000
stack_base        = 0x000040000FEC1000
guard             = 0x0000000000001000
```

The repair compares the NativeAOT value with
`thread->stack_contract.reservation_base` and makes the fresh-boot validator
assert `runtime_stack_low + guard_bytes == stack_base`. High bound and RSP
checks remain unchanged. This is the only C implementation change; it does
not alter scheduler, GS, TLS, GC, or VM behavior.

Two bounded acceptance-fixture defects were also corrected:

- `Run-NativeAotCallbackBridgeHostTests.ps1` now passes GCC arguments as an
  argument array, restoring the intended freestanding compile and zero
  external-reference check.
- `Run-SyntheticSchedulerProof.ps1` now checks the current authoritative
  payload hash instead of an obsolete pre-53Y payload hash.

Focused validation passed after each correction, followed by the integrated
matrix below. No speculative production scheduler, TLS, GS, GC, EFI, or VM
change was made.

## Final acceptance evidence

### Host and build validation

| Gate | Result |
| --- | --- |
| Scheduler model | `SCHEDULER_MODEL_TESTS=PASSED checks=256` |
| Scheduler durability | `SCHEDULER_DURABILITY_HOST_TEST=PASS` |
| Scheduler stack VM | `SCHEDULER_STACK_VM_HOST_TEST=PASS` |
| GC probe contract | `NATIVEAOT_GC_PROBE_CONTRACT_TESTS=PASSED checks=8` |
| Callback bridge | `NATIVEAOT_CALLBACK_BRIDGE_HOST_TESTS=PASSED`; `NATIVEAOT_CALLBACK_BRIDGE_NO_EXTERNAL_REFERENCES=PASS` |
| Phase 53W Normal | PASS; EFI SHA-256 `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` |
| Phase 53W SyntheticScheduler | PASS; EFI SHA-256 `4681AC05E641A1305817730ED712BB487C6BBAD943D11C2DE13B332AA7AC7D9A` |
| Phase 53W payload | Both modes used 730112-byte payload SHA-256 `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |

### Fresh-boot matrix

| Gate | Result |
| --- | --- |
| Phase 53V guard | 3/3 fresh boots; guard fault, non-present state, GS/TEB integrity, unrelated-state integrity, and two-worker coherency all passed. Guard EFI SHA-256 `BF234DCF8F903D3900A64F0BE1B151A1C24A3CAA7AF9D104CB3CD38507F96B09`. |
| Phase 53O/lifecycle | 3/3 fresh boots after repair. Lifecycle EFI SHA-256 `B3D192AC3D4B261BCB9C283F1D97914A286C74E826646DDA0421A87AB1699D4F`. |
| Phase 53X reclaim/reuse | PASS: `vmBefore=0x3`, `vmCreated=0x5`, `vmAfter=0x3`; reclaimed identity was reused without stale state. |
| Scheduler callback | All 3 boot bodies passed every callback, switch, FLS, guard, reclaim, and baseline-relative assertion. The runner's final cleanup check raced an unrelated QEMU workload after the third completed boot; no QEMU process remained afterward. |
| Scheduler GC | 3/3 fresh boots passed allocation, worker attach/repeat/return, GC, reclaim, and post-state checks. |
| SyntheticScheduler smoke | 3/3 fresh boots passed with `classification=EXPECTED_HALT` and current payload hash. |
| Final-source callback soak | 25/25 fresh boots passed; all 25 captured baseline `3`, post-state OK, and `MANAGED_GC_MAIN_OK=1`. This exceeds the established 12-cycle acceptance workload. |

The all-feature integrated gate EFI SHA-256 was
`71C0F5D95649CAC4156B8E332FC2B45D33CA04649D37CCEB2E652E5E42ACD054`; its
payload was the same authoritative 730112-byte hash above.

Representative current serial evidence records:

```text
MANAGED_CALLBACK_VM_BASELINE=0x0000000000000003
PHASE53Y_B1/B2_VM_COUNT=0x3
PHASE53Y_B3/B4/B5_VM_COUNT=0x5
PHASE53Y_B6/B7/B8_VM_COUNT=0x3
NATIVEAOT_DURABILITY_BASELINE_VM_REGION_COUNT=0x3
NATIVEAOT_DURABILITY_AFTER_CLEANUP_VM_REGION_COUNT=0x3
MANAGED_CALLBACK_POST_STATE_OK=1
MANAGED_GC_MAIN_OK=1
```

The lifecycle serial proof also records runtime state `1 -> 2`, transition
frame `UINT64_MAX`, FLS after clear `0`, distinct worker allocation contexts,
reserved low `0x...FEC0000`, usable low `0x...FEC1000`, identical high bounds,
and worker RSP inside the usable interval.

## Final architecture invariants

Future work must preserve these implementation-backed invariants:

1. NativeAOT's stack bounds describe the reserved interval; the scheduler's
   compatibility `stack_base` describes the usable interval. The non-present
   guard page is explicit and must remain outside the usable stack.
2. `GS+0x10` and `TEB+0x10` identify the usable worker-stack low address; GS,
   TEB, FLS, and saved RSP are restored with the worker context.
3. A scheduler-created managed worker has a coherent NativeAOT runtime thread,
   allocation context, managed worker object, and runtime state before it runs.
4. Callback registration and deregistration are paired. Detach/reclaim must
   complete before VM records or worker identities are reused.
5. The durable VM baseline is measured from the current runtime. The current
   proven baseline is three records: boot stack, finalizer guard, and finalizer
   usable stack. Callback resources may raise the live count to five and must
   return to three.
6. GC relocation and worker continuation are accepted only when the managed
   root, worker object, allocation context, and post-GC execution all remain
   valid.

## Known limitations outside Phase 53

This closure does not claim a general managed thread pool, arbitrary managed
thread API, Task/async scheduling, exception propagation across every native
boundary, reflection/dynamic loading, APC/COM integration, or a broader
multi-process runtime contract. Those are separate future scopes, not Phase 53
defects.

## Closure statement

The complete current-source matrix is green after the narrow diagnostic and
acceptance-fixture repairs. **Phase 53 is accepted and closed.**

The smallest logical Phase 54 starting point is a separately scoped managed
thread-lifetime contract beyond the proven callback/GC worker pair—first a
single additional worker create/run/detach/reclaim workload with its own
ownership ledger. It is intentionally not implemented here.

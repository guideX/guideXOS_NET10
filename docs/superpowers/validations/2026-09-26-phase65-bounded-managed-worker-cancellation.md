# guideXOS C# .NET 10 — Phase 65 Bounded Managed-Worker Cancellation

Date: 2026-09-26
Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
Branch: `nativeaot-managed-kernel-integration`
Starting HEAD: `63de8df908e372b813c557a20709ba502ddefd16` — `Validate capacity-three managed worker API`

## Outcome

**Outcome A — bounded cooperative cancellation validated.**

Phase 65 cancellation is cooperative and checkpoint-based, not asynchronous preemption. A request records sticky state. The worker observes it only at the supported post-root-publication, pre-GC checkpoint and then uses the existing root-release, runtime-detach, scheduler-termination, and reclaim lifecycle.

No cancellation token compatible with .NET, asynchronous interruption, exception injection, cancellation during GC/detach, callback mechanism, queue, or worker-pool behavior was added. Cancellation support in this phase is limited to a `GC_CHECK` request with managed-root support.

## API Contract

The caller-facing operation is:

`gxos_nativeaot_managed_worker_api_request_cancel(api, handle)`

The handle remains opaque and consists of scheduler slot, worker identity, and generation. The function never accepts a TCB or runtime `Thread*`.

| Status | Value | Meaning |
| --- | ---: | --- |
| `OK` | 0 | This generation accepted the sticky request. |
| `INVALID_HANDLE` | 5 | The handle is malformed or unknown. |
| `STALE_HANDLE` | 6 | The slot has another generation/identity, or the saved handle is stale. |
| `CANCELED` | 14 | Completion result status for a worker that observed cancellation. |
| `CANCEL_ALREADY_REQUESTED` | 15 | A request is already sticky; cleanup remains single-shot. |
| `CANCEL_ALREADY_COMPLETED` | 16 | The worker completed, failed, or was canceled. Its result is unchanged. |
| `CANCEL_ALREADY_CLOSED` | 17 | The exact generation was closed and reclaimed. |
| `CANCEL_UNSUPPORTED_STATE` | 18 | The operation/state has no supported checkpoint, including a request after the checkpoint. |

The accepted request states are `SUBMITTED` (before worker execution) and `RUNNING` while the supported checkpoint is open. A request made while submitted remains sticky until observed at that checkpoint. A request after the checkpoint is rejected as unsupported; a completed worker is never relabeled canceled.

API state transitions are `CREATED → SUBMITTED → RUNNING → COMPLETED → CLOSED` for success and `SUBMITTED/RUNNING → CANCEL_REQUESTED → CANCELED → CLOSED` for cancellation. Internal failure remains `FAILED`.

`poll` returns status `CANCELED` and a copied result containing the operation ID and original slot/identity/generation. `output0`, `output1`, `managed_result`, and `result_code` are zero, so the canceled `GC_CHECK` has no successful-operation payload. `close` copies this result before releasing the API slot. No result points into reclaimed worker state.

## Supported Checkpoint and Cleanup

The only Phase 65 cancellation checkpoint is after:

1. Runtime attach succeeded.
2. `ManagedRootPublish` succeeded and lifecycle ownership was recorded.
3. The root ledger includes the worker.

It is before GC invocation. The worker checks its generation-local request flag at this point. If set, it releases only its own root through the existing managed-root release bridge, records canceled completion, detaches through the existing lifecycle guard, clears runtime FLS ownership, terminates, and awaits normal API close/reclaim.

For the canceled worker, assertions require root publication count `1`, root release count `1`, GC count `0`, root-survival callback count `0`, post-GC continuation count `0`, detach count `1`, cleared FLS, terminated scheduler TCB, and reclaimed lifecycle state after close. Duplicate requests do not repeat any cleanup step.

The Phase 65 fixture uses scheduler yields to coordinate the checkpoint. It calls the public cancellation operation while B is parked. Separate test-only holds keep A and C inspectable until B is reclaimed; those holds are not cancellation points and are not part of the production request semantics. Cancellation does not map to the Phase 58 failure-injection enum.

## Authoritative Scenario

- **A:** `ADD_ONE`, remains attached with a correct result during B cancellation and completes normally.
- **B:** `GC_CHECK`, target. It publishes one root, pauses before GC, accepts cancellation, observes it, runs cancellation cleanup, and is reclaimed.
- **C:** `GC_CHECK`, healthy root-bearing peer. Its root remains live when B's root is released and while B is reclaimed. C then completes GC, validates its root, rejects B's stale root token, and releases its own root.
- **D:** `ADD_ONE`, created after B is reclaimed. It reuses B's scheduler/API slot with a different identity and generation and completes normally.

On the first final verification boot, B was slot `3`, identity `0x30`, generation `0x10`, runtime `Thread*` `0x52D2030`, and root token `0x6730`. D reused slot `3` with identity `0x32` and generation `0x11`.

At overlap, the API held three workers and the root ledger held B and C (`0 → 2`). After B's cancellation cleanup it was `2 → 1`; after C's normal completion it was `1 → 0`. The guest proof separately rejects invalid, wrong-generation, post-checkpoint, completed, closed, and stale-B requests. Stale B cancellation and stale B API use are rejected after D reuses the slot.

Healthy completion-order signatures observed across the 12 scenarios were `0x123` (A, C, D) and `0x213` (C, A, D). Creation and close orders also rotate across scenarios.

## Resource Accounting

Baseline and final values match. Peak and intermediate values are from the first final-verification boot.

| Point | VM regions | Scheduler threads | Scheduler objects | API workers | Root ledger |
| --- | ---: | ---: | ---: | ---: | ---: |
| Baseline | 3 | 2 | 13 | 0 | 0 |
| A/B/C live; B and C roots live | 9 | 5 | 16 | 3 | 2 |
| Cancellation requested | 9 | 5 | 16 | 3 | 2 |
| B canceled, before reclaim | 9 | 5 | 16 | 3 | 1 |
| B reclaimed; A/C remain live | 7 | 4 | 15 | 2 | 1 |
| D created; A/C still held | 9 | 5 | 16 | 3 | 1 |
| Peak | 9 | 5 | 16 | 3 | 2 |
| Final | 3 | 2 | 13 | 0 | 0 |

## Validation

### Phase 65

- Host command: `tools/Run-Phase61ManagedWorkerApiHostTests.ps1` — Phase 61, 62, 64, and 65 host markers all passed. The Phase 65 cases cover valid/pre-checkpoint request, sticky/duplicate request, invalid and stale handles, wrong generation, unsupported ADD_ONE and late checkpoint, completed and closed results, canceled payload suppression, capacity recovery, and same-slot generation reuse.
- Normal-scheduler Phase 65 build passed. EFI SHA-256: `8748AECB532BD4CA52CC05C4906344E1DF972DAAFCFCF9662497ECB61A2AE7C9`. Managed payload SHA-256: `D818513EA4C8308CFFF7C1F570C2744762DBC3225B3E18D3733F0ED0DE5DA7F0`.
- QEMU runner `tools/Run-Phase65BoundedManagedWorkerCancellationFreshBoots.ps1`: **3 fresh boots passed, 12/12 scenarios each (36 canceled workers total)**. Each boot reported `MANAGED_GC_MAIN_OK=1`, `PHASE53O_PASS=1`, callback count `43`, root transition `0>2>1>0`, capacity recovery, same-slot reuse, peer isolation, and final baseline restoration.
- Evidence: `artifacts/phase65-final-evidence3-20260926/runs/run-1..3/serial.log`; runner summaries are alongside each run.

### Regressions and build variants

| Coverage | Result |
| --- | --- |
| Phase 64 capacity-three, no cancellation | 3 fresh boots; 12 scenarios and 36 primary lifecycles per boot passed. Final evidence: `artifacts/phase65-phase64-final-evidence3-20260926/runs/run-1..3/serial.log`. |
| Phase 62 two-worker API | 3 fresh boots; 12 pairs per boot passed. |
| Phase 61 sequential API | 3 fresh boots; 12 cycles per boot passed. |
| Phase 60 post-root-release/pre-detach rollback | 3 fresh boots; 12 cycles per boot passed. |
| Phase 54 managed-worker ownership | 3 fresh boots; 12 cycles per boot passed. |
| Phase 56–60 failure-seam host coverage | All five host runners passed. |
| SyntheticScheduler | Build passed; 3 QEMU scheduler proofs passed with expected halt. |
| Phase 53 | Phase 65 guest reported `PHASE53O_PASS=1` and `MANAGED_GC_MAIN_OK=1`; Phase 54 and Phase 61–65 guest regressions exercised attach, GC/root, context, FLS, reclaim, and reuse paths. |

The Phase 65 final serial captures the B handle/runtime pointer/root token, status values, B's suppressed GC and single release/detach, C's surviving root and successful GC, post-B-reclaim worker counts, D's generation, completion orders, and baseline-relative resource values.

## Limitations

Cancellation is limited to the supported `GC_CHECK` root-before-GC checkpoint. There is no asynchronous preemption. Requests after that checkpoint return `CANCEL_UNSUPPORTED_STATE`; cancellation during GC, runtime detach, arbitrary managed instructions, or after side effects is not supported. Close remains the normal reclaim boundary.

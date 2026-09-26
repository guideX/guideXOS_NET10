# Phase 61 — First Bounded Managed-Worker API

Date: 2026-09-25
Result: Outcome A — the bounded API is implemented and passes host validation, three fresh Phase 61 QEMU boots, the Phase 54 ownership regression, and the Phase 60 post-root-release regression.

## Scope

Phase 61 adds the first reusable managed-worker service boundary above the accepted Phase 53O–60 lifecycle. It is intentionally a bounded operation API, not a general threading or task runtime. The implementation creates one suspended scheduler worker, accepts one fixed request, drives the existing scheduler dispatch path, returns one copied result, and requires explicit close after completion or failure.

The new public header is `src/Gate4Harness/nativeaot_managed_worker_api.h`. The implementation is `src/Gate4Harness/nativeaot_managed_worker_api.c`. The public surface exposes only fixed request, result, and value-handle data plus aligned opaque API storage. Raw scheduler handles, TCB pointers, lifecycle records, and probe pointers remain private to the implementation.

## Public contract

The API version is `1`. Requests are fixed-size 32-byte values:

| Field | Contract |
| --- | --- |
| `version` | Must be `1` |
| `size` | Must equal `sizeof(GXOS_NATIVEAOT_MANAGED_WORKER_REQUEST)` (`0x20`) |
| `operation_id` | `ADD_ONE` or `GC_CHECK` |
| `payload_size` | Must be zero in Phase 61; payload capacity is 16 bytes for later bounded extensions |
| `argument0` | `ADD_ONE`: `0..0xFFFE`; `GC_CHECK`: `0..0xFFFF` |
| `argument1` | Must be zero |
| `payload` | No pointers, callbacks, or caller-owned addresses |

The caller-visible handle is a 12-byte value containing scheduler slot, worker identity, and worker generation. It is not a raw scheduler handle and does not expose a TCB pointer. A copied result contains the API version and size, terminal state/status, operation ID, result code, two bounded outputs, the managed return value, and the value handle.

Supported operations are deliberately small:

- `ADD_ONE` invokes the existing managed callback bridge with a bounded integer and returns the incremented low word plus the callback-count word.
- `GC_CHECK` invokes the existing managed GC probe bridge, validates the result with the existing GC contract, records root survival in the lifecycle ledger, and returns collection delta plus the checksum low word.

The state machine is:

```text
FREE -> CREATED -> SUBMITTED -> RUNNING -> COMPLETED -> CLOSED
                                      \-> FAILED -> CLOSED
```

The implementation rejects invalid request version/size, unsupported operations, duplicate submit, poll-before-complete, close-before-complete, invalid handles, stale handles, and duplicate close. After close, the copied result remains caller-owned while the active record is cleared and the closed value handle is retained only for stale/duplicate rejection.

## Lifecycle and ownership mapping

The API uses the accepted Phase 53O lifecycle functions without replacing or weakening them:

1. `create` calls `gxos_scheduler_create_suspended_thread` and `gxos_nativeaot_scheduler_worker_prepare`.
2. `submit` validates the fixed request, resumes the scheduler thread, marks the lifecycle runnable, and transitions `CREATED -> SUBMITTED`.
3. The worker entry marks the lifecycle running, validates the captured worker register snapshot, invokes exactly one known managed bridge, validates the operation result, and uses the existing detach guard.
4. `drive` calls `gxos_scheduler_main_dispatch`; the caller never supplies an arbitrary entry point or callback.
5. `poll` returns only the copied terminal result.
6. `close` requires a terminated worker in `RUNTIME_DETACHED`, records `RECLAIMABLE`, closes the scheduler handle, collects the scheduler, records `RECLAIMED`, and verifies the TCB is no longer live.

The opaque storage is 768 bytes and is checked at compile time against the private context size. This is intentionally a small embedded service context for the current one-at-a-time API; it is not a heap allocator or an unbounded worker registry.

## Validation

Focused host validation:

```text
PHASE61_MANAGED_WORKER_API_HOST_TEST=PASS
```

The host test covers request/result/handle/storage sizes, valid and invalid request contracts, transition legality, operation bounds, handle equality, and invalid-handle behavior.

The Phase 61 boot runner is `tools/Run-Phase61ManagedWorkerApiFreshBoots.ps1`. It requires at least three fresh boots, checks the authoritative payload hash, rejects fault/failure markers, verifies all API markers, requires exactly 12 cycles and 12 results per boot, checks VM/thread/object baselines, and checks the managed callback count.

Fresh Phase 61 result:

```text
PHASE61_API_RUN_1=PASS cycles=12
PHASE61_API_RUN_2=PASS cycles=12
PHASE61_API_RUN_3=PASS cycles=12
PHASE61_API_PAYLOAD_SHA256=AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012
PHASE61_API_RUNS=3
```

The final Phase 61 image used the exact authoritative managed payload: 730,112 bytes, SHA-256 `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`.

| Artifact | Size | SHA-256 |
| --- | ---: | --- |
| Phase 61 final EFI | 571,712 | `21D9694E444F8EDF1526CDAA3EB4741E2549CAC8DAB60392D780C3787E856E28` |
| Phase 61 host-test executable | 154,955 | `34B92569ACF99D8DEA1F9A07B185E74DB1F6F4B777E2000AC401535B075B8A00` |
| Normal build EFI | 155,051 | `660A6D7277C5BE37BD012702AA9560B66EBBCC7817433E6976CD2AC3CEF87F8F` |
| SyntheticScheduler build EFI | 154,425 | `0255209EDCD0276250D76D5917ECB4874F26897F0D7E0A8ECD435D84F96AA175` |

Evidence directories:

- `artifacts/phase61-evidence-final5`
- `artifacts/phase61-managed-worker-api-host-tests`
- `artifacts/phase61-normal-build`
- `artifacts/phase61-synthetic-build`

## Regression validation

Phase 54 ownership regression was rebuilt from the current source and passed three fresh boots:

```text
PHASE54_OWNERSHIP_RUN_1=PASS cycles=12 peakVm=0x7 peakThreads=0x4 peakObjects=0xF
PHASE54_OWNERSHIP_RUN_2=PASS cycles=12 peakVm=0x7 peakThreads=0x4 peakObjects=0xF
PHASE54_OWNERSHIP_RUN_3=PASS cycles=12 peakVm=0x7 peakThreads=0x4 peakObjects=0xF
PHASE54_OWNERSHIP_PAYLOAD_SHA256=AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012
```

Phase 54 image: 563,334 bytes, EFI SHA-256 `BB2A19A230224F4912202BC0E52066A1E4F6EA6151894DB8DB1B04072487FE81`. Evidence: `artifacts/phase61-regression-phase54-evidence`.

The Phase 60 post-root-release/pre-detach regression also passed three fresh boots:

```text
PHASE60_POSTROOTRELEASE_ROLLBACK_RUN_1=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
PHASE60_POSTROOTRELEASE_ROLLBACK_RUN_2=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
PHASE60_POSTROOTRELEASE_ROLLBACK_RUN_3=PASS cycles=12 peakVm=0x5 peakThreads=0x3 peakObjects=0xE
PHASE60_POSTROOTRELEASE_ROLLBACK_PAYLOAD_SHA256=2E25F807194864CF667FA2B10D72D608247518D3C4A2C02DF691A9D7FCB16FCC
```

Phase 60 image: 583,965 bytes, EFI SHA-256 `5D2204E851F8E559DBD99A39A94B75A13EFD584240B29B5F172C0D533DCDF492`. Evidence: `artifacts/phase61-regression-phase60-evidence-r4`.

The Phase 54 and Phase 60 regression boots used an isolated executable copy for QEMU because an unrelated pre-existing QEMU process was intentionally left untouched. The Phase 61 runner likewise preserves baseline process IDs and stops only processes it starts.

## Deliberate limits

Phase 61 is not a general threading API. It does not expose arbitrary managed method pointers, arbitrary native entry points, caller callbacks, pointer-bearing payloads, cancellation, waits, priorities, multiple simultaneous records, or a general-purpose task scheduler. Those capabilities require a separate contract and must continue to use the established ownership/lifecycle ledgers rather than bypassing them.

Phase 62 was not started.

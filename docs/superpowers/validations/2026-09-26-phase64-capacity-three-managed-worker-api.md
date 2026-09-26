# guideXOS C# .NET 10 — Phase 64 Capacity-Three Managed-Worker Validation

Date: 2026-09-26
Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
Branch: `nativeaot-managed-kernel-integration`

## Outcome

Outcome B: capacity three was validated after narrow bounded repairs. The API
capacity is exactly `3`; the managed root facility is independently bounded at
`2`. Three API-created workers coexist with three runtime attachments, two
independent root-bearing `GC_CHECK` workers, deterministic reclaim in three
orders, clean fourth-worker admission rejection, and an injected
`AFTER_RUNTIME_ATTACH` failure that unwinds through the API while two healthy
peers remain live.

Capacity 3 is validated only because all three workers coexist successfully and
the two-root case is proven. This is not a general-N or runtime-configurable
capacity change. Phase 65 was not started.

## Measured contract

The authoritative first serial log was:

`artifacts/phase64-evidence-final4/runs/run-1/serial.log`

| Field | Measured value |
|---|---:|
| API capacity | `3` |
| Managed root capacity | `2` |
| Scheduler thread capacity | `6` |
| Scheduler object capacity | `16` |
| Baseline VM regions | `0x3` |
| Three-worker peak VM regions | `0x9` |
| Final VM regions | `0x3` |
| Baseline scheduler threads | `0x2` |
| Three-worker peak scheduler threads | `0x5` |
| Final scheduler threads | `0x2` |
| Baseline scheduler objects | `0xD` |
| Three-worker peak scheduler objects | `0x10` |
| Final scheduler objects | `0xD` |
| Baseline API workers | `0` |
| Peak API workers | `3` |
| Final API workers | `0` |
| Baseline root ledger | `0` |
| Peak root-bearing workers | `2` (`PHASE64_ROOT_OVERLAP_ROOTS=2`) |
| Final root ledger | `0` |
| Maximum attached workers | `3` |
| Maximum root workers | `2` |

The scheduler object peak reaches, but does not exceed, the configured table
capacity: `13 + 3 = 16`. The fourth API create returns status `0xD` (capacity)
while VM regions, scheduler threads, scheduler objects, and the scheduler next
identity remain unchanged. No lower-layer allocation is attempted.

## Worker and root proof

The first scenario created the following independent records:

| Field | Worker A | Worker B | Worker C |
|---|---:|---:|---:|
| Handle slot | `0x2` | `0x3` | `0x4` |
| Worker identity | `0x7` | `0x8` | `0x9` |
| Generation | `0x4` | `0x3` | `0x1` |
| Scheduler TCB | `0x19BA70` | `0x19BF40` | `0x19C410` |
| Stack reservation | `0x40000FEC0000` | `0x40000FEE0000` | `0x40000FF00000` |
| Guard VM identity | `0xC` | `0xE` | `0x10` |
| Saved RSP | `0x40000FED0FF8` | `0x40000FEF0FF8` | `0x40000FF10FF8` |
| Runtime `Thread*` | `0x52F6030` | `0x52D2030` | `0x52BD030` |
| Allocation context | `0x52F6038` | `0x52D2038` | `0x52BD038` |
| TLS vector | `0x52F7000` | `0x52D3000` | `0x52BE000` |
| TLS block | `0x52F6000` | `0x52D2000` | `0x52BD000` |
| FLS value | `0x52F6030` | `0x52D2030` | `0x52BD030` |
| Operation | `ADD_ONE` | `GC_CHECK` | `GC_CHECK` |
| Result | `42` | delta `1`, checksum `0xD00` | delta `1`, checksum `0x708` |

At the three-worker overlap checkpoint, API workers were `3`, threads were
`0x5`, objects were `0x10`, and the measured maximum attached count was `3`.
The baseline-plus-three ThreadStore/lifecycle accounting remained valid and
the final count restored to baseline.

B and C published distinct bounded managed root tokens:

- B: `0x6708`, root survived GC: `1`.
- C: `0x6809`, root survived GC: `1`.

The maximum simultaneous root-bearing worker count was `2`. B release while C
remained live and C release while B remained live both passed. A previously
published root token was rejected as stale across scenarios. The managed side
uses exactly two static root slots with token/generation-safe publication,
validation, and release; it does not alias the two workers to one root.

Request storage is copied at submit. Post-submit caller mutations did not alter
A/B/C requests. Polling and reclaiming one worker did not change another
worker's result, handle, runtime thread, root, stack, or lifecycle record.

## Completion, reclaim, failure, and recovery

The 12 deterministic scenarios exercised the completion/reclaim order families
`A_B_C`, `C_B_A`, and `B_A_C`. The first three scenarios explicitly proved:

1. reclaim A while B and C remain live;
2. reclaim B while A and C remain live;
3. reclaim C while A and B remain live.

Every scenario returned VM, thread, object, API-worker, and root accounting to
its per-scenario baseline. The boot-level final checkpoint returned to the
original baseline as well.

The attach-failure fixture arms the existing
`GXOS_NATIVEAOT_FAILURE_INJECTION_AFTER_RUNTIME_ATTACH` point through the API.
The failed worker acquired runtime ownership (`attach_count=1`), then detached
exactly once (`detach_count=1`) and entered the API `FAILED` state with internal
failure status. Its failed runtime thread was `0x52BD030`; the two healthy-peer
checkpoint remained VM `0x9`, threads `0x5`, objects `0x10`. Failed-worker
cleanup completed, the stale failed handle was rejected, and the healthy peer
results remained valid.

Capacity reopened after failed-worker close. A replacement worker succeeded in
the failed worker's slot (`0x4`) with an advanced identity/generation, and the
stale failed handle and stale root token were rejected. Final cleanup restored
all resource counts.

## Validation matrix

| Validation | Result |
|---|---|
| Host API model | `PHASE61_MANAGED_WORKER_API_HOST_TEST=PASS`, `PHASE62_MANAGED_WORKER_API_HOST_TEST=PASS`, `PHASE64_MANAGED_WORKER_API_HOST_TEST=PASS` |
| Phase 64 Normal QEMU | 3/3 fresh boots; 12 scenarios per boot; 36 primary three-worker lifecycles per boot |
| Phase 64 total primary lifecycles | 108 across three boots; attach-failure fixture adds 6 healthy-peer, 3 failed, and 3 replacement lifecycles |
| Phase 62 regression | 3/3 fresh boots, 12 pairs per boot; capacity-three compatibility branch passed |
| Phase 61 regression | 3/3 fresh boots, 12 sequential cycles per boot |
| Phase 60 regression | 3/3 fresh boots, 12 rollback cycles per boot |
| Phase 59 regression | 3/3 fresh boots, 12 rollback cycles per boot |
| Phase 58 regression | 3/3 fresh boots, 12 rollback cycles per boot |
| Phase 57 regression | 3/3 fresh boots, 12 rollback cycles per boot |
| Phase 56 regression | 3/3 fresh boots, 12 rollback cycles per boot |
| Phase 54 regression | 3/3 fresh boots, 12 ownership cycles per boot |
| Phase 53 core regression | Phase 53O scheduler/context, stack/VM, attach/detach, GC/root, reclaim/reuse markers and `MANAGED_GC_MAIN_OK=1` remained green in the Phase 54/62 serial proofs |
| Normal build | Passed; EFI 606,783 bytes |
| SyntheticScheduler build | Passed; EFI 154,425 bytes |
| SyntheticScheduler guest proof | 3/3 fresh QEMU runs passed with `EXPECTED_HALT` classification |
| Leak trend | None; every run restored baseline |

The separate Phase 53V guard-fault experiment is not the Phase 53 core gate
requested by Phase 64 and is not used as acceptance evidence here.

## Defects found and repairs

The following bounded defects were found during implementation and repaired:

- The public API capacity, opaque storage, record arrays, and host model were
  raised from two to exactly three; root storage was independently bounded at
  two.
- Managed root publication/release/validation changed from a singleton to two
  bounded static slots with duplicate/stale rejection.
- The API fixture now validates all three workers, two roots, fourth-create
  admission, three reclaim orders, and API-path attach rollback/recovery.
- The Phase 62 validator and overlap bookkeeping retain the historical
  capacity-two branch while accepting the explicit capacity-three regression
  marker.
- The scheduler assembly handoff was corrected to pass the single
  `gxos_scheduler_prepare_yield` argument in `rcx`, matching the compiled
  Microsoft x64 ABI. The former `rdx` handoff overwrote the API context under
  the capacity-three optimized register layout.
- An injected post-attach failure now forces the API record to `FAILED` after
  the existing detach authority runs; stale root capture occurs after a real
  completed worker lifecycle.
- Root peak accounting now reports the measured maximum root-bearing worker
  count (`2`) while final root ledger balance remains `0`.

No runtime-configurable capacity, cancellation, fourth-worker test, speculative
scheduler-capacity increase, unrelated failure framework, or Phase 65 work was
introduced.

## Artifacts and evidence

Final Phase 64 evidence:

- Normal build: `artifacts/phase64-normal-final4`
- Fresh-boot evidence: `artifacts/phase64-evidence-final4`
- Runner: `tools/Run-Phase64CapacityThreeManagedWorkerFreshBoots.ps1`
- Normal EFI: 607,295 bytes, SHA-256
  `7067EBC98A2B93938BC529382FC96E6F64DF46FF078DB2B451AAE13EABAAE37C`
- Managed payload: 733,696 bytes, SHA-256
  `D818513EA4C8308CFFF7C1F570C2744762DBC3225B3E18D3733F0ED0DE5DA7F0`
- Run 1 serial SHA-256:
  `262623FF6EF869E7BFED88FD5746AD137208E20A3BD4C9AC78A69B08860AA986`
- Run 2 serial SHA-256:
  `E7902F4FBD0C4607242B0B1A57E4542B3CEB241A3B60C7CA926B96FB1FFC2758`
- Run 3 serial SHA-256:
  `B32CAF671F6ED10019B9D2EF26EE8BC3775BB518ECE39B137CFF892F05E76303`

SyntheticScheduler evidence:

- Build: `artifacts/phase64-synthetic-final3`
- Guest proof: `tools/Run-SyntheticSchedulerProof.ps1`
- EFI: 154,425 bytes, SHA-256
  `F6D26C97D4DAA1FC4188C8DCCC531460091C421C8F3198648191BE7401187568`
- Payload SHA-256:
  `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`

Host evidence:

- Runner: `tools/Run-Phase61ManagedWorkerApiHostTests.ps1`
- Binary: `artifacts/phase61-managed-worker-api-host-tests/phase61-managed-worker-api-host-tests.exe`
- Binary SHA-256:
  `DDE29BDA2E7D2BC59F6A823A7DE1D324BFFAB9670F2BCEE5B53EACAB85E85382`

## Git and remaining scope

The live preflight started at commit `88b0be924916e1bbc6522f7be87e28e74db6c932`
(`Document managed worker capacity contract`) on
`nativeaot-managed-kernel-integration`, with actual divergence `0 ahead / 0
behind` from `origin`. The pasted request's expected `1 ahead / 0 behind`
state differed from live Git state; no remotes, credentials, branches,
worktrees, or history were altered to repair it.

The coherent Phase 64 change includes the bounded API/root implementation,
scheduler ABI repair, regression-validator compatibility, host coverage, fresh
boot runner, and this report. The final commit and push result are recorded in
the task handoff after Git review. If SSH push remains unavailable, the local
commit is retained without changing remotes or credentials.

Remaining limitations are intentional: only three API workers and two
simultaneous managed roots are validated; root capacity is not generalized to
three; no cancellation, timeout, pool, priority, affinity, arbitrary delegate,
persistent worker, or runtime-configurable capacity exists. The smallest Phase
65 step, if later authorized, is a separately scoped bounded follow-up chosen
after reviewing this accepted capacity-three contract. Do not begin it as part
of Phase 64.

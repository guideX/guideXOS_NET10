# guideXOS .NET 10 — Phase 62 concurrent managed-worker API validation

Date: 2026-09-26  
Outcome: **Outcome B — concurrency exposed narrow ownership defects and the repair passed**

## Contract

Phase 62 extends the accepted Phase 61 bounded managed-worker API to exactly two
simultaneously live API records. Worker A submits `ADD_ONE(41)` and Worker B
submits `GC_CHECK(0x61)`. Both records are created and submitted before either
worker is fully reclaimed. The fixture alternates scheduler resume and reclaim
order on twelve pairs per boot; no sleeps, timers, cancellation, pools, or
timing races are used.

The API capacity is exactly two. A third create while A and B are live returns
`GXOS_NATIVEAOT_MANAGED_WORKER_STATUS_CAPACITY` (13). Handles remain opaque
`slot + identity + generation` values; TCB pointers do not cross the API.

## Ownership and isolation proof

The first pair in each final boot reported the following authoritative values:

| Field | Worker A | Worker B |
|---|---:|---:|
| API handle | `(slot=2, identity=7, generation=4)` | `(slot=3, identity=8, generation=3)` |
| scheduler TCB | `0x195A70` | `0x195F40` |
| stack reservation | `0x40000FEC0000` | `0x40000FEE0000` |
| guard VM identity | `0xC` | `0xE` |
| saved RSP | `0x40000FED0FF8` | `0x40000FEF0FF8` |
| runtime `Thread*` | `0x52F7030` | `0x52D3030` |
| allocation context | `0x52F7038` | `0x52D3038` |
| TLS vector | `0x52F8000` | `0x52D4000` |
| TLS block | `0x52F7000` | `0x52D3000` |
| FLS value | `0x52F7030` | `0x52D3030` |
| operation | `ADD_ONE` | `GC_CHECK` |
| result | `42` | collection delta `1`, checksum `0x688` |

The A and B records, request buffers, result buffers, scheduler slots, stacks,
guards, runtime threads, allocation contexts, TLS vectors/blocks, and FLS
values are independently addressable. Caller request copies were mutated after
submit; the workers still observed their original values. Polling A twice did
not change B's result. Forged cross-handle submit, poll, and close operations
were rejected. Duplicate submit was rejected independently for both records.

`PHASE62_RUNTIME_OVERLAP=1` occurred for all 12 pairs. At the overlap
checkpoint the image reported VM regions `0x7`, scheduler threads `0x4`, API
workers `0x2`, and root ledger `0x1`; therefore both scheduler/runtime records
were live at the same authoritative point. The GC worker preserved its root
while the other worker remained live, and the other worker survived the GC
operation.

Completion ordering was proven in both directions: six A-first pairs and six
B-first pairs in each boot. A was reclaimed while B remained live in six pairs;
B was reclaimed while A remained live in six pairs. The surviving worker's
result and ownership state remained valid after the peer reclaim.

After the first pair, a new C worker reused A's scheduler slot (`slot=2`) with
identity `9` and generation `5`; old A and B handles were rejected. This proves
generation advancement and stale cross-worker rejection against a later worker.

## Resource accounting

Final run 1 checkpoints:

| Resource | Baseline | Two-worker peak | Final |
|---|---:|---:|---:|
| VM regions | `0x3` | `0x7` | `0x3` |
| scheduler threads | `0x2` | `0x4` | `0x2` |
| scheduler objects | `0xD` | `0xF` | `0xD` |
| API workers | `0` | `2` | `0` |
| root ledger | `0` | `1` | `0` |

There was no upward leak trend across the 12 pairs. Each pair restored its
per-pair baseline, and the boot-level final checkpoint restored the original
VM, thread, object, API-worker, and root-ledger counts.

## Defects found and repairs

The Phase 61 implementation had an accidental singleton boundary: one active
record, one request/result path, one close history, and one active-operation
assumption. Those were converted to two bounded per-worker records, request
storage, result storage, close histories, and completion-order evidence.

The shared lifecycle helper also assumed that the global ThreadStore count
must increase and decrease by exactly one. That is correct for sequential
workers but false while another runtime-attached worker exists. The concurrent
API marks its lifecycle record as shared-ThreadStore mode, accepts global count
movement in either direction, and verifies that the detached worker's own
runtime thread is absent from the remaining ThreadStore chain. Sequential
Phase 61 retains the original exact-count invariant.

The Phase 62 fixture reloads handles from persistent API records after scheduler
context switches; main-stack/register values are not treated as ownership
authority.

## Validation matrix

- Host API model: `PHASE61_MANAGED_WORKER_API_HOST_TEST=PASS` and
  `PHASE62_MANAGED_WORKER_API_HOST_TEST=PASS`.
- Phase 62 QEMU: 3 fresh boots, 12 pairs per boot, 36 pairs / 72 paired
  worker lifecycles passed; the three required C reuse lifecycles bring the
  total worker lifecycle count to 75.
- Phase 61 sequential regression: 3/3 fresh boots, 12 cycles each.
- Phase 60 rollback regression: 3/3 direct fresh boots.
- Phase 54 two-worker ownership regression: 3/3 direct fresh boots.
- Phase 56, 57, 58, and 59 host rollback tests: all passed; the accepted
  QEMU evidence for those phases remains green.
- Phase 53O markers remained green in the Phase 54 and Phase 62 boots;
  scheduler/context, stack/VM, runtime attach/detach, GC/root, reclaim, and
  reuse coverage remained green.
- Normal Phase 62 EFI build: passed.
- SyntheticScheduler build and 3 direct fresh boots: passed.
- `MANAGED_GC_MAIN_OK=1`: present in all Phase 62 boots.
- Phase 62 was compiled without Phase 56–60 diagnostic injection flags.

## Artifacts

- Documentation: `docs/superpowers/validations/2026-09-25-phase62-concurrent-managed-worker-api.md`
- Phase 62 build: `artifacts/phase62-final-build2`
- Phase 62 evidence: `artifacts/phase62-final-evidence2`
- Phase 62 runner: `tools/Run-Phase62ConcurrentManagedWorkerApiFreshBoots.ps1`
- Phase 62 EFI: 585,638 bytes, SHA-256
  `C8F58E86EC37B0F7267D0FD7CAF66CD18EBEC3F94052B2D255A41FCFE15FDBF8`
- Authoritative managed payload: 730,112 bytes, SHA-256
  `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`
- SyntheticScheduler EFI: 154,425 bytes, SHA-256
  `0255209EDCD0276250D76D5917ECB4874F26897F0D7E0A8ECD435D84F96AA175`
- Host-test executable: 169,922 bytes, SHA-256
  `65E9BCFB9593EA0E9FCCBFDEBD625D3F23E84B8D2A5DBCDE01D41A5F86E97452`
- Final serial hashes:
  - run 1: `EB48360B1D889E01E746C961DAB6A75DFBB439FE02933C31AB8AF1AA14657ACF`
  - run 2: `CB6FC04B61012A5B990178FA71393522204E03BE130C77BDD96BC75D32F3C4EC`
  - run 3: `91FD7CFD5698781302023917400D1945B054722F121876A22968081E31B89F96`

**Phase 62 validates two concurrently live API workers, not a thread pool or general concurrent threading API.**

Phase 63 was not started. Remaining limits are unchanged: exactly two API
workers, bounded fixed operations, no cancellation, pool, timeout, priority,
affinity, arbitrary delegates, persistent worker, `Task`, async/await, or
`System.Threading.Thread` compatibility. The smallest Phase 63 step should be
chosen only after this bounded contract is accepted; no Phase 63 implementation
is included here.

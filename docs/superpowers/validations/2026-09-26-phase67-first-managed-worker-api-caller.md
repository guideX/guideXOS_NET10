# guideXOS C# .NET 10 — Phase 67 First Managed-Worker API Caller Audit

Date: 2026-09-26

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Outcome: **Outcome C — no legitimate production caller is ready for this bounded API**

## Decision

No production caller was added. The source audit found a real managed driver
worker in the serial/interrupt path, but it is a long-lived, event-driven worker
that waits for interrupt notifications and drains repeated dispatch batches.
The Phase 61–66 API is a one-shot service with only `ADD_ONE` and `GC_CHECK`;
neither operation is a legitimate request from that driver. Making the driver
use this API would require a new driver-dispatch operation and changes to the
event-driven worker ownership and scheduling model. A boot-time `ADD_ONE` or a
synthetic `GC_CHECK` would be an artificial caller.

The API also lacks a production initialization boundary. Its initializer takes
`GXOS_PHASE53O_PROBE`, which carries the scheduler, main TCB, managed callback
bridges, VM region counter, runtime FLS cleanup, and lifecycle instrumentation.
The build script compiles the API only under Phase 61/62/64/65/66 flags. The
normal ManagedKernel build does not include or initialize this service. These
are production-readiness gaps to address after selecting a real operation and
service owner; this audit did not redesign the API or add a diagnostic caller.

## Read-only preflight

| Field | Live value |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `d3a8644041dda603fd82fa0aad696e4e89945c34` — `Document Phase 66 validation` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | `0 ahead / 0 behind` |
| Starting worktree | Clean |
| Phase 61 API | `92547c6` — `Add bounded managed worker API`; ancestor of HEAD |
| Phase 64 capacity three | `63de8df` — `Validate capacity-three managed worker API`; ancestor of HEAD |
| Phase 65 cancellation | `66e187e` — `Add bounded managed worker cancellation`; ancestor of HEAD |
| Phase 66 post-GC cancellation | validation report is at HEAD `d3a8644`; implementation history includes `39c05c6` |
| Upstream `39c05c6` | `Phase 32`; ancestor of HEAD and preserved |

The prompt expected `1 ahead / 0 behind`; live Git was `0 ahead / 0 behind`.
HEAD and `origin/nativeaot-managed-kernel-integration` both resolved to
`d3a8644`. The report files for Phases 61, 64, 65, and 66 were read before
caller selection, as was the Phase 62 request-isolation report. Live source was
used for the selection decision.

## Production caller inventory

The repository-wide caller search found calls to
`gxos_nativeaot_managed_worker_api_*` only in the Phase 61–66 API implementation,
conditional loader fixtures, and host tests. There is no normal ManagedKernel
consumer of the API.

The real candidate examined was the production ManagedKernel serial/interrupt
driver worker:

- Native owner: `src/Gate4Harness/managed_kernel_driver_worker.c`
- Managed dispatch policy: `src/ManagedKernel/ManagedDriverWorker.cs`
- Managed dispatch entry: `src/ManagedKernel/ManagedKernel.cs`,
  `GxManagedKernelRunDriverWorker`
- Dispatch state machine: `src/ManagedKernel/ManagedSerialDriver.cs`,
  `RunDriverWorker`

This component handles actual queued serial/keyboard interrupt work. Its native
worker attaches once, invokes managed start, waits on a wake event, dispatches
up to four batches per activation, yields and rearms, then runs explicit stop
and reclaim. Its managed dispatch state persists across events. Those are
legitimate product operations, but they do not fit a create/submit/drive/
complete/close one-shot request without changing service ownership. The public
API's `drive` also dispatches the scheduler from the configured main thread.

Other production ManagedKernel calls found in the boot/service integration are
stateful installation, bind, start, or teardown operations invoked directly
through existing NativeAOT exports. None currently needs bounded offloaded
`ADD_ONE` work, or a GC-root self-check as product behavior. No second candidate
qualified.

## API production-surface audit

| Area | Classification | Source evidence |
|---|---|---|
| Fixed request, opaque value handle, copied result, explicit close, capacity and generation checks | Production-ready mechanics | `src/Gate4Harness/nativeaot_managed_worker_api.h`; submit stores `*request`, poll/close copy result |
| `ADD_ONE`, `GC_CHECK` | Fixture-only operation choices; not suitable production work for the audited driver | `src/Gate4Harness/nativeaot_managed_worker_api.c` request validation and worker dispatch |
| API initialization | **Production blocker** | `initialize` requires `GXOS_PHASE53O_PROBE` with scheduler, main TCB, callback/GC bridges, FLS cleanup, VM counter, and main-thread identity |
| Normal build inclusion | **Production blocker** | `tools/Build-Gate4Harness.ps1` adds the API source only for Phase 61/62/64/65/66 flags |
| `drive` | Constrained service behavior | Requires the configured main thread and pumps `gxos_scheduler_main_dispatch` |
| Phase pause/diagnostic coordination | Fixture-only and harmless to existing tests | Phase 65/66 fixture controls are conditional; no production caller was found |
| Request ownership | Existing public contract is sound for the fixture | Submit copies the fixed request into its private record |
| Result ownership | Existing public contract is sound for the fixture | Poll/close copy the fixed result; it carries the handle generation |

The caller-facing API contract does not hand a TCB, scheduler slot pointer,
NativeAOT `Thread*`, allocation context, root ledger, or FLS pointer to its
callers. Its initializer, however, requires a caller to supply the diagnostic
probe object that contains those internal service dependencies. This prevents
normal production use as a self-contained black-box service today.

## Required boundary before adoption

The smallest next design step is a production-owned managed dispatch service
boundary for the existing serial/interrupt worker. That boundary must define a
fixed dispatch request/result for one actual bounded batch, own initialization
and runtime/scheduler wiring privately, preserve event wake/coalescing and
persistent managed driver state, and map capacity/internal/canceled statuses
to the driver's retry or failure policy. Only after that contract is defined
can the existing event worker be evaluated for a one-shot API adapter without
changing its architecture. This report does not begin that implementation.

## Validation performed

Scripts executed:

- `tools/Run-Phase61ManagedWorkerApiHostTests.ps1`
- `tools/Build-Gate4Harness.ps1 -PayloadMode ManagedKernel -EnableNativeAotStartup`
- `tools/Build-Gate4Harness.ps1 -Scenario SyntheticScheduler`
- `tools/Run-SyntheticSchedulerProof.ps1` — three fresh boots
- `tools/Run-Phase66PostGcManagedWorkerCancellationFreshBoots.ps1` — three fresh boots
- `tools/Run-Phase65BoundedManagedWorkerCancellationFreshBoots.ps1` — three fresh boots
- `tools/Run-Phase64CapacityThreeManagedWorkerFreshBoots.ps1` — three fresh boots
- `tools/Run-Phase62ConcurrentManagedWorkerApiFreshBoots.ps1` — three fresh boots
- `tools/Run-Phase61ManagedWorkerApiFreshBoots.ps1` — three fresh boots
- `tools/Run-Phase60PostRootReleaseRollbackFreshBoots.ps1` — three fresh boots
- `tools/Run-Phase59PostGcRollbackFreshBoots.ps1` — three fresh boots

### Host and build configurations

- `tools/Run-Phase61ManagedWorkerApiHostTests.ps1`: passed all five markers:
  Phase 61, 62, 64, 65, and 66.
- Normal production-capable ManagedKernel build:
  `tools/Build-Gate4Harness.ps1 -PayloadMode ManagedKernel -EnableNativeAotStartup`
  passed. This confirms the existing production integration builds; it does
  not include or exercise the managed-worker API.
- SyntheticScheduler build passed, followed by three fresh QEMU guest proofs;
  all three reached `EXPECTED_HALT`.
- The live Phase 66 regression emitted `MANAGED_GC_MAIN_OK=1`,
  `PHASE53O_PASS=1`, and `PHASE66_PASS=1` on all three boots.

### Fresh guest regressions

All guest regressions used accepted Phase 59–66 build images. The Phase 66 and
SyntheticScheduler runs completed on the installed QEMU path. The first Phase
65 attempt stopped before booting when its runner detected an already-running
system QEMU process. That process was inspected and left untouched; Phase 65
through Phase 59 then ran on the repository's private QEMU copy.

| Regression | Fresh boots | Per-boot result | Total |
|---|---:|---|---:|
| Phase 66 post-GC cancellation | 3 | 12 scenarios; 12 canceled target workers | 36/36 |
| Phase 65 pre-GC cancellation | 3 | 12 scenarios; 12 canceled target workers | 36/36 |
| Phase 64 capacity three | 3 | 12 scenarios; 36 primary lifecycles | 108 primary lifecycles |
| Phase 62 concurrent API | 3 | 12 worker pairs | 36 pairs |
| Phase 61 sequential API | 3 | 12 cycles | 36 cycles |
| Phase 60 post-root-release/pre-detach rollback | 3 | 12 cycles | 36 cycles |
| Phase 59 post-GC rollback | 3 | 12 cycles | 36 cycles |

The Phase 66 run-1 log confirms GC completed once, the logical root survived,
checkpoint 2 observed cancellation, root release and runtime detach each ran
once, FLS was clean, the scheduler thread terminated, and final API-worker and
root counts returned to zero. The Phase 65 run-1 log confirms cancellation at
the pre-GC checkpoint, zero target GCs, and exactly one root release and
detach. Phase 64 confirms capacity `3`, root capacity `2`, fourth-create status
`13` without a lower allocation, two-root overlap, attach-failure rollback,
and capacity recovery. Phase 62 confirms two-worker overlap and slot reuse;
Phase 61 confirms sequential reuse and stale-handle rejection. Phase 60's
post-root-release failure and Phase 59's post-GC failure each completed 12
failure/replacement cycles per boot with single cleanup and successful
replacement.

Phase 66 resource accounting remained baseline restored in each boot. Run 1
reported baseline/peak/final VM regions `3/9/3`, scheduler threads `2/5/2`,
scheduler objects `13/16/13`, API workers `0/3/0`, and root ledger `0/2/0`.
Phase 67 itself has no caller worker or resource snapshots because no
production API request was submitted. The regression had no leak trend.

## Phase 67 caller fields

These values are deliberately not inferred from fixture results:

| Requested field | Phase 67 audit result |
|---|---|
| Selected production caller / caller source path | None selected. Candidate: the event-driven serial/interrupt worker above. |
| Why it qualifies as real production use | It handles actual hardware events, but is not ready to adopt the one-shot API without service/lifetime changes. |
| API entry points used by caller | None |
| Caller touches scheduler or NativeAOT internals | No Phase 67 caller exists; existing worker owner has its own scheduler/lifecycle implementation. |
| Request operation/payload and ownership | Not applicable; no request was submitted. Existing API fixture contract copies fixed-size requests. |
| Result structure/value and ownership | Not applicable; no result was consumed. Existing API fixture contract copies fixed-size results. |
| Handle/generation behavior | No Phase 67 handle created. Existing Phase 61–66 generation/stale-handle suites passed. |
| Capacity-full behavior/partial allocation | Not exercised by a production caller. Phase 64's API capacity/rejection/recovery suite passed. |
| Caller-level error mapping | None; requires the production dispatch service boundary above. |
| Cancellation/checkpoint/result handling | No production caller cancellation. Phase 65 checkpoint 1 and Phase 66 checkpoint 2 regressions passed. |
| Caller normal-build result | No caller/API use. Normal ManagedKernel build passed without Phase flags. |
| Peer coexistence/request and result isolation | No Phase 67 caller scenario. API concurrency/isolation regressions passed in Phases 62 and 64–66. |
| Caller invocations / passes | `0 / 0`; no artificial caller was created. |
| Slot reuse/generation advancement/stale rejection | Not measured for Phase 67; existing API tests passed. |
| Phase 67 baseline/peak/final VM, threads, workers, roots | Not applicable; no Phase 67 API worker was created. Phase 66 regression values are above. |
| Caller leak trend | Not measurable; Phase 66 and prior regressions restored their baselines. |
| Production defect/root cause/repair | No caller defect was reproduced or repaired. Readiness gap: fixture-owned API initialization and operation mismatch. |
| Production caller fixture dependency | No caller exists. Existing Phase API consumers remain dedicated fixtures/tests. |
| Phase 67 QEMU boots and caller success count | No Phase 67 scenario; `0` caller boots and `0` caller invocations. |

## Evidence and hashes

All generated evidence remains under ignored `artifacts/` paths and is not
staged. The audit outputs are:

- Host binary: `artifacts/phase61-managed-worker-api-host-tests/phase61-managed-worker-api-host-tests.exe`
- Normal ManagedKernel build: `artifacts/phase67-audit-normal-build`
- SyntheticScheduler build and guest logs: `artifacts/phase67-audit-synthetic-build`
- Phase 66–59 fresh guest logs: `artifacts/phase67-audit-phase66-regression` through `artifacts/phase67-audit-phase59-regression`
- Private QEMU executable used by fresh regressions: `artifacts/phase66-qemu-private/qemu-system-x86_64-phase65.exe`

| Artifact | SHA-256 |
|---|---|
| Phase 67 audit Normal `BOOTX64.EFI` (697,291 bytes) | `0B02EC80AF8DEF2567B2E4FEF1C31AF70242BE2A1F2064F7A0B0606D5DC3229D` |
| Normal ManagedKernel payload (4,781,568 bytes) | `96CA2273FB1D14A13596858A2D536EF8622469595883B61BE2EEBB75324A7A4C` |
| Phase 67 audit SyntheticScheduler `BOOTX64.EFI` (154,425 bytes) | `F6D26C97D4DAA1FC4188C8DCCC531460091C421C8F3198648191BE7401187568` |
| Synthetic payload (730,112 bytes) | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |
| Phase 66 accepted regression EFI (641,679 bytes) | `DA039130D590FBD47E49EDF56A850A11A6EA6F02C0D701A2F0DBD486D8F0F87A` |
| Host test executable (183,450 bytes) | `C0A8CA0B142CD822F4A16175EB5C374DD725F9A0F832D278A81743D9BEC01271` |
| Private QEMU executable (25,287,296 bytes) | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |

Fresh serial-log SHA-256 values:

| Regression | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Phase 66 | `B036F783707F06A9838E9B8F534401321839A0923C7D6DF3C278BDFC4F0D1F2F` | `8E12083F663A5C7A80F5D61A4FC28F2E058CE60D7FAAD97663195A00F22D0222` | `37DC45863F1BB6AB72D6B6AEB5D5E8A5C6B66491D1B5BDF69F03EE4040DB9C62` |
| Phase 65 | `012B899B32FD06E6193F9085E09DAE8823441187111D14F64E646698E86E7FB4` | `0257BC807AB178053A1CCE64EB34CD33E40E14EF946537E90FC9E2F721E74EAB` | `138456CC78A215785188A228D32CABCE5AFE89411D9DEBF16D803EE5785A93FC` |
| Phase 64 | `E160AB036AFC5438CB2135BA5618A09A5556C983EEED2003A7AF184F978EE182` | `435D717C99054790B5062F6A221D0538EFAEB2EFC3D722515BE80CE83C28E5F2` | `B9FEEA3EAA05479FE389C109E612B0BFAD4A07ED78A1EC45E0D5D4F8CD25FE36` |
| Phase 62 | `C8424D67281DB1583315FAB4C1EDB80B6B7783E2C5611CBAB962BE3AD2F84A84` | `F5E14FFF50856715C8C30577A9D4092F515203A72232B5172418C5BDD9B0780D` | `C6E70F7D915235C73604A0E020DE92260EC23403ECBF1CD3667F59C8D7F313DC` |
| Phase 61 | `AF0BAC07A30F9834437F94A97E288FD8E3C7B9F9D909AC912598CFCF83BAF58B` | `64D6959099EF96BAA595BE555D88579D490A7697AE4764BD768917F318E764A3` | `DDF80F0CD37ED349854D1EFD220F0CBCD0A839A80C314A8408A5C2E2CB6C8A50` |
| Phase 60 | `9BF4A6D66A70AF0140FC00728112C3850A9801311678ABE298EF209EFE7E8849` | `612A14460C7CC9127E37A35771B10DD243ABD66B40FDC3AF1C2F21684F5772AB` | `421EDFAE290BA3199D19998F4BEFD60188AED6627CE80A53BD8B271983D82A7A` |
| Phase 59 | `CABA1ECCEC40F3FA62732D7D0316C9EACE733141B1B44C6A146BC890085D946B` | `28A212F5F42AF573C14FA90C4CE3B0A50ED13FFC23D93D62AF19316BBB856E17` | `F92A13BB98E327B45B49458FA8DCFBF62CD17230691AD853A7DE2CD7095A1482` |
| SyntheticScheduler guest | `AFE66F447E93D120DDD6D5075312DEBDD04EBD95C4213B1331FDF25456458121` | `AFE66F447E93D120DDD6D5075312DEBDD04EBD95C4213B1331FDF25456458121` | `AFE66F447E93D120DDD6D5075312DEBDD04EBD95C4213B1331FDF25456458121` |

## Changed files, Git, and limits

Changed file: this audit report only. No production code, test, runner, remote,
credential, branch, additional worktree, or Git history was changed. No
prohibited Git operation occurred. Because Outcome C was selected, no Phase 67
implementation commit was created and no push was attempted. The prior
public-key push failure was not addressed.

Ending HEAD remains `d3a8644041dda603fd82fa0aad696e4e89945c34` —
`Document Phase 66 validation`; ending divergence remains `0 ahead / 0 behind`.
The ending worktree contains only this uncommitted report; generated
`artifacts/` outputs are ignored.

Phase 67 is **not accepted**: no genuine production-side component used the
bounded managed-worker API. The report records Outcome C and the required
production service boundary. No Phase 68 work was started.

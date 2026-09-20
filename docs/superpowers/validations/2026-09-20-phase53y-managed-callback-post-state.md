# Phase 53Y — Managed Callback Post-State VM Baseline

Date: 2026-09-20
Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

## Outcome

Outcome A, implemented with a baseline-relative assertion contract.

The three post-callback VM records are legitimate durable baseline state:

1. the harness/main boot stack,
2. the blocked NativeAOT finalizer worker's 4 KiB non-present guard record, and
3. that worker's 64 KiB committed usable-stack record.

The old absolute expectation of `2` was stale after Phase 53V introduced a
separate VM ledger record for the worker guard. The repair does not change VM
accounting or stack geometry. It captures the total VM ledger count immediately
before callback activity and requires the post-state count to return to that
same baseline. The Phase 53X live-worker invariant remains an explicit `+2`
delta.

## Live preflight

The preflight was read-only and used live repository state as authoritative.

| Item | Result |
| --- | --- |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `ad91bad1985f5a8f9f45e8fa1456d4685ff97a9c` |
| Starting subject | `Complete Phase 53X scheduler callback reclaim validation` |
| Upstream | `origin` = `git@github.com:guideX/guideXOS_NET10.git` |
| Starting ahead/behind | `1 ahead / 0 behind` |
| Starting worktree | clean |
| Phase 53X completion commit | present at starting HEAD |
| QEMU/GDB/LLDB at preflight | none |

Toolchain versions recorded during preflight:

| Tool | Version |
| --- | --- |
| .NET SDK | `10.0.401` |
| .NET host | `10.0.12` |
| GCC | `15.2.0` (`C:\mingw64\bin\gcc.exe`) |
| Binutils/objdump | `2.46.0.20260210` |
| QEMU | `11.0.0` (`v11.0.0-12122-ga4bb4b10c9`) |
| Git | `2.54.0.windows.1` |

The authoritative managed payload used for the integrated builds was 730112
bytes with SHA-256
`AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`.

## Exact assertion and execution path

The original failing assertion was around `src/Gate4Harness/gate4_loader.c:22588`
at preflight. Instrumentation moved the current predicate to approximately
line 22764. Its relevant exact predicate is:

```c
if (callback_thread != gxos_scheduler_current_thread() ||
    callback_thread->state != GXOS_SCHEDULER_THREAD_RUNNING ||
    callback_finalizer == 0 ||
    callback_finalizer->state != GXOS_SCHEDULER_THREAD_BLOCKED ||
    callback_finalizer->wait_record == 0 ||
    callback_finalizer->fls_values[g_nativeaot_runtime_fls_slot] == 0 ||
    callback_finalizer->fls_values[g_nativeaot_runtime_fls_slot] !=
        callback_finalizer_fls_before ||
    gxos_com_is_initialized(callback_thread) != 0 ||
    gxos_com_model(callback_finalizer) != GXOS_COM_MODEL_MTA ||
    callback_counts.active_wait_count != 1U ||
    callback_counts.valid_wait_records != 1U ||
    callback_counts.vm_region_count != callback_baseline.vm_region_count) {
    fail("nativeaot-managed-callback-post-state");
}
```

Before repair, the last term was `callback_counts.vm_region_count != 2U`.
Therefore the precise old failure was:

```text
nativeaot-managed-callback-post-state expected total live VM region count == 2,
but the actual durable post-reclamation baseline was 3.
```

`callback_counts.vm_region_count` is not a callback-only or scheduler-only
subset. `nativeaot_durability_counts()` assigns it directly from
`g_memory_vm_regions.live_count`, so it is the total live VM-region ledger
count.

The execution path is:

```text
efi_main
  → managed entry returns / MANAGED_ENTRY_COMPLETE
  → capture callback thread and pre-existing blocked finalizer worker
  → capture MANAGED_CALLBACK_VM_BASELINE
  → direct managed callbacks and optional managed GC probe
  → nativeaot_scheduler_callback_probe()
       → create callback worker
       → callback worker runs, blocks, signals, terminates
       → close_handle() / collect() reclaim its resources
       → create and reclaim a second worker to prove slot reuse
  → refresh finalizer and durability counts
  → B8 checkpoint
  → nativeaot-managed-callback-post-state
```

“Post-state” therefore means after the managed callback probe, worker
termination, worker close/collect reclamation, and slot-reuse validation, while
the harness's durable boot/finalizer state is still live.

## VM ledger implementation

The count is maintained by `GXOS_VM_REGION_LEDGER` in `vm_substrate.h` and
`vm_substrate.c`. Each live record contains base, byte length, allocation base,
allocation protection, state, protection, type, and allocation identity.
Registration increments `live_count`; unregistering clears the complete record
and decrements `live_count`.

The Phase 53Y checkpoint enumerator records every live ledger entry with base,
end, size, allocation base, identity, state, mapping class, owner, allocation
source, lifetime, and persistence classification. It is diagnostic-only; it
does not change ledger accounting.

## The three durable records

Representative B8 output from the final integrated image identified these exact
records:

| Ledger index / identity | Base → end | Size | State / mapping | Owner | Source | Lifetime / persistence |
| --- | --- | ---: | --- | --- | --- | --- |
| index `0` / identity `0x1` | `0x0000000007E63000` → `0x0000000007F63000` | `0x100000` / 1 MiB | commit / present, RW | `BOOT_STACK` | `LOADER_MEMORY_INIT` | harness kernel lifetime / persistent baseline |
| index `1` / identity `0x2` | `0x0000400000000000` → `0x0000400000001000` | `0x1000` / 4 KiB | reserve / non-present | `FINALIZER_WORKER_GUARD` | `MEMORY_ALLOCATE_SCHEDULER_STACK` | durable blocked finalizer worker / persistent baseline |
| index `2` / identity `0x3` | `0x0000400000001000` → `0x0000400000011000` | `0x10000` / 64 KiB | commit / present, RW | `FINALIZER_WORKER_USABLE_STACK` | `MEMORY_ALLOCATE_SCHEDULER_STACK` | durable blocked finalizer worker / persistent baseline |

The finalizer usable record has allocation base
`0x0000400000000000`; its usable base is one guard page above that
reservation. The guard record is intentionally reserved and non-present. It is
not an untracked hole and it must remain in VM accounting while the worker is
live.

The blocked worker is the live, non-boot, blocked scheduler TCB returned by
`nativeaot_durability_blocked_worker()`. It is established before the
durability baseline and retains its wait record, NativeAOT runtime FLS, TLS/FLS
state, and MTA COM state throughout the callback test. Its two stack records
are created by `memory_allocate_scheduler_stack()` and are released only when
that durable worker is torn down by scheduler shutdown/lifecycle cleanup, not
by the callback worker's `close_handle()`/`collect()` path.

The boot stack record is registered by `initialize_memory_accounting()` and is
also intentionally persistent for the harness/kernel lifetime.

For comparison, the first callback worker's ephemeral records were observed at
identities `0x8` and `0x9`:

| Record | Base → end | Size | State / mapping | Classification |
| --- | --- | ---: | --- | --- |
| callback guard | `0x000040000FEC0000` → `0x000040000FEC1000` | `0x1000` / 4 KiB | reserve / non-present | ephemeral until collect |
| callback usable | `0x000040000FEC1000` → `0x000040000FED1000` | `0x10000` / 64 KiB | commit / present, RW | ephemeral until collect |

The second worker used identities `0xA` and `0xB` in the same boot. Both
workers' records disappeared on reclaim. No callback-owned record remains at
B8.

## Baseline history

The final integrated image emitted the following checkpoints. The values were
identical in all 12 repeated boots.

| Checkpoint | Lifecycle point | Total live VM records |
| --- | --- | ---: |
| B0 | ledger initialized; main boot stack registered, before durable runtime state | `1` |
| B1 | after durable NativeAOT/runtime initialization; blocked finalizer worker present | `3` |
| B2 | immediately before callback worker creation | `3` |
| B3 | callback worker created and fresh-state validated | `5` |
| B4 | callback worker active | `5` |
| B5 | callback worker terminated, before close/reclaim | `5` |
| B6 | after `close_handle()`; this path synchronously unregisters the worker stack records | `3` |
| B7 | after explicit `collect()` | `3` |
| B8 | final post-state assertion point | `3` |

The observed healthy pattern is therefore:

```text
durable baseline 3 → callback worker live 5 → durable baseline 3
```

The Phase 53X assertion still checks the worker live delta as `+2`, and the
post-state assertion checks that the total returns to the captured baseline.

## Why the old value was `2`

Git history shows the absolute `2` was introduced with the original managed
callback bridge in commit `8120627` (`Add NativeAOT managed callback bridge`).
At that time the stack model contributed one VM ledger record for the boot
stack and one record for the pre-existing finalizer worker's usable stack.

Phase 53V commit `df77d27` (`feat: implement Phase 53V sparse worker stacks`)
changed the worker stack contract to a sparse reservation with a separate
4 KiB guard record and a 64 KiB usable record. The current architecture thus
adds one legitimate persistent ledger record. Under the current architecture,
`2` was historically correct but is no longer correct.

## Leak audit and repeated cycles

The focused current-tree red reproduction was captured before the repair:

```text
tools/Run-Phase53XCallbackReclaimFreshBoot.ps1
  -GateDirectory .\artifacts\phase53x-callback-reclaim-green
  -EvidenceDirectory .\artifacts\phase53y-red-current
  -PayloadSha256 AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012
```

Red result:

```text
PHASE53X_CALLBACK_RECLAIM=PASS identity=0x5 vmBefore=0x3 vmCreated=0x5 vmAfter=0x3
PHASE53X_ISOLATED_DOWNSTREAM_FAILURE=GXOS_NET10:FAIL:nativeaot-managed-callback-post-state
```

After the repair, the baseline-relative callback validator was run for 12
fresh integrated boots. Every run reported:

```text
B1=3 B2=3 B3=5 B4=5 B5=5 B6=3 B7=3 B8=3
baseline=3 post=3 postOK=True MANAGED_GC_MAIN_OK=True fail=False
```

The 12-run evidence is under:
`artifacts\phase53y-repeated-callback-cycles-12`.

Additional final-image QEMU validation passed:

- one dedicated integrated post-state boot;
- three baseline-relative managed-callback boots;
- three scheduler-callback boots;
- three scheduler-GC boots;
- three Phase 53V guard boots.

Across the repeated callback set there was no monotonic VM growth, no baseline
drift, no stale callback-owned record, and no worker-slot leak. The durable
baseline remained exactly `3`.

## TDD and assertion repair

The red result above was captured against the original hard-coded `2` contract.
The smallest correct production change was:

```c
callback_counts.vm_region_count != callback_baseline.vm_region_count
```

where `callback_baseline` is captured immediately before callback activity.
The old global `2` is not reintroduced anywhere in the callback VM validators.
The focused validators now require:

```text
MANAGED_CALLBACK_POST_STATE_OK=1
MANAGED_CALLBACK_STACK_VM_REGIONS == MANAGED_CALLBACK_VM_BASELINE
```

Green results include:

```text
NATIVEAOT_MANAGED_CALLBACK_RUN_1=PASS
NATIVEAOT_MANAGED_CALLBACK_RUN_2=PASS
NATIVEAOT_MANAGED_CALLBACK_RUN_3=PASS
NATIVEAOT_SCHEDULER_CALLBACK_RUNS=3
NATIVEAOT_SCHEDULER_GC_RUNS=3
```

The Phase 53X validator still requires:

```text
created == before + 2
after_close == before
```

This preserves the semantic worker cleanup contract instead of converting the
live guard reservation into an invisible record.

## Regression gates

### Phase 53V

`Run-Phase53VGuardFreshBoots.ps1` passed 3/3 fresh boots using a fresh
all-features gate built from the final source. The final guard gate EFI SHA-256
was `BF234DCF8F903D3900A64F0BE1B151A1C24A3CAA7AF9D104CB3CD38507F96B09`.
The existing contract remained intact:

```text
usable stack = 0x10000 / 64 KiB
guard        = 0x1000 / 4 KiB, non-present
GS+0x10      = usableStackLow
TEB+0x10     = usableStackLow
```

### Phase 53W

`Run-Phase53WReadableRangeBuildTests.ps1` passed both modes:

| Mode | EFI SHA-256 | Payload SHA-256 |
| --- | --- | --- |
| Normal | `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |
| SyntheticScheduler | `4681AC05E641A1305817730ED712BB487C6BBAD943D11C2DE13B332AA7AC7D9A` | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |

The Phase 53Y source/validation diff does not alter the Phase 53W
`nativeaot_gc_readable_range` guard scope or reintroduce its former defect. The
integrated Normal-mode QEMU boots emitted `MANAGED_GC_MAIN_OK=1`.
SyntheticScheduler is a compile/proof mode and does not expose the
NativeAotEventWait managed-callback post-state assertion; no shared absolute
callback constant was applied to that mode.

Scheduler stack VM, scheduler durability, and scheduler model host tests also
passed.

### Phase 53X

The callback reclaim validator passed with:

```text
vmBefore=0x3 vmCreated=0x5 vmAfter=0x3
```

The lifecycle remains `TERMINATED → scheduler handoff → close_handle() /
collect() → worker-local resource reclamation`, followed by successful slot
reuse. TCB, TLS/FLS, GS/TEB, handles, and callback resources remained clean.

## Final contract and classification

The correct long-term contract is not `total_vm_region_count == 2` or even a
new global `== 3`. It is:

```text
post callback total VM count == pre-callback total VM count
callback-owned VM delta while live == +2
callback-owned VM records after reclaim == 0
```

The count check is therefore baseline-relative, supplemented by ownership and
worker-delta checks. The third durable record is intentional persistent
baseline state, not a leak.

Final status: `nativeaot-managed-callback-post-state` passes.

No new downstream Phase 53Z failure was exposed by the final-image runs.

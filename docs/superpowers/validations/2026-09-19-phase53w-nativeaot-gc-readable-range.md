# Phase 53W `nativeaot_gc_readable_range` validation — 2026-09-19

## Outcome

Outcome A for the Phase 53W target: the Normal/SyntheticScheduler failure is
proven to be a shared compile-time integration-glue defect, repaired with the
smallest guard-scope change, and validated in fresh builds and boots. The
Phase 53V worker-stack contract remains intact.

The remaining failure observed by the full NativeAOT GC scheduler probe is a
separate downstream scheduler-reclamation assertion:
`nativeaot-scheduler-callback-thread-reclaim`. It occurs after the repaired
readable-range/unwind path has completed, is outside the Phase 53W predicate,
and was not changed or hidden by this repair.

## Live preflight

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `f7af5a47f0a502bca25c966f966953985aa801f0`,
  `Complete Phase 53V worker stack validation`.
- Upstream: `origin/nativeaot-managed-kernel-integration`.
- Starting ahead/behind: `0/0`; `f7af5a47` is already present on the
  configured upstream, so it is not unpushed in the live tree.
- Starting worktree: clean. The only final changes are this document, the
  production source repair, and the new focused build-test runner.
- Phase 53V completion commit: present locally and on `origin`.
- Existing Phase 53V documentation: `docs/superpowers/validations/2026-09-19-phase53v-worker-stack-validation.md`.
  Existing Phase 53V artifacts include `artifacts/phase53v-guard-build-final6`
  and the prior fresh-boot evidence. Existing relevant runners include
  `Run-Phase53VGuardFreshBoots.ps1`, the NativeAOT callback/GC fresh-boot
  runners, and the scheduler host/model runners.
- A pre-existing QEMU process was present at preflight and during part of the
  validation. Its command line used
  `D:\dev\guideXOSServerV0.5_DEVELOPER_STUDIO\OVMF.fd` and a separate
  temporary ESP. It was identified read-only and never terminated or modified.
  All validation QEMU instances used separate copied firmware variable stores,
  ESPs, serial logs, and owned PIDs.
- Toolchain: .NET SDK `10.0.401` / runtime `10.0.12`; the repository's
  `global.json` requests SDK `10.0.302` with roll-forward disabled, so the
  repository-pinned `dotnet` command is unavailable on this host. GCC `15.2.0`,
  Binutils/objdump `2.46.0.20260210`, QEMU `11.0.0`, and MinGW GDB `17.1` are
  installed. The freestanding UEFI harness and host/model tests therefore ran
  with the available toolchain; no managed payload was republished.
- Initial artifact inventory found no PDB, map, or disassembly artifact for the
  failing build. Existing authoritative payloads were preserved:
  Phase 53V/NativeAOT GC payload
  `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` and
  Phase 53V guard EFI
  `2F17CE1CFB0195D6C534AE7B561E0CF5326F618AFB01E2E2A2B3808918EACA3C`.
- Clean-build reproduction: fresh output directories reproduced the same
  Normal and SyntheticScheduler compiler failure before the source repair.

## Step 1 — What the diagnostic means

`nativeaot_gc_readable_range` is not a managed GC event name and is not a
runtime-reported failing range. It is a private C predicate in
`src/Gate4Harness/gate4_loader.c`. The unconditional fault-provenance helpers
`fault_read_u64`, `fault_read_u32`, and `emit_fault_gc_provenance` call it with
an address and a byte count. The NativeAOT unwind path also uses it for
`RtlVirtualUnwind` metadata, context records, unwind-code reads, and 8-byte
stack reads.

Its contract is conservative and half-open. A nonzero range is accepted only
when it is wholly inside one of these domains:

1. the loaded managed image `[g_managed_image_base,
   g_managed_image_base + g_managed_image_size)`;
2. the loader stack `[g_stack_lower, g_stack_upper)`, plus the one adjacent
   legacy page below `g_stack_lower` used for fault provenance;
3. the virtual arena, when every touched page has a VM commitment; or
4. a live scheduler-thread stack in NativeAOT event-wait builds.

The range helper rejects zero length, inverted bounds, integer overflow, and
non-canonical AMD64 endpoints. The arena path page-rounds the half-open range
and calls `gxos_vm_arena_find_commitment` for every touched page. It does not
make the Phase 53V guard page readable, and it does not query hardware page
table permissions for the image/stack domains; those domains are accepted as
known loader-owned virtual ranges. Thus “readable” here means “safe for this
diagnostic/runtime reader according to these ownership and commitment
predicates,” not “the checker mapped arbitrary memory.”

## Step 2 — Exact Normal and SyntheticScheduler reproduction

Before the repair, fresh `Build-Gate4Harness.ps1` invocations failed in both
modes with the same diagnostic:

```text
gate4_loader.c:4237:12: error: 'nativeaot_gc_readable_range' used but never defined [-Werror]
```

The failing translation unit retained the unconditional declaration and call
sites at line 4237, but the only definition was inside:

```c
#if defined(GXOS_ENABLE_NATIVEAOT_MANAGED_GC_PROBE) || \
    defined(GXOS_ENABLE_MANAGED_KERNEL)
```

Normal and SyntheticScheduler do not define either macro. GCC therefore
diagnosed a static function that was declared and used but had no definition;
no EFI image was produced and no guest range was tested.

This is the explicit failure assertion:

```text
nativeaot_gc_readable_range fails because:
there is no guest range [A, B) in the failing boots.  The unconditional
fault-provenance consumer requires predicate P, but preprocessing removes P's
definition while retaining its declaration and uses.  GCC -Werror stops the
build at line 4237 before QEMU can execute.
```

Therefore the Normal and SyntheticScheduler manifestations are the same
shared defect, not two invalid GC ranges and not a NativeAOT GC fault.

The red reproductions used the clean managed-entry payload from
`artifacts/gate1-brepro-shared`:

| Mode | Payload size | Payload SHA-256 | Result |
| --- | ---: | --- | --- |
| Normal | 729600 | `A423B0D27839B9D6DB907B090A8E58479BEBF1A0CB200769DF48130549EB6A60` | compile failure at line 4237 |
| SyntheticScheduler | 729600 | same | compile failure at line 4237 |

## Step 3 — Range provenance and page audit

There is no start/end/size/owner/physical-page tuple for the red build because
the harness never linked. The immediate producer/consumer chain is still
concrete:

```text
fault trap or NativeAOT unwind context
  -> fault_read_u64/u32 or nativeaot_gc_read_stack_u64
  -> nativeaot_gc_readable_range(address, 8)
  -> image / loader-stack / committed-arena / live-thread-stack predicate
```

The loaded image producer records `image.actual_base` and `image.loaded_size`
at `g_managed_image_base`/`g_managed_image_size`; preferred image addresses are
not used by this predicate. The loader stack bounds are initialized from the
current RSP and page geometry in `initialize_nativeaot_tls`. Dynamic arena
ownership comes from `g_memory_virtual_arena` and its commitment table. Live
scheduler stack ownership comes from the live TCB records.

For a runtime range that reaches the predicate, the only accepted page-level
states are:

- image or loader-stack pages within the recorded half-open virtual interval;
- arena pages whose page-aligned addresses all resolve through
  `gxos_vm_arena_find_commitment`; or
- live scheduler stack pages within their TCB stack interval.

The implementation checks the first and last page boundaries with overflow-safe
half-open arithmetic. No page-rounding, inclusive-end, physical/virtual, or
preferred/rebased-image defect was found. The Phase 53V guard remains a
genuinely non-present page immediately below the usable stack and is not added
to the accepted stack range except for the existing one-page fault-provenance
inspection window.

## Step 4 — NativeAOT contract classification

The relevant platform contract is not a GC heap registration callback. The
platform supplies a safe address-domain predicate to the NativeAOT unwind and
fault-provenance readers: loaded image metadata, current/runtime stack storage,
committed VM arena storage, and live scheduler-thread stack storage must be
available to the reader when those paths are active.

The source already satisfied that contract for feature-enabled NativeAOT
builds. The defect was integration glue: the shared fault diagnostic was built
for every harness mode, but its predicate implementation was guarded as if it
were only a NativeAOT managed-GC/managed-kernel helper. This is class **G —
Shared NativeAOT integration defect**, specifically a compile-time
feature-guard/definition-visibility defect. It is not classes A–D, E, H, or I.

## Step 5 — Phase 53V independence

The direct NativeAOT GC-probe serial evidence retains the closed Phase 53V
invariants before the downstream lifecycle assertion:

```text
PHASE53V_GUARD_BYTES=0x0000000000001000
PHASE53V_USABLE_STACK_BYTES=0x0000000000010000
PHASE53V_GUARD_NONPRESENT=1
PHASE53V_WORKER_GS_LOWER=0x000040000FEC1000
PHASE53V_WORKER_TEB_LOWER=0x000040000FEC1000
PHASE53V_WORKER_USABLE_LOW=0x000040000FEC1000
MANAGED_GC_WORKER_BEFORE_UNWIND_FAILURES=0x0000000000000000
MANAGED_GC_WORKER_AFTER_UNWIND_FAILURES=0x0000000000000000
```

The post-repair guard-only gate rebuilt to the exact Phase 53V EFI hash
`2F17CE1CFB0195D6C534AE7B561E0CF5326F618AFB01E2E2A2B3808918EACA3C`, and
three fresh boots passed the guard-fault, non-present, GS/TEB-intact, and
unrelated-state-intact assertions. The repaired predicate therefore did not
derive from stale pre-53V 16 KiB geometry, make the guard readable, or alter
GS/RSP ownership.

## Step 6 — First-invalid-state timeline

```text
T-3  The range predicate and its address-domain helpers were implemented in
     the NativeAOT GC/managed-kernel feature block.
T-2  Fault-provenance helpers were made unconditional and retained an
     unconditional static declaration for the predicate.
T0   Normal/SyntheticScheduler preprocessing removed the only definition but
     retained the declaration and call sites.
T+1  GCC -Werror reported “used but never defined” at line 4237.
T+2  No EFI image linked; no guest range, page mapping, RSP, GS, or GC state
     could be the cause of the red result.
```

The first invalid state is therefore the translation-unit visibility state at
T0, not a runtime address state at T+2.

## TDD and production repair

The red result was captured before editing. The production repair is bounded
to `src/Gate4Harness/gate4_loader.c`: the original helper definitions remain
in their original feature-enabled location, and a copy is compiled only when
neither `GXOS_ENABLE_NATIVEAOT_MANAGED_GC_PROBE` nor
`GXOS_ENABLE_MANAGED_KERNEL` is defined. This makes the unconditional
fault-provenance consumer link in Normal/SyntheticScheduler while preserving
the NativeAOT feature-enabled code layout and all predicate semantics.

The new focused runner is
`tools/Run-Phase53WReadableRangeBuildTests.ps1`. Fresh green builds passed:

| Mode | EFI SHA-256 | Payload SHA-256 |
| --- | --- | --- |
| Normal | `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` | `A423B0D27839B9D6DB907B090A8E58479BEBF1A0CB200769DF48130549EB6A60` |
| SyntheticScheduler | `4681AC05E641A1305817730ED712BB487C6BBAD943D11C2DE13B332AA7AC7D9A` | same |

Focused neighboring suites also passed:

```text
NATIVEAOT_GC_PROBE_CONTRACT_TESTS=PASSED checks=8
SCHEDULER_MODEL_TESTS=PASSED checks=256
SCHEDULER_STACK_VM_HOST_TEST=PASS
SCHEDULER_DURABILITY_HOST_TEST=PASS
PHASE53W_READABLE_RANGE_BUILD_TESTS=PASSED modes=Normal,SyntheticScheduler
```

## Fresh QEMU validation

Fresh Normal and SyntheticScheduler boots from the final green builds passed
without `FAIL`, `CPU_EXCEPTION`, or `PAGE_FAULT_` markers:

```text
PHASE53W_QEMU_MODE=Normal PASS bytes=11817
PHASE53W_QEMU_MODE=SyntheticScheduler PASS bytes=4137
```

Normal reached `MANAGED_ENTRY_COMPLETE`. SyntheticScheduler reached
`SYNTHETIC_SCHEDULER_PROOF_RETURNED` with zero scheduler failures, proof
passed, neutral state restored, and teardown complete.

The additional direct NativeAOT GC-probe build used the authoritative
730112-byte payload above and produced EFI SHA-256
`A99C9D72B9AF313764A4613734F9B6C3CE1FDB9A2DA066C267D15394C261C363`. Its
fresh serial evidence reached:

```text
NATIVEAOT_STARTUP_OK
GC_STARTUP_ADVANCED
NATIVEAOT_DURABILITY_PASS=1
MANAGED_ENTRY_COMPLETE
MANAGED_GC_MAIN_OK=1
MANAGED_GC_WORKER_BEFORE_UNWIND_FAILURES=0
MANAGED_GC_WORKER_AFTER_UNWIND_FAILURES=0
```

No `nativeaot_gc_readable_range` failure, unresolved import, repaired-range
page fault, or CPU exception occurred. The run then reached the known separate
`nativeaot-scheduler-callback-thread-reclaim` assertion. The serial values at
that point show the thread was closed and its FLS/TLS/handle/canary state was
cleared, but the scheduler VM-region count did not satisfy that later
assertion's expected decrement. That code path is outside this Phase 53W
predicate and was not modified.

The repository's all-marker three-boot GC scheduler runner also correctly
refused to start while the unrelated pre-existing QEMU was present. A manual
owned-PID run was used only to isolate the post-repair readable-range path;
the unrelated process remained untouched.

## Final status

The Normal/SyntheticScheduler `nativeaot_gc_readable_range` failure is closed
as a proven shared compile-time guard-scope defect. The smallest correct
repair is implemented and green in both modes. Phase 53V remains closed and
its post-repair guard regression is 3/3 fresh boots. The separate scheduler
reclamation assertion is preserved as downstream work rather than being
silenced or folded into Phase 53W.

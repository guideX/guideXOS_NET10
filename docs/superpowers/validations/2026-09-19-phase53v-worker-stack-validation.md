# Phase 53V worker-stack validation — 2026-09-19

## Current outcome — Outcome A

Phase 53V is closed. Boot 3 completed as a new isolated QEMU boot using the
committed Phase 53V artifacts and reproduced the repaired worker-stack
contract. The final result is 3/3 fresh isolated boots, with no production
source changes required.

The previous Outcome B attempt remains below as historical record; its blocked
attempts and earlier evidence have not been rewritten or removed.

## Historical Outcome B — prior validation pass

Outcome B — the committed Phase 53V repair remains valid, but the required
third isolated fresh QEMU boot was blocked by another task's QEMU process.
No production source was changed and no unrelated process was terminated.

The production worktree was clean at the start. Ending HEAD remains
`df77d2769a07e98ab63cd22a78cc050bdd083f91` (`feat:
implement Phase 53V sparse worker stacks`), on
`nativeaot-managed-kernel-integration`, tracking
`origin/nativeaot-managed-kernel-integration` at ahead/behind `0/0`.
`e39ac25250adb7ca633eeaab193eecaf882f4ef8` and
`df77d2769a07e98ab63cd22a78cc050bdd083f91` are present.

## Preflight

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Worktree: clean before validation; after validation only this untracked
  documentation file exists; no production files changed
- Required commits: present
- Initial QEMU process: PID `37256`, command line rooted at
  `D:\dev\guideXOSServerV0.5_DEVELOPER_STUDIO`; unrelated and left untouched.
  It exited naturally. Later unrelated instances were observed as PIDs
  `16420` and `26184`, with the same external repository root; they were also
  left untouched. The final read-only process check also found unrelated QEMU
  PIDs `34548` (`D:\dev\guideXOSServerV1.1_DOTNET_SUPPORT`) and `33560`
  (`D:\dev\guideXOSServerV0.5_DEVELOPER_STUDIO`); both were left untouched.
  No GDB or LLDB process was active during preflight.
- Toolchain: .NET SDK `10.0.401` installed; `global.json` requests
  `10.0.302` with roll-forward disabled. GCC `15.2.0`, NASM `2.16.03`, QEMU
  `11.0.0`, PowerShell `7.6.5`, and MinGW GDB `17.1` were available. No
  rebuild was performed in this validation-only pass.

Prior Phase 53V evidence already present includes the TDD red gate, stack-VM,
durability, scheduler-model, CreateThread-model, virtual-memory, guard-enabled
EFI build, and an earlier guard-fault boot under `artifacts\phase53v-*`.

## Artifacts

The exact staged gate used for the fresh boots was
`artifacts\phase53v-guard-build-final6`:

| Artifact | Size | SHA-256 |
| --- | ---: | --- |
| `ESP\GXOS\gxos-managed-entry-probe.dll` | 730112 | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |
| `ESP\EFI\BOOT\BOOTX64.EFI` | 554272 | `2F17CE1CFB0195D6C534AE7B561E0CF5326F618AFB01E2E2A2B3808918EACA3C` |

No PDB is staged in the authoritative gate and no PDB was used; the EFI PE
report has no debug directory. The QEMU firmware hashes were
`edk2-x86_64-code.fd = 33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`
and `edk2-i386-vars.fd = 5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.

## Fresh isolated boots — prior validation pass

Two fresh boots completed and persisted serial evidence under
`evidence\phase53v-guard-fresh-boot-20260919-final-attempt\run-1` and
`run-2`. Each used a new QEMU process and a new copied OVMF variable store.
The first boot's owned QEMU PID was `35488`. The second boot's process was
owned and cleaned up, but its PID was not retained by the inline wrapper
because the first serial read raced the file flush. The persisted `run-2`
serial is complete and independently validates the same fields.

| Boot | QEMU PID | Worker | Usable low | Usable high | Guard | GS base | GS+0x10 / TEB+0x10 | CR2 | Fault RSP |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 35488 | `0x3` | `0x40000FEC1000` | `0x40000FED1000` | `0x40000FEC0000..0x40000FEC0FFF` | `0x5321000` | `0x40000FEC1000` / `0x40000FEC1000` | `0x40000FEC0000` | `0x40000FED0E78` |
| 2 | not retained | `0x3` | `0x40000FEC1000` | `0x40000FED1000` | `0x40000FEC0000..0x40000FEC0FFF` | `0x5321000` | `0x40000FEC1000` / `0x40000FEC1000` | `0x40000FEC0000` | `0x40000FED0E78` |

Both completed boots prove:

- usable stack size `0x10000` (64 KiB), guard size `0x1000` (4 KiB), and
  `usableStackHigh - usableStackLow == 0x10000`;
- `GS+0x10 == TEB+0x10 == usableStackLow`;
- CR2 is inside the dedicated non-present guard page;
- fault RSP and the pre-probe context RSP are inside the usable interval;
- `PHASE53V_GUARD_GS_TEB_INTACT=1` and
  `PHASE53V_GUARD_UNRELATED_STATE_INTACT=1`;
- the original 16 KiB worker-stack geometry does not appear: the runtime
  creation record reports `CREATETHREAD_STACK_SIZE=0x10000`, and the source
  contains no scheduler-stack `0x4000`/16384 policy.

The repository runner `Run-Phase53VGuardFreshBoots.ps1` rejected the first
boot because its first-match parser selected an earlier non-target
`PHASE53V_GUARD_BASE` record. A last-record read of the same persisted serial
shows the actual worker guard and CR2 equality above. A separate immediate
post-exit read also raced serial-file flushing on boot 2; the persisted log
was re-read after flush and passed without modifying the runner or target.

Boot 3 remains outstanding. At the next launch opportunity the host was again
occupied by unrelated QEMU PID `26184`, so validation stopped without
interference.

## Initial RSP and historical frame

The creation record for worker identity `0x2` in both completed logs reports:

```text
stack low  = 0x400000001000
stack high = 0x400000011000
initial RSP = 0x400000010FF8
```

The guard-probe worker identity `0x3` does not emit a separate initial-RSP
marker before deliberately faulting. The committed implementation derives it
as `usableStackHigh - 8`, giving `0x40000FED0FF8`; the captured context/fault
RSP is `0x40000FED0E78`, leaving `0x188` bytes to usableStackHigh at the
diagnostic boundary.

The historical Phase 53T GDB capture remains the exact generated NativeAOT
large-frame witness: frame size `0x41B0`, pre-frame RSP `0x4D02970`, and
post-frame RSP `0x4CFE7C0`, with the expected `0x41B0` delta. That older
capture is retained under
`evidence\phase53t-first-rsp-producer-20260914-08\run-1\gdb.stdout.log`.
The guard-enabled Phase 53V boot intentionally faults before the later managed
body, so it does not emit a new per-boot `0x41B0` pre/post pair. Projecting
that unchanged frame contract from the Phase 53V worker-3 initial RSP gives
post-frame RSP `0x40000FECCE48`, which remains above usable low by `0xBE48`;
this projection is not counted as a new runtime capture.

## Context and regression result

Worker identity `0x3` has an explicit coherent runtime sample in both logs:
context RSP `0x40000FED0E78`, context GS `0x5321000`, GS lower alias and TEB
lower alias both `0x40000FEC1000`, and guard-fault worker identity `0x3`.
Worker identity `0x2` has the independent creation/context record with its
own `0x10000` stack and GS base `0x545D000`. No RSP/GS mixed-worker pairing,
zero lower alias, or GS-page collision was observed. The full two-worker
scheduler/context host evidence was already passing before this completion
pass.

The original defect class did not recur: no RSP entered the active GS page,
the dedicated guard stopped exhaustion, and `GS+0x10` was nonzero and equal to
the usable low bound on both completed boots.

## Historical downstream classification and disposition

The known `nativeaot_gc_readable_range` failure in Normal/SyntheticScheduler
builds remains separate downstream work. It was not investigated or changed
here and is retained for Phase 53W. It does not violate the observed Phase
53V stack contract.

Phase 53V is not formally closed in this pass because the required three-boot
fresh-isolation requirement is incomplete: two completed, one remaining due to
host QEMU ownership. No completion commit was created under Outcome B.

Exact Phase 53W target: investigate and repair `nativeaot_gc_readable_range`
in the Normal/SyntheticScheduler NativeAOT paths, without changing the Phase
53V worker-stack contract.

## Final closure pass — Boot 3

The final closure pass began from repository
`D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`, branch
`nativeaot-managed-kernel-integration`, at HEAD
`6618afbddacacca678c2ad326f6d2cbbe1c35e66` (`...`). The configured upstream
was `origin/nativeaot-managed-kernel-integration`, with starting ahead/behind
`0/0`, and the starting worktree was clean. The preflight found no active
QEMU, GDB, or LLDB processes, so no unrelated process was active or modified.
The previously present empty `run-3` directory was left untouched; Boot 3
used the distinct fresh evidence directory
`evidence\phase53v-guard-fresh-boot-20260919-final-attempt\run-3-fresh`.

The authoritative committed gate remained
`artifacts\phase53v-guard-build-final6` with the following current hashes:

| Artifact | Size | SHA-256 |
| --- | ---: | --- |
| `ESP\GXOS\gxos-managed-entry-probe.dll` | 730112 | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |
| `ESP\EFI\BOOT\BOOTX64.EFI` | 554272 | `2F17CE1CFB0195D6C534AE7B561E0CF5326F618AFB01E2E2A2B3808918EACA3C` |

QEMU `11.0.0` launched exactly one new isolated instance, PID `20796`, with a
new copied OVMF variable store. The process was owned by this validation,
stopped after the deterministic guard-fault marker, and confirmed gone. No
failure marker or CPU-exception marker was emitted.

Boot 3 serial evidence records:

| Field | Boot 3 value |
| --- | --- |
| Worker identity | `0x3` |
| Reservation base | `0x40000FEC0000` |
| Guard range | `0x40000FEC0000..0x40000FEC0FFF` |
| Guard size | `0x1000` / 4 KiB |
| Usable stack | `0x40000FEC1000..0x40000FED1000` |
| Usable stack size | `0x10000` / 64 KiB |
| Initial RSP | Not emitted for the deliberate probe worker; derived contract value `usableStackHigh - 8 = 0x40000FED0FF8`. The independent creation record for worker `0x2` reports `0x400000010FF8`. |
| GS base | `0x5321000` |
| GS+0x10 | `0x40000FEC1000` |
| TEB+0x10 | `0x40000FEC1000` |
| Fault CR2 | `0x40000FEC0000` |
| Fault RSP | `0x40000FED0E78` |

The required arithmetic and identity invariants pass:

* `usableStackHigh - usableStackLow == 0x10000`.
* The guard is exactly `0x1000` bytes immediately below the usable stack.
* `GS+0x10 == TEB+0x10 == usableStackLow`.
* `GS+0x10` is nonzero and belongs to the active worker; no mixed-worker
  stack/GS metadata was observed.
* CR2 is inside the dedicated guard page, and fault RSP is inside the
  committed usable stack interval.
* `PHASE53V_GUARD_NONPRESENT=1`,
  `PHASE53V_GUARD_GS_TEB_INTACT=1`, and
  `PHASE53V_GUARD_UNRELATED_STATE_INTACT=1`.

The historical regression remains absent. Boot 3 reports
`CREATETHREAD_STACK_SIZE=0x10000`; no old `0x4000` scheduler-worker geometry,
zero GS stack limit, worker-stack-to-GS collision, or GS/TEB corruption was
observed. The deliberate exhaustion probe faulted at the non-present guard
before unrelated memory corruption. Together with the prior Phase 53T
`0x41B0` frame evidence and the two completed Phase 53V boots, this closes the
original failure class.

The known `nativeaot_gc_readable_range` failure in Normal/SyntheticScheduler
builds remains an independent downstream failure retained for Phase 53W. It
was not investigated or modified in this pass.

Therefore, **Phase 53V — sparse managed worker stacks — is formally complete
at 3/3 fresh isolated QEMU boots.**

Next phase: **Phase 53W — investigate and repair
`nativeaot_gc_readable_range` in the Normal/SyntheticScheduler NativeAOT paths,
without changing the Phase 53V worker-stack contract.**

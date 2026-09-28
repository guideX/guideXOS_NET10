# guideXOS C# .NET 10 — Phase 72 Persistent-Service Health and Explicit Restart

Date: 2026-09-28

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

## Outcome

**Outcome B — the new health surface exposed two snapshot integration defects, both were corrected, and all acceptance boots passed.** The API reports the bounded COM1 service state without returning internal pointers. In three fresh QEMU boots, it reported Phase 71's exhausted `RESTART_FAILED` state, then accepted a caller-requested restart into a new generation. The new runtime attached, the route re-enabled after service start, a real COM1 event reached managed dispatch, and old-generation requests were rejected.

**Phase 72 adds operator/caller control over an already bounded recovery model; it does not add more automatic retries.**

No console or command shell exists at this service boundary, so Phase 72 provides an internal production control API and validates it through the harness. This is not a service manager.

## API and state contract

The production APIs are:

- `gxos_managed_kernel_driver_service_get_status(...)`
- `gxos_managed_kernel_driver_service_restart(...)`

Status is `GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1`, version 1. It contains structure size/version, the one service slot, device identity, owner state, current-handle validity and identity/generation, route state, runtime attachment, last failure reason and failed tuple, automatic restart budget and attempts, `restart_failed`, explicit-restart eligibility, and explicit-restart-in-progress. It contains no TCB, stack, runtime `Thread*`, wake-event, or managed-object pointers and performs no allocation.

The authoritative lifecycle values come from the existing one-slot service owner, worker context, NativeAOT lifecycle record, and interrupt route. The query copies these values while using the interrupt context's existing critical callbacks for the route flags. An invalid current handle is represented by `current_service_valid = 0`, identity 0, and generation 0; the last failed identity tuple remains separately available.

Failure reasons are bounded numeric values: NONE (0), START_ATTACH (1), RUNNING_DISPATCH (2), AUTOMATIC_REPLACEMENT_ADMISSION (3), and AMBIGUOUS_RUNTIME_OWNERSHIP (4). The last failure is sticky across a successful episode; a later failure replaces it. Thus the accepted fixture's explicit capacity failure became the most recent failure (START_ATTACH) and remained visible after the subsequent successful restart. A normal DRAIN or DISCARD alone does not create a failure.

The explicit restart API is restricted to a settled, fully reclaimed `RESTART_FAILED` service and checks the expected failed identity/generation/device tuple. It does not restart a normally stopped service; callers use the existing start path there. Running/starting/stopping states are rejected, duplicate in-progress requests return IN_PROGRESS, stale tuples return STALE, and quarantine/ambiguous ownership returns QUARANTINED. Capacity failure returns CAPACITY with no partial replacement or route activation, leaves the service restart-failed, and does not consume the new episode's automatic restart budget or trigger an automatic retry. A later explicit attempt can begin the episode again.

The explicit episode preserves COM1 device identity, allocates a new identity/generation, resets the automatic budget to one, performs ordinary scheduler admission and runtime attach, and enables the route only after successful managed start. The new episode can still use at most one automatic restart. Host coverage proves that budget permits exactly one later automatic attempt.

## Acceptance scenario

Each accepted boot reproduced the Phase 71 automatic replacement admission failure after generation 1 had run and handled COM1. Old-generation resources were reclaimed, the replacement admission was rejected once, and no automatic retry followed. Immediately before explicit restart, status reported:

| Field | Value |
|---|---|
| Owner slot / device | 0 / COM1 device identity 1 |
| Owner state | `RESTART_FAILED` (14) |
| Current service | Invalid; no live handle, identity 0, generation 0 |
| Route / runtime attached | Disabled / no |
| Failure | AUTOMATIC_REPLACEMENT_ADMISSION (3), failed identity 1 / generation 1 / device 1 |
| Automatic restart budget / attempts | 0 remaining / 1 attempted |
| Explicit restart allowed | Yes |

A stale expected tuple was rejected. The fixture then injected one explicit admission capacity result (CAPACITY, 6): no replacement was published, the route stayed disabled, owner state remained restart-failed, and the new episode retained budget 1. The following explicit request was accepted with identity 2 / generation 2, advancing each by one while preserving device identity 1. Runtime attach succeeded, the route became active only after start, and a real COM1 byte traversed IRQ4, FIFO, wake event, and managed dispatch in generation 2.

Healthy post-restart status reported generation 2, budget 1, route enabled, attached runtime, and explicit restart disallowed. It retained the latest failure reason (START_ATTACH from the injected explicit capacity failure); successful start does not erase diagnostic history. Stopping through the old handle, restarting with the old tuple, and requesting restart while running were rejected. Quarantine restart was rejected. A pending one-shot survived service teardown and explicit restart, then completed `ADD_ONE(41) → 42`.

The Phase 72 fixture also queried status while normally running, stopping, and after DRAIN. Host status checks cover normal DRAIN and DISCARD as stopped states with no failure caused by shutdown. Phase 69's DISCARD/attach-failure guest regression remains green.

## Snapshot defects found and repaired

The first startup status check exposed an integration defect in the new query path: interrupt critical callbacks are owned by `GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT`, not the route entry. Looking them up through the wrong structure prevented the query from taking a valid route snapshot. A second restriction incorrectly limited the query to the boot thread even though the startup callback runs in an active scheduler service thread. The implementation now calls the context-owned critical callbacks with the route's hardware context and permits any active scheduler thread. The snapshot remains read-only and route/lifecycle state is copied under the existing IRQ critical section. No owner policy or retry limit changed.

## Resource evidence

All three accepted boots showed the same accounting. The Phase 9 post-GC comparison baseline was 195 live allocation pages (`0xC3`), physical bytes `0x18EB40`, committed bytes `0x183B40`, virtual reservation bytes `0x5000`, 8 reservations, and 7 regions. The highest recorded service/one-shot comparison snapshot before final stop was 277 pages (`0x115`), physical `0x1E0C80`, committed `0x1D2C80`, virtual `0xF000`, 11 reservations, 210 commitments (`0xD2`), `0xD2000` committed bytes, and 12 regions. The one-shot's measured delta was 21 pages, one reservation, two regions, and `0x10000` committed bytes.

After service stop and one-shot completion, raw accounting was 233 pages (`0xE9`), physical `0x1B4C80`, committed `0x1A6C80`, virtual `0x5000`, 8 reservations, 176 commitments (`0xB0`), `0xB0000` committed bytes, and 7 regions. Subtracting the still-live one-shot's measured delta at the service teardown boundary leaves the expected 23-page service release (`0x17`), two reservations, and three regions. ThreadStore was 2 at baseline, 3 after managed service attach, and 2 after detach/final cleanup. Scheduler capacity returned to 3 free objects and 4 free thread slots; coexistence reached all 16 scheduler objects used, with two thread slots free. The persistent 38-page difference from the early baseline is the known retained NativeAOT/runtime GC-proof footprint, not a service leak. No increasing leak trend appeared across accepted boots.

## Validation results

| Check | Result |
|---|---|
| Status and owner host tests | PASS; includes read-only, running/stopped/failed/quarantined snapshots, route/failure/budget fields, eligibility, stale tuple, duplicate, new episode, capacity/slot bounds, and one later automatic attempt |
| Managed worker host tests | PASS |
| Phase 61/62/64/65/66 host runner | PASS |
| Phase 56/57/58/59/60 rollback host suites | PASS |
| Phase 72 QEMU acceptance | 3/3 fresh boots |
| Phase 71 automatic replacement admission failure | PASS in all three Phase 72 boots; no second automatic attempt |
| Phase 70 successful automatic restart | PASS, 3/3 fresh boots |
| Phase 69 DRAIN | PASS, 1 fresh boot on final normal build |
| Phase 69 AttachFailureDiscard | PASS, 1 fresh boot |
| Phase 69 IdleStop | PASS, 1 fresh boot |
| Normal ManagedKernel build | PASS; fixture failure hooks absent from ordinary EFI |
| SyntheticScheduler build/proof | PASS, 3/3 `EXPECTED_HALT` runs |

The separate optional `Run-ManagedKernelDriverBindingHostTests.ps1` could not pass: its wrapper points to absent SDK 10.0.400 MSBuild; retrying with installed SDK 10.0.401 reached compilation but that standalone project omits the current `ManagedSecureRandom`, `ManagedEntropyService`, and `ManagedE1000Driver` sources. The requested service, worker, phase 61–66, and rollback host suites passed. Initial QEMU boot setup hit the existing `EFI_UNSPECIFIED_TIMEZONE` path; final QEMU builds used the builder's `-AssumeUnspecifiedTimezoneUtc` option. SyntheticScheduler's final proof used an isolated copy of QEMU/firmware because the wrapper refuses to run when an unrelated QEMU process is active; that process was left untouched.

## Artifact hashes and evidence

The final Phase 72 fixture payload is 4,791,808 bytes, SHA-256 `3F2C49A25C4FF72C8876CED23C7C9C67EF930D3F84C8FC4013D00F6CF9D462BC`; fixture EFI SHA-256 is `D126F37AAA7E9932D1CD35EBE7ACB435C338DAE483F57E456E03F4389540FB60`. The ordinary owner-enabled EFI SHA-256 is `512EB2BFCFBE57E38C105DFC6A92BD1EECB044E94266436DF215B9CD4903E135`; binary scan confirmed the Phase 71 admission trigger and Phase 72 fixture marker are absent. SyntheticScheduler payload is 730,112 bytes, SHA-256 `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`; EFI SHA-256 `26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946`.

Phase 72 serial logs, each reporting `PHASE72_RESOURCE_BASELINE_RESTORED=1`:

| Boot | Evidence | Serial SHA-256 |
|---|---|---|
| 1 | `artifacts/phase72-acceptance-3boots/runs/run-1/serial.log` | `0B4558737FE2A1568073EE49353227A965203F0784024D918E981226567915B1` |
| 2 | `artifacts/phase72-acceptance-3boots/runs/run-2/serial.log` | `AAF8A270F152D3CD21E9B0E9DFBD11A2E9FE7CF9CFFB91012F092382561A2E1F` |
| 3 | `artifacts/phase72-acceptance-3boots/runs/run-3/serial.log` | `83DD2B4FF8EF20DA1774D254734E7BCD98F1E8F1A63BE57E1526DD5E19158D02` |

Regression evidence is under `artifacts/phase72-phase69-drain-regression`, `artifacts/phase72-phase69-attach-regression`, `artifacts/phase72-phase69-idle-regression`, `artifacts/phase72-phase70-regression-3boots`, and `artifacts/phase72-synthetic-authoritative-build/synthetic-runs-20260928-005504-738/run-{1,2,3}`. Build outputs are under `artifacts/phase72-final-fixture-build`, `artifacts/phase72-normal-build`, and `artifacts/phase72-synthetic-authoritative-build`. Generated artifacts remain ignored.

## Limitations

- No operator console exists here; the caller-facing surface is the bounded internal API and harness.
- The accepted Phase 69 ambiguous runtime ownership boundary remains quarantined. Phase 72 does not restart it.
- The capacity rejection was deterministically injected before replacement ownership publication; this proves no partial generation or route publication under the fixture's admission failure.
- The optional standalone driver-binding host project remains incompatible with the installed SDK/source list as described above.
- No service enumeration, timer, retry loop, health polling thread, telemetry, RPC, GUI, or additional automatic restart was added.

Phase 72 meets its success criterion and is accepted as **Outcome B**. No Phase 73 work was started.

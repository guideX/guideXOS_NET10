# guideXOS C# .NET 10 — Phase 73 Service Health Diagnostic Caller Audit

Date: 2026-09-28

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

## Outcome

**Outcome C — no suitable existing diagnostic caller was found.** The repository has serial diagnostic output and a Phase 72 QEMU acceptance fixture, but no user-invokable kernel console, command dispatcher, or established diagnostic action surface. The Phase 72 fixture is test-only, calls the APIs directly, and inspects private lifecycle state. It is not an operator-facing caller. No shell, service manager, command parser, or other infrastructure was added.

**Phase 73 reuses an existing diagnostic surface; it does not introduce a new shell or service manager.** In this checkout, the audit found no eligible surface to reuse, so status/restart remain available through the Phase 72 APIs and test harness only.

## Read-only preflight

| Field | Live value |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `f3c68591f110fa8f141327bca31d89d75739b8dc` — `Add persistent service health and explicit restart` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | 0 ahead / 0 behind against the local tracking ref |
| Starting worktree | Clean |

The request listed 1 ahead / 0 behind as expected divergence. The live tracking ref was at the starting HEAD, so the live 0/0 result was used. No fetch or Git configuration changes were made.

Read before the audit:

- `docs/superpowers/validations/2026-09-27-phase70-bounded-persistent-service-restart.md`
- `docs/superpowers/validations/2026-09-27-phase71-persistent-service-restart-admission-failure.md`
- `docs/superpowers/validations/2026-09-27-phase72-persistent-service-health-explicit-restart.md`

Phases 70–72 report accepted restart, failure-containment, status, and explicit-restart behavior. Phase 72 explicitly records that no console or command shell exists at the service boundary and validates the APIs through a harness fixture.

## Caller audit

The source search covered kernel console commands, diagnostic/debug dispatch, runtime and driver status output, service/scheduler status, restart/reset commands, serial input, keyboard input, command loops, and shell entry points.

| Candidate | Audit result |
|---|---|
| Gate4 serial diagnostics | Output-only boot and proof markers in `src/Gate4Harness/gate4_loader.c`; no command dispatch or user action entry point. The existing standard-handle proof records `STDIN=ABSENT` and serial COM1 as stdout/stderr. |
| Phase 72 QEMU acceptance fixture | `GXOS_ENABLE_PHASE72_EXPLICIT_RESTART_FIXTURE` is added only by the opt-in build path in `tools/Build-Gate4Harness.ps1`. `src/Gate4Harness/gate4_loader.c` invokes status/restart APIs as a scripted acceptance scenario and prints marker output. It is not available as a normal diagnostic command. |
| Phase 72 fixture boundary | The fixture also reads owner restart state, worker/service fields, NativeAOT lifecycle state, and interrupt route internals to assert test invariants. That makes it unsuitable as the production caller boundary required by Phase 73. |
| Managed COM1/keyboard path | `src/ManagedKernel/ManagedSerialDriver.cs` dispatches received device events and emits debug markers. The keyboard routines in `gate4_loader.c` are device readiness/event tests, not a console command reader. |
| Other status/restart commands | No production command interpreter, runtime status command, driver status command, service command, or restart/reset command was found. Host-test runners and QEMU wrappers are build/test entry points, not guest operator callers. |

The closest candidate is the Phase 72 QEMU fixture, but it fails both caller requirements: it is fixture-only rather than a production or operator diagnostic surface, and its test assertions reach into internals. It was therefore not selected or modified.

## Exposure and API mapping

No caller was selected. There is no command/action syntax, human-readable status output, caller result translation, or caller-initiated restart proof for Phase 73.

The existing Phase 72 boundary remains:

- Status API: `gxos_managed_kernel_driver_service_get_status(...)` returning `GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1`.
- Restart API: `gxos_managed_kernel_driver_service_restart(...)`.
- Status fields available to a future caller: service slot and device identity; bounded owner state; current service validity and identity/generation; route enabled state; runtime attached state; last failure reason and failed identity/generation/device tuple; automatic restart budget and attempts; restart-failed state; explicit-restart eligibility and in-progress state.
- The bounded snapshot contains no raw owner, TCB, stack, wake-event, runtime `Thread*`, or managed-object pointer. A future caller can stay within this abstraction by consuming only the snapshot and restart API.

The Phase 72 result enum remains the complete available mapping contract. No Phase 73 caller maps these results to console text:

| API result | Value | Phase 73 caller mapping |
|---|---:|---|
| `OK` | 0 | Not applicable; no caller |
| `INVALID_ARGUMENT` | 1 | Not applicable; no caller |
| `INVALID_STATE` | 2 | Not applicable; no caller |
| `IN_PROGRESS` | 3 | Not applicable; no caller |
| `QUARANTINED` | 4 | Not applicable; no caller |
| `STALE` | 5 | Not applicable; no caller |
| `CAPACITY` | 6 | Not applicable; no caller |
| `START_FAILURE` | 7 | Not applicable; no caller |
| `RESOURCE_FAILURE` | 8 | Not applicable; no caller |

These values are defined in `src/Gate4Harness/managed_kernel_driver_worker.h`. Phase 72 reports direct fixture/host coverage for the applicable healthy, failed, capacity, running, stale, quarantined, and in-progress cases. Phase 73 did not rerun those tests.

## Phase 72 state evidence relevant to a future caller

The following values summarize the accepted Phase 72 evidence. They are API/harness evidence, not output from a Phase 73 operator caller.

| State | Phase 72 snapshot values |
|---|---|
| Healthy running service | Owner state `WAITING`; device identity 1 (COM1); current service valid with nonzero identity/generation; route enabled; runtime attached; restart budget 1; explicit restart disallowed. |
| `RESTART_FAILED` | Owner state `RESTART_FAILED` (14); device identity 1 (COM1); no current live handle (valid 0, identity 0, generation 0); route disabled; runtime detached; last failure `AUTOMATIC_REPLACEMENT_ADMISSION` (3), failed identity 1 / generation 1 / device 1; restart budget 0 with one automatic attempt; explicit restart allowed. |
| Explicit restart after failure | Phase 72 fixture directly called the API. An injected capacity result was contained; a later request succeeded with identity 2 / generation 2, preserved device identity 1, attached runtime, and enabled route after managed start. |
| COM1 event after restart | Phase 72 fixture proved IRQ4 → FIFO → wake event → managed dispatch after its direct API restart. This was not a caller-initiated Phase 73 restart. |
| Rejections | Phase 72 covered restart while running (`INVALID_STATE`), stale tuple (`STALE`), quarantine (`QUARANTINED`), capacity (`CAPACITY`), and in-progress through its accepted host/fixture coverage. |
| Read-only status | Phase 72 host/guest coverage queried snapshots without changing lifecycle state, route, budget, or identity/generation. No Phase 73 caller exists to exercise. |
| Stale generation | Phase 72 proved old handles and old restart tuples rejected after generation advancement; status reported the current generation only. |
| One-shot coexistence | Phase 72 reported `ADD_ONE(41) → 42` completing across the direct restart scenario. Not rerun for Phase 73. |

## Validation and regressions

No production implementation or test fixture changed, so no host suite, build, SyntheticScheduler proof, or QEMU boot was run for this audit. This phase attempted 0 QEMU boots and passed 0 QEMU boots.

The following are prior accepted evidence recorded by Phase 72, not Phase 73 reruns:

- Phase 72 status/restart host coverage and 3/3 QEMU acceptance.
- Phase 71 automatic replacement-admission failure and no-second-auto-retry behavior.
- Phase 70 successful bounded automatic restart.
- Phase 69 DRAIN, attach-failure cleanup, and idle-stop regressions.
- Phase 66/65/64/62/61 managed-worker API host coverage.
- Normal ManagedKernel build and SyntheticScheduler build/proof (3/3 in the Phase 72 report).

No production defect was found. The limitation is the absence of an operator-facing diagnostic input/dispatch surface, not a Phase 72 API defect. No repair was made.

## Files, evidence, and limitations

Changed file: this validation report only. No generated evidence or build outputs were created.

Audit evidence and source paths:

- `docs/superpowers/validations/2026-09-27-phase70-bounded-persistent-service-restart.md`
- `docs/superpowers/validations/2026-09-27-phase71-persistent-service-restart-admission-failure.md`
- `docs/superpowers/validations/2026-09-27-phase72-persistent-service-health-explicit-restart.md`
- `src/Gate4Harness/gate4_loader.c`
- `src/Gate4Harness/managed_kernel_driver_worker.h`
- `src/Gate4Harness/standard_handle.c` and `src/Gate4Harness/standard_handle.h`
- `src/ManagedKernel/ManagedSerialDriver.cs`
- `tools/Build-Gate4Harness.ps1`
- `tools/Run-ManagedKernelPhase10FreshBoots.ps1`

There is no Phase 73 caller output, new restart proof, new artifact hash, or Phase 73 build/test evidence. Phase 72 artifact hashes and log paths remain in the Phase 72 report.

## Recommendation

Do not treat this audit as operator-facing integration. The smallest next step is to identify or approve a normal diagnostic input/dispatch surface for the kernel, then define one bounded status action and one explicit restart action that consume only the Phase 72 APIs. This report does not begin that work.

**Phase 73 is complete as Outcome C. No caller integration was accepted, and Phase 74 was not started.**

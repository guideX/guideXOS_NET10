# guideXOS C# .NET 10 — Phase 78 Real-Target Diagnostic UART Authority and Boot Handoff

Date: 2026-10-02

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

## Outcome

**Outcome C — no qualifying actual guideXOS target can be established from the repository, project history, or retained bare-metal validation material.** The current host identifies as an HP ProDesk 600 G6 Microtower PC, but the project contains no evidence that guideXOS has booted on or explicitly targets that machine. It is not selected by inference. Recorded platform runs use QEMU with UEFI/OVMF. No real-machine UART provider or BootInfo extension was added.

The target's UART existence, base, span, IRQ, backend, firmware-console use, and ownership are **unknown**, not absent or inferred. There is consequently no authoritative resource source to carry into the kernel. This is an audit disposition, not a bare-metal pass.

The phase rule is: **Phase 78 establishes authority and ownership for one actual target; it does not infer serial resources from legacy PC conventions.** This checkout could not meet the first clause because no qualifying real target was evidenced; the correct result is Outcome C and no UART activation.

## Read-only preflight

| Field | Live observation |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `48051dc446b6dcbb506ee325acb557108bf07e73` — `Document real-platform diagnostic UART provider limits` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Starting divergence | 0 ahead / 0 behind (the task's expected 1 ahead / 0 behind differed from live Git state) |
| Starting worktree | Clean |
| Phase reports read | Phase 76 trusted diagnostic UART resource; Phase 77 real-platform provider audit |
| Available Windows host | HP ProDesk 600 G6 Microtower PC, observed via `Win32_ComputerSystem`; not recorded as a guideXOS target |

### Target evidence audit

The Phase 77 report says no target hardware/resource was supplied and no real platform was selected. The current repository's target references and validation reports contain no manufacturer/model or board profile for a machine booted by guideXOS. `docs/REFERENCE_REPOSITORIES.md` identifies `D:\dev\guideXOSUEFI` as a source repository but gives no machine identity. Its retained run scripts, including `D:\dev\guideXOSUEFI\run_qemu.ps1` and `D:\dev\guideXOSUEFI\Scripts\RunUefiSafeNormalDesktop.ps1`, launch QEMU with OVMF firmware. The older UEFI material therefore does not establish a physical target.

| Selection item | Result |
|---|---|
| Manufacturer/model selected | None. HP ProDesk 600 G6 is the current development host only; no guideXOS target evidence links it to a boot. |
| Firmware type for a real target | Unknown. Retained emulator tests use UEFI/OVMF. |
| Boot mode for a real target | Unknown. Retained emulator tests use x64 UEFI. |
| Why no target was selected | The available logs and profiles identify the QEMU/OVMF path, and the repository has no physical machine identity or bare-metal boot record. |

## Resource authority result

| UART question | Phase 78 result |
|---|---|
| Does the selected real target have a secondary UART? | Unknown; there is no selected real target or authoritative resource observation. Unknown is not treated as present. |
| Firmware resource table / ACPI `_CRS` / UEFI Serial I/O | No real UART source is consumed by this checkout. Phase 77 found ACPI support only for the PM timer, no AML `_CRS` parser, and no UEFI Serial I/O enumeration. |
| Board/platform profile | No physical-board profile is present. The only populated descriptor is explicitly QEMU-only. |
| Authoritative base/span/IRQ/backend | None for real hardware. All four remain unavailable. |
| Authority source path | None. Relevant audit sources: `src/Gate4Harness/gate4_loader.c`, `src/Gate4Harness/platform_performance.c`, and the Phase 77 report. |
| Provenance | No real-target provenance value is selected or emitted. |
| Firmware or bootloader ownership before kernel | Unknown for real hardware. |
| Firmware console/debugger conflict | Unknown. There is no evidence that a real candidate UART is independent of firmware console/debugger use. |
| Ownership transfer point | None is defined for a real UART. The current Gate4Harness source declares the `ExitBootServices` function type/table slot but contains no call to `ExitBootServices`; it does not demonstrate a post-EBS kernel ownership transfer. |
| COM1 | Existing COM1 remains an owned kernel serial route at I/O `0x3F8`, span 8, IRQ4. It is not offered as a diagnostic resource. |

No hint combination was promoted to authority. There was no UART probe, register access, generic firmware parser, or machine switch.

## BootInfo and handoff audit

`GuideXBootInfo` is defined in `src/Gate4Harness/gate4_loader.c` as a packed v1, 88-byte record and mirrored at that size in `src/ManagedKernel/ManagedKernel.cs`. It contains magic, version, size, architecture, flags, a `SerialWrite` function pointer, and a 64-byte framebuffer value. It has no UART presence, provenance, backend, I/O base, span, IRQ, or ownership field, and no optional-extension directory or negotiation mechanism. `src/ManagedEntryProbe/ManagedEntry.cs` declares the same v1 prefix through `SerialWrite` as a 24-byte minimum-size structure; the default `ManagedEntryProbe` payload consumes that prefix and ignores the framebuffer tail. The `ManagedKernel` payload mode uses the full 88-byte managed mirror.

The loader fills the v1 fields in `efi_main` before calling the managed entry. The current record is a local boot handoff used by this harness. No firmware UART resource is read or normalized there, and the source path has no `ExitBootServices` invocation. Thus it cannot prove that firmware stopped using a candidate resource or that ownership transferred to a post-firmware kernel.

The `ManagedKernel` entry checks its compiled layout (`sizeof(GuideXBootInfo) == 88` and field offsets) and then requires the magic, exact version 1, `Size >= 88`, x64 architecture, and nonzero serial callback before emitting the bootstrap marker. The default `ManagedEntryProbe` entry checks the same magic/version/architecture/serial prefix but only requires `Size >= 24`. Thus the current kernel rejects records below 88 bytes, the probe rejects records below 24 bytes, and both reject unsupported versions; sizes at or above each consumer's minimum are accepted while unconsumed trailing bytes are ignored. This is observed code policy, not a new ABI compatibility guarantee. There is no version negotiation. Silently adding a UART field to v1 would not be a safe handoff contract.

No BootInfo changes were made:

| Compatibility field | Before | After |
|---|---:|---:|
| `GuideXBootInfo` version | 1 | 1 |
| `GuideXBootInfo` size | 88 bytes | 88 bytes |
| UART handoff record | None | None |
| Old-size behavior | `< 88` rejected by ManagedKernel; `< 24` rejected by ManagedEntryProbe | Unchanged |
| Unsupported-version behavior | Rejected by exact `Version == 1` check | Unchanged |
| Malformed-size behavior | ManagedKernel rejects `< 88`; ManagedEntryProbe rejects `< 24`; each accepts larger records and ignores its unconsumed tail | Unchanged |
| Absent UART | Not represented; existing provider reports unavailable when no QEMU fixture is compiled | Unchanged |

Because no handoff code was added, no old/new bootloader interoperability, malformed UART-record tests, or version-2 compatibility claim is made.

## Phase 76 mapping and safety boundary

The provider in `gate4_loader.c` returns only a compile-time QEMU descriptor or unavailable. Its QEMU fixture is source `QEMU_PLATFORM`, backend `IO_16550`, base `0x2F8`, span 8, IRQ3, exclusive and diagnostic-only. Those values describe QEMU's explicit device configuration only. The real-target provider query returns unavailable because no real authority feeds it.

The existing Phase 76 path remains authoritative: provider output → descriptor validation → full I/O-range and IRQ collision checks → reservation → UART initialization → IRQ registration. The optional diagnostic permission is a separate build policy and defaults off unless `GXOS_ENABLE_PHASE75_COM2_DIAGNOSTIC_UART` is set. The Phase 76 collision checks protect COM1's full range and IRQ4. No discovery result enables GXDC by itself.

The repeated Phase 76 host run below passed. Its rejected-resource and policy-disabled cases assert zero UART/IRQ hardware callbacks before the validator, collision checks, reservation, and policy gate accept activation. No real hardware callback was attempted in Phase 78.

## Validation and retained regression evidence

Executed during Phase 78:

| Check | Result |
|---|---|
| `tools/Run-ManagedKernelDiagnosticHostTests.ps1 -OutputDirectory %TEMP%\gxos-phase78-diagnostic-host-tests` | Passed diagnostic protocol, device-resource, and diagnostic-resource suites. |
| `tools/Run-Phase53WReadableRangeBuildTests.ps1 -OutputDirectory %TEMP%\gxos-phase78-build-tests` | Passed Normal and SyntheticScheduler builds. Normal EFI SHA-256: `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964`. SyntheticScheduler EFI SHA-256: `26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946`. Both staged payloads matched SHA-256 `A423B0D27839B9D6DB907B090A8E58479BEBF1A0CB200769DF48130549EB6A60`. |
| Phase 76 QEMU provider / GXDC regression | Retained from the accepted Phase 76 report; not rerun because Phase 78 changed no production code. That report records three fresh QEMU boots with healthy STATUS, RESTART_FAILED STATUS, explicit RESTART, and continued STATUS service. |
| SyntheticScheduler proof | Build passed here. The Phase 76 report's existing 3/3 QEMU proof remains applicable; no common startup or handoff code changed, so it was not rerun. |
| Real hardware | No qualifying target identified; no bare-metal resource detection, acceptance, activation, STATUS, or RESTART claim. |

Generated artifacts are outside the repository under `%TEMP%\gxos-phase78-diagnostic-host-tests` and `%TEMP%\gxos-phase78-build-tests`. The build report hashes are recorded above. No handoff/provider host tests were added because neither a real authority input nor handoff implementation exists.

Host-test executable SHA-256 values: diagnostic protocol `A74F4D4FA2A26B1668378CDA323DF345B605134E1CACD0B529ACBB79B917230F`; device resources `49CBE8E4AF4E24F78CA488DAB291C673DF89B149C9658DE9A60013F88F2EB5FA`; diagnostic resource claim `B18318BE6D5396CA09025E032D63615F8CFDFC5FF4A9F08BB706002F68CC60EE`.

## Defect, changes, and disposition

No production defect was found. The blocker is absence of an evidenced physical target and therefore absence of an authoritative resource/ownership source, not a Phase 76 validation, collision, reservation, policy, or GXDC defect. No repair was made. GXDC v1 is unchanged, including request size, command IDs, CRC, response sizes, endian encoding, parser, and STATUS/RESTART semantics. The QEMU provider is unchanged.

Only this validation report is changed. Phase 78 is recorded as an accepted Outcome C audit: it conclusively avoids inventing or activating an unsupported resource, while the requested one-real-target authority success criterion remains unmet until a qualifying target is identified.

The smallest next step is to obtain an explicit physical guideXOS target identity and its retained bare-metal boot evidence, then audit that target's authoritative firmware/board resource and ownership data before proposing any boot-info extension. Phase 79 has not started.

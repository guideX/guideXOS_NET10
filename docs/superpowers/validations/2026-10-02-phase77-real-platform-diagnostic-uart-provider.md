# Phase 77 — Real-Platform Diagnostic UART Provider Audit

Date: 2026-10-02

## Outcome

**Outcome C — no authoritative real-platform UART source exists in the current repository path.** No real platform was selected and no provider was added. The live boot, firmware, PCI, and platform interfaces do not supply a trustworthy secondary 16550-compatible UART base, span, IRQ, and type as one bounded resource. Choosing `0x2F8`/IRQ3 outside the QEMU profile would rely on the legacy PC convention explicitly excluded by this phase.

This is an audit result, not a bare-metal pass. The Phase 76 QEMU provider and diagnostic policy remain unchanged.

**Phase 77 adds one real-platform provider only; it does not create a generic serial-device discovery subsystem.** No real-platform provider was added because the audited source interfaces cannot yet provide one authoritatively.

## Read-only preflight

| Field | Observed value |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| Starting HEAD | `ddbd7c573c4429ccc434f7c1e2e7c46755ce0147` — `Add trusted diagnostic UART resource discovery` |
| Upstream | `origin/nativeaot-managed-kernel-integration` (`git@github.com:guideX/guideXOS_NET10.git`) |
| Starting divergence | 0 ahead / 0 behind (the supplied expected 2 ahead / 0 behind differed from live Git state) |
| Starting worktree | Clean |
| Reports read | Phase 74, Phase 75, and Phase 76 validation reports |

The live repository, branch, and HEAD matched the request. The divergence check was quoted for PowerShell and confirmed `0 0`.

## Platform-source audit

| Candidate source | Existing support | Why it cannot identify a real diagnostic UART |
|---|---|---|
| Bootloader/platform handoff | `GuideXBootInfo` in `src/Gate4Harness/gate4_loader.c` is a bounded versioned structure carrying architecture, flags, a serial-write callback, and video mode. Existing managed-device resource APIs publish a platform snapshot. | `GuideXBootInfo` has no UART base, span, IRQ, type, or ownership descriptor. The current platform resource builder describes existing devices, including production COM1, but has no trusted secondary-UART input outside the compile-time QEMU fixture. No existing resource handoff can be extended with authoritative secondary-UART data. |
| UEFI configuration and Serial I/O | The UEFI system table and configuration-table pointers are available to the loader. The source declares/uses PCI I/O for selected PCI BAR queries. | No UEFI Serial I/O Protocol lookup/use exists. A Serial I/O handle alone would not give this kernel a stable post-boot port and IRQ descriptor. The existing PCI I/O query only obtains BAR address ranges for selected e1000e/virtio RNG devices; it does not enumerate a UART or provide its IRQ. |
| ACPI | `src/Gate4Harness/platform_performance.c` reads RSDP, XSDT/RSDT, and FADT data to obtain the ACPI PM timer port. | There is no AML interpreter or ACPI namespace `_CRS` parser. The PM timer port does not identify a UART, its span, or its IRQ. Implementing AML resource evaluation here would exceed the accepted narrow scope. |
| Board/platform descriptor | The only concrete diagnostic descriptor is the compile-time QEMU provider in `src/Gate4Harness/gate4_loader.c`. The Phase 76 descriptor enum contains reserved `BOARD_DESCRIPTOR` and `FIRMWARE_DESCRIPTOR` values. | No real-machine board profile/table supplies UART data. The enum values are not providers or provenance evidence. The QEMU q35/PC emulation setup is not a real platform and cannot establish a real machine's secondary UART. |
| Legacy BIOS/BDA or SMBIOS | No BDA, BIOS serial-base, or SMBIOS UART resource consumer was found. | Identity-only or BDA-only data would not authoritatively provide the complete base/span/IRQ/backend/ownership claim required here. |

The UEFI ACPI tables, UEFI memory map, and selected PCI BAR query are mechanisms that survive long enough for the current loader's work. None survives as a UART resource handoff because none is parsed or normalized into one. The `GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE` query remains an in-process platform boundary; its current implementation returns only the compile-time QEMU value or unavailable.

## Selection and resource result

No real platform was selected. The only fully specified candidate is the QEMU platform profile, which Phase 76 already identifies as emulated rather than real hardware. The current production serial route is COM1 at I/O `0x3F8` with IRQ4; source comments and the managed resource snapshot assign its ownership to the existing kernel serial path. COM1 is not eligible for diagnostic reuse.

There is no authoritative real-platform candidate descriptor, so the following values are intentionally unavailable rather than inferred:

| Resource field | Phase 77 result |
|---|---|
| UART presence | Unknown on real hardware; provider must report unavailable until a source is supplied |
| Backend/type | Unknown; existing implementation supports only I/O-port 16550 |
| I/O base / span | Unknown; no real base or register span obtained |
| IRQ | Unknown; no real IRQ obtained |
| Provenance | No real provenance value selected or added |
| Handoff structure/version | None for UART. `GuideXBootInfo` is versioned but contains no UART resource fields. |
| Firmware-backed / bootloader-backed / board-profile-backed | No real provider in any of these categories |
| Firmware-console takeover | Not established. Firmware console handles are not used to establish secondary-UART ownership; no candidate can be safely taken over from the available evidence. |
| Unsupported UART / absent UART | Must remain unavailable. There is no provider data to classify a real device, and no random probing is permitted. |

No-resource behavior remains fail-closed: the provider reports unavailable, Phase 76 does not initialize the diagnostic UART, and boot continues with COM1. Discovery does not enable the diagnostic policy. The normal policy remains disabled unless the separate explicit development build option is selected.

## Phase 76 trust and ownership boundary

No Phase 76 logic was changed. The existing order remains provider query → descriptor validation → full I/O and IRQ collision checks → singleton reservation → UART initialization → IRQ registration → ingress enabled. The QEMU-only resource remains `IO_16550`, base `0x2F8`, span 8, IRQ3, source `QEMU_PLATFORM`; these are facts about its explicit emulator configuration only.

The unchanged Phase 76 validator and reservation path:

- rejects malformed descriptors and unsupported backends before hardware callbacks;
- checks the full I/O range against registered resources, including COM1;
- rejects a registered or exclusively owned IRQ collision;
- prevents duplicate claims;
- rolls back a failed UART initialization and a failed IRQ registration;
- performs no UART access when the resource is unavailable, invalid, conflicting, or policy-disabled.

The focused host suite passed during this audit, including the existing provider-boundary validator, collision, reservation, and rollback coverage. These tests validate Phase 76 common code; they do not constitute tests of a real provider or real hardware.

## Validation and regression evidence

Executed during Phase 77:

- `tools/Run-ManagedKernelDiagnosticHostTests.ps1 -OutputDirectory %TEMP%\gxos-phase77-outcome-c-diagnostic-host-tests` — passed all three suites: diagnostic protocol, device resources, and diagnostic resource claim.
- `tools/Run-Phase53WReadableRangeBuildTests.ps1` — passed Normal and SyntheticScheduler builds (`PHASE53W_READABLE_RANGE_BUILD_TESTS=PASSED modes=Normal,SyntheticScheduler`).
- `git diff --cached --check` — passed before commit.

No production source changed, so this audit did not rerun the QEMU protocol sequence or the broader Phase 72/75 regressions. The accepted Phase 76 report at the starting HEAD records three fresh QEMU boots of the trusted QEMU provider, including healthy STATUS, `RESTART_FAILED` STATUS, explicit RESTART, stale-request rejection, a real COM1 event after restart, provider-only policy-off behavior, and no-resource behavior. Phase 75 and Phase 72 outcomes remain documented in their accepted reports; this audit adds no new QEMU or bare-metal claim.

| Validation item | Result |
|---|---|
| QEMU provider / GXDC v1 | Existing Phase 76 three-boot evidence remains applicable; not rerun because no production code changed |
| QEMU STATUS / RESTART / post-restart COM1 | Existing Phase 76 evidence; not rerun |
| Real-platform provider host tests | Not applicable; no real provider data source exists |
| Bare-metal hardware availability / detection | No target hardware/resource was supplied to this task; not tested |
| Bare-metal resource acceptance / GXDC STATUS / RESTART | Not tested; no real provider was implemented |
| Normal and SyntheticScheduler build check | Passed via Phase 53W build script |
| Phase 76 focused host regressions | Passed |
| Phase 72 / 75 and managed-worker host regressions | Not rerun; production code unchanged, accepted evidence remains in the Phase 74–76 reports |

Evidence paths produced by the executed checks are outside tracked source: `%TEMP%\gxos-phase77-outcome-c-diagnostic-host-tests` and `artifacts\phase53w-readable-range-build-tests`. The focused host executables and both build artifacts were generated for this audit and are not committed.

SHA-256 hashes for the generated evidence:

| Artifact | SHA-256 |
|---|---|
| `managed_kernel_diagnostic_tests.exe` | `EC8ED20E8DC965775672E6953A16D03D4631FD2CE56328F20EA36005E3F32BBA` |
| `managed_kernel_device_resources_tests.exe` | `B0780002C0E147DD524F794A49DAEBD897063498C4B1B34FFF012A0F939B2FBF` |
| `managed_kernel_diagnostic_resource_tests.exe` | `3F44497D760E35035EDCB458329CD054F165ECFA2DDAAD9717E25F73C3961545` |
| Normal build `BOOTX64.EFI` | `04F208423AA27BC47AE15E484FA9E91BA566496558FC58A0A28A357C0556F964` |
| SyntheticScheduler build `BOOTX64.EFI` | `26988BDA6037481D4FE31CE67E4B1690F8C10786BE43140810AEB57E18E63946` |
| Staged shared managed payload | `A423B0D27839B9D6DB907B090A8E58479BEBF1A0CB200769DF48130549EB6A60` |

## Defect, changes, and next step

No production defect was found and no repair was made. The blocker is missing authoritative real-platform UART data, not a Phase 76 validator, collision, reservation, policy, or GXDC defect. No GXDC v1 field, ID, size, CRC, endian rule, parser, or status/restart behavior changed.

Changed file: this validation report only.

The smallest follow-up is to choose one actual target machine and establish its authoritative platform source and ownership contract first. If its bootloader can report the UART, define a bounded versioned value handoff (base, span, IRQ, backend/type, presence and ownership) and validate it against the existing Phase 76 descriptor path. If no such handoff exists, identify an already-supported firmware resource interface before considering any parser work. Do not infer COM2 or begin generic enumeration.

No Phase 78 work was started.

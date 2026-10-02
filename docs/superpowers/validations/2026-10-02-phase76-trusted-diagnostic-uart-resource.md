# Phase 76 — Trusted Diagnostic UART Resource

Date: 2026-10-02

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

## Outcome

**Outcome A — a trusted optional UART resource path is validated.** A normalized platform descriptor now passes capability validation, registered-resource and exclusive-IRQ collision checks, and a single diagnostic reservation before UART programming or IRQ registration. Policy remains a separate explicit build choice. The accepted provider retained Phase 75 GXDC v1 STATUS and RESTART behavior across three fresh QEMU boots.

**UART discovery does not imply diagnostic enablement; the resource must be both trusted and explicitly permitted before GXDC ingress becomes active.**

## Preflight and source audit

| Field | Starting value |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| HEAD | `fd7d0fa67ca5a1009bc2ea7021e84957d091d91a` — `Add native COM2 diagnostic ingress` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Divergence | 1 ahead / 0 behind |
| Worktree | Clean |

Read the Phase 74 diagnostic caller audit and Phase 75 ingress validation. The source audit found no mature ACPI, SMBIOS, UEFI configuration-table, or bootloader UART resource path. `managed_kernel_device_resources` supplies a bounded existing device-resource snapshot, but there is no general I/O-port/IRQ reservation manager. COM1 is represented as an existing platform serial I/O resource and owns IRQ4; the keyboard path owns IRQ1. This phase therefore uses one explicit QEMU/platform provider and a small diagnostic-UART claim, without adding generalized discovery or PnP machinery.

Selected provider: the compile-time QEMU platform descriptor in [gate4_loader.c](../../../src/Gate4Harness/gate4_loader.c). `-ConfigurePhase76QemuDiagnosticUartProvider` supplies resource presence only. The accepted development profile uses `-EnablePhase75Com2DiagnosticIngress`, which supplies the same descriptor and separately permits diagnostics. Firmware/ACPI discovery remains future work; no port probing is used.

## Descriptor, provenance, and platform boundary

`GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE` in [managed_kernel_diagnostic_resource.h](../../../src/Gate4Harness/managed_kernel_diagnostic_resource.h) is a bounded value descriptor. It contains present/enabled, source, backend, ownership flags, I/O base, register span, IRQ, and reserved bytes. It contains no pointers and no GXDC fields.

The current provider value is:

| Field | Value |
|---|---|
| Provenance enum/value | `GXOS_DIAGNOSTIC_UART_SOURCE_QEMU_PLATFORM` (`1`) |
| Present / enabled | 1 / 1 |
| Backend | `GXOS_DIAGNOSTIC_UART_BACKEND_IO_16550` |
| Ownership | exclusive, diagnostic-only |
| I/O base / span | `0x2F8` / 8 ports |
| IRQ | 3 |

The platform query `gxos_platform_get_diagnostic_uart_resource(...)` normalizes provider output to unavailable, valid, or invalid. The ingress consumes only that descriptor. Its backend presently supports only an I/O-port 16550-compatible UART. Validation rejects absent/disabled or malformed descriptors, unknown source/capability, unsupported backend, non-eight-byte-aligned base, zero base, incorrect register span, invalid/reserved IRQ2, out-of-range register span, unknown ownership bits, and nonzero reserved bytes.

The `0x2F8`/IRQ3 mapping is one PC/QEMU provider result, not a universal COM2 constant. MMIO UARTs and other register/interrupt layouts require a future supported backend.

## Claim, collisions, and hardware ordering

The Phase 76 path is: provider query → descriptor validation → I/O and IRQ collision checks → reserve the singleton diagnostic UART claim → initialize UART → register IRQ → mark ingress enabled. The reservation records resource type, private owner ID, I/O range, and IRQ. The claim remains held while ingress is enabled; release unregisters IRQ, stops RX, and clears ownership. Duplicate activation is rejected before repeating initialization or registration.

Before any hardware callback, `managed_kernel_diagnostic_resource.c` compares the complete `[base, base + span)` range against every registered I/O-port resource, including COM1, then compares the IRQ against every registered interrupt descriptor and the kernel's exclusive IRQ mask. IRQ sharing is not introduced. The platform's existing IRQ0/1/2/4 routes are exclusive. No firmware or device probing occurs in the interrupt path.

Host tests prove exact COM1-base collision, partial range overlap, COM1 IRQ4 conflict, other exclusive IRQ conflict, registered IRQ collision, duplicate claim rejection, and release behavior. Invalid descriptors and all conflicts leave fake UART-init and IRQ-registration callback counts at zero. UART-init failure clears the reservation; IRQ-registration failure unregisters and stops the UART before clearing it.

## Optional policy and safe failure

The resource provider and diagnostic permission are independent. The provider-only build supplies a valid QEMU resource while policy is zero; activation records `POLICY_DISABLED` and does not initialize the UART, enable RX, register an IRQ, or enable the diagnostic context. The Phase 75 build option explicitly permits ingress. Missing, invalid, conflicting, or failed optional resources record a disable reason and leave diagnostics off while boot continues.

Disable reasons are bounded enum values for no resource, invalid resource, policy disabled, I/O conflict, IRQ conflict, reservation failure, UART initialization failure, and IRQ registration failure. Boot markers include provider source, policy, accepted descriptor or rejection reason, and final Phase 75 enabled/disabled state. No pointers are logged.

The no-resource QEMU configuration omits the second UART entirely. It reports source `NONE`, policy disabled, `NO_RESOURCE`, and Phase 75 disabled; the ordinary COM1 worker and service boot passed. The provider-only QEMU proof attaches a null chardev at the described I/O/IRQ location, so it can prove descriptor presence without creating a diagnostic host endpoint.

## GXDC and QEMU endpoint

GXDC v1 is unchanged: 32-byte requests, STATUS command 1, RESTART command 2, existing CRC32, existing response layouts and parser/recovery behavior. No commands or wire bytes were added. The accepted QEMU runner keeps both serial sockets bound to `127.0.0.1` on separate ephemeral ports. No public network listener or authentication change was added.

## Validation

### Host resource and protocol tests

`tools/Run-ManagedKernelDiagnosticHostTests.ps1` passed:

* `MANAGED_KERNEL_DIAGNOSTIC_HOST_TESTS=PASSED`
* `MANAGED_KERNEL_DEVICE_RESOURCES_HOST_TESTS=PASSED`
* `MANAGED_KERNEL_DIAGNOSTIC_RESOURCE_HOST_TESTS=PASSED`

The resource suite covers a valid resource, no resource, zero base, unaligned base, invalid IRQ, unsupported backend, policy disabled/enabled, exact and partial COM1 overlap, registered/exclusive IRQ conflicts, duplicate reservation, release, both rollback points, reason codes, and no hardware operations before validation. The protocol suite keeps Phase 75 request/response and malformed-stream regressions green.

### QEMU and builds

The accepted ManagedKernel image used payload SHA-256 `3F2C49A25C4FF72C8876CED23C7C9C67EF930D3F84C8FC4013D00F6CF9D462BC` (4,791,808 bytes) and EFI SHA-256 `0BC883ADD25CF614710CE0D83904E104A310DD36E1B10CBEAB8EC17F54148B9E` (794,183 bytes). Three fresh valid-resource boots passed under `%TEMP%\gxos-phase76-valid-three-boot-provider-split\runs\run-{1,2,3}`. Each verified the trusted source and accepted 0x2F8/span-8/IRQ3 descriptor, then the Phase 75 healthy STATUS, RESTART_FAILED STATUS, explicit RESTART, stale RESTART rejection, post-restart COM1 event, malformed-frame recovery, and COM2 liveness after COM1 service stop.

| Boot | Serial log SHA-256 | Protocol transcript SHA-256 |
|---|---|---|
| 1 | `0CB7AA30F5DB2AF2B151B51BA9E27EA24A00ACCB02AF872EBA9BB5DBEDCB9CC1` | `5E080C8A06FF6733575C439BE319D9DAB098A9778ED73EC509940F9B465C78DC` |
| 2 | `1EAE782597645DEB85BFD940710E2CE38FF7AB9AE5C5DBA3715C45F9E4C6B066` | `5E080C8A06FF6733575C439BE319D9DAB098A9778ED73EC509940F9B465C78DC` |
| 3 | `21825D07579BC2F470637AFCFD9EFB5449A61C87BB51A7A5FDFFD53C550FAAF7` | `5E080C8A06FF6733575C439BE319D9DAB098A9778ED73EC509940F9B465C78DC` |

The transcripts record 100-byte healthy and `RESTART_FAILED` STATUS responses; a 24-byte `RESTART`-while-running error; a 36-byte successful explicit RESTART response; 24-byte stale/already-running restart errors; and no response to bad CRC, version, size, target, garbage, and truncated-frame cases. The failed STATUS showed owner state `14`, no current generation, route/runtime disabled, exhausted automatic restart budget, and explicit restart allowed. Successful restart returned COM1 identity 1, generation 2. The runner observed a real COM1 event after restart and confirmed COM2 still answered STATUS after COM1 service stop. Request size stayed 32 bytes; command IDs, response sizes, CRC32, and parser semantics are unchanged.

The provider-only build used EFI SHA-256 `59CC032F561DAF78093FA828FE53D8E2C0D079D37E937CC3B667609BE1CFDE1C` (792,012 bytes). Its fresh boot passed at `%TEMP%\gxos-phase76-provider-policy-off-boot\runs\run-1`; serial SHA-256 is `5E0BBAC246EB0E7C903AB6F18164BFBCEFDD67D95F43182B9A1A982920C19D4C` (640,196 bytes). Markers reported source `QEMU_PLATFORM`, policy `0`, rejection `POLICY_DISABLED`, reason `3`, and ingress disabled. The QEMU command line had only the COM1 loopback TCP chardev; COM2 used `-chardev null,id=diag0`, so no diagnostic host endpoint was opened. The normal COM1 fixture sequence passed.

The no-resource ManagedKernel build used EFI SHA-256 `55FDB1E2441BF09A1E12A3E3E0AA6F04F0D924EDCA20E13EA364326CB1FEBC51` (787,828 bytes). Its fresh boot passed at `%TEMP%\gxos-phase76-no-com2-final-boot\runs\run-1`; serial SHA-256 is `25D9B996C402EE57B15B311D62BCD81BDA68811F90B55E6DF1904F69C0A338AA` (654,637 bytes). It reported source `NONE`, policy `0`, `NO_RESOURCE`, and Phase 75 disabled. Its QEMU command line contained COM1 at 0x3F8/IRQ4 only—no COM2 device or 0x2F8 endpoint—and the COM1 worker/service fixture sequence passed.

Other regression evidence: Phase 56–60 rollback host suites; Phase 61/62/64/65/66 managed-worker API host suites; Phase 69 service-owner/status host suites; managed-worker, interrupt-native, device-inventory, and service host suites all passed. SyntheticScheduler built and passed its 3/3 QEMU proof under `%TEMP%\gxos-phase76-synthetic-scheduler\synthetic-runs-20261002-053556-918\run-{1,2,3}`. The older `Run-ManagedKernelInterruptHostTests.ps1` remains blocked by its hardcoded SDK `10.0.400` (only `10.0.401` is installed); invoking it with the installed SDK exposes an existing missing-source-list issue for `ManagedSecureRandom`, `ManagedEntropyService`, and `ManagedE1000Driver`. This unrelated harness issue was not changed.

Generated boot evidence remains outside the repository. The accepted QEMU profile binds COM1 and COM2 TCP chardevs separately to `127.0.0.1` on ephemeral ports. The valid-resource, policy-disabled, and no-resource QEMU command lines and serial logs are retained in the evidence directories above.

## Limitations

The only provider added is explicit QEMU/platform data. There is no ACPI, SMBIOS, UEFI, or bootloader serial discovery yet. The I/O UART backend and exclusive IRQ policy are intentionally narrow. QEMU negative collision/IRQ boots were not added; deterministic host tests exercise those rejections and assert zero hardware callbacks.

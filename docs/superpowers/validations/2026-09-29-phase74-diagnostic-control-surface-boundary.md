# guideXOS C# .NET 10 — Phase 74 Diagnostic Control Surface Boundary

Date: 2026-10-01

Repository: D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration

Branch: nativeaot-managed-kernel-integration

## Outcome

**Outcome C — a minimal new diagnostic ingress is required.** No existing input path is a safe caller for the Phase 72 status and explicit-restart APIs. The smallest design selected for a follow-up is a dedicated native-owned COM2 receive channel, separate from COM1, with a bounded two-command protocol and dispatch from the scheduler boot thread. This report does not implement that channel or begin Phase 75.

**The recovery control path must remain usable while the COM1 managed service itself is unavailable.**

The COM2 proposal is hardware-capable only on platforms that expose and reserve a compatible secondary UART. For QEMU development, a second emulated UART can carry the same protocol over a host-local socket. Neither is present in this checkout. COM2 is selected because it separates diagnostic ownership from the ordinary COM1 receive path and can be kept in native boot-lifetime code outside the service being recovered.

## Read-only preflight

| Field | Live value |
|---|---|
| Repository | D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration |
| Branch | nativeaot-managed-kernel-integration |
| Starting HEAD | 8b6b7b7b3cc8b11fcdb959d7a5d5149945d8adef |
| Starting subject | Document Phase 73 diagnostic caller audit |
| Upstream | origin/nativeaot-managed-kernel-integration |
| Starting divergence | 0 ahead / 0 behind |
| Starting worktree | Clean |

The requested expected divergence was 1 ahead / 0 behind. The live tracking ref is authoritative and matched HEAD at preflight, so 0/0 was recorded. No fetch or Git configuration changes were made.

Read before source audit:

- docs/superpowers/validations/2026-09-27-phase72-persistent-service-health-explicit-restart.md
- docs/superpowers/validations/2026-09-28-phase73-service-health-diagnostic-caller.md

Phase 72 records the bounded status/restart APIs and three accepted boots in which the service reached RESTART_FAILED with no current generation, route disabled, and runtime detached, then accepted explicit restart from the test caller. Phase 73 records the absence of a production command caller. The Phase 72 fixture is opt-in, invokes the APIs directly, and observes owner/worker/lifecycle/route internals; it is not a suitable production dispatcher.

## Source inventory

The audit covered the serial receive/transmit implementation, interrupt routes and event queue, managed serial and keyboard drivers, UEFI system-table and standard-handle input, service status/restart implementation, Phase 72 fixture gates, and QEMU launcher arguments.

| Candidate | Source and owner | Build/lifetime and framing | COM1-service dependency and result |
|---|---|---|---|
| COM1 receive | src/Gate4Harness/gate4_loader.c configures the COM1 route and implements managed_kernel_serial_interrupt_source; src/Gate4Harness/managed_kernel_interrupt.c captures route bytes into the fixed event queue; src/ManagedKernel/ManagedSerialDriver.cs subscribes and consumes serial events. Native IDT/IRQ and UART code own byte capture; the managed driver owns subscription and event handling. | The ordinary Gate4 EFI path contains the UART/IRQ plumbing. The UART source reads one byte from the COM1 receive register when the configured route asks for data. Events have one payload byte; this is device input, not command framing. | Unsuitable. Route capture requires an active, accepting subscription and service wake/dispatch. Phase 72 shows the route disabled in RESTART_FAILED. There is no independent native COM1 command reader that can call restart after that service disappears. |
| Raw native serial output / serial service API | src/Gate4Harness/gate4_loader.c raw serial_text/serial_out8 routines; src/Gate4Harness/managed_kernel_serial.c and managed_kernel_serial.h. | Present in ordinary EFI diagnostic builds. The service API exposes transmit and transmitter-status capabilities only; the raw polled logger writes COM1 output. No receive API or command dispatcher is present. | Output can remain useful when the managed service is down, but it is output-only. It cannot carry authoritative recovery input. |
| Dedicated secondary UART / COM2 | No COM2, base 0x2F8, or second-UART receive path was found in src, tools, or docs. Current resource publication describes COM1, not a diagnostic UART. | Not present in production or debug builds. A hardware-capable candidate only where platform firmware/resource policy identifies and reserves a compatible second UART. QEMU can be configured with a second UART for development, but current launchers attach only the COM1 serial device. | Independent by design if native-owned from platform initialization through kernel lifetime and dispatched outside the COM1 service. This is the selected new ingress, not a claim of existing support. |
| i8042 / PS/2 keyboard | src/Gate4Harness/gate4_loader.c configures a second keyboard route; src/ManagedKernel/ManagedKeyboardDriver.cs validates and records scan-code events. The event queue and managed dispatch are in the same interrupt/worker subsystem as COM1. | Hardware-capable hardware exists on some machines, but the guest keyboard route is enabled by the Phase 11 proof path and is unsubscribed after its proof. It records scan-code evidence; it does not parse diagnostic commands. | Unsuitable. Its event drain uses the same persistent managed service worker and event/wake path. The Phase 11 key tests therefore do not prove keyboard recovery while that worker is unavailable. |
| UEFI console input | src/Gate4Harness/gate4_loader.c declares the system-table ConIn member. The standard-handle proof reports UEFI text console unavailable and stdin absent. No SimpleTextInput/ReadKeyStroke call or console command parser was found. | Firmware-owned input is a possible future UEFI application input, but no reader is wired into this image. Firmware routing and continued availability after the loader's UART/interrupt setup are not proven. | Not an existing candidate. A firmware-owned input adapter could be independent of the managed service if its platform ownership and lifetime are established, but this audit cannot claim that from the unused pointer declaration. |
| QEMU monitor / host control | Some tools, including Run-ManagedKernelPhase11FreshBoots.ps1 and capture scripts under tools, expose a loopback QEMU monitor and issue sendkey for test input. Normal runners such as Run-Gate4.ps1 disable the monitor. QEMU serial sockets in the Phase 9/10/11/25 launchers are attached to COM1. | QEMU-only host-side management. It controls the VM or injects keyboard events; there is no guest monitor RPC parser, QMP/qtest guest protocol, debugcon input, or diagnostic service dispatcher in the source/configuration audited. | The host monitor process can remain available when a guest service fails, but it cannot call the guest Phase 72 API. Keyboard injection reaches the guest's existing managed event route and is not independent recovery ingress. |
| Debug port / debugcon | Searches for port 0xE9, isa-debugcon, isa-debug-exit, QMP, and qtest found no configured guest input channel. | Not included in ordinary or debug guest builds. QEMU debug output is not an input transport in this tree. | No candidate exists. |
| Network/host RPC and command parser | No diagnostic packet parser, inbound host-control RPC, kernel command dispatch table, shell, or service command was found. The test runners are host-side build/acceptance drivers. | No guest input framing or command table exists. The OS networking sources do not expose a caller for the Phase 72 APIs. | No suitable path exists. No network-facing restart control is recommended. |
| Phase 72 fixture | The opt-in GXOS_ENABLE_PHASE72_EXPLICIT_RESTART_FIXTURE branch in src/Gate4Harness/gate4_loader.c is enabled only by the explicit Build-Gate4Harness.ps1 fixture switch. | QEMU acceptance/test-only direct call sequence. It has no external request framing and reads internal service/worker/route state for assertions. | It successfully demonstrates API behavior after service failure, but is not a diagnostic ingress and is not an acceptable production caller. |

### COM1 ownership and recovery independence

The native COM1 source in gate4_loader.c reads the UART receive register from the route capture callback. managed_kernel_interrupt.c calls that callback only while the route is subscribed and accepting events, then enqueues into the shared event queue and requests managed work. ManagedSerialDriver subscribes to the serial event. Phase 72 records that the route is disabled when the owner is in RESTART_FAILED. The raw native logger and serial service API transmit through COM1 but do not receive command bytes.

Therefore the chain COM1 IRQ → managed COM1 service → diagnostic command is the only current COM1 receive chain, and it fails the recovery-independence rule. COM1 payload must not be reserved or reinterpreted as diagnostic traffic. A second UART avoids stealing ordinary application data and avoids ambiguous framing.

The Phase 72 QEMU fixture proves a narrower and useful fact: the service owner context and scheduler boot thread remain available to its direct caller after the service reaches RESTART_FAILED, has no current service generation, has disabled its route, and has detached the runtime. The API can therefore be called from an independent, longer-lived native ingress. The fixture does not prove that any such ingress exists today.

## Selected control-plane model

**Model C — minimal new bounded diagnostic ingress required.**

Add a native-owned, 16550-compatible secondary-UART diagnostic endpoint with a byte-only IRQ capture path and a bounded scheduler-boot-thread dispatcher. For a PC-compatible resource map, use COM2 at I/O base 0x2F8 and IRQ3 only when platform resource discovery confirms and reserves that mapping; do not hard-code it on platforms that report another resource or no second UART. Initialize the endpoint in native platform setup before the COM1 service starts, and keep it alive for the kernel/scheduler lifetime without routing its input through the COM1 worker. Where a compatible secondary UART is unavailable, leave this endpoint disabled; do not fall back to COM1, network input, or the Phase 72 fixture.

The QEMU development configuration should attach a second emulated UART at I/O base 0x2F8/IRQ3 to a host-local socket and send the same wire records. The socket is a QEMU test transport, not a production operator interface. QEMU HMP remains outside the guest command boundary.

## Minimal wire protocol

The transport is binary, so Phase 75 should not add text parsing. Wire integers are little-endian. Serialize fields explicitly; do not memcpy an in-memory C struct across the wire.

### Request

Maximum request size: 32 bytes. Exactly one fixed-size request record is accepted:

| Offset | Size | Field | Rule |
|---:|---:|---|---|
| 0 | 4 | Magic | ASCII GXDC |
| 4 | 2 | Version | 1 |
| 6 | 2 | Frame bytes | 32 |
| 8 | 4 | Request ID | Opaque correlation value |
| 12 | 2 | Command ID | 1 = SERVICE_STATUS; 2 = SERVICE_RESTART |
| 14 | 2 | Flags | Must be zero |
| 16 | 4 | Target device identity | COM1 identity 1 |
| 20 | 4 | Expected failed identity | Zero for STATUS; required for RESTART |
| 24 | 2 | Expected failed generation | Zero for STATUS; required for RESTART |
| 26 | 2 | Reserved | Must be zero |
| 28 | 4 | CRC32 | CRC over bytes 0–27 |

There is no payload in version 1. STATUS invokes only gxos_managed_kernel_driver_service_get_status(...). RESTART invokes only gxos_managed_kernel_driver_service_restart(...) with the request's expected failed identity, generation, and target device identity. The service slot remains the API's bounded one-slot value; no service enumeration is added.

### Response

Responses use a fixed common header and a bounded command-specific body:

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | Magic, ASCII GXDR |
| 4 | 2 | Version, 1 |
| 6 | 2 | Total frame bytes |
| 8 | 4 | Request ID |
| 12 | 2 | Response-to command ID |
| 14 | 2 | Reserved, zero |
| 16 | 4 | Phase 72 API result code |
| 20 | 0 or bounded body | Status snapshot or new service handle |
| Last 4 | 4 | CRC32 over all preceding response bytes |

For STATUS, the body is the value-only Phase 72 status snapshot: owner state; device and current service identity/generation/validity; route enabled; runtime attached; last failure and failed identity/generation/device tuple; automatic restart budget and attempts; restart-failed; explicit-restart eligibility and in-progress. The current V1 snapshot is 76 bytes, making the maximum response 100 bytes including header and CRC.

For a successful RESTART, the body is the new service identity, device identity, generation, and slot (12 bytes); for an unsuccessful call it is omitted. API result codes are returned as bounded numeric values. No response contains a pointer, TCB, stack, wake-event, runtime Thread*, or managed object reference.

## Buffering and malformed requests

The proposed native UART IRQ handler only reads received bytes and copies them into a 64-byte single-producer/single-consumer ring. It performs no parsing that calls service APIs, no allocation, scheduling, or logging. The scheduler boot thread owns parsing and dispatch. A single 32-byte parser buffer holds a partial request; there is no general terminal buffer or work queue.

The 64-byte input ring bounds capture to two complete requests. The host tool should use stop-and-wait and await the response before sending another command. On ring overflow, latch overrun, discard pending partial input, and resynchronize at the next GXDC magic. A partial frame is held only in the fixed parser buffer; a new magic before completion abandons it and starts a new frame. A CRC failure, wrong frame size, bad version, unknown command, nonzero flags/reserved field, or invalid target receives a bounded rejection when a complete header provides a request ID; otherwise it is discarded. None of these malformed requests calls either Phase 72 API or mutates service state.

An invalid RESTART state is passed only to the Phase 72 API, which returns INVALID_STATE without starting a replacement. Stale expected identity/generation/device values return STALE. RESTART remains admitted only from a settled, fully reclaimed RESTART_FAILED state. A normally stopped service is queryable but the current explicit-restart API rejects restart from that state; use the existing start path for normal startup.

No diagnostic command shares COM1 payload. COM2 is exclusively owned by this diagnostic protocol while enabled. Its host-side QEMU socket should bind to loopback only; it must not be exposed through a network listener.

## Execution context and synchronization

Status is read-only. Phase 72 status requires an active scheduler and current thread, and reuses the interrupt route's existing critical section while copying route flags and lifecycle/owner values. It does not wake the service, allocate, mutate the restart budget, change the route, or change identity. Repeated STATUS commands use that API synchronization and remain value-only.

Restart must run on the scheduler boot thread, as stated by the API declaration in managed_kernel_driver_worker.h. The implementation initializes/admit a replacement worker, performs the runtime attach/restart preparation, publishes the route only after preparation, and calls scheduler dispatch in a bounded readiness loop. It can allocate and schedule; it is not safe from an IRQ handler. The UART IRQ is therefore only a byte producer. This repository has no operator idle loop today, so Phase 75 must add a small persistent poll/drain hook to the scheduler boot-thread lifetime; it must remain active outside the COM1 worker. That hook drains and dispatches parsed requests synchronously through the Phase 72 APIs. No general deferred-work system is needed.

The dispatcher receives a pre-bound API context and calls only the two Phase 72 APIs. It interprets their bounded values and never dereferences owner fields. It does not access the service owner record, scheduler TCB, ThreadStore, route internals, wake event, or NativeAOT Thread*. Route synchronization and lifecycle policy remain inside Phase 72.

## Privilege and availability

The proposed authority is local physical access to a dedicated diagnostic UART, enabled by trusted boot/platform configuration. It is not authenticated in Phase 74. Any physical peer that can transmit on this port can request the two bounded actions, including a service restart; keep the port physically controlled. The QEMU development socket is host-local and likewise grants its host process access. Do not bind the diagnostic protocol to a network-facing socket.

With the proposed native lifetime and separate COM2 hardware:

| COM1 service condition | Ingress alive? | STATUS | RESTART |
|---|---|---|---|
| RESTART_FAILED; route disabled; no current generation; runtime detached | Yes | Reads the Phase 72 snapshot | Accepted only if Phase 72 reports settled explicit-restart eligibility and expected tuple matches |
| Normal stopped service | Yes | Reports stopped state | Current API returns INVALID_STATE |
| Running/starting/stopping service | Yes | Reports bounded state | Current API rejects with INVALID_STATE or IN_PROGRESS |
| COM1 route disabled | Yes | Reads route-disabled state | Does not rely on the COM1 route; API decides eligibility |
| COM1 UART or managed service absent | Yes, if separate diagnostic UART/platform support is present | Uses service context only | Uses boot-thread owner context and Phase 72 restart preparation |

If there is no supported independent UART, this control surface is unavailable on that platform. The diagnostic transport must not pretend COM1 is independent merely because raw transmit code is native.

## Production, debug, and QEMU classification

- Current production-capable input: none.
- Current production-capable output: native polled COM1 diagnostic output; output is not input.
- Current keyboard path: test/proof activation and shared managed dispatch, not an authoritative recovery input.
- Current QEMU-only controls: host-side QEMU monitor/serial launcher plumbing in selected test scripts. The monitor is not a guest service API; current COM1 sockets target COM1 itself.
- Current debugcon/QEMU debug input: none found.
- Selected next transport: native-owned COM2 RX/TX. Hardware use depends on explicit platform resource support; QEMU can validate it through a second emulated UART socket. No transport was added in Phase 74.

## Phase 75 recommendation

The smallest next step is to add optional secondary-UART resource discovery/reservation and byte-only RX capture, then add a small persistent poll/drain hook to the scheduler boot thread and connect it to a one-record parser and two-action dispatcher. For the PC-compatible mapping, use COM2 at 0x2F8/IRQ3 after platform confirmation. Keep the fixed protocol above, emit replies on COM2, and configure a separate loopback QEMU UART socket for the acceptance harness. First prove requests reach the dispatcher with the COM1 service stopped and its route disabled; then use Phase 72 status/restart APIs and the existing Phase 72 recovery evidence to verify that a new generation can start and handle a real COM1 event. Keep the endpoint explicitly disabled on platforms without a supported secondary UART.

This is a two-action persistent-service diagnostic boundary, not a shell, arbitrary function runner, generic driver console, managed-worker console, or network control protocol. Phase 61–66 managed-worker APIs remain separate.

## Phase 74 validation

Documentation-only change. No production code, host tests, or QEMU runs were added or rerun. Existing Phase 72 evidence is cited for API state/recovery behavior; it does not validate the proposed COM2 ingress. The repository's Phase 72/73 source paths were inspected read-only. The Phase 74 report records the live preflight state above; final commit and push state are reported by the Phase 74 task result.

Phase 74 is accepted as Outcome C. No Phase 75 work was started.

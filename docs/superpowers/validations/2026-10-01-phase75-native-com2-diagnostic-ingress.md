# Phase 75 — Native COM2 Diagnostic Ingress

## Outcome

**Outcome B — ingress exposed a parser recovery defect, the repair is in place, and acceptance passed.** A truncated GXDC header followed by a new frame could leave the parser consuming the next frame as payload. The parser now rejects an impossible version or size as soon as that field is complete and preserves a matching magic prefix when resynchronizing. Host coverage and the QEMU garbage-plus-fragment case pass with the repair. A minimal non-fixture build also exposed a `-Werror=unused-function` warning for the Phase 70 boot-thread wrapper; it is now compiled only with the fixture that calls it, and the ordinary build passes.

GXDC v1 is a bounded diagnostic control protocol, not a shell or general remote-management interface.

## Secondary UART discovery and ownership

The platform resource builder accepts an optional `GXOS_MANAGED_KERNEL_SECONDARY_UART_CONFIG`. A zero `present` value publishes no COM2 resources. The current PC/QEMU adapter sets the configuration only when the build explicitly enables `-EnablePhase75Com2DiagnosticIngress`; that configuration declares I/O base `0x2F8` and IRQ3. The implementation does not probe for or assume COM2 on other builds.

The resource builder publishes a separate eight-port serial range and interrupt descriptor for COM2. It rejects malformed or overlapping resources and refuses IRQ1 (i8042) and IRQ4 (managed COM1). The explicit QEMU build option is the platform evidence for this validation profile; a future platform can provide its own trusted UART configuration without changing the GXDC parser or dispatcher.

`GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT` is a native global context. It records presence/enabled state, base/IRQ, RX indices and count, overflow count/generation, parser state, IRQ/poll/protocol/API counters, UART callbacks, and the Phase 72 API context. It is not stored in the managed COM1 service and does not share COM1's wake event. Platform setup claims and enables it before starting the COM1 managed service. COM1 stop, failure, route disable, runtime detach, or missing generation do not disable COM2.

The enabled QEMU profile registers a native IRQ3 entry and routes the IRQ through the existing PIC/APIC setup. UART RX is enabled with the one-byte FIFO trigger; only receive-data-available interrupts are enabled. The code saves the previous UART interrupt-enable/modem-control and PIC/APIC route values and restores them if initialization fails. Without the platform option, boot reports `PHASE75_DIAGNOSTIC_INGRESS_DISABLED=NO_PLATFORM_UART` and continues normally.

## IRQ capture and boot-thread dispatch

IRQ3 enters `diagnostic_irq_entry.S` and calls the bounded native capture routine. The capture routine reads UART interrupt/status registers, reads at most 16 bytes per interrupt, appends bytes to the fixed ring, drops new bytes when the ring is full, counts each drop, signals the pending flag, and acknowledges the interrupt. It does not parse, transmit, allocate, call either service API, or run managed code.

The fixed RX ring holds 64 bytes; unread bytes are never overwritten. If it fills, newly read UART bytes are discarded and `rx_overflow_count` plus the overflow generation advance. The boot-thread consumer observes the generation change and resets partial parser state, then resumes scanning. Host coverage fills the ring, verifies the original queued bytes remain intact and 16 new bytes are counted as dropped, drains it, and successfully parses a later STATUS request.

The existing scheduler boot-thread wait/work paths call the diagnostic poll hook. Each poll drains at most 16 bytes and dispatches at most one complete command. It then returns to ordinary scheduler work. Phase 75 uses bounded fixture waits of 2,000,000 iterations only in the Phase 75-enabled fixture path; builds without Phase 75 keep their existing 100,000-iteration wait bounds. No new worker or general deferred-work mechanism is created.

Durable proof includes the guest markers `PHASE75_IRQ_CAPTURE_ONLY=1` and `PHASE75_BOOT_THREAD_DISPATCH=1`, API and IRQ counters, and host assertions that direct IRQ capture leaves fake STATUS/RESTART API counts unchanged. In `managed_kernel_diagnostic.c`, request dispatch calls only `gxos_managed_kernel_driver_service_get_status(...)` or `gxos_managed_kernel_driver_service_restart(...)`. It does not inspect the owner, route, runtime, ThreadStore, or scheduler internals; the worker context is passed only as the Phase 72 API parameter.

## GXDC v1 request

Requests are exactly 32 bytes. Every multibyte value is explicitly encoded little-endian; C struct padding is not used on the wire.

| Offset | Bytes | Field |
| --- | ---: | --- |
| 0 | 4 | ASCII `GXDC` |
| 4 | 2 | Protocol version, `1` |
| 6 | 2 | Record size, `32` |
| 8 | 4 | Request ID |
| 12 | 2 | Command ID: `1` STATUS, `2` RESTART |
| 14 | 2 | Flags, must be zero |
| 16 | 4 | Target device identity, COM1 is `1` |
| 20 | 4 | Expected failed service identity |
| 24 | 2 | Expected failed generation |
| 26 | 2 | Reserved, must be zero |
| 28 | 4 | CRC32 |

CRC is reflected IEEE CRC-32: polynomial `0xEDB88320`, initial value `0xFFFFFFFF`, final XOR `0xFFFFFFFF`. The request CRC covers bytes 0–27; the CRC field is excluded. The implementation uses a compact bitwise loop.

STATUS requires target identity `1` and zero expected-failure identity/generation. RESTART requires target identity `1` and a nonzero expected failed identity/generation tuple. Both commands require zero flags and reserved fields. Unknown commands, invalid target/fields, bad magic/version/size/CRC, and incomplete malformed frames do not call a service API or produce a response.

## Stream framing and recovery

The parser scans for `GXDC`, holds at most 32 bytes, and validates the fixed header as soon as its version and size fields are available. If the header is impossible, the parser scans the already-buffered suffix for a complete nested magic and otherwise retains up to three bytes that match the start of `GXDC`. Once a full 32-byte candidate is collected, it validates the CRC and fields before dispatch. On rejection it applies the same bounded suffix recovery. After an RX ring overflow it discards any partial frame and starts scanning again.

The parser is bounded and eventually recovers after garbage or a truncated record. Host tests cover garbage plus a fragment, a partial magic prefix followed by a valid STATUS, malformed headers, CRC failure, truncation, and overflow recovery. QEMU sends a garbage byte and truncated GXDC prefix immediately before a valid STATUS; the request is answered with the expected identity/generation.

## GXDR responses and API boundary

Responses use `GXDR`, version `1`, a length field, request ID, command ID, reserved zero field, API result, optional body, and a trailing CRC32. The response CRC covers every response byte except its final four-byte CRC field. Maximum response size is 100 bytes.

* STATUS is 100 bytes: 20-byte header, 76-byte explicit serialization of `GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1`, and CRC. It carries slot/device identity, owner state, current validity/identity/generation, route/runtime state, failure reason and last-failed tuple, restart budget/attempt counts, restart-failed state, and explicit-restart eligibility/in-progress flags. It exposes no pointers.
* Successful RESTART is 36 bytes: 20-byte header, 12-byte service handle (identity, device identity, generation, slot), and CRC.
* RESTART errors are 24 bytes: header, API result, and CRC, with no handle body.

STATUS (`1`) calls only `gxos_managed_kernel_driver_service_get_status(...)`. RESTART (`2`) calls only `gxos_managed_kernel_driver_service_restart(...)`, passing the expected failed identity/generation and target COM1 identity. The dispatcher does not implement restart policy. Native COM2 transmit uses a bounded UART-ready poll for each byte and never routes through COM1 or a managed serial driver.

## QEMU and host client

For the explicit PC/QEMU profile, build with `-EnablePhase75Com2DiagnosticIngress`. The fresh-boot runner adds a distinct second UART device at `0x2F8/IRQ3`, with its own `diag0` chardev socket. Both COM1 and COM2 sockets bind to `127.0.0.1` and use different ephemeral ports; unrelated QEMU sessions were left running and untouched. COM1 continues to drive the managed production service.

`tools/Phase75DiagnosticClient.psm1` constructs requests, writes the explicit byte format at a paced rate, scans a bounded number of response bytes, and validates response length, request correlation, and CRC. The one-command CLI is `tools/Send-Phase75DiagnosticRequest.ps1`; it accepts only STATUS or RESTART and connects to loopback. The QEMU runner exercises malformed frames and verifies silence as part of its acceptance fixture.

Example:

```powershell
./tools/Send-Phase75DiagnosticRequest.ps1 -Port 54001 -Command STATUS -RequestId 1
./tools/Send-Phase75DiagnosticRequest.ps1 -Port 54001 -Command RESTART `
  -RequestId 2 -ExpectedFailedIdentity 1 -ExpectedFailedGeneration 1
```

The port is the local COM2 socket port printed by the QEMU runner. This diagnostic surface assumes local physical access or an explicitly enabled local QEMU endpoint. It adds no authentication.

## Validation

Final Phase 75 gate: `Run-ManagedKernelPhase10FreshBoots.ps1`, three fresh QEMU boots, all passed. Each boot verified healthy STATUS and RESTART-while-running rejection; bad CRC/version/size/target silence; garbage/truncated-frame resynchronization; settled RESTART_FAILED status with route disabled, runtime detached, exhausted budget, and explicit restart eligibility; Phase 72 restart success; new service identity/generation; stale old-tuple rejection; restarted COM1 event delivery; final service stop/reclaim; and a COM2 STATUS response after COM1 stop.

The three final diagnostic protocol transcripts have SHA-256 `5E080C8A06FF6733575C439BE319D9DAB098A9778ED73EC509940F9B465C78DC` each. The managed payload hash is `3F2C49A25C4FF72C8876CED23C7C9C67EF930D3F84C8FC4013D00F6CF9D462BC` (4,791,808 bytes); the final Phase 75 EFI image hash is `DF321AE56CBC1CC5B889B0EC1AC33F9B3F729CBAAF0C861E8948A6259BEBB30B`.

Other checks passed:

* Phase 75 diagnostic/resource host tests; Phase 72 service-owner and status API host tests; managed-worker host tests.
* Diagnostic host cases cover exact request bytes and little-endian fields, CRC, STATUS/RESTART validation, bad magic/version/size/CRC/target/flags/reserved/command, truncated input, resynchronization, overflow, per-poll bounds, and no API calls for malformed frames.
* Phase 61, 62, 64, 65, and 66 managed-worker API host tests.
* Phase 56, 57, 58, 59, and 60 rollback host tests.
* Phase 69 DRAIN in the final Phase 75 stop/reclaim sequence, plus attach-failure/discard and idle-stop QEMU regressions.
* Phase 70 recoverable automatic-restart QEMU regression.
* Ordinary ManagedKernel build without diagnostic/failure-fixture options and one fresh boot. The guest reported `PHASE75_DIAGNOSTIC_INGRESS_DISABLED=NO_PLATFORM_UART`; COM1 completed normally.
* SyntheticScheduler build and three fresh QEMU proofs passed.

Generated evidence is not committed. The final three-boot evidence is under `%TEMP%\gxos-phase75-final-accepted-qemu-three\runs\run-{1,2,3}`; optional-disabled evidence is under `%TEMP%\gxos-phase75-ordinary-managed-qemu\runs\run-1`; Phase 69/70 regression evidence is under the matching `%TEMP%\gxos-phase75-phase69-*` and `%TEMP%\gxos-phase75-phase70-recoverable-qemu` directories. Final SyntheticScheduler serial logs are under `%TEMP%\gxos-phase75-final-accepted-synthetic\synthetic-runs-20261001-215505-459`.

## Limitations

The current platform adapter supports only the explicitly configured PC-compatible I/O UART profile. It does not claim COM2 through ACPI/firmware enumeration yet. Platforms without an approved secondary UART remain disabled. The protocol is local and unauthenticated and intentionally contains only STATUS and RESTART commands.

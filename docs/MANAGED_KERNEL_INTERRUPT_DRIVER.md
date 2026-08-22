# ManagedKernel Phase 9: Interrupt Capture and Deferred Serial Delivery

Phase 9 extends the Phase 8 COM1 driver with one narrow receive path. The
hardware interrupt remains native-owned. Native code captures a byte in a
fixed queue, returns through the native interrupt gate, and managed code drains
that queue at an explicit safe point. No managed method, GC operation, or
allocation is reachable from the raw ISR.

## Substrate audit and selected path

The current guideXOS harness preserves the firmware IDT and interrupt
controller state; it does not have a guideXOS-owned APIC/PIC abstraction,
interrupt stack, or generic IRQ registration API. The existing QEMU/firmware
layout places the legacy timer at vector `0x20`. COM1 is the standard 16550
UART at I/O base `0x3F8`, legacy IRQ4, so the selected gate is vector `0x24`.

The native path therefore installs only the temporary vector `0x24`, copies
the current IDT before changing it, and restores the exact previous IDTR on
unsubscribe. Native code retains authority over COM1 I/O, UART IER/FCR state,
the IOAPIC legacy IRQ4 redirection entry, PIC master-mask bit 4, and PIC/LAPIC
EOI. The managed ABI contains no port address, vector, PIC, or raw IRQ
callback. The Phase 9 window lowers the UART FIFO RX trigger to one byte and
restores the console's original `FCR=0xC7` configuration during unsubscribe.

`serial_irq_entry.S` saves all general-purpose registers, aligns a native
stack, calls only `gxos_managed_kernel_serial_irq_capture`, restores the
registers, and executes `iretq`. The capture function performs no allocation,
logging, managed call, or unbounded loop. It reads the UART IIR/LSR, consumes
the RBR byte when the hardware reports a receive event, publishes a fixed event
record, and sends EOI.

## Interrupt ABI v1

The ABI is separate from Serial Services v1 and uses packed fixed-size records:

| Structure | Size | Purpose |
|---|---:|---|
| `GX_MANAGED_KERNEL_INTERRUPT_SERVICES_V1` | 88 | versioned subscribe, unsubscribe, drain, and stats callbacks |
| `GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1` | 48 | one serial receive event |
| `GX_MANAGED_KERNEL_INTERRUPT_STATS_V1` | 80 | IRQ, ISR, queue, drop, and active-state counters |

The native queue is a static eight-record ring. One ISR entry captures at most
four source records (`MaxDrain=4`); a full queue increments `DroppedCount`
without overwriting published records. Event records carry the fixed COM1
identity, a monotonic nonzero sequence beginning at one, payload length one,
the `HARDWARE_CAPTURE` flag, and a zero timestamp in v1. All output pointers
are checked against the native host range before native writes.

Managed `ManagedInterruptDispatcher` validates the table metadata and callback
addresses, subscribes by exact event/device identity, drains into stack-backed
storage, and fails closed on any malformed identity, flag, length, reserved
field, timestamp, or sequence. `ManagedSerialDriver` accepts the event only in
the started/subscribed state, requires the expected validation byte, and
requires strict sequence continuity.

## Acceptance sequence

`tools/Run-ManagedKernelPhase9FreshBoots.ps1` uses a bidirectional QEMU socket
chardev attached to an explicit `isa-serial` device at `0x3F8`/IRQ4, so the
host can inject bytes into the guest. Each fresh boot must complete the earlier
Phase 1–8 pass markers, then perform this sequence:

```text
subscribe -> RX_READY -> host sends 'R' -> native IRQ capture
         -> managed safe-point dispatch -> RX_FROM_HARDWARE_OK
         -> runtime/time/memory/GC/inventory/PCI activity
         -> RX_RUNTIME_SURVIVAL_OK -> host sends 'S'
         -> native IRQ capture -> managed dispatch
         -> RX_AFTER_RUNTIME_OK -> unsubscribe
         -> host sends 'Z' -> no post-unsubscribe delivery
         -> exact counters/accounting -> PHASE9_PASS
```

On a successful acceptance boot, the guest must report two IRQ entries, two
serial ISR captures, two enqueued and drained records, zero drops, inactive
hardware after unsubscribe, and restored native memory accounting. The host
records both injected bytes separately in `injections.log`; `serial.log` and
`injections.log` each receive a SHA-256 fingerprint in the runner output.

Current validation status: `PHASE9_PASS` is complete. The original Phase 8
`-serial file:<path>` backend is output-only, so it cannot provide deterministic
host-to-guest input. Phase 9 uses a full-duplex QEMU 11 TCP chardev socket with
one `isa-serial` COM1 device; the runner writes raw bytes with
`NetworkStream.WriteByte` and flushes the stream. The bounded diagnostic run
first observed `LSR=0x61` after sending `0x52`, proving UART RX data presence.

The same diagnostic run exposed a second routing defect: the firmware profile
left legacy PIC IRQ4 unmasked while its vector base was zero, so COM1 could
arrive at CPU vector `0x04` instead of the configured IOAPIC vector `0x24`.
Phase 9 now masks only legacy PIC IRQ4 during the subscription window, keeps
the existing IOAPIC/IDT/native queue path authoritative, and restores the
saved PIC state on unsubscribe. Three fresh QEMU boots reached
`PHASE9_PASS`; each reported two IRQ entries, two serial ISR captures, two
enqueues, two drains, zero drops, unsubscribe success, and native accounting
restoration. The runner also timestamps each native `SERIAL_IRQ_CAPTURED`
marker. Evidence is retained under
`artifacts/phase9-final-acceptance-evidence-20260822-final4`.

Native queue semantics are covered by
`tools/Run-ManagedKernelInterruptNativeHostTests.ps1`. Managed ABI,
dispatcher, driver state, sequence, runtime-arena, and post-unsubscribe
semantics are covered by
`tools/Run-ManagedKernelInterruptHostTests.ps1`. The old
`Run-ManagedKernelFreshBoots.ps1` remains the Phase 8 non-interactive
regression gate and stops at the Phase 8 pass marker before the Phase 9 path.

Phase 9 does not claim a general interrupt framework, APIC support, arbitrary
managed ISRs, device-independent receive queues, DMA, or asynchronous managed
execution. The next interrupt-bearing device must repeat the substrate audit
and introduce its own native-authoritative ABI.

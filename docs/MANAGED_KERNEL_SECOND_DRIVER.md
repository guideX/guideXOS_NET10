# ManagedKernel Phase 11 — Second Driver Foundation

Phase 11 adds an i8042/PS/2 keyboard driver beside the Phase 10 COM1 driver.
Both drivers use the same scheduler-driven `ManagedDriverWorker`, bounded
native event queue, and managed dispatcher. No device-specific worker or
manual managed drain was added.

## Candidate audit and selection

The repository audit found no existing safe keyboard, USB HID, mouse, timer,
RTC, framebuffer, or generic input path that could be reused without creating
a broader subsystem. PCI inventory is read-only and has no suitable input
consumer. The selected device is the QEMU i8042 keyboard path because it is a
small port-I/O device with deterministic monitor injection and no DMA,
bus-mastering, MMIO, or generic managed port-I/O exposure.

| Property | Phase 11 contract |
| --- | --- |
| Native identity | platform keyboard, device ID `1`, i8042 |
| Raw access | native ports `0x64` status and `0x60` data |
| Interrupt | legacy IRQ1, IOAPIC vector `0x21`, edge-triggered, unmasked |
| PIC policy | PIC IRQ1 remains masked; IOAPIC is authoritative |
| Event form | raw Set 1 scancode, one byte, make/break encoded by bit 7 |
| Managed driver | `ManagedKeyboardDriver`, ID `0x8202` |
| QEMU injection | monitor `sendkey a`, `sendkey b`, `sendkey c` |
| DMA/MMIO | none |

The firmware-provided IDT is copied once and the native loader installs only
the bounded serial and keyboard gates. The keyboard enable path sends i8042
command `0xAE`, waits for the controller input buffer to clear, saves and
restores the IOAPIC entry, and leaves auxiliary (`AUX`) bytes out of the
keyboard route. The native guideXOS path is the sole controller data reader in
this harness; no second consumer competes for port `0x60`.

## ABI and event routing

`GX_MANAGED_KERNEL_INPUT_SERVICES_V1` is a separate 88-byte, packed, versioned
service table. It reuses the 48-byte interrupt event record and bounded
eight-record queue, but has its own subscribe/unsubscribe/drain/query function
addresses so the earlier serial service-table ABI remains unchanged. The
32-byte native-authoritative keyboard descriptor publishes identity, IRQ,
Set 1, and raw/make-break capabilities. Managed layout checks cover sizes and
key offsets; all reserved fields are required to be zero.

The normalized event record contains event type, device kind, device ID,
global monotonic sequence, hardware-capture flag, one-byte payload, status,
and no managed pointer. Sequence numbers are global across serial and
keyboard records; each managed driver additionally rejects stale records and
validates its own identity. The worker drains at most four records per
activation, so interleaved serial and keyboard events share wake coalescing,
ordering, and bounded batching.

## Native/managed boundary

The IRQ1 entry stub saves the register and XMM/FPU state, calls only the
bounded native capture routine, sends EOI, restores state, and returns. The
capture routine checks controller status, discards auxiliary data, reads one
scancode from the data port, publishes a primitive record, and requests one
coalesced worker wake. It does not allocate, log, call C#, or retain a managed
reference.

`ManagedKeyboardDriver` owns binding validation, explicit lifecycle
(`Uninitialized → Initialized → Started → Subscribed → Stopped → Disposed`),
a 64-byte KernelArena-backed bounded history, raw/make-break interpretation,
sequence validation, and the small runtime-survival proof. The managed worker
remains generic; routing is based on event identity and type.

## Acceptance and teardown

The Phase 11 runner records serial injection, monitor key commands, timestamps,
QEMU command lines, firmware hashes, serial evidence, and per-run timelines.
It requires three fresh single-threaded-TCG QEMU boots. The acceptance chain
injects real monitor keys, observes controller IRQ capture and queue counters,
dispatches `A` and `B` make/break records through the shared worker, performs
GC/arena/inventory/PCI runtime activity between keys, and proves serial still
routes while keyboard is active.

Keyboard unsubscribe disables only the Phase 11 keyboard route, restores its
IOAPIC/PIC/controller state, detaches and destroys the keyboard driver, and
leaves the serial route active. A post-unsubscribe `sendkey c` produces no
keyboard delivery. The serial route is then unsubscribed, the common worker is
stopped and reclaimed, both managed driver arenas are destroyed, and the
ManagedKernel accounting baseline is checked for restoration with zero normal
path queue drops.

Host worker tests cover two consumers, wrong-route rejection, interleaving,
coalesced wakeups, bounded activations, queue-full accounting, teardown of one
route while the other remains usable, and repeated wake/sleep cycles.

## Limitations

This phase proves raw Set 1 scancode delivery, not keyboard layouts, Unicode,
modifiers, repeat, mouse, USB HID, or a general input server. Native owns the
i8042 controller and IRQ; managed owns identity binding and bounded event
interpretation. The harness uses the native IOAPIC keyboard route with the
legacy PIC IRQ1 bit masked because the firmware PIC base is not a valid guest
interrupt vector for this boot configuration.

Phase 11 proves that the ManagedKernel driver architecture is reusable across
multiple real hardware devices: both managed drivers share the scheduler-
driven execution foundation introduced in Phase 10.

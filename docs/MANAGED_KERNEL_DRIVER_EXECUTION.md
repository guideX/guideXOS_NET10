# ManagedKernel scheduler-driven driver execution

ManagedKernel Phase 10 adds a narrow execution foundation for managed hardware
drivers. It does not add a managed `Thread`, thread pool, `Task`, async
continuation, or general-purpose synchronization API.

## Selected substrate

The worker reuses the existing cooperative scheduler substrate in
`src/Gate4Harness/scheduler_foundation.[ch]` and `scheduler_context.S`:

- one statically bounded scheduler TCB and stack are created suspended;
- the existing scheduler context switch attaches the TEB/GS, TLS, FLS,
  NativeAOT thread state, and worker stack already used by the scheduler
  callback proof;
- the worker TLS vector points at a private block seeded from the initialized
  main NativeAOT TLS block, preserving runtime thread state without sharing
  mutable TLS storage;
- an existing auto-reset scheduler event is the wake primitive;
- the worker is resumed before subscription, while the first scheduler
  activation is deferred until the first hardware notification;
- the boot scheduler pump dispatches the worker after that interrupt returns;
- a bounded activation drains at most four managed interrupt records and
  yields back to the boot runnable context when work remains.

## Phase 10 acceptance status

Phase 10 is complete as of 2026-08-22. Three fresh QEMU boots passed the full
sequence in `artifacts/phase10-final-fix-evidence15`: the same scheduler
worker handled the real `0x52` and `0x53` COM1 interrupts on every boot after
runtime/GC activity, then drained the `A/B/C` burst, unsubscribed, stopped,
reclaimed, and restored accounting. The acceptance runner uses deterministic
single-threaded TCG for this control; multi-threaded TCG was not used as
acceptance evidence because it intermittently raised an unrelated NativeAOT
runtime GP during the existing runtime-survival proof.

The native IRQ capture path remains the producer. It only reads the bounded
UART source, writes a primitive ABI record into the fixed eight-record queue,
updates counters, and requests one coalesced event wake. It does not call
managed code, allocate, log, or retain a managed reference. The native
notification callback only signals the existing scheduler event.

The second-event failure was in the native IRQ return stub, not in the worker
architecture. `src/Gate4Harness/serial_irq_entry.S` saved the register-save
stack pointer in `%r11`, which is caller-clobbered by the C capture routine.
The first interrupt returned successfully by accident; on the second return
the corrupted `%r11` restored an invalid `%rsp`, so capture, enqueue, wake,
and EOI completed but the CPU could not return durably to the scheduler. The
stub now keeps the saved stack pointer in callee-saved `%r12` until the C
capture returns. The stub still calls no C# code: the C routine only performs
bounded native capture and EOI work.

The diagnostic split showed the following before/after values on a successful
run. `IIR=0xC1` is the no-pending state after the RBR read; `IOAPIC_LOW=0x24`
means vector `0x24`, edge-triggered, unmasked, with remote-IRR clear.

| State | Before IRQ1 | After IRQ1 | Before IRQ2 | After IRQ2 |
| --- | ---: | ---: | ---: | ---: |
| UART IER | `0x01` | `0x01` | `0x01` | `0x01` |
| UART IIR | `0xC1` | `0xC1` | `0xC1` | `0xC1` |
| UART LSR | `0x60` | `0x60` | `0x60` | `0x60` |
| UART MCR | `0x0B` | `0x0B` | `0x0B` | `0x0B` |
| FCR configuration | `0x07` | `0x07` | `0x07` | `0x07` |
| PIC mask | `0xFF` | `0xFF` | `0xFF` | `0xFF` |
| IOAPIC low | `0x24` | `0x24` | `0x24` | `0x24` |
| IRQ / ISR / enqueue / drain | `0/0/0/0` | `1/1/1/1` | `1/1/1/1` | `2/2/2/2` |
| work pending | `0` | `0` | `0` | `0` |
| wake requests / worker wakes | `0/0` | `1/1` | `1/1` | `2/2` |

The readiness marker is emitted only after the subscription is active, RX IER
and OUT2 are enabled, legacy PIC IRQ4 remains masked, IOAPIC vector `0x24`
remains unmasked and edge-triggered, the queue is empty, `work_pending` is
clear, and the same worker is blocked on its live auto-reset event. After a
dispatch, the worker drains the queue, clears `work_pending` through the
native re-arm path, returns to the event wait, and can be signaled again. An
auto-reset signal wakes one waiter and consumes the signal; a signal with no
waiter stores one pending signal for the next wait. The worker host model now
tests three separate signal → dispatch → sleep cycles on one worker instance.

## Managed ownership and routing

`ManagedDriverWorker` owns the managed lifecycle and dispatch policy. It holds
the existing `ManagedInterruptDispatcher` and `ManagedSerialDriver` objects,
validates each ABI record, rejects malformed or stale records individually,
and routes valid records to the subscribed driver. The native queue contains
only the versioned primitive event record; no managed object crosses the IRQ
boundary.

The managed object has the lifecycle
`Created -> Starting -> Running -> Stopping -> Stopped -> Destroyed`.
Shutdown first unsubscribes the driver, then asks the native worker to stop and
reclaim its scheduler handle, stack, TCB, and event, and finally releases the
managed driver arena. Repeated lifecycle transitions are rejected. The worker
adds no KernelArena allocation; the existing serial driver remains the only
two-page driver arena allocation. The full-run accounting delta also includes
managed runtime pages created by the required burst proof; those pages are
included in the final pre-unsubscribe baseline and are not treated as leaks.

## Acceptance sequence

The Phase 10 QEMU runner waits for the worker-ready and subscribed markers,
injects `0x52`, and lets the scheduler-driven worker perform the first drain
and runtime/GC/allocator/inventory/PCI survival proof. It then waits for the
guest’s bounded second-wait-ready marker before injecting `0x53`, injects the
bounded `A/B/C` burst, waits for the worker to drain five total records,
unsubscribes, and injects `0x5A` after unsubscribe. The post-unsubscribe byte
must not produce a new IRQ entry. Three fresh boots are required, with serial,
injection, timeline, command-line, payload, and firmware identity recorded.

The legacy explicit `TryDispatch` path remains as a strict diagnostic/host
control for Phase 9 compatibility. The official Phase 10 path never invokes
that direct managed safe-point drain; its first delivery is reached through
the scheduler worker wake and dispatch export.

The teardown comparison is taken again immediately before unsubscribe, after
the required managed burst. This separates intentional NativeAOT pages
retained by the managed runtime from the worker/subscription cleanup delta:
the complete Phase 10 run releases seven pages, two commitments, one
reservation, and two VM regions, including managed activity retained by the
worker proof. The final run reports five enqueued and drained records, zero
drops, no post-unsubscribe delivery, worker reclamation, and restored
accounting.

Host model tests independently cover two-consumer routing, stale/unknown
record rejection, queue-full drops, wake coalescing, four-record bounded
activations, yield/reschedule accounting, and duplicate lifecycle rejection.
The event API host vector also covers auto-reset wait/consume/re-signal
behavior; the dedicated worker vector reports `REPEATED_WAKE_CYCLES=3`.

## Known boundaries

This phase proves one managed worker and one bounded serial receive route. It
does not claim arbitrary managed thread creation, concurrent GC stress,
multiple worker scheduling, general IRQ routing, DMA, or broad Windows
compatibility. The scheduler remains cooperative and its worker stack/TCB
pool is statically bounded.

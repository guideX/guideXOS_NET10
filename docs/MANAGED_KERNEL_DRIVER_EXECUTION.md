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

The native IRQ capture path remains the producer. It only reads the bounded
UART source, writes a primitive ABI record into the fixed eight-record queue,
updates counters, and requests one coalesced event wake. It does not call
managed code, allocate, log, or retain a managed reference. The native
notification callback only signals the existing scheduler event.

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
two-page driver arena allocation and its accounting must return to the
post-runtime baseline.

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

Host model tests independently cover two-consumer routing, stale/unknown
record rejection, queue-full drops, wake coalescing, four-record bounded
activations, yield/reschedule accounting, and duplicate lifecycle rejection.

## Known boundaries

This phase proves one managed worker and one bounded serial receive route. It
does not claim arbitrary managed thread creation, concurrent GC stress,
multiple worker scheduling, general IRQ routing, DMA, or broad Windows
compatibility. The scheduler remains cooperative and its worker stack/TCB
pool is statically bounded.

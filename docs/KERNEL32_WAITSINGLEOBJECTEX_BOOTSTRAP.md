# `KERNEL32.dll!WaitForSingleObjectEx` payload boundary

The first natural NativeAOT wait is the call at payload caller RVA `0x3539C`
through IAT RVA `0x7D180`:

```text
KERNEL32.dll!WaitForSingleObjectEx
descriptor index: 2
symbol index:     0x29
IAT RVA:          0x7D180
caller RVA:       0x3539C
```

The exact AMD64 register capture is:

```text
RCX = 0xA702000000010002
RDX = 0xFFFFFFFF       (INFINITE)
R8  = 0x0              (FALSE)
R9  = 0x0              (unused)
stack args: none
```

The handle resolves through the guideXOS generation-checked handle table to
object slot `1`, generation `1`, type `Event`.  It is an auto-reset,
nonsignaled Event with one public handle reference and no waiter before the
call.  The object was created by the first NativeAOT `CreateEventW` call at
caller RVA `0x41E04`, then supplied as the `CreateThread` parameter for the
worker entry at RVA `0x35320`.  The worker initializes COM, signals the second
manual-reset startup Event, and then waits on this first Event.  This is
finalizer/worker startup coordination, not the main thread's earlier wait on
the second Event.

The bridge uses one shared internal wait routine.  It validates the public
handle and object generation, consumes already-signaled auto-reset Events,
preserves manual-reset state, supports zero/finite/`INFINITE` timeouts, and
registers a finite deadline in the same wait record used by the existing
`WaitForMultipleObjectsEx` substrate.  A blocking wait follows:

```text
Running -> Blocked/TimedWait -> Runnable -> Running
```

Signal and timeout completion both unlink the waiter, release the internal
object pin, enqueue the TCB once, and complete through the existing scheduler
finish path.  Finite deadlines use an overflow-saturating millisecond value
from the configured EFI `GetTime` clock.  Successful results and
`WAIT_TIMEOUT` preserve the caller's prior `LastError`; invalid handles return
`WAIT_FAILED` with `ERROR_INVALID_HANDLE`, while unsupported object types and
unsupported alertable mode return `WAIT_FAILED` with
`ERROR_INVALID_PARAMETER`.

The observed first call has `bAlertable == FALSE`.  guideXOS has no APC queue,
completion routine delivery, or asynchronous-I/O completion source, so the
adapter does not return `WAIT_IO_COMPLETION`.  Alertable waits remain an
explicitly unsupported, fail-closed contract until such a mechanism is
implemented.

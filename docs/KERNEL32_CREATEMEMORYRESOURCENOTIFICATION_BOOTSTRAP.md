# `KERNEL32.dll!CreateMemoryResourceNotification` payload boundary

This bounded milestone routes only the exact import pair
`KERNEL32.dll!CreateMemoryResourceNotification` (descriptor `2`, symbol index
`0x36`, IAT RVA `0x7D1E8`). The route is enabled by
`GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION`; similarly named symbols or
exports from another DLL are not routed.

## Supported contract

The Microsoft x64 signature is consumed as:

```text
HANDLE CreateMemoryResourceNotification(
    MEMORY_RESOURCE_NOTIFICATION_TYPE NotificationType);
```

The only accepted raw value is `0`, interpreted as
`LowMemoryResourceNotification`. Raw value `1`,
`HighMemoryResourceNotification`, and every other value fail closed with no
object or registry-slot change. No `QueryMemoryResourceNotification` route is
provided, and no broader Win32 memory-resource compatibility is claimed.

## Object and handle design

Success allocates one explicit internal `MemoryResourceNotification` object
through the existing fixed generation-checked opaque object registry. The
object type tag is `3`; it is not an Event, Thread, `EFI_EVENT`, raw pointer,
registry index, constant, Windows handle, or untyped integer token. The handle
encoding remains:

```text
[63:56] magic 0xA7 | [55:48] type | [47:16] generation | [15:0] slot+1
```

The object owns a one-reference public handle, open state, notification type,
generation, registry slot, and object slot. Its waitable state is backed by the
same internal waiter/signaled-state foundation used by Event objects. Generic
internal waitable inspection accepts both types while typed lookup preserves
the `MemoryResourceNotification` identity. No public wait, close, duplicate,
query, signal, reset, or thread API is added here.

The existing capacities are unchanged: 6 TCBs, 12 Event records, and 16 total
object records. Memory-resource notifications use one separate notification
record slot; the object consumes an ordinary object-registry slot but never a
Thread slot. At the payload boundary the boot Thread object is slot `0`, the
two Events are slots `1` and `2`, and the notification is object slot `3` and
notification-record slot `0`, all generation `1`. There are 4 live objects and
12 free object slots; 2 of 12 Event slots remain free, and the one notification
slot is occupied.

## Bootstrap state and lifetime

The returned notification is initialized nonsignaled with zero waiters and one
public handle reference. guideXOS has no proven Windows-compatible low-memory
pressure model, so this milestone does not invent thresholds, free-memory
percentages, GC-pressure transitions, polling, or a worker. No UEFI event is
created and no scheduler thread is started. Actual pressure signaling is
deferred until evidence requires it. The persistent object remains live after
the call and through the next naturally reached import blocker.

This is an explicit bounded bootstrap policy, not a claim of complete
`CreateMemoryResourceNotification` or `LowMemoryResourceNotification`
compatibility.

## Exact payload proof

Every real-payload gate verified the immutable payload before execution:

```text
Path:   artifacts\veh-final3-normal-gate\ESP\GXOS\gxos-managed-entry-probe.dll
SHA256: 2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837
```

The three fresh enabled QEMU runs agreed on one invocation, `RCX=0`, raw type
`0`, type-3 handle `0xA703000000010004`, generation `1`, notification slot `0`,
object slot `3`, nonsignaled state, zero waiters, one public reference, and no
additional `CreateEventW` calls. The runtime call site was
`base + 0x353F8`; the payload directly executed
`mov [base+0xADA28], rax`. After the call and at the next blocker, the stored
value at `base + 0xADA28` remained the returned handle unchanged. The bridge
only inspected this location; it did not write payload storage.

The same runs preserved the two established Events: call 1 was auto-reset,
nonsignaled, and stored at `base + 0xADA08`; call 2 was manual-reset,
nonsignaled, and stored at `base + 0xADA18`. Both remained live. Final enabled
counts were 2 live Event objects, 1 live notification object, 3 live public
handle references, 1 scheduler Thread object (the boot object), zero additional
threads, and zero waiters.

## Disabled-route control

`Build-Gate4Harness.ps1 -Scenario CreateMemoryResourceNotificationDisabled`
retains the existing `CreateEventW` route while omitting only
`GXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION` and its source. A fresh
disabled run returned to the exact `CreateMemoryResourceNotification` blocker
after the two successful Event calls, with 2 Event objects, 0 notification
objects, 0 notification handles, 3 total live objects including the boot
Thread, and 2 public handles. The disabled route did not create a notification
object or invoke any additional CreateEventW call.

## Next honest blocker

With this route enabled, the payload naturally reaches exactly one next
unsupported import and stops there. The observed values were:

```text
DLL:             KERNEL32.dll
Symbol:          CreateThread
Descriptor:      2
Symbol index:    0x2D
IAT RVA:         0x7D1A0
Runtime IAT:     base + 0x7D1A0 = 0x054F81A0
Runtime call:    0x054B7FA0
Caller RVA:      0x3CFA0
RCX:             0x0
RDX:             0x0
R8:              0x054B0320
R9:              0xA702000000010002
Stack argument:  arg5 = 0x4
```

The deepest successful continuation marker is the final notification/object
summary immediately before this CreateThread fail-fast stop. At that point the
notification handle was still stored at `base + 0xADA28`, its waiter count was
zero, and no notification query, close, or duplicate occurred. The exact
`CreateThread` import is intentionally not implemented by this milestone.

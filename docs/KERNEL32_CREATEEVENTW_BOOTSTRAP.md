# `KERNEL32.dll!CreateEventW` payload boundary

This milestone adds the first payload-facing use of the cooperative scheduler
foundation. It routes only the exact import identity
`KERNEL32.dll!CreateEventW` (descriptor `2`, symbol `42`, IAT RVA `0x7D188`).
The route requires both the DLL and symbol strings to match exactly; a same-
named export from another DLL is not routed.

## Supported contract

The Microsoft x64 signature is consumed as:

```text
HANDLE CreateEventW(
    LPSECURITY_ATTRIBUTES lpEventAttributes,
    BOOL bManualReset,
    BOOL bInitialState,
    LPCWSTR lpName);
```

Only the observed contract is supported: `lpEventAttributes == NULL`,
`lpName == NULL`, and Boolean consumption of `bManualReset` and
`bInitialState`.

Named events, event namespaces, `SECURITY_ATTRIBUTES`, inherited handles,
access rights, and broader Win32 compatibility are not implemented. A
non-null attribute or name fails closed, creates no object, consumes no
registry slot, and returns `NULL`. The bounded internal last-error value is
`87` (`ERROR_INVALID_PARAMETER`); no `GetLastError` payload route is added.
Registry exhaustion returns `NULL` with bounded internal value `8` and does
not disturb existing objects.

## Object and handle ownership

Each successful call creates one guideXOS Event record and one public opaque
handle through the existing fixed registries. The handle contains the existing
magic, object-type tag, generation, and slot encoding:

```text
[63:56] magic 0xA7 | [55:48] type | [47:16] generation | [15:0] slot+1
```

The returned value is a synthetic handle understood by future event
operations. It is not an `EFI_EVENT`, pointer, TCB pointer, raw slot,
constant, or Windows handle value. The Event record owns the manual-reset
flag, initial signaled state, waiter count, open state, and public reference.
The public handle lifetime remains distinct from the eventual object lifetime.

Manual-reset `FALSE` maps to an auto-reset Event; `TRUE` maps to a manual-reset
Event. Initial state is copied exactly. Creation starts with zero waiters and
one public reference. The fixed foundation capacities remain six TCBs, twelve
Event records, and sixteen total object records; they were not increased.

The real bridge adopts the already-created NativeAOT boot GS/TEB/TLS pages and
loader stack as the scheduler's boot environment. It initializes the real
execution-context registry once, without replacing the payload's TLS state or
creating a scheduler worker. Synthetic proof objects are not carried into the
payload-facing run.

## Caller storage and observed run

The exact payload was hash-checked before each build and run:

`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`

The known persistent records at payload RVAs `base + 0xADA08` and
`base + 0xADA18` retained the first two returned handles unchanged. Each
allocator-backed record stores the opaque 64-bit return value at
`[record + 0]`; the bridge checks the value after return, without modifying
payload memory beyond the API return value. The two observed calls had NULL
attributes and names, preserved the full 64-bit handles, and left TCB state
unchanged:

| Call | Caller RVA | Manual | Initial | Event slot | Handle generation/type | Storage |
| ---: | ---: | ---: | ---: | ---: | --- | --- |
| 1 | `0x41E04` | auto | nonsignaled | `0` | `1` / Event | `base + 0xADA08` |
| 2 | `0x41E37` | manual | nonsignaled | `1` | `1` / Event | `base + 0xADA18` |

The three fresh enabled QEMU runs agreed: two successful CreateEventW calls,
manual-reset sequence `FALSE, TRUE`, initial-state sequence `FALSE, FALSE`,
two live Event objects, two live public handles, zero waiters, one auto-reset
event, one manual-reset event, and no additional scheduler thread.

The requested Windows oracle describes ten CreateEventW calls. This exact
payload run reaches the next unresolved import after calls 1 and 2, before
the later static CreateEventW call forms execute. The bridge does not route or
fabricate continuation through that blocker. The honest next blocker is:

```text
DLL:           KERNEL32.dll
Symbol:        CreateMemoryResourceNotification
Descriptor:    2
Symbol index:  0x36
IAT RVA:       0x7D1E8
Runtime IAT:   0x054F81E8
Call site:     0x054B03F8
Caller RVA:    0x353F8
RCX/RDX/R8/R9: 0 / 1 / 0x3F8 / 0
Stack arg 5:   0
```

Implementing that import is explicitly outside this milestone, so the
ten-call oracle and ten-object postcondition are not claimed by this build.

## Disabled-route control

`Build-Gate4Harness.ps1 -Scenario CreateEventWDisabled` retains all earlier
startup routes but omits only `GXOS_ENABLE_CREATE_EVENT_W` and the scheduler
event source. The exact payload then stops at
`KERNEL32.dll!CreateEventW`, with no CreateEventW invocation and no Event
objects. This switch does not disable on-exit, module-handle, malloc,
exception-context, or vectored-handler support.

The synthetic scheduler proof remains independently required and passing. It
continues to cover complete AMD64 contexts, GPR and XMM preservation, MXCSR,
x87, GS/TLS/FLS/last-error isolation, event signaling/reset/wait behavior,
thread lifetime, teardown, negative controls, and stack canaries. Passing the
synthetic proof does not imply that the payload has reached waits, signaling,
reset, close, duplicate, thread creation, timeout scheduling, or any other
unimplemented API.

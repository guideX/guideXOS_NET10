# Synthetic cooperative scheduler foundation

This document describes the bounded Gate4 scheduler contract proof.  It is an
internal test foundation only.  It is not connected to NativeAOT import
routing and it does not claim Windows thread, event, or handle compatibility.
The real payload path therefore remains unchanged and still stops at the
unresolved `KERNEL32.dll!CreateEventW` import.

## Purpose and limits

The proof is a guideXOS-owned, one-CPU cooperative scheduler.  It demonstrates
the execution, wait, event, lifetime, and thread-environment machinery needed
by the observed startup path without using a UEFI event callback, timer
callback, MP Services, `StartupThisAP`, `StartupAllAPs`, or another processor.

The registry is fixed at six synthetic TCBs, twelve synthetic events, and
sixteen object records.  The boot context consumes one TCB and one object
record, so the proof can create at most five additional live synthetic thread
records.  Worker stacks are fixed at 16 KiB (four 4 KiB pages).  All storage is
embedded in the scheduler or obtained through the bounded page callbacks; no
linked-list growth or unbounded allocation is used.

Current limitations are explicit:

- one CPU only;
- cooperative scheduling only, with no preemption or SMP;
- no timeout queue yet, although wait preparation is the integration point for
  a future timer queue;
- no NativeAOT payload import integration and no general Windows API claim;
- unnamed events only, with no `SECURITY_ATTRIBUTES` support;
- no production post-`ExitBootServices` scheduler claim;
- synthetic contract proof only.

## TCB and states

The boot/main execution context is a real scheduler TCB.  Each live TCB has a
stable internal identity, state, entry and argument, return value, saved
context, owned stack, public-handle reference count, execution-lifetime
reference, GS/TEB-like state, TLS vector, TLS block, 64-slot FLS array,
last-error value, and blocked-event identity.

States are `Free`, `CreatedSuspended`, `Runnable`, `Running`, `Blocked`, and
`Terminated`.  Creation starts at `CreatedSuspended` with suspend count one.
Resume decrements that count and changes the TCB to `Runnable` when it reaches
zero.  The runnable selector is a bounded round-robin scan beginning after the
current TCB.  A blocking wait registers the current TCB in the event's bounded
waiter array, marks it `Blocked`, and selects a runnable TCB.  A wake changes
the waiter to `Runnable`; context transfer remains an explicit scheduler
operation.

## Saved AMD64 context and switch ABI

`GXOS_SCHEDULER_CONTEXT` is a 272-byte, 16-byte-aligned record:

| Offset | Field |
|---:|---|
| `0x00`–`0x38` | `RBX`, `RBP`, `RSI`, `RDI`, `R12`–`R15` |
| `0x40` | `RSP` |
| `0x48` | `RIP` |
| `0x50` | saved `RFLAGS` |
| `0x58` | `MXCSR` |
| `0x5C` | x87 control word |
| `0x60` | GS base |
| `0x68`–`0x108` | XMM6 through XMM15, 16 bytes each |

The assembly entry is the Windows x64 ABI form
`gxos_scheduler_context_switch(old_context_pointer, new_context)`.  It saves
the caller's post-call `RSP` and return `RIP`, writes the saved frame through
the old-context pointer, loads the complete nonvolatile integer and SIMD
state, switches MXCSR and the x87 control word, writes the target GS base, and
jumps to the target RIP.  The switch's private scratch area is outside the
context record.  The pending switch plan is copied into scheduler-owned
storage before a C wrapper returns, so a plan cannot be overwritten by the
caller stack that is about to be switched away.

The saved-flags policy masks IF and DF (`RFLAGS & ~0x600`) and executes `CLD`
after restoration.  Interrupts remain disabled while this cooperative proof
owns the CPU; no preemption is implied.  FS is deliberately unchanged and is
an explicit future integration assumption.  GS is switched through the
available AMD64 `IA32_KERNEL_GS_BASE` MSR (`0xC0000101`).

All entry calls reserve the Windows x64 32-byte shadow space and maintain the
required stack alignment.  The context code is a freestanding assembly ABI,
not a Windows exception/unwind frame.  No exception or unwind operation is
allowed to cross a synthetic context transfer; invalid switch paths fail
closed.  The existing exception-dispatch machinery remains separate.

## Stack bootstrap and ownership

Every worker owns four contiguous pages.  Sixteen-byte alignment is applied at
the high end, with a 16-byte low canary and a 16-byte high canary.  The initial
`RSP` is the aligned high end minus one pointer-sized return slot, so a direct
jump observes the normal function-entry alignment.  That slot contains a
valid fail-closed synthetic return stub, not an arbitrary address.  The
startup assembly carries the entry function and argument in the initial
nonvolatile context, reserves shadow space, calls the entry normally, records
the return value, marks the TCB `Terminated`, drops its execution reference,
and transfers to the next runnable TCB.  It never returns into the bootstrap
slot during the normal path.

Canaries are checked before creation is accepted, after handle closure, after
resumption, before collection, and during the deliberate corruption negative
control.  Reclamation is refused if a canary is damaged.

## GS, TLS, FLS, and last error

The scheduler saves the boot GS base before installing its synthetic GS area.
Every TCB receives its own GS page, TEB-like page, 512-entry TLS vector, and
TLS block.  GS offsets `0x30` and `0x58` contain the TEB-like base and TLS
vector; vector entry zero points to the TLS block.  A TEB-like identity field
is also populated.  The proof performs direct `GS` reads and writes at the
same TLS offset, not merely an indexed global emulation.

FLS is a fixed 64-entry per-TCB table.  Last-error is a per-TCB 32-bit value.
Context transfer restores GS first and the proof then verifies that the active
GS base, TEB-like base, TLS vector/block, TLS value, FLS value, and last-error
all belong to the resumed TCB.  The original boot GS and flags are restored by
deterministic teardown.

## Synthetic objects and handles

The object registry is fixed-capacity and currently tags `Thread` and `Event`.
Handles are opaque 64-bit values:

```text
  [63:56] magic 0xA7 | [55:48] type | [47:16] generation | [15:0] slot+1
```

The generation is retained when a record is released.  Lookup validates the
magic, type, slot, generation, and live bit, rejecting invalid, stale, and
wrong-type handles without touching other objects.

A public handle reference is separate from the object internal reference and
the TCB execution reference.  Closing the last public worker handle therefore
does not reclaim a running or blocked worker.  A terminated worker is
reclaimed only when its execution reference is zero, public references are
zero, canaries are intact, and it is not the current TCB.  Teardown first
collects terminated workers and refuses to destroy events with registered
waiters.

## Event state machine

Events are unnamed and have a fixed waiter array.  Manual-reset signaling sets
the signaled bit and wakes every eligible waiter; the bit remains set until an
explicit reset.  Auto-reset signaling wakes one waiter and consumes the bit;
when no waiter exists, one signal token remains for a later successful wait.
Reset clears the bit in either mode.  Wait registration, wakeup, reset, close,
and destruction are bounded operations.  The event record remains live while a
waiter is registered, even if its public handle has been closed.

The current wait API accepts infinite waits.  It has no timeout implementation
yet; the scheduler's wait preparation/selection boundary is the reserved
timer-queue integration point.

## Deterministic Gate4 scenario

The proof registers boot/main `M`, creates auto-reset nonsignaled `A` and
manual-reset nonsignaled `B`, and creates worker `W` suspended with suspend
count one.  It verifies that W has not executed, resumes it, closes its public
handle immediately, and proves that the TCB, stack, and execution reference
remain live.  M blocks on B; W runs on its independent stack and GS state,
proves private TLS/FLS/last-error values, signals B, and blocks on A.  B wakes
M and remains signaled.  M resets B, signals A, and selects W.  W resumes after
the auto-reset wait, proves the signal was consumed, returns zero, and becomes
`Terminated`.  M observes termination through the TCB after the public handle
has already been closed, collects W, destroys A and B, tears down all object
records and pages, restores boot GS, and returns to a scheduler-neutral Gate4
state.

The proof also exercises main→worker, worker-blocked→main, and worker-resumed→
termination→main paths with distinct GPR, XMM6–XMM15, MXCSR, x87, RSP, GS,
TLS, FLS, last-error, and canary checks.  Negative controls cover invalid and
stale handles, wrong object types, double close, suspend misuse, TCB/event/
object exhaustion, canary corruption, blocked-worker reclamation, and event
destruction with a registered waiter.

## Reuse audit

The related guideXOS Server implementation was inspected without modifying it.
Concepts reused here are its bounded TCB/runnable selection model in
`kernel/core/process.cpp`, per-thread entry/argument bootstrap through
nonvolatile context slots and a startup wrapper in
`kernel/arch/amd64/context_switch.cpp`, and explicit wait-queue completion and
wake ownership from `runtime/synchronization/guidexos_scheduler_wait.*` and
`guidexos_event_baremetal.*`.  Its public thread contract also informed the
separation between a value-like public handle and execution state.

The Server AMD64 switch was not copied.  This foundation strengthens it with
RSI/RDI preservation, XMM6–XMM15, MXCSR, x87 control, flags/direction policy,
actual GS switching, generation-checked fixed object records, bounded stacks
and canaries, independent TLS/FLS/last-error state, and deterministic lifetime
rules.  Server scheduler code and files remain untouched.

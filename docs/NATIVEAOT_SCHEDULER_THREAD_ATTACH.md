# NativeAOT scheduler-thread callback bridge

Status: passed for the bounded contract in this milestone.

The guideXOS scheduler can now call the already-initialized `ManagedCallback`
export from a non-main scheduler thread. No NativeAOT process-entry replay,
main-thread state copy, FLS copy, or process-global managed-thread singleton is
used.

The follow-on managed allocation/GC proof is documented in
[NATIVEAOT_GC_SCHEDULER_THREAD.md](NATIVEAOT_GC_SCHEDULER_THREAD.md).

## Proven lifecycle

The final QEMU harness performs this sequence:

1. NativeAOT process initialization once on scheduler identity 1.
2. Main callbacks with inputs `41` and `99`, returning
   `0x0001002A` and `0x00020064`.
3. A naturally assigned scheduler thread (identity 5 in this harness because
   the existing durability probe has already used identities 3 and 4) starts
   with fresh GS/TEB/TLS/FLS/COM state.
4. The thread calls `ManagedCallback(7)` and returns
   `0x00030008`.
5. It blocks on the canonical scheduler event path, the main thread signals
   it, and it resumes with the same worker-local FLS state.
6. The same thread calls `ManagedCallback(8)` and returns
   `0x00040009`.
7. The thread terminates, its handle and stack VM registration are reclaimed,
   and its TCB/environment fields are cleared.
8. A second fresh scheduler thread (identity 6 in the representative boot)
   starts unattached, independently enters `ManagedCallback(9)`, and returns
   `0x0005000A`.

The managed callback counter therefore progresses `1, 2, 3, 4, 5`, while the
process-initialization counter remains exactly `1`.

## What the generated thunk does

The authoritative payload is the callback payload from the previous
milestone:

| Item | Value |
| --- | --- |
| Payload SHA-256 | `72F5CD40EE698B6BCCF6D67AEAB1BA570A2CE6B49B083B447AF067AA6F1EE9FA` |
| Payload size | `729600` bytes |
| `ManagedCallback` export RVA | `0x24724` |
| Representative relocated address | `0x000000000549F724` |

The stripped payload was inspected directly. The export thunk at RVA
`0x24724` calls the transition helper at `0x337E0`, executes the managed body,
then calls the return helper at `0x33940`.

The transition helper:

- reads the TLS vector through `GS:[0x58]`;
- loads the payload TLS slot selected by the runtime TLS index at payload RVA
  `0xB3E54`;
- uses the TLS block at vector-entry `+0x30`;
- saves the prior thread-state value from TLS block `+0x78`;
- examines the runtime flags at TLS block `+0x70`; and
- enters the runtime thread-store/attach path at `0x33840` when the fresh
  thread has not yet been recognized.

The attach path coordinates through the runtime state at payload addresses
`0xADB9D0` and `0xADB9D8`, calls the indirect runtime dispatch at IAT RVA
`0x7D440`, and reaches the runtime initialization helper at `0x37610`. The
missing-state/failure path formats a diagnostic through `0x3CDA0` and calls
the imported `RaiseFailFastException` at IAT RVA `0x7D0D8`.

The return helper at RVA `0x33940` restores the saved transition state to
TLS block `+0x78`. A fresh worker is `0` before its first callback; after the
generated transition it has the same `0xFFFFFFFFFFFFFFFF` attached/preemptive
sentinel observed on main and finalizer threads. A repeated callback on that
worker and a callback on a second fresh worker both follow the same path
without copying another TCB's state.

The payload also imports the platform FLS operations at IAT RVAs `0x7D168`
and `0x7D170`. The QEMU trace shows the fresh worker's runtime FLS slot 1
changing from zero to its own worker-local address, remaining stable over the
block/resume, and remaining distinct from main and finalizer FLS values.

## Thread-local state evidence

Representative exact-payload boot values were:

| State | Main | Finalizer | Callback worker before entry |
| --- | ---: | ---: | ---: |
| Scheduler identity | 1 | 2 | 5 |
| Scheduler state | Running (`3`) | Blocked (`4`) | Running (`3`) |
| Runtime FLS slot | `0x5479030` | `0x5469030` | `0` before thunk |
| TLS block | `0x5479000` | distinct | `0x5332000` |
| TLS `+0x78` | `0xFFFFFFFFFFFFFFFF` | `0xFFFFFFFFFFFFFFFF` | `0` before, sentinel after |
| COM | uninitialized | MTA | uninitialized |
| Scheduler last error | stable | stable | `0` before/after |

The callback worker's GS base, TEB base, TLS vector, TLS block, stack base,
and VM stack identity are all distinct from main. After callback return the
worker's FLS remains stable across the scheduler wait; after reclaim its FLS,
TLS pointers, stack VM registration, handle lookup, and TCB environment are
cleared. The finalizer wait record, finalizer FLS, COM MTA state, main FLS,
object counts, handle counts, reference counts, and two baseline VM regions
are restored.

The worker entry snapshot reports the expected `RSP mod 16` for its C frame;
the callback wrapper then establishes the Microsoft x64 call boundary and
32-byte shadow space. The callback address, ECX input, EAX result,
register-save path, and nonvolatile preservation are identical to the
main-thread callback. The
generated export has NativeAOT unwind metadata; no caller-side ABI variant is
needed for a scheduler thread.

## GuideXOS implementation boundary

No new guideXOS-side NativeAOT attach pointer or registration flag was added.
The existing scheduler TCB already owns the required independent GS/TEB/TLS/
FLS/COM/stack state. The generated reverse-P/Invoke thunk recognizes that
fresh environment and creates/activates its runtime-affine state through the
supported runtime machinery. This is the smallest truthful implementation:
guideXOS supplies a distinct thread environment, and NativeAOT owns the
managed transition state.

There is no separate exported NativeAOT detach API used by this payload. The
supported transition return restores the prior state/sentinel; guideXOS then
terminates the scheduler thread and frees its own environment and stack. The
opaque runtime thread-store internals are not spoofed or manually removed.
Full managed-thread lifecycle APIs, thread-pool behavior, allocation stress,
exception propagation, and arbitrary managed thread creation remain deferred.

## Validation

`tools\Run-NativeAotSchedulerCallbackFreshBoots.ps1` runs three independent
fresh QEMU boots and validates the complete marker order, exact callback
results, state transitions, FLS isolation, wait/VM restoration, and the
one-time process-initialization counter. The three final serial hashes were:

```text
9B3AF24D939EBC749FDEE107270DB4D4E9227F5C0B14E804B54BCF15EC068A1B
D3F99B309EF4E8C74197CFE9D179CDB2D8ED5BC4E1E1E730DE8FC0A007041B69
A50EB29905916DFDEA712FB161C4D13CCFDA3B16F202F73935B0BC1786A0B339
```

The focused scheduler durability host suite passes with explicit GS/TEB/TLS
environment isolation and cleared-reclaim assertions. The broader host
matrix passes for the callback bridge, scheduler/model/durability/stack VM,
Event/Wait, COM, CreateThread, ResumeThread, WriteFile, CRT, handles,
topology, NUMA, VM, memory accounting, system information, time/performance,
exception/context, VEH, multibyte, module loading, GetProcAddress, imports,
and standard handles.

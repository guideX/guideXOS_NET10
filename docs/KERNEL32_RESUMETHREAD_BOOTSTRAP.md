# `KERNEL32.dll!ResumeThread` bootstrap

Status: CLOSED for the exact NativeAOT payload call only.

This milestone routes only `KERNEL32.dll!ResumeThread`. It implements the
bounded one-level transition needed by the existing real NativeAOT worker:
`CreatedSuspended`, suspend count `1` to `Runnable`, suspend count `0`.
`ResumeThread` makes the worker eligible; it does not explicitly yield or
context-switch.

No route or implementation was added for `SuspendThread`, `CloseHandle`,
`SetEvent`, `ResetEvent`, any wait API, `SwitchToThread`, `Sleep`, `SleepEx`,
`CoInitializeEx`, FLS APIs, or any other unresolved payload import.

## Exact payload and import

The only real payload is:

`artifacts/veh-final3-normal-gate/ESP/GXOS/gxos-managed-entry-probe.dll`

Required SHA-256:

`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`

The build script and the real-payload validation script reject any other
payload hash.

| Fact | Value |
| --- | --- |
| DLL | `KERNEL32.dll` |
| Symbol | `ResumeThread` |
| Import descriptor | `2` |
| Symbol index | `0x31` / `49` |
| IAT RVA | `0x7D1C0` |
| Preferred IAT | `0x18007D1C0` |
| Caller RVA | `0x3CFCA` |
| Signature | `DWORD ResumeThread(HANDLE hThread)` |

The matcher is exact. The adapter consumes only `RCX` as `hThread`. RDX, R8,
and R9 are captured as incidental diagnostics and are not interpreted as
additional API arguments. The adapter returns a `uint32_t`, so the DWORD is
zero-extended in RAX; failure is `0xFFFFFFFF`.

## Handle validation

The incoming value is resolved through the existing generation-checked opaque
object registry. A successful call requires:

- a non-null handle with a valid magic, type, slot, and generation;
- a live object of type `Thread` with public and internal references;
- a live, non-reclaimed TCB whose object slot and generation match;
- a live execution reference; and
- a prepared, internally consistent worker context.

NULL, arbitrary integers, Event handles, the
`MemoryResourceNotification` handle, stale generations, closed or reclaimed
Thread handles, and corrupted TCBs fail with `0xFFFFFFFF`. A failed call does
not change suspend count, state, runnable-queue membership, execution count,
references, execution lifetime, priority, stack, or GS/TLS/FLS state.

The real payload handle is decoded dynamically. In all three enabled runs it
was object slot `4`, generation `1`, TCB slot `1`, internal identity `2`, and
relative priority `2`. The numeric handle is evidence, not a hardcoded route
value.

## Context validation and transition

Before changing the suspend count, the route revalidates the prepared worker:

- entry RVA is exactly `0x35320` and the entry lies in an executable payload
  section;
- the entry argument is the exact Event #1 handle;
- the initial RSP is inside the independently owned 16 KiB worker stack and
  has `RSP % 16 == 8` at thread entry;
- saved entry/RSP/argument and nonvolatile-register sentinels are intact;
- stack canaries, flags, MXCSR, x87 state, and GS base are valid;
- GS/TEB/TLS-vector/TLS-block relationships and stack bounds are valid;
- FLS allocation metadata and the per-thread environment are valid; and
- the worker has a live execution reference.

For the exact suspended worker the atomic logical transition is:

| Field | Before | After |
| --- | --- | --- |
| State | `CreatedSuspended` | `Runnable` |
| Suspend count | `1` | `0` |
| Runnable queue | absent | exactly one entry, position `0` |
| Return value | — | previous count `1` |
| Relative priority | `2` | `2` |
| Execution count | `0` | `0` |
| Public reference | `1` | `1` |
| Execution reference | live | live |

Queue insertion is idempotent and the scheduler tracks explicit queue
membership. `ResumeThread` itself does not call yield, context-switch, or
worker code. The main/boot TCB remains the current execution context during
the route, with identity `1` and unchanged GS base. Therefore “worker is
runnable” and “worker has executed” are deliberately different claims.

The current cooperative policy has no automatic dispatch at API return. The
worker remains queued while main continues normally, so no worker instruction
executes before the route returns. If a future scheduler policy dispatches at
that boundary, the route contains no special suppression; the first worker
instruction and next unresolved import must then be traced rather than
predicted.

## Repeated resume and failure controls

The scheduler foundation currently models one suspend level (`0` or `1`); it
does not claim arbitrary nested suspend compatibility. A second valid resume
when the count is already zero is a successful no-op returning previous count
`0`. It cannot underflow the count, enqueue a duplicate, reset the saved
context, create another object, or change references. An inconsistent
zero-count/runnable TCB that is not queued fails closed.

The focused `RESUME_THREAD_MODEL_TESTS` suite covers:

- successful resume and returned previous count `1`;
- one queue entry, preserved priority, execution lifetime, public reference,
  context, stack, canaries, GS, TLS, FLS, and zero execution count;
- repeat-resume return `0`, no underflow, and no duplicate queue entry;
- NULL, arbitrary, stale, wrong-generation, Event, notification, closed, and
  reclaimed handles;
- corrupted saved context and canary failure before state mutation; and
- deterministic teardown of a runnable-but-never-executed synthetic worker.

The suite passed `57` checks. The complete scheduler model suite passed `255`
checks, and the existing CreateThread and SetThreadPriority model suites
passed `134` and `149` checks respectively.

## Real-payload evidence

Three fresh enabled QEMU runs used the required hash and agreed on the
immediate transition:

| Observation | Result |
| --- | --- |
| Payload base | `0x000000000547B000` |
| Runtime ResumeThread IAT | `0x00000000054F81C0` |
| Runtime call site | `0x00000000054B7FCA` |
| Caller RVA | `0x000000000003CFCA` |
| Incoming Thread handle | dynamic `0xA701000000010005` in these runs |
| Object / generation | `4 / 1` |
| TCB slot / identity | `1 / 2` |
| Priority | `2` |
| State before / after | `CreatedSuspended / Runnable` |
| Suspend count before / after | `1 / 0` |
| Return value | `1` |
| Queue position / count | `0 / 1` |
| Execution count immediately after return | `0` |
| Public reference / execution reference | `1 / live` |
| Worker stack | `0x5472000`–`0x5476000`, canaries intact |
| Initial RSP | `0x5475FE8`, entry alignment `8` mod `16` |
| Entry / argument | RVA `0x35320` / exact Event #1 handle |
| Worker GS / TEB / TLS vector / TLS block | `0x5471000` / `0x546E000` / `0x5470000` / `0x546F000` |
| FLS | 64 slots, valid storage |
| Current identity before / after | `1 / 1` |
| Current GS before / after | `0x5478000 / 0x5478000` |

All three enabled runs had one invocation, one success, zero failures, zero
worker execution, one runnable entry, and zero blocked threads. The scheduler
did not naturally dispatch the worker before the main thread reached its next
blocker.

## Natural continuation and next blocker

After `ResumeThread` returned, normal main-thread execution continued. The
first unresolved import actually encountered was:

```text
DLL:          KERNEL32.dll
Symbol:       IsProcessInJob
Descriptor:   2
Symbol index: 0x4B / 75
IAT RVA:      0x7D290
Runtime IAT:  0x00000000054F8290
Call site:    0x00000000054BE28B
Caller RVA:   0x000000000004328B
RCX:          0xFFFFFFFFFFFFFFFF
RDX:          0x0000000000000000
R8:           0x0000000000141620
R9:           0x0000000000000000
Stack arg 5:  0x0000000000000047 (run 1; 0x5 in runs 2 and 3)
Stack arg 6:  0x0000000000142C00 (run 1; 0x0000000180078339 in runs 2 and 3)
```

The blocker was encountered by the main scheduler thread, identity `1`, not
by the worker. At the stop, main was `Running`, worker was `Runnable`, the
runnable count was `1`, blocked count was `0`, the worker public handle
reference was `1`, execution reference was live, and worker execution count
was `0`. Main's active GS base was `0x5478000`; its stack bounds were
`0x7E64000`–`0x7F64000`.

No `IsProcessInJob` route was added. The worker did not reach its first
instruction at RVA `0x35320`, and no worker helper or FLS/COM dependency was
implemented speculatively.

## Disabled-route control

`ResumeThreadDisabled` enables all preceding Event,
MemoryResourceNotification, CreateThread, and SetThreadPriority routes while
omitting only the ResumeThread target. Its real-payload run returns to the
unresolved exact import at descriptor `2`, symbol index `0x31`, IAT RVA
`0x7D1C0`, caller RVA `0x3CFCA`, with the dynamic Thread handle in RCX. The
pre-blocker state remains two Event objects, one LowMemory notification, one
CreatedSuspended worker, suspend count `1`, priority `2`, execution count `0`,
and zero runnable entries.

## Regression evidence

- Three-run synthetic cooperative scheduler QEMU proof: passed.
- Three-run CreateThread payload regression: passed; next blocker remained
  SetThreadPriority.
- Three-run SetThreadPriority payload regression: passed; next blocker was
  ResumeThread with the worker still suspended.
- Three-run event/MemoryResourceNotification regression: passed.
- `_register_onexit_function`, bounded `malloc`, GetModuleHandleExW,
  GetProcAddress, environment, exception-context, and vectored-exception host
  suites: passed.

The synthetic scheduler continues to preserve full AMD64 nonvolatile context,
XMM6-XMM15, MXCSR, x87, GS switching, TLS/FLS/last-error isolation, stack
ownership/canaries, block/wake, event modes, separated public and execution
lifetime, termination, teardown, and negative controls.

## Explicit non-claims

This is not a general Windows thread implementation. It does not provide
nested suspend counts, SuspendThread, CloseHandle, waits, signaling/reset,
preemption, SMP scheduling, priority-sensitive selection, worker execution
guarantees, COM initialization, FLS APIs, or any dependency after the exact
`IsProcessInJob` blocker.

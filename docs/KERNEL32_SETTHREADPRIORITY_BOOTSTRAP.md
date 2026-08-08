# KERNEL32.dll!SetThreadPriority bootstrap

Status: CLOSED for the exact NativeAOT payload call only.

This milestone adds one payload route:

`KERNEL32.dll!SetThreadPriority`

No route or implementation was added for `ResumeThread`, `SuspendThread`,
`CloseHandle`, events, waits, yielding, sleeping, or any other unresolved
payload import. The real payload worker is not resumed or executed.

## Exact import

The required payload is:

`artifacts\veh-final3-normal-gate\ESP\GXOS\gxos-managed-entry-probe.dll`

Its required SHA-256 is:

`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`

The route requires all of the following identity facts:

| Fact | Value |
| --- | --- |
| DLL | `KERNEL32.dll` |
| Symbol | `SetThreadPriority` |
| Import descriptor | `2` |
| Symbol index | `0x2F` |
| IAT RVA | `0x7D1B0` |
| Preferred IAT | `0x18007D1B0` |
| Caller RVA | `0x3CFC1` |

The matcher is exact. A similarly named function is not routed.

## Contract

The payload observes the Microsoft x64 arguments:

```text
RCX = hThread
RDX = 2
R8  = 0
R9  = 0
```

The only supported relative priority is signed value `2`, corresponding to
`THREAD_PRIORITY_HIGHEST` for this observed call. The implementation returns
TRUE only after accepting that exact value and updating a genuine Thread
object. All other values fail closed and do not mutate the target TCB.

The TCB field is:

`GXOS_SCHEDULER_TCB.relative_priority` (`int32_t`)

It is explicitly initialized to `0` (`GXOS_SCHEDULER_DEFAULT_RELATIVE_PRIORITY`,
the milestone's default relative priority) when the boot and created-suspended
TCBs are initialized. The field is not overloaded onto suspend counts,
execution references, or another scheduler field.

## Handle validation

The route resolves the incoming value through the existing generation-checked
object registry. It requires a live object whose type is Thread, a nonzero
public and internal reference, a live TCB and execution reference, matching
object slot and generation metadata, and a non-terminated TCB. NULL, arbitrary,
stale-generation, wrong-type, closed, and reclaimed handles fail without
changing scheduler state.

The returned payload handle is decoded dynamically in the proof. In the three
fresh enabled runs it had object slot `4`, generation `1`, TCB slot `1`, and
internal identity `2`; the numeric opaque handle is evidence, not a hardcoded
route value.

## State transition

For the exact payload call, the recorded transition is:

```text
relative priority: 0 -> 2
return value:      FALSE -> TRUE (route result)
state:             CreatedSuspended -> CreatedSuspended
suspend count:     1 -> 1
execution count:   0 -> 0
runnable:          false -> false
```

The update is metadata-only. It does not dispatch, switch context, decrement
the suspend count, change the execution lifetime, run the worker, or alter its
stack, GS/TLS/FLS state, or canaries. The proven 16 KiB worker stack and
per-thread GS/TLS/FLS ownership remain intact.

The cooperative selector retains its established round-robin behavior. It may
inspect the retained field in a future contract, but this milestone does not
claim priority-sensitive selection among multiple runnable threads, complete
Windows thread-priority scheduling semantics, preemptive scheduling, or a
process-priority-class model. Because this exact call occurs while the worker
is suspended, no execution ordering is observable from the retained priority.

## Controls

The focused model suite covers successful Thread + priority `2`, retained
metadata, suspended/non-runnable/no-execution state, NULL and arbitrary
handles, stale-generation and reclaimed Thread handles, Event and
MemoryResourceNotification handles, unsupported values `0`, `1`, `-1`, `-2`,
`15`, `-15`, and an arbitrary invalid value, plus failure preservation of the
previous priority, suspend count, runnable state, execution lifetime, stack and
per-thread environment, and unrelated objects.

The build scenario `SetThreadPriorityDisabled` enables the established Event,
MemoryResourceNotification, and CreateThread routes but omits only the
SetThreadPriority target. Its real-payload control stops at the original
unresolved SetThreadPriority import after two Event objects, one nonsignaled
LowMemory notification, and one CreatedSuspended worker.

## Payload evidence

Three fresh enabled QEMU executions using the required hash agreed on:

| Observation | Result |
| --- | --- |
| Payload base | `0x000000000547B000` |
| Runtime SetThreadPriority IAT | `0x00000000054F81B0` |
| Runtime caller | `0x00000000054B7FC1` |
| Caller RVA | `0x000000000003CFC1` |
| Handle type / slot / generation | Thread / `4` / `1` |
| TCB slot / identity | `1` / `2` |
| Requested priority | raw `RDX=2`, signed `2` |
| Stored priority | `0 -> 2` |
| Return | TRUE |
| Worker state | `CreatedSuspended` before and after |
| Suspend count | `1` before and after |
| Execution count | `0` |
| Runnable | `false` |
| Stack | independent 16 KiB, canaries intact |
| GS/TLS/FLS | independent ownership present; 64 FLS slots |

All three then stopped at the first next unresolved import, without routing
it:

```text
DLL:          KERNEL32.dll
Symbol:       ResumeThread
Descriptor:   2
Symbol index: 0x31
IAT RVA:      0x7D1C0
Runtime IAT:  0x00000000054F81C0
Call site:    0x00000000054B7FCA
Caller RVA:   0x000000000003CFCA
RCX:          0xA701000000010005 (the dynamic Thread handle)
RDX:          0x00000000000003F8
R8:           0
R9:           0
Stack arg 5:  0x0000000000000004
Stack arg 6:  0
```

The nonzero raw RDX and bounded stack value belong to the unresolved
ResumeThread call and are recorded as observed; no ResumeThread contract is
inferred from them.

The enabled blocker summary retained two live Thread objects (boot plus
worker), two live Event objects, one live notification object, five live object
records, four public handles, zero runnable threads, and zero blocked threads.

The disabled control stopped at SetThreadPriority with descriptor `2`, symbol
index `0x2F`, IAT RVA `0x7D1B0`, caller RVA `0x3CFC1`, the dynamic Thread
handle in RCX, priority `2` in RDX, and zero R8/R9. It retained state
`CreatedSuspended`, suspend count `1`, execution count `0`, and zero runnable
threads.

This evidence distinguishes “priority value retained” from “complete Windows
thread priority scheduling semantics.” Only the former is established.

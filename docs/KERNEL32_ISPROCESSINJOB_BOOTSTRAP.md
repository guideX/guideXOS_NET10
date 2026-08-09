# `KERNEL32.dll!IsProcessInJob` bootstrap contract

Status: CLOSED only for the exact current-process/NULL-job call exercised by
the required NativeAOT payload. This milestone does not implement Windows job
objects, job creation, assignment, lookup, quotas, containment, or any other
unresolved payload import.

Exact payload:

```text
artifacts\veh-final3-normal-gate\ESP\GXOS\gxos-managed-entry-probe.dll
SHA-256 = 2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837
```

## Supported contract

The only newly routed import is `KERNEL32.dll!IsProcessInJob`:

```c
BOOL IsProcessInJob(HANDLE ProcessHandle, HANDLE JobHandle, PBOOL Result);
```

The bridge honors the Microsoft x64 ABI: `RCX` is `ProcessHandle`, `RDX` is
`JobHandle`, `R8` is `Result`, and the BOOL result is returned in `EAX`.

The supported input is exactly:

```text
ProcessHandle = (HANDLE)-1  // existing GetCurrentProcess pseudo-handle
JobHandle     = NULL
Result        = non-null, canonical, four-byte writable payload memory
```

The current guideXOS process model is not contained in a Windows-style job.
The route performs one bounded four-byte write of `FALSE` (`0`) and returns
`TRUE` (`1`). It does not fabricate a job, containment object, quota object,
associated job handle, or synthetic handle. The `Result == TRUE` caller path
remains unsupported and unexercised.

The process token is checked against the existing payload-facing
`GetCurrentProcess` representation. `NULL`, arbitrary integers, thread,
event, notification, unsupported process-like, and stale typed handles fail.
Only a `NULL` `JobHandle` is accepted; every non-null job handle fails. A null,
non-canonical, read-only, truncated, wrapping, or otherwise unapproved
`Result` range fails before any read or write. Failures return `FALSE`, do not
modify `Result`, and do not change scheduler state or the object registry. No
new last-error route was added.

The focused model suite covers the success return, exact four-byte write and
unchanged surrounding bytes, all process/job/result rejection cases, range
wraparound, and scheduler/object/worker invariants: 34 checks, zero failures.
The worker remains Runnable with suspend count `0`, relative priority `2`,
execution count `0`, and runnable queue count `1`.

## Register diagnostic correction

The old unresolved-import diagnostic pushed its register block into the stack
area subsequently used by the Microsoft-ABI C handler. Handler stack traffic
could overwrite the saved third argument, serializing the incorrect
`R8 = 0x141620`. The common fail-fast bridge now creates a real Microsoft x64
shadow-space call frame and keeps captures in a stable frame below it. This is
a shared diagnostic fix, not an `IsProcessInJob` special case.

The bounded four-register control captures:

```text
RCX = 0xFFFFFFFFFFFFFFFF
RDX = 0x0000000000000000
R8  = 0x0000000007E64AC0
R9  = 0x0000000000000005
```

The enabled payload trace reports the live `R8 = 0x0000000007E64AC0`, rather
than the old serialized marker.

## Enabled payload proof

Three fresh QEMU executions used the same verified payload and agreed on:

```text
payload base             = 0x000000000547B000
runtime IsProcessInJob IAT = 0x00000000054F8290
payload call site        = 0x00000000054BE28B
caller RVA               = 0x000000000004328B
RCX / ProcessHandle     = 0xFFFFFFFFFFFFFFFF / current-process pseudo-handle
RDX / JobHandle         = 0x0000000000000000 / NULL
R8 / Result              = 0x0000000007E64AC0
Result writable range   = 0x0000000007E64000..0x0000000007F64000
Result before/write/after = 0 / 4 bytes / 0
BOOL return              = 1 / TRUE
caller branch            = SUCCESS_RESULT_FALSE_FALLBACK
main                     = identity 1, Running (3)
worker                   = identity 2, Runnable (2), priority 2, suspend 0
                           runnable 1, execution count 0
queue / blocked          = 1 / 0
live objects / handles   = 5 / 4
```

The caller control flow was not patched. It tested the successful BOOL first,
then compared the written FALSE result and took the same fallback branch as
the Windows oracle.

## Disabled route and next boundary

With only `IsProcessInJob` disabled, the exact payload stops at
`KERNEL32.dll!IsProcessInJob` (descriptor `0x2`, symbol index `0x4B`, IAT RVA
`0x7D290`). The disabled run reports the corrected live `R8` pointer, performs
no result write, and preserves main Running, worker Runnable, priority `2`,
suspend `0`, execution count `0`, runnable count `1`, blocked count `0`, five
live objects, and four public handles. `ResumeThread` and all preceding routes
remain enabled.

With the route enabled, execution continues naturally to the first unresolved
dependency, encountered by the main thread:

```text
KERNEL32.dll!GlobalMemoryStatusEx
descriptor       = 0x2
symbol index     = 0x44
IAT RVA          = 0x7D258
runtime IAT      = 0x00000000054F8258
runtime call     = 0x00000000054BE361
caller RVA       = 0x0000000000043361
RCX              = 0x0000000007E64AD0
RDX              = 0x00000000000003F8
R8               = 0x0000000000000001
R9               = 0x0000000000000000
```

The bounded stack captures there were `(arg5,arg6) =
(`5`, `0x180078339`) in all three final runs; these incidental values are
diagnostics only. At
the boundary main is identity `1`/Running, the worker is identity `2`/Runnable
with priority `2`, suspend `0`, execution count `0`, runnable count `1`, and
blocked count `0`; live objects/handles are `5`/`4`.

The existing isolated `QueryInformationJobObject` compatibility route is
preserved solely to reach this established path; no new query route or
job-object model was added. `GlobalMemoryStatusEx` is intentionally not
implemented in this milestone. See the [dependency census](DEPENDENCY_CENSUS.md)
and [QueryInformationJobObject contract](KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md)
for the preserved prior boundary.

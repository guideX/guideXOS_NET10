# `KERNEL32.dll!QueryInformationJobObject` bootstrap contract

This task implements only the Microsoft x64 `QueryInformationJobObject` platform contract required by the current NativeAOT startup path.

It closes the one live CPU-rate query reached after the two `GetProcessAffinityMask` callers. It does not implement a general job-object subsystem, nested jobs, job creation/assignment, process-handle support, CPU throttling, GC initialization, allocation, SMP, or managed-thread registration.

## Boundary and live call

The preceding immutable `GetProcessAffinityMask` closure proved two live affinity calls. The processor-count caller then invokes `KERNEL32.dll!QueryInformationJobObject`; the checked query route returns the documented no-associated-job failure for `hJob = NULL` and the caller takes its bounded fallback. The next authentic import after that caller is `KERNEL32.dll!GetModuleHandleW`.

Static disassembly found two direct references to the query IAT slot:

| Static site | Class | Buffer size | Reachability in this startup |
| --- | ---: | ---: | --- |
| `0x18003CCA1` | `0x0F` | `0x08` | Live; `NativeAOT_processor_count_setup` |
| `0x1800432BD` | `0x09` | `0x90` | Dormant; not reached before the bounded next boundary |

The second reference is retained as a static fact, not treated as a second live call. Each of the three positive QEMU runs records exactly one query call, one result, one caller-consumption marker, and the `GetModuleHandleW` boundary.

The live preferred image facts are:

```text
KERNEL32.dll import descriptor index = 2
QueryInformationJobObject IAT RVA   = 0x0007D1F0
Preferred IAT address                = 0x18007D1F0
Static call site                      = 0x18003CCA1
Static return address                 = 0x18003CCA7
Caller start                          = 0x18003CBE0
Runtime call site (positive run)      = 0x00000000054B7CA1
Runtime return address                = 0x00000000054B7CA7
Runtime IAT                            = 0x00000000054F81F0
```

The loader routes only the exact import pair `KERNEL32.dll!QueryInformationJobObject`. All other imports retain their unique guideXOS-owned fail-fast stubs.

## Microsoft contract and exact ABI

Microsoft documents the signature as:

```c
BOOL QueryInformationJobObject(
    HANDLE hJob,
    JOBOBJECTINFOCLASS JobObjectInformationClass,
    LPVOID lpJobObjectInformation,
    DWORD cbJobObjectInformationLength,
    LPDWORD lpReturnLength
);
```

The x64 call observed in the payload is:

```text
RCX  hJob                         = 0
EDX  JobObjectInformationClass   = 0x0000000F
R8   lpJobObjectInformation       = 0x0000000007E64CD0
R9D  cbJobObjectInformationLength = 8
[entry RSP + 0x28] lpReturnLength = 0
EAX  BOOL result
```

The fifth argument is a stack argument under the Microsoft x64 ABI. The naked loader shim records the entry stack pointer before changing control flow and proves:

```text
ENTRY_RSP                    = 0x0000000007E64C78
FIFTH_ARGUMENT_STACK_ADDRESS = 0x0000000007E64CA0
FIFTH_ARGUMENT_RELATION     = ENTRY_RSP_PLUS_0x28
FIFTH_ARGUMENT_STACK_VALUE  = 0
```

The wrapper does not use a SysV register permutation, does not add a C prologue before capturing the fifth slot, and does not reinterpret the fifth argument as an output pointer when it is null.

## Information class and structure

The live class is Microsoft `JobObjectCpuRateControlInformation`, numeric value `15`. Its exact fixed-size structure is eight bytes:

```c
typedef struct _JOBOBJECT_CPU_RATE_CONTROL_INFORMATION {
    DWORD ControlFlags;     /* offset 0, 4 bytes */
    union {
        DWORD CpuRate;      /* offset 4, 4 bytes */
        DWORD Weight;
        struct {
            WORD MinRate;   /* offset 4, 2 bytes */
            WORD MaxRate;   /* offset 6, 2 bytes */
        } MinMaxRate;
    } DUMMYUNIONNAME;
} JOBOBJECT_CPU_RATE_CONTROL_INFORMATION;
```

The checked header asserts the Microsoft x64 widths, offsets, alignment, and `sizeof == 8`. A deliberate wrong-layout compile is part of the negative pipeline and must fail before any runtime test.

The documented control flags are represented exactly: enable `0x1`, weight-based `0x2`, hard-cap `0x4`, notification `0x8`, and min/max-rate `0x10`. The live contract does not claim enforcement or dynamic rate changes; it only publishes the fixed query structure for the explicitly modeled guideXOS facts.

## Handle, class, pointer, and publication policy

The implementation follows the documented `hJob = NULL` interpretation: query the calling process's associated job. The guideXOS fact snapshot proves no associated job, so the live call returns `FALSE` with `ERROR_ACCESS_DENIED` (`5`), leaves both optional output regions untouched, and does not attempt `GetLastError` in the NativeAOT caller. A non-null handle is rejected by the checked route; no fabricated job handle or access-rights claim is introduced. Microsoft documents that a real job handle requires `JOB_OBJECT_QUERY` access, but this pass does not create or open one.

Before publication, the freestanding core validates:

1. exact class `15` and exact minimum buffer size `8`;
2. canonical output and optional return-length pointers;
3. complete non-wrapping ranges under the existing guideXOS memory map;
4. writable output and return-length regions;
5. non-overlap of output and return-length regions;
6. valid guideXOS job facts, flag combinations, and percentage ranges.

Failures publish nothing. A successful query publishes the complete eight-byte structure with one local copy, and writes `*lpReturnLength = 8` only when the optional pointer is supplied. Oversized buffers are accepted because the documented `cb` parameter is a byte count and the fixed structure occupies the first eight bytes; undersized buffers are rejected with `ERROR_INSUFFICIENT_BUFFER` (`122`).

The wrapper maps checked failures to bounded errors (`5`, `6`, `87`, `122`, or `998`) and preserves the caller's last error on success. The live no-job failure changes the sentinel from `0xCB` to `5`; the synthetic success experiments preserve `0xCB`.

## Live trace and caller consumption

The three positive runs consistently record:

```text
hJob                         = 0
class                        = 0x0F / JobObjectCpuRateControlInformation
output length                = 8
lpReturnLength               = NULL
output before                = 0x00000000FFFF0FF0
output after                 = 0x00000000FFFF0FF0
BOOL                         = 0
last error                   = 0xCB -> 5
output written               = 0
return length written        = 0
processor count              = 1 -> 1
association                  = NONE
caller branch                = FAILURE_NO_ASSOCIATED_JOB_FALLBACK
query call count             = 1
success count                = 0
expected no-job failure      = 1
next boundary                = KERNEL32.dll!GetModuleHandleW
```

The caller tests `EAX` immediately. On failure it does not read the eight-byte output, does not call `GetLastError`, and takes its processor-count fallback. On success the static consumer reads `ControlFlags` at offset `0`, then reads `CpuRate` at offset `4` for control flags `0x5`, or `MaxRate` at offset `6` for the `0x11` branch. Both paths converge to the processor-count calculation. This field-consumption behavior is recorded separately from the live no-job result.

## Host reference and experiments

The Windows host reference uses the real API, not the guideXOS implementation. It reports:

```text
IsProcessInJob(NULL)                = TRUE, in_job = 0
NULL-handle CPU-rate query          = FALSE
NULL-handle last error              = 5
NULL-handle return length           = unchanged 0xA5A5A5A5
NULL-handle output                  = unchanged CCCCCCCCCCCCCCCC
empty real job query                = TRUE
empty real job return length        = 8
empty real job output               = 0000000000000000
empty real job last error           = preserved 0xA5A5A5A5
```

The captured host output is under `artifacts\query-information-job-object-host-reference-20260801`. The focused freestanding suite passes the exact ABI probe, full-width handles, layout guards, no-mutation failures, canonical/range/writable checks, alias rejection, unsupported classes, invalid flags/rates, synthetic no-limit success, hard-cap publication, min/max publication, and weight publication.

Two bounded reachability experiments exercise the success-only consumer without changing the positive contract:

| Evidence | Modeled facts | Result |
| --- | --- | --- |
| `evidence\generated\queryjobobject-success-experiment-20260801` | Associated job, no active CPU-rate flags | `TRUE`, complete zero structure, last error preserved, processor count `1 -> 1`, next `GetModuleHandleW` |
| `evidence\generated\queryjobobject-active-experiment-20260801` | Associated job, hard cap `0x5`, `CpuRate=5000` | `TRUE`, fields `ControlFlags=0x5`/`CpuRate=5000`, last error preserved, processor count `1 -> 1`, next `GetModuleHandleW` |

These are explicitly synthetic fact experiments. They do not establish that the firmware has a real Windows job object or that guideXOS enforces CPU limits.

## Immutable evidence

The positive closure is `evidence\generated\queryjobobject-final-20260801`, built from `artifacts\queryjobobject-dev`. The bounded validator passed all three runs, including artifact/source hashes, unique run IDs/PIDs/serial hashes, complete cleanup, serial size under `524288`, import census, prior startup invariants, exact ABI markers, output immutability, caller branch, and boundary ordering.

| Run | QEMU PID | Serial bytes | Serial SHA-256 |
| --- | ---: | ---: | --- |
| `queryjobobject-final-20260801-run1` | `9780` | `245966` | `9CEA1BDD6BE7CEB9B2790215A9E13427DA437956A5E4E7556D1580FE6556CBEA` |
| `queryjobobject-final-20260801-run2` | `19916` | `245966` | `965D41B52F16A763BF6A8F730C08BAFEF13E0EF4F2313638A537DE1BC8CFFB3E` |
| `queryjobobject-final-20260801-run3` | `8916` | `245966` | `0FAAB17A50030EA5986967EE9E0CE06DBFB37DEF743C7B942BB89F4F17456E79` |

All three use the same artifact fingerprint and QEMU `11.0.0 (v11.0.0-12122-ga4bb4b10c9)`. The disabled control is `evidence\generated\queryjobobject-disabled-20260801`; it retains the query fail-fast boundary with `33 / 91 / 0` functional/fail-fast/unresolved imports. The enabled closure is `34 / 90 / 0`.

The evidence-pipeline controls are run by `tools\Test-QueryInformationJobObjectEvidencePipeline.ps1` and passed wrong-layout, marker-mutation, truncation, stale-run-ID, duplicate-PID, and artifact-hash-mismatch rejection. Existing focused regressions for affinity, process-group, NUMA, system information, environment lookup, `strlen`, `_stricmp`, `strcmp`, `_initterm`, and `_initterm_e` also pass.

## Stopping point

This closure adds one exact import route and one bounded job-information class. The next authentic dependency is `KERNEL32.dll!GetModuleHandleW`. No job subsystem, process/job handle rights, CPU-rate enforcement, allocator, GC heap, managed-thread registration, or general Windows compatibility is implied. No commit or push was performed.

## Follow-on `GetModuleHandleW` boundary

The subsequent fresh baseline confirms that the query closure's next import is `KERNEL32.dll!GetModuleHandleW` at descriptor `0x2`, IAT RVA `0x7d130`. Its first live caller passes `&L"ntdll.dll"`; because no ntdll PE is mapped, the follow-on contract returns `NULL`/`ERROR_MOD_NOT_FOUND` and advances only to `KERNEL32.dll!GetProcAddress`. The job-object contract remains unchanged and all no-job, output-preservation, processor-count, and zero-allocation invariants are retained. See [KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md](KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md).

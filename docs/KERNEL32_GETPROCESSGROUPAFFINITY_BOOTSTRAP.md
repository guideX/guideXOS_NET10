# `KERNEL32.dll!GetProcessGroupAffinity` bootstrap contract

This pass implements only the Microsoft x64 `GetProcessGroupAffinity` platform contract required by the current NativeAOT startup path. It does not implement processor-topology discovery, process-affinity mutation, NUMA allocation, SMP scheduling, or GC initialization.

## Authoritative contract

Microsoft documents [`GetProcessGroupAffinity`](https://learn.microsoft.com/en-us/windows/win32/api/processtopologyapi/nf-processtopologyapi-getprocessgroupaffinity) as:

```text
BOOL GetProcessGroupAffinity(
    HANDLE    hProcess,
    PUSHORT   GroupCount,
    PUSHORT   GroupArray
);
```

The documented behavior used here is:

| Contract item | Required behavior | Checked implementation |
| --- | --- | --- |
| `hProcess` | process handle with the documented query permission | only the current-process pseudo-handle is accepted in this freestanding substrate |
| `GroupCount` | input capacity and output number of group elements | readable and writable two-byte `USHORT`; capacity is read before the result is produced |
| `GroupArray` | caller-owned array of group numbers | `USHORT` elements are written only when capacity is sufficient |
| insufficient capacity | return `FALSE`, set required count, set `ERROR_INSUFFICIENT_BUFFER` | capacity `0`, required count `1`, null array, output count becomes `1`, BOOL `0`, last error `122` |
| sufficient capacity | return `TRUE` and publish group numbers | exact Group 0 policy; trailing caller storage is untouched |
| return type | BOOL, zero/nonzero | 32-bit signed integer representation, `0` or `1` |

The official Windows data-type widths are summarized by Microsoft's [Windows data types](https://learn.microsoft.com/en-us/windows/win32/winprog/windows-data-types): `BOOL` is a 32-bit `int`, `USHORT` is 16 bits, and a Microsoft x64 `HANDLE`/pointer is 64 bits. The implementation uses explicit `int32_t`, `uint16_t`, and `uintptr_t` types with compile-time width assertions.

## Observed NativeAOT caller

The unchanged NativeAOT payload is `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`. Its final PE import table places `GetProcessGroupAffinity` at IAT RVA `0x7d2a0`, preferred address `0x18007d2a0`, and the direct call at preferred `0x1800436da`. The caller function begins at preferred `0x180043650`.

The live call sequence is:

```text
GetCurrentProcess() -> RCX = 0xffffffffffffffff
BX = 0; [rsp+0x60] = 0       // GroupCount capacity
R8 = 0                       // GroupArray = NULL
RDX = rsp+0x60               // GroupCount
call [IAT+0x7d2a0]
test EAX, EAX
if zero: GetLastError(); compare 0x7a; read [rsp+0x60]
```

There is one call in this startup path. The caller consumes the required count after `ERROR_INSUFFICIENT_BUFFER`, does not read a group array, performs no retry, and then reaches the next authentic dependency, `KERNEL32.dll!GetProcessAffinityMask`. That later API is outside this milestone.

## Narrow guideXOS policy

The current QEMU harness has one bootstrap processor and no processor-group discovery service. The contract therefore publishes an explicit snapshot:

| Fact | Value |
| --- | ---: |
| usable processors | `1` |
| active processor mask | `1` |
| group count | `1` |
| group number | `0` |
| topology policy | `FACT_SNAPSHOT` / `SINGLE_GROUP_ZERO` |

This is sufficient to answer the observed capacity probe. It is not a claim that all Windows systems have one group, that groups are generally contiguous, or that the loader supports more than the checked synthetic host vectors.

## Checked implementation

The allocation-free core is in [`src/Gate4Harness/platform_process_group_affinity.c`](../src/Gate4Harness/platform_process_group_affinity.c), with the ABI and layout contract in [`platform_process_group_affinity.h`](../src/Gate4Harness/platform_process_group_affinity.h). The loader wrapper is in `src/Gate4Harness/gate4_loader.c` and routes only the exact `KERNEL32.dll!GetProcessGroupAffinity` pair when `GXOS_ENABLE_PROCESS_GROUP_AFFINITY` is enabled.

Before the output count is written, the core validates:

- the exact current-process pseudo-handle;
- a canonical, readable, writable two-byte `GroupCount` range;
- the explicit topology snapshot, processor count, active-mask population, and group-number policy;
- insufficient-capacity semantics without touching or validating `GroupArray`;
- canonical, writable, overflow-safe array storage when capacity is sufficient;
- exact `USHORT` writes and no partial array publication on rejected calls;
- the bounded canonical memory-region context.

The core has no allocation, recursion, external CRT/runtime references, or last-error side effects. The wrapper maps the checked status to the observed Win32 last-error result; a successful call preserves the preexisting last error, while the live insufficient-capacity call changes `0xcb` to `0x7a`.

Focused host tests cover widths, Microsoft x64 RCX/RDX/R8/RAX routing, zero-capacity and null-array probing, exact/excess buffers, trailing poison preservation, synthetic multi-group insufficiency, invalid handles, null/noncanonical/read-only/undersized pointers, topology contradictions, no-mutation-on-failure, and external-reference absence.

## Runtime evidence

The final positive artifact set is `artifacts\getprocessgroup-final3-20260801`; its immutable evidence is `evidence\generated\getprocessgroup-final3-20260801-immutable-v4`.

| Artifact | SHA-256 |
| --- | --- |
| EFI loader | `4EA2A456A8175D06DB73E3346DA0744E498A680BA0255AAB8B30AD1CF8F4994F` |
| NativeAOT payload | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| runtime archive | `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` |
| OVMF code | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` |
| QEMU executable | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |

Three fresh positive runs passed the immutable validator. Each used an independent QEMU PID, the same artifact fingerprint, complete cleanup, `229,967` serial bytes, and the same terminal boundary:

| Run | QEMU PID | Serial SHA-256 |
| --- | ---: | --- |
| `...-run1` | `6032` | `8218C7C687D3A6C753FD31904A3611DAA4A80C4D8870892A969A1E617B36E519` |
| `...-run2` | `16084` | `4B2AEBB491AD5B332222A7A141754457CA30EF7EB886563F2B85D499793C483D` |
| `...-run3` | `14692` | `93BF1D713E2D0E49387F89C6B23691BD74AB82D3BD0CAD6E103BE6464F245B89` |

The positive trace records image base `0x547b000`, relocated IAT `0x54f82a0`, relocated call site `0x54be6da`, caller start `0x54be650`, handle `0xffffffffffffffff`, count pointer `0x7e64c80`, null array, capacity `0`, required/output count `1`, count readable/writable `1/1`, groups written `0`, BOOL `0`, status `INSUFFICIENT_BUFFER`, last error `0xcb -> 0x7a`, caller count-read `1`, array-read `0`, retry `0`, and no subsequent group API call.

The same runs retain `32 / 92 / 0` functional/fail-fast/unresolved imports, `_stricmp` census `0x375` calls and successes with zero failures, census hash `0x9E89C714CD4695E6`, QPC count `2`, zero QPC regressions, and zero TLS allocation, managed-thread, GC-heap, allocation-context, and managed-allocation markers.

The disabled routing control is `evidence\generated\getprocessgroup-disabled-final-20260801-control-v2`. Its fresh run passed with `31 / 93 / 0`, emitted no process-group wrapper marker, and stopped at the original `KERNEL32.dll!GetProcessGroupAffinity` fail-fast boundary. The evidence validator negative-control suite passed marker mutation, truncation, stale identity, duplicate PID, artifact-hash, capacity-result, and last-error mutations. The marker-mutation build also compiles successfully.

## Boundary and conclusion

This closure stops at `KERNEL32.dll!GetProcessAffinityMask`. No process-affinity mask API, processor-group companion API, NUMA API, allocation, GC heap, thread registration, or general topology support is added or inferred. No commit or push was performed.

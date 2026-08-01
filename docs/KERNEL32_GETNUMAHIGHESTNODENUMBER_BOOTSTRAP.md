# `KERNEL32.dll!GetNumaHighestNodeNumber` bootstrap contract

This task implements only the Microsoft x64 `GetNumaHighestNodeNumber` platform contract required by the current NativeAOT startup path. It does not claim general NUMA discovery, SMP support, node-targeted allocation, scheduler locality, virtual-memory management, GC initialization, or general Windows compatibility.

## Starting point and boundary

This pass began after the committed `GetSystemInfo` milestone:

- branch: `main`;
- HEAD: `14d865eeb19e97a627824671104cf377cdda5bb9` (`Implement GetSystemInfo Contract`);
- upstream: `origin/main`;
- initial worktree: clean;
- fresh baseline: `evidence\generated\getnumahighest-baseline-20260801`.

The fresh baseline reproduced the exact `KERNEL32.dll!GetNumaHighestNodeNumber` import after the bounded `GetSystemInfo` route. It used a fresh QEMU process (PID `23940`), emitted `2,115,119` serial bytes, and preserved the prior `GetSystemInfo` facts and zero allocation/GC state. The baseline is retained; it was not overwritten by the enabled artifact.

The imported symbol is the exact pair `KERNEL32.dll!GetNumaHighestNodeNumber`, at payload IAT RVA `0x7e298` and preferred IAT address `0x18007e298`. The static direct call is preferred `0x1800437dd`; the caller passes `lea rcx,[rsp+0x60]`, so RCX points to a four-byte `ULONG` output slot. The static return address is `0x1800437e3`. In the final relocated QEMU image, the wrapper observed runtime call site `0x54bc7dd`, return address `0x54bc7e3`, and output pointer `0x7e64c80`.

## Microsoft contract

Microsoft documents [`GetNumaHighestNodeNumber`](https://learn.microsoft.com/en-us/windows/win32/api/systemtopologyapi/nf-systemtopologyapi-getnumahighestnodenumber) as a `BOOL` function in `Kernel32.dll` with one `[out] PULONG` argument. A nonzero return indicates success and a zero return indicates failure; the documented failure action is to call `GetLastError`. The API returns the highest NUMA node number, which is not itself a promise that the nodes are contiguous or that the value is a total node count.

The implementation fixes the Microsoft x64 ABI types explicitly:

| Contract item | Required representation | Checked implementation |
| --- | --- | --- |
| return value | `BOOL`, signed 32-bit integer | `int32_t`, `1` for success and `0` for failure |
| output value | `ULONG`, unsigned 32-bit integer | `uint32_t`, exactly four bytes |
| first integer/pointer argument | RCX | `PULONG` output pointer in RCX |
| return register | RAX | ABI probe observes `RAX=1` for success and `RAX=0` for failure |
| output publication | caller-owned memory | one four-byte store only after all checks pass |

The width and return-register statements follow Microsoft's [Windows data types](https://learn.microsoft.com/en-us/windows/win32/winprog/windows-data-types), [Windows coding conventions](https://learn.microsoft.com/en-us/windows/win32/learnwin32/windows-coding-conventions), and [x64 software/calling conventions](https://learn.microsoft.com/en-us/cpp/build/x64-software-conventions?view=msvc-170). The implementation does not assume that every successful call changes last error: Microsoft's [`GetLastError`](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-getlasterror) guidance says callers may only rely on last-error changes where the called API documents them.

## Current guideXOS topology policy

The current startup harness has one QEMU vCPU (`-m 128M` and no `-smp` option), initializes only the bootstrap processor, and has no ACPI SRAT/locality parser, node-targeted allocator, or scheduler distinction between local and remote memory. The prior `GetSystemInfo` contract observed `dwNumberOfProcessors=1`, `dwActiveProcessorMask=1`, page size `0x1000`, and AMD64. Therefore this contract publishes an explicit, narrow policy snapshot:

| Fact | Value | Meaning |
| --- | ---: | --- |
| usable processors | `1` | one proven bootstrap processor |
| `SYSTEM_INFO.dwNumberOfProcessors` | `1` | must agree with the snapshot |
| `SYSTEM_INFO.dwActiveProcessorMask` | `1` | one active bit, not general SMP discovery |
| locality-domain count | `1` | one policy domain, not a parsed Windows NUMA topology |
| highest node number | `0` | the only valid highest node for the one-domain policy |
| node-targeted allocation | false | no node-aware allocation API exists in this substrate |
| topology policy | `FACT_SNAPSHOT` | facts are an explicit loader snapshot, not guessed discovery |

The value `0` means the highest node number is zero. The caller separately treats a successful zero output as its one-domain/non-NUMA fallback. It does not interpret zero as zero nodes. When the caller receives a successful nonzero output, its observed code derives a count as `highest_node + 1`; that is caller behavior for this branch, not a general statement that all Windows NUMA nodes are contiguous.

## Checked implementation

The contract core is in [`src/Gate4Harness/platform_numa.c`](../src/Gate4Harness/platform_numa.c) and [`src/Gate4Harness/platform_numa.h`](../src/Gate4Harness/platform_numa.h). The exact wrapper is in `src/Gate4Harness/gate4_loader.c` and is routed only when `GXOS_ENABLE_NUMA_HIGHEST_NODE` is enabled and both the module and symbol match exactly.

Before the output store, the core validates:

- non-null, canonical x64 output pointer;
- a complete writable four-byte range, including range-overflow and guard checks;
- the explicit `FACT_SNAPSHOT` policy;
- processor count, active-mask population, and agreement with the `GetSystemInfo` snapshot;
- at least one locality domain;
- `highest_node_number < locality_domain_count` and the single-domain rule `highest_node_number == 0`;
- overflow safety for any caller-derived `highest + 1` interpretation;
- the bounded memory-context description.

Only after validation does the checked core write the four-byte highest-node value. It leaves the facts unchanged, preserves the output on all rejected inputs, and has no external CRT/runtime references. Focused host vectors cover exact widths, ABI routing, repeated and separate outputs, zero/one/two/three-domain facts, null/noncanonical/read-only/undersized/overflow destinations, contradictory snapshots, unsupported policy, and failure output preservation.

The loader wrapper emits the exact call index, static/runtime call sites, return address, RCX destination, width/alignment, approved writable range, output before/after, topology facts, status, Boolean result, and last-error before/after. The caller trace records whether it read the output, which fallback branch it selected, any derived domain count, and that no later NUMA API call occurred.

## Runtime evidence

The final immutable positive artifact is `artifacts\getnumahighest-final-v2-20260801`. Its manifest is retained under `evidence\generated\getnumahighest-final-20260801-immutable-v2\artifact-manifest.json`.

| Artifact | SHA-256 |
| --- | --- |
| EFI loader | `8A83363EAA6CB4167E2BF7898310C229BD48FFF9E5E6CAA6F6C2753B3BFBF230` |
| NativeAOT payload | `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` |
| runtime archive | `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` |
| OVMF code | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` |
| QEMU executable | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |
| validation runner | `786E2B86ECA7E070E08B7903080AE26C9C2F97D278E0822DE88A5C28F370A149` |
| evidence validator | `7197D27CCF6D2BB8518B14C301C564C97A19759219B8EF5FEF72B02356BAC191` |
| NUMA C source | `0AAAE636C6843ED3CA5AE4B76DD8338E5B36891C735280F56B642907256B27FD` |
| NUMA header | `01C635F61D48132700C90FC9EF1408A0CB573E4BB7B36E0EF06B3A6B859A0D16` |

Three fresh positive runs passed the immutable validator and reached the next authentic boundary, `KERNEL32.dll!GetProcessGroupAffinity`:

| Run | QEMU PID | Serial bytes | Exit | Cleanup |
| --- | ---: | ---: | ---: | --- |
| `...-run1` | `22788` | `2,117,419` | `0` | complete |
| `...-run2` | `13324` | `2,117,419` | `0` | complete |
| `...-run3` | `20836` | `2,117,419` | `0` | complete |

The bounded runner stopped each guest after the terminal no-progress condition had been reached; it preserved complete serial evidence and performed cleanup. Each run recorded `31 / 93 / 0` functional/fail-fast/unresolved imports, `_stricmp=0x375`, QPC count `2`, zero QPC regressions, and zero allocation-context, managed-thread, GC-heap, or managed-allocation markers.

The live positive wrapper facts were: output pointer `0x7e64c80`, writable region `0x7e64000..0x7f64000`, writable range `0xff380`, width `4`, alignment `0`, output before/after `0`, topology `1` processor / `1` domain / highest `0`, status `OK`, Boolean `1`, and last error preserved from `0xcb` to `0xcb`. The wrapper read the output. The caller selected `SUCCESS_BOOLEAN_OUTPUT_ZERO_NON_NUMA_FALLBACK`, recorded caller output-read `1`, derived domain count `0`, applied no output transform, and made zero subsequent NUMA calls.

## Routing, controls, and conclusion

The disabled final control is `evidence\generated\getnumahighest-disabled-final-20260801`; it passed with `30 / 94 / 0` imports, emitted no NUMA wrapper marker, and retained the original `KERNEL32.dll!GetNumaHighestNodeNumber` fail-fast boundary. The success experiment is `evidence\generated\getnumahighest-success-final-20260801` (PID `18304`); it returned `BOOL=1`, `STATUS=OK`, output `0`, preserved last error `0xcb`, and selected the zero-output fallback. The controlled failure experiment is `evidence\generated\getnumahighest-failure-final-20260801` (PID `16884`); it forced the checked `UNSUPPORTED_TOPOLOGY` status, returned `BOOL=0`, preserved output `0`, changed last error from `0xcb` to `0x32` for that controlled failure, and selected `FAILURE_NON_NUMA_FALLBACK` without reading the output. The forced error is a harness experiment; it is not a claim that Microsoft assigns that exact error to every real topology failure.

The evidence-pipeline suite passed eleven negative controls: marker mutation, truncation, stale run identity, duplicate PID, artifact-hash mismatch, highest-node/count confusion, zero-node confusion, success without an output write, failure with a claimed output write, wrong output width, and unexpected last error. All prior focused host suites and the existing `GetSystemInfo`, `_stricmp`, environment, `strlen`, `strcmp`, `_initterm`, and `_initterm_e` regression suites passed, including no-external-reference checks where provided.

The next authentic dependency is `KERNEL32.dll!GetProcessGroupAffinity`, not another NUMA API. No first allocation, GC heap, managed-thread registration, node-targeted allocation, or general processor-topology support was added or inferred. No commit or push was performed.

## Follow-on `GetProcessGroupAffinity` (2026-08-01)

The one-domain successful zero result reaches the exact process-group capacity probe documented in [KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md](KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md). That follow-on returns required count `1` for zero capacity and the caller takes its required-count branch without retry or group-array consumption. This NUMA closure remains unchanged; `GetProcessAffinityMask` is the next boundary after the process-group contract.

## Follow-on process affinity (2026-08-01)

The process-group closure is followed by the separately scoped [`GetProcessAffinityMask`](KERNEL32_GETPROCESSAFFINITYMASK_BOOTSTRAP.md) contract. It reuses the same one-processor facts and returns process/system masks `0x1`/`0x1`; this does not change the NUMA result, add topology discovery, or establish allocation/GC readiness. The next boundary after the two affinity callers is `KERNEL32.dll!QueryInformationJobObject`.

The query-information follow-on is recorded in [KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md](KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md). It remains outside NUMA scope and only proves the class-15 no-associated-job fallback before `GetModuleHandleW`.

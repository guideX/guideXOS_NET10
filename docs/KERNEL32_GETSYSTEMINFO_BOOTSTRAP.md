# `KERNEL32.dll!GetSystemInfo` bootstrap contract

This pass implements only the Microsoft x64 `GetSystemInfo` platform contract required by the current NativeAOT startup path. It does not claim GC initialization, virtual-memory allocation, processor discovery, or general Windows compatibility.

## Authoritative contract

Microsoft documents `GetSystemInfo` as a `VOID` function in `Kernel32.dll` that fills an output `SYSTEM_INFO` structure. The structure is the exact x64 layout below; the union is four bytes and the complete structure is `0x30` bytes with eight-byte alignment.

| Offset | Field | Width | Implemented value/policy |
| ---: | --- | ---: | --- |
| `0x00` | `wProcessorArchitecture` / `dwOemId` union | 4 | `AMD64 = 9`; reserved word zero |
| `0x04` | `dwPageSize` | 4 | `4096`, the loader's `EFI_PAGE_SIZE` |
| `0x08` | `lpMinimumApplicationAddress` | 8 | loaded image base |
| `0x10` | `lpMaximumApplicationAddress` | 8 | last byte of the loaded image allocation |
| `0x18` | `dwActiveProcessorMask` | 8 | `1`, bootstrap processor only |
| `0x20` | `dwNumberOfProcessors` | 4 | `1` |
| `0x24` | `dwProcessorType` | 4 | `PROCESSOR_AMD_X8664 = 8664` |
| `0x28` | `dwAllocationGranularity` | 4 | `4096`, matching the only proven loader allocation unit |
| `0x2C` | `wProcessorLevel` | 2 | `0`; no CPUID processor-level contract is present |
| `0x2E` | `wProcessorRevision` | 2 | `0`; no revision contract is present |

The Microsoft references are [`GetSystemInfo`](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getsysteminfo), [`SYSTEM_INFO`](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-system_info), and [`GetNativeSystemInfo`](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getnativesysteminfo). The last API is intentionally not aliased: this is a native x64 image, so only the exact imported `GetSystemInfo` symbol is in scope.

## Observed NativeAOT consumer

Static import census for the committed `_stricmp` payload found `KERNEL32.dll!GetSystemInfo` at IAT RVA `0x7e260` (preferred IAT address `0x18007e260`) and the direct call at preferred `0x18004379f`. The caller passes `lea rcx,[rsp+0x20]`. Its post-call reads consume only:

- offset `0x04`: `dwPageSize`;
- offset `0x20`: `dwNumberOfProcessors`;
- offset `0x28`: `dwAllocationGranularity`.

The static field-consumption mask is `0xA2` under the documented bit mapping: page size, processor count, and allocation granularity. The live QEMU call returned to `0x00000000054BC7A5` from call site `0x00000000054BC79F`, with destination `0x0000000007E64C40`, a writable aligned stack address.

The minimum/maximum address policy is deliberately image-backed. The loader already knows the exact UEFI page allocation containing the relocated PE image, so it publishes that loaded image range and does not invent a Windows process-wide address-space boundary. The stack is approved as writable destination memory only; it is not advertised as application address range. Allocation granularity is not a claim that a general Windows virtual allocator exists: it is the current loader's `4096`-byte page unit.

## Checked implementation

The allocation-free core is in `src/Gate4Harness/platform_system_info.c` and its ABI/layout header is `src/Gate4Harness/platform_system_info.h`. The wrapper is routed only when `GXOS_ENABLE_SYSTEM_INFO` is enabled and the import module/symbol pair is exactly `KERNEL32.dll!GetSystemInfo`.

Before writing, the core validates:

- non-null, canonical, eight-byte-aligned destination pointers and the complete `0x30`-byte range;
- containment of that range in an approved writable loader region;
- AMD64 architecture, power-of-two page size, and a page-divisible power-of-two allocation granularity;
- one-to-64 processor count, nonzero active mask, and mask population matching the count;
- canonical ordered application addresses and the explicit image-backed range policy;
- bounded, canonical memory-region context.

Only after all checks pass does it zero the complete structure and assign every field, including the reserved architecture word. The checked core has no external references. Host tests cover complete initialization from poison, guard preservation, repeated destinations, malformed facts, null/noncanonical/read-only/undersized/overflow destinations, invalid memory contexts, and the Microsoft ABI wrapper. The intentional wrong-layout compile control fails at the `_Static_assert`.

## Runtime evidence

The positive immutable artifact set is `artifacts/getsysteminfo-final-20260731`; its three-run evidence is `evidence/generated/getsysteminfo-final-20260731-immutable-v3`. All three runs passed the validator with the same loader/payload/runtime/firmware/QEMU/source fingerprints, unique PIDs, clean cleanup, and the next authentic boundary `KERNEL32.dll!GetNumaHighestNodeNumber`.

| Run | PID | Serial bytes | Result |
| --- | ---: | ---: | --- |
| `getsysteminfo-final-20260731-immutable-v3-run1` | 3164 | 2,115,119 | `GetSystemInfo` complete; next boundary |
| `getsysteminfo-final-20260731-immutable-v3-run2` | 26008 | 2,115,119 | `GetSystemInfo` complete; next boundary |
| `getsysteminfo-final-20260731-immutable-v3-run3` | 26176 | 2,115,119 | `GetSystemInfo` complete; next boundary |

The positive run records `30 / 94 / 0` functional/fail-fast/unresolved imports, `0x375` successful `_stricmp` calls, `QPC_COUNT=2`, zero QPC regressions, zero allocation context, zero managed-thread registration, zero GC heap usability, and zero managed allocations. The complete marker sequence is `GETSYSTEMINFO_BEGIN` → validated status zero → all fields → `GETSYSTEMINFO_RETURNED` → `GETSYSTEMINFO_OK` → field-consumption complete → `GetNumaHighestNodeNumber` boundary.

The disabled immutable control is `evidence/generated/getsysteminfo-disabled-20260731-immutable-v2`. Its three runs retain `29 / 95 / 0`, preserve the `_stricmp` count and startup summaries, prove the original RCX destination in `GETSYSTEMINFO_FAILFAST_RCX`, and stop at `KERNEL32.dll!GetSystemInfo` without any checked implementation marker. The marker-mutation control is `evidence/generated/getsysteminfo-marker-mutation-20260731`; it reaches the same authentic next boundary while emitting `GETSYSTEMINFO_OX` and no positive `GETSYSTEMINFO_OK`, so the evidence validator rejects marker substitution rather than treating it as success.

No commit or push was performed for this pass.

## Follow-on `GetNumaHighestNodeNumber` consumer (2026-08-01)

The `GetSystemInfo` consumer census is now followed by the separate exact [`GetNumaHighestNodeNumber`](KERNEL32_GETNUMAHIGHESTNODENUMBER_BOOTSTRAP.md) contract. The payload imports that symbol at IAT RVA `0x7e298` and calls it at preferred `0x1800437dd`; the caller passes `rsp+0x60` as a four-byte `ULONG` output pointer, tests the returned `BOOL`, and reads the output only after success.

The current `GetSystemInfo` snapshot is intentionally sufficient only for a one-domain policy: processor count `1`, active mask `1`, domain count `1`, highest node `0`, and no node-targeted allocation support. A successful zero output therefore selects the caller's non-NUMA fallback. A nonzero output would be converted by the caller to `highest + 1` for its node-table setup; this is not a claim about general Windows node contiguity.

The new wrapper records the exact output range and last-error behavior while preserving the `GetSystemInfo` field-consumption marker. Positive QEMU runs advance to `KERNEL32.dll!GetProcessGroupAffinity`; the disabled route retains the original NUMA fail-fast boundary. This follow-on does not broaden the `GetSystemInfo` contract into processor-topology discovery, NUMA allocation, SMP support, or GC readiness.

## Follow-on process-group consumer (2026-08-01)

The same one-processor snapshot is reused by the separately scoped [`GetProcessGroupAffinity`](KERNEL32_GETPROCESSGROUPAFFINITY_BOOTSTRAP.md) contract. The process-group caller passes a zero-capacity `USHORT` and null array, so the snapshot's one group produces `ERROR_INSUFFICIENT_BUFFER` and required count `1`. No additional `SYSTEM_INFO` fields, processor-topology APIs, or allocation state are inferred from that reuse.

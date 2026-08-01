# `KERNEL32.dll!GetProcessAffinityMask` bootstrap contract

This task implements only the Microsoft x64 `GetProcessAffinityMask` platform contract required by the current NativeAOT startup path. It does not implement arbitrary process handles, process security, affinity mutation, processor-group scheduling, SMP, AP startup, topology enumeration, GC initialization, or managed allocation.

## Repository boundary

The task began on branch `main`, at committed HEAD `72d8f8b7f3c0501a3895563e2de4c57f8eef6b91`, tracking `origin/main`, with a clean worktree. The preceding `GetProcessGroupAffinity` milestone was committed at that HEAD. No commit or push was performed for this task. The fresh pre-change boundary is preserved under `evidence\generated\getprocessaffinity-baseline-20260801-fresh`.

## Import and caller identity

The unchanged NativeAOT image imports `GetProcessAffinityMask` from `KERNEL32.dll` at descriptor index `2`. Its IAT slot is RVA `0x7d208`, preferred address `0x18007d208`, and the final enabled image relocates that slot to `0x54f8208` at image base `0x547b000`.

Two live calls were found and both are routed. The bounded startup chain is:

```text
ManagedMain
  -> NativeAOT processor bitmap setup at preferred 0x180043650
     -> GetCurrentProcess
     -> GetProcessAffinityMask at 0x180043793
     -> process-mask bit tests and processor-bitmap updates
  -> NativeAOT processor-count setup at preferred 0x18003cbe0
     -> GetProcessAffinityMask at 0x18003cc55
     -> process-mask population count
     -> QueryInformationJobObject
```

The first call's runtime call site is `0x54be793`, return address `0x54be799`, and caller start `0x54be650`. The second call's runtime call site is `0x54b7c55`, return address `0x54b7c5b`, and caller start `0x54b7be0`. The first call is the processor-bitmap helper; the second is the processor-count helper. The next authentic dependency after the second successful call is `KERNEL32.dll!QueryInformationJobObject`.

For the first immutable run, NativeAOT supplied `RCX=0xffffffffffffffff`, `RDX=0x7e64c80`, and `R8=0x7e64c88`. The second call supplied `RCX=0xffffffffffffffff`, `RDX=0x7e64ce0`, and `R8=0x7e64ce8`. Both output pointers are distinct, eight-byte aligned stack destinations inside a readable/writable `0x7e64000`–`0x7f64000` region. The complete trace records pointer regions, permissions, writable ranges, before values, after values, and output widths for every call.

The only observed handle is the full 64-bit current-process pseudo-handle `0xffffffffffffffff`, produced by the existing `GetCurrentProcess` route. No process object, handle table, duplication, access-token, security, or external-process policy was added.

## Caller consumption

The first caller tests `EAX` immediately. On success it reads only the process mask with an eight-byte load from `[rsp+0x60]`, tests its bits, and updates a processor bitmap. It does not read the system mask, intersect the masks, count bits, derive a processor count, call `GetLastError`, or update GC state. Its failure branch returns a local fallback without reading either output.

The second caller also tests `EAX` immediately and reads only the process mask with an eight-byte load. It uses a manual `value & (value - 1)` loop to count set bits, uses `64` as the zero-mask fallback, then calls `QueryInformationJobObject` to apply a later job-related bound. It does not read the system mask or call `GetLastError`. The controlled forced-failure experiment shows the second caller uses a derived fallback of `1` without reading either output.

Therefore the system mask is a required valid API output but is not consumed by either currently live caller. The returned masks are not a GC heap count. No GC contract, heap, allocation context, managed thread, or managed allocation became usable.

## Microsoft contract and x64 ABI

Microsoft documents the API as:

```c
BOOL GetProcessAffinityMask(
    HANDLE     hProcess,
    PDWORD_PTR lpProcessAffinityMask,
    PDWORD_PTR lpSystemAffinityMask
);
```

The authoritative contract is in the [Microsoft `GetProcessAffinityMask` documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getprocessaffinitymask). The API returns nonzero on success and publishes both masks. On failure it returns zero; output values are undefined and callers should call `GetLastError`. The process mask is a processor bit vector allowed for the process; the system mask is the configured/active processor bit vector for the relevant group, and the process mask is a subset of the system mask. The documented handle rights are `PROCESS_QUERY_INFORMATION` or `PROCESS_QUERY_LIMITED_INFORMATION`, but this bootstrap route intentionally supports only the current-process pseudo-handle.

The [Microsoft Windows data-type definitions](https://learn.microsoft.com/en-us/windows/win32/winprog/windows-data-types) establish `BOOL` as a four-byte integer, `HANDLE` as a pointer-sized value, and `DWORD_PTR` as `ULONG_PTR`. On Microsoft x64 the checked contract proves:

```text
sizeof(BOOL)      = 4
sizeof(HANDLE)    = 8
sizeof(DWORD_PTR) = 8

RCX = hProcess
RDX = lpProcessAffinityMask
R8  = lpSystemAffinityMask
EAX = BOOL result
```

The [processor-group documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/processor-groups) explains the single-group interpretation and the limitations of these legacy single-group APIs on systems with more than one processor group. Multi-group behavior remains explicitly out of scope.

## guideXOS facts and checked core

The authoritative snapshot is the existing `GetSystemInfo` fact set, sourced from the UEFI page and loaded-image memory map. It publishes one initialized and scheduler-usable bootstrap processor, `dwNumberOfProcessors=1`, and `dwActiveProcessorMask=0x1`. The existing `GetProcessGroupAffinity` snapshot publishes one group, Group 0. `GetNumaHighestNodeNumber` publishes node 0 and selects the non-NUMA fallback. The affinity route reuses those facts rather than introducing a second topology source.

The current truthful policy is:

```text
process affinity mask = 0x0000000000000001
system affinity mask  = 0x0000000000000001
```

The checked core is in [`platform_process_affinity.c`](../src/Gate4Harness/platform_process_affinity.c), with the ABI/layout contract in [`platform_process_affinity.h`](../src/Gate4Harness/platform_process_affinity.h). It validates, in order:

1. the exact current-process pseudo-handle;
2. both non-null output pointers;
3. canonical addresses and complete eight-byte writable ranges;
4. pointer-arithmetic overflow and destination aliasing;
5. nonzero masks, process-mask subset, and processor-population invariants;
6. one-group/Group-0 facts and `GetSystemInfo` count/mask consistency.

Only after all checks pass does it publish both eight-byte outputs. Checked failures perform no output write. The core allocates nothing, recurses nowhere, and has no external references. The thin loader wrapper maps invalid handles to error 6, unsupported topology to error 50, other exposed validation failures to error 87, and preserves the caller's last-error value on success. The live success trace shows `0xcb` before and after both calls; the caller never invokes `GetLastError`.

## Validation evidence

The focused host command is:

```text
tools\Run-PlatformProcessAffinityHostTests.ps1
```

It passes 57 tests. Coverage includes exact widths, Microsoft ABI register placement, 64-bit handle identity, separate eight-byte writes, guards, repeatability, aliasing, null/noncanonical/read-only/undersized/overflow pointers, zero and non-subset masks, fabricated processor bits, topology and snapshot mismatches, no mutation on failure, synthetic subset/full masks, and no external references in the freestanding object. The preceding process-group host suite also passes.

The disabled routing control is `evidence\generated\getprocessaffinity-disabled-20260801-control-v3`; it emits no affinity wrapper marker, keeps the process-group wrapper, and stops at `KERNEL32.dll!GetProcessAffinityMask` with `32 functional / 92 fail-fast / 0 unresolved`.

The forced-failure experiment is `evidence\generated\getprocessaffinity-failure-experiment-20260801-v3`. It records two `FALSE` returns, error 6 after each call, zero writes, unchanged eight-byte outputs, no caller output reads, the first bitmap fallback, the second processor-count fallback, and the same next `QueryInformationJobObject` boundary.

The immutable positive closure is `evidence\generated\getprocessaffinity-final-20260801-immutable-v2`. Three fresh QEMU processes used one artifact fingerprint and passed the bounded serial ceiling of 512 KiB. Each run produced 241,507 serial bytes, two affinity calls, process mask `0x1`, system mask `0x1`, caller system-mask reads `0`, and derived processor count `NOT_DERIVED` for the bitmap call and `0x1` for the processor-count call. The QEMU PIDs were `21460`, `8300`, and `17884`; serial hashes are `60EB5C387B9FC01B3E5CF4154FD79B4B7E39B6DEFB4618E9D9757D84E3751BED`, `74F95EDA1BD4B6B768F3C6423C511E53FD26F2CF16B8B59728341FEDF6E8A516`, and `1B6E842D3485F8DDAF5F4489C44D9C17A9CF72E6C14AFE2F9D0E19FCBC3322B8`. The validator also checks unique run IDs/PIDs, unique serial hashes, unchanged artifact hashes, cleanup completion, QPC regressions, import census, TLS/GC/allocation state, and the exact next boundary.

The execution-relevant artifact hashes are:

| Artifact | SHA-256 |
| --- | --- |
| EFI loader | `38C4806B86AEA003D3E48435E010FCC5E35AAFC58602C8DC495C64097222623D` |
| NativeAOT payload | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| Runtime archive | `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` |
| OVMF code | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` |
| QEMU | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |

The negative-control pipeline is [`Test-GetProcessAffinityMaskEvidencePipeline.ps1`](../tools/Test-GetProcessAffinityMaskEvidencePipeline.ps1). It rejects marker mutation, truncation, stale identity, duplicate PID, artifact hash mutation, process-mask mutation, system-mask mutation, caller system-mask-read mutation, last-error mutation, and output-width mutation. The marker-mutation build also compiles successfully.

## Result and stopping point

The only newly routed import is `KERNEL32.dll!GetProcessAffinityMask`. Prior time, performance, CRT, environment, `GetSystemInfo`, NUMA, and process-group contracts remain intact. No other affinity or topology API was aliased. The next milestone is `KERNEL32.dll!QueryInformationJobObject`; work stops here because it is outside this task's requested scope.

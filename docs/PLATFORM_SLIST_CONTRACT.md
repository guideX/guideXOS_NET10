# Windows x64 SLIST initialization contract

Status: the current NativeAOT startup path has a narrow, allocation-free `InitializeSListHead` implementation. This document covers header initialization only. It does not implement or claim general lock-free SLIST support, push/pop/flush/depth operations, scheduler integration, managed-thread registration, GC startup, or allocation.

## Why startup reaches the API

The allocation-enabled payload is the fresh differential artifact with SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. Its preferred image base is `0x180000000`. The current payload imports `InitializeSListHead` at IAT RVA `0x7e2f8` and `InterlockedFlushSList` at IAT RVA `0x7e2e8`.

The exact static path is:

```text
0x180077550  NativeAOT attach/bootstrap helper
  -> 0x180078350  lea rcx, [0x1800b5ed0]
                  tail-jump through KERNEL32.dll!InitializeSListHead
  -> 0x180078380  post-initialization static-state helper
  -> api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e
```

The nearest identifiable owner is the NativeAOT attach/bootstrap runtime static state. The linked artifact does not expose a higher-level list-owner symbol. The destination preferred address `0x1800b5ed0` is in the image's writable zero-filled static-data region; after relocation in the QEMU profile it was `0x552eed0`. It is not loader scratch, TLS, stack, or heap memory. The header is initialized once on the current path, after both CRT on-exit tables and before `_initterm_e`. No SLIST operation occurs before initialization. At the call, no allocation, synchronization, managed-thread registration, or GC initialization has begun, and the allocation context remains zero.

## Authoritative x64 representation

The installed Windows SDK `winnt.h` declares the x64 `SLIST_HEADER` as a 16-byte, 16-byte-aligned union. Its x64 view is:

```c
typedef union DECLSPEC_ALIGN(16) _SLIST_HEADER {
    struct { ULONGLONG Alignment; ULONGLONG Region; };
    struct {
        ULONGLONG Depth:16;
        ULONGLONG Sequence:48;
        ULONGLONG Reserved:4;
        ULONGLONG NextEntry:60;
    } HeaderX64;
} SLIST_HEADER;
```

The x64 header therefore has these contract properties:

| Property | Contract |
| --- | --- |
| `SLIST_HEADER` size | 16 bytes |
| Header alignment | 16 bytes on x64 |
| `SLIST_ENTRY` | next pointer, 16-byte aligned on x64 in the Windows SDK |
| Empty state | both 64-bit words are zero |
| Depth | zero in the empty state |
| Sequence | zero in the empty state; later list mutations change sequence state |
| Reserved / encoded next | zero in the empty state; the x64 next-entry field uses a 60-bit pointer encoding view with four low zero bits |
| Pointer assumptions | 64-bit x64 image and pointer-width fields |
| API return | `VOID`; Windows `InitializeSListHead` has no success return |

Microsoft documents that the head is 16-byte aligned on 64-bit systems and that unaligned list storage has unpredictable behavior: [InitializeSListHead (interlockedapi.h)](https://learn.microsoft.com/en-us/windows/win32/api/interlockedapi/nf-interlockedapi-initializeslisthead), [InitializeSListHead (wdm.h)](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/wdm/nf-wdm-initializeslisthead), and [SLIST_ENTRY](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-slist_entry). Microsoft’s NDIS analogue explicitly describes initialization as zero-initializing the opaque head and setting the first-entry pointer to null: [NdisInitializeSListHead](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ndis/nf-ndis-ndisinitializeslisthead).

All-zero initialization is therefore contractually correct for this x64 layout. The implementation writes the two named 64-bit union words through the declared structure, not through an unexplained byte count. Compile-time assertions enforce 16-byte size and alignment for both `GXOS_SLIST_HEADER` and `GXOS_SLIST_ENTRY`.

`CMPXCHG16B` is not required for this milestone. Windows documents the double-width atomic difficulty for x64 SLIST operations and the need for a 128-bit compare/exchange primitive for later synchronized mutations: [Interlocked singly linked lists](https://learn.microsoft.com/en-us/windows/win32/sync/interlocked-singly-linked-lists). Initialization is a single-threaded state establishment and does not perform push, pop, flush, or depth publication; no memory barrier or atomic RMW is added here. A future companion implementation would need to preserve the 16-byte alignment, encoded next-entry representation, sequence semantics, and the documented interlocked publication rules.

The Windows API is void and does not provide a portable invalid-input result. The guideXOS wrapper checks the pointer and 16-byte alignment before calling the internal helper; an invalid address fails closed through the existing bounded `fail()` diagnostic/halt path. The host contract helper returns `-1` only so deterministic unit tests can verify that validation occurred before any write. It is not exposed as the Windows import ABI.

## guideXOS contract and implementation

The internal contract is:

```c
int gxos_initialize_slist_head(GXOS_SLIST_HEADER *head);
```

It uses the x64 Microsoft ABI, accepts ownership of no storage, requires a non-null 16-byte-aligned writable `GXOS_SLIST_HEADER`, resets all 16 bytes to the documented empty state, performs no allocation, and calls no timing, firmware, GC, thread, scheduler, or synchronization service. It is safe before scheduler and managed-thread initialization because it is a bounded local write. It does not provide atomic publication and is initialization-only. The PE-facing wrapper is:

```c
void ms_abi InitializeSListHead(GXOS_SLIST_HEADER *head);
```

The import resolver preserves exact DLL/name matching and the existing functional/fail-fast accounting. With this mapping, the current import totals are 23 functional and 101 deterministic fail-fast, with `UNRESOLVED_REQUIRED_IMPORTS=0`. Disabling the mapping returns the same import to the original fail-fast boundary.

## Focused family census

| API | Classification in the current payload |
| --- | --- |
| `InitializeSListHead` | Imported and reached once; functional in the SLIST-enabled harness |
| `InterlockedFlushSList` | Imported at IAT RVA `0x7e2e8`, not reached; a later-runtime or shutdown-only helper is present at preferred `0x180079430` |
| `InterlockedPushEntrySList` | Absent from the payload import census |
| `InterlockedPopEntrySList` | Absent from the payload import census |
| `QueryDepthSList` | Absent from the payload import census |
| `RtlInitializeSListHead` | Absent from the payload import census; SDK declaration only |
| `RtlInterlockedPushEntrySList` | Absent from the payload import census; SDK declaration only |
| `RtlInterlockedPopEntrySList` | Absent from the payload import census; SDK declaration only |
| `RtlInterlockedFlushSList` | Absent from the payload import census; SDK declaration only |
| `RtlQueryDepthSList` | Absent from the payload import census; SDK declaration only |
| Direct compiler-generated atomic header operations | None identified on the current startup path |

No companion operation is implemented. The presence of the imported flush helper is not reachability evidence.

## Host tests and diagnostics

`tools/Run-SlistHostTests.ps1` runs the bounded vectors in `src/Gate4Harness/tests/platform_slist_tests.c`. They cover nonzero and opaque prior state, exact all-zero bytes, depth/sequence/reserved/next fields, repeated initialization, adjacent canaries, null, misalignment, structure assertions, no external core references, and the intentionally incorrect-layout compile control. The suite emits `SLIST_HOST_TESTS=PASSED`; it is single-threaded and makes no concurrency claim.

The QEMU wrapper emits bounded address/alignment data for at most the first eight calls, then emits `GXOS_NET10:SLIST_HEAD_INITIALIZED_OK` only after the complete header contract check. The current startup trace emits one call, one header address, alignment zero, and then reaches `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. No push/pop/flush/depth marker, allocation marker, GC-advanced marker, or managed-thread marker appears.

## Validation and next boundary

The pre-change fresh baseline was recorded in `artifacts\slist-baseline-20260729-091209-685`; it stopped at `KERNEL32.dll!InitializeSListHead` after the two CRT success markers, with 22 functional / 102 fail-fast imports and zero unresolved required imports. The final SLIST-enabled build is in `artifacts\slist-final-20260729-104138-993`; its loader SHA-256 is `67284C49FE561EB9E53B5990E58CAD1F76AB8348F7255AF05AFCC50BB7C34909` and the payload hash is unchanged above. Fresh QEMU logs and the final negative-control log paths are recorded in the evidence ledger addendum. The monitored positive run reached `_initterm_e`; the final-hash three-run completion gate remains explicitly open because host TCG stalls truncated the retained final-hash attempts before their full terminal summaries.

The deepest supported boundary for this pass is:

```text
PE load -> relocation -> TLS/GS/TEB/FLS -> NativeAOT entry
  -> FILETIME -> QPC/QPF -> two CRT on-exit tables
  -> InitializeSListHead: one valid 16-byte empty x64 header
  -> api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e: next blocker
```

The next milestone is the `_initterm_e` CRT startup contract. It must not be conflated with SLIST companions, GC startup, managed-thread registration, or first allocation.

## 2026-07-29 evidence-closure pass

Implementation, host vectors, and the disabled functional-routing control are complete; the final-hash three-run criterion is not. The immutable positive sequence `slist-initialize-final-20260729-195900-run1`/`run2`/`run3` used loader `333F110626390045D8E9DB5081A99D198BB84720F5519CDCB4FE3B74B3C2CE9C` and payload `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, but all three were rejected before complete summaries. Run 3 reached `_initterm_e` without the trailing QPC summary. A separate fresh final-artifact probe completed the exact marker sequence and summary; it is evidence of correctness, not a substitute for the three-run gate.

The harness now retains raw serial, stdout, stderr, lifecycle, PID, timestamps, timeout/kill reasons, file lengths, and artifact hashes. QEMU monitor diagnostics showed `VM status: paused (shutdown)`, `HLT=0`, and a reset-vector RIP while monitor queries continued, classifying the apparent stalls as guest/firmware shutdowns rather than PowerShell truncation or competing serial readers. The six truncated/missing-summary/stale/hash-mismatch/duplicate/marker-mutation controls all rejected their mutated evidence. The retained complete disabled control stops at `KERNEL32.dll!InitializeSListHead` with no SLIST success marker. No allocation, GC initialization, managed-thread registration, or general SLIST mutation is claimed.

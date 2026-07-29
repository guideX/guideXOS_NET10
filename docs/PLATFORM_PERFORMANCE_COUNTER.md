# NativeAOT platform performance-counter contract

Status: passed for the bounded `QueryPerformanceCounter` / `QueryPerformanceFrequency` milestone. The allocation-enabled NativeAOT startup path returns from FILETIME, obtains a monotonic normalized counter value, passes the minimal CRT on-exit initialization, and now stops at `KERNEL32.dll!InitializeSListHead`. No GC heap, managed allocation, or thread runtime is claimed.

## Exact imports and semantics

The allocation PE is unchanged: SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. Its two relevant IAT slots are:

| Import | IAT RVA | ABI and output | Failure behavior |
| --- | ---: | --- | --- |
| `KERNEL32.dll!QueryPerformanceCounter` | `0x7e0c8` | Microsoft x64 ABI; one writable `int64_t*`; writes normalized signed-64 counter units and returns `1` | Null output, uninitialized source, unavailable source, raw regression, ambiguous wrap, or checked overflow returns `0` |
| `KERNEL32.dll!QueryPerformanceFrequency` | `0x7e0d0` | Microsoft x64 ABI; one writable `int64_t*`; writes a positive stable frequency and returns `1` | Null output, uninitialized source, unavailable source, or signed-64 overflow returns `0` |

The implementation is allocation-free and freestanding. It does not call libc, a host OS, UEFI Boot Services, events, threads, or the NativeAOT runtime. The source and public contract are in `src/Gate4Harness/platform_performance.c` and `src/Gate4Harness/platform_performance.h`; the wrappers are explicitly `ms_abi` on x64.

QPC values are normalized as `raw - start_raw`, so the first result need not be zero because the startup consumer performs work after source initialization. Equality is valid. A regression is rejected and counted; the authoritative startup path records one authentic QPC call with zero regressions. QPF returns the source frequency in counter ticks per second.

## Source inventory and selection

| Candidate | Investigation result | Decision |
| --- | --- | --- |
| UEFI `Stall` / events | Delay and event services, not a readable monotonic counter; `Stall(1)` is useful only as a diagnostic perturbation | Not the QPC source |
| Invariant TSC + CPUID leaf `0x15` | Supported in code with exact checked ratio arithmetic. Default QEMU reports max basic leaf `0xD`, invariant-TSC bit `0`, and leaf-15 denominator/numerator/crystal all zero | Fallback for the current VM; selected when the CPU advertises complete metadata |
| ACPI PM timer | ACPI RSDP/root/FADT checksums and lengths validate. OVMF exposes legacy PM port `0x608`, width 24, standard frequency `3,579,545` Hz | Selected source in all authoritative QEMU runs |
| HPET | No HPET source was required or discovered in the observed startup path | Not implemented in this bounded profile |
| Local APIC/PIT | Interrupt/scheduler-dependent and would add unrelated runtime state | Not used |

ACPI discovery reads the internal UEFI configuration-table pointer before any possible boot-service transition, recognizes ACPI 2.0 and 1.0 GUIDs, validates RSDP and XSDT/RSDT checksums, scans the FADT, validates the PM port and timer-width flag, and retains only the selected table metadata and hardware port. The PM raw counter is extended across its 24-bit wrap using checked delta arithmetic. Half-range ambiguity, invalid width, raw regression, and extension overflow are rejected.

The current harness intentionally does not call `ExitBootServices`. After source initialization, the wrappers depend only on retained ACPI table data and the PM I/O port; they do not depend on Boot Services. A future ExitBootServices profile must preserve the ACPI tables or copy the needed FADT metadata before exit.

## Host and QEMU evidence

Host vectors pass all checked cases: CPUID-15 frequency arithmetic, calibrated-frequency overflow, null/uninitialized output, normalization and regression, statistics, 24-bit/32-bit wrapping, ambiguous half-range, extension overflow, and source-selection decisions.

Final host evidence:

```text
PLATFORM_PERFORMANCE_TESTS=PASSED failures=0
platform_performance.c       354B1741AE278E620239AE0AEF00000E1B912200C757E38BACFF78ABCEEADC38
platform_performance.h       AFFB6A28D685EEF9D9CEA0EB6F9BD0C45F1762F2F97964E61A9C154B698B146E
platform_performance_tests.c 8ED08BCA7C6A0632003FE9FA80D365AF3478E15704CCF3270217ECB5B9C08543
test executable              D956E93F9C034395B5CB2D4E6BB8E9BE2B5F3D9BF0ACCA4DE22A4F023A944285
```

The final allocation-enabled loader is `45CEC283943BD3B7A2F96C55285829C833EA454DE3F8E7F0113AA2350FD73927`; the no-allocation control is `F5CF3B2A5D0636C778CFB40E42DEDE13FF00E1F2B6DC6919F41C3805D7402858`. QEMU is `11.0.0 (v11.0.0-12122-ga4bb4b10c9)` and the firmware code image is `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.

Three complete fresh allocation-startup logs were selected from `artifacts\qpc-final-20260729-allocation\time-contract-runs-*`:

| Run | Source | Frequency | Initial raw | QPC count | First/last normalized | Regressions | Next boundary |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| `time-contract-20260729-053118-727-run2` | ACPI PM timer | `0x369E99` | `0x866FE8` | 1 | `0x1BB74 / 0x1BB74` | 0 | `api-ms-win-crt-runtime-l1-1-0.dll!_initialize_onexit_table` |
| `time-contract-20260729-053238-898-run1` | ACPI PM timer | `0x369E99` | `0x868114` | 1 | `0x1C46F / 0x1C46F` | 0 | same |
| `time-contract-20260729-053440-091-run3` | ACPI PM timer | `0x369E99` | `0x856146` | 1 | `0x1C9AA / 0x1C9AA` | 0 | same |

Each selected log contains `PERF_SOURCE_INIT_OK`, `PERF_INITIAL_RAW`, `QPC_CALL`, `QPC_OK`, `QPC_COUNT=1`, `QPC_MIN_DELTA=0`, `QPC_MAX_DELTA=0`, `QPC_REGRESSIONS=0`, the valid UEFI FILETIME sequence, phase `0x18`, zero TLS allocation limit/pointer, `MANAGED_THREAD_REGISTERED=0`, `ALLOCATION_CONTEXT_VALID=0`, and no fault or allocation marker. The two observations are the source-initialization raw read and the authentic normalized QPC read; the startup consumer itself calls QPC once.

The separate fresh `PerfStallProbe` uses loader `2F419FCBE5FA7162D6613BCADA7AD8F251A0A896B8E679DE1B6560B26F1EAC93` and log `artifacts\qpc-final-20260729-stall\perf-stall-runs-20260729-054743-604\serial.log`. It verifies QPF `0x369E99`, an immediate QPC delta `0x438`, `Stall(1)` status zero, a positive post-stall QPC delta `0x659`, and `PERF_STALL_TEST_OK`; it halts before the FILETIME path so the diagnostic delay cannot perturb the authoritative startup consumer.

## Negative and next boundary

The final `PerfDisabled` loader is `D5F65BCBEB40AD993F0E1A739421A1D61FA9C5EF136A6CAC3CC6D6663F3217BB`. Its fresh serial log contains `PERF_SOURCE_DISCOVERY_BEGIN`, `PERF_SOURCE_UNAVAILABLE`, and `FAIL:perf-source-init`, and contains neither `PERF_SOURCE_INIT_OK` nor `QPC_OK`. This proves that the QPC result is not a marker-only or unconditional-success shim. Historical FILETIME negative controls remain documented in `PLATFORM_TIME_CONTRACT.md`.

The CRT on-exit initialization boundary is now closed only for two empty tables; registration and execution remain unimplemented and untested. The next real blocker is `KERNEL32.dll!InitializeSListHead`. GC heap ownership, virtual memory, managed-thread registration, and first allocation remain separate unresolved contracts.

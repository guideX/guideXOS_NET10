# NativeAOT platform time contract

Status: passed for the bounded `GetSystemTimeAsFileTime` milestone. Three fresh QEMU processes reached a valid time value and the same next NativeAOT boundary, `KERNEL32.dll!QueryPerformanceCounter`. No GC startup or managed allocation is claimed.

## Exact call chain

The allocation-enabled artifact is the PE with SHA-256 `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. Its preferred image base is `0x180000000`, entry RVA is `0x77840`, and the loader calls the entry with `(image_base, 1, null)`.

| Depth | Function / address | Module or object | Inputs and output | State and purpose | Evidence |
| ---: | --- | --- | --- | --- | --- |
| 0 | `efi_main` -> `image.actual_base + 0x77840` | `gate4_loader.c` -> allocation PE | PE image base, DLL process-attach `1`, null reserved argument | Relocations, IAT, firmware-backed TLS vector/block, GS/TEB-like state, and fault handlers are already installed. TLS allocation limit/pointer are both zero. | Loader trace; PE entry RVA; `time-trace-pe-report-20260728.txt` |
| 1 | `0x180077840` -> `call 0x180078290` at `0x18007785c` | Allocation PE entry wrapper | Preserves DLL entry arguments | Compiler/CRT DLL entry wrapper performs security-cookie initialization before the later NativeAOT DLL/bootstrap path. No GC singleton or managed-thread registration has occurred. | Disassembly at `0x180077840` |
| 2 | `0x180078290`, call site `0x1800782ca` | Compiler-generated security-cookie initializer | `RCX = RSP + 0x40`; `[RSP+0x40]` is a writable zeroed 8-byte local | Requests system time to mix into the process/module security cookie. The output is read at `0x1800782d0`; it is not passed to a GC heuristic. | Disassembly; direct serial caller `0x54f12d0` |
| 3 | IAT slot RVA `0x7e1e0`, VA `0x18007e1e0` | `KERNEL32.dll` import table | `RCX` points to the caller's `FILETIME` storage; no return value | Imported function boundary. The normal import thunk also exists at RVA `0x3ca70` (`0x18003ca70`), but this call site uses the direct IAT slot. | `objdump -p`; disassembly |
| 4 | `gxos_get_system_time_as_file_time` | `platform_time.c` | Calls UEFI `RuntimeServices->GetTime(&EFI_TIME, NULL)`, converts, writes exactly 8 bytes | Allocation-free guideXOS implementation. It emits bounded phase markers and halts deterministically on invalid or unavailable time. | Source, host vectors, serial traces |
| 5 | Return to `0x1800782d0`; then `GetCurrentThreadId`, `GetCurrentProcessId`, and `QueryPerformanceCounter` | Same security-cookie initializer | Reads the 64-bit output and mixes identity, address, and counter values | The time contract is complete when the function returns. The next import is reached in the same consumer at call site `0x1800782f9`, IAT RVA `0x7e0c8`. | Three positive serial traces; QPC fail-fast trace |

The call occurs once during the DLL process-attach path when the security-cookie global contains its sentinel. It is before GC singleton construction, managed-thread registration, and creation of a nonzero NativeAOT allocation context. The loader's one-thread TLS substrate exists, but the allocation slots remain zero before and after the call.

## Windows-compatible contract

`GetSystemTimeAsFileTime` follows the Microsoft x64 ABI. The caller supplies one argument in `RCX`: a writable pointer to at least eight bytes. The function has no return value. The output is equivalent to:

```c
typedef struct _FILETIME {
    uint32_t dwLowDateTime;
    uint32_t dwHighDateTime;
} FILETIME;
```

The two 32-bit words are little-endian in memory, with the low word first. Interpreted as an unsigned 64-bit value, the count is in 100-nanosecond intervals since `1601-01-01 00:00:00 UTC`. The implementation accepts unaligned output pointers and writes exactly eight bytes. It does not use host OS services, libc calendar functions, exceptions, allocation, or NativeAOT runtime calls.

The normal arithmetic path is:

```text
validated UTC civil time
  -> checked days since 1601-01-01
  -> checked seconds since that epoch
  -> checked timezone adjustment
  -> checked 100-nanosecond units
  -> nanoseconds / 100 (truncate)
  -> low 32-bit word, then high 32-bit word
```

Leap years use Gregorian rules: divisible by four, except centuries not divisible by 400. Every addition and multiplication that can overflow is checked. Years before 1601, years after 9999, invalid civil fields, invalid EFI padding, invalid timezones, and unsupported daylight semantics are rejected.

## Authoritative guideXOS time source

The implementation reads UEFI `EFI_RUNTIME_SERVICES.GetTime`. The [UEFI Runtime Services specification](https://uefi.org/specs/UEFI/2.9_A/08_Services_Runtime_Services.html) defines `Localtime = UTC - TimeZone`, so the conversion uses `UTC = local time + TimeZone`. The firmware `TimeZone` value is accepted only in `[-1440,1440]`, or as `2047` for unspecified. Daylight bits outside bits 0 and 1 are invalid. Bit 0 without bit 1 means DST is pending but supplies no adjustment amount, so it is rejected; values already adjusted by firmware (bit 1 set) are accepted with the supplied timezone.

The OVMF/QEMU profile returned `TimeZone=2047`, `Daylight=0`. The authoritative QEMU command line uses `-rtc base=utc,clock=vm`, and the build explicitly enables `GXOS_ASSUME_UNSPECIFIED_TIMEZONE_UTC`. The implementation emits `TIME_UNSPECIFIED_TIMEZONE_UTC_POLICY` and then treats that one QEMU-profile value as UTC. This is not a silent generic firmware fallback: a strict build rejects 2047 with `TIME_INVALID_TIMEZONE`. A deterministic fixed value is available only in isolated negative/test builds.

The imported API has no return channel. The platform policy is therefore deterministic halt with a class marker: `TIME_NULL_OUTPUT`, `TIME_FIRMWARE_ERROR`, `TIME_INVALID_FIELD`, `TIME_INVALID_TIMEZONE`, or `TIME_CONVERSION_OVERFLOW`. Normal success markers are `TIME_API_ENTER`, `UEFI_TIME_OK`, `FILETIME_CONVERSION_OK`, and `TIME_API_RETURN`.

## Consumer requirements

1. The artifact calls the API because the compiler/CRT security-cookie initializer executes during DLL process attach.
2. The result is consumed by that security-cookie initializer, which mixes it into the process/module cookie before continuing to thread/runtime initialization.
3. The consumer requires a Windows-compatible UTC `FILETIME` shape and a meaningful nonzero entropy input; the trace proves the implementation supplies a real firmware-derived UTC-compatible value.
4. Monotonicity is not required by this consumer: the call is read once, not compared with a later sample.
5. Cross-boot stability is not required; security-cookie diversity is more relevant than stable identity.
6. Subsecond precision is not required for correctness, but the implementation preserves representable 100 ns units and truncates sub-100 ns nanoseconds explicitly.
7. A fixed deterministic test value is legal only in an isolated test build. It is not the authoritative runtime path.
8. Zero is not an immediate invariant failure in this consumer—the controlled zero experiment still reaches QPC—but zero is not a valid authoritative Windows time result and provides no useful time/entropy contribution.
9. An incorrect value affects security-cookie entropy and possibly diagnostics/heuristics; this call is not a GC-correctness input. No evidence indicates it initializes the GC or allocation context.
10. In this artifact it is expected once during startup, before every allocation and before managed-thread registration. It is not periodic.

## Independent conversion vectors

The host test executable was built with `gcc -std=c11 -Wall -Wextra -Werror -O2` from the same `platform_time.c`. Expected modern values were independently calculated rather than generated by the helper.

| Input | Expected 64-bit FILETIME | Actual | Low word | High word | Result |
| --- | ---: | ---: | ---: | ---: | --- |
| `1601-01-01T00:00:00Z` | `0x0000000000000000` | `0x0000000000000000` | `0x00000000` | `0x00000000` | PASS |
| `1601-01-01T00:00:00.0000001Z` | `0x0000000000000001` | `0x0000000000000001` | `0x00000001` | `0x00000000` | PASS |
| `2024-02-29T23:59:59.1234567Z` | `0x01DA6B6B66CD0007` | same | `0x66CD0007` | `0x01DA6B6B` | PASS |
| `1900-02-28T00:00:00Z` | `0x014F64CF99D5C000` | same | `0x99D5C000` | `0x014F64CF` | PASS |
| `1900-03-01T00:00:00Z` | `0x014F6598C43F8000` | same | `0xC43F8000` | `0x014F6598` | PASS |
| `2000-02-29T00:00:00Z` | `0x01BF8247EBCC8000` | same | `0xEBCC8000` | `0x01BF8247` | PASS |
| `1999-12-31T23:59:59.999999999Z` | `0x01BF53EB256D3FFF` | same | `0x256D3FFF` | `0x01BF53EB` | PASS |
| `2000-01-01T00:00:00Z` | `0x01BF53EB256D4000` | same | `0x256D4000` | `0x01BF53EB` | PASS |
| `2020-01-02T03:04:05.678901299Z` | `0x01D5C1194B2B9814` | same | `0x4B2B9814` | `0x01D5C119` | PASS; truncates |
| `9999-12-31T23:59:59.999999999Z` | `0x24C85A5ED1C03FFF` | same | `0xD1C03FFF` | `0x24C85A5E` | PASS |
| `2020-01-02T03:04:05.600000000Z` deterministic test clock | `0x01D5C1194B1F8E00` | same | `0x4B1F8E00` | `0x01D5C119` | PASS |
| EFI `2024-02-29`, TZ `0`, daylight `0` | `0x01DA6B6B66CD0007` | same | `0x66CD0007` | `0x01DA6B6B` | PASS |
| EFI `2024-02-29`, TZ `480`, daylight `2` | `0x01DA6BAE74F04007` | same | `0x74F04007` | `0x01DA6BAE` | PASS |
| Year `1600` | reject | reject | — | — | PASS |
| Invalid month, day, hour, minute, second, or nanoseconds | reject | reject | — | — | PASS |
| EFI invalid month or day | reject | reject | — | — | PASS |
| EFI timezone `2047` in strict mode | reject | reject | — | — | PASS |
| EFI pending daylight (`Daylight=1`) | reject | reject | — | — | PASS |
| Null output pointer | reject | reject | — | — | PASS |
| Checked add/multiply overflow | reject | reject | — | — | PASS |

The isolated `GXOS_TEST_WRONG_EPOCH` build fails the known vectors, confirming that the epoch constant is tested rather than assumed. The controlled fixed-zero build writes zero and proceeds to the next QPC import only as a negative experiment.

## Runtime validation

The three authoritative positive runs used QEMU `11.0.0 (v11.0.0-12122-ga4bb4b10c9)`, firmware SHA-256 `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, managed artifact SHA-256 above, and loader SHA-256 `37F8D02CDC9536871D06C1CDCD7356D1FADD44C91338F16CC7837EC15B67A845`.

| Run | FILETIME | Count | Next boundary | TLS alloc limit/pointer | Thread/allocation state | Result |
| --- | ---: | ---: | --- | --- | --- | --- |
| `time-contract-20260728-181753-150-run1` | `0x01DD1EF8155B2380` | 1 | `KERNEL32.dll!QueryPerformanceCounter` | `0/0` | managed thread `0`; allocation context invalid | `TIME_CONTRACT_PASSED_NEXT_IMPORT` |
| `time-contract-20260728-181813-050-run1` | `0x01DD1EF82146E580` | 1 | same | `0/0` | same | `TIME_CONTRACT_PASSED_NEXT_IMPORT` |
| `time-contract-20260728-181832-511-run1` | `0x01DD1EF82C9A1100` | 1 | same | `0/0` | same | `TIME_CONTRACT_PASSED_NEXT_IMPORT` |

All three contain `TIME_API_RETURN`, `TIME_CONSUMER_PHASE=0x5`, no fault marker, and no unresolved required import at the old boundary. `GC_STARTUP_ADVANCED` is intentionally not emitted because the immediate security-cookie consumer reaches the next fail-fast import before the NativeAOT runtime startup path completes.

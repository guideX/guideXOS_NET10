# Evidence ledger

Generated binaries, emulator firmware, logs, and manifests remain under ignored `artifacts\` directories. This ledger records the reproducible source and runtime evidence for Gate 4.

## Repository baseline and scope

| Evidence | Result |
| --- | --- |
| Branch at start of this pass | `main` |
| HEAD at start of this pass | `03b8e420dc82bcd1e013bf713ea1efec66d0f792` |
| Upstream at start | `origin/main` |
| Remote at start | `origin git@github.com:guideX/guideXOS_NET10.git` (fetch/push) |
| Worktree at start | clean; no pre-existing dirty files in this pass |
| Reference repositories | read-only evidence only; no edits, formatting, regeneration, commits, or staging |
| Prior Gate 4 loader hash | `C92E4286AEE275212128C6B6718AF8B0C23A0EAB7BAB8EBD5D0E7B07D3256E1A` |
| Prior Gate 4 result | three fresh runs stopped at `PE_IMPORT_COUNT=10` / `GATE4_BLOCKED_IMPORTS`; preserved in earlier logs and documents |
| Commit policy | no commit or staging performed by this pass |

## NativeAOT artifact

| Path | SHA-256 |
| --- | --- |
| `artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll` | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| final staged `artifacts\gate4\ESP\GXOS\gxos-managed-entry-probe.dll` | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| NativeAOT map XML retained from Gate 1 | `E38DB968C40F19F427D4AEF64D7BF5B19E3E16B3010F8DA83FD07CFB449899FC` |
| ILC response retained from Gate 1 | `7E33D44C6E1ECF354F732A56565521DD87C086A194C557641882B4FE4232BF85` |
| linker response retained from Gate 1 | `4A0B63F84FA712D4C30556C532C6F1F62C825257B5C78B45DDFBE5C6605C704A` |

### Current allocation milestone artifacts

| Artifact | SHA-256 | Size |
| --- | --- | ---: |
| no-allocation control shared PE | `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861` | 729,600 |
| allocation-enabled shared PE | `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` | 731,136 |
| no-allocation map XML | `E38DB968C40F19F427D4AEF64D7BF5B19E3E16B3010F8DA83FD07CFB449899FC` | — |
| allocation-enabled map XML | `65DDD404B161E26E5B33A158EFA678A0AC33F4CCA474BD697F17DDE85C84D34F` | — |
| allocation differential JSON | `A1F78E6C7F7983690A24370EFC74441F1F7FC90EB52D9C1FF28E498352308DE3` | — |

## Final loader and firmware

| Artifact | SHA-256 |
| --- | --- |
| `artifacts\gate4\ESP\EFI\BOOT\BOOTX64.EFI` | `92C8371430116BA459A8EC1B5CC445DBF4222B40A57265BC1BDACF24FD46BEA0` |
| QEMU firmware code image | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` |
| QEMU | `11.0.0 (v11.0.0-12122-ga4bb4b10c9)` |

Harness command: `tools\Build-Gate4Harness.ps1`. Positive command: `tools\Run-Gate4.ps1 -RunCount 3 -TimeoutSeconds 15 -ExpectedLoaderSha256 92C8371430116BA459A8EC1B5CC445DBF4222B40A57265BC1BDACF24FD46BEA0`.

Current-source fresh control: `gate4-20260728-063011-635-run1`, artifact `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`, loader `8382E6579D2ED3E6E12EC26A86E6DA1683A849A5E01BC26BBBDD98AFFB1E2A71`, firmware `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.

Final current-source control after the time implementation and diagnostics were stabilized: `gate4-20260728-183949-461-run1`, artifact `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`, loader `D54D783066263B62057AC0B2F7B8692FA38B370339094A38AFF3F5A70ED9F94E`, firmware `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, managed marker present, return zero, deterministic halt.

## Positive validation matrix

All runs used the same staged PE and final loader, fresh OVMF variable storage, isolated serial output, and a bounded 15-second process. Each serial log contains image base `0x000000000547B000`, target VA `0x000000000549F724`, `10` descriptors, `124` symbols, `124` resolved, `18` functional, `106` fail-fast, `0` unresolved, `BOOT_INFO_PTR=0x00000000001076D0`, `CPU_DF=0`, `CPU_MXCSR=0x1F80`, `CPU_X87_CONTROL=0x037F`, `STACK_RSP_BEFORE_CALL=0x0000000007E65820`, `STACK_RSP_MOD16=0`, the managed marker, return zero, and completion.

| Sequence | Run ID | Managed artifact hash | Loader hash | Image base | Target VA | Import result | Managed marker | Return/halt | Fault | Pass |
| ---: | --- | --- | --- | ---: | ---: | --- | --- | --- | --- | ---: |
| 1 | `gate4-20260728-051529-350-run1` | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` | `92C8371430116BA459A8EC1B5CC445DBF4222B40A57265BC1BDACF24FD46BEA0` | `0x0547B000` | `0x0549F724` | `10/124; resolved 124; functional 18; fail-fast 106; unresolved 0` | `GXOS_NET10:MANAGED_ENTRY_OK` | return `0`; deterministic halt | none | yes |
| 2 | `gate4-20260728-051544-737-run2` | same | same | `0x0547B000` | `0x0549F724` | same | same | return `0`; deterministic halt | none | yes |
| 3 | `gate4-20260728-051600-050-run3` | same | same | `0x0547B000` | `0x0549F724` | same | same | return `0`; deterministic halt | none | yes |

Run directory: `artifacts\gate4\runs-20260728-051529-268`.

## Negative controls

| Control | Variant artifact/loader hash | Observed result | Success marker | Result |
| --- | --- | --- | --- | --- |
| invalid boot-info version | normal PE; loader `A019350E2B389ED0CC7701A889623C79FC3ADF923B5492EDFBBE8F1CE7313A34` | managed return `0x00000000FFFFFFFE`; `FAIL:managed-return-nonzero` | absent | pass |
| null serial callback | normal PE; loader `047FC785E9BB506F3A71EA109EB9B80BEE6A71EE7D8B164390D80DB6054C240D` | managed return `0x00000000FFFFFFFE`; failure path | absent | pass |
| one unresolved import | normal PE; loader `BD49E7A40BDCCDA529115F1683D6722500F12636C8D58A06D48B08F527054D65` | `UNRESOLVED_REQUIRED_IMPORTS=1`; `FAIL:negative-unresolved-import` | absent | pass |
| deliberate fail-fast call | normal PE; loader `4534D90075BA0D9836134F3566D1AA3E9C5A92DF9E8056AA08B793A81F20BDD0` | `UNEXPECTED_IMPORT_CALL:ADVAPI32.dll!RegisterEventSourceW`; halt | absent | pass |
| managed return changed to `7` | PE `0EAABC682F876ECC6C9CDE4FC460C1CA42D74DDF9C36D0358FB7299778CDDC7B`; loader `FE2EF390DFD64EC765747AD55B7F90B71B7846ACDFA28A43CF48449F5D1AF568` | `AFTER_MANAGED_RETURN=0x0000000000000007`; failure path | absent | pass |
| managed marker byte changed `K -> X` | PE `C30AD0C7F6513AA33E4FBFB320E717E06E4A7111A717BBA161BCD316F3AF977F`; loader same `FE2EF...` | exact `GXOS_NET10:MANAGED_ENTRY_OX`; return `0` and halt | exact success marker absent | pass |

The normal staged artifact was re-hashed after the negative builds and restored to the normal payload before the final positive matrix.

## Historical Gate 4 evidence

The prior three-run evidence used loader `C92E4286AEE275212128C6B6718AF8B0C23A0EAB7BAB8EBD5D0E7B07D3256E1A` and stopped after relocation at the ten-descriptor import boundary. That evidence is not overwritten and explains why the first pass did not claim managed entry.

## Allocation milestone evidence

The pre-startup allocation run `allocation-probe-before-gc-20260728-060732-637` used the allocation-enabled PE and reached managed status `-10`; it emitted no first-allocation marker. The clean standard-startup blocker run `allocation-startup-blocker-20260728-0630` used loader `CB48F1CACF18207EB71BB3B7D3255B0B8E82C485E688167753E84ECEB71C56C8` and stopped at `UNEXPECTED_IMPORT_CALL:KERNEL32.dll!GetSystemTimeAsFileTime`. These are bounded negative results, not Gate 4 failures.

## Conclusions

| Gate | Conclusion |
| --- | --- |
| 1 | Passed: reproducible .NET 10 NativeAOT PE artifact. |
| 2 | Passed: exact runtime/platform census recorded. |
| 3 | Passed: direct PE/COFF load, relocation, and byte-identity staging. |
| 4 | Passed: three consecutive fresh QEMU processes entered the legitimate NativeAOT export path, emitted the managed marker, returned zero, and halted with zero unresolved required imports. |
| 5 | Bounded negative: the opt-in standard NativeAOT DLL/bootstrap entrypoint is reached, but its freestanding process-time dependency is not supplied. |
| 6 | Not passed: the generated `RhpNewFast` allocation path is present, but no GC-backed first allocation is proven; the probe returns `-10` before startup. |
| 7 | Not run: repeated allocation/exhaustion is gated on a proven first allocation. |

## GetSystemTimeAsFileTime milestone addendum

This addendum is the authoritative record for the bounded time-contract pass. Baseline branch was `main`, HEAD was `03b8e420dc82bcd1e013bf713ea1efec66d0f792`, upstream was `origin/main`, and `git status --short --branch` was `## main...origin/main` with no pre-existing dirty files. No commit, push, staging, or reference-repository write occurred.

Tool versions: .NET SDK `10.0.302`; GCC `15.2.0` MinGW-W64; GNU objdump `2.46.0.20260210`; Git `2.54.0.windows.1`; QEMU `11.0.0 (v11.0.0-12122-ga4bb4b10c9)`. Firmware code SHA-256 is `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.

Commands used:

```powershell
.\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\time-allocation-authoritative-20260728 -ManagedArtifact .\artifacts\allocation-startup-blocker-20260728-0630\ESP\GXOS\gxos-managed-entry-probe.dll -EnableNativeAotStartup -AssumeUnspecifiedTimezoneUtc
.\tools\Run-PlatformTimeTests.ps1 -OutputDirectory .\artifacts\platform-time-tests-final-20260728
.\tools\Run-PlatformTimeTests.ps1 -OutputDirectory .\artifacts\platform-time-tests-wrong-epoch-final-20260728 -WrongEpoch -ExpectFailure
.\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\time-noalloc-control-final-20260728 -ManagedArtifact .\artifacts\baseline-noalloc-control-20260728\ESP\GXOS\gxos-managed-entry-probe.dll
.\tools\Run-Gate4.ps1 -GateDirectory .\artifacts\time-noalloc-control-final-20260728 -RunCount 1 -TimeoutSeconds 15 -ExpectedLoaderSha256 9BD280D9E82D38EACE3518C172776A2C9A84BF2E884503A0C735B71A9DAD4069
.\tools\Run-NativeAotTimeContract.ps1 -GateDirectory .\artifacts\time-allocation-authoritative-20260728 -ExpectedArtifactSha256 6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379 -ExpectedLoaderSha256 37F8D02CDC9536871D06C1CDCD7356D1FADD44C91338F16CC7837EC15B67A845 -RunCount 1 -TimeoutSeconds 15
```

The current time-source implementation hashes are: `platform_time.c` `EB09B3A3E190C7CA279DA5FDA87F056576524EB05F2B4DA0F041524258E4648E`; `platform_time.h` `EDBE9BB1033883D25F1DC3D7A0E3A2090EA4642E5EC982F8E77206F13DB0B37A`; host vector source `87313FB93C958F687D75E87E9BDCD9EF98A9488490F89CF6B22B7E76575EE10E`; loader source `D4E150720DB0B17F4F11BBD94CD632F292089EDEA83B4F5173931FEEFB4A46AC`. The host test executable from the final vector run is `2E000A5E14A89E5EF4BCF0B7C1F2A858A61C19FAB190B76E48C9DF07BA07181`.

The allocation PE remained `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, with 10 import descriptors and 124 imported symbols. The successful three-run time sequence used loader SHA-256 `37F8D02CDC9536871D06C1CDCD7356D1FADD44C91338F16CC7837EC15B67A845` and these isolated serial logs:

| Sequence | Run ID | FILETIME | Time source | Count | Next boundary | Context | Pass |
| ---: | --- | ---: | --- | ---: | --- | --- | ---: |
| 1 | `time-contract-20260728-181753-150-run1` | `0x01DD1EF8155B2380` | UEFI `GetTime`, QEMU UTC VM-clock policy | 1 | `KERNEL32.dll!QueryPerformanceCounter` | TLS limit/pointer `0/0`; thread `0`; allocation valid `0` | yes |
| 2 | `time-contract-20260728-181813-050-run1` | `0x01DD1EF82146E580` | same | 1 | same | same | yes |
| 3 | `time-contract-20260728-181832-511-run1` | `0x01DD1EF82C9A1100` | same | 1 | same | same | yes |

Each run verified the managed payload hash, loader hash, firmware hash, `TIME_API_ENTER`, firmware status `0`, `UEFI_TIME_OK`, explicit unspecified-timezone policy marker, valid conversion, `TIME_API_RETURN`, `TIME_CONSUMER_PHASE=0x5`, and no fault. The pre-change reproduction was `gate4-20260728-174524-533-run1`, which stopped at `GetSystemTimeAsFileTime`; the final fresh no-allocation control was `gate4-20260728-181908-176-run1`, which emitted `MANAGED_ENTRY_OK` and returned zero.

The final diagnostic-only source update (storing the caller address for fault records) was rebuilt as loader `FE4AB87302757580183826D81679783BAB15D4715883641E93FE82848FE5C331`; fresh run `time-contract-20260728-184130-390-run1` also passed through the time contract and reached QPC with FILETIME `0x01DD1EFB61F42E00`.

Negative controls passed: time disabled restored the original fail-fast boundary; invalid firmware month, day, and timezone halted with the exact class markers; null output, pre-1601, overflow, invalid civil fields, EFI invalid timezone, pending daylight, leap-day, nanosecond truncation, and deterministic test-clock vectors behaved as expected; the one-byte marker mutation changed `TIME_API_ENTER` to `TIME_API_ENTEr`; the wrong-epoch isolated build failed known vectors; and the fixed-zero experiment wrote zero and reached QPC but was not treated as authoritative. All negative harnesses used isolated directories and the authoritative payload was not mutated.

## Loader-code classification

| Category | Lines/components | Permanent | Temporary | Generated | Retain | Refactor later | Evidence |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| PE loader logic | PE headers, section loading, relocations, export lookup, bounds checks, EFI image staging | 1 | 0 | 0 | 1 | 0 | Gate 3/4 traces and PE reports |
| Import resolver | Descriptor/name walk, 21 functional targets, 103 fail-fast targets | 1 | 0 | 0 | 1 | 0 | 10/124 census; positive and negative traces |
| TLS/runtime substrate | TLS vector/block, GS/TEB-like state, FLS, identity, handles, stack query, critical sections | 0 | 1 | 0 | 1 | 1 | Managed-entry proof; allocation context remains zero |
| GC/startup tracing | `GC_STARTUP_BEGIN`, time phases, startup markers, consumer-state fields | 0 | 1 | 0 | 1 | 1 | Time serial logs and fault fields |
| Time/performance contracts | Isolated civil conversion, EFI validation, FILETIME writer, ACPI PM/TSC source selection, QPC/QPF wrappers | 1 | 0 | 0 | 1 | 0 | Host vectors, three positive QEMU runs, and Stall probe |
| Diagnostic instrumentation | Serial markers, IDT/fault capture, bounded phase-aware state | 0 | 1 | 0 | 1 | 1 | Prior Gate 4 and current negative controls |
| Generated import tables | NativeAOT PE import directory and IAT | 0 | 0 | 1 | 1 | 0 | Artifact/map/PE reports; not hand-authored loader logic |
| Negative controls | Build-script scenarios and isolated mutation builds | 0 | 1 | 0 | 1 | 0 | Negative serial logs and wrong-epoch host run |
| Abandoned attempts | Fake allocator, fake GC startup, broad compatibility shims | 0 | 0 | 0 | 0 | 0 | None retained; no dead compatibility helper was needed |
| Duplicated helpers | Date conversion is isolated; existing byte-copy/zero helpers are reused | 0 | 0 | 0 | 1 | 1 | No conclusively abandoned duplicate removed in this pass |

The old `~1,981`-line loader addition is therefore retained as evidence-bearing loader/substrate code, with time conversion kept outside the giant loader function. No broad refactor was performed.

## QueryPerformanceCounter milestone addendum

This is the current-source evidence for the performance-counter pass. The final host vector run reported `PLATFORM_PERFORMANCE_TESTS=PASSED failures=0`; `platform_performance.c` SHA-256 is `354B1741AE278E620239AE0AEF00000E1B912200C757E38BACFF78ABCEEADC38`, `platform_performance.h` is `AFFB6A28D685EEF9D9CEA0EB6F9BD0C45F1762F2F97964E61A9C154B698B146E`, the host vector source is `8ED08BCA7C6A0632003FE9FA80D365AF3478E15704CCF3270217ECB5B9C08543`, and the test executable is `D956E93F9C034395B5CB2D4E6BB8E9BE2B5F3D9BF0ACCA4DE22A4F023A944285`.

Final allocation-startup artifacts: PE `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, loader `45CEC283943BD3B7A2F96C55285829C833EA454DE3F8E7F0113AA2350FD73927`. Final no-allocation artifacts: PE `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`, loader `F5CF3B2A5D0636C778CFB40E42DEDE13FF00E1F2B6DC6919F41C3805D7402858`. QEMU and firmware remained `11.0.0 (v11.0.0-12122-ga4bb4b10c9)` and `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.

Three complete fresh allocation-startup logs passed as `QPC_CONTRACT_PASSED_NEXT_IMPORT`: `time-contract-20260729-053118-727-run2`, `time-contract-20260729-053238-898-run1`, and `time-contract-20260729-053440-091-run3`. All selected the ACPI PM timer at port `0x608`, width 24, frequency `0x369E99`, recorded two source/normalized observations, one QPC call, zero regressions, phase `0x18`, zero TLS allocation context, and next boundary `api-ms-win-crt-runtime-l1-1-0.dll!_initialize_onexit_table`. The separate fresh Stall probe passed QPF plus immediate/after-`Stall(1)` QPC checks with loader `2F419FCBE5FA7162D6613BCADA7AD8F251A0A896B8E679DE1B6560B26F1EAC93`; final log `perf-stall-runs-20260729-054743-604`, with deltas `0x438` and `0x659`. The final disabled-source negative passed with loader `D5F65BCBEB40AD993F0E1A739421A1D61FA9C5EF136A6CAC3CC6D6663F3217BB` and stopped at `FAIL:perf-source-init` before QPC.

The no-allocation control passed three fresh QEMU runs under `artifacts\qpc-final-20260729-noalloc\runs-20260729-053730-063`, with loader `F5CF3B2A5D0636C778CFB40E42DEDE13FF00E1F2B6DC6919F41C3805D7402858`, import treatment 21 functional / 103 fail-fast, managed entry, zero return, and completion. The next real blocker is CRT on-exit/bootstrap initialization; first allocation and GC ownership remain unproven. See [PLATFORM_PERFORMANCE_COUNTER.md](PLATFORM_PERFORMANCE_COUNTER.md) for the full source inventory and contract.

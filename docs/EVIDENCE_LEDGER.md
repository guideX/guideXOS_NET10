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

## Historical pre-callback/control artifacts

The entries below are retained as historical Gate 1/Gate 4 evidence. They are
not the current managed-entry/callback/GC payload identity.

| Path | SHA-256 |
| --- | --- |
| historical `artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll` | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| historical staged `artifacts\gate4\ESP\GXOS\gxos-managed-entry-probe.dll` | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| NativeAOT map XML retained from Gate 1 | `E38DB968C40F19F427D4AEF64D7BF5B19E3E16B3010F8DA83FD07CFB449899FC` |
| ILC response retained from Gate 1 | `7E33D44C6E1ECF354F732A56565521DD87C086A194C557641882B4FE4232BF85` |
| linker response retained from Gate 1 | `4A0B63F84FA712D4C30556C532C6F1F62C825257B5C78B45DDFBE5C6605C704A` |

### Current merge-gate payload

| Artifact | SHA-256 | Size |
| --- | --- | ---: |
| source `artifacts\nativeaot-gc-probe-gate1-20260817\shared\gxos-managed-entry-probe.dll` | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` | 730,112 |
| staged `artifacts\nativeaot-gc-probe-gate4-20260817\ESP\GXOS\gxos-managed-entry-probe.dll` | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` | 730,112 |
| deterministic audit loader `artifacts\nativeaot-gc-audit-gate4-deterministic-a-20260817\ESP\EFI\BOOT\BOOTX64.EFI` | `9E78E7145C8BB3AC8E5559C4347275EFDC160FC062088769C15A335E5E6D1601` | 534,299 |

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

## CRT on-exit bootstrap milestone addendum

This addendum records the follow-on requested after the QPC/QPF milestone. Baseline was branch `main`, HEAD `52bdc9cad93bfd4404e11c07defa11db955f4afa`, upstream `origin/main`, and a clean worktree (`## main...origin/main`). No commit, push, staging, or reference-repository write occurred. The pre-change timing evidence was preserved under `artifacts\qpc-final-20260729-allocation` before the CRT source was changed.

The CRT-enabled loader was built with `CrtOnexitInit`; the final rebuilt loader SHA-256 is `257CA5DC1BF38CB485844B62B97BEFBC37E8A0535F9A89AF7DE2A314CD54764A`. The immediately preceding equivalent CRT-enabled build used for the three complete selected traces below was `54CD910800FB808255C8A1490EF89ACDF1D09FB3C306ABF588453BE2F5CE58B8`; the only source difference was caching the encoded-null read before writing the three fields. The managed artifact remained `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. QEMU is `11.0.0 (v11.0.0-12122-ga4bb4b10c9)` and the copied firmware SHA-256 is `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.

The complete fresh positive traces below each contain the prior FILETIME/QPC milestones, `PE_IMPORT_FUNCTIONAL=22`, `PE_IMPORT_FAILFAST=102`, `UNRESOLVED_REQUIRED_IMPORTS=0`, two `CRT_ONEXIT_INITIALIZED_OK` markers, `KERNEL32.dll!InitializeSListHead` as the next boundary, `QPC_COUNT=1`, zero regressions, and zero allocation context:

| Positive process | FILETIME | CRT calls | Next boundary | Serial evidence |
| --- | --- | ---: | --- | --- |
| `time-contract-20260729-060833-471-run1` | `0x01DD1F5B5D5F6200` | 2 | `KERNEL32.dll!InitializeSListHead` | `artifacts\crt-onexit-init-final\time-contract-runs-20260729-060833-153\time-contract-20260729-060833-471-run1.serial.log` |
| `time-contract-20260729-060940-331-run2` | `0x01DD1F5B84B62F00` | 2 | `KERNEL32.dll!InitializeSListHead` | `artifacts\crt-onexit-init-final\time-contract-runs-20260729-060909-384\time-contract-20260729-060940-331-run2.serial.log` |
| `time-contract-20260729-061255-615-run1` | `0x01DD1F5BF9896900` | 2 | `KERNEL32.dll!InitializeSListHead` | `artifacts\crt-onexit-init-final\time-contract-runs-20260729-061255-307\time-contract-20260729-061255-615-run1.serial.log` |

These are three complete fresh QEMU processes selected from isolated runs of the immediately preceding equivalent implementation; incomplete QEMU startup attempts were retained but are not counted as positive evidence. The final rebuilt loader also produced one complete positive trace before additional QEMU serial truncation variability, and its host contract vectors pass. The positive claim is limited to the complete logs above and does not claim a fourth complete final-hash process.

The exact static call sites are `0x180077c8d` and `0x180077c9d`, passing tables at `0x1800b5e98` and `0x1800b5eb0`. The implementation is `src\Gate4Harness\crt_onexit.c`, with its target declaration in `src\Gate4Harness\crt_onexit.h`; it sets all three fields to the image security-cookie-derived encoded-null token only for an empty table, returns zero for a non-empty/idempotent state, and returns negative for null or disabled encoding. It does not allocate, synchronize, register, execute, or shut down.

Negative evidence:

| Control | Result |
| --- | --- |
| Host CRT vectors | `CRT_ONEXIT_HOST_TESTS=PASSED`; null argument, initialization, repeated initialization, marker mutation, opaque non-empty state, and disabled encoding all passed. Executable SHA-256: `638120BA5B22FCD2EFDE1D465C35C22BDFEB451026BB507C458F4688E5FD343B`. |
| Disabled QEMU implementation | Loader `D04AF049FD23433846F1A99958B6C1011C2B9B85A99908499C84A89018136EE9`; halted at `api-ms-win-crt-runtime-l1-1-0.dll!_initialize_onexit_table`, emitted no CRT success marker, and retained QPC summary/zero allocation context. |
| Marker mutation QEMU | Loader `A8FCABF5BADD60D11D7E4FA612E28521C55A175451B50F067D370A2594433F69`; reached both calls and the next boundary, emitted `CRT_ONEXIT_INITIALIZED_OX`, and did not emit `CRT_ONEXIT_INITIALIZED_OK`. |

No positive trace reached `_register_onexit_function`, `_execute_onexit_table`, `_crt_atexit`, `atexit`, `_cexit`, or `_c_exit`. No callback registration or execution occurred. No allocation, managed-thread registration, GC heap initialization, or first allocation occurred. The new deepest boundary is `KERNEL32.dll!InitializeSListHead`.

## SLIST initialization contract addendum

This addendum records the follow-on from the committed CRT on-exit milestone. Baseline was re-recorded before source changes on branch `main`, HEAD `cd59ff5edd25d21b998b64148c79eb2712d17f3f`, upstream `origin/main`, with a clean worktree. The baseline loader was a fresh `CrtOnexitInit` build in `artifacts\slist-baseline-20260729-091209-685`; it preserved PE loading, relocation, TLS/GS/TEB/FLS, FILETIME, QPC/QPF, both CRT table markers, zero unresolved imports, zero allocation context, and stopped at `KERNEL32.dll!InitializeSListHead`.

The SLIST-enabled final harness was rebuilt in `artifacts\slist-final-20260729-104138-993`. The managed payload SHA-256 is `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`; the positive loader SHA-256 is `67284C49FE561EB9E53B5990E58CAD1F76AB8348F7255AF05AFCC50BB7C34909`; the disabled loader is `950D28D84D35A6AF6F0E243DA0940D9763E7A9C71CE8EF80EE3B866966A9FDD7`; the marker-mutation loader is `AD3B65DA2C92F2906FC44FE2919CF524BE9E34AECFCCC62AC18E3BC8A8FD1CEF`. The current SLIST-enabled import treatment is 23 functional / 101 fail-fast, with zero unresolved required imports.

The current payload import/disassembly census found `InterlockedFlushSList` at IAT RVA `0x7e2e8` and `InitializeSListHead` at `0x7e2f8`; the other requested SLIST names are absent from the PE import census. The exact caller is the preferred `0x180077550` NativeAOT attach/bootstrap helper, through `0x180078350`, with a static writable image header at preferred `0x1800b5ed0` and relocated QEMU address `0x552eed0`. One call occurs, with zero alignment remainder. The wrapper verifies all 16 bytes and the x64 depth/sequence/reserved/next fields before emitting `GXOS_NET10:SLIST_HEAD_INITIALIZED_OK`. The next observed dependency is `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`. No push, pop, flush, depth, atomic header operation, allocation, GC initialization, managed-thread registration, or scheduler activity was observed.

The complete host suite was rerun after the ABI-explicit header change:

| Host evidence | Result |
| --- | --- |
| Exact empty bytes, fields, guards, repeated and opaque reset | `SLIST_TEST_INITIALIZATION=PASS`, `SLIST_TEST_REINITIALIZATION=PASS`, `SLIST_TEST_OPAQUE_STATE=PASS` |
| Null and misalignment | `SLIST_TEST_NULL=PASS`, `SLIST_TEST_MISALIGNMENT=PASS`; no writes occurred |
| Size/alignment assertions | `SLIST_TEST_LAYOUT_ASSERTIONS=PASS` |
| No allocation/platform services | `SLIST_TEST_NO_ALLOCATION_OR_PLATFORM_SERVICES=PASS` |
| Host suite | `SLIST_HOST_TESTS=PASSED` |
| Incorrect-layout compile control | `SLIST_TEST_INCORRECT_LAYOUT_CONTROL=PASS` |
| No external core references | `SLIST_TEST_NO_EXTERNAL_REFERENCES=PASS` |

The disabled QEMU control is complete and positive as a negative control: the final disabled binary was rebuilt in `artifacts\slist-final-20260729-104138-993\disabled` and the earlier complete disabled trace at `artifacts\slist-final-20260729-100119-060\disabled\time-contract-runs-20260729-101316-870\time-contract-20260729-101317-171-run1.serial.log` passed the full validator, stopped at the original `KERNEL32.dll!InitializeSListHead` import boundary, emitted no SLIST success marker, retained `QPC_REGRESSIONS=0`, and retained zero TLS allocation context. An earlier monitored positive run for the equivalent SLIST-enabled build is retained at `artifacts\slist-positive-20260729-092308-876\debug-monitor\serial.log`; it reached `_initterm_e` and emitted the full terminal summary.

The final-hash positive attempts are retained under `artifacts\slist-final-20260729-100119-060\time-contract-runs-*`, `monitor-run1`, `tcp-run1`, `priority-run1`, `nopoll-run1`, and `tcg-multi-run1`; the final rebuilt binary is retained under `artifacts\slist-final-20260729-104138-993`. They reached the SLIST success marker and, in the monitored attempt, the `_initterm_e` boundary, but several QEMU runs were truncated by host execution stalls before the complete QPC summary. They are deliberately not counted as complete Gate I runs. The marker-mutation QEMU attempt was likewise not counted because the host stall occurred before the mutated marker; the host marker and layout controls remain deterministic. Therefore this pass does not claim the required three consecutive final-hash QEMU runs; the implementation and next-boundary result are proven, but the three-run evidence gate remains open pending a clean QEMU host run.

## Final immutable validation attempt (2026-07-29)

Baseline for this evidence-closing pass was branch `main`, HEAD `8f76741c2d358ad06856c1329ced84651fb7f8a1`, upstream `origin/main`, with a clean worktree. The final build was `artifacts\slist-final-validation-20260729-195900`; the loader SHA-256 was `333F110626390045D8E9DB5081A99D198BB84720F5519CDCB4FE3B74B3C2CE9C`, the staged and source NativeAOT payload SHA-256 was `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, the runtime archive was `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`, OVMF code was `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, OVMF vars were `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`, and QEMU was `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`. The frozen runner and validator hashes were `949FDA8FBD32B1E1EDD5046F01FCDA7BACC3ABF35A43EEB37935028A073A0BF7` and `7050B57B1E792D28B6750FEE8A8DE0A7AC007CDE16C7999D875F540A84E70999`.

The required sequence used fresh QEMU PIDs `17692`, `9524`, and `16252`. Run 1 ended at `SLIST_IMPORT_FUNCTIONAL=1` (serial length 2703); run 2 ended at the second `CRT_ONEXIT_INITIALIZED_OK` (length 2581); run 3 reached `UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e` (length 3370) but did not emit the final QPC/allocation summary. The validator rejected all three, so the SLIST milestone is not fully closed by the requested three-run criterion.

A separate fresh probe `slist-final-single-probe-20260729-200400-run1`, PID `8408`, completed with the exact sequence, `PE_IMPORT_FUNCTIONAL=23`, `PE_IMPORT_FAILFAST=101`, `UNRESOLVED_REQUIRED_IMPORTS=0`, `QPC_REGRESSIONS=0`, zero allocation-context pointer/limit, and the `_initterm_e` boundary. The six evidence-pipeline controls all rejected their intentionally incomplete or mutated inputs. The retained complete disabled control is `artifacts\slist-final-20260729-100119-060\disabled\time-contract-runs-20260729-101316-870\time-contract-20260729-101317-171-run1.serial.log`; it reports 22/102 imports, stops at `KERNEL32.dll!InitializeSListHead`, has no SLIST success marker, and reports zero QPC regressions and zero allocation context. Fresh disabled retries were incomplete due the same QEMU/guest shutdown condition and are not substituted for the retained control.

The harness diagnosis is supported by file-backed events and QEMU monitor captures: stdout/stderr remained empty, serial files stopped growing, QEMU CPU time stopped, and monitor queries still succeeded; QEMU reported `VM status: paused (shutdown)` with `HLT=0` and reset-vector RIP. The collection harness therefore preserved and rejected incomplete evidence rather than losing a complete summary to terminal truncation. No allocation, GC initialization, managed-thread registration, or general SLIST mutation is claimed. The next milestone remains `_initterm_e`.

## Final SLIST evidence closure (2026-07-29)

This pass began on branch `main`, HEAD `c7eac442d580c178e59480a05f8dd573c5611c6e`, upstream `origin/main`, with a clean worktree. The initial execution artifact set was the retained `slist-initialize-final-20260729-195900` manifest: loader `333F110626390045D8E9DB5081A99D198BB84720F5519CDCB4FE3B74B3C2CE9C`, NativeAOT payload/source `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, runtime archive `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`, OVMF code `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, OVMF vars `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`, startup script `36735816647B797ACC483C75D12D7768215A9379B3428D4901B2B03C5ED36786`, QEMU `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`, runner `949FDA8FBD32B1E1EDD5046F01FCDA7BACC3ABF35A43EEB37935028A073A0BF7`, and validator `7050B57B1E792D28B6750FEE8A8DE0A7AC007CDE16C7999D875F540A84E70999`.

The prior apparent TCG stall was guest-side: QEMU monitor evidence showed `paused (shutdown)`, stopped CPU time, `HLT=0`, and a reset/triple-fault state. QEMU debug logs recorded hardware IRQ `0x20` entering the loader's 32-entry replacement IDT, followed by `#GP`/`#DF`; a second probe exposed the un-packed `IDTR` ABI as the reason the replacement table itself was loaded at a corrupt base. This was not PowerShell display truncation, stale-log reuse, competing readers, a short timeout, or lost pipe output. The minimal correction preserved the full firmware 256-vector IDT, overrode only exception vectors `0..31`, and declared `IDTR` packed. The SLIST implementation was not changed.

The final immutable artifact set is `artifacts\slist-final-validation-20260729-corrected3` and the three-run evidence is `evidence\generated\slist-final-20260730-immutable`. Its execution hashes are:

| Artifact | SHA-256 |
| --- | --- |
| EFI loader | `2EEBCD284F6D2E5AD1526EB15FA4AF6483E7B1FE9D17A448720A289FF64B0362` |
| NativeAOT payload and source | `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` |
| Runtime archive | `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` |
| OVMF code / vars template | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` / `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E` |
| ESP startup script | `36735816647B797ACC483C75D12D7768215A9379B3428D4901B2B03C5ED36786` |
| QEMU executable | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |
| Validation runner / evidence validator | `949FDA8FBD32B1E1EDD5046F01FCDA7BACC3ABF35A43EEB37935028A073A0BF7` / `2B40930C75A70712F1882A199EF733773E5D516648AA6711E3C2BEEDB4A910FD` |

All three QEMU processes were fresh and used identical hashes. Run IDs and complete terminal data are:

| Run | PID | Exit | Serial bytes | Boundary | QPC summary |
| --- | ---: | ---: | ---: | --- | --- |
| `slist-final-20260730-immutable-run1` | `17256` | `0` | `3419` | `_initterm_e` | count `1`, first=last `0x23060`, regressions `0` |
| `slist-final-20260730-immutable-run2` | `660` | `0` | `3419` | `_initterm_e` | count `1`, first=last `0x1D51B`, regressions `0` |
| `slist-final-20260730-immutable-run3` | `15344` | `0` | `3419` | `_initterm_e` | count `1`, first=last `0x1E6EE`, regressions `0` |

Each marker sequence is `NATIVEAOT_STARTUP_OK -> FILETIME_CONVERSION_OK -> QPC_OK -> CRT_ONEXIT_INITIALIZED_OK -> CRT_ONEXIT_INITIALIZED_OK -> SLIST_HEAD_INITIALIZED_OK -> api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e`, with `PE_IMPORT_FUNCTIONAL=23`, `PE_IMPORT_FAILFAST=101`, `UNRESOLVED_REQUIRED_IMPORTS=0`, `TIME_CONSUMER_PHASE=0x18`, zero TLS allocation limit/pointer, `MANAGED_THREAD_REGISTERED=0`, `ALLOCATION_CONTEXT_VALID=0`, and the final QPC summary. The disabled implementation control `slist-disabled-final-20260730-run1` (PID `18868`) passed the disabled validator, stopped at `KERNEL32.dll!InitializeSListHead`, emitted no SLIST success marker, and retained the same zero-allocation/QPC summary shape.

The evidence-pipeline controls all passed: truncated log, missing final summary, stale run ID, hash mismatch, duplicate process evidence, and mutated `GXOS_NET10:SLIST_HEAD_INITIALIZED_OX` were rejected for their intended reasons. Host vectors and the intentionally wrong-layout compile control also passed. The SLIST initialization milestone is fully closed; the next boundary remains `_initterm_e`. No commit or push was performed.

## Final `_initterm_e` evidence closure (2026-07-30)

This pass began on branch `main`, HEAD `c66dcedb5a15fd832965712e0adb7cff4be74cf5`, upstream `origin/main`, with a clean worktree. The SLIST closure commit and immutable evidence were preserved. A fresh disposable baseline run was captured under `evidence\generated\initterm-e-baseline-20260730`; it retained the FILETIME/QPC/QPF, both CRT on-exit, and SLIST markers, then stopped at `api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e` with the pre-change import treatment `23` functional / `101` fail-fast and zero unresolved required imports.

The narrow implementation was built in `artifacts\crt-initterm-e-build-20260730`. Its loader SHA-256 is `DCC5A21797FDA0F5FB0470EBD51D9A93387436E6E278CDEE587FFA03C2E615C4`; the NativeAOT payload remains `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`. The final immutable evidence root is `evidence\generated\crt-initterm-e-final-20260730-immutable-v4`; the validator SHA-256 is `0C4D297A7990A59C46792BC7ACDEAEB7CF2116307900373719D2F930A7770126` and the runner SHA-256 is `D0157ABA681864810ECF4432C79D415474DE0A7B9B367B6AFB049A5148FC2B98`.

The exact NativeAOT caller is preferred `0x1800775bb` in the helper beginning at `0x180077550`, through `_initterm_e` IAT RVA `0x7e380`. With relocated `IMAGE_BASE=0x0000000005479000`, the runtime return address is `0x00000000054F05C0`, RCX/first is `0x00000000054F74D0`, and RDX/last is `0x00000000054F74D8`. The exclusive range is one eight-byte pointer in `.rdata` RVA `0x7e4d0`; its raw value is null and no relocation targets either slot.

The current import treatment is `24` functional / `100` fail-fast, `124` symbols, and `UNRESOLVED_REQUIRED_IMPORTS=0`. The actual census is one entry, one null, zero non-null, zero invoked callbacks, and zero failures. Each of the three fresh final-hash QEMU runs completed with exit `0`, a 4,320-byte serial log, unique PID (`13460`, `18140`, `18128`), `CRT_INITTERM_E_RESULT=0`, `CRT_INITTERM_E_OK`, and the next boundary `api-ms-win-crt-runtime-l1-1-0.dll!_initterm`. Each retained `QPC_COUNT=1`, `QPC_REGRESSIONS=0`, `TLS_ALLOC_LIMIT=0`, `TLS_ALLOC_PTR=0`, `MANAGED_THREAD_REGISTERED=0`, and `ALLOCATION_CONTEXT_VALID=0`. The existing `GC_STARTUP_BEGIN` marker is only the loader trace boundary; no GC-advanced/heap initialization evidence was observed.

The focused host suite passed all empty/null/order/ABI/failure/exclusive-end/range/target/guard/mutation/duplicate/no-allocation vectors and the no-external-reference check. The evidence pipeline passed disabled implementation, marker mutation, empty-table, null-entry, failing-initializer, reversed-range, out-of-image, noncanonical-target, inclusive-end, truncated-evidence, missing-summary, stale-log, duplicate-process, and hash-mismatch controls. The disabled QEMU root is `evidence\generated\crt-initterm-e-disabled-20260730-v2`; it passed in Disabled mode, retained `23` / `101`, emitted no iterator markers, and stopped at the original `_initterm_e` boundary.

No callback was executed in QEMU because the actual NativeAOT table is empty. Host callbacks prove the Microsoft x64 ABI, forward order, null skipping, exact first-error propagation, and exclusive-end behavior. `_initterm`, general CRT startup, C++ initializers, allocation, GC startup, managed-thread registration, teardown, and SLIST mutation remain out of scope. No commit or push occurred.

## Final `_initterm` evidence closure (2026-07-30)

Baseline was `main` at `a54b64eb07808b50ace4ee7c54ee655a6e90bc27`, upstream `origin/main`, with a clean worktree; the committed `_initterm_e` milestone was preserved. A fresh pre-routing run stopped at `api-ms-win-crt-runtime-l1-1-0.dll!_initterm` after the proven `_initterm_e` boundary.

The immutable artifact set is `artifacts\crt-initterm-final-build-20260730`. Its execution-relevant hashes are: loader `7FF2C0082E570D4021CA6B63AFA0132222AD46DBCBDEFE7A833AD6C7DEBEA655`, payload `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, runtime archive `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`, OVMF code `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, OVMF vars template `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`, QEMU `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`, runner `D00B998A03E7F9135BD75A6B1A4E451B752844840E43E9EFE65384ECFA4A9D43`, and validator `9E008DC5B6E13F722E50C5FB0F3199968F1453F347E39CF6990F5594A7FC3C66`.

Three fresh counted runs passed: `crt-initterm-final-20260730-immutable-v2-run1` (PID `17088`), `run2` (PID `4252`), and `run3` (PID `22780`); each had serial length `11482`, exit `0`, unique fresh OVMF variables, and the same artifact snapshot. Each proved a nine-entry range, one null, eight non-null, eight invoked, eight returned, `_initterm` completion, and the next `strcmp` boundary. The negative-control bundle `evidence\generated\crt-initterm-negative-controls-20260730-v2` passed all intended rejection controls. No commit or push occurred.

## Final `strcmp` evidence closure (2026-07-30)

This pass began at the committed `_initterm` baseline: branch `main`, HEAD `1692ccdca007edc5bb5f9365513bc9bceaa6ae99`, upstream `origin/main`, with a clean worktree. The exact caller investigation recorded one live call per run: `strcmp("gcServer", "gcConservative")`, both immutable `.rdata` strings, result `+1`, and preferred return site `0x18003EB24` / runtime return site `0x00000000054B7B24`. A temporary fail-fast-only probe captured the bounded arguments and was removed before the implementation.

The final enabled profile is 26 functional / 98 fail-fast / 0 unresolved imports. Only `api-ms-win-crt-string-l1-1-0.dll!strcmp` is routed; `strlen` remains fail-fast. The focused host suite passed all required vectors and negative controls, and the standalone core object has no unresolved external references. The immutable evidence is `evidence\generated\crt-strcmp-final-20260730-immutable`: loader `585EDDB8D7A16F1CE98B881E4BCD124C0C4B6E001AA3BDA89FB2DF9D99229AEC`, payload `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, runtime archive `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`, QEMU `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`.

| Run | PID | Exit | Bytes | Compared strings / result | Imports | QPC | State | Boundary |
| --- | ---: | ---: | ---: | --- | --- | --- | --- | --- |
| `crt-strcmp-final-20260730-immutable-run1` | 23404 | 0 | 12137 | `gcServer` / `gcConservative` / `+1` | 26 / 98 / 0 | count 2, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `strlen` |
| `crt-strcmp-final-20260730-immutable-run2` | 2376 | 0 | 12137 | `gcServer` / `gcConservative` / `+1` | 26 / 98 / 0 | count 2, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `strlen` |
| `crt-strcmp-final-20260730-immutable-run3` | 21892 | 0 | 12137 | `gcServer` / `gcConservative` / `+1` | 26 / 98 / 0 | count 2, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `strlen` |

The disabled control `evidence\generated\crt-strcmp-disabled-20260730` used PID `19500`, exit `0`, 11482 bytes, 25 / 99 / 0 imports, and stopped at the original `strcmp` boundary with no implementation marker. The new deepest boundary is `api-ms-win-crt-string-l1-1-0.dll!strlen`. No commit or push occurred.

## Final `strlen` evidence closure (2026-07-31)

This pass began on branch `main`, HEAD `eaddb75a920dfa3abbcba512cfabcf854cc822fd`, upstream `origin/main`, with a clean worktree. A fresh disposable baseline run was captured under `evidence\generated\crt-strlen-baseline-fresh-20260731`; it retained the prior `strcmp` result and stopped at `api-ms-win-crt-string-l1-1-0.dll!strlen` with 26 functional / 98 fail-fast imports and zero unresolved required imports. No commit or push occurred during this pass.

The checked core contract is in `src/Gate4Harness/crt_strlen.c` / `crt_strlen.h`. The immutable positive artifact set is `evidence\generated\crt-strlen-final-20260731-immutable-v3`, built under `artifacts\crt-strlen-build-20260731`. Its execution-relevant hashes are: loader `B0FA9D7587D73154DF52F769205B6F4B632698ECF90CDFC246BBA4257023B191`, NativeAOT payload `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`, runtime archive `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`, OVMF code `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, OVMF vars template `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`, QEMU `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`, runner `8429EAC7EB62CE8BDE5E98A674B7843FA9B778C6F4C6EB20287360CB963FC489`, and validator `FF44995656AAD1CD52D68E1FEEF376EED73AEB06C59BF1747AA7E7368D3361A0`.

The enabled profile is 27 functional / 97 fail-fast / 0 unresolved. The exact IAT slot is `0x7d3e8`; the static call is preferred `0x18003dba0`, and the runtime return site is `0x00000000054B8BA5`. The input is the relocated read-only `.rdata` string `gcServer` at `0x0000000005513498`; the wrapper validated the mapped region `0x00000000054F8000..0x0000000005524E00`, returned length `8`, and identified the terminator at `0x00000000055134A0`. The next boundary is `KERNEL32.dll!GetEnvironmentVariableW`.

| Run | PID | Exit | Bytes | `strlen` result | Imports | QPC | State | Boundary |
| --- | ---: | ---: | ---: | --- | --- | --- | --- | --- |
| `crt-strlen-final-20260731-immutable-v3-run1` | 26236 | 0 | 13704 | `gcServer` / `8` | 27 / 97 / 0 | count 2, first `0x1D37F`, last `0x39F0A`, delta `0x1CB8B`, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `GetEnvironmentVariableW` |
| `crt-strlen-final-20260731-immutable-v3-run2` | 20464 | 0 | 13704 | `gcServer` / `8` | 27 / 97 / 0 | count 2, first `0x1D412`, last `0x38C70`, delta `0x1B85E`, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `GetEnvironmentVariableW` |
| `crt-strlen-final-20260731-immutable-v3-run3` | 23172 | 0 | 13704 | `gcServer` / `8` | 27 / 97 / 0 | count 2, first `0x1E0F9`, last `0x39D33`, delta `0x1BC3A`, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `GetEnvironmentVariableW` |

The disabled control is `evidence\generated\crt-strlen-disabled-20260731-immutable`; its three fresh runs passed with 26 / 98 imports, no `CRT_STRLEN_*` implementation marker, and the original `api-ms-win-crt-string-l1-1-0.dll!strlen` boundary. The host suite, prior `strcmp`/`_initterm`/`_initterm_e`/SLIST regression suites, and the negative evidence pipeline all passed. The first allocation, GC initialization, managed-thread registration, and broad CRT/string support remain unproven.

## Final `GetEnvironmentVariableW` evidence closure (2026-07-31)

This pass began on branch `main`, HEAD `69dfcfd70b4ce6bae35672e4539eaaac0b31774d`, upstream `origin/main`, with a clean worktree and the prior `strlen` milestone committed. A fresh pre-change run under `evidence\generated\getenv-baseline-prechange-20260731` reproduced `KERNEL32.dll!GetEnvironmentVariableW` after `strcmp` and `strlen`; no prior evidence was overwritten.

The checked implementation is in `src/Gate4Harness/platform_environment.c` / `platform_environment.h`. The enabled immutable artifact set is `evidence\generated\getenv-final-20260731-immutable`, built from `artifacts\getenv-build-20260731`. Its execution-relevant hashes are: EFI loader `968F09CC3B2D44D8FB242FE556BC59214A64F659893940B664D1ECEFD20789D3`, NativeAOT image `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`, runtime archive `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`, OVMF code `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, OVMF vars `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`, QEMU `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`, runner `F5BEBE70AACEA13168F7633CDAF0293F11448D5ACD42FDD5AF11D35F0DC2AE44`, and validator `307598AC72876E7C4C29A86F3D3DF64BFC5D4C62A331D7CE5D704E87FB74012E`.

The live caller was the NativeAOT GC-configuration helper: preferred direct call `0x18003e196`, runtime call `0x00000000054B9196`, and runtime return `0x00000000054B919B`. Every run queried exactly one variable, `DOTNET_gcServer`, with `lpName=0x0000000007E64B40`, `lpBuffer=0x0000000007E64B10`, and `nSize=0x11`. The return was `0`; `GetLastError` changed from `0` to `203`; the caller selected its absent/fallback path; no second call or parse followed.

| Run | PID | Exit | Bytes | GetEnv call | Return / last error | Imports | QPC | Runtime state | Boundary |
| --- | ---: | ---: | ---: | --- | --- | --- | --- | --- | --- |
| `getenv-final-20260731-immutable-run1` | 8648 | 0 | 15500 | `DOTNET_gcServer`, `nSize=17`, non-null buffer | `0` / `203` | 28 / 96 / 0 | count 2, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `_stricmp` |
| `getenv-final-20260731-immutable-run2` | 13476 | 0 | 15500 | `DOTNET_gcServer`, `nSize=17`, non-null buffer | `0` / `203` | 28 / 96 / 0 | count 2, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `_stricmp` |
| `getenv-final-20260731-immutable-run3` | 7100 | 0 | 15500 | `DOTNET_gcServer`, `nSize=17`, non-null buffer | `0` / `203` | 28 / 96 / 0 | count 2, regressions 0 | TLS 0/0; managed 0; allocation context 0; GC heap 0 | `_stricmp` |

The disabled three-run control is `evidence\generated\getenv-disabled-20260731`; it retained 27 / 97 / 0 imports, emitted no GetEnv implementation marker, and stopped at the original GetEnvironmentVariableW boundary. The negative-control bundle `evidence\generated\getenv-negative-controls-20260731-final` passed disabled routing, missing-variable, existing-variable, empty-variable, NULL size-probe, insufficient-buffer, stale-evidence, marker-mutation, duplicate-PID, and artifact-hash-mismatch checks. Host regressions for `strlen`, `strcmp`, `_initterm`, and `_initterm_e` passed. No commit or push occurred.

## Final `_stricmp` evidence closure (2026-07-31)

This pass began with a fresh disposable pre-change QEMU reproduction under `evidence\generated\stricmp-baseline-20260731-v2`. It stopped at the exact `api-ms-win-crt-string-l1-1-0.dll!_stricmp` import with the preceding `GetEnvironmentVariableW("DOTNET_gcServer")` lookup still missing. The baseline was preserved and no reference repository was changed.

The final positive artifact set is `evidence\generated\crt-stricmp-final-20260731-immutable-v4`; the disabled control is `evidence\generated\crt-stricmp-disabled-20260731-v3`. The three positive runs all validate the same immutable source/loader/payload/runtime/OVMF/QEMU/runner/validator snapshot, use unique fresh QEMU PIDs, complete cleanup, and stop at `KERNEL32.dll!GetSystemInfo` immediately after the `_stricmp` summary. Each run records:

| Field | Result |
| --- | --- |
| import census | `29` functional / `95` fail-fast / `0` unresolved |
| `_stricmp` calls | `885` successful / `0` failures |
| result categories | `2` equal / `566` less / `317` greater |
| total compared bytes | `0x362A` |
| longest compared prefix | `0x15` |
| QPC | count `2`, regressions `0` |
| runtime state | TLS allocation `0/0`, managed thread `0`, allocation context `0`, GC heap `0`, managed allocations `0` |
| next boundary | `KERNEL32.dll!GetSystemInfo` |

The host suite and evidence pipeline passed the focused contract vectors, no-external-reference check, disabled-route control, marker mutation, stale evidence ID, duplicate PID, and artifact-hash mutation. No later API was implemented, and no allocation or GC readiness is inferred.

The positive run records are PIDs `25028`, `24284`, and `26960`, each with `2,113,224` serial bytes and exit `0`. The disabled control records PIDs `13764`, `25892`, and `27332`, each with `15,500` serial bytes and exit `0`. Positive execution hashes are: loader `64E46560A00EA3C04F59E1BBC239991262D02AB6F2E486992ADBC1CD3B470DBC`, payload `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`, runtime archive `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`, OVMF code `33090CC07675BA519D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`, QEMU `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`, runner `03BCDA7D2D835C3CA2F7C724D6D5E206BFA6B6DA10EF6971F0D1DBBF8C70A6D1`, and validator `9F481DEEFFEC827BF7238534F96F5E7CEA68B4D7957FF57836491BAB3E7F84C2`.

## `GetSystemInfo` evidence-closure result (2026-07-31)

This pass began on `main` at HEAD `089dafc7613ec27b6226bc68b6a08c9934ced08c`, upstream `origin/main`, with a clean worktree. A fresh correct-payload baseline was reproduced before enabling the route: the `GetSystemInfo` boundary was observed at runtime, with static IAT RVA `0x7e260`, preferred call `0x18004379f`, and return address `0x1800437a5`. The baseline payload hash was `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`.

The checked core is `src/Gate4Harness/platform_system_info.c` / `platform_system_info.h`; the exact wrapper is in `gate4_loader.c`, gated by `GXOS_ENABLE_SYSTEM_INFO`. Host tests passed for layout, complete poison overwrite, guards, repeatability, destination/range validation, invalid facts, configured snapshots, MS ABI, and zero external references. The intentional wrong-layout compile test failed at the static assertion. Existing `_stricmp`, environment, `strlen`, `strcmp`, `_initterm_e`, and `_initterm` host regression suites also passed.

The immutable positive gate is `artifacts/getsysteminfo-final-20260731`; evidence is `evidence/generated/getsysteminfo-final-20260731-immutable-v2`. The loader hash is `0CFFF09C12BC567615540CDB2CDE01A8327342E3E316FED2957D3F7F78FAF931`; payload `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379`; runtime archive `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311`; OVMF code `33090CC07675BA519D0F9F1E84BF5176B33BCBFA9ACAC522961150CDB6DBB2A`; OVMF vars `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`; QEMU `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02`. Three fresh runs used PIDs `20632`, `3732`, and `25688`, each with serial length `2115119`, clean cleanup, stable artifact fingerprints, `30 / 94 / 0` imports, `_stricmp=0x375`, QPC count `2`, zero regressions, and zero allocation/GC state. All reached `KERNEL32.dll!GetNumaHighestNodeNumber` after `GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE`.

The disabled three-run control is `evidence/generated/getsysteminfo-disabled-20260731-immutable-v2`, with PIDs `25452`, `17108`, and `26976`, loader hash `174E4FC67CD788B46574016CCB17DC3CC76043CAC632E5B1B6927858757B2275`, `29 / 95 / 0` imports, and the exact GetSystemInfo fail-fast boundary. The marker-mutation negative control is `evidence/generated/getsysteminfo-marker-mutation-20260731`; it emitted `GETSYSTEMINFO_OX`, never `GETSYSTEMINFO_OK`, and still reached the authentic next boundary. No commit or push occurred.

The runner-frozen positive evidence superseding the earlier v2 attempt is `evidence/generated/getsysteminfo-final-20260731-immutable-v3`, with PIDs `3164`, `26008`, and `26176`, serial length `2115119` per run, loader `0CFFF09C12BC567615540CDB2CDE01A8327342E3E316FED2957D3F7F78FAF931`, runner `ACC92AD93262D714450F14C3A666B94B1224EC63C59C839E6A71695B6A5B5BD6`, and validator `0CE9E28EBBAFA7039A6570D7E2268729134155E1538A70BD914D50E929350EB4`. All three validate successfully against the final runner revision.

## Final `GetNumaHighestNodeNumber` evidence closure (2026-08-01)

This pass began from the committed `GetSystemInfo` milestone at `14d865eeb19e97a627824671104cf377cdda5bb9`, with branch `main`, upstream `origin/main`, and a clean initial worktree. A fresh baseline under `evidence\generated\getnumahighest-baseline-20260801` reproduced the exact `KERNEL32.dll!GetNumaHighestNodeNumber` dependency. The baseline used QEMU PID `23940`, produced `2,115,119` serial bytes, and was preserved.

The final immutable positive artifact is `artifacts\getnumahighest-final-v2-20260801`; the evidence root is `evidence\generated\getnumahighest-final-20260801-immutable-v2`. The execution-relevant hashes are:

| Artifact | SHA-256 |
| --- | --- |
| EFI loader | `8A83363EAA6CB4167E2BF7898310C229BD48FFF9E5E6CAA6F6C2753B3BFBF230` |
| NativeAOT payload | `6D1306C8E1DE9DDEADAC478171418B32841E1E683F3DBCEB8191BDBCB48A1379` |
| runtime archive | `DBA78CC0C6747E2E0CF51894F1492A70ECD08513151D9473C04048CD7B9D9311` |
| OVMF code | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` |
| QEMU | `A930E028F93D0FA47E4D58BDAD2432F7466DC2B6AF0AE376F77EF7A298FFDD02` |
| runner / validator | `786E2B86ECA7E070E08B7903080AE26C9C2F97D278E0822DE88A5C28F370A149` / `7197D27CCF6D2BB8518B14C301C564C97A19759219B8EF5FEF72B02356BAC191` |

The three fresh positive runs passed the immutable validator and reached `KERNEL32.dll!GetProcessGroupAffinity`:

| Run | PID | Serial bytes | Exit | Cleanup |
| --- | ---: | ---: | ---: | --- |
| run1 | `22788` | `2,117,419` | `0` | complete |
| run2 | `13324` | `2,117,419` | `0` | complete |
| run3 | `20836` | `2,117,419` | `0` | complete |

Each run proved `31 / 93 / 0` functional/fail-fast/unresolved imports, `_stricmp=0x375`, QPC count `2`, zero QPC regressions, and zero allocation-context, managed-thread, managed-allocation, or GC-heap state. The wrapper observed the exact four-byte output at runtime pointer `0x7e64c80` in approved writable memory, with output before/after `0`, facts `1` processor / `1` domain / highest `0`, `BOOL=1`, status `OK`, and last error preserved `0xcb -> 0xcb`. The caller read the output, selected `SUCCESS_BOOLEAN_OUTPUT_ZERO_NON_NUMA_FALLBACK`, derived domain count `0`, and made no subsequent NUMA call.

The one-run success experiment (`evidence\generated\getnumahighest-success-final-20260801`, PID `18304`) reproduced the same success branch. The one-run forced failure experiment (`evidence\generated\getnumahighest-failure-final-20260801`, PID `16884`) returned `BOOL=0`, status `UNSUPPORTED_TOPOLOGY`, preserved output `0`, changed last error `0xcb -> 0x32`, did not read the output, and selected `FAILURE_NON_NUMA_FALLBACK`. The one-run disabled control (`evidence\generated\getnumahighest-disabled-final-20260801`, PID `23912`) retained the original NUMA fail-fast boundary and emitted no wrapper marker.

The negative-control pipeline passed marker mutation, truncated evidence, stale run identity, duplicate PID, artifact-hash mismatch, highest-node/count confusion, zero-node confusion, success without output write, failure with claimed output, wrong output width, and unexpected last error. This is a bounded platform closure only; the next authentic dependency is `GetProcessGroupAffinity`, and first allocation/GC remains unproven. No commit or push occurred.

## Final `GetProcessGroupAffinity` evidence closure (2026-08-01)

This pass began from the committed `GetSystemInfo`/NUMA history on branch `main`; no commit or push was performed. The final artifact is `artifacts\getprocessgroup-final3-20260801`, and immutable evidence is `evidence\generated\getprocessgroup-final3-20260801-immutable-v4`. The EFI loader SHA-256 is `4EA2A456A8175D06DB73E3346DA0744E498A680BA0255AAB8B30AD1CF8F4994F`; the unchanged NativeAOT payload SHA-256 is `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`.

Three fresh positive runs passed with unique PIDs `6032`, `16084`, and `14692`, serial length `229,967` each, unique serial hashes, identical artifact fingerprints, exit `0`, and complete cleanup. The enabled census is `32 / 92 / 0` functional/fail-fast/unresolved. All runs record `_stricmp=0x375` calls/successes with zero failures, census hash `0x9E89C714CD4695E6`, QPC count `2`, zero regressions, and zero TLS allocation, managed-thread, GC-heap, allocation-context, and managed-allocation markers.

The live trace is exact: IAT RVA `0x7d2a0`, preferred call `0x1800436da`, runtime image base `0x547b000`, runtime IAT `0x54f82a0`, runtime call `0x54be6da`, caller start `0x54be650`, current-process pseudo-handle `0xffffffffffffffff`, `GroupCount=0`, `GroupArray=NULL`, required/output count `1`, count readable/writable `1/1`, groups written `0`, BOOL `0`, status `INSUFFICIENT_BUFFER`, last error `0xcb -> 0x7a`, caller count-read `1`, array-read `0`, retry `0`, and no subsequent group API call. The next authentic boundary is `KERNEL32.dll!GetProcessAffinityMask`.

The disabled control is `evidence\generated\getprocessgroup-disabled-final-20260801-control-v2`, with `31 / 93 / 0` and the original process-group fail-fast boundary. Host contract/regression suites passed; the marker-mutation build compiled; and the evidence validator rejected seven mutation controls: marker, truncation, stale identity, duplicate PID, artifact hash, capacity result, and last error. This is a bounded process-group capacity closure only; first allocation and GC remain unproven.

## Final `GetProcessAffinityMask` evidence closure (2026-08-01)

The exact Microsoft x64 `GetProcessAffinityMask` route is recorded under `evidence\generated\getprocessaffinity-final-20260801-immutable-v2`. Three fresh runs use one immutable artifact set, each emits 241,507 serial bytes, passes cleanup and the bounded validator, records two live affinity calls, returns process/system masks `0x1`/`0x1`, and advances to `KERNEL32.dll!QueryInformationJobObject`. The enabled census is `33 / 91 / 0`; the disabled control is under `evidence\generated\getprocessaffinity-disabled-20260801-control-v3` and stops at the original affinity boundary.

The affinity host suite passes 57 tests, the forced-failure experiment proves unchanged outputs and both caller fallbacks, and the negative pipeline rejects ten evidence mutations. No commit or push occurred; GC, allocation context, managed-thread registration, and managed allocation remain zero.

## `QueryInformationJobObject` evidence-closure result (2026-08-01)

The exact query contract is closed under [KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md](KERNEL32_QUERYINFORMATIONJOBOBJECT_BOOTSTRAP.md). The live class is `JobObjectCpuRateControlInformation` (`15`), the output structure is eight bytes, and the fifth Microsoft x64 argument is proven at `entry RSP + 0x28` with a null value. The current guideXOS snapshot has no associated job, so the route returns `BOOL=0`, `ERROR_ACCESS_DENIED`, and no output or return-length writes; the processor-count caller takes its no-job fallback and the next dependency is `KERNEL32.dll!GetModuleHandleW`.

The immutable positive evidence is `evidence\generated\queryjobobject-final-20260801`, with PIDs `9780`, `19916`, and `8916`, 245,966 serial bytes per run, unique serial hashes, one artifact fingerprint, complete cleanup, and `34 / 90 / 0` imports. The disabled control is `evidence\generated\queryjobobject-disabled-20260801` (`33 / 91 / 0`). The real-Windows host reference is `artifacts\query-information-job-object-host-reference-20260801`; the no-limit and active-limit synthetic experiments are retained separately. The negative evidence pipeline rejected all six mutation controls, and prior focused regressions passed.

## `GetModuleHandleW` evidence-closure result (2026-08-01)

The exact import route is documented in [KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md](KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md). A fresh baseline stopped at `GetModuleHandleW`; the enabled route records one non-null `ntdll.dll` call, returns `NULL`/`126` because ntdll is not mapped, and reaches `GetProcAddress`. The disabled route retains the original boundary. The final positive artifact set is `artifacts\getmodulehandlew-final-20260801`, and the three-run immutable evidence is `evidence\generated\getmodulehandlew-final-20260801-immutable` with `35 / 89 / 0` imports. Every run retains the prior 885-call `_stricmp` aggregate, QPC count `2`, zero regressions, zero TLS allocation context, zero managed-thread registration, zero GC heap usability, zero allocation context, and zero managed allocations. The exact per-run hashes and PIDs are recorded in [KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md](KERNEL32_GETMODULEHANDLEW_BOOTSTRAP.md).

## Final `GetProcAddress` evidence closure (2026-08-01)

This pass began from the committed GetModuleHandleW contract on branch `main`; no commit or push was performed. The implementation is limited to the Microsoft x64 `KERNEL32.dll!GetProcAddress` contract required by the current NativeAOT startup path. The exact live argument is `HMODULE=NULL` and `lpProcName="RtlDllShutdownInProgress"`; the checked result is `NULL` with `ERROR_PROC_NOT_FOUND` (`127`), and the caller selects `FAILURE_NULL_OPTIONAL_FALLBACK`.

Positive immutable evidence is `artifacts\getprocaddress-final-v3-20260801-immutable-v2`: loader SHA-256 `C692F38E990ACB0A9A69E5F059520ABC6FB43B23E50EF5F848A2F71CA2845593`, unchanged NativeAOT payload SHA-256 `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`, three passes, PIDs `19136`, `17308`, `27284`, and serial length `253581` per run. The disabled immutable control is `artifacts\getprocaddress-final-disabled-v7-20260801-immutable-v2`, with loader SHA-256 `B005C47CF37EDC2D2288C48B5F07FF7DC046D57C2BAB91863A36B90E43CC1409`, PIDs `28968`, `26340`, `27204`, serial length `249669`, and the authentic `GetProcAddress` fail-fast boundary.

The focused host suite, Windows host-reference probe, all prior host regressions, and the final negative pipeline passed. The synthetic-pointer and wrong-error runs are investigation-only and explicitly ineligible as positive contract evidence. Final traces retain zero QPC regressions, zero GC/heap initialization, zero allocation context, zero managed-thread registration, and zero managed allocations. The next authentic dependency is `_register_onexit_function`; first allocation remains unproven.

## Final `_register_onexit_function` evidence closure (2026-08-02)

This pass began on branch `main`, at HEAD `034c04a15c6dab8c824716ef8b8d56c8a6e0ebee`, tracking `origin/main`, with a clean worktree. A fresh pre-change run under `artifacts\register-onexit-boundary-baseline-20260801-fresh` reproduced the exact next import after the committed `GetProcAddress` closure. No commit or push was performed.

The final enabled artifact is `artifacts\register-onexit-final-v1-20260802`, with loader SHA-256 `4B8F505AE86A2FF6232CB8C570CB499F6439ED3068E915600A1E5D57836971A2` and unchanged NativeAOT payload SHA-256 `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`. Immutable evidence is `artifacts\register-onexit-final-evidence-v5-20260802`; its runner/validator manifest freezes the loader, payload, runtime archive, OVMF, QEMU, startup script, source, runner, and validator hashes before and after each run.

| Run | QEMU PID | Serial bytes | Result |
| --- | ---: | ---: | --- |
| `register-onexit-final-evidence-v5-20260802-run1` | `15740` | `258443` | pass; serial hash `ACB47433CB2FB0C21F9660D849F9649D0454B95C0C28F15E99623F7B08D8BDF8` |
| `register-onexit-final-evidence-v5-20260802-run2` | `27740` | `258443` | pass; serial hash `DE5032C9DF9E6708CA12E96F17B7FA8E91C397800E8CD32FAD2AB3B96BF9C3F0` |
| `register-onexit-final-evidence-v5-20260802-run3` | `29220` | `258443` | pass; serial hash `3DDC36A8A97E2FB24DE056264C5268BCA15993E48D9EF2C5F24FFC52AC63EE6C` |

All three runs validate `37 / 87 / 0` functional/fail-fast/unresolved imports, descriptor `8`, IAT RVA `0x7d358`, preferred call `0x180077e13`, helper range `0x180077df0..0x180077e30`, table RVA `0xb3e78`, callback RVA `0x37bd0`, initialized-table match `1`, decoded `first=last=end=0`, encoded raw-field equality before/after, `GROWTH_REQUIRED`, result `-1`, allocation attempted `0`, callback executed `0`, and zero GC/allocation state. The explicit next dependency is `_recalloc_crt_t(_PVFV,NULL,0x20)`, not a later generic fail-fast import.

The disabled control is `artifacts\register-onexit-disabled-v1-20260802` with loader SHA-256 `5432ED75D7E1616E4EB8E9A4565B33840B041F31DC93CC2A287F8479407B633B`; immutable evidence is `artifacts\register-onexit-disabled-evidence-v3-20260802`. Its three runs pass `36 / 88 / 0`, emit no register wrapper marker, and stop at `UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_register_onexit_function`. The focused existing CRT host suite, new register host suite, and final evidence negative-control pipeline (`artifacts\register-onexit-negative-controls-final-v3-20260802`) pass, including the no-external-reference check. See [CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md](CRT_REGISTER_ONEXIT_FUNCTION_BOOTSTRAP.md).

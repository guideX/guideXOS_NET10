# Evidence ledger

Generated binaries, emulator firmware, logs, and manifests remain under ignored `artifacts\` directories. This ledger records the reproducible source and runtime evidence for Gate 4.

## Repository baseline and scope

| Evidence | Result |
| --- | --- |
| Branch at start of this pass | `main` |
| HEAD at start of this pass | `3f36766ba535f8d3fa545c3724f466d5a27784e0` |
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

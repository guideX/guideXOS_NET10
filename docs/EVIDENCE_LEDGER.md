# Evidence ledger

All paths below are generated and ignored unless they are source or documentation paths. Large binaries, emulator firmware, logs, and manifests are deliberately kept out of source control.

## Repository and reference baseline

| Evidence | Result |
| --- | --- |
| New repository baseline commit | `21ffe77` |
| New repository before implementation | clean worktree on `main` |
| Reference repositories | inspected read-only; no edits, commits, formatting, or regeneration performed. Legacy (`D:\dev\guideX`) and UEFI (`D:\dev\guideXOSUEFI`) were clean at final check. Server (`D:\dev\guideXOSServer`) retained three pre-existing status entries; no operation in this task targeted them. |
| Current repository | source/docs/scripts changed for this milestone; no commit made |

## Gate 1 artifacts

| Path | Size | SHA-256 |
| --- | ---: | --- |
| `artifacts\gate1-brepro-1\gxos-managed-entry-probe.exe` | 732,672 | `230CEBD7158AD164331DB488A3E19C6189DA63C4D186EFB219BA540D5BFDF3D9` |
| `artifacts\gate1-brepro-1\gxos-managed-entry-probe.pdb` | 5,861,376 | `E6FD1FB689D97FE7A628482DC5C8B7D93555C8D43463E5E8FFAFEB1E52917BA5` |
| `artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll` | 729,600 | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| `artifacts\gate1-final-static\gxos-managed-entry-probe.lib` | 2,479,220 | `96D0D421E70C99CCA128D066A573C7EFB97B740217C774C8839F5AC233BBAEB6` |
| NativeAOT map XML | 650,200 | `E38DB968C40F19F427D4AEF64D7BF5B19E3E16B3010F8DA83FD07CFB449899FC` |
| ILC response | 32,127 | `7E33D44C6E1ECF354F732A56565521DD87C086A194C557641882B4FE4232BF85` |
| linker response | 3,052 | `4A0B63F84FA712D4C30556C532C6F1F62C825257B5C78B45DDFBE5C6605C704A` |

Two `/Brepro` executable publishes matched exactly. A host execution of the executable returned `0`; no host console API was used by the managed entry method.

## Gate 3 evidence

Checked evidence directory: `artifacts\gate3-evidence-20260727-192651-108`

| File | Purpose |
| --- | --- |
| `source.manifest.json` | parsed NativeAOT shared artifact |
| `staged.manifest.json` | parsed ESP payload |
| `comparison.json` | fail-closed source/staged comparison |
| `source-pe-report.txt` | full `objdump -p` report |
| `hashes.json` | source and staged artifact hashes |

The comparison hash is `CEBE94DD8138C54AA2E93A564B7B4A810F44D5EA977F71347FB018B836BC432`. Result: `GATE3_COMPARISON=PASS`. The manifest records the current map hash `E38DB968C40F19F427D4AEF64D7BF5B19E3E16B3010F8DA83FD07CFB449899FC`.

## Gate 4 harness

| Artifact | Size | SHA-256 |
| --- | ---: | --- |
| `artifacts\gate4\ESP\EFI\BOOT\BOOTX64.EFI` | 7,943 | `C92E4286AEE275212128C6B6718AF8B0C23A0EAB7BAB8EBD5D0E7B07D3256E1A` |
| `artifacts\gate4\ESP\GXOS\gxos-managed-entry-probe.dll` | 729,600 | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| QEMU x86-64 firmware code image | 3,653,632 | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` |

Harness build command: `tools\Build-Gate4Harness.ps1`. The harness uses GCC freestanding flags, no CRT, no stack protector, no unwind tables, no red zone, a UEFI application subsystem, and an explicit `efi_main` entry.

## Three fresh QEMU runs

Command:

```powershell
& .\tools\Run-Gate4.ps1 -RunCount 3 -TimeoutSeconds 5
```

QEMU version: `QEMU emulator version 11.0.0 (v11.0.0-12122-ga4bb4b10c9)`.

| Run ID | Classification | Artifact hash | Loader hash |
| --- | --- | --- | --- |
| `gate4-20260727-190915-841-run1` | `BLOCKED_IMPORTS_CONFIRMED` | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` | `C92E4286AEE275212128C6B6718AF8B0C23A0EAB7BAB8EBD5D0E7B07D3256E1A` |
| `gate4-20260727-190921-230-run2` | `BLOCKED_IMPORTS_CONFIRMED` | same | same |
| `gate4-20260727-190926-521-run3` | `BLOCKED_IMPORTS_CONFIRMED` | same | same |

Serial logs are under `artifacts\gate4\runs-20260727-190915-783`. Each contains firmware startup, `LOADER_START`, PE read, relocation, export RVA `0x24724`, import count `10`, and `GATE4_BLOCKED_IMPORTS`. None contains the managed success marker.

## Negative static-link evidence

The direct static link experiment using the NativeAOT static library and standard runtime-pack libraries failed with 158 unresolved externals. Evidence is in `artifacts\gate4\static-link-attempt\link.stdout.log`; examples include Windows virtual memory, events, threads/FLS, COM, unwind context, TLS, CRT heap/string/math, `__chkstk`, and C++ allocation operators.

## Gate conclusions

| Gate | Conclusion |
| --- | --- |
| 1 | Passed: reproducible standard .NET 10 NativeAOT artifact. |
| 2 | Passed as a documented census; platform/runtime treatment remains unresolved. |
| 3 | Passed for the provisional direct-PE path and its machine-checked staging identity. |
| 4 | Not passed: harness loads and analyzes the artifact but does not enter managed code. |

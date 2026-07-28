# Managed-entry proof procedure

This is the bounded Gate 4 proof. It uses a fresh QEMU process for every run, an isolated serial log, the exact staged NativeAOT PE, a hash-checked UEFI loader, and a fail-closed import/runtime boundary.

## Build and inspect

```powershell
Set-Location D:\dev\guideXOS_NET10
& .\tools\Build-Gate1.ps1
& .\tools\Build-Gate3Evidence.ps1
& .\tools\Build-Gate4Harness.ps1
```

The intended shared payload is `artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll`. The harness stages it at `ESP\GXOS\gxos-managed-entry-probe.dll`. Gate 3 must report `GATE3_COMPARISON=PASS` before the QEMU step.

## Positive validation

```powershell
$loader = (Get-FileHash .\artifacts\gate4\ESP\EFI\BOOT\BOOTX64.EFI -Algorithm SHA256).Hash
& .\tools\Run-Gate4.ps1 -RunCount 3 -TimeoutSeconds 15 -ExpectedLoaderSha256 $loader
```

The script requires, in order, PE validation, relocation, `PE_IMPORT_DESCRIPTORS=10`, `PE_IMPORT_SYMBOLS=124`, `PE_IMPORT_RESOLVED=124`, `PE_IMPORT_FUNCTIONAL=18`, `PE_IMPORT_FAILFAST=106`, `UNRESOLVED_REQUIRED_IMPORTS=0`, `BEFORE_MANAGED_CALL`, the exact managed marker, a zero return, and `MANAGED_ENTRY_COMPLETE`. It also verifies the artifact and loader hashes in each fresh process. A successful run ends with `GATE4_PROOF=PASSED` and `GATE4_RESULT=MANAGED_ENTRY`.

## Current-source fresh control baseline

The allocation-probe source was rebuilt with the no-allocation branch disabled. Fresh control run `gate4-20260728-063011-635-run1` passed with artifact SHA-256 `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`, loader SHA-256 `8382E6579D2ED3E6E12EC26A86E6DA1683A849A5E01BC26BBBDD98AFFB1E2A71`, QEMU `11.0.0 (v11.0.0-12122-ga4bb4b10c9)`, and firmware SHA-256 `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`. It recorded 10 descriptors, 124 symbols, 18 functional imports, 106 fail-fast imports, zero unresolved imports, the managed marker, return zero, and `MANAGED_ENTRY_COMPLETE`.

The allocation-enabled artifact is a separate negative experiment. Its opt-in startup trace is documented in [ALLOCATION_GC_PROBE.md](ALLOCATION_GC_PROBE.md); it must not be passed to this Gate 4 success validator because it emits `FIRST_ALLOCATION_OK` only after an unproven GC-backed object allocation.

Representative positive serial sequence:

```text
GXOS_NET10:LOADER_START
GXOS_NET10:PE_READ_OK
GXOS_NET10:PE_RELOCATIONS_OK
GXOS_NET10:MANAGED_EXPORT_RVA=0x0000000000024724
GXOS_NET10:PE_IMPORT_DESCRIPTORS=10
GXOS_NET10:PE_IMPORT_SYMBOLS=124
GXOS_NET10:PE_IMPORT_RESOLVED=124
GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=0
GXOS_NET10:BOOT_INFO_PTR=0x...
GXOS_NET10:CALL_TARGET_VA=0x...
GXOS_NET10:BEFORE_MANAGED_CALL
GXOS_NET10:MANAGED_ENTRY_OK
GXOS_NET10:STACK_RSP_BEFORE_CALL=0x...
GXOS_NET10:STACK_RSP_MOD16=0
GXOS_NET10:AFTER_MANAGED_RETURN=0x0000000000000000
GXOS_NET10:MANAGED_ENTRY_COMPLETE
```

## Negative controls

Each control uses an isolated harness or NativeAOT artifact. `Run-Gate4-Negative.ps1` requires expected diagnostic tokens and rejects the exact success marker.

```powershell
& .\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\gate4-negative-invalid -Scenario InvalidBootInfo
& .\tools\Run-Gate4-Negative.ps1 -GateDirectory .\artifacts\gate4-negative-invalid -ExpectedPresent 'GXOS_NET10:AFTER_MANAGED_RETURN=0x00000000FFFFFFFE','GXOS_NET10:FAIL:managed-return-nonzero'

& .\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\gate4-negative-null -Scenario NullSerial
& .\tools\Run-Gate4-Negative.ps1 -GateDirectory .\artifacts\gate4-negative-null -ExpectedPresent 'GXOS_NET10:AFTER_MANAGED_RETURN=0x00000000FFFFFFFE','GXOS_NET10:FAIL:managed-return-nonzero'

& .\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\gate4-negative-unresolved -Scenario UnresolvedImport
& .\tools\Run-Gate4-Negative.ps1 -GateDirectory .\artifacts\gate4-negative-unresolved -ExpectedPresent 'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=1','GXOS_NET10:FAIL:negative-unresolved-import'

& .\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\gate4-negative-failfast -Scenario InvokeFailfast
& .\tools\Run-Gate4-Negative.ps1 -GateDirectory .\artifacts\gate4-negative-failfast -ExpectedPresent 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:ADVAPI32.dll!RegisterEventSourceW'

& .\tools\Build-Gate1.ps1 -OutputDirectory .\artifacts\gate1-negative-return -AdditionalProperties @('-p:DefineConstants=GXOS_NEGATIVE_RETURN')
& .\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\gate4-negative-return -ManagedArtifact .\artifacts\gate1-negative-return\shared\gxos-managed-entry-probe.dll
& .\tools\Run-Gate4-Negative.ps1 -GateDirectory .\artifacts\gate4-negative-return -ExpectedPresent 'GXOS_NET10:AFTER_MANAGED_RETURN=0x0000000000000007','GXOS_NET10:FAIL:managed-return-nonzero'

& .\tools\Build-Gate1.ps1 -OutputDirectory .\artifacts\gate1-negative-marker -AdditionalProperties @('-p:DefineConstants=GXOS_NEGATIVE_MARKER')
& .\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\gate4-negative-marker -ManagedArtifact .\artifacts\gate1-negative-marker\shared\gxos-managed-entry-probe.dll
& .\tools\Run-Gate4-Negative.ps1 -GateDirectory .\artifacts\gate4-negative-marker -ExpectedPresent 'GXOS_NET10:MANAGED_ENTRY_OX','GXOS_NET10:AFTER_MANAGED_RETURN=0x0000000000000000'
```

The normal artifact hash is rechecked after all mutations. None of the controls may contain `GXOS_NET10:MANAGED_ENTRY_OK`.

## Why the marker is managed-origin evidence

The proof has independent checks:

1. Native `BEFORE_MANAGED_CALL` precedes the marker; native code does not print the marker.
2. Native `AFTER_MANAGED_RETURN=0` follows the marker and changes to `7` in the managed-return mutation.
3. The managed marker mutation changes the exact serial bytes from `...ENTRY_OK` to `...ENTRY_OX` while the native return remains zero.
4. Invalid version and null callback controls return `-2` without the marker.
5. The export RVA, relocated target VA, and `objdump` disassembly link the call to the `[UnmanagedCallersOnly]` export and its callback invocation.

## ABI and fault diagnosis

`call_managed_entry` is an explicit `ms_abi` function pointer call. The wrapper clears DF and loads MXCSR `0x1F80` and x87 control `0x037F`. In the positive runs it recorded `RSP=0x0000000007E65820`, so `RSP mod 16 = 0` immediately before `CALL`; the call boundary supplies the 32-byte Microsoft x64 shadow space and the callee sees the normal return-address layout. The harness is compiled `-mno-red-zone`, preserves nonvolatile registers through the compiler-generated wrapper, and restores GS/interrupt state after the call. The loader stack is writable as provided by firmware; stack execution is not required, and the probe did not emit stack probing.

The local IDT records vector, error code, RIP, RSP, CR2, image base, managed target, boot-info pointer, and deepest marker. Phase state classifies faults as `FAULT_BEFORE_MANAGED`, `FAULT_IN_MANAGED`, or `FAULT_AFTER_MANAGED_RETURN`; arbitrary recovery is not attempted.

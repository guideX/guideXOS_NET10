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

The script requires, in order, PE validation, relocation, `PE_IMPORT_DESCRIPTORS=10`, `PE_IMPORT_SYMBOLS=124`, `PE_IMPORT_RESOLVED=124`, `PE_IMPORT_FUNCTIONAL=21`, `PE_IMPORT_FAILFAST=103`, `UNRESOLVED_REQUIRED_IMPORTS=0`, `BEFORE_MANAGED_CALL`, the exact managed marker, a zero return, and `MANAGED_ENTRY_COMPLETE`. It also verifies the artifact and loader hashes in each fresh process. A successful run ends with `GATE4_PROOF=PASSED` and `GATE4_RESULT=MANAGED_ENTRY`.

## Current-source fresh control baseline

The allocation-probe source was rebuilt with the no-allocation branch disabled. Final control runs `gate4-20260729-053730-218-run1`, `gate4-20260729-053750-777-run2`, and `gate4-20260729-053811-101-run3` passed with artifact SHA-256 `C9BCC17E21BE1871C9BBFA4FFFEAD7211513AD420F073F0023DEEB122B5C4861`, loader SHA-256 `F5CF3B2A5D0636C778CFB40E42DEDE13FF00E1F2B6DC6919F41C3805D7402858`, QEMU `11.0.0 (v11.0.0-12122-ga4bb4b10c9)`, and firmware SHA-256 `33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`. Each recorded 10 descriptors, 124 symbols, 21 functional imports, 103 fail-fast imports, zero unresolved imports, the managed marker, return zero, and `MANAGED_ENTRY_COMPLETE`.

The allocation-enabled artifact is a separate negative experiment. Its opt-in startup trace is documented in [ALLOCATION_GC_PROBE.md](ALLOCATION_GC_PROBE.md); it must not be passed to this Gate 4 success validator because its later allocation path remains unproven. The CRT-enabled variant is a separate bounded startup experiment documented in [CRT_ONEXIT_BOOTSTRAP.md](CRT_ONEXIT_BOOTSTRAP.md); it does not change the managed-entry success criterion.

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

## Separate allocation-startup path

The Gate 4 managed-entry proof above remains independent and passing. The allocation-enabled artifact is a separate opt-in path: its authentic PE entry reaches the verified `GetSystemTimeAsFileTime` and QPC contracts during compiler/CRT security-cookie initialization, then reaches `api-ms-win-crt-runtime-l1-1-0.dll!_initialize_onexit_table`. This path is documented in `docs\PLATFORM_TIME_CONTRACT.md`, `docs\PLATFORM_PERFORMANCE_COUNTER.md`, and `docs\ALLOCATION_GC_PROBE.md`; it is not part of the managed-entry success criterion. The allocation probe still returns `-10` before startup with zero allocation-context slots, and no allocation marker is claimed.

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

## CRT-enabled allocation-startup follow-on

The separate allocation-enabled NativeAOT entry now has a CRT opt-in profile with 22 functional imports and 102 deterministic fail-fast imports. After the already-proven FILETIME and QPC/QPF sequence, the attach helper calls `_initialize_onexit_table` twice. Each call returns zero and emits `CRT_ONEXIT_INITIALIZED_OK`; the next boundary is `KERNEL32.dll!InitializeSListHead`. No `_register_onexit_function`, `_execute_onexit_table`, `_crt_atexit`, `atexit`, `_cexit`, or `_c_exit` call was reached. The serial traces retain `TLS_ALLOC_LIMIT=0`, `TLS_ALLOC_PTR=0`, `MANAGED_THREAD_REGISTERED=0`, and `ALLOCATION_CONTEXT_VALID=0`, so this follow-on does not prove managed allocation or GC startup.

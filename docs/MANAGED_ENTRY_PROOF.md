# Managed-entry proof procedure

This procedure is intentionally bounded and fail-closed. The current result is a negative Gate 4 proof: the harness executes and reaches the dependency boundary, but it does not enter managed code.

## 1. Build the NativeAOT artifacts

```powershell
Set-Location D:\dev\guideXOS_NET10
& .\tools\Build-Gate1.ps1
```

For the checked evidence, the shared artifact is `artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll`. Run the host executable separately when checking Gate 1:

```powershell
& .\artifacts\gate1-brepro-1\gxos-managed-entry-probe.exe
if ($LASTEXITCODE -ne 0) { throw "host artifact failed: $LASTEXITCODE" }
```

## 2. Inspect and compare the PE

```powershell
& .\tools\Build-Gate3Evidence.ps1
```

The command creates a timestamped evidence directory, emits two PE manifests, compares the source artifact with the exact ESP-staged copy, and writes an `objdump -p` report. It must print:

```text
GATE3_COMPARISON=PASS
GATE3_PROOF=PE_IDENTITY_AND_MANIFEST_PASS
```

The current checked run is `artifacts\gate3-evidence-20260727-191336-817`.

## 3. Build the UEFI harness

```powershell
& .\tools\Build-Gate4Harness.ps1
```

The harness is a freestanding PE/COFF EFI application built without CRT or platform imports. It creates an ignored ESP staging directory containing:

```text
ESP\EFI\BOOT\BOOTX64.EFI
ESP\GXOS\gxos-managed-entry-probe.dll
ESP\startup.nsh
```

`startup.nsh` is present because fresh QEMU firmware variable stores may select the embedded UEFI shell before the fallback boot path. It invokes the same `BOOTX64.EFI` and does not print the managed marker.

## 4. Run fresh emulator processes

```powershell
& .\tools\Run-Gate4.ps1 -RunCount 3 -TimeoutSeconds 5
```

The script detects QEMU, copies the firmware code image into the per-run directory, copies a fresh variable image for every run, starts QEMU with no display and a bounded timeout, captures serial/stdout/stderr separately, and rejects stale logs by using a new timestamped run directory. The expected current result is:

```text
GATE4_PROOF=NOT_PASSED
GATE4_RESULT=BLOCKED_IMPORTS
```

The loader serial sequence proving the deepest current gate is:

```text
GXOS_NET10:LOADER_START
GXOS_NET10:PE_READ_OK
GXOS_NET10:PE_RELOCATIONS_OK
GXOS_NET10:MANAGED_EXPORT_RVA=0x0000000000024724
GXOS_NET10:PE_IMPORT_COUNT=10
GXOS_NET10:GATE4_BLOCKED_IMPORTS
```

The import-boundary marker is native loader evidence and is intentionally different from `GXOS_NET10:MANAGED_ENTRY_OK`.

## 5. Pass criteria for a future positive run

A future run may be called a managed-entry pass only when all of these are true:

1. Gate 1 hash and Gate 3 manifest checks pass.
2. A fresh QEMU process emits `LOADER_START`, PE validation, relocation, and export evidence.
3. The loader has a documented and verified import/runtime treatment, or the artifact is proven import-free.
4. The serial stream contains `GXOS_NET10:MANAGED_ENTRY_OK` only after the loader’s before-entry markers.
5. A controlled mutation of the managed return value or marker changes the observed result.
6. Three consecutive fresh processes pass with the same artifact and loader hashes.

The current run satisfies 1, 2, and 6 for the negative import-boundary result. It does not satisfy 3–5 and must not be reported as a managed-entry success.

## Failure diagnosis

| Observation | Meaning |
| --- | --- |
| Host executable returns nonzero | Gate 1 failed; inspect the publish logs and hashes. |
| Gate 3 comparison fails | Staging or a loader transformation changed important image content; do not continue. |
| No serial output | Firmware/image discovery or harness startup failure; inspect per-run stderr and firmware paths. |
| `PE_READ_OK` absent | File-system read or PE header validation failed. |
| `PE_RELOCATIONS_OK` absent | Image allocation, section copy, or relocation validation failed. |
| Export RVA absent | The artifact is not the expected shared NativeAOT form. |
| `PE_IMPORT_COUNT=10` and blocked marker | Current expected negative result; the artifact still requires Windows/CRT support. |
| Managed marker appears without a managed-entry proof | Treat as failure; stale output or native pre-printing is not acceptable. |

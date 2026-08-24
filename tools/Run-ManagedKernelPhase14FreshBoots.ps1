[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require14([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$expectedHash = $PayloadSha256.ToUpperInvariant()
Require14 ($RunCount -ge 3) 'Three fresh Phase 14 boots are required.'
Require14 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'

$phase11Runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
Require14 (Test-Path -LiteralPath $phase11Runner) 'Phase 11 fresh-boot runner is required.'

& $phase11Runner -GateDirectory $gate -EvidenceDirectory $evidence -PayloadSha256 $expectedHash -PayloadSize $PayloadSize -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS'

$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_DEVICE_CLAIMED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MMIO_WRITE_MAPPING_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_COMMAND_ORIGINAL=',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_COMMAND_RESULT=',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_BUS_MASTER_ENABLED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC_VALID',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_CAPABILITY_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_RING_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RING_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_INITIALIZED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_SUBMITTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_COMPLETED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_QUIESCED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_RELEASED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_BUS_MASTER_DISABLED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_NATIVE_TEARDOWN',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MMIO_UNMAPPED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_SEQUENCE_COMPLETE',
    'MANAGED_KERNEL_PHASE14_PASS')

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $serial = Join-Path $evidence ('runs\run-{0}\serial.log' -f $sequence)
    Require14 (Test-Path -LiteralPath $serial) "Missing serial log for boot $sequence."
    $text = [IO.File]::ReadAllText($serial)
    foreach ($marker in $required) {
        Require14 $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require14 ($text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_HARNESS_DEFERRED') -xor
               $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RECEIVED')) "Boot $sequence did not classify RX exactly once."
    Require14 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE14_PASS')).Count -eq 1) "Boot $sequence did not emit exactly one Phase 14 pass marker."
    Require14 (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')).Count -eq 1) "Boot $sequence did not emit exactly one Phase 12 pass marker."
    Require14 (!$text.Contains('GXOS_NET10:FAIL:') -and
               !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) "Boot $sequence reported a native or managed fault."
    Write-Output ('MANAGED_KERNEL_PHASE14_BOOT_{0}=PASS serial={1}' -f $sequence, $serial)
}

$owned = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
    Where-Object {
        ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ([string]$_.CommandLine).IndexOf($evidence, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
Require14 ($owned.Count -eq 0) 'An owned QEMU process remains after Phase 14 boots.'
Write-Output "MANAGED_KERNEL_PHASE14_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE14_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE14_QEMU_RUNS=$RunCount"

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

function Require15([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$expectedHash = $PayloadSha256.ToUpperInvariant()
Require15 ($RunCount -ge 3) 'Three fresh Phase 15 boots are required.'
Require15 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'

$phase11Runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
Require15 (Test-Path -LiteralPath $phase11Runner) 'Phase 11 fresh-boot runner is required.'

& $phase11Runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $expectedHash -PayloadSize $PayloadSize -RunCount $RunCount `
    -TimeoutSeconds $TimeoutSeconds -PostPhase11Marker `
    'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' -EnablePhase15Rx -Phase15NetworkBackend dgram

$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE13_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_DEVICE_CLAIMED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MMIO_WRITE_MAPPING_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_BUS_MASTER_ENABLED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC_VALID',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_CAPABILITY_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_RING_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RING_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_INITIALIZED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_SUBMITTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_COMPLETED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_E1000_RX_READY',
    'GXOS_NET10:MANAGED_E1000_RX_COMPLETE',
    'GXOS_NET10:MANAGED_E1000_RX_FRAME_OK',
    'GXOS_NET10:MANAGED_E1000_RX_RECYCLED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE15_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_DMA_PROVEN',
    'GXOS_NET10:MANAGED_KERNEL_PHASE15_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_QUIESCED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_RELEASED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_BUS_MASTER_DISABLED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_NATIVE_TEARDOWN',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MMIO_UNMAPPED',
    'MANAGED_KERNEL_PHASE15_PASS')

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $sequence)
    $serial = Join-Path $run 'serial.log'
    $injections = Join-Path $run 'injections.log'
    Require15 (Test-Path -LiteralPath $serial) "Missing serial log for boot $sequence."
    Require15 (Test-Path -LiteralPath $injections) "Missing injection log for boot $sequence."
    $text = [IO.File]::ReadAllText($serial)
    $injectionText = [IO.File]::ReadAllText($injections)
    foreach ($marker in $required) {
        Require15 $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require15 ($injectionText.Contains('MANAGED_E1000_PHASE15_INJECTED=PASS')) `
        "Boot $sequence lacks exactly one host Ethernet injection record."
    Require15 (([regex]::Matches($injectionText,
        'MANAGED_E1000_PHASE15_INJECTED=PASS')).Count -eq 1) `
        "Boot $sequence injected more than one Ethernet frame."
    Require15 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE15_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 15 pass marker."
    Require15 (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 12 pass marker."
    Require15 (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE13_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 13 pass marker."
    Require15 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE14_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 14 pass marker."
    Require15 (!$text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_HARNESS_DEFERRED') -and
               !$text.Contains('GXOS_NET10:MANAGED_E1000_RX_DESCRIPTOR_REJECTED') -and
               !$text.Contains('GXOS_NET10:MANAGED_E1000_RX_FRAME_REJECTED') -and
               !$text.Contains('GXOS_NET10:FAIL:') -and
               !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
        "Boot $sequence reported a receive rejection or native/managed fault."
    Write-Output ('MANAGED_KERNEL_PHASE15_BOOT_{0}=PASS serial={1} injections={2}' -f
        $sequence, $serial, $injections)
}

$owned = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
    Where-Object {
        ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ([string]$_.CommandLine).IndexOf($evidence, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
Require15 ($owned.Count -eq 0) 'An owned QEMU process remains after Phase 15 boots.'
Write-Output "MANAGED_KERNEL_PHASE15_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE15_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE15_QEMU_RUNS=$RunCount"

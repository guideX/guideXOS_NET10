[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require13([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$expectedHash = $PayloadSha256.ToUpperInvariant()
Require13 ($RunCount -ge 3) 'Three fresh ManagedKernel Phase 13 boots are required.'
Require13 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'

$phase11Runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
Require13 (Test-Path -LiteralPath $phase11Runner) 'Phase 11 fresh-boot runner is required.'

& $phase11Runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $expectedHash -PayloadSize $PayloadSize -RunCount $RunCount `
    -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE13_SEQUENCE_COMPLETE'

$required = @(
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_PHASE_PROOF_OK',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_PCI_TARGET=0000:00:02.0_8086:10D3',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_BAR_AUTHORITY=UEFI_RAM_EXCLUSION_PAGE',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_PAT_SUPPORTED=0x0000000000000001',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_CACHE_POLICY_PROVEN',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_PTE_FLAGS=0x0000000000000018',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_SERVICES_INSTALLED',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_MAPPING_CREATED',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_READ',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_REGISTER_OFFSET=0x0000000000000008',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_GC_SURVIVAL_OK',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_MAPPING_TEARDOWN',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_NEGATIVE_TESTS_OK',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE13_PASS',
    'GXOS_NET10:MANAGED_KERNEL_MMIO_PHASE_PROOF_OK',
    'GXOS_NET10:MANAGED_KERNEL_PHASE13_SEQUENCE_COMPLETE')

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $serial = Join-Path $evidence ('runs\run-{0}\serial.log' -f $sequence)
    Require13 (Test-Path -LiteralPath $serial) "Missing serial log for boot $sequence."
    $text = [IO.File]::ReadAllText($serial)
    foreach ($marker in $required) {
        Require13 $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require13 (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE13_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 13 pass marker."
    Require13 (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 12 pass marker."
    Require13 ($text.Contains('GXOS_NET10:MANAGED_KERNEL_RESOURCE_COUNT=0x0000000000000004')) `
        "Boot $sequence did not publish the expected four-resource snapshot."
    Require13 (!$text.Contains('GXOS_NET10:FAIL:') -and
        !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
        !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
        !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
        "Boot $sequence reported a native or managed fault."
    Write-Output ('MANAGED_KERNEL_PHASE13_BOOT_{0}=PASS serial={1}' -f $sequence, $serial)
}

Require13 (@(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
    Where-Object { ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -eq 0) `
    'An owned QEMU process remains after Phase 13 boots.'
Write-Output "MANAGED_KERNEL_PHASE13_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE13_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE13_QEMU_RUNS=$RunCount"

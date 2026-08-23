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

function Require12([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
& $runner -GateDirectory $GateDirectory -EvidenceDirectory $EvidenceDirectory `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize -RunCount $RunCount `
    -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS'

$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$markers = @(
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_NATIVE_SNAPSHOT_READY',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_SERVICES_INSTALLED',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_DISCOVERY_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_COUNT=0x0000000000000003',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_CATALOG_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_DESCRIPTOR_MATCH',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_PHASE_PROOF_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_CLAIM_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_WRONG_OWNER_REJECTED',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_RUNTIME_SURVIVAL_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_GC_SURVIVAL_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_ACCESS_DEFERRED_MMIO_CACHE',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_NEGATIVE_TESTS_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_RELEASE_OK',
    'GXOS_NET10:MANAGED_KERNEL_RESOURCE_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $serial = Join-Path $evidence ("runs\run-{0}\serial.log" -f $sequence)
    Require12 (Test-Path -LiteralPath $serial) "Missing Phase 12 serial log: $serial"
    $transcript = Get-Content -LiteralPath $serial -Raw
    foreach ($marker in $markers) {
        Require12 $transcript.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require12 (([regex]::Matches($transcript,
        'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')).Count -eq 1) `
        "Boot $sequence Phase 12 pass marker count was not one."
    Write-Output "MANAGED_KERNEL_PHASE12_RUN_$sequence=PASS serial=$serial"
}

Write-Output "MANAGED_KERNEL_PHASE12_QEMU_RUNS=$RunCount"

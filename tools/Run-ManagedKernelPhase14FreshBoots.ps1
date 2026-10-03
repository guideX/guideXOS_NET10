[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 240,
    [switch]$EnablePhase82
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
if ($EnablePhase82) {
    Require14 ($RunCount -eq 3) 'Phase 82 acceptance requires exactly three fresh boots.'
}
Require14 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'

$phase11Runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
Require14 (Test-Path -LiteralPath $phase11Runner) 'Phase 11 fresh-boot runner is required.'

if ($EnablePhase82) {
    & $phase11Runner -GateDirectory $gate -EvidenceDirectory $evidence `
        -PayloadSha256 $expectedHash -PayloadSize $PayloadSize `
        -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
        -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
        -EnablePhase80E1000 -EnablePhase82
} else {
    & $phase11Runner -GateDirectory $gate -EvidenceDirectory $evidence `
        -PayloadSha256 $expectedHash -PayloadSize $PayloadSize `
        -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
        -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
        -EnablePhase80E1000
}

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
    'GXOS_NET10:PHASE80_E1000_PCI=0000:00:02.0_8086:10D3',
    'GXOS_NET10:PHASE80_E1000_IRQ_ROUTE_REGISTERED',
    'GXOS_NET10:PHASE80_E1000_MANAGED_BINDING_READY',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_SUBMITTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_COMPLETED',
    'GXOS_NET10:PHASE80_E1000_HW_IRQ_ENTRY=1',
    'GXOS_NET10:PHASE80_E1000_NATIVE_DISPATCH_ENTRY=1',
    'GXOS_NET10:PHASE80_E1000_MANAGED_DELIVERY=4',
    'GXOS_NET10:PHASE80_E1000_REPEATS=3',
    'GXOS_NET10:PHASE80_E1000_HW_IRQ=4',
    'GXOS_NET10:PHASE80_E1000_NATIVE_DISPATCH=4',
    'GXOS_NET10:PHASE80_E1000_INTERRUPT_MASK_TXDW=0x',
    'GXOS_NET10:PHASE80_E1000_NATIVE_CAUSE=0x',
    'GXOS_NET10:PHASE80_E1000_MASK_READBACK=0x',
    'GXOS_NET10:PHASE80_E1000_CAUSE_CLEAR_READ=0x',
    'GXOS_NET10:PHASE80_E1000_INTERRUPT_STORM=0',
    'GXOS_NET10:PHASE80_E1000_ROUTE_QUIESCED=1',
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
    $commandLine = Join-Path (Split-Path -Parent $serial) 'qemu-commandline.log'
    Require14 (Test-Path -LiteralPath $commandLine) "Missing QEMU command line for boot $sequence."
    $qemuCommand = [IO.File]::ReadAllText($commandLine)
    Require14 ($qemuCommand.Contains('-nic none') -and
               $qemuCommand.Contains('-netdev user,id=net0') -and
               $qemuCommand.Contains('e1000e,netdev=net0,addr=2')) "Boot $sequence did not use the bounded e1000e/user-mode configuration."
    $text = [IO.File]::ReadAllText($serial)
    $requiredForBoot = @($required)
    if ($EnablePhase82) {
        $requiredForBoot += @(
            'GXOS_NET10:PHASE82_BOTH_ROUTES_REGISTERED=1',
            'GXOS_NET10:PHASE82_PHASE80_REGRESSION_4_OF_4=1',
            'GXOS_NET10:PHASE82_INTERLEAVING_6_EVENTS=1',
            'GXOS_NET10:PHASE82_ZERO_CROSS_DELIVERY=1',
            'GXOS_NET10:PHASE82_SHARED_QUEUE_IDENTITY=1',
            'GXOS_NET10:PHASE82_COM1_UNSUBSCRIBE_E1000_REMAINS_ACTIVE=1',
            'GXOS_NET10:PHASE82_E1000_AFTER_COM1_UNSUBSCRIBE=1',
            'GXOS_NET10:PHASE82_E1000_UNSUBSCRIBE_COM1_REMAINS_ACTIVE=1',
            'GXOS_NET10:PHASE82_COM1_AFTER_E1000_UNSUBSCRIBE=1',
            'GXOS_NET10:PHASE82_TEARDOWN_INDEPENDENCE=1',
            'GXOS_NET10:PHASE82_COM1_HARDWARE_EVENTS=0x0000000000000004',
            'GXOS_NET10:PHASE82_COM1_NATIVE_CAPTURES=0x0000000000000004',
            'GXOS_NET10:PHASE82_COM1_MANAGED_DELIVERIES=0x0000000000000004',
            'GXOS_NET10:PHASE82_E1000_HARDWARE_EVENTS=0x0000000000000008',
            'GXOS_NET10:PHASE82_E1000_NATIVE_CAPTURES=0x0000000000000008',
            'GXOS_NET10:PHASE82_E1000_MANAGED_DELIVERIES=0x0000000000000008',
            'GXOS_NET10:PHASE82_COM1_IRQ_EOI=0x0000000000000004',
            'GXOS_NET10:PHASE82_E1000_MSI_ACKNOWLEDGEMENTS=0x0000000000000008',
            'GXOS_NET10:PHASE82_COM1_ROUTE_IRQ_ENTRIES=0x0000000000000004',
            'GXOS_NET10:PHASE82_E1000_TXDW_DELIVERED_ONLY=1')
    }
    foreach ($marker in $requiredForBoot) {
        Require14 $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require14 ($text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_HARNESS_DEFERRED') -xor
               $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RECEIVED')) "Boot $sequence did not classify RX exactly once."
    Require14 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE14_PASS')).Count -eq 1) "Boot $sequence did not emit exactly one Phase 14 pass marker."
    Require14 (([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')).Count -eq 1) "Boot $sequence did not emit exactly one Phase 12 pass marker."
    Require14 (([regex]::Matches($text, 'GXOS_NET10:PHASE80_E1000_MANAGED_DELIVERY=4')).Count -eq 1) "Boot $sequence did not emit exactly one managed E1000 delivery summary."
    Require14 (([regex]::Matches($text, 'GXOS_NET10:PHASE80_E1000_HW_IRQ=4')).Count -eq 1) "Boot $sequence did not emit exactly one hardware IRQ summary."
    Require14 (([regex]::Matches($text, 'GXOS_NET10:PHASE80_E1000_NATIVE_DISPATCH=4')).Count -eq 1) "Boot $sequence did not emit exactly one native dispatch summary."
    Require14 ($text.IndexOf('GXOS_NET10:PHASE80_E1000_HW_IRQ_ENTRY=1') -lt $text.IndexOf('GXOS_NET10:PHASE80_E1000_MANAGED_DELIVERY=4')) "Boot $sequence did not log the hardware IRQ before managed delivery."
    if ($EnablePhase82) {
        $interleaved = [regex]::Matches(
            $text, 'GXOS_NET10:PHASE82_INTERLEAVED_STEP=0x([0-9A-F]{16})_(COM1|E1000)')
        $serialSteps = @($interleaved | Where-Object { $_.Groups[2].Value -eq 'COM1' })
        $e1000Steps = @($interleaved | Where-Object { $_.Groups[2].Value -eq 'E1000' })
        Require14 ($interleaved.Count -eq 6 -and
                   $serialSteps.Count -eq 3 -and $e1000Steps.Count -eq 3) `
            "Boot $sequence did not complete a six-event 3/3 interleaving."
        Require14 ($text.Contains('GXOS_NET10:PHASE82_CROSS_DELIVERIES=0') -and
                   $text.Contains('GXOS_NET10:PHASE82_WRONG_DEVICE_DELIVERIES=0') -and
                   $text.Contains('GXOS_NET10:PHASE82_WRONG_EVENT_TYPE_DELIVERIES=0')) `
            "Boot $sequence reported a route identity or event-type rejection."
        $phase80Raw = [regex]::Match(
            $text, 'GXOS_NET10:PHASE82_PHASE80_RAW_IRQ_ENTRIES=0x([0-9A-F]{16})')
        $e1000Raw = [regex]::Match(
            $text, 'GXOS_NET10:PHASE82_E1000_ROUTE_ISR_ENTRIES=0x([0-9A-F]{16})')
        $e1000Eoi = [regex]::Match(
            $text, 'GXOS_NET10:PHASE82_E1000_APIC_EOI=0x([0-9A-F]{16})')
        Require14 ($phase80Raw.Success -and
                   $phase80Raw.Groups[1].Value -in @(
                       '0000000000000004', '0000000000000005')) `
            "Boot $sequence had an unbounded or missing Phase 80 raw MSI entry count."
        Require14 ($e1000Raw.Success -and $e1000Eoi.Success -and
                   $e1000Raw.Groups[1].Value -eq $e1000Eoi.Groups[1].Value) `
            "Boot $sequence did not acknowledge each captured E1000 MSI entry."
    }
    $causeMatch = [regex]::Match($text, 'GXOS_NET10:PHASE80_E1000_NATIVE_CAUSE=0x([0-9A-F]{16})')
    $maskMatch = [regex]::Match($text, 'GXOS_NET10:PHASE80_E1000_MASK_READBACK=0x([0-9A-F]{16})')
    $clearMatch = [regex]::Match($text, 'GXOS_NET10:PHASE80_E1000_CAUSE_CLEAR_READ=0x([0-9A-F]{16})')
    Require14 ($causeMatch.Success -and (([Convert]::ToUInt64($causeMatch.Groups[1].Value, 16) -band 1) -ne 0)) "Boot $sequence did not report the TXDW cause bit."
    Require14 ($maskMatch.Success -and (([Convert]::ToUInt64($maskMatch.Groups[1].Value, 16) -band 1) -ne 0)) "Boot $sequence did not read back the TXDW interrupt mask."
    Require14 ($clearMatch.Success -and ([Convert]::ToUInt64($clearMatch.Groups[1].Value, 16) -eq 0)) "Boot $sequence did not observe the read-to-clear ICR state."
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

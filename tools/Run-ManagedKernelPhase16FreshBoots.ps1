[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Require16([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$expectedHash = $PayloadSha256.ToUpperInvariant()
Require16 ($RunCount -ge 3) 'Three fresh Phase 16 boots are required.'
Require16 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 must be 64 hex characters.'
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
Require16 (Test-Path -LiteralPath $runner) 'Phase 11 fresh-boot runner is required.'

& $runner -GateDirectory $GateDirectory -EvidenceDirectory $EvidenceDirectory `
    -PayloadSha256 $expectedHash -PayloadSize $PayloadSize -RunCount $RunCount `
    -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx `
    -EnablePhase16Protocol -Phase15NetworkBackend dgram `
    -Phase15EnableFilterDump -Phase15FilterDumpQueue all

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
    'GXOS_NET10:MANAGED_ETHERNET_READY',
    'GXOS_NET10:MANAGED_ARP_READY',
    'GXOS_NET10:MANAGED_ARP_RESOLUTION_STARTED',
    'GXOS_NET10:MANAGED_ETHERNET_TX_ARP_REQUEST',
    'GXOS_NET10:MANAGED_ETHERNET_RX_ARP',
    'GXOS_NET10:MANAGED_ARP_REPLY_VALID',
    'GXOS_NET10:MANAGED_ARP_CACHE_LEARNED',
    'GXOS_NET10:MANAGED_ARP_RESOLUTION_COMPLETE',
    'GXOS_NET10:MANAGED_ARP_REQUEST_FOR_LOCAL',
    'GXOS_NET10:MANAGED_ARP_REPLY_SENT',
    'GXOS_NET10:MANAGED_ARP_RESPONDER_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE16_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE15_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_DMA_PROVEN',
    'GXOS_NET10:MANAGED_KERNEL_PHASE15_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_QUIESCED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_RELEASED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_BUS_MASTER_DISABLED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_ACCOUNTING_RESTORED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_NATIVE_TEARDOWN',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_MMIO_UNMAPPED',
    'MANAGED_KERNEL_PHASE15_PASS',
    'MANAGED_KERNEL_PHASE16_PASS')

$pcapParser = Join-Path $PSScriptRoot 'Parse-ManagedE1000Phase16Pcap.ps1'
for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $run = Join-Path ([IO.Path]::GetFullPath($EvidenceDirectory)) `
        ('runs\run-{0}' -f $sequence)
    $serial = Join-Path $run 'serial.log'
    $injections = Join-Path $run 'injections.log'
    $pcap = Join-Path $run 'netdev.pcap'
    Require16 (Test-Path -LiteralPath $serial) "Missing serial log for boot $sequence."
    Require16 (Test-Path -LiteralPath $injections) "Missing injection log for boot $sequence."
    Require16 (Test-Path -LiteralPath $pcap) "Missing PCAP for boot $sequence."
    $text = [IO.File]::ReadAllText($serial)
    $injectionText = [IO.File]::ReadAllText($injections)
    foreach ($marker in $required) {
        Require16 $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    foreach ($marker in @(
        'MANAGED_PHASE16_GUEST_ARP_REQUEST=PASS',
        'MANAGED_PHASE16_HOST_ARP_REPLY=PASS',
        'MANAGED_PHASE16_HOST_ARP_REQUEST=PASS',
        'MANAGED_PHASE16_GUEST_ARP_REPLY=PASS')) {
        Require16 (([regex]::Matches($injectionText, $marker)).Count -eq 1) `
            "Boot $sequence did not log exactly one $marker record."
    }
    Require16 (([regex]::Matches($text, 'MANAGED_ARP_CACHE_LEARNED')).Count -eq 1) `
        "Boot $sequence learned more than one ARP mapping."
    Require16 (([regex]::Matches($text, 'MANAGED_ARP_REPLY_SENT')).Count -eq 1) `
        "Boot $sequence sent more than one responder reply."
    Require16 (([regex]::Matches($text, 'MANAGED_ETHERNET_TX_ARP_REQUEST')).Count -eq 1) `
        "Boot $sequence sent more than one resolution request."
    Require16 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE16_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 16 pass marker."
    Require16 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE15_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 15 pass marker."
    Require16 (!$text.Contains('MANAGED_KERNEL_PHASE15_RX_HARNESS_DEFERRED') -and
               !$text.Contains('MANAGED_E1000_RX_DESCRIPTOR_REJECTED') -and
               !$text.Contains('MANAGED_E1000_RX_FRAME_REJECTED') -and
               !$text.Contains('MANAGED_E1000_RX_PROTOCOL_DESCRIPTOR_REJECTED') -and
               !$text.Contains('GXOS_NET10:FAIL:') -and
               !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
        "Boot $sequence reported a malformed frame, rejection, or fault."
    $macMatch = [regex]::Match($text,
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})\s*([0-9A-Fa-f]{8})')
    Require16 $macMatch.Success "Boot $sequence did not publish the runtime e1000 MAC."
    $guestMac = $macMatch.Groups[1].Value + $macMatch.Groups[2].Value
    $pcapResult = & $pcapParser -PcapPath $pcap -GuestMac $guestMac
    Require16 (($pcapResult -join "`n").Contains('MANAGED_E1000_PHASE16_PCAP=PASS')) `
        "Boot $sequence did not contain the four exact ARP PCAP frames."
    Require16 ((Get-FileHash -LiteralPath (Join-Path $GateDirectory 'ESP\GXOS\gxos-managed-kernel.dll') `
        -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
        "Payload hash changed during boot $sequence."
    Write-Output ('MANAGED_KERNEL_PHASE16_BOOT_{0}=PASS guest_mac={1} serial={2} injections={3} pcap={4}' -f `
        $sequence, $guestMac, $serial, $injections, $pcap)
}

$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$owned = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
    Where-Object {
        ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ([string]$_.CommandLine).IndexOf($evidence, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
Require16 ($owned.Count -eq 0) 'An owned QEMU process remains after Phase 16 boots.'
Write-Output "MANAGED_KERNEL_PHASE16_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE16_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE16_QEMU_RUNS=$RunCount"

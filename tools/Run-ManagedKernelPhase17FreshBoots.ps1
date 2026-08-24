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

function Require17([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$expectedHash = $PayloadSha256.ToUpperInvariant()
Require17 ($RunCount -ge 3) 'Three fresh Phase 17 boots are required.'
Require17 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 is invalid.'
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
Require17 (Test-Path -LiteralPath $runner) 'The shared fresh-boot runner is missing.'

& $runner -GateDirectory $GateDirectory -EvidenceDirectory $EvidenceDirectory `
    -PayloadSha256 $expectedHash -PayloadSize $PayloadSize -RunCount $RunCount `
    -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx -EnablePhase17Protocol `
    -Phase15NetworkBackend dgram -Phase15EnableFilterDump -Phase15FilterDumpQueue all

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
    'GXOS_NET10:MANAGED_ARP_REPLY_VALID',
    'GXOS_NET10:MANAGED_ARP_RESOLUTION_COMPLETE',
    'GXOS_NET10:MANAGED_ARP_REQUEST_FOR_LOCAL',
    'GXOS_NET10:MANAGED_ARP_REPLY_SENT',
    'GXOS_NET10:MANAGED_ARP_RESPONDER_PASS',
    'GXOS_NET10:MANAGED_IPV4_READY',
    'GXOS_NET10:MANAGED_ICMPV4_READY',
    'GXOS_NET10:MANAGED_IPV4_FIRST_PING_SENT',
    'GXOS_NET10:MANAGED_ICMP_FIRST_REPLY_VALID',
    'GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_0',
    'GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_1',
    'GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_2',
    'GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_3',
    'GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_4',
    'GXOS_NET10:MANAGED_IPV4_MALFORMED_CONTROLS_PASS',
    'GXOS_NET10:MANAGED_ICMP_RESPONDER_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE17_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_IPV4_POST_GC_PING_SENT',
    'GXOS_NET10:MANAGED_ICMP_POST_GC_REPLY_VALID',
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
    'MANAGED_KERNEL_PHASE16_PASS',
    'MANAGED_KERNEL_PHASE17_PASS')

$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$pcapParser = Join-Path $PSScriptRoot 'Parse-ManagedE1000Phase17Pcap.ps1'
for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $sequence)
    $serial = Join-Path $run 'serial.log'
    $injections = Join-Path $run 'injections.log'
    $pcap = Join-Path $run 'netdev.pcap'
    Require17 (Test-Path -LiteralPath $serial) "Missing serial log for boot $sequence."
    Require17 (Test-Path -LiteralPath $injections) "Missing injection log for boot $sequence."
    Require17 (Test-Path -LiteralPath $pcap) "Missing PCAP for boot $sequence."
    $text = [IO.File]::ReadAllText($serial)
    $injectionText = [IO.File]::ReadAllText($injections)
    foreach ($marker in $required) {
        Require17 $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require17 (([regex]::Matches($injectionText,
        'MANAGED_E1000_PHASE17_INJECTED=PASS')).Count -eq 1) `
        "Boot $sequence did not record the Phase 17 RX proof injection exactly once."
    foreach ($name in @(
        'GUEST_IPV4_ECHO_REQUEST', 'HOST_IPV4_ECHO_REPLY',
        'HOST_IPV4_ECHO_REQUEST', 'GUEST_IPV4_ECHO_REPLY',
        'GUEST_POST_GC_ECHO_REQUEST', 'HOST_POST_GC_ECHO_REPLY')) {
        Require17 (([regex]::Matches($injectionText,
            "MANAGED_PHASE17_${name}=PASS")).Count -eq 1) `
            "Boot $sequence did not record exactly one $name frame."
    }
    Require17 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE17_PASS')).Count -eq 1) `
        "Boot $sequence did not emit exactly one Phase 17 pass marker."
    Require17 (([regex]::Matches($text, 'MANAGED_KERNEL_PHASE16_PASS')).Count -eq 1) `
        "Boot $sequence did not preserve the Phase 16 pass marker."
    Require17 (!$text.Contains('MANAGED_IPV4_PENDING_OVERFLOW') -and
               !$text.Contains('GXOS_NET10:FAIL:') -and
               !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
        "Boot $sequence reported a failure, fault, or pending overflow."
    $macMatch = [regex]::Match($text,
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})\s*([0-9A-Fa-f]{8})')
    Require17 $macMatch.Success "Boot $sequence did not publish the runtime e1000 MAC."
    $guestMac = $macMatch.Groups[1].Value + $macMatch.Groups[2].Value
    $pcapResult = & $pcapParser -PcapPath $pcap -GuestMac $guestMac
    Require17 (($pcapResult -join "`n").Contains('MANAGED_E1000_PHASE17_PCAP=PASS')) `
        "Boot $sequence did not pass exact Phase 17 PCAP validation."
    Require17 ((Get-FileHash -LiteralPath (Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll') `
        -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
        "Payload hash changed during boot $sequence."
    Write-Output ('MANAGED_KERNEL_PHASE17_BOOT_{0}=PASS guest_mac={1} serial={2} injections={3} pcap={4}' -f
        $sequence, $guestMac, $serial, $injections, $pcap)
}

$owned = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
    Where-Object {
        ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ([string]$_.CommandLine).IndexOf($evidence, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
Require17 ($owned.Count -eq 0) 'An owned QEMU process remains after Phase 17 boots.'
Write-Output "MANAGED_KERNEL_PHASE17_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE17_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE17_QEMU_RUNS=$RunCount"

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

function Require18([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

$expectedHash = $PayloadSha256.ToUpperInvariant()
Require18 ($RunCount -ge 3) 'Three fresh Phase 18 boots are required.'
Require18 ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 is invalid.'
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
Require18 (Test-Path -LiteralPath $runner) 'The shared fresh-boot runner is missing.'

& $runner -GateDirectory $GateDirectory -EvidenceDirectory $EvidenceDirectory `
    -PayloadSha256 $expectedHash -PayloadSize $PayloadSize -RunCount $RunCount `
    -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx -EnablePhase18Protocol `
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
    'GXOS_NET10:MANAGED_UDP_READY',
    'GXOS_NET10:MANAGED_UDP_MANAGED_REQUEST_SENT',
    'GXOS_NET10:MANAGED_UDP_MANAGED_RESPONSE_VALID',
    'GXOS_NET10:MANAGED_UDP_MANAGED_EXCHANGE_PASS',
    'GXOS_NET10:MANAGED_UDP_PEER_RESPONSE_SENT',
    'GXOS_NET10:MANAGED_UDP_ZERO_CHECKSUM_ACCEPTED',
    'GXOS_NET10:MANAGED_UDP_ZERO_CHECKSUM_RESPONSE_SENT',
    'GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_0',
    'GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_1',
    'GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_2',
    'GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_3',
    'GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_4',
    'GXOS_NET10:MANAGED_UDP_MALFORMED_CONTROLS_PASS',
    'GXOS_NET10:MANAGED_UDP_POST_MALFORMED_RESPONSE_SENT',
    'GXOS_NET10:MANAGED_UDP_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_UDP_POST_GC_REQUEST_SENT',
    'GXOS_NET10:MANAGED_UDP_POST_GC_RESPONSE_VALID',
    'GXOS_NET10:MANAGED_UDP_POST_GC_PEER_RESPONSE_SENT',
    'GXOS_NET10:MANAGED_UDP_POST_GC_EXCHANGE_PASS',
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
    'MANAGED_KERNEL_PHASE17_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE18_PASS')

$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$pcapParser = Join-Path $PSScriptRoot 'Parse-ManagedE1000Phase18Pcap.ps1'
Require18 (Test-Path -LiteralPath $pcapParser) 'The Phase 18 PCAP parser is missing.'
for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $sequence)
    $serial = Join-Path $run 'serial.log'
    $injections = Join-Path $run 'injections.log'
    $pcap = Join-Path $run 'netdev.pcap'
    Require18 (Test-Path -LiteralPath $serial) "Missing serial log for boot $sequence."
    Require18 (Test-Path -LiteralPath $injections) "Missing injection log for boot $sequence."
    Require18 (Test-Path -LiteralPath $pcap) "Missing PCAP for boot $sequence."
    $text = [IO.File]::ReadAllText($serial)
    $injectionText = [IO.File]::ReadAllText($injections)
    foreach ($marker in $required) {
        Require18 $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require18 (([regex]::Matches($injectionText,
        'MANAGED_E1000_PHASE18_INJECTED=PASS')).Count -eq 1) `
        "Boot $sequence did not record the Phase 18 RX proof injection exactly once."
    foreach ($name in @(
        'GUEST_IPV4_ECHO_REQUEST', 'HOST_IPV4_ECHO_REPLY',
        'HOST_IPV4_ECHO_REQUEST', 'GUEST_IPV4_ECHO_REPLY',
        'GUEST_POST_GC_ECHO_REQUEST', 'HOST_POST_GC_ECHO_REPLY')) {
        Require18 (([regex]::Matches($injectionText,
            "MANAGED_PHASE17_${name}=PASS")).Count -eq 1) `
            "Boot $sequence did not record exactly one $name frame."
    }
    foreach ($name in @(
        'GUEST_UDP_REQUEST', 'HOST_UDP_RESPONSE', 'HOST_UDP_REQUEST',
        'GUEST_UDP_ENDPOINT_RESPONSE', 'HOST_UDP_ZERO_CHECKSUM_REQUEST',
        'GUEST_UDP_ZERO_CHECKSUM_RESPONSE', 'HOST_UDP_POST_MALFORMED_REQUEST',
        'GUEST_UDP_POST_MALFORMED_RESPONSE', 'GUEST_UDP_POST_GC_REQUEST',
        'HOST_UDP_POST_GC_RESPONSE', 'HOST_UDP_POST_GC_REQUEST',
        'GUEST_UDP_POST_GC_RESPONSE')) {
        Require18 (([regex]::Matches($injectionText,
            "MANAGED_PHASE18_${name}=PASS")).Count -eq 1) `
            "Boot $sequence did not record exactly one $name frame."
    }
    Require18 (!$text.Contains('MANAGED_IPV4_PENDING_OVERFLOW') -and
               !$text.Contains('GXOS_NET10:FAIL:') -and
               !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
               !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
               !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) `
        "Boot $sequence reported a failure, fault, or pending overflow."
    $macMatch = [regex]::Match($text,
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})\s*([0-9A-Fa-f]{8})')
    Require18 $macMatch.Success "Boot $sequence did not publish the runtime e1000 MAC."
    $guestMac = $macMatch.Groups[1].Value + $macMatch.Groups[2].Value
    $pcapResult = & $pcapParser -PcapPath $pcap -GuestMac $guestMac
    Require18 (($pcapResult -join "`n").Contains('MANAGED_E1000_PHASE18_PCAP=PASS')) `
        "Boot $sequence did not pass exact Phase 18 PCAP validation."
    Require18 ((Get-FileHash -LiteralPath (Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll') `
        -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
        "Payload hash changed during boot $sequence."
    Write-Output ('MANAGED_KERNEL_PHASE18_BOOT_{0}=PASS guest_mac={1} serial={2} injections={3} pcap={4}' -f
        $sequence, $guestMac, $serial, $injections, $pcap)
}

$owned = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
    Where-Object {
        ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ([string]$_.CommandLine).IndexOf($evidence, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
Require18 ($owned.Count -eq 0) 'An owned QEMU process remains after Phase 18 boots.'
Write-Output "MANAGED_KERNEL_PHASE18_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE18_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE18_QEMU_RUNS=$RunCount"

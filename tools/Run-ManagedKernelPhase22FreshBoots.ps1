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
function Require22Run([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

Require22Run ($RunCount -ge 3) 'Three fresh Phase 22 boots are required.'
$expectedHash = $PayloadSha256.ToUpperInvariant()
Require22Run ($expectedHash -match '^[0-9A-F]{64}$') 'Payload SHA-256 is invalid.'
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$parser = Join-Path $PSScriptRoot 'Parse-ManagedE1000Phase22Pcap.ps1'
Require22Run (Test-Path -LiteralPath $runner) 'The shared fresh-boot runner is missing.'
Require22Run (Test-Path -LiteralPath $parser) 'The Phase 22 PCAP parser is missing.'

& $runner -GateDirectory $GateDirectory -EvidenceDirectory $EvidenceDirectory `
    -PayloadSha256 $expectedHash -PayloadSize $PayloadSize -RunCount $RunCount `
    -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx -EnablePhase22Protocol `
    -Phase15NetworkBackend dgram -Phase15EnableFilterDump -Phase15FilterDumpQueue all

$required = @(
    'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_READY',
    'GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT',
    'GXOS_NET10:MANAGED_DHCP_REQUEST_SENT',
    'GXOS_NET10:MANAGED_DHCP_ACK_RECEIVED',
    'GXOS_NET10:MANAGED_DHCP_BOUND',
    'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_CONFIGURED',
    'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_DHCP_BOUND',
    'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_DNS_SUCCESS',
    'GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_RESOLVED_IPV4=0x000000000A0F0002',
    'GXOS_NET10:MANAGED_TCP_CONNECT_STARTED',
    'GXOS_NET10:MANAGED_TCP_HANDSHAKE_SUCCESS',
    'GXOS_NET10:MANAGED_TCP_FIRST_REQUEST_SENT',
    'MANAGED_TCP_FIRST_EXCHANGE_SUCCESS',
    'MANAGED_TCP_GC_WHILE_ESTABLISHED_PASSED',
    'MANAGED_TCP_POST_GC_REQUEST_SENT',
    'MANAGED_TCP_POST_GC_EXCHANGE_SUCCESS',
    'MANAGED_TCP_FIN_SENT',
    'MANAGED_TCP_GRACEFUL_CLOSE_SUCCESS',
    'MANAGED_NETWORK_SERVICE_TCP_TEARDOWN_PASSED',
    'MANAGED_NETWORK_SERVICE_PHASE22_PASS',
    'MANAGED_KERNEL_PHASE22_PASS'
)
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
for ($sequence = 1; $sequence -le $RunCount; ++$sequence) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $sequence)
    $serial = Join-Path $run 'serial.log'
    $injections = Join-Path $run 'injections.log'
    $pcap = Join-Path $run 'netdev.pcap'
    Require22Run ((Test-Path -LiteralPath $serial) -and
        (Test-Path -LiteralPath $injections) -and (Test-Path -LiteralPath $pcap)) `
        "Missing Phase 22 evidence for boot $sequence."
    $text = [IO.File]::ReadAllText($serial)
    $injectionText = [IO.File]::ReadAllText($injections)
    foreach ($marker in $required) {
        Require22Run $text.Contains($marker) "Boot $sequence missing marker: $marker"
    }
    Require22Run (([regex]::Matches($injectionText,
        'MANAGED_E1000_PHASE22_INJECTED=PASS')).Count -eq 1) `
        "Boot $sequence did not record exactly one Phase 22 RX proof injection."
    Require22Run (!$text.Contains('GXOS_NET10:FAIL:') -and
        !$text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -and
        !$text.Contains('GXOS_NET10:PAGE_FAULT_') -and
        !$text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:') ) `
        "Boot $sequence reported a fault."
    $macMatch = [regex]::Match($text,
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})\s*([0-9A-Fa-f]{8})')
    Require22Run $macMatch.Success "Boot $sequence did not publish the runtime e1000 MAC."
    $guestMac = $macMatch.Groups[1].Value + $macMatch.Groups[2].Value
    $pcapResult = & $parser -PcapPath $pcap -GuestMac $guestMac
    Require22Run (($pcapResult -join "`n").Contains('MANAGED_E1000_PHASE22_PCAP=PASS')) `
        "Boot $sequence did not pass exact Phase 22 PCAP validation."
    Require22Run ((Get-FileHash -LiteralPath (Join-Path $gate 'ESP\GXOS\gxos-managed-kernel.dll') `
        -Algorithm SHA256).Hash.ToUpperInvariant() -eq $expectedHash) `
        "Payload hash changed during boot $sequence."
    Write-Output ('MANAGED_KERNEL_PHASE22_BOOT_{0}=PASS guest_mac={1} serial={2} injections={3} pcap={4}' -f
        $sequence, $guestMac, $serial, $injections, $pcap)
}
$owned = @(Get-CimInstance Win32_Process -Filter "Name = 'qemu-system-x86_64.exe'" |
    Where-Object {
        ([string]$_.CommandLine).IndexOf($gate, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ([string]$_.CommandLine).IndexOf($evidence, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
Require22Run ($owned.Count -eq 0) 'An owned QEMU process remains after Phase 22 boots.'
Write-Output "MANAGED_KERNEL_PHASE22_PAYLOAD_SHA256=$expectedHash"
Write-Output "MANAGED_KERNEL_PHASE22_PAYLOAD_SIZE=$PayloadSize"
Write-Output "MANAGED_KERNEL_PHASE22_QEMU_RUNS=$RunCount"

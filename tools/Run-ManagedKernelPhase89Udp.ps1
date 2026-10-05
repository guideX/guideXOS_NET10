[CmdletBinding()]
param(
    [string]$BuildDirectory = '',
    [string]$EvidenceDirectory = '',
    [int]$TimeoutSeconds = 240,
    [ValidateSet(1, 3)] [int]$RunCount = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $root 'artifacts\phase89-build'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $tag = if ($RunCount -eq 1) { 'diagnostic' } else { 'acceptance' }
    $EvidenceDirectory = Join-Path $root ("artifacts\phase89-$tag-qemu-evidence")
}
$build = [IO.Path]::GetFullPath($BuildDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$payloadDirectory = Join-Path $build 'managed-kernel'
$payload = Join-Path $payloadDirectory 'publish\gxos-managed-kernel.dll'
$normalGate = Join-Path $build 'gate4-normal'
$acceptanceGate = Join-Path $build 'gate4-phase89'
$managedBuild = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$gateBuild = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$bootRunner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$injector = Join-Path $PSScriptRoot 'Inject-ManagedKernelPhase89Udp.ps1'

function Require89([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Marker89([string]$text, [string]$name) {
    $match = [regex]::Match($text,
        [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    Require89 $match.Success "Missing numeric marker: $name"
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
$injectorValidation = @(& $injector -ValidateOnly)
Require89 ($injectorValidation -contains 'PHASE89_UDP_INJECTOR_VALIDATION=PASS packets=4 ethernet=0800 ipv4=45 protocol=17 ipv4_checksum=valid udp_checksum=nonzero-valid payload=89-sequence-A5-5x-C3-3C') `
    'Phase 89 injector byte validation failed.'
$injectorValidation | Set-Content -LiteralPath `
    (Join-Path $build 'phase89-injector-validation.log') -Encoding ascii

$managedOutput = @(& $managedBuild -OutputDirectory $payloadDirectory)
$managedOutput | Set-Content -LiteralPath `
    (Join-Path $build 'managed-kernel-build-output.log') -Encoding ascii
Require89 (Test-Path -LiteralPath $payload) 'ManagedKernel NativeAOT payload is missing.'
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

$normalOutput = @(& $gateBuild -OutputDirectory $normalGate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -AssumeUnspecifiedTimezoneUtc)
$normalOutput | Set-Content -LiteralPath `
    (Join-Path $build 'gate4-normal-build-output.log') -Encoding ascii
$acceptanceOutput = @(& $gateBuild -OutputDirectory $acceptanceGate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -EnablePhase83E1000Rx -EnablePhase89ManagedUdp `
    -AssumeUnspecifiedTimezoneUtc)
$acceptanceOutput | Set-Content -LiteralPath `
    (Join-Path $build 'gate4-phase89-build-output.log') -Encoding ascii

$runnerArguments = @{
    GateDirectory = $acceptanceGate
    EvidenceDirectory = $evidence
    PayloadSha256 = $payloadHash
    PayloadSize = $payloadSize
    RunCount = $RunCount
    TimeoutSeconds = $TimeoutSeconds
    PostPhase11Marker = 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS'
    EnablePhase80E1000 = $true
    EnablePhase83 = $true
    EnablePhase89Udp = $true
    Phase89InjectorPath = $injector
}
if ($RunCount -eq 1) { $runnerArguments['Phase15EnableQemuReceiveTrace'] = $true }
$runnerOutput = @(& $bootRunner @runnerArguments)
$runnerOutput | Set-Content -LiteralPath `
    (Join-Path $build 'qemu-runner-output.log') -Encoding ascii

$summaries = @()
for ($boot = 1; $boot -le $RunCount; $boot++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $boot)
    $serialPath = Join-Path $run 'serial.log'
    $injectionsPath = Join-Path $run 'injections.log'
    $timelinePath = Join-Path $run 'timeline.log'
    $commandPath = Join-Path $run 'qemu-commandline.log'
    foreach ($path in @($serialPath, $injectionsPath, $timelinePath, $commandPath)) {
        Require89 (Test-Path -LiteralPath $path) "Boot $boot evidence is missing: $path"
    }
    $serial = [IO.File]::ReadAllText($serialPath)
    $injections = [IO.File]::ReadAllText($injectionsPath)
    $timeline = [IO.File]::ReadAllText($timelinePath)
    $command = [IO.File]::ReadAllText($commandPath)
    foreach ($marker in @(
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_INITIALIZED',
        'GXOS_NET10:PHASE89_UDP_MODE_SELECTED=1',
        'GXOS_NET10:PHASE89_UDP_DISPATCH_READY=1',
        'GXOS_NET10:PHASE89_UDP_RX_READY=1',
        'GXOS_NET10:PHASE89_UDP_HARDWARE_INGRESS_PASS=1',
        'GXOS_NET10:PHASE83_COM1_ROUTE_ACTIVE_DURING_RX=1',
        'GXOS_NET10:PHASE83_COM1_REMAINS_ISOLATED=1',
        'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')) {
        Require89 $serial.Contains($marker) "Boot $boot missing marker: $marker"
    }
    Require89 ($command.Contains('-netdev dgram,id=net0') -and
               $command.Contains('e1000e,netdev=net0,addr=2')) `
        "Boot $boot did not use the existing local dgram/e1000e topology."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_GUEST_MAC') -eq 0x525400123456) `
        "Boot $boot guest MAC mismatch."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_LOCAL_IPV4') -eq 0x0A0F0001) `
        "Boot $boot local IPv4 mismatch."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_UDP_MANAGED_DELIVERIES') -eq 4) `
        "Boot $boot did not deliver four typed UDP RX events."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_UDP_PACKETS_PARSED') -eq 4 -and
               (Marker89 $serial 'GXOS_NET10:PHASE89_UDP_VALID_PACKETS') -eq 4) `
        "Boot $boot did not parse and validate exactly four UDP datagrams."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_UDP_BOUND_CLASSIFICATIONS') -eq 3 -and
               (Marker89 $serial 'GXOS_NET10:PHASE89_UDP_UNBOUND_CLASSIFICATIONS') -eq 1) `
        "Boot $boot bound/unbound UDP classifications did not reconcile."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_UDP_MALFORMED_DROPS') -eq 0 -and
               (Marker89 $serial 'GXOS_NET10:PHASE89_UDP_CHECKSUM_FAILURES') -eq 0) `
        "Boot $boot reported a malformed UDP packet or checksum failure."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_UDP_UNKNOWN_PORTS') -eq 1 -and
               (Marker89 $serial 'GXOS_NET10:PHASE89_UDP_BOUND_CONSUMER_DELIVERIES') -eq 3) `
        "Boot $boot unknown-port and bound-consumer counts did not reconcile."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_RX_DESCRIPTORS_COMPLETED') -eq 4) `
        "Boot $boot did not complete four RX descriptors."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_UDP_RX_EVENTS') -ge 4) `
        "Boot $boot did not capture four or more independent RXT0 causes."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_NATIVE_RXT0_CAPTURES') -ge 4) `
        "Boot $boot lacks native RXT0 capture evidence."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_NATIVE_TXDW_CAPTURES') -ge 0) `
        "Boot $boot lacks separate TXDW accounting."
    Require89 $serial.Contains('GXOS_NET10:PHASE89_INTERRUPT_STORM=0') `
        "Boot $boot reported an interrupt storm."
    Require89 ((Marker89 $serial 'GXOS_NET10:PHASE89_RX_RDT_AFTER_RECYCLE') -eq 3) `
        "Boot $boot did not recycle the last RX descriptor."
    Require89 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE89_UDP_DELIVERY=')).Count -eq 4) `
        "Boot $boot did not log exactly four ordered UDP deliveries."
    Require89 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE89_UDP_BOUND_DELIVERY=1')).Count -eq 3) `
        "Boot $boot did not deliver exactly three bound datagrams."
    Require89 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE89_UDP_UNBOUND_IGNORED=1')).Count -eq 1) `
        "Boot $boot did not classify exactly one unbound datagram."
    Require89 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE89_RX_DESCRIPTOR_RECYCLED=1')).Count -eq 4) `
        "Boot $boot did not recycle all four descriptors."
    Require89 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE89_UDP_CHECKSUM=0x(?!0{16})')).Count -eq 4) `
        "Boot $boot did not log four nonzero UDP checksums."
    Require89 (([regex]::Matches($injections,
        'PHASE89_UDP_FRAME_INJECTED=PASS')).Count -eq 4) `
        "Boot $boot injector did not send exactly four frames."
    Require89 ($injections.Contains(
        'PHASE89_UDP_INJECTION_SUMMARY=PASS frames=4 bound=3 unbound=1 external_network=false')) `
        "Boot $boot injector summary did not reconcile."
    $deliveries = [regex]::Matches($serial,
        'GXOS_NET10:PHASE89_SEQUENCE=0x([0-9A-Fa-f]+)')
    Require89 ($deliveries.Count -eq 4) "Boot $boot sequence proof count mismatch."
    for ($index = 0; $index -lt 4; $index++) {
        Require89 ([Convert]::ToUInt64($deliveries[$index].Groups[1].Value, 16) -eq ($index + 1)) `
            "Boot $boot duplicated or reordered UDP sequence $($index + 1)."
        $sourceIp = [uint64](0x0A0F0002 + $index)
        $destinationPort = if ($index -lt 3) { 15890 } else { 15891 }
        $sourcePort = 24001 + $index
        $sourceIpMarker = 'GXOS_NET10:PHASE89_SOURCE_IPV4=0x{0:X16}' -f $sourceIp
        $destinationIpMarker = 'GXOS_NET10:PHASE89_DESTINATION_IPV4=0x000000000A0F0001'
        $sourcePortMarker = 'GXOS_NET10:PHASE89_SOURCE_PORT=0x{0:X16}' -f $sourcePort
        $destinationPortMarker = 'GXOS_NET10:PHASE89_DESTINATION_PORT=0x{0:X16}' -f $destinationPort
        Require89 $serial.Contains($sourceIpMarker) `
            "Boot $boot did not report source IPv4 for sequence $($index + 1)."
        Require89 $serial.Contains($destinationIpMarker) `
            "Boot $boot did not report local destination IPv4 for sequence $($index + 1)."
        Require89 $serial.Contains($sourcePortMarker) `
            "Boot $boot did not report source UDP port for sequence $($index + 1)."
        Require89 $serial.Contains($destinationPortMarker) `
            "Boot $boot did not report destination UDP port for sequence $($index + 1)."
    }
    Require89 ($timeline.Contains('PHASE89_UDP_INJECTOR_COMPLETED') -and
               $timeline.Contains('marker=GXOS_NET10:PHASE89_UDP_HARDWARE_INGRESS_PASS=1')) `
        "Boot $boot timeline lacks injector and positive guest completion events."
    $summaries += [pscustomobject]@{
        Boot = $boot
        GuestMac = '52:54:00:12:34:56'
        LocalIpv4 = '10.15.0.1'
        Injected = 4
        Parsed = 4
        Bound = 3
        Unbound = 1
        Recycled = 4
        Rxt0 = (Marker89 $serial 'GXOS_NET10:PHASE89_NATIVE_RXT0_CAPTURES')
        Txdw = (Marker89 $serial 'GXOS_NET10:PHASE89_NATIVE_TXDW_CAPTURES')
        SerialSha256 = (Get-FileHash $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
        InjectionSha256 = (Get-FileHash $injectionsPath -Algorithm SHA256).Hash.ToUpperInvariant()
        TimelineSha256 = (Get-FileHash $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}

$summaries | Format-Table -AutoSize | Out-String | Set-Content `
    -LiteralPath (Join-Path $evidence 'phase89-summary.txt') -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE89_QEMU_RUNS=$RunCount/$RunCount"
Write-Output "MANAGED_KERNEL_PHASE89_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE89_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE89_EVIDENCE=$evidence"
$summaries | Format-Table -AutoSize

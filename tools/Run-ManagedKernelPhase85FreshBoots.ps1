[CmdletBinding()]
param(
    [string]$BuildDirectory = '',
    [string]$EvidenceDirectory = '',
    [int]$TimeoutSeconds = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $root 'artifacts\phase85-build'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'artifacts\phase85-qemu-evidence'
}
$build = [IO.Path]::GetFullPath($BuildDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$payloadDirectory = Join-Path $build 'managed-kernel'
$normalGate = Join-Path $build 'gate4-normal'
$phase85Gate = Join-Path $build 'gate4-phase85'
$managedBuild = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$gateBuild = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$bootRunner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'

function Require85([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

function Get-MarkerHex85([string]$text, [string]$name) {
    $match = [regex]::Match($text,
        [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    Require85 $match.Success "Missing numeric acceptance marker: $name"
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
$managedOutput = @(& $managedBuild -OutputDirectory $payloadDirectory)
$payload = Join-Path $payloadDirectory 'publish\gxos-managed-kernel.dll'
Require85 (Test-Path -LiteralPath $payload) `
    'ManagedKernel NativeAOT build did not produce the payload.'
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length
$managedOutput | Set-Content -LiteralPath `
    (Join-Path $build 'managed-kernel-build-output.log') -Encoding ascii

$normalOutput = @(& $gateBuild -OutputDirectory $normalGate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -AssumeUnspecifiedTimezoneUtc)
$normalOutput | Set-Content -LiteralPath `
    (Join-Path $build 'gate4-normal-build-output.log') -Encoding ascii

$acceptanceOutput = @(& $gateBuild -OutputDirectory $phase85Gate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -EnablePhase83E1000Rx -EnablePhase85ManagedArp `
    -AssumeUnspecifiedTimezoneUtc)
$acceptanceOutput | Set-Content -LiteralPath `
    (Join-Path $build 'gate4-phase85-build-output.log') -Encoding ascii

$bootOutput = @(& $bootRunner -GateDirectory $phase85Gate `
    -EvidenceDirectory $evidence -PayloadSha256 $payloadHash `
    -PayloadSize $payloadSize -RunCount 3 -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
    -EnablePhase80E1000 -EnablePhase83 -EnablePhase85Arp)
$bootOutput | Set-Content -LiteralPath `
    (Join-Path $build 'qemu-runner-output.log') -Encoding ascii

$summaries = @()
for ($boot = 1; $boot -le 3; $boot++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $boot)
    $serialPath = Join-Path $run 'serial.log'
    $injectionPath = Join-Path $run 'injections.log'
    $timelinePath = Join-Path $run 'timeline.log'
    $captureLogPath = Join-Path $run 'arp-tx-capture.log'
    $captureBinaryPath = Join-Path $run 'arp-tx-capture.bin'
    foreach ($path in @($serialPath, $injectionPath, $timelinePath,
                        $captureLogPath, $captureBinaryPath)) {
        Require85 (Test-Path -LiteralPath $path) `
            "Boot $boot evidence file is missing: $path"
    }
    $serial = [IO.File]::ReadAllText($serialPath)
    $injections = [IO.File]::ReadAllText($injectionPath)
    $captureLog = [IO.File]::ReadAllText($captureLogPath)
    $required = @(
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_INITIALIZED',
        'GXOS_NET10:PHASE85_ARP_MODE_SELECTED=1',
        'GXOS_NET10:PHASE85_ARP_HANDLER_READY=1',
        'GXOS_NET10:PHASE85_ARP_RX_READY=1',
        'GXOS_NET10:PHASE85_ARP_REQUEST_NOT_FOR_US=1',
        'GXOS_NET10:PHASE85_RX_DESCRIPTOR_RECYCLE_PASS=1',
        'GXOS_NET10:PHASE85_HARDWARE_ARP_INGRESS_PASS=1',
        'GXOS_NET10:PHASE83_COM1_ROUTE_ACTIVE_DURING_RX=1',
        'GXOS_NET10:PHASE83_COM1_REMAINS_ISOLATED=1',
        'GXOS_NET10:PHASE83_TXDW_REGRESSION=4',
        'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')
    foreach ($marker in $required) {
        Require85 $serial.Contains($marker) `
            "Boot $boot missing marker: $marker"
    }
    Require85 ((Get-MarkerHex85 $serial 'GXOS_NET10:PHASE85_GUEST_MAC') -eq
               0x525400123456) "Boot $boot reported the wrong E1000 MAC."
    Require85 ((Get-MarkerHex85 $serial 'GXOS_NET10:PHASE85_LOCAL_IPV4') -eq
               0x0A0F0001) "Boot $boot reported the wrong existing local IPv4."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_ARP_MANAGED_DELIVERIES') -eq 4) `
        "Boot $boot did not deliver four ARP frames."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_RX_DESCRIPTORS_COMPLETED') -eq 4) `
        "Boot $boot did not complete four RX descriptors."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_ARP_REPLY_TX_RESULTS') -eq 3) `
        "Boot $boot did not submit exactly three normal-path ARP replies."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_ETHERNET_RECEIVED') -eq 4 -and
               (Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_ETHERNET_PARSED') -eq 4) `
        "Boot $boot did not parse four frames in the managed Ethernet layer."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_RX_RDT_AFTER_RECYCLE') -eq 3) `
        "Boot $boot did not return the final RX descriptor to hardware."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_NATIVE_RXT0_CAPTURES') -eq 4) `
        "Boot $boot did not capture four real RXT0 causes."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_NATIVE_TXDW_CAPTURES') -ge 3) `
        "Boot $boot did not preserve TXDW captures for the replies."
    Require85 ((Get-MarkerHex85 $serial `
        'GXOS_NET10:PHASE85_NATIVE_INTERRUPT_CAPTURES') -ge 7) `
        "Boot $boot did not account for the RX and TX interrupt causes."

    $deliveries = [regex]::Matches(
        $serial, 'GXOS_NET10:PHASE85_ARP_DELIVERY=0x([0-9A-Fa-f]+)')
    Require85 ($deliveries.Count -eq 4) `
        "Boot $boot did not deliver exactly four typed RX events to ARP."
    for ($index = 0; $index -lt 4; $index++) {
        Require85 ([Convert]::ToUInt64(
            $deliveries[$index].Groups[1].Value, 16) -eq ($index + 1)) `
            "Boot $boot duplicated or reordered ARP delivery $($index + 1)."
        $descriptor = 'GXOS_NET10:PHASE85_RX_DESCRIPTOR_INDEX=0x{0:X16}' -f $index
        Require85 $serial.Contains($descriptor) `
            "Boot $boot did not consume RX descriptor $index."
    }
    foreach ($field in @(
        @{ Name = 'GXOS_NET10:PHASE85_ETHERNET_DESTINATION'; Value = 0xFFFFFFFFFFFF },
        @{ Name = 'GXOS_NET10:PHASE85_ETHERNET_ETHERTYPE'; Value = 0x0806 },
        @{ Name = 'GXOS_NET10:PHASE85_ARP_PAYLOAD_LENGTH'; Value = 46 },
        @{ Name = 'GXOS_NET10:PHASE85_ARP_HARDWARE_TYPE'; Value = 1 },
        @{ Name = 'GXOS_NET10:PHASE85_ARP_PROTOCOL_TYPE'; Value = 0x0800 },
        @{ Name = 'GXOS_NET10:PHASE85_ARP_HLEN'; Value = 6 },
        @{ Name = 'GXOS_NET10:PHASE85_ARP_PLEN'; Value = 4 },
        @{ Name = 'GXOS_NET10:PHASE85_ARP_OPCODE'; Value = 1 },
        @{ Name = 'GXOS_NET10:PHASE85_ARP_TARGET_MAC'; Value = 0 })) {
        $observed = [regex]::Matches(
            $serial, [regex]::Escape($field.Name) + '=0x([0-9A-Fa-f]+)')
        Require85 ($observed.Count -eq 4) `
            "Boot $boot did not record the field $($field.Name) four times."
        foreach ($match in $observed) {
            Require85 ([Convert]::ToUInt64($match.Groups[1].Value, 16) -eq
                       [uint64]$field.Value) `
                "Boot $boot reported an unexpected value for $($field.Name)."
        }
    }
    for ($index = 0; $index -lt 4; $index++) {
        $senderMac = [uint64](0x021500000000 + ($index + 1))
        $senderIpv4 = [uint64](0x0A0F0002 + $index)
        $targetIpv4 = if ($index -lt 3) { [uint64]0x0A0F0001 } else { [uint64]0x0A0F0063 }
        $senderMacText = 'GXOS_NET10:PHASE85_ARP_SENDER_MAC=0x{0:X16}' -f $senderMac
        $senderIpText = 'GXOS_NET10:PHASE85_ARP_SENDER_IPV4=0x{0:X16}' -f $senderIpv4
        $targetIpText = 'GXOS_NET10:PHASE85_ARP_TARGET_IPV4=0x{0:X16}' -f $targetIpv4
        Require85 $serial.Contains($senderMacText) `
            "Boot $boot sender MAC $index was stale or incorrect."
        Require85 $serial.Contains($senderIpText) `
            "Boot $boot sender IPv4 $index was stale or incorrect."
        Require85 $serial.Contains($targetIpText) `
            "Boot $boot target IPv4 $index was stale or incorrect."
    }
    Require85 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE85_ARP_REQUEST_FOR_LOCAL=1')).Count -eq 3) `
        "Boot $boot did not classify exactly three requests for the local IP."
    Require85 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE85_ARP_REQUEST_NOT_FOR_US=1')).Count -eq 1) `
        "Boot $boot did not classify exactly one not-for-us request."
    Require85 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE85_RX_DESCRIPTOR_RECYCLED=1')).Count -eq 4) `
        "Boot $boot did not recycle every completed ARP descriptor."
    Require85 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE85_ARP_REPLY_TX_COMPLETED=1')).Count -eq 3) `
        "Boot $boot did not complete all three reply TX submissions."

    Require85 (([regex]::Matches($injections,
        'PHASE85_ARP_REQUEST_INJECTED=PASS')).Count -eq 4) `
        "Boot $boot did not inject four deterministic ARP requests."
    Require85 (([regex]::Matches($injections,
        'PHASE85_QEMU_ARP_REPLY_CAPTURED=PASS')).Count -eq 3) `
        "Boot $boot did not capture exactly three QEMU E1000 replies."
    Require85 ($injections.Contains(
        'PHASE85_ARP_INJECTION_SUMMARY=PASS requests=4 local_targets=3 not_for_us=1 captured_replies=3 external_network=false')) `
        "Boot $boot injection/reply summary did not match the request matrix."
    $captureDatagrams = [regex]::Matches(
        $captureLog, 'PHASE85_QEMU_TX_DATAGRAM sequence=')
    Require85 ($captureDatagrams.Count -ge 3) `
        "Boot $boot did not preserve raw QEMU TX datagrams."

    $serialHash = (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $injectionHash = (Get-FileHash -LiteralPath $injectionPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $timelineHash = (Get-FileHash -LiteralPath $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $captureLogHash = (Get-FileHash -LiteralPath $captureLogPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $captureBinaryHash = (Get-FileHash -LiteralPath $captureBinaryPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $summaries += [pscustomobject]@{
        Boot = $boot
        GuestMac = '525400123456'
        LocalIpv4 = '10.15.0.1'
        Requests = 4
        ManagedArpDeliveries = 4
        RepliesCaptured = 3
        SerialSha256 = $serialHash
        InjectionSha256 = $injectionHash
        ReplyLogSha256 = $captureLogHash
        ReplyBytesSha256 = $captureBinaryHash
        TimelineSha256 = $timelineHash
    }
}

$summaries | Format-Table -AutoSize | Out-String |
    Set-Content -LiteralPath (Join-Path $evidence 'phase85-summary.txt') -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE85_QEMU_RUNS=3/3"
Write-Output "MANAGED_KERNEL_PHASE85_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE85_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE85_EVIDENCE=$evidence"
$summaries | Format-Table -AutoSize

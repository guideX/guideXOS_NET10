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
    $BuildDirectory = Join-Path $root 'artifacts\phase86-build'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'artifacts\phase86-qemu-evidence'
}
$build = [IO.Path]::GetFullPath($BuildDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$payloadDirectory = Join-Path $build 'managed-kernel'
$normalGate = Join-Path $build 'gate4-normal'
$phase86Gate = Join-Path $build 'gate4-phase86'
$managedBuild = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$gateBuild = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$bootRunner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'

function Require86([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}
function Marker86([string]$text, [string]$name) {
    $match = [regex]::Match($text,
        [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    Require86 $match.Success "Missing numeric marker: $name"
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
$managedOutput = @(& $managedBuild -OutputDirectory $payloadDirectory)
$payload = Join-Path $payloadDirectory 'publish\gxos-managed-kernel.dll'
Require86 (Test-Path -LiteralPath $payload) 'ManagedKernel NativeAOT payload is missing.'
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length
$managedOutput | Set-Content -LiteralPath (Join-Path $build 'managed-kernel-build-output.log') -Encoding ascii

$normalOutput = @(& $gateBuild -OutputDirectory $normalGate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -AssumeUnspecifiedTimezoneUtc)
$normalOutput | Set-Content -LiteralPath (Join-Path $build 'gate4-normal-build-output.log') -Encoding ascii
$acceptanceOutput = @(& $gateBuild -OutputDirectory $phase86Gate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -EnablePhase83E1000Rx -EnablePhase86ManagedIpv4 `
    -AssumeUnspecifiedTimezoneUtc)
$acceptanceOutput | Set-Content -LiteralPath (Join-Path $build 'gate4-phase86-build-output.log') -Encoding ascii
$bootOutput = @(& $bootRunner -GateDirectory $phase86Gate `
    -EvidenceDirectory $evidence -PayloadSha256 $payloadHash `
    -PayloadSize $payloadSize -RunCount 3 -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
    -EnablePhase80E1000 -EnablePhase83 -EnablePhase86Ipv4)
$bootOutput | Set-Content -LiteralPath (Join-Path $build 'qemu-runner-output.log') -Encoding ascii

$summaries = @()
for ($boot = 1; $boot -le 3; $boot++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $boot)
    $serialPath = Join-Path $run 'serial.log'
    $injectionPath = Join-Path $run 'injections.log'
    $timelinePath = Join-Path $run 'timeline.log'
    foreach ($path in @($serialPath, $injectionPath, $timelinePath)) {
        Require86 (Test-Path -LiteralPath $path) "Boot $boot evidence is missing: $path"
    }
    $serial = [IO.File]::ReadAllText($serialPath)
    $injections = [IO.File]::ReadAllText($injectionPath)
    foreach ($marker in @(
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_INITIALIZED',
        'GXOS_NET10:PHASE86_IPV4_MODE_SELECTED=1',
        'GXOS_NET10:PHASE86_IPV4_PARSER_READY=1',
        'GXOS_NET10:PHASE86_IPV4_NOT_FOR_US=1',
        'GXOS_NET10:PHASE86_RX_DESCRIPTOR_RECYCLE_PASS=1',
        'GXOS_NET10:PHASE86_HARDWARE_IPV4_INGRESS_PASS=1',
        'GXOS_NET10:PHASE83_COM1_ROUTE_ACTIVE_DURING_RX=1',
        'GXOS_NET10:PHASE83_COM1_REMAINS_ISOLATED=1',
        'GXOS_NET10:PHASE83_TXDW_REGRESSION=4',
        'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')) {
        Require86 $serial.Contains($marker) "Boot $boot missing marker: $marker"
    }
    Require86 ((Marker86 $serial 'GXOS_NET10:PHASE86_GUEST_MAC') -eq 0x525400123456) `
        "Boot $boot guest MAC mismatch."
    Require86 ((Marker86 $serial 'GXOS_NET10:PHASE86_LOCAL_IPV4') -eq 0x0A0F0001) `
        "Boot $boot did not use authoritative local IPv4 10.15.0.1."
    foreach ($name in @(
        'GXOS_NET10:PHASE86_IPV4_MANAGED_DELIVERIES',
        'GXOS_NET10:PHASE86_IPV4_PARSED_FRAMES',
        'GXOS_NET10:PHASE86_RX_DESCRIPTORS_COMPLETED',
        'GXOS_NET10:PHASE86_ETHERNET_RECEIVED',
        'GXOS_NET10:PHASE86_ETHERNET_PARSED')) {
        Require86 ((Marker86 $serial $name) -eq 4) "Boot $boot expected four for $name."
    }
    Require86 ((Marker86 $serial 'GXOS_NET10:PHASE86_IPV4_LOCAL_DELIVERIES') -eq 3) `
        "Boot $boot expected three local IPv4 observer deliveries."
    Require86 ((Marker86 $serial 'GXOS_NET10:PHASE86_IPV4_NOT_FOR_US_COUNT') -eq 1) `
        "Boot $boot expected one valid not-for-us IPv4 packet."
    Require86 ((Marker86 $serial 'GXOS_NET10:PHASE86_UNSUPPORTED_PROTOCOL_PACKETS') -eq 3) `
        "Boot $boot expected three valid local packets classified as unsupported protocol."
    Require86 ((Marker86 $serial 'GXOS_NET10:PHASE86_RX_RDT_AFTER_RECYCLE') -eq 3) `
        "Boot $boot did not recycle all descriptors."
    $deliveries = [regex]::Matches($serial,
        'GXOS_NET10:PHASE86_IPV4_DELIVERY=0x([0-9A-Fa-f]+)')
    Require86 ($deliveries.Count -eq 4) "Boot $boot did not log four deliveries."
    for ($index = 0; $index -lt 4; $index++) {
        Require86 ([Convert]::ToUInt64($deliveries[$index].Groups[1].Value, 16) -eq ($index + 1)) `
            "Boot $boot duplicated or reordered frame $($index + 1)."
        $source = [uint64](0x0A0F0001 + $index + 1)
        $destination = if ($index -lt 3) { [uint64]0x0A0F0001 } else { [uint64]0x0A0F0063 }
        foreach ($field in @(
            @{ Name = 'GXOS_NET10:PHASE86_ETHERTYPE'; Value = [uint64]0x0800 },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_SOURCE'; Value = $source },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_DESTINATION'; Value = $destination },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_IDENTIFICATION'; Value = [uint64](0x8601 + $index) },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_TOTAL_LENGTH'; Value = [uint64]24 },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_HEADER_LENGTH'; Value = [uint64]20 },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_TTL'; Value = [uint64]63 },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_PROTOCOL'; Value = [uint64]253 },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_PAYLOAD_LENGTH'; Value = [uint64]4 },
            @{ Name = 'GXOS_NET10:PHASE86_IPV4_SENTINEL_SEQUENCE'; Value = [uint64]($index + 1) })) {
            $observed = [regex]::Matches($serial,
                [regex]::Escape($field.Name) + '=0x([0-9A-Fa-f]+)')
            Require86 ($observed.Count -eq 4 -and
                [Convert]::ToUInt64($observed[$index].Groups[1].Value, 16) -eq $field.Value) `
                "Boot $boot metadata mismatch for $($field.Name), frame $($index + 1)."
        }
    }
    Require86 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE86_IPV4_CHECKSUM_VALID=1')).Count -eq 4) `
        "Boot $boot did not validate all four header checksums."
    Require86 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE86_RX_DESCRIPTOR_RECYCLED=1')).Count -eq 4) `
        "Boot $boot did not recycle all four descriptors."
    Require86 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE86_IPV4_LOCAL_DELIVERY=1')).Count -eq 3) `
        "Boot $boot local delivery count mismatch."
    Require86 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE86_IPV4_NOT_FOR_US=1')).Count -eq 1) `
        "Boot $boot not-for-us count mismatch."
    Require86 (([regex]::Matches($injections,
        'PHASE86_IPV4_FRAME_INJECTED=PASS')).Count -eq 4) `
        "Boot $boot did not inject four IPv4 frames."
    Require86 $injections.Contains(
        'PHASE86_IPV4_INJECTION_SUMMARY=PASS frames=4 local=3 not_for_us=1') `
        "Boot $boot injection summary mismatch."
    $summaries += [pscustomobject]@{
        Boot = $boot
        GuestMac = '525400123456'
        LocalIpv4 = '10.15.0.1'
        Frames = 4
        LocalDeliveries = 3
        NotForUs = 1
        SerialSha256 = (Get-FileHash $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
        InjectionSha256 = (Get-FileHash $injectionPath -Algorithm SHA256).Hash.ToUpperInvariant()
        TimelineSha256 = (Get-FileHash $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}
$summaries | Format-Table -AutoSize | Out-String | Set-Content `
    -LiteralPath (Join-Path $evidence 'phase86-summary.txt') -Encoding ascii
Write-Output 'MANAGED_KERNEL_PHASE86_QEMU_RUNS=3/3'
Write-Output "MANAGED_KERNEL_PHASE86_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE86_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE86_EVIDENCE=$evidence"
$summaries | Format-Table -AutoSize

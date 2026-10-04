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
    $BuildDirectory = Join-Path $root 'artifacts\phase83-build'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'artifacts\phase83-qemu-evidence'
}
$build = [IO.Path]::GetFullPath($BuildDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$normalGate = Join-Path $build 'gate4-normal'
$phase83Gate = Join-Path $build 'gate4-phase83'
$managedBuild = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$gateBuild = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$bootRunner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'

function Require83([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
$managedOutput = @(& $managedBuild -OutputDirectory (Join-Path $build 'managed-kernel'))
$payload = Join-Path (Join-Path $build 'managed-kernel\publish') 'gxos-managed-kernel.dll'
Require83 (Test-Path -LiteralPath $payload) 'ManagedKernel NativeAOT build did not produce the payload.'
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length
$managedOutput | Set-Content -LiteralPath (Join-Path $build 'managed-kernel-build-output.log') -Encoding ascii

# Verify the normal Gate4 image compiles without the RX acceptance switch.
$normalOutput = @(& $gateBuild -OutputDirectory $normalGate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel -EnableNativeAotStartup)
$normalOutput | Set-Content -LiteralPath (Join-Path $build 'gate4-normal-build-output.log') -Encoding ascii

# The Phase83 RXT0 injector drives the Phase84 managed Ethernet receive path.
$phase83Output = @(& $gateBuild -OutputDirectory $phase83Gate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -EnablePhase83E1000Rx -AssumeUnspecifiedTimezoneUtc)
$phase83Output | Set-Content -LiteralPath (Join-Path $build 'gate4-phase83-build-output.log') -Encoding ascii

& $bootRunner -GateDirectory $phase83Gate -EvidenceDirectory $evidence `
    -PayloadSha256 $payloadHash -PayloadSize $payloadSize -RunCount 3 `
    -TimeoutSeconds $TimeoutSeconds -PostPhase11Marker `
        'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' -EnablePhase80E1000 -EnablePhase83

function Get-MarkerHex83([string]$text, [string]$name) {
    $match = [regex]::Match($text, [regex]::Escape($name) + '=0x([0-9A-Fa-f]+)')
    Require83 $match.Success "Missing numeric acceptance marker: $name"
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

$runSummaries = @()
for ($boot = 1; $boot -le 3; $boot++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $boot)
    $serialPath = Join-Path $run 'serial.log'
    $injectionPath = Join-Path $run 'injections.log'
    $commandPath = Join-Path $run 'qemu-commandline.log'
    Require83 (Test-Path -LiteralPath $serialPath) "Boot $boot serial log is missing."
    Require83 (Test-Path -LiteralPath $injectionPath) "Boot $boot injection log is missing."
    $serial = [IO.File]::ReadAllText($serialPath)
    $injection = [IO.File]::ReadAllText($injectionPath)
    $qemuCommand = [IO.File]::ReadAllText($commandPath)
    $required = @(
        'GXOS_NET10:PHASE83_RX_MODE_SELECTED=1',
        'GXOS_NET10:PHASE83_COM1_ROUTE_READY=1',
        'GXOS_NET10:PHASE80_E1000_MANAGED_DELIVERY=4',
        'GXOS_NET10:PHASE83_TXDW_REGRESSION=4',
        'GXOS_NET10:PHASE83_RX_READY=1',
        'GXOS_NET10:PHASE83_RX_MANAGED_PROOF=1',
        'GXOS_NET10:PHASE84_ETHERNET_INTEGRATION_PASS=1',
        'GXOS_NET10:PHASE84_CROSS_FRAME_SENTINELS_DISTINCT=1',
        'GXOS_NET10:PHASE83_RX_RING_ADVANCE_AND_RECYCLE_OK=1',
        'GXOS_NET10:PHASE83_COM1_ROUTE_ACTIVE_DURING_RX=1',
        'GXOS_NET10:PHASE83_COM1_REMAINS_ISOLATED=1',
        'GXOS_NET10:PHASE83_INTERRUPT_STORM=0',
        'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')
    foreach ($marker in $required) {
        Require83 $serial.Contains($marker) "Boot $boot missing marker: $marker"
    }
    for ($frame = 1; $frame -le 4; $frame++) {
        $accepted = 'GXOS_NET10:PHASE83_RX_ACCEPTED=0x{0:X16}' -f $frame
        Require83 $serial.Contains($accepted) "Boot $boot missing RX delivery $frame."
        $descriptor = 'GXOS_NET10:PHASE83_RX_DESCRIPTOR_INDEX=0x{0:X16}' -f ($frame - 1)
        Require83 $serial.Contains($descriptor) `
            "Boot $boot did not consume the expected descriptor index $($frame - 1)."
    }
    Require83 (([regex]::Matches($serial, 'GXOS_NET10:PHASE83_RX_FRAME_FIELDS_VALID=1')).Count -eq 4) `
        "Boot $boot did not validate every injected frame."
    Require83 (([regex]::Matches($serial, 'GXOS_NET10:PHASE83_RX_DESCRIPTOR_RECYCLED=1')).Count -eq 4) `
        "Boot $boot did not recycle each of the four RX descriptors."
    Require83 (([regex]::Matches($injection, 'PHASE83_FRAME_INJECTED=PASS')).Count -eq 4) `
        "Boot $boot host injection log does not contain four frame records."
    $guestMac = [regex]::Match($serial,
        'GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})\s*([0-9A-Fa-f]{8})')
    Require83 $guestMac.Success "Boot $boot did not report the guest MAC."
    $guestMacText = ($guestMac.Groups[1].Value + $guestMac.Groups[2].Value).ToUpperInvariant()
    Require83 ($injection.Contains("destination=$guestMacText ")) `
        "Boot $boot injector destination does not match the configured guest MAC."
    Require83 ($qemuCommand.Contains('-netdev dgram,id=net0') -and
               $qemuCommand.Contains('e1000e,netdev=net0,addr=2')) `
        "Boot $boot did not use the local QEMU dgram/e1000e path."

    $maskBefore = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_IMS_BEFORE_ENABLE'
    $maskAfter = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_IMS_AFTER_ENABLE'
    $maskExpected = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_RX_MASK'
    $nativeMsi = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_NATIVE_MSI_ENTRIES'
    $nativeRx = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_NATIVE_RX_CAPTURES'
    $rawCause = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_RAW_ICR_CAUSE'
    $expectedCause = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_EXPECTED_RX_CAUSE'
    $acknowledged = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_RX_CAUSE_ACKNOWLEDGEMENTS'
    $managedRx = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_RX_MANAGED_DELIVERIES'
    $rxStatus = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_RX_LAST_DESCRIPTOR_STATUS'
    $rxErrors = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_RX_LAST_DESCRIPTOR_ERRORS'
    $rxLength = Get-MarkerHex83 $serial 'GXOS_NET10:PHASE83_RX_LAST_FRAME_LENGTH'
    Require83 (($maskBefore -band 0x81) -eq 0 -and $maskAfter -eq 0x81 -and $maskExpected -eq 0x81) `
        "Boot $boot did not retain exactly TXDW plus RXT0 in IMS."
    Require83 ($nativeMsi -eq 4 -and $nativeRx -eq 4 -and
               (($rawCause -band 0x80) -ne 0) -and $expectedCause -eq 0x80 -and
               $acknowledged -eq 4) `
        "Boot $boot did not capture and acknowledge four authoritative RXT0 causes."
    Require83 ($managedRx -eq 4 -and $rxStatus -eq 3 -and
               $rxErrors -eq 0 -and $rxLength -eq 60) `
        "Boot $boot managed descriptor completion or frame bounds failed."

    $phase84Expected = @{
        'GXOS_NET10:PHASE84_RX_DESCRIPTORS_COMPLETED' = 4
        'GXOS_NET10:PHASE84_ETHERNET_RECEIVED' = 4
        'GXOS_NET10:PHASE84_ETHERNET_PARSED' = 4
        'GXOS_NET10:PHASE84_TEST_CONSUMER_DELIVERIES' = 4
        'GXOS_NET10:PHASE84_MALFORMED_DROPS' = 0
        'GXOS_NET10:PHASE84_UNSUPPORTED_ETHER_TYPE_DROPS' = 0
        'GXOS_NET10:PHASE84_CONSUMER_FAILURES' = 0
        'GXOS_NET10:PHASE84_LAST_DESTINATION' = 0x525400123456
        'GXOS_NET10:PHASE84_LAST_SOURCE' = 0x021500000001
        'GXOS_NET10:PHASE84_LAST_ETHERTYPE' = 0x88B5
        'GXOS_NET10:PHASE84_LAST_PAYLOAD_LENGTH' = 46
        'GXOS_NET10:PHASE84_LAST_SENTINEL' = 4
    }
    foreach ($markerName in $phase84Expected.Keys) {
        $observed = Get-MarkerHex83 $serial $markerName
        Require83 ($observed -eq $phase84Expected[$markerName]) `
            "Boot $boot Phase84 marker $markerName was $observed."
    }
    foreach ($markerName in @(
        'GXOS_NET10:PHASE84_NORMAL_ETHERNET_PATH=1',
        'GXOS_NET10:PHASE84_ETHERTYPE_CONSUMED=1',
        'GXOS_NET10:PHASE84_ETHERNET_DESTINATION=0x0000525400123456',
        'GXOS_NET10:PHASE84_ETHERNET_SOURCE=0x0000021500000001',
        'GXOS_NET10:PHASE84_ETHERNET_ETHERTYPE=0x00000000000088B5',
        'GXOS_NET10:PHASE84_ETHERNET_PAYLOAD_LENGTH=0x000000000000002E')) {
        Require83 (([regex]::Matches($serial, [regex]::Escape($markerName))).Count -eq 4) `
            "Boot $boot did not record four consistent Phase84 metadata values for $markerName."
    }
    $sentinels = [regex]::Matches(
        $serial, 'GXOS_NET10:PHASE84_ETHERNET_SENTINEL=0x([0-9A-Fa-f]+)')
    Require83 ($sentinels.Count -eq 4) `
        "Boot $boot did not record four payload sentinels in the Ethernet consumer."
    for ($index = 0; $index -lt $sentinels.Count; $index++) {
        Require83 ([Convert]::ToUInt64($sentinels[$index].Groups[1].Value, 16) -eq ($index + 1)) `
            "Boot $boot repeated or reordered a Phase84 payload sentinel."
    }
    $serialHash = (Get-FileHash -LiteralPath $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $injectionHash = (Get-FileHash -LiteralPath $injectionPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $timelinePath = Join-Path $run 'timeline.log'
    $timelineHash = (Get-FileHash -LiteralPath $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $runSummaries += [pscustomobject]@{
        Boot = $boot; SerialSha256 = $serialHash
        InjectionSha256 = $injectionHash; TimelineSha256 = $timelineHash
        NativeMsi = $nativeMsi; RxCause = ('0x{0:X}' -f $rawCause)
        ManagedRx = $managedRx; RingCount = 16; BufferSize = 2048
    }
}

$runSummaries | Format-Table -AutoSize | Out-String | Write-Output
Write-Output "PHASE83_MANAGED_PAYLOAD_SHA256=$payloadHash"
Write-Output "PHASE83_MANAGED_PAYLOAD_SIZE=$payloadSize"
Write-Output "PHASE83_NORMAL_GATE4_BUILD=PASS path=$normalGate"
Write-Output "PHASE83_ACCEPTANCE_GATE4_BUILD=PASS path=$phase83Gate"
Write-Output "PHASE83_QEMU_BOOTS=3/3 evidence=$evidence"

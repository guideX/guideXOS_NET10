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
    $BuildDirectory = Join-Path $root 'artifacts\phase88-build'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'artifacts\phase88-qemu-evidence'
}
$build = [IO.Path]::GetFullPath($BuildDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$managedBuild = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$gateBuild = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$bootRunner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$payloadDirectory = Join-Path $build 'managed-kernel'
$payload = Join-Path $payloadDirectory 'publish\gxos-managed-kernel.dll'
$normalGate = Join-Path $build 'gate4-normal'
$acceptanceGate = Join-Path $build 'gate4-phase88'

function Require88([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
$managedOutput = @(& $managedBuild -OutputDirectory $payloadDirectory)
$managedOutput | Set-Content -LiteralPath (Join-Path $build 'managed-kernel-build-output.log') -Encoding ascii
Require88 (Test-Path -LiteralPath $payload) 'ManagedKernel NativeAOT payload is missing.'
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length
$normalOutput = @(& $gateBuild -OutputDirectory $normalGate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -AssumeUnspecifiedTimezoneUtc)
$normalOutput | Set-Content -LiteralPath (Join-Path $build 'gate4-normal-build-output.log') -Encoding ascii
$acceptanceOutput = @(& $gateBuild -OutputDirectory $acceptanceGate `
    -ManagedArtifact $payload -PayloadMode ManagedKernel `
    -Scenario ManagedKernelPhase11 -EnableNativeAotStartup `
    -EnablePhase83E1000Rx -EnablePhase85ManagedArp `
    -AssumeUnspecifiedTimezoneUtc)
$acceptanceOutput | Set-Content -LiteralPath (Join-Path $build 'gate4-phase88-build-output.log') -Encoding ascii
$bootOutput = @(& $bootRunner -GateDirectory $acceptanceGate `
    -EvidenceDirectory $evidence -PayloadSha256 $payloadHash `
    -PayloadSize $payloadSize -RunCount 3 -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
    -EnablePhase80E1000 -EnablePhase83 -EnablePhase85Arp)
$bootOutput | Set-Content -LiteralPath (Join-Path $build 'qemu-runner-output.log') -Encoding ascii

$summaries = @()
for ($boot = 1; $boot -le 3; $boot++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $boot)
    $serialPath = Join-Path $run 'serial.log'
    $injectionPath = Join-Path $run 'injections.log'
    $timelinePath = Join-Path $run 'timeline.log'
    $capturePath = Join-Path $run 'arp-tx-capture.bin'
    foreach ($path in @($serialPath, $injectionPath, $timelinePath, $capturePath)) {
        Require88 (Test-Path -LiteralPath $path) "Boot $boot evidence is missing: $path"
    }
    $serial = [IO.File]::ReadAllText($serialPath)
    $injections = [IO.File]::ReadAllText($injectionPath)
    $timeline = [IO.File]::ReadAllText($timelinePath)
    Require88 (([regex]::Matches($serial, 'GXOS_NET10:PHASE85_ARP_DELIVERY=0x')).Count -eq 4) `
        "Boot $boot did not positively deliver all four ARP requests."
    Require88 (([regex]::Matches($serial, 'GXOS_NET10:PHASE85_RX_DESCRIPTOR_RECYCLED=1')).Count -eq 4) `
        "Boot $boot did not recycle all four completed descriptors."
    Require88 (([regex]::Matches($serial, 'GXOS_NET10:PHASE85_ARP_REQUEST_FOR_LOCAL=1')).Count -eq 3) `
        "Boot $boot did not classify three local ARP requests."
    Require88 (([regex]::Matches($serial, 'GXOS_NET10:PHASE85_ARP_REQUEST_NOT_FOR_US=1')).Count -eq 1) `
        "Boot $boot did not classify one not-for-us request."
    Require88 (([regex]::Matches($injections, 'PHASE85_QEMU_ARP_REPLY_CAPTURED=PASS')).Count -eq 3) `
        "Boot $boot did not capture exactly three positive replies."
    Require88 ($injections.Contains('PHASE85_ARP_INJECTION_SUMMARY=PASS requests=4 local_targets=3 not_for_us=1 captured_replies=3 external_network=false')) `
        "Boot $boot did not report bounded four-request completion."
    Require88 ($serial.Contains('GXOS_NET10:PHASE85_HARDWARE_ARP_INGRESS_PASS=1') -and
        $serial.Contains('GXOS_NET10:PHASE83_COM1_ROUTE_ACTIVE_DURING_RX=1') -and
        $serial.Contains('GXOS_NET10:PHASE83_COM1_REMAINS_ISOLATED=1') -and
        $serial.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')) `
        "Boot $boot lacks the guest completion, COM1, or later-progress marker."
    Require88 ($timeline.Contains('marker=GXOS_NET10:PHASE85_ARP_DELIVERY=0x0000000000000004') -and
        $timeline.Contains('marker=GXOS_NET10:PHASE85_HARDWARE_ARP_INGRESS_PASS=1')) `
        "Boot $boot did not record positive guest completion after the fourth request."
    $captureLength = (Get-Item -LiteralPath $capturePath).Length
    Require88 ($captureLength -eq 448) "Boot $boot capture did not preserve the expected four Phase 80 fixture frames and three ARP replies."
    $captureLogPath = Join-Path $run 'arp-tx-capture.log'
    $captureLog = [IO.File]::ReadAllText($captureLogPath)
    $guestArpDatagrams = [regex]::Matches($captureLog, 'PHASE85_QEMU_TX_DATAGRAM sequence=[123] length=60 sha256=(?:E7259DD0E4AB1EA1DD90FF5CE0F3B277FEEF60BB40C282ECD23027FD8933F7A7|906AE4F56182F96058BCD562698737AFE8FEF8994BEB8A30338D58460F4A0083|BE675996B099D11E538D91D5DA5A0F33A979AA9F08D91165E05FFC1CC351C010)')
    Require88 ($guestArpDatagrams.Count -eq 3) "Boot $boot did not capture exactly the three expected guest ARP replies."
    $summaries += [pscustomobject]@{
        Boot = $boot
        RequestsInjected = 4
        RequestsReceived = 4
        RequestsParsed = 4
        Local = 3
        NotForUs = 1
        RepliesCaptured = 3
        DescriptorsRecycled = 4
        SerialSha256 = (Get-FileHash $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
        InjectionSha256 = (Get-FileHash $injectionPath -Algorithm SHA256).Hash.ToUpperInvariant()
        CaptureSha256 = (Get-FileHash $capturePath -Algorithm SHA256).Hash.ToUpperInvariant()
        TimelineSha256 = (Get-FileHash $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}
$summaries | Format-Table -AutoSize | Out-String | Set-Content `
    -LiteralPath (Join-Path $evidence 'phase88-summary.txt') -Encoding ascii
Write-Output 'MANAGED_KERNEL_PHASE88_QEMU_RUNS=3/3'
Write-Output "MANAGED_KERNEL_PHASE88_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE88_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE88_EVIDENCE=$evidence"
$summaries | Format-Table -AutoSize

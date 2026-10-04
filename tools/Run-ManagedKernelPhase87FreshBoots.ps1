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
    $BuildDirectory = Join-Path $root 'artifacts\phase87-build'
}
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root 'artifacts\phase87-qemu-evidence'
}
$build = [IO.Path]::GetFullPath($BuildDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$managedBuild = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$gateBuild = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$bootRunner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$payloadDirectory = Join-Path $build 'managed-kernel'
$payload = Join-Path $payloadDirectory 'publish\gxos-managed-kernel.dll'
$normalGate = Join-Path $build 'gate4-normal'
$acceptanceGate = Join-Path $build 'gate4-phase87'

function Require87([bool]$condition, [string]$message) {
    if (!$condition) { throw $message }
}

New-Item -ItemType Directory -Force -Path $build | Out-Null
$managedOutput = @(& $managedBuild -OutputDirectory $payloadDirectory)
$managedOutput | Set-Content -LiteralPath (Join-Path $build 'managed-kernel-build-output.log') -Encoding ascii
Require87 (Test-Path -LiteralPath $payload) 'ManagedKernel NativeAOT payload is missing.'
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
    -EnablePhase83E1000Rx -EnablePhase87ManagedIcmpv4 `
    -AssumeUnspecifiedTimezoneUtc)
$acceptanceOutput | Set-Content -LiteralPath (Join-Path $build 'gate4-phase87-build-output.log') -Encoding ascii

$bootOutput = @(& $bootRunner -GateDirectory $acceptanceGate `
    -EvidenceDirectory $evidence -PayloadSha256 $payloadHash `
    -PayloadSize $payloadSize -RunCount 3 -TimeoutSeconds $TimeoutSeconds `
    -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' `
    -EnablePhase80E1000 -EnablePhase83 -EnablePhase87Icmpv4)
$bootOutput | Set-Content -LiteralPath (Join-Path $build 'qemu-runner-output.log') -Encoding ascii

$summary = @()
for ($boot = 1; $boot -le 3; $boot++) {
    $run = Join-Path $evidence ('runs\run-{0}' -f $boot)
    $serialPath = Join-Path $run 'serial.log'
    $injectionPath = Join-Path $run 'injections.log'
    $capturePath = Join-Path $run 'icmp-tx-capture.bin'
    $captureLogPath = Join-Path $run 'icmp-tx-capture.log'
    $timelinePath = Join-Path $run 'timeline.log'
    foreach ($path in @($serialPath, $injectionPath, $capturePath,
                        $captureLogPath, $timelinePath)) {
        Require87 (Test-Path -LiteralPath $path) "Boot $boot evidence is missing: $path"
    }
    $serial = [IO.File]::ReadAllText($serialPath)
    $injections = [IO.File]::ReadAllText($injectionPath)
    Require87 (([regex]::Matches($injections,
        'PHASE87_ICMP_ECHO_REQUEST_INJECTED=PASS')).Count -eq 3) `
        "Boot $boot did not inject three ICMP requests."
    Require87 (([regex]::Matches($injections,
        'PHASE87_QEMU_ICMP_ECHO_REPLY_CAPTURED=PASS')).Count -eq 3) `
        "Boot $boot did not capture three QEMU ICMP replies."
    Require87 (([regex]::Matches($serial,
        'GXOS_NET10:PHASE87_ICMP_DELIVERY=')).Count -eq 3) `
        "Boot $boot managed ingress count is not three."
    Require87 ($injections.Contains(
        'PHASE87_ICMP_ECHO_SUMMARY=PASS requests=3 captured_replies=3 duplicate_replies=0 external_network=false')) `
        "Boot $boot did not report the bounded three-request summary."
    Require87 ($serial.Contains('GXOS_NET10:PHASE87_HARDWARE_ICMP_ECHO_PASS=1') -and
        $serial.Contains('GXOS_NET10:PHASE83_COM1_ROUTE_ACTIVE_DURING_RX=1') -and
        $serial.Contains('GXOS_NET10:PHASE83_COM1_REMAINS_ISOLATED=1') -and
        $serial.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS')) `
        "Boot $boot is missing the hardware ICMP, COM1, or later-progress proof."
    $summary += [pscustomobject]@{
        Boot = $boot
        Requests = 3
        CapturedReplies = 3
        GuestMac = '52:54:00:12:34:56'
        RequesterMac = '02:15:00:00:00:02'
        RequesterIpv4 = '10.15.0.2'
        LocalIpv4 = '10.15.0.1'
        SerialSha256 = (Get-FileHash $serialPath -Algorithm SHA256).Hash.ToUpperInvariant()
        InjectionSha256 = (Get-FileHash $injectionPath -Algorithm SHA256).Hash.ToUpperInvariant()
        ReplyCaptureSha256 = (Get-FileHash $capturePath -Algorithm SHA256).Hash.ToUpperInvariant()
        TimelineSha256 = (Get-FileHash $timelinePath -Algorithm SHA256).Hash.ToUpperInvariant()
    }
}
$summary | Format-Table -AutoSize | Out-String | Set-Content `
    -LiteralPath (Join-Path $evidence 'phase87-summary.txt') -Encoding ascii
Write-Output 'MANAGED_KERNEL_PHASE87_QEMU_RUNS=3/3'
Write-Output "MANAGED_KERNEL_PHASE87_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE87_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE87_EVIDENCE=$evidence"
$summary | Format-Table -AutoSize

[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 180,
    [switch]$EnableQemuReceiveTrace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh public HTTPS boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase35-public-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "Output directory already exists: $output"
}

$compile = Join-Path $output 'managed-build'
$gate = Join-Path $output 'gate4'
$evidence = Join-Path $output 'evidence'
$buildManaged = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$buildGate = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$runFreshBoots = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$payload = Join-Path $compile 'publish\gxos-managed-kernel.dll'

New-Item -ItemType Directory -Force -Path $output | Out-Null

& $buildManaged -OutputDirectory $compile
if ($LASTEXITCODE -ne 0) {
    throw "ManagedKernel NativeAOT build failed: $LASTEXITCODE"
}
if (-not (Test-Path -LiteralPath $payload)) {
    throw "ManagedKernel payload was not emitted: $payload"
}
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload `
    -PayloadMode ManagedKernel -Scenario ManagedKernelPhase35 `
    -EnableNativeAotStartup -EnableManagedKernelPhase35 `
    -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) {
    throw "Gate 4 public-network build failed: $LASTEXITCODE"
}

$metadata = @(
    'MANAGED_KERNEL_PHASE35_RUN=PUBLIC_HTTPS',
    'MANAGED_KERNEL_PHASE35_TARGET_HOST=www.cloudflare.com',
    'MANAGED_KERNEL_PHASE35_TARGET_PATH=/llms.txt',
    'MANAGED_KERNEL_PHASE35_BACKEND=QEMU_USER_NETDEV_NAT',
    'MANAGED_KERNEL_PHASE35_DEVICE=e1000e,netdev=net0,addr=2',
    'MANAGED_KERNEL_PHASE35_DNS_SOURCE=DHCP_OPTION_6',
    'MANAGED_KERNEL_PHASE35_HTTP=MANAGED_TLS12_AND_HTTP_CLIENT',
    "MANAGED_KERNEL_PHASE35_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE35_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE35_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE35_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE35_QEMU_RECEIVE_TRACE=$([bool]$EnableQemuReceiveTrace)",
    "MANAGED_KERNEL_PHASE35_GATE=$gate",
    "MANAGED_KERNEL_PHASE35_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase35-run-metadata.log') `
    -Value $metadata -Encoding ascii

if ($EnableQemuReceiveTrace) {
    & $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
        -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
        -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
        -EnablePhase15Rx -Phase15NetworkBackend user `
        -EnableManagedKernelPhase35 -EnablePhase26VirtioRng `
        -Phase15EnableQemuReceiveTrace
} else {
    & $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence `
        -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) `
        -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
        -EnablePhase15Rx -Phase15NetworkBackend user `
        -EnableManagedKernelPhase35 -EnablePhase26VirtioRng
}
if ($LASTEXITCODE -ne 0) {
    throw "Phase 35 fresh boots failed: $LASTEXITCODE"
}

$runReports = @()
$outcomes = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') `
                                      -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    $matches = [regex]::Matches($text, 'PUBLIC_HTTPS_OUTCOME=([A-D])')
    if ($matches.Count -ne 1) {
        throw "Expected one Phase 35 outcome in $($serial.FullName)."
    }
    $outcome = $matches[0].Groups[1].Value
    $outcomes += $outcome
    $runReports += "serial=$($serial.FullName) outcome=$outcome sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($outcomes.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 35 serial logs, found $($outcomes.Count)."
}
if (@($outcomes | Where-Object { $_ -notin @('A', 'B') }).Count -ne 0) {
    throw "Phase 35 public proof did not reach an accepted outcome: $($outcomes -join ',')"
}

$summary = @(
    'MANAGED_KERNEL_PHASE35_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE35_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE35_OUTCOMES=$($outcomes -join ',')",
    "MANAGED_KERNEL_PHASE35_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE35_PAYLOAD_SIZE=$payloadSize",
    'MANAGED_KERNEL_PHASE35_TARGET=www.cloudflare.com/llms.txt',
    'MANAGED_KERNEL_PHASE35_NETWORK=DHCP->ARP_GATEWAY->DNS->TCP',
    'MANAGED_KERNEL_PHASE35_TLS_PROFILE=TLS1.2_C02B_P256_ECDSA_SHA256_EMS_REQUIRED') +
    $runReports
Set-Content -LiteralPath (Join-Path $output 'phase35-summary.log') `
    -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE35_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE35_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE35_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE35_OUTCOMES=$($outcomes -join ',')"

[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 240,
    [switch]$EnableQemuReceiveTrace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh public HTTPS boots are required.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase36-public-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "Output directory already exists: $output"
}

$phase35Runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase35PublicHttps.ps1'
$arguments = @{
    OutputDirectory = $output
    RunCount = $RunCount
    TimeoutSeconds = $TimeoutSeconds
}
if ($EnableQemuReceiveTrace) { $arguments.EnableQemuReceiveTrace = $true }
& $phase35Runner @arguments
if ($LASTEXITCODE -ne 0) { throw "Public Phase 36 runner failed: $LASTEXITCODE" }

$serials = @(Get-ChildItem -LiteralPath (Join-Path $output 'evidence\runs') `
                         -Filter serial.log -Recurse)
if ($serials.Count -ne $RunCount) {
    throw "Expected $RunCount serial logs, found $($serials.Count)."
}

$required = @(
    'PUBLIC_TLS_CERTIFICATE_VALIDATED',
    'PUBLIC_TLS_CERT_HOSTNAME_VALIDATED',
    'PUBLIC_TLS_FINISHED',
    'PUBLIC_HTTP_REQUEST_ENCRYPTED_SENT',
    'PUBLIC_HTTP_STATUS=0x00000000000000C8',
    'PUBLIC_HTTPS_BODY_VERIFIED',
    'PUBLIC_HTTPS_COMPLETE')
$reports = @()
foreach ($serial in $serials) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    if ($text -notmatch 'PUBLIC_HTTPS_OUTCOME=A') {
        throw "Phase 36 public boot was not Outcome A: $($serial.FullName)"
    }
    foreach ($marker in $required) {
        if ($text -notmatch [regex]::Escape($marker)) {
            throw "Missing Phase 36 proof marker '$marker' in $($serial.FullName)"
        }
    }
    $reports += "serial=$($serial.FullName) outcome=A sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}

$summary = @(
    'MANAGED_KERNEL_PHASE36_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE36_RUNS=$RunCount",
    'MANAGED_KERNEL_PHASE36_OUTCOMES=A,A,A',
    'MANAGED_KERNEL_PHASE36_TARGET=www.cloudflare.com/llms.txt',
    'MANAGED_KERNEL_PHASE36_NETWORK=DHCP->ARP_GATEWAY->DNS->TCP',
    'MANAGED_KERNEL_PHASE36_TLS_PROFILE=TLS1.2_C02B_P256_ECDSA_SHA256_EMS_REQUIRED') + $reports
Set-Content -LiteralPath (Join-Path $output 'phase36-summary.log') -Value $summary -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE36_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE36_OUTCOMES=A,A,A"

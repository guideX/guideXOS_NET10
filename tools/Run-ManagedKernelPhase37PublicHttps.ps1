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
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase37-public-$stamp"
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
if ($LASTEXITCODE -ne 0) { throw "Public Phase 37 runner failed: $LASTEXITCODE" }

$serials = @(Get-ChildItem -LiteralPath (Join-Path $output 'evidence\runs') -Filter serial.log -Recurse)
if ($serials.Count -ne $RunCount) {
    throw "Expected $RunCount serial logs, found $($serials.Count)."
}

$required = @(
    'PUBLIC_TLS_CERTIFICATE_VALIDATED',
    'PUBLIC_TLS_CERT_HOSTNAME_VALIDATED',
    'PUBLIC_TLS_FINISHED',
    'PUBLIC_HTTP_REQUEST_ENCRYPTED_SENT',
    'PUBLIC_HTTP_STATUS=0x00000000000000C8',
    'PUBLIC_HTTP_TRANSFER_MODE=0x',
    'PUBLIC_HTTP_BODY_SEGMENTS=0x',
    'PUBLIC_HTTP_BODY_PEAK_BUFFER=0x',
    'PUBLIC_HTTP_BODY_LENGTH=0x',
    'PUBLIC_HTTP_BODY_SHA256_WORD=0x',
    'PUBLIC_HTTPS_BODY_VERIFIED',
    'PUBLIC_HTTPS_COMPLETE')
$reports = @()
$metrics = @()
foreach ($serial in $serials) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    if ($text -notmatch 'PUBLIC_HTTPS_OUTCOME=A') {
        throw "Phase 37 public boot was not Outcome A: $($serial.FullName)"
    }
    foreach ($marker in $required) {
        if ($text -notmatch [regex]::Escape($marker)) {
            throw "Missing Phase 37 proof marker '$marker' in $($serial.FullName)"
        }
    }
    if ($text -match 'PUBLIC_HTTPS_NEXT_BLOCKER=HTTP_BODY_LIMIT_EXCEEDED' -or
        $text -match 'PUBLIC_HTTP_PARSE_FAILURE=0x000000000000000E' -or
        $text -notmatch 'PUBLIC_HTTP_BODY_DELIVERED=0x') {
        throw "Public boot reported an HTTP body-limit failure: $($serial.FullName)"
    }
    if ($text -notmatch 'PUBLIC_HTTP_TRANSFER_MODE=0x0000000000000002' -and
        $text -notmatch 'PUBLIC_HTTP_TRANSFER_MODE=0x0000000000000003') {
        throw "Public boot did not use bounded content-length or chunked framing: $($serial.FullName)"
    }
    $lengthMatch = [regex]::Match($text, 'PUBLIC_HTTP_BODY_LENGTH=0x([0-9A-Fa-f]+)')
    $segmentMatch = [regex]::Match($text, 'PUBLIC_HTTP_BODY_SEGMENTS=0x([0-9A-Fa-f]+)')
    $peakMatch = [regex]::Match($text, 'PUBLIC_HTTP_BODY_PEAK_BUFFER=0x([0-9A-Fa-f]+)')
    if (-not $lengthMatch.Success -or -not $segmentMatch.Success -or
        -not $peakMatch.Success) {
        throw "Public body metrics were incomplete: $($serial.FullName)"
    }
    $length = [Convert]::ToInt64($lengthMatch.Groups[1].Value, 16)
    $segments = [Convert]::ToInt64($segmentMatch.Groups[1].Value, 16)
    $peak = [Convert]::ToInt64($peakMatch.Groups[1].Value, 16)
    if ($length -le 4096 -or $segments -le 1 -or $peak -gt 1024) {
        throw "Public body metrics did not prove bounded streaming: $($serial.FullName) length=$length segments=$segments peak=$peak"
    }
    if (([regex]::Matches($text, 'PUBLIC_HTTP_BODY_SHA256_WORD=0x')).Count -ne 8) {
        throw "Expected eight decoded-body SHA-256 words: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) outcome=A sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
    $metrics += "serial=$($serial.Name) body_bytes=$length body_segments=$segments peak_body_buffer=$peak"
}

$summary = @(
    'MANAGED_KERNEL_PHASE37_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE37_RUNS=$RunCount",
    'MANAGED_KERNEL_PHASE37_OUTCOMES=A,A,A',
    'MANAGED_KERNEL_PHASE37_TARGET=www.cloudflare.com/llms.txt',
    'MANAGED_KERNEL_PHASE37_NETWORK=DHCP->ARP_GATEWAY->DNS->TCP',
    'MANAGED_KERNEL_PHASE37_TLS_PROFILE=TLS1.2_C02B_P256_ECDSA_SHA256_EMS_REQUIRED',
    'MANAGED_KERNEL_PHASE37_HTTP_REQUIREMENT=200+CONTENT_LENGTH_OR_CHUNKED+COMPLETE_BOUNDED_BODY',
    $metrics,
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase37-summary.log') -Value $summary -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE37_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE37_OUTCOMES=A,A,A"

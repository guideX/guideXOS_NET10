[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 300,
    [switch]$EnableQemuReceiveTrace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 38 public HTTPS boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase38-public-$stamp"
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
if ($LASTEXITCODE -ne 0) { throw "Phase 38 public runner failed: $LASTEXITCODE" }

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
    'PUBLIC_HTTP_STATUS=0x',
    'PUBLIC_HTTP_BODY_PAUSED',
    'PUBLIC_HTTP_BODY_PAUSE_PROGRESS_STABLE',
    'PUBLIC_HTTP_BODY_PAUSED_POLLS=0x',
    'PUBLIC_HTTP_BODY_RESUMED',
    'PUBLIC_HTTP_PROGRESS_STATE=0x0000000000000003',
    'PUBLIC_HTTP_TOTAL_KNOWN=0x0000000000000001',
    'PUBLIC_HTTP_BODY_RECEIVED=0x',
    'PUBLIC_HTTP_BODY_DELIVERED=0x',
    'PUBLIC_HTTP_BODY_BUFFERED=0x0000000000000000',
    'PUBLIC_HTTP_BODY_PAUSE_COUNT=0x0000000000000001',
    'PUBLIC_HTTP_BODY_RESUME_COUNT=0x0000000000000001',
    'PUBLIC_PHASE38_CANCEL_REQUEST_STARTED',
    'PUBLIC_PHASE38_CANCEL_RECEIVED=0x',
    'PUBLIC_PHASE38_CANCEL_DELIVERED=0x',
    'PUBLIC_PHASE38_CANCEL_BUFFERED=0x',
    'PUBLIC_PHASE38_CANCELLED_STATE',
    'PUBLIC_PHASE38_CANCEL_NO_LATE_DELIVERY',
    'PUBLIC_PHASE38_CANCEL_TEARDOWN_COMPLETE',
    'PUBLIC_PHASE38_CANCEL_RESET_REUSE_READY',
    'PUBLIC_PHASE38_COMPLETE',
    'PUBLIC_HTTPS_BODY_VERIFIED',
    'PUBLIC_HTTPS_COMPLETE')

$reports = @()
$metrics = @()
foreach ($serial in $serials) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    if ($text -notmatch 'PUBLIC_HTTPS_OUTCOME=A') {
        throw "Phase 38 public boot was not Outcome A: $($serial.FullName)"
    }
    foreach ($marker in $required) {
        if ($text -notmatch [regex]::Escape($marker)) {
            throw "Missing Phase 38 proof marker '$marker' in $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'PUBLIC_HTTP_BODY_PAUSED\r?\n')).Count -ne 1 -or
        ([regex]::Matches($text, 'PUBLIC_HTTP_BODY_RESUMED\r?\n')).Count -ne 1 -or
        ([regex]::Matches($text, 'PUBLIC_PHASE38_CANCELLED_STATE\r?\n')).Count -ne 1 -or
        ([regex]::Matches($text, 'PUBLIC_PHASE38_CANCEL_NO_LATE_DELIVERY\r?\n')).Count -ne 1 -or
        ([regex]::Matches($text, 'PUBLIC_PHASE38_COMPLETE\r?\n')).Count -ne 1) {
        throw "Phase 38 pause/cancel marker counts were not exactly one: $($serial.FullName)"
    }
    $bodyReceived = [regex]::Match($text, 'PUBLIC_HTTP_BODY_RECEIVED=0x([0-9A-Fa-f]+)')
    $bodyDelivered = [regex]::Match($text, 'PUBLIC_HTTP_BODY_DELIVERED=0x([0-9A-Fa-f]+)')
    $bodyLength = [regex]::Match($text, 'PUBLIC_HTTP_BODY_LENGTH=0x([0-9A-Fa-f]+)')
    $pausedPolls = [regex]::Match($text, 'PUBLIC_HTTP_BODY_PAUSED_POLLS=0x([0-9A-Fa-f]+)')
    $cancelReceived = [regex]::Match($text, 'PUBLIC_PHASE38_CANCEL_RECEIVED=0x([0-9A-Fa-f]+)')
    $cancelDelivered = [regex]::Match($text, 'PUBLIC_PHASE38_CANCEL_DELIVERED=0x([0-9A-Fa-f]+)')
    if (-not $bodyReceived.Success -or -not $bodyDelivered.Success -or
        -not $bodyLength.Success -or -not $pausedPolls.Success -or
        -not $cancelReceived.Success -or -not $cancelDelivered.Success) {
        throw "Phase 38 body metrics were incomplete: $($serial.FullName)"
    }
    $received = [Convert]::ToInt64($bodyReceived.Groups[1].Value, 16)
    $delivered = [Convert]::ToInt64($bodyDelivered.Groups[1].Value, 16)
    $length = [Convert]::ToInt64($bodyLength.Groups[1].Value, 16)
    $pausePollCount = [Convert]::ToInt64($pausedPolls.Groups[1].Value, 16)
    $cancelReceivedValue = [Convert]::ToInt64($cancelReceived.Groups[1].Value, 16)
    $cancelDeliveredValue = [Convert]::ToInt64($cancelDelivered.Groups[1].Value, 16)
    if ($length -le 4096 -or $received -ne $length -or $delivered -ne $length -or
        $pausePollCount -lt 4 -or $cancelReceivedValue -le 0 -or
        $cancelDeliveredValue -le 0 -or $cancelDeliveredValue -gt $cancelReceivedValue) {
        throw "Phase 38 metrics did not prove pause/resume/cancel semantics: $($serial.FullName)"
    }
    if (([regex]::Matches($text, 'PUBLIC_HTTP_BODY_SHA256_WORD=0x')).Count -ne 8) {
        throw "Expected eight decoded-body SHA-256 words: $($serial.FullName)"
    }
    $reports += "serial=$($serial.FullName) outcome=A sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
    $metrics += "serial=$($serial.Name) body_bytes=$length body_received=$received body_delivered=$delivered pause_polls=$pausePollCount cancel_received=$cancelReceivedValue cancel_delivered=$cancelDeliveredValue"
}

$summary = @(
    'MANAGED_KERNEL_PHASE38_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE38_RUNS=$RunCount",
    'MANAGED_KERNEL_PHASE38_OUTCOMES=A,A,A',
    'MANAGED_KERNEL_PHASE38_TARGET=www.cloudflare.com/llms.txt',
    'MANAGED_KERNEL_PHASE38_NETWORK=DHCP->ARP_GATEWAY->DNS->TCP',
    'MANAGED_KERNEL_PHASE38_TLS_PROFILE=TLS1.2_C02B_P256_ECDSA_SHA256_EMS_REQUIRED',
    'MANAGED_KERNEL_PHASE38_FLOW=bounded-parser-window->pause->stable-polls->resume',
    'MANAGED_KERNEL_PHASE38_CANCELLATION=second-request->one-delivery->cancel->no-late-delivery',
    $metrics,
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase38-summary.log') -Value $summary -Encoding ascii
Write-Output "MANAGED_KERNEL_PHASE38_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE38_OUTCOMES=A,A,A"

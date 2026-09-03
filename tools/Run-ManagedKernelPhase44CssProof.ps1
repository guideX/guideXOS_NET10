[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900,
    [switch]$CapacityControl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $CapacityControl -and $RunCount -lt 3) {
    throw 'Three fresh Phase 44 positive boots are required.'
}
if ($RunCount -lt 1) { throw 'At least one Phase 44 capacity-control boot is required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $suffix = if ($CapacityControl) { 'css-capacity' } else { 'css' }
    $OutputDirectory = Join-Path $root "artifacts\phase44-$suffix-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
$compile = Join-Path $output 'managed-build'
$gate = Join-Path $output 'gate4'
$evidence = Join-Path $output 'evidence'
$buildManaged = Join-Path $PSScriptRoot 'Build-ManagedKernel.ps1'
$buildGate = Join-Path $PSScriptRoot 'Build-Gate4Harness.ps1'
$runFreshBoots = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$payload = Join-Path $compile 'publish\gxos-managed-kernel.dll'
New-Item -ItemType Directory -Force -Path $output | Out-Null

& $buildManaged -OutputDirectory $compile
if ($LASTEXITCODE -ne 0) { throw "ManagedKernel NativeAOT build failed: $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath $payload)) { throw "ManagedKernel payload was not emitted: $payload" }
$payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$payloadSize = (Get-Item -LiteralPath $payload).Length

$html = "<!doctype html><html><head><title>GuideX Phase 44</title><style>body{color:green;font-size:20px}div{display:block}#main.container{background-color:#1234;padding:1px 2px}#main p{font-weight:bold}section > p.note, section > p.alert{color:red}.important{color:blue !important}</style></head><body><div id=main class=container style='padding:3px 4px'><section><p class='note'>Note</p><p class=plain style='color:white;width:50%'>Plain</p><p class=important style='color:white'>Important</p></section><table><tr><td>cell</td></tr></table><form><input disabled></form></div></body></html>"
$resourceLength = [Text.Encoding]::UTF8.GetByteCount($html)
$resourceSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($html)))
$metadata = @(
    'MANAGED_KERNEL_PHASE44_RUN=BOUNDED_HTTPS_GZIP_HTML_CSS_CASCADE',
    'MANAGED_KERNEL_PHASE44_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE44_TARGET_PATH=/phase44/gzip',
    'MANAGED_KERNEL_PHASE44_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_UTF8_FIXTURE',
    'MANAGED_KERNEL_PHASE44_DEVICE=e1000e,addr=2',
    "MANAGED_KERNEL_PHASE44_DECODED_RESOURCE_LENGTH=$resourceLength",
    'MANAGED_KERNEL_PHASE44_CONTENT_TYPE=text/html; charset=utf-8',
    'MANAGED_KERNEL_PHASE44_CONTENT_ENCODING=gzip',
    "MANAGED_KERNEL_PHASE44_RESOURCE_SHA256=$resourceSha256",
    "MANAGED_KERNEL_PHASE44_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE44_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE44_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE44_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE44_CAPACITY_CONTROL=$([bool]$CapacityControl)",
    "MANAGED_KERNEL_PHASE44_GATE=$gate",
    "MANAGED_KERNEL_PHASE44_EVIDENCE=$evidence")
Set-Content -LiteralPath (Join-Path $output 'phase44-run-metadata.log') -Value $metadata -Encoding ascii

$gateParameters = @{
    OutputDirectory = $gate
    ManagedArtifact = $payload
    PayloadMode = 'ManagedKernel'
    Scenario = 'ManagedKernelPhase44'
    EnableNativeAotStartup = $true
    AssumeUnspecifiedTimezoneUtc = $true
}
if ($CapacityControl) { $gateParameters.EnableManagedKernelPhase44Capacity = $true }
else { $gateParameters.EnableManagedKernelPhase44 = $true }
& $buildGate @gateParameters
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 44 build failed: $LASTEXITCODE" }

$bootParameters = @{
    GateDirectory = $gate
    EvidenceDirectory = $evidence
    PayloadSha256 = $payloadHash
    PayloadSize = [long]$payloadSize
    RunCount = $RunCount
    TimeoutSeconds = $TimeoutSeconds
    EnablePhase15Rx = $true
    EnablePhase44Protocol = $true
    EnablePhase26VirtioRng = $true
}
if ($CapacityControl) { $bootParameters.EnablePhase44CapacityControl = $true }
& $runFreshBoots @bootParameters
if ($LASTEXITCODE -ne 0) { throw "Phase 44 fresh boots failed: $LASTEXITCODE" }

$required = if ($CapacityControl) {
    @('GXOS_NET10:MANAGED_KERNEL_PHASE44_CAPACITY_MODE_SELECTED',
      'GXOS_NET10:MANAGED_KERNEL_PHASE44_STARTING',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_BEGIN',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_TREE_VALIDATED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_ENGINE_CREATED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_FAILURE=0x0000000000000002',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_CAPACITY_CONTROL_VALIDATED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_CAPACITY_NEGATIVE_PASS',
      'GXOS_NET10:MANAGED_KERNEL_PHASE44_START_FAILED',
      'GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START',
      'GXOS_NET10:FAIL:managed-kernel-phase14-driver-proof')
} else {
    @('GXOS_NET10:MANAGED_KERNEL_PHASE44_MODE_SELECTED',
      'GXOS_NET10:MANAGED_KERNEL_PHASE44_STARTING',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_BEGIN',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CONFIGURED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_READY',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_STARTED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_REQUEST_STARTED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_BODY_RECEIVED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_TREE_VALIDATED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_ENGINE_CREATED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_VERIFIED',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_COMPLETE',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_PASS',
      'GXOS_NET10:MANAGED_HTTPS_PHASE44_PASS',
      'GXOS_NET10:MANAGED_KERNEL_PHASE44_PASS')
}

$reports = @()
$serialLogs = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)
foreach ($serial in $serialLogs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) {
        if (-not $text.Contains($marker)) { throw "Phase 44 boot missing '$marker': $($serial.FullName)" }
    }
    if ($CapacityControl) {
        if ($text.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_PASS') -or
            $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE44_PASS') -or
            $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
            $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
            $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
            throw "Phase 44 capacity boot emitted an invalid success or machine-fault marker: $($serial.FullName)"
        }
    } else {
        $status = [regex]::Match($text, 'MANAGED_HTTPS_PHASE44_STATUS=0x([0-9A-Fa-f]+)')
        $decompressed = [regex]::Match($text, 'MANAGED_HTTPS_PHASE44_DECOMPRESSED_BYTES=0x([0-9A-Fa-f]+)')
        $encoded = [regex]::Match($text, 'MANAGED_HTTPS_PHASE44_ENCODED_BYTES=0x([0-9A-Fa-f]+)')
        if (-not $status.Success -or [Convert]::ToInt32($status.Groups[1].Value, 16) -ne 200 -or
            -not $decompressed.Success -or [Convert]::ToInt32($decompressed.Groups[1].Value, 16) -ne $resourceLength -or
            -not $encoded.Success -or [Convert]::ToInt32($encoded.Groups[1].Value, 16) -le 0 -or
            [Convert]::ToInt32($encoded.Groups[1].Value, 16) -ge $resourceLength -or
            ([regex]::Matches($text, 'MANAGED_HTTPS_PHASE44_STYLE_HANDLE=')).Count -ne 4 -or
            ([regex]::Matches($text, 'MANAGED_HTTPS_PHASE44_STYLE_COLOR=')).Count -ne 4 -or
            ([regex]::Matches($text, 'MANAGED_HTTPS_PHASE44_STYLE_BACKGROUND=')).Count -ne 4 -or
            $text.Contains('GXOS_NET10:FAIL:') -or $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
            $text.Contains('GXOS_NET10:PAGE_FAULT_') -or $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
            throw "Phase 44 boot did not prove the expected gzip CSS result: $($serial.FullName)"
        }
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($reports.Count -ne $RunCount) { throw "Expected $RunCount Phase 44 serial logs, found $($reports.Count)." }

$summaryPrefix = if ($CapacityControl) { 'MANAGED_KERNEL_PHASE44_CAPACITY_CONTROL' } else { 'MANAGED_KERNEL_PHASE44' }
$summary = @(
    "${summaryPrefix}_BOOT_SUMMARY=PASS",
    "${summaryPrefix}_RUNS=$RunCount",
    "${summaryPrefix}_DECODED_RESOURCE_LENGTH=$resourceLength",
    "${summaryPrefix}_RESOURCE_SHA256=$resourceSha256",
    "${summaryPrefix}_PAYLOAD_SHA256=$payloadHash",
    "${summaryPrefix}_PAYLOAD_SIZE=$payloadSize",
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase44-summary.log') -Value $summary -Encoding ascii

Write-Output "MANAGED_KERNEL_PHASE44_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE44_RESOURCE_SHA256=$resourceSha256"
Write-Output "MANAGED_KERNEL_PHASE44_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE44_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE44_RUNS=$RunCount"

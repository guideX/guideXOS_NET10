[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 51 wrong-MIME boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase51-wrong-mime-$stamp"
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

Set-Content -LiteralPath (Join-Path $output 'phase51-wrong-mime-run-metadata.log') -Encoding ascii -Value @(
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_RUN=HTTPS_LINKED_STYLESHEET_CONTENT_TYPE_REJECTION',
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_TARGET_HOST=www.example.com',
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_TARGET_PAGE=/phase51/index.html',
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_TARGET_STYLESHEET=/phase51/a.css',
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_BACKEND=QEMU_DGRAM_DETERMINISTIC_GZIP_HTML_AND_CSS_FIXTURE',
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_EXPECTED_STATUS=200',
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_EXPECTED_CONTENT_TYPE=text/html; charset=utf-8',
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_PAYLOAD_SIZE=$payloadSize",
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_RUN_COUNT=$RunCount",
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_TIMEOUT_SECONDS=$TimeoutSeconds",
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_GATE=$gate",
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_EVIDENCE=$evidence")

& $buildGate -OutputDirectory $gate -ManagedArtifact $payload -PayloadMode ManagedKernel -Scenario ManagedKernelPhase51 -EnableNativeAotStartup -EnableManagedKernelPhase51 -AssumeUnspecifiedTimezoneUtc
if ($LASTEXITCODE -ne 0) { throw "Gate 4 Phase 51 wrong-MIME build failed: $LASTEXITCODE" }

& $runFreshBoots -GateDirectory $gate -EvidenceDirectory $evidence -PayloadSha256 $payloadHash -PayloadSize ([long]$payloadSize) -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds -EnablePhase15Rx -EnablePhase51WrongMimeControl -CaptureQemuScreen -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) { throw "Phase 51 wrong-MIME fresh boots failed: $LASTEXITCODE" }

$serialLogs = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse | Sort-Object FullName)
if ($serialLogs.Count -ne $RunCount) {
    throw "Expected $RunCount wrong-MIME serial logs, found $($serialLogs.Count)."
}
$required = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE51_MODE_SELECTED',
    'GXOS_NET10:MANAGED_KERNEL_PHASE51_STARTING',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_REQUEST_STARTED=',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_WRONG_MIME_HTTP_STATUS=0x00000000000000C8',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_WRONG_MIME_CONTENT_TYPE=text/html; charset=utf-8',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_WRONG_MIME_REJECTION=ExternalStylesheetContentTypeRejected',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_WRONG_MIME_CSS_SCALARS=0x0000000000000000',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_WRONG_MIME_CSS_RULES=0x0000000000000000',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_WRONG_MIME_CSS_DECLARATIONS=0x0000000000000000',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_NO_VISIBLE_PAGE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE51_WRONG_MIME_CONTROL_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE14_ACCOUNTING_RESTORED',
    'MANAGED_KERNEL_PHASE14_PASS')
$reports = @()
foreach ($serial in $serialLogs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) {
        if (-not $text.Contains($marker)) { throw "Wrong-MIME boot missing '$marker': $($serial.FullName)" }
    }
    foreach ($marker in @(
        'GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_PASS',
        'GXOS_NET10:MANAGED_HTTPS_PHASE51_GOP_PRESENT_PASS',
        'GXOS_NET10:MANAGED_HTTPS_PHASE51_VISIBLE_PAGE_PASS',
        'GXOS_NET10:MANAGED_KERNEL_PHASE51_PASS',
        'GXOS_NET10:CPU_EXCEPTION_VECTOR=',
        'GXOS_NET10:PAGE_FAULT_',
        'GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        if ($text.Contains($marker)) { throw "Wrong-MIME boot emitted forbidden marker '$marker': $($serial.FullName)" }
    }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}

Set-Content -LiteralPath (Join-Path $output 'phase51-wrong-mime-summary.log') -Encoding ascii -Value @(
    'MANAGED_KERNEL_PHASE51_WRONG_MIME_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_PAYLOAD_SHA256=$payloadHash",
    "MANAGED_KERNEL_PHASE51_WRONG_MIME_PAYLOAD_SIZE=$payloadSize",
    $reports)
Write-Output "MANAGED_KERNEL_PHASE51_WRONG_MIME_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE51_WRONG_MIME_EVIDENCE=$evidence"
Write-Output "MANAGED_KERNEL_PHASE51_WRONG_MIME_PAYLOAD_SHA256=$payloadHash"
Write-Output "MANAGED_KERNEL_PHASE51_WRONG_MIME_PAYLOAD_SIZE=$payloadSize"
Write-Output "MANAGED_KERNEL_PHASE51_WRONG_MIME_RUNS=$RunCount"

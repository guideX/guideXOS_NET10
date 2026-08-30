[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 600
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 33 boots are required.' }
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase33Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 33 fresh boots failed: $LASTEXITCODE"
}

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE33_REQUESTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_NETWORK_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_DNS_SUCCESS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_TCP_CONNECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_TLS_HANDSHAKE_AUTHENTICATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_HTTP_REQUEST_ENCRYPTED_SENT',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_HTTP_STATUS_PARSED=200',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_CONTENT_LENGTH_SELECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_CHUNKED_SELECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_CONTENT_LENGTH_SELECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_BODY_VERIFIED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_BODY_VERIFIED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_READS_MULTIPLE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_RESPONSE_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_TEARDOWN_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE33_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE33_PASS')

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            throw "Phase 33 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'GXOS_NET10:MANAGED_HTTPS_PHASE33_RESPONSE_COMPLETE')).Count -ne 3 -or
        ([regex]::Matches($text, 'GXOS_NET10:MANAGED_HTTPS_PHASE33_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE33_PASS')).Count -ne 1) {
        throw "Phase 33 boot had incorrect completion/pass marker counts: $($serial.FullName)"
    }
    if ($text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 33 boot reported a fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 33 serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase33-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE33_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE33_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE33_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE33_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE33_ENDPOINTS=www.example.com/phase33-length,www.example.com/phase33-chunked,www.example.com/phase33-stream',
    'MANAGED_KERNEL_PHASE33_CONTENT_LENGTH_RESPONSE=phase33-content-length-pass',
    'MANAGED_KERNEL_PHASE33_CHUNKED_RESPONSE=phase33-http-pass',
    'MANAGED_KERNEL_PHASE33_STREAM_LENGTH=4097',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase33-summary.log')

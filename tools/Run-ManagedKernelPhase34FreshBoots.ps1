[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 34 boots are required.' }
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase34Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 34 fresh boots failed: $LASTEXITCODE"
}

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE34_REQUESTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_NETWORK_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_REQUEST_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_DNS_SUCCESS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_TCP_CONNECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_HANDSHAKE_AUTHENTICATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REQUEST_ENCRYPTED_SENT',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REDIRECT_STATUS=',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_LOCATION_PARSED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_REDIRECT_FOLLOWED=',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_STATUS_PARSED=200',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_URL_VERIFIED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_VERIFIED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_TEARDOWN_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE34_PASS')

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            throw "Phase 34 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    foreach ($pair in @(
        @('GXOS_NET10:MANAGED_HTTPS_PHASE34_DNS_SUCCESS', 4),
        @('GXOS_NET10:MANAGED_HTTPS_PHASE34_TCP_CONNECTED', 4),
        @('GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_HANDSHAKE_AUTHENTICATED', 4),
        @('GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REQUEST_ENCRYPTED_SENT', 4),
        @('GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REDIRECT_STATUS=', 3),
        @('GXOS_NET10:MANAGED_HTTPS_PHASE34_LOCATION_PARSED', 3),
        @('GXOS_NET10:MANAGED_HTTPS_PHASE34_REDIRECT_FOLLOWED=', 3))) {
        if (([regex]::Matches($text, [regex]::Escape([string]$pair[0]))).Count -ne [int]$pair[1]) {
            throw "Phase 34 boot had an unexpected '$($pair[0])' count: $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE34_PASS')).Count -ne 1 -or
        $text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 34 boot did not finish cleanly: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 34 serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase34-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE34_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE34_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE34_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE34_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE34_CHAIN=https://www.example.com/phase34/start -> /phase34/step2 -> next -> https://other.example.com:8443/phase34/final',
    'MANAGED_KERNEL_PHASE34_FINAL_BODY=phase34-redirect-pass',
    'MANAGED_KERNEL_PHASE34_HOPS=4',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase34-summary.log')

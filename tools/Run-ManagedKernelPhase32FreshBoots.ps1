[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)] [string]$PayloadSha256,
    [Parameter(Mandatory = $true)] [long]$PayloadSize,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 32 boots are required.' }
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase32Protocol -Phase15EnableFilterDump `
    -EnablePhase26VirtioRng
if ($LASTEXITCODE -ne 0) {
    throw "Phase 32 fresh boots failed: $LASTEXITCODE"
}

$requiredMarkers = @(
    'GXOS_NET10:MANAGED_KERNEL_PHASE32_REQUESTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_NETWORK_READY',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_REQUEST_STARTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_DNS_SUCCESS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_RESOLVED_IPV4=0x000000000A0F0002',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_TCP_CONNECTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_TLS_HANDSHAKE_AUTHENTICATED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_REQUEST_ENCRYPTED_SENT',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_STATUS_PARSED=200',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_GC_SURVIVAL_PASSED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_RECEIVED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_VERIFIED',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_TEARDOWN_COMPLETE',
    'GXOS_NET10:MANAGED_HTTPS_PHASE32_PASS',
    'GXOS_NET10:MANAGED_KERNEL_PHASE32_PASS')

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $requiredMarkers) {
        if (-not $text.Contains($marker)) {
            throw "Phase 32 boot missing marker '$marker': $($serial.FullName)"
        }
    }
    if (([regex]::Matches($text, 'GXOS_NET10:MANAGED_HTTPS_PHASE32_PASS')).Count -ne 1 -or
        ([regex]::Matches($text, 'GXOS_NET10:MANAGED_KERNEL_PHASE32_PASS')).Count -ne 1) {
        throw "Phase 32 boot did not report exactly one pass marker: $($serial.FullName)"
    }
    if ($text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 32 boot reported a fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 32 serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase32-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE32_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE32_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE32_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE32_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE32_ENDPOINT=www.example.com/phase32',
    'MANAGED_KERNEL_PHASE32_RESPONSE=HTTP/1.1 200 phase32-http-pass',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase32-summary.log')

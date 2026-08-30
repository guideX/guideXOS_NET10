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
if ($RunCount -lt 3) { throw 'Three fresh Phase 34 negative boots are required.' }
$runner = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase11FreshBoots.ps1'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (Test-Path -LiteralPath $evidence) {
    throw "Evidence directory already exists: $evidence"
}

& $runner -GateDirectory $gate -EvidenceDirectory $evidence `
    -PayloadSha256 $PayloadSha256 -PayloadSize $PayloadSize `
    -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds `
    -EnablePhase15Rx -EnablePhase34Protocol -EnablePhase34NegativeControl `
    -Phase15EnableFilterDump
if ($LASTEXITCODE -ne 0) {
    throw "Phase 34 negative boots failed: $LASTEXITCODE"
}

$runReports = @()
foreach ($serial in @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in @(
        'GXOS_NET10:MANAGED_KERNEL_PHASE34_REQUESTED',
        'GXOS_NET10:MANAGED_HTTPS_PHASE34_NETWORK_READY',
        'GXOS_NET10:MANAGED_HTTPS_PHASE34_DNS_SUCCESS',
        'GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_HANDSHAKE_AUTHENTICATED',
        'GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REDIRECT_STATUS=',
        'GXOS_NET10:MANAGED_HTTPS_PHASE34_LOCATION_PARSED',
        'GXOS_NET10:MANAGED_HTTPS_PHASE34_REDIRECT_FOLLOWED=',
        'GXOS_NET10:MANAGED_KERNEL_PHASE34_START_FAILED')) {
        if (-not $text.Contains($marker)) {
            throw "Phase 34 negative boot missing marker '$marker': $($serial.FullName)"
        }
    }
    if ($text.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS') -or
        $text.Contains('GXOS_NET10:MANAGED_KERNEL_PHASE34_PASS') -or
        $text.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_VERIFIED') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 34 negative boot emitted success or a machine fault: $($serial.FullName)"
    }
    $runReports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
if ($runReports.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 34 negative serial logs, found $($runReports.Count)."
}

Set-Content -LiteralPath (Join-Path $evidence 'phase34-negative-summary.log') -Value @(
    'MANAGED_KERNEL_PHASE34_NEGATIVE_CONTROL=PASS',
    "MANAGED_KERNEL_PHASE34_NEGATIVE_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE34_PAYLOAD_SHA256=$($PayloadSha256.ToUpperInvariant())",
    "MANAGED_KERNEL_PHASE34_PAYLOAD_SIZE=$PayloadSize",
    'MANAGED_KERNEL_PHASE34_NEGATIVE_FAULT=SECOND_ORIGIN_CERTIFICATE_HOSTNAME_MISMATCH',
    'MANAGED_KERNEL_PHASE34_NEGATIVE_EXPECTED=FIRST_REDIRECT_AUTHENTICATED_SECOND_TLS_REJECTED_NO_FINAL_SUCCESS',
    $runReports) -Encoding ascii
Get-Content -LiteralPath (Join-Path $evidence 'phase34-negative-summary.log')

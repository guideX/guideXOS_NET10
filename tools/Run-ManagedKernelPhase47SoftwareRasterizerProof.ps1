[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($RunCount -lt 3) { throw 'Three fresh Phase 47 positive boots are required.' }
if ($TimeoutSeconds -le 0) { throw 'TimeoutSeconds must be positive.' }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmssfff')
    $OutputDirectory = Join-Path $root "artifacts\phase47-raster-$stamp"
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Output directory already exists: $output" }
$phase46Output = Join-Path $output 'phase46-pipeline'
$phase46 = Join-Path $PSScriptRoot 'Run-ManagedKernelPhase46DisplayListProof.ps1'
New-Item -ItemType Directory -Force -Path $output | Out-Null

# Phase 47 intentionally reuses the deterministic Phase 46 HTTPS fixture.  The
# native proof enters the rasterizer only after document, CSS, layout, and
# display-list validation has completed, so this wrapper exercises the entire
# production path rather than a host-only synthetic command stream.
& $phase46 -OutputDirectory $phase46Output -RunCount $RunCount -TimeoutSeconds $TimeoutSeconds
if ($LASTEXITCODE -ne 0) { throw "Phase 47 native pipeline failed: $LASTEXITCODE" }

$evidence = Join-Path $phase46Output 'evidence'
$runs = @(Get-ChildItem -LiteralPath (Join-Path $evidence 'runs') -Filter serial.log -Recurse)
if ($runs.Count -ne $RunCount) {
    throw "Expected $RunCount Phase 47 serial logs, found $($runs.Count)."
}

$required = @(
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_RASTER_VALIDATOR_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_SCALED_GLYPH_PROOF_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_FRAMEBUFFER_TOO_SMALL_NEGATIVE_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_RASTER_PASS',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_FRAMEBUFFER_WIDTH=0x00000000000000A0',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_FRAMEBUFFER_HEIGHT=0x00000000000000B4',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_FRAMEBUFFER_STRIDE=0x00000000000000A0',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_FRAMEBUFFER_FORMAT_ARGB8888=0x0000000000000000',
    'GXOS_NET10:MANAGED_HTTPS_PHASE47_FRAMEBUFFER_TOO_SMALL_NEGATIVE_PASS')
$referenceLines = $null
$reports = @()
foreach ($serial in $runs) {
    $text = Get-Content -LiteralPath $serial.FullName -Raw
    foreach ($marker in $required) {
        if (-not $text.Contains($marker)) {
            throw "Phase 47 boot missing '$marker': $($serial.FullName)"
        }
    }
    if ($text.Contains('GXOS_NET10:FAIL:') -or
        $text.Contains('GXOS_NET10:CPU_EXCEPTION_VECTOR=') -or
        $text.Contains('GXOS_NET10:PAGE_FAULT_') -or
        $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) {
        throw "Phase 47 boot emitted a failure or machine-fault marker: $($serial.FullName)"
    }
    $phase47Lines = @($text -split "`r?`n" | Where-Object {
        $_.Contains('GXOS_NET10:MANAGED_HTTPS_PHASE47_')
    })
    if ($null -eq $referenceLines) {
        $referenceLines = $phase47Lines
    } elseif ((($phase47Lines -join "`n") -ne ($referenceLines -join "`n"))) {
        throw "Phase 47 raster telemetry/pixels were not deterministic: $($serial.FullName)"
    }
    $hash = [regex]::Matches($text,
        'MANAGED_HTTPS_PHASE47_FRAMEBUFFER_HASH_WORD=0x([0-9A-Fa-f]+)')
    if ($hash.Count -ne 8) { throw "Expected eight framebuffer hash words: $($serial.FullName)" }
    $reports += "serial=$($serial.FullName) sha256=$((Get-FileHash -LiteralPath $serial.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}

$summary = @(
    'MANAGED_KERNEL_PHASE47_RASTER_BOOT_SUMMARY=PASS',
    "MANAGED_KERNEL_PHASE47_RASTER_RUNS=$RunCount",
    "MANAGED_KERNEL_PHASE47_PHASE46_PIPELINE=$phase46Output",
    $reports)
Set-Content -LiteralPath (Join-Path $output 'phase47-summary.log') -Value $summary -Encoding ascii
Copy-Item -LiteralPath (Join-Path $phase46Output 'phase46-run-metadata.log') `
          -Destination (Join-Path $output 'phase47-run-metadata.log')

Write-Output "MANAGED_KERNEL_PHASE47_OUTPUT=$output"
Write-Output "MANAGED_KERNEL_PHASE47_EVIDENCE=$evidence"
Write-Output "MANAGED_KERNEL_PHASE47_RUNS=$RunCount"

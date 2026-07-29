[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GateDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedArtifactSha256,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedLoaderSha256,
    [int]$TimeoutSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$gate = [IO.Path]::GetFullPath($GateDirectory)
$esp = Join-Path $gate 'ESP'
$efi = Join-Path $esp 'EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $esp 'GXOS\gxos-managed-entry-probe.dll'
if (-not (Test-Path -LiteralPath $efi)) { throw "Harness not found: $efi" }

$qemu = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
if ($qemu) { $qemuPath = $qemu.Source }
else {
    $qemuPath = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
    if (-not (Test-Path -LiteralPath $qemuPath)) { throw 'qemu-system-x86_64.exe is required.' }
}
$share = Join-Path (Split-Path -Parent $qemuPath) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
$runRoot = Join-Path $gate ('perf-stall-runs-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$code = Join-Path $runRoot 'edk2-x86_64-code.fd'
$vars = Join-Path $runRoot 'ovmf-vars.fd'
$serial = Join-Path $runRoot 'serial.log'
$stdout = Join-Path $runRoot 'stdout.log'
$stderr = Join-Path $runRoot 'stderr.log'
Copy-Item -LiteralPath $ovmf -Destination $code
Copy-Item -LiteralPath $varsTemplate -Destination $vars
$artifactHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
$loaderHash = (Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash
if ($artifactHash -ne $ExpectedArtifactSha256) { throw "Managed artifact hash mismatch: $artifactHash" }
if ($loaderHash -ne $ExpectedLoaderSha256) { throw "Loader hash mismatch: $loaderHash" }
$arguments = @(
    '-machine', 'q35', '-m', '128M',
    '-drive', "if=pflash,format=raw,readonly=on,file=$code",
    '-drive', "if=pflash,format=raw,file=$vars",
    '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
    '-rtc', 'base=utc,clock=vm',
    '-boot', 'order=c', '-serial', "file:$serial",
    '-monitor', 'none', '-display', 'none', '-no-reboot', '-no-shutdown'
)
$process = Start-Process -FilePath $qemuPath -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
$completed = $process.WaitForExit($TimeoutSeconds * 1000)
if (-not $process.HasExited) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 250
$serialText = if (Test-Path -LiteralPath $serial) { [IO.File]::ReadAllText($serial) } else { '' }
$frequencyQuery = $null
$stallDelta = $null
$frequencyMatch = [regex]::Match($serialText, 'GXOS_NET10:PERF_FREQUENCY_QUERY=0x([0-9A-Fa-f]+)')
if ($frequencyMatch.Success) { $frequencyQuery = [Convert]::ToUInt64($frequencyMatch.Groups[1].Value, 16) }
$stallMatch = [regex]::Match($serialText, 'GXOS_NET10:QPC_STALL_DELTA=0x([0-9A-Fa-f]+)')
if ($stallMatch.Success) { $stallDelta = [Convert]::ToUInt64($stallMatch.Groups[1].Value, 16) }
$pass = $serialText.Contains('GXOS_NET10:PERF_SOURCE_INIT_OK') -and
    $serialText.Contains('GXOS_NET10:PERF_SOURCE_ACPI_PM_TIMER') -and
    $serialText.Contains('GXOS_NET10:PERF_FREQUENCY=0x') -and
    $frequencyQuery -ne $null -and $frequencyQuery -gt 0 -and
    $serialText.Contains('GXOS_NET10:QPC_CALL') -and
    $serialText.Contains('GXOS_NET10:QPC_OK=0x') -and
    $serialText.Contains('GXOS_NET10:QPC_IMMEDIATE_DELTA=0x') -and
    $serialText.Contains('GXOS_NET10:PERF_STALL_STATUS=0x0000000000000000') -and
    $serialText.Contains('GXOS_NET10:QPC_STALL_DELTA=0x') -and
    $stallDelta -ne $null -and $stallDelta -gt 0 -and
    $serialText.Contains('GXOS_NET10:PERF_STALL_TEST_OK') -and
    $serialText.Contains('GXOS_NET10:PERF_STALL_PROBE_COMPLETE') -and
    -not $serialText.Contains('GXOS_NET10:TIME_API_ENTER') -and
    -not $serialText.Contains('GXOS_NET10:FAULT_')
[PSCustomObject]@{
    Pass = $pass
    Classification = if ($pass) { 'PERF_STALL_DIAGNOSTIC_PASSED' } elseif ($completed) { 'EXITED' } else { 'TIMEOUT' }
    QemuVersion = (& $qemuPath '--version' 2>$null | Select-Object -First 1).Trim()
    FirmwareSha256 = (Get-FileHash $code -Algorithm SHA256).Hash
    ArtifactSha256 = $artifactHash
    LoaderSha256 = $loaderHash
    SerialLog = $serial
    Serial = $serialText.Trim()
} | ConvertTo-Json -Compress
if (-not $pass) { exit 2 }
Write-Output 'PERF_STALL_PROBE=PASSED'

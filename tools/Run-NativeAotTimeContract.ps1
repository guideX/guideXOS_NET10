[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GateDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedArtifactSha256,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedLoaderSha256,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 20
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
$runRoot = Join-Path $gate ('time-contract-runs-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$code = Join-Path $runRoot 'edk2-x86_64-code.fd'
Copy-Item -LiteralPath $ovmf -Destination $code
$qemuVersion = (& $qemuPath '--version' 2>$null | Select-Object -First 1).Trim()
$artifactHash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
if ($artifactHash -ne $ExpectedArtifactSha256) { throw "Managed artifact hash mismatch: $artifactHash" }
$firmwareHash = (Get-FileHash -LiteralPath $code -Algorithm SHA256).Hash
$results = @()

function Read-SerialText([string]$path) {
    try { return [IO.File]::ReadAllText($path) } catch { return '' }
}

for ($i = 1; $i -le $RunCount; $i++) {
    $runId = 'time-contract-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-run' + $i
    $serial = Join-Path $runRoot ($runId + '.serial.log')
    $stdout = Join-Path $runRoot ($runId + '.stdout.log')
    $stderr = Join-Path $runRoot ($runId + '.stderr.log')
    $vars = Join-Path $runRoot ($runId + '.vars.fd')
    Copy-Item -LiteralPath $varsTemplate -Destination $vars
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
    $reachedBoundary = $false
    $completed = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 500
    $classification = if ($completed) { 'EXITED' } else { 'TIMEOUT_AT_BOUNDARY' }
    $serialText = if (Test-Path -LiteralPath $serial) { Read-SerialText $serial } else { '' }
    $matches = [regex]::Matches($serialText, 'GXOS_NET10:FILETIME_CONVERSION_OK=0x([0-9A-Fa-f]{16})')
    $filetime = if ($matches.Count -eq 1) { [Convert]::ToUInt64($matches[0].Groups[1].Value, 16) } else { 0 }
    $nextBoundary = if ($serialText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!QueryPerformanceCounter')) { 'KERNEL32.dll!QueryPerformanceCounter' } else { 'unknown' }
    $loaderHash = (Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash
    if ($loaderHash -ne $ExpectedLoaderSha256) { throw "Loader hash mismatch: $loaderHash" }
    $pass = $serialText.Contains('GXOS_NET10:TIME_SOURCE=UEFI_GETTIME_QEMU_RTC_UTC_POLICY') -and
        $serialText.Contains('GXOS_NET10:GC_STARTUP_BEGIN') -and
        $serialText.Contains('GXOS_NET10:TIME_API_ENTER') -and
        $serialText.Contains('GXOS_NET10:TIME_API_RETURN=0x') -and
        $serialText.Contains('GXOS_NET10:TIME_UNSPECIFIED_TIMEZONE_UTC_POLICY') -and
        $serialText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!QueryPerformanceCounter') -and
        $serialText.Contains('GXOS_NET10:TIME_CONSUMER_PHASE=0x5') -and
        $serialText.Contains('GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000') -and
        $serialText.Contains('GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000') -and
        $serialText.Contains('GXOS_NET10:MANAGED_THREAD_REGISTERED=0') -and
        $serialText.Contains('GXOS_NET10:ALLOCATION_CONTEXT_VALID=0') -and
        $serialText.Contains('GXOS_NET10:TIME_API_COUNT=0x0000000000000001') -and
        $filetime -ne 0 -and $artifactHash -eq $ExpectedArtifactSha256 -and
        -not $serialText.Contains('GXOS_NET10:FAULT_')
    $results += [PSCustomObject]@{
        Sequence = $i
        RunId = $runId
        Pass = $pass
        Classification = if ($pass) { 'TIME_CONTRACT_PASSED_NEXT_IMPORT' } else { $classification }
        QemuVersion = $qemuVersion
        FirmwareSha256 = $firmwareHash
        ManagedHash = $artifactHash
        LoaderHash = $loaderHash
        TimeSource = 'UEFI_GETTIME_QEMU_RTC_UTC_POLICY'
        Filetime = ('0x{0:X16}' -f $filetime)
        CallCount = 1
        AdvancedBeyondBlocker = $serialText.Contains('GXOS_NET10:TIME_API_RETURN=0x') -and $nextBoundary -eq 'KERNEL32.dll!QueryPerformanceCounter'
        NextBoundary = $nextBoundary
        AllocationContext = 'limit=0;ptr=0;valid=0'
        FirstAllocation = 'not-run'
        Fault = $serialText.Contains('GXOS_NET10:FAULT_')
        SerialLog = $serial
        Serial = $serialText.Trim()
    }
}
$results | ForEach-Object { $_ | ConvertTo-Json -Compress }
if (@($results | Where-Object { -not $_.Pass }).Count -ne 0) { exit 2 }
Write-Output 'TIME_CONTRACT_VALIDATION=PASSED'

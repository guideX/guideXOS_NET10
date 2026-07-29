[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GateDirectory,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedArtifactSha256,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedLoaderSha256,
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 20,
    [int]$ExpectedFunctionalImports = 21,
    [int]$ExpectedFailfastImports = 103,
    [string]$RequiredNextBoundary = '',
    [string[]]$RequiredMarkers = @()
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
    $lastLength = -1
    $stableReads = 0
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        try {
            if (-not (Test-Path -LiteralPath $path)) {
                Start-Sleep -Milliseconds 100
                continue
            }
            $text = [IO.File]::ReadAllText($path)
            if ($text.Length -eq $lastLength) { $stableReads++ } else { $stableReads = 0 }
            $lastLength = $text.Length
            if ($stableReads -ge 2) { return $text }
        } catch [IO.IOException] {
        }
        Start-Sleep -Milliseconds 100
    }
    try { return [IO.File]::ReadAllText($path) } catch { return '' }
}

function Read-HexMarker([string]$text, [string]$marker) {
    $match = [regex]::Match($text, [regex]::Escape($marker) + '=0x([0-9A-Fa-f]+)')
    if (-not $match.Success) { return $null }
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
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
    $completed = $false
    $boundaryObserved = $false
    $terminalSummaryObserved = $false
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            $completed = $true
            break
        }
        try {
            if (Test-Path -LiteralPath $serial) {
                $liveText = [IO.File]::ReadAllText($serial)
                if ($liveText.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:') -or
                    $liveText.Contains('GXOS_NET10:FAIL:')) {
                    $boundaryObserved = $true
                    # The loader emits the QPC summary after recording the
                    # boundary.  Let that summary drain before stopping QEMU
                    # so a valid boundary is not mistaken for a truncated run.
                    if ($liveText.Contains('GXOS_NET10:QPC_REGRESSIONS=')) {
                        $terminalSummaryObserved = $true
                        break
                    }
                }
            }
        } catch [IO.IOException] {
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 5 -ErrorAction SilentlyContinue
    }
    for ($cleanupAttempt = 1; $cleanupAttempt -le 20; $cleanupAttempt++) {
        if (-not (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 100
    }
    Start-Sleep -Milliseconds 300
    $classification = if ($completed) { 'EXITED' } elseif ($boundaryObserved) { 'BOUNDARY_OBSERVED' } else { 'TIMEOUT_AT_BOUNDARY' }
    $serialText = if (Test-Path -LiteralPath $serial) { Read-SerialText $serial } else { '' }
    $filetime = Read-HexMarker $serialText 'GXOS_NET10:FILETIME_CONVERSION_OK'
    $frequency = Read-HexMarker $serialText 'GXOS_NET10:PERF_FREQUENCY'
    $frequencyQuery = Read-HexMarker $serialText 'GXOS_NET10:PERF_FREQUENCY_QUERY'
    $initialRaw = Read-HexMarker $serialText 'GXOS_NET10:PERF_INITIAL_RAW'
    $qpcCount = Read-HexMarker $serialText 'GXOS_NET10:QPC_COUNT'
    $qpcFirst = Read-HexMarker $serialText 'GXOS_NET10:QPC_FIRST'
    $qpcLast = Read-HexMarker $serialText 'GXOS_NET10:QPC_LAST'
    $qpcMinimumDelta = Read-HexMarker $serialText 'GXOS_NET10:QPC_MIN_DELTA'
    $qpcMaximumDelta = Read-HexMarker $serialText 'GXOS_NET10:QPC_MAX_DELTA'
    $qpcRegressions = Read-HexMarker $serialText 'GXOS_NET10:QPC_REGRESSIONS'
    $nextMatch = [regex]::Match($serialText, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
    $nextBoundary = if ($nextMatch.Success) { $nextMatch.Groups[1].Value } else { 'unknown' }
    $requiredBoundaryPass = [string]::IsNullOrWhiteSpace($RequiredNextBoundary) -or $nextBoundary -eq $RequiredNextBoundary
    $requiredMarkersPass = @($RequiredMarkers | Where-Object { -not $serialText.Contains($_) }).Count -eq 0
    $source = if ($serialText.Contains('GXOS_NET10:PERF_SOURCE_ACPI_PM_TIMER')) { 'ACPI_PM_TIMER' }
              elseif ($serialText.Contains('GXOS_NET10:PERF_SOURCE_TSC_INVARIANT_CPUID_15')) { 'INVARIANT_TSC_CPUID_15' }
              else { 'unknown' }
    $loaderHash = (Get-FileHash -LiteralPath $efi -Algorithm SHA256).Hash
    if ($loaderHash -ne $ExpectedLoaderSha256) { throw "Loader hash mismatch: $loaderHash" }
    $pass = $serialText.Contains("GXOS_NET10:PE_IMPORT_FUNCTIONAL=$ExpectedFunctionalImports") -and
        $serialText.Contains("GXOS_NET10:PE_IMPORT_FAILFAST=$ExpectedFailfastImports") -and
        $source -ne 'unknown' -and
        $serialText.Contains('GXOS_NET10:PERF_SOURCE_INIT_OK') -and
        $frequency -ne $null -and $frequency -gt 0 -and
        ($frequencyQuery -eq $null -or $frequencyQuery -eq $frequency) -and
        $initialRaw -ne $null -and
        $serialText.Contains('GXOS_NET10:GC_STARTUP_BEGIN') -and
        $serialText.Contains('GXOS_NET10:QPC_CALL') -and
        $serialText.Contains('GXOS_NET10:QPC_OK=0x') -and
        $qpcCount -ne $null -and $qpcCount -ge 1 -and
        $qpcFirst -ne $null -and $qpcLast -ne $null -and $qpcLast -ge $qpcFirst -and
        $qpcMinimumDelta -ne $null -and $qpcMaximumDelta -ne $null -and $qpcMaximumDelta -ge $qpcMinimumDelta -and
        $qpcRegressions -eq 0 -and
        $serialText.Contains('GXOS_NET10:TIME_SOURCE=UEFI_GETTIME_QEMU_RTC_UTC_POLICY') -and
        $serialText.Contains('GXOS_NET10:TIME_API_ENTER') -and
        $serialText.Contains('GXOS_NET10:TIME_API_RETURN=0x') -and
        $serialText.Contains('GXOS_NET10:TIME_UNSPECIFIED_TIMEZONE_UTC_POLICY') -and
        $nextBoundary -ne 'unknown' -and $nextBoundary -ne 'KERNEL32.dll!QueryPerformanceCounter' -and
        $serialText.Contains('GXOS_NET10:TIME_CONSUMER_PHASE=0x18') -and
        $serialText.Contains('GXOS_NET10:TLS_ALLOC_LIMIT=0x0000000000000000') -and
        $serialText.Contains('GXOS_NET10:TLS_ALLOC_PTR=0x0000000000000000') -and
        $serialText.Contains('GXOS_NET10:MANAGED_THREAD_REGISTERED=0') -and
        $serialText.Contains('GXOS_NET10:ALLOCATION_CONTEXT_VALID=0') -and
        $serialText.Contains('GXOS_NET10:TIME_API_COUNT=0x0000000000000001') -and
        $filetime -ne $null -and $filetime -ne 0 -and $artifactHash -eq $ExpectedArtifactSha256 -and
        $requiredBoundaryPass -and $requiredMarkersPass -and
        -not $serialText.Contains('GXOS_NET10:GC_STARTUP_ADVANCED') -and
        -not $serialText.Contains('GXOS_NET10:FIRST_ALLOCATION_OK') -and
        -not $serialText.Contains('GXOS_NET10:FAULT_')
    $results += [PSCustomObject]@{
        Sequence = $i
        RunId = $runId
        Pass = $pass
        Classification = if ($pass) { 'QPC_CONTRACT_PASSED_NEXT_IMPORT' } else { $classification }
        QemuVersion = $qemuVersion
        FirmwareSha256 = $firmwareHash
        ManagedHash = $artifactHash
        LoaderHash = $loaderHash
        TimeSource = 'UEFI_GETTIME_QEMU_RTC_UTC_POLICY'
        PerformanceSource = $source
        Frequency = if ($null -eq $frequency) { 'unknown' } else { ('0x{0:X}' -f $frequency) }
        InitialRaw = if ($null -eq $initialRaw) { 'unknown' } else { ('0x{0:X}' -f $initialRaw) }
        SourceObservations = if ($null -eq $initialRaw -or $null -eq $qpcFirst) { 0 } else { 2 }
        Filetime = if ($null -eq $filetime) { 'unknown' } else { ('0x{0:X16}' -f $filetime) }
        CallCount = $qpcCount
        First = if ($null -eq $qpcFirst) { 'unknown' } else { ('0x{0:X}' -f $qpcFirst) }
        Last = if ($null -eq $qpcLast) { 'unknown' } else { ('0x{0:X}' -f $qpcLast) }
        MinDelta = if ($null -eq $qpcMinimumDelta) { 'unknown' } else { ('0x{0:X}' -f $qpcMinimumDelta) }
        MaxDelta = if ($null -eq $qpcMaximumDelta) { 'unknown' } else { ('0x{0:X}' -f $qpcMaximumDelta) }
        Regressions = $qpcRegressions
        AdvancedBeyondBlocker = $serialText.Contains('GXOS_NET10:TIME_API_RETURN=0x') -and $nextBoundary -ne 'unknown' -and $nextBoundary -ne 'KERNEL32.dll!QueryPerformanceCounter'
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
Write-Output 'QPC_CONTRACT_VALIDATION=PASSED'

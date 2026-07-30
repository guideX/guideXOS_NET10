[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GateDirectory,
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [ValidateSet('Positive', 'Disabled')]
    [string]$Mode = 'Positive',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 120,
    [int]$NoProgressSeconds = 20,
    [string]$NativeAotSourceArtifactPath = '',
    [string]$RuntimeArchivePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$startup = Join-Path $gate 'ESP\startup.nsh'
$payloadSource = if ([string]::IsNullOrWhiteSpace($NativeAotSourceArtifactPath)) { Join-Path $root 'artifacts\allocation-enabled-final-20260728-060439-726\shared\gxos-managed-entry-probe.dll' } else { [IO.Path]::GetFullPath($NativeAotSourceArtifactPath) }
$runtimeArchive = if ([string]::IsNullOrWhiteSpace($RuntimeArchivePath)) { Join-Path $root 'artifacts\allocation-enabled-final-20260728-060439-726\static\gxos-managed-entry-probe.lib' } else { [IO.Path]::GetFullPath($RuntimeArchivePath) }
$validationScript = [IO.Path]::GetFullPath($PSCommandPath)
$validatorScript = Join-Path $root 'tools\Validate-CrtInittermEEvidence.ps1'
$runsRoot = Join-Path $evidence 'runs'
foreach ($path in @($efi,$payload,$startup,$payloadSource,$runtimeArchive,$validatorScript)) { if (-not (Test-Path $path)) { throw "Required path missing: $path" } }
if (Test-Path $evidence) { throw "Evidence directory already exists: $evidence" }
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path $qemu)) { throw 'qemu-system-x86_64.exe is required.' }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path $ovmf) -or -not (Test-Path $varsTemplate)) { throw 'OVMF code and vars templates are required.' }

function Write-JsonFile([string]$path, $value) { $value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8 }
function Write-Event([string]$path, [string]$name, [hashtable]$data = @{}) {
    $record = [ordered]@{ Utc = (Get-Date).ToUniversalTime().ToString('o'); Event = $name }
    foreach ($key in $data.Keys) { $record[$key] = $data[$key] }
    ($record | ConvertTo-Json -Compress -Depth 12) | Add-Content -LiteralPath $path -Encoding utf8
}
function Read-Serial([string]$path) {
    if (-not (Test-Path $path)) { return '' }
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        $stream = $null
        $reader = $null
        try {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            $reader = New-Object IO.StreamReader($stream)
            $value = [string]$reader.ReadToEnd()
            $reader.Dispose(); $stream.Dispose()
            return $value
        } catch {
            if ($null -ne $reader) { $reader.Dispose() }
            elseif ($null -ne $stream) { $stream.Dispose() }
            Start-Sleep -Milliseconds 50
        }
    }
    return ''
}
function Snapshot([string]$kind, [string]$path) {
    $item = Get-Item $path
    [PSCustomObject]@{ Kind = $kind; Path = [IO.Path]::GetFullPath($path); Sha256 = (Get-FileHash $path -Algorithm SHA256).Hash.ToUpperInvariant(); Length = [int64]$item.Length; LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o') }
}
function Get-ArtifactSet {
    @(
        Snapshot 'efi_loader' $efi
        Snapshot 'nativeaot_payload' $payload
        Snapshot 'nativeaot_payload_source' $payloadSource
        Snapshot 'runtime_archive' $runtimeArchive
        Snapshot 'ovmf_code' $ovmf
        Snapshot 'ovmf_vars_template' $varsTemplate
        Snapshot 'esp_startup' $startup
        Snapshot 'qemu_executable' $qemu
        Snapshot 'validation_runner' $validationScript
        Snapshot 'evidence_validator' $validatorScript
    )
}
function Assert-ArtifactSet($expected) {
    $current = @(Get-ArtifactSet)
    foreach ($item in $expected) {
        $now = $current | Where-Object Kind -eq $item.Kind | Select-Object -First 1
        if ($null -eq $now -or $now.Sha256 -ne $item.Sha256 -or $now.Length -ne $item.Length -or $now.LastWriteTimeUtc -ne $item.LastWriteTimeUtc) { throw "Execution artifact changed: $($item.Kind)" }
    }
    return $current
}
function Last-Marker([string]$text) {
    $markers = @('GXOS_NET10:CRT_INITTERM_E_OK','GXOS_NET10:CRT_INITTERM_E_BEGIN','GXOS_NET10:SLIST_HEAD_INITIALIZED_OK','GXOS_NET10:UNEXPECTED_IMPORT_CALL:','GXOS_NET10:QPC_REGRESSIONS=')
    $last = 'none'; $position = -1
    foreach ($marker in $markers) { $at = $text.LastIndexOf($marker, [StringComparison]::Ordinal); if ($at -gt $position) { $position = $at; $last = $marker } }
    return $last
}

$qemuVersion = (& $qemu '--version' 2>$null | Select-Object -First 1).Trim()
$artifactSet = @(Get-ArtifactSet)
$evidenceId = Split-Path -Leaf $evidence
Write-JsonFile (Join-Path $evidence 'artifact-manifest.json') ([ordered]@{ EvidenceId = $evidenceId; CreatedUtc = (Get-Date).ToUniversalTime().ToString('o'); RepositoryRoot = $root; GateDirectory = $gate; Mode = $Mode; QemuVersion = $qemuVersion; Acceleration = 'tcg,thread=multi'; MachineType = 'q35'; Artifacts = $artifactSet })
Write-JsonFile (Join-Path $evidence 'validation-context.json') ([ordered]@{ EvidenceId = $evidenceId; Mode = $Mode; RunCount = $RunCount; TimeoutSeconds = $TimeoutSeconds; NoProgressSeconds = $NoProgressSeconds; QemuPath = $qemu; QemuVersion = $qemuVersion; StartedUtc = (Get-Date).ToUniversalTime().ToString('o') })

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) { throw "Preexisting QEMU process detected before run $sequence." }
    $runId = "$evidenceId-run$sequence"
    $runDirectory = Join-Path $runsRoot ("run-{0}" -f $sequence)
    New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
    $serial = Join-Path $runDirectory 'serial.log'; $stdout = Join-Path $runDirectory 'qemu.stdout.log'; $stderr = Join-Path $runDirectory 'qemu.stderr.log'; $events = Join-Path $runDirectory 'harness-events.jsonl'; $vars = Join-Path $runDirectory 'ovmf-vars.fd'
    Copy-Item $varsTemplate $vars
    Write-Event $events 'run-prepared' @{ RunId = $runId; Sequence = $sequence; VarsSha256 = (Get-FileHash $vars -Algorithm SHA256).Hash }
    $args = @('-machine','q35','-accel','tcg,thread=multi','-m','128M','-drive',"if=pflash,format=raw,readonly=on,file=`"$ovmf`"",'-drive',"if=pflash,format=raw,file=`"$vars`"",'-drive','file="fat:rw:ESP",format=raw,if=ide,index=0,media=disk','-rtc','base=utc,clock=vm','-boot','order=c','-serial',"file:$serial",'-monitor','none','-display','none','-no-reboot','-no-shutdown')
    $process = Start-Process -FilePath $qemu -ArgumentList $args -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
    $startUtc = (Get-Date).ToUniversalTime()
    Write-Event $events 'qemu-started' @{ RunId = $runId; Pid = $process.Id; StartUtc = $startUtc.ToString('o'); Arguments = ($args -join ' ') }
    $requiredBoundary = if ($Mode -eq 'Positive') { 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm' } else { 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e' }
    $summaryObserved = $false; $boundaryObserved = $false; $captured = $false; $timeoutReason = ''; $deadline = $startUtc.AddSeconds($TimeoutSeconds); $lastProgress = $startUtc; $lastLength = 0
    while ((Get-Date).ToUniversalTime() -lt $deadline) {
        $text = Read-Serial $serial
        if ($text.Length -ne $lastLength) { $lastLength = $text.Length; $lastProgress = (Get-Date).ToUniversalTime(); Write-Event $events 'guest-progress' @{ RunId = $runId; Length = $lastLength; LastMarker = (Last-Marker $text) } }
        $boundaryObserved = $text.Contains($requiredBoundary)
        $summaryObserved = $text.Contains('GXOS_NET10:QPC_REGRESSIONS=') -and $text.Contains('GXOS_NET10:ALLOCATION_CONTEXT_VALID=0')
        if ($boundaryObserved -and $summaryObserved) { $captured = $true; Write-Event $events 'complete-evidence-captured' @{ RunId = $runId; SerialLength = $text.Length }; Start-Sleep -Milliseconds 500; break }
        try { if ($process.HasExited) { $timeoutReason = 'qemu-exited-before-complete-evidence'; break } } catch { }
        if ((Get-Date).ToUniversalTime() -gt $lastProgress.AddSeconds($NoProgressSeconds)) { $timeoutReason = 'guest-no-progress'; Write-Event $events 'no-progress-timeout' @{ RunId = $runId; Reason = $timeoutReason }; break }
        Start-Sleep -Milliseconds 100
    }
    try { $process.Refresh() } catch { }
    $alive = $false; try { $alive = -not $process.HasExited } catch { }
    if ($alive) { Write-Event $events 'qemu-stop-requested' @{ RunId = $runId; Pid = $process.Id; Reason = if ($captured) { 'capture-complete' } else { 'incomplete-evidence' } }; Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    $cleanupComplete = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue | Where-Object Id -eq $process.Id).Count -eq 0
    $finalText = Read-Serial $serial
    $serialItem = if (Test-Path $serial) { Get-Item $serial } else { $null }
    $exitCode = $null; try { $process.Refresh(); if ($process.HasExited) { $exitCode = [int]$process.ExitCode } } catch { }
    $endUtc = (Get-Date).ToUniversalTime()
    $runData = [ordered]@{ EvidenceId = $evidenceId; RunId = $runId; Sequence = $sequence; Mode = $Mode; Pass = $captured -and $cleanupComplete; QemuPid = $process.Id; QemuVersion = $qemuVersion; QemuStartUtc = $startUtc.ToString('o'); QemuEndUtc = $endUtc.ToString('o'); QemuExitCode = $exitCode; ProcessExitedNaturally = (-not $alive); CleanupComplete = $cleanupComplete; TimeoutReason = $timeoutReason; BoundaryObserved = $boundaryObserved; SummaryObserved = $summaryObserved; LastObservedGuestMarker = (Last-Marker $finalText); FinalSerialLength = if ($null -eq $serialItem) { 0 } else { [int64]$serialItem.Length }; SerialLog = $serial.Substring($evidence.Length).TrimStart('\','/'); QemuStdoutLog = $stdout.Substring($evidence.Length).TrimStart('\','/'); QemuStderrLog = $stderr.Substring($evidence.Length).TrimStart('\','/'); HarnessEventLog = $events.Substring($evidence.Length).TrimStart('\','/'); VarsPath = $vars.Substring($evidence.Length).TrimStart('\','/'); ArtifactSetAfterRun = @(Assert-ArtifactSet $artifactSet) }
    Write-JsonFile (Join-Path $runDirectory 'run.json') $runData
    Write-Event $events 'run-finalized' @{ RunId = $runId; EndUtc = $endUtc.ToString('o'); CleanupComplete = $cleanupComplete; FinalSerialLength = $runData.FinalSerialLength }
    if (-not $cleanupComplete) { throw "QEMU cleanup failed for $runId." }
}

$context = Get-Content (Join-Path $evidence 'validation-context.json') -Raw | ConvertFrom-Json
$context | Add-Member -NotePropertyName FinishedUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o'))
Write-JsonFile (Join-Path $evidence 'validation-context.json') $context
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validatorScript -EvidenceRoot $evidence -Mode $Mode -ExpectedRunCount $RunCount
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "CRT_INITTERM_E_EVIDENCE_ROOT=$evidence"
Write-Output 'CRT_INITTERM_E_FINAL_VALIDATION=PASSED'

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [ValidateSet('Positive', 'Disabled')] [string]$Mode = 'Positive',
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
$payloadSource = if ([string]::IsNullOrWhiteSpace($NativeAotSourceArtifactPath)) { $payload } else { [IO.Path]::GetFullPath($NativeAotSourceArtifactPath) }
$runtimeArchive = if ([string]::IsNullOrWhiteSpace($RuntimeArchivePath)) { Join-Path $root 'artifacts\allocation-enabled-final-20260728-060439-726\static\gxos-managed-entry-probe.lib' } else { [IO.Path]::GetFullPath($RuntimeArchivePath) }
$validatorScript = Join-Path $root 'tools\Validate-CrtStricmpEvidence.ps1'
$runnerScript = [IO.Path]::GetFullPath($PSCommandPath)
$sourceFiles = @(
    (Join-Path $root 'src\Gate4Harness\crt_stricmp.c'),
    (Join-Path $root 'src\Gate4Harness\crt_stricmp.h'),
    (Join-Path $root 'src\Gate4Harness\gate4_loader.c'),
    (Join-Path $root 'tools\Build-Gate4Harness.ps1')
)
$runsRoot = Join-Path $evidence 'runs'
$expectedBoundary = if ($Mode -eq 'Positive') { 'KERNEL32.dll!GetSystemInfo' } else { 'api-ms-win-crt-string-l1-1-0.dll!_stricmp' }

foreach ($path in @($efi,$payload,$startup,$payloadSource,$runtimeArchive,$validatorScript) + $sourceFiles) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required path missing: $path" }
}
if (Test-Path -LiteralPath $evidence) { throw "Evidence directory already exists: $evidence" }
if ($RunCount -lt 1) { throw 'RunCount must be positive.' }
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null

function Write-JsonFile([string]$path, $value) { $value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8 }
function Write-Event([string]$path, [string]$name, [hashtable]$data = @{}) {
    $record = [ordered]@{ Utc = (Get-Date).ToUniversalTime().ToString('o'); Event = $name }
    foreach ($key in $data.Keys) { $record[$key] = $data[$key] }
    ($record | ConvertTo-Json -Compress -Depth 12) | Add-Content -LiteralPath $path -Encoding utf8
}
function Snapshot([string]$kind, [string]$path) {
    $item = Get-Item -LiteralPath $path
    [PSCustomObject]@{
        Kind = $kind; Path = [IO.Path]::GetFullPath($path)
        Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
        Length = [int64]$item.Length; LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
    }
}
function Get-ArtifactSet {
    $items = @(
        (Snapshot 'efi_loader' $efi)
        (Snapshot 'nativeaot_payload' $payload)
        (Snapshot 'nativeaot_payload_source' $payloadSource)
        (Snapshot 'runtime_archive' $runtimeArchive)
        (Snapshot 'ovmf_code' $ovmf)
        (Snapshot 'ovmf_vars_template' $varsTemplate)
        (Snapshot 'esp_startup' $startup)
        (Snapshot 'qemu_executable' $qemu)
        (Snapshot 'validation_runner' $runnerScript)
        (Snapshot 'evidence_validator' $validatorScript)
    )
    foreach ($sourceFile in $sourceFiles) { $items += Snapshot 'contract_source' $sourceFile }
    return $items
}
function Assert-ArtifactSet($expected) {
    foreach ($item in $expected) {
        $now = Snapshot $item.Kind ([string]$item.Path)
        if ($now.Sha256 -ne $item.Sha256 -or $now.Length -ne $item.Length -or
            $now.LastWriteTimeUtc -ne $item.LastWriteTimeUtc) {
            throw "Execution artifact changed: $($item.Kind): $($item.Path)"
        }
    }
}
function Test-QemuGone([int]$processId) {
    try { return (Get-Process -Id $processId -ErrorAction Stop).ProcessName -ne 'qemu-system-x86_64' }
    catch { return $true }
}
function Wait-QemuGone([int]$processId, [int]$seconds) {
    $deadline = (Get-Date).ToUniversalTime().AddSeconds($seconds)
    while ((Get-Date).ToUniversalTime() -lt $deadline) {
        if (Test-QemuGone $processId) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return (Test-QemuGone $processId)
}
function Read-Serial([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try {
        $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = New-Object IO.StreamReader($stream)
        $value = [string]$reader.ReadToEnd()
        $reader.Dispose(); $stream.Dispose(); return $value
    } catch { return '' }
}

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Path) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw 'qemu-system-x86_64.exe is required.' }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf) -or -not (Test-Path -LiteralPath $varsTemplate)) { throw 'OVMF code and vars templates are required.' }
$qemuVersion = (& $qemu '--version' 2>$null | Select-Object -First 1).Trim()
$artifactSet = @(Get-ArtifactSet)
$evidenceId = Split-Path -Leaf $evidence
Write-JsonFile (Join-Path $evidence 'artifact-manifest.json') ([ordered]@{
    EvidenceId = $evidenceId; CreatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    RepositoryRoot = $root; GateDirectory = $gate; Mode = $Mode; ExpectedBoundary = $expectedBoundary
    QemuVersion = $qemuVersion; Acceleration = 'tcg,thread=multi'; MachineType = 'q35'; Artifacts = $artifactSet
})
Write-JsonFile (Join-Path $evidence 'validation-context.json') ([ordered]@{
    EvidenceId = $evidenceId; Mode = $Mode; RunCount = $RunCount; ExpectedBoundary = $expectedBoundary
    TimeoutSeconds = $TimeoutSeconds; NoProgressSeconds = $NoProgressSeconds
    QemuPath = $qemu; QemuVersion = $qemuVersion; StartedUtc = (Get-Date).ToUniversalTime().ToString('o')
})

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
        throw "Preexisting QEMU process detected before run $sequence."
    }
    $runId = "$evidenceId-run$sequence"
    $runDirectory = Join-Path $runsRoot ("run-{0}" -f $sequence)
    New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
    $serial = Join-Path $runDirectory 'serial.log'; $stdout = Join-Path $runDirectory 'qemu.stdout.log'
    $stderr = Join-Path $runDirectory 'qemu.stderr.log'; $events = Join-Path $runDirectory 'harness-events.jsonl'
    $vars = Join-Path $runDirectory 'ovmf-vars.fd'; Copy-Item -LiteralPath $varsTemplate -Destination $vars
    Write-Event $events 'run-prepared' @{ RunId = $runId; Sequence = $sequence; VarsSha256 = (Get-FileHash -LiteralPath $vars -Algorithm SHA256).Hash }
    $args = @('-machine','q35','-accel','tcg,thread=multi','-m','128M',
              '-drive',"if=pflash,format=raw,readonly=on,file=`"$ovmf`"",
              '-drive',"if=pflash,format=raw,file=`"$vars`"",
              '-drive','file="fat:rw:ESP",format=raw,if=ide,index=0,media=disk',
              '-rtc','base=utc,clock=vm','-boot','order=c','-serial',"file:$serial",
              '-monitor','none','-display','none','-no-reboot','-no-shutdown')
    $process = Start-Process -FilePath $qemu -ArgumentList $args -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
    $processId = [int]$process.Id; $startUtc = (Get-Date).ToUniversalTime()
    Write-Event $events 'qemu-started' @{ RunId = $runId; Pid = $processId; StartUtc = $startUtc.ToString('o'); Arguments = ($args -join ' ') }
    $summaryObserved = $false; $boundaryObserved = $false; $captured = $false; $timeoutReason = ''
    $boundary = ''; $deadline = $startUtc.AddSeconds($TimeoutSeconds); $lastProgress = $startUtc; $lastLength = 0
    while ((Get-Date).ToUniversalTime() -lt $deadline) {
        $text = Read-Serial $serial
        if ($text.Length -ne $lastLength) { $lastLength = $text.Length; $lastProgress = (Get-Date).ToUniversalTime(); Write-Event $events 'guest-progress' @{ RunId = $runId; Length = $lastLength } }
        $matches = [regex]::Matches($text, 'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)')
        if ($matches.Count -ne 0) { $boundary = $matches[$matches.Count - 1].Groups[1].Value; $boundaryObserved = $boundary -eq $expectedBoundary }
        $summaryObserved = $text.Contains('GXOS_NET10:QPC_REGRESSIONS=') -and $text.Contains('GXOS_NET10:ALLOCATION_CONTEXT_VALID=0')
        if ($Mode -eq 'Positive') { $summaryObserved = $summaryObserved -and $text.Contains('GXOS_NET10:CRT_STRICMP_CALL_COUNT=') -and $text.Contains('GXOS_NET10:CRT_STRICMP_OK') }
        if ($boundaryObserved -and $summaryObserved) { $captured = $true; Write-Event $events 'complete-evidence-captured' @{ RunId = $runId; SerialLength = $text.Length; Boundary = $boundary }; Start-Sleep -Milliseconds 500; break }
        try { if ($process.HasExited) { $timeoutReason = 'qemu-exited-before-complete-evidence'; break } } catch { }
        if ((Get-Date).ToUniversalTime() -gt $lastProgress.AddSeconds($NoProgressSeconds)) { $timeoutReason = 'guest-no-progress'; Write-Event $events 'no-progress-timeout' @{ RunId = $runId; Reason = $timeoutReason }; break }
        Start-Sleep -Milliseconds 100
    }
    try { $process.Refresh() } catch { }
    $alive = $false; try { $alive = -not $process.HasExited } catch { }
    if ($alive) { Write-Event $events 'qemu-stop-requested' @{ RunId = $runId; Pid = $processId; Reason = if ($captured) { 'capture-complete' } else { 'incomplete-evidence' } }; Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue }
    $cleanupComplete = Wait-QemuGone $processId 15; Start-Sleep -Milliseconds 300
    $finalText = Read-Serial $serial; $serialItem = if (Test-Path -LiteralPath $serial) { Get-Item -LiteralPath $serial } else { $null }
    $exitCode = $null; try { $process.Refresh(); if ($process.HasExited) { $exitCode = [int]$process.ExitCode } } catch { }
    $endUtc = (Get-Date).ToUniversalTime()
    Write-JsonFile (Join-Path $runDirectory 'run.json') ([ordered]@{
        EvidenceId = $evidenceId; RunId = $runId; Sequence = $sequence; Mode = $Mode; ExpectedBoundary = $expectedBoundary
        Pass = $captured -and $cleanupComplete; QemuPid = $processId; QemuVersion = $qemuVersion
        QemuStartUtc = $startUtc.ToString('o'); QemuEndUtc = $endUtc.ToString('o'); QemuExitCode = $exitCode
        ProcessExitedNaturally = (-not $alive); CleanupComplete = $cleanupComplete; TimeoutReason = $timeoutReason
        BoundaryObserved = $boundaryObserved; SummaryObserved = $summaryObserved; Boundary = $boundary
        LastObservedGuestMarker = if ($boundary -eq '') { '' } else { "GXOS_NET10:UNEXPECTED_IMPORT_CALL:$boundary" }
        FinalSerialLength = if ($null -eq $serialItem) { 0 } else { [int64]$serialItem.Length }
        SerialLog = $serial.Substring($evidence.Length).TrimStart('\','/'); QemuStdoutLog = $stdout.Substring($evidence.Length).TrimStart('\','/')
        QemuStderrLog = $stderr.Substring($evidence.Length).TrimStart('\','/'); HarnessEventLog = $events.Substring($evidence.Length).TrimStart('\','/'); VarsPath = $vars.Substring($evidence.Length).TrimStart('\','/')
    })
    Write-Event $events 'run-finalized' @{ RunId = $runId; EndUtc = $endUtc.ToString('o'); CleanupComplete = $cleanupComplete; FinalSerialLength = if ($null -eq $serialItem) { 0 } else { [int64]$serialItem.Length } }
    if (-not $cleanupComplete) { throw "QEMU cleanup failed for $runId." }
    [void](Assert-ArtifactSet $artifactSet)
}

$context = Get-Content -LiteralPath (Join-Path $evidence 'validation-context.json') -Raw | ConvertFrom-Json
$context | Add-Member -NotePropertyName FinishedUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o'))
Write-JsonFile (Join-Path $evidence 'validation-context.json') $context
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validatorScript -EvidenceRoot $evidence -Mode $Mode -ExpectedRunCount $RunCount
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "CRT_STRICMP_EVIDENCE_ROOT=$evidence"
Write-Output 'CRT_STRICMP_FINAL_VALIDATION=PASSED'

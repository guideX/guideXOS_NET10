[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GateDirectory,
    [string]$EvidenceDirectory = '',
    [ValidateSet('Positive', 'Disabled')]
    [string]$Mode = 'Positive',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 300,
    [int]$NoProgressSeconds = 90,
    [string]$ExpectedArtifactSha256 = '',
    [string]$ExpectedLoaderSha256 = '',
    [string]$NativeAotSourceArtifactPath = '',
    [string]$Acceleration = 'tcg,thread=multi',
    [string]$MachineType = 'q35',
    [string]$CpuModel = '',
    [ValidateSet('File', 'Tcp')]
    [string]$SerialTransport = 'File',
    [ValidateSet('Normal', 'AboveNormal', 'High')]
    [string]$ProcessPriority = 'Normal',
    [switch]$DisableLiveSerialPolling,
    [string]$RuntimeArchivePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$esp = Join-Path $gate 'ESP'
$efi = Join-Path $esp 'EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $esp 'GXOS\gxos-managed-entry-probe.dll'
$startup = Join-Path $esp 'startup.nsh'
$payloadSource = if ([string]::IsNullOrWhiteSpace($NativeAotSourceArtifactPath)) {
    Join-Path $root 'artifacts\allocation-enabled-final-20260728-060439-726\shared\gxos-managed-entry-probe.dll'
} else {
    [IO.Path]::GetFullPath($NativeAotSourceArtifactPath)
}
$validationScript = [IO.Path]::GetFullPath($PSCommandPath)
$validatorScript = Join-Path $root 'tools\Validate-SlistEvidence.ps1'
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $root ('evidence\generated\slist-initialize-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$runsRoot = Join-Path $evidence 'runs'
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($RuntimeArchivePath)) {
    $RuntimeArchivePath = Join-Path $root 'artifacts\allocation-enabled-final-20260728-060439-726\static\gxos-managed-entry-probe.lib'
}
$runtimeArchive = [IO.Path]::GetFullPath($RuntimeArchivePath)

if (-not (Test-Path -LiteralPath $efi)) { throw "EFI loader not found: $efi" }
if (-not (Test-Path -LiteralPath $payload)) { throw "NativeAOT payload not found: $payload" }
if (-not (Test-Path -LiteralPath $startup)) { throw "ESP startup script not found: $startup" }
if (-not (Test-Path -LiteralPath $payloadSource)) { throw "NativeAOT source artifact not found: $payloadSource" }
if (-not (Test-Path -LiteralPath $runtimeArchive)) { throw "Runtime archive not found: $runtimeArchive" }
if (-not (Test-Path -LiteralPath $validatorScript)) { throw "Evidence validator not found: $validatorScript" }

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
if ($null -eq $qemuCommand) {
    $qemuPath = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
    if (-not (Test-Path -LiteralPath $qemuPath)) { throw 'qemu-system-x86_64.exe is required.' }
} else {
    $qemuPath = [IO.Path]::GetFullPath($qemuCommand.Source)
}
$qemuShare = Join-Path (Split-Path -Parent $qemuPath) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf)) { throw "OVMF code not found: $ovmf" }
if (-not (Test-Path -LiteralPath $varsTemplate)) { throw "OVMF vars template not found: $varsTemplate" }

function Write-JsonFile([string]$path, $value) {
    $value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8
}

function Get-RelativeEvidencePath([string]$path) {
    return $path.Substring($evidence.Length).TrimStart('\', '/')
}

function Get-ArtifactSnapshot([string]$kind, [string]$path) {
    $item = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    return [PSCustomObject]@{
        Kind = $kind
        Path = [IO.Path]::GetFullPath($path)
        Sha256 = $hash
        Length = [int64]$item.Length
        LastWriteTimeUtc = $item.LastWriteTimeUtc.ToString('o')
    }
}

function Get-CurrentArtifactSet {
    @(
        Get-ArtifactSnapshot 'efi_loader' $efi
        Get-ArtifactSnapshot 'nativeaot_payload' $payload
        Get-ArtifactSnapshot 'nativeaot_payload_source' $payloadSource
        Get-ArtifactSnapshot 'runtime_archive' $runtimeArchive
        Get-ArtifactSnapshot 'ovmf_code' $ovmf
        Get-ArtifactSnapshot 'ovmf_vars_template' $varsTemplate
        Get-ArtifactSnapshot 'esp_startup' $startup
        Get-ArtifactSnapshot 'qemu_executable' $qemuPath
        Get-ArtifactSnapshot 'validation_script' $validationScript
        Get-ArtifactSnapshot 'evidence_validator' $validatorScript
    )
}

function Compare-ArtifactSet($expected) {
    $current = @(Get-CurrentArtifactSet)
    foreach ($entry in $expected) {
        $now = $current | Where-Object { $_.Kind -eq $entry.Kind } | Select-Object -First 1
        if ($null -eq $now) { throw "Execution artifact disappeared: $($entry.Kind)" }
        if ($now.Sha256 -ne $entry.Sha256 -or $now.Length -ne $entry.Length -or
            $now.LastWriteTimeUtc -ne $entry.LastWriteTimeUtc) {
            throw "Execution artifact changed: $($entry.Kind)"
        }
    }
    return $current
}

function Read-SerialSafe([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        $stream = $null
        $reader = $null
        try {
            $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
            $reader = New-Object IO.StreamReader($stream)
            $value = [string]$reader.ReadToEnd()
            $reader.Dispose()
            $stream.Dispose()
            return $value
        } catch {
            if ($null -ne $reader) { $reader.Dispose() }
            elseif ($null -ne $stream) { $stream.Dispose() }
            Start-Sleep -Milliseconds 50
        }
    }
    return ''
}

function Write-Event([string]$eventPath, [string]$name, [hashtable]$data = @{}) {
    $record = [ordered]@{ Utc = (Get-Date).ToUniversalTime().ToString('o'); Event = $name }
    foreach ($key in $data.Keys) { $record[$key] = $data[$key] }
    ($record | ConvertTo-Json -Compress -Depth 12) | Add-Content -LiteralPath $eventPath -Encoding utf8
}

function Receive-TcpSerial($stream, [string]$path) {
    if ($null -eq $stream) { return }
    $buffer = New-Object byte[] 4096
    try {
        while ($stream.DataAvailable) {
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -le 0) { break }
            $file = [IO.File]::Open($path, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite)
            try { $file.Write($buffer, 0, $read); $file.Flush() } finally { $file.Dispose() }
        }
    } catch { }
}

function Get-LastGuestMarker([string]$text, [string[]]$markers) {
    $lastName = 'none'
    $lastIndex = -1
    foreach ($marker in $markers) {
        $index = $text.LastIndexOf($marker, [StringComparison]::Ordinal)
        if ($index -gt $lastIndex) { $lastIndex = $index; $lastName = $marker }
    }
    return $lastName
}

function Get-ExitCode($process) {
    try { $process.Refresh(); if ($process.HasExited) { return [int]$process.ExitCode } } catch { }
    return $null
}

$qemuVersion = (& $qemuPath '--version' 2>$null | Select-Object -First 1).Trim()
$artifactSet = @(Get-CurrentArtifactSet)
if (-not [string]::IsNullOrWhiteSpace($ExpectedArtifactSha256) -and
    (($artifactSet | Where-Object Kind -eq 'nativeaot_payload').Sha256 -ne $ExpectedArtifactSha256.ToUpperInvariant())) {
    throw 'NativeAOT payload hash does not match the requested final hash.'
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedLoaderSha256) -and
    (($artifactSet | Where-Object Kind -eq 'efi_loader').Sha256 -ne $ExpectedLoaderSha256.ToUpperInvariant())) {
    throw 'EFI loader hash does not match the requested final hash.'
}

$evidenceId = Split-Path -Leaf $evidence
$manifest = [ordered]@{
    EvidenceId = $evidenceId
    CreatedUtc = (Get-Date).ToUniversalTime().ToString('o')
    RepositoryRoot = $root
    GateDirectory = $gate
    Mode = $Mode
    QemuVersion = $qemuVersion
    Acceleration = $Acceleration
    Artifacts = $artifactSet
}
Write-JsonFile (Join-Path $evidence 'artifact-manifest.json') $manifest
Write-JsonFile (Join-Path $evidence 'validation-context.json') ([ordered]@{
    EvidenceId = $evidenceId
    Mode = $Mode
    RunCount = $RunCount
    TimeoutSeconds = $TimeoutSeconds
    NoProgressSeconds = $NoProgressSeconds
    QemuPath = $qemuPath
    QemuVersion = $qemuVersion
    Acceleration = $Acceleration
    MachineType = $MachineType
    CpuModel = $CpuModel
    StartedUtc = (Get-Date).ToUniversalTime().ToString('o')
})

$knownMarkers = @(
    'GXOS_NET10:LOADER_START', 'GXOS_NET10:PE_IMPORT_FUNCTIONAL=',
    'GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=', 'GXOS_NET10:GC_STARTUP_BEGIN',
    'GXOS_NET10:NATIVEAOT_STARTUP_BEGIN', 'GXOS_NET10:TIME_API_ENTER',
    'GXOS_NET10:FILETIME_CONVERSION_OK=', 'GXOS_NET10:QPC_CALL',
    'GXOS_NET10:QPC_OK=', 'GXOS_NET10:CRT_ONEXIT_INITIALIZED_OK',
    'GXOS_NET10:SLIST_IMPORT_FUNCTIONAL=1', 'GXOS_NET10:SLIST_HEAD_INITIALIZED_OK',
    'GXOS_NET10:UNEXPECTED_IMPORT_CALL:', 'GXOS_NET10:QPC_REGRESSIONS='
)

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    $existingQemu = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue)
    if ($existingQemu.Count -ne 0) { throw "Preexisting QEMU process detected before run $sequence." }
    $runId = "$evidenceId-run$sequence"
    $runDirectory = Join-Path $runsRoot ("run-{0}" -f $sequence)
    New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
    $serial = Join-Path $runDirectory 'serial.log'
    $stdout = Join-Path $runDirectory 'qemu.stdout.log'
    $stderr = Join-Path $runDirectory 'qemu.stderr.log'
    $events = Join-Path $runDirectory 'harness-events.jsonl'
    $vars = Join-Path $runDirectory 'ovmf-vars.fd'
    Copy-Item -LiteralPath $varsTemplate -Destination $vars
    Write-Event $events 'run-prepared' @{ RunId = $runId; Sequence = $sequence; VarsSha256 = (Get-FileHash $vars -Algorithm SHA256).Hash }

    $listener = $null
    $serialClient = $null
    $serialStream = $null
    $acceptTask = $null
    $serialArgument = "file:$serial"
    if ($SerialTransport -eq 'Tcp') {
        $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
        $acceptTask = $listener.AcceptTcpClientAsync()
        $serialArgument = "tcp:127.0.0.1:$port"
        Write-Event $events 'serial-listener-started' @{ RunId = $runId; Port = $port; Transport = 'Tcp' }
    }

    $arguments = @(
        '-machine', $MachineType, '-accel', $Acceleration, '-m', '128M',
        '-drive', "if=pflash,format=raw,readonly=on,file=`"$ovmf`"",
        '-drive', "if=pflash,format=raw,file=`"$vars`"",
        '-drive', 'file="fat:rw:ESP",format=raw,if=ide,index=0,media=disk',
        '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
        '-serial', $serialArgument, '-monitor', 'none', '-display', 'none',
        '-no-reboot', '-no-shutdown'
    )
    if (-not [string]::IsNullOrWhiteSpace($CpuModel)) {
        $arguments = @('-machine', $MachineType, '-accel', $Acceleration, '-cpu', $CpuModel, '-m', '128M') + $arguments[6..($arguments.Count - 1)]
    }
    $process = Start-Process -FilePath $qemuPath -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    try { $process.PriorityClass = [Diagnostics.ProcessPriorityClass]::$ProcessPriority } catch { }
    $startUtc = (Get-Date).ToUniversalTime()
    Write-Event $events 'qemu-started' @{ RunId = $runId; Pid = $process.Id; StartUtc = $startUtc.ToString('o'); Priority = $ProcessPriority; Arguments = ($arguments -join ' ') }
    $deadline = $startUtc.AddSeconds($TimeoutSeconds)
    $lastProgressUtc = $startUtc
    $lastSampleUtc = $startUtc
    $lastLength = 0
    $lastWriteUtc = $null
    $lastMarker = 'none'
    $lastCpuMs = 0.0
    $boundaryObserved = $false
    $summaryObserved = $false
    $timeoutReason = ''
    $killReason = ''
    $processExitedNaturally = $false
    $lastEventMarker = 'none'

    while ([DateTime]::UtcNow -lt $deadline) {
        try { $process.Refresh() } catch { }
        if ($SerialTransport -eq 'Tcp' -and $null -eq $serialClient -and $null -ne $acceptTask -and $acceptTask.IsCompleted) {
            try {
                $serialClient = $acceptTask.Result
                $serialStream = $serialClient.GetStream()
                Write-Event $events 'serial-connected' @{ RunId = $runId; Transport = 'Tcp' }
            } catch { Write-Event $events 'serial-connect-failed' @{ RunId = $runId; Error = $_.Exception.Message } }
        }
        if ($SerialTransport -eq 'Tcp') { Receive-TcpSerial $serialStream $serial }
        $text = if ($DisableLiveSerialPolling) { '' } else { [string](Read-SerialSafe $serial) }
        $length = $text.Length
        $writeUtc = if (Test-Path -LiteralPath $serial) { (Get-Item -LiteralPath $serial).LastWriteTimeUtc } else { $null }
        $marker = Get-LastGuestMarker $text $knownMarkers
        if ($length -ne $lastLength -or $marker -ne $lastMarker) {
            $lastProgressUtc = [DateTime]::UtcNow
            Write-Event $events 'guest-progress' @{ RunId = $runId; Length = $length; LastMarker = $marker; LastFileWriteUtc = if ($null -eq $writeUtc) { $null } else { $writeUtc.ToString('o') } }
            $lastLength = $length
            $lastWriteUtc = $writeUtc
            $lastMarker = $marker
        }
        try { $cpuMs = $process.TotalProcessorTime.TotalMilliseconds } catch { $cpuMs = $lastCpuMs }
        if ([DateTime]::UtcNow -ge $lastSampleUtc.AddSeconds(1)) {
            Write-Event $events 'process-sample' @{ RunId = $runId; Pid = $process.Id; Alive = (-not $process.HasExited); CpuMilliseconds = $cpuMs; SerialLength = $length; LastMarker = $marker }
            $lastSampleUtc = [DateTime]::UtcNow
            $lastCpuMs = $cpuMs
        }
        if ($text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')) { $boundaryObserved = $true }
        if ($text.Contains('GXOS_NET10:QPC_REGRESSIONS=') -and
            $text.Contains('GXOS_NET10:ALLOCATION_CONTEXT_VALID=0')) { $summaryObserved = $true }
        $requiredBoundary = if ($Mode -eq 'Positive') {
            'GXOS_NET10:UNEXPECTED_IMPORT_CALL:api-ms-win-crt-runtime-l1-1-0.dll!_initterm_e'
        } else {
            'GXOS_NET10:UNEXPECTED_IMPORT_CALL:KERNEL32.dll!InitializeSListHead'
        }
        if ($text.Contains($requiredBoundary) -and $summaryObserved) {
            Write-Event $events 'complete-evidence-captured' @{ RunId = $runId; LastMarker = $marker; SerialLength = $length }
            Start-Sleep -Milliseconds 500
            $killReason = 'capture-complete-guest-halts-forever'
            break
        }
        try {
            if ($process.HasExited) { $processExitedNaturally = $true; $timeoutReason = 'process-exited-before-complete-evidence'; break }
        } catch { }
        if ([DateTime]::UtcNow -gt $lastProgressUtc.AddSeconds($NoProgressSeconds)) {
            if ($process.HasExited) { $timeoutReason = 'process-exited-no-progress' }
            elseif ($length -eq $lastLength -and $cpuMs -eq $lastCpuMs) { $timeoutReason = 'qemu-alive-guest-output-and-cpu-stalled' }
            elseif ($length -eq $lastLength) { $timeoutReason = 'qemu-alive-serial-stopped-while-cpu-active' }
            else { $timeoutReason = 'guest-progress-exceeded-no-progress-window' }
            Write-Event $events 'no-progress-timeout' @{ RunId = $runId; Reason = $timeoutReason; LastMarker = $marker; SerialLength = $length; CpuMilliseconds = $cpuMs }
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $summaryObserved -and [string]::IsNullOrWhiteSpace($timeoutReason) -and $lastProgressUtc -le $deadline) { $timeoutReason = 'deadline-reached-before-complete-evidence' }
    try { $process.Refresh() } catch { }
    $aliveBeforeKill = $false
    try { $aliveBeforeKill = -not $process.HasExited } catch { }
    if ($aliveBeforeKill) {
        if ([string]::IsNullOrWhiteSpace($killReason)) { $killReason = 'timeout-or-incomplete-evidence' }
        Write-Event $events 'qemu-stop-requested' @{ RunId = $runId; Pid = $process.Id; Reason = $killReason }
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    } elseif ([string]::IsNullOrWhiteSpace($killReason)) {
        $killReason = 'not-required-process-already-exited'
    }
    try { Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue } catch { }
    if ($SerialTransport -eq 'Tcp') {
        for ($drainAttempt = 1; $drainAttempt -le 10; $drainAttempt++) {
            Receive-TcpSerial $serialStream $serial
            Start-Sleep -Milliseconds 50
        }
        if ($null -ne $serialStream) { $serialStream.Dispose() }
        if ($null -ne $serialClient) { $serialClient.Dispose() }
        if ($null -ne $listener) { $listener.Stop() }
    }
    Start-Sleep -Milliseconds 300
    $cleanupComplete = $null -eq (Get-Process -Id $process.Id -ErrorAction SilentlyContinue)
    if (-not $cleanupComplete) { Write-Event $events 'cleanup-blocked' @{ RunId = $runId; Pid = $process.Id } }
    $endUtc = (Get-Date).ToUniversalTime()
    $finalText = Read-SerialSafe $serial
    $finalItem = if (Test-Path -LiteralPath $serial) { Get-Item -LiteralPath $serial } else { $null }
    $exitCode = Get-ExitCode $process
    $runArtifactSet = @(Compare-ArtifactSet $artifactSet)
    $runData = [ordered]@{
        EvidenceId = $evidenceId
        RunId = $runId
        Sequence = $sequence
        Mode = $Mode
        Pass = $false
        QemuPid = $process.Id
        QemuVersion = $qemuVersion
        QemuStartUtc = $startUtc.ToString('o')
        QemuEndUtc = $endUtc.ToString('o')
        QemuExitCode = $exitCode
        ProcessExitedNaturally = $processExitedNaturally
        CleanupComplete = $cleanupComplete
        TimeoutReason = $timeoutReason
        KillReason = $killReason
        SerialTransport = $SerialTransport
        BoundaryObserved = $boundaryObserved
        SummaryObserved = $summaryObserved
        LastObservedGuestMarker = (Get-LastGuestMarker $finalText $knownMarkers)
        LastFileWriteTimeUtc = if ($null -eq $finalItem) { $null } else { $finalItem.LastWriteTimeUtc.ToString('o') }
        FinalSerialLength = if ($null -eq $finalItem) { 0 } else { [int64]$finalItem.Length }
        SerialLog = (Get-RelativeEvidencePath $serial)
        QemuStdoutLog = (Get-RelativeEvidencePath $stdout)
        QemuStderrLog = (Get-RelativeEvidencePath $stderr)
        HarnessEventLog = (Get-RelativeEvidencePath $events)
        VarsPath = (Get-RelativeEvidencePath $vars)
        ArtifactSetAfterRun = $runArtifactSet
    }
    Write-JsonFile (Join-Path $runDirectory 'run.json') $runData
    Write-Event $events 'run-finalized' @{ RunId = $runId; EndUtc = $endUtc.ToString('o'); ExitCode = $exitCode; CleanupComplete = $cleanupComplete; FinalSerialLength = $runData.FinalSerialLength; LastMarker = $runData.LastObservedGuestMarker }
    if (-not $cleanupComplete) { throw "QEMU cleanup did not complete for $runId." }
}

$context = Get-Content (Join-Path $evidence 'validation-context.json') -Raw | ConvertFrom-Json
$context | Add-Member -NotePropertyName FinishedUtc -NotePropertyValue ((Get-Date).ToUniversalTime().ToString('o'))
Write-JsonFile (Join-Path $evidence 'validation-context.json') $context
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validatorScript -EvidenceRoot $evidence -ExpectedRunCount $RunCount
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "SLIST_EVIDENCE_ROOT=$evidence"
Write-Output 'SLIST_FINAL_VALIDATION=PASSED'

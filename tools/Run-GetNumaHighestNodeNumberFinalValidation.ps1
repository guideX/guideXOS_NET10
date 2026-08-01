[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [ValidateSet('Positive', 'Disabled', 'SuccessExperiment', 'FailureExperiment')] [string]$Mode = 'Positive',
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
$validatorScript = Join-Path $root 'tools\Validate-GetNumaHighestNodeNumberEvidence.ps1'
$runnerScript = [IO.Path]::GetFullPath($PSCommandPath)
$sourceFiles = @(
    (Join-Path $root 'src\Gate4Harness\platform_numa.c'),
    (Join-Path $root 'src\Gate4Harness\platform_numa.h'),
    (Join-Path $root 'src\Gate4Harness\platform_system_info.c'),
    (Join-Path $root 'src\Gate4Harness\platform_system_info.h'),
    (Join-Path $root 'src\Gate4Harness\gate4_loader.c'),
    (Join-Path $root 'tools\Build-Gate4Harness.ps1')
)
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Path) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw 'qemu-system-x86_64.exe is required.' }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
foreach ($path in @($efi,$payload,$startup,$payloadSource,$runtimeArchive,$validatorScript) + $sourceFiles + @($ovmf,$varsTemplate)) { if (-not (Test-Path -LiteralPath $path)) { throw "Required path missing: $path" } }
if (Test-Path -LiteralPath $evidence) { throw "Evidence directory already exists: $evidence" }
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs') | Out-Null
function Snapshot([string]$kind, [string]$path) {
    $item = Get-Item -LiteralPath $path
    [PSCustomObject]@{ Kind=$kind; Path=[IO.Path]::GetFullPath($path); Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant(); Length=[int64]$item.Length }
}
function Write-Json([string]$path, $value) { $value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8 }
function Write-Event([string]$path, [string]$event, [hashtable]$data = @{}) {
    $record = [ordered]@{ Utc=(Get-Date).ToUniversalTime().ToString('o'); Event=$event }
    foreach ($key in $data.Keys) { $record[$key] = $data[$key] }
    ($record | ConvertTo-Json -Compress -Depth 12) | Add-Content -LiteralPath $path -Encoding utf8
}
function Read-Serial([string]$path) { if (-not (Test-Path -LiteralPath $path)) { return '' }; try { return [IO.File]::ReadAllText($path) } catch { return '' } }
function Test-QemuGone([int]$processId) { try { return (Get-Process -Id $processId -ErrorAction Stop).ProcessName -ne 'qemu-system-x86_64' } catch { return $true } }
function Wait-QemuGone([int]$processId) {
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) { if (Test-QemuGone $processId) { return $true }; Start-Sleep -Milliseconds 100 }
    return (Test-QemuGone $processId)
}
$artifacts = @(
    (Snapshot 'efi_loader' $efi), (Snapshot 'nativeaot_payload' $payload), (Snapshot 'nativeaot_payload_source' $payloadSource),
    (Snapshot 'runtime_archive' $runtimeArchive), (Snapshot 'ovmf_code' $ovmf), (Snapshot 'ovmf_vars_template' $varsTemplate),
    (Snapshot 'esp_startup' $startup), (Snapshot 'qemu_executable' $qemu), (Snapshot 'validation_runner' $runnerScript),
    (Snapshot 'evidence_validator' $validatorScript)
)
foreach ($sourceFile in $sourceFiles) { $artifacts += Snapshot 'contract_source' $sourceFile }
$evidenceId = Split-Path -Leaf $evidence
$qemuVersion = (& $qemu '--version' 2>$null | Select-Object -First 1).Trim()
$expectedBoundary = if ($Mode -eq 'Disabled') { 'KERNEL32.dll!GetNumaHighestNodeNumber' } else { 'KERNEL32.dll!GetProcessGroupAffinity' }
$artifactFingerprint = (($artifacts | ForEach-Object { "$($_.Kind)=$($_.Sha256):$($_.Length)" }) -join '|')
Write-Json (Join-Path $evidence 'artifact-manifest.json') ([ordered]@{ EvidenceId=$evidenceId; CreatedUtc=(Get-Date).ToUniversalTime().ToString('o'); RepositoryRoot=$root; GateDirectory=$gate; Mode=$Mode; ExpectedBoundary=$expectedBoundary; QemuVersion=$qemuVersion; Artifacts=$artifacts })
Write-Json (Join-Path $evidence 'validation-context.json') ([ordered]@{ EvidenceId=$evidenceId; Mode=$Mode; RunCount=$RunCount; TimeoutSeconds=$TimeoutSeconds; NoProgressSeconds=$NoProgressSeconds; StartedUtc=(Get-Date).ToUniversalTime().ToString('o') })
function Assert-ArtifactSet($expected, [string]$phase) {
    foreach ($artifact in $expected) {
        if (-not (Test-Path -LiteralPath $artifact.Path)) { throw "Artifact disappeared during phase $($phase): $($artifact.Path)" }
        $item = Get-Item -LiteralPath $artifact.Path
        $hash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($hash -ne $artifact.Sha256 -or [int64]$item.Length -ne [int64]$artifact.Length) { throw "Immutable artifact changed during phase $($phase): $($artifact.Path)" }
    }
}
Assert-ArtifactSet $artifacts 'preflight'
for ($sequence=1; $sequence -le $RunCount; $sequence++) {
    Assert-ArtifactSet $artifacts "before-run-$sequence"
    if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) { throw "Preexisting QEMU process before run $sequence" }
    $runId = "$evidenceId-run$sequence"; $runDirectory = Join-Path $evidence ("runs\run-{0}" -f $sequence); New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
    $serial=Join-Path $runDirectory 'serial.log'; $stdout=Join-Path $runDirectory 'qemu.stdout.log'; $stderr=Join-Path $runDirectory 'qemu.stderr.log'; $events=Join-Path $runDirectory 'harness-events.jsonl'; $vars=Join-Path $runDirectory 'ovmf-vars.fd'; Copy-Item -LiteralPath $varsTemplate -Destination $vars
    Write-Event $events 'run-prepared' @{RunId=$runId;Sequence=$sequence;VarsSha256=(Get-FileHash $vars -Algorithm SHA256).Hash}
    $quote=[char]34
    $args=@('-machine','q35','-accel','tcg,thread=multi','-m','128M','-drive',"if=pflash,format=raw,readonly=on,file=$quote$ovmf$quote",'-drive',"if=pflash,format=raw,file=$quote$vars$quote",'-drive','file="fat:rw:ESP",format=raw,if=ide,index=0,media=disk','-rtc','base=utc,clock=vm','-boot','order=c','-serial',"file:$serial",'-monitor','none','-display','none','-no-reboot','-no-shutdown')
    $process=Start-Process -FilePath $qemu -ArgumentList $args -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
    $qemuPid=[int]$process.Id; $start=(Get-Date).ToUniversalTime(); Write-Event $events 'qemu-started' @{RunId=$runId;Pid=$qemuPid;StartUtc=$start.ToString('o');Arguments=($args -join ' ')}
    $captured=$false; $timeoutReason=''; $lastLength=0; $lastProgress=$start; $deadline=$start.AddSeconds($TimeoutSeconds)
    while ((Get-Date).ToUniversalTime() -lt $deadline) {
        $text=Read-Serial $serial
        if ($text.Length -ne $lastLength) { $lastLength=$text.Length; $lastProgress=(Get-Date).ToUniversalTime(); Write-Event $events 'guest-progress' @{RunId=$runId;Length=$lastLength} }
        $summary=$text.Contains('GXOS_NET10:QPC_REGRESSIONS=') -and $text.Contains('GXOS_NET10:ALLOCATION_CONTEXT_VALID=0')
        $complete=if ($Mode -eq 'Disabled') { $text.Contains('GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE') } elseif ($Mode -eq 'FailureExperiment') { $text.Contains('GXOS_NET10:GETNUMAHIGHESTNODE_FAILED') } else { $text.Contains('GXOS_NET10:GETNUMAHIGHESTNODE_OK') }
        $boundary=$text.Contains("GXOS_NET10:UNEXPECTED_IMPORT_CALL:$expectedBoundary")
        if($summary -and $complete -and $boundary){$captured=$true;Write-Event $events 'complete-evidence-captured' @{RunId=$runId;SerialLength=$text.Length};Start-Sleep -Milliseconds 500;break}
        try{if($process.HasExited){$timeoutReason='qemu-exited-before-complete-evidence';break}}catch{}
        if((Get-Date).ToUniversalTime() -gt $lastProgress.AddSeconds($NoProgressSeconds)){$timeoutReason='guest-no-progress';break}
        Start-Sleep -Milliseconds 100
    }
    try{$process.Refresh()}catch{}; $alive=$false; try{$alive=-not $process.HasExited}catch{}
    if($alive){Write-Event $events 'qemu-stop-requested' @{RunId=$runId;Pid=$qemuPid;Reason=if($captured){'capture-complete'}else{'incomplete-evidence'}};Stop-Process -Id $qemuPid -Force -ErrorAction SilentlyContinue}
    $cleanup=Wait-QemuGone $qemuPid; Start-Sleep -Milliseconds 300; $final=Read-Serial $serial
    $finalSummary=$final.Contains('GXOS_NET10:QPC_REGRESSIONS=') -and $final.Contains('GXOS_NET10:ALLOCATION_CONTEXT_VALID=0')
    $finalComplete=if ($Mode -eq 'Disabled') { $final.Contains('GXOS_NET10:GETSYSTEMINFO_FIELD_CONSUMPTION_COMPLETE') } elseif ($Mode -eq 'FailureExperiment') { $final.Contains('GXOS_NET10:GETNUMAHIGHESTNODE_FAILED') } else { $final.Contains('GXOS_NET10:GETNUMAHIGHESTNODE_OK') }
    $finalBoundary=$final.Contains("GXOS_NET10:UNEXPECTED_IMPORT_CALL:$expectedBoundary")
    if ($finalSummary -and $finalComplete -and $finalBoundary) { $captured=$true; Write-Event $events 'complete-evidence-captured-after-cleanup' @{RunId=$runId;SerialLength=$final.Length} }
    $exit=$null; try{$process.Refresh();if($process.HasExited){$exit=[int]$process.ExitCode}}catch{}
    Write-Json (Join-Path $runDirectory 'run.json') ([ordered]@{EvidenceId=$evidenceId;RunId=$runId;Sequence=$sequence;Mode=$Mode;Pass=$captured -and $cleanup;QemuPid=$qemuPid;QemuVersion=$qemuVersion;QemuStartUtc=$start.ToString('o');QemuEndUtc=(Get-Date).ToUniversalTime().ToString('o');QemuExitCode=$exit;ProcessExitedNaturally=(-not $alive);CleanupComplete=$cleanup;TimeoutReason=$timeoutReason;FinalSerialLength=$final.Length;ArtifactFingerprint=$artifactFingerprint;SerialLog=('runs\run-'+$sequence+'\serial.log');QemuStdoutLog=('runs\run-'+$sequence+'\qemu.stdout.log');QemuStderrLog=('runs\run-'+$sequence+'\qemu.stderr.log');HarnessEventLog=('runs\run-'+$sequence+'\harness-events.jsonl');VarsPath=('runs\run-'+$sequence+'\ovmf-vars.fd')})
    Write-Event $events 'run-finalized' @{RunId=$runId;CleanupComplete=$cleanup;FinalSerialLength=$final.Length}
    if(-not $cleanup){throw "QEMU cleanup failed for $runId"}
    Assert-ArtifactSet $artifacts "after-run-$sequence"
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $validatorScript -EvidenceRoot $evidence -Mode $Mode -ExpectedRunCount $RunCount
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
Write-Output "GETNUMAHIGHESTNODE_EVIDENCE_ROOT=$evidence"
Write-Output 'GETNUMAHIGHESTNODE_FINAL_VALIDATION=PASSED'

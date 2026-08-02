[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$EvidenceDirectory,
    [ValidateSet('SyntheticPointer', 'WrongError')] [string]$Scenario = 'SyntheticPointer',
    [int]$TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$evidence = [IO.Path]::GetFullPath($EvidenceDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
$startup = Join-Path $gate 'ESP\startup.nsh'
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Path) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
foreach ($path in @($efi, $payload, $startup, $qemu, $ovmf, $varsTemplate)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required path missing: $path" }
}
if (Test-Path -LiteralPath $evidence) { throw "Evidence directory already exists: $evidence" }
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) { throw 'Preexisting QEMU process detected.' }
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'runs\run-1') | Out-Null

function Write-Json([string]$path, [object]$value) {
    $value | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding utf8
}
function Read-Serial([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    try { return [IO.File]::ReadAllText($path) } catch { return '' }
}
function Wait-QemuGone([int]$processId) {
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        try { if ((Get-Process -Id $processId -ErrorAction Stop).ProcessName -ne 'qemu-system-x86_64') { return $true } } catch { return $true }
        Start-Sleep -Milliseconds 100
    }
    try { return (Get-Process -Id $processId -ErrorAction Stop).ProcessName -ne 'qemu-system-x86_64' } catch { return $true }
}

$run = Join-Path $evidence 'runs\run-1'
$serial = Join-Path $run 'serial.log'
$stdout = Join-Path $run 'qemu.stdout.log'
$stderr = Join-Path $run 'qemu.stderr.log'
$vars = Join-Path $run 'ovmf-vars.fd'
Copy-Item -LiteralPath $varsTemplate -Destination $vars
$quote = [char]34
$args = @('-machine','q35','-accel','tcg,thread=multi','-m','128M',
    '-drive',"if=pflash,format=raw,readonly=on,file=$quote$ovmf$quote",
    '-drive',"if=pflash,format=raw,file=$quote$vars$quote",
    '-drive','file="fat:rw:ESP",format=raw,if=ide,index=0,media=disk',
    '-rtc','base=utc,clock=vm','-boot','order=c','-serial',"file:$serial",
    '-monitor','none','-display','none','-no-reboot','-no-shutdown')
$runId = "getprocaddress-$($Scenario.ToLowerInvariant())-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
$process = Start-Process -FilePath $qemu -ArgumentList $args -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
$pidValue = [int]$process.Id
$start = (Get-Date).ToUniversalTime()
$lastLength = 0
$lastProgress = $start
$captured = $false
$timeoutReason = ''
$deadline = $start.AddSeconds($TimeoutSeconds)
$required = if ($Scenario -eq 'SyntheticPointer') { 'GXOS_NET10:GETPROCADDRESS_SYNTHETIC_RESULT=1' } else { 'GXOS_NET10:GETPROCADDRESS_WRONG_ERROR_EXPERIMENT=1' }
while ((Get-Date).ToUniversalTime() -lt $deadline) {
    $text = Read-Serial $serial
    if ($text.Length -ne $lastLength) { $lastLength = $text.Length; $lastProgress = (Get-Date).ToUniversalTime() }
    if ($text.Contains($required) -and $text.Contains('GXOS_NET10:GETPROCADDRESS_CALLER_CONSUMPTION_COMPLETE')) {
        $captured = $true
        Start-Sleep -Milliseconds 500
        break
    }
    try { if ($process.HasExited) { $timeoutReason = 'qemu-exited-before-experiment-marker'; break } } catch { }
    if ((Get-Date).ToUniversalTime() -gt $lastProgress.AddSeconds(20)) { $timeoutReason = 'guest-no-progress'; break }
    Start-Sleep -Milliseconds 100
}
try { $process.Refresh() } catch { }
$alive = $false
try { $alive = -not $process.HasExited } catch { }
if ($alive) { Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue }
$cleanup = Wait-QemuGone $pidValue
Start-Sleep -Milliseconds 300
$final = Read-Serial $serial
$captured = $captured -or ($final.Contains($required) -and $final.Contains('GXOS_NET10:GETPROCADDRESS_CALLER_CONSUMPTION_COMPLETE'))
$serialHash = (Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant()
$exitCode = $null
try { $process.Refresh(); if ($process.HasExited) { $exitCode = [int]$process.ExitCode } } catch { }
$pass = $captured -and $cleanup
Write-Json (Join-Path $run 'run.json') ([ordered]@{
    RunId=$runId;Scenario=$Scenario;QemuPid=$pidValue;QemuStartUtc=$start.ToString('o');QemuEndUtc=(Get-Date).ToUniversalTime().ToString('o')
    QemuExitCode=$exitCode;CleanupComplete=$cleanup;Pass=$pass;TimeoutReason=$timeoutReason;FinalSerialLength=[int64](Get-Item -LiteralPath $serial).Length
    SerialSha256=$serialHash;RequiredMarker=$required;SerialLog='runs\run-1\serial.log';QemuStdoutLog='runs\run-1\qemu.stdout.log';QemuStderrLog='runs\run-1\qemu.stderr.log';VarsPath='runs\run-1\ovmf-vars.fd'
})
$artifacts = @($efi, $payload, $startup, $ovmf, $varsTemplate, $qemu) | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    [ordered]@{Path=[IO.Path]::GetFullPath($_);Length=[int64]$item.Length;Sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToUpperInvariant()}
}
Write-Json (Join-Path $evidence 'experiment-manifest.json') ([ordered]@{
    Scenario=$Scenario;EvidenceRoot=$evidence;ExpectedBehavior='Investigation-only; synthetic pointer or deliberately wrong error must never be used as positive contract evidence.'
    Artifacts=$artifacts;Run='runs\run-1\run.json'
})
Write-Json (Join-Path $evidence 'experiment-summary.json') ([ordered]@{
    Scenario=$Scenario;Passed=$pass;RequiredMarkerSeen=$final.Contains($required);CallerConsumptionComplete=$final.Contains('GXOS_NET10:GETPROCADDRESS_CALLER_CONSUMPTION_COMPLETE')
    SyntheticStubCalled=$final.Contains('GXOS_NET10:GETPROCADDRESS_SYNTHETIC_STUB_CALLED=1');WrongErrorMarkerSeen=$final.Contains('GXOS_NET10:GETPROCADDRESS_WRONG_ERROR_EXPERIMENT=1')
    PositiveContractEligible=$false;NextBoundary=([regex]::Matches($final, 'GXOS_NET10:GETPROCADDRESS_NEXT_BOUNDARY=([^\r\n]+)') | Select-Object -Last 1).Groups[1].Value
})
if (-not $pass) { exit 2 }
Write-Output "GETPROCADDRESS_EXPERIMENT=$Scenario"
Write-Output 'GETPROCADDRESS_EXPERIMENT_RESULT=CAPTURED'

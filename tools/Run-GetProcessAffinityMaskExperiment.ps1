[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$GateDirectory,
    [Parameter(Mandatory = $true)] [string]$OutputDirectory,
    [ValidateSet('Positive', 'Failure')] [string]$Scenario = 'Positive',
    [int]$TimeoutSeconds = 120,
    [int]$NoProgressSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = [IO.Path]::GetFullPath($GateDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Experiment output already exists: $output" }
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Path) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
foreach ($path in @($qemu,$ovmf,$varsTemplate,$efi)) { if (-not (Test-Path -LiteralPath $path)) { throw "Required path missing: $path" } }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$serial = Join-Path $output 'serial.log'; $stdout=Join-Path $output 'qemu.stdout.log'; $stderr=Join-Path $output 'qemu.stderr.log'; $vars=Join-Path $output 'ovmf-vars.fd'; Copy-Item -LiteralPath $varsTemplate -Destination $vars
function Read-Serial { if (-not (Test-Path -LiteralPath $serial)) { return '' }; try { return [IO.File]::ReadAllText($serial) } catch { return '' } }
function Wait-Gone([int]$processId) {
    $deadline=(Get-Date).AddSeconds(15)
    while((Get-Date)-lt $deadline){try{if((Get-Process -Id $processId -ErrorAction Stop).ProcessName -ne 'qemu-system-x86_64'){return $true}}catch{return $true};Start-Sleep -Milliseconds 100}
    try{return (Get-Process -Id $processId -ErrorAction Stop).ProcessName -ne 'qemu-system-x86_64'}catch{return $true}
}
$quote=[char]34
$args=@('-machine','q35','-accel','tcg,thread=multi','-m','128M','-drive',"if=pflash,format=raw,readonly=on,file=$quote$ovmf$quote",'-drive',"if=pflash,format=raw,file=$quote$vars$quote",'-drive','file="fat:rw:ESP",format=raw,if=ide,index=0,media=disk','-rtc','base=utc,clock=vm','-boot','order=c','-serial',"file:$serial",'-monitor','none','-display','none','-no-reboot','-no-shutdown')
$start=(Get-Date).ToUniversalTime(); $process=Start-Process -FilePath $qemu -ArgumentList $args -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -WindowStyle Hidden
$processIdValue=[int]$process.Id; $lastLength=0; $lastProgress=$start; $captured=$false; $reason=''; $deadline=$start.AddSeconds($TimeoutSeconds)
while((Get-Date).ToUniversalTime()-lt $deadline){
    $text=Read-Serial
    if($text.Length-ne $lastLength){$lastLength=$text.Length;$lastProgress=(Get-Date).ToUniversalTime()}
    if($text.Contains('GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_CONSUMPTION_COMPLETE') -and $text.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:')){$captured=$true;Start-Sleep -Milliseconds 500;break}
    try{if($process.HasExited){$reason='qemu-exited-before-experiment-complete';break}}catch{}
    if((Get-Date).ToUniversalTime()-gt $lastProgress.AddSeconds($NoProgressSeconds)){$reason='guest-no-progress';break}
    Start-Sleep -Milliseconds 100
}
try{$process.Refresh()}catch{}; $alive=$false; try{$alive=-not $process.HasExited}catch{}
if($alive){Stop-Process -Id $processIdValue -Force -ErrorAction SilentlyContinue}
$cleanup=Wait-Gone $processIdValue; Start-Sleep -Milliseconds 300; $final=Read-Serial
$captured = $captured -or ($final.Contains('GXOS_NET10:GETPROCESSAFFINITYMASK_CALLER_CONSUMPTION_COMPLETE') -and $final.Contains('GXOS_NET10:UNEXPECTED_IMPORT_CALL:'))
$boundary=[regex]::Match($final,'GXOS_NET10:UNEXPECTED_IMPORT_CALL:([^\r\n]+)').Groups[1].Value
$returned=([regex]::Matches($final,'GXOS_NET10:GETPROCESSAFFINITYMASK_RETURNED')).Count
$failed=([regex]::Matches($final,'GXOS_NET10:GETPROCESSAFFINITYMASK_FAILED')).Count
$success=([regex]::Matches($final,'GXOS_NET10:GETPROCESSAFFINITYMASK_OK')).Count
$exit=$null;try{$process.Refresh();if($process.HasExited){$exit=[int]$process.ExitCode}}catch{}
$result=[ordered]@{Scenario=$Scenario;GateDirectory=$gate;QemuPid=$processIdValue;QemuStartUtc=$start.ToString('o');QemuExitCode=$exit;CleanupComplete=$cleanup;Captured=$captured;TimeoutReason=$reason;SerialLength=$final.Length;SerialSha256=(Get-FileHash -LiteralPath $serial -Algorithm SHA256).Hash.ToUpperInvariant();AffinityReturnedCount=$returned;AffinitySuccessMarkerCount=$success;AffinityFailureMarkerCount=$failed;NextBoundary=$boundary;SerialPath=$serial}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'experiment.json') -Encoding utf8
$result | ConvertTo-Json -Depth 8
if(-not $captured -or -not $cleanup){exit 2}
Write-Output 'GETPROCESSAFFINITYMASK_EXPERIMENT=PASSED'

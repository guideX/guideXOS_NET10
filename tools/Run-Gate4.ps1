[CmdletBinding()]
param(
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 15,
    [string]$GateDirectory = '',
    [string]$OvmfPath = '',
    [string]$OvmfVarsTemplate = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($GateDirectory)) {
    $GateDirectory = Join-Path $root 'artifacts\gate4'
}
$GateDirectory = [IO.Path]::GetFullPath($GateDirectory)
$espDirectory = Join-Path $GateDirectory 'ESP'
if (-not (Test-Path -LiteralPath (Join-Path $espDirectory 'EFI\BOOT\BOOTX64.EFI'))) {
    throw 'Build the Gate 4 harness first with tools\Build-Gate4Harness.ps1.'
}
$qemu = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
if (-not $qemu) {
    $candidate = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
    if (-not (Test-Path -LiteralPath $candidate)) { throw 'qemu-system-x86_64.exe is required.' }
    $qemuPath = $candidate
} else {
    $qemuPath = $qemu.Source
}

$qemuShare = Join-Path (Split-Path -Parent $qemuPath) 'share'
if ([string]::IsNullOrWhiteSpace($OvmfPath)) {
    $OvmfPath = Join-Path $qemuShare 'edk2-x86_64-code.fd'
}
if ([string]::IsNullOrWhiteSpace($OvmfVarsTemplate)) {
    $OvmfVarsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
}
if (-not (Test-Path -LiteralPath $OvmfPath)) { throw "OVMF firmware not found: $OvmfPath" }
if (-not (Test-Path -LiteralPath $OvmfVarsTemplate)) { throw "OVMF variable template not found: $OvmfVarsTemplate" }

$runRoot = Join-Path $GateDirectory ('runs-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$codePath = Join-Path $runRoot 'edk2-x86_64-code.fd'
Copy-Item -LiteralPath $OvmfPath -Destination $codePath -Force
$qemuVersion = (& $qemuPath '--version' 2>$null | Select-Object -First 1).Trim()
$allBlocked = $true

function Read-SerialLog([string]$Path)
{
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            if (-not (Test-Path -LiteralPath $Path)) { return '' }
            return [IO.File]::ReadAllText($Path)
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 100
        }
    }
    throw "Serial log remained locked after process termination: $Path"
}

for ($i = 1; $i -le $RunCount; $i++) {
    $runId = 'gate4-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-run' + $i
    $serialLog = Join-Path $runRoot ($runId + '.serial.log')
    $stdoutLog = Join-Path $runRoot ($runId + '.stdout.log')
    $stderrLog = Join-Path $runRoot ($runId + '.stderr.log')
    $varsPath = Join-Path $runRoot ($runId + '.vars.fd')
    Copy-Item -LiteralPath $OvmfVarsTemplate -Destination $varsPath
    $arguments = @(
        '-machine', 'q35', '-m', '128M',
        '-drive', "if=pflash,format=raw,readonly=on,file=$codePath",
        '-drive', "if=pflash,format=raw,file=$varsPath",
        '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
        '-boot', 'order=c', '-serial', "file:$serialLog",
        '-monitor', 'none', '-display', 'none', '-no-reboot', '-no-shutdown'
    )
    $process = Start-Process -FilePath $qemuPath -ArgumentList $arguments -WorkingDirectory $GateDirectory -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
    $completed = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force
        Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 250
        $classification = 'TIMEOUT'
    } else {
        $classification = 'EXITED'
    }
    $serial = Read-SerialLog $serialLog
    if ($null -eq $serial) { $serial = '' }
    $managed = $serial.Contains('GXOS_NET10:MANAGED_ENTRY_OK')
    $blocked = $serial.Contains('GXOS_NET10:GATE4_BLOCKED_IMPORTS')
    if ($managed -or -not $blocked) { $allBlocked = $false }
    [PSCustomObject]@{
        RunId = $runId
        Classification = if ($managed) { 'UNEXPECTED_MANAGED_MARKER' } elseif ($blocked) { 'BLOCKED_IMPORTS_CONFIRMED' } else { $classification }
        QemuVersion = $qemuVersion
        FirmwareSha256 = (Get-FileHash $codePath -Algorithm SHA256).Hash
        ArtifactSha256 = (Get-FileHash (Join-Path $espDirectory 'GXOS\gxos-managed-entry-probe.dll') -Algorithm SHA256).Hash
        LoaderSha256 = (Get-FileHash (Join-Path $espDirectory 'EFI\BOOT\BOOTX64.EFI') -Algorithm SHA256).Hash
        SerialLog = $serialLog
        Serial = $serial.Trim()
    } | ConvertTo-Json -Compress
}

if ($allBlocked) {
    Write-Output 'GATE4_PROOF=NOT_PASSED'
    Write-Output 'GATE4_RESULT=BLOCKED_IMPORTS'
    exit 2
}
Write-Output 'GATE4_PROOF=UNEXPECTED_RESULT'
exit 1

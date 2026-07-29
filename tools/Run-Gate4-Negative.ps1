[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$GateDirectory,
    [Parameter(Mandatory = $true)]
    [string[]]$ExpectedPresent,
    [string[]]$ExpectedAbsent = @('GXOS_NET10:MANAGED_ENTRY_OK'),
    [int]$TimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
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
$qemuShare = Join-Path (Split-Path -Parent $qemuPath) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
$runRoot = Join-Path $gate ('negative-run-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$code = Join-Path $runRoot 'edk2-x86_64-code.fd'
$vars = Join-Path $runRoot 'ovmf-vars.fd'
$serial = Join-Path $runRoot 'serial.log'
$stdout = Join-Path $runRoot 'stdout.log'
$stderr = Join-Path $runRoot 'stderr.log'
Copy-Item -LiteralPath $ovmf -Destination $code
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
$completed = $process.WaitForExit($TimeoutSeconds * 1000)
if (-not $completed) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
    $classification = 'TIMEOUT'
} else { $classification = 'EXITED' }
Start-Sleep -Milliseconds 250
$serialText = if (Test-Path -LiteralPath $serial) { [IO.File]::ReadAllText($serial) } else { '' }
$present = @($ExpectedPresent | Where-Object { -not $serialText.Contains($_) })
$absent = @($ExpectedAbsent | Where-Object { $serialText.Contains($_) })
$pass = ($present.Count -eq 0) -and ($absent.Count -eq 0)
[PSCustomObject]@{
    Pass = $pass
    Classification = $classification
    QemuVersion = (& $qemuPath '--version' 2>$null | Select-Object -First 1).Trim()
    FirmwareSha256 = (Get-FileHash $code -Algorithm SHA256).Hash
    ArtifactSha256 = (Get-FileHash $payload -Algorithm SHA256).Hash
    LoaderSha256 = (Get-FileHash $efi -Algorithm SHA256).Hash
    SerialLog = $serial
    MissingExpected = $present
    UnexpectedPresent = $absent
    Serial = $serialText.Trim()
} | ConvertTo-Json -Compress
if (-not $pass) { exit 2 }
exit 0

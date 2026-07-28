[CmdletBinding()]
param(
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 15,
    [string]$GateDirectory = '',
    [string]$OvmfPath = '',
    [string]$OvmfVarsTemplate = '',
    [string]$ExpectedArtifactSha256 = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837',
    [string]$ExpectedLoaderSha256 = '',
    [int]$ExpectedImportDescriptors = 10,
    [int]$ExpectedImportSymbols = 124,
    [int]$ExpectedFunctionalImports = 18,
    [int]$ExpectedFailfastImports = 106
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
$allPassed = $true

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
    $before = $serial.IndexOf('GXOS_NET10:BEFORE_MANAGED_CALL', [StringComparison]::Ordinal)
    $managedIndex = $serial.IndexOf('GXOS_NET10:MANAGED_ENTRY_OK', [StringComparison]::Ordinal)
    $after = $serial.IndexOf('GXOS_NET10:AFTER_MANAGED_RETURN=0x0000000000000000', [StringComparison]::Ordinal)
    $complete = $serial.IndexOf('GXOS_NET10:MANAGED_ENTRY_COMPLETE', [StringComparison]::Ordinal)
    $resolved = $serial.Contains('GXOS_NET10:UNRESOLVED_REQUIRED_IMPORTS=0')
    $stackAligned = $serial.Contains('GXOS_NET10:STACK_RSP_MOD16=0')
    $imports = $serial.Contains("GXOS_NET10:PE_IMPORT_DESCRIPTORS=$ExpectedImportDescriptors") -and
        $serial.Contains("GXOS_NET10:PE_IMPORT_SYMBOLS=$ExpectedImportSymbols") -and
        $serial.Contains("GXOS_NET10:PE_IMPORT_FUNCTIONAL=$ExpectedFunctionalImports") -and
        $serial.Contains("GXOS_NET10:PE_IMPORT_FAILFAST=$ExpectedFailfastImports")
    $fault = $serial.Contains('GXOS_NET10:FAULT_')
    $artifactHash = (Get-FileHash (Join-Path $espDirectory 'GXOS\gxos-managed-entry-probe.dll') -Algorithm SHA256).Hash
    $loaderHash = (Get-FileHash (Join-Path $espDirectory 'EFI\BOOT\BOOTX64.EFI') -Algorithm SHA256).Hash
    $runPass = $managed -and $resolved -and $imports -and $stackAligned -and $before -ge 0 -and $managedIndex -gt $before -and $after -gt $managedIndex -and $complete -gt $after -and -not $fault -and $artifactHash -eq $ExpectedArtifactSha256
    if (-not [string]::IsNullOrWhiteSpace($ExpectedLoaderSha256)) { $runPass = $runPass -and $loaderHash -eq $ExpectedLoaderSha256 }
    if (-not $runPass) { $allPassed = $false }
    [PSCustomObject]@{
        RunId = $runId
        Classification = if ($runPass) { 'MANAGED_ENTRY_PASS' } elseif ($managed) { 'MANAGED_MARKER_WITH_FAILED_PROOF' } else { $classification }
        Pass = $runPass
        QemuVersion = $qemuVersion
        FirmwareSha256 = (Get-FileHash $codePath -Algorithm SHA256).Hash
        ArtifactSha256 = $artifactHash
        LoaderSha256 = $loaderHash
        SerialLog = $serialLog
        Serial = $serial.Trim()
    } | ConvertTo-Json -Compress
}

if ($allPassed) {
    Write-Output 'GATE4_PROOF=PASSED'
    Write-Output 'GATE4_RESULT=MANAGED_ENTRY'
    exit 0
}
Write-Output 'GATE4_PROOF=NOT_PASSED'
Write-Output 'GATE4_RESULT=MANAGED_ENTRY_VALIDATION_FAILED'
exit 2

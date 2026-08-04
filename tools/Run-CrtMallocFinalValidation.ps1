[CmdletBinding()]
param(
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 30,
    [string]$GateDirectory = '',
    [string]$OvmfPath = '',
    [string]$OvmfVarsTemplate = '',
    [string]$ExpectedPayloadSha256 = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($GateDirectory)) {
    $GateDirectory = Join-Path $root 'artifacts\malloc-build'
}
$GateDirectory = [IO.Path]::GetFullPath($GateDirectory)
$espDirectory = Join-Path $GateDirectory 'ESP'
$efiPath = Join-Path $espDirectory 'EFI\BOOT\BOOTX64.EFI'
$payloadPath = Join-Path $espDirectory 'GXOS\gxos-managed-entry-probe.dll'
if (-not (Test-Path -LiteralPath $efiPath)) { throw "Malloc harness not found: $efiPath" }
if (-not (Test-Path -LiteralPath $payloadPath)) { throw "Payload not found: $payloadPath" }

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
if ($qemuCommand) {
    $qemuPath = $qemuCommand.Source
} else {
    $qemuPath = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
    if (-not (Test-Path -LiteralPath $qemuPath)) { throw 'qemu-system-x86_64.exe is required.' }
}
$qemuShare = Join-Path (Split-Path -Parent $qemuPath) 'share'
if ([string]::IsNullOrWhiteSpace($OvmfPath)) {
    $OvmfPath = Join-Path $qemuShare 'edk2-x86_64-code.fd'
}
if ([string]::IsNullOrWhiteSpace($OvmfVarsTemplate)) {
    $OvmfVarsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
}
if (-not (Test-Path -LiteralPath $OvmfPath)) { throw "OVMF code not found: $OvmfPath" }
if (-not (Test-Path -LiteralPath $OvmfVarsTemplate)) { throw "OVMF vars not found: $OvmfVarsTemplate" }

function Read-Serial([string]$path) {
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            return [IO.File]::ReadAllText($path)
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 100
        }
    }
    throw "Serial log remained locked: $path"
}

function Get-HexValues([string]$serial, [string]$prefix) {
    $values = @()
    foreach ($line in ($serial -split "`r?`n")) {
        if ($line.StartsWith($prefix, [StringComparison]::Ordinal)) {
            $text = $line.Substring($prefix.Length)
            $values += [Convert]::ToUInt64($text, 16)
        }
    }
    return $values
}

function Get-TextValues([string]$serial, [string]$prefix) {
    $values = @()
    foreach ($line in ($serial -split "`r?`n")) {
        if ($line.StartsWith($prefix, [StringComparison]::Ordinal)) {
            $values += $line.Substring($prefix.Length)
        }
    }
    return $values
}

$payloadHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash
if ($payloadHash -ne $ExpectedPayloadSha256) {
    throw "Payload hash mismatch before QEMU: $payloadHash"
}

$runRoot = Join-Path $GateDirectory ('runs-malloc-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$codePath = Join-Path $runRoot 'edk2-x86_64-code.fd'
Copy-Item -LiteralPath $OvmfPath -Destination $codePath -Force
$runResults = @()
$allPassed = $true

for ($run = 1; $run -le $RunCount; $run++) {
    $runName = 'run-' + $run
    $serialPath = Join-Path $runRoot ($runName + '.serial.log')
    $stdoutPath = Join-Path $runRoot ($runName + '.stdout.log')
    $stderrPath = Join-Path $runRoot ($runName + '.stderr.log')
    $varsPath = Join-Path $runRoot ($runName + '.vars.fd')
    Copy-Item -LiteralPath $OvmfVarsTemplate -Destination $varsPath -Force
    $arguments = @(
        '-machine', 'q35', '-m', '128M',
        '-drive', "if=pflash,format=raw,readonly=on,file=$codePath",
        '-drive', "if=pflash,format=raw,file=$varsPath",
        '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
        '-rtc', 'base=utc,clock=vm',
        '-boot', 'order=c', '-serial', "file:$serialPath",
        '-monitor', 'none', '-display', 'none', '-no-reboot', '-no-shutdown'
    )
    $process = Start-Process -FilePath $qemuPath -ArgumentList $arguments `
        -WorkingDirectory $GateDirectory -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath -WindowStyle Hidden -PassThru
    try {
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
        }
    } finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
        }
    }

    $serial = Read-Serial $serialPath
    $sizes = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_REQUESTED_SIZE=0x')
    $pointers = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_RETURNED_POINTER=0x')
    $returns = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_RETURN_VALUE=0x')
    $alignments = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_ALIGNMENT_MOD8=0x')
    $slots = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_REGISTRY_SLOT=0x')
    $liveAfter = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_LIVE_COUNT_AFTER=0x')
    $totals = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_TOTAL_REQUESTED_BYTES=0x')
    $next = @(Get-TextValues $serial 'GXOS_NET10:MALLOC_NEXT_UNRESOLVED_IMPORT=')
    $callnewh = @(Get-HexValues $serial 'GXOS_NET10:MALLOC_CALLNEWH_REACHED=0x')
    $hash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash
    $runPass = $hash -eq $ExpectedPayloadSha256 -and
        $serial.Contains('GXOS_NET10:MALLOC_CONTEXT_VALID=1') -and
        $serial.Contains('GXOS_NET10:MALLOC_IMPORT_MODULE=api-ms-win-crt-heap-l1-1-0.dll') -and
        $serial.Contains('GXOS_NET10:MALLOC_IMPORT_SYMBOL=malloc') -and
        $serial.Contains('GXOS_NET10:MALLOC_HIDDEN_HEADER=0') -and
        $serial.Contains('GXOS_NET10:MALLOC_ZEROING=0') -and
        $sizes.Count -gt 0 -and $sizes[0] -eq 88 -and
        $pointers.Count -eq $sizes.Count -and
        $returns.Count -eq $sizes.Count -and
        $alignments.Count -eq $sizes.Count -and
        $slots.Count -eq $sizes.Count -and
        $liveAfter.Count -eq $sizes.Count -and
        (@($pointers | Where-Object { $_ -eq 0 }).Count -eq 0) -and
        (@($returns | Where-Object { $_ -eq 0 }).Count -eq 0) -and
        (@($alignments | Where-Object { $_ -ne 0 }).Count -eq 0) -and
        (@($slots | Select-Object -Unique).Count -eq $slots.Count) -and
        (@($liveAfter | Where-Object { $_ -eq 0 }).Count -eq 0) -and
        $callnewh.Count -ge 1 -and $callnewh[-1] -eq 0 -and
        $next.Count -ge 1 -and $next[-1] -eq 'KERNEL32.dll!AddVectoredExceptionHandler'
    if (-not $runPass) { $allPassed = $false }
    $runResults += [PSCustomObject]@{
        Run = $runName
        Pass = $runPass
        PayloadSha256 = $hash
        InvocationCount = $sizes.Count
        Sizes = ($sizes -join ',')
        ReturnedPointers = (($pointers | ForEach-Object { '0x{0:X16}' -f $_ }) -join ',')
        AlignmentsMod8 = ($alignments -join ',')
        RegistrySlots = ($slots -join ',')
        LiveCountsAfter = ($liveAfter -join ',')
        TotalRequestedBytes = if ($totals.Count -gt 0) { $totals[-1] } else { 0 }
        CallnewhReached = if ($callnewh.Count -gt 0) { $callnewh[-1] } else { 0 }
        NextUnresolvedImport = if ($next.Count -gt 0) { $next[-1] } else { '' }
        SerialLog = $serialPath
    }
}

if ($runResults.Count -gt 1) {
    $reference = $runResults[0].Sizes
    foreach ($result in $runResults) {
        if ($result.Sizes -ne $reference) { $allPassed = $false }
    }
}
$runResults | ConvertTo-Json -Depth 4
if ($allPassed) {
    Write-Output 'CRT_MALLOC_QEMU_PROOF=PASSED'
    exit 0
}
Write-Output 'CRT_MALLOC_QEMU_PROOF=NOT_PASSED'
exit 2

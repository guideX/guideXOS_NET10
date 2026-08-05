[CmdletBinding()]
param(
    [ValidateSet('Enabled', 'Disabled')]
    [string]$Mode = 'Enabled',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 25,
    [string]$GateDirectory = '',
    [string]$ExpectedPayloadSha256 = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($GateDirectory)) {
    $GateDirectory = Join-Path $root 'artifacts\veh-ordinary-gate'
}
$gate = [IO.Path]::GetFullPath($GateDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
if (-not (Test-Path -LiteralPath $efi)) { throw "Harness not found: $efi" }
if (-not (Test-Path -LiteralPath $payload)) { throw "Payload not found: $payload" }
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash -ne $ExpectedPayloadSha256) {
    throw 'Payload hash mismatch before ordinary validation.'
}
$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($qemuCommand) { $qemuCommand.Source } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw 'qemu-system-x86_64.exe is required.' }
$share = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf)) { throw "OVMF code not found: $ovmf" }
if (-not (Test-Path -LiteralPath $varsTemplate)) { throw "OVMF vars not found: $varsTemplate" }

function Read-Serial([string]$path) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try { if (Test-Path -LiteralPath $path) { return [IO.File]::ReadAllText($path) } return '' }
        catch [IO.IOException] { Start-Sleep -Milliseconds 100 }
    }
    throw "Serial log remained locked: $path"
}

function Get-HexValue([string]$text, [string]$prefix) {
    $match = [regex]::Match($text, [regex]::Escape($prefix) + '([0-9A-Fa-f]+)')
    if (-not $match.Success) { return $null }
    return [Convert]::ToUInt64($match.Groups[1].Value, 16)
}

$runRoot = Join-Path $gate ('veh-validation-' + $Mode.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$codePath = Join-Path $runRoot 'edk2-x86_64-code.fd'
Copy-Item -LiteralPath $ovmf -Destination $codePath
$allPassed = $true

for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
    for ($cleanupAttempt = 1; $cleanupAttempt -le 30; $cleanupAttempt++) {
        if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
        throw "Preexisting QEMU process before run $sequence"
    }
    $runId = "run-$sequence"
    $serial = Join-Path $runRoot "$runId.serial.log"
    $stdout = Join-Path $runRoot "$runId.stdout.log"
    $stderr = Join-Path $runRoot "$runId.stderr.log"
    $vars = Join-Path $runRoot "$runId.vars.fd"
    Copy-Item -LiteralPath $varsTemplate -Destination $vars
    $arguments = @(
        '-machine', 'q35', '-m', '128M',
        '-drive', "if=pflash,format=raw,readonly=on,file=$codePath",
        '-drive', "if=pflash,format=raw,file=$vars",
        '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
        '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
        '-serial', "file:$serial", '-monitor', 'none', '-display', 'none',
        '-no-reboot', '-no-shutdown'
    )
    $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
        }
    } finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
        }
    }
    for ($cleanupAttempt = 1; $cleanupAttempt -le 30; $cleanupAttempt++) {
        if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) { break }
        Start-Sleep -Milliseconds 100
    }
    $text = Read-Serial $serial
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash
    $nextMatch = [regex]::Match($text, 'GXOS_NET10:MALLOC_NEXT_UNRESOLVED_IMPORT=([^\r\n]+)')
    $handle = Get-HexValue $text 'GXOS_NET10:VEH_ADD_HANDLE=0x'
    $addInvocation = Get-HexValue $text 'GXOS_NET10:VEH_ADD_INVOCATION=0x'
    $rva = Get-HexValue $text 'GXOS_NET10:VEH_ADD_CALLBACK_RVA=0x'
    $live = Get-HexValue $text 'GXOS_NET10:VEH_REGISTRY_LIVE_COUNT=0x'
    $sizeMatches = [regex]::Matches($text, 'GXOS_NET10:MALLOC_REQUESTED_SIZE=0x([0-9A-Fa-f]+)')
    $sizes = @($sizeMatches | ForEach-Object { [Convert]::ToUInt64($_.Groups[1].Value, 16) })
    $required = @(
        'GXOS_NET10:REGISTER_ONEXIT_STATUS=OK',
        'GXOS_NET10:GETMODULEHANDLEEX_OK',
        'GXOS_NET10:MALLOC_CONTEXT_VALID=1',
        'GXOS_NET10:MALLOC_CALLNEWH_REACHED=0x0000000000000000',
        'GXOS_NET10:VEH_IMPORT_DLL=KERNEL32.dll',
        'GXOS_NET10:VEH_IMPORT_SYMBOL=AddVectoredExceptionHandler',
        'GXOS_NET10:VEH_IMPORT_DESCRIPTOR_INDEX=0x0000000000000002',
        'GXOS_NET10:VEH_IMPORT_SYMBOL_INDEX=0x000000000000001E',
        'GXOS_NET10:VEH_IMPORT_IAT_RVA=0x000000000007D128',
        'GXOS_NET10:VEH_ADD_FIRST=0x0000000000000001',
        'GXOS_NET10:VEH_ADD_RETURN_ADDRESS_RVA=0x0000000000037D06',
        'GXOS_NET10:VEH_ADD_CALL_SITE_RVA=0x0000000000037D00',
        'GXOS_NET10:VEH_ADD_INVOCATION=0x0000000000000001',
        'GXOS_NET10:VEH_ADD_CALLBACK_RVA=0x0000000000033CF0',
        'GXOS_NET10:VEH_ADD_CALLBACK_SECTION=.text',
        'GXOS_NET10:VEH_ADD_CALLBACK_SECTION_EXECUTABLE=0x0000000000000001',
        'GXOS_NET10:VEH_ADD_RESULT=SUCCESS',
        'GXOS_NET10:VEH_ADD_ORDER_AFTER=0000000000000000',
        'GXOS_NET10:VEH_REGISTRY_ALLOCATION_COUNT=0x0000000000000000'
    )
    $missing = @()
    if ($Mode -eq 'Enabled') {
        $missing = @($required | Where-Object { -not $text.Contains($_) })
    }
    $disabledPass = $Mode -eq 'Disabled' -and
        $text.Contains('GXOS_NET10:MALLOC_NEXT_UNRESOLVED_IMPORT=KERNEL32.dll!AddVectoredExceptionHandler') -and
        -not $text.Contains('GXOS_NET10:VEH_ADD_INVOCATION=')
    $enabledPass = $Mode -eq 'Enabled' -and $nextMatch.Success -and
        $nextMatch.Groups[1].Value -ne 'KERNEL32.dll!AddVectoredExceptionHandler' -and
        $addInvocation -eq 1 -and $handle -ne $null -and $handle -ne 0 -and $rva -eq 0x33CF0 -and $live -eq 1 -and
        $sizes.Count -eq 3 -and $sizes[0] -eq 0x58 -and $sizes[1] -eq 0x48 -and
        $sizes[2] -eq 0x38 -and -not $text.Contains('GXOS_NET10:EXCEPTION_HANDLER_')
    $pass = $hash -eq $ExpectedPayloadSha256 -and (($Mode -eq 'Enabled' -and $missing.Count -eq 0 -and $enabledPass) -or $disabledPass)
    if (-not $pass) { $allPassed = $false }
    [PSCustomObject]@{
        Run=$runId; Mode=$Mode; Pass=$pass; PayloadSha256=$hash; Missing=$missing
        MallocSizes=($sizes -join ','); CallbackRva=$rva; Handle=$handle; RegistryLiveCount=$live
        NextBlocker=if ($nextMatch.Success) { $nextMatch.Groups[1].Value } else { '' }
        NaturalPayloadHandlerInvocationCount=if ($text.Contains('GXOS_NET10:EXCEPTION_HANDLER_')) { 'nonzero' } else { '0' }
        SerialLog=$serial
    } | ConvertTo-Json -Compress
}

for ($cleanupAttempt = 1; $cleanupAttempt -le 30; $cleanupAttempt++) {
    if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -eq 0) { break }
    Start-Sleep -Milliseconds 100
}
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'QEMU process remains after validation.'
}
if ($allPassed) { "VEH_ORDINARY_$($Mode.ToUpperInvariant())=PASSED"; exit 0 }
"VEH_ORDINARY_$($Mode.ToUpperInvariant())=NOT_PASSED"; exit 2

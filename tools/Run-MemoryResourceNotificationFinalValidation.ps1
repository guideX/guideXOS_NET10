[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EnabledBuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$DisabledBuildDirectory,
    [string]$PayloadPath = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $root 'artifacts\veh-final3-normal-gate\ESP\GXOS\gxos-managed-entry-probe.dll'
}
$payloadPath = [IO.Path]::GetFullPath($PayloadPath)
$enabled = [IO.Path]::GetFullPath($EnabledBuildDirectory)
$disabled = [IO.Path]::GetFullPath($DisabledBuildDirectory)
$expectedHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'

if ($RunCount -lt 3) { throw 'At least three enabled QEMU runs are required.' }
if (-not (Test-Path -LiteralPath $payloadPath)) { throw "Payload not found: $payloadPath" }
$sourceHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($sourceHash -ne $expectedHash) { throw "Exact payload hash mismatch: $sourceHash" }

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw "QEMU not found: $qemu" }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf) -or -not (Test-Path -LiteralPath $varsTemplate)) {
    throw "OVMF files not found under $qemuShare"
}

$runRoot = Join-Path $root ('artifacts\memory-resource-notification-runs-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$taskQemuIds = [Collections.Generic.List[int]]::new()
$unrelatedQemuIds = [Collections.Generic.HashSet[int]]::new()

function Get-QemuIds {
    @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Id)
}

function Assert-Marker([string]$Serial, [string]$Marker) {
    if (-not $Serial.Contains($Marker)) { throw "Missing marker: $Marker" }
}

function Get-HexMarker([string]$Serial, [string]$Name) {
    $match = [regex]::Match($Serial, [regex]::Escape($Name) + '0x([0-9A-Fa-f]+)')
    if (-not $match.Success) { throw "Missing hex marker: $Name" }
    return [UInt64]::Parse($match.Groups[1].Value,
        [Globalization.NumberStyles]::AllowHexSpecifier)
}

function Get-TextMarker([string]$Serial, [string]$Name) {
    $match = [regex]::Match($Serial, [regex]::Escape($Name) + '([^\r\n]+)')
    if (-not $match.Success) { throw "Missing text marker: $Name" }
    return $match.Groups[1].Value
}

function Read-Serial([string]$Path) {
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        try {
            if (-not (Test-Path -LiteralPath $Path)) { return '' }
            return [IO.File]::ReadAllText($Path)
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 100
        }
    }
    throw "Serial log remained locked: $Path"
}

function Invoke-PayloadRun([string]$BuildDirectory, [string]$Label, [int]$Sequence) {
    $esp = Join-Path $BuildDirectory 'ESP'
    $efi = Join-Path $esp 'EFI\BOOT\BOOTX64.EFI'
    $builtPayload = Join-Path $esp 'GXOS\gxos-managed-entry-probe.dll'
    if (-not (Test-Path -LiteralPath $efi) -or -not (Test-Path -LiteralPath $builtPayload)) {
        throw "Harness or payload missing in $BuildDirectory"
    }

    $runDirectory = Join-Path $runRoot ("$Label-run-$Sequence")
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $code = Join-Path $runDirectory 'edk2-code.fd'
    $vars = Join-Path $runDirectory 'edk2-vars.fd'
    $serial = Join-Path $runDirectory 'serial.log'
    $stdout = Join-Path $runDirectory 'qemu.stdout.log'
    $stderr = Join-Path $runDirectory 'qemu.stderr.log'

    $runSourceHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $runBuiltHash = (Get-FileHash -LiteralPath $builtPayload -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($runSourceHash -ne $expectedHash -or $runBuiltHash -ne $expectedHash) {
        throw "Exact payload hash mismatch before $Label run ${Sequence}: source=$runSourceHash built=$runBuiltHash"
    }
    Copy-Item -LiteralPath $ovmf -Destination $code
    Copy-Item -LiteralPath $varsTemplate -Destination $vars

    $beforeIds = Get-QemuIds
    foreach ($id in $beforeIds) { [void]$unrelatedQemuIds.Add([int]$id) }
    $arguments = @(
        '-machine', 'q35', '-m', '128M',
        '-drive', "if=pflash,format=raw,readonly=on,file=$code",
        '-drive', "if=pflash,format=raw,file=$vars",
        '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
        '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
        '-serial', "file:$serial", '-monitor', 'none', '-display', 'none',
        '-no-reboot', '-no-shutdown'
    )
    $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $BuildDirectory `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
    [void]$taskQemuIds.Add([int]$process.Id)
    $completed = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 250
    if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "Task QEMU run did not stop: $($process.Id)"
    }
    $serialText = Read-Serial $serial
    [PSCustomObject]@{
        Label = $Label
        Sequence = $Sequence
        Classification = if ($completed) { 'EXITED' } else { 'TIMEOUT_STOPPED' }
        PayloadSha256 = $runSourceHash
        BuiltPayloadSha256 = $runBuiltHash
        PayloadBase = ('0x{0:X}' -f (Get-HexMarker $serialText 'GXOS_NET10:IMAGE_BASE='))
        SerialLog = $serial
        Serial = $serialText
    }
}

function Validate-Enabled([pscustomobject]$Run) {
    $serial = $Run.Serial
    Assert-Marker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_BEGIN'
    Assert-Marker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_TYPE=LowMemoryResourceNotification'
    Assert-Marker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_RETURNED'
    Assert-Marker $serial 'GXOS_NET10:IMPORT_BLOCKER_DLL='
    Assert-Marker $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL='
    if ($serial.Contains('GXOS_NET10:MANAGED_ENTRY_OK')) { throw 'Enabled run reached managed entry.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_INVOCATION=') -ne 1) { throw 'Unexpected notification invocation count.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_RCX=') -ne 0) { throw 'Unexpected notification RCX.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_RAW_TYPE=') -ne 0) { throw 'Unexpected notification type.' }
    $handle = Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_RETURNED_HANDLE='
    if ($handle -eq 0) { throw 'Notification handle is NULL.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_HANDLE_TYPE=') -ne 3) { throw 'Notification object type is not 3.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_SIGNALED=') -ne 0) { throw 'Notification is not nonsignaled.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_WAITABLE_LIVE=') -ne 1) { throw 'Notification waitable is not live.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_WAITABLE_COMPATIBLE=') -ne 1) { throw 'Notification waitable compatibility failed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_CLOSE_STATE=') -ne 0) { throw 'Notification is not open.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_PUBLIC_REFERENCE_COUNT=') -ne 1) { throw 'Notification reference count is not 1.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_WAITER_COUNT=') -ne 0) { throw 'Notification waiter count is not 0.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_STORAGE_RVA=') -ne 0xADA28) { throw 'Notification storage RVA mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_STORAGE_VALUE=') -ne $handle) { throw 'Notification storage value changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:MEMORYRESOURCENOTIFICATION_FINAL_STORAGE_FAILURE_COUNT=') -ne 0) { throw 'Notification storage validation failed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_SUCCESS_COUNT=') -ne 2) { throw 'CreateEventW count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_LIVE_EVENT_OBJECT_COUNT=') -ne 2) { throw 'Event object count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_OBJECT_COUNT=') -ne 1) { throw 'Notification object count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_LIVE_PUBLIC_HANDLE_COUNT=') -ne 2) { throw 'Event public handle regression.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_TOTAL_LIVE_PUBLIC_HANDLE_COUNT=') -ne 3) { throw 'Total public handle count mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_TOTAL_LIVE_OBJECT_COUNT=') -ne 4) { throw 'Total live object count mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_FREE_OBJECT_COUNT=') -ne 12) { throw 'Object free count mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_SCHEDULER_THREAD_OBJECT_COUNT=') -ne 1) { throw 'Scheduler thread object count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_ADDITIONAL_THREAD_COUNT=') -ne 0) { throw 'Notification created a scheduler thread.' }
    return [PSCustomObject]@{
        BlockerDll = Get-TextMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_DLL='
        BlockerSymbol = Get-TextMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL='
        BlockerDescriptor = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX='
        BlockerSymbolIndex = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX='
        BlockerIatRva = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA='
        BlockerRuntimeIat = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_IAT='
        BlockerRuntimeCallSite = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_RUNTIME_CALL_SITE='
        BlockerCallerRva = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA='
        BlockerRcx = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_RCX='
        BlockerRdx = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_RDX='
        BlockerR8 = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_R8='
        BlockerR9 = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_R9='
        BlockerStackArg5 = Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_STACK_ARG5='
    }
}

function Validate-Disabled([pscustomobject]$Run) {
    $serial = $Run.Serial
    Assert-Marker $serial 'GXOS_NET10:IMPORT_BLOCKER_DLL=KERNEL32.dll'
    Assert-Marker $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL=CreateMemoryResourceNotification'
    if ($serial.Contains('GXOS_NET10:MEMORYRESOURCENOTIFICATION_BEGIN')) { throw 'Disabled route invoked notification.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_DESCRIPTOR_INDEX=') -ne 2) { throw 'Disabled descriptor mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_SYMBOL_INDEX=') -ne 0x36) { throw 'Disabled symbol index mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_IAT_RVA=') -ne 0x7D1E8) { throw 'Disabled IAT RVA mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_CALLER_RVA=') -ne 0x353F8) { throw 'Disabled caller RVA mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_RCX=') -ne 0) { throw 'Disabled RCX mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_RDX=') -ne 0x3F8) { throw 'Disabled RDX mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_R8=') -ne 1) { throw 'Disabled R8 mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_R9=') -ne 0) { throw 'Disabled R9 mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:IMPORT_BLOCKER_STACK_ARG5=') -ne 0) { throw 'Disabled stack argument mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_SUCCESS_COUNT=') -ne 2) { throw 'Disabled CreateEventW count mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_LIVE_EVENT_OBJECT_COUNT=') -ne 2) { throw 'Disabled Event count mismatch.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_OBJECT_COUNT=') -ne 0) { throw 'Disabled notification object count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_LIVE_MEMORY_RESOURCE_NOTIFICATION_HANDLE_COUNT=') -ne 0) { throw 'Disabled notification handle count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_TOTAL_LIVE_PUBLIC_HANDLE_COUNT=') -ne 2) { throw 'Disabled public handle count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_TOTAL_LIVE_OBJECT_COUNT=') -ne 3) { throw 'Disabled object count changed.' }
    if ((Get-HexMarker $serial 'GXOS_NET10:CREATEEVENTW_FINAL_ADDITIONAL_THREAD_COUNT=') -ne 0) { throw 'Disabled scheduler thread count changed.' }
}

$enabledResults = [Collections.Generic.List[object]]::new()
$disabledResult = $null
try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        $run = Invoke-PayloadRun $enabled 'enabled' $sequence
        $validation = Validate-Enabled $run
        $enabledResults.Add([PSCustomObject]@{ Run = $run; Validation = $validation })
        Write-Output ("ENABLED_RUN_{0}=" -f $sequence)
        $validation | ConvertTo-Json -Compress | Write-Output
    }
    $first = $enabledResults[0].Validation
    foreach ($result in $enabledResults) {
        $validation = $result.Validation
        foreach ($property in @('BlockerDll','BlockerSymbol','BlockerDescriptor','BlockerSymbolIndex','BlockerIatRva','BlockerCallerRva','BlockerRcx','BlockerRdx','BlockerR8','BlockerR9','BlockerStackArg5')) {
            if ($validation.$property -ne $first.$property) {
                throw "Enabled run semantic disagreement: $property"
            }
        }
    }
    $disabledRun = Invoke-PayloadRun $disabled 'disabled' 1
    Validate-Disabled $disabledRun
    $disabledResult = $disabledRun
    Write-Output 'DISABLED_RUN=PASSED'
    Write-Output ("ENABLED_RUNS={0}" -f $enabledResults.Count)
    Write-Output ("ENABLED_NEXT_BLOCKER={0}!{1}" -f $first.BlockerDll, $first.BlockerSymbol)
    Write-Output ("UNRELATED_QEMU_IDS={0}" -f (($unrelatedQemuIds | Sort-Object) -join ','))
}
finally {
    foreach ($id in @($taskQemuIds)) {
        if (Get-Process -Id $id -ErrorAction SilentlyContinue) {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $id -Timeout 3 -ErrorAction SilentlyContinue
        }
    }
}

$remainingTaskQemu = @($taskQemuIds | Where-Object {
    Get-Process -Id $_ -ErrorAction SilentlyContinue
})
if ($remainingTaskQemu.Count -ne 0) { throw "Task-owned QEMU remains: $($remainingTaskQemu -join ',')" }
Write-Output 'TASK_QEMU_REMAINING=0'

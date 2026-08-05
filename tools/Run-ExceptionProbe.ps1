[CmdletBinding()]
param(
    [ValidateSet('Positive', 'ContinueSearch', 'InvalidReturn', 'Empty', 'Nested')]
    [string]$Mode = 'Positive',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 15,
    [string]$GateDirectory = '',
    [string]$ExpectedPayloadSha256 = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($GateDirectory)) {
    $GateDirectory = Join-Path $root 'artifacts\exception-probe-gate'
}
$gate = [IO.Path]::GetFullPath($GateDirectory)
$efi = Join-Path $gate 'ESP\EFI\BOOT\BOOTX64.EFI'
$payload = Join-Path $gate 'ESP\GXOS\gxos-managed-entry-probe.dll'
if (-not (Test-Path -LiteralPath $efi)) { throw "Harness not found: $efi" }
if (-not (Test-Path -LiteralPath $payload)) { throw "Payload not found: $payload" }
$payloadHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash
if ($payloadHash -ne $ExpectedPayloadSha256) { throw "Payload SHA-256 mismatch: $payloadHash" }

$qemu = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
if ($qemu) { $qemuPath = $qemu.Source }
else {
    $qemuPath = 'C:\Program Files\qemu\qemu-system-x86_64.exe'
    if (-not (Test-Path -LiteralPath $qemuPath)) { throw 'qemu-system-x86_64.exe is required.' }
}
$share = Join-Path (Split-Path -Parent $qemuPath) 'share'
$ovmf = Join-Path $share 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $share 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf)) { throw "OVMF code not found: $ovmf" }
if (-not (Test-Path -LiteralPath $varsTemplate)) { throw "OVMF vars not found: $varsTemplate" }

$runRoot = Join-Path $gate ('exception-' + $Mode.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$codePath = Join-Path $runRoot 'edk2-x86_64-code.fd'
Copy-Item -LiteralPath $ovmf -Destination $codePath
$requiredPositive = @(
    'GXOS_NET10:VEH_REGISTRY_INITIALIZED=1',
    'GXOS_NET10:VEH_REGISTRY_CAPACITY=0x0000000000000008',
    'GXOS_NET10:EXCEPTION_TRAP_ENTERED=1',
    'GXOS_NET10:EXCEPTION_VECTOR_EQUALS_3=1',
    'GXOS_NET10:EXCEPTION_COMPLETE_FRAME_CAPTURED=1',
    'GXOS_NET10:EXCEPTION_COMPATIBILITY_STRUCTURES_BUILT=1',
    'GXOS_NET10:EXCEPTION_CODE=0x0000000080000003',
    'GXOS_NET10:EXCEPTION_RIP_SEMANTICS=AFTER_INT3',
    'GXOS_NET10:EXCEPTION_HANDLER_B_INVOKED=1',
    'GXOS_NET10:EXCEPTION_HANDLER_B_RETURN=0x0000000000000000',
    'GXOS_NET10:EXCEPTION_HANDLER_C_INVOKED=1',
    'GXOS_NET10:EXCEPTION_HANDLER_C_VALIDATION=1',
    'GXOS_NET10:EXCEPTION_HANDLER_C_RETURN=0x00000000FFFFFFFF',
    'GXOS_NET10:EXCEPTION_HANDLER_ORDER_SNAPSHOT=B,C,A',
    'GXOS_NET10:EXCEPTION_CALLBACK_SLOT=0x0000000000000000',
    'GXOS_NET10:EXCEPTION_CALLBACK_SLOT=0x0000000000000001',
    'GXOS_NET10:EXCEPTION_CALLBACK_INVOCATION=0x0000000000000001',
    'GXOS_NET10:EXCEPTION_DISPATCH_ACTIVE_AFTER=0x0000000000000000',
    'GXOS_NET10:EXCEPTION_NESTED_REGISTRATION_REJECTED=1',
    'GXOS_NET10:EXCEPTION_HANDLER_RETURNED_CONTINUE_EXECUTION=1',
    'GXOS_NET10:EXCEPTION_REQUESTED_CONTEXT_MODIFICATION_VALIDATED=1',
    'GXOS_NET10:EXCEPTION_IRETQ_RESTORATION_STARTED=1',
    'GXOS_NET10:EXCEPTION_LANDING_PAD_REACHED=1',
    'GXOS_NET10:EXCEPTION_MODIFIED_RCX_VERIFIED=1',
    'GXOS_NET10:EXCEPTION_MODIFIED_RDX_VERIFIED=1',
    'GXOS_NET10:EXCEPTION_STACK_POINTER_VALID=1',
    'GXOS_NET10:EXCEPTION_PROBE_COMPLETED=1',
    'GXOS_NET10:EXCEPTION_PROBE_SUCCESS=1'
)
$requiredNegative = @(
    'GXOS_NET10:VEH_REGISTRY_INITIALIZED=1',
    'GXOS_NET10:EXCEPTION_TRAP_ENTERED=1',
    'GXOS_NET10:EXCEPTION_VECTOR_EQUALS_3=1',
    'GXOS_NET10:EXCEPTION_COMPLETE_FRAME_CAPTURED=1',
    'GXOS_NET10:EXCEPTION_COMPATIBILITY_STRUCTURES_BUILT=1',
    'GXOS_NET10:EXCEPTION_CODE=0x0000000080000003',
    'GXOS_NET10:EXCEPTION_HANDLER_B_INVOKED=1',
    'GXOS_NET10:EXCEPTION_HANDLER_A_INVOKED=1',
    'GXOS_NET10:EXCEPTION_HANDLER_ORDER_SNAPSHOT=B,A',
    'GXOS_NET10:EXCEPTION_DISPATCH_ACTIVE_AFTER=0x0000000000000000',
    'GXOS_NET10:EXCEPTION_HANDLER_RETURNED_CONTINUE_SEARCH=1',
    'GXOS_NET10:EXCEPTION_FATAL_PATH=1',
    'GXOS_NET10:FAULT_VECTOR=0x3'
)
$requiredInvalid = @(
    'GXOS_NET10:VEH_REGISTRY_INITIALIZED=1',
    'GXOS_NET10:EXCEPTION_HANDLER_INVALID_INVOKED=1',
    'GXOS_NET10:EXCEPTION_HANDLER_INVALID_RETURN=0x0000000000000001',
    'GXOS_NET10:EXCEPTION_HANDLER_A_INVOKED=1',
    'GXOS_NET10:EXCEPTION_INVALID_RETURN_COUNT=0x0000000000000001',
    'GXOS_NET10:EXCEPTION_HANDLER_RETURNED_CONTINUE_SEARCH=1',
    'GXOS_NET10:EXCEPTION_FATAL_PATH=1',
    'GXOS_NET10:FAULT_VECTOR=0x3'
)
$requiredEmpty = @(
    'GXOS_NET10:VEH_REGISTRY_INITIALIZED=1',
    'GXOS_NET10:EXCEPTION_REGISTRY_MODE=EMPTY',
    'GXOS_NET10:EXCEPTION_HANDLER_ORDER_SNAPSHOT=',
    'GXOS_NET10:EXCEPTION_HANDLER_RETURNED_CONTINUE_SEARCH=1',
    'GXOS_NET10:EXCEPTION_FATAL_PATH=1',
    'GXOS_NET10:FAULT_VECTOR=0x3'
)
$requiredNested = @(
    'GXOS_NET10:VEH_REGISTRY_INITIALIZED=1',
    'GXOS_NET10:EXCEPTION_REGISTRY_MODE=B_C_A',
    'GXOS_NET10:EXCEPTION_HANDLER_B_INVOKED=1',
    'GXOS_NET10:EXCEPTION_NESTED_REGISTRATION_REJECTED=1',
    'GXOS_NET10:EXCEPTION_DISPATCH_ACTIVE_TERMINAL_CLEAR=1',
    'GXOS_NET10:EXCEPTION_NESTED_DISPATCH_TERMINAL=1',
    'GXOS_NET10:FAULT_VECTOR=0x3'
)
$allPassed = $true

function Read-Serial([string]$path) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            if (-not (Test-Path -LiteralPath $path)) { return '' }
            return [IO.File]::ReadAllText($path)
        } catch [IO.IOException] {
            Start-Sleep -Milliseconds 100
        }
    }
    throw "Serial log remained locked: $path"
}

for ($i = 1; $i -le $RunCount; $i++) {
    $runId = 'exception-' + $Mode.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-run' + $i
    $serial = Join-Path $runRoot ($runId + '.serial.log')
    $stdout = Join-Path $runRoot ($runId + '.stdout.log')
    $stderr = Join-Path $runRoot ($runId + '.stderr.log')
    $vars = Join-Path $runRoot ($runId + '.vars.fd')
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
    $process = Start-Process -FilePath $qemuPath -ArgumentList $arguments -WorkingDirectory $gate -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $completed = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $completed) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
        $classification = 'TIMEOUT_TERMINATED_AFTER_MARKERS'
    } else { $classification = 'EXITED' }
    Start-Sleep -Milliseconds 250
    $serialText = Read-Serial $serial
    $required = switch ($Mode) {
        'Positive' { $requiredPositive }
        'ContinueSearch' { $requiredNegative }
        'InvalidReturn' { $requiredInvalid }
        'Empty' { $requiredEmpty }
        'Nested' { $requiredNested }
    }
    $missing = @($required | Where-Object { -not $serialText.Contains($_) })
    if ($Mode -eq 'Positive') {
        $possibleUnexpected = @(
            'GXOS_NET10:EXCEPTION_FATAL_PATH=1',
            'GXOS_NET10:EXCEPTION_HANDLER_RETURNED_CONTINUE_SEARCH=1',
            'GXOS_NET10:MANAGED_ENTRY_OK',
            'GXOS_NET10:EXCEPTION_HANDLER_A_INVOKED=1'
        )
    } else {
        $possibleUnexpected = @(
            'GXOS_NET10:EXCEPTION_LANDING_PAD_REACHED=1',
            'GXOS_NET10:EXCEPTION_PROBE_SUCCESS=1',
            'GXOS_NET10:MANAGED_ENTRY_OK'
        )
    }
    $unexpected = @($possibleUnexpected | Where-Object { $serialText.Contains($_) })
    $runPass = $missing.Count -eq 0 -and $unexpected.Count -eq 0 -and
        ((Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash -eq $ExpectedPayloadSha256)
    if (-not $runPass) { $allPassed = $false }
    [PSCustomObject]@{
        RunId = $runId
        Mode = $Mode
        Pass = $runPass
        Classification = $classification
        QemuVersion = (& $qemuPath '--version' 2>$null | Select-Object -First 1).Trim()
        PayloadSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $payload).Hash
        LoaderSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $efi).Hash
        SerialLog = $serial
        MissingExpected = $missing
        UnexpectedPresent = $unexpected
        Serial = $serialText.Trim()
    } | ConvertTo-Json -Compress
}

$remaining = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue)
if ($remaining.Count -ne 0) {
    $allPassed = $false
    Write-Error 'QEMU process remains after exception probe validation.'
}
if ($allPassed) {
    Write-Output "EXCEPTION_PROBE_$($Mode.ToUpperInvariant())=PASSED"
    exit 0
}
Write-Output "EXCEPTION_PROBE_$($Mode.ToUpperInvariant())=NOT_PASSED"
exit 2

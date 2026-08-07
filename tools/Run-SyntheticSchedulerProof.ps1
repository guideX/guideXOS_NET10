[CmdletBinding()]
param(
    [string]$BuildDirectory = '',
    [string]$PayloadPath = '',
    [int]$RunCount = 3,
    [int]$TimeoutSeconds = 15
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($BuildDirectory)) {
    $BuildDirectory = Join-Path $root 'artifacts\scheduler-foundation-build'
}
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $root 'artifacts\veh-final3-normal-gate\ESP\GXOS\gxos-managed-entry-probe.dll'
}
$buildDirectory = [IO.Path]::GetFullPath($BuildDirectory)
$payloadPath = [IO.Path]::GetFullPath($PayloadPath)
$esp = Join-Path $buildDirectory 'ESP'
$efi = Join-Path $esp 'EFI\BOOT\BOOTX64.EFI'
$builtPayload = Join-Path $esp 'GXOS\gxos-managed-entry-probe.dll'
$expectedHash = '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837'

if (-not (Test-Path -LiteralPath $efi)) { throw "Synthetic harness not found: $efi" }
if (-not (Test-Path -LiteralPath $payloadPath)) { throw "Payload not found: $payloadPath" }
$sourceHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToUpperInvariant()
$builtHash = (Get-FileHash -LiteralPath $builtPayload -Algorithm SHA256).Hash.ToUpperInvariant()
if ($sourceHash -ne $expectedHash -or $builtHash -ne $expectedHash) {
    throw "Exact payload hash mismatch. Source=$sourceHash Built=$builtHash"
}

$qemuCommand = Get-Command qemu-system-x86_64.exe -ErrorAction SilentlyContinue
$qemu = if ($null -ne $qemuCommand) { [IO.Path]::GetFullPath($qemuCommand.Source) } else { 'C:\Program Files\qemu\qemu-system-x86_64.exe' }
if (-not (Test-Path -LiteralPath $qemu)) { throw "QEMU not found: $qemu" }
$qemuShare = Join-Path (Split-Path -Parent $qemu) 'share'
$ovmf = Join-Path $qemuShare 'edk2-x86_64-code.fd'
$varsTemplate = Join-Path $qemuShare 'edk2-i386-vars.fd'
if (-not (Test-Path -LiteralPath $ovmf) -or -not (Test-Path -LiteralPath $varsTemplate)) {
    throw "OVMF files not found under $qemuShare"
}
if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'A pre-existing QEMU process is present.'
}
if ($RunCount -lt 3) { throw 'At least three fresh QEMU runs are required.' }

$runRoot = Join-Path $buildDirectory ('synthetic-runs-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$required = @(
    'GXOS_NET10:SCHEDULER_PROOF=PASSED',
    'GXOS_NET10:SCHEDULER_NEUTRAL_STATE=1',
    'GXOS_NET10:SCHEDULER_TRANSITION=CreatedSuspended->Runnable',
    'GXOS_NET10:SCHEDULER_RESUME_PREVIOUS_SUSPEND_COUNT=0x0000000000000001',
    'GXOS_NET10:SCHEDULER_WORKER_ENTRY_ALIGNMENT=0x0000000000000008',
    'GXOS_NET10:SCHEDULER_WORKER_HANDLE_CLOSED_LIVE=1',
    'GXOS_NET10:SCHEDULER_WORKER_PRIVATE_STATE=PROVEN',
    'GXOS_NET10:SCHEDULER_EVENT_B_MANUAL_SIGNAL_PERSISTS=1',
    'GXOS_NET10:SCHEDULER_EVENT_B_RESET_NONSIGNALED=1',
    'GXOS_NET10:SCHEDULER_EVENT_A_SIGNAL_WAKE=1',
    'GXOS_NET10:SCHEDULER_WORKER_WAIT_RETURNED=1',
    'GXOS_NET10:SCHEDULER_WORKER_RESUMED_AFTER_EVENT_A',
    'GXOS_NET10:SCHEDULER_WORKER_TERMINATED=1',
    'GXOS_NET10:SCHEDULER_WORKER_CANARIES_INTACT=1',
    'GXOS_NET10:SCHEDULER_TEARDOWN=0x0000000000000001',
    'GXOS_NET10:SCHEDULER_FAILURE_COUNT=0x0000000000000000',
    'GXOS_NET10:SYNTHETIC_SCHEDULER_PROOF_RETURNED'
)

try {
    for ($sequence = 1; $sequence -le $RunCount; $sequence++) {
        if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
            throw "QEMU process present before run $sequence."
        }
        $run = Join-Path $runRoot ('run-' + $sequence)
        New-Item -ItemType Directory -Path $run -Force | Out-Null
        $code = Join-Path $run 'edk2-code.fd'
        $vars = Join-Path $run 'edk2-vars.fd'
        $serial = Join-Path $run 'serial.log'
        $stdout = Join-Path $run 'qemu.stdout.log'
        $stderr = Join-Path $run 'qemu.stderr.log'
        Copy-Item -LiteralPath $ovmf -Destination $code
        Copy-Item -LiteralPath $varsTemplate -Destination $vars
        $arguments = @(
            '-machine', 'q35', '-accel', 'tcg,thread=multi', '-m', '128M',
            '-drive', "if=pflash,format=raw,readonly=on,file=$code",
            '-drive', "if=pflash,format=raw,file=$vars",
            '-drive', 'file=fat:rw:ESP,format=raw,if=ide,index=0,media=disk',
            '-rtc', 'base=utc,clock=vm', '-boot', 'order=c',
            '-serial', "file:$serial", '-monitor', 'none', '-display', 'none',
            '-no-reboot', '-no-shutdown'
        )
        $process = Start-Process -FilePath $qemu -ArgumentList $arguments -WorkingDirectory $buildDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $completed = $process.WaitForExit($TimeoutSeconds * 1000)
        if (-not $completed) {
            Stop-Process -Id $process.Id -Force
            Wait-Process -Id $process.Id -Timeout 3 -ErrorAction SilentlyContinue
        }
        Start-Sleep -Milliseconds 250
        if (Get-Process -Id $process.Id -ErrorAction SilentlyContinue) {
            Stop-Process -Id $process.Id -Force
            throw "QEMU run $sequence remained alive after exit."
        }
        $serialText = if (Test-Path -LiteralPath $serial) {
            [IO.File]::ReadAllText($serial)
        } else { '' }
        foreach ($marker in $required) {
            if (-not $serialText.Contains($marker)) {
                throw "Synthetic run $sequence missing marker: $marker"
            }
        }
        if ($serialText.Contains('DEBUG') -or $serialText.Contains('FAULT_') -or
            $serialText.Contains('GXOS_NET10:FAIL:')) {
            throw "Synthetic run $sequence contains a fault, failure, or temporary diagnostic."
        }
        $classification = if ($completed) { 'EXITED' } else { 'EXPECTED_HALT' }
        Write-Output "SYNTHETIC_QEMU_RUN_$sequence=PASSED classification=$classification serial=$serial"
    }
}
finally {
    $remaining = @(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue)
    foreach ($process in $remaining) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

if (@(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue).Count -ne 0) {
    throw 'QEMU cleanup failed.'
}
Write-Output "SYNTHETIC_QEMU_RUNS=$RunCount"
Write-Output "SYNTHETIC_PAYLOAD_SHA256=$sourceHash"

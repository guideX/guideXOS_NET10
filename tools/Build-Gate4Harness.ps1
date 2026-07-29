[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [string]$ManagedArtifact = '',
    [ValidateSet('Normal', 'InvalidBootInfo', 'NullSerial', 'UnresolvedImport', 'InvokeFailfast', 'TimeDisabled', 'TimeInvalidMonth', 'TimeInvalidDay', 'TimeInvalidTimezone', 'TimeFixedZero', 'TimeMarkerMutation')]
    [string]$Scenario = 'Normal',
    [switch]$EnableNativeAotStartup,
    [switch]$AssumeUnspecifiedTimezoneUtc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\gate4'
}
$outputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$espDirectory = Join-Path $outputDirectory 'ESP'
$efiDirectory = Join-Path $espDirectory 'EFI\BOOT'
$payloadDirectory = Join-Path $espDirectory 'GXOS'
$buildLog = Join-Path $outputDirectory 'harness-build.stdout.log'
$buildErrorLog = Join-Path $outputDirectory 'harness-build.stderr.log'
$source = Join-Path $root 'src\Gate4Harness\gate4_loader.c'
$timeSource = Join-Path $root 'src\Gate4Harness\platform_time.c'
$startupSource = Join-Path $root 'src\Gate4Harness\startup.nsh'
$efi = Join-Path $efiDirectory 'BOOTX64.EFI'
$payload = Join-Path $payloadDirectory 'gxos-managed-entry-probe.dll'
$startupScript = Join-Path $espDirectory 'startup.nsh'
if ([string]::IsNullOrWhiteSpace($ManagedArtifact)) {
    $managedArtifact = Join-Path $root 'artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll'
} else {
    $managedArtifact = [IO.Path]::GetFullPath($ManagedArtifact)
}

if (-not (Test-Path -LiteralPath $source)) { throw "Harness source not found: $source" }
if (-not (Test-Path -LiteralPath $timeSource)) { throw "Platform time source not found: $timeSource" }
if (-not (Test-Path -LiteralPath $startupSource)) { throw "UEFI startup script not found: $startupSource" }
if (-not (Test-Path -LiteralPath $managedArtifact)) {
    throw "Build the Gate 1 shared artifact first: $managedArtifact"
}

$gccCommand = Get-Command gcc -ErrorAction SilentlyContinue
if (-not $gccCommand) { throw 'gcc is required to build the freestanding UEFI harness.' }
$objdumpCommand = Get-Command objdump -ErrorAction SilentlyContinue
if (-not $objdumpCommand) { throw 'objdump is required to validate the UEFI harness image.' }

New-Item -ItemType Directory -Path $efiDirectory,$payloadDirectory -Force | Out-Null
Copy-Item -LiteralPath $managedArtifact -Destination $payload -Force
Copy-Item -LiteralPath $startupSource -Destination $startupScript -Force

$gccArguments = @(
    '-ffreestanding', '-fno-stack-protector', '-fno-asynchronous-unwind-tables',
    '-fno-ident', '-mno-red-zone', '-O2', '-Wall', '-Wextra', '-Werror',
    '-nostdlib', '-Wl,--entry,efi_main', '-Wl,--subsystem,10',
    '-Wl,--image-base,0x100000', '-Wl,--enable-reloc-section',
    '-o', $efi, $source, $timeSource
)
switch ($Scenario) {
    'InvalidBootInfo' { $gccArguments += '-DGXOS_NEGATIVE_INVALID_BOOT_INFO' }
    'NullSerial' { $gccArguments += '-DGXOS_NEGATIVE_NULL_SERIAL' }
    'UnresolvedImport' { $gccArguments += '-DGXOS_NEGATIVE_UNRESOLVED_IMPORT' }
    'InvokeFailfast' { $gccArguments += '-DGXOS_NEGATIVE_INVOKE_FAILFAST' }
    'TimeDisabled' { $gccArguments += '-DGXOS_DISABLE_TIME_IMPLEMENTATION' }
    'TimeInvalidMonth' { $gccArguments += '-DGXOS_TIME_TEST_INVALID_MONTH' }
    'TimeInvalidDay' { $gccArguments += '-DGXOS_TIME_TEST_INVALID_DAY' }
    'TimeInvalidTimezone' { $gccArguments += '-DGXOS_TIME_TEST_INVALID_TIMEZONE' }
    'TimeFixedZero' { $gccArguments += '-DGXOS_TIME_TEST_FIXED_ZERO' }
    'TimeMarkerMutation' { $gccArguments += '-DGXOS_TIME_MARKER_MUTATION' }
}
if ($EnableNativeAotStartup) { $gccArguments += '-DGXOS_ENABLE_NATIVEAOT_STARTUP' }
if ($AssumeUnspecifiedTimezoneUtc) { $gccArguments += '-DGXOS_ASSUME_UNSPECIFIED_TIMEZONE_UTC' }

& $gccCommand.Source @gccArguments 1> $buildLog 2> $buildErrorLog
if ($LASTEXITCODE -ne 0) {
    throw "UEFI harness compile failed (exit $LASTEXITCODE). See $buildErrorLog"
}

$peReport = & $objdumpCommand.Source '-p' $efi 2>&1
if ($LASTEXITCODE -ne 0) { throw "objdump could not read $efi" }
$peReport | Set-Content -LiteralPath (Join-Path $outputDirectory 'harness-pe-report.txt')
if (-not ($peReport -match 'Subsystem\s+0000000a')) { throw 'Harness is not a PE/COFF EFI application (Subsystem 10).' }
if ($peReport -match 'DLL Name:') { throw 'Harness unexpectedly has a platform import.' }

Get-FileHash -Algorithm SHA256 -LiteralPath $efi,$payload | Format-Table -AutoSize
Write-Output "GATE4_HARNESS=$efi"
Write-Output "GATE4_ESP=$espDirectory"

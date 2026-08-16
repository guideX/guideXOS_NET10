[CmdletBinding()]
param(
    [string]$OutputDirectory = '',
    [string]$ManagedArtifact = '',
    [ValidateSet('Normal', 'InvalidBootInfo', 'NullSerial', 'UnresolvedImport', 'InvokeFailfast', 'ExceptionProbe', 'ExceptionProbeContinueSearch', 'ExceptionRegistryAllContinueSearch', 'ExceptionRegistryInvalidReturn', 'ExceptionRegistryEmpty', 'ExceptionRegistryNested', 'TimeDisabled', 'TimeInvalidMonth', 'TimeInvalidDay', 'TimeInvalidTimezone', 'TimeFixedZero', 'TimeMarkerMutation', 'PerfDisabled', 'PerfStallProbe', 'CrtOnexitInit', 'CrtOnexitDisabled', 'CrtOnexitMarkerMutation', 'SlistInit', 'SlistDisabled', 'SlistMarkerMutation', 'CrtInittermE', 'CrtInittermEDisabled', 'CrtInittermEMarkerMutation', 'CrtInitterm', 'CrtInittermDisabled', 'CrtInittermMarkerMutation', 'CrtStrcmp', 'CrtStrcmpDisabled', 'CrtStrlen', 'CrtStrlenDisabled', 'GetEnvironmentVariableW', 'GetEnvironmentVariableWDisabled', 'GetEnvironmentVariableWMarkerMutation', 'CrtStricmp', 'CrtStricmpDisabled', 'CrtStricmpMarkerMutation', 'GetSystemInfo', 'GetSystemInfoDisabled', 'GetSystemInfoMarkerMutation', 'GetNumaHighestNodeNumber', 'GetNumaHighestNodeNumberDisabled', 'GetNumaHighestNodeNumberSuccessExperiment', 'GetNumaHighestNodeNumberFailureExperiment', 'GetProcessGroupAffinity', 'GetProcessGroupAffinityDisabled', 'GetProcessGroupAffinityMarkerMutation', 'GetProcessAffinityMask', 'GetProcessAffinityMaskDisabled', 'GetProcessAffinityMaskMarkerMutation', 'GetProcessAffinityMaskFailureExperiment', 'QueryInformationJobObject', 'QueryInformationJobObjectDisabled', 'QueryInformationJobObjectMarkerMutation', 'QueryInformationJobObjectSuccessExperiment', 'QueryInformationJobObjectActiveLimitExperiment', 'IsProcessInJob', 'IsProcessInJobDisabled', 'GetModuleHandleW', 'GetModuleHandleWDisabled', 'GetModuleHandleWNamedMainExperiment', 'GetModuleHandleWForcedFailure', 'GetModuleHandleWRvaExperiment', 'GetModuleHandleWWrongImageExperiment', 'GetModuleHandleEx', 'GetModuleHandleExDisabled', 'GetProcAddress', 'GetProcAddressDisabled', 'GetProcAddressSyntheticPointer', 'GetProcAddressWrongError', 'RegisterOnexit', 'RegisterOnexitDisabled', 'RegisterOnexitMarkerMutation', 'Malloc', 'MallocDisabled', 'VectoredExceptionHandler', 'VectoredExceptionHandlerDisabled', 'CreateEventW', 'CreateEventWDisabled', 'CreateMemoryResourceNotification', 'CreateMemoryResourceNotificationDisabled', 'CreateThread', 'CreateThreadDisabled', 'SetThreadPriority', 'SetThreadPriorityDisabled', 'ResumeThread', 'ResumeThreadDisabled', 'GlobalMemoryStatusEx', 'GlobalMemoryStatusExDisabled', 'VirtualMemory', 'NativeAotEventWait', 'SyntheticScheduler')]
    [string]$Scenario = 'Normal',
    [switch]$EnableNativeAotStartup,
    [switch]$EnableNativeAotManagedCallback,
    [switch]$EnableNativeAotSchedulerCallback,
    [switch]$AssumeUnspecifiedTimezoneUtc
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($EnableNativeAotManagedCallback -and $Scenario -ne 'NativeAotEventWait') {
    throw 'NativeAot managed callback validation requires the NativeAotEventWait scenario.'
}
if ($EnableNativeAotManagedCallback -and -not $EnableNativeAotStartup) {
    throw 'NativeAot managed callback validation requires -EnableNativeAotStartup.'
}
if ($EnableNativeAotSchedulerCallback -and -not $EnableNativeAotManagedCallback) {
    throw 'NativeAot scheduler callback validation requires -EnableNativeAotManagedCallback.'
}
if ($EnableNativeAotSchedulerCallback -and $Scenario -ne 'NativeAotEventWait') {
    throw 'NativeAot scheduler callback validation requires the NativeAotEventWait scenario.'
}

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
$memoryAccountingSource = Join-Path $root 'src\Gate4Harness\memory_accounting.c'
$vmSubstrateSource = Join-Path $root 'src\Gate4Harness\vm_substrate.c'
$virtualMemorySource = Join-Path $root 'src\Gate4Harness\virtual_memory.c'
$virtualQueryCaptureAssembly = Join-Path $root 'src\Gate4Harness\virtual_query_capture.S'
$processorTopologySource = Join-Path $root 'src\Gate4Harness\platform_processor_topology.c'
$timeSource = Join-Path $root 'src\Gate4Harness\platform_time.c'
$performanceSource = Join-Path $root 'src\Gate4Harness\platform_performance.c'
$exceptionSource = Join-Path $root 'src\Gate4Harness\exception_context.c'
$exceptionAssembly = Join-Path $root 'src\Gate4Harness\exception_entry.S'
$vectoredHandlerSource = Join-Path $root 'src\Gate4Harness\vectored_handler.c'
$schedulerSource = Join-Path $root 'src\Gate4Harness\scheduler_foundation.c'
$schedulerAssembly = Join-Path $root 'src\Gate4Harness\scheduler_context.S'
$schedulerProofSource = Join-Path $root 'src\Gate4Harness\scheduler_proof.c'
$createEventSource = Join-Path $root 'src\Gate4Harness\create_event_w.c'
$eventApiSource = Join-Path $root 'src\Gate4Harness\event_api.c'
$standardHandleSource = Join-Path $root 'src\Gate4Harness\standard_handle.c'
$writeFileSource = Join-Path $root 'src\Gate4Harness\write_file.c'
$writeFileEntryAssembly = Join-Path $root 'src\Gate4Harness\write_file_entry.S'
$comApiSource = Join-Path $root 'src\Gate4Harness\com_api.c'
$multibyteSource = Join-Path $root 'src\Gate4Harness\platform_multibyte.c'
$multibyteAssembly = Join-Path $root 'src\Gate4Harness\platform_multibyte_entry.S'
$moduleRegistrySource = Join-Path $root 'src\Gate4Harness\platform_module_registry.c'
$loadLibrarySource = Join-Path $root 'src\Gate4Harness\platform_load_library.c'
$nativeAotCallbackBridgeSource = Join-Path $root 'src\Gate4Harness\nativeaot_callback_bridge.c'
$createMemoryResourceNotificationSource = Join-Path $root 'src\Gate4Harness\create_memory_resource_notification.c'
$createThreadSource = Join-Path $root 'src\Gate4Harness\create_thread.c'
$createThreadEntryAssembly = Join-Path $root 'src\Gate4Harness\create_thread_entry.S'
$setThreadPrioritySource = Join-Path $root 'src\Gate4Harness\set_thread_priority.c'
$setThreadPriorityEntryAssembly = Join-Path $root 'src\Gate4Harness\set_thread_priority_entry.S'
$isProcessInJobSource = Join-Path $root 'src\Gate4Harness\platform_is_process_in_job.c'
$isProcessInJobEntryAssembly = Join-Path $root 'src\Gate4Harness\is_process_in_job_entry.S'
$importFailfastEntryAssembly = Join-Path $root 'src\Gate4Harness\import_failfast_entry.S'
$startupSource = Join-Path $root 'src\Gate4Harness\startup.nsh'
$efi = Join-Path $efiDirectory 'BOOTX64.EFI'
$payload = Join-Path $payloadDirectory 'gxos-managed-entry-probe.dll'
$startupScript = Join-Path $espDirectory 'startup.nsh'
if ([string]::IsNullOrWhiteSpace($ManagedArtifact)) {
    if ($Scenario -eq 'CreateEventW' -or $Scenario -eq 'CreateEventWDisabled' -or
        $Scenario -eq 'CreateMemoryResourceNotification' -or
        $Scenario -eq 'CreateMemoryResourceNotificationDisabled' -or
        $Scenario -eq 'CreateThread' -or $Scenario -eq 'CreateThreadDisabled' -or
        $Scenario -eq 'SetThreadPriority' -or $Scenario -eq 'SetThreadPriorityDisabled' -or
        $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
        $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled' -or
        $Scenario -eq 'GlobalMemoryStatusEx' -or $Scenario -eq 'GlobalMemoryStatusExDisabled' -or
        $Scenario -eq 'VirtualMemory' -or $Scenario -eq 'NativeAotEventWait') {
        $managedArtifact = Join-Path $root 'artifacts\veh-final3-normal-gate\ESP\GXOS\gxos-managed-entry-probe.dll'
    } else {
        $managedArtifact = Join-Path $root 'artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll'
    }
} else {
    $managedArtifact = [IO.Path]::GetFullPath($ManagedArtifact)
}

if (-not (Test-Path -LiteralPath $source)) { throw "Harness source not found: $source" }
if (-not (Test-Path -LiteralPath $moduleRegistrySource) -or
    -not (Test-Path -LiteralPath $loadLibrarySource)) {
    throw "Module loading sources not found: $moduleRegistrySource / $loadLibrarySource"
}
if (-not (Test-Path -LiteralPath $nativeAotCallbackBridgeSource)) {
    throw "NativeAOT callback bridge source not found: $nativeAotCallbackBridgeSource"
}
if (-not (Test-Path -LiteralPath $memoryAccountingSource)) { throw "Memory accounting source not found: $memoryAccountingSource" }
if (-not (Test-Path -LiteralPath $vmSubstrateSource)) { throw "VM substrate source not found: $vmSubstrateSource" }
if (-not (Test-Path -LiteralPath $virtualMemorySource)) { throw "Virtual memory source not found: $virtualMemorySource" }
if (-not (Test-Path -LiteralPath $virtualQueryCaptureAssembly)) { throw "VirtualQuery capture assembly not found: $virtualQueryCaptureAssembly" }
if (-not (Test-Path -LiteralPath $timeSource)) { throw "Platform time source not found: $timeSource" }
if (-not (Test-Path -LiteralPath $performanceSource)) { throw "Platform performance source not found: $performanceSource" }
if (-not (Test-Path -LiteralPath $exceptionSource)) { throw "Exception context source not found: $exceptionSource" }
if (-not (Test-Path -LiteralPath $exceptionAssembly)) { throw "Exception entry assembly not found: $exceptionAssembly" }
if (-not (Test-Path -LiteralPath $vectoredHandlerSource)) { throw "Vectored handler source not found: $vectoredHandlerSource" }
if (-not (Test-Path -LiteralPath $schedulerSource)) { throw "Scheduler source not found: $schedulerSource" }
if (-not (Test-Path -LiteralPath $schedulerAssembly)) { throw "Scheduler assembly not found: $schedulerAssembly" }
if (-not (Test-Path -LiteralPath $schedulerProofSource)) { throw "Scheduler proof source not found: $schedulerProofSource" }
if (-not (Test-Path -LiteralPath $createEventSource)) { throw "CreateEventW source not found: $createEventSource" }
if (-not (Test-Path -LiteralPath $comApiSource)) { throw "COM API source not found: $comApiSource" }
if (-not (Test-Path -LiteralPath $multibyteSource) -or
    -not (Test-Path -LiteralPath $multibyteAssembly)) {
    throw "MultiByteToWideChar sources not found: $multibyteSource / $multibyteAssembly"
}
if (-not (Test-Path -LiteralPath $standardHandleSource)) { throw "Standard handle source not found: $standardHandleSource" }
if (-not (Test-Path -LiteralPath $writeFileSource) -or
    -not (Test-Path -LiteralPath $writeFileEntryAssembly)) {
    throw "WriteFile sources not found: $writeFileSource / $writeFileEntryAssembly"
}
if (-not (Test-Path -LiteralPath $createMemoryResourceNotificationSource)) {
    throw "CreateMemoryResourceNotification source not found: $createMemoryResourceNotificationSource"
}
if (-not (Test-Path -LiteralPath $createThreadSource) -or
    -not (Test-Path -LiteralPath $createThreadEntryAssembly)) {
    throw "CreateThread sources not found: $createThreadSource / $createThreadEntryAssembly"
}
if (-not (Test-Path -LiteralPath $setThreadPrioritySource) -or
    -not (Test-Path -LiteralPath $setThreadPriorityEntryAssembly)) {
    throw "SetThreadPriority sources not found: $setThreadPrioritySource / $setThreadPriorityEntryAssembly"
}
if (-not (Test-Path -LiteralPath $startupSource)) { throw "UEFI startup script not found: $startupSource" }
if (-not (Test-Path -LiteralPath $managedArtifact)) {
    throw "Build the Gate 1 shared artifact first: $managedArtifact"
}
if ($Scenario -eq 'CreateEventW' -or $Scenario -eq 'CreateEventWDisabled' -or
    $Scenario -eq 'CreateMemoryResourceNotification' -or
    $Scenario -eq 'CreateMemoryResourceNotificationDisabled' -or
    $Scenario -eq 'CreateThread' -or $Scenario -eq 'CreateThreadDisabled' -or
    $Scenario -eq 'SetThreadPriority' -or $Scenario -eq 'SetThreadPriorityDisabled' -or
    $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
    $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled' -or
    $Scenario -eq 'GlobalMemoryStatusEx' -or $Scenario -eq 'GlobalMemoryStatusExDisabled' -or
    $Scenario -eq 'VirtualMemory' -or $Scenario -eq 'NativeAotEventWait') {
    $payloadHash = (Get-FileHash -LiteralPath $managedArtifact -Algorithm SHA256).Hash.ToUpperInvariant()
    if (-not $EnableNativeAotManagedCallback -and
        $payloadHash -ne '2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837') {
        throw "The thread payload integration requires the exact veh-final3-normal-gate payload. Hash=$payloadHash"
    }
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
    '-o', $efi, $source, $memoryAccountingSource, $vmSubstrateSource, $timeSource, $performanceSource,
    $exceptionSource, $exceptionAssembly, $vectoredHandlerSource,
    $virtualMemorySource,
    $virtualQueryCaptureAssembly,
    (Join-Path $root 'src\Gate4Harness\crt_onexit.c'),
    (Join-Path $root 'src\Gate4Harness\crt_initterm_e.c'),
    (Join-Path $root 'src\Gate4Harness\crt_initterm.c'),
    (Join-Path $root 'src\Gate4Harness\crt_strcmp.c'),
    (Join-Path $root 'src\Gate4Harness\crt_strlen.c'),
    (Join-Path $root 'src\Gate4Harness\crt_stricmp.c'),
    (Join-Path $root 'src\Gate4Harness\platform_environment.c'),
    (Join-Path $root 'src\Gate4Harness\platform_slist.c'),
    (Join-Path $root 'src\Gate4Harness\platform_system_info.c'),
    (Join-Path $root 'src\Gate4Harness\platform_numa.c'),
    (Join-Path $root 'src\Gate4Harness\platform_process_group_affinity.c'),
    (Join-Path $root 'src\Gate4Harness\platform_process_affinity.c'),
    (Join-Path $root 'src\Gate4Harness\platform_query_information_job_object.c'),
    $isProcessInJobSource,
    $importFailfastEntryAssembly,
    $moduleRegistrySource,
    $loadLibrarySource,
    $nativeAotCallbackBridgeSource,
    (Join-Path $root 'src\Gate4Harness\platform_get_module_handle.c'),
    (Join-Path $root 'src\Gate4Harness\platform_get_module_handle_ex.c'),
    (Join-Path $root 'src\Gate4Harness\platform_get_proc_address.c'),
    (Join-Path $root 'src\Gate4Harness\global_memory_status_ex.c'),
    $processorTopologySource
)
switch ($Scenario) {
    'InvalidBootInfo' { $gccArguments += '-DGXOS_NEGATIVE_INVALID_BOOT_INFO' }
    'NullSerial' { $gccArguments += '-DGXOS_NEGATIVE_NULL_SERIAL' }
    'UnresolvedImport' { $gccArguments += '-DGXOS_NEGATIVE_UNRESOLVED_IMPORT' }
    'InvokeFailfast' { $gccArguments += '-DGXOS_NEGATIVE_INVOKE_FAILFAST' }
    'ExceptionProbe' { $gccArguments += '-DGXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE' }
    'ExceptionProbeContinueSearch' {
        $gccArguments += '-DGXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE'
        $gccArguments += '-DGXOS_EXCEPTION_SYNTHETIC_CONTINUE_SEARCH'
        $gccArguments += '-DGXOS_EXCEPTION_REGISTRY_ALL_CONTINUE_SEARCH'
    }
    'ExceptionRegistryAllContinueSearch' {
        $gccArguments += '-DGXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE'
        $gccArguments += '-DGXOS_EXCEPTION_REGISTRY_ALL_CONTINUE_SEARCH'
    }
    'ExceptionRegistryInvalidReturn' {
        $gccArguments += '-DGXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE'
        $gccArguments += '-DGXOS_EXCEPTION_REGISTRY_INVALID_RETURN'
    }
    'ExceptionRegistryEmpty' {
        $gccArguments += '-DGXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE'
        $gccArguments += '-DGXOS_EXCEPTION_REGISTRY_EMPTY'
    }
    'ExceptionRegistryNested' {
        $gccArguments += '-DGXOS_ENABLE_EXCEPTION_SYNTHETIC_PROBE'
        $gccArguments += '-DGXOS_EXCEPTION_REGISTRY_NESTED'
    }
    'TimeDisabled' { $gccArguments += '-DGXOS_DISABLE_TIME_IMPLEMENTATION' }
    'TimeInvalidMonth' { $gccArguments += '-DGXOS_TIME_TEST_INVALID_MONTH' }
    'TimeInvalidDay' { $gccArguments += '-DGXOS_TIME_TEST_INVALID_DAY' }
    'TimeInvalidTimezone' { $gccArguments += '-DGXOS_TIME_TEST_INVALID_TIMEZONE' }
    'TimeFixedZero' { $gccArguments += '-DGXOS_TIME_TEST_FIXED_ZERO' }
    'TimeMarkerMutation' { $gccArguments += '-DGXOS_TIME_MARKER_MUTATION' }
    'PerfDisabled' { $gccArguments += '-DGXOS_PERF_TEST_DISABLED' }
    'PerfStallProbe' {
        $gccArguments += '-DGXOS_PERF_STALL_DIAGNOSTIC'
        $gccArguments += '-DGXOS_PERF_STALL_ONLY'
    }
    'CrtOnexitInit' { $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT' }
    'CrtOnexitDisabled' { }
    'CrtOnexitMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_CRT_ONEXIT_MARKER_MUTATION'
    }
    'SlistInit' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
    }
    'SlistDisabled' { $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT' }
    'SlistMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_SLIST_MARKER_MUTATION'
    }
    'CrtInittermE' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
    }
    'CrtInittermEDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
    }
    'CrtInittermEMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_CRT_INITTERM_E_MARKER_MUTATION'
    }
    'CrtInitterm' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
    }
    'CrtInittermDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
    }
    'CrtInittermMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_CRT_INITTERM_MARKER_MUTATION'
    }
    'CrtStrcmp' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
    }
    'CrtStrcmpDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
    }
    'CrtStrlen' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
    }
    'CrtStrlenDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
    }
    'GetEnvironmentVariableW' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
    }
    'GetEnvironmentVariableWDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
    }
    'GetEnvironmentVariableWMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_GETENV_MARKER_MUTATION'
    }
    'CrtStricmp' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
    }
    'CrtStricmpDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
    }
    'CrtStricmpMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_CRT_STRICMP_MARKER_MUTATION'
    }
    'GetSystemInfo' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
    }
    'GetSystemInfoDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
    }
    'GetSystemInfoMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_SYSTEM_INFO_MARKER_MUTATION'
    }
    'GetNumaHighestNodeNumber' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
    }
    'GetNumaHighestNodeNumberDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
    }
    'GetNumaHighestNodeNumberSuccessExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
    }
    'GetNumaHighestNodeNumberFailureExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_NUMA_FORCE_FAILURE'
    }
    'GetProcessGroupAffinity' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
    }
    'GetProcessGroupAffinityDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
    }
    'GetProcessGroupAffinityMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_PROCESS_GROUP_AFFINITY_MARKER_MUTATION'
    }
    'GetProcessAffinityMask' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
    }
    'GetProcessAffinityMaskDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
    }
    'GetProcessAffinityMaskMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_PROCESS_AFFINITY_MARKER_MUTATION'
    }
    'GetProcessAffinityMaskFailureExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_PROCESS_AFFINITY_FORCE_FAILURE'
    }
    'QueryInformationJobObject' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
    }
    'QueryInformationJobObjectDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
    }
    'QueryInformationJobObjectMarkerMutation' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_QUERY_JOB_MARKER_MUTATION'
    }
    'QueryInformationJobObjectSuccessExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_QUERY_JOB_SUCCESS_NO_LIMIT_EXPERIMENT'
    }
    'QueryInformationJobObjectActiveLimitExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_QUERY_JOB_ACTIVE_LIMIT_EXPERIMENT'
    }
    'GetModuleHandleW' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
    }
    'GetModuleHandleWDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
    }
    'GetModuleHandleWNamedMainExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_MODULE_HANDLE_NAMED_MAIN_EXPERIMENT'
    }
    'GetModuleHandleWForcedFailure' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_MODULE_HANDLE_FORCE_FAILURE'
    }
    'GetModuleHandleWPreferredBaseExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_MODULE_HANDLE_PREFERRED_BASE_EXPERIMENT'
    }
    'GetModuleHandleWRvaExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_MODULE_HANDLE_RVA_EXPERIMENT'
    }
    'GetModuleHandleWWrongImageExperiment' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_MODULE_HANDLE_WRONG_IMAGE_EXPERIMENT'
    }
    'GetModuleHandleEx' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE_EX'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
    }
    'GetModuleHandleExDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
    }
    'GetProcAddress' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
    }
    'GetProcAddressDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
    }
    'GetProcAddressSyntheticPointer' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_GET_PROC_ADDRESS_SYNTHETIC_RESULT'
    }
    'GetProcAddressWrongError' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_GET_PROC_ADDRESS_WRONG_ERROR'
    }
    'RegisterOnexit' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
    }
    'RegisterOnexitDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
    }
    'Malloc' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE_EX'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
        $gccArguments += '-DGXOS_ENABLE_CRT_MALLOC'
    }
    'MallocDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE_EX'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
    }
    'VectoredExceptionHandler' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE_EX'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
        $gccArguments += '-DGXOS_ENABLE_CRT_MALLOC'
        $gccArguments += '-DGXOS_ENABLE_VECTORED_EXCEPTION_HANDLER'
    }
    'VectoredExceptionHandlerDisabled' {
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
        $gccArguments += '-DGXOS_ENABLE_SLIST'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
        $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
        $gccArguments += '-DGXOS_ENABLE_GETENV'
        $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
        $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
        $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
        $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
        $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE_EX'
        $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
        $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
        $gccArguments += '-DGXOS_ENABLE_CRT_MALLOC'
    }
}
if ($Scenario -eq 'GlobalMemoryStatusEx' -or
    $Scenario -eq 'VirtualMemory' -or
    $Scenario -eq 'GlobalMemoryStatusExDisabled' -or
    $Scenario -eq 'NativeAotEventWait') {
    $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
    $gccArguments += '-DGXOS_ENABLE_SLIST'
    $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
    $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
    $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
    $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
    $gccArguments += '-DGXOS_ENABLE_GETENV'
    $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
    $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
    $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
    $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
    $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
    $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
    $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
    $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE_EX'
    $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
    $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
    $gccArguments += '-DGXOS_ENABLE_CRT_MALLOC'
    $gccArguments += '-DGXOS_ENABLE_VECTORED_EXCEPTION_HANDLER'
    $gccArguments += '-DGXOS_ENABLE_CREATE_EVENT_W'
    $gccArguments += $schedulerSource
    $gccArguments += $schedulerAssembly
    $gccArguments += $createEventSource
    $gccArguments += '-DGXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION'
    $gccArguments += $createMemoryResourceNotificationSource
    $gccArguments += '-DGXOS_ENABLE_CREATE_THREAD'
    $gccArguments += $createThreadSource
    $gccArguments += $createThreadEntryAssembly
    $gccArguments += '-DGXOS_ENABLE_SET_THREAD_PRIORITY'
    $gccArguments += $setThreadPrioritySource
    $gccArguments += $setThreadPriorityEntryAssembly
    $gccArguments += '-DGXOS_ENABLE_RESUME_THREAD'
    $gccArguments += (Join-Path $root 'src\Gate4Harness\resume_thread_entry.S')
    $gccArguments += '-DGXOS_ENABLE_IS_PROCESS_IN_JOB'
    $gccArguments += $isProcessInJobEntryAssembly
    if ($Scenario -eq 'GlobalMemoryStatusEx' -or $Scenario -eq 'VirtualMemory' -or
        $Scenario -eq 'NativeAotEventWait') {
        $gccArguments += '-DGXOS_ENABLE_GLOBAL_MEMORY_STATUS_EX'
    }
}
if ($Scenario -eq 'Malloc' -or $Scenario -eq 'VectoredExceptionHandler' -or
    $Scenario -eq 'VectoredExceptionHandlerDisabled' -or
    $Scenario -eq 'CreateEventW' -or $Scenario -eq 'CreateEventWDisabled' -or
    $Scenario -eq 'CreateMemoryResourceNotification' -or
    $Scenario -eq 'CreateMemoryResourceNotificationDisabled' -or
    $Scenario -eq 'CreateThread' -or $Scenario -eq 'CreateThreadDisabled' -or
    $Scenario -eq 'SetThreadPriority' -or $Scenario -eq 'SetThreadPriorityDisabled' -or
    $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
    $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled' -or
    $Scenario -eq 'GlobalMemoryStatusEx' -or $Scenario -eq 'GlobalMemoryStatusExDisabled' -or
    $Scenario -eq 'VirtualMemory' -or $Scenario -eq 'NativeAotEventWait') {
    $gccArguments += (Join-Path $root 'src\Gate4Harness\crt_malloc.c')
}
if ($Scenario -eq 'VirtualMemory' -or $Scenario -eq 'NativeAotEventWait') {
    $gccArguments += '-DGXOS_ENABLE_VIRTUAL_MEMORY'
    $gccArguments += '-DGXOS_ENABLE_PROCESSOR_TOPOLOGY'
}
if ($Scenario -eq 'NativeAotEventWait') {
    $gccArguments += '-DGXOS_ENABLE_NATIVEAOT_EVENT_WAIT'
    $gccArguments += $eventApiSource
    $gccArguments += $standardHandleSource
    $gccArguments += $writeFileSource
    $gccArguments += $writeFileEntryAssembly
    $gccArguments += $comApiSource
    $gccArguments += $multibyteSource
    $gccArguments += $multibyteAssembly
}
if ($EnableNativeAotStartup) { $gccArguments += '-DGXOS_ENABLE_NATIVEAOT_STARTUP' }
if ($EnableNativeAotManagedCallback) { $gccArguments += '-DGXOS_ENABLE_NATIVEAOT_MANAGED_CALLBACK' }
if ($EnableNativeAotSchedulerCallback) { $gccArguments += '-DGXOS_ENABLE_NATIVEAOT_SCHEDULER_CALLBACK' }
if ($AssumeUnspecifiedTimezoneUtc) { $gccArguments += '-DGXOS_ASSUME_UNSPECIFIED_TIMEZONE_UTC' }
if ($Scenario -eq 'SyntheticScheduler') {
    $gccArguments += '-DGXOS_ENABLE_SYNTHETIC_SCHEDULER_PROOF'
    $gccArguments += $schedulerSource
    $gccArguments += $schedulerAssembly
    $gccArguments += $schedulerProofSource
}
if ($Scenario -eq 'CreateEventW' -or $Scenario -eq 'CreateEventWDisabled' -or
    $Scenario -eq 'CreateMemoryResourceNotification' -or
    $Scenario -eq 'CreateMemoryResourceNotificationDisabled' -or
    $Scenario -eq 'CreateThread' -or $Scenario -eq 'CreateThreadDisabled' -or
    $Scenario -eq 'SetThreadPriority' -or $Scenario -eq 'SetThreadPriorityDisabled' -or
    $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
    $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled') {
    $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT'
    $gccArguments += '-DGXOS_ENABLE_SLIST'
    $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM_E'
    $gccArguments += '-DGXOS_ENABLE_CRT_INITTERM'
    $gccArguments += '-DGXOS_ENABLE_CRT_STRCMP'
    $gccArguments += '-DGXOS_ENABLE_CRT_STRLEN'
    $gccArguments += '-DGXOS_ENABLE_GETENV'
    $gccArguments += '-DGXOS_ENABLE_CRT_STRICMP'
    $gccArguments += '-DGXOS_ENABLE_SYSTEM_INFO'
    $gccArguments += '-DGXOS_ENABLE_NUMA_HIGHEST_NODE'
    $gccArguments += '-DGXOS_ENABLE_PROCESS_GROUP_AFFINITY'
    $gccArguments += '-DGXOS_ENABLE_PROCESS_AFFINITY'
    $gccArguments += '-DGXOS_ENABLE_QUERY_INFORMATION_JOB_OBJECT'
    $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE'
    $gccArguments += '-DGXOS_ENABLE_GET_MODULE_HANDLE_EX'
    $gccArguments += '-DGXOS_ENABLE_GET_PROC_ADDRESS'
    $gccArguments += '-DGXOS_ENABLE_CRT_ONEXIT_REGISTER'
    $gccArguments += '-DGXOS_ENABLE_CRT_MALLOC'
    $gccArguments += '-DGXOS_ENABLE_VECTORED_EXCEPTION_HANDLER'
    if ($Scenario -eq 'CreateEventW' -or
        $Scenario -eq 'CreateMemoryResourceNotification' -or
        $Scenario -eq 'CreateMemoryResourceNotificationDisabled' -or
        $Scenario -eq 'CreateThread' -or $Scenario -eq 'CreateThreadDisabled' -or
        $Scenario -eq 'SetThreadPriority' -or $Scenario -eq 'SetThreadPriorityDisabled' -or
        $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
        $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled' -or
        $Scenario -eq 'NativeAotEventWait') {
        $gccArguments += '-DGXOS_ENABLE_CREATE_EVENT_W'
        $gccArguments += $schedulerSource
        $gccArguments += $schedulerAssembly
        $gccArguments += $createEventSource
        if ($Scenario -eq 'NativeAotEventWait') {
            $gccArguments += $eventApiSource
        }
    }
    if ($Scenario -eq 'CreateMemoryResourceNotification' -or
        $Scenario -eq 'CreateThread' -or $Scenario -eq 'CreateThreadDisabled' -or
        $Scenario -eq 'SetThreadPriority' -or $Scenario -eq 'SetThreadPriorityDisabled' -or
        $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
        $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled') {
        $gccArguments += '-DGXOS_ENABLE_CREATE_MEMORY_RESOURCE_NOTIFICATION'
        $gccArguments += $createMemoryResourceNotificationSource
    }
    if ($Scenario -eq 'CreateThread' -or $Scenario -eq 'SetThreadPriority' -or
        $Scenario -eq 'SetThreadPriorityDisabled' -or
        $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
        $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled') {
        $gccArguments += '-DGXOS_ENABLE_CREATE_THREAD'
        $gccArguments += $createThreadSource
        $gccArguments += $createThreadEntryAssembly
    }
    if ($Scenario -eq 'SetThreadPriority' -or
        $Scenario -eq 'ResumeThread' -or $Scenario -eq 'ResumeThreadDisabled' -or
        $Scenario -eq 'IsProcessInJob' -or $Scenario -eq 'IsProcessInJobDisabled') {
        $gccArguments += '-DGXOS_ENABLE_SET_THREAD_PRIORITY'
        $gccArguments += $setThreadPrioritySource
        $gccArguments += $setThreadPriorityEntryAssembly
    }
    if ($Scenario -eq 'ResumeThread' -or
        $Scenario -eq 'IsProcessInJob' -or
        $Scenario -eq 'IsProcessInJobDisabled') {
        $gccArguments += '-DGXOS_ENABLE_RESUME_THREAD'
        $gccArguments += (Join-Path $root 'src\Gate4Harness\resume_thread_entry.S')
    }
}

if ($Scenario -eq 'IsProcessInJob') {
    $gccArguments += '-DGXOS_ENABLE_IS_PROCESS_IN_JOB'
    $gccArguments += $isProcessInJobEntryAssembly
}

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

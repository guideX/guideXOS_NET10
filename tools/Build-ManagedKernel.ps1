[CmdletBinding()]
param(
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'artifacts\managed-kernel'
}
$out = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $root 'src\ManagedKernel\ManagedKernel.csproj'
$publish = Join-Path $out 'publish'
$binlog = Join-Path $out 'managed-kernel.binlog'
$dotnet = Get-Command dotnet -ErrorAction Stop
$sdkDirectory = Join-Path (Split-Path -Parent $dotnet.Source) 'sdk\10.0.400'
$msbuild = Join-Path $sdkDirectory 'MSBuild.dll'
if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "The installed .NET 10.0.400 MSBuild entry point is missing: $msbuild"
}

New-Item -ItemType Directory -Force -Path $out,$publish | Out-Null

# The repository pins 10.0.302 for the accepted foundation.  This machine
# currently has 10.0.400, so invoke from the parent of the checkout to use the
# installed fallback without changing global.json or the foundation project.
$parent = Split-Path -Parent $root
Push-Location $parent
$previousDotnetHostPath = $env:DOTNET_HOST_PATH
try {
    $env:DOTNET_HOST_PATH = $dotnet.Source
    $sdkVersion = '10.0.400 (installed fallback; MSBuild 18.9.6 direct entry point)'
    $sdkInfo = & dotnet $msbuild '/version' 2>&1
    $sdkVersion | Set-Content -LiteralPath (Join-Path $out 'dotnet-version.log')
    $sdkInfo | Set-Content -LiteralPath (Join-Path $out 'dotnet-info.log')
    $restoreArguments = @(
        $msbuild, $project, '/t:Restore', '/p:Configuration=Release',
        '/p:RuntimeIdentifier=win-x64', '/p:SelfContained=true')
    & $dotnet.Source @restoreArguments 1> (Join-Path $out 'restore.stdout.log') 2> (Join-Path $out 'restore.stderr.log')
    if ($LASTEXITCODE -ne 0) {
        throw "ManagedKernel restore failed (exit $LASTEXITCODE)."
    }
    $arguments = @(
        $msbuild, $project, '/t:Publish', '/p:Configuration=Release',
        '/p:RuntimeIdentifier=win-x64', '/p:SelfContained=true',
        '/p:PublishAot=true', '/p:OutputType=Library', '/p:NativeLib=Shared',
        "/p:PublishDir=$publish\", "/bl:$binlog")
    & $dotnet.Source @arguments 1> (Join-Path $out 'publish.stdout.log') 2> (Join-Path $out 'publish.stderr.log')
    if ($LASTEXITCODE -ne 0) {
        throw "ManagedKernel NativeAOT publish failed (exit $LASTEXITCODE)."
    }
} finally {
    $env:DOTNET_HOST_PATH = $previousDotnetHostPath
    Pop-Location
}

$payload = Join-Path $publish 'gxos-managed-kernel.dll'
if (-not (Test-Path -LiteralPath $payload)) {
    throw "ManagedKernel payload was not emitted: $payload"
}
$hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash.ToUpperInvariant()
$size = (Get-Item -LiteralPath $payload).Length
$objdump = Get-Command objdump -ErrorAction Stop
$pe = & $objdump.Source '-p' $payload 2>&1
if ($LASTEXITCODE -ne 0) { throw 'objdump could not inspect the ManagedKernel payload.' }
$pe | Set-Content -LiteralPath (Join-Path $out 'managed-kernel-pe-report.txt')
$exports = @($pe | Select-String -Pattern 'ManagedMain|GxManagedKernelInitialize|GxManagedKernelInstallBootResources|GxManagedQueryBootResources|GxManagedQueryMemoryRegion|GxManagedQuerySystemInfo|GxManagedKernelInstallHostServices|GxManagedKernelInstallEntropyServices|GxManagedKernelInstallMemoryServices|GxManagedKernelInstallDeviceInventory|GxManagedQueryDeviceInventorySummary|GxManagedQueryDevice|GxManagedKernelInstallDeviceResources|GxManagedQueryDeviceResourceSummary|GxManagedQueryDeviceResource|GxManagedKernelInstallMmioServices|GxManagedKernelInstallPciServices|GxManagedKernelInstallDmaServices|GxManagedKernelStart|GxManagedKernelRunPhase4|GxManagedKernelRunPhase5|GxManagedKernelRunPhase6|GxManagedKernelRunPhase7|GxManagedKernelRunPhase13|GxManagedKernelRunPhase14|GxManagedKernelInstallSerialServices|GxManagedKernelRunPhase8|GxManagedKernelInstallInterruptServices|GxManagedKernelInstallInputServices|GxManagedKernelRunPhase9|GxManagedKernelRunDriverWorker|GxManagedKernelRunPhase10|GxManagedKernelRunPhase11|GxManagedKernelRunPhase12|GxManagedKernelRunPhase25' | ForEach-Object { $_.Line.Trim() })
$exports | Set-Content -LiteralPath (Join-Path $out 'managed-kernel-exports.txt')

Write-Output "MANAGED_KERNEL_PROJECT=$project"
Write-Output "MANAGED_KERNEL_TOOLCHAIN_SDK=$sdkVersion"
Write-Output "MANAGED_KERNEL_PAYLOAD=$payload"
Write-Output "MANAGED_KERNEL_PAYLOAD_SIZE=$size"
Write-Output "MANAGED_KERNEL_PAYLOAD_SHA256=$hash"
Write-Output 'MANAGED_KERNEL_EXPORTS=ManagedMain,GxManagedKernelInitialize,GxManagedKernelInstallBootResources,GxManagedQueryBootResources,GxManagedQueryMemoryRegion,GxManagedQuerySystemInfo,GxManagedKernelInstallHostServices,GxManagedKernelInstallEntropyServices,GxManagedKernelInstallMemoryServices,GxManagedKernelInstallDeviceInventory,GxManagedQueryDeviceInventorySummary,GxManagedQueryDevice,GxManagedKernelInstallDeviceResources,GxManagedQueryDeviceResourceSummary,GxManagedQueryDeviceResource,GxManagedKernelInstallMmioServices,GxManagedKernelInstallPciServices,GxManagedKernelInstallDmaServices,GxManagedKernelStart,GxManagedKernelRunPhase4,GxManagedKernelRunPhase5,GxManagedKernelRunPhase6,GxManagedKernelRunPhase7,GxManagedKernelRunPhase7Accounting,GxManagedKernelRunPhase13,GxManagedKernelRunPhase14,GxManagedKernelInstallSerialServices,GxManagedKernelRunPhase8Accounting,GxManagedKernelRunPhase8,GxManagedKernelInstallInterruptServices,GxManagedKernelInstallInputServices,GxManagedKernelRunPhase9,GxManagedKernelRunDriverWorker,GxManagedKernelRunPhase10,GxManagedKernelRunPhase11,GxManagedKernelRunPhase12,GxManagedKernelRunPhase25'

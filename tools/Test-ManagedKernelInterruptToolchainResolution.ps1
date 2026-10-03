[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-ManagedKernelHostToolchain.ps1')
$assertions = 0
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "gxos-interrupt-sdk-resolution-$([guid]::NewGuid().ToString('N'))"

function Assert-Phase79Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )
    $script:assertions++
    if (-not $Condition) { throw $Message }
}

function Add-FakeSdk {
    param(
        [string]$Root,
        [string]$Version,
        [switch]$OmitCompiler
    )
    $sdkDirectory = Join-Path (Join-Path $Root 'sdk') $Version
    $compilerDirectory = Join-Path $sdkDirectory 'Roslyn\bincore'
    New-Item -ItemType Directory -Force -Path $compilerDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $sdkDirectory 'MSBuild.dll') -Value 'fixture'
    if (-not $OmitCompiler) {
        Set-Content -LiteralPath (Join-Path $compilerDirectory 'csc.dll') -Value 'fixture'
    }
    return $sdkDirectory
}

try {
    $fakeDotNet = Join-Path $tempRoot 'dotnet.exe'
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    Set-Content -LiteralPath $fakeDotNet -Value 'fixture'
    [void](Add-FakeSdk -Root $tempRoot -Version '10.0.400')
    [void](Add-FakeSdk -Root $tempRoot -Version '10.0.401')
    [void](Add-FakeSdk -Root $tempRoot -Version '10.0.402' -OmitCompiler)
    [void](Add-FakeSdk -Root $tempRoot -Version '10.1.100')
    [void](Add-FakeSdk -Root $tempRoot -Version '11.0.100')

    $automatic = Resolve-ManagedKernelHostToolchain -DotNetPath $fakeDotNet
    Assert-Phase79Condition ($automatic.SdkVersion -eq '10.0.401') `
        'automatic discovery did not select the newest complete compatible .NET 10.0.x SDK'
    Assert-Phase79Condition ($automatic.SelectionSource -eq 'automatic discovery') `
        'automatic discovery did not identify its selection source'
    Assert-Phase79Condition ($automatic.CompilerPath -like '*10.0.401*\Roslyn\bincore\csc.dll') `
        'automatic discovery returned an unexpected Roslyn compiler path'

    $olderSdk = Join-Path (Join-Path $tempRoot 'sdk') '10.0.400'
    $explicit = Resolve-ManagedKernelHostToolchain -DotNetPath $fakeDotNet `
        -SdkDirectoryOverride $olderSdk
    Assert-Phase79Condition ($explicit.SdkVersion -eq '10.0.400' -and
        $explicit.SelectionSource -eq 'explicit override') `
        'a valid explicit compatible SDK override was not selected'

    $incompatible = Join-Path (Join-Path $tempRoot 'sdk') '11.0.100'
    $invalidOverrideError = ''
    try {
        [void](Resolve-ManagedKernelHostToolchain -DotNetPath $fakeDotNet `
            -SdkDirectoryOverride $incompatible)
    } catch {
        $invalidOverrideError = $_.Exception.Message
    }
    Assert-Phase79Condition ($invalidOverrideError -like '*not a compatible .NET 10.0.x SDK*') `
        'an incompatible explicit override was not rejected with a clear message'

    $emptyRoot = Join-Path $tempRoot 'empty-install'
    New-Item -ItemType Directory -Force -Path $emptyRoot | Out-Null
    $emptyDotNet = Join-Path $emptyRoot 'dotnet.exe'
    Set-Content -LiteralPath $emptyDotNet -Value 'fixture'
    $missingSdkError = ''
    try {
        [void](Resolve-ManagedKernelHostToolchain -DotNetPath $emptyDotNet)
    } catch {
        $missingSdkError = $_.Exception.Message
    }
    Assert-Phase79Condition ($missingSdkError -like '*No compatible installed .NET 10.0.x SDK*') `
        'missing compatible SDKs did not produce a clear failure'

    Write-Output "MANAGED_KERNEL_INTERRUPT_TOOLCHAIN_TESTS=PASSED assertions=$assertions"
} finally {
    $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ((Test-Path -LiteralPath $resolvedTempRoot -PathType Container) -and
        $resolvedTempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}

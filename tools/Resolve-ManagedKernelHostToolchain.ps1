function Resolve-ManagedKernelHostToolchain {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$DotNetPath,
        [string]$SdkDirectoryOverride = ''
    )

    if (-not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
        throw "The dotnet host does not exist: $DotNetPath"
    }
    $dotnet = (Resolve-Path -LiteralPath $DotNetPath).Path
    $compatibleSdkPattern = '^10\.0\.\d+$'

    if (-not [string]::IsNullOrWhiteSpace($SdkDirectoryOverride)) {
        $sdkDirectory = [IO.Path]::GetFullPath($SdkDirectoryOverride)
        if (-not (Test-Path -LiteralPath $sdkDirectory -PathType Container)) {
            throw "The requested .NET SDK directory override does not exist: $sdkDirectory"
        }
        $sdkVersion = Split-Path -Leaf $sdkDirectory
        if ($sdkVersion -notmatch $compatibleSdkPattern) {
            throw "The requested SDK directory is not a compatible .NET 10.0.x SDK: $sdkDirectory"
        }
        $msbuild = Join-Path $sdkDirectory 'MSBuild.dll'
        $compiler = Join-Path $sdkDirectory 'Roslyn\bincore\csc.dll'
        if (-not (Test-Path -LiteralPath $msbuild -PathType Leaf)) {
            throw "The requested .NET SDK directory is missing MSBuild.dll: $msbuild"
        }
        if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
            throw "The requested .NET SDK directory is missing the Roslyn compiler: $compiler"
        }
        return [pscustomobject]@{
            DotNetPath = $dotnet
            SdkVersion = $sdkVersion
            SdkDirectory = $sdkDirectory
            MSBuildPath = $msbuild
            CompilerPath = $compiler
            SelectionSource = 'explicit override'
        }
    }

    $sdkRoot = Join-Path (Split-Path -Parent $dotnet) 'sdk'
    $candidates = @(Get-ChildItem -LiteralPath $sdkRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $compatibleSdkPattern } |
        Sort-Object { [version]$_.Name } -Descending)
    foreach ($candidate in $candidates) {
        $msbuild = Join-Path $candidate.FullName 'MSBuild.dll'
        $compiler = Join-Path $candidate.FullName 'Roslyn\bincore\csc.dll'
        if ((Test-Path -LiteralPath $msbuild -PathType Leaf) -and
            (Test-Path -LiteralPath $compiler -PathType Leaf)) {
            return [pscustomobject]@{
                DotNetPath = $dotnet
                SdkVersion = $candidate.Name
                SdkDirectory = $candidate.FullName
                MSBuildPath = $msbuild
                CompilerPath = $compiler
                SelectionSource = 'automatic discovery'
            }
        }
    }

    throw "No compatible installed .NET 10.0.x SDK with MSBuild.dll and Roslyn csc.dll was found under $sdkRoot"
}

# Build and toolchain record

This record describes the first reproducible host-side NativeAOT artifact. It does not claim that the Windows-targeted artifact is already freestanding.

## Baseline

The new repository was clean before implementation work at commit `21ffe77` on branch `main`. Only this repository was changed. The legacy, UEFI, Server, and older NativeAOT repositories were inspected as read-only evidence.

## Toolchain identity

| Item | Value |
| --- | --- |
| SDK | .NET SDK `10.0.302`, commit `35b593bebf`; MSBuild `18.6.11` |
| Runtime | .NET `10.0.10` |
| Runtime pack | `Microsoft.NETCore.App.Runtime.NativeAOT.win-x64` `10.0.10` |
| ILCompiler | `Microsoft.DotNet.ILCompiler` `10.0.10` |
| Target framework | `net10.0` |
| Target architecture/RID | x64 / `win-x64` |
| Native linker | MSVC `link.exe` from Visual Studio `18`, toolset `14.51.36231` |
| Binary inspection | MinGW-w64 `objdump`, `nm` |
| Freestanding harness compiler | MinGW-w64 GCC |
| Emulator | QEMU `11.0.0 (v11.0.0-12122-ga4bb4b10c9)` |

The SDK is pinned by [global.json](../global.json). The project is [ManagedEntryProbe.csproj](../src/ManagedEntryProbe/ManagedEntryProbe.csproj), and the managed method is [ManagedEntry.cs](../src/ManagedEntryProbe/ManagedEntry.cs).

## Reproduction

From the repository root:

```powershell
dotnet --version
dotnet publish src\ManagedEntryProbe\ManagedEntryProbe.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishAot=true `
  -p:PublishDir="$PWD\artifacts\gate1-repro\exe\" `
  -bl:"$PWD\artifacts\gate1-repro\exe.binlog"

dotnet publish src\ManagedEntryProbe\ManagedEntryProbe.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishAot=true `
  -p:OutputType=Library -p:NativeLib=Shared `
  -p:PublishDir="$PWD\artifacts\gate1-repro\shared\" `
  -bl:"$PWD\artifacts\gate1-repro\shared.binlog"

dotnet publish src\ManagedEntryProbe\ManagedEntryProbe.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishAot=true `
  -p:OutputType=Library -p:NativeLib=Static `
  -p:PublishDir="$PWD\artifacts\gate1-repro\static\" `
  -bl:"$PWD\artifacts\gate1-repro\static.binlog"
```

The equivalent isolated three-form build is [Build-Gate1.ps1](../tools/Build-Gate1.ps1). It records `dotnet --info`, binlogs, stdout/stderr, and per-output hashes.

The project deliberately sets invariant globalization and timezone, disables debugger/EventPipe/stack-trace support, uses workstation/non-concurrent GC settings, enables trimming analysis, and passes `/Brepro` to the Windows linker. It does not add a custom runtime, CoreLib, ILCompiler fork, linker stub library, or host console call.

The complete intentional project property set is:

```text
TargetFramework=net10.0
RuntimeIdentifier=win-x64
OutputType=Exe (overridden to Library for Shared/Static forms)
PublishAot=true; SelfContained=true; InvariantGlobalization=true
OptimizationPreference=Size; IlcGenerateMapFile=true; StripSymbols=false
DebugType=embedded; DebugSymbols=true; EnableTrimAnalyzer=true; TrimMode=partial
IlcScanReflection=false; StackTraceSupport=false; EventSourceSupport=false
DebuggerSupport=false; InvariantTimezone=true; ServerGarbageCollection=false
ConcurrentGarbageCollection=false; Deterministic=true
ContinuousIntegrationBuild=true; AllowUnsafeBlocks=true; Nullable=enable
ImplicitUsings=disable; PathMap=$(MSBuildProjectDirectory)=/gxos/src/ManagedEntryProbe
RootNamespace=GuideXOS.Net10.ManagedEntryProbe; AssemblyName=gxos-managed-entry-probe
LinkerArg=/Brepro; NativeLib=Shared or Static for library forms
```

## Historical Gate 1 reproducibility result

The following records describe the earlier no-allocation/pre-callback Gate 1
baseline. They are retained as historical evidence; the current merge-gate
payload is the 730,112-byte managed-entry/callback/GC artifact with SHA-256
`AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`.

Two independent `/Brepro` executable publishes produced the same SHA-256:

```text
230CEBD7158AD164331DB488A3E19C6189DA63C4D186EFB219BA540D5BFDF3D9
```

The matching PDB hash was:

```text
E6FD1FB689D97FE7A628482DC5C8B7D93555C8D43463E5E8FFAFEB1E52917BA5
```

Recorded artifacts:

| Artifact | Size | SHA-256 |
| --- | ---: | --- |
| `gxos-managed-entry-probe.exe` | 732,672 | `230CEBD7158AD164331DB488A3E19C6189DA63C4D186EFB219BA540D5BFDF3D9` |
| `gxos-managed-entry-probe.dll` shared form | 729,600 | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| `gxos-managed-entry-probe.lib` static form | 2,479,220 | `96D0D421E70C99CCA128D066A573C7EFB97B740217C774C8839F5AC233BBAEB6` |
| NativeAOT map XML | 650,200 | `E38DB968C40F19F427D4AEF64D7BF5B19E3E16B3010F8DA83FD07CFB449899FC` |
| ILC response | 32,127 | `7E33D44C6E1ECF354F732A56565521DD87C086A194C557641882B4FE4232BF85` |
| linker response | 3,052 | `4A0B63F84FA712D4C30556C532C6F1F62C825257B5C78B45DDFBE5C6605C704A` |

The executable ran in a fresh Windows host process and returned `0`. That is Gate 1 evidence only; it is not freestanding evidence.

## Current Gate 4 reproducibility audit

The Gate 4 build script passes GNU `ld`'s `--no-insert-timestamp` option. Two
independent callback/GC harness builds therefore produced the same 534,299-byte
loader with SHA-256
`9E78E7145C8BB3AC8E5559C4347275EFDC160FC062088769C15A335E5E6D1601` and staged
the same 730,112-byte payload with SHA-256
`AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`.

## Negative cross-OS check

Publishing the same source with `-r linux-x64` restored the managed project but stopped before native code generation with:

```text
Cross-OS native compilation is not supported.
```

The current Windows environment therefore cannot produce a Linux ELF NativeAOT artifact through the standard Windows SDK invocation. This is one reason the first image-format experiment stayed with PE/COFF.

## Inspection commands

The exact compiler/linker inputs are retained in the ILC and linker response files under `src\ManagedEntryProbe\obj\Release\net10.0\win-x64\native`. Useful checks are:

```powershell
objdump -p artifacts\gate1-brepro-1\gxos-managed-entry-probe.exe
objdump -p artifacts\gate1-brepro-shared\gxos-managed-entry-probe.dll
nm -u src\ManagedEntryProbe\obj\Release\net10.0\win-x64\native\gxos-managed-entry-probe.obj
```

The output is interpreted in [NativeAOT artifact anatomy](NATIVEAOT_ARTIFACT_ANATOMY.md) and [Dependency census](DEPENDENCY_CENSUS.md), rather than treated as a build-only success.

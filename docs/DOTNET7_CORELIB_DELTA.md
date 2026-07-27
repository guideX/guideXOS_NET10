# .NET 7 CoreLib/runtime delta

Audit scope: Legacy HEAD `4ff1ac4`, UEFI HEAD `65178e8`, and the local package artifacts in both repositories.

## Bottom line

Neither Legacy nor UEFI contains a full `System.Private.CoreLib` source copy, a complete CoreCLR/runtime source tree, a runtime pack source tree, linker descriptors, or a patch series against the .NET repository. Each contains a curated 119-file source subset that resembles the NativeAOT/CoreRT `System.Private.CoreLib` surface and is compiled through shared-project imports.

This distinction matters. The repositories did not replace .NET 7 CoreLib in the same way a maintained runtime fork would. They supplied selected types, runtime layout structures, compiler helper exports, and guideXOS-specific substitutions that were sufficient for the particular NativeAOT proof.

## Evidence and provenance

- `Corlib\Corlib.projitems` in both repositories enumerates the source subset.
- The subset includes `Internal\Runtime\CompilerHelpers`, `Internal\Runtime\EEType.cs`, `ModuleHeaders.cs`, `RuntimeConstants.cs`, selected `System.*` types, `RuntimeExportAttribute`, and selected interop/compiler-service attributes.
- Both repositories carry the same local packages: `microsoft.dotnet.ilcompiler.7.0.0-alpha.1.22074.1.nupkg` and `runtime.win-x64.microsoft.dotnet.ilcompiler.7.0.0-alpha.1.22074.1.nupkg`.
- The package nuspec identifies the compiler as `Microsoft.DotNet.ILCompiler` `7.0.0-alpha.1.22074.1`, from `dotnet/runtimelab` commit `04b092003db5ba207d4fa3f3becb7f01828bf16c`.
- `packages\README.md` explicitly says the compiler package is modified and documents edits to `build\Microsoft.NETCore.Native.Windows.props`.
- A normalized Legacy/UEFI comparison found no meaningful content split across the 119-file set except the UEFI `System\Windows\Forms\Control.cs` addition and a project-file change; most raw differences are line endings. UEFI history shows the CoreLib set was initially added as a snapshot and only `Control.cs` was later changed.
- Legacy history shows the meaningful evolution: broad initial source import, later additions of `IO`, `TextReader`, and `ArrayPool`, and a later removal of `ArrayPool`, `StreamReader`, and `TextReader` while runtime helpers and many types were edited.

## Meaningful changes and classifications

| File/component | Repository | Category | Evidence / likely reason | Confidence | .NET 10 relevance |
|---|---|---|---|---|---|
| `Corlib\Corlib.projitems` | Legacy + UEFI | Removed/excluded; build adaptation | Only 119 selected files are compiled; no full CoreLib closure is present. The project deliberately assembles a small surface. | High | Re-derive the closure from .NET 10 NativeAOT; do not copy the list. |
| `System\Runtime\RuntimeExportAttribute.cs` | Legacy + UEFI | Runtime/compiler intrinsic | Defines the attribute used by `Entry`, `KMain`, `Rhp*`, and type-cast exports. | High | Preserve the concept only if .NET 10’s compiler contract requires a compatible export mechanism. |
| `Internal\Runtime\CompilerHelpers\StartupCodeHelpers.cs` | Legacy + UEFI, with later Legacy edits | Reimplemented for guideXOS / runtime intrinsic | Exports `memset`, `memcpy`, fail-fast, P/Invoke transition placeholders, `RhpNewFast`, `RhpNewArray`, reference assignment, type tests, and module/static initialization. | High | Runtime internals and symbols are version-sensitive; replace with a .NET 10-specific integration layer. |
| `RhpNewFast` / `RhpNewArray` in `StartupCodeHelpers.cs` | Legacy + UEFI | Reimplemented; unsafe runtime hook | Calls an imported `malloc`, writes an EEType pointer and array length directly, and zeroes memory. | High | Do not port object layout blindly. First prove the .NET 10 object/EEType contract with a controlled runtime pack. |
| `InitializeModules` in `StartupCodeHelpers.cs` | Legacy + UEFI | Reimplemented for guideXOS | Walks ReadyToRun/module sections, initializes GC statics and eager cctors, then initializes date tables. UEFI conditionally skips it because the bootloader path did not provide a compatible module pointer. | High | The module table and startup sections may differ in .NET 10; make the contract an explicit proof target. |
| `Internal\Runtime\CompilerHelpers\InteropHelpers.cs` | Legacy + UEFI, later Legacy edits | Reimplemented / stubbed | `ResolvePInvoke` emits an `int 0x80`-style call-through, ANSI conversion returns the original value, and `CoTaskMemFree` is a no-op. | High | Replace with a defined host-call/ABI boundary; do not retain an x86 syscall stub in x64 code. |
| `Internal\Runtime\CompilerHelpers\ArrayHelpers.cs` | Legacy + UEFI, later Legacy edits | Stubbed / partial reimplementation | Some multidimensional cases return `null` instead of throwing or creating the array; non-zero lower bounds are rejected by a commented-out throw. | High | Treat as a known unsupported surface, not as a .NET 10 behavior. |
| `System\Runtime\TypeCast.cs` and type-cast exports | Legacy + UEFI, later Legacy edits | Stubbed / reimplemented | Several invalid-cast paths return `null` where a runtime exception would normally be raised. | High | Rebuild against .NET 10 runtime exception and EEType rules. |
| `SynchronizedMethodHelpers.cs` | Legacy + UEFI | Stubbed | Monitor enter/exit helpers only set `lockTaken`; they do not implement mutual exclusion. | High | Must not be used as a synchronization implementation in the new runtime. |
| `ThrowHelpers.cs` | Legacy + UEFI | Reimplemented for guideXOS | Runtime throws route through `[DllImport("Error")]` and a guideXOS diagnostic function rather than normal managed exception construction. | High | Keep early-panic semantics separate from normal managed exceptions. |
| `System\Delegate.cs`, selected `Enum`, serialization, and reflection paths | Legacy + UEFI | Stubbed / reduced | The source contains default or `null` returns in runtime-sensitive paths; the curated set omits broad reflection/BCL support. | Medium | Re-test each needed feature; do not infer full BCL compatibility. |
| `System\Buffers\ArrayPool.cs`, `System\IO\StreamReader.cs`, `System\IO\TextReader.cs` | Legacy | Removed or excluded | Added in commit `9966778`, removed in commit `7439ab4`; the latter was a leak-fix/userland change. | High for removal; medium for exact cause | Do not reproduce the removal. Decide from the .NET 10 dependency graph and memory proof. |
| `System\IO.cs`, `UTF8.cs`, `StringPool.cs`, selected collections and drawing types | Legacy | Reimplemented / build-surface adaptation | Added or modified to support the existing kernel and userland without the standard library surface. | High | Reuse behavior only after a managed-entry proof. |
| `System\Windows\Forms\Control.cs` | UEFI | Reimplemented for guideXOS | UEFI adds a documented global mouse-state container; it is pure data and not firmware access. | High | Useful as a behavioral donor, not as a runtime dependency. |
| `System\Runtime\InteropServices\*` attributes | Legacy + UEFI | Unmodified upstream-shaped definitions plus build adaptation | The project needs compiler-recognized metadata while avoiding the full desktop BCL. | Medium | Prefer .NET 10 reference assemblies and compiler-supported attributes. |
| `guideXOS\guideXOS.csproj` `Compile Remove`, reference pruning, and target constants | Legacy + UEFI | Build-system adaptation | Removes `Portable\**`, clears reference paths, imports shared Kernel/Corlib, and defines `Kernel`, `NETWORK`, `X64`, and `UseAPIC`. | High | Replace with explicit project boundaries and target-specific runtime packs. |
| Local ILCompiler package `Microsoft.NETCore.Native.Windows.props` | Legacy + UEFI | Build-system adaptation | `packages\README.md` shows bootstrapper/runtime libraries, compression native library, and standard Windows system libraries commented out; `/ENTRY:$(EntryPointSymbol)` remains. | High | This is the key reason not to reuse the package. Use current .NET 10 NativeAOT targets and explicit native libraries. |
| Linker descriptors, substitutions, source patches outside `Corlib` | Legacy + UEFI | Not found / unknown | Searches found no dedicated descriptor or patch tree. Some behavior is embedded directly in source and project files. | High for “not found”; low for hidden upstream provenance | Audit the .NET 10 linker output only when the first sample publishes. |

## Why the old removals must not be repeated

The old project optimized for a fragile freestanding proof: it removed standard native dependencies, supplied selected runtime exports, and replaced unsupported runtime operations with direct memory access or diagnostics. Those changes may have been necessary for that exact ILCompiler snapshot and object layout. In .NET 10, the NativeAOT compiler, runtime pack, startup symbols, linker inputs, and GC/runtime contracts are different. The new project should port requirements and tests, not deletions.

## Recommended delta method for .NET 10

1. Start with a stock .NET 10 NativeAOT sample and record the produced module format, import set, runtime sections, and entry symbol.
2. Build a guideXOS runtime pack or overlay only after a missing symbol or unsupported platform contract is observed.
3. Keep each guideXOS runtime hook in a small, named patch/overlay with an upstream source revision and a proof test.
4. Maintain an explicit “not supported yet” list for GC, exceptions, reflection, threading, and P/Invoke rather than silently returning dummy values.


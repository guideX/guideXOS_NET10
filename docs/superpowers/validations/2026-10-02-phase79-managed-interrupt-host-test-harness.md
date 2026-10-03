# guideXOS C# .NET 10 — Phase 79 Managed Interrupt Host-Test Harness

Date: 2026-10-03

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

## Outcome

**Outcome A — the managed interrupt host-test harness is repaired and passes against the complete current ManagedKernel source set.** The runner now discovers the newest complete installed .NET 10.0.x SDK, accepts an explicit SDK directory override, verifies the actual source graph before building, and reports exact assertion totals. SDK selection and source completeness each have deterministic regression checks.

The phase changed only the host-test project, test reporting, runner, resolver/test scripts, and this report. Production source and the production project file are unchanged.

## Read-only preflight

| Field | Starting value |
|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` |
| Branch | `nativeaot-managed-kernel-integration` |
| HEAD | `fa300ce9091cb7489f5eb6a16507703787ceaf90` — `Document real-target UART authority limits` |
| Upstream | `origin/nativeaot-managed-kernel-integration` |
| Divergence | 1 ahead / 0 behind |
| Worktree | Clean |
| `global.json` | SDK `10.0.302`, `rollForward: disable` |

The live repository matched the supplied starting revision. The normal runner was executed before editing and failed at line 18. It required `C:\Program Files\dotnet\sdk\10.0.400\MSBuild.dll`, which was absent.

The original project passed to MSBuild was `src\ManagedKernelInterruptHostTests\ManagedKernelInterruptHostTests.csproj`.

`dotnet --list-sdks` reported only `10.0.401`. SDK-shaped directories `10.0.302` and `10.0.400` were also present under the SDK root but lacked `MSBuild.dll`; `10.0.401` had both MSBuild and Roslyn compiler entry points. The installed Windows SDK include tree contained `10.0.26100.0`.

Visual Studio Community 18 was found at `C:\Program Files\Microsoft Visual Studio\18\Community`, with its own `MSBuild.exe`. It was not used. The repository's `Build-ManagedKernel.ps1` convention discovers the newest `.NET 10.0.x` SDK beside the selected `dotnet` host and invokes that SDK's `MSBuild.dll` directly. Phase 79 follows that convention and runs outside the checkout so the older pinned `global.json` does not redirect SDK selection. No Visual Studio developer shell or machine-wide environment change was used. The managed `net10.0` host project does not consume native Windows SDK headers; the detected Windows SDK is informational.

## Two independent original failures

### Toolchain selection

The runner constructed the fixed path `C:\Program Files\dotnet\sdk\10.0.400\MSBuild.dll`. This was a .NET SDK/MSBuild path, despite the task description calling it a Windows SDK. The normal runner therefore failed before restore or project evaluation.

### Stale source list

After redirecting the preflight build to the installed `10.0.401` MSBuild, restore succeeded and compilation failed with `CS0246` errors from `ManagedKernel.cs`:

| Missing type | Current source file omitted by the old host project |
|---|---|
| `ManagedSecureRandom` | `ManagedSecureRandom.cs` |
| `ManagedEntropyService` | `ManagedSecureRandom.cs` |
| `ManagedE1000Driver` | `ManagedE1000Driver.cs` |

The five reported unresolved references were at `ManagedKernel.cs` lines 840, 841, 894, 933, and 934. The old host project linked 13 ManagedKernel source files. The production project evaluated 83 `Compile` files, leaving 70 current production inputs outside the standalone project. The first-failure evidence is in `artifacts\phase79-preflight-interrupt\build.stdout.log`.

These were test-infrastructure defects. No production runtime defect was found.

## Repair and ownership model

The production SDK-style project uses the standard recursive C# source glob. The interrupt host project now links `..\ManagedKernel\**\*.cs`, excluding that source tree's `bin` and `obj` generated outputs. This compiles the same 83 source files as the production project and the host test's own `Program.cs`, for 84 compile items total.

This is an explicit host-project source-root glob that mirrors the current production source graph. No common production source manifest existed, and no shared production manifest or production project was changed. A new source-completeness check asks MSBuild to evaluate both projects' `Compile` items, compares their full paths, and rejects omissions, unexpected files, and duplicate items. Future production source files under `src\ManagedKernel` enter the host project through the glob automatically; the comparison guards that relationship against later project drift.

The whole source set also compiles `ManagedSecureRandom.cs`, `ManagedE1000Driver.cs`, and all their transitive dependencies. It does not execute those unrelated service or hardware paths in the interrupt test.

No host shims were added or changed. Existing bounded test substitutes remain in `Program.cs`: the fake memory provider and the `UnmanagedCallersOnly` callbacks for the serial/interrupt native ABI. Production implementations were not replaced or weakened.

## Toolchain discovery and regression checks

`Resolve-ManagedKernelHostToolchain.ps1` selects the highest installed, complete version matching `10.0.x`, using the repository's existing SDK-directory/version-sort pattern. It requires both `MSBuild.dll` and `Roslyn\bincore\csc.dll`. SDKs from incompatible feature bands such as `10.1.x` or `11.x` are excluded.

The normal runner accepts `-SdkDirectory <path>` as a bounded override. The directory must be named as a compatible `10.0.x` SDK and contain both required compiler files. An invalid explicit override fails with a direct error and never falls back to automatic discovery.

The runner reports the selected `dotnet.exe`, `MSBuild.dll`, Roslyn compiler, .NET SDK version and path, selection source, and the detected Windows SDK include path. In this environment the automatic selection was .NET SDK `10.0.401`, MSBuild `18.9.11.42413`, and Roslyn compiler `C:\Program Files\dotnet\sdk\10.0.401\Roslyn\bincore\csc.dll`. MSBuild launched the SDK's `csc.exe` compiler host.

`Test-ManagedKernelInterruptToolchainResolution.ps1` passed six assertions using temporary mock SDK directories. It proved newest complete compatible SDK selection, skipped an incomplete newer SDK, valid explicit override selection, incompatible override rejection, and a clear no-compatible-SDK failure. It does not read or alter installed machine SDKs. A full runner invocation with the valid `10.0.401` override also passed. A full invocation using the incomplete `10.0.400` directory failed on missing `MSBuild.dll` without creating its output directory or falling back to `10.0.401`.

## Interrupt host-test inventory and intent

The suite is one host program with 15 assertions. All 15 ran; there are no disabled tests or skip paths. It covers:

| Category | Assertions | Behavior checked |
|---|---:|---|
| ABI and service validation | 3 | Exact managed layout; accepts valid versioned interrupt services; rejects nonzero reserved metadata. |
| Driver startup and subscription | 3 | Serial driver reaches started state; subscription binds the native callback token; duplicate subscription is rejected. |
| Managed event dispatch | 5 | Delivers valid input; rejects the wrong device identity; preserves sequence continuity; fails closed on a sequence gap; does not deliver after unsubscribe. |
| Bounded state and resources | 4 | Bounded statistics; runtime arena proof while subscribed; managed/native unsubscribe state clears together; teardown releases provider allocations. |

The native boundary is represented by explicit host callback functions, not real IRQ delivery or hardware. “Driver binding” here means the managed serial driver/subscription relationship exercised by this suite; it is not a PCI/E1000 hardware-start test. The E1000 implementation is compiled as part of the authoritative source graph but is not run by this interrupt-focused program.

The program now reports `assertions`, `passed`, `failed`, and `skipped` totals. No coverage checks were removed or replaced.

## Validation

| Validation | Result |
|---|---|
| `tools/Test-ManagedKernelInterruptToolchainResolution.ps1` | Pass — 6 assertions |
| `tools/Test-ManagedKernelInterruptHostSourceCompleteness.ps1` | Pass — 83 production source items equal the host project's production subset; 84 host items including `Program.cs`; 0 duplicates |
| `tools/Run-ManagedKernelInterruptHostTests.ps1` automatic discovery | Pass — .NET SDK 10.0.401; 15/15 assertions; 0 failed; 0 skipped; exit 0 |
| Runner with `-SdkDirectory C:\Program Files\dotnet\sdk\10.0.401` | Pass — override selected and 15/15 assertions passed |
| Runner with incomplete explicit 10.0.400 override | Rejected clearly, no fallback |
| Direct `ManagedKernelInterruptHostTests.csproj` rebuild and assembly execution | Pass — fresh `Rebuild` invoked the 10.0.401 Roslyn compiler; 15/15 assertions; exit 0 |
| Driver service-owner/status host tests | Pass — both suites |
| Managed driver-worker host tests | Pass — `WAKE_REQUESTS=4`, `DISPATCH_BATCHES=6`, `DELIVERED=8`, `REJECTED=10`, `DROPPED=1`, `YIELDS=2`, `REPEATED_WAKE_CYCLES=3` |
| Diagnostic host tests | Pass — diagnostic protocol, device resources, and diagnostic resource claim |
| Phase 61 managed-worker API host runner | Pass — Phase 61, 62, 64, 65, and 66 API markers passed |
| Ordinary ManagedKernel NativeAOT build | Pass — SDK 10.0.401; payload emitted |

The Phase 56–60 rollback host regressions were not run because Phase 79 did not change shared managed source manifests or production build inputs. No fresh QEMU run was required because production sources and production build configuration were unchanged. SyntheticScheduler was not required; the regular ManagedKernel NativeAOT build supplied the requested build sanity check for a standalone host-project/runner change.

The fresh direct host-project rebuild and ordinary production build each report the same existing `CS0169` warning at `ManagedKernel.cs(3730,31)` for `KernelLog.s_hexScratch`; both complete with zero errors. The final normal runner invocation was incremental and reported 0 warnings and 0 errors. Phase 79 changed no production source that could introduce the existing warning.

## Evidence and hashes

Key evidence is retained under ignored `artifacts` outputs:

* Original runner failure: `artifacts\phase79-preflight-interrupt\` was created for the manual 10.0.401 reproduction; `build.stdout.log` contains the first compile errors.
* Automatic runner: `artifacts\managed-kernel-interrupt-host-tests\` (`restore.*.log`, `build.*.log`, `run.*.log`).
* Explicit override runner: `artifacts\phase79-interrupt-override\`.
* Fresh direct project rebuild: `artifacts\phase79-direct-interrupt-project-rebuild\`.
* Production NativeAOT build: `artifacts\managed-kernel\` (`publish.*.log`, `dotnet-version.log`, `dotnet-info.log`).

| Artifact | SHA-256 |
|---|---|
| `ManagedKernelInterruptHostTests.dll` | `C35F098D90B1C7EEB286D3EDBC6BC4B44B561A0FEB75E4EED1720121FD575232` |
| `gxos-managed-kernel.dll` | `E9B0CB94CBC25238B68F695ACBBC783FA4DC2C0EFCEE3C967B2370BBFFF907B8` |

## Change boundary and follow-up

No production source, production compile manifest, NativeAOT configuration, remotes, credentials, Git identity, branch, or worktree was altered. The requested Phase 79 commit adds one new commit without rewriting existing history. There was no SDK installation, Visual Studio mutation, persistent PATH change, QEMU boot, UART discovery, ACPI work, or Phase 80 implementation.

The Phase 79 acceptance criterion is satisfied: the normal interrupt runner resolves a compatible installed SDK, compiles the current production source graph once, runs the interrupt assertions successfully, and has deterministic regression checks for both toolchain selection and source completeness.

The smallest Phase 80 step is to review the accepted Phase 78 evidence and select a new bounded objective before implementation. No Phase 80 work is started by this report.

# App Model Direction

Status: architecture recommendation grounded in the current guideXOS Server experiments. This document does not port the Server App Model.

## Evidence from guideXOS Server

The Server repository already provides useful vocabulary and several experiments:

- `app_manifest.h` models an application identity, display metadata, version, publisher, category, icon, supported architectures, entry points, permissions, file associations, and default-window hints.
- `app_launch_target.h` separates launch intent from dispatch and names built-in, manifest, native ELF, package, shell-action, legacy-alias, file-open, service, guest, and script targets.
- Hosted built-ins are represented by synthetic manifests and hard-coded dispatch. Bare-metal registration is handled separately by a kernel-side `AppManager`. This is useful evidence, but it is currently two registries rather than one finished cross-platform model.
- The `.gxapp` design uses a custom container with a metadata entry and architecture-specific payload entries. The current specification is version 1, has a custom magic, is not ZIP, has no compression or signatures, and is not yet a complete loader/install pipeline.
- The experimental native ELF path accepts only static amd64 `ET_EXEC` images without `PT_INTERP`, dynamic linking, or relocations, and gates calls through a versioned `guidexos-c-abi-v1` contract.

These experiments establish concepts, not compatibility guarantees. The new project should reuse identity and ABI vocabulary while keeping package loading and kernel/runtime integration behind explicit versioned boundaries.

## Recommended layering

```text
AppModel.Abstractions
    stable public identity, manifest, lifecycle, launch, window, logging, and capability types

AppModel.SDK
    managed application helpers and NativeAOT-friendly wrappers

AppHost
    package selection, lifecycle supervision, window/event bridge, capabilities, and diagnostics

Stable host ABI
    fixed-width versioned C-compatible calls and records

Kernel/runtime implementation
    private boot, memory, scheduling, graphics, storage, and NativeAOT details
```

The public SDK must not expose kernel pointers, framebuffer addresses, interrupt objects, scheduler types, CoreLib implementation types, or the layout of private runtime structures.

## Smallest subset that should influence the bootstrap

Only the following concepts need to shape early interfaces:

| Concept | Initial requirement | Deliberately deferred |
|---|---|---|
| Application ID | Stable, case-normalized package/application identity | Installer, publisher trust, update policy |
| Manifest | Schema version, ID, version, supported architecture, entry kind, display name | Full permissions, associations, theme metadata |
| Launch | Arguments and an opaque launch request | Restore, activation history, deep links |
| Lifecycle | Launching, Running, Stopping, Exited | Suspension, background quotas, crash recovery |
| Window | Request one basic window and receive an opaque handle | Multi-window policy, composition, decorations |
| Exit | Application exit code/reason | Restart policy and user-session policy |
| Logging | Structured level plus bounded text | Remote logging and persistent log policy |
| Files | Capability-checked read-only access to an explicitly granted path or package asset | General filesystem namespaces and write transactions |
| Capabilities | Query whether a named capability is present | Permission prompts and policy administration |

Built-in applications should go through the same launch resolver and lifecycle surface as packaged applications, even if their implementation is linked into the OS during the first experiments. A synthetic manifest is acceptable for a built-in; a second, incompatible launch API is not.

## Shared identity with guideXOS Server

Use the Server identity vocabulary as the compatibility starting point: application ID, display name, semantic version, publisher, architecture list, entry kind, and shared assets/manifest. A package intended for both systems should carry one identity and manifest with architecture-specific payload records rather than two independently named applications.

The `.gxapp` v1 container should be treated as an external compatibility target, not as an excuse to couple the kernel to the current implementation. For a future .NET 10 NativeAOT payload, add an explicit entry kind and metadata for:

- CPU architecture and ABI;
- payload format and preferred load contract;
- NativeAOT/runtime contract version;
- required App Model API version;
- shared asset references;
- capabilities requested by the app.

If v1 cannot express these fields without ambiguity, define a versioned extension or v2 and document the compatibility rule. Do not silently reinterpret an existing native payload entry.

## Stable host ABI

The first ABI should be intentionally small and C-compatible. Every record begins with a size and ABI version, uses fixed-width integer types, and treats handles as opaque integers. No C++ classes, managed object references, compiler-specific enums, or pointers to variable-layout structures cross the boundary.

The initial conceptual calls are:

```text
gx_app_get_api_version
gx_app_start(manifest/application identity, launch arguments)
gx_app_request_window(window description)
gx_app_poll_event(event buffer)
gx_app_log(level, bounded UTF-8 message)
gx_app_file_read(asset/path request, bounded result buffer)
gx_app_get_capabilities(result buffer)
gx_app_exit(exit code)
```

Names and exact signatures should be finalized only after the first native payload proof. The ABI must have explicit error codes, buffer-size rules, ownership rules, and an unsupported-version result. Managed wrappers in `AppModel.SDK` can then evolve without changing the kernel/runtime implementation boundary.

## Compatibility and sequencing

1. Define identity, manifest, launch arguments, lifecycle, window request, exit, logging, and capability discovery in `AppModel.Abstractions`.
2. Define a native ABI record/version document and a diagnostics host that can answer the version query.
3. Make a built-in diagnostic app use the same abstractions without requiring package loading.
4. Add a NativeAOT diagnostic payload only after managed entry is proven.
5. Add architecture-specific package selection and shared assets after the payload ABI is stable.
6. Add install, permissions, file associations, suspension, and richer desktop integration later.

The first milestone does not need the App Model to launch an application. It only needs to avoid making kernel-private choices part of the future public surface.

## Risks

- The Server hosted registry and bare-metal registry can drift unless they consume the same manifest and launch-resolution definitions.
- The current `.gxapp` document is a design/experimental format, not evidence of a production-compatible package loader.
- Native ELF and NativeAOT payloads may have different relocation, import, runtime, and ABI requirements; an architecture entry must identify those requirements explicitly.
- A package identity shared with Server is useful only if versioning, architecture names, and entry kinds are specified as compatibility contracts rather than informal strings.

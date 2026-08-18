# ManagedKernel ABI v1

Phase 1 introduces the first real managed system layer without changing the
accepted NativeAOT proof payload. The native guideXOS bootstrap/runtime loads
the separate `gxos-managed-kernel.dll` image, starts NativeAOT once, calls the
bootstrap `ManagedMain` export, and then invokes ordinary managed-kernel
services through fixed-layout, versioned contracts.

```text
Native guideXOS bootstrap/runtime
                |
                | GX_MANAGED_KERNEL_ABI_V1
                v
          ManagedKernel
                |
                +-- system initialization
                +-- system-information service
```

`src/ManagedEntryProbe` remains the foundation control. It is retained for
runtime, scheduler, callback, allocation, and GC regressions. It is not the
managed system project. The new `src/ManagedKernel` project produces the
separate `gxos-managed-kernel` NativeAOT payload.

## Versioning policy

ABI v1 is identified by `GX_MANAGED_KERNEL_ABI_V1 == 1`. Every public
structure starts with a 32-bit `Size` and a 32-bit `AbiVersion` where
applicable. Callers must send a supported version and a structure size at
least as large as the v1 structure. Unknown versions return
`GX_MANAGED_UNSUPPORTED_ABI` before reading or writing versioned payload data.

Future revisions may extend a structure by increasing `Size`; implementations
must only read or write fields covered by the negotiated size. Capability bits
are bounded and additive. Unknown capability bits must be ignored.

The ABI uses Microsoft x64 calling convention entry points, fixed-width C
fields, one-byte packing, and no C++ ABI, reflection marshaling, JSON,
serialization framework, CLR object reference, or managed handle. A managed
object identity never crosses this boundary.

## ABI v1 structures

The authoritative native declarations are in
`src/Gate4Harness/managed_kernel_abi.h`. Managed definitions in
`src/ManagedKernel/ManagedKernel.cs` use `StructLayout(Pack = 1)` and verify
the same sizes and offsets during the bootstrap path.

`GX_MANAGED_KERNEL_INIT_REQUEST_V1` is 16 bytes:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Bytes available for this request; v1 requires 16 or more. |
| 4 | `AbiVersion` | `uint32_t` | Must be 1. |
| 8 | `Architecture` | `uint32_t` | Must be `0x8664` for x64. |
| 12 | `Flags` | `uint32_t` | Must be zero in v1. |

`GX_MANAGED_KERNEL_SYSTEM_INFO_V1` is 32 bytes:

| Offset | Field | Type | Meaning |
|---:|---|---|---|
| 0 | `Size` | `uint32_t` | Bytes written; v1 returns 32. |
| 4 | `AbiVersion` | `uint32_t` | Returned interface version; 1. |
| 8 | `ServiceVersion` | `uint32_t` | System-information service version; 1. |
| 12 | `Architecture` | `uint32_t` | Returned architecture; x64 (`0x8664`). |
| 16 | `Capabilities` | `uint64_t` | Bounded capability mask. |
| 24 | `Reserved` | `uint64_t` | Zero in v1; reserved for future use. |

Native `_Static_assert` declarations cover sizes and offsets. The managed
layout check covers sizes and offsets with `sizeof` and `Marshal.OffsetOf`.
The native host ABI test also checks alignment, constants, status values, and
overflow-safe output-buffer validation.

## Status codes

These are guideXOS managed-kernel service statuses, not Win32 last-error
values:

| Name | Value | Meaning |
|---|---:|---|
| `GX_MANAGED_OK` | 0 | Operation succeeded. |
| `GX_MANAGED_INVALID_ARGUMENT` | 1 | A pointer, structure field, or required argument is invalid. |
| `GX_MANAGED_UNSUPPORTED_ABI` | 2 | The requested ABI version is not supported. |
| `GX_MANAGED_BUFFER_TOO_SMALL` | 3 | The caller supplied less than the v1 output size. |
| `GX_MANAGED_NOT_INITIALIZED` | 4 | A service was called before managed-kernel initialization. |
| `GX_MANAGED_ALREADY_INITIALIZED` | 5 | Initialization was requested after readiness was established. |

The native caller receives the status directly. Last-error state is not used
to communicate managed-kernel service results.

## Initialization contract

`GxManagedKernelInitialize(uint32 requestedAbiVersion, uintptr requestAddress)`
is an `[UnmanagedCallersOnly]` export. The native bridge resolves it through
the PE export directory and never assumes an RVA. It validates its local
request buffer before crossing the boundary, while managed code validates the
requested version, pointer, size, architecture, and flags before setting the
one-time readiness state.

The valid sequence is:

1. NativeAOT runtime startup occurs once.
2. `ManagedMain` performs bootstrap entry and returns.
3. Native code resolves and calls `GxManagedKernelInitialize`.
4. A valid first call returns `GX_MANAGED_OK` and makes ManagedKernel ready.
5. A valid later call returns `GX_MANAGED_ALREADY_INITIALIZED`.

Initialization is deliberately separate from NativeAOT process startup and
does not call `ManagedMain` again. The current scheduler is cooperative and
single-instance, so initialization is a one-time system lifecycle transition;
future concurrent callers must be serialized by the native kernel service
dispatcher before entering this contract.

## System-information service

`GxManagedQuerySystemInfo(uint32 requestedAbiVersion, uintptr outputAddress,
uintptr outputCapacity)` is an `[UnmanagedCallersOnly]` export. It returns
`GX_MANAGED_NOT_INITIALIZED` before initialization, rejects unknown ABI
versions, rejects null output, and requires at least 32 bytes before writing.
The managed implementation constructs a local fixed-layout result, writes
exactly the v1 structure, and retains no native pointer after return.

The Phase 1 result is intentionally limited to truthful interface data:

```text
ABI=1
SERVICE_VERSION=1
ARCH=X64
CAPABILITIES=0x0000000000000003
```

No canonical guideXOS OS version/build field exists yet, so none is invented.

## Capability bits

| Bit | Name | Meaning |
|---:|---|---|
| 0 | `GX_MANAGED_CAPABILITY_SERVICE_ABI` | The versioned managed service ABI is available. |
| 1 | `GX_MANAGED_CAPABILITY_SYSTEM_INFORMATION` | The v1 system-information service is available. |

These bits describe usable interfaces, not internal test markers. Scheduler
attachment, GC, finalizer behavior, and other runtime facts remain runtime
acceptance evidence and are not advertised as public system-service
capabilities by this phase.

## Pointer, buffer, and lifetime rules

Pointers are opaque native addresses used only for the duration of a call.
They point to caller-owned unmanaged storage, never to a managed object. The
native bridge validates the output address, minimum capacity, and address-plus-
capacity overflow before the call. The managed side repeats null and capacity
checks at the ABI boundary. Failure paths do not partially initialize or
partially populate the output structure; the native acceptance path uses
sentinel-filled buffers to verify this property.

The v1 service writes no bytes beyond 32 bytes, does not retain the output
pointer, and returns only fixed-width values. The native caller owns the
storage and may reuse it after return.

## Payload roles and selection

The two payload roles are explicit:

| Mode | Project/payload | Purpose |
|---|---|---|
| `ManagedEntryProbe` | `src/ManagedEntryProbe`, `gxos-managed-entry-probe.dll` | Foundation regression/control, including callback and GC controls. |
| `ManagedKernel` | `src/ManagedKernel`, `gxos-managed-kernel.dll` | Real managed system initialization and services. |

`tools/Build-Gate4Harness.ps1` accepts `-Payload ManagedEntryProbe` or
`-Payload ManagedKernel`. It logs source and staged paths, sizes, SHA-256
identities, and verifies byte identity. It never selects a payload by
directory ordering or modification time. The control payload's authoritative
identity remains governed by its existing acceptance path.

## Current limitations and deferred work

Phase 1 does not add full corlib integration, GUI, window management,
filesystem, networking, sound, drivers, process management, application
model, thread pool, `Task`, async/await, managed `Thread`, reflection,
dynamic assemblies, rich marshaling, object RPC, or a full managed-kernel
port. The ABI currently exposes one service and x64 only. Additional services
should follow this versioned, size-checked, capability-negotiated pattern.

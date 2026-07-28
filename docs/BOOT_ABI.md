# Minimal boot-information ABI

Status: **version 1, proof-only and extensible**. It is the smallest contract needed to validate a raw pointer and emit the first marker. It is not a kernel boot-information schema.

## Native layout

The native and managed declarations use a one-byte packing boundary and little-endian fields:

```c
#pragma pack(push, 1)
typedef struct GuideXBootInfo
{
    uint32_t Magic;         /* offset 0, 0x534F5847: bytes GXOS */
    uint16_t Version;       /* offset 4, 1 */
    uint16_t Size;          /* offset 6, minimum 24 */
    uint32_t Architecture;  /* offset 8, 0x8664 for x86-64 */
    uint32_t Flags;         /* offset 12, zero in version 1 */
    uint64_t SerialWrite;   /* offset 16, native callback pointer */
} GuideXBootInfo;
#pragma pack(pop)
```

The exact size is 24 bytes. There are no managed object references, strings, arrays, ownership handles, or speculative framebuffer/filesystem/ACPI/memory-map fields.

## Managed declaration and entry ABI

The authoritative managed declaration is `GuideXBootInfo` in [ManagedEntry.cs](../src/ManagedEntryProbe/ManagedEntry.cs), with `[StructLayout(LayoutKind.Sequential, Pack = 1)]`.

The entry method is:

```csharp
[UnmanagedCallersOnly(EntryPoint = "ManagedMain")]
public static int ManagedMain(nint bootInfoAddress);
```

The first argument is a raw native-sized integer containing the address of the 24-byte prefix. On x86-64, pointers and `nint` are 64 bits. The call uses the platform x64 ABI: the first integer argument is in the normal x64 integer argument register, and the return value is a 32-bit signed integer in `EAX`.

`SerialWrite` points to this unmanaged callback shape:

```c
typedef void (*GuideXSerialWrite)(const uint8_t *bytes, uintptr_t length);
```

The callback is valid only for the duration of `ManagedMain`. The loader owns the boot-information storage and callback implementation; the managed method borrows both and does not retain them.

## Validation and version behavior

`ManagedMain` rejects:

- a null address with return `-1`;
- wrong magic, version, too-small size, wrong architecture, or null callback with return `-2`.

Version 1 consumers must accept `Size >= 24` and read only the fields in the known prefix. Future versions may append fields after offset 24. A consumer must reject a version it cannot interpret and must never read beyond `Size`. Unknown flag bits are reserved and must be rejected or ignored only after their version policy is defined.

The byte order is little-endian. The structure is naturally aligned only by its explicit packed layout; the pointer field is at offset 16 despite the pack boundary. The architecture field makes the pointer-width/calling-convention contract explicit rather than inferred.

## Marker and return contract

After validation, the managed method constructs these 29 bytes on its stack and calls `SerialWrite`:

```text
GXOS_NET10:MANAGED_ENTRY_OK\r\n
```

It returns `0` only after the callback returns. No managed allocation, managed string, object graph, exception construct, thread, synchronization primitive, reflection, dynamic load, host OS service, or file operation is present in the source method.

The current Gate 4 run has not reached this method. The loader reports the export and then stops at unresolved PE imports, so the marker remains an unobserved success condition rather than a claimed result.

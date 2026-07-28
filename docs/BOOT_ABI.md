# Minimal boot-information ABI

Status: **version 1, verified in Gate 4, proof-only and extensible**. This is the smallest borrowed contract needed to validate a raw pointer and emit one fixed marker. It is not a kernel boot-information schema.

## Byte layout

Native and managed declarations are byte-for-byte equivalent. Both use one-byte packing and little-endian fields:

```text
offset  size  field          value/meaning
0       4     Magic          0x534F5847; bytes "GXOS" in memory
4       2     Version        1
6       2     Size           24 minimum
8       4     Architecture   0x8664 for x86-64
12      4     Flags          0 for this proof
16      8     SerialWrite    raw callback pointer
total 24 bytes
```

The native packed declaration is in `src\Gate4Harness\gate4_loader.c`; the managed declaration is `GuideXBootInfo` in [ManagedEntry.cs](../src/ManagedEntryProbe/ManagedEntry.cs), `[StructLayout(LayoutKind.Sequential, Pack = 1)]`. The native build has `_Static_assert` checks for total size, every field offset, and 64-bit pointer width. The managed constants define the same magic, version, architecture, and minimum size. No speculative fields, object references, strings, arrays, ownership handles, framebuffer, filesystem, ACPI, or memory-map fields were added.

## Entry and callback ABI

```c
typedef void (EFIAPI *GuideXSerialWrite)(const uint8_t *bytes, uintptr_t length);
typedef int (EFIAPI *ManagedMainEntry)(uintptr_t boot_info_address);
```

On x86-64 `EFIAPI` is GCC `ms_abi`, matching the Microsoft x64 ABI used by the NativeAOT Windows artifact. `ManagedMain(nint)` receives the raw pointer in RCX and returns its 32-bit status in EAX. `SerialWrite` receives the byte pointer in RCX and length in RDX. The callback and boot-info storage are borrowed: they are valid only during the loader's managed call and are never retained by managed code.

The harness calls the relocated export at:

```text
preferred image base  0x0000000180000000
export RVA             0x0000000000024724
positive actual base   0x000000000547B000
positive target VA     0x000000000549F724
boot-info pointer      loader-owned static 24-byte object (logged in fault state)
```

The exact target is recomputed from the actual allocation each run; the representative address above is the one recorded in the three final runs.

## Validation behavior

The managed method performs non-throwing primitive checks in this order:

- null pointer: return `-1` (`0xFFFFFFFF` in the native unsigned log);
- wrong magic, version, too-small size, architecture, or null callback: return `-2` (`0xFFFFFFFE`);
- valid prefix and callback: load the raw function pointer, build 29 fixed bytes with `stackalloc`, invoke the callback, return `0`.

The success bytes are exactly:

```text
GXOS_NET10:MANAGED_ENTRY_OK\r\n
```

The positive serial ordering is `BEFORE_MANAGED_CALL`, the managed marker, `AFTER_MANAGED_RETURN=0x0000000000000000`, and `MANAGED_ENTRY_COMPLETE`. The invalid-version and null-callback controls both reached the managed export, returned `0x00000000FFFFFFFE`, emitted no success marker, and stopped through the native nonzero-return path.

The loader remains responsible for keeping the serial callback valid until after the managed return. It restores GS and fault-handler state before announcing completion, then halts deterministically.

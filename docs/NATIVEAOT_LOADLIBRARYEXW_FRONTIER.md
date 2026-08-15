# NativeAOT `LoadLibraryExW` frontier

The NativeAOT startup payload requests the UTF-16 string `kernel32` from the
finalizer-thread description setup at preferred caller RVA `0x3CE67`. The
call is:

```text
LoadLibraryExW(L"kernel32", NULL, 0x00000800)
```

Static payload inspection and three fresh QEMU boots agree on the following
facts:

| Field | Value |
| --- | --- |
| Import descriptor / symbol | `2 / 0x39` |
| IAT RVA | `0x7D200` |
| Natural caller RVA | `0x3CE67` |
| Runtime `lpLibFileName` | `0x5512230` in the observed boot layout |
| UTF-16 contents | `006B,0065,0072,006E,0065,006C,0033,0032` |
| UTF-16 length / terminator | `8` code units / `0x5512240` |
| `hFile` | `NULL` |
| `dwFlags` | `0x800` |

`0x800` is `LOAD_LIBRARY_SEARCH_SYSTEM32`. Windows uses that flag to limit
DLL search to the system directory and the dependencies resolved from that
search scope. The payload supplies no path and no extension, so the Windows
name-resolution form is the `kernel32.dll` system module. See the [Microsoft
`LoadLibraryEx` documentation](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexa).

## Supported guideXOS contract

Gate4 now has one explicit built-in module descriptor for `KERNEL32.dll`.
The descriptor is a stable process-lifetime compatibility object and its
address is the HMODULE value. It is not an arbitrary sentinel, a fake PE
image base, or a host OS handle. Repeated supported loads return the same
nonzero descriptor address. Existing `GetModuleHandleW` name lookup shares the
same descriptor, and `GetProcAddress` recognizes it as an approved module.

The bounded support is intentionally narrow:

- `kernel32`, `kernel32.dll`, and case variants are accepted without a path.
- `hFile` must be `NULL`.
- The flags must be exactly `LOAD_LIBRARY_SEARCH_SYSTEM32`.
- The UTF-16 source must be canonical, readable through the registered image
  regions, and terminated within 256 code units.
- Unsupported names and paths fail with `ERROR_MOD_NOT_FOUND` (`126`).
- Invalid pointers, non-NULL `hFile`, and unsupported flags fail with
  `ERROR_INVALID_PARAMETER` (`87`).
- Success preserves the incoming LastError value.

This is a compatibility interpretation of the system-directory search for an
already-registered built-in module. It does not open a filesystem path, map a
new PE image, resolve arbitrary DLL dependencies, maintain a reference count,
or claim support for `FreeLibrary`. Those behaviors remain intentionally
outside this milestone.

The immediately inseparable follow-up is `GetProcAddress` on the returned
handle. `SetThreadDescription` is not yet registered in the built-in export
surface, so the lookup returns `NULL` and `ERROR_PROC_NOT_FOUND` (`127`). No
synthetic function pointer is returned.

## Bounded loader census

For the exact payload, the relevant loader APIs are:

| API | Descriptor / symbol | IAT RVA | Direct call sites | Startup reachability |
| --- | --- | --- | --- | --- |
| `LoadLibraryExW` | `2 / 0x39` | `0x7D200` | `0x3C99E`, `0x3CACA`, `0x3CE67` | `0x3CE67` reached; the other two are dormant probes |
| `GetProcAddress` | `2 / 0x20` | `0x7D138` | six sites, including `0x3CE77` | reached immediately after the load |
| `GetModuleHandleW` | `2 / 0x1F` | `0x7D130` | `0x37C61`, `0x3C553` | present; current path's earlier named lookup remains separate |
| `GetModuleHandleExW` | `2 / 0x38` | `0x7D1F8` | `0x3C911` | present; not reached before the new frontier |

The payload imports no `LoadLibraryA`, `LoadLibraryW`, `LoadLibraryExA`,
`FreeLibrary`, `GetModuleHandleA`, or `GetModuleHandleExA`.

## QEMU result

Three independent fresh boots reached the same sequence without a CPU fault,
page fault, fatal message, or scheduler/COM corruption:

1. `LoadLibraryExW` returned the registered `KERNEL32.dll` descriptor.
2. LastError was preserved (`0x7A` in the observed boots).
3. `GetProcAddress` approved that handle, searched for
   `SetThreadDescription`, and returned `NULL` with error `127`.
4. Execution continued to the next unresolved import:
   `KERNEL32.dll!RaiseFailFastException`.

The next-call identity in all three boots was descriptor `2`, symbol index
`0x14`, IAT RVA `0x7D0D8`, runtime call site RVA `0x32C21`. Its arguments were
`RCX=0`, `RDX=0`, `R8=1`, `R9=0x54690B8`, stack arguments 5 and 6 both
`0x5757000000000000`, and LastError `0x7F` from the preceding missing-export
lookup.

At that boundary the current scheduler thread was identity `2` (finalizer
worker), main state `2` (Runnable), worker state `3`, worker priority `2`, and
runnable count `1`. The worker remained COM-initialized as MTA with nesting
count `1`; main COM remained uninitialized. No managed execution or runtime
initialization markers were reached.

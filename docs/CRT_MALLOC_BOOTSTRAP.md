# Microsoft x64 bounded `malloc` bootstrap contract

This milestone implements only `api-ms-win-crt-heap-l1-1-0.dll!malloc` for the
required NativeAOT payload
`gxos-managed-entry-probe.dll` with SHA-256
`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`.

The exact route is case-sensitive and matches only that API-set DLL and the
`malloc` symbol. Ordinal imports and `free`, `calloc`, `realloc`, `_recalloc`,
`_callnewh`, `_malloc_base`, and debug-heap variants remain unresolved.

## Windows evidence reconciliation

The retained source inspected was
`artifacts/windows-malloc-oracle-20260804-033340/native-run-3/malloc-events.csv`
and its integrity/canonical files. It contains 39 entry/return pairs. Every
entry has payload call-site RVA `0x78339`; setup captured zero payload calls,
non-payload calls were excluded, and entry/return pairing passed.

The previous 40-item transcription duplicated one `8` between the `24` and
`12520` requests. Therefore the previous “39” count was correct. The canonical
fixture is
`src/Gate4Harness/tests/crt_malloc_trace_fixture.h` and has total requested
bytes `1,054,602`, largest request `819,200`, and this ordered sequence:

```text
88,72,56,8,8,64188,80,864,819200,6448,8,8,8,8,8,8,64,40,32,8,8,8,
147456,88,800,1368,640,80,24,8,12520,32,30,64,24,16,48,16,168
```

Native oracle runs 1 and 2 independently captured 39 calls and the same call
site, but reported `64184` rather than `64188` at position 6. This is a
bounded runtime-size variance, not a duplicate event or another caller. The
fixture preserves verified run 3, which matches the corrected transcription.

## Supported contract

Requests must be nonzero, no larger than `0xC8000`, representable as `UINTN`,
and made while boot services are available. The bridge calls exactly
`AllocatePool(EFI_LOADER_DATA, requestedSize, &pointer)` and returns the direct
pointer. It does not add a header, clear bytes, inspect bytes, poison bytes,
or implement freeing.

Ownership is held in a fixed external registry of 64 records. Each record has
the returned pointer, requested size, monotonically increasing allocation
sequence, and occupied state. The registry is enough to replay the complete
observed 39-call startup sequence; it is bounded evidence machinery, not a
general process-heap limit. Protected ranges and existing records are checked
with overflow-safe half-open range arithmetic. Invalid, overlapping,
under-aligned, duplicate, or unrecordable results are rolled back once through
`FreePool` and returned as null.

Each invocation receives bounded diagnostics for call-site identity, request,
registry counts and slot, pool status, pointer and alignment, range and overlap
validation, rollback, and return value. Summary counters include maximum live
allocations, total bytes, largest request, failures, metadata exhaustion,
duplicate-pointer rejection, rollbacks, and `_callnewh` reachability. The
diagnostic path never reads the returned storage.

## Validation boundary

The focused host suite passes the canonical replay, all required positive
sizes, capacity, repeated sizes, rollback and negative vectors, malformed
registry state, sequence behavior, diagnostics, memory-preservation, external
record placement, and independent-context isolation. The compatibility object
has no external references.

Three fresh QEMU runs each verified the payload hash and reached:

```text
malloc sizes:       88,72,56
registry slots:      0,1,2
live counts:         1,2,3
total bytes:         216
alignment modulo 8:  0,0,0
_callnewh reached:   0
```

Absolute pool addresses are run-dependent. These runs preserve the prior
on-exit and pinned module-handle paths, then stop naturally at the next
unresolved import:
`KERNEL32.dll!AddVectoredExceptionHandler`. That import is deliberately not
routed by this milestone.

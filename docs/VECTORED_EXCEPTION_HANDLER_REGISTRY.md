# Ordered vectored exception-handler registry

The exception-context foundation provides the bounded x64 trap capture and
resumable context application described in
[`EXCEPTION_CONTEXT_FOUNDATION.md`](EXCEPTION_CONTEXT_FOUNDATION.md). This
document records the separate bootstrap registry added for the NativeAOT
`AddVectoredExceptionHandler` import.

## Scope and storage

The registry is one process-level `GXOS_VEH_REGISTRY` object in persistent,
harness-owned storage. It contains eight fixed records and performs no
allocation. Records are never reused during ordinary execution because
removal is intentionally not implemented. The allocation diagnostic is
therefore always zero.

Each record retains:

* occupied state and fixed slot number;
* callback address, callback image identity, image base, callback RVA, and
  PE-section name/executable state;
* the raw requested `First` value and its monotonic registration sequence;
* a guideXOS-private opaque handle;
* invocation count and last callback return value.

The handle is the address of the persistent registry record. It is non-null,
stable for the record lifetime, unique among live registrations, and opaque to
the payload. It is not a Windows-private object and is reserved for a future
removal implementation.

## Ordering and registration

`First == 0` appends a record after the current ordered records. Any nonzero
`First` value inserts it before the current first record. Thus the ordinary
payload registration (`First == 1`) becomes the first record. A failed
validation, active-dispatch registration, or full registry returns null and
does not alter the existing order.

The exact route is only the pair `KERNEL32.dll!AddVectoredExceptionHandler`.
It is selected by the verified import descriptor, per-descriptor symbol
index, and IAT RVA. Disabled routing leaves the original unresolved-import
fail-fast path visible. No removal, continue-handler, ordinal, exception-raise,
or unrelated-DLL route is installed.

## Callback validation

Registration accepts only a non-null canonical x64 address inside a
persistent mapped image represented by the registry. The bounded PE parser
checks complete image bounds, section arithmetic, readable/executable section
flags, and rejects writable-only or non-executable data. The live payload
callback is validated against its mapped payload image and `.text`; gated
synthetic callbacks are validated against the harness image. This milestone
represents only those two images and does not claim general Windows image
discovery.

## Dispatch policy

Only translated vector 3 (`0x80000003`) dispatches through the registry. The
ordered slots are snapshotted before the first callback. Callbacks receive the
bounded compatibility `EXCEPTION_POINTERS` pointer in `RCX` using the
Microsoft x64 ABI. Return `0` means continue-search; `0xFFFFFFFF` means
continue-execution and stops iteration. Any other return is recorded as
invalid, treated conservatively as continue-search, and cannot resume the
interrupted context. Unsupported vectors retain the existing fatal path.

Registry mutation during dispatch is not supported. Since removal is absent,
the relevant mutation is nested registration; it is rejected while the
registry dispatch-active flag is set. The flag is cleared on every ordinary,
continue-execution, and terminal dispatch path. A nested CPU exception still
uses the existing terminal nested-dispatch guard.

The payload callback is not invoked artificially during ordinary startup. The
ordinary path records the successful registration and continues to the next
unresolved import.

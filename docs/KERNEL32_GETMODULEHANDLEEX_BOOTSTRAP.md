# `KERNEL32.dll!GetModuleHandleExW` bootstrap contract

Status: the loader-model investigation supports the exact `PIN | FROM_ADDRESS`
form required by the NativeAOT payload, and the bounded implementation is
present. The first live call succeeds, but the complete three-invocation gate
is currently blocked by the next pre-existing heap import (`malloc`) exposed
after that success; no heap API was added to this loader-only change.

## Payload gate

The required payload is `gxos-managed-entry-probe.dll` with SHA-256
`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837`.
The repository baseline is branch `main`, commit
`a31f7556ba2c651efa1efd65ff1e4764c476ac78`, subject
`Implement initial register_onexit storage path`.

The payload's mapped image facts are supplied by the PE loader. The reference
payload has `SizeOfImage == 0xD3000`; the live address samples are all inside
its `.text` mapping.

## Loader-model investigation

The exact ownership and metadata flow is:

| Responsibility | Existing structure or function | Finding |
| --- | --- | --- |
| Read the payload file | `read_payload` in `src/Gate4Harness/gate4_loader.c` | Opens `\\GXOS\\gxos-managed-entry-probe.dll`, allocates an 8 MiB `EFI_LOADER_DATA` read buffer, reads it, and stores `PE_IMAGE.file` and `PE_IMAGE.file_size`. |
| Parse and map the PE | `PE_IMAGE` and `load_pe_image` in `src/Gate4Harness/gate4_loader.c` | Reads the PE32+ optional header, stores `SizeOfImage` in `PE_IMAGE.loaded_size`, allocates the mapped pages, records `PE_IMAGE.loaded` and `PE_IMAGE.actual_base`, zeros the image, copies headers and sections, and applies relocations. |
| Record section ranges/protection | `PE_IMAGE.memory_regions[]` and `PE_IMAGE.executable_regions[]` | Each section's `[virtual_address, max(virtual_size, raw_size))` is converted to an actual `[base,end)` range with readable/executable/writable flags. Executable ranges are recorded separately. The complete image range remains `actual_base .. actual_base + loaded_size`. |
| Translate an RVA | `rva_to_file`, `rva_to_loaded`, and the direct `actual_base + rva` calculations in `efi_main` | File RVAs are section-checked; mapped RVAs are checked against `loaded_size` and then index the single mapped image. Relocation and managed-target calculations use the actual base. |
| Retain post-relocation/import facts | `g_managed_image_base`, `g_main_module_facts`, and the other configured platform contexts in `efi_main` | Relocations modify the mapped allocation in place. Import resolution writes the IAT in that same allocation. `g_main_module_facts` persistently copies the base, size, entry, import, relocation, and region metadata needed by the module APIs. |
| Track loaded images | `g_main_module_facts` and `PE_IMAGE image` in `efi_main` | There is no general loaded-module registry. There is one active payload descriptor. `PE_IMAGE image` is an `efi_main` stack object; its region array is referenced by the static `g_main_module_facts`. |
| Release image memory | `load_pe_image`, `efi_main`, and the whole loader | The image is allocated by `BootServices->AllocatePages(EFI_LOADER_DATA, ...)`. There is no `FreePages` call, no `UnloadImage` call, and no image-reclaim routine. Existing `FreePool` use is limited to transactional `_register_onexit_function` storage rollback and does not own the image pages. |
| Managed/process teardown | `call_managed_entry`, `restore_nativeaot_tls`, `restore_fault_handlers`, `fail`, and `halt_forever` | After managed return the loader restores temporary TLS/fault state, records the result, and halts. Failure paths print `GXOS_NET10:FAIL:*` and halt. `efi_main` does not return on either path, so its stack-resident `PE_IMAGE` and region array remain valid during all later managed calls. |
| Reference or pin state | All loader sources | There is no reference count, permanent-image bit, unload API, or existing pin field. The image's lifetime is stronger than a per-call pin because the loader never frees or unloads it before the process-ending halt. |

## Lifetime outcome: A — already permanently resident

The mapped payload allocation is process-resident under the current
architecture. The invariant is explicit in code: `load_pe_image` performs the
only image-page allocation, no image `FreePages`/`UnloadImage` path exists,
`fail` halts instead of unwinding, and the successful `efi_main` path also
ends in `halt_forever`. The stack object containing the region array cannot go
out of scope because `efi_main` never returns. Process/QEMU termination may
reclaim all firmware allocations as an external process-final action, which
is the allowed process-final teardown and is not a normal image unload.

The paths checked for lifetime challenges are:

* normal `efi_main` completion: restores TLS/fault state, records the managed
  result, then calls `halt_forever`; it does not free the image;
* `fail`: emits a bounded failure marker and calls `halt_forever`; it does not
  free the image;
* `_register_onexit_function` rollback: may call `FreePool` for its own
  temporary 0x100-byte table allocation, never for `PE_IMAGE.loaded`;
* `restore_nativeaot_tls` and `restore_fault_handlers`: restore CPU/process
  state only and do not touch the mapped image;
* EFI protocol/file cleanup: closes the payload file after reading, but does
  not free the mapped pages or the read buffer.

Therefore no pin bit is added. The `PIN` requirement is recorded as satisfied
by the permanent-residency invariant, and the implementation will report the
prior and resulting pin state as resident/pinned without changing a reference
count or allocating another image.

## Bounded lookup and output proof

The supported lookup searches the one existing `g_main_module_facts`
descriptor only. It treats `RDX` as an integer address, never as UTF-16 text,
and accepts it iff:

```
base <= address < base + size_of_image
```

The implementation rejects a null base/size, a noncanonical base or address,
and `base + size_of_image` overflow before performing containment. Since the
current loader has exactly one registered payload descriptor, a contained
address has exactly one owner; zero matches and any future ambiguous match
fail without changing the output. Section membership is recorded for
diagnostics only and is not used as a substitute for complete image
containment.

`R8` is accepted only when it is non-null and the existing guideXOS stack
bounds prove that eight bytes fit in the writable stack range established by
`initialize_nativeaot_tls` (`g_stack_lower .. g_stack_upper`). The output is
read or written only after that proof. Invalid flags, addresses, metadata, or
output pointers return zero and leave the output unchanged.

The import route is limited to `KERNEL32.dll!GetModuleHandleExW` with
`dwFlags == 0x5` exactly: `GET_MODULE_HANDLE_EX_FLAG_PIN` and
`GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS`, with unchanged-refcount clear and
no unknown bits. Name lookup, loading, unloading, reference counting, and
other loader APIs remain unsupported.

## Evidence boundaries and validation status

The implementation is split between
`src/Gate4Harness/platform_get_module_handle_ex.c/.h` (the checked core),
`src/Gate4Harness/gate4_loader.c` (the exact import route and bounded
diagnostics), and `tools/Build-Gate4Harness.ps1` (positive and disabled
profiles). `src/Gate4Harness/tests/platform_get_module_handle_ex_tests.c` and
`tools/Run-PlatformGetModuleHandleExHostTests.ps1` cover the three oracle RVAs,
boundary addresses, idempotence, both invocation orders, unsupported flags,
null/outside addresses, overflow and malformed metadata, null/short output
ranges, unproven lifetime, UTF-16 non-interpretation, output preservation, and
the no-external-reference check.

### Proven by live guideXOS execution

Three fresh positive QEMU runs reached the exact first invocation at payload
RVA `0x37C40`. Each recorded flags `0x5`, raw `RDX == image_base + 0x37C40`, a
proven writable `R8`, unique payload lookup, image base `0x547B000`, image size
`0xD3000`, output before `0`, output after `0x547B000`, return value `1`,
residency/pin state `1 -> 1`, no allocation, no image free/unload, and no
prior on-exit callback execution. Each run reached
`GXOS_NET10:GETMODULEHANDLEEX_OK` and then stopped at the authentic next
boundary `api-ms-win-crt-heap-l1-1-0.dll!malloc`.

The exact payload did not naturally execute the later `0x42110` and `0x29ABC`
call sites because `malloc` occurs first. This milestone does not claim live
execution of those calls, managed-entry completion, or a managed result.

The disabled-routing control preserved the `37 / 87 / 0` import census and
stopped at `KERNEL32.dll!GetModuleHandleExW` before entering the implementation.

### Proven by the Windows oracle

The Windows-observed payload address RVAs are `0x37C40`, `0x42110`, and
`0x29ABC`. The oracle shows that all three calls use `PIN | FROM_ADDRESS`, all
three return the same payload image base, and the caller consumes that result
as a PE image base.

### Proven by deterministic contract tests

The host suite uses one synthetic registered image descriptor with
`SizeOfImage == 0xD3000`. The generic guideXOS complete-image containment
algorithm accepts all three oracle RVAs, selects the descriptor uniquely,
returns the exact mapped base, performs no allocation or unload, and leaves the
permanent-residency metadata unchanged. It repeats each request and runs both
the Windows-observed order (`0x37C40`, `0x42110`, `0x29ABC`) and a different
order, proving idempotence and order independence. Boundary, overflow, malformed
metadata, unsupported-form, address-interpretation, and output-preservation
controls also pass.

The prior `GetModuleHandleW` and `_register_onexit_function` host suites pass as
regressions. The current limitation remains one persistent main payload
descriptor: there is no generalized loaded-module registry, no name-based
lookup, and no unload or reference-count implementation. The `PIN` behavior is
satisfied by the stronger existing loader residency invariant documented above;
this change adds no artificial pin state.

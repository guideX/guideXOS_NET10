# Image-format decision

Status: **provisional direct PE/COFF remains selected**. Gate 4 success validates the direct experiment; it does not promote PE/COFF to a permanent guideXOS kernel ABI.

## Options considered

| Option | Evidence | Current assessment |
| --- | --- | --- |
| Direct NativeAOT PE/COFF loading | Standard `win-x64` NativeAOT emits PE32+, with relocations, TLS, `.pdata`, an import directory, and the `ManagedMain` export. The UEFI loader parses, relocates, resolves, and enters this exact image. | Best current controlled handoff. Keep provisional because later allocation, GC, exceptions, unwind, and broader startup contracts remain unimplemented. |
| Historical PE-to-ELF conversion | Read-only UEFI evidence contains an older converter for an older runtime. | Not adopted. It would risk discarding PE imports, TLS, unwind metadata, BSS, and relocations. |
| Direct ELF NativeAOT | The available Windows invocation with `-r linux-x64` stopped with cross-OS native compilation unsupported in this checkout. | Not proven here. Revisit only with an independently pinned Linux toolchain. |
| Flat/custom image | Would require a new durable ABI and explicit treatment of relocations, data, unwind, TLS, metadata, and runtime initialization. | Premature; it would not remove the runtime boundary. |

## Proven Gate 4 path

```text
byte-for-byte PE payload
  -> validate PE32+, sections, BSS extent, directories, and export
  -> allocate and zero SizeOfImage
  -> copy headers/sections
  -> apply all DIR64 relocations
  -> patch all 124 IAT slots
  -> install bounded fault diagnostics
  -> initialize PE TLS template, _tls_index, GS/TEB, and one-thread state
  -> call relocated ManagedMain export with Microsoft x64 ABI
  -> managed callback emits GXOS_NET10:MANAGED_ENTRY_OK
  -> return 0, restore loader state, and halt
```

The positive serial evidence is `PE_IMPORT_DESCRIPTORS=10`, `PE_IMPORT_SYMBOLS=124`, `PE_IMPORT_RESOLVED=124`, `UNRESOLVED_REQUIRED_IMPORTS=0`, followed by `BEFORE_MANAGED_CALL`, the managed marker, `AFTER_MANAGED_RETURN=0`, and `MANAGED_ENTRY_COMPLETE`.

The 106 imports not reached by this path are fail-fast guarded. The 18 functional imports are intentionally limited to the observed NativeAOT transition. No broad Windows API or CRT layer was added.

## Decision boundary

Direct PE/COFF remains the provisional choice for the next narrowly bounded NativeAOT experiment. It becomes permanent only after separate evidence for the next runtime boundaries, especially first allocation/GC, virtual memory ownership, thread state beyond one boot CPU, TLS lifetime, exceptions, unwinding, and static-constructor policy. The successful managed-entry marker is evidence for this handoff only.

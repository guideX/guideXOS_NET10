# NativeAOT GC on scheduler-created threads

Status: passed for the bounded cooperative-scheduler merge gate.

This milestone proves that a guideXOS scheduler-created thread can attach to
NativeAOT through the generated reverse-P/Invoke path, allocate managed
objects, initiate a real collection, retain a managed stack-local reference
across that collection, return to native code, block/resume, and repeat the
same proof. It does not add managed thread-pool, `Task`, async/await, or
general managed-thread support.

## Payload and exports

The GC probe is an intentional payload rebuild. `ManagedMain` and
`ManagedCallback` remain present and the existing callback behavior is
unchanged.

| Item | Before | Final |
| --- | ---: | ---: |
| Payload SHA-256 | `72F5CD40EE698B6BCCF6D67AEAB1BA570A2CE6B49B083B447AF067AA6F1EE9FA` | `AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012` |
| Payload size | `729600` bytes | `730112` bytes |
| `ManagedCallback` RVA | `0x24724` | `0x24764` |
| `ManagedMain` RVA | `0x2476C` | `0x24958` |
| `ManagedGcProbe` RVA | not present | `0x247E0` |
| ABI | — | Microsoft x64 `int32 -> int32` |
| Discovery | — | PE export-directory lookup |

The changed RVAs are expected from the deliberate rebuild. The final staged
ESP payload was checked byte-for-byte by the fresh-boot runner on every run.

## Managed probe

The new export is:

```csharp
[UnmanagedCallersOnly(EntryPoint = "ManagedGcProbe")]
public static int ManagedGcProbe(int seed)
```

For each deterministic seed it:

1. allocates one `int[8]` retained array;
2. writes eight seed-derived sentinels and computes a checksum;
3. allocates four short-lived `byte[64]` pressure arrays;
4. calls a no-inline helper that records `GC.CollectionCount(0)`, calls the
   public `GC.Collect()`, and records the count again;
5. keeps the retained array live with ordinary managed semantics;
6. rereads every retained element and recomputes the checksum; and
7. returns a value containing the collection delta, retained-array generation,
   and checksum low bits.

The element payload is approximately 288 bytes per invocation (32 bytes for
the retained `int[8]` plus 256 bytes of pressure-array elements), excluding
managed object/array headers. No raw object address is returned or stored in
native scheduler state.

The retained reference is a managed local in `ManagedGcProbe`, live across a
separate no-inline collection helper. It is not moved to a static, a
`ThreadStatic` field, a native global, or a TCB field.

## Collection and root evidence

The final three fresh boots produced the same deterministic results:

| Caller | Seed | Result | Collection delta | Generation after collection | Checksum |
| --- | ---: | ---: | ---: | ---: | ---: |
| Main scheduler thread | `0x31` | `0xC0011908` | `1` | `1` | `0x908` |
| Worker, first call | `0x51` | `0xC0011A08` | `1` | `1` | `0xA08` |
| Same worker after block/resume | `0x52` | `0xC0011C00` | `1` | `1` | `0xC00` |

The collection marker is not synthesized by the managed method: the result
is accepted only when `GC.CollectionCount(0)` advances. The worker path also
reached the real runtime imports for `FlushProcessWriteBuffers`,
`GetTickCount64`, and `RtlVirtualUnwind`. The unwind bridge parses the loaded
payload's x64 `.pdata`/`UNWIND_INFO` entries and updates the supplied context;
all observed worker collection unwinds completed with zero failures.

Representative worker evidence from the final boot:

| Field | Value |
| --- | ---: |
| Scheduler identity | `5` |
| Stack base / limit | `0x530D000 .. 0x5311000` |
| RSP before first collection | `0x5310DE8` |
| TEB stack lower / upper | `0x530D000 .. 0x5311000` |
| Unwind calls after first collection | `4` |
| Unwind calls after repeat collection | `6` |
| Unwind failures | `0` |
| Last unwind context RSP | `0x5310E80` |
| Low sentinel before/after collection | `0xC1` / `0xC1` |
| High sentinel before/after collection | `0xD0` / `0xD0` |

The runtime does not expose a custom root-promotion callback in this payload,
and the harness intentionally does not inspect or persist object addresses.
Therefore the root evidence is the stronger externally observable invariant:
the ordinary managed local remains type-correct, retains its eight values, and
produces the expected checksum after each actual collection. Object movement
is neither required nor inferred.

During investigation, NativeAOT cleared the lower page of the registered
worker stack as part of its stack/GC boundary handling. The scheduler now keeps
its low reclamation sentinel in a separately allocated per-thread page and
leaves the registered 16 KiB stack as runtime-owned memory. That page is freed
with the worker; the VM stack registration remains the single canonical
`stack_base..stack_limit` range. This preserves the GC's real behavior rather
than treating the runtime-owned lower page as a scheduler corruption marker.

## Attach, switching, and isolation

The required lifecycle completed in every final boot:

```text
native worker starts
  -> fresh GS/TEB/TLS/FLS state
  -> generated reverse-P/Invoke thunk attaches automatically
  -> ManagedGcProbe(0x51)
  -> native return
  -> canonical event block/signal/resume
  -> ManagedCallback(8)
  -> ManagedGcProbe(0x52)
  -> native return and worker reclamation
  -> independent fresh worker callback
```

The first worker had TLS `+0x78 == 0` and runtime FLS `0` before entry; after
the thunk it reported the valid attached/preemptive sentinel
`0xFFFFFFFFFFFFFFFF` and its own FLS value. The sentinel remained valid after
both managed returns and after scheduler resume. The second fresh worker
attached independently with a distinct identity and TLS block.

The finalizer worker remained identity `2`, blocked on its existing startup
wait record with independent FLS and COM MTA state. The probe introduced no
finalizable objects and did not manually wake the finalizer.

Observed final state in each boot included:

```text
process-entry initialization calls = 1
managed callback count = 5
main FLS != finalizer FLS != worker FLS
active waits = 1
valid wait records = 1
baseline scheduler VM regions = 2
worker VM regions after GC = 3
worker VM regions after reclaim = 2
worker live/FLS/TLS/handle lookup after reclaim = 0/0/0/0
```

The worker allocation-context slots at TLS `+0x30/+0x38` were observed as
zero before and after both worker probes. Main's slots were nonzero before its
probe and zero after collection. This harness does not claim that those slots
are the complete allocator-context representation, and it does not copy or
manufacture an allocation context. Managed allocations and collection
success are the authoritative evidence for this milestone; allocator-context
identity/lifetime remains a narrower runtime audit boundary.

## Validation

The exact-payload pre-change callback baseline passed three independent boots
with the callback-era `72F5...` payload and the existing callback/durability
markers. The final GC runner passed three independent fresh boots with the
current authoritative `AE19...` payload (730,112 bytes). Their serial byte
counts were all `527111`; the serial files differ only in expected
firmware/runtime addresses and were hashed as:

```text
4F353A4D2D8424D50A1410C59EB13F923B92006DBCD35E6BBD952ED45AFCA1C2
C040E1937F7BC748A24AA555065015A7B0BC0191976524708FBBB9E68D66FF41
B9B28B8C343EC9848FD7BD721CEB4FEEDE83968303301F87E92C5656A59213A9
```

These fresh logs are retained under
`artifacts\nativeaot-gc-audit-qemu-authoritative-20260817`.

Focused host results:

- scheduler model: passed, 256 checks;
- scheduler durability: passed;
- scheduler stack VM: passed;
- Event API: passed, 245 checks;
- CreateThread model: passed, 134 checks;
- ResumeThread model: passed, 57 checks;
- COM API: passed, 92 checks;
- WriteFile: passed, 269 checks;
- CRT malloc/free: passed;
- topology, NUMA, VM, memory accounting, system info, time, performance,
  exception/context, VEH, multibyte, module loading, GetModuleHandle,
  GetProcAddress, environment, job, standard handles, and global-memory
  tests: passed;
- NativeAOT callback bridge: passed;
- GC probe contract decoder: passed, 8 checks;
- synthetic scheduler QEMU proof: passed.

The final runner rejects fail-fast markers, CPU faults, page faults, unresolved
imports, repeated startup, incorrect result encoding, nonzero unwind failures,
FLS/TLS aliasing, stale handles, unreclaimed VM state, and QEMU cleanup
failures.

## Boundary and deferred work

The foundation is now integrated and closed on `main`; the next natural
development boundary is managed kernel integration. This proof does not
establish a general managed thread store destruction protocol, finalizer execution, allocation
stress behavior, managed exceptions across the export ABI, managed thread
pool, `Task`, async/await, arbitrary managed thread creation, reflection,
dynamic assembly loading, APCs, or broader wait/COM APIs.

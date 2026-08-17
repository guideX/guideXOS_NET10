# NativeAOT managed callback bridge

Status: passed. This milestone adds a second direct NativeAOT export that is callable after the one legal NativeAOT process-entry call has returned. It does not call the top-level NativeAOT entry a second time. The follow-on scheduler-thread attachment proof is documented in [NATIVEAOT_SCHEDULER_THREAD_ATTACH.md](NATIVEAOT_SCHEDULER_THREAD_ATTACH.md).

## Contract

The managed project adds:

```csharp
[UnmanagedCallersOnly(EntryPoint = "ManagedCallback")]
public static int ManagedCallback(int value)
```

The method is deliberately GC-light. A managed static counter starts at zero, increments on each valid call, and returns `(counter << 16) | (value + 1)`. The two proof calls are:

| Call | Input | Result | Managed counter |
| --- | ---: | ---: | ---: |
| 1 | `41` | `0x0001002A` | `1` |
| 2 | `99` | `0x00020064` | `2` |

The high word proves that the same initialized managed static state was entered twice; replaying native results or rerunning process startup would not produce this sequence.

## Export and payload identity

The selected mechanism is the existing direct NativeAOT export contract already used by `ManagedMain`. The loader resolves the name from the loaded PE export directory, converts the discovered RVA to the relocated image address, registers the pointer, and refuses calls until the managed-entry completion marker has been reached.

The callback payload is an intentional, limited rebuild:

| Item | Value |
| --- | --- |
| Original payload SHA-256 | `2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` |
| Original size | `729600` bytes |
| Callback payload SHA-256 | `72F5CD40EE698B6BCCF6D67AEAB1BA570A2CE6B49B083B447AF067AA6F1EE9FA` |
| Callback payload size | `729600` bytes |
| Staged ESP SHA-256 | `72F5CD40EE698B6BCCF6D67AEAB1BA570A2CE6B49B083B447AF067AA6F1EE9FA` |
| `ManagedCallback` export RVA | `0x24724` |
| `ManagedMain` export RVA | `0x2476C` |
| Preferred callback address | `0x180024724` |
| Representative relocated callback address | `0x000000000549F724` |
| Exported symbols | `ManagedCallback`, `ManagedMain` |

This table records the historical callback milestone artifact. The current
merge-gate payload is the later 730,112-byte managed-entry/callback/GC rebuild
with SHA-256
`AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`.

The payload was rebuilt with `tools\Build-Gate1.ps1` using the existing `ManagedEntryProbe.csproj` and `-c Release -r win-x64 --self-contained true -p:PublishAot=true`; the command was launched from an unrelated directory because this machine has SDK `10.0.400` while the repository pins `10.0.302` with roll-forward disabled. No repository or global configuration was changed.

The emitted callback entry begins with the Microsoft x64 first integer argument in `ECX` and returns in `EAX`. The disassembly preserves nonvolatile registers and has associated `.pdata` exception/unwind entries. Its transition sequence calls the same generated NativeAOT reverse-P/Invoke/thread-transition helper family used by `ManagedMain`; the callback does not bypass the runtime transition. The native wrapper clears DF and establishes `MXCSR=0x1F80` and x87 control `0x037F`. The Microsoft x64 call boundary supplies the 32-byte shadow space; the native harness is built with `-mno-red-zone`.

## Readiness and thread contract

The export table exists before runtime initialization, but the pointer is not legally callable at that point. The minimum proven readiness contract is:

1. PE relocation and complete IAT patching have succeeded.
2. The payload TLS template, TLS vector, TLS block, GS/TEB state, stack limits, FLS bridge, and imports are installed.
3. The top-level NativeAOT process entry has been called exactly once.
4. `NATIVEAOT_STARTUP_OK`, `GC_STARTUP_ADVANCED`, `MANAGED_ENTRY_OK`, `AFTER_MANAGED_RETURN=0`, and `MANAGED_ENTRY_COMPLETE` have been reached.
5. The invoking scheduler thread is either the initialized main thread with its existing NativeAOT TLS/FLS state active, or a scheduler-created thread with a distinct GS/TEB/TLS/FLS environment that the generated reverse-P/Invoke transition can recognize and attach.

Before each callback, the loader reactivates the already allocated main-thread GS/TLS state. It does not allocate a second runtime state, clone the main state into another TCB, or call the top-level NativeAOT entry again. After each call it restores the firmware/native GS base.

The first proof caller is scheduler identity `1`, state `RUNNING` (`3`). The finalizer worker remains identity `2`, state `BLOCKED` (`4`), with a valid wait record and independent FLS value. Main FLS is unchanged before and after both callbacks; finalizer FLS, COM MTA state, active wait count, valid wait-record count, and two registered scheduler VM regions remain stable.

The scheduler-thread follow-on proof uses the canonical scheduler APIs to create a fresh worker without copying main state. The generated reverse-P/Invoke thunk attaches it automatically, returns to native code, survives a scheduler block/resume, and can be called again. A second fresh worker attaches independently. Main/finalizer isolation and the existing durability invariants remain unchanged.

Exceptions are intentionally excluded from the ABI. The callback does not throw, and no managed exception is allowed to cross this unmanaged export boundary. Results are explicit integer status/data. The callback performs no deliberate allocation and no GC activity was observed in the QEMU proof.

## Validation

The focused host test covers synthetic PE export discovery, null and unknown symbol rejection, malformed export-directory rejection, registration, pre-readiness rejection, null-result rejection, stable callback pointer behavior, two sequential ABI calls, state-coded results, and an untouched scheduler-metadata sentinel. The result was `NATIVEAOT_CALLBACK_BRIDGE_HOST_TESTS=PASSED` and the freestanding bridge object had no undefined references.

The callback harness was built with `-Scenario NativeAotEventWait -EnableNativeAotStartup -EnableNativeAotManagedCallback`. Gate 3 byte-for-byte PE identity comparison passed. Three independent fresh exact-payload QEMU boots passed under `artifacts\nativeaot-managed-callback-qemu-v4`; each reached:

```text
NATIVEAOT_STARTUP_OK
GC_STARTUP_ADVANCED
MANAGED_ENTRY_OK
AFTER_MANAGED_RETURN=0
MANAGED_ENTRY_COMPLETE
MANAGED_CALLBACK_1_OK
MANAGED_CALLBACK_2_OK
MANAGED_CALLBACK_COUNT=2
NATIVEAOT_DURABILITY_PASS=1
```

Each run also reported one process-entry initialization call, callback results `0x0001002A` and `0x00020064`, counter values `1` and `2`, unchanged FLS values, a blocked finalizer, one active wait, one valid wait record, and two live scheduler VM regions. No fail-fast, CPU exception, page fault, or repeated startup marker occurred.

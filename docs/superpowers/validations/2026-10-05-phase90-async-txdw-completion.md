# Phase 90 — Non-Blocking TXDW-Driven E1000 Transmit Completion

Date: 2026-10-05  
Prompt phase: 90  
Outcome: **E — Phase 90 implementation and hardware acceptance remain incomplete**

## Preflight

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `8f77c33b1f16024555d0beb09727a2e640f61ee6` — `Validate hardware-backed managed UDP ingress`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: 1 ahead / 0 behind
- Starting worktree: clean
- `.phase`: absent before this work; remains absent

The Phase 80, Phase 81, and Phase 81R2 reports were inspected. Phase 80 established the typed E1000 TXDW route. Phase 81 documents that TXDW is notification and descriptor DD is authoritative, while synchronous callers cannot wait safely. Phase 81R2 remains unaccepted and is outside this work.

## Existing TX ownership

The E1000 TX ring has 16 descriptors. Startup initializes descriptors with DD set. `TryTransmitFrame` uses `_txIndex`, writes the frame and descriptor, stores `_lastSubmittedTxIndex`, advances TDT, polls the selected descriptor's DD bit up to `PollLimit`, and advances `_txIndex` only after DD. It remains the normal synchronous TX API.

Phase 80's managed interrupt handler validates the typed event sequence and cause, then checks DD at `_lastSubmittedTxIndex`. It does not maintain multiple request identities or scan a bounded outstanding window.

## Work implemented, not accepted

The working tree contains an initial internal async path in `ManagedE1000Driver`:

- `TrySubmitFrame` returns an internal submit result and a 64-bit identity made from descriptor index plus a nonzero generation.
- Outstanding requests are bounded to four and use fixed arrays sized to the 16-entry TX ring.
- Ownership values are `Free`, `Prepared`, `HardwareOwned`, and `Completed`.
- The request record is established and descriptor status cleared by descriptor construction before TDT is written.
- Deferred typed TXDW handling scans all hardware-owned descriptors and uses DD as completion authority. Completion is retained in the ledger until consumed.
- Query returns `Pending`, `Success`, `HardwareError`, or invalid/stale. Consume frees only an authoritatively completed descriptor. A reused slot increments its generation.
- The existing synchronous method remains DD-polling and refuses to overwrite a slot still owned by the async ledger.
- No wait, timer, scheduler, or GC behavior was changed.

These changes are plumbing only. They are not wired into a Phase 90 guest mode or proven through real QEMU TX traffic. In particular, there is no Phase 90 acceptance marker, no host-side frame capture/validation, and no three-boot run. Coalescing, capacity-full behavior, duplicate/stale event handling against the live ledger, shutdown with outstanding work, same-slot reuse, and synchronous regressions have not been proven end to end. The current host assertions cover request-ID encoding and generation distinction, not the full driver ledger.

## Checks run

- ManagedKernel NativeAOT build: passed with .NET SDK 10.0.401.
- Normal Gate4 build: passed.
- Phase 14 E1000 host suite: passed; request identity/generation assertions added.
- Managed interrupt host suite: passed, 15/15.
- Native interrupt host suite: passed.
- Driver-binding host suite: passed.
- Phase 16 networking suite: passed, 139 cases including 55 UDP cases.
- Managed host services suite: passed.
- `git diff --check`: passed.
- Phase 90 QEMU boots: 0/3 attempted.

## Outcome and limits

This work does not satisfy the Phase 90 success boundary because real QEMU transmissions, TXDW-driven managed completion, host frame capture, same-slot reuse, and the required three fresh boots were not exercised. Phase 90 is **not accepted**. No commit or push was made.

Phase 81 accepted: **No**.  
Phase 81R2 accepted: **No**.  
Synchronous normal TX still DD-polls: **Yes**.  
The new async implementation has no timeout or reset recovery proof; a request can remain pending if hardware does not complete it.

Phase 90 adds and validates a non-blocking TXDW-driven completion path. It does not resolve the scheduler-wait blocker or convert the existing synchronous transmit API away from DD polling.

---

## Phase 90 continuation — hardware acceptance completed

**Execution date:** 2026-10-05
**Current outcome:** **A — non-blocking TXDW completion validated**

This continuation records the completed qualification. The earlier Outcome E section above is retained as the initial investigation snapshot; its statement that no acceptance boots were attempted was true at that time and is superseded by the results below.

### Live repository preflight

The pasted prompt expected starting HEAD `8f77c33b1f16024555d0beb09727a2e640f61ee6` and divergence `1 ahead / 0 behind`. Live Git state was authoritative and differed:

| Field | Live start | Expected by pasted prompt |
|---|---|---|
| Repository | `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration` | Same |
| Branch | `nativeaot-managed-kernel-integration` | Same |
| HEAD | `017a9f6f488d44fe91ae65d9ea16b8ca1456972e` — subject `...` | `8f77c33b1f16024555d0beb09727a2e640f61ee6` |
| Upstream | `origin/nativeaot-managed-kernel-integration` | Same |
| Divergence | `0 ahead / 0 behind` | `1 ahead / 0 behind` |
| Worktree | Clean | Clean |
| `.phase` | Absent | Not specified |

No branch, remote, worktree, or history operation was performed. The worktree was clean before these changes, and `.phase` remains absent.

### Implementation and semantics

The fixed E1000 ring contains 16 TX descriptors. The normal `TryTransmitFrame` method still prepares one descriptor, advances TDT, and polls descriptor DD up to its existing limit. Phase 90 remains a separate internal API: `TrySubmitFrame(byte[]?, int, out ulong)` returns `Accepted`, `Busy`, `InvalidArgument`, or `Unavailable` without polling DD. `TryGetTransmitCompletion(ulong)` returns immediately with `Pending`, `Success`, `HardwareError`, or `InvalidOrStale`; `TryConsumeTransmitCompletion(ulong)` frees only a completed request and makes its prior identity stale.

Async capacity is fixed at four, represented by fixed per-descriptor arrays. The Phase 90 proof uses four outstanding descriptors, leaving the other twelve ring entries outside this bounded proof window. An identity encodes descriptor index plus a nonzero 32-bit generation. On submit, generation and `Prepared` ownership are recorded before DMA data/descriptor writes; the descriptor builder clears old DD; ownership changes to `HardwareOwned` before TDT. TXDW remains a notification. Deferred managed handling scans the full 16-entry ring and DD plus descriptor error bits determine completion. One event may complete multiple outstanding descriptors; later events with no outstanding requests are safe unmatched notifications. The typed-event dispatcher rejects stale event sequences before ledger mutation.

The acceptance scenario verifies four unique accepted IDs and `Pending` before dispatch, then verifies the fifth request is `Busy` at capacity. It drains only the deferred managed event queue; `TrySubmitFrame` and completion queries contain no DD-wait loop. All four initial requests report success, repeated query remains success until consume, and a second consume is invalid/stale. It then advances around the ring, reuses the original descriptor index 4 at generation 2, and confirms the generation-1 identity cannot query or consume the reused request. A final ordinary synchronous transmit succeeds using its existing DD polling path. The guest also observes unmatched TXDW events with an empty ledger.

Shutdown unsubscribes the interrupt route, disables the E1000 engines before releasing DMA, and transitions the driver out of `Running`; completion queries then return `InvalidOrStale`. No test forced shutdown while hardware still owned a Phase 90 descriptor. There is no asynchronous timeout or reset recovery contract; a request can remain pending if hardware never completes it. This phase makes no claim that QEMU coalesced TXDW interrupts: each accepted run produced 18 native TXDW captures for the 18 post-Phase-80 test transmissions. The bounded scanner supports a coalesced/deferred batch by inspecting every outstanding descriptor on each TXDW, and the initial four submissions were all made before managed dispatch.

### Build and identified acceptance inputs

The payload was built from the current working tree with .NET SDK `10.0.401` using:

```powershell
.\tools\Build-ManagedKernel.ps1 -OutputDirectory .\artifacts\phase90-build\managed-kernel
.\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\phase90-build\gate4-normal -ManagedArtifact .\artifacts\phase90-build\managed-kernel\publish\gxos-managed-kernel.dll -PayloadMode ManagedKernel -Scenario ManagedKernelPhase11 -EnableNativeAotStartup -AssumeUnspecifiedTimezoneUtc
.\tools\Build-Gate4Harness.ps1 -OutputDirectory .\artifacts\phase90-build\gate4-phase90 -ManagedArtifact .\artifacts\phase90-build\managed-kernel\publish\gxos-managed-kernel.dll -PayloadMode ManagedKernel -Scenario ManagedKernelPhase11 -EnableNativeAotStartup -EnablePhase90AsyncTx -AssumeUnspecifiedTimezoneUtc
```

Phase 90 is opt-in; the ordinary Gate4 build omitted its acceptance define. Artifact identities:

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| ManagedKernel NativeAOT payload | 4,882,432 | `C2450FF4CB8ED1FBC1281154F08BDC88DDAD72B5C4ACCC64FFA10C172E2C390A` |
| Ordinary Gate4 `BOOTX64.EFI` | 745,682 | `D53E21539286F90208850A3450CBDC6830070200A856E5DBEC6ABE2C4682CD98` |
| Phase 90 Gate4 `BOOTX64.EFI` | 747,790 | `34F667294B027212D3F7768197AB7182C9E92C28A3E6FD81FD0C201DEFC9096E` |
| OVMF code copy (each run) | 3,653,632 | `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` |

The boot runner recorded per-run OVMF variable-store input hashes in `firmware-identity.log`; the writable variable store changes during boot. QEMU used `C:\Program Files\qemu\qemu-system-x86_64.exe` and OVMF code from the QEMU `share\edk2-x86_64-code.fd` path. Each fresh run booted the phase90 Gate4 EFI containing the same managed payload hash above.

The three-boot invocation was:

```powershell
.\tools\Run-ManagedKernelPhase11FreshBoots.ps1 -GateDirectory .\artifacts\phase90-build\gate4-phase90 -EvidenceDirectory .\artifacts\phase90-qemu-evidence-final -PayloadSha256 C2450FF4CB8ED1FBC1281154F08BDC88DDAD72B5C4ACCC64FFA10C172E2C390A -PayloadSize 4882432 -RunCount 3 -TimeoutSeconds 240 -PostPhase11Marker 'GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS' -EnablePhase80E1000 -EnablePhase90AsyncTx
```

Gate4 used the existing Q35/e1000e MSI route and a local QEMU dgram peer. The Phase 90 peer captured 18 sequence-tagged 60-byte Ethernet frames per boot. Host validation checked destination MAC `FF:FF:FF:FF:FF:FF`, guest source MAC `52:54:00:12:34:56`, EtherType `88B5`, `GXOS_PHASE90_TX` signature, and the unique sequence values 1 through 18. Each capture binary is 1,152 bytes (18 length-prefixed frames). Evidence is under `artifacts/phase90-qemu-evidence-final/runs/run-{1,2,3}/`.

| Boot | Async requests completed | Managed TXDW events after Phase 80 | Native MSI/TXDW captures | Managed events | Host frames | Guest marker |
|---:|---:|---:|---:|---:|---:|---|
| 1 | 17 | 18 | 18 | 18 | 18 | PASS |
| 2 | 17 | 18 | 18 | 18 | 18 | PASS |
| 3 | 17 | 18 | 18 | 18 | 18 | PASS |

The 17 async completions are the four-request batch, twelve one-at-a-time wraparound requests, and one generation-2 descriptor reuse request. The eighteenth post-baseline TX is the normal synchronous regression. Each run reports descriptor index 4 reused at generation 2 and an empty-ledger unmatched TXDW count of 5. Counts are intentionally not equated: the 18 native notifications include a synchronous request, while 17 are ledger completions. Host frame capture confirms hardware transmission of all 18 phase-tagged frames.

| Boot | Serial log SHA-256 | Host capture binary SHA-256 | Capture log SHA-256 | Timeline SHA-256 |
|---:|---|---|---|---|
| 1 | `D045E83FA9008E68E8AFB7BD7C138A68D90B695710F1AE275939836508E01852` | `B999AB748647B00E4B0B19DB06589392B549A1DCC7B5708EFED8DBF89DA58B7E` | `EDAC11069B242CAC0BABCD349093E87A353FAD21045DAF2297D252E3ADAD8A3D` | `C73D66047179694196B18FEEC7A957EFD9F9A8FA21E496FA105C6679FBFEF2B7` |
| 2 | `AAED7B189623AC82B23DBE49E3B58B468D5FC4FB40A2CD6DA3529D860C8D638C` | `B999AB748647B00E4B0B19DB06589392B549A1DCC7B5708EFED8DBF89DA58B7E` | `EDAC11069B242CAC0BABCD349093E87A353FAD21045DAF2297D252E3ADAD8A3D` | `E595F974B850CC3F2A34DBA27733565094AC4FF1019D0223A47497692FD0415E` |
| 3 | `00E98560ED3E431D40E7A555361C291A38F44D9C3B2B24070E86F8A3133B61EA` | `B999AB748647B00E4B0B19DB06589392B549A1DCC7B5708EFED8DBF89DA58B7E` | `EDAC11069B242CAC0BABCD349093E87A353FAD21045DAF2297D252E3ADAD8A3D` | `3CC11C1FCC8A5A19C72D8972C73E771A2F7CA56A908E2007B82A6CDE2FC581D7` |

### Regressions

| Check | Result |
|---|---|
| ManagedKernel NativeAOT build | PASS; payload above |
| Ordinary Gate4 build, Phase 90 disabled | PASS |
| Phase 90 Gate4 build | PASS |
| Phase 14 E1000 host tests | PASS, including request-ID generations and Phase 90 frame encoding |
| Managed interrupt harness/toolchain/source completeness | PASS; 15/15 assertions; 84 production sources, 85 compile items, zero duplicates |
| Native interrupt tests | PASS |
| Driver binding host tests | PASS |
| Managed service host tests | PASS |
| Phase 16 networking host tests | PASS, 139 cases |
| Phase 85 ARP/QEMU regression | PASS, 3/3 fresh boots; each captured 3 local replies from 4 requests |
| Phase 87 ICMP/QEMU regression | PASS, 3/3 fresh boots; each captured 3 replies from 3 requests |
| Phase 89 UDP/QEMU regression | PASS, 1/1 fresh boot; 4 frames parsed, 3 bound and 1 unbound |
| COM1 | Phase 90 ran with its COM1 route active; no Phase 82 stress matrix was requested |
| SyntheticScheduler | Not run; scheduler source and wait behavior were not changed |

### Final classification and Git closeout

**Outcome A — non-blocking TXDW-driven completion validated.** Real e1000e hardware transmitted the async frames; deferred managed TXDW processing scanned authoritative DD state; all request results were queried and consumed; full capacity, pending results, same-slot reuse, and stale generations were exercised; and all three fresh boots reached the later progress marker. The normal synchronous API still DD-polls. Phase 81 accepted: **No**. Phase 81R2 accepted: **No**.

`git diff --check` passed. No unrelated Phase 90 behavior was enabled in ordinary builds. Production changes are limited to the gated Phase 90 acceptance path/proof hooks and frame construction; no scheduler, timer, timeout, GC, or network-service API was changed. No remaining Phase 90 release-blocking defect was observed. The earlier host capture parser mistake was corrected and the accepted result comes only from the final 3/3 run above. Local commit and push results are recorded in the task closeout.

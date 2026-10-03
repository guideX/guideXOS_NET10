# Phase 80 — Hardware-Backed Managed E1000 Interrupt Delivery

**Date:** 2026-10-03
**Outcome:** B — the hardware proof exposed a production managed interrupt binding defect; the narrow repair passed three fresh QEMU boots.

> Phase 80 proves a real QEMU E1000 hardware interrupt crosses into managed code; it does not claim DHCP, IP networking, or bare-metal NIC support.

## Device and trigger

- QEMU instantiated `e1000e` (`-device e1000e,netdev=net0,addr=2`) with a local QEMU user-mode backend. No external network traffic or Internet access was used.
- PCI function `0000:00:02.0`, vendor/device `8086:10D3`.
- BAR0 mapped at `0x81060000`, length `0x20000`.
- Interrupt delivery used PCI MSI, capability at `0xD0`, vector `0x51`, MSI message address `0xFEE00000`. The legacy INTx line was 11; it was not the selected route.
- The selected cause was E1000 TX descriptor written-back (`TXDW`, ICR bit 0). IMS was programmed with `0x1` and read back as `0x1`.
- The managed proof submitted one deterministic 60-byte synthetic local Ethernet frame, then three bounded repeats. The test does not require that the frame reach a network endpoint.

## Authoritative delivery evidence

Each of the three independent fresh boots logged the following ordered path:

1. Device and MSI route configured; managed binding reported ready before transmission.
2. The E1000 TX descriptor completed.
3. Native IRQ entry and native route dispatch each advanced to 1 for the first event.
4. The native read-to-clear ICR reported `0x80000003`. TXDW bit 0 was present; the other observed asserted cause bits are preserved in `0x80000002`.
5. The existing native-to-managed interrupt event batch delivered four typed E1000 events to `ManagedE1000Driver`, with the last sequence `0xD`.
6. Three repeats completed. Final hardware IRQ entry, native dispatch, managed delivery, and cause-acknowledgement counts were all 4.
7. A subsequent ICR read was 0; the TXDW mask readback remained 1; the route was quiesced; interrupt-storm count was 0.
8. Phase12 and Phase13 completion markers appeared after E1000 proof, showing continued kernel/scheduler progress.

The managed event is the existing `GxManagedKernelInterruptEventV1` (E1000 event type 3). Native code queues the typed event through the existing interrupt route and drain service; `ManagedInterruptDispatcher` matches device/event identity and calls `ManagedE1000Driver.TryHandleInterrupt`. No raw native pointer is exposed. The QEMU fixture does not call the managed handler, native-to-managed callback, generic managed dispatcher, or synthesize ICR state.

The x64 IRQ entry saves/restores processor general and floating-point/SSE state and invokes native capture only. Native capture reads the device ICR, acknowledges the interrupt controller, and queues bounded state; managed work is deferred to the existing event drain path. The hardware IRQ path adds no allocation, blocking, NativeAOT attach, or repeated logging.

## Defect found and repair

The existing managed interrupt service wrappers selected COM1's route 0 for every subscription and unsubscription. Consequently, an E1000 subscription could not resolve its device/event route through the production binding service. The wrappers now use the existing generic route lookup keyed by the supplied device identity and event type; the existing ABI is retained. E1000 subscribe/unsubscribe cleanup follows the driver's start, teardown, and abort paths.

The proof also exposed two capture details that were corrected narrowly: an empty follow-up ICR read must not erase the last nonzero cause associated with a queued record; and QEMU may make descriptor completion visible before its deferred interrupt record is drained, so the proof uses a bounded 100,000-iteration deferred-queue poll outside IRQ context. Neither change fabricates a cause or invokes a managed callback directly.

Supporting harness repairs: the Phase11 boot runner now waits for the actual `MANAGED_KERNEL_DRIVER_BURST_OK` marker; the driver-binding host project compiles the current ManagedKernel source graph and its runner resolves an installed .NET 10 SDK; and the Phase14 host runner uses the same SDK resolver. These removed stale marker/source-list/SDK pins that prevented the intended regressions from running reliably.

## Binding, negative, and repeat behavior

- Native host coverage verifies an unbound E1000 route only acknowledges the interrupt and does not read/enqueue a device event; a typed record reaches the replacement binding; a stale token cannot unsubscribe that replacement.
- Phase14 managed host tests cover TXDW cause classification, unrelated cause preservation, bound-event acceptance, stale sequence, wrong device, unbound/empty cause rejection, bounded deterministic frame construction, and zero-sequence rejection.
- The guest trace contains four distinct event sequences for the initial operation and three repeats. This proves the observed bounded run; it does not establish a universal hardware exactly-once guarantee under all coalescing/load behavior.

## Regression results

| Check | Result |
|---|---|
| NativeAOT ManagedKernel build | Passed; failure/proof mode is opt-in and not enabled by default. |
| Gate4 harness build | Passed, including IRQ assembly validation. |
| Managed interrupt host toolchain resolution | Passed, 6 assertions; .NET SDK 10.0.401 selected automatically. |
| Managed interrupt source completeness | Passed: 84 production sources, 85 host compile items, 0 duplicates. |
| Managed interrupt host tests | Passed, 15/15. |
| Native interrupt route host tests | Passed. |
| Phase14 focused E1000 host tests | Passed, 33 assertions. |
| Managed driver binding host tests | Passed. |
| Driver service owner/status host tests | Passed. |
| Driver worker host tests | Passed. |
| Phase61–66 managed-worker API host tests | Passed for Phases 61, 62, 64, 65, and 66. |
| Phase11/12/13 + Phase80 QEMU acceptance | Passed, 3/3 fresh boots. |
| SyntheticScheduler proof | Not run: scheduler internals were untouched; the existing SyntheticScheduler script requires a different pinned payload/harness. Normal builds plus post-interrupt Phase12/13 progress markers supplied build/progress sanity. |

COM1/keyboard and managed worker Phase11 checks passed before the E1000 portion. The Phase11 worker is stopped before Phase14 E1000 proof begins, so this run does not claim simultaneous COM1 service or worker coexistence during the NIC interrupt.

## Reproduction evidence

Evidence directory: `artifacts/evidence-phase80-accepted-final/` (generated and ignored; not committed).

| Run | Serial log SHA-256 | Injection record SHA-256 | Timeline SHA-256 |
|---|---|---|---|
| 1 | `CF618C27B7D23D4DF561206C0048CA9BB8178984B88C7637CAC175F88CDB6A6B` | `EBC3A29D71D9BD970BFBFA0016FAE6AF119D28A0B3FE53A5BD809284C8FFAAE1` | `588F225CF3603841C9DC788F28ACB3DFC9EDA231787E13501DCD07F624B4D841` |
| 2 | `CA01A5059600AB8A3C3DF1634045A38797FDAAA98AA2BCDAA49F02EF40C8DB70` | `31046EBF6C27689C09680180E83760E8B4CEE816EF8C99C6E5EEC7D4698B653C` | `25CD0D1FBB3690A0DF2EFE0319164A4F22D8F875143BD122190F8E91981D52C4` |
| 3 | `62287F68E320AE32B0BAE0CDB3E08A04BFFF8E611B0A6B5E3EA39AA0A1B17291` | `96ED420EC8E432E368E07617C28F2BA48C055B5448CD70A97E8B75F83184B2CD` | `7644C21BB0750EF287ABFEB5E80E9583332CAC3DD5864CE4F0B08C6E29D6FEE2` |

Built artifacts used for all three boots:

- Managed payload: 4,799,488 bytes, SHA-256 `C21848229A7899A778C8927C21AD3E649802F51E05BA7762C7BC728BE5AF656B`.
- EFI image: 745,008 bytes, SHA-256 `19559462BA65B8010DFD7E77DB1DD1C41194B9F7130DB7331DC87B9484119DDB`.

The QEMU runner verified the requested NIC model and event markers on each run. The payload hash/size were supplied to the runner and matched its output.

## Limits and next step

This proves the selected QEMU `e1000e` TX completion interrupt path only. It does not validate DHCP, IPv4, packet delivery beyond the local QEMU backend, E1000 RX interrupts, physical hardware, bare-metal interrupt routing, or NIC support outside this harness. The smallest follow-up is to use the typed event in bounded production TX/RX completion handling, with a separate local RX proof only if RX integration is next in scope. Phase 81 was not started.

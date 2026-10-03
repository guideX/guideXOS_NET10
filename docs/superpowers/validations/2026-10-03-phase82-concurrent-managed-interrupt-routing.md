# Phase 82 — Concurrent Managed Interrupt Route Isolation and Coexistence

**Date:** 2026-10-03
**Outcome:** A — concurrent multi-device routing validated.

**Phase 82 validates concurrent route isolation and coexistence; it does not resolve scheduler waits or make normal E1000 TX interrupt-driven.**

## Result

Three fresh QEMU boots kept the production COM1 and E1000 managed routes registered together. Real UART IRQ4 receive events and real E1000e MSI/TXDW events were interleaved through the native FIFO and existing deferred managed dispatcher. Each event reached only its matching managed driver. Both unsubscribe orders preserved the remaining route, and each boot continued through the Phase12 and Phase14 progress markers.

No production route-table defect was found. The native route table already had room for both routes, its service wrappers resolved subscriptions by the supplied device/event identity, and the shared FIFO retained that identity in every record. Phase 82 adds route-local diagnostics and an opt-in guest proof that exercises those existing paths.

During early harness iterations, one boot observed an extra empty E1000 MSI handler entry while still producing four TXDW records. The counters and acceptance checks now distinguish raw vector entries from nonzero captured causes and queued events. The final three boots each observed four raw Phase80 entries and four acknowledged Phase80 causes.

## Route and queue model

| Device | Identity tuple `(device kind, device ID, event type)` | Route slot | Interrupt | Managed consumer |
|---|---|---:|---|---|
| COM1 | `(platform serial, 1, serial receive)` = `(2, 1, 1)` | 0 | Legacy IRQ4 | Production `ManagedSerialDriver` |
| E1000e | `(PCI, 0x808610D3, E1000 cause)` = `(1, 0x808610D3, 3)` | 2 | MSI vector `0x51` | `ManagedE1000Driver` |
| i8042 keyboard | `(platform keyboard, 1, keyboard scancode)` | 1 | Legacy keyboard route | Existing keyboard service |

The configured route table has capacity 3. Route identity is device kind, device ID, and event type; the existing subscription token distinguishes an active subscription generation. Phase 82 did not add a new routing key or use a route-0 fallback.

Both device routes use the same native FIFO: capacity 8 records, maximum drain 4 records per call. Each record contains event type, device kind, device ID, and a global sequence number. Sequence numbers are global and monotonically increase across routes; each managed driver also validates its own last delivered sequence. The Phase 82 interleaving reached no queue drops, drained the FIFO after every bounded dispatch, and preserved per-route ordering.

## QEMU acceptance

The acceptance runner used QEMU 11.0.0, `e1000e,netdev=net0,addr=2`, and the bounded local user-mode backend. The E1000 trigger reused the Phase 80 proof-style TX submission and DD polling only to produce a real TXDW interrupt. COM1 input was injected through the QEMU serial device and captured by the UART IRQ4 route.

Each boot first retained the Phase 80 four-event E1000 regression while COM1 was already registered. It then delivered one COM1-only event and ran this six-event interleaving while both subscriptions remained active:

| Boot | Selector | Six-event order | COM1 deliveries | E1000 TXDW deliveries | Phase80 raw MSI entries |
|---:|---|---|---:|---:|---:|
| 1 | A | COM1 → E1000 → COM1 → E1000 → E1000 → COM1 | 4 | 8 total, including 4 Phase80 events | 4 |
| 2 | B | COM1 → COM1 → E1000 → E1000 → COM1 → E1000 | 4 | 8 total, including 4 Phase80 events | 4 |
| 3 | C | COM1 → E1000 → E1000 → COM1 → E1000 → COM1 | 4 | 8 total, including 4 Phase80 events | 4 |

After the six-event sequence, the guest unsubscribed COM1 and delivered another E1000 TXDW event with no COM1 delivery. It then replaced the COM1 subscription, unsubscribed E1000, and delivered another COM1 IRQ4 event with no E1000 delivery. The replacement COM1 token differed from the original token.

| Boot | COM1 hardware events / native queued / managed | E1000 TXDW events / native queued / managed | Cross / wrong-device / wrong-type deliveries | E1000 raw route entries / APIC EOIs |
|---:|---:|---:|---:|---:|
| 1 | 4 / 4 / 4 | 8 / 8 / 8 | 0 / 0 / 0 | 8 / 8 |
| 2 | 4 / 4 / 4 | 8 / 8 / 8 | 0 / 0 / 0 | 8 / 8 |
| 3 | 4 / 4 / 4 | 8 / 8 / 8 | 0 / 0 / 0 | 8 / 8 |

The four COM1 hardware events generated four route captures and four COM1 EOI acknowledgements per boot. The eight E1000 TXDW events generated eight typed records and eight ICR cause acknowledgements per boot. COM1 events did not change E1000 cause or EOI counts; E1000 events did not change COM1 receive or EOI counts. The native queue reported zero drops. After E1000 teardown, its raw handler-entry count remained unchanged during the bounded quiet interval; no interrupt storm was observed.

## Subscription, replacement, and stale-token checks

- Both COM1 and E1000 subscriptions were active before the first Phase82 event. Their tokens were distinct (`0x3` and `0x4` in each fresh boot).
- COM1 unsubscribe disabled only slot 0; slot 2 remained active and delivered another real E1000 TXDW event.
- COM1 re-subscribe produced a new token. Native host coverage passed the original COM1 token directly to the production unsubscribe callback and got `NOT_FOUND`; the replacement COM1 and E1000 subscriptions stayed active.
- E1000 unsubscribe disabled only slot 2; the replacement COM1 route remained active and delivered another real IRQ4 event.
- Native host coverage passed the stale E1000 token directly to the production unsubscribe callback and got `NOT_FOUND`; COM1 and an E1000 replacement stayed active.
- Wrong event type and wrong device identity were rejected by host coverage without selecting route 0.

## Validation

| Check | Result |
|---|---|
| ManagedKernel NativeAOT build | Passed. Payload: 4,805,120 bytes; SHA-256 `D70F59A74DD8FBB217508F07C3682A980F4D3169BC78BD5F60876A14E0373B8D`. |
| Ordinary ManagedKernel Gate4 build | Passed without `-EnablePhase82ConcurrentManagedInterrupts`. EFI SHA-256 `117842A1E23A83506031B2B63D2F2F48C0AF6CF56F71191CCE65FEC0CC84E06B`. |
| Phase82 acceptance Gate4 build | Passed with the opt-in acceptance switch and the QEMU firmware UTC assumption. EFI SHA-256 `1E2DD15216C3F819AF24871EC478B671B79DF282EA14DDAED7F4FC4F9EE9E658`. |
| Managed interrupt host harness | Passed: toolchain discovery, 84 production sources / 85 host compile items / 0 duplicates, 15/15 assertions. |
| Native interrupt host tests | Passed, including two-route FIFO identity, ordering, bounded-capacity coverage, and independent unsubscribe/replacement. |
| Driver binding host tests | Passed, including wrong identity/type rejection, concurrent distinct tokens, independent teardown, and stale COM1/E1000 token rejection. |
| Phase14 E1000 host tests | Passed: 33 assertions. |
| Driver service owner and status | Passed. |
| Driver worker | Passed. |
| Service host tests | Passed. |
| Managed worker API regression | Passed for Phases 61, 62, 64, 65, and 66. |
| Phase11/12/13 + Phase80/82 QEMU acceptance | Passed: 3/3 fresh boots. |
| PowerShell runner parsing | Passed for the Gate4 builder and both fresh-boot runners. |
| `git diff --check` | Passed. |

SyntheticScheduler was not required because scheduler code was not changed. Event API host coverage was not required because event API semantics were not changed.

## Evidence

Final accepted evidence directory: `artifacts/phase82-qemu-evidence-final6/` (generated and ignored by Git). Each boot directory includes its serial log, host injection log, event timeline, QEMU command line, firmware identity, and QEMU stdout/stderr.

| Boot | Serial log SHA-256 | Injection log SHA-256 | Timeline SHA-256 |
|---:|---|---|---|
| 1 | `49A7C4D03ED929A80CA956D6653E7FBB46D5D30A30268D50F87E9B1C6E7FB153` | `09F5C09E78AE0695D4F97FD9B52FA78F16234D1E19DE18FE7108D2C9BC3066F9` | `F24AFE339C1E1D4D345B0DF8A621279441F2E6CB519F64A20CE42AD63434DC52` |
| 2 | `7210279C7E9446C3A6DB5FA4360D4642991DA153D5E5C3E21C3C4C169A6D3801` | `488EA6C47BBD0F24FFA9004894F3EABCCC81908859B8E1BBA9480A7EF5C7929C` | `0849B7BA793CAF20B384A73C78D54BACE44B828DAC85B48BA82F519C41FDC2F2` |
| 3 | `934181BD7FF5A733C7A00936F7626BB37A77224D3D3A8C77637F6613A363FE15` | `6429FAC86F5D5E4485731B3FEC53C3102D2EE9288CD7CD79D8792D9AE0C54363` | `E78392B7E743910EFEE30E544936D962B1C00EC660697A3F29D227FBB94BA757` |

## Limits and follow-up

Phase 81 remains pending. Phase 81R2 remains unresolved. Normal E1000 TX completion still uses DD polling; the Phase 82 proof uses that bounded polling only to create real TXDW interrupts. This result covers concurrent managed route isolation in the QEMU fixture. It does not add RX networking, DHCP, IP, DNS, physical NIC support, or a general interrupt-controller redesign.

The smallest next objective is to resume the separately documented Phase 81 TX-completion work only after its scheduler-wait and timeout blockers are resolved. Phase 83 was not started.

## Git starting state

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `01e663d8ca67caf211d824114a8927bd89001fb7` — `Document Phase 81R2 managed wait blocker`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: ahead 0, behind 0
- Starting worktree: clean
- `.phase`: absent before the work and remains absent

The source changes and this report are being committed together as `Validate concurrent managed interrupt routing`. A single normal push is attempted after the commit; its result is reported in the task closeout.

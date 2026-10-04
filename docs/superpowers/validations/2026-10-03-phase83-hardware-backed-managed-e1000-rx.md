# Phase 83 — Hardware-Backed Managed E1000 RX Interrupt and Local Frame Proof

**Date:** 2026-10-03
**Prompt phase:** 83
**Outcome:** A — hardware-backed managed RX validated.

> Phase 83 proves a local hardware-backed managed E1000 receive path. It does not claim DHCP, IPv4, Internet connectivity, interrupt-driven synchronous TX, or physical-NIC support.

## Result

Three fresh QEMU boots each injected four deterministic local Ethernet frames through QEMU's `e1000e` device. Every frame was DMA-written to the existing guest RX ring, notified by a real RXT0 interrupt on MSI vector `0x51`, captured with the authoritative ICR value by native code, dispatched as the existing typed E1000 managed interrupt event, validated and recycled by `ManagedE1000Driver`, and followed by continued Phase12/Phase14 progress.

The first accepted Phase83 event sequences were `0x0E` through `0x11`, after the four Phase80 TXDW records. Per boot, native and managed counters reported four RX events, four acknowledged RXT0 causes, four frame validations, four descriptor recycles, and zero interrupt storms. The RX proof is bounded to one initial frame plus three repeats.

No pre-existing production defect was found. The existing driver already allocated and initialized an RX ring and had a separate polling receive path; the Phase80 managed interrupt handler only accepted TXDW. This change adds opt-in RXT0 classification and bounded RX descriptor/frame handling to that existing driver and acceptance harness. It does not replace the normal RX polling path.

## Device, ring, and cause

- QEMU 11.0.0 `e1000e`, PCI `0000:00:02.0`, ID `8086:10D3`, BAR0 `0x81060000`, MSI vector `0x51`.
- Runtime-configured guest MAC: `52:54:00:12:34:56`.
- The driver retains its existing 16-entry legacy RX descriptor ring. Each descriptor is 16 bytes; `RDLEN=0x100` (256 bytes). Each RX buffer is 2048 bytes; the maximum accepted length is 2048 bytes.
- In boot 1, the RX ring base read back as `RDBAL=0x04CF3000`, `RDBAH=0`; `RDH=0`, `RDT=0x0F` before injection. The RX control register was `RCTL=0x04008002` (receive enabled, broadcast accepted, 2048-byte buffers, CRC stripped). Later boots initialized the same ring layout with per-boot DMA addresses.
- The ring uses the existing DMA service allocation: descriptor and packet storage have validated bus addresses and remain allocated while hardware owns them. The hardware sees DMA allocations, not movable managed-array addresses. Managed code reads through its bounded DMA allocation wrapper and only returns a buffer after consumption.
- Initial ownership is descriptor/buffer preparation by software, device ownership after `RDT=15`, DMA completion with DD/EOP, bounded managed consumption, descriptor reset, and return by writing the consumed index to RDT. The accepted descriptors were indices 0, 1, 2, and 3; final RDT was 3.
- The selected receive cause is RXT0, ICR bit 7 (`0x80`). The acceptance mask is TXDW plus RXT0 (`IMS=0x81`); the pre-enable readback was 0 and the post-enable readback was `0x81`. This preserves Phase80 TXDW bit 0 and does not enable unrelated E1000 causes.
- TX and RX use the same typed `GxManagedKernelInterruptEventV1` event (event type 3, PCI device ID `0x808610D3`). The event carries the full cause/status and global sequence. An observed raw ICR cause was `0x80000080`: RXT0 was set and the unrelated asserted bit remained represented. Native host tests also verify that combined RXT0+TXDW (`0x81`) survives capture and acknowledgement.

## Local frame and RX proof

The host injector uses QEMU's private `-netdev dgram` endpoint bound to `127.0.0.1`; a host loopback UDP socket sends Ethernet frames to that endpoint after the guest reports `PHASE83_RX_READY`. No LAN, public network, DHCP, DNS, IP, TCP, or Internet connection is used. The acceptance fixture is compile-time opt-in; the normal Gate4 image is built without the Phase83 acceptance define.

Each of the four 60-byte frames has:

- destination `52:54:00:12:34:56`, read from the guest's runtime NIC configuration;
- source `02:15:00:00:00:01`;
- private EtherType `0x88B5`;
- ASCII sentinel `guideXOS Phase83 RX`;
- a big-endian sequence value 1–4 and zero padding to 60 bytes.

The injector records each attempted frame, destination, backend, endpoint, and SHA-256. The guest validates the descriptor length and bounds, DD and EOP status, a zero error byte, destination MAC, source MAC, EtherType, sentinel, sequence, and padding. The RX error mask rejects any set error byte bit; status acceptance requires DD and EOP. Other status flags are not separately filtered. No IP parsing is done.

For each received frame, `ManagedE1000Driver` handles one queued typed event in deferred managed dispatch, inspects only the current expected descriptor and its 60-byte frame, then clears the descriptor completion/length/error state and advances RDT. The IRQ path only captures cause/status, queues bounded route state, and acknowledges the interrupt; it does not copy a packet or run the managed frame validator. The managed receive copy uses a fixed 60-byte driver buffer. RX proof counters and expected descriptor indices are fixed-size arrays; no unbounded allocation was added.

All four descriptors were DD+EOP (`status=0x03`), error-free (`errors=0`), and exactly 60 bytes. Each frame's fields passed validation and each consumed descriptor was recycled exactly once. The native follow-up ICR read was 0; four RX causes were acknowledged; the interrupt-storm counter was 0. A full hardware ring wrap was not exercised: the 16-entry ring consumed indices 0–3. Phase14 host tests cover index wrap from 15 to 0 and subsequent advance to 1.

## Coexistence and regression results

- The COM1 managed route remained registered and hardware-enabled during all RX receives. The Phase83 fixture did not inject a COM1 byte during the RX window; its managed delivery count stayed unchanged, and the route remained isolated. Route coexistence and interleaving are separately covered by Phase82 host tests and the accepted Phase82 QEMU report.
- Four real Phase80 TXDW events still reached managed handling in each boot. TXDW cause classification and the combined TXDW/RXT0 bit representation remain covered by managed and native host tests. No extra TXDW frame was triggered after the RX mask update. Normal synchronous TX still polls DD; this is not Phase81 completion.
- The shared native managed-interrupt FIFO has capacity 8 and drains at most 4 records per call. RX acceptance accounted for four enqueued and drained route events with no new drops. The observed queue high-water mark before RX was 2. The proof dispatcher accepts no more than one E1000 managed delivery per RX dispatch iteration.
- The RX event contains no native pointer. DMA ownership is held until software validates and consumes the descriptor, and teardown disables the device before releasing its DMA allocations.
- Negative host coverage rejects an empty cause, an RX event without DD, DD without EOP, error bits, lengths below 60 or above the 2048-byte buffer, wrong destination/source, wrong EtherType, wrong payload/sequence, duplicate/stale event sequence, and an already-cleared/recycled descriptor. The host tests also cover ring-index wrap. A wrong-MAC packet was not sent through QEMU to test the device's filter behavior.
- No combined TXDW+RXT0 arrival was forced on QEMU. Cause preservation is covered by managed/native host tests, not claimed as a simultaneous hardware observation.
- No scheduler wait, timeout owner, timer service, blocking semantics, general network receive queue, Ethernet upper-layer demultiplexer, or Phase81R2 path was changed.

## Validation

| Check | Result |
|---|---|
| ManagedKernel NativeAOT build | Passed; 4,813,312-byte payload. |
| Gate4 ordinary build | Passed without `GXOS_ENABLE_MANAGED_KERNEL_PHASE83_RX`. |
| Gate4 Phase83 acceptance build | Passed with the opt-in acceptance define. |
| Managed interrupt host harness | Passed: .NET SDK 10.0.401 selected; source completeness 84 production sources / 85 host compile items / 0 duplicates; 15/15 assertions. |
| Native interrupt host tests | Passed, including RX cause capture, simultaneous TXDW/RXT0 cause preservation, and acknowledgement. |
| Phase14 E1000 managed host tests | Passed: 51 assertions covering RX cause, descriptor completion/bounds/errors, frame validation, recycle, stale/duplicate sequence, and ring wrap. |
| Managed driver binding host tests | Passed. |
| Driver service owner/status host tests | Passed. |
| Service host tests | Passed. |
| Driver worker host tests | Passed. |
| Phase82 route regression | Passed in native/managed route host coverage; COM1 route also stayed active and isolated during all three Phase83 RX windows. No COM1 frame was injected during the RX window. |
| QEMU acceptance | Passed: 3/3 fresh boots. Each boot accepted four frames and completed the guest Phase12/Phase14 progress markers. |
| PowerShell parse and `git diff --check` | Passed. |
| SyntheticScheduler / managed-worker API regression | Not run; scheduler and shared managed worker APIs were not changed. |

### QEMU run evidence

Evidence is generated and ignored by Git under `artifacts/phase83-qemu-evidence-accepted/`. Each run directory also contains the QEMU command line, firmware identity, stdout/stderr, and per-event timeline.

| Boot | Serial SHA-256 | Injector SHA-256 | Timeline SHA-256 |
|---:|---|---|---|
| 1 | `957401E5B9D7B48668B303A949EB11E3E9FFCDAC9798FB20B0D194F6FA7995D5` | `02884A1A1C595F1C80691FC002602279A93F7725592D339ADBDBC3176D8BBDEB` | `764E8FF97B28AE48BD4E944E4286A69CCE739207B1F2DC5E875011F5E3A95151` |
| 2 | `61FFC9044CC0EF83368736C41F34A30F2B5B28F65D4F6E320BFBF2CA348D3B17` | `F09293AB07BF039A9C4AF022BE8F1F283ECCF52E7363743253CEA1EDC6418D99` | `E50BEB329A62B5D7C432ACFC37150D48841E4E22595B6F46BD4C9CBFBDBEE7A4` |
| 3 | `200EAAEE0E426A9ED987C7758D52B55A1314610C3AE490D9AC8D4FA3F002FF53` | `7C9D3B15BAB14404E987DCE339EBCBDCEAF51E8BF9C766C1C3860A3A7DE99BC7` | `67CD01169741ECDF795F9DFB7AA17A9ABA552301A2C5B1A8A99CAC37144B001F` |

Identical frame inputs were recorded on all boots. Sequence 1–4 frame SHA-256 values were respectively `A75681764206A479932ED5A84F5D200A5C42E677F71447B1A920C292564F078A`, `9DE2B219DB59300F2A12637213DF585EE216D157EDB31A20899AA60E983FFFA4`, `3475EA316B13982D4DEDFE7BC074439BFB87525D81D07A237B5583DE48A87703`, and `A5A91136B77809029E1BE5CAEC3A03A4A99CA944FF787D795BBF422A411DCBDE`.

| Artifact | Size | SHA-256 |
|---|---:|---|
| ManagedKernel payload | 4,813,312 bytes | `28A47CF238C8110E66568418166073F9758D5CFBBAD277F5D18342BCC7CD0BA3` |
| Ordinary Gate4 EFI image | 742,098 bytes | `29AC1CB3B22B6638351EB1240DC0A943B68914145B43D95AF9F65297F4062DF5` |
| Phase83 acceptance Gate4 EFI image | 748,863 bytes | `E32009215FC1E65C6F4162AF097B73BA92B47404BB17995261931C09A7CB8A38` |

## Phase status and limits

Phase81 remains pending, Phase81R2 remains unresolved, and normal E1000 TX still polls descriptor DD. This acceptance result proves local QEMU receive only; it does not validate DHCP, IPv4, Internet connectivity, physical NIC operation, hardware ring wrap, wrong-MAC filter behavior, or simultaneous TX/RX interrupt timing.

The smallest next objective is to resolve the separately tracked scheduler-wait blocker and continue Phase81 TX completion work as its own bounded task. Do not interpret Phase83 as authorization or completion of Phase84.

## Git state at preflight

- Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`
- Branch: `nativeaot-managed-kernel-integration`
- Starting HEAD: `4acbae41722a4edb55eff6e41031a7b25fae2b40` — `Validate concurrent managed interrupt routing`
- Upstream: `origin/nativeaot-managed-kernel-integration`
- Starting divergence: ahead 0, behind 0 (live state; the prompt's expected divergence was stale)
- Starting worktree: clean
- `.phase`: absent before the work; no `.phase` file was introduced.

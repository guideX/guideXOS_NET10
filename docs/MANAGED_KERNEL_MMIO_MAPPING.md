# ManagedKernel Phase 13: Bounded MMIO Mapping

Phase 12 deliberately stopped at native-authoritative resource discovery and
bounded ownership. It did not provide generic MMIO because the existing
substrate had not yet established a safe physical mapping path or a proven
device cache policy. Phase 13 adds the smallest capability layer needed to
map one already-authorized MMIO resource and prove one read-only PCI register
access.

The security boundary is:

```text
native discovery -> immutable resource publication -> managed claim
                 -> validated native MMIO mapping -> opaque managed handle
                 -> bounded volatile read -> unmap -> claim release
```

There is no managed API that accepts a physical address. PCI configuration
writes, BAR sizing probes, arbitrary physical mapping, and MMIO writes remain
outside the contract.

## Actual x86-64 architecture

The Phase 13 audit found the following architecture in the current loader and
QEMU profile:

- The loader clones the current x86-64 PML4 into a private CR3 and owns the
  page-table pages it adds. The current paging mode is four-level, 48-bit
  canonical addressing, with NX enabled in the accepted QEMU boot.
- The normal private VM arena is the lower-canonical range beginning at
  `0x0000400000000000` and extending for `0x40000000` bytes (1 GiB). It is a
  bounded arena for managed runtime commitments and is not a generic direct
  map.
- The original page tables contain identity/firmware aliases for the measured
  UEFI coverage. Those aliases are not treated as a durable MMIO interface:
  their lifetime, ownership, and cache type are insufficient for arbitrary
  managed access.
- Page-table modification remains native-owned by the loader VM substrate.
  Managed code can call only the resource and MMIO callbacks, which accept
  resource/claim/mapping handles and scalar offsets.
- A separate fixed MMIO arena is reserved in the same PML4 branch but outside
  the normal 1 GiB arena. It begins at `0x0000400040000000` and is
  `0x10000000` bytes (256 MiB) long. The separate reservation and ledger
  prevent a device mapping from silently aliasing a normal managed commitment.
- Page-table intermediate pages are allocated from the existing bounded
  private table-page owner. They are retained as bounded paging ownership;
  leaf mappings are explicitly removed on unmap. Each changed leaf receives
  `INVLPG`.

The MMIO window is page aligned, remains within one PML4 entry, uses
deterministic first-fit allocation, and has no user mapping or executable
permission. The native mapping table is fixed capacity, so virtual address
allocation cannot grow an unbounded registry.

## Cache policy

Only uncacheable MMIO is supported in this phase. The native boot path probes
CPUID for PAT support, reads `IA32_PAT` (`0x277`) and reads
`IA32_MTRR_DEF_TYPE` (`0x2FF`). It does not reprogram PAT or MTRRs.

In the accepted QEMU boot the observed PAT value is:

```text
IA32_PAT = 0x0007040600070406
```

PAT entry 3 is byte zero, which is the architectural UC type. MMIO leaf PTEs
use `PWT=1`, `PCD=1`, and `PAT=0` (`PTE cache flags = 0x18`), selecting PAT
entry 3. The mapping is read-only, NX, and uses the UC encoding rather than
the ordinary write-back leaf encoding. The MTRR default-type MSR is audited
and reported (`0xC06` in QEMU); it is not globally changed. The service fails
closed if PAT is unavailable, the required PAT entry is not UC, or the cache
policy cannot be proven.

The service also rejects an MMIO descriptor that overlaps a RAM-like UEFI
memory-map descriptor. This prevents a BAR mapping from becoming an alias for
ordinary RAM even when a firmware-provided range is incomplete.

## Native mapping service

`src/Gate4Harness/managed_kernel_mmio.c` owns a fixed service state. It binds
to the immutable native resource descriptor array and the resource-map
generation used to publish it. Each claim record stores the resource ID,
driver owner, mapping count, and a generation-protected opaque handle. Each
mapping record stores the claim handle, requested resource-relative range,
page span, virtual allocation, and generation-protected opaque handle.

The current limits are:

| Quantity | Limit |
|---|---:|
| Published descriptors | 64 |
| Simultaneous claims | 16 |
| Simultaneous MMIO mappings | 8 |
| Pages in one mapping | 64 |
| Dedicated MMIO window | 256 MiB |

Mapping derives `PhysicalBase + offset` only after finding a live claim and
checking its owner. It rejects non-MMIO resources, non-read-only access,
zero-length or overflowing ranges, ranges outside the descriptor, invalid
physical/page arithmetic, capacity exhaustion, overlap, stale handles, and
unproven cache policy. The physical range is rounded only inside native
state; the physical address never crosses the managed ABI.

The page span is computed as:

```text
physical_start = floor(resource.PhysicalBase + offset, 4096)
page_offset    = (resource.PhysicalBase + offset) mod 4096
mapped_length  = ceil(page_offset + requested_length, 4096)
```

Every addition and rounding step is overflow checked, and the page count is
bounded to 64. Virtual allocation is deterministic first-fit inside the
dedicated window and records are checked for overlap before page-table edits.
Mapping failures roll back leaf edits made by the range planner.

## Managed capability and lifetime

`ManagedMmioMapping` is an opaque managed object. It has no pointer, physical
address, general pointer conversion, or `Span<byte>`. It exposes only
`TryRead8`, `TryRead16`, `TryRead32`, `TryRead64`, and `TryUnmap`.

Each read rechecks the live mapping state, owner, width, alignment, offset,
overflow, and mapping bounds. Native reads use volatile 8/16/32/64-bit loads
and a compiler memory barrier. No MMIO write operation is exported.

The explicit lifetime rule is:

1. claim the published resource;
2. create a read-only mapping from the claim;
3. read through the opaque mapping;
4. unmap the mapping;
5. release the claim.

Releasing a claim with a live mapping is rejected. Unmap is required before
release, double unmap is rejected, and generation counters make a stale
mapping or claim handle invalid after slot reuse. Managed accounting is
restored only after a successful native unmap/release. The Phase 13 managed
proof also roots a live mapping across `GC.Collect()` and repeats the full
acquire/read/unmap/release sequence three times.

## First PCI proof

The existing side-effect-free PCI BAR decoder is used for the emulated
target:

```text
BDF       0000:00:02.0
Vendor    0x8086
Device    0x10D3
Class     02/00/00
BAR       0
Raw base  0x81060000
```

The canonical OVMF profile did not expose a matching EFI_PCI_IO resource
protocol range for this device. The loader therefore publishes the BAR only
after matching the read-only decoded base to a retained UEFI memory-map
exclusion page. That conservative authoritative representation is one
page (`0x1000`) at `0x81060000`, sufficient for the documented e1000-family
`STATUS` register at offset `0x0008`. The publication records the authority
as `UEFI_RAM_EXCLUSION_PAGE`; it is not a BAR-sizing probe and no PCI
configuration register is written.

After Phase 9/10/11 initialization, the managed Phase 13 proof locates the
device through the existing inventory, finds the matching resource, claims
it, maps resource offset `0x0` for `0x10` bytes, and reads `STATUS` at
offset `0x8` twice. The accepted QEMU observation is `0x00080283`; the test
requires completion, equality across repeated reads, and rejection of the
unmapped-bus sentinel rather than a fragile exact hardware value. It then
proves GC survival, negative ranges, live-claim release rejection, stale and
forged handles, teardown, and accounting restoration.

The managed-kernel sequence runs this proof after the established
Phase 9/10/11 worker, interrupt, serial, keyboard, and GC activity. Phase 12
resource teardown runs afterward so the Phase 13 mapping/claim lifetime is
complete before the immutable resource catalog is destroyed.

## Deliberately unsupported

Phase 13 does not implement:

- arbitrary physical-to-virtual mapping or managed page-table manipulation;
- PCI configuration writes or destructive BAR sizing probes;
- MMIO writes, device reset, MAC/link configuration, EEPROM writes, or
  register programming;
- bus mastering, DMA, RX/TX descriptors, packets, NIC interrupts, MSI/MSI-X,
  or legacy PCI INTx handling;
- a general pointer, raw virtual address, or managed byte span over MMIO;
- cache policies other than the proven UC policy.

The next logical phase is the first operational managed PCI device driver,
which must add its own ownership, interrupt, DMA, and register-write
contracts explicitly rather than widening this mapping capability implicitly.


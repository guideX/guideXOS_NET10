# NativeAOT virtual-memory foundation

This milestone adds the substrate below the still-unresolved
`KERNEL32.dll!VirtualAlloc` boundary. It does not register `VirtualAlloc`,
`VirtualFree`, write-watch, or protection handlers.

## Phase 0 result

The immutable payload was disassembled at RVA `0x438A8`. The helper at
`0x43890` supplies `RCX = NULL`, `RDX = [0x1800AE808]` (the current guideXOS
value is `0x1000`), `R8D = 0x202000`, and `R9D = PAGE_READWRITE`. It tests the
return value. A null result returns false to its only observed caller at
`0x6368E`; that caller skips the capability byte write and continues with the
next initialization calls. A non-null result is immediately released through
`VirtualFree(ptr, 0, MEM_RELEASE)` and returns true. Failure is therefore a
non-fatal capability-probe fallback. It is not write-watch support, and a
future API handler must reject `MEM_WRITE_WATCH` unless write tracking exists.

## Live policy

The live dynamic arena is a page-aligned one-GiB range at
`0x0000400000000000`. NULL-address reservation placement uses the lowest
suitable 64-KiB-aligned base; reserved byte length is independently rounded to
4-KiB page granularity, matching the observed Windows region sizes. The manager
stores requested size, rounded size,
ownership, state,
generation, and sparse page commitments. A synthetic arena can represent a
63-GiB reservation without allocating proportional memory.

The paging context allocates a guideXOS-owned PML4, copies the active PML4,
requires the selected PML4 slot to be unused, and creates only private PDPT,
PD, and PT pages beneath that slot. Existing firmware page-table subtrees are
shared read/write through the copied root and are never split in place. New
data pages are allocated through UEFI `AllocatePages`, zero-filled through the
known physical alias, recorded in the physical ledger, and mapped as Present +
Writable with NX when EFER.NXE is enabled. Unmapping clears the PTE and uses
`invlpg` after CR3 activation. Empty paging infrastructure is retained as
accounted paging overhead after a temporary test is cleaned up.

The internal commit transaction page-rounds safely, preserves already
committed pages, allocates only missing backing, rolls back mappings/backing/
bookkeeping on failure, and never relocates the requested virtual range.

The public NativeAOT boundary remains unchanged: the first unresolved import
continues to be `KERNEL32.dll!VirtualAlloc`.

## QEMU proof snapshot

Three fresh Gate4Harness boots passed the paging-context, existing-mapping,
Boot Services, temporary zero-fill/map/unmap, cleanup, accounting,
GlobalMemoryStatusEx, scheduler, and unresolved-import checks. The observed
firmware state was `CR0=0x80010033`, `CR3=0x7801000`, `CR4=0x668`,
`EFER=0xD00`, NXE enabled, with a four-GiB leading identity map composed of
`0x200` 4-KiB and `0x7FF` 2-MiB mappings. The owned root was
`0x5474000`; the old root was `0x7801000`. The payload hash remained
`2F66A6E85B61C48E87238EC972C9681B15084340C6F3C86F2FCA5EDC7FC3F837` on all
three boots.

# Managed Kernel Phase 53: bounded CSS image resources

Phase 53 extends the Phase 52 PNG path with a deliberately small CSS resource
subset: `background-image: none` and one `background-image: url(...)` value,
plus `background-repeat: repeat` or `no-repeat`. It does not add a general CSS
value tree, a cache, parallel loading, or recursive resource loading.

## Resource model

The CSS engine retains a fixed table of compact `ManagedCssImageReference`
records. Each record stores the declaring owner, inline/embedded/external
origin, stylesheet index, bounded raw URL length, and a small URL arena. Raw
CSS bodies and response bodies are never retained as resource dependencies.

External stylesheet provenance is kept in the page orchestrator. The base for a
CSS image is the stylesheet's final URL after redirects; inline and embedded
style use the document's final URL. The image URL is resolved only after
cascade, so losing declarations do not initiate requests. The page resource
graph is sequential and fixed at two edges from the HTML root:

```
HTML -> stylesheet -> PNG
HTML -> PNG
```

`@import` and all deeper resource references remain unsupported, so the
currently supported graph cannot form a cycle.

HTML and CSS image consumers share the Phase 52 fixed image descriptor/pixel
store. Identical canonical final URLs use the existing image slot and attach a
bounded source-node alias. CSS image capacity is charged against the same
image-store budget; there is no second CSS pixel arena. Reset clears CSS
references, stylesheet bases, dedup state, handles, descriptors, and decoded
pixels.

## Style, paint, and raster behavior

The computed style stores a bounded image reference identity, resolved image
handle, and repeat mode. Background images are emitted as a dedicated paint
command after the background color and before the border. The rasterizer clips
to the padding/background box, the ancestor clip stack, and the framebuffer.
`no-repeat` draws one intrinsic-size tile at the box origin; `repeat` uses
bounded modulo tiling. Existing alpha blending is used once per source pixel.
The border therefore remains authoritative over the image, and existing text
painting remains later in the display-list order.

## Bounds and failure policy

The supported CSS URL grammar is ASCII-case-insensitive `url(...)`, with
quoted or unquoted URLs, bounded whitespace, and a non-empty URL no longer
than `ManagedCssLimits.MaximumValueLength` (512 bytes). Controls, escapes, gradients, multiple
layers, shorthand, `background-size`, and arbitrary positioning are rejected
or ignored as unsupported declarations. Reference-table overflow, URL/scheme
failure, MIME failure, transport failure, PNG decode failure, and shared-store
capacity failure map to CSS-specific page failure reasons. A CSS image is
accepted only as `image/png` and is decoded by the Phase 52 PNG decoder,
including its existing bounds, CRC, zlib, filter, and alpha checks.

## Proof coverage

`src/ManagedKernelPhase53HostTests` contains 540 deterministic assertions for:

- quoted, unquoted, mixed-case, whitespace, fragmented, malformed, and
  unsupported CSS image values;
- cascade winning-only behavior, `none`, `!important`, non-inheritance,
  inline origin, external stylesheet provenance, final-URL resolution, and
  exact reference capacity;
- shared image aliases and reset behavior;
- background-color/image alpha interaction, no-repeat/repeat rasterization,
  border ordering, clipping, and image-command validation.

The existing Phase 52 host suite remains green at 651 cases.

The managed kernel proof logs the following Phase 53 markers after validating
the HTML `<img>` path, CSS reference/fetch/decode counters, stylesheet final
URL, resolved image URL, paint commands, raster output, and GOP presentation:

```text
GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_REFERENCE_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_FETCH_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_DECODE_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_BACKGROUND_PAINT_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_GOP_PRESENT_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_VISIBLE_CSS_IMAGE_PASS
GXOS_NET10:MANAGED_KERNEL_PHASE53_PASS
```

The Gate4 runner has a deterministic Phase 53 fixture with the request order
`/phase53/index.html`, `/phase53/css/theme.css`,
`/phase53/images/content.png`, `/phase53/images/background.png`. The CSS URL
`../images/background.png` is resolved against the external stylesheet final
URL, not the HTML URL. A CSS-origin bad-CRC control is also wired and expects
CSS image failure, no background paint, no visible-page marker, and restored
Phase 14 accounting.

## GC/RX acceptance blocker

The Phase 53 guest reaches `MANAGED_KERNEL_PHASE53_MODE_SELECTED`, completes
the E1000 proof TX, and then stops in the pre-existing Phase 14/15
GC-survival prerequisite. The witness payload recorded:

```text
MANAGED_KERNEL_PHASE15_GC_PROBE_BEGIN
MANAGED_KERNEL_PHASE15_GC_PROBE_RETURNED
MANAGED_KERNEL_PHASE15_GC_ROOTS_LIVE
```

No `MANAGED_E1000_RX_READY` marker follows. The temporary `-cpu max`
experiment was removed; the exact historical reproduction uses the default
QEMU CPU plus virtio-rng. With the boundary-scoped Gate4 diagnostic handler,
payload `E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388`
(4,790,272 bytes) faulted after `MANAGED_KERNEL_PHASE15_GC_PROBE_BEGIN`:

```text
FAULT_VECTOR=0x000000000000000E
FAULT_ERROR=0x0000000000000000
FAULT_RIP=0x0000000004D91090
FAULT_CR2=0x0000400002431000
FAULT_REG_RBX=0x0000400002431000
FAULT_REG_R15=0x0000400002405C20
FAULT_GC_OBJECT=0x0000400002405C20
FAULT_GC_SLOT=0x0000400002431000
FAULT_GC_SLOT_OBJECT_OFFSET=0x000000000002B3E0
FAULT_GC_SLOT_IS_COMMITMENT_FRONTIER=1
FAULT_GC_PREDECESSOR_COMMITMENT_BASE=0x0000400002430000
FAULT_GC_PREDECESSOR_COMMITMENT_BYTES=0x0000000000001000
FAULT_GC_OBJECT_HEADER=0x0000400002405E99
FAULT_GC_METHOD_TABLE=0x0000400002405E98
FAULT_GC_METHOD_TABLE_IN_IMAGE=0
FAULT_GC_METHOD_TABLE_IN_VIRTUAL_ARENA=1
FAULT_GC_METHOD_TABLE_VALID=0
```

The linked NativeAOT PDB and disassembly resolve `RVA=0x17A090` to
`WKS::gc_heap::mark_object_simple1(unsigned char *, unsigned char *) + 0x180`,
whose exact instruction is `mov r9,QWORD PTR [rbx]`. The PDB's register
locations identify `R15` as the `oo` object parameter and `RBX` as the local
`ppslot` field-slot address. Thus `0x400002431000` is the invalid slot address
being read, not a successfully loaded managed reference. The fault occurs
before the following `queue_mark` path can consume a field value.

The object header is already invalid at this boundary: its masked MethodTable
value is inside the managed arena and outside the loaded image, so no EEType or
GC descriptor can be resolved from this object without guessing. This is
stronger than a generic “GC candidate” label, but it does not identify the
managed producer/root that corrupted the object header.

The in-guest page walk found the target PTE absent. The immediate predecessor
ledger page is `[0x400002430000, 0x400002431000)`; the aggregate GC PAL call
that created the surrounding run was `VirtualAlloc(0x400002421000, 0x10000)`,
whose exclusive end is exactly `0x400002431000`. No VirtualAlloc call
requested the candidate, and no decommit/release preceded the fault. The
candidate lies inside the live managed reservation but outside committed/mapped
pages. This is a genuine frontier observation, not an arbitrary mapping or an
allocator correction.

The clean current payload (`9B72B8B939789C69D6937FB4933DBA0EE267EA03B463ECC8019562D7B2C64F18`)
was also run with the same boundary harness. It selected Phase 53 and reached
E1000 TX completion, then faulted earlier in the same GC scan at
`RVA=0x17A159`, instruction `mov ecx,DWORD PTR [r10]`, with
`r10=0xF2000000000529B8` (noncanonical), `RBX=oo+0x80`, and `CR2=0`. It
reported the same invalid `oo` header/arena MethodTable shape. This current
payload result is not substituted for the requested `0x400002431000` candidate;
it corroborates an earlier invalid-object/reference state in the NativeAOT GC
walk, not a CSS/PNG or E1000 MMIO access.

The known-good Phase 52 payload and Gate4 EFI were also run through the
current runner. That control stopped earlier at
`MANAGED_KERNEL_START_BLOCKED=0x6`, the advertised monotonic-time callback,
before E1000 construction. Therefore it is harness/environment-inconclusive
as a current-source Phase 52 control; historical Phase 52 evidence remains
the valid 651-case, 3/3 guest baseline. The current source does not remove
the GC probe and does not add a GC call.

The live E1000 worktree disposition is intentionally small. The Phase 53
mode/phase-result plumbing in `ManagedE1000Driver`, `ManagedEthernetLayer`,
and `ManagedIpv4Layer` is retained as legitimate feature wiring. The prior
Phase 53 compatibility bypass that converted a failed GC-survival result into
success was removed. No E1000 ownership rewrite or packet-semantic change is
retained. The runner's `-cpu max` experiment was also removed because it
changes the guest resource/layout surface and did not close RX readiness.

Accordingly this phase is **Outcome B**: host CSS-image coverage and the
browser-side implementation remain substantially proven, but Phase 53 is
not guest-accepted. The missing evidence is the clean current-source
`RX_READY` boundary followed by Phase 53 positive 3/3, CSS bad-CRC 1/1, and
the required deterministic screenshots. Phase 54 must not begin.

## Phase 53B GC-survival closure

The exact diagnostic payload used for the Phase 53B fault conclusions was
`E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388`,
4,790,272 bytes. Its preferred image base is `0x180000000`; the failing
guest load bases varied, but the RVA remained `0x17A090`. DIA/PDB and
disassembly resolve that RVA to
`WKS::gc_heap::mark_object_simple1(unsigned char *, unsigned char *) +
0x180`, in `.text`, with the exact instruction:

```text
mov r9,QWORD PTR [rbx]
```

The clean fault context was vector `0x0E`, page-fault error code `0`, CPL 0,
supervisor read, not-present, not a write, and not an instruction fetch. The
faulting register was `RBX=CR2=0x400002431000`; this is therefore a GC mark
read of the candidate field-slot address, not a successfully loaded candidate
object and not a driver MMIO or DMA access. The object parameter was
`R15=0x400002405C20`, with the slot `0x2B3E0` bytes from that object.

The in-guest page walk used the active `CR3=0x4C0D000` and found:

```text
PML4E=0x4C0B023  PDPTE=0x4C0A023  PDE=0x4B1B023
PTE=0x0          LEAF=0x0          walk=non-present
```

All upper levels were present and no large-page leaf was used. The page lies
inside the live managed VM reservation `[0x400000000000,
0x40000FE38000)`, but it had no commitment record, no virtual-region
ledger entry, and no PTE. The reservation reported `0x79000` committed bytes
at the diagnostic boundary. Immediately before that boundary the GC PAL had
successfully committed `0x400002411000..0x400002420FFF` and then
`0x400002421000..0x400002430FFF`; `0x400002431000` is the next page and was
never committed. No decommit or release involving that page preceded the
fault.

The PAL contract is `VirtualAlloc`/`VirtualFree`/`VirtualQuery` routed through
the Gate4 bridge into `gxos_vm_public_virtual_alloc`, the fixed managed arena,
commitment ledger, and x64 page-table mapping. Reserve addresses and sizes are
arena-aligned, commit ranges use an exclusive end rounded up to 4 KiB, each
page is zero-filled and mapped before the ledger is published, and rollback
removes partial mappings. The evidence does not implicate reserve/commit
rounding, decommit rounding, stale PTEs, or missing TLB invalidation: the
faulting page was free/uncommitted according to both the ledger and the live
page tables.

The resolved routine is the Workstation GC mark path. `GC.Collect()` is the
full collection call used by the E1000 proof; the driver’s DMA rings and packet
buffers are native-authoritative allocations, so the managed wrappers do not
carry movable managed-array addresses into the device. `GC.KeepAlive` extends
wrapper reachability through the call site but does not pin memory. No native
DMA pointer crosses the collection as a pointer into a movable managed object.
The observed invalid slot/frontier and invalid object header are consequently
an H1-style invalid GC reference/GC state observation, not proof that a valid
GC page was omitted by the VM bridge. The exact producer/root of that object
header/reference state is not yet identified, so no runtime or allocator
correction is justified.

`START_BLOCKED=0x6` is independently mapped to the first
`TryQueryMonotonicTime` validation in `ManagedKernelContract.Start`; it is a
startup host-service callback failure and shares no proven state with the
GC fault. The accepted Phase 52 source used the same pre-driver arena
priming shape, while Phase 53 adds bounded CSS image tables and scratch
storage to the primed `ManagedCssEngine`. That is a controlled allocation and
layout difference worth a follow-up, but not evidence for moving or lazily
initializing objects solely to change layout.

One independent source-isolation issue was corrected during the host rerun:
the bounded CSS image URL storage had referenced `ManagedHttpsUrl` directly,
which is not compiled into the standalone Phase 47–49 host projects. The CSS
limit now owns the same 512-byte bound locally. This changes no parser,
fetch, image, or browser behavior; it restores compilation of the existing
host-project boundaries and is unrelated to the GC page fault.

No GC bypass, fake survival marker, page-fault recovery, relaxed DMA address
comparison, or E1000 packet-semantic change was retained. The Gate4 change is a
bounded, read-only fault-time provenance reporter and boundary-scoped IDT
installation; it does not map, commit, decommit, or recover the candidate page.
The focused next regression is a minimal GC-only managed-kernel boot with
bounded allocation-pressure and gen0/full-collection variants; it remains
open because the current Phase 15 driver path faults before `RX_READY`.

## Phase 53D mark-queue provenance

The exact historical payload was booted under QEMU with a temporary host-only
GDB stub. The stub was removed after the diagnostic boots; it did not alter the
guest image, VM bridge, allocator, page tables, or packet path. The image base
varied between boots (`0x4C15000`, `0x4C17000`, and `0x4C24000`), but the
RVA-based evidence was stable.

The first successful entry breakpoint at
`mark_object_simple1` resolved the faulting object as a mark-ring item. Its
return address was `image+0x171011`, immediately after the direct call at
`image+0x17100C`. Static disassembly of that caller shows the queue semantics:
the field value is loaded from `(%rbx)` into `R9`, the previous ring item is
rotated into `R8`, and the previous item is then passed as both `RCX` and `RDX`
to `mark_object_simple1`. Therefore the faulting `R15` object was not produced
by the frontier slot that later faulted while scanning it.

The mark-ring insertion was then caught at `image+0x17A865`, immediately after
the root-promotion callback had loaded the value from its source slot. The live
registers were:

```text
RDX = 0x400002405C20       ; value inserted into the mark ring
RCX = 0x0000000007E63B40   ; local promotion/root slot
R11 = 0x000000000509FFB0   ; ring base
RAX = 0x000000000000000E   ; ring index
```

The preceding callback path is in the image function at `RVA=0x160E40`. It
loads `RBX=[RCX]` from source slot `0x0000000007E64878`, writes that value to
its local slot `[RSP+0x40] = 0x0000000007E63B40`, and calls the root-promotion
helper at `RVA=0x17A840`; that helper stores `RDX` into the mark ring at
`RVA=0x17A865`. The callback was reached through the runtime import thunk at
`RVA=0x1515E0` (return address `image+0x1515E6`). This closes the native
mark-queue origin chain as:

```text
source root slot 0x7E64878
  -> GC promotion callback RVA 0x160E40
  -> local promotion slot 0x7E63B40
  -> mark ring slot (base 0x509FFB0, index 0xE)
  -> mark_object_simple1(RCX=RDX=0x400002405C20)
  -> frontier read RBX=0x400002431000
```

The source slot already contained `0x400002405C20` when the promotion
callback loaded it. A final source-slot watchpoint boot was not creditable:
that diagnostic boot stopped at the pre-GC serial RX timeout before reaching
the callback. Accordingly, the exact upstream managed/native writer of
`0x7E64878` is still unresolved. No arbitrary mapping, commitment, object
relocation, GC bypass, or allocator workaround is justified by this evidence.
The phase remains **Outcome B**, with the remaining closure item being the
writer/root that populated the source slot.

## Phase 53E source-slot owner and writer investigation

Phase 53E used external QEMU/GDB only against the exact historical payload
(`E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388`,
4,790,272 bytes) and the exact Gate4 -4 image. No source, loader, GC, E1000,
or runner change was made. The bounded captures are preserved under
`artifacts/phase53e-writer-run-1` through `artifacts/phase53e-writer-run-20`.

The address is not image data, Gate4 static storage, a GC native heap entry,
a handle entry, a mark-ring entry, or a managed allocation. It is a writable,
non-executable stack word in the Gate4 boot/main stack. The loader computes
that stack as `[0x0000000007E63000, 0x0000000007F63000)`, and the slot is in
page `0x0000000007E64000`. The loader's paging proof reported an identity
mapping for sampled address/physical address `0x0000000007E64730` with 2 MiB
page size; the slot is consequently identity-backed in that preexisting
mapping. No new mapping or commitment was involved.

The bounded neighboring-word snapshots show ordinary stack-frame reuse, not
a named global or standalone root table. In the target callback capture the
window included:

```text
0x7E64840: 0x00000000052AB798  0x0000000004C22030
0x7E64850: 0x0000400002400070  0x0000000004C71E0E
0x7E64860: 0x0000000000000001  0x0000000007E648C0
0x7E64870: 0x00000000000080F7  0x0000400002405C20
0x7E64880: 0x0000000007E64940  0x0000000000000000
```

At the exact Phase 15 `GC.Collect` call in a separate staged capture, the
same stack word held `0x00000000001AF7A0`; adjacent words held other stack
values and valid-looking arena candidates. This run-to-run change is why the
slot is classified as a **stack root/root-location supplied by NativeAOT
stack enumeration**, not as a stable managed field. No PDB-backed managed
method/local identity was available for this historical stripped payload.

The promotion helper is confirmed at image `RVA=0x160E40`. Its entry loads
`RBX=[RCX]`, where `RCX=0x0000000007E64878` in the target capture, copies that
value into its local `[RSP+0x40]` slot (observed local promotion slot
`0x0000000007E63B40` in the previously captured chain), and calls the
root-promotion helper at `RVA=0x17A840`; the mark-ring store remains at
`RVA=0x17A865`. The target callback supplied `R8=0`, so the observed flags
are the exact-root case: no interior-pointer or pinned-root bit was present.
The callback therefore consumed the source as an exact stack reference, not
as an interior pointer or handle.

The most useful new capture was run 15. A conditional hardware breakpoint on
the promotion helper stopped at runtime `RIP=0x0000000004D84E40` with image
base `0x0000000004C24000`, `RCX=0x0000000007E64878`, and the slot already
equal to `0x0000400002405C20`. This independently reproduces the known
source → local promotion → mark-ring value. It also shows that the helper's
first earlier invocation was unrelated (`RCX=0x0000000004C15010`), so an
unconditional helper breakpoint is not a writer proof.

The staged run 18 stopped immediately before the exact Phase 15 collection at
`RVA=0x61A4A` and read `0x7E64878=0x00000000001AF7A0`; a full-width write
watchpoint armed for the collection saw no transition to
`0x0000400002405C20` before the known fault. Runs 11–12 captured the slot's
ordinary writes, including the prologue `push %rbx` at runtime
`0x0000000004C77982` (the exact runtime address captured in run 11), which
reused the slot for `0x0000400002404740` as a callee-save stack spill. That
is a proven stack writer for a non-target value, not proof that it produced
the bad candidate.
Full-width conditional watchpoints from reset and staged at the collection
boundary did not capture a CPU store producing the exact candidate. Thus the
first write, final pre-GC write, writer function/instruction, source register,
and one-predecessor value origin remain unresolved.

The evidence now materially narrows the defect: the GC sees an exact stack
root at `0x7E64878`, and the bad value is already present when the promotion
helper consumes it, but the bounded writer captures do not prove whether it
was introduced by an earlier stack-frame spill, an earlier lifecycle callback,
or a NativeAOT GCInfo/root-location mismatch. The slot is not proven to be a
managed local, static field, handle, interior pointer, or valid object. The
candidate's header/MethodTable remains invalid and its downstream frontier
page remains correctly absent. No production correction is justified because
the exact producer is still unproven. Outcome remains **B**; Phase 54 is not
safe.

## Build and current evidence

The repository pins SDK `10.0.302`; this host uses the installed fallback SDK
`10.0.401` (MSBuild 18.9.11.42413 direct entry point). The last successful
diagnostic NativeAOT candidate was 4,790,272 bytes with SHA-256
`E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388`; it
contained temporary GC witnesses and is not a final acceptance payload. The
clean final NativeAOT payload is 4,789,760 bytes with SHA-256
`9B72B8B939789C69D6937FB4933DBA0EE267EA03B463ECC8019562D7B2C64F18`;
the build completed with one pre-existing `CS0169` warning and no errors.

The available Release host binaries were directly executed during this audit
and reported Phase 47=955, Phase 48=698, Phase 49=694, Phase 50=683,
Phase 51=1083, Phase 52=651, and Phase 53=540, for an aggregate of 5304
cases. QEMU 11.0.0 is installed
and repository-owned QEMU cleanup was confirmed after the diagnostic runs.
The final provenance Gate4 EFI used SHA-256
`FF145440ED33BEB723020E954B99A09BC3043C20AA3C16F15B6DC83806E9AB9C`.
The historical-payload serial transcript is
`artifacts/phase53c-provenance-run-12/runs/run-1/serial.log` with SHA-256
`81CF47107EE19FB442D5BA78C09DF8C8B755A30D1C4C79073981093B21115B48`.
The clean current-payload transcript reached the same driver boundary but
faulted at the earlier `RVA=0x17A159` invalid-reference dereference. A
separate compatibility bypass was removed rather than accepted, and the
runner layout perturbation was removed.

Consequently, this working tree does not claim the requested QEMU 3/3 visual
acceptance or 1/1 CSS bad-CRC acceptance yet. The deterministic CSS bad-CRC
fixture and runner assertions are wired, but neither network control can be
credited until the guest reaches the Phase 53 HTTP exchange and produces the
required GOP markers.

## Exclusions and Phase 54 direction

This phase intentionally excludes gradients, multiple layers, shorthand,
positioning, cover/contain sizing, SVG/JPEG/GIF/WebP, masks, cursor images,
`@import`, `@font-face`, JavaScript, navigation, prefetch, parallel loading,
and HTTP caching. A later phase can add broader CSS image grammar only after a
separate bounded representation, cancellation policy, and visual proof are
specified.

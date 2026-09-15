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

## Phase 53F GCInfo and frame provenance

Phase 53F corrected the earlier tentative interpretation of the absolute
address `0x0000000007E64878`. The exact historical NativeAOT payload and its
matching PDB were decoded without rebuilding:

```text
DLL SHA-256: E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388
PDB SHA-256: 7270B65498976A8B531E04758C91E1A627E4A916508DB5FC411E9FB4CA8FCA9C
Method:      RunGcSurvival, RVA 0x619DC, code length 0x148
GCInfo:      xdata RVA 0x4602B8, blob RVA 0x4602C5, 15 bytes
Blob SHA-256: A467DA8C4D7851C0A2C608C56FD2F98244D768C82DEE301AABA15713BC1DB3F4
Blob:        20 0D E0 DC 98 13 E9 58 C4 9E 13 0D 15 F2 00
```

The record is a valid NativeAOT AMD64 v1 slim GCInfo record. Its six safe
points are `0x37`, `0x73`, `0x91`, `0xC7`, `0x111`, and `0x13D`. It has one
tracked register slot, register number 3 (`RBX`), with exact-reference flags,
and one separate untracked stack slot at `GC_SP_REL + 0x28` with the interior
flag. Direct liveness keeps `RBX` live at the first four safe points and dead
at the last two. The call to `RhCollect` is at method offset `0x6E`; its
return address is the `0x73` safe point. The record therefore proves that
`RunGcSurvival` exposes a live tracked `RBX` root at the collection return
safe point. It does not prove that the absolute address supplied to the
promotion helper is a normal `[RSP+0x8]` local in that method.

The relevant native ABI is also settled. The PDB identifies
`WKS::GCHeap::Promote(Object**, ScanContext*, unsigned int)` at RVA
`0x160E40`. On AMD64, NativeAOT's GCInfo decoder returns the address of the
`RegDisplay` register field for a tracked register slot (`pRD->pRbx`), while
an untracked stack slot is computed from the active register-display stack
pointer. At the promotion entry, `RCX` is consequently a pointer to the
root-location word and `R8` carries the promotion flags. The captured
`RCX=0x0000000007E64878, R8=0` hit is the tracked-register path; it is not
evidence that `0x7E64878` is the untracked interior slot.

The exact `RunGcSurvival` prologue is:

```text
push rdi; push rsi; push rbx; sub rsp, 0x40
```

At the healthy pre-collection stop, the method's current `RSP` was
`0x0000000007E64870`. Its saved `RBX` was therefore at
`[RSP+0x40] = 0x0000000007E648B0`, with the return address at
`[RSP+0x58] = 0x0000000007E648C8`. The method contains no access to
`[RSP+0x8]`; its observed stack accesses are at `+0x28`, `+0x30`, and
`+0x38`. The word at `0x7E64878` held `0x1AF7A0` in that healthy capture and
is therefore an unrelated/reused word in that frame, not the proven saved
`RBX` location.

The callback capture cannot currently recover a trustworthy managed owner.
Its immediate GDB stack return value was runtime address `0x4D745E6`, which
maps at that boot's image base to RVA `0x1515E6`, one byte before the end of
the seven-byte `PalSleep` import thunk. That is not a valid normal call-return
boundary. The custom `RtlVirtualUnwind` bridge in
`src/Gate4Harness/gate4_loader.c` was audited against the exact
`RunGcSurvival` unwind record: it maps the AMD64 register numbers correctly,
applies the three pushes and the `0x40` allocation, then reads the return
address from the post-unwind stack pointer. This makes the known managed
prologue internally consistent, but does not turn the malformed callback
context into a proven owner/context chain.

Accordingly, Phase 53F **supersedes the earlier tentative `[RSP+0x8]`
wording**. The exact GCInfo producer and safe point are proven; the absolute
callback root-location address's owning `RegDisplay`/managed frame and the
upstream context bridge that supplied it remain unresolved. The diagnostic
launch attempts under `artifacts/phase53f-frame-capture-*` are negative
instrumentation records only: they did not reach a creditable same-boot
three-stop capture and are not acceptance evidence. No root-range filter,
GC bypass, VM mapping change, allocator workaround, or production runtime
correction was made. The result remains **Outcome B**, no focused regression
is justified, and Phase 54 remains unsafe.

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

## Phase 53G RegDisplay ownership and RBX-root provenance

Phase 53G started from the uncommitted Phase 53F worktree at
`2bb3e9c5caa42f7ac446c3de09550c3f85b302da`. The Phase 53F documentation
change was preserved. No production/runtime source change, commit, push,
reset, checkout, stash, or cleanup operation was made.

The first capture used the historical diagnostic DLL and matching PDB from
`artifacts/phase53a-diagnostic-build` (DLL SHA-256
`E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388`, PDB
SHA-256 `7270B65498976A8B531E04758C91E1A627E4A916508DB5FC411E9FB4CA8FCA9C`).
The relocation-correct GDB attempt under
`artifacts/phase53g-capture-2` observed IMAGE_BASE `0x501F000`, installed
`RunGcSurvival`, `GC.Collect`, return, and `Promote` breakpoints, and did not
reach `RunGcSurvival`. Its generated status string was corrected in
`capture-status-correction.txt`: the original classifier matched its own
`GDB_NO_BAD_CALLBACK_HIT` marker, so that run is **no callback captured**, not
a new callback hit.

A clean control used the clean Phase 53C gate payload
(`9B72B8B939789C69D6937FB4933DBA0EE267EA03B463ECC8019562D7B2C64F18`,
4,789,760 bytes) and the same actual-image-base procedure. Static
disassembly confirmed the clean payload retains `RunGcSurvival` at RVA
`0x619DC`; its `GC.Collect` call is at method offset `0x48` rather than the
diagnostic payload's `0x6E`. The control observed IMAGE_BASE `0x5012000`,
installed runtime breakpoints at `0x50739DC`, `0x5073A24`, `0x5073A29`, and
`0x5172E40`, but did not reach the managed method within the bounded run.
It is preserved under `artifacts/phase53g-clean-control-1` as a negative
reachability record, not callback evidence.

The creditable historical callback evidence remains the Phase 53E run-15
capture. At `WKS::GCHeap::Promote(Object**, ScanContext*, unsigned int)`
(image RVA `0x160E40`, runtime PC `0x4D84E40` at image base `0x4C24000`),
GDB recorded:

```text
RSP = 0x0000000007E63B38
RCX = 0x0000000007E64878   ; callback Object** / root-location address
RDX = 0x0000000007E64470   ; ScanContext*
R8  = 0x0000000000000000   ; exact, non-interior, non-pinned flags
RBX = 0x0000000007E63C40   ; incoming callback-frame RBX
*RCX = 0x0000400002405C20  ; bad candidate
```

This proves the callback consumed the stack word at `0x7E64878`, and the
historical native helper then copied it into its local promotion slot and the
mark ring. It does **not** prove that this word is the `RunGcSurvival` saved
RBX slot. The Phase 53F GCInfo decode proves only that the tracked root is
NativeAOT register slot 3 (`RBX`), so NativeAOT's tracked-register enumeration
semantics make `RCX` the `pRD->pRbx` root-location pointer for this callback.
The physical address of the containing `RegDisplay`, and therefore the exact
storage object owning that field, was not captured. `pRD->pRbx == RCX` is
true as the GCInfo/callback ABI relation; the address of `pRD` itself remains
unknown.

The callback's immediate stack return was `0x4D745E6`, image RVA `0x1515E6`,
which is inside the historical seven-byte `PalSleep` import thunk rather than
at a valid normal call-return boundary. The backtrace repeated the callback
and did not identify a trustworthy `EnumGcRefs` caller or a managed PC. Thus
the exact active enumeration, owner method, safe point used by that
enumeration, caller/callee frame, and upstream writer of `0x400002405C20`
remain unresolved. The known `RunGcSurvival` frame remains a plausible
tracked-RBX producer because its decoded GCInfo has RBX live at the
`0x73` post-`GC.Collect` safe point, but the callback capture does not prove
that it owns this `RegDisplay`.

Static lifetime analysis found the historical `RunGcSurvival` prologue
(`push rdi; push rsi; push rbx; sub rsp,0x40`), the saved-RBX location
`[RSP+0x40]`, the return-address location `[RSP+0x58]`, and no method access
at `[RSP+0x8]`. The bad source word is a reused Gate4 stack word, not a
proven C# local, static, handle, or managed allocation. No exact writer of
the candidate was found: the observed `push rbx` stack spill writes a
different value, and the bounded conditional watchpoints did not catch a
store of `0x400002405C20` before the failing collection.

The guideXOS unwind audit found correct AMD64 register-number mapping
(`register 3 -> context.rbx`), the expected nonvolatile restores, stack
advancement, and return-PC load in
`src/Gate4Harness/gate4_loader.c`. `GXOS_CONTEXT_COMPAT` has the expected
Windows AMD64 offsets and size in `src/Gate4Harness/exception_context.h`.
That is a consistency result, not proof that the malformed callback context
was constructed correctly; no dangling `RegDisplay` pointer or specific
transition-frame defect was proven.

No causal defect is established, so no repair is justified. The result is
**Outcome B — provenance substantially advanced, repair not justified**.
The tracked-RBX interpretation and callback argument are established, but
the exact `RegDisplay` owner, active managed frame/PC, and candidate writer
are not. Phase 54 remains unsafe. The next narrow step is a same-boot
capture that stops at `EnumGcRefs`/RegDisplay construction and records the
RegDisplay address, `pRbx`, managed PC, SP, unwind-before/after state, and
the exact callback call site before investigating any writer.

## Phase 53G forensic completion: RegDisplay ownership and RBX-root provenance

The repository state at the start of this continuation differed from the
state described by the Phase 53F handoff. The actual branch was
`nativeaot-managed-kernel-integration` at committed HEAD
`6d476f1293b06067f8492609913fa0f131ae0f5c` (`....`), tracking
`origin/nativeaot-managed-kernel-integration` at 0/0 ahead/behind. The
expected `2bb3e9c5caa42f7ac446c3de09550c3f85b302da` was its parent. The
worktree was clean: the expected uncommitted Phase 53F documentation change
had already been committed in the preliminary Phase 53G commit. That
commit, the Phase 53F correction, and all ignored evidence directories were
preserved. No reset, checkout, restore, stash, clean, rebase, commit, push,
or PR was performed during this continuation.

The historical payload remained the decisive artifact:

```text
DLL: artifacts/phase53a-diagnostic-build/publish/gxos-managed-kernel.dll
Size: 4,790,272 bytes
SHA-256: E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388
PDB SHA-256: 7270B65498976A8B531E04758C91E1A627E4A916508DB5FC411E9FB4CA8FCA9C
```

### Exact callback path

The strongest same-boot ownership capture is
`artifacts/phase53g-capture-13`, using the historical DLL and the known
Phase 53 E1000 topology. Its IMAGE_BASE was `0x5011000`. The captured chain
was:

```text
GC stack walk
  -> CoffNativeCodeManager::EnumGcRefs
       image RVA 0x14DD20, MethodInfo 0x7E642F8
       safePointAddress / managed PC 0x505EE29
       REGDISPLAY 0x7E641C0
       REGDISPLAY->pRbx = 0x7E64878
  -> TGcInfoDecoder::ReportSlotToGC
       image RVA 0x1504E0, exact indirect callback call at RVA 0x1505E0
       pRD remains in R9 on the exact-register path
  -> WKS::GCHeap::Promote
       image RVA 0x160E40, runtime PC 0x5171E40
       RCX = 0x7E64878
```

The callback arguments from that run were `RCX=0x7E64878`,
`RDX=0x7E64470`, and `R8=0`. The callback root word was
`*RCX=0x400002405BD8`. The historical bad run
`artifacts/phase53e-writer-run-15` used the same payload and the same logical
`ReportSlotToGC` call return RVA, with `R9=0x7E641C0` newly captured, but its
root word was `0x400002405C20` and its callback PC was `0x4D84E40` at
IMAGE_BASE `0x4C24000`. Because those are different boots, the absolute stack
addresses are not equated by address reuse; capture 13 proves the active
owner for the reproduced `pRbx` path, while the historical bad hit does not
contain a same-stop `EnumGcRefs` owner record.

The owner PC in the creditable ownership capture is not
`ManagedE1000Driver.RunGcSurvival`. PDB/runtime-function correlation resolves
it to `ManagedCssEngine___ctor_1`, method RVA `0x4DB18`, method offset
`0x311`, with runtime-function range `0x4DB18–0x4DF78`. Its unwind data is
RVA `0x45E438`; the associated GCInfo blob begins at RVA `0x45E44D` and has
SHA-256
`DFE13F6914CBE31CB72925FF9011DFB8AB85DBABC74E0C14AEF9D303B0BB2D05`.
The safe-point address actually passed to `EnumGcRefs` is therefore the
constructor's `base+0x4DE29` (`0x505EE29` in capture 13), not
`RunGcSurvival+0x73`.

`pRD->pRbx == RCX` is proven: the AMD64 `REGDISPLAY` layout places `pRbx` at
offset `0x18`, and capture 13 printed both `pRD=0x7E641C0` and
`pRbx=0x7E64878`; the callback printed `RCX=0x7E64878`. The root is therefore
the address of the tracked RBX register home, not the address of an
untracked interior stack slot. `pRD` is an explicit register-display buffer
owned by the stack-walk invocation; the callback-frame CPU `RBX` was a
different value (`0x7E63C40` in the historical bad stop). The physical root
location was not a live CPU register and was not a PAL context pointer.

The constructor prologue is:

```text
push r15; push r14; push r13; push rdi; push rsi; push rbp; push rbx;
sub rsp, 0x20; mov rbx, rcx
```

At `pRD->SP=0x7E648C0`, the direct saved-RBX home is `0x7E648E0`; the
constructor's RBX means its managed `this` receiver. The observed root home
`0x7E64878` is not that save slot. The earlier `RunGcSurvival` association
came from the separate decoded GCInfo record showing a live tracked RBX at
that method's `0x73` return safe point and from reused absolute stack
addresses. That was not sufficient to establish frame identity and is
superseded by this direct `EnumGcRefs` capture.

### Proven bridge defect and attempted narrow repair

The PDB type record for the historical payload's AMD64
`_KNONVOLATILE_CONTEXT_POINTERS` is 256 bytes: `FloatingContext[16]` occupies
offsets `0x00–0x7F`, `IntegerContext[16]` begins at `0x80`, and `Rbx` is the
integer-register entry at offset `0x98`. NativeAOT's
`CoffNativeCodeManager::UnwindStackFrame` initializes those homes from the
REGDISPLAY, passes the structure to `RtlVirtualUnwind`, and then rebuilds the
REGDISPLAY homes from the returned structure. This is the contract described
by the NativeAOT source and its REGDISPLAY definition.

The guideXOS `platform_rtl_virtual_unwind` bridge previously ignored its
`context_pointers` argument. It updated the copied register values and SP/IP,
but discarded the saved-register home addresses. Consequently, after a
specific unwind, the NativeAOT caller could retain a stale `pRD->pRbx` such
as `0x7E64878` instead of the current frame's direct saved-RBX home
`0x7E648E0`. That is the causal invariant defect: a stale/reused stack word
was exposed as the tracked RBX root. It is not a GC range-policy problem,
not a malformed RunGcSurvival GCInfo record, and not a proven
safe-point-minus-one error.

After that invariant was established, the narrow source change in
`src/Gate4Harness/gate4_loader.c` was kept for review. It models the 256-byte
AMD64 context-pointer layout and records the stack address for every handled
`UWOP_PUSH_NONVOL`, `UWOP_SAVE_NONVOL`, and `UWOP_SAVE_NONVOL_FAR` operation.
The corrected-gate trace shows the intended effect: enumerated frames now
receive direct saved-register homes such as `0x7E64758`, rather than the
pre-repair stale home. The earlier provisional eight-pointer model was
discarded; it wrote the wrong part of the Windows structure and did not
change `pRD->pRbx`.

The exact CPU instruction that originally wrote
`0x400002405C20` was not captured. Conditional write-watchpoint runs did not
observe a store of that value before the failing collection. A normal
`push rbx` stack spill was observed, but it wrote a different value. The
candidate is therefore proven to have been consumed from a stale root home,
but its one-predecessor writer remains unresolved.

### Validation boundary

The historical payload was not rebuilt. The corrected Gate4 harness was
rebuilt with the repository build script using the installed fallback
toolchain: requested SDK `10.0.302` was unavailable; actual SDK `10.0.401`,
MSBuild `18.9.11.42413`, MinGW GCC `15.2.0`, and LLVM `22.1.8` were used.
The corrected harness is 662,162 bytes with SHA-256
`770C78B808E3C1BCC0305A2A62F4FE430577214FBA94DE61DA9CF8559537E252`.

The direct host regressions passed:

```text
Phase 48: 698
Phase 49: 694
Phase 50: 683
Phase 51: 1083
Phase 52: 651
Phase 53: 540
```

Three fresh corrected-harness QEMU attempts were preserved under
`artifacts/phase53g-capture-24`, `-25`, and `-26`. Their relocation-correct
IMAGE_BASE values were respectively `0x5010000`, `0x501F000`, and
`0x5012000`. All three reached the same earlier pre-keyboard failure:
`#UD` at `RIP=0xB0000`, before the E1000 Phase 15 GC-survival path. None is a
successful post-fix acceptance boot. The historical decisive capture used
OVMF code SHA-256
`33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` and
the capture-13 variable-store SHA-256
`115872CEAF965BAD1EAEF46017F2464D2191AB2AE8CB397CA08F28EC1E621EE9`.

The corrected bridge's root-home effect is proven, but the full E1000,
GC-survival, three-boot, resource/PNG, framebuffer, and CSS mapped-pixel
acceptance sequence is not. The exact writer of the historical candidate and
the cause of the new pre-target `#UD` boundary remain limitations. No root
filter, GC bypass, object pinning, callback suppression, or other masking
workaround was added.

**Outcome: B — provenance substantially advanced; Phase 54 remains blocked.**
The exact `pRD`, `pRbx`, callback equality, active native enumeration path,
managed owner for the reproduced path, and causal register-home invariant
are now established. The historical bad hit still lacks a same-stop owner
record, the candidate writer is unresolved, and the evidence-backed bridge
repair is not yet acceptance-validated. Phase 54 is not safe.

## Phase 53H — Corrected Unwind Bridge #UD Root Cause and Same-Boot GC Closure

### Starting state and scope

The handoff expected HEAD `6d476f1293b06067f8492609913fa0f131ae0f5c`, but the
actual starting HEAD was `63b7b462d92ab6d872955f2ac92535214c94dac6`, whose
literal commit subject is `...`. The expected
commit was an ancestor of the actual HEAD. The branch was
`nativeaot-managed-kernel-integration`, tracking
`origin/nativeaot-managed-kernel-integration` at 0/0. The worktree was clean:
the Phase 53G documentation and Gate4 source changes were already committed
in the actual HEAD. No reset, checkout, restore, clean, stash, rebase, push,
PR, or destructive Git operation was performed. No QEMU or GDB process was
live at the start or end of the investigation; the only diagnostic processes
stopped during the phase were launched for this repository's evidence runs.

The historical managed payload was preserved unchanged:

```text
size = 4,790,272
SHA-256 = E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388
```

The Phase 53G corrected Gate4 EFI was also preserved unchanged:

```text
size = 662,162
SHA-256 = 770C78B808E3C1BCC0305A2A62F4FE430577214FBA94DE61DA9CF8559537E252
```

The Phase 53H ABI-hardening rebuild is separate evidence and has size
665,536 with SHA-256
`86AD54FC34B110E36482736365ED3C9EE3BE97CCDAB8EFA4415259D4936E69FC`.

### #UD characterization

The strongest full-state capture is
`artifacts/phase53h-ud-capture-6`, with the corrected bridge and historical
payload. CPU 0 (APIC ID 0) reached exception vector 6, with no error code:

```text
RIP    = 0x00000000000B0000
RSP    = 0x0000000004E68D68
RBP    = 0x0000000004E68DA0
RAX    = 0x0000000000000000
RBX    = 0x0000000000000000
RCX    = 0x0000000000000000
RDX    = 0x0000000000000000
RSI    = 0x0000000004E68EE0
RDI    = 0x0000000004E68ED8
R8     = 0x0000000000000000
R9     = 0x0000000004E68E00
R10    = 0x0000000004E62030
R11    = 0x0000000004E68D70
R12    = 0x0000000000000000
R13    = 0x0000000000000000
R14    = 0x5757000000000007
R15    = 0x0000000004E68ED8
CR0    = 0x0000000080010033
CR2    = 0x0000000000000000
CR3    = 0x000000000500B000
CR4    = 0x0000000000000668
EFER   = 0x0000000000000D00
CS     = 0x0038
SS     = 0x0030
RFLAGS = 0x0000000000000046  (GDB pre-instruction state)
```

The firmware exception frame records `RFLAGS=0x10046` after entry; its saved
frame at `0x4E68D38` is `RIP=0xB0000, CS=0x38, RFLAGS=0x10046,
RSP=0x4E68D68, SS=0x30`. The exception handler is vector 6 at
`0x006F5FE3C`; vector 14 has a separate handler at `0x006F5FECC`, but no page
fault occurred. The instruction bytes at `0xB0000` were 32 bytes of `FF`.
The `FF FF` stream is not a valid intended payload instruction; the observed
invalid opcode is therefore a consequence of executing device bytes.

The page walk was valid and executable. `CR3=0x500B000` leads through
`PML4E=0x7C02023`, `PDPTE=0x7C04023`, and PDE `0xE3` at `0x7C04000`. That PDE
is a present, writable, supervisor 2-MiB identity mapping with NX clear, so
VA `0xB0000` translates to physical `0xB0000`. QEMU's memory tree identifies
`0x000A0000–0x000AFFFF` as `vga.vram` and
`0x000B0000–0x000BFFFF` as `vga-lowmem` I/O. QEMU physical reads returned
`FF`, proving that this is VGA low-memory I/O, not executable guest RAM,
firmware, poison, or an unmapped translation.

### Control-flow source

The transfer did not jump directly to `0xB0000`. The corrected trace in
`artifacts/phase53h-gc-capture-2` records:

```text
previous RIP       = image + 0x7D648 = 0x509C648
instruction        = call *%rbx
RBX target         = 0x0
saved return RIP   = image + 0x7D64A = 0x509C64A
```

The next stop, `NULL_TARGET_PREEXEC`, is at `RIP=0` with the valid return
address still at the top of the stack. Since page zero is also identity
mapped, control executed sequential low-memory bytes rather than taking a
page fault, eventually reaching the VGA window at `0xB0000`. The vector-6
handler then dispatches normally. This rules out `ret`, exception return,
interrupt return, unwinder continuation, and a corrupted return address.

The first call on the same path had `RBX=0x103720` and returned normally. The
second call had `RBX=0`, `RCX=0`, and `RDX=0`. Immediately beforehand,
`ManagedDriverWorker.Dispatch` had received relocated worker object
`0x400004C00118`; its field at `+0x8` was `0x180`, rather than the valid
dispatcher `0x400005000D78`. That malformed object state caused the dispatch
path to obtain a null drain target. The exact store of the zero drain pointer
was not separately captured; its origin is nevertheless downstream of the
malformed post-GC object, not the call/return stack.

The static worker slot at `0x4000000008C0` was watched in
`artifacts/phase53h-gc-watch-1`. It changed from
`0x400005000E50` to `0x400004C00118` in a runtime relocation-style write with
`RAX=new`, `RDX=old`, `RBX=old`, and `RCX=destination slot`. The reported
watchpoint PC was not an instruction boundary under QEMU/GDB, so it is not
assigned a guessed mnemonic. It does establish that the static root update
was a GC/runtime relocation write. Historical A/B evidence kept the old
worker and its valid fields in place after the same collection.

The first semantically important corrected-vs-historical divergence is thus
after the first normal worker dispatch, during the collection/relocation
boundary: corrected execution updates the managed static graph and later
observes a malformed relocated worker; historical execution does not relocate
that graph and reaches the original worker path. The divergence is not a
context-pointer buffer overwrite: the valid call return address remains
intact, and no surrounding output-buffer overwrite was observed.

### ABI and unwind audit

The historical payload PDB record, the MinGW `winnt.h` declaration, and the
NativeAOT/Native Runtime unwind implementation agree on the AMD64 structure:

```text
GXOS_KNONVOLATILE_CONTEXT_POINTERS
  sizeof                         = 0x100 (256)
  alignment                      = 8
  FloatingContext[16]            = offset 0x00, 16 pointer slots
  IntegerContext[16]             = offset 0x80, 16 pointer slots
  IntegerContext[3] / RBX home   = offset 0x98
  IntegerContext[15] / R15 home  = offset 0xF8
```

The floating entries are pointers to SIMD homes, not inline `M128A` values;
the integer entries are pointers to the RAX-through-R15 homes. There is no
union or padding discrepancy in the linked toolchain's declaration. Phase
53H added `_Static_assert` checks for 64-bit pointer width, 8-byte alignment,
the two array offsets, total size, RBX offset, and R15 offset in
`src/Gate4Harness/gate4_loader.c`. The ABI rebuild passed those assertions.

The external Windows contract is:

```c
PEXCEPTION_ROUTINE RtlVirtualUnwind(
    DWORD HandlerType, DWORD64 ImageBase, DWORD64 ControlPc,
    PRUNTIME_FUNCTION FunctionEntry, PCONTEXT ContextRecord,
    PVOID *HandlerData, PDWORD64 EstablisherFrame,
    PKNONVOLATILE_CONTEXT_POINTERS ContextPointers);
```

The local replacement now declares a pointer-sized `void *` return and uses
the same eight arguments with `EFIAPI` (`ms_abi` on AMD64). The corrected
NativeAOT call-site disassembly passes the first four arguments in RCX/RDX/R8/R9
and the remaining four through the Windows shadow-space stack area. GDB
bridge-entry captures agree: `RDX=image base`, `R8=control PC`, `R9=function
entry`, followed by context, handler-data, establisher-frame, and context
pointers on the stack. The call-site ignores the return value, so the old
`uint32_t` spelling did not explain the observed crash, but it was not an
exact declaration of the pointer-return ABI and is now corrected.

The Phase 53G register-home behavior remains architecturally correct. The
NativeAOT unwind contract initializes every nonvolatile home from the incoming
REGDISPLAY, lets the unwind operation replace only homes for reported saves,
and copies all returned homes back into REGDISPLAY. The bridge does that for
`UWOP_PUSH_NONVOL`, `UWOP_SAVE_NONVOL`, and `UWOP_SAVE_NONVOL_FAR`; it retains
incoming homes when no new save is reported. The context-pointer output is
written only within its 0x100-byte structure. No evidence shows a structure
overwrite, bad stack alignment, wrong argument ordering, dangling context
pointer, or pointer lifetime escaping the unwind call.

### Same-boot corrected GC evidence

The corrected capture reached the original managed GC walk before the later
worker failure. In `artifacts/phase53h-gc-capture-2`, one same-stop callback
record is:

```text
IMAGE_BASE       = 0x501F000
Promote RIP      = 0x517FE40
RCX              = 0x0000000007E64748
RDX              = 0x0000000007E643F0
R8               = 0x0000000000000000
R9               = 0x0000000007E64140   (REGDISPLAY)
REGDISPLAY       = 0x0000000007E64140
pRD->pRbx        = 0x0000000007E64748
*pRD->pRbx       = 0x40000280F470
candidate        = 0x40000280F470
pRD->SP          = 0x0000000007E64790
pRD->IP          = 0x00000000050AE679
```

Thus `Promote RCX == pRD->pRbx` in the same stop, and the home was derived
from `REGDISPLAY + 0x18`, not from the recurring stale address
`0x7E64878`. This callback is not the previously identified
`ManagedCssEngine___ctor_1` owner frame, so it is not misreported as closure
of that exact constructor capture. The target historical owner remains
`frame PC=0x505EE29`, `frame SP=0x7E648C0`, method RVA `0x4DB18`, method
offset `0x311`, with GCInfo legitimately tracking RBX. The corrected run
stopped at the independent worker/object defect before producing a same-stop
capture for that exact constructor receiver.

The corrected capture did produce the runtime survival markers
`MANAGED_KERNEL_DRIVER_WORKER_RUNTIME_SURVIVAL_OK` and
`MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_OK` before the second dispatch.
It did not reach `SERIAL_RX_AFTER_RUNTIME_OK`, the full E1000 continuation, or
the later CSS/image feature markers. Therefore the register-home correction is
validated on a real same-stop callback, but exact constructor-owner receiver
validity and end-to-end stale-root closure remain blocked by the independent
post-GC object defect.

The historical value `0x400002405C20` is still not assigned a writer. The
evidence supports the narrower conclusion that it was ordinary reused stack
content exposed only because the old bridge retained a stale RBX home; no GC
contract required that location to contain a valid object. Finding that writer
is not necessary to establish the Phase 53G defect. It would matter only if a
later investigation links it to the separate relocated-object corruption.

### Validation and disposition

The historical A/B bridge reached the original worker path after collection;
the corrected bridge reached GC, updated the static graph, and then failed at
the null indirect call described above. The corrected bridge is therefore not
reverted, masked, or filtered. The ABI assertions and pointer-return spelling
are the only Phase 53H production-source changes; no repair of the independent
NativeAOT managed-heap/object-state defect was justified from this evidence.

The installed toolchain was .NET SDK `10.0.401`, MSBuild `18.9.11.42413`,
GCC `15.2.0`, binutils `2.46.0.20260210`, LLVM `22.1.8`, and QEMU `11.0.0`.
The pinned SDK `10.0.302` remained unavailable, so `dotnet run` remained
blocked by repository SDK selection and was not called a host-test failure.
The existing direct host results remain Phase 48 `698`, Phase 49 `694`, Phase
50 `683`, Phase 51 `1083`, Phase 52 `651`, and Phase 53 `540`; the Gate4
bridge-only change does not alter those host vectors.

Three independent Phase 53G corrected-harness boots
(`artifacts/phase53g-capture-24`, `-25`, `-26`) all reproduced `#UD` at
`0xB0000`, so corrected successful boots remain `0/3`. The Phase 53H ABI
rebuild was separately compiled as `artifacts/phase53h-abi-gate`; its
diagnostic replay was stopped before payload execution after a control stall
and is not counted as an acceptance boot. OVMF code SHA-256 was
`33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`; the
representative corrected capture variable store SHA-256 was
`319EEA06415AD81F655F6EDE7410C0EFACB294B525919011D81CA3468D7BD067`.

CSS URL resolution, HTTP/HTTPS fetch, PNG SHA/decode/pixel proof, image
placement, layout interaction, framebuffer proof, screenshot proof, and
mapped-pixel proof were not accepted in this phase. No repository-owned
QEMU/GDB process remains. The evidence directories created for Phase 53H are
the ignored `artifacts/phase53h-ud-capture-1..13`,
`phase53h-gc-capture-1..5`, `phase53h-gc-watch-1`,
`phase53h-ab-historical`, `phase53h-ab-historical-2`,
`phase53h-ab-historical-3`, `phase53h-abi-gate`, and
`phase53h-abi-replay-1`.

**Outcome: E — corrected bridge exposes an independent prerequisite defect.**

The `#UD` cause is proven as control flow through a null indirect call into
identity-mapped VGA low-memory bytes. The Phase 53G context-pointer repair is
architecturally valid and its same-stop REGDISPLAY equality is reproduced.
However, corrected GC relocation exposes malformed managed worker state before
the target constructor-owner capture and before full Phase 52/53 acceptance.
Phase 54 is unsafe. The recommended next phase is a separately scoped
NativeAOT managed-heap/object-relocation investigation; it is not Phase 54.

### Phase 53I — Managed Heap Relocation and Worker Object Integrity

Phase 53I was a forensic continuation of Phase 53H only. Phase 54 was not
started. The branch was `nativeaot-managed-kernel-integration` at
`860c8a5b943e9a6c01bd226530760523e6047cdb` (`origin` ahead/behind counts
`1/0`; raw `@{upstream}...HEAD` count was `0 1`), and the worktree was clean
before the investigation. No production
source fix was made in this phase.

The first observed managed-object integrity violation is now localized. The
worker was valid at the old address `0x400005000e50` before the GC relocation
sequence: its `+0x8` field held the dispatcher `0x400005000d78`, and the
static worker root was `0x4000000008c0`. The first bad write to that live
object was in the GC free-block path, not in the root update and not in the
relocation copy:

```text
source worker       0x400005000e50
source dispatcher   0x400005000e58
SetFree size        0x198
post-write +0       free-object EEType (run-dependent: 0x54a0b80/0x5490b80)
post-write +0x8     0x180
destination worker  0x400004c00118
root slot           0x4000000008c0
```

The object is `GuideXOS.Net10.ManagedKernel.ManagedDriverWorker`, allocated
by `ManagedSerialDriver.RunDriverWorker` through
`new ManagedDriverWorker(dispatcher, driver)`. Its NativeAOT layout is
consistent with a `0x38`-byte, pointer-aligned object: EEType at `+0x0`,
managed references `_dispatcher`, `_serialDriver`, and `_keyboardDriver` at
`+0x8`, `+0x10`, and `+0x18`, followed by scalar state/counters at `+0x20`,
`+0x24`, `+0x28`, `+0x2c`, and `+0x30`. Therefore `+0x8` is the managed
`_dispatcher` reference, not an EEType field, scalar, padding, or function
pointer. The expected object base/total size is `0x38` (56 bytes). The exact
emitted EEType GC-descriptor address, series encoding, and pointer count were
not decoded from this capture; the field classification above is proven by
the managed source and generated field access paths, but no descriptor defect
is claimed.

The complete root chain proven here is the managed static field
`ManagedSerialDriver.s_driverWorker` in the runtime static region. Its
storage cell is `0x4000000008c0` (static-base diagnostic `0x400000000898`).
Before relocation it contained `0x400005000e50`; after
`WKS::gc_heap::relocate_address` it contained `0x400004c00118`. The source
worker's reachable references before the bad write were dispatcher
`0x400005000d78`, serial driver `0x4000024019c0`, and keyboard driver null;
the post-copy destination retained the serial reference but had the free
object header and free-block value at `+0x8`. Allocation timestamp, precise
region-generation label, and forwarding-record encoding were not exposed by
the bounded capture.

The decisive watch stop was the source `+0x8` write at runtime PC
`0x51807d3` in the source-field run. The preceding instruction sequence
computes the free-block payload size and the store at `0x51807cf` writes it
to `oldWorker+0x8`; the PDB maps this code to
`WKS::CObjectHeader::SetFree`, RVA `0x161770`. The same stop showed
`RBX=RCX=0x400005000e50`, `RDX=0x198`, and `RAX=0x180`. The preceding source
header watch in `phase53i-relocation-watch-1` captured the corresponding
free-object method-table write. Thus the first captured violation is the
GC classifying/marking the live worker range as a free block and applying
the normal free-object image to it.

The later events are consistent with that source corruption rather than its
cause. `WKS::memcopy` at RVA `0x17D360` copied the already-corrupt source
header and `+0x8` value to `0x400004c00118`; the destination watch showed the
source and destination addresses and then `WATCH_RELOCATION_COPY_COMPLETE`.
`WKS::gc_heap::relocate_address` at RVA `0x182660` subsequently updated the
static root from the old worker to `0x400004c00118`. The root update was
observed, but it published a destination that already contained the free
object image. Managed dispatch then read `+0x8 == 0x180` as the dispatcher
reference and reached the previously documented invalid indirect call.
The managed path is `ManagedDriverWorker.Dispatch` to
`ManagedInterruptDispatcher.TryDispatchBatch`. In the failing historical
capture, the second worker entry loaded `RCX=0x180` where a dispatcher
receiver should have been; the later call site was image `+ RVA 0x7D648`
(`0x509C648` for image base `0x501F000`) with `call *%rbx` and `RBX=0`.
The capture proves the corrupted receiver/field transition and the null
target, but did not isolate a single pre-call instruction that loaded zero
into `RBX`; the call-target scratch area was already zero by the breakpoint.
That remaining dataflow detail is not needed to identify the earlier
`SetFree` violation and was not used to justify a null-dispatch patch.

The PDB/RVA cross-checks for the relevant native paths are:

| observed operation | PDB symbol | RVA |
| --- | --- | ---: |
| source object overwritten as free block | `WKS::CObjectHeader::SetFree` | `0x161770` |
| source image copied to destination | `WKS::memcopy` | `0x17D360` |
| static root relocated | `WKS::gc_heap::relocate_address` | `0x182660` |
| source free-block search caller | `WKS::gc_heap::find_first_valid_region` | `0x171F80` |
| destination compaction caller | `WKS::gc_heap::compact_plug` | `0x16BCE0` |

The targeted `Promote` hook did not emit a static-root callback in
`phase53i-static-root-promote-1`. That is a limitation of that hook's
coverage, not evidence that the static field was unrooted: the static slot's
old-to-new relocation write was independently observed. The remaining
unresolved question is upstream of `SetFree`: why the collector's region,
plug, mark-root, or object-boundary state allowed this live NativeAOT worker
range to enter the free-block path. The captures do not prove which of those
invariants failed, so no collector rewrite or workaround is justified.

The ignored evidence directories are:

* `artifacts/phase53i-relocation-watch-1` — initial source/destination/root
  watchpoints; the run was interrupted after the useful stops were captured.
* `artifacts/phase53i-source-field-watch-1` — completed source-field watch,
  exact `SetFree` stop, relocation copy completion, and payload/firmware
  identity.
* `artifacts/phase53i-static-root-promote-1` — completed targeted static-root
  promotion hook run and its coverage limitation.

The upstream CoreCLR contract used for interpretation is that `SetFree` marks
a free block with the free-object method table and records the free payload
size; it is normal when the collector has already identified an unused range.
The defect proven here is that this normal operation was applied to the live
managed worker source range. The phase therefore ends with the same
disposition as Phase 53H: the managed-heap/object-relocation investigation
must continue under a separately scoped phase, and Phase 54 remains unsafe.

### Phase 53J — Allocator Boundary Forensics at the Live Worker SetFree

Phase 53J did not start Phase 54. It is a continuation of the Phase 53I
forensic investigation and leaves the managed-kernel production sources
unchanged. The objective was to identify the earliest observable heap-state
decision that supplied a live `ManagedDriverWorker` address to
`WKS::CObjectHeader::SetFree`, while preserving the normal collector and
relocation paths.

#### Capture identity and method

The authoritative capture is the ignored
`artifacts/phase53j-setfree-capture-8` directory. It completed with
`TARGETED_SEQUENCE_CAPTURED` on QEMU `11.0.0`, `tcg,thread=single`, CPU 0,
using the exact Phase 53G corrected-gate NativeAOT payload and matching PDB.
The payload SHA-256 is
`E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388`, its
size is `4,790,272` bytes, the relocated image base is `0x5012000`, and the
OVMF code SHA-256 is
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.
The per-run copied OVMF variable store SHA-256 is
`13C1F20F38CF2DAFC98FDF04C8BBB0E0D65290A7B859CA65E78A65F758BEC370`.

The tracked reproducer is
`tools/Run-Phase53JSetFreeCapture.ps1`. It starts the existing QEMU/GDB
topology, watches the managed static root, records worker entry and fields,
breaks `garbage_collect`, `mark_phase`, `plan_phase`,
`relocate_survivors`, `compact_phase`, and `compact_plug`, and stops at the
exact `SetFree` entry and the exact `memcopy` instructions that read the
source `+0x8` slot and write the destination `+0x8` slot. It does not alter
the payload or runtime and does not patch `SetFree`.

#### Worker identity, layout, and GC descriptor

The managed source and matching CodeView type record identify the object as
`GuideXOS.Net10.ManagedKernel.ManagedDriverWorker`, allocated by
`ManagedSerialDriver.RunDriverWorker` through
`new ManagedDriverWorker(dispatcher, driver)`. On capture 8 its old address
was `0x400005000e50`, its EEType was `0x5687e68`, and its fields were:

```text
+0x00 EEType             0x0000000005687e68
+0x08 _dispatcher        0x0000400005000d78
+0x10 _serialDriver      0x00004000024019c0
+0x18 _keyboardDriver    0x0000000000000000
+0x20 _state             0x0000000000000002
+0x24 _dispatchBatches   0x0000000000000000
+0x28 _managedDispatches 0x0000000000000000
+0x2c _delivered         0x0000000000000000
+0x30 _rejected          0x0000000000000000
```

The CodeView record gives the managed object size as `0x38` bytes. The live
EEType bytes decode the NativeAOT `MethodTable` header as flags
`0x51000000` and base size `0x40`; the `0x40` is the aligned allocation
size, while the last declared field ends at `+0x34` in the `0x38`-byte type
record. The matching descriptor window is:

```text
0x5687e50 : 0xffffffffffffffd8  (series size = -0x28)
0x5687e58 : 0x0000000000000008  (series start = +0x08)
0x5687e60 : 0x0000000000000001  (number of series = 1)
0x5687e68 : 0x0000004051000000  (flags = 0x51000000, base size = 0x40)
```

Using the NativeAOT/CoreCLR GC descriptor layout, one series with
`startoffset=0x8`, `series_size=-0x28`, and `object_size=0x40` describes
`(-0x28 + 0x40) / 8 = 3` reference slots: `+0x8`, `+0x10`, and `+0x18`.
Those are exactly `_dispatcher`, `_serialDriver`, and `_keyboardDriver`;
the descriptor therefore corroborates the source/type-field classification
and does not itself show a missing or extra reference slot. The descriptor
interpretation follows the runtime `CGCDesc` series encoding documented in
`src/coreclr/gc/gcdesc.h`.

#### Strong-root and liveness proof

The managed static field `ManagedSerialDriver.s_driverWorker` has diagnostic
static base `0x400000000898` and root storage cell `0x4000000008c0`. Event 1
recorded the root publication as `0x400005000e50`. Event 2 then entered the
worker with the same receiver, and the worker's `_dispatcher` and
`_serialDriver` slots still held valid managed references. The worker was
also executed immediately before the collection: the dispatch call used
`RBX=0x400005000e50`, `RCX=0x400005000d78`, and
`RDX=0x4000024019c0`.

This establishes a strong static root and an actually live object at the
old address before the damaging free-block operation. It is stronger than
an untriggered root watch or a presumed type layout: the root slot, worker
receiver, EEType, three reference slots, and managed dispatch path were all
observed in the same boot.

#### Exact damaging call and immediate caller

With image base `0x5012000`, the matching PDB gives
`WKS::CObjectHeader::SetFree` RVA `0x1617b0`, so the exact entry was
`0x51737b0`. Event 4 stopped there with:

```text
this/RCX       0x400005000e50
RDX total size  0x198
RDX - 0x18     0x180
free end       0x400005000fe8
RIP            0x51737b0
return         0x5184e9a
call site      0x5184e95
RBX            0x400005000e50
R8             0x1
CPU            0
```

The return address `0x5184e9a` is the instruction after the direct call at
`0x5184e95`. The caller is therefore unambiguously
`WKS::gc_heap::fix_allocation_context`, RVA `0x172e20`, whose PDB-verified
call-site sequence is:

```text
0x5184e88: sub %rbx,%rcx
0x5184e8b: lea 0x18(%rcx),%r15
0x5184e8f: mov %rbx,%rcx
0x5184e92: mov %r15,%rdx
0x5184e95: call 0x51737b0 <CObjectHeader::SetFree>
```

The logical allocator adapter above this function is
`WKS::GCHeap::FixAllocContext`, RVA `0x15f420`; its no-extra-context branch
tail-jumps to `fix_allocation_context`. The native unwind at the SetFree
stop cannot name that parent reliably because the adapter is a tail jump and
the generated frame has nonstandard saved-register homes. The independent
direct-call map contains only that adapter tail jump and the direct
`soh_try_fit` call at RVA `0x185dab`; the live stop occurred during the
`garbage_collect` path, so the exact physical return chain above the tail
jump is recorded as an unwind limitation, not guessed.

#### What SetFree did, and what it did not do

The exact `SetFree` body is ordinary free-block canonicalization:

```text
mov  rax,[free-object-EEType-cell]
mov  rbx,rcx
mov  [rcx],rax             ; write free-object EEType
lea  rax,[rdx-0x18]
mov  [rcx+0x08],rax        ; write free payload size
...
clear [rcx+0x10] when payload is nonzero
set  [rcx+0x18] to 1 when total size is at least 0x30
```

Event 4's pre-image was still the worker image, including
`[oldWorker+0x8] = 0x400005000d78`. Event 5 observed the free-object EEType
write, and event 6 observed `[oldWorker+0x8] = 0x180` with the free-object
EEType at `[oldWorker+0x0]`. `SetFree` performed exactly the stores its
arguments request; it did not inspect or decide managed liveness, roots,
marks, forwarding records, or object boundaries. The violated precondition
was therefore upstream of `SetFree`.

#### Earliest wrong allocator boundary

The same stop captured the allocator context at `0x5010038`:

```text
alloc_context + 0x00  alloc_ptr   = 0x400005000e50
alloc_context + 0x08  alloc_limit = 0x400005000fd0
alloc_context + 0x10              = 0x1d8a8
alloc_context + 0x18              = 0x1130
```

`fix_allocation_context` computed `alloc_limit - alloc_ptr = 0x180`, added
the `0x18` allocator/header adjustment, and passed `0x198` to `SetFree`.
Consequently the collector treated `[0x400005000e50,
0x400005000fe8)` as a free range even though the rooted worker begins at
`0x400005000e50` and its aligned live allocation extends through at least
`0x400005000e90` (`0x40` bytes). The first wrong heap-state decision that
can be proven from this run is thus the allocator-context boundary itself:
`alloc_ptr` was a live object start instead of the first byte after the last
live object in the allocation area.

The preceding `soh_try_fit` disassembly explains the normal control flow:
`soh_try_fit` calls `a_fit_segment_end_p`; when that fit check fails it calls
`fix_allocation_context` with the current allocation context. The capture
does not contain a write watch on `alloc_context+0x0`, so it does not prove
which earlier store or collector subphase first put the worker address into
that context. It does prove the first invalid state at the boundary where
the collector consumed it. No narrower producer claim is made.

The violated invariant is:

```text
For a live allocation context, alloc_ptr must be the first free address
after the last live allocation, alloc_ptr <= alloc_limit, and the range
[alloc_ptr, alloc_limit) must not overlap any live object or its header.
```

In this capture the context instead had `alloc_ptr == oldWorker`, while the
static root and just-executed managed receiver proved `oldWorker` live. That
is the precise invariant violation to repair in a future, separately scoped
collector investigation.

#### Mark, plan, relocation, and source/destination ordering

The event order in the authoritative log is:

```text
1  root publication:       root -> 0x400005000e50
2  worker entry/dispatch:  old worker and valid references
   garbage_collect         entered with arg 2
3  fix_allocation_context: ptr=oldWorker, limit=0x400005000fd0
4  SetFree entry:          oldWorker, total=0x198, payload=0x180
5  SetFree header store
6  SetFree payload store:  oldWorker+0x8 = 0x180
   gc1, mark_phase, plan_phase
7  root relocation:        root -> 0x400004c00118
   relocate_survivors, compact_phase, compact_plug
17 memcopy outer range:    dest=0x400004c00038, src=0x400005000d70, size=0x270
18 source read boundary:   src=oldWorker, dest=0x400004c00118
19 destination write:      dest+0x8 = source+0x8 = 0x180
```

The root relocation happened before the targeted copy, but it did not repair
the source. The outer `memcopy` range contains the worker at offset `0xe0`:
`0x400005000d70 + 0xe0 = 0x400005000e50` and
`0x400004c00038 + 0xe0 = 0x400004c00118`. At event 18 the destination header
was inspected before the source `+0x8` read; at event 19 the exact following
instruction had loaded the already-corrupt source value and stored that same
value into the destination. The logged `remaining=0x170` is the outer-copy
remaining length after earlier bytes, not the worker object size.

Therefore the causal ordering is `bad allocator context -> SetFree(source)
-> source corruption -> relocation copy -> root update/publication of the
corrupt destination`. The later `relocate_address` root write is not the
cause, and the relocation copy is not the first corruption.

#### Disposition

The fresh capture closes the prior Phase 53I uncertainty about the exact
EEType descriptor and immediate `SetFree` caller, but it does not identify
the earlier writer or collector decision that made the allocation context
stale. A collector patch, source workaround, worker pin, dispatch
suppression, GC disablement, compaction disablement, leaked region, or
hard-coded address would all bypass rather than repair the proven invariant.
No such change was made.

Because no causal repair was justified, the requested three corrected QEMU
boots and post-repair host regressions were not claimed or run under Phase
53J. Existing Phase 53H/53I baselines remain historical and are not silently
reclassified. CSS/image acceptance remains blocked behind this managed-heap
defect, and Phase 54 remains unsafe.

**Outcome: C — the first invalid allocator boundary is proven, but its
upstream producer is not yet isolated; no repair is justified.**

## Phase 53K — allocator-boundary provenance

Phase 53J established the first invalid consumer state but did not identify
the writer that supplied the boundary. Phase 53K continued from that exact
state using the matching NativeAOT payload and PDB. The authoritative
provenance capture is `artifacts/phase53k-allocator-context-capture-8`:

```text
payload SHA-256  E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388
image base       0x501f000
allocator ctx    0x501d038
old worker       0x400005000e50
worker EEType    0x5694e68
```

The capture used the same OVMF code as the Phase 53J evidence and reached
`TARGETED_SEQUENCE_COMPLETE`. The earlier K captures remain useful negative
evidence: fixed candidate addresses did not identify the live context across
boots, the public `GetCurrentThreadAllocContext` return was not reached on
the failing path, and the ordinary `GCHeap::Alloc` store probe did not see
the decisive transition.

### Context ownership and semantic layout

The relevant state is not a guideXOS allocator field. It is the current
thread's NativeAOT runtime allocation context. The PDB type information gives
`gc_alloc_context` as:

```text
+0x00  alloc_ptr
+0x08  alloc_limit
+0x10  alloc_bytes
+0x18  alloc_bytes_uoh
```

The context is embedded in the NativeAOT thread-local allocation block. The
preceding word at `context-0x08` is the combined limit used by the fast
allocator path. `GCToEEInterface::GetAllocContext`, RVA `0x153080`, reads
that TLS block directly. The public `GetCurrentThreadAllocContext` wrapper
was watchpointed in capture 6 but did not execute before the failure; the
failing path therefore uses an inlined equivalent TLS read.

The allocator invariant remains the one established in Phase 53J:

```text
alloc_ptr is the first free address after the last live allocation;
alloc_ptr <= alloc_limit; and [alloc_ptr, alloc_limit) does not overlap
any live object or object header.
```

### Last known-good and first provably invalid states

Immediately before the decisive transition, event 1346 advanced the same
context from `0x400005000c58` to `0x400005000d78`, with
`alloc_limit = 0x400005000fd0`. The preceding object type and the allocator
range were not yet shown to overlap the worker. This is the last captured
allocator state that is good under the allocator's own boundary semantics.

Event 1347 then advanced `alloc_ptr` from `0x400005000d78` to the worker's
address `0x400005000e50`, leaving the same limit. Event 1348 published that
address into the strong static root `0x4000000008c0`, and event 1349 entered
the object with:

```text
[worker+0x00] = 0x5694e68
[worker+0x08] = 0x400005000d78
[worker+0x10] = 0x4000024019c0
```

The first state that can be proved invalid, rather than merely unusual, is
event 1350: `FixAllocContext` consumed `alloc_ptr == 0x400005000e50` while
the same address was a rooted, live worker. It computed the free payload as
`0x180` and passed total size `0x198` to `SetFree`. The capture does not
timestamp the worker header's own initialization between events 1347 and
1348, so event 1347 is the earliest boundary transition and event 1350 is
the first complete proof of the violated live-range invariant.

### Exact producer instruction

The event-1347 hardware watchpoint stopped after the store at the `ret` at
`0x5167a58`. The exact writer is the preceding instruction at
`0x5167a54`, image-relative RVA `0x148a54`:

```text
RhpNewFast, RVA 0x148a20, parent AllocFast.asm.obj

0x180148a46: sub    r9,rax
0x180148a49: cmp    r8,r9
0x180148a4e: add    r8,rax
0x180148a51: mov    [rax],rcx
0x180148a54: mov    [rdx+0x08],r8       ; alloc_ptr = new boundary
0x180148a58: ret
```

At the decisive stop, the registers were:

```text
RDX = 0x501d030       ; allocator context - 0x08
RAX = 0x400005000d78  ; previous alloc_ptr
R8  = 0x400005000e50  ; new alloc_ptr / worker address
RCX = 0x5696950       ; type being allocated by this fast-path call
```

This is a matching-binary/native-symbol result, not a source-address
guess. DIA maps RVA `0x148a54` to `RhpNewFast`; its parent is the runtime
`AllocFast.asm.obj`. The subsequent root publication stopped at
`0x5167c83`, RVA `0x148c83`, in the write-barrier routine
`RhpAssignRefAVLocation` (RVA `0x148c80`), writing the worker address into
`ManagedSerialDriver.s_driverWorker` storage.

The decisive store is therefore a normal NativeAOT allocation-fast-path
update. It is not a store performed by `SetFree`, by relocation, or by the
managed worker code itself. The event's `RCX` type value is also different
from the worker EEType, so this capture does not by itself prove that the
fast-path call allocated or initialized the worker object. No later
`alloc_ptr` transition from the worker start to the end of its `0x40`-byte
allocation was captured before root publication.

### Provenance chain and remaining ambiguity

The strongest evidence-backed chain is:

```text
TLS-owned gc_alloc_context
  -> RhpNewFast [context+0] = 0x400005000e50 (event 1347, RVA 0x148a54)
  -> s_driverWorker root = 0x400005000e50 (event 1348)
  -> valid worker entry/dispatch (event 1349)
  -> FixAllocContext consumes ptr == worker (event 1350)
  -> SetFree treats the worker range as free (event 1351 and stores)
  -> relocation copies the corrupted source value (events 1355 onward)
  -> managed dispatch later observes the damaged worker
```

This proves who last wrote the boundary value and proves the downstream
causal path. It does not yet prove which operation initialized the worker
header at `0x400005000e50`, which allocator context owned that initialization,
or why that object's live lifetime was not followed by a corresponding
`alloc_ptr` advance. The observed fast-path write may be the correct boundary
after a preceding allocation, with the ownership/range disagreement arising
from an unobserved worker-creation path, a separate context, or another
runtime transition. Those are unresolved hypotheses, not conclusions.

Consequently, no guideXOS-local repair is justified. Changing the worker,
GC policy, `SetFree`, relocation, or the fast-path allocator would either
mask the consumer or alter a NativeAOT runtime contract without a demonstrated
producer invariant violation. The exact remaining forensic question is:

```text
Which instruction wrote the ManagedDriverWorker header and reference fields
at 0x400005000e50, under which allocator context, and why was that context
not advanced past the worker before s_driverWorker was published?
```

K diagnostic changes are confined to the removable options in
`tools/Run-Phase53JSetFreeCapture.ps1`. Captures 9 and 10 were not used as
evidence: the optional worker-header probe changed debugger command flow and
detached before the target sequence. No production allocator, GC, image,
CSS, or managed-kernel behavior was changed.

**Outcome: C — boundary provenance narrowed to the NativeAOT TLS fast-path
store, but the causal producer/ownership transition that made the live worker
overlap that boundary remains unresolved; no repair is justified.**

## Phase 53L — ManagedDriverWorker allocation provenance

Phase 53L worked backward from the known live address
`0x400005000e50`. It used the matching Phase 53 payload and PDB without a
rebuild. The payload SHA-256 was
`E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388` and the
PDB SHA-256 was
`7270B65498976A8B531E04758C91E1A627E4A916508DB5FC411E9FB4CA8FCA9C`.
Because QEMU relocates the image, the capture derived runtime addresses from
the serial `IMAGE_BASE` marker. At the Phase 53K base `0x501f000`, the worker
EEType RVA `0x675e68` resolves to `0x5694e68`.

### EEType and object-layout facts

The matching CodeView type record is
`gxos_managed_kernel_GuideXOS_Net10_ManagedKernel_ManagedDriverWorker`, which
is `GuideXOS.Net10.ManagedKernel.ManagedDriverWorker` in the managed source.
The type record has `sizeof 56` (`0x38`) and the following fields:

```text
+0x00  NativeAOT object header / EEType
+0x08  _dispatcher       reference
+0x10  _serialDriver     reference
+0x18  _keyboardDriver   reference
+0x20  _state            uint32
+0x24  _dispatchBatches  uint32
+0x28  _managedDispatches uint32
+0x2c  _delivered        uint32
+0x30  _rejected         uint32
```

The live EEType header at `0x5694e68` decodes as flags `0x51000000` and
NativeAOT base size `0x40`. The descriptor immediately preceding it is:

```text
0x5694e50 : 0xffffffffffffffd8  (descriptor series size = -0x28)
0x5694e58 : 0x0000000000000008  (descriptor series start = +0x08)
0x5694e60 : 0x0000000000000001  (descriptor series count = 1)
0x5694e68 : 0x0000004051000000  (flags = 0x51000000, base size = 0x40)
```

The authoritative descriptor bytes captured for this EEType are the
relocation-independent series values `series_size = -0x28`,
`series_start = +0x08`, and `series_count = 1`. Together with object size
`0x40`, they describe three eight-byte reference slots at `+0x08`, `+0x10`,
and `+0x18`. They exactly match the three reference fields above. The
allocation is eight-byte aligned, with no evidence of a runtime-owned prefix
before the address returned by `RhpNewFast`: the returned address is the
header address itself.

The descriptor window is the one recorded in the Phase 53J analysis and
repeated in the live capture. The decisive layout invariants are:

```text
worker base:              0x400005000e50
managed type size:        0x38
NativeAOT allocation size: 0x40
alignment:                0x8
expected post-allocation: 0x400005000e90
```

### Exact worker birth and publication

The authoritative birth log is
`artifacts/phase53l-worker-allocation-capture-7/gdb.stdout.log`. It used the
historical relocated base `0x501f000`, so the runtime EEType was exactly
`0x5694e68`. Captures 4, 5, 7, and 8 independently reached the same complete
birth sequence; captures 7 and 8 also reproduced the later target-side
failure, but did not reach the Phase 53K consumer breakpoint.

The generated method is
`ManagedSerialDriverSubsystem__RunDriverWorker`, whose matching disassembly
contains this sequence at RVA `0xf8449` onward:

```text
RVA 0xf8449  lea  rcx,[rip+...]        ; &EEType at image+0x675e68
RVA 0xf8450  call RhpNewFast
RVA 0xf8455  mov  rbp,rax              ; returned worker base
RVA 0xf8458  ...                       ; _dispatcher write-barrier call
RVA 0xf8464  ...                       ; _serialDriver write-barrier call
RVA 0xf8472  mov  [rbp+0x18],0         ; _keyboardDriver
RVA 0xf8476  mov  [rbp+0x20],0         ; Created
RVA 0xf8480  call RhpAssignRefAVLocation ; publish s_driverWorker
RVA 0xf8489  mov  [rcx+0x20],2         ; Running, inlined Start()
```

The constructor symbol exists in the PDB, but this call site does not call
the standalone constructor symbol. Constructor field initialization and
`Start()` are inlined into `RunDriverWorker`; the source-level operation is
`ManagedSerialDriver.cs:985`, `new ManagedDriverWorker(dispatcher, driver)`,
followed by the assignment to `s_driverWorker`.

The exact allocation helper and its relevant instructions are:

```text
RhpNewFast RVA 0x148a20
RVA 0x148a51  mov [rax],rcx       ; first header/EEType write
RVA 0x148a54  mov [rdx+0x08],r8   ; commit new alloc_ptr
RVA 0x148a58  ret                 ; return object base in RAX
```

The capture’s event sequence was:

| Event | Thread/TLS and context | Pointer state | Meaning |
|---|---|---|---|
| 1 | managed-driver scheduler worker stack `RSP=0x4e65e98`; `RDX=0x4e5f030` | EEType `0x5694e68` | `RhpNewFast` entry, return address `0x5117455` |
| 2 | TLS block `0x4e5f000`; `gc_alloc_context=0x4e5f038`; `GS=0x4e61000`; TLS index 0 | before `0x400005000e50`, limit `0x400005000fd0`, expected after `0x400005000e90` | allocator context and size `0x40` captured |
| 3 | same context and worker stack | header at worker base was nil, then `0x5694e68` | first worker-header writer at runtime PC `0x5167a51`, RVA `0x148a51` |
| 4 | same context | new pointer `0x400005000e90` | exact boundary commit at runtime PC `0x5167a54`, RVA `0x148a54` |
| 5 | same context | after `0x400005000e90`, limit unchanged | returned worker base `0x400005000e50`; expected pointer matched |
| 6 | same context, managed return site | pointer `0x400005000e90` | returned to `RunDriverWorker` at RVA `0xf8455` |
| 7–8 | same context | pointer `0x400005000e90` | constructor fields and Created state initialized |
| 9–11 | same context | pointer `0x400005000e90` | publication call site RVA `0xf8480`, helper store RVA `0x148c80`, after-store RVA `0x148c83` |
| 12 | same context | pointer `0x400005000e90` | `Start()` completed its Running state write after publication |

The exact publication destination was
`0x4000000008c0`, the `s_driverWorker` GC-static storage cell. The helper’s
first instruction is `mov [rcx],rdx` at RVA `0x148c80`; the captured value was
`0x400005000e50`, and the after-store observation showed the same value in the
root. No GC was observed between allocation, initialization, and publication.
At publication, the owning worker context already had a valid post-allocation
pointer, so the worker birth itself satisfies the NativeAOT bump-pointer
invariant.

### Allocating thread and context ownership

The allocating execution is the scheduler-created managed driver worker, not
the boot/main loader execution. The native source creates that thread
suspended and resumes it in
`src/Gate4Harness/managed_kernel_driver_worker.c:150-153`; its
`worker_entry()` invokes the managed entry at lines 41–53. The GDB call chain
at allocation is the same worker stack (`0x4e65...`) and the same private
TLS block (`0x4e5f000`). This is stronger than assigning ownership from an
incidental stack alone: the native worker-entry source, generated managed
call site, TLS base, GS base, and call stack agree.

The original worker allocation context in the birth capture was
`0x4e5f038`. Its `context-0x08` combined limit was `0x400005000fd0`, its
`alloc_ptr` was `0x400005000e50` before allocation, and its `alloc_limit` was
`0x400005000fd0`. After `RhpNewFast`, its `alloc_ptr` was
`0x400005000e90`; `alloc_limit` remained `0x400005000fd0`.

The context is not the Phase 53K context address `0x501d038`. That K address
was observed on a different stack during a prior non-worker allocation and was
later passed to the consumer while the managed worker stack was active. The
addresses are not treated as equal merely because their low offsets are the
same.

### Context lineage and the first proven disagreement

The following table combines facts from the Phase 53L birth capture with the
already-authoritative Phase 53K capture. Event numbers are local to their
respective capture; this is not presented as one uninterrupted GDB event
stream.

| Evidence event | Thread/TLS identity | Context | `alloc_ptr` | `alloc_limit` | Meaning |
|---|---|---:|---:|---:|---|
| L worker allocation before | worker stack `0x4e65...`, TLS `0x4e5f000`, `GS=0x4e61000` | `0x4e5f038` | `0x400005000e50` | `0x400005000fd0` | worker allocation begins at the returned object base |
| L worker allocation after | same worker/TLS | `0x4e5f038` | `0x400005000e90` | `0x400005000fd0` | correct `+0x40` advance |
| L publication | same worker/TLS | `0x4e5f038` | `0x400005000e90` | `0x400005000fd0` | root publication occurs after the valid advance |
| K event 1346 | main/loader stack `RSP=0x7e64628` | `0x501d038` | `0x400005000d78` | `0x400005000fd0` | last known-good K state |
| K event 1347 | main/loader stack `RSP=0x7e64728` | `0x501d038` | `0x400005000e50` | `0x400005000fd0` | a different `RhpNewFast` operation, EEType `0x5696950`, advances the non-worker context to the worker address |
| K event 1348–1349 | managed worker stack `0x4e65...` | root `0x4000000008c0` | — | — | worker address is published and dispatched as a live object |
| K event 1350 | worker stack around `0x4e65a58` | `0x501d038` passed in `RDI` | `0x400005000e50` | `0x400005000fd0` | `FixAllocContext` reaches the known invalid boundary |

Phase 53L therefore disproves Case 1: the worker allocation did advance its
own context from `0x...0e50` to `0x...0e90`. It also disproves the Phase 53K
assumption that the K event-1347 `RhpNewFast` call was necessarily the worker
allocation: that call used EEType `0x5696950`, ran on the main/loader stack,
and advanced context `0x501d038` from `0x...0d78` to `0x...0e50`.

There is a concrete ownership mechanism that explains the two observations.
Before the managed worker is run, `gate4_loader.c:14833-14843` reads the
initialized main TLS block and calls
`gxos_managed_kernel_driver_worker_configure_nativeaot_tls`. That helper
zeroes the worker TLS vector and block, then copies the entire 4-KiB source
block byte-for-byte at `managed_kernel_driver_worker.c:173-197`. The copied
block includes the mutable NativeAOT allocator words at the offsets that
become `context-0x08`, `alloc_ptr`, and `alloc_limit`.

The evidence-backed inference is that the main/loader context can retain the
`0x...0e50` boundary while the private worker copy starts at the same boundary
and correctly advances to `0x...0e90`. The later K consumer then receives the
main/loader context `0x501d038` while executing on the worker stack. That is a
duplicated or mismatched allocation-context ownership state, not a failure of
the worker’s own `RhpNewFast` update. It is consistent with the explicit TLS
block clone and with every captured address, stack, and pointer value.

This remains an inference rather than a complete single-boot proof. No Phase
53L capture simultaneously logged the TLS-block copy instruction, the source
context value at the copy, the worker-context value immediately after the
copy, and the later `FixAllocContext` selection. The low-perturbation captures
7 and 8 reached the worker birth and publication but hit the existing target
`#UD` path before the pre-`SetFree` breakpoint. Capture 6 recorded subsequent
writes to the worker’s original context (`0x...0e90 → 0x...16a8`, back to
`0x...0e90`, then `0x...0e90 → 0x...0ea8` and a refill to `0x...16c0`) before
the bounded run was stopped; it never showed that context returning to
`0x...0e50`.

Thus the first state that is directly proven to violate the live-range
invariant remains K event 1350: the context actually consumed by
`FixAllocContext` has `alloc_ptr == 0x400005000e50` while that address is a
rooted, initialized, dispatched `ManagedDriverWorker`. The earliest likely
ownership disagreement is the mutable-TLS-context clone, but the exact
instruction that later selects or restores `0x501d038` is still unresolved.

### Adjacent allocation evidence

No post-worker neighboring object was captured in the focused Phase 53L
trace. The adjacent K evidence is limited to the immediately preceding
main-context fast-path allocation: it advanced `0x400005000d78` to
`0x400005000e50` by `0xd8` bytes before worker stage 1. Because the worker
birth used a different TLS context in the L capture, that observation is
useful for explaining the shared boundary but is not claimed as a same-context
adjacent-object proof.

### Diagnostic tooling and validation

Phase 53L added the focused diagnostic driver
`tools/Run-Phase53LWorkerAllocationCapture.ps1`. It validates the exact
payload/PDB hashes, derives the relocated EEType address from RVA `0x675e68`,
breaks the worker-specific `RhpNewFast` path and generated call site, records
the header and boundary instructions, captures TLS/GS/context state, records
the root publication, and optionally watches the original context after
publication. It also has a narrowly scoped pre-`SetFree` comparison and
classifies partial timeouts separately from completed runtime outcomes. It
does not change the payload or runtime behavior.

Capture 1 was a debugger-script address-formatting failure; capture 2 rejected
an unexpected relocation base; capture 3 reached the birth path but stopped
on a GDB formatting error. These are tooling failures, not runtime evidence.
Capture 4 completed allocation/publication, capture 5 completed the same path
with the first lineage instrumentation, and capture 7 completed the path at
the historical relocated address. Captures 6 and 8 are preserved as
incomplete diagnostic runs: capture 6 recorded lineage writes before bounded
operator termination, while capture 8 reached the target `#UD` before the
pre-`SetFree` correlation. The capture summaries and raw GDB/QEMU logs remain
under the ignored `artifacts/phase53l-worker-allocation-capture-*` directories.

The PowerShell script parsed successfully after the final diagnostic changes.
No production source, allocator policy, GC policy, object layout, worker
lifetime, relocation path, CSS/image-resource behavior, or serial-driver
semantics were changed. No rebuild, post-repair boot, or unrelated regression
run was performed because the repair threshold was not reached.

### Phase 53L result

The exact worker allocation, header writer, size, context, post-allocation
pointer, worker execution identity, and root publication are now proven. The
worker itself was allocated correctly. The later Phase 53K boundary belongs to
a different context identity, and the full context-selection/ownership
transition between those observations remains incomplete.

**Outcome: C — worker allocation isolated, but context-transition provenance
remains incomplete; no production repair is justified.**

## Phase 53M — allocation-context handoff and stale-context selection

Phase 53M supersedes the open-provenance portion of Phase 53L. It used the
preserved historical payload and PDB, not a rebuild:

```text
payload SHA256 = E82F3B7111716291CDDE931D6618D14DD3B4490577535B4A1C8760B48F107388
PDB SHA256     = 7270B65498976A8B531E04758C91E1A627E4A916508DB5FC411E9FB4CA8FCA9C
runtime        = NativeAOT package 10.0.11
```

The principal result is **Outcome B — exact context-handoff defect proven;
repair belongs outside the current guideXOS allocator layer**. The evidence
now proves the origin of the stale value, the full TLS clone, the worker's
private advance, the later context selection, the `FixAllocContext` call
chain, and the first invalid boundary. It does not justify clearing fields,
adding a registration call, or changing allocator behavior without first
selecting and validating the supported NativeAOT thread/TLS integration
contract.

### Context lifecycle

The direct `0x501d038` address below is from capture 5. Capture 7 relocated
the same source context to `0x500d038`, with source TLS `0x500d000` and main
GS base `0x500c000`. The worker allocation and object address remained
`0x400005000e50` in both captures.

| Event | Thread | TLS base | GS base | `gc_alloc_context` | `alloc_ptr` | `alloc_limit` | Origin/source | GC-visible? | Meaning |
|---|---|---:|---:|---:|---:|---:|---|---|---|
| NativeAOT TLS creation | main TCB `0x1a5150`, identity 1 | `0x501d000` | `0x501c000` | `0x501d038` | `0` | `0` | `initialize_nativeaot_tls`, entry `0x105db0`; block populated at `0x105e50` | main runtime TLS / registered thread | Earliest observed existence; zero-filled runtime block before nonzero allocation state |
| First nonzero `alloc_ptr` store | main TCB identity 1 | `0x501d000` | `0x501c000` | `0x501d038` | `0x20` | `0` | generated `mov [rdx],rax`, RVA `0x15e348`; source `RAX=0x20`, `RDX=context` | yes | First watchpoint-observed pointer value; zero-fill itself preceded the watchpoint |
| First nonzero `alloc_limit` store | main TCB identity 1 | `0x501d000` | `0x501c000` | `0x501d038` | `0x400002800028` | `0x400002800fd0` | generated `mov [rdi+0x08],rax`, RVA `0x162acd`; source `RAX=0x400002800fd0`, destination `RDI=context` | yes | Main context receives its first usable bump range |
| Source reaches worker boundary | main TCB identity 1 | `0x501d000` | `0x501c000` | `0x501d038` | `0x400005000e50` | `0x400005000fd0` | ordinary NativeAOT allocation writes | yes | The original context is still at the future worker address |
| Worker TLS configuration entry | scheduler worker TCB `0x1a59f0`, identity 5, state 2 | `0x4e5f000` | not active yet; later `0x4e61000` | `0x4e5f038` | `0` | `0` | `gate4_loader.c` passes main `g_tls_block` and `0x1000` to `gxos_managed_kernel_driver_worker_configure_nativeaot_tls` | custom scheduler live; NativeAOT ThreadStore membership not proven | Destination is independently allocated and initially zeroed |
| Worker TLS copy reaches context fields | worker TCB identity 5 | `0x4e5f000` | not active yet | `0x4e5f038` | `0x400005000e50` | `0x400005000fd0` | explicit byte loop; helper entry RVA `0x15d600`, byte store RVA `0x15d6f1` | custom scheduler live; ThreadStore membership not proven | Full initialized main TLS state is copied into the worker block |
| Worker context selected by scheduler | worker TCB identity 5, state 3 | `0x4e5f000` | `0x4e61000` | `0x4e5f038` | `0x400005000e50` | `0x400005000fd0` | scheduler context switch RVA `0x164500`; worker start RVA `0x164fc0` | physically live in active GS/TLS; NativeAOT enumeration membership not proven | Worker executes with the cloned context |
| Worker allocation | worker TCB identity 5 | `0x4e5f000` | `0x4e61000` | `0x4e5f038` | `0x400005000e90` | `0x400005000fd0` | `RhpNewFast` RVA `0x148a20`; pointer commit RVA `0x148a54` | physically live; worker root follows | Correctly consumes the `0x40` NativeAOT object size |
| Worker publication | worker TCB identity 5 | `0x4e5f000` | `0x4e61000` | `0x4e5f038` | `0x400005000e90` | `0x400005000fd0` | `s_driverWorker` root cell `0x4000000008c0` | worker object rooted; worker context visibility unresolved | Root contains the live worker while its private context is advanced |
| Context enumeration selection | current scheduler TCB is worker identity 5; selected record is main-origin | `0x501d000` record source | active GS remains `0x4e61000` | selected `0x501d038` | `0x400005000e50` | `0x400005000fd0` | enumerator record base `RBX=TLS+0x30`; `LEA RCX,[RBX+8]`, RVA `0x152e73`; callback call RVA `0x152e7a` | selected main context is GC-visible; worker ThreadStore inclusion unresolved | The runtime selects the original main context, not the active worker GS context |
| `FixAllocContext` entry | worker TCB identity 5 executes the fix | `0x4e5f000` active | `0x4e61000` | selected `0x501d038` | `0x400005000e50` | `0x400005000fd0` | `WKS::gc_heap::fix_allocation_context`, RVA `0x172e20` | selected context treated as GC-visible | Main context is fixed while a different context is active |
| Pre-`SetFree` boundary | worker TCB identity 5 | `0x4e5f000` active | `0x4e61000` | consumer `0x501d038` | `0x400005000e50` | `0x400005000fd0` | direct call site RVA `0x172e95`; object/worker `0x400005000e50`; root still points to it | root proves object is live | Live worker range is consumed as free space; this is the first proven corrupting boundary |

The lifecycle is therefore:

```text
NativeAOT main TLS 0x501d000
    +0x38 -> main context 0x501d038: e50 / fd0
       |
       +-- full 0x1000-byte guideXOS clone --> worker TLS 0x4e5f000
                                                +0x38 -> worker context 0x4e5f038: e50 / fd0
                                                +-- RhpNewFast worker e50
                                                    -> worker context e90 / fd0
       |
       +-- original main context remains e50 / fd0
             |
             +-- GcEnumAllocContexts record base RBX = TLS + 0x30
                 +-- LEA RCX,[RBX+8] at RVA 0x152e73
                 +-- indirect callback at RVA 0x152e7a
                 +-- FixAllocContext adapter and fix_allocation_context
                 +-- SetFree consumes the worker address
```

The graph is not a worker-context regression. It is a main-context selection
after the worker has advanced its private copy.

### Characterization and birth of `0x501d038`

The earliest observed point is `initialize_nativeaot_tls` at native RVA
`0x105db0`. At its block-allocation return site, RVA `0x105e50`, the global
TLS block cell contained `0x501d000` and the context at `+0x38` was zero.
The loader creates the TLS vector, TLS block, GS area, and TEB state, copies
the PE TLS template, stores the block in the TLS vector, and publishes the
vector through the GS-area slot. The initial `alloc_ptr` and `alloc_limit`
values were therefore zero-fill/runtime-initialization state, not copied
from the worker.

The context belongs to the original main scheduler/runtime environment:

```text
main TCB       = 0x1a5150
thread identity = 1
source TLS      = 0x501d000   (capture 5)
source GS       = 0x501c000   (capture 5)
context         = 0x501d038
```

The scheduler-created worker later has TCB `0x1a59f0`, identity 5, worker
GS `0x4e61000`, worker TLS `0x4e5f000`, and worker context `0x4e5f038`.
These are different contexts and different scheduler identities; address
similarity is not being used as an identity claim.

### First writes to the source fields

The hardware watchpoints were installed after the TLS block became
discoverable, so the zero-fill writes are not represented as watchpoint
events. The first nonzero source writes are nevertheless captured:

```text
alloc_ptr:
  instruction = mov [rdx],rax
  exact RVA   = 0x15e348
  watch stop  = RVA 0x15e34b, after the store
  source      = RAX = 0x20, from the preceding LEA computation
  destination = RDX = 0x501d038 (0x500d038 in capture 7)

alloc_limit:
  instruction = mov [rdi+0x08],rax
  exact RVA   = 0x162acd
  watch stop  = RVA 0x162ad1, after the store
  source      = RAX = 0x400002800fd0
  destination = RDI = 0x501d038 (0x500d038 in capture 7)
```

The source context subsequently reaches `alloc_ptr=0x400005000e50` and
`alloc_limit=0x400005000fd0` before the worker TLS configuration call. No
write was captured that regressed this same context from `0x…0e90` to
`0x…0e50`; the later stale value belongs to the original context.

### Copy and scheduler handoff

The source call is explicit in `src/Gate4Harness/gate4_loader.c`: it passes
the initialized `g_tls_block` and `GXOS_SCHEDULER_PAGE_SIZE` to
`gxos_managed_kernel_driver_worker_configure_nativeaot_tls`. The helper in
`src/Gate4Harness/managed_kernel_driver_worker.c`:

1. zeroes the worker TLS vector;
2. zeroes the worker TLS block;
3. copies the source block byte by byte for `0x1000` bytes; and
4. installs the worker block in the worker TLS vector.

The helper entry is RVA `0x15d600`; the byte store is at RVA `0x15d6f1`,
with the loop continuing at RVA `0x15d6f4`. Captures 5 and 7 observe the
destination context changing from zero to the source pair
`0x400005000e50 / 0x400005000fd0` as the copy crosses the two fields.

This is a full initialized-TLS clone, not a copy made by `RhpNewFast` and not
a mutation of the worker context after allocation. The scheduler then
switches GS to `0x4e61000` and selects TLS `0x4e5f000`. The worker starts with
the copied pair, allocates the worker, and advances only its private context
to `0x400005000e90`.

### Exact selection and `FixAllocContext` path

The preserved payload disassembly identifies the callback helper at image
RVA `0x152e30`; the PDB public record is
`?GcEnumAllocContexts@GCToEEInterface@@...` (the PDB code-segment offset is
`0x151e30`, which maps to the image RVA after the code-section base). The
relevant instructions are:

```text
RVA 0x152e70   mov rdx,rsi
RVA 0x152e73   lea rcx,[rbx+0x08]
RVA 0x152e77   mov rax,rbp
RVA 0x152e7a   call [rip+0x425c8]       ; IAT RVA 0x195448
RVA 0x152e80   callback return
```

In the authoritative capture 7, the wrapper entry at RVA `0x172df0` had:

```text
RBX = 0x500d030
RCX = 0x500d038
return = image + 0x152e80
```

Thus the runtime-selected context is exactly the record base plus eight,
matching the static `LEA` instruction. The wrapper's `RDX=0x4e65b60` is a
separate argument record, not the selected context; the wrapper preserves
the selected `RCX` in `R10`, moves it to the indirect-call `RDX`, and invokes
the GC vtable slot. The adapter at RVA `0x15f420` executes `mov rax,rdx`
and `mov rcx,rax` at RVA `0x15f42a`, then tail-jumps to
`WKS::gc_heap::fix_allocation_context` at RVA `0x172e20`. The direct
`SetFree` call is at RVA `0x172e95`.

At the captured endpoint:

```text
selected/fixed context = 0x500d038 (0x501d038 in capture 5)
selected ptr/limit     = 0x400005000e50 / 0x400005000fd0
active worker context  = 0x4e5f038
active worker ptr      = 0x400005000e90
worker/root            = 0x400005000e50
current scheduler TCB  = 0x1a59f0, identity 5
```

The exact high-level collector reason was not emitted as a separate phase
flag. The observed path is the NativeAOT GC allocation-context enumeration
and GC-to-EE callback path; its `GcEnumAllocContexts` record selection and
the subsequent `FixAllocContext`/`SetFree` path are directly correlated.

### NativeAOT ownership invariant

The matching NativeAOT 10.0.11 sources define the relevant contract:

- `gc_alloc_context::init()` starts the allocation fields at zero;
- `GcEnumAllocContexts` enumerates every active allocation context exposed by
  the runtime thread store, and only permits GC-side pointer/limit mutation
  when both fields are zero; and
- the caller of allocation operations must ensure that the passed context is
  owned by the calling thread. Per-thread contexts avoid a lock; contexts
  not owned by the calling thread require unique ownership before use.

The relevant versioned sources are the [NativeAOT GC interface](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/gc/gcinterface.h),
[GC-to-EE interface](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/gc/gcinterface.ee.h),
[NativeAOT thread implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/thread.cpp),
[NativeAOT thread store](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/threadstore.cpp),
and [GC environment bridge](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/vm/gcenv.ee.cpp).

The first proven violation is not the existence of two equal initial pairs.
It is the later use of the original main context, still ending at the live
worker address, while the worker's independent active context has already
advanced beyond that object and the object is rooted in `s_driverWorker`.
`FixAllocContext` therefore binds/fixes the wrong context for the current
worker execution and `SetFree` consumes a live object range.

### Classification and ownership conclusion

The supported classification is **M3 — wrong context selection proven**.
The worker context does not regress (so this is not M1). A context clone does
occur, but it is the worker receiving a copy of the main context; the context
later selected for `FixAllocContext` is the original main context, not the
worker copy. The selection hop is now proven, so C and D no longer describe
the remaining state. The NativeAOT ownership invariant is established, so E
does not apply. The Phase 53L relationship was refined rather than
disproven, so F does not apply.

The complete evidence-backed chain is:

```text
main NativeAOT TLS is created and initialized
  -> main context reaches ptr=e50, limit=fd0
  -> guideXOS clones the full initialized TLS block into worker TLS
  -> worker context starts with the same e50/fd0 pair
  -> scheduler switches GS/TLS to the worker clone
  -> worker RhpNewFast allocates e50 and advances worker context to e90
  -> worker is published at s_driverWorker and is live/rooted
  -> NativeAOT context enumeration derives main record+8 = main context
  -> wrapper/adapter passes main context to fix_allocation_context
  -> FixAllocContext executes on the worker scheduler execution with main ptr=e50
  -> SetFree consumes the live worker address and corruption begins
```

This identifies a concrete guideXOS integration boundary: the custom worker
path clones an already initialized NativeAOT TLS block and switches to it by
custom scheduler mechanisms, while the evidence does not establish a
corresponding NativeAOT `ThreadStore::AttachCurrentThread` registration and
fresh runtime-owned allocation context for that worker. The safe correction
is therefore a supported thread/TLS attachment and ownership transition, not
a local allocator workaround. The evidence does not establish enough of
that bridge's required root, registration, detach, and allocation-space
semantics to implement it safely in this phase.

### Proven facts, strong inferences, and open hypothesis

Proven facts:

- `0x501d038`/`0x500d038` is the original main TLS context, created by the
  NativeAOT TLS initialization path.
- Its first observed nonzero writes, their source registers, destinations,
  and RVAs are captured.
- The guideXOS worker helper copies the initialized main TLS block into a
  separate worker TLS block.
- The worker context advances from `e50` to `e90`; the main context remains
  at `e50`.
- The worker is published/rooted before the stale context is fixed.
- The enumerator's record-plus-eight selection, callback return address,
  wrapper, adapter, direct fix entry, and `SetFree` boundary are correlated.
- The first corrupting boundary is `SetFree` operating from the selected main
  context while the worker object is live.

Strong inferences:

- The selected record is the NativeAOT active/main context record represented
  by `GcEnumAllocContexts`; this is supported by the PDB symbol, generated
  helper shape, and matching NativeAOT source semantics.
- The worker's custom scheduler TLS is not shown to be a member of the
  NativeAOT ThreadStore enumeration that selected the main record.
- The guideXOS custom worker path is exposing runtime thread state without a
  proven supported NativeAOT attach/registration handoff.

Open hypothesis:

- The exact supported replacement bridge—whether an attach hook, runtime
  thread-start transition, scheduler integration contract, or another
  NativeAOT entry point—remains unspecified. It must be resolved before any
  field reset, registration, or scheduler change is considered.

### Diagnostic tooling and validation

Phase 53M added `tools/Run-Phase53MAllocContextHandoffCapture.ps1`. It
hash-gates the historical payload/PDB, discovers the main TLS block, watches
the first source field writes, records scheduler TLS creation/switches,
captures the full worker TLS copy, correlates worker allocation/publication,
records the exact wrapper/adapter/fix path, numbers events, and writes a
machine-readable summary. The script now also has dynamic probes for the
enumerator selection RVA `0x152e73` and callback call RVA `0x152e7a`.

Captures 1–3 are classified as debugger/tooling failures and are not
allocator evidence. Captures 4–7 reached the authoritative handoff and
`FixAllocContext`/`SetFree` endpoint; capture 7 is the most complete final
run and uses the relocated source `0x500d038`. The final PowerShell parser
check passed after the script changes. The new enumeration probes were added
after capture 7; capture 7 proves the selection through the static payload
instructions plus the runtime wrapper registers/return address.

No production source, allocator policy, GC policy, thread registration,
worker lifetime, relocation, CSS/image-resource behavior, or serial-driver
semantics were changed. No rebuild or ceremonial regression run was
performed because no production repair was justified.

Raw evidence remains under the ignored directories:

```text
artifacts/phase53m-alloc-context-handoff-capture-1
artifacts/phase53m-alloc-context-handoff-capture-2
artifacts/phase53m-alloc-context-handoff-capture-3
artifacts/phase53m-alloc-context-handoff-capture-4
artifacts/phase53m-alloc-context-handoff-capture-5
artifacts/phase53m-alloc-context-handoff-capture-6
artifacts/phase53m-alloc-context-handoff-capture-7
```

The smallest remaining question is:

> Which supported NativeAOT thread attachment/registration/TLS-creation
> bridge must replace the custom full-TLS clone and direct worker path so the
> worker owns a fresh zeroed allocation context and is correctly represented
> in ThreadStore/GC enumeration, without losing valid main-thread allocation
> slack, roots, or detach bookkeeping?

**Phase 53M outcome: B — exact context-handoff defect proven; repair belongs
outside the current guideXOS allocator layer. No production repair is
justified.**

## Phase 53N — NativeAOT foreign/custom-thread attachment contract

Status: forensic/design-only. No production repair, allocator change, rebuild,
commit, or push was made in this phase.

### Principal outcome

**Outcome E — NativeAOT's attachment contract is materially narrowed, but the
runtime detach/unregister lifecycle for a guideXOS scheduler TCB remains
unresolved.**

The exact reverse-P/Invoke entry path is established. A fresh scheduler TLS
environment can enter an `[UnmanagedCallersOnly]` export through the generated
NativeAOT thunk, which takes the slow attachment path when the runtime state is
unknown. That path is not what the Phase 53M driver worker currently uses: its
full initialized-TLS copy sets the worker state to an already-attached-looking
value, so the thunk's fast path skips `ThreadStore::AttachCurrentThread`.

The existing scheduler callback/GC probe proves the bounded entry and live-GC
behavior of a fresh worker. It does not prove that the runtime ThreadStore
entry is detached before `gxos_scheduler_collect` frees the worker TLS block.
The current scheduler reclamation path has no call to
`RuntimeThreadShutdown`, `ThreadStore::DetachCurrentThread`, or an equivalent
runtime-owned fiber-destruction callback. That is a critical lifecycle gap;
therefore the Phase 53N repair threshold is not met.

### Version and evidence discipline

The contract audit used the exact source tag selected by the Phase 53M
analysis: NativeAOT/runtime **v10.0.11**, commit
`79d0c463f1b55624c874a11585f7e47731e8d675`, inspected in an external temporary
clone. The repository's reproducible build record identifies the historical
artifact toolchain as SDK `10.0.302`, runtime/NativeAOT pack/ILCompiler
`10.0.10`; the repository pins SDK `10.0.302` with roll-forward disabled in
`global.json`. The one-patch-level artifact/source uncertainty is recorded
explicitly; runtime `main` or a newer branch was not substituted.

The primary versioned sources are the [NativeAOT thread store](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/threadstore.cpp),
[thread TLS accessors](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/threadstore.inl),
[thread implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/thread.cpp),
[thread inline transition logic](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/thread.inl),
[GC-to-EE bridge](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/gcenv.ee.cpp),
[GC allocation context](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/gc/gcinterface.h),
[Windows PAL thread/FLS hooks](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/windows/PalMinWin.cpp),
[Windows PAL stack bounds](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/windows/PalCommon.cpp),
and [runtime thread shutdown](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/startup.cpp).

Facts below are labeled as one of:

* **Runtime fact** — directly defined by v10.0.11 source.
* **Repository fact** — directly present in guideXOS source, diagnostics, or
  preserved artifacts.
* **Inference** — a conclusion joining those facts; it is not a new runtime
  API claim.
* **Proposal** — deferred Phase 53O design, not an implementation.

### NativeAOT threading contract

#### Runtime thread representation

**Runtime fact:** v10.0.11 declares a platform-thread-local
`RuntimeThreadLocals tls_CurrentThread`. `ThreadStore::RawGetCurrentThread()`
returns the address of that TLS storage cast to `Thread*`. The NativeAOT
`Thread` is therefore not a separately allocated opaque record created by the
guideXOS scheduler; its runtime-local storage is the current thread's TLS
object.

`RuntimeThreadLocals` contains, among other fields:

* an EE allocation context and the embedded GC allocation context;
* `m_ThreadStateFlags` (`TSF_Unknown`, `TSF_Attached`, or `TSF_Detached` plus
  other state flags);
* current, deferred, and cached P/Invoke transition frames;
* the ThreadStore next pointer;
* exception state and thread-static storage/root lists;
* GC frame registrations;
* stack low/high bounds;
* OS thread handle and OS thread ID; and
* runtime interruption/stress-log state where enabled.

The address of a worker TLS block can consequently produce a pointer-shaped
`Thread*`, but that pointer is only a valid managed thread after runtime
construction, state initialization, ThreadStore registration, and platform
attachment have completed.

#### ThreadStore registration

**Runtime fact:** `ThreadStore::AttachCurrentThread(bool)` obtains the current
TLS `Thread*`, rejects a detached thread, returns immediately if the state is
already initialized, calls `PalAttachThread`, calls `Thread::Construct`, sets
`TSF_Attached`, and pushes the `Thread*` into the runtime ThreadStore list.
`Thread::Construct` sets the initial transition-frame markers, obtains the OS
thread ID and handle, and obtains maximum stack bounds. The allocation context
is expected to be zero from static TLS initialization; `Construct` deliberately
does not manufacture a copied allocation context.

The corresponding `ThreadStore::Iterator` walks the runtime ThreadStore list,
not the guideXOS scheduler's `GXOS_SCHEDULER_TCB` array. `GcEnumAllocContexts`
enumerates that list and passes each `Thread`'s own EE/GC allocation context to
the GC. Changing GS or the TLS vector alone does not insert a new record into
that list.

#### Foreign/native attachment through reverse P/Invoke

**Runtime fact:** the v10.0.11 generated unmanaged-to-managed path is:

```text
generated UnmanagedCallersOnly export thunk
  -> RhpReversePInvoke(ReversePInvokeFrame*)
  -> InlineTryFastReversePInvoke
       -> if TSF_Attached and preemptive: save frame, enter cooperative mode
       -> otherwise slow path
  -> RhpReversePInvokeAttachOrTrapThread2
  -> Thread::ReversePInvokeAttachOrTrapThread
  -> optional EnsureRuntimeInitialized
  -> ThreadStore::AttachCurrentThread()
  -> save prior transition state and enter cooperative mode
  -> managed body
  -> RhpReversePInvokeReturn
  -> restore the saved transition frame/state
```

The slow path is selected by `TSF_Attached` being absent. A copied block with
`TSF_Attached` present suppresses the attach operation; it is not a supported
registration handoff. Ordinary reverse-P/Invoke return is a transition return,
not a detach operation.

**Repository fact:** `GxManagedKernelRunDriverWorker` is an
`[UnmanagedCallersOnly]` export and is invoked through the generated export
address. The current native wrapper supplies the Microsoft x64 ABI and calls
the export directly; it does not call a runtime attach helper itself.

**Repository fact:** the separate `NativeAotEventWait` scheduler callback probe
creates a fresh zeroed scheduler TLS block and calls `ManagedCallback` through
the same style of generated thunk. Its diagnostics observe the worker state
changing from zero to the attached/preemptive sentinel, distinct FLS, managed
allocation, GC, and root survival while that worker remains live. This is the
existing evidence for the entry path, not evidence that the later scheduler
reclamation performs NativeAOT detach.

#### Runtime-created thread entry

**Runtime fact:** v10.0.11 also has the internal `RhThreadEntryPoint`, returned
by the exported `RhGetThreadEntryPointAddress`. It calls
`ThreadStore::AttachCurrentThread()` before invoking the managed class-library
thread entrypoint, sets a deferred transition frame, and manages preemptive
mode around that entrypoint. This is the runtime's OS-thread creation path; it
is not a general scheduler-TCB registration API and is not used by the current
guideXOS worker.

#### Allocation-context initialization

**Runtime fact:** `gc_alloc_context` starts with `alloc_ptr`, `alloc_limit`,
allocation counters, reserved GC fields, and allocation count zero. The EE
`ee_alloc_context` wraps it with `combined_limit`. Each attached runtime
`Thread` owns one such context; the GC enumerates all active contexts from
ThreadStore. `GcEnumAllocContexts` permits the GC callback to zero the pointer
and limit pair and then keeps `combined_limit` consistent; it is not an API for
transferring ownership between threads.

**Inference:** a fresh worker context must be created by the runtime's fresh
TLS/attach path. Copying main's nonzero pair is not a handoff, and zeroing only
that pair would still leave the other per-thread state unresolved.

#### Stack bounds and GC state

**Runtime fact:** `Thread::Construct` calls `PalGetMaximumStackBounds`. On the
v10.0.11 Windows PAL this uses `VirtualQuery` for the low allocation base and
the current TEB's `StackBase` for the high bound. The ThreadStore and GC use
these bounds with the transition frame to walk a thread's managed stack.

**Repository fact:** guideXOS creates a private committed worker stack,
registers it with its own VM stack ledger, writes synthetic TEB low/high values,
and switches RSP and GS in `gxos_scheduler_context_switch`. That proves the
guideXOS scheduler stack contract. It does not, by itself, prove that the
runtime `Thread` has been constructed with those bounds or that the runtime's
GC suspend/stack-walk machinery can safely coordinate with every saved green
stack.

**Runtime fact:** cooperative mode is represented by a null current transition
frame; preemptive mode is represented by the saved/top-of-stack transition
state. Reverse-P/Invoke must enter cooperative mode for managed execution and
restore the prior preemptive state on return. A worker that starts with copied
transition-frame pointers can be in an invalid mode and can alias another
thread's frame state.

#### GC suspension and root enumeration

**Runtime fact:** `GcScanRoots` iterates ThreadStore entries, skips only GC
special threads, scans thread-static roots, and walks each `Thread`'s managed
stack using its transition frame and stack bounds. `GcEnumAllocContexts` also
iterates ThreadStore entries. NativeAOT GC suspension uses the ThreadStore and
platform thread state; it is not driven by the guideXOS runnable queue.

**Inference:** a worker with only a scheduler TCB, private GS, and private RSP
is not automatically GC-visible. It must be a correctly attached ThreadStore
entry with valid runtime stack/transition state, or the NativeAOT port must
explicitly integrate the scheduler's saved green stacks and suspension model.

#### Detach and runtime shutdown

**Runtime fact:** `ThreadStore::DetachCurrentThread()` removes the current
`Thread*` from ThreadStore, calls `Thread::Detach`, fixes/releases its
allocation context, marks it detached, and then calls `Thread::Destroy`. On
Windows, the NativeAOT PAL allocates an FLS slot with `FiberDetachCallback`;
that callback calls `RuntimeThreadShutdown`, which calls
`ThreadStore::DetachCurrentThread` when the home fiber is destroyed.
`PalAttachThread` associates the current runtime `Thread*` with the current
fiber and explicitly fail-fasts if another runtime thread is attached to the
same fiber.

**Repository fact:** guideXOS virtualizes FLS values through per-TCB arrays,
but `gxos_scheduler_create_suspended_thread`/`maybe_reclaim_thread` only
create, terminate, unregister the scheduler stack, free the synthetic
GS/vector/TLS/TEB pages, release the scheduler object, and clear the TCB. The
current path does not invoke `RuntimeThreadShutdown`, a runtime detach export,
or a per-TCB `FiberDetachCallback` equivalent before freeing worker TLS.

**Conclusion:** generated thunk return and scheduler TCB reclamation are not
the same lifecycle event. Detach/unregister is the unresolved Phase 53N
requirement.

### NativeAOT TLS block layout

The 0x1000-byte value is an image-specific TLS block, not a documented
standalone “thread object” ABI. In the Phase 53 payload, the generated
`tls_CurrentThread` storage begins at payload block `+0x30`; this agrees with
the observed transition state at `+0x78` and the Phase 53M context at `+0x38`.
The payload-relative map below distinguishes the loader's historical labels
from the runtime field names:

| Payload block offset | Runtime/source meaning in this payload | Ownership and clone status |
| --- | --- | --- |
| `+0x00..+0x2F` | PE TLS template/image-specific TLS storage preceding `tls_CurrentThread` | Template bytes may be initialized from the image; runtime-written values are not generally cloneable |
| `+0x30` | `ee_alloc_context.combined_limit`; loader diagnostics call this `TLS_ALLOC_LIMIT` | Runtime/GC-owned; must be fresh and consistent with the worker context |
| `+0x38` | `gc_alloc_context.alloc_ptr`, the first field of the embedded GC context | Per-thread and mutable; must be fresh |
| `+0x40` | `gc_alloc_context.alloc_limit` | Per-thread and mutable; must be fresh |
| `+0x48..+0x68` | allocation counters, GC-reserved fields, and allocation count in the embedded x64 context | Per-thread/GC-owned; must not be copied as live state |
| `+0x70` | `m_ThreadStateFlags` for this payload layout | Must begin as `TSF_Unknown` for reverse-P/Invoke attach, then be runtime-set |
| `+0x78` | `m_pTransitionFrame` for this payload layout | Must be constructed by the runtime; copied pointer is invalid/aliased |
| following runtime fields | deferred/cached transition frames, ThreadStore next link, exception state, thread statics, GC frame registrations, stack bounds, OS handle/ID, and optional runtime fields | Must be zeroed/constructed or runtime-populated per field; never copied wholesale |
| outside `tls_CurrentThread` | guideXOS scheduler FLS arrays, COM state, TCB identity, stack metadata, and synthetic GS/TEB bookkeeping | Scheduler-owned; separate from NativeAOT ThreadStore ownership |

The `+0x30`/`+0x38` pair emitted by current loader diagnostics is therefore a
payload-layout observation. In the v10.0.11 source, `combined_limit` is an EE
wrapper field and the actual `gc_alloc_context` begins at the payload's
`+0x38`; the actual `alloc_limit` is the second pointer in that context at
`+0x40`. The source context used by Phase 53M was correctly identified as
`TLS+0x38`, with the pair read at `context` and `context+8`.

**Conclusion:** full initialized-TLS cloning is unsupported. It copies not
only the live allocation range, but also attachment flags, transition-frame
state, ThreadStore linkage, exception/thread-static/GC registration pointers,
stack and OS identity, and other runtime-owned fields. “Copy then zero the
allocation context” is not an established repair.

### Current guideXOS worker lifecycle

The actual Phase 53M driver path is:

```text
initialize_nativeaot_tls(image, boot services)
  -> allocate raw main GS/vector/TLS/TEB pages
  -> zero pages and copy only the PE TLS template
  -> install TLS vector and synthetic TEB stack bounds
  -> activate main GS
  -> call NativeAOT process entry once
  -> scheduler adopts the main GS/vector/TLS/TEB as boot TCB identity 1

gxos_managed_kernel_driver_worker_initialize
  -> create scheduler event
  -> gxos_scheduler_create_suspended_thread
       -> allocate 16 KiB private stack and canary page
       -> allocate zeroed private GS/vector/TLS/TEB pages
       -> write vector/TEB/GS scheduler fields
       -> assign scheduler identity 5 in the authoritative capture
  -> resume the scheduler TCB

gate4_loader.c Phase 9 setup
  -> read the payload TLS index
  -> pass g_tls_block and 0x1000 to
     gxos_managed_kernel_driver_worker_configure_nativeaot_tls
  -> zero worker vector and worker block
  -> copy the entire initialized main TLS block byte-by-byte
  -> install worker block in the worker TLS vector

first scheduler activation
  -> scheduler context switch saves/restores registers, RSP, flags, FP state,
     XMM state, and GS base
  -> worker entry sets scheduler sentinels
  -> worker calls GxManagedKernelRunDriverWorker through the generated thunk
  -> managed worker allocates/publishes/dispatches
  -> worker waits, wakes, yields, or terminates through scheduler APIs

stop/reclaim
  -> signal worker event and pump scheduler until TCB is terminated
  -> close handle and collect scheduler objects
  -> unregister scheduler stack VM region
  -> free scheduler stack/canary/GS/vector/TLS/TEB pages
  -> clear scheduler TCB
```

The worker helper and call site are in
[managed_kernel_driver_worker.c](../src/Gate4Harness/managed_kernel_driver_worker.c)
and [gate4_loader.c](../src/Gate4Harness/gate4_loader.c). The scheduler's
environment creation, boot adoption, context switch, and reclamation are in
[scheduler_foundation.c](../src/Gate4Harness/scheduler_foundation.c),
[scheduler_foundation.h](../src/Gate4Harness/scheduler_foundation.h), and
[scheduler_context.S](../src/Gate4Harness/scheduler_context.S).

### Contract mismatch matrix

| Requirement | v10.0.11 expected path | Current guideXOS path | Status |
| --- | --- | --- | --- |
| Unique runtime Thread | `tls_CurrentThread` is fresh per attached execution context | Worker receives a byte-for-byte copy of main runtime-local state | **FAIL** |
| ThreadStore registration | `ThreadStore::AttachCurrentThread` pushes the constructed Thread | No worker-side call; copied `TSF_Attached` suppresses slow attach | **FAIL** |
| Fresh allocation context | zero TLS initialization, then runtime/GC owns one context | main nonzero `gc_alloc_context` copied into worker | **FAIL; M3 reproduced** |
| Combined allocation limit | runtime keeps `combined_limit` aligned with the worker context | main wrapper field copied with main context | **FAIL/unproven** |
| TLS template | copy image TLS template, then runtime-populate thread state | main initialized runtime block copied after startup | **FAIL** |
| Thread flags | begin `TSF_Unknown`; runtime sets `TSF_Attached` | worker inherits main attachment flags | **FAIL** |
| Transition frame/mode | runtime saves frame and enters cooperative mode; return restores it | worker inherits main transition pointers and mode | **FAIL/unproven** |
| ThreadStore link | runtime owns a valid list link | worker may contain copied main link but is not inserted | **FAIL/dangerous alias** |
| Exception state | fresh per-thread state | copied pointers/state | **FAIL/unproven** |
| Thread statics/GC registrations | runtime constructs and registers worker-specific state | copied runtime pointers; no worker registration proof | **FAIL/unproven** |
| OS thread identity/handle | `Construct` obtains current OS identity/handle | copied main fields; scheduler identity is separate | **FAIL** |
| Stack bounds | `PalGetMaximumStackBounds` initializes runtime Thread bounds | scheduler writes synthetic TEB and owns separate stack ledger | **PARTIAL; runtime use unproven** |
| GS/TLS activation | PAL/runtime current TLS points to the attached Thread | scheduler switches GS/vector manually | **PARTIAL** |
| FLS | runtime PAL associates current fiber and invokes detach callback | guideXOS virtualizes values per TCB | **PARTIAL; detach missing** |
| GC allocation enumeration | GC walks ThreadStore entries | main selected; worker entry not proven in current driver path | **FAIL** |
| GC root scanning | ThreadStore entry, valid transition frame, stack bounds | worker stack is scheduler-visible but not runtime-registered | **FAIL for current driver worker** |
| GC suspension | runtime PAL/thread-store suspension protocol | scheduler queue/context switch protocol | **UNPROVEN** |
| Managed entry | generated reverse-P/Invoke thunk | same generated export thunk | **PASS for entry mechanism** |
| Runtime initialization | attach slow path may ensure initialization | process startup is called once before worker | **PASS for existing startup prerequisite** |
| Detach/unregister | FLS/fiber callback -> `RuntimeThreadShutdown` -> ThreadStore detach | TCB reclaim frees TLS without runtime detach | **FAIL; critical unresolved issue** |
| Allocator ownership | one active context per registered runtime Thread | cloned main and worker contexts can claim same range | **FAIL; Phase 53M M3** |

### Main and worker identity mapping

| guideXOS context | Scheduler state | GS / TLS facts | NativeAOT Thread / ThreadStore conclusion |
| --- | --- | --- | --- |
| Main TCB, identity 1 | boot/current; adopted after NativeAOT startup | `initialize_nativeaot_tls` creates the raw environment; scheduler adopts `g_gs_area`, `g_tls_vector`, `g_tls_block`, and main TEB | `RawGetCurrentThread()` points into main TLS; main context is the record selected by Phase 53M `GcEnumAllocContexts` and is GC-visible |
| Driver worker TCB, identity 5 | private stack; scheduler current during the Phase 53M failure | private GS/vector/TLS/TEB, then full initialized main block clone; worker context advances independently | a pointer-shaped copied `Thread` storage exists, but no distinct valid ThreadStore registration is proven; source semantics say the copied attached flag bypasses the attach path |
| Callback-probe worker, representative identity 5 | fresh scheduler TCB, no TLS copy | private zeroed TLS; generated callback thunk changes runtime state from unknown to attached/preemptive sentinel | entry-time attachment is established by the exact thunk/source path and bounded GC proof; post-TCB-reclaim runtime detach is not established |

Consequently, the answer for the current **driver** worker is:

1. It has a distinct guideXOS TCB, stack, GS base, TLS vector, TLS block, FLS
   storage, and scheduler identity.
2. It does not currently have a proven distinct valid NativeAOT ThreadStore
   record.
3. It executes through a copied runtime-local `Thread` representation whose
   `TSF_Attached` state prevents the normal foreign-thread registration path.
4. Its managed stack is not currently proven safely visible to NativeAOT GC;
   the separate fresh callback/GC probe only proves the narrower live-worker
   case before scheduler reclamation.

### Existing path versus bypass

The repository already contains a **fresh-entry** path in the callback probe:
create a zeroed scheduler environment, enter a generated export, let
`RhpReversePInvoke` attach, execute managed code, return through the generated
transition, and run GC while the worker remains live. This is a real existing
mechanism and is the reason N5 is a valid secondary classification.

The managed driver worker bypasses that entry precondition by invoking
`gxos_managed_kernel_driver_worker_configure_nativeaot_tls` after creating the
worker and copying the initialized main block. The bypass explains the Phase
53M wrong-context selection, but reusing the fresh-entry pattern alone does
not resolve the missing runtime detach before TCB/TLS reclamation.

### Runtime helpers and callable boundary

The exact v10.0.11 helper set relevant to guideXOS is:

| Helper/API | Visibility/calling convention | Role and guideXOS status |
| --- | --- | --- |
| `RhpReversePInvoke` | generated/runtime `FCIMPL1`; takes `ReversePInvokeFrame*` | Entry transition; called by generated export thunk, not directly by current loader |
| `RhpReversePInvokeAttachOrTrapThread2` | `EXTERN_C NOINLINE`, `FASTCALL`; internal bridge to `Thread::ReversePInvokeAttachOrTrapThread` | Internal slow attach helper; not a complete public worker lifecycle API |
| `RhpReversePInvokeReturn` | generated/runtime `FCIMPL1`; takes `ReversePInvokeFrame*` | Restores the saved transition state; does not detach |
| `ThreadStore::AttachCurrentThread` | internal C++ static method | Constructs/registers current TLS Thread; reached by reverse P/Invoke or runtime thread entry |
| `ThreadStore::DetachCurrentThread` | internal C++ static method | Removes/fixes/destroys current runtime Thread; no current direct guideXOS call |
| `RhGetThreadEntryPointAddress` | exported runtime `FCIMPL0` returning a function pointer | Returns runtime-created-thread entrypoint; not an arbitrary TCB attach API |
| `RhThreadEntryPoint` | internal static platform thread entry | Attaches an OS-created runtime thread and invokes classlib ThreadEntryPoint |
| `PalAttachThread` | internal PAL hook | Associates runtime Thread with current OS thread/fiber; Windows PAL rejects multiple runtime Threads on one fiber |
| `PalGetMaximumStackBounds` | internal PAL hook | Supplies runtime Thread stack bounds |
| `PalInitComAndFlsSlot` / `FlsAlloc` | internal PAL plus imported platform FLS | Allocates the runtime FLS slot and shutdown callback |
| `FiberDetachCallback` / `RuntimeThreadShutdown` | internal callback/shutdown path | Expected detach/unregister trigger; missing from scheduler TCB destruction |

The generated `GxManagedKernelRunDriverWorker` export is the legitimate
callable boundary already used by guideXOS. Directly fabricating a
`ReversePInvokeFrame`, calling internal attach code, or writing runtime flags
would not establish the missing platform/thread-store ownership. A proper
bridge must be a NativeAOT platform/runtime shim or must give each runtime
Thread a lifecycle that actually invokes the runtime-owned detach path.

### Proposed supported lifecycle for Phase 53O

This is a proposal, not a Phase 53N implementation. It has two possible
architectural forms, and the detach/GC choice must be made before production
code changes.

#### Form 1: one runtime Thread per actual OS/fiber execution context

```text
create scheduler/native execution context with a one-to-one runtime home fiber
  -> allocate private stack and raw zeroed TLS/vector/GS/TEB state
  -> initialize the platform PAL/FLS association for that execution context
  -> enter the generated UnmanagedCallersOnly export
  -> RhpReversePInvoke sees TSF_Unknown
  -> ThreadStore::AttachCurrentThread
       -> PalAttachThread
       -> Thread::Construct
       -> fresh zero allocation context and stack bounds
       -> TSF_Attached and ThreadStore insertion
  -> generated transition enters managed cooperative mode
  -> GC enumerates/suspends the registered Thread and its managed stack
  -> generated return restores preemptive state
  -> destroy the home fiber / invoke the equivalent runtime shutdown callback
  -> RuntimeThreadShutdown
       -> ThreadStore::DetachCurrentThread
       -> FixAllocContext and Thread::Destroy
  -> only then release guideXOS TLS, GS, stack, FLS, and TCB resources
```

This form preserves the v10.0.11 PAL invariant that a fiber has one runtime
home Thread. It requires proving that guideXOS can provide the corresponding
OS/fiber lifecycle in the target environment.

#### Form 2: retain green scheduler TCBs with a deliberate NativeAOT port

```text
create zeroed scheduler TCB/TLS/stack
  -> NativeAOT port shim creates/registers a matching runtime Thread
  -> shim binds runtime Thread stack/transition/GC state to the TCB
  -> every scheduler switch performs the runtime-required Thread/GC state handoff
  -> GC suspension/root enumeration includes every runnable or blocked green stack
  -> generated managed entry executes only with the matching current Thread
  -> scheduler termination calls runtime detach while that TCB/TLS is current
  -> verify ThreadStore removal and GC quiescence
  -> release TCB/TLS/stack resources
```

This is broader than an allocator or scheduler flag change: it requires a
defined port contract for ThreadStore membership, transition frames, stack
walking, suspension, FLS callbacks, OS identity/handle behavior, and teardown.
The current repository does not expose that complete contract.

### N1–N5 ownership classification

* **N1 — not sufficient:** the scheduler can allocate a distinct TCB and call
  the existing export, but it cannot by itself perform the missing runtime
  ThreadStore registration and detach lifecycle through an exposed API.
* **N2 — selected:** a guideXOS NativeAOT platform/runtime shim is needed to
  connect the scheduler execution-context lifecycle to NativeAOT PAL/FLS,
  stack, ThreadStore, and shutdown ownership, or to provide a verified shim
  around the internal runtime boundary.
* **N3 — selected jointly:** the current NativeAOT platform integration has
  guideXOS FLS/GS/TEB emulation but lacks the runtime-equivalent per-TCB fiber
  destruction/shutdown hook and complete custom-thread lifecycle. That is a
  missing port implementation, not an allocator defect.
* **N4 — not proven, but a Phase 53O gate:** the v10.0.11 Windows PAL assumes
  one home fiber per runtime Thread, while guideXOS multiplexes green TCBs.
  The bounded callback/GC proof shows this is not immediately impossible in
  the current emulation, but full suspension and teardown compatibility are
  unresolved. If those cannot be proven, the ownership escalates to N4.
* **N5 — true as a secondary finding:** the repository's fresh zeroed callback
  path already exercises the correct reverse-P/Invoke entry mechanism, while
  the managed driver worker bypasses it with a full initialized-TLS clone.
  N5 does not eliminate the N2/N3 detach gap.

### Diagnostics, validation, and preserved captures

No production or diagnostic source changes were made in Phase 53N. The audit
used read-only source inspection, the exact v10.0.11 source clone, repository
preflight, the existing callback/GC documentation and source path, and the
authoritative Phase 53M artifacts. No build, QEMU boot, or allocator rerun was
performed because the repair threshold was not met.

The authoritative Phase 53M raw captures remain preserved at:

```text
artifacts/phase53m-alloc-context-handoff-capture-1
artifacts/phase53m-alloc-context-handoff-capture-2
artifacts/phase53m-alloc-context-handoff-capture-3
artifacts/phase53m-alloc-context-handoff-capture-4
artifacts/phase53m-alloc-context-handoff-capture-5
artifacts/phase53m-alloc-context-handoff-capture-6
artifacts/phase53m-alloc-context-handoff-capture-7
```

Capture 7 remains the most complete stale-context selection trace. The
existing bounded fresh-entry/GC evidence is documented in
[NATIVEAOT_SCHEDULER_THREAD_ATTACH.md](NATIVEAOT_SCHEDULER_THREAD_ATTACH.md)
and [NATIVEAOT_GC_SCHEDULER_THREAD.md](NATIVEAOT_GC_SCHEDULER_THREAD.md).

### Smallest unresolved issue and exact Phase 53O step

The smallest unresolved issue is precise:

> When a fresh scheduler worker has attached through the generated reverse
> P/Invoke path, what guideXOS-owned event is the authoritative equivalent of
> NativeAOT's home-fiber destruction, and how does it invoke or replace
> `RuntimeThreadShutdown` so the worker is removed from ThreadStore and its
> allocation context is fixed before its TLS block is freed?

The first Phase 53O step should be a non-production diagnostic runtime shim or
instrumented payload that records, for one fresh worker, the current
`tls_CurrentThread` address, `TSF` value, ThreadStore membership/list count,
stack low/high, allocation-context address, FLS value, and the exact detach
event. The probe must then verify that `GcEnumAllocContexts` no longer sees
the worker before scheduler TLS/stack reclamation. Only after that evidence
should the project choose Form 1 or Form 2 and draft a production patch.

### Phase 53N final invariants

* The full initialized 0x1000-byte main TLS clone is **not supported**.
* The Phase 53M M3 wrong-context selection remains valid.
* No allocator workaround, `FixAllocContext` change, `SetFree` change,
  collection suppression, worker pinning, or context hiding was introduced.
* No production source was changed.
* Nothing was committed or pushed.

**Phase 53N outcome: E — NativeAOT contract materially narrowed; runtime
detach/unregister and complete green-scheduler GC lifecycle remain unresolved;
defer implementation to Phase 53O.**

## Phase 53O diagnostic attach/detach completion

Phase 53O added a diagnostic-only scheduler lifecycle probe. The production
managed driver-worker path and its full initialized-TLS clone remain
unchanged and available for historical reproduction. The new path is isolated
in:

    src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.c
    src/Gate4Harness/nativeaot_scheduler_thread_lifecycle.h
    tools/Run-NativeAotSchedulerThreadLifecycleFreshBoots.ps1

The build is enabled with the switch
EnableNativeAotSchedulerThreadLifecycle alongside the existing
NativeAotEventWait, managed-callback, scheduler-callback, managed-GC, and
startup switches. The probe uses the generated reverse-P/Invoke export for
attachment. It does not write NativeAOT thread flags or fabricate a
ReversePInvokeFrame.

### Detach mechanism proven

The runtime-created FLS slot and its registered cleanup callback are captured
from the existing platform FLS adapter. After managed return and the managed
GC/root probe, the diagnostic worker invokes that runtime-owned callback with
the current FLS value. This exercises the NativeAOT cleanup chain:

    runtime FLS callback
      -> RuntimeThreadShutdown
      -> ThreadStore::DetachCurrentThread
      -> Thread::Detach / FixAllocContext
      -> Thread::Destroy
      -> callback returns
      -> scheduler FLS slot is cleared
      -> scheduler handle closes and TCB/TLS/stack resources reclaim

The FLS value is the runtime Thread*, which is the scheduler TLS allocation
base plus 0x30. The diagnostic census walks the version-matched
RuntimeThreadLocals::m_pNext link at offset 0x60 from that runtime Thread*;
this is deliberately a bounded forensic layout check, not a new production
runtime API. The runtime transition-frame sentinel is at 0x48, the state
flags at 0x40, and runtime stack bounds at 0xA8/0xB0.

The diagnostic worker rehomes its scheduler low-sentinel page to a separate
nonadjacent page before entering managed code. This preserves the scheduler
integrity check at a boundary NativeAOT may own during stack probing; the
production scheduler allocation and the production managed TLS-clone path are
not changed.

### Fresh-boot evidence

The final gate used the preserved authoritative managed payload:

    size: 730112
    SHA-256: AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012
    EFI size: 538602
    EFI SHA-256: 40BC0724CB9A5A64D991690DCC8F38AC221948657A67A7C61237ED13E002F37E

Three independent QEMU fresh boots passed:

    evidence/phase53o-scheduler-thread-lifecycle-fresh-boots-auth-9/runs/run-1/serial.log
      bytes=521514 sha256=E8E36A6E771F6E2708E32159E0A6E80646315439E1D1A11FAE56AD3D5DF7013F
    evidence/phase53o-scheduler-thread-lifecycle-fresh-boots-auth-9/runs/run-2/serial.log
      bytes=521514 sha256=DAE19C0BF7F34969023EC398474975BA5C441C99C3ECC5094346B4296AEA9C60
    evidence/phase53o-scheduler-thread-lifecycle-fresh-boots-auth-9/runs/run-3/serial.log
      bytes=521514 sha256=A18474DF9AC183BCEC78C5F3E70CED594D9448468480CE49011198FB517C83E4

Representative run-1 facts were:

| Proof item | Cycle 1 | Cycle 2 |
| --- | ---: | ---: |
| Scheduler identity | 5 | 6 |
| Scheduler TLS block | 0x5309000 | 0x52F5000 |
| Runtime Thread* / allocation context | 0x5309030 | 0x52F5030 |
| Runtime stack low/high | 0x530D000 / 0x5311000 | 0x5309000 / 0x530D000 |
| ThreadStore count before/after detach | 3 / 2 | 3 / 2 |
| Runtime state before/after | 1 / 2 | 1 / 2 |
| GC collection delta | 1 | 1 |

Both cycles also emitted the managed callback return, GC allocation,
collection, root-survival, FLS-callback-returned, managed-return,
detach/unregister, scheduler-reclaim, and repeat markers. After each
reclaim, the scheduler TCB was no longer live, the handle lookup was empty,
the runtime TLS/vector/GS/TEB fields were zeroed, the stack VM identity was
zeroed, and the VM-region count returned to its baseline.

The first early capture with the newly rebuilt SDK 10.0.401 payload is
preserved under evidence/phase53o-scheduler-thread-lifecycle-fresh-boots; it
stopped at the known strict-timezone precondition. Additional failed
diagnostic-gate iterations and the non-authoritative 10.0.401 managed rebuild
remain preserved under the corresponding artifacts/phase53o-* directories.
The final passing gate intentionally returned to the existing authoritative
payload because its historical NativeAOT export/RVA identity is required by
the harness.

### Phase 53O principal outcome

**Outcome A — complete diagnostic attach/detach lifecycle proven.**

A scheduler-created worker attached through the supported generated
reverse-P/Invoke mechanism, became a distinct ThreadStore member with fresh
TLS and allocation state, reported the expected stack bounds, executed
managed callback and allocating GC/root work, returned to native execution,
detached through the runtime-owned FLS cleanup path, disappeared from the
ThreadStore census, and was reclaimed by the scheduler afterward on three
fresh boots. This is a diagnostic proof only; it does not authorize replacing
the production TLS-clone worker path in this phase.

## Phase 53P — production managed-driver lifecycle migration

Phase 53P applied the Phase 53O lifecycle to the real
`ManagedSerialDriverSubsystem` worker. The version-matched NativeAOT runtime
model remains v10.0.11 at commit
`79d0c463f1b55624c874a11585f7e47731e8d675`; the repository's historical
artifact metadata continues to identify 10.0.10 where previously documented.

### Old production lifecycle

The production path before this migration was:

    ManagedSerialDriverSubsystem.RunDriverWorker(stage 1)
      -> gate4 phase 9 creates event and suspended scheduler TCB
      -> worker stack, GS area, TLS vector, TLS block, and TEB are allocated
      -> gxos_managed_kernel_driver_worker_configure_nativeaot_tls(
           g_tls_block, 0x1000)
      -> initialized main NativeAOT TLS page is copied byte-for-byte
      -> scheduler resumes the worker
      -> worker_entry directly calls the managed RunDriverWorker export
      -> RunDriverWorker publishes s_driverWorker and dispatches work
      -> stop destroys the worker and scheduler reclaims its backing state

The `configure_nativeaot_tls` operation duplicated the main thread's live
NativeAOT `Thread*`, allocation ownership, transition/runtime fields, and
other thread-owned state. There was no supported NativeAOT attach on worker
entry and no runtime detach before scheduler reclamation. The helper and its
historical evidence remain in the Phase 53 forensic record, but the normal
production worker no longer calls it.

### Production-to-Phase-53O mapping

| Production component | Phase 53O counterpart | Phase 53P action |
| --- | --- | --- |
| scheduler TCB creation | diagnostic suspended TCB | unchanged scheduler creation and activation |
| private stack creation | diagnostic registered stack | reused unchanged stack ownership; runtime receives the same low/high bounds |
| GS/TLS environment | fresh diagnostic GS/vector/block/TEB | reused fresh scheduler-owned environment |
| full TLS clone | fresh runtime state | removed from the production worker |
| direct managed call | generated reverse-P/Invoke callback | replaced by `gxos_nativeaot_callback_invoke` through the shared lifecycle |
| real worker stage 1 | diagnostic managed callback | invokes `GxManagedKernelRunDriverWorker(1)` |
| real dispatch stages | diagnostic managed callback | invokes the real stage 2 callback on the scheduler worker |
| managed return | Phase 53O return | uses the same callback-return boundary |
| worker teardown | Phase 53O FLS cleanup/detach | runtime cleanup callback, ThreadStore census, then scheduler reclaim |

The shared implementation is in
`nativeaot_scheduler_thread_lifecycle.c/.h`. Its reusable operations are
`gxos_nativeaot_scheduler_worker_prepare`,
`gxos_nativeaot_scheduler_worker_attach`,
`gxos_nativeaot_scheduler_worker_invoke`, and
`gxos_nativeaot_scheduler_worker_detach`. The Phase 53O diagnostic worker
uses those same operations; no second ThreadStore or managed-side runtime
thread implementation was introduced.

### New production lifecycle

The repaired path is:

    scheduler TCB
      -> fresh scheduler-owned vector/block/GS/TEB/stack
      -> PE TLS block installed at the actual TLS index
      -> generated reverse-P/Invoke enters NativeAOT
      -> NativeAOT creates and attaches a unique Thread*
      -> ThreadStore membership and fresh allocation context
      -> real managed driver worker stage 1
      -> s_driverWorker publication and real dispatch/GC work
      -> managed callback returns
      -> runtime FLS cleanup callback detaches/unregisters Thread*
      -> ThreadStore absence verified
      -> scheduler reclaims TLS, stack, and TCB

Phase 53P does not change the driver's purpose, serial protocol, dispatch
policy, publication semantics, or managed worker layout. The only production
execution change is the NativeAOT thread/runtime ownership boundary.

### Production identity and allocation proof

Representative clean production run-1 values from
`evidence/phase53p-production-worker-fresh-boots-v3/runs/run-1/serial.log`
were:

| Proof item | Main | Production worker |
| --- | ---: | ---: |
| scheduler TCB | boot TCB | `0x1A99F0` |
| stack low/high | main-owned | `0x4CFF000 / 0x4D03000` |
| GS base | main-owned | `0x4CFE000` |
| TLS vector/block | main-owned | `0x4CFD000 / 0x4CFC000` |
| NativeAOT Thread* | `0x4EAA030` | `0x4CFC030` |
| ThreadStore census before/after attach | `2` | `2 / 3` |
| ThreadStore census after detach/reclaim | `2` | `2 / 2` |
| allocation pointer before first callback | main live state | `0` (fresh worker) |
| first worker allocation | not claimed by main | `0x4000050000FE8`, size `0x40` |
| worker allocation pointer after | not applicable | `0x4000050001028` |
| main allocation pointer at same point | `0x4000050000E50` | independently unchanged |

Thus `main Thread* != worker Thread*`, the worker TLS block is distinct from
the main TLS block, and the worker begins without a copied live bump-pointer
range. The bounded ThreadStore allocation-context scan reported
`PHASE53_STALE_CONTEXT_REGRESSION_PASS=1`; it found no second GC-visible
context whose free boundary claimed the already allocated worker object.
The worker published `s_driverWorker` successfully.

The production worker also emitted the attach, ThreadStore, stack-bounds,
allocation-context, GC-root, `GcEnumAllocContexts`, `FixAllocContext`,
`SetFree`, relocation, post-GC dispatch, return, detach, and reclaim pass
markers. These are bounded proof markers around the actual managed
allocation/GC path; no allocator, GC, `FixAllocContext`, or `SetFree`
implementation was changed.

### Ownership table

| Resource | Created by | Owned while active | Released by |
| --- | --- | --- | --- |
| scheduler TCB | guideXOS scheduler | scheduler | scheduler collection after worker termination |
| native stack | guideXOS scheduler | scheduler; registered with NativeAOT as bounds | scheduler collection after detach |
| GS state | scheduler worker preparation | scheduler while active; installed on worker switch | scheduler collection |
| NativeAOT TLS storage | scheduler worker preparation as fresh zeroed storage | scheduler storage plus NativeAOT runtime fields after attach | scheduler after runtime detach |
| NativeAOT `Thread*` | NativeAOT reverse-P/Invoke attach | NativeAOT ThreadStore and worker FLS slot | runtime FLS cleanup callback |
| ThreadStore membership | NativeAOT attach | NativeAOT runtime | `ThreadStore::DetachCurrentThread` through cleanup callback |
| allocation context | NativeAOT attach/runtime | the unique worker `Thread*` | runtime detach/fix-up |
| managed worker object | real managed stage-1 callback | `ManagedSerialDriverSubsystem.s_driverWorker` and worker managed roots | real managed stop/destroy sequence |

### Historical invariant and canary audit

The historical bad state was:

    main context at X/e50
      -> full clone copied into worker
      -> worker allocates object X and advances to X+size
      -> original main-origin context remains at X
      -> GC later selects the stale context and SetFree consumes worker memory

The repaired production path cannot produce that state through TLS cloning:
the worker starts with fresh runtime storage, NativeAOT creates a distinct
`Thread*`, and the worker's allocation context is independently owned. The
first production managed allocation and the bounded overlap scan passed on
all clean worker boots. No `FixAllocContext` or `SetFree` workaround, GC
suppression, pinning, allocation serialization, or object-layout change was
introduced.

The Phase 53O low-sentinel canary rehome remains diagnostic-only. Production
worker allocation and production stack placement do not depend on that
rehome; the production gate used the normal worker stack allocation.

### Validation

Focused host validation passed, including:

    MANAGED_KERNEL_PHASE53_HOST_TESTS_PASS cases=540
    MANAGED_KERNEL_DRIVER_WORKER_HOST_TESTS=PASSED
    scheduler model checks=256
    NativeAOT GC probe contract checks=8
    NativeAOT interrupt, stack-VM, durability, memory-accounting, and
    allocator/GC contract suites passed

The Phase 53O diagnostic regression remained green on three fresh boots. Its
authoritative payload remains 730112 bytes with SHA-256
`AE19A4C414A7F642B89B637D131A86E206300323914858E882E1293636A5C012`, and
its authoritative EFI remains 538602 bytes with SHA-256
`40BC0724CB9A5A64D991690DCC8F38AC221948657A67A7C61237ED13E002F37E`.

The production worker-only regression passed 3/3 fresh boots and every boot
emitted all production lifecycle and stale-context markers. A longer
25-boot worker stress run passed runs 1 through 11. Run 12 then exposed a
separate, concrete production-scheduling fault after the worker's fresh
identity and first managed allocation had passed:

    X64 Exception Type - 0D (#GP)
    RIP 0x4FF3B80 (payload RVA 0x147B80)
    RCX 0x48C3C3C920C48378 (invalid Thread-shaped receiver)
    worker RSP 0x4D028C8

The fault occurred during the real worker's longer dispatch/runtime-activity
sequence, before the worker GC/return/detach markers. It is not the
Phase-53M stale allocation-context condition: no duplicate live bump-pointer
range or `SetFree` ownership violation was observed. Because this is a
production-only scheduler/NativeAOT compatibility failure under extended
stress, the migration is not classified as production-ready.

The original broader Phase 53 replay was also executed with the repaired
worker. The production worker completed its attach, managed publication,
managed allocation, actual GC/root path, return, detach, and reclaim before
the page test. The HTML document and stylesheet resource bodies were reached
in the clean replay, but the host/guest fixture stalled before the content
image TCP request; an earlier preserved replay reached the image response and
reported image transport failure (`0x13`). No Phase-53M stale-context marker
or worker reclamation corruption appeared in those replays. The remaining
failure is therefore kept as a separate production scheduler/page-fixture
boundary, not attributed to the repaired TLS ownership defect.

### Phase 53P principal outcome

**Outcome E — production migration exposes broader scheduler/NativeAOT
compatibility under extended real-worker scheduling.**

Phase 53O generalizes to the real worker for clean attach, ThreadStore
registration, independent allocation, managed GC/root participation, return,
detach, and scheduler reclaim. However, the required extended production
stress was not zero-fault: a later real-worker dispatch entered a
NativeAOT Thread-state routine with an invalid receiver. The exact failure is
preserved in
`evidence/phase53p-production-worker-stress-25/runs/run-12/serial.log`.
The complete diagnostic Phase 53O proof remains preserved and green, and the
production migration must not be treated as suitable for retention until this
broader compatibility failure and the remaining page replay boundary are
resolved.

## Phase 53Q — repeated-production-worker NativeAOT lifetime/context fault

Phase 53Q investigated the separate fault exposed by the Phase 53P production
worker stress. The Phase 53P ownership repair was preserved: production uses
fresh scheduler-owned runtime storage, actual PE TLS installation, generated
reverse-P/Invoke attachment, a unique NativeAOT `Thread*`, managed execution,
runtime detach, ThreadStore removal, and only then scheduler reclamation. The
full initialized NativeAOT TLS clone was not restored.

### Artifact identity and reproduction

All successful Phase 53Q captures used the exact Phase 53P production artifacts:

    EFI:     681287 bytes
             SHA-256 D8DC2BFC4D58DFB27C05A82FFBE145E22AF7BC699599B8477BC3500C60BFD69
    payload: 4790784 bytes
             SHA-256 24ECBA6EBDADD720351BD0AE768AB177F366D5CCECFA318881376128351B6D09
    PDB:     11292672 bytes
             SHA-256 BB12D9271C3D4CAC80C2BB3BCF25C3ED839BE4561126F68B818F81A975F3F7ED

The runtime remained NativeAOT v10.0.11 at commit
`79d0c463f1b55624c874a11585f7e47731e8d675`; it was not upgraded. The
authoritative Phase 53P 25-cycle stress passed cycles 1–11 and faulted on
cycle 12. An independent no-network reproduction passed cycles 1–9 and
faulted on cycle 10. The boot number is not stable, but the fault RVA and
invalid value were stable. A bounded clean diagnostic run reached managed
return, detach, and reclaim without a target fault. The timer-audit attempt
was a tooling failure because the dynamic firmware IDT entry was not initialized
when GDB installed that optional breakpoint; it is not runtime evidence.

The available cycle ledger is:

| cycle | authoritative stress | independent no-network reproduction |
|---:|---|---|
| 9 | pass | pass |
| 10 | pass | target fault |
| 11 | pass | not reached |
| 12 | target fault | not reached |

The cycle-12 serial evidence is
`evidence/phase53p-production-worker-stress-25/runs/run-12/serial.log`.

### Exact fault identity

For payload image base `0x4EAC000`, RVA `0x147B80` is loaded at
`0x4FF3B80`. The matching PDB resolves the containing function as:

    Thread::SetDoNotTriggerGc
    function range: RVA [0x147B80, 0x147B90)

The faulting bytes are:

    f0 83 49 40 10    lock orl $0x10,0x40(%rcx)
    c3                ret

This is NativeAOT runtime code. It is not generated managed code, the guideXOS
shim, or the scheduler. Under the Windows x64 ABI, `RCX` is the member-function
`this` receiver. The instruction expects a valid NativeAOT `Thread*` and sets
the `TSF_DoNotTriggerGc` bit in `Thread::m_ThreadStateFlags` at offset `0x40`;
it does not accept an arbitrary callback argument.

The direct caller in this capture is `InvokeGcCallouts`, whose function starts
at RVA `0x1536C0` and calls `Thread::SetDoNotTriggerGc` at RVA `0x1536F8`
(`+0x38`). The caller's dataflow is:

    GS:0x58
      -> TLS vector
      -> vector[runtime TLS index]
      -> TLS block
      -> TLS block + 0x30
      -> RCX / RDI
      -> Thread::SetDoNotTriggerGc

The captured `RBX=1`, `RDX=2`, `RSI=2`, and `R8=0` match the
`GcDone -> InvokeGcCallouts(GCRC_EndCollection)` path, not the neighboring
ref-counted-handle callback path. Version-matched NativeAOT sources document
the corresponding runtime assumptions in [`thread.cpp`](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/thread.cpp),
[`RestrictedCallouts.cpp`](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/RestrictedCallouts.cpp),
and [`gcenv.ee.cpp`](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/gcenv.ee.cpp).

### Invalid value and proven immediate producer

The invalid receiver was:

    RCX = 0x48C3C3C920C48378

The corresponding source-slot value is `RCX-0x30`:

    0x48C3C3C920C48348

As little-endian bytes, the source is:

    48 83 c4 20 c9 c3 c3 48

The complete eight-byte sequence was not found in the matching payload, EFI,
or PDB. Its leading bytes decode as `add rsp,0x20; leave; ret; ret`, and the
capture proves why those bytes were consumed as data: the first bad worker
TLS-vector watch saw `GS+0x58` overwritten with `0x7E6E2B9`. That address is
the return address pushed by the dynamic-code instruction at `0x7E6E2B6`,
`call *0x8(%rax)`. The later NativeAOT load therefore treated that firmware
return address as the TLS-vector pointer and produced the invalid `Thread*`.

The first bad worker-TLS-vector capture was:

    write site RIP = 0x6B5025E
    RSP            = 0x4CFE058 = worker GS + 0x58
    GS base        = 0x4CFE000
    new GS+0x58    = 0x7E6E2B9

The first bad worker-TEB capture was:

    write site RIP = 0x7E6E26B
    RSP            = 0x4CFE030 = worker GS + 0x30

The surrounding dynamic code is an OVMF/UEFI event/lock dispatch path: its
`EVENT_SIGNATURE` check, event-list walk, and indirect notification call match
the EDK2 event core in [`Event.c`](https://github.com/tianocore/edk2/blob/master/MdeModulePkg/Core/Dxe/Event/Event.c).
This identifies the immediate clobbering execution as outside the payload and
outside NativeAOT. It does not yet prove what earlier transition placed RSP in
the GS page. The exact earlier pivot writer is therefore unresolved.

The authoritative target fault state was:

    RIP 0x4FF3B80       RSP 0x4D028C8       RBP 0x4EAC000
    RAX 0x7E6E2B9       RBX 1                RCX 0x48C3C3C920C48378
    RDX 2               RSI 2                RDI 0x48C3C3C920C48378
    R8 0                 R9 0x54FD49          R10 1  R11 1  R12 1  R13 0  R14 1  R15 0
    GS base 0x4CFE000    FS base 0

At that point the intact worker vector was still `0x4CFD000`, with slot zero
pointing to `0x4CFC000`; the corrupted value was in the installed GS vector
slot, not in the fresh worker block observed by the direct vector dump.

### Lifecycle and ownership ledger

The worker identity in the Phase 53P/53Q target was:

    scheduler TCB       0x1A99F0
    worker stack        0x4CFF000–0x4D03000
    worker GS           0x4CFE000
    worker TLS vector   0x4CFD000
    worker TLS block    0x4CFC000
    worker Thread*      0x4CFC030
    main Thread*        0x4EAA030

The successful attach census was ThreadStore `2 -> 3`; Phase 53P's successful
return/detach census was `3 -> 2`, followed by scheduler reclaim at baseline
`2`. The faulting cycle occurs before managed return, detach, and reclaim, so
the failing target does not establish a use-after-reclaim of its own worker
resources. Clean captures show fresh worker resources can complete the full
return/detach/reclaim path. Across the available failing captures, the fixed
QEMU layout reuses the same address ranges; however, cumulative reuse across
completed worker lifetimes, and all stale references after reclaim, were not
proven to be the cause of the fault.

The required lifetime rule is:

    while a worker is active, GS+0x58 must name its installed TLS vector;
    the runtime TLS slot must name that worker's TLS block; TLS block+0x30
    must remain the live NativeAOT Thread* until GcDone and all callouts using
    the current thread have completed. RSP must remain in the live worker
    stack or an explicitly valid transition stack.

The observed violation is the first part of that rule: ordinary firmware
event-path stack activity ran with RSP in the worker GS page and overwrote
`GS+0x30`/`GS+0x58`. The missing evidence is the owner of the earlier stack
pivot. No post-detach stale `Thread*`, TLS block, GS address, stack address,
transition frame, or scheduler TCB was proven in the target fault. ThreadStore
baseline alone is therefore not treated as sufficient proof of complete FLS/PAL
teardown.

### Reverse-P/Invoke, FLS/PAL, GS/TLS, scheduler, and GC audit

The attach/return audit remains consistent with the Phase 53P repair. The
successful run entered through generated reverse-P/Invoke attachment, used a
distinct NativeAOT `Thread*`, returned through the managed path, and detached
before reclaim. The version-matched NativeAOT reverse-transition contract is
implemented in [`thread.inl`](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/thread.inl)
and [`UniversalTransition.asm`](https://raw.githubusercontent.com/dotnet/runtime/v10.0.11/src/coreclr/nativeaot/Runtime/amd64/UniversalTransition.asm).
Those sources establish that normal reverse-P/Invoke return restores the saved
transition frame; they do not explain the earlier RSP pivot seen here.

GS/TLS captures showed the expected worker activation values before the fault:
worker GS `0x4CFE000`, vector `0x4CFD000`, block `0x4CFC000`, and runtime
Thread `0x4CFC030`. A clean capture restored main state and completed detach.
The fault is during worker managed/runtime activity, specifically the GC end
callout, not during reverse return, FLS cleanup, scheduler reclaim, or main
thread execution. No causal GC allocation-context overlap was observed; the
Phase 53P worker allocation context remained independently owned and the
original `FixAllocContext`/`SetFree` path was unchanged.

The scheduler context audit observed a production switch with:

    old context 0x7E645D8
    new context 0x1A9A30
    new RSP     0x4D02FE8
    new RIP     0x167B40
    new GS      0x4CFE000

The scheduler assembly saves/restores the nonvolatile GPRs, stack, RIP,
flags, floating-point state, XMM6–15, and GS; it intentionally does not save
volatile GPRs under the ABI. Existing model, stack-VM, durability, interrupt,
and memory-accounting tests remained green. This rules out neither a later
interrupt/firmware boundary error nor the unresolved pivot, so a scheduler
context-save/restore defect is not declared proven and the scheduler was not
rewritten.

### Classification and outcome

The best-supported current classification is an unresolved boundary corruption
candidate (Q5/Q7 remain possible); no Q1–Q8 mechanism is accepted as proven
because the first RSP-pivot writer has not been identified. In particular, Q1,
Q2, Q3, and Q4 are not established by the target fault because it precedes
detach/reclaim; Q6 is not established because reverse-P/Invoke succeeds in
isolation and in the clean lifecycle; and Q5 versus Q7 cannot be separated
without the earlier pivot producer.

**Principal outcome: Outcome E — invalid-pointer provenance narrowed but
producer unresolved.**

The complete immediate chain is proven:

    valid worker/runtime state
      -> RSP enters the worker GS page in an earlier, unresolved transition
      -> OVMF/UEFI event-path calls use that page as a stack
      -> return address 0x7E6E2B9 overwrites GS+0x58
      -> GcDone/InvokeGcCallouts loads it as the TLS vector
      -> TLS-block derivation places 0x48C3C3C920C48378 in RCX
      -> Thread::SetDoNotTriggerGc at RVA 0x147B80 faults

The decisive first transition into the GS page remains the smallest next
question. No production repair is justified. The production lifecycle is not
retainable or acceptance-ready for extended repeated-worker scheduling, but
Phase 53P's ownership repair remains valid and must be retained.

No allocator or GC workaround was added. No changes were made to
`FixAllocContext` or `SetFree`. Diagnostic tooling is preserved in
`tools/Run-Phase53QWorkerLifetimeFaultCapture.ps1`; separate captures are
preserved under the `evidence/phase53q-*` directories, including the clean
pass, target faults, and the excluded tooling-failure run. Historical Phase
53P forensic evidence was not overwritten or deleted. No EFI or managed
payload was rebuilt in Phase 53Q, and nothing was committed or pushed.

## Phase 53R — First Bad-RSP Producer (2026-09-14)

### Scope, preflight, and artifact provenance

Phase 53R was a read-only forensic investigation of the first observed RSP
pivot into the worker GS page. The requested starting commit `842d0dd` was not
the live checkout: the repository was on branch
`nativeaot-managed-kernel-integration`, at
`30b6e79d6536e99de90e32cbd5851cc41efe81fa`, one commit beyond `842d0dd`, with
origin at the same commit. The mandatory preflight was clean
(`git status --porcelain` empty) before the diagnostic script was added. No
reset, restore, clean, stash, branch switch, rebase, amend, push, merge, or
production rebuild was performed.

Every target run used the same Phase 53P production gate and exact hashes:

    EFI  artifacts/phase53p-production-gate-final-v8/ESP/EFI/BOOT/BOOTX64.EFI
         size 681287
         SHA256 D8DC2BFC4D58DFB27C05A82FFBE145E22AF7BC699599B8477BC3500C60BFD69D
    DLL  artifacts/phase53p-production-gate-final-v8/ESP/GXOS/gxos-managed-kernel.dll
         size 4790784
         SHA256 24ECBA6EBDADD720351BD0AE768AB177F366D5CCECFA318881376128351B6D09
    PDB  artifacts/phase53p-managed-build-final-v8/publish/gxos-managed-kernel.pdb
         size 11292672
         SHA256 BB12D9271C3D4CAC80C2BB3BCF25C3ED839BE4561126F68B818F81A975F3F7ED

The copied QEMU OVMF code image was 3653632 bytes with SHA256
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`; the
copied variables image was 540672 bytes with SHA256
`5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.
The NativeAOT source correlation remains version `v10.0.11`, commit
`79d0c463f1b55624c874a11585f7e47731e8d675`.

The PDB maps `Thread::SetDoNotTriggerGc` to RVA `[0x147B80, 0x147B90)`;
with image base `0x4EAC000`, the downstream fault address is `0x4FF3B80`.
The faulting instruction is the expected `lock orl $0x10,0x40(%rcx)`.
The invalid RCX and its source slot remain the previously recorded
`0x48C3C3C920C48378` and `0x48C3C3C920C48348`, respectively.

### Diagnostic method and reproductions

The only new source-controlled diagnostic is
[`Run-Phase53RFirstBadRspCapture.ps1`](../tools/Run-Phase53RFirstBadRspCapture.ps1).
It validates the artifact sizes and hashes, copies OVMF into a fresh run
directory, starts the unchanged Phase 53P gate under QEMU/TCG, and arms
hardware breakpoints only after the worker scheduler start. It records the
scheduler context restore, the scheduler's `mov rsp` consumer, the event
callsite, and the narrowed OVMF callback path. It makes no production-source,
EFI, managed-payload, or NativeAOT changes.

The evidence directories `evidence/phase53r-first-bad-rsp-20260914-01` through
`-19` are retained. Early runs that stalled, timed out, or produced a
diagnostic timing perturbation are not target evidence. The useful target
captures are:

    R13, R14, R15  repeated target faults; first observed bad RSP at
                   OVMF event callsite 0x7E6E6BC
    R17            bad RSP at 0x6B0D6B9, before the indirect callback executes
    R18            bad RSP at 0x6B0D680, after the preceding push/mov prologue
    R19            bad RSP at true entry 0x6B0D67C, before its push %rbp

R16's extra exact pre-call probe changed the stress timing and ended at the
guest keyboard timeout; it is retained as a timing-perturbation record, not
counted as a target reproduction. R19 is the strongest boundary capture
because it contains both a preceding valid event-call observation and the
true-entry bad-RSP observation in one run.

### Last-good and first-bad boundary

Observed and repeatable last-good state:

    scheduler context consumer 0x16722E
    new context               0x1A9A30
    restored RSP              0x4D02FE8
    restored RIP              0x167B40
    restored GS               0x4CFE000
    worker stack              0x4CFF000–0x4D03000

R19 then observed a valid worker-stack event call at `0x7E6E6BC`, with
`RSP=0x4D02470`, followed in the same execution trace by the first narrow
bad boundary:

    PHASE53R_FIRST_BAD_RSP OVMF_CALLER_ENTRY_6B0D67C
    RIP 0x6B0D67C
    RSP 0x4CFE228
    GS  0x4CFE000
    [RSP] 0x6B61123

The OVMF bytes at this boundary are:

    0x6B0D67C: push %rbp
    0x6B0D67D: mov  %rsp,%rbp
    0x6B0D680: push %r12
    ...
    0x6B0D6B9: call *0x18(%rax)
    0x6B0D6BC: mov  %rax,%rbx

This proves the bad RSP predates the `push %rbp` at `0x6B0D67C`, predates
the indirect callback at `0x6B0D6B9`, and is not created by the callback's
return. The saved return address `0x6B61123` identifies the next upstream
OVMF continuation, but no write watchpoint or pre-call capture proves which
instruction first wrote or loaded the bad RSP. The exact producer therefore
remains unknown.

### NativeAOT and scheduler correlation

The first-bad RIPs (`0x6B0D67C`, `0x6B0D680`, `0x6B0D6B9`, and the repeated
`0x7E6E6BC`) are outside the NativeAOT managed payload/PDB region. The
NativeAOT `SetDoNotTriggerGc` fault is downstream: the OVMF event path runs
with RSP in the GS page, a return address overwrites the worker TLS-vector
slot at `GS+0x58`, and the GC end-callout later derives the invalid RCX from
that corrupted vector. This downstream chain is proven by the captures; the
earlier RSP writer is not.

The scheduler source and live captures agree: it restores the recorded worker
RSP/RIP/GS and then consumes RSP at `0x16722E`. No scheduler save/restore
defect was observed. Likewise, the target fault precedes managed return,
detach, or reclaim, so stale post-detach `Thread*` or allocator teardown is
not proven as the mechanism.

### Classification and outcome

The narrowed interval is:

    valid worker context restore at 0x16722E
      -> valid OVMF event call at 0x7E6E6BC in R19
      -> unresolved transition/caller path
      -> OVMF entry 0x6B0D67C with RSP already in GS page 0x4CFE000–0x4CFEFFF
      -> event-path stack use and downstream TLS-vector corruption
      -> NativeAOT SetDoNotTriggerGc fault at 0x4FF3B80

**Outcome E — the invalid-pointer provenance and first bad-RSP interval are
narrowed, but the exact producer is unresolved.** No production repair is
justified by this evidence. The Phase 53P ownership repair, allocator path,
and GC workaround were left unchanged; nothing was committed or pushed.

## Phase 53S — Pre-OVMF Stack-Pivot Transition

Phase 53S remained forensic. It did not change production C, assembly, EFI,
managed-payload, scheduler, GS setup, or GC behavior. The purpose was to treat
`0x6B0D67C` as a landing point and capture the final transfer into it before
its first instruction executed.

### Preflight and inherited boundary

The live preflight found:

    repository       D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration
    branch           nativeaot-managed-kernel-integration
    starting HEAD    1930ec1d4ddfe1f6d5d7c3d27db5baacbd623c41  Phase 53R
    upstream         origin/nativeaot-managed-kernel-integration
    divergence       0 ahead / 0 behind
    tracked changes  none
    untracked files  none (non-ignored)

The prompt's expected `842d0dd` was not the live HEAD; the repository already
contained the committed Phase 53R work at `1930ec1`. Existing ignored Phase
53Q/R evidence was inventoried and preserved. An unrelated QEMU process was
running during preflight and was not terminated.

The decisive retained R19 evidence was read first:

    evidence/phase53r-first-bad-rsp-20260914-19/run-1/capture-manifest.txt
    evidence/phase53r-first-bad-rsp-20260914-19/run-1/gdb.stdout.log
    evidence/phase53r-first-bad-rsp-20260914-19/run-1/gdb-commands.txt

The matching Phase 53P artifacts were unchanged across the captures:

    EFI  artifacts/phase53p-production-gate-final-v8/ESP/EFI/BOOT/BOOTX64.EFI
         size 681287
         SHA256 D8DC2BFC4D58DFB27C05A82FFBE145E22AF7BC699599B8477BC3500C60BFD69D
    DLL  artifacts/phase53p-production-gate-final-v8/ESP/GXOS/gxos-managed-kernel.dll
         size 4790784
         SHA256 24ECBA6EBDADD720351BD0AE768AB177F366D5CCECFA318881376128351B6D09
    PDB  artifacts/phase53p-managed-build-final-v8/publish/gxos-managed-kernel.pdb
         size 11292672
         SHA256 BB12D9271C3D4CAC80C2BB3BCF25C3ED839BE4561126F68B818F81A975F3F7ED

The inherited last-known-good scheduler state was observed at the scheduler
RSP consumer `0x16722E`:

    context       0x1A9A30
    RSP           0x4D02FE8
    RIP           0x167B40
    GS base       0x4CFE000
    worker stack  0x4CFF000–0x4D03000

### Immediate predecessor and exact transfer

The final transfer into `0x6B0D67C` is proven to be an indirect tail jump:

    0x6B57E3C: 4A 8B 04 F7       mov    (%rdi,%r14,8),%rax
               RDI=0x6B65B00, R14=0x20
               effective address 0x6B65C00
               [0x6B65C00] = 0x6B0D67C

    0x6B57E5E: FF E0              jmp    *%rax

The immediate pre-transfer snapshot in the matching bad captures was:

    RIP       0x6B57E5E
    RAX       0x6B0D67C
    RSP       0x4CFE228
    RBP       0x4CFE5C0 (representative matching capture)
    GS base   0x4CFE000

At OVMF entry, before `push %rbp` executed:

    RIP       0x6B0D67C
    RSP       0x4CFE228
    RBP       0x4CFE5C0 (representative matching capture)
    GS base   0x4CFE000
    [RSP]     0x6B61123

The jump itself does not change RSP. The target-entry RSP equals the
pre-jump RSP in every successful final-transfer capture. The apparent
`pop %rsp` in some raw GDB windows was a misaligned decode of the second byte
of `41 5C` at `0x6B57E55`; the aligned static decode is `pop %r12` at
`0x6B57E55`, followed by `pop %r13` at `0x6B57E57`, and no `pop %rsp`.

The upstream OVMF handler call is also ordinary:

    0x6B6111E: E8 B7 5C FF FF    call 0x6B56DDA
    RSP before call               0x4CFE230
    return address pushed         0x6B61123 at 0x4CFE228

The thunk at `0x6B56DDA` restores its `0x188`-byte local area and saved
registers before the final jump. Therefore the ordinary call is a consumer of
an already GS-page-derived stack, not the pivot producer. Its `RSP` arithmetic
does, however, exactly explain the final `0x4CFE228` landing value:

    0x4CFE230 - 8 = 0x4CFE228

### Executed backward walk

The following is the narrowest machine-level chain captured in the Phase 53S
runs. The first four bad entries are ordinary firmware function entries; none
loads RSP from GS or a saved context in the captured instructions.

    valid  0x16722E   scheduler consumes context RSP 0x4D02FE8
    valid  0x7E6E6BC  OVMF event call, RSP 0x4D021F0
                       call 0x7E6E25E
    bad    0x501A800  function entry, RSP 0x4CFE7B8
                       first instruction: sub $0x28,%rsp
                       return address: 0x501C2B3
    bad    0x501A5B0  function entry, RSP 0x4CFE788
                       first operations are stack stores, then push %r14;
                       sub $0x20,%rsp
    bad    0x5004140  wrapper entry, RSP 0x4CFE758
                       sub $0x28,%rsp; call *0x5041100
                       dynamic return site 0x5004150 identifies 0x111870
    bad    0x111870   function entry, RSP 0x4CFE728
                       push saves; and $-16,%rsp; sub $0xD0,%rsp
    bad    0x105BC0   serial function entry, RSP 0x4CFE608
                       first instruction: push %rbx
                       return address: 0x111A6E
                       callsite: 0x111A69: call 0x105BC0
    bad    0x105C26   serial polling instruction, RSP 0x4CFE600
    bad    0x6B61004  OVMF vector-0x20 common entry, RSP 0x4CFE5C8
                       saved interrupted RSP: 0x4CFE600
    bad    0x6B6111E  direct call to thunk, RSP before call 0x4CFE230
    bad    0x6B57E3C  target-table load, RSP 0x4CFE060
                       [0x6B65C00] = 0x6B0D67C
    bad    0x6B57E5E  jmp *%rax, RSP 0x4CFE228
    bad    0x6B0D67C  OVMF entry, RSP 0x4CFE228

The `0x501A800` through `0x105BC0` edges were captured dynamically in the
same failing runs. Their relevant static calls are:

    0x501A86F: call 0x501A5B0
    0x501A634: call 0x5004140
    0x500414A: call *0x5041100       -> observed entry 0x111870
    0x111A69:  call 0x105BC0

The timer interrupt did not create the bad firmware stack. At common entry,
the hardware/firmware frame already contained interrupted RIP `0x105C28`
(the probe caught the serial polling site at `0x105C26`) and interrupted RSP
`0x4CFE600`. The common handler then used ordinary pushes and local frame
operations before reaching `0x6B6111E`.

### GS-page analysis

The numerical relationship is real for the R19/S1/S5/S7/S11/S12/S13/S15/S16/
S18/S19-style landing:

    GS base       0x4CFE000
    entry RSP     0x4CFE228
    entry RSP-GS  0x228

It is not invariant across all timing variants. Among 21 successful target
captures, all 21 had RSP inside the page rooted at `0x4CFE000`; the exact
`GS+0x228` address occurred in 14. Other successful landings were
`GS+0x178`, `GS+0x1F8`, `GS+0x348`, `GS+0x3A8`, or `GS+0x3B8`. Thus page
containment is reproducible, while the exact `+0x228` offset depends on stack
depth/timing.

The loader source identifies `0x4CFE000` as the allocated, zeroed NativeAOT
GS area. In `src/Gate4Harness/gate4_loader.c`, the established fields are:

    GS+0x30  -> TEB allocation
    GS+0x58  -> TLS vector allocation

The rest of the page is zeroed initially. The scheduler TCB separately stores
per-thread `gs_base`, `teb_base`, and `tls_vector_base`; the scheduler context
stores GS at its defined `+0x60` field. No source definition assigns a GS
field at `+0x228`.

At the matching target snapshot, the relevant page contents included:

    GS+0x1E8  0x0000000000180047  0x0000000000004000
    GS+0x1F8  0x0000000004CFE260  0x0000000000243E50
    GS+0x208  0x0000000000000001  0x0000000000000000
    GS+0x218  0x0000000004CFE6A0  0x0000000004CFE5C0
    GS+0x228  0x0000000006B61123  0x0000000000000000

The value at `GS+0x228` is the ordinary return address left by the
`0x6B6111E` call. The address `GS+0x228` is therefore an active stack
location at that moment, not an identified GS structure field. No valid
semantic source for the RSP value itself was established.

### ABI, ownership, GS behavior, and saved context

The observed `0x20` interrupt path is firmware/OVMF-owned. It remained at the
same privilege level (`CS=0x38`, `SS=0x30`) and no guideXOS IST stack switch
was observed. The frame contains the already-bad interrupted RSP, so this
boundary is an interrupt consumer of the bad stack. The subsequent firmware
dispatcher, thunk, and callback retain firmware control of RSP; the final
tail jump does not establish a new stack.

The guideXOS scheduler context restore is not implicated by the captured
values. Context `0x1A9A30` held the valid RSP/RIP/GS triple above, and
`scheduler_context.S` restores GS, then executes the recorded RSP load and
indirect jump. No bad saved scheduler RSP was observed. The OVMF interrupt
frame is a saved copy of the already-bad current RSP, not the identified
writer of it.

GS base remained `0x4CFE000` at the scheduler boundary, valid event call,
serial caller/function entries, interrupt common/frame/handler entries,
target-table load, final jump, and OVMF entry. No `swapgs`, intentional GS
swap, or GS-base transition correlated with the first bad capture.

No unique saved-RSP slot was identified, so no broad or speculative hardware
watchpoint was installed. The bounded breakpoint strategy proved the final
target operand and transfer, then walked backward through the actually
executed firmware path.

### Reproduction matrix

There were 21 successful target-boundary captures in retained
`evidence/phase53s-pre-ovmf-stack-pivot-20260914-01` through `-26` runs:

    runs  01,02,05,06,07,08,09,11,12,13,15,16,18,19,20,21,22,23,24,25,26
    entry RSP values  GS+0x228 (14), GS+0x178, GS+0x1F8 (2),
                     GS+0x348 (2), GS+0x3A8, GS+0x3B8
    final transfer    0x6B57E5E: jmp *%rax, target 0x6B0D67C in all 21
    GS-page relation  21/21 inside 0x4CFE000–0x4CFEFFF

Runs `03`, `04`, `10`, `14`, and `17` are retained non-target records caused
by debugger timing or command-file setup; they were not counted as causal
reproductions. Run `17` specifically stopped on an incorrect GDB breakpoint
number before capture and was retained rather than overwritten.

### Classification and outcome

**Outcome E — producer still unresolved, with the interval narrowed further.**

The exact final transition is proven:

    valid scheduler restore
      -> valid OVMF event-call observation
      -> unresolved transition before OVMF function 0x501A800
      -> 0x501A800 / 0x501A5B0 / 0x5004140 / 0x111870
         ordinary firmware stack consumers, already GS-page-based
      -> serial call 0x111A69 -> serial entry 0x105BC0
      -> timer IRQ saves the already-bad RSP 0x4CFE600
      -> handler call 0x6B6111E with RSP 0x4CFE230
      -> call pushes 0x6B61123 at 0x4CFE228
      -> target-table load and 0x6B57E5E jmp *%rax
      -> OVMF entry 0x6B0D67C with RSP 0x4CFE228

The smallest remaining causal boundary is before the captured entry of OVMF
function `0x501A800`, which already had `RSP=0x4CFE7B8` and return address
`0x501C2B3`. The exact instruction or architectural transition that first
changes a legitimate stack into that GS-page value remains unknown. No
source operand or saved-context writer for the first bad RSP was proven.

Phase 53T should therefore test the boundary before `0x501A800`—including
the caller/return or firmware context handoff that reaches it—using a narrow
entry/return capture or a specifically identified saved-RSP watchpoint. It
should not treat `0x6B57E5E`, the target-table load, the ordinary call at
`0x6B6111E`, the timer interrupt frame, or OVMF's `push %rbp` as the pivot
producer.

Phase 53S changes are limited to the diagnostic script
`tools/Run-Phase53SPreOvmfStackPivotCapture.ps1`, retained raw evidence, and
this documentation section. No production source changes were made. HEAD
remains `1930ec1d4ddfe1f6d5d7c3d27db5baacbd623c41` (`Phase 53R`), with no
commit and no push.

## Phase 53T — First GS-page RSP Producer (2026-09-14)

Phase 53T preserves the Phase 53S record above and resolves the remaining
producer question. Phase 53S correctly identified the exact OVMF transfer and
the already-invalid stack observed by later firmware code. Its statement that
the first producer was still unknown is retained as the state at the end of
Phase 53S; the bounded prologue trace below supersedes only that unresolved
part of the conclusion.

### Live preflight and repository state

The live preflight was performed before adding the Phase 53T diagnostic. The
prompt's expected HEAD was not current; the actual repository state was

    repository  D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration
    branch      nativeaot-managed-kernel-integration
    HEAD        4008a890d94ac5ca68867499d1ad6f1f960fd169
    subject     Outcome E — exact OVMF transfer proven; first GS-page RSP producer remains unresolved.
    upstream    origin/nativeaot-managed-kernel-integration
    ahead       0
    behind      0
    status      clean

The starting worktree was clean. This differs from the prompt's expected
`1930ec1d4ddfe1f6d5d7c3d27db5baacbd623c41`; no attempt was made to reconcile
the two states. The Phase 53S document, its 26 retained evidence directories,
the Phase 53S capture script, and all other existing forensic artifacts were
preserved.

At preflight, two QEMU processes were already running: the repository's
unrelated application-runtime validation and a separate Phase 28 compiler
bootstrap validation. No GDB, LLDB, or WinDbg process was running. Neither
unrelated QEMU process was stopped or reused. Each Phase 53T capture launched
its own QEMU/GDB pair and recorded a before/after process inventory; the clean
confirmation run left no diagnostic process behind.

The exact Phase 53S v8 binary remains available and was treated as
authoritative. No rebuild was required. The preserved artifacts used for the
Phase 53T confirmation were

    managed payload  4,790,784 bytes
                    SHA-256 24ECBA6EBDADD720351BD0AE768AB177F366D5CCECFA318881376128351B6D09
    BOOTX64.EFI      681,287 bytes
                    SHA-256 D8DC2BFC4D58DFB27C05A82FFBE145E22AF7BC699599B8477BC3500C60BFD69D
    matching PDB     11,292,672 bytes
                    SHA-256 BB12D9271C3D4CAC80C2BB3BCF25C3ED839BE4561126F68B818F81A975F3F7ED
    OVMF code        3,653,632 bytes
                    SHA-256 33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A
    OVMF vars        540,672 bytes
                    SHA-256 5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E

The diagnostic toolchain was PowerShell 7.6.5, QEMU 11.0.0, and MinGW GDB
17.1. The repository's `global.json` requests .NET SDK 10.0.302 while only
10.0.401 is installed on this host, but no build was attempted because the
preserved v8 artifact was sufficient.

### Inherited domains and Phase 53S boundary

The retained Phase 53S run established the following domains and observations:

    target GSBASE                 0x4CFE000
    GS page                       0x4CFE000–0x4CFEFFF
    worker stack                  0x4CFF000–0x4D02FFF
    valid scheduler RSP           0x4D02FE8 (context 0x1A9A30)
    valid event-call RSP          e.g. 0x4D021F0 / 0x4D02470
    bad OVMF-entry RSP             0x4CFE228 (Phase 53S run 19)
    later bad serial/interrupt RSP values in the same GS page

Phase 53S also proved the scheduler consumer at `0x16722E`:

    mov 0x128(%rsp),%r10
    mov %r10,%rsp
    jmp *%rax

The context `0x1A9A30` supplied a valid worker RSP and GS pair. The OVMF
entry at `0x6B0D67C`, its caller, the timer frame, and the later firmware
thunk therefore remained downstream consumers of an already-invalid stack.
The OVMF-side transfer boundary remains valid after Phase 53T.

### Experiment design and captures

`tools/Run-Phase53TFirstRspProducerCapture.ps1` reused the exact v8 payload,
fresh copied OVMF code/vars, and the bounded Phase 53S serial/keyboard input
sequence. It added debugger-side breakpoints at the scheduler restore, worker
entry, valid event call, the suspected large-frame function, the generated
stack-probe GS read, the explicit RSP subtraction, and the first instruction
after that subtraction. Known Phase 53S downstream faults for the two retained
worker contexts were logged and continued so they could not hide a later
invocation of the target function.

The bounded run sequence was retained as raw evidence:

    run-02  diagnostic layout/timing variant; no target pivot, excluded
    run-03  worker path did not reach the large-frame routine
    run-04  same bounded negative observation on a fresh boot
    run-05  reached the routine; nested single-step instrumentation stopped after the probe setup
    run-06  reached the writer pre-state; nested single-step instrumentation stopped before post-state
    run-07  captured the complete pivot; result marker was present in GDB output
    run-08  clean confirmation; FIRST_RSP_PIVOT_CAPTURED

The authoritative confirmation is
`evidence/phase53t-first-rsp-producer-20260914-08/run-1/`. Its manifest and
GDB log contain the complete before/after state. Runs 05 and 06 are retained
as instrumentation diagnostics rather than interpreted as negative runtime
results.

### Static disassembly and exact function

The preserved payload's PE preferred image base is `0x180000000`; the v8
loaded image base is `0x4EAC000`. The `.pdata` entry covering the target
routine spans preferred `0x18016F330–0x180170327`, or loaded
`0x501B330–0x501C327`. The PDB is retained, but the available MinGW objdump
does not emit its managed NativeAOT symbol name; the exact module, loaded
address, preferred address, and unwind range are unambiguous.

This is the NativeAOT-generated large-frame routine in
`gxos-managed-kernel.dll`, loaded at `0x501B330` (`.text+0x16E330`, preferred
`0x18016F330`). Its relevant prologue is

    0x501B330: 48 89 5C 24 08       mov [rsp+0x08],rbx
    0x501B335: 48 89 74 24 10       mov [rsp+0x10],rsi
    0x501B33A: 48 89 7C 24 18       mov [rsp+0x18],rdi
    0x501B33F: 55                    push rbp
    0x501B340: 41 54                 push r12
    0x501B342: 41 55                 push r13
    0x501B344: 41 56                 push r14
    0x501B346: 41 57                 push r15
    0x501B348: 48 8D AC 24 50 BF FF FF lea rbp,[rsp-0x40B0]
    0x501B350: B8 B0 41 00 00       mov eax,0x41B0
    0x501B355: E8 E6 D2 01 00       call 0x5038640
    0x501B35A: 48 2B E0             sub rsp,rax
    0x501B35D: 48 8B 05 9C DD 30 00 mov rax,[rip+0x30DD9C]

The call target at `0x5038640` is a stack-probe helper. Its relevant
GS-relative audit is

    0x503865C: 65 4C 8B 1C 25 10 00 00 00  mov r11,qword ptr gs:[0x10]

The helper temporarily subtracts `0x10` from its own RSP, computes a
prospective lower address from RAX, reads `GS+0x10`, and conditionally probes
pages. It restores its temporary frame and returns; it does not load RSP from
GS. The Phase 53T GDB capture observed the GS read at effective address
`0x4CFE010` with value zero, and then observed RAX still equal to `0x41B0`
at the explicit `sub rsp,rax` instruction.

### First architectural RSP pivot

The confirmation timeline is

    T-3  context 0x1A9A30 supplies RSP=0x4D02FE8, RIP=0x167B40,
         GSBASE=0x4CFE000; scheduler restores RSP at 0x16722E.
    T-2  target worker executes valid event calls, including
         RIP=0x7E6E6BC with RSP=0x4D021F0 and GSBASE=0x4CFE000.
    T-1  large-frame routine enters at RIP=0x501B330 with RSP=0x4D02998.
         Its pushes reduce RSP only within the valid worker stack.
    T-0  0x501B350 supplies the immediate frame size 0x41B0; the
         0x5038640 probe returns with RAX=0x41B0.
    T0   0x501B35A executes bytes 48 2B E0: sub rsp,rax.
         old RSP=0x4D02970; RAX=0x41B0.
    T+1  0x501B35D is reached with RSP=0x4CFE7C0.
         This equals GSBASE+0x7C0 and lies in 0x4CFE000–0x4CFEFFF.
    T+n  the later call reaches the Phase 53S-observed 0x501A800 entry
         with the already-invalid GS-page stack; OVMF/interrupt/GC failures
         remain downstream symptoms.

The arithmetic is exact:

    0x4D02970 - 0x41B0 = 0x4CFE7C0
    0x4CFE7C0 - 0x4CFE000 = 0x7C0
    worker-stack-low 0x4CFF000 - new-RSP 0x4CFE7C0 = 0x840

Thus the exact valid RSP immediately before the first invalid transition is
`0x4D02970`, and the exact first observed invalid RSP is `0x4CFE7C0`.
The earlier pushes and the probe helper's temporary subtraction remain inside
the valid stack domain. No earlier RSP writer was observed in this execution:
the scheduler restore, worker entry, event call, generated prologue pushes,
and probe-helper return all leave architectural RSP valid. The explicit
`sub rsp,rax` is therefore the first writer that moves RSP into the GS page.

### Source provenance and GS-relative interpretation

The source is not a saved context field, return slot, interrupt frame member,
`ret`, `iretq`, or direct GS-relative pointer load. It is the generated
immediate `0x41B0` at `0x501B350`, preserved through the stack-probe call at
`0x5038640`, and consumed by `sub rsp,rax` at `0x501B35A`.

The GS relationship is causal as an address-domain collision, but not as the
source operand of RSP. The valid current stack was already only `0x3970`
above the worker-stack low boundary, while the generated frame allocation was
`0x41B0`; the subtraction crossed that boundary by `0x840` and landed at
`GSBASE+0x7C0`. The GS-relative stack-limit read at `GS+0x10` returned zero
and may explain why the probe did not guard this boundary, but it did not
supply the new RSP. The correct conclusion is therefore: GS-page overlap is
proven causal in placement, while a GS pointer/value load is disproven as the
direct RSP producer.

### Classification and disposition

**Outcome A — First RSP Producer Proven.**

The first architectural producer is `sub rsp,rax` at loaded address
`0x501B35A` in the NativeAOT-generated `gxos-managed-kernel.dll` routine
starting at `0x501B330`. Its source is `RAX=0x41B0`, supplied by the
`mov eax,0x41B0` generated frame-size constant at `0x501B350` and preserved
by the stack-probe helper. The Phase 53S OVMF boundary remains valid but is
downstream of this newly proven pivot.

No production repair was made. The evidence proves the producer, but Phase
53T does not yet establish the complete intended stack-contract repair. In
particular, the worker stack assignment, its GS-page exclusion, and the
meaning/initialization of the GS stack-limit slot require a separate bounded
repair design and before/after reproduction.

### Files, validation, and final repository state

Phase 53T added the diagnostic script
`tools/Run-Phase53TFirstRspProducerCapture.ps1` and retained raw evidence in
`evidence/phase53t-first-rsp-producer-20260914-02` through `-08`. The script
was PowerShell-AST parsed successfully after its final changes. The clean
confirmation run 08 completed with `target_result=FIRST_RSP_PIVOT_CAPTURED`;
its process inventory shows no diagnostic process left running. No production
source, EFI, payload, PDB, OVMF image, scheduler assembly, or runtime helper
was modified. No rebuild, commit, push, PR, reset, clean, stash, checkout,
amend, rebase, merge, or destructive operation was performed.

The ending HEAD is unchanged:

    4008a890d94ac5ca68867499d1ad6f1f960fd169
    Outcome E — exact OVMF transfer proven; first GS-page RSP producer remains unresolved.

Ending ahead/behind remains `0/0`. The ending worktree contains the modified
Phase 53 forensic document, the untracked Phase 53T diagnostic script, and
the untracked retained Phase 53T evidence directories; existing Phase 53S
work remains intact.

### Recommended Phase 53U target

Target the worker-stack contract around context `0x1A9A30` and the generated
frame/probe contract, not the later OVMF or GC failure. Specifically, prove
why the worker's current RSP reaches `0x4D02970` while its low boundary is
`GSBASE+0x1000`, why `GS+0x10` is zero, and whether the stack-probe/runtime
contract is supposed to reject or accommodate a `0x41B0` frame. Phase 53U
should then design a local repair with a before/after boot proof; it should
not patch the later callback, interrupt frame, allocator, or exception path.

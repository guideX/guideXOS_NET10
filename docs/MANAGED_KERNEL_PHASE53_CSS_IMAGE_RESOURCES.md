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

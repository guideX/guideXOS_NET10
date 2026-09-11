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

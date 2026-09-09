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
than `ManagedHttpsUrl.MaximumUrlLength`. Controls, escapes, gradients, multiple
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

No `MANAGED_E1000_RX_READY` marker follows. With the Phase 53 runner's
temporary `-cpu max` perturbation the post-GC conjunction returned false
(driver status `0x7`) after the roots witness. With that speculative CPU
change removed, the same Phase 53 path faulted inside the collection: vector
`0x0E`, `RIP=0x4D91090`, `CR2=0x400002431000`, image base `0x4C17000`
(`RVA=0x17A090`). `CR2` is in the managed virtual-heap range, not the E1000
MMIO or DMA ranges. The available PDB evidence identifies the native image
but does not resolve a closer method symbol. This is allocation/layout
sensitivity at the NativeAOT GC boundary, not evidence of a CSS or PNG defect.

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

The exact diagnostic payload used for all Phase 53B fault conclusions was
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
read of the candidate object address itself, not a driver MMIO or DMA access.

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
The observed invalid candidate address is consequently an H1-style invalid
GC reference/GC state observation, not proof that a valid GC page was omitted
by the VM bridge. The exact producer of that invalid reference is not yet
identified, so no runtime or allocator correction is justified.

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
comparison, or E1000 packet-semantic change was retained. Temporary page-table
and VM-ledger witnesses were removed after the exact evidence was captured.
The focused next regression is a minimal GC-only managed-kernel boot with
bounded allocation-pressure and gen0/full-collection variants; it remains
open because the current Phase 15 driver path faults before `RX_READY`.

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
The exact Phase 53 Gate4 image reached E1000 TX completion but failed in the
existing post-TX GC-survival probe before `MANAGED_E1000_RX_READY`. A
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

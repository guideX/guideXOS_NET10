# Phase 47 — bounded deterministic software rasterizer

Phase 47 adds a platform-portable software raster stage after the Phase 46
validated display list.  It is deliberately not a GOP/PCI/framebuffer-driver
integration: the caller supplies the framebuffer storage and owns its lifetime.
The current repository audit found no existing managed GOP, compositor,
Navigator, or font backend to reuse.  The implementation therefore provides a
small proof glyph source and a bounded ARGB8888 target without introducing a
second platform surface.

## Contract

`ManagedSoftwareRasterizer` consumes `ManagedPaintEngine` commands, or a
validated command span for host tests, and writes a caller-owned
`ManagedFramebuffer`:

```csharp
uint[] pixels = new uint[width * height];
ManagedFramebuffer target = new(pixels, width, height);
ManagedSoftwareRasterizer rasterizer = new();
bool complete = rasterizer.TryRender(paint, target);
```

`ManagedFramebuffer` supports an offset and padded stride, while the supported
format is exactly `ManagedRasterPixelFormat.Argb8888`.  Preflight happens before
the first write and rejects null/invalid geometry, short stride/storage,
unsupported format, and checked geometry overflow.  The active rectangle is
`Width × Height`; row padding and caller guard words are never written or
hashed.

The render attempt is bounded by the command count, framebuffer dimensions,
and the fixed clip-stack capacity.  The rasterizer allocates only its fixed
clip stack, hash state, and bounded scalar telemetry; it does not allocate a
surface, strings, per-pixel objects, or a text copy. `Reset` clears rasterizer
state and cancellation controls but never clears caller storage.

## Phase 46R handoff and rendering audit

The rasterizer preserves the corrected Phase 46R contract in all five places
that affect pixels: ancestor opacity is already packed into command alpha;
fixed descendants are already viewport anchored; failed paint preflight cannot
leave a stale command/hash/telemetry snapshot; z-order magnitude and source
order are already established by the display list; and source-box/source-node
pairs are validated before rasterization. Phase 47 traverses that list in
order and applies no second scroll, opacity, CSS, layout, or z-index pass.

The repository audit found no reusable managed GOP, compositor, Navigator
surface, bitmap font/atlas, or image decoder. Existing drawing paths are not
used as they either target physical output or carry allocation-heavy desktop
assumptions. The Phase 47 surface is therefore intentionally platform-neutral:
`ManagedFramebuffer` is the future physical-output adapter boundary, while
`ManagedProofGlyphSource` is a checked-in proof source rather than a production
font engine.

## Rendering semantics

Commands are executed in display-list order.  `BeginClip` intersects the
current clip, `EndClip` restores the previous clip, and every primitive is
intersected with both the active clip and its command clip.  Rectangles are
half-open.  Fills use direct scanlines.  Solid borders draw the four edge bands
with independent widths and count each border pixel once.  Unsupported border
styles fail validation.  Image placeholders use a deterministic checkerboard,
dark edge, and diagonal cross.  Text reads scalar values directly from the
document arena and renders through the allocation-free seven-row proof glyph
source, including a deterministic fallback glyph.

The raster stage does not apply scroll or opacity a second time.  Phase 46 has
already emitted viewport-transformed geometry and effective packed alpha in
`ManagedPaintCommand.Color`; the rasterizer consumes those values directly.
Consequently normal content moves with the generated scrolled display list,
fixed content remains viewport-anchored, and nested opacity is blended once.

Source-over blending uses integer straight-alpha arithmetic with round-to-near:

```text
inverse = 255 - sourceAlpha
outAlpha = sourceAlpha + round(destinationAlpha * inverse / 255)
outChannel = round((sourceChannel * sourceAlpha * 255
                   + destinationChannel * destinationAlpha * inverse)
                   / (outAlpha * 255))
```

Transparent source pixels are skipped.  The implementation uses long
intermediates, so the specified checks are stable: opaque white over opaque
black is `0xFF808080` for 50% white, 50% red over opaque blue is
`0xFF80007F`, and Phase 46's effective `0x3F123456` over opaque black is
`0xFF040D15`.

Text is top-aligned to the Phase 46 fragment rectangle; it does not invent
ascent/descent metrics. The proof atlas is seven rows by five columns and
covers space, A–Z (with lowercase mapped to the corresponding compact proof
shape), digits, and common punctuation. Missing scalars use a fixed `?`
fallback and are counted. Font sizes select 1× through 4× integer nearest-
neighbour scaling in both axes, with no antialiasing. Text scalars are read directly from
the document's bounded arena through `TryGetTextScalar`.

## Hash and telemetry

On successful completion the framebuffer hash is SHA-256 over the fixed prefix
`GXOS-P47-FB-ARGB8888\0`, followed by every active pixel in explicit ARGB byte
order, row-major, excluding stride padding.  A failed or cancelled attempt has
an invalid hash.  Telemetry reports framebuffer geometry, command and primitive
counts, clip depth, glyph requests/fallbacks, considered/written pixels,
blends, transparent skips, offscreen primitives, cancellation checkpoints,
dirty bounds, terminal state, failure reason, and hash validity.

Preflight validates format, positive geometry, stride, offset, backing
capacity, and checked arithmetic before the first write. It distinguishes
`InvalidFramebuffer`, `FramebufferTooSmall`, `FramebufferGeometryOverflow`,
and `UnsupportedPixelFormat`; display-list, text-reference, clip-depth,
paint-command, glyph-source, cancellation, and invalid-state failures remain
distinct. Every attempt clears prior counters, clip state, dirty bounds, and
hash validity. `Cancel` is checked before commands and during clear, fill,
border, image, and glyph work; `Reset` permits reuse without implicitly
clearing the caller's storage.

## Memory and complexity

The existing Phase 43 document, Phase 44 styles, Phase 45 layout, and Phase
46 display list retain their established bounded arenas. Phase 47 adds a
default 64-entry × 16-byte clip stack, the fixed SHA-256 block/state/schedule
and 32-byte digest, scalar telemetry, and no surface-sized scratch. The
caller-owned native proof surface is 160 × 180 × 4 = 115,200 active/backing
bytes with no padding. The host suite additionally covers a 320 × 240 scene,
offset surfaces, row padding, and guard words. Thus the complete active proof
footprint is the existing Phase 43–46 pipeline plus one 115,200-byte caller
surface; there is no hidden second framebuffer.

Traversal is O(commands); fills, borders, images, and hashing are O(clipped
pixels); glyph work is O(glyph bitmap coverage); and clip changes are O(depth)
with a fixed maximum depth. There is no recursion, render queue, dynamic
scanline buffer, or per-glyph allocation.

## Authoritative proof record

The native fixture reports 68 tokens, 41 document nodes, 13 CSS rules, 33
layout boxes, 16 lines, 51 text fragments, and 59 paint commands. Its raster
record is 3 fills, 2 borders, 47 text commands, 1 image, clip push/pop 3/3,
peak depth 3, 189 glyph requests/rendered, 8 fallbacks, 26,460 considered
glyph pixels, 8,232 fill pixels, 684 border pixels, 2,755 glyph pixels, 96
image pixels, 40,567 total writes, 11,671 blended writes, and dirty bounds
`[0,0]..[159,179]`. The selected proof values include fixed pixel
`(8,6)=FF102070`, normal scrolled pixel `(29,18)=FF081119`, nested command
`0x3F123456`, nested output `FF0B1A28`, and image sample `(44,179)=FFB0B0B0`.

The host suite reports 952 assertions. Existing Phase 44, 45, and 46 host
suites report 66, 292, and 1,846 respectively (2,204 existing; 3,156 combined).
NativeAOT publishing with SDK 10.0.400/MSBuild 18.9.6 succeeds with the three
pre-existing warnings (one `CS8602`, two `CA2014`) and zero errors; the current
payload is 2,439,168 bytes,
SHA-256
`9EB3815A27C0813C2512D12F862A72A7C79E09B2EC0B2B6FFA313B454281939A`.

Phase 44, corrected Phase 45, and corrected Phase 46 evidence each contain
3/3 QEMU boots with the final payload. The Phase 47 runner performs the full
Phase 46 HTTPS pipeline and completes 3/3 fresh boots, including the validator
pass and one-word-short framebuffer negative control. QEMU is
`C:\Program Files\qemu\qemu-system-x86_64.exe`; the final process count is
zero. Current native Phase 47 evidence is retained under the generated
`artifacts\phase47-raster-*` directory; earlier corrected Phase 44/45 evidence
is retained under `artifacts\phase47-qemu-phase44-current` and
`artifacts\phase47-qemu-phase45-current`.

## Verification

The host suite is `ManagedKernelPhase47HostTests`; it covers preflight and
guards, clear/preserve behavior, all required alpha cases, borders, clipping,
text/fallbacks, image placeholders, fixed-scroll/z-order/source pairing,
cancellation, hash invalidation/recovery, and reset behavior. Run it with:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Run-ManagedKernelPhase47HostTests.ps1
```

The authoritative native path is:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass `
  -File .\tools\Run-ManagedKernelPhase47SoftwareRasterizerProof.ps1
```

That runner builds the NativeAOT managed-kernel payload, builds the existing
Gate 4 harness, performs three fresh QEMU boots of the deterministic Phase 46
HTTPS fixture, then requires the Phase 47 raster pass, the short-framebuffer
negative marker, fixed/scroll and nested-alpha proof output, eight framebuffer
hash words, and byte-for-byte identical Phase 47 telemetry/pixel lines across
all runs.  It intentionally leaves the generated evidence directory in place
for inspection and does not commit or push changes. The native proof currently
uses a caller-owned 160×180 ARGB8888 surface: 160×120 clips the fixture's image
sample, while 320×240 is unnecessarily slow under QEMU TCG. The host suite
also exercises larger 320×240 surfaces and padded/offset guard layouts; no
physical display is required.

The next sensible step is a Phase 48 physical-output adapter or production
font/image backend, keeping this deterministic in-memory rasterizer as the
semantic reference.

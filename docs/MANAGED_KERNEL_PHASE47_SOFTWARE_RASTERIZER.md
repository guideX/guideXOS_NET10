# Phase 47 — bounded deterministic software rasterizer

## Interrupted-run recovery record

The host PC hard-rebooted during final Phase 47 validation. Recovery began
from the live repository, not from an assumed revision. The starting branch was
`nativeaot-managed-kernel-integration`, starting HEAD was
`7615f4ccd6098e5bb79503b3ea7ce82413c0daf7` (subject `...`), upstream was
`origin/nativeaot-managed-kernel-integration`, and the branch was 0 ahead / 0
behind. The starting worktree was clean, with no tracked modifications or
untracked files. No source file was partially written or corrupt; generated
validation directories from earlier attempts were retained as evidence and
were not used as final proof.

The checked-out HEAD already contained the Phase 47 glyph repair. The audit
confirmed that the original defect was real: the proof glyph source repeated
columns horizontally for integer scaling but emitted only its seven source
rows vertically. A 2×–4× glyph therefore had blank rows below the original
seven-row bitmap. The corrected implementation maps both axes with integer
nearest-neighbour selection: `sourceRow = outputRow / scale` and
`sourceColumn = outputColumn / scale`; each source pixel becomes an `S × S`
block. The repair was preserved and covered by new host tests plus a guest
scaled-glyph proof.

The Phase 46 PowerShell wrapper also contained a tooling-only compatibility
defect: `SHA256.HashData` was unavailable in the Windows PowerShell runtime
used by the proof. It now uses `SHA256.Create().ComputeHash()` and retains
SHA-256 semantics; no weaker checksum was substituted.

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
neighbour scaling in both axes, with no antialiasing. Text scalars are read
directly from the document's bounded arena through `TryGetTextScalar`.

The proof atlas base glyph is 5×7 with a 6-pixel advance. The supported proof
dimensions are 5×7 at 1×, 10×14 at 2×, 15×21 at 3×, and 20×28 at 4×; advances
are 6, 12, 18, and 24 respectively.

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

The final source-aligned proof is retained under
`artifacts\phase47-raster-final8-20260905` and completed 3/3 fresh QEMU
boots. The final Phase 47 host suite reports 955 assertions, up from the
previous 952: the added checks are
`glyph-nearest-neighbor-scale-1x`, `glyph-nearest-neighbor-scale-2x`,
`glyph-nearest-neighbor-scale-3x`, and
`glyph-nearest-neighbor-scale-4x`. They verify exact dimensions and advances,
every vertically repeated row, every horizontally repeated column, clipping,
and deterministic lookup. The four checks fail against the old
horizontally-only implementation because its extra rows are zero.

The final host totals are Phase 44 = 66, Phase 45 = 292, Phase 46 = 1,846,
and Phase 47 = 955, for an arithmetic aggregate of 3,159 assertions. The
final native payload is 2,444,288 bytes. The standalone final publish hash is
`E2D4F343888F0829D6D04B7688D86EC4F12399B46B002A3EB023F2A2338CBB8C`; the
source-aligned payload used by the authoritative QEMU run is 2,444,288 bytes
with hash
`DDAD75E91B5213F5F6BE8DC3C8780F4B961642C268EAFD7FFBCA7F0EDF8169D6`.
Both were produced from the same corrected source; the difference is the
path-dependent NativeAOT output artifact.

The requested SDK 10.0.302 was not installed; repository fallback used SDK
10.0.400 and MSBuild 18.9.6+14fbf8d52 (`18.9.6.38015`). NativeAOT completed
with zero errors and the expected warning profile: one `CS8602` and two
`CA2014` warnings. QEMU is
`C:\Program Files\qemu\qemu-system-x86_64.exe` version 11.0.0.0.

The resource pipeline reports HTTP 200, Content-Type length 24
(`text/html; charset=utf-8`), gzip encoding, 776 encoded bytes, 1,574 decoded
bytes, 1,143 Unicode scalars, 68 HTML tokens, 41 document nodes, 13 CSS rules,
14 selector matches, 33 layout boxes, 16 lines, 51 text fragments, and 59
display commands. The final resource SHA-256 is
`88F996E3FBC184B7725B7D5D347B283C3E87F006C5912352D95EA2F84F30754B`.

The final framebuffer is 160×180, stride 160, ARGB8888, with 115,200 active
bytes and 115,200 backing bytes. Raster telemetry is: 59 commands processed,
3 fills, 2 borders, 47 text commands, 1 image placeholder, clip pushes/pops
3/3, peak clip depth 3, 189 glyph requests and 189 glyphs rendered, 8
fallback glyphs, 189 scaled glyphs, 26,460 glyph pixels considered, 5,510
glyph pixels written, 8,232 fill pixels, 684 border pixels, 96 image pixels,
43,322 total pixels written, 14,426 blended pixels, zero transparent skips,
zero offscreen skips, and dirty bounds `[0,0]..[159,179]`. The final
framebuffer SHA-256 is
`6F671E61760024E683A40BC5FED749FF902746702E8C4431715BCD5EB0340548`.

The scaled guest proof records scalar `0x6E` (`n`), scale 2, source 5×7,
output 10×14, row A `(7,5)=FF102070`, and the vertically repeated row B
`(7,6)=FF102070`. It emits
`GXOS_NET10:MANAGED_HTTPS_PHASE47_SCALED_GLYPH_PROOF_PASS` on all 3 boots.
The same final proof records fixed `(8,6)=FF102070`, normal scrolled
`(29,18)=FF081119`, nested command alpha `0x3F` and color `0x3F123456` with
output `FF0B1A28`, text `(8,6)=FF102070`, and image `(44,179)=FFB0B0B0`.
The Phase 46 fixed-scroll and nested-opacity controls also pass 3/3 inside
the final pipeline; their semantic markers are
`normal-document-scroll-fixed-viewport` and `2500-0x3F123456`.

The final display-list and framebuffer/raster validators pass. The
framebuffer-too-small negative control passes 3/3 with zero writes, no stale
hash or telemetry, and a healthy kernel marker. Host guard/canary tests pass;
the final guest proof uses the caller-owned guarded framebuffer contract, with
the host guard suite providing the explicit padded/offset canary coverage.

Hash-chain isolation is clean: resource, document, style, layout, and
display-list hashes remain unchanged from the pre-fix proof. The final hashes
are resource `88F996E3FBC184B7725B7D5D347B283C3E87F006C5912352D95EA2F84F30754B`,
document `9C10390C80349958863E40612E8F8E7755F14CB4B461597073F7782A5C620882`,
style `109E9C5887415F84BEB090216DC906F3505EC3905AD303AF71C424FE06DB63B4`,
layout `5BFA6AAA55309A627D06770FD357E0550AB572E0A9BCE0673BCF06B1828D1089`,
and display list
`677EAC49EC7EC785A3B11F6B494B43B14CB8ED25C4B7666C6B2529BA79F8BF39`.
Only raster telemetry, selected pixels, and the framebuffer hash changed: the
corrected implementation now writes repeated vertical glyph rows, while all
upstream inputs and commands remain identical.

The selected Phase 47 pixel set includes at least twelve exact values across
the guest proof and host regression fixtures: scaled row A `(7,5)=FF102070`,
scaled repeated row B `(7,6)=FF102070`, fixed guest `(8,6)=FF102070`, normal
guest `(29,18)=FF081119`, nested-opacity guest `FF0B1A28`, text guest
`(8,6)=FF102070`, image guest `(44,179)=FFB0B0B0`, host nested-opacity
`FF040D15`, host fixed blue `FF0000FF`, host normal red `FFFF0000`, host
z-order overlap `(6,6)=FF445566`, and host border corner `FFFF00FF`.

Standalone final-source QEMU regressions also pass: Phase 44 = 3/3 in
`artifacts\phase44-final-source-20260905`, Phase 45 = 3/3 in
`artifacts\phase45-final-source-retry2-20260905`, and corrected Phase 46 =
3/3 in `artifacts\phase46-final-source-20260905`. The authoritative Phase 47
proof is in `artifacts\phase47-raster-final8-20260905`. OVMF code SHA-256 is
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` and the
OVMF vars template SHA-256 is
`5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.

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
all runs. It intentionally leaves the generated evidence directory in place
for inspection and does not commit or push changes. The native proof currently
uses a caller-owned 160×180 ARGB8888 surface: 160×120 clips the fixture's image
sample, while 320×240 is unnecessarily slow under QEMU TCG. The host suite
also exercises larger 320×240 surfaces and padded/offset guard layouts; no
physical display is required.

Phase 48 was not started during this recovery. Remaining Phase 47 limitations
are the intentional proof glyph atlas, placeholder image primitive, in-memory
framebuffer, and absence of shaping, antialiasing, image decoding, or physical
output.

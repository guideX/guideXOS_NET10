# Managed Kernel Phase 48 — Bounded Real Font Integration

Phase 48 connects the existing bounded HTML/CSS layout and semantic paint
pipeline to real, generated Roboto bitmap data. The implementation is bounded
and deterministic: eight fixed faces, a dense ASCII map, a visible fallback,
shared layout/raster metrics, alpha coverage, and no runtime font-file or
system-font lookup.

## Runtime design

`ManagedPhase48FontRegistry` owns the eight generated faces. CSS-like requests
select the 9pt family for sizes up to 10 and the 12pt family above that;
font-weight 700 or greater selects bold and `italic` selects the italic face.
The registry implements both layout measurement and raster glyph lookup, so
advance, bearing, baseline, line height, fallback, and coverage all come from
the same metadata.

The old Phase 47 5x7 proof source remains separate. The rasterizer accepts
both sources, and the Phase 47 host suite continues to exercise the old source
and scaling path.

## Asset audit and provenance

The local sibling Navigator trees contain the same Roboto bitmap family at:

- `D:\dev\guideXOSServer_NAVIGATOR_IMPROVEMENTS\assets\Fonts\roboto`
- `D:\dev\guideXOS\Ramdisk\Fonts\roboto`

There are eight 260x160 PNG atlases: 9pt and 12pt regular, bold, italic, and
bold-italic. Each is a 13x8 grid of 20x20 cells covering printable ASCII
U+0020..U+007E. This existing guideXOS asset was selected because it is already
the Navigator font source, has useful regular/bold/italic variants, and can be
converted offline to static alpha data without placing a desktop font stack in
the guest. The source hashes, license note, and generator details are recorded
in [MANAGED_PHASE48_FONT_PROVENANCE.md](MANAGED_PHASE48_FONT_PROVENANCE.md).

Roboto is distributed under Apache License 2.0. The sibling directories do not
contain a separate license file, so downstream redistribution must retain the
applicable upstream license and notice. No font was downloaded during Phase 48.

## Bounded representation

`ManagedPhase48FontFace` and `ManagedPhase48FontRegistry` are the shared face
and registry API. The registry has fixed capacity for eight faces and exposes
face selection, line metrics, scalar measurement, glyph lookup, atlas coverage,
telemetry, validation, and a semantic SHA-256. The generated file
`ManagedPhase48GeneratedFontData.g.cs` contains only static atlas bytes and
metadata; the guest performs no file I/O, system-font discovery, or TTF/OTF
parsing.

Each face has 95 fixed glyph records and an implicit dense U+0020..U+007E map,
so lookup is O(1) after a range check. The packed metadata record is 10 bytes:
atlas X/Y, bitmap width/height, advance, signed bearing X/Y, and a pixel flag.
Whitespace is represented by a valid record with advance and no bitmap pixels.
Kerning, shaping, ligatures, bidi, and complex-script fallback are explicitly
out of scope; measured width is the sum of glyph advances.

The alpha atlas is 8-bit, one byte per 260x160 source cell plane. There are
8 x 41,600 = 332,800 atlas bytes and 760 x 10 = 7,600 metadata bytes. The
generator uses `System.Drawing` only offline while extracting PNG alpha; it is
not referenced by the NativeAOT guest.

## Coverage, CSS policy, and metrics

The bounded coverage is printable ASCII, including punctuation and digits.
Every valid unsupported scalar maps to the visible `?` glyph and increments
fallback telemetry; surrogate values and values above U+10FFFF are rejected.
This prevents silent spaces and atlas out-of-bounds access. Unsupported CSS
families use the default Roboto face. Sizes up to 10 select the 9pt family;
larger sizes select 12pt. Weight 700 or greater selects bold, and italic style
selects italic; the four combinations are present at both sizes.

The 9pt metrics are ascent 8, descent 4, line gap 4, baseline 12, and line
height 16. The 12pt metrics are ascent 9, descent 5, line gap 4, baseline 13,
and line height 18. Baseline is the offset from line top. Glyph coverage is
sampled at `atlasX + bearingX + column` and
`atlasY + bearingY + baseline + row`, so layout and rasterization use the same
authoritative bearings and advances.

For 8-bit coverage, the rasterizer computes
`floor(commandAlpha * glyphCoverage / 255)` using integer arithmetic, then
uses the existing deterministic source-over blend. Text color remains owned by
the paint command. No per-character allocation or dynamic glyph cache is used.

## Memory accounting

Logical fixed font data is 340,624 bytes: 332,800 atlas bytes, 7,600 packed
metadata bytes, and 224 bytes for eight records of integer face metrics. The
dense codepoint map is implicit and costs 0 additional map bytes. Runtime font
state is bounded: a 32-byte semantic hash, one active-face value, and eleven
32-bit telemetry counters (45 logical bytes), plus the fixed face/reference
table. Managed object/array headers and references are runtime bookkeeping, not
font payload data; there is no unbounded cache. The 160x180 ARGB8888 proof
framebuffer is 115,200 bytes, so the measured font-data-plus-framebuffer
footprint is 455,869 logical bytes before the unchanged document, CSS, layout,
paint, and managed-runtime arenas.

Compared with the Phase 47 standalone payload of 2,444,288 bytes, the Phase 48
payload is 3,475,968 bytes: growth is 1,031,680 bytes, or 42.2078%. This is
the generated eight-face alpha/metadata representation plus the integration
and proof code; it is a deliberate bounded persistent asset rather than a
runtime font loader.

## Layout, display list, and raster integration

Phase 48 passes the same registry to layout, paint, and rasterization. Text
commands resolve the bounded font identity, size, weight, and style; no family
string is stored in a command. The 5x7 proof source remains available through
`ManagedProofGlyphSource` for isolated Phase 47 regression tests, while the
authoritative Phase 48 page uses generated Roboto alpha masks.

The reference page includes `guideXOS`, `iiii WWWW`, `Hello, world!`, digits,
accented/Greek/CJK/emoji samples, regular and bold text, multiple sizes, and a
wrapped paragraph. The measured widths are `iiii` = 28, `WWWW` = 44, and
`guideXOS` = 56 pixels. The proof uses 13 lines, 51 text fragments, and 2 soft
wraps with 12pt line height 18 and baseline 13. Selected layout boxes are BODY
`(8,8,784,227)`, MAIN `(28,22,606,195)`, and NOTE `(41,55,582,36)`.

## Hash-chain transition

The Phase 47 hashes were resource
`88F996E3FBC184B7725B7D5D347B283C3E87F006C5912352D95EA2F84F30754B`, document
`9C10390C80349958863E40612E8F8E7755F14CB4B461597073F7782A5C620882`, style
`109E9C5887415F84BEB090216DC906F3505EC3905AD303AF71C424FE06DB63B4`, layout
`5BFA6AAA55309A627D06770FD357E0550AB572E0A9BCE0673BCF06B1828D1089`,
display list `677EAC49EC7EC785A3B11F6B494B43B14CB8ED25C4B7666C6B2529BA79F8BF39`,
and framebuffer
`6F671E61760024E683A40BC5FED749FF902746702E8C4431715BCD5EB0340548`.

The Phase 48 reference fixture intentionally changes the resource and document
to demonstrate real font content, so its resource/document hashes are
`887E4B1A2DCD1E319D3D678ADF516A25F16C8CADEAE66E7559BDDFBCC138CD83` and
`48FDC0E2C908EA769700029EC7BEACD87C352D415129BFE97A946DE22F888EDA`.
Style remains `109E9C5887415F84BEB090216DC906F3505EC3905AD303AF71C424FE06DB63B4`.
The new layout, display-list/paint, font, and framebuffer hashes are
`74F6BC0EF5DD63F1A824BA4F6B4CAF2B96BF496A59AA31C390D4216878DACD04`,
`9715A7209FA568E645730C58A841D3D983F9C9187CAE6457D60415A082DEF360`,
`4872C3D6EA0701697830CD9FCB21294BF097584B6014C70CEE8D52359A6BE5F0`, and
`678D0C9489B06D56E058206D818A420C048773336400FF2DB0D261D48BAC956D`.
The layout/display/framebuffer changes are therefore expected consequences of
real metrics and glyph shapes, not an accidental change to the Phase 47 proof
source.

## Validation and telemetry

The validator checks face identity, metrics, 260x160 atlas geometry, exact
95-glyph coverage, cell bounds, glyph dimensions, positive advances, and a
visible fallback glyph. Telemetry counts face selection, ASCII/non-ASCII
lookups, fallback hits, bold/italic requests, layout measurements, raster
glyphs, atlas bytes, metadata records, and maximum generated dimensions.

The host proof covers registry shape/hash stability, all ASCII mappings,
unsupported-scalar fallback and rejection, malformed registry fixtures,
variable-width layout, CSS face selection, shared baselines, partial alpha
coverage, framebuffer determinism, and the preserved Phase 47 proof source.

## End-to-end proof

The proof wrapper is `tools/Run-ManagedKernelPhase48BoundedFontProof.ps1`.
It builds the managed NativeAOT payload, selects the Gate 4
`ManagedKernelPhase48` scenario, serves `/phase48/gzip`, and requires three
fresh QEMU boots. The serial proof emits the decoded resource identity,
document/style/layout/paint/font semantic hashes, font telemetry, layout and
paint records, sampled raster pixels, a deterministic framebuffer hash, and a
negative framebuffer-capacity check.

Host baseline and final retained evidence:

- Host cases: 398.
- Font faces: 8.
- Atlas bytes: 332,800.
- Metadata records/bytes: 760 / 7,600.
- Maximum generated glyph width/height/advance: 11 / 12 / 12.
- Font semantic hash:
  `4872C3D6EA0701697830CD9FCB21294BF097584B6014C70CEE8D52359A6BE5F0`.
- Managed payload: 3,475,968 bytes,
  `94E7C59CCBAB5D6731CE0EFEC24FDB86ECAD57E2B9BCBD47869BDD03F73CE3D4`.
- Served decoded resource: 1,574 bytes,
  `887E4B1A2DCD1E319D3D678ADF516A25F16C8CADEAE66E7559BDDFBCC138CD83`.
- Raster framebuffer: 160 x 180; glyph requests/rendered 189 / 189;
  fallback glyphs 8; glyph pixels considered/written 5,753 / 2,784;
  blended pixels 8,844.
- Framebuffer hash:
  `678D0C9489B06D56E058206D818A420C048773336400FF2DB0D261D48BAC956D`.
- Layout/paint evidence: 13 lines, 51 text fragments, 2 soft wraps, 59
  paint commands, and 47 text commands. `iiii` measures 28 pixels while
  `WWWW` measures 44; the 12pt baseline is 13 and line height is 18.
- Three fresh QEMU Phase 48 boots passed. Retained serial hashes are
  `E93C4D183098E9A064FA01429B5B8107346EBA3EF9E1F9FCC069AC96DA6FD725`,
  `71EDF92F87F2C2C5C40FDC4816ADF3E09748A7BB2E1B38D96C8F80065CCAE126`,
  and `007660AFE3DED23E097539A802D71FACAC14367169F8C999037BFAFA08D158E6`.
- Retained run metadata and summary are under
  `artifacts\phase48-font-20260905-185234395`; the clean three-run evidence
  is in `evidence-clean3`.

The clean guest record is HTTP 200, `text/html; charset=utf-8`, gzip, 776
encoded bytes, and 1,574 decoded bytes, with all received Unicode scalars
consumed. It validates the HTML/CSS tree, display list, font metadata, fixed
scroll and nested-opacity controls, and the framebuffer-capacity negative
control. Font telemetry is 618 lookups/hits, 24 fallback lookups, 594 ASCII
lookups, 24 non-ASCII lookups, 94 bold requests, 0 italic requests, 240 layout
measurements, and 378 raster glyphs. Raster telemetry is 5,753 glyph pixels
considered, 2,784 written, and 8,844 blended pixels.

The 12 exact retained raster samples are all ARGB8888 `0xFF8385C7` at y=4 and
x=6,7,8,9,10,11,12,13,14,15,16,17. They are emitted with the glyph sample
coordinates and are checked by the retained-evidence validator. The font,
display-list, and raster validators all pass. Fallback is proven in the same
positive page: eight fallback glyphs are rendered and the following supported
text remains intact.

NativeAOT used the installed fallback SDK 10.0.400 because the repository's
requested 10.0.302 is unavailable; MSBuild is 18.9.6. The final publish has
zero build errors and one pre-existing `CS8602` warning.
OVMF code SHA-256 is
`33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`; the
fresh-vars template SHA-256 is
`5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.

## Tests and next phase

The current host sweep passes Phase 39 through Phase 48 with 301, 4,347,
5,466, 205, 31, 66, 292, 1,846, 955, and 398 cases respectively, for an
arithmetic aggregate of 13,907. The retained QEMU regressions are Phase 44
3/3, Phase 45 3/3, corrected Phase 46 3/3, and Phase 47 3/3; Phase 48 is 3/3
fresh boots from the final payload. PowerShell syntax checks and
`git diff --check` pass, and the final QEMU process count is zero.

Remaining limitations are the ASCII-only bounded coverage, absent kerning and
shaping, integer size selection between two pre-generated sizes, and no
runtime font loading. Phase 49 should consider a larger bounded Latin-1 map or
additional pre-generated coverage, while preserving the single-face-metrics
path and the existing NativeAOT memory/accounting invariants.

QEMU run records are written below the wrapper output directory and are
summarized in `phase48-summary.log`. No commit, push, or pull request is part
of this phase.

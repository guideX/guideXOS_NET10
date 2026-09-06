# Managed Phase 48/50 Font Provenance

Phase 48 embeds a bounded, generated bitmap representation of the existing
guideXOS Roboto assets. Runtime code contains only static byte arrays and
metadata; `System.Drawing` is used by the build-time generator and is not a
runtime dependency of the NativeAOT kernel.

## Selected source assets

The selected sibling-tree source is:

`D:\dev\guideXOSServer_NAVIGATOR_IMPROVEMENTS\assets\Fonts\roboto`

The same assets are also present in:

`D:\dev\guideXOS\Ramdisk\Fonts\roboto`

Each source PNG is a 260x160, 13x8 grid of 20x20 cells for ASCII code points
32 through 126. The generator is
`tools/Generate-ManagedPhase48FontData.ps1`; it verifies the dimensions,
extracts the alpha plane, derives the glyph bounds/advance/bearings, and
emits deterministic C# into
`src/ManagedKernel/ManagedPhase48GeneratedFontData.g.cs`.

| Face | Source SHA-256 |
| --- | --- |
| roboto_9pt_regular.png | `4B556657B17446FCCA4B85FE94481EECF778DE3494939A0184E9F4E90693246F` |
| roboto_9pt_bold.png | `9D32C0F0CB1C1DB1C6CF98433B17BE1A86360D73D05193B0AABF0894EEF9D9D7` |
| roboto_9pt_italic.png | `3EBFC04652697BF191A0BE2ABEAB855D25A2AB32C8F56AF9E28A7473F4EB6406` |
| roboto_9pt_bolditalic.png | `BF98A245CF668C36D395B53693767189E13AC01036F6FCEB767B82C55E444C77` |
| roboto_12pt_regular.png | `EBB4218CC1C6CA9701D7341372CBF06F64E3DD04A2407A6AC96A353ADE66B322` |
| roboto_12pt_bold.png | `D5FE86F10FF4D230D8284F0068A31BB036140A49F1AB58F77C5259082B6BE5D8` |
| roboto_12pt_italic.png | `5B6DE13369B009A317DAD43FC607124A72091A56E63967CBAC930D53000B00ED` |
| roboto_12pt_bolditalic.png | `719C6D9C23C790A80FB5C9B1699F6C3BDC18952C6D31DD5D5CC6EE6C87BB5F47` |

The sibling trees did not contain a separate license file beside these PNGs.
The Roboto family is distributed under the Apache License 2.0; downstream
packaging should retain the applicable upstream license and notice text when
redistributing the source assets. This repository records the exact local
input hashes above and does not silently fetch or regenerate from a network
source.

## Bounded representation

- 8 fixed faces: 9pt and 12pt, each regular/bold/italic/bold-italic.
- 95 ASCII glyph records per face, with an implicit dense 32..126 map.
- 8 x 41,600 = 332,800 alpha-atlas bytes.
- 760 records x 10 bytes = 7,600 metadata bytes.
- 20x20 maximum cell geometry; generated maxima are 11 pixels wide, 12 high,
  and 12 pixels advance.
- 9pt metrics: baseline 12, ascent 8, descent 4, line gap 4, line height 16.
- 12pt metrics: baseline 13, ascent 9, descent 5, line gap 4, line height 18.
- Unsupported Unicode scalars use the visible `?` glyph; malformed surrogate
  scalars and values above U+10FFFF are rejected.
- No kerning, shaping, ligatures, or unbounded font discovery are included in
  this phase.

The registry semantic hash is computed over the registry format marker, map
range, metadata layout, face metrics, metadata records, and atlas bytes. The
host proof currently reports:

`4872C3D6EA0701697830CD9FCB21294BF097584B6014C70CEE8D52359A6BE5F0`

## Phase 50 extension

Phase 50 keeps the eight PNG atlases and adds a second fixed atlas page per
face, generated from the matching local Roboto TTF files. The TTF cmap audit
confirmed complete U+00A0..U+00FF coverage and the ten selected punctuation
scalars U+2013, U+2014, U+2018, U+2019, U+201C, U+201D, U+2022, U+2026, U+20AC,
and U+2122 for all four styles and both nominal sizes.

| TTF face | Source SHA-256 |
| --- | --- |
| Roboto-Regular.ttf | `DBD285B518E398832F6F4A736109C355CE25A49546BFCE41BAB256C9EF7E56EB` |
| Roboto-Bold.ttf | `27467020FEBCE4C0AC8482EA8E8D6F32BF8C5721FB9C301344D0E3F4D93A0119` |
| Roboto-Italic.ttf | `5A2F18DBDBE3AC07F14D38817D1FD22DF4E8E3B74F661F7AE7B29CC8140092BC` |
| Roboto-BoldItalic.ttf | `8234ADD45022E40D0AD293C8A2854A8F387E53DB5C433698FE6D89C8D1B5362E` |

The generated extended page is 260x180 (13x9 20x20 cells), with 106 records
per face: the dense 96-code-point Latin-1 map followed by a sorted ten-entry
sparse punctuation map. The generated output is deterministic; two generator
runs produced identical 1,062,494-byte C# outputs with SHA-256
`56018ABA6A19C6747F5DD4FF7F4EF5907415140119598CDE062750F8D4D23670`.

Phase 50 totals are 201 glyphs per face, 1,608 glyphs, 707,200 alpha bytes,
and 16,080 metadata bytes. The semantic hash is now computed in the
`GXOS-P50-FONT` domain and is:

`4184C857A49DABEF9ED19BA97EB0DBF87879EAD873F835336149BADB0F7D094A`

Only the bounded Latin-1 and common-punctuation subset is runtime-declared.
The source TTF audit also observed partial Greek and Cyrillic cmap coverage,
but those code points are intentionally not embedded or advertised by this
phase.

# Managed Phase 48 Font Provenance

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

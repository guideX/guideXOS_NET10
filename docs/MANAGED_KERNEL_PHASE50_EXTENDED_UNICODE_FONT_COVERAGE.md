# Managed Kernel Phase 50 — bounded extended Unicode font coverage

Phase 50 extends the existing static Roboto bitmap registry without adding a
runtime font loader, shaping engine, heap-backed map, or network dependency.
The managed kernel now has deterministic coverage for Latin-1 Supplement and
the most common typographic punctuation used by the first managed web page.

## Scope and source audit

The generator continues to consume the checked local assets at
`D:\dev\guideXOSServer_NAVIGATOR_IMPROVEMENTS\assets\Fonts\roboto`:

- The eight PNGs remain the authoritative ASCII pages: 260x160, 13x8 cells,
  20x20 cell geometry, U+0020..U+007E.
- The four matching Roboto TTFs were audited by cmap. Every style contains
  U+00A0..U+00FF and the ten selected punctuation scalars U+2013, U+2014,
  U+2018, U+2019, U+201C, U+201D, U+2022, U+2026, U+20AC, and U+2122.
- The audit also found partial Greek and Cyrillic coverage. Those ranges are
  deliberately not embedded or advertised in Phase 50.
- The exact PNG/TTF hashes are recorded in
  [MANAGED_PHASE48_FONT_PROVENANCE.md](MANAGED_PHASE48_FONT_PROVENANCE.md).

No font files were downloaded or silently replaced.

## Runtime representation

Each of the eight faces has two static alpha pages:

| Page | Geometry | Map |
| --- | ---: | --- |
| ASCII | 260x160 | dense U+0020..U+007E, 95 glyphs |
| Extended | 260x180 | dense U+00A0..U+00FF, then ten sorted punctuation scalars |

The mapping is intentionally simple and bounded:

- Latin-1 is one subtraction and bounds check.
- The ten punctuation values use a fixed sorted array and binary search.
- There is no dictionary, culture-sensitive lookup, runtime discovery, or
  unbounded fallback chain.
- Unsupported valid scalars render the visible `?` glyph. Invalid surrogate
  scalars and values above U+10FFFF are rejected.
- A declared scalar can fall back to the corresponding regular face if a
  future face-specific page is incomplete; the current eight generated faces
  all contain the declared subset.

The generated C# is emitted by
`tools/Generate-ManagedPhase48FontData.ps1`. It uses `System.Drawing` only at
generation time, embeds source hashes, validates page bounds and glyph maps,
and emits stable ordering. Two independent runs produced the same
1,062,494-byte generated file and SHA-256
`56018ABA6A19C6747F5DD4FF7F4EF5907415140119598CDE062750F8D4D23670`.

## Whitespace and decoding semantics

U+00A0 is a real mapped glyph with its advance width and no painted pixels.
The HTML UTF-8 decoder, numeric entities, and Latin-1 test path all preserve
it as U+00A0. Layout treats it as non-breaking whitespace: it participates in
the word width and is not a legal line-break boundary. The layout telemetry
records a prevented break when a non-breaking run exceeds the content width.

The same scalar is therefore preserved across:

`UTF-8 bytes → entity/character-reference decoding → tree text → layout → paint → raster`

Malformed UTF-8 remains rejected according to the existing decoder contract;
unsupported-but-valid Unicode remains deterministic `?` fallback.

## Footprint and semantic identity

Phase 50 reports:

- 8 faces × 201 glyphs = 1,608 glyph records.
- 707,200 alpha bytes total: 332,800 ASCII plus 374,400 extended.
- 16,080 metadata bytes at 10 bytes per record.
- The semantic hash is
  `4184C857A49DABEF9ED19BA97EB0DBF87879EAD873F835336149BADB0F7D094A`.

The hash domain is `GXOS-P50-FONT`, and covers the fixed maps, code points,
metrics, metadata, and both static atlas pages.

## Host proofs

`ManagedKernelPhase50HostTests` passes 683 cases, including:

- coverage descriptor and sorted sparse-map validation;
- all eight faces, glyph counts, page bounds, NBSP metadata, and semantic hash;
- UTF-8, HTML entity, and Latin-1 equivalence checks;
- NBSP no-break layout behavior;
- accented and punctuation width differences;
- deterministic raster output and unsupported-scalar fallback;
- negative validator cases and generator determinism.

The preserved Phase 48 suite passes 397 cases against the expanded registry.
The Phase 49 host suite remains a visible-page regression gate.

## NativeAOT and QEMU evidence

The NativeAOT payload was built with the installed .NET 10.0.400 fallback
toolchain (the repository’s pinned 10.0.302 SDK is not installed):

- payload size: 4,618,240 bytes;
- payload SHA-256:
  `2731DF0248FCDC25EF5D9E83A67338008735224FED024AF8C47BBD37442C7BDF`;
- QEMU: 11.0.0.

The repeatable proof wrapper is
`tools/Run-ManagedKernelPhase50ExtendedUnicodeProof.ps1`. It performed three
fresh Gate 4/QEMU dgram boots. All three reached `PASS_PHASE50`, emitted the
Phase 50 coverage/Latin-1/NBSP telemetry, presented the page through the GOP
framebuffer, and produced byte-identical screen pixels:

- resource SHA-256:
  `024F42459477DB695D9C742A6EA869270CC9349797996C47E5047747FEF0852A`;
- screen SHA-256:
  `4359B06D9037BD3C25B9638F4810E8032517A8970F33736957B09DFEB36C9789`;
- evidence directory:
  `artifacts\phase50-qemu-proof-final6`.

The same payload also passed the existing Phase 49 visible-page proof 3/3;
that regression evidence is in `artifacts\phase49-qemu-regression-final4`.

This phase does not claim full Unicode shaping, kerning, combining-mark
composition, Greek/Cyrillic embedding, emoji, or right-to-left text support.
Those remain explicit future scope rather than implicit fallback behavior.

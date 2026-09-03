# Managed Kernel Phase 45: Bounded Layout and Geometry Arena

Phase 45 is Outcome A: the managed-kernel browser pipeline now produces deterministic geometry after the Phase 43 document and Phase 44 computed-style stages. The implementation stops at layout. It does not paint, rasterize glyphs, decode images, or provide a browser UI.

The central invariant is that document complexity can exhaust an explicit arena, but it cannot turn layout into an unbounded allocation or recursive traversal:

```text
HTML bytes -> tokenizer -> bounded document -> CSS cascade -> computed styles
           -> fixed layout boxes/lines/fragments -> validated geometry/hash
```

## Starting point and audit

Phase 44 supplies the authoritative inputs:

* `src/ManagedKernel/ManagedHtmlTokenizer.cs` preserves bounded HTML token storage and text scalars.
* `src/ManagedKernel/ManagedHtmlDocument.cs` owns the bounded document/node/attribute/text representation.
* `src/ManagedKernel/ManagedCss.cs` parses selectors and typed CSS values, performs cascade/inheritance, and stores computed styles.
* `src/ManagedKernel/ManagedPhase43HtmlProof.cs` owns the streamed HTML proof and now invokes layout after CSS validation.

The writable repository was searched for Navigator layout, box-tree, block/inline, font-metric, table-layout, and painting implementations. No existing Navigator layout implementation was present to reuse or port. Consequently, Phase 45 deliberately does not claim semantic parity with a hosted Navigator engine and does not introduce a desktop browser dependency. The useful Phase 44 semantics are consumed directly: display, position, dimensions, margins, padding, border width, white-space, font properties, overflow, z-index, and inherited values.

This also avoids the unsuitable architecture the phase request called out: an allocation-heavy object graph with recursive traversal and layout/paint coupling. The minimum future rendering substrate is now present as source-node-indexed boxes, line origins, text fragments, content/border/padding rectangles, overflow metadata, z-index, and deterministic hashes.

## Public API and ownership

The implementation is `src/ManagedKernel/ManagedLayout.cs`, in namespace `GuideXOS.Net10.ManagedKernel`.

```csharp
var layout = new ManagedLayoutEngine(document, styles);
bool ok = layout.TryLayout(new ManagedLayoutViewport(800, 600));
if (ok && layout.Validate(out var validationFailure)) { /* inspect */ }
```

The main API is:

* `ManagedLayoutEngine(ManagedHtmlDocument, ManagedCssEngine)` using default capacities.
* `ManagedLayoutEngine(ManagedHtmlDocument, ManagedCssEngine, ManagedLayoutArenaOptions, IManagedLayoutTextMetrics?)` for explicit limits and a metrics provider.
* `TryLayout(int width, int height)` or `TryLayout(ManagedLayoutViewport)`.
* `Reset()` for deterministic full reuse; `Cancel()` for a bounded cancellation failure at the next safe check.
* `TryGetBox`, `TryGetLine`, `TryGetTextFragment`, and `TryGetBoxForNode` for copy-out/read-only inspection.
* `Validate(out ManagedLayoutValidationFailureReason)` for structural/debug validation.
* `TryCopyCanonicalLayoutHash(Span<byte>)` for the 32-byte semantic geometry digest.
* `Telemetry` and individual counters for bounded proof reporting.

The document and CSS engine are borrowed as immutable layout inputs. Layout owns its fixed arrays and never writes document text, nodes, attributes, or computed styles. `ManagedLayoutBox`, `ManagedLayoutLine`, and `ManagedLayoutTextFragment` are compact copy-safe value projections. Capacity arguments are checked in the constructor; a layout attempt reports failures through `FailureReason` and returns `false` rather than throwing for ordinary malformed/oversized layout input.

## Coordinate and viewport model

Geometry uses signed integer CSS pixels. Phase 44 fixed-point CSS lengths are converted deterministically: px values use the existing hundredths-of-a-pixel representation, percentages are resolved as `containing * value / 10000`, and `em`/`rem` use a 16 px root default (with inherited font size for `em`). Every relevant addition and positioned offset is checked against `[-1,000,000,000, +1,000,000,000]`; an out-of-range operation returns `GeometryOverflow`.

The viewport is explicit. The proof uses 800 x 600 CSS pixels (`0x320` x `0x258`), but the engine accepts alternate dimensions, including zero. The root box starts at `(0, 0)`, has the viewport width and height, and supplies the initial containing block. Document content extents may exceed the viewport and are reported separately.

## Layout box arena

The box arena contains no extra box for hidden or non-rendered subtrees. Each rendered source node gets one box in deterministic document order; text nodes, line breaks, and replaced placeholders use their own compact kinds. Parent/first-child/last-child/next-sibling indices retain the layout hierarchy without object references.

`ManagedLayoutBox` distinguishes:

* `Root`, `Block`, and `InlineContainer`;
* `Text`, `LineBreak`, and `Replaced` atomic/inline content;
* `Table`, `TableRow`, and `TableCell` classification placeholders.

The public packed record is 154 bytes (`Marshal.SizeOf<ManagedLayoutBox>()` in the host suite). The current naturally aligned backing record is approximately 160 bytes. It includes source index, kind, links, flags, z-index, border/padding/content rectangles, four-side edges, overflow extent, and clip rectangle. No raw pointers or struct padding are included in the canonical hash.

## Block flow and box model

Block layout is an iterative vertical normal-flow pass:

1. Resolve the child containing width and the four margin/padding sides plus uniform computed border width.
2. Resolve the content width, then clamp it by min/max width.
3. Place the border box below preceding in-flow content and enter the child with its content rectangle.
4. Accumulate child border height plus bottom margin into the parent cursor.
5. Resolve auto height from the cursor, or retain a definite height, then apply min/max height.

The box model is `content -> padding -> border -> margin`. Auto width fills available containing width after horizontal margins, padding, and border. Fixed px and safely resolvable percentage widths are supported. Auto margins resolve to zero in this bounded subset, and over-constrained values use deterministic clamping rather than browser-specific redistribution. Padding percentages resolve against containing width. Border style and color remain CSS metadata for a future painter; only border width affects Phase 45 geometry.

Vertical margin collapsing is intentionally not partially implemented. Adjacent margins are additive. Signed margins are retained and bounded; negative values cannot wrap coordinates. Parent/child collapse, clearance, floats, and other CSS formatting-context interactions are deferred.

Auto height comes from laid-out content. Fixed heights are honored, with min/max clamping. Percentage height resolves only when the containing height is definite; otherwise it follows the bounded auto-height fallback. No recursive percentage dependency is introduced.

## Hidden and non-rendered content

`display:none` skips the element and its entire subtree, increments `DisplayNoneSkips`, and creates no layout box or text measurement. Phase 44 UA non-rendered elements (`head`, `style`, `script`, `meta`, `link`, `title`, and `base`) are also excluded from the layout tree. The validator checks that a computed `display:none` element has no source-node mapping to a visible box.

## Inline text, whitespace, and lines

Inline descendants are consumed in source order within the current block. `span`, `a`, `strong`, `em`, and other inline containers do not force separate block lines. Each text fragment refers to the existing document text node and stores source offset/length, line index, rectangle, and text style; it never copies a string and never allocates per character.

`IManagedLayoutTextMetrics` is the small future-proof boundary between layout and fonts:

```csharp
bool TryMeasureScalar(uint scalar, in ManagedLayoutTextStyle style, out int advance);
int GetLineHeight(in ManagedLayoutTextStyle style);
```

The authoritative `ManagedDeterministicLayoutTextMetrics` provider is usable in NativeAOT and on the host. It measures ordinary scalars at `fontSize * 3 / 5`, ASCII space/tab at `fontSize / 2`, adds `max(1, fontSize / 20)` for weight >= 700, treats supplementary scalars as one scalar, and returns line height `fontSize * 5 / 4`. These integer formulas are intentionally synthetic; they prove layout ownership without an OS font API. Future real metrics must preserve the bounded interface and deterministic failure behavior.

The supported whitespace policy is deliberately small:

* `normal`: collapse ASCII HTML whitespace runs and allow word wrapping;
* `nowrap`: collapse whitespace and suppress soft wrapping;
* `pre`: preserve whitespace, honor LF and CRLF as one forced break, and suppress soft wrapping;
* `pre-wrap`: preserve whitespace and honor line breaks while allowing wrapping;
* `pre-line`: collapse ordinary whitespace but treat newline as a forced break.

Words/runs are measured by Unicode scalar and wrapped at available width. A long run is split by scalar when it cannot fit as a whole. This is not Unicode line breaking: shaping, bidi, grapheme segmentation, kerning, ligatures, hyphenation, and language-specific breaking are intentionally absent. `<br>` emits a forced line transition and does not require a visible painted rectangle.

`ManagedLayoutLine` is a persistent bounded record with owner box, origin/rectangle, and a range into the fragment arena. The packed public line record is 32 bytes. `ManagedLayoutTextFragment` is 46 bytes in the host layout check.

## Replaced, relative, absolute, fixed, and tables

`img`, `input`, `button`, `textarea`, `select`, and `embed` become deterministic replaced placeholders. CSS width/height are honored; auto dimensions use 32 x 24 px intrinsic fallback. The current document API does not make image decoding part of this phase. Replaced elements participate in inline line geometry.

Relative elements reserve their normal-flow space and receive the bounded visual offset from top/bottom and left/right. Absolute elements are removed from normal flow and are positioned relative to the nearest positioned ancestor, or the viewport when none exists. Fixed elements always use the viewport as containing block. Left/top and unambiguous right/bottom plus fixed dimensions are supported. z-index is retained but no stacking sort or painting is performed.

Phase 44 knows table display classifications, so the layout tree preserves `table`, `table-row`, and `table-cell` kinds. Phase 45 intentionally uses the regular bounded block-flow fallback for them. It does not claim equal-column sizing, intrinsic table sizing, colspan/rowspan, captions, or border-collapse. `TableColumnCapacity` and `TableColumnCapacityExceeded` reserve an explicit extension point; the current fallback does not allocate column scratch or reach that failure.

## Overflow and extents

Each box stores content geometry, overflow extent, and a clip rectangle. Visible overflow receives an effectively unbounded bounded clip rectangle; hidden/scroll/auto use the padding box as the current clip geometry. No scrollbars or scrolling interaction are implemented. Finalization unions descendant extents and sets `HasHorizontalOverflow`/`HasVerticalOverflow` flags. `DocumentContentWidth` and `DocumentContentHeight` are checked aggregate extents useful to a future scrollbar/layout consumer.

## Fixed limits and memory accounting

The defaults and hard maximums are:

| Arena/state | Default | Maximum | Current record/state estimate |
| --- | ---: | ---: | ---: |
| Layout boxes | 1,024 | 4,096 | 154 B public / ~160 B backing record |
| Lines | 2,048 | 8,192 | 32 B |
| Text fragments | 4,096 | 16,384 | 46 B |
| Layout traversal frames | 128 | 512 | ~40 B block frame plus 8 B inline frame |
| Table column capacity | 32 | 256 | reserved; no current scratch allocation |
| Coordinate magnitude | 1,000,000,000 | same | checked signed integer pixels |

The default engine allocates fixed arrays sized by the default capacities. Approximate array payload is:

* boxes: `1,024 * 160 = 163,840` bytes;
* lines: `2,048 * 32 = 65,536` bytes;
* fragments: `4,096 * 46 = 188,416` bytes;
* block traversal frames: `128 * 40 = 5,120` bytes;
* inline traversal frames: `128 * 8 = 1,024` bytes;
* Phase 43 node-to-box map: `1,024 * 4 = 4,096` bytes;
* per-box flow coordinates: `2 * 1,024 * 4 = 8,192` bytes;
* canonical digest storage: 32 bytes.

That is approximately 436,256 bytes of Phase 45 fixed backing arrays, before CLR array/object headers and the small hash state. The maximum-capacity equivalent is approximately 1,732,640 bytes. The existing Phase 43 document plus Phase 44 CSS estimate is 820,368 bytes (~801.14 KiB), so the default document + CSS + layout backing arrays are approximately 1,256,624 bytes (~1.20 MiB), excluding object headers/hash state. Adding the previously reported active pipeline estimate of 830,487 bytes gives approximately 1,266,743 bytes (~1.21 MiB), still excluding lower TLS/network state and runtime overhead. There is no document-sized temporary layout array or per-character allocation.

## Failure taxonomy and reset

`ManagedLayoutFailureReason` distinguishes invalid document/styles/viewport, each primary arena (`LayoutBoxCapacityExceeded`, `LineCapacityExceeded`, `TextFragmentCapacityExceeded`, `TraversalStackCapacityExceeded`, and the reserved table-column failure), `GeometryOverflow`, unsupported layout values, text measurement failure, invalid state, and cancellation. The first failure is retained for diagnosis. A failed layout does not fall back to heap growth.

`Reset()` clears every geometry arena, source mapping, traversal array, flow coordinate, counter, overflow state, digest, and status flag. Reusing the same engine with the same document, styles, viewport, and metrics produces the same canonical layout hash. Changing the viewport deterministically changes geometry while avoiding stale boxes.

## Validation and canonical hash

The validator checks box and source ranges, root/index bounds, parent/child/sibling consistency and cycles, rectangle non-negativity, fragment source ranges, line references, arena ranges, and hidden-node absence. It is a host/debug API and the guest proof reports layout validation before publishing success.

The canonical SHA-256 begins with the domain separator `GXOS-P45\0`, then hashes viewport and document extents, every box’s semantic source/kind/links/flags/geometry/edges/z-index, every line, and every fragment/style field in deterministic arena order. It excludes pointers, object identities, addresses, and struct padding. The proof emits the digest as eight big-endian 32-bit words.

## Complexity and bounded work

Box-tree construction is iterative and visits each document node once: O(nodes), with O(traversal-capacity) explicit stack storage. Parent child insertion is O(1). Block flow maintains a parent cursor and does not rescan all prior boxes. Inline layout is O(text scalars plus emitted runs); a long word may be measured once for total width and again for bounded scalar splitting. Line and fragment emission is capped by their arenas. Positioned elements are visited once after normal flow, so the normal case is O(boxes). The current table fallback is ordinary block flow; no table-column algorithm is present.

## Host coverage

`src/ManagedKernelPhase45HostTests/Program.cs` contains 292 deterministic cases. It covers root/viewport, block stacking, width/height/min/max, signed margins, padding/border, display:none and UA-hidden elements, deterministic Unicode scalar metrics, normal/nowrap/pre/pre-wrap/pre-line whitespace, CRLF, wrapping, nested inline source order, `<br>`, replaced placeholders, relative/absolute/fixed positioning, overflow, table classification fallback, all exercised arena failures, cancellation, reset/relayout/hash stability, style integration, and validator behavior.

Run it with:

```powershell
tools\Run-ManagedKernelPhase45HostTests.ps1
```

The acceptance output is:

```text
MANAGED_KERNEL_PHASE45_SIZES rect=16 edges=16 box=154 line=32 fragment=46 viewport=8
MANAGED_KERNEL_PHASE45_HOST_TESTS_PASS cases=292
```

## NativeAOT and QEMU proof

The end-to-end wrapper is `tools/Run-ManagedKernelPhase45LayoutProof.ps1`. It builds the NativeAOT managed payload, stages the Gate 4 harness, serves a deterministic gzip UTF-8 HTML/CSS fixture at `https://www.example.com/phase45/gzip`, and performs fresh QEMU boots. The fixture exercises nested blocks, box model, percentage/min/max width, inherited font size, inline/strong text, `<br>`, pre-wrap text, image placeholder, display:none, overflow, relative/absolute/fixed positioning, and table display types.

Final positive evidence is under `artifacts/phase45-layout-final`:

* QEMU: `C:\Program Files\qemu\qemu-system-x86_64.exe`, version 11.0.0;
* NativeAOT SDK fallback: .NET SDK 10.0.400, MSBuild 18.9.6;
* payload: 2,324,992 bytes;
* payload SHA-256: `C745002C2F1509A62CC56C5A7858A92A671DD9A6CFBDDB00363EF7CBB4F5BFCB`;
* resource: 1,287 decoded UTF-8 bytes, SHA-256 `929D8C15F1B85C6E21AB1FE28C8213E67C59B5B9E0DCC06C359080F0BFB900E3`;
* encoded gzip response: 685 bytes;
* fresh boots: 3/3 `PASS_PHASE45`;
* serial hashes: `2A1EEBE249F0D2FD4448359F596B5C8EB1CDFF466E3AA85170F0A1F262C71CDA`, `49C155833A0129CDC4FC9394DEA5B32E64E5E169EF05894D4993959ADD06F70C`, and `5562C8B0DEDB900B752BEE1C86C04DC468F461BD1447D4C66A0C50502D7E0CD9`.

The representative run reports:

| Metric | Value |
| --- | ---: |
| HTTP status | 200 |
| content type / charset / encoding | HTML UTF-8 / gzip |
| Unicode scalars | 873 |
| HTML tokens / nodes / elements | 61 / 38 / 22 |
| CSS rules / selector matches / elements styled | 12 / 13 / 22 |
| viewport | 800 x 600 |
| layout boxes / block boxes / inline-text boxes | 30 / 14 / 15 |
| lines / text fragments | 13 / 44 |
| measured scalars / soft wraps / forced breaks | 206 / 3 / 2 |
| display:none skips / positioned boxes | 2 / 3 |
| horizontal / vertical overflow boxes | 5 / 9 |
| peak box / line / fragment / traversal | 30 / 13 / 44 / 10 |
| document content width / height | 808 / 639 |

Selected geometry records are source-node-indexed. In the fixture, source node 8 is `body`, node 9 is `#main`, and node 13 is the `.note` paragraph:

| Element | Border box `(x,y,w,h)` | Content box `(x,y,w,h)` |
| --- | --- | --- |
| body | `(8,8,784,243)` | `(12,12,776,235)` |
| `#main` | `(28,22,606,211)` | `(41,32,582,189)` |
| `.note` | `(41,57,582,60)` | `(41,57,582,60)` |

The hidden subtree has no box mapping. The document, style, and canonical layout hashes are:

```text
document: DE9619BE670C1E0F1470E8A88C4BA0098B10DDB2CBD39E83D229859C5D0CD3BD
style:    1754EE62C0737C81E0B60EE0C403E237121559C0FBA46D5DCA5C199C52D170A8
layout:   FA7E08B217370CCCF4AAC5AF12A9A854F43F3FBAC9045D988DBB31EAFA32632E
```

The layout-capacity control uses the same fixture, derives the exact positive box count (`29` usable boxes after reserving the root), and retries with that capacity. Final evidence is under `artifacts/phase45-layout-capacity-final2` and records:

* one fresh boot: `NEGATIVE_PASS_PHASE45`;
* `MANAGED_HTTPS_PHASE45_LAYOUT_CAPACITY=0x1D`;
* `MANAGED_HTTPS_PHASE45_LAYOUT_FAILURE=0x4` (`LayoutBoxCapacityExceeded`);
* capacity validated and negative-pass markers;
* kernel phase-14 driver-start failure/blocked markers;
* no layout/resource/kernel success marker, CPU exception, page fault, or unexpected import marker.

The final payload passed the Phase 39 resource proof in 3/3, Phase 40 gzip proof in 3/3, Phase 41 text proof in 3/3 boots (with the optional packet-filter dump disabled), Phase 42 tokenizer proof in 3/3, and Phase 43 document proof in 3/3. Evidence is under `artifacts/phase45-phase39-regression`, `artifacts/phase45-phase40-regression`, `artifacts/phase45-phase41-regression-no-filter2`, `artifacts/phase45-phase42-regression-rerun`, and `artifacts/phase45-phase43-regression`. It also passed the Phase 44 CSS proof regression in 3/3 fresh boots under `artifacts/phase45-phase44-regression-final`. The historical Phase 22–44 host aggregate is 13,317 cases. Adding the 292 Phase 45 cases gives 13,609; Phase 44 itself was 66 cases.

## Changed files and commands

Implementation and integration:

* `src/ManagedKernel/ManagedLayout.cs` — bounded geometry engine, public records, metrics, telemetry, validator, and hash;
* `src/ManagedKernelPhase45HostTests/ManagedKernelPhase45HostTests.csproj` and `Program.cs` — host acceptance suite;
* `src/ManagedKernel/ManagedPhase43HtmlProof.cs` — layout invocation and bounded proof markers;
* `src/ManagedKernel/ManagedIpv4Layer.cs`, `ManagedE1000Driver.cs`, `ManagedEthernetLayer.cs`, and `ManagedKernel.cs` — Phase 45 mode/capacity routing and lifecycle;
* `src/Gate4Harness/gate4_loader.c` and `tools/Build-Gate4Harness.ps1` — Phase 45 harness selection;
* `tools/Run-ManagedKernelPhase11FreshBoots.ps1` — deterministic gzip fixture, exchange, positive/capacity validation;
* `tools/Run-ManagedKernelPhase45HostTests.ps1` and `tools/Run-ManagedKernelPhase45LayoutProof.ps1` — reproducible host and QEMU entry points.

Useful commands:

```powershell
.\tools\Run-ManagedKernelPhase45HostTests.ps1
.\tools\Run-ManagedKernelPhase45LayoutProof.ps1 -OutputDirectory .\artifacts\phase45-layout-final
.\tools\Run-ManagedKernelPhase45LayoutProof.ps1 -CapacityControl -RunCount 1 -OutputDirectory .\artifacts\phase45-layout-capacity-final
git diff --check
```

## Limitations and Phase 46

The remaining deliberate gaps are real browser semantics: full table layout and column scratch, flex/grid/floats, complex margin collapse, transforms, animations, external stylesheets, image decoding, font shaping, bidi, Unicode line breaking, selection/hit testing, scrolling/scrollbars, DOM mutation and incremental reflow, painting, and compositor integration. Unsupported future CSS values must remain explicit fallbacks or typed failures; they must not widen the arena contract.

Phase 46 should consume this geometry without changing the bounded ownership model. The highest-value next steps are a bounded paint/clip traversal, real guideXOS font metrics behind `IManagedLayoutTextMetrics`, a deliberate equal-column table subset with tested column capacity, and then narrowly scoped flex/grid or hit-test work. Every addition should extend the same telemetry, validator, deterministic hash, and capacity-negative proof pattern.

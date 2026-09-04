# Managed Kernel Phase 46: Bounded Display List and Paint Commands

Phase 46 is the first rendering-facing stage after Phase 45 layout. It converts validated managed geometry into a bounded semantic command stream. It does not rasterize, call a window system, resolve an OS font, fetch an image, or write a framebuffer.

```text
HTML bytes -> bounded document -> computed CSS -> layout boxes/lines/fragments
           -> fixed paint arenas -> validated commands -> canonical paint hash
```

## Scope and audit

The implementation is `src/ManagedKernel/ManagedPaint.cs`, in namespace `GuideXOS.Net10.ManagedKernel`. The existing native Navigator/compositor sources were audited before implementation. They use host containers, retained `WinInfo` vectors, GDI drawing, bitmap/font helpers, and pointer-oriented state, so they are not a safe NativeAOT managed-kernel dependency. Phase 46 therefore uses a small semantic display-list boundary over the existing managed Phase 45 layout records.

The painter borrows the document, computed styles, and layout as immutable inputs. It owns fixed arrays only. Every command refers to a source node, source box, text offset/length, line, or image element by bounded integer index; no command owns a string or pointer.

## API and fixed arenas

```csharp
var paint = new ManagedPaintEngine(layout);
bool ok = paint.TryGenerate(800, 600, scrollX: 0, scrollY: 0);
if (ok && paint.Validate(out var failure)) { /* consume commands */ }
```

The public surface includes:

* `ManagedPaintCommand`, a packed 98-byte tagged value record;
* `ManagedPaintCommandKind`: `BeginClip`, `EndClip`, `FillRectangle`, `BorderRectangle`, `TextRun`, and `ImagePlaceholder`;
* `ManagedPaintArenaOptions` for command, clip-depth, and ordering capacities;
* `TryGenerate`, `TryGetCommand`, `TryCopyCanonicalPaintHash`, `Validate`, `Reset`, `Cancel`, and deterministic `CancelAfterCommands`;
* `ManagedPaintTelemetry` and `ManagedPaintFailureReason` for bounded proof reporting.

Default capacities are 12,288 commands, 64 clip levels, and 4,096 ordering entries. Hard maxima are 32,768 commands and 256 clip levels. The constructor rejects invalid capacities. A generation that would exceed the command arena performs a count-only preflight, leaves `CommandsEmitted == 0`, reports `PaintCommandCapacityExceeded`, and does not overwrite memory.

The fixed backing-array estimate for default capacities is:

| Array/state | Count | Record size | Bytes |
| --- | ---: | ---: | ---: |
| commands | 12,288 | 98 | 1,204,224 |
| active clip rectangles | 64 | 16 | 1,024 |
| active clip path | 64 | 4 | 256 |
| clip scratch path | 64 | 4 | 256 |
| stable order | 4,096 | 4 | 16,384 |
| paint digest | 1 | 32 | 32 |

The explicit payload is 1,222,176 bytes, approximately 1.17 MiB, before CLR array headers and the existing document/CSS/layout/runtime state. There is no page-sized temporary command array and no per-character command allocation.

## Primitive semantics

The engine walks Phase 45 boxes in a stable order and emits compact semantic primitives:

* Backgrounds become `FillRectangle` over the border box. Transparent colors are skipped and counted. Opacity multiplies the ARGB alpha channel using integer arithmetic after composing the local opacity with every ancestor opacity.
* Borders become one `BorderRectangle` retaining four edge widths, border style, color, opacity, and source box. Solid borders are represented directly. Dashed and dotted styles are retained as metadata and counted as unsupported rasterization styles; they do not silently become a different visual primitive.
* Each layout text fragment becomes one `TextRun`. The command stores source text node, offset, length, line index, baseline, color, font size/weight/style, and the deterministic `DefaultUi` font identifier. Text is never copied.
* An `img` replaced fragment becomes an `ImagePlaceholder` with source element and geometry. The placeholder uses a deterministic gray fallback color; image bytes are not fetched or decoded.

The command record also carries the transformed rectangle, active clip rectangle, effective opacity, effective z-index, clip depth, and positioned flag. Geometry is translated by the requested signed scroll offset with checked coordinate arithmetic.

## Visibility, clipping, z-order, and scrolling

`display:none` has no layout box and is counted by the paint telemetry scan. `visibility:hidden` and `visibility:collapse` retain layout but suppress the box’s own visual primitives; descendants continue through the flat layout walk so explicitly visible descendants remain representable. Later visible siblings are not suppressed.

The root always opens one viewport clip. Overflow `hidden`, `scroll`, and `auto` add the box’s layout clip to a fixed clip stack. Clip transitions emit `BeginClip`/`EndClip` commands, intersect each new clip with the active clip, and are balanced at finalization. Primitive commands are culled when wholly outside the active clip; partially intersecting rectangles remain semantic rectangles with their active `ClipRect`, allowing a future rasterizer to perform the actual scissor. Normal-flow, relative, and document-positioned absolute geometry subtracts the requested scroll offset. A fixed box and its descendants use viewport coordinates, and fixed descendant clip paths stop at the fixed containing boundary rather than inheriting document clips above it.

Stable z buckets are negative positioned, normal flow, and positive positioned. Within a nonzero bucket, effective z-index magnitude is ordered before source/layout order; source/layout order remains the deterministic tie-breaker. Descendants inherit the nearest positioned ancestor’s effective z-index, and positioned commands are marked. No sorting allocation, recursive stacking context, or framebuffer operation is introduced.

## Reset, cancellation, and validation

`Reset()` clears all command, clip, path, ordering, counters, status, and digest state. `Cancel()` causes the next generation to fail as `Cancelled`; `CancelAfterCommands(n)` provides a deterministic cooperative checkpoint for tests. Count-only capacity preflight is not cancellable and does not publish a partial list. Any failed preflight clears the command arena, count, hash availability, telemetry counters, clip depth, ordering scratch, and planned count before publishing the current failure reason/state; a prior successful list is never exposed as the failed attempt.

`ManagedPaintValidator` checks command kinds, bounded rectangles, source box/node ranges and pairing, opacity range, flags, text source ranges and text-node kind, image source kind, border metadata, clip-depth balance, and monotonic primitive ordering including z-index magnitude. It rejects unmatched clips, bad text references, invalid image references, and ordering regressions. The guest proof validates the complete list before reporting success.

The canonical SHA-256 uses domain separator `GXOS-P46\0`, viewport and scroll values, command count, and explicit big-endian fields for every command. It excludes object identity, pointers, array addresses, and struct padding. The proof emits the digest as eight big-endian words, providing a stable semantic identity for the display list.

## Host coverage

`src/ManagedKernelPhase46HostTests/Program.cs` exercises 1,846 assertions, including:

* empty/root clips and transparent-background skipping;
* fill, border, text, and image-placeholder command references;
* opacity/ARGB handling, hidden versus `display:none`, and later siblings;
* nested overflow clipping, clip intersection, depth-capacity failure, and reset;
* negative/normal/positive z buckets, positioned flags, z-index magnitude ordering, scrolling, fixed viewport anchoring, and layout immutability;
* exact command capacity, count-minus-one capacity failure, cancellation before/after a deterministic checkpoint, regeneration/hash stability; and
* synthetic validator failures for unbalanced clips, invalid text references, invalid source boxes, invalid source-box/node pairing, and z-order regressions;
* ancestor opacity combinations for backgrounds, borders, text, and images, including three levels, zero, sibling isolation, exact alpha packing, and reset; and
* successful-generation/preflight-failure/success recovery, clip/order scratch clearing, hash invalidation, normal scrolling, fixed background/border/text/descendant invariance, and repeat determinism.

Run it with:

```powershell
.\tools\Run-ManagedKernelPhase46HostTests.ps1
```

Acceptance output:

```text
MANAGED_KERNEL_PHASE46_SIZES command=98 rect=16 edges=16 options=12
MANAGED_KERNEL_PHASE46_HOST_TESTS_PASS cases=1846
```

## NativeAOT and QEMU proof

`tools/Run-ManagedKernelPhase46DisplayListProof.ps1` builds the NativeAOT payload, stages Gate 4, serves a deterministic gzip UTF-8 HTML/CSS fixture at `https://www.example.com/phase46/gzip`, and runs fresh QEMU boots using the existing dgram E1000 transport harness. The fixture includes overflow clipping, explicit border, ancestor and nested opacity, hidden and display-none content, image replacement, fixed negative-z content with a border, absolute positive-z content, inline text, line breaks, pre-wrap text, and table classifications. The guest proof also regenerates the same layout at `scrollY=37` and inspects ordinary, fixed, and nested-opacity commands directly.

Final positive evidence is under `artifacts/phase46r-qemu-positive-final3`:

* QEMU: `C:\Program Files\qemu\qemu-system-x86_64.exe`, version 11.0.0;
* NativeAOT toolchain: .NET SDK 10.0.400 fallback, MSBuild 18.9.6;
* payload: 2,388,480 bytes;
* payload SHA-256: `C83F147BCE52DF17CA8265CA8327935040023984C2D9082192DD4394A3A5B463`;
* decoded resource: 1,574 bytes, SHA-256 `88F996E3FBC184B7725B7D5D347B283C3E87F006C5912352D95EA2F84F30754B`;
* encoded gzip response: 776 bytes;
* fresh boots: 3/3 `PASS_PHASE46`;
* representative command count: 59 of 12,288 capacity;
* dedicated fixed-scroll and nested-opacity proofs: pass on all 3 boots; and
* no CPU exception, page fault, or unexpected-import marker.

Representative positive telemetry:

| Metric | Value |
| --- | ---: |
| HTTP status | 200 |
| layout boxes / lines / text fragments | 33 / 16 / 51 |
| fill / border / text / image commands | 3 / 2 / 47 / 1 |
| clip pushes / pops / peak depth | 2 / 3 / 3 |
| transparent skips / offscreen culled | 14 / 0 |
| positioned commands | 4 |
| negative / normal / positive z counts | 2 / 3 / 28 |

The representative hashes are:

```text
document: 9C10390C80349958863E40612E8F8E7755F14CB4B461597073F7782A5C620882
style:    109E9C5887415F84BEB090216DC906F3505EC3905AD303AF71C424FE06DB63B4
layout:   5BFA6AAA55309A627D06770FD357E0550AB572E0A9BCE0673BCF06B1828D1089
paint:    677EAC49EC7EC785A3B11F6B494B43B14CB8ED25C4B7666C6B2529BA79F8BF39
```

The prior capacity proof is under `artifacts/phase46-proof-capacity-final2` and recorded the count-minus-one `PaintCommandCapacityExceeded` (`0x4`) control. A fresh corrective rerun was started under `artifacts/phase46r-qemu-capacity-final`; it stalled before serial boot output and was terminated after verifying the exact QEMU command line. The host capacity/cancellation/reset regressions remain passing, and the required positive NativeAOT/QEMU proof is unaffected.

Useful commands:

```powershell
.\tools\Run-ManagedKernelPhase46HostTests.ps1
.\tools\Run-ManagedKernelPhase46DisplayListProof.ps1 -OutputDirectory .\artifacts\phase46-proof-positive -RunCount 3
.\tools\Run-ManagedKernelPhase46DisplayListProof.ps1 -CapacityControl -RunCount 1 -OutputDirectory .\artifacts\phase46-proof-capacity
git diff --check
```

## Phase 46R corrective review

This corrective pass reviewed baseline `b9f69f9` (`Bounded Display List and Deterministic Paint Command Generation`). The original review metadata was not recoverable from local Git notes/refs, repository review endpoints, or the current task history. A focused audit of the changed Phase 46 code reconstructed the two remaining actionable findings from observable behavior; both are included below and have direct regressions.

1. **Ancestor opacity composition (P1).** The painter used only each box’s local computed opacity when storing command metadata and alpha-packing colors. It now composes the local value with every ancestor element opacity for backgrounds, borders, text, image placeholders, and positioned descendants. The fixed representation is `0..10,000`; starting with local opacity, each ancestor is applied as `next = floor(current * ancestor / 10,000)`, truncating toward zero at each step. Alpha packing uses the same floor rule: `packedAlpha = floor(sourceAlpha * effectiveOpacity / 10,000)`. Thus `.5 × .5` is `2,500`, `.5 × .3333` is `1,666`, three `.5` levels are `1,250`, and `0xFF123456` at `2,500` becomes `0x3F123456`. A small CSS decimal-parser correction accepts the literal zero required by the zero-opacity regression without changing nonzero parsing semantics.

2. **Fixed-position scroll translation (P1).** The original paint transform subtracted document scroll from every command. Transforms now inspect the layout ownership chain: ordinary, relative, and document-positioned absolute boxes subtract scroll; a fixed box and all descendants of its fixed boundary use zero scroll. Fixed descendant clip paths retain clips through the fixed boundary, but do not inherit document clips above it; the fixed box’s own overflow clip does not clip its own background/border. Host and guest proofs compare scroll zero/nonzero for normal content, fixed background, fixed border, fixed text, fixed descendants, and clips.

3. **Failed preflight state (P1).** Count-only generation failures could leave count-only usage, telemetry, clip/order scratch, planned count, or digest state visible after a prior success. `FailPreflight` now clears commands, all pass counters, clip stack/path scratch, order scratch, z counts, planned count, generated/hash state, and digest before publishing the failure reason and `Failed`/`Cancelled` state. A failed attempt therefore reports zero commands and no hash; a later valid generation is deterministic.

4. **Z-index magnitude inside sign buckets (audit finding).** The original stable order compared only negative/normal/positive sign buckets, so `z-index:-1` could precede `-2` and `1` could precede `2`. The bounded insertion comparator and validator now compare effective z-index magnitude within nonzero buckets, then source-box index as the deterministic tie-breaker. This remains the Phase 46 simplified ordering model and does not add CSS stacking-context compositing.

5. **Source-box/source-node validator pairing (audit finding).** The validator checked that source indices were individually in range but accepted a command pairing a valid box with a different valid node. It now requires `sourceBox.SourceNodeIndex == command.SourceNodeIndex` when both are present. This catches a future rasterizer being handed a geometrically valid command attributed to the wrong DOM node.

New targeted host coverage includes opacity combinations `1×1`, `.5×1`, `1×.5`, `.5×.5`, three nested `.5` levels, zero ancestor suppression, background/border/text/image exact metadata and alpha, sibling isolation, and reset; normal versus fixed scroll at both axes; fixed text/background/border/descendant clipping and hash determinism; successful generation → invalid-viewport preflight failure → success; invalid-document failure; scratch/telemetry/hash clearing; z-index magnitude; and source-box/node pairing. The Phase 46 suite increased from 709 to 1,846 cases. Phase 45 and Phase 44 host regressions pass at 292 and 66 cases respectively.

The guest Phase 46 proof now performs direct command inspection for the same layout at `scrollY=0` and `scrollY=37`, and emits `FIXED_SCROLL_PROOF_PASS` plus `NESTED_OPACITY_PROOF_PASS` with values `normal-document-scroll-fixed-viewport` and `2500-0x3F123456`. Fresh QEMU evidence is `artifacts/phase46r-qemu-positive-final3` (3/3), with final-payload Phase 45 evidence in `artifacts/phase46r-qemu-phase45-final` (3/3) and Phase 44 evidence in `artifacts/phase46r-qemu-phase44-final` (3/3). The optional capacity-negative run was started under `artifacts/phase46r-qemu-capacity-final` but stalled before serial boot output and was terminated after verifying its exact QEMU command line; the required positive proof and host capacity regressions pass.

The prior representative Phase 46 display hash was `6A4458CFBE4B83E26F1561E74B48E9631223C20E33291B59E2C1820B42E3FEC2`. The corrected representative hash is `677EAC49EC7EC785A3B11F6B494B43B14CB8ED25C4B7666C6B2529BA79F8BF39`. The fresh QEMU semantic fixture also added fixed-border and nested-opacity controls, so its resource/document/style/layout hashes changed to the values recorded above; the Phase 46 implementation fixes themselves affect the paint command stream, while Phase 44/45 semantic inputs remain unchanged. Phase 47 rasterization was not started.

## Integration and limitations

Phase 46 is selected by managed boot stages 14 (positive) and 15 (capacity negative), Gate 4 macros `GXOS_ENABLE_MANAGED_KERNEL_PHASE46` and `GXOS_ENABLE_MANAGED_KERNEL_PHASE46_CAPACITY`, and the shared fresh-boot runner switches `-EnablePhase46Protocol` and `-EnablePhase46CapacityControl`. Phase 45 routing remains unchanged when those switches are absent.

This phase intentionally does not claim CSS stacking-context parity, gradients, shadows, border-radius, SVG/canvas, real font shaping, bidi, glyph rasterization, image decode, scrollbar painting, compositing, or framebuffer presentation. These are Phase 47 candidates. The current contract is the bounded, deterministic semantic command stream that a later renderer can consume without reopening the NativeAOT memory and ownership problem.

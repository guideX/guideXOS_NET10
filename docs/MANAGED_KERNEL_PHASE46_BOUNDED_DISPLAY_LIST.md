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

* Backgrounds become `FillRectangle` over the border box. Transparent colors are skipped and counted. Opacity multiplies the ARGB alpha channel using integer arithmetic.
* Borders become one `BorderRectangle` retaining four edge widths, border style, color, opacity, and source box. Solid borders are represented directly. Dashed and dotted styles are retained as metadata and counted as unsupported rasterization styles; they do not silently become a different visual primitive.
* Each layout text fragment becomes one `TextRun`. The command stores source text node, offset, length, line index, baseline, color, font size/weight/style, and the deterministic `DefaultUi` font identifier. Text is never copied.
* An `img` replaced fragment becomes an `ImagePlaceholder` with source element and geometry. The placeholder uses a deterministic gray fallback color; image bytes are not fetched or decoded.

The command record also carries the transformed rectangle, active clip rectangle, effective opacity, effective z-index, clip depth, and positioned flag. Geometry is translated by the requested signed scroll offset with checked coordinate arithmetic.

## Visibility, clipping, z-order, and scrolling

`display:none` has no layout box and is counted by the paint telemetry scan. `visibility:hidden` and `visibility:collapse` retain layout but suppress the box’s own visual primitives; descendants continue through the flat layout walk so explicitly visible descendants remain representable. Later visible siblings are not suppressed.

The root always opens one viewport clip. Overflow `hidden`, `scroll`, and `auto` add the box’s layout clip to a fixed clip stack. Clip transitions emit `BeginClip`/`EndClip` commands, intersect each new clip with the active clip, and are balanced at finalization. Primitive commands are culled when wholly outside the active clip; partially intersecting rectangles remain semantic rectangles with their active `ClipRect`, allowing a future rasterizer to perform the actual scissor.

Stable z buckets are negative positioned, normal flow, and positive positioned. Source/layout order is stable within each bucket. Descendants inherit the nearest positioned ancestor’s effective z-index, and positioned commands are marked. No sorting allocation, recursive stacking context, or framebuffer operation is introduced.

## Reset, cancellation, and validation

`Reset()` clears all command, clip, path, ordering, counters, status, and digest state. `Cancel()` causes the next generation to fail as `Cancelled`; `CancelAfterCommands(n)` provides a deterministic cooperative checkpoint for tests. Count-only capacity preflight is not cancellable and does not publish a partial list.

`ManagedPaintValidator` checks command kinds, bounded rectangles, source box/node ranges, text source ranges and text-node kind, image source kind, border metadata, clip-depth balance, and monotonic primitive ordering. It rejects unmatched clips, bad text references, invalid image references, and ordering regressions. The guest proof validates the complete list before reporting success.

The canonical SHA-256 uses domain separator `GXOS-P46\0`, viewport and scroll values, command count, and explicit big-endian fields for every command. It excludes object identity, pointers, array addresses, and struct padding. The proof emits the digest as eight big-endian words, providing a stable semantic identity for the display list.

## Host coverage

`src/ManagedKernelPhase46HostTests/Program.cs` exercises 709 assertions, including:

* empty/root clips and transparent-background skipping;
* fill, border, text, and image-placeholder command references;
* opacity/ARGB handling, hidden versus `display:none`, and later siblings;
* nested overflow clipping, clip intersection, depth-capacity failure, and reset;
* negative/normal/positive z buckets, positioned flags, stable ordering, scrolling, and layout immutability;
* exact command capacity, count-minus-one capacity failure, cancellation before/after a deterministic checkpoint, regeneration/hash stability; and
* synthetic validator failures for unbalanced clips, invalid text references, and invalid source boxes.

Run it with:

```powershell
.\tools\Run-ManagedKernelPhase46HostTests.ps1
```

Acceptance output:

```text
MANAGED_KERNEL_PHASE46_SIZES command=98 rect=16 edges=16 options=12
MANAGED_KERNEL_PHASE46_HOST_TESTS_PASS cases=709
```

## NativeAOT and QEMU proof

`tools/Run-ManagedKernelPhase46DisplayListProof.ps1` builds the NativeAOT payload, stages Gate 4, serves a deterministic gzip UTF-8 HTML/CSS fixture at `https://www.example.com/phase46/gzip`, and runs fresh QEMU boots using the existing dgram E1000 transport harness. The fixture includes overflow clipping, explicit border, opacity, hidden and display-none content, image replacement, absolute negative/positive z-index content, inline text, line breaks, pre-wrap text, and table classifications.

Final positive evidence is under `artifacts/phase46-proof-positive-final3`:

* QEMU: `C:\Program Files\qemu\qemu-system-x86_64.exe`, version 11.0.0;
* NativeAOT toolchain: .NET SDK 10.0.400 fallback, MSBuild 18.9.6;
* payload: 2,383,872 bytes;
* payload SHA-256: `C94B4EE610527A958EC57F270261F0EA38F16ECB0F18129DF5755759B6ADBC13`;
* decoded resource: 1,486 bytes, SHA-256 `C05ACC69AA37C3AA16514660E72C4CC9D1CC47C9E0656B5BE493996103121554`;
* encoded gzip response: 751 bytes;
* fresh boots: 3/3 `PASS_PHASE46`;
* representative command count: 61 of 12,288 capacity; and
* no CPU exception, page fault, or unexpected-import marker.

Representative positive telemetry:

| Metric | Value |
| --- | ---: |
| HTTP status | 200 |
| layout boxes / lines / text fragments | 33 / 16 / 51 |
| fill / border / text / image commands | 2 / 1 / 47 / 1 |
| clip pushes / pops / peak depth | 4 / 5 / 3 |
| transparent skips / offscreen culled | 15 / 0 |
| positioned commands | 3 |
| negative / normal / positive z counts | 2 / 3 / 28 |

The representative hashes are:

```text
document: 39DB7A6DE1F4DC674CEB635EBABBDD0038E2EE87F832F1E958DD18D207064124
style:    B9E20AEB930A01A6F7FE7A6D9AF235A2499074F36203E90D9B580F1F30D3DDAE
layout:   E574A928EAB5C44CF57F1C25CE8D087B3D59030BA11EB5FFB20405EA5F3D3FE9
paint:    6A4458CFBE4B83E26F1561E74B48E9631223C20E33291B59E2C1820B42E3FEC2
```

The capacity proof is under `artifacts/phase46-proof-capacity-final2` and runs one fresh boot. It derives the positive command count and retries with capacity 60 (`0x3C`), one below the 61-command plan. Evidence reports `PaintCommandCapacityExceeded` (`0x4`), `PAINT_CAPACITY_CONTROL_VALIDATED`, and `PAINT_CAPACITY_NEGATIVE_PASS`, followed by the expected driver-start failure/blocked markers. It emits no resource or kernel Phase 46 success marker and no machine-fault marker.

Useful commands:

```powershell
.\tools\Run-ManagedKernelPhase46HostTests.ps1
.\tools\Run-ManagedKernelPhase46DisplayListProof.ps1 -OutputDirectory .\artifacts\phase46-proof-positive -RunCount 3
.\tools\Run-ManagedKernelPhase46DisplayListProof.ps1 -CapacityControl -RunCount 1 -OutputDirectory .\artifacts\phase46-proof-capacity
git diff --check
```

## Integration and limitations

Phase 46 is selected by managed boot stages 14 (positive) and 15 (capacity negative), Gate 4 macros `GXOS_ENABLE_MANAGED_KERNEL_PHASE46` and `GXOS_ENABLE_MANAGED_KERNEL_PHASE46_CAPACITY`, and the shared fresh-boot runner switches `-EnablePhase46Protocol` and `-EnablePhase46CapacityControl`. Phase 45 routing remains unchanged when those switches are absent.

This phase intentionally does not claim CSS stacking-context parity, gradients, shadows, border-radius, SVG/canvas, real font shaping, bidi, glyph rasterization, image decode, scrollbar painting, compositing, or framebuffer presentation. These are Phase 47 candidates. The current contract is the bounded, deterministic semantic command stream that a later renderer can consume without reopening the NativeAOT memory and ownership problem.

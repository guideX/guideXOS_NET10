# Managed Kernel Phase 49 — Bounded GOP Framebuffer Presentation

Phase 49 completes the first visible managed web-page path. The existing
Phase 48 bounded HTML/CSS/layout/paint/font/raster pipeline now presents its
caller-owned 160x180 ARGB8888 framebuffer into the UEFI GOP framebuffer that
Gate 4 acquired before `ExitBootServices`. The presenter is bounded, direct,
format-aware, and auditable; it does not introduce a compositor, window
manager, page-table mutation, runtime font loader, or unbounded allocation.

## Handoff and ABI

The Gate 4 loader resolves GOP through `LocateProtocol` while Boot Services
are alive, copies the immutable mode descriptor into the existing boot-info
handoff, and calls the managed install export after managed startup. No GOP
protocol call occurs after `ExitBootServices`; managed code receives only the
descriptor and an identity-mapped physical framebuffer range.

The packed `GxManagedKernelFramebufferV1` descriptor is 64 bytes:

| Field | Meaning |
| --- | --- |
| `Size`, `AbiVersion` | descriptor versioning (`64`, ABI `1`) |
| `FramebufferBase`, `FramebufferSize` | physical byte range |
| `Width`, `Height`, `PixelsPerScanLine` | visible geometry and physical stride |
| `BytesPerPixel` | required value is `4` |
| `PixelFormat` | UEFI RGBX, BGRX, or bit-mask mode |
| `RedMask`..`ReservedMask` | bit-mask channels for bit-mask mode |

The loader's GOP snapshot is copied into the 88-byte versioned boot-info
record. Managed installation validates size, ABI, geometry, stride, pixel
format, masks, address arithmetic, and framebuffer range before accepting it.
The handoff is rejected on malformed or overflowing descriptors and every
rejection is logged with a stable failure reason.

## Presenter contract

`ManagedFramebufferPresenter` in
`src/ManagedKernel/ManagedFramebufferPresenter.cs` consumes the existing
caller-owned `ManagedFramebuffer` source. It converts each source pixel
directly into the destination row and never writes physical scan-line
padding. RGBX and BGRX use the matching byte order with an opaque reserved
byte; bit-mask mode packs channels using the supplied masks. The source alpha
is intentionally treated as already resolved by the Phase 48 rasterizer, so
presentation is a byte-format conversion rather than a second blend pass.

The operation supports bounded placement and clipping. It validates source
dimensions, destination range, stride multiplication, address addition, and
per-pixel writes before touching the destination. Telemetry records state,
failure reason, rows, written pixels, clipped pixels, source-hash capture,
source-hash stability, and destination-hash validity. Cancellation is checked
at row boundaries. The presenter can be reset and reused after success,
failure, or cancellation.

The source digest domain is `GXOS-P47-FB-ARGB8888`; the canonical logical
destination digest domain is `GXOS-P49-DST-ARGB8888`. The source is hashed both
before and after presentation, while the destination hash covers only the
logical presented rectangle. This makes the proof sensitive to source
mutation, channel swaps, clipping mistakes, and padding writes without
requiring a full physical framebuffer copy.

## Visible-page fixture and proof

The Phase 49 proof derives its HTML from the authoritative Phase 46 fixture,
preserving the existing semantic controls and adding bounded fixed-position
proof swatches. The page contains managed web text and these exact-color
anchors: red, pure green, blue, black, `#123456`, `#8385C7`, and the page
background `#101820`. The swatches are outside the old opacity controls so
the screenshot checks the presentation path's exact channel conversion.

The managed raster is centered in the QEMU GOP mode. The retained clean run
record reports:

- GOP acquired before ExitBootServices: yes.
- GOP mode: BGRX (`PixelFormat=0x1`), 1280x800, stride 1280, 4,096,000 bytes.
- Managed source: 160x180 ARGB8888.
- Placement: `(560,310)`.
- Pixels written: 28,800; clipped pixels: 0; rows processed: 180.
- Source hash:
  `B5CDB816759039A692EE20A0C1282A4EEB33B887E6C64DB42B66250F3D2A3173`.
- Logical destination hash:
  `54E2AD1C26FFE17EE76D3002A984D33CF4EA2B82D4A3B8F092DB54754093E36C`.
- Source unchanged after presentation: yes.

Three fresh QEMU boots were validated from the same Phase 49 runner path.
Each emitted the complete Phase 49 marker chain, no page fault/CPU exception/
unexpected import/failure marker, a 1280x800 P6 screenshot of exactly
3,072,016 bytes, and 12 mapped source-to-physical samples. The 36 mapped
samples matched their screenshot pixels, all eight required exact colors were
found, and all three complete RGB pixel streams were identical:

- Raw PPM SHA-256:
  `5076193A4522C7CBFD4F3385A543F230A56751D175A5F0605273BE490346294E`.
- RGB pixel-stream SHA-256:
  `9ECD1A1BBA6308391BB8B88FC67BF0FC416385C55CDF8E7E71DA4F031556E088`.
- Unique raw hashes: `1`; unique pixel hashes: `1`.
- QEMU processes after validation: `0`.

The retained evidence is under
`artifacts/phase49-visible-green-extra/`. The raw screenshot is
`runs/run-1/qemu-screen.ppm`; a previewable conversion is
`runs/run-1/qemu-screen.bmp`. The reproducible wrapper is
`tools/Run-ManagedKernelPhase49VisiblePageProof.ps1`.

## Hash-chain and build evidence

The final Phase 49 serial chain retains the Phase 48 font semantic hash
`4872C3D6EA0701697830CD9FCB21294BF097584B6014C70CEE8D52359A6BE5F0` and
records the Phase 49 resource as 2,534 decoded UTF-8 bytes with SHA-256
`855E195866EEA3632588F20F81D827C4B40CE9A9A6EC8636433D668F8C0CD731`.

The NativeAOT managed payload is 3,502,592 bytes with SHA-256
`7D07146236A59530BEB84879B0FEDC0E980714AA7D487423A3EB8460F716DCB3`.
Compared with the Phase 48 payload of 3,475,968 bytes, Phase 49 adds 26,624
bytes, or approximately 0.766%. NativeAOT publish completed with zero build
errors and zero warnings in the retained final build logs. The repository
requests SDK 10.0.302, which is not installed on this host; the verified
fallback was SDK 10.0.400 with MSBuild 18.9.6.

## Validation

The new host suite, `ManagedKernelPhase49HostTests`, passes 694 cases. It
covers RGB/BGR/bit-mask packing, placement and clipping, invalid and
overflowing descriptors, source immutability, row padding canaries,
destination canaries, cancellation, reset/reuse, and failed-preflight
no-write behavior. The retained Phase 44–49 host aggregate is 4,251 cases:

| Suite | Cases |
| --- | ---: |
| Phase 44 | 66 |
| Phase 45 | 292 |
| Phase 46 | 1,846 |
| Phase 47 | 955 |
| Phase 48 | 398 |
| Phase 49 | 694 |
| Total | 4,251 |

The final worktree remains uncommitted and preserves the legitimate Phase 48
changes that were present at the start of this phase. No commit, push, or pull
request was performed. The branch remains aligned with its upstream at
`39228da00d29cd0d37c09adde1378728c0344ee1`.

## Remaining boundary

Phase 49 deliberately proves a single bounded managed page into the current
identity-mapped GOP range. A future phase can add a managed framebuffer
mapping policy, damage rectangles, or a larger page surface, but those should
remain separate from this first presentation proof so the current ownership,
overflow, clipping, and source-integrity guarantees remain auditable.

# Managed Kernel Phase 52: External PNG Images

Phase 52 adds the first real web image to the bounded HTTPS page pipeline. The
page is fetched over HTTPS, the HTML `<img src>` is discovered in document
order, and the image is fetched as a separate HTTPS resource. The resource is
accepted only when the final response is successful and its parsed media type
is `image/png`. The response body is streamed directly into the bounded PNG
consumer; image bytes are not compiled into the NativeAOT payload.

An image that has not completed successfully remains a replaced-element
placeholder. The placeholder uses a 32 by 24 intrinsic size. A completed image
publishes its intrinsic PNG dimensions, and CSS width/height resolution and
aspect-ratio behavior then determine the layout box. Phase 52's 48 by 32 image
is laid out at 96 by 64. Paint emits an image command only for a live,
generation-valid image handle; otherwise it emits the placeholder command.

## Fixed resource limits

The page image store is one fixed cumulative ARGB8888 arena:

| Limit | Value |
| --- | ---: |
| Default image slots | 4 |
| Maximum image slots | 16 |
| Maximum width/height | 256 pixels |
| Default decoded pixel budget | 65,536 pixels |
| Maximum pixel arena | 262,144 bytes |
| Maximum PNG chunk length | 1 MiB |
| Maximum filtered row bytes | 1,024 bytes |

The store allocates a record array, a `uint` pixel arena, and one 32-byte
decoded-pixel digest per slot. A reservation checks slot count, dimensions,
and cumulative pixel budget before any decoded pixel is written. Failed or
cancelled reservations are reclaimed when they are the newest reservation;
reset clears the complete arena and advances the store generation.

`ManagedImageHandle` is exactly a `(Slot, Generation)` pair. Every lookup
checks both values against the current store generation, so a reset invalidates
all old handles without object identity or a per-image allocation. A completed
`ManagedPageImageDescriptor` carries the handle, source-node index, width,
height, pixel start, pixel count, and state. The internal record has the same
source-node, geometry, arena-range, and state fields; the store's arrays are
indexed by slot.

The PNG decoder keeps fixed signature, chunk-header, IHDR, type, and CRC
buffers. Its two scanline buffers are each 1,024 bytes (`256 * 4`), one for
the previous reconstructed row and one for the current row. It also reuses
the existing bounded zlib/DEFLATE decoder, whose input and output windows are
1,024 bytes and whose history window is 32 KiB. The expected inflated size is
checked as `height * (rowBytes + 1)` before decoding begins.

## PNG subset and validation

The decoder accepts PNG signature and ordered `IHDR`, one or more consecutive
`IDAT` chunks, and `IEND`. It supports bit depth 8 with color types:

- 2: RGB
- 6: RGBA
- 0: grayscale
- 4: grayscale plus alpha

Pixels are normalized to ARGB8888. Indexed color, 16-bit samples, palette
transparency (`tRNS`), unsupported compression/filter methods, Adam7
interlace, invalid dimensions, missing or split `IDAT` sequences, unknown
critical chunks, truncated data, and decoded-size mismatches are rejected.
Every chunk CRC is checked. Unknown ancillary chunks are CRC-checked and
skipped; `PLTE` is recognized as a non-critical chunk for ordering purposes,
but indexed color is still outside the supported subset.

The five PNG filters (None, Sub, Up, Average, and Paeth) reconstruct into the
current row using the previous row and the bytes-per-pixel predictor. The
decoder tracks all five filter counts, compressed IDAT bytes, inflated bytes,
decoded pixels, and both the entity and canonical decoded-pixel SHA-256
digests. A PNG CRC failure is reported as `InvalidChunkCrc` and aborts the
reservation, leaving the image slot count at zero.

## Layout, paint, raster, and presentation

The layout engine treats `<img>` as a replaced inline element. It starts with
the bounded placeholder intrinsic size, substitutes the completed image's
intrinsic dimensions, resolves CSS dimensions, and preserves the intrinsic
aspect ratio when one CSS dimension is automatic. Paint validates the source
node, source box, image handle, generation, dimensions, and pixel count before
emitting a resolved image command.

The software rasterizer samples the stored image with bounded nearest-neighbor
mapping. It applies command opacity before source-over alpha blending, honors
the existing clip stack, and keeps normal z-order validation. The framebuffer
is allocated only after the page objects and rasterizer are ready; a generation
0 collection runs immediately before that allocation. Presentation copies the
software framebuffer to the installed physical framebuffer through the
validated descriptor and separately verifies the source and destination
digests.

## Verification

The deterministic Phase 52 fixture serves a 48 by 32 RGBA8 PNG split across
three IDAT chunks and exercises all five filters. The host suite covers PNG
decoding, CRC and bounds failures, generation invalidation, HTML discovery,
intrinsic/CSS layout, resolved and placeholder paint, alpha blending, clipping,
and framebuffer presentation.

The final gate records three fresh positive QEMU boots and one bad-CRC negative
control. The positive proof requires one discovered image, one request, one
loaded image, PNG MIME classification, 48 by 32 dimensions, 1,536 decoded
pixels, all five filter counters, image rasterization, GOP presentation, 16
fixed source-to-framebuffer samples, and visible screen colors. The negative
control requires `InvalidChunkCrc`, zero image slots, start failure, completed
teardown, and no positive image or machine-fault markers.

The clean final evidence is under
`artifacts/phase52-external-png-final4-20260908`. Its NativeAOT payload is
4,761,600 bytes with SHA-256
`A32B4434259F065116DD5224115AF950A036CE319F2EC81168E6329E59219ABD`.

Phase 53 should extend the subset only with an explicitly budgeted feature
such as palette/`tRNS` support or a second image, while retaining the same
fixed arena, generation-handle, chunk, inflated-size, and pixel-budget
contracts.

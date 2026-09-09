# Managed Kernel Phase 53: bounded CSS image resources

Phase 53 extends the Phase 52 PNG path with a deliberately small CSS resource
subset: `background-image: none` and one `background-image: url(...)` value,
plus `background-repeat: repeat` or `no-repeat`. It does not add a general CSS
value tree, a cache, parallel loading, or recursive resource loading.

## Resource model

The CSS engine retains a fixed table of compact `ManagedCssImageReference`
records. Each record stores the declaring owner, inline/embedded/external
origin, stylesheet index, bounded raw URL length, and a small URL arena. Raw
CSS bodies and response bodies are never retained as resource dependencies.

External stylesheet provenance is kept in the page orchestrator. The base for a
CSS image is the stylesheet's final URL after redirects; inline and embedded
style use the document's final URL. The image URL is resolved only after
cascade, so losing declarations do not initiate requests. The page resource
graph is sequential and fixed at two edges from the HTML root:

```
HTML -> stylesheet -> PNG
HTML -> PNG
```

`@import` and all deeper resource references remain unsupported, so the
currently supported graph cannot form a cycle.

HTML and CSS image consumers share the Phase 52 fixed image descriptor/pixel
store. Identical canonical final URLs use the existing image slot and attach a
bounded source-node alias. CSS image capacity is charged against the same
image-store budget; there is no second CSS pixel arena. Reset clears CSS
references, stylesheet bases, dedup state, handles, descriptors, and decoded
pixels.

## Style, paint, and raster behavior

The computed style stores a bounded image reference identity, resolved image
handle, and repeat mode. Background images are emitted as a dedicated paint
command after the background color and before the border. The rasterizer clips
to the padding/background box, the ancestor clip stack, and the framebuffer.
`no-repeat` draws one intrinsic-size tile at the box origin; `repeat` uses
bounded modulo tiling. Existing alpha blending is used once per source pixel.
The border therefore remains authoritative over the image, and existing text
painting remains later in the display-list order.

## Bounds and failure policy

The supported CSS URL grammar is ASCII-case-insensitive `url(...)`, with
quoted or unquoted URLs, bounded whitespace, and a non-empty URL no longer
than `ManagedHttpsUrl.MaximumUrlLength`. Controls, escapes, gradients, multiple
layers, shorthand, `background-size`, and arbitrary positioning are rejected
or ignored as unsupported declarations. Reference-table overflow, URL/scheme
failure, MIME failure, transport failure, PNG decode failure, and shared-store
capacity failure map to CSS-specific page failure reasons. A CSS image is
accepted only as `image/png` and is decoded by the Phase 52 PNG decoder,
including its existing bounds, CRC, zlib, filter, and alpha checks.

## Proof coverage

`src/ManagedKernelPhase53HostTests` contains 540 deterministic assertions for:

- quoted, unquoted, mixed-case, whitespace, fragmented, malformed, and
  unsupported CSS image values;
- cascade winning-only behavior, `none`, `!important`, non-inheritance,
  inline origin, external stylesheet provenance, final-URL resolution, and
  exact reference capacity;
- shared image aliases and reset behavior;
- background-color/image alpha interaction, no-repeat/repeat rasterization,
  border ordering, clipping, and image-command validation.

The existing Phase 52 host suite remains green at 651 cases.

The managed kernel proof logs the following Phase 53 markers after validating
the HTML `<img>` path, CSS reference/fetch/decode counters, stylesheet final
URL, resolved image URL, paint commands, raster output, and GOP presentation:

```text
GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_REFERENCE_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_FETCH_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_DECODE_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_BACKGROUND_PAINT_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_GOP_PRESENT_PASS
GXOS_NET10:MANAGED_HTTPS_PHASE53_VISIBLE_CSS_IMAGE_PASS
GXOS_NET10:MANAGED_KERNEL_PHASE53_PASS
```

The Gate4 runner has a deterministic Phase 53 fixture with the request order
`/phase53/index.html`, `/phase53/css/theme.css`,
`/phase53/images/content.png`, `/phase53/images/background.png`. The CSS URL
`../images/background.png` is resolved against the external stylesheet final
URL, not the HTML URL. A CSS-origin bad-CRC control is also wired and expects
CSS image failure, no background paint, no visible-page marker, and restored
Phase 14 accounting.

## Build and current evidence

The repository pins SDK `10.0.302`; this host uses the installed fallback SDK
`10.0.400` (MSBuild 18.9.6 direct entry point). NativeAOT publish completed
with one pre-existing unused-field warning (`KernelLog.s_hexScratch`) and no
errors. The latest Phase 53 NativeAOT payload produced by the managed-kernel
build is 4,789,760 bytes with SHA-256
`01BF6594C5115DC340064FB147C6DD79F85E78CF75EF9945BBA6420283EFD142`.

The host suites pass, and the Gate4 C/PowerShell harness parses and builds.
QEMU 11.0.0 is installed and repository-owned QEMU cleanup was confirmed
before each attempt. The latest exact Gate4 Phase 53 image (`phase53-gate-final7`,
payload SHA-256
`EA82804526D90D9BF89951C54E1601A2D368D108860DDDC4BA3193A2EA37E14`) passes
startup through Phase 11 and reaches E1000 TX completion, but faults in the
existing post-TX GC-survival probe before `MANAGED_E1000_RX_READY`. A separate
attempt to bypass that probe changed the NativeAOT image layout and faulted
earlier in Phase 11, so that experiment was reverted.

Consequently, this working tree does not claim the requested QEMU 3/3 visual
acceptance or 1/1 CSS bad-CRC acceptance yet. The deterministic CSS bad-CRC
fixture and runner assertions are wired, but neither network control can be
credited until the guest reaches the Phase 53 HTTP exchange and produces the
required GOP markers.

## Exclusions and Phase 54 direction

This phase intentionally excludes gradients, multiple layers, shorthand,
positioning, cover/contain sizing, SVG/JPEG/GIF/WebP, masks, cursor images,
`@import`, `@font-face`, JavaScript, navigation, prefetch, parallel loading,
and HTTP caching. A later phase can add broader CSS image grammar only after a
separate bounded representation, cancellation policy, and visual proof are
specified.

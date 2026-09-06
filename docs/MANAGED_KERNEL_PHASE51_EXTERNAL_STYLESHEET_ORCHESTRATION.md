# Managed Kernel Phase 51 — bounded external stylesheet fetching and page orchestration

Phase 51 adds a bounded page-resource coordinator to the managed kernel. It
fetches one HTTPS document, discovers stylesheet sources in document order,
fetches external CSS one resource at a time, streams each decoded stylesheet
into the fixed-arena CSS engine, then runs cascade, layout, paint, raster, and
GOP presentation. The implementation is deterministic and synchronous from
the kernel’s point of view: it uses no `Task`, `async`, thread pool, dynamic
CSS body buffer, `@import` recursion, or general browser networking layer.

## Orchestration contract

`ManagedPageResourceOrchestrator` in
`src/ManagedKernel/ManagedPageResourceOrchestration.cs` owns the page state
machine and the fixed source table. Its states are:

`Idle → FetchingDocument → DiscoveringSources → FetchingStylesheet →
ParsingStylesheet → Cascade → Layout → Paint → Raster → Presenting → Complete`

The HTML tree is built by the existing streaming tokenizer/tree builder. After
the document body completes, the coordinator polls one source at a time. A
`<link>` is eligible when `rel` contains `stylesheet`, `alternate` sources are
ignored, and the `type` attribute is absent or is `text/css` (ASCII
case-insensitive). Inline `<style>` nodes are parsed at their source position.
Consequently, equal-specificity rules obey document order across inline and
external sources. The proof fixture has external A, inline CSS, then external
B; B wins and produces the visible blue target.

The source table is fixed-capacity. The Phase 51 proof allows two external
stylesheets per page while the CSS arena reserves four external records. The
engine’s general maximum is 16 records. Hrefs are copied through fixed
scratch buffers and are limited to 512 scalars. Relative URLs resolve against
the final document URL, including a redirected document URL. HTTPS is
required for the document and every stylesheet; HTTP downgrade redirects and
unsupported URL forms are rejected. Redirect counts, final URLs, status, MIME,
charset, content encoding, encoded bytes, decoded bytes, scalar counts, rule
counts, declaration counts, and per-sheet digests are retained as bounded
telemetry.

## HTTP, decoding, and CSS limits

The document request requires a successful 2xx status. External stylesheet
requests require both a successful 2xx status and `text/css` MIME. The shared
resource stack validates the response framing and streams the body through
the existing bounded gzip decoder before text decoding. UTF-8 is the default;
explicit UTF-8, US-ASCII, and ISO-8859-1 declarations are supported by the
bounded decoder, with charset metadata and source (default, BOM, or explicit)
recorded. CSS does not restart on `@charset`, and `@import` is deliberately
unsupported so a stylesheet cannot create an unbounded recursive fetch graph.

The CSS parser consumes decoded scalars incrementally. It keeps comment and
brace state across delivery windows and retains only one completed rule in
fixed scratch storage before committing it to the arena. The stylesheet scalar
limit is 16,384. The page proof uses fixed capacities of 8 stylesheets, 64
rules, 128 selectors, 256 selector steps, 256 declarations, and 128 computed
styles, with an external record capacity of 4. Capacity overflow is a
terminal, observable failure rather than an allocation fallback.

## Host coverage

`src/ManagedKernelPhase51HostTests` exercises 1,083 cases. The suite covers
document-order cascade, inline/external source discovery, `rel` tokenization,
mixed-case `type`, alternate suppression, href resolution and length bounds,
fragmented CSS delivery, comments, rule/declaration limits, URL and HTTPS
policy, reset/cancel behavior, and layout/paint/raster output. The preserved
Phase 44 CSS suite passes 66 cases; the Phase 50 extended-font suite passes
683 cases.

## NativeAOT and authoritative QEMU proof

The payload was built with the installed .NET 10.0.400 SDK fallback because
the repository’s pinned 10.0.302 SDK is not installed. The resulting
NativeAOT payload is 4,680,192 bytes with SHA-256
`711C3598F44974C282DF7EFC2B8C90FD127750A88DCEDB0715643BB72CA85CB1`.
QEMU is 11.0.0. The reusable proof wrapper is
`tools/Run-ManagedKernelPhase51ExternalStylesheetProof.ps1`; it builds the
managed payload and Gate 4 image, runs the Phase 11 fresh-boot harness, checks
the external-sheet and visual invariants, and requires at least three boots.

The authoritative evidence is in
`artifacts/phase51-final-evidence-6`. All three fresh dgram boots reached
`MANAGED_KERNEL_PHASE51_PASS`, loaded exactly two gzip external stylesheets,
parsed one inline stylesheet, resolved the target to external blue
(`0xFF0000FF`), completed layout/paint/raster, presented through GOP, and
passed the visible-page checks. The QEMU PPM screen hash was identical across
all three boots:

`5ABE0CBAD302CEB63906CC47AFBEEEFA83C9D1DF43356B63F19F8BD8B96985F9`

The fresh-boot harness also uses expected TCP tuple matching for each new
Phase 34 SYN, so late frames from a prior sequential stylesheet connection
cannot be mistaken for the next resource handshake. No CPU exception, page
fault, unexpected import call, or managed proof failure was present in the
three successful serial logs.

This phase intentionally does not claim JavaScript, media fetching, CSS
`@import`, selector expansion beyond the bounded CSS engine, or an unbounded
browser cache. Those remain outside the Phase 51 contract.

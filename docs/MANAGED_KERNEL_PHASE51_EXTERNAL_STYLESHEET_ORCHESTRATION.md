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

## Phase 51A acceptance closure

The acceptance-closure result is **Outcome B — core Phase 51 proven, acceptance
evidence incomplete**. The implementation remains bounded and the host suites
pass from the final source tree, but the required fresh guest matrix was not
completed in this environment.

The final-source NativeAOT build used the installed .NET 10.0.400 SDK fallback
and MSBuild 18.9.6. It produced a 4,683,264-byte payload with SHA-256
`F6730C6FA17E4C14E08E52D7E5110046BC16031809918530ABA67E4B3A444614`.
The build had one pre-existing CS0169 warning for `KernelLog.s_hexScratch` and
zero errors.

The host closure counts are Phase 47 = 955, Phase 48 = 397, Phase 49 = 694,
Phase 50 = 683, and Phase 51 = 1,083, for an aggregate of 3,812 cases. All
five suites passed. The Phase 51 source still permits two external resources in
the guest proof, reserves four external CSS records, limits href scratch to 512
scalars, CSS delivery to a 16,384-scalar stylesheet, and retains the fixed CSS
arena capacities documented above.
The guest proof also deliberately uses the existing 256-byte compatibility
entity/body bound and a 16 KiB decoded stylesheet bound; these are proof-fixture
limits, not a relaxed MIME or unbounded-body policy.

The wrong-MIME control uses the production DNS → TCP → TLS → HTTP response
metadata path and leaves the strict `text/css` gate intact. A completed
final-source boot is retained in
`artifacts/phase51a-final-wrong-mime-7`: it reached HTTP 200,
`text/html; charset=utf-8`, `ExternalStylesheetContentTypeRejected`, zero CSS
scalars/rules/declarations, `MANAGED_KERNEL_PHASE51_WRONG_MIME_CONTROL_PASS`,
and the Phase 14 accounting/health markers without a fault. The negative
harness marks this as a control result rather than a visible-page success.
However, the required 3/3 fresh negative guest runs were not completed because
the deterministic TLS fixture intermittently stalled before the response
boundary on subsequent boots. The negative harness includes only proof-fixture
changes: the wrong-MIME response, explicit acceptance markers, and early
negative teardown after MIME rejection; it does not weaken production policy.

The prior three-boot positive evidence remains in
`artifacts/phase51a-final-positive-2` and retains the complete two-resource
proof: external A → embedded style → external B, with B winning equal-specificity
`color: #0000FF` and external geometry affecting width, padding, and border.
Its payload was the immediately preceding closure build, not the final payload
hash above. A fresh positive run against the final payload was not completed
after the final negative-control propagation change, so that directory is
supporting evidence rather than final-authoritative evidence for Outcome A.

The production close path remains bounded polling with normal FIN teardown for
successful resources. The MIME-negative path terminates at metadata rejection
and uses the normal driver teardown/accounting path; it does not wait for the
successful-page FIN sequence. No Phase 47–50 fresh QEMU regression matrix was
completed during this closure, and the full guest successful-load/reset/
wrong-MIME reuse sequence therefore remains open even though host reset/cancel
coverage passes.

No Phase 52 work was started. Phase 51A should be reopened for the missing
fresh guest matrix and authoritative final-source positive/negative 3/3 runs
before Phase 52 begins.

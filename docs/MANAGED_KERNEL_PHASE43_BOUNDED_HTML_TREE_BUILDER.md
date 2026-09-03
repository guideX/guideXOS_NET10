# Managed Kernel Phase 43 — Bounded HTML Tree Builder and Document Arena

Phase 43 adds the first persistent managed HTML document above the Phase 42
pipeline:

`DNS → TCP → TLS → HTTP → gzip/zlib → bounded resource → MIME/charset → Unicode scalars → tokenizer → tree builder → document arena`

The tree builder consumes `ManagedHtmlToken` synchronously. It does not retain
the response source or token stream, and it does not use `HttpClient`,
`SslStream`, tasks, reflection, LINQ, per-node collections, dictionaries, or
growing document arrays.

## Navigator audit

The repository audit found no existing Navigator HTML/document model, DOM node
classes, CSS selector/style dependencies, layout-facing document API, or parser
that retains a complete HTML source document. The pre-Phase-43 HTML surface was
the Phase 42 tokenizer and resource adapter only. Consequently there was no
allocation-heavy Navigator DOM to port or compare structurally in this
checkout. The Phase 43 document is an independent semantic donor and is the
future integration target for Navigator; selector matching, style, layout,
painting, navigation, scripting, and mutation remain out of scope.

## Arena layout and memory invariant

All retained document data is allocated once by `ManagedHtmlTreeBuilder` and
is bounded by `ManagedHtmlDocumentArenaOptions`. The default proof capacities
are:

| Arena/state | Capacity | Representation |
|---|---:|---|
| Nodes | 1,024 | packed value records |
| Text | 65,536 | Unicode scalar `uint` values |
| Tag names | 8,192 | ASCII bytes for unknown names and doctype |
| Attributes | 2,048 | packed value records |
| Attribute names | 16,384 | ASCII bytes for unknown names |
| Attribute values | 16,384 | Unicode scalar `uint` values |
| Open-element stack | 128 | node indices |

The limits are configurable but constructor validation caps them at finite
repository-defined maxima. Exhaustion fails closed; no arena is resized and
no document-sized fallback allocation is attempted.

The host-measured packed record sizes are 48 bytes per node and 23 bytes per
attribute. The complete default persistent arrays plus open-element stack are
approximately 449,024 bytes:

`1024×48 + 65536×4 + 8192 + 2048×23 + 16384 + 16384×4 + 128×4`.

This excludes managed array headers and small metadata. The builder's fixed
scratch buffers add about 1.2 KiB, plus hash state and metadata. The tokenizer
retains the same approximately 21 KiB array payload documented by Phase 42.
The transfer-layer estimates remain approximately 27 KiB for HTTPS + text +
HTML and 62 KiB for HTTPS + gzip + text + HTML, before TCP/TLS/driver/DMA
allocations. Thus the engineering estimates for the new default document are
approximately 476 KiB for HTTPS + identity HTML + tree and 511 KiB for HTTPS
+ gzip HTML + tree, excluding the existing fixed kernel/network allocations.

## Node model and handles

The packed node record contains `Kind`, known `Tag`, parent, first/last child,
previous/next sibling, bounded name range, attribute range, text range, and
flags. Links are signed arena indices with `-1` as the internal no-relation
sentinel. A document has exactly one Document root when parsing has begun.

Supported node kinds are Document, Doctype, Element, Text, and Comment.
Comments are intentionally discarded after being counted, so no comment-sized
storage is retained. Doctype is retained as one bounded Doctype node and the
document exposes `IsHtmlDoctype`.

`ManagedHtmlNodeHandle` is a value type containing an arena index and a
generation. Reset increments the generation; handles from the prior document
then fail `Document.IsValid` and cannot be used to access the new document.
The public document API returns copied views and handles, not mutable arena
arrays.

The public API includes root/html/head/body/doctype handles, node kind/tag and
relationship accessors, child traversal through first-child/next-sibling,
bounded text and tag/name/value copy methods, attribute enumeration and lookup
by known enum or ASCII name, capacities and usage counters, `Validate`, and
`TryCopyCanonicalHash`.

## Names, attributes, and text

Common tags use `ManagedHtmlTag` and common attributes use
`ManagedHtmlAttributeName`. Unknown names are copied into the fixed name
arenas, so an unknown bounded element or attribute remains representable
without a `String`. Attribute lookup is a bounded linear scan of the element's
finite attribute range. Attribute order is retained. Boolean attributes carry
`HasValue = false`; values are Unicode scalar sequences in their separate
fixed value arena.

Text is copied from the transient tokenizer token into the persistent scalar
arena. Adjacent text tokens for the same parent coalesce when the previous
text node ends at the current arena tail; no existing text is moved. This
makes tokenizer segmentation independent of the logical document. Supplementary
characters remain one scalar in storage.

## Tree construction

The implemented insertion modes are Initial, BeforeHtml, BeforeHead, InHead,
AfterHead, InBody, Text, AfterBody, AfterAfterBody, InTable, InTableBody,
InRow, and InCell.

The common structure rules are:

- html, head, and body are implied as needed; empty input completes as
  Document → html → head → body;
- head-only tags include title, meta, link, style, script, and base;
- first non-whitespace body content closes head and transitions to body;
- common block starts close an open paragraph, and a repeated li closes the
  earlier li;
- void elements, including br, img, meta, link, input, and hr, are appended
  but never pushed;
- table, tbody/thead/tfoot, tr, td, and th receive a bounded insertion-mode
  subset with implicit tbody/row/cell creation;
- title, style, script, and textarea text is stored normally; no CSS or
  JavaScript is parsed or executed;
- unmatched end tags are ignored and counted; a matching ancestor may be
  closed through nested open elements;
- formatting misnesting uses a conservative bounded ancestor-close policy,
  not the full adoption agency algorithm.

Full HTML5 foster parenting, active formatting elements, scripting insertion,
custom-element semantics, XML/XHTML rules, and browser-equivalent implied-end
tag recovery are deferred. Table content is retained under the current
bounded table context; it is not full foster parenting.

## Flow control, cancellation, and failures

Normal tree insertion returns `Continue`. `RequestPause` and `Resume` are
available on the tree consumer and preserve token ownership when the
tokenizer is paused. Arena exhaustion is terminal, never backpressure.
`Cancel` is terminal and prevents subsequent mutations. `Reset` clears all
arenas, counters, stack state, handles, and hash availability.

Tree-builder failures are explicitly classified as:

`NodeCapacityExceeded`, `TextCapacityExceeded`, `AttributeCapacityExceeded`,
`AttributeValueCapacityExceeded`, `AttributeNameCapacityExceeded`,
`TagNameCapacityExceeded`, `TreeDepthExceeded`, `InvalidTreeState`,
`UnsupportedInsertionModeCase`, `TokenConsumerFailure`, and `Cancelled`.

`ManagedHtmlResourceRequest` exposes tree-builder state/failure in its copied
progress snapshot and maps these failures to the corresponding resource
failure taxonomy while preserving lower tokenizer, text, compression, HTTP,
TLS, transport, teardown, and cancellation failures.

## Validation and canonical proof

`ManagedHtmlDocument.Validate` is a deterministic host/debug validator. It
checks node kinds, root ownership, all index ranges, child-parent agreement,
first/last consistency, previous/next sibling agreement, sibling cycles,
text ranges, attribute ranges, and attribute ownership. The builder also
checks that open-stack entries remain valid elements.

Completion computes a SHA-256 over a canonical, length-delimited structural
representation containing node kind/tag, links, ordered attributes, text
scalars, and child order. It never hashes raw record padding or managed array
layout. The digest is stable across tokenizer input segmentation, resource
fragmentation, compression, and pause/resume.

## Host validation

`src\ManagedKernelPhase43HostTests` currently passes 31 cases. It covers empty
and implied documents, explicit roots, links and coalesced text, Unicode and
supplementary scalars, known/unknown and boolean attributes, exact failure
taxonomy for node/text/attribute/value/name/tag/depth limits, void/raw text,
tables, discarded comments, pause/resume, cancellation/reset, handle
generation, segmentation-independent hashes, and synthetic validator faults
for parent links, sibling cycles, text ranges, and attribute ranges.

The deterministic rich fixture reports:

| Metric | Value |
|---|---:|
| UTF-8 bytes | 2,566 |
| Scalars | 2,491 |
| Tokens | 251 |
| Text/start/end/comment/doctype tokens | 60 / 97 / 91 / 1 / 1 |
| Token attributes | 62 |
| Nodes/elements/text nodes | 160 / 98 / 60 |
| Text scalars / attribute-value scalars | 662 / 202 |
| Peak depth | 7 |
| Implied / unmatched / implicit closes | 0 / 0 / 1 |
| Canonical document hash | `E693068D356DCA59E61D57CD387C261CF452373B98364F4B1A6D3EFCB281224A` |

The existing Phase 42 host suite was rerun and passes 204 cases. The Phase 43
host runner is `tools\Run-ManagedKernelPhase43HostTests.ps1`.

## NativeAOT and QEMU proof tooling

The managed kernel now has a separate Phase 43 mode (`RunPhase14(8)`) and
emits document metrics, resource SHA-256, pause/resume metrics, and canonical
document hash under `MANAGED_HTTPS_PHASE43_*` markers. The Gate 4 harness accepts
`ManagedKernelPhase43` and `-EnableManagedKernelPhase43`. The fresh-boot proof
wrapper is `tools\Run-ManagedKernelPhase43HtmlDocumentProof.ps1`.

The deterministic gzip fixture is served at `/phase43/gzip` with
`text/html; charset=utf-8`, gzip encoding, 2,566 decompressed bytes, and raw
resource SHA-256:

`F5E393FF306737E41C5BD930642786870E733E8F1644BD75E28A138DFB82EB21`

The wrapper requires 3/3 fresh boots, the full metric set above, the canonical
document hash, and absence of machine-fault markers. It also requires
compressed transfer (`encoded bytes > 0` and `< 2,566`).

The node-capacity negative-control wrapper is
`tools\Run-ManagedKernelPhase43NodeCapacityControl.ps1`. It selects a fixed
80-node document arena against the rich fixture, requires
`NodeCapacityExceeded`, validates the partial tree, confirms no Phase 43
success marker, and expects clean kernel health failure-boundary markers.
The same control can be run for three fresh boots.

QEMU was available for the Phase 44 continuation at
`C:\Program Files\qemu\qemu-system-x86_64.exe` (QEMU 11.0.0). The final Phase
43 continuation evidence is preserved in
`artifacts\phase43-document-final-20260902` (3/3 positive boots) and
`artifacts\phase43-node-capacity-final-20260902` (3/3 node-capacity negative
boots). Phase 40/41/42 QEMU regressions were not rerun in this continuation;
their existing evidence remains preserved.

## Compatibility and remaining limitations

The representation is sufficient for selector traversal, element-type checks,
bounded link/image discovery, CSS matching, computed style, and layout tree
traversal. It does not expose layout boxes, painting, resource fetching,
navigation, DOM mutation/events, form behavior, image decoding, JavaScript,
shadow DOM, accessibility, or XML semantics.

The measured fixture deliberately has no implied html/head/body elements;
separate host cases cover the implied structure. Comments are counted and
discarded. Hashing is post-build and bounded by the node/attribute/text
arenas. Ordinary child insertion is O(1) through `LastChild`; end-tag and
attribute matching are bounded linear scans over the fixed depth or element
attribute slice.

Phase 44 adds the CSS parser/matcher/cascade as a separate bounded layer; see
`docs\MANAGED_KERNEL_PHASE44_BOUNDED_CSS_CASCADE.md`. CSS remains separate from
layout and painting. A future phase can add external stylesheet streaming,
sibling combinators, or a layout adapter without changing the Phase 43 node
arena contract.

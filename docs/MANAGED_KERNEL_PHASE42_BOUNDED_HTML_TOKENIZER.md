# Managed Kernel Phase 42 — Bounded Streaming HTML Tokenizer

Phase 42 adds a streaming HTML-token layer above the Phase 41 resource and Unicode-scalar pipeline:

`DNS → TCP → TLS → HTTP → optional gzip → bounded resource bytes → MIME/charset → UTF-8 scalars → HTML tokens → consumer`

The central invariant is that document length affects iteration count and token count, never persistent tokenizer storage. Phase 42 does not build a DOM, tree, CSS model, JavaScript runtime, navigation stack, or XML parser.

## Existing Navigator audit

The sibling `D:\dev\guideXOSServerV0.5_DEVELOPER_STUDIO` tree contains `guide_web_html_parser.cpp/.h` and `navigator_html_parser.cpp/.h`. That implementation is a useful semantic reference for case-insensitive tags, entities, text handling, links, forms, and CSS-lite behavior. Its API accepts a whole `std::string` document and its implementation uses document-sized strings, vectors, maps, and other hosted containers. It is not suitable for direct NativeAOT kernel reuse.

The active managed-kernel repository had no pre-existing managed Navigator tokenizer or DOM. Phase 42 therefore leaves the existing Navigator untouched and introduces a separate lexical API for a future tree-builder. In particular, it does not copy the sibling parser's whole-document ownership model.

## Tokenizer API

`ManagedHtmlTokenizer` is reusable and synchronous:

```csharp
bool AppendInput(ReadOnlySpan<uint> scalars);
ManagedHtmlTokenizerProcessResult Pump(
    IManagedHtmlTokenConsumer consumer, bool endOfInput = false);
void Resume();
void Cancel();
void Reset();
ManagedHtmlTokenizerProgressSnapshot Progress { get; }
```

`IManagedHtmlTokenConsumer.Consume(in ManagedHtmlToken)` returns the existing `ManagedHttpBodySinkResult` contract: `Continue`, `Pause`, or `Fail`. `Complete`, `Cancel`, and `Reset` complete the lifecycle. The resource-level wrapper is `ManagedHtmlResourceRequest`, with `BeginGet`, `BeginGetUrl`, `Poll`, `Pause`, `Resume`, `Cancel`, `Reset`, copied progress, content-type copying, and resource-digest copying.

`ManagedHtmlToken` is a synchronous snapshot view. It exposes lengths and flags plus `TryCopy*` methods for text, tag names, comments, doctype names, and attribute name/value data. It does not expose a retainable span into tokenizer storage. Consumers must copy data they need before `Consume` returns.

Token kinds are `Text`, `StartTag`, `EndTag`, `Comment`, `Doctype`, and `EndOfFile`.

## State machine and supported syntax

The parser uses an explicit enum state machine and no recursion. States cover data, tag open/end-tag open, tag names, all attribute phases, self-closing syntax, markup declarations, comment phases, doctype phases, character references, `RawText`, `RcData`, `ScriptData`, raw closing-tag candidates, pause, terminal completion, cancellation, and failure.

Text is emitted in adjacent bounded chunks when necessary. A future tree-builder must not assume adjacent text tokens are coalesced.

Start and end tag names are ASCII-limited, normalized to lower case, and allow the bounded HTML name characters. Start tags support whitespace, `>`, `/ >`, boolean attributes, empty values, double-quoted values, single-quoted values, and unquoted values. The tokenizer does not infer nesting or void-element tree semantics. Self-closing syntax is preserved on `StartTag` tokens.

The fixed limits are:

| Storage | Limit |
| --- | ---: |
| scalar input window | 256 scalars |
| text token | 128 scalars |
| tag name | 64 ASCII bytes |
| attribute name | 32 ASCII bytes |
| attribute value | 256 scalars |
| attributes per start tag | 16 |
| comment fragment | 256 scalars |
| doctype name | 32 ASCII bytes |
| entity name | 32 scalars |

No limit is silently truncated. Duplicate attributes are compared after ASCII lower-case normalization; the first occurrence wins and later duplicates are ignored while still being parsed within the same bounded tag.

## Character references

References are decoded in text, quoted attribute values, and unquoted attribute values. The bounded named subset is `amp`, `lt`, `gt`, `quot`, and `apos`. Decimal (`&#65;`) and hexadecimal (`&#x41;`/`&#X41;`) numeric references are supported. Resulting values must be valid Unicode scalars; zero, surrogate values, and values above `0x10FFFF` fail with `InvalidNumericEntity`.

Unknown names, missing semicolons, and malformed-looking nonnumeric references are preserved literally rather than incorrectly transformed. Entity state survives scalar-window and input-span boundaries. Entity names beyond 32 scalars fail with `EntityNameTooLong`.

## Comments and doctype

Comments are emitted as bounded fragments. `IsCommentFragment` and `IsCommentFinalFragment` distinguish a chunked logical comment from its final fragment. A comment can therefore exceed 256 scalars without document-sized storage. Unterminated comments fail with `TruncatedComment`.

The supported declaration is a case-insensitive minimal `<!DOCTYPE html>` form with bounded whitespace and a bounded name. Historical public/system identifiers are intentionally outside this phase. Unsupported declarations fail with `UnsupportedMarkupDeclaration`; malformed or overlong doctypes fail with `InvalidMarkup` or `DoctypeTooLong`.

## Raw text modes

After a `style` start tag, content is `RAWTEXT`: `<` and `&` are text unless a case-insensitive `</style>` candidate matches. `script` uses the same safe candidate-based treatment as script data: script is never executed, and `<div>` or `&amp;` inside it remains text. `title` and `textarea` use `RCDATA` behavior: matching end tags close the mode, nonmatching `<` is text, and supported references decode.

Candidate matching is bounded by the raw tag name and `MaximumRawCandidateLength`. False candidates are flushed as text. The implementation is deliberately a useful safe subset, not full HTML5 script-data error recovery.

## EOF, failure, and progress

At EOF, data flushes a final text token and then `EndOfFile`. Raw and RCDATA text also flushes bounded text before EOF. A partial tag, attribute, or declaration fails with `TruncatedMarkup`; a partial comment fails with `TruncatedComment`; a pending entity follows the literal-preservation policy. A pending token is delivered before more input is consumed.

`ManagedHtmlTokenizerFailureReason` preserves distinct bounded failures: `InvalidMarkup`, `TruncatedMarkup`, `TagNameTooLong`, `AttributeNameTooLong`, `AttributeValueTooLong`, `TooManyAttributes`, `EntityNameTooLong`, `InvalidNumericEntity`, `CommentStateError`, `TruncatedComment`, `DoctypeTooLong`, `UnsupportedMarkupDeclaration`, `TokenConsumerFailure`, `Cancelled`, and `NoProgress`.

`ManagedHtmlTokenizerProgressSnapshot` is copied and non-mutating. It contains state/failure, received and consumed scalars, total tokens, per-kind token counts, attributes, decoded references, buffered text, current tag length, current attribute count, pause/resume counts, pending-token state, and peak text-token usage. `ManagedHtmlProgressSnapshot` adds HTTP status, MIME/charset/content-encoding state, encoded/decompressed/text byte counts, Phase 41 peak buffers, resource state, and the tokenizer fields.

Every processing loop must consume scalars, emit/finalize a token, change state, pause, fail, or complete EOF. The implementation has no wall-clock progress safeguard and does not intentionally spin on an unchanged state.

## Backpressure, cancellation, and MIME gating

If a consumer returns `Pause`, the tokenizer retains the pending token, stops consuming scalars, and reports stable `Paused` results until `Resume`. The token is committed exactly once after a later successful delivery. The `ManagedHtmlResourceRequest` pause is applied at the Phase 41 text-resource boundary, so HTTP, decompression, text decoding, and tokenizer ownership remain bounded. Repeated paused polls leave encoded bytes, decoded bytes, scalar counts, and token counts unchanged.

Cancellation is terminal. It is propagated through the HTML wrapper, Phase 41 text request, tokenizer, and consumer; no later token callback is made. `Reset` is allowed after a terminal state and clears input, partial tag/attribute/comment/entity state, counters, and consumer state for reuse.

HTML tokenization is MIME-gated. `text/html` is accepted case-insensitively; `text/plain`, images, and binary types are rejected by the HTML operation while the independent raw-resource and text APIs remain available. `application/xhtml+xml` is not treated as HTML in this phase because XHTML requires separate XML semantics. HTML charset restart or `<meta charset>` rescanning is not implemented; the tokenizer consumes the Unicode scalars already produced by Phase 41.

## Fixed-memory accounting

The tokenizer owns fixed arrays for 256 input scalars, 128 text scalars, two 64-byte tag-name buffers, a 32-byte active attribute name, a 256-scalar active value, sixteen 32-byte attribute-name slots, sixteen 256-scalar attribute-value slots, sixteen attribute descriptors, a 256-scalar comment fragment, a 32-byte doctype name, a 32-scalar entity name, a 34-scalar entity output window, and a 72-scalar raw candidate window. The array payload is approximately 21 KiB, before managed array/object headers and scalar fields. The token hash consumer adds one 32-byte digest and fixed SHA-256 state; its temporary copy buffers are bounded stack windows and do not persist per token.

The dedicated transfer storage is approximately:

* HTTPS + text + HTML: roughly 27 KiB for the HTTP delivery/parser windows, Phase 41 text decoder windows, and Phase 42 tokenizer arrays.
* HTTPS + gzip + text + HTML: roughly 62 KiB after adding the fixed 1 KiB compressed-input staging window, 1 KiB output window, and 32 KiB deflate history window.

These are engineering estimates of the bounded transfer layers and exclude the existing fixed TLS, TCP, Ethernet DMA, driver, and kernel service allocations. They deliberately exclude the maximum decoded-resource limit because that is a validation limit, not a document-sized HTML buffer. Document length can increase iterations, emitted text fragments, and hash work, but not retained tokenizer memory.

## Tests and authoritative fixture

`src\ManagedKernelPhase42HostTests` covers basic text, Unicode scalars, all supported tag/attribute forms, duplicate policy, named and numeric references, comments, doctype, raw/RCDATA/script modes, exact and over-limit bounds, two-part split enumeration, one-scalar fragmentation, pseudo-random fragmentation, pause stability, cancellation/reset, MIME classification, long-document memory behavior, canonical hash stability, and an exact gzip decoder round trip. The suite currently reports 204 cases.

The deterministic positive fixture is served as `text/html; charset=utf-8` with gzip encoding at `/phase42/gzip`. It contains doctype, html/head/body, title, style, script containing `<`, headings, paragraphs, links, an image, classes/IDs, a form with controls, a comment, textarea/RCDATA, Unicode text, quoted/unquoted/boolean attributes, and entity references. The positive proof uses a fixed token hash consumer and does not retain the token stream. Its expected values are 1,894 decompressed bytes, 1,883 Unicode scalars (`0x75B`), 52 tokens, 19 text tokens, 16 start tags, 14 end tags, 1 comment, 1 doctype, 21 attributes, 1,362 text scalars, 5 decoded references, resource SHA-256 `FAC7D0EB02B0940018D627A86731E8754BD1010F10A825F91147D7908FFD3C44`, and canonical token hash `15967F70BB89C5AC00D73E4D4D73B057F6C48E670F3EE789C0A0B945705605B6`.

The proof pauses once the first token has crossed the resource boundary, performs four stable paused polls, resumes, validates content type and resource SHA-256, and emits bounded HTTP/decompression/text/tokenizer peak metrics. The fresh-boot wrapper is `tools\Run-ManagedKernelPhase42HtmlProof.ps1`. The malformed-control wrapper is `tools\Run-ManagedKernelPhase42MalformedHtmlControl.ps1`; it serves a gzip HTML body with a 257-scalar unquoted attribute value and expects `AttributeValueTooLong` without a success marker or machine fault.

## Compatibility and limitations

The tokenizer is lexically compatible with the representative Navigator concepts that matter to a future tree-builder: headings, paragraphs, links, images, forms, class/id attributes, comments, scripts, and style blocks can all be represented as tokens. It intentionally does not apply Navigator's hosted document construction, CSS-lite parsing, selector matching, form behavior, image decoding, script execution, or URL resolution. There is no implied html/head/body insertion, nesting stack, foster parenting, adoption agency algorithm, full HTML5 named-reference table, or XML/XHTML interpretation.

Recommended Phase 43 work is a separate bounded tree-builder that consumes these tokens, owns an explicit bounded policy for element depth/document metadata, and keeps the same synchronous backpressure, cancellation, reset, and fixed-memory contracts. The existing Navigator should be integrated only after that tree-builder has an explicit ownership design.

# Phase 41 — Bounded MIME Classification and Streaming Text Decoding

Phase 41 adds a bounded text interpretation layer above the Phase 40 HTTP/
HTTPS resource pipeline. It classifies the final `Content-Type`, selects a
small supported charset set, strictly decodes bytes to Unicode scalar values,
and delivers those scalars to streaming consumers without retaining the
response body.

## Scope

The additive implementation is in
`src/ManagedKernel/ManagedTextDecoding.cs`. It does not change the existing
raw-byte `ManagedResourceRequest` API or the Phase 40 decompression decoder.
The new `ManagedTextResourceRequest` wraps the raw resource request and keeps
the original bytes connected to the existing SHA-256 consumer while sending
decoded scalars to a text consumer.

Supported MIME classifications are:

| Classification | Recognized media types |
| --- | --- |
| `TextPlain` | `text/plain` |
| `Html` | `text/html`, `application/xhtml+xml` |
| `Css` | `text/css` |
| `Json` | `application/json`, `application/*+json` |
| `JavaScript` | `application/javascript`, `text/javascript` |
| `Xml` | `application/xml`, `text/xml`, `application/*+xml` |
| `Textual` | other `text/*` types |
| `Binary` | `application/octet-stream`, image/audio/video types, and other non-text types |

The parser is intentionally a bounded subset: one media type token, optional
semicolon-separated `name=value` parameters, ASCII token names, and quoted
parameter values without escape processing. The `charset` parameter is the
only semantic parameter. It is limited to 32 bytes and accepts UTF-8,
US-ASCII, and ISO-8859-1 aliases. Unsupported, malformed, empty, overlong,
or conflicting duplicate declarations are reported separately.

The default policy accepts recognized textual types and rejects unknown or
binary types. Callers may explicitly opt into unknown and binary MIME classes.
No charset defaults to UTF-8, with a UTF-8 BOM taking precedence as the
effective source. An explicit non-UTF-8 charset is not overridden by a BOM.

## Decoder contract

`ManagedTextDecoder` is strict and incremental:

- UTF-8 accepts only valid shortest-form sequences, rejects surrogate values,
  rejects values above `U+10FFFF`, and reports invalid versus truncated input.
- US-ASCII rejects bytes above `0x7F`.
- ISO-8859-1 maps each byte directly to `U+0000..U+00FF`.
- A UTF-8 BOM is recognized across arbitrary input fragmentation and is not
  delivered as a scalar.
- Output is represented as Unicode scalar values in fixed `uint` windows;
  no UTF-16 strings are created in the guest path.
- The fixed decoder input queue is 1,024 bytes. The output window is 256
  scalars (1,024 bytes of scalar storage). The BOM candidate is three bytes.
- Consumers receive `ReadOnlySpan<uint>` segments and may return Continue,
  Pause, or Failure through the bounded consumer state contract.

`ManagedTextCountConsumer`, `ManagedTextPrefixConsumer`,
`ManagedTextDestinationConsumer`, and `ManagedTextCompositeConsumer` provide
the first consumers. The count consumer can count lines with CRLF treated as
one line ending. The destination consumer fails closed when its caller-owned
scalar buffer is full.

Pause is a real back-pressure boundary. A paused text consumer does not cause
the same scalar segment to be delivered twice. `Resume`, `Cancel`, and
`Reset` are explicit operations and are safe at fragmentation boundaries.
Decoder failures, downstream failures, destination exhaustion, unsupported
metadata, transport failures, decompression failures, and teardown failures
remain distinguishable in `ManagedTextFailureReason` and the associated
decoder/consumer progress fields.

## Resource integration and telemetry

`ManagedTextResourceRequest` preserves HTTP versus HTTPS selection, final
redirect behavior, identity versus gzip/deflate decoding, decoded-resource
limits, raw-byte SHA-256 coverage, and the existing lower-layer progress
accounting. Its `ManagedTextProgressSnapshot` adds:

- status, MIME classification, content-type state, charset, and charset source;
- content-encoding state and encoded HTTP bytes received;
- decompressed resource bytes produced and text input bytes consumed;
- scalars produced and delivered, text segments, pause/resume counts, and
  buffered decoded text;
- decoder state/failure and peak HTTP, decompression, and text buffer sizes.

The wrapper parses metadata only after the final response headers are known.
It never classifies a redirect body as the final resource. Header values remain
bounded by the existing 64-byte `Content-Type` storage and the charset token
has its own 32-byte semantic bound.

## Proof fixture

The Phase 41 kernel proof is in
`src/ManagedKernel/ManagedPhase41TextProof.cs`. It requests
`https://www.example.com/phase41/gzip` and verifies every scalar in a
deterministic 256-repeat corpus:

```text
GuideXOS 41\r\nRé sum λη Ж 中 ★ 🙂\n
```

The fixture is 10,496 UTF-8 bytes, gzip encoded for transport, and contains
7,680 Unicode scalars. The proof uses a verifying consumer, line counter,
16-scalar prefix consumer, and the raw SHA-256 consumer. It pauses after the
first delivered scalar, requires four stable polls with all progress counters
unchanged, resumes, and checks exact completion.

The deterministic decoded-resource SHA-256 is:

```text
6D91A155D767AC7C0C1E2C5B49479CF1D7FDE8DF7C4F459A9BCECE43EF11DF79
```

The Gate4 loader selects Phase41 with `run_phase14(6U)` under
`GXOS_ENABLE_MANAGED_KERNEL_PHASE41`. The runner
`tools/Run-ManagedKernelPhase41TextProof.ps1` builds the NativeAOT payload,
stages Gate4, serves the fragmented HTTPS/gzip fixture, requires three fresh
boots, and validates the serial markers and proof fields.

## Validation

Host validation:

```powershell
& .\tools\Run-ManagedKernelPhase41HostTests.ps1 -Configuration Release
```

The Phase41 host suite covers parser fragmentation and policy, ASCII and
Latin-1, exhaustive valid UTF-8 sequence classes, malformed/truncated UTF-8,
BOM fragmentation, output-window behavior, consumer pause/resume/reset,
identity and gzip resource integration, raw SHA-256 preservation, and
invalid-MIME/charset/UTF-8 controls. The current run passed **5,466** cases.

Authoritative guest validation:

```powershell
& .\tools\Run-ManagedKernelPhase41TextProof.ps1 -RunCount 3
```

The recorded three-boot proof used payload SHA-256
`606D71A9E3D0AFC8CB995F4CC147BE305BD2B6137E3A92E45CBCD140E0FF57C0`, payload
size 1,951,232 bytes, and evidence directory
`artifacts/phase41-text-authoritative-20260901-final`.

Each boot reported status `0xC8`, MIME `TextPlain`, explicit UTF-8, 10,496
decompressed/input bytes, 7,680 produced/delivered scalars, one pause and one
resume, four stable paused polls, text peak `0x1`, decompression peak
`0x400`, the expected prefix, all eight SHA-256 words, and clean
`MANAGED_KERNEL_PHASE41_PASS` completion. No CPU exception, page fault,
unexpected import, or `GXOS_NET10:FAIL:` marker was present.

## Memory accounting and limits

The new text stage contributes fixed storage: a 1,024-byte input queue, a
256-entry scalar output window (1,024 bytes), a three-byte BOM candidate, and
small fixed consumer/adaptor state. It does not allocate in the guest hot
path or retain the entity. When gzip is enabled, the existing Phase 40
decoder retains its fixed 1,024-byte input/output windows and 32 KiB history
window; the text stage is downstream of that decoder. The total process still
includes the existing HTTP/TLS/network and NativeAOT runtime storage, so the
reported peak fields are stage-local bounds rather than a fabricated whole
process byte total.

The decoded-resource limit remains the existing bounded resource limit
(`ManagedContentEncodingLimits.MaximumDecodedResourceLength`, 4 MiB in the
proof configuration). Entity-size, header-size, decompression, input-buffer,
output-window, and destination-consumer exhaustion are all terminally
bounded failure paths.

## Deliberate limitations

This phase does not implement HTML parsing, CSS tokenization, JSON parsing,
JavaScript parsing, charset sniffing beyond the UTF-8 BOM rule, Unicode
normalization, UTF-16 decoding, or a general RFC media-parameter grammar.
Those belong in a later layer built on the scalar streaming contract. The
existing raw resource API remains the compatibility boundary for callers that
need bytes rather than text.

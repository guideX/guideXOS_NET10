# guideXOS C# .NET 10 Managed Kernel — Phase 40
# Bounded Content-Encoding Decoding Above HTTP/HTTPS Streaming

Status: implementation, host acceptance, NativeAOT publication, and fresh-boot
acceptance are complete in the current worktree. The live starting commit was
`102628b0dbb2bf9095326f31325d36965742a5e8` (`Phase 39`). No commit is made by
this phase.

## Decision

Phase 40 adds a local managed content-decoding boundary above the existing
Phase 39 HTTP/HTTPS resource stream. The kernel does not reference
`System.IO.Compression`; the decoder is repository-owned code using fixed byte
arrays and scalar state. The supported first slice is:

- `Content-Encoding: gzip`, including optional fields and FHCRC validation;
- `Content-Encoding: deflate` as the RFC 1950 zlib wrapper around RFC 1951
  DEFLATE, including stored, fixed-Huffman, and dynamic-Huffman blocks;
- one content-coding token only, with `identity`, missing, unsupported, malformed,
  and over-limit metadata states kept distinct.

Raw DEFLATE, Brotli, comma-separated coding chains, concatenated gzip members,
and trailing compressed bytes are rejected explicitly. Brotli and other
encodings remain a later phase rather than silently falling back to identity.

## API and ownership

`ManagedHttpResponseParser` recognizes one bounded `Content-Encoding` value and
the HTTP and HTTPS clients forward its scalar state and bounded copy method.
`ManagedResourceRequest` selects the decoder only after the final response
headers are parsed. Missing and `identity` responses retain the Phase 39 fast
path and have no decoder allocation. Gzip and zlib responses use:

```csharp
ManagedResourceRequest resource = new(service, maximumEntityLength,
                                       maximumDecodedResourceLength);
resource.BeginGetUrl("https://www.example.com/phase40/gzip"u8, pipeline);
resource.Poll();
ManagedResourceProgressSnapshot progress = resource.Progress;
```

The decoder copies encoded bytes into a fixed 1,024-byte input staging area,
produces into a fixed 1,024-byte output window, and retains only a 32 KiB
DEFLATE history ring. The resource wrapper drains decoded output through the
same `IManagedHttpBodySink` boundary used by identity responses. It never
retains a decoded response-sized queue and never hands the caller a reference
to decoder storage.

The ownership sequence is strict:

```text
transport/TLS -> HTTP parser body window -> decoder input
                                      decoder output -> resource consumer
```

The parser does not release an encoded body segment until the decoder has
accepted it. The decoder does not release its output window until the
downstream consumer returns `Continue`. A consumer `Pause` freezes the
resource, parser, decoder, TLS, and network; repeated polls while paused do
not advance any counters. `Cancel` is terminal for the operation and prevents
later consumer calls. `Reset` clears decoder, parser, consumer, and progress
state for reuse.

## Limits and failure taxonomy

The fixed policy is:

| Limit | Value |
|---|---:|
| `Content-Encoding` metadata | 32 bytes |
| Decoder input staging | 1,024 bytes |
| Decoder output window | 1,024 bytes |
| DEFLATE history | 32,768 bytes |
| Gzip optional field | 1,024 bytes |
| Encoded HTTP entity | existing 1 MiB limit |
| Decoded resource | 4 MiB default, caller-selectable downward |

Decoded length is checked before each output byte is committed. The resource
failure reason distinguishes unsupported or malformed metadata,
`Content-Encoding` over-limit, malformed gzip/zlib/DEFLATE data, checksum and
ISIZE mismatch, truncation, decoded-resource overflow, trailing data, and
ordinary transport/parser/TLS/consumer failures. Progress exposes encoded bytes
received/consumed, decoded bytes produced/consumed, buffered decoded bytes,
decoder state/reason, pause/resume counters, history size, and CRC/ISIZE/Adler
validation flags.

Checksum validation is part of completion: gzip CRC32 and modulo-2^32 ISIZE,
or zlib Adler-32, must match before `Complete` is reported. A checksum failure
cannot expose a successful resource even if decoded bytes were produced into
the temporary output window.

## Bounded CPU and memory

The decoder has no recursive parsing, unbounded loop, dynamic collection, or
allocation proportional to encoded or decoded size. Huffman trees use fixed
canonical-code arrays. Each `Pump` call stops at input starvation, output-window
full, completion, cancellation, or a structural failure. Resource polling is
bounded by its existing transport polling policy.

Approximate active decoder storage is 36.5 KiB: 32,768-byte history, 1,024-byte
input, 1,024-byte output, fixed gzip/zlib header/trailer state, dynamic code
length storage, literal/distance/code-length tables, and scalar state. The
exact layout is implementation-owned; these are fixed arrays, not response
buffers. Adding the existing Phase 39 HTTP-side staging gives approximately
41–42 KiB for HTTP, or approximately 46 KiB for HTTPS before lower TLS/network
storage. Identity responses continue to use the Phase 39 footprint with no
decoder. Caller-owned destinations and consumer state remain outside the
decoder budget and are bounded by the caller's explicit capacity.

The 4 MiB decoded-resource limit is a policy limit, not an allocation. A
highly-compressible input still produces only the fixed output window and is
failed before the next byte would exceed the limit.

## Tests

The dedicated host project is
`src/ManagedKernelPhase40HostTests/ManagedKernelPhase40HostTests.csproj`.
The final direct run reported:

```text
MANAGED_KERNEL_PHASE40_HOST_TESTS_PASS cases=4347
```

Coverage includes fragmented metadata, case/whitespace handling, duplicate and
chained codings, unsupported and over-limit values, minimal and optional-field
gzip members, FHCRC, stored/fixed/dynamic DEFLATE, zlib framing, overlapping
history copies, byte/137/whole fragmentation, truncation, malformed headers
and trees, checksum/ISIZE/Adler mismatch, raw DEFLATE rejection, trailing and
concatenated members, decoded limits, consumer pause stability, cancellation,
encoded-versus-decoded progress, fixed history/window bounds, and the full
`ManagedResourceRequest` HTTP integration.

The Phase 39 host regression was rerun after the Phase 40 API additions:

```text
MANAGED_KERNEL_PHASE39_HOST_TESTS_PASS cases=301
```

The ManagedKernel NativeAOT publish also succeeded with the installed .NET
10.0.400 fallback MSBuild entry point. The produced payload was
1,897,472 bytes with SHA-256
`B340466E49C160B3384BFC7F032509CE6468AF1BBA18AAD2FD9A5826EEF5D7BE`.

## Deterministic QEMU proof

`tools/Run-ManagedKernelPhase40ResourceProof.ps1` builds the NativeAOT
payload, selects Gate 4 `ManagedKernelPhase40`, and performs three fresh
QEMU boots using a dgram e1000e fixture. The fixture serves
`/phase40/gzip` with `Content-Encoding: gzip`, an encoded Content-Length, and
the 16,384-byte pattern `(index * 31 + 7) & 255`. The proof verifies the
decoded count, first 32 bytes, SHA-256
`9038AC64E659335CCBFDD3F684F35A26A2C9E580D9AF6B4807AF3ADBE2C257E3`, gzip
CRC/ISIZE, encoded/decoded counters, one deliberate four-poll pause, and the
absence of machine faults or success-marker duplication.

The authoritative fresh-boot summary is
`artifacts/phase40-resource-20260901-164231427/phase40-summary.log`. It records
three independent passes with encoded length `0x1E1` (481 bytes), decoded
length `0x4000` (16,384 bytes), a 1,024-byte peak decoded window, a 32 KiB
history window, and four stable pause polls. The per-boot serial-log SHA-256
values are:

```text
run-1 081113687EFA062524D2F0DBAC110C1827415332021CB7157864B6DB61D4962D
run-2 EF78BECEAB36963A43FBC7E546E2EA8DD08D167C34DEAA9E98C2B14E1D12EE26
run-3 D924C96DE43F5793F8E50D8A3CE0FB632000ABE553D029AD27F87AFDD8ED3CD8
```

The final regression evidence uses the same NativeAOT payload and includes
three fresh Phase 33 positive passes, three Phase 34 positive passes, three
Phase 34 negative-control passes, and three Phase 39 resource-proof passes.
The summaries are in `artifacts/regression-phase33-positive-v2`,
`artifacts/regression-phase34-positive-v2`,
`artifacts/regression-phase34-negative-v2`, and
`artifacts/regression-phase39-resource-v3`. The Phase 39 rerun used
`artifacts/regression-phase39-resource-v3` after the shared runner was fixed
to wait for its dedicated `PHASE39_RESOURCE_BODY_RECEIVED` marker.

## Follow-up

Phase 41 should add one independently bounded content-coding family, such as
Brotli, behind the same metadata, ownership, pause, cancellation, checksum,
decoded-limit, and failure-taxonomy contracts. The decoder should remain a
separate transform boundary so adding an encoding does not change the
identity-path or parser-owned delivery guarantees.

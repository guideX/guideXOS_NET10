# guideXOS C# .NET 10 Managed Kernel — Phase 39
# Bounded Reusable HTTP Resource Streaming and Incremental Consumers

Status: implementation and deterministic acceptance are complete in the current
worktree. The live starting commit was `416afe149d10eafa73413ecd7814fd347d138d80`
(`Implement HTTP Streaming Flow`); this is the authoritative repository state
even though the Phase 39 request described `c1948fb` plus uncommitted Phase 38
work. No commit is made by this phase.

## Starting architecture

Phase 38 established a parser-owned, fixed 1,024-byte HTTP delivery window.
`ManagedHttpResponseParser.TryFeed` stops source ownership at that window and
`ConsumeBody` presents the exact parser-owned segment to an
`IManagedHttpBodySink`. `Continue` releases the segment, `Pause` preserves it
and blocks parser/TLS/network advancement, and `Fail` preserves ownership and
terminates the client. HTTP and HTTPS clients expose the same body contract,
progress counters, cancellation, and reset/reuse lifecycle.

Phase 39 adds a reusable consumer-facing request above those clients:

```csharp
ManagedResourceRequest resource = new(service); // HTTP
ManagedResourceRequest secure = new(service, trustedRoot, validationTime); // HTTPS
IManagedResourceConsumer consumer = new ManagedResourceCountConsumer();

resource.BeginGet("host.example"u8, "/resource"u8, consumer);
resource.Poll();
resource.Pause();
resource.Resume();
resource.Cancel();
ManagedResourceProgressSnapshot snapshot = resource.Progress;
```

The wrapper owns no response-sized queue. It drains one parser segment at a
time, hides client-specific polling, exposes `Pause`, `Resume`, `Cancel`, and
`Reset`, and calls `Complete` only after transport teardown and all final body
segments have been accepted. `BeginGetUrl` is available on an HTTPS resource
request for the existing HTTPS-only redirect URL behavior.

## Resource API and outcomes

`ManagedResourceRequest` is configured as either `Http` or `Https`; the same
`IManagedResourceConsumer` is used for both. The public protocol-specific
client constructors and an internal deterministic HTTPS constructor are both
supported. `Poll` returns the existing `NetworkOperationResult`; a paused
resource returns `Success` without progressing its client. Resource state is
one of `Idle`, `Receiving`, `Paused`, `Completed`, `Cancelled`, or `Failed`.

`IManagedResourceConsumer` extends the Phase 38 sink shape and adds bounded
lifecycle methods:

- `Consume(ReadOnlySpan<byte>)` returns `Continue`, `Pause`, or `Fail`.
- `Complete()` finalizes a successful resource exactly once.
- `Cancel()` permanently prevents successful completion for that operation.
- `Reset()` clears operation state for reuse.
- `State`, `FailureReason`, and `BytesProcessed` are scalar status fields.

`ManagedResourceFailureReason` distinguishes cancellation, destination-full,
consumer, HTTP parser, body/entity-limit, premature-close, transport, TLS,
request, and teardown failures. The snapshot also retains the underlying HTTP,
HTTPS, and parser reasons, so a consumer error cannot be misclassified as a
transport or body-limit error.

## Built-in consumers

`ManagedResourceDestinationConsumer` takes a caller-owned `byte[]`, optionally
with an offset and fixed length. It writes only within that range. A segment is
accepted atomically: if it does not fit in the remaining capacity, no part of
that segment is written, `BytesWritten` remains the number of previously
accepted bytes, and the consumer enters `Failed` with
`DestinationFull`. The resource enters `Failed` with
`ManagedResourceFailureReason.DestinationFull`. This is distinct from
`BodyTooLarge`, which originates in the HTTP parser before a consumer can
bypass the transport entity limit. Exact fit succeeds; there is no truncation.

The consumer uses a fixed array range because a durable request cannot retain a
`Span<byte>`. The array is caller-owned and is the only response-sized storage
allowed by this consumer. A caller can use a span to inspect or copy the
bounded array after completion.

`ManagedResourceCountConsumer` counts accepted bytes and retains no body data.
It accepts any number of parser segments subject to the transport's bounded
entity limit. `Count` and `BytesProcessed` are exact decoded-entity counts.

`ManagedResourceSha256Consumer` composes the existing repository-owned
`ManagedSha256`; it does not duplicate cryptography. It retains the existing
fixed SHA-256 block/state/schedule plus a fixed 32-byte final digest. It accepts
incremental segments, finalizes only from `Complete`, and exposes
`TryCopyDigest(Span<byte>)`. Cancellation leaves the hash unfinalized and
cannot report a successful complete digest.

`ManagedResourcePrefixConsumer` takes either a caller-owned fixed byte array or
an explicit capacity. It retains the first `N` bytes exactly, discards later
bytes after the prefix is full, and tracks all bytes processed. Capacity zero
is a valid discard probe. There is no dynamic growth. Stop-after-prefix is not
implemented as a fake pause: a segment can contain both the final prefix byte
and later bytes, so a caller that wants to stop cancels the resource after
observing `IsFull`.

## Fixed composition

`ManagedResourceCompositeConsumer` supports two to four fixed components in
constructor order. It passes the same parser-owned span directly to each
component; it does not copy the segment and does not allocate a collection.
Later components are not called after an earlier component fails.

The first component may return `Pause`, which the composite propagates safely.
If a later component returns `Pause` after an earlier component has accepted the
same segment, replay would duplicate the earlier operation. The composite
therefore returns a terminal
`ComponentPauseAfterAcceptance` failure in that case. This makes the unsafe
case explicit and preserves byte ownership. The built-in count, hash, prefix,
and destination consumers never pause, so the normal fixed pipeline is
atomic with respect to a parser segment.

## Metadata

`ManagedResourceProgressSnapshot` is a copied scalar snapshot containing:

- resource state and HTTP/HTTPS protocol;
- consumer state and consumer-specific failure;
- HTTP, HTTPS, and parser failure reasons;
- status code and transfer mode;
- known-total flag and Content-Length value;
- decoded bytes received by the parser;
- bytes delivered/accepted by the parser sink;
- bytes processed by the resource consumer;
- current bounded buffered bytes and delivered segment count;
- resource pause and resume counts;
- Content-Type state and bounded length;
- `IsComplete`, `IsCancelled`, and `IsTerminal` projections.

`TryCopyContentType(Span<byte>, out int)` copies the final response's bounded
Content-Type representation. No general header dictionary is introduced.
The parser recognizes `Content-Type` in its existing bounded header parser and
uses `ManagedHttpLimits.MaximumContentTypeLength` (currently 64 bytes). The
representation has three explicit states: `Missing`, `Available`, and
`TooLong`. An over-limit value is not truncated; it is unavailable with the
`TooLong` state. A duplicate Content-Type follows the existing parser behavior
and the last value wins, including replacing an earlier too-long value.
Content-Length and transfer mode remain parser-owned; chunked resources expose
an unknown total.

## Redirect behavior

HTTPS redirects remain owned by `ManagedHttpsClient`: the existing maximum of
five hops, HTTPS-only resolution, hostname validation, SNI, certificate
validation, and downgrade rejection are unchanged. The resource wrapper
detects redirect response bodies and drains them through a fixed discard sink;
they are never presented to the final consumer. Per-hop parser state is reset by
the existing HTTPS client, so final status, Content-Type, Content-Length,
count, prefix, and SHA-256 values describe only the final response. Redirect
cancellation and all existing redirect failures remain client failures.

## Flow control, cancellation, and reuse

`ManagedResourceRequest.Pause` stops the wrapper before polling its underlying
client. A consumer-returned `Pause` is propagated from the parser without
releasing the exact segment. While paused, no resource, parser, TLS, or network
progress occurs. `Resume` clears the wrapper boundary; if the parser owns a
paused segment, the next poll retries that exact segment.

`Cancel` calls the underlying client cancellation boundary and then marks the
consumer cancelled. Later polls and body deliveries do not call the consumer.
HTTP cancellation now uses `ReleaseTcpForReuse`, matching HTTPS, so reset after
cancellation retains configured DHCP/service state and a second request can
start on the same service. `Reset` clears transport/parser and consumer state;
all counters restart at zero.

## Entity limits and memory ownership

The streaming entity limit remains 1 MiB. The complete-body compatibility API
remains 256 bytes and is not replaced by this layer. A caller-owned destination
larger than 1 MiB does not bypass the parser limit; count/discard and hash
consumers are subject to the same limit.

The fixed active-transfer storage is:

| Owner | Fixed storage |
|---|---:|
| HTTP parser: line + delivery window + compatibility body + Content-Type + Location | 3,520 bytes |
| HTTP client/parser staging | approximately 5,312 bytes |
| HTTPS client/parser staging | approximately 9,408 bytes, excluding lower TLS/network fixed storage |
| Resource request wrapper | no byte arrays; references and scalar counters only |
| Count consumer | no byte arrays; scalar counter/state only |
| SHA-256 consumer | existing fixed SHA-256 state plus fixed 32-byte digest |
| Prefix consumer | exactly caller capacity, or explicitly requested fixed capacity |
| Destination consumer | caller-owned fixed destination range only |
| Composite consumer | fixed component references and scalar state; no queues |

The HTTP parser's 3,520-byte total is 2,048-byte line storage, 1,024-byte
delivery storage, 256-byte compatibility storage, 64-byte Content-Type
storage, and 128-byte Location storage. HTTP client staging includes its
512-byte request, 512-byte receive, 512-byte pending receive, and 256-byte
compatibility response storage. HTTPS adds fixed 512-byte request/receive/TLS
output, two 2,048-byte plaintext buffers, and the same 256-byte compatibility
storage before the parser total. The lower TLS/network fixed storage is not
included in the Phase 38 9,408-byte figure.

No queue, string, dictionary, delegate list, or allocation proportional to the
decoded entity exists in the resource layer. The only response-sized storage
available to a consumer is an explicitly bounded array selected by the caller.

## Tests and verification

The dedicated project is
`src/ManagedKernelPhase39HostTests/ManagedKernelPhase39HostTests.csproj` and
reports `MANAGED_KERNEL_PHASE39_HOST_TESTS_PASS cases=301`. It covers
empty/one-byte/exact/overflow/multi-segment destinations, canaries and
pause/resume, count/discard across Content-Length and chunked framing,
incremental SHA-256 under multiple fragmentations and cancellation, fixed
prefix probes, four-way composition and safe failure semantics, bounded
Content-Type metadata, HTTP/HTTPS API parity, cancellation/reset/reuse, and
the exact/over-limit 1 MiB entity boundary. It also exercises the managed TLS
record path with a long-sequence fixture record so the incremental consumer
proof and the TLS record-protection state share the same test surface.

The final host matrix was run directly from the Phase 22–39 project files with
the installed .NET 10 SDK. The individual totals were:

| Suite | Result |
| --- | ---: |
| Phase 22 | 56/56 |
| Phase 23 | 60/60 |
| Phase 30 | 91/91 |
| Phase 31 | 33/33 |
| Phase 32 | 69/69 |
| Phase 33 | 185/185 |
| Phase 34 | 140/140 |
| Phase 35 | 6/6 |
| Phase 36 | 72/72 |
| Phase 37 | 34/34 |
| Phase 38 | 1,475/1,475 |
| Phase 39 | 301/301 |
| **Phase 22–38 arithmetic total** | **2,221/2,221** |
| **Phase 22–39 arithmetic total** | **2,522/2,522** |

The earlier Phase 38 document says `2,219/2,219`, but its own individual
rows sum to 2,221; this Phase 39 report preserves those individual results and
uses the arithmetic sum. No prior suite was weakened or changed.

## Deterministic resource proof

The intended Phase 39 QEMU proof uses the new `ManagedResourceRequest`, a
deterministic HTTPS fixture, a resource substantially larger than the 1,024-
byte delivery window, a fixed count/hash/prefix pipeline, and a deliberate
pause/resume interval. It records status, transfer mode, Content-Type,
known/unknown total, received and processed bytes, segment count, pause/resume
counts, peak buffered bytes, prefix bytes, and the final SHA-256. A separate
small exact-fit destination proof and one-byte-too-small negative proof record
the destination-specific outcome without treating it as an HTTP body-limit
failure. Public HTTPS is supplemental because external DNS/site availability
is not authoritative.

The authoritative deterministic evidence is in
`artifacts/phase39-resource-authoritative-20260901-final-peak/evidence`.
All three fresh boots passed the resource proof. Each recorded:

```text
HTTP status                 = 200
transfer mode               = Content-Length (marker value 2)
Content-Type                = application/octet-stream (24 bytes)
known total                 = yes
decoded total               = 16,884 bytes
received / processed       = 16,884 / 16,884 bytes
delivered segments          = 36
pause / resume              = 1 / 1
stable paused polls         = 4
peak HTTP body buffered      = 480 bytes (`0x1E0`, parser-owned peak)
prefix captured             = 32 bytes
resource SHA-256            = 0284CD23ED354023F0363678794905B285C104A2056189B36C23C0689924454F
terminal state              = Complete
```

The decoded digest was independently calculated from the fixture pattern
`(index * 31 + 7) & 255`. The fixed parser delivery window remained 1,024
bytes; the resource was 16,884 bytes and therefore required no response-sized
buffer. The caller-owned destination consumer's exact-fit and one-byte-too-
small controls passed in the Phase 39 host suite: the exact fit completed with
the exact written count/content, while the smaller destination terminated with
`DestinationFull`, preserved the prior bytes, and did not become an HTTP body
limit failure.

The final resource-proof NativeAOT payload was 1,847,808 bytes with SHA-256
`02750B16D523EA54A1610BC04A247D5410BF48520419EA55ED8CD34D1513A166`.
The fresh proof copied OVMF code SHA-256
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A` and
OVMF variables SHA-256
`B71FD7FDE1F20392E29EB4F1E330E15AEC61724A2FAA21A9D30D85D4A468245D`.

Current-source deterministic networking regressions used fresh evidence
directories:

- `artifacts/phase39-regressions-20260901/phase33-positive` — Phase 33 HTTPS positive, 3/3.
- `artifacts/phase39-regressions-20260901/phase34-positive-retry` — Phase 34 redirect/hostname positive, 3/3.
- `artifacts/phase39-regressions-20260901/phase34-negative` — Phase 34 hostname mismatch negative control, 3/3.

These complete legacy regression sets used the immediately preceding
Phase 39 payload `6E979E03...F9140D`; the final resource-only peak-tracking
rebuild changed the NativeAOT identity to `02750B16...1513A166` and passed the
resource proof 3/3. Replacement legacy attempts with that rebuilt payload
hit an external fixture-injection stall before completing and were excluded,
never counted as passes.

One discarded Phase 34 positive attempt is retained separately at
`artifacts/phase39-regressions-20260901/phase34-positive`; run 2 never entered
the protocol. It was terminated only after its exact QEMU command line was
verified, and it is not counted as acceptance evidence. The replacement fresh
set above passed 3/3.

The supplemental public HTTPS proof was not rerun in Phase 39. Phase 38 had
already shown external DNS instability, while the deterministic fixture
provided authoritative HTTPS, certificate, hostname, SNI, redirect, and
consumer evidence. No TLS or DNS validation was weakened.

At the end of verification, the QEMU process count was zero.

## Limitations and Phase 40

This phase deliberately does not implement filesystem writes, Navigator,
HTML/JSON/image parsing, MIME dispatch, caching, cookies, ranges/resume,
HTTP/2, WebSockets, pooling, multipart, archives, or decompression.
`Content-Encoding` is intentionally not dispatched or decompressed. The
fixed-composition pause rule is explicit rather than pretending arbitrary
rollback is possible.

Phase 40 should add one independently bounded transformation interface (for
example decompression) only after its expansion bound, output ownership, and
backpressure behavior are specified and proven against this resource contract.

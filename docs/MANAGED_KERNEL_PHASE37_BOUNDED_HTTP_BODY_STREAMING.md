# guideXOS Managed Kernel Phase 37 — Bounded HTTP Body Streaming

Status: Outcome A

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

Starting HEAD and ending HEAD: `595bdf6a3f15a22d5bcbecdba5ed4ec74d877f63`

The requested starting HEAD was `9120b12808e676d150fcf452404e39c548c1b48d`,
but the checked-out repository was already one commit ahead at the start.  The
worktree was clean rather than containing the described uncommitted Phase 35
and Phase 36 baseline.  That unexpected state was preserved; no existing
baseline files were discarded or rewritten.

## 1. Exact Phase 36 failure

Phase 36 had already completed DNS, TCP, TLS 1.2, certificate-chain
validation, hostname validation, ServerKeyExchange validation, TLS Finished,
encrypted HTTP GET, and HTTP status parsing.  The remaining public failure was
after HTTP 200:

1. `ManagedPhase35PublicHttpsConsumer` constructed `ManagedHttpsClient` with
   `MaximumBodyBytes = 4096`.
2. That value became the parser's `_maximumAcceptedBodyLength`.
3. Chunked body bytes were decoded incrementally into the parser's bounded
   1,024-byte delivery queue while `_bodyLength` counted the complete decoded
   entity.
4. When the cumulative decoded length reached the 4 KiB public limit,
   `TryAcceptBodyByte()` failed with `BodyTooLarge`.
5. The HTTPS client reported HTTP parse failure even though the status line was
   200 and TLS was authenticated.

This was a cumulative entity-length rejection, not a receive-buffer overflow
and not an allocation of a 4 KiB or larger response buffer.  The parser also
retained only its 256-byte compatibility copy; the public consumer drained
512-byte chunks into SHA-256.  The artificial limit was therefore owned by
the public caller/parser policy, not by the TCP or TLS record buffers.

The Phase 36 public evidence is retained at
`artifacts\phase36-public-final5\evidence\runs`.

## 2. Phase 37 design

The response path is now:

```text
TCP/TLS plaintext segments
    -> bounded HTTP parser
    -> 1,024-byte decoded body queue
    -> IManagedHttpBodySink.TryConsume(segment)
    -> consumer-owned incremental processing
```

`IManagedHttpBodySink` receives the current decoded segment only for the
duration of the call and must not retain the span.  `TryConsumeBody()` removes
the segment only after the sink returns success.  A sink failure therefore
leaves the segment buffered and leaves `BodyBytesDelivered` unchanged; the
caller can handle the failure without corrupting parser accounting.

The parser's existing `TryFeed(ReadOnlySpan<byte>, out int consumed)`
backpressure remains in force.  When the body queue is full, no input byte is
accepted until the consumer drains it.  No response-sized array is allocated.

The public HTTPS proof uses a sink that updates managed SHA-256, counts decoded
bytes, counts delivered segments, and rejects anything over the explicit
1 MiB streaming policy.  It does not retain the public document or print its
contents.

## 3. Bounds and compatibility

The limits have distinct meanings:

| Bound | Value | Meaning |
| --- | ---: | --- |
| decoded body delivery window | 1,024 bytes | Parser-owned pending body bytes at one time |
| compatibility body capacity | 256 bytes | Small complete-body copy retained for existing callers |
| legacy accepted-body limit | 16 KiB | Existing bounded parser policy for callers that select it |
| streaming accepted-body limit | 1 MiB | Explicit maximum total entity length for streaming callers |
| header line | 2,048 bytes | Bounded live-header/token storage; needed for the current CSP line |
| response headers | 4,096 bytes / 32 headers | Bounded header policy |
| chunk-size metadata line | 128 bytes | Existing bounded chunk-token policy |

The parser-owned fixed arrays are 2,048 + 1,024 + 256 + 64 + 128 = 3,520
bytes.  The decoded body queue itself is never larger than 1,024 bytes.  The
existing HTTP client and TLS staging arrays remain bounded separately: TCP
payload staging is 512 bytes and pending TLS application plaintext is 2,048
bytes.  These are working-storage bounds, not an HTTP entity-size limit.

The complete-body convenience API remains deliberately bounded.  The default
parser/client path still uses the 256-byte compatibility policy, and
`TryCopyBody()` returns false when a completed entity exceeds that capacity.
Callers needing larger responses must use `TryConsumeBody()` or
`TryReadBodyChunk()`.  A 257-byte streamed response is accepted but is not
materializable through the compatibility API.

Consequently:

- total entity size can exceed 4 KiB;
- total entity size can exceed the old approximately 16 KiB parser policy when
  the caller selects streaming, while parser working storage remains bounded;
- the streaming path is still explicitly bounded at 1 MiB and is not an
  unbounded allocator.

## 4. Transfer-encoding behavior

Chunked responses keep the existing strict framing policy.  Each chunk size is
parsed with the existing 128-byte metadata limit and 4 KiB individual-chunk
limit, extensions remain bounded, trailers remain bounded, malformed sizes are
rejected, and the zero-sized chunk transitions through trailer parsing to one
clean end-of-body state.  Chunk payloads may be split across arbitrary input
segments and are delivered incrementally.

Content-Length responses use the same decoded body queue and sink.  A declared
length over 4 KiB and over 16 KiB can therefore be consumed incrementally up to
the explicit 1 MiB streaming limit.  Declared lengths, conflicting lengths,
extra bytes, and premature connection close continue to fail closed.

The current live Cloudflare response used for the final proof was
`Content-Length` framed (`0x2`), not chunked.  The deterministic host suite
and the parser's existing chunked path cover the chunked requirement directly.

## 5. Focused host tests

The new project is
`src\ManagedKernelPhase37HostTests\ManagedKernelPhase37HostTests.csproj`.
It passed `34/34` cases in
`artifacts\phase37-regressions-final\phase37.log`, including:

- chunked bodies of 4,095, 4,096, 4,097, and 20,000 bytes;
- aggregate chunked length beyond one parser buffer;
- framing and payload splits at one-, two-, three-, 17-, 19-, and 31-byte
  input boundaries;
- chunk extensions, trailers, and exactly-once zero-sized termination;
- Content-Length body streaming at 20,000 bytes;
- malformed size, truncated payload, missing terminal chunk, oversized chunk
  metadata, and oversized individual chunk rejection;
- the 256-byte complete-body compatibility behavior and bounded materialized
  overflow behavior;
- sink failure propagation with buffered data and delivery counters preserved;
- exact source byte count and SHA-256 digest agreement.

## 6. Regression results

The final host regression command ran from the system temporary directory so
the installed .NET 10.0.400 SDK could be used despite the repository's
`global.json` requesting unavailable SDK 10.0.302 with roll-forward disabled.
Results:

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
| **Total** | **746/746** |

The captured final logs are in `artifacts\phase37-regressions-final`.
Phase 34's hostname-negative behavior remained negative in the deterministic
control at `artifacts\phase37-deterministic\phase34-negative-final`.

## 7. Deterministic QEMU validation

The deterministic Phase 33 positive control passed three fresh boots with the
large bounded streaming body path.  The Phase 34 positive control passed
three fresh boots, and the Phase 34 hostname-mismatch negative control passed
three fresh boots.  The final Phase 37 streaming-build controls are under:

- `artifacts\phase37-deterministic\phase33-positive-final`
- `artifacts\phase37-deterministic\phase34-negative-final`

The final public-only build added the `PUBLIC_HTTP_BODY_DELIVERED` metric after
these deterministic controls; it does not change the streaming/parser logic.
Earlier Phase 37 deterministic positive/negative runs are retained in the
non-suffixed directories.

The negative serial logs prove hostname mismatch remains rejected; no
certificate or hostname validation was bypassed.  QEMU was terminated after
each run.

## 8. Public HTTPS proof

The final public runner is
`tools\Run-ManagedKernelPhase37PublicHttps.ps1`.
It requires all three fresh boots to reach the complete proof markers rather
than counting HTTP 200 alone.  The final result was:

```text
MANAGED_KERNEL_PHASE35_BOOT_SUMMARY=PASS
MANAGED_KERNEL_PHASE35_RUNS=3
MANAGED_KERNEL_PHASE35_OUTCOMES=A,A,A
```

All three runs reached DNS resolution, TCP connection, certificate validation,
hostname validation, ECDHE P-256 ServerKeyExchange validation, TLS Finished,
encrypted HTTP request transmission, HTTP 200, body verification, clean
completion, and post-completion managed GC/resource-health markers.

Public metrics:

- transfer mode: `Content-Length` (`0x2`) on all three runs;
- HTTP status: `200` on all three runs;
- decoded body length: `0x41F4` = `16,884` bytes;
- delivered segments: 24, 24, 24;
- peak decoded parser body buffer: `0x400` = `1,024` bytes;
- decoded entity SHA-256:
  `FC99C93AE04A20CD15EA5E3D3B11116A4265C7529170E3E424AF40F4A9E70729`;
- body-limit rejection: none;
- public result: `3/3` Outcome A.

Evidence:
`artifacts\phase37-public-final5\phase35-summary.log`,
`artifacts\phase37-public-final5\phase37-summary.log`, and
`artifacts\phase37-public-final5\evidence\runs\run-1\serial.log` through
`run-3\serial.log`.

No complete public body was dumped into serial output.  The final managed
payload was 1,796,096 bytes with SHA-256
`DF9659054C14804F9878383B91739CAD6A4C4E3D0C035B488416FB9E592D497B`.
The OVMF firmware SHA-256 was
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.
Remaining QEMU process count after validation: `0`.

An extra metadata-summary retry is retained at
`artifacts\phase37-public-final4`; its first two boots reached Outcome A but
its third boot hit the existing QEMU `RaiseFailFastException` machine-fault
guard.  It is not used for the Phase 37 result; the subsequent clean
`phase37-public-final5` run is the recorded public proof.  The leftover QEMU
instance from that retry was explicitly terminated and the final process count
was verified as zero.

## 9. Security non-regression

Phase 37 changed only HTTP body delivery policy and proof instrumentation.  It
did not alter SHA-256/SHA-384, ECDSA P-256/P-384, certificate algorithm
matching, chain validation, subject + SPKI trust-anchor identity, hostname
validation, TLS transcript/Finished verification, AEAD authentication, or
entropy requirements.  RSA support was not added in this phase.

## 10. Remaining limitations and Phase 38

The streaming entity limit is 1 MiB.  Header lines, total response headers,
chunk metadata, individual chunks, and certificate/TLS structures remain
bounded and reject larger or malformed inputs.  The convenience complete-body
API is intentionally limited to 256 bytes; it is not a general materializer.
The public proof accepts Content-Length or chunked framing, but does not add
redirect or connection-close body scenarios to the Phase 37 public contract.

Recommended Phase 38: make the sink/flow-control contract reusable across
higher-level HTTP callers, add explicit consumer-cancellation and progress
telemetry, and extend deterministic coverage for long-lived multi-megabyte
responses without increasing parser working memory.

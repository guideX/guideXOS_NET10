# Managed Kernel Phase 38: HTTP Stream Flow Control, Cancellation, and Progress

## 1. Scope and starting point

Phase 37 replaced the old cumulative public 4 KiB HTTP-body rejection with a
bounded incremental stream.  Its parser owns fixed arrays for headers,
compatibility copying, and a 1,024-byte decoded-body delivery window.  A
consumer received one bounded span at a time through `IManagedHttpBodySink`,
but the sink could report only Boolean success or failure.

That Boolean was insufficient for a reusable stream contract: a consumer that
was temporarily full had no way to stop ownership from advancing without
pretending that the transfer had failed.  Phase 38 keeps the Phase 37 memory
model and adds explicit flow control, cooperative cancellation, copied
progress telemetry, and distinct terminal states.

The live repository started at `c1948fb` (`Phase 37 Http Body Streaming`), not
the user-provided `595bdf6` reference, and the starting worktree was clean.
The live state is authoritative; no reset, checkout, stash, rebase, or discard
operation was performed.

## 2. Stream contract

The public sink contract is now:

```csharp
public interface IManagedHttpBodySink
{
    ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment);
}

public enum ManagedHttpBodySinkResult : byte
{
    Continue = 0,
    Pause = 1,
    Fail = 2
}
```

`ManagedHttpResponseParser.ConsumeBody()` returns a separate
`ManagedHttpBodyDeliveryResult` so the caller can distinguish `NoData`,
`Delivered`, `Paused`, `Failed`, and `Cancelled`.  The existing Boolean
`TryConsumeBody()` and client `TryConsumeResponseBody()` wrappers remain for
compatibility; they return false only for failure/cancellation and do not
provide the new Pause detail.

There is no exception-driven normal flow control, task, queue, or retained
span.  The sink must consume the span during the call and must not retain it.

## 3. Ownership and pause/resume rules

```text
TCP receive / TLS plaintext staging
              |
              v
      HTTP parser (fixed state)
              |
              v
   parser-owned decoded window (<= 1,024)
              |
              +-- Consume(span) -- Continue --> acknowledge/remove bytes
              |                  Pause -------> keep same bytes in parser
              |                  Fail ---------> keep bytes; terminal failure
              v
          consumer
```

The parser owns every byte in its delivery window until the sink returns
`Continue`, or until a caller removes bytes through the existing
`TryReadBodyChunk()` API.  A `Pause` leaves the span contents, buffered count,
decoded-received count, delivered count, and segment count unchanged.  A
repeated poll while paused calls neither the transport nor the parser with
new input.  A repeated `ConsumeBody()` while the sink remains paused presents
the same segment again and does not increment the pause counter after the
first transition.

When the sink later returns `Continue`, the parser clears only that
acknowledged segment, advances delivered-byte and segment counters, and
transitions from `Paused` to `Receiving` with one resume count.  It then may
accept more source bytes, still stopping at the same 1,024-byte window.  The
HTTP and HTTPS clients also recheck the window after draining pending receive
or TLS plaintext staging; a single poll cannot fetch another transport unit
after that drain fills the safe parser boundary.

This means an arbitrarily long pause consumes constant storage.  It does not
turn backpressure into an implicit network or response-sized queue.

## 4. Cancellation

`ManagedHttpClient.Cancel()` and `ManagedHttpsClient.Cancel()` are explicit,
cooperative, idempotent operations.  They are valid before headers, after
headers, between segments, while paused, and during a long transfer.  They:

1. stop all future sink delivery and body-chunk reads;
2. release/tear down the current network/TLS operation through the existing
   lifecycle boundary;
3. preserve parser counters and the currently buffered body bytes for the
   final progress snapshot;
4. set the client to `Cancelled` and the generic terminal reason to
   `Cancelled`;
5. allow `Reset()` to clear the operation and reuse the client.

Repeated cancellation is a successful no-op.  Cancellation is not mapped to
HTTP parse failure, TLS failure, body-too-large, or sink failure.  A teardown
return value may still be reported by the operation result, but the terminal
state remains explicitly cancelled so callers do not lose the cancellation
cause.

## 5. Progress snapshot

`ManagedHttpProgressSnapshot` is a copied readonly value returned by both
`ManagedHttpClient.Progress` and `ManagedHttpsClient.Progress`.  It exposes no
internal buffer and has no per-read allocation.  Its fields are:

| Field | Meaning |
| --- | --- |
| `State` | `Idle`, `Receiving`, `Paused`, `Completed`, `Cancelled`, or `Failed` |
| `StatusCode` | Parsed HTTP status, or zero before headers |
| `TransferMode` | Content-Length, chunked, connection-close, or no-body framing |
| `DecodedBodyBytesReceived` | Decoded bytes accepted into parser ownership |
| `DecodedBodyBytesDelivered` | Bytes acknowledged by the sink/chunk reader |
| `BufferedBodyBytes` | Exact parser-owned, undelivered body bytes |
| `DeliveredSegmentCount` | Successfully acknowledged segment count |
| `PauseCount` / `ResumeCount` | Flow-control transition counts |
| `HasKnownTotalLength` | True only when Content-Length is present |
| `TotalEntityLength` | Declared Content-Length, otherwise zero |
| `TerminalFailureReason` | Generic terminal classification |
| `ParseFailureReason` | Detailed parser reason when applicable |
| `IsTerminal` | Completed, Cancelled, or Failed |

Snapshot reads do not mutate parser, transport, TLS, or counters.

## 6. Terminal-state model

The generic terminal failure classifications are:

| State/reason | Meaning |
| --- | --- |
| `Completed` / `None` | Complete, valid response and body delivery |
| `Cancelled` / `Cancelled` | Explicit consumer cancellation |
| `Failed` / `SinkFailure` | Sink returned `Fail` |
| `Failed` / `BodyTooLarge` | Decoded entity exceeded the configured bound |
| `Failed` / `MalformedHttp` | Invalid HTTP framing or syntax |
| `Failed` / `PrematureConnectionClose` | Peer closed before framing completed |
| `Failed` / `TransportFailure` | DNS, connect, reset, or network failure |
| `Failed` / `TlsFailure` | HTTPS authentication/protocol failure |
| `Failed` / `TeardownFailure` | Normal completion/cancellation teardown failure |
| `Failed` / `RequestFailure` | Other request/setup failure |

The existing HTTP and HTTPS-specific failure enums remain the detailed
operation-level API.  Phase 38 adds `SinkFailure` to each without renumbering
prior values.

## 7. Framing, limits, and compatibility

Content-Length responses report a known total and increment decoded-received
bytes as body bytes enter the parser window.  Chunked responses use the
existing bounded chunk-size, extension, trailer, and CRLF state machine;
their total length remains unknown (`HasKnownTotalLength == false`) because
the decoded entity length is not declared up front.  Bodyless and
connection-close framing retain their existing semantics.

The streaming entity limit remains exactly 1 MiB.  The exact boundary is
accepted and one byte beyond it fails as `BodyTooLarge`.  This phase does not
raise that limit.

The complete-body compatibility path remains deliberately small:
`MaximumBodyCapacity` and `TryCopyBody()` remain bounded at 256 bytes.  A
257-byte streamed response can complete and be delivered incrementally, but
cannot be materialized through that compatibility API.  No response-sized
copy is introduced.

## 8. Fixed-memory analysis

The parser-owned fixed arrays remain:

```text
2,048 header line + 1,024 body window + 256 compatibility body
+ 64 content type + 128 location = 3,520 bytes
```

Phase 38 adds counters, flags, enums, and a copied snapshot; it adds no body
array.  The HTTP client’s request/receive/pending/compatibility staging is
512 + 512 + 512 + 256 = 1,792 bytes, so parser plus HTTP client staging is
5,312 bytes, excluding the network service’s own fixed receive slot.  The
HTTPS client’s request/receive/TLS-output/plaintext/pending-plaintext/
compatibility staging is 512 + 512 + 512 + 2,048 + 2,048 + 256 = 5,888
bytes, so parser plus HTTPS client staging is 9,408 bytes, excluding the
network service and TLS cryptographic/certificate working storage.  Those
other areas are also fixed by their existing bounds and are not response
entity storage.

The maximum parser-owned undelivered body is always 1,024 bytes.  The Phase
38 host test also measures zero managed allocations on a warmed streaming
hot path; the production project builds NativeAOT without adding desktop
networking abstractions.

## 9. Host coverage

`src/ManagedKernelPhase38HostTests/` contains 1,475 deterministic assertions.
Coverage includes Content-Length and chunked streams, fragmented input,
first-segment and repeated pauses, exact-window and partial-final segments,
ordering/no-duplicate/no-missing SHA-256 proofs, stable paused polling,
1 MiB exact/one-byte-over limits, 256-byte compatibility copying, sink-failure
ownership preservation, HTTP-client backpressure, cancellation at multiple
points, teardown, idempotence, reset/reuse, HTTPS cancellation parity,
terminal reason mapping, and zero allocations on the hot path.

The authoritative host regression result is:

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
| **Aggregate** | **2,219/2,219** |

## 10. QEMU and live HTTPS evidence

The final NativeAOT managed payload was built at
`artifacts/phase38-build-final/publish/gxos-managed-kernel.dll`:

```text
size   = 1,810,432 bytes
SHA256 = 1FAEFBDFD5AD693D9E3A39A2D3C4D03725CF38A32F8DC00C0295A0A71B9C0F07
OVMF code SHA256 = 33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A
```

Fresh deterministic QEMU proofs against that exact payload passed three of
three boots for:

- Phase 33 HTTPS/TLS positive behavior:
  `artifacts/phase38-qemu/evidence-final2-phase33`
- Phase 34 redirect and hostname behavior:
  `artifacts/phase38-qemu/evidence-final2-phase34`
- Phase 34 hostname-mismatch negative control:
  `artifacts/phase38-qemu/evidence-final5-phase34-negative`

The serial hashes and per-boot proof summaries are recorded in those
directories.  The negative-control runner had one transient first attempt
that stopped in the pre-proof serial timing checks; the subsequent
authoritative three-boot runner passed all intended hostname-mismatch checks.

The dedicated public HTTPS Phase 38 harness is
`tools/Run-ManagedKernelPhase38PublicHttps.ps1`.  Its final-source successful
boot is recorded at
`artifacts/phase38-public-final3/evidence/runs/run-1/serial.log` and proves
actual pause/resume and cancellation through `ManagedHttpsClient`:

```text
status                  = 200
transfer mode           = Content-Length (2)
known total             = true, 16,884 bytes
received/delivered      = 16,884 / 16,884
delivered segments      = 25
maximum buffered body   = 1,024 bytes
paused polls            = 4
pause/resume counts     = 1 / 1
decoded SHA-256         = FC99C93AE04A20CD15EA5E3D3B11116A4265C7529170E3E424AF40F4A9E70729
cancellation received   = 395 bytes
cancellation delivered = 395 bytes
cancellation buffered   = 0 bytes
```

That same boot records no-late-delivery, teardown completion, and reset/reuse
markers.  A three-boot public wrapper attempt was not used as acceptance
because the external endpoint stalled in DNS on a later fresh boot; the
deterministic host proof and the successful final-source HTTPS boot remain
the functional Phase 38 evidence.  This is an external network limitation,
not a relaxed certificate or hostname check.

All completed QEMU runs were checked after teardown; the final owned QEMU
process count was zero.

## 11. Remaining limitations and Phase 39

This phase still has one HTTP/TLS operation in flight at a time, a fixed 1 MiB
streaming entity limit, and no persistence beyond the current client/parser
operation.  It does not add decompression, file downloads, Navigator,
caching, HTTP/2, ranges, multipart, or WebSockets.

Recommended Phase 39: define a bounded reusable resource/download consumer
over this contract, including explicit sink lifetime ownership and (if
needed) a separately bounded decompression stage.  It should retain the same
pause boundary and snapshot semantics rather than reintroducing an
entity-sized queue.

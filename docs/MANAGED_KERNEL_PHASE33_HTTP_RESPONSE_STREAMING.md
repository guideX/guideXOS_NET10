# Managed Kernel Phase 33 — HTTP/1.1 Response Framing and Bounded Streaming

## 1. Objective

Phase 33 extends the authenticated HTTPS proof from Phase 32 with one incremental HTTP/1.1 response parser. The parser selects a framing mode, delivers bounded body chunks, rejects unsafe ambiguity, and preserves the TLS/application boundary.

## 2. Phase 32 baseline

The existing network service remains the only HTTPS transport boundary. DHCP, DNS, ARP, TCP, TLS 1.2, certificate validation, deterministic QEMU fixtures, and the Phase 32 request/response proof remain in place.

## 3. Ownership boundary

`ManagedHttpsClient` owns request state, TLS state, parser state, and copied application bytes. `ManagedTls12Client` exposes authenticated plaintext only. Ethernet, IPv4, UDP, TCP, and the network-service adapter remain below the client.

## 4. Framing selection algorithm

After the final non-informational header block, the parser selects exactly one mode: bodyless, Content-Length, chunked Transfer-Encoding, or connection-close. A response cannot silently fall through from one mode to another.

## 5. Content-Length

Decimal Content-Length values are validated for syntax and overflow. Repeated values are accepted only when identical, including a comma-separated repeated value. Conflicting, malformed, oversized, or ambiguous values fail closed.

## 6. Chunked transfer coding

The chunk state machine incrementally consumes a bounded size line, optional extensions, chunk bytes, the required CRLF, the zero-size terminator, and trailers. Chunk payloads are delivered as they arrive; the complete decoded body is never required to fit one compatibility buffer.

## 7. Chunk extensions and trailers

Extensions are syntax-checked and bounded independently of chunk payload size. Trailer lines, aggregate trailer bytes, and trailer count have separate limits. Trailer `Content-Length`, `Transfer-Encoding`, and `Connection` fields are rejected as framing-affecting metadata.

## 8. Connection-close framing

A response without Content-Length or chunked coding may be close-delimited only when the response headers explicitly establish the supported close boundary. `NotifyConnectionClosed` completes that mode; an early close in any other incomplete mode is a premature-close failure.

## 9. Duplicate headers

Duplicate framing headers are normalized and compared before mode selection. Identical Content-Length declarations are safe; contradictory declarations are rejected rather than choosing the first or last value.

## 10. Transfer-Encoding plus Content-Length

The parser rejects a response that presents both Transfer-Encoding and Content-Length. This removes request-smuggling-style ambiguity at the application boundary.

## 11. Public parser API

`ManagedHttpResponseParser.TryFeed` reports the number of source bytes consumed. `TryReadBodyChunk` drains copied body bytes. `FramingMode`, `BodyLength`, `BodyBytesDelivered`, `BufferedBodyLength`, and `ParseFailureReason` expose bounded progress without exposing TLS record storage.

## 12. HTTPS composition API

`ManagedHttpsClient.TryReadResponseBodyChunk` forwards only parser-owned copied bytes. The compatibility `TryCopyResponseBody` API remains available for responses within the original 256-byte contract.

## 13. Backpressure

The parser has a 1,024-byte body delivery window. TLS plaintext is held in a bounded 2,048-byte pending buffer when that window is full. `ManagedHttpsClient` drains the window before polling for more TCP data, so authenticated input cannot grow without a consumer read.

## 14. Bounds

The Phase 33 response limit is 16 KiB. Individual chunks, header lines, chunk-size lines, extensions, trailers, informational responses, and header count each have independent limits in `ManagedHttpLimits`. Exceeding any bound produces a typed parse failure.

## 15. Fragmentation

Status lines, headers, chunk sizes, chunk data, CRLF delimiters, trailers, TLS records, and TCP segments may all be split at arbitrary byte boundaries. Host tests feed one byte at a time; QEMU splits TLS fixture records and TCP payloads.

## 16. Partial failure behavior

Malformed status, headers, lengths, transfer coding, chunk syntax, delimiters, trailers, trailing bytes, TLS authentication, fatal alerts, and premature transport close transition the operation to failure. No partial body is reported as a successful response.

## 17. TLS authentication boundary

HTTP framing begins only after the TLS 1.2 client authenticates and decrypts application data. Bad AEAD tags and fatal alerts fail before HTTP bytes can be accepted. HTTP code does not access certificate, key, nonce, record, or transcript storage.

## 18. GC and storage lifetime

The proof invokes GC after status parsing and continues reading the response. TLS record buffers, parser buffers, and copied delivery buffers have stable managed ownership; the host suite also exercises the shared source after the TLS storage refactor.

## 19. Reuse and teardown

Length-framed and chunked responses close at their authenticated framing boundary, then release the TCP operation for reuse. The Phase 33 consumer resets the HTTPS client between three requests while retaining the DHCP configuration.

## 20. Host coverage

`ManagedKernelPhase33HostTests` covers Content-Length, identical and conflicting duplicates, chunking, extensions, trailers, close framing, bodyless responses, informational responses, malformed input, bounded backpressure, digest verification, and TLS security-boundary composition. The current suite reports 185 cases.

## 21. QEMU normal path

The fresh-boot protocol drives `/phase33-length`, `/phase33-chunked`, and `/phase33-stream`. It validates authenticated TLS, encrypted requests, `phase33-content-length-pass`, `phase33-http-pass`, a 4,097-byte patterned stream, multiple body reads, SHA-256 verification, and teardown.

The final payload is `artifacts/managed-kernel-phase33-final/publish/gxos-managed-kernel.dll`, exactly 1,698,816 bytes with SHA-256 `2DC28033E091B33FB0E083564836D371A477A5677B24F49E2994D8A9DF66CF96`. Three fresh-boot results are preserved under `artifacts/phase33-boot-final-verification/`.

## 22. QEMU negative path

The negative control injects a valid chunk followed by a malformed later chunk-size line. It requires `MANAGED_KERNEL_PHASE33_START_FAILED` after `MANAGED_HTTPS_PHASE33_CHUNKED_SELECTED` and rejects both HTTPS and kernel pass markers. Three fresh-boot results are preserved under `artifacts/phase33-negative-final3/`.

## 23. Regression evidence

The final validation reruns the Phase 22, 23, 30, 31, and 32 host suites and the Phase 33 host suite; the Phase 33 suite reports 185 cases. It also reruns three fresh Phase 33 QEMU boots and the three-boot negative control using the exact staged payload hash and size above. The staged Gate 4 payload is under `artifacts/gate4-phase33-final/` and has the same identity.

## 24. Current limitations

This is a deliberately bounded HTTP/1.1 subset. It does not implement arbitrary transfer-coding chains, HTTP/2, HTTP/3, compression decoding, connection pooling, redirects, or unbounded header/body storage.

## 25. Deferred work

Future phases may add broader HTTP semantics only with explicit limits and new fixtures. The 256-byte default plain-client compatibility behavior remains unchanged; callers requiring Phase 33 framing opt into the larger bounded parser configuration.

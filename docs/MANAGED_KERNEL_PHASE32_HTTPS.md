# Managed Kernel Phase 32 — HTTPS over Managed TCP/TLS/HTTP

## 1. Objective

Phase 32 composes the existing managed DNS, IPv4/ARP/Ethernet, TCPv4, HTTP/1.1, X.509, and Phase 31 TLS 1.2 implementations into an authenticated HTTPS GET path:

```text
managed HTTPS → HTTP/1.1 → TLS 1.2 → TCPv4 → IPv4/ARP/Ethernet → E1000
```

The authoritative request is `GET /phase32` for `www.example.com`. The controlled endpoint returns `HTTP/1.1 200` with the body `phase32-http-pass`.

## 2. Starting Phase 31 architecture

Phase 31 provided a transport-independent TLS 1.2 client for the deliberately narrow profile `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256` (`0xC02B`), TLS 1.2 (`0x0303`), P-256 ECDH, ECDSA/SHA-256, mandatory Extended Master Secret, certificate-chain and hostname validation, fragmented record/handshake parsing, Finished verification, alerts, and deterministic teardown. Its proof transport was a deterministic byte fixture; TCP and HTTP application composition were deferred.

## 3. Final architecture

`ManagedHttpsClient` is the consumer-facing composition layer. It owns the request/response lifecycle and uses `ManagedNetworkService` for DNS and TCP. The service continues to own protocol state and the E1000 path; HTTPS does not introduce sockets, `HttpClient`, `SslStream`, or a parallel HTTP parser.

The NativeAOT proof consumer is `ManagedHttpsKernelProof`, reached through the existing E1000 → Ethernet → IPv4 phase-proof path. The host suite links the same managed source files and uses only a deterministic backend fixture.

## 4. TLS/TCP transport boundary

The internal `IManagedTlsTransport` boundary exposes only:

* connection state;
* poll/progress;
* begin-connect;
* send and receive byte spans;
* close;
* release-for-reuse.

`ManagedNetworkServiceTlsTransport` adapts those operations to `ManagedNetworkService`. TLS has no knowledge of TCP PCB state, Ethernet, E1000 objects, or host networking. The TLS engine remains independently testable.

## 5. HTTP/TLS composition

`ManagedHttpRequestBuilder` remains authoritative for the request. HTTPS supplies its serialized GET bytes to the TLS client; TLS wraps them in application-data records; the adapter sends the resulting bytes through managed TCP. Received TCP bytes are consumed as an arbitrary TLS byte stream, decrypted only after AEAD authentication, and the resulting plaintext is fed to the existing `ManagedHttpResponseParser`.

The request is equivalent to:

```http
GET /phase32 HTTP/1.1
Host: www.example.com
Connection: close
```

## 6. SNI behavior

The requested DNS hostname is encoded in the ClientHello `server_name` extension. The same hostname span is passed to TLS certificate/hostname validation, so SNI and validation cannot silently diverge. Hostname lengths and DNS syntax are bounded by the existing managed DNS/X.509 limits. Host tests compare the emitted ClientHello against the Phase 31 fixture and require `www.example.com`; the QEMU harness performs the same check on the wire.

## 7. Certificate and hostname validation

The authoritative success path uses the Phase 30 trust and validation path with the deterministic Phase 32 root fixture and validation time `2028-01-01T00:00:00Z`. The leaf chain, validity period, requested hostname, ECDSA signature, EMS negotiation, Finished verify, and subsequent AEAD records must all authenticate before HTTP success is possible.

Host tests cover a hostname mismatch and a modified/untrusted root. TLS authentication failures are distinguished from protocol failures and both fail closed.

## 8. TLS application-data record flow

Outbound data follows:

```text
HTTP bytes → TLS application-data record → AES-128-GCM → managed TCP byte stream
```

Inbound data follows:

```text
managed TCP byte stream → TLS record reassembly → AES-GCM authentication/decryption → HTTP parser
```

The TLS client keeps client and server record sequence numbers across the handshake-to-application transition. Each successful application record advances the appropriate direction independently. Decrypted data is queued in a bounded application buffer and drained by HTTPS.

## 9. Fragmentation handling

The TLS record parser accepts arbitrary input chunks and may retain a partial header, partial payload, or partial handshake message. The Phase 32 fixture deliberately exercises record-header fragmentation, record-payload fragmentation, multiple records in one TCP receive, HTTP headers split across TLS records, HTTP body split across TLS records, and TCP segmentation unrelated to TLS boundaries. A valid `Connection: close` response is followed by transport EOF and deterministic close/release.

## 10. Bounded buffering

The implementation retains the established limits:

| Boundary | Limit |
|---|---:|
| TCP payload / HTTPS receive staging | 512 bytes |
| HTTP serialized request | 512 bytes |
| HTTP response status line | 64 bytes |
| HTTP header line | 96 bytes |
| HTTP header bytes | 512 bytes |
| HTTP header count | 16 |
| HTTP body | 256 bytes |
| TLS pending application plaintext | 2048 bytes |
| TLS plaintext fragment | 16 KiB |

Oversized request, response, or pending application data fails rather than becoming unbounded accumulation.

## 11. Lifecycle/state machine

`ManagedHttpsClient` is poll-driven and bounded. Its public states are:

```text
Idle → Resolving → Connecting → Handshaking → Established
     → SendingRequest → ReceivingResponse → Closing → Succeeded
```

Terminal failure and cancellation are explicit states. `BeginGet` performs validation and starts DNS; each `Poll` performs finite progress and returns a stable `NetworkOperationResult`. No blocking socket, task, or indefinite loop is hidden in the managed-kernel path.

## 12. Error handling

Stable HTTPS failure reasons include invalid/oversized request, entropy unavailable, DNS failure, TCP connect/reset, transport failure, TLS authentication/protocol failure, HTTP parse failure, premature close, teardown failure, and cancellation.

Fatal TLS alerts, unexpected record types, malformed records, bad Finished, certificate/hostname failures, bad application-data tags, application data before handshake completion, incomplete TLS records at EOF, malformed HTTP, and body/header limit violations all fail closed. Unauthenticated plaintext is never passed to the HTTP parser.

## 13. Teardown

On successful `Connection: close`, HTTPS accepts transport EOF only after TLS has an established session and no TLS record or handshake fragment is incomplete; the HTTP parser must also report a complete response. `close_notify` is supported by the TLS alert handling but is not mandatory for this controlled HTTP/1.1 close profile. TCP is closed, the service's active TCP/DNS state is released, TLS secrets and parser buffers are cleared, and the service remains available for another request.

Failure and cancellation use the same release boundary. The original terminal `ManagedNetworkService.Teardown()` remains available for whole-service shutdown; HTTPS uses `ReleaseTcpForReuse()` so DHCP configuration is retained.

## 14. GC-survival behavior

The proof consumer forces collection pressure during network setup, after TLS authentication, while receiving the response, and before teardown. Required state is held by the managed client/service objects and fixed buffers rather than accidental temporary roots. The NativeAOT QEMU proof emits `MANAGED_HTTPS_PHASE32_GC_SURVIVAL_PASSED` on the successful path.

## 15. Deterministic test endpoint and fixture

Host and QEMU tests use the checked-in Phase 31 certificate/key/record fixture with a controlled endpoint at `10.15.0.2`, DNS name `www.example.com`, TCP port 443, and path `/phase32`. The QEMU harness responds to the managed DHCP/DNS/ARP/TCP traffic, validates ClientHello/SNI, sends the fragmented TLS server flight, decrypts and validates the encrypted HTTP request, and returns fragmented encrypted HTTP response records. This is a real managed TCP boundary, not plaintext injection into the HTTP parser.

## 16. QEMU proof

Three fresh boots passed with QEMU `11.0.0`. Each boot emitted, exactly once, the required sequence including DNS success, resolved IPv4 `0x000000000A0F0002`, TCP connected, authenticated TLS, encrypted request sent, authenticated/decrypted application data, HTTP 200, expected body, GC survival, teardown, and Phase32 pass. The acceptance wrapper rejects stale evidence directories and missing/duplicate markers.

The final summary is [phase32-summary.log](../artifacts/phase32-boot/phase32-summary.log). Per-boot serial logs and packet captures are under `../artifacts/phase32-boot/runs/`.

QEMU/firmware identity:

```text
QEMU: 11.0.0
OVMF code SHA-256: 33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A
```

## 17. Negative controls

The authoritative negative wrapper corrupts the final byte of the server Finished record, which invalidates the TLS AEAD tag. Three fresh boots passed the negative control: the driver proof was rejected, no encrypted HTTP request marker appeared, no HTTPS or kernel Phase32 pass marker appeared, and no machine fault marker appeared. See [phase32-negative-summary.log](../artifacts/phase32-negative-control-final/phase32-negative-summary.log).

The host failure matrix additionally covers DNS failure, TCP connect failure/reset, close mid-record, bad Finished, bad application tag, malformed/oversized HTTP, unexpected application data, hostname mismatch, untrusted root, and sequence-dependent AEAD failure.

## 18. Regression results

The Phase 32 host suite passed with `MANAGED_KERNEL_PHASE32_HOST_TESTS_PASS cases=69`. Earlier managed-kernel suites were rerun and passed, including Phase 22 TCP (`56` cases), Phase 23 HTTP (`60` cases), and Phase 31 TLS (`33` cases). The counted total from the earlier Phase 15–31/22–23 regression runs is `1312` cases, plus the Phase 14 proof pass marker (that runner does not report a numeric case count).

## 19. Payload size and hash

The final NativeAOT managed-kernel payload is:

```text
Size: 1,673,216 bytes
SHA-256: 27A54D0C1F6590A5A38B28588DDCEFDF0F7C916910DB3E63737CAB0964C4D87F
```

The staged Gate4 ESP payload uses the same size and hash. It is available at [gxos-managed-kernel.dll](../artifacts/managed-kernel-phase32/publish/gxos-managed-kernel.dll).

## 20. Limitations and intentionally deferred features

Phase 32 intentionally remains the Phase 31 TLS 1.2 profile and does not add TLS 1.3, RSA key exchange/certificate expansion, cipher-suite proliferation, HTTP/2, HTTP/3, QUIC, redirects, cookies, caching, proxies, compression, chunked uploads, browser integration, HTML rendering, large downloads, connection pooling, persistent keep-alive, or generalized PKI/CA-bundle redesign.

No public Internet HTTPS dependency was added or used. The existing bare-metal harness has a deterministic controlled endpoint and no host-socket path is permitted in the managed-kernel proof; public interoperability therefore remains an optional follow-up rather than an authority for this phase. The controlled endpoint is the repeatable acceptance authority.

## Result

**Outcome A — Full success.** The managed NativeAOT kernel resolved a hostname, established its own TCP connection, completed authenticated TLS 1.2, encrypted an HTTP request, authenticated/decrypted the response, parsed HTTP, verified the expected body, and released the connection across three fresh QEMU boots. The worktree remains intentionally uncommitted for review.

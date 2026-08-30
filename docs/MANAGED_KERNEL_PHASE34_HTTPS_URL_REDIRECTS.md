# Managed Kernel Phase 34: HTTPS URL Resolution and Redirects

## Outcome

Phase 34 is **Outcome A**: the bounded URL-oriented HTTPS GET path, safe
redirect composition, per-hop TLS authentication, downgrade rejection, host
tests, focused regressions, and deterministic three-boot QEMU proofs pass.

The public-Web probe below is supplementary only.  The authoritative QEMU
fixture deliberately uses an isolated datagram peer, so it does not provide a
live Internet route.

## Objective and starting architecture

Phase 33 already provided the managed path

```text
managed consumer -> HTTPS client -> HTTP/1.1 framing/streaming -> TLS 1.2
                 -> TCPv4 -> DNS/IPv4/ARP/Ethernet -> E1000
```

Phase 34 adds URL input and redirect policy on top of those same layers.  It
does not add a second DNS, TCP, TLS, or HTTP implementation.  The public entry
points are `ManagedHttpsClient.BeginGetUrl(ReadOnlySpan<byte>)`,
`BeginGetUrl(string)`, and the existing hostname/path `BeginGet` overloads.

## Bounded URL representation and grammar

`ManagedHttpsUrl` stores only a canonical lower-case ASCII hostname, a bounded
request target, and a `ushort` port.  Parsing and resolution use stack scratch
buffers; there is no URI framework and no unbounded visited-URL collection.

The supported grammar is the intentionally narrow HTTPS subset:

```text
https://hostname[:port][/path][?query][#fragment]
```

Limits are:

| Value | Limit |
|---|---:|
| Full URL | 512 bytes/chars |
| `Location` reference | 128 bytes |
| Hostname | 253 bytes, using the existing hostname validator |
| Request target/path | 128 bytes |
| Redirects followed | 5 |

The default port is 443.  Explicit non-default ports are parsed and used for
TCP and the HTTP `Host` header.  ASCII hostname matching is case-insensitive;
the stored hostname is lower-case.  Controls, whitespace, NUL, backslash,
malformed ports, port overflow, empty hosts, and invalid host labels fail
closed.  Userinfo and IPv6 literals are rejected because credentials and IPv6
are outside the current managed stack.

Fragments are accepted and stripped.  They never enter the HTTP request
target, DNS lookup, SNI value, or `Host` header.  A URL without a path becomes
`/`; a query-only URL becomes `/?query`.

## Host header and TLS identity

Requests generated from the parsed URL use:

```text
Host: example.test
```

for port 443, and:

```text
Host: example.test:8443
```

for a non-default port.  The scheme, path, fragment, and userinfo are never
included in the header.

For every hop, one hostname is carried consistently through URL resolution,
DNS, TCP destination selection, TLS SNI, certificate hostname validation, and
the logical HTTP origin.  The TLS workspace is reset between hops with the
new hostname, clearing handshake transcript, record sequence state, traffic
keys, certificate state, and authentication result.  The implementation reuses
the already bounded workspace to avoid allocating another certificate-sized
buffer, but it does not reuse a TLS session.

## Redirect policy

GET responses automatically follow 301, 302, 303, 307, and 308.  Phase 34 is
GET-only, so every followed redirect remains a GET.  A redirect must contain a
single bounded `Location` header.  Empty, duplicate, overlong, control-bearing,
NUL-bearing, or malformed values fail closed.

Supported references are:

```text
https://other.example/final       absolute HTTPS
/final                             absolute path
next                              ordinary relative path
../next                            parent-relative path
?page=2                            query reference
//other.example/final              scheme-relative HTTPS
```

Dot segments are normalized within the fixed path buffer.  A reference with
`http:` is rejected as `HttpsDowngrade` before a port-80 connection can be
started.  Other schemes and malformed scheme-bearing references are rejected;
there is no partial `ftp`, `file`, `data`, `javascript`, `ws`, or `wss` support.

The redirect count is bounded at five.  The bound is the loop-safety
mechanism; Phase 34 does not retain an unbounded URL history or perform
explicit cycle detection.  Reaching the bound returns the explicit redirect
limit failure.  Each same-origin and cross-origin redirect tears down the
current TCP/TLS hop, starts fresh DNS, TCP, and TLS work, and then emits the
next GET.

Redirect response bodies are not exposed to the caller, but they are consumed
by the existing bounded HTTP framing engine before the redirect transition.
The fixture uses `Content-Length` plus `Connection: close`, and the managed
client performs the normal peer-FIN/local-close/release-for-reuse sequence.

## Fragmentation and failure coverage

The response parser is fed one byte at a time by the host suite, including a
`Location` split inside the header name and value.  The QEMU fixture splits
TLS application records into 11-byte plaintext chunks, which naturally splits
status lines, header fields, hostnames, ports, paths, and CR/LF boundaries
across TCP/TLS deliveries.  The tests cover malformed URLs, invalid ports,
overflows, invalid Location values, duplicate Location, empty references,
HTTPS downgrade, unsupported schemes, redirect limits/loops, hostname
rebinding, cross-origin TLS failure, teardown/reuse, and GC retention of URL
state.

The Phase 34 host suite reports **140 cases**.  Its GC checks collect after URL
parsing and after relative resolution, then verify the bounded URL remains
usable.  A QEMU `GC.Collect()` probe was also attempted at redirect/TLS/body
transitions, but the call did not return in the current 128 MiB OVMF/E1000
runtime path.  It was therefore not converted into a false success marker.
Host GC coverage passes; the QEMU acceptance marker intentionally covers the
network/TLS/HTTP lifecycle only.

## Deterministic fixture topology

The normal fixture is:

```text
https://www.example.com/phase34/start
  302 Location: /phase34/step2
https://www.example.com/phase34/step2
  301 Location: next
https://www.example.com/phase34/next
  307 Location: https://other.example.com:8443/phase34/final
https://other.example.com:8443/phase34/final
  200 Content-Length: 21
  phase34-redirect-pass
```

The normal proof observes four independent DNS/TCP/TLS/request transitions,
three redirect/status/location transitions, the final status 200, the exact
body, final URL, and clean teardown.  The first three hops use the static
`www.example.com` certificate fixture.  The fourth hop uses a fresh dynamic
TLS flight for `other.example.com` and validates that new origin.

The security negative fixture uses a valid first response redirecting to
`https://bad.example.net/final`, then offers the `www.example.com` certificate
on the second TCP connection.  Three fresh boots prove the first redirect is
authenticated, the second DNS/TCP/TLS attempt occurs, hostname validation
rejects the certificate, no final HTTP success is emitted, and no machine
fault occurs.  Host tests separately prove an `http:` Location is rejected
before any insecure request.

## Verification results

Focused host suites, all passing:

| Suite | Cases |
|---|---:|
| Phase 22 TCP | 56 |
| Phase 23 HTTP | 60 |
| Phase 30 certificates/hostname | 91 |
| Phase 31 TLS | 33 |
| Phase 32 HTTPS | 69 |
| Phase 33 streaming | 185 |
| Phase 34 URL/redirects | 140 |
| Focused total | 634 |

The three fresh normal Phase 34 boots pass under
`artifacts/phase34-final-positive/`.  The three fresh hostname-mismatch
negative boots pass under `artifacts/phase34-final-negative/`.  No separate
broader aggregate runner exists in this repository; the required focused
regression set was rerun directly and all seven suites passed.

The supplementary host connectivity probe resolved `example.com` to
`104.20.23.154` and `172.66.147.243`; host `curl` received HTTP 200 on
2026-08-30.  This was not a managed-kernel interoperability result.  The
authoritative QEMU Phase 34 runner uses an isolated datagram peer and does not
route arbitrary public DNS/TCP, so a managed public-Web attempt was deferred
without weakening the Phase 31 TLS profile.

## Payload and artifacts

Final managed payload:

```text
size: 1,730,560 bytes
SHA-256: 08F926A98042B4B43F8CCBFF4FF20D070B144DCCA2B753F3737716514ABA0396
```

The staged Gate 4 payload is under
`artifacts/gate4-phase34-utc-final/ESP/GXOS/gxos-managed-kernel.dll` and has
the same identity.  QEMU is 11.0.0.  OVMF code SHA-256 is
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.

Authoritative scripts and logs:

```text
tools/Run-ManagedKernelPhase34FreshBoots.ps1
tools/Run-ManagedKernelPhase34NegativeControl.ps1
tools/Run-ManagedKernelPhase11FreshBoots.ps1
artifacts/phase34-final-positive/phase34-summary.log
artifacts/phase34-final-negative/phase34-negative-summary.log
artifacts/phase34-final-positive/runs/run-1/serial.log
artifacts/phase34-final-positive/runs/run-1/injections.log
artifacts/phase34-final-positive/runs/run-1/timeline.log
artifacts/phase34-final-negative/runs/run-1/serial.log
artifacts/gate4-phase34-utc-final/ESP/GXOS/gxos-managed-kernel.dll
```

## Known limitations and deferred work

Phase 34 intentionally does not implement IPv6, IDNA/Unicode hostnames,
cookies, authentication, POST or other methods, proxying, compression,
HTTP/2, HTTP/3, QUIC, TLS 1.3, additional cipher suites, RSA TLS, connection
pooling, HSTS, certificate pinning, or a general RFC 3986 URI library.

Explicit QEMU GC pressure at the transitions listed above remains deferred
until the runtime hang can be isolated.  Public-Web testing through the
managed E1000 proof path is also deferred until an Internet-capable fixture
backend exists.  Neither limitation changes the deterministic Phase 34
security or redirect result.

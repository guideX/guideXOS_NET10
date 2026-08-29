# Managed Kernel Phase 31 — TLS 1.2 ECDHE-ECDSA Handshake

## Outcome and repository state

Phase 31 is Outcome A for the intentionally narrow, transport-independent
profile below. It reaches `Established`, authenticates both Finished messages,
and proves small protected application data. It does not claim TCP, DNS, HTTP,
HTTPS, or general TLS interoperability; those are Phase 32 work.

The work started on branch `nativeaot-managed-kernel-integration` at
`6c0424c71e9b72a0e0bb1bef8c1db4fb43af48f9` (`Phase 30`). The upstream was
`ahead=0 behind=0`, and the starting worktree had no staged, unstaged, or
untracked changes. Phase 30 was already committed in repository reality and
was preserved. Nothing was pushed.

New files are `src/ManagedKernel/ManagedTls12Client.cs`,
`src/ManagedKernel/ManagedTls12Prf.cs`,
`src/ManagedKernel/ManagedTls12RecordProtection.cs`,
`src/ManagedKernel/ManagedTls12KernelProof.cs`,
`src/ManagedKernel/ManagedTls12Phase31Fixtures.cs`,
`src/ManagedKernelPhase31HostTests/ManagedKernelPhase31HostTests.csproj`,
`src/ManagedKernelPhase31HostTests/Program.cs`,
`tools/phase31-fixtures/ReferenceFixtureGenerator.csproj`,
`tools/phase31-fixtures/Program.cs`,
`tools/phase31-fixtures/Generate-Phase31Fixture.ps1`,
`tools/Run-ManagedKernelPhase31HostTests.ps1`, and
`tools/Run-ManagedKernelPhase31FreshBoots.ps1`. Phase 31 also updates the
managed HMAC/P-256 implementations, the NativeAOT project, and the Gate4
loader/build scripts.

## Exact profile and standards

| Item | Supported value |
| --- | --- |
| Protocol | TLS 1.2, exactly `0x0303` |
| Cipher suite | `TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256`, `0xC02B` |
| Key exchange | ECDHE, named P-256/secp256r1 (`0x0017`) |
| Authentication | ECDSA/SHA-256 (`0x0403`) |
| Record cipher | AES-128-GCM |
| PRF | TLS 1.2 P_SHA256/HMAC-SHA256 |
| Master secret | RFC 7627 Extended Master Secret, mandatory |
| Certificate policy | Phase 30 narrow DER/X.509 policy |

The implementation follows RFC 5246, RFC 5288, RFC 5289, RFC 8422, and RFC
7627. TLS 1.3, SSLv3, TLS 1.0/1.1, RSA, DHE, PSK, anonymous suites, CBC,
ChaCha20, AES-256, SHA-1 signatures, client certificates, CertificateRequest,
renegotiation, resumption, session tickets, 0-RTT, ALPN, OCSP stapling,
compression, DTLS, and unsupported extensions are rejected or unsupported.

## Architecture and state machine

`ManagedTls12Client` consumes arbitrary caller-owned byte chunks through
`TryConsume` and produces caller-drained bytes through `TryStart` and
`TryTakeOutput`. It contains no socket, stream, task, async operation,
scheduler, DNS lookup, or network timing. Construction binds one ASCII hostname,
one configured root, one current UTC time, one `ManagedSecureRandom`, and one
caller-owned work buffer. The same hostname bytes are used for SNI and Phase 30
hostname validation.

The public projection is `Created`, `NeedInput`, `OutputReady`, `Established`,
`Closed`, or `Failed`. The internal progression is:

```text
Created -> ClientHello -> ServerHello -> Certificate -> ServerKeyExchange
        -> ServerHelloDone -> ClientFlight -> Server CCS
        -> encrypted Server Finished -> Established -> Closed or Failed
```

`Teardown` clears and closes the session. `TryReset` is explicit and only
reopens a torn-down instance with a caller-supplied entropy provider; it reuses
the bounded buffers for allocator-safe retry/recovery.

## Record layer and fragmentation

Records are `ContentType || 0x0303 || uint16 length || fragment`. The parser
handles partial five-byte headers, partial bodies, several records in one
chunk, and one record split across arbitrary chunks. It accepts only content
types ChangeCipherSpec (`20`), Alert (`21`), Handshake (`22`), and
ApplicationData (`23`). Lengths are checked before copying. The narrow profile
rejects zero-length records; CCS is exactly `01`, plaintext alerts are exactly
two bytes, and protected records must contain nonce and tag bytes.

The maximum plaintext fragment is 16,384 bytes. AES-GCM adds an eight-byte
explicit nonce and 16-byte tag, giving a 16,408-byte ciphertext fragment and a
16,413-byte complete record including its header.

Handshake messages are `type || uint24 length || body`. ServerHello,
Certificate, ServerKeyExchange, ServerHelloDone, and Finished are accepted;
ClientHello, ClientKeyExchange, and Finished are emitted. Messages may span
any number of records and chunks. CCS or Finished is rejected while an earlier
message is incomplete. General messages are limited to 4,096 bytes;
Certificate is limited to 49,152 bytes, four peer certificates, and the Phase
30 16 KiB individual-certificate limit. Reassembly starts at 4,100 bytes and
grows only to 49,156 bytes including its four-byte header.

The transcript stores exact encoded handshake messages, including handshake
headers, from ClientHello through the current message. Record headers, CCS,
and alerts are excluded. Hash snapshots are taken without changing the
continuing transcript at the EMS, client Finished, and server Finished points.
The current Finished is excluded from its own verify-data calculation; client
Finished is included when verifying server Finished.

## ClientHello and ServerHello

ClientHello uses version `0x0303`, exactly 32 secure-random bytes, an empty
session ID, only suite `0xC02B`, and only null compression. It sends SNI
`server_name` for the configured ASCII host, `supported_groups` containing
only secp256r1, `ec_point_formats` containing only uncompressed, the sole
signature pair SHA-256/ECDSA (`04 03`), and zero-length
`extended_master_secret` (`23`). There is no timestamp-derived random.

ServerHello must select exactly TLS 1.2, `0xC02B`, compression zero, and the
zero-length EMS extension. A session ID is bounded and parsed but never cached
or used for resumption. Duplicate, malformed, missing, contradictory, or
unknown unsolicited extensions fail closed. The server cannot select a
capability that was not offered.

## Certificate and trust-anchor integration

Certificate parsing enforces the uint24 list length, exact uint24 entries,
nonzero certificates, list accounting, count, individual size, truncation,
and no trailing bytes. Certificates are copied into the caller's work buffer
and parsed with Phase 30.

The first peer certificate is the leaf. Remaining peer certificates are
intermediates or a final root candidate. Trust comes only from the configured
root. A peer final certificate is an anchor only when byte-for-byte equal to
that root. When the wire omits the root, the configured root is supplied
logically to `ManagedX509.TryValidateServerChain`. A different self-verifying
peer root is not trusted. Phase 30 then performs exact-TBS signatures, P-256
SPKI checks, validity, CA/path length, key usage, EKU, SAN/CN hostname rules,
and configured-root validation. The narrow policy has no OS trust store,
revocation, AIA, name constraints, or general PKI path building.

## ServerKeyExchange and ECDH

ServerKeyExchange requires exact named-curve parameters `03 00 17`, a 65-byte
SEC1 uncompressed finite on-curve P-256 point through the Phase 28 validator,
signature algorithm `04 03`, and bounded DER ECDSA. The exact signed input is

```text
client_random || server_random || ServerECDHParams
```

where `ServerECDHParams` is the exact curve/point wire encoding. The digest is
verified with the authenticated Phase 30 leaf key through the Phase 29
verifier. After ServerHelloDone, a fresh P-256 scalar is generated from
`ManagedSecureRandom`, the uncompressed public point is derived, and ECDH
produces the 32-byte affine X-coordinate with leading zeroes preserved.
ClientKeyExchange encodes point length 65 and is appended exactly to the
transcript.

## PRF, EMS, key expansion, and CCS

`ManagedTls12Prf` implements RFC 5246 P_SHA256 with the managed HMAC:

```text
A(0) = label || seed
A(1) = HMAC(secret, A(0))
A(i) = HMAC(secret, A(i-1))
output blocks = HMAC(secret, A(i) || label || seed)
```

EMS derives 48 bytes as
`PRF(premaster, "extended master secret", SHA256(ClientHello..ClientKeyExchange))`.
The legacy `"master secret"` random-pair seed is never used. The premaster is
cleared after derivation. Key expansion uses `server_random || client_random`
and extracts `client_write_key[16]`, `server_write_key[16]`,
`client_write_IV[4]`, and `server_write_IV[4]`, with no CBC/MAC keys.

Outbound order is ClientKeyExchange, plaintext CCS `01`, activate client write
state and sequence zero, then encrypted Finished. Inbound order is valid
plaintext CCS, activate server read state and sequence zero, then encrypted
Finished. Early, duplicate, malformed, or plaintext post-CCS transitions fail.

## AES-GCM records and Finished

The TLS nonce is `fixed_iv[4] || explicit_nonce[8]`. Outbound explicit nonce
is the current big-endian 64-bit sequence. The AAD is exactly
`sequence || content_type || 0x0303 || uint16(plaintext_length)`. Sequence
exhaustion is rejected and increments occur only after success. Inbound uses
the transmitted explicit nonce while retaining the sequence in AAD; GCM
authentication completes before plaintext output is written.

Client Finished is the 12-byte `client finished` PRF value over the transcript
through ClientKeyExchange, then its exact handshake encoding is appended and
encrypted. Server Finished is verified over the transcript including client
Finished, appended only after verification, and transitions to Established.
Established sessions encrypt/decrypt bounded ApplicationData. The proof uses
synthetic `PING`/`PONG` and an encrypted `close_notify`; fatal alerts and
renegotiation/HelloRequest attempts fail closed.

## Teardown and entropy

`Fail` and `Teardown` clear randoms, ephemeral scalar, public and premaster
buffers, master/key material, fixed IVs, transcript, HMAC state/pads,
certificate work storage, handshake, record, GCM, and application buffers.
Logs contain only status markers and never print secrets, verify data, keys,
IVs, or plaintext.

Production initialization uses the Phase 26 virtio-rng-backed
`ManagedSecureRandom` for 32 ClientHello bytes and a valid fresh ephemeral
scalar/public key. Entropy unavailability fails before handshake initiation.
The deterministic fixture provider is reachable only from the separated proof
path; it is never selected as a production fallback.

## Fixture provenance

`tools/phase31-fixtures/Generate-Phase31Fixture.ps1` builds the host-only
`ReferenceFixtureGenerator` and emits `ManagedTls12Phase31Fixtures.cs`.
OpenSSL creates the P-256 root/intermediate/leaf DER chain and a host-only
reference uses framework SHA-256, HMAC-SHA256, and AES-GCM to generate the
transcript and records. OpenSSL and framework crypto are not production
dependencies. The fixture uses client random `00..1F`, scalar one only in the
explicit test provider, server random `A0..BF`, leaf plus intermediate on the
wire, and the configured root omitted from the peer chain.

## Bounds and tests

The client has a 65,536-byte caller certificate store, 5-byte header,
16,408-byte body, 16,413-byte record copy, 16,384-byte plaintext buffer,
4,100-to-49,156-byte bounded handshake buffer, 512-byte outbound flight, and
16,384-byte application buffer. It also has fixed secret/hash/key arrays,
four certificate result structs, and one reusable HMAC workspace. The
transcript is capped at 64 KiB; PRF/GCM scratch uses stack spans. No generic
stream or unbounded collection is used.

The host runner reports `MANAGED_KERNEL_PHASE31_HOST_TESTS_PASS cases=33`.
Those tests cover independent PRF/EMS KATs, record framing and GCM/AAD
failures, exact ClientHello and client flight, one-byte fragmented handshake,
Finished and application data, close-notify, teardown, missing EMS, malformed
Finished, ordering, entropy-unavailable failure, and recovery. Retained
regressions are Phase 15–29 `1,188/1,188`, Phase 30 `91/91`, and Phase 15–30
aggregate `1,279/1,279`.

## NativeAOT result and import audit

The final payload is
`artifacts/managed-kernel-phase31-final/publish/gxos-managed-kernel.dll`,
1,652,224 bytes, SHA-256
`D7F91113B443DE7428887F4F4A94A5E7CA3339E36840FFD8136D6642E339DFE8`, and
exports `GxManagedKernelRunPhase31`. The Gate4 staged copy has the same
identity. The PE report is
`artifacts/managed-kernel-phase31-final/managed-kernel-pe-report.txt`.

The production source/PE path contains no `SslStream`, Schannel/SSPI TLS,
`System.Net.Security`, framework X.509/ASN.1, `ECDiffieHellman`, `ECDsa`,
hosted AES-GCM, OpenSSL, libssl, libcrypto, Crypt32, NCrypt, or CommonCrypto
dependency. The pre-existing `bcrypt.dll!BCryptGenRandom` import remains;
managed TLS operations do not delegate to it. Hosted crypto names are
confined to host tests and the fixture generator.

## Three fresh authoritative boots

`tools/Run-ManagedKernelPhase31FreshBoots.ps1` passed three new QEMU processes
using evidence directory
`artifacts/managed-kernel-phase31-boots-authoritative4/`.

```text
QEMU: QEMU emulator version 11.0.0 (v11.0.0-12122-ga4bb4b10c9)
OVMF code SHA-256: 33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A
OVMF vars SHA-256: 5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E
```

| Boot | Serial SHA-256 | Result |
| --- | --- | --- |
| 1 | `AAEBB6C15FFBFDF497647F266A7652B88657FE472FE8AB5F73EA01EBD01FFADA` | PASS |
| 2 | `4A8BE2F0A34AB19ADD00F380625857ABFAC2A25C64C468DBBCAFB68050F540CF` | PASS |
| 3 | `F59D3388860F2FDD35BDEFB695D04963061268EFF42A0ED9DF2E57D2639959C0` | PASS |

Each log has exactly one `GXOS_NET10:MANAGED_KERNEL_PHASE31_PASS`, all
required Phase 26–30/TLS/negative markers, no forbidden fault or unexpected
import markers, and the runner confirmed no test-owned QEMU remained.

## Capability matrix and Phase 32 boundary

| Capability | Status |
| --- | --- |
| Entropy, SHA-256, HMAC-SHA256 | Proven |
| AES-128, GHASH, AES-GCM | Proven |
| P-256 ECDH, P-256 ECDSA verification | Proven |
| Bounded DER, narrow X.509, chain, hostname | Proven; reused from Phase 30 |
| TLS 1.2 PRF, EMS, AES-GCM records | Proven; EMS mandatory |
| ECDHE-ECDSA handshake and encrypted Finished | Proven for `0xC02B`/P-256 |
| Established transport-independent session | Proven |
| TCP/TLS, DNS, remote HTTPS, HTTP | Deferred to Phase 32 |
| TLS 1.3, RSA, general PKI/revocation | Unsupported |

The inherited Phase 27 direct-crypto-state `GC.Collect()` limitation remains
known and unchanged; it does not block Outcome A. Phase 31 adds no new
mandatory GC boundary and reuses large buffers across rejection/recovery.
Phase 32 may place this engine behind the existing TCP service, then add DNS
and HTTP as separately scoped features while preserving EMS, exact transcript,
certificate policy, GCM AAD/sequence rules, and the no-hosted-TLS boundary.

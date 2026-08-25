# Managed TLS Phase 24 cryptographic capability audit

Status: Outcome C. The audit stops before TLS protocol implementation because
the current managed/native boundary does not provide a cryptographically
credible client entropy source or a bare-metal-proven asymmetric primitive.
No TLS cipher suite, TLS record parser, `ManagedTlsClient`, or deterministic
TLS peer was added. This is intentional: a TLS-shaped protocol without these
primitives would not be genuine authenticated TLS.

The executable audit is
`tools/Invoke-ManagedTlsPhase24CryptoAudit.ps1`. It inspects the current
Phase 23 NativeAOT payload, the managed-kernel source, the local host framework
surface, NativeAOT imports, and Git identity. It does not execute host crypto
operations and does not add a crypto dependency to the kernel. Its retained
report is under
`evidence/phase24-crypto-audit-20260825/` when the audit is run.

## Repository and payload findings

The repository is clean at the Phase 23 commit `0f0258b` on
`nativeaot-managed-kernel-integration`; the prompt-described uncommitted
Phase 22/23 baseline is not present in this checkout. The current authoritative
Phase 23 payload is 1,237,504 bytes with SHA-256
`D936958D695D970C63920885FECB6CEFBAF7C4AAB78EFE495DF93FB46E16CA35`.

The managed-kernel source contains no repository-owned implementation of
SHA-256, HMAC-SHA256, AES, AES-GCM, AES-CBC, RSA, ECDSA, ECDH, P-256,
constant-time comparison, or big-integer arithmetic. It also contains no
`System.Security.Cryptography` reference, no `SslStream`, no `HttpClient`, no
socket path, and no direct entropy abstraction.

The NativeAOT PE import table contains `bcrypt.dll!BCryptGenRandom` as part of
the runtime PAL surface. Existing dependency-census evidence classifies this
import as an unimplemented/fail-fast boundary. It is not reached by the
successful managed-kernel path, and the existing three-boot Phase 23 evidence
does not prove a random-byte call. The loader’s proven firmware time path feeds
the NativeAOT startup security cookie; it is not a CSPRNG and cannot be used as
TLS client randomness or key material.

The local host runtime exposes framework type names for SHA-256, HMAC-SHA256,
`RandomNumberGenerator`, AES, `AesGcm`, RSA, ECDSA, ECDH,
`CryptographicOperations`, and `BigInteger`. That is host API availability
only. Depending on the API and platform, framework implementations may use
Windows CNG, OpenSSL, a native PAL, OS certificate services, or other runtime
components. No such API is admitted into the kernel until its complete
NativeAOT bare-metal dependency and execution path is demonstrated. The audit
therefore records these types as unproven rather than silently treating host
success as kernel support.

## Capability matrix

| Capability | Repository-owned code | Host type surface | Bare-metal proof | Phase 24 decision |
| --- | --- | --- | --- | --- |
| SHA-256 | none | present | none | blocked |
| HMAC-SHA256 | none | present | none | blocked |
| Secure/random bytes | none | present | none; `BCryptGenRandom` is fail-fast/unreached | first blocker |
| AES | none | present | none | blocked |
| AES-GCM/CBC | none | present | none | blocked |
| RSA signature verification | none | present | none | blocked |
| ECC/P-256 | none | present | none | blocked |
| ECDH | none | present | none | blocked |
| ECDSA | none | present | none | blocked |
| Constant-time equality | none | present | none | blocked |
| Big integer arithmetic | none | present | none | blocked |

The first exact blocker is the missing genuine entropy source. Even if a
managed AES-GCM implementation were added, a TLS client with predictable
ClientHello randomness, ephemeral private key, or nonce state would not meet
the acceptance contract. The independent authentication/key-exchange blocker
is the lack of a bare-metal-proven RSA/ECDSA/ECDH substrate for the
deterministic peer trust model.

## Protocol and architecture decision

TLS 1.2 remains the intended next protocol version because its deliberately
bounded handshake is a better fit for the current TCP contract than adding TLS
1.3 prematurely. No cipher suite was selected: the candidate
`TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256` (numeric ID `0xC02F`) remains an audit
candidate only, not an implementation commitment. Selecting it before proving
entropy, ECDH, RSA verification, AES-GCM, and the TLS 1.2 PRF would be
backwards reasoning.

The intended future architecture remains:

```text
managed application
        |
        v
ManagedTlsClient       (not implemented in Outcome C)
        |
        v
ManagedNetworkService  (Phase 23 boundary retained unchanged)
        |
        v
TCPv4 -> IPv4 / ARP / Ethernet -> E1000
```

No TLS-facing API is exposed in this outcome. Consequently there are no
selected TLS record, handshake, application-data, key-material, or receive
capacities to report; adding capacities without an implementation would imply
support that does not exist. Existing Phase 23 TCP/DNS capacities remain
unchanged and all previous networking functionality is preserved.

## Safe next prerequisite

The next phase should first add one narrowly scoped, independently tested
entropy boundary. The preferred order is a genuine firmware/CPU entropy source
exposed through a small checked native contract, followed by a repository-owned
managed implementation of only the cryptographic primitives needed by one TLS
1.2 suite, or a framework primitive whose complete NativeAOT/bare-metal
dependency is proven. The primitive work must include known-answer tests and a
real NativeAOT payload execution test before any TLS record or handshake code
is accepted.

Until then, Phase 24 must not claim HTTPS, public CA trust, X.509 validation,
Internet TLS interoperability, encrypted application data, or a chosen cipher
suite. The Phase 23 DNS -> TCP -> HTTP evidence remains authoritative and
unmodified.

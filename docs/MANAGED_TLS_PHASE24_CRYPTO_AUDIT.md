# Managed TLS Phase 24 cryptographic capability audit

Status: Phase 24 remains Outcome C for TLS. Phase 25 has since completed the
owned SHA-256, HMAC-SHA256, and constant-time comparison substrate, but the
authoritative QEMU CPU still provides no usable RDSEED/RDRAND entropy source,
so TLS protocol implementation remains blocked.
No TLS cipher suite, TLS record parser, `ManagedTlsClient`, or deterministic
TLS peer was added. This is intentional: a TLS-shaped protocol without these
primitives would not be genuine authenticated TLS.

The executable Phase 24 audit is
`tools/Invoke-ManagedTlsPhase24CryptoAudit.ps1`. It inspects the current
Phase 23 NativeAOT payload, the managed-kernel source, the local host framework
surface, NativeAOT imports, and Git identity. It does not execute host crypto
operations and does not add a crypto dependency to the kernel. Its retained
report is under
`evidence/phase24-crypto-audit-20260825/` when the audit is run.

## Repository and payload findings

The Phase 24 audit baseline was the Phase 23 parent `0f0258b` on
`nativeaot-managed-kernel-integration`. The audit artifacts were subsequently
recorded in the current HEAD `67b67b0`, whose subject is
`## Managed Cryptographic Foundation I — SHA-256, HMAC-SHA256, and Secure Entropy`.
The current authoritative Phase 23 payload was 1,237,504 bytes with SHA-256
`D936958D695D970C63920885FECB6CEFBAF7C4AAB78EFE495DF93FB46E16CA35`.

At the Phase 24 audit point, the managed-kernel source contained no
repository-owned implementation of
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

Phase 25 removed the hash and comparison rows from that blocker list, but the
first remaining exact blocker is still the missing genuine entropy source. Even if a
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

Phase 25 is the follow-up to this audit. It added the owned hash/MAC substrate
and audited a genuine CPU entropy boundary; the target QEMU configuration
reported `RDRAND=0` and `RDSEED=0`, so the production service fails closed and
the overall Phase 25 result is Outcome C. The next phase must add a credible
platform entropy source (or explicitly change the target platform contract),
then prove the remaining TLS primitives before any TLS record or handshake is
accepted.

The historical Phase 24 recommendation was:

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

## Phase 25 follow-up: managed cryptographic foundation

The Phase 25 implementation is retained in `ManagedSha256.cs`,
`ManagedHmacSha256.cs`, `ManagedSecureRandom.cs`, and the native boundary
`managed_kernel_entropy.c/.h`. The managed hashes are incremental, bounded,
framework-independent implementations. HMAC uses the 64-byte SHA-256 block,
pre-hashes long keys, and clears temporary key material and inner/outer state
on teardown. `ManagedCryptoComparison.FixedTimeEquals` folds length mismatch
into the accumulator and compares all bytes in the common range without a
first-difference branch.

The standardized fixtures are the FIPS 180-4 SHA-256 vectors, including empty,
`abc`, the multi-block vector, and 55/56/63/64/65-byte boundaries, plus RFC
4231 HMAC-SHA256 vectors. The dedicated host suite passes exactly 113 cases,
including segmentation, byte-at-a-time updates, reset/reuse, GC survival,
HMAC long-key preprocessing, corrupted-MAC mismatch, constant-time comparison,
deterministic test-provider injection, unavailable/failure recovery, bounded
fills, and production-provider fail-closed behavior.

Production `ManagedSecureRandom` accepts only a caller buffer up to 1,024
bytes. The native x64 service detects CPUID leaf 1 ECX bit 30 (RDRAND) and
leaf 7 EBX bit 18 (RDSEED), prefers RDSEED per word, falls back to RDRAND,
checks the instruction carry flag, retries at most 10 times, and returns an
explicit failure without deterministic or time-derived fallback. No DRBG,
firmware RNG protocol, or virtio-rng device is assumed. Host tests inject a
named deterministic provider; production construction has no path to that
provider.

The authoritative CPU configuration is the existing QEMU command line from
`Run-ManagedKernelPhase25FreshBoots.ps1`: q35, single-threaded TCG, 128 MiB,
and no explicit `-cpu` override. Three standalone fresh boots executed the
managed proof in the NativeAOT kernel and all passed SHA-256, HMAC, GC,
comparison, reset, and teardown markers. All three reported CPUID maximum
basic leaf `0xD`, leaf-1 ECX `0x80002001`, leaf-7 EBX `0x0`, feature flags
`0x0`, and `MANAGED_ENTROPY_PROVIDER_UNAVAILABLE=1`; the managed service then
proved random-fill failure is fail-closed. This is an entropy unavailability
proof, not a claim of secure randomness.

The Phase 25 payload is 1,253,888 bytes with SHA-256
`98D945E9508FF83ADC9C536D68CE59072F113435210DFF664DE539B260061735`.
The normalized import set is unchanged from Phase 23. The retained
`bcrypt.dll!BCryptGenRandom` import is the existing NativeAOT runtime/PAL
random-byte boundary identified by `docs/DEPENDENCY_CENSUS.md`; Phase 25 does
not reference it, and none of the three boots reached the loader’s fail-fast
unexpected-import marker. The Phase 25 audit is reproducible with
`tools/Invoke-ManagedTlsPhase25CryptoAudit.ps1` and retained under
`evidence/phase25-crypto-foundation-20260825-final4/`.

### Remaining TLS prerequisite matrix after Phase 25

| TLS prerequisite | Status after Phase 25 |
| --- | --- |
| Secure entropy | Blocked: target QEMU CPUID exposes neither RDRAND nor RDSEED; service fails closed |
| SHA-256 | Proven: owned managed implementation, host vectors, three bare-metal boots |
| HMAC-SHA256 | Proven: owned managed implementation, RFC 4231 vectors, three bare-metal boots |
| Constant-time equality | Proven for bounded byte comparison; no broader timing claim |
| TLS 1.2 PRF building blocks | Available as primitives; integration not implemented |
| AES-128 | Missing |
| GCM | Missing |
| ECDH P-256 | Missing |
| RSA/ECDSA verification | Missing |
| X.509 narrow parser | Missing |
| TLS state machine/records | Deferred |

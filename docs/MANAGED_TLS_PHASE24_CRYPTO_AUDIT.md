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

Phase 25 removed the hash and comparison rows from that blocker list. Phase 26
now supplies a bounded QEMU virtio-rng provider for the managed-kernel
integration proof, but the TLS blocker remains until the target TLS payload
and its complete key-exchange/cipher dependency path are implemented. Even if a
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

## Phase 26 entropy follow-up

Phase 26 adds `ManagedVirtioRngDriver.cs`, `ManagedVirtioRngProtocol.cs`, and
the bounded router in `ManagedSecureRandom.cs`. Provider order is native
RDSEED/RDRAND first, then modern non-transitional virtio-rng; there is no
timing-derived or deterministic fallback. The host suite covers
unavailable-provider fail-closed behavior and bounded queue semantics. Three
QEMU fresh boots proved queue completion, explicit GC survival, teardown, and
reinitialization reuse.

This is a genuine entropy-provider integration proof for the QEMU target, not
a TLS implementation or an Internet interoperability claim. The historical
`bcrypt.dll!BCryptGenRandom` NativeAOT import remains in the PE surface as an
unimplemented runtime/PAL boundary; Phase 26 does not call it.

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

## Phase 27 symmetric foundation follow-up — Outcome B

Phase 27 now supplies a real managed AES-128, GHASH, and AES-GCM foundation
without adding TLS records or handshake code. `ManagedAes128` implements the
FIPS-197 AES-128 encryption schedule and rounds. `ManagedGhash` implements
SP 800-38D fixed-state GHASH, including partial-block padding and the AAD/
ciphertext bit-length block. `ManagedAesGcm` is intentionally narrow: exactly
16-byte keys, 12-byte nonces, 16-byte tags, at most 256 bytes of AAD, and at
most 16 KiB of plaintext/ciphertext. It constructs `J0 = nonce || 1`, uses a
big-endian low-32-bit counter, rejects exhaustion, and exposes no public CTR
mode.

GCM decryption computes and compares the authentication tag before GCTR and
before writing the caller's plaintext buffer. Authentication failure leaves
the output untouched. Expected malformed calls fail explicitly without
uncontrolled exceptions. State and temporary cryptographic material are
cleared on operation completion or reset. Reusing a nonce with the same key
is catastrophic and prohibited; nonce uniqueness is a caller policy, not a
global registry or automatic GCM feature.

The dedicated Phase 27 host suite passes 100/100 cases, including FIPS-197,
SP 800-38A, SP 800-38D, GHASH, capacity, corruption, no-plaintext, recovery,
lifecycle, GC, and independent host-oracle checks. Three fresh NativeAOT
boots each execute and pass the AES, GHASH, GCM encryption/decryption,
invalid-tag, no-plaintext, reset/reuse, and Phase 26 virtio-rng nonce markers.
The strict Phase 27 runner does not classify the result as Outcome A because
the direct crypto-state GC checkpoint exits after its begin marker in the
current NativeAOT/kernel runtime. The established Phase 26 secure-random GC
proof and host AES expanded-key GC proof remain passing. This is a runtime
boundary and is recorded as Outcome B; it is not a vector-correctness failure.

The final payload is 1,314,816 bytes with SHA-256
`124B02BF07966654AC08D578F6BC07EB252EAD8BE28846D0EB1D1153F55C26A2`.
Import inspection shows the pre-existing `bcrypt.dll!BCryptGenRandom` runtime
PAL import and no BCrypt AES APIs, OpenSSL, libcrypto, CommonCrypto, or hosted
AES PAL imports. The Phase 27 design, vectors, boot records, regression
totals, and audit are retained under
`evidence/managed-kernel-phase27-authoritative-final24/`.

### Updated TLS prerequisite matrix

| TLS prerequisite | Status after Phase 27 |
| --- | --- |
| Secure entropy | Proven |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| TLS 1.2 PRF building blocks | Available |
| AES-128 | Proven functional; Outcome B crypto-GC caveat |
| GCM | Proven functional; Outcome B crypto-GC caveat |
| ECDH P-256 | Missing |
| RSA/ECDSA verification | Missing |
| X.509 parser | Missing |
| TLS state machine | Deferred |

## Phase 28 managed P-256 ECDH follow-up — Outcome A

The managed TLS crypto substrate now includes a self-contained P-256 ECDH
primitive. The implementation is limited to NIST P-256/secp256r1 and SEC1
65-byte uncompressed points. It validates private scalars and peer points,
uses fixed-width bounded arithmetic and a fixed 256-step scalar loop, and
returns the raw 32-byte shared X coordinate. Key generation is rejection
sampling exclusively through the Phase 26 virtio-rng-backed
`ManagedSecureRandom`; unavailable entropy fails closed.

The Phase 28 host suite passes 188/188 cases. The authoritative NativeAOT
payload is 1,341,952 bytes with SHA-256
`DC431B422D1D8B53690A30882F24CA215A85A5FAC7D558C54AEA0984BA248211`. Three
fresh QEMU boots passed from the evidence directory
`evidence/managed-kernel-phase28-authoritative-final7/`, including the
Phase 26 secure-random proof, Phase 27 AES/GHASH/GCM regression, malformed
point and scalar rejection, and unchanged-output-on-failure markers.

This is an ECDH primitive milestone, not TLS capability: RSA verification,
ECDSA verification, X.509 parsing, record protection integration, and the TLS
handshake/state machine remain absent. The import audit retains only the
pre-existing `bcrypt.dll!BCryptGenRandom` runtime/PAL import and found no
hosted P-256 or forbidden ECC dependency. The known Phase 27 direct
crypto-state `GC.Collect()` boundary remains documented and is not repeated as
a mandatory Phase 28 criterion.

### TLS prerequisite matrix after Phase 28

| TLS prerequisite | Status |
| --- | --- |
| Secure entropy | Proven |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| AES-128 | Proven |
| GHASH | Proven |
| AES-GCM | Proven |
| TLS PRF building blocks | Available |
| P-256 ECDH | Proven |
| RSA verification | Missing |
| ECDSA verification | Missing |
| X.509 | Missing |
| TLS handshake/state machine | Deferred |

## Phase 29 managed P-256 ECDSA verification — Outcome A

Phase 29 adds a self-contained managed ECDSA P-256/SHA-256 verification
primitive. It uses a separate fixed-width scalar representation modulo the
P-256 subgroup order `n`, fixed 512-bit product reduction, fixed exponent
inversion by `n-2`, the Phase 28 strict SEC1 public-key validator and Jacobian
point machinery, and a bounded canonical DER parser for exactly two positive
INTEGERs. It accepts raw 32-byte `r`/`s` values or the bounded DER
`ECDSA-Sig-Value`; it does not implement signing, X.509, certificate
authentication, RSA, or TLS handshake processing.

The Phase 29 host suite passes 209/209 cases. The retained regressions pass
Phase 15–26 691/691, Phase 27 100/100, and Phase 28 188/188. Three fresh
authoritative NativeAOT boots, payload identity, QEMU/OVMF identity, serial
logs, and import evidence are recorded under
`evidence/managed-kernel-phase29-authoritative-final5/`.

### TLS prerequisite matrix after Phase 29

| TLS prerequisite | Status |
| --- | --- |
| Secure entropy | Proven |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| AES-128 | Proven |
| GHASH | Proven |
| AES-GCM | Proven |
| TLS PRF building blocks | Available |
| P-256 ECDH | Proven |
| P-256 ECDSA verification | Proven |
| RSA verification | Missing |
| X.509 certificate validation | Missing |
| TLS handshake/state machine | Deferred |

ECDSA verification is an input primitive for future certificate processing;
it is not certificate authentication by itself.

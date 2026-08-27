# Managed Kernel Phase 27 — AES-128 and AES-GCM

Status: Outcome B. The managed AES-128, GHASH, and AES-GCM implementation is
functionally complete and independently verified, and the real NativeAOT
kernel executes the AES/GHASH/GCM known-answer and authentication-failure
proofs. A direct Phase 27 `GC.Collect()` checkpoint with AES state live is
blocked by the current NativeAOT/kernel runtime boundary; it is not reported
as a passing GC proof. The strict fresh-boot runner records this limitation.

## Scope and implementation

Phase 27 is a primitive phase. It adds no TLS records, handshake, HTTPS,
ECDH, ECDSA, RSA, X.509, certificate validation, generic provider hierarchy,
OS crypto wrapper, or public CTR API.

`ManagedAes128` implements FIPS-197 AES-128 encryption: a 16-byte key, a
16-byte block, the standard 176-byte/11-round-key expansion, AddRoundKey,
SubBytes, ShiftRows, MixColumns, and the final round without MixColumns. The
expanded schedule is stored as a 176-byte primitive-only sequential value
layout rather than a managed byte-array field. The S-box is computed by fixed-
loop GF(2^8) inversion and the AES affine transform; there is no large managed
S-box lookup table. This is a small portable implementation, not a claim of
complete constant-time or microarchitectural side-channel resistance.

`ManagedGhash` is incremental and fixed-state. It uses the MSB-first,
bit-serial GF(2^128) multiply from GCM with reduction constant `E1 || 0^120`.
AAD and ciphertext are consumed in caller-provided segments, partial final
blocks are zero-padded, and the final block contains the 64-bit big-endian AAD
bit length followed by the 64-bit big-endian ciphertext bit length. It never
buffers unbounded AAD or ciphertext.

`ManagedAesGcm` is a narrow one-shot profile:

* key: exactly 16 bytes (AES-128)
* nonce: exactly 12 bytes (96-bit profile)
* tag: exactly 16 bytes (128-bit tag)
* AAD: at most 256 bytes
* plaintext/ciphertext: at most 16,384 bytes
* input and output buffers: caller-owned; unsupported overlaps are rejected

For a 96-bit nonce, `J0 = nonce || 00000001`. GCTR increments only the low
32-bit counter in big-endian order and rejects exhaustion instead of wrapping.
The profile's 16 KiB limit is far below the theoretical GCM counter limit and
matches the later TLS 1.2 record-size planning boundary.

Encryption derives `H = AES_K(0^128)`, encrypts with GCTR, GHASHes AAD and
ciphertext plus the length block, and XORs that result with `AES_K(J0)` for
the tag. Decryption authenticates the ciphertext, AAD, nonce-derived `J0`,
and tag before calling GCTR or writing the caller's plaintext buffer. A bad
tag, corrupted ciphertext, corrupted AAD, corrupted nonce, or wrong key
returns failure and leaves the caller output untouched. Tag comparison uses
the existing bounded accumulated-difference comparison.

All expected malformed inputs return `false`: wrong key/nonce/tag lengths,
capacity violations, short output, overlap, and counter exhaustion. AES round
keys, GHASH state, counters, temporary tags, staging spans, and authentication
intermediates are cleared on operation completion or reset. The implementation
does not log keys, plaintext, ciphertext, nonces, or tags.

GCM nonce uniqueness is the caller's responsibility. Reusing a nonce with the
same key is catastrophic and prohibited. Phase 27 has no persistent nonce
counter or global registry; later TLS code will construct record nonces from
the TLS cipher-suite specification. Phase 26 `ManagedSecureRandom` is only
shown as a caller-side 12-byte nonce source in a liveness test; automatic nonce
management is deliberately not part of this primitive.

## Verification

The dedicated suite is `tools/Run-ManagedKernelPhase27HostTests.ps1` and
passes exactly 100 cases. It covers FIPS-197 AES-128, SP 800-38A AES-128,
SP 800-38D GCM empty/one-block/multi-block/partial/AAD vectors, incremental
GHASH boundaries and reset, exact capacities and max-plus-one rejection,
malformed calls, overlap policy, reset/reuse, GC survival, tag/ciphertext/AAD/
nonce/key corruption, untouched output after authentication failure, recovery
after failure, Phase 26 nonce integration, and 16 KiB performance sanity.
The host suite also cross-checks selected GCM cases against the host
`System.Security.Cryptography.AesGcm` oracle; the production project does not
reference that API. The host sanity measurement was 37 ms for one 16 KiB
encryption and 40 ms for one 16 KiB decryption in the final run.

The independent standards sources are FIPS 197 Appendix C, SP 800-38A
Appendix F.5.1, and SP 800-38D Appendices B/C. The primary references are
[FIPS 197](https://csrc.nist.gov/pubs/fips/197/final),
[SP 800-38A](https://csrc.nist.gov/pubs/sp/800/38/a/final), and
[SP 800-38D](https://csrc.nist.gov/pubs/sp/800/38/d/final).

## NativeAOT proof and runtime boundary

The final payload is 1,314,816 bytes with SHA-256
`124B02BF07966654AC08D578F6BC07EB252EAD8BE28846D0EB1D1153F55C26A2`.
Three fresh QEMU boots of the real NativeAOT payload each passed:

* AES-128 known-answer encryption
* GHASH known-answer computation
* AES-GCM encryption and authenticated decryption
* invalid-tag rejection and no-plaintext-on-failure
* post-failure valid decrypt recovery and reset/reuse
* Phase 26 modern virtio-rng discovery, provider selection, GC survival,
  teardown, reinitialization, and a 12-byte virtio-rng nonce round trip
* `MANAGED_KERNEL_PHASE27_PASS`

The strict Phase 27 runner additionally requires direct AES/GCM GC markers.
Multiple isolated attempts at that checkpoint—array-backed and primitive-only
key schedules, helper and static-root variants—emitted the GC-begin marker and
then the NativeAOT kernel exited before GC completion. The existing Phase 26
secure-random GC path remains green, and host AES expanded-key GC survival is
green. This is a characterized runtime limitation and is the only reason the
phase is Outcome B rather than Outcome A.

Evidence is under
`evidence/managed-kernel-phase27-authoritative-final24/`, including all three
serial logs, hashes, vectors, host results, regression totals, the failed-GC
boundary record, Phase 26 entropy evidence, and the import audit.

The Phase 15–26 host regression total is 691/691: 28, 57, 48, 55, 39, 123,
42, 56, 60, 113, and 70 for Phases 15–23, 25, and 26 respectively. The
Phase 25 no-provider regression remains fail-closed with the expected
`ENTROPY_UNAVAILABLE` behavior in the retained prior evidence.

The PE import audit found the existing `bcrypt.dll!BCryptGenRandom` runtime
PAL import and no new OS crypto dependency. There are no imports or production
references to BCryptEncrypt, BCryptDecrypt, BCryptOpenAlgorithmProvider,
BCryptGenerateSymmetricKey, OpenSSL, libcrypto, CommonCrypto, or hosted AES
PALs. `BCryptGenRandom` remains documented as an existing, unchanged runtime
boundary and is not called by the managed AES/GCM implementation.

## TLS prerequisite matrix

| TLS prerequisite | Status after Phase 27 |
| --- | --- |
| Secure entropy | Proven: Phase 26 virtio-rng provider and lifecycle |
| SHA-256 | Proven |
| HMAC-SHA256 | Proven |
| TLS 1.2 PRF building blocks | Available as standalone primitives |
| AES-128 | Proven functional; direct crypto-GC checkpoint is Outcome B blocked |
| GCM | Proven functional; direct crypto-GC checkpoint is Outcome B blocked |
| ECDH P-256 | Missing |
| RSA/ECDSA verification | Missing |
| X.509 parser | Missing |
| TLS state machine | Deferred |

The next TLS prerequisite is a separately bounded and independently proven
ECDH P-256 plus peer-authentication foundation, followed by TLS record and
handshake work only after the remaining asymmetric and X.509 boundaries are
closed.

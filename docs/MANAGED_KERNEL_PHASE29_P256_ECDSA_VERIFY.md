# Managed Kernel Phase 29 — Managed P-256 ECDSA Verification

Phase 29 adds a narrow, repository-owned ECDSA verification primitive for
NIST P-256/secp256r1 with a SHA-256-sized digest. It accepts a validated SEC1
uncompressed public key and either raw fixed-width `r`/`s` values or the narrow
DER `ECDSA-Sig-Value` form. It does not implement signing, X.509, certificate
chains, RSA, TLS `CertificateVerify`, or a TLS handshake.

## 1. Scope

The fundamental API is digest-oriented:

```text
TryVerifyDigest(digest[32], publicKey[65], r[32], s[32]) -> bool
```

`TryVerifyDerSignature` adds only the bounded DER wrapper needed by future
certificate and handshake code. The lowest-level primitive does not hash
message bytes. The already-proven managed SHA-256 implementation remains a
separate layer.

## 2. ECDSA verification algorithm

For P-256 subgroup order `n`, the implementation performs the required
verification steps:

1. Reject unless `1 <= r < n` and `1 <= s < n`.
2. Convert the 32-byte digest to `e`.
3. Compute `w = s^(n-2) mod n`.
4. Compute `u1 = e*w mod n` and `u2 = r*w mod n`.
5. Compute `R = u1*G + u2*Q` using the Phase 28 Jacobian point machinery.
6. Reject the point at infinity.
7. Accept exactly when `R.x mod n == r`.

The two scalar products are evaluated separately and then added. A Shamir
dual-scalar optimization is intentionally not needed for this phase.

## 3. Field modulus `p` and scalar modulus `n`

The curve field modulus is

```text
p = FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF
```

ECDSA scalar arithmetic uses the distinct subgroup order

```text
n = FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551
```

`ManagedP256FieldElement` remains the Phase 28 field type and all curve
equation/Jacobian arithmetic remains modulo `p`. `ManagedP256ScalarElement`
is a separate internal type and all ECDSA scalar operations are modulo `n`.
The explicit conversion boundaries make accidental modulus mixing difficult.

## 4. Scalar representation

`ManagedP256ScalarElement` is exactly eight little-endian `uint` limbs
(`L0` is the least significant limb), with no arrays, `BigInteger`, arbitrary
precision, or dynamically sized state. Its byte interface is exactly 32-byte
unsigned big-endian. Canonical input accepts values below `n`; the raw
signature path additionally rejects zero.

## 5. Modulo-`n` multiplication and reduction

Multiplication first forms an eight-by-eight product in a fixed 16-`uint`
temporary (512 bits). Reduction is fixed-width binary long division:

* process product bits 511 down through 0;
* shift a nine-limb remainder left by one and insert the current bit;
* conditionally subtract the eight-limb order when the remainder is at least
  `n` (including the ninth-limb carry);
* return the low eight limbs.

The invariant before each step is `remainder < n`; after shifting and adding a
bit the remainder is below `2n`, so one subtraction is sufficient. This is a
bounded 512-step reduction and does not assume the pseudo-Mersenne structure
of `p`. Addition and subtraction use fixed eight/nine-limb carry/borrow paths.

## 6. Modulo-`n` inversion

Because P-256's subgroup order is prime, inversion uses the fixed exponent
`n-2` with a 256-iteration square-and-multiply loop. Zero maps to zero for the
internal total function, while the public signature validation rejects zero
before inversion. No Euclidean loop or input-dependent iteration count is
used.

## 7. Digest conversion

The initial API requires exactly 32 bytes. The digest is interpreted as the
big-endian ECDSA integer (`qlen = 256`, so no right truncation is needed).
Since a SHA-256 integer is below `2^256` and `n` is just below `2^256`, the
implementation performs at most one conditional subtraction of `n`. Zero is
a valid digest integer. Short and long digest spans are rejected.

## 8. Public-key validation reuse

ECDSA calls the existing Phase 28 `ManagedP256.TryReadPublicPoint` path. The
only accepted encoding is `04 || X || Y`, 65 bytes total. It rejects an
incorrect length or prefix, coordinates at or above `p`, off-curve points,
infinity, and malformed input. ECDSA does not maintain a weaker parallel
validator.

## 9. Dual scalar multiplication approach

The implementation computes `u1*G` and `u2*Q` independently with the existing
fixed 256-step Jacobian scalar-multiplication ladder, then uses the existing
Jacobian addition. Scalar values are explicitly converted to the field-sized
representation only at the shared point-ladder boundary; no second EC
implementation was introduced.

## 10. Affine-X modulo-`n` comparison

The affine X coordinate returned by the point arithmetic is a field element in
`[0,p-1]`, not an `n`-reduced scalar. `FromFieldX` copies the eight limbs into
the scalar type and conditionally subtracts `n` once (`p < 2n`). The resulting
scalar is compared with `r`. Host coverage includes an X value of `n+1`, which
must reduce to one, and rejects the non-canonical field value `p`.

## 11. Raw signature representation

The raw API requires caller-owned 32-byte big-endian unsigned `r` and `s`
spans. It rejects wrong lengths, zero, `n`, and values greater than `n`; it
never silently reduces an invalid signature scalar. Normal malformed or
cryptographically invalid input returns `false` without logging key or
signature contents.

## 12. DER ECDSA signature parser

`TryParseDerSignature` parses only:

```text
SEQUENCE { INTEGER r, INTEGER s }
```

The output spans must each be exactly 32 bytes and are cleared before parsing.
The parser is bounded to at most 72 input bytes, emits canonical 32-byte
values, and then applies the same nonzero and `< n` scalar validation as the
raw API. `TryVerifyDerSignature` parses into stack storage and invokes the raw
verification primitive.

## 13. DER canonicality rules

Only the short-form sequence and INTEGER lengths are accepted. The parser
requires the sequence length to consume the complete input and requires two
INTEGERs with no trailing bytes. INTEGERs are positive and nonempty. A single
leading `00` is accepted only when the next byte has its high bit set; an
unnecessary leading zero, multiple leading zeros, negative INTEGER, length
over 33 bytes, overlong/indefinite length, or truncated value is rejected.

## 14. Malformed-input behavior

Wrong tags, lengths, prefixes, coordinates, scalar encodings, DER structure,
or signature values fail closed with `false`. Verification failures do not
alter caller output spans, and a valid verification after prior failures is
covered by both host and NativeAOT proofs. The DER parser performs no reads
outside the supplied span.

## 15. Memory bounds

All production temporaries are fixed-size stack spans or fixed-size structs.
There are no heap-heavy parser objects, arbitrary precision values, dynamic
limbs, or unbounded loops. Maximum DER input is 72 bytes; public keys and raw
scalars have exact 65/32-byte bounds. Temporary point and scalar material is
cleared on completion where it is held in mutable storage.

## 16. Timing and control-flow properties

Scalar multiplication, scalar multiplication-reduction, and inversion have
fixed iteration bounds. Input-shape validation necessarily returns early for
malformed public inputs, scalar encodings, and DER. This phase does not claim a
formal constant-time proof for the complete managed runtime or compiler, and
verification inputs are public certificate/signature data. The implementation
avoids secret-key operations because Phase 29 is verification only.

## 17. Vector sources

The host and NativeAOT proofs use authoritative published vectors from:

* [RFC 6979, section A.2.5](https://www.rfc-editor.org/rfc/rfc6979.txt),
  P-256/SHA-256 `sample` and `test` signatures, including the published public
  key, digest, `r`, and `s` values.
* [RFC 4754, section 8.1](https://www.rfc-editor.org/rfc/rfc4754.txt),
  P-256/SHA-256 verification material for the SHA-256 digest of `abc`.

The host suite also includes deterministic reference arithmetic checks using
`System.Numerics.BigInteger` only in the host-test project. Production managed
kernel code has no such dependency and no hosted ECC oracle.

## 18. Host results

`tools/Run-ManagedKernelPhase29HostTests.ps1` passes **209/209**. Coverage
includes scalar arithmetic and inversion, digest conversion, field-X
conversion, three published valid ECDSA cases, raw and DER verification,
public-key/scalar/signature/digest corruption, infinity and off-curve input,
all required DER malformed classes, recovery after failures, and GC survival.

The retained host regressions also pass: Phase 15–26 **691/691**, Phase 27
**100/100**, and Phase 28 **188/188**.

## 19. NativeAOT results

The Phase 29 gate invokes `GxManagedKernelRunPhase29`. Its proof covers scalar
self-test, strict public-key validation, published raw verification, modified
digest/signature/wrong-key rejection, canonical DER verification, malformed
DER rejection, zero/out-of-range scalar rejection, post-failure recovery, and
the complete Phase 28 ECDH plus Phase 27 AES-GCM regression proof.

The final NativeAOT payload is **1,358,848 bytes** with SHA-256
`AA0AC98CCD31FC525D1DBDF348574633A80DEB476608387F32ED7949CEF3BCEA`.
Three fresh authoritative boots passed under
`evidence/managed-kernel-phase29-authoritative-final5/`:

```text
run-1 serial_sha256 5CBE46FA0CB269B75D2F3AB4865C8D485D15F5FC3AD79799936CD8B1770D9991
run-2 serial_sha256 BC7E02A8CF6570E170E53882C547EEBD430B3B4C1E9DA5DBA6AD671FCF45EFA2
run-3 serial_sha256 4A8E0EB679F33EE9D511FDC8200AFABD7A4B9D9F31091408561EF4CE80AAF534
```

The verified QEMU version is 11.0.0
(`v11.0.0-12122-ga4bb4b10c9`). The copied OVMF code image has SHA-256
`33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`.

## 20. Import audit

Phase 29 production sources introduce no `BCryptVerifySignature`,
`BCryptImportKeyPair`, BCrypt ECC, `BCryptSecretAgreement`, `NCrypt*`,
`Crypt32`, OpenSSL, libcrypto, CommonCrypto, `ECDsa`, `ECDiffieHellman`,
hosted P-256 PAL, or hosted ASN.1/X.509 parser dependency. The pre-existing
`bcrypt.dll!BCryptGenRandom` runtime import may remain for secure entropy and
is not used by ECDSA verification. The final PE report and source audit are
retained under `artifacts/managed-kernel-phase29-final/`, with the Gate4
fixture under `artifacts/gate4-phase29-final/`.

## 21. Inherited GC limitation

The known Phase 27 direct crypto-state `GC.Collect()` NativeAOT boundary
remains documented. It is not a Phase 29 Outcome B criterion. Phase 29 adds
host GC-survival coverage, retains the Phase 26/27/28 tests, and introduces no
new failure at a previously working boundary.

## 22. Planned X.509/TLS reuse

The future X.509 layer can reuse this digest-oriented raw/DER verification
substrate and the strict SEC1 public-key validation path. Certificate parsing,
certificate-chain validation, trust anchors, hostname policy, TLS
`CertificateVerify`, RSA verification, and the TLS state machine remain out of
scope. ECDSA verification alone is not certificate authentication.

# Managed Kernel Phase 28 — P-256 ECDH

## 1. Scope

Phase 28 adds a narrow, repository-owned NIST P-256 / secp256r1 / prime256v1
ECDH primitive for the managed kernel. It does not add TLS records, a TLS
handshake, certificates, X.509 parsing, ECDSA, RSA, cipher-suite negotiation,
or HTTP behavior. The production path is:

```text
ManagedSecureRandom -> private scalar -> public point -> peer validation
                    -> scalar multiplication -> 32-byte shared X coordinate
```

The implementation is in `src/ManagedKernel/ManagedP256.cs`; the native proof
entry is `ManagedP256KernelProof.cs`. Host coverage is in
`src/ManagedKernelPhase28HostTests` and is run by
`tools/Run-ManagedKernelPhase28HostTests.ps1`.

## 2. P-256 parameters

The implementation is fixed to the P-256 parameters:

```text
p = FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF
a = FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFC
b = 5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B
Gx = 6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296
Gy = 4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5
n = FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551
h = 1
```

The curve equation is `y^2 = x^3 - 3x + b (mod p)`. The parameters and the
RFC 5903 test pair are independently listed in [RFC 5903, sections 3.1 and
8.1](https://www.rfc-editor.org/rfc/rfc5903.html).

## 3. Internal integer representation

Each field element is an inline `readonly struct` containing eight `uint`
limbs in little-endian order. There are no limb arrays, `BigInteger` values,
or arbitrary-precision heap objects in the production implementation. The
encoding boundary is exactly 32 bytes, unsigned, big-endian. Scalars use the
same bounded representation and are checked against `n`, not silently
reduced modulo `n`.

## 4. Field reduction strategy

Field elements entering the arithmetic are canonical. The special P-256 prime
is represented as:

```text
p = 2^256 - 2^224 + 2^192 + 2^96 - 1
```

so the reduction relation used for high limbs is:

```text
2^256 = 1 + 2^224 - 2^192 - 2^96 (mod p)
```

Multiplication is bounded schoolbook base-2^32 multiplication into sixteen
`ulong` cells. High cells are folded into signed low-limb storage using the
relation above, followed by a fixed sixteen-round carry normalization and four
fixed conditional subtractions of `p`. Squaring reuses multiplication. The
host suite covers zero, one, `p - 1`, rejected `p` and `p + 1` encodings,
maximum products, products against curve constants, and independent modular
oracles.

Addition handles the possible 257th carry with the same fixed prime relation
using raw bounded operations; it does not recurse based on the input value.
Subtraction adds `p` on a borrow, and negation maps zero to zero and otherwise
computes `p - x`.

## 5. Inversion strategy

Nonzero inversion uses fixed 256-bit square-and-multiply exponentiation by
`p - 2`, as permitted by Fermat's little theorem. Zero maps to zero and is
never accepted as a projective `Z` value for affine conversion. The host suite
checks inverse values and inverse products for one, `p - 1`, generator
coordinates, and zero handling.

## 6. Point representation

Internal points use Jacobian coordinates `(X:Y:Z)` with affine coordinates
`x = X/Z^2` and `y = Y/Z^3`. The point at infinity is represented by `Z = 0`.
The implementation includes only the P-256 operations needed here: point
doubling, point addition, fixed scalar multiplication, affine conversion, and
clearing. Affine import creates `Z = 1`; export performs one inversion rather
than inverting at every point operation.

## 7. Scalar-multiplication algorithm

Private scalar multiplication uses a Montgomery ladder with exactly 256 bit
iterations. Each iteration uses mask-based selection for the ladder swap and
performs one add and one double; the loop does not terminate at the scalar's
highest set bit. Exceptional point cases (infinity, equal points, inverse
points, and `Y = 0`) remain ordinary managed branches in the point formulas.
Therefore this implementation is described as fixed-iteration and designed
to avoid obvious secret-dependent control flow; it makes no formal constant-
time claim. NativeAOT-generated code has not received a formal side-channel
audit.

## 8. Public-key validation

`TryValidatePublicKey` accepts only a finite SEC1 uncompressed point. It
requires all of the following:

* exactly 65 bytes;
* prefix byte `04`;
* both 32-byte coordinates strictly less than `p`;
* the point is not the infinity representation; and
* `y^2 = x^3 - 3x + b (mod p)`.

Because this curve's cofactor is one, a validated finite on-curve point is in
the intended P-256 subgroup; no separate cofactor clearing operation is
needed for this narrow ECDH primitive. Invalid points are rejected before
scalar multiplication.

## 9. SEC1 encoding

The public-key API emits and imports exactly:

```text
04 || X[32] || Y[32]
```

Coordinates are unsigned big-endian and retain leading zero bytes. Compressed
points, hybrid points, DER, ASN.1, and general EC key formats are out of scope.

## 10. Key generation and rejection sampling

`TryGeneratePrivateKey` requires an existing `ManagedSecureRandom` and a
caller-owned 32-byte destination. It requests a fresh 32-byte candidate from
the provider and accepts only `1 <= d < n`. It makes at most 128 fixed-bounded
attempts, clears each temporary candidate, copies only an accepted candidate,
and fails closed if the provider is unavailable or all bounded attempts fail.
There is no modulo reduction, timer fallback, deterministic fallback, fixed
production key, or alternate hosted random implementation. The NativeAOT
proof records the provider as `VIRTIO_RNG` and does not print the generated
scalar.

## 11. ECDH output format

`TryDeriveSharedSecret` validates the local scalar and peer SEC1 point,
computes `d * Q`, rejects infinity, and writes exactly the affine X coordinate
as 32 unsigned big-endian bytes. It does not hash or KDF the result. On every
failure, the caller's output buffer is left unchanged. Public-key derivation
also computes into a temporary fixed buffer before publishing success, and
the host suite verifies supported overlapping input/output cases.

## 12. Memory and buffer bounds

Production operations use caller-owned spans plus fixed stack-local values and
fixed-size temporary spans. The public API sizes are 32-byte private scalars,
65-byte public keys, and 32-byte shared secrets. There is no dynamic limb
storage, no attacker-controlled loop bound, and no general-purpose bigint API.

## 13. Secret-data handling

Private scalars, candidate entropy, temporary public encodings, Jacobian
results, and shared-secret temporaries are cleared on the managed success and
failure paths where practical. Production proof logs emit only PASS/provider
markers and never private scalars or shared secrets. Managed-memory clearing is
best-effort: copies can exist in registers, stack slots, compiler temporaries,
or runtime state, and the existing NativeAOT runtime does not provide a hard
guarantee that every historical copy is erased. This phase makes no stronger
zeroization claim.

## 14. Timing and control-flow limitations

The scalar loop and inversion exponent have fixed iteration counts. The field
and point code still contains comparisons, canonicalization branches, and
exceptional-point branches, and managed JIT/AOT optimization and memory
behavior have not been audited as a constant-time implementation. The code is
intended to remove obvious scalar-bit loop leakage, not to claim formal
constant-time behavior.

## 15. Authoritative test-vector sources

The imported vectors are:

* RFC 5903 section 8.1, the P-256 IKE Group 19 initiator/responder private
  keys, public points, and common X coordinate:
  [RFC 5903](https://www.rfc-editor.org/rfc/rfc5903.html).
* NIST CAVP ECC CDH Primitive Test Vectors, file
  `KAS_ECC_CDH_PrimitiveTest.txt`, `[P-256]` records 0 through 3. The source
  archive is the official
  [ECCCDH primitive vector archive](https://csrc.nist.gov/CSRC/media/Projects/Cryptographic-Algorithm-Validation-Program/documents/components/ecccdhtestvectors.zip).
  The `QCAVSx/QCAVSy`, `dIUT`, `QIUTx/QIUTy`, and `ZIUT` fields are imported
  without modification.
* RFC 9500's published P-256 test key is used as an additional independent
  public-key derivation and validated peer fixture:
  [RFC 9500](https://www.rfc-editor.org/rfc/rfc9500.html).

The host oracle uses `System.Numerics.BigInteger` only inside the test project;
that assembly is not referenced by the managed-kernel production project.

## 16. Host results

The dedicated Phase 28 host runner reports:

```text
MANAGED_KERNEL_PHASE28_HOST_TESTS_PASS cases=188
```

The suite separately covers limb encoding/comparison, modular add/subtract/
negate/multiply/square/reduction/inversion, curve equation, point infinity,
doubling/addition/scalar multiplication, SEC1 import/export, RFC/NIST vectors,
symmetry, all requested malformed scalar and point classes, unchanged output
on failure, supported overlap, entropy integration, teardown/reuse, and GC
survival. Phase 26 and Phase 27 host suites remain separate regressions.

## 17. NativeAOT results

The authoritative runner is
`tools/Run-ManagedKernelPhase28FreshBoots.ps1`. It combines the established
Phase 26 virtio-rng lifecycle path, the Phase 28 proof, the Phase 27 AES/GHASH/
GCM core proof, and the earlier Phase 15/23 network harness markers. It
requires three fresh QEMU processes, a fresh evidence directory, the exact
payload size/hash, one final Phase 28 marker per boot, and no loader fault
markers. The final authoritative run used QEMU 11.0.0
(`v11.0.0-12122-ga4bb4b10c9`) and the existing single-threaded TCG/q35
runner profile. The NativeAOT payload is 1,341,952 bytes with SHA-256`DC431B422D1D8B53690A30882F24CA215A85A5FAC7D558C54AEA0984BA248211`.
All three fresh boots passed. Their serial-log SHA-256 values are:

```text
run-1  6B415DEF614F0E23E0DB79F853D21C47A1FB7F6ABD4E9490886F8C04F5456FC7
run-2  C91CF876C0142B7DB97401F12FA35629E089A5CB42A066165E77809FB68E8F68
run-3  90862B88351E8A4F0E41ADBE2652BD4AF333ACA311826C14C3C3544106B75A7C
```

The OVMF code identity is SHA-256
`33090CC07675BA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`; the
per-run variable-store copy is SHA-256
`5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.
Exact logs and runner metadata are retained under
`evidence/managed-kernel-phase28-authoritative-final7/`.

The Phase 28 gate uses a dedicated standalone compile flag that suppresses
only the inherited ACPI PM-timer half-wrap ambiguity check which caused the
pre-proof `MANAGED_KERNEL_START_BLOCKED=0x6` false negative. The monotonic
service and earlier validation paths remain enabled; this is a harness timing
fixture, not a relaxation of P-256 validation or arithmetic.

The Phase 28 gate uses a dedicated standalone compile flag that suppresses
only the inherited ACPI PM-timer half-wrap ambiguity check which caused the
pre-proof `MANAGED_KERNEL_START_BLOCKED=0x6` false negative. The monotonic
service and earlier validation paths remain enabled; this is a harness timing
fixture, not a relaxation of P-256 validation or arithmetic.

## 18. Import audit

The final NativeAOT PE is inspected with `objdump -p` and compared with the
retained Phase 27 import surface. Phase 28 production source contains no
`BigInteger`, `ECDiffieHellman`, `ECDsa`, BCrypt ECC/secret-agreement API,
NCrypt, OpenSSL, libcrypto, CommonCrypto, or hosted P-256 PAL dependency. The
pre-existing `bcrypt.dll!BCryptGenRandom` runtime/PAL import may remain for
the established runtime surface; it is not used by this P-256 implementation.
The final PE retains only that pre-existing BCrypt random import among the
crypto-related imports. The exact audit output is retained in
`artifacts/managed-kernel-phase28/managed-kernel-pe-report.txt`.

## 19. Inherited Phase 27 NativeAOT GC limitation

Phase 27 characterized a direct `GC.Collect()` checkpoint that exits when AES
crypto state remains live, while the established Phase 26 secure-random GC
proof and Phase 27 host expanded-key GC test pass. Phase 28 does not make that
known direct crypto-state checkpoint a mandatory acceptance criterion and does
not invoke it as part of the combined proof. A new failure at an existing
working Phase 26 collection boundary would be a Phase 28-specific blocker; the
inherited Phase 27 boundary alone is not.

## 20. Future ECDSA/X.509/TLS reuse

Future signature verification can reuse the fixed-width field element
encoding, P-256 reduction, inversion, curve equation, Jacobian point formulas,
affine conversion, and fixed scalar-multiplication structure internally. It
must add separately reviewed scalar arithmetic, signature parsing, public-key
policy, hash integration, and verification-specific side-channel analysis.
This phase intentionally exposes no general curve framework and does not
claim RSA, ECDSA, X.509, or TLS capability merely because P-256 ECDH exists.



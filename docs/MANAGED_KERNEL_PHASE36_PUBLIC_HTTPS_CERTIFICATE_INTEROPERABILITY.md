# guideXOS Managed Kernel Phase 36 — Public HTTPS Certificate Interoperability

Status: Outcome B

Repository: `D:\dev\guideXOS_NET10_nativeaot-managed-kernel-integration`

Branch: `nativeaot-managed-kernel-integration`

Phase 36 advances the managed X.509 path through the exact public certificate
chain observed in Phase 35.  The public request now reaches certificate
validation, hostname validation, TLS Finished, and encrypted HTTP request
transmission on all three fresh boots.  The same public request then reaches
HTTP status 200 but is rejected by the existing bounded 4 KiB body limit, so
the complete HTTP/body proof is not yet Outcome A.

## 1. Phase 35 failure boundary

Phase 35 used three fresh public QEMU boots against
`https://www.cloudflare.com/llms.txt`.  DHCP, ARP, DNS, TCP, TLS 1.2,
`TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256` (`0xC02B`), EMS, and the complete
certificate handshake succeeded.  The managed validator stopped at peer
certificate index `0x1` with `UnsupportedAlgorithm`.

The exact immediate cause was the certificate's
`signatureAlgorithm`/TBSCertificate signature OID
`1.2.840.10045.4.3.3` (`ecdsa-with-SHA384`).  The Phase 35 parser recognized
only ECDSA with SHA-256.  The chain was not rejected because of the RSA
signature on the cross-signed root: that root signature was not reached by the
old validator.

The Phase 35 embedded anchor text also contained a latent DER fixture error:
the issuer name encoded `GlobalSignn Root CA` and one shifted validity byte.
Phase 36 corrected it to the captured 894-byte GTS Root R4 cross-signed DER,
SHA-256 `76B27B80A58027DC3CF1DA68DAC17010ED93997D0B603E2FADBE85012493B5A7`.

## 2. Exact DER audit

The audit was performed by `tools\Inspect-Phase36PublicCertificateChain.ps1`.
It sends a bounded managed diagnostic ClientHello and captures only the
server's plaintext TLS Certificate message.  Host certificate APIs are used
only by the audit decoder; they are not used by the guest implementation.

Evidence: `artifacts\phase36-certificate-audit\public-certificate-chain.log`

The capture contained exactly three certificates.  The extension values below
are the relevant decoded DER values; the complete raw audit output contains
the extension OIDs and bounded raw hex values.

### Certificate 0 — leaf

- DER length: 919 bytes
- DER SHA-256: `67947E58370D4BC680D1CB1880F4F17FDD13674A44C51101B552B7B27A55063D`
- Subject: `CN=www.cloudflare.com`
- Issuer: `CN=WE1, O=Google Trust Services, C=US`
- Serial: `00C19A2F3791E0A049139013EFF97D1843`
- SignatureAlgorithm OID: `1.2.840.10045.4.3.2` (`ecdsa-with-SHA256`)
- TBSCertificate signature OID: the same `ecdsa-with-SHA256` OID
- SubjectPublicKeyInfo algorithm OID: `1.2.840.10045.2.1` (`id-ecPublicKey`)
- EC curve OID: `1.2.840.10045.3.1.7` (P-256)
- Public point: uncompressed, 65 bytes
- BasicConstraints: critical, `CA=FALSE`
- KeyUsage: critical, digitalSignature (`03020780`)
- ExtendedKeyUsage: serverAuth
- SubjectAltName: `www.cloudflare.com`, `*.www.cloudflare.com`
- Validity: `2026-08-28T00:16:10Z` through `2026-11-26T01:16:07Z`
- SKI: `6003676F2F9D2465E260D7462C8388D6B0CF3583`
- AKI: `9077923567C4FFA8CCA9E67BD980797BCC93F938`
- Role: leaf

### Certificate 1 — Google Trust Services WE1 intermediate

- DER length: 675 bytes
- DER SHA-256: `1DFC1605FBAD358D8BC844F76D15203FAC9CA5C1A79FD4857FFAF2864FBEBF96`
- Subject: `CN=WE1, O=Google Trust Services, C=US`
- Issuer: `CN=GTS Root R4, O=Google Trust Services LLC, C=US`
- Serial: `7FF31977972C224A76155D13B6D685E3`
- SignatureAlgorithm OID: `1.2.840.10045.4.3.3` (`ecdsa-with-SHA384`)
- TBSCertificate signature OID: the same `ecdsa-with-SHA384` OID
- SubjectPublicKeyInfo algorithm OID: `1.2.840.10045.2.1` (`id-ecPublicKey`)
- EC curve OID: `1.2.840.10045.3.1.7` (P-256)
- Public point: uncompressed, 65 bytes
- BasicConstraints: critical, `CA=TRUE`, pathLenConstraint `0`
- KeyUsage: critical, digitalSignature, keyCertSign, cRLSign (`03020186`)
- ExtendedKeyUsage: serverAuth and clientAuth
- SubjectAltName: absent
- Validity: `2023-12-13T09:00:00Z` through `2029-02-20T14:00:00Z`
- SKI: `9077923567C4FFA8CCA9E67BD980797BCC93F938`
- AKI: `804CD6EB74FF4936A3D5D8FCB53EC56AF0941D8C`
- Role: intermediate

### Certificate 2 — cross-signed GTS Root R4

- DER length: 894 bytes
- DER SHA-256: `76B27B80A58027DC3CF1DA68DAC17010ED93997D0B603E2FADBE85012493B5A7`
- Subject: `CN=GTS Root R4, O=Google Trust Services LLC, C=US`
- Issuer: `CN=GlobalSign Root CA, OU=Root CA, O=GlobalSign nv-sa, C=BE`
- Serial: `7FE530BF331343BEDD821610493D8A1B`
- SignatureAlgorithm OID: `1.2.840.113549.1.1.11` (`sha256WithRSAEncryption`)
- TBSCertificate signature OID: the same RSA/SHA-256 OID
- RSA signature parameters: DER `NULL` parameters
- SubjectPublicKeyInfo algorithm OID: `1.2.840.10045.2.1` (`id-ecPublicKey`)
- EC curve OID: `1.3.132.0.34` (P-384)
- Public point: uncompressed, 97 bytes
- BasicConstraints: critical, `CA=TRUE`, no path-length constraint
- KeyUsage: critical, digitalSignature, keyCertSign, cRLSign (`03020186`)
- ExtendedKeyUsage: serverAuth and clientAuth
- SubjectAltName: absent
- Validity: `2023-11-15T03:43:21Z` through `2028-01-28T00:00:42Z`
- SKI: `804CD6EB74FF4936A3D5D8FCB53EC56AF0941D8C`
- AKI: `607B661A450D97CA89502F7D04CD34A8FFFCFD4B`
- Role: cross-signed representation of the configured trust anchor; not a
  self-signed root and not independently trusted merely because it was sent

No self-signed GTS Root R4 was sent by the server.  The host audit's optional
system-built chain contained a GlobalSign root, but that host-built object is
not a peer certificate and is not used by the guest.

## 3. Required algorithm decision

RSA was observed only as the signature algorithm on certificate 2.  The
candidate root is terminated against the configured GTS Root R4 anchor, so
verifying the cross-signer's RSA signature is not required for the secure
observed path.  No RSA public operation, PKCS#1 v1.5 verification, RSA SPKI
acceptance, signing, encryption, private-key operation, or key generation was
implemented.

SHA-384 was required for the certificate 1 ECDSA signature.  A bounded managed
SHA-384 implementation was added, with incremental updates and a SHA-512
compression core exposed only as SHA-384.

P-384 was required because certificate 2's SPKI is an ECDSA P-384 key used to
verify the certificate 1 ECDSA/SHA-384 signature.  A fixed-width bounded P-384
field/scalar implementation was added for ECDSA verification.  TLS remains
the existing P-256 ECDHE profile; P-384 was not added to TLS negotiation.

Newly accepted certificate forms are therefore:

- ECDSA/SHA-256 with a P-256 issuer key;
- ECDSA/SHA-384 with a P-256 or P-384 issuer key;
- EC P-256 and P-384 named-curve SPKIs;
- RSA/SHA-256 AlgorithmIdentifier with explicit DER NULL parameters, only as a
  parsed-but-unverifiable root signature form when the certificate is the
  configured anchor identity.

Deliberately unsupported forms remain rejected: RSA SPKIs, RSA public
operations, RSA PKCS#1 signatures, RSA/SHA-384, ECDSA with other hashes or
curves, unknown signature/public-key OIDs, malformed parameters, and all
unrecognized critical extensions.

## 4. Trust-anchor semantics

Before Phase 36, the validator required the candidate root certificate to be
the exact configured trust-anchor DER, including its issuer, serial, signature,
and every encoding byte.  That is not a valid cross-sign interoperability rule.

After Phase 36, a candidate CA reaches the configured anchor only when all of
the following match:

1. the complete DER Subject Name encoding;
2. the recognized public-key algorithm; and
3. the complete subject public-key byte string (the uncompressed EC point for
   the observed anchors).

The candidate and configured anchor are both strictly parsed.  The candidate
still passes validity, CA, KeyUsage, critical-extension, issuer/ordering, and
all non-anchor chain checks.  The candidate's own external cross-signature is
not used after its authenticated subject/key identity matches the configured
anchor.  A certificate that shares only the Subject DN, or shares the subject
but has a different public key, remains untrusted.  The latter is explicitly
reported as a trust-anchor key mismatch.

## 5. ASN.1, hashing, and cryptographic bounds

- DER certificate: maximum 16 KiB per certificate.
- Certificate handshake: maximum 49,152 bytes.
- Certificate workspace: 65,536 bytes, four peer certificate slots, and at
  most three non-anchor chain certificates.
- DER lengths are definite, bounded, and checked before slicing; indefinite
  lengths, overflow, out-of-buffer reads, malformed integers, unknown OIDs,
  malformed AlgorithmIdentifier parameters, and excessive nesting fail closed.
- SHA-384 uses a 128-byte block, 48-byte digest, incremental state, and a
  128-bit encoded length.  The implementation rejects lengths outside its
  bounded internal domain before encoding the final length.
- P-384 uses fixed 12-limb 384-bit field/scalar values and no arbitrary-
  precision or unbounded allocation.
- TLS records remain capped at 16 KiB; delivery fragments remain capped at
  512 bytes.
- HTTP's existing accepted-body bound remains 4,096 bytes in Phase 36.  The
  live target exceeded that bound after returning HTTP 200; this is the
  documented next blocker rather than an HTTP validation bypass.

## 6. Negative-test matrix

The Phase 36 host suite has 72 cases.  It includes deterministic SHA-384
vectors for empty input, `abc`, multi-block input, 111/112/127/128/129-byte
padding boundaries, and fragmented updates; observed leaf/intermediate/root
parsing; P-256 ECDSA/SHA-256; P-384 ECDSA/SHA-384; full-chain validation; GC
survival; and these fail-closed controls:

| Vector | Expected result |
| --- | --- |
| unsupported signature OID | `UnsupportedAlgorithm` |
| signatureAlgorithm/TBSCertificate mismatch | `CertificateAlgorithmMismatch` |
| malformed P-384 public point | `InvalidPublicKey` |
| bad SHA-384 signature | `BadSignature` |
| malformed ECDSA signature | `BadSignature` |
| wrong issuer key | `BadSignature` |
| wrong SHA-384 TBS digest | `BadSignature` |
| fake root with trusted subject but different key | `UntrustedRoot` / key mismatch |
| missing intermediate keyCertSign | `InvalidKeyUsage` |
| non-CA intermediate | `InvalidCa` |
| expired chain | `Expired` |
| not-yet-valid chain | `NotYetValid` |
| hostname mismatch | `HostnameMismatch` |
| changed cross-signed-anchor signature with same subject/key | accepted as anchor identity; external signature is not consulted |

RSA malformed-signature, modulus-size, exponent, and DigestInfo vectors are
not applicable to the required observed path because RSA verification was
deliberately not implemented.  RSA SPKI input is explicitly rejected rather
than accepted without verification.

## 7. Regression results

Final focused host results:

- Phase 22: `56/56`
- Phase 23: `60/60`
- Phase 30: `91/91`
- Phase 31: `33/33`
- Phase 32: `69/69`
- Phase 33: `185/185`
- Phase 34: `140/140`
- Phase 35: `6/6`
- Phase 36: `72/72`
- Focused aggregate: `712/712`

Host evidence is under `artifacts\phase36-regressions-final` and
`artifacts\phase36-host-tests-final`.

The deterministic Phase 34 QEMU controls used a Phase 34-specific gate built
from the final payload:

- Positive redirect/body/teardown control: `3/3` in
  `artifacts\phase36-deterministic\positive2`.
- Hostname-mismatch negative control: `3/3` in
  `artifacts\phase36-deterministic\negative`.

## 8. Public QEMU result

Target: `https://www.cloudflare.com/llms.txt`

Architecture: managed application -> managed HTTPS/TLS -> managed TCP ->
managed IPv4 -> managed ARP/Ethernet -> managed E1000E -> QEMU user networking
(`-netdev user,id=net0`, `-device e1000e,netdev=net0,addr=2`) -> public Internet.
No host HTTP/TLS/DNS/socket/proxy was used for the guest request.

Final public evidence:
`artifacts\phase36-public-final5\evidence\runs\run-1\serial.log`,
`run-2\serial.log`, and `run-3\serial.log`.

All three fresh boots recorded:

- DHCP address `10.0.2.15`;
- subnet `255.255.255.0`;
- gateway `10.0.2.2`;
- DNS `10.0.2.3`;
- direct same-subnet ARP to DNS and gateway next-hop ARP for the public route;
- managed TCP establishment;
- TLS ServerHello `0x0303`;
- cipher suite `0xC02B`;
- three peer certificates;
- certificate algorithm mask `0x07` (leaf ECDSA/SHA-256/P-256,
  intermediate ECDSA/SHA-384/P-256, cross-signed root RSA/SHA-256/P-384 SPKI);
- trust-anchor decision `SUBJECT_AND_SPKI_KEY`;
- certificate validation success;
- hostname validation success;
- ServerKeyExchange verification success;
- TLS Finished verification success;
- encrypted HTTP GET transmission.

Resolved public IPs were:

- run 1: `104.16.124.96`;
- run 2: `104.16.123.96`;
- run 3: `104.16.123.96`.

The response parser observed HTTP status `200` on all three boots and then
failed closed with `BodyTooLarge` (`0x0E`).  The parser's content-length field
was zero because the response was chunked; the live response exceeded the
configured 4,096-byte accepted-body limit.  Consequently:

- TLS Finished: passed `3/3`;
- HTTP request sent: `3/3`;
- HTTP status observed: `200`, `3/3`;
- complete HTTP response parsing: not completed;
- body-byte verification: not reached;
- public full Outcome A: `0/3`;
- public certificate-compatibility proof: `3/3`;
- final classification: Outcome B.

The precise next blocker is logged as
`PUBLIC_HTTPS_NEXT_BLOCKER=HTTP_BODY_LIMIT_EXCEEDED`.  No certificate,
hostname, time, trust, TLS, or body-validation bypass was added.

## 9. Memory, GC, teardown, and reuse

The 16 KiB TLS record, 49,152-byte certificate-message, 65,536-byte
certificate-workspace, 512-byte delivery-fragment, and 4,096-byte HTTP-body
limits remain explicit and bounded.  SHA-384 and P-384 use fixed managed
state; no unbounded certificate, recursion, or arbitrary-precision subsystem
was added.

The host Phase 36 suite passed GC-survival checks for parsed certificate
structures, P-384 key material, and hostname use.  The existing guest forced-
GC experiment remains separate from the host coverage; the public consumer
also emitted `PUBLIC_GC_SURVIVAL_PASSED` after TLS authentication.

The public certificate failure/HTTP body-limit failure reached the managed
failure path and teardown.  The deterministic Phase 34 positive and negative
controls both retained their existing teardown/reuse proofs.  No test-owned
QEMU processes remain after the runs.

## 10. Build identity and evidence

- Starting HEAD: `9120b12`
- Ending HEAD: `9120b12` (no commit was made)
- Final worktree changes: uncommitted Phase 35 baseline plus Phase 36
  implementation, tests, diagnostics, and this document
- Final public payload size: `1,793,536` bytes
- Final public payload SHA-256:
  `8622C909428A4275F34F4EF52B5865E67702940A5F6CD293C11737BFECB0323B`
- OVMF code SHA-256:
  `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`
- Final public gate BOOTX64.EFI SHA-256:
  `6CACB2AAD8798DE219C1BCEBF1DD7AE36E0CD2F45082D7399A805F170B6AB585`
- DER audit evidence:
  `artifacts\phase36-certificate-audit\public-certificate-chain.log`
- Final public evidence:
  `artifacts\phase36-public-final5\evidence\runs`
- Deterministic evidence:
  `artifacts\phase36-deterministic\positive2` and
  `artifacts\phase36-deterministic\negative`
- Final host regression evidence:
  `artifacts\phase36-regressions-final`

## 11. Outcome and Phase 37 direction

Outcome B: Phase 36 correctly implements the exact certificate
interoperability required by the captured public chain and proves the managed
chain through hostname validation and TLS Finished.  The next standards-
compliant blocker is the existing 4 KiB HTTP body bound, not certificate
validation.

The narrowest Phase 37 work is to choose and test a bounded HTTP response
policy for this target: either raise the accepted body limit within the
existing 16 KiB parser ceiling, or add a bounded streaming/discard policy
that verifies framing and a deterministic prefix without retaining the full
body.  Keep TLS and certificate validation unchanged, preserve the existing
negative controls, and do not bypass HTTP framing or body bounds.

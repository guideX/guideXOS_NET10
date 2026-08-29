# Managed Kernel Phase 30 — Narrow X.509 / DER Certificate Validation

## 1. Outcome

Phase 30 delivers Outcome A: a bounded, self-contained managed parser and validator for one deliberately narrow DER/X.509 profile. It is integrated into the NativeAOT Gate4 path, covered by 91 host cases, and reaches `MANAGED_KERNEL_PHASE30_PASS` in three fresh QEMU boots. It is not a general-purpose X.509 or TLS implementation.

## 2. Repository baseline

Work started on branch `nativeaot-managed-kernel-integration` at `9798fd296b155a9d96368a0260aa83f12ddb0e7b`. The worktree was clean and matched its upstream tracking branch. Phase 29 was already committed at that baseline and was preserved. Phase 30 changes are intentionally uncommitted and were not pushed.

## 3. Supported certificate profile

The accepted certificate is DER `Certificate` with `ecdsa-with-SHA256` for both the outer and TBS signature algorithms, `id-ecPublicKey` on `prime256v1`/NIST P-256, and the Phase 29 canonical DER ECDSA `SEQUENCE { INTEGER r, INTEGER s }` signature form. Version 3 extensions are supported only through the bounded profile below.

## 4. DER reader bounds

`ManagedDerReader` is a `ref struct` over the caller-owned byte buffer. It accepts only definite-length DER with one-byte tags and at most two long-form length octets. It rejects indefinite length, high-tag-number form, non-minimal lengths, zero-leading long lengths, parent overrun, truncation, and trailing bytes. Nesting is bounded at 12 levels, each reader at 256 elements, and certificate input at 16 KiB.

The supported ASN.1 tags are SEQUENCE, SET, INTEGER, OBJECT IDENTIFIER,
BOOLEAN, OCTET STRING, BIT STRING, UTF8String, PrintableString, IA5String,
UTCTime, GeneralizedTime, and the explicit/implicit context-specific wrappers
needed for version, unique IDs, and extensions. Other tags are rejected where
the profile requires a supported semantic type.

## 5. Compact parse result

`ManagedX509Certificate` is a compact immutable result containing offsets and lengths into the original buffer rather than copied strings or object graphs. It records exact TBS bytes, serial, issuer, subject, validity, SPKI, signature, SAN/CN, basic constraints, key usage, EKU, and unknown-critical-extension state. The caller retains the source buffer.

## 6. Exact TBS preservation

The parser records the complete encoded TBS `SEQUENCE`, including its tag and length octets. Signature verification hashes exactly those bytes with managed Phase 26 SHA-256 and passes the digest to the managed Phase 29 ECDSA verifier. No re-encoding or host certificate object is used.

## 7. Certificate signature validation

`TryValidateCertificateSignature` requires complete input consumption, checks all recorded ranges, hashes the exact TBS range, and verifies the outer ECDSA-SHA256 signature with the certificate P-256 public key. Corrupt TBS bytes, corrupt signatures, unsupported algorithms, and malformed signature encodings are rejected.

## 8. Supported OIDs

The narrow OID set is ECDSA-SHA256 (`1.2.840.10045.4.3.2`), EC public key (`1.2.840.10045.2.1`), prime256v1 (`1.2.840.10045.3.1.7`), commonName (`2.5.4.3`), subjectAltName (`2.5.29.17`), basicConstraints (`2.5.29.19`), keyUsage (`2.5.29.15`), extendedKeyUsage (`2.5.29.37`), and serverAuth (`1.3.6.1.5.5.7.3.1`). Unknown non-critical extensions are ignored after safe outer DER parsing; unknown critical extensions fail.

## 9. SPKI and P-256 extraction

SPKI must contain exact EC public-key and prime256v1 algorithm identifiers, an uncompressed point BIT STRING with zero unused bits, and exactly 65 point bytes (`04 || X || Y`). Existing managed P-256 validation rejects off-curve and otherwise invalid points. RSA, other curves, compressed points, and malformed BIT STRING wrappers are unsupported.

## 10. Validity time

UTCTime is accepted with required `Z` form and maps years 50–99 to 1950–1999 and 00–49 to 2000–2049. GeneralizedTime requires four-digit years and the same UTC form. Seconds are required; offsets, fractional seconds, invalid dates, and impossible calendar values fail. The validator receives a caller/kernel-supplied UTC timestamp, reports `TimeUnavailable` when it is unavailable, and distinguishes `NotYetValid` from `Expired`.

## 11. Name parsing

Issuer and subject are parsed as bounded sequences of RDN sets and attribute type/value pairs. Values are restricted to UTF8String, PrintableString, and IA5String. Exact encoded issuer and subject ranges are retained for byte-for-byte matching. Duplicate commonName attributes are rejected.

## 12. Basic constraints

`basicConstraints` is supported only in its bounded DER sequence form. CA certificates require `cA = TRUE`; an optional nonnegative pathLenConstraint is recorded. A leaf must not assert CA, and an issuer must be a CA. The trust anchor is an explicitly configured anchor; its self-signature is not required by the chain routine.

## 13. Key usage

The optional keyUsage BIT STRING is parsed with DER unused-bit checks. Issuers must have `keyCertSign`; an end-entity certificate must have `digitalSignature` when keyUsage is present. Unsupported malformed bit strings fail rather than being treated as absent.

## 14. Extended key usage

The optional EKU sequence is bounded and recognized for server authentication through the exact serverAuth OID. A leaf with EKU must contain serverAuth; missing EKU remains permitted for this narrow profile. Unknown EKU values do not satisfy server authentication.

## 15. Subject alternative names

The supported SAN form is `dNSName` IA5 content. DNS names are limited to 253 ASCII bytes and 32 entries. Critical SAN entries of unsupported form fail; non-critical unsupported GeneralName entries are skipped after safe parsing. Malformed SAN structure, invalid DNS bytes, and overlong names fail.

## 16. Hostname matching

SAN DNS names take precedence over CN. If any DNS SAN exists, CN is never used; if SAN has no DNS name, a single CN may be used as fallback. Matching is ASCII case-insensitive. The only wildcard permitted is a complete leftmost label such as `*.example.com`, matching exactly one label. Partial, embedded, multi-label, and non-ASCII/IDNA wildcards are rejected.

## 17. Chain shape

The bounded chain validator accepts a leaf, zero to two optional intermediates, and one candidate root: at most four certificates total. It requires candidate-root bytes to equal the configured trusted-root bytes exactly. It matches each child issuer to issuer subject by exact DER bytes and verifies each child signature with the issuer public key.

## 18. Chain constraints

The leaf must be time-valid, satisfy serverAuth and hostname policy, and be an end entity. Each intermediate must be time-valid, CA-enabled, and keyCertSign-enabled. Path length is enforced for subordinate CA depth. The root profile and validity are parsed, but the trust decision is the exact trusted-root byte match; no ambient OS trust store is consulted.

## 19. Critical extension policy

Known profile extensions are parsed and validated. Unknown critical extensions produce `UnknownCriticalExtension`. Unknown non-critical extensions are ignored only after bounded outer parsing. This avoids silently accepting an assertion that could change certificate meaning.

## 20. Status model

Statuses include malformed DER, unsupported algorithm, invalid certificate/public key/time, unavailable time, not-yet-valid, expired, unknown critical extension, bad signature, issuer mismatch, invalid CA/key usage/EKU/path length, hostname mismatch, and untrusted root. No status depends on exceptions or ambient framework policy.

## 21. Memory and execution model

Production parsing uses fixed-size structs, caller-owned buffers, bounded loops, manual range checks, and no reflection, ASN.1 object model, certificate store, or unbounded allocation. The implementation is linked directly into the NativeAOT managed kernel and does not require a GC collection at the Gate4 boundary.

## 22. Fixture generation

Six deterministic DER fixtures were generated outside production using Git OpenSSL and `tools/phase30-fixtures/phase30-openssl.cnf`, then embedded as byte arrays. The reproducible command family, run in a temporary fixture directory, was:

```text
openssl ecparam -name prime256v1 -genkey -noout -out root-key.pem
openssl req -x509 -new -sha256 -key root-key.pem -out root.pem -config phase30-openssl.cnf -extensions root_ext -days 3650
openssl ecparam -name prime256v1 -genkey -noout -out intermediate-key.pem
openssl req -new -sha256 -key intermediate-key.pem -out intermediate.csr -config phase30-openssl.cnf
openssl x509 -req -in intermediate.csr -CA root.pem -CAkey root-key.pem -CAcreateserial -out intermediate.pem -days 3650 -sha256 -extfile phase30-openssl.cnf -extensions intermediate_ext
openssl ecparam -name prime256v1 -genkey -noout -out leaf-key.pem
openssl req -new -sha256 -key leaf-key.pem -out leaf.csr -config phase30-openssl.cnf
openssl x509 -req -in leaf.csr -CA intermediate.pem -CAkey intermediate-key.pem -CAcreateserial -out leaf.pem -days 3650 -sha256 -extfile phase30-openssl.cnf -extensions leaf_ext
openssl x509 -in root.pem -outform DER -out root.der
openssl x509 -in intermediate.pem -outform DER -out intermediate.der
openssl x509 -in leaf.pem -outform DER -out leaf.der
```

The direct-leaf and SAN/CN variants use the same commands with the corresponding `-extensions` section and issuer key. OpenSSL is fixture-generation-only, not a production dependency.

## 23. Fixture identities

| Fixture | Bytes | SHA-256 |
| --- | ---: | --- |
| Root | 434 | `9E420679E08150868848A417A60EE08C1621E277B4C336FB764E1EB06565FBB8` |
| Intermediate | 475 | `9195D89C8B3BB092761A10DB3D985505D4A2ABC777D134D0699E165549499D31` |
| Leaf | 540 | `E9287C431A80A07CAE1F40F7FAE238B264479FDAF99F3D93EA42467631AA7D56` |
| DirectLeaf | 531 | `2E4DC99D1C0CB5E38EDB0471D980B506AECDC0A838D2D43F7655C138653DAA89` |
| NoSanLeaf | 483 | `5FC3A96B56F271988B8205CE96D220E3AE39C3BEF45F379593D0E5F1E6A7AE6E` |
| SanOnlyLeaf | 516 | `CEDFF0A121CE912ADCC10B2CCBFBB3657A3BB05CF2F4DD987552C98F4DE6ED39` |

## 24. Host positive coverage

The Phase 30 host suite passes `MANAGED_KERNEL_PHASE30_HOST_TESTS_PASS cases=91`. Positive cases cover parsing and extraction, exact TBS hashing, direct and intermediate chains, serverAuth, CA/path length, SAN precedence, CN fallback, case-insensitive hostnames, exact wildcards, time boundaries, and recovery after rejected input.

## 25. Host negative coverage

Negative cases cover corrupt leaf/intermediate signatures, wrong root, untrusted root, issuer mismatch, expired/not-yet-valid certificates, CA false, missing keyCertSign, path length, EKU, key usage, unsupported signature/public-key/curve algorithms, off-curve points, bad BIT STRING unused bits, unknown critical extensions, malformed SAN/name/validity, zero serial, truncation, trailing bytes, parent overrun, oversized input, and TBS mutation.

## 26. Host DER and time boundaries

The suite explicitly tests short-form and 127/128 length boundaries, overlong/indefinite/three-octet lengths, depth and element limits, truncated wrappers, exact UTC years 1949/1950/2049/2050, and one-second validity boundaries.

## 27. Retained regression suites

Phase 15–23 pass 508/508; Phase 25 passes 113/113; Phase 26 passes 70/70; Phase 27 passes 100/100; Phase 28 passes 188/188; and Phase 29 passes 209/209. The established Phase 15–29 aggregate remains 1,188/1,188. Existing Phase 25 entropy-unavailable behavior remains an intentional test case.

## 28. NativeAOT integration

`GxManagedKernelRunPhase30` is exported from the NativeAOT DLL and resolved by the Gate4 loader after Phase 29. The proof emits bounded parser, certificate, SPKI, exact-TBS, ECDSA, chain, expiry, corrupted-signature, critical-extension, hostname, trust, Phase 29/28/27/26 regression, recovery, and final-pass markers. The phase is enabled by `-EnableManagedKernelPhase30` and the `ManagedKernelPhase30` scenario.

## 29. Final NativeAOT artifact

The final managed-kernel payload is 1,414,144 bytes with SHA-256 `B50F287EB127C0FBE9D97E6BE99BC9EFBA7686FA7AD41760B51C91B40E41D835`. The export audit contains `GxManagedKernelRunPhase30`; the staged Gate4 payload has the same size and hash.

## 30. Three fresh QEMU boots

The authoritative final run used QEMU 11.0.0 (`v11.0.0-12122-ga4bb4b10c9`). OVMF code SHA-256 is `33090CC07675BAA5190D9F1E84BF5176B33BCBFA9BACAC522961150CDB6DBB2A`; OVMF vars SHA-256 is `5D2AC383371B408398ACCEE7EC27C8C09EA5B74A0DE0CEEA6513388B15BE5D1E`.

| Boot | Serial log SHA-256 | Result |
| --- | --- | --- |
| 1 | `DEAF2EFBA1DA9319637A07C4D444CDE1E28E7CFE6160CDA413A472FD020BFA5D` | PASS |
| 2 | `850DE6B09886BB3487B1E9647B707EE47D0684C2F5BEC7B07B79189C1B33BDFC` | PASS |
| 3 | `0B3EEE15B21DB2C96D11E3D07ED076B7222DD6D4C2FB53AA859C4D7D588455B6` | PASS |

Each log contains exactly one `GXOS_NET10:MANAGED_KERNEL_PHASE30_PASS`, all required Phase 26–29 regression markers, all Phase 30 proof markers, and none of the runner-forbidden terminal markers `GXOS_NET10:FAIL:`, `GXOS_NET10:CPU_EXCEPTION_VECTOR=`, `GXOS_NET10:PAGE_FAULT_`, or `GXOS_NET10:UNEXPECTED_IMPORT_CALL:`. Existing unrelated `...FAILURE...` diagnostic counters remain part of the established harness output.

## 31. Import audit and limitations

Production source contains no framework X509Certificate2, ASN.1, OpenSSL, libcrypto, CommonCrypto, Crypt32, NCrypt, hosted ECDSA, or hosted P-256 dependency. The PE import report retains only the pre-existing `bcrypt.dll!BCryptGenRandom` entropy import among crypto-related imports. Limitations are the narrow profile, P-256/ECDSA-SHA256 only, no RSA, no revocation/AIA/name constraints/policy processing, no OS trust store, no root self-signature requirement in chain validation, ASCII hostname policy only, and the inherited Phase 27 direct crypto-state GC boundary.

## 32. Future TLS integration

Future TLS work can reuse exact-TBS SHA-256, managed ECDSA verification, P-256 SPKI validation, bounded chain validation, and hostname policy. It still needs the TLS handshake/state machine, transcript and CertificateVerify integration, record protection, alert/error policy, broader certificate profiles, and any additional algorithms required by the protocol. Phase 30 certificate validation is not itself a TLS implementation.

## 33. Phase 31 reuse and status

Phase 31 now consumes this validator directly. Its TLS Certificate path copies
the bounded peer chain into caller-owned storage, treats the first peer
certificate as the leaf, and supplies the explicitly configured Phase 30 root
when the server omits that root. A peer root is accepted only on exact
byte-for-byte equality with the configured trust anchor. The Phase 31 client
then reuses Phase 30's exact-TBS signature, SPKI, validity, CA/path-length,
key-usage, EKU, SAN/CN, hostname, and critical-extension policy; no hosted
certificate APIs or OS trust store were added.

Phase 30 remains independently green at 91/91 host cases and is retained in
each Phase 31 authoritative boot. Phase 31's TLS handshake status and evidence
are recorded in [MANAGED_KERNEL_PHASE31_TLS12_HANDSHAKE.md](MANAGED_KERNEL_PHASE31_TLS12_HANDSHAKE.md).

using System;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase36HostTests;

internal static class Program
{
    private static int s_cases;
    private static readonly ManagedX509UtcTime TestTime =
        new(2026, 8, 31, 12, 0, 0);

    private static int Main()
    {
        try
        {
            RunSha384Tests();
            RunObservedCertificateTests();
            RunNegativeTests();
            RunGcSurvivalTest();
            Console.WriteLine($"MANAGED_KERNEL_PHASE36_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE36_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void RunSha384Tests()
    {
        CheckHash("sha384-empty", Array.Empty<byte>(),
            "38B060A751AC96384CD9327EB1B1E36A21FDB71114BE07434C0CC7BF63F6E1DA274EDEBFE76F65FBD51AD2F14898B95B");
        CheckHash("sha384-abc", "abc"u8.ToArray(),
            "CB00753F45A35E8BB5A03D699AC65007272C32AB0EDED1631A8B605A43FF5BED8086072BA1E7CC2358BAECA134C825A7");
        CheckHash("sha384-multi-block", Pattern(256),
            "FFDAEBFF65ED05CF400F0221C4CCFB4B2104FB6A51F87E40BE6C4309386BFDEC2892E9179B34632331A59592737DB5C5");
        CheckHash("sha384-boundary-111", Pattern(111),
            "F5F9FE110D809D34029DE262A01B208356CAEC6E054C7F926B2591F6C9780579D4B59F5578C6F531A84F158A33660CEF");
        CheckHash("sha384-boundary-112", Pattern(112),
            "33BA080EC0CCB378E4E95FED3B26C23AA1A280476E007519EE47F60CD9C5C8A65D627259A9AA2FD33CA06D3C14EE5548");
        CheckHash("sha384-boundary-127", Pattern(127),
            "D5FCFE2FCF6B3EF375EDE37C8123D9B78065FECC1D55197E2F7721E6E9A93D0BA4D7FD15F9B96DEA2744DF24141BA2EF");
        CheckHash("sha384-boundary-128", Pattern(128),
            "CA2385773319124534111A36D0581FC3F00815E907034B90CFF9C3A861E126A741D5DFCFF65A417B6D7296863AC0EC17");
        CheckHash("sha384-boundary-129", Pattern(129),
            "EF49AE5B9AD51433D00323528D81EA8D2E4D2B507DBD9F1CB84F952B66249A788B1C89FCDB77A0DB9F1FEB901D47FC73");

        byte[] fragmented = Pattern(256);
        ManagedSha384 hash = new();
        int offset = 0;
        int[] chunks = { 1, 7, 13, 2, 31 };
        int chunkIndex = 0;
        while (offset != fragmented.Length)
        {
            int length = Math.Min(chunks[chunkIndex++ % chunks.Length],
                                  fragmented.Length - offset);
            Case("sha384-fragmented-append", hash.Append(
                fragmented.AsSpan(offset, length)));
            offset += length;
        }
        Span<byte> digest = stackalloc byte[ManagedSha384.DigestSize];
        Case("sha384-fragmented-finalize", hash.TryFinalize(digest));
        Case("sha384-fragmented-value", ToHex(digest) ==
            "FFDAEBFF65ED05CF400F0221C4CCFB4B2104FB6A51F87E40BE6C4309386BFDEC2892E9179B34632331A59592737DB5C5");
        digest.Clear();
    }

    private static void RunObservedCertificateTests()
    {
        ManagedX509Certificate leaf = Parse(ManagedPhase36Fixtures.Leaf, "leaf");
        ManagedX509Certificate intermediate = Parse(
            ManagedPhase36Fixtures.Intermediate, "intermediate");
        ManagedX509Certificate root = Parse(
            ManagedPhase36Fixtures.CrossSignedRoot, "cross-signed-root");

        Case("leaf-ecdsa-sha256", leaf.SignatureAlgorithm ==
            ManagedX509SignatureAlgorithm.EcdsaSha256);
        Case("leaf-p256", leaf.PublicKeyAlgorithm ==
            ManagedX509PublicKeyAlgorithm.EcdsaP256);
        Case("leaf-san-two-names", leaf.HasSubjectAltName &&
             leaf.DnsNameCount == 2);
        Case("leaf-server-auth", leaf.HasExtendedKeyUsage &&
             leaf.HasServerAuth);
        Case("leaf-digital-signature", leaf.HasDigitalSignature);
        Case("intermediate-ecdsa-sha384", intermediate.SignatureAlgorithm ==
            ManagedX509SignatureAlgorithm.EcdsaSha384);
        Case("intermediate-p256", intermediate.PublicKeyAlgorithm ==
            ManagedX509PublicKeyAlgorithm.EcdsaP256);
        Case("intermediate-ca", intermediate.IsCertificateAuthority);
        Case("intermediate-path-length-zero", intermediate.HasPathLengthConstraint &&
             intermediate.PathLengthConstraint == 0);
        Case("intermediate-key-cert-sign", intermediate.HasKeyCertSign);
        Case("cross-root-rsa-sha256-signature", root.SignatureAlgorithm ==
            ManagedX509SignatureAlgorithm.RsaSha256);
        Case("cross-root-p384", root.PublicKeyAlgorithm ==
            ManagedX509PublicKeyAlgorithm.EcdsaP384 &&
            root.PublicKeyLength == ManagedP384.PublicKeySize);
        Case("cross-root-ca", root.IsCertificateAuthority && root.HasKeyCertSign);

        ManagedX509ValidationStatus status;
        bool leafLink = ManagedX509.TryValidateCertificateSignature(
            ManagedPhase36Fixtures.Leaf, in leaf,
            ManagedPhase36Fixtures.Intermediate.AsSpan(
                intermediate.PublicKeyOffset, intermediate.PublicKeyLength),
            out status);
        CaseStatus("leaf-signature-link", ManagedX509ValidationStatus.Success,
                   status);
        Case("leaf-signature-link-accepted", leafLink);

        bool intermediateLink = ManagedX509.TryValidateCertificateSignature(
            ManagedPhase36Fixtures.Intermediate, in intermediate,
            ManagedPhase36Fixtures.CrossSignedRoot.AsSpan(
                root.PublicKeyOffset, root.PublicKeyLength),
            out status);
        CaseStatus("intermediate-sha384-p384-link",
                   ManagedX509ValidationStatus.Success, status);
        Case("intermediate-sha384-p384-link-accepted", intermediateLink);

        CaseStatus("observed-cross-signed-chain",
            ManagedX509ValidationStatus.Success,
            Validate(ManagedPhase36Fixtures.Leaf,
                     ManagedPhase36Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot, TestTime,
                     "www.cloudflare.com"u8));

        ManagedX509TrustAnchorMatch match;
        Case("trust-anchor-subject-key-match",
            ManagedX509.TryMatchTrustAnchorIdentity(
                ManagedPhase36Fixtures.CrossSignedRoot, in root,
                ManagedPhase36Fixtures.CrossSignedRoot, out match) &&
            match == ManagedX509TrustAnchorMatch.Match);

        byte[] reencodedAnchor = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.CrossSignedRoot);
        reencodedAnchor[root.SignatureOffset + root.SignatureLength - 1] ^= 1;
        CaseStatus("cross-signed-anchor-encoding-independent",
            ManagedX509ValidationStatus.Success,
            Validate(ManagedPhase36Fixtures.Leaf,
                     ManagedPhase36Fixtures.Intermediate, Array.Empty<byte>(),
                     reencodedAnchor, ManagedPhase36Fixtures.CrossSignedRoot,
                     TestTime, "www.cloudflare.com"u8));
    }

    private static void RunNegativeTests()
    {
        byte[] unsupported = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.Intermediate);
        int sha384Oid = Find(unsupported,
            new byte[] { 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D,
                         0x04, 0x03, 0x03 });
        Require(sha384Oid >= 0, "sha384 OID fixture");
        unsupported[sha384Oid + 9] = 0x04;
        ExpectParseStatus("unsupported-signature-algorithm", unsupported,
            ManagedX509ValidationStatus.UnsupportedAlgorithm);

        byte[] mismatch = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.Intermediate);
        mismatch[sha384Oid + 9] = 0x02;
        ExpectParseStatus("signature-algorithm-tbs-mismatch", mismatch,
            ManagedX509ValidationStatus.CertificateAlgorithmMismatch);

        byte[] badPoint = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.CrossSignedRoot);
        ManagedX509Certificate root = Parse(
            ManagedPhase36Fixtures.CrossSignedRoot, "cross-signed-root-again");
        badPoint[root.PublicKeyOffset] = 0x05;
        ExpectParseStatus("malformed-p384-public-point", badPoint,
            ManagedX509ValidationStatus.InvalidPublicKey);

        byte[] fakeKey = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.CrossSignedRoot);
        WriteP384Generator(fakeKey.AsSpan(root.PublicKeyOffset,
                                          root.PublicKeyLength));
        ManagedX509Certificate fakeRoot = Parse(fakeKey, "fake-anchor");
        ManagedX509TrustAnchorMatch match;
        Case("fake-anchor-key-is-not-anchor",
            ManagedX509.TryMatchTrustAnchorIdentity(
                fakeKey, in fakeRoot,
                ManagedPhase36Fixtures.CrossSignedRoot, out match) &&
            match == ManagedX509TrustAnchorMatch.SubjectKeyMismatch);
        CaseStatus("fake-anchor-key-rejected",
            ManagedX509ValidationStatus.UntrustedRoot,
            Validate(ManagedPhase36Fixtures.Leaf,
                     ManagedPhase36Fixtures.Intermediate, Array.Empty<byte>(),
                     fakeKey, ManagedPhase36Fixtures.CrossSignedRoot,
                     TestTime, "www.cloudflare.com"u8));

        byte[] badIntermediateSignature = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.Intermediate);
        ManagedX509Certificate intermediate = Parse(
            ManagedPhase36Fixtures.Intermediate, "intermediate-again");
        badIntermediateSignature[intermediate.SignatureOffset +
                                 intermediate.SignatureLength - 1] ^= 1;
        CaseStatus("bad-sha384-signature", ManagedX509ValidationStatus.BadSignature,
            Validate(ManagedPhase36Fixtures.Leaf, badIntermediateSignature,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot, TestTime,
                     "www.cloudflare.com"u8));

        byte[] malformedSignature = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.Intermediate);
        malformedSignature[intermediate.SignatureOffset] = 0x31;
        CaseStatus("malformed-ecdsa-signature", ManagedX509ValidationStatus.BadSignature,
            Validate(ManagedPhase36Fixtures.Leaf, malformedSignature,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot, TestTime,
                     "www.cloudflare.com"u8));

        byte[] wrongIssuerKey = P384Generator();
        bool wrongIssuerAccepted = ManagedX509.TryValidateCertificateSignature(
            ManagedPhase36Fixtures.Intermediate, in intermediate,
            wrongIssuerKey, out ManagedX509ValidationStatus wrongIssuerStatus);
        CaseStatus("wrong-issuer-key", ManagedX509ValidationStatus.BadSignature,
            wrongIssuerStatus);
        Case("wrong-issuer-key-rejected", !wrongIssuerAccepted);

        byte[] noKeyCertSign = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.Intermediate);
        int intermediateKeyUsage = Find(noKeyCertSign,
            new byte[] { 0x03, 0x02, 0x01, 0x86 });
        Require(intermediateKeyUsage >= 0, "intermediate key usage fixture");
        noKeyCertSign[intermediateKeyUsage + 3] = 0x80;
        CaseStatus("missing-intermediate-key-cert-sign",
            ManagedX509ValidationStatus.InvalidKeyUsage,
            Validate(ManagedPhase36Fixtures.Leaf, noKeyCertSign,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot, TestTime,
                     "www.cloudflare.com"u8));

        CaseStatus("non-ca-intermediate", ManagedX509ValidationStatus.InvalidCa,
            Validate(ManagedPhase36Fixtures.Leaf, ManagedPhase36Fixtures.Leaf,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot, TestTime,
                     "www.cloudflare.com"u8));
        CaseStatus("hostname-mismatch", ManagedX509ValidationStatus.HostnameMismatch,
            Validate(ManagedPhase36Fixtures.Leaf, ManagedPhase36Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot, TestTime,
                     "not-cloudflare.example"u8));
        CaseStatus("expired-chain", ManagedX509ValidationStatus.Expired,
            Validate(ManagedPhase36Fixtures.Leaf, ManagedPhase36Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot,
                     new ManagedX509UtcTime(2029, 3, 1, 0, 0, 0),
                     "www.cloudflare.com"u8));
        CaseStatus("not-yet-valid-chain", ManagedX509ValidationStatus.NotYetValid,
            Validate(ManagedPhase36Fixtures.Leaf, ManagedPhase36Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot,
                     new ManagedX509UtcTime(2023, 1, 1, 0, 0, 0),
                     "www.cloudflare.com"u8));

        byte[] changedTbs = ManagedPhase36Fixtures.Clone(
            ManagedPhase36Fixtures.Intermediate);
        changedTbs[intermediate.SerialOffset] ^= 1;
        CaseStatus("sha384-tbs-digest-mismatch",
            ManagedX509ValidationStatus.BadSignature,
            Validate(ManagedPhase36Fixtures.Leaf, changedTbs, Array.Empty<byte>(),
                     ManagedPhase36Fixtures.CrossSignedRoot,
                     ManagedPhase36Fixtures.CrossSignedRoot, TestTime,
                     "www.cloudflare.com"u8));
    }

    private static void RunGcSurvivalTest()
    {
        ManagedX509Certificate leaf = Parse(ManagedPhase36Fixtures.Leaf,
                                             "gc-leaf");
        ManagedX509Certificate root = Parse(ManagedPhase36Fixtures.CrossSignedRoot,
                                             "gc-root");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Case("gc-leaf-hostname", ManagedX509.TryMatchHostname(
            ManagedPhase36Fixtures.Leaf, in leaf, "www.cloudflare.com"u8));
        GC.Collect();
        Case("gc-cross-root-key", ManagedP384.TryValidatePublicKey(
            ManagedPhase36Fixtures.CrossSignedRoot.AsSpan(
                root.PublicKeyOffset, root.PublicKeyLength)));
        GC.KeepAlive(ManagedPhase36Fixtures.Leaf);
        GC.KeepAlive(ManagedPhase36Fixtures.CrossSignedRoot);
    }

    private static ManagedX509Certificate Parse(byte[] bytes, string name)
    {
        ManagedX509ValidationStatus status = ManagedX509.TryParseCertificate(
            bytes, out ManagedX509Certificate certificate, out _);
        if (status != ManagedX509ValidationStatus.Success)
            throw new InvalidOperationException($"{name} parse: {status}");
        return certificate;
    }

    private static ManagedX509ValidationStatus Validate(
        ReadOnlySpan<byte> leaf, ReadOnlySpan<byte> intermediate1,
        ReadOnlySpan<byte> intermediate2, ReadOnlySpan<byte> candidateRoot,
        ReadOnlySpan<byte> trustedRoot, in ManagedX509UtcTime time,
        ReadOnlySpan<byte> hostname)
    {
        ManagedX509.TryValidateServerChain(
            leaf, intermediate1, intermediate2, candidateRoot, trustedRoot,
            in time, hostname, out ManagedX509ValidationStatus status);
        return status;
    }

    private static void CheckHash(string name, byte[] input, string expected)
    {
        Span<byte> digest = stackalloc byte[ManagedSha384.DigestSize];
        Case(name, ManagedSha384.TryHash(input, digest) && ToHex(digest) == expected);
        digest.Clear();
    }

    private static void ExpectParseStatus(string name, byte[] bytes,
                                          ManagedX509ValidationStatus expected)
    {
        ManagedX509ValidationStatus actual = ManagedX509.TryParseCertificate(
            bytes, out _, out _);
        CaseStatus(name, expected, actual);
    }

    private static void CaseStatus(string name,
                                   ManagedX509ValidationStatus expected,
                                   ManagedX509ValidationStatus actual)
    {
        ++s_cases;
        if (expected != actual)
            throw new InvalidOperationException(
                $"{name}: expected {expected}, actual {actual}");
    }

    private static void Case(string name, bool passed)
    {
        ++s_cases;
        if (!passed) throw new InvalidOperationException($"failed: {name}");
    }

    private static byte[] Pattern(int length)
    {
        byte[] value = new byte[length];
        for (int index = 0; index != value.Length; ++index)
            value[index] = (byte)index;
        return value;
    }

    private static byte[] P384Generator()
    {
        byte[] value = new byte[ManagedP384.PublicKeySize];
        WriteP384Generator(value);
        return value;
    }

    private static void WriteP384Generator(Span<byte> destination)
    {
        byte[] generator = Convert.FromHexString(
            "04AA87CA22BE8B05378EB1C71EF320AD746E1D3B628BA79B9859F741E082542A385502F25DBF55296C3A545E3872760AB7" +
            "3617DE4A96262C6F5D9E98BF9292DC29F8F41DBD289A147CE9DA3113B5F0B8C00A60B1CE1D7E819D7A431D7C90EA0E5F");
        generator.AsSpan().CopyTo(destination);
    }

    private static int Find(ReadOnlySpan<byte> haystack,
                            ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length) return -1;
        for (int offset = 0; offset <= haystack.Length - needle.Length; ++offset)
        {
            bool equal = true;
            for (int index = 0; index != needle.Length; ++index)
            {
                if (haystack[offset + index] != needle[index])
                {
                    equal = false;
                    break;
                }
            }
            if (equal) return offset;
        }
        return -1;
    }

    private static string ToHex(ReadOnlySpan<byte> bytes)
    {
        const string digits = "0123456789ABCDEF";
        char[] chars = new char[bytes.Length * 2];
        for (int index = 0; index != bytes.Length; ++index)
        {
            chars[index * 2] = digits[bytes[index] >> 4];
            chars[index * 2 + 1] = digits[bytes[index] & 0x0F];
        }
        return new string(chars);
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}

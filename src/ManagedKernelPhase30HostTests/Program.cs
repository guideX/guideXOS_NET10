using System;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase30HostTests;

internal static class Program
{
    private static int s_cases;
    private static readonly ManagedX509UtcTime TestTime =
        new(2028, 1, 1, 0, 0, 0);

    private static int Main()
    {
        try
        {
            RunParserTests();
            RunChainTests();
            RunHostnameTests();
            RunDerBoundaryTests();
            RunTimeTests();
            RunMutationTests();
            RunGcSurvivalTest();
            Console.WriteLine($"MANAGED_KERNEL_PHASE30_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE30_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void RunParserTests()
    {
        ManagedX509Certificate root = Parse(
            ManagedX509Phase30Fixtures.Root, "root");
        ManagedX509Certificate intermediate = Parse(
            ManagedX509Phase30Fixtures.Intermediate, "intermediate");
        ManagedX509Certificate leaf = Parse(
            ManagedX509Phase30Fixtures.Leaf, "leaf");
        ManagedX509Certificate directLeaf = Parse(
            ManagedX509Phase30Fixtures.DirectLeaf, "direct-leaf");

        Case("root-version-v3", root.TbsLength != 0);
        Case("root-ca", root.HasBasicConstraints && root.IsCertificateAuthority);
        Case("root-key-cert-sign", root.HasKeyUsage && root.HasKeyCertSign);
        Case("root-path-length", root.HasPathLengthConstraint &&
             root.PathLengthConstraint == 1);
        Case("intermediate-ca", intermediate.IsCertificateAuthority);
        Case("intermediate-key-cert-sign", intermediate.HasKeyCertSign);
        Case("intermediate-path-length-zero",
             intermediate.PathLengthConstraint == 0);
        Case("leaf-not-ca", leaf.HasBasicConstraints &&
             !leaf.IsCertificateAuthority);
        Case("leaf-digital-signature", leaf.HasDigitalSignature);
        Case("leaf-server-auth", leaf.HasExtendedKeyUsage &&
             leaf.HasServerAuth);
        Case("leaf-two-dns-names", leaf.HasSubjectAltName &&
             leaf.DnsNameCount == 2);
        Case("leaf-common-name", leaf.CommonNameLength != 0);
        Case("p256-spki-size", leaf.PublicKeyLength == ManagedP256.PublicKeySize);
        Case("exact-tbs-signature-root-leaf",
             Verify(ManagedX509Phase30Fixtures.DirectLeaf, directLeaf,
                    ManagedX509Phase30Fixtures.Root, root));
        Case("exact-tbs-signature-intermediate-leaf",
             Verify(ManagedX509Phase30Fixtures.Leaf, leaf,
                    ManagedX509Phase30Fixtures.Intermediate, intermediate));

        Span<byte> digest = stackalloc byte[ManagedP256.DigestSize];
        Case("exact-tbs-hash-available",
             ManagedSha256.TryHash(
                 ManagedX509Phase30Fixtures.Leaf.AsSpan(
                     leaf.TbsOffset, leaf.TbsLength), digest));
        digest.Clear();
    }

    private static void RunChainTests()
    {
        CaseStatus("chain-a-root-leaf", ManagedX509ValidationStatus.Success,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        CaseStatus("chain-b-root-intermediate-leaf",
            ManagedX509ValidationStatus.Success,
            Validate(ManagedX509Phase30Fixtures.Leaf,
                     ManagedX509Phase30Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        CaseStatus("unconfigured-self-signed-root",
            ManagedX509ValidationStatus.UntrustedRoot,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Intermediate, TestTime,
                     "www.example.com"u8));
        CaseStatus("unavailable-current-time",
            ManagedX509ValidationStatus.TimeUnavailable,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509UtcTime.Unavailable, "www.example.com"u8));
        CaseStatus("chain-gap-rejected", ManagedX509ValidationStatus.InvalidCertificate,
            Validate(ManagedX509Phase30Fixtures.Leaf, Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Intermediate,
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
    }

    private static void RunHostnameTests()
    {
        ManagedX509Certificate leaf = Parse(
            ManagedX509Phase30Fixtures.Leaf, "hostname-leaf");
        Case("hostname-exact", ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.Leaf, in leaf, "www.example.com"u8));
        Case("hostname-case-insensitive", ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.Leaf, in leaf, "WWW.EXAMPLE.COM"u8));
        Case("hostname-wildcard-single-label", ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.Leaf, in leaf, "api.example.com"u8));
        Case("hostname-wildcard-parent-rejected", !ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.Leaf, in leaf, "example.com"u8));
        Case("hostname-wildcard-multilevel-rejected", !ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.Leaf, in leaf, "a.b.example.com"u8));
        Case("hostname-mismatch", !ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.Leaf, in leaf, "other.example.net"u8));
        ManagedX509Certificate sanOnly = Parse(
            ManagedX509Phase30Fixtures.SanOnlyLeaf, "san-only-hostname");
        Case("san-precedes-cn", !ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.SanOnlyLeaf, in sanOnly,
            "cn-only.example.com"u8));

        ManagedX509Certificate cnOnly = Parse(
            ManagedX509Phase30Fixtures.NoSanLeaf, "cn-only-leaf");
        Case("cn-fallback-without-dns-san", ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.NoSanLeaf, in cnOnly,
            "cn-only.example.com"u8));
        Case("cn-fallback-mismatch", !ManagedX509.TryMatchHostname(
            ManagedX509Phase30Fixtures.NoSanLeaf, in cnOnly,
            "www.example.com"u8));

        Case("wildcard-pattern-valid",
             ManagedX509.IsValidDnsNameForTest("*.example.com"u8, true));
        Case("wildcard-top-level-rejected",
             !ManagedX509.IsValidDnsNameForTest("*.com"u8, true));
        Case("wildcard-embedded-rejected",
             !ManagedX509.IsValidDnsNameForTest("foo*.example.com"u8, true));
        Case("wildcard-prefix-rejected",
             !ManagedX509.IsValidDnsNameForTest("*foo.example.com"u8, true));
        Case("hostname-empty-rejected",
             !ManagedX509.IsValidDnsNameForTest(ReadOnlySpan<byte>.Empty, false));
        Case("hostname-nonascii-rejected",
             !ManagedX509.IsValidDnsNameForTest(
                 new byte[] { (byte)'w', (byte)'e', 0xC3, 0xA9 }, false));
        Case("hostname-oversized-rejected",
             !ManagedX509.IsValidDnsNameForTest(new byte[254], false));
    }

    private static void RunDerBoundaryTests()
    {
        Case("der-short-sequence", ReadContainer(
            new byte[] { 0x30, 0x03, 0x02, 0x01, 0x01 }));
        byte[] longForm = new byte[131];
        longForm[0] = 0x30;
        longForm[1] = 0x81;
        longForm[2] = 0x80;
        for (int index = 0; index != 64; ++index)
        {
            longForm[3 + index * 2] = 0x05;
            longForm[4 + index * 2] = 0;
        }
        Case("der-127-128-transition", ReadContainer(longForm));
        Case("der-overlong-length-rejected", !ReadContainer(
            new byte[] { 0x30, 0x81, 0x01, 0x00 }));
        Case("der-indefinite-length-rejected", !ReadContainer(
            new byte[] { 0x30, 0x80, 0x00, 0x00 }));
        Case("der-three-length-octets-rejected", !ReadContainer(
            new byte[] { 0x30, 0x83, 0x00, 0x00, 0x01, 0x00 }));
        Case("der-parent-overrun-rejected", !ReadContainer(
            new byte[] { 0x30, 0x03, 0x02, 0x02, 0x01 }));
        Case("der-truncated-header-rejected", !ReadContainer(
            new byte[] { 0x30 }));
        Case("der-trailing-top-level-rejected", !ReadContainer(
            new byte[] { 0x30, 0x00, 0x00 }));
        byte[] tooMany = new byte[4 + 2 * 257];
        tooMany[0] = 0x30;
        tooMany[1] = 0x82;
        tooMany[2] = 0x02;
        tooMany[3] = 0x02;
        for (int index = 0; index != 257; ++index)
        {
            tooMany[4 + index * 2] = 0x05;
            tooMany[5 + index * 2] = 0;
        }
        Case("der-element-count-bound", !ReadContainer(tooMany));

        byte[] nested = new byte[26];
        for (int index = 0; index != 13; ++index)
        {
            nested[index] = 0x30;
            nested[index + 1] = (byte)(24 - 2 * index);
        }
        Case("der-depth-bound", !ReadNestedDepth(nested));
    }

    private static void RunTimeTests()
    {
        Case("utc-1950", ManagedX509.TryParseTimeForTest(
            0x17, "500101000000Z"u8, out ManagedX509UtcTime utc1950) &&
            utc1950.Year == 1950);
        Case("utc-2049", ManagedX509.TryParseTimeForTest(
            0x17, "491231235959Z"u8, out ManagedX509UtcTime utc2049) &&
            utc2049.Year == 2049);
        Case("generalized-2050", ManagedX509.TryParseTimeForTest(
            0x18, "20500101000000Z"u8, out ManagedX509UtcTime generalized2050) &&
            generalized2050.Year == 2050);
        Case("utc-49-is-2049", ManagedX509.TryParseTimeForTest(
            0x17, "490101000000Z"u8, out ManagedX509UtcTime utc49) &&
            utc49.Year == 2049);
        Case("generalized-1949", ManagedX509.TryParseTimeForTest(
            0x18, "19490101000000Z"u8, out ManagedX509UtcTime generalized1949) &&
            generalized1949.Year == 1949);
        Case("time-malformed-zone-rejected", !ManagedX509.TryParseTimeForTest(
            0x18, "20500101000000+"u8, out _));
        Case("time-invalid-calendar-rejected", !ManagedX509.TryParseTimeForTest(
            0x18, "20230230000000Z"u8, out _));

        ManagedX509Certificate directLeaf = Parse(
            ManagedX509Phase30Fixtures.DirectLeaf, "time-leaf");
        CaseStatus("exact-not-before", ManagedX509ValidationStatus.Success,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, directLeaf.NotBefore,
                     "www.example.com"u8));
        CaseStatus("exact-not-after", ManagedX509ValidationStatus.Success,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, directLeaf.NotAfter,
                     "www.example.com"u8));
        CaseStatus("one-second-before-not-before",
            ManagedX509ValidationStatus.NotYetValid,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root,
                     new ManagedX509UtcTime(directLeaf.NotBefore.Year,
                         directLeaf.NotBefore.Month, directLeaf.NotBefore.Day,
                         directLeaf.NotBefore.Hour, directLeaf.NotBefore.Minute,
                         directLeaf.NotBefore.Second - 1),
                     "www.example.com"u8));
        CaseStatus("one-second-after-not-after",
            ManagedX509ValidationStatus.Expired,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root,
                     new ManagedX509UtcTime(directLeaf.NotAfter.Year,
                         directLeaf.NotAfter.Month, directLeaf.NotAfter.Day,
                         directLeaf.NotAfter.Hour, directLeaf.NotAfter.Minute,
                         directLeaf.NotAfter.Second + 1),
                     "www.example.com"u8));
    }

    private static void RunMutationTests()
    {
        byte[] badLeafSignature = Clone(ManagedX509Phase30Fixtures.DirectLeaf);
        badLeafSignature[^1] ^= 1;
        CaseStatus("corrupted-leaf-signature", ManagedX509ValidationStatus.BadSignature,
            Validate(badLeafSignature, Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));

        byte[] badIntermediateSignature = Clone(ManagedX509Phase30Fixtures.Intermediate);
        badIntermediateSignature[^1] ^= 1;
        CaseStatus("corrupted-intermediate-signature",
            ManagedX509ValidationStatus.BadSignature,
            Validate(ManagedX509Phase30Fixtures.Leaf, badIntermediateSignature,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));

        CaseStatus("wrong-root", ManagedX509ValidationStatus.UntrustedRoot,
            Validate(ManagedX509Phase30Fixtures.DirectLeaf,
                     Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Intermediate, TestTime,
                     "www.example.com"u8));

        byte[] expiredLeaf = WithUtcTime(ManagedX509Phase30Fixtures.DirectLeaf,
                                          false, "270825200539Z"u8);
        Case("expired-leaf-remains-parseable",
             ManagedX509.TryParseCertificate(expiredLeaf, out _));
        CaseStatus("leaf-expired", ManagedX509ValidationStatus.Expired,
            Validate(expiredLeaf, Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        byte[] futureLeaf = WithUtcTime(ManagedX509Phase30Fixtures.DirectLeaf,
                                        true, "300101000000Z"u8);
        CaseStatus("leaf-not-yet-valid", ManagedX509ValidationStatus.NotYetValid,
            Validate(futureLeaf, Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        byte[] expiredIntermediate = WithUtcTime(
            ManagedX509Phase30Fixtures.Intermediate, false,
            "270825200525Z"u8);
        CaseStatus("intermediate-expired", ManagedX509ValidationStatus.Expired,
            Validate(ManagedX509Phase30Fixtures.Leaf, expiredIntermediate,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));

        byte[] unsupportedSignature = MutateOid(
            ManagedX509Phase30Fixtures.Root,
            new byte[] { 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x04,
                         0x03, 0x02 }, false);
        ExpectParseStatus("unsupported-signature-algorithm", unsupportedSignature,
                          ManagedX509ValidationStatus.UnsupportedAlgorithm);
        byte[] unsupportedOuter = MutateOid(
            ManagedX509Phase30Fixtures.Root,
            new byte[] { 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x04,
                         0x03, 0x02 }, true);
        ExpectParseStatus("outer-tbs-signature-algorithm-mismatch",
                          unsupportedOuter,
                          ManagedX509ValidationStatus.UnsupportedAlgorithm);
        byte[] unsupportedPublicAlgorithm = MutateOid(
            ManagedX509Phase30Fixtures.Root,
            new byte[] { 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02,
                         0x01 }, false);
        ExpectParseStatus("unsupported-public-key-algorithm",
                          unsupportedPublicAlgorithm,
                          ManagedX509ValidationStatus.UnsupportedAlgorithm);
        byte[] unsupportedCurve = MutateOid(
            ManagedX509Phase30Fixtures.Root,
            new byte[] { 0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03,
                         0x01, 0x07 }, false);
        ExpectParseStatus("unsupported-curve", unsupportedCurve,
                          ManagedX509ValidationStatus.UnsupportedAlgorithm);

        byte[] malformedKey = Clone(ManagedX509Phase30Fixtures.Root);
        int keyPrefix = Find(malformedKey, new byte[] { 0x03, 0x42, 0x00, 0x04 });
        Require(keyPrefix >= 0, "key prefix fixture");
        malformedKey[keyPrefix + 4] ^= 1;
        ExpectParseStatus("malformed-off-curve-public-key", malformedKey,
                          ManagedX509ValidationStatus.InvalidPublicKey);
        byte[] nonzeroUnusedBits = Clone(ManagedX509Phase30Fixtures.Root);
        Require(keyPrefix >= 0, "key prefix fixture reused");
        nonzeroUnusedBits[keyPrefix + 2] = 1;
        ExpectParseStatus("nonzero-spki-unused-bits", nonzeroUnusedBits,
                          ManagedX509ValidationStatus.InvalidPublicKey);

        byte[] issuerMismatch = Clone(ManagedX509Phase30Fixtures.Leaf);
        int issuerName = Find(issuerMismatch,
            new byte[] { (byte)'g', (byte)'u', (byte)'i', (byte)'d', (byte)'e',
                         (byte)'X', (byte)'O', (byte)'S', (byte)' ', (byte)'T',
                         (byte)'e', (byte)'s', (byte)'t', (byte)' ', (byte)'I',
                         (byte)'n', (byte)'t', (byte)'e', (byte)'r', (byte)'m',
                         (byte)'e', (byte)'d', (byte)'i', (byte)'a', (byte)'t',
                         (byte)'e' });
        Require(issuerName >= 0, "issuer fixture");
        issuerMismatch[issuerName] = (byte)'X';
        CaseStatus("issuer-subject-mismatch",
            ManagedX509ValidationStatus.IssuerSubjectMismatch,
            Validate(issuerMismatch, ManagedX509Phase30Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));

        CaseStatus("ca-flag-false-intermediate", ManagedX509ValidationStatus.InvalidCa,
            Validate(ManagedX509Phase30Fixtures.Leaf,
                     ManagedX509Phase30Fixtures.Leaf, Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        byte[] missingKeyCertSign = Clone(ManagedX509Phase30Fixtures.Intermediate);
        int keyUsage = Find(missingKeyCertSign,
            new byte[] { 0x03, 0x02, 0x01, 0x06 });
        Require(keyUsage >= 0, "issuer key usage fixture");
        missingKeyCertSign[keyUsage + 3] = 0x02;
        CaseStatus("issuer-key-cert-sign-missing",
            ManagedX509ValidationStatus.InvalidKeyUsage,
            Validate(ManagedX509Phase30Fixtures.Leaf, missingKeyCertSign,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        byte[] pathExceeded = Clone(ManagedX509Phase30Fixtures.Root);
        int path = Find(pathExceeded,
            new byte[] { 0x30, 0x06, 0x01, 0x01, 0xFF, 0x02, 0x01, 0x01 });
        Require(path >= 0, "root path fixture");
        pathExceeded[path + 7] = 0;
        CaseStatus("path-length-exceeded", ManagedX509ValidationStatus.PathLengthExceeded,
            Validate(ManagedX509Phase30Fixtures.Leaf,
                     ManagedX509Phase30Fixtures.Intermediate,
                     Array.Empty<byte>(), pathExceeded, pathExceeded, TestTime,
                     "www.example.com"u8));

        byte[] noServerAuth = MutateOid(ManagedX509Phase30Fixtures.Leaf,
            new byte[] { 0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07,
                         0x03, 0x01 }, false);
        CaseStatus("leaf-eku-excludes-server-auth",
            ManagedX509ValidationStatus.InvalidExtendedKeyUsage,
            Validate(noServerAuth, ManagedX509Phase30Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        byte[] noDigitalSignature = Clone(ManagedX509Phase30Fixtures.Leaf);
        int digitalSignature = Find(noDigitalSignature,
            new byte[] { 0x03, 0x02, 0x07, 0x80 });
        Require(digitalSignature >= 0, "leaf key usage fixture");
        noDigitalSignature[digitalSignature + 3] = 0;
        CaseStatus("leaf-key-usage-excludes-digital-signature",
            ManagedX509ValidationStatus.InvalidKeyUsage,
            Validate(noDigitalSignature, ManagedX509Phase30Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));

        byte[] unknownCritical = MutateSanOid(ManagedX509Phase30Fixtures.Leaf);
        CaseStatus("unknown-critical-extension",
            ManagedX509ValidationStatus.UnknownCriticalExtension,
            Validate(unknownCritical, ManagedX509Phase30Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
        byte[] malformedCritical = Clone(ManagedX509Phase30Fixtures.Leaf);
        int sanSequence = Find(malformedCritical,
            new byte[] { 0x55, 0x1D, 0x11, 0x01, 0x01, 0xFF, 0x04, 0x22,
                         0x30, 0x20 });
        Require(sanSequence >= 0, "san critical fixture");
        malformedCritical[sanSequence + 8] = 0x31;
        ExpectParseStatus("malformed-critical-extension", malformedCritical,
                          ManagedX509ValidationStatus.MalformedDer);
        byte[] malformedSan = Clone(ManagedX509Phase30Fixtures.Leaf);
        int dnsLength = Find(malformedSan,
            new byte[] { 0x82, 0x0F, (byte)'w', (byte)'w', (byte)'w' });
        Require(dnsLength >= 0, "dns fixture");
        malformedSan[dnsLength + 1] = 0x7F;
        ExpectParseStatus("malformed-san", malformedSan,
                          ManagedX509ValidationStatus.MalformedDer);

        byte[] malformedIssuer = Clone(ManagedX509Phase30Fixtures.Root);
        int issuerSet = Find(malformedIssuer, new byte[] { 0x31, 0x0B });
        Require(issuerSet >= 0, "issuer SET fixture");
        malformedIssuer[issuerSet] = 0x30;
        ExpectParseStatus("malformed-issuer-name", malformedIssuer,
                          ManagedX509ValidationStatus.MalformedDer);
        byte[] malformedValidity = Clone(ManagedX509Phase30Fixtures.Root);
        int validityTag = Find(malformedValidity,
            new byte[] { 0x30, 0x1E, 0x17, 0x0D });
        Require(validityTag >= 0, "validity tag fixture");
        malformedValidity[validityTag + 2] = 0x16;
        ExpectParseStatus("malformed-validity-tag", malformedValidity,
                          ManagedX509ValidationStatus.MalformedDer);
        byte[] truncatedVersionWrapper = Clone(ManagedX509Phase30Fixtures.Root);
        int versionWrapper = Find(truncatedVersionWrapper,
            new byte[] { 0xA0, 0x03, 0x02, 0x01, 0x02 });
        Require(versionWrapper >= 0, "version wrapper fixture");
        truncatedVersionWrapper[versionWrapper + 1] = 0x02;
        ExpectParseStatus("truncated-context-wrapper", truncatedVersionWrapper,
                          ManagedX509ValidationStatus.MalformedDer);
        byte[] truncatedKeyUsage = Clone(ManagedX509Phase30Fixtures.Root);
        int keyUsageValue = Find(truncatedKeyUsage,
            new byte[] { 0x03, 0x02, 0x01, 0x06 });
        Require(keyUsageValue >= 0, "key usage bit string fixture");
        truncatedKeyUsage[keyUsageValue + 1] = 0x01;
        ExpectParseStatus("truncated-key-usage-bit-string", truncatedKeyUsage,
                          ManagedX509ValidationStatus.MalformedDer);
        byte[] oversizedRoot = Clone(ManagedX509Phase30Fixtures.Root);
        oversizedRoot[2] = 0xFF;
        ExpectParseStatus("oversized-root-length", oversizedRoot,
                          ManagedX509ValidationStatus.MalformedDer);
        byte[] zeroSerial = Clone(ManagedX509Phase30Fixtures.Root);
        int serial = Find(zeroSerial, new byte[] { 0x02, 0x01, 0x01 });
        Require(serial >= 0, "serial fixture");
        zeroSerial[serial + 2] = 0;
        ExpectParseStatus("zero-serial-rejected", zeroSerial,
                          ManagedX509ValidationStatus.MalformedDer);

        ExpectParseStatus("trailing-garbage", Append(
            ManagedX509Phase30Fixtures.Root, 0x00),
            ManagedX509ValidationStatus.MalformedDer);
        byte[] truncated = ManagedX509Phase30Fixtures.Root.AsSpan(
            0, ManagedX509Phase30Fixtures.Root.Length - 1).ToArray();
        ExpectParseStatus("truncated-certificate", truncated,
                          ManagedX509ValidationStatus.MalformedDer);
        byte[] tbsMutation = Clone(ManagedX509Phase30Fixtures.DirectLeaf);
        tbsMutation[15] ^= 1;
        CaseStatus("tbs-mutation-fails-signature",
            ManagedX509ValidationStatus.BadSignature,
            Validate(tbsMutation, Array.Empty<byte>(), Array.Empty<byte>(),
                     ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));

        CaseStatus("valid-after-failure-recovery",
            ManagedX509ValidationStatus.Success,
            Validate(ManagedX509Phase30Fixtures.Leaf,
                     ManagedX509Phase30Fixtures.Intermediate,
                     Array.Empty<byte>(), ManagedX509Phase30Fixtures.Root,
                     ManagedX509Phase30Fixtures.Root, TestTime,
                     "www.example.com"u8));
    }

    private static void RunGcSurvivalTest()
    {
        ManagedX509Certificate parsed = Parse(
            ManagedX509Phase30Fixtures.Leaf, "gc-leaf");
        GC.Collect();
        Case("parser-result-survives-gc",
             parsed.PublicKeyLength == ManagedP256.PublicKeySize &&
             ManagedX509.TryMatchHostname(ManagedX509Phase30Fixtures.Leaf,
                                          in parsed, "www.example.com"u8));
    }

    private static ManagedX509Certificate Parse(byte[] bytes, string label)
    {
        ManagedX509ValidationStatus status = ManagedX509.TryParseCertificate(
            bytes, out ManagedX509Certificate parsed, out _);
        Require(status == ManagedX509ValidationStatus.Success,
                label + " parse status=" + status);
        return parsed;
    }

    private static bool Verify(byte[] childBytes, in ManagedX509Certificate child,
                               byte[] issuerBytes, in ManagedX509Certificate issuer)
    {
        return ManagedX509.TryValidateCertificateSignature(
            childBytes, in child,
            issuerBytes.AsSpan(issuer.PublicKeyOffset, issuer.PublicKeyLength),
            out ManagedX509ValidationStatus status) &&
            status == ManagedX509ValidationStatus.Success;
    }

    private static ManagedX509ValidationStatus Validate(
        ReadOnlySpan<byte> leaf, ReadOnlySpan<byte> intermediate1,
        ReadOnlySpan<byte> intermediate2, ReadOnlySpan<byte> candidateRoot,
        ReadOnlySpan<byte> trustedRoot, ManagedX509UtcTime current,
        ReadOnlySpan<byte> hostname)
    {
        ManagedX509.TryValidateServerChain(
            leaf, intermediate1, intermediate2, candidateRoot, trustedRoot,
            in current, hostname, out ManagedX509ValidationStatus status);
        return status;
    }

    private static bool ReadContainer(ReadOnlySpan<byte> bytes)
    {
        ManagedDerReader root = new(bytes);
        if (!root.TryEnter(0x30, out ManagedDerReader child,
                           out int offset, out int length) ||
            offset != 0 || length != bytes.Length || !root.AtEnd)
            return false;
        while (child.HasData)
        {
            if (!child.TryReadAny(out _, out _, out _, out _, out _))
                return false;
        }
        return offset == 0 && length == bytes.Length && root.AtEnd;
    }

    private static bool ReadNestedDepth(ReadOnlySpan<byte> bytes)
    {
        ManagedDerReader reader = new(bytes);
        for (int index = 0; index != 13; ++index)
        {
            if (!reader.TryEnter(0x30, out ManagedDerReader child,
                                 out _, out _)) return false;
            reader = child;
        }
        return reader.AtEnd;
    }

    private static byte[] WithUtcTime(byte[] source, bool notBefore,
                                      ReadOnlySpan<byte> replacement)
    {
        byte[] result = Clone(source);
        int validity = Find(result, new byte[] { 0x30, 0x1E, 0x17, 0x0D });
        Require(validity >= 0, "validity fixture");
        int offset = validity + (notBefore ? 4 : 19);
        Require(replacement.Length == 13, "time replacement length");
        replacement.CopyTo(result.AsSpan(offset));
        return result;
    }

    private static byte[] MutateOid(byte[] source, byte[] encodedOid,
                                    bool lastOccurrence)
    {
        byte[] result = Clone(source);
        int offset = lastOccurrence ? FindLast(result, encodedOid) :
                                      Find(result, encodedOid);
        Require(offset >= 0, "OID fixture");
        result[offset + encodedOid.Length - 1] ^= 1;
        return result;
    }

    private static byte[] MutateSanOid(byte[] source)
    {
        byte[] result = Clone(source);
        int offset = Find(result, new byte[] { 0x06, 0x03, 0x55, 0x1D, 0x11 });
        Require(offset >= 0, "SAN OID fixture");
        result[offset + 4] = 0x12;
        return result;
    }

    private static void ExpectParseStatus(string name, byte[] bytes,
                                           ManagedX509ValidationStatus expected)
    {
        ManagedX509ValidationStatus actual = ManagedX509.TryParseCertificate(
            bytes, out _, out _);
        CaseStatus(name, expected, actual);
    }

    private static byte[] Append(byte[] source, byte value)
    {
        byte[] result = new byte[source.Length + 1];
        source.CopyTo(result, 0);
        result[^1] = value;
        return result;
    }

    private static byte[] Clone(byte[] source) => (byte[])source.Clone();

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

    private static int FindLast(ReadOnlySpan<byte> haystack,
                                ReadOnlySpan<byte> needle)
    {
        int found = -1;
        int start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            int current = Find(haystack[start..], needle);
            if (current < 0) break;
            found = start + current;
            start = found + 1;
        }
        return found;
    }

    private static void Case(string name, bool passed)
    {
        ++s_cases;
        if (!passed) throw new InvalidOperationException("failed: " + name);
    }

    private static void CaseStatus(string name,
                                   ManagedX509ValidationStatus expected,
                                   ManagedX509ValidationStatus actual)
    {
        Case(name + " expected=" + expected + " actual=" + actual,
             expected == actual);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class ManagedX509KernelProof
{
    private static int s_run;

    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase30")]
    internal static uint Run()
    {
        if (!ManagedKernelContract.IsStarted || s_run != 0 ||
            !ManagedKernelContract.DeviceResourcesInstalled ||
            !ManagedKernelContract.DmaServicesInstalled ||
            !ManagedKernelContract.EntropyServicesInstalled)
            return ManagedKernelContract.InvalidState;

        if (!RunProof() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE30_PASS\r\n"u8))
            return ManagedKernelContract.InvalidState;
        s_run = 1;
        return ManagedKernelContract.ManagedOk;
    }

    private static bool RunProof()
    {
        if (!RunDerReaderSelfTest() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_DER_READER_SELF_TEST_PASS\r\n"u8))
            return false;

        if (!ManagedX509.TryParseCertificate(
                ManagedX509Phase30Fixtures.Root,
                out ManagedX509Certificate root) ||
            !ManagedX509.TryParseCertificate(
                ManagedX509Phase30Fixtures.Intermediate,
                out ManagedX509Certificate intermediate) ||
            !ManagedX509.TryParseCertificate(
                ManagedX509Phase30Fixtures.Leaf,
                out ManagedX509Certificate leaf) ||
            !ManagedX509.TryParseCertificate(
                ManagedX509Phase30Fixtures.DirectLeaf,
                out ManagedX509Certificate directLeaf) ||
            !ManagedX509.TryParseCertificate(
                ManagedX509Phase30Fixtures.SanOnlyLeaf,
                out ManagedX509Certificate sanOnly))
            return false;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_X509_CERTIFICATE_PARSE_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_SPKI_P256_EXTRACTION_PASS\r\n"u8))
            return false;

        Span<byte> digest = stackalloc byte[ManagedP256.DigestSize];
        try
        {
            if (!ManagedSha256.TryHash(
                    ManagedX509Phase30Fixtures.Leaf.AsSpan(
                        leaf.TbsOffset, leaf.TbsLength), digest) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_X509_EXACT_TBS_SHA256_PASS\r\n"u8) ||
                !ManagedX509.TryValidateCertificateSignature(
                    ManagedX509Phase30Fixtures.Leaf, in leaf,
                    ManagedX509Phase30Fixtures.Intermediate.AsSpan(
                        intermediate.PublicKeyOffset,
                        intermediate.PublicKeyLength), out _))
                return false;
        }
        finally
        {
            digest.Clear();
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_X509_ECDSA_CERT_SIGNATURE_PASS\r\n"u8))
            return false;

        ManagedX509UtcTime validTime = new(2028, 1, 1, 0, 0, 0);
        if (!ManagedX509.TryValidateServerChain(
                ManagedX509Phase30Fixtures.DirectLeaf,
                ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty,
                ManagedX509Phase30Fixtures.Root,
                ManagedX509Phase30Fixtures.Root, in validTime,
                "www.example.com"u8, out _) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_CHAIN_A_PASS\r\n"u8))
            return false;
        if (!ManagedX509.TryValidateServerChain(
                ManagedX509Phase30Fixtures.Leaf,
                ManagedX509Phase30Fixtures.Intermediate,
                ReadOnlySpan<byte>.Empty, ManagedX509Phase30Fixtures.Root,
                ManagedX509Phase30Fixtures.Root, in validTime,
                "www.example.com"u8, out _) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_CHAIN_B_PASS\r\n"u8))
            return false;

        ManagedX509UtcTime afterLeaf = new(2040, 1, 1, 0, 0, 0);
        if (ManagedX509.TryValidateServerChain(
                ManagedX509Phase30Fixtures.DirectLeaf,
                ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty,
                ManagedX509Phase30Fixtures.Root,
                ManagedX509Phase30Fixtures.Root, in afterLeaf,
                "www.example.com"u8, out ManagedX509ValidationStatus expired) ||
            expired != ManagedX509ValidationStatus.Expired ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_EXPIRATION_REJECTION_PASS\r\n"u8))
            return false;

        Span<byte> corruptedLeaf = stackalloc byte[
            ManagedX509Phase30Fixtures.DirectLeaf.Length];
        try
        {
            ManagedX509Phase30Fixtures.DirectLeaf.CopyTo(corruptedLeaf);
            corruptedLeaf[^1] ^= 1;
            if (ManagedX509.TryValidateServerChain(
                    corruptedLeaf, ReadOnlySpan<byte>.Empty,
                    ReadOnlySpan<byte>.Empty, ManagedX509Phase30Fixtures.Root,
                    ManagedX509Phase30Fixtures.Root, in validTime,
                    "www.example.com"u8,
                    out ManagedX509ValidationStatus corruptedStatus) ||
                corruptedStatus != ManagedX509ValidationStatus.BadSignature ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_X509_CORRUPTED_SIGNATURE_REJECTION_PASS\r\n"u8))
                return false;
        }
        finally
        {
            corruptedLeaf.Clear();
        }

        Span<byte> unknownCritical = stackalloc byte[
            ManagedX509Phase30Fixtures.Leaf.Length];
        try
        {
            ManagedX509Phase30Fixtures.Leaf.CopyTo(unknownCritical);
            int sanOid = Find(unknownCritical,
                stackalloc byte[] { 0x06, 0x03, 0x55, 0x1D, 0x11 });
            if (sanOid < 0) return false;
            unknownCritical[sanOid + 4] = 0x12;
            if (ManagedX509.TryValidateServerChain(
                    unknownCritical, ManagedX509Phase30Fixtures.Intermediate,
                    ReadOnlySpan<byte>.Empty, ManagedX509Phase30Fixtures.Root,
                    ManagedX509Phase30Fixtures.Root, in validTime,
                    "www.example.com"u8,
                    out ManagedX509ValidationStatus criticalStatus) ||
                criticalStatus != ManagedX509ValidationStatus.UnknownCriticalExtension ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_X509_UNKNOWN_CRITICAL_REJECTION_PASS\r\n"u8))
                return false;
        }
        finally
        {
            unknownCritical.Clear();
        }

        if (!ManagedX509.TryMatchHostname(
                ManagedX509Phase30Fixtures.Leaf, in leaf,
                "www.example.com"u8) ||
            ManagedX509.TryMatchHostname(
                ManagedX509Phase30Fixtures.Leaf, in leaf,
                "other.example.net"u8) ||
            !ManagedX509.TryMatchHostname(
                ManagedX509Phase30Fixtures.Leaf, in leaf,
                "api.example.com"u8) ||
            ManagedX509.TryMatchHostname(
                ManagedX509Phase30Fixtures.Leaf, in leaf,
                "a.b.example.com"u8) ||
            ManagedX509.TryMatchHostname(
                ManagedX509Phase30Fixtures.SanOnlyLeaf, in sanOnly,
                "cn-only.example.com"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_HOSTNAME_RULES_PASS\r\n"u8))
            return false;

        if (ManagedX509.TryValidateServerChain(
                ManagedX509Phase30Fixtures.DirectLeaf,
                ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty,
                ManagedX509Phase30Fixtures.Root,
                ManagedX509Phase30Fixtures.Intermediate, in validTime,
                "www.example.com"u8, out ManagedX509ValidationStatus trustStatus) ||
            trustStatus != ManagedX509ValidationStatus.UntrustedRoot ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_UNTRUSTED_ROOT_REJECTION_PASS\r\n"u8))
            return false;

        if (!ManagedX509.TryValidateCertificateSignature(
                ManagedX509Phase30Fixtures.Root, in root,
                ManagedX509Phase30Fixtures.Root.AsSpan(
                    root.PublicKeyOffset, root.PublicKeyLength), out _) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_PHASE29_ECDSA_REGRESSION_PASS\r\n"u8) ||
            !RunPhase28Regression() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_PHASE28_ECDH_REGRESSION_PASS\r\n"u8) ||
            !RunPhase27Regression() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_PHASE27_AES_GCM_REGRESSION_PASS\r\n"u8) ||
            !ManagedKernelContract.EntropyServicesInstalled ||
            !ManagedKernelContract.TryEnsureEntropyService() ||
            ManagedKernelContract.SecureRandom == null ||
            !KernelLog.Write("GXOS_NET10:MANAGED_X509_PHASE26_ENTROPY_REGRESSION_PASS\r\n"u8))
            return false;

        return ManagedX509.TryValidateServerChain(
                   ManagedX509Phase30Fixtures.Leaf,
                   ManagedX509Phase30Fixtures.Intermediate,
                   ReadOnlySpan<byte>.Empty, ManagedX509Phase30Fixtures.Root,
                   ManagedX509Phase30Fixtures.Root, in validTime,
                   "www.example.com"u8, out _) &&
               KernelLog.Write("GXOS_NET10:MANAGED_X509_POST_FAILURE_RECOVERY_PASS\r\n"u8);
    }

    private static bool RunDerReaderSelfTest()
    {
        Span<byte> bytes = stackalloc byte[] { 0x30, 0x03, 0x02, 0x01, 0x01 };
        ManagedDerReader reader = new(bytes);
        if (!reader.TryEnter(0x30, out ManagedDerReader body,
                             out int offset, out int length) ||
            offset != 0 || length != bytes.Length || !reader.AtEnd ||
            !body.TryRead(0x02, out _, out _, out _, out _) || !body.AtEnd)
            return false;
        return true;
    }

    private static bool RunPhase28Regression()
    {
        Span<byte> privateKey = stackalloc byte[]
        {
            0xC8, 0x8F, 0x01, 0xF5, 0x10, 0xD9, 0xAC, 0x3F,
            0x70, 0xA2, 0x92, 0xDA, 0xA2, 0x31, 0x6D, 0xE5,
            0x44, 0xE9, 0xAA, 0xB8, 0xAF, 0xE8, 0x40, 0x49,
            0xC6, 0x2A, 0x9C, 0x57, 0x86, 0x2D, 0x14, 0x33
        };
        Span<byte> publicKey = stackalloc byte[]
        {
            0x04, 0xD1, 0x2D, 0xFB, 0x52, 0x89, 0xC8, 0xD4, 0xF8,
            0x12, 0x08, 0xB7, 0x02, 0x70, 0x39, 0x8C, 0x34, 0x22,
            0x96, 0x97, 0x0A, 0x0B, 0xCC, 0xB7, 0x4C, 0x73, 0x6F,
            0xC7, 0x55, 0x44, 0x94, 0xBF, 0x63, 0x56, 0xFB, 0xF3,
            0xCA, 0x36, 0x6C, 0xC2, 0x3E, 0x81, 0x57, 0x85, 0x4C,
            0x13, 0xC5, 0x8D, 0x6A, 0xAC, 0x23, 0xF0, 0x46, 0xAD,
            0xA3, 0x0F, 0x83, 0x53, 0xE7, 0x4F, 0x33, 0x03, 0x98,
            0x72, 0xAB
        };
        Span<byte> expected = stackalloc byte[]
        {
            0xD6, 0x84, 0x0F, 0x6B, 0x42, 0xF6, 0xED, 0xAF,
            0xD1, 0x31, 0x16, 0xE0, 0xE1, 0x25, 0x65, 0x20,
            0x2F, 0xEF, 0x8E, 0x9E, 0xCE, 0x7D, 0xCE, 0x03,
            0x81, 0x24, 0x64, 0xD0, 0x4B, 0x94, 0x42, 0xDE
        };
        Span<byte> actual = stackalloc byte[ManagedP256.SharedSecretSize];
        try
        {
            return ManagedP256.TryDeriveSharedSecret(
                       privateKey, publicKey, actual) &&
                   ManagedCryptoComparison.FixedTimeEquals(actual, expected);
        }
        finally
        {
            privateKey.Clear();
            publicKey.Clear();
            expected.Clear();
            actual.Clear();
        }
    }

    private static bool RunPhase27Regression()
    {
        Span<byte> key = stackalloc byte[ManagedAesGcm.KeySize];
        Span<byte> nonce = stackalloc byte[ManagedAesGcm.NonceSize];
        Span<byte> tag = stackalloc byte[ManagedAesGcm.TagSize];
        Span<byte> expected = stackalloc byte[]
        {
            0x58, 0xE2, 0xFC, 0xCE, 0xFA, 0x7E, 0x30, 0x61,
            0x36, 0x7F, 0x1D, 0x57, 0xA4, 0xE7, 0x45, 0x5A
        };
        try
        {
            return ManagedAesGcm.TryEncrypt(key, nonce,
                       ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty,
                       Span<byte>.Empty, tag) &&
                   ManagedCryptoComparison.FixedTimeEquals(tag, expected);
        }
        finally
        {
            key.Clear();
            nonce.Clear();
            tag.Clear();
            expected.Clear();
        }
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
}

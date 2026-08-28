using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class ManagedP256EcdsaKernelProof
{
    private static int s_run;

    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase29")]
    internal static uint Run()
    {
        if (!ManagedKernelContract.IsStarted || s_run != 0 ||
            !ManagedKernelContract.DeviceResourcesInstalled ||
            !ManagedKernelContract.DmaServicesInstalled ||
            !ManagedKernelContract.EntropyServicesInstalled)
        {
            return ManagedKernelContract.InvalidState;
        }

        if (!RunProof() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE29_PASS\r\n"u8))
        {
            return ManagedKernelContract.InvalidState;
        }
        s_run = 1;
        return ManagedKernelContract.ManagedOk;
    }

    private static bool RunProof()
    {
        Span<byte> publicKey = stackalloc byte[ManagedP256.PublicKeySize];
        Span<byte> secondPublicKey = stackalloc byte[ManagedP256.PublicKeySize];
        Span<byte> digest = stackalloc byte[ManagedP256.DigestSize];
        Span<byte> secondDigest = stackalloc byte[ManagedP256.DigestSize];
        Span<byte> r = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> s = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> secondR = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> secondS = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> der = stackalloc byte[]
        {
            0x30, 0x46,
            0x02, 0x21, 0x00,
            0xCB, 0x28, 0xE0, 0x99, 0x9B, 0x9C, 0x77, 0x15,
            0xFD, 0x0A, 0x80, 0xD8, 0xE4, 0x7A, 0x77, 0x07,
            0x97, 0x16, 0xCB, 0xBF, 0x91, 0x7D, 0xD7, 0x2E,
            0x97, 0x56, 0x6E, 0xA1, 0xC0, 0x66, 0x95, 0x7C,
            0x02, 0x21, 0x00,
            0x86, 0xFA, 0x3B, 0xB4, 0xE2, 0x6C, 0xAD, 0x5B,
            0xF9, 0x0B, 0x7F, 0x81, 0x89, 0x92, 0x56, 0xCE,
            0x75, 0x94, 0xBB, 0x1E, 0xA0, 0xC8, 0x92, 0x12,
            0x74, 0x8B, 0xFF, 0x3B, 0x3D, 0x5B, 0x03, 0x15
        };
        Span<byte> parsedR = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> parsedS = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> invalidPublic = stackalloc byte[ManagedP256.PublicKeySize];
        Span<byte> invalidDigest = stackalloc byte[ManagedP256.DigestSize];
        Span<byte> invalidSignature = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> zero = stackalloc byte[ManagedP256.SignatureScalarSize];
        Span<byte> order = stackalloc byte[]
        {
            0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xBC, 0xE6, 0xFA, 0xAD, 0xA7, 0x17, 0x9E, 0x84,
            0xF3, 0xB9, 0xCA, 0xC2, 0xFC, 0x63, 0x25, 0x51
        };
        Span<byte> infinity = stackalloc byte[] { 0x00 };
        Span<byte> malformedDer = stackalloc byte[72];
        try
        {
            WritePublicKey(publicKey,
                stackalloc byte[]
                {
                    0x24, 0x42, 0xA5, 0xCC, 0x0E, 0xCD, 0x01, 0x5F,
                    0xA3, 0xCA, 0x31, 0xDC, 0x8E, 0x2B, 0xBC, 0x70,
                    0xBF, 0x42, 0xD6, 0x0C, 0xBC, 0xA2, 0x00, 0x85,
                    0xE0, 0x82, 0x2C, 0xB0, 0x42, 0x35, 0xE9, 0x70
                },
                stackalloc byte[]
                {
                    0x6F, 0xC9, 0x8B, 0xD7, 0xE5, 0x02, 0x11, 0xA4,
                    0xA2, 0x71, 0x02, 0xFA, 0x35, 0x49, 0xDF, 0x79,
                    0xEB, 0xCB, 0x4B, 0xF2, 0x46, 0xB8, 0x09, 0x45,
                    0xCD, 0xDF, 0xE7, 0xD5, 0x09, 0xBB, 0xFD, 0x7D
                });
            WritePublicKey(secondPublicKey,
                stackalloc byte[]
                {
                    0x60, 0xFE, 0xD4, 0xBA, 0x25, 0x5A, 0x9D, 0x31,
                    0xC9, 0x61, 0xEB, 0x74, 0xC6, 0x35, 0x6D, 0x68,
                    0xC0, 0x49, 0xB8, 0x92, 0x3B, 0x61, 0xFA, 0x6C,
                    0xE6, 0x69, 0x62, 0x2E, 0x60, 0xF2, 0x9F, 0xB6
                },
                stackalloc byte[]
                {
                    0x79, 0x03, 0xFE, 0x10, 0x08, 0xB8, 0xBC, 0x99,
                    0xA4, 0x1A, 0xE9, 0xE9, 0x56, 0x28, 0xBC, 0x64,
                    0xF2, 0xF1, 0xB2, 0x0C, 0x2D, 0x7E, 0x9F, 0x51,
                    0x77, 0xA3, 0xC2, 0x94, 0xD4, 0x46, 0x22, 0x99
                });
            CopyBytes(digest, stackalloc byte[]
            {
                0xBA, 0x78, 0x16, 0xBF, 0x8F, 0x01, 0xCF, 0xEA,
                0x41, 0x41, 0x40, 0xDE, 0x5D, 0xAE, 0x22, 0x23,
                0xB0, 0x03, 0x61, 0xA3, 0x96, 0x17, 0x7A, 0x9C,
                0xB4, 0x10, 0xFF, 0x61, 0xF2, 0x00, 0x15, 0xAD
            });
            CopyBytes(secondDigest, stackalloc byte[]
            {
                0xAF, 0x2B, 0xDB, 0xE1, 0xAA, 0x9B, 0x6E, 0xC1,
                0xE2, 0xAD, 0xE1, 0xD6, 0x94, 0xF4, 0x1F, 0xC7,
                0x1A, 0x83, 0x1D, 0x02, 0x68, 0xE9, 0x89, 0x15,
                0x62, 0x11, 0x3D, 0x8A, 0x62, 0xAD, 0xD1, 0xBF
            });
            CopyBytes(r, stackalloc byte[]
            {
                0xCB, 0x28, 0xE0, 0x99, 0x9B, 0x9C, 0x77, 0x15,
                0xFD, 0x0A, 0x80, 0xD8, 0xE4, 0x7A, 0x77, 0x07,
                0x97, 0x16, 0xCB, 0xBF, 0x91, 0x7D, 0xD7, 0x2E,
                0x97, 0x56, 0x6E, 0xA1, 0xC0, 0x66, 0x95, 0x7C
            });
            CopyBytes(s, stackalloc byte[]
            {
                0x86, 0xFA, 0x3B, 0xB4, 0xE2, 0x6C, 0xAD, 0x5B,
                0xF9, 0x0B, 0x7F, 0x81, 0x89, 0x92, 0x56, 0xCE,
                0x75, 0x94, 0xBB, 0x1E, 0xA0, 0xC8, 0x92, 0x12,
                0x74, 0x8B, 0xFF, 0x3B, 0x3D, 0x5B, 0x03, 0x15
            });
            CopyBytes(secondR, stackalloc byte[]
            {
                0xEF, 0xD4, 0x8B, 0x2A, 0xAC, 0xB6, 0xA8, 0xFD,
                0x11, 0x40, 0xDD, 0x9C, 0xD4, 0x5E, 0x81, 0xD6,
                0x9D, 0x2C, 0x87, 0x7B, 0x56, 0xAA, 0xF9, 0x91,
                0xC3, 0x4D, 0x0E, 0xA8, 0x4E, 0xAF, 0x37, 0x16
            });
            CopyBytes(secondS, stackalloc byte[]
            {
                0xF7, 0xCB, 0x1C, 0x94, 0x2D, 0x65, 0x7C, 0x41,
                0xD4, 0x36, 0xC7, 0xA1, 0xB6, 0xE2, 0x9F, 0x65,
                0xF3, 0xE9, 0x00, 0xDB, 0xB9, 0xAF, 0xF4, 0x06,
                0x4D, 0xC4, 0xAB, 0x2F, 0x84, 0x3A, 0xCD, 0xA8
            });

            if (!RunScalarArithmeticProof() ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_SCALAR_N_SELF_TEST_PASS\r\n"u8))
                return false;

            if (!ManagedP256.TryValidatePublicKey(publicKey) ||
                !ManagedP256.TryValidatePublicKey(secondPublicKey) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_PUBLIC_KEY_VALIDATION_PASS\r\n"u8))
                return false;

            if (!ManagedP256.TryVerifyDigest(digest, publicKey, r, s) ||
                !ManagedP256.TryVerifyDigest(secondDigest, secondPublicKey,
                                             secondR, secondS) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_RAW_KAT_PASS\r\n"u8))
                return false;

            digest.CopyTo(invalidDigest);
            invalidDigest[0] ^= 1;
            if (ManagedP256.TryVerifyDigest(invalidDigest, publicKey, r, s) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_MODIFIED_DIGEST_REJECTION_PASS\r\n"u8))
                return false;

            r.CopyTo(invalidSignature);
            invalidSignature[16] ^= 1;
            if (ManagedP256.TryVerifyDigest(digest, publicKey,
                                            invalidSignature, s) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_MODIFIED_SIGNATURE_REJECTION_PASS\r\n"u8))
                return false;

            publicKey.CopyTo(invalidPublic);
            invalidPublic[1] ^= 1;
            if (ManagedP256.TryVerifyDigest(digest, invalidPublic, r, s) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_WRONG_PUBLIC_KEY_REJECTION_PASS\r\n"u8))
                return false;

            if (!ManagedP256.TryParseDerSignature(der, parsedR, parsedS) ||
                !ManagedCryptoComparison.FixedTimeEquals(parsedR, r) ||
                !ManagedCryptoComparison.FixedTimeEquals(parsedS, s) ||
                !ManagedP256.TryVerifyDerSignature(digest, publicKey, der) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_DER_KAT_PASS\r\n"u8))
                return false;

            der.CopyTo(malformedDer);
            malformedDer[0] = 0x31;
            if (ManagedP256.TryParseDerSignature(malformedDer, parsedR, parsedS) ||
                ManagedP256.TryVerifyDerSignature(digest, publicKey,
                                                   malformedDer) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_MALFORMED_DER_REJECTION_PASS\r\n"u8))
                return false;

            if (ManagedP256.TryVerifyDigest(digest, publicKey, zero, s) ||
                ManagedP256.TryVerifyDigest(digest, publicKey, r, zero) ||
                ManagedP256.TryVerifyDigest(digest, publicKey, order, s) ||
                ManagedP256.TryVerifyDigest(digest, publicKey, r, order) ||
                ManagedP256.TryVerifyDigest(digest, infinity, r, s) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_SCALAR_REJECTION_PASS\r\n"u8))
                return false;

            if (!ManagedP256.TryVerifyDigest(digest, publicKey, r, s) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_POST_FAILURE_RECOVERY_PASS\r\n"u8))
                return false;

            if (!ManagedP256KernelProof.RunForPhase29() ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDSA_PHASE28_REGRESSION_PASS\r\n"u8))
                return false;
            return true;
        }
        finally
        {
            publicKey.Clear();
            secondPublicKey.Clear();
            digest.Clear();
            secondDigest.Clear();
            r.Clear();
            s.Clear();
            secondR.Clear();
            secondS.Clear();
            der.Clear();
            parsedR.Clear();
            parsedS.Clear();
            invalidPublic.Clear();
            invalidDigest.Clear();
            invalidSignature.Clear();
            zero.Clear();
            order.Clear();
            infinity.Clear();
            malformedDer.Clear();
        }
    }

    private static bool RunScalarArithmeticProof()
    {
        ManagedP256ScalarElement one = ManagedP256ScalarElement.One;
        ManagedP256ScalarElement zero = ManagedP256ScalarElement.Zero;
        ManagedP256ScalarElement last = ManagedP256ScalarElement.OrderMinusOne;
        ManagedP256ScalarElement two =
            ManagedP256ScalarElement.Add(one, one);
        ManagedP256ScalarElement inverse =
            ManagedP256ScalarElement.Invert(two);
        Span<byte> digest = stackalloc byte[ManagedP256.DigestSize];
        digest[31] = 1;
        return ManagedP256ScalarElement.Equals(
                   ManagedP256ScalarElement.Add(last, one), zero) &&
               ManagedP256ScalarElement.Equals(
                   ManagedP256ScalarElement.Subtract(zero, one), last) &&
               ManagedP256ScalarElement.Equals(
                   ManagedP256ScalarElement.Multiply(last, last), one) &&
               ManagedP256ScalarElement.Equals(
                   ManagedP256ScalarElement.Multiply(two, inverse), one) &&
               ManagedP256ScalarElement.TryReduceDigest(
                   digest, out ManagedP256ScalarElement digestValue) &&
               ManagedP256ScalarElement.Equals(digestValue, one);
    }

    private static void WritePublicKey(Span<byte> destination,
                                       ReadOnlySpan<byte> x,
                                       ReadOnlySpan<byte> y)
    {
        destination.Clear();
        destination[0] = 4;
        x.CopyTo(destination.Slice(1, 32));
        y.CopyTo(destination.Slice(33, 32));
    }

    private static void CopyBytes(Span<byte> destination,
                                  ReadOnlySpan<byte> source)
    {
        source.CopyTo(destination);
    }
}

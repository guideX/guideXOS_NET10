using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class ManagedP256KernelProof
{
    private static int s_run;

    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase28")]
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
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE28_PASS\r\n"u8))
        {
            return ManagedKernelContract.InvalidState;
        }
        s_run = 1;
        return ManagedKernelContract.ManagedOk;
    }

    private static bool RunProof()
    {
        Span<byte> privateA = stackalloc byte[]
        {
            0xC8, 0x8F, 0x01, 0xF5, 0x10, 0xD9, 0xAC, 0x3F,
            0x70, 0xA2, 0x92, 0xDA, 0xA2, 0x31, 0x6D, 0xE5,
            0x44, 0xE9, 0xAA, 0xB8, 0xAF, 0xE8, 0x40, 0x49,
            0xC6, 0x2A, 0x9C, 0x57, 0x86, 0x2D, 0x14, 0x33
        };
        Span<byte> privateB = stackalloc byte[]
        {
            0xC6, 0xEF, 0x9C, 0x5D, 0x78, 0xAE, 0x01, 0x2A,
            0x01, 0x11, 0x64, 0xAC, 0xB3, 0x97, 0xCE, 0x20,
            0x88, 0x68, 0x5D, 0x8F, 0x06, 0xBF, 0x9B, 0xE0,
            0xB2, 0x83, 0xAB, 0x46, 0x47, 0x6B, 0xEE, 0x53
        };
        Span<byte> publicA = stackalloc byte[]
        {
            0x04,
            0xDA, 0xD0, 0xB6, 0x53, 0x94, 0x22, 0x1C, 0xF9,
            0xB0, 0x51, 0xE1, 0xFE, 0xCA, 0x57, 0x87, 0xD0,
            0x98, 0xDF, 0xE6, 0x37, 0xFC, 0x90, 0xB9, 0xEF,
            0x94, 0x5D, 0x0C, 0x37, 0x72, 0x58, 0x11, 0x80,
            0x52, 0x71, 0xA0, 0x46, 0x1C, 0xDB, 0x82, 0x52,
            0xD6, 0x1F, 0x1C, 0x45, 0x6F, 0xA3, 0xE5, 0x9A,
            0xB1, 0xF4, 0x5B, 0x33, 0xAC, 0xCF, 0x5F, 0x58,
            0x38, 0x9E, 0x05, 0x77, 0xB8, 0x99, 0x0B, 0xB3
        };
        Span<byte> publicB = stackalloc byte[]
        {
            0x04,
            0xD1, 0x2D, 0xFB, 0x52, 0x89, 0xC8, 0xD4, 0xF8,
            0x12, 0x08, 0xB7, 0x02, 0x70, 0x39, 0x8C, 0x34,
            0x22, 0x96, 0x97, 0x0A, 0x0B, 0xCC, 0xB7, 0x4C,
            0x73, 0x6F, 0xC7, 0x55, 0x44, 0x94, 0xBF, 0x63,
            0x56, 0xFB, 0xF3, 0xCA, 0x36, 0x6C, 0xC2, 0x3E,
            0x81, 0x57, 0x85, 0x4C, 0x13, 0xC5, 0x8D, 0x6A,
            0xAC, 0x23, 0xF0, 0x46, 0xAD, 0xA3, 0x0F, 0x83,
            0x53, 0xE7, 0x4F, 0x33, 0x03, 0x98, 0x72, 0xAB
        };
        Span<byte> sharedExpected = stackalloc byte[]
        {
            0xD6, 0x84, 0x0F, 0x6B, 0x42, 0xF6, 0xED, 0xAF,
            0xD1, 0x31, 0x16, 0xE0, 0xE1, 0x25, 0x65, 0x20,
            0x2F, 0xEF, 0x8E, 0x9E, 0xCE, 0x7D, 0xCE, 0x03,
            0x81, 0x24, 0x64, 0xD0, 0x4B, 0x94, 0x42, 0xDE
        };
        Span<byte> derivedA = stackalloc byte[ManagedP256.PublicKeySize];
        Span<byte> derivedB = stackalloc byte[ManagedP256.PublicKeySize];
        Span<byte> sharedA = stackalloc byte[ManagedP256.SharedSecretSize];
        Span<byte> sharedB = stackalloc byte[ManagedP256.SharedSecretSize];
        Span<byte> failedOutput = stackalloc byte[ManagedP256.SharedSecretSize];
        Span<byte> invalidPublic = stackalloc byte[ManagedP256.PublicKeySize];
        try
        {
            ManagedP256FieldElement pMinusOne =
                ManagedP256FieldElement.PrimeMinusOne;
            if (!ManagedP256FieldElement.Equals(
                    ManagedP256FieldElement.Add(
                        pMinusOne, ManagedP256FieldElement.One),
                    ManagedP256FieldElement.Zero) ||
                !ManagedP256.IsOnCurveForTest(
                    ManagedP256FieldElement.GeneratorX,
                    ManagedP256FieldElement.GeneratorY) ||
                !ManagedP256FieldElement.Equals(
                    ManagedP256FieldElement.Multiply(
                        pMinusOne, pMinusOne),
                    ManagedP256FieldElement.One) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_FIELD_SELF_TEST_PASS\r\n"u8))
            {
                return false;
            }

            if (!ManagedP256.TryDerivePublicKey(privateA, derivedA) ||
                !ManagedP256.TryDerivePublicKey(privateB, derivedB) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    derivedA, publicA) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    derivedB, publicB) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_PRIVATE_PUBLIC_KAT_PASS\r\n"u8))
            {
                return false;
            }

            if (!ManagedP256.TryValidatePublicKey(publicA) ||
                !ManagedP256.TryValidatePublicKey(publicB) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_PUBLIC_KEY_VALIDATION_PASS\r\n"u8))
            {
                return false;
            }

            if (!ManagedP256.TryDeriveSharedSecret(
                    privateA, publicB, sharedA) ||
                !ManagedP256.TryDeriveSharedSecret(
                    privateB, publicA, sharedB) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    sharedA, sharedExpected) ||
                !ManagedCryptoComparison.FixedTimeEquals(
                    sharedB, sharedExpected) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ECDH_KAT_PASS\r\n"u8))
            {
                return false;
            }

            Span<byte> invalidScalar = stackalloc byte[ManagedP256.PrivateScalarSize];
            Span<byte> order = stackalloc byte[]
            {
                0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00,
                0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                0xBC, 0xE6, 0xFA, 0xAD, 0xA7, 0x17, 0x9E, 0x84,
                0xF3, 0xB9, 0xCA, 0xC2, 0xFC, 0x63, 0x25, 0x51
            };
            invalidScalar.Clear();
            if (ManagedP256.TryDerivePublicKey(
                    invalidScalar, derivedA) ||
                ManagedP256.TryDerivePublicKey(order, derivedA) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_INVALID_PRIVATE_REJECTION_PASS\r\n"u8))
            {
                invalidScalar.Clear();
                order.Clear();
                return false;
            }
            invalidScalar.Clear();
            order.Clear();

            publicA.CopyTo(invalidPublic);
            invalidPublic[12] ^= 1;
            failedOutput.Fill(0xA5);
            if (ManagedP256.TryValidatePublicKey(invalidPublic) ||
                ManagedP256.TryDeriveSharedSecret(
                    privateA, invalidPublic, failedOutput) ||
                !AllBytes(failedOutput, 0xA5) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_INVALID_PUBLIC_REJECTION_PASS\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_OUTPUT_UNCHANGED_ON_FAILURE_PASS\r\n"u8))
            {
                return false;
            }

            if (!RunEntropyKeyProof(publicPeer: publicB))
            {
                return false;
            }

            /* RunForPhase28 is the Phase 27 core proof.  It keeps the prior
               AES/GHASH/GCM and virtio-rng nonce markers in this combined
               authoritative boot without invoking the unmanaged export. */
            return ManagedAesGcmKernelProof.RunForPhase28();
        }
        finally
        {
            privateA.Clear();
            privateB.Clear();
            publicA.Clear();
            publicB.Clear();
            sharedExpected.Clear();
            derivedA.Clear();
            derivedB.Clear();
            sharedA.Clear();
            sharedB.Clear();
            failedOutput.Clear();
            invalidPublic.Clear();
        }
    }

    private static bool RunEntropyKeyProof(ReadOnlySpan<byte> publicPeer)
    {
        ManagedEntropyService? entropy = ManagedKernelContract.EntropyService;
        ManagedSecureRandom? random = ManagedKernelContract.SecureRandom;
        ManagedVirtioRngDriver? candidate = ManagedVirtioRngDriver.TryCreate();
        if (!candidate.HasValue) return false;
        if (entropy == null || random == null)
        {
            entropy = new ManagedEntropyService(
                ManagedKernelContract.EntropyFillAddress,
                ManagedKernelContract.EntropyCapabilities,
                ManagedKernelContract.EntropyMaxBytesPerFill);
            random = new ManagedSecureRandom(entropy);
        }

        ManagedVirtioRngDriver driver = candidate.Value;
        Span<byte> privateKey = stackalloc byte[ManagedP256.PrivateScalarSize];
        Span<byte> publicKey = stackalloc byte[ManagedP256.PublicKeySize];
        Span<byte> sharedSecret = stackalloc byte[ManagedP256.SharedSecretSize];
        try
        {
            if (!driver.TryStart()) return false;
            entropy.AttachVirtioRng(driver);
            if (!ManagedP256.TryGeneratePrivateKey(random, privateKey) ||
                entropy.LastProvider != ManagedEntropyProviderKind.VirtioRng ||
                !ManagedP256.IsValidScalarForTest(privateKey) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ENTROPY_PROVIDER=VIRTIO_RNG\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_ENTROPY_KEY_GENERATION_PASS\r\n"u8) ||
                !ManagedP256.TryDerivePublicKey(privateKey, publicKey) ||
                !ManagedP256.TryValidatePublicKey(publicKey) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_GENERATED_PUBLIC_VALIDATION_PASS\r\n"u8) ||
                !ManagedP256.TryDeriveSharedSecret(
                    privateKey, publicPeer, sharedSecret) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_P256_GENERATED_ECDH_PASS\r\n"u8))
            {
                return false;
            }
            return driver.TryStop() &&
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCountForDriver(
                    ManagedVirtioRngProtocol.DriverId) == 0;
        }
        finally
        {
            entropy.DetachVirtioRng(driver);
            driver.TryStop();
            privateKey.Clear();
            publicKey.Clear();
            sharedSecret.Clear();
        }
    }

    private static bool AllBytes(ReadOnlySpan<byte> value, byte expected)
    {
        for (int index = 0; index != value.Length; ++index)
        {
            if (value[index] != expected) return false;
        }
        return true;
    }
}

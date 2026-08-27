using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class ManagedAesGcmKernelProof
{
    private static int s_run;

    [MethodImpl(MethodImplOptions.NoInlining)]
    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase27")]
    internal static uint Run()
    {
        if (!ManagedKernelContract.IsStarted || s_run != 0 ||
            !ManagedKernelContract.DeviceResourcesInstalled ||
            !ManagedKernelContract.DmaServicesInstalled ||
            !ManagedKernelContract.EntropyServicesInstalled)
        {
            return ManagedKernelContract.InvalidState;
        }

        ManagedAes128 aes = new();
        Span<byte> aesKey = stackalloc byte[]
        {
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F
        };
        Span<byte> aesPlaintext = stackalloc byte[]
        {
            0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
            0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
        };
        Span<byte> aesCiphertext = stackalloc byte[]
        {
            0x69, 0xC4, 0xE0, 0xD8, 0x6A, 0x7B, 0x04, 0x30,
            0xD8, 0xCD, 0xB7, 0x80, 0x70, 0xB4, 0xC5, 0x5A
        };
        Span<byte> gcmKey = stackalloc byte[16];
        Span<byte> gcmNonce = stackalloc byte[12];
        Span<byte> gcmCiphertext = stackalloc byte[]
        {
            0x03, 0x88, 0xDA, 0xCE, 0x60, 0xB6, 0xA3, 0x92,
            0xF3, 0x28, 0xC2, 0xB9, 0x71, 0xB2, 0xFE, 0x78
        };
        Span<byte> gcmTag = stackalloc byte[]
        {
            0xAB, 0x6E, 0x47, 0xD4, 0x2C, 0xEC, 0x13, 0xBD,
            0xF5, 0x3A, 0x67, 0xB2, 0x12, 0x57, 0xBD, 0xDF
        };
        Span<byte> ghashSubkey = stackalloc byte[]
        {
            0xB8, 0x3B, 0x53, 0x37, 0x08, 0xBF, 0x53, 0x5D,
            0x0A, 0xA6, 0xE5, 0x29, 0x80, 0xD5, 0x3B, 0x78
        };
        Span<byte> ghashAad = stackalloc byte[]
        {
            0xFE, 0xED, 0xFA, 0xCE, 0xDE, 0xAD, 0xBE, 0xEF,
            0xFE, 0xED, 0xFA, 0xCE, 0xDE, 0xAD, 0xBE, 0xEF,
            0xAB, 0xAD, 0xDA, 0xD2
        };
        Span<byte> ghashCiphertext = stackalloc byte[]
        {
            0x42, 0x83, 0x1E, 0xC2, 0x21, 0x77, 0x74, 0x24,
            0x4B, 0x72, 0x21, 0xB7, 0x84, 0xD0, 0xD4, 0x9C,
            0xE3, 0xAA, 0x21, 0x2F, 0x2C, 0x02, 0xA4, 0xE0,
            0x35, 0xC1, 0x7E, 0x23, 0x29, 0xAC, 0xA1, 0x2E,
            0x21, 0xD5, 0x14, 0xB2, 0x54, 0x66, 0x93, 0x1C,
            0x7D, 0x8F, 0x6A, 0x5A, 0xAC, 0x84, 0xAA, 0x05,
            0x1B, 0xA3, 0x0B, 0x39, 0x6A, 0x0A, 0xAC, 0x97,
            0x3D, 0x58, 0xE0, 0x91
        };
        Span<byte> ghashExpected = stackalloc byte[]
        {
            0x69, 0x8E, 0x57, 0xF7, 0x0E, 0x6E, 0xCC, 0x7F,
            0xD9, 0x46, 0x3B, 0x72, 0x60, 0xA9, 0xAE, 0x5F
        };
        Span<byte> block = stackalloc byte[16];
        Span<byte> ghash = stackalloc byte[16];
        Span<byte> ciphertext = stackalloc byte[16];
        Span<byte> tag = stackalloc byte[16];
        Span<byte> plaintext = stackalloc byte[16];
        Span<byte> zeroPlaintext = stackalloc byte[16];
        Span<byte> failedPlaintext = stackalloc byte[16];
        try
        {
            if (!aes.TrySetKey(aesKey) ||
                !aes.TryEncryptBlock(aesPlaintext, block) ||
                !ManagedCryptoComparison.FixedTimeEquals(block, aesCiphertext) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_AES128_KAT_PASS\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }

            if (!ManagedGhash.TryCompute(ghashSubkey, ghashAad,
                                         ghashCiphertext, ghash) ||
                !ManagedCryptoComparison.FixedTimeEquals(ghash, ghashExpected) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_GHASH_KAT_PASS\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }

            if (!ManagedAesGcm.TryEncrypt(gcmKey, gcmNonce,
                                          ReadOnlySpan<byte>.Empty,
                                          new byte[16], ciphertext, tag) ||
                !ManagedCryptoComparison.FixedTimeEquals(ciphertext, gcmCiphertext) ||
                !ManagedCryptoComparison.FixedTimeEquals(tag, gcmTag) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_AES_GCM_ENCRYPT_KAT_PASS\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }

            if (!ManagedAesGcm.TryDecrypt(gcmKey, gcmNonce,
                                          ReadOnlySpan<byte>.Empty,
                                          gcmCiphertext, gcmTag, plaintext) ||
                !ManagedCryptoComparison.FixedTimeEquals(plaintext,
                                                         zeroPlaintext) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_AES_GCM_DECRYPT_KAT_PASS\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }

            failedPlaintext.Fill(0xA5);
            Span<byte> invalidTag = stackalloc byte[16];
            gcmTag.CopyTo(invalidTag);
            invalidTag[0] ^= 1;
            if (ManagedAesGcm.TryDecrypt(gcmKey, gcmNonce,
                                         ReadOnlySpan<byte>.Empty,
                                         gcmCiphertext, invalidTag,
                                         failedPlaintext) ||
                !AllBytes(failedPlaintext, 0xA5) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_AES_GCM_INVALID_TAG_FAIL_CLOSED_PASS\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_AES_GCM_NO_PLAINTEXT_ON_FAILURE_PASS\r\n"u8))
            {
                invalidTag.Clear();
                return ManagedKernelContract.InvalidState;
            }
            invalidTag.Clear();

            plaintext.Clear();
            if (!ManagedAesGcm.TryDecrypt(gcmKey, gcmNonce,
                                          ReadOnlySpan<byte>.Empty,
                                          gcmCiphertext, gcmTag, plaintext) ||
                !ManagedCryptoComparison.FixedTimeEquals(plaintext,
                                                         zeroPlaintext) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_AES_GCM_POST_FAILURE_RECOVERY_PASS\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }

            aes.Reset();
            if (aes.IsInitialized || aes.TryEncryptBlock(aesPlaintext, block) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_AES_GCM_RESET_REUSE_PASS\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }

            if (!RunEntropyNonceProof())
            {
                return ManagedKernelContract.InvalidState;
            }

            s_run = 1;
            return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE27_PASS\r\n"u8)
                ? ManagedKernelContract.ManagedOk
                : ManagedKernelContract.InvalidState;
        }
        finally
        {
            aes.Clear();
            block.Clear();
            ghash.Clear();
            ciphertext.Clear();
            tag.Clear();
            plaintext.Clear();
            zeroPlaintext.Clear();
            failedPlaintext.Clear();
            aesKey.Clear();
            aesPlaintext.Clear();
            aesCiphertext.Clear();
            gcmKey.Clear();
            gcmNonce.Clear();
            gcmCiphertext.Clear();
            gcmTag.Clear();
            ghashSubkey.Clear();
            ghashAad.Clear();
            ghashCiphertext.Clear();
            ghashExpected.Clear();
        }
    }

    private static bool RunEntropyNonceProof()
    {
        ManagedEntropyService? entropy = ManagedKernelContract.EntropyService;
        ManagedSecureRandom? random = ManagedKernelContract.SecureRandom;
        ManagedVirtioRngDriver? candidate = ManagedVirtioRngDriver.TryCreate();
        if (!candidate.HasValue)
        {
            return false;
        }
        if (entropy == null || random == null)
        {
            entropy = new ManagedEntropyService(
                ManagedKernelContract.EntropyFillAddress,
                ManagedKernelContract.EntropyCapabilities,
                ManagedKernelContract.EntropyMaxBytesPerFill);
            random = new ManagedSecureRandom(entropy);
        }

        ManagedVirtioRngDriver driver = candidate.Value;
        Span<byte> nonce = stackalloc byte[ManagedAesGcm.NonceSize];
        Span<byte> message = stackalloc byte[19];
        Span<byte> encrypted = stackalloc byte[19];
        Span<byte> tag = stackalloc byte[ManagedAesGcm.TagSize];
        Span<byte> recovered = stackalloc byte[19];
        try
        {
            if (!driver.TryStart())
            {
                KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_START_FAIL\r\n"u8);
                return false;
            }
            KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_START_PASS\r\n"u8);
            entropy.AttachVirtioRng(driver);
            message.Clear();
            "phase27-nonce-proof"u8.CopyTo(message);
            if (!random.TryFill(nonce) ||
                entropy.LastProvider != ManagedEntropyProviderKind.VirtioRng ||
                !ManagedAesGcm.TryEncrypt(new byte[16], nonce,
                                          ReadOnlySpan<byte>.Empty, message,
                                          encrypted, tag) ||
                !ManagedAesGcm.TryDecrypt(new byte[16], nonce,
                                          ReadOnlySpan<byte>.Empty, encrypted,
                                          tag, recovered) ||
                !ManagedCryptoComparison.FixedTimeEquals(message, recovered))
            {
                KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_OPERATION_FAIL\r\n"u8);
                return false;
            }
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_PROVIDER=VIRTIO_RNG\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_SECURE_RANDOM_GCM_NONCE_PASS\r\n"u8))
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
            nonce.Clear();
            message.Clear();
            encrypted.Clear();
            tag.Clear();
            recovered.Clear();
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

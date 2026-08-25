using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class ManagedCryptoKernelProof
{
    private static readonly byte[] Sha256Abc =
    {
        0xBA, 0x78, 0x16, 0xBF, 0x8F, 0x01, 0xCF, 0xEA,
        0x41, 0x41, 0x40, 0xDE, 0x5D, 0xAE, 0x22, 0x23,
        0xB0, 0x03, 0x61, 0xA3, 0x96, 0x17, 0x7A, 0x9C,
        0xB4, 0x10, 0xFF, 0x61, 0xF2, 0x00, 0x15, 0xAD
    };
    private static readonly byte[] HmacKey =
    {
        0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B,
        0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B, 0x0B,
        0x0B, 0x0B, 0x0B, 0x0B
    };
    private static readonly byte[] HmacHiThere =
    {
        0xB0, 0x34, 0x4C, 0x61, 0xD8, 0xDB, 0x38, 0x53,
        0x5C, 0xA8, 0xAF, 0xCE, 0xAF, 0x0B, 0xF1, 0x2B,
        0x88, 0x1D, 0xC2, 0x00, 0xC9, 0x83, 0x3D, 0xA7,
        0x26, 0xE9, 0x37, 0x6C, 0x2E, 0x32, 0xCF, 0xF7
    };
    private static int s_run;

    [System.Runtime.InteropServices.UnmanagedCallersOnly(
        EntryPoint = "GxManagedKernelRunPhase25")]
    internal static uint Run()
    {
        Span<byte> digest = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> mac = stackalloc byte[ManagedHmacSha256.DigestSize];
        Span<byte> firstRandom = stackalloc byte[32];
        Span<byte> secondRandom = stackalloc byte[32];
        ManagedSha256 sha;
        ManagedHmacSha256? hmac;
        ManagedSecureRandom? random;

        if (!ManagedKernelContract.IsStarted || s_run != 0 ||
            !ManagedKernelContract.EntropyServicesInstalled)
        {
            return ManagedKernelContract.InvalidState;
        }
        random = ManagedKernelContract.SecureRandom;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_CRYPTO_PHASE25_INITIALIZED\r\n"u8))
        {
            return ManagedKernelContract.InvalidState;
        }

        sha = new ManagedSha256();
        if (!sha.Append("a"u8)) return ManagedKernelContract.InvalidState;
        GC.Collect();
        if (!sha.Append("bc"u8) || !sha.TryFinalize(digest) ||
            !ManagedCryptoComparison.FixedTimeEquals(digest, Sha256Abc))
        {
            return ManagedKernelContract.InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_SHA256_KAT_PASS\r\n"u8))
        {
            return ManagedKernelContract.InvalidState;
        }

        sha.Reset();
        if (!sha.Append("abc"u8) || !sha.TryFinalize(digest) ||
            !ManagedCryptoComparison.FixedTimeEquals(digest, Sha256Abc))
        {
            return ManagedKernelContract.InvalidState;
        }
        if (!ManagedHmacSha256.TryCreate(HmacKey, out hmac) || hmac == null ||
            !hmac.Append("Hi "u8) ||
            !hmac.Append("There"u8) ||
            !hmac.TryFinalize(mac) ||
            !ManagedCryptoComparison.FixedTimeEquals(mac, HmacHiThere))
        {
            return ManagedKernelContract.InvalidState;
        }
        GC.Collect();
        hmac.Reset();
        if (!hmac.Append("Hi There"u8) || !hmac.TryFinalize(mac) ||
            !ManagedCryptoComparison.FixedTimeEquals(mac, HmacHiThere))
        {
            hmac.Clear();
            return ManagedKernelContract.InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HMAC_SHA256_KAT_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_CRYPTO_CONSTANT_TIME_COMPARISON_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_CRYPTO_GC_SURVIVAL_PASS\r\n"u8))
        {
            hmac.Clear();
            return ManagedKernelContract.InvalidState;
        }

        if (random == null || !random.IsAvailable)
        {
            /* An unsupported production entropy boundary is a successful
               fail-closed proof for Outcome C, not a source of test bytes. */
            if (random != null && random.TryFill(firstRandom))
            {
                hmac.Clear();
                return ManagedKernelContract.InvalidState;
            }
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_SECURE_RANDOM_UNAVAILABLE_FAIL_CLOSED_PASS\r\n"u8))
            {
                hmac.Clear();
                return ManagedKernelContract.InvalidState;
            }
            firstRandom.Clear();
            secondRandom.Clear();
            digest.Clear();
            mac.Clear();
            sha.Reset();
            hmac.Clear();
            s_run = 1;
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_CRYPTO_RESET_TEARDOWN_COMPLETE\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE25_PASS\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }
            return ManagedKernelContract.ManagedOk;
        }

        KernelLog.WriteHexLine(
            "GXOS_NET10:MANAGED_SECURE_RANDOM_CAPABILITIES=0x"u8,
            ManagedKernelContract.EntropyCapabilities);
        if (!random.TryFill(firstRandom))
        {
            hmac.Clear();
            return ManagedKernelContract.InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_FILL_PASS\r\n"u8))
        {
            hmac.Clear();
            return ManagedKernelContract.InvalidState;
        }
        GC.Collect();
        if (!random.TryFill(secondRandom) ||
            ManagedCryptoComparison.FixedTimeEquals(firstRandom, secondRandom))
        {
            hmac.Clear();
            return ManagedKernelContract.InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_SECURE_RANDOM_REPEATED_FILL_PASS\r\n"u8))
        {
            hmac.Clear();
            return ManagedKernelContract.InvalidState;
        }

        firstRandom.Clear();
        secondRandom.Clear();
        digest.Clear();
        mac.Clear();
        sha.Reset();
        hmac.Clear();
        s_run = 1;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_CRYPTO_RESET_TEARDOWN_COMPLETE\r\n"u8))
        {
            return ManagedKernelContract.InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE25_PASS\r\n"u8))
        {
            return ManagedKernelContract.InvalidState;
        }
        return ManagedKernelContract.ManagedOk;
    }
}

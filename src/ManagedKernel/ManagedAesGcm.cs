using System;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>
/// Narrow AES-128-GCM profile for the managed kernel.  It accepts only the
/// standard 96-bit nonce and full 128-bit tag, uses caller-owned I/O buffers,
/// and rejects overlapping input/output spans.  Nonce uniqueness for each
/// key is the caller's responsibility; reusing a GCM nonce with the same key
/// is catastrophic and prohibited.
/// </summary>
internal static class ManagedAesGcm
{
    internal const int KeySize = ManagedAes128.KeySize;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal const int MaximumAadBytes = 256;
    internal const int MaximumPayloadBytes = 16 * 1024;

    internal static bool TryEncrypt(ReadOnlySpan<byte> key,
                                    ReadOnlySpan<byte> nonce,
                                    ReadOnlySpan<byte> aad,
                                    ReadOnlySpan<byte> plaintext,
                                    Span<byte> ciphertext,
                                    Span<byte> tag)
    {
        if (!Validate(key, nonce, aad, plaintext.Length, ciphertext.Length,
                      tag, true) ||
            HasOverlap(key, ciphertext) || HasOverlap(key, tag) ||
            HasOverlap(nonce, ciphertext) || HasOverlap(nonce, tag) ||
            HasOverlap(aad, ciphertext) || HasOverlap(aad, tag) ||
            HasOverlap(plaintext, ciphertext) || HasOverlap(plaintext, tag) ||
            HasOverlap(ciphertext, tag))
        {
            return false;
        }

        ManagedAes128 aes = new();
        Span<byte> hashSubkey = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> zero = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> j0 = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> counter = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> computedTag = stackalloc byte[TagSize];
        try
        {
            if (!aes.TrySetKey(key) ||
                !aes.TryEncryptBlock(zero, hashSubkey))
            {
                return false;
            }
            nonce.CopyTo(j0);
            j0[15] = 1;
            j0.CopyTo(counter);
            if (!TryGctr(aes, counter, plaintext, ciphertext) ||
                !TryComputeTag(aes, hashSubkey, j0, aad,
                    ciphertext[..plaintext.Length], computedTag))
            {
                return false;
            }
            computedTag.CopyTo(tag);
            return true;
        }
        finally
        {
            aes.Clear();
            hashSubkey.Clear();
            zero.Clear();
            j0.Clear();
            counter.Clear();
            computedTag.Clear();
        }
    }

    internal static bool TryDecrypt(ReadOnlySpan<byte> key,
                                    ReadOnlySpan<byte> nonce,
                                    ReadOnlySpan<byte> aad,
                                    ReadOnlySpan<byte> ciphertext,
                                    ReadOnlySpan<byte> tag,
                                    Span<byte> plaintext)
    {
        if (!Validate(key, nonce, aad, ciphertext.Length, plaintext.Length,
                      tag, false) ||
            HasOverlap(key, plaintext) || HasOverlap(nonce, plaintext) ||
            HasOverlap(aad, plaintext) || HasOverlap(ciphertext, plaintext) ||
            HasOverlap(tag, plaintext))
        {
            return false;
        }

        ManagedAes128 aes = new();
        Span<byte> hashSubkey = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> zero = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> j0 = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> counter = stackalloc byte[ManagedAes128.BlockSize];
        Span<byte> expectedTag = stackalloc byte[TagSize];
        try
        {
            if (!aes.TrySetKey(key) ||
                !aes.TryEncryptBlock(zero, hashSubkey))
            {
                return false;
            }
            nonce.CopyTo(j0);
            j0[15] = 1;
            if (!TryComputeTag(aes, hashSubkey, j0, aad, ciphertext,
                               expectedTag) ||
                !ManagedCryptoComparison.FixedTimeEquals(expectedTag, tag))
            {
                /* No byte of plaintext has been written before this point.
                   The caller's output remains untouched on authentication
                   failure, including tag/ciphertext corruption. */
                return false;
            }

            j0.CopyTo(counter);
            return TryGctr(aes, counter, ciphertext, plaintext);
        }
        finally
        {
            aes.Clear();
            hashSubkey.Clear();
            zero.Clear();
            j0.Clear();
            counter.Clear();
            expectedTag.Clear();
        }
    }

    private static bool TryComputeTag(ManagedAes128 aes,
                                      ReadOnlySpan<byte> hashSubkey,
                                      ReadOnlySpan<byte> j0,
                                      ReadOnlySpan<byte> aad,
                                      ReadOnlySpan<byte> ciphertext,
                                      Span<byte> destination)
    {
        Span<byte> ghash = stackalloc byte[TagSize];
        Span<byte> encryptedJ0 = stackalloc byte[TagSize];
        try
        {
            if (!ManagedGhash.TryCompute(hashSubkey, aad, ciphertext, ghash) ||
                !aes.TryEncryptBlock(j0, encryptedJ0))
            {
                return false;
            }
            for (int index = 0; index != TagSize; ++index)
            {
                destination[index] = (byte)(ghash[index] ^ encryptedJ0[index]);
            }
            return true;
        }
        finally
        {
            ghash.Clear();
            encryptedJ0.Clear();
        }
    }

    private static bool TryGctr(ManagedAes128 aes,
                                Span<byte> counter,
                                ReadOnlySpan<byte> input,
                                Span<byte> output)
    {
        Span<byte> keystream = stackalloc byte[ManagedAes128.BlockSize];
        try
        {
            int offset = 0;
            while (offset != input.Length)
            {
                if (!TryIncrementCounter(counter) ||
                    !aes.TryEncryptBlock(counter, keystream))
                {
                    return false;
                }
                int count = Math.Min(ManagedAes128.BlockSize,
                                     input.Length - offset);
                for (int index = 0; index != count; ++index)
                {
                    output[offset + index] = (byte)(input[offset + index] ^
                                                    keystream[index]);
                }
                offset += count;
            }
            return true;
        }
        finally
        {
            keystream.Clear();
        }
    }

    private static bool TryIncrementCounter(Span<byte> counter)
    {
        uint value = ((uint)counter[12] << 24) |
                     ((uint)counter[13] << 16) |
                     ((uint)counter[14] << 8) |
                     counter[15];
        if (value == uint.MaxValue)
        {
            return false;
        }
        value++;
        counter[12] = (byte)(value >> 24);
        counter[13] = (byte)(value >> 16);
        counter[14] = (byte)(value >> 8);
        counter[15] = (byte)value;
        return true;
    }

    private static bool Validate(ReadOnlySpan<byte> key,
                                 ReadOnlySpan<byte> nonce,
                                 ReadOnlySpan<byte> aad,
                                 int inputLength,
                                 int outputLength,
                                 ReadOnlySpan<byte> tag,
                                 bool encryption)
    {
        if (key.Length != KeySize || nonce.Length != NonceSize ||
            tag.Length != TagSize || aad.Length > MaximumAadBytes ||
            inputLength > MaximumPayloadBytes || outputLength < inputLength)
        {
            return false;
        }

        /* J0 starts at counter 1.  At most 0xFFFFFFFE blocks may be
           incremented before the low 32-bit counter would exhaust.  The
           Phase 27 payload cap is much smaller, but keep this check beside
           the counter construction so the wrap policy remains explicit. */
        int blocks = (inputLength + (ManagedAes128.BlockSize - 1)) /
                     ManagedAes128.BlockSize;
        if ((ulong)blocks > 0xFFFFFFFEUL)
        {
            return false;
        }

        if (encryption)
        {
            return outputLength >= inputLength;
        }
        return outputLength == inputLength || outputLength > inputLength;
    }

    private static bool HasOverlap(ReadOnlySpan<byte> input,
                                   Span<byte> output) => input.Overlaps(output);
}

using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static class ManagedTls12RecordProtection
{
    internal const byte ChangeCipherSpec = 20;
    internal const byte Alert = 21;
    internal const byte Handshake = 22;
    internal const byte ApplicationData = 23;
    internal const int HeaderSize = 5;
    internal const int ExplicitNonceSize = 8;
    internal const int MaximumPlaintextFragment = 16 * 1024;
    internal const int MaximumCiphertextFragment =
        ExplicitNonceSize + MaximumPlaintextFragment + ManagedAesGcm.TagSize;
    internal const int MaximumRecordSize = HeaderSize + MaximumCiphertextFragment;

    internal static bool TryEncrypt(ulong sequence,
                                    ReadOnlySpan<byte> key,
                                    ReadOnlySpan<byte> fixedIv,
                                    byte contentType,
                                    ReadOnlySpan<byte> plaintext,
                                    Span<byte> destination,
                                    out int written)
    {
        written = 0;
        if (sequence == ulong.MaxValue || key.Length != ManagedAesGcm.KeySize ||
            fixedIv.Length != 4 || !IsSupportedContentType(contentType) ||
            plaintext.Length > MaximumPlaintextFragment)
            return false;

        int fragmentLength = ExplicitNonceSize + plaintext.Length +
                             ManagedAesGcm.TagSize;
        int totalLength = HeaderSize + fragmentLength;
        if (destination.Length < totalLength)
            return false;

        Span<byte> nonce = stackalloc byte[ManagedAesGcm.NonceSize];
        Span<byte> aad = stackalloc byte[13];
        Span<byte> tag = stackalloc byte[ManagedAesGcm.TagSize];
        try
        {
            fixedIv.CopyTo(nonce);
            WriteUInt64(sequence, nonce[4..]);
            destination[0] = contentType;
            destination[1] = 3;
            destination[2] = 3;
            WriteUInt16((ushort)fragmentLength, destination[3..]);
            WriteUInt64(sequence, aad);
            aad[8] = contentType;
            aad[9] = 3;
            aad[10] = 3;
            WriteUInt16((ushort)plaintext.Length, aad[11..]);
            WriteUInt64(sequence, destination.Slice(HeaderSize,
                                                     ExplicitNonceSize));
            if (!ManagedAesGcm.TryEncrypt(
                    key, nonce, aad, plaintext,
                    destination.Slice(HeaderSize + ExplicitNonceSize,
                                      plaintext.Length), tag))
                return false;
            tag.CopyTo(destination.Slice(HeaderSize + ExplicitNonceSize +
                                         plaintext.Length));
            written = totalLength;
            return true;
        }
        finally
        {
            nonce.Clear();
            aad.Clear();
            tag.Clear();
        }
    }

    internal static bool TryDecrypt(ulong sequence,
                                    ReadOnlySpan<byte> key,
                                    ReadOnlySpan<byte> fixedIv,
                                    byte expectedContentType,
                                    ReadOnlySpan<byte> record,
                                    Span<byte> plaintext,
                                    out int plaintextLength)
    {
        plaintextLength = 0;
        if (sequence == ulong.MaxValue ||
            key.Length != ManagedAesGcm.KeySize || fixedIv.Length != 4 ||
            !IsSupportedContentType(expectedContentType) ||
            record.Length < HeaderSize + ExplicitNonceSize +
                            ManagedAesGcm.TagSize ||
            record.Length > MaximumRecordSize ||
            record[0] != expectedContentType || record[1] != 3 ||
            record[2] != 3)
            return false;

        int fragmentLength = record.Length - HeaderSize;
        int cipherLength = fragmentLength - ExplicitNonceSize -
                           ManagedAesGcm.TagSize;
        if (cipherLength < 0 || cipherLength > MaximumPlaintextFragment ||
            fragmentLength != ExplicitNonceSize + cipherLength +
                              ManagedAesGcm.TagSize ||
            plaintext.Length < cipherLength ||
            ((record[3] << 8) | record[4]) != fragmentLength)
            return false;

        Span<byte> nonce = stackalloc byte[ManagedAesGcm.NonceSize];
        Span<byte> aad = stackalloc byte[13];
        try
        {
            fixedIv.CopyTo(nonce);
            record.Slice(HeaderSize, ExplicitNonceSize).CopyTo(nonce[4..]);
            WriteUInt64(sequence, aad);
            aad[8] = expectedContentType;
            aad[9] = 3;
            aad[10] = 3;
            WriteUInt16((ushort)cipherLength, aad[11..]);
            if (!ManagedAesGcm.TryDecrypt(
                    key, nonce, aad,
                    record.Slice(HeaderSize + ExplicitNonceSize, cipherLength),
                    record.Slice(record.Length - ManagedAesGcm.TagSize),
                    plaintext[..cipherLength]))
            {
                /* ManagedAesGcm authenticates before writing. Preserve the
                   caller's destination on failure so the record boundary is
                   fail-closed without silently mutating an output buffer. */
                return false;
            }
            plaintextLength = cipherLength;
            return true;
        }
        finally
        {
            nonce.Clear();
            aad.Clear();
        }
    }

    internal static void WriteUInt16(ushort value, Span<byte> destination)
    {
        destination[0] = (byte)(value >> 8);
        destination[1] = (byte)value;
    }

    internal static void WriteUInt24(int value, Span<byte> destination)
    {
        destination[0] = (byte)(value >> 16);
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)value;
    }

    internal static void WriteUInt64(ulong value, Span<byte> destination)
    {
        for (int index = 0; index != 8; ++index)
            destination[index] = (byte)(value >> (56 - index * 8));
    }

    private static bool IsSupportedContentType(byte value)
    {
        return value == ChangeCipherSpec || value == Alert ||
               value == Handshake || value == ApplicationData;
    }
}

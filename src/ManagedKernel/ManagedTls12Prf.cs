using System;

namespace GuideXOS.Net10.ManagedKernel;

/* RFC 5246 §5 and RFC 7627 §4.  Phase 31 has one PRF profile: the TLS 1.2
   SHA-256 construction.  The implementation is deliberately bounded by the
   caller's destination span and never uses a framework cryptography type. */
internal static class ManagedTls12Prf
{
    internal const int DigestSize = ManagedSha256.DigestSize;

    internal static bool TryCompute(ReadOnlySpan<byte> secret,
                                    ReadOnlySpan<byte> label,
                                    ReadOnlySpan<byte> seed,
                                    Span<byte> destination)
    {
        ManagedHmacSha256 hmac = new();
        try
        {
            return TryCompute(secret, label, seed, destination, hmac);
        }
        finally
        {
            hmac.Clear();
        }
    }

    internal static bool TryCompute(ReadOnlySpan<byte> secret,
                                    ReadOnlySpan<byte> label,
                                    ReadOnlySpan<byte> seed,
                                    Span<byte> destination,
                                    ManagedHmacSha256 hmac)
    {
        if (destination.Length == 0 || label.Length > 128 ||
            seed.Length > 256 ||
            label.Length > int.MaxValue - seed.Length)
            return false;

        int labelSeedLength = label.Length + seed.Length;
        Span<byte> labelSeed = stackalloc byte[384];
        Span<byte> a = stackalloc byte[DigestSize];
        Span<byte> nextA = stackalloc byte[DigestSize];
        Span<byte> blockInput = stackalloc byte[DigestSize + 384];
        Span<byte> block = stackalloc byte[DigestSize];
        try
        {
            label.CopyTo(labelSeed);
            seed.CopyTo(labelSeed[label.Length..]);
            if (!hmac.TryComputeInto(secret, labelSeed[..labelSeedLength], a))
                return false;

            int written = 0;
            while (written < destination.Length)
            {
                a.CopyTo(blockInput);
                labelSeed[..labelSeedLength].CopyTo(
                    blockInput[DigestSize..]);
                if (!hmac.TryComputeInto(
                        secret, blockInput[..(DigestSize + labelSeedLength)],
                        block))
                    return false;

                int count = Math.Min(DigestSize, destination.Length - written);
                block[..count].CopyTo(destination[written..]);
                written += count;
                if (written < destination.Length)
                {
                    if (!hmac.TryComputeInto(secret, a, nextA))
                        return false;
                    nextA.CopyTo(a);
                }
            }
            return true;
        }
        finally
        {
            labelSeed.Clear();
            a.Clear();
            nextA.Clear();
            blockInput.Clear();
            block.Clear();
        }
    }
}

internal sealed class ManagedTls12Transcript
{
    internal const int MaximumBytes = 64 * 1024;
    private const int InitialBytes = 4096;

    private byte[] _encoded = new byte[InitialBytes];
    private int _length;

    internal int Length => _length;

    internal bool Append(ReadOnlySpan<byte> handshakeMessage)
    {
        if (handshakeMessage.Length == 0 ||
            handshakeMessage.Length > MaximumBytes - _length)
            return false;
        int required = _length + handshakeMessage.Length;
        if (required > _encoded.Length)
        {
            int capacity = _encoded.Length;
            while (capacity < required)
            {
                if (capacity >= MaximumBytes / 2)
                {
                    capacity = MaximumBytes;
                    break;
                }
                capacity *= 2;
            }
            byte[] expanded = new byte[capacity];
            _encoded.AsSpan(0, _length).CopyTo(expanded);
            _encoded.AsSpan().Clear();
            _encoded = expanded;
        }
        handshakeMessage.CopyTo(_encoded.AsSpan(_length));
        _length += handshakeMessage.Length;
        return true;
    }

    internal bool TryHash(Span<byte> destination)
    {
        return destination.Length >= ManagedSha256.DigestSize &&
               ManagedSha256.TryHash(_encoded.AsSpan(0, _length),
                                     destination);
    }

    internal void Clear()
    {
        _encoded.AsSpan().Clear();
        _length = 0;
    }
}

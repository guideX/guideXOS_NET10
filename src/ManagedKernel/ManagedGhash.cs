using System;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>
/// Incremental GHASH for GCM.  Multiplication follows SP 800-38D's
/// MSB-first, bit-serial algorithm and the reduction constant R = E1 || 0^120.
/// The state is fixed-size and never buffers AAD or ciphertext.
/// </summary>
internal sealed class ManagedGhash
{
    internal const int BlockSize = 16;
    internal const int DigestSize = 16;
    internal const ulong MaximumByteCount = ulong.MaxValue >> 3;

    private readonly byte[] _hashSubkey = new byte[BlockSize];
    private readonly byte[] _hash = new byte[BlockSize];
    private readonly byte[] _partial = new byte[BlockSize];
    private int _partialLength;
    private ulong _aadByteCount;
    private ulong _ciphertextByteCount;
    private bool _initialized;
    private bool _ciphertextPhase;
    private bool _finalized;

    internal bool TryInitialize(ReadOnlySpan<byte> hashSubkey)
    {
        Clear();
        if (hashSubkey.Length != BlockSize)
        {
            return false;
        }
        hashSubkey.CopyTo(_hashSubkey);
        _initialized = true;
        return true;
    }

    internal bool AppendAad(ReadOnlySpan<byte> data)
    {
        if (!_initialized || _ciphertextPhase || _finalized ||
            !TryAddLength(ref _aadByteCount, data.Length))
        {
            return false;
        }
        AppendBytes(data);
        return true;
    }

    internal bool AppendCiphertext(ReadOnlySpan<byte> data)
    {
        if (!_initialized || _finalized ||
            !TryAddLength(ref _ciphertextByteCount, data.Length))
        {
            return false;
        }
        if (!_ciphertextPhase)
        {
            FinishPartial();
            _ciphertextPhase = true;
        }
        AppendBytes(data);
        return true;
    }

    internal bool TryFinalize(Span<byte> destination)
    {
        if (!_initialized || _finalized || destination.Length < DigestSize)
        {
            return false;
        }

        FinishPartial();
        Span<byte> lengthBlock = stackalloc byte[BlockSize];
        WriteUInt64BigEndian(lengthBlock, 0, _aadByteCount << 3);
        WriteUInt64BigEndian(lengthBlock, 8, _ciphertextByteCount << 3);
        ProcessBlock(lengthBlock);
        _hash.AsSpan().CopyTo(destination);
        lengthBlock.Clear();
        _partial.AsSpan().Clear();
        _partialLength = 0;
        _finalized = true;
        return true;
    }

    internal void Reset()
    {
        _hash.AsSpan().Clear();
        _partial.AsSpan().Clear();
        _partialLength = 0;
        _aadByteCount = 0;
        _ciphertextByteCount = 0;
        _ciphertextPhase = false;
        _finalized = false;
    }

    internal void Clear()
    {
        _hashSubkey.AsSpan().Clear();
        Reset();
        _initialized = false;
    }

    internal static bool TryCompute(ReadOnlySpan<byte> hashSubkey,
                                    ReadOnlySpan<byte> aad,
                                    ReadOnlySpan<byte> ciphertext,
                                    Span<byte> destination)
    {
        ManagedGhash ghash = new();
        try
        {
            return ghash.TryInitialize(hashSubkey) &&
                ghash.AppendAad(aad) &&
                ghash.AppendCiphertext(ciphertext) &&
                ghash.TryFinalize(destination);
        }
        finally
        {
            ghash.Clear();
        }
    }

    private void AppendBytes(ReadOnlySpan<byte> data)
    {
        if (_partialLength != 0)
        {
            int copied = Math.Min(data.Length, BlockSize - _partialLength);
            data[..copied].CopyTo(_partial.AsSpan(_partialLength));
            _partialLength += copied;
            data = data[copied..];
            if (_partialLength == BlockSize)
            {
                ProcessBlock(_partial);
                _partial.AsSpan().Clear();
                _partialLength = 0;
            }
        }

        while (data.Length >= BlockSize)
        {
            ProcessBlock(data[..BlockSize]);
            data = data[BlockSize..];
        }
        if (!data.IsEmpty)
        {
            data.CopyTo(_partial);
            _partialLength = data.Length;
        }
    }

    private void FinishPartial()
    {
        if (_partialLength == 0)
        {
            return;
        }
        _partial.AsSpan(_partialLength).Clear();
        ProcessBlock(_partial);
        _partial.AsSpan().Clear();
        _partialLength = 0;
    }

    private void ProcessBlock(ReadOnlySpan<byte> block)
    {
        for (int index = 0; index != BlockSize; ++index)
        {
            _hash[index] ^= block[index];
        }
        Span<byte> product = stackalloc byte[BlockSize];
        Multiply(_hash, _hashSubkey, product);
        product.CopyTo(_hash);
        product.Clear();
    }

    private static void Multiply(ReadOnlySpan<byte> x,
                                 ReadOnlySpan<byte> hashSubkey,
                                 Span<byte> destination)
    {
        Span<byte> z = stackalloc byte[BlockSize];
        Span<byte> v = stackalloc byte[BlockSize];
        hashSubkey.CopyTo(v);
        for (int bit = 0; bit != 128; ++bit)
        {
            int byteIndex = bit >> 3;
            int bitMask = 0x80 >> (bit & 7);
            if ((x[byteIndex] & bitMask) != 0)
            {
                for (int index = 0; index != BlockSize; ++index)
                {
                    z[index] ^= v[index];
                }
            }

            bool lowBitSet = (v[BlockSize - 1] & 1) != 0;
            ShiftRight(v);
            if (lowBitSet)
            {
                v[0] ^= 0xE1;
            }
        }
        z.CopyTo(destination);
        z.Clear();
        v.Clear();
    }

    private static void ShiftRight(Span<byte> value)
    {
        byte carry = 0;
        for (int index = 0; index != BlockSize; ++index)
        {
            byte next = value[index];
            value[index] = (byte)((next >> 1) | carry);
            carry = (byte)((next & 1) << 7);
        }
    }

    private static bool TryAddLength(ref ulong count, int bytes)
    {
        ulong length = (uint)bytes;
        if (length > MaximumByteCount - count)
        {
            return false;
        }
        count += length;
        return true;
    }

    private static void WriteUInt64BigEndian(Span<byte> destination,
                                              int offset,
                                              ulong value)
    {
        for (int index = 0; index != 8; ++index)
        {
            destination[offset + index] = (byte)(value >> (56 - (index * 8)));
        }
    }
}

using System;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>
/// Small, allocation-bounded, repository-owned SHA-256 implementation.
/// </summary>
internal sealed class ManagedSha256
{
    internal const int BlockSize = 64;
    internal const int DigestSize = 32;

    private static readonly uint[] RoundConstants =
    {
        0x428A2F98U, 0x71374491U, 0xB5C0FBCFU, 0xE9B5DBA5U,
        0x3956C25BU, 0x59F111F1U, 0x923F82A4U, 0xAB1C5ED5U,
        0xD807AA98U, 0x12835B01U, 0x243185BEU, 0x550C7DC3U,
        0x72BE5D74U, 0x80DEB1FEU, 0x9BDC06A7U, 0xC19BF174U,
        0xE49B69C1U, 0xEFBE4786U, 0x0FC19DC6U, 0x240CA1CCU,
        0x2DE92C6FU, 0x4A7484AAU, 0x5CB0A9DCU, 0x76F988DAU,
        0x983E5152U, 0xA831C66DU, 0xB00327C8U, 0xBF597FC7U,
        0xC6E00BF3U, 0xD5A79147U, 0x06CA6351U, 0x14292967U,
        0x27B70A85U, 0x2E1B2138U, 0x4D2C6DFCU, 0x53380D13U,
        0x650A7354U, 0x766A0ABBU, 0x81C2C92EU, 0x92722C85U,
        0xA2BFE8A1U, 0xA81A664BU, 0xC24B8B70U, 0xC76C51A3U,
        0xD192E819U, 0xD6990624U, 0xF40E3585U, 0x106AA070U,
        0x19A4C116U, 0x1E376C08U, 0x2748774CU, 0x34B0BCB5U,
        0x391C0CB3U, 0x4ED8AA4AU, 0x5B9CCA4FU, 0x682E6FF3U,
        0x748F82EEU, 0x78A5636FU, 0x84C87814U, 0x8CC70208U,
        0x90BEFFFAU, 0xA4506CEBU, 0xBEF9A3F7U, 0xC67178F2U
    };

    private readonly byte[] _block = new byte[BlockSize];
    private readonly uint[] _state = new uint[8];
    private readonly uint[] _schedule = new uint[64];
    private int _blockLength;
    private ulong _messageLengthBytes;
    private bool _finalized;

    internal ManagedSha256()
    {
        Reset();
    }

    internal bool Append(ReadOnlySpan<byte> data)
    {
        if (_finalized || (ulong)data.Length > ulong.MaxValue - _messageLengthBytes)
        {
            return false;
        }

        _messageLengthBytes += (ulong)data.Length;
        while (!data.IsEmpty)
        {
            int copyLength = Math.Min(data.Length, BlockSize - _blockLength);
            data[..copyLength].CopyTo(_block.AsSpan(_blockLength));
            _blockLength += copyLength;
            data = data[copyLength..];
            if (_blockLength == BlockSize)
            {
                Compress(_block);
                _blockLength = 0;
            }
        }
        return true;
    }

    internal bool TryFinalize(Span<byte> destination)
    {
        if (_finalized || destination.Length < DigestSize)
        {
            return false;
        }

        ulong bitLength = _messageLengthBytes << 3;
        _block[_blockLength++] = 0x80;
        if (_blockLength > 56)
        {
            while (_blockLength != BlockSize) _block[_blockLength++] = 0;
            Compress(_block);
            _blockLength = 0;
        }
        while (_blockLength != 56) _block[_blockLength++] = 0;
        for (int index = 0; index != 8; ++index)
        {
            _block[56 + index] = (byte)(bitLength >> (56 - index * 8));
        }
        Compress(_block);
        _blockLength = 0;

        for (int index = 0; index != 8; ++index)
        {
            uint word = _state[index];
            int offset = index * 4;
            destination[offset] = (byte)(word >> 24);
            destination[offset + 1] = (byte)(word >> 16);
            destination[offset + 2] = (byte)(word >> 8);
            destination[offset + 3] = (byte)word;
        }

        _block.AsSpan().Clear();
        _finalized = true;
        return true;
    }

    internal void Reset()
    {
        _state[0] = 0x6A09E667U;
        _state[1] = 0xBB67AE85U;
        _state[2] = 0x3C6EF372U;
        _state[3] = 0xA54FF53AU;
        _state[4] = 0x510E527FU;
        _state[5] = 0x9B05688CU;
        _state[6] = 0x1F83D9ABU;
        _state[7] = 0x5BE0CD19U;
        _block.AsSpan().Clear();
        _schedule.AsSpan().Clear();
        _blockLength = 0;
        _messageLengthBytes = 0;
        _finalized = false;
    }

    internal static bool TryHash(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (destination.Length < DigestSize)
        {
            return false;
        }
        ManagedSha256 hash = new();
        return hash.Append(data) && hash.TryFinalize(destination);
    }

    private void Compress(ReadOnlySpan<byte> block)
    {
        for (int index = 0; index != 16; ++index)
        {
            int offset = index * 4;
            _schedule[index] = ((uint)block[offset] << 24) |
                ((uint)block[offset + 1] << 16) |
                ((uint)block[offset + 2] << 8) |
                block[offset + 3];
        }
        for (int index = 16; index != 64; ++index)
        {
            uint lower = _schedule[index - 15];
            uint upper = _schedule[index - 2];
            uint sigma0 = RotateRight(lower, 7) ^ RotateRight(lower, 18) ^
                (lower >> 3);
            uint sigma1 = RotateRight(upper, 17) ^ RotateRight(upper, 19) ^
                (upper >> 10);
            _schedule[index] = _schedule[index - 16] + sigma0 +
                _schedule[index - 7] + sigma1;
        }

        uint a = _state[0];
        uint b = _state[1];
        uint c = _state[2];
        uint d = _state[3];
        uint e = _state[4];
        uint f = _state[5];
        uint g = _state[6];
        uint h = _state[7];
        for (int index = 0; index != 64; ++index)
        {
            uint sigma1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^
                RotateRight(e, 25);
            uint choice = (e & f) ^ (~e & g);
            uint temporary1 = h + sigma1 + choice + RoundConstants[index] +
                _schedule[index];
            uint sigma0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^
                RotateRight(a, 22);
            uint majority = (a & b) ^ (a & c) ^ (b & c);
            uint temporary2 = sigma0 + majority;
            h = g;
            g = f;
            f = e;
            e = d + temporary1;
            d = c;
            c = b;
            b = a;
            a = temporary1 + temporary2;
        }

        _state[0] += a;
        _state[1] += b;
        _state[2] += c;
        _state[3] += d;
        _state[4] += e;
        _state[5] += f;
        _state[6] += g;
        _state[7] += h;
    }

    private static uint RotateRight(uint value, int count)
    {
        return (value >> count) | (value << (32 - count));
    }
}

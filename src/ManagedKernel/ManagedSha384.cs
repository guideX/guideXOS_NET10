using System;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>
/// Small, allocation-bounded SHA-384 implementation used by the bounded
/// certificate profile.  It exposes SHA-384 only; the compression core is
/// intentionally kept private to this primitive.
/// </summary>
internal sealed class ManagedSha384
{
    internal const int BlockSize = 128;
    internal const int DigestSize = 48;

    private static readonly ulong[] RoundConstants =
    {
        0x428A2F98D728AE22UL, 0x7137449123EF65CDUL,
        0xB5C0FBCFEC4D3B2FUL, 0xE9B5DBA58189DBBCUL,
        0x3956C25BF348B538UL, 0x59F111F1B605D019UL,
        0x923F82A4AF194F9BUL, 0xAB1C5ED5DA6D8118UL,
        0xD807AA98A3030242UL, 0x12835B0145706FBEUL,
        0x243185BE4EE4B28CUL, 0x550C7DC3D5FFB4E2UL,
        0x72BE5D74F27B896FUL, 0x80DEB1FE3B1696B1UL,
        0x9BDC06A725C71235UL, 0xC19BF174CF692694UL,
        0xE49B69C19EF14AD2UL, 0xEFBE4786384F25E3UL,
        0x0FC19DC68B8CD5B5UL, 0x240CA1CC77AC9C65UL,
        0x2DE92C6F592B0275UL, 0x4A7484AA6EA6E483UL,
        0x5CB0A9DCBD41FBD4UL, 0x76F988DA831153B5UL,
        0x983E5152EE66DFABUL, 0xA831C66D2DB43210UL,
        0xB00327C898FB213FUL, 0xBF597FC7BEEF0EE4UL,
        0xC6E00BF33DA88FC2UL, 0xD5A79147930AA725UL,
        0x06CA6351E003826FUL, 0x142929670A0E6E70UL,
        0x27B70A8546D22FFCUL, 0x2E1B21385C26C926UL,
        0x4D2C6DFC5AC42AEDUL, 0x53380D139D95B3DFUL,
        0x650A73548BAF63DEUL, 0x766A0ABB3C77B2A8UL,
        0x81C2C92E47EDAEE6UL, 0x92722C851482353BUL,
        0xA2BFE8A14CF10364UL, 0xA81A664BBC423001UL,
        0xC24B8B70D0F89791UL, 0xC76C51A30654BE30UL,
        0xD192E819D6EF5218UL, 0xD69906245565A910UL,
        0xF40E35855771202AUL, 0x106AA07032BBD1B8UL,
        0x19A4C116B8D2D0C8UL, 0x1E376C085141AB53UL,
        0x2748774CDF8EEB99UL, 0x34B0BCB5E19B48A8UL,
        0x391C0CB3C5C95A63UL, 0x4ED8AA4AE3418ACBUL,
        0x5B9CCA4F7763E373UL, 0x682E6FF3D6B2B8A3UL,
        0x748F82EE5DEFB2FCUL, 0x78A5636F43172F60UL,
        0x84C87814A1F0AB72UL, 0x8CC702081A6439ECUL,
        0x90BEFFFA23631E28UL, 0xA4506CEBDE82BDE9UL,
        0xBEF9A3F7B2C67915UL, 0xC67178F2E372532BUL,
        0xCA273ECEEA26619CUL, 0xD186B8C721C0C207UL,
        0xEADA7DD6CDE0EB1EUL, 0xF57D4F7FEE6ED178UL,
        0x06F067AA72176FBAUL, 0x0A637DC5A2C898A6UL,
        0x113F9804BEF90DAEUL, 0x1B710B35131C471BUL,
        0x28DB77F523047D84UL, 0x32CAAB7B40C72493UL,
        0x3C9EBE0A15C9BEBCUL, 0x431D67C49C100D4CUL,
        0x4CC5D4BECB3E42B6UL, 0x597F299CFC657E2AUL,
        0x5FCB6FAB3AD6FAECUL, 0x6C44198C4A475817UL
    };

    private readonly byte[] _block = new byte[BlockSize];
    private readonly ulong[] _state = new ulong[8];
    private readonly ulong[] _schedule = new ulong[80];
    private int _blockLength;
    private ulong _messageLengthLowBytes;
    private ulong _messageLengthHighBytes;
    private bool _finalized;

    internal ManagedSha384()
    {
        Reset();
    }

    internal bool Append(ReadOnlySpan<byte> data)
    {
        if (_finalized) return false;
        ulong oldLow = _messageLengthLowBytes;
        ulong addition = (ulong)data.Length;
        ulong newLow = oldLow + addition;
        ulong carry = newLow < oldLow ? 1UL : 0UL;
        ulong oldHigh = _messageLengthHighBytes;
        ulong newHigh = oldHigh + carry;
        if (newHigh < oldHigh) return false;
        _messageLengthLowBytes = newLow;
        _messageLengthHighBytes = newHigh;

        while (!data.IsEmpty)
        {
            int copyLength = Math.Min(data.Length,
                                      BlockSize - _blockLength);
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
        if (_finalized || destination.Length < DigestSize ||
            (_messageLengthHighBytes >> 61) != 0)
            return false;

        ulong bitLengthHigh = (_messageLengthHighBytes << 3) |
                              (_messageLengthLowBytes >> 61);
        ulong bitLengthLow = _messageLengthLowBytes << 3;
        _block[_blockLength++] = 0x80;
        if (_blockLength > 112)
        {
            while (_blockLength != BlockSize) _block[_blockLength++] = 0;
            Compress(_block);
            _blockLength = 0;
        }
        while (_blockLength != 112) _block[_blockLength++] = 0;
        WriteUInt64(_block, 112, bitLengthHigh);
        WriteUInt64(_block, 120, bitLengthLow);
        Compress(_block);
        _blockLength = 0;

        for (int index = 0; index != 6; ++index)
            WriteUInt64(destination, index * 8, _state[index]);
        _block.AsSpan().Clear();
        _finalized = true;
        return true;
    }

    internal void Reset()
    {
        _state[0] = 0xCBBB9D5DC1059ED8UL;
        _state[1] = 0x629A292A367CD507UL;
        _state[2] = 0x9159015A3070DD17UL;
        _state[3] = 0x152FECD8F70E5939UL;
        _state[4] = 0x67332667FFC00B31UL;
        _state[5] = 0x8EB44A8768581511UL;
        _state[6] = 0xDB0C2E0D64F98FA7UL;
        _state[7] = 0x47B5481DBEFA4FA4UL;
        _block.AsSpan().Clear();
        _schedule.AsSpan().Clear();
        _blockLength = 0;
        _messageLengthLowBytes = 0;
        _messageLengthHighBytes = 0;
        _finalized = false;
    }

    internal static bool TryHash(ReadOnlySpan<byte> data, Span<byte> destination)
    {
        if (destination.Length < DigestSize) return false;
        ManagedSha384 hash = new();
        return hash.Append(data) && hash.TryFinalize(destination);
    }

    private void Compress(ReadOnlySpan<byte> block)
    {
        for (int index = 0; index != 16; ++index)
            _schedule[index] = ReadUInt64(block, index * 8);
        for (int index = 16; index != 80; ++index)
        {
            ulong lower = _schedule[index - 15];
            ulong upper = _schedule[index - 2];
            ulong sigma0 = RotateRight(lower, 1) ^ RotateRight(lower, 8) ^
                           (lower >> 7);
            ulong sigma1 = RotateRight(upper, 19) ^ RotateRight(upper, 61) ^
                           (upper >> 6);
            _schedule[index] = _schedule[index - 16] + sigma0 +
                               _schedule[index - 7] + sigma1;
        }

        ulong a = _state[0], b = _state[1], c = _state[2], d = _state[3];
        ulong e = _state[4], f = _state[5], g = _state[6], h = _state[7];
        for (int index = 0; index != 80; ++index)
        {
            ulong sigma1 = RotateRight(e, 14) ^ RotateRight(e, 18) ^
                           RotateRight(e, 41);
            ulong choice = (e & f) ^ (~e & g);
            ulong temporary1 = h + sigma1 + choice + RoundConstants[index] +
                               _schedule[index];
            ulong sigma0 = RotateRight(a, 28) ^ RotateRight(a, 34) ^
                           RotateRight(a, 39);
            ulong majority = (a & b) ^ (a & c) ^ (b & c);
            ulong temporary2 = sigma0 + majority;
            h = g; g = f; f = e; e = d + temporary1;
            d = c; c = b; b = a; a = temporary1 + temporary2;
        }
        _state[0] += a; _state[1] += b; _state[2] += c; _state[3] += d;
        _state[4] += e; _state[5] += f; _state[6] += g; _state[7] += h;
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> source, int offset)
    {
        return ((ulong)source[offset] << 56) |
               ((ulong)source[offset + 1] << 48) |
               ((ulong)source[offset + 2] << 40) |
               ((ulong)source[offset + 3] << 32) |
               ((ulong)source[offset + 4] << 24) |
               ((ulong)source[offset + 5] << 16) |
               ((ulong)source[offset + 6] << 8) | source[offset + 7];
    }

    private static void WriteUInt64(Span<byte> destination, int offset,
                                    ulong value)
    {
        destination[offset] = (byte)(value >> 56);
        destination[offset + 1] = (byte)(value >> 48);
        destination[offset + 2] = (byte)(value >> 40);
        destination[offset + 3] = (byte)(value >> 32);
        destination[offset + 4] = (byte)(value >> 24);
        destination[offset + 5] = (byte)(value >> 16);
        destination[offset + 6] = (byte)(value >> 8);
        destination[offset + 7] = (byte)value;
    }

    private static ulong RotateRight(ulong value, int count)
    {
        return (value >> count) | (value << (64 - count));
    }
}

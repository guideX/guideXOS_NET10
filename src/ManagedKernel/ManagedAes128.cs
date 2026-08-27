using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>
/// Portable AES-128 block encryption using the FIPS 197 byte-oriented
/// representation.  The S-box is computed with fixed-loop GF(2^8)
/// arithmetic, avoiding a managed lookup table.  Phase 27 proves functional
/// correctness and does not claim complete microarchitectural resistance.
/// </summary>
internal sealed class ManagedAes128
{
    internal const int KeySize = 16;
    internal const int BlockSize = 16;
    internal const int RoundKeySize = 176;

    /* Keep the expanded key as primitive-only inline state.  In addition to
       avoiding a per-instance array allocation, this keeps the key schedule
       out of the managed reference graph while a NativeAOT GC runs. */
    [StructLayout(LayoutKind.Sequential, Size = RoundKeySize)]
    private struct KeySchedule
    {
        internal ulong Word0;
        internal ulong Word1;
        internal ulong Word2;
        internal ulong Word3;
        internal ulong Word4;
        internal ulong Word5;
        internal ulong Word6;
        internal ulong Word7;
        internal ulong Word8;
        internal ulong Word9;
        internal ulong Word10;
        internal ulong Word11;
        internal ulong Word12;
        internal ulong Word13;
        internal ulong Word14;
        internal ulong Word15;
        internal ulong Word16;
        internal ulong Word17;
        internal ulong Word18;
        internal ulong Word19;
        internal ulong Word20;
        internal ulong Word21;
    }

    private KeySchedule _roundKeys;
    private bool _initialized;

    internal bool IsInitialized => _initialized;

    internal bool TrySetKey(ReadOnlySpan<byte> key)
    {
        Clear();
        if (key.Length != KeySize)
        {
            return false;
        }

        Span<byte> roundKeys = RoundKeys;
        key.CopyTo(roundKeys);
        int generated = KeySize;
        byte roundConstant = 1;
        Span<byte> temporary = stackalloc byte[4];
        while (generated < RoundKeySize)
        {
            roundKeys.Slice(generated - 4, 4).CopyTo(temporary);
            if (generated % KeySize == 0)
            {
                byte first = temporary[0];
                temporary[0] = SubByte(temporary[1]);
                temporary[1] = SubByte(temporary[2]);
                temporary[2] = SubByte(temporary[3]);
                temporary[3] = SubByte(first);
                temporary[0] ^= roundConstant;
                roundConstant = Xtime(roundConstant);
            }

            for (int index = 0; index != 4; ++index)
            {
                roundKeys[generated] = (byte)(roundKeys[generated - KeySize] ^ temporary[index]);
                generated++;
            }
        }
        temporary.Clear();
        _initialized = true;
        return true;
    }

    internal bool TryEncryptBlock(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (!_initialized || input.Length != BlockSize || output.Length < BlockSize)
        {
            return false;
        }

        Span<byte> state = stackalloc byte[BlockSize];
        input.CopyTo(state);
        AddRoundKey(state, 0);
        for (int round = 1; round != 10; ++round)
        {
            SubBytes(state);
            ShiftRows(state);
            MixColumns(state);
            AddRoundKey(state, round * BlockSize);
        }
        SubBytes(state);
        ShiftRows(state);
        AddRoundKey(state, 10 * BlockSize);
        state.CopyTo(output);
        state.Clear();
        return true;
    }

    internal void Reset() => Clear();

    internal void Clear()
    {
        RoundKeys.Clear();
        _initialized = false;
    }

    internal static bool TryEncrypt(ReadOnlySpan<byte> key,
                                    ReadOnlySpan<byte> input,
                                    Span<byte> output)
    {
        ManagedAes128 aes = new();
        try
        {
            return aes.TrySetKey(key) && aes.TryEncryptBlock(input, output);
        }
        finally
        {
            aes.Clear();
        }
    }

    private void AddRoundKey(Span<byte> state, int offset)
    {
        Span<byte> roundKeys = RoundKeys;
        for (int index = 0; index != BlockSize; ++index)
        {
            state[index] ^= roundKeys[offset + index];
        }
    }

    private Span<byte> RoundKeys =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(
            ref _roundKeys.Word0, 22));

    private static void SubBytes(Span<byte> state)
    {
        for (int index = 0; index != BlockSize; ++index)
        {
            state[index] = SubByte(state[index]);
        }
    }

    private static void ShiftRows(Span<byte> state)
    {
        byte row1 = state[1];
        state[1] = state[5];
        state[5] = state[9];
        state[9] = state[13];
        state[13] = row1;

        byte row2 = state[2];
        byte row6 = state[6];
        state[2] = state[10];
        state[6] = state[14];
        state[10] = row2;
        state[14] = row6;

        byte row3 = state[3];
        state[3] = state[15];
        state[15] = state[11];
        state[11] = state[7];
        state[7] = row3;
    }

    private static void MixColumns(Span<byte> state)
    {
        for (int offset = 0; offset != BlockSize; offset += 4)
        {
            byte a0 = state[offset];
            byte a1 = state[offset + 1];
            byte a2 = state[offset + 2];
            byte a3 = state[offset + 3];
            byte b0 = Xtime(a0);
            byte b1 = Xtime(a1);
            byte b2 = Xtime(a2);
            byte b3 = Xtime(a3);
            state[offset] = (byte)(b0 ^ (b1 ^ a1) ^ a2 ^ a3);
            state[offset + 1] = (byte)(a0 ^ b1 ^ (b2 ^ a2) ^ a3);
            state[offset + 2] = (byte)(a0 ^ a1 ^ b2 ^ (b3 ^ a3));
            state[offset + 3] = (byte)((b0 ^ a0) ^ a1 ^ a2 ^ b3);
        }
    }

    private static byte Xtime(byte value)
    {
        return (byte)((value << 1) ^ ((value >> 7) * 0x1B));
    }

    private static byte SubByte(byte value)
    {
        byte inverse = 1;
        byte power = value;
        int exponent = 0xFE;
        while (exponent != 0)
        {
            if ((exponent & 1) != 0)
            {
                inverse = GfMultiply(inverse, power);
            }
            power = GfMultiply(power, power);
            exponent >>= 1;
        }

        return (byte)(inverse ^ RotateLeft(inverse, 1) ^
                      RotateLeft(inverse, 2) ^ RotateLeft(inverse, 3) ^
                      RotateLeft(inverse, 4) ^ 0x63);
    }

    private static byte GfMultiply(byte left, byte right)
    {
        byte result = 0;
        for (int index = 0; index != 8; ++index)
        {
            byte rightMask = (byte)(0 - (right & 1));
            byte highMask = (byte)(0 - (left >> 7));
            result ^= (byte)(left & rightMask);
            left = (byte)((left << 1) ^ (0x1B & highMask));
            right >>= 1;
        }
        return result;
    }

    private static byte RotateLeft(byte value, int count) =>
        (byte)((value << count) | (value >> (8 - count)));

}

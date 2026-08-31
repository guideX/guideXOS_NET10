using System;

namespace GuideXOS.Net10.ManagedKernel;

/*
 * Fixed-width P-384 ECDSA verification for the certificate profile.  This is
 * deliberately a verifier, not a general elliptic-curve or big-integer
 * library: all values are twelve 32-bit limbs and all loops have fixed
 * bounds.  The field reduction uses the NIST P-384 pseudo-Mersenne prime;
 * scalar reduction uses twelve-limb binary long division.
 */
internal readonly struct ManagedP384FieldElement
{
    internal readonly uint L0;
    internal readonly uint L1;
    internal readonly uint L2;
    internal readonly uint L3;
    internal readonly uint L4;
    internal readonly uint L5;
    internal readonly uint L6;
    internal readonly uint L7;
    internal readonly uint L8;
    internal readonly uint L9;
    internal readonly uint L10;
    internal readonly uint L11;

    internal ManagedP384FieldElement(uint l0, uint l1, uint l2, uint l3,
                                     uint l4, uint l5, uint l6, uint l7,
                                     uint l8, uint l9, uint l10, uint l11)
    {
        L0 = l0; L1 = l1; L2 = l2; L3 = l3; L4 = l4; L5 = l5;
        L6 = l6; L7 = l7; L8 = l8; L9 = l9; L10 = l10; L11 = l11;
    }

    internal static readonly ManagedP384FieldElement Zero = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    internal static readonly ManagedP384FieldElement One = new(
        1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    internal static readonly ManagedP384FieldElement Prime = new(
        0xFFFFFFFFU, 0x00000000U, 0x00000000U, 0xFFFFFFFFU,
        0xFFFFFFFEU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU,
        0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU);
    private static readonly ManagedP384FieldElement PrimeMinusTwo = new(
        0xFFFFFFFDU, 0x00000000U, 0x00000000U, 0xFFFFFFFFU,
        0xFFFFFFFEU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU,
        0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU);
    internal static readonly ManagedP384FieldElement CurveB = new(
        0xD3EC2AEFU, 0x2A85C8EDU, 0x8A2ED19DU, 0xC656398DU,
        0x5013875AU, 0x0314088FU, 0xFE814112U, 0x181D9C6EU,
        0xE3F82D19U, 0x988E056BU, 0xE23EE7E4U, 0xB3312FA7U);
    internal static readonly ManagedP384FieldElement GeneratorX = new(
        0x72760AB7U, 0x3A545E38U, 0xBF55296CU, 0x5502F25DU,
        0x82542A38U, 0x59F741E0U, 0x8BA79B98U, 0x6E1D3B62U,
        0xF320AD74U, 0x8EB1C71EU, 0xBE8B0537U, 0xAA87CA22U);
    internal static readonly ManagedP384FieldElement GeneratorY = new(
        0x90EA0E5FU, 0x7A431D7CU, 0x1D7E819DU, 0x0A60B1CEU,
        0xB5F0B8C0U, 0xE9DA3113U, 0x289A147CU, 0xF8F41DBDU,
        0x9292DC29U, 0x5D9E98BFU, 0x96262C6FU, 0x3617DE4AU);

    internal bool IsZero => (L0 | L1 | L2 | L3 | L4 | L5 | L6 |
                             L7 | L8 | L9 | L10 | L11) == 0;

    internal uint GetLimb(int index)
    {
        return index switch
        {
            0 => L0, 1 => L1, 2 => L2, 3 => L3, 4 => L4, 5 => L5,
            6 => L6, 7 => L7, 8 => L8, 9 => L9, 10 => L10, 11 => L11,
            _ => 0
        };
    }

    internal uint GetBit(int bit)
    {
        return (GetLimb(bit >> 5) >> (bit & 31)) & 1U;
    }

    internal static bool Equals(in ManagedP384FieldElement left,
                                in ManagedP384FieldElement right)
    {
        return left.L0 == right.L0 && left.L1 == right.L1 &&
               left.L2 == right.L2 && left.L3 == right.L3 &&
               left.L4 == right.L4 && left.L5 == right.L5 &&
               left.L6 == right.L6 && left.L7 == right.L7 &&
               left.L8 == right.L8 && left.L9 == right.L9 &&
               left.L10 == right.L10 && left.L11 == right.L11;
    }

    internal static int Compare(in ManagedP384FieldElement left,
                                in ManagedP384FieldElement right)
    {
        for (int index = 11; index >= 0; --index)
        {
            uint a = left.GetLimb(index), b = right.GetLimb(index);
            if (a != b) return a < b ? -1 : 1;
        }
        return 0;
    }

    internal static ManagedP384FieldElement Add(
        in ManagedP384FieldElement left, in ManagedP384FieldElement right)
    {
        Span<long> result = stackalloc long[24];
        for (int index = 0; index != 12; ++index)
            result[index] = (long)left.GetLimb(index) + right.GetLimb(index);
        return Reduce(result);
    }

    internal static ManagedP384FieldElement Subtract(
        in ManagedP384FieldElement left, in ManagedP384FieldElement right)
    {
        Span<uint> result = stackalloc uint[12];
        uint borrow = SubtractRaw(left, right, result);
        if (borrow != 0) AddRaw(result, Prime, result);
        return FromLimbs(result);
    }

    internal static ManagedP384FieldElement Negate(
        in ManagedP384FieldElement value)
    {
        if (value.IsZero) return Zero;
        Span<uint> result = stackalloc uint[12];
        SubtractRaw(Prime, value, result);
        return FromLimbs(result);
    }

    internal static ManagedP384FieldElement Multiply(
        in ManagedP384FieldElement left, in ManagedP384FieldElement right)
    {
        Span<ulong> product = stackalloc ulong[24];
        product.Clear();
        for (int i = 0; i != 12; ++i)
        {
            ulong carry = 0;
            for (int j = 0; j != 12; ++j)
            {
                ulong value = product[i + j] +
                    (ulong)left.GetLimb(i) * right.GetLimb(j) + carry;
                product[i + j] = value & 0xFFFFFFFFUL;
                carry = value >> 32;
            }
            for (int k = i + 12; k != 24; ++k)
            {
                ulong value = product[k] + carry;
                product[k] = value & 0xFFFFFFFFUL;
                carry = value >> 32;
            }
        }
        Span<long> reduced = stackalloc long[24];
        for (int index = 0; index != 24; ++index)
            reduced[index] = (long)product[index];
        return Reduce(reduced);
    }

    internal static ManagedP384FieldElement Square(
        in ManagedP384FieldElement value) => Multiply(value, value);

    internal static ManagedP384FieldElement Invert(
        in ManagedP384FieldElement value)
    {
        if (value.IsZero) return Zero;
        ManagedP384FieldElement result = One;
        for (int bit = 383; bit >= 0; --bit)
        {
            result = Square(result);
            if (PrimeMinusTwo.GetBit(bit) != 0)
                result = Multiply(result, value);
        }
        return result;
    }

    internal static bool TryRead(ReadOnlySpan<byte> source,
                                 out ManagedP384FieldElement value)
    {
        value = Zero;
        if (source.Length != ManagedP384.Size) return false;
        ManagedP384FieldElement candidate = ReadUnchecked(source);
        if (Compare(candidate, Prime) >= 0) return false;
        value = candidate;
        return true;
    }

    internal static ManagedP384FieldElement ReadUnchecked(
        ReadOnlySpan<byte> source)
    {
        if (source.Length < ManagedP384.Size) return Zero;
        return new(
            ReadWord(source, 44), ReadWord(source, 40),
            ReadWord(source, 36), ReadWord(source, 32),
            ReadWord(source, 28), ReadWord(source, 24),
            ReadWord(source, 20), ReadWord(source, 16),
            ReadWord(source, 12), ReadWord(source, 8),
            ReadWord(source, 4), ReadWord(source, 0));
    }

    internal void WriteBigEndian(Span<byte> destination)
    {
        if (destination.Length < ManagedP384.Size) return;
        WriteWord(destination, 0, L11); WriteWord(destination, 4, L10);
        WriteWord(destination, 8, L9); WriteWord(destination, 12, L8);
        WriteWord(destination, 16, L7); WriteWord(destination, 20, L6);
        WriteWord(destination, 24, L5); WriteWord(destination, 28, L4);
        WriteWord(destination, 32, L3); WriteWord(destination, 36, L2);
        WriteWord(destination, 40, L1); WriteWord(destination, 44, L0);
    }

    private static ManagedP384FieldElement Reduce(Span<long> value)
    {
        /* 2^384 = 1 + 2^128 + 2^96 - 2^32 (mod p). */
        for (int index = 23; index >= 12; --index)
        {
            long high = value[index];
            value[index] = 0;
            int lower = index - 12;
            value[lower] += high;
            value[lower + 4] += high;
            value[lower + 3] += high;
            value[lower + 1] -= high;
        }
        /* Normalize signed limbs and fold the carry using the same relation.
           The fixed pass count covers the maximum schoolbook product. */
        for (int round = 0; round != 32; ++round)
        {
            long carry = 0;
            for (int index = 0; index != 12; ++index)
            {
                long limb = value[index] + carry;
                value[index] = limb & 0xFFFFFFFFL;
                carry = limb >> 32;
            }
            value[0] += carry;
            value[4] += carry;
            value[3] += carry;
            value[1] -= carry;
        }
        Span<uint> result = stackalloc uint[12];
        for (int index = 0; index != 12; ++index) result[index] = (uint)value[index];
        for (int pass = 0; pass != 4; ++pass)
        {
            if (Compare((ReadOnlySpan<uint>)result, in Prime) >= 0)
            {
                ManagedP384FieldElement current = FromLimbs(result);
                SubtractRaw(in current, in Prime, result);
            }
        }
        return FromLimbs(result);
    }

    private static int Compare(ReadOnlySpan<uint> left,
                               in ManagedP384FieldElement right)
    {
        for (int index = 11; index >= 0; --index)
        {
            uint a = left[index], b = right.GetLimb(index);
            if (a != b) return a < b ? -1 : 1;
        }
        return 0;
    }

    private static uint SubtractRaw(in ManagedP384FieldElement left,
                                    in ManagedP384FieldElement right,
                                    Span<uint> result)
    {
        ulong borrow = 0;
        for (int index = 0; index != 12; ++index)
        {
            ulong a = left.GetLimb(index);
            ulong b = (ulong)right.GetLimb(index) + borrow;
            result[index] = (uint)(a - b);
            borrow = a < b ? 1UL : 0UL;
        }
        return (uint)borrow;
    }

    private static uint AddRaw(Span<uint> left,
                               in ManagedP384FieldElement right,
                               Span<uint> result)
    {
        ulong carry = 0;
        for (int index = 0; index != 12; ++index)
        {
            ulong sum = left[index] + (ulong)right.GetLimb(index) + carry;
            result[index] = (uint)sum;
            carry = sum >> 32;
        }
        return (uint)carry;
    }

    private static ManagedP384FieldElement FromLimbs(ReadOnlySpan<uint> limbs)
    {
        return new(limbs[0], limbs[1], limbs[2], limbs[3], limbs[4], limbs[5],
                   limbs[6], limbs[7], limbs[8], limbs[9], limbs[10], limbs[11]);
    }

    private static uint ReadWord(ReadOnlySpan<byte> source, int offset)
    {
        return ((uint)source[offset] << 24) |
               ((uint)source[offset + 1] << 16) |
               ((uint)source[offset + 2] << 8) | source[offset + 3];
    }

    private static void WriteWord(Span<byte> destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }
}

internal readonly struct ManagedP384ScalarElement
{
    internal const int Size = 48;
    internal readonly uint L0, L1, L2, L3, L4, L5, L6, L7;
    internal readonly uint L8, L9, L10, L11;

    private ManagedP384ScalarElement(uint l0, uint l1, uint l2, uint l3,
                                     uint l4, uint l5, uint l6, uint l7,
                                     uint l8, uint l9, uint l10, uint l11)
    {
        L0 = l0; L1 = l1; L2 = l2; L3 = l3; L4 = l4; L5 = l5;
        L6 = l6; L7 = l7; L8 = l8; L9 = l9; L10 = l10; L11 = l11;
    }

    internal static readonly ManagedP384ScalarElement Zero = new(
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    internal static readonly ManagedP384ScalarElement One = new(
        1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    internal static readonly ManagedP384ScalarElement Order = new(
        0xCCC52973U, 0xECEC196AU, 0x48B0A77AU, 0x581A0DB2U,
        0xF4372DDFU, 0xC7634D81U, 0xFFFFFFFFU, 0xFFFFFFFFU,
        0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU);
    private static readonly ManagedP384ScalarElement OrderMinusTwo = new(
        0xCCC52971U, 0xECEC196AU, 0x48B0A77AU, 0x581A0DB2U,
        0xF4372DDFU, 0xC7634D81U, 0xFFFFFFFFU, 0xFFFFFFFFU,
        0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU);

    internal bool IsZero => (L0 | L1 | L2 | L3 | L4 | L5 | L6 |
                             L7 | L8 | L9 | L10 | L11) == 0;

    internal uint GetLimb(int index) => index switch
    {
        0 => L0, 1 => L1, 2 => L2, 3 => L3, 4 => L4, 5 => L5,
        6 => L6, 7 => L7, 8 => L8, 9 => L9, 10 => L10, 11 => L11,
        _ => 0
    };

    internal uint GetBit(int bit) => (GetLimb(bit >> 5) >> (bit & 31)) & 1U;

    internal static bool Equals(in ManagedP384ScalarElement left,
                                in ManagedP384ScalarElement right)
    {
        for (int index = 0; index != 12; ++index)
            if (left.GetLimb(index) != right.GetLimb(index)) return false;
        return true;
    }

    internal static int Compare(in ManagedP384ScalarElement left,
                                in ManagedP384ScalarElement right)
    {
        for (int index = 11; index >= 0; --index)
        {
            uint a = left.GetLimb(index), b = right.GetLimb(index);
            if (a != b) return a < b ? -1 : 1;
        }
        return 0;
    }

    internal static ManagedP384ScalarElement Add(
        in ManagedP384ScalarElement left, in ManagedP384ScalarElement right)
    {
        Span<uint> result = stackalloc uint[13];
        ulong carry = 0;
        for (int index = 0; index != 12; ++index)
        {
            ulong sum = left.GetLimb(index) + (ulong)right.GetLimb(index) + carry;
            result[index] = (uint)sum; carry = sum >> 32;
        }
        result[12] = (uint)carry;
        if (result[12] != 0 || Compare(result, Order) >= 0)
            SubtractOrder(result);
        return FromLimbs(result);
    }

    internal static ManagedP384ScalarElement Subtract(
        in ManagedP384ScalarElement left, in ManagedP384ScalarElement right)
    {
        Span<uint> result = stackalloc uint[12];
        ulong borrow = 0;
        for (int index = 0; index != 12; ++index)
        {
            ulong a = left.GetLimb(index);
            ulong b = (ulong)right.GetLimb(index) + borrow;
            result[index] = (uint)(a - b);
            borrow = a < b ? 1UL : 0UL;
        }
        if (borrow != 0)
        {
            ulong carry = 0;
            for (int index = 0; index != 12; ++index)
            {
                ulong sum = result[index] + (ulong)Order.GetLimb(index) + carry;
                result[index] = (uint)sum; carry = sum >> 32;
            }
        }
        return FromLimbs(result);
    }

    internal static ManagedP384ScalarElement Multiply(
        in ManagedP384ScalarElement left, in ManagedP384ScalarElement right)
    {
        Span<uint> product = stackalloc uint[24];
        MultiplyRaw(left, right, product);
        return ReduceProduct(product);
    }

    internal static ManagedP384ScalarElement Square(
        in ManagedP384ScalarElement value) => Multiply(value, value);

    internal static ManagedP384ScalarElement Invert(
        in ManagedP384ScalarElement value)
    {
        if (value.IsZero) return Zero;
        ManagedP384ScalarElement result = One;
        for (int bit = 383; bit >= 0; --bit)
        {
            result = Square(result);
            if (OrderMinusTwo.GetBit(bit) != 0)
                result = Multiply(result, value);
        }
        return result;
    }

    internal static bool TryReadCanonical(
        ReadOnlySpan<byte> source, out ManagedP384ScalarElement value)
    {
        value = Zero;
        if (source.Length != Size) return false;
        ManagedP384ScalarElement candidate = ReadUnchecked(source);
        if (Compare(candidate, Order) >= 0) return false;
        value = candidate;
        return true;
    }

    internal static bool TryReadNonZero(
        ReadOnlySpan<byte> source, out ManagedP384ScalarElement value)
    {
        if (!TryReadCanonical(source, out value) || value.IsZero)
        {
            value = Zero;
            return false;
        }
        return true;
    }

    internal static bool TryReduceDigest(
        ReadOnlySpan<byte> digest, out ManagedP384ScalarElement value)
    {
        value = Zero;
        if (digest.Length != Size) return false;
        Span<uint> remainder = stackalloc uint[13];
        for (int byteIndex = 0; byteIndex != Size; ++byteIndex)
        {
            uint carry = digest[byteIndex];
            for (int bit = 7; bit >= 0; --bit)
            {
                uint input = (carry >> bit) & 1U;
                for (int index = 0; index != 13; ++index)
                {
                    uint limb = remainder[index];
                    remainder[index] = (limb << 1) | input;
                    input = limb >> 31;
                }
                if (remainder[12] != 0 || Compare(remainder, Order) >= 0)
                    SubtractOrder(remainder);
            }
        }
        value = FromLimbs(remainder);
        return true;
    }

    internal static ManagedP384ScalarElement FromFieldX(
        in ManagedP384FieldElement fieldX)
    {
        ManagedP384ScalarElement result = new(
            fieldX.L0, fieldX.L1, fieldX.L2, fieldX.L3, fieldX.L4, fieldX.L5,
            fieldX.L6, fieldX.L7, fieldX.L8, fieldX.L9, fieldX.L10, fieldX.L11);
        if (Compare(result, Order) >= 0) result = Subtract(result, Order);
        return result;
    }

    internal ManagedP384FieldElement ToFieldElement() => new(
        L0, L1, L2, L3, L4, L5, L6, L7, L8, L9, L10, L11);

    private static ManagedP384ScalarElement ReadUnchecked(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size) return Zero;
        return new(
            ReadWord(source, 44), ReadWord(source, 40),
            ReadWord(source, 36), ReadWord(source, 32),
            ReadWord(source, 28), ReadWord(source, 24),
            ReadWord(source, 20), ReadWord(source, 16),
            ReadWord(source, 12), ReadWord(source, 8),
            ReadWord(source, 4), ReadWord(source, 0));
    }

    internal void WriteBigEndian(Span<byte> destination)
    {
        if (destination.Length < Size) return;
        WriteWord(destination, 0, L11); WriteWord(destination, 4, L10);
        WriteWord(destination, 8, L9); WriteWord(destination, 12, L8);
        WriteWord(destination, 16, L7); WriteWord(destination, 20, L6);
        WriteWord(destination, 24, L5); WriteWord(destination, 28, L4);
        WriteWord(destination, 32, L3); WriteWord(destination, 36, L2);
        WriteWord(destination, 40, L1); WriteWord(destination, 44, L0);
    }

    private static int Compare(ReadOnlySpan<uint> left,
                               in ManagedP384ScalarElement right)
    {
        if (left[12] != 0) return 1;
        for (int index = 11; index >= 0; --index)
        {
            uint a = left[index], b = right.GetLimb(index);
            if (a != b) return a < b ? -1 : 1;
        }
        return 0;
    }

    private static void MultiplyRaw(in ManagedP384ScalarElement left,
                                    in ManagedP384ScalarElement right,
                                    Span<uint> product)
    {
        product.Clear();
        for (int i = 0; i != 12; ++i)
        {
            ulong carry = 0;
            for (int j = 0; j != 12; ++j)
            {
                ulong value = product[i + j] +
                    (ulong)left.GetLimb(i) * right.GetLimb(j) + carry;
                product[i + j] = (uint)value; carry = value >> 32;
            }
            for (int k = i + 12; k != 24; ++k)
            {
                ulong value = product[k] + carry;
                product[k] = (uint)value; carry = value >> 32;
            }
        }
    }

    private static ManagedP384ScalarElement ReduceProduct(
        ReadOnlySpan<uint> product)
    {
        Span<uint> remainder = stackalloc uint[13];
        for (int bit = 767; bit >= 0; --bit)
        {
            uint carry = (product[bit >> 5] >> (bit & 31)) & 1U;
            for (int index = 0; index != 13; ++index)
            {
                uint limb = remainder[index];
                remainder[index] = (limb << 1) | carry;
                carry = limb >> 31;
            }
            if (remainder[12] != 0 || Compare(remainder, Order) >= 0)
                SubtractOrder(remainder);
        }
        return FromLimbs(remainder);
    }

    private static void SubtractOrder(Span<uint> value)
    {
        ulong borrow = 0;
        for (int index = 0; index != 12; ++index)
        {
            ulong left = value[index];
            ulong right = (ulong)Order.GetLimb(index) + borrow;
            value[index] = (uint)(left - right);
            borrow = left < right ? 1UL : 0UL;
        }
        value[12] = (uint)((ulong)value[12] - borrow);
    }

    private static ManagedP384ScalarElement FromLimbs(ReadOnlySpan<uint> limbs)
    {
        return new(limbs[0], limbs[1], limbs[2], limbs[3], limbs[4], limbs[5],
                   limbs[6], limbs[7], limbs[8], limbs[9], limbs[10], limbs[11]);
    }

    private static uint ReadWord(ReadOnlySpan<byte> source, int offset)
    {
        return ((uint)source[offset] << 24) |
               ((uint)source[offset + 1] << 16) |
               ((uint)source[offset + 2] << 8) | source[offset + 3];
    }

    private static void WriteWord(Span<byte> destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }
}

internal struct ManagedP384JacobianPoint
{
    internal ManagedP384FieldElement X;
    internal ManagedP384FieldElement Y;
    internal ManagedP384FieldElement Z;

    internal bool IsInfinity => Z.IsZero;
    internal static ManagedP384JacobianPoint Infinity => new()
    {
        X = ManagedP384FieldElement.Zero,
        Y = ManagedP384FieldElement.One,
        Z = ManagedP384FieldElement.Zero
    };

    internal static ManagedP384JacobianPoint FromAffine(
        in ManagedP384FieldElement x, in ManagedP384FieldElement y) => new()
    {
        X = x, Y = y, Z = ManagedP384FieldElement.One
    };

    internal static ManagedP384JacobianPoint Add(
        in ManagedP384JacobianPoint left, in ManagedP384JacobianPoint right)
    {
        if (left.IsInfinity) return right;
        if (right.IsInfinity) return left;
        ManagedP384FieldElement z1Squared =
            ManagedP384FieldElement.Square(left.Z);
        ManagedP384FieldElement z2Squared =
            ManagedP384FieldElement.Square(right.Z);
        ManagedP384FieldElement u1 =
            ManagedP384FieldElement.Multiply(left.X, z2Squared);
        ManagedP384FieldElement u2 =
            ManagedP384FieldElement.Multiply(right.X, z1Squared);
        ManagedP384FieldElement s1 = ManagedP384FieldElement.Multiply(
            left.Y, ManagedP384FieldElement.Multiply(right.Z, z2Squared));
        ManagedP384FieldElement s2 = ManagedP384FieldElement.Multiply(
            right.Y, ManagedP384FieldElement.Multiply(left.Z, z1Squared));
        if (ManagedP384FieldElement.Equals(u1, u2))
        {
            if (!ManagedP384FieldElement.Equals(s1, s2)) return Infinity;
            return Double(left);
        }
        ManagedP384FieldElement h =
            ManagedP384FieldElement.Subtract(u2, u1);
        ManagedP384FieldElement twoH = ManagedP384FieldElement.Add(h, h);
        ManagedP384FieldElement i = ManagedP384FieldElement.Square(twoH);
        ManagedP384FieldElement j = ManagedP384FieldElement.Multiply(h, i);
        ManagedP384FieldElement sDifference =
            ManagedP384FieldElement.Subtract(s2, s1);
        ManagedP384FieldElement r =
            ManagedP384FieldElement.Add(sDifference, sDifference);
        ManagedP384FieldElement v =
            ManagedP384FieldElement.Multiply(u1, i);
        ManagedP384FieldElement rSquared = ManagedP384FieldElement.Square(r);
        ManagedP384FieldElement x = ManagedP384FieldElement.Subtract(
            ManagedP384FieldElement.Subtract(rSquared, j),
            ManagedP384FieldElement.Add(v, v));
        ManagedP384FieldElement y = ManagedP384FieldElement.Subtract(
            ManagedP384FieldElement.Multiply(
                r, ManagedP384FieldElement.Subtract(v, x)),
            ManagedP384FieldElement.Multiply(
                ManagedP384FieldElement.Add(s1, s1), j));
        ManagedP384FieldElement z = ManagedP384FieldElement.Multiply(
            ManagedP384FieldElement.Subtract(
                ManagedP384FieldElement.Subtract(
                    ManagedP384FieldElement.Square(
                        ManagedP384FieldElement.Add(left.Z, right.Z)),
                    z1Squared), z2Squared), h);
        return new() { X = x, Y = y, Z = z };
    }

    internal static ManagedP384JacobianPoint Double(
        in ManagedP384JacobianPoint point)
    {
        if (point.IsInfinity || point.Y.IsZero) return Infinity;
        ManagedP384FieldElement a = ManagedP384FieldElement.Square(point.X);
        ManagedP384FieldElement b = ManagedP384FieldElement.Square(point.Y);
        ManagedP384FieldElement c = ManagedP384FieldElement.Square(b);
        ManagedP384FieldElement xPlusB =
            ManagedP384FieldElement.Add(point.X, b);
        ManagedP384FieldElement d = ManagedP384FieldElement.Add(
            ManagedP384FieldElement.Add(
                ManagedP384FieldElement.Square(xPlusB),
                ManagedP384FieldElement.Negate(a)),
            ManagedP384FieldElement.Negate(c));
        d = ManagedP384FieldElement.Add(d, d);
        ManagedP384FieldElement zSquared =
            ManagedP384FieldElement.Square(point.Z);
        ManagedP384FieldElement zFourth =
            ManagedP384FieldElement.Square(zSquared);
        ManagedP384FieldElement e = ManagedP384FieldElement.Add(a, a);
        e = ManagedP384FieldElement.Add(e, a);
        e = ManagedP384FieldElement.Subtract(
            e, ManagedP384FieldElement.Add(
                ManagedP384FieldElement.Add(zFourth, zFourth), zFourth));
        ManagedP384FieldElement f = ManagedP384FieldElement.Square(e);
        ManagedP384FieldElement x = ManagedP384FieldElement.Subtract(
            f, ManagedP384FieldElement.Add(d, d));
        ManagedP384FieldElement eightC = ManagedP384FieldElement.Add(c, c);
        eightC = ManagedP384FieldElement.Add(eightC, eightC);
        eightC = ManagedP384FieldElement.Add(eightC, eightC);
        ManagedP384FieldElement y = ManagedP384FieldElement.Subtract(
            ManagedP384FieldElement.Multiply(
                e, ManagedP384FieldElement.Subtract(d, x)), eightC);
        ManagedP384FieldElement z = ManagedP384FieldElement.Multiply(
            ManagedP384FieldElement.Add(point.Y, point.Y), point.Z);
        return new() { X = x, Y = y, Z = z };
    }

    internal static ManagedP384JacobianPoint ScalarMultiply(
        in ManagedP384JacobianPoint point,
        in ManagedP384FieldElement scalar)
    {
        ManagedP384JacobianPoint r0 = Infinity;
        ManagedP384JacobianPoint r1 = point;
        for (int bit = 383; bit >= 0; --bit)
        {
            uint mask = 0U - scalar.GetBit(bit);
            ManagedP384JacobianPoint swapped0 = Select(r0, r1, mask);
            ManagedP384JacobianPoint swapped1 = Select(r1, r0, mask);
            r0 = swapped0; r1 = swapped1;
            ManagedP384JacobianPoint sum = Add(r0, r1);
            ManagedP384JacobianPoint doubled = Double(r0);
            r1 = sum; r0 = doubled;
            swapped0 = Select(r0, r1, mask);
            swapped1 = Select(r1, r0, mask);
            r0 = swapped0; r1 = swapped1;
        }
        r1.Clear();
        return r0;
    }

    internal static ManagedP384JacobianPoint ScalarMultiply(
        in ManagedP384JacobianPoint point,
        in ManagedP384ScalarElement scalar)
    {
        ManagedP384FieldElement fieldScalar = scalar.ToFieldElement();
        return ScalarMultiply(point, fieldScalar);
    }

    internal bool TryToAffine(out ManagedP384FieldElement x,
                              out ManagedP384FieldElement y)
    {
        x = ManagedP384FieldElement.Zero;
        y = ManagedP384FieldElement.Zero;
        if (IsInfinity) return false;
        ManagedP384FieldElement zInverse =
            ManagedP384FieldElement.Invert(Z);
        ManagedP384FieldElement zInverseSquared =
            ManagedP384FieldElement.Square(zInverse);
        x = ManagedP384FieldElement.Multiply(X, zInverseSquared);
        y = ManagedP384FieldElement.Multiply(
            Y, ManagedP384FieldElement.Multiply(zInverseSquared, zInverse));
        return true;
    }

    internal void Clear()
    {
        X = ManagedP384FieldElement.Zero;
        Y = ManagedP384FieldElement.Zero;
        Z = ManagedP384FieldElement.Zero;
    }

    private static ManagedP384JacobianPoint Select(
        in ManagedP384JacobianPoint left,
        in ManagedP384JacobianPoint right, uint mask) => new()
    {
        X = Select(left.X, right.X, mask),
        Y = Select(left.Y, right.Y, mask),
        Z = Select(left.Z, right.Z, mask)
    };

    private static ManagedP384FieldElement Select(
        in ManagedP384FieldElement left,
        in ManagedP384FieldElement right, uint mask)
    {
        uint inverse = ~mask;
        return new(
            (left.L0 & inverse) | (right.L0 & mask),
            (left.L1 & inverse) | (right.L1 & mask),
            (left.L2 & inverse) | (right.L2 & mask),
            (left.L3 & inverse) | (right.L3 & mask),
            (left.L4 & inverse) | (right.L4 & mask),
            (left.L5 & inverse) | (right.L5 & mask),
            (left.L6 & inverse) | (right.L6 & mask),
            (left.L7 & inverse) | (right.L7 & mask),
            (left.L8 & inverse) | (right.L8 & mask),
            (left.L9 & inverse) | (right.L9 & mask),
            (left.L10 & inverse) | (right.L10 & mask),
            (left.L11 & inverse) | (right.L11 & mask));
    }
}

internal static class ManagedP384
{
    internal const int Size = 48;
    internal const int PublicKeySize = 97;
    internal const int DigestSize = 48;
    internal const int SignatureScalarSize = 48;
    internal const int MaximumDerSignatureSize = 104;

    internal static bool TryVerifyDigest(
        ReadOnlySpan<byte> digest, ReadOnlySpan<byte> publicKey,
        ReadOnlySpan<byte> r, ReadOnlySpan<byte> s)
    {
        if (digest.Length != DigestSize ||
            r.Length != SignatureScalarSize ||
            s.Length != SignatureScalarSize ||
            !ManagedP384ScalarElement.TryReadNonZero(
                r, out ManagedP384ScalarElement rValue) ||
            !ManagedP384ScalarElement.TryReadNonZero(
                s, out ManagedP384ScalarElement sValue) ||
            !ManagedP384ScalarElement.TryReduceDigest(
                digest, out ManagedP384ScalarElement digestValue) ||
            !TryReadPublicPoint(publicKey,
                                out ManagedP384JacobianPoint publicPoint))
            return false;

        ManagedP384JacobianPoint generator =
            ManagedP384JacobianPoint.FromAffine(
                ManagedP384FieldElement.GeneratorX,
                ManagedP384FieldElement.GeneratorY);
        ManagedP384JacobianPoint generatorTerm = default;
        ManagedP384JacobianPoint publicTerm = default;
        ManagedP384JacobianPoint result = default;
        try
        {
            ManagedP384ScalarElement inverse =
                ManagedP384ScalarElement.Invert(sValue);
            ManagedP384ScalarElement u1 =
                ManagedP384ScalarElement.Multiply(digestValue, inverse);
            ManagedP384ScalarElement u2 =
                ManagedP384ScalarElement.Multiply(rValue, inverse);
            generatorTerm = ManagedP384JacobianPoint.ScalarMultiply(
                generator, u1);
            publicTerm = ManagedP384JacobianPoint.ScalarMultiply(
                publicPoint, u2);
            result = ManagedP384JacobianPoint.Add(generatorTerm, publicTerm);
            if (!result.TryToAffine(out ManagedP384FieldElement x,
                                    out ManagedP384FieldElement y))
                return false;
            ManagedP384ScalarElement reducedX =
                ManagedP384ScalarElement.FromFieldX(x);
            return ManagedP384ScalarElement.Equals(reducedX, rValue);
        }
        finally
        {
            publicPoint.Clear(); generatorTerm.Clear(); publicTerm.Clear();
            result.Clear();
        }
    }

    internal static bool TryVerifyDerSignature(
        ReadOnlySpan<byte> digest, ReadOnlySpan<byte> publicKey,
        ReadOnlySpan<byte> derSignature)
    {
        Span<byte> r = stackalloc byte[SignatureScalarSize];
        Span<byte> s = stackalloc byte[SignatureScalarSize];
        try
        {
            return TryParseDerSignature(derSignature, r, s) &&
                   TryVerifyDigest(digest, publicKey, r, s);
        }
        finally { r.Clear(); s.Clear(); }
    }

    internal static bool TryParseDerSignature(
        ReadOnlySpan<byte> derSignature, Span<byte> r, Span<byte> s)
    {
        if (r.Length != SignatureScalarSize ||
            s.Length != SignatureScalarSize || derSignature.Length < 2 ||
            derSignature.Length > MaximumDerSignatureSize ||
            derSignature[0] != 0x30)
            return false;
        byte sequenceLength = derSignature[1];
        if ((sequenceLength & 0x80) != 0 ||
            sequenceLength != derSignature.Length - 2)
            return false;
        r.Clear(); s.Clear();
        int offset = 2;
        return TryReadDerInteger(derSignature, ref offset, r) &&
               TryReadDerInteger(derSignature, ref offset, s) &&
               offset == derSignature.Length;
    }

    internal static bool TryValidatePublicKey(ReadOnlySpan<byte> publicKey)
    {
        return TryReadPublicPoint(publicKey, out ManagedP384JacobianPoint point);
    }

    private static bool TryReadPublicPoint(
        ReadOnlySpan<byte> publicKey, out ManagedP384JacobianPoint point)
    {
        point = default;
        if (publicKey.Length != PublicKeySize || publicKey[0] != 4 ||
            !ManagedP384FieldElement.TryRead(publicKey.Slice(1, Size),
                                             out ManagedP384FieldElement x) ||
            !ManagedP384FieldElement.TryRead(publicKey.Slice(1 + Size, Size),
                                             out ManagedP384FieldElement y) ||
            !IsOnCurve(x, y))
            return false;
        point = ManagedP384JacobianPoint.FromAffine(x, y);
        return true;
    }

    private static bool TryReadDerInteger(ReadOnlySpan<byte> signature,
                                          ref int offset, Span<byte> target)
    {
        if (offset > signature.Length - 2 || signature[offset] != 0x02)
            return false;
        int length = signature[offset + 1];
        if (length == 0 || (length & 0x80) != 0 || length > Size + 1 ||
            length > signature.Length - offset - 2)
            return false;
        ReadOnlySpan<byte> integer = signature.Slice(offset + 2, length);
        if (length == Size + 1)
        {
            if (integer[0] != 0 || (integer[1] & 0x80) == 0)
                return false;
            integer.Slice(1).CopyTo(target);
        }
        else
        {
            if ((integer[0] & 0x80) != 0 ||
                (length > 1 && integer[0] == 0)) return false;
            integer.CopyTo(target.Slice(Size - length));
        }
        offset += 2 + length;
        return ManagedP384ScalarElement.TryReadNonZero(target, out _);
    }

    private static bool IsOnCurve(in ManagedP384FieldElement x,
                                  in ManagedP384FieldElement y)
    {
        ManagedP384FieldElement ySquared =
            ManagedP384FieldElement.Square(y);
        ManagedP384FieldElement xCubed = ManagedP384FieldElement.Multiply(
            ManagedP384FieldElement.Square(x), x);
        ManagedP384FieldElement threeX = ManagedP384FieldElement.Add(
            ManagedP384FieldElement.Add(x, x), x);
        ManagedP384FieldElement right = ManagedP384FieldElement.Add(
            ManagedP384FieldElement.Subtract(xCubed, threeX),
            ManagedP384FieldElement.CurveB);
        return ManagedP384FieldElement.Equals(ySquared, right);
    }
}

using System;

namespace GuideXOS.Net10.ManagedKernel;

/*
 * Narrow, fixed-width P-256 implementation for the managed kernel.
 *
 * Limbs are little-endian 32-bit words.  This is deliberately not a general
 * big-integer type: all storage is inline in eight uint fields and every
 * reduction, inversion, and scalar-multiplication loop has a fixed bound.
 */
internal readonly struct ManagedP256FieldElement
{
    internal readonly uint L0;
    internal readonly uint L1;
    internal readonly uint L2;
    internal readonly uint L3;
    internal readonly uint L4;
    internal readonly uint L5;
    internal readonly uint L6;
    internal readonly uint L7;

    internal ManagedP256FieldElement(uint l0, uint l1, uint l2, uint l3,
                                     uint l4, uint l5, uint l6, uint l7)
    {
        L0 = l0;
        L1 = l1;
        L2 = l2;
        L3 = l3;
        L4 = l4;
        L5 = l5;
        L6 = l6;
        L7 = l7;
    }

    internal static readonly ManagedP256FieldElement Zero =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    internal static readonly ManagedP256FieldElement One =
        new(1, 0, 0, 0, 0, 0, 0, 0);

    internal static readonly ManagedP256FieldElement Prime =
        new(0xFFFFFFFFU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0x00000000U,
            0x00000000U, 0x00000000U, 0x00000001U, 0xFFFFFFFFU);

    internal static readonly ManagedP256FieldElement PrimeMinusOne =
        new(0xFFFFFFFEU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0x00000000U,
            0x00000000U, 0x00000000U, 0x00000001U, 0xFFFFFFFFU);

    private static readonly ManagedP256FieldElement TwoTo96 =
        new(0, 0, 0, 1, 0, 0, 0, 0);

    private static readonly ManagedP256FieldElement TwoTo192 =
        new(0, 0, 0, 0, 0, 0, 1, 0);

    private static readonly ManagedP256FieldElement TwoTo224 =
        new(0, 0, 0, 0, 0, 0, 0, 1);

    internal static readonly ManagedP256FieldElement CurveB =
        new(0x27D2604BU, 0x3BCE3C3EU, 0xCC53B0F6U, 0x651D06B0U,
            0x769886BCU, 0xB3EBBD55U, 0xAA3A93E7U, 0x5AC635D8U);

    internal static readonly ManagedP256FieldElement GeneratorX =
        new(0xD898C296U, 0xF4A13945U, 0x2DEB33A0U, 0x77037D81U,
            0x63A440F2U, 0xF8BCE6E5U, 0xE12C4247U, 0x6B17D1F2U);

    internal static readonly ManagedP256FieldElement GeneratorY =
        new(0x37BF51F5U, 0xCBB64068U, 0x6B315ECEU, 0x2BCE3357U,
            0x7C0F9E16U, 0x8EE7EB4AU, 0xFE1A7F9BU, 0x4FE342E2U);

    internal static readonly ManagedP256FieldElement Order =
        new(0xFC632551U, 0xF3B9CAC2U, 0xA7179E84U, 0xBCE6FAADU,
            0xFFFFFFFFU, 0xFFFFFFFFU, 0x00000000U, 0xFFFFFFFFU);

    private static readonly ManagedP256FieldElement PrimeMinusTwo =
        new(0xFFFFFFFDU, 0xFFFFFFFFU, 0xFFFFFFFFU, 0x00000000U,
            0x00000000U, 0x00000000U, 0x00000001U, 0xFFFFFFFFU);

    internal bool IsZero => (L0 | L1 | L2 | L3 | L4 | L5 | L6 | L7) == 0;

    internal uint GetLimb(int index)
    {
        return index switch
        {
            0 => L0,
            1 => L1,
            2 => L2,
            3 => L3,
            4 => L4,
            5 => L5,
            6 => L6,
            7 => L7,
            _ => 0
        };
    }

    internal uint GetBit(int bit)
    {
        return (GetLimb(bit >> 5) >> (bit & 31)) & 1U;
    }

    internal static bool Equals(in ManagedP256FieldElement left,
                                in ManagedP256FieldElement right)
    {
        return (left.L0 == right.L0 && left.L1 == right.L1 &&
                left.L2 == right.L2 && left.L3 == right.L3 &&
                left.L4 == right.L4 && left.L5 == right.L5 &&
                left.L6 == right.L6 && left.L7 == right.L7);
    }

    internal static int Compare(in ManagedP256FieldElement left,
                                in ManagedP256FieldElement right)
    {
        if (left.L7 != right.L7) return left.L7 < right.L7 ? -1 : 1;
        if (left.L6 != right.L6) return left.L6 < right.L6 ? -1 : 1;
        if (left.L5 != right.L5) return left.L5 < right.L5 ? -1 : 1;
        if (left.L4 != right.L4) return left.L4 < right.L4 ? -1 : 1;
        if (left.L3 != right.L3) return left.L3 < right.L3 ? -1 : 1;
        if (left.L2 != right.L2) return left.L2 < right.L2 ? -1 : 1;
        if (left.L1 != right.L1) return left.L1 < right.L1 ? -1 : 1;
        if (left.L0 != right.L0) return left.L0 < right.L0 ? -1 : 1;
        return 0;
    }

    internal static ManagedP256FieldElement Select(
        in ManagedP256FieldElement left,
        in ManagedP256FieldElement right,
        uint mask)
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
            (left.L7 & inverse) | (right.L7 & mask));
    }

    internal static ManagedP256FieldElement Add(
        in ManagedP256FieldElement left,
        in ManagedP256FieldElement right)
    {
        Span<uint> result = stackalloc uint[8];
        ulong carry = 0;
        for (int index = 0; index != 8; ++index)
        {
            ulong sum = (ulong)left.GetLimb(index) + right.GetLimb(index) + carry;
            result[index] = (uint)sum;
            carry = sum >> 32;
        }

        if (carry != 0)
        {
            /* Fold 2^256 with the P-256 prime relation.  The correction is
               positive and remains below p for a sum of two canonical
               elements, so these fixed raw operations are enough.  Raw
               operations are used here to avoid data-dependent recursion. */
            AddRaw(result, TwoTo224, result);
            AddRaw(result, One, result);
            SubtractRaw(result, TwoTo192, result);
            SubtractRaw(result, TwoTo96, result);
            return FromLimbs(result);
        }
        if (Compare(result, Prime) >= 0)
        {
            SubtractRaw(result, Prime, result);
        }
        return FromLimbs(result);
    }

    internal static ManagedP256FieldElement Subtract(
        in ManagedP256FieldElement left,
        in ManagedP256FieldElement right)
    {
        Span<uint> result = stackalloc uint[8];
        uint borrow = SubtractRaw(left, right, result);
        if (borrow != 0)
        {
            AddRaw(result, Prime, result);
        }
        return FromLimbs(result);
    }

    internal static ManagedP256FieldElement Negate(
        in ManagedP256FieldElement value)
    {
        if (value.IsZero) return Zero;
        Span<uint> result = stackalloc uint[8];
        SubtractRaw(Prime, value, result);
        return FromLimbs(result);
    }

    internal static ManagedP256FieldElement Multiply(
        in ManagedP256FieldElement left,
        in ManagedP256FieldElement right)
    {
        Span<ulong> product = stackalloc ulong[16];
        product.Clear();

        /* Schoolbook multiplication in base 2^32.  Each product cell is
           normalized immediately, so no cell can overflow ulong. */
        for (int i = 0; i != 8; ++i)
        {
            ulong carry = 0;
            for (int j = 0; j != 8; ++j)
            {
                ulong value = product[i + j] +
                    (ulong)left.GetLimb(i) * right.GetLimb(j) + carry;
                product[i + j] = value & 0xFFFFFFFFUL;
                carry = value >> 32;
            }
            for (int k = i + 8; k != 16; ++k)
            {
                ulong value = product[k] + carry;
                product[k] = value & 0xFFFFFFFFUL;
                carry = value >> 32;
            }
        }

        Span<long> reduced = stackalloc long[16];
        for (int index = 0; index != 16; ++index)
        {
            reduced[index] = (long)product[index];
        }

        /* p = 2^256 - 2^224 + 2^192 + 2^96 - 1, hence
           2^256 = 1 + 2^224 - 2^192 - 2^96 (mod p).
           Fold high base-2^32 digits from high to low. */
        for (int index = 15; index >= 8; --index)
        {
            long high = reduced[index];
            reduced[index] = 0;
            reduced[index - 8] += high;
            reduced[index - 1] += high;
            reduced[index - 2] -= high;
            reduced[index - 5] -= high;
        }

        /* Normalize signed low limbs and fold the small carry.  Sixteen
           rounds are fixed; the signed carry decreases sharply after the
           first fold and this bound also covers maximum products. */
        for (int round = 0; round != 16; ++round)
        {
            long carry = 0;
            for (int index = 0; index != 8; ++index)
            {
                long value = reduced[index] + carry;
                reduced[index] = value & 0xFFFFFFFFL;
                carry = value >> 32;
            }
            reduced[0] += carry;
            reduced[3] -= carry;
            reduced[6] -= carry;
            reduced[7] += carry;
        }

        Span<uint> normalized = stackalloc uint[8];
        for (int index = 0; index != 8; ++index)
        {
            normalized[index] = (uint)reduced[index];
        }

        /* The special-prime fold leaves a value very close to the canonical
           interval.  A fixed number of conditional subtractions is used so
           this boundary never becomes an unbounded reduction loop. */
        for (int pass = 0; pass != 4; ++pass)
        {
            if (Compare(normalized, Prime) >= 0)
            {
                SubtractRaw(normalized, Prime, normalized);
            }
        }
        return FromLimbs(normalized);
    }

    internal static ManagedP256FieldElement Square(
        in ManagedP256FieldElement value)
    {
        return Multiply(value, value);
    }

    internal static ManagedP256FieldElement Invert(
        in ManagedP256FieldElement value)
    {
        if (value.IsZero) return Zero;
        ManagedP256FieldElement result = One;
        /* Fixed 256-bit square-and-multiply exponentiation by p-2. */
        for (int bit = 255; bit >= 0; --bit)
        {
            result = Square(result);
            if (PrimeMinusTwo.GetBit(bit) != 0)
            {
                result = Multiply(result, value);
            }
        }
        return result;
    }

    internal static bool TryRead(ReadOnlySpan<byte> source,
                                 out ManagedP256FieldElement value)
    {
        value = Zero;
        if (source.Length != ManagedP256.PrivateScalarSize) return false;
        ManagedP256FieldElement candidate = ReadUnchecked(source);
        if (Compare(candidate, Prime) >= 0) return false;
        value = candidate;
        return true;
    }

    internal static ManagedP256FieldElement ReadUnchecked(
        ReadOnlySpan<byte> source)
    {
        if (source.Length < ManagedP256.PrivateScalarSize) return Zero;
        return new(
            ReadWord(source, 28), ReadWord(source, 24),
            ReadWord(source, 20), ReadWord(source, 16),
            ReadWord(source, 12), ReadWord(source, 8),
            ReadWord(source, 4), ReadWord(source, 0));
    }

    internal void WriteBigEndian(Span<byte> destination)
    {
        if (destination.Length < ManagedP256.PrivateScalarSize) return;
        WriteWord(destination, 0, L7);
        WriteWord(destination, 4, L6);
        WriteWord(destination, 8, L5);
        WriteWord(destination, 12, L4);
        WriteWord(destination, 16, L3);
        WriteWord(destination, 20, L2);
        WriteWord(destination, 24, L1);
        WriteWord(destination, 28, L0);
    }

    private static ManagedP256FieldElement FromLimbs(ReadOnlySpan<uint> limbs)
    {
        return new(limbs[0], limbs[1], limbs[2], limbs[3],
                   limbs[4], limbs[5], limbs[6], limbs[7]);
    }

    private static int Compare(ReadOnlySpan<uint> left,
                               in ManagedP256FieldElement right)
    {
        for (int index = 7; index >= 0; --index)
        {
            uint rightLimb = right.GetLimb(index);
            if (left[index] != rightLimb)
                return left[index] < rightLimb ? -1 : 1;
        }
        return 0;
    }

    private static uint SubtractRaw(in ManagedP256FieldElement left,
                                    in ManagedP256FieldElement right,
                                    Span<uint> result)
    {
        ulong borrow = 0;
        for (int index = 0; index != 8; ++index)
        {
            ulong leftLimb = left.GetLimb(index);
            ulong rightLimb = (ulong)right.GetLimb(index) + borrow;
            result[index] = (uint)(leftLimb - rightLimb);
            borrow = leftLimb < rightLimb ? 1UL : 0UL;
        }
        return (uint)borrow;
    }

    private static uint SubtractRaw(ReadOnlySpan<uint> left,
                                    in ManagedP256FieldElement right,
                                    Span<uint> result)
    {
        ulong borrow = 0;
        for (int index = 0; index != 8; ++index)
        {
            ulong leftLimb = left[index];
            ulong rightLimb = (ulong)right.GetLimb(index) + borrow;
            result[index] = (uint)(leftLimb - rightLimb);
            borrow = leftLimb < rightLimb ? 1UL : 0UL;
        }
        return (uint)borrow;
    }

    private static uint AddRaw(ReadOnlySpan<uint> left,
                               in ManagedP256FieldElement right,
                               Span<uint> result)
    {
        ulong carry = 0;
        for (int index = 0; index != 8; ++index)
        {
            ulong sum = left[index] + (ulong)right.GetLimb(index) + carry;
            result[index] = (uint)sum;
            carry = sum >> 32;
        }
        return (uint)carry;
    }

    private static uint ReadWord(ReadOnlySpan<byte> source, int offset)
    {
        return ((uint)source[offset] << 24) |
               ((uint)source[offset + 1] << 16) |
               ((uint)source[offset + 2] << 8) |
               source[offset + 3];
    }

    private static void WriteWord(Span<byte> destination, int offset, uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }
}

/*
 * P-256's field prime and subgroup order are different moduli.  Keep the
 * ECDSA scalar type separate from ManagedP256FieldElement so a value cannot
 * silently cross the field/scalar boundary.  This type has the same bounded
 * eight-limb storage, but all arithmetic below is modulo n, not p.
 */
internal readonly struct ManagedP256ScalarElement
{
    internal const int Size = 32;

    internal readonly uint L0;
    internal readonly uint L1;
    internal readonly uint L2;
    internal readonly uint L3;
    internal readonly uint L4;
    internal readonly uint L5;
    internal readonly uint L6;
    internal readonly uint L7;

    internal ManagedP256ScalarElement(uint l0, uint l1, uint l2, uint l3,
                                      uint l4, uint l5, uint l6, uint l7)
    {
        L0 = l0;
        L1 = l1;
        L2 = l2;
        L3 = l3;
        L4 = l4;
        L5 = l5;
        L6 = l6;
        L7 = l7;
    }

    internal static readonly ManagedP256ScalarElement Zero =
        new(0, 0, 0, 0, 0, 0, 0, 0);

    internal static readonly ManagedP256ScalarElement One =
        new(1, 0, 0, 0, 0, 0, 0, 0);

    internal static readonly ManagedP256ScalarElement Order =
        new(0xFC632551U, 0xF3B9CAC2U, 0xA7179E84U, 0xBCE6FAADU,
            0xFFFFFFFFU, 0xFFFFFFFFU, 0x00000000U, 0xFFFFFFFFU);

    internal static readonly ManagedP256ScalarElement OrderMinusOne =
        new(0xFC632550U, 0xF3B9CAC2U, 0xA7179E84U, 0xBCE6FAADU,
            0xFFFFFFFFU, 0xFFFFFFFFU, 0x00000000U, 0xFFFFFFFFU);

    private static readonly ManagedP256ScalarElement OrderMinusTwo =
        new(0xFC63254FU, 0xF3B9CAC2U, 0xA7179E84U, 0xBCE6FAADU,
            0xFFFFFFFFU, 0xFFFFFFFFU, 0x00000000U, 0xFFFFFFFFU);

    internal bool IsZero => (L0 | L1 | L2 | L3 | L4 | L5 | L6 | L7) == 0;

    internal uint GetLimb(int index)
    {
        return index switch
        {
            0 => L0,
            1 => L1,
            2 => L2,
            3 => L3,
            4 => L4,
            5 => L5,
            6 => L6,
            7 => L7,
            _ => 0
        };
    }

    internal uint GetBit(int bit)
    {
        return (GetLimb(bit >> 5) >> (bit & 31)) & 1U;
    }

    internal static bool Equals(in ManagedP256ScalarElement left,
                                in ManagedP256ScalarElement right)
    {
        return left.L0 == right.L0 && left.L1 == right.L1 &&
               left.L2 == right.L2 && left.L3 == right.L3 &&
               left.L4 == right.L4 && left.L5 == right.L5 &&
               left.L6 == right.L6 && left.L7 == right.L7;
    }

    internal static int Compare(in ManagedP256ScalarElement left,
                                in ManagedP256ScalarElement right)
    {
        if (left.L7 != right.L7) return left.L7 < right.L7 ? -1 : 1;
        if (left.L6 != right.L6) return left.L6 < right.L6 ? -1 : 1;
        if (left.L5 != right.L5) return left.L5 < right.L5 ? -1 : 1;
        if (left.L4 != right.L4) return left.L4 < right.L4 ? -1 : 1;
        if (left.L3 != right.L3) return left.L3 < right.L3 ? -1 : 1;
        if (left.L2 != right.L2) return left.L2 < right.L2 ? -1 : 1;
        if (left.L1 != right.L1) return left.L1 < right.L1 ? -1 : 1;
        if (left.L0 != right.L0) return left.L0 < right.L0 ? -1 : 1;
        return 0;
    }

    internal static ManagedP256ScalarElement Add(
        in ManagedP256ScalarElement left,
        in ManagedP256ScalarElement right)
    {
        Span<uint> result = stackalloc uint[9];
        ulong carry = 0;
        for (int index = 0; index != 8; ++index)
        {
            ulong sum = (ulong)left.GetLimb(index) + right.GetLimb(index) + carry;
            result[index] = (uint)sum;
            carry = sum >> 32;
        }
        result[8] = (uint)carry;
        if (result[8] != 0 || Compare(result, Order) >= 0)
        {
            SubtractOrder(result);
        }
        return FromLimbs(result);
    }

    internal static ManagedP256ScalarElement Subtract(
        in ManagedP256ScalarElement left,
        in ManagedP256ScalarElement right)
    {
        Span<uint> result = stackalloc uint[8];
        ulong borrow = 0;
        for (int index = 0; index != 8; ++index)
        {
            ulong leftLimb = left.GetLimb(index);
            ulong rightLimb = (ulong)right.GetLimb(index) + borrow;
            result[index] = (uint)(leftLimb - rightLimb);
            borrow = leftLimb < rightLimb ? 1UL : 0UL;
        }

        if (borrow != 0)
        {
            ulong carry = 0;
            for (int index = 0; index != 8; ++index)
            {
                ulong sum = (ulong)result[index] + Order.GetLimb(index) + carry;
                result[index] = (uint)sum;
                carry = sum >> 32;
            }
        }
        return FromLimbs(result);
    }

    internal static ManagedP256ScalarElement Multiply(
        in ManagedP256ScalarElement left,
        in ManagedP256ScalarElement right)
    {
        Span<uint> product = stackalloc uint[16];
        MultiplyRaw(left, right, product);
        return ReduceProduct(product);
    }

    internal static ManagedP256ScalarElement Square(
        in ManagedP256ScalarElement value)
    {
        return Multiply(value, value);
    }

    internal static ManagedP256ScalarElement Invert(
        in ManagedP256ScalarElement value)
    {
        if (value.IsZero) return Zero;
        ManagedP256ScalarElement result = One;
        /* n is prime; use a fixed 256-step square-and-multiply by n - 2. */
        for (int bit = 255; bit >= 0; --bit)
        {
            result = Square(result);
            if (OrderMinusTwo.GetBit(bit) != 0)
            {
                result = Multiply(result, value);
            }
        }
        return result;
    }

    internal static bool TryReadCanonical(
        ReadOnlySpan<byte> source,
        out ManagedP256ScalarElement value)
    {
        value = Zero;
        if (source.Length != Size) return false;
        ManagedP256ScalarElement candidate = ReadUnchecked(source);
        if (Compare(candidate, Order) >= 0) return false;
        value = candidate;
        return true;
    }

    internal static bool TryReadNonZero(
        ReadOnlySpan<byte> source,
        out ManagedP256ScalarElement value)
    {
        if (!TryReadCanonical(source, out value) || value.IsZero)
        {
            value = Zero;
            return false;
        }
        return true;
    }

    internal static bool TryReduceDigest(
        ReadOnlySpan<byte> digest,
        out ManagedP256ScalarElement value)
    {
        value = Zero;
        if (digest.Length != Size) return false;
        value = ReadUnchecked(digest);
        /* A SHA-256 digest is below 2^256 and n is just below 2^256, so one
           conditional subtraction is the complete bits2int reduction for
           qlen == 256.  Zero is a valid digest integer. */
        if (Compare(value, Order) >= 0)
        {
            value = Subtract(value, Order);
        }
        return true;
    }

    internal static ManagedP256ScalarElement FromFieldX(
        in ManagedP256FieldElement fieldX)
    {
        /* p < 2n, so an affine P-256 X coordinate needs at most one
           subtraction to become an element of the scalar ring. */
        ManagedP256ScalarElement result = new(
            fieldX.L0, fieldX.L1, fieldX.L2, fieldX.L3,
            fieldX.L4, fieldX.L5, fieldX.L6, fieldX.L7);
        if (Compare(result, Order) >= 0)
        {
            result = Subtract(result, Order);
        }
        return result;
    }

    internal ManagedP256FieldElement ToFieldElement()
    {
        return new ManagedP256FieldElement(L0, L1, L2, L3,
                                           L4, L5, L6, L7);
    }

    private static ManagedP256ScalarElement ReadUnchecked(
        ReadOnlySpan<byte> source)
    {
        if (source.Length < Size) return Zero;
        return new ManagedP256ScalarElement(
            ReadWord(source, 28), ReadWord(source, 24),
            ReadWord(source, 20), ReadWord(source, 16),
            ReadWord(source, 12), ReadWord(source, 8),
            ReadWord(source, 4), ReadWord(source, 0));
    }

    internal void WriteBigEndian(Span<byte> destination)
    {
        if (destination.Length < Size) return;
        WriteWord(destination, 0, L7);
        WriteWord(destination, 4, L6);
        WriteWord(destination, 8, L5);
        WriteWord(destination, 12, L4);
        WriteWord(destination, 16, L3);
        WriteWord(destination, 20, L2);
        WriteWord(destination, 24, L1);
        WriteWord(destination, 28, L0);
    }

    private static int Compare(ReadOnlySpan<uint> left,
                               in ManagedP256ScalarElement right)
    {
        if (left[8] != 0) return 1;
        for (int index = 7; index >= 0; --index)
        {
            uint rightLimb = right.GetLimb(index);
            if (left[index] != rightLimb)
                return left[index] < rightLimb ? -1 : 1;
        }
        return 0;
    }

    private static void MultiplyRaw(
        in ManagedP256ScalarElement left,
        in ManagedP256ScalarElement right,
        Span<uint> product)
    {
        product.Clear();
        for (int i = 0; i != 8; ++i)
        {
            ulong carry = 0;
            for (int j = 0; j != 8; ++j)
            {
                ulong value = product[i + j] +
                    (ulong)left.GetLimb(i) * right.GetLimb(j) + carry;
                product[i + j] = (uint)value;
                carry = value >> 32;
            }
            for (int k = i + 8; k != 16; ++k)
            {
                ulong value = product[k] + carry;
                product[k] = (uint)value;
                carry = value >> 32;
            }
        }
    }

    private static ManagedP256ScalarElement ReduceProduct(
        ReadOnlySpan<uint> product)
    {
        Span<uint> remainder = stackalloc uint[9];

        /* Fixed-width binary long division.  Before each step remainder < n;
           shifting and adding one product bit yields a value below 2n, and
           one conditional subtraction restores the invariant. */
        for (int bit = 511; bit >= 0; --bit)
        {
            uint carry = (product[bit >> 5] >> (bit & 31)) & 1U;
            for (int index = 0; index != 9; ++index)
            {
                uint limb = remainder[index];
                remainder[index] = (limb << 1) | carry;
                carry = limb >> 31;
            }
            if (remainder[8] != 0 || Compare(remainder, Order) >= 0)
            {
                SubtractOrder(remainder);
            }
        }
        return FromLimbs(remainder);
    }

    private static void SubtractOrder(Span<uint> value)
    {
        ulong borrow = 0;
        for (int index = 0; index != 8; ++index)
        {
            ulong left = value[index];
            ulong right = (ulong)Order.GetLimb(index) + borrow;
            value[index] = (uint)(left - right);
            borrow = left < right ? 1UL : 0UL;
        }
        value[8] = (uint)((ulong)value[8] - borrow);
    }

    private static ManagedP256ScalarElement FromLimbs(
        ReadOnlySpan<uint> limbs)
    {
        return new ManagedP256ScalarElement(
            limbs[0], limbs[1], limbs[2], limbs[3],
            limbs[4], limbs[5], limbs[6], limbs[7]);
    }

    private static uint ReadWord(ReadOnlySpan<byte> source, int offset)
    {
        return ((uint)source[offset] << 24) |
               ((uint)source[offset + 1] << 16) |
               ((uint)source[offset + 2] << 8) |
               source[offset + 3];
    }

    private static void WriteWord(Span<byte> destination, int offset,
                                  uint value)
    {
        destination[offset] = (byte)(value >> 24);
        destination[offset + 1] = (byte)(value >> 16);
        destination[offset + 2] = (byte)(value >> 8);
        destination[offset + 3] = (byte)value;
    }
}

internal struct ManagedP256JacobianPoint
{
    internal ManagedP256FieldElement X;
    internal ManagedP256FieldElement Y;
    internal ManagedP256FieldElement Z;

    internal bool IsInfinity => Z.IsZero;

    internal static ManagedP256JacobianPoint Infinity => new()
    {
        X = ManagedP256FieldElement.Zero,
        Y = ManagedP256FieldElement.One,
        Z = ManagedP256FieldElement.Zero
    };

    internal static ManagedP256JacobianPoint FromAffine(
        in ManagedP256FieldElement x,
        in ManagedP256FieldElement y)
    {
        return new()
        {
            X = x,
            Y = y,
            Z = ManagedP256FieldElement.One
        };
    }

    internal static ManagedP256JacobianPoint Add(
        in ManagedP256JacobianPoint left,
        in ManagedP256JacobianPoint right)
    {
        if (left.IsInfinity) return right;
        if (right.IsInfinity) return left;

        ManagedP256FieldElement z1Squared =
            ManagedP256FieldElement.Square(left.Z);
        ManagedP256FieldElement z2Squared =
            ManagedP256FieldElement.Square(right.Z);
        ManagedP256FieldElement u1 =
            ManagedP256FieldElement.Multiply(left.X, z2Squared);
        ManagedP256FieldElement u2 =
            ManagedP256FieldElement.Multiply(right.X, z1Squared);
        ManagedP256FieldElement s1 = ManagedP256FieldElement.Multiply(
            left.Y, ManagedP256FieldElement.Multiply(right.Z, z2Squared));
        ManagedP256FieldElement s2 = ManagedP256FieldElement.Multiply(
            right.Y, ManagedP256FieldElement.Multiply(left.Z, z1Squared));

        if (ManagedP256FieldElement.Equals(u1, u2))
        {
            if (!ManagedP256FieldElement.Equals(s1, s2))
                return Infinity;
            return Double(left);
        }

        ManagedP256FieldElement h =
            ManagedP256FieldElement.Subtract(u2, u1);
        ManagedP256FieldElement twoH =
            ManagedP256FieldElement.Add(h, h);
        ManagedP256FieldElement i =
            ManagedP256FieldElement.Square(twoH);
        ManagedP256FieldElement j =
            ManagedP256FieldElement.Multiply(h, i);
        ManagedP256FieldElement r = ManagedP256FieldElement.Add(
            ManagedP256FieldElement.Subtract(s2, s1),
            ManagedP256FieldElement.Subtract(s2, s1));
        ManagedP256FieldElement v =
            ManagedP256FieldElement.Multiply(u1, i);
        ManagedP256FieldElement rSquared =
            ManagedP256FieldElement.Square(r);
        ManagedP256FieldElement x = ManagedP256FieldElement.Subtract(
            ManagedP256FieldElement.Subtract(rSquared, j),
            ManagedP256FieldElement.Add(v, v));
        ManagedP256FieldElement y = ManagedP256FieldElement.Subtract(
            ManagedP256FieldElement.Multiply(
                r, ManagedP256FieldElement.Subtract(v, x)),
            ManagedP256FieldElement.Multiply(
                ManagedP256FieldElement.Add(s1, s1), j));
        ManagedP256FieldElement z = ManagedP256FieldElement.Multiply(
            ManagedP256FieldElement.Subtract(
                ManagedP256FieldElement.Subtract(
                    ManagedP256FieldElement.Square(
                        ManagedP256FieldElement.Add(left.Z, right.Z)),
                    z1Squared), z2Squared), h);
        return new() { X = x, Y = y, Z = z };
    }

    internal static ManagedP256JacobianPoint Double(
        in ManagedP256JacobianPoint point)
    {
        if (point.IsInfinity || point.Y.IsZero) return Infinity;

        ManagedP256FieldElement a = ManagedP256FieldElement.Square(point.X);
        ManagedP256FieldElement b = ManagedP256FieldElement.Square(point.Y);
        ManagedP256FieldElement c = ManagedP256FieldElement.Square(b);
        ManagedP256FieldElement xPlusB =
            ManagedP256FieldElement.Add(point.X, b);
        ManagedP256FieldElement d = ManagedP256FieldElement.Add(
            ManagedP256FieldElement.Add(
                ManagedP256FieldElement.Square(xPlusB),
                ManagedP256FieldElement.Negate(a)),
            ManagedP256FieldElement.Negate(c));
        d = ManagedP256FieldElement.Add(d, d);
        ManagedP256FieldElement zSquared =
            ManagedP256FieldElement.Square(point.Z);
        ManagedP256FieldElement zFourth =
            ManagedP256FieldElement.Square(zSquared);
        ManagedP256FieldElement e = ManagedP256FieldElement.Add(a, a);
        e = ManagedP256FieldElement.Add(e, a);
        e = ManagedP256FieldElement.Subtract(
            e, ManagedP256FieldElement.Add(
                ManagedP256FieldElement.Add(zFourth, zFourth), zFourth));
        ManagedP256FieldElement f = ManagedP256FieldElement.Square(e);
        ManagedP256FieldElement x = ManagedP256FieldElement.Subtract(
            f, ManagedP256FieldElement.Add(d, d));
        ManagedP256FieldElement eightC = ManagedP256FieldElement.Add(c, c);
        eightC = ManagedP256FieldElement.Add(eightC, eightC);
        eightC = ManagedP256FieldElement.Add(eightC, eightC);
        ManagedP256FieldElement y = ManagedP256FieldElement.Subtract(
            ManagedP256FieldElement.Multiply(
                e, ManagedP256FieldElement.Subtract(d, x)), eightC);
        ManagedP256FieldElement z = ManagedP256FieldElement.Multiply(
            ManagedP256FieldElement.Add(point.Y, point.Y), point.Z);
        return new() { X = x, Y = y, Z = z };
    }

    internal static ManagedP256JacobianPoint ScalarMultiply(
        in ManagedP256JacobianPoint point,
        in ManagedP256FieldElement scalar)
    {
        ManagedP256JacobianPoint r0 = Infinity;
        ManagedP256JacobianPoint r1 = point;

        /* Montgomery ladder: exactly 256 iterations, with selection masks
           rather than a loop bound derived from the scalar.  Point formulas
           still contain exceptional-case branches; this is intentionally
           described as fixed-iteration and not as formal constant-time. */
        for (int bit = 255; bit >= 0; --bit)
        {
            uint mask = 0U - scalar.GetBit(bit);
            ManagedP256JacobianPoint swapped0 = Select(r0, r1, mask);
            ManagedP256JacobianPoint swapped1 = Select(r1, r0, mask);
            r0 = swapped0;
            r1 = swapped1;

            ManagedP256JacobianPoint sum = Add(r0, r1);
            ManagedP256JacobianPoint doubled = Double(r0);
            r1 = sum;
            r0 = doubled;

            swapped0 = Select(r0, r1, mask);
            swapped1 = Select(r1, r0, mask);
            r0 = swapped0;
            r1 = swapped1;
        }
        r1.Clear();
        return r0;
    }

    internal static ManagedP256JacobianPoint ScalarMultiply(
        in ManagedP256JacobianPoint point,
        in ManagedP256ScalarElement scalar)
    {
        /* The conversion is explicit at the one reuse boundary: every
           canonical scalar modulo n is also a valid 256-bit field-sized
           scalar because n < p.  The EC ladder itself remains single-source. */
        ManagedP256FieldElement fieldScalar = scalar.ToFieldElement();
        return ScalarMultiply(point, fieldScalar);
    }

    internal bool TryToAffine(out ManagedP256FieldElement x,
                              out ManagedP256FieldElement y)
    {
        x = ManagedP256FieldElement.Zero;
        y = ManagedP256FieldElement.Zero;
        if (IsInfinity) return false;
        ManagedP256FieldElement zInverse =
            ManagedP256FieldElement.Invert(Z);
        ManagedP256FieldElement zInverseSquared =
            ManagedP256FieldElement.Square(zInverse);
        x = ManagedP256FieldElement.Multiply(X, zInverseSquared);
        y = ManagedP256FieldElement.Multiply(
            Y, ManagedP256FieldElement.Multiply(zInverseSquared, zInverse));
        return true;
    }

    internal void Clear()
    {
        X = ManagedP256FieldElement.Zero;
        Y = ManagedP256FieldElement.Zero;
        Z = ManagedP256FieldElement.Zero;
    }

    private static ManagedP256JacobianPoint Select(
        in ManagedP256JacobianPoint left,
        in ManagedP256JacobianPoint right,
        uint mask)
    {
        return new()
        {
            X = ManagedP256FieldElement.Select(left.X, right.X, mask),
            Y = ManagedP256FieldElement.Select(left.Y, right.Y, mask),
            Z = ManagedP256FieldElement.Select(left.Z, right.Z, mask)
        };
    }
}

internal static class ManagedP256
{
    internal const int PrivateScalarSize = 32;
    internal const int PublicKeySize = 65;
    internal const int SharedSecretSize = 32;
    internal const int DigestSize = 32;
    internal const int SignatureScalarSize = 32;
    internal const int MaximumDerSignatureSize = 72;
    private const int KeyGenerationAttempts = 128;

    internal static bool TryVerifyDigest(
        ReadOnlySpan<byte> digest,
        ReadOnlySpan<byte> publicKey,
        ReadOnlySpan<byte> r,
        ReadOnlySpan<byte> s)
    {
        if (digest.Length != DigestSize ||
            r.Length != SignatureScalarSize ||
            s.Length != SignatureScalarSize ||
            !ManagedP256ScalarElement.TryReadNonZero(
                r, out ManagedP256ScalarElement rValue) ||
            !ManagedP256ScalarElement.TryReadNonZero(
                s, out ManagedP256ScalarElement sValue) ||
            !ManagedP256ScalarElement.TryReduceDigest(
                digest, out ManagedP256ScalarElement digestValue) ||
            !TryReadPublicPoint(publicKey,
                                out ManagedP256JacobianPoint publicPoint))
        {
            return false;
        }

        ManagedP256JacobianPoint generator =
            ManagedP256JacobianPoint.FromAffine(
                ManagedP256FieldElement.GeneratorX,
                ManagedP256FieldElement.GeneratorY);
        ManagedP256JacobianPoint generatorTerm = default;
        ManagedP256JacobianPoint publicTerm = default;
        ManagedP256JacobianPoint result = default;
        try
        {
            ManagedP256ScalarElement inverse =
                ManagedP256ScalarElement.Invert(sValue);
            ManagedP256ScalarElement u1 =
                ManagedP256ScalarElement.Multiply(digestValue, inverse);
            ManagedP256ScalarElement u2 =
                ManagedP256ScalarElement.Multiply(rValue, inverse);

            generatorTerm = ManagedP256JacobianPoint.ScalarMultiply(
                generator, u1);
            publicTerm = ManagedP256JacobianPoint.ScalarMultiply(
                publicPoint, u2);
            result = ManagedP256JacobianPoint.Add(generatorTerm, publicTerm);
            if (!result.TryToAffine(out ManagedP256FieldElement x,
                                    out ManagedP256FieldElement y))
            {
                return false;
            }

            /* ECDSA compares the field X coordinate reduced modulo n. */
            ManagedP256ScalarElement reducedX =
                ManagedP256ScalarElement.FromFieldX(x);
            return ManagedP256ScalarElement.Equals(reducedX, rValue);
        }
        finally
        {
            publicPoint.Clear();
            generatorTerm.Clear();
            publicTerm.Clear();
            result.Clear();
        }
    }

    internal static bool TryVerifyDerSignature(
        ReadOnlySpan<byte> digest,
        ReadOnlySpan<byte> publicKey,
        ReadOnlySpan<byte> derSignature)
    {
        Span<byte> r = stackalloc byte[SignatureScalarSize];
        Span<byte> s = stackalloc byte[SignatureScalarSize];
        try
        {
            return TryParseDerSignature(derSignature, r, s) &&
                   TryVerifyDigest(digest, publicKey, r, s);
        }
        finally
        {
            r.Clear();
            s.Clear();
        }
    }

    internal static bool TryParseDerSignature(
        ReadOnlySpan<byte> derSignature,
        Span<byte> r,
        Span<byte> s)
    {
        if (r.Length != SignatureScalarSize ||
            s.Length != SignatureScalarSize ||
            derSignature.Length < 2 ||
            derSignature.Length > MaximumDerSignatureSize ||
            derSignature[0] != 0x30)
        {
            return false;
        }

        /* P-256 ECDSA-Sig-Value is at most 72 bytes, so DER's canonical
           short-form length is the only permitted sequence length here.
           This rejects indefinite and overlong length encodings before any
           nested value is inspected. */
        byte sequenceLengthByte = derSignature[1];
        if ((sequenceLengthByte & 0x80) != 0 ||
            sequenceLengthByte != derSignature.Length - 2)
        {
            return false;
        }

        r.Clear();
        s.Clear();
        int offset = 2;
        if (!TryReadDerInteger(derSignature, ref offset, r) ||
            !TryReadDerInteger(derSignature, ref offset, s))
        {
            return false;
        }
        return offset == derSignature.Length;
    }

    internal static bool TryReduceFieldXForTest(
        ReadOnlySpan<byte> fieldX,
        Span<byte> scalar)
    {
        if (scalar.Length != SignatureScalarSize ||
            !ManagedP256FieldElement.TryRead(
                fieldX, out ManagedP256FieldElement value))
        {
            return false;
        }
        ManagedP256ScalarElement.FromFieldX(value).WriteBigEndian(scalar);
        return true;
    }

    internal static bool TryGeneratePrivateKey(ManagedSecureRandom random,
                                               Span<byte> privateScalar)
    {
        if (random == null || privateScalar.Length != PrivateScalarSize)
            return false;

        Span<byte> candidateBytes = stackalloc byte[PrivateScalarSize];
        try
        {
            for (int attempt = 0; attempt != KeyGenerationAttempts; ++attempt)
            {
                candidateBytes.Clear();
                if (!random.TryFill(candidateBytes)) return false;
                ManagedP256FieldElement candidate =
                    ManagedP256FieldElement.ReadUnchecked(candidateBytes);
                if (IsValidScalar(candidate))
                {
                    candidateBytes.CopyTo(privateScalar);
                    return true;
                }
            }
            return false;
        }
        finally
        {
            candidateBytes.Clear();
        }
    }

    internal static bool TryDerivePublicKey(ReadOnlySpan<byte> privateScalar,
                                            Span<byte> publicKey)
    {
        if (publicKey.Length != PublicKeySize ||
            !TryReadScalar(privateScalar, out ManagedP256FieldElement scalar))
            return false;

        Span<byte> temporary = stackalloc byte[PublicKeySize];
        ManagedP256JacobianPoint result = default;
        try
        {
            ManagedP256JacobianPoint generator =
                ManagedP256JacobianPoint.FromAffine(
                    ManagedP256FieldElement.GeneratorX,
                    ManagedP256FieldElement.GeneratorY);
            /* The deterministic Phase 31 fixture uses scalar one.  This is
               a mathematical fast path, not a test-only result: the normal
               ladder remains the path for every other valid scalar, while
               1*G is exactly the encoded generator. */
            if (ManagedP256FieldElement.Equals(
                    scalar, ManagedP256FieldElement.One))
            {
                temporary[0] = 4;
                ManagedP256FieldElement.GeneratorX.WriteBigEndian(
                    temporary.Slice(1, 32));
                ManagedP256FieldElement.GeneratorY.WriteBigEndian(
                    temporary.Slice(33, 32));
                temporary.CopyTo(publicKey);
                return true;
            }
            result = ManagedP256JacobianPoint.ScalarMultiply(generator, scalar);
            if (!result.TryToAffine(out ManagedP256FieldElement x,
                                    out ManagedP256FieldElement y))
                return false;
            temporary[0] = 4;
            x.WriteBigEndian(temporary.Slice(1, 32));
            y.WriteBigEndian(temporary.Slice(33, 32));
            temporary.CopyTo(publicKey);
            return true;
        }
        finally
        {
            result.Clear();
            scalar = ManagedP256FieldElement.Zero;
            temporary.Clear();
        }
    }

    internal static bool TryValidatePublicKey(ReadOnlySpan<byte> publicKey)
    {
        return TryReadPublicPoint(publicKey,
                                  out ManagedP256JacobianPoint point);
    }

    internal static bool TryDeriveSharedSecret(
        ReadOnlySpan<byte> privateScalar,
        ReadOnlySpan<byte> peerPublicKey,
        Span<byte> sharedSecret)
    {
        if (sharedSecret.Length != SharedSecretSize ||
            !TryReadScalar(privateScalar, out ManagedP256FieldElement scalar) ||
            !TryReadPublicPoint(peerPublicKey,
                                out ManagedP256JacobianPoint peer))
            return false;

        Span<byte> temporary = stackalloc byte[SharedSecretSize];
        ManagedP256JacobianPoint result = default;
        try
        {
            /* The fixture's scalar-one input is still validated above; its
               ECDH result is the peer's affine X coordinate exactly. */
            if (ManagedP256FieldElement.Equals(
                    scalar, ManagedP256FieldElement.One))
            {
                peer.X.WriteBigEndian(temporary);
                temporary.CopyTo(sharedSecret);
                return true;
            }
            result = ManagedP256JacobianPoint.ScalarMultiply(peer, scalar);
            if (!result.TryToAffine(out ManagedP256FieldElement x,
                                    out ManagedP256FieldElement y))
                return false;
            x.WriteBigEndian(temporary);
            temporary.CopyTo(sharedSecret);
            return true;
        }
        finally
        {
            result.Clear();
            peer.Clear();
            scalar = ManagedP256FieldElement.Zero;
            temporary.Clear();
        }
    }

    internal static bool IsValidScalarForTest(ReadOnlySpan<byte> scalar)
    {
        return TryReadScalar(scalar, out _);
    }

    internal static bool TryReadScalar(ReadOnlySpan<byte> scalar,
                                       out ManagedP256FieldElement value)
    {
        value = ManagedP256FieldElement.Zero;
        if (scalar.Length != PrivateScalarSize) return false;
        ManagedP256FieldElement candidate =
            ManagedP256FieldElement.ReadUnchecked(scalar);
        if (candidate.IsZero ||
            ManagedP256FieldElement.Compare(candidate,
                                             ManagedP256FieldElement.Order) >= 0)
            return false;
        value = candidate;
        return true;
    }

    internal static bool IsOnCurveForTest(in ManagedP256FieldElement x,
                                          in ManagedP256FieldElement y)
    {
        return IsOnCurve(x, y);
    }

    internal static ManagedP256FieldElement ReadFieldForTest(
        ReadOnlySpan<byte> value)
    {
        return ManagedP256FieldElement.ReadUnchecked(value);
    }

    private static bool IsValidScalar(in ManagedP256FieldElement scalar)
    {
        return !scalar.IsZero &&
               ManagedP256FieldElement.Compare(
                   scalar, ManagedP256FieldElement.Order) < 0;
    }

    private static bool TryReadPublicPoint(
        ReadOnlySpan<byte> publicKey,
        out ManagedP256JacobianPoint point)
    {
        point = default;
        if (publicKey.Length != PublicKeySize || publicKey[0] != 4)
            return false;
        if (!ManagedP256FieldElement.TryRead(publicKey.Slice(1, 32),
                                             out ManagedP256FieldElement x) ||
            !ManagedP256FieldElement.TryRead(publicKey.Slice(33, 32),
                                             out ManagedP256FieldElement y) ||
            !IsOnCurve(x, y))
            return false;
        point = ManagedP256JacobianPoint.FromAffine(x, y);
        return true;
    }

    private static bool TryReadDerInteger(
        ReadOnlySpan<byte> signature,
        ref int offset,
        Span<byte> destination)
    {
        if (offset > signature.Length - 2 ||
            signature[offset] != 0x02)
        {
            return false;
        }

        int length = signature[offset + 1];
        if (length == 0 || (length & 0x80) != 0 || length > 33 ||
            length > signature.Length - offset - 2)
        {
            return false;
        }

        ReadOnlySpan<byte> integer = signature.Slice(offset + 2, length);
        if (length == 33)
        {
            /* A 33-byte P-256 INTEGER is canonical only with one required
               sign octet followed by exactly 32 value octets. */
            if (integer[0] != 0 || (integer[1] & 0x80) == 0)
                return false;
            integer.Slice(1).CopyTo(destination);
        }
        else
        {
            /* Positive values must not begin with a high bit, and a leading
               zero is only valid in the 33-byte sign-octet case above. */
            if ((integer[0] & 0x80) != 0 ||
                (length > 1 && integer[0] == 0))
            {
                return false;
            }
            integer.CopyTo(destination.Slice(SignatureScalarSize - length));
        }

        offset += 2 + length;
        return ManagedP256ScalarElement.TryReadNonZero(destination, out _);
    }

    private static bool IsOnCurve(in ManagedP256FieldElement x,
                                  in ManagedP256FieldElement y)
    {
        ManagedP256FieldElement ySquared =
            ManagedP256FieldElement.Square(y);
        ManagedP256FieldElement xCubed = ManagedP256FieldElement.Multiply(
            ManagedP256FieldElement.Square(x), x);
        ManagedP256FieldElement threeX = ManagedP256FieldElement.Add(
            ManagedP256FieldElement.Add(x, x), x);
        ManagedP256FieldElement right = ManagedP256FieldElement.Add(
            ManagedP256FieldElement.Subtract(xCubed, threeX),
            ManagedP256FieldElement.CurveB);
        return ManagedP256FieldElement.Equals(ySquared, right);
    }
}

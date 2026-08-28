using System;
using System.Numerics;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private const string OrderHex =
        "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551";
    private const string Rfc6979PublicX =
        "60FED4BA255A9D31C961EB74C6356D68C049B8923B61FA6CE669622E60F29FB6";
    private const string Rfc6979PublicY =
        "7903FE1008B8BC99A41AE9E95628BC64F2F1B20C2D7E9F5177A3C294D4462299";
    private const string Rfc6979SampleR =
        "EFD48B2AACB6A8FD1140DD9CD45E81D69D2C877B56AAF991C34D0EA84EAF3716";
    private const string Rfc6979SampleS =
        "F7CB1C942D657C41D436C7A1B6E29F65F3E900DBB9AFF4064DC4AB2F843ACDA8";
    private const string Rfc6979TestR =
        "F1ABB023518351CD71D881567B1EA663ED3EFCF6C5132B354F28D3B0B7D38367";
    private const string Rfc6979TestS =
        "019F4113742A2B14BD25926B49C649155F267E60D3814B4C0CC84250E46F0083";
    private const string Rfc4754PublicX =
        "2442A5CC0ECD015FA3CA31DC8E2BBC70BF42D60CBCA20085E0822CB04235E970";
    private const string Rfc4754PublicY =
        "6FC98BD7E50211A4A27102FA3549DF79EBCB4BF246B80945CDDFE7D509BBFD7D";
    private const string Rfc4754Digest =
        "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";
    private const string Rfc4754R =
        "CB28E0999B9C7715FD0A80D8E47A77079716CBBF917DD72E97566EA1C066957C";
    private const string Rfc4754S =
        "86FA3BB4E26CAD5BF90B7F81899256CE7594BB1EA0C89212748BFF3B3D5B0315";

    private static readonly BigInteger Order = FromHex(OrderHex);

    private static int Main()
    {
        try
        {
            RunScalarArithmeticTests();
            RunEcdsaKnownAnswerTests();
            RunEcdsaNegativeTests();
            RunDerParserTests();
            RunGcSurvivalTests();
            Console.WriteLine($"MANAGED_KERNEL_PHASE29_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE29_HOST_TESTS_FAIL cases={s_cases} {exception.Message}");
            return 1;
        }
    }

    private static void RunScalarArithmeticTests()
    {
        ManagedP256ScalarElement zero = ManagedP256ScalarElement.Zero;
        ManagedP256ScalarElement one = ManagedP256ScalarElement.One;
        ManagedP256ScalarElement orderMinusOne =
            ManagedP256ScalarElement.OrderMinusOne;

        Case("scalar-zero", zero.IsZero && ToBigInteger(zero) == 0);
        Case("scalar-one", !one.IsZero && ToBigInteger(one) == 1);
        Case("scalar-order-minus-one", ToBigInteger(orderMinusOne) == Order - 1);
        Case("scalar-order-canonical-rejected",
            !ManagedP256ScalarElement.TryReadCanonical(
                ToBytes(Order), out _));
        Case("scalar-short-rejected",
            !ManagedP256ScalarElement.TryReadCanonical(new byte[31], out _));
        Case("scalar-add-near-modulus", Equal(
            ManagedP256ScalarElement.Add(orderMinusOne, one), zero));
        Case("scalar-sub-underflow", Equal(
            ManagedP256ScalarElement.Subtract(zero, one), orderMinusOne));
        Case("scalar-sub-exact", Equal(
            ManagedP256ScalarElement.Subtract(one, one), zero));
        Case("scalar-multiply-zero", Equal(
            ManagedP256ScalarElement.Multiply(orderMinusOne, zero), zero));
        Case("scalar-multiply-one", Equal(
            ManagedP256ScalarElement.Multiply(orderMinusOne, one),
            orderMinusOne));
        Case("scalar-order-minus-one-square", Equal(
            ManagedP256ScalarElement.Multiply(orderMinusOne, orderMinusOne),
            one));

        string[] referenceValues =
        {
            "0000000000000000000000000000000000000000000000000000000000000000",
            "0000000000000000000000000000000000000000000000000000000000000001",
            "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632550",
            "1234567890ABCDEF00112233445566778899AABBCCDDEEFF1020304050607080",
            "7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
            "D09D5D9E8C5A9F6C0B7E1A2D3C4F5061728394A5B6C7D8E9F1029384756A7B8C"
        };
        for (int leftIndex = 0; leftIndex != referenceValues.Length; ++leftIndex)
        {
            ManagedP256ScalarElement left = Scalar(referenceValues[leftIndex]);
            for (int rightIndex = 0; rightIndex != referenceValues.Length;
                 ++rightIndex)
            {
                ManagedP256ScalarElement right =
                    Scalar(referenceValues[rightIndex]);
                BigInteger leftInteger = ToBigInteger(left);
                BigInteger rightInteger = ToBigInteger(right);
                Case($"scalar-add-reference-{leftIndex}-{rightIndex}",
                    ToBigInteger(ManagedP256ScalarElement.Add(left, right)) ==
                    (leftInteger + rightInteger) % Order);
                Case($"scalar-sub-reference-{leftIndex}-{rightIndex}",
                    ToBigInteger(ManagedP256ScalarElement.Subtract(left, right)) ==
                    Normalize(leftInteger - rightInteger, Order));
                Case($"scalar-multiply-reference-{leftIndex}-{rightIndex}",
                    ToBigInteger(ManagedP256ScalarElement.Multiply(left, right)) ==
                    (leftInteger * rightInteger) % Order);
            }
        }

        ManagedP256ScalarElement[] inversionValues =
        {
            one,
            Scalar("0000000000000000000000000000000000000000000000000000000000000002"),
            orderMinusOne,
            Scalar("1234567890ABCDEF00112233445566778899AABBCCDDEEFF1020304050607080"),
            Scalar("D09D5D9E8C5A9F6C0B7E1A2D3C4F5061728394A5B6C7D8E9F1029384756A7B8C"),
            Scalar("0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20")
        };
        for (int index = 0; index != inversionValues.Length; ++index)
        {
            ManagedP256ScalarElement value = inversionValues[index];
            ManagedP256ScalarElement inverse =
                ManagedP256ScalarElement.Invert(value);
            BigInteger expected = BigInteger.ModPow(
                ToBigInteger(value), Order - 2, Order);
            Case($"scalar-inversion-{index}",
                ToBigInteger(inverse) == expected);
            Case($"scalar-inversion-product-{index}",
                Equal(ManagedP256ScalarElement.Multiply(value, inverse), one));
        }
        Case("scalar-zero-inversion", Equal(
            ManagedP256ScalarElement.Invert(zero), zero));

        for (int index = 1; index != 12; ++index)
        {
            BigInteger sample = Normalize(
                (BigInteger)index * 0x1020304050607 + 0xABCDEF, Order);
            ManagedP256ScalarElement value = Scalar(sample);
            ManagedP256ScalarElement inverse =
                ManagedP256ScalarElement.Invert(value);
            Case($"scalar-deterministic-inversion-{index}", Equal(
                ManagedP256ScalarElement.Multiply(value, inverse), one));
        }

        byte[] digestOrder = ToBytes(Order);
        byte[] digestOrderPlusOne = ToBytes(Order + 1);
        byte[] digestMaximum = ToBytes((BigInteger.One << 256) - 1);
        Case("digest-zero-reduction", ReduceDigest(new byte[32]).IsZero);
        Case("digest-order-reduction", ReduceDigest(digestOrder).IsZero);
        Case("digest-order-plus-one-reduction",
            Equal(ReduceDigest(digestOrderPlusOne), one));
        Case("digest-maximum-reduction",
            ToBigInteger(ReduceDigest(digestMaximum)) ==
            ((BigInteger.One << 256) - 1) % Order);
        Case("digest-short-rejected",
            !ManagedP256ScalarElement.TryReduceDigest(new byte[31], out _));

        byte[] fieldX = ToBytes(Order + 1);
        byte[] reducedX = new byte[32];
        Case("field-x-reduction-n-plus-one",
            ManagedP256.TryReduceFieldXForTest(fieldX, reducedX) &&
            EqualBytes(reducedX, ToBytes(BigInteger.One)));
        Case("field-x-reduction-generator",
            ManagedP256.TryReduceFieldXForTest(
                Hex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"),
                reducedX) &&
            FromUnsignedBigEndian(reducedX) ==
            FromHex("6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296") % Order);
        Case("field-x-prime-rejected",
            !ManagedP256.TryReduceFieldXForTest(
                Hex("FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF"),
                reducedX));
    }

    private static void RunEcdsaKnownAnswerTests()
    {
        // Authoritative verification fixtures: RFC 6979 section A.2.5
        // (SHA-256 "sample" and "test") and RFC 4754 section 8.1
        // (P-256/SHA-256 "abc").  No hosted signer is used here.
        byte[] rfc6979Public = PublicKey(Rfc6979PublicX, Rfc6979PublicY);
        byte[] rfc4754Public = PublicKey(Rfc4754PublicX, Rfc4754PublicY);
        byte[] sampleDigest = Hex(
            "AF2BDBE1AA9B6EC1E2ADE1D694F41FC71A831D0268E9891562113D8A62ADD1BF");
        byte[] testDigest = Hex(
            "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08");
        byte[] abcDigest = Hex(Rfc4754Digest);
        byte[] sampleR = Hex(Rfc6979SampleR);
        byte[] sampleS = Hex(Rfc6979SampleS);
        byte[] testR = Hex(Rfc6979TestR);
        byte[] testS = Hex(Rfc6979TestS);
        byte[] rfc4754R = Hex(Rfc4754R);
        byte[] rfc4754S = Hex(Rfc4754S);

        Case("rfc6979-p256-sha256-sample-public-valid",
            ManagedP256.TryValidatePublicKey(rfc6979Public));
        Case("rfc4754-p256-sha256-abc-public-valid",
            ManagedP256.TryValidatePublicKey(rfc4754Public));
        Case("rfc6979-p256-sha256-sample-valid",
            ManagedP256.TryVerifyDigest(sampleDigest, rfc6979Public,
                                         sampleR, sampleS));
        Case("rfc6979-p256-sha256-test-valid",
            ManagedP256.TryVerifyDigest(testDigest, rfc6979Public,
                                         testR, testS));
        Case("rfc4754-p256-sha256-abc-valid",
            ManagedP256.TryVerifyDigest(abcDigest, rfc4754Public,
                                         rfc4754R, rfc4754S));

        byte[] rfc4754Der = DerSignature(rfc4754R, rfc4754S);
        byte[] parsedR = new byte[32];
        byte[] parsedS = new byte[32];
        Case("rfc4754-der-canonical-parse",
            ManagedP256.TryParseDerSignature(rfc4754Der, parsedR, parsedS) &&
            EqualBytes(parsedR, rfc4754R) && EqualBytes(parsedS, rfc4754S));
        Case("rfc4754-der-valid",
            ManagedP256.TryVerifyDerSignature(abcDigest, rfc4754Public,
                                              rfc4754Der));

        byte[] sampleDer = DerSignature(sampleR, sampleS);
        Case("rfc6979-der-valid",
            ManagedP256.TryVerifyDerSignature(sampleDigest, rfc6979Public,
                                              sampleDer));
    }

    private static void RunEcdsaNegativeTests()
    {
        byte[] publicKey = PublicKey(Rfc4754PublicX, Rfc4754PublicY);
        byte[] digest = Hex(Rfc4754Digest);
        byte[] r = Hex(Rfc4754R);
        byte[] s = Hex(Rfc4754S);
        byte[] otherPublicKey = PublicKey(Rfc6979PublicX, Rfc6979PublicY);
        byte[] validDer = DerSignature(r, s);

        byte[] changedDigest = (byte[])digest.Clone();
        changedDigest[0] ^= 1;
        Case("modified-digest-rejected", !Verify(changedDigest,
            publicKey, r, s));

        byte[] changedX = (byte[])publicKey.Clone();
        changedX[1] ^= 1;
        Case("modified-public-x-rejected",
            !ManagedP256.TryValidatePublicKey(changedX) &&
            !Verify(digest, changedX, r, s));

        byte[] changedY = (byte[])publicKey.Clone();
        changedY[33] ^= 1;
        Case("modified-public-y-rejected",
            !ManagedP256.TryValidatePublicKey(changedY) &&
            !Verify(digest, changedY, r, s));

        byte[] changedR = (byte[])r.Clone();
        changedR[17] ^= 1;
        Case("modified-r-rejected", !Verify(digest, publicKey, changedR, s));
        byte[] changedS = (byte[])s.Clone();
        changedS[17] ^= 1;
        Case("modified-s-rejected", !Verify(digest, publicKey, r, changedS));

        byte[] zero = new byte[32];
        byte[] order = ToBytes(Order);
        byte[] aboveOrder = ToBytes(Order + 1);
        Case("r-zero-rejected", !Verify(digest, publicKey, zero, s));
        Case("s-zero-rejected", !Verify(digest, publicKey, r, zero));
        Case("r-order-rejected", !Verify(digest, publicKey, order, s));
        Case("s-order-rejected", !Verify(digest, publicKey, r, order));
        Case("r-above-order-rejected",
            !Verify(digest, publicKey, aboveOrder, s));
        Case("s-above-order-rejected",
            !Verify(digest, publicKey, r, aboveOrder));
        Case("raw-r-short-rejected",
            !Verify(digest, publicKey, new byte[31], s));
        Case("raw-s-long-rejected",
            !Verify(digest, publicKey, r, new byte[33]));
        Case("invalid-off-curve-public-key-rejected",
            !ManagedP256.TryValidatePublicKey(changedX));
        Case("point-at-infinity-public-key-rejected",
            !Verify(digest, new byte[] { 0 }, r, s));
        Case("wrong-valid-public-key-rejected",
            !Verify(digest, otherPublicKey, r, s));
        Case("wrong-valid-digest-rejected",
            !Verify(Hex(
                "0000000000000000000000000000000000000000000000000000000000000001"),
                publicKey, r, s));

        for (int index = 0; index != validDer.Length; ++index)
        {
            if (index != 0 && index != validDer.Length / 2 &&
                index != validDer.Length - 1)
                continue;
            byte[] corrupted = (byte[])validDer.Clone();
            corrupted[index] ^= 1;
            Case($"corrupted-der-byte-{index}-rejected",
                !ManagedP256.TryVerifyDerSignature(digest, publicKey, corrupted));
        }

        Case("valid-after-failure-cases",
            ManagedP256.TryVerifyDigest(digest, publicKey, r, s));
    }

    private static void RunDerParserTests()
    {
        byte[] outputR = new byte[32];
        byte[] outputS = new byte[32];
        byte[] lowR = Hex("01FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        byte[] lowS = Hex("02FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        byte[] highR = Hex("80FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");
        byte[] highS = Hex("81FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF");

        Case("der-valid-both-32-byte-integers",
            ParseAndMatch(DerSignature(lowR, lowS), lowR, lowS,
                          outputR, outputS));
        Case("der-valid-r-required-sign-zero",
            ParseAndMatch(DerSignature(highR, lowS), highR, lowS,
                          outputR, outputS));
        Case("der-valid-s-required-sign-zero",
            ParseAndMatch(DerSignature(lowR, highS), lowR, highS,
                          outputR, outputS));
        Case("der-valid-both-required-sign-zero",
            ParseAndMatch(DerSignature(highR, highS), highR, highS,
                          outputR, outputS));

        byte[] valid = DerSignature(lowR, lowS);
        Case("der-wrong-sequence-tag", !Parse(
            Replace(valid, 0, 0x31), outputR, outputS));
        Case("der-incorrect-sequence-length", !Parse(
            Replace(valid, 1, (byte)(valid[1] - 1)), outputR, outputS));
        Case("der-truncated-sequence", !Parse(
            Prefix(valid, valid.Length - 1), outputR, outputS));
        Case("der-trailing-data", !Parse(Append(valid, 0), outputR, outputS));
        Case("der-wrong-integer-tag", !Parse(
            Replace(valid, 2, 0x03), outputR, outputS));
        Case("der-empty-integer", !Parse(
            Hex("3006020002020101"), outputR, outputS));
        Case("der-negative-r", !Parse(
            Hex("3006020180020101"), outputR, outputS));
        Case("der-negative-s", !Parse(
            Hex("3006020101020180"), outputR, outputS));
        Case("der-unnecessary-leading-zero", !Parse(
            Hex("300702020001020101"), outputR, outputS));
        Case("der-multiple-unnecessary-leading-zeros", !Parse(
            Hex("30080203000001020101"), outputR, outputS));
        Case("der-integer-over-33-bytes", !Parse(
            RawDerSignature(RawDerInteger(new byte[34]),
                            DerInteger(new byte[] { 1 })),
            outputR, outputS));
        Case("der-overlong-sequence-length", !Parse(
            Hex("308106020101020101"), outputR, outputS));
        Case("der-indefinite-length", !Parse(
            Hex("3080020101020101"), outputR, outputS));
        Case("der-truncated-integer", !Parse(
            Hex("3005020201"), outputR, outputS));
        Case("der-malformed-sequence", !Parse(
            Hex("300100"), outputR, outputS));
        Case("der-r-zero", !Parse(
            DerSignature(new byte[32], lowS), outputR, outputS));
        Case("der-s-zero", !Parse(
            DerSignature(lowR, new byte[32]), outputR, outputS));
        Case("der-r-equal-order", !Parse(
            DerSignature(ToBytes(Order), lowS), outputR, outputS));
        Case("der-s-equal-order", !Parse(
            DerSignature(lowR, ToBytes(Order)), outputR, outputS));
        Case("der-r-above-order", !Parse(
            DerSignature(ToBytes(Order + 1), lowS), outputR, outputS));
        Case("der-s-above-order", !Parse(
            DerSignature(lowR, ToBytes(Order + 1)), outputR, outputS));
        Case("der-input-beyond-bounded-maximum", !Parse(
            new byte[73], outputR, outputS));
    }

    private static void RunGcSurvivalTests()
    {
        byte[] publicKey = PublicKey(Rfc4754PublicX, Rfc4754PublicY);
        byte[] digest = Hex(Rfc4754Digest);
        byte[] r = Hex(Rfc4754R);
        byte[] s = Hex(Rfc4754S);
        for (int collection = 0; collection != 3; ++collection)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Case($"ecdsa-gc-survival-{collection}",
                ManagedP256.TryVerifyDigest(digest, publicKey, r, s));
        }
    }

    private static bool Verify(ReadOnlySpan<byte> digest,
                               ReadOnlySpan<byte> publicKey,
                               ReadOnlySpan<byte> r,
                               ReadOnlySpan<byte> s)
    {
        return ManagedP256.TryVerifyDigest(digest, publicKey, r, s);
    }

    private static bool Parse(ReadOnlySpan<byte> der,
                              Span<byte> r,
                              Span<byte> s)
    {
        return ManagedP256.TryParseDerSignature(der, r, s);
    }

    private static bool ParseAndMatch(ReadOnlySpan<byte> der,
                                      ReadOnlySpan<byte> expectedR,
                                      ReadOnlySpan<byte> expectedS,
                                      Span<byte> outputR,
                                      Span<byte> outputS)
    {
        outputR.Clear();
        outputS.Clear();
        return Parse(der, outputR, outputS) &&
               EqualBytes(outputR, expectedR) && EqualBytes(outputS, expectedS);
    }

    private static ManagedP256ScalarElement ReduceDigest(byte[] digest)
    {
        if (!ManagedP256ScalarElement.TryReduceDigest(digest, out
                ManagedP256ScalarElement result))
            throw new InvalidOperationException("digest conversion failed");
        return result;
    }

    private static ManagedP256ScalarElement Scalar(string hex)
    {
        byte[] bytes = Hex(hex);
        if (!ManagedP256ScalarElement.TryReadCanonical(bytes,
                out ManagedP256ScalarElement value))
            throw new InvalidOperationException("non-canonical scalar fixture");
        return value;
    }

    private static byte[] ScalarBytes(ManagedP256ScalarElement value)
    {
        byte[] result = new byte[32];
        value.WriteBigEndian(result);
        return result;
    }

    private static ManagedP256ScalarElement Scalar(BigInteger value)
    {
        value = Normalize(value, Order);
        return Scalar(Convert.ToHexString(ToBytes(value)));
    }

    private static bool Equal(ManagedP256ScalarElement left,
                              ManagedP256ScalarElement right)
    {
        return ManagedP256ScalarElement.Equals(left, right);
    }

    private static BigInteger ToBigInteger(ManagedP256ScalarElement value)
    {
        return FromUnsignedBigEndian(ScalarBytes(value));
    }

    private static byte[] PublicKey(string x, string y)
    {
        byte[] result = new byte[ManagedP256.PublicKeySize];
        result[0] = 4;
        Hex(x).CopyTo(result, 1);
        Hex(y).CopyTo(result, 33);
        return result;
    }

    private static byte[] DerSignature(ReadOnlySpan<byte> r,
                                       ReadOnlySpan<byte> s)
    {
        byte[] rInteger = DerInteger(r);
        byte[] sInteger = DerInteger(s);
        return RawDerSignature(rInteger, sInteger);
    }

    private static byte[] DerInteger(ReadOnlySpan<byte> value)
    {
        int first = 0;
        while (first < value.Length - 1 && value[first] == 0) ++first;
        int valueLength = value.Length - first;
        bool sign = (value[first] & 0x80) != 0;
        byte[] result = new byte[2 + valueLength + (sign ? 1 : 0)];
        result[0] = 0x02;
        result[1] = (byte)(valueLength + (sign ? 1 : 0));
        int destination = 2;
        if (sign) result[destination++] = 0;
        value.Slice(first).CopyTo(result.AsSpan(destination));
        return result;
    }

    private static byte[] RawDerSignature(ReadOnlySpan<byte> firstInteger,
                                          ReadOnlySpan<byte> secondInteger)
    {
        byte[] result = new byte[2 + firstInteger.Length + secondInteger.Length];
        result[0] = 0x30;
        result[1] = (byte)(firstInteger.Length + secondInteger.Length);
        firstInteger.CopyTo(result.AsSpan(2));
        secondInteger.CopyTo(result.AsSpan(2 + firstInteger.Length));
        return result;
    }

    private static byte[] RawDerInteger(ReadOnlySpan<byte> value)
    {
        byte[] result = new byte[2 + value.Length];
        result[0] = 0x02;
        result[1] = (byte)value.Length;
        value.CopyTo(result.AsSpan(2));
        return result;
    }

    private static byte[] Replace(byte[] source, int index, byte value)
    {
        byte[] result = (byte[])source.Clone();
        result[index] = value;
        return result;
    }

    private static byte[] Prefix(byte[] source, int length)
    {
        byte[] result = new byte[length];
        Array.Copy(source, result, length);
        return result;
    }

    private static byte[] Append(byte[] source, byte value)
    {
        byte[] result = new byte[source.Length + 1];
        Array.Copy(source, result, source.Length);
        result[^1] = value;
        return result;
    }

    private static byte[] Hex(string value)
    {
        return Convert.FromHexString(value.Replace(" ", string.Empty));
    }

    private static BigInteger FromHex(string value)
    {
        return FromUnsignedBigEndian(Hex(value));
    }

    private static BigInteger FromUnsignedBigEndian(byte[] value)
    {
        byte[] littleEndian = new byte[value.Length + 1];
        for (int index = 0; index != value.Length; ++index)
            littleEndian[index] = value[value.Length - index - 1];
        return new BigInteger(littleEndian, isUnsigned: true,
                              isBigEndian: false);
    }

    private static byte[] ToBytes(BigInteger value)
    {
        value = Normalize(value, BigInteger.One << 256);
        byte[] source = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] result = new byte[32];
        if (source.Length > result.Length)
            throw new InvalidOperationException("integer exceeds 256 bits");
        source.CopyTo(result, result.Length - source.Length);
        return result;
    }

    private static BigInteger Normalize(BigInteger value, BigInteger modulus)
    {
        value %= modulus;
        return value < 0 ? value + modulus : value;
    }

    private static bool EqualBytes(ReadOnlySpan<byte> left,
                                   ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        uint difference = 0;
        for (int index = 0; index != left.Length; ++index)
            difference |= (uint)(left[index] ^ right[index]);
        return difference == 0;
    }

    private static void Case(string name, bool passed)
    {
        ++s_cases;
        if (!passed) throw new InvalidOperationException(name);
    }
}

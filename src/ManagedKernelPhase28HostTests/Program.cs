using System;
using System.Numerics;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private const string RfcPrivateA =
        "C88F01F510D9AC3F70A292DAA2316DE544E9AAB8AFE84049C62A9C57862D1433";
    private const string RfcPublicAX =
        "DAD0B65394221CF9B051E1FECA5787D098DFE637FC90B9EF945D0C3772581180";
    private const string RfcPublicAY =
        "5271A0461CDB8252D61F1C456FA3E59AB1F45B33ACCF5F58389E0577B8990BB3";
    private const string RfcPrivateB =
        "C6EF9C5D78AE012A011164ACB397CE2088685D8F06BF9BE0B283AB46476BEE53";
    private const string RfcPublicBX =
        "D12DFB5289C8D4F81208B70270398C342296970A0BCCB74C736FC7554494BF63";
    private const string RfcPublicBY =
        "56FBF3CA366CC23E8157854C13C58D6AAC23F046ADA30F8353E74F33039872AB";
    private const string RfcShared =
        "D6840F6B42F6EDA FD13116E0E12565202FEF8E9ECE7DCE03812464D04B9442DE";
    private const string Rfc9500Private =
        "E6CB5BDD80AA45AE9C95E8C15476679FFEC953C16851E711E743939589C64FC1";
    private const string Rfc9500PublicX =
        "422548F88FB782FFB5ECA3744452C72A1E558FBD6F73BE5E48E93232CC45C5B1";
    private const string Rfc9500PublicY =
        "6C4CD10C4CB8D5B8A17139E94882C8992572993425F41419AB7E90A42A494272";

    /* NIST CAVP ECC CDH Primitive Test Vectors, P-256 records 0 through 3.
       The CAVP file names these fields QCAVS, dIUT, QIUT, and ZIUT. */
    private static readonly string[][] CavpVectors =
    {
        new[]
        {
            "700c48f77f56584c5cc632ca65640db91b6bacce3a4df6b42ce7cc838833d287",
            "db71e509e3fd9b060ddb20ba5c51dcc5948d46fbf640dfe0441782cab85fa4ac",
            "7d7dc5f71eb29ddaf80d6214632eeae03d9058af1fb6d22ed80badb62bc1a534",
            "ead218590119e8876b29146ff89ca61770c4edbbf97d38ce385ed281d8a6b230",
            "28af61281fd35e2fa7002523acc85a429cb06ee6648325389f59edfce1405141",
            "46fc62106420ff012e54a434fbdd2d25ccc5852060561e68040dd7778997bd7b"
        },
        new[]
        {
            "809f04289c64348c01515eb03d5ce7ac1a8cb9498f5caa50197e58d43a86a7ae",
            "b29d84e811197f25eba8f5194092cb6ff440e26d4421011372461f579271cda3",
            "38f65d6dce47676044d58ce5139582d568f64bb16098d179dbab07741dd5caf5",
            "119f2f047902782ab0c9e27a54aff5eb9b964829ca99c06b02ddba95b0a3f6d0",
            "8f52b726664cac366fc98ac7a012b2682cbd962e5acb544671d41b9445704d1d",
            "057d636096cb80b67a8c038c890e887d1adfa4195e9b3ce241c8a778c59cda67"
        },
        new[]
        {
            "a2339c12d4a03c33546de533268b4ad667debf458b464d77443636440ee7fec3",
            "ef48a3ab26e20220bcda2c1851076839dae88eae962869a497bf73cb66faf536",
            "1accfaf1b97712b85a6f54b148985a1bdc4c9bec0bd258cad4b3d603f49f32c8",
            "d9f2b79c172845bfdb560bbb01447ca5ecc0470a09513b6126902c6b4f8d1051",
            "f815ef5ec32128d3487834764678702e64e164ff7315185e23aff5facd96d7bc",
            "2d457b78b4614132477618a5b077965ec90730a8c81a1c75d6d4ec68005d67ec"
        },
        new[]
        {
            "df3989b9fa55495719b3cf46dccd28b5153f7808191dd518eff0c3cff2b705ed",
            "422294ff46003429d739a33206c8752552c8ba54a270defc06e221e0feaf6ac4",
            "207c43a79bfee03db6f4b944f53d2fb76cc49ef1c9c4d34d51b6c65c4db6932d",
            "24277c33f450462dcb3d4801d57b9ced05188f16c28eda873258048cd1607e0d",
            "c4789753e2b1f63b32ff014ec42cd6a69fac81dfe6d0d6fd4af372ae27c46f88",
            "96441259534b80f6aee3d287a6bb17b5094dd4277d9e294f8fe73e48bf2a0024"
        }
    };

    private static readonly BigInteger Prime = FromHex(
        "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF");

    private static int Main()
    {
        try
        {
            RunFieldTests();
            RunPointTests();
            RunKnownAnswerTests();
            RunNegativeTests();
            RunEntropyAndGcTests();
            Console.WriteLine($"MANAGED_KERNEL_PHASE28_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE28_HOST_TESTS_FAIL cases={s_cases} {exception.Message}");
            return 1;
        }
    }

    private static void RunFieldTests()
    {
        ManagedP256FieldElement zero = ManagedP256FieldElement.Zero;
        ManagedP256FieldElement one = ManagedP256FieldElement.One;
        ManagedP256FieldElement pMinusOne = ManagedP256FieldElement.PrimeMinusOne;
        ManagedP256FieldElement gx = Field(
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");
        ManagedP256FieldElement gy = Field(
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");

        Case("field-zero", zero.IsZero);
        Case("field-one", !one.IsZero && ToBigInteger(one) == BigInteger.One);
        Case("field-prime-minus-one", ToBigInteger(pMinusOne) == Prime - 1);
        Case("field-encoding-gx", ToBigInteger(gx) == FromHex(
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"));
        Case("field-encoding-gy", ToBigInteger(gy) == FromHex(
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5"));
        Case("field-p-rejected", !ManagedP256FieldElement.TryRead(
            ToBytes(Prime), out _));
        Case("field-p-plus-one-rejected", !ManagedP256FieldElement.TryRead(
            ToBytes(Prime + 1), out _));
        Case("field-short-rejected", !ManagedP256FieldElement.TryRead(
            new byte[31], out _));

        Case("field-add-wrap", Equal(ManagedP256FieldElement.Add(pMinusOne, one), zero));
        Case("field-sub-wrap", Equal(ManagedP256FieldElement.Subtract(zero, one), pMinusOne));
        Case("field-negate-zero", Equal(ManagedP256FieldElement.Negate(zero), zero));
        Case("field-negate-one", Equal(ManagedP256FieldElement.Negate(one), pMinusOne));
        Case("field-add-commutative", Equal(
            ManagedP256FieldElement.Add(gx, gy),
            ManagedP256FieldElement.Add(gy, gx)));
        Case("field-sub-inverse", Equal(
            ManagedP256FieldElement.Add(gx, ManagedP256FieldElement.Negate(gx)), zero));

        ManagedP256FieldElement[] values =
        {
            zero, one, pMinusOne, gx, gy,
            Field("5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B")
        };
        for (int leftIndex = 0; leftIndex != values.Length; ++leftIndex)
        {
            for (int rightIndex = 0; rightIndex != values.Length; ++rightIndex)
            {
                BigInteger expected = (ToBigInteger(values[leftIndex]) *
                                       ToBigInteger(values[rightIndex])) % Prime;
                Case($"field-multiply-{leftIndex}-{rightIndex}",
                    ToBigInteger(ManagedP256FieldElement.Multiply(
                        values[leftIndex], values[rightIndex])) == expected);
            }
        }

        for (int index = 0; index != 20; ++index)
        {
            BigInteger left = ((BigInteger)(index + 17) * 0x1F12345 + 0xABCDEF) % Prime;
            BigInteger right = ((BigInteger)(index + 31) * (index + 7) *
                               0x100000001 + 19) % Prime;
            ManagedP256FieldElement leftElement = Field(left);
            ManagedP256FieldElement rightElement = Field(right);
            Case($"field-random-multiply-{index}", ToBigInteger(
                ManagedP256FieldElement.Multiply(leftElement, rightElement)) ==
                (left * right) % Prime);
            Case($"field-random-square-{index}", ToBigInteger(
                ManagedP256FieldElement.Square(leftElement)) == (left * left) % Prime);
        }

        ManagedP256FieldElement[] inversionValues = { one, pMinusOne, gx, gy };
        for (int index = 0; index != inversionValues.Length; ++index)
        {
            ManagedP256FieldElement inverse =
                ManagedP256FieldElement.Invert(inversionValues[index]);
            Case($"field-inversion-{index}", ToBigInteger(inverse) ==
                BigInteger.ModPow(ToBigInteger(inversionValues[index]), Prime - 2, Prime));
            Case($"field-inversion-product-{index}", Equal(
                ManagedP256FieldElement.Multiply(inversionValues[index], inverse), one));
        }
        Case("field-zero-inversion", Equal(
            ManagedP256FieldElement.Invert(zero), zero));
        Case("field-maximum-product", ToBigInteger(
            ManagedP256FieldElement.Multiply(pMinusOne, pMinusOne)) == 1);
        Case("field-curve-equation", ManagedP256.IsOnCurveForTest(gx, gy));
    }

    private static void RunPointTests()
    {
        ManagedP256FieldElement gx = Field(
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296");
        ManagedP256FieldElement gy = Field(
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");
        ManagedP256JacobianPoint generator =
            ManagedP256JacobianPoint.FromAffine(gx, gy);
        ManagedP256JacobianPoint infinity = ManagedP256JacobianPoint.Infinity;

        Case("point-infinity-state", infinity.IsInfinity);
        Case("point-add-infinity-left", SameAffine(
            ManagedP256JacobianPoint.Add(infinity, generator), generator));
        Case("point-add-infinity-right", SameAffine(
            ManagedP256JacobianPoint.Add(generator, infinity), generator));
        Case("point-double-add-equal", SameAffine(
            ManagedP256JacobianPoint.Double(generator),
            ManagedP256JacobianPoint.Add(generator, generator)));

        for (int scalarValue = 1; scalarValue != 7; ++scalarValue)
        {
            ManagedP256FieldElement scalar = Field(new BigInteger(scalarValue));
            ManagedP256JacobianPoint actual =
                ManagedP256JacobianPoint.ScalarMultiply(generator, scalar);
            ReferencePoint expected = ReferenceMultiply(
                new BigInteger(scalarValue), new ReferencePoint(
                    ToBigInteger(gx), ToBigInteger(gy)));
            Case($"point-scalar-{scalarValue}", Matches(actual, expected));
        }

        ManagedP256JacobianPoint doubled =
            ManagedP256JacobianPoint.Double(generator);
        ReferencePoint referenceDouble = ReferenceMultiply(
            new BigInteger(2), new ReferencePoint(ToBigInteger(gx), ToBigInteger(gy)));
        Case("point-double-reference", Matches(doubled, referenceDouble));
        Case("point-add-reference", Matches(
            ManagedP256JacobianPoint.Add(generator, doubled),
            ReferenceMultiply(new BigInteger(3), new ReferencePoint(
                ToBigInteger(gx), ToBigInteger(gy)))));
    }

    private static void RunKnownAnswerTests()
    {
        byte[] publicA = PublicKey(RfcPublicAX, RfcPublicAY);
        byte[] publicB = PublicKey(RfcPublicBX, RfcPublicBY);
        byte[] public9500 = PublicKey(Rfc9500PublicX, Rfc9500PublicY);

        byte[] derived = new byte[ManagedP256.PublicKeySize];
        Case("rfc5903-private-a-public", ManagedP256.TryDerivePublicKey(
            Hex(RfcPrivateA), derived) && EqualBytes(derived, publicA));
        Case("rfc5903-private-b-public", ManagedP256.TryDerivePublicKey(
            Hex(RfcPrivateB), derived) && EqualBytes(derived, publicB));
        Case("rfc9500-test-key-public", ManagedP256.TryDerivePublicKey(
            Hex(Rfc9500Private), derived) && EqualBytes(derived, public9500));
        Case("rfc5903-public-a-valid", ManagedP256.TryValidatePublicKey(publicA));
        Case("rfc5903-public-b-valid", ManagedP256.TryValidatePublicKey(publicB));
        Case("rfc9500-public-valid", ManagedP256.TryValidatePublicKey(public9500));

        byte[] sharedA = new byte[ManagedP256.SharedSecretSize];
        byte[] sharedB = new byte[ManagedP256.SharedSecretSize];
        byte[] expectedShared = Hex(RfcShared.Replace(" ", string.Empty));
        Case("rfc5903-ecdh-a-b", ManagedP256.TryDeriveSharedSecret(
            Hex(RfcPrivateA), publicB, sharedA) && EqualBytes(sharedA, expectedShared));
        Case("rfc5903-ecdh-b-a", ManagedP256.TryDeriveSharedSecret(
            Hex(RfcPrivateB), publicA, sharedB) && EqualBytes(sharedB, expectedShared));
        Case("rfc5903-ecdh-symmetry", EqualBytes(sharedA, sharedB));

        byte[] generatorPublic = PublicKey(
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296",
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");
        byte[] oneSecret = new byte[ManagedP256.SharedSecretSize];
        Case("parameter-generator-public", ManagedP256.TryDerivePublicKey(
            ToBytes(BigInteger.One), derived) && EqualBytes(derived, generatorPublic));
        Case("parameter-generator-ecdh", ManagedP256.TryDeriveSharedSecret(
            ToBytes(BigInteger.One), generatorPublic, oneSecret) &&
            EqualBytes(oneSecret, Hex(
                "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296")));

        /* RFC 9500 is a published deterministic peer-key fixture.  Its
           public/private pair is exercised independently from RFC 5903's
           IKE group-19 agreement and is also used by the bare-metal proof. */
        Case("rfc9500-peer-used-by-ecdh", ManagedP256.TryDeriveSharedSecret(
            Hex(RfcPrivateA), public9500, sharedA) &&
            ManagedP256.TryDeriveSharedSecret(
            Hex(RfcPrivateA), public9500, sharedB) && EqualBytes(sharedA, sharedB));

        for (int index = 0; index != CavpVectors.Length; ++index)
        {
            string[] vector = CavpVectors[index];
            byte[] peer = PublicKey(vector[0], vector[1]);
            byte[] privateScalar = Hex(vector[2]);
            byte[] expectedPublic = PublicKey(vector[3], vector[4]);
            byte[] cavpExpectedShared = Hex(vector[5]);
            byte[] actualPublic = new byte[ManagedP256.PublicKeySize];
            byte[] actualShared = new byte[ManagedP256.SharedSecretSize];
            Case($"nist-cavp-p256-{index}-public",
                ManagedP256.TryDerivePublicKey(privateScalar, actualPublic) &&
                EqualBytes(actualPublic, expectedPublic));
            Case($"nist-cavp-p256-{index}-peer-valid",
                ManagedP256.TryValidatePublicKey(peer));
            Case($"nist-cavp-p256-{index}-ecdh",
                ManagedP256.TryDeriveSharedSecret(privateScalar, peer, actualShared) &&
                EqualBytes(actualShared, cavpExpectedShared));
        }
    }

    private static void RunNegativeTests()
    {
        byte[] validPrivate = Hex(RfcPrivateA);
        byte[] validPublic = PublicKey(RfcPublicAX, RfcPublicAY);
        byte[] validPeer = PublicKey(RfcPublicBX, RfcPublicBY);
        byte[] output = new byte[ManagedP256.SharedSecretSize];
        byte[] validDerived = new byte[ManagedP256.PublicKeySize];

        byte[] zero = new byte[32];
        byte[] order = ToBytes(ToBigInteger(ManagedP256FieldElement.Order));
        byte[] orderPlusOne = ToBytes(ToBigInteger(ManagedP256FieldElement.Order) + 1);
        byte[][] invalidPrivates = { zero, order, orderPlusOne, new byte[31] };
        for (int index = 0; index != invalidPrivates.Length; ++index)
        {
            output.AsSpan().Fill(0xA5);
            Case($"invalid-private-{index}",
                !ManagedP256.TryDerivePublicKey(invalidPrivates[index], output) &&
                AllBytes(output, 0xA5));
            Case($"invalid-private-ecdh-{index}",
                !ManagedP256.TryDeriveSharedSecret(invalidPrivates[index], validPublic, output) &&
                AllBytes(output, 0xA5));
            Case($"invalid-private-accepted-after-{index}",
                ManagedP256.TryDerivePublicKey(validPrivate, validDerived) &&
                EqualBytes(validDerived, validPublic));
        }

        byte[][] invalidPublics =
        {
            new byte[64],
            new byte[66],
            Replace(validPublic, 0, 3),
            ReplaceCoordinate(validPublic, true, ToBytes(Prime)),
            ReplaceCoordinate(validPublic, false, ToBytes(Prime)),
            ReplaceCoordinate(validPublic, true, ToBytes(Prime + 1)),
            ReplaceCoordinate(validPublic, false, ToBytes(Prime + 1)),
            PublicKey("0000000000000000000000000000000000000000000000000000000000000001",
                      "0000000000000000000000000000000000000000000000000000000000000001"),
            ReplaceCoordinate(validPublic, true, Flip(Hex(RfcPublicAX), 0)),
            ReplaceCoordinate(validPublic, false, Flip(Hex(RfcPublicAY), 0)),
            new byte[65],
            new byte[] { 0 }
        };
        for (int index = 0; index != invalidPublics.Length; ++index)
        {
            output.AsSpan().Fill(0x5A);
            Case($"invalid-public-{index}",
                !ManagedP256.TryValidatePublicKey(invalidPublics[index]) &&
                !ManagedP256.TryDeriveSharedSecret(validPrivate, invalidPublics[index], output) &&
                AllBytes(output, 0x5A));
            Case($"invalid-public-accepted-after-{index}",
                ManagedP256.TryDeriveSharedSecret(validPrivate, validPublic, output));
        }

        Case("public-output-too-small", !ManagedP256.TryDerivePublicKey(
            validPrivate, new byte[64]));
        Case("shared-output-too-small", !ManagedP256.TryDeriveSharedSecret(
            validPrivate, validPublic, new byte[31]));
        output.AsSpan().Fill(0xC3);
        Case("output-unchanged-on-shared-length-failure",
            !ManagedP256.TryDeriveSharedSecret(validPrivate, validPublic,
                output.AsSpan(0, 31)) && AllBytes(output, 0xC3));

        byte[] overlappingPublic = new byte[65];
        validPrivate.CopyTo(overlappingPublic, 0);
        Case("overlap-private-public-supported", ManagedP256.TryDerivePublicKey(
            overlappingPublic.AsSpan(0, 32), overlappingPublic) &&
            EqualBytes(overlappingPublic, validPublic));

        byte[] overlappingPeer = new byte[65];
        validPeer.CopyTo(overlappingPeer, 0);
        Case("overlap-peer-output-supported", ManagedP256.TryDeriveSharedSecret(
            validPrivate, overlappingPeer, overlappingPeer.AsSpan(33, 32)) &&
            EqualBytes(overlappingPeer.AsSpan(33, 32), Hex(RfcShared)));
    }

    private static void RunEntropyAndGcTests()
    {
        byte[] candidateRejected = new byte[32];
        byte[] candidateAccepted = Hex(RfcPrivateA);
        TestEntropyProvider provider = new(candidateRejected, candidateAccepted);
        ManagedSecureRandom random = new(provider);
        byte[] generated = new byte[ManagedP256.PrivateScalarSize];
        Case("entropy-key-generation", ManagedP256.TryGeneratePrivateKey(random, generated));
        Case("entropy-rejection-sampling", EqualBytes(generated, candidateAccepted));
        Case("entropy-generated-scalar-range", ManagedP256.IsValidScalarForTest(generated));

        byte[] generatedPublic = new byte[ManagedP256.PublicKeySize];
        byte[] generatedShared = new byte[ManagedP256.SharedSecretSize];
        byte[] peer = PublicKey(RfcPublicAX, RfcPublicAY);
        Case("entropy-generated-public", ManagedP256.TryDerivePublicKey(
            generated, generatedPublic));
        Case("entropy-generated-public-valid", ManagedP256.TryValidatePublicKey(generatedPublic));
        Case("entropy-generated-ecdh", ManagedP256.TryDeriveSharedSecret(
            generated, peer, generatedShared));

        TestEntropyProvider unavailableProvider = new();
        ManagedSecureRandom unavailable = new(unavailableProvider);
        generated.AsSpan().Fill(0xD7);
        Case("entropy-unavailable-fail-closed", !ManagedP256.TryGeneratePrivateKey(
            unavailable, generated) && AllBytes(generated, 0xD7));

        for (int collection = 0; collection != 3; ++collection)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            byte[] afterGc = new byte[32];
            Case($"p256-gc-survival-{collection}", ManagedP256.TryDeriveSharedSecret(
                Hex(RfcPrivateA), peer, afterGc));
        }
    }

    private static ManagedP256FieldElement Field(string hex)
    {
        return ManagedP256.ReadFieldForTest(Hex(hex));
    }

    private static ManagedP256FieldElement Field(BigInteger value)
    {
        return Field(ToBytes(value % Prime));
    }

    private static ManagedP256FieldElement Field(byte[] bytes)
    {
        return ManagedP256.ReadFieldForTest(bytes);
    }

    private static byte[] PublicKey(string x, string y)
    {
        byte[] result = new byte[ManagedP256.PublicKeySize];
        result[0] = 4;
        Hex(x).CopyTo(result, 1);
        Hex(y).CopyTo(result, 33);
        return result;
    }

    private static byte[] Replace(byte[] value, int offset, byte replacement)
    {
        byte[] result = (byte[])value.Clone();
        result[offset] = replacement;
        return result;
    }

    private static byte[] ReplaceCoordinate(byte[] publicKey, bool x,
                                            byte[] coordinate)
    {
        byte[] result = (byte[])publicKey.Clone();
        coordinate.CopyTo(result, x ? 1 : 33);
        return result;
    }

    private static byte[] Flip(byte[] value, int offset)
    {
        value[offset] ^= 1;
        return value;
    }

    private static byte[] Hex(string value)
    {
        return Convert.FromHexString(value.Replace(" ", string.Empty));
    }

    private static BigInteger FromHex(string value)
    {
        byte[] bigEndian = Hex(value);
        byte[] unsignedLittleEndian = new byte[bigEndian.Length + 1];
        for (int index = 0; index != bigEndian.Length; ++index)
            unsignedLittleEndian[index] = bigEndian[bigEndian.Length - index - 1];
        return new BigInteger(unsignedLittleEndian, isUnsigned: true,
                              isBigEndian: false);
    }

    private static BigInteger ToBigInteger(ManagedP256FieldElement value)
    {
        byte[] bigEndian = new byte[32];
        value.WriteBigEndian(bigEndian);
        byte[] littleEndian = new byte[33];
        for (int index = 0; index != bigEndian.Length; ++index)
            littleEndian[index] = bigEndian[bigEndian.Length - index - 1];
        return new BigInteger(littleEndian, isUnsigned: true, isBigEndian: false);
    }

    private static string ToHex(ManagedP256FieldElement value)
    {
        byte[] bytes = new byte[32];
        value.WriteBigEndian(bytes);
        return Convert.ToHexString(bytes);
    }

    private static byte[] ToBytes(BigInteger value)
    {
        byte[] source = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] result = new byte[32];
        if (source.Length > result.Length) throw new InvalidOperationException("integer exceeds 256 bits");
        source.CopyTo(result, result.Length - source.Length);
        return result;
    }

    private static bool Equal(ManagedP256FieldElement left,
                              ManagedP256FieldElement right)
    {
        return ManagedP256FieldElement.Equals(left, right);
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

    private static bool AllBytes(ReadOnlySpan<byte> value, byte expected)
    {
        for (int index = 0; index != value.Length; ++index)
            if (value[index] != expected) return false;
        return true;
    }

    private static bool SameAffine(ManagedP256JacobianPoint left,
                                   ManagedP256JacobianPoint right)
    {
        if (!left.TryToAffine(out ManagedP256FieldElement leftX,
                              out ManagedP256FieldElement leftY) ||
            !right.TryToAffine(out ManagedP256FieldElement rightX,
                               out ManagedP256FieldElement rightY))
            return left.IsInfinity && right.IsInfinity;
        return Equal(leftX, rightX) && Equal(leftY, rightY);
    }

    private static bool Matches(ManagedP256JacobianPoint actual,
                                ReferencePoint expected)
    {
        if (expected.Infinity) return actual.IsInfinity;
        return actual.TryToAffine(out ManagedP256FieldElement x,
                                  out ManagedP256FieldElement y) &&
               ToBigInteger(x) == expected.X && ToBigInteger(y) == expected.Y;
    }

    private static ReferencePoint ReferenceMultiply(BigInteger scalar,
                                                    ReferencePoint point)
    {
        ReferencePoint result = ReferencePoint.InfinityPoint;
        ReferencePoint addend = point;
        BigInteger remaining = scalar;
        while (remaining > 0)
        {
            if (!remaining.IsEven) result = ReferenceAdd(result, addend);
            addend = ReferenceAdd(addend, addend);
            remaining >>= 1;
        }
        return result;
    }

    private static ReferencePoint ReferenceAdd(ReferencePoint left,
                                               ReferencePoint right)
    {
        if (left.Infinity) return right;
        if (right.Infinity) return left;
        if (left.X == right.X)
        {
            if ((left.Y + right.Y) % Prime == 0)
                return ReferencePoint.InfinityPoint;
            BigInteger lambda = ((3 * left.X * left.X - 3) *
                                 BigInteger.ModPow(2 * left.Y, Prime - 2, Prime)) % Prime;
            BigInteger xDouble = (lambda * lambda - 2 * left.X) % Prime;
            BigInteger yDouble = (lambda * (left.X - xDouble) - left.Y) % Prime;
            return new ReferencePoint(Normalize(xDouble), Normalize(yDouble));
        }
        BigInteger slope = ((right.Y - left.Y) *
                            BigInteger.ModPow(right.X - left.X, Prime - 2, Prime)) % Prime;
        BigInteger x = (slope * slope - left.X - right.X) % Prime;
        BigInteger y = (slope * (left.X - x) - left.Y) % Prime;
        return new ReferencePoint(Normalize(x), Normalize(y));
    }

    private static BigInteger Normalize(BigInteger value)
    {
        value %= Prime;
        return value < 0 ? value + Prime : value;
    }

    private static void Case(string name, bool passed)
    {
        ++s_cases;
        if (!passed) throw new InvalidOperationException(name);
    }

    private readonly struct ReferencePoint
    {
        internal readonly BigInteger X;
        internal readonly BigInteger Y;
        internal readonly bool Infinity;

        internal ReferencePoint(BigInteger x, BigInteger y)
        {
            X = x;
            Y = y;
            Infinity = false;
        }

        private ReferencePoint(bool infinity)
        {
            X = BigInteger.Zero;
            Y = BigInteger.Zero;
            Infinity = infinity;
        }

        internal static ReferencePoint InfinityPoint => new(true);
    }

    private sealed class TestEntropyProvider : IManagedEntropyProvider
    {
        private readonly byte[][] _candidates;
        private int _index;

        internal TestEntropyProvider(params byte[][] candidates)
        {
            _candidates = candidates;
            IsAvailable = true;
        }

        internal bool IsAvailable { get; set; }

        bool IManagedEntropyProvider.IsAvailable => IsAvailable;

        bool IManagedEntropyProvider.TryFill(Span<byte> destination)
        {
            if (!IsAvailable || _index == _candidates.Length) return false;
            byte[] candidate = _candidates[_index++];
            if (candidate.Length != destination.Length) return false;
            candidate.CopyTo(destination);
            return true;
        }
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            AesKnownAnswerTests();
            AesValidationAndLifecycleTests();
            GhashTests();
            GcmKnownAnswerTests();
            GcmAuthenticationTests();
            GcmValidationTests();
            IndependentOracleTests();
            EntropyIntegrationTests();
            PerformanceSanityTests();
            Console.WriteLine($"MANAGED_KERNEL_PHASE27_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE27_HOST_TESTS_FAIL {exception.Message}");
            return 1;
        }
    }

    private static void AesKnownAnswerTests()
    {
        byte[] fipsKey = Hex("000102030405060708090A0B0C0D0E0F");
        byte[] fipsPlaintext = Hex("00112233445566778899AABBCCDDEEFF");
        byte[] fipsCiphertext = Hex("69C4E0D86A7B0430D8CDB78070B4C55A");
        byte[] output = new byte[16];
        Check(ManagedAes128.TryEncrypt(fipsKey, fipsPlaintext, output),
              "aes-fips-encrypt-success");
        CheckBytes(output, fipsCiphertext, "aes-fips-197-vector");

        byte[] zero = new byte[16];
        byte[] zeroCiphertext = Hex("66E94BD4EF8A2C3B884CFA59CA342B2E");
        Check(ManagedAes128.TryEncrypt(zero, zero, output),
              "aes-zero-encrypt-success");
        CheckBytes(output, zeroCiphertext, "aes-zero-vector");

        byte[] nistKey = Hex("2B7E151628AED2A6ABF7158809CF4F3C");
        byte[] nistPlaintext = Hex("6BC1BEE22E409F96E93D7E117393172A");
        byte[] nistCiphertext = Hex("3AD77BB40D7A3660A89ECAF32466EF97");
        Check(ManagedAes128.TryEncrypt(nistKey, nistPlaintext, output),
              "aes-nist-encrypt-success");
        CheckBytes(output, nistCiphertext, "aes-sp800-38a-vector");

        ManagedAes128 aes = new();
        Check(aes.TrySetKey(fipsKey), "aes-key-schedule-initialize");
        GC.Collect();
        Check(aes.TryEncryptBlock(fipsPlaintext, output), "aes-gc-encrypt-success");
        CheckBytes(output, fipsCiphertext, "aes-gc-expanded-key-survival");
        Check(aes.TryEncryptBlock(fipsPlaintext, output), "aes-repeat-encrypt-success");
        CheckBytes(output, fipsCiphertext, "aes-repeat-deterministic");
        aes.Clear();
    }

    private static void AesValidationAndLifecycleTests()
    {
        ManagedAes128 aes = new();
        byte[] key = Hex("000102030405060708090A0B0C0D0E0F");
        byte[] block = Hex("00112233445566778899AABBCCDDEEFF");
        byte[] output = new byte[16];
        Check(!aes.TryEncryptBlock(block, output), "aes-uninitialized-rejected");
        Check(!aes.TrySetKey(new byte[15]), "aes-short-key-rejected");
        Check(!aes.TrySetKey(new byte[17]), "aes-long-key-rejected");
        Check(!aes.TryEncryptBlock(new byte[15], output), "aes-short-block-rejected");
        Check(!aes.TryEncryptBlock(block, new byte[15]), "aes-short-output-rejected");
        Check(aes.TrySetKey(key), "aes-reinitialize-after-invalid-key");
        Check(aes.TryEncryptBlock(block, output), "aes-reinitialize-encrypt-success");
        CheckBytes(output, Hex("69C4E0D86A7B0430D8CDB78070B4C55A"),
                   "aes-reinitialize-vector");
        byte[] guardedOutput = new byte[18];
        guardedOutput[0] = 0xA5;
        guardedOutput[17] = 0x5A;
        Check(aes.TryEncryptBlock(block, guardedOutput.AsSpan(1, 16)),
              "aes-exact-boundary-success");
        Check(guardedOutput[0] == 0xA5 && guardedOutput[17] == 0x5A,
              "aes-exact-boundary-guards");
        aes.Reset();
        Check(!aes.IsInitialized && !aes.TryEncryptBlock(block, output),
              "aes-reset-clears-key-state");
    }

    private static void GhashTests()
    {
        byte[] zeroHashSubkey = new byte[16];
        byte[] zeroDigest = new byte[16];
        Check(ManagedGhash.TryCompute(zeroHashSubkey, ReadOnlySpan<byte>.Empty,
                                       ReadOnlySpan<byte>.Empty, zeroDigest),
              "ghash-empty-success");
        CheckBytes(zeroDigest, new byte[16], "ghash-empty-zero");

        byte[] h = Hex("66E94BD4EF8A2C3B884CFA59CA342B2E");
        byte[] oneBlock = Hex("0388DACE60B6A392F328C2B971B2FE78");
        byte[] oneBlockExpected = Hex("F38CBB1AD69223DCC3457AE5B6B0F885");
        Check(ManagedGhash.TryCompute(h, ReadOnlySpan<byte>.Empty, oneBlock,
                                      zeroDigest), "ghash-one-block-success");
        CheckBytes(zeroDigest, oneBlockExpected, "ghash-nist-one-block");

        byte[] case3Ciphertext = Hex(
            "42831EC2217774244B7221B784D0D49CE3AA212F2C02A4E035C17E2329ACA12E" +
            "21D514B25466931C7D8F6A5AAC84AA051BA30B396A0AAC973D58E091473F5985");
        byte[] case3HashSubkey = Hex("B83B533708BF535D0AA6E52980D53B78");
        byte[] case3Expected = Hex("7F1B32B81B820D02614F8895AC1D4EAC");
        Check(ManagedGhash.TryCompute(case3HashSubkey,
                                      ReadOnlySpan<byte>.Empty, case3Ciphertext,
                                      zeroDigest), "ghash-multiple-block-success");
        CheckBytes(zeroDigest, case3Expected, "ghash-nist-multiple-block");

        byte[] partialAad = Hex("FEEDFACEDEADBEEFFEEDFACEDEADBEEFABADDAD2");
        byte[] partialCiphertext = Hex(
            "42831EC2217774244B7221B784D0D49CE3AA212F2C02A4E035C17E2329ACA12E" +
            "21D514B25466931C7D8F6A5AAC84AA051BA30B396A0AAC973D58E091");
        byte[] partialExpected = Hex("698E57F70E6ECC7FD9463B7260A9AE5F");
        Check(ManagedGhash.TryCompute(case3HashSubkey, partialAad,
                                      partialCiphertext, zeroDigest),
              "ghash-aad-and-partial-ciphertext-success");
        CheckBytes(zeroDigest, partialExpected, "ghash-nist-aad-and-partial");

        ManagedGhash incremental = new();
        Check(incremental.TryInitialize(case3HashSubkey),
              "ghash-incremental-initialize");
        Check(incremental.AppendAad(partialAad.AsSpan(0, 7)) &&
              incremental.AppendAad(partialAad.AsSpan(7)),
              "ghash-incremental-aad");
        Check(incremental.AppendCiphertext(partialCiphertext.AsSpan(0, 13)) &&
              incremental.AppendCiphertext(partialCiphertext.AsSpan(13)),
              "ghash-incremental-ciphertext");
        Check(incremental.TryFinalize(zeroDigest), "ghash-incremental-finalize");
        CheckBytes(zeroDigest, partialExpected, "ghash-incremental-vector");
        Check(!incremental.TryFinalize(zeroDigest), "ghash-double-finalize-rejected");
        incremental.Reset();
        Check(incremental.AppendAad(partialAad) &&
              incremental.AppendCiphertext(partialCiphertext) &&
              incremental.TryFinalize(zeroDigest), "ghash-reset-reuse");
        CheckBytes(zeroDigest, partialExpected, "ghash-reset-reuse-vector");
        incremental.Clear();
        Check(!incremental.AppendAad(partialAad), "ghash-cleared-state-rejected");
    }

    private static void GcmKnownAnswerTests()
    {
        byte[] zeroKey = new byte[16];
        byte[] zeroNonce = new byte[12];
        byte[] emptyCiphertext = new byte[1];
        byte[] emptyTag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(zeroKey, zeroNonce,
                                       ReadOnlySpan<byte>.Empty,
                                       ReadOnlySpan<byte>.Empty,
                                       emptyCiphertext.AsSpan(0, 0), emptyTag),
              "gcm-empty-encrypt-success");
        CheckBytes(emptyTag, Hex("58E2FCCEFA7E3061367F1D57A4E7455A"),
                   "gcm-nist-empty-vector");
        byte[] emptyPlaintext = new byte[1];
        Check(ManagedAesGcm.TryDecrypt(zeroKey, zeroNonce,
                                       ReadOnlySpan<byte>.Empty,
                                       ReadOnlySpan<byte>.Empty, emptyTag,
                                       emptyPlaintext.AsSpan(0, 0)),
              "gcm-empty-decrypt-success");

        byte[] onePlaintext = new byte[16];
        byte[] oneCiphertext = new byte[16];
        byte[] oneTag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(zeroKey, zeroNonce,
                                       ReadOnlySpan<byte>.Empty, onePlaintext,
                                       oneCiphertext, oneTag),
              "gcm-one-block-encrypt-success");
        CheckBytes(oneCiphertext, Hex("0388DACE60B6A392F328C2B971B2FE78"),
                   "gcm-nist-one-block-ciphertext");
        CheckBytes(oneTag, Hex("AB6E47D42CEC13BDF53A67B21257BDDF"),
                   "gcm-nist-one-block-tag");
        byte[] recovered = new byte[16];
        Check(ManagedAesGcm.TryDecrypt(zeroKey, zeroNonce,
                                       ReadOnlySpan<byte>.Empty, oneCiphertext,
                                       oneTag, recovered),
              "gcm-one-block-decrypt-success");
        CheckBytes(recovered, onePlaintext, "gcm-one-block-round-trip");

        byte[] case3Key = Hex("FEFFE9928665731C6D6A8F9467308308");
        byte[] case3Nonce = Hex("CAFEBABEFACEDBADDECAF888");
        byte[] case3Plaintext = Hex(
            "D9313225F88406E5A55909C5AFF5269A86A7A9531534F7DA2E4C303D8A318A72" +
            "1C3C0C95956809532FCF0E2449A6B525B16AEDF5AA0DE657BA637B391AAFD255");
        byte[] case3Ciphertext = Hex(
            "42831EC2217774244B7221B784D0D49CE3AA212F2C02A4E035C17E2329ACA12E" +
            "21D514B25466931C7D8F6A5AAC84AA051BA30B396A0AAC973D58E091473F5985");
        byte[] case3Tag = Hex("4D5C2AF327CD64A62CF35ABD2BA6FAB4");
        byte[] output = new byte[case3Plaintext.Length];
        byte[] tag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(case3Key, case3Nonce,
                                       ReadOnlySpan<byte>.Empty, case3Plaintext,
                                       output, tag), "gcm-case3-encrypt-success");
        CheckBytes(output, case3Ciphertext, "gcm-nist-case3-ciphertext");
        CheckBytes(tag, case3Tag, "gcm-nist-case3-tag");
        Array.Clear(output);
        Check(ManagedAesGcm.TryDecrypt(case3Key, case3Nonce,
                                       ReadOnlySpan<byte>.Empty, case3Ciphertext,
                                       case3Tag, output), "gcm-case3-decrypt-success");
        CheckBytes(output, case3Plaintext, "gcm-nist-case3-plaintext");

        byte[] aad = Hex("FEEDFACEDEADBEEFFEEDFACEDEADBEEFABADDAD2");
        byte[] case13Plaintext = Hex(
            "D9313225F88406E5A55909C5AFF5269A86A7A9531534F7DA2E4C303D8A318A72" +
            "1C3C0C95956809532FCF0E2449A6B525B16AEDF5AA0DE657BA637B39");
        byte[] case13Ciphertext = Hex(
            "42831EC2217774244B7221B784D0D49CE3AA212F2C02A4E035C17E2329ACA12E" +
            "21D514B25466931C7D8F6A5AAC84AA051BA30B396A0AAC973D58E091");
        byte[] case13Tag = Hex("5BC94FBC3221A5DB94FAE95AE7121A47");
        output = new byte[case13Plaintext.Length];
        tag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(case3Key, case3Nonce, aad,
                                       case13Plaintext, output, tag),
              "gcm-aad-partial-encrypt-success");
        CheckBytes(output, case13Ciphertext, "gcm-nist-aad-partial-ciphertext");
        CheckBytes(tag, case13Tag, "gcm-nist-aad-partial-tag");
    }

    private static void GcmAuthenticationTests()
    {
        byte[] key = Hex("000102030405060708090A0B0C0D0E0F");
        byte[] nonce = Hex("101112131415161718191A1B");
        byte[] aad = Hex("202122232425262728292A2B");
        byte[] plaintext = Hex("303132333435363738393A3B3C3D3E3F404142434445");
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(key, nonce, aad, plaintext, ciphertext, tag),
              "gcm-auth-fixture-encrypt");

        byte[] failedOutput = new byte[plaintext.Length];
        Array.Fill(failedOutput, (byte)0xA5);
        byte[] corruptedTag = (byte[])tag.Clone();
        corruptedTag[0] ^= 1;
        Check(!ManagedAesGcm.TryDecrypt(key, nonce, aad, ciphertext, corruptedTag,
                                        failedOutput), "gcm-first-tag-corruption-fails");
        Check(AllBytes(failedOutput, 0xA5), "gcm-first-tag-no-plaintext");

        corruptedTag = (byte[])tag.Clone();
        corruptedTag[7] ^= 1;
        Check(!ManagedAesGcm.TryDecrypt(key, nonce, aad, ciphertext, corruptedTag,
                                        failedOutput), "gcm-middle-tag-corruption-fails");
        Check(AllBytes(failedOutput, 0xA5), "gcm-middle-tag-no-plaintext");

        corruptedTag = (byte[])tag.Clone();
        corruptedTag[15] ^= 1;
        Check(!ManagedAesGcm.TryDecrypt(key, nonce, aad, ciphertext, corruptedTag,
                                        failedOutput), "gcm-last-tag-corruption-fails");
        Check(AllBytes(failedOutput, 0xA5), "gcm-last-tag-no-plaintext");

        byte[] corruptedCiphertext = (byte[])ciphertext.Clone();
        corruptedCiphertext[plaintext.Length / 2] ^= 1;
        Check(!ManagedAesGcm.TryDecrypt(key, nonce, aad, corruptedCiphertext, tag,
                                        failedOutput), "gcm-ciphertext-corruption-fails");
        Check(AllBytes(failedOutput, 0xA5), "gcm-ciphertext-no-plaintext");

        byte[] corruptedAad = (byte[])aad.Clone();
        corruptedAad[0] ^= 1;
        Check(!ManagedAesGcm.TryDecrypt(key, nonce, corruptedAad, ciphertext, tag,
                                        failedOutput), "gcm-aad-corruption-fails");
        Check(AllBytes(failedOutput, 0xA5), "gcm-aad-no-plaintext");

        byte[] corruptedNonce = (byte[])nonce.Clone();
        corruptedNonce[11] ^= 1;
        Check(!ManagedAesGcm.TryDecrypt(key, corruptedNonce, aad, ciphertext, tag,
                                        failedOutput), "gcm-nonce-corruption-fails");
        Check(AllBytes(failedOutput, 0xA5), "gcm-nonce-no-plaintext");

        byte[] wrongKey = (byte[])key.Clone();
        wrongKey[3] ^= 1;
        Check(!ManagedAesGcm.TryDecrypt(wrongKey, nonce, aad, ciphertext, tag,
                                        failedOutput), "gcm-wrong-key-fails");
        Check(AllBytes(failedOutput, 0xA5), "gcm-wrong-key-no-plaintext");

        Array.Clear(failedOutput);
        Check(ManagedAesGcm.TryDecrypt(key, nonce, aad, ciphertext, tag,
                                       failedOutput), "gcm-recovery-after-failure");
        CheckBytes(failedOutput, plaintext, "gcm-recovery-plaintext");
    }

    private static void GcmValidationTests()
    {
        byte[] key = new byte[16];
        byte[] nonce = new byte[12];
        byte[] aad = new byte[4];
        byte[] plaintext = new byte[32];
        byte[] ciphertext = new byte[32];
        byte[] tag = new byte[16];
        Check(!ManagedAesGcm.TryEncrypt(new byte[15], nonce, aad, plaintext,
                                        ciphertext, tag), "gcm-short-key-rejected");
        Check(!ManagedAesGcm.TryEncrypt(new byte[17], nonce, aad, plaintext,
                                        ciphertext, tag), "gcm-long-key-rejected");
        Check(!ManagedAesGcm.TryEncrypt(key, new byte[11], aad, plaintext,
                                        ciphertext, tag), "gcm-short-nonce-rejected");
        Check(!ManagedAesGcm.TryEncrypt(key, new byte[13], aad, plaintext,
                                        ciphertext, tag), "gcm-long-nonce-rejected");
        Check(!ManagedAesGcm.TryEncrypt(key, nonce, aad, plaintext, ciphertext,
                                        new byte[15]), "gcm-short-tag-rejected");
        Check(!ManagedAesGcm.TryEncrypt(key, nonce, aad, plaintext, ciphertext,
                                        new byte[17]), "gcm-long-tag-rejected");
        Check(!ManagedAesGcm.TryEncrypt(key, nonce, new byte[257], plaintext,
                                        ciphertext, tag), "gcm-aad-over-capacity-rejected");
        Check(!ManagedAesGcm.TryEncrypt(key, nonce, aad,
                                        new byte[ManagedAesGcm.MaximumPayloadBytes + 1],
                                        new byte[ManagedAesGcm.MaximumPayloadBytes + 1], tag),
              "gcm-plaintext-over-capacity-rejected");
        Check(!ManagedAesGcm.TryEncrypt(key, nonce, aad, plaintext,
                                        new byte[31], tag), "gcm-small-encrypt-output-rejected");
        Check(!ManagedAesGcm.TryDecrypt(key, nonce, aad, ciphertext, tag,
                                        new byte[31]), "gcm-small-decrypt-output-rejected");

        byte[] maxAad = new byte[ManagedAesGcm.MaximumAadBytes];
        byte[] maxPlaintext = new byte[ManagedAesGcm.MaximumPayloadBytes];
        byte[] maxCiphertext = new byte[maxPlaintext.Length];
        byte[] maxTag = new byte[ManagedAesGcm.TagSize];
        Check(ManagedAesGcm.TryEncrypt(key, nonce, maxAad, maxPlaintext,
                                       maxCiphertext, maxTag), "gcm-aad-maximum-accepted");
        Check(ManagedAesGcm.TryDecrypt(key, nonce, maxAad, maxCiphertext, maxTag,
                                       maxPlaintext), "gcm-payload-maximum-accepted");
        Check(!ManagedAesGcm.TryEncrypt(key, nonce, aad, plaintext,
                                        ciphertext.AsSpan(0, 31), tag),
              "gcm-exact-output-boundary-rejected");

        byte[] overlap = new byte[64];
        Check(!ManagedAesGcm.TryEncrypt(key, nonce, aad,
                                        overlap.AsSpan(0, 32), overlap.AsSpan(1, 32),
                                        tag), "gcm-overlap-encrypt-rejected");
        Check(!ManagedAesGcm.TryDecrypt(key, nonce, aad,
                                        ciphertext, tag, ciphertext.AsSpan(0, 32)),
              "gcm-overlap-decrypt-rejected");
    }

    private static void IndependentOracleTests()
    {
        byte[] key = Hex("000102030405060708090A0B0C0D0E0F");
        byte[] nonce = Hex("A0A1A2A3A4A5A6A7A8A9AAAB");
        byte[] aad = new byte[37];
        byte[] plaintext = new byte[47];
        for (int index = 0; index != aad.Length; ++index) aad[index] = (byte)(0x30 + index);
        for (int index = 0; index != plaintext.Length; ++index) plaintext[index] = (byte)(0x80 + index);

        byte[] expectedCiphertext = new byte[plaintext.Length];
        byte[] expectedTag = new byte[16];
        using (AesGcm oracle = new(key, 16))
        {
            oracle.Encrypt(nonce, plaintext, expectedCiphertext, expectedTag, aad);
        }

        byte[] actualCiphertext = new byte[plaintext.Length];
        byte[] actualTag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(key, nonce, aad, plaintext,
                                       actualCiphertext, actualTag),
              "gcm-independent-oracle-encrypt-success");
        CheckBytes(actualCiphertext, expectedCiphertext,
                   "gcm-independent-oracle-ciphertext");
        CheckBytes(actualTag, expectedTag, "gcm-independent-oracle-tag");
        byte[] actualPlaintext = new byte[plaintext.Length];
        Check(ManagedAesGcm.TryDecrypt(key, nonce, aad, actualCiphertext,
                                       actualTag, actualPlaintext),
              "gcm-independent-oracle-decrypt-success");
        CheckBytes(actualPlaintext, plaintext, "gcm-independent-oracle-plaintext");
    }

    private static void EntropyIntegrationTests()
    {
        EntropyFixture fixture = new();
        ManagedEntropyService service = new(fixture);
        ManagedSecureRandom random = new(service);
        byte[] nonce = new byte[ManagedAesGcm.NonceSize];
        Check(random.IsAvailable && random.TryFill(nonce),
              "phase26-secure-random-12-byte-nonce");
        Check(service.LastProvider == ManagedEntropyProviderKind.Hardware &&
              fixture.FillCount == 1 && !AllZero(nonce),
              "phase26-secure-random-no-fallback");
        byte[] key = new byte[16];
        byte[] plaintext = Hex("706861736532372D6E6F6E63652D6C697665");
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];
        Check(ManagedAesGcm.TryEncrypt(key, nonce, ReadOnlySpan<byte>.Empty,
                                       plaintext, ciphertext, tag),
              "phase26-nonce-gcm-encrypt");
        byte[] recovered = new byte[plaintext.Length];
        Check(ManagedAesGcm.TryDecrypt(key, nonce, ReadOnlySpan<byte>.Empty,
                                       ciphertext, tag, recovered),
              "phase26-nonce-gcm-decrypt");
        CheckBytes(recovered, plaintext, "phase26-nonce-gcm-round-trip");
        GC.Collect();
        Check(random.TryFill(nonce) && fixture.FillCount == 2,
              "phase26-random-gc-survival");
    }

    private static void PerformanceSanityTests()
    {
        byte[] key = new byte[16];
        byte[] nonce = new byte[12];
        byte[] input = new byte[ManagedAesGcm.MaximumPayloadBytes];
        byte[] output = new byte[input.Length];
        byte[] tag = new byte[16];
        Stopwatch stopwatch = Stopwatch.StartNew();
        Check(ManagedAesGcm.TryEncrypt(key, nonce, ReadOnlySpan<byte>.Empty,
                                       input, output, tag), "gcm-16k-encrypt-sanity");
        stopwatch.Stop();
        long encryptMilliseconds = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();
        Check(ManagedAesGcm.TryDecrypt(key, nonce, ReadOnlySpan<byte>.Empty,
                                       output, tag, input), "gcm-16k-decrypt-sanity");
        stopwatch.Stop();
        Console.WriteLine($"MANAGED_KERNEL_PHASE27_PERF encrypt_16k_ms={encryptMilliseconds} decrypt_16k_ms={stopwatch.ElapsedMilliseconds}");
    }

    private static byte[] Hex(string value) => Convert.FromHexString(value.Replace(" ", ""));

    private static bool AllBytes(ReadOnlySpan<byte> value, byte expected)
    {
        foreach (byte item in value) if (item != expected) return false;
        return true;
    }

    private static bool AllZero(ReadOnlySpan<byte> value) => AllBytes(value, 0);

    private static void CheckBytes(ReadOnlySpan<byte> actual,
                                   ReadOnlySpan<byte> expected,
                                   string name)
    {
        Check(actual.SequenceEqual(expected), name);
    }

    private static void Check(bool condition, string name)
    {
        s_cases++;
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class EntropyFixture : IManagedEntropyProvider
    {
        internal int FillCount { get; private set; }

        public bool IsAvailable => true;

        public bool TryFill(Span<byte> destination)
        {
            FillCount++;
            for (int index = 0; index != destination.Length; ++index)
            {
                destination[index] = (byte)(0xC0 + ((FillCount + index) & 0x1F));
            }
            return true;
        }
    }
}

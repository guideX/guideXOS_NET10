using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        Sha256Tests();
        HmacTests();
        ComparisonTests();
        EntropyTests();
        Console.WriteLine($"MANAGED_KERNEL_PHASE25_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void Sha256Tests()
    {
        CheckSha(string.Empty,
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
            "sha256-empty");
        CheckSha("abc",
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            "sha256-abc");
        CheckSha("abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq",
            "248D6A61D20638B8E5C026930C3E6039A33CE45964FF2167F6ECEDD419DB06C1",
            "sha256-standard-multiblock");

        string[] boundaryExpected =
        {
            "9F4390F8D30C2DD92EC9F095B65E2B9AE9B0A925A5258E241C9F1E910F734318",
            "B35439A4AC6F0948B6D6F9E3C6AF0F5F590CE20F1BDE7090EF7970686EC6738A",
            "7D3E74A05D7DB15BCE4AD9EC0658EA98E3F06EEECF16B4C6FFF2DA457DDC2F34",
            "FFE054FE7AE0CB6DC65C3AF9B61D5209F439851DB43D0BA5997337DF154668EB",
            "635361C48BB9EAB14198E76EA8AB7F1A41685D6AD62AA9146D301D4F17EB0AE0"
        };
        for (int length = 55; length <= 65; ++length)
        {
            if (length == 57 || length == 58 || length == 59 || length == 60 ||
                length == 61 || length == 62)
            {
                continue;
            }
            CheckSha(new string('a', length), boundaryExpected[BoundaryIndex(length)],
                "sha256-boundary-" + length);
        }

        byte[] segmentedMessage =
            "The quick brown fox jumps over the lazy dog. NativeAOT segmentation."u8.ToArray();
        byte[] segmentedDigest = new byte[ManagedSha256.DigestSize];
        ManagedSha256 segmented = new();
        Check(segmented.Append(segmentedMessage.AsSpan(0, 1)) &&
              segmented.Append(segmentedMessage.AsSpan(1, 7)), "sha256-segment-prefix");
        GC.Collect();
        Check(segmented.Append(segmentedMessage.AsSpan(8, 13)) &&
              segmented.Append(segmentedMessage.AsSpan(21)) &&
              segmented.TryFinalize(segmentedDigest), "sha256-segment-finish");
        byte[] oneShotDigest = new byte[ManagedSha256.DigestSize];
        Check(ManagedSha256.TryHash(segmentedMessage, oneShotDigest) &&
              ManagedCryptoComparison.FixedTimeEquals(segmentedDigest, oneShotDigest),
            "sha256-segmented-equals-one-shot");

        ManagedSha256 byteAtATime = new();
        for (int index = 0; index != segmentedMessage.Length; ++index)
        {
            Check(byteAtATime.Append(segmentedMessage.AsSpan(index, 1)),
                "sha256-byte-at-a-time-" + index);
        }
        Check(byteAtATime.TryFinalize(segmentedDigest) &&
              ManagedCryptoComparison.FixedTimeEquals(segmentedDigest, oneShotDigest),
            "sha256-byte-at-a-time-equals-one-shot");

        byteAtATime.Reset();
        Check(byteAtATime.Append("abc"u8) && byteAtATime.TryFinalize(segmentedDigest) &&
              ManagedCryptoComparison.FixedTimeEquals(segmentedDigest, Sha256AbcExpected),
            "sha256-reset-reuse");
        Check(!byteAtATime.Append("after-finalize"u8), "sha256-finalized-rejects-update");
        Check(!byteAtATime.TryFinalize(segmentedDigest.AsSpan(0, 31)),
            "sha256-short-output-rejected");

        ManagedSha256 gcHash = new();
        byte[] gcMessage = "GC must not invalidate incremental SHA-256 state."u8.ToArray();
        Check(gcHash.Append(gcMessage.AsSpan(0, 9)), "sha256-gc-prefix");
        GC.Collect();
        Check(gcHash.Append(gcMessage.AsSpan(9)) && gcHash.TryFinalize(segmentedDigest) &&
              ManagedSha256.TryHash(gcMessage, oneShotDigest) &&
              ManagedCryptoComparison.FixedTimeEquals(segmentedDigest, oneShotDigest),
            "sha256-gc-survival");
    }

    private static void HmacTests()
    {
        byte[] key20 = Repeated(0x0B, 20);
        byte[] keyAa = Repeated(0xAA, 20);
        byte[] keyLong = Repeated(0xAA, 131);
        CheckHmac(key20, "Hi There"u8,
            "B0344C61D8DB38535CA8AFCEAF0BF12B881DC200C9833DA726E9376C2E32CFF7",
            "hmac-rfc4231-short-key");
        CheckHmac("Jefe"u8, "what do ya want for nothing?"u8,
            "5BDCC146BF60754E6A042426089575C75A003F089D2739839DEC58B964EC3843",
            "hmac-rfc4231-jefe");
        CheckHmac(keyAa, Repeated(0xDD, 50),
            "773EA91E36800E46854DB8EBD09181A72959098B3EF8C122D9635514CED565FE",
            "hmac-rfc4231-repeated-byte-key");
        CheckHmac(keyLong, "Test Using Larger Than Block-Size Key - Hash Key First"u8,
            "60E431591EE0B67F0D8A26AACBF5B77F8E0BC6213728C5140546040F0EE37F54",
            "hmac-rfc4231-long-key");
        CheckHmac(keyLong,
            "Test Using Larger Than Block-Size Key and Larger Than One Block-Size Data"u8,
            "C9731F25665706DAB8200D9CE68FAD2CBAC48EFC4A5F72292E4EEB81E7D29298",
            "hmac-rfc4231-long-key-long-data");
        CheckHmac(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty,
            "B613679A0814D9EC772F95D778C35FC5FF1697C493715653C6C712144292C5AD",
            "hmac-empty-key-empty-message");

        byte[] segmented = "segmented HMAC input remains equivalent."u8.ToArray();
        byte[] incrementalMac = new byte[ManagedHmacSha256.DigestSize];
        Check(ManagedHmacSha256.TryCreate("key"u8, out ManagedHmacSha256? hmac) &&
              hmac != null && hmac.Append(segmented.AsSpan(0, 4)),
            "hmac-segment-prefix");
        GC.Collect();
        Check(hmac != null && hmac.Append(segmented.AsSpan(4, 11)) &&
              hmac.Append(segmented.AsSpan(15)) && hmac.TryFinalize(incrementalMac),
            "hmac-segment-finish");
        byte[] oneShotMac = new byte[ManagedHmacSha256.DigestSize];
        Check(ManagedHmacSha256.TryCompute("key"u8, segmented, oneShotMac) &&
              ManagedCryptoComparison.FixedTimeEquals(incrementalMac, oneShotMac),
            "hmac-segmented-equals-one-shot");
        Check(hmac != null, "hmac-incremental-instance-retained");
        hmac!.Reset();
        Check(hmac.Append(segmented) && hmac.TryFinalize(incrementalMac) &&
              ManagedCryptoComparison.FixedTimeEquals(incrementalMac, oneShotMac),
            "hmac-reset-reuse");
        byte[] corrupted = (byte[])oneShotMac.Clone();
        corrupted[17] ^= 1;
        Check(!ManagedCryptoComparison.FixedTimeEquals(corrupted, oneShotMac),
            "hmac-corrupted-expected-mismatch");
        hmac.Clear();
    }

    private static void ComparisonTests()
    {
        Check(ManagedCryptoComparison.FixedTimeEquals("same"u8, "same"u8),
            "constant-time-equal");
        Check(!ManagedCryptoComparison.FixedTimeEquals(
                  new byte[] { 0x00, 0x02, 0x03 }, new byte[] { 0x01, 0x02, 0x03 }),
            "constant-time-first-byte-mismatch");
        Check(!ManagedCryptoComparison.FixedTimeEquals(
                  new byte[] { 0x01, 0x00, 0x03 }, new byte[] { 0x01, 0x02, 0x03 }),
            "constant-time-middle-byte-mismatch");
        Check(!ManagedCryptoComparison.FixedTimeEquals(
                  new byte[] { 0x01, 0x02, 0x00 }, new byte[] { 0x01, 0x02, 0x03 }),
            "constant-time-last-byte-mismatch");
        Check(ManagedCryptoComparison.FixedTimeEquals(
                  ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty),
            "constant-time-zero-length");
        Check(!ManagedCryptoComparison.FixedTimeEquals(
                  new byte[] { 0x01 }, ReadOnlySpan<byte>.Empty),
            "constant-time-length-mismatch");
    }

    private static void EntropyTests()
    {
        DeterministicTestEntropy firstProvider = new(0x12345678U);
        DeterministicTestEntropy secondProvider = new(0x12345678U);
        ManagedSecureRandom first = new(firstProvider);
        ManagedSecureRandom second = new(secondProvider);
        byte[] firstBytes = new byte[64];
        byte[] secondBytes = new byte[64];
        Check(first.IsAvailable && first.TryFill(firstBytes),
            "entropy-deterministic-provider-available");
        Check(second.TryFill(secondBytes) &&
              ManagedCryptoComparison.FixedTimeEquals(firstBytes, secondBytes),
            "entropy-deterministic-provider-reproducible");
        Check(first.TryFill(secondBytes) &&
              !ManagedCryptoComparison.FixedTimeEquals(firstBytes, secondBytes),
            "entropy-successive-samples-differ");
        Check(first.TryFill(Span<byte>.Empty), "entropy-zero-length-fill");
        Check(!first.TryFill(new byte[ManagedSecureRandom.MaximumBytesPerFill + 1]),
            "entropy-max-plus-one-rejected");

        DeterministicTestEntropy unavailableProvider = new(1U) { Available = false };
        ManagedSecureRandom unavailable = new(unavailableProvider);
        Check(!unavailable.IsAvailable && !unavailable.TryFill(new byte[8]),
            "entropy-unavailable-fails-closed");

        DeterministicTestEntropy failingProvider = new(2U) { FailuresRemaining = 1 };
        ManagedSecureRandom failing = new(failingProvider);
        byte[] recovery = new byte[16];
        Check(!failing.TryFill(recovery), "entropy-injected-failure-visible");
        Check(failing.TryFill(recovery), "entropy-failure-recovery");

        NativeHardwareEntropy noHardware = new(0, 0, ManagedSecureRandom.MaximumBytesPerFill);
        ManagedSecureRandom productionShape = new(noHardware);
        Check(!productionShape.IsAvailable && !productionShape.TryFill(new byte[8]),
            "entropy-production-provider-no-test-fallback");

        GC.Collect();
        Check(first.TryFill(firstBytes), "entropy-gc-survival");
    }

    private static int BoundaryIndex(int length)
    {
        return length switch
        {
            55 => 0,
            56 => 1,
            63 => 2,
            64 => 3,
            65 => 4,
            _ => throw new InvalidOperationException("Unexpected SHA boundary")
        };
    }

    private static readonly byte[] Sha256AbcExpected =
    {
        0xBA, 0x78, 0x16, 0xBF, 0x8F, 0x01, 0xCF, 0xEA,
        0x41, 0x41, 0x40, 0xDE, 0x5D, 0xAE, 0x22, 0x23,
        0xB0, 0x03, 0x61, 0xA3, 0x96, 0x17, 0x7A, 0x9C,
        0xB4, 0x10, 0xFF, 0x61, 0xF2, 0x00, 0x15, 0xAD
    };

    private static void CheckSha(string text, string expected, string name)
    {
        byte[] output = new byte[ManagedSha256.DigestSize];
        Check(ManagedSha256.TryHash(System.Text.Encoding.ASCII.GetBytes(text), output) &&
              Convert.ToHexString(output) == expected, name);
    }

    private static void CheckHmac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data,
                                  string expected, string name)
    {
        byte[] output = new byte[ManagedHmacSha256.DigestSize];
        Check(ManagedHmacSha256.TryCompute(key, data, output) &&
              Convert.ToHexString(output) == expected, name);
    }

    private static byte[] Repeated(byte value, int count)
    {
        byte[] result = new byte[count];
        Array.Fill(result, value);
        return result;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        ++s_cases;
        Console.WriteLine("PASS: " + name);
    }

    private sealed class DeterministicTestEntropy : IManagedEntropyProvider
    {
        private uint _state;

        internal DeterministicTestEntropy(uint seed)
        {
            _state = seed;
            Available = true;
        }

        internal bool Available { get; set; }
        internal int FailuresRemaining { get; set; }

        public bool IsAvailable => Available;

        public bool TryFill(Span<byte> destination)
        {
            if (!Available || destination.Length > ManagedSecureRandom.MaximumBytesPerFill)
            {
                return false;
            }
            if (FailuresRemaining != 0)
            {
                --FailuresRemaining;
                destination.Clear();
                return false;
            }
            for (int index = 0; index != destination.Length; ++index)
            {
                _state ^= _state << 13;
                _state ^= _state >> 17;
                _state ^= _state << 5;
                destination[index] = (byte)_state;
            }
            return true;
        }
    }
}

using System;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly byte[] s_name = Encoding.ASCII.GetBytes("phase20.test");
    private static readonly byte[] s_missing =
        Encoding.ASCII.GetBytes("missing.phase20.test");
    private static readonly byte[] s_server = { 10, 15, 0, 2 };
    private static readonly byte[] s_answer = { 10, 15, 0, 2 };
    private static int s_cases;

    private static int Main()
    {
        QueryEncodingTests();
        HeaderAndQuestionTests();
        NameCompressionTests();
        ResourceRecordTests();
        ResolverStateTests();
        DhcpIntegrationTests();
        Console.WriteLine($"MANAGED_KERNEL_PHASE20_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void QueryEncodingTests()
    {
        byte[] encoded = Encode(s_name);
        Check(encoded.AsSpan().SequenceEqual(new byte[]
        {
            7, (byte)'p', (byte)'h', (byte)'a', (byte)'s', (byte)'e',
            (byte)'2', (byte)'0', 4, (byte)'t', (byte)'e', (byte)'s', (byte)'t', 0
        }), "exact-phase20-qname");

        byte[] query = new byte[ManagedDnsProtocol.MaximumMessageLength];
        Check(ManagedDnsProtocol.TryBuildQuery(query, 0x2001, s_name,
                  out ushort queryLength) && queryLength == 30 &&
              Read16(query, 0) == 0x2001 && Read16(query, 2) == 0x0100 &&
              Read16(query, 4) == 1 && Read16(query, 6) == 0 &&
              Read16(query, 8) == 0 && Read16(query, 10) == 0 &&
              query.AsSpan(12, encoded.Length).SequenceEqual(encoded) &&
              Read16(query, 26) == ManagedDnsProtocol.TypeA &&
              Read16(query, 28) == ManagedDnsProtocol.ClassIn,
              "exact-dns-header-and-question");
        Check(!ManagedDnsProtocol.TryEncodeName(Array.Empty<byte>(),
                  new byte[255], out _), "empty-name-rejected");
        Check(!ManagedDnsProtocol.TryEncodeName(Encoding.ASCII.GetBytes("a..b"),
                  new byte[255], out _), "empty-interior-label-rejected");
        Check(!ManagedDnsProtocol.TryEncodeName(new byte[64], new byte[255],
                  out _), "label-over-63-rejected");
        byte[] tooLong = new byte[255];
        Array.Fill(tooLong, (byte)'a');
        Check(!ManagedDnsProtocol.TryEncodeName(tooLong, new byte[255], out _),
              "hostname-over-253-rejected");
        byte[] maximum = Encoding.ASCII.GetBytes(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa." +
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb." +
            "ccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc." +
            "ddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd");
        Check(maximum.Length == 253 &&
              ManagedDnsProtocol.TryEncodeName(maximum, new byte[255], out _),
              "maximum-bounded-name-accepted");
        Check(!ManagedDnsProtocol.TryBuildQuery(query, 0, s_name, out _),
              "zero-dns-id-rejected");
        Check(!ManagedDnsProtocol.TryBuildQuery(new byte[16], 1, s_name, out _),
              "short-query-buffer-rejected");
    }

    private static void HeaderAndQuestionTests()
    {
        byte[] valid = BuildResponse(0x2001, s_name);
        Check(Parse(valid, s_name) == ManagedDnsResult.Resolved,
              "valid-compressed-response");
        byte[] shortHeader = new byte[11];
        Check(Parse(shortHeader, s_name) == ManagedDnsResult.Malformed,
              "short-dns-header-rejected");
        byte[] wrongId = (byte[])valid.Clone();
        Write16(wrongId, 0, 0x2002);
        Check(Parse(wrongId, s_name) == ManagedDnsResult.TransactionMismatch,
              "wrong-dns-id-rejected");
        byte[] notResponse = (byte[])valid.Clone();
        Write16(notResponse, 2, 0x0100);
        Check(Parse(notResponse, s_name) == ManagedDnsResult.NotResponse,
              "qr-zero-rejected");
        byte[] opcode = (byte[])valid.Clone();
        Write16(opcode, 2, (ushort)(0x8180 | (1 << 11)));
        Check(Parse(opcode, s_name) == ManagedDnsResult.UnsupportedOpcode,
              "unsupported-opcode-rejected");
        byte[] noQuestion = (byte[])valid.Clone();
        Write16(noQuestion, 4, 0);
        Check(Parse(noQuestion, s_name) == ManagedDnsResult.Malformed,
              "zero-question-count-rejected");
        byte[] questionName = BuildResponse(0x2001,
            Encoding.ASCII.GetBytes("other.phase20.test"));
        Check(Parse(questionName, s_name) == ManagedDnsResult.Malformed,
              "mismatched-question-name-rejected");
        byte[] questionType = BuildResponse(0x2001, s_name,
                                             questionType: 28);
        Check(Parse(questionType, s_name) == ManagedDnsResult.Malformed,
              "wrong-question-type-rejected");
        byte[] questionClass = BuildResponse(0x2001, s_name,
                                              questionClass: 3);
        Check(Parse(questionClass, s_name) == ManagedDnsResult.Malformed,
              "wrong-question-class-rejected");
        byte[] caseVariant = BuildResponse(0x2001,
            Encoding.ASCII.GetBytes("PHASE20.TEST"));
        Check(Parse(caseVariant, s_name) == ManagedDnsResult.Resolved,
              "case-insensitive-question-accepted");
        byte[] tc = BuildResponse(0x2001, s_name, flags: 0x8380);
        Check(Parse(tc, s_name) == ManagedDnsResult.Truncated,
              "truncated-response-rejected");
        byte[] unsupportedRcode = BuildResponse(0x2001, s_name, flags: 0x8182,
                                                  answerCount: 0);
        Check(Parse(unsupportedRcode, s_name) == ManagedDnsResult.UnsupportedRcode,
              "unsupported-rcode-rejected");
        byte[] excessiveAnswers = BuildResponse(0x2001, s_name,
                                                 answerCount: 9);
        Check(Parse(excessiveAnswers, s_name) == ManagedDnsResult.Malformed,
              "answer-count-bound-enforced");
    }

    private static void NameCompressionTests()
    {
        byte[] encoded = Encode(s_name);
        Span<byte> decoded = stackalloc byte[ManagedDnsProtocol.MaximumDecodedNameLength];
        Check(ManagedDnsProtocol.TryDecodeName(encoded, 0, decoded,
                  out int consumed, out int decodedLength) && consumed == encoded.Length &&
              decoded.Slice(0, decodedLength).SequenceEqual(encoded),
              "uncompressed-name-decoding");

        byte[] compressed = { 0xC0, 0x0C };
        byte[] message = new byte[32];
        encoded.CopyTo(message.AsSpan(12));
        compressed.CopyTo(message.AsSpan(0));
        Check(ManagedDnsProtocol.TryDecodeName(message, 0, decoded,
                  out consumed, out decodedLength) && consumed == 2 &&
              decoded.Slice(0, decodedLength).SequenceEqual(encoded),
              "compressed-pointer-to-question");
        Check(!ManagedDnsProtocol.TryDecodeName(new byte[] { 0xC0, 0xFF }, 0,
                  decoded, out _, out _), "compression-pointer-out-of-range");
        Check(!ManagedDnsProtocol.TryDecodeName(new byte[] { 0xC0, 0x00 }, 0,
                  decoded, out _, out _), "compression-self-loop");

        byte[] cycle = new byte[36];
        cycle[0] = 0xC0; cycle[1] = 2;
        cycle[2] = 0xC0; cycle[3] = 0;
        Check(!ManagedDnsProtocol.TryDecodeName(cycle, 0, decoded,
                  out _, out _), "compression-two-pointer-loop");

        byte[] depth = new byte[12 + 34];
        for (int index = 0; index != 17; ++index)
        {
            int offset = 12 + index * 2;
            int target = index == 16 ? 12 : offset + 2;
            depth[offset] = (byte)(0xC0 | (target >> 8));
            depth[offset + 1] = (byte)target;
        }
        Check(!ManagedDnsProtocol.TryDecodeName(depth, 12, decoded,
                  out _, out _), "compression-depth-bound");

        byte[] truncatedLabel = { 3, (byte)'a', (byte)'b' };
        Check(!ManagedDnsProtocol.TryDecodeName(truncatedLabel, 0, decoded,
                  out _, out _), "truncated-label-rejected");
        byte[] longLabel = new byte[65];
        longLabel[0] = 64;
        Check(!ManagedDnsProtocol.TryDecodeName(longLabel, 0, decoded,
                  out _, out _), "decoded-label-over-63-rejected");

        byte[] malformedOwner = BuildResponse(0x2001, s_name);
        int answerOffset = 30;
        malformedOwner[answerOffset] = 0xC0;
        malformedOwner[answerOffset + 1] = 0xFF;
        Check(Parse(malformedOwner, s_name) == ManagedDnsResult.Malformed,
              "malformed-compressed-answer-name-rejected");
    }

    private static void ResourceRecordTests()
    {
        byte[] uncompressed = BuildResponse(0x2001, s_name, compressedOwner: false);
        Check(Parse(uncompressed, s_name) == ManagedDnsResult.Resolved,
              "uncompressed-a-answer");
        byte[] invalidLength = BuildResponse(0x2001, s_name, dataLength: 3);
        Check(Parse(invalidLength, s_name) == ManagedDnsResult.Malformed,
              "a-rdata-length-three-rejected");
        byte[] tooLong = BuildResponse(0x2001, s_name, dataLength: 5);
        Check(Parse(tooLong, s_name) == ManagedDnsResult.Malformed,
              "a-rdata-length-beyond-message-rejected");
        byte[] truncatedRr = BuildResponse(0x2001, s_name);
        Check(Parse(truncatedRr.AsSpan(0, truncatedRr.Length - 2).ToArray(), s_name) ==
                  ManagedDnsResult.Malformed, "truncated-rr-rejected");
        byte[] wrongOwner = BuildResponse(0x2001, s_name,
                                           compressedOwner: false,
                                           ownerName: Encode(
                                               Encoding.ASCII.GetBytes("other.test")));
        Check(Parse(wrongOwner, s_name) == ManagedDnsResult.NoAddress,
              "nonmatching-a-owner-does-not-resolve");
        byte[] unsupportedThenA = BuildResponseWithUnsupportedThenA();
        Check(Parse(unsupportedThenA, s_name) == ManagedDnsResult.Resolved,
              "unsupported-rr-skipped-bounded");
        byte[] badOwner = BuildResponse(0x2001, s_name);
        badOwner[30] = 0x40;
        Check(Parse(badOwner, s_name) == ManagedDnsResult.Malformed,
              "reserved-name-length-rejected");
    }

    private static void ResolverStateTests()
    {
        ManagedDnsResolver resolver = new();
        Check(!resolver.TryStartQuery(s_name), "resolver-refuses-without-dhcp-dns");
        Check(resolver.TryInstallServer(s_server), "dhcp-dns-server-install");
        Check(resolver.TryStartQuery(s_name) && resolver.IsActive &&
              resolver.Attempts == 1 && resolver.TransactionId != 0,
              "one-outstanding-query-start");
        Check(!resolver.TryStartQuery(s_missing) &&
              resolver.Result == ManagedDnsResult.OutstandingQuery,
              "second-query-rejected-while-active");
        byte[] query = new byte[512];
        Check(resolver.TryBuildQuery(query, out ushort length) && length == 30 &&
              Read16(query, 0) == resolver.TransactionId &&
              Read16(query, 2) == 0x0100 && Read16(query, 26) == 1 &&
              Read16(query, 28) == 1, "resolver-builds-a-in-query");
        Check(resolver.TryProcessResponse(54, ManagedDnsResolver.ClientPort,
                  BuildResponse(resolver.TransactionId, s_name)) ==
                  ManagedDnsResult.PortMismatch && resolver.IsActive,
              "wrong-dns-source-port-rejected");
        Check(resolver.TryProcessResponse(53, 15201,
                  BuildResponse(resolver.TransactionId, s_name)) ==
                  ManagedDnsResult.PortMismatch && resolver.IsActive,
              "wrong-dns-client-port-rejected");
        Check(resolver.TryProcessResponse(53, 15200,
                  BuildResponse((ushort)(resolver.TransactionId + 1), s_name)) ==
                  ManagedDnsResult.TransactionMismatch && resolver.IsActive,
              "resolver-wrong-id-does-not-complete");
        ushort firstId = resolver.TransactionId;
        Check(resolver.TryRetry() && resolver.TransactionId != firstId &&
              resolver.Attempts == 2, "retry-changes-dns-transaction");
        Check(resolver.TryProcessResponse(53, 15200,
                  BuildResponse(resolver.TransactionId, s_name)) ==
                  ManagedDnsResult.Resolved && !resolver.IsActive &&
              resolver.HasResolvedAddress && resolver.ResolvedIpv4.SequenceEqual(s_answer) &&
              resolver.Ttl == 300, "resolver-extracts-address-and-ttl");

        Check(resolver.TryStartQuery(s_missing) &&
              resolver.TryProcessResponse(53, 15200,
                  BuildNxDomain(resolver.TransactionId, s_missing)) ==
                  ManagedDnsResult.NxDomain && !resolver.HasResolvedAddress,
              "nxdomain-clears-result");
        Check(resolver.TryStartQuery(s_name) && resolver.IsActive,
              "resolver-reusable-after-nxdomain");
        GC.Collect();
        Check(resolver.HasServer && resolver.IsActive,
              "resolver-state-survives-gc");
        resolver.ResetForTeardown();
        Check(!resolver.HasServer && !resolver.IsActive &&
              !resolver.HasResolvedAddress && resolver.TransactionId == 0,
              "resolver-teardown-clears-state");
        Check(!resolver.TryStartQuery(s_name), "resolver-reinit-needs-fresh-dns");
    }

    private static void DhcpIntegrationTests()
    {
        byte[] options = new byte[]
        {
            6, 4, 10, 15, 0, 2, 53, 1, 5, 255
        };
        byte[] parameterList = new byte[16];
        Check(ManagedDhcpv4Protocol.TryParseOptions(options, parameterList,
                  out ManagedDhcpv4OptionValues parsed) && parsed.DnsCount == 1 &&
              parsed.DnsServer1 == 0x0A0F0002,
              "dhcp-option-six-parsed");
        ManagedDnsResolver resolver = new();
        Check(!resolver.HasServer && !resolver.TryStartQuery(s_name),
              "pre-ack-dns-is-not-authoritative");
        Check(resolver.TryInstallServer(new byte[] { 10, 15, 0, 2 }) &&
              resolver.HasServer, "ack-committed-dns-becomes-authoritative");
        resolver.ResetForDhcp();
        Check(!resolver.HasServer && !resolver.TryStartQuery(s_name),
              "new-dhcp-cycle-clears-old-dns");
    }

    private static ManagedDnsResult Parse(byte[] message, byte[] name)
    {
        byte[] encoded = Encode(name);
        return ManagedDnsProtocol.TryParseResponse(message, 0x2001, encoded,
                                                    out _, out _);
    }

    private static byte[] Encode(byte[] name)
    {
        byte[] encoded = new byte[ManagedDnsProtocol.MaximumEncodedNameLength];
        Check(ManagedDnsProtocol.TryEncodeName(name, encoded, out ushort length),
              "test-name-encoding");
        return encoded.AsSpan(0, length).ToArray();
    }

    private static byte[] BuildResponse(ushort id, byte[] name,
                                        ushort flags = 0x8180,
                                        bool compressedOwner = true,
                                        ushort answerCount = 1,
                                        ushort questionType = 1,
                                        ushort questionClass = 1,
                                        ushort dataLength = 4,
                                        byte[]? ownerName = null)
    {
        byte[] encoded = Encode(name);
        byte[] query = new byte[512];
        Check(ManagedDnsProtocol.TryBuildQuery(query, id, name,
                  out ushort queryLength), "test-query-build");
        byte[] response = new byte[512];
        Write16(response, 0, id);
        Write16(response, 2, flags);
        Write16(response, 4, 1);
        Write16(response, 6, answerCount);
        Write16(response, 8, 0);
        Write16(response, 10, 0);
        query.AsSpan(12, queryLength - 12).CopyTo(response.AsSpan(12));
        int questionTypeOffset = 12 + encoded.Length;
        Write16(response, questionTypeOffset, questionType);
        Write16(response, questionTypeOffset + 2, questionClass);
        int offset = queryLength;
        if (answerCount != 0)
        {
            if (compressedOwner)
            {
                response[offset++] = 0xC0;
                response[offset++] = 0x0C;
            }
            else
            {
                (ownerName ?? encoded).CopyTo(response.AsSpan(offset));
                offset += (ownerName ?? encoded).Length;
            }
            Write16(response, offset, ManagedDnsProtocol.TypeA);
            Write16(response, offset + 2, ManagedDnsProtocol.ClassIn);
            ManagedEthernetProtocol.WriteUInt32Network(response, offset + 4, 300);
            Write16(response, offset + 8, dataLength);
            offset += 10;
            s_answer.CopyTo(response.AsSpan(offset));
            offset += s_answer.Length;
        }
        return response.AsSpan(0, offset).ToArray();
    }

    private static byte[] BuildNxDomain(ushort id, byte[] name)
    {
        return BuildResponse(id, name, flags: 0x8183, answerCount: 0);
    }

    private static byte[] BuildResponseWithUnsupportedThenA()
    {
        byte[] encoded = Encode(s_name);
        byte[] response = new byte[512];
        Write16(response, 0, 0x2001);
        Write16(response, 2, 0x8180);
        Write16(response, 4, 1);
        Write16(response, 6, 2);
        byte[] query = new byte[512];
        Check(ManagedDnsProtocol.TryBuildQuery(query, 0x2001, s_name,
                  out ushort queryLength), "unsupported-test-query-build");
        query.AsSpan(12, queryLength - 12).CopyTo(response.AsSpan(12));
        int offset = queryLength;
        response[offset++] = 0xC0; response[offset++] = 0x0C;
        Write16(response, offset, 16); Write16(response, offset + 2, 1);
        ManagedEthernetProtocol.WriteUInt32Network(response, offset + 4, 1);
        Write16(response, offset + 8, 1); offset += 10;
        response[offset++] = 0xAA;
        response[offset++] = 0xC0; response[offset++] = 0x0C;
        Write16(response, offset, 1); Write16(response, offset + 2, 1);
        ManagedEthernetProtocol.WriteUInt32Network(response, offset + 4, 300);
        Write16(response, offset + 8, 4); offset += 10;
        s_answer.CopyTo(response.AsSpan(offset)); offset += 4;
        return response.AsSpan(0, offset).ToArray();
    }

    private static ushort Read16(byte[] bytes, int offset)
    {
        return ManagedEthernetProtocol.ReadUInt16Network(bytes, offset);
    }

    private static void Write16(byte[] bytes, int offset, ushort value)
    {
        ManagedEthernetProtocol.WriteUInt16Network(bytes, offset, value);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {name}");
        s_cases++;
        Console.WriteLine($"PASS: {name}");
    }
}

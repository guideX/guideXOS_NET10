using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly byte[] s_localIp = { 10, 15, 0, 1 };
    private static readonly byte[] s_peerIp = { 10, 15, 0, 2 };
    private static readonly byte[] s_otherIp = { 10, 15, 1, 2 };
    private static readonly byte[] s_localMac =
        { 0x52, 0x54, 0x00, 0x12, 0x34, 0x56 };
    private static readonly byte[] s_peerMac =
        { 0x02, 0x15, 0x00, 0x00, 0x00, 0x02 };
    private static int s_cases;

    private static int Main()
    {
        Ipv4ParsingTests();
        Ipv4ConstructionTests();
        IcmpTests();
        ArpIntegrationTests();
        Console.WriteLine($"MANAGED_KERNEL_PHASE17_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void Ipv4ParsingTests()
    {
        byte[] packet = BuildIpv4(0x0042, 0, 0x40, 1,
                                  s_localIp, s_peerIp, new byte[] { 1, 2 });
        Check(ManagedIpv4Protocol.TryParse(packet, Ip(s_peerIp),
                  out ManagedIpv4Packet parsed) && parsed.Version == 4 &&
              parsed.HeaderLength == 20 && parsed.TotalLength == 22 &&
              parsed.Protocol == 1 && parsed.SourceAddress.SequenceEqual(s_localIp) &&
              parsed.Payload.SequenceEqual(new byte[] { 1, 2 }),
              "valid-minimal-ipv4");

        byte[] wrongVersion = (byte[])packet.Clone();
        wrongVersion[0] = 0x65;
        Check(!ManagedIpv4Protocol.TryParse(wrongVersion, Ip(s_peerIp), out _),
              "wrong-version-rejected");
        byte[] shortIhl = (byte[])packet.Clone();
        shortIhl[0] = 0x44;
        Check(!ManagedIpv4Protocol.TryParse(shortIhl, Ip(s_peerIp), out _),
              "ihl-less-than-five-rejected");
        byte[] options = (byte[])packet.Clone();
        options[0] = 0x46;
        Check(!ManagedIpv4Protocol.TryParse(options, Ip(s_peerIp), out _),
              "ipv4-options-rejected");
        Check(!ManagedIpv4Protocol.TryParse(packet.AsSpan(0, 19), Ip(s_peerIp),
                                            out _), "truncated-ipv4-header-rejected");

        byte[] totalShort = (byte[])packet.Clone();
        totalShort[2] = 0;
        totalShort[3] = 19;
        Check(!ManagedIpv4Protocol.TryParse(totalShort, Ip(s_peerIp), out _),
              "total-length-less-than-header-rejected");
        byte[] totalLong = (byte[])packet.Clone();
        totalLong[2] = 0x01;
        totalLong[3] = 0x00;
        Check(!ManagedIpv4Protocol.TryParse(totalLong, Ip(s_peerIp), out _),
              "declared-length-beyond-buffer-rejected");
        byte[] badChecksum = (byte[])packet.Clone();
        badChecksum[10] ^= 0x01;
        Check(!ManagedIpv4Protocol.TryParse(badChecksum, Ip(s_peerIp), out _),
              "bad-ipv4-checksum-rejected");
        byte[] wrongDestination = BuildIpv4(0x0042, 0, 0x40, 1,
                                            s_localIp, s_otherIp, new byte[] { 1, 2 });
        Check(!ManagedIpv4Protocol.TryParse(wrongDestination, Ip(s_peerIp), out _),
              "wrong-destination-rejected");

        byte[] unsupportedProtocol = BuildIpv4(0x0042, 0, 0x40, 17,
                                                s_localIp, s_peerIp,
                                                new byte[] { 1, 2 });
        Check(ManagedIpv4Protocol.TryParse(unsupportedProtocol, Ip(s_peerIp),
                  out ManagedIpv4Packet unsupported) && unsupported.Protocol == 17,
              "unsupported-protocol-bounded");
        byte[] moreFragments = BuildIpv4(0x0042, 0x2000, 0x40, 1,
                                         s_localIp, s_peerIp, Array.Empty<byte>());
        Check(!ManagedIpv4Protocol.TryParse(moreFragments, Ip(s_peerIp), out _),
              "more-fragments-rejected");
        byte[] fragmentOffset = BuildIpv4(0x0042, 1, 0x40, 1,
                                          s_localIp, s_peerIp, Array.Empty<byte>());
        Check(!ManagedIpv4Protocol.TryParse(fragmentOffset, Ip(s_peerIp), out _),
              "fragment-offset-rejected");
        byte[] dontFragment = BuildIpv4(0x0042, 0x4000, 0x40, 1,
                                        s_localIp, s_peerIp, Array.Empty<byte>());
        Check(ManagedIpv4Protocol.TryParse(dontFragment, Ip(s_peerIp), out _),
              "dont-fragment-accepted");

        byte[] known =
        {
            0x45, 0x00, 0x00, 0x3C, 0x1C, 0x46, 0x40, 0x00,
            0x40, 0x06, 0xB1, 0xE6, 0xAC, 0x10, 0x0A, 0x63,
            0xAC, 0x10, 0x0A, 0x0C
        };
        Check(ManagedIpv4Protocol.ComputeChecksum(known) == 0,
              "known-ipv4-checksum-vector");
        Check(ManagedIpv4Protocol.ComputeChecksum(packet.AsSpan(0, 20)) == 0,
              "generated-ipv4-checksum-verifies");
        byte[] mutation = (byte[])packet.Clone();
        mutation[19] ^= 0x01;
        Check(ManagedIpv4Protocol.ComputeChecksum(mutation.AsSpan(0, 20)) != 0,
              "ipv4-checksum-mutation-fails");
    }

    private static void Ipv4ConstructionTests()
    {
        byte[] packet = new byte[ManagedIpv4Protocol.MaximumPacketLength];
        Check(ManagedIpv4Protocol.TryBuild(
                  packet, 0x0042, 0, ManagedIpv4Protocol.DefaultTtl, 1,
                  s_localIp, s_peerIp, new byte[] { 1, 2 },
                  out ushort length) && length == 22,
              "ipv4-construction-length");
        byte[] expected =
        {
            0x45, 0x00, 0x00, 0x16, 0x00, 0x42, 0x00, 0x00,
            0x40, 0x01, 0x66, 0x85, 0x0A, 0x0F, 0x00, 0x01,
            0x0A, 0x0F, 0x00, 0x02, 0x01, 0x02
        };
        Check(packet.AsSpan(0, length).SequenceEqual(expected),
              "exact-ipv4-wire-bytes");
        Check(packet[22] == 0, "ipv4-construction-does-not-overrun");
        Check(ManagedIpv4Protocol.TryBuild(
                  packet, 0x0042, 0x4000, 63, 17, s_localIp, s_peerIp,
                  Array.Empty<byte>(), out ushort dfLength) &&
              ManagedIpv4Protocol.TryParse(packet.AsSpan(0, dfLength),
                  Ip(s_peerIp), out ManagedIpv4Packet df) &&
              df.Ttl == 63 && df.Protocol == 17 &&
              df.FlagsFragmentOffset == 0x4000,
              "ipv4-fields-round-trip");
    }

    private static void IcmpTests()
    {
        byte[] request = new byte[264];
        Check(ManagedIcmpv4Protocol.TryBuildEchoRequest(
                  request, 0x1234, 1, new byte[] { 1, 2 },
                  out ushort requestLength) && requestLength == 10,
              "icmp-request-construction-length");
        byte[] expectedRequest =
            { 8, 0, 0xE4, 0xC8, 0x12, 0x34, 0, 1, 1, 2 };
        Check(request.AsSpan(0, requestLength).SequenceEqual(expectedRequest),
              "exact-icmp-echo-request-bytes");
        Check(ManagedIcmpv4Protocol.TryParse(
                  request.AsSpan(0, requestLength), out ManagedIcmpv4Packet requestView) &&
              requestView.Type == ManagedIcmpv4Protocol.EchoRequest &&
              requestView.Identifier == 0x1234 && requestView.Sequence == 1 &&
              requestView.Payload.SequenceEqual(new byte[] { 1, 2 }),
              "valid-icmp-echo-request");

        Check(ManagedIcmpv4Protocol.TryBuildEchoReply(
                  request, 0x1234, 1, new byte[] { 1, 2 },
                  out ushort replyLength) &&
              request.AsSpan(0, replyLength).SequenceEqual(
                  new byte[] { 0, 0, 0xEC, 0xC8, 0x12, 0x34, 0, 1, 1, 2 }),
              "exact-icmp-echo-reply-bytes");
        Check(ManagedIcmpv4Protocol.TryParse(
                  request.AsSpan(0, replyLength), out ManagedIcmpv4Packet replyView) &&
              replyView.Type == ManagedIcmpv4Protocol.EchoReply,
              "valid-icmp-echo-reply");
        Check(!ManagedIcmpv4Protocol.TryParse(new byte[7], out _),
              "too-short-icmp-rejected");
        byte[] invalidChecksum = request.AsSpan(0, replyLength).ToArray();
        invalidChecksum[2] ^= 1;
        Check(!ManagedIcmpv4Protocol.TryParse(invalidChecksum, out _),
              "bad-icmp-checksum-rejected");
        byte[] invalidCode = request.AsSpan(0, replyLength).ToArray();
        invalidCode[1] = 1;
        Check(!ManagedIcmpv4Protocol.TryParse(invalidCode, out _),
              "invalid-icmp-code-rejected");

        byte[] oddPayload = new byte[264];
        Check(ManagedIcmpv4Protocol.TryBuildEchoRequest(
                  oddPayload, 7, 9, new byte[] { 1, 2, 3 },
                  out ushort oddLength) &&
              ManagedIcmpv4Protocol.TryParse(oddPayload.AsSpan(0, oddLength), out _),
              "odd-icmp-payload-checksum");
        oddPayload[oddLength - 1] ^= 1;
        Check(!ManagedIcmpv4Protocol.TryParse(oddPayload.AsSpan(0, oddLength), out _),
              "odd-payload-mutation-fails-checksum");
        Check(ManagedIcmpv4Protocol.TryBuildEchoRequest(
                  request, 1, 1, Array.Empty<byte>(), out ushort zeroLength) &&
              zeroLength == 8 &&
              ManagedIcmpv4Protocol.TryParse(request.AsSpan(0, zeroLength), out _),
              "zero-length-icmp-payload");
        byte[] maximum = new byte[264];
        Check(ManagedIcmpv4Protocol.TryBuildEchoRequest(
                  maximum, 1, 2, new byte[256], out ushort maximumLength) &&
              maximumLength == 264 &&
              ManagedIcmpv4Protocol.TryParse(maximum.AsSpan(0, maximumLength), out _),
              "bounded-maximum-icmp-payload");
        Check(!ManagedIcmpv4Protocol.TryBuildEchoRequest(
                  maximum, 1, 2, new byte[257], out _),
              "oversized-icmp-payload-rejected");
    }

    private static void ArpIntegrationTests()
    {
        ManagedArpCache cache = new();
        byte[] mac = new byte[6];
        Check(cache.TryLearn(s_peerIp, s_peerMac) &&
              cache.TryLookup(s_peerIp, mac) && mac.SequenceEqual(s_peerMac),
              "warm-arp-cache-lookup");
        byte[] packet = BuildIpv4(0x1701, 0, 64, 1,
                                  s_localIp, s_peerIp, new byte[] { 9, 8, 7 });
        byte[] frame = new byte[60];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  frame, mac, s_localMac, ManagedIpv4Protocol.EtherType,
                  packet, out ushort frameLength) && frameLength == 60 &&
              ManagedEthernetProtocol.TryParseFrame(
                  frame, s_peerMac, out ManagedEthernetFrame ethernet) &&
              ethernet.EtherType == ManagedIpv4Protocol.EtherType &&
              ethernet.Payload.Length >= packet.Length,
              "warm-arp-ipv4-ethernet-transmit");

        ManagedIpv4PendingTransmission pending = new(128);
        Check(pending.TryStage(s_peerIp, packet), "cold-arp-stage-pending-ipv4");
        Check(!pending.TryStage(s_peerIp, packet),
              "bounded-pending-overflow-rejected");
        byte[] pendingIp = new byte[4];
        byte[] pendingPacket = new byte[128];
        Check(pending.TryTake(pendingIp, pendingPacket, out ushort pendingLength) &&
              pendingLength == packet.Length && pendingIp.SequenceEqual(s_peerIp) &&
              pendingPacket.AsSpan(0, pendingLength).SequenceEqual(packet) &&
              !pending.IsActive,
              "arp-completion-releases-pending-ipv4");
        Check(pending.TryStage(s_peerIp, packet), "pending-reuse-after-completion");
        pending.Clear();
        Check(!pending.IsActive && pending.Length == 0,
              "teardown-clears-pending-ipv4");
        Check(ManagedIpv4Protocol.IsDirectlyReachable(
                  Ip(s_localIp), Ip(new byte[] { 255, 255, 255, 0 }),
                  Ip(s_peerIp)) &&
              !ManagedIpv4Protocol.IsDirectlyReachable(
                  Ip(s_localIp), Ip(new byte[] { 255, 255, 255, 0 }),
                  Ip(s_otherIp)),
              "same-subnet-policy-explicit");
    }

    private static byte[] BuildIpv4(ushort identification, ushort flagsOffset,
                                    byte ttl, byte protocol, byte[] source,
                                    byte[] destination, byte[] payload)
    {
        byte[] packet = new byte[ManagedIpv4Protocol.MaximumPacketLength];
        Check(ManagedIpv4Protocol.TryBuild(
                  packet, identification, flagsOffset, ttl, protocol,
                  source, destination, payload, out ushort length),
              "build-ipv4-helper");
        byte[] exact = new byte[length];
        Array.Copy(packet, exact, length);
        return exact;
    }

    private static uint Ip(byte[] address)
    {
        return ManagedEthernetProtocol.ReadUInt32Network(address, 0);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        s_cases++;
        Console.WriteLine("PASS: " + name);
    }
}

using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly byte[] s_guestMac =
        { 0x52, 0x54, 0x00, 0x12, 0x34, 0x56 };
    private static readonly byte[] s_hostMac =
        { 0x02, 0x15, 0x00, 0x00, 0x00, 0x02 };
    private static readonly byte[] s_guestIp = { 10, 15, 0, 1 };
    private static readonly byte[] s_hostIp = { 10, 15, 0, 2 };
    private static readonly byte[] s_broadcast =
        { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

    private static int s_cases;

    private static int Main()
    {
        EthernetTests();
        Ipv4IngressTests();
        Icmpv4Tests();
        ArpTests();
        ArpHandlerTests();
        CacheTests();
        Console.WriteLine($"MANAGED_KERNEL_PHASE16_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void Ipv4IngressTests()
    {
        uint local = ManagedEthernetProtocol.ReadUInt32Network(s_guestIp, 0);
        uint peer = ManagedEthernetProtocol.ReadUInt32Network(s_hostIp, 0);
        byte[] payload = { 0xA6, 0x86, 0x01 };
        byte[] packet = BuildIpv4(0x8601, 0, 63, 253,
                                  s_hostIp, s_guestIp, payload);
        byte[] frame = new byte[60];
        ManagedEthernetFrame ethernet = default;
        ManagedIpv4Packet parsed = default;
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  frame, s_guestMac, s_hostMac,
                  ManagedIpv4Protocol.EtherType, packet,
                  out ushort frameLength) && frameLength == 60 &&
              ManagedEthernetProtocol.TryParseFrame(
                  frame, s_guestMac, out ethernet) &&
              ethernet.EtherType == 0x0800 &&
              ManagedIpv4Protocol.TryParse(ethernet.Payload,
                  out parsed) &&
              parsed.Version == 4 && parsed.HeaderLength == 20 &&
              parsed.TotalLength == 23 && parsed.Identification == 0x8601 &&
              parsed.Ttl == 63 && parsed.Protocol == 253 &&
              ManagedEthernetProtocol.ReadUInt32Network(
                  parsed.SourceAddress, 0) == peer &&
              ManagedEthernetProtocol.ReadUInt32Network(
                  parsed.DestinationAddress, 0) == local &&
              parsed.Payload.SequenceEqual(payload),
              "ipv4-hardware-boundary-fields-and-padding-slice");
        Check(ethernet.Payload.Length == 46 && parsed.Payload.Length == 3 &&
              parsed.Payload[0] == 0xA6 && parsed.Payload[2] == 0x01,
              "ipv4-total-length-excludes-ethernet-padding");

        byte[] notForUs = BuildIpv4(0x8602, 0, 62, 253,
                                    s_hostIp, new byte[] { 10, 15, 1, 2 },
                                    payload);
        Check(ManagedIpv4Protocol.TryParse(notForUs,
                  out ManagedIpv4Packet other) &&
              ManagedEthernetProtocol.ReadUInt32Network(
                  other.DestinationAddress, 0) != local &&
              !ManagedIpv4Protocol.TryParse(notForUs, local, out _),
              "valid-not-for-us-parses-before-owner-filter");

        byte[] exactHeader = BuildIpv4(0x8603, 0, 64, 253,
                                       s_hostIp, s_guestIp, Array.Empty<byte>());
        Check(exactHeader.Length == 20 &&
              ManagedIpv4Protocol.TryParse(exactHeader, out ManagedIpv4Packet empty) &&
              empty.TotalLength == 20 && empty.Payload.IsEmpty,
              "ipv4-exact-header-zero-payload");

        byte[] malformed = (byte[])packet.Clone();
        malformed[0] = 0x65;
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-version-mismatch-rejected");
        malformed = (byte[])packet.Clone(); malformed[0] = 0x40;
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-zero-ihl-rejected");
        malformed = (byte[])packet.Clone(); malformed[0] = 0x44;
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-ihl-below-five-rejected");
        malformed = (byte[])packet.Clone(); malformed[0] = 0x46;
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-options-rejected-explicitly");
        malformed = (byte[])packet.Clone(); malformed[2] = 0; malformed[3] = 19;
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-total-length-below-header-rejected");
        malformed = (byte[])packet.Clone(); malformed[2] = 0; malformed[3] = 24;
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-truncated-total-length-rejected");
        Check(!ManagedIpv4Protocol.TryParse(packet.AsSpan(0, 19), out _),
              "ipv4-truncated-header-rejected");
        malformed = (byte[])packet.Clone(); malformed[10] ^= 1;
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-bad-network-order-checksum-rejected");
        malformed = (byte[])packet.Clone(); malformed[6] = 0x20;
        RecomputeIpv4Checksum(malformed);
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-more-fragments-rejected");
        malformed = (byte[])packet.Clone(); malformed[7] = 1;
        RecomputeIpv4Checksum(malformed);
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-fragment-offset-rejected");
        malformed = (byte[])packet.Clone(); malformed[8] = 0;
        RecomputeIpv4Checksum(malformed);
        Check(!ManagedIpv4Protocol.TryParse(malformed, out _),
              "ipv4-zero-ttl-not-delivered");
        Check(ManagedIpv4Protocol.TryParse(packet, out ManagedIpv4Packet unsupported) &&
              unsupported.Protocol == 253 && unsupported.Payload.Length == 3,
              "ipv4-unsupported-protocol-remains-valid");

        byte[] maximum = new byte[ManagedIpv4Protocol.MaximumPacketLength];
        Check(ManagedIpv4Protocol.TryBuild(
                  maximum, 0x86FF, 0x4000, 64, 253, s_hostIp, s_guestIp,
                  new byte[ManagedIpv4Protocol.MaximumPacketLength - 20],
                  out ushort maximumLength) &&
              maximumLength == ManagedIpv4Protocol.MaximumPacketLength &&
              ManagedIpv4Protocol.TryParse(
                  maximum.AsSpan(0, maximumLength), out ManagedIpv4Packet max) &&
              max.TotalLength == maximumLength,
              "ipv4-maximum-bounded-packet");
    }

    private static byte[] BuildIpv4(ushort identification, ushort flags,
                                    byte ttl, byte protocol, byte[] source,
                                    byte[] destination, byte[] payload)
    {
        byte[] packet = new byte[ManagedIpv4Protocol.MaximumPacketLength];
        if (!ManagedIpv4Protocol.TryBuild(packet, identification, flags, ttl,
                protocol, source, destination, payload, out ushort length))
            throw new InvalidOperationException("Could not build IPv4 host vector.");
        return packet.AsSpan(0, length).ToArray();
    }

    private static void Icmpv4Tests()
    {
        byte[] payload = System.Text.Encoding.ASCII.GetBytes("gX87-0001");
        byte[] request = new byte[ManagedIcmpv4Protocol.HeaderLength + payload.Length];
        Check(ManagedIcmpv4Protocol.TryBuildEchoRequest(request, 0x8701, 1,
                  payload, out ushort requestLength) &&
              requestLength == request.Length && request[0] == 8 && request[1] == 0 &&
              ManagedIcmpv4Protocol.ComputeChecksum(request) == 0,
              "icmpv4-valid-echo-request-and-network-order-checksum");
        Check(ManagedIcmpv4Protocol.TryParse(request,
                  out ManagedIcmpv4Packet parsedRequest) &&
              parsedRequest.Type == 8 && parsedRequest.Code == 0 &&
              parsedRequest.Identifier == 0x8701 && parsedRequest.Sequence == 1 &&
              parsedRequest.Payload.SequenceEqual(payload),
              "icmpv4-echo-request-fields-and-payload");

        byte[] reply = new byte[request.Length];
        Check(ManagedIcmpv4Protocol.TryBuildEchoReply(reply,
                  parsedRequest.Identifier, parsedRequest.Sequence,
                  parsedRequest.Payload, out ushort replyLength) &&
              replyLength == requestLength && reply[0] == 0 && reply[1] == 0 &&
              ManagedIcmpv4Protocol.TryParse(reply,
                  out ManagedIcmpv4Packet parsedReply) &&
              parsedReply.Identifier == parsedRequest.Identifier &&
              parsedReply.Sequence == parsedRequest.Sequence &&
              parsedReply.Payload.SequenceEqual(payload),
              "icmpv4-echo-reply-preserves-id-sequence-payload-and-checksum");

        byte[] oddPayloadRequest = new byte[9];
        Check(ManagedIcmpv4Protocol.TryBuildEchoRequest(
                  oddPayloadRequest, 0x1234, 0xABCD, new byte[] { 0xA5 },
                  out _) && ManagedIcmpv4Protocol.ComputeChecksum(
                  oddPayloadRequest) == 0,
              "icmpv4-checksum-odd-payload-zero-pad");

        byte[] malformed = (byte[])request.Clone();
        malformed[8] ^= 1;
        Check(!ManagedIcmpv4Protocol.TryParse(malformed, out _),
              "icmpv4-bad-checksum-rejected");
        malformed = (byte[])request.Clone();
        malformed[1] = 1;
        RecomputeIcmpChecksum(malformed);
        Check(ManagedIcmpv4Protocol.TryParse(malformed,
                  out ManagedIcmpv4Packet wrongCode) && wrongCode.Code == 1 &&
              ManagedIcmpv4Protocol.Classify(in wrongCode) ==
                  ManagedIcmpv4Disposition.Unsupported,
              "icmpv4-valid-wrong-code-is-unsupported-without-echo-reply");
        Check(!ManagedIcmpv4Protocol.TryParse(request.AsSpan(0, 7), out _),
              "icmpv4-truncated-echo-header-rejected");
        malformed = (byte[])request.Clone();
        malformed[0] = 3;
        RecomputeIcmpChecksum(malformed);
        Check(ManagedIcmpv4Protocol.TryParse(malformed,
                  out ManagedIcmpv4Packet unsupported) && unsupported.Type == 3 &&
              ManagedIcmpv4Protocol.Classify(in unsupported) ==
                  ManagedIcmpv4Disposition.Unsupported,
              "icmpv4-valid-unsupported-type-classified-after-parse");
        Check(ManagedIcmpv4Protocol.TryParse(reply,
                  out ManagedIcmpv4Packet incomingReply) &&
              incomingReply.Type == ManagedIcmpv4Protocol.EchoReply &&
              ManagedIcmpv4Protocol.Classify(in incomingReply) ==
                  ManagedIcmpv4Disposition.EchoReply,
              "icmpv4-incoming-echo-reply-does-not-request-reply");

        byte[] paddedFrame = new byte[60];
        Check(ManagedEthernetProtocol.TryBuildFrame(paddedFrame,
                  s_guestMac, s_hostMac, ManagedIpv4Protocol.EtherType,
                  BuildIpv4(0x8701, 0, 63, ManagedIpv4Protocol.IcmpProtocol,
                      s_hostIp, s_guestIp, request), out _) &&
              ManagedEthernetProtocol.TryParseFrame(paddedFrame, s_guestMac,
                  out ManagedEthernetFrame ethernet) &&
              ManagedIpv4Protocol.TryParse(ethernet.Payload,
                  out ManagedIpv4Packet ip) && ip.Payload.Length == request.Length &&
              ManagedIcmpv4Protocol.TryParse(ip.Payload, out _) &&
              ethernet.Payload.Length > ip.TotalLength,
              "icmpv4-ip-total-length-excludes-ethernet-padding");

        byte[] otherHost = BuildIpv4(0x8702, 0, 63,
            ManagedIpv4Protocol.IcmpProtocol, s_hostIp,
            new byte[] { 10, 15, 0, 99 }, request);
        Check(ManagedIpv4Protocol.TryParse(otherHost,
                  out ManagedIpv4Packet notForUs) &&
              ManagedEthernetProtocol.ReadUInt32Network(
                  notForUs.DestinationAddress, 0) !=
              ManagedEthernetProtocol.ReadUInt32Network(s_guestIp, 0),
              "icmpv4-not-for-us-ip-remains-filtered-before-handler");
    }

    private static void RecomputeIcmpChecksum(byte[] packet)
    {
        packet[2] = 0;
        packet[3] = 0;
        ManagedEthernetProtocol.WriteUInt16Network(packet, 2,
            ManagedIcmpv4Protocol.ComputeChecksum(packet));
    }

    private static void RecomputeIpv4Checksum(byte[] packet)
    {
        packet[10] = 0;
        packet[11] = 0;
        ManagedEthernetProtocol.WriteUInt16Network(packet, 10,
            ManagedIpv4Protocol.ComputeChecksum(packet.AsSpan(0, 20)));
    }

    private static void EthernetTests()
    {
        byte[] request = BuildRequest(s_broadcast, s_guestMac, s_guestIp, s_hostIp);
        Check(ManagedEthernetProtocol.IsBroadcast(request.AsSpan(0, 6)),
              "valid-request-broadcast-before-parse");
        Check(!ManagedEthernetProtocol.IsInvalidMac(s_guestMac) &&
              ManagedEthernetProtocol.IsUsableSourceMac(request.AsSpan(6, 6)),
              "valid-request-source-before-parse");
        Check(ManagedEthernetProtocol.TryParseFrame(
                  request, s_guestMac, out ManagedEthernetFrame parsed) &&
              parsed.EtherType == ManagedEthernetProtocol.ArpEtherType &&
              parsed.Payload.Length >= ManagedArpProtocol.PayloadLength,
              "valid-ethernet-arp-frame");
        Check(!ManagedEthernetProtocol.TryParseFrame(
                  new byte[13], s_guestMac, out _), "truncated-ethernet-rejected");

        byte[] headerOnly = BuildHeaderOnlyFrame(
            s_guestMac, s_hostMac, ManagedEthernetPhase84TestConsumer.EtherType);
        ManagedEthernetFrame headerView = default;
        Check(headerOnly.Length == ManagedEthernetProtocol.HeaderLength &&
              ManagedEthernetProtocol.TryParseFrame(
                  headerOnly, s_guestMac, out headerView) &&
              headerView.Payload.Length == 0 &&
              headerView.EtherType == 0x88B5,
              "exact-header-network-order-ethertype-zero-payload");
        Check(!ManagedEthernetPhase84TestConsumer.TryConsume(
                  in headerView, 1, out _),
              "header-only-frame-safe-consumer-rejection");
        Check(ManagedEthernetProtocol.ClassifyEtherType(
                  ManagedEthernetPhase84TestConsumer.EtherType, false) ==
                  ManagedEthernetDispatchKind.Unsupported &&
              ManagedEthernetProtocol.ClassifyEtherType(
                  ManagedEthernetPhase84TestConsumer.EtherType, true) ==
                  ManagedEthernetDispatchKind.Phase84TestConsumer &&
              ManagedEthernetProtocol.ClassifyEtherType(
                  ManagedEthernetProtocol.Ipv4EtherType, false) ==
                  ManagedEthernetDispatchKind.Ipv4,
              "bounded-ethertype-registration-and-unsupported-result");

        byte[] unknown = new byte[60];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  unknown, s_guestMac, s_hostMac,
                  ManagedEthernetPhase84TestConsumer.EtherType, new byte[1],
                  out ushort unknownLength) &&
              unknownLength == 60 &&
              ManagedEthernetProtocol.TryParseFrame(unknown, s_guestMac,
                                                     out ManagedEthernetFrame unknownView) &&
              unknown[12] == 0x88 && unknown[13] == 0xB5 &&
              unknownView.EtherType == 0x88B5,
              "network-order-ethertype-decoding");

        byte[] testFrame = BuildPhase84TestFrame(s_guestMac, s_hostMac, 1);
        ManagedEthernetFrame testView = default;
        ManagedEthernetTestFrameMetadata firstMetadata = default;
        Check(ManagedEthernetProtocol.TryParseFrame(
                  testFrame, s_guestMac, out testView) &&
              ManagedEthernetPhase84TestConsumer.TryConsume(
                  in testView, 1, out firstMetadata) &&
              firstMetadata.DestinationMac == 0x525400123456UL &&
              firstMetadata.SourceMac == 0x021500000002UL &&
              firstMetadata.EtherType == 0x88B5 &&
              firstMetadata.PayloadLength == 46 &&
              firstMetadata.Sentinel == 1,
              "phase84-test-consumer-records-bounded-metadata");
        testFrame[0] ^= 1;
        Check(firstMetadata.DestinationMac == 0x525400123456UL &&
              firstMetadata.SourceMac == 0x021500000002UL,
              "phase84-consumer-metadata-does-not-retain-frame-view");
        testFrame[0] ^= 1;
        Check(!ManagedEthernetPhase84TestConsumer.TryConsume(
                  in testView, 2, out _),
              "phase84-stale-sentinel-consumer-failure");
        byte[] nextFrame = BuildPhase84TestFrame(s_guestMac, s_hostMac, 2);
        Check(ManagedEthernetProtocol.TryParseFrame(
                  nextFrame, s_guestMac, out ManagedEthernetFrame nextView) &&
              ManagedEthernetPhase84TestConsumer.TryConsume(
                  in nextView, 2,
                  out ManagedEthernetTestFrameMetadata nextMetadata) &&
              nextMetadata.Sentinel == 2 && firstMetadata.Sentinel == 1,
              "phase84-cross-frame-sentinel-does-not-leak");
        testFrame[14] ^= 1;
        Check(ManagedEthernetProtocol.TryParseFrame(
                  testFrame, s_guestMac, out ManagedEthernetFrame rejectedView) &&
              !ManagedEthernetPhase84TestConsumer.TryConsume(
                  in rejectedView, 1, out _),
              "phase84-consumer-rejects-corrupt-payload");

        byte[] local = BuildRequest(s_guestMac, s_hostMac, s_hostIp, s_guestIp);
        Check(ManagedEthernetProtocol.TryParseFrame(
                  local, s_guestMac, out _), "local-unicast-destination-accepted");
        Check(ManagedEthernetProtocol.TryParseFrame(
                  request, s_guestMac, out _), "broadcast-destination-accepted");
        byte[] unrelated = BuildRequest(s_hostMac, s_hostMac, s_hostIp, s_guestIp);
        Check(!ManagedEthernetProtocol.TryParseFrame(
                  unrelated, s_guestMac, out _), "unrelated-destination-rejected");
        byte[] oversizedReceive = new byte[
            ManagedEthernetProtocol.MaximumFrameLength + 1];
        s_guestMac.CopyTo(oversizedReceive, 0);
        s_hostMac.CopyTo(oversizedReceive, 6);
        oversizedReceive[12] = 0x88;
        oversizedReceive[13] = 0xB5;
        Check(!ManagedEthernetProtocol.TryParseFrame(
                  oversizedReceive, s_guestMac, out _),
              "oversized-received-frame-rejected-before-payload-access");
        byte[] unsupported = BuildHeaderOnlyFrame(
            s_guestMac, s_hostMac, 0x1234);
        Check(ManagedEthernetProtocol.TryParseFrame(
                  unsupported, s_guestMac,
                  out ManagedEthernetFrame unsupportedView) &&
              ManagedEthernetProtocol.ClassifyEtherType(
                  unsupportedView.EtherType, false) ==
                  ManagedEthernetDispatchKind.Unsupported,
              "valid-unsupported-ethertype-classified-without-dispatch");
        Check(request[12] == 0x08 && request[13] == 0x06 &&
              request[20] == 0x00 && request[21] == 0x01,
              "network-byte-order-fields");
        Check(request.Length == ManagedEthernetProtocol.MinimumFrameLength &&
              request[59] == 0, "minimum-frame-padding");

        byte[] maximum = new byte[ManagedEthernetProtocol.MaximumFrameLength];
        byte[] maximumPayload = new byte[
            ManagedEthernetProtocol.MaximumFrameLength - ManagedEthernetProtocol.HeaderLength];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  maximum, s_hostMac, s_guestMac, ManagedEthernetProtocol.ArpEtherType,
                  maximumPayload, out ushort maximumLength) &&
              maximumLength == maximum.Length &&
              ManagedEthernetProtocol.TryParseFrame(
                  maximum, s_hostMac, out ManagedEthernetFrame maximumView) &&
              maximumView.Payload.Length == maximum.Length -
                  ManagedEthernetProtocol.HeaderLength,
              "maximum-supported-frame-size");
        Check(!ManagedEthernetProtocol.TryBuildFrame(
                  maximum, s_hostMac, s_guestMac, ManagedEthernetProtocol.ArpEtherType,
                  new byte[maximumPayload.Length + 1], out _),
              "oversized-frame-rejected");
        byte[] guarded = new byte[ManagedEthernetProtocol.MaximumFrameLength + 2];
        guarded[guarded.Length - 2] = 0xA5;
        guarded[guarded.Length - 1] = 0x5A;
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  guarded.AsSpan(0, ManagedEthernetProtocol.MaximumFrameLength),
                  s_hostMac, s_guestMac, ManagedEthernetProtocol.ArpEtherType,
                  maximumPayload, out _),
              "bounded-frame-construction");
        Check(guarded[guarded.Length - 2] == 0xA5 &&
              guarded[guarded.Length - 1] == 0x5A, "no-buffer-overrun");
    }

    private static void ArpTests()
    {
        byte[] request = BuildRequest(s_broadcast, s_hostMac, s_hostIp, s_guestIp);
        Check(ManagedArpProtocol.TryParse(
                  request.AsSpan(14), out ManagedArpPacket requestPacket) &&
              requestPacket.Operation == ManagedArpProtocol.OperationRequest &&
              requestPacket.SenderIpv4.SequenceEqual(s_hostIp),
              "canonical-arp-request");
        Check(ManagedArpProtocol.IsRequestForLocal(
                  requestPacket, request.AsSpan(6, 6), request.AsSpan(0, 6),
                  s_guestIp), "request-for-local-address-recognized");

        byte[] reply = BuildReply(s_guestMac, s_hostMac, s_hostIp, s_guestIp);
        Check(ManagedArpProtocol.TryParse(
                  reply.AsSpan(14), out ManagedArpPacket replyPacket) &&
              requestPacket.Operation == ManagedArpProtocol.OperationRequest &&
              replyPacket.Operation == ManagedArpProtocol.OperationReply &&
              ManagedArpProtocol.IsPendingReplyMatch(
                  replyPacket, reply.AsSpan(6, 6), reply.AsSpan(0, 6),
                  s_guestMac, s_guestIp,
                  ManagedEthernetProtocol.ReadUInt32Network(s_hostIp, 0)),
              "canonical-arp-reply");

        Check(!ManagedArpProtocol.TryParse(
                  request.AsSpan(14, ManagedArpProtocol.PayloadLength - 1), out _),
              "truncated-arp-rejected");
        Check(!TryMutatedArp(request, 0, 2), "wrong-htype-rejected");
        Check(!TryMutatedArp(request, 2, 0x86DD), "wrong-ptype-rejected");
        Check(!TryMutatedArp(request, 4, 5), "wrong-hlen-rejected");
        Check(!TryMutatedArp(request, 5, 6), "wrong-plen-rejected");
        Check(!TryMutatedArp(request, 6, 3), "unsupported-opcode-rejected");

        byte[] mismatchedSource = (byte[])request.Clone();
        mismatchedSource[6] ^= 1;
        Check(ManagedArpProtocol.TryParse(
                  mismatchedSource.AsSpan(14), out ManagedArpPacket mismatchPacket) &&
              !mismatchedSource.AsSpan(6, 6).SequenceEqual(mismatchPacket.SenderMac),
              "ethernet-source-sender-mac-mismatch-rejected");
        Check(!ManagedArpProtocol.IsPendingReplyMatch(
                  replyPacket, reply.AsSpan(6, 6), reply.AsSpan(0, 6),
                  s_guestMac, s_guestIp,
                  ManagedEthernetProtocol.ReadUInt32Network(new byte[] { 10, 15, 0, 9 }, 0)),
              "unrelated-reply-does-not-satisfy-pending");
        Check(!ManagedArpProtocol.IsPendingReplyMatch(
                  replyPacket, new byte[] { 2, 15, 0, 0, 0, 3 },
                  reply.AsSpan(0, 6), s_guestMac,
                  s_guestIp, ManagedEthernetProtocol.ReadUInt32Network(s_hostIp, 0)),
              "incorrect-sender-mac-does-not-satisfy");
        Check(!ManagedArpProtocol.IsPendingReplyMatch(
                  replyPacket, reply.AsSpan(6, 6), reply.AsSpan(0, 6),
                  s_guestMac, s_guestIp,
                  ManagedEthernetProtocol.ReadUInt32Network(
                      new byte[] { 10, 15, 0, 3 }, 0)),
              "wrong-sender-ip-does-not-satisfy");

        byte[] unrelatedRequest = BuildRequest(
            s_broadcast, s_hostMac, s_hostIp, new byte[] { 10, 15, 0, 9 });
        Check(ManagedArpProtocol.TryParse(
                  unrelatedRequest.AsSpan(14), out ManagedArpPacket unrelatedPacket) &&
              !ManagedArpProtocol.IsRequestForLocal(
                  unrelatedPacket, unrelatedRequest.AsSpan(6, 6),
                  unrelatedRequest.AsSpan(0, 6), s_guestIp),
              "request-for-unrelated-ip-rejected");
    }

    private static void ArpHandlerTests()
    {
        ManagedE1000Driver.Phase16MacHigh = 0x5254;
        ManagedE1000Driver.Phase16MacLow = 0x00123456;
        FakeArpEthernet ethernet = new();
        ManagedArpLayer arp = new(ethernet);
        arp.InitializeMac();
        Check(arp.TryInstallLocalIpv4(s_guestIp),
              "arp-local-address-installed-through-existing-boundary");

        byte[] localRequest = BuildRequest(
            s_broadcast, s_hostMac, s_hostIp, s_guestIp);
        Check(ManagedEthernetProtocol.TryParseFrame(
                  localRequest, s_guestMac,
                  out ManagedEthernetFrame localFrame),
              "arp-handler-receives-normal-ethernet-parse");
        ManagedArpHandleResult localResult = arp.TryHandleEthernetArp(
            in localFrame, out ManagedArpReceiveMetadata localMetadata);
        Check(localResult == ManagedArpHandleResult.ResponderReplySent &&
              ethernet.TransmitCount == 1,
              "valid-request-for-local-address-reaches-existing-handler");
        Check(arp.Cache.Count == 0,
              "accepted-arp-request-does-not-invent-cache-learning");
        Check(localMetadata.EthernetDestination == 0xFFFFFFFFFFFFUL &&
              localMetadata.EthernetSource == 0x021500000002UL &&
              localMetadata.EtherType == 0x0806 &&
              localMetadata.PayloadLength == 46 &&
              localMetadata.HardwareType == 1 &&
              localMetadata.ProtocolType == 0x0800 &&
              localMetadata.HardwareLength == 6 &&
              localMetadata.ProtocolLength == 4 &&
              localMetadata.Operation == 1 &&
              localMetadata.SenderMac == 0x021500000002UL &&
              localMetadata.SenderIpv4 == 0x0A0F0002 &&
              localMetadata.TargetMac == 0 &&
              localMetadata.TargetIpv4 == 0x0A0F0001,
              "arp-handler-copies-authoritative-sender-target-metadata");
        Check(ethernet.LastEtherType == ManagedEthernetProtocol.ArpEtherType &&
              ethernet.LastDestination.AsSpan().SequenceEqual(s_hostMac) &&
              ManagedArpProtocol.TryParse(
                  ethernet.LastPayload, out ManagedArpPacket reply) &&
              reply.Operation == ManagedArpProtocol.OperationReply &&
              reply.SenderMac.SequenceEqual(s_guestMac) &&
              reply.SenderIpv4.SequenceEqual(s_guestIp) &&
              reply.TargetMac.SequenceEqual(s_hostMac) &&
              reply.TargetIpv4.SequenceEqual(s_hostIp),
              "existing-arp-reply-uses-normal-ethernet-transmit-boundary");

        byte[] notForUs = BuildRequest(
            s_broadcast, s_hostMac, s_hostIp, new byte[] { 10, 15, 0, 99 });
        Check(ManagedEthernetProtocol.TryParseFrame(
                  notForUs, s_guestMac,
                  out ManagedEthernetFrame unrelatedFrame),
              "valid-not-for-us-request-passes-ethernet");
        ManagedArpHandleResult unrelatedResult = arp.TryHandleEthernetArp(
            in unrelatedFrame, out ManagedArpReceiveMetadata unrelatedMetadata);
        Check(unrelatedResult == ManagedArpHandleResult.RequestNotForUs &&
              unrelatedMetadata.TargetIpv4 == 0x0A0F0063 &&
              ethernet.TransmitCount == 1 && arp.Cache.Count == 0,
              "valid-not-for-us-request-is-classified-without-reply");

        FakeArpEthernet negativeFirstEthernet = new();
        ManagedArpLayer negativeFirstArp = new(negativeFirstEthernet);
        negativeFirstArp.InitializeMac();
        Check(negativeFirstArp.TryInstallLocalIpv4(s_guestIp),
              "arp-negative-first-local-address-installed");
        ManagedArpHandleResult negativeFirstResult =
            negativeFirstArp.TryHandleEthernetArp(in unrelatedFrame, out _);
        Check(negativeFirstResult == ManagedArpHandleResult.RequestNotForUs &&
              negativeFirstEthernet.TransmitCount == 0 &&
              negativeFirstArp.Cache.Count == 0,
              "negative-first-request-is-parsed-with-zero-replies");

        ManagedArpHandleResult negativeFirstLiveness =
            negativeFirstArp.TryHandleEthernetArp(in localFrame, out _);
        Check(negativeFirstLiveness == ManagedArpHandleResult.ResponderReplySent &&
              negativeFirstEthernet.TransmitCount == 1,
              "local-request-after-negative-first-replies");

        FakeArpEthernet positiveFirstEthernet = new();
        ManagedArpLayer positiveFirstArp = new(positiveFirstEthernet);
        positiveFirstArp.InitializeMac();
        Check(positiveFirstArp.TryInstallLocalIpv4(s_guestIp),
              "arp-positive-first-local-address-installed");
        ManagedArpHandleResult positiveFirstLocal =
            positiveFirstArp.TryHandleEthernetArp(in localFrame, out _);
        int positiveReplyCount = positiveFirstEthernet.TransmitCount;
        ManagedArpHandleResult positiveFirstNegative =
            positiveFirstArp.TryHandleEthernetArp(
                in unrelatedFrame, out ManagedArpReceiveMetadata positiveFirstMetadata);
        Check(positiveFirstLocal == ManagedArpHandleResult.ResponderReplySent &&
              positiveReplyCount == 1 &&
              positiveFirstNegative == ManagedArpHandleResult.RequestNotForUs &&
              positiveFirstEthernet.TransmitCount == positiveReplyCount &&
              positiveFirstMetadata.TargetIpv4 == 0x0A0F0063,
              "positive-then-not-for-us-adds-zero-replies");
        ManagedArpHandleResult positiveFirstLiveness =
            positiveFirstArp.TryHandleEthernetArp(in localFrame, out _);
        Check(positiveFirstLiveness == ManagedArpHandleResult.ResponderReplySent &&
              positiveFirstEthernet.TransmitCount == 2,
              "positive-after-not-for-us-liveness-replies");

        foreach ((int offset, int value, string name) in new[]
        {
            (0, 2, "unsupported-hardware-type"),
            (2, 0x86DD, "unsupported-protocol-type"),
            (4, 5, "wrong-hlen"),
            (5, 6, "wrong-plen"),
            (6, 3, "unsupported-opcode")
        })
        {
            byte[] malformed = MutateArp(localRequest, offset, value);
            Check(ManagedEthernetProtocol.TryParseFrame(
                      malformed, s_guestMac,
                      out ManagedEthernetFrame malformedFrame),
                  "malformed-" + name + "-passes-ethernet");
            Check(arp.TryHandleEthernetArp(
                      in malformedFrame, out _) == ManagedArpHandleResult.Invalid &&
                  ethernet.TransmitCount == 1,
                  "arp-handler-rejects-" + name + "-without-reply");
        }

        byte[] shortPayload = new byte[ManagedArpProtocol.PayloadLength - 1];
        Check(!ManagedArpProtocol.TryParse(shortPayload, out _),
              "arp-handler-supported-payload-minimum-is-28-bytes");
        byte[] wrongEtherType = new byte[ManagedEthernetProtocol.MinimumFrameLength];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  wrongEtherType, s_broadcast, s_hostMac, 0x0800,
                  localRequest.AsSpan(14, ManagedArpProtocol.PayloadLength),
                  out _) &&
              ManagedEthernetProtocol.TryParseFrame(
                  wrongEtherType, s_guestMac,
                  out ManagedEthernetFrame wrongTypeFrame) &&
              arp.TryHandleEthernetArp(
                  in wrongTypeFrame, out _) == ManagedArpHandleResult.Invalid &&
              ethernet.TransmitCount == 1,
              "arp-handler-requires-ethernet-arp-classification");
    }

    private static void CacheTests()
    {
        ManagedArpCache cache = new();
        Check(cache.Count == 0 && cache.Capacity == 8, "cache-starts-empty");
        Check(cache.TryLearn(s_hostIp, s_hostMac, out bool newEntry) && !newEntry,
              "valid-cache-learn");
        byte[] lookup = new byte[6];
        Check(cache.TryLookup(s_hostIp, lookup) && lookup.SequenceEqual(s_hostMac),
              "cache-lookup-hit");
        Check(!cache.TryLookup(new byte[] { 10, 15, 0, 99 }, lookup),
              "cache-lookup-miss");
        byte[] updatedMac = { 0x02, 0x15, 0, 0, 0, 3 };
        Check(cache.TryLearn(s_hostIp, updatedMac, out bool updated) && updated &&
              cache.TryLookup(s_hostIp, lookup) && lookup.SequenceEqual(updatedMac),
              "cache-update-existing");

        ManagedArpCache full = new();
        for (int index = 0; index != full.Capacity; ++index)
        {
            byte[] address = { 10, 15, 1, (byte)(index + 1) };
            byte[] mac = { 2, 15, 1, 0, 0, (byte)(index + 1) };
            Check(full.TryLearn(address, mac), "cache-fill-entry-" + index);
        }
        Check(full.Count == full.Capacity, "cache-does-not-grow");
        Check(full.TryLearn(new byte[] { 10, 15, 1, 99 }, s_hostMac) &&
              !full.TryLookup(new byte[] { 10, 15, 1, 1 }, lookup) &&
              full.Count == full.Capacity,
              "deterministic-oldest-replacement");

        ManagedArpCache malformed = new();
        byte[] bad = BuildRequest(s_broadcast, s_hostMac, s_hostIp, s_guestIp);
        bad[15] = 0;
        Check(!ManagedArpProtocol.TryParse(
                  bad.AsSpan(14), out _) && malformed.Count == 0,
              "malformed-arp-does-not-learn");
    }

    private static byte[] BuildRequest(byte[] destination, byte[] source,
                                       byte[] senderIp, byte[] targetIp)
    {
        byte[] payload = new byte[ManagedArpProtocol.PayloadLength];
        Check(ManagedArpProtocol.TryBuildRequest(
                  payload, source, senderIp, targetIp), "build-request");
        byte[] frame = new byte[ManagedEthernetProtocol.MinimumFrameLength];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  frame, destination, source, ManagedEthernetProtocol.ArpEtherType,
                  payload, out _), "build-request-ethernet");
        return frame;
    }

    private static byte[] BuildHeaderOnlyFrame(byte[] destination, byte[] source,
                                                ushort etherType)
    {
        byte[] frame = new byte[ManagedEthernetProtocol.HeaderLength];
        destination.CopyTo(frame, 0);
        source.CopyTo(frame, 6);
        frame[12] = (byte)(etherType >> 8);
        frame[13] = (byte)etherType;
        return frame;
    }

    private static byte[] BuildPhase84TestFrame(byte[] destination, byte[] source,
                                                 uint sequence)
    {
        ReadOnlySpan<byte> signature =
            ManagedE1000Protocol.Phase83RxPayloadSignature;
        byte[] payload = new byte[signature.Length + 4];
        signature.CopyTo(payload);
        int offset = signature.Length;
        payload[offset] = (byte)(sequence >> 24);
        payload[offset + 1] = (byte)(sequence >> 16);
        payload[offset + 2] = (byte)(sequence >> 8);
        payload[offset + 3] = (byte)sequence;
        byte[] frame = new byte[ManagedEthernetProtocol.MinimumFrameLength];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  frame, destination, source,
                  ManagedEthernetPhase84TestConsumer.EtherType,
                  payload, out _), "build-phase84-test-frame");
        return frame;
    }

    private static byte[] BuildReply(byte[] destination, byte[] source,
                                     byte[] senderIp, byte[] targetIp)
    {
        byte[] payload = new byte[ManagedArpProtocol.PayloadLength];
        Check(ManagedArpProtocol.TryBuildReply(
                  payload, source, senderIp, destination, targetIp), "build-reply");
        byte[] frame = new byte[ManagedEthernetProtocol.MinimumFrameLength];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  frame, destination, source, ManagedEthernetProtocol.ArpEtherType,
                  payload, out _), "build-reply-ethernet");
        return frame;
    }

    private static bool TryMutatedArp(byte[] source, int offset, int value)
    {
        byte[] mutated = MutateArp(source, offset, value);
        return ManagedArpProtocol.TryParse(mutated.AsSpan(14), out _);
    }

    private static byte[] MutateArp(byte[] source, int offset, int value)
    {
        byte[] mutated = (byte[])source.Clone();
        if (offset == 0 || offset == 2 || offset == 6)
        {
            mutated[14 + offset] = (byte)(value >> 8);
            mutated[15 + offset] = (byte)value;
        }
        else
        {
            mutated[14 + offset] = (byte)value;
        }
        return mutated;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        s_cases++;
        Console.WriteLine("PASS: " + name);
    }
}

internal sealed class FakeArpEthernet : IManagedArpEthernet
{
    internal int TransmitCount { get; private set; }
    internal ushort LastEtherType { get; private set; }
    internal byte[] LastDestination { get; private set; } = Array.Empty<byte>();
    internal byte[] LastPayload { get; private set; } = Array.Empty<byte>();

    public bool TryTransmit(ushort etherType, byte[] destination,
                            byte[] payload, int payloadLength)
    {
        if (payloadLength < 0 || payloadLength > payload.Length) return false;
        TransmitCount++;
        LastEtherType = etherType;
        LastDestination = destination.AsSpan().ToArray();
        LastPayload = payload.AsSpan(0, payloadLength).ToArray();
        return true;
    }

    public bool TryTransmitBroadcast(ushort etherType, byte[] payload,
                                     int payloadLength) => false;

    public bool TryReceiveAndDispatch(ManagedArpLayer arp,
                                     out ManagedArpHandleResult result)
    {
        result = ManagedArpHandleResult.Invalid;
        return false;
    }

    public bool TryVerifyTransportAfterGc() => true;
}

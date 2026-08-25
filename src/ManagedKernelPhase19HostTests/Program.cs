using System;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly byte[] s_mac = { 0x52, 0x54, 0, 0x12, 0x34, 0x56 };
    private static readonly byte[] s_serverIp = { 10, 15, 0, 2 };
    private static readonly byte[] s_leaseIp = { 10, 15, 0, 42 };
    private static readonly byte[] s_mask = { 255, 255, 255, 0 };
    private static int s_cases;

    private static int Main()
    {
        ParserTests();
        BuilderTests();
        ClientValidationTests();
        RetryAndTeardownTests();
        Console.WriteLine($"MANAGED_KERNEL_PHASE19_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void ParserTests()
    {
        byte[] discover = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
        Check(ManagedDhcpv4Protocol.TryBuildDiscover(discover, 0x19000001,
                  s_mac, out ushort length) && length == 250,
              "discover-fixed-length");
        Check(ManagedDhcpv4Protocol.TryParse(discover.AsSpan(0, length),
                  out ManagedDhcpv4Packet parsed) &&
              parsed.Op == ManagedDhcpv4Protocol.BootRequest &&
              parsed.HardwareType == ManagedDhcpv4Protocol.HardwareTypeEthernet &&
              parsed.HardwareLength == 6 && parsed.TransactionId == 0x19000001 &&
              parsed.Flags == ManagedDhcpv4Protocol.BroadcastFlag &&
              parsed.ClientHardwareAddress.Slice(0, 6).SequenceEqual(s_mac) &&
              parsed.Options.HasMessageType &&
              parsed.Options.MessageType == ManagedDhcpv4MessageType.Discover,
              "discover-fixed-fields");

        byte[] prl = new byte[16];
        Check(ManagedDhcpv4Protocol.TryParseOptions(
                  discover.AsSpan(240, length - 240), prl,
                  out ManagedDhcpv4OptionValues discoverOptions) &&
              discoverOptions.ParameterRequestListLength == 4 &&
              prl.AsSpan(0, 4).SequenceEqual(new byte[] { 1, 3, 6, 51 }),
              "discover-parameter-request-list");

        byte[] badCookie = (byte[])discover.Clone();
        badCookie[239] ^= 1;
        Check(!ManagedDhcpv4Protocol.TryParse(badCookie.AsSpan(0, length), out _),
              "bad-cookie-rejected");
        Check(!ManagedDhcpv4Protocol.TryParse(discover.AsSpan(0, 239), out _),
              "truncated-cookie-rejected");

        byte[] missingEnd = (byte[])discover.Clone();
        missingEnd[length - 1] = ManagedDhcpv4Protocol.OptionPad;
        Check(!ManagedDhcpv4Protocol.TryParse(missingEnd.AsSpan(0, length), out _),
              "missing-end-rejected");

        byte[] padded = MakeOptions(new byte[] { 0, 0, 99, 2, 0xAA, 0xBB, 255 });
        Check(ManagedDhcpv4Protocol.TryParseOptions(padded, prl, out _),
              "pad-and-unknown-option-skipped");
        byte[] truncatedOption = MakeOptions(new byte[] { 53, 2, 2, 255 });
        Check(!ManagedDhcpv4Protocol.TryParseOptions(truncatedOption, prl, out _),
              "truncated-option-rejected");
        byte[] oversizedOption = MakeOptions(new byte[] { 53, 8, 2, 2, 2, 2, 2, 2, 2, 255 });
        Check(!ManagedDhcpv4Protocol.TryParseOptions(oversizedOption, prl, out _),
              "fixed-width-option-rejected");

        byte[] conflict = MakeOptions(new byte[]
        {
            53, 1, 2, 53, 1, 5, 255
        });
        Check(!ManagedDhcpv4Protocol.TryParseOptions(conflict, prl, out _),
              "conflicting-duplicate-rejected");
        byte[] duplicateSame = MakeOptions(new byte[]
        {
            1, 4, 255, 255, 255, 0, 1, 4, 255, 255, 255, 0, 255
        });
        Check(ManagedDhcpv4Protocol.TryParseOptions(duplicateSame, prl, out _),
              "identical-duplicate-accepted-deterministically");

        byte[] malformedSubnet = MakeOptions(new byte[] { 1, 3, 255, 255, 255, 255 });
        Check(!ManagedDhcpv4Protocol.TryParseOptions(malformedSubnet, prl, out _),
              "subnet-width-rejected");
        byte[] malformedLease = MakeOptions(new byte[] { 51, 3, 0, 0, 1, 255 });
        Check(!ManagedDhcpv4Protocol.TryParseOptions(malformedLease, prl, out _),
              "lease-width-rejected");

        byte[] options = MakeOptions(new byte[]
        {
            1, 4, 255, 255, 255, 0,
            3, 4, 10, 15, 0, 2,
            6, 8, 10, 15, 0, 2, 1, 1, 1, 1,
            51, 4, 0, 0, 0x0E, 0x10,
            53, 1, 5,
            54, 4, 10, 15, 0, 2,
            50, 4, 10, 15, 0, 42,
            55, 4, 1, 3, 6, 51,
            255
        });
        Check(ManagedDhcpv4Protocol.TryParseOptions(options, prl,
                  out ManagedDhcpv4OptionValues values) &&
              values.HasSubnetMask && values.SubnetMask == 0xFFFFFF00 &&
              values.HasRouter && values.Router == 0x0A0F0002 &&
              values.DnsCount == 2 && values.DnsServer1 == 0x0A0F0002 &&
              values.DnsServer2 == 0x01010101 && values.HasLeaseTime &&
              values.LeaseTime == 3600 && values.HasMessageType &&
              values.MessageType == ManagedDhcpv4MessageType.Ack &&
              values.HasServerIdentifier && values.ServerIdentifier == 0x0A0F0002 &&
              values.HasRequestedIp && values.RequestedIp == 0x0A0F002A,
              "all-supported-options-parsed");
    }

    private static void BuilderTests()
    {
        byte[] request = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
        Check(ManagedDhcpv4Protocol.TryBuildRequest(request, 0x19000002, s_mac,
                  0x0A0F002A, 0x0A0F0002, out ushort length),
              "request-build");
        Check(ManagedDhcpv4Protocol.TryParse(request.AsSpan(0, length),
                  out ManagedDhcpv4Packet parsed) &&
              parsed.Op == ManagedDhcpv4Protocol.BootRequest &&
              parsed.TransactionId == 0x19000002 &&
              parsed.Options.MessageType == ManagedDhcpv4MessageType.Request &&
              parsed.Options.HasRequestedIp && parsed.Options.RequestedIp == 0x0A0F002A &&
              parsed.Options.HasServerIdentifier &&
              parsed.Options.ServerIdentifier == 0x0A0F0002,
              "request-exact-options");
        Check(!ManagedDhcpv4Protocol.TryBuildDiscover(
                  new byte[ManagedDhcpv4Protocol.MinimumPacketLength - 1],
                  1, s_mac, out _), "short-builder-buffer-rejected");
        Check(!ManagedDhcpv4Protocol.TryBuildDiscover(request, 1,
                  new byte[5], out _), "wrong-mac-width-rejected");
        Check(ManagedUdpProtocol.TryBuild(
                  new byte[ManagedUdpProtocol.MaximumDatagramLength], 68, 67,
                  new byte[] { 0, 0, 0, 0 }, new byte[] { 255, 255, 255, 255 },
                  request.AsSpan(0, length), out ushort udpLength) &&
              udpLength == length + ManagedUdpProtocol.HeaderLength,
              "dhcp-udp-ports-and-payload-build");
    }

    private static void ClientValidationTests()
    {
        ManagedDhcpv4Client client = new();
        client.Initialize(s_mac);
        byte[] transmit = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
        Check(client.TryBuildDiscover(transmit, out ushort discoverLength) &&
              client.State == ManagedDhcpv4State.Selecting &&
              client.DiscoverAttempts == 1,
              "client-discover-selecting");
        Check(ManagedDhcpv4Protocol.TryParse(transmit.AsSpan(0, discoverLength),
                  out ManagedDhcpv4Packet discover) &&
              discover.TransactionId == client.TransactionId,
              "client-transaction-published");

        byte[] offer = BuildReply(client.TransactionId, s_mac, s_leaseIp,
                                  ManagedDhcpv4MessageType.Offer, true, true, false);
        byte[] request = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
        byte[] wrongXid = (byte[])offer.Clone();
        wrongXid[7] ^= 1;
        Check(client.TryProcessResponse(s_serverIp, wrongXid, request, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored &&
              client.State == ManagedDhcpv4State.Selecting && !client.HasLease,
              "wrong-xid-offer-rejected");
        byte[] wrongMac = (byte[])offer.Clone();
        wrongMac[33] ^= 1;
        Check(client.TryProcessResponse(s_serverIp, wrongMac, request, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored,
              "wrong-chaddr-offer-rejected");
        byte[] noServer = BuildReply(client.TransactionId, s_mac, s_leaseIp,
                                     ManagedDhcpv4MessageType.Offer, false, true, false);
        Check(client.TryProcessResponse(s_serverIp, noServer, request, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored &&
              client.State == ManagedDhcpv4State.Selecting,
              "offer-without-server-rejected");
        byte[] noMask = BuildReply(client.TransactionId, s_mac, s_leaseIp,
                                   ManagedDhcpv4MessageType.Offer, true, false, false);
        Check(client.TryProcessResponse(s_serverIp, noMask, request, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored &&
              client.State == ManagedDhcpv4State.Selecting,
              "offer-without-mask-rejected");
        Check(client.TryProcessResponse(new byte[] { 10, 15, 0, 9 }, offer,
                  request, out _) == ManagedDhcpv4ReceiveResult.Ignored &&
              client.State == ManagedDhcpv4State.Selecting,
              "offer-source-server-mismatch-rejected");

        Check(client.TryProcessResponse(s_serverIp, offer, request,
                  out ushort requestLength) == ManagedDhcpv4ReceiveResult.RequestReady &&
              client.State == ManagedDhcpv4State.Requesting &&
              client.HasCandidate && !client.HasLease,
              "valid-offer-enters-requesting-only");
        Check(ManagedDhcpv4Protocol.TryParse(request.AsSpan(0, requestLength),
                  out ManagedDhcpv4Packet requestPacket) &&
              requestPacket.Options.MessageType == ManagedDhcpv4MessageType.Request &&
              requestPacket.Options.RequestedIp == 0x0A0F002A &&
              requestPacket.Options.ServerIdentifier == 0x0A0F0002 &&
              requestPacket.Flags == ManagedDhcpv4Protocol.BroadcastFlag,
              "valid-offer-builds-broadcast-request");

        byte[] wrongAckServer = BuildReply(client.TransactionId, s_mac, s_leaseIp,
                                           ManagedDhcpv4MessageType.Ack, true, true, false,
                                           0x0A0F0009);
        Check(client.TryProcessResponse(s_serverIp, wrongAckServer, request, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored && !client.HasLease &&
              client.State == ManagedDhcpv4State.Requesting,
              "wrong-server-ack-rejected");
        byte[] wrongAckIp = BuildReply(client.TransactionId, s_mac,
                                       new byte[] { 10, 15, 0, 43 },
                                       ManagedDhcpv4MessageType.Ack, true, true, false);
        Check(client.TryProcessResponse(s_serverIp, wrongAckIp, request, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored && !client.HasLease,
              "wrong-yiaddr-ack-rejected");
        byte[] ack = BuildReply(client.TransactionId, s_mac, s_leaseIp,
                                ManagedDhcpv4MessageType.Ack, true, true, true);
        Check(client.TryProcessResponse(s_serverIp, ack, request, out _) ==
                  ManagedDhcpv4ReceiveResult.Bound && client.State == ManagedDhcpv4State.Bound &&
              client.HasLease && client.LeasedIpv4.SequenceEqual(s_leaseIp) &&
              client.LeasedMask.SequenceEqual(s_mask) && client.LeasedLeaseTime == 3600,
              "matching-ack-commits-atomically");
    }

    private static void RetryAndTeardownTests()
    {
        ManagedDhcpv4Client client = new();
        client.Initialize(s_mac);
        byte[] discover = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
        Check(client.TryBuildDiscover(discover, out _) && client.TryRetry() &&
              client.TryBuildDiscover(discover, out _) && client.TransactionId != 0,
              "retry-gets-new-transaction-id");
        uint current = client.TransactionId;
        byte[] stale = BuildReply(current - 1, s_mac, s_leaseIp,
                                  ManagedDhcpv4MessageType.Offer, true, true, false);
        Check(client.TryProcessResponse(s_serverIp, stale, discover, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored &&
              client.State == ManagedDhcpv4State.Selecting,
              "stale-offer-after-retry-rejected");
        Check(client.TryRetry() && client.TryBuildDiscover(discover, out _) &&
              client.DiscoverAttempts == ManagedDhcpv4Client.MaximumDiscoverAttempts,
              "bounded-three-discover-attempts");
        Check(client.TryRetry() &&
              !client.TryBuildDiscover(discover, out _),
              "discover-retry-bound-is-finite");
        client.ResetForTeardown();
        Check(client.State == ManagedDhcpv4State.Disabled && !client.HasLease &&
              client.TransactionId == 0 && client.DiscoverAttempts == 0 &&
              client.RequestAttempts == 0,
              "teardown-clears-dhcp-state");
        client.Initialize(s_mac);
        Check(client.State == ManagedDhcpv4State.Init &&
              client.TryBuildDiscover(discover, out _) &&
              client.TransactionId != current &&
              client.TryProcessResponse(s_serverIp, stale, discover, out _) ==
                  ManagedDhcpv4ReceiveResult.Ignored &&
              client.State == ManagedDhcpv4State.Selecting,
              "reinit-gets-fresh-transaction-and-rejects-stale-reply");

        ManagedDhcpv4Client nakClient = new();
        nakClient.Initialize(s_mac);
        Check(nakClient.TryBuildDiscover(discover, out _) &&
              nakClient.TryProcessResponse(s_serverIp,
                  BuildReply(nakClient.TransactionId, s_mac, s_leaseIp,
                             ManagedDhcpv4MessageType.Offer, true, true, false),
                  discover, out _) == ManagedDhcpv4ReceiveResult.RequestReady,
              "nak-test-enters-requesting");
        byte[] nak = BuildReply(nakClient.TransactionId, s_mac, s_leaseIp,
                                ManagedDhcpv4MessageType.Nak, true, false, false);
        Check(nakClient.TryProcessResponse(s_serverIp, nak, discover, out _) ==
                  ManagedDhcpv4ReceiveResult.Nak &&
              nakClient.State == ManagedDhcpv4State.Init && !nakClient.HasLease &&
              !nakClient.HasCandidate,
              "matching-nak-clears-candidate");
    }

    private static byte[] BuildReply(uint xid, byte[] mac, byte[] yiaddr,
                                     ManagedDhcpv4MessageType messageType,
                                     bool includeServer, bool includeMask,
                                     bool includeLease, uint server = 0x0A0F0002)
    {
        byte[] packet = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
        packet[0] = ManagedDhcpv4Protocol.BootReply;
        packet[1] = ManagedDhcpv4Protocol.HardwareTypeEthernet;
        packet[2] = ManagedDhcpv4Protocol.HardwareAddressLength;
        ManagedEthernetProtocol.WriteUInt32Network(packet, 4, xid);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 10,
                                                   ManagedDhcpv4Protocol.BroadcastFlag);
        yiaddr.CopyTo(packet.AsSpan(16, 4));
        mac.CopyTo(packet.AsSpan(28, 6));
        ManagedEthernetProtocol.WriteUInt32Network(packet, 236,
                                                   ManagedDhcpv4Protocol.MagicCookie);
        int offset = 240;
        packet[offset++] = ManagedDhcpv4Protocol.OptionMessageType;
        packet[offset++] = 1;
        packet[offset++] = (byte)messageType;
        if (includeMask) WriteOption(packet, ref offset, 1, 0xFFFFFF00);
        if (includeServer) WriteOption(packet, ref offset, 54, server);
        if (includeLease) WriteOption(packet, ref offset, 51, 3600);
        if (messageType == ManagedDhcpv4MessageType.Ack)
            WriteOption(packet, ref offset, 3, 0x0A0F0002);
        packet[offset++] = ManagedDhcpv4Protocol.OptionEnd;
        return packet.AsSpan(0, offset).ToArray();
    }

    private static void WriteOption(byte[] packet, ref int offset, byte code,
                                    uint value)
    {
        packet[offset++] = code;
        packet[offset++] = 4;
        ManagedEthernetProtocol.WriteUInt32Network(packet, offset, value);
        offset += 4;
    }

    private static byte[] MakeOptions(byte[] bytes)
    {
        return bytes;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {name}");
        s_cases++;
        Console.WriteLine($"PASS: {name}");
    }
}

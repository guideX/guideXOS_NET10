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
        ArpTests();
        CacheTests();
        Console.WriteLine($"MANAGED_KERNEL_PHASE16_HOST_TESTS_PASS cases={s_cases}");
        return 0;
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
                  new byte[59], s_guestMac, out _), "truncated-ethernet-rejected");

        byte[] unknown = new byte[60];
        Check(ManagedEthernetProtocol.TryBuildFrame(
                  unknown, s_guestMac, s_guestMac, 0x88B5, new byte[1],
                  out ushort unknownLength) &&
              unknownLength == 60 &&
              ManagedEthernetProtocol.TryParseFrame(unknown, s_guestMac,
                                                     out ManagedEthernetFrame unknownView) &&
              unknownView.EtherType == 0x88B5,
              "unknown-ether-type-safe");

        byte[] local = BuildRequest(s_guestMac, s_hostMac, s_hostIp, s_guestIp);
        Check(ManagedEthernetProtocol.TryParseFrame(
                  local, s_guestMac, out _), "local-unicast-destination-accepted");
        Check(ManagedEthernetProtocol.TryParseFrame(
                  request, s_guestMac, out _), "broadcast-destination-accepted");
        byte[] unrelated = BuildRequest(s_hostMac, s_hostMac, s_hostIp, s_guestIp);
        Check(!ManagedEthernetProtocol.TryParseFrame(
                  unrelated, s_guestMac, out _), "unrelated-destination-rejected");
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
              maximumLength == maximum.Length,
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
        return ManagedArpProtocol.TryParse(mutated.AsSpan(14), out _);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + name);
        s_cases++;
        Console.WriteLine("PASS: " + name);
    }
}

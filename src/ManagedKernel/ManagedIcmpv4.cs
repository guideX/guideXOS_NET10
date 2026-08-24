using System;

namespace GuideXOS.Net10.ManagedKernel;

internal readonly ref struct ManagedIcmpv4Packet
{
    internal readonly byte Type;
    internal readonly byte Code;
    internal readonly ushort Checksum;
    internal readonly ushort Identifier;
    internal readonly ushort Sequence;
    internal readonly ReadOnlySpan<byte> Payload;

    internal ManagedIcmpv4Packet(ReadOnlySpan<byte> packet)
    {
        Type = packet[0];
        Code = packet[1];
        Checksum = ManagedEthernetProtocol.ReadUInt16Network(packet, 2);
        Identifier = ManagedEthernetProtocol.ReadUInt16Network(packet, 4);
        Sequence = ManagedEthernetProtocol.ReadUInt16Network(packet, 6);
        Payload = packet.Slice(8);
    }
}

internal static class ManagedIcmpv4Protocol
{
    internal const byte EchoReply = 0;
    internal const byte EchoRequest = 8;
    internal const byte EchoCode = 0;
    internal const int HeaderLength = 8;
    internal const int MaximumEchoPayloadLength = 256;

    internal static bool TryParse(ReadOnlySpan<byte> packet,
                                  out ManagedIcmpv4Packet parsed)
    {
        parsed = default;
        if (packet.Length < HeaderLength ||
            packet.Length > HeaderLength + MaximumEchoPayloadLength ||
            (packet[0] != EchoRequest && packet[0] != EchoReply) ||
            packet[1] != EchoCode || ComputeChecksum(packet) != 0)
            return false;
        parsed = new ManagedIcmpv4Packet(packet);
        return true;
    }

    internal static bool TryBuildEchoRequest(Span<byte> packet,
                                              ushort identifier,
                                              ushort sequence,
                                              ReadOnlySpan<byte> payload,
                                              out ushort length)
    {
        return TryBuild(packet, EchoRequest, identifier, sequence, payload,
                        out length);
    }

    internal static bool TryBuildEchoReply(Span<byte> packet,
                                            ushort identifier,
                                            ushort sequence,
                                            ReadOnlySpan<byte> payload,
                                            out ushort length)
    {
        return TryBuild(packet, EchoReply, identifier, sequence, payload,
                        out length);
    }

    internal static ushort ComputeChecksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        int offset = 0;
        while (offset + 1 < bytes.Length)
        {
            sum += (uint)((bytes[offset] << 8) | bytes[offset + 1]);
            sum = (sum & 0xFFFFU) + (sum >> 16);
            offset += 2;
        }
        if (offset < bytes.Length)
        {
            sum += (uint)(bytes[offset] << 8);
            sum = (sum & 0xFFFFU) + (sum >> 16);
        }
        sum = (sum & 0xFFFFU) + (sum >> 16);
        sum = (sum & 0xFFFFU) + (sum >> 16);
        return (ushort)~sum;
    }

    private static bool TryBuild(Span<byte> packet, byte type,
                                 ushort identifier, ushort sequence,
                                 ReadOnlySpan<byte> payload,
                                 out ushort length)
    {
        length = 0;
        int requiredLength = HeaderLength + payload.Length;
        if (payload.Length > MaximumEchoPayloadLength ||
            packet.Length < requiredLength)
            return false;
        packet.Slice(0, requiredLength).Clear();
        packet[0] = type;
        packet[1] = EchoCode;
        ManagedEthernetProtocol.WriteUInt16Network(packet, 4, identifier);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 6, sequence);
        payload.CopyTo(packet.Slice(HeaderLength));
        ManagedEthernetProtocol.WriteUInt16Network(
            packet, 2, ComputeChecksum(packet.Slice(0, requiredLength)));
        length = (ushort)requiredLength;
        return true;
    }
}

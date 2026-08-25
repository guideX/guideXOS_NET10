using System;

namespace GuideXOS.Net10.ManagedKernel;

[Flags]
internal enum ManagedTcpFlags : byte
{
    Fin = 0x01,
    Syn = 0x02,
    Rst = 0x04,
    Psh = 0x08,
    Ack = 0x10
}

internal readonly ref struct ManagedTcpSegment
{
    internal readonly ushort SourcePort;
    internal readonly ushort DestinationPort;
    internal readonly uint SequenceNumber;
    internal readonly uint AcknowledgmentNumber;
    internal readonly byte DataOffset;
    internal readonly ManagedTcpFlags Flags;
    internal readonly ushort Window;
    internal readonly ushort Checksum;
    internal readonly ushort UrgentPointer;
    internal readonly bool HasMss;
    internal readonly ushort Mss;
    internal readonly ReadOnlySpan<byte> SourceAddressForTcp;
    internal readonly ReadOnlySpan<byte> DestinationAddressForTcp;
    internal readonly ReadOnlySpan<byte> Options;
    internal readonly ReadOnlySpan<byte> Payload;

    internal ManagedTcpSegment(ReadOnlySpan<byte> packet, byte headerLength,
                               bool hasMss, ushort mss,
                               ReadOnlySpan<byte> sourceAddress,
                               ReadOnlySpan<byte> destinationAddress)
    {
        SourcePort = ManagedEthernetProtocol.ReadUInt16Network(packet, 0);
        DestinationPort = ManagedEthernetProtocol.ReadUInt16Network(packet, 2);
        SequenceNumber = ManagedEthernetProtocol.ReadUInt32Network(packet, 4);
        AcknowledgmentNumber = ManagedEthernetProtocol.ReadUInt32Network(packet, 8);
        DataOffset = (byte)(headerLength / 4);
        Flags = (ManagedTcpFlags)packet[13];
        Window = ManagedEthernetProtocol.ReadUInt16Network(packet, 14);
        Checksum = ManagedEthernetProtocol.ReadUInt16Network(packet, 16);
        UrgentPointer = ManagedEthernetProtocol.ReadUInt16Network(packet, 18);
        HasMss = hasMss;
        Mss = mss;
        SourceAddressForTcp = sourceAddress;
        DestinationAddressForTcp = destinationAddress;
        Options = packet.Slice(20, headerLength - 20);
        Payload = packet.Slice(headerLength);
    }

    internal int PayloadLength => Payload.Length;
    internal bool Has(ManagedTcpFlags flag) => (Flags & flag) != 0;
}

internal static class ManagedTcpProtocol
{
    internal const byte Protocol = 6;
    internal const byte HeaderLength = 20;
    internal const byte MaximumHeaderLength = 60;
    internal const ushort MaximumPayloadLength = 512;
    internal const ushort MaximumMss = 512;
    internal const ushort DefaultWindow = 512;

    internal static bool TryParse(ReadOnlySpan<byte> packet,
                                  ReadOnlySpan<byte> sourceAddress,
                                  ReadOnlySpan<byte> destinationAddress,
                                  out ManagedTcpSegment parsed)
    {
        parsed = default;
        if (sourceAddress.Length != 4 || destinationAddress.Length != 4 ||
            packet.Length < HeaderLength)
            return false;

        byte dataOffsetWords = (byte)(packet[12] >> 4);
        if (dataOffsetWords < 5 || dataOffsetWords > 15)
            return false;
        int headerLength = dataOffsetWords * 4;
        if (headerLength < HeaderLength || headerLength > MaximumHeaderLength ||
            packet.Length < headerLength)
            return false;

        // NS and the three reserved bits are not supported by this bounded
        // client.  CWR/ECE/URG are also rejected because their semantics are
        // intentionally outside Phase 22.
        if ((packet[12] & 0x0F) != 0 || (packet[13] & 0xE0) != 0)
            return false;
        if (packet.Length - headerLength > MaximumPayloadLength)
            return false;

        bool hasMss = false;
        ushort mss = 0;
        if (!TryParseOptions(packet.Slice(HeaderLength, headerLength - HeaderLength),
                             ref hasMss, ref mss))
            return false;
        if (ComputeChecksum(sourceAddress, destinationAddress, packet) != 0)
            return false;

        parsed = new ManagedTcpSegment(packet, (byte)headerLength, hasMss, mss,
                                       sourceAddress, destinationAddress);
        return true;
    }

    internal static bool TryBuild(Span<byte> packet,
                                  ushort sourcePort,
                                  ushort destinationPort,
                                  uint sequenceNumber,
                                  uint acknowledgmentNumber,
                                  ManagedTcpFlags flags,
                                  ushort window,
                                  ReadOnlySpan<byte> sourceAddress,
                                  ReadOnlySpan<byte> destinationAddress,
                                  ReadOnlySpan<byte> payload,
                                  bool advertiseMss,
                                  ushort mss,
                                  out ushort length)
    {
        length = 0;
        if (sourceAddress.Length != 4 || destinationAddress.Length != 4 ||
            (flags & (ManagedTcpFlags)0xE0) != 0 ||
            payload.Length > MaximumPayloadLength ||
            (advertiseMss && (!(flags.HasFlag(ManagedTcpFlags.Syn)) ||
                              mss == 0 || mss > MaximumMss)))
            return false;

        int headerLength = HeaderLength + (advertiseMss ? 4 : 0);
        int requiredLength = headerLength + payload.Length;
        if (packet.Length < requiredLength)
            return false;

        packet.Slice(0, requiredLength).Clear();
        ManagedEthernetProtocol.WriteUInt16Network(packet, 0, sourcePort);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 2, destinationPort);
        ManagedEthernetProtocol.WriteUInt32Network(packet, 4, sequenceNumber);
        ManagedEthernetProtocol.WriteUInt32Network(packet, 8, acknowledgmentNumber);
        packet[12] = (byte)((headerLength / 4) << 4);
        packet[13] = (byte)flags;
        ManagedEthernetProtocol.WriteUInt16Network(packet, 14, window);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 16, 0);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 18, 0);
        if (advertiseMss)
        {
            packet[20] = 2;
            packet[21] = 4;
            ManagedEthernetProtocol.WriteUInt16Network(packet, 22, mss);
        }
        payload.CopyTo(packet.Slice(headerLength));
        ManagedEthernetProtocol.WriteUInt16Network(
            packet, 16, ComputeChecksum(sourceAddress, destinationAddress,
                                        packet.Slice(0, requiredLength)));
        length = (ushort)requiredLength;
        return true;
    }

    internal static ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress,
                                           ReadOnlySpan<byte> destinationAddress,
                                           ReadOnlySpan<byte> packet)
    {
        if (sourceAddress.Length != 4 || destinationAddress.Length != 4 ||
            packet.Length > ushort.MaxValue)
            return 0;

        uint sum = 0;
        AddWord(ref sum, sourceAddress[0], sourceAddress[1]);
        AddWord(ref sum, sourceAddress[2], sourceAddress[3]);
        AddWord(ref sum, destinationAddress[0], destinationAddress[1]);
        AddWord(ref sum, destinationAddress[2], destinationAddress[3]);
        AddWord(ref sum, 0, Protocol);
        AddWord(ref sum, (byte)(packet.Length >> 8), (byte)packet.Length);
        for (int offset = 0; offset + 1 < packet.Length; offset += 2)
            AddWord(ref sum, packet[offset], packet[offset + 1]);
        if ((packet.Length & 1) != 0)
            AddWord(ref sum, packet[packet.Length - 1], 0);
        Fold(ref sum);
        return (ushort)~sum;
    }

    private static bool TryParseOptions(ReadOnlySpan<byte> options,
                                        ref bool hasMss, ref ushort mss)
    {
        int offset = 0;
        while (offset < options.Length)
        {
            byte kind = options[offset++];
            if (kind == 0) return true;
            if (kind == 1) continue;
            if (offset >= options.Length) return false;
            byte optionLength = options[offset++];
            if (optionLength < 2 || offset - 2 + optionLength > options.Length)
                return false;
            if (kind == 2)
            {
                if (optionLength != 4 || hasMss) return false;
                hasMss = true;
                mss = ManagedEthernetProtocol.ReadUInt16Network(options, offset);
            }
            offset += optionLength - 2;
        }
        return true;
    }

    private static void AddWord(ref uint sum, byte high, byte low)
    {
        sum += (uint)((high << 8) | low);
        Fold(ref sum);
    }

    private static void Fold(ref uint sum)
    {
        sum = (sum & 0xFFFFU) + (sum >> 16);
        sum = (sum & 0xFFFFU) + (sum >> 16);
    }
}

internal static class ManagedTcpSequence
{
    internal static bool IsBefore(uint left, uint right) =>
        unchecked((int)(left - right)) < 0;

    internal static bool IsAfter(uint left, uint right) =>
        unchecked((int)(left - right)) > 0;

    internal static bool IsBeforeOrEqual(uint left, uint right) =>
        left == right || IsBefore(left, right);

    internal static bool IsAfterOrEqual(uint left, uint right) =>
        left == right || IsAfter(left, right);

    internal static uint Advance(uint sequence, uint length) =>
        unchecked(sequence + length);
}

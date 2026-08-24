using System;

namespace GuideXOS.Net10.ManagedKernel;

internal ref struct ManagedEthernetFrame
{
    internal ReadOnlySpan<byte> Destination;
    internal ReadOnlySpan<byte> Source;
    internal ushort EtherType;
    internal ReadOnlySpan<byte> Payload;

    internal ManagedEthernetFrame(ReadOnlySpan<byte> frame, ushort etherType)
    {
        Destination = frame.Slice(0, ManagedEthernetProtocol.MacLength);
        Source = frame.Slice(6, ManagedEthernetProtocol.MacLength);
        EtherType = etherType;
        Payload = frame.Slice(ManagedEthernetProtocol.HeaderLength);
    }
}

/* Ethernet II is deliberately small: no VLAN, LLC/SNAP, jumbo, or
   promiscuous-mode policy is hidden here.  This type owns only bounded L2
   interpretation and construction; the e1000 driver owns transport. */
internal static class ManagedEthernetProtocol
{
    internal const int MacLength = 6;
    internal const int HeaderLength = 14;
    internal const int MinimumFrameLength = 60;
    internal const ushort ArpEtherType = 0x0806;
    internal const int ProtocolAddressLength = 4;
    internal const int MaximumFrameLength = (int)ManagedE1000Protocol.PacketBufferSize;

    internal static bool TryBuildFrame(Span<byte> frame,
                                       ReadOnlySpan<byte> destination,
                                       ReadOnlySpan<byte> source,
                                       ushort etherType,
                                       ReadOnlySpan<byte> payload,
                                       out ushort frameLength)
    {
        frameLength = 0;
        if (frame.Length < MinimumFrameLength || frame.Length > MaximumFrameLength ||
            destination.Length != MacLength || IsInvalidDestination(destination) ||
            !IsUsableSourceMac(source) ||
            payload.Length > MaximumFrameLength - HeaderLength)
            return false;

        int requiredLength = HeaderLength + payload.Length;
        int actualLength = requiredLength < MinimumFrameLength
            ? MinimumFrameLength : requiredLength;
        if (actualLength > frame.Length) return false;

        frame.Slice(0, actualLength).Clear();
        destination.CopyTo(frame.Slice(0, MacLength));
        source.CopyTo(frame.Slice(6, MacLength));
        payload.CopyTo(frame.Slice(HeaderLength));
        frame[12] = (byte)(etherType >> 8);
        frame[13] = (byte)etherType;
        frameLength = (ushort)actualLength;
        return true;
    }

    internal static bool TryParseFrame(ReadOnlySpan<byte> frame,
                                       ReadOnlySpan<byte> localMac,
                                       out ManagedEthernetFrame parsed)
    {
        parsed = default;
        if (frame.Length < MinimumFrameLength || frame.Length > MaximumFrameLength ||
            localMac.Length != MacLength || IsInvalidMac(localMac) ||
            !IsValidDestination(frame.Slice(0, MacLength), localMac) ||
            !IsUsableSourceMac(frame.Slice(6, MacLength)))
            return false;

        ushort etherType = ReadUInt16Network(frame, 12);
        parsed = new ManagedEthernetFrame(frame, etherType);
        return true;
    }

    internal static bool IsBroadcast(ReadOnlySpan<byte> mac)
    {
        if (mac.Length != MacLength) return false;
        for (int index = 0; index != MacLength; ++index)
            if (mac[index] != 0xFF) return false;
        return true;
    }

    internal static bool IsInvalidMac(ReadOnlySpan<byte> mac)
    {
        if (mac.Length != MacLength) return true;
        bool allZero = true;
        bool allOnes = true;
        for (int index = 0; index != MacLength; ++index)
        {
            allZero &= mac[index] == 0;
            allOnes &= mac[index] == 0xFF;
        }
        return allZero || allOnes;
    }

    internal static bool IsUsableSourceMac(ReadOnlySpan<byte> mac)
    {
        return !IsInvalidMac(mac) && (mac[0] & 1) == 0;
    }

    internal static bool IsInvalidDestination(ReadOnlySpan<byte> mac)
    {
        return mac.Length != MacLength ||
               (!IsBroadcast(mac) &&
                (IsInvalidMac(mac) || (mac[0] & 1) != 0));
    }

    internal static bool IsValidDestination(ReadOnlySpan<byte> destination,
                                            ReadOnlySpan<byte> localMac)
    {
        return destination.SequenceEqual(localMac) || IsBroadcast(destination);
    }

    internal static ushort ReadUInt16Network(ReadOnlySpan<byte> bytes, int offset)
    {
        return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    }

    internal static uint ReadUInt32Network(ReadOnlySpan<byte> bytes, int offset)
    {
        return ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
               ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    internal static void WriteUInt16Network(Span<byte> bytes, int offset,
                                             ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    internal static void WriteUInt32Network(Span<byte> bytes, int offset,
                                             uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}

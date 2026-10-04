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
    internal const ushort Ipv4EtherType = 0x0800;
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
        if (frame.Length < HeaderLength || frame.Length > MaximumFrameLength ||
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

    internal static ManagedEthernetDispatchKind ClassifyEtherType(
        ushort etherType, bool phase84TestConsumerEnabled)
    {
        if (etherType == ArpEtherType) return ManagedEthernetDispatchKind.Arp;
        if (etherType == Ipv4EtherType)
            return ManagedEthernetDispatchKind.Ipv4;
        if (etherType == ManagedEthernetPhase84TestConsumer.EtherType &&
            phase84TestConsumerEnabled)
            return ManagedEthernetDispatchKind.Phase84TestConsumer;
        return ManagedEthernetDispatchKind.Unsupported;
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

internal enum ManagedEthernetDispatchKind : byte
{
    Unsupported = 0,
    Arp = 1,
    Ipv4 = 2,
    Phase84TestConsumer = 3
}

internal readonly struct ManagedEthernetTestFrameMetadata
{
    internal readonly ulong DestinationMac;
    internal readonly ulong SourceMac;
    internal readonly ushort EtherType;
    internal readonly ushort PayloadLength;
    internal readonly uint Sentinel;

    internal ManagedEthernetTestFrameMetadata(
        ulong destinationMac, ulong sourceMac, ushort etherType,
        ushort payloadLength, uint sentinel)
    {
        DestinationMac = destinationMac;
        SourceMac = sourceMac;
        EtherType = etherType;
        PayloadLength = payloadLength;
        Sentinel = sentinel;
    }
}

/* Registered only by the Phase 84 acceptance fixture.  The Ethernet parser
   and EtherType dispatch remain production code; this validates the bounded
   private payload used to prove that dispatch reached a consumer. */
internal static class ManagedEthernetPhase84TestConsumer
{
    internal const ushort EtherType = 0x88B5;

    internal static bool TryConsume(in ManagedEthernetFrame frame,
                                    uint expectedSequence,
                                    out ManagedEthernetTestFrameMetadata metadata)
    {
        metadata = default;
        ReadOnlySpan<byte> payload = frame.Payload;
        ReadOnlySpan<byte> signature = ManagedE1000Protocol.Phase83RxPayloadSignature;
        if (frame.EtherType != EtherType || expectedSequence == 0 ||
            payload.Length < signature.Length + 4 ||
            !payload.Slice(0, signature.Length).SequenceEqual(signature))
            return false;

        int sentinelOffset = signature.Length;
        uint sentinel = ((uint)payload[sentinelOffset] << 24) |
                        ((uint)payload[sentinelOffset + 1] << 16) |
                        ((uint)payload[sentinelOffset + 2] << 8) |
                        payload[sentinelOffset + 3];
        if (sentinel != expectedSequence) return false;
        for (int index = sentinelOffset + 4; index != payload.Length; ++index)
            if (payload[index] != 0) return false;

        metadata = new ManagedEthernetTestFrameMetadata(
            ReadMacValue(frame.Destination), ReadMacValue(frame.Source),
            frame.EtherType, (ushort)payload.Length, sentinel);
        return true;
    }

    private static ulong ReadMacValue(ReadOnlySpan<byte> mac)
    {
        ulong value = 0;
        for (int index = 0; index != ManagedEthernetProtocol.MacLength; ++index)
            value = (value << 8) | mac[index];
        return value;
    }
}

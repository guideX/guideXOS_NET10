using System;

namespace GuideXOS.Net10.ManagedKernel;

internal readonly ref struct ManagedIpv4Packet
{
    internal readonly byte Version;
    internal readonly byte HeaderLength;
    internal readonly byte DscpEcn;
    internal readonly ushort TotalLength;
    internal readonly ushort Identification;
    internal readonly ushort FlagsFragmentOffset;
    internal readonly byte Ttl;
    internal readonly byte Protocol;
    internal readonly ushort HeaderChecksum;
    internal readonly ReadOnlySpan<byte> SourceAddress;
    internal readonly ReadOnlySpan<byte> DestinationAddress;
    internal readonly ReadOnlySpan<byte> Payload;

    internal ManagedIpv4Packet(ReadOnlySpan<byte> packet, ushort totalLength)
    {
        Version = (byte)(packet[0] >> 4);
        HeaderLength = (byte)((packet[0] & 0x0F) * 4);
        DscpEcn = packet[1];
        TotalLength = totalLength;
        Identification = ManagedEthernetProtocol.ReadUInt16Network(packet, 4);
        FlagsFragmentOffset = ManagedEthernetProtocol.ReadUInt16Network(packet, 6);
        Ttl = packet[8];
        Protocol = packet[9];
        HeaderChecksum = ManagedEthernetProtocol.ReadUInt16Network(packet, 10);
        SourceAddress = packet.Slice(12, ManagedEthernetProtocol.ProtocolAddressLength);
        DestinationAddress = packet.Slice(16, ManagedEthernetProtocol.ProtocolAddressLength);
        Payload = packet.Slice(HeaderLength, totalLength - HeaderLength);
    }
}

internal static class ManagedIpv4Protocol
{
    internal const ushort EtherType = 0x0800;
    internal const byte Version = 4;
    internal const byte MinimumHeaderLength = 20;
    internal const byte SupportedHeaderWords = 5;
    internal const byte DefaultTtl = 64;
    internal const byte IcmpProtocol = 1;
    internal const ushort MoreFragments = 0x2000;
    internal const ushort FragmentOffsetMask = 0x1FFF;
    internal const int MaximumPacketLength =
        ManagedEthernetProtocol.MaximumFrameLength -
        ManagedEthernetProtocol.HeaderLength;

    internal static bool TryParse(ReadOnlySpan<byte> packet,
                                  uint localAddress,
                                  out ManagedIpv4Packet parsed)
    {
        return TryParse(packet, localAddress, false, out parsed);
    }

    internal static bool TryParse(ReadOnlySpan<byte> packet,
                                  uint localAddress,
                                  bool allowBootstrapBroadcast,
                                  out ManagedIpv4Packet parsed)
    {
        parsed = default;
        if (packet.Length < MinimumHeaderLength ||
            (packet[0] >> 4) != Version)
            return false;

        byte headerWords = (byte)(packet[0] & 0x0F);
        if (headerWords != SupportedHeaderWords)
            return false;

        int headerLength = headerWords * 4;
        if (packet.Length < headerLength) return false;
        ushort totalLength = ManagedEthernetProtocol.ReadUInt16Network(packet, 2);
        if (totalLength < headerLength || totalLength > packet.Length ||
            totalLength > MaximumPacketLength)
            return false;

        ushort flagsFragmentOffset =
            ManagedEthernetProtocol.ReadUInt16Network(packet, 6);
        if ((flagsFragmentOffset & (MoreFragments | FragmentOffsetMask)) != 0)
            return false;
        if (ComputeChecksum(packet.Slice(0, headerLength)) != 0)
            return false;
        uint destinationAddress = ManagedEthernetProtocol.ReadUInt32Network(
            packet, 16);
        if (destinationAddress != localAddress &&
            (!allowBootstrapBroadcast || destinationAddress != 0xFFFFFFFFU))
            return false;

        parsed = new ManagedIpv4Packet(packet, totalLength);
        return true;
    }

    internal static bool TryBuild(Span<byte> packet,
                                  ushort identification,
                                  ushort flagsFragmentOffset,
                                  byte ttl,
                                  byte protocol,
                                  ReadOnlySpan<byte> sourceAddress,
                                  ReadOnlySpan<byte> destinationAddress,
                                  ReadOnlySpan<byte> payload,
                                  out ushort totalLength)
    {
        totalLength = 0;
        if (sourceAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength ||
            destinationAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength ||
            payload.Length > MaximumPacketLength - MinimumHeaderLength)
            return false;

        int requiredLength = MinimumHeaderLength + payload.Length;
        if (requiredLength > ushort.MaxValue || packet.Length < requiredLength)
            return false;
        packet.Slice(0, requiredLength).Clear();
        packet[0] = (byte)(Version << 4 | SupportedHeaderWords);
        packet[1] = 0;
        ManagedEthernetProtocol.WriteUInt16Network(packet, 2,
                                                    (ushort)requiredLength);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 4, identification);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 6,
                                                    flagsFragmentOffset);
        packet[8] = ttl;
        packet[9] = protocol;
        sourceAddress.CopyTo(packet.Slice(12, 4));
        destinationAddress.CopyTo(packet.Slice(16, 4));
        payload.CopyTo(packet.Slice(MinimumHeaderLength));
        ManagedEthernetProtocol.WriteUInt16Network(
            packet, 10, ComputeChecksum(packet.Slice(0, MinimumHeaderLength)));
        totalLength = (ushort)requiredLength;
        return true;
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

    internal static bool IsDirectlyReachable(uint localAddress,
                                             uint subnetMask,
                                             uint destinationAddress)
    {
        return (localAddress & subnetMask) ==
               (destinationAddress & subnetMask);
    }
}

internal sealed class ManagedIpv4PendingTransmission
{
    private readonly byte[] _packet;
    private readonly byte[] _destination =
        new byte[ManagedEthernetProtocol.ProtocolAddressLength];
    private bool _active;
    private ushort _length;

    internal ManagedIpv4PendingTransmission(int capacity =
        ManagedIpv4Protocol.MaximumPacketLength)
    {
        if (capacity <= 0 || capacity > ManagedIpv4Protocol.MaximumPacketLength)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _packet = new byte[capacity];
    }

    internal bool IsActive => _active;
    internal ushort Length => _length;

    internal bool TryStage(ReadOnlySpan<byte> destination,
                           ReadOnlySpan<byte> packet)
    {
        if (_active || destination.Length !=
            ManagedEthernetProtocol.ProtocolAddressLength ||
            packet.Length <= 0 || packet.Length > _packet.Length)
            return false;
        destination.CopyTo(_destination);
        packet.CopyTo(_packet);
        _length = (ushort)packet.Length;
        _active = true;
        return true;
    }

    internal bool TryTake(Span<byte> destination, Span<byte> packet,
                          out ushort length)
    {
        length = 0;
        if (!_active || destination.Length < _destination.Length ||
            packet.Length < _length)
            return false;
        _destination.CopyTo(destination);
        _packet.AsSpan(0, _length).CopyTo(packet);
        length = _length;
        Clear();
        return true;
    }

    internal void Clear()
    {
        _active = false;
        _length = 0;
        _destination.AsSpan().Clear();
    }
}

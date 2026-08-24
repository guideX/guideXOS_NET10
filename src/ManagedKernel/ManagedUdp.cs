using System;

namespace GuideXOS.Net10.ManagedKernel;

internal readonly ref struct ManagedUdpDatagram
{
    internal readonly ushort SourcePort;
    internal readonly ushort DestinationPort;
    internal readonly ushort Length;
    internal readonly ushort Checksum;
    internal readonly ReadOnlySpan<byte> Payload;

    internal ManagedUdpDatagram(ReadOnlySpan<byte> datagram)
    {
        SourcePort = ManagedEthernetProtocol.ReadUInt16Network(datagram, 0);
        DestinationPort = ManagedEthernetProtocol.ReadUInt16Network(datagram, 2);
        Length = ManagedEthernetProtocol.ReadUInt16Network(datagram, 4);
        Checksum = ManagedEthernetProtocol.ReadUInt16Network(datagram, 6);
        Payload = datagram.Slice(HeaderLength, Length - HeaderLength);
    }

    private const int HeaderLength = 8;
}

internal enum ManagedUdpEndpointHandler : byte
{
    None = 0,
    Phase18Echo = 1
}

/* Phase 18 intentionally has no delegate or socket surface.  A fixed table of
   handler identities keeps registration deterministic and avoids retaining a
   callback target across NativeAOT/E1000 lifetime boundaries. */
internal sealed class ManagedUdpEndpointTable
{
    internal const int Capacity = 4;

    private readonly ushort[] _ports = new ushort[Capacity];
    private readonly ManagedUdpEndpointHandler[] _handlers =
        new ManagedUdpEndpointHandler[Capacity];
    private readonly bool[] _active = new bool[Capacity];
    private int _count;

    internal int Count => _count;

    internal bool TryRegister(ushort port, ManagedUdpEndpointHandler handler)
    {
        if (port == 0 || handler == ManagedUdpEndpointHandler.None)
            return false;
        for (int index = 0; index != Capacity; ++index)
        {
            if (_active[index] && _ports[index] == port) return false;
        }
        for (int index = 0; index != Capacity; ++index)
        {
            if (_active[index]) continue;
            _ports[index] = port;
            _handlers[index] = handler;
            _active[index] = true;
            _count++;
            return true;
        }
        return false;
    }

    internal bool TryLookup(ushort port, out ManagedUdpEndpointHandler handler)
    {
        handler = ManagedUdpEndpointHandler.None;
        if (port == 0) return false;
        for (int index = 0; index != Capacity; ++index)
        {
            if (_active[index] && _ports[index] == port)
            {
                handler = _handlers[index];
                return true;
            }
        }
        return false;
    }

    internal bool TryUnregister(ushort port)
    {
        if (port == 0) return false;
        for (int index = 0; index != Capacity; ++index)
        {
            if (!_active[index] || _ports[index] != port) continue;
            _ports[index] = 0;
            _handlers[index] = ManagedUdpEndpointHandler.None;
            _active[index] = false;
            _count--;
            return true;
        }
        return false;
    }

    internal void Clear()
    {
        for (int index = 0; index != Capacity; ++index)
        {
            _ports[index] = 0;
            _handlers[index] = ManagedUdpEndpointHandler.None;
            _active[index] = false;
        }
        _count = 0;
    }
}

internal static class ManagedUdpProtocol
{
    internal const byte Protocol = 17;
    internal const int HeaderLength = 8;
    internal const int MaximumPayloadLength = 512;
    internal const int MaximumDatagramLength = HeaderLength + MaximumPayloadLength;

    internal static bool TryParse(ReadOnlySpan<byte> payload,
                                  ReadOnlySpan<byte> sourceAddress,
                                  ReadOnlySpan<byte> destinationAddress,
                                  out ManagedUdpDatagram parsed)
    {
        parsed = default;
        if (payload.Length < HeaderLength ||
            sourceAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength ||
            destinationAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength)
            return false;

        ushort declaredLength = ManagedEthernetProtocol.ReadUInt16Network(payload, 4);
        if (declaredLength < HeaderLength ||
            declaredLength > payload.Length ||
            declaredLength > MaximumDatagramLength)
            return false;

        ushort sourcePort = ManagedEthernetProtocol.ReadUInt16Network(payload, 0);
        ushort destinationPort = ManagedEthernetProtocol.ReadUInt16Network(payload, 2);
        if (sourcePort == 0 || destinationPort == 0) return false;

        ReadOnlySpan<byte> datagram = payload.Slice(0, declaredLength);
        ushort checksum = ManagedEthernetProtocol.ReadUInt16Network(datagram, 6);
        if (checksum != 0 && ComputeChecksum(sourceAddress, destinationAddress,
                                              datagram) != 0)
            return false;

        parsed = new ManagedUdpDatagram(datagram);
        return true;
    }

    internal static bool TryBuild(Span<byte> datagram,
                                  ushort sourcePort,
                                  ushort destinationPort,
                                  ReadOnlySpan<byte> sourceAddress,
                                  ReadOnlySpan<byte> destinationAddress,
                                  ReadOnlySpan<byte> payload,
                                  out ushort length)
    {
        length = 0;
        if (sourcePort == 0 || destinationPort == 0 ||
            sourceAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength ||
            destinationAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength ||
            payload.Length > MaximumPayloadLength ||
            datagram.Length < HeaderLength + payload.Length)
            return false;

        int requiredLength = HeaderLength + payload.Length;
        datagram.Slice(0, requiredLength).Clear();
        ManagedEthernetProtocol.WriteUInt16Network(datagram, 0, sourcePort);
        ManagedEthernetProtocol.WriteUInt16Network(datagram, 2, destinationPort);
        ManagedEthernetProtocol.WriteUInt16Network(datagram, 4,
                                                    (ushort)requiredLength);
        payload.CopyTo(datagram.Slice(HeaderLength));

        ushort checksum = ComputeChecksum(sourceAddress, destinationAddress,
                                          datagram.Slice(0, requiredLength));
        if (checksum == 0) checksum = 0xFFFF;
        ManagedEthernetProtocol.WriteUInt16Network(datagram, 6, checksum);
        length = (ushort)requiredLength;
        return true;
    }

    /* Returns the one's-complement checksum over the IPv4 pseudo-header and
       the complete declared UDP datagram.  The caller may use zero only for
       receive-side checksum-disabled semantics; transmitted zero is encoded
       as FFFF by TryBuild. */
    internal static ushort ComputeChecksum(ReadOnlySpan<byte> sourceAddress,
                                           ReadOnlySpan<byte> destinationAddress,
                                           ReadOnlySpan<byte> datagram)
    {
        if (sourceAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength ||
            destinationAddress.Length != ManagedEthernetProtocol.ProtocolAddressLength ||
            datagram.Length > ushort.MaxValue)
            return 0;

        uint sum = 0;
        sum = AddWord(sum, ManagedEthernetProtocol.ReadUInt16Network(sourceAddress, 0));
        sum = AddWord(sum, ManagedEthernetProtocol.ReadUInt16Network(sourceAddress, 2));
        sum = AddWord(sum, ManagedEthernetProtocol.ReadUInt16Network(destinationAddress, 0));
        sum = AddWord(sum, ManagedEthernetProtocol.ReadUInt16Network(destinationAddress, 2));
        sum = AddWord(sum, Protocol);
        sum = AddWord(sum, (ushort)datagram.Length);

        int offset = 0;
        while (offset + 1 < datagram.Length)
        {
            sum = AddWord(sum, ManagedEthernetProtocol.ReadUInt16Network(
                datagram, offset));
            offset += 2;
        }
        if (offset < datagram.Length)
            sum = AddWord(sum, (uint)datagram[offset] << 8);

        sum = Fold(sum);
        return (ushort)~sum;
    }

    private static uint AddWord(uint sum, uint word)
    {
        return Fold(sum + word);
    }

    private static uint Fold(uint sum)
    {
        sum = (sum & 0xFFFFU) + (sum >> 16);
        return (sum & 0xFFFFU) + (sum >> 16);
    }
}

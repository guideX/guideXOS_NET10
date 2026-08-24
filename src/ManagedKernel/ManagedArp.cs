using System;

namespace GuideXOS.Net10.ManagedKernel;

internal readonly ref struct ManagedArpPacket
{
    internal readonly ushort HardwareType;
    internal readonly ushort ProtocolType;
    internal readonly byte HardwareLength;
    internal readonly byte ProtocolLength;
    internal readonly ushort Operation;
    internal readonly ReadOnlySpan<byte> SenderMac;
    internal readonly ReadOnlySpan<byte> SenderIpv4;
    internal readonly ReadOnlySpan<byte> TargetMac;
    internal readonly ReadOnlySpan<byte> TargetIpv4;

    internal ManagedArpPacket(ReadOnlySpan<byte> payload)
    {
        HardwareType = ManagedEthernetProtocol.ReadUInt16Network(payload, 0);
        ProtocolType = ManagedEthernetProtocol.ReadUInt16Network(payload, 2);
        HardwareLength = payload[4];
        ProtocolLength = payload[5];
        Operation = ManagedEthernetProtocol.ReadUInt16Network(payload, 6);
        SenderMac = payload.Slice(8, 6);
        SenderIpv4 = payload.Slice(14, 4);
        TargetMac = payload.Slice(18, 6);
        TargetIpv4 = payload.Slice(24, 4);
    }
}

internal static class ManagedArpProtocol
{
    internal const int PayloadLength = 28;
    internal const ushort HardwareTypeEthernet = 1;
    internal const ushort ProtocolTypeIpv4 = 0x0800;
    internal const byte HardwareAddressLength = 6;
    internal const byte ProtocolAddressLength = 4;
    internal const ushort OperationRequest = 1;
    internal const ushort OperationReply = 2;

    internal static bool TryParse(ReadOnlySpan<byte> payload,
                                  out ManagedArpPacket packet)
    {
        packet = default;
        if (payload.Length < PayloadLength) return false;

        ManagedArpPacket candidate = new(payload);
        if (candidate.HardwareType != HardwareTypeEthernet ||
            candidate.ProtocolType != ProtocolTypeIpv4 ||
            candidate.HardwareLength != HardwareAddressLength ||
            candidate.ProtocolLength != ProtocolAddressLength ||
            (candidate.Operation != OperationRequest &&
             candidate.Operation != OperationReply) ||
            !ManagedEthernetProtocol.IsUsableSourceMac(candidate.SenderMac) ||
            !IsUsableIpv4(candidate.SenderIpv4) ||
            !IsUsableIpv4(candidate.TargetIpv4))
            return false;

        packet = candidate;
        return true;
    }

    internal static bool TryBuildRequest(Span<byte> payload,
                                         ReadOnlySpan<byte> senderMac,
                                         ReadOnlySpan<byte> senderIpv4,
                                         ReadOnlySpan<byte> targetIpv4)
    {
        if (!TryPrepare(payload, senderMac, senderIpv4, targetIpv4))
            return false;
        if (!IsZeroMac(payload.Slice(18, 6))) return false;
        ManagedEthernetProtocol.WriteUInt16Network(payload, 6, OperationRequest);
        return true;
    }

    internal static bool TryBuildReply(Span<byte> payload,
                                       ReadOnlySpan<byte> senderMac,
                                       ReadOnlySpan<byte> senderIpv4,
                                       ReadOnlySpan<byte> targetMac,
                                       ReadOnlySpan<byte> targetIpv4)
    {
        if (!TryPrepare(payload, senderMac, senderIpv4, targetIpv4) ||
            targetMac.Length != ManagedEthernetProtocol.MacLength ||
            !ManagedEthernetProtocol.IsUsableSourceMac(targetMac)) return false;
        targetMac.CopyTo(payload.Slice(18, 6));
        ManagedEthernetProtocol.WriteUInt16Network(payload, 6, OperationReply);
        return true;
    }

    internal static bool IsUsableIpv4(ReadOnlySpan<byte> address)
    {
        if (address.Length != ManagedEthernetProtocol.ProtocolAddressLength)
            return false;
        bool allZero = true;
        bool allOnes = true;
        for (int index = 0; index != address.Length; ++index)
        {
            allZero &= address[index] == 0;
            allOnes &= address[index] == 0xFF;
        }
        return !allZero && !allOnes;
    }

    internal static bool IsZeroMac(ReadOnlySpan<byte> mac)
    {
        if (mac.Length != ManagedEthernetProtocol.MacLength) return false;
        for (int index = 0; index != mac.Length; ++index)
            if (mac[index] != 0) return false;
        return true;
    }

    internal static bool IsPendingReplyMatch(ManagedArpPacket packet,
                                             ReadOnlySpan<byte> ethernetSource,
                                             ReadOnlySpan<byte> ethernetDestination,
                                             ReadOnlySpan<byte> localMac,
                                             ReadOnlySpan<byte> localIpv4,
                                             uint pendingIpv4)
    {
        return packet.Operation == OperationReply &&
               ethernetSource.SequenceEqual(packet.SenderMac) &&
               ethernetDestination.SequenceEqual(localMac) &&
               ReadNetworkIpv4(packet.SenderIpv4) == pendingIpv4 &&
               packet.TargetIpv4.SequenceEqual(localIpv4) &&
               packet.TargetMac.SequenceEqual(localMac);
    }

    internal static bool IsRequestForLocal(ManagedArpPacket packet,
                                           ReadOnlySpan<byte> ethernetSource,
                                           ReadOnlySpan<byte> ethernetDestination,
                                           ReadOnlySpan<byte> localIpv4)
    {
        return packet.Operation == OperationRequest &&
               ethernetSource.SequenceEqual(packet.SenderMac) &&
               ManagedEthernetProtocol.IsBroadcast(ethernetDestination) &&
               packet.TargetIpv4.SequenceEqual(localIpv4) &&
               IsZeroMac(packet.TargetMac);
    }

    private static uint ReadNetworkIpv4(ReadOnlySpan<byte> address)
    {
        return ManagedEthernetProtocol.ReadUInt32Network(address, 0);
    }

    private static bool TryPrepare(Span<byte> payload,
                                   ReadOnlySpan<byte> senderMac,
                                   ReadOnlySpan<byte> senderIpv4,
                                   ReadOnlySpan<byte> targetIpv4)
    {
        if (payload.Length != PayloadLength ||
            !ManagedEthernetProtocol.IsUsableSourceMac(senderMac) ||
            !IsUsableIpv4(senderIpv4) || !IsUsableIpv4(targetIpv4))
            return false;
        payload.Clear();
        ManagedEthernetProtocol.WriteUInt16Network(payload, 0,
                                                   HardwareTypeEthernet);
        ManagedEthernetProtocol.WriteUInt16Network(payload, 2, ProtocolTypeIpv4);
        payload[4] = HardwareAddressLength;
        payload[5] = ProtocolAddressLength;
        senderMac.CopyTo(payload.Slice(8, 6));
        senderIpv4.CopyTo(payload.Slice(14, 4));
        targetIpv4.CopyTo(payload.Slice(24, 4));
        return true;
    }

}

/* Fixed-capacity cache.  Generations are monotonic and the lowest generation
   is replaced when all eight slots are occupied.  There is no growth path. */
internal sealed class ManagedArpCache
{
    internal const int DefaultCapacity = 8;
    private readonly uint[] _ipv4;
    private readonly byte[,] _mac;
    private readonly byte[] _valid;
    private readonly uint[] _generation;
    private uint _nextGeneration;
    private int _count;

    internal ManagedArpCache(int capacity = DefaultCapacity)
    {
        if (capacity <= 0 || capacity > 64)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _ipv4 = new uint[capacity];
        _mac = new byte[capacity, ManagedEthernetProtocol.MacLength];
        _valid = new byte[capacity];
        _generation = new uint[capacity];
    }

    internal int Capacity => _valid.Length;
    internal int Count => _count;

    internal bool TryLookup(ReadOnlySpan<byte> ipv4, Span<byte> mac)
    {
        if (!TryReadIpv4(ipv4, out uint key) ||
            mac.Length < ManagedEthernetProtocol.MacLength) return false;
        int index = Find(key);
        if (index < 0) return false;
        for (int byteIndex = 0; byteIndex != ManagedEthernetProtocol.MacLength;
             ++byteIndex)
            mac[byteIndex] = _mac[index, byteIndex];
        _generation[index] = NextGeneration();
        return true;
    }

    internal bool TryLearn(ReadOnlySpan<byte> ipv4, ReadOnlySpan<byte> mac)
    {
        return TryLearn(ipv4, mac, out _);
    }

    internal bool TryLearn(ReadOnlySpan<byte> ipv4, ReadOnlySpan<byte> mac,
                           out bool updatedExisting)
    {
        updatedExisting = false;
        if (!TryReadIpv4(ipv4, out uint key) ||
            !ManagedEthernetProtocol.IsUsableSourceMac(mac)) return false;

        int index = Find(key);
        if (index >= 0)
        {
            updatedExisting = true;
        }
        else
        {
            index = FindEmpty();
            if (index < 0) index = FindOldest();
            if (_valid[index] == 0) _count++;
            _ipv4[index] = key;
            _valid[index] = 1;
        }

        for (int byteIndex = 0; byteIndex != ManagedEthernetProtocol.MacLength;
             ++byteIndex)
            _mac[index, byteIndex] = mac[byteIndex];
        _generation[index] = NextGeneration();
        return true;
    }

    internal void Clear()
    {
        Array.Clear(_valid, 0, _valid.Length);
        Array.Clear(_generation, 0, _generation.Length);
        _count = 0;
        _nextGeneration = 0;
    }

    internal bool IsValidIndex(int index)
    {
        return index >= 0 && index < _valid.Length && _valid[index] != 0;
    }

    private int Find(uint key)
    {
        for (int index = 0; index != _ipv4.Length; ++index)
            if (_valid[index] != 0 && _ipv4[index] == key) return index;
        return -1;
    }

    private int FindEmpty()
    {
        for (int index = 0; index != _valid.Length; ++index)
            if (_valid[index] == 0) return index;
        return -1;
    }

    private int FindOldest()
    {
        int oldest = 0;
        for (int index = 1; index != _generation.Length; ++index)
            if (_generation[index] < _generation[oldest]) oldest = index;
        return oldest;
    }

    private uint NextGeneration()
    {
        _nextGeneration++;
        if (_nextGeneration == 0)
        {
            _nextGeneration = 1;
            for (int index = 0; index != _generation.Length; ++index)
                if (_valid[index] != 0) _generation[index] = 1;
        }
        return _nextGeneration;
    }

    private static bool TryReadIpv4(ReadOnlySpan<byte> ipv4, out uint key)
    {
        key = 0;
        if (!ManagedArpProtocol.IsUsableIpv4(ipv4)) return false;
        key = ManagedEthernetProtocol.ReadUInt32Network(ipv4, 0);
        return true;
    }
}

internal enum ManagedArpHandleResult : byte
{
    Invalid = 0,
    Ignored = 1,
    ReplySatisfied = 2,
    ResponderReplySent = 3,
    Failed = 4
}

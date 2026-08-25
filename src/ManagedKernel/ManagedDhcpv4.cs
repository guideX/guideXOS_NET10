using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedDhcpv4MessageType : byte
{
    Discover = 1,
    Offer = 2,
    Request = 3,
    Decline = 4,
    Ack = 5,
    Nak = 6
}

internal enum ManagedDhcpv4State : byte
{
    Disabled = 0,
    Init = 1,
    Selecting = 2,
    Requesting = 3,
    Bound = 4,
    Failed = 5
}

internal enum ManagedDhcpv4ReceiveResult : byte
{
    Ignored = 0,
    Malformed = 1,
    RequestReady = 2,
    Bound = 3,
    Nak = 4
}

internal struct ManagedDhcpv4OptionValues
{
    internal bool HasSubnetMask;
    internal uint SubnetMask;
    internal bool HasRouter;
    internal uint Router;
    internal byte DnsCount;
    internal uint DnsServer1;
    internal uint DnsServer2;
    internal bool HasRequestedIp;
    internal uint RequestedIp;
    internal bool HasLeaseTime;
    internal uint LeaseTime;
    internal bool HasMessageType;
    internal ManagedDhcpv4MessageType MessageType;
    internal bool HasServerIdentifier;
    internal uint ServerIdentifier;
    internal byte ParameterRequestListLength;
}

internal readonly ref struct ManagedDhcpv4Packet
{
    internal readonly byte Op;
    internal readonly byte HardwareType;
    internal readonly byte HardwareLength;
    internal readonly byte Hops;
    internal readonly uint TransactionId;
    internal readonly ushort SecondsElapsed;
    internal readonly ushort Flags;
    internal readonly ReadOnlySpan<byte> ClientIpv4;
    internal readonly ReadOnlySpan<byte> YourIpv4;
    internal readonly ReadOnlySpan<byte> ServerIpv4;
    internal readonly ReadOnlySpan<byte> RelayIpv4;
    internal readonly ReadOnlySpan<byte> ClientHardwareAddress;
    internal readonly ManagedDhcpv4OptionValues Options;

    internal ManagedDhcpv4Packet(ReadOnlySpan<byte> packet,
                                  ManagedDhcpv4OptionValues options)
    {
        Op = packet[0];
        HardwareType = packet[1];
        HardwareLength = packet[2];
        Hops = packet[3];
        TransactionId = ManagedEthernetProtocol.ReadUInt32Network(packet, 4);
        SecondsElapsed = ManagedEthernetProtocol.ReadUInt16Network(packet, 8);
        Flags = ManagedEthernetProtocol.ReadUInt16Network(packet, 10);
        ClientIpv4 = packet.Slice(12, 4);
        YourIpv4 = packet.Slice(16, 4);
        ServerIpv4 = packet.Slice(20, 4);
        RelayIpv4 = packet.Slice(24, 4);
        ClientHardwareAddress = packet.Slice(28, 16);
        Options = options;
    }
}

/* The DHCP subset deliberately has no option dictionary.  The parser keeps
   the fixed fields that Phase 19 can consume and skips all other options with
   a validated length.  Callers own the optional parameter-request-list
   storage supplied to TryParseOptions. */
internal static class ManagedDhcpv4Protocol
{
    internal const byte BootRequest = 1;
    internal const byte BootReply = 2;
    internal const byte HardwareTypeEthernet = 1;
    internal const byte HardwareAddressLength = 6;
    internal const ushort BroadcastFlag = 0x8000;
    internal const int FixedHeaderLength = 236;
    internal const int CookieLength = 4;
    internal const int MinimumPacketLength = FixedHeaderLength + CookieLength;
    internal const int MaximumPacketLength = ManagedUdpProtocol.MaximumPayloadLength;
    internal const uint MagicCookie = 0x63825363;
    internal const byte OptionPad = 0;
    internal const byte OptionSubnetMask = 1;
    internal const byte OptionRouter = 3;
    internal const byte OptionDnsServer = 6;
    internal const byte OptionRequestedIp = 50;
    internal const byte OptionLeaseTime = 51;
    internal const byte OptionMessageType = 53;
    internal const byte OptionServerIdentifier = 54;
    internal const byte OptionParameterRequestList = 55;
    internal const byte OptionEnd = 255;
    internal const int MaximumParameterRequestListLength = 16;

    internal static bool TryParse(ReadOnlySpan<byte> packet,
                                  out ManagedDhcpv4Packet parsed)
    {
        parsed = default;
        if (packet.Length < MinimumPacketLength ||
            packet.Length > MaximumPacketLength ||
            ManagedEthernetProtocol.ReadUInt32Network(packet, FixedHeaderLength) !=
                MagicCookie ||
            packet[2] == 0 || packet[2] > 16)
            return false;

        Span<byte> parameterRequestList = stackalloc byte[
            MaximumParameterRequestListLength];
        if (!TryParseOptions(packet.Slice(MinimumPacketLength),
                             parameterRequestList,
                             out ManagedDhcpv4OptionValues options))
            return false;
        parsed = new ManagedDhcpv4Packet(packet, options);
        return true;
    }

    internal static bool TryParseOptions(ReadOnlySpan<byte> options,
                                         Span<byte> parameterRequestList,
                                         out ManagedDhcpv4OptionValues values)
    {
        values = default;
        if (parameterRequestList.Length > MaximumParameterRequestListLength)
            return false;

        int offset = 0;
        bool ended = false;
        while (offset < options.Length)
        {
            byte code = options[offset++];
            if (code == OptionPad) continue;
            if (code == OptionEnd)
            {
                ended = true;
                break;
            }
            if (offset >= options.Length) return false;
            int length = options[offset++];
            if (length > options.Length - offset) return false;
            ReadOnlySpan<byte> payload = options.Slice(offset, length);
            if (!TryReadOption(code, payload, parameterRequestList, ref values))
                return false;
            offset += length;
        }
        return ended;
    }

    internal static bool TryBuildDiscover(Span<byte> packet, uint transactionId,
                                          ReadOnlySpan<byte> clientMac,
                                          out ushort length)
    {
        length = 0;
        if (!TryInitialize(packet, transactionId, clientMac)) return false;
        int offset = MinimumPacketLength;
        if (!TryWriteByteOption(packet, ref offset, OptionMessageType,
                                (byte)ManagedDhcpv4MessageType.Discover) ||
            !TryWriteParameterRequestList(packet, ref offset) ||
            !TryWriteEnd(packet, ref offset)) return false;
        length = (ushort)offset;
        return true;
    }

    internal static bool TryBuildRequest(Span<byte> packet, uint transactionId,
                                         ReadOnlySpan<byte> clientMac,
                                         uint requestedIp,
                                         uint serverIdentifier,
                                         out ushort length)
    {
        length = 0;
        if (!TryInitialize(packet, transactionId, clientMac)) return false;
        int offset = MinimumPacketLength;
        if (!TryWriteByteOption(packet, ref offset, OptionMessageType,
                                (byte)ManagedDhcpv4MessageType.Request) ||
            !TryWriteUInt32Option(packet, ref offset, OptionRequestedIp,
                                  requestedIp) ||
            !TryWriteUInt32Option(packet, ref offset, OptionServerIdentifier,
                                  serverIdentifier) ||
            !TryWriteParameterRequestList(packet, ref offset) ||
            !TryWriteEnd(packet, ref offset)) return false;
        length = (ushort)offset;
        return true;
    }

    private static bool TryInitialize(Span<byte> packet, uint transactionId,
                                      ReadOnlySpan<byte> clientMac)
    {
        if (packet.Length < MinimumPacketLength ||
            packet.Length > MaximumPacketLength ||
            clientMac.Length != HardwareAddressLength)
            return false;
        packet.Clear();
        packet[0] = BootRequest;
        packet[1] = HardwareTypeEthernet;
        packet[2] = HardwareAddressLength;
        ManagedEthernetProtocol.WriteUInt32Network(packet, 4, transactionId);
        ManagedEthernetProtocol.WriteUInt16Network(packet, 10, BroadcastFlag);
        clientMac.CopyTo(packet.Slice(28, HardwareAddressLength));
        ManagedEthernetProtocol.WriteUInt32Network(packet, FixedHeaderLength,
                                                   MagicCookie);
        return true;
    }

    private static bool TryWriteByteOption(Span<byte> packet, ref int offset,
                                            byte code, byte value)
    {
        if (offset > packet.Length - 3) return false;
        packet[offset++] = code;
        packet[offset++] = 1;
        packet[offset++] = value;
        return true;
    }

    private static bool TryWriteUInt32Option(Span<byte> packet, ref int offset,
                                             byte code, uint value)
    {
        if (offset > packet.Length - 6) return false;
        packet[offset++] = code;
        packet[offset++] = 4;
        ManagedEthernetProtocol.WriteUInt32Network(packet, offset, value);
        offset += 4;
        return true;
    }

    private static bool TryWriteParameterRequestList(Span<byte> packet,
                                                     ref int offset)
    {
        if (offset > packet.Length - 6) return false;
        packet[offset++] = OptionParameterRequestList;
        packet[offset++] = 4;
        packet[offset++] = OptionSubnetMask;
        packet[offset++] = OptionRouter;
        packet[offset++] = OptionDnsServer;
        packet[offset++] = OptionLeaseTime;
        return true;
    }

    private static bool TryWriteEnd(Span<byte> packet, ref int offset)
    {
        if (offset >= packet.Length) return false;
        packet[offset++] = OptionEnd;
        return true;
    }

    private static bool TryReadOption(byte code, ReadOnlySpan<byte> payload,
                                      Span<byte> parameterRequestList,
                                      ref ManagedDhcpv4OptionValues values)
    {
        switch (code)
        {
            case OptionSubnetMask:
                return TryReadUniqueUInt32(payload, ref values.HasSubnetMask,
                                           ref values.SubnetMask);
            case OptionRouter:
                if (payload.Length != 4) return false;
                return TryReadUniqueUInt32(payload, ref values.HasRouter,
                                           ref values.Router);
            case OptionDnsServer:
                if (payload.Length == 0 || (payload.Length & 3) != 0 ||
                    payload.Length > 8) return false;
                if (values.DnsCount != 0)
                {
                    if (payload.Length != values.DnsCount * 4) return false;
                    for (int index = 0; index < payload.Length; ++index)
                    {
                        byte expected = index < 4
                            ? (byte)(values.DnsServer1 >> (24 - index * 8))
                            : (byte)(values.DnsServer2 >> (24 - (index - 4) * 8));
                        if (payload[index] != expected) return false;
                    }
                    return true;
                }
                for (int offset = 0; offset < payload.Length; offset += 4)
                {
                    uint address = ManagedEthernetProtocol.ReadUInt32Network(
                        payload, offset);
                    if (values.DnsCount == 0)
                        values.DnsServer1 = address;
                    else if (values.DnsCount == 1)
                        values.DnsServer2 = address;
                    values.DnsCount++;
                }
                return true;
            case OptionRequestedIp:
                return TryReadUniqueUInt32(payload, ref values.HasRequestedIp,
                                           ref values.RequestedIp);
            case OptionLeaseTime:
                return TryReadUniqueUInt32(payload, ref values.HasLeaseTime,
                                           ref values.LeaseTime);
            case OptionMessageType:
                if (payload.Length != 1 || payload[0] < 1 || payload[0] > 6)
                    return false;
                ManagedDhcpv4MessageType messageType =
                    (ManagedDhcpv4MessageType)payload[0];
                if (values.HasMessageType && values.MessageType != messageType)
                    return false;
                values.HasMessageType = true;
                values.MessageType = messageType;
                return true;
            case OptionServerIdentifier:
                return TryReadUniqueUInt32(payload,
                                           ref values.HasServerIdentifier,
                                           ref values.ServerIdentifier);
            case OptionParameterRequestList:
                if (payload.Length == 0 || payload.Length > parameterRequestList.Length)
                    return false;
                if (values.ParameterRequestListLength != 0)
                {
                    if (values.ParameterRequestListLength != payload.Length)
                        return false;
                    for (int index = 0; index < payload.Length; ++index)
                        if (parameterRequestList[index] != payload[index]) return false;
                    return true;
                }
                payload.CopyTo(parameterRequestList);
                values.ParameterRequestListLength = (byte)payload.Length;
                return true;
            default:
                return true;
        }
    }

    private static bool TryReadUniqueUInt32(ReadOnlySpan<byte> payload,
                                            ref bool present, ref uint value)
    {
        if (payload.Length != 4) return false;
        uint next = ManagedEthernetProtocol.ReadUInt32Network(payload, 0);
        if (present && value != next) return false;
        present = true;
        value = next;
        return true;
    }
}

internal sealed class ManagedDhcpv4Client
{
    internal const int MaximumDiscoverAttempts = 3;
    internal const int MaximumRequestAttempts = 3;
    private static uint s_nextTransactionId = 0x19000001;

    private readonly byte[] _clientMac = new byte[6];
    private readonly byte[] _candidateIpv4 = new byte[4];
    private readonly byte[] _candidateMask = new byte[4];
    private readonly byte[] _candidateServer = new byte[4];
    private readonly byte[] _candidateRouter = new byte[4];
    private readonly byte[] _candidateDns1 = new byte[4];
    private readonly byte[] _candidateDns2 = new byte[4];
    private readonly byte[] _leasedIpv4 = new byte[4];
    private readonly byte[] _leasedMask = new byte[4];
    private readonly byte[] _leasedServer = new byte[4];
    private readonly byte[] _leasedRouter = new byte[4];
    private readonly byte[] _leasedDns1 = new byte[4];
    private readonly byte[] _leasedDns2 = new byte[4];
    private uint _transactionId;
    private uint _candidateLeaseTime;
    private uint _leasedLeaseTime;
    private bool _candidateHasRouter;
    private bool _candidateHasDns1;
    private bool _candidateHasDns2;
    private bool _leasedHasRouter;
    private bool _leasedHasDns1;
    private bool _leasedHasDns2;
    private int _discoverAttempts;
    private int _requestAttempts;

    internal ManagedDhcpv4State State { get; private set; } =
        ManagedDhcpv4State.Disabled;
    internal uint TransactionId => _transactionId;
    internal int DiscoverAttempts => _discoverAttempts;
    internal int RequestAttempts => _requestAttempts;
    internal bool HasCandidate => State == ManagedDhcpv4State.Requesting;
    internal bool HasLease => State == ManagedDhcpv4State.Bound;
    internal ReadOnlySpan<byte> LeasedIpv4 => _leasedIpv4;
    internal ReadOnlySpan<byte> LeasedMask => _leasedMask;
    internal ReadOnlySpan<byte> LeasedServerIdentifier => _leasedServer;
    internal ReadOnlySpan<byte> LeasedRouter => _leasedRouter;
    internal ReadOnlySpan<byte> LeasedDnsServer1 => _leasedDns1;
    internal ReadOnlySpan<byte> LeasedDnsServer2 => _leasedDns2;
    internal bool LeasedHasRouter => _leasedHasRouter;
    internal bool LeasedHasDnsServer1 => _leasedHasDns1;
    internal bool LeasedHasDnsServer2 => _leasedHasDns2;
    internal uint LeasedLeaseTime => _leasedLeaseTime;

    internal void Initialize(ReadOnlySpan<byte> clientMac)
    {
        if (clientMac.Length != _clientMac.Length) throw new ArgumentException();
        clientMac.CopyTo(_clientMac);
        ClearProtocolState();
        State = ManagedDhcpv4State.Init;
    }

    internal bool TryBuildDiscover(Span<byte> packet, out ushort length)
    {
        length = 0;
        if (State != ManagedDhcpv4State.Init ||
            _discoverAttempts >= MaximumDiscoverAttempts)
            return false;
        uint transactionId = s_nextTransactionId;
        if (transactionId == 0) transactionId = 1;
        if (!ManagedDhcpv4Protocol.TryBuildDiscover(
                packet, transactionId, _clientMac, out length))
            return false;
        _transactionId = transactionId;
        s_nextTransactionId = transactionId + 1;
        if (s_nextTransactionId == 0) s_nextTransactionId = 1;
        _discoverAttempts++;
        State = ManagedDhcpv4State.Selecting;
        return true;
    }

    internal ManagedDhcpv4ReceiveResult TryProcessResponse(
        ReadOnlySpan<byte> sourceIpv4, ReadOnlySpan<byte> payload,
        Span<byte> requestPacket, out ushort requestLength)
    {
        requestLength = 0;
        if (State != ManagedDhcpv4State.Selecting &&
            State != ManagedDhcpv4State.Requesting)
            return ManagedDhcpv4ReceiveResult.Ignored;
        if (sourceIpv4.Length != 4 ||
            !ManagedDhcpv4Protocol.TryParse(payload,
                                             out ManagedDhcpv4Packet packet))
            return ManagedDhcpv4ReceiveResult.Malformed;
        if (packet.Op != ManagedDhcpv4Protocol.BootReply ||
            packet.HardwareType != ManagedDhcpv4Protocol.HardwareTypeEthernet ||
            packet.HardwareLength != ManagedDhcpv4Protocol.HardwareAddressLength ||
            packet.TransactionId != _transactionId ||
            !packet.ClientHardwareAddress.Slice(0, 6).SequenceEqual(_clientMac) ||
            packet.Options.MessageType == 0 ||
            !packet.Options.HasMessageType)
            return ManagedDhcpv4ReceiveResult.Ignored;

        if (packet.Options.HasServerIdentifier &&
            ManagedEthernetProtocol.ReadUInt32Network(sourceIpv4, 0) !=
                packet.Options.ServerIdentifier)
            return ManagedDhcpv4ReceiveResult.Ignored;

        if (State == ManagedDhcpv4State.Selecting)
        {
            if (packet.Options.MessageType != ManagedDhcpv4MessageType.Offer ||
                !TryValidateOffer(packet))
                return ManagedDhcpv4ReceiveResult.Ignored;
            CopyCandidate(packet);
            if (_requestAttempts >= MaximumRequestAttempts ||
                !ManagedDhcpv4Protocol.TryBuildRequest(
                    requestPacket, _transactionId, _clientMac,
                    ManagedEthernetProtocol.ReadUInt32Network(_candidateIpv4, 0),
                    ManagedEthernetProtocol.ReadUInt32Network(_candidateServer, 0),
                    out requestLength))
            {
                ClearCandidate();
                State = ManagedDhcpv4State.Init;
                return ManagedDhcpv4ReceiveResult.Malformed;
            }
            _requestAttempts++;
            State = ManagedDhcpv4State.Requesting;
            return ManagedDhcpv4ReceiveResult.RequestReady;
        }

        if (packet.Options.MessageType == ManagedDhcpv4MessageType.Nak)
        {
            if (!packet.Options.HasServerIdentifier ||
                packet.Options.ServerIdentifier !=
                    ManagedEthernetProtocol.ReadUInt32Network(_candidateServer, 0))
                return ManagedDhcpv4ReceiveResult.Ignored;
            ClearCandidate();
            State = ManagedDhcpv4State.Init;
            return ManagedDhcpv4ReceiveResult.Nak;
        }
        if (packet.Options.MessageType != ManagedDhcpv4MessageType.Ack ||
            !TryValidateAck(packet))
            return ManagedDhcpv4ReceiveResult.Ignored;
        Commit(packet);
        return ManagedDhcpv4ReceiveResult.Bound;
    }

    internal bool TryRetry()
    {
        if (State != ManagedDhcpv4State.Selecting &&
            State != ManagedDhcpv4State.Requesting &&
            State != ManagedDhcpv4State.Init)
            return false;
        ClearCandidate();
        State = ManagedDhcpv4State.Init;
        return true;
    }

    internal void ResetForTeardown()
    {
        ClearProtocolState();
        State = ManagedDhcpv4State.Disabled;
    }

    private bool TryValidateOffer(ManagedDhcpv4Packet packet)
    {
        return ReadUsableIpv4(packet.YourIpv4) &&
               packet.Options.HasServerIdentifier &&
               packet.Options.ServerIdentifier != 0 &&
               packet.Options.ServerIdentifier != 0xFFFFFFFFU &&
               packet.Options.HasSubnetMask &&
               packet.Options.SubnetMask != 0 &&
               packet.Options.SubnetMask != 0xFFFFFFFFU;
    }

    private bool TryValidateAck(ManagedDhcpv4Packet packet)
    {
        return ReadUsableIpv4(packet.YourIpv4) &&
               packet.YourIpv4.SequenceEqual(_candidateIpv4) &&
               packet.Options.HasServerIdentifier &&
               packet.Options.ServerIdentifier ==
                   ManagedEthernetProtocol.ReadUInt32Network(_candidateServer, 0) &&
               packet.Options.HasSubnetMask &&
               packet.Options.SubnetMask ==
                   ManagedEthernetProtocol.ReadUInt32Network(_candidateMask, 0) &&
               packet.Options.HasLeaseTime && packet.Options.LeaseTime != 0;
    }

    private void CopyCandidate(ManagedDhcpv4Packet packet)
    {
        packet.YourIpv4.CopyTo(_candidateIpv4);
        WriteUInt32(_candidateMask, packet.Options.SubnetMask);
        WriteUInt32(_candidateServer, packet.Options.ServerIdentifier);
        _candidateLeaseTime = packet.Options.HasLeaseTime
            ? packet.Options.LeaseTime : 0;
        _candidateHasRouter = packet.Options.HasRouter;
        _candidateHasDns1 = packet.Options.DnsCount > 0;
        _candidateHasDns2 = packet.Options.DnsCount > 1;
        if (_candidateHasRouter) WriteUInt32(_candidateRouter, packet.Options.Router);
        if (_candidateHasDns1) WriteUInt32(_candidateDns1, packet.Options.DnsServer1);
        if (_candidateHasDns2) WriteUInt32(_candidateDns2, packet.Options.DnsServer2);
    }

    private void Commit(ManagedDhcpv4Packet packet)
    {
        packet.YourIpv4.CopyTo(_leasedIpv4);
        WriteUInt32(_leasedMask, packet.Options.SubnetMask);
        WriteUInt32(_leasedServer, packet.Options.ServerIdentifier);
        _leasedLeaseTime = packet.Options.LeaseTime;
        _leasedHasRouter = packet.Options.HasRouter;
        _leasedHasDns1 = packet.Options.DnsCount > 0;
        _leasedHasDns2 = packet.Options.DnsCount > 1;
        if (_leasedHasRouter) WriteUInt32(_leasedRouter, packet.Options.Router);
        if (_leasedHasDns1) WriteUInt32(_leasedDns1, packet.Options.DnsServer1);
        if (_leasedHasDns2) WriteUInt32(_leasedDns2, packet.Options.DnsServer2);
        ClearCandidate();
        State = ManagedDhcpv4State.Bound;
    }

    private void ClearProtocolState()
    {
        ClearCandidate();
        _leasedIpv4.AsSpan().Clear();
        _leasedMask.AsSpan().Clear();
        _leasedServer.AsSpan().Clear();
        _leasedRouter.AsSpan().Clear();
        _leasedDns1.AsSpan().Clear();
        _leasedDns2.AsSpan().Clear();
        _leasedLeaseTime = 0;
        _leasedHasRouter = false;
        _leasedHasDns1 = false;
        _leasedHasDns2 = false;
        _discoverAttempts = 0;
        _requestAttempts = 0;
        _transactionId = 0;
    }

    private void ClearCandidate()
    {
        _candidateIpv4.AsSpan().Clear();
        _candidateMask.AsSpan().Clear();
        _candidateServer.AsSpan().Clear();
        _candidateRouter.AsSpan().Clear();
        _candidateDns1.AsSpan().Clear();
        _candidateDns2.AsSpan().Clear();
        _candidateLeaseTime = 0;
        _candidateHasRouter = false;
        _candidateHasDns1 = false;
        _candidateHasDns2 = false;
    }

    private static bool ReadUsableIpv4(ReadOnlySpan<byte> address)
    {
        if (address.Length != 4) return false;
        uint value = ManagedEthernetProtocol.ReadUInt32Network(address, 0);
        return value != 0 && value != 0xFFFFFFFFU;
    }

    private static void WriteUInt32(Span<byte> destination, uint value)
    {
        ManagedEthernetProtocol.WriteUInt32Network(destination, 0, value);
    }
}

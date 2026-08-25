using System;

namespace GuideXOS.Net10.ManagedKernel;

internal sealed class ManagedEthernetLayer
{
    internal const uint ReceivePollLimit = 1000000000;
    private readonly ManagedE1000Driver _transport;
    private readonly ManagedArpLayer _arp;
    private readonly ManagedIpv4Layer _ipv4;
    private readonly byte[] _localMac;
    private readonly byte[] _broadcastMac;
    private readonly byte[] _txFrame;
    private readonly byte[] _rxFrame;
    private uint _unknownEtherTypeCount;
    private uint _malformedFrameCount;
    private bool _accepting = true;

    internal ManagedEthernetLayer(ManagedE1000Driver transport)
    {
        _transport = transport;
        _localMac = new byte[ManagedEthernetProtocol.MacLength];
        _broadcastMac = new byte[ManagedEthernetProtocol.MacLength];
        _broadcastMac.AsSpan().Fill(0xFF);
        _txFrame = new byte[ManagedEthernetProtocol.MaximumFrameLength];
        _rxFrame = new byte[ManagedEthernetProtocol.MaximumFrameLength];
        _arp = new ManagedArpLayer(this);
        _ipv4 = new ManagedIpv4Layer(this, _arp);
    }

    internal uint UnknownEtherTypeCount => _unknownEtherTypeCount;
    internal uint MalformedFrameCount => _malformedFrameCount;
    internal bool Phase16Passed => _arp.Phase16Passed;
    internal bool Phase17Passed => _ipv4.Phase17Passed;
    internal bool Phase18Passed => _ipv4.Phase18Passed;
    internal bool Phase19Passed => _ipv4.Phase19Passed;
    internal ReadOnlySpan<byte> LocalMac => _localMac;

    internal bool TryRunPhase16()
    {
        return _arp.TryRunPhase16();
    }

    internal bool TryRunPhase17()
    {
        return _arp.TryRunPhase16() && _ipv4.TryRunPhase17();
    }

    internal bool TryRunPhase18()
    {
        return _arp.TryRunPhase16() && _ipv4.TryRunPhase18();
    }

    internal bool TryRunPhase19()
    {
        return _ipv4.TryRunPhase19();
    }

    internal void InitializeMac()
    {
        uint macHigh = ManagedE1000Driver.Phase16MacHigh;
        uint macLow = ManagedE1000Driver.Phase16MacLow;
        _localMac[0] = (byte)(macHigh >> 8);
        _localMac[1] = (byte)macHigh;
        _localMac[2] = (byte)(macLow >> 24);
        _localMac[3] = (byte)(macLow >> 16);
        _localMac[4] = (byte)(macLow >> 8);
        _localMac[5] = (byte)macLow;
        _arp.InitializeMac();
        _ipv4.InitializeMac();
    }

    internal bool TryTransmit(ushort etherType, byte[] destination,
                              byte[] payload, int payloadLength)
    {
        if (!_accepting || destination == null || payload == null ||
            payloadLength < 0 || payloadLength > payload.Length) return false;
        if (ManagedEthernetProtocol.IsBroadcast(destination)) return false;
        Span<byte> frame = _txFrame;
        if (!ManagedEthernetProtocol.TryBuildFrame(
                frame, destination, _localMac, etherType,
                payload.AsSpan(0, payloadLength), out ushort frameLength))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_ETHERNET_TX_BUILD_FAILED\r\n"u8);
            return false;
        }
        return _transport.TryTransmitFrame(_txFrame, frameLength);
    }

    internal bool TryTransmitBroadcast(ushort etherType, byte[] payload,
                                        int payloadLength)
    {
        if (!_accepting || payload == null || payloadLength < 0 ||
            payloadLength > payload.Length) return false;
        if (!ManagedEthernetProtocol.TryBuildFrame(
                _txFrame, _broadcastMac, _localMac, etherType,
                payload.AsSpan(0, payloadLength), out ushort frameLength))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_ETHERNET_BROADCAST_TX_BUILD_FAILED\r\n"u8);
            return false;
        }
        return _transport.TryTransmitFrame(_txFrame, frameLength);
    }

    internal bool TryReceiveAndDispatch(ManagedArpLayer arp,
                                         out ManagedArpHandleResult result)
    {
        result = ManagedArpHandleResult.Invalid;
        if (!_accepting || arp == null) return false;

        if (!_transport.TryReceiveProtocolFrame(
                _rxFrame, _rxFrame.Length, ReceivePollLimit,
                out ushort frameLength))
            return false;
        if (frameLength == 0)
        {
            _malformedFrameCount++;
            return true;
        }
        ReadOnlySpan<byte> received = _rxFrame.AsSpan(0, frameLength);
        if (!ManagedEthernetProtocol.TryParseFrame(
                received, _localMac, out ManagedEthernetFrame parsed))
        {
            _malformedFrameCount++;
            return true;
        }

        if (parsed.EtherType != ManagedEthernetProtocol.ArpEtherType)
        {
            _unknownEtherTypeCount++;
            result = ManagedArpHandleResult.Ignored;
            return true;
        }

        result = arp.TryHandleEthernetArp(
            _rxFrame, 0, 6, ManagedEthernetProtocol.HeaderLength,
            parsed.Payload.Length);
        if (result == ManagedArpHandleResult.ReplySatisfied)
            _ipv4.TryReleasePendingAfterArp();
        return result != ManagedArpHandleResult.Failed;
    }

    internal bool TryReceiveAndDispatch(out ManagedNetworkDispatchResult result)
    {
        result = ManagedNetworkDispatchResult.Invalid;
        if (!_accepting) return false;
        if (!_transport.TryReceiveProtocolFrame(
                _rxFrame, _rxFrame.Length, ReceivePollLimit,
                out ushort frameLength))
            return false;
        if (frameLength == 0)
        {
            _malformedFrameCount++;
            result = ManagedNetworkDispatchResult.Malformed;
            return true;
        }
        ReadOnlySpan<byte> received = _rxFrame.AsSpan(0, frameLength);
        if (!ManagedEthernetProtocol.TryParseFrame(
                received, _localMac, out ManagedEthernetFrame parsed))
        {
            _malformedFrameCount++;
            result = ManagedNetworkDispatchResult.Malformed;
            return true;
        }

        if (parsed.EtherType == ManagedEthernetProtocol.ArpEtherType)
        {
            ManagedArpHandleResult arpResult = _arp.TryHandleEthernetArp(
                _rxFrame, 0, 6, ManagedEthernetProtocol.HeaderLength,
                parsed.Payload.Length);
            if (arpResult == ManagedArpHandleResult.ReplySatisfied)
            {
                _ipv4.TryReleasePendingAfterArp();
                result = ManagedNetworkDispatchResult.ArpReplySatisfied;
            }
            else if (arpResult == ManagedArpHandleResult.ResponderReplySent)
                result = ManagedNetworkDispatchResult.ArpResponderReplySent;
            else if (arpResult == ManagedArpHandleResult.Ignored)
                result = ManagedNetworkDispatchResult.Ignored;
            else
                result = ManagedNetworkDispatchResult.Malformed;
            return arpResult != ManagedArpHandleResult.Failed;
        }

        if (parsed.EtherType == ManagedIpv4Protocol.EtherType)
        {
            ManagedIpv4HandleResult ipv4Result = _ipv4.TryHandle(
                parsed.Payload);
            result = (ManagedNetworkDispatchResult)ipv4Result;
            return ipv4Result != ManagedIpv4HandleResult.Failed;
        }

        _unknownEtherTypeCount++;
        result = ManagedNetworkDispatchResult.Ignored;
        return true;
    }

    internal bool TryVerifyTransportAfterGc()
    {
        return _transport.TryVerifyProtocolGcSurvival();
    }

    internal bool TryStop()
    {
        _accepting = false;
        return _ipv4.TryStop() && _arp.TryStop();
    }
}

internal enum ManagedNetworkDispatchResult : byte
{
    Invalid = 0,
    Ignored = 1,
    ArpReplySatisfied = 2,
    ArpResponderReplySent = 3,
    IcmpEchoReplyValidated = 4,
    IcmpResponderReplySent = 5,
    Malformed = 6,
    Failed = 7,
    UdpEndpointResponseSent = 8,
    UdpResponseValidated = 9,
    UdpZeroChecksumAccepted = 10,
    DhcpRequestSent = 11,
    DhcpBound = 12,
    DhcpNak = 13
}

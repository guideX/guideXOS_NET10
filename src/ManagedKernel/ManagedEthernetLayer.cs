using System;

namespace GuideXOS.Net10.ManagedKernel;

internal sealed class ManagedEthernetLayer : IManagedArpEthernet
{
    internal const uint ReceivePollLimit = 1000000000;
    internal const uint Phase34ReceivePollLimit = 4096;
    internal const uint Phase34ClosingReceivePollLimit = 256;
    private readonly ManagedE1000Driver _transport;
    private readonly ManagedArpLayer _arp;
    private readonly ManagedIpv4Layer _ipv4;
    private readonly ManagedNetworkService _networkService;
    private readonly byte[] _localMac;
    private readonly byte[] _broadcastMac;
    private readonly byte[] _txFrame;
    private readonly byte[] _rxFrame;
    private uint _unknownEtherTypeCount;
    private uint _malformedFrameCount;
    private uint _receivedFrameCount;
    private uint _parsedFrameCount;
    private ushort _lastReceivedEtherType;
    private uint _unsupportedEtherTypeDropCount;
    private uint _phase84TestConsumerDeliveryCount;
    private uint _phase84TestConsumerFailureCount;
    private ManagedEthernetTestFrameMetadata _phase84LastMetadata;
    private bool _accepting = true;
    private bool _phase84TestConsumerEnabled;
    private bool _phase34Polling;
    private uint _phase34PollLimit = Phase34ReceivePollLimit;

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
        _networkService = new ManagedNetworkService(
            new ManagedNetworkServiceBackend(this, _ipv4));
        ((ManagedNetworkServiceBackend)_networkService.Backend)
            .AttachService(_networkService);
        _ipv4.AttachNetworkService(_networkService);
    }

    internal uint UnknownEtherTypeCount => _unknownEtherTypeCount;
    internal uint MalformedFrameCount => _malformedFrameCount;
    internal uint ReceivedFrameCount => _receivedFrameCount;
    internal uint ParsedFrameCount => _parsedFrameCount;
    internal ushort LastReceivedEtherType => _lastReceivedEtherType;
    internal uint UnsupportedEtherTypeDropCount => _unsupportedEtherTypeDropCount;
    internal uint Phase84TestConsumerDeliveryCount =>
        _phase84TestConsumerDeliveryCount;
    internal uint Phase84TestConsumerFailureCount =>
        _phase84TestConsumerFailureCount;
    internal ManagedEthernetTestFrameMetadata Phase84LastMetadata =>
        _phase84LastMetadata;
    internal Span<byte> DriverReceiveBuffer => _rxFrame;
    internal bool Phase16Passed => _arp.Phase16Passed;
    internal bool Phase17Passed => _ipv4.Phase17Passed;
    internal bool Phase18Passed => _ipv4.Phase18Passed;
    internal bool Phase19Passed => _ipv4.Phase19Passed;
    internal bool Phase20Passed => _ipv4.Phase20Passed;
    internal bool Phase21Passed => _ipv4.Phase21Passed;
    internal bool Phase22Passed => _ipv4.Phase22Passed;
    internal bool Phase23Passed => _ipv4.Phase23Passed;
    internal bool Phase32Passed => _ipv4.Phase32Passed;
    internal bool Phase33Passed => _ipv4.Phase33Passed;
    internal bool Phase34Passed => _ipv4.Phase34Passed;
    internal bool Phase35Passed => _ipv4.Phase35Passed;
    internal bool Phase39Passed => _ipv4.Phase39Passed;
    internal bool Phase40Passed => _ipv4.Phase40Passed;
    internal bool Phase41Passed => _ipv4.Phase41Passed;
    internal bool Phase42Passed => _ipv4.Phase42Passed;
    internal bool Phase43Passed => _ipv4.Phase43Passed;
    internal bool Phase44Passed => _ipv4.Phase44Passed;
    internal bool Phase45Passed => _ipv4.Phase45Passed;
    internal bool Phase46Passed => _ipv4.Phase46Passed;
    internal bool Phase48Passed => _ipv4.Phase48Passed;
    internal bool Phase49Passed => _ipv4.Phase49Passed;
    internal bool Phase50Passed => _ipv4.Phase50Passed;
    internal bool Phase51Passed => _ipv4.Phase51Passed;
    internal bool Phase51NegativeMimeControlPassed =>
        _ipv4.Phase51NegativeMimeControlPassed;
    internal bool Phase51ResetReusePassed => _ipv4.Phase51ResetReusePassed;
    internal bool Phase52Passed => _ipv4.Phase52Passed;
    internal bool Phase53Passed => _ipv4.Phase53Passed;

    internal void EnablePhase34Polling()
    {
        _phase34Polling = true;
        _phase34PollLimit = Phase34ReceivePollLimit;
    }

    internal void EnablePhase34ClosingPolling()
    {
        _phase34Polling = true;
        _phase34PollLimit = Phase34ClosingReceivePollLimit;
    }

    internal void EnablePhase34HandshakePolling()
    {
        _phase34Polling = true;
        _phase34PollLimit = Phase34ReceivePollLimit;
    }

    internal void EnablePhase84TestConsumer()
    {
        _phase84TestConsumerEnabled = true;
    }
    internal ReadOnlySpan<byte> LocalMac => _localMac;
    internal bool IsAccepting => _accepting;
    internal bool DriverReady => _transport.State == ManagedE1000DriverState.Running;
    internal ManagedNetworkService NetworkService => _networkService!;

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

    internal bool TryRunPhase20()
    {
        return _ipv4.TryRunPhase20();
    }

    internal bool TryRunPhase21()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase21();
    }

    internal bool TryRunPhase22()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase22();
    }

    internal bool TryRunPhase23()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase23();
    }

    internal bool TryRunPhase32()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase32();
    }

    internal bool TryRunPhase33()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase33();
    }

    internal bool TryRunPhase34()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase34();
    }

    internal bool TryRunPhase35()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase35();
    }

    internal bool TryRunPhase39()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase39();
    }

    internal bool TryRunPhase40()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase40();
    }

    internal bool TryRunPhase41()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase41();
    }

    internal bool TryRunPhase42()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase42();
    }

    internal bool TryRunPhase43(bool capacityControl = false)
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase43(capacityControl);
    }

    internal bool TryRunPhase44(bool capacityControl = false)
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase44(capacityControl);
    }

    internal bool TryRunPhase45(bool capacityControl = false)
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase45(capacityControl);
    }

    internal bool TryRunPhase46(bool capacityControl = false)
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase46(capacityControl);
    }

    internal bool TryRunPhase48(bool capacityControl = false)
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase48(capacityControl);
    }

    internal bool TryRunPhase49()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase49();
    }

    internal bool TryRunPhase50()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase50();
    }

    internal bool TryRunPhase51(bool resetReuse = false)
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase51(resetReuse);
    }

    internal bool TryRunPhase52()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase52();
    }

    internal bool TryRunPhase53()
    {
        ManagedNetworkServiceBackend runtime =
            (ManagedNetworkServiceBackend)_networkService!.Backend;
        runtime.Rebind(this, _ipv4);
        return _ipv4.TryRunPhase53();
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

    internal bool TryTransmit(ushort etherType, ReadOnlySpan<byte> destination,
                              byte[] payload, int payloadLength)
    {
        if (!_accepting || destination.Length != ManagedEthernetProtocol.MacLength ||
            payload == null ||
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

    bool IManagedArpEthernet.TryTransmit(ushort etherType, byte[] destination,
                                         byte[] payload, int payloadLength) =>
        TryTransmit(etherType, destination, payload, payloadLength);

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

    bool IManagedArpEthernet.TryTransmitBroadcast(ushort etherType,
        byte[] payload, int payloadLength) =>
        TryTransmitBroadcast(etherType, payload, payloadLength);

    internal bool TryReceiveAndDispatch(ManagedArpLayer arp,
                                         out ManagedArpHandleResult result)
    {
        result = ManagedArpHandleResult.Invalid;
        if (!_accepting || arp == null) return false;

        if (!_transport.TryReceiveProtocolFrame(
                _rxFrame, _rxFrame.Length,
                _phase34Polling ? _phase34PollLimit : ReceivePollLimit,
                out ushort frameLength))
            return false;
        if (frameLength == 0)
        {
            _malformedFrameCount++;
            return true;
        }
        ManagedEthernetReceiveResult dispatch = TryDispatchFrame(
            _rxFrame.AsSpan(0, frameLength), arp,
            out ManagedNetworkDispatchResult _, out result,
            out ManagedArpReceiveMetadata _);
        return dispatch != ManagedEthernetReceiveResult.DispatchFailed &&
               dispatch != ManagedEthernetReceiveResult.DriverError;
    }

    bool IManagedArpEthernet.TryReceiveAndDispatch(
        ManagedArpLayer arp, out ManagedArpHandleResult result) =>
        TryReceiveAndDispatch(arp, out result);

    internal bool TryReceiveAndDispatch(out ManagedNetworkDispatchResult result)
    {
        result = ManagedNetworkDispatchResult.Invalid;
        if (!_accepting) return false;
        if (!_transport.TryReceiveProtocolFrame(
                _rxFrame, _rxFrame.Length,
                _phase34Polling ? _phase34PollLimit : ReceivePollLimit,
                out ushort frameLength))
            return false;
        if (frameLength == 0)
        {
            _malformedFrameCount++;
            result = ManagedNetworkDispatchResult.Malformed;
            return true;
        }
        ManagedEthernetReceiveResult dispatch = TryDispatchFrame(
            _rxFrame.AsSpan(0, frameLength), _arp,
            out result, out ManagedArpHandleResult _,
            out ManagedArpReceiveMetadata _);
        return dispatch != ManagedEthernetReceiveResult.DispatchFailed &&
               dispatch != ManagedEthernetReceiveResult.DriverError;
    }

    /* Called by the E1000 driver while the completed descriptor is still
       software-owned.  The driver has copied bytes into this layer's fixed
       buffer; all spans below are consumed synchronously and are not retained. */
    internal ManagedEthernetReceiveResult ProcessDriverReceivedFrame(
        ushort frameLength)
    {
        return ProcessDriverReceivedFrame(frameLength,
            out ManagedNetworkDispatchResult _,
            out ManagedArpHandleResult _, out ManagedArpReceiveMetadata _);
    }

    internal ManagedEthernetReceiveResult ProcessDriverReceivedFrame(
        ushort frameLength, out ManagedNetworkDispatchResult networkResult,
        out ManagedArpHandleResult arpResult,
        out ManagedArpReceiveMetadata arpMetadata)
    {
        networkResult = ManagedNetworkDispatchResult.Invalid;
        arpResult = ManagedArpHandleResult.Invalid;
        arpMetadata = default;
        if (!_accepting || frameLength > _rxFrame.Length)
            return ManagedEthernetReceiveResult.DriverError;
        if (frameLength == 0)
        {
            _malformedFrameCount++;
            return ManagedEthernetReceiveResult.MalformedFrame;
        }
        return TryDispatchFrame(_rxFrame.AsSpan(0, frameLength), _arp,
                                out networkResult, out arpResult,
                                out arpMetadata);
    }

    internal void RecordDriverRejectedFrame()
    {
        _malformedFrameCount++;
    }

    internal ManagedEthernetReceiveResult ProcessReceivedFrameForHostTest(
        ReadOnlySpan<byte> frame)
    {
        if (frame.Length > _rxFrame.Length)
        {
            _malformedFrameCount++;
            return ManagedEthernetReceiveResult.MalformedFrame;
        }
        frame.CopyTo(_rxFrame);
        return ProcessDriverReceivedFrame((ushort)frame.Length);
    }

    internal bool TryEnablePhase85ArpAcceptance()
    {
        return _arp.TryInstallLocalIpv4(_arp.LocalIpv4);
    }

    internal bool TryEnablePhase86Ipv4Acceptance()
    {
        if (!_arp.TryInstallLocalIpv4(_arp.LocalIpv4)) return false;
        _ipv4.EnablePhase86AcceptanceObserver();
        return true;
    }

    internal bool TryEnablePhase87IcmpAcceptance()
    {
        if (!_arp.TryInstallLocalIpv4(_arp.LocalIpv4)) return false;
        _ipv4.EnablePhase87IcmpAcceptance();
        return true;
    }

    internal uint Phase86Ipv4ParsedFrameCount =>
        _ipv4.Phase86ParsedFrameCount;
    internal uint Phase86Ipv4LocalDeliveryCount =>
        _ipv4.Phase86LocalDeliveryCount;
    internal uint Phase86Ipv4NotForUsCount => _ipv4.Phase86NotForUsCount;
    internal uint Phase86UnsupportedProtocolCount =>
        _ipv4.UnsupportedProtocolCount;
    internal ManagedIpv4Phase86Metadata Phase86Ipv4LastMetadata =>
        _ipv4.Phase86LastMetadata;
    internal uint Phase87IcmpEchoRequestCount => _ipv4.Phase87EchoRequestCount;
    internal ManagedIpv4Phase87Metadata Phase87IcmpLastMetadata =>
        _ipv4.Phase87LastMetadata;

    internal uint LocalIpv4Value =>
        ManagedEthernetProtocol.ReadUInt32Network(_arp.LocalIpv4, 0);

    private ManagedEthernetReceiveResult TryDispatchFrame(
        ReadOnlySpan<byte> frame, ManagedArpLayer arp,
        out ManagedNetworkDispatchResult networkResult,
        out ManagedArpHandleResult arpResult,
        out ManagedArpReceiveMetadata arpMetadata)
    {
        networkResult = ManagedNetworkDispatchResult.Invalid;
        arpResult = ManagedArpHandleResult.Invalid;
        arpMetadata = default;
        _receivedFrameCount++;
        if (frame.Length < ManagedEthernetProtocol.HeaderLength ||
            frame.Length > ManagedEthernetProtocol.MaximumFrameLength ||
            !ManagedEthernetProtocol.TryParseFrame(
                frame, _localMac, out ManagedEthernetFrame parsed))
        {
            _malformedFrameCount++;
            networkResult = ManagedNetworkDispatchResult.Malformed;
            return ManagedEthernetReceiveResult.MalformedFrame;
        }
        _parsedFrameCount++;
        _lastReceivedEtherType = parsed.EtherType;
        ManagedEthernetDispatchKind dispatchKind =
            ManagedEthernetProtocol.ClassifyEtherType(
                parsed.EtherType, _phase84TestConsumerEnabled);

        if (dispatchKind == ManagedEthernetDispatchKind.Phase84TestConsumer)
        {
            uint expectedSequence = _phase84TestConsumerDeliveryCount + 1U;
            if (!ManagedEthernetPhase84TestConsumer.TryConsume(
                    in parsed, expectedSequence,
                    out ManagedEthernetTestFrameMetadata metadata))
            {
                _phase84TestConsumerFailureCount++;
                return ManagedEthernetReceiveResult.ConsumerRejected;
            }
            _phase84LastMetadata = metadata;
            _phase84TestConsumerDeliveryCount++;
            if (!LogPhase84Metadata(metadata))
                return ManagedEthernetReceiveResult.DispatchFailed;
            return ManagedEthernetReceiveResult.Consumed;
        }

        if (dispatchKind == ManagedEthernetDispatchKind.Arp)
        {
            arpResult = arp.TryHandleEthernetArp(
                in parsed, out arpMetadata);
            if (arpResult == ManagedArpHandleResult.ReplySatisfied)
            {
                _ipv4.TryReleasePendingAfterArp();
                networkResult = ManagedNetworkDispatchResult.ArpReplySatisfied;
                return ManagedEthernetReceiveResult.Consumed;
            }
            if (arpResult == ManagedArpHandleResult.ResponderReplySent)
            {
                networkResult = ManagedNetworkDispatchResult.ArpResponderReplySent;
                return ManagedEthernetReceiveResult.Consumed;
            }
            if (arpResult == ManagedArpHandleResult.Ignored)
            {
                networkResult = ManagedNetworkDispatchResult.Ignored;
                return ManagedEthernetReceiveResult.Consumed;
            }
            if (arpResult == ManagedArpHandleResult.RequestNotForUs)
            {
                networkResult = ManagedNetworkDispatchResult.Ignored;
                return ManagedEthernetReceiveResult.Consumed;
            }
            networkResult = arpResult == ManagedArpHandleResult.Failed
                ? ManagedNetworkDispatchResult.Failed
                : ManagedNetworkDispatchResult.Malformed;
            return arpResult == ManagedArpHandleResult.Failed
                ? ManagedEthernetReceiveResult.DispatchFailed
                : ManagedEthernetReceiveResult.MalformedFrame;
        }

        if (dispatchKind == ManagedEthernetDispatchKind.Ipv4)
        {
            ManagedIpv4HandleResult ipv4Result = _ipv4.TryHandle(
                parsed.Payload, parsed.Source);
            networkResult = (ManagedNetworkDispatchResult)ipv4Result;
            return ipv4Result == ManagedIpv4HandleResult.Failed
                ? ManagedEthernetReceiveResult.DispatchFailed
                : ManagedEthernetReceiveResult.Consumed;
        }

        _unknownEtherTypeCount++;
        _unsupportedEtherTypeDropCount++;
        networkResult = ManagedNetworkDispatchResult.Ignored;
        return ManagedEthernetReceiveResult.UnsupportedEtherType;
    }

    private static bool LogPhase84Metadata(
        ManagedEthernetTestFrameMetadata metadata)
    {
        return KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_ETHERNET_DESTINATION=0x"u8,
                   metadata.DestinationMac) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_ETHERNET_SOURCE=0x"u8,
                   metadata.SourceMac) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_ETHERNET_ETHERTYPE=0x"u8,
                   metadata.EtherType) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_ETHERNET_PAYLOAD_LENGTH=0x"u8,
                   metadata.PayloadLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_ETHERNET_SENTINEL=0x"u8,
                   metadata.Sentinel) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE84_ETHERTYPE_CONSUMED=1\r\n"u8);
    }

    internal bool TryVerifyTransportAfterGc()
    {
        return _transport.TryVerifyProtocolGcSurvival();
    }

    bool IManagedArpEthernet.TryVerifyTransportAfterGc() =>
        TryVerifyTransportAfterGc();

    internal bool TryStop()
    {
        _accepting = false;
        ManagedNetworkService service =
            ManagedNetworkServiceBackend.LiveService ?? _networkService;
        ManagedIpv4Layer ipv4 =
            ManagedNetworkServiceBackend.LiveIpv4 ?? _ipv4;
        service.OnProtocolTeardown();
        return ipv4.TryStop() && _arp.TryStop();
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
    UdpServiceReceived = 19,
    UdpReceiveOverflow = 20,
    TcpEstablished = 21,
    TcpDataAcknowledged = 22,
    TcpDataReceived = 23,
    TcpDuplicateData = 24,
    TcpOutOfOrder = 25,
    TcpRstReceived = 26,
    TcpFinReceived = 27,
    TcpReceiveUnavailable = 28,
    DhcpRequestSent = 11,
    DhcpBound = 12,
    DhcpNak = 13,
    DnsResponseIgnored = 14,
    DnsResponseMalformed = 15,
    DnsResponseTruncated = 16,
    DnsNxDomain = 17,
    DnsResolved = 18,
    IcmpUnsupported = 29
}

internal enum ManagedEthernetReceiveResult : byte
{
    Consumed = 0,
    UnsupportedEtherType = 1,
    MalformedFrame = 2,
    ConsumerRejected = 3,
    DispatchFailed = 4,
    DriverError = 5
}

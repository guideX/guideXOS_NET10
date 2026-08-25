using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedIpv4HandleResult : byte
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

internal sealed class ManagedIpv4Layer
{
    private const int MaximumProtocolFrames = 16;
    private const int MalformedControlFrames = 5;
    private const ushort FirstIdentifier = 0x1701;
    private const ushort SecondIdentifier = 0x1702;
    private const ushort FirstSequence = 1;
    private const ushort SecondSequence = 2;
    internal const ushort Phase18LocalPort = 15180;
    internal const ushort Phase18PeerPort = 15181;
    internal const ushort DhcpClientPort = 68;
    internal const ushort DhcpServerPort = 67;

    private readonly ManagedEthernetLayer _ethernet;
    private readonly ManagedArpLayer _arp;
    private readonly byte[] _localIpv4 = new byte[4];
    private readonly byte[] _peerIpv4 = new byte[4];
    private readonly byte[] _subnetMask = { 255, 255, 255, 0 };
    private readonly byte[] _destinationMac = new byte[6];
    private readonly byte[] _pendingIpv4 = new byte[4];
    private readonly byte[] _txPacket =
        new byte[ManagedIpv4Protocol.MaximumPacketLength];
    private readonly byte[] _txIcmp = new byte[
        ManagedIcmpv4Protocol.HeaderLength +
        ManagedIcmpv4Protocol.MaximumEchoPayloadLength];
    private readonly byte[] _txUdp = new byte[ManagedUdpProtocol.MaximumDatagramLength];
    private readonly byte[] _dhcpPacket = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
    private readonly byte[] _managedUdpPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly byte[] _peerUdpAckPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly byte[] _peerUdpRequestPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly byte[] _managedUdpAckPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly ManagedUdpEndpointTable _udpEndpoints = new();
    private readonly ManagedDhcpv4Client _dhcp = new();
    private readonly byte[] _pingPayload = new byte[32];
    private readonly ManagedIpv4PendingTransmission _pending = new();
    private uint _localIpv4Value;
    private uint _peerIpv4Value;
    private uint _subnetMaskValue;
    private byte _pingPayloadLength;
    private byte _managedUdpPayloadLength;
    private byte _peerUdpAckPayloadLength;
    private byte _peerUdpRequestPayloadLength;
    private byte _managedUdpAckPayloadLength;
    private ushort _awaitedIdentifier;
    private ushort _awaitedSequence;
    private bool _active;
    private bool _awaitingReply;
    private bool _replyValidated;
    private bool _responderReplySent;
    private uint _malformedPacketCount;
    private uint _unsupportedProtocolCount;
    private uint _unsupportedOptionsCount;
    private uint _pendingOverflowCount;
    private bool _phase18Passed;
    private bool _phase19Passed;
    private uint _udpRxValidCount;
    private uint _udpRxMalformedCount;
    private uint _udpChecksumFailureCount;
    private uint _udpZeroChecksumAcceptedCount;
    private uint _udpUnknownPortCount;
    private uint _udpEndpointDispatchCount;
    private uint _udpTxCount;
    private uint _udpPendingRejectCount;
    private uint _udpManagedResponseCount;
    private uint _udpPeerResponseCount;

    internal ManagedIpv4Layer(ManagedEthernetLayer ethernet,
                              ManagedArpLayer arp)
    {
        _ethernet = ethernet;
        _arp = arp;
        arp.LocalIpv4.AsSpan().CopyTo(_localIpv4);
        arp.HostIpv4.AsSpan().CopyTo(_peerIpv4);
        _localIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_localIpv4, 0);
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        _subnetMaskValue = ManagedEthernetProtocol.ReadUInt32Network(_subnetMask, 0);
        ReadOnlySpan<byte> payload = "guideXOS Phase17 ping payload"u8;
        payload.CopyTo(_pingPayload);
        _pingPayloadLength = (byte)payload.Length;
        ReadOnlySpan<byte> managedUdpPayload = "PHASE18-MANAGED-HELLO"u8;
        managedUdpPayload.CopyTo(_managedUdpPayload);
        _managedUdpPayloadLength = (byte)managedUdpPayload.Length;
        ReadOnlySpan<byte> peerUdpAckPayload = "PHASE18-PEER-ACK"u8;
        peerUdpAckPayload.CopyTo(_peerUdpAckPayload);
        _peerUdpAckPayloadLength = (byte)peerUdpAckPayload.Length;
        ReadOnlySpan<byte> peerUdpRequestPayload = "PHASE18-PEER-HELLO"u8;
        peerUdpRequestPayload.CopyTo(_peerUdpRequestPayload);
        _peerUdpRequestPayloadLength = (byte)peerUdpRequestPayload.Length;
        ReadOnlySpan<byte> managedUdpAckPayload = "PHASE18-MANAGED-ACK"u8;
        managedUdpAckPayload.CopyTo(_managedUdpAckPayload);
        _managedUdpAckPayloadLength = (byte)managedUdpAckPayload.Length;
    }

    internal void InitializeMac()
    {
        _dhcp.Initialize(_ethernet.LocalMac);
    }

    internal bool Phase17Passed { get; private set; }
    internal bool PendingTransmissionActive => _pending.IsActive;
    internal uint MalformedPacketCount => _malformedPacketCount;
    internal uint UnsupportedProtocolCount => _unsupportedProtocolCount;
    internal uint UnsupportedOptionsCount => _unsupportedOptionsCount;
    internal uint PendingOverflowCount => _pendingOverflowCount;
    internal bool ResponderReplySent => _responderReplySent;
    internal bool Phase18Passed => _phase18Passed;
    internal bool Phase19Passed => _phase19Passed;
    internal ManagedDhcpv4State DhcpState => _dhcp.State;
    internal uint DhcpTransactionId => _dhcp.TransactionId;
    internal ReadOnlySpan<byte> DhcpLeasedIpv4 => _dhcp.LeasedIpv4;
    internal uint DhcpLeaseTime => _dhcp.LeasedLeaseTime;
    internal ManagedUdpEndpointTable UdpEndpoints => _udpEndpoints;
    internal uint UdpRxValidCount => _udpRxValidCount;
    internal uint UdpRxMalformedCount => _udpRxMalformedCount;
    internal uint UdpChecksumFailureCount => _udpChecksumFailureCount;
    internal uint UdpZeroChecksumAcceptedCount => _udpZeroChecksumAcceptedCount;
    internal uint UdpUnknownPortCount => _udpUnknownPortCount;
    internal uint UdpEndpointDispatchCount => _udpEndpointDispatchCount;
    internal uint UdpTxCount => _udpTxCount;
    internal uint UdpPendingRejectCount => _udpPendingRejectCount;

    internal bool TryRunPhase17()
    {
        if (_active || _arp.Cache.Count == 0) return false;
        _active = true;
        return TryRunPhase17Core();
    }

    private bool TryRunPhase17Core()
    {
        if (!KernelLog.Write("GXOS_NET10:MANAGED_IPV4_READY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMPV4_READY\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_IPV4_LOCAL=0x"u8,
                                    _localIpv4Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_IPV4_PEER=0x"u8,
                                    _peerIpv4Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_IPV4_MASK=0x"u8,
                                    _subnetMaskValue) ||
            !TrySendPing(FirstIdentifier, FirstSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_FIRST_PING_SENT\r\n"u8) ||
            !WaitForReply(FirstIdentifier, FirstSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_FIRST_EXCHANGE_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_MALFORMED_READY\r\n"u8) ||
            !ConsumeMalformedControls() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_MALFORMED_CONTROLS_PASS\r\n"u8) ||
            !WaitForResponderRequest() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMP_RESPONDER_PASS\r\n"u8) ||
            !_ethernet.TryVerifyTransportAfterGc() ||
            _pending.IsActive ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE17_GC_SURVIVAL_PASSED\r\n"u8) ||
            !TrySendPing(SecondIdentifier, SecondSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_POST_GC_PING_SENT\r\n"u8) ||
            !WaitForReply(SecondIdentifier, SecondSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_POST_GC_EXCHANGE_PASS\r\n"u8))
            return false;

        Phase17Passed = true;
        return true;
    }

    internal bool TryRunPhase18()
    {
        if (_phase18Passed || _arp.Cache.Count == 0 ||
            !TryRunPhase17() ||
            !TryRunUdpCore())
            return false;

        _phase18Passed = true;
        return true;
    }

    internal bool TryRunPhase19()
    {
        if (_phase19Passed || _active || !_arp.TryBeginDhcp()) return false;
        _active = true;
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                        ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_DHCP_READY\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_CLIENT_PORT=0x"u8,
                                    DhcpClientPort) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_SERVER_PORT=0x"u8,
                                    DhcpServerPort) ||
            !TryRunDhcpDora() ||
            !_arp.TryRunPhase16() ||
            !TryRunPhase17Core() ||
            !TryRunUdpCore())
            return false;

        _phase19Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DHCP_PHASE19_PASS\r\n"u8);
    }

    private bool TryRunUdpCore()
    {
        if (!_udpEndpoints.TryRegister(Phase18LocalPort,
                                        ManagedUdpEndpointHandler.Phase18Echo) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_UDP_READY\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_UDP_LOCAL_PORT=0x"u8,
                                    Phase18LocalPort) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_UDP_PEER_PORT=0x"u8,
                                    Phase18PeerPort))
            return false;

        if (!TrySendUdpDatagram(Phase18LocalPort, Phase18PeerPort,
                                _managedUdpPayload.AsSpan(0, _managedUdpPayloadLength),
                                _peerIpv4, out _))
            return false;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_UDP_MANAGED_REQUEST_SENT\r\n"u8) ||
            !WaitForUdpResponse(1) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_UDP_MANAGED_EXCHANGE_PASS\r\n"u8) ||
            !WaitForUdpEndpointResponse(1) ||
            !WaitForUdpEndpointResponse(2) ||
            !ConsumeUdpMalformedControls() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_UDP_MALFORMED_CONTROLS_PASS\r\n"u8) ||
            !WaitForUdpEndpointResponse(3) ||
            !_ethernet.TryVerifyTransportAfterGc() ||
            !_udpEndpoints.TryLookup(Phase18LocalPort, out ManagedUdpEndpointHandler handler) ||
            handler != ManagedUdpEndpointHandler.Phase18Echo ||
            !KernelLog.Write("GXOS_NET10:MANAGED_UDP_GC_SURVIVAL_PASSED\r\n"u8))
            return false;

        if (!TrySendUdpDatagram(Phase18LocalPort, Phase18PeerPort,
                                _managedUdpPayload.AsSpan(0, _managedUdpPayloadLength),
                                _peerIpv4, out _) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_UDP_POST_GC_REQUEST_SENT\r\n"u8) ||
            !WaitForUdpResponse(2) ||
            !WaitForUdpEndpointResponse(4) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_UDP_POST_GC_EXCHANGE_PASS\r\n"u8))
            return false;

        return true;
    }

    private bool TryRunDhcpDora()
    {
        for (int attempt = 0; attempt != ManagedDhcpv4Client.MaximumDiscoverAttempts;
             ++attempt)
        {
            if (!_dhcp.TryBuildDiscover(_dhcpPacket, out ushort discoverLength) ||
                !TrySendDhcpPacket(_dhcpPacket.AsSpan(0, discoverLength)) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_DHCP_DISCOVER_SENT\r\n"u8))
                return false;
            bool completed = false;
            for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
            {
                if (!_ethernet.TryReceiveAndDispatch(
                        out ManagedNetworkDispatchResult result))
                    break;
                if (_dhcp.State == ManagedDhcpv4State.Bound)
                {
                    completed = true;
                    break;
                }
                if (result == ManagedNetworkDispatchResult.Failed) return false;
            }
            if (completed)
            {
                if (!ApplyDhcpLease() ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_DHCP_ACK_ACCEPTED\r\n"u8) ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_DHCP_BOUND\r\n"u8) ||
                    !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_LEASED_IPV4=0x"u8,
                                            _localIpv4Value) ||
                    !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_SUBNET_MASK=0x"u8,
                                            _subnetMaskValue) ||
                    !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_LEASE_TIME=0x"u8,
                                            _dhcp.LeasedLeaseTime))
                    return false;
                return true;
            }
            if (!_dhcp.TryRetry()) return false;
            if (!KernelLog.Write("GXOS_NET10:MANAGED_DHCP_RETRY\r\n"u8))
                return false;
        }
        _dhcp.TryRetry();
        return KernelLog.Write("GXOS_NET10:MANAGED_DHCP_FAILED\r\n"u8);
    }

    private bool ApplyDhcpLease()
    {
        if (!_dhcp.HasLease)
            return false;
        _dhcp.LeasedIpv4.CopyTo(_localIpv4);
        _dhcp.LeasedMask.CopyTo(_subnetMask);
        _dhcp.LeasedServerIdentifier.CopyTo(_peerIpv4);
        _localIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_localIpv4, 0);
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        _subnetMaskValue = ManagedEthernetProtocol.ReadUInt32Network(_subnetMask, 0);
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, _peerIpv4Value))
            return false;
        return _arp.TryInstallLocalIpv4(_localIpv4);
    }

    private bool TrySendDhcpPacket(ReadOnlySpan<byte> dhcpPacket)
    {
        if (dhcpPacket.Length < ManagedDhcpv4Protocol.MinimumPacketLength ||
            dhcpPacket.Length > ManagedDhcpv4Protocol.MaximumPacketLength)
            return false;
        Span<byte> zeroAddress = stackalloc byte[4];
        Span<byte> broadcastAddress = stackalloc byte[4];
        broadcastAddress.Fill(0xFF);
        if (!ManagedUdpProtocol.TryBuild(
                _txUdp, DhcpClientPort, DhcpServerPort, zeroAddress,
                broadcastAddress, dhcpPacket, out ushort udpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, (ushort)(0x1D00 + _dhcp.DiscoverAttempts), 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedUdpProtocol.Protocol,
                zeroAddress, broadcastAddress, _txUdp.AsSpan(0, udpLength),
                out ushort packetLength))
            return false;
        return _ethernet.TryTransmitBroadcast(ManagedIpv4Protocol.EtherType,
                                               _txPacket, packetLength);
    }

    internal ManagedIpv4HandleResult TryHandle(ReadOnlySpan<byte> packet)
    {
        if (!_active) return ManagedIpv4HandleResult.Failed;
        if (packet.Length >= 1 && (packet[0] & 0x0F) >
            ManagedIpv4Protocol.SupportedHeaderWords)
            _unsupportedOptionsCount++;
        bool allowDhcpBroadcast = !_dhcp.HasLease &&
            packet.Length >= ManagedIpv4Protocol.MinimumHeaderLength &&
            packet[9] == ManagedUdpProtocol.Protocol &&
            ManagedEthernetProtocol.ReadUInt32Network(packet, 16) == 0xFFFFFFFFU;
        if (!ManagedIpv4Protocol.TryParse(packet, _localIpv4Value,
                                          allowDhcpBroadcast,
                                          out ManagedIpv4Packet parsed))
        {
            _malformedPacketCount++;
            return ManagedIpv4HandleResult.Malformed;
        }
        if (parsed.Protocol != ManagedIpv4Protocol.IcmpProtocol)
        {
            if (parsed.Protocol == ManagedUdpProtocol.Protocol)
                return TryHandleUdp(parsed);
            _unsupportedProtocolCount++;
            return ManagedIpv4HandleResult.Ignored;
        }
        if (!ManagedIcmpv4Protocol.TryParse(parsed.Payload,
                                            out ManagedIcmpv4Packet icmp))
        {
            _malformedPacketCount++;
            return ManagedIpv4HandleResult.Malformed;
        }
        if (icmp.Type == ManagedIcmpv4Protocol.EchoReply)
            return TryHandleEchoReply(parsed, icmp);
        return TryHandleEchoRequest(parsed, icmp);
    }

    internal bool TryReleasePendingAfterArp()
    {
        if (!_pending.IsActive) return true;
        if (!_arp.Cache.TryLookup(_pendingIpv4, _destinationMac))
            return false;
        if (!_pending.TryTake(_pendingIpv4, _txPacket, out ushort length))
            return false;
        return _ethernet.TryTransmit(ManagedIpv4Protocol.EtherType,
                                     _destinationMac, _txPacket, length);
    }

    internal bool TryStop()
    {
        _active = false;
        _awaitingReply = false;
        _replyValidated = false;
        _responderReplySent = false;
        _awaitedIdentifier = 0;
        _awaitedSequence = 0;
        _malformedPacketCount = 0;
        _unsupportedProtocolCount = 0;
        _unsupportedOptionsCount = 0;
        _pendingOverflowCount = 0;
        _pending.Clear();
        _pendingIpv4.AsSpan().Clear();
        _udpEndpoints.Clear();
        _udpRxValidCount = 0;
        _udpRxMalformedCount = 0;
        _udpChecksumFailureCount = 0;
        _udpZeroChecksumAcceptedCount = 0;
        _udpUnknownPortCount = 0;
        _udpEndpointDispatchCount = 0;
        _udpTxCount = 0;
        _udpPendingRejectCount = 0;
        _udpManagedResponseCount = 0;
        _udpPeerResponseCount = 0;
        _txUdp.AsSpan().Clear();
        _dhcpPacket.AsSpan().Clear();
        _dhcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _phase19Passed = false;
        return true;
    }

    private bool TrySendPing(ushort identifier, ushort sequence)
    {
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, _peerIpv4Value) ||
            !ManagedIcmpv4Protocol.TryBuildEchoRequest(
                _txIcmp, identifier, sequence,
                _pingPayload.AsSpan(0, _pingPayloadLength),
                out ushort icmpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, (ushort)(0x1700 + sequence), 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedIpv4Protocol.IcmpProtocol,
                _localIpv4, _peerIpv4, _txIcmp.AsSpan(0, icmpLength),
                out ushort packetLength) ||
            !TrySendPacket(_peerIpv4, _txPacket.AsSpan(0, packetLength)))
            return false;
        _awaitedIdentifier = identifier;
        _awaitedSequence = sequence;
        _awaitingReply = true;
        _replyValidated = false;
        return true;
    }

    private ManagedIpv4HandleResult TryHandleUdp(ManagedIpv4Packet packet)
    {
        if (!ManagedUdpProtocol.TryParse(
                packet.Payload, packet.SourceAddress, packet.DestinationAddress,
                out ManagedUdpDatagram datagram))
        {
            if (packet.Payload.Length >= ManagedUdpProtocol.HeaderLength)
            {
                ushort declaredLength = ManagedEthernetProtocol.ReadUInt16Network(
                    packet.Payload, 4);
                if (declaredLength >= ManagedUdpProtocol.HeaderLength &&
                    declaredLength <= packet.Payload.Length)
                {
                    ushort checksum = ManagedEthernetProtocol.ReadUInt16Network(
                        packet.Payload, 6);
                    if (checksum != 0 && ManagedUdpProtocol.ComputeChecksum(
                            packet.SourceAddress, packet.DestinationAddress,
                            packet.Payload.Slice(0, declaredLength)) != 0)
                        _udpChecksumFailureCount++;
                }
            }
            _udpRxMalformedCount++;
            return ManagedIpv4HandleResult.Malformed;
        }

        _udpRxValidCount++;
        if (datagram.Checksum == 0)
            _udpZeroChecksumAcceptedCount++;
        if (!_udpEndpoints.TryLookup(datagram.DestinationPort,
                                     out ManagedUdpEndpointHandler handler))
        {
            _udpUnknownPortCount++;
            return ManagedIpv4HandleResult.Ignored;
        }
        _udpEndpointDispatchCount++;
        if (handler == ManagedUdpEndpointHandler.Dhcpv4Client)
            return TryHandleDhcpUdp(packet, datagram);
        if (handler != ManagedUdpEndpointHandler.Phase18Echo)
            return ManagedIpv4HandleResult.Ignored;
        return TryHandlePhase18Udp(packet, datagram);
    }

    private ManagedIpv4HandleResult TryHandleDhcpUdp(
        ManagedIpv4Packet packet, ManagedUdpDatagram datagram)
    {
        if (_dhcp.HasLease || datagram.SourcePort != DhcpServerPort ||
            datagram.DestinationPort != DhcpClientPort ||
            ManagedEthernetProtocol.ReadUInt32Network(packet.DestinationAddress, 0) !=
                0xFFFFFFFFU)
            return ManagedIpv4HandleResult.Ignored;

        ManagedDhcpv4ReceiveResult response = _dhcp.TryProcessResponse(
            packet.SourceAddress, datagram.Payload, _dhcpPacket,
            out ushort requestLength);
        if (response == ManagedDhcpv4ReceiveResult.RequestReady)
        {
            if (!TrySendDhcpPacket(_dhcpPacket.AsSpan(0, requestLength)) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_DHCP_REQUEST_SENT\r\n"u8))
                return ManagedIpv4HandleResult.Failed;
            return ManagedIpv4HandleResult.DhcpRequestSent;
        }
        if (response == ManagedDhcpv4ReceiveResult.Bound)
        {
            if (!ApplyDhcpLease()) return ManagedIpv4HandleResult.Failed;
            return KernelLog.Write("GXOS_NET10:MANAGED_DHCP_ACK_RECEIVED\r\n"u8)
                ? ManagedIpv4HandleResult.DhcpBound
                : ManagedIpv4HandleResult.Failed;
        }
        if (response == ManagedDhcpv4ReceiveResult.Nak)
            return KernelLog.Write("GXOS_NET10:MANAGED_DHCP_NAK_RECEIVED\r\n"u8)
                ? ManagedIpv4HandleResult.DhcpNak
                : ManagedIpv4HandleResult.Failed;
        return response == ManagedDhcpv4ReceiveResult.Malformed
            ? ManagedIpv4HandleResult.Malformed
            : ManagedIpv4HandleResult.Ignored;
    }

    private ManagedIpv4HandleResult TryHandlePhase18Udp(
        ManagedIpv4Packet packet, ManagedUdpDatagram datagram)
    {
        uint sourceIpv4 = ManagedEthernetProtocol.ReadUInt32Network(
            packet.SourceAddress, 0);
        if (sourceIpv4 != _peerIpv4Value ||
            datagram.SourcePort != Phase18PeerPort ||
            datagram.DestinationPort != Phase18LocalPort)
            return ManagedIpv4HandleResult.Ignored;

        if (datagram.Payload.SequenceEqual(
                _peerUdpAckPayload.AsSpan(0, _peerUdpAckPayloadLength)))
        {
            _udpManagedResponseCount++;
            return KernelLog.Write(_udpManagedResponseCount == 1
                ? "GXOS_NET10:MANAGED_UDP_MANAGED_RESPONSE_VALID\r\n"u8
                : "GXOS_NET10:MANAGED_UDP_POST_GC_RESPONSE_VALID\r\n"u8)
                ? ManagedIpv4HandleResult.UdpResponseValidated
                : ManagedIpv4HandleResult.Failed;
        }

        if (!datagram.Payload.SequenceEqual(
                _peerUdpRequestPayload.AsSpan(0, _peerUdpRequestPayloadLength)))
            return ManagedIpv4HandleResult.Ignored;

        if (datagram.Checksum == 0 &&
            !KernelLog.Write("GXOS_NET10:MANAGED_UDP_ZERO_CHECKSUM_ACCEPTED\r\n"u8))
            return ManagedIpv4HandleResult.Failed;
        if (!TrySendUdpDatagram(Phase18LocalPort, Phase18PeerPort,
                                _managedUdpAckPayload.AsSpan(0,
                                    _managedUdpAckPayloadLength),
                                packet.SourceAddress, out _))
            return ManagedIpv4HandleResult.Failed;

        _udpPeerResponseCount++;
        ReadOnlySpan<byte> marker = _udpPeerResponseCount switch
        {
            1 => "GXOS_NET10:MANAGED_UDP_PEER_RESPONSE_SENT\r\n"u8,
            2 => "GXOS_NET10:MANAGED_UDP_ZERO_CHECKSUM_RESPONSE_SENT\r\n"u8,
            3 => "GXOS_NET10:MANAGED_UDP_POST_MALFORMED_RESPONSE_SENT\r\n"u8,
            _ => "GXOS_NET10:MANAGED_UDP_POST_GC_PEER_RESPONSE_SENT\r\n"u8
        };
        return KernelLog.Write(marker)
            ? ManagedIpv4HandleResult.UdpEndpointResponseSent
            : ManagedIpv4HandleResult.Failed;
    }

    private bool TrySendUdpDatagram(ushort sourcePort, ushort destinationPort,
                                    ReadOnlySpan<byte> payload,
                                    ReadOnlySpan<byte> destinationIpv4,
                                    out ushort packetLength)
    {
        packetLength = 0;
        if (_pending.IsActive)
        {
            _udpPendingRejectCount++;
            return false;
        }
        if (!ManagedUdpProtocol.TryBuild(
                _txUdp, sourcePort, destinationPort, _localIpv4,
                destinationIpv4, payload, out ushort udpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, (ushort)(0x1900 + _udpTxCount), 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedUdpProtocol.Protocol,
                _localIpv4, destinationIpv4, _txUdp.AsSpan(0, udpLength),
                out packetLength) ||
            !TrySendPacket(destinationIpv4,
                            _txPacket.AsSpan(0, packetLength)))
            return false;
        _udpTxCount++;
        return true;
    }

    private bool TrySendPacket(ReadOnlySpan<byte> destinationIpv4,
                               ReadOnlySpan<byte> packet)
    {
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue,
                ManagedEthernetProtocol.ReadUInt32Network(destinationIpv4, 0)))
            return false;
        if (_arp.Cache.TryLookup(destinationIpv4, _destinationMac))
            return _ethernet.TryTransmit(ManagedIpv4Protocol.EtherType,
                                         _destinationMac, _txPacket,
                                         packet.Length);
        if (!_pending.TryStage(destinationIpv4, packet))
        {
            _pendingOverflowCount++;
            KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_PENDING_OVERFLOW\r\n"u8);
            return false;
        }
        destinationIpv4.CopyTo(_pendingIpv4);
        if (!_arp.TryResolve(destinationIpv4) || _pending.IsActive)
        {
            _pending.Clear();
            return false;
        }
        return true;
    }

    private bool WaitForReply(ushort identifier, ushort sequence)
    {
        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result)) return false;
            if (result == ManagedNetworkDispatchResult.IcmpEchoReplyValidated &&
                !_awaitingReply && _replyValidated &&
                _awaitedIdentifier == identifier && _awaitedSequence == sequence)
                return KernelLog.Write(
                    sequence == FirstSequence
                        ? "GXOS_NET10:MANAGED_ICMP_FIRST_REPLY_VALID\r\n"u8
                        : "GXOS_NET10:MANAGED_ICMP_POST_GC_REPLY_VALID\r\n"u8);
        }
        return false;
    }

    private bool WaitForUdpResponse(uint expectedCount)
    {
        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result)) return false;
            if (result == ManagedNetworkDispatchResult.UdpResponseValidated &&
                _udpManagedResponseCount >= expectedCount)
                return true;
            if (result == ManagedNetworkDispatchResult.Failed) return false;
        }
        return false;
    }

    private bool WaitForUdpEndpointResponse(uint expectedCount)
    {
        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result)) return false;
            if (result == ManagedNetworkDispatchResult.UdpEndpointResponseSent &&
                _udpPeerResponseCount >= expectedCount)
                return true;
            if (result == ManagedNetworkDispatchResult.Failed) return false;
        }
        return false;
    }

    private bool ConsumeUdpMalformedControls()
    {
        for (int frame = 0; frame != 5; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result) ||
                (result != ManagedNetworkDispatchResult.Malformed &&
                 result != ManagedNetworkDispatchResult.Ignored))
                return false;
            if (!WriteUdpMalformedControlMarker(frame)) return false;
        }
        return true;
    }

    private static bool WriteUdpMalformedControlMarker(int frame)
    {
        return frame switch
        {
            0 => KernelLog.Write(
                "GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_0\r\n"u8),
            1 => KernelLog.Write(
                "GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_1\r\n"u8),
            2 => KernelLog.Write(
                "GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_2\r\n"u8),
            3 => KernelLog.Write(
                "GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_3\r\n"u8),
            4 => KernelLog.Write(
                "GXOS_NET10:MANAGED_UDP_MALFORMED_FRAME_4\r\n"u8),
            _ => false
        };
    }

    private bool ConsumeMalformedControls()
    {
        for (int frame = 0; frame != MalformedControlFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result) ||
                result != ManagedNetworkDispatchResult.Malformed)
                return false;
            if (!WriteMalformedControlMarker(frame)) return false;
        }
        return true;
    }

    private static bool WriteMalformedControlMarker(int frame)
    {
        return frame switch
        {
            0 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_0\r\n"u8),
            1 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_1\r\n"u8),
            2 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_2\r\n"u8),
            3 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_3\r\n"u8),
            4 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_4\r\n"u8),
            _ => false
        };
    }

    private bool WaitForResponderRequest()
    {
        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result)) return false;
            if (result == ManagedNetworkDispatchResult.IcmpResponderReplySent)
                return true;
        }
        return false;
    }

    private ManagedIpv4HandleResult TryHandleEchoReply(
        ManagedIpv4Packet packet, ManagedIcmpv4Packet icmp)
    {
        if (!_awaitingReply ||
            ManagedEthernetProtocol.ReadUInt32Network(packet.SourceAddress, 0) !=
                _peerIpv4Value ||
            ManagedEthernetProtocol.ReadUInt32Network(packet.DestinationAddress, 0) !=
                _localIpv4Value || icmp.Identifier != _awaitedIdentifier ||
            icmp.Sequence != _awaitedSequence ||
            !icmp.Payload.SequenceEqual(_pingPayload.AsSpan(0, _pingPayloadLength)))
            return ManagedIpv4HandleResult.Ignored;
        _awaitingReply = false;
        _replyValidated = true;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_IPV4_RX_ECHO_REPLY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMP_ECHO_REPLY_VALID\r\n"u8))
            return ManagedIpv4HandleResult.Failed;
        return ManagedIpv4HandleResult.IcmpEchoReplyValidated;
    }

    private ManagedIpv4HandleResult TryHandleEchoRequest(
        ManagedIpv4Packet packet, ManagedIcmpv4Packet icmp)
    {
        uint sourceIpv4 = ManagedEthernetProtocol.ReadUInt32Network(
            packet.SourceAddress, 0);
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, sourceIpv4))
            return ManagedIpv4HandleResult.Ignored;
        if (!ManagedIcmpv4Protocol.TryBuildEchoReply(
                _txIcmp, icmp.Identifier, icmp.Sequence, icmp.Payload,
                out ushort icmpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, (ushort)(0x1800 + icmp.Sequence), 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedIpv4Protocol.IcmpProtocol,
                _localIpv4, packet.SourceAddress,
                _txIcmp.AsSpan(0, icmpLength), out ushort packetLength) ||
            !TrySendPacket(packet.SourceAddress,
                            _txPacket.AsSpan(0, packetLength)))
            return ManagedIpv4HandleResult.Failed;
        _responderReplySent = true;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_IPV4_RX_ECHO_REQUEST\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMP_ECHO_REPLY_SENT\r\n"u8))
            return ManagedIpv4HandleResult.Failed;
        return ManagedIpv4HandleResult.IcmpResponderReplySent;
    }
}

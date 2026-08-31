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
    DhcpNak = 13,
    DnsResponseIgnored = 14,
    DnsResponseMalformed = 15,
    DnsResponseTruncated = 16,
    DnsNxDomain = 17,
    DnsResolved = 18,
    UdpServiceReceived = 19,
    UdpReceiveOverflow = 20,
    TcpEstablished = 21,
    TcpDataAcknowledged = 22,
    TcpDataReceived = 23,
    TcpDuplicateData = 24,
    TcpOutOfOrder = 25,
    TcpRstReceived = 26,
    TcpFinReceived = 27,
    TcpReceiveUnavailable = 28
}

internal sealed class ManagedIpv4Layer : IManagedTcpPacketSender
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
    internal const ushort DnsClientPort = ManagedDnsResolver.ClientPort;
    internal const ushort DnsServerPort = ManagedDnsResolver.ServerPort;
    internal static ReadOnlySpan<byte> Phase20QueryName => "phase20.test"u8;
    internal static ReadOnlySpan<byte> Phase20MissingQueryName =>
        "missing.phase20.test"u8;
    internal static ReadOnlySpan<byte> Phase22QueryName => "phase22.test"u8;
    internal static ReadOnlySpan<byte> Phase23QueryName => "phase23.test"u8;
    internal static ReadOnlySpan<byte> Phase32QueryName => "www.example.com"u8;

    private readonly ManagedEthernetLayer _ethernet;
    private readonly ManagedArpLayer _arp;
    private readonly byte[] _localIpv4 = new byte[4];
    private readonly byte[] _peerIpv4 = new byte[4];
    private readonly byte[] _gatewayIpv4 = new byte[4];
    private readonly byte[] _subnetMask = { 255, 255, 255, 0 };
    private readonly byte[] _destinationMac = new byte[6];
    private readonly byte[] _pendingIpv4 = new byte[4];
    private readonly byte[] _txPacket =
        new byte[ManagedIpv4Protocol.MaximumPacketLength];
    private readonly byte[] _txIcmp = new byte[
        ManagedIcmpv4Protocol.HeaderLength +
        ManagedIcmpv4Protocol.MaximumEchoPayloadLength];
    private readonly byte[] _txUdp = new byte[ManagedUdpProtocol.MaximumDatagramLength];
    private readonly byte[] _txTcp = new byte[
        ManagedTcpProtocol.HeaderLength + 4 + ManagedTcpProtocol.MaximumPayloadLength];
    private readonly byte[] _dhcpPacket = new byte[ManagedDhcpv4Protocol.MaximumPacketLength];
    private readonly byte[] _managedUdpPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly byte[] _peerUdpAckPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly byte[] _peerUdpRequestPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly byte[] _managedUdpAckPayload = new byte[ManagedUdpProtocol.MaximumPayloadLength];
    private readonly byte[] _dnsQuery = new byte[ManagedDnsProtocol.MaximumMessageLength];
    private readonly byte[] _awaitedDestinationIpv4 = new byte[4];
    private readonly ManagedUdpEndpointTable _udpEndpoints = new();
    private readonly ManagedDhcpv4Client _dhcp = new();
    private readonly ManagedDnsResolver _dns = new();
    private readonly byte[] _pingPayload = new byte[32];
    private readonly ManagedIpv4PendingTransmission _pending = new();
    private ManagedNetworkService? _networkService;
    private ManagedPhase21TestConsumer? _phase21Consumer;
    private ManagedPhase22TestConsumer? _phase22Consumer;
    private ManagedPhase23TestConsumer? _phase23Consumer;
    private ManagedPhase32TestConsumer? _phase32Consumer;
    private ManagedPhase33TestConsumer? _phase33Consumer;
    private ManagedPhase34TestConsumer? _phase34Consumer;
    private ManagedPhase35PublicHttpsConsumer? _phase35Consumer;
    private readonly ManagedTcpConnection _tcp;
    private uint _localIpv4Value;
    private uint _peerIpv4Value;
    private uint _gatewayIpv4Value;
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
    private bool _phase20Passed;
    private bool _phase21Passed;
    private bool _phase22Passed;
    private bool _phase23Passed;
    private bool _phase32Passed;
    private bool _phase33Passed;
    private bool _phase34Passed;
    private bool _phase35Passed;
    private uint _tcpGeneration;
    private uint _tcpRxValidCount;
    private uint _tcpRxMalformedCount;
    private uint _tcpChecksumFailureCount;
    private ushort _servicePingIdentifier = 0x2101;
    private ushort _servicePingSequence = 1;
    private uint _udpRxValidCount;
    private uint _udpRxMalformedCount;
    private uint _udpChecksumFailureCount;
    private uint _udpZeroChecksumAcceptedCount;
    private uint _udpUnknownPortCount;
    private uint _udpEndpointDispatchCount;
    private uint _udpTxCount;
    private uint _serviceUdpTxCount;
    private uint _udpPendingRejectCount;
    private uint _udpManagedResponseCount;
    private uint _udpPeerResponseCount;
    private uint _tcpTxCount;

    internal ManagedIpv4Layer(ManagedEthernetLayer ethernet,
                              ManagedArpLayer arp)
    {
        _ethernet = ethernet;
        _arp = arp;
        _tcp = new ManagedTcpConnection(this);
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
    internal bool Phase20Passed => _phase20Passed;
    internal bool Phase21Passed => _phase21Passed;
    internal bool Phase22Passed => _phase22Passed;
    internal bool Phase23Passed => _phase23Passed;
    internal bool Phase32Passed => _phase32Passed;
    internal bool Phase33Passed => _phase33Passed;
    internal bool Phase34Passed => _phase34Passed;
    internal bool Phase35Passed => _phase35Passed;
    internal ManagedTcpConnectionState TcpState => _tcp.State;
    internal bool TcpHasInFlight => _tcp.HasInFlight;
    internal uint TcpGeneration => _tcp.Generation;
    internal uint TcpLocalIsn => _tcp.LocalIsn;
    internal uint TcpPeerIsn => _tcp.PeerIsn;
    internal uint TcpSendNext => _tcp.SendNext;
    internal uint TcpReceiveNext => _tcp.ReceiveNext;
    internal uint TcpRxValidCount => _tcpRxValidCount;
    internal uint TcpRxMalformedCount => _tcpRxMalformedCount;
    internal uint TcpChecksumFailureCount => _tcpChecksumFailureCount;
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
    internal ManagedDnsResult DnsResult => _dns.Result;
    internal bool DnsHasServer => _dns.HasServer;
    internal bool DnsQueryActive => _dns.IsActive;
    internal ushort DnsTransactionId => _dns.TransactionId;
    internal ReadOnlySpan<byte> DnsServerIpv4 => _dns.ServerIpv4;
    internal ReadOnlySpan<byte> DnsResolvedIpv4 => _dns.ResolvedIpv4;
    internal uint LocalIpv4Value => _localIpv4Value;
    internal uint SubnetMaskValue => _subnetMaskValue;
    internal uint GatewayIpv4Value => _gatewayIpv4Value;
    internal uint DnsServerValue => _dns.HasServer
        ? ManagedEthernetProtocol.ReadUInt32Network(_dns.ServerIpv4, 0) : 0;
    internal uint DnsResolvedIpv4Value => _dns.HasResolvedAddress
        ? ManagedEthernetProtocol.ReadUInt32Network(_dns.ResolvedIpv4, 0) : 0;
    internal bool DnsHasResolvedAddress => _dns.HasResolvedAddress;
    internal bool ServicePingActive => _awaitingReply;

    internal void AttachNetworkService(ManagedNetworkService service)
    {
        _networkService = service;
    }

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
        _gatewayIpv4.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _gatewayIpv4Value = 0;
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

    internal bool TryRunPhase20()
    {
        if (_phase20Passed || _active || !_arp.TryBeginDhcp()) return false;
        _active = true;
        _dns.ResetForDhcp();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _gatewayIpv4.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _gatewayIpv4Value = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                        ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_DNS_DHCP_READY\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true) ||
            !_dns.HasServer ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                        ManagedUdpEndpointHandler.DnsResolver) ||
            !_udpEndpoints.TryRegister(Phase18LocalPort,
                                        ManagedUdpEndpointHandler.Phase18Echo) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_DNS_READY\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DNS_CLIENT_PORT=0x"u8,
                                    DnsClientPort) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DNS_SERVER_PORT=0x"u8,
                                    DnsServerPort) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_DNS_SERVER=0x"u8,
                                    ManagedEthernetProtocol.ReadUInt32Network(
                                        _dns.ServerIpv4, 0)) ||
            !TryResolveDns(Phase20QueryName, expectResolved: true) ||
            !TryRunResolvedTraffic(firstRun: true) ||
            !TryResolveDns(Phase20MissingQueryName, expectResolved: false) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_DNS_NXDOMAIN_PASS\r\n"u8) ||
            !TryResolveDns(Phase20QueryName, expectResolved: true) ||
            !_ethernet.TryVerifyTransportAfterGc() ||
            _dhcp.State != ManagedDhcpv4State.Bound || !_dns.HasServer ||
            !KernelLog.Write("GXOS_NET10:MANAGED_DNS_GC_SURVIVAL_PASSED\r\n"u8) ||
            !TryResolveDns(Phase20QueryName, expectResolved: true) ||
            !TryRunResolvedTraffic(firstRun: false))
            return false;

        _phase20Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DNS_PHASE20_PASS\r\n"u8);
    }

    internal bool TryRunPhase21()
    {
        if (_phase21Passed || _active || !_arp.TryBeginDhcp() ||
            _networkService == null) return false;
        _phase21Consumer ??= new ManagedPhase21TestConsumer(_networkService);
        _active = true;
        _networkService.BeginBoot();
        _dns.ResetForDhcp();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                       ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_READY\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true) ||
            !_udpEndpoints.TryUnregister(DhcpClientPort) ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                       ManagedUdpEndpointHandler.DnsResolver) ||
            !PublishNetworkServiceStatus() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_CONFIGURED\r\n"u8))
            return false;

        ManagedNetworkServiceBackend.SetLiveIpv4(this);
        if (_phase21Consumer == null || !_phase21Consumer.TryRun()) return false;
        _phase21Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DNS_PHASE21_PASS\r\n"u8);
    }

    internal bool TryRunPhase22()
    {
        if (_phase22Passed || _active || !_arp.TryBeginDhcp() ||
            _networkService == null)
            return false;
        _phase22Consumer ??= new ManagedPhase22TestConsumer(_networkService);
        _active = true;
        _networkService.BeginBoot();
        _dns.ResetForDhcp();
        _tcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                       ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_READY\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true) ||
            !_udpEndpoints.TryUnregister(DhcpClientPort) ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                       ManagedUdpEndpointHandler.DnsResolver) ||
            !PublishNetworkServiceStatus() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_CONFIGURED\r\n"u8))
            return false;

        ManagedNetworkServiceBackend.SetLiveIpv4(this);
        if (!_phase22Consumer.TryRun()) return false;
        _phase22Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DNS_PHASE22_PASS\r\n"u8);
    }

    internal bool TryRunPhase23()
    {
        if (_phase23Passed || _active || !_arp.TryBeginDhcp() ||
            _networkService == null)
            return false;
        _phase23Consumer ??= new ManagedPhase23TestConsumer(_networkService);
        _active = true;
        _networkService.BeginBoot();
        _dns.ResetForDhcp();
        _tcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                       ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_READY\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true) ||
            !_udpEndpoints.TryUnregister(DhcpClientPort) ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                       ManagedUdpEndpointHandler.DnsResolver) ||
            !PublishNetworkServiceStatus() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_CONFIGURED\r\n"u8))
            return false;

        ManagedNetworkServiceBackend.SetLiveIpv4(this);
        if (!_phase23Consumer.TryRun()) return false;
        _phase23Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DNS_PHASE23_PASS\r\n"u8);
    }

    internal bool TryRunPhase32()
    {
        if (_phase32Passed || _active || _networkService == null)
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE32_IPV4_GUARD_FAILED\r\n"u8);
            return false;
        }
        _phase32Consumer ??= new ManagedPhase32TestConsumer(_networkService);
        if (!_arp.TryBeginDhcp())
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE32_ARP_DHCP_BEGIN_FAILED\r\n"u8);
            return false;
        }
        _active = true;
        _networkService.BeginBoot();
        _dns.ResetForDhcp();
        _tcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                       ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_READY\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true) ||
            !_udpEndpoints.TryUnregister(DhcpClientPort) ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                       ManagedUdpEndpointHandler.DnsResolver) ||
            !PublishNetworkServiceStatus() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_CONFIGURED\r\n"u8))
            return false;

        ManagedNetworkServiceBackend.SetLiveIpv4(this);
        if (!_phase32Consumer.TryRun()) return false;
        _phase32Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DNS_PHASE32_PASS\r\n"u8);
    }

    internal bool TryRunPhase33()
    {
        if (_phase33Passed || _active || _networkService == null)
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE33_IPV4_GUARD_FAILED\r\n"u8);
            return false;
        }
        _phase33Consumer ??= new ManagedPhase33TestConsumer(_networkService);
        if (!_arp.TryBeginDhcp())
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE33_ARP_DHCP_BEGIN_FAILED\r\n"u8);
            return false;
        }
        _active = true;
        _networkService.BeginBoot();
        _dns.ResetForDhcp();
        _tcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                       ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE33_READY\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true) ||
            !_udpEndpoints.TryUnregister(DhcpClientPort) ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                       ManagedUdpEndpointHandler.DnsResolver) ||
            !PublishNetworkServiceStatus() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE33_CONFIGURED\r\n"u8))
            return false;

        ManagedNetworkServiceBackend.SetLiveIpv4(this);
        return TryRunPhase33Consumer();
    }

    private bool TryRunPhase33Consumer()
    {
        if (_phase33Consumer == null || !_phase33Consumer.TryRun()) return false;
        _phase33Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DNS_PHASE33_PASS\r\n"u8);
    }

    internal bool TryRunPhase34()
    {
        if (_phase34Passed || _active || _networkService == null)
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE34_IPV4_GUARD_FAILED\r\n"u8);
            return false;
        }
        _phase34Consumer ??= new ManagedPhase34TestConsumer(_networkService);
        if (!_arp.TryBeginDhcp())
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE34_ARP_DHCP_BEGIN_FAILED\r\n"u8);
            return false;
        }
        _active = true;
        _networkService.BeginBoot();
        _dns.ResetForDhcp();
        _tcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                       ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE34_READY\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true) ||
            !_udpEndpoints.TryUnregister(DhcpClientPort) ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                       ManagedUdpEndpointHandler.DnsResolver) ||
            !PublishNetworkServiceStatus() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE34_CONFIGURED\r\n"u8))
            return false;

        ManagedNetworkServiceBackend.SetLiveIpv4(this);
        return TryRunPhase34Consumer();
    }

    private bool TryRunPhase34Consumer()
    {
        if (_phase34Consumer == null || !_phase34Consumer.TryRun()) return false;
        _phase34Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_DNS_PHASE34_PASS\r\n"u8);
    }

    /* Phase 35 is an intentionally separate boot mode.  It performs the same
       managed DHCP/DNS/TCP/TLS composition as the production service, but is
       entered only after the loader has selected QEMU's user-mode backend.
       The fixture phases never call this method. */
    internal bool TryRunPhase35()
    {
        if (_phase35Passed || _active || _networkService == null)
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE35_IPV4_GUARD_FAILED\r\n"u8);
            return false;
        }
        _phase35Consumer ??= new ManagedPhase35PublicHttpsConsumer(_networkService);
        if (!_arp.TryBeginDhcp())
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE35_ARP_DHCP_BEGIN_FAILED\r\n"u8);
            return false;
        }
        _active = true;
        _networkService.BeginBoot();
        _dns.ResetForDhcp();
        _tcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _gatewayIpv4.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _gatewayIpv4Value = 0;
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        if (!_udpEndpoints.TryRegister(DhcpClientPort,
                                       ManagedUdpEndpointHandler.Dhcpv4Client) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_PUBLIC_HTTPS_BEGIN\r\n"u8) ||
            !TryRunDhcpDora(requireDnsServer: true, requireGateway: true) ||
            !_udpEndpoints.TryUnregister(DhcpClientPort) ||
            !_udpEndpoints.TryRegister(DnsClientPort,
                                       ManagedUdpEndpointHandler.DnsResolver) ||
            !PublishNetworkServiceStatus() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_PUBLIC_HTTPS_CONFIGURED\r\n"u8))
            return false;

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_PUBLIC_HTTPS_IPV4=0x"u8,
                _localIpv4Value) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_PUBLIC_HTTPS_SUBNET=0x"u8,
                _subnetMaskValue) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_PUBLIC_HTTPS_GATEWAY=0x"u8,
                _gatewayIpv4Value) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_PUBLIC_HTTPS_DNS=0x"u8,
                DnsServerValue))
            return false;

        ManagedNetworkServiceBackend.SetLiveIpv4(this);
        if (!_phase35Consumer.TryRun()) return false;
        if (_phase35Consumer.ControlledTlsIncompatibility)
            return true;
        _phase35Passed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_PUBLIC_HTTPS_PASS\r\n"u8);
    }

    private bool PublishNetworkServiceStatus()
    {
        if (_networkService == null) return false;
        ulong mac = 0;
        ReadOnlySpan<byte> localMac = _ethernet.LocalMac;
        for (int index = 0; index != localMac.Length; ++index)
            mac = (mac << 8) | localMac[index];
        _networkService.SetRuntimeStatus(new NetworkStatus(
            true, true, true, true, mac,
            new Ipv4Address(_localIpv4Value),
            new Ipv4Address(_subnetMaskValue),
            new Ipv4Address(DnsServerValue),
            new Ipv4Address(_gatewayIpv4Value)));
        return true;
    }

    private bool TryResolveDns(ReadOnlySpan<byte> name, bool expectResolved)
    {
        if (!_dns.TryStartQuery(name) ||
            !_dns.TryBuildQuery(_dnsQuery, out ushort queryLength) ||
            !TrySendUdpDatagram(DnsClientPort, DnsServerPort,
                                 _dnsQuery.AsSpan(0, queryLength),
                                 _dns.ServerIpv4, out _) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_DNS_QUERY_SENT\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DNS_TRANSACTION_ID=0x"u8,
                                    _dns.TransactionId))
            return false;

        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result)) return false;
            if (result == ManagedNetworkDispatchResult.DnsResolved)
                return expectResolved && _dns.HasResolvedAddress;
            if (result == ManagedNetworkDispatchResult.DnsNxDomain)
                return !expectResolved && !_dns.HasResolvedAddress;
            if (result == ManagedNetworkDispatchResult.Failed) return false;
        }
        return false;
    }

    private bool TryRunResolvedTraffic(bool firstRun)
    {
        if (!_dns.HasResolvedAddress ||
            !ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue,
                ManagedEthernetProtocol.ReadUInt32Network(
                    _dns.ResolvedIpv4, 0)))
            return false;
        ushort identifier = firstRun ? (ushort)0x2001 : (ushort)0x2002;
        ushort sequence = firstRun ? (ushort)1 : (ushort)2;
        if (!TrySendPing(_dns.ResolvedIpv4, identifier, sequence) ||
            !KernelLog.Write(firstRun
                ? "GXOS_NET10:MANAGED_DNS_RESOLVED_ICMP_SENT\r\n"u8
                : "GXOS_NET10:MANAGED_DNS_POST_GC_ICMP_SENT\r\n"u8) ||
            !WaitForReply(identifier, sequence) ||
            !TrySendUdpDatagram(Phase18LocalPort, Phase18PeerPort,
                                 _managedUdpPayload.AsSpan(0,
                                     _managedUdpPayloadLength),
                                 _dns.ResolvedIpv4, out _) ||
            !KernelLog.Write(firstRun
                ? "GXOS_NET10:MANAGED_DNS_RESOLVED_UDP_SENT\r\n"u8
                : "GXOS_NET10:MANAGED_DNS_POST_GC_UDP_SENT\r\n"u8) ||
            !WaitForUdpResponse(firstRun ? 1U : 2U))
            return false;
        return KernelLog.Write(firstRun
            ? "GXOS_NET10:MANAGED_DNS_RESOLVED_TRAFFIC_PASS\r\n"u8
            : "GXOS_NET10:MANAGED_DNS_POST_GC_TRAFFIC_PASS\r\n"u8);
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

    private bool TryRunDhcpDora(bool requireDnsServer = false,
                                bool requireGateway = false)
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
                if (!ApplyDhcpLease(requireDnsServer, requireGateway) ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_DHCP_ACK_ACCEPTED\r\n"u8) ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_DHCP_BOUND\r\n"u8) ||
                    !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_LEASED_IPV4=0x"u8,
                                            _localIpv4Value) ||
                     !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_SUBNET_MASK=0x"u8,
                                             _subnetMaskValue) ||
                     !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_GATEWAY=0x"u8,
                                             _gatewayIpv4Value) ||
                     !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DHCP_DNS_SERVER=0x"u8,
                                             DnsServerValue) ||
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

    private bool ApplyDhcpLease(bool requireDnsServer = false,
                                bool requireGateway = false)
    {
        if (!_dhcp.HasLease ||
            (requireDnsServer && !_dhcp.LeasedHasDnsServer1) ||
            (requireGateway && !_dhcp.LeasedHasRouter))
            return false;
        _dhcp.LeasedIpv4.CopyTo(_localIpv4);
        _dhcp.LeasedMask.CopyTo(_subnetMask);
        _dhcp.LeasedServerIdentifier.CopyTo(_peerIpv4);
        _gatewayIpv4.AsSpan().Clear();
        if (_dhcp.LeasedHasRouter) _dhcp.LeasedRouter.CopyTo(_gatewayIpv4);
        _localIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_localIpv4, 0);
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        _gatewayIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_gatewayIpv4, 0);
        _subnetMaskValue = ManagedEthernetProtocol.ReadUInt32Network(_subnetMask, 0);
        if (requireDnsServer && !_dns.TryInstallServer(_dhcp.LeasedDnsServer1))
            return false;
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, _peerIpv4Value) ||
            (requireGateway && !ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, _gatewayIpv4Value)))
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
        if (parsed.Protocol == ManagedTcpProtocol.Protocol)
            return TryHandleTcp(parsed);
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
        _serviceUdpTxCount = 0;
        _udpPendingRejectCount = 0;
        _udpManagedResponseCount = 0;
        _udpPeerResponseCount = 0;
        _txUdp.AsSpan().Clear();
        _txTcp.AsSpan().Clear();
        _tcp.ResetForTeardown();
        _tcpRxValidCount = 0;
        _tcpRxMalformedCount = 0;
        _tcpChecksumFailureCount = 0;
        _tcpTxCount = 0;
        _dhcpPacket.AsSpan().Clear();
        _dhcp.ResetForTeardown();
        _localIpv4.AsSpan().Clear();
        _subnetMask.AsSpan().Clear();
        _gatewayIpv4.AsSpan().Clear();
        _localIpv4Value = 0;
        _subnetMaskValue = 0;
        _gatewayIpv4Value = 0;
        _phase19Passed = false;
        _phase20Passed = false;
        _phase21Passed = false;
        _phase22Passed = false;
        _phase23Passed = false;
        _phase32Passed = false;
        _phase33Passed = false;
        _phase34Passed = false;
        _phase35Passed = false;
        _servicePingIdentifier = 0x2101;
        _servicePingSequence = 1;
        _dns.ResetForTeardown();
        _awaitedDestinationIpv4.AsSpan().Clear();
        return true;
    }

    internal bool TryBeginServiceResolve(ReadOnlySpan<byte> name)
    {
        if (!_active || _dhcp.State != ManagedDhcpv4State.Bound ||
            !_dns.HasServer || _dns.IsActive ||
            !_dns.TryStartQuery(name) ||
            !_dns.TryBuildQuery(_dnsQuery, out ushort queryLength) ||
            !TrySendUdpDatagram(DnsClientPort, DnsServerPort,
                                _dnsQuery.AsSpan(0, queryLength),
                                _dns.ServerIpv4, out _))
        {
            _dns.CancelActiveQuery();
            return false;
        }
        return true;
    }

    internal bool TryGetServiceResolved(out Ipv4Address address)
    {
        address = new Ipv4Address(DnsResolvedIpv4Value);
        return DnsHasResolvedAddress;
    }

    internal bool TryBeginServicePing(Ipv4Address destination)
    {
        if (!_active || _dhcp.State != ManagedDhcpv4State.Bound ||
            !destination.IsUsable || _awaitingReply || _pending.IsActive)
            return false;
        Span<byte> address = stackalloc byte[4];
        destination.CopyTo(address);
        ushort identifier = _servicePingIdentifier++;
        if (_servicePingIdentifier == 0) _servicePingIdentifier = 1;
        ushort sequence = _servicePingSequence++;
        if (_servicePingSequence == 0) _servicePingSequence = 1;
        ushort ipIdentifier = sequence == 1 ? (ushort)0x2E12 : (ushort)0x2E17;
        return TrySendPing(address, identifier, sequence, ipIdentifier);
    }

    internal bool TryRegisterServiceEndpoint(ushort port)
    {
        return _active && _dhcp.State == ManagedDhcpv4State.Bound &&
               _udpEndpoints.TryRegister(port,
                                          ManagedUdpEndpointHandler.Phase21Service);
    }

    internal bool TryUnregisterServiceEndpoint(ushort port)
    {
        return _udpEndpoints.TryUnregister(port);
    }

    internal bool TryServiceSendUdp(Ipv4Address destination,
                                    ushort destinationPort, ushort sourcePort,
                                    ReadOnlySpan<byte> payload)
    {
        if (!_udpEndpoints.TryLookup(sourcePort,
                                     out ManagedUdpEndpointHandler handler) ||
            handler != ManagedUdpEndpointHandler.Phase21Service)
            return false;
        Span<byte> address = stackalloc byte[4];
        destination.CopyTo(address);
        ushort ipIdentifier = _serviceUdpTxCount == 0
            ? (ushort)0x2E14
            : (ushort)0x2E19;
        if (!TrySendUdpDatagram(sourcePort, destinationPort, payload,
                                address, ipIdentifier, out _))
            return false;
        _serviceUdpTxCount++;
        return true;
    }

    internal bool TryBeginServiceTcpConnect(Ipv4Address destination,
                                             ushort destinationPort)
    {
        if (!_active || _dhcp.State != ManagedDhcpv4State.Bound ||
            !destination.IsUsable || destinationPort == 0 ||
            _pending.IsActive || _tcp.State != ManagedTcpConnectionState.Closed)
            return false;
        _tcpGeneration = _tcpGeneration == uint.MaxValue ? 1 : _tcpGeneration + 1;
        return _tcp.TryBeginConnect(new Ipv4Address(_localIpv4Value), destination,
                                     destinationPort, _tcpGeneration);
    }

    internal bool TryServiceSendTcp(ReadOnlySpan<byte> payload)
    {
        if (!_active || _dhcp.State != ManagedDhcpv4State.Bound ||
            _pending.IsActive)
            return false;
        return _tcp.TrySendApplication(payload);
    }

    internal bool TryServiceCloseTcp()
    {
        if (!_active || _dhcp.State != ManagedDhcpv4State.Bound ||
            _pending.IsActive)
            return false;
        return _tcp.TryClose();
    }

    internal bool TryServiceTeardown()
    {
        _udpEndpoints.TryUnregisterHandler(ManagedUdpEndpointHandler.Phase21Service);
        _dns.CancelActiveQuery();
        _awaitingReply = false;
        _replyValidated = false;
        _awaitedIdentifier = 0;
        _awaitedSequence = 0;
        _tcp.ResetForTeardown();
        return true;
    }

    private bool TrySendPing(ushort identifier, ushort sequence)
    {
        return TrySendPing(_peerIpv4, identifier, sequence,
                           (ushort)(0x1700 + sequence));
    }

    private bool TrySendPing(ReadOnlySpan<byte> destinationIpv4,
                             ushort identifier, ushort sequence)
    {
        return TrySendPing(destinationIpv4, identifier, sequence,
                           (ushort)(0x1700 + sequence));
    }

    private bool TrySendPing(ReadOnlySpan<byte> destinationIpv4,
                             ushort identifier, ushort sequence,
                             ushort ipIdentifier)
    {
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue,
                ManagedEthernetProtocol.ReadUInt32Network(destinationIpv4, 0)) ||
            !ManagedIcmpv4Protocol.TryBuildEchoRequest(
                _txIcmp, identifier, sequence,
                _pingPayload.AsSpan(0, _pingPayloadLength),
                out ushort icmpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, ipIdentifier, 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedIpv4Protocol.IcmpProtocol,
                _localIpv4, destinationIpv4, _txIcmp.AsSpan(0, icmpLength),
                out ushort packetLength) ||
            !TrySendPacket(destinationIpv4, _txPacket.AsSpan(0, packetLength)))
            return false;
        destinationIpv4.CopyTo(_awaitedDestinationIpv4);
        _awaitedIdentifier = identifier;
        _awaitedSequence = sequence;
        _awaitingReply = true;
        _replyValidated = false;
        return true;
    }

    private ManagedIpv4HandleResult TryHandleTcp(ManagedIpv4Packet packet)
    {
        if (!ManagedTcpProtocol.TryParse(
                packet.Payload, packet.SourceAddress, packet.DestinationAddress,
                out ManagedTcpSegment segment))
        {
            if (packet.Payload.Length >= ManagedTcpProtocol.HeaderLength &&
                ManagedTcpProtocol.ComputeChecksum(
                    packet.SourceAddress, packet.DestinationAddress,
                    packet.Payload) != 0)
                _tcpChecksumFailureCount++;
            _tcpRxMalformedCount++;
            return ManagedIpv4HandleResult.Malformed;
        }

        _tcpRxValidCount++;
        ManagedTcpHandleResult result = _tcp.TryHandle(
            segment, _networkService as IManagedTcpApplicationSink);
        if (_phase34Consumer != null &&
            (result == ManagedTcpHandleResult.FinReceived ||
             result == ManagedTcpHandleResult.OutOfOrder ||
             result == ManagedTcpHandleResult.Failed))
            KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE34_TCP_DISPATCH_RESULT=0x"u8,
                (ulong)result);
        return result switch
        {
            ManagedTcpHandleResult.Established =>
                ManagedIpv4HandleResult.TcpEstablished,
            ManagedTcpHandleResult.DataAcknowledged =>
                ManagedIpv4HandleResult.TcpDataAcknowledged,
            ManagedTcpHandleResult.DataReceived =>
                ManagedIpv4HandleResult.TcpDataReceived,
            ManagedTcpHandleResult.DuplicateData =>
                ManagedIpv4HandleResult.TcpDuplicateData,
            ManagedTcpHandleResult.OutOfOrder =>
                ManagedIpv4HandleResult.TcpOutOfOrder,
            ManagedTcpHandleResult.RstReceived =>
                ManagedIpv4HandleResult.TcpRstReceived,
            ManagedTcpHandleResult.FinReceived =>
                ManagedIpv4HandleResult.TcpFinReceived,
            ManagedTcpHandleResult.ReceiveUnavailable =>
                ManagedIpv4HandleResult.TcpReceiveUnavailable,
            ManagedTcpHandleResult.Failed => ManagedIpv4HandleResult.Failed,
            _ => ManagedIpv4HandleResult.Ignored
        };
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
        if (handler == ManagedUdpEndpointHandler.DnsResolver)
            return TryHandleDnsUdp(packet, datagram);
        if (handler == ManagedUdpEndpointHandler.Phase21Service)
            return TryHandleServiceUdp(packet, datagram);
        if (handler != ManagedUdpEndpointHandler.Phase18Echo)
            return ManagedIpv4HandleResult.Ignored;
        return TryHandlePhase18Udp(packet, datagram);
    }

    private ManagedIpv4HandleResult TryHandleServiceUdp(
        ManagedIpv4Packet packet, ManagedUdpDatagram datagram)
    {
        if (_networkService == null) return ManagedIpv4HandleResult.Failed;
        Ipv4Address source = new Ipv4Address(
            ManagedEthernetProtocol.ReadUInt32Network(packet.SourceAddress, 0));
        Ipv4Address destination = new Ipv4Address(
            ManagedEthernetProtocol.ReadUInt32Network(packet.DestinationAddress, 0));
        return _networkService.TryCaptureReceivedUdp(
                   source, destination, datagram.SourcePort,
                   datagram.DestinationPort, datagram.Payload)
            ? ManagedIpv4HandleResult.UdpServiceReceived
            : ManagedIpv4HandleResult.UdpReceiveOverflow;
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

    private ManagedIpv4HandleResult TryHandleDnsUdp(
        ManagedIpv4Packet packet, ManagedUdpDatagram datagram)
    {
        if (!_dns.HasServer ||
            ManagedEthernetProtocol.ReadUInt32Network(packet.SourceAddress, 0) !=
                ManagedEthernetProtocol.ReadUInt32Network(_dns.ServerIpv4, 0) ||
            ManagedEthernetProtocol.ReadUInt32Network(packet.DestinationAddress, 0) !=
                _localIpv4Value || datagram.SourcePort != DnsServerPort ||
            datagram.DestinationPort != DnsClientPort)
            return ManagedIpv4HandleResult.DnsResponseIgnored;

        ManagedDnsResult result = _dns.TryProcessResponse(
            datagram.SourcePort, datagram.DestinationPort, datagram.Payload);
        return result switch
        {
            ManagedDnsResult.Resolved =>
                KernelLog.Write("GXOS_NET10:MANAGED_DNS_RESPONSE_VALID\r\n"u8) &&
                KernelLog.WriteHexLine("GXOS_NET10:MANAGED_DNS_RESOLVED_IPV4=0x"u8,
                                       ManagedEthernetProtocol.ReadUInt32Network(
                                           _dns.ResolvedIpv4, 0))
                    ? ManagedIpv4HandleResult.DnsResolved
                    : ManagedIpv4HandleResult.Failed,
            ManagedDnsResult.NxDomain =>
                KernelLog.Write("GXOS_NET10:MANAGED_DNS_NXDOMAIN_RECEIVED\r\n"u8)
                    ? ManagedIpv4HandleResult.DnsNxDomain
                    : ManagedIpv4HandleResult.Failed,
            ManagedDnsResult.Truncated =>
                KernelLog.Write("GXOS_NET10:MANAGED_DNS_TRUNCATED_REJECTED\r\n"u8)
                    ? ManagedIpv4HandleResult.DnsResponseTruncated
                    : ManagedIpv4HandleResult.Failed,
            ManagedDnsResult.Malformed or ManagedDnsResult.UnsupportedOpcode or
            ManagedDnsResult.UnsupportedRcode =>
                KernelLog.Write("GXOS_NET10:MANAGED_DNS_MALFORMED_REJECTED\r\n"u8)
                    ? ManagedIpv4HandleResult.DnsResponseMalformed
                    : ManagedIpv4HandleResult.Failed,
            _ => ManagedIpv4HandleResult.DnsResponseIgnored
        };
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
        return TrySendUdpDatagram(sourcePort, destinationPort, payload,
                                  destinationIpv4,
                                  (ushort)(0x1900 + _udpTxCount),
                                  out packetLength);
    }

    private bool TrySendUdpDatagram(ushort sourcePort, ushort destinationPort,
                                    ReadOnlySpan<byte> payload,
                                    ReadOnlySpan<byte> destinationIpv4,
                                    ushort ipIdentifier,
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
                _txPacket, ipIdentifier, 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedUdpProtocol.Protocol,
                _localIpv4, destinationIpv4, _txUdp.AsSpan(0, udpLength),
                out packetLength) ||
            !TrySendPacket(destinationIpv4,
                            _txPacket.AsSpan(0, packetLength)))
            return false;
        _udpTxCount++;
        return true;
    }

    bool IManagedTcpPacketSender.TrySendTcp(
        Ipv4Address destination, ushort sourcePort, ushort destinationPort,
        uint sequenceNumber, uint acknowledgmentNumber, ManagedTcpFlags flags,
        ushort window, ReadOnlySpan<byte> payload, bool advertiseMss)
    {
        Span<byte> destinationBytes = stackalloc byte[4];
        destination.CopyTo(destinationBytes);
        if (!_active || _dhcp.State != ManagedDhcpv4State.Bound ||
            !destination.IsUsable ||
            !ManagedTcpProtocol.TryBuild(
                _txTcp, sourcePort, destinationPort, sequenceNumber,
                acknowledgmentNumber, flags, window, _localIpv4,
                destinationBytes, payload,
                advertiseMss, ManagedTcpProtocol.MaximumMss, out ushort tcpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, (ushort)(0x2A00 + _tcpTxCount), 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedTcpProtocol.Protocol,
                _localIpv4, destinationBytes,
                _txTcp.AsSpan(0, tcpLength), out ushort packetLength) ||
            !TrySendPacket(destinationBytes,
                            _txPacket.AsSpan(0, packetLength)))
            return false;
        _tcpTxCount++;
        return true;
    }

    private bool TrySendPacket(ReadOnlySpan<byte> destinationIpv4,
                               ReadOnlySpan<byte> packet)
    {
        uint destinationIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(
            destinationIpv4, 0);
        if (!ManagedIpv4Protocol.TrySelectNextHop(
                _localIpv4Value, _subnetMaskValue, _gatewayIpv4Value,
                destinationIpv4Value, out uint nextHopIpv4Value))
            return false;
        Span<byte> nextHopIpv4 = stackalloc byte[4];
        ManagedEthernetProtocol.WriteUInt32Network(nextHopIpv4, 0,
                                                    nextHopIpv4Value);
        if (_arp.Cache.TryLookup(nextHopIpv4, _destinationMac))
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
        nextHopIpv4.CopyTo(_pendingIpv4);
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, nextHopIpv4Value) ||
            !KernelLog.WriteHexLine(
                destinationIpv4Value == nextHopIpv4Value
                    ? "GXOS_NET10:MANAGED_IPV4_ARP_NEXT_HOP_DIRECT=0x"u8
                    : "GXOS_NET10:MANAGED_IPV4_ARP_NEXT_HOP_GATEWAY=0x"u8,
                nextHopIpv4Value) ||
            !_arp.TryResolve(nextHopIpv4))
        {
            _pending.Clear();
            return false;
        }
        // ARP resolution consumes the ARP layer's pending state, but the
        // packet staged above still belongs to this IPv4 layer.  Release that
        // single bounded slot only after the cache has been populated.
        return TryReleasePendingAfterArp();
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
                ManagedEthernetProtocol.ReadUInt32Network(
                    _awaitedDestinationIpv4, 0) ||
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

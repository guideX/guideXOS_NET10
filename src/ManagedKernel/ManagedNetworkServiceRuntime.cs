using System;

namespace GuideXOS.Net10.ManagedKernel;

internal sealed class ManagedNetworkServiceBackend : IManagedNetworkServiceBackend
{
    private static bool s_runtimeStatusValid;
    private static NetworkStatus s_runtimeStatus;
    private static ManagedEthernetLayer? s_runtimeEthernet;
    private static ManagedIpv4Layer? s_runtimeIpv4;
    private static ManagedNetworkService? s_runtimeService;
    private ManagedEthernetLayer _ethernet;
    private ManagedIpv4Layer _ipv4;

    internal ManagedNetworkServiceBackend(ManagedEthernetLayer ethernet,
                                          ManagedIpv4Layer ipv4)
    {
        _ethernet = ethernet;
        _ipv4 = ipv4;
        s_runtimeEthernet = ethernet;
        s_runtimeIpv4 = ipv4;
    }

    internal static ManagedEthernetLayer? LiveEthernet => s_runtimeEthernet;
    internal static ManagedIpv4Layer? LiveIpv4 => s_runtimeIpv4;
    internal static ManagedNetworkService? LiveService => s_runtimeService;

    internal void AttachService(ManagedNetworkService service)
    {
        s_runtimeService = service;
    }

    internal void Rebind(ManagedEthernetLayer ethernet,
                         ManagedIpv4Layer ipv4)
    {
        s_runtimeEthernet = ethernet;
        s_runtimeIpv4 = ipv4;
        _ethernet = ethernet;
        _ipv4 = ipv4;
    }

    internal static void SetLiveIpv4(ManagedIpv4Layer ipv4)
    {
        s_runtimeIpv4 = ipv4;
    }

    private ManagedEthernetLayer Ethernet => s_runtimeEthernet ?? _ethernet;
    private ManagedIpv4Layer Ipv4 => s_runtimeIpv4 ?? _ipv4;

    public bool IsAvailable => s_runtimeStatusValid
        ? s_runtimeStatus.LinkReady : Ethernet.IsAccepting;

    public ManagedTcpConnectionState TcpState => Ipv4.TcpState;

    public void SetRuntimeStatus(NetworkStatus status)
    {
        s_runtimeStatus = status;
        s_runtimeStatusValid = status.LinkReady || status.DriverReady ||
                               status.Configured || status.DhcpBound ||
                               status.MacAddress != 0 ||
                               status.Ipv4Address.Value != 0 ||
                               status.DnsServer.Value != 0;
    }

    public NetworkStatus GetStatus()
    {
        if (s_runtimeStatusValid) return s_runtimeStatus;
        ulong mac = 0;
        ReadOnlySpan<byte> localMac = Ethernet.LocalMac;
        for (int index = 0; index != localMac.Length; ++index)
            mac = (mac << 8) | localMac[index];
        return new NetworkStatus(
            Ethernet.IsAccepting,
            Ethernet.DriverReady,
            Ipv4.DhcpState == ManagedDhcpv4State.Bound,
            Ipv4.DhcpState == ManagedDhcpv4State.Bound,
            mac,
            new Ipv4Address(Ipv4.LocalIpv4Value),
            new Ipv4Address(Ipv4.SubnetMaskValue),
            new Ipv4Address(Ipv4.DnsServerValue));
    }

    public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
    {
        if (!Ipv4.TryBeginServiceResolve(name))
            return ManagedNetworkServiceBackendResult.NoResource;
        return ManagedNetworkServiceBackendResult.Started;
    }

    public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
    {
        serviceEvent = ManagedNetworkServiceBackendEvent.None;
        if (!Ethernet.TryReceiveAndDispatch(out ManagedNetworkDispatchResult result))
        {
            /* A bounded RX poll with no completed descriptor is an idle
               service poll, not a protocol failure.  The consumer remains in
               Pending state and the host may satisfy the outstanding ARP or
               peer exchange before the next poll. */
            return true;
        }
        serviceEvent = result switch
        {
            ManagedNetworkDispatchResult.DnsResolved =>
                ManagedNetworkServiceBackendEvent.DnsResolved,
            ManagedNetworkDispatchResult.DnsNxDomain =>
                ManagedNetworkServiceBackendEvent.DnsNxDomain,
            ManagedNetworkDispatchResult.IcmpEchoReplyValidated =>
                ManagedNetworkServiceBackendEvent.PingReply,
            ManagedNetworkDispatchResult.UdpServiceReceived =>
                ManagedNetworkServiceBackendEvent.UdpReceived,
            ManagedNetworkDispatchResult.UdpReceiveOverflow =>
                ManagedNetworkServiceBackendEvent.UdpReceiveOverflow,
            ManagedNetworkDispatchResult.TcpEstablished =>
                ManagedNetworkServiceBackendEvent.TcpEstablished,
            ManagedNetworkDispatchResult.TcpDataReceived =>
                ManagedNetworkServiceBackendEvent.TcpReceived,
            ManagedNetworkDispatchResult.TcpFinReceived =>
                ManagedNetworkServiceBackendEvent.TcpClosed,
            ManagedNetworkDispatchResult.TcpRstReceived =>
                ManagedNetworkServiceBackendEvent.TcpFailed,
            ManagedNetworkDispatchResult.Failed =>
                ManagedNetworkServiceBackendEvent.None,
            _ => ManagedNetworkServiceBackendEvent.None
        };
        return result != ManagedNetworkDispatchResult.Failed;
    }

    public bool TryGetResolved(out Ipv4Address address)
    {
        address = new Ipv4Address(Ipv4.DnsResolvedIpv4Value);
        return Ipv4.DnsHasResolvedAddress;
    }

    public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination)
    {
        if (Ipv4.ServicePingActive || Ipv4.PendingTransmissionActive)
            return ManagedNetworkServiceBackendResult.Busy;
        return Ipv4.TryBeginServicePing(destination)
            ? ManagedNetworkServiceBackendResult.Started
            : ManagedNetworkServiceBackendResult.NoResource;
    }

    public ManagedNetworkServiceBackendResult BindUdp(ushort port)
    {
        return Ipv4.TryRegisterServiceEndpoint(port)
            ? ManagedNetworkServiceBackendResult.Started
            : ManagedNetworkServiceBackendResult.NoResource;
    }

    public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port)
    {
        return Ipv4.TryUnregisterServiceEndpoint(port)
            ? ManagedNetworkServiceBackendResult.Success
            : ManagedNetworkServiceBackendResult.Rejected;
    }

    public ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination,
                                                      ushort destinationPort,
                                                      ushort sourcePort,
                                                      ReadOnlySpan<byte> payload)
    {
        if (Ipv4.PendingTransmissionActive)
            return ManagedNetworkServiceBackendResult.Busy;
        if (!Ipv4.TryServiceSendUdp(destination, destinationPort, sourcePort,
                                     payload))
            return ManagedNetworkServiceBackendResult.NoResource;
        return ManagedNetworkServiceBackendResult.Success;
    }

    public ManagedNetworkServiceBackendResult BeginTcpConnect(
        Ipv4Address destination, ushort destinationPort)
    {
        if (Ipv4.TcpState != ManagedTcpConnectionState.Closed)
            return Ipv4.TcpState == ManagedTcpConnectionState.Failed
                ? ManagedNetworkServiceBackendResult.Failed
                : ManagedNetworkServiceBackendResult.Busy;
        return Ipv4.TryBeginServiceTcpConnect(destination, destinationPort)
            ? ManagedNetworkServiceBackendResult.Started
            : ManagedNetworkServiceBackendResult.NoResource;
    }

    public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
    {
        if (Ipv4.TcpState != ManagedTcpConnectionState.Established)
            return ManagedNetworkServiceBackendResult.Rejected;
        if (!Ipv4.TryServiceSendTcp(payload))
            return Ipv4.TcpHasInFlight
                ? ManagedNetworkServiceBackendResult.Busy
                : ManagedNetworkServiceBackendResult.NoResource;
        return ManagedNetworkServiceBackendResult.Success;
    }

    public ManagedNetworkServiceBackendResult CloseTcp()
    {
        return Ipv4.TryServiceCloseTcp()
            ? ManagedNetworkServiceBackendResult.Started
            : ManagedNetworkServiceBackendResult.Rejected;
    }

    public bool Teardown()
    {
        return Ipv4.TryServiceTeardown();
    }
}

/* This is the managed application-level consumer used by the authoritative
   Phase 21 boot.  It knows only the service contract and fixed application
   constants; protocol layers remain behind ManagedNetworkService. */
internal sealed class ManagedPhase21TestConsumer
{
    private const ushort LocalPort = 15210;
    private const ushort PeerPort = 15211;
    private static ReadOnlySpan<byte> Hostname => "phase21.test"u8;
    private static ReadOnlySpan<byte> Request => "PHASE21-API-HELLO"u8;
    private static ReadOnlySpan<byte> Reply => "PHASE21-API-ACK"u8;

    private readonly ManagedNetworkService _service;
    private readonly byte[] _receivePayload =
        new byte[ManagedNetworkService.MaximumUdpPayloadLength];

    internal ManagedPhase21TestConsumer(ManagedNetworkService service)
    {
        _service = service;
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.DhcpBound || !status.Configured)
            return false;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_DHCP_BOUND\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_NETWORK_SERVICE_IPV4=0x"u8,
                                    status.Ipv4Address.Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_NETWORK_SERVICE_DNS=0x"u8,
                                    status.DnsServer.Value))
            return false;

        if (_service.BeginResolveIpv4(Hostname) != NetworkOperationResult.Started ||
            !WaitForResolution(out Ipv4Address resolved) ||
            resolved.Value != 0x0A0F0002U ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_DNS_SUCCESS\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_NETWORK_SERVICE_RESOLVED_IPV4=0x"u8,
                                    resolved.Value) ||
            !RunExchange(resolved, firstRun: true))
            return false;

        GC.Collect();
        if (!KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_GC_SURVIVAL_PASSED\r\n"u8) ||
            _service.GetStatus().Ipv4Address != status.Ipv4Address ||
            _service.BeginResolveIpv4(Hostname) != NetworkOperationResult.Started ||
            !WaitForResolution(out resolved) ||
            !RunExchange(resolved, firstRun: false))
            return false;

        if (_service.UnregisterUdpEndpoint(LocalPort) != NetworkOperationResult.Success ||
            _service.Teardown() != NetworkOperationResult.Success ||
            _service.GetStatus().Configured ||
            _service.BeginResolveIpv4(Hostname) != NetworkOperationResult.Unavailable ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_TEARDOWN_PASSED\r\n"u8))
            return false;
        return KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_PHASE21_PASS\r\n"u8);
    }

    private bool WaitForResolution(out Ipv4Address address)
    {
        address = default;
        for (int count = 0; count != 16; ++count)
        {
            if (_service.Poll() == NetworkOperationResult.Unavailable) return false;
            if (_service.ResolutionState == NetworkResolutionState.Success)
                return _service.TryGetResolvedIpv4(out address);
            if (_service.ResolutionState != NetworkResolutionState.Pending)
                return false;
        }
        return false;
    }

    private bool RunExchange(Ipv4Address destination, bool firstRun)
    {
        NetworkOperationResult pingResult =
            _service.BeginPingIpv4(destination);
        if (pingResult != NetworkOperationResult.Started) return false;
        if (firstRun &&
            _service.BindUdpEndpoint(LocalPort) != NetworkOperationResult.Started)
            return false;
        if (!WaitForPing() ||
            !KernelLog.Write(firstRun
                ? "GXOS_NET10:MANAGED_NETWORK_SERVICE_ICMP_SUCCESS\r\n"u8
                : "GXOS_NET10:MANAGED_NETWORK_SERVICE_POST_GC_ICMP_SUCCESS\r\n"u8) ||
            _service.SendUdp(destination, PeerPort, LocalPort, Request) !=
                NetworkOperationResult.Success ||
            !WaitForReply())
            return false;
        return KernelLog.Write(firstRun
            ? "GXOS_NET10:MANAGED_NETWORK_SERVICE_UDP_SUCCESS\r\n"u8
            : "GXOS_NET10:MANAGED_NETWORK_SERVICE_POST_GC_UDP_SUCCESS\r\n"u8);
    }

    private bool WaitForPing()
    {
        for (int count = 0; count != 16; ++count)
        {
            if (_service.Poll() == NetworkOperationResult.Unavailable) return false;
            if (_service.PingState == NetworkPingState.Success) return true;
            if (_service.PingState != NetworkPingState.Pending) return false;
        }
        return false;
    }

    private bool WaitForReply()
    {
        for (int count = 0; count != 16; ++count)
        {
            if (_service.Poll() == NetworkOperationResult.Unavailable) return false;
            if (!_service.HasReceivedUdp) continue;
            if (!_service.TryReceiveUdp(_receivePayload, out _, out ushort sourcePort,
                                        out ushort destinationPort,
                                        out int length) ||
                sourcePort != PeerPort || destinationPort != LocalPort ||
                length != Reply.Length ||
                !_receivePayload.AsSpan(0, length).SequenceEqual(Reply))
                return false;
            return KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_UDP_REPLY_VALID\r\n"u8);
        }
        return false;
    }
}

/* The Phase 22 consumer deliberately exposes only the service contract.  The
   TCP tuple, sequence numbers, options, and packet buffers remain below this
   boundary. */
internal sealed class ManagedPhase22TestConsumer
{
    private const ushort PeerPort = ManagedTcpConnection.ServerPort;
    private static ReadOnlySpan<byte> Hostname => "phase22.test"u8;
    private static ReadOnlySpan<byte> FirstRequest => "PHASE22-MANAGED-HELLO"u8;
    private static ReadOnlySpan<byte> FirstReply => "PHASE22-PEER-ACK"u8;
    private static ReadOnlySpan<byte> SecondRequest => "PHASE22-POSTGC-HELLO"u8;
    private static ReadOnlySpan<byte> SecondReply => "PHASE22-POSTGC-ACK"u8;

    private readonly ManagedNetworkService _service;
    private readonly byte[] _receivePayload =
        new byte[ManagedNetworkService.MaximumTcpPayloadLength];

    internal ManagedPhase22TestConsumer(ManagedNetworkService service)
    {
        _service = service;
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.DhcpBound || !status.Configured)
            return false;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_DHCP_BOUND\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_IPV4=0x"u8,
                                    status.Ipv4Address.Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_DNS=0x"u8,
                                    status.DnsServer.Value))
            return false;

        if (_service.BeginResolveIpv4(Hostname) != NetworkOperationResult.Started ||
            !WaitForResolution(out Ipv4Address resolved) ||
            resolved.Value != 0x0A0F0002U ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_DNS_SUCCESS\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_RESOLVED_IPV4=0x"u8,
                                    resolved.Value) ||
            _service.BeginTcpConnect(resolved, PeerPort) != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_CONNECT_STARTED\r\n"u8) ||
            !WaitForState(NetworkTcpState.Established) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_HANDSHAKE_SUCCESS\r\n"u8) ||
            _service.SendTcp(FirstRequest) != NetworkOperationResult.Success ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_FIRST_REQUEST_SENT\r\n"u8) ||
            !WaitForReply(FirstReply) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_FIRST_EXCHANGE_SUCCESS\r\n"u8))
            return false;

        if (_service.TcpState != NetworkTcpState.Established)
            return false;
        GC.Collect();
        if (_service.TcpState != NetworkTcpState.Established ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_GC_WHILE_ESTABLISHED_PASSED\r\n"u8) ||
            _service.SendTcp(SecondRequest) != NetworkOperationResult.Success ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_POST_GC_REQUEST_SENT\r\n"u8) ||
            !WaitForReply(SecondReply) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_POST_GC_EXCHANGE_SUCCESS\r\n"u8) ||
            _service.CloseTcp() != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_FIN_SENT\r\n"u8) ||
            !WaitForState(NetworkTcpState.TimeWait) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_TCP_GRACEFUL_CLOSE_SUCCESS\r\n"u8) ||
            _service.Teardown() != NetworkOperationResult.Success ||
            _service.TcpState != NetworkTcpState.Closed ||
            !KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_TCP_TEARDOWN_PASSED\r\n"u8))
            return false;
        return KernelLog.Write("GXOS_NET10:MANAGED_NETWORK_SERVICE_PHASE22_PASS\r\n"u8);
    }

    private bool WaitForResolution(out Ipv4Address address)
    {
        address = default;
        for (int count = 0; count != 32; ++count)
        {
            if (_service.Poll() == NetworkOperationResult.Unavailable) return false;
            if (_service.ResolutionState == NetworkResolutionState.Success)
                return _service.TryGetResolvedIpv4(out address);
            if (_service.ResolutionState != NetworkResolutionState.Pending)
                return false;
        }
        return false;
    }

    private bool WaitForState(NetworkTcpState expected)
    {
        for (int count = 0; count != 64; ++count)
        {
            if (_service.TcpState == expected) return true;
            if (_service.Poll() == NetworkOperationResult.Unavailable ||
                _service.TcpState == NetworkTcpState.Failed)
                return false;
        }
        return _service.TcpState == expected;
    }

    private bool WaitForReply(ReadOnlySpan<byte> expected)
    {
        for (int count = 0; count != 64; ++count)
        {
            if (_service.Poll() == NetworkOperationResult.Unavailable ||
                _service.TcpState == NetworkTcpState.Failed)
                return false;
            if (!_service.HasReceivedTcp) continue;
            if (!_service.TryReceiveTcp(_receivePayload, out _,
                                        out ushort sourcePort,
                                        out ushort destinationPort,
                                        out int length) ||
                sourcePort != PeerPort ||
                destinationPort != ManagedTcpConnection.ClientPort ||
                length != expected.Length ||
                !_receivePayload.AsSpan(0, length).SequenceEqual(expected))
                return false;
            return KernelLog.Write("GXOS_NET10:MANAGED_TCP_RESPONSE_VALID\r\n"u8);
        }
        return false;
    }
}

/* Phase 23 is the application proof boundary.  This consumer knows only the
   managed HTTP service contract; Ethernet, ARP, IPv4, TCP, and E1000 details
   remain below ManagedNetworkService. */
internal sealed class ManagedPhase23TestConsumer
{
    private static ReadOnlySpan<byte> Hostname => "phase23.test"u8;
    private static ReadOnlySpan<byte> Path => "/phase23"u8;
    private static ReadOnlySpan<byte> ExpectedBody => "phase23-http-pass"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHttpClient _client;
    private readonly byte[] _body =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];

    internal ManagedPhase23TestConsumer(ManagedNetworkService service)
    {
        _service = service;
        _client = new ManagedHttpClient(service);
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.DhcpBound || !status.Configured ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_NETWORK_READY\r\n"u8))
            return false;
        if (_client.BeginGet(Hostname, Path) != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_REQUEST_STARTED\r\n"u8))
            return false;

        bool dnsLogged = false;
        bool tcpLogged = false;
        bool statusLogged = false;
        bool bodyLogged = false;
        bool gcLogged = false;
        for (int count = 0; count != 128; ++count)
        {
            NetworkOperationResult result = _client.Poll();
            if (result == NetworkOperationResult.Failed ||
                _client.State == ManagedHttpClientState.Failed)
                return false;
            if (!dnsLogged && _client.ResolvedAddress.IsUsable)
            {
                if (_client.ResolvedAddress.Value != 0x0A0F0002U ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_DNS_SUCCESS\r\n"u8) ||
                    !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTP_PHASE23_RESOLVED_IPV4=0x"u8,
                                            _client.ResolvedAddress.Value))
                    return false;
                dnsLogged = true;
            }
            if (!tcpLogged && _client.RequestSent)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_TCP_CONNECTED\r\n"u8) ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_REQUEST_SENT\r\n"u8))
                    return false;
                tcpLogged = true;
            }
            if (!statusLogged && _client.StatusParsed)
            {
                if (_client.StatusCode != 200 ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_STATUS_PARSED=200\r\n"u8))
                    return false;
                statusLogged = true;
            }
            if (statusLogged && !gcLogged)
            {
                GC.Collect();
                if (!_client.StatusParsed || _client.StatusCode != 200 ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_GC_SURVIVAL_PASSED\r\n"u8))
                    return false;
                gcLogged = true;
            }
            if (!bodyLogged && _client.ResponseBodyComplete)
            {
                if (_client.ResponseBodyLength != ExpectedBody.Length ||
                    _client.ContentLength != ExpectedBody.Length ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_BODY_RECEIVED\r\n"u8))
                    return false;
                bodyLogged = true;
            }
            if (_client.State != ManagedHttpClientState.Succeeded) continue;
            if (!dnsLogged || !tcpLogged || !statusLogged || !bodyLogged ||
                !_client.TryCopyResponseBody(_body, out int bodyLength) ||
                bodyLength != ExpectedBody.Length ||
                !_body.AsSpan(0, bodyLength).SequenceEqual(ExpectedBody) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_BODY_VERIFIED\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_TEARDOWN_COMPLETE\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTP_PHASE23_PASS\r\n"u8))
                return false;
            return true;
        }
        return false;
    }
}

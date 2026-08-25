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

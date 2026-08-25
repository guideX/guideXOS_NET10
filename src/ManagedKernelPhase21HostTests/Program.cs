using System;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly Ipv4Address s_peer = new(0x0A0F0002U);
    private static readonly byte[] s_name = Encoding.ASCII.GetBytes("phase21.test");
    private static readonly byte[] s_request = Encoding.ASCII.GetBytes("PHASE21-API-HELLO");
    private static readonly byte[] s_reply = Encoding.ASCII.GetBytes("PHASE21-API-ACK");
    private static int s_cases;

    private static int Main()
    {
        DefaultAndConfiguredStatus();
        DnsServiceState();
        PingServiceState();
        UdpServiceState();
        ReceiveDeliveryAndGc();
        TeardownAndReinitialize();
        Console.WriteLine($"MANAGED_KERNEL_PHASE21_HOST_TESTS_PASS cases={s_cases}");
        return 0;
    }

    private static void DefaultAndConfiguredStatus()
    {
        FakeBackend fake = new(configured: false);
        ManagedNetworkService service = NewService(fake);
        NetworkStatus initial = service.GetStatus();
        Check(!initial.Configured && !initial.DhcpBound &&
              initial.MacAddress == 0x525400123456UL,
              "status-default-unconfigured");
        Check(service.BeginResolveIpv4(s_name) == NetworkOperationResult.NotConfigured,
              "resolve-before-configured");

        fake.SetConfigured();
        NetworkStatus snapshot = service.GetStatus();
        Check(snapshot.LinkReady && snapshot.DriverReady && snapshot.Configured &&
              snapshot.DhcpBound && snapshot.MacAddress == 0x525400123456UL &&
              snapshot.Ipv4Address == new Ipv4Address(0x0A0F002AU) &&
              snapshot.SubnetMask == new Ipv4Address(0xFFFFFF00U) &&
              snapshot.DnsServer == s_peer, "status-dhcp-bound-snapshot");
        snapshot = default;
        NetworkStatus unchanged = service.GetStatus();
        Check(unchanged.Configured && unchanged.Ipv4Address ==
              new Ipv4Address(0x0A0F002AU), "status-snapshot-is-value-copy");
    }

    private static void DnsServiceState()
    {
        FakeBackend fake = new(configured: true);
        ManagedNetworkService service = NewService(fake);
        Check(service.BeginResolveIpv4(s_name) == NetworkOperationResult.Started &&
              service.ResolutionState == NetworkResolutionState.Pending,
              "resolve-started");
        Check(service.BeginResolveIpv4(s_name) == NetworkOperationResult.Busy,
              "second-resolve-busy");
        Check(service.Poll() == NetworkOperationResult.Success &&
              service.ResolutionState == NetworkResolutionState.Success &&
              service.TryGetResolvedIpv4(out Ipv4Address resolved) &&
              resolved == s_peer, "resolve-success-uses-backend-result");
        Check(service.BeginResolveIpv4(Array.Empty<byte>()) ==
                  NetworkOperationResult.InvalidArgument,
              "invalid-hostname-rejected");
        Check(service.BeginResolveIpv4(Encoding.ASCII.GetBytes("a..b")) ==
                  NetworkOperationResult.InvalidArgument,
              "empty-host-label-rejected");

        fake.NextResolutionIsNxDomain = true;
        Check(service.BeginResolveIpv4(s_name) == NetworkOperationResult.Started &&
              service.Poll() == NetworkOperationResult.Success &&
              service.ResolutionState == NetworkResolutionState.NxDomain &&
              !service.TryGetResolvedIpv4(out _), "resolve-nxdomain");
        fake.BeginResolveResult = ManagedNetworkServiceBackendResult.Busy;
        Check(service.BeginResolveIpv4(s_name) == NetworkOperationResult.Busy,
              "resolver-backend-busy-is-deterministic");
    }

    private static void PingServiceState()
    {
        FakeBackend fake = new(configured: false);
        ManagedNetworkService service = NewService(fake);
        Check(service.BeginPingIpv4(s_peer) == NetworkOperationResult.NotConfigured,
              "ping-before-configured");
        fake.SetConfigured();
        Check(service.BeginPingIpv4(s_peer) == NetworkOperationResult.Started &&
              service.PingState == NetworkPingState.Pending,
              "ping-started");
        Check(service.BeginPingIpv4(s_peer) == NetworkOperationResult.Busy,
              "second-ping-busy");
        Check(service.Poll() == NetworkOperationResult.Success &&
              service.PingState == NetworkPingState.Success, "ping-success");
        Check(service.BeginPingIpv4(default) == NetworkOperationResult.InvalidArgument,
              "zero-ping-destination-rejected");
    }

    private static void UdpServiceState()
    {
        FakeBackend fake = new(configured: true);
        ManagedNetworkService service = NewService(fake);
        Check(service.SendUdp(s_peer, 15211, 15210, s_request) ==
                  NetworkOperationResult.Rejected,
              "udp-send-requires-bound-source");
        Check(service.BindUdpEndpoint(15210) == NetworkOperationResult.Started,
              "udp-bind");
        Check(service.BindUdpEndpoint(15210) == NetworkOperationResult.Busy,
              "udp-duplicate-bind");
        Check(service.BindUdpEndpoint(0) == NetworkOperationResult.InvalidArgument,
              "udp-zero-port-rejected");
        Check(service.SendUdp(s_peer, 0, 15210, s_request) ==
                  NetworkOperationResult.InvalidArgument,
              "udp-zero-destination-port-rejected");
        byte[] oversized = new byte[ManagedNetworkService.MaximumUdpPayloadLength + 1];
        Check(service.SendUdp(s_peer, 15211, 15210, oversized) ==
                  NetworkOperationResult.InvalidArgument,
              "udp-payload-bound");
        fake.SendResult = ManagedNetworkServiceBackendResult.Busy;
        Check(service.SendUdp(s_peer, 15211, 15210, s_request) ==
                  NetworkOperationResult.Busy, "udp-pending-send-busy");
        fake.SendResult = ManagedNetworkServiceBackendResult.Success;
        Check(service.SendUdp(s_peer, 15211, 15210, s_request) ==
                  NetworkOperationResult.Success && fake.LastPayload.SequenceEqual(s_request),
              "udp-send-success-exact-payload");
        Check(service.UnregisterUdpEndpoint(15210) == NetworkOperationResult.Success,
              "udp-unregister");
        Check(service.UnregisterUdpEndpoint(15210) == NetworkOperationResult.Rejected,
              "udp-unregister-inactive-rejected");

        for (ushort port = 15220; port != 15224; ++port)
            Check(service.BindUdpEndpoint(port) == NetworkOperationResult.Started,
                  "udp-fixed-capacity-bind");
        Check(service.BoundEndpointCount == ManagedNetworkService.UdpEndpointCapacity &&
              service.BindUdpEndpoint(15224) == NetworkOperationResult.NoResource,
              "udp-endpoint-capacity-is-fixed");
    }

    private static void ReceiveDeliveryAndGc()
    {
        FakeBackend fake = new(configured: true);
        ManagedNetworkService service = NewService(fake);
        Check(service.BindUdpEndpoint(15210) == NetworkOperationResult.Started,
              "receive-endpoint-bind");
        fake.QueueUdpReply(service, s_peer, new Ipv4Address(0x0A0F002AU),
                           15211, 15210, s_reply);
        GC.Collect();
        Check(service.Poll() == NetworkOperationResult.Success && service.HasReceivedUdp,
              "receive-survives-gc");
        byte[] output = new byte[ManagedNetworkService.MaximumUdpPayloadLength];
        Check(service.TryReceiveUdp(output, out Ipv4Address source,
                  out ushort sourcePort, out ushort destinationPort,
                  out int length) && source == s_peer && sourcePort == 15211 &&
              destinationPort == 15210 && length == s_reply.Length &&
              output.AsSpan(0, length).SequenceEqual(s_reply),
              "receive-copies-owned-message-slot");
        Check(!service.HasReceivedUdp, "receive-consume-clears-slot");
        Check(service.TryCaptureReceivedUdp(s_peer, new Ipv4Address(0x0A0F002AU),
                  15211, 15210, s_reply), "receive-slot-refill");
        Check(!service.TryCaptureReceivedUdp(s_peer, new Ipv4Address(0x0A0F002AU),
                  15211, 15210, s_reply), "receive-overflow-not-silent");
        Check(service.ReceiveOverflowCount == 0, "receive-overflow-count-is-dispatch-owned");
    }

    private static void TeardownAndReinitialize()
    {
        FakeBackend fake = new(configured: true);
        ManagedNetworkService service = NewService(fake);
        Check(service.BindUdpEndpoint(15210) == NetworkOperationResult.Started,
              "teardown-registration-setup");
        Check(service.Teardown() == NetworkOperationResult.Success &&
              !service.GetStatus().Configured && service.BoundEndpointCount == 0 &&
              service.ResolutionState == NetworkResolutionState.Idle &&
              service.PingState == NetworkPingState.Idle,
              "service-teardown-clears-state");
        Check(service.BeginPingIpv4(s_peer) == NetworkOperationResult.Unavailable &&
              service.SendUdp(s_peer, 15211, 15210, s_request) ==
                  NetworkOperationResult.Unavailable &&
              !service.TryCaptureReceivedUdp(s_peer, s_peer, 15211, 15210, s_reply),
              "operations-after-teardown-rejected");
        service.BeginBoot();
        Check(service.BindUdpEndpoint(15210) == NetworkOperationResult.Started,
              "service-reinit-clears-stale-generation");
    }

    private static ManagedNetworkService NewService(FakeBackend fake)
    {
        return ManagedNetworkService.CreateForTests(fake);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {name}");
        s_cases++;
        Console.WriteLine($"PASS: {name}");
    }

    private sealed class FakeBackend : IManagedNetworkServiceBackend
    {
        private NetworkStatus _status;
        private bool _eventPending;
        private ManagedNetworkServiceBackendEvent _event;
        private ManagedNetworkService? _service;
        private Ipv4Address _resolved;
        private ManagedTcpConnectionState _tcpState;

        internal FakeBackend(bool configured)
        {
            _status = new NetworkStatus(true, true, configured, configured,
                0x525400123456UL, new Ipv4Address(0x0A0F002AU),
                new Ipv4Address(0xFFFFFF00U), s_peer);
            SendResult = ManagedNetworkServiceBackendResult.Success;
            BeginResolveResult = ManagedNetworkServiceBackendResult.Started;
        }

        internal bool NextResolutionIsNxDomain;
        internal ManagedNetworkServiceBackendResult BeginResolveResult;
        internal ManagedNetworkServiceBackendResult SendResult;
        internal byte[] LastPayload { get; private set; } = Array.Empty<byte>();

        public bool IsAvailable => true;
        public ManagedTcpConnectionState TcpState => _tcpState;
        public NetworkStatus GetStatus() => _status;
        public void SetRuntimeStatus(NetworkStatus status) { }

        internal void SetConfigured()
        {
            _status = new NetworkStatus(true, true, true, true,
                0x525400123456UL, new Ipv4Address(0x0A0F002AU),
                new Ipv4Address(0xFFFFFF00U), s_peer);
        }

        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
            if (BeginResolveResult != ManagedNetworkServiceBackendResult.Started)
                return BeginResolveResult;
            _eventPending = true;
            _event = NextResolutionIsNxDomain
                ? ManagedNetworkServiceBackendEvent.DnsNxDomain
                : ManagedNetworkServiceBackendEvent.DnsResolved;
            NextResolutionIsNxDomain = false;
            _resolved = s_peer;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool TryGetResolved(out Ipv4Address address)
        {
            address = _resolved;
            return true;
        }

        public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
        {
            if (!_eventPending)
            {
                serviceEvent = ManagedNetworkServiceBackendEvent.None;
                return true;
            }
            serviceEvent = _event;
            _eventPending = false;
            if (serviceEvent == ManagedNetworkServiceBackendEvent.UdpReceived &&
                _service != null && _service.BoundEndpointCount != 0)
            {
                _service.TryCaptureReceivedUdp(s_peer,
                    new Ipv4Address(0x0A0F002AU), 15211, 15210, s_reply);
            }
            return true;
        }

        public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination)
        {
            _eventPending = true;
            _event = ManagedNetworkServiceBackendEvent.PingReply;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public ManagedNetworkServiceBackendResult BindUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.Started;

        public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.Success;

        public ManagedNetworkServiceBackendResult SendUdp(
            Ipv4Address destination, ushort destinationPort, ushort sourcePort,
            ReadOnlySpan<byte> payload)
        {
            LastPayload = payload.ToArray();
            return SendResult;
        }

        public ManagedNetworkServiceBackendResult BeginTcpConnect(
            Ipv4Address destination, ushort destinationPort) =>
            ManagedNetworkServiceBackendResult.NoResource;

        public ManagedNetworkServiceBackendResult SendTcp(
            ReadOnlySpan<byte> payload) =>
            ManagedNetworkServiceBackendResult.Rejected;

        public ManagedNetworkServiceBackendResult CloseTcp() =>
            ManagedNetworkServiceBackendResult.Rejected;

        public bool Teardown()
        {
            _eventPending = false;
            _tcpState = ManagedTcpConnectionState.Closed;
            return true;
        }

        internal void QueueUdpReply(ManagedNetworkService service,
                                    Ipv4Address source, Ipv4Address destination,
                                    ushort sourcePort, ushort destinationPort,
                                    ReadOnlySpan<byte> payload)
        {
            _service = service;
            _eventPending = true;
            _event = ManagedNetworkServiceBackendEvent.UdpReceived;
        }
    }
}

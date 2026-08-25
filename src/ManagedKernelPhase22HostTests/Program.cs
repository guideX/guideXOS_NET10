using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly Ipv4Address Local = new(0x0A0F002AU);
    private static readonly Ipv4Address Peer = new(0x0A0F0002U);
    private const ushort PeerPort = ManagedTcpConnection.ServerPort;
    private const ushort LocalPort = ManagedTcpConnection.ClientPort;
    private static int s_cases;

    private static void Main()
    {
        TestParserAndBuilder();
        TestChecksumVectors();
        TestSequenceHelpers();
        TestConnectionHandshakeAndData();
        TestConnectionCloseAndReset();
        TestRetryBound();
        TestServiceBoundary();
        Console.WriteLine($"MANAGED_KERNEL_PHASE22_HOST_TESTS_PASS cases={s_cases}");
    }

    private static void TestParserAndBuilder()
    {
        byte[] syn = Build(ManagedTcpFlags.Syn, 0x22000001U, 0,
                           ReadOnlySpan<byte>.Empty, advertiseMss: false);
        Check(ManagedTcpProtocol.TryParse(syn, LocalBytes, PeerBytes,
                  out ManagedTcpSegment parsed) && parsed.DataOffset == 5 &&
              parsed.SourcePort == LocalPort && parsed.DestinationPort == PeerPort,
              "tcp-valid-minimum-header");
        Check(!ManagedTcpProtocol.TryParse(syn.AsSpan(0, 19), LocalBytes,
                  PeerBytes, out _), "tcp-truncated-header");
        byte[] offsetFour = (byte[])syn.Clone(); offsetFour[12] = 0x40;
        Check(!ManagedTcpProtocol.TryParse(offsetFour, LocalBytes, PeerBytes,
                  out _), "tcp-data-offset-four-rejected");
        byte[] offsetPast = (byte[])syn.Clone(); offsetPast[12] = 0xF0;
        Check(!ManagedTcpProtocol.TryParse(offsetPast, LocalBytes, PeerBytes,
                  out _), "tcp-declared-header-past-packet-rejected");

        byte[] mss = Build(ManagedTcpFlags.Syn, 0x22000001U, 0,
                           ReadOnlySpan<byte>.Empty, advertiseMss: true);
        Check(ManagedTcpProtocol.TryParse(mss, LocalBytes, PeerBytes,
                  out parsed) && parsed.DataOffset == 6 && parsed.HasMss &&
              parsed.Mss == ManagedTcpProtocol.MaximumMss,
              "tcp-mss-option");
        byte[] nopEol = ReplaceOptions(mss, new byte[] { 1, 0, 0, 0 });
        Check(ManagedTcpProtocol.TryParse(nopEol, LocalBytes, PeerBytes,
                  out parsed) && !parsed.HasMss, "tcp-nop-eol-options");
        byte[] unknown = ReplaceOptions(mss, new byte[] { 30, 4, 0x12, 0x34 });
        Check(ManagedTcpProtocol.TryParse(unknown, LocalBytes, PeerBytes,
                  out _), "tcp-unknown-well-formed-option-skipped");
        byte[] badOption = ReplaceOptions(mss, new byte[] { 2, 1, 0, 0 });
        Check(!ManagedTcpProtocol.TryParse(badOption, LocalBytes, PeerBytes,
                  out _), "tcp-malformed-option-length-rejected");

        byte[] ack = Build(ManagedTcpFlags.Ack, 2, 3, ReadOnlySpan<byte>.Empty,
                           advertiseMss: false);
        Check(ManagedTcpProtocol.TryParse(ack, LocalBytes, PeerBytes,
                  out parsed) && parsed.Has(ManagedTcpFlags.Ack),
              "tcp-ack-only-header");
        byte[] fin = Build(ManagedTcpFlags.Fin | ManagedTcpFlags.Ack, 2, 3,
                           ReadOnlySpan<byte>.Empty, advertiseMss: false);
        Check(ManagedTcpProtocol.TryParse(fin, LocalBytes, PeerBytes,
                  out parsed) && parsed.Has(ManagedTcpFlags.Fin),
              "tcp-fin-header");
        byte[] badReserved = (byte[])ack.Clone(); badReserved[12] = 0x51;
        Check(!ManagedTcpProtocol.TryParse(badReserved, LocalBytes, PeerBytes,
                  out _), "tcp-reserved-bits-rejected");
    }

    private static void TestChecksumVectors()
    {
        byte[] syn = Build(ManagedTcpFlags.Syn, 0x22000001U, 0,
                           ReadOnlySpan<byte>.Empty, false);
        byte[] synMss = Build(ManagedTcpFlags.Syn, 0x22000001U, 0,
                              ReadOnlySpan<byte>.Empty, true);
        byte[] ack = Build(ManagedTcpFlags.Ack, 0x22000002U, 0x22010002U,
                           ReadOnlySpan<byte>.Empty, false);
        byte[] fin = Build(ManagedTcpFlags.Fin | ManagedTcpFlags.Ack,
                           0x22000020U, 0x22010020U,
                           ReadOnlySpan<byte>.Empty, false);
        byte[] odd = Build(ManagedTcpFlags.Ack | ManagedTcpFlags.Psh,
                           0x12345678U, 0x9ABCDEF0U,
                           new byte[] { 1, 2, 3, 4, 5 }, false);
        byte[] even = Build(ManagedTcpFlags.Ack | ManagedTcpFlags.Psh,
                            0x12345678U, 0x9ABCDEF0U,
                            new byte[] { 1, 2, 3, 4, 5, 6 }, false);
        Check(Read16(syn, 16) == 0x00AD, "tcp-checksum-independent-syn");
        Check(Read16(synMss, 16) == 0xECA4, "tcp-checksum-independent-syn-mss");
        Check(Read16(ack, 16) == 0xDE9A, "tcp-checksum-independent-ack");
        Check(Read16(fin, 16) == 0xDE5D, "tcp-checksum-independent-fin");
        Check(Read16(odd, 16) == 0x3733, "tcp-checksum-independent-odd");
        Check(Read16(even, 16) == 0x372C, "tcp-checksum-independent-even");
        Check(ManagedTcpProtocol.ComputeChecksum(LocalBytes, PeerBytes, syn) == 0,
              "tcp-checksum-validates-syn");

        byte[] payloadMutation = (byte[])odd.Clone(); payloadMutation[^1] ^= 1;
        byte[] portMutation = (byte[])odd.Clone(); portMutation[1] ^= 1;
        byte[] sequenceMutation = (byte[])odd.Clone(); sequenceMutation[7] ^= 1;
        byte[] ackMutation = (byte[])odd.Clone(); ackMutation[11] ^= 1;
        byte[] flagMutation = (byte[])odd.Clone(); flagMutation[13] ^= 1;
        Check(!Valid(payloadMutation) && !Valid(portMutation) &&
              !Valid(sequenceMutation) && !Valid(ackMutation) && !Valid(flagMutation),
              "tcp-checksum-mutations-rejected");
        Check(!ManagedTcpProtocol.TryParse(odd, PeerBytes, PeerBytes, out _),
              "tcp-source-ip-mutation-rejected");
        Check(!ManagedTcpProtocol.TryParse(odd, LocalBytes, LocalBytes, out _),
              "tcp-destination-ip-mutation-rejected");
    }

    private static void TestSequenceHelpers()
    {
        Check(ManagedTcpSequence.IsBefore(0xFFFFFFFEU, 1) &&
              ManagedTcpSequence.IsAfter(1, 0xFFFFFFFEU), "tcp-wrap-before-after");
        Check(ManagedTcpSequence.IsBefore(0x7FFFFFFFU, 0x80000000U) &&
              ManagedTcpSequence.IsBefore(0x80000000U, 0xFFFFFFFFU),
              "tcp-sequence-boundaries");
        Check(ManagedTcpSequence.Advance(0xFFFFFFFFU, 2) == 1,
              "tcp-sequence-wrap-advance");
        Check(ManagedTcpSequence.IsAfterOrEqual(0, 0) &&
              ManagedTcpSequence.IsBeforeOrEqual(0xFFFFFFFFU, 0),
              "tcp-sequence-equality");
    }

    private static void TestConnectionHandshakeAndData()
    {
        FakeSender sender = new();
        FakeSink sink = new();
        ManagedTcpConnection connection = new(sender);
        Check(connection.TryBeginConnect(Local, Peer, PeerPort, 1) &&
              connection.State == ManagedTcpConnectionState.SynSent &&
              sender.LastFlags == ManagedTcpFlags.Syn &&
              sender.LastSequence == ManagedTcpConnection.FirstClientIsn,
              "tcp-connect-sends-exact-syn");

        byte[] wrongAck = BuildInbound(ManagedTcpFlags.Syn | ManagedTcpFlags.Ack,
            0x22010001U, 0x22000003U, ReadOnlySpan<byte>.Empty, true);
        Check(Parsed(wrongAck, out ManagedTcpSegment wrong) &&
              connection.TryHandle(wrong, sink) == ManagedTcpHandleResult.Ignored &&
              connection.State == ManagedTcpConnectionState.SynSent,
              "tcp-wrong-synack-ack-rejected");
        byte[] synAck = BuildInbound(ManagedTcpFlags.Syn | ManagedTcpFlags.Ack,
            0x22010001U, 0x22000002U, ReadOnlySpan<byte>.Empty, true);
        Check(Parsed(synAck, out ManagedTcpSegment accepted) &&
              connection.TryHandle(accepted, sink) == ManagedTcpHandleResult.Established &&
              connection.State == ManagedTcpConnectionState.Established &&
              sender.LastFlags == ManagedTcpFlags.Ack,
              "tcp-valid-synack-establishes");
        Check(!connection.TrySendApplication(new byte[ManagedTcpProtocol.MaximumPayloadLength + 1]),
              "tcp-above-bound-send-rejected");
        byte[] request = "PHASE22-MANAGED-HELLO"u8.ToArray();
        Check(connection.TrySendApplication(request) && connection.HasInFlight &&
              sender.LastSequence == 0x22000002U &&
              connection.SendNext == 0x22000002U + (uint)request.Length,
              "tcp-data-sequence-advances-exactly");
        Check(!connection.TrySendApplication("SECOND"u8),
              "tcp-one-in-flight-restriction");
        byte[] dataAck = BuildInbound(ManagedTcpFlags.Ack, 0x22010002U,
            connection.SendNext, ReadOnlySpan<byte>.Empty, false);
        Check(Parsed(dataAck, out ManagedTcpSegment dataAckSegment) &&
              connection.TryHandle(dataAckSegment, sink) ==
                  ManagedTcpHandleResult.DataAcknowledged &&
              !connection.HasInFlight,
              "tcp-exact-data-ack-clears-inflight");
        byte[] peerData = "PHASE22-PEER-ACK"u8.ToArray();
        uint peerDataSequence = connection.ReceiveNext;
        byte[] peerFrame = BuildInbound(ManagedTcpFlags.Ack | ManagedTcpFlags.Psh,
            peerDataSequence, connection.SendNext, peerData, false);
        Check(Parsed(peerFrame, out ManagedTcpSegment peerSegment) &&
              connection.TryHandle(peerSegment, sink) == ManagedTcpHandleResult.DataReceived &&
              sink.Count == 1 && sink.Last.AsSpan(0, peerData.Length).SequenceEqual(peerData),
              "tcp-peer-payload-copied-and-delivered");
        Check(Parsed(peerFrame, out peerSegment) &&
              connection.TryHandle(peerSegment, sink) == ManagedTcpHandleResult.DuplicateData &&
              sink.Count == 1,
              "tcp-duplicate-payload-acked-not-redelivered");
        byte[] future = BuildInbound(ManagedTcpFlags.Ack | ManagedTcpFlags.Psh,
            connection.ReceiveNext + 3, connection.SendNext, "FUTURE"u8, false);
        Check(Parsed(future, out ManagedTcpSegment futureSegment) &&
              connection.TryHandle(futureSegment, sink) == ManagedTcpHandleResult.OutOfOrder &&
              sink.Count == 1,
              "tcp-future-payload-not-buffered");
        GC.Collect();
        Check(connection.State == ManagedTcpConnectionState.Established &&
              connection.ReceiveNext == peerDataSequence + (uint)peerData.Length,
              "tcp-established-gc-survival");
    }

    private static void TestConnectionCloseAndReset()
    {
        FakeSink sink = new();
        FakeSender rstSender = new();
        ManagedTcpConnection rst = Established(rstSender, sink, 3);
        byte[] rstFrame = BuildInbound(ManagedTcpFlags.Rst | ManagedTcpFlags.Ack,
            rst.ReceiveNext, rst.SendNext, ReadOnlySpan<byte>.Empty, false);
        Check(Parsed(rstFrame, out ManagedTcpSegment rstSegment) &&
              rst.TryHandle(rstSegment, sink) == ManagedTcpHandleResult.RstReceived &&
              rst.State == ManagedTcpConnectionState.Failed,
              "tcp-matching-rst-fails-connection");
        rst.ResetForTeardown();
        Check(rst.State == ManagedTcpConnectionState.Closed &&
              Parsed(rstFrame, out rstSegment) &&
              rst.TryHandle(rstSegment, sink) == ManagedTcpHandleResult.Ignored,
              "tcp-stale-rst-rejected-after-reset");

        FakeSender sender = new();
        ManagedTcpConnection close = Established(sender, sink, 4);
        Check(close.TryClose() && close.State == ManagedTcpConnectionState.FinWait1 &&
              sender.LastFlags == (ManagedTcpFlags.Fin | ManagedTcpFlags.Ack) &&
              close.SendNext == sender.LastSequence + 1,
              "tcp-active-close-fin-consumes-one");
        byte[] finAck = BuildInbound(ManagedTcpFlags.Ack, 0x22010002U,
            close.SendNext, ReadOnlySpan<byte>.Empty, false);
        Check(Parsed(finAck, out ManagedTcpSegment finAckSegment) &&
              close.TryHandle(finAckSegment, sink) == ManagedTcpHandleResult.DataAcknowledged &&
              close.State == ManagedTcpConnectionState.FinWait2,
              "tcp-fin-ack-enters-finwait2");
        byte[] peerFin = BuildInbound(ManagedTcpFlags.Fin | ManagedTcpFlags.Ack,
            close.ReceiveNext, close.SendNext, ReadOnlySpan<byte>.Empty, false);
        Check(Parsed(peerFin, out ManagedTcpSegment peerFinSegment) &&
              close.TryHandle(peerFinSegment, sink) == ManagedTcpHandleResult.FinReceived &&
              close.State == ManagedTcpConnectionState.TimeWait,
              "tcp-peer-fin-final-ack-timewait");
        Check(!close.TryBeginConnect(Local, Peer, PeerPort, 5),
              "tcp-timewait-tuple-not-reusable");

        ManagedTcpConnection peerClose = Established(new FakeSender(), sink, 6);
        byte[] incomingFin = BuildInbound(ManagedTcpFlags.Fin | ManagedTcpFlags.Ack,
            peerClose.ReceiveNext, peerClose.SendNext, ReadOnlySpan<byte>.Empty, false);
        Check(Parsed(incomingFin, out ManagedTcpSegment incomingFinSegment) &&
              peerClose.TryHandle(incomingFinSegment, sink) == ManagedTcpHandleResult.FinReceived &&
              peerClose.State == ManagedTcpConnectionState.CloseWait &&
              peerClose.TryClose() && peerClose.State == ManagedTcpConnectionState.LastAck,
              "tcp-peer-initiated-close-closewait-lastack");
        byte[] lastAck = BuildInbound(ManagedTcpFlags.Ack, 0x22010002U,
            peerClose.SendNext, ReadOnlySpan<byte>.Empty, false);
        Check(Parsed(lastAck, out ManagedTcpSegment lastAckSegment) &&
              peerClose.TryHandle(lastAckSegment, sink) == ManagedTcpHandleResult.DataAcknowledged &&
              peerClose.State == ManagedTcpConnectionState.TimeWait,
              "tcp-peer-initiated-close-terminal");
    }

    private static void TestRetryBound()
    {
        FakeSender sender = new();
        ManagedTcpConnection connection = new(sender);
        Check(connection.TryBeginConnect(Local, Peer, PeerPort, 7),
              "tcp-retry-setup");
        Check(connection.TryRetryPending() && connection.TryRetryPending() &&
              connection.TryRetryPending() && connection.RetryCount == 3,
              "tcp-syn-retry-bound-three");
        Check(!connection.TryRetryPending() &&
              connection.State == ManagedTcpConnectionState.Failed &&
              connection.RetryExhaustionCount == 1,
              "tcp-syn-retry-exhaustion-fails");
    }

    private static void TestServiceBoundary()
    {
        FakeBackend notConfiguredBackend = new(false);
        ManagedNetworkService notConfigured = ManagedNetworkService.CreateForTests(
            notConfiguredBackend);
        Check(notConfigured.BeginTcpConnect(Peer, PeerPort) ==
              NetworkOperationResult.NotConfigured, "service-tcp-not-configured");

        FakeBackend backend = new(true);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        Ipv4Address resolved = default;
        Check(service.BeginResolveIpv4("phase22.test"u8) == NetworkOperationResult.Started &&
              service.Poll() == NetworkOperationResult.Success &&
              service.TryGetResolvedIpv4(out resolved) &&
              resolved == Peer, "service-dns-result-feeds-tcp");
        Check(service.BeginTcpConnect(resolved, PeerPort) == NetworkOperationResult.Started &&
              backend.LastConnectDestination == resolved &&
              service.BeginTcpConnect(resolved, PeerPort) == NetworkOperationResult.Busy,
              "service-one-tcp-connection");
        Check(service.SendTcp("before"u8) == NetworkOperationResult.Busy,
              "service-send-before-established-rejected");
        Check(service.Poll() == NetworkOperationResult.Success &&
              service.TcpState == NetworkTcpState.Established &&
              service.SendTcp("payload"u8) == NetworkOperationResult.Success,
              "service-tcp-established-send");
        IManagedTcpApplicationSink sink = service;
        Check(sink.TryCaptureReceivedTcp(Peer, Local, PeerPort, LocalPort,
                                         "reply"u8) && service.HasReceivedTcp &&
              !sink.TryCaptureReceivedTcp(Peer, Local, PeerPort, LocalPort,
                                          "overwrite"u8),
              "service-tcp-single-receive-slot");
        byte[] receive = new byte[32];
        Check(service.TryReceiveTcp(receive, out _, out ushort sourcePort,
                  out ushort destinationPort, out int length) &&
              sourcePort == PeerPort && destinationPort == LocalPort &&
              length == 5 && receive.AsSpan(0, length).SequenceEqual("reply"u8),
              "service-tcp-copies-receive-slot");
        Check(service.CloseTcp() == NetworkOperationResult.Started &&
              service.TcpState == NetworkTcpState.TimeWait &&
              service.BeginTcpConnect(Peer, PeerPort) == NetworkOperationResult.Busy,
              "service-close-timewait-policy");
        Check(service.Teardown() == NetworkOperationResult.Success &&
              service.TcpState == NetworkTcpState.Closed &&
              service.BeginTcpConnect(Peer, PeerPort) == NetworkOperationResult.Unavailable,
              "service-tcp-teardown-clears-state");
    }

    private static ManagedTcpConnection Established(FakeSender sender,
                                                     FakeSink sink,
                                                     uint generation)
    {
        ManagedTcpConnection connection = new(sender);
        if (!connection.TryBeginConnect(Local, Peer, PeerPort, generation))
            throw new InvalidOperationException("connection setup failed");
        byte[] synAck = BuildInbound(ManagedTcpFlags.Syn | ManagedTcpFlags.Ack,
            0x22010001U, connection.LocalIsn + 1, ReadOnlySpan<byte>.Empty, true);
        if (!Parsed(synAck, out ManagedTcpSegment segment) ||
            connection.TryHandle(segment, sink) != ManagedTcpHandleResult.Established)
            throw new InvalidOperationException("handshake setup failed");
        return connection;
    }

    private static byte[] BuildInbound(ManagedTcpFlags flags, uint sequence,
                                       uint acknowledgment, ReadOnlySpan<byte> payload,
                                       bool advertiseMss)
    {
        byte[] packet = new byte[ManagedTcpProtocol.HeaderLength +
                                (advertiseMss ? 4 : 0) + payload.Length];
        if (!ManagedTcpProtocol.TryBuild(packet, PeerPort, LocalPort, sequence,
                acknowledgment, flags, ManagedTcpProtocol.DefaultWindow,
                PeerBytes, LocalBytes, payload, advertiseMss,
                ManagedTcpProtocol.MaximumMss, out ushort length))
            throw new InvalidOperationException("inbound segment build failed");
        return packet.AsSpan(0, length).ToArray();
    }

    private static byte[] Build(ManagedTcpFlags flags, uint sequence,
                                uint acknowledgment, ReadOnlySpan<byte> payload,
                                bool advertiseMss)
    {
        byte[] packet = new byte[ManagedTcpProtocol.HeaderLength +
                                (advertiseMss ? 4 : 0) + payload.Length];
        if (!ManagedTcpProtocol.TryBuild(packet, LocalPort, PeerPort, sequence,
                acknowledgment, flags, ManagedTcpProtocol.DefaultWindow,
                LocalBytes, PeerBytes, payload, advertiseMss,
                ManagedTcpProtocol.MaximumMss, out ushort length))
            throw new InvalidOperationException("segment build failed");
        return packet.AsSpan(0, length).ToArray();
    }

    private static byte[] ReplaceOptions(byte[] packet, byte[] options)
    {
        byte[] result = (byte[])packet.Clone();
        options.CopyTo(result, 20);
        result[(20 + options.Length)..].AsSpan().Clear();
        result[16] = 0;
        result[17] = 0;
        ushort checksum = ManagedTcpProtocol.ComputeChecksum(LocalBytes, PeerBytes,
                                                             result);
        result[16] = (byte)(checksum >> 8);
        result[17] = (byte)checksum;
        return result;
    }

    private static bool Parsed(byte[] packet, out ManagedTcpSegment segment) =>
        ManagedTcpProtocol.TryParse(packet, PeerBytes, LocalBytes, out segment);

    private static bool Valid(byte[] packet) =>
        ManagedTcpProtocol.TryParse(packet, LocalBytes, PeerBytes, out _);

    private static ushort Read16(byte[] bytes, int offset) =>
        (ushort)((bytes[offset] << 8) | bytes[offset + 1]);

    private static ushort IndependentChecksum(byte[] packet)
    {
        packet = (byte[])packet.Clone();
        packet[16] = 0; packet[17] = 0;
        uint sum = 0;
        AddIndependent(ref sum, 10, 15); AddIndependent(ref sum, 0, 42);
        AddIndependent(ref sum, 10, 15); AddIndependent(ref sum, 0, 2);
        AddIndependent(ref sum, 0, 6);
        AddIndependent(ref sum, (byte)(packet.Length >> 8), (byte)packet.Length);
        for (int i = 0; i + 1 < packet.Length; i += 2)
            AddIndependent(ref sum, packet[i], packet[i + 1]);
        if ((packet.Length & 1) != 0) AddIndependent(ref sum, packet[^1], 0);
        sum = (sum & 0xFFFFU) + (sum >> 16);
        sum = (sum & 0xFFFFU) + (sum >> 16);
        return (ushort)~sum;
    }

    private static void AddIndependent(ref uint sum, byte high, byte low)
    {
        sum += (uint)((high << 8) | low);
        sum = (sum & 0xFFFFU) + (sum >> 16);
    }

    private static byte[] LocalBytes => new byte[] { 10, 15, 0, 42 };
    private static byte[] PeerBytes => new byte[] { 10, 15, 0, 2 };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {name}");
        s_cases++;
        Console.WriteLine($"PASS: {name}");
    }

    private sealed class FakeSender : IManagedTcpPacketSender
    {
        internal int SendCount;
        internal ManagedTcpFlags LastFlags;
        internal uint LastSequence;
        internal uint LastAcknowledgment;
        internal byte[] LastPayload { get; private set; } = Array.Empty<byte>();

        public bool TrySendTcp(Ipv4Address destination, ushort sourcePort,
                               ushort destinationPort, uint sequenceNumber,
                               uint acknowledgmentNumber, ManagedTcpFlags flags,
                               ushort window, ReadOnlySpan<byte> payload,
                               bool advertiseMss)
        {
            SendCount++;
            LastFlags = flags;
            LastSequence = sequenceNumber;
            LastAcknowledgment = acknowledgmentNumber;
            LastPayload = payload.ToArray();
            return true;
        }
    }

    private sealed class FakeSink : IManagedTcpApplicationSink
    {
        internal int Count;
        internal byte[] Last { get; } = new byte[ManagedTcpProtocol.MaximumPayloadLength];

        public bool TryCaptureReceivedTcp(Ipv4Address source,
            Ipv4Address destination, ushort sourcePort, ushort destinationPort,
            ReadOnlySpan<byte> payload)
        {
            payload.CopyTo(Last);
            Count++;
            return true;
        }
    }

    private sealed class FakeBackend : IManagedNetworkServiceBackend
    {
        private NetworkStatus _status;
        private ManagedNetworkServiceBackendEvent _event;
        private bool _eventPending;
        private Ipv4Address _resolved;
        private ManagedTcpConnectionState _tcpState;

        internal FakeBackend(bool configured)
        {
            _status = new NetworkStatus(true, true, configured, configured,
                0x525400123456UL, Local, new Ipv4Address(0xFFFFFF00U), Peer);
        }

        internal Ipv4Address LastConnectDestination { get; private set; }
        public bool IsAvailable => true;
        public ManagedTcpConnectionState TcpState => _tcpState;
        public NetworkStatus GetStatus() => _status;
        public void SetRuntimeStatus(NetworkStatus status) { }

        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
            _resolved = Peer;
            _event = ManagedNetworkServiceBackendEvent.DnsResolved;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool TryGetResolved(out Ipv4Address address)
        {
            address = _resolved;
            return true;
        }

        public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
        {
            serviceEvent = _eventPending ? _event : ManagedNetworkServiceBackendEvent.None;
            _eventPending = false;
            if (serviceEvent == ManagedNetworkServiceBackendEvent.TcpEstablished)
                _tcpState = ManagedTcpConnectionState.Established;
            return true;
        }

        public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult BindUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.Rejected;
        public ManagedNetworkServiceBackendResult SendUdp(
            Ipv4Address destination, ushort destinationPort, ushort sourcePort,
            ReadOnlySpan<byte> payload) => ManagedNetworkServiceBackendResult.NoResource;

        public ManagedNetworkServiceBackendResult BeginTcpConnect(
            Ipv4Address destination, ushort destinationPort)
        {
            LastConnectDestination = destination;
            _tcpState = ManagedTcpConnectionState.SynSent;
            _event = ManagedNetworkServiceBackendEvent.TcpEstablished;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload) =>
            ManagedNetworkServiceBackendResult.Success;

        public ManagedNetworkServiceBackendResult CloseTcp()
        {
            _tcpState = ManagedTcpConnectionState.TimeWait;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool Teardown()
        {
            _tcpState = ManagedTcpConnectionState.Closed;
            _eventPending = false;
            return true;
        }
    }
}

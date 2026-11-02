using System;
using System.Collections.Generic;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private const ushort ServicePort = 15930;
    private const uint Seed = 0x93C0FFEE;
    private const int TargetLifecycles = 1500;
    private static readonly Ipv4Address s_localIp = new(0x0A0F0001U);
    private static readonly Ipv4Address s_peerIp = new(0x0A0F0002U);
    private static readonly byte[] s_localMac = { 0x52, 0x54, 0, 0x12, 0x34, 0x56 };
    private static readonly byte[] s_peerMac = { 0x52, 0x54, 0, 0xAA, 0xBB, 0xCC };
    private static int s_checks;
    private static int s_lifecycles;
    private static int s_rejected;

    private static int Main()
    {
        CheckUnavailableAndInvalidSubmission();
        CheckCapacityCompletionAndPackets();
        CheckEpisodeRestart();
        RunMixedLifecycles();
        Console.WriteLine("MANAGED_KERNEL_PHASE93_HOST_TESTS=PASS checks=" + s_checks);
        Console.WriteLine("PHASE93_HOST_SEED=0x" + Seed.ToString("X8"));
        Console.WriteLine("PHASE93_SERVICE_REQUEST_LIFECYCLES=" + s_lifecycles);
        Console.WriteLine("PHASE93_HOST_REJECTED_SUBMISSIONS=" + s_rejected);
        Console.WriteLine("PHASE93_FINAL_SERVICE_OUTSTANDING=0");
        Console.WriteLine("PHASE93_FINAL_DRIVER_OUTSTANDING=0");
        return 0;
    }

    private static void CheckUnavailableAndInvalidSubmission()
    {
        FakeBackend unavailable = new(0x9301, available: false);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(unavailable);
        Check(service.TrySendReceivedUdpEchoAsync(out _) ==
              ManagedServiceAsyncTxSubmitResult.Unavailable,
              "service-unavailable-is-explicit");
        FakeBackend downDriver = new(0x9302, available: true, driverReady: false);
        ManagedNetworkService downService = NewService(downDriver);
        QueueAndReceive(downDriver, downService, 1);
        Check(downService.TrySendReceivedUdpEchoAsync(out _) ==
              ManagedServiceAsyncTxSubmitResult.Unavailable &&
              downDriver.Adapter.Outstanding == 0,
              "uninitialized-driver-rejects-without-slot");

        FakeBackend invalid = new(0x9303, available: true);
        Check(invalid.Adapter.TrySubmitFrame(null, 14, out _) ==
                  ManagedServiceAsyncTxSubmitResult.Invalid &&
              invalid.Adapter.TrySubmitFrame(new byte[13], 13, out _) ==
                  ManagedServiceAsyncTxSubmitResult.Invalid &&
              invalid.Adapter.TrySubmitFrame(new byte[64], 1519, out _) ==
                  ManagedServiceAsyncTxSubmitResult.Invalid &&
              invalid.Adapter.Statistics.Invalid == 3 &&
              invalid.Adapter.Outstanding == 0,
              "invalid-frame-lengths-do-not-claim-ownership");
        s_rejected += 3;

        ManagedNetworkService wrongPortService = NewService(invalid);
        Check(!wrongPortService.TryCaptureReceivedUdp(s_peerIp, s_localIp,
                  24001, 15931, s_peerMac, Payload(0x93030001)) &&
              !ManagedUdpProtocol.TryParse(new byte[] { 1, 2, 3 },
                  new byte[] { 10, 15, 0, 2 }, new byte[] { 10, 15, 0, 1 },
                  out _) &&
              wrongPortService.TrySendReceivedUdpEchoAsync(out _) ==
                  ManagedServiceAsyncTxSubmitResult.Invalid &&
              invalid.Adapter.Outstanding == 0,
              "wrong-port-and-malformed-udp-do-not-submit-reply");
        s_rejected++;
    }

    private static void CheckCapacityCompletionAndPackets()
    {
        FakeBackend backend = new(0x9304, available: true);
        ManagedNetworkService service = NewService(backend);
        ulong[] ids = new ulong[ManagedNetworkServiceAsyncTxAdapter.Capacity];
        for (int index = 0; index < ids.Length; ++index)
        {
            QueueAndReceive(backend, service, (uint)(0x93040000 + index));
            Check(service.TrySendReceivedUdpEchoAsync(out ids[index]) ==
                  ManagedServiceAsyncTxSubmitResult.Accepted,
                  "capacity-accept-" + index);
            ValidateReply(backend.LastSubmittedFrame,
                Payload((uint)(0x93040000 + index)));
        }
        int ownedBeforeBusy = (int)backend.Driver.Ledger.Outstanding;
        QueueAndReceive(backend, service, 0x9304FFFF);
        Check(service.TrySendReceivedUdpEchoAsync(out _) ==
                  ManagedServiceAsyncTxSubmitResult.Busy &&
              service.UdpEchoBusyDropCount == 1 &&
              backend.Adapter.Outstanding == 4 &&
              backend.Driver.Ledger.Outstanding == ownedBeforeBusy,
              "fifth-request-busy-drops-without-mutation");
        s_rejected++;

        Check(backend.TryGetServiceTxCompletion(ids[0]) ==
              ManagedServiceAsyncTxCompletionResult.Pending,
              "pending-completion-query-is-nonblocking");
        backend.Driver.SetDd(ids[0], dispatch: false);
        Check(backend.TryGetServiceTxCompletion(ids[0]) ==
              ManagedServiceAsyncTxCompletionResult.Pending,
              "dd-before-deferred-dispatch-is-not-falsely-published");
        backend.Driver.DispatchTxdw();
        Check(service.Poll() == NetworkOperationResult.Success &&
              backend.Adapter.Outstanding == 3 &&
              backend.Adapter.Statistics.Consumed == 1,
              "dd-completion-reclaimed-by-no-rx-service-pump");
        int remainingBeforeRepeatedQuery = backend.Adapter.Outstanding;
        Check(backend.TryGetServiceTxCompletion(ids[0]) ==
                  ManagedServiceAsyncTxCompletionResult.InvalidOrStale &&
              backend.TryConsumeServiceTxCompletion(ids[0]) ==
                  ManagedServiceAsyncTxCompletionResult.InvalidOrStale &&
              backend.Adapter.Outstanding == remainingBeforeRepeatedQuery,
              "consumed-completion-cannot-be-observed-or-consumed-twice");

        for (int index = 1; index < ids.Length; ++index)
            backend.Driver.SetDd(ids[index], dispatch: false);
        Check(backend.Driver.DispatchTxdw() == 3,
              "one-coalesced-txdw-publishes-multiple-dds");
        Check(service.Poll() == NetworkOperationResult.Success &&
              backend.Adapter.Outstanding == 0 &&
              backend.Adapter.Statistics.Consumed == 4 &&
              backend.Driver.Ledger.Outstanding == 0,
              "all-four-service-slots-recovered-after-drain");
        s_lifecycles += 4;
    }

    private static void CheckEpisodeRestart()
    {
        FakeBackend oldBackend = new(0x9305, available: true);
        ManagedNetworkService oldService = NewService(oldBackend);
        QueueAndReceive(oldBackend, oldService, 0x93050001);
        Check(oldService.TrySendReceivedUdpEchoAsync(out ulong staleId) ==
              ManagedServiceAsyncTxSubmitResult.Accepted,
              "pending-service-request-before-stop");
        oldBackend.RetireAfterDmaQuiesced();
        Check(oldBackend.Adapter.Outstanding == 0 &&
              oldBackend.Adapter.Statistics.Invalidated == 1 &&
              oldBackend.Driver.Ledger.Outstanding == 0,
              "driver-quiescence-invalidates-service-record");
        Check(oldBackend.TryGetServiceTxCompletion(staleId) ==
              ManagedServiceAsyncTxCompletionResult.InvalidOrStale &&
              oldBackend.TryConsumeServiceTxCompletion(staleId) ==
              ManagedServiceAsyncTxCompletionResult.InvalidOrStale,
              "retired-adapter-rejects-old-request-id");

        FakeBackend freshBackend = new(0x9306, available: true);
        ManagedNetworkService freshService = NewService(freshBackend);
        QueueAndReceive(freshBackend, freshService, 0x93060000);
        Check(freshService.TrySendReceivedUdpEchoAsync(out ulong freshId) ==
                  ManagedServiceAsyncTxSubmitResult.Accepted && freshId != staleId,
              "restart-restores-service-submission");
        ManagedServiceAsyncTxStatistics before = freshBackend.GetAsyncTxStatistics();
        Check(freshBackend.TryGetServiceTxCompletion(staleId) ==
                  ManagedServiceAsyncTxCompletionResult.InvalidOrStale &&
              freshBackend.TryConsumeServiceTxCompletion(staleId) ==
                  ManagedServiceAsyncTxCompletionResult.InvalidOrStale &&
              freshBackend.GetAsyncTxStatistics().Outstanding == before.Outstanding &&
              freshBackend.Adapter.TryGetCompletion(freshId) ==
                  ManagedE1000AsyncTxResult.Pending &&
              freshBackend.Driver.Ledger.Outstanding == 1,
              "new-episode-stale-id-rejected-without-mutation");
        freshBackend.Driver.CompleteAllCoalesced();
        Check(freshService.Poll() == NetworkOperationResult.Success &&
              freshBackend.Adapter.Outstanding == 0,
              "post-restart-service-pump-reclaims-without-rx");
        s_lifecycles++;
    }

    private static void RunMixedLifecycles()
    {
        Random random = new(unchecked((int)Seed));
        uint episode = 0x9310;
        FakeBackend backend = new(episode, available: true);
        ManagedNetworkService service = NewService(backend);
        uint identifier = 0x94000000;
        int accepted = 0;
        int restartOrdinal = 1;
        while (accepted < TargetLifecycles)
        {
            int count = Math.Min(1 + random.Next(4), TargetLifecycles - accepted);
            ulong[] batchIds = new ulong[count];
            for (int index = 0; index < count; ++index)
            {
                uint id = identifier++;
                QueueAndReceive(backend, service, id);
                Check(service.TrySendReceivedUdpEchoAsync(out batchIds[index]) ==
                      ManagedServiceAsyncTxSubmitResult.Accepted,
                      "mixed-service-accepted-" + accepted);
                accepted++;
            }
            int[] order = new int[count];
            for (int index = 0; index < count; ++index) order[index] = index;
            for (int index = order.Length - 1; index > 0; --index)
            {
                int other = random.Next(index + 1);
                (order[index], order[other]) = (order[other], order[index]);
            }
            foreach (int index in order)
                backend.Driver.SetDd(batchIds[index], dispatch: false,
                    hardwareError: (accepted + index) % 113 == 0);
            backend.Driver.DispatchTxdw();
            Check(service.Poll() == NetworkOperationResult.Success &&
                  backend.Adapter.Outstanding == 0 &&
                  backend.Driver.Ledger.Outstanding == 0,
                  "mixed-capacity-batch-drained-" + accepted);
            s_lifecycles += count;

            if (accepted >= 137 * restartOrdinal)
            {
                QueueAndReceive(backend, service, identifier++);
                Check(service.TrySendReceivedUdpEchoAsync(out ulong retiringId) ==
                      ManagedServiceAsyncTxSubmitResult.Accepted,
                      "mixed-restart-pending-accepted-" + accepted);
                backend.RetireAfterDmaQuiesced();
                Check(backend.Adapter.Outstanding == 0 &&
                      backend.Adapter.Statistics.Invalidated == 1,
                      "mixed-restart-pending-invalidated-" + accepted);
                Check(backend.TryConsumeServiceTxCompletion(retiringId) ==
                      ManagedServiceAsyncTxCompletionResult.InvalidOrStale,
                      "mixed-restart-old-id-rejected-" + accepted);
                ++episode;
                backend = new FakeBackend(episode, available: true);
                service = NewService(backend);
                s_lifecycles++;
                restartOrdinal++;
            }
        }
        backend.Driver.CompleteAllCoalesced();
        service.Poll();
        Check(backend.Adapter.Outstanding == 0 &&
              backend.Driver.Ledger.Outstanding == 0 &&
              backend.Adapter.Statistics.ReclamationFailures == 0,
              "mixed-run-final-drain-and-ownership-reconciled");
    }

    private static ManagedNetworkService NewService(FakeBackend backend)
    {
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        service.BeginBoot();
        Check(service.BindUdpEndpoint(ServicePort) == NetworkOperationResult.Started,
              "diagnostic-endpoint-binds");
        return service;
    }

    private static void QueueAndReceive(FakeBackend backend,
                                        ManagedNetworkService service,
                                        uint identifier)
    {
        backend.QueueRequest(identifier);
        Check(service.Poll() == NetworkOperationResult.Success &&
              service.HasReceivedUdp,
              "validated-udp-receive-slot-owned-0x" + identifier.ToString("X8"));
    }

    private static byte[] Payload(uint identifier)
    {
        byte[] payload = new byte[12];
        payload[0] = 0x93;
        payload[1] = 0xA5;
        payload[2] = 0x5A;
        payload[3] = 0xC3;
        payload[4] = (byte)(identifier >> 24);
        payload[5] = (byte)(identifier >> 16);
        payload[6] = (byte)(identifier >> 8);
        payload[7] = (byte)identifier;
        payload[8] = 0x91;
        payload[9] = 0x17;
        payload[10] = 0x00;
        payload[11] = 0xFF;
        return payload;
    }

    private static void ValidateReply(byte[] frame, byte[] expectedPayload)
    {
        Check(ManagedEthernetProtocol.TryParseFrame(frame, s_peerMac,
                  out ManagedEthernetFrame ethernet) &&
              ethernet.EtherType == ManagedIpv4Protocol.EtherType &&
              ethernet.Destination.SequenceEqual(s_peerMac) &&
              ethernet.Source.SequenceEqual(s_localMac),
              "reply-ethernet-addresses");
        Check(ManagedIpv4Protocol.TryParse(ethernet.Payload, out ManagedIpv4Packet ip) &&
              ip.TotalLength == 20 + 8 + expectedPayload.Length &&
              ip.Protocol == ManagedUdpProtocol.Protocol &&
              new Ipv4Address(ManagedEthernetProtocol.ReadUInt32Network(
                  ip.SourceAddress, 0)) == s_localIp &&
              new Ipv4Address(ManagedEthernetProtocol.ReadUInt32Network(
                  ip.DestinationAddress, 0)) == s_peerIp &&
              ip.HeaderChecksum != 0,
              "reply-ipv4-addresses-length-and-parse");
        Check(ManagedUdpProtocol.TryParse(ip.Payload, ip.SourceAddress,
                  ip.DestinationAddress, out ManagedUdpDatagram udp) &&
              udp.SourcePort == ServicePort && udp.DestinationPort == 24001 &&
              udp.Length == 8 + expectedPayload.Length &&
              udp.Payload.SequenceEqual(expectedPayload),
              "reply-udp-ports-length-checksum-and-payload");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + name);
        s_checks++;
    }

    private sealed class FakeBackend : IManagedNetworkServiceBackend,
        IManagedNetworkServiceAsyncTxBackend
    {
        private ManagedNetworkService? _service;
        private bool _requestPending;
        private byte[] _requestPayload = Array.Empty<byte>();
        private uint _requestIdentifier;
        private readonly byte[] _frame = new byte[ManagedEthernetProtocol.MaximumFrameLength];
        private readonly byte[] _ip = new byte[ManagedIpv4Protocol.MaximumPacketLength];
        private readonly byte[] _udp = new byte[ManagedUdpProtocol.MaximumDatagramLength];
        private readonly NetworkStatus _status = new(true, true, true, true,
            0x525400123456UL, s_localIp, new Ipv4Address(0xFFFFFF00), s_peerIp);

        internal FakeBackend(uint episode, bool available,
                             bool driverReady = true)
        {
            IsAvailable = available;
            Driver = new FakeDriver(episode, driverReady);
            Adapter = new ManagedNetworkServiceAsyncTxAdapter(Driver);
        }

        public bool IsAvailable { get; }
        internal FakeDriver Driver { get; }
        internal ManagedNetworkServiceAsyncTxAdapter Adapter { get; }
        internal byte[] LastSubmittedFrame { get; private set; } = Array.Empty<byte>();
        public ManagedTcpConnectionState TcpState => ManagedTcpConnectionState.Closed;
        public NetworkStatus GetStatus() => _status;
        public void SetRuntimeStatus(NetworkStatus status) { }
        internal void Attach(ManagedNetworkService service) => _service = service;

        internal void QueueRequest(uint identifier)
        {
            if (_requestPending) throw new InvalidOperationException("RX fixture busy");
            _requestIdentifier = identifier;
            _requestPayload = Payload(identifier);
            _requestPending = true;
        }

        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public bool TryGetResolved(out Ipv4Address address)
        {
            address = default;
            return false;
        }
        public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
        {
            Adapter.PumpCompletions();
            serviceEvent = ManagedNetworkServiceBackendEvent.None;
            if (!_requestPending) return true;
            _requestPending = false;
            if (_service == null || !_service.TryCaptureReceivedUdp(
                    s_peerIp, s_localIp, 24001, ServicePort,
                    s_peerMac, _requestPayload))
            {
                serviceEvent = ManagedNetworkServiceBackendEvent.UdpReceiveOverflow;
                return true;
            }
            serviceEvent = ManagedNetworkServiceBackendEvent.UdpReceived;
            return true;
        }
        public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult BindUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.Started;
        public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.Success;
        public ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination,
            ushort destinationPort, ushort sourcePort, ReadOnlySpan<byte> payload) =>
            ManagedNetworkServiceBackendResult.Success;
        public ManagedNetworkServiceBackendResult BeginTcpConnect(
            Ipv4Address destination, ushort destinationPort) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload) =>
            ManagedNetworkServiceBackendResult.Rejected;
        public ManagedNetworkServiceBackendResult CloseTcp() =>
            ManagedNetworkServiceBackendResult.Rejected;
        public bool Teardown() => true;

        public ManagedServiceAsyncTxSubmitResult SendReceivedUdpEchoAsync(
            Ipv4Address source, Ipv4Address destination,
            ushort sourcePort, ushort destinationPort,
            ReadOnlySpan<byte> sourceMac, ReadOnlySpan<byte> payload,
            out ulong requestId)
        {
            requestId = 0;
            if (!ManagedUdpEchoReplyFrameBuilder.TryBuild(
                    _frame, _ip, _udp, s_localMac, sourceMac,
                    destination, source, destinationPort, sourcePort,
                    payload, (ushort)_requestIdentifier, out ushort length))
                return ManagedServiceAsyncTxSubmitResult.Invalid;
            ManagedServiceAsyncTxSubmitResult result = Adapter.TrySubmitFrame(
                _frame, length, out requestId);
            if (result == ManagedServiceAsyncTxSubmitResult.Accepted)
                LastSubmittedFrame = Driver.LastDmaCopy;
            return result;
        }
        public ManagedServiceAsyncTxStatistics GetAsyncTxStatistics() =>
            Adapter.Statistics;
        public ManagedServiceAsyncTxCompletionResult TryGetServiceTxCompletion(
            ulong requestId) => Convert( Adapter.TryGetCompletion(requestId));
        public ManagedServiceAsyncTxCompletionResult TryConsumeServiceTxCompletion(
            ulong requestId) => Convert(Adapter.TryConsumeCompletion(requestId));

        internal void RetireAfterDmaQuiesced()
        {
            Adapter.CloseAdmission();
            Driver.BeginStopping();
            if (!Driver.Ledger.InvalidateAfterDmaQuiesced() ||
                !Adapter.OnDmaQuiesced(Driver.DriverEpisode))
                throw new InvalidOperationException("retire-contract");
        }

        private static ManagedServiceAsyncTxCompletionResult Convert(
            ManagedE1000AsyncTxResult value) => value switch
        {
            ManagedE1000AsyncTxResult.Pending =>
                ManagedServiceAsyncTxCompletionResult.Pending,
            ManagedE1000AsyncTxResult.Success =>
                ManagedServiceAsyncTxCompletionResult.Success,
            ManagedE1000AsyncTxResult.HardwareError =>
                ManagedServiceAsyncTxCompletionResult.HardwareError,
            ManagedE1000AsyncTxResult.AlreadyConsumed =>
                ManagedServiceAsyncTxCompletionResult.AlreadyConsumed,
            _ => ManagedServiceAsyncTxCompletionResult.InvalidOrStale
        };
    }

    private sealed class FakeDriver : IManagedE1000AsyncTxTransport
    {
        private readonly byte[][] _dma = new byte[ManagedE1000Protocol.RingCount][];
        private readonly byte[] _status = new byte[ManagedE1000Protocol.RingCount];
        private uint _nextIndex;
        private bool _ready;

        internal FakeDriver(uint episode, bool ready)
        {
            DriverEpisode = episode;
            _ready = ready;
            Ledger = new ManagedE1000AsyncTxLedger();
            if (!Ledger.StartEpisode(episode))
                throw new InvalidOperationException("ledger-episode");
            for (int index = 0; index < _dma.Length; ++index)
                _dma[index] = new byte[ManagedE1000Protocol.PacketBufferSize];
        }

        public uint DriverEpisode { get; }
        public bool IsAsyncTxReady => _ready;
        internal ManagedE1000AsyncTxLedger Ledger { get; }
        internal byte[] LastDmaCopy { get; private set; } = Array.Empty<byte>();

        public ManagedE1000AsyncTxSubmitResult TrySubmitFrame(
            byte[]? frame, int length, out ulong requestId)
        {
            requestId = 0;
            if (!_ready) return ManagedE1000AsyncTxSubmitResult.Unavailable;
            if (frame == null || length < ManagedEthernetProtocol.MinimumFrameLength ||
                length > frame.Length || length > ManagedE1000Protocol.PacketBufferSize)
                return ManagedE1000AsyncTxSubmitResult.InvalidArgument;
            if (Ledger.Outstanding >= ManagedE1000AsyncTxLedger.Capacity)
                return ManagedE1000AsyncTxSubmitResult.Busy;
            uint index = FindFreeIndex();
            if (index == uint.MaxValue || Ledger.TryPrepare(index, out requestId) !=
                ManagedE1000AsyncTxSubmitResult.Accepted)
                return ManagedE1000AsyncTxSubmitResult.Busy;
            frame.AsSpan(0, length).CopyTo(_dma[index]);
            LastDmaCopy = _dma[index].AsSpan(0, length).ToArray();
            Span<byte> descriptor = stackalloc byte[
                (int)ManagedE1000Protocol.DescriptorSize];
            if (!ManagedE1000Protocol.TryBuildTxDescriptor(descriptor,
                    0x100000UL + (ulong)index * ManagedE1000Protocol.PacketBufferSize,
                    (ushort)length) || !Ledger.MarkHardwareOwned(index))
            {
                Ledger.RollbackPrepared(index);
                requestId = 0;
                return ManagedE1000AsyncTxSubmitResult.Unavailable;
            }
            _status[index] = 0;
            _nextIndex = (index + 1) % ManagedE1000Protocol.RingCount;
            return ManagedE1000AsyncTxSubmitResult.Accepted;
        }

        public ManagedE1000AsyncTxResult TryGetTransmitCompletion(ulong requestId) =>
            Ledger.Query(requestId);
        public ManagedE1000AsyncTxResult TryConsumeTransmitCompletion(ulong requestId) =>
            Ledger.Consume(requestId);

        internal void SetDd(ulong requestId, bool dispatch,
                            bool hardwareError = false)
        {
            if (!ManagedE1000Protocol.TryDecodeAsyncTxRequestId(requestId,
                    ManagedE1000Protocol.RingCount, out uint index, out _,
                    out uint episode) || episode != DriverEpisode)
                throw new InvalidOperationException("request-decode");
            _status[index] = (byte)(ManagedE1000Protocol.TxStatusDone |
                (hardwareError ? ManagedE1000Protocol.TxStatusErrorMask : 0));
            if (dispatch && !Ledger.TryCompleteOwned(index, _status[index]))
                throw new InvalidOperationException("dd-completion");
        }

        internal int DispatchTxdw()
        {
            int completed = 0;
            for (uint index = 0; index != ManagedE1000Protocol.RingCount; ++index)
                if ((_status[index] & ManagedE1000Protocol.TxStatusDone) != 0 &&
                    Ledger.OwnershipAt(index) == ManagedE1000AsyncTxOwnership.HardwareOwned &&
                    Ledger.TryCompleteOwned(index, _status[index]))
                    completed++;
            return completed;
        }

        internal void CompleteAllCoalesced()
        {
            for (uint index = 0; index != ManagedE1000Protocol.RingCount; ++index)
                if (Ledger.OwnershipAt(index) == ManagedE1000AsyncTxOwnership.HardwareOwned)
                    _status[index] = ManagedE1000Protocol.TxStatusDone;
            DispatchTxdw();
        }

        internal void BeginStopping()
        {
            _ready = false;
            Ledger.BeginStopping();
        }

        private uint FindFreeIndex()
        {
            for (uint offset = 0; offset != ManagedE1000Protocol.RingCount; ++offset)
            {
                uint index = (_nextIndex + offset) % ManagedE1000Protocol.RingCount;
                if (Ledger.IsFree(index)) return index;
            }
            return uint.MaxValue;
        }
    }
}

internal static class KernelLog
{
    internal static bool Write(ReadOnlySpan<byte> value) => true;
    internal static bool WriteHexLine(ReadOnlySpan<byte> prefix, ulong value) => true;
    internal static bool WriteHexLine(ReadOnlySpan<byte> prefix, uint value) => true;
}

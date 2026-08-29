using System;
using System.Collections.Generic;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly Ipv4Address Local = new(0x0A0F002AU);
    private static readonly Ipv4Address Peer = new(0x0A0F0002U);
    private static readonly ManagedX509UtcTime TestTime =
        new(2028, 1, 1, 0, 0, 0);
    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestTlsTransportBoundaryAndSni();
            TestEndToEndFragmentation();
            TestFailureMatrix();
            TestTeardownAndReuse();
            Console.WriteLine($"MANAGED_KERNEL_PHASE32_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE32_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestTlsTransportBoundaryAndSni()
    {
        ManagedTls12Client client = CreateTlsClient("www.example.com"u8);
        byte[] hello = new byte[512];
        Check(client.TryStart(hello, out int length), "tls-start-through-boundary");
        Check(Equal(hello.AsSpan(5, ManagedTls12Phase31Fixtures.ClientHello.Length),
                    ManagedTls12Phase31Fixtures.ClientHello), "sni-clienthello-exact");
        Check(Contains(hello.AsSpan(5, length - 5), "www.example.com"u8),
              "sni-requested-hostname-present");

        byte[] badRecord = (byte[])ManagedTls12Phase31Fixtures.ServerHelloRecord.Clone();
        badRecord[0] = ManagedTls12RecordProtection.ApplicationData;
        Check(!client.TryConsume(badRecord) &&
              client.State == ManagedTls12ClientState.Failed,
              "unexpected-record-fails-closed");
        client.Teardown();
    }

    private static void TestEndToEndFragmentation()
    {
        FakeBackend backend = new(FakeScenario.Normal);
        ManagedNetworkService service = CreateService(backend);
        ManagedHttpsClient client = CreateHttpsClient(service, backend);
        Check(client.BeginGet("www.example.com"u8, "/phase32"u8) ==
              NetworkOperationResult.Started, "https-begin-resolve");
        bool sawDns = false;
        bool sawTcp = false;
        bool sawTls = false;
        bool sawRequest = false;
        bool sawApplication = false;
        for (int count = 0; count != 512 &&
             client.State != ManagedHttpsClientState.Succeeded; ++count)
        {
            CheckPoll(client.Poll(), "https-progress");
            sawDns |= client.ResolvedAddress.IsUsable;
            sawTcp |= client.State >= ManagedHttpsClientState.Handshaking;
            sawTls |= client.TlsAuthenticated;
            sawRequest |= client.RequestSent;
            sawApplication |= client.ApplicationDataReceived;
        }
        Check(client.State == ManagedHttpsClientState.Succeeded,
              "https-complete-over-tcp");
        Check(sawDns && sawTcp && sawTls && sawRequest && sawApplication,
              "https-layer-progress-markers");
        Check(client.StatusCode == 200 && client.ResponseBodyComplete,
              "https-http-status-and-body-framing");
        byte[] body = new byte[ManagedHttpLimits.MaximumBodyCapacity];
        Check(client.TryCopyResponseBody(body, out int bodyLength) &&
              bodyLength == 17 && Equal(body.AsSpan(0, bodyLength),
                                        "phase32-http-pass"u8),
              "https-body-delivery");
        Check(backend.SawTlsApplicationRequest && backend.SawExpectedSni,
              "peer-authenticated-encrypted-request");
        Check(backend.SawMultipleTlsRecords && backend.SawTcpRecordFragmentation,
              "record-and-tcp-fragmentation-observed");
        Check(client.Reset() == NetworkOperationResult.Success &&
              client.State == ManagedHttpsClientState.Idle,
              "https-reset-after-success");
    }

    private static void TestFailureMatrix()
    {
        Check(Fails(FakeScenario.DnsFailure,
                    ManagedHttpsFailureReason.DnsFailure), "dns-failure");
        Check(Fails(FakeScenario.ConnectFailure,
                    ManagedHttpsFailureReason.TcpConnectFailure), "tcp-connect-failure");
        Check(Fails(FakeScenario.TcpReset,
                    ManagedHttpsFailureReason.TcpReset), "tcp-reset-during-tls");
        Check(Fails(FakeScenario.CloseMidRecord,
                    ManagedHttpsFailureReason.TlsProtocolFailure), "tcp-close-mid-record");
        Check(Fails(FakeScenario.BadFinished,
                    ManagedHttpsFailureReason.TlsAuthenticationFailure), "bad-finished");
        Check(Fails(FakeScenario.BadApplicationTag,
                    ManagedHttpsFailureReason.TlsAuthenticationFailure), "bad-application-tag");
        Check(Fails(FakeScenario.MalformedHttp,
                    ManagedHttpsFailureReason.HttpParseFailure), "malformed-http");
        Check(Fails(FakeScenario.OversizedHttp,
                    ManagedHttpsFailureReason.HttpParseFailure), "http-body-bound");
        Check(Fails(FakeScenario.UnexpectedApplication,
                    ManagedHttpsFailureReason.TlsProtocolFailure),
              "application-before-handshake");
        Check(HostnameMismatchFails(), "hostname-mismatch");
        Check(UntrustedRootFails(), "invalid-certificate-chain");
        Check(SequenceFailureFails(), "sequence-dependent-tag-failure");
    }

    private static void TestTeardownAndReuse()
    {
        FakeBackend backend = new(FakeScenario.Normal);
        ManagedNetworkService service = CreateService(backend);
        ManagedHttpsClient client = CreateHttpsClient(service, backend);
        Check(RunToSuccess(client, "/phase32"u8), "reuse-first-request");
        Check(client.Reset() == NetworkOperationResult.Success, "reuse-reset");
        backend.ResetForNextConnection();
        NetworkOperationResult secondBegin = client.BeginGet("www.example.com"u8, "/phase32"u8);
        bool secondSuccess = RunUntilTerminal(client) ==
                             ManagedHttpsClientState.Succeeded &&
                             client.StatusCode == 200;
        Check(secondBegin == NetworkOperationResult.Started && secondSuccess,
              "reuse-second-request");
        Check(client.Reset() == NetworkOperationResult.Success &&
              service.TcpState == NetworkTcpState.Closed,
              "reuse-no-stale-tcp-state");

        FakeBackend failedBackend = new(FakeScenario.BadApplicationTag);
        ManagedNetworkService failedService = CreateService(failedBackend);
        ManagedHttpsClient failed = CreateHttpsClient(failedService, failedBackend);
        Check(failed.BeginGet("www.example.com"u8, "/phase32"u8) ==
              NetworkOperationResult.Started &&
              RunUntilTerminal(failed) == ManagedHttpsClientState.Failed &&
              failedService.TcpState == NetworkTcpState.Closed,
              "failure-teardown-releases-tcp");
        Check(failed.Reset() == NetworkOperationResult.Success,
              "failure-reset");
    }

    private static bool Fails(FakeScenario scenario,
                              ManagedHttpsFailureReason expected)
    {
        FakeBackend backend = new(scenario);
        ManagedNetworkService service = CreateService(backend);
        ManagedHttpsClient client = CreateHttpsClient(service, backend);
        if (client.BeginGet("www.example.com"u8, "/phase32"u8) !=
            NetworkOperationResult.Started)
            return false;
        RunUntilTerminal(client);
        return client.State == ManagedHttpsClientState.Failed &&
               client.FailureReason == expected &&
               service.TcpState == NetworkTcpState.Closed;
    }

    private static bool HostnameMismatchFails()
    {
        FakeBackend backend = new(FakeScenario.Normal);
        ManagedNetworkService service = CreateService(backend);
        ManagedHttpsClient client = CreateHttpsClient(service, backend);
        NetworkOperationResult begin = client.BeginGet("mismatch.example.com"u8, "/phase32"u8);
        RunUntilTerminal(client);
        return begin == NetworkOperationResult.Started &&
               client.State == ManagedHttpsClientState.Failed &&
               client.FailureReason == ManagedHttpsFailureReason.TlsAuthenticationFailure;
    }

    private static bool UntrustedRootFails()
    {
        FakeBackend backend = new(FakeScenario.Normal);
        ManagedNetworkService service = CreateService(backend);
        byte[] root = (byte[])ManagedTls12Phase31Fixtures.Root.Clone();
        root[240] ^= 1;
        ManagedHttpsClient client = CreateHttpsClient(service, backend, root);
        if (client.BeginGet("www.example.com"u8, "/phase32"u8) !=
            NetworkOperationResult.Started)
            return false;
        RunUntilTerminal(client);
        return client.State == ManagedHttpsClientState.Failed &&
               client.FailureReason == ManagedHttpsFailureReason.TlsAuthenticationFailure;
    }

    private static bool SequenceFailureFails()
    {
        byte[] key = ManagedTls12Phase31Fixtures.KeyBlock[16..32];
        byte[] iv = ManagedTls12Phase31Fixtures.KeyBlock[36..40];
        byte[] record = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
        Check(ManagedTls12RecordProtection.TryEncrypt(2, key, iv,
            ManagedTls12RecordProtection.ApplicationData, "bad-sequence"u8,
            record, out int length), "sequence-control-build");
        byte[] plaintext = new byte[32];
        return !ManagedTls12RecordProtection.TryDecrypt(1, key, iv,
            ManagedTls12RecordProtection.ApplicationData, record.AsSpan(0, length),
            plaintext, out _);
    }

    private static bool RunToSuccess(ManagedHttpsClient client,
                                     ReadOnlySpan<byte> path)
    {
        if (client.BeginGet("www.example.com"u8, path) !=
            NetworkOperationResult.Started)
            return false;
        RunUntilTerminal(client);
        return client.State == ManagedHttpsClientState.Succeeded &&
               client.StatusCode == 200;
    }

    private static ManagedHttpsClientState RunUntilTerminal(ManagedHttpsClient client)
    {
        for (int count = 0; count != 512 &&
             client.State != ManagedHttpsClientState.Succeeded &&
             client.State != ManagedHttpsClientState.Failed; ++count)
            client.Poll();
        return client.State;
    }

    private static ManagedTls12Client CreateTlsClient(ReadOnlySpan<byte> hostname)
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        ManagedSecureRandom random = new(new FixedEntropy(entropy));
        if (!ManagedTls12Client.TryCreate(hostname, ManagedTls12Phase31Fixtures.Root,
                in TestTime, random,
                new byte[ManagedTls12Client.CertificateStorageBytes],
                out ManagedTls12Client? client) || client == null)
            throw new InvalidOperationException("TLS client creation failed.");
        return client;
    }

    private static ManagedNetworkService CreateService(FakeBackend backend)
    {
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        return service;
    }

    private static ManagedHttpsClient CreateHttpsClient(
        ManagedNetworkService service, FakeBackend backend,
        byte[]? root = null)
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        ManagedSecureRandom random = new(new FixedEntropy(entropy));
        return new ManagedHttpsClient(service,
            root ?? ManagedTls12Phase31Fixtures.Root, in TestTime, random);
    }

    private static void CheckPoll(NetworkOperationResult result, string name)
    {
        Check(result == NetworkOperationResult.Success, name);
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException("failed: " + name);
    }

    private static bool Equal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        ManagedCryptoComparison.FixedTimeEquals(left, right);

    private static bool Contains(ReadOnlySpan<byte> value, ReadOnlySpan<byte> needle)
    {
        for (int offset = 0; offset <= value.Length - needle.Length; ++offset)
            if (value.Slice(offset, needle.Length).SequenceEqual(needle)) return true;
        return false;
    }

    private enum FakeScenario : byte
    {
        Normal,
        DnsFailure,
        ConnectFailure,
        TcpReset,
        CloseMidRecord,
        BadFinished,
        BadApplicationTag,
        MalformedHttp,
        OversizedHttp,
        UnexpectedApplication
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;
        internal FixedEntropy(byte[] bytes) => _bytes = bytes;
        public bool IsAvailable => _bytes.Length != 0;
        public bool TryFill(Span<byte> destination)
        {
            for (int index = 0; index != destination.Length; ++index)
                destination[index] = _bytes[_offset++ % _bytes.Length];
            return true;
        }
    }

    private sealed class FakeBackend : IManagedNetworkServiceBackend
    {
        private readonly FakeScenario _scenario;
        private readonly List<byte[]> _queued = new();
        private ManagedNetworkService? _service;
        private ManagedNetworkServiceBackendEvent _event;
        private bool _eventPending;
        private ManagedTcpConnectionState _tcpState;
        private int _queueIndex;
        private bool _finQueued;
        private bool _serverFlightQueued;
        private bool _serverApplicationQueued;
        internal bool SawTlsApplicationRequest { get; private set; }
        internal bool SawExpectedSni { get; private set; }
        internal bool SawMultipleTlsRecords { get; private set; }
        internal bool SawTcpRecordFragmentation { get; private set; }

        internal FakeBackend(FakeScenario scenario) => _scenario = scenario;
        internal void Attach(ManagedNetworkService service) => _service = service;
        public bool IsAvailable => true;
        public NetworkStatus GetStatus() => new(true, true, true, true,
            0x021500000002, Local, new Ipv4Address(0xFFFFFF00),
            new Ipv4Address(0x0A0F0001));
        public void SetRuntimeStatus(NetworkStatus status) { }

        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
            _event = _scenario == FakeScenario.DnsFailure
                ? ManagedNetworkServiceBackendEvent.DnsNxDomain
                : ManagedNetworkServiceBackendEvent.DnsResolved;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool TryGetResolved(out Ipv4Address address)
        {
            address = Peer;
            return _scenario != FakeScenario.DnsFailure;
        }

        public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
        {
            if (_tcpState == ManagedTcpConnectionState.LastAck)
                _tcpState = ManagedTcpConnectionState.TimeWait;
            if (_eventPending)
            {
                serviceEvent = _event;
                _eventPending = false;
                if (serviceEvent == ManagedNetworkServiceBackendEvent.TcpEstablished)
                    _tcpState = ManagedTcpConnectionState.Established;
                return true;
            }
            if (_scenario == FakeScenario.TcpReset &&
                _tcpState == ManagedTcpConnectionState.Established)
            {
                _tcpState = ManagedTcpConnectionState.Failed;
                serviceEvent = ManagedNetworkServiceBackendEvent.TcpFailed;
                return true;
            }
            if (_queueIndex < _queued.Count && _service != null)
            {
                byte[] chunk = _queued[_queueIndex++];
                if (!((IManagedTcpApplicationSink)_service).TryCaptureReceivedTcp(
                        Peer, Local, 443, ManagedTcpConnection.ClientPort, chunk))
                {
                    serviceEvent = ManagedNetworkServiceBackendEvent.None;
                    return true;
                }
                if (chunk.Length < 5) SawTcpRecordFragmentation = true;
                serviceEvent = ManagedNetworkServiceBackendEvent.TcpReceived;
                return true;
            }
            if (_finQueued)
            {
                _finQueued = false;
                _tcpState = ManagedTcpConnectionState.CloseWait;
                serviceEvent = ManagedNetworkServiceBackendEvent.TcpClosed;
                return true;
            }
            serviceEvent = ManagedNetworkServiceBackendEvent.None;
            return true;
        }

        public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult BindUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.Rejected;
        public ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination,
            ushort destinationPort, ushort sourcePort, ReadOnlySpan<byte> payload) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedTcpConnectionState TcpState => _tcpState;

        public ManagedNetworkServiceBackendResult BeginTcpConnect(
            Ipv4Address destination, ushort destinationPort)
        {
            if (_scenario == FakeScenario.ConnectFailure)
                return ManagedNetworkServiceBackendResult.Failed;
            _tcpState = ManagedTcpConnectionState.SynSent;
            _event = ManagedNetworkServiceBackendEvent.TcpEstablished;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
        {
            if (payload.Length >= 5 &&
                payload[0] == ManagedTls12RecordProtection.Handshake &&
                payload[5] == 1)
            {
                SawExpectedSni = Contains(payload, "www.example.com"u8);
                if (_scenario == FakeScenario.UnexpectedApplication)
                {
                    Queue(ManagedTls12Phase31Fixtures.ServerApplicationRecord);
                    return ManagedNetworkServiceBackendResult.Success;
                }
                QueueServerHandshake();
                return ManagedNetworkServiceBackendResult.Success;
            }
            if (!_serverFlightQueued && payload.Length >= 5 &&
                payload[0] == ManagedTls12RecordProtection.Handshake)
            {
                _serverFlightQueued = true;
                byte[] ccs = ManagedTls12Phase31Fixtures.ChangeCipherSpec;
                Queue(ccs);
                byte[] finished = (byte[])ManagedTls12Phase31Fixtures.ServerFinishedRecord.Clone();
                if (_scenario == FakeScenario.BadFinished) finished[^1] ^= 1;
                QueueRecordFragments(finished);
                return ManagedNetworkServiceBackendResult.Success;
            }
            if (!_serverApplicationQueued && payload.Length >= 5 &&
                payload[0] == ManagedTls12RecordProtection.ApplicationData)
            {
                byte[] request = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
                if (!ManagedTls12RecordProtection.TryDecrypt(1,
                        ManagedTls12Phase31Fixtures.KeyBlock[..16],
                        ManagedTls12Phase31Fixtures.KeyBlock[32..36],
                        ManagedTls12RecordProtection.ApplicationData, payload,
                        request, out int requestLength))
                    return ManagedNetworkServiceBackendResult.Failed;
                SawTlsApplicationRequest =
                    AsAscii(request, requestLength).StartsWith("GET /phase32",
                        StringComparison.Ordinal);
                _serverApplicationQueued = true;
                QueueResponse();
                return ManagedNetworkServiceBackendResult.Success;
            }
            return ManagedNetworkServiceBackendResult.Success;
        }

        public ManagedNetworkServiceBackendResult CloseTcp()
        {
            _tcpState = ManagedTcpConnectionState.LastAck;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool Teardown()
        {
            _tcpState = ManagedTcpConnectionState.Closed;
            _queued.Clear();
            _queueIndex = 0;
            _finQueued = false;
            return true;
        }

        internal void ResetForNextConnection()
        {
            _queued.Clear();
            _queueIndex = 0;
            _tcpState = ManagedTcpConnectionState.Closed;
            _eventPending = false;
            _serverFlightQueued = false;
            _serverApplicationQueued = false;
            _finQueued = false;
            SawTlsApplicationRequest = false;
            SawExpectedSni = false;
            SawMultipleTlsRecords = false;
            SawTcpRecordFragmentation = false;
        }

        private void QueueServerHandshake()
        {
            if (_scenario == FakeScenario.CloseMidRecord)
            {
                byte[] partial = ManagedTls12Phase31Fixtures.ServerHelloRecord[..3];
                Queue(partial);
                _finQueued = true;
                return;
            }
            for (int index = 0; index != ManagedTls12Phase31Fixtures.ServerRecordCount; ++index)
                QueueRecordFragments(ManagedTls12Phase31Fixtures.GetServerRecord(index));
        }

        private void QueueResponse()
        {
            string[] parts =
            {
                "HTTP/1.1 200",
                " OK\r\nContent-Length: 17\r\nConnection: close\r\n",
                "Content-Type: text/plain\r\n\r\nphase32-",
                "http-pass"
            };
            if (_scenario == FakeScenario.MalformedHttp)
            {
                QueueOneResponseRecord(1, "HTTP/1.1 2x0\r\n"u8.ToArray());
                _finQueued = true;
                return;
            }
            if (_scenario == FakeScenario.OversizedHttp)
            {
                QueueOneResponseRecord(1,
                    System.Text.Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\nContent-Length: 257\r\nConnection: close\r\n\r\n"));
                _finQueued = true;
                return;
            }
            byte[][] records = new byte[parts.Length][];
            for (int index = 0; index != parts.Length; ++index)
                records[index] = BuildResponseRecordBytes((ulong)(index + 1),
                                                           parts[index]);
            QueueRecordFragments(records[0]);
            byte[] combined = new byte[records[1].Length + records[2].Length];
            records[1].CopyTo(combined, 0);
            records[2].CopyTo(combined, records[1].Length);
            Queue(combined);
            SawMultipleTlsRecords = true;
            QueueRecordFragments(records[3]);
            if (_scenario == FakeScenario.BadApplicationTag && _queued.Count != 0)
                _queued[^1][^1] ^= 1;
            _finQueued = true;
        }

        private static bool BuildResponseRecord(ulong sequence, byte[] plaintext,
                                                 byte[] destination, out int length)
        {
            return ManagedTls12RecordProtection.TryEncrypt(
                sequence, ManagedTls12Phase31Fixtures.KeyBlock[16..32],
                ManagedTls12Phase31Fixtures.KeyBlock[36..40],
                ManagedTls12RecordProtection.ApplicationData, plaintext,
                destination, out length);
        }

        private static byte[] BuildResponseRecordBytes(ulong sequence, string plaintext)
        {
            byte[] record = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
            if (!BuildResponseRecord(sequence,
                    System.Text.Encoding.ASCII.GetBytes(plaintext), record,
                    out int length))
                throw new InvalidOperationException("response record build failed");
            return record[..length];
        }

        private void QueueOneResponseRecord(ulong sequence, ReadOnlySpan<byte> plaintext)
        {
            byte[] record = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
            if (!BuildResponseRecord(sequence, plaintext.ToArray(), record,
                                     out int length))
                throw new InvalidOperationException("response record build failed");
            QueueRecordFragments(record[..length]);
        }

        private void QueueRecordFragments(byte[] record)
        {
            if (record.Length < 3)
            {
                Queue(record);
                return;
            }
            Queue(record[..2]);
            Queue(record[2..]);
            SawTcpRecordFragmentation = true;
        }

        private void Queue(byte[] bytes)
        {
            _queued.Add((byte[])bytes.Clone());
        }

        private static string AsAscii(byte[] bytes, int length) =>
            System.Text.Encoding.ASCII.GetString(bytes, 0, length);
    }
}

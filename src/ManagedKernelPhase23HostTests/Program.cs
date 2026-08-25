using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly Ipv4Address Local = new(0x0A0F002AU);
    private static readonly Ipv4Address Peer = new(0x0A0F0002U);
    private static int s_cases;

    private static void Main()
    {
        TestRequestBuilder();
        TestStatusAndFraming();
        TestHeaders();
        TestBodyAndClose();
        TestClientLifecycle();
        Console.WriteLine($"MANAGED_KERNEL_PHASE23_HOST_TESTS_PASS cases={s_cases}");
    }

    private static void TestRequestBuilder()
    {
        byte[] request = new byte[ManagedHttpLimits.MaximumSerializedRequestSize];
        Check(ManagedHttpRequestBuilder.TryBuildGet("phase23.test"u8, "/phase23"u8,
                  request, out int length) &&
              Ascii(request, length) ==
                  "GET /phase23 HTTP/1.1\r\nHost: phase23.test\r\nConnection: close\r\n\r\n",
              "request-canonical-get");
        Check(Ascii(request, length).Contains("Host: phase23.test\r\n") &&
              Ascii(request, length).EndsWith("Connection: close\r\n\r\n",
                  StringComparison.Ordinal), "request-required-headers-crlf");

        byte[] host = new byte[ManagedHttpLimits.MaximumHostnameLength];
        host.AsSpan().Fill((byte)'a');
        host[63] = (byte)'.';
        host[127] = (byte)'.';
        host[191] = (byte)'.';
        Check(ManagedHttpRequestBuilder.TryBuildGet(host, "/"u8, request, out _),
              "request-maximum-hostname");
        byte[] tooLongHost = new byte[ManagedHttpLimits.MaximumHostnameLength + 1];
        tooLongHost.AsSpan().Fill((byte)'a');
        Check(!ManagedHttpRequestBuilder.TryBuildGet(tooLongHost, "/"u8,
                  request, out _), "request-hostname-one-over-limit");
        byte[] path = new byte[ManagedHttpLimits.MaximumPathLength];
        path[0] = (byte)'/';
        path.AsSpan(1).Fill((byte)'a');
        Check(ManagedHttpRequestBuilder.TryBuildGet("x.test"u8, path, request,
                  out _), "request-maximum-path");
        byte[] tooLongPath = new byte[ManagedHttpLimits.MaximumPathLength + 1];
        tooLongPath[0] = (byte)'/';
        tooLongPath.AsSpan(1).Fill((byte)'a');
        Check(!ManagedHttpRequestBuilder.TryBuildGet("x.test"u8, tooLongPath,
                  request, out _), "request-path-one-over-limit");
        Check(!ManagedHttpRequestBuilder.TryBuildGet("x.test"u8, "bad"u8,
                  request, out _), "request-origin-form-required");
        Check(!ManagedHttpRequestBuilder.TryBuildGet("bad host"u8, "/"u8,
                  request, out _), "request-hostname-grammar");
        Check(!ManagedHttpRequestBuilder.TryBuildGet("x.test"u8, "/bad path"u8,
                  request, out _), "request-path-control-space-rejected");
    }

    private static void TestStatusAndFraming()
    {
        ManagedHttpResponseParser parser = new();
        Check(parser.Feed("HTTP/1.1 200"u8) &&
              parser.Feed(" OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8) &&
              parser.StatusCode == 200 && parser.IsBodyComplete,
              "status-segmented-200");
        Check(parser.NotifyConnectionClosed() && parser.State == ManagedHttpParseState.Closed,
              "status-zero-body-close");

        parser.Reset();
        Check(parser.Feed("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8) &&
              parser.StatusCode == 404, "status-valid-404");
        parser.Reset();
        Check(!Feed(parser, "HTTP/1.0 200 OK\r\n"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.StatusLine,
              "status-version-rejected");
        parser.Reset();
        Check(!Feed(parser, "HTTP/1.1 2x0 OK\r\n"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.StatusCode,
              "status-numeric-code-rejected");
        parser.Reset();
        byte[] longStatus = new byte[ManagedHttpLimits.MaximumStatusLineLength + 1];
        longStatus.AsSpan().Fill((byte)'A');
        Check(!parser.Feed(longStatus) &&
              parser.FailureReason == ManagedHttpParseFailureReason.StatusLineOverflow,
              "status-line-overflow");
        parser.Reset();
        Check(!parser.Feed("HTTP/1.1 200 OK\n"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.LineFraming,
              "status-lone-lf-rejected");
    }

    private static void TestHeaders()
    {
        ManagedHttpResponseParser parser = new();
        Check(parser.Feed("HTTP/1.1 200 OK\r\ncontent-length: 3\r\nX-Ignored: yes\r\nCoNnEcTiOn: close\r\n\r\nabc"u8) &&
              parser.ContentLength == 3 && parser.HeaderCount == 3 &&
              parser.IsBodyComplete, "headers-case-insensitive-and-unknown");

        parser.Reset();
        Check(parser.Feed("HTTP/1.1 200 OK\r\nContent-Length: 3\r\n"u8) &&
              parser.Feed("Connection: close\r\n\r"u8) &&
              parser.Feed("\nabc"u8) && parser.IsBodyComplete,
              "headers-and-terminator-segmented");
        parser.Reset();
        Check(parser.Feed("HTTP/1.1 200 OK\r\nContent-Length: 3\r\n"u8) &&
              parser.Feed("Content-Length: 3\r\nConnection: close\r\n\r\nabc"u8),
              "headers-matching-duplicate-content-length");
        parser.Reset();
        Check(!Feed(parser, "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nContent-Length: 4\r\n"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.ConflictingContentLength,
              "headers-conflicting-content-length");
        parser.Reset();
        Check(!Feed(parser, "HTTP/1.1 200 OK\r\nContent-Length: x\r\n"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.ContentLength,
              "headers-malformed-content-length");
        parser.Reset();
        Check(!Feed(parser, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.UnsupportedTransferEncoding,
              "headers-chunked-rejected");
        parser.Reset();
        Check(!Feed(parser, "HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.MissingConnectionClose,
              "headers-connection-close-required");

        parser.Reset();
        for (int index = 0; index != 15; ++index)
        {
            if (!parser.Feed(index == 0
                ? "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n"u8
                : "X-A: b\r\n"u8))
                throw new InvalidOperationException("header boundary setup failed");
        }
        Check(parser.Feed("\r\n"u8), "headers-count-boundary");
        parser.Reset();
        byte[] hugeHeader = new byte[ManagedHttpLimits.MaximumHeaderLineLength + 1];
        hugeHeader.AsSpan().Fill((byte)'a');
        Check(parser.Feed("HTTP/1.1 200 OK\r\n"u8) &&
              !parser.Feed(hugeHeader) &&
              parser.FailureReason == ManagedHttpParseFailureReason.HeaderLineOverflow,
              "headers-line-overflow");
        parser.Reset();
        Check(parser.Feed("HTTP/1.1 200"u8), "headers-incremental-prefix-accepted");
        Check(hugeHeader.Length == ManagedHttpLimits.MaximumHeaderLineLength + 1,
              "headers-line-capacity-visible");
    }

    private static void TestBodyAndClose()
    {
        ManagedHttpResponseParser parser = new();
        Check(parser.Feed("HTTP/1.1 200 OK\r\nContent-Length: 6\r\nConnection: close\r\n\r\n"u8) &&
              parser.Feed("ab"u8) && parser.Feed("cd"u8) && parser.Feed("ef"u8) &&
              parser.BodyLength == 6 && parser.IsBodyComplete &&
              parser.NotifyConnectionClosed(), "body-fragmented-exact-close");
        byte[] body = new byte[6];
        Check(parser.TryCopyBody(body, out int length) && length == 6 &&
              Ascii(body, length) == "abcdef", "body-copy-bounded");
        parser.Reset();
        Check(parser.Feed("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8) &&
              parser.NotifyConnectionClosed(), "body-zero-length");
        parser.Reset();
        Check(!Feed(parser, "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\nabcd"u8) &&
              parser.FailureReason == ManagedHttpParseFailureReason.BodyExceedsContentLength,
              "body-exceeds-declared-length");
        parser.Reset();
        Check(parser.Feed("HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\nab"u8) &&
              !parser.NotifyConnectionClosed() &&
              parser.FailureReason == ManagedHttpParseFailureReason.PrematureConnectionClose,
              "body-premature-close");
        parser.Reset();
        byte[] oversizedLength = System.Text.Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Length: {ManagedHttpLimits.MaximumBodyCapacity + 1}\r\n");
        Check(!Feed(parser, oversizedLength) &&
              parser.FailureReason == ManagedHttpParseFailureReason.BodyTooLarge,
              "body-capacity-rejected");
    }

    private static void TestClientLifecycle()
    {
        FakeBackend backend = new();
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend._service = service;
        ManagedHttpClient client = new(service);
        Check(client.BeginGet("phase23.test"u8, "/phase23"u8) ==
                  NetworkOperationResult.Started, "client-begin-dns");
        Check(client.Poll() == NetworkOperationResult.Success &&
              client.State == ManagedHttpClientState.Connecting, "client-dns-success");
        Check(client.Poll() == NetworkOperationResult.Success &&
              client.State == ManagedHttpClientState.Receiving && client.RequestSent &&
              Ascii(backend.LastTcpPayload, backend.LastTcpPayloadLength) ==
                  "GET /phase23 HTTP/1.1\r\nHost: phase23.test\r\nConnection: close\r\n\r\n",
              "client-tcp-connect-and-request");

        byte[] response = "HTTP/1.1 200 OK\r\nContent-Length: 17\r\nConnection: close\r\nContent-Type: text/plain\r\n\r\nphase23-http-pass"u8.ToArray();
        for (int offset = 0; offset != response.Length; )
        {
            int count = Math.Min(7, response.Length - offset);
            backend.QueuePayload(response.AsSpan(offset, count));
            Check(client.Poll() == NetworkOperationResult.Success,
                  "client-segmented-response-poll");
            offset += count;
        }
        Check(client.StatusCode == 200 && client.ResponseBodyComplete,
              "client-response-parsed");
        backend.QueueFin();
        Check(client.Poll() == NetworkOperationResult.Success &&
              client.State == ManagedHttpClientState.Closing, "client-peer-fin-close");
        Check(client.Poll() == NetworkOperationResult.Success &&
              client.State == ManagedHttpClientState.Succeeded, "client-teardown-complete");
        byte[] body = new byte[ManagedHttpLimits.MaximumBodyCapacity];
        Check(client.TryCopyResponseBody(body, out int bodyLength) && bodyLength == 17 &&
              Ascii(body, bodyLength) == "phase23-http-pass", "client-body-delivery");
        GC.Collect();
        Check(client.State == ManagedHttpClientState.Succeeded &&
              client.StatusCode == 200, "client-gc-survival");

        Check(client.Reset() == NetworkOperationResult.Success &&
              client.State == ManagedHttpClientState.Idle, "client-reset-reuse");

        FakeBackend dnsFailureBackend = new() { ResolveResult = false };
        ManagedNetworkService dnsFailureService = ManagedNetworkService.CreateForTests(dnsFailureBackend);
        dnsFailureBackend._service = dnsFailureService;
        ManagedHttpClient dnsFailure = new(dnsFailureService);
        Check(dnsFailure.BeginGet("missing.test"u8, "/"u8) == NetworkOperationResult.Started &&
              dnsFailure.Poll() == NetworkOperationResult.Failed &&
              dnsFailure.State == ManagedHttpClientState.Failed &&
              dnsFailure.FailureReason == ManagedHttpFailureReason.DnsFailure,
              "client-dns-failure");

        FakeBackend resetBackend = new() { ConnectResult = false };
        ManagedNetworkService resetService = ManagedNetworkService.CreateForTests(resetBackend);
        resetBackend._service = resetService;
        ManagedHttpClient resetClient = new(resetService);
        Check(resetClient.BeginGet("phase23.test"u8, "/"u8) == NetworkOperationResult.Started &&
              resetClient.Poll() == NetworkOperationResult.Failed &&
              resetClient.State == ManagedHttpClientState.Failed &&
              resetClient.FailureReason == ManagedHttpFailureReason.TcpConnectFailure,
              "client-connect-failure");

        FakeBackend cancelBackend = new();
        ManagedNetworkService cancelService = ManagedNetworkService.CreateForTests(cancelBackend);
        cancelBackend._service = cancelService;
        ManagedHttpClient cancelClient = new(cancelService);
        Check(cancelClient.BeginGet("phase23.test"u8, "/"u8) == NetworkOperationResult.Started &&
              cancelClient.Cancel() == NetworkOperationResult.Success &&
              cancelClient.State == ManagedHttpClientState.Cancelled,
              "client-cancel-teardown");
    }

    private static bool Feed(ManagedHttpResponseParser parser, ReadOnlySpan<byte> value) =>
        parser.Feed(value);

    private static string Ascii(byte[] value, int length) =>
        System.Text.Encoding.ASCII.GetString(value, 0, length);

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"FAIL: {name}");
        s_cases++;
        Console.WriteLine($"PASS: {name}");
    }

    private sealed class FakeBackend : IManagedNetworkServiceBackend
    {
        private ManagedNetworkServiceBackendEvent _event;
        private bool _eventPending;
        private ManagedTcpConnectionState _tcpState;
        private Ipv4Address _resolved = Peer;
        internal bool ResolveResult = true;
        internal bool ConnectResult = true;
        internal readonly byte[] LastTcpPayload = new byte[512];
        internal int LastTcpPayloadLength;
        public bool IsAvailable => true;
        public NetworkStatus GetStatus() => new(true, true, true, true, 0x021500000002,
                                                 Local, new Ipv4Address(0xFFFFFF00),
                                                 new Ipv4Address(0x0A0F0001));
        public void SetRuntimeStatus(NetworkStatus status) { }
        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
            _event = ResolveResult ? ManagedNetworkServiceBackendEvent.DnsResolved
                                    : ManagedNetworkServiceBackendEvent.DnsNxDomain;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }
        public bool TryGetResolved(out Ipv4Address address) { address = _resolved; return ResolveResult; }
        public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
        {
            if (_tcpState == ManagedTcpConnectionState.LastAck)
                _tcpState = ManagedTcpConnectionState.TimeWait;
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
        public ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination,
            ushort destinationPort, ushort sourcePort, ReadOnlySpan<byte> payload) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedTcpConnectionState TcpState => _tcpState;
        public ManagedNetworkServiceBackendResult BeginTcpConnect(Ipv4Address destination,
            ushort destinationPort)
        {
            if (!ConnectResult) return ManagedNetworkServiceBackendResult.Failed;
            _tcpState = ManagedTcpConnectionState.SynSent;
            _event = ManagedNetworkServiceBackendEvent.TcpEstablished;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }
        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
        {
            payload.CopyTo(LastTcpPayload);
            LastTcpPayloadLength = payload.Length;
            return ManagedNetworkServiceBackendResult.Success;
        }
        public ManagedNetworkServiceBackendResult CloseTcp()
        {
            _tcpState = ManagedTcpConnectionState.LastAck;
            return ManagedNetworkServiceBackendResult.Started;
        }
        public bool Teardown() { _tcpState = ManagedTcpConnectionState.Closed; return true; }
        internal void QueuePayload(ReadOnlySpan<byte> payload)
        {
            IManagedTcpApplicationSink sink = _service!;
            payload.CopyTo(_queuedPayload);
            _queuedLength = payload.Length;
            sink.TryCaptureReceivedTcp(Peer, Local, ManagedTcpConnection.ServerPort,
                                       ManagedTcpConnection.ClientPort, _queuedPayload.AsSpan(0, _queuedLength));
            _tcpState = ManagedTcpConnectionState.Established;
            _event = ManagedNetworkServiceBackendEvent.TcpReceived;
            _eventPending = true;
        }
        internal void QueueFin()
        {
            _tcpState = ManagedTcpConnectionState.CloseWait;
            _event = ManagedNetworkServiceBackendEvent.TcpClosed;
            _eventPending = true;
        }
        internal ManagedNetworkService? _service;
        private readonly byte[] _queuedPayload = new byte[512];
        private int _queuedLength;
    }
}

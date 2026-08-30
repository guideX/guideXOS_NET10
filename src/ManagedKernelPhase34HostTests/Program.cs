using System;
using System.Collections.Generic;
using System.Text;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly ManagedX509UtcTime TestTime =
        new(2028, 1, 1, 0, 0, 0);
    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestUrlParsing();
            TestReferenceResolution();
            TestLocationCaptureAndHostHeader();
            TestGcSurvival();
            TestDeterministicRedirectChain();
            TestSecurityAndBoundedFailures();
            Console.WriteLine($"MANAGED_KERNEL_PHASE34_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE34_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestUrlParsing()
    {
        Check(ManagedHttpsUrl.TryParse("https://example.test"u8,
                                       out ManagedHttpsUrl root),
              "url-root");
        Check(root.Port == 443 && Equal(root.Hostname, "example.test"u8) &&
              Equal(root.RequestTarget, "/"u8), "url-default-port-and-root");

        Check(ManagedHttpsUrl.TryParse("HTTPS://Example.Test:8443/a/b?q=1#part"u8,
                                       out ManagedHttpsUrl parsed), "url-case-port");
        Check(parsed.Port == 8443 && Equal(parsed.Hostname, "example.test"u8) &&
              Equal(parsed.RequestTarget, "/a/b?q=1"u8),
              "url-canonical-host-query-fragment");
        Check(ManagedHttpsUrl.TryParse("https://example.test?query=value"u8,
                                       out ManagedHttpsUrl queryOnly) &&
              Equal(queryOnly.RequestTarget, "/?query=value"u8),
              "url-query-without-path");
        Check(ManagedHttpsUrl.TryParse("https://example.test:443/final"u8,
                                       out ManagedHttpsUrl explicitDefault) &&
              explicitDefault.Port == ManagedHttpsClient.HttpsPort,
              "url-explicit-default-port");

        Check(!ManagedHttpsUrl.TryParse(ReadOnlySpan<byte>.Empty, out _, out
            ManagedHttpsUrlParseFailureReason empty) &&
            empty == ManagedHttpsUrlParseFailureReason.Empty, "url-empty");
        Check(!ManagedHttpsUrl.TryParse("example.test/path"u8, out _, out
            ManagedHttpsUrlParseFailureReason noScheme) &&
            noScheme == ManagedHttpsUrlParseFailureReason.MalformedScheme,
            "url-no-scheme");
        Check(!ManagedHttpsUrl.TryParse("http://example.test/"u8, out _, out
            ManagedHttpsUrlParseFailureReason http) &&
            http == ManagedHttpsUrlParseFailureReason.UnsupportedScheme,
            "url-http-rejected");
        Check(!ManagedHttpsUrl.TryParse("https:/example.test/"u8, out _, out
            ManagedHttpsUrlParseFailureReason malformed) &&
            malformed == ManagedHttpsUrlParseFailureReason.MalformedScheme,
            "url-malformed-scheme");
        Check(!ManagedHttpsUrl.TryParse("https:///path"u8, out _, out
            ManagedHttpsUrlParseFailureReason emptyHost) &&
            emptyHost == ManagedHttpsUrlParseFailureReason.EmptyHostname,
            "url-empty-host");
        Check(!ManagedHttpsUrl.TryParse("https://user:pass@example.test/"u8,
                                       out _, out ManagedHttpsUrlParseFailureReason user)
              && user == ManagedHttpsUrlParseFailureReason.UserinfoNotSupported,
              "url-userinfo-rejected");
        Check(!ManagedHttpsUrl.TryParse("https://[::1]/"u8, out _, out
            ManagedHttpsUrlParseFailureReason ipv6) &&
            ipv6 == ManagedHttpsUrlParseFailureReason.Ipv6NotSupported,
            "url-ipv6-rejected");
        Check(!ManagedHttpsUrl.TryParse("https://example.test:abc/"u8, out _, out
            ManagedHttpsUrlParseFailureReason badPort) &&
            badPort == ManagedHttpsUrlParseFailureReason.InvalidPort,
            "url-bad-port");
        Check(!ManagedHttpsUrl.TryParse("https://example.test:65536/"u8, out _, out
            ManagedHttpsUrlParseFailureReason portOverflow) &&
            portOverflow == ManagedHttpsUrlParseFailureReason.PortOverflow,
            "url-port-overflow");
        Check(!ManagedHttpsUrl.TryParse("https://example.test/a b"u8, out _, out
            ManagedHttpsUrlParseFailureReason whitespace) &&
            whitespace == ManagedHttpsUrlParseFailureReason.InvalidCharacter,
            "url-whitespace-rejected");
        Check(!ManagedHttpsUrl.TryParse("https://example.test/a\0b"u8, out _, out
            ManagedHttpsUrlParseFailureReason nul) &&
            nul == ManagedHttpsUrlParseFailureReason.InvalidCharacter,
            "url-nul-rejected");
        byte[] tooLongHost = new byte[ManagedHttpLimits.MaximumHostnameLength + 1];
        tooLongHost.AsSpan().Fill((byte)'a');
        byte[] tooLongHostUrl = new byte["https://"u8.Length + tooLongHost.Length];
        "https://"u8.CopyTo(tooLongHostUrl);
        tooLongHost.CopyTo(tooLongHostUrl, "https://"u8.Length);
        Check(!ManagedHttpsUrl.TryParse(tooLongHostUrl, out _, out
            ManagedHttpsUrlParseFailureReason hostTooLong) &&
            hostTooLong == ManagedHttpsUrlParseFailureReason.InvalidHostname,
            "url-host-bound");

        byte[] tooLongPath = new byte[ManagedHttpsUrl.MaximumPathLength + 1];
        tooLongPath[0] = (byte)'/';
        tooLongPath.AsSpan(1).Fill((byte)'x');
        byte[] tooLongUrl = new byte["https://example.test"u8.Length +
                                     tooLongPath.Length];
        "https://example.test"u8.CopyTo(tooLongUrl);
        tooLongPath.CopyTo(tooLongUrl, "https://example.test"u8.Length);
        Check(!ManagedHttpsUrl.TryParse(tooLongUrl, out _, out
            ManagedHttpsUrlParseFailureReason longPath) &&
            longPath == ManagedHttpsUrlParseFailureReason.PathTooLong,
            "url-path-bound");
    }

    private static void TestReferenceResolution()
    {
        Check(ManagedHttpsUrl.TryParse("https://example.test/a/b/start"u8,
                                       out ManagedHttpsUrl current),
              "resolve-base");
        CheckResolve(current, "/final"u8, "https://example.test/final"u8,
                     "resolve-absolute-path");
        CheckResolve(current, "next"u8, "https://example.test/a/b/next"u8,
                     "resolve-relative-path");
        CheckResolve(current, "../next"u8, "https://example.test/a/next"u8,
                     "resolve-parent-path");
        CheckResolve(current, "./next#fragment"u8,
                     "https://example.test/a/b/next"u8, "resolve-dot-fragment");
        CheckResolve(current, "?page=2"u8,
                     "https://example.test/a/b/start?page=2"u8,
                     "resolve-query-reference");
        CheckResolve(current, "//other.example.com:8443/final"u8,
                     "https://other.example.com:8443/final"u8,
                     "resolve-scheme-relative");
        CheckResolve(current, "https://other.example.com/final#x"u8,
                     "https://other.example.com/final"u8,
                     "resolve-absolute-https");

        Check(!ManagedHttpsUrl.TryResolve(current, "http://example.test/insecure"u8,
                                          out _, out ManagedHttpsUrlParseFailureReason downgrade) &&
              downgrade == ManagedHttpsUrlParseFailureReason.HttpsDowngrade,
              "resolve-downgrade-rejected");
        Check(!ManagedHttpsUrl.TryResolve(current, "ftp://example.test/file"u8,
                                          out _, out ManagedHttpsUrlParseFailureReason scheme) &&
              scheme == ManagedHttpsUrlParseFailureReason.UnsupportedReference,
              "resolve-unsupported-scheme");
        Check(!ManagedHttpsUrl.TryResolve(current, ReadOnlySpan<byte>.Empty, out _, out
            ManagedHttpsUrlParseFailureReason empty) &&
            empty == ManagedHttpsUrlParseFailureReason.EmptyReference,
            "resolve-empty-location");

        byte[] longLocation = new byte[ManagedHttpsUrl.MaximumLocationLength + 1];
        longLocation.AsSpan().Fill((byte)'x');
        Check(!ManagedHttpsUrl.TryResolve(current, longLocation, out _, out
            ManagedHttpsUrlParseFailureReason overlong) &&
            overlong == ManagedHttpsUrlParseFailureReason.TooLong,
            "resolve-location-bound");
    }

    private static void TestLocationCaptureAndHostHeader()
    {
        ManagedHttpResponseParser parser = new(
            ManagedHttpLimits.MaximumAcceptedBodyLength, false, true);
        byte[] response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 302 Found\r\nLo" +
            "cation: /phase34/next\r\nContent-Length: 0\r\n\r\n");
        for (int index = 0; index != response.Length; ++index)
            Check(parser.TryFeed(response.AsSpan(index, 1), out int consumed) &&
                  consumed == 1, "location-byte-fragment");
        Check(parser.StatusCode == 302 && parser.IsBodyComplete &&
              parser.HasLocation, "location-status-and-capture");
        Span<byte> location = stackalloc byte[ManagedHttpLimits.MaximumLocationLength];
        Check(parser.TryCopyLocation(location, out int locationLength) &&
              Equal(location[..locationLength], "/phase34/next"u8),
              "location-copied");

        Span<byte> request = stackalloc byte[ManagedHttpLimits.MaximumSerializedRequestSize];
        Check(ManagedHttpRequestBuilder.TryBuildGet("example.test"u8, 8443,
            "/final?q=1"u8, request, out int requestLength),
            "host-header-build");
        ReadOnlySpan<byte> serialized = request[..requestLength];
        Check(Contains(serialized, "GET /final?q=1 HTTP/1.1\r\n"u8) &&
              Contains(serialized, "Host: example.test:8443\r\n"u8) &&
              !Contains(serialized, "https://"u8), "host-header-port-policy");
        Check(ManagedHttpRequestBuilder.TryBuildGet("example.test"u8, 443,
            "/final"u8, request, out requestLength) &&
              Contains(request[..requestLength], "Host: example.test\r\n"u8) &&
              !Contains(request[..requestLength], "Host: example.test:443"u8),
              "host-header-default-port-policy");

        ManagedHttpResponseParser duplicate = new(
            ManagedHttpLimits.MaximumAcceptedBodyLength, false, true);
        byte[] duplicateLocation = Encoding.ASCII.GetBytes(
            "HTTP/1.1 302 Found\r\nLocation: /a\r\nLocation: /b\r\n" +
            "Content-Length: 0\r\n\r\n");
        Check(!duplicate.TryFeed(duplicateLocation, out _) &&
              duplicate.FailureReason == ManagedHttpParseFailureReason.InvalidLocation,
              "location-duplicate-rejected");

        ManagedHttpResponseParser malformed = new(
            ManagedHttpLimits.MaximumAcceptedBodyLength, false, true);
        byte[] malformedLocation = Encoding.ASCII.GetBytes(
            "HTTP/1.1 302 Found\r\nLocation: https://bad host/final\r\n" +
            "Content-Length: 0\r\n\r\n");
        Check(!malformed.TryFeed(malformedLocation, out _) &&
              malformed.FailureReason == ManagedHttpParseFailureReason.InvalidLocation,
              "location-character-rejected");
    }

    private static void TestGcSurvival()
    {
        Check(ManagedHttpsUrl.TryParse("https://example.test/a/b/start"u8,
                                       out ManagedHttpsUrl current), "gc-base");
        GC.Collect();
        CheckResolve(current, "../final"u8, "https://example.test/a/final"u8,
                     "gc-relative-resolution");
        GC.Collect();
        Span<byte> copy = stackalloc byte[ManagedHttpsUrl.MaximumUrlLength];
        Check(current.TryCopyAbsoluteUrl(copy, out int length) &&
              Equal(copy[..length], "https://example.test/a/b/start"u8),
              "gc-url-retention");
    }

    private static void TestDeterministicRedirectChain()
    {
        RedirectFixtureBackend backend = new(RedirectFixtureScenario.Chain);
        ManagedNetworkService service = CreateService(backend);
        ManagedHttpsClient client = CreateHttpsClient(service);
        Check(client.BeginGetUrl("https://www.example.com/phase34/start") ==
              NetworkOperationResult.Started, "chain-begin-url");
        RunUntilTerminal(client);
        Check(client.State == ManagedHttpsClientState.Succeeded &&
              client.TlsAuthenticated && client.StatusCode == 200 &&
              client.RedirectCount == 3, "chain-final-response");
        Span<byte> body = stackalloc byte[64];
        Check(client.TryCopyResponseBody(body, out int bodyLength) &&
              Equal(body[..bodyLength], "phase34-redirect-pass"u8),
              "chain-final-body");
        Check(backend.ConnectionCount == 4 && backend.ResolveNames.Count == 4 &&
              backend.Requests.Count == 4 && backend.DestinationPorts.Count == 4,
              "chain-fresh-hop-count");
        Check(backend.ResolveNames[0] == "www.example.com" &&
              backend.ResolveNames[1] == "www.example.com" &&
              backend.ResolveNames[2] == "www.example.com" &&
              backend.ResolveNames[3] == "other.example.com",
              "chain-dns-hostnames");
        Check(backend.SniNames[0] == "www.example.com" &&
              backend.SniNames[1] == "www.example.com" &&
              backend.SniNames[2] == "www.example.com" &&
              backend.SniNames[3] == "other.example.com",
              "chain-sni-hostnames");
        Check(backend.DestinationPorts[0] == 443 &&
              backend.DestinationPorts[1] == 443 &&
              backend.DestinationPorts[2] == 443 &&
              backend.DestinationPorts[3] == 8443,
              "chain-explicit-port");
        Check(backend.Requests[0].Contains("GET /phase34/start", StringComparison.Ordinal) &&
              backend.Requests[1].Contains("GET /phase34/step2", StringComparison.Ordinal) &&
              backend.Requests[2].Contains("GET /phase34/next", StringComparison.Ordinal) &&
              backend.Requests[3].Contains("GET /phase34/final", StringComparison.Ordinal) &&
              backend.Requests[3].Contains("Host: other.example.com:8443", StringComparison.Ordinal),
              "chain-request-targets-and-host");
        Check(client.FinalUrl.Equals(MakeUrl("other.example.com"u8, 8443,
                                             "/phase34/final"u8)),
              "chain-final-url");

        Check(client.Reset() == NetworkOperationResult.Success &&
              service.TcpState == NetworkTcpState.Closed,
              "chain-reset-after-teardown");
        Check(client.BeginGetUrl("https://www.example.com/phase34/final") ==
              NetworkOperationResult.Started, "chain-reuse-begin");
        RunUntilTerminal(client);
        Check(client.State == ManagedHttpsClientState.Succeeded &&
              client.StatusCode == 200, "chain-reuse-success");
    }

    private static void TestSecurityAndBoundedFailures()
    {
        RedirectFixtureBackend bad = new(RedirectFixtureScenario.BadCertificate);
        ManagedNetworkService badService = CreateService(bad);
        ManagedHttpsClient badClient = CreateHttpsClient(badService);
        Check(badClient.BeginGetUrl("https://www.example.com/phase34-bad-redirect") ==
              NetworkOperationResult.Started, "bad-redirect-begin");
        RunUntilTerminal(badClient);
        Check(badClient.State == ManagedHttpsClientState.Failed &&
              badClient.FailureReason == ManagedHttpsFailureReason.TlsAuthenticationFailure &&
              bad.ConnectionCount == 2 && bad.Requests.Count == 1 &&
              bad.SniNames[1] == "bad.example.net" &&
              !bad.Requests.Exists(static value => value.Contains("/final", StringComparison.Ordinal)),
              "bad-redirect-hostname-mismatch");
        Check(badClient.Reset() == NetworkOperationResult.Success &&
              badService.TcpState == NetworkTcpState.Closed,
              "bad-redirect-teardown");

        RedirectFixtureBackend downgrade = new(RedirectFixtureScenario.Downgrade);
        ManagedNetworkService downgradeService = CreateService(downgrade);
        ManagedHttpsClient downgradeClient = CreateHttpsClient(downgradeService);
        Check(downgradeClient.BeginGetUrl("https://www.example.com/phase34-downgrade") ==
              NetworkOperationResult.Started, "downgrade-begin");
        RunUntilTerminal(downgradeClient);
        Check(downgradeClient.State == ManagedHttpsClientState.Failed &&
              downgradeClient.FailureReason == ManagedHttpsFailureReason.RedirectDowngradeRejected &&
              downgrade.ConnectionCount == 1 && downgrade.ResolveNames.Count == 1,
              "downgrade-rejected-before-connect");

        RedirectFixtureBackend loop = new(RedirectFixtureScenario.Loop);
        ManagedHttpsClient loopClient = CreateHttpsClient(CreateService(loop));
        Check(loopClient.BeginGetUrl("https://www.example.com/loop") ==
              NetworkOperationResult.Started, "loop-begin");
        RunUntilTerminal(loopClient);
        Check(loopClient.State == ManagedHttpsClientState.Failed &&
              loopClient.FailureReason == ManagedHttpsFailureReason.RedirectLimitExceeded &&
              loopClient.RedirectCount == ManagedHttpsClient.MaximumRedirects &&
              loop.ConnectionCount == ManagedHttpsClient.MaximumRedirects + 1,
              "redirect-hop-bound");

        RedirectFixtureBackend missing = new(RedirectFixtureScenario.MissingLocation);
        ManagedHttpsClient missingClient = CreateHttpsClient(CreateService(missing));
        Check(missingClient.BeginGetUrl("https://www.example.com/missing") ==
              NetworkOperationResult.Started, "missing-location-begin");
        RunUntilTerminal(missingClient);
        Check(missingClient.State == ManagedHttpsClientState.Failed &&
              missingClient.FailureReason == ManagedHttpsFailureReason.RedirectMissingLocation,
              "missing-location-rejected");

        RedirectFixtureBackend empty = new(RedirectFixtureScenario.EmptyLocation);
        ManagedHttpsClient emptyClient = CreateHttpsClient(CreateService(empty));
        Check(emptyClient.BeginGetUrl("https://www.example.com/empty") ==
              NetworkOperationResult.Started, "empty-location-begin");
        RunUntilTerminal(emptyClient);
        Check(emptyClient.State == ManagedHttpsClientState.Failed &&
              emptyClient.FailureReason == ManagedHttpsFailureReason.RedirectInvalidLocation,
              "empty-location-rejected");

        RedirectFixtureBackend malformed = new(RedirectFixtureScenario.MalformedLocation);
        ManagedHttpsClient malformedClient = CreateHttpsClient(CreateService(malformed));
        Check(malformedClient.BeginGetUrl("https://www.example.com/malformed") ==
              NetworkOperationResult.Started, "malformed-location-begin");
        RunUntilTerminal(malformedClient);
        Check(malformedClient.State == ManagedHttpsClientState.Failed &&
              malformedClient.FailureReason == ManagedHttpsFailureReason.HttpParseFailure,
              "malformed-location-rejected");
    }

    private static ManagedHttpsUrl MakeUrl(ReadOnlySpan<byte> host, ushort port,
                                           ReadOnlySpan<byte> path)
    {
        if (!ManagedHttpsUrl.TryCreate(host, port, path, out ManagedHttpsUrl url))
            throw new InvalidOperationException("fixture URL creation failed");
        return url;
    }

    private static ManagedHttpsClient CreateHttpsClient(ManagedNetworkService service)
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        ManagedSecureRandom random = new(new FixedEntropy(entropy));
        return new ManagedHttpsClient(service, ManagedTls12Phase31Fixtures.Root,
                                       in TestTime, random);
    }

    private static ManagedNetworkService CreateService(
        RedirectFixtureBackend backend)
    {
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        return service;
    }

    private static void RunUntilTerminal(ManagedHttpsClient client)
    {
        for (int index = 0; index != 4096 &&
             client.State != ManagedHttpsClientState.Succeeded &&
             client.State != ManagedHttpsClientState.Failed; ++index)
        {
            client.Poll();
            if ((index & 7) == 0) GC.Collect();
        }
        if (client.State != ManagedHttpsClientState.Succeeded &&
            client.State != ManagedHttpsClientState.Failed)
            throw new InvalidOperationException("fixture client did not terminate");
    }

    private enum RedirectFixtureScenario : byte
    {
        Chain,
        BadCertificate,
        Downgrade,
        Loop,
        MissingLocation,
        EmptyLocation,
        MalformedLocation
    }

    private sealed class RedirectFixtureBackend : IManagedNetworkServiceBackend
    {
        private static readonly Ipv4Address Local = new(0x0A0F002AU);
        private static readonly Ipv4Address Peer = new(0x0A0F0002U);
        private readonly RedirectFixtureScenario _scenario;
        private readonly List<byte[]> _queued = new();
        private ManagedNetworkService? _service;
        private ManagedNetworkServiceBackendEvent _event;
        private bool _eventPending;
        private ManagedTcpConnectionState _tcpState;
        private int _queueIndex;
        private bool _serverHelloQueued;
        private bool _serverFlightQueued;
        private bool _responseQueued;
        private bool _finQueued;
        private string _hostname = string.Empty;

        internal RedirectFixtureBackend(RedirectFixtureScenario scenario) =>
            _scenario = scenario;
        internal List<string> ResolveNames { get; } = new();
        internal List<string> SniNames { get; } = new();
        internal List<string> Requests { get; } = new();
        internal List<ushort> DestinationPorts { get; } = new();
        internal int ConnectionCount { get; private set; }

        internal void Attach(ManagedNetworkService service) => _service = service;
        public bool IsAvailable => true;
        public NetworkStatus GetStatus() => new(true, true, true, true,
            0x021500000002, Local, new Ipv4Address(0xFFFFFF00),
            new Ipv4Address(0x0A0F0001));
        public void SetRuntimeStatus(NetworkStatus status) { }

        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
            _hostname = Encoding.ASCII.GetString(name);
            ResolveNames.Add(_hostname);
            _event = ManagedNetworkServiceBackendEvent.DnsResolved;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool TryGetResolved(out Ipv4Address address)
        {
            address = Peer;
            return true;
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
            if (_queueIndex < _queued.Count && _service != null)
            {
                byte[] item = _queued[_queueIndex];
                if (!((IManagedTcpApplicationSink)_service).TryCaptureReceivedTcp(
                        Peer, Local, ManagedTcpConnection.ServerPort,
                        ManagedTcpConnection.ClientPort, item))
                {
                    serviceEvent = ManagedNetworkServiceBackendEvent.None;
                    return true;
                }
                _queueIndex++;
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
            ConnectionCount++;
            DestinationPorts.Add(destinationPort);
            ManagedTls12Phase31Fixtures.KeyBlock[..16].CopyTo(_clientApplicationKey);
            ManagedTls12Phase31Fixtures.KeyBlock[32..36].CopyTo(_clientApplicationIv);
            ManagedTls12Phase31Fixtures.KeyBlock[16..32].CopyTo(_serverApplicationKey);
            ManagedTls12Phase31Fixtures.KeyBlock[36..40].CopyTo(_serverApplicationIv);
            _tcpState = ManagedTcpConnectionState.SynSent;
            _event = ManagedNetworkServiceBackendEvent.TcpEstablished;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
        {
            if (payload.Length >= 6 &&
                payload[0] == ManagedTls12RecordProtection.Handshake &&
                payload[5] == 1 && !_serverHelloQueued)
            {
                _serverHelloQueued = true;
                int helloLength = (payload[3] << 8) | payload[4];
                _lastClientHello = payload.Slice(5, helloLength).ToArray();
                SniNames.Add(ExtractSni(payload));
                QueueServerHandshake();
                return ManagedNetworkServiceBackendResult.Success;
            }
            if (!_serverFlightQueued && payload.Length >= 5 &&
                payload[0] == ManagedTls12RecordProtection.Handshake)
            {
                _serverFlightQueued = true;
                Queue(ManagedTls12Phase31Fixtures.ChangeCipherSpec);
                if (_hostname == "www.example.com")
                    QueueRecordFragments(ManagedTls12Phase31Fixtures.ServerFinishedRecord);
                else if (!QueueDynamicServerFinished(payload))
                    throw new InvalidOperationException("dynamic TLS fixture: " +
                                                        _dynamicFailure);
                return ManagedNetworkServiceBackendResult.Success;
            }
            if (!_responseQueued && payload.Length >= 5 &&
                payload[0] == ManagedTls12RecordProtection.ApplicationData)
            {
                byte[] request = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
                if (!ManagedTls12RecordProtection.TryDecrypt(1,
                        _clientApplicationKey, _clientApplicationIv,
                        ManagedTls12RecordProtection.ApplicationData, payload,
                        request, out int requestLength))
                    return ManagedNetworkServiceBackendResult.Failed;
                string requestText = Encoding.ASCII.GetString(request, 0, requestLength);
                Requests.Add(requestText);
                _responseQueued = true;
                QueueResponse(requestText);
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
            _queued.Clear();
            _queueIndex = 0;
            _eventPending = false;
            _tcpState = ManagedTcpConnectionState.Closed;
            _serverHelloQueued = false;
            _serverFlightQueued = false;
            _responseQueued = false;
            _finQueued = false;
            _lastClientHello = Array.Empty<byte>();
            return true;
        }

        private void QueueResponse(string request)
        {
            string response;
            if (_scenario == RedirectFixtureScenario.BadCertificate &&
                request.Contains("/phase34-bad-redirect", StringComparison.Ordinal))
            {
                response = RedirectResponse(302, "https://bad.example.net/final");
            }
            else if (_scenario == RedirectFixtureScenario.Downgrade &&
                     request.Contains("/phase34-downgrade", StringComparison.Ordinal))
            {
                response = RedirectResponse(302, "http://www.example.com/insecure");
            }
            else if (_scenario == RedirectFixtureScenario.MissingLocation)
            {
                response = "HTTP/1.1 302 Found\r\nContent-Length: 0\r\n\r\n";
            }
            else if (_scenario == RedirectFixtureScenario.EmptyLocation)
            {
                response = RedirectResponse(302, string.Empty);
            }
            else if (_scenario == RedirectFixtureScenario.MalformedLocation)
            {
                response = RedirectResponse(302, "https://bad host/final");
            }
            else if (_scenario == RedirectFixtureScenario.Loop)
            {
                response = RedirectResponse(302, "/loop");
            }
            else if (request.Contains("/phase34/start", StringComparison.Ordinal))
            {
                response = RedirectResponse(302, "/phase34/step2");
            }
            else if (request.Contains("/phase34/step2", StringComparison.Ordinal))
            {
                response = RedirectResponse(301, "next");
            }
            else if (request.Contains("/phase34/next", StringComparison.Ordinal))
            {
                response = RedirectResponse(307,
                    "https://other.example.com:8443/phase34/final");
            }
            else
            {
                response = "HTTP/1.1 200 OK\r\nContent-Length: 21\r\n" +
                           "Connection: close\r\n\r\nphase34-redirect-pass";
            }
            QueueApplicationPlaintext(Encoding.ASCII.GetBytes(response));
            _finQueued = true;
        }

        private static string RedirectResponse(int status, string location)
        {
            return "HTTP/1.1 " + status + " Redirect\r\nLocation: " + location +
                   "\r\nContent-Length: 8\r\nConnection: close\r\n\r\nredirect";
        }

        private void QueueServerHandshake()
        {
            for (int index = 0; index != ManagedTls12Phase31Fixtures.ServerRecordCount; ++index)
                QueueRecordFragments(ManagedTls12Phase31Fixtures.GetServerRecord(index));
        }

        private bool QueueDynamicServerFinished(ReadOnlySpan<byte> clientFlight)
        {
            if (clientFlight.Length < 5 || clientFlight[0] !=
                ManagedTls12RecordProtection.Handshake)
                return DynamicFail("client-flight-header");
            int clientHelloLength = _lastClientHello.Length;
            if (clientHelloLength == 0 || clientHelloLength > 512 ||
                clientHelloLength + ManagedTls12Phase31Fixtures.ServerHello.Length +
                ManagedTls12Phase31Fixtures.CertificateMessage.Length +
                ManagedTls12Phase31Fixtures.ServerKeyExchange.Length +
                ManagedTls12Phase31Fixtures.ServerHelloDone.Length + 70 > 8192)
                return DynamicFail("client-hello-size");
            int clientKeyExchangeLength = (clientFlight[3] << 8) | clientFlight[4];
            if (clientKeyExchangeLength != 70 ||
                clientFlight.Length < 5 + clientKeyExchangeLength + 6 + 5)
                return DynamicFail("client-key-exchange-size");

            byte[] transcript = new byte[8192];
            int offset = 0;
            _lastClientHello.AsSpan().CopyTo(transcript.AsSpan(offset));
            offset += _lastClientHello.Length;
            ManagedTls12Phase31Fixtures.ServerHello.CopyTo(transcript, offset);
            offset += ManagedTls12Phase31Fixtures.ServerHello.Length;
            ManagedTls12Phase31Fixtures.CertificateMessage.CopyTo(transcript, offset);
            offset += ManagedTls12Phase31Fixtures.CertificateMessage.Length;
            ManagedTls12Phase31Fixtures.ServerKeyExchange.CopyTo(transcript, offset);
            offset += ManagedTls12Phase31Fixtures.ServerKeyExchange.Length;
            ManagedTls12Phase31Fixtures.ServerHelloDone.CopyTo(transcript, offset);
            offset += ManagedTls12Phase31Fixtures.ServerHelloDone.Length;
            clientFlight.Slice(5, clientKeyExchangeLength).CopyTo(transcript.AsSpan(offset));
            offset += clientKeyExchangeLength;

            Span<byte> sessionHash = stackalloc byte[ManagedSha256.DigestSize];
            Span<byte> masterSecret = stackalloc byte[48];
            Span<byte> keySeed = stackalloc byte[64];
            Span<byte> keyBlock = stackalloc byte[40];
            Span<byte> clientFinished = stackalloc byte[16];
            Span<byte> transcriptWithFinished = stackalloc byte[8192];
            Span<byte> transcriptHash = stackalloc byte[ManagedSha256.DigestSize];
            Span<byte> verifyData = stackalloc byte[12];
            Span<byte> finished = stackalloc byte[16];
            try
            {
                if (!ManagedSha256.TryHash(transcript.AsSpan(0, offset), sessionHash) ||
                    !ManagedTls12Prf.TryCompute(
                        ManagedTls12Phase31Fixtures.PremasterSecret,
                        "extended master secret"u8, sessionHash, masterSecret))
                    return DynamicFail("master-derivation");
                ManagedTls12Phase31Fixtures.ServerRandom.CopyTo(keySeed);
                _lastClientHello.AsSpan(6, 32).CopyTo(keySeed[32..]);
                if (!ManagedTls12Prf.TryCompute(masterSecret, "key expansion"u8,
                                                 keySeed, keyBlock))
                    return DynamicFail("key-derivation");
                keyBlock[..16].CopyTo(_clientApplicationKey);
                keyBlock[32..36].CopyTo(_clientApplicationIv);
                keyBlock[16..32].CopyTo(_serverApplicationKey);
                keyBlock[36..40].CopyTo(_serverApplicationIv);
                int encryptedFinishedOffset = 5 + clientKeyExchangeLength + 6;
                byte[] decrypted = new byte[64];
                int encryptedLength = (clientFlight[encryptedFinishedOffset + 3] << 8) |
                                      clientFlight[encryptedFinishedOffset + 4];
                if (encryptedLength <= 0 ||
                    encryptedFinishedOffset + 5 + encryptedLength > clientFlight.Length ||
                    !ManagedTls12RecordProtection.TryDecrypt(0, keyBlock[..16],
                        keyBlock[32..36], ManagedTls12RecordProtection.Handshake,
                        clientFlight[encryptedFinishedOffset..], decrypted,
                        out int finishedLength) || finishedLength != 16)
                    return DynamicFail("client-finished-decrypt");
                decrypted.AsSpan(0, finishedLength).CopyTo(clientFinished);
                transcript.AsSpan(0, offset).CopyTo(transcriptWithFinished);
                clientFinished.CopyTo(transcriptWithFinished[offset..]);
                int finalTranscriptLength = offset + clientFinished.Length;
                if (!ManagedSha256.TryHash(
                        transcriptWithFinished[..finalTranscriptLength], transcriptHash) ||
                    !ManagedTls12Prf.TryCompute(masterSecret, "server finished"u8,
                                                transcriptHash, verifyData))
                    return DynamicFail("server-finished-derivation");
                finished[0] = 20;
                finished[1] = 0;
                finished[2] = 0;
                finished[3] = 12;
                verifyData.CopyTo(finished[4..]);
                byte[] record = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
                if (!ManagedTls12RecordProtection.TryEncrypt(0, keyBlock[16..32],
                        keyBlock[36..40], ManagedTls12RecordProtection.Handshake,
                        finished, record, out int recordLength))
                    return DynamicFail("server-finished-encrypt");
                QueueRecordFragments(record[..recordLength]);
                decrypted.AsSpan().Clear();
                return true;
            }
            finally
            {
                sessionHash.Clear();
                masterSecret.Clear();
                keySeed.Clear();
                keyBlock.Clear();
                clientFinished.Clear();
                transcriptWithFinished.Clear();
                transcriptHash.Clear();
                verifyData.Clear();
                finished.Clear();
                transcript.AsSpan().Clear();
            }
        }

        private bool DynamicFail(string reason)
        {
            _dynamicFailure = reason;
            return false;
        }

        private void QueueApplicationPlaintext(byte[] plaintext)
        {
            const int fragment = 11;
            int offset = 0;
            ulong sequence = 1;
            while (offset != plaintext.Length)
            {
                int count = Math.Min(fragment, plaintext.Length - offset);
                QueueServerRecord(sequence++, plaintext.AsSpan(offset, count));
                offset += count;
            }
        }

        private void QueueServerRecord(ulong sequence, ReadOnlySpan<byte> plaintext)
        {
            byte[] record = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
            if (!ManagedTls12RecordProtection.TryEncrypt(sequence,
                    _serverApplicationKey, _serverApplicationIv,
                    ManagedTls12RecordProtection.ApplicationData, plaintext,
                    record, out int length))
                throw new InvalidOperationException("fixture record build failed");
            QueueRecordFragments(record[..length]);
        }

        private void QueueRecordFragments(byte[] record)
        {
            if (record.Length < 3) { Queue(record); return; }
            Queue(record[..2]);
            Queue(record[2..Math.Min(9, record.Length)]);
            if (record.Length > 9) Queue(record[9..]);
        }

        private byte[] _lastClientHello = Array.Empty<byte>();
        private string _dynamicFailure = string.Empty;
        private readonly byte[] _clientApplicationKey = new byte[16];
        private readonly byte[] _clientApplicationIv = new byte[4];
        private readonly byte[] _serverApplicationKey = new byte[16];
        private readonly byte[] _serverApplicationIv = new byte[4];

        private void Queue(byte[] bytes) => _queued.Add((byte[])bytes.Clone());

        private static string ExtractSni(ReadOnlySpan<byte> record)
        {
            const int extensionsStart = 52;
            if (record.Length < extensionsStart + 2) return string.Empty;
            int extensionLength = (record[50] << 8) | record[51];
            int end = extensionsStart + extensionLength;
            if (end > record.Length) return string.Empty;
            int index = extensionsStart;
            while (index + 4 <= end)
            {
                int type = (record[index] << 8) | record[index + 1];
                int length = (record[index + 2] << 8) | record[index + 3];
                index += 4;
                if (index + length > end) return string.Empty;
                if (type == 0 && length >= 5)
                {
                    int nameLength = (record[index + 3] << 8) | record[index + 4];
                    if (record[index + 2] == 0 && nameLength != 0 &&
                        index + 5 + nameLength <= index + length)
                        return Encoding.ASCII.GetString(record.Slice(index + 5,
                                                                     nameLength));
                }
                index += length;
            }
            return string.Empty;
        }
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

    private static void CheckResolve(in ManagedHttpsUrl current,
                                     ReadOnlySpan<byte> reference,
                                     ReadOnlySpan<byte> expected,
                                     string name)
    {
        Check(ManagedHttpsUrl.TryResolve(current, reference,
                                         out ManagedHttpsUrl resolved), name);
        Span<byte> actual = stackalloc byte[ManagedHttpsUrl.MaximumUrlLength];
        Check(resolved.TryCopyAbsoluteUrl(actual, out int length) &&
              Equal(actual[..length], expected), name + "-value");
    }

    private static bool Equal(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.SequenceEqual(right);

    private static bool Contains(ReadOnlySpan<byte> value, ReadOnlySpan<byte> needle)
    {
        for (int index = 0; index <= value.Length - needle.Length; ++index)
            if (value.Slice(index, needle.Length).SequenceEqual(needle)) return true;
        return false;
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException("failed: " + name);
    }
}

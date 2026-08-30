using System;
using System.Collections.Generic;
using System.Text;

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
            TestContentLengthAndDuplicates();
            TestChunkedAndFragmentation();
            TestCloseNoBodyAndInformational();
            TestMalformedMatrix();
            TestBoundedBackpressureAndDigest();
            TestHttpsCompositionAndSecurityBoundary();
            Console.WriteLine($"MANAGED_KERNEL_PHASE33_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE33_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestContentLengthAndDuplicates()
    {
        byte[] response = Ascii(
            "HTTP/1.1 200 OK\r\nContent-Length: 13\r\nConnection: close\r\n\r\nhello, world!");
        ManagedHttpResponseParser parser = NewParser();
        Check(FeedOneByteAtATime(parser, response) &&
              parser.FramingMode == ManagedHttpFramingMode.ContentLength &&
              parser.ContentLength == 13 && parser.BodyLength == 13 &&
              parser.IsBodyComplete, "length-hostile-one-byte-fragmentation");
        Check(ReadBody(parser).AsSpan().SequenceEqual("hello, world!"u8),
              "length-body-delivery");

        parser.Reset();
        byte[] exact = Ascii(
            "HTTP/1.1 200 OK\r\nContent-Length: 3\r\nConnection: close\r\n\r\nabc");
        Check(parser.TryFeed(exact, out int consumed) && consumed == exact.Length &&
              parser.IsBodyComplete,
              "length-completes-without-eof");
        Check(parser.NotifyConnectionClosed() && parser.State == ManagedHttpParseState.Closed,
              "length-close-after-completion");

        Check(Succeeds("HTTP/1.1 200 OK\r\nContent-Length: 3\r\n" +
                       "Content-Length: 3\r\nConnection: close\r\n\r\nabc",
                       ManagedHttpFramingMode.ContentLength),
              "length-identical-duplicates");
        Check(Succeeds("HTTP/1.1 200 OK\r\nContent-Length: 3, 3\r\n" +
                       "Connection: close\r\n\r\nabc",
                       ManagedHttpFramingMode.ContentLength),
              "length-identical-combined-values");
        Check(Fails("HTTP/1.1 200 OK\r\nContent-Length: 3\r\n" +
                    "Content-Length: 4\r\nConnection: close\r\n\r\n",
                    ManagedHttpParseFailureReason.ConflictingContentLength),
              "length-contradictory-duplicates");
    }

    private static void TestChunkedAndFragmentation()
    {
        byte[] response = Ascii(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: ChUnKeD\r\n" +
            "Connection: close\r\n\r\n" +
            "7;foo=bar\r\nphase33\r\n1\r\n-\r\n4\r\nhttp\r\n" +
            "1\r\n-\r\n4\r\npass\r\n0\r\nX-Trace: bounded\r\n\r\n");
        ManagedHttpResponseParser parser = NewParser();
        Check(FeedOneByteAtATime(parser, response) &&
              parser.FramingMode == ManagedHttpFramingMode.Chunked &&
              parser.IsChunked && parser.IsBodyComplete && parser.BodyLength == 17,
              "chunked-case-insensitive-fragmented");
        Check(ReadBody(parser).AsSpan().SequenceEqual("phase33-http-pass"u8),
              "chunked-decoded-body-and-trailer");

        parser.Reset();
        byte[] split = Ascii("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                             "A\r\n0123456789\r\n0\r\n\r\n");
        for (int index = 0; index != split.Length; ++index)
        {
            Check(parser.TryFeed(split.AsSpan(index, 1), out int consumed) &&
                  consumed == 1, "chunked-byte-boundary-" + index);
            if (parser.HasPendingBody)
            {
                byte[] scratch = new byte[8];
                while (parser.TryReadBodyChunk(scratch, out _)) { }
            }
        }
        Check(parser.IsBodyComplete && parser.BodyLength == 10,
              "chunked-crlf-and-data-boundaries");

        parser.Reset();
        byte[] informational = Ascii("HTTP/1.1 100 Continue\r\n\r\n" +
                                     "HTTP/1.1 200 OK\r\nContent-Length: 2\r\n\r\nok");
        Check(parser.Feed(informational) && parser.StatusCode == 200 &&
              parser.IsBodyComplete, "informational-followed-by-final");
    }

    private static void TestCloseNoBodyAndInformational()
    {
        ManagedHttpResponseParser parser = NewParser();
        Check(parser.Feed(Ascii("HTTP/1.1 200 OK\r\nConnection: close\r\n\r\nabc")) &&
              parser.State == ManagedHttpParseState.BodyUntilClose &&
              !parser.IsBodyComplete, "connection-close-selected");
        Check(parser.NotifyConnectionClosed() && parser.IsBodyComplete &&
              parser.FramingMode == ManagedHttpFramingMode.ConnectionClose &&
              ReadBody(parser).AsSpan().SequenceEqual("abc"u8),
              "connection-close-body-completes-at-eof");

        parser.Reset();
        Check(parser.Feed(Ascii("HTTP/1.1 204 No Content\r\nConnection: keep-alive\r\n\r\n")) &&
              parser.FramingMode == ManagedHttpFramingMode.NoBody &&
              parser.IsBodyComplete, "204-no-body-semantics");
        parser.Reset();
        Check(parser.Feed(Ascii("HTTP/1.1 304 Not Modified\r\n" +
                               "Content-Length: 99\r\nConnection: close\r\n\r\n")) &&
              parser.FramingMode == ManagedHttpFramingMode.NoBody &&
              parser.BodyLength == 0, "304-no-body-semantics");

        parser.Reset();
        Check(!parser.Feed(Ascii("HTTP/1.1 20x Bad\r\n")) &&
              parser.FailureReason == ManagedHttpParseFailureReason.StatusCode,
              "malformed-status-line");
        parser.Reset();
        Check(!parser.Feed(Ascii("HTTP/1.1 200 OK\r\nConnection: keep-alive\r\n\r\n")) &&
              parser.FailureReason == ManagedHttpParseFailureReason.MissingConnectionClose,
              "unframed-keep-alive-rejected");
    }

    private static void TestMalformedMatrix()
    {
        Check(Fails("HTTP/1.1 200 OK\r\nContent-Length:\r\nConnection: close\r\n\r\n",
                    ManagedHttpParseFailureReason.ContentLength), "length-empty");
        Check(Fails("HTTP/1.1 200 OK\r\nContent-Length: -1\r\nConnection: close\r\n\r\n",
                    ManagedHttpParseFailureReason.ContentLength), "length-negative");
        Check(Fails("HTTP/1.1 200 OK\r\nContent-Length: +1\r\nConnection: close\r\n\r\n",
                    ManagedHttpParseFailureReason.ContentLength), "length-plus-sign");
        Check(Fails("HTTP/1.1 200 OK\r\nContent-Length: abc\r\nConnection: close\r\n\r\n",
                    ManagedHttpParseFailureReason.ContentLength), "length-non-decimal");
        Check(Fails("HTTP/1.1 200 OK\r\nContent-Length: 999999999999999999999999\r\n" +
                    "Connection: close\r\n\r\n",
                    ManagedHttpParseFailureReason.ContentLengthOverflow), "length-overflow");

        ManagedHttpResponseParser bounded = new(3, false, true);
        Check(!bounded.Feed(Ascii("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\n")) &&
              bounded.FailureReason == ManagedHttpParseFailureReason.BodyTooLarge,
              "length-configured-limit");
        ManagedHttpResponseParser incomplete = new(4, false, true);
        Check(incomplete.Feed(Ascii("HTTP/1.1 200 OK\r\nContent-Length: 4\r\n\r\nab")) &&
              !incomplete.NotifyConnectionClosed() &&
              incomplete.FailureReason == ManagedHttpParseFailureReason.PrematureConnectionClose,
              "length-premature-eof");

        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: gzip\r\n\r\n",
                    ManagedHttpParseFailureReason.UnsupportedTransferEncoding),
              "transfer-unsupported-coding");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked, gzip\r\n\r\n",
                    ManagedHttpParseFailureReason.UnsupportedTransferEncoding),
              "transfer-chunked-not-final-only");
        Check(Fails("HTTP/1.1 200 OK\r\nContent-Length: 1\r\n" +
                    "Transfer-Encoding: chunked\r\n\r\n",
                    ManagedHttpParseFailureReason.AmbiguousFraming),
              "transfer-content-length-ambiguity");

        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "\r\n", ManagedHttpParseFailureReason.ChunkSizeSyntax),
              "chunk-empty-size");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "Z\r\n", ManagedHttpParseFailureReason.ChunkSizeSyntax),
              "chunk-invalid-hex");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "FFFFFFFFFFFFFFFF\r\n", ManagedHttpParseFailureReason.ChunkSizeOverflow),
              "chunk-size-overflow");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "1001\r\n", ManagedHttpParseFailureReason.ChunkTooLarge),
              "chunk-size-limit");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "1;" + new string('x', ManagedHttpLimits.MaximumChunkExtensionLength) +
                    "\r\n", ManagedHttpParseFailureReason.ChunkSizeSyntax),
              "chunk-extension-limit");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "1\r\naX", ManagedHttpParseFailureReason.ChunkDataCrlf),
              "chunk-data-crlf-required");
        ManagedHttpResponseParser missingZero = NewParser();
        Check(missingZero.Feed(Ascii("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                                     "1\r\na\r\n")) &&
              !missingZero.NotifyConnectionClosed() &&
              missingZero.FailureReason == ManagedHttpParseFailureReason.PrematureConnectionClose,
              "chunk-missing-zero-eof");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "0\r\nbad\r\n", ManagedHttpParseFailureReason.HeaderSyntax),
              "chunk-malformed-trailer");
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    "0\r\nContent-Length: 0\r\n\r\n",
                    ManagedHttpParseFailureReason.TrailerFramingField),
              "chunk-framing-trailer-rejected");

        string longTrailers = "0\r\n" + "X-A: " + new string('x', 80) + "\r\n" +
                              "X-B: " + new string('x', 80) + "\r\n" +
                              "X-C: " + new string('x', 80) + "\r\n";
        Check(Fails("HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
                    longTrailers, ManagedHttpParseFailureReason.TrailerBytes),
              "chunk-trailer-total-limit");
    }

    private static void TestBoundedBackpressureAndDigest()
    {
        const int bodyLength = 4097;
        byte[] body = new byte[bodyLength];
        for (int index = 0; index != body.Length; ++index)
            body[index] = (byte)(index & 0xFF);
        byte[] prefix = Ascii("HTTP/1.1 200 OK\r\nContent-Length: " + bodyLength +
                              "\r\nConnection: close\r\n\r\n");
        byte[] response = new byte[prefix.Length + body.Length];
        prefix.CopyTo(response, 0);
        body.CopyTo(response, prefix.Length);

        ManagedHttpResponseParser parser = NewParser();
        Check(parser.TryFeed(response, out int consumed) && consumed < response.Length &&
              parser.BufferedBodyLength == ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "backpressure-stops-at-delivery-window");
        int offset = consumed;
        int maximumBuffered = parser.BufferedBodyLength;
        ManagedSha256 digest = new();
        int delivered = 0;
        byte[] readBuffer = new byte[73];
        while (offset != response.Length || parser.HasPendingBody)
        {
            while (parser.TryReadBodyChunk(readBuffer, out int read))
            {
                Check(digest.Append(readBuffer.AsSpan(0, read)),
                      "backpressure-digest-append");
                delivered += read;
            }
            if (offset == response.Length) break;
            Check(parser.TryFeed(response.AsSpan(offset), out int next),
                  "backpressure-resume-feed");
            Check(next != 0, "backpressure-makes-progress-after-drain");
            offset += next;
            maximumBuffered = Math.Max(maximumBuffered, parser.BufferedBodyLength);
        }
        while (parser.TryReadBodyChunk(readBuffer, out int finalRead))
        {
            Check(digest.Append(readBuffer.AsSpan(0, finalRead)),
                  "backpressure-final-digest-append");
            delivered += finalRead;
        }
        byte[] actualDigest = new byte[ManagedSha256.DigestSize];
        Check(digest.TryFinalize(actualDigest), "backpressure-digest-finalize");
        byte[] expectedDigest = new byte[ManagedSha256.DigestSize];
        Check(ManagedSha256.TryHash(body, expectedDigest) &&
              actualDigest.AsSpan().SequenceEqual(expectedDigest),
              "backpressure-streaming-digest");
        Check(parser.IsBodyComplete && parser.BodyLength == bodyLength &&
              delivered == bodyLength && maximumBuffered <= ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "backpressure-count-and-fixed-window");
    }

    private static void TestHttpsCompositionAndSecurityBoundary()
    {
        byte[] lengthBody = Ascii("phase33-content-length-pass");
        HttpsFixtureBackend lengthBackend = new(HttpsFixtureScenario.Valid);
        Check(RunHttps(lengthBackend, "/phase33-length"u8.ToArray(), lengthBody.Length,
                       lengthBody, out ManagedHttpsClient lengthClient) &&
              lengthClient.FramingMode == ManagedHttpFramingMode.ContentLength,
              "https-content-length-authoritative");

        byte[] chunkedBody = Ascii("phase33-http-pass");
        HttpsFixtureBackend chunkedBackend = new(HttpsFixtureScenario.Valid);
        Check(RunHttps(chunkedBackend, "/phase33-chunked"u8.ToArray(), chunkedBody.Length,
                       chunkedBody, out ManagedHttpsClient chunkedClient) &&
              chunkedClient.FramingMode == ManagedHttpFramingMode.Chunked,
              "https-chunked-authoritative");

        HttpsFixtureBackend streamBackend = new(HttpsFixtureScenario.Valid);
        Check(RunHttps(streamBackend, "/phase33-stream"u8.ToArray(), 4097, null,
                       out ManagedHttpsClient streamClient) &&
              streamClient.ResponseBodyLength == 4097 &&
              streamClient.ResponseBodyBytesDelivered == 4097,
              "https-large-body-streaming");

        HttpsFixtureBackend malformed = new(HttpsFixtureScenario.MalformedChunk);
        Check(!RunHttps(malformed, "/phase33-malformed"u8.ToArray(), 0, null,
                        out ManagedHttpsClient malformedClient) &&
              malformedClient.State == ManagedHttpsClientState.Failed &&
              malformedClient.FailureReason == ManagedHttpsFailureReason.HttpParseFailure &&
              malformedClient.ResponseBodyBytesDelivered == 4,
              "https-malformed-later-chunk-fails-after-partial-data");

        HttpsFixtureBackend badTag = new(HttpsFixtureScenario.CorruptLaterBodyTag);
        Check(!RunHttps(badTag, "/phase33-bad-tag"u8.ToArray(), 0, null,
                        out ManagedHttpsClient badTagClient) &&
              badTagClient.State == ManagedHttpsClientState.Failed &&
              badTagClient.FailureReason == ManagedHttpsFailureReason.TlsAuthenticationFailure,
              "https-corrupt-aead-never-completes");

        HttpsFixtureBackend fatal = new(HttpsFixtureScenario.FatalAlert);
        Check(!RunHttps(fatal, "/phase33-alert"u8.ToArray(), 0, null,
                        out ManagedHttpsClient fatalClient) &&
              fatalClient.State == ManagedHttpsClientState.Failed &&
              fatalClient.FailureReason == ManagedHttpsFailureReason.TlsProtocolFailure,
              "https-fatal-alert-terminates-stream");

        HttpsFixtureBackend close = new(HttpsFixtureScenario.CloseMidHttp);
        Check(!RunHttps(close, "/phase33-close"u8.ToArray(), 0, null,
                        out ManagedHttpsClient closeClient) &&
              closeClient.State == ManagedHttpsClientState.Failed &&
              closeClient.FailureReason == ManagedHttpsFailureReason.PrematureConnectionClose,
              "https-close-mid-framing-fails");

        Check(lengthClient.Reset() == NetworkOperationResult.Success &&
              chunkedClient.Reset() == NetworkOperationResult.Success &&
              streamClient.Reset() == NetworkOperationResult.Success,
              "https-parser-teardown-reuse");
    }

    private static bool RunHttps(HttpsFixtureBackend backend, byte[] path,
                                 int expectedLength, byte[]? expected,
                                 out ManagedHttpsClient client)
    {
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        client = new ManagedHttpsClient(service, ManagedTls12Phase31Fixtures.Root,
                                        in TestTime, CreateRandom(),
                                        ManagedHttpLimits.MaximumAcceptedBodyLength);
        if (client.BeginGet("www.example.com"u8, path) != NetworkOperationResult.Started)
            return false;
        ManagedSha256 digest = new();
        byte[] readBuffer = new byte[73];
        byte[]? observed = expected == null ? null : new byte[expected.Length];
        int observedLength = 0;
        for (int count = 0; count != 3000 &&
             client.State != ManagedHttpsClientState.Succeeded &&
             client.State != ManagedHttpsClientState.Failed; ++count)
        {
            if (client.Poll() == NetworkOperationResult.Failed) break;
            DrainHttpsBody(client, digest, readBuffer, observed, ref observedLength);
        }
        DrainHttpsBody(client, digest, readBuffer, observed, ref observedLength);
        if (client.State != ManagedHttpsClientState.Succeeded ||
            !client.ResponseBodyComplete || client.ResponseBodyLength != expectedLength ||
            observed != null && (observedLength != expectedLength ||
                                  !observed.AsSpan().SequenceEqual(expected)))
        {
            return false;
        }
        byte[] actualDigest = new byte[ManagedSha256.DigestSize];
        if (!digest.TryFinalize(actualDigest)) return false;
        if (expected != null)
        {
            byte[] expectedDigest = new byte[ManagedSha256.DigestSize];
            if (!ManagedSha256.TryHash(expected, expectedDigest) ||
                !actualDigest.AsSpan().SequenceEqual(expectedDigest)) return false;
        }
        else
        {
            byte[] streamBody = new byte[expectedLength];
            for (int index = 0; index != streamBody.Length; ++index)
                streamBody[index] = (byte)(index & 0xFF);
            byte[] expectedDigest = new byte[ManagedSha256.DigestSize];
            if (!ManagedSha256.TryHash(streamBody, expectedDigest) ||
                !actualDigest.AsSpan().SequenceEqual(expectedDigest)) return false;
        }
        return true;
    }

    private static void DrainHttpsBody(ManagedHttpsClient client, ManagedSha256 digest,
                                       byte[] readBuffer, byte[]? observed,
                                       ref int observedLength)
    {
        while (client.TryReadResponseBodyChunk(readBuffer, out int length))
        {
            if (!digest.Append(readBuffer.AsSpan(0, length)))
                throw new InvalidOperationException("HTTPS digest append failed");
            if (observed != null)
            {
                readBuffer.AsSpan(0, length).CopyTo(observed.AsSpan(observedLength));
                observedLength += length;
            }
        }
    }

    private static ManagedHttpResponseParser NewParser() =>
        new(ManagedHttpLimits.MaximumAcceptedBodyLength, false, true);

    private static bool FeedOneByteAtATime(ManagedHttpResponseParser parser,
                                           byte[] value)
    {
        for (int index = 0; index != value.Length; ++index)
            if (!parser.TryFeed(value.AsSpan(index, 1), out int consumed) || consumed != 1)
                return false;
        return true;
    }

    private static byte[] ReadBody(ManagedHttpResponseParser parser)
    {
        byte[] body = new byte[parser.BodyLength];
        int offset = 0;
        byte[] buffer = new byte[73];
        while (parser.TryReadBodyChunk(buffer, out int length))
        {
            buffer.AsSpan(0, length).CopyTo(body.AsSpan(offset));
            offset += length;
        }
        return body;
    }

    private static bool Succeeds(string value, ManagedHttpFramingMode framing)
    {
        ManagedHttpResponseParser parser = NewParser();
        byte[] bytes = Ascii(value);
        return parser.Feed(bytes) && parser.IsBodyComplete &&
               parser.FramingMode == framing;
    }

    private static bool Fails(string value, ManagedHttpParseFailureReason reason)
    {
        ManagedHttpResponseParser parser = NewParser();
        byte[] bytes = Ascii(value);
        bool result = parser.Feed(bytes);
        if (reason == ManagedHttpParseFailureReason.None)
            return result && parser.IsBodyComplete;
        return !result && parser.State == ManagedHttpParseState.Failed &&
               parser.FailureReason == reason;
    }

    private static ManagedSecureRandom CreateRandom()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return new ManagedSecureRandom(new FixedEntropy(entropy));
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException("failed: " + name);
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

    private enum HttpsFixtureScenario : byte
    {
        Valid,
        MalformedChunk,
        CorruptLaterBodyTag,
        FatalAlert,
        CloseMidHttp
    }

    private sealed class HttpsFixtureBackend : IManagedNetworkServiceBackend
    {
        private readonly HttpsFixtureScenario _scenario;
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

        internal HttpsFixtureBackend(HttpsFixtureScenario scenario) => _scenario = scenario;
        internal void Attach(ManagedNetworkService service) => _service = service;
        public bool IsAvailable => true;
        public NetworkStatus GetStatus() => new(true, true, true, true,
            0x021500000002, Local, new Ipv4Address(0xFFFFFF00),
            new Ipv4Address(0x0A0F0001));
        public void SetRuntimeStatus(NetworkStatus status) { }

        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
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
            _tcpState = ManagedTcpConnectionState.SynSent;
            _event = ManagedNetworkServiceBackendEvent.TcpEstablished;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
        {
            if (payload.Length >= 6 &&
                payload[0] == ManagedTls12RecordProtection.Handshake && payload[5] == 1 &&
                !_serverHelloQueued)
            {
                _serverHelloQueued = true;
                QueueServerHandshake();
                return ManagedNetworkServiceBackendResult.Success;
            }
            if (!_serverFlightQueued && payload.Length >= 5 &&
                payload[0] == ManagedTls12RecordProtection.Handshake)
            {
                _serverFlightQueued = true;
                Queue(ManagedTls12Phase31Fixtures.ChangeCipherSpec);
                QueueRecordFragments(ManagedTls12Phase31Fixtures.ServerFinishedRecord);
                return ManagedNetworkServiceBackendResult.Success;
            }
            if (!_responseQueued && payload.Length >= 5 &&
                payload[0] == ManagedTls12RecordProtection.ApplicationData)
            {
                byte[] request = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
                if (!ManagedTls12RecordProtection.TryDecrypt(1,
                        ManagedTls12Phase31Fixtures.KeyBlock[..16],
                        ManagedTls12Phase31Fixtures.KeyBlock[32..36],
                        ManagedTls12RecordProtection.ApplicationData, payload,
                        request, out int requestLength))
                    return ManagedNetworkServiceBackendResult.Failed;
                _responseQueued = true;
                QueueResponse(Encoding.ASCII.GetString(request, 0, requestLength));
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

        private void QueueServerHandshake()
        {
            for (int index = 0; index != ManagedTls12Phase31Fixtures.ServerRecordCount; ++index)
                QueueRecordFragments(ManagedTls12Phase31Fixtures.GetServerRecord(index));
        }

        private void QueueResponse(string request)
        {
            if (_scenario == HttpsFixtureScenario.FatalAlert)
            {
                QueueServerRecord(1, ManagedTls12RecordProtection.Alert, new byte[] { 2, 40 });
                _finQueued = true;
                return;
            }
            if (_scenario == HttpsFixtureScenario.CloseMidHttp)
            {
                QueueApplicationPlaintext(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 100\r\nConnection: close\r\n\r\nabc"),
                    corruptLast: false);
                _finQueued = true;
                return;
            }
            string response;
            if (_scenario == HttpsFixtureScenario.MalformedChunk)
            {
                response = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n" +
                           "Connection: close\r\n\r\n4\r\ntest\r\nZZ\r\n";
            }
            else if (_scenario == HttpsFixtureScenario.CorruptLaterBodyTag)
            {
                response = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n" +
                           "Connection: close\r\n\r\n4\r\ntest\r\n4\r\nfail\r\n0\r\n\r\n";
            }
            else if (request.Contains("/phase33-chunked", StringComparison.Ordinal))
            {
                response = "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n" +
                           "Connection: close\r\n\r\n7\r\nphase33\r\n1\r\n-\r\n" +
                           "4\r\nhttp\r\n1\r\n-\r\n4\r\npass\r\n0\r\n\r\n";
            }
            else if (request.Contains("/phase33-stream", StringComparison.Ordinal))
            {
                byte[] body = new byte[4097];
                for (int index = 0; index != body.Length; ++index)
                    body[index] = (byte)(index & 0xFF);
                byte[] header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 4097\r\n" +
                    "Connection: close\r\n\r\n");
                byte[] combined = new byte[header.Length + body.Length];
                header.CopyTo(combined, 0);
                body.CopyTo(combined, header.Length);
                QueueApplicationPlaintext(combined, corruptLast: false);
                _finQueued = true;
                return;
            }
            else
            {
                byte[] body = Encoding.ASCII.GetBytes("phase33-content-length-pass");
                response = "HTTP/1.1 200 OK\r\nContent-Length: " + body.Length +
                           "\r\nConnection: close\r\n\r\n" + Encoding.ASCII.GetString(body);
            }
            QueueApplicationPlaintext(Encoding.ASCII.GetBytes(response),
                corruptLast: _scenario == HttpsFixtureScenario.CorruptLaterBodyTag);
            _finQueued = true;
        }

        private void QueueApplicationPlaintext(byte[] plaintext, bool corruptLast)
        {
            const int fragment = 180;
            int offset = 0;
            int recordIndex = 0;
            while (offset != plaintext.Length)
            {
                int count = Math.Min(fragment, plaintext.Length - offset);
                QueueServerRecord((ulong)(recordIndex + 1),
                    ManagedTls12RecordProtection.ApplicationData,
                    plaintext.AsSpan(offset, count),
                    corruptLast && offset + count == plaintext.Length);
                offset += count;
                recordIndex++;
            }
        }

        private void QueueServerRecord(ulong sequence, byte type, ReadOnlySpan<byte> plaintext,
                                       bool corrupt = false)
        {
            byte[] record = new byte[ManagedTls12RecordProtection.MaximumRecordSize];
            if (!ManagedTls12RecordProtection.TryEncrypt(sequence,
                    ManagedTls12Phase31Fixtures.KeyBlock[16..32],
                    ManagedTls12Phase31Fixtures.KeyBlock[36..40], type,
                    plaintext, record, out int length))
                throw new InvalidOperationException("fixture record build failed");
            if (corrupt) record[length - 1] ^= 1;
            QueueRecordFragments(record[..length]);
        }

        private void QueueRecordFragments(byte[] record)
        {
            Queue(record[..Math.Min(2, record.Length)]);
            if (record.Length > 2)
            {
                int middle = Math.Min(9, record.Length);
                Queue(record[2..middle]);
                if (middle != record.Length) Queue(record[middle..]);
            }
        }

        private void Queue(byte[] value) => _queued.Add((byte[])value.Clone());
    }
}

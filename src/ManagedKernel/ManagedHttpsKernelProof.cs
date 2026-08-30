using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Bare-metal proof consumer. It knows only the managed service and the
   intentionally deterministic Phase 31 fixture. The fixture provider makes
   the endpoint replayable; the production public ManagedHttpsClient path
   obtains entropy from ManagedKernelContract instead. */
internal sealed class ManagedPhase32TestConsumer
{
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> Path => "/phase32"u8;
    private static ReadOnlySpan<byte> ExpectedBody => "phase32-http-pass"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHttpsClient _client;
    private readonly byte[] _body =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];

    internal ManagedPhase32TestConsumer(ManagedNetworkService service)
    {
        _service = service;
        ManagedSecureRandom random = new(new FixedEntropy(
            CreateDeterministicEntropy()));
        _client = new ManagedHttpsClient(
            service, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random);
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.DhcpBound || !status.Configured ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_NETWORK_READY\r\n"u8) ||
            _client.BeginGet(Hostname, Path) != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_REQUEST_STARTED\r\n"u8))
            return false;

        bool dnsLogged = false;
        bool tcpLogged = false;
        bool tlsLogged = false;
        bool requestLogged = false;
        bool applicationLogged = false;
        bool statusLogged = false;
        bool gcLogged = false;
        bool bodyLogged = false;
        for (int count = 0; count != 512; ++count)
        {
            NetworkOperationResult result = _client.Poll();
            if (result == NetworkOperationResult.Failed ||
                _client.State == ManagedHttpsClientState.Failed)
                return false;

            if (!dnsLogged && _client.ResolvedAddress.IsUsable)
            {
                if (_client.ResolvedAddress.Value != 0x0A0F0002U ||
                    !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_DNS_SUCCESS\r\n"u8) ||
                    !KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_RESOLVED_IPV4=0x"u8,
                        _client.ResolvedAddress.Value))
                    return false;
                dnsLogged = true;
            }
            if (!tcpLogged && _client.State >= ManagedHttpsClientState.Handshaking)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_TCP_CONNECTED\r\n"u8))
                    return false;
                tcpLogged = true;
            }
            if (!tlsLogged && _client.TlsAuthenticated)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_TLS_HANDSHAKE_AUTHENTICATED\r\n"u8))
                    return false;
                tlsLogged = true;
            }
            if (!requestLogged && _client.RequestSent)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_REQUEST_ENCRYPTED_SENT\r\n"u8))
                    return false;
                requestLogged = true;
            }
            if (!applicationLogged && _client.ApplicationDataReceived)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED\r\n"u8))
                    return false;
                applicationLogged = true;
            }
            if (!statusLogged && _client.StatusParsed)
            {
                if (_client.StatusCode != 200 ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_HTTP_STATUS_PARSED=200\r\n"u8))
                    return false;
                statusLogged = true;
            }
            if (statusLogged && !gcLogged)
            {
                GC.Collect();
                if (!_client.StatusParsed || _client.StatusCode != 200 ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_GC_SURVIVAL_PASSED\r\n"u8))
                    return false;
                gcLogged = true;
            }
            if (!bodyLogged && _client.ResponseBodyComplete)
            {
                if (_client.ResponseBodyLength != ExpectedBody.Length ||
                    _client.ContentLength != ExpectedBody.Length ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_RECEIVED\r\n"u8))
                    return false;
                bodyLogged = true;
            }
            if (_client.State != ManagedHttpsClientState.Succeeded) continue;
            if (!dnsLogged || !tcpLogged || !tlsLogged || !requestLogged ||
                !applicationLogged || !statusLogged || !gcLogged || !bodyLogged ||
                !_client.TryCopyResponseBody(_body, out int bodyLength) ||
                bodyLength != ExpectedBody.Length ||
                !_body.AsSpan(0, bodyLength).SequenceEqual(ExpectedBody) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE32_BODY_VERIFIED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE32_TEARDOWN_COMPLETE\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE32_PASS\r\n"u8))
                return false;
            return true;
        }
        return false;
    }

    private static byte[] CreateDeterministicEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;

        internal FixedEntropy(byte[] bytes) => _bytes = bytes;

        public bool IsAvailable => _offset <= _bytes.Length;

        public bool TryFill(Span<byte> destination)
        {
            if (destination.Length > _bytes.Length - _offset)
            {
                if (_offset != _bytes.Length) return false;
                _offset = 0;
            }
            _bytes.AsSpan(_offset, destination.Length).CopyTo(destination);
            _offset += destination.Length;
            return true;
        }
    }
}

/* Phase 33 keeps the same service/TLS boundary as Phase 32 while exercising
   three independently framed responses.  The consumer only receives copied,
   authenticated body chunks; it never sees TLS record storage. */
internal sealed class ManagedPhase33TestConsumer
{
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> LengthPath => "/phase33-length"u8;
    private static ReadOnlySpan<byte> ChunkedPath => "/phase33-chunked"u8;
    private static ReadOnlySpan<byte> StreamPath => "/phase33-stream"u8;
    private static ReadOnlySpan<byte> LengthBody => "phase33-content-length-pass"u8;
    private static ReadOnlySpan<byte> ChunkedBody => "phase33-http-pass"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHttpsClient _client;
    private readonly byte[] _body = new byte[73];

    internal ManagedPhase33TestConsumer(ManagedNetworkService service)
    {
        _service = service;
        ManagedSecureRandom random = new(new FixedEntropy(
            CreateDeterministicEntropy()));
        _client = new ManagedHttpsClient(
            service, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random,
            ManagedHttpLimits.MaximumAcceptedBodyLength,
            compactTlsProfile: false);
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.DhcpBound || !status.Configured ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE33_NETWORK_READY\r\n"u8))
            return false;
        if (!RunRequest(LengthPath, LengthBody, LengthBody.Length,
                        "CONTENT_LENGTH"u8))
            return false;
        if (_client.Reset() != NetworkOperationResult.Success ||
            !RunRequest(ChunkedPath, ChunkedBody, ChunkedBody.Length,
                        "CHUNKED"u8))
            return false;
        if (_client.Reset() != NetworkOperationResult.Success ||
            !RunRequest(StreamPath, ReadOnlySpan<byte>.Empty, 4097, "STREAM"u8))
            return false;
        if (!KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE33_TEARDOWN_COMPLETE\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE33_PASS\r\n"u8))
            return false;
        return true;
    }

    private bool RunRequest(ReadOnlySpan<byte> path, ReadOnlySpan<byte> expected,
                            int expectedLength, ReadOnlySpan<byte> kind)
    {
        if (!KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE33_BEGIN_GET\r\n"u8))
            return false;
        NetworkOperationResult begin = BeginRequest(path);
        if (begin != NetworkOperationResult.Started)
        {
            KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE33_BEGIN_GET_FAILED\r\n"u8);
            return false;
        }
        if (!KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE33_REQUEST_STARTED\r\n"u8))
            return false;
        bool dnsLogged = false;
        bool tcpLogged = false;
        bool tlsLogged = false;
        bool requestLogged = false;
        bool applicationLogged = false;
        bool statusLogged = false;
        bool framingLogged = false;
        bool gcLogged = false;
        int delivered = 0;
        int reads = 0;
        ManagedSha256 digest = new();
        for (int count = 0; count != 4096; ++count)
        {
            NetworkOperationResult result = _client.Poll();
            if (result == NetworkOperationResult.Failed ||
                _client.State == ManagedHttpsClientState.Failed)
                return false;
            if (!dnsLogged && _client.ResolvedAddress.IsUsable)
            {
                if (_client.ResolvedAddress.Value != 0x0A0F0002U ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_DNS_SUCCESS\r\n"u8) ||
                    !KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_RESOLVED_IPV4=0x"u8,
                        _client.ResolvedAddress.Value))
                    return false;
                dnsLogged = true;
            }
            if (!tcpLogged && _client.State >= ManagedHttpsClientState.Handshaking)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_TCP_CONNECTED\r\n"u8))
                    return false;
                tcpLogged = true;
            }
            if (!tlsLogged && _client.TlsAuthenticated)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_TLS_HANDSHAKE_AUTHENTICATED\r\n"u8))
                    return false;
                tlsLogged = true;
            }
            if (!requestLogged && _client.RequestSent)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_HTTP_REQUEST_ENCRYPTED_SENT\r\n"u8))
                    return false;
                requestLogged = true;
            }
            if (!applicationLogged && _client.ApplicationDataReceived)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED\r\n"u8))
                    return false;
                applicationLogged = true;
            }
            if (!statusLogged && _client.StatusParsed)
            {
                if (_client.StatusCode != 200 ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_HTTP_STATUS_PARSED=200\r\n"u8))
                    return false;
                statusLogged = true;
            }
            if (!framingLogged && _client.FramingMode != ManagedHttpFramingMode.None)
            {
                if (!WriteFramingMarker(kind, _client.FramingMode)) return false;
                framingLogged = true;
            }
            if (statusLogged && !gcLogged)
            {
                GC.Collect();
                if (!_client.StatusParsed || _client.StatusCode != 200 ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE33_GC_SURVIVAL_PASSED\r\n"u8))
                    return false;
                gcLogged = true;
            }
            while (_client.TryReadResponseBodyChunk(_body, out int length))
            {
                if (!digest.Append(_body.AsSpan(0, length)) ||
                    !ValidateChunk(kind, expected, delivered, _body.AsSpan(0, length)))
                    return false;
                delivered += length;
                reads++;
            }
            if (_client.State == ManagedHttpsClientState.Succeeded &&
                _client.BufferedResponseBodyLength == 0)
                break;
        }
        if (!dnsLogged || !tcpLogged || !tlsLogged || !requestLogged ||
            !applicationLogged || !statusLogged || !framingLogged || !gcLogged ||
            _client.State != ManagedHttpsClientState.Succeeded ||
            !_client.ResponseBodyComplete || _client.ResponseBodyLength != expectedLength ||
            delivered != expectedLength)
            return false;
        byte[] actualDigest = new byte[ManagedSha256.DigestSize];
        if (!digest.TryFinalize(actualDigest)) return false;
        byte[] expectedDigest = new byte[ManagedSha256.DigestSize];
        ManagedSha256 expectedHash = new();
        if (expectedLength == 4097)
        {
            for (int offset = 0; offset != expectedLength;)
            {
                int length = Math.Min(_body.Length, expectedLength - offset);
                for (int index = 0; index != length; ++index)
                    _body[index] = (byte)((offset + index) & 0xFF);
                if (!expectedHash.Append(_body.AsSpan(0, length))) return false;
                offset += length;
            }
        }
        else if (!expectedHash.Append(expected))
            return false;
        if (!expectedHash.TryFinalize(expectedDigest) ||
            !actualDigest.AsSpan().SequenceEqual(expectedDigest) ||
            (expectedLength == 4097 && reads < 2))
            return false;
        if (!KernelLog.Write(kind.SequenceEqual("STREAM"u8)
                ? "GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_BODY_VERIFIED\r\n"u8
                : "GXOS_NET10:MANAGED_HTTPS_PHASE33_BODY_VERIFIED\r\n"u8) ||
            (expectedLength == 4097 &&
             !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_READS_MULTIPLE\r\n"u8)) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE33_RESPONSE_COMPLETE\r\n"u8))
            return false;
        return true;
    }

    private NetworkOperationResult BeginRequest(ReadOnlySpan<byte> path)
    {
        return _client.BeginGet(Hostname, path);
    }

    private static bool ValidateChunk(ReadOnlySpan<byte> kind,
                                     ReadOnlySpan<byte> expected,
                                     int offset, ReadOnlySpan<byte> chunk)
    {
        if (kind.SequenceEqual("STREAM"u8))
        {
            for (int index = 0; index != chunk.Length; ++index)
                if (chunk[index] != (byte)((offset + index) & 0xFF)) return false;
            return true;
        }
        return offset + chunk.Length <= expected.Length &&
               chunk.SequenceEqual(expected.Slice(offset, chunk.Length));
    }

    private static bool WriteFramingMarker(ReadOnlySpan<byte> kind,
                                           ManagedHttpFramingMode framing)
    {
        if (kind.SequenceEqual("CONTENT_LENGTH"u8) &&
            framing == ManagedHttpFramingMode.ContentLength)
            return KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE33_CONTENT_LENGTH_SELECTED\r\n"u8);
        if (kind.SequenceEqual("CHUNKED"u8) && framing == ManagedHttpFramingMode.Chunked)
            return KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE33_CHUNKED_SELECTED\r\n"u8);
        if (kind.SequenceEqual("STREAM"u8) &&
            framing == ManagedHttpFramingMode.ContentLength)
            return KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE33_STREAM_CONTENT_LENGTH_SELECTED\r\n"u8);
        return false;
    }

    private static byte[] CreateDeterministicEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;

        internal FixedEntropy(byte[] bytes) => _bytes = bytes;

        public bool IsAvailable => _offset <= _bytes.Length;

        public bool TryFill(Span<byte> destination)
        {
            if (destination.Length > _bytes.Length - _offset)
            {
                if (_offset != _bytes.Length) return false;
                _offset = 0;
            }
            _bytes.AsSpan(_offset, destination.Length).CopyTo(destination);
            _offset += destination.Length;
            return true;
        }
    }
}

/* Phase 34 exercises the URL-oriented public operation across multiple
   independently authenticated hops.  The consumer deliberately retains only
   the final body; redirect bodies are consumed by the HTTP framing engine and
   discarded when each connection is torn down. */
internal sealed class ManagedPhase34TestConsumer
{
    private static ReadOnlySpan<byte> StartUrl =>
        "https://www.example.com/phase34/start"u8;
    private static ReadOnlySpan<byte> FinalUrl =>
        "https://other.example.com:8443/phase34/final"u8;
    private static ReadOnlySpan<byte> ExpectedBody => "phase34-redirect-pass"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHttpsClient _client;
    private readonly byte[] _body =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];

    internal ManagedPhase34TestConsumer(ManagedNetworkService service)
    {
        _service = service;
        ManagedSecureRandom random = new(new FixedEntropy(
            CreateDeterministicEntropy()));
        _client = new ManagedHttpsClient(
            service, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random);
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.DhcpBound || !status.Configured ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE34_NETWORK_READY\r\n"u8))
            return false;
        if (_client.BeginGetUrl(StartUrl) != NetworkOperationResult.Started ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE34_REQUEST_STARTED\r\n"u8))
            return false;
        ManagedNetworkServiceBackend.LiveEthernet?.EnablePhase34Polling();

        int observedRedirects = 0;
        int observedHop = -1;
        bool dnsLogged = false;
        bool tcpLogged = false;
        bool tlsLogged = false;
        bool requestLogged = false;
        bool applicationLogged = false;
        bool finalStatusLogged = false;
        bool finalBodyLogged = false;
        bool redirectStatusLogged = false;
        bool closingLogged = false;

        for (int count = 0; count != 65536; ++count)
        {
            NetworkOperationResult result = _client.Poll();
            if (result == NetworkOperationResult.Failed ||
                _client.State == ManagedHttpsClientState.Failed)
                return false;

            int hop = _client.RedirectCount;
            bool hopChanged = hop != observedHop;
            if (hopChanged)
            {
                observedHop = hop;
                dnsLogged = false;
                tcpLogged = false;
                tlsLogged = false;
                requestLogged = false;
                applicationLogged = false;
                redirectStatusLogged = false;
                closingLogged = false;
            }
            if (!dnsLogged && _client.ResolvedAddress.IsUsable)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_DNS_SUCCESS\r\n"u8) ||
                    !KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_RESOLVED_IPV4=0x"u8,
                        _client.ResolvedAddress.Value))
                    return false;
                dnsLogged = true;
            }
            if (!tcpLogged && _client.State >= ManagedHttpsClientState.Handshaking)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_TCP_CONNECTED\r\n"u8))
                    return false;
                tcpLogged = true;
            }
            if (!tlsLogged && _client.TlsAuthenticated)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_HANDSHAKE_AUTHENTICATED\r\n"u8))
                    return false;
                tlsLogged = true;
            }
            if (!requestLogged && _client.RequestSent)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REQUEST_ENCRYPTED_SENT\r\n"u8))
                    return false;
                requestLogged = true;
            }
            if (!applicationLogged && _client.ApplicationDataReceived)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED\r\n"u8))
                    return false;
                applicationLogged = true;
            }
            if (_client.StatusParsed &&
                (_client.StatusCode == 301 || _client.StatusCode == 302 ||
                 _client.StatusCode == 303 || _client.StatusCode == 307 ||
                 _client.StatusCode == 308) &&
                _client.RedirectCount == observedRedirects &&
                !hopChanged &&
                !redirectStatusLogged)
            {
                if (!KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REDIRECT_STATUS=0x"u8,
                        (ulong)_client.StatusCode) ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_LOCATION_PARSED\r\n"u8))
                    return false;
                redirectStatusLogged = true;
            }
            if (_client.RedirectCount > observedRedirects)
            {
                observedRedirects = _client.RedirectCount;
                ManagedNetworkServiceBackend.LiveEthernet?
                    .EnablePhase34ClosingPolling();
                if (!KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_REDIRECT_FOLLOWED=0x"u8,
                        (ulong)observedRedirects) ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_REDIRECT_TEARDOWN_PENDING\r\n"u8))
                    return false;
            }
            if (_client.State == ManagedHttpsClientState.Resolving)
            {
                ManagedNetworkServiceBackend.LiveEthernet?
                    .EnablePhase34HandshakePolling();
            }
            if (_client.StatusParsed && _client.StatusCode == 200 &&
                !finalStatusLogged)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_STATUS_PARSED=200\r\n"u8))
                    return false;
                finalStatusLogged = true;
            }
            if (!finalBodyLogged && _client.StatusParsed &&
                _client.StatusCode == 200 && _client.ResponseBodyComplete)
            {
                ManagedNetworkServiceBackend.LiveEthernet?
                    .EnablePhase34ClosingPolling();
                if (_client.ResponseBodyLength != ExpectedBody.Length ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_RECEIVED\r\n"u8))
                    return false;
                finalBodyLogged = true;
            }
            if (!closingLogged && _client.State == ManagedHttpsClientState.Closing)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE34_HOP_CLOSING\r\n"u8))
                    return false;
                closingLogged = true;
            }
            if (_client.State != ManagedHttpsClientState.Succeeded) continue;

            Span<byte> finalUrl = stackalloc byte[ManagedHttpsUrl.MaximumUrlLength];
            if (!finalStatusLogged || !finalBodyLogged ||
                _client.RedirectCount != 3 ||
                !_client.TryCopyResponseBody(_body, out int bodyLength) ||
                bodyLength != ExpectedBody.Length ||
                !_body.AsSpan(0, bodyLength).SequenceEqual(ExpectedBody) ||
                !_client.FinalUrl.TryCopyAbsoluteUrl(finalUrl, out int finalUrlLength) ||
                !finalUrl[..finalUrlLength].SequenceEqual(FinalUrl) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_URL_VERIFIED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_VERIFIED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_TEARDOWN_COMPLETE\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS\r\n"u8))
                return false;
            return true;
        }
        return false;
    }

    private static byte[] CreateDeterministicEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;

        internal FixedEntropy(byte[] bytes) => _bytes = bytes;

        public bool IsAvailable => _offset <= _bytes.Length;

        public bool TryFill(Span<byte> destination)
        {
            if (destination.Length > _bytes.Length - _offset)
            {
                if (_offset != _bytes.Length) return false;
                _offset = 0;
            }
            _bytes.AsSpan(_offset, destination.Length).CopyTo(destination);
            _offset += destination.Length;
            return true;
        }
    }
}

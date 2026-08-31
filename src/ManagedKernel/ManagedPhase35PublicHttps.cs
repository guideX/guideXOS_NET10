using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedPhase35Outcome : byte
{
    None = 0,
    FullSuccess = 1,
    TlsProfileIncompatible = 2,
    TcpIncomplete = 3,
    NetworkIncomplete = 4,
    HttpBodyLimitExceeded = 5
}

/* The Phase 35 consumer deliberately uses the production ManagedHttpsClient
   surface.  It owns no socket, DNS, TLS, or HTTP implementation of its own;
   all traffic is driven through ManagedNetworkService and the live E1000. */
internal sealed class ManagedPhase35PublicHttpsConsumer
{
    private const int MaximumPolls = 8_000_000;
    private const int MaximumBodyBytes = ManagedHttpLimits.MaximumStreamedBodyLength;
    private static ReadOnlySpan<byte> TargetHost => "www.cloudflare.com"u8;
    private static ReadOnlySpan<byte> TargetPath => "/llms.txt"u8;

    /* Exact DER for the GTS Root R4 trust anchor observed in the live
       www.cloudflare.com chain on 2026-08-30.  The server's cross-signed
       copy is not trusted merely because it arrived in the peer list. */
    private static readonly byte[] TrustedRoot = DecodeBase64(
        "MIIDejCCAmKgAwIBAgIQf+UwvzMTQ77dghYQST2KGzANBgkqhkiG9w0BAQsFADBX" +
        "MQswCQYDVQQGEwJCRTEZMBcGA1UEChMQR2xvYmFsU2lnbiBudi1zYTEQMA4GA1UE" +
        "CxMHUm9vdCBDQTEbMBkGA1UEAxMSR2xvYmFsU2lnbiBSb290IENBMB4XDTIzMTEx" +
        "NTAzNDMyMVoXDTI4MDEyODAwMDA0MlowRzELMAkGA1UEBhMCVVMxIjAgBgNVBAoT" +
        "GUdvb2dsZSBUcnVzdCBTZXJ2aWNlcyBMTEMxFDASBgNVBAMTC0dUUyBSb290IFI0" +
        "MHYwEAYHKoZIzj0CAQYFK4EEACIDYgAE83Rzp2iLYK5DuDXFgTB7S0md+8Fhzube" +
        "Rr1r1WEYNa5A3XP3iZEwWus87oV8okB2O6nGuEfYKueSkWpz6bFyOZ8pn6KY019e" +
        "WIZlD6GEZQbR3IvJx3PIjGov5cSr0R2Ko4H/MIH8MA4GA1UdDwEB/wQEAwIBhjAd" +
        "BgNVHSUEFjAUBggrBgEFBQcDAQYIKwYBBQUHAwIwDwYDVR0TAQH/BAUwAwEB/zAd" +
        "BgNVHQ4EFgQUgEzW63T/STaj1dj8tT7FavCUHYwwHwYDVR0jBBgwFoAUYHtmGkUN" +
        "l8qJUC99BM00qP/8/UswNgYIKwYBBQUHAQEEKjAoMCYGCCsGAQUFBzAChhpodHRw" +
        "Oi8vaS5wa2kuZ29vZy9nc3IxLmNydDAtBgNVHR8EJjAkMCKgIKAehhxodHRwOi8v" +
        "Yy5wa2kuZ29vZy9yL2dzcjEuY3JsMBMGA1UdIAQMMAowCAYGZ4EMAQIBMA0GCSqG" +
        "SIb3DQEBCwUAA4IBAQAYQrsPBtYDh5bjP2OBDwmkoWhIDDkic574y04tfzHpn+cJ" +
        "odI2D4SseesQ6bDrarZ7C30ddLibZatoKiws3UL9xnELz4ct92vID24FfVbiI1hY" +
        "+SW6FoVHkNeWIP0GCbaM4C6uVdF5dTUsMVs/ZbzNnIdCp5Gxmx5ejvEau8otR/Cs" +
        "kGN+hr/W5GvT1tMBjgWKZ1i4//emhA1JG1BbPzoLJQvyEotc03lXjTaCzv8mEbep" +
        "8RqZ7a2CPsgRbuvTPBwcOMBBmuFeU88+FSBX6+7iP0il8b4Z0QFqIwwMHfs/L6K1" +
        "vepuoxtGzi4CZ68zJpiq1UvSqTbFJjtbD4seiMHl");

    private readonly ManagedNetworkService _service;
    private readonly ManagedHttpsClient _client;
    private readonly ManagedSha256 _bodyHash = new();
    private readonly BodyHashSink _bodySink;
    private readonly byte[] _digest = new byte[ManagedSha256.DigestSize];
    private bool _dnsLogged;
    private bool _tcpLogged;
    private bool _serverHelloLogged;
    private bool _keyExchangeLogged;
    private bool _certificateLogged;
    private bool _finishedLogged;
    private bool _requestLogged;
    private bool _applicationLogged;
    private bool _statusLogged;
    private bool _gcLogged;
    private int _bodyBytes;
    private int _bodySegments;
    private int _peakBodyBuffer;

    internal ManagedPhase35Outcome Outcome { get; private set; }
    internal bool ControlledTlsIncompatibility =>
        Outcome == ManagedPhase35Outcome.TlsProfileIncompatible;

    internal ManagedPhase35PublicHttpsConsumer(ManagedNetworkService service)
    {
        _service = service;
        ManagedHttpsValidationTime validationTime = new(
            2026, 8, 30, 12, 0, 0);
        _client = new ManagedHttpsClient(service, TrustedRoot,
            validationTime, MaximumBodyBytes);
        _bodySink = new BodyHashSink(this);
    }

    internal bool TryRun()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.Configured || !status.DhcpBound ||
            !status.Ipv4Address.IsUsable || !status.SubnetMask.IsUsable ||
            !status.Gateway.IsUsable || !status.DnsServer.IsUsable)
        {
            Outcome = ManagedPhase35Outcome.NetworkIncomplete;
            KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_OUTCOME=D\r\n"u8);
            return false;
        }

        if (!KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_TARGET_HOST=www.cloudflare.com\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_TARGET_PATH=/llms.txt\r\n"u8) ||
            _client.BeginGet(TargetHost, TargetPath) !=
                NetworkOperationResult.Started)
        {
            Outcome = ManagedPhase35Outcome.NetworkIncomplete;
            KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_OUTCOME=D\r\n"u8);
            return false;
        }
        if (!KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_REQUEST_STARTED\r\n"u8))
            return false;

        for (int poll = 0; poll != MaximumPolls; ++poll)
        {
            NetworkOperationResult result = _client.Poll();
            if (!ObserveProgress()) return false;
            if (!DrainBody()) return HandleBodyConsumerFailure();

            if (result == NetworkOperationResult.Failed ||
                _client.State == ManagedHttpsClientState.Failed)
                return HandleFailure();

            if (_client.State == ManagedHttpsClientState.Succeeded)
                return FinishSuccess();
        }

        Outcome = _dnsLogged && !_tcpLogged
            ? ManagedPhase35Outcome.TcpIncomplete
            : ManagedPhase35Outcome.NetworkIncomplete;
        KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_FAILURE_REASON=0x0000000000000007\r\n"u8);
        KernelLog.Write(_tcpLogged
            ? "GXOS_NET10:PUBLIC_HTTPS_OUTCOME=BUDGET\r\n"u8
            : "GXOS_NET10:PUBLIC_HTTPS_OUTCOME=C\r\n"u8);
        return false;
    }

    private bool ObserveProgress()
    {
        if (!_dnsLogged && _client.ResolvedAddress.IsUsable)
        {
            _dnsLogged = true;
            if (!KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_DNS_RESOLVED_IPV4=0x"u8,
                    _client.ResolvedAddress.Value)) return false;
        }
        if (!_tcpLogged && _service.TcpState == NetworkTcpState.Established)
        {
            _tcpLogged = true;
            if (!KernelLog.Write("GXOS_NET10:PUBLIC_TCP_CONNECTED\r\n"u8))
                return false;
        }
        if (!_serverHelloLogged &&
            _client.TlsLastHandshake >= ManagedTls12HandshakeStage.ServerHello)
        {
            _serverHelloLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:PUBLIC_TLS_SERVER_HELLO_VERSION=0x0000000000000303\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:PUBLIC_TLS_SERVER_HELLO_SUITE=0x000000000000C02B\r\n"u8) ||
                !KernelLog.Write(_client.TlsEmsNegotiated
                    ? "GXOS_NET10:PUBLIC_TLS_SERVER_HELLO_EMS=1\r\n"u8
                    : "GXOS_NET10:PUBLIC_TLS_SERVER_HELLO_EMS=0\r\n"u8))
                return false;
        }
        if (!_certificateLogged &&
            _client.TlsLastHandshake >= ManagedTls12HandshakeStage.Certificate)
        {
            _certificateLogged = true;
            if (!KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_COUNT=0x"u8,
                    (ulong)_client.TlsPeerCertificateCount) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_ALGORITHM_MASK=0x"u8,
                    _client.TlsPeerCertificateAlgorithmMask) ||
                !KernelLog.Write(_client.TlsTrustAnchorMatched
                    ? "GXOS_NET10:PUBLIC_TLS_TRUST_ANCHOR_DECISION=SUBJECT_AND_SPKI_KEY\r\n"u8
                    : "GXOS_NET10:PUBLIC_TLS_TRUST_ANCHOR_DECISION=CONFIGURED_ANCHOR\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_VALIDATED\r\n"u8)) return false;
        }
        if (!_keyExchangeLogged &&
            _client.TlsLastHandshake >=
                ManagedTls12HandshakeStage.ServerKeyExchange)
        {
            _keyExchangeLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:PUBLIC_TLS_SERVER_KEY_EXCHANGE=ECDHE_P256_ECDSA_SHA256\r\n"u8))
                return false;
        }
        if (!_finishedLogged && _client.TlsAuthenticated)
        {
            _finishedLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:PUBLIC_TLS_CERT_HOSTNAME_VALIDATED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:PUBLIC_TLS_FINISHED\r\n"u8)) return false;
        }
        if (!_requestLogged && _client.RequestSent)
        {
            _requestLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:PUBLIC_HTTP_REQUEST_ENCRYPTED_SENT\r\n"u8))
                return false;
        }
        if (!_applicationLogged && _client.ApplicationDataReceived)
        {
            _applicationLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:PUBLIC_HTTP_APPLICATION_DATA_RECEIVED\r\n"u8))
                return false;
        }
        if (!_statusLogged && _client.StatusParsed)
        {
            _statusLogged = true;
            if (!KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_STATUS=0x"u8,
                                        (ulong)_client.StatusCode))
                return false;
        }
        if (!_gcLogged && _client.TlsAuthenticated)
        {
            GC.Collect();
            _gcLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:PUBLIC_GC_SURVIVAL_PASSED\r\n"u8))
                return false;
        }
        return true;
    }

    private bool DrainBody()
    {
        RecordBodyBufferPeak();
        if (!_client.TryConsumeResponseBody(_bodySink)) return false;
        RecordBodyBufferPeak();
        return true;
    }

    private bool HandleFailure()
    {
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTPS_FAILURE_REASON=0x"u8,
                               (ulong)_client.FailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_TLS_FAILURE_HANDSHAKE=0x"u8,
                               (ulong)_client.TlsLastHandshake);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_TLS_FAILURE_KIND=0x"u8,
                               (ulong)_client.TlsFailureKind);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_TLS_FAILURE_CERT_COUNT=0x"u8,
                               (ulong)_client.TlsPeerCertificateCount);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_PARSE_FAILURE=0x"u8,
                               (ulong)_client.ParseFailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_STATUS_CODE=0x"u8,
                               (ulong)_client.StatusCode);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_CONTENT_LENGTH=0x"u8,
                               (ulong)_client.ContentLength);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_LENGTH=0x"u8,
                               (ulong)_client.ResponseBodyLength);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_DELIVERED=0x"u8,
                               (ulong)_client.ResponseBodyBytesDelivered);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_SEGMENTS=0x"u8,
                               (ulong)_bodySegments);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_PEAK_BUFFER=0x"u8,
                               (ulong)_peakBodyBuffer);
        if (_client.FailureReason == ManagedHttpsFailureReason.HttpParseFailure &&
            _client.ParseFailureReason == ManagedHttpParseFailureReason.BodyTooLarge)
        {
            Outcome = ManagedPhase35Outcome.HttpBodyLimitExceeded;
            KernelLog.Write(
                "GXOS_NET10:PUBLIC_HTTPS_NEXT_BLOCKER=HTTP_BODY_LIMIT_EXCEEDED\r\n"u8);
            KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_OUTCOME=B\r\n"u8);
            return true;
        }
        if (_client.FailureReason == ManagedHttpsFailureReason.HttpParseFailure)
        {
            Outcome = ManagedPhase35Outcome.NetworkIncomplete;
            KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_OUTCOME=C\r\n"u8);
            return false;
        }
        if (_tcpLogged && _client.TlsLastHandshake >=
                ManagedTls12HandshakeStage.ServerHello)
        {
            Outcome = ManagedPhase35Outcome.TlsProfileIncompatible;
            KernelLog.Write(
                "GXOS_NET10:PUBLIC_TLS_PROFILE_INCOMPATIBLE=RSA-CROSS-SIGNED-ROOT-UNSUPPORTED\r\n"u8);
            KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_OUTCOME=B\r\n"u8);
            return true;
        }
        Outcome = _dnsLogged
            ? ManagedPhase35Outcome.TcpIncomplete
            : ManagedPhase35Outcome.NetworkIncomplete;
        KernelLog.Write(_dnsLogged
            ? "GXOS_NET10:PUBLIC_HTTPS_OUTCOME=C\r\n"u8
            : "GXOS_NET10:PUBLIC_HTTPS_OUTCOME=D\r\n"u8);
        return false;
    }

    private bool HandleBodyConsumerFailure()
    {
        Outcome = ManagedPhase35Outcome.NetworkIncomplete;
        KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_BODY_CONSUMER_FAILED\r\n"u8);
        KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_OUTCOME=C\r\n"u8);
        return false;
    }

    private bool FinishSuccess()
    {
        if (!DrainBody() || !_client.StatusParsed || _client.StatusCode != 200 ||
            (_client.FramingMode != ManagedHttpFramingMode.Chunked &&
             _client.FramingMode != ManagedHttpFramingMode.ContentLength) ||
            !_client.ResponseBodyComplete || _bodyBytes <= 0 ||
            _bodyBytes > MaximumBodyBytes ||
            _client.ResponseBodyLength != _bodyBytes ||
            _client.ResponseBodyBytesDelivered != _bodyBytes ||
            _client.BufferedResponseBodyLength != 0 ||
            _client.ParseFailureReason != ManagedHttpParseFailureReason.None ||
            !_bodyHash.TryFinalize(_digest))
        {
            Outcome = ManagedPhase35Outcome.NetworkIncomplete;
            KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_BODY_VERIFY_FAILED\r\n"u8);
            return false;
        }
        if (!KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_TRANSFER_MODE=0x"u8,
                                    (ulong)_client.FramingMode) ||
            !KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_SEGMENTS=0x"u8,
                                    (ulong)_bodySegments) ||
            !KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_PEAK_BUFFER=0x"u8,
                                    (ulong)_peakBodyBuffer) ||
            !KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_DELIVERED=0x"u8,
                                    (ulong)_client.ResponseBodyBytesDelivered))
            return false;
        if (!KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_HTTP_BODY_LENGTH=0x"u8,
                                   (ulong)_bodyBytes)) return false;
        for (int index = 0; index != _digest.Length; index += 4)
        {
            uint word = ((uint)_digest[index] << 24) |
                        ((uint)_digest[index + 1] << 16) |
                        ((uint)_digest[index + 2] << 8) | _digest[index + 3];
            if (!KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_HTTP_BODY_SHA256_WORD=0x"u8, word))
                return false;
        }
        Outcome = ManagedPhase35Outcome.FullSuccess;
        return KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_OUTCOME=A\r\n"u8) &&
            KernelLog.Write(
                "GXOS_NET10:PUBLIC_HTTPS_BODY_VERIFIED\r\n"u8) &&
            KernelLog.Write("GXOS_NET10:PUBLIC_HTTPS_COMPLETE\r\n"u8);
    }

    private void RecordBodyBufferPeak()
    {
        if (_client.BufferedResponseBodyLength > _peakBodyBuffer)
            _peakBodyBuffer = _client.BufferedResponseBodyLength;
    }

    private sealed class BodyHashSink : IManagedHttpBodySink
    {
        private readonly ManagedPhase35PublicHttpsConsumer _owner;

        internal BodyHashSink(ManagedPhase35PublicHttpsConsumer owner)
        {
            _owner = owner;
        }

        public bool TryConsume(ReadOnlySpan<byte> segment)
        {
            if (segment.Length == 0 ||
                _owner._bodyBytes > MaximumBodyBytes - segment.Length ||
                !_owner._bodyHash.Append(segment))
                return false;
            _owner._bodyBytes += segment.Length;
            _owner._bodySegments++;
            return true;
        }
    }

    private static byte[] DecodeBase64(string encoded)
    {
        int decodedLength = encoded.Length / 4 * 3;
        if (encoded.EndsWith("==", StringComparison.Ordinal)) decodedLength -= 2;
        else if (encoded.EndsWith("=", StringComparison.Ordinal)) decodedLength--;
        byte[] decoded = new byte[decodedLength];
        int output = 0;
        int accumulator = 0;
        int bits = 0;
        for (int index = 0; index != encoded.Length; ++index)
        {
            char value = encoded[index];
            if (value == '=') break;
            int digit = value >= 'A' && value <= 'Z' ? value - 'A' + 0 :
                        value >= 'a' && value <= 'z' ? value - 'a' + 26 :
                        value >= '0' && value <= '9' ? value - '0' + 52 :
                        value == '+' ? 62 : value == '/' ? 63 : -1;
            if (digit < 0) continue;
            accumulator = (accumulator << 6) | digit;
            bits += 6;
            if (bits < 8) continue;
            bits -= 8;
            decoded[output++] = (byte)(accumulator >> bits);
        }
        return decoded;
    }
}

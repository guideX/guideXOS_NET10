using System;

namespace GuideXOS.Net10.ManagedKernel;

public readonly struct ManagedHttpsValidationTime
{
    public ManagedHttpsValidationTime(int year, int month, int day, int hour,
                                      int minute, int second)
    {
        Year = year;
        Month = month;
        Day = day;
        Hour = hour;
        Minute = minute;
        Second = second;
        IsValid = year >= 0 && year <= 9999 && month >= 1 && month <= 12 &&
                  day >= 1 && day <= DaysInMonth(year, month) &&
                  hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59 &&
                  second >= 0 && second <= 59;
    }

    public int Year { get; }
    public int Month { get; }
    public int Day { get; }
    public int Hour { get; }
    public int Minute { get; }
    public int Second { get; }
    public bool IsValid { get; }

    internal ManagedX509UtcTime ToX509Time() =>
        new(Year, Month, Day, Hour, Minute, Second);

    private static int DaysInMonth(int year, int month)
    {
        return month switch
        {
            2 => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0
                ? 29 : 28,
            4 or 6 or 9 or 11 => 30,
            _ => 31
        };
    }
}

public enum ManagedHttpsClientState : byte
{
    Idle = 0,
    Resolving = 1,
    Connecting = 2,
    Handshaking = 3,
    Established = 4,
    SendingRequest = 5,
    ReceivingResponse = 6,
    Closing = 7,
    Succeeded = 8,
    Failed = 9,
    Cancelled = 10
}

public enum ManagedHttpsFailureReason : byte
{
    None = 0,
    InvalidRequest = 1,
    RequestTooLarge = 2,
    EntropyUnavailable = 3,
    DnsFailure = 4,
    TcpConnectFailure = 5,
    TcpReset = 6,
    TransportFailure = 7,
    TlsAuthenticationFailure = 8,
    TlsProtocolFailure = 9,
    HttpParseFailure = 10,
    PrematureConnectionClose = 11,
    TeardownFailure = 12,
    Cancelled = 13,
    RedirectMissingLocation = 14,
    RedirectInvalidLocation = 15,
    RedirectLimitExceeded = 16,
    RedirectDowngradeRejected = 17,
    RedirectUnsupportedScheme = 18,
    SinkFailure = 19
}

/* The HTTPS layer speaks to this narrow stream boundary only.  TLS has no
   knowledge of PCB state, Ethernet, or the network service implementation. */
internal interface IManagedTlsTransport
{
    NetworkTcpState State { get; }
    bool HasReceived { get; }
    NetworkOperationResult Poll();
    NetworkOperationResult BeginConnect(Ipv4Address destination,
                                        ushort destinationPort);
    NetworkOperationResult Send(ReadOnlySpan<byte> payload);
    bool TryReceive(Span<byte> destination, out int length);
    NetworkOperationResult Close();
    NetworkOperationResult ReleaseForReuse();
}

internal sealed class ManagedNetworkServiceTlsTransport : IManagedTlsTransport
{
    private readonly ManagedNetworkService _service;

    internal ManagedNetworkServiceTlsTransport(ManagedNetworkService service)
    {
        _service = service;
    }

    public NetworkTcpState State => _service.TcpState;
    public bool HasReceived => _service.HasReceivedTcp;
    public NetworkOperationResult Poll() => _service.Poll();
    public NetworkOperationResult BeginConnect(Ipv4Address destination,
                                               ushort destinationPort) =>
        _service.BeginTcpConnect(destination, destinationPort);
    public NetworkOperationResult Send(ReadOnlySpan<byte> payload) =>
        _service.SendTcp(payload);

    public bool TryReceive(Span<byte> destination, out int length)
    {
        return _service.TryReceiveTcp(destination, out _, out _, out _,
                                      out length);
    }

    public NetworkOperationResult Close() => _service.CloseTcp();
    public NetworkOperationResult ReleaseForReuse() =>
        _service.ReleaseTcpForReuse();
}

public sealed class ManagedHttpsClient
{
    public const ushort HttpsPort = 443;
    public const int MaximumRedirects = 5;
    public const int MaximumTlsTransportPayload =
        ManagedTls12Client.MaximumPendingApplicationBytes;

    private readonly ManagedNetworkService _service;
    private readonly IManagedTlsTransport _transport;
    private readonly byte[] _trustedRoot;
    private readonly ManagedX509UtcTime _validationTime;
    private readonly bool _closeOnHttpCompletion;
    private readonly bool _compactTlsProfile;
    private ManagedHttpsUrl _currentUrl;
    private ManagedHttpsUrl _nextUrl;
    private ManagedSecureRandom? _random;
    private ManagedTls12Client? _tls;
    private readonly byte[] _request =
        new byte[ManagedHttpLimits.MaximumSerializedRequestSize];
    private readonly byte[] _receive =
        new byte[ManagedNetworkService.MaximumTcpPayloadLength];
    private readonly byte[] _tlsOutput =
        new byte[ManagedNetworkService.MaximumTcpPayloadLength];
    private readonly byte[] _plaintext =
        new byte[ManagedTls12Client.MaximumPendingApplicationBytes];
    private readonly byte[] _pendingPlaintext =
        new byte[ManagedTls12Client.MaximumPendingApplicationBytes];
    private readonly byte[] _responseBody =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];
    private readonly ManagedHttpResponseParser _parser;
    private ManagedHttpsClientState _state;
    private ManagedHttpsFailureReason _failureReason;
    private int _requestLength;
    private int _tlsOutputLength;
    private int _pendingPlaintextLength;
    private bool _requestSent;
    private bool _requestOutputReady;
    private bool _tlsAuthenticated;
    private bool _applicationDataReceived;
    private bool _closingStarted;
    private bool _redirectPending;
    private int _redirectCount;
    private ManagedHttpsUrlParseFailureReason _urlParseFailure;
    private ManagedTls12HandshakeStage _lastTlsHandshake =
        ManagedTls12HandshakeStage.ClientHello;
    private ManagedTls12FailureKind _lastTlsFailureKind =
        ManagedTls12FailureKind.Protocol;
    private bool _lastTlsEmsNegotiated;
    private int _lastTlsPeerCertificateCount;
    private byte _lastTlsPeerCertificateAlgorithmMask;
    private bool _lastTlsTrustAnchorMatched;

    public ManagedHttpsClient(ManagedNetworkService service,
                              ReadOnlySpan<byte> trustedRoot,
                              ManagedHttpsValidationTime validationTime,
                              int maximumResponseBodyLength =
                                  ManagedHttpLimits.MaximumBodyCapacity)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _transport = new ManagedNetworkServiceTlsTransport(service);
        _trustedRoot = trustedRoot.ToArray();
        _validationTime = validationTime.ToX509Time();
        _closeOnHttpCompletion = maximumResponseBodyLength >
                                 ManagedHttpLimits.MaximumBodyCapacity;
        _compactTlsProfile = false;
        _parser = new ManagedHttpResponseParser(maximumResponseBodyLength,
                                                requireConnectionClose: false,
                                                allowChunked: true);
        _state = ManagedHttpsClientState.Idle;
    }

    /* The injected constructor is internal on purpose: deterministic fixture
       providers are available to host/QEMU proof code, never to the public
       production API. */
    internal ManagedHttpsClient(ManagedNetworkService service,
                                ReadOnlySpan<byte> trustedRoot,
                                in ManagedX509UtcTime validationTime,
                                ManagedSecureRandom random,
                                int maximumResponseBodyLength =
                                    ManagedHttpLimits.MaximumBodyCapacity,
                                bool compactTlsProfile = false)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _transport = new ManagedNetworkServiceTlsTransport(service);
        _trustedRoot = trustedRoot.ToArray();
        _validationTime = validationTime;
        _closeOnHttpCompletion = maximumResponseBodyLength >
                                 ManagedHttpLimits.MaximumBodyCapacity;
        _compactTlsProfile = compactTlsProfile;
        _random = random;
        _parser = new ManagedHttpResponseParser(maximumResponseBodyLength,
                                                requireConnectionClose: false,
                                                allowChunked: true);
        _state = ManagedHttpsClientState.Idle;
    }

    public ManagedHttpsClientState State => _state;
    public ManagedHttpsFailureReason FailureReason => _failureReason;
    public ManagedHttpsUrlParseFailureReason UrlParseFailureReason =>
        _urlParseFailure;
    public ManagedHttpsUrl CurrentUrl => _currentUrl;
    public ManagedHttpsUrl FinalUrl => _currentUrl;
    public int RedirectCount => _redirectCount;
    public ushort Port => _currentUrl.Port;
    public Ipv4Address ResolvedAddress { get; private set; }
    public bool RequestSent => _requestSent;
    public bool TlsAuthenticated => _tlsAuthenticated;
    public bool ApplicationDataReceived => _applicationDataReceived;
    public bool StatusParsed => _parser.IsStatusParsed;
    public int StatusCode => _parser.StatusCode;
    public int ResponseBodyLength => _parser.BodyLength;
    public int ResponseBodyBytesDelivered => _parser.BodyBytesDelivered;
    public int BufferedResponseBodyLength => _parser.BufferedBodyLength;
    public int DeliveredResponseBodySegmentCount =>
        _parser.DeliveredSegmentCount;
    public int ContentLength => _parser.ContentLength;
    public ManagedHttpContentTypeState ContentTypeState => _parser.ContentTypeState;
    public int ContentTypeLength => _parser.ContentTypeLength;
    public ManagedHttpFramingMode FramingMode => _parser.FramingMode;
    public bool ResponseBodyComplete => _parser.IsBodyComplete;
    public ManagedHttpParseFailureReason ParseFailureReason => _parser.FailureReason;
    public ManagedHttpProgressSnapshot Progress =>
        _parser.CreateProgressSnapshot(GetTransferState(), GetTerminalFailure());
    internal ManagedTls12HandshakeStage TlsLastHandshake => _tls == null
        ? _lastTlsHandshake : _tls.LastHandshake;
    internal NetworkTcpState TcpState => _service.TcpState;
    internal ManagedTls12FailureKind TlsFailureKind => _tls == null
        ? _lastTlsFailureKind : _tls.FailureKind;
    internal bool TlsEmsNegotiated => _tls == null
        ? _lastTlsEmsNegotiated : _tls.EmsNegotiated;
    internal int TlsPeerCertificateCount => _tls == null
        ? _lastTlsPeerCertificateCount : _tls.PeerCertificateCount;
    internal byte TlsPeerCertificateAlgorithmMask => _tls == null
        ? _lastTlsPeerCertificateAlgorithmMask :
          _tls.PeerCertificateAlgorithmMask;
    internal bool TlsTrustAnchorMatched => _tls == null
        ? _lastTlsTrustAnchorMatched : _tls.TrustAnchorMatched;

    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ReadOnlySpan<byte> path)
    {
        return BeginGet(hostname, HttpsPort, path);
    }

    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ushort port,
                                           ReadOnlySpan<byte> path)
    {
        if (!CanBegin()) return NetworkOperationResult.Busy;
        ClearOperationBuffers();
        if (!ManagedHttpsUrl.TryCreate(hostname, port, path,
                                       out ManagedHttpsUrl url))
            return Fail(ManagedHttpsFailureReason.InvalidRequest);
        _currentUrl = url;
        return BeginCurrentHop();
    }

    public NetworkOperationResult BeginGetUrl(ReadOnlySpan<byte> url)
    {
        if (!CanBegin()) return NetworkOperationResult.Busy;
        ClearOperationBuffers();
        if (!ManagedHttpsUrl.TryParse(url, out _currentUrl,
                                      out _urlParseFailure))
            return Fail(ManagedHttpsFailureReason.InvalidRequest);
        return BeginCurrentHop();
    }

    public NetworkOperationResult BeginGetUrl(string url)
    {
        if (url == null) throw new ArgumentNullException(nameof(url));
        if (!CanBegin()) return NetworkOperationResult.Busy;
        ClearOperationBuffers();
        if (!ManagedHttpsUrl.TryParse(url.AsSpan(), out _currentUrl,
                                      out _urlParseFailure))
            return Fail(ManagedHttpsFailureReason.InvalidRequest);
        return BeginCurrentHop();
    }

    private bool CanBegin()
    {
        if (_state != ManagedHttpsClientState.Idle &&
            _state != ManagedHttpsClientState.Succeeded &&
            _state != ManagedHttpsClientState.Failed &&
            _state != ManagedHttpsClientState.Cancelled)
            return false;
        return true;
    }

    private NetworkOperationResult BeginCurrentHop()
    {
        ClearPerHopBuffers();
        if (!ManagedHttpRequestBuilder.TryBuildGet(_currentUrl.Hostname,
                                                   _currentUrl.Port,
                                                   _currentUrl.RequestTarget,
                                                   _request,
                                                   out _requestLength))
            return Fail(ManagedHttpsFailureReason.InvalidRequest);
        if (_requestLength + ManagedTls12RecordProtection.HeaderSize +
            ManagedTls12RecordProtection.ExplicitNonceSize +
            ManagedAesGcm.TagSize > ManagedNetworkService.MaximumTcpPayloadLength)
            return Fail(ManagedHttpsFailureReason.RequestTooLarge);
        if (_trustedRoot.Length == 0 || !_validationTime.IsValid)
            return Fail(ManagedHttpsFailureReason.TlsAuthenticationFailure);

        if (_random == null)
        {
            if (!ManagedKernelContract.TryEnsureEntropyService() ||
                ManagedKernelContract.SecureRandom == null ||
                !ManagedKernelContract.SecureRandom.IsAvailable)
                return Fail(ManagedHttpsFailureReason.EntropyUnavailable);
            _random = ManagedKernelContract.SecureRandom;
        }
        NetworkOperationResult result = _service.BeginResolveIpv4(
            _currentUrl.Hostname);
        if (result != NetworkOperationResult.Started)
            return Fail(result == NetworkOperationResult.Unavailable
                ? ManagedHttpsFailureReason.TransportFailure
                : ManagedHttpsFailureReason.DnsFailure);
        _state = ManagedHttpsClientState.Resolving;
        return NetworkOperationResult.Started;
    }

    public NetworkOperationResult Poll()
    {
        if (_state == ManagedHttpsClientState.Succeeded)
            return NetworkOperationResult.Success;
        if (_state == ManagedHttpsClientState.Cancelled ||
            _state == ManagedHttpsClientState.Failed)
            return _state == ManagedHttpsClientState.Cancelled
                ? NetworkOperationResult.Success : NetworkOperationResult.Failed;

        // A sink pause is a hard ownership boundary.  Do not advance TLS,
        // plaintext, or TCP state until the consumer acknowledges the segment.
        if (_parser.IsBodyDeliveryPaused ||
            (!_parser.IsBodyComplete && _parser.IsBodyDeliveryWindowFull))
            return NetworkOperationResult.Success;

        // A full HTTP delivery window applies backpressure above TLS.  Drain
        // the authenticated plaintext remainder before polling TCP again.
        if (_pendingPlaintextLength != 0)
        {
            if (!DrainApplicationData())
                return Fail(ManagedHttpsFailureReason.HttpParseFailure);
            if (_pendingPlaintextLength != 0)
                return NetworkOperationResult.Success;
            if (_parser.IsBodyDeliveryPaused ||
                (!_parser.IsBodyComplete && _parser.IsBodyDeliveryWindowFull))
                return NetworkOperationResult.Success;
        }

        NetworkOperationResult poll = _transport.Poll();
        if (poll == NetworkOperationResult.Unavailable ||
            poll == NetworkOperationResult.Failed)
            return Fail(ManagedHttpsFailureReason.TransportFailure);

        if (_state == ManagedHttpsClientState.Resolving)
        {
            if (_service.ResolutionState == NetworkResolutionState.Success &&
                _service.TryGetResolvedIpv4(out Ipv4Address resolved))
            {
                ResolvedAddress = resolved;
                NetworkOperationResult connect = _transport.BeginConnect(
                    resolved, _currentUrl.Port);
                if (connect != NetworkOperationResult.Started)
                    return Fail(ManagedHttpsFailureReason.TcpConnectFailure);
                _state = ManagedHttpsClientState.Connecting;
            }
            else if (_service.ResolutionState == NetworkResolutionState.NxDomain ||
                     _service.ResolutionState == NetworkResolutionState.Failed)
                return Fail(ManagedHttpsFailureReason.DnsFailure);
            return NetworkOperationResult.Success;
        }

        if (_transport.State == NetworkTcpState.Failed)
            return Fail(_state == ManagedHttpsClientState.Connecting
                ? ManagedHttpsFailureReason.TcpConnectFailure
                : ManagedHttpsFailureReason.TcpReset);

        if (_state == ManagedHttpsClientState.Connecting &&
            _transport.State == NetworkTcpState.Established)
        {
            if (_tls == null && !TryCreateTls())
                return Fail(ManagedHttpsFailureReason.TlsAuthenticationFailure);
            if (_tls == null || !_tls.TryStart(_tlsOutput, out _tlsOutputLength))
                return Fail(ManagedHttpsFailureReason.TlsProtocolFailure);
            NetworkOperationResult output = FlushTlsOutput();
            if (output == NetworkOperationResult.Failed)
                return Fail(ManagedHttpsFailureReason.TransportFailure);
            _state = ManagedHttpsClientState.Handshaking;
        }

        if (_state == ManagedHttpsClientState.Handshaking ||
            _state == ManagedHttpsClientState.Established ||
            _state == ManagedHttpsClientState.SendingRequest ||
            _state == ManagedHttpsClientState.ReceivingResponse ||
            _state == ManagedHttpsClientState.Closing)
        {
            if (_tlsOutputLength != 0)
            {
                NetworkOperationResult output = FlushTlsOutput();
                if (output == NetworkOperationResult.Failed)
                    return Fail(ManagedHttpsFailureReason.TransportFailure);
                if (output == NetworkOperationResult.Success &&
                    _requestOutputReady)
                {
                    _requestOutputReady = false;
                    _requestSent = true;
                }
            }

            if (_transport.HasReceived)
            {
                if (!_transport.TryReceive(_receive, out int received) ||
                    received <= 0 || _tls == null)
                {
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PUBLIC_TLS_RECEIVE_FAILED_LENGTH=0x"u8,
                        (ulong)Math.Max(received, 0));
                    return Fail(ManagedHttpsFailureReason.TlsProtocolFailure);
                }
                if (received >= ManagedTls12RecordProtection.HeaderSize)
                {
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PUBLIC_TLS_RECORD_TYPE=0x"u8,
                        _receive[0]);
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PUBLIC_TLS_RECORD_LENGTH=0x"u8,
                        (ulong)((_receive[3] << 8) | _receive[4]));
                    if (_receive[0] == ManagedTls12RecordProtection.Handshake &&
                        received >= ManagedTls12RecordProtection.HeaderSize + 6)
                    {
                        KernelLog.WriteHexLine(
                            "GXOS_NET10:PUBLIC_TLS_HANDSHAKE_TYPE=0x"u8,
                            _receive[5]);
                        KernelLog.WriteHexLine(
                            "GXOS_NET10:PUBLIC_TLS_HANDSHAKE_LENGTH=0x"u8,
                            (ulong)((_receive[6] << 16) |
                                    (_receive[7] << 8) | _receive[8]));
                    }
                }
                if (!_tls.TryConsume(_receive.AsSpan(0, received)))
                {
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PUBLIC_TLS_CONSUME_FAILED_BYTES=0x"u8,
                        (ulong)received);
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PUBLIC_TLS_CONSUME_FAILED_STAGE=0x"u8,
                        (ulong)_tls.LastHandshake);
                    return Fail(_tls.State == ManagedTls12ClientState.Failed &&
                                _tls.FailureKind ==
                                ManagedTls12FailureKind.Authentication
                        ? ManagedHttpsFailureReason.TlsAuthenticationFailure
                        : ManagedHttpsFailureReason.TlsProtocolFailure);
                }

                if (_tls.State == ManagedTls12ClientState.Failed)
                    return Fail(_tls.FailureKind ==
                                ManagedTls12FailureKind.Authentication
                        ? ManagedHttpsFailureReason.TlsAuthenticationFailure
                        : ManagedHttpsFailureReason.TlsProtocolFailure);
                if (_tls.State == ManagedTls12ClientState.OutputReady)
                {
                    if (!_tls.TryTakeOutput(_tlsOutput, out _tlsOutputLength))
                        return Fail(ManagedHttpsFailureReason.TlsProtocolFailure);
                    NetworkOperationResult output = FlushTlsOutput();
                    if (output == NetworkOperationResult.Failed)
                        return Fail(ManagedHttpsFailureReason.TransportFailure);
                }
                if (_tls.State == ManagedTls12ClientState.Established)
                    _tlsAuthenticated = true;
                if (_tls.PendingApplicationDataLength != 0 &&
                    !DrainApplicationData())
                    return Fail(ManagedHttpsFailureReason.HttpParseFailure);
            }

            if (_tls != null && _tls.State == ManagedTls12ClientState.Established)
                _tlsAuthenticated = true;
            if (_tlsAuthenticated && !_requestSent &&
                _state != ManagedHttpsClientState.Closing)
            {
                if (!SendRequest(out NetworkOperationResult sendResult))
                    return sendResult == NetworkOperationResult.Failed
                        ? Fail(ManagedHttpsFailureReason.TransportFailure)
                        : sendResult;
                if (_requestSent)
                    _state = ManagedHttpsClientState.ReceivingResponse;
            }

            if (_transport.State == NetworkTcpState.CloseWait)
            {
                if (_tls == null || !_tls.TryNotifyTransportClosed() ||
                    !_parser.NotifyConnectionClosed())
                    return Fail(_parser.FailureReason ==
                        ManagedHttpParseFailureReason.PrematureConnectionClose
                        ? ManagedHttpsFailureReason.PrematureConnectionClose
                        : ManagedHttpsFailureReason.TlsProtocolFailure);
                if (IsRedirectStatus(_parser.StatusCode) && !_redirectPending &&
                    !TryPrepareRedirect(out ManagedHttpsFailureReason redirectFailure))
                    return Fail(redirectFailure);
                if (!_closingStarted)
                {
                    NetworkOperationResult close = _transport.Close();
                    if (close == NetworkOperationResult.Busy)
                        return NetworkOperationResult.Success;
                    if (close != NetworkOperationResult.Started)
                        return Fail(ManagedHttpsFailureReason.TransportFailure);
                    _closingStarted = true;
                }
                else
                {
                    // A peer FIN can race the local framing-driven close and
                    // leave the TCP adapter in CloseWait.  Complete the
                    // already-started local close so the adapter can advance
                    // through LastAck/TimeWait.
                    NetworkOperationResult close = _transport.Close();
                    if (close == NetworkOperationResult.Busy)
                        return NetworkOperationResult.Success;
                    if (close != NetworkOperationResult.Started)
                        return Fail(ManagedHttpsFailureReason.TransportFailure);
                }
                _state = ManagedHttpsClientState.Closing;
            }

            if (_state == ManagedHttpsClientState.ReceivingResponse &&
                _parser.IsBodyComplete && IsRedirectStatus(_parser.StatusCode))
            {
                if (!_redirectPending &&
                    !TryPrepareRedirect(out ManagedHttpsFailureReason redirectFailure))
                    return Fail(redirectFailure);
                // A server that explicitly asks to close owns the first half
                // of this TCP teardown.  Waiting for its FIN avoids a
                // simultaneous-close race with a pending application ACK;
                // the CloseWait branch above then sends the managed ACK+FIN.
                if (!_parser.ConnectionClose)
                {
                    NetworkOperationResult close = _transport.Close();
                    if (close == NetworkOperationResult.Busy)
                        return NetworkOperationResult.Success;
                    if (close != NetworkOperationResult.Started)
                        return Fail(ManagedHttpsFailureReason.TransportFailure);
                    _closingStarted = true;
                    _state = ManagedHttpsClientState.Closing;
                }
            }

            // Content-Length, chunked, and bodyless responses complete from
            // authenticated HTTP bytes.  Do not wait for peer EOF once the
            // framing boundary is known; close-delimited bodies reach this
            // branch only after the transport reports EOF above.
            if (_closeOnHttpCompletion &&
                _state == ManagedHttpsClientState.ReceivingResponse &&
                _parser.IsBodyComplete &&
                _pendingPlaintextLength == 0 &&
                _transport.State == NetworkTcpState.Established)
            {
                NetworkOperationResult close = _transport.Close();
                if (close == NetworkOperationResult.Busy)
                    return NetworkOperationResult.Success;
                if (close != NetworkOperationResult.Started)
                    return Fail(ManagedHttpsFailureReason.TransportFailure);
                _closingStarted = true;
                _state = ManagedHttpsClientState.Closing;
            }
        }

        if (_state == ManagedHttpsClientState.Closing &&
            (_transport.State == NetworkTcpState.TimeWait ||
             _transport.State == NetworkTcpState.Closed))
        {
            if (_transport.ReleaseForReuse() != NetworkOperationResult.Success)
                return Fail(ManagedHttpsFailureReason.TeardownFailure);
            SnapshotTlsDiagnostics();
            _tls?.Teardown();
            if (_redirectPending)
            {
                if (_tls == null || !_tls.TryReset(_nextUrl.Hostname, _random))
                    return Fail(ManagedHttpsFailureReason.TlsProtocolFailure);
                _redirectCount++;
                _currentUrl.Clear();
                _currentUrl = _nextUrl;
                _nextUrl = default;
                _redirectPending = false;
                if (BeginCurrentHop() == NetworkOperationResult.Failed)
                    return NetworkOperationResult.Failed;
                return NetworkOperationResult.Success;
            }
            _tls = null;
            _state = ManagedHttpsClientState.Succeeded;
            return NetworkOperationResult.Success;
        }
        return _state == ManagedHttpsClientState.Failed
            ? NetworkOperationResult.Failed : NetworkOperationResult.Success;
    }

    public bool TryCopyResponseBody(Span<byte> destination, out int length)
    {
        length = 0;
        if (_state != ManagedHttpsClientState.Succeeded ||
            !_parser.TryCopyBody(_responseBody, out int parserLength) ||
            destination.Length < parserLength)
            return false;
        _responseBody.AsSpan(0, parserLength).CopyTo(destination);
        length = parserLength;
        return true;
    }

    public bool TryReadResponseBodyChunk(Span<byte> destination, out int length)
    {
        if (_state == ManagedHttpsClientState.Cancelled)
        {
            length = 0;
            return false;
        }
        return _parser.TryReadBodyChunk(destination, out length);
    }

    public bool TryCopyResponseContentType(Span<byte> destination, out int length)
    {
        return _parser.TryCopyContentType(destination, out length);
    }

    public ManagedHttpBodyDeliveryResult ConsumeResponseBody(
        IManagedHttpBodySink sink)
    {
        if (_state == ManagedHttpsClientState.Cancelled)
            return ManagedHttpBodyDeliveryResult.Cancelled;
        if (_state == ManagedHttpsClientState.Failed)
            return ManagedHttpBodyDeliveryResult.Failed;
        ManagedHttpBodyDeliveryResult result = _parser.ConsumeBody(sink);
        if (result == ManagedHttpBodyDeliveryResult.Failed)
            Fail(ManagedHttpsFailureReason.SinkFailure);
        return result;
    }

    /* Compatibility wrapper for callers that only need a boolean result. */
    public bool TryConsumeResponseBody(IManagedHttpBodySink sink)
    {
        ManagedHttpBodyDeliveryResult result = ConsumeResponseBody(sink);
        return result != ManagedHttpBodyDeliveryResult.Failed &&
               result != ManagedHttpBodyDeliveryResult.Cancelled;
    }

    public NetworkOperationResult Cancel()
    {
        if (_state == ManagedHttpsClientState.Succeeded ||
            _state == ManagedHttpsClientState.Cancelled)
            return NetworkOperationResult.Success;
        if (_state == ManagedHttpsClientState.Failed)
            return NetworkOperationResult.Success;
        NetworkOperationResult result = _transport.ReleaseForReuse();
        SnapshotTlsDiagnostics();
        _tls?.Teardown();
        _tls = null;
        ClearCancelledBuffers();
        _state = ManagedHttpsClientState.Cancelled;
        _failureReason = ManagedHttpsFailureReason.Cancelled;
        return result == NetworkOperationResult.Success
            ? NetworkOperationResult.Success : NetworkOperationResult.Failed;
    }

    public NetworkOperationResult Reset()
    {
        if (_state != ManagedHttpsClientState.Idle &&
            _state != ManagedHttpsClientState.Succeeded &&
            _state != ManagedHttpsClientState.Failed &&
            _state != ManagedHttpsClientState.Cancelled)
            return NetworkOperationResult.Busy;
        _tls?.Teardown();
        _tls = null;
        ClearOperationBuffers();
        _state = ManagedHttpsClientState.Idle;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult FlushTlsOutput()
    {
        if (_tlsOutputLength == 0) return NetworkOperationResult.Success;
        NetworkOperationResult send = _transport.Send(
            _tlsOutput.AsSpan(0, _tlsOutputLength));
        if (send == NetworkOperationResult.Success)
        {
            _tlsOutput.AsSpan(0, _tlsOutputLength).Clear();
            _tlsOutputLength = 0;
            return NetworkOperationResult.Success;
        }
        if (send == NetworkOperationResult.Busy)
            return NetworkOperationResult.Busy;
        return NetworkOperationResult.Failed;
    }

    private bool SendRequest(out NetworkOperationResult result)
    {
        result = NetworkOperationResult.Success;
        if (_tls == null ||
            !_tls.TryEncryptApplicationData(_request.AsSpan(0, _requestLength),
                                            _tlsOutput, out _tlsOutputLength))
        {
            result = NetworkOperationResult.Failed;
            return false;
        }
        result = FlushTlsOutput();
        if (result == NetworkOperationResult.Success)
        {
            _requestOutputReady = false;
            _requestSent = true;
            return true;
        }
        if (result == NetworkOperationResult.Busy)
        {
            _requestOutputReady = true;
            return true;
        }
        return false;
    }

    private bool DrainApplicationData()
    {
        if (_pendingPlaintextLength == 0)
        {
            if (_tls == null ||
                !_tls.TryTakeApplicationData(_plaintext, out int length))
                return false;
            if (length == 0) return true;
            _plaintext.AsSpan(0, length).CopyTo(_pendingPlaintext);
            _plaintext.AsSpan(0, length).Clear();
            _pendingPlaintextLength = length;
        }

        if (!_parser.TryFeed(_pendingPlaintext.AsSpan(0, _pendingPlaintextLength),
                             out int consumed))
            return false;
        _applicationDataReceived = _applicationDataReceived || consumed != 0;
        if (consumed == _pendingPlaintextLength)
        {
            _pendingPlaintext.AsSpan().Clear();
            _pendingPlaintextLength = 0;
            return true;
        }
        int remaining = _pendingPlaintextLength - consumed;
        _pendingPlaintext.AsSpan(consumed, remaining).CopyTo(_pendingPlaintext);
        _pendingPlaintext.AsSpan(remaining, consumed).Clear();
        _pendingPlaintextLength = remaining;
        return true;
    }

    private bool TryCreateTls()
    {
        if (_random == null || !_currentUrl.IsValid) return false;
        int workingStorageBytes = _compactTlsProfile
            ? ManagedTls12Client.CompactWorkingStorageBytes
            : ManagedTls12Client.CertificateStorageBytes;
        return ManagedTls12Client.TryCreate(_currentUrl.Hostname, _trustedRoot,
                in _validationTime, _random, new byte[workingStorageBytes],
                out _tls,
                _compactTlsProfile
                    ? ManagedTls12Client.CompactRecordBytes
                    : ManagedTls12RecordProtection.MaximumRecordSize) &&
               _tls != null;
    }

    private NetworkOperationResult Fail(ManagedHttpsFailureReason reason)
    {
        SnapshotTlsDiagnostics();
        if (_state != ManagedHttpsClientState.Failed &&
            _state != ManagedHttpsClientState.Succeeded)
        {
            _tls?.Teardown();
            _transport.ReleaseForReuse();
        }
        _tls = null;
        _tlsOutput.AsSpan().Clear();
        _tlsOutputLength = 0;
        _failureReason = reason;
        _state = ManagedHttpsClientState.Failed;
        return NetworkOperationResult.Failed;
    }

    private ManagedHttpTransferState GetTransferState()
    {
        if (_state == ManagedHttpsClientState.Cancelled)
            return ManagedHttpTransferState.Cancelled;
        if (_state == ManagedHttpsClientState.Failed)
            return ManagedHttpTransferState.Failed;
        if (_parser.IsBodyDeliveryPaused)
            return ManagedHttpTransferState.Paused;
        if (_state == ManagedHttpsClientState.Succeeded)
            return ManagedHttpTransferState.Completed;
        if (_state == ManagedHttpsClientState.Idle)
            return ManagedHttpTransferState.Idle;
        return ManagedHttpTransferState.Receiving;
    }

    private ManagedHttpTerminalFailureReason GetTerminalFailure()
    {
        if (_state == ManagedHttpsClientState.Cancelled ||
            _failureReason == ManagedHttpsFailureReason.Cancelled)
            return ManagedHttpTerminalFailureReason.Cancelled;
        if (_failureReason == ManagedHttpsFailureReason.SinkFailure)
            return ManagedHttpTerminalFailureReason.SinkFailure;
        if (_parser.FailureReason == ManagedHttpParseFailureReason.BodyTooLarge)
            return ManagedHttpTerminalFailureReason.BodyTooLarge;
        if (_failureReason == ManagedHttpsFailureReason.TlsAuthenticationFailure ||
            _failureReason == ManagedHttpsFailureReason.TlsProtocolFailure)
            return ManagedHttpTerminalFailureReason.TlsFailure;
        if (_failureReason == ManagedHttpsFailureReason.HttpParseFailure)
            return ManagedHttpTerminalFailureReason.MalformedHttp;
        if (_failureReason == ManagedHttpsFailureReason.PrematureConnectionClose)
            return ManagedHttpTerminalFailureReason.PrematureConnectionClose;
        if (_failureReason == ManagedHttpsFailureReason.TransportFailure ||
            _failureReason == ManagedHttpsFailureReason.TcpReset ||
            _failureReason == ManagedHttpsFailureReason.TcpConnectFailure ||
            _failureReason == ManagedHttpsFailureReason.DnsFailure)
            return ManagedHttpTerminalFailureReason.TransportFailure;
        if (_failureReason == ManagedHttpsFailureReason.TeardownFailure)
            return ManagedHttpTerminalFailureReason.TeardownFailure;
        return _state == ManagedHttpsClientState.Failed
            ? ManagedHttpTerminalFailureReason.RequestFailure
            : ManagedHttpTerminalFailureReason.None;
    }

    private void ClearOperationBuffers()
    {
        _currentUrl.Clear();
        _nextUrl.Clear();
        _currentUrl = default;
        _nextUrl = default;
        _redirectPending = false;
        _redirectCount = 0;
        _urlParseFailure = ManagedHttpsUrlParseFailureReason.None;
        _failureReason = ManagedHttpsFailureReason.None;
        _lastTlsHandshake = ManagedTls12HandshakeStage.ClientHello;
        _lastTlsFailureKind = ManagedTls12FailureKind.Protocol;
        _lastTlsEmsNegotiated = false;
        _lastTlsPeerCertificateCount = 0;
        _lastTlsPeerCertificateAlgorithmMask = 0;
        _lastTlsTrustAnchorMatched = false;
        ClearPerHopBuffers();
    }

    private void ClearCancelledBuffers()
    {
        _currentUrl.Clear();
        _nextUrl.Clear();
        _currentUrl = default;
        _nextUrl = default;
        _redirectPending = false;
        _redirectCount = 0;
        _urlParseFailure = ManagedHttpsUrlParseFailureReason.None;
        _request.AsSpan().Clear();
        _receive.AsSpan().Clear();
        _tlsOutput.AsSpan().Clear();
        _plaintext.AsSpan().Clear();
        _pendingPlaintext.AsSpan().Clear();
        _responseBody.AsSpan().Clear();
        _requestLength = 0;
        _tlsOutputLength = 0;
        _pendingPlaintextLength = 0;
        _requestSent = false;
        _requestOutputReady = false;
        _tlsAuthenticated = false;
        _applicationDataReceived = false;
        _closingStarted = false;
        ResolvedAddress = default;
    }

    private void SnapshotTlsDiagnostics()
    {
        if (_tls == null) return;
        _lastTlsHandshake = _tls.LastHandshake;
        _lastTlsFailureKind = _tls.FailureKind;
        _lastTlsEmsNegotiated = _tls.EmsNegotiated;
        _lastTlsPeerCertificateCount = _tls.PeerCertificateCount;
        _lastTlsPeerCertificateAlgorithmMask =
            _tls.PeerCertificateAlgorithmMask;
        _lastTlsTrustAnchorMatched = _tls.TrustAnchorMatched;
    }

    private void ClearPerHopBuffers()
    {
        _parser.Reset();
        _request.AsSpan().Clear();
        _receive.AsSpan().Clear();
        _tlsOutput.AsSpan().Clear();
        _plaintext.AsSpan().Clear();
        _pendingPlaintext.AsSpan().Clear();
        _responseBody.AsSpan().Clear();
        _requestLength = 0;
        _tlsOutputLength = 0;
        _pendingPlaintextLength = 0;
        _requestSent = false;
        _requestOutputReady = false;
        _tlsAuthenticated = false;
        _applicationDataReceived = false;
        _closingStarted = false;
        ResolvedAddress = default;
    }

    private bool TryPrepareRedirect(out ManagedHttpsFailureReason failure)
    {
        failure = ManagedHttpsFailureReason.None;
        if (_redirectCount >= MaximumRedirects)
        {
            failure = ManagedHttpsFailureReason.RedirectLimitExceeded;
            return false;
        }
        if (!_parser.HasLocation)
        {
            failure = ManagedHttpsFailureReason.RedirectMissingLocation;
            return false;
        }
        Span<byte> location = stackalloc byte[ManagedHttpLimits.MaximumLocationLength];
        if (!_parser.TryCopyLocation(location, out int length) || length == 0)
        {
            failure = ManagedHttpsFailureReason.RedirectInvalidLocation;
            return false;
        }
        if (!ManagedHttpsUrl.TryResolve(_currentUrl, location[..length],
                                        out ManagedHttpsUrl next,
                                        out ManagedHttpsUrlParseFailureReason reason))
        {
            failure = reason == ManagedHttpsUrlParseFailureReason.HttpsDowngrade
                ? ManagedHttpsFailureReason.RedirectDowngradeRejected
                : reason == ManagedHttpsUrlParseFailureReason.UnsupportedScheme
                    ? ManagedHttpsFailureReason.RedirectUnsupportedScheme
                    : ManagedHttpsFailureReason.RedirectInvalidLocation;
            return false;
        }
        _nextUrl = next;
        _redirectPending = true;
        return true;
    }

    private static bool IsRedirectStatus(int statusCode)
    {
        return statusCode == 301 || statusCode == 302 || statusCode == 303 ||
               statusCode == 307 || statusCode == 308;
    }
}

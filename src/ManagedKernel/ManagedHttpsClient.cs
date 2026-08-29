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
    Cancelled = 13
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
    public const int MaximumTlsTransportPayload =
        ManagedTls12Client.MaximumPendingApplicationBytes;

    private readonly ManagedNetworkService _service;
    private readonly IManagedTlsTransport _transport;
    private readonly byte[] _trustedRoot;
    private readonly ManagedX509UtcTime _validationTime;
    private ManagedSecureRandom? _random;
    private ManagedTls12Client? _tls;
    private readonly byte[] _request =
        new byte[ManagedHttpLimits.MaximumSerializedRequestSize];
    private readonly byte[] _receive =
        new byte[ManagedNetworkService.MaximumTcpPayloadLength];
    private readonly byte[] _tlsOutput =
        new byte[ManagedTls12RecordProtection.MaximumRecordSize];
    private readonly byte[] _plaintext =
        new byte[ManagedTls12Client.MaximumPendingApplicationBytes];
    private readonly byte[] _responseBody =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];
    private readonly ManagedHttpResponseParser _parser = new();
    private ManagedHttpsClientState _state;
    private ManagedHttpsFailureReason _failureReason;
    private int _requestLength;
    private int _tlsOutputLength;
    private bool _requestSent;
    private bool _requestOutputReady;
    private bool _tlsAuthenticated;
    private bool _applicationDataReceived;
    private bool _closingStarted;

    public ManagedHttpsClient(ManagedNetworkService service,
                              ReadOnlySpan<byte> trustedRoot,
                              ManagedHttpsValidationTime validationTime)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _transport = new ManagedNetworkServiceTlsTransport(service);
        _trustedRoot = trustedRoot.ToArray();
        _validationTime = validationTime.ToX509Time();
        _state = ManagedHttpsClientState.Idle;
    }

    /* The injected constructor is internal on purpose: deterministic fixture
       providers are available to host/QEMU proof code, never to the public
       production API. */
    internal ManagedHttpsClient(ManagedNetworkService service,
                                ReadOnlySpan<byte> trustedRoot,
                                in ManagedX509UtcTime validationTime,
                                ManagedSecureRandom random)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _transport = new ManagedNetworkServiceTlsTransport(service);
        _trustedRoot = trustedRoot.ToArray();
        _validationTime = validationTime;
        _random = random;
        _state = ManagedHttpsClientState.Idle;
    }

    public ManagedHttpsClientState State => _state;
    public ManagedHttpsFailureReason FailureReason => _failureReason;
    public Ipv4Address ResolvedAddress { get; private set; }
    public bool RequestSent => _requestSent;
    public bool TlsAuthenticated => _tlsAuthenticated;
    public bool ApplicationDataReceived => _applicationDataReceived;
    public bool StatusParsed => _parser.IsStatusParsed;
    public int StatusCode => _parser.StatusCode;
    public int ResponseBodyLength => _parser.BodyLength;
    public int ContentLength => _parser.ContentLength;
    public bool ResponseBodyComplete => _parser.IsBodyComplete;
    public ManagedHttpParseFailureReason ParseFailureReason => _parser.FailureReason;

    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ReadOnlySpan<byte> path)
    {
        if (_state != ManagedHttpsClientState.Idle &&
            _state != ManagedHttpsClientState.Succeeded &&
            _state != ManagedHttpsClientState.Failed &&
            _state != ManagedHttpsClientState.Cancelled)
            return NetworkOperationResult.Busy;

        ClearOperationBuffers();
        if (!ManagedHttpRequestBuilder.TryBuildGet(hostname, path, _request,
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
        if (!ManagedTls12Client.TryCreate(hostname, _trustedRoot,
                in _validationTime, _random, new byte[
                    ManagedTls12Client.CertificateStorageBytes],
                out _tls) || _tls == null)
            return Fail(ManagedHttpsFailureReason.TlsAuthenticationFailure);

        NetworkOperationResult result = _service.BeginResolveIpv4(hostname);
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
                    resolved, HttpsPort);
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
                    return Fail(ManagedHttpsFailureReason.TlsProtocolFailure);
                if (!_tls.TryConsume(_receive.AsSpan(0, received)))
                    return Fail(_tls.State == ManagedTls12ClientState.Failed &&
                                _tls.FailureKind ==
                                ManagedTls12FailureKind.Authentication
                        ? ManagedHttpsFailureReason.TlsAuthenticationFailure
                        : ManagedHttpsFailureReason.TlsProtocolFailure);

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
                if (!_closingStarted)
                {
                    NetworkOperationResult close = _transport.Close();
                    if (close == NetworkOperationResult.Busy)
                        return NetworkOperationResult.Success;
                    if (close != NetworkOperationResult.Started)
                        return Fail(ManagedHttpsFailureReason.TransportFailure);
                    _closingStarted = true;
                }
                _state = ManagedHttpsClientState.Closing;
            }
        }

        if (_state == ManagedHttpsClientState.Closing &&
            (_transport.State == NetworkTcpState.TimeWait ||
             _transport.State == NetworkTcpState.Closed))
        {
            if (_transport.ReleaseForReuse() != NetworkOperationResult.Success)
                return Fail(ManagedHttpsFailureReason.TeardownFailure);
            _tls?.Teardown();
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

    public NetworkOperationResult Cancel()
    {
        if (_state == ManagedHttpsClientState.Succeeded ||
            _state == ManagedHttpsClientState.Cancelled)
            return NetworkOperationResult.Success;
        if (_state == ManagedHttpsClientState.Failed)
            return NetworkOperationResult.Success;
        NetworkOperationResult result = _transport.ReleaseForReuse();
        _tls?.Teardown();
        _tls = null;
        ClearOperationBuffers();
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
        if (_tls == null ||
            !_tls.TryTakeApplicationData(_plaintext, out int length))
            return false;
        if (length == 0) return true;
        if (!_parser.Feed(_plaintext.AsSpan(0, length))) return false;
        _applicationDataReceived = true;
        return true;
    }

    private NetworkOperationResult Fail(ManagedHttpsFailureReason reason)
    {
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

    private void ClearOperationBuffers()
    {
        _parser.Reset();
        _request.AsSpan().Clear();
        _receive.AsSpan().Clear();
        _tlsOutput.AsSpan().Clear();
        _plaintext.AsSpan().Clear();
        _responseBody.AsSpan().Clear();
        _requestLength = 0;
        _tlsOutputLength = 0;
        _requestSent = false;
        _requestOutputReady = false;
        _tlsAuthenticated = false;
        _applicationDataReceived = false;
        _closingStarted = false;
        _failureReason = ManagedHttpsFailureReason.None;
        ResolvedAddress = default;
    }
}

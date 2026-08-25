using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedHttpClientState : byte
{
    Idle = 0,
    Resolving = 1,
    Connecting = 2,
    Receiving = 3,
    Closing = 4,
    Succeeded = 5,
    Failed = 6,
    Cancelled = 7
}

public enum ManagedHttpFailureReason : byte
{
    None = 0,
    InvalidRequest = 1,
    DnsFailure = 2,
    TcpConnectFailure = 3,
    TcpReset = 4,
    TransportFailure = 5,
    HttpParseFailure = 6,
    PrematureConnectionClose = 7,
    TeardownFailure = 8,
    Cancelled = 9
}

public enum ManagedHttpParseState : byte
{
    Idle = 0,
    StatusLine = 1,
    Headers = 2,
    Body = 3,
    BodyComplete = 4,
    Closed = 5,
    Failed = 6
}

public enum ManagedHttpParseFailureReason : byte
{
    None = 0,
    StatusLine = 1,
    StatusCode = 2,
    LineFraming = 3,
    StatusLineOverflow = 4,
    HeaderLineOverflow = 5,
    HeaderCount = 6,
    HeaderBytes = 7,
    HeaderSyntax = 8,
    ContentLength = 9,
    ConflictingContentLength = 10,
    UnsupportedTransferEncoding = 11,
    MissingContentLength = 12,
    MissingConnectionClose = 13,
    BodyTooLarge = 14,
    BodyExceedsContentLength = 15,
    PrematureConnectionClose = 16
}

public static class ManagedHttpLimits
{
    public const int MaximumHostnameLength = 253;
    public const int MaximumPathLength = 128;
    public const int MaximumSerializedRequestSize = ManagedNetworkService.MaximumTcpPayloadLength;
    public const int MaximumStatusLineLength = 64;
    public const int MaximumHeaderLineLength = 96;
    public const int MaximumHeaderCount = 16;
    public const int MaximumResponseHeaderBytes = 512;
    public const int MaximumHeaderNameLength = 32;
    public const int MaximumBodyCapacity = 256;
    public const int MaximumReceiveStagingBuffer = ManagedNetworkService.MaximumTcpPayloadLength;
    public const int MaximumContentTypeLength = 64;
}

public static class ManagedHttpRequestBuilder
{
    public static bool TryBuildGet(ReadOnlySpan<byte> hostname,
                                   ReadOnlySpan<byte> path,
                                   Span<byte> destination,
                                   out int length)
    {
        length = 0;
        if (!TryValidateHostname(hostname) || !TryValidatePath(path) ||
            destination.Length < ManagedHttpLimits.MaximumSerializedRequestSize)
            return false;

        int offset = 0;
        if (!Append(destination, ref offset, "GET "u8) ||
            !Append(destination, ref offset, path) ||
            !Append(destination, ref offset, " HTTP/1.1\r\nHost: "u8) ||
            !Append(destination, ref offset, hostname) ||
            !Append(destination, ref offset, "\r\nConnection: close\r\n\r\n"u8))
            return false;
        length = offset;
        return length <= ManagedHttpLimits.MaximumSerializedRequestSize;
    }

    private static bool Append(Span<byte> destination, ref int offset,
                               ReadOnlySpan<byte> value)
    {
        if (value.Length > destination.Length - offset) return false;
        value.CopyTo(destination.Slice(offset));
        offset += value.Length;
        return true;
    }

    private static bool TryValidateHostname(ReadOnlySpan<byte> hostname)
    {
        if (hostname.Length == 0 || hostname.Length > ManagedHttpLimits.MaximumHostnameLength)
            return false;
        int labelLength = 0;
        for (int index = 0; index <= hostname.Length; ++index)
        {
            if (index != hostname.Length && hostname[index] != (byte)'.')
            {
                byte value = hostname[index];
                bool letter = value >= (byte)'A' && value <= (byte)'Z';
                bool lower = value >= (byte)'a' && value <= (byte)'z';
                bool digit = value >= (byte)'0' && value <= (byte)'9';
                if ((!letter && !lower && !digit && value != (byte)'-') ||
                    (labelLength == 0 && value == (byte)'-'))
                    return false;
                if (++labelLength > 63) return false;
                continue;
            }
            if (labelLength == 0 || hostname[index - 1] == (byte)'-') return false;
            labelLength = 0;
        }
        return true;
    }

    private static bool TryValidatePath(ReadOnlySpan<byte> path)
    {
        if (path.Length == 0 || path.Length > ManagedHttpLimits.MaximumPathLength ||
            path[0] != (byte)'/') return false;
        for (int index = 0; index != path.Length; ++index)
        {
            byte value = path[index];
            if (value < 0x21 || value > 0x7E || value == (byte)'\r' ||
                value == (byte)'\n' || value == (byte)' ') return false;
        }
        return true;
    }
}

public sealed class ManagedHttpResponseParser
{
    private readonly byte[] _line = new byte[ManagedHttpLimits.MaximumHeaderLineLength];
    private readonly byte[] _body = new byte[ManagedHttpLimits.MaximumBodyCapacity];
    private readonly byte[] _contentType = new byte[ManagedHttpLimits.MaximumContentTypeLength];
    private ManagedHttpParseState _state;
    private ManagedHttpParseFailureReason _failureReason;
    private int _lineLength;
    private int _headerBytes;
    private int _headerCount;
    private int _statusCode;
    private int _contentLength;
    private int _bodyLength;
    private int _contentTypeLength;
    private bool _sawCarriageReturn;
    private bool _hasContentLength;
    private bool _connectionClose;

    public ManagedHttpResponseParser() => Reset();

    public ManagedHttpParseState State => _state;
    public ManagedHttpParseFailureReason FailureReason => _failureReason;
    public int StatusCode => _statusCode;
    public int HeaderCount => _headerCount;
    public int HeaderBytes => _headerBytes;
    public bool HasContentLength => _hasContentLength;
    public int ContentLength => _contentLength;
    public int BodyLength => _bodyLength;
    public int ContentTypeLength => _contentTypeLength;
    public bool IsStatusParsed => _state >= ManagedHttpParseState.Headers &&
                                  _failureReason == ManagedHttpParseFailureReason.None;
    public bool IsBodyComplete => _state == ManagedHttpParseState.BodyComplete ||
                                  _state == ManagedHttpParseState.Closed;

    public void Reset()
    {
        _state = ManagedHttpParseState.StatusLine;
        _failureReason = ManagedHttpParseFailureReason.None;
        _lineLength = 0;
        _headerBytes = 0;
        _headerCount = 0;
        _statusCode = 0;
        _contentLength = 0;
        _bodyLength = 0;
        _contentTypeLength = 0;
        _sawCarriageReturn = false;
        _hasContentLength = false;
        _connectionClose = false;
        _line.AsSpan().Clear();
        _body.AsSpan().Clear();
        _contentType.AsSpan().Clear();
    }

    public bool Feed(ReadOnlySpan<byte> bytes)
    {
        if (_state == ManagedHttpParseState.Failed ||
            _state == ManagedHttpParseState.Closed)
            return bytes.Length == 0;
        for (int index = 0; index != bytes.Length; ++index)
        {
            byte value = bytes[index];
            if (_state == ManagedHttpParseState.Body ||
                _state == ManagedHttpParseState.BodyComplete)
            {
                if (_state == ManagedHttpParseState.BodyComplete)
                    return Fail(ManagedHttpParseFailureReason.BodyExceedsContentLength);
                if (_bodyLength >= _contentLength ||
                    _bodyLength >= ManagedHttpLimits.MaximumBodyCapacity)
                    return Fail(ManagedHttpParseFailureReason.BodyTooLarge);
                _body[_bodyLength++] = value;
                if (_bodyLength == _contentLength)
                    _state = ManagedHttpParseState.BodyComplete;
                continue;
            }

            if (_headerBytes == ManagedHttpLimits.MaximumResponseHeaderBytes)
                return Fail(ManagedHttpParseFailureReason.HeaderBytes);
            _headerBytes++;
            if (_sawCarriageReturn)
            {
                if (value != (byte)'\n')
                    return Fail(ManagedHttpParseFailureReason.LineFraming);
                _sawCarriageReturn = false;
                if (!FinishLine()) return false;
                continue;
            }
            if (value == (byte)'\r')
            {
                _sawCarriageReturn = true;
                continue;
            }
            if (value == (byte)'\n' || value == 0x7F || value < 0x20 && value != 0x09 ||
                value > 0x7E)
                return Fail(ManagedHttpParseFailureReason.LineFraming);
            int lineLimit = _state == ManagedHttpParseState.StatusLine
                ? ManagedHttpLimits.MaximumStatusLineLength
                : ManagedHttpLimits.MaximumHeaderLineLength;
            if (_lineLength == lineLimit)
                return Fail(_state == ManagedHttpParseState.StatusLine
                    ? ManagedHttpParseFailureReason.StatusLineOverflow
                    : ManagedHttpParseFailureReason.HeaderLineOverflow);
            _line[_lineLength++] = value;
        }
        return true;
    }

    public bool NotifyConnectionClosed()
    {
        if (_state != ManagedHttpParseState.BodyComplete)
            return Fail(ManagedHttpParseFailureReason.PrematureConnectionClose);
        _state = ManagedHttpParseState.Closed;
        return true;
    }

    public bool TryCopyBody(Span<byte> destination, out int length)
    {
        length = _bodyLength;
        if (!IsBodyComplete || destination.Length < _bodyLength) return false;
        _body.AsSpan(0, _bodyLength).CopyTo(destination);
        return true;
    }

    private bool FinishLine()
    {
        if (_state == ManagedHttpParseState.StatusLine)
        {
            if (!ParseStatusLine()) return false;
            _state = ManagedHttpParseState.Headers;
            _lineLength = 0;
            return true;
        }
        if (_lineLength == 0)
        {
            if (!_hasContentLength)
                return Fail(ManagedHttpParseFailureReason.MissingContentLength);
            if (!_connectionClose)
                return Fail(ManagedHttpParseFailureReason.MissingConnectionClose);
            _state = _contentLength == 0
                ? ManagedHttpParseState.BodyComplete : ManagedHttpParseState.Body;
            _lineLength = 0;
            return true;
        }
        if (++_headerCount > ManagedHttpLimits.MaximumHeaderCount)
            return Fail(ManagedHttpParseFailureReason.HeaderCount);
        if (!ParseHeader()) return false;
        _lineLength = 0;
        return true;
    }

    private bool ParseStatusLine()
    {
        if (_lineLength < 13 || !EqualsAscii(0, "HTTP/1.1"u8) ||
            _line[8] != (byte)' ' || _line[12] != (byte)' ')
            return Fail(ManagedHttpParseFailureReason.StatusLine);
        byte one = _line[9], two = _line[10], three = _line[11];
        if (one < (byte)'0' || one > (byte)'9' ||
            two < (byte)'0' || two > (byte)'9' ||
            three < (byte)'0' || three > (byte)'9')
            return Fail(ManagedHttpParseFailureReason.StatusCode);
        _statusCode = (one - (byte)'0') * 100 +
                      (two - (byte)'0') * 10 + three - (byte)'0';
        if (_statusCode < 100 || _statusCode > 599)
            return Fail(ManagedHttpParseFailureReason.StatusCode);
        return true;
    }

    private bool ParseHeader()
    {
        int colon = -1;
        for (int index = 0; index != _lineLength; ++index)
        {
            if (_line[index] == (byte)':') { colon = index; break; }
        }
        if (colon <= 0 || colon > ManagedHttpLimits.MaximumHeaderNameLength)
            return Fail(ManagedHttpParseFailureReason.HeaderSyntax);
        for (int index = 0; index != colon; ++index)
            if (!IsToken(_line[index]))
                return Fail(ManagedHttpParseFailureReason.HeaderSyntax);

        int valueStart = colon + 1;
        while (valueStart < _lineLength && IsWhitespace(_line[valueStart]))
            valueStart++;
        int valueEnd = _lineLength;
        while (valueEnd > valueStart && IsWhitespace(_line[valueEnd - 1]))
            valueEnd--;
        ReadOnlySpan<byte> name = _line.AsSpan(0, colon);
        ReadOnlySpan<byte> value = _line.AsSpan(valueStart, valueEnd - valueStart);
        if (EqualsAsciiIgnoreCase(name, "Content-Length"u8))
            return ParseContentLength(value);
        if (EqualsAsciiIgnoreCase(name, "Connection"u8))
        {
            if (!EqualsAsciiIgnoreCase(value, "close"u8))
                return Fail(ManagedHttpParseFailureReason.MissingConnectionClose);
            _connectionClose = true;
            return true;
        }
        if (EqualsAsciiIgnoreCase(name, "Transfer-Encoding"u8))
            return Fail(ManagedHttpParseFailureReason.UnsupportedTransferEncoding);
        if (EqualsAsciiIgnoreCase(name, "Content-Type"u8))
        {
            if (value.Length > ManagedHttpLimits.MaximumContentTypeLength)
                return Fail(ManagedHttpParseFailureReason.HeaderLineOverflow);
            value.CopyTo(_contentType);
            _contentTypeLength = value.Length;
        }
        return true;
    }

    private bool ParseContentLength(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0) return Fail(ManagedHttpParseFailureReason.ContentLength);
        int parsed = 0;
        for (int index = 0; index != value.Length; ++index)
        {
            byte digit = value[index];
            if (digit < (byte)'0' || digit > (byte)'9')
                return Fail(ManagedHttpParseFailureReason.ContentLength);
            int next = parsed * 10 + digit - (byte)'0';
            if (next < parsed || next > ManagedHttpLimits.MaximumBodyCapacity)
                return Fail(ManagedHttpParseFailureReason.BodyTooLarge);
            parsed = next;
        }
        if (_hasContentLength && _contentLength != parsed)
            return Fail(ManagedHttpParseFailureReason.ConflictingContentLength);
        _hasContentLength = true;
        _contentLength = parsed;
        return true;
    }

    private bool Fail(ManagedHttpParseFailureReason reason)
    {
        _failureReason = reason;
        _state = ManagedHttpParseState.Failed;
        return false;
    }

    private bool EqualsAscii(int offset, ReadOnlySpan<byte> value)
    {
        return _lineLength >= offset + value.Length &&
               _line.AsSpan(offset, value.Length).SequenceEqual(value);
    }

    private static bool EqualsAsciiIgnoreCase(ReadOnlySpan<byte> left,
                                              ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length) return false;
        for (int index = 0; index != left.Length; ++index)
        {
            byte a = left[index], b = right[index];
            if (a >= (byte)'A' && a <= (byte)'Z') a = (byte)(a + 32);
            if (b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
            if (a != b) return false;
        }
        return true;
    }

    private static bool IsWhitespace(byte value) => value == (byte)' ' || value == 9;

    private static bool IsToken(byte value)
    {
        return (value >= (byte)'A' && value <= (byte)'Z') ||
               (value >= (byte)'a' && value <= (byte)'z') ||
               (value >= (byte)'0' && value <= (byte)'9') || value == (byte)'-';
    }
}

public sealed class ManagedHttpClient
{
    private readonly ManagedNetworkService _service;
    private readonly byte[] _request =
        new byte[ManagedHttpLimits.MaximumSerializedRequestSize];
    private readonly byte[] _receive =
        new byte[ManagedHttpLimits.MaximumReceiveStagingBuffer];
    private readonly byte[] _responseBody =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];
    private readonly ManagedHttpResponseParser _parser = new();
    private ManagedHttpClientState _state;
    private ManagedHttpFailureReason _failureReason;
    private int _requestLength;
    private bool _requestSent;

    public ManagedHttpClient(ManagedNetworkService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _state = ManagedHttpClientState.Idle;
    }

    public ManagedHttpClientState State => _state;
    public ManagedHttpFailureReason FailureReason => _failureReason;
    public Ipv4Address ResolvedAddress { get; private set; }
    public int RequestLength => _requestLength;
    public bool RequestSent => _requestSent;
    public bool StatusParsed => _parser.IsStatusParsed;
    public int StatusCode => _parser.StatusCode;
    public int ResponseBodyLength => _parser.BodyLength;
    public int ContentLength => _parser.ContentLength;
    public bool ResponseBodyComplete => _parser.IsBodyComplete;
    public ManagedHttpParseFailureReason ParseFailureReason => _parser.FailureReason;

    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ReadOnlySpan<byte> path)
    {
        if (_state != ManagedHttpClientState.Idle &&
            _state != ManagedHttpClientState.Succeeded &&
            _state != ManagedHttpClientState.Failed &&
            _state != ManagedHttpClientState.Cancelled)
            return NetworkOperationResult.Busy;
        _parser.Reset();
        _request.AsSpan().Clear();
        _responseBody.AsSpan().Clear();
        _requestSent = false;
        _failureReason = ManagedHttpFailureReason.None;
        if (!ManagedHttpRequestBuilder.TryBuildGet(hostname, path, _request,
                                                   out _requestLength))
            return Fail(ManagedHttpFailureReason.InvalidRequest);
        NetworkOperationResult result = _service.BeginResolveIpv4(hostname);
        if (result != NetworkOperationResult.Started)
            return Fail(MapFailure(result, ManagedHttpFailureReason.DnsFailure));
        _state = ManagedHttpClientState.Resolving;
        return NetworkOperationResult.Started;
    }

    public NetworkOperationResult Poll()
    {
        if (_state == ManagedHttpClientState.Succeeded ||
            _state == ManagedHttpClientState.Cancelled)
            return NetworkOperationResult.Success;
        if (_state == ManagedHttpClientState.Failed)
            return NetworkOperationResult.Failed;
        NetworkOperationResult poll = _service.Poll();
        if (poll == NetworkOperationResult.Unavailable)
            return Fail(ManagedHttpFailureReason.TransportFailure);
        if (poll == NetworkOperationResult.Failed)
            return Fail(ManagedHttpFailureReason.TransportFailure);

        if (_state == ManagedHttpClientState.Resolving)
        {
            if (_service.ResolutionState == NetworkResolutionState.Success &&
                _service.TryGetResolvedIpv4(out Ipv4Address address))
            {
                ResolvedAddress = address;
                NetworkOperationResult connect = _service.BeginTcpConnect(
                    address, ManagedTcpConnection.ServerPort);
                if (connect != NetworkOperationResult.Started)
                    return Fail(ManagedHttpFailureReason.TcpConnectFailure);
                _state = ManagedHttpClientState.Connecting;
            }
            else if (_service.ResolutionState == NetworkResolutionState.NxDomain ||
                     _service.ResolutionState == NetworkResolutionState.Failed)
                return Fail(ManagedHttpFailureReason.DnsFailure);
            return NetworkOperationResult.Success;
        }

        if (_service.TcpState == NetworkTcpState.Failed)
            return Fail(_state == ManagedHttpClientState.Connecting
                ? ManagedHttpFailureReason.TcpConnectFailure
                : ManagedHttpFailureReason.TcpReset);
        if (_state == ManagedHttpClientState.Connecting)
        {
            if (_service.TcpState != NetworkTcpState.Established)
                return NetworkOperationResult.Success;
            NetworkOperationResult send = _service.SendTcp(
                _request.AsSpan(0, _requestLength));
            if (send != NetworkOperationResult.Success)
                return Fail(ManagedHttpFailureReason.TransportFailure);
            _requestSent = true;
            _state = ManagedHttpClientState.Receiving;
        }

        if (_state == ManagedHttpClientState.Receiving ||
            _state == ManagedHttpClientState.Closing)
        {
            if (_service.HasReceivedTcp)
            {
                if (!_service.TryReceiveTcp(_receive,
                        out _, out ushort sourcePort, out ushort destinationPort,
                        out int length) ||
                    sourcePort != ManagedTcpConnection.ServerPort ||
                    destinationPort != ManagedTcpConnection.ClientPort ||
                    length <= 0 || !_parser.Feed(_receive.AsSpan(0, length)))
                    return Fail(ManagedHttpFailureReason.HttpParseFailure);
            }
            if (_state == ManagedHttpClientState.Receiving &&
                _service.TcpState == NetworkTcpState.CloseWait)
            {
                if (!_parser.NotifyConnectionClosed() ||
                    _service.CloseTcp() != NetworkOperationResult.Started)
                    return Fail(_parser.FailureReason ==
                        ManagedHttpParseFailureReason.PrematureConnectionClose
                        ? ManagedHttpFailureReason.PrematureConnectionClose
                        : ManagedHttpFailureReason.TransportFailure);
                _state = ManagedHttpClientState.Closing;
            }
        }
        if (_state == ManagedHttpClientState.Closing &&
            _service.TcpState == NetworkTcpState.TimeWait)
        {
            if (_service.Teardown() != NetworkOperationResult.Success)
                return Fail(ManagedHttpFailureReason.TeardownFailure);
            _state = ManagedHttpClientState.Succeeded;
            return NetworkOperationResult.Success;
        }
        return _state == ManagedHttpClientState.Failed
            ? NetworkOperationResult.Failed : NetworkOperationResult.Success;
    }

    public bool TryCopyResponseBody(Span<byte> destination, out int length)
    {
        length = 0;
        if (_state != ManagedHttpClientState.Succeeded ||
            !_parser.TryCopyBody(_responseBody, out int parserLength) ||
            destination.Length < parserLength)
            return false;
        _responseBody.AsSpan(0, parserLength).CopyTo(destination);
        length = parserLength;
        return true;
    }

    public NetworkOperationResult Cancel()
    {
        if (_state == ManagedHttpClientState.Succeeded ||
            _state == ManagedHttpClientState.Cancelled)
            return NetworkOperationResult.Success;
        if (_state == ManagedHttpClientState.Failed)
            return NetworkOperationResult.Success;
        NetworkOperationResult result = _service.Teardown();
        _state = ManagedHttpClientState.Cancelled;
        _failureReason = ManagedHttpFailureReason.Cancelled;
        _parser.Reset();
        return result == NetworkOperationResult.Success
            ? NetworkOperationResult.Success : NetworkOperationResult.Failed;
    }

    public NetworkOperationResult Reset()
    {
        if (_state != ManagedHttpClientState.Idle &&
            _state != ManagedHttpClientState.Succeeded &&
            _state != ManagedHttpClientState.Failed &&
            _state != ManagedHttpClientState.Cancelled)
            return NetworkOperationResult.Busy;
        _state = ManagedHttpClientState.Idle;
        _failureReason = ManagedHttpFailureReason.None;
        _requestLength = 0;
        _requestSent = false;
        ResolvedAddress = default;
        _parser.Reset();
        _request.AsSpan().Clear();
        _receive.AsSpan().Clear();
        _responseBody.AsSpan().Clear();
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult Fail(ManagedHttpFailureReason reason)
    {
        if (_state != ManagedHttpClientState.Failed &&
            _state != ManagedHttpClientState.Succeeded)
            _service.Teardown();
        _failureReason = reason;
        _state = ManagedHttpClientState.Failed;
        return NetworkOperationResult.Failed;
    }

    private static ManagedHttpFailureReason MapFailure(
        NetworkOperationResult result, ManagedHttpFailureReason fallback)
    {
        return result == NetworkOperationResult.Unavailable
            ? ManagedHttpFailureReason.TransportFailure : fallback;
    }
}

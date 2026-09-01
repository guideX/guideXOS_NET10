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
    Cancelled = 9,
    SinkFailure = 10
}

public enum ManagedHttpBodySinkResult : byte
{
    Continue = 0,
    Pause = 1,
    Fail = 2
}

public enum ManagedHttpBodyDeliveryResult : byte
{
    NoData = 0,
    Delivered = 1,
    Paused = 2,
    Failed = 3,
    Cancelled = 4
}

public enum ManagedHttpTransferState : byte
{
    Idle = 0,
    Receiving = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}

public enum ManagedHttpTerminalFailureReason : byte
{
    None = 0,
    Cancelled = 1,
    SinkFailure = 2,
    BodyTooLarge = 3,
    MalformedHttp = 4,
    PrematureConnectionClose = 5,
    TransportFailure = 6,
    TlsFailure = 7,
    TeardownFailure = 8,
    RequestFailure = 9
}

public readonly struct ManagedHttpProgressSnapshot
{
    internal ManagedHttpProgressSnapshot(
        ManagedHttpTransferState state, int statusCode,
        ManagedHttpFramingMode transferMode, int decodedBodyBytesReceived,
        int decodedBodyBytesDelivered, int bufferedBodyBytes,
        int deliveredSegmentCount, int pauseCount, int resumeCount,
        bool hasKnownTotalLength, int totalEntityLength,
        ManagedHttpTerminalFailureReason terminalFailureReason,
        ManagedHttpParseFailureReason parseFailureReason)
    {
        State = state;
        StatusCode = statusCode;
        TransferMode = transferMode;
        DecodedBodyBytesReceived = decodedBodyBytesReceived;
        DecodedBodyBytesDelivered = decodedBodyBytesDelivered;
        BufferedBodyBytes = bufferedBodyBytes;
        DeliveredSegmentCount = deliveredSegmentCount;
        PauseCount = pauseCount;
        ResumeCount = resumeCount;
        HasKnownTotalLength = hasKnownTotalLength;
        TotalEntityLength = totalEntityLength;
        TerminalFailureReason = terminalFailureReason;
        ParseFailureReason = parseFailureReason;
    }

    public ManagedHttpTransferState State { get; }
    public int StatusCode { get; }
    public ManagedHttpFramingMode TransferMode { get; }
    public int DecodedBodyBytesReceived { get; }
    public int DecodedBodyBytesDelivered { get; }
    public int BufferedBodyBytes { get; }
    public int DeliveredSegmentCount { get; }
    public int PauseCount { get; }
    public int ResumeCount { get; }
    public bool HasKnownTotalLength { get; }
    public int TotalEntityLength { get; }
    public ManagedHttpTerminalFailureReason TerminalFailureReason { get; }
    public ManagedHttpParseFailureReason ParseFailureReason { get; }
    public bool IsTerminal => State == ManagedHttpTransferState.Completed ||
                              State == ManagedHttpTransferState.Cancelled ||
                              State == ManagedHttpTransferState.Failed;
}

public enum ManagedHttpParseState : byte
{
    Idle = 0,
    StatusLine = 1,
    Headers = 2,
    BodyContentLength = 3,
    ChunkSize = 4,
    ChunkData = 5,
    ChunkDataCrlf = 6,
    ChunkTrailers = 7,
    BodyUntilClose = 8,
    Complete = 9,
    Closed = 10,
    Failed = 11,

    // Compatibility aliases for the Phase 23 parser contract.
    Body = BodyContentLength,
    BodyComplete = Complete
}

public enum ManagedHttpFramingMode : byte
{
    None = 0,
    NoBody = 1,
    ContentLength = 2,
    Chunked = 3,
    ConnectionClose = 4
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
    PrematureConnectionClose = 16,
    ContentLengthOverflow = 17,
    AmbiguousFraming = 18,
    InvalidTransferEncoding = 19,
    ChunkSizeLineOverflow = 20,
    ChunkSizeSyntax = 21,
    ChunkSizeOverflow = 22,
    ChunkTooLarge = 23,
    ChunkDataCrlf = 24,
    TrailerLineOverflow = 25,
    TrailerBytes = 26,
    TrailerCount = 27,
    TrailerFramingField = 28,
    InformationalResponseLimit = 29,
    BodyDeliveryBufferFull = 30,
    TrailingData = 31,
    InvalidLocation = 32
}

public static class ManagedHttpLimits
{
    public const ushort DefaultHttpsPort = 443;
    public const int MaximumHostnameLength = 253;
    public const int MaximumPathLength = 128;
    public const int MaximumSerializedRequestSize = ManagedNetworkService.MaximumTcpPayloadLength;
    public const int MaximumStatusLineLength = 64;
    // The live public endpoint currently sends a 1,990-byte CSP line and 25
    // headers.  These remain bounded independently from the body stream.
    public const int MaximumHeaderLineLength = 2048;
    public const int MaximumHeaderCount = 32;
    public const int MaximumResponseHeaderBytes = 4096;
    public const int MaximumHeaderNameLength = 32;
    // This is the compatibility-sized body retained for TryCopyBody().
    public const int MaximumBodyCapacity = 256;
    // This remains the historical explicit total-body bound used by the
    // small-response parser callers.  It is not parser working storage.
    public const int MaximumAcceptedBodyLength = 16 * 1024;
    // Streaming callers may accept a larger bounded entity without allocating
    // storage proportional to that entity.  The delivery window remains the
    // parser's only body queue.
    public const int MaximumStreamedBodyLength = 1024 * 1024;
    public const int MaximumBodyDeliveryWindow = 1024;
    public const int MaximumChunkSizeLineLength = 128;
    public const int MaximumChunkExtensionLength = 64;
    public const int MaximumIndividualChunkSize = 4096;
    public const int MaximumTrailerLineLength = 96;
    public const int MaximumTrailerBytes = 256;
    public const int MaximumTrailerCount = 16;
    public const int MaximumInformationalResponses = 4;
    public const int MaximumReceiveStagingBuffer = ManagedNetworkService.MaximumTcpPayloadLength;
    public const int MaximumContentTypeLength = 64;
    public const int MaximumLocationLength = ManagedHttpsUrl.MaximumLocationLength;
}

/* A body sink receives one bounded parser segment at a time.  The segment is
   valid only for the duration of Consume; a sink must not retain it. */
public interface IManagedHttpBodySink
{
    ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment);
}

public static class ManagedHttpRequestBuilder
{
    public static bool TryBuildGet(ReadOnlySpan<byte> hostname,
                                   ReadOnlySpan<byte> path,
                                   Span<byte> destination,
                                   out int length)
    {
        return TryBuildGet(hostname, ManagedHttpLimits.DefaultHttpsPort, path,
                           destination, out length);
    }

    public static bool TryBuildGet(ReadOnlySpan<byte> hostname,
                                   ushort port,
                                   ReadOnlySpan<byte> path,
                                   Span<byte> destination,
                                   out int length)
    {
        length = 0;
        if (!IsValidHostname(hostname) || port == 0 || !TryValidatePath(path) ||
            destination.Length < ManagedHttpLimits.MaximumSerializedRequestSize)
            return false;

        int offset = 0;
        if (!Append(destination, ref offset, "GET "u8) ||
            !Append(destination, ref offset, path) ||
            !Append(destination, ref offset, " HTTP/1.1\r\nHost: "u8) ||
            !Append(destination, ref offset, hostname) ||
            (port != ManagedHttpLimits.DefaultHttpsPort &&
             (!Append(destination, ref offset, ":"u8) ||
              !AppendPort(destination, ref offset, port))) ||
            !Append(destination, ref offset, "\r\nConnection: close\r\n\r\n"u8))
            return false;
        length = offset;
        return length <= ManagedHttpLimits.MaximumSerializedRequestSize;
    }

    public static bool TryBuildGet(ReadOnlySpan<byte> hostname,
                                   ReadOnlySpan<byte> path,
                                   ushort port,
                                   Span<byte> destination,
                                   out int length)
    {
        return TryBuildGet(hostname, port, path, destination, out length);
    }

    private static bool Append(Span<byte> destination, ref int offset,
                               ReadOnlySpan<byte> value)
    {
        if (value.Length > destination.Length - offset) return false;
        value.CopyTo(destination.Slice(offset));
        offset += value.Length;
        return true;
    }

    public static bool IsValidHostname(ReadOnlySpan<byte> hostname)
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

    private static bool AppendPort(Span<byte> destination, ref int offset,
                                   ushort port)
    {
        Span<byte> digits = stackalloc byte[5];
        int count = 0;
        do
        {
            digits[count++] = (byte)('0' + port % 10);
            port /= 10;
        } while (port != 0);
        if (count > destination.Length - offset) return false;
        for (int index = 0; index != count; ++index)
            destination[offset + index] = digits[count - index - 1];
        offset += count;
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
    private readonly byte[] _body = new byte[ManagedHttpLimits.MaximumBodyDeliveryWindow];
    private readonly byte[] _compatibilityBody =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];
    private readonly byte[] _contentType = new byte[ManagedHttpLimits.MaximumContentTypeLength];
    private readonly byte[] _location = new byte[ManagedHttpLimits.MaximumLocationLength];
    private readonly int _maximumAcceptedBodyLength;
    private readonly bool _requireConnectionClose;
    private readonly bool _allowChunked;
    private ManagedHttpParseState _state;
    private ManagedHttpParseFailureReason _failureReason;
    private int _lineLength;
    private int _headerBytes;
    private int _headerCount;
    private int _statusCode;
    private int _contentLength;
    private int _bodyLength;
    private int _bodyDelivered;
    private int _bufferedBodyLength;
    private int _deliveredSegmentCount;
    private int _pauseCount;
    private int _resumeCount;
    private int _contentTypeLength;
    private int _chunkRemaining;
    private int _trailerBytes;
    private int _trailerCount;
    private int _informationalCount;
    private int _chunkCrlfProgress;
    private bool _sawCarriageReturn;
    private bool _hasContentLength;
    private bool _hasTransferEncoding;
    private bool _chunked;
    private bool _connectionClose;
    private bool _connectionKeepAlive;
    private bool _hasLocation;
    private int _locationLength;
    private bool _compatibilityBodyOverflow;
    private bool _bodyDeliveryPaused;
    private ManagedHttpFramingMode _framingMode;

    public ManagedHttpResponseParser()
        : this(ManagedHttpLimits.MaximumBodyCapacity, true, false)
    {
    }

    public ManagedHttpResponseParser(int maximumAcceptedBodyLength,
                                     bool requireConnectionClose = false)
        : this(maximumAcceptedBodyLength, requireConnectionClose, true)
    {
    }

    public ManagedHttpResponseParser(int maximumAcceptedBodyLength,
                                     bool requireConnectionClose,
                                     bool allowChunked)
    {
        if (maximumAcceptedBodyLength < 0 ||
            maximumAcceptedBodyLength > ManagedHttpLimits.MaximumStreamedBodyLength)
            throw new ArgumentOutOfRangeException(nameof(maximumAcceptedBodyLength));
        _maximumAcceptedBodyLength = maximumAcceptedBodyLength;
        _requireConnectionClose = requireConnectionClose;
        _allowChunked = allowChunked;
        Reset();
    }

    public ManagedHttpParseState State => _state;
    public ManagedHttpParseFailureReason FailureReason => _failureReason;
    public int StatusCode => _statusCode;
    public int HeaderCount => _headerCount;
    public int HeaderBytes => _headerBytes;
    public bool HasContentLength => _hasContentLength;
    public int ContentLength => _contentLength;
    public int BodyLength => _bodyLength;
    public int BodyBytesDelivered => _bodyDelivered;
    public int BufferedBodyLength => _bufferedBodyLength;
    public int DeliveredSegmentCount => _deliveredSegmentCount;
    public int PauseCount => _pauseCount;
    public int ResumeCount => _resumeCount;
    public bool IsBodyDeliveryPaused => _bodyDeliveryPaused;
    public bool IsBodyDeliveryWindowFull =>
        _bufferedBodyLength == ManagedHttpLimits.MaximumBodyDeliveryWindow;
    public int ContentTypeLength => _contentTypeLength;
    public bool HasTransferEncoding => _hasTransferEncoding;
    public bool HasLocation => _hasLocation;
    public bool ConnectionClose => _connectionClose;
    public bool IsChunked => _chunked;
    public ManagedHttpFramingMode FramingMode => _framingMode;
    public bool IsStatusParsed => _state >= ManagedHttpParseState.Headers &&
                                  _failureReason == ManagedHttpParseFailureReason.None;
    public bool IsBodyComplete => _state == ManagedHttpParseState.Complete ||
                                  _state == ManagedHttpParseState.Closed;
    public bool HasPendingBody => _bufferedBodyLength != 0;
    public ManagedHttpProgressSnapshot Progress => CreateProgressSnapshot(
        _failureReason != ManagedHttpParseFailureReason.None
            ? ManagedHttpTransferState.Failed
            : _bodyDeliveryPaused
                ? ManagedHttpTransferState.Paused
                : IsBodyComplete
                    ? ManagedHttpTransferState.Completed
                    : ManagedHttpTransferState.Receiving,
        _failureReason == ManagedHttpParseFailureReason.None
            ? ManagedHttpTerminalFailureReason.None
            : _failureReason == ManagedHttpParseFailureReason.BodyTooLarge
                ? ManagedHttpTerminalFailureReason.BodyTooLarge
                : ManagedHttpTerminalFailureReason.MalformedHttp);

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
        _bodyDelivered = 0;
        _bufferedBodyLength = 0;
        _deliveredSegmentCount = 0;
        _pauseCount = 0;
        _resumeCount = 0;
        _contentTypeLength = 0;
        _chunkRemaining = 0;
        _trailerBytes = 0;
        _trailerCount = 0;
        _informationalCount = 0;
        _chunkCrlfProgress = 0;
        _sawCarriageReturn = false;
        _hasContentLength = false;
        _hasTransferEncoding = false;
        _chunked = false;
        _connectionClose = false;
        _connectionKeepAlive = false;
        _hasLocation = false;
        _locationLength = 0;
        _compatibilityBodyOverflow = false;
        _bodyDeliveryPaused = false;
        _framingMode = ManagedHttpFramingMode.None;
        _line.AsSpan().Clear();
        _body.AsSpan().Clear();
        _compatibilityBody.AsSpan().Clear();
        _contentType.AsSpan().Clear();
        _location.AsSpan().Clear();
    }

    public bool Feed(ReadOnlySpan<byte> bytes)
    {
        if (!TryFeed(bytes, out int consumed)) return false;
        if (consumed == bytes.Length) return true;
        if (_bodyDeliveryPaused) return false;
        return Fail(ManagedHttpParseFailureReason.BodyDeliveryBufferFull);
    }

    /* Feed returns the number of source bytes actually consumed.  A successful
       call may stop before the end only when the bounded body delivery window
       is full; the caller must drain it and resume with the remainder. */
    public bool TryFeed(ReadOnlySpan<byte> bytes, out int consumed)
    {
        consumed = 0;
        if (_state == ManagedHttpParseState.Failed ||
            _state == ManagedHttpParseState.Closed)
            return bytes.Length == 0;
        if (_bodyDeliveryPaused) return true;

        while (consumed != bytes.Length)
        {
            if (_state == ManagedHttpParseState.BodyContentLength ||
                _state == ManagedHttpParseState.BodyUntilClose)
            {
                if (!TryAcceptBodyByte(bytes[consumed], out bool accepted))
                    return false;
                if (!accepted) return true;
                consumed++;
                continue;
            }

            if (_state == ManagedHttpParseState.ChunkData)
            {
                if (_chunkRemaining == 0)
                {
                    _state = ManagedHttpParseState.ChunkDataCrlf;
                    _chunkCrlfProgress = 0;
                    continue;
                }
                if (!TryAcceptBodyByte(bytes[consumed], out bool accepted))
                    return false;
                if (!accepted) return true;
                consumed++;
                _chunkRemaining--;
                continue;
            }

            if (_state == ManagedHttpParseState.ChunkDataCrlf)
            {
                byte value = bytes[consumed++];
                if ((_chunkCrlfProgress == 0 && value != (byte)'\r') ||
                    (_chunkCrlfProgress == 1 && value != (byte)'\n'))
                    return Fail(ManagedHttpParseFailureReason.ChunkDataCrlf);
                _chunkCrlfProgress++;
                if (_chunkCrlfProgress == 2)
                {
                    _chunkCrlfProgress = 0;
                    _state = ManagedHttpParseState.ChunkSize;
                }
                continue;
            }

            if (_state == ManagedHttpParseState.Complete)
                return Fail(_framingMode == ManagedHttpFramingMode.ContentLength
                    ? ManagedHttpParseFailureReason.BodyExceedsContentLength
                    : ManagedHttpParseFailureReason.TrailingData);

            if (_state == ManagedHttpParseState.StatusLine ||
                _state == ManagedHttpParseState.Headers ||
                _state == ManagedHttpParseState.ChunkSize ||
                _state == ManagedHttpParseState.ChunkTrailers)
            {
                byte value = bytes[consumed++];
                if (_state == ManagedHttpParseState.ChunkTrailers)
                {
                    if (_trailerBytes == ManagedHttpLimits.MaximumTrailerBytes)
                        return Fail(ManagedHttpParseFailureReason.TrailerBytes);
                    _trailerBytes++;
                }
                else if (_state != ManagedHttpParseState.ChunkSize)
                {
                    if (_headerBytes == ManagedHttpLimits.MaximumResponseHeaderBytes)
                        return Fail(ManagedHttpParseFailureReason.HeaderBytes);
                    _headerBytes++;
                }
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
                if (value == (byte)'\n' || value == 0x7F ||
                    (value < 0x20 && value != 0x09) || value > 0x7E)
                    return Fail(ManagedHttpParseFailureReason.LineFraming);
                int lineLimit = _state == ManagedHttpParseState.StatusLine
                    ? ManagedHttpLimits.MaximumStatusLineLength
                    : _state == ManagedHttpParseState.ChunkSize
                        ? ManagedHttpLimits.MaximumChunkSizeLineLength
                        : _state == ManagedHttpParseState.ChunkTrailers
                            ? ManagedHttpLimits.MaximumTrailerLineLength
                            : ManagedHttpLimits.MaximumHeaderLineLength;
                if (_lineLength == lineLimit)
                {
                    return Fail(_state == ManagedHttpParseState.StatusLine
                        ? ManagedHttpParseFailureReason.StatusLineOverflow
                        : _state == ManagedHttpParseState.ChunkSize
                            ? ManagedHttpParseFailureReason.ChunkSizeLineOverflow
                            : _state == ManagedHttpParseState.ChunkTrailers
                                ? ManagedHttpParseFailureReason.TrailerLineOverflow
                                : ManagedHttpParseFailureReason.HeaderLineOverflow);
                }
                _line[_lineLength++] = value;
                continue;
            }

            return Fail(ManagedHttpParseFailureReason.StatusLine);
        }
        return true;
    }

    public bool NotifyConnectionClosed()
    {
        if (_state == ManagedHttpParseState.Closed)
            return true;
        if (_state == ManagedHttpParseState.BodyUntilClose)
        {
            _framingMode = ManagedHttpFramingMode.ConnectionClose;
            _state = ManagedHttpParseState.Complete;
        }
        if (_state != ManagedHttpParseState.Complete)
            return Fail(ManagedHttpParseFailureReason.PrematureConnectionClose);
        _state = ManagedHttpParseState.Closed;
        return true;
    }

    public bool TryReadBodyChunk(Span<byte> destination, out int length)
    {
        length = 0;
        if (destination.Length == 0 || _bufferedBodyLength == 0) return false;
        length = Math.Min(destination.Length, _bufferedBodyLength);
        _body.AsSpan(0, length).CopyTo(destination);
        int remaining = _bufferedBodyLength - length;
        if (remaining != 0)
            _body.AsSpan(length, remaining).CopyTo(_body);
        _body.AsSpan(remaining, length).Clear();
        _bufferedBodyLength = remaining;
        _bodyDelivered += length;
        _deliveredSegmentCount++;
        if (_bodyDeliveryPaused)
        {
            _bodyDeliveryPaused = false;
            _resumeCount++;
        }
        return true;
    }

    /* Delivers the current bounded segment without allocating a second
       response-sized buffer.  Pause and Fail both leave the exact segment in
       parser ownership, so the next call observes the same bytes. */
    public ManagedHttpBodyDeliveryResult ConsumeBody(IManagedHttpBodySink sink)
    {
        if (sink == null) throw new ArgumentNullException(nameof(sink));
        if (_bufferedBodyLength == 0)
        {
            return ManagedHttpBodyDeliveryResult.NoData;
        }
        int length = _bufferedBodyLength;
        ManagedHttpBodySinkResult result = sink.Consume(
            _body.AsSpan(0, length));
        if (result == ManagedHttpBodySinkResult.Continue)
        {
            _body.AsSpan(0, length).Clear();
            _bufferedBodyLength = 0;
            _bodyDelivered += length;
            _deliveredSegmentCount++;
            if (_bodyDeliveryPaused)
            {
                _bodyDeliveryPaused = false;
                _resumeCount++;
            }
            return ManagedHttpBodyDeliveryResult.Delivered;
        }
        if (result == ManagedHttpBodySinkResult.Pause)
        {
            if (!_bodyDeliveryPaused)
            {
                _bodyDeliveryPaused = true;
                _pauseCount++;
            }
            return ManagedHttpBodyDeliveryResult.Paused;
        }
        return ManagedHttpBodyDeliveryResult.Failed;
    }

    /* Compatibility wrapper for Phase 37 and earlier callers.  New callers
       should use ConsumeBody to distinguish Pause from a successful drain. */
    public bool TryConsumeBody(IManagedHttpBodySink sink)
    {
        return ConsumeBody(sink) != ManagedHttpBodyDeliveryResult.Failed;
    }

    internal ManagedHttpProgressSnapshot CreateProgressSnapshot(
        ManagedHttpTransferState state,
        ManagedHttpTerminalFailureReason terminalFailureReason)
    {
        return new ManagedHttpProgressSnapshot(
            state, _statusCode, _framingMode, _bodyLength, _bodyDelivered,
            _bufferedBodyLength, _deliveredSegmentCount, _pauseCount,
            _resumeCount, _hasContentLength, _contentLength,
            terminalFailureReason, _failureReason);
    }

    public bool TryCopyBody(Span<byte> destination, out int length)
    {
        length = _bodyLength;
        if (!IsBodyComplete || _compatibilityBodyOverflow ||
            destination.Length < _bodyLength || _bodyLength > _compatibilityBody.Length)
            return false;
        _compatibilityBody.AsSpan(0, _bodyLength).CopyTo(destination);
        return true;
    }

    public bool TryCopyLocation(Span<byte> destination, out int length)
    {
        length = _locationLength;
        if (!_hasLocation || destination.Length < length) return false;
        _location.AsSpan(0, length).CopyTo(destination);
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
        if (_state == ManagedHttpParseState.ChunkSize)
        {
            if (!ParseChunkSize()) return false;
            _lineLength = 0;
            return true;
        }
        if (_state == ManagedHttpParseState.ChunkTrailers)
        {
            if (_lineLength == 0)
            {
                _framingMode = ManagedHttpFramingMode.Chunked;
                _state = ManagedHttpParseState.Complete;
                _lineLength = 0;
                return true;
            }
            if (++_trailerCount > ManagedHttpLimits.MaximumTrailerCount)
                return Fail(ManagedHttpParseFailureReason.TrailerCount);
            if (!ParseHeader(isTrailer: true)) return false;
            _lineLength = 0;
            return true;
        }
        if (_lineLength == 0)
        {
            if (_statusCode >= 100 && _statusCode <= 199)
            {
                if (++_informationalCount > ManagedHttpLimits.MaximumInformationalResponses)
                    return Fail(ManagedHttpParseFailureReason.InformationalResponseLimit);
                PrepareForNextResponse();
                return true;
            }
            if (IsBodyProhibitedStatus(_statusCode))
            {
                _framingMode = ManagedHttpFramingMode.NoBody;
                _state = ManagedHttpParseState.Complete;
                _lineLength = 0;
                return true;
            }
            if (_hasTransferEncoding)
            {
                if (_hasContentLength)
                    return Fail(ManagedHttpParseFailureReason.AmbiguousFraming);
                _framingMode = ManagedHttpFramingMode.Chunked;
                _state = ManagedHttpParseState.ChunkSize;
                _lineLength = 0;
                return true;
            }
            if (_hasContentLength)
            {
                if (_requireConnectionClose && !_connectionClose)
                    return Fail(ManagedHttpParseFailureReason.MissingConnectionClose);
                _framingMode = ManagedHttpFramingMode.ContentLength;
                _state = _contentLength == 0
                    ? ManagedHttpParseState.Complete
                    : ManagedHttpParseState.BodyContentLength;
                _lineLength = 0;
                return true;
            }
            if (_connectionKeepAlive)
                return Fail(ManagedHttpParseFailureReason.MissingConnectionClose);
            _framingMode = ManagedHttpFramingMode.ConnectionClose;
            _state = ManagedHttpParseState.BodyUntilClose;
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

    private bool ParseHeader(bool isTrailer = false)
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
        if (isTrailer)
        {
            if (EqualsAsciiIgnoreCase(name, "Content-Length"u8) ||
                EqualsAsciiIgnoreCase(name, "Transfer-Encoding"u8))
                return Fail(ManagedHttpParseFailureReason.TrailerFramingField);
            return true;
        }
        if (EqualsAsciiIgnoreCase(name, "Content-Length"u8))
            return ParseContentLength(value);
        if (EqualsAsciiIgnoreCase(name, "Connection"u8))
        {
            return ParseConnection(value);
        }
        if (EqualsAsciiIgnoreCase(name, "Transfer-Encoding"u8))
            return ParseTransferEncoding(value);
        if (EqualsAsciiIgnoreCase(name, "Content-Type"u8))
        {
            if (value.Length > ManagedHttpLimits.MaximumContentTypeLength)
                return Fail(ManagedHttpParseFailureReason.HeaderLineOverflow);
            value.CopyTo(_contentType);
            _contentTypeLength = value.Length;
        }
        if (EqualsAsciiIgnoreCase(name, "Location"u8))
        {
            if (_hasLocation || value.Length > ManagedHttpLimits.MaximumLocationLength)
                return Fail(ManagedHttpParseFailureReason.InvalidLocation);
            for (int index = 0; index != value.Length; ++index)
                if (value[index] < 0x21 || value[index] > 0x7E)
                    return Fail(ManagedHttpParseFailureReason.InvalidLocation);
            value.CopyTo(_location);
            _locationLength = value.Length;
            _hasLocation = true;
        }
        return true;
    }

    private bool ParseContentLength(ReadOnlySpan<byte> value)
    {
        int offset = 0;
        bool sawValue = false;
        while (offset < value.Length)
        {
            while (offset < value.Length && IsWhitespace(value[offset])) offset++;
            int start = offset;
            while (offset < value.Length && value[offset] != (byte)',') offset++;
            int end = offset;
            while (end > start && IsWhitespace(value[end - 1])) end--;
            if (start == end) return Fail(ManagedHttpParseFailureReason.ContentLength);
            ulong parsed = 0;
            for (int index = start; index != end; ++index)
            {
                byte digit = value[index];
                if (digit < (byte)'0' || digit > (byte)'9')
                    return Fail(ManagedHttpParseFailureReason.ContentLength);
                uint number = (uint)(digit - (byte)'0');
                if (parsed > ((ulong)int.MaxValue - number) / 10)
                    return Fail(ManagedHttpParseFailureReason.ContentLengthOverflow);
                parsed = parsed * 10 + number;
            }
            if (parsed > (ulong)_maximumAcceptedBodyLength)
                return Fail(ManagedHttpParseFailureReason.BodyTooLarge);
            int candidate = (int)parsed;
            if (_hasContentLength && _contentLength != candidate)
                return Fail(ManagedHttpParseFailureReason.ConflictingContentLength);
            _contentLength = candidate;
            sawValue = true;
            if (offset == value.Length) break;
            offset++;
            if (offset == value.Length)
                return Fail(ManagedHttpParseFailureReason.ContentLength);
        }
        if (!sawValue) return Fail(ManagedHttpParseFailureReason.ContentLength);
        _hasContentLength = true;
        if (_hasTransferEncoding)
            return Fail(ManagedHttpParseFailureReason.AmbiguousFraming);
        return true;
    }

    private bool ParseTransferEncoding(ReadOnlySpan<byte> value)
    {
        if (!_allowChunked)
            return Fail(ManagedHttpParseFailureReason.UnsupportedTransferEncoding);
        if (_hasTransferEncoding || value.Length == 0)
            return Fail(ManagedHttpParseFailureReason.InvalidTransferEncoding);
        int start = 0;
        while (start < value.Length && IsWhitespace(value[start])) start++;
        int end = value.Length;
        while (end > start && IsWhitespace(value[end - 1])) end--;
        if (start == end || !EqualsAsciiIgnoreCase(
                value.Slice(start, end - start), "chunked"u8))
            return Fail(ManagedHttpParseFailureReason.UnsupportedTransferEncoding);
        if (_hasContentLength)
            return Fail(ManagedHttpParseFailureReason.AmbiguousFraming);
        _hasTransferEncoding = true;
        _chunked = true;
        return true;
    }

    private bool ParseConnection(ReadOnlySpan<byte> value)
    {
        int offset = 0;
        bool sawToken = false;
        while (offset < value.Length)
        {
            while (offset < value.Length && (IsWhitespace(value[offset]) ||
                                              value[offset] == (byte)',')) offset++;
            int start = offset;
            while (offset < value.Length && value[offset] != (byte)',') offset++;
            int end = offset;
            while (end > start && IsWhitespace(value[end - 1])) end--;
            if (start == end) return Fail(ManagedHttpParseFailureReason.HeaderSyntax);
            ReadOnlySpan<byte> token = value.Slice(start, end - start);
            if (!IsTokenSpan(token))
                return Fail(ManagedHttpParseFailureReason.HeaderSyntax);
            if (EqualsAsciiIgnoreCase(token, "close"u8)) _connectionClose = true;
            if (EqualsAsciiIgnoreCase(token, "keep-alive"u8)) _connectionKeepAlive = true;
            sawToken = true;
            if (offset != value.Length) offset++;
        }
        return sawToken ? true : Fail(ManagedHttpParseFailureReason.HeaderSyntax);
    }

    private bool ParseChunkSize()
    {
        int sizeEnd = 0;
        ulong size = 0;
        while (sizeEnd < _lineLength && IsHex(_line[sizeEnd]))
        {
            uint digit = HexValue(_line[sizeEnd++]);
            if (size > ((ulong)int.MaxValue - digit) / 16)
                return Fail(ManagedHttpParseFailureReason.ChunkSizeOverflow);
            size = size * 16 + digit;
        }
        if (sizeEnd == 0 || (sizeEnd < _lineLength && _line[sizeEnd] != (byte)';'))
            return Fail(ManagedHttpParseFailureReason.ChunkSizeSyntax);
        if (sizeEnd < _lineLength)
        {
            int extensionLength = _lineLength - sizeEnd;
            if (extensionLength > ManagedHttpLimits.MaximumChunkExtensionLength)
                return Fail(ManagedHttpParseFailureReason.ChunkSizeSyntax);
            for (int index = sizeEnd; index != _lineLength; ++index)
                if (_line[index] < 0x21 || _line[index] > 0x7E)
                    return Fail(ManagedHttpParseFailureReason.ChunkSizeSyntax);
        }
        if (size > (ulong)ManagedHttpLimits.MaximumIndividualChunkSize)
            return Fail(ManagedHttpParseFailureReason.ChunkTooLarge);
        _chunkRemaining = (int)size;
        _state = size == 0 ? ManagedHttpParseState.ChunkTrailers :
                            ManagedHttpParseState.ChunkData;
        return true;
    }

    private bool TryAcceptBodyByte(byte value, out bool accepted)
    {
        accepted = false;
        if (_bufferedBodyLength == _body.Length) return true;
        if (_bodyLength == _maximumAcceptedBodyLength)
        {
            Fail(ManagedHttpParseFailureReason.BodyTooLarge);
            return false;
        }
        if (_state == ManagedHttpParseState.BodyContentLength &&
            _bodyLength == _contentLength)
        {
            Fail(ManagedHttpParseFailureReason.BodyExceedsContentLength);
            return false;
        }
        _body[_bufferedBodyLength++] = value;
        if (_bodyLength < _compatibilityBody.Length)
            _compatibilityBody[_bodyLength] = value;
        else
            _compatibilityBodyOverflow = true;
        _bodyLength++;
        accepted = true;
        if (_state == ManagedHttpParseState.BodyContentLength &&
            _bodyLength == _contentLength)
            _state = ManagedHttpParseState.Complete;
        return true;
    }

    private void PrepareForNextResponse()
    {
        _state = ManagedHttpParseState.StatusLine;
        _lineLength = 0;
        _headerCount = 0;
        _statusCode = 0;
        _contentLength = 0;
        _contentTypeLength = 0;
        _hasContentLength = false;
        _hasTransferEncoding = false;
        _chunked = false;
        _connectionClose = false;
        _connectionKeepAlive = false;
        _hasLocation = false;
        _locationLength = 0;
        _location.AsSpan().Clear();
        _framingMode = ManagedHttpFramingMode.None;
    }

    private static bool IsBodyProhibitedStatus(int statusCode) =>
        (statusCode >= 100 && statusCode <= 199) || statusCode == 204 || statusCode == 304;

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

    private static bool IsHex(byte value) =>
        (value >= (byte)'0' && value <= (byte)'9') ||
        (value >= (byte)'A' && value <= (byte)'F') ||
        (value >= (byte)'a' && value <= (byte)'f');

    private static uint HexValue(byte value) => value <= (byte)'9'
        ? (uint)(value - (byte)'0')
        : value <= (byte)'F'
            ? (uint)(value - (byte)'A' + 10)
            : (uint)(value - (byte)'a' + 10);

    private static bool IsTokenSpan(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0) return false;
        for (int index = 0; index != value.Length; ++index)
            if (!IsToken(value[index])) return false;
        return true;
    }

    private static bool IsToken(byte value)
    {
        return (value >= (byte)'A' && value <= (byte)'Z') ||
               (value >= (byte)'a' && value <= (byte)'z') ||
               (value >= (byte)'0' && value <= (byte)'9') ||
               value == (byte)'-' || value == (byte)'!' || value == (byte)'#' ||
               value == (byte)'$' || value == (byte)'%' || value == (byte)'&' ||
               value == (byte)'\'' || value == (byte)'*' || value == (byte)'+' ||
               value == (byte)'.' || value == (byte)'^' || value == (byte)'_' ||
               value == (byte)'`' || value == (byte)'|' || value == (byte)'~';
    }
}

public sealed class ManagedHttpClient
{
    private readonly ManagedNetworkService _service;
    private readonly byte[] _request =
        new byte[ManagedHttpLimits.MaximumSerializedRequestSize];
    private readonly byte[] _receive =
        new byte[ManagedHttpLimits.MaximumReceiveStagingBuffer];
    private readonly byte[] _pendingReceive =
        new byte[ManagedHttpLimits.MaximumReceiveStagingBuffer];
    private readonly byte[] _responseBody =
        new byte[ManagedHttpLimits.MaximumBodyCapacity];
    private readonly ManagedHttpResponseParser _parser;
    private ManagedHttpClientState _state;
    private ManagedHttpFailureReason _failureReason;
    private int _requestLength;
    private int _pendingReceiveLength;
    private bool _requestSent;

    public ManagedHttpClient(ManagedNetworkService service)
        : this(service, ManagedHttpLimits.MaximumBodyCapacity, true)
    {
    }

    public ManagedHttpClient(ManagedNetworkService service,
                             int maximumResponseBodyLength,
                             bool requireConnectionClose = false)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _parser = new ManagedHttpResponseParser(maximumResponseBodyLength,
                                                requireConnectionClose);
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
    public int ResponseBodyBytesDelivered => _parser.BodyBytesDelivered;
    public int BufferedResponseBodyLength => _parser.BufferedBodyLength;
    public int DeliveredResponseBodySegmentCount =>
        _parser.DeliveredSegmentCount;
    public int ContentLength => _parser.ContentLength;
    public ManagedHttpFramingMode FramingMode => _parser.FramingMode;
    public bool ResponseBodyComplete => _parser.IsBodyComplete;
    public ManagedHttpParseFailureReason ParseFailureReason => _parser.FailureReason;
    public ManagedHttpProgressSnapshot Progress =>
        _parser.CreateProgressSnapshot(GetTransferState(), GetTerminalFailure());

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
        _pendingReceive.AsSpan().Clear();
        _responseBody.AsSpan().Clear();
        _pendingReceiveLength = 0;
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
        if (_parser.IsBodyDeliveryPaused ||
            (!_parser.IsBodyComplete && _parser.IsBodyDeliveryWindowFull))
            return NetworkOperationResult.Success;
        if (_pendingReceiveLength != 0)
        {
            if (!FeedPendingReceive())
                return Fail(ManagedHttpFailureReason.HttpParseFailure);
            if (_pendingReceiveLength != 0)
                return NetworkOperationResult.Success;
            if (_parser.IsBodyDeliveryPaused ||
                (!_parser.IsBodyComplete && _parser.IsBodyDeliveryWindowFull))
                return NetworkOperationResult.Success;
        }

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
                    length <= 0 || !FeedReceived(_receive.AsSpan(0, length)))
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

    public bool TryReadResponseBodyChunk(Span<byte> destination, out int length)
    {
        if (_state == ManagedHttpClientState.Cancelled)
        {
            length = 0;
            return false;
        }
        return _parser.TryReadBodyChunk(destination, out length);
    }

    public ManagedHttpBodyDeliveryResult ConsumeResponseBody(
        IManagedHttpBodySink sink)
    {
        if (_state == ManagedHttpClientState.Cancelled)
            return ManagedHttpBodyDeliveryResult.Cancelled;
        if (_state == ManagedHttpClientState.Failed)
            return ManagedHttpBodyDeliveryResult.Failed;
        ManagedHttpBodyDeliveryResult result = _parser.ConsumeBody(sink);
        if (result == ManagedHttpBodyDeliveryResult.Failed)
        {
            Fail(ManagedHttpFailureReason.SinkFailure);
        }
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
        if (_state == ManagedHttpClientState.Succeeded ||
            _state == ManagedHttpClientState.Cancelled)
            return NetworkOperationResult.Success;
        if (_state == ManagedHttpClientState.Failed)
            return NetworkOperationResult.Success;
        NetworkOperationResult result = _service.Teardown();
        _request.AsSpan().Clear();
        _receive.AsSpan().Clear();
        _pendingReceive.AsSpan().Clear();
        _responseBody.AsSpan().Clear();
        _requestLength = 0;
        _pendingReceiveLength = 0;
        _requestSent = false;
        _state = ManagedHttpClientState.Cancelled;
        _failureReason = ManagedHttpFailureReason.Cancelled;
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
        _pendingReceiveLength = 0;
        _requestSent = false;
        ResolvedAddress = default;
        _parser.Reset();
        _request.AsSpan().Clear();
        _receive.AsSpan().Clear();
        _pendingReceive.AsSpan().Clear();
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

    private ManagedHttpTransferState GetTransferState()
    {
        if (_state == ManagedHttpClientState.Cancelled)
            return ManagedHttpTransferState.Cancelled;
        if (_state == ManagedHttpClientState.Failed)
            return ManagedHttpTransferState.Failed;
        if (_parser.IsBodyDeliveryPaused)
            return ManagedHttpTransferState.Paused;
        if (_state == ManagedHttpClientState.Succeeded)
            return ManagedHttpTransferState.Completed;
        if (_state == ManagedHttpClientState.Idle)
            return ManagedHttpTransferState.Idle;
        return ManagedHttpTransferState.Receiving;
    }

    private ManagedHttpTerminalFailureReason GetTerminalFailure()
    {
        if (_state == ManagedHttpClientState.Cancelled ||
            _failureReason == ManagedHttpFailureReason.Cancelled)
            return ManagedHttpTerminalFailureReason.Cancelled;
        if (_failureReason == ManagedHttpFailureReason.SinkFailure)
            return ManagedHttpTerminalFailureReason.SinkFailure;
        if (_parser.FailureReason == ManagedHttpParseFailureReason.BodyTooLarge)
            return ManagedHttpTerminalFailureReason.BodyTooLarge;
        if (_failureReason == ManagedHttpFailureReason.HttpParseFailure)
            return ManagedHttpTerminalFailureReason.MalformedHttp;
        if (_failureReason == ManagedHttpFailureReason.PrematureConnectionClose)
            return ManagedHttpTerminalFailureReason.PrematureConnectionClose;
        if (_failureReason == ManagedHttpFailureReason.TransportFailure ||
            _failureReason == ManagedHttpFailureReason.TcpReset ||
            _failureReason == ManagedHttpFailureReason.TcpConnectFailure ||
            _failureReason == ManagedHttpFailureReason.DnsFailure)
            return ManagedHttpTerminalFailureReason.TransportFailure;
        if (_failureReason == ManagedHttpFailureReason.TeardownFailure)
            return ManagedHttpTerminalFailureReason.TeardownFailure;
        return _state == ManagedHttpClientState.Failed
            ? ManagedHttpTerminalFailureReason.RequestFailure
            : ManagedHttpTerminalFailureReason.None;
    }

    private bool FeedReceived(ReadOnlySpan<byte> value)
    {
        if (!_parser.TryFeed(value, out int consumed)) return false;
        if (consumed == value.Length) return true;
        value[consumed..].CopyTo(_pendingReceive);
        _pendingReceiveLength = value.Length - consumed;
        return true;
    }

    private bool FeedPendingReceive()
    {
        if (!_parser.TryFeed(_pendingReceive.AsSpan(0, _pendingReceiveLength),
                             out int consumed))
            return false;
        if (consumed == _pendingReceiveLength)
        {
            _pendingReceive.AsSpan().Clear();
            _pendingReceiveLength = 0;
            return true;
        }
        int remaining = _pendingReceiveLength - consumed;
        _pendingReceive.AsSpan(consumed, remaining).CopyTo(_pendingReceive);
        _pendingReceive.AsSpan(remaining, consumed).Clear();
        _pendingReceiveLength = remaining;
        return true;
    }

    private static ManagedHttpFailureReason MapFailure(
        NetworkOperationResult result, ManagedHttpFailureReason fallback)
    {
        return result == NetworkOperationResult.Unavailable
            ? ManagedHttpFailureReason.TransportFailure : fallback;
    }
}

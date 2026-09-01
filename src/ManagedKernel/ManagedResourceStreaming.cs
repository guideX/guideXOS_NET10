using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedResourceProtocol : byte
{
    Http = 0,
    Https = 1
}

public enum ManagedResourceState : byte
{
    Idle = 0,
    Receiving = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}

public enum ManagedResourceConsumerState : byte
{
    Idle = 0,
    Receiving = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}

public enum ManagedResourceConsumerFailureReason : byte
{
    None = 0,
    DestinationFull = 1,
    ConsumerFailure = 2,
    FinalizationFailure = 3,
    ComponentPauseAfterAcceptance = 4
}

public enum ManagedResourceFailureReason : byte
{
    None = 0,
    Cancelled = 1,
    DestinationFull = 2,
    ConsumerFailure = 3,
    HttpParserFailure = 4,
    BodyTooLarge = 5,
    PrematureConnectionClose = 6,
    TransportFailure = 7,
    TlsFailure = 8,
    RequestFailure = 9,
    TeardownFailure = 10,
    UnsupportedContentEncoding = 11,
    MalformedContentEncoding = 12,
    ContentEncodingHeaderTooLong = 13,
    MalformedGzipHeader = 14,
    MalformedZlibHeader = 15,
    MalformedDeflateStream = 16,
    GzipCrcMismatch = 17,
    GzipIsizeMismatch = 18,
    ZlibAdlerMismatch = 19,
    TruncatedCompressedStream = 20,
    DecodedResourceTooLarge = 21,
    TrailingCompressedData = 22
}

public interface IManagedResourceConsumer : IManagedHttpBodySink
{
    ManagedResourceConsumerState State { get; }
    ManagedResourceConsumerFailureReason FailureReason { get; }
    int BytesProcessed { get; }
    bool Complete();
    void Cancel();
    void Reset();
}

public readonly struct ManagedResourceProgressSnapshot
{
    internal ManagedResourceProgressSnapshot(
        ManagedResourceState state,
        ManagedResourceProtocol protocol,
        ManagedResourceConsumerState consumerState,
        ManagedResourceFailureReason failureReason,
        ManagedResourceConsumerFailureReason consumerFailureReason,
        ManagedHttpFailureReason httpFailureReason,
        ManagedHttpsFailureReason httpsFailureReason,
        ManagedHttpParseFailureReason parseFailureReason,
        int statusCode,
        ManagedHttpFramingMode transferMode,
        bool hasKnownTotalLength,
        int totalEntityLength,
        int receivedBytes,
        int deliveredBytes,
        int resourceBytesProcessed,
        int bufferedBytes,
        int peakBufferedBytes,
        int deliveredSegmentCount,
        int pauseCount,
        int resumeCount,
        ManagedHttpContentTypeState contentTypeState,
        int contentTypeLength,
        ManagedHttpContentEncodingState contentEncodingState,
        int contentEncodingLength,
        int encodedBytesReceived,
        int encodedBytesConsumed,
        int decodedBytesProduced,
        int bufferedDecodedBytes,
        int peakDecodedOutputBytes,
        ManagedContentDecoderState decoderState,
        ManagedContentDecoderFailureReason decoderFailureReason,
        int decoderPauseCount,
        int decoderResumeCount,
        int decoderHistoryWindowSize,
        bool crcValidated,
        bool isizeValidated,
        bool adlerValidated)
    {
        State = state;
        Protocol = protocol;
        ConsumerState = consumerState;
        FailureReason = failureReason;
        ConsumerFailureReason = consumerFailureReason;
        HttpFailureReason = httpFailureReason;
        HttpsFailureReason = httpsFailureReason;
        ParseFailureReason = parseFailureReason;
        StatusCode = statusCode;
        TransferMode = transferMode;
        HasKnownTotalLength = hasKnownTotalLength;
        TotalEntityLength = totalEntityLength;
        ReceivedBytes = receivedBytes;
        DeliveredBytes = deliveredBytes;
        ResourceBytesProcessed = resourceBytesProcessed;
        BufferedBytes = bufferedBytes;
        PeakBufferedBytes = peakBufferedBytes;
        DeliveredSegmentCount = deliveredSegmentCount;
        PauseCount = pauseCount;
        ResumeCount = resumeCount;
        ContentTypeState = contentTypeState;
        ContentTypeLength = contentTypeLength;
        ContentEncodingState = contentEncodingState;
        ContentEncodingLength = contentEncodingLength;
        EncodedBytesReceived = encodedBytesReceived;
        EncodedBytesConsumed = encodedBytesConsumed;
        DecodedBytesProduced = decodedBytesProduced;
        BufferedDecodedBytes = bufferedDecodedBytes;
        PeakDecodedOutputBytes = peakDecodedOutputBytes;
        DecoderState = decoderState;
        DecoderFailureReason = decoderFailureReason;
        DecoderPauseCount = decoderPauseCount;
        DecoderResumeCount = decoderResumeCount;
        DecoderHistoryWindowSize = decoderHistoryWindowSize;
        CrcValidated = crcValidated;
        IsizeValidated = isizeValidated;
        AdlerValidated = adlerValidated;
    }

    public ManagedResourceState State { get; }
    public ManagedResourceProtocol Protocol { get; }
    public ManagedResourceConsumerState ConsumerState { get; }
    public ManagedResourceFailureReason FailureReason { get; }
    public ManagedResourceConsumerFailureReason ConsumerFailureReason { get; }
    public ManagedHttpFailureReason HttpFailureReason { get; }
    public ManagedHttpsFailureReason HttpsFailureReason { get; }
    public ManagedHttpParseFailureReason ParseFailureReason { get; }
    public int StatusCode { get; }
    public ManagedHttpFramingMode TransferMode { get; }
    public bool HasKnownTotalLength { get; }
    public int TotalEntityLength { get; }
    public int ReceivedBytes { get; }
    public int DeliveredBytes { get; }
    public int ResourceBytesProcessed { get; }
    public int BufferedBytes { get; }
    public int PeakBufferedBytes { get; }
    public int DeliveredSegmentCount { get; }
    public int PauseCount { get; }
    public int ResumeCount { get; }
    public ManagedHttpContentTypeState ContentTypeState { get; }
    public int ContentTypeLength { get; }
    public ManagedHttpContentEncodingState ContentEncodingState { get; }
    public int ContentEncodingLength { get; }
    public int EncodedBytesReceived { get; }
    public int EncodedBytesConsumed { get; }
    public int DecodedBytesProduced { get; }
    public int DecodedBytesConsumed => ResourceBytesProcessed;
    public int BufferedDecodedBytes { get; }
    public int PeakDecodedOutputBytes { get; }
    public ManagedContentDecoderState DecoderState { get; }
    public ManagedContentDecoderFailureReason DecoderFailureReason { get; }
    public int DecoderPauseCount { get; }
    public int DecoderResumeCount { get; }
    public int DecoderHistoryWindowSize { get; }
    public bool CrcValidated { get; }
    public bool IsizeValidated { get; }
    public bool AdlerValidated { get; }
    public bool IsComplete => State == ManagedResourceState.Completed;
    public bool IsCancelled => State == ManagedResourceState.Cancelled;
    public bool IsTerminal => State == ManagedResourceState.Completed ||
                              State == ManagedResourceState.Cancelled ||
                              State == ManagedResourceState.Failed;
}

/* This consumer deliberately accepts a byte array rather than retaining a
   Span.  The array is caller-owned and its fixed range is the only storage
   this consumer may write. */
public sealed class ManagedResourceDestinationConsumer : IManagedResourceConsumer
{
    private readonly byte[] _destination;
    private readonly int _destinationOffset;
    private readonly int _capacity;
    private int _written;
    private ManagedResourceConsumerState _state;
    private ManagedResourceConsumerFailureReason _failureReason;

    public ManagedResourceDestinationConsumer(byte[] destination)
        : this(destination, 0, destination?.Length ?? 0)
    {
    }

    public ManagedResourceDestinationConsumer(byte[] destination, int offset,
                                              int length)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        if (offset < 0 || length < 0 || offset > destination.Length - length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _destination = destination;
        _destinationOffset = offset;
        _capacity = length;
        Reset();
    }

    public ManagedResourceConsumerState State => _state;
    public ManagedResourceConsumerFailureReason FailureReason => _failureReason;
    public int BytesProcessed => _written;
    public int BytesWritten => _written;
    public int Capacity => _capacity;
    public bool IsFull => _written == _capacity;

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed ||
            _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        if (segment.Length > _capacity - _written)
        {
            _failureReason = ManagedResourceConsumerFailureReason.DestinationFull;
            _state = ManagedResourceConsumerState.Failed;
            return ManagedHttpBodySinkResult.Fail;
        }
        segment.CopyTo(_destination.AsSpan(_destinationOffset + _written,
                                           segment.Length));
        _written += segment.Length;
        _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed)
            return false;
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }

    public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;

    public void Reset()
    {
        _written = 0;
        _failureReason = ManagedResourceConsumerFailureReason.None;
        _state = ManagedResourceConsumerState.Idle;
    }
}

public sealed class ManagedResourceCountConsumer : IManagedResourceConsumer
{
    private int _count;
    private ManagedResourceConsumerState _state;
    private ManagedResourceConsumerFailureReason _failureReason;

    public ManagedResourceConsumerState State => _state;
    public ManagedResourceConsumerFailureReason FailureReason => _failureReason;
    public int BytesProcessed => _count;
    public int Count => _count;

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed ||
            _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        if (segment.Length > int.MaxValue - _count)
        {
            _failureReason = ManagedResourceConsumerFailureReason.ConsumerFailure;
            _state = ManagedResourceConsumerState.Failed;
            return ManagedHttpBodySinkResult.Fail;
        }
        _count += segment.Length;
        _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed)
            return false;
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }

    public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;

    public void Reset()
    {
        _count = 0;
        _failureReason = ManagedResourceConsumerFailureReason.None;
        _state = ManagedResourceConsumerState.Idle;
    }
}

public sealed class ManagedResourceSha256Consumer : IManagedResourceConsumer
{
    public const int DigestSize = ManagedSha256.DigestSize;

    private readonly ManagedSha256 _hash = new();
    private readonly byte[] _digest = new byte[DigestSize];
    private int _count;
    private bool _finalized;
    private ManagedResourceConsumerState _state;
    private ManagedResourceConsumerFailureReason _failureReason;

    public ManagedResourceConsumerState State => _state;
    public ManagedResourceConsumerFailureReason FailureReason => _failureReason;
    public int BytesProcessed => _count;
    public bool IsFinalized => _finalized;

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed ||
            _state == ManagedResourceConsumerState.Completed ||
            _finalized)
            return ManagedHttpBodySinkResult.Fail;
        if (segment.Length > int.MaxValue - _count || !_hash.Append(segment))
        {
            _failureReason = ManagedResourceConsumerFailureReason.ConsumerFailure;
            _state = ManagedResourceConsumerState.Failed;
            return ManagedHttpBodySinkResult.Fail;
        }
        _count += segment.Length;
        _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed)
            return false;
        if (!_finalized && !_hash.TryFinalize(_digest))
        {
            _failureReason = ManagedResourceConsumerFailureReason.FinalizationFailure;
            _state = ManagedResourceConsumerState.Failed;
            return false;
        }
        _finalized = true;
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }

    public bool TryCopyDigest(Span<byte> destination)
    {
        if (!_finalized || _state != ManagedResourceConsumerState.Completed ||
            destination.Length < DigestSize)
            return false;
        _digest.AsSpan().CopyTo(destination);
        return true;
    }

    public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;

    public void Reset()
    {
        _hash.Reset();
        _digest.AsSpan().Clear();
        _count = 0;
        _finalized = false;
        _failureReason = ManagedResourceConsumerFailureReason.None;
        _state = ManagedResourceConsumerState.Idle;
    }
}

/* A prefix consumer always continues after its fixed prefix is full.  A
   caller that wants to stop after probing can cancel the resource request at
   that point; returning Pause here would be unsafe because a segment can
   contain both the final prefix byte and later bytes. */
public sealed class ManagedResourcePrefixConsumer : IManagedResourceConsumer
{
    private readonly byte[] _prefix;
    private int _capturedLength;
    private int _processed;
    private ManagedResourceConsumerState _state;

    public ManagedResourcePrefixConsumer(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _prefix = new byte[capacity];
        Reset();
    }

    public ManagedResourcePrefixConsumer(byte[] destination)
    {
        _prefix = destination ?? throw new ArgumentNullException(nameof(destination));
        Reset();
    }

    public ManagedResourceConsumerState State => _state;
    public ManagedResourceConsumerFailureReason FailureReason =>
        ManagedResourceConsumerFailureReason.None;
    public int BytesProcessed => _processed;
    public int Capacity => _prefix.Length;
    public int CapturedLength => _capturedLength;
    public bool IsFull => _capturedLength == _prefix.Length;

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed ||
            _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        int remaining = _prefix.Length - _capturedLength;
        int copyLength = Math.Min(remaining, segment.Length);
        if (copyLength != 0)
        {
            segment[..copyLength].CopyTo(_prefix.AsSpan(_capturedLength));
            _capturedLength += copyLength;
        }
        _processed += segment.Length;
        _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool TryCopyPrefix(Span<byte> destination, out int length)
    {
        length = _capturedLength;
        if (destination.Length < length) return false;
        _prefix.AsSpan(0, length).CopyTo(destination);
        return true;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed)
            return false;
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }

    public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;

    public void Reset()
    {
        _prefix.AsSpan().Clear();
        _capturedLength = 0;
        _processed = 0;
        _state = ManagedResourceConsumerState.Idle;
    }
}

/* Fixed four-way composition.  Components are invoked in constructor order
   with the same parser-owned span; no segment copy or collection is created.
   A Pause is safe and propagated when it comes from the first component.  A
   later component returning Pause cannot be replayed without duplicating the
   already accepted segment in earlier components, so that case is converted
   to a terminal, explicit consumer failure.  Later components never receive
   a segment after an earlier component fails. */
public sealed class ManagedResourceCompositeConsumer : IManagedResourceConsumer
{
    private readonly IManagedResourceConsumer _first;
    private readonly IManagedResourceConsumer _second;
    private readonly IManagedResourceConsumer? _third;
    private readonly IManagedResourceConsumer? _fourth;
    private int _processed;
    private ManagedResourceConsumerState _state;
    private ManagedResourceConsumerFailureReason _failureReason;

    public ManagedResourceCompositeConsumer(IManagedResourceConsumer first,
                                             IManagedResourceConsumer second,
                                             IManagedResourceConsumer? third = null,
                                             IManagedResourceConsumer? fourth = null)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
        _third = third;
        _fourth = fourth;
        Reset();
    }

    public ManagedResourceConsumerState State => _state;
    public ManagedResourceConsumerFailureReason FailureReason => _failureReason;
    public int BytesProcessed => _processed;

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
    {
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed ||
            _state == ManagedResourceConsumerState.Completed)
            return ManagedHttpBodySinkResult.Fail;

        ManagedHttpBodySinkResult result = _first.Consume(segment);
        if (result == ManagedHttpBodySinkResult.Pause)
        {
            _state = ManagedResourceConsumerState.Paused;
            return result;
        }
        if (result == ManagedHttpBodySinkResult.Fail)
            return FailFrom(_first);

        result = _second.Consume(segment);
        if (result == ManagedHttpBodySinkResult.Pause)
            return FailPauseAfterAcceptance();
        if (result == ManagedHttpBodySinkResult.Fail)
            return FailFrom(_second);

        if (_third != null)
        {
            result = _third.Consume(segment);
            if (result == ManagedHttpBodySinkResult.Pause)
                return FailPauseAfterAcceptance();
            if (result == ManagedHttpBodySinkResult.Fail)
                return FailFrom(_third);
        }
        if (_fourth != null)
        {
            result = _fourth.Consume(segment);
            if (result == ManagedHttpBodySinkResult.Pause)
                return FailPauseAfterAcceptance();
            if (result == ManagedHttpBodySinkResult.Fail)
                return FailFrom(_fourth);
        }

        if (segment.Length > int.MaxValue - _processed)
        {
            _failureReason = ManagedResourceConsumerFailureReason.ConsumerFailure;
            _state = ManagedResourceConsumerState.Failed;
            return ManagedHttpBodySinkResult.Fail;
        }
        _processed += segment.Length;
        _state = ManagedResourceConsumerState.Receiving;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed)
            return false;
        if (!_first.Complete() || !_second.Complete() ||
            (_third != null && !_third.Complete()) ||
            (_fourth != null && !_fourth.Complete()))
        {
            _failureReason = ManagedResourceConsumerFailureReason.FinalizationFailure;
            _state = ManagedResourceConsumerState.Failed;
            return false;
        }
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }

    public void Cancel()
    {
        _first.Cancel();
        _second.Cancel();
        _third?.Cancel();
        _fourth?.Cancel();
        _state = ManagedResourceConsumerState.Cancelled;
    }

    public void Reset()
    {
        _first.Reset();
        _second.Reset();
        _third?.Reset();
        _fourth?.Reset();
        _processed = 0;
        _failureReason = ManagedResourceConsumerFailureReason.None;
        _state = ManagedResourceConsumerState.Idle;
    }

    private ManagedHttpBodySinkResult FailFrom(IManagedResourceConsumer component)
    {
        _failureReason = component.FailureReason ==
            ManagedResourceConsumerFailureReason.None
            ? ManagedResourceConsumerFailureReason.ConsumerFailure
            : component.FailureReason;
        _state = ManagedResourceConsumerState.Failed;
        return ManagedHttpBodySinkResult.Fail;
    }

    private ManagedHttpBodySinkResult FailPauseAfterAcceptance()
    {
        _failureReason =
            ManagedResourceConsumerFailureReason.ComponentPauseAfterAcceptance;
        _state = ManagedResourceConsumerState.Failed;
        return ManagedHttpBodySinkResult.Fail;
    }
}

public sealed class ManagedResourceRequest
{
    private static readonly IManagedHttpBodySink RedirectBodySink =
        new RedirectDiscardSink();

    private readonly ManagedResourceProtocol _protocol;
    private readonly ManagedHttpClient? _http;
    private readonly ManagedHttpsClient? _https;
    private readonly int _maximumEntityLength;
    private readonly int _maximumDecodedResourceLength;
    private IManagedResourceConsumer? _consumer;
    private ManagedContentEncodingDecoder? _decoder;
    private byte[]? _encodedDecoderStaging;
    private ManagedContentDecoderFailureReason _decoderFailureReason;
    private ManagedResourceState _state;
    private ManagedResourceFailureReason _failureReason;
    private ManagedResourceConsumerFailureReason _consumerFailureReason;
    private bool _pauseRequested;
    private int _pauseCount;
    private int _resumeCount;
    private int _peakBufferedBytes;
    private int _peakDecodedOutputBytes;

    public ManagedResourceRequest(ManagedNetworkService service,
                                  int maximumEntityLength =
                                      ManagedHttpLimits.MaximumStreamedBodyLength)
        : this(service, maximumEntityLength,
               ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
    }

    public ManagedResourceRequest(ManagedNetworkService service,
                                  int maximumEntityLength,
                                  int maximumDecodedResourceLength)
    {
        if (maximumEntityLength < 0 ||
            maximumEntityLength > ManagedHttpLimits.MaximumStreamedBodyLength)
            throw new ArgumentOutOfRangeException(nameof(maximumEntityLength));
        ValidateDecodedLimit(maximumDecodedResourceLength);
        _protocol = ManagedResourceProtocol.Http;
        _maximumEntityLength = maximumEntityLength;
        _maximumDecodedResourceLength = maximumDecodedResourceLength;
        _http = new ManagedHttpClient(service, maximumEntityLength, false);
        _state = ManagedResourceState.Idle;
    }

    public ManagedResourceRequest(ManagedNetworkService service,
                                  ReadOnlySpan<byte> trustedRoot,
                                  ManagedHttpsValidationTime validationTime,
                                  int maximumEntityLength =
                                      ManagedHttpLimits.MaximumStreamedBodyLength)
        : this(service, trustedRoot, validationTime, maximumEntityLength,
               ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
    }

    public ManagedResourceRequest(ManagedNetworkService service,
                                  ReadOnlySpan<byte> trustedRoot,
                                  ManagedHttpsValidationTime validationTime,
                                  int maximumEntityLength,
                                  int maximumDecodedResourceLength)
    {
        if (maximumEntityLength < 0 ||
            maximumEntityLength > ManagedHttpLimits.MaximumStreamedBodyLength)
            throw new ArgumentOutOfRangeException(nameof(maximumEntityLength));
        ValidateDecodedLimit(maximumDecodedResourceLength);
        _protocol = ManagedResourceProtocol.Https;
        _maximumEntityLength = maximumEntityLength;
        _maximumDecodedResourceLength = maximumDecodedResourceLength;
        _https = new ManagedHttpsClient(service, trustedRoot, validationTime,
                                        maximumEntityLength);
        _state = ManagedResourceState.Idle;
    }

    internal ManagedResourceRequest(ManagedNetworkService service,
                                    ReadOnlySpan<byte> trustedRoot,
                                    in ManagedX509UtcTime validationTime,
                                    ManagedSecureRandom random,
                                    int maximumEntityLength =
                                        ManagedHttpLimits.MaximumStreamedBodyLength,
                                    bool compactTlsProfile = false,
                                    int maximumDecodedResourceLength =
                                        ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
        if (maximumEntityLength < 0 ||
            maximumEntityLength > ManagedHttpLimits.MaximumStreamedBodyLength)
            throw new ArgumentOutOfRangeException(nameof(maximumEntityLength));
        ValidateDecodedLimit(maximumDecodedResourceLength);
        _protocol = ManagedResourceProtocol.Https;
        _maximumEntityLength = maximumEntityLength;
        _maximumDecodedResourceLength = maximumDecodedResourceLength;
        _https = new ManagedHttpsClient(service, trustedRoot, in validationTime,
                                        random, maximumEntityLength,
                                        compactTlsProfile);
        _state = ManagedResourceState.Idle;
    }

    public ManagedResourceProtocol Protocol => _protocol;
    public ManagedResourceState State => _state;
    public ManagedResourceFailureReason FailureReason => _failureReason;
    public ManagedResourceConsumerFailureReason ConsumerFailureReason =>
        _consumerFailureReason;
    public int MaximumEntityLength => _maximumEntityLength;
    public int MaximumDecodedResourceLength => _maximumDecodedResourceLength;
    public ManagedResourceProgressSnapshot Progress => CreateProgress();

    public ManagedHttpContentEncodingState ContentEncodingState =>
        _protocol == ManagedResourceProtocol.Http
            ? _http!.ContentEncodingState : _https!.ContentEncodingState;

    /* These narrow observations are intentionally internal.  They let the
       kernel proof expose transport milestones without making resource
       consumers depend on DNS/TCP/TLS implementation details. */
    internal Ipv4Address ResolvedAddress => _https?.ResolvedAddress ?? default;
    internal NetworkTcpState TcpState => _https?.TcpState ?? NetworkTcpState.Closed;
    internal ManagedTls12HandshakeStage TlsLastHandshake =>
        _https?.TlsLastHandshake ?? ManagedTls12HandshakeStage.ClientHello;
    internal bool TlsAuthenticated => _https?.TlsAuthenticated ?? false;
    internal bool RequestSent => _https?.RequestSent ?? false;
    internal bool ApplicationDataReceived =>
        _https?.ApplicationDataReceived ?? false;

    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ReadOnlySpan<byte> path,
                                           IManagedResourceConsumer consumer)
    {
        if (!CanBegin()) return NetworkOperationResult.Busy;
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        consumer.Reset();
        ClearRequestState(consumer);
        ClearDecoderState();
        NetworkOperationResult result = _protocol == ManagedResourceProtocol.Http
            ? _http!.BeginGet(hostname, path)
            : _https!.BeginGet(hostname, path);
        if (result != NetworkOperationResult.Started)
        {
            _consumer = consumer;
            CaptureUnderlyingFailure();
            return result;
        }
        _consumer = consumer;
        _state = ManagedResourceState.Receiving;
        return result;
    }

    public NetworkOperationResult BeginGetUrl(ReadOnlySpan<byte> url,
                                              IManagedResourceConsumer consumer)
    {
        if (_protocol != ManagedResourceProtocol.Https)
            return NetworkOperationResult.InvalidArgument;
        if (!CanBegin()) return NetworkOperationResult.Busy;
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        consumer.Reset();
        ClearRequestState(consumer);
        ClearDecoderState();
        NetworkOperationResult result = _https!.BeginGetUrl(url);
        if (result != NetworkOperationResult.Started)
        {
            _consumer = consumer;
            CaptureUnderlyingFailure();
            return result;
        }
        _consumer = consumer;
        _state = ManagedResourceState.Receiving;
        return result;
    }

    public NetworkOperationResult Pause()
    {
        if (_state == ManagedResourceState.Paused) return NetworkOperationResult.Success;
        if (_state != ManagedResourceState.Receiving)
            return _state == ManagedResourceState.Idle
                ? NetworkOperationResult.InvalidArgument
                : NetworkOperationResult.Success;
        _pauseRequested = true;
        _state = ManagedResourceState.Paused;
        _pauseCount++;
        return NetworkOperationResult.Success;
    }

    public NetworkOperationResult Resume()
    {
        if (_state != ManagedResourceState.Paused)
            return _state == ManagedResourceState.Idle
                ? NetworkOperationResult.InvalidArgument
                : NetworkOperationResult.Success;
        _pauseRequested = false;
        _state = ManagedResourceState.Receiving;
        _resumeCount++;
        return NetworkOperationResult.Success;
    }

    public NetworkOperationResult Poll()
    {
        if (_state == ManagedResourceState.Completed ||
            _state == ManagedResourceState.Cancelled)
            return NetworkOperationResult.Success;
        if (_state == ManagedResourceState.Failed)
            return NetworkOperationResult.Failed;
        if (_pauseRequested) return NetworkOperationResult.Success;

        if (_decoder != null)
        {
            if (!DriveDecoded()) return NetworkOperationResult.Failed;
            if (_state == ManagedResourceState.Paused ||
                _state == ManagedResourceState.Failed)
                return _state == ManagedResourceState.Paused
                    ? NetworkOperationResult.Success
                    : NetworkOperationResult.Failed;
        }
        else if (HasPendingBody())
        {
            RecordBufferedBodyPeak();
            if (!DeliverPendingBody()) return NetworkOperationResult.Failed;
            if (_state == ManagedResourceState.Paused ||
                _state == ManagedResourceState.Failed)
                return _state == ManagedResourceState.Paused
                    ? NetworkOperationResult.Success
                    : NetworkOperationResult.Failed;
            return NetworkOperationResult.Success;
        }

        NetworkOperationResult result = _protocol == ManagedResourceProtocol.Http
            ? _http!.Poll() : _https!.Poll();
        if (result == NetworkOperationResult.Failed || UnderlyingFailed())
        {
            CaptureUnderlyingFailure();
            return NetworkOperationResult.Failed;
        }

        if (!EnsureDecoder()) return NetworkOperationResult.Failed;
        if (_decoder != null)
        {
            if (!DriveDecoded()) return NetworkOperationResult.Failed;
            if (_state == ManagedResourceState.Paused ||
                _state == ManagedResourceState.Failed)
                return _state == ManagedResourceState.Paused
                    ? NetworkOperationResult.Success
                    : NetworkOperationResult.Failed;
        }
        else if (HasPendingBody())
        {
            RecordBufferedBodyPeak();
            if (!DeliverPendingBody()) return NetworkOperationResult.Failed;
            if (_state == ManagedResourceState.Paused ||
                _state == ManagedResourceState.Failed)
                return _state == ManagedResourceState.Paused
                    ? NetworkOperationResult.Success
                    : NetworkOperationResult.Failed;
        }

        if (UnderlyingSucceeded() &&
            (_decoder == null || _decoder.IsComplete) && !HasPendingBody())
        {
            if (_consumer == null || !_consumer.Complete())
            {
                _consumerFailureReason = _consumer?.FailureReason ??
                    ManagedResourceConsumerFailureReason.FinalizationFailure;
                _failureReason = _consumerFailureReason ==
                    ManagedResourceConsumerFailureReason.DestinationFull
                    ? ManagedResourceFailureReason.DestinationFull
                    : ManagedResourceFailureReason.ConsumerFailure;
                _state = ManagedResourceState.Failed;
                return NetworkOperationResult.Failed;
            }
            _state = ManagedResourceState.Completed;
        }
        return result;
    }

    public NetworkOperationResult Cancel()
    {
        if (_state == ManagedResourceState.Completed ||
            _state == ManagedResourceState.Cancelled ||
            _state == ManagedResourceState.Failed)
            return NetworkOperationResult.Success;
        NetworkOperationResult result = _protocol == ManagedResourceProtocol.Http
            ? _http!.Cancel() : _https!.Cancel();
        _decoder?.Cancel();
        _consumer?.Cancel();
        _pauseRequested = false;
        _state = ManagedResourceState.Cancelled;
        _failureReason = ManagedResourceFailureReason.Cancelled;
        return result == NetworkOperationResult.Failed
            ? NetworkOperationResult.Failed : NetworkOperationResult.Success;
    }

    public NetworkOperationResult Reset()
    {
        if (_state == ManagedResourceState.Receiving ||
            _state == ManagedResourceState.Paused)
            return NetworkOperationResult.Busy;
        NetworkOperationResult result = _protocol == ManagedResourceProtocol.Http
            ? _http!.Reset() : _https!.Reset();
        if (result != NetworkOperationResult.Success) return result;
        _consumer?.Reset();
        ClearDecoderState();
        _consumer = null;
        _pauseRequested = false;
        _pauseCount = 0;
        _resumeCount = 0;
        _peakBufferedBytes = 0;
        _peakDecodedOutputBytes = 0;
        _failureReason = ManagedResourceFailureReason.None;
        _consumerFailureReason = ManagedResourceConsumerFailureReason.None;
        _decoderFailureReason = ManagedContentDecoderFailureReason.None;
        _state = ManagedResourceState.Idle;
        return NetworkOperationResult.Success;
    }

    public bool TryCopyContentType(Span<byte> destination, out int length)
    {
        return _protocol == ManagedResourceProtocol.Http
            ? _http!.TryCopyResponseContentType(destination, out length)
            : _https!.TryCopyResponseContentType(destination, out length);
    }

    public bool TryCopyContentEncoding(Span<byte> destination, out int length)
    {
        return _protocol == ManagedResourceProtocol.Http
            ? _http!.TryCopyResponseContentEncoding(destination, out length)
            : _https!.TryCopyResponseContentEncoding(destination, out length);
    }

    private bool CanBegin() => _state == ManagedResourceState.Idle ||
                               _state == ManagedResourceState.Completed ||
                               _state == ManagedResourceState.Cancelled ||
                               _state == ManagedResourceState.Failed;

    private void ClearRequestState(IManagedResourceConsumer consumer)
    {
        _consumer = consumer;
        _state = ManagedResourceState.Idle;
        _failureReason = ManagedResourceFailureReason.None;
        _consumerFailureReason = ManagedResourceConsumerFailureReason.None;
        _pauseRequested = false;
        _pauseCount = 0;
        _resumeCount = 0;
        _peakBufferedBytes = 0;
        _peakDecodedOutputBytes = 0;
    }

    private bool HasPendingBody() => _protocol == ManagedResourceProtocol.Http
            ? _http!.BufferedResponseBodyLength != 0
            : _https!.BufferedResponseBodyLength != 0;

    private static void ValidateDecodedLimit(int value)
    {
        if (value < 0 || value > ManagedContentEncodingLimits.MaximumDecodedResourceLength)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private void ClearDecoderState()
    {
        _decoder?.Reset();
        _decoder = null;
        _encodedDecoderStaging = null;
        _decoderFailureReason = ManagedContentDecoderFailureReason.None;
    }

    private void RecordBufferedBodyPeak()
    {
        int buffered = _protocol == ManagedResourceProtocol.Http
            ? _http!.BufferedResponseBodyLength
            : _https!.BufferedResponseBodyLength;
        if (buffered > _peakBufferedBytes) _peakBufferedBytes = buffered;
    }

    private bool IsRedirectBody()
    {
        if (_https == null || !_https.StatusParsed ||
            _https.State == ManagedHttpsClientState.Succeeded)
            return false;
        int status = _https.StatusCode;
        return status == 301 || status == 302 || status == 303 ||
               status == 307 || status == 308;
    }

    private bool EnsureDecoder()
    {
        if (_decoder != null || _consumer == null) return true;
        bool statusParsed = _protocol == ManagedResourceProtocol.Http
            ? _http!.StatusParsed : _https!.StatusParsed;
        if (!statusParsed || IsRedirectBody()) return true;
        ManagedHttpContentEncodingState encoding = _protocol == ManagedResourceProtocol.Http
            ? _http!.ContentEncodingState : _https!.ContentEncodingState;
        switch (encoding)
        {
            case ManagedHttpContentEncodingState.Missing:
            case ManagedHttpContentEncodingState.Identity:
                return true;
            case ManagedHttpContentEncodingState.Gzip:
            case ManagedHttpContentEncodingState.Deflate:
                _decoder = new ManagedContentEncodingDecoder(
                    encoding, _maximumDecodedResourceLength);
                _encodedDecoderStaging = new byte[ManagedContentEncodingLimits.InputStagingSize];
                return true;
            case ManagedHttpContentEncodingState.Unsupported:
                return FailDecoder(ManagedResourceFailureReason.UnsupportedContentEncoding,
                    ManagedContentDecoderFailureReason.UnsupportedEncoding);
            case ManagedHttpContentEncodingState.TooLong:
                return FailDecoder(ManagedResourceFailureReason.ContentEncodingHeaderTooLong,
                    ManagedContentDecoderFailureReason.UnsupportedEncoding);
            default:
                return FailDecoder(ManagedResourceFailureReason.MalformedContentEncoding,
                    ManagedContentDecoderFailureReason.MalformedDeflateStream);
        }
    }

    private bool DriveDecoded()
    {
        if (_decoder == null || _consumer == null) return true;
        while (true)
        {
            if (_decoder.OutputLength != 0)
            {
                ManagedHttpBodyDeliveryResult delivered =
                    _decoder.ConsumeOutput(_consumer);
                if (delivered == ManagedHttpBodyDeliveryResult.Paused)
                {
                    _pauseRequested = true;
                    _state = ManagedResourceState.Paused;
                    _pauseCount++;
                    return true;
                }
                if (delivered == ManagedHttpBodyDeliveryResult.Failed)
                {
                    _consumerFailureReason = _consumer.FailureReason ==
                        ManagedResourceConsumerFailureReason.None
                        ? ManagedResourceConsumerFailureReason.ConsumerFailure
                        : _consumer.FailureReason;
                    _failureReason = _consumerFailureReason ==
                        ManagedResourceConsumerFailureReason.DestinationFull
                        ? ManagedResourceFailureReason.DestinationFull
                        : ManagedResourceFailureReason.ConsumerFailure;
                    _state = ManagedResourceState.Failed;
                    return false;
                }
                if (delivered == ManagedHttpBodyDeliveryResult.Cancelled)
                {
                    _failureReason = ManagedResourceFailureReason.Cancelled;
                    _state = ManagedResourceState.Cancelled;
                    return false;
                }
                _state = ManagedResourceState.Receiving;
                continue;
            }

            bool bodyComplete = TransportBodyComplete() && !HasPendingBody();
            ManagedContentDecoderProcessResult result = _decoder.Pump(bodyComplete);
            if (result == ManagedContentDecoderProcessResult.Failed)
                return FailDecoder(_decoder.FailureReason);
            if (result == ManagedContentDecoderProcessResult.Cancelled)
            {
                _failureReason = ManagedResourceFailureReason.Cancelled;
                _state = ManagedResourceState.Cancelled;
                return false;
            }
            if (result == ManagedContentDecoderProcessResult.OutputAvailable)
            {
                if (_decoder.OutputLength > _peakDecodedOutputBytes)
                    _peakDecodedOutputBytes = _decoder.OutputLength;
                continue;
            }
            if (result == ManagedContentDecoderProcessResult.Complete)
                return true;
            if (result != ManagedContentDecoderProcessResult.NeedInput)
                return true;

            if (!HasPendingBody()) return true;
            if (_encodedDecoderStaging == null || _decoder.InputFreeCapacity == 0)
                return true;
            int readCapacity = Math.Min(_encodedDecoderStaging.Length,
                                        _decoder.InputFreeCapacity);
            bool read = _protocol == ManagedResourceProtocol.Http
                ? _http!.TryReadResponseBodyChunk(
                    _encodedDecoderStaging.AsSpan(0, readCapacity), out int length)
                : _https!.TryReadResponseBodyChunk(
                    _encodedDecoderStaging.AsSpan(0, readCapacity), out length);
            if (!read || length == 0) return true;
            if (!_decoder.AppendInput(_encodedDecoderStaging.AsSpan(0, length)))
                return FailDecoder(_decoder.FailureReason);
        }
    }

    private bool TransportBodyComplete() => _protocol == ManagedResourceProtocol.Http
        ? _http!.ResponseBodyComplete : _https!.ResponseBodyComplete;

    private bool FailDecoder(ManagedContentDecoderFailureReason reason)
    {
        _decoderFailureReason = reason;
        ManagedResourceFailureReason resourceReason = reason switch
        {
            ManagedContentDecoderFailureReason.MalformedGzipHeader =>
                ManagedResourceFailureReason.MalformedGzipHeader,
            ManagedContentDecoderFailureReason.MalformedZlibHeader =>
                ManagedResourceFailureReason.MalformedZlibHeader,
            ManagedContentDecoderFailureReason.MalformedDeflateStream =>
                ManagedResourceFailureReason.MalformedDeflateStream,
            ManagedContentDecoderFailureReason.GzipCrcMismatch =>
                ManagedResourceFailureReason.GzipCrcMismatch,
            ManagedContentDecoderFailureReason.GzipIsizeMismatch =>
                ManagedResourceFailureReason.GzipIsizeMismatch,
            ManagedContentDecoderFailureReason.ZlibAdlerMismatch =>
                ManagedResourceFailureReason.ZlibAdlerMismatch,
            ManagedContentDecoderFailureReason.TruncatedCompressedStream =>
                ManagedResourceFailureReason.TruncatedCompressedStream,
            ManagedContentDecoderFailureReason.DecodedResourceLimitExceeded =>
                ManagedResourceFailureReason.DecodedResourceTooLarge,
            ManagedContentDecoderFailureReason.TrailingCompressedData =>
                ManagedResourceFailureReason.TrailingCompressedData,
            ManagedContentDecoderFailureReason.GzipOptionalFieldTooLong =>
                ManagedResourceFailureReason.MalformedGzipHeader,
            ManagedContentDecoderFailureReason.GzipHeaderCrcMismatch =>
                ManagedResourceFailureReason.MalformedGzipHeader,
            ManagedContentDecoderFailureReason.DownstreamConsumerFailure =>
                ManagedResourceFailureReason.ConsumerFailure,
            _ => ManagedResourceFailureReason.MalformedDeflateStream
        };
        return FailDecoder(resourceReason, reason);
    }

    private bool FailDecoder(ManagedResourceFailureReason reason,
                             ManagedContentDecoderFailureReason decoderReason)
    {
        _failureReason = reason;
        _decoderFailureReason = decoderReason;
        _state = ManagedResourceState.Failed;
        return false;
    }

    private bool DeliverPendingBody()
    {
        ManagedHttpBodyDeliveryResult result;
        if (IsRedirectBody())
        {
            result = _https!.ConsumeResponseBody(RedirectBodySink);
        }
        else
        {
            if (_consumer == null)
            {
                _failureReason = ManagedResourceFailureReason.ConsumerFailure;
                _consumerFailureReason =
                    ManagedResourceConsumerFailureReason.ConsumerFailure;
                _state = ManagedResourceState.Failed;
                return false;
            }
            result = _protocol == ManagedResourceProtocol.Http
                ? _http!.ConsumeResponseBody(_consumer)
                : _https!.ConsumeResponseBody(_consumer);
        }

        if (result == ManagedHttpBodyDeliveryResult.Delivered)
        {
            _state = ManagedResourceState.Receiving;
            return true;
        }
        if (result == ManagedHttpBodyDeliveryResult.Paused)
        {
            _state = ManagedResourceState.Paused;
            _pauseCount++;
            return true;
        }
        if (result == ManagedHttpBodyDeliveryResult.Cancelled)
        {
            _failureReason = ManagedResourceFailureReason.Cancelled;
            _state = ManagedResourceState.Cancelled;
            return false;
        }
        if (result == ManagedHttpBodyDeliveryResult.Failed)
        {
            _consumerFailureReason = _consumer?.FailureReason ??
                ManagedResourceConsumerFailureReason.ConsumerFailure;
            _failureReason = _consumerFailureReason ==
                ManagedResourceConsumerFailureReason.DestinationFull
                ? ManagedResourceFailureReason.DestinationFull
                : ManagedResourceFailureReason.ConsumerFailure;
            _state = ManagedResourceState.Failed;
            return false;
        }
        return true;
    }

    private bool UnderlyingSucceeded() => _protocol == ManagedResourceProtocol.Http
        ? _http!.State == ManagedHttpClientState.Succeeded
        : _https!.State == ManagedHttpsClientState.Succeeded;

    private bool UnderlyingFailed() => _protocol == ManagedResourceProtocol.Http
        ? _http!.State == ManagedHttpClientState.Failed
        : _https!.State == ManagedHttpsClientState.Failed;

    private void CaptureUnderlyingFailure()
    {
        if (_protocol == ManagedResourceProtocol.Http)
        {
            if (_http!.FailureReason == ManagedHttpFailureReason.SinkFailure)
                return;
            _failureReason = _http.FailureReason == ManagedHttpFailureReason.Cancelled
                ? ManagedResourceFailureReason.Cancelled
                : _http.ParseFailureReason == ManagedHttpParseFailureReason.BodyTooLarge
                    ? ManagedResourceFailureReason.BodyTooLarge
                    : _http.FailureReason == ManagedHttpFailureReason.PrematureConnectionClose
                        ? ManagedResourceFailureReason.PrematureConnectionClose
                        : _http.FailureReason == ManagedHttpFailureReason.TransportFailure ||
                          _http.FailureReason == ManagedHttpFailureReason.DnsFailure ||
                          _http.FailureReason == ManagedHttpFailureReason.TcpConnectFailure ||
                          _http.FailureReason == ManagedHttpFailureReason.TcpReset
                            ? ManagedResourceFailureReason.TransportFailure
                            : _http.FailureReason == ManagedHttpFailureReason.TeardownFailure
                                ? ManagedResourceFailureReason.TeardownFailure
                                : ManagedResourceFailureReason.HttpParserFailure;
        }
        else
        {
            if (_https!.FailureReason == ManagedHttpsFailureReason.SinkFailure)
                return;
            _failureReason = _https.FailureReason == ManagedHttpsFailureReason.Cancelled
                ? ManagedResourceFailureReason.Cancelled
                : _https.ParseFailureReason == ManagedHttpParseFailureReason.BodyTooLarge
                    ? ManagedResourceFailureReason.BodyTooLarge
                    : _https.FailureReason == ManagedHttpsFailureReason.PrematureConnectionClose
                        ? ManagedResourceFailureReason.PrematureConnectionClose
                        : _https.FailureReason == ManagedHttpsFailureReason.TlsAuthenticationFailure ||
                          _https.FailureReason == ManagedHttpsFailureReason.TlsProtocolFailure
                            ? ManagedResourceFailureReason.TlsFailure
                            : _https.FailureReason == ManagedHttpsFailureReason.TransportFailure ||
                              _https.FailureReason == ManagedHttpsFailureReason.DnsFailure ||
                              _https.FailureReason == ManagedHttpsFailureReason.TcpConnectFailure ||
                              _https.FailureReason == ManagedHttpsFailureReason.TcpReset
                                ? ManagedResourceFailureReason.TransportFailure
                                : _https.FailureReason == ManagedHttpsFailureReason.TeardownFailure
                                    ? ManagedResourceFailureReason.TeardownFailure
                                    : ManagedResourceFailureReason.HttpParserFailure;
        }
        _state = _failureReason == ManagedResourceFailureReason.Cancelled
            ? ManagedResourceState.Cancelled : ManagedResourceState.Failed;
    }

    private ManagedResourceProgressSnapshot CreateProgress()
    {
        ManagedHttpProgressSnapshot transport = _protocol == ManagedResourceProtocol.Http
            ? _http!.Progress : _https!.Progress;
        ManagedHttpFailureReason httpFailure = _http?.FailureReason ??
            ManagedHttpFailureReason.None;
        ManagedHttpsFailureReason httpsFailure = _https?.FailureReason ??
            ManagedHttpsFailureReason.None;
        ManagedResourceConsumerState consumerState = _consumer == null
            ? ManagedResourceConsumerState.Idle : _consumer.State;
        if (_state == ManagedResourceState.Paused)
            consumerState = ManagedResourceConsumerState.Paused;
        if (_state == ManagedResourceState.Completed)
            consumerState = ManagedResourceConsumerState.Completed;
        if (_state == ManagedResourceState.Cancelled)
            consumerState = ManagedResourceConsumerState.Cancelled;
        if (_state == ManagedResourceState.Failed)
            consumerState = ManagedResourceConsumerState.Failed;
        ManagedHttpContentEncodingState contentEncodingState =
            _protocol == ManagedResourceProtocol.Http
                ? _http!.ContentEncodingState : _https!.ContentEncodingState;
        int contentEncodingLength = _protocol == ManagedResourceProtocol.Http
            ? _http!.ContentEncodingLength : _https!.ContentEncodingLength;
        int encodedBytesReceived = transport.DecodedBodyBytesReceived;
        int encodedBytesConsumed = transport.DecodedBodyBytesDelivered;
        int decodedBytesProduced = _decoder?.DecodedBytesProduced ??
            (_consumer?.BytesProcessed ?? 0);
        int bufferedDecodedBytes = _decoder?.BufferedOutputLength ?? 0;
        ManagedContentDecoderState decoderState = _decoder?.State ??
            ManagedContentDecoderState.Idle;
        ManagedContentDecoderFailureReason decoderFailureReason =
            _decoder?.FailureReason ?? _decoderFailureReason;
        return new ManagedResourceProgressSnapshot(
            _state, _protocol, consumerState, _failureReason,
            _consumerFailureReason, httpFailure, httpsFailure,
            transport.ParseFailureReason, transport.StatusCode,
            transport.TransferMode, transport.HasKnownTotalLength,
            transport.TotalEntityLength, transport.DecodedBodyBytesReceived,
            transport.DecodedBodyBytesDelivered,
            _consumer?.BytesProcessed ?? 0, transport.BufferedBodyBytes,
            _peakBufferedBytes,
            transport.DeliveredSegmentCount, _pauseCount, _resumeCount,
            _protocol == ManagedResourceProtocol.Http
                ? _http!.ContentTypeState : _https!.ContentTypeState,
            _protocol == ManagedResourceProtocol.Http
                ? _http!.ContentTypeLength : _https!.ContentTypeLength,
            contentEncodingState, contentEncodingLength,
            encodedBytesReceived, encodedBytesConsumed,
            decodedBytesProduced, bufferedDecodedBytes,
            _peakDecodedOutputBytes,
            decoderState, decoderFailureReason,
            _decoder?.PauseCount ?? 0, _decoder?.ResumeCount ?? 0,
            _decoder?.HistoryWindowSize ?? 0,
            _decoder?.CrcValidated ?? false,
            _decoder?.IsizeValidated ?? false,
            _decoder?.AdlerValidated ?? false);
    }

    private sealed class RedirectDiscardSink : IManagedHttpBodySink
    {
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment) =>
            ManagedHttpBodySinkResult.Continue;
    }
}

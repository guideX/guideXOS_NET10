using System;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedHtmlFailureReason : byte
{
    None = 0,
    Cancelled = 1,
    UnsupportedMime = 2,
    ContentTypeFailure = 3,
    UnsupportedCharset = 4,
    TextDecodingFailure = 5,
    HtmlTokenizerFailure = 6,
    TokenConsumerFailure = 7,
    ContentEncodingFailure = 8,
    DecodedResourceLimit = 9,
    HttpParserFailure = 10,
    TransportFailure = 11,
    TlsFailure = 12,
    TeardownFailure = 13,
    RequestFailure = 14,
    NodeCapacityExceeded = 15,
    TextCapacityExceeded = 16,
    AttributeCapacityExceeded = 17,
    AttributeValueCapacityExceeded = 18,
    AttributeNameCapacityExceeded = 19,
    TagNameCapacityExceeded = 20,
    TreeDepthExceeded = 21,
    InvalidTreeState = 22,
    UnsupportedInsertionModeCase = 23
}

public readonly struct ManagedHtmlProgressSnapshot
{
    internal ManagedHtmlProgressSnapshot(
        ManagedResourceState state, ManagedHtmlFailureReason failureReason,
        ManagedTextFailureReason textFailureReason,
        ManagedTextProgressSnapshot text,
        ManagedHtmlProgressSnapshotData tokenizer)
    {
        State = state;
        FailureReason = failureReason;
        TextFailureReason = textFailureReason;
        Text = text;
        StatusCode = text.StatusCode;
        MimeClassification = text.MimeClassification;
        ContentTypeState = text.ContentTypeState;
        Charset = text.Charset;
        ContentEncodingState = text.ContentEncodingState;
        EncodedBytesReceived = text.EncodedHttpBytesReceived;
        DecompressedBytesProduced = text.DecompressedResourceBytesProduced;
        TextInputBytesConsumed = text.TextInputBytesConsumed;
        ScalarsProduced = text.ScalarsProduced;
        ScalarsDelivered = text.ScalarsDelivered;
        PeakHttpBuffer = text.PeakHttpBuffer;
        PeakDecompressionBuffer = text.PeakDecompressionBuffer;
        PeakTextBuffer = text.PeakTextBuffer;
        TokenizerState = tokenizer.State;
        TokenizerFailureReason = tokenizer.FailureReason;
        ScalarsReceived = tokenizer.ScalarsReceived;
        ScalarsConsumed = tokenizer.ScalarsConsumed;
        TokensEmitted = tokenizer.TokensEmitted;
        TextTokens = tokenizer.TextTokens;
        StartTagTokens = tokenizer.StartTagTokens;
        EndTagTokens = tokenizer.EndTagTokens;
        CommentTokens = tokenizer.CommentTokens;
        DoctypeTokens = tokenizer.DoctypeTokens;
        AttributesEmitted = tokenizer.AttributesEmitted;
        CharacterReferencesDecoded = tokenizer.CharacterReferencesDecoded;
        BufferedTextScalars = tokenizer.BufferedTextScalars;
        CurrentTagNameLength = tokenizer.CurrentTagNameLength;
        CurrentAttributeCount = tokenizer.CurrentAttributeCount;
        /* The public HTML resource pause boundary is the Phase 41 text
           resource boundary.  Tokenizer callback pauses are still exposed by
           the tokenizer progress snapshot and exercised by host tests, but
           this aggregate reports the resource contract seen by callers. */
        PauseCount = text.PauseCount;
        ResumeCount = text.ResumeCount;
        PeakTokenizerTextScalars = tokenizer.PeakTextScalars;
        TreeBuilderState = tokenizer.TreeBuilder?.State ?? ManagedHtmlTreeBuilderState.Idle;
        TreeBuilderFailureReason = tokenizer.TreeBuilder?.FailureReason ??
            ManagedHtmlTreeBuilderFailureReason.None;
    }

    public ManagedResourceState State { get; }
    public ManagedHtmlFailureReason FailureReason { get; }
    public ManagedTextFailureReason TextFailureReason { get; }
    public ManagedTextProgressSnapshot Text { get; }
    public int StatusCode { get; }
    public ManagedMimeClassification MimeClassification { get; }
    public ManagedHttpContentTypeState ContentTypeState { get; }
    public ManagedTextCharset Charset { get; }
    public ManagedHttpContentEncodingState ContentEncodingState { get; }
    public int EncodedBytesReceived { get; }
    public int DecompressedBytesProduced { get; }
    public int TextInputBytesConsumed { get; }
    public int ScalarsProduced { get; }
    public int ScalarsDelivered { get; }
    public int PeakHttpBuffer { get; }
    public int PeakDecompressionBuffer { get; }
    public int PeakTextBuffer { get; }
    public ManagedHtmlTokenizerState TokenizerState { get; }
    public ManagedHtmlTokenizerFailureReason TokenizerFailureReason { get; }
    public int ScalarsReceived { get; }
    public int ScalarsConsumed { get; }
    public int TokensEmitted { get; }
    public int TextTokens { get; }
    public int StartTagTokens { get; }
    public int EndTagTokens { get; }
    public int CommentTokens { get; }
    public int DoctypeTokens { get; }
    public int AttributesEmitted { get; }
    public int CharacterReferencesDecoded { get; }
    public int BufferedTextScalars { get; }
    public int CurrentTagNameLength { get; }
    public int CurrentAttributeCount { get; }
    public int PauseCount { get; }
    public int ResumeCount { get; }
    public int PeakTokenizerTextScalars { get; }
    public ManagedHtmlTreeBuilderState TreeBuilderState { get; }
    public ManagedHtmlTreeBuilderFailureReason TreeBuilderFailureReason { get; }
    public int DecodedScalarsProduced => ScalarsProduced;
    public bool IsComplete => State == ManagedResourceState.Completed;
    public bool IsCancelled => State == ManagedResourceState.Cancelled;
    public bool IsTerminal => State == ManagedResourceState.Completed ||
                              State == ManagedResourceState.Cancelled ||
                              State == ManagedResourceState.Failed;
}

internal readonly struct ManagedHtmlProgressSnapshotData
{
    internal ManagedHtmlProgressSnapshotData(ManagedHtmlTokenizerProgressSnapshot source)
    {
        State = source.State;
        FailureReason = source.FailureReason;
        ScalarsReceived = source.ScalarsReceived;
        ScalarsConsumed = source.ScalarsConsumed;
        TokensEmitted = source.TokensEmitted;
        TextTokens = source.TextTokens;
        StartTagTokens = source.StartTagTokens;
        EndTagTokens = source.EndTagTokens;
        CommentTokens = source.CommentTokens;
        DoctypeTokens = source.DoctypeTokens;
        AttributesEmitted = source.AttributesEmitted;
        CharacterReferencesDecoded = source.CharacterReferencesDecoded;
        BufferedTextScalars = source.BufferedTextScalars;
        CurrentTagNameLength = source.CurrentTagNameLength;
        CurrentAttributeCount = source.CurrentAttributeCount;
        PauseCount = source.PauseCount;
        ResumeCount = source.ResumeCount;
        PeakTextScalars = source.PeakTextScalars;
        TreeBuilder = null;
    }

    internal ManagedHtmlProgressSnapshotData(ManagedHtmlTokenizerProgressSnapshot source,
                                             ManagedHtmlTreeBuilder? treeBuilder)
        : this(source)
    {
        TreeBuilder = treeBuilder;
    }

    internal readonly ManagedHtmlTokenizerState State;
    internal readonly ManagedHtmlTokenizerFailureReason FailureReason;
    internal readonly int ScalarsReceived;
    internal readonly int ScalarsConsumed;
    internal readonly int TokensEmitted;
    internal readonly int TextTokens;
    internal readonly int StartTagTokens;
    internal readonly int EndTagTokens;
    internal readonly int CommentTokens;
    internal readonly int DoctypeTokens;
    internal readonly int AttributesEmitted;
    internal readonly int CharacterReferencesDecoded;
    internal readonly int BufferedTextScalars;
    internal readonly int CurrentTagNameLength;
    internal readonly int CurrentAttributeCount;
    internal readonly int PauseCount;
    internal readonly int ResumeCount;
    internal readonly int PeakTextScalars;
    internal readonly ManagedHtmlTreeBuilder? TreeBuilder;
}

/* HTML is deliberately a wrapper above Phase 41.  The response body first
   passes the existing bounded resource, compression, MIME, and Unicode
   decoder gates.  This wrapper only permits text/html and feeds its scalar
   windows into the tokenizer. */
public sealed class ManagedHtmlResourceRequest
{
    private static ManagedHtmlTokenizer? s_nativeKernelTokenizer;
    private readonly ManagedTextResourceRequest _text;
    private readonly ManagedHtmlScalarAdapter _scalarAdapter;
    private IManagedHtmlTokenConsumer? _consumer;
    private ManagedHtmlTokenizer? _tokenizer;
    private ManagedResourceState _state;
    private ManagedHtmlFailureReason _failureReason;
    private bool _metadataChecked;

    public ManagedHtmlResourceRequest(ManagedNetworkService service,
                                      int maximumEntityLength = ManagedHttpLimits.MaximumStreamedBodyLength,
                                      int maximumDecodedResourceLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
        _text = new ManagedTextResourceRequest(service, maximumEntityLength,
                                                maximumDecodedResourceLength);
        _scalarAdapter = new(this);
        _state = ManagedResourceState.Idle;
    }

    public ManagedHtmlResourceRequest(ManagedNetworkService service,
                                      ReadOnlySpan<byte> trustedRoot,
                                      ManagedHttpsValidationTime validationTime,
                                      int maximumEntityLength = ManagedHttpLimits.MaximumStreamedBodyLength,
                                      int maximumDecodedResourceLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
        _text = new ManagedTextResourceRequest(service, trustedRoot, validationTime,
                                                maximumEntityLength,
                                                maximumDecodedResourceLength);
        _scalarAdapter = new(this);
        _state = ManagedResourceState.Idle;
    }

    internal ManagedHtmlResourceRequest(ManagedNetworkService service,
                                        ReadOnlySpan<byte> trustedRoot,
                                        in ManagedX509UtcTime validationTime,
                                        ManagedSecureRandom random,
                                        int maximumEntityLength,
                                        bool compactTlsProfile,
                                        int maximumDecodedResourceLength)
    {
        _text = new ManagedTextResourceRequest(service, trustedRoot,
                                                in validationTime, random,
                                                maximumEntityLength,
                                                compactTlsProfile,
                                                maximumDecodedResourceLength);
        _scalarAdapter = new(this);
        _tokenizer = TakeNativeKernelTokenizer();
        _state = ManagedResourceState.Idle;
    }

    internal static bool PrimeNativeKernelTokenizer()
    {
        if (s_nativeKernelTokenizer != null) return true;
        s_nativeKernelTokenizer = new ManagedHtmlTokenizer();
        return true;
    }

    private static ManagedHtmlTokenizer? TakeNativeKernelTokenizer()
    {
        ManagedHtmlTokenizer? tokenizer = s_nativeKernelTokenizer;
        s_nativeKernelTokenizer = null;
        return tokenizer;
    }

    public ManagedResourceState State => _state;
    public ManagedHtmlFailureReason FailureReason => _failureReason;
    public ManagedTextFailureReason TextFailureReason => _text.FailureReason;
    public ManagedHtmlProgressSnapshot Progress => CreateProgress();
    public ManagedHtmlTokenizer Tokenizer => _tokenizer!;
    public ManagedHtmlTreeBuilder? TreeBuilder => _consumer as ManagedHtmlTreeBuilder;

    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ReadOnlySpan<byte> path,
                                           IManagedHtmlTokenConsumer consumer)
    {
        if (!CanBegin()) return NetworkOperationResult.Busy;
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        _tokenizer ??= new ManagedHtmlTokenizer();
        _tokenizer.Reset();
        consumer.Reset();
        _consumer = consumer;
        _scalarAdapter.Reset();
        _failureReason = ManagedHtmlFailureReason.None;
        _metadataChecked = false;
        NetworkOperationResult result = _text.BeginGet(hostname, path,
                                                        _scalarAdapter);
        if (result != NetworkOperationResult.Started)
        {
            MapTextFailure();
            return result;
        }
        _scalarAdapter.Configure(_tokenizer, consumer);
        _state = ManagedResourceState.Receiving;
        return result;
    }

    public NetworkOperationResult BeginGetUrl(ReadOnlySpan<byte> url,
                                              IManagedHtmlTokenConsumer consumer)
    {
        if (!CanBegin()) return NetworkOperationResult.Busy;
        if (consumer == null) throw new ArgumentNullException(nameof(consumer));
        _tokenizer ??= new ManagedHtmlTokenizer();
        _tokenizer.Reset();
        consumer.Reset();
        _consumer = consumer;
        _scalarAdapter.Reset();
        _failureReason = ManagedHtmlFailureReason.None;
        _metadataChecked = false;
        NetworkOperationResult result = _text.BeginGetUrl(url, _scalarAdapter);
        if (result != NetworkOperationResult.Started)
        {
            MapTextFailure();
            return result;
        }
        _scalarAdapter.Configure(_tokenizer, consumer);
        _state = ManagedResourceState.Receiving;
        return result;
    }

    public NetworkOperationResult BeginGet(ReadOnlySpan<byte> hostname,
                                           ReadOnlySpan<byte> path,
                                           ManagedHtmlTreeBuilder builder) =>
        BeginGet(hostname, path, (IManagedHtmlTokenConsumer)builder);

    public NetworkOperationResult BeginGetUrl(ReadOnlySpan<byte> url,
                                              ManagedHtmlTreeBuilder builder) =>
        BeginGetUrl(url, (IManagedHtmlTokenConsumer)builder);

    public NetworkOperationResult Pause()
    {
        if (_state == ManagedResourceState.Paused) return NetworkOperationResult.Success;
        if (_state != ManagedResourceState.Receiving)
            return _state == ManagedResourceState.Idle
                ? NetworkOperationResult.InvalidArgument : NetworkOperationResult.Success;
        NetworkOperationResult result = _text.Pause();
        if (result == NetworkOperationResult.Success) _state = ManagedResourceState.Paused;
        return result;
    }

    public NetworkOperationResult Resume()
    {
        if (_state != ManagedResourceState.Paused)
            return _state == ManagedResourceState.Idle
                ? NetworkOperationResult.InvalidArgument : NetworkOperationResult.Success;
        _scalarAdapter.Resume();
        NetworkOperationResult result = _text.Resume();
        if (result != NetworkOperationResult.Success)
        {
            MapTextFailure();
            return result;
        }
        if (_tokenizer != null && _tokenizer.State == ManagedHtmlTokenizerState.Paused)
            _scalarAdapter.Resume();
        return FinishPollState();
    }

    public NetworkOperationResult Poll()
    {
        if (_state == ManagedResourceState.Completed ||
            _state == ManagedResourceState.Cancelled)
            return NetworkOperationResult.Success;
        if (_state == ManagedResourceState.Failed)
            return NetworkOperationResult.Failed;
        if (_state == ManagedResourceState.Paused) return NetworkOperationResult.Success;
        NetworkOperationResult result = _text.Poll();
        CheckMime();
        if (_state == ManagedResourceState.Failed) return NetworkOperationResult.Failed;
        if (_text.State == ManagedResourceState.Cancelled)
        {
            _state = ManagedResourceState.Cancelled;
            _failureReason = ManagedHtmlFailureReason.Cancelled;
            return NetworkOperationResult.Success;
        }
        if (_text.State == ManagedResourceState.Failed)
        {
            MapTextFailure();
            return NetworkOperationResult.Failed;
        }
        if (_text.State == ManagedResourceState.Paused)
        {
            _state = ManagedResourceState.Paused;
            return NetworkOperationResult.Success;
        }
        if (_scalarAdapter.IsPaused)
        {
            _state = ManagedResourceState.Paused;
            return NetworkOperationResult.Success;
        }
        if (result == NetworkOperationResult.Failed)
        {
            MapTextFailure();
            return result;
        }
        return FinishPollState(result);
    }

    public NetworkOperationResult Cancel()
    {
        if (_state == ManagedResourceState.Completed ||
            _state == ManagedResourceState.Cancelled ||
            _state == ManagedResourceState.Failed)
            return NetworkOperationResult.Success;
        NetworkOperationResult result = _text.Cancel();
        _scalarAdapter.Cancel();
        _consumer?.Cancel();
        _state = ManagedResourceState.Cancelled;
        _failureReason = ManagedHtmlFailureReason.Cancelled;
        return result;
    }

    public NetworkOperationResult Reset()
    {
        if (_state == ManagedResourceState.Receiving ||
            _state == ManagedResourceState.Paused)
            return NetworkOperationResult.Busy;
        NetworkOperationResult result = _text.Reset();
        if (result != NetworkOperationResult.Success) return result;
        _scalarAdapter.Reset();
        _tokenizer?.Reset();
        _consumer?.Reset();
        _consumer = null;
        _state = ManagedResourceState.Idle;
        _failureReason = ManagedHtmlFailureReason.None;
        _metadataChecked = false;
        return NetworkOperationResult.Success;
    }

    public bool TryCopyContentType(Span<byte> destination, out int length) =>
        _text.TryCopyContentType(destination, out length);

    public bool TryCopyResourceDigest(Span<byte> destination) =>
        _text.TryCopyResourceDigest(destination);

    private bool CanBegin() => _state == ManagedResourceState.Idle ||
        _state == ManagedResourceState.Completed ||
        _state == ManagedResourceState.Cancelled ||
        _state == ManagedResourceState.Failed;

    private void CheckMime()
    {
        if (_metadataChecked) return;
        ManagedTextProgressSnapshot progress = _text.Progress;
        if (progress.StatusCode == 0 ||
            progress.ContentTypeState == ManagedHttpContentTypeState.Missing)
            return;
        _metadataChecked = true;
        if (progress.MimeClassification != ManagedMimeClassification.Html)
        {
            _failureReason = ManagedHtmlFailureReason.UnsupportedMime;
            _text.Cancel();
            _state = ManagedResourceState.Failed;
        }
    }

    private NetworkOperationResult FinishPollState(
        NetworkOperationResult result = NetworkOperationResult.Success)
    {
        if (_text.State == ManagedResourceState.Completed)
        {
            if (!_scalarAdapter.CompleteIfNeeded())
            {
                MapTokenizerFailure();
                return NetworkOperationResult.Failed;
            }
            if (_scalarAdapter.IsPaused)
            {
                _state = ManagedResourceState.Paused;
                return NetworkOperationResult.Success;
            }
            _state = ManagedResourceState.Completed;
        }
        else _state = ManagedResourceState.Receiving;
        return result;
    }

    private void MapTextFailure()
    {
        switch (_text.FailureReason)
        {
            case ManagedTextFailureReason.Cancelled:
                _failureReason = ManagedHtmlFailureReason.Cancelled; break;
            case ManagedTextFailureReason.UnsupportedMime:
                _failureReason = ManagedHtmlFailureReason.UnsupportedMime; break;
            case ManagedTextFailureReason.ContentTypeTooLong:
            case ManagedTextFailureReason.MalformedContentType:
            case ManagedTextFailureReason.MalformedCharset:
            case ManagedTextFailureReason.EmptyCharset:
                _failureReason = ManagedHtmlFailureReason.ContentTypeFailure; break;
            case ManagedTextFailureReason.UnsupportedCharset:
                _failureReason = ManagedHtmlFailureReason.UnsupportedCharset; break;
            case ManagedTextFailureReason.InvalidUtf8:
            case ManagedTextFailureReason.TruncatedUtf8:
            case ManagedTextFailureReason.InvalidAscii:
                _failureReason = ManagedHtmlFailureReason.TextDecodingFailure; break;
            case ManagedTextFailureReason.ContentEncodingFailure:
            case ManagedTextFailureReason.DecodedResourceLimit:
            case ManagedTextFailureReason.EncodedEntityLimit:
                _failureReason = ManagedHtmlFailureReason.ContentEncodingFailure; break;
            case ManagedTextFailureReason.TlsFailure:
                _failureReason = ManagedHtmlFailureReason.TlsFailure; break;
            case ManagedTextFailureReason.TransportFailure:
                _failureReason = ManagedHtmlFailureReason.TransportFailure; break;
            case ManagedTextFailureReason.TeardownFailure:
                _failureReason = ManagedHtmlFailureReason.TeardownFailure; break;
            case ManagedTextFailureReason.RequestFailure:
                _failureReason = ManagedHtmlFailureReason.RequestFailure; break;
            default:
                MapTokenizerFailure(); break;
        }
        _state = ManagedResourceState.Failed;
    }

    private void MapTokenizerFailure()
    {
        if (_consumer is ManagedHtmlTreeBuilder builder &&
            builder.FailureReason != ManagedHtmlTreeBuilderFailureReason.None)
        {
            _failureReason = builder.FailureReason switch
            {
                ManagedHtmlTreeBuilderFailureReason.NodeCapacityExceeded => ManagedHtmlFailureReason.NodeCapacityExceeded,
                ManagedHtmlTreeBuilderFailureReason.TextCapacityExceeded => ManagedHtmlFailureReason.TextCapacityExceeded,
                ManagedHtmlTreeBuilderFailureReason.AttributeCapacityExceeded => ManagedHtmlFailureReason.AttributeCapacityExceeded,
                ManagedHtmlTreeBuilderFailureReason.AttributeValueCapacityExceeded => ManagedHtmlFailureReason.AttributeValueCapacityExceeded,
                ManagedHtmlTreeBuilderFailureReason.AttributeNameCapacityExceeded => ManagedHtmlFailureReason.AttributeNameCapacityExceeded,
                ManagedHtmlTreeBuilderFailureReason.TagNameCapacityExceeded => ManagedHtmlFailureReason.TagNameCapacityExceeded,
                ManagedHtmlTreeBuilderFailureReason.TreeDepthExceeded => ManagedHtmlFailureReason.TreeDepthExceeded,
                ManagedHtmlTreeBuilderFailureReason.InvalidTreeState => ManagedHtmlFailureReason.InvalidTreeState,
                ManagedHtmlTreeBuilderFailureReason.UnsupportedInsertionModeCase => ManagedHtmlFailureReason.UnsupportedInsertionModeCase,
                ManagedHtmlTreeBuilderFailureReason.Cancelled => ManagedHtmlFailureReason.Cancelled,
                _ => ManagedHtmlFailureReason.TokenConsumerFailure
            };
            _state = ManagedResourceState.Failed;
            return;
        }
        _failureReason = _tokenizer?.FailureReason ==
            ManagedHtmlTokenizerFailureReason.TokenConsumerFailure
            ? ManagedHtmlFailureReason.TokenConsumerFailure
            : ManagedHtmlFailureReason.HtmlTokenizerFailure;
        _state = ManagedResourceState.Failed;
    }

    private ManagedHtmlProgressSnapshot CreateProgress()
    {
        ManagedTextProgressSnapshot text = _text.Progress;
        ManagedHtmlProgressSnapshotData tokenizer = new(
            _tokenizer?.Progress ?? default, _consumer as ManagedHtmlTreeBuilder);
        return new(_state, _failureReason, text.FailureReason, text, tokenizer);
    }

    private sealed class ManagedHtmlScalarAdapter : IManagedTextConsumer
    {
        private readonly ManagedHtmlResourceRequest _owner;
        private ManagedHtmlTokenizer? _tokenizer;
        private IManagedHtmlTokenConsumer? _consumer;
        private ManagedResourceConsumerState _state;
        private ManagedTextConsumerFailureReason _failureReason;
        private bool _paused;
        private bool _completeRequested;
        private bool _consumerCompleted;

        internal ManagedHtmlScalarAdapter(ManagedHtmlResourceRequest owner) => _owner = owner;
        public ManagedResourceConsumerState State => _state;
        public ManagedTextConsumerFailureReason FailureReason => _failureReason;
        public int ScalarsProcessed => _tokenizer?.ScalarsConsumed ?? 0;
        internal bool IsPaused => _paused;

        internal void Configure(ManagedHtmlTokenizer tokenizer,
                                IManagedHtmlTokenConsumer consumer)
        {
            _tokenizer = tokenizer;
            _consumer = consumer;
            _state = ManagedResourceConsumerState.Receiving;
        }

        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment)
        {
            if (_state == ManagedResourceConsumerState.Cancelled ||
                _state == ManagedResourceConsumerState.Failed ||
                _state == ManagedResourceConsumerState.Completed)
                return ManagedHttpBodySinkResult.Fail;
            if (_paused || _tokenizer == null || _consumer == null)
                return ManagedHttpBodySinkResult.Pause;
            if (!_tokenizer.AppendInput(segment))
            {
                /* The text decoder presents a whole bounded scalar window.
                   After a token-consumer pause, the tokenizer may still own
                   part of the preceding window.  Drain that input first so
                   the next decoder window is not rejected forever. */
                ManagedHtmlTokenizerProcessResult drained =
                    _tokenizer.Pump(_consumer);
                if (drained == ManagedHtmlTokenizerProcessResult.Failed)
                {
                    _owner.MapTokenizerFailure();
                    _state = ManagedResourceConsumerState.Failed;
                    return ManagedHttpBodySinkResult.Fail;
                }
                if (drained == ManagedHtmlTokenizerProcessResult.Paused ||
                    !_tokenizer.AppendInput(segment))
                {
                    _paused = true;
                    _state = ManagedResourceConsumerState.Paused;
                    return ManagedHttpBodySinkResult.Pause;
                }
            }
            ManagedHtmlTokenizerProcessResult result =
                _tokenizer.Pump(_consumer);
            if (result == ManagedHtmlTokenizerProcessResult.Failed)
            {
                _failureReason = _tokenizer.FailureReason ==
                    ManagedHtmlTokenizerFailureReason.TokenConsumerFailure
                    ? ManagedTextConsumerFailureReason.ConsumerFailure
                    : ManagedTextConsumerFailureReason.ConsumerFailure;
                _state = ManagedResourceConsumerState.Failed;
                _owner.MapTokenizerFailure();
                return ManagedHttpBodySinkResult.Fail;
            }
            if (result == ManagedHtmlTokenizerProcessResult.Paused)
            {
                /* The complete decoder segment has already been copied into
                   the tokenizer's fixed queue.  Returning Continue commits
                   that segment to Phase 41; the next segment observes the
                   pause and returns Pause without advancing input. */
                _paused = true;
                _state = ManagedResourceConsumerState.Paused;
            }
            else _state = ManagedResourceConsumerState.Receiving;
            return ManagedHttpBodySinkResult.Continue;
        }

        public bool Complete()
        {
            if (_consumerCompleted) return true;
            if (_state == ManagedResourceConsumerState.Cancelled ||
                _state == ManagedResourceConsumerState.Failed ||
                _tokenizer == null || _consumer == null) return false;
            _completeRequested = true;
            if (_paused) return true;
            ManagedHtmlTokenizerProcessResult result =
                _tokenizer.Pump(_consumer, true);
            if (result == ManagedHtmlTokenizerProcessResult.Paused)
            {
                _paused = true;
                _state = ManagedResourceConsumerState.Paused;
                return true;
            }
            if (result == ManagedHtmlTokenizerProcessResult.Failed)
            {
                _state = ManagedResourceConsumerState.Failed;
                _owner.MapTokenizerFailure();
                return false;
            }
            if (result != ManagedHtmlTokenizerProcessResult.Complete ||
                !_consumer.Complete())
            {
                _failureReason = ManagedTextConsumerFailureReason.FinalizationFailure;
                _state = ManagedResourceConsumerState.Failed;
                return false;
            }
            _consumerCompleted = true;
            _state = ManagedResourceConsumerState.Completed;
            return true;
        }

        internal bool CompleteIfNeeded()
        {
            if (_consumerCompleted) return true;
            return Complete();
        }

        internal void Resume()
        {
            if (_tokenizer == null || _state == ManagedResourceConsumerState.Cancelled)
                return;
            _paused = false;
            _tokenizer.Resume();
            if (_state == ManagedResourceConsumerState.Paused)
                _state = ManagedResourceConsumerState.Receiving;
            if (_completeRequested && !_consumerCompleted && _consumer != null)
                Complete();
        }

        public void Cancel()
        {
            _tokenizer?.Cancel();
            _consumer?.Cancel();
            _state = ManagedResourceConsumerState.Cancelled;
            _failureReason = ManagedTextConsumerFailureReason.None;
        }

        public void Reset()
        {
            _tokenizer = null;
            _consumer = null;
            _state = ManagedResourceConsumerState.Idle;
            _failureReason = ManagedTextConsumerFailureReason.None;
            _paused = false;
            _completeRequested = false;
            _consumerCompleted = false;
        }
    }
}

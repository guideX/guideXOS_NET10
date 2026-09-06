using System;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>
/// Fixed-arena adapter which lets the text resource decoder feed one external
/// stylesheet directly into the CSS parser.  It deliberately has no retained
/// stylesheet buffer.
/// </summary>
public sealed class ManagedCssStreamingParser : IManagedTextConsumer
{
    private ManagedCssEngine? _engine;
    private ManagedResourceConsumerState _state;
    private ManagedTextConsumerFailureReason _failureReason;
    private int _processed;

    public ManagedCssStreamingParser(ManagedCssEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _state = ManagedResourceConsumerState.Idle;
    }

    public ManagedResourceConsumerState State => _state;
    public ManagedTextConsumerFailureReason FailureReason => _failureReason;
    public int ScalarsProcessed => _processed;

    public void Begin()
    {
        if (_engine == null || !_engine.BeginExternalStylesheet())
        {
            _state = ManagedResourceConsumerState.Failed;
            _failureReason = ManagedTextConsumerFailureReason.ConsumerFailure;
            return;
        }
        _processed = 0;
        _failureReason = ManagedTextConsumerFailureReason.None;
        _state = ManagedResourceConsumerState.Receiving;
    }

    public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> scalars)
    {
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed)
            return ManagedHttpBodySinkResult.Fail;
        if (_state != ManagedResourceConsumerState.Receiving ||
            _engine == null || !_engine.AppendExternalStylesheet(scalars))
        {
            _state = ManagedResourceConsumerState.Failed;
            _failureReason = ManagedTextConsumerFailureReason.ConsumerFailure;
            return ManagedHttpBodySinkResult.Fail;
        }
        _processed += scalars.Length;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedResourceConsumerState.Completed) return true;
        if (_state == ManagedResourceConsumerState.Cancelled ||
            _state == ManagedResourceConsumerState.Failed ||
            _engine == null || !_engine.CompleteExternalStylesheet())
        {
            _state = ManagedResourceConsumerState.Failed;
            if (_failureReason == ManagedTextConsumerFailureReason.None)
                _failureReason = ManagedTextConsumerFailureReason.FinalizationFailure;
            return false;
        }
        _state = ManagedResourceConsumerState.Completed;
        return true;
    }

    public void Cancel()
    {
        _engine?.CancelExternalStylesheet();
        _state = ManagedResourceConsumerState.Cancelled;
    }

    public void Reset()
    {
        _engine?.CancelExternalStylesheet();
        _processed = 0;
        _failureReason = ManagedTextConsumerFailureReason.None;
        _state = ManagedResourceConsumerState.Idle;
    }
}

public enum ManagedPageResourceState : byte
{
    Idle = 0,
    FetchingDocument = 1,
    DiscoveringSources = 2,
    FetchingExternalStylesheet = 3,
    Cascading = 4,
    LayingOut = 5,
    Painting = 6,
    Rasterizing = 7,
    Presenting = 8,
    Complete = 9,
    Cancelled = 10,
    Failed = 11
}

public enum ManagedPageFailureReason : byte
{
    None = 0,
    Cancelled = 1,
    ExternalStylesheetLimitExceeded = 2,
    ExternalStylesheetUrlInvalid = 3,
    ExternalStylesheetSchemeRejected = 4,
    ExternalStylesheetTransportFailure = 5,
    ExternalStylesheetHttpFailure = 6,
    ExternalStylesheetContentTypeRejected = 7,
    ExternalStylesheetTextDecodeFailure = 8,
    ExternalStylesheetCssFailure = 9,
    PageCascadeFailure = 10,
    PageLayoutFailure = 11,
    PagePaintFailure = 12,
    PageRasterFailure = 13,
    PagePresentationFailure = 14,
    DocumentFailure = 15
}

public readonly struct ManagedPageResourceOptions
{
    public ManagedPageResourceOptions(ManagedCssArenaOptions cssArenas,
                                      int externalStylesheetLimit = ManagedCssLimits.DefaultExternalStylesheetCapacity,
                                      int viewportWidth = 800,
                                      int viewportHeight = 600)
    {
        if (externalStylesheetLimit <= 0 ||
            externalStylesheetLimit > cssArenas.ExternalStylesheetCapacity)
            throw new ArgumentOutOfRangeException(nameof(externalStylesheetLimit));
        if (viewportWidth < 0 || viewportWidth > ManagedLayoutLimits.MaximumCoordinate)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight < 0 || viewportHeight > ManagedLayoutLimits.MaximumCoordinate)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        CssArenas = cssArenas;
        ExternalStylesheetLimit = externalStylesheetLimit;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
    }

    public static ManagedPageResourceOptions Default =>
        new(ManagedCssArenaOptions.Default);

    public ManagedCssArenaOptions CssArenas { get; }
    public int ExternalStylesheetLimit { get; }
    public int ViewportWidth { get; }
    public int ViewportHeight { get; }
}

public readonly struct ManagedExternalStylesheetTelemetry
{
    private readonly byte[]? _decodedDigest;

    internal ManagedExternalStylesheetTelemetry(int requestIndex, int nodeIndex,
        ManagedHttpsUrl requestedUrl, ManagedHttpsUrl finalUrl, int redirectCount,
        int statusCode, ManagedMimeClassification mime, ManagedTextCharset charset,
        ManagedTextCharsetSource charsetSource, ManagedHttpContentEncodingState contentEncoding,
        int encodedBytes, int decodedBytes,
        int scalars, int rulesAdded, int declarationsAdded, byte[]? decodedDigest)
    {
        RequestIndex = requestIndex;
        NodeIndex = nodeIndex;
        RequestedUrl = requestedUrl;
        FinalUrl = finalUrl;
        RedirectCount = redirectCount;
        StatusCode = statusCode;
        MimeClassification = mime;
        Charset = charset;
        CharsetSource = charsetSource;
        ContentEncoding = contentEncoding;
        EncodedBytes = encodedBytes;
        DecodedBytes = decodedBytes;
        Scalars = scalars;
        RulesAdded = rulesAdded;
        DeclarationsAdded = declarationsAdded;
        _decodedDigest = decodedDigest;
    }

    public int RequestIndex { get; }
    public int NodeIndex { get; }
    public ManagedHttpsUrl RequestedUrl { get; }
    public ManagedHttpsUrl FinalUrl { get; }
    public int RedirectCount { get; }
    public int StatusCode { get; }
    public ManagedMimeClassification MimeClassification { get; }
    public ManagedTextCharset Charset { get; }
    public ManagedTextCharsetSource CharsetSource { get; }
    public ManagedHttpContentEncodingState ContentEncoding { get; }
    public int EncodedBytes { get; }
    public int DecodedBytes { get; }
    public int Scalars { get; }
    public int RulesAdded { get; }
    public int DeclarationsAdded { get; }

    public bool TryCopyDecodedDigest(Span<byte> destination)
    {
        if (_decodedDigest == null || destination.Length < _decodedDigest.Length)
            return false;
        _decodedDigest.AsSpan().CopyTo(destination);
        return true;
    }
}

public readonly struct ManagedPageResourceTelemetry
{
    internal ManagedPageResourceTelemetry(int externalLimit, int encountered,
        int started, int loaded, int embedded, int alternate, int sourceCount,
        int currentSource, int activeRequest, int redirects, int encodedBytes,
        int decodedBytes, int documentScalars, int stylesheetScalars)
    {
        ExternalStylesheetLimit = externalLimit;
        ExternalStylesheetsEncountered = encountered;
        ExternalStylesheetRequestsStarted = started;
        ExternalStylesheetsLoaded = loaded;
        EmbeddedStylesheetsParsed = embedded;
        AlternateStylesheetsIgnored = alternate;
        StyleSourcesVisited = sourceCount;
        CurrentSourceNodeIndex = currentSource;
        ActiveExternalRequestIndex = activeRequest;
        ExternalStylesheetRedirects = redirects;
        ExternalEncodedBytes = encodedBytes;
        ExternalDecodedBytes = decodedBytes;
        DocumentScalars = documentScalars;
        StylesheetScalars = stylesheetScalars;
    }

    public int ExternalStylesheetLimit { get; }
    public int ExternalStylesheetsEncountered { get; }
    public int ExternalStylesheetRequestsStarted { get; }
    public int ExternalStylesheetsLoaded { get; }
    public int EmbeddedStylesheetsParsed { get; }
    public int AlternateStylesheetsIgnored { get; }
    public int StyleSourcesVisited { get; }
    public int CurrentSourceNodeIndex { get; }
    public int ActiveExternalRequestIndex { get; }
    public int ExternalStylesheetRedirects { get; }
    public int ExternalEncodedBytes { get; }
    public int ExternalDecodedBytes { get; }
    public int DocumentScalars { get; }
    public int StylesheetScalars { get; }
}

internal sealed class ManagedExternalStylesheetRecord
{
    internal int RequestIndex;
    internal int NodeIndex;
    internal ManagedHttpsUrl RequestedUrl;
    internal ManagedHttpsUrl FinalUrl;
    internal int RedirectCount;
    internal int StatusCode;
    internal ManagedMimeClassification Mime;
    internal ManagedTextCharset Charset;
    internal ManagedTextCharsetSource CharsetSource;
    internal ManagedHttpContentEncodingState ContentEncoding;
    internal int EncodedBytes;
    internal int DecodedBytes;
    internal int Scalars;
    internal int RulesAdded;
    internal int DeclarationsAdded;
    internal readonly byte[] Digest = new byte[ManagedSha256.DigestSize];
    internal bool HasDigest;

    internal ManagedExternalStylesheetTelemetry Snapshot() =>
        new(RequestIndex, NodeIndex, RequestedUrl, FinalUrl, RedirectCount,
            StatusCode, Mime, Charset, CharsetSource, ContentEncoding, EncodedBytes, DecodedBytes,
            Scalars, RulesAdded, DeclarationsAdded, HasDigest ? Digest : null);
}

/// <summary>
/// Cooperative page pipeline: HTML fetch, document-order style discovery,
/// bounded external CSS fetch/decode/parse, cascade, layout, paint, raster,
/// and the presentation boundary.  Every Poll performs bounded work and no
/// task, async state machine, stylesheet-sized buffer, or dynamic collection.
/// </summary>
public sealed class ManagedPageResourceOrchestrator
{
    private readonly ManagedHtmlResourceRequest _documentResource;
    private readonly ManagedTextResourceRequest _stylesheetResource;
    private readonly ManagedHtmlTreeBuilder _tree;
    private readonly ManagedCssEngine _styles;
    private readonly ManagedCssStreamingParser _stylesheetParser;
    private readonly ManagedPageResourceOptions _options;
    private readonly ManagedExternalStylesheetRecord[] _externalTelemetry;
    private readonly uint[] _attributeScratch = new uint[ManagedCssLimits.MaximumExternalStylesheetHrefLength];
    private readonly byte[] _hrefScratch = new byte[ManagedCssLimits.MaximumExternalStylesheetHrefLength];
    private readonly byte[] _resolvedUrlScratch = new byte[ManagedHttpsUrl.MaximumUrlLength];
    private readonly uint[] _typeScratch = new uint[ManagedTokenizerScratch.MaximumAttributeValueLength];
    private ManagedLayoutEngine? _layout;
    private ManagedPaintEngine? _paint;
    private ManagedSoftwareRasterizer? _rasterizer;
    private ManagedFramebuffer _framebuffer;
    private bool _hasFramebuffer;
    private ManagedPageResourceState _state;
    private ManagedPageFailureReason _failureReason;
    private ManagedCssParseFailureReason _cssFailureReason;
    private ManagedHttpsUrl _documentFinalUrl;
    private ManagedHttpsUrl _currentResolvedUrl;
    private int _sourceCursor;
    private int _sourcesVisited;
    private int _externalEncountered;
    private int _externalStarted;
    private int _externalLoaded;
    private int _embeddedParsed;
    private int _alternateIgnored;
    private int _currentSourceNode = -1;
    private int _activeRequest = -1;
    private int _nextRequestIndex;
    private int _currentRules;
    private int _currentDeclarations;
    private bool _activeExternalRequest;
    private bool _skipInitialAuthorReset;

    public ManagedPageResourceOrchestrator(ManagedHtmlResourceRequest documentResource,
                                           ManagedTextResourceRequest stylesheetResource,
                                           ManagedPageResourceOptions options)
    {
        _documentResource = documentResource ?? throw new ArgumentNullException(nameof(documentResource));
        _stylesheetResource = stylesheetResource ?? throw new ArgumentNullException(nameof(stylesheetResource));
        _options = options;
        if (_documentResource.Protocol != ManagedResourceProtocol.Https ||
            _stylesheetResource.Protocol != ManagedResourceProtocol.Https ||
            !_documentResource.RequiresSuccessfulStatus)
            throw new ArgumentException("Page resources must use HTTPS.");
        if (_stylesheetResource.RequiredMime != ManagedMimeClassification.Css ||
            !_stylesheetResource.RequiresSuccessfulStatus)
            throw new ArgumentException("Stylesheet resource must require text/css and a successful HTTP status.");

        _tree = new ManagedHtmlTreeBuilder();
        _styles = new ManagedCssEngine(_tree.Document, options.CssArenas);
        _stylesheetParser = new ManagedCssStreamingParser(_styles);
        _externalTelemetry = new ManagedExternalStylesheetRecord[options.ExternalStylesheetLimit];
        _state = ManagedPageResourceState.Idle;
    }

    internal ManagedPageResourceOrchestrator(ManagedNetworkService service,
        ReadOnlySpan<byte> trustedRoot, in ManagedX509UtcTime validationTime,
        ManagedSecureRandom random, ManagedPageResourceOptions options,
        int maximumEntityLength = ManagedHttpLimits.MaximumStreamedBodyLength,
        int maximumDecodedResourceLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
        _options = options;
        _documentResource = new ManagedHtmlResourceRequest(service, trustedRoot,
            in validationTime, random, maximumEntityLength, false,
            maximumDecodedResourceLength, true);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_DOCUMENT_RESOURCE_READY\r\n"u8);
        _stylesheetResource = new ManagedTextResourceRequest(service, trustedRoot,
            in validationTime, random, maximumEntityLength, false,
            maximumDecodedResourceLength, false, false,
            ManagedMimeClassification.Css, true);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_STYLESHEET_RESOURCE_READY\r\n"u8);
        _tree = new ManagedHtmlTreeBuilder();
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_TREE_READY\r\n"u8);
        /* The guest proof uses a dedicated bounded CSS arena.  Keeping it
           private to this page avoids stale author rules from the stage-19
           diagnostic arena while DHCP/TLS and page orchestration are live. */
        _styles = new ManagedCssEngine(_tree.Document, options.CssArenas);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_CSS_READY\r\n"u8);
        _stylesheetParser = new ManagedCssStreamingParser(_styles);
        _externalTelemetry = new ManagedExternalStylesheetRecord[options.ExternalStylesheetLimit];
        _skipInitialAuthorReset = true;
        _state = ManagedPageResourceState.Idle;
    }

    public ManagedPageResourceState State => _state;
    public ManagedPageFailureReason FailureReason => _failureReason;
    public ManagedCssParseFailureReason CssFailureReason => _cssFailureReason;
    public ManagedHtmlResourceRequest DocumentResource => _documentResource;
    public ManagedTextResourceRequest StylesheetResource => _stylesheetResource;
    public ManagedHtmlDocument Document => _tree.Document;
    public ManagedCssEngine Styles => _styles;
    public ManagedLayoutEngine? Layout => _layout;
    public ManagedPaintEngine? Paint => _paint;
    public ManagedSoftwareRasterizer? Rasterizer => _rasterizer;
    public ManagedFramebuffer Framebuffer => _framebuffer;
    public bool HasFramebuffer => _hasFramebuffer;
    public ManagedHttpsUrl FinalDocumentUrl => _documentFinalUrl;
    public ManagedPageResourceTelemetry Telemetry => new(
        _options.ExternalStylesheetLimit, _externalEncountered, _externalStarted,
        _externalLoaded, _embeddedParsed, _alternateIgnored, _sourcesVisited,
        _currentSourceNode, _activeRequest, ExternalRedirects(), ExternalEncodedBytes(),
        ExternalDecodedBytes(), _documentResource.Progress.Text.ScalarsDelivered,
        _stylesheetParser.ScalarsProcessed);

    public bool TryGetExternalStylesheetTelemetry(int index,
                                                   out ManagedExternalStylesheetTelemetry telemetry)
    {
        telemetry = default;
        if (index < 0 || index >= _externalTelemetry.Length ||
            _externalTelemetry[index] == null)
            return false;
        telemetry = _externalTelemetry[index].Snapshot();
        return true;
    }

    public NetworkOperationResult BeginGetUrl(ReadOnlySpan<byte> url)
    {
        if (_state != ManagedPageResourceState.Idle &&
            _state != ManagedPageResourceState.Complete &&
            _state != ManagedPageResourceState.Cancelled &&
            _state != ManagedPageResourceState.Failed)
            return NetworkOperationResult.Busy;
        if (_state != ManagedPageResourceState.Idle && Reset() != NetworkOperationResult.Success)
            return NetworkOperationResult.Busy;
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_BEGIN_TREE_RESET\r\n"u8);
        _tree.Reset();
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_BEGIN_TREE_RESET_DONE\r\n"u8);
        if (_skipInitialAuthorReset) _skipInitialAuthorReset = false;
        else if (!_styles.BeginAuthorStyles()) return Fail(ManagedPageFailureReason.DocumentFailure);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_BEGIN_STYLES_DONE\r\n"u8);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_BEGIN_DOCUMENT\r\n"u8);
        NetworkOperationResult result = _documentResource.BeginGetUrl(url, _tree);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_BEGIN_DOCUMENT_DONE\r\n"u8);
        if (result != NetworkOperationResult.Started)
            return Fail(ManagedPageFailureReason.DocumentFailure);
        ClearRunState();
        _state = ManagedPageResourceState.FetchingDocument;
        return result;
    }

    public NetworkOperationResult Poll()
    {
        switch (_state)
        {
            case ManagedPageResourceState.FetchingDocument:
                return PollDocument();
            case ManagedPageResourceState.DiscoveringSources:
                return PollSource();
            case ManagedPageResourceState.FetchingExternalStylesheet:
                return PollStylesheet();
            case ManagedPageResourceState.Cascading:
                return PollCascade();
            case ManagedPageResourceState.LayingOut:
                return PollLayout();
            case ManagedPageResourceState.Painting:
                return PollPaint();
            case ManagedPageResourceState.Rasterizing:
                return PollRaster();
            case ManagedPageResourceState.Presenting:
                _state = ManagedPageResourceState.Complete;
                return NetworkOperationResult.Success;
            case ManagedPageResourceState.Complete:
            case ManagedPageResourceState.Cancelled:
                return NetworkOperationResult.Success;
            case ManagedPageResourceState.Failed:
                return NetworkOperationResult.Failed;
            default:
                return NetworkOperationResult.InvalidArgument;
        }
    }

    public NetworkOperationResult Cancel()
    {
        if (_state == ManagedPageResourceState.Complete ||
            _state == ManagedPageResourceState.Cancelled ||
            _state == ManagedPageResourceState.Failed)
            return NetworkOperationResult.Success;
        if (_activeExternalRequest) _stylesheetResource.Cancel();
        _documentResource.Cancel();
        _stylesheetParser.Cancel();
        _state = ManagedPageResourceState.Cancelled;
        _failureReason = ManagedPageFailureReason.Cancelled;
        return NetworkOperationResult.Success;
    }

    public NetworkOperationResult Reset()
    {
        if (_state == ManagedPageResourceState.FetchingDocument ||
            _state == ManagedPageResourceState.FetchingExternalStylesheet)
            return NetworkOperationResult.Busy;
        NetworkOperationResult document = _documentResource.Reset();
        if (document != NetworkOperationResult.Success) return document;
        NetworkOperationResult stylesheet = _stylesheetResource.Reset();
        if (stylesheet != NetworkOperationResult.Success) return stylesheet;
        _stylesheetParser.Reset();
        _tree.Reset();
        _styles.Reset();
        _layout = null;
        _paint = null;
        _rasterizer = null;
        _framebuffer = default;
        _hasFramebuffer = false;
        ClearRunState();
        _state = ManagedPageResourceState.Idle;
        _failureReason = ManagedPageFailureReason.None;
        _cssFailureReason = ManagedCssParseFailureReason.None;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollDocument()
    {
        NetworkOperationResult result = _documentResource.Poll();
        if (_documentResource.State == ManagedResourceState.Failed)
            return Fail(ManagedPageFailureReason.DocumentFailure);
        if (_documentResource.State == ManagedResourceState.Cancelled)
            return Fail(ManagedPageFailureReason.Cancelled);
        if (result == NetworkOperationResult.Failed)
            return Fail(ManagedPageFailureReason.DocumentFailure);
        if (_documentResource.State != ManagedResourceState.Completed)
            return result;
        if (!_tree.Document.Validate(out ManagedHtmlDocumentValidationFailureReason validation) ||
            validation != ManagedHtmlDocumentValidationFailureReason.None)
            return Fail(ManagedPageFailureReason.DocumentFailure);
        _documentFinalUrl = _documentResource.FinalUrl;
        if (!_documentFinalUrl.IsValid) return Fail(ManagedPageFailureReason.DocumentFailure);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_DOCUMENT_COMPLETE\r\n"u8);
        _state = ManagedPageResourceState.DiscoveringSources;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollSource()
    {
        if (_sourceCursor >= _tree.Document.NodeCount)
        {
            _state = ManagedPageResourceState.Cascading;
            return NetworkOperationResult.Success;
        }
        int nodeIndex = _sourceCursor++;
        _currentSourceNode = nodeIndex;
        ++_sourcesVisited;
        if (!_styles.TryGetStyleSource(nodeIndex, out ManagedCssStyleSource source))
            return NetworkOperationResult.Success;
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_SOURCE_NODE=0x"u8,
                               (ulong)nodeIndex);
        if (source.Kind == ManagedCssStyleSourceKind.Embedded)
        {
            if (!_styles.TryParseEmbeddedStylesheet(source.Node))
                return FailCss(ManagedPageFailureReason.PageCascadeFailure);
            ++_embeddedParsed;
            return NetworkOperationResult.Success;
        }
        if (source.IsAlternate)
        {
            ++_alternateIgnored;
            return NetworkOperationResult.Success;
        }
        if (!IsCssType(source.Node)) return NetworkOperationResult.Success;
        if (!source.HasHref || source.HrefLength <= 0 ||
            source.HrefLength > _hrefScratch.Length ||
            !_tree.Document.TryFindAttribute(source.Node, ManagedHtmlAttributeName.Href,
                                             out ManagedHtmlAttributeView href) ||
            !_tree.Document.TryCopyAttributeValue(source.Node, href.Index,
                                                   _attributeScratch, out int hrefLength,
                                                   out bool hasValue) || !hasValue ||
            hrefLength <= 0 || hrefLength > _hrefScratch.Length)
            return Fail(ManagedPageFailureReason.ExternalStylesheetUrlInvalid);
        for (int index = 0; index != hrefLength; ++index)
        {
            uint scalar = _attributeScratch[index];
            if (scalar > 0x7F) return Fail(ManagedPageFailureReason.ExternalStylesheetUrlInvalid);
            _hrefScratch[index] = (byte)scalar;
        }
        if (!TryResolveStylesheetUrl(_hrefScratch.AsSpan(0, hrefLength),
                                     out _currentResolvedUrl,
                                     out ManagedHttpsUrlParseFailureReason urlFailure))
            return Fail(urlFailure == ManagedHttpsUrlParseFailureReason.HttpsDowngrade ||
                        urlFailure == ManagedHttpsUrlParseFailureReason.UnsupportedScheme ||
                        urlFailure == ManagedHttpsUrlParseFailureReason.UnsupportedReference
                            ? ManagedPageFailureReason.ExternalStylesheetSchemeRejected
                            : ManagedPageFailureReason.ExternalStylesheetUrlInvalid);
        ++_externalEncountered;
        if (_externalEncountered > _options.ExternalStylesheetLimit)
            return Fail(ManagedPageFailureReason.ExternalStylesheetLimitExceeded);
        if (!_currentResolvedUrl.TryCopyAbsoluteUrl(_resolvedUrlScratch, out int resolvedLength))
            return Fail(ManagedPageFailureReason.ExternalStylesheetUrlInvalid);
        _activeRequest = _nextRequestIndex++;
        _currentRules = _styles.RulesParsed;
        _currentDeclarations = _styles.DeclarationsParsed;
        NetworkOperationResult begin = _stylesheetResource.BeginGetUrl(
            _resolvedUrlScratch.AsSpan(0, resolvedLength), _stylesheetParser);
        if (begin != NetworkOperationResult.Started)
            return MapStylesheetFailureOnBegin();
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_BEGIN=0x"u8,
                               (ulong)_activeRequest);
        ++_externalStarted;
        _stylesheetParser.Begin();
        if (_stylesheetParser.State == ManagedResourceConsumerState.Failed)
            return FailCss(ManagedPageFailureReason.ExternalStylesheetCssFailure);
        _activeExternalRequest = true;
        _state = ManagedPageResourceState.FetchingExternalStylesheet;
        return begin;
    }

    private NetworkOperationResult PollStylesheet()
    {
        NetworkOperationResult result = _stylesheetResource.Poll();
        if (_stylesheetResource.State == ManagedResourceState.Failed)
            return MapStylesheetFailure();
        if (_stylesheetResource.State == ManagedResourceState.Cancelled)
            return Fail(ManagedPageFailureReason.Cancelled);
        if (result == NetworkOperationResult.Failed)
            return MapStylesheetFailure();
        if (_stylesheetResource.State != ManagedResourceState.Completed)
            return result;
        if (!_stylesheetParser.Complete())
            return FailCss(ManagedPageFailureReason.ExternalStylesheetCssFailure);
        ManagedExternalStylesheetRecord record = new();
        record.RequestIndex = _activeRequest;
        record.NodeIndex = _currentSourceNode;
        record.RequestedUrl = _currentResolvedUrl;
        record.FinalUrl = _stylesheetResource.FinalUrl;
        record.RedirectCount = _stylesheetResource.RedirectCount;
        ManagedTextProgressSnapshot progress = _stylesheetResource.Progress;
        record.StatusCode = progress.StatusCode;
        record.Mime = progress.MimeClassification;
        record.Charset = progress.Charset;
        record.CharsetSource = progress.CharsetSource;
        record.ContentEncoding = progress.ContentEncodingState;
        record.EncodedBytes = progress.EncodedHttpBytesReceived;
        record.DecodedBytes = progress.DecompressedResourceBytesProduced;
        record.Scalars = _stylesheetParser.ScalarsProcessed;
        record.RulesAdded = _styles.RulesParsed - _currentRules;
        record.DeclarationsAdded = _styles.DeclarationsParsed - _currentDeclarations;
        record.HasDigest = _stylesheetResource.TryCopyResourceDigest(record.Digest);
        int slot = _externalLoaded;
        if (slot < _externalTelemetry.Length) _externalTelemetry[slot] = record;
        ++_externalLoaded;
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_COMPLETE=0x"u8,
                               (ulong)record.RequestIndex);
        _activeExternalRequest = false;
        _activeRequest = -1;
        _state = ManagedPageResourceState.DiscoveringSources;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollCascade()
    {
        if (!_styles.CompleteAuthorStyles())
            return FailCss(ManagedPageFailureReason.PageCascadeFailure);
        _state = ManagedPageResourceState.LayingOut;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollLayout()
    {
        ManagedPhase48FontRegistry fonts = ManagedPhase48FontRegistry.Instance;
        fonts.ResetTelemetry();
        _layout = new ManagedLayoutEngine(_tree.Document, _styles,
                                          ManagedLayoutArenaOptions.Default, fonts);
        if (!_layout.TryLayout(_options.ViewportWidth, _options.ViewportHeight))
            return Fail(ManagedPageFailureReason.PageLayoutFailure);
        _state = ManagedPageResourceState.Painting;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollPaint()
    {
        if (_layout == null) return Fail(ManagedPageFailureReason.PagePaintFailure);
        _paint = new ManagedPaintEngine(_layout, ManagedPaintArenaOptions.Default,
                                        ManagedPhase48FontRegistry.Instance);
        if (!_paint.TryGenerate(_options.ViewportWidth, _options.ViewportHeight))
            return Fail(ManagedPageFailureReason.PagePaintFailure);
        _state = ManagedPageResourceState.Rasterizing;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollRaster()
    {
        if (_paint == null) return Fail(ManagedPageFailureReason.PageRasterFailure);
        int pixels = checked(_options.ViewportWidth * _options.ViewportHeight);
        uint[] storage = new uint[pixels];
        _framebuffer = new ManagedFramebuffer(storage, _options.ViewportWidth,
                                              _options.ViewportHeight);
        _rasterizer = new ManagedSoftwareRasterizer();
        if (!_rasterizer.TryRender(_paint, _framebuffer,
                                   ManagedPhase48FontRegistry.Instance))
            return Fail(ManagedPageFailureReason.PageRasterFailure);
        _hasFramebuffer = true;
        _state = ManagedPageResourceState.Presenting;
        return NetworkOperationResult.Success;
    }

    private bool IsCssType(ManagedHtmlNodeHandle node)
    {
        if (!_tree.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Type,
                                              out ManagedHtmlAttributeView type) ||
            !type.HasValue)
            return true;
        if (!_tree.Document.TryCopyAttributeValue(node, type.Index, _typeScratch,
                                                  out int length, out bool hasValue) ||
            !hasValue || length == 0)
            return false;
        int start = 0;
        while (start != length && IsAsciiWhitespace(_typeScratch[start])) ++start;
        int end = length;
        while (end > start && IsAsciiWhitespace(_typeScratch[end - 1])) --end;
        ReadOnlySpan<byte> css = "text/css"u8;
        if (end - start != css.Length) return false;
        for (int index = 0; index != css.Length; ++index)
        {
            uint scalar = _typeScratch[start + index];
            byte expected = css[index];
            if (scalar > 0x7F || ToLowerAscii((byte)scalar) != expected)
                return false;
        }
        return true;
    }

    private bool TryResolveStylesheetUrl(ReadOnlySpan<byte> reference,
        out ManagedHttpsUrl resolved, out ManagedHttpsUrlParseFailureReason failure)
    {
        if (StartsWithAsciiIgnoreCase(reference, "https:"u8))
            return ManagedHttpsUrl.TryParse(reference, out resolved, out failure);
        return ManagedHttpsUrl.TryResolve(_documentFinalUrl, reference,
                                          out resolved, out failure);
    }

    private NetworkOperationResult MapStylesheetFailureOnBegin()
    {
        if (_stylesheetResource.FailureReason == ManagedTextFailureReason.HttpFailure)
            return Fail(ManagedPageFailureReason.ExternalStylesheetHttpFailure);
        return MapStylesheetFailure();
    }

    private NetworkOperationResult MapStylesheetFailure()
    {
        ManagedTextFailureReason failure = _stylesheetResource.FailureReason;
        switch (failure)
        {
            case ManagedTextFailureReason.UnsupportedMime:
            case ManagedTextFailureReason.MalformedContentType:
            case ManagedTextFailureReason.ContentTypeTooLong:
                return Fail(ManagedPageFailureReason.ExternalStylesheetContentTypeRejected);
            case ManagedTextFailureReason.HttpFailure:
                return Fail(ManagedPageFailureReason.ExternalStylesheetHttpFailure);
            case ManagedTextFailureReason.InvalidUtf8:
            case ManagedTextFailureReason.TruncatedUtf8:
            case ManagedTextFailureReason.InvalidAscii:
            case ManagedTextFailureReason.UnsupportedCharset:
            case ManagedTextFailureReason.MalformedCharset:
            case ManagedTextFailureReason.EmptyCharset:
                return Fail(ManagedPageFailureReason.ExternalStylesheetTextDecodeFailure);
            case ManagedTextFailureReason.TextConsumerFailure:
            case ManagedTextFailureReason.TextDestinationFull:
                _cssFailureReason = _styles.FailureReason;
                return Fail(ManagedPageFailureReason.ExternalStylesheetCssFailure);
            default:
                return Fail(ManagedPageFailureReason.ExternalStylesheetTransportFailure);
        }
    }

    private NetworkOperationResult FailCss(ManagedPageFailureReason reason)
    {
        _cssFailureReason = _styles.FailureReason;
        return Fail(reason);
    }

    private NetworkOperationResult Fail(ManagedPageFailureReason reason)
    {
        if (_activeExternalRequest) _stylesheetResource.Cancel();
        _stylesheetParser.Cancel();
        _activeExternalRequest = false;
        _activeRequest = -1;
        _failureReason = reason;
        _state = reason == ManagedPageFailureReason.Cancelled
            ? ManagedPageResourceState.Cancelled : ManagedPageResourceState.Failed;
        return reason == ManagedPageFailureReason.Cancelled
            ? NetworkOperationResult.Success : NetworkOperationResult.Failed;
    }

    private void ClearRunState()
    {
        for (int index = 0; index != _externalTelemetry.Length; ++index)
            _externalTelemetry[index] = null!;
        _sourceCursor = 0;
        _sourcesVisited = 0;
        _externalEncountered = 0;
        _externalStarted = 0;
        _externalLoaded = 0;
        _embeddedParsed = 0;
        _alternateIgnored = 0;
        _currentSourceNode = -1;
        _activeRequest = -1;
        _nextRequestIndex = 0;
        _currentRules = 0;
        _currentDeclarations = 0;
        _activeExternalRequest = false;
        _documentFinalUrl = default;
        _currentResolvedUrl = default;
        _failureReason = ManagedPageFailureReason.None;
        _cssFailureReason = ManagedCssParseFailureReason.None;
        _layout = null;
        _paint = null;
        _rasterizer = null;
        _framebuffer = default;
        _hasFramebuffer = false;
    }

    private int ExternalRedirects()
    {
        int total = 0;
        for (int index = 0; index != _externalTelemetry.Length; ++index)
            if (_externalTelemetry[index] != null) total += _externalTelemetry[index].RedirectCount;
        if (_activeExternalRequest) total += _stylesheetResource.RedirectCount;
        return total;
    }

    private int ExternalEncodedBytes()
    {
        int total = 0;
        for (int index = 0; index != _externalTelemetry.Length; ++index)
            if (_externalTelemetry[index] != null) total += _externalTelemetry[index].EncodedBytes;
        if (_activeExternalRequest) total += _stylesheetResource.Progress.EncodedHttpBytesReceived;
        return total;
    }

    private int ExternalDecodedBytes()
    {
        int total = 0;
        for (int index = 0; index != _externalTelemetry.Length; ++index)
            if (_externalTelemetry[index] != null) total += _externalTelemetry[index].DecodedBytes;
        if (_activeExternalRequest) total += _stylesheetResource.Progress.DecompressedResourceBytesProduced;
        return total;
    }

    private static bool IsAsciiWhitespace(uint scalar) =>
        scalar == 0x09 || scalar == 0x0A || scalar == 0x0C ||
        scalar == 0x0D || scalar == 0x20;

    private static byte ToLowerAscii(byte value) =>
        value >= (byte)'A' && value <= (byte)'Z' ? (byte)(value + 32) : value;

    private static bool StartsWithAsciiIgnoreCase(ReadOnlySpan<byte> value,
                                                   ReadOnlySpan<byte> prefix)
    {
        if (value.Length < prefix.Length) return false;
        for (int index = 0; index != prefix.Length; ++index)
            if (ToLowerAscii(value[index]) != ToLowerAscii(prefix[index])) return false;
        return true;
    }

    private static class ManagedTokenizerScratch
    {
        internal const int MaximumAttributeValueLength = ManagedHtmlTokenizerLimits.MaximumAttributeValueLength;
    }
}

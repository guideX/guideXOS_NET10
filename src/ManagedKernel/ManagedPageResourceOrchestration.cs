using System;

namespace GuideXOS.Net10.ManagedKernel;

internal interface IManagedPageTerminalProof
{
    bool TryComplete(in ManagedFramebuffer framebuffer);
}

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
    Failed = 11,
    DiscoveringImages = 12,
    FetchingImage = 13
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
    DocumentFailure = 15,
    ImageLimitExceeded = 16,
    ImageUrlInvalid = 17,
    ImageSchemeRejected = 18,
    ImageTransportFailure = 19,
    ImageHttpFailure = 20,
    ImageContentTypeRejected = 21,
    ImagePngFailure = 22,
    ImageStoreFailure = 23,
    CssImageReferenceLimitExceeded = 24,
    CssImageUrlInvalid = 25,
    CssImageSchemeRejected = 26,
    CssImageResourceFailure = 27,
    CssImageContentTypeRejected = 28,
    CssImageDecodeFailure = 29,
    CssImageCapacityExceeded = 30
}

public readonly struct ManagedPageResourceOptions
{
    public ManagedPageResourceOptions(ManagedCssArenaOptions cssArenas,
                                      int externalStylesheetLimit = ManagedCssLimits.DefaultExternalStylesheetCapacity,
                                      int viewportWidth = 800,
                                      int viewportHeight = 600,
                                      int imageLimit = ManagedImageLimits.DefaultImageCapacity,
                                      int maximumImageWidth = ManagedImageLimits.DefaultMaximumWidth,
                                      int maximumImageHeight = ManagedImageLimits.DefaultMaximumHeight,
                                      int imagePixelBudget = ManagedImageLimits.DefaultPixelBudget)
    {
        if (externalStylesheetLimit <= 0 ||
            externalStylesheetLimit > cssArenas.ExternalStylesheetCapacity)
            throw new ArgumentOutOfRangeException(nameof(externalStylesheetLimit));
        if (viewportWidth < 0 || viewportWidth > ManagedLayoutLimits.MaximumCoordinate)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (viewportHeight < 0 || viewportHeight > ManagedLayoutLimits.MaximumCoordinate)
            throw new ArgumentOutOfRangeException(nameof(viewportHeight));
        if (imageLimit <= 0 || imageLimit > ManagedImageLimits.MaximumImageCapacity)
            throw new ArgumentOutOfRangeException(nameof(imageLimit));
        if (maximumImageWidth <= 0 || maximumImageWidth > ManagedImageLimits.DefaultMaximumWidth)
            throw new ArgumentOutOfRangeException(nameof(maximumImageWidth));
        if (maximumImageHeight <= 0 || maximumImageHeight > ManagedImageLimits.DefaultMaximumHeight)
            throw new ArgumentOutOfRangeException(nameof(maximumImageHeight));
        if (imagePixelBudget <= 0 || imagePixelBudget > ManagedImageLimits.DefaultPixelBudget)
            throw new ArgumentOutOfRangeException(nameof(imagePixelBudget));
        CssArenas = cssArenas;
        ExternalStylesheetLimit = externalStylesheetLimit;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        ImageLimit = imageLimit;
        MaximumImageWidth = maximumImageWidth;
        MaximumImageHeight = maximumImageHeight;
        ImagePixelBudget = imagePixelBudget;
    }

    public static ManagedPageResourceOptions Default =>
        new(ManagedCssArenaOptions.Default);

    public ManagedCssArenaOptions CssArenas { get; }
    public int ExternalStylesheetLimit { get; }
    public int ViewportWidth { get; }
    public int ViewportHeight { get; }
    public int ImageLimit { get; }
    public int MaximumImageWidth { get; }
    public int MaximumImageHeight { get; }
    public int ImagePixelBudget { get; }
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
        int decodedBytes, int documentScalars, int stylesheetScalars,
        int imageNodes, int imageRequests, int imagesLoaded, int imageCursor)
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
        ImageNodes = imageNodes;
        ImageRequestsStarted = imageRequests;
        ImagesLoaded = imagesLoaded;
        ImageCursor = imageCursor;
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
    public int ImageNodes { get; }
    public int ImageRequestsStarted { get; }
    public int ImagesLoaded { get; }
    public int ImageCursor { get; }
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

internal sealed class ManagedImageResourceRecord
{
    internal int Slot;
    internal int SourceNodeIndex;
    internal bool IsCssImage;
    internal int CssReferenceIndex;
    internal ManagedHttpsUrl RequestedUrl;
    internal ManagedHttpsUrl FinalUrl;
    internal int RedirectCount;
    internal int StatusCode;
    internal ManagedMimeClassification Mime;
    internal ManagedHttpContentEncodingState ContentEncoding;
    internal int WireBytes;
    internal int EntityBytes;
    internal int PngBytes;
    internal int Width;
    internal int Height;
    internal int BitDepth;
    internal int ColorType;
    internal int IdatChunks;
    internal int IdatCompressedBytes;
    internal int InflatedBytes;
    internal int DecodedPixels;
    internal int FilterNone;
    internal int FilterSub;
    internal int FilterUp;
    internal int FilterAverage;
    internal int FilterPaeth;
    internal readonly byte[] PngDigest = new byte[ManagedSha256.DigestSize];
    internal readonly byte[] PixelDigest = new byte[ManagedSha256.DigestSize];
    internal bool HasPngDigest;
    internal bool HasPixelDigest;

    internal ManagedImageResourceTelemetry Snapshot() =>
        new(Slot, SourceNodeIndex, RequestedUrl, FinalUrl, RedirectCount, StatusCode,
            Mime, ContentEncoding, WireBytes, EntityBytes, PngBytes, Width, Height,
            BitDepth, ColorType, IdatChunks, IdatCompressedBytes, InflatedBytes,
            DecodedPixels, FilterNone, FilterSub, FilterUp, FilterAverage, FilterPaeth,
            HasPngDigest ? PngDigest : null, HasPixelDigest ? PixelDigest : null);
}

public readonly struct ManagedImageResourceTelemetry
{
    private readonly byte[]? _pngDigest;
    private readonly byte[]? _pixelDigest;

    internal ManagedImageResourceTelemetry(int slot, int sourceNodeIndex,
        ManagedHttpsUrl requestedUrl, ManagedHttpsUrl finalUrl, int redirectCount,
        int statusCode, ManagedMimeClassification mime,
        ManagedHttpContentEncodingState contentEncoding, int wireBytes,
        int entityBytes, int pngBytes, int width, int height, int bitDepth,
        int colorType, int idatChunks, int idatCompressedBytes, int inflatedBytes,
        int decodedPixels, int filterNone, int filterSub, int filterUp,
        int filterAverage, int filterPaeth, byte[]? pngDigest, byte[]? pixelDigest)
    {
        Slot = slot; SourceNodeIndex = sourceNodeIndex; RequestedUrl = requestedUrl;
        FinalUrl = finalUrl; RedirectCount = redirectCount; StatusCode = statusCode;
        MimeClassification = mime; ContentEncoding = contentEncoding;
        WireBytes = wireBytes; EntityBytes = entityBytes; PngBytes = pngBytes;
        Width = width; Height = height; BitDepth = bitDepth; ColorType = colorType;
        IdatChunks = idatChunks; IdatCompressedBytes = idatCompressedBytes;
        InflatedBytes = inflatedBytes; DecodedPixels = decodedPixels;
        FilterNone = filterNone; FilterSub = filterSub; FilterUp = filterUp;
        FilterAverage = filterAverage; FilterPaeth = filterPaeth;
        _pngDigest = pngDigest; _pixelDigest = pixelDigest;
    }

    public int Slot { get; }
    public int SourceNodeIndex { get; }
    public ManagedHttpsUrl RequestedUrl { get; }
    public ManagedHttpsUrl FinalUrl { get; }
    public int RedirectCount { get; }
    public int StatusCode { get; }
    public ManagedMimeClassification MimeClassification { get; }
    public ManagedHttpContentEncodingState ContentEncoding { get; }
    public int WireBytes { get; }
    public int EntityBytes { get; }
    public int PngBytes { get; }
    public int Width { get; }
    public int Height { get; }
    public int BitDepth { get; }
    public int ColorType { get; }
    public int IdatChunks { get; }
    public int IdatCompressedBytes { get; }
    public int InflatedBytes { get; }
    public int DecodedPixels { get; }
    public int FilterNone { get; }
    public int FilterSub { get; }
    public int FilterUp { get; }
    public int FilterAverage { get; }
    public int FilterPaeth { get; }
    public bool TryCopyPngDigest(Span<byte> destination) => Copy(_pngDigest, destination);
    public bool TryCopyPixelDigest(Span<byte> destination) => Copy(_pixelDigest, destination);
    private static bool Copy(byte[]? value, Span<byte> destination)
    {
        if (value == null || destination.Length < value.Length) return false;
        value.AsSpan().CopyTo(destination); return true;
    }
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
    private readonly ManagedResourceRequest? _imageResource;
    private readonly ManagedHtmlTreeBuilder _tree;
    private readonly ManagedCssEngine _styles;
    private readonly ManagedCssStreamingParser _stylesheetParser;
    private readonly ManagedPageResourceOptions _options;
    private readonly bool _phase52Markers;
    private readonly IManagedPageTerminalProof? _terminalProof;
    private readonly ManagedExternalStylesheetRecord[] _externalTelemetry;
    private readonly ManagedImageResourceRecord[] _imageTelemetry;
    private readonly ManagedHttpsUrl[] _stylesheetBases;
    private readonly ManagedPageImageStore _images;
    private readonly ManagedPngDecoder _imageDecoder;
    private readonly uint[] _attributeScratch = new uint[ManagedCssLimits.MaximumExternalStylesheetHrefLength];
    private readonly byte[] _hrefScratch = new byte[ManagedCssLimits.MaximumExternalStylesheetHrefLength];
    private readonly byte[] _resolvedUrlScratch = new byte[ManagedHttpsUrl.MaximumUrlLength];
    private readonly uint[] _typeScratch = new uint[ManagedTokenizerScratch.MaximumAttributeValueLength];
    private readonly byte[] _typeScratchBytes = new byte[ManagedHttpLimits.MaximumContentTypeLength];
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
    private int _imageCursor;
    private int _cssImageCursor;
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
    private int _imageNodes;
    private int _imageStarted;
    private int _imagesLoaded;
    private int _currentImageNode = -1;
    private ManagedHttpsUrl _currentImageUrl;
    private bool _activeImageRequest;
    private bool _currentCssImage;
    private int _currentCssImageReference = -1;
    private int _currentExternalStylesheetIndex = -1;
    private int _cssImageFetches;
    private int _cssImageDeduplicated;
    private int _cssImageWinningReferences;

    public ManagedPageResourceOrchestrator(ManagedHtmlResourceRequest documentResource,
                                           ManagedTextResourceRequest stylesheetResource,
                                           ManagedPageResourceOptions options,
                                           ManagedResourceRequest? imageResource = null)
    {
        _documentResource = documentResource ?? throw new ArgumentNullException(nameof(documentResource));
        _stylesheetResource = stylesheetResource ?? throw new ArgumentNullException(nameof(stylesheetResource));
        _imageResource = imageResource;
        _options = options;
        _phase52Markers = false;
        _terminalProof = null;
        if (_documentResource.Protocol != ManagedResourceProtocol.Https ||
            _stylesheetResource.Protocol != ManagedResourceProtocol.Https ||
            !_documentResource.RequiresSuccessfulStatus)
            throw new ArgumentException("Page resources must use HTTPS.");
        if (_stylesheetResource.RequiredMime != ManagedMimeClassification.Css ||
            !_stylesheetResource.RequiresSuccessfulStatus)
            throw new ArgumentException("Stylesheet resource must require text/css and a successful HTTP status.");
        if (_imageResource != null &&
            (_imageResource.Protocol != ManagedResourceProtocol.Https ||
             _imageResource.RequiredMime != ManagedMimeClassification.Png ||
             !_imageResource.RequiresSuccessfulStatus))
            throw new ArgumentException("Image resource must require image/png and a successful HTTP status.");

        _tree = new ManagedHtmlTreeBuilder();
        _styles = new ManagedCssEngine(_tree.Document, options.CssArenas);
        _stylesheetParser = new ManagedCssStreamingParser(_styles);
        _externalTelemetry = new ManagedExternalStylesheetRecord[options.ExternalStylesheetLimit];
        _images = new ManagedPageImageStore(options.ImageLimit, options.ImagePixelBudget,
                                            options.MaximumImageWidth, options.MaximumImageHeight);
        _imageDecoder = new ManagedPngDecoder(_images);
        _imageTelemetry = new ManagedImageResourceRecord[options.ImageLimit];
        _stylesheetBases = new ManagedHttpsUrl[options.CssArenas.StylesheetCapacity];
        _state = ManagedPageResourceState.Idle;
    }

    internal ManagedPageResourceOrchestrator(ManagedNetworkService service,
        ReadOnlySpan<byte> trustedRoot, in ManagedX509UtcTime validationTime,
        ManagedSecureRandom random, ManagedPageResourceOptions options,
        int maximumEntityLength = ManagedHttpLimits.MaximumStreamedBodyLength,
        int maximumDecodedResourceLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength,
        bool phase52Markers = false,
        IManagedPageTerminalProof? terminalProof = null)
    {
        _options = options;
        _phase52Markers = phase52Markers;
        _terminalProof = terminalProof;
        _documentResource = new ManagedHtmlResourceRequest(service, trustedRoot,
            in validationTime, random, maximumEntityLength, false,
            maximumDecodedResourceLength, true);
        _stylesheetResource = new ManagedTextResourceRequest(service, trustedRoot,
            in validationTime, random, maximumEntityLength, false,
            maximumDecodedResourceLength, false, false,
            ManagedMimeClassification.Css, true);
        _imageResource = new ManagedResourceRequest(service, trustedRoot, in validationTime,
            random, maximumEntityLength, false, maximumDecodedResourceLength, true,
            ManagedMimeClassification.Png);
        _tree = new ManagedHtmlTreeBuilder();
        /* The guest proof uses a dedicated bounded CSS arena.  Keeping it
           private to this page avoids stale author rules from the stage-19
           diagnostic arena while DHCP/TLS and page orchestration are live. */
        _styles = new ManagedCssEngine(_tree.Document, options.CssArenas);
        _stylesheetParser = new ManagedCssStreamingParser(_styles);
        _externalTelemetry = new ManagedExternalStylesheetRecord[options.ExternalStylesheetLimit];
        _images = new ManagedPageImageStore(options.ImageLimit, options.ImagePixelBudget,
                                            options.MaximumImageWidth, options.MaximumImageHeight);
        _imageDecoder = new ManagedPngDecoder(_images);
        _imageTelemetry = new ManagedImageResourceRecord[options.ImageLimit];
        _stylesheetBases = new ManagedHttpsUrl[options.CssArenas.StylesheetCapacity];
        _skipInitialAuthorReset = true;
        _state = ManagedPageResourceState.Idle;
    }

    public ManagedPageResourceState State => _state;
    public ManagedPageFailureReason FailureReason => _failureReason;
    public ManagedCssParseFailureReason CssFailureReason => _cssFailureReason;
    public ManagedHtmlResourceRequest DocumentResource => _documentResource;
    public ManagedTextResourceRequest StylesheetResource => _stylesheetResource;
    public ManagedResourceRequest? ImageResource => _imageResource;
    public ManagedPageImageStore Images => _images;
    public int CssImageReferencesEncountered => _styles.CssImageReferenceCount;
    public int CssImageWinningReferences => _cssImageWinningReferences;
    public int CssImageFetches => _cssImageFetches;
    public int CssImageDeduplicated => _cssImageDeduplicated;
    public ManagedPngFailureReason ImagePngFailureReason => _imageDecoder.PngFailureReason;
    public ManagedPngTelemetry ImagePngTelemetry => _imageDecoder.Telemetry;
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
        _stylesheetParser.ScalarsProcessed, _imageNodes, _imageStarted,
        _imagesLoaded, _imageCursor);

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

    public bool TryGetImageTelemetry(int index, out ManagedImageResourceTelemetry telemetry)
    {
        telemetry = default;
        if (index < 0 || index >= _imageTelemetry.Length || _imageTelemetry[index] == null)
            return false;
        telemetry = _imageTelemetry[index].Snapshot();
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
        _tree.Reset();
        if (_skipInitialAuthorReset) _skipInitialAuthorReset = false;
        else if (!_styles.BeginAuthorStyles()) return Fail(ManagedPageFailureReason.DocumentFailure);
        NetworkOperationResult result = _documentResource.BeginGetUrl(url, _tree);
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
            case ManagedPageResourceState.DiscoveringImages:
                return PollImageDiscovery();
            case ManagedPageResourceState.FetchingExternalStylesheet:
                return PollStylesheet();
            case ManagedPageResourceState.FetchingImage:
                return PollImage();
            case ManagedPageResourceState.Cascading:
                return PollCascade();
            case ManagedPageResourceState.LayingOut:
                return PollLayout();
            case ManagedPageResourceState.Painting:
                return PollPaint();
            case ManagedPageResourceState.Rasterizing:
                /* If a caller observes the state byte one poll late after a
                   completed NativeAOT render, never rasterize the same page
                   a second time. */
                if (_hasFramebuffer)
                {
                    _state = ManagedPageResourceState.Complete;
                    return NetworkOperationResult.Success;
                }
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
        if (_activeImageRequest) _imageResource?.Cancel();
        _documentResource.Cancel();
        _stylesheetParser.Cancel();
        _state = ManagedPageResourceState.Cancelled;
        _failureReason = ManagedPageFailureReason.Cancelled;
        return NetworkOperationResult.Success;
    }

    public NetworkOperationResult Reset()
    {
        if (_state == ManagedPageResourceState.FetchingDocument ||
            _state == ManagedPageResourceState.FetchingExternalStylesheet ||
            _state == ManagedPageResourceState.FetchingImage)
            return NetworkOperationResult.Busy;
        NetworkOperationResult document = _documentResource.Reset();
        if (document != NetworkOperationResult.Success) return document;
        NetworkOperationResult stylesheet = _stylesheetResource.Reset();
        if (stylesheet != NetworkOperationResult.Success) return stylesheet;
        if (_imageResource != null)
        {
            NetworkOperationResult image = _imageResource.Reset();
            if (image != NetworkOperationResult.Success) return image;
        }
        _stylesheetParser.Reset();
        _tree.Reset();
        _styles.Reset();
        _images.Reset();
        _imageDecoder.Reset();
        _skipInitialAuthorReset = true;
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

    internal bool IsResetForReuse()
    {
        ManagedPageResourceTelemetry telemetry = Telemetry;
        ManagedHtmlProgressSnapshot document = _documentResource.Progress;
        ManagedTextProgressSnapshot stylesheet = _stylesheetResource.Progress;
        return _state == ManagedPageResourceState.Idle &&
               _failureReason == ManagedPageFailureReason.None &&
               _cssFailureReason == ManagedCssParseFailureReason.None &&
               !_documentFinalUrl.IsValid && !_currentResolvedUrl.IsValid &&
               _sourceCursor == 0 && _currentSourceNode == -1 &&
               _activeRequest == -1 && _nextRequestIndex == 0 &&
               _currentRules == 0 && _currentDeclarations == 0 &&
               !_activeExternalRequest &&
               _stylesheetParser.State == ManagedResourceConsumerState.Idle &&
               _stylesheetParser.FailureReason == ManagedTextConsumerFailureReason.None &&
               _stylesheetParser.ScalarsProcessed == 0 &&
               _documentResource.State == ManagedResourceState.Idle &&
               _documentResource.FailureReason == ManagedHtmlFailureReason.None &&
               !_documentResource.FinalUrl.IsValid &&
               _documentResource.TcpState == NetworkTcpState.Closed &&
               document.State == ManagedResourceState.Idle &&
               document.StatusCode == 0 && document.ScalarsProduced == 0 &&
               document.ScalarsDelivered == 0 &&
               _stylesheetResource.State == ManagedResourceState.Idle &&
               _stylesheetResource.FailureReason == ManagedTextFailureReason.None &&
               !_stylesheetResource.FinalUrl.IsValid &&
               _stylesheetResource.TcpState == NetworkTcpState.Closed &&
               stylesheet.State == ManagedResourceState.Idle &&
               stylesheet.StatusCode == 0 && stylesheet.ScalarsProduced == 0 &&
               stylesheet.ScalarsDelivered == 0 &&
               _tree.Document.NodeCount == 0 &&
               !_tree.Document.CanonicalHashAvailable &&
               !_styles.IsStyled && _styles.StylesheetsParsed == 0 &&
               _styles.RulesParsed == 0 && _styles.DeclarationsParsed == 0 &&
               !_styles.CanonicalHashAvailable &&
               telemetry.ExternalStylesheetsEncountered == 0 &&
               telemetry.ExternalStylesheetRequestsStarted == 0 &&
               telemetry.ExternalStylesheetsLoaded == 0 &&
               telemetry.EmbeddedStylesheetsParsed == 0 &&
               telemetry.StyleSourcesVisited == 0 &&
               telemetry.CurrentSourceNodeIndex == -1 &&
               telemetry.ActiveExternalRequestIndex == -1 &&
               telemetry.DocumentScalars == 0 && telemetry.StylesheetScalars == 0 &&
               _imageCursor == 0 && _imageNodes == 0 && _imageStarted == 0 &&
               _imagesLoaded == 0 && !_activeImageRequest &&
               _images.Count == 0 && _imageDecoder.State == ManagedResourceConsumerState.Idle &&
               (_imageResource == null || (_imageResource.State == ManagedResourceState.Idle &&
                _imageResource.Progress.StatusCode == 0)) &&
               _layout == null && _paint == null && _rasterizer == null &&
               !_hasFramebuffer;
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
        if (source.Kind == ManagedCssStyleSourceKind.Embedded)
        {
            if (!_styles.TryParseEmbeddedStylesheet(source.Node))
                return FailCss(ManagedPageFailureReason.PageCascadeFailure);
            int stylesheetIndex = _styles.StylesheetsParsed - 1;
            if (stylesheetIndex >= 0 && stylesheetIndex < _stylesheetBases.Length)
                _stylesheetBases[stylesheetIndex] = _documentFinalUrl;
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
        _currentExternalStylesheetIndex = _styles.StylesheetsParsed;
        _currentRules = _styles.RulesParsed;
        _currentDeclarations = _styles.DeclarationsParsed;
        NetworkOperationResult begin = _stylesheetResource.BeginGetUrl(
            _resolvedUrlScratch.AsSpan(0, resolvedLength), _stylesheetParser);
        if (begin != NetworkOperationResult.Started)
            return MapStylesheetFailureOnBegin();
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
        if (_currentExternalStylesheetIndex >= 0 &&
            _currentExternalStylesheetIndex < _stylesheetBases.Length)
            _stylesheetBases[_currentExternalStylesheetIndex] = record.FinalUrl;
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
        _activeExternalRequest = false;
        _activeRequest = -1;
        _state = ManagedPageResourceState.DiscoveringSources;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollImageDiscovery()
    {
        if (_imageCursor >= _tree.Document.NodeCount)
            return PollCssImageDiscovery();
        int nodeIndex = _imageCursor++;
        ManagedHtmlNodeHandle node = new(nodeIndex, _tree.Document.DocumentNode.Generation);
        if (_tree.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
            _tree.Document.GetElementTag(node) != ManagedHtmlTag.Img)
            return NetworkOperationResult.Success;
        ++_imageNodes;
        if (_imageNodes > _options.ImageLimit)
            return Fail(ManagedPageFailureReason.ImageLimitExceeded);
        if (!_tree.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Src,
                                             out ManagedHtmlAttributeView src) ||
            !src.HasValue)
            return NetworkOperationResult.Success;
        if (!_tree.Document.TryCopyAttributeValue(node, src.Index, _attributeScratch,
                                                   out int srcLength, out bool hasValue) ||
            !hasValue || srcLength == 0)
            return NetworkOperationResult.Success;
        if (srcLength > _hrefScratch.Length) return Fail(ManagedPageFailureReason.ImageUrlInvalid);
        for (int index = 0; index != srcLength; ++index)
        {
            uint scalar = _attributeScratch[index];
            if (scalar > 0x7F) return Fail(ManagedPageFailureReason.ImageUrlInvalid);
            _hrefScratch[index] = (byte)scalar;
        }
        if (!TryResolveImageUrl(_hrefScratch.AsSpan(0, srcLength),
                                out _currentImageUrl,
                                out ManagedHttpsUrlParseFailureReason urlFailure))
            return Fail(urlFailure == ManagedHttpsUrlParseFailureReason.HttpsDowngrade ||
                        urlFailure == ManagedHttpsUrlParseFailureReason.UnsupportedScheme ||
                        urlFailure == ManagedHttpsUrlParseFailureReason.UnsupportedReference
                            ? ManagedPageFailureReason.ImageSchemeRejected
                            : ManagedPageFailureReason.ImageUrlInvalid);
        if (TryFindLoadedImage(_currentImageUrl, out ManagedImageHandle existing))
        {
            if (!_images.TryAssociateSourceNode(existing, nodeIndex))
                return Fail(ManagedPageFailureReason.ImageStoreFailure);
            return NetworkOperationResult.Success;
        }
        if (_images.Count >= _options.ImageLimit)
            return Fail(ManagedPageFailureReason.ImageLimitExceeded);
        if (_imageResource == null ||
            !_currentImageUrl.TryCopyAbsoluteUrl(_resolvedUrlScratch, out int resolvedLength))
            return Fail(ManagedPageFailureReason.ImageTransportFailure);
        _currentImageNode = nodeIndex;
        _currentCssImage = false;
        _currentCssImageReference = -1;
        NetworkOperationResult begin = _imageResource.BeginGetUrl(
            _resolvedUrlScratch.AsSpan(0, resolvedLength), _imageDecoder);
        if (begin != NetworkOperationResult.Started)
            return MapImageFailure();
        /* BeginGetUrl resets the supplied consumer as part of the resource
           contract.  Start the decoder after that reset so the first entity
           byte enters its Receiving state and retains the source-node binding. */
        _imageDecoder.Start(nodeIndex);
        ++_imageStarted;
        _activeImageRequest = true;
        _state = ManagedPageResourceState.FetchingImage;
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_REQUEST_BEGIN\r\n"u8);
        return begin;
    }

    private NetworkOperationResult PollCssImageDiscovery()
    {
        if (_cssImageCursor >= _tree.Document.NodeCount)
        {
            _state = ManagedPageResourceState.LayingOut;
            return NetworkOperationResult.Success;
        }
        int nodeIndex = _cssImageCursor++;
        ManagedHtmlNodeHandle node = new(nodeIndex, _tree.Document.DocumentNode.Generation);
        if (_tree.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
            !_styles.TryGetComputedStyle(node, out ManagedComputedStyle style) ||
            style.BackgroundImageKind != ManagedCssBackgroundImageKind.Unresolved)
            return NetworkOperationResult.Success;
        ++_cssImageWinningReferences;
        if (!_styles.TryGetCssImageReference(style.BackgroundImageReferenceIndex,
                                             out ManagedCssImageReference reference) ||
            !reference.TryCopyUrl(_hrefScratch, out int urlLength))
            return Fail(ManagedPageFailureReason.CssImageUrlInvalid);
        ManagedHttpsUrl baseUrl = _documentFinalUrl;
        if (reference.Origin != ManagedCssImageReferenceOrigin.Inline)
        {
            if (reference.StylesheetIndex < 0 ||
                reference.StylesheetIndex >= _stylesheetBases.Length ||
                !_stylesheetBases[reference.StylesheetIndex].IsValid)
                return Fail(ManagedPageFailureReason.CssImageUrlInvalid);
            baseUrl = _stylesheetBases[reference.StylesheetIndex];
        }
        if (!TryResolveImageUrl(baseUrl, _hrefScratch.AsSpan(0, urlLength),
                                out _currentImageUrl,
                                out ManagedHttpsUrlParseFailureReason urlFailure))
            return Fail(urlFailure == ManagedHttpsUrlParseFailureReason.HttpsDowngrade ||
                        urlFailure == ManagedHttpsUrlParseFailureReason.UnsupportedScheme ||
                        urlFailure == ManagedHttpsUrlParseFailureReason.UnsupportedReference
                            ? ManagedPageFailureReason.CssImageSchemeRejected
                            : ManagedPageFailureReason.CssImageUrlInvalid);
        if (TryFindLoadedImage(_currentImageUrl, out ManagedImageHandle existing))
        {
            if (!_styles.TrySetBackgroundImageHandle(node, existing))
                return Fail(ManagedPageFailureReason.CssImageResourceFailure);
            ++_cssImageDeduplicated;
            return NetworkOperationResult.Success;
        }
        if (_images.Count >= _options.ImageLimit)
            return Fail(ManagedPageFailureReason.CssImageCapacityExceeded);
        if (_imageResource == null ||
            !_currentImageUrl.TryCopyAbsoluteUrl(_resolvedUrlScratch, out int resolvedLength))
            return Fail(ManagedPageFailureReason.CssImageResourceFailure);
        _currentImageNode = nodeIndex;
        _currentCssImage = true;
        _currentCssImageReference = reference.Index;
        NetworkOperationResult begin = _imageResource.BeginGetUrl(
            _resolvedUrlScratch.AsSpan(0, resolvedLength), _imageDecoder);
        if (begin != NetworkOperationResult.Started)
            return MapImageFailure();
        _imageDecoder.Start(nodeIndex);
        ++_imageStarted;
        ++_cssImageFetches;
        _activeImageRequest = true;
        _state = ManagedPageResourceState.FetchingImage;
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_REQUEST_BEGIN\r\n"u8);
        return begin;
    }

    private NetworkOperationResult PollImage()
    {
        if (_imageResource == null) return Fail(ManagedPageFailureReason.ImageTransportFailure);
        NetworkOperationResult result = _imageResource.Poll();
        if (_imageResource.State == ManagedResourceState.Failed ||
            result == NetworkOperationResult.Failed)
            return MapImageFailure();
        if (_imageResource.State == ManagedResourceState.Cancelled)
            return Fail(ManagedPageFailureReason.Cancelled);
        if (_imageResource.State != ManagedResourceState.Completed)
            return result;
        if (!_imageDecoder.IsComplete)
            return Fail(ManagedPageFailureReason.ImagePngFailure);
        ManagedResourceProgressSnapshot progress = _imageResource.Progress;
        ManagedImageResourceRecord record = new()
        {
            Slot = _imagesLoaded,
            SourceNodeIndex = _currentImageNode,
            RequestedUrl = _currentImageUrl,
            FinalUrl = _imageResource.FinalUrl,
            RedirectCount = _imageResource.RedirectCount,
            StatusCode = progress.StatusCode,
            Mime = ClassifyImageMime(_imageResource),
            ContentEncoding = progress.ContentEncodingState,
            WireBytes = progress.EncodedBytesReceived,
            EntityBytes = progress.DecodedBytesProduced,
            PngBytes = _imageDecoder.BytesProcessed,
            Width = _imageDecoder.Width,
            Height = _imageDecoder.Height,
            BitDepth = _imageDecoder.BitDepth,
            ColorType = _imageDecoder.ColorType,
            IdatChunks = _imageDecoder.IdatChunkCount,
            IdatCompressedBytes = _imageDecoder.IdatCompressedBytes,
            InflatedBytes = _imageDecoder.InflatedBytes,
            DecodedPixels = _imageDecoder.DecodedPixels,
            FilterNone = _imageDecoder.FilterNone,
            FilterSub = _imageDecoder.FilterSub,
            FilterUp = _imageDecoder.FilterUp,
            FilterAverage = _imageDecoder.FilterAverage,
            FilterPaeth = _imageDecoder.FilterPaeth
        };
        record.HasPngDigest = _imageDecoder.TryCopyResourceDigest(record.PngDigest);
        record.HasPixelDigest = _imageDecoder.TryCopyDecodedPixelDigest(record.PixelDigest);
        record.IsCssImage = _currentCssImage;
        record.CssReferenceIndex = _currentCssImageReference;
        if (_imagesLoaded < _imageTelemetry.Length) _imageTelemetry[_imagesLoaded] = record;
        ++_imagesLoaded;
        ManagedImageHandle completedHandle = new(record.Slot, _images.Generation);
        if (_currentCssImage &&
            !_styles.TrySetBackgroundImageHandle(
                new ManagedHtmlNodeHandle(_currentImageNode,
                                          _tree.Document.DocumentNode.Generation),
                completedHandle))
            return Fail(ManagedPageFailureReason.CssImageResourceFailure);
        _activeImageRequest = false;
        _currentImageNode = -1;
        _state = ManagedPageResourceState.DiscoveringImages;
        KernelLog.Write(_currentCssImage
            ? "GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_DECODE_PASS\r\n"u8
            : "GXOS_NET10:MANAGED_HTTPS_PHASE52_PNG_DECODE_PASS\r\n"u8);
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult MapImageFailure()
    {
        if (_imageResource == null) return Fail(ManagedPageFailureReason.ImageTransportFailure);
        if (_currentCssImage)
        {
            switch (_imageResource.FailureReason)
            {
                case ManagedResourceFailureReason.UnsupportedMime:
                    return Fail(ManagedPageFailureReason.CssImageContentTypeRejected);
                case ManagedResourceFailureReason.HttpFailure:
                    return Fail(ManagedPageFailureReason.CssImageResourceFailure);
                case ManagedResourceFailureReason.ConsumerFailure:
                case ManagedResourceFailureReason.DestinationFull:
                    return Fail(ManagedPageFailureReason.CssImageDecodeFailure);
                case ManagedResourceFailureReason.Cancelled:
                    return Fail(ManagedPageFailureReason.Cancelled);
                default:
                    return Fail(ManagedPageFailureReason.CssImageResourceFailure);
            }
        }
        switch (_imageResource.FailureReason)
        {
            case ManagedResourceFailureReason.UnsupportedMime:
                return Fail(ManagedPageFailureReason.ImageContentTypeRejected);
            case ManagedResourceFailureReason.HttpFailure:
                return Fail(ManagedPageFailureReason.ImageHttpFailure);
            case ManagedResourceFailureReason.ConsumerFailure:
            case ManagedResourceFailureReason.DestinationFull:
                return Fail(ManagedPageFailureReason.ImagePngFailure);
            case ManagedResourceFailureReason.Cancelled:
                return Fail(ManagedPageFailureReason.Cancelled);
            default:
                return Fail(ManagedPageFailureReason.ImageTransportFailure);
        }
    }

    private bool TryFindLoadedImage(in ManagedHttpsUrl requestedUrl,
                                    out ManagedImageHandle handle)
    {
        handle = ManagedImageHandle.Invalid;
        for (int index = 0; index != _imagesLoaded; ++index)
        {
            ManagedImageResourceRecord? record = _imageTelemetry[index];
            if (record == null || !record.RequestedUrl.Equals(requestedUrl)) continue;
            handle = new ManagedImageHandle(record.Slot, _images.Generation);
            return _images.TryGetDescriptor(handle, out _);
        }
        return false;
    }

    private ManagedMimeClassification ClassifyImageMime(ManagedResourceRequest resource)
    {
        if (!resource.TryCopyContentType(_typeScratchBytes, out int length))
            return ManagedMimeClassification.Unknown;
        return ManagedContentTypeParser.Parse(_typeScratchBytes.AsSpan(0, length)).Classification;
    }

    private NetworkOperationResult PollCascade()
    {
        if (!_styles.CompleteAuthorStyles())
            return FailCss(ManagedPageFailureReason.PageCascadeFailure);
        _imageCursor = 0;
        _cssImageCursor = 0;
        _state = ManagedPageResourceState.DiscoveringImages;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollLayout()
    {
        ManagedPhase48FontRegistry fonts = ManagedPhase48FontRegistry.Instance;
        fonts.ResetTelemetry();
        _layout = new ManagedLayoutEngine(_tree.Document, _styles,
                                          ManagedLayoutArenaOptions.Default, fonts, _images);
        if (!_layout.TryLayout(_options.ViewportWidth, _options.ViewportHeight))
            return Fail(ManagedPageFailureReason.PageLayoutFailure);
        _state = ManagedPageResourceState.Painting;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollPaint()
    {
        if (_layout == null) return Fail(ManagedPageFailureReason.PagePaintFailure);
        _paint = new ManagedPaintEngine(_layout, ManagedPaintArenaOptions.Default,
                                        ManagedPhase48FontRegistry.Instance, _images);
        if (!_paint.TryGenerate(_options.ViewportWidth, _options.ViewportHeight))
            return Fail(ManagedPageFailureReason.PagePaintFailure);
        _state = ManagedPageResourceState.Rasterizing;
        return NetworkOperationResult.Success;
    }

    private NetworkOperationResult PollRaster()
    {
        if (_paint == null) return Fail(ManagedPageFailureReason.PageRasterFailure);
        _rasterizer = new ManagedSoftwareRasterizer();
        int pixels = checked(_options.ViewportWidth * _options.ViewportHeight);
        GC.Collect(0);
        uint[] storage = new uint[pixels];
        _framebuffer = new ManagedFramebuffer(storage, _options.ViewportWidth,
                                              _options.ViewportHeight);
        if (!_rasterizer.TryRender(_paint, _framebuffer,
                                   ManagedPhase48FontRegistry.Instance))
            return Fail(ManagedPageFailureReason.PageRasterFailure);
        _hasFramebuffer = true;
        _state = _phase52Markers ? ManagedPageResourceState.Complete
                                 : ManagedPageResourceState.Presenting;
        if (_phase52Markers && _terminalProof != null &&
            !_terminalProof.TryComplete(in _framebuffer))
            return Fail(ManagedPageFailureReason.PagePresentationFailure);
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

    private bool TryResolveImageUrl(ReadOnlySpan<byte> reference,
        out ManagedHttpsUrl resolved, out ManagedHttpsUrlParseFailureReason failure)
        => TryResolveImageUrl(_documentFinalUrl, reference, out resolved, out failure);

    private bool TryResolveImageUrl(in ManagedHttpsUrl baseUrl,
        ReadOnlySpan<byte> reference, out ManagedHttpsUrl resolved,
        out ManagedHttpsUrlParseFailureReason failure)
    {
        if (StartsWithAsciiIgnoreCase(reference, "https:"u8))
            return ManagedHttpsUrl.TryParse(reference, out resolved, out failure);
        return ManagedHttpsUrl.TryResolve(baseUrl, reference,
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
        if (_styles.FailureReason == ManagedCssParseFailureReason.CssImageReferenceCapacityExceeded)
            reason = ManagedPageFailureReason.CssImageReferenceLimitExceeded;
        return Fail(reason);
    }

    private NetworkOperationResult Fail(ManagedPageFailureReason reason)
    {
        if (_activeExternalRequest) _stylesheetResource.Cancel();
        if (_activeImageRequest) _imageResource?.Cancel();
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
        _imageCursor = 0;
        _cssImageCursor = 0;
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
        _imageNodes = 0;
        _imageStarted = 0;
        _imagesLoaded = 0;
        _currentImageNode = -1;
        _currentImageUrl = default;
        _activeImageRequest = false;
        _currentCssImage = false;
        _currentCssImageReference = -1;
        _currentExternalStylesheetIndex = -1;
        _cssImageFetches = 0;
        _cssImageDeduplicated = 0;
        _cssImageWinningReferences = 0;
        _stylesheetBases.AsSpan().Clear();
        _images.Reset();
        _imageDecoder.Reset();
        for (int index = 0; index != _imageTelemetry.Length; ++index)
            _imageTelemetry[index] = null!;
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

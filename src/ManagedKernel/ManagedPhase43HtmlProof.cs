using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Deterministic Phase 43/44 proof.  This is the first guest proof that retains
   a managed HTML document; Phase 44 extends it with bounded CSS parsing and
   cascade.  The document is a fixed-capacity arena and the response still
   traverses the Phase 41/42 HTTP, gzip, MIME, UTF-8, and tokenizer gates before
   the tree builder sees a scalar. */
internal sealed class ManagedPhase43HtmlProof
{
    private const int ResourceLength = 2566;
    private const int TokenCount = 251;
    private const int TextTokenCount = 60;
    private const int StartTagCount = 97;
    private const int EndTagCount = 91;
    private const int CommentCount = 1;
    private const int DoctypeCount = 1;
    private const int AttributeCount = 62;
    private const int EntityCount = 26;
    private const int NodeCount = 160;
    private const int ElementCount = 98;
    private const int TextNodeCount = 60;
    private const int TextScalarCount = 662;
    private const int AttributeValueScalarCount = 202;
    private const int PeakDepth = 7;
    private const int ImpliedCount = 0;
    private const int UnmatchedCount = 0;
    private const int ImplicitCloseCount = 1;
    private static readonly byte[] ExpectedDocumentHash =
    {
        0xE6, 0x93, 0x06, 0x8D, 0x35, 0x6D, 0xCA, 0x59,
        0xE6, 0x1D, 0x57, 0xCD, 0x38, 0x7C, 0x26, 0x1C,
        0xF4, 0x52, 0x37, 0x3B, 0x98, 0x36, 0x4F, 0x4B,
        0x1A, 0x6D, 0x3E, 0xFC, 0xB2, 0x81, 0x22, 0x4A
    };
    private static readonly byte[] ExpectedResourceDigest =
    {
        0xF5, 0xE3, 0x93, 0xFF, 0x30, 0x67, 0x37, 0xE4,
        0x1C, 0x5B, 0xD9, 0x30, 0x64, 0x27, 0x86, 0x87,
        0x0E, 0x73, 0x3E, 0x8F, 0x16, 0x44, 0xBD, 0x75,
        0xE2, 0x8A, 0x13, 0x8D, 0xFB, 0x82, 0xEB, 0x21
    };
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private ReadOnlySpan<byte> Path => _paintMode ? "/phase46/gzip"u8 :
        (_layoutMode ? "/phase45/gzip"u8 :
        (_cssMode ? "/phase44/gzip"u8 : "/phase43/gzip"u8));
    private ReadOnlySpan<byte> PhasePrefix => _paintMode
        ? "GXOS_NET10:MANAGED_HTTPS_PHASE46_"u8
        : (_layoutMode ? "GXOS_NET10:MANAGED_HTTPS_PHASE45_"u8
        : (_cssMode ? "GXOS_NET10:MANAGED_HTTPS_PHASE44_"u8
        : "GXOS_NET10:MANAGED_HTTPS_PHASE43_"u8));
    private static ReadOnlySpan<byte> ContentType => "text/html; charset=utf-8"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHtmlResourceRequest _resource;
    private readonly ManagedHtmlTreeBuilder _tree;
    private readonly ManagedCssEngine? _cssEngine;
    private ManagedLayoutEngine? _layoutEngine;
    private readonly bool _capacityControl;
    private readonly bool _cssMode;
    private readonly bool _layoutMode;
    private readonly bool _paintMode;
    private ManagedPaintEngine? _paintEngine;
    private bool _bodyReceivedLogged;
    private bool _pauseObserved;
    private int _stablePausedPolls;

    internal ManagedPhase43HtmlProof(ManagedNetworkService service,
                                     bool capacityControl = false,
                                     bool cssMode = false,
                                     bool layoutMode = false,
                                     bool paintMode = false)
    {
        _service = service;
        _capacityControl = capacityControl;
        _cssMode = cssMode;
        _layoutMode = layoutMode;
        _paintMode = paintMode;
        _tree = capacityControl && !cssMode
            ? new ManagedHtmlTreeBuilder(new ManagedHtmlDocumentArenaOptions(
                80, 65_536, 2_048, 16_384, 128))
            : new ManagedHtmlTreeBuilder();
        _cssEngine = cssMode
            ? ManagedCssEngine.TakeNativeKernelArena(_tree.Document, capacityControl && !layoutMode)
            : null;
        ManagedSecureRandom random = new(new FixedEntropy(CreateEntropy()));
        _resource = new(service, ManagedTls12Phase31Fixtures.Root,
                        new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random,
                        ManagedHttpLimits.MaximumStreamedBodyLength,
                        compactTlsProfile: false,
                        maximumDecodedResourceLength: ManagedContentEncodingLimits.MaximumDecodedResourceLength);
    }

    internal bool TryRun()
    {
        if (!_service.GetStatus().DhcpBound || !_service.GetStatus().Configured ||
            !WriteResourceMarker("RESOURCE_READY"u8) ||
            !WriteResourceMarker("BEGIN_GET"u8))
            return false;
        NetworkOperationResult begin = _resource.BeginGet(Hostname, Path, _tree);
        if (begin != NetworkOperationResult.Started)
        {
            KernelLog.WriteHexLine(_paintMode
                    ? "GXOS_NET10:MANAGED_HTTPS_PHASE46_BEGIN_GET_FAILURE=0x"u8
                    : (_cssMode
                    ? "GXOS_NET10:MANAGED_HTTPS_PHASE44_BEGIN_GET_FAILURE=0x"u8
                    : "GXOS_NET10:MANAGED_HTTPS_PHASE43_BEGIN_GET_FAILURE=0x"u8),
                (ulong)begin);
            return false;
        }
        if (!WriteResourceMarker("RESOURCE_STARTED"u8) ||
            !WriteResourceMarker("REQUEST_STARTED"u8))
            return false;

        for (int poll = 0; poll != 131_072; ++poll)
        {
            NetworkOperationResult result = _resource.Poll();
            ManagedHtmlProgressSnapshot progress = _resource.Progress;
            if (result == NetworkOperationResult.Failed ||
                _resource.State == ManagedResourceState.Failed)
                return WriteFailure(progress);
            if (!_bodyReceivedLogged && progress.TextInputBytesConsumed != 0 &&
                !WriteResourceMarker("RESOURCE_BODY_RECEIVED"u8))
                return false;
            _bodyReceivedLogged |= progress.TextInputBytesConsumed != 0;
            if (!_pauseObserved && progress.TokensEmitted != 0)
            {
                if (_resource.Pause() != NetworkOperationResult.Success)
                    return false;
                if (!CheckStablePause()) return false;
                _pauseObserved = true;
                if (_resource.Resume() != NetworkOperationResult.Success) return false;
            }
            else if (_resource.State == ManagedResourceState.Paused)
            {
                if (!CheckStablePause()) return false;
                _pauseObserved = true;
                if (_resource.Resume() != NetworkOperationResult.Success) return false;
            }
            if (_resource.State == ManagedResourceState.Completed)
                return FinishSuccess();
        }
        return false;
    }

    private bool WriteResourceMarker(ReadOnlySpan<byte> suffix)
    {
        return KernelLog.Write(PhasePrefix) &&
               KernelLog.Write(suffix) && KernelLog.Write("\r\n"u8);
    }

    private bool CheckStablePause()
    {
        ManagedHtmlProgressSnapshot paused = _resource.Progress;
        for (int stable = 0; stable != 4; ++stable)
        {
            if (_resource.Poll() != NetworkOperationResult.Success) return false;
            ManagedHtmlProgressSnapshot current = _resource.Progress;
            if (current.State != ManagedResourceState.Paused ||
                current.EncodedBytesReceived != paused.EncodedBytesReceived ||
                current.DecompressedBytesProduced != paused.DecompressedBytesProduced ||
                current.TextInputBytesConsumed != paused.TextInputBytesConsumed ||
                current.ScalarsProduced != paused.ScalarsProduced ||
                current.ScalarsDelivered != paused.ScalarsDelivered ||
                current.TokensEmitted != paused.TokensEmitted)
                return false;
            ++_stablePausedPolls;
        }
        return true;
    }

    private bool FinishSuccess()
    {
        if (_cssMode) return FinishCssSuccess();
        ManagedHtmlProgressSnapshot progress = _resource.Progress;
        ManagedHtmlTreeBuilderProgressSnapshot tree = _tree.Progress;
        Span<byte> contentType = stackalloc byte[ManagedHttpLimits.MaximumContentTypeLength];
        Span<byte> documentHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> resourceDigest = stackalloc byte[ManagedSha256.DigestSize];
        bool contentTypeAvailable = _resource.TryCopyContentType(
            contentType, out int contentTypeLength);
        bool resourceDigestAvailable = _resource.TryCopyResourceDigest(resourceDigest);
        if (!_pauseObserved || progress.StatusCode != 200 ||
            progress.MimeClassification != ManagedMimeClassification.Html ||
            progress.Charset != ManagedTextCharset.Utf8 ||
            progress.ContentTypeState != ManagedHttpContentTypeState.Available ||
            progress.ContentEncodingState != ManagedHttpContentEncodingState.Gzip ||
            progress.DecompressedBytesProduced != ResourceLength ||
            progress.TextInputBytesConsumed != ResourceLength ||
            progress.ScalarsConsumed != progress.ScalarsReceived ||
            progress.TokensEmitted != TokenCount ||
            progress.TextTokens != TextTokenCount ||
            progress.StartTagTokens != StartTagCount ||
            progress.EndTagTokens != EndTagCount ||
            progress.CommentTokens != CommentCount ||
            progress.DoctypeTokens != DoctypeCount ||
            progress.AttributesEmitted != AttributeCount ||
            progress.CharacterReferencesDecoded != EntityCount ||
            progress.BufferedTextScalars != 0 || progress.PauseCount != 1 ||
            progress.ResumeCount != 1 ||
            progress.TokenizerFailureReason != ManagedHtmlTokenizerFailureReason.None ||
            progress.TreeBuilderState != ManagedHtmlTreeBuilderState.Completed ||
            progress.TreeBuilderFailureReason != ManagedHtmlTreeBuilderFailureReason.None ||
            tree.NodeCount != NodeCount || tree.ElementCount != ElementCount ||
            tree.TextNodeCount != TextNodeCount || tree.AttributeCount != AttributeCount ||
            tree.TextScalarsUsed != TextScalarCount ||
            tree.AttributeValueScalarsUsed != AttributeValueScalarCount ||
            tree.PeakStackDepth != PeakDepth ||
            tree.ImpliedElementsInserted != ImpliedCount ||
            tree.UnmatchedEndTagsIgnored != UnmatchedCount ||
            tree.ImplicitClosesPerformed != ImplicitCloseCount ||
            !_tree.Document.IsHtmlDoctype ||
            !_tree.Validate(out ManagedHtmlDocumentValidationFailureReason validation) ||
            validation != ManagedHtmlDocumentValidationFailureReason.None ||
            !_tree.TryCopyCanonicalHash(documentHash) ||
            !documentHash.SequenceEqual(ExpectedDocumentHash) ||
            !contentTypeAvailable || !contentType[..contentTypeLength].SequenceEqual(ContentType) ||
            !resourceDigestAvailable || !resourceDigest.SequenceEqual(ExpectedResourceDigest))
            return false;

        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_STATUS=0x"u8, (ulong)progress.StatusCode) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_MIME=0x"u8, (ulong)progress.MimeClassification) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_CHARSET=0x"u8, (ulong)progress.Charset) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ENCODED_BYTES=0x"u8, (ulong)progress.EncodedBytesReceived) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_DECOMPRESSED_BYTES=0x"u8, (ulong)progress.DecompressedBytesProduced) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_INPUT_BYTES=0x"u8, (ulong)progress.TextInputBytesConsumed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_SCALARS_RECEIVED=0x"u8, (ulong)progress.ScalarsReceived) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_SCALARS_CONSUMED=0x"u8, (ulong)progress.ScalarsConsumed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TOKENS=0x"u8, (ulong)progress.TokensEmitted) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_TOKENS=0x"u8, (ulong)progress.TextTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_START_TAGS=0x"u8, (ulong)progress.StartTagTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_END_TAGS=0x"u8, (ulong)progress.EndTagTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_COMMENTS=0x"u8, (ulong)progress.CommentTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_DOCTYPES=0x"u8, (ulong)progress.DoctypeTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ATTRIBUTES=0x"u8, (ulong)progress.AttributesEmitted) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_NODES=0x"u8, (ulong)tree.NodeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_NODE_ARENA_USED=0x"u8, (ulong)tree.NodeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_NODE_ARENA_PEAK=0x"u8, (ulong)tree.PeakNodeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_NODE_ARENA_CAPACITY=0x"u8, (ulong)_tree.NodeCapacity) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ELEMENTS=0x"u8, (ulong)tree.ElementCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_NODES=0x"u8, (ulong)tree.TextNodeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_SCALARS=0x"u8, (ulong)tree.TextScalarsUsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_ARENA_USED=0x"u8, (ulong)tree.TextScalarsUsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_ARENA_PEAK=0x"u8, (ulong)tree.PeakTextScalars) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_ARENA_CAPACITY=0x"u8, (ulong)_tree.TextScalarCapacity) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ATTRIBUTE_ARENA_USED=0x"u8, (ulong)tree.AttributeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ATTRIBUTE_ARENA_CAPACITY=0x"u8, (ulong)_tree.AttributeCapacity) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_SCALARS=0x"u8, (ulong)tree.AttributeValueScalarsUsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_ARENA_USED=0x"u8, (ulong)tree.AttributeValueScalarsUsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_ARENA_PEAK=0x"u8, (ulong)tree.PeakAttributeValueScalars) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ATTRIBUTE_VALUE_ARENA_CAPACITY=0x"u8, (ulong)_tree.AttributeValueScalarCapacity) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PEAK_DEPTH=0x"u8, (ulong)tree.PeakStackDepth) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_FINAL_DEPTH=0x"u8, (ulong)tree.CurrentStackDepth) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_STACK_CAPACITY=0x"u8, (ulong)_tree.TreeDepthCapacity) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_IMPLIED=0x"u8, (ulong)tree.ImpliedElementsInserted) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_UNMATCHED_END_TAGS=0x"u8, (ulong)tree.UnmatchedEndTagsIgnored) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_IMPLICIT_CLOSES=0x"u8, (ulong)tree.ImplicitClosesPerformed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_ROOT_HANDLE=0x"u8, (ulong)_tree.DocumentRoot.Index) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_HTML_HANDLE=0x"u8, (ulong)_tree.Html.Index) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_HEAD_HANDLE=0x"u8, (ulong)_tree.Head.Index) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_BODY_HANDLE=0x"u8, (ulong)_tree.Body.Index) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_DOCTYPE_HANDLE=0x"u8, (ulong)_tree.Doctype.Index) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_HTML_PRESENT=0x"u8, _tree.Html != ManagedHtmlNodeHandle.Invalid ? 1UL : 0UL) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_HEAD_PRESENT=0x"u8, _tree.Head != ManagedHtmlNodeHandle.Invalid ? 1UL : 0UL) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_BODY_PRESENT=0x"u8, _tree.Body != ManagedHtmlNodeHandle.Invalid ? 1UL : 0UL) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PAUSE_COUNT=0x"u8, (ulong)progress.PauseCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_RESUME_COUNT=0x"u8, (ulong)progress.ResumeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_STABLE_PAUSED_POLLS=0x"u8, (ulong)_stablePausedPolls) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PEAK_HTTP_BUFFER=0x"u8, (ulong)progress.PeakHttpBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PEAK_DECOMPRESSION_BUFFER=0x"u8, (ulong)progress.PeakDecompressionBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PEAK_TEXT_BUFFER=0x"u8, (ulong)progress.PeakTextBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PEAK_TOKENIZER_TEXT=0x"u8, (ulong)progress.PeakTokenizerTextScalars) &&
               WriteTraversalPrefix() &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE43_DOCUMENT_HASH_WORD=0x"u8, documentHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_SHA256_WORD=0x"u8, resourceDigest) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_COMPLETE\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_PASS\r\n"u8);
    }

    private bool FinishCssSuccess()
    {
        ManagedHtmlProgressSnapshot progress = _resource.Progress;
        ManagedHtmlTreeBuilderProgressSnapshot tree = _tree.Progress;
        if (!KernelLog.Write(PhasePrefix) || !KernelLog.Write("CSS_BEGIN\r\n"u8))
            return false;
        if (progress.StatusCode != 200 ||
            progress.MimeClassification != ManagedMimeClassification.Html ||
            progress.Charset != ManagedTextCharset.Utf8 ||
            progress.ContentTypeState != ManagedHttpContentTypeState.Available ||
            progress.ContentEncodingState != ManagedHttpContentEncodingState.Gzip ||
            progress.DecompressedBytesProduced == 0 ||
            progress.TextInputBytesConsumed == 0 ||
            progress.ScalarsConsumed != progress.ScalarsReceived ||
            progress.TokenizerFailureReason != ManagedHtmlTokenizerFailureReason.None ||
            progress.TreeBuilderState != ManagedHtmlTreeBuilderState.Completed ||
            progress.TreeBuilderFailureReason != ManagedHtmlTreeBuilderFailureReason.None ||
            !_tree.Validate(out ManagedHtmlDocumentValidationFailureReason validation) ||
            validation != ManagedHtmlDocumentValidationFailureReason.None)
            return false;
        if (!KernelLog.Write(PhasePrefix) || !KernelLog.Write("CSS_TREE_VALIDATED\r\n"u8))
            return false;

        ManagedCssEngine? css = _cssEngine;
        if (css == null) return false;
        if (!KernelLog.Write(PhasePrefix) || !KernelLog.Write("CSS_ENGINE_CREATED\r\n"u8))
            return false;
        if (!css.TryStyle())
        {
            if (!_capacityControl || _layoutMode || css.FailureReason != ManagedCssParseFailureReason.RuleCapacityExceeded)
                return WriteCssFailure(css);
            KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_FAILURE=0x"u8,
                                   (ulong)css.FailureReason);
            KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_CAPACITY_CONTROL_VALIDATED\r\n"u8);
            KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_CAPACITY_NEGATIVE_PASS\r\n"u8);
            return false;
        }
        if (_capacityControl && !_layoutMode) return false;

        if (_layoutMode)
            return FinishLayoutSuccess(progress, tree, css);

        ManagedHtmlNodeHandle main = FindElementById("main"u8);
        ManagedHtmlNodeHandle note = FindElementByClass("note"u8);
        ManagedHtmlNodeHandle plain = FindElementByClass("plain"u8);
        ManagedHtmlNodeHandle important = FindElementByClass("important"u8);
        if (main == ManagedHtmlNodeHandle.Invalid || note == ManagedHtmlNodeHandle.Invalid ||
            plain == ManagedHtmlNodeHandle.Invalid || important == ManagedHtmlNodeHandle.Invalid ||
            !css.TryGetComputedStyle(main, out ManagedComputedStyle mainStyle) ||
            !css.TryGetComputedStyle(note, out ManagedComputedStyle noteStyle) ||
            !css.TryGetComputedStyle(plain, out ManagedComputedStyle plainStyle) ||
            !css.TryGetComputedStyle(important, out ManagedComputedStyle importantStyle) ||
            mainStyle.Display != ManagedCssDisplay.Block ||
            mainStyle.Color != 0xFF008000U || mainStyle.BackgroundColor != 0x44112233U ||
            mainStyle.PaddingTop != new ManagedCssLength(300, ManagedCssLengthUnit.Px) ||
            mainStyle.PaddingRight != new ManagedCssLength(400, ManagedCssLengthUnit.Px) ||
            noteStyle.Color != 0xFFFF0000U || noteStyle.FontWeight != 700 ||
            plainStyle.Color != 0xFFFFFFFFU ||
            plainStyle.Width != new ManagedCssLength(5000, ManagedCssLengthUnit.Percent) ||
            importantStyle.Color != 0xFF0000FFU || css.InlineStylesParsed != 3 ||
            css.ImportantDeclarations == 0 || css.InheritedAssignments == 0 ||
            css.ElementsStyled == 0)
            return WriteCssFailure(css);
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE44_CSS_VERIFIED\r\n"u8))
            return false;

        Span<byte> documentHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> styleHash = stackalloc byte[ManagedSha256.DigestSize];
        if (!_tree.TryCopyCanonicalHash(documentHash) ||
            !css.TryCopyCanonicalStyleHash(styleHash)) return false;
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STATUS=0x"u8,
                                      (ulong)progress.StatusCode) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_DECOMPRESSED_BYTES=0x"u8,
                                      (ulong)progress.DecompressedBytesProduced) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_ENCODED_BYTES=0x"u8,
                                      (ulong)progress.EncodedBytesReceived) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_TOKENS=0x"u8,
                                      (ulong)progress.TokensEmitted) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_PAUSE_COUNT=0x"u8,
                                      (ulong)progress.PauseCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_RESUME_COUNT=0x"u8,
                                      (ulong)progress.ResumeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STABLE_PAUSED_POLLS=0x"u8,
                                      (ulong)_stablePausedPolls) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_PEAK_HTTP_BUFFER=0x"u8,
                                      (ulong)progress.PeakHttpBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_PEAK_DECOMPRESSION_BUFFER=0x"u8,
                                      (ulong)progress.PeakDecompressionBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_PEAK_TEXT_BUFFER=0x"u8,
                                      (ulong)progress.PeakTextBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_PEAK_TOKENIZER_TEXT=0x"u8,
                                      (ulong)progress.PeakTokenizerTextScalars) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_NODES=0x"u8,
                                      (ulong)tree.NodeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_ELEMENTS=0x"u8,
                                      (ulong)tree.ElementCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_TEXT_SCALARS=0x"u8,
                                      (ulong)tree.TextScalarsUsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLESHEETS=0x"u8,
                                      (ulong)css.StylesheetsParsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_RULES=0x"u8,
                                      (ulong)css.RulesParsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_DECLARATIONS=0x"u8,
                                      (ulong)css.DeclarationsParsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_SELECTOR_MATCHES=0x"u8,
                                      (ulong)css.SelectorMatches) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_INLINE_STYLES=0x"u8,
                                      (ulong)css.InlineStylesParsed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_IMPORTANT=0x"u8,
                                      (ulong)css.ImportantDeclarations) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_INHERITED=0x"u8,
                                      (ulong)css.InheritedAssignments) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_ELEMENTS_STYLED=0x"u8,
                                      (ulong)css.ElementsStyled) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_RULE_CAPACITY=0x"u8,
                                      (ulong)css.RuleCapacity) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_RULE_PEAK=0x"u8,
                                      (ulong)css.RulePeak) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_DECLARATION_CAPACITY=0x"u8,
                                      (ulong)css.DeclarationCapacity) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_COMPUTED_STYLE_CAPACITY=0x"u8,
                                      (ulong)css.ComputedStyleCapacity) &&
                WriteCssStyle("MAIN"u8, main, mainStyle) && WriteCssStyle("NOTE"u8, note, noteStyle) &&
                WriteCssStyle("PLAIN"u8, plain, plainStyle) && WriteCssStyle("IMPORTANT"u8, important, importantStyle) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE44_DOCUMENT_HASH_WORD=0x"u8,
                           documentHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLE_HASH_WORD=0x"u8,
                           styleHash) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_COMPLETE\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE44_RESOURCE_PASS\r\n"u8);
    }

    private bool WriteCssFailure(ManagedCssEngine css)
    {
        KernelLog.Write(PhasePrefix);
        KernelLog.WriteHexLine("CSS_FAILURE=0x"u8, (ulong)css.FailureReason);
        return false;
    }

    private bool FinishLayoutSuccess(ManagedHtmlProgressSnapshot progress,
                                     ManagedHtmlTreeBuilderProgressSnapshot tree,
                                     ManagedCssEngine css)
    {
        if (_paintMode)
            return FinishPaintSuccess(progress, tree, css);
        if (_capacityControl)
        {
            ManagedLayoutEngine sizing = new(_tree.Document, css,
                ManagedLayoutArenaOptions.Default, new ManagedDeterministicLayoutTextMetrics());
            if (!sizing.TryLayout(800, 600) || sizing.LayoutBoxCount <= 1)
                return false;
            int exactCapacity = sizing.LayoutBoxCount - 1;
            ManagedLayoutEngine negative = new(_tree.Document, css,
                new ManagedLayoutArenaOptions(exactCapacity,
                    ManagedLayoutLimits.DefaultLineCapacity,
                    ManagedLayoutLimits.DefaultTextFragmentCapacity,
                    ManagedLayoutLimits.DefaultTraversalStackCapacity),
                new ManagedDeterministicLayoutTextMetrics());
            bool failed = !negative.TryLayout(800, 600) &&
                          negative.FailureReason == ManagedLayoutFailureReason.LayoutBoxCapacityExceeded;
            if (!failed || !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_CAPACITY=0x"u8,
                    (ulong)exactCapacity) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_FAILURE=0x"u8,
                    (ulong)negative.FailureReason) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_CAPACITY_CONTROL_VALIDATED\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_CAPACITY_NEGATIVE_PASS\r\n"u8))
                return false;
            return false;
        }

        _layoutEngine = new ManagedLayoutEngine(_tree.Document, css,
            ManagedLayoutArenaOptions.Default, new ManagedDeterministicLayoutTextMetrics());
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_ENGINE_CREATED\r\n"u8) ||
            !_layoutEngine.TryLayout(800, 600) ||
            !_layoutEngine.Validate(out ManagedLayoutValidationFailureReason validation) ||
            validation != ManagedLayoutValidationFailureReason.None)
        {
            KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_FAILURE=0x"u8,
                                  (ulong)(_layoutEngine?.FailureReason ?? ManagedLayoutFailureReason.InvalidState));
            return false;
        }
        ManagedLayoutTelemetry telemetry = _layoutEngine.Telemetry;
        ManagedHtmlNodeHandle body = _tree.Body;
        ManagedHtmlNodeHandle main = FindElementById("main"u8);
        ManagedHtmlNodeHandle note = FindElementByClass("note"u8);
        if (body == ManagedHtmlNodeHandle.Invalid || main == ManagedHtmlNodeHandle.Invalid ||
            note == ManagedHtmlNodeHandle.Invalid ||
            !_layoutEngine.TryGetBoxForNode(body, out int bodyIndex) ||
            !_layoutEngine.TryGetBoxForNode(main, out int mainIndex) ||
            !_layoutEngine.TryGetBoxForNode(note, out int noteIndex) ||
            !_layoutEngine.TryGetBox(bodyIndex, out ManagedLayoutBox bodyBox) ||
            !_layoutEngine.TryGetBox(mainIndex, out ManagedLayoutBox mainBox) ||
            !_layoutEngine.TryGetBox(noteIndex, out ManagedLayoutBox noteBox)) return false;
        Span<byte> documentHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> styleHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> layoutHash = stackalloc byte[ManagedSha256.DigestSize];
        if (!_tree.TryCopyCanonicalHash(documentHash) || !css.TryCopyCanonicalStyleHash(styleHash) ||
            !_layoutEngine.TryCopyCanonicalLayoutHash(layoutHash)) return false;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_VERIFIED\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_STATUS=0x"u8, (ulong)progress.StatusCode) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_CONTENT_TYPE=0x"u8, (ulong)ContentType.Length) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_CONTENT_ENCODING=0x"u8, 1) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_ENCODED_BYTES=0x"u8, (ulong)progress.EncodedBytesReceived) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_DECOMPRESSED_BYTES=0x"u8, (ulong)progress.DecompressedBytesProduced) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_SCALARS=0x"u8, (ulong)tree.TextScalarsUsed) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_TOKENS=0x"u8, (ulong)progress.TokensEmitted) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_NODES=0x"u8, (ulong)tree.NodeCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_ELEMENTS=0x"u8, (ulong)tree.ElementCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_CSS_RULES=0x"u8, (ulong)css.RulesParsed) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_SELECTOR_MATCHES=0x"u8, (ulong)css.SelectorMatches) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_ELEMENTS_STYLED=0x"u8, (ulong)css.ElementsStyled) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_VIEWPORT_WIDTH=0x"u8, 800) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_VIEWPORT_HEIGHT=0x"u8, 600) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_BOXES=0x"u8, (ulong)telemetry.LayoutBoxCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BLOCK_BOXES=0x"u8, (ulong)telemetry.BlockBoxCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_INLINE_TEXT_BOXES=0x"u8, (ulong)telemetry.InlineTextBoxCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_LINES=0x"u8, (ulong)telemetry.LineCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_TEXT_FRAGMENTS=0x"u8, (ulong)telemetry.TextFragmentCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_TEXT_SCALARS_MEASURED=0x"u8, (ulong)telemetry.TextScalarsMeasured) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_SOFT_WRAPS=0x"u8, (ulong)telemetry.SoftWrapCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_FORCED_BREAKS=0x"u8, (ulong)telemetry.ForcedBreakCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_DISPLAY_NONE_SKIPS=0x"u8, (ulong)telemetry.DisplayNoneSkips) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_POSITIONED_BOXES=0x"u8, (ulong)telemetry.PositionedBoxCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_HORIZONTAL_OVERFLOW=0x"u8, (ulong)telemetry.HorizontalOverflowCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_VERTICAL_OVERFLOW=0x"u8, (ulong)telemetry.VerticalOverflowCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_PEAK_BOX_ARENA=0x"u8, (ulong)telemetry.PeakBoxArena) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_PEAK_LINE_ARENA=0x"u8, (ulong)telemetry.PeakLineArena) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_PEAK_FRAGMENT_ARENA=0x"u8, (ulong)telemetry.PeakFragmentArena) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_PEAK_TRAVERSAL_DEPTH=0x"u8, (ulong)telemetry.PeakTraversalDepth) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_CONTENT_WIDTH=0x"u8, (ulong)telemetry.DocumentContentWidth) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_CONTENT_HEIGHT=0x"u8, (ulong)telemetry.DocumentContentHeight) ||
            !WriteLayoutRecord("BODY"u8, bodyBox) || !WriteLayoutRecord("MAIN"u8, mainBox) ||
            !WriteLayoutRecord("NOTE"u8, noteBox)) return false;
        return WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE45_DOCUMENT_HASH_WORD=0x"u8, documentHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE45_STYLE_HASH_WORD=0x"u8, styleHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE45_LAYOUT_HASH_WORD=0x"u8, layoutHash) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE45_RESOURCE_COMPLETE\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE45_RESOURCE_PASS\r\n"u8);
    }

    private bool FinishPaintSuccess(ManagedHtmlProgressSnapshot progress,
                                    ManagedHtmlTreeBuilderProgressSnapshot tree,
                                    ManagedCssEngine css)
    {
        _layoutEngine = new ManagedLayoutEngine(_tree.Document, css,
            ManagedLayoutArenaOptions.Default, new ManagedDeterministicLayoutTextMetrics());
        if (!KernelLog.Write(PhasePrefix) || !KernelLog.Write("LAYOUT_ENGINE_CREATED\r\n"u8) ||
            !_layoutEngine.TryLayout(800, 600) ||
            !_layoutEngine.Validate(out ManagedLayoutValidationFailureReason layoutValidation) ||
            layoutValidation != ManagedLayoutValidationFailureReason.None)
        {
            KernelLog.Write(PhasePrefix);
            KernelLog.WriteHexLine("LAYOUT_FAILURE=0x"u8,
                                  (ulong)(_layoutEngine?.FailureReason ?? ManagedLayoutFailureReason.InvalidState));
            return false;
        }

        if (_capacityControl)
        {
            ManagedPaintEngine sizing = new(_layoutEngine);
            if (!sizing.TryGenerate(800, 600) || sizing.CommandsEmitted <= 1)
                return false;
            int exactCapacity = sizing.CommandsEmitted - 1;
            ManagedPaintEngine negative = new(_layoutEngine,
                new ManagedPaintArenaOptions(exactCapacity,
                    ManagedPaintLimits.DefaultClipDepthCapacity,
                    ManagedPaintLimits.DefaultOrderingCapacity));
            bool failed = !negative.TryGenerate(800, 600) &&
                          negative.FailureReason == ManagedPaintFailureReason.PaintCommandCapacityExceeded &&
                          negative.CommandsEmitted == 0;
            if (!failed ||
                !KernelLog.Write(PhasePrefix) || !KernelLog.Write("PAINT_CAPACITY_CONTROL_VALIDATED\r\n"u8) ||
                !WritePaintHex("PAINT_COMMAND_CAPACITY"u8, (ulong)exactCapacity) ||
                !WritePaintHex("PAINT_FAILURE"u8, (ulong)negative.FailureReason) ||
                !KernelLog.Write(PhasePrefix) || !KernelLog.Write("PAINT_CAPACITY_NEGATIVE_PASS\r\n"u8))
                return false;
            return false;
        }

        _paintEngine = new ManagedPaintEngine(_layoutEngine);
        if (!KernelLog.Write(PhasePrefix) || !KernelLog.Write("PAINT_ENGINE_CREATED\r\n"u8) ||
            !_paintEngine.TryGenerate(800, 600) ||
            !_paintEngine.Validate(out ManagedPaintValidationFailureReason paintValidation) ||
            paintValidation != ManagedPaintValidationFailureReason.None)
        {
            KernelLog.Write(PhasePrefix);
            KernelLog.WriteHexLine("PAINT_FAILURE=0x"u8,
                                  (ulong)(_paintEngine?.FailureReason ?? ManagedPaintFailureReason.InvalidState));
            return false;
        }

        ManagedLayoutTelemetry layoutTelemetry = _layoutEngine.Telemetry;
        ManagedPaintTelemetry paintTelemetry = _paintEngine.Telemetry;
        if (paintTelemetry.FillCommands == 0 || paintTelemetry.BorderCommands == 0 ||
            paintTelemetry.TextCommands == 0 || paintTelemetry.ImagePlaceholderCommands == 0 ||
            paintTelemetry.ClipPushes == 0 || paintTelemetry.DisplayNoneBoxesSkipped == 0 ||
            paintTelemetry.PositionedCommands == 0 || paintTelemetry.PositiveZOrderCount == 0)
            return false;
        ManagedHtmlNodeHandle body = _tree.Body;
        ManagedHtmlNodeHandle main = FindElementById("main"u8);
        ManagedHtmlNodeHandle note = FindElementByClass("note"u8);
        if (body == ManagedHtmlNodeHandle.Invalid || main == ManagedHtmlNodeHandle.Invalid ||
            note == ManagedHtmlNodeHandle.Invalid ||
            !_layoutEngine.TryGetBoxForNode(body, out int bodyIndex) ||
            !_layoutEngine.TryGetBoxForNode(main, out int mainIndex) ||
            !_layoutEngine.TryGetBoxForNode(note, out int noteIndex) ||
            !_layoutEngine.TryGetBox(bodyIndex, out ManagedLayoutBox bodyBox) ||
            !_layoutEngine.TryGetBox(mainIndex, out ManagedLayoutBox mainBox) ||
            !_layoutEngine.TryGetBox(noteIndex, out ManagedLayoutBox noteBox))
            return false;

        Span<byte> documentHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> styleHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> layoutHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> paintHash = stackalloc byte[ManagedSha256.DigestSize];
        if (!_tree.TryCopyCanonicalHash(documentHash) || !css.TryCopyCanonicalStyleHash(styleHash) ||
            !_layoutEngine.TryCopyCanonicalLayoutHash(layoutHash) ||
            !_paintEngine.TryCopyCanonicalPaintHash(paintHash)) return false;

        return KernelLog.Write(PhasePrefix) && KernelLog.Write("PAINT_VERIFIED\r\n"u8) &&
               WritePaintHex("STATUS"u8, (ulong)progress.StatusCode) &&
               WritePaintHex("CONTENT_TYPE"u8, (ulong)ContentType.Length) &&
               WritePaintHex("CONTENT_ENCODING"u8, 1) &&
               WritePaintHex("ENCODED_BYTES"u8, (ulong)progress.EncodedBytesReceived) &&
               WritePaintHex("DECOMPRESSED_BYTES"u8, (ulong)progress.DecompressedBytesProduced) &&
               WritePaintHex("SCALARS"u8, (ulong)tree.TextScalarsUsed) &&
               WritePaintHex("TOKENS"u8, (ulong)progress.TokensEmitted) &&
               WritePaintHex("NODES"u8, (ulong)tree.NodeCount) &&
               WritePaintHex("ELEMENTS"u8, (ulong)tree.ElementCount) &&
               WritePaintHex("CSS_RULES"u8, (ulong)css.RulesParsed) &&
               WritePaintHex("CSS_SELECTOR_MATCHES"u8, (ulong)css.SelectorMatches) &&
               WritePaintHex("CSS_ELEMENTS_STYLED"u8, (ulong)css.ElementsStyled) &&
               WritePaintHex("VIEWPORT_WIDTH"u8, 800) &&
               WritePaintHex("VIEWPORT_HEIGHT"u8, 600) &&
               WritePaintHex("LAYOUT_BOXES"u8, (ulong)layoutTelemetry.LayoutBoxCount) &&
               WritePaintHex("LAYOUT_LINES"u8, (ulong)layoutTelemetry.LineCount) &&
               WritePaintHex("LAYOUT_TEXT_FRAGMENTS"u8, (ulong)layoutTelemetry.TextFragmentCount) &&
               WritePaintHex("LAYOUT_DISPLAY_NONE_SKIPS"u8, (ulong)layoutTelemetry.DisplayNoneSkips) &&
               WritePaintHex("PAINT_COMMANDS"u8, (ulong)paintTelemetry.CommandsEmitted) &&
               WritePaintHex("PAINT_COMMAND_CAPACITY"u8, (ulong)_paintEngine.CommandCapacity) &&
               WritePaintHex("PAINT_COMMAND_PEAK"u8, (ulong)paintTelemetry.PeakCommandUsage) &&
               WritePaintHex("PAINT_FILL_COMMANDS"u8, (ulong)paintTelemetry.FillCommands) &&
               WritePaintHex("PAINT_BORDER_COMMANDS"u8, (ulong)paintTelemetry.BorderCommands) &&
               WritePaintHex("PAINT_TEXT_COMMANDS"u8, (ulong)paintTelemetry.TextCommands) &&
               WritePaintHex("PAINT_IMAGE_PLACEHOLDERS"u8, (ulong)paintTelemetry.ImagePlaceholderCommands) &&
               WritePaintHex("PAINT_CLIP_PUSHES"u8, (ulong)paintTelemetry.ClipPushes) &&
               WritePaintHex("PAINT_CLIP_POPS"u8, (ulong)paintTelemetry.ClipPops) &&
               WritePaintHex("PAINT_CLIP_PEAK"u8, (ulong)paintTelemetry.PeakClipDepth) &&
               WritePaintHex("PAINT_CLIP_CAPACITY"u8, (ulong)_paintEngine.ClipDepthCapacity) &&
               WritePaintHex("PAINT_CULLED"u8, (ulong)paintTelemetry.OffscreenCommandsCulled) &&
               WritePaintHex("PAINT_TRANSPARENT_SKIPS"u8, (ulong)paintTelemetry.TransparentBackgroundsSkipped) &&
               WritePaintHex("PAINT_UNSUPPORTED_BORDERS"u8, (ulong)paintTelemetry.UnsupportedBorderStyles) &&
               WritePaintHex("PAINT_POSITIONED"u8, (ulong)paintTelemetry.PositionedCommands) &&
               WritePaintHex("PAINT_NEGATIVE_Z"u8, (ulong)paintTelemetry.NegativeZOrderCount) &&
               WritePaintHex("PAINT_NORMAL_Z"u8, (ulong)paintTelemetry.NormalZOrderCount) &&
               WritePaintHex("PAINT_POSITIVE_Z"u8, (ulong)paintTelemetry.PositiveZOrderCount) &&
               WritePaintLayoutRecord("BODY"u8, bodyBox) && WritePaintLayoutRecord("MAIN"u8, mainBox) &&
               WritePaintLayoutRecord("NOTE"u8, noteBox) && WritePaintCommandPrefix() &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE46_DOCUMENT_HASH_WORD=0x"u8, documentHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE46_STYLE_HASH_WORD=0x"u8, styleHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE46_LAYOUT_HASH_WORD=0x"u8, layoutHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE46_PAINT_HASH_WORD=0x"u8, paintHash) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE46_RESOURCE_COMPLETE\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE46_RESOURCE_PASS\r\n"u8);
    }

    private bool WritePaintLayoutRecord(ReadOnlySpan<byte> name, ManagedLayoutBox box)
    {
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_BOX_NAME=0x"u8, (ulong)name.Length) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_BOX_SOURCE=0x"u8, (ulong)box.SourceNodeIndex) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_BOX_X=0x"u8, (ulong)box.BorderBox.X) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_BOX_Y=0x"u8, (ulong)box.BorderBox.Y) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_BOX_WIDTH=0x"u8, (ulong)box.BorderBox.Width) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_BOX_HEIGHT=0x"u8, (ulong)box.BorderBox.Height);
    }

    private bool WritePaintHex(ReadOnlySpan<byte> name, ulong value) =>
        KernelLog.Write(PhasePrefix) && KernelLog.Write(name) &&
        KernelLog.WriteHexLine("=0x"u8, value);

    private bool WritePaintCommandPrefix()
    {
        int count = Math.Min(_paintEngine?.CommandsEmitted ?? 0, 16);
        for (int index = 0; index != count; ++index)
        {
            if (_paintEngine == null || !_paintEngine.TryGetCommand(index, out ManagedPaintCommand command) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_INDEX=0x"u8, (ulong)index) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_KIND=0x"u8, (ulong)command.Kind) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_BOX=0x"u8, (ulong)command.SourceBoxIndex) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_CLIP_DEPTH=0x"u8, command.ClipDepth) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_X=0x"u8, (ulong)command.Rect.X) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_Y=0x"u8, (ulong)command.Rect.Y) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_WIDTH=0x"u8, (ulong)command.Rect.Width) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_HEIGHT=0x"u8, (ulong)command.Rect.Height) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_COLOR=0x"u8, command.Color) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE46_COMMAND_Z=0x"u8, (ulong)command.ZIndex))
                return false;
        }
        return true;
    }

    private bool WriteLayoutRecord(ReadOnlySpan<byte> name, ManagedLayoutBox box)
    {
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_"u8, (ulong)name.Length) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_SOURCE=0x"u8, (ulong)box.SourceNodeIndex) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_X=0x"u8, (ulong)box.BorderBox.X) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_Y=0x"u8, (ulong)box.BorderBox.Y) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_WIDTH=0x"u8, (ulong)box.BorderBox.Width) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_HEIGHT=0x"u8, (ulong)box.BorderBox.Height) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_CONTENT_X=0x"u8, (ulong)box.ContentRect.X) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_CONTENT_Y=0x"u8, (ulong)box.ContentRect.Y) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_CONTENT_WIDTH=0x"u8, (ulong)box.ContentRect.Width) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE45_BOX_CONTENT_HEIGHT=0x"u8, (ulong)box.ContentRect.Height);
    }

    private bool WriteCssStyle(ReadOnlySpan<byte> name, ManagedHtmlNodeHandle node,
                               ManagedComputedStyle style)
    {
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLE_"u8, (ulong)name.Length) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLE_HANDLE=0x"u8,
                                      (ulong)node.Index) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLE_DISPLAY=0x"u8,
                                      (ulong)style.Display) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLE_COLOR=0x"u8,
                                      style.Color) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLE_BACKGROUND=0x"u8,
                                      style.BackgroundColor) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE44_STYLE_FONT_SIZE=0x"u8,
                                      (ulong)style.FontSize.Value);
    }

    private ManagedHtmlNodeHandle FindElementById(ReadOnlySpan<byte> expected)
    {
        for (int index = 0; index != _tree.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, _tree.Document.DocumentNode.Generation);
            if (_tree.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !_tree.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Id,
                                                  out ManagedHtmlAttributeView view)) continue;
            Span<uint> value = stackalloc uint[64];
            if (_tree.Document.TryCopyAttributeValue(node, view.Index, value,
                                                      out int length, out _) &&
                ScalarSpanEquals(value[..length], expected)) return node;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private ManagedHtmlNodeHandle FindElementByClass(ReadOnlySpan<byte> expected)
    {
        for (int index = 0; index != _tree.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, _tree.Document.DocumentNode.Generation);
            if (_tree.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !_tree.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Class,
                                                  out ManagedHtmlAttributeView view)) continue;
            Span<uint> value = stackalloc uint[128];
            if (!_tree.Document.TryCopyAttributeValue(node, view.Index, value,
                                                      out int length, out _)) continue;
            int position = 0;
            while (position < length)
            {
                while (position < length && value[position] <= 0x7F &&
                       IsCssWhitespace((byte)value[position])) ++position;
                int begin = position;
                while (position < length && (value[position] > 0x7F ||
                                             !IsCssWhitespace((byte)value[position]))) ++position;
                if (ScalarSpanEquals(value[begin..position], expected)) return node;
            }
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static bool ScalarSpanEquals(ReadOnlySpan<uint> value, ReadOnlySpan<byte> expected)
    {
        if (value.Length != expected.Length) return false;
        for (int index = 0; index != value.Length; ++index)
            if (value[index] > 0x7F || (byte)value[index] != expected[index]) return false;
        return true;
    }

    private static bool IsCssWhitespace(byte value) =>
        value == 0x20 || value == 0x09 || value == 0x0A || value == 0x0C || value == 0x0D;

    private static bool WriteDigest(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> digest)
    {
        for (int index = 0; index != digest.Length; index += 4)
        {
            uint word = ((uint)digest[index] << 24) | ((uint)digest[index + 1] << 16) |
                        ((uint)digest[index + 2] << 8) | digest[index + 3];
            if (!KernelLog.WriteHexLine(prefix, word)) return false;
        }
        return true;
    }

    private bool WriteFailure(ManagedHtmlProgressSnapshot progress)
    {
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_FAILURE=0x"u8,
                               (ulong)progress.FailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TEXT_FAILURE=0x"u8,
                               (ulong)progress.TextFailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TOKENIZER_FAILURE=0x"u8,
                               (ulong)progress.TokenizerFailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_TREE_FAILURE=0x"u8,
                               (ulong)progress.TreeBuilderFailureReason);
        if (_capacityControl &&
            progress.FailureReason == ManagedHtmlFailureReason.NodeCapacityExceeded &&
            _tree.FailureReason == ManagedHtmlTreeBuilderFailureReason.NodeCapacityExceeded &&
            _tree.Validate(out ManagedHtmlDocumentValidationFailureReason validation) &&
            validation == ManagedHtmlDocumentValidationFailureReason.None)
        {
            KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_CAPACITY_CONTROL_VALIDATED\r\n"u8);
            KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_CAPACITY_NEGATIVE_PASS\r\n"u8);
        }
        return false;
    }

    private bool WriteTraversalPrefix()
    {
        Span<ManagedHtmlNodeHandle> pending = stackalloc ManagedHtmlNodeHandle[128];
        Span<byte> depths = stackalloc byte[128];
        Span<uint> text = stackalloc uint[256];
        int pendingCount = 1;
        pending[0] = _tree.DocumentRoot;
        depths[0] = 0;
        int emitted = 0;
        while (pendingCount != 0 && emitted != 16)
        {
            --pendingCount;
            ManagedHtmlNodeHandle node = pending[pendingCount];
            byte depth = depths[pendingCount];
            ManagedHtmlNodeKind kind = _tree.Document.GetNodeKind(node);
            if (!KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PREFIX_DEPTH=0x"u8, depth) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PREFIX_KIND=0x"u8, (ulong)kind) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PREFIX_TAG=0x"u8, (ulong)_tree.Document.GetElementTag(node)) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PREFIX_ATTRIBUTES=0x"u8, (ulong)_tree.Document.GetAttributeCount(node)) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PREFIX_TEXT_LENGTH=0x"u8, (ulong)_tree.Document.GetTextLength(node)))
                return false;
            if (kind == ManagedHtmlNodeKind.Text)
            {
                if (!_tree.Document.TryCopyText(node, text, out int length) || length == 0 ||
                    !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE43_PREFIX_TEXT_SCALAR=0x"u8, text[0]))
                    return false;
            }
            ++emitted;
            for (ManagedHtmlNodeHandle child = _tree.Document.GetLastChild(node);
                 child != ManagedHtmlNodeHandle.Invalid;
                 child = _tree.Document.GetPreviousSibling(child))
            {
                if (pendingCount == pending.Length) return false;
                pending[pendingCount] = child;
                depths[pendingCount++] = (byte)(depth + 1);
            }
        }
        return KernelLog.WriteHexLine(
            "GXOS_NET10:MANAGED_HTTPS_PHASE43_PREFIX_COUNT=0x"u8, (ulong)emitted);
    }

    private static byte[] CreateEntropy()
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
        public bool IsAvailable => _bytes.Length != 0;
        public bool TryFill(Span<byte> destination)
        {
            for (int index = 0; index != destination.Length; ++index)
                destination[index] = _bytes[_offset++ % _bytes.Length];
            return true;
        }
    }
}

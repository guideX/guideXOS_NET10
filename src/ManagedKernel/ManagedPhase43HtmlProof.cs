using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Deterministic Phase 43 proof.  This is the first guest proof that retains a
   managed HTML document.  The document is a fixed-capacity arena and the
   response still traverses the Phase 41/42 HTTP, gzip, MIME, UTF-8, and
   tokenizer gates before the tree builder sees a scalar. */
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
        0xC5, 0x1E, 0x52, 0x24, 0xE6, 0x91, 0x9E, 0x31,
        0xA2, 0xD8, 0x83, 0x5E, 0xD6, 0x74, 0xED, 0x08,
        0x8C, 0x5F, 0x64, 0x4A, 0xC8, 0xDD, 0xA3, 0x94,
        0xAE, 0x5D, 0x16, 0x2C, 0x05, 0x06, 0x24, 0x40
    };
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> Path => "/phase43/gzip"u8;
    private static ReadOnlySpan<byte> ContentType => "text/html; charset=utf-8"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHtmlResourceRequest _resource;
    private readonly ManagedHtmlTreeBuilder _tree;
    private readonly bool _capacityControl;
    private bool _bodyReceivedLogged;
    private bool _pauseObserved;
    private int _stablePausedPolls;

    internal ManagedPhase43HtmlProof(ManagedNetworkService service,
                                     bool capacityControl = false)
    {
        _service = service;
        _capacityControl = capacityControl;
        _tree = capacityControl
            ? new ManagedHtmlTreeBuilder(new ManagedHtmlDocumentArenaOptions(
                80, 65_536, 2_048, 16_384, 128))
            : new ManagedHtmlTreeBuilder();
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
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_READY\r\n"u8) ||
            _resource.BeginGet(Hostname, Path, _tree) != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_STARTED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_REQUEST_STARTED\r\n"u8))
            return false;

        for (int poll = 0; poll != 131_072; ++poll)
        {
            NetworkOperationResult result = _resource.Poll();
            ManagedHtmlProgressSnapshot progress = _resource.Progress;
            if (result == NetworkOperationResult.Failed ||
                _resource.State == ManagedResourceState.Failed)
                return WriteFailure(progress);
            if (!_bodyReceivedLogged && progress.TextInputBytesConsumed != 0 &&
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE43_RESOURCE_BODY_RECEIVED\r\n"u8))
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
        Span<ManagedHtmlNodeHandle> pending = stackalloc ManagedHtmlNodeHandle[32];
        Span<byte> depths = stackalloc byte[32];
        Span<uint> text = stackalloc uint[1];
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

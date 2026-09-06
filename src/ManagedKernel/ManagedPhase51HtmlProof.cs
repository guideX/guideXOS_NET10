using System;

namespace GuideXOS.Net10.ManagedKernel;

/* The authoritative Phase 51 guest proof deliberately uses the page
   orchestrator.  The host serves one HTML response followed by two sequential
   CSS responses; this class only drives the bounded Poll state machine and
   verifies the resulting style, layout, raster, and GOP presentation. */
internal sealed class ManagedPhase51HtmlProof
{
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> PageUrl =>
        "https://www.example.com/phase51/index.html"u8;

    private static ManagedCssArenaOptions CssArenas => new(
        stylesheetCapacity: 8,
        ruleCapacity: 64,
        selectorCapacity: 128,
        selectorStepCapacity: 256,
        declarationCapacity: 256,
        computedStyleCapacity: 128,
        externalStylesheetCapacity: 4);

    private readonly ManagedNetworkService _service;
    private readonly ManagedPageResourceOrchestrator _page;

    internal ManagedPhase51HtmlProof(ManagedNetworkService service)
    {
        _service = service;
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_CONSTRUCTOR_BEGIN\r\n"u8);
        ManagedSecureRandom random = new(new FixedEntropy(CreateEntropy()));
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_RANDOM_READY\r\n"u8);
        _page = new ManagedPageResourceOrchestrator(
            service, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random,
            new ManagedPageResourceOptions(CssArenas,
                                           externalStylesheetLimit: 2,
                                           viewportWidth: 320,
                                           viewportHeight: 180),
            // Keep each proof response within the bounded compatibility body
            // buffer so the peer-owned Connection: close path is exercised.
            maximumEntityLength: ManagedHttpLimits.MaximumBodyCapacity,
            maximumDecodedResourceLength: 16 * 1024);
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_PAGE_READY\r\n"u8);
    }

    internal bool TryRun()
    {
        if (!_service.GetStatus().DhcpBound || !_service.GetStatus().Configured ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_READY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_BEGIN_GET\r\n"u8))
            return false;
        NetworkOperationResult begin = _page.BeginGetUrl(PageUrl);
        if (begin != NetworkOperationResult.Started)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE51_BEGIN_GET_FAILURE=0x"u8,
                (ulong)begin);
            return false;
        }
        ManagedNetworkServiceBackend.LiveEthernet?.EnablePhase34Polling();
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_STARTED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_REQUEST_STARTED\r\n"u8))
            return false;

        bool bodyReceivedLogged = false;
        for (int poll = 0; poll != 131_072; ++poll)
        {
            NetworkOperationResult result = _page.Poll();
            if (!bodyReceivedLogged &&
                _page.DocumentResource.ResponseBodyComplete)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_BODY_RECEIVED\r\n"u8))
                    return false;
                bodyReceivedLogged = true;
                ManagedNetworkServiceBackend.LiveEthernet?.EnablePhase34Polling();
            }
            if (result == NetworkOperationResult.Failed ||
                _page.State == ManagedPageResourceState.Failed)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE51_FAILURE_REASON=0x"u8,
                    (ulong)_page.FailureReason);
                KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE51_CSS_FAILURE=0x"u8,
                    (ulong)_page.CssFailureReason);
                return false;
            }
            if (_page.State == ManagedPageResourceState.Complete)
                return FinishSuccess();
        }
        KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_POLL_LIMIT_FAILURE\r\n"u8);
        return false;
    }

    private bool FinishSuccess()
    {
        ManagedPageResourceTelemetry pageTelemetry = _page.Telemetry;
        if (_page.Document.NodeCount == 0 ||
            pageTelemetry.ExternalStylesheetsEncountered != 2 ||
            pageTelemetry.ExternalStylesheetRequestsStarted != 2 ||
            pageTelemetry.ExternalStylesheetsLoaded != 2 ||
            pageTelemetry.EmbeddedStylesheetsParsed != 1 ||
            pageTelemetry.AlternateStylesheetsIgnored != 0 ||
            _page.Layout == null || _page.Paint == null ||
            _page.Rasterizer == null || !_page.HasFramebuffer)
            return false;

        ManagedHtmlNodeHandle target = FindElementById("target"u8);
        if (target == ManagedHtmlNodeHandle.Invalid ||
            !_page.Styles.TryGetComputedStyle(target, out ManagedComputedStyle style) ||
            style.Color != 0xFF0000FFU ||
            style.BackgroundColor != 0xFF123456U ||
            style.Width.Value != 18_000 ||
            style.PaddingTop.Value != 800 ||
            style.BorderWidth.Value != 300 ||
            style.BorderStyle != ManagedCssBorderStyle.Solid ||
            !_page.Layout.TryGetBoxForNode(target, out int targetBoxIndex) ||
            !_page.Layout.TryGetBox(targetBoxIndex, out ManagedLayoutBox targetBox) ||
            targetBox.BorderBox.Width <= 180 || targetBox.BorderBox.Height <= 74)
            return false;

        Span<byte> documentHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> styleHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> layoutHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> paintHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> framebufferHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> fontHash = stackalloc byte[ManagedSha256.DigestSize];
        if (!_page.Document.TryCopyCanonicalHash(documentHash) ||
            !_page.Styles.TryCopyCanonicalStyleHash(styleHash) ||
            !_page.Layout.TryCopyCanonicalLayoutHash(layoutHash) ||
            !_page.Paint.TryCopyCanonicalPaintHash(paintHash) ||
            !_page.Rasterizer.TryCopyFramebufferHash(framebufferHash) ||
            !ManagedPhase48FontRegistry.Instance.TryCopySemanticHash(fontHash))
            return false;

        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_COMPLETE\r\n"u8) ||
            !WritePageTelemetry(pageTelemetry) ||
            !WriteExternalTelemetry(0) || !WriteExternalTelemetry(1) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_TARGET_NODE=0x"u8,
                                    (ulong)target.Index) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_TARGET_COLOR=0x"u8,
                                    style.Color) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_TARGET_BACKGROUND=0x"u8,
                                    style.BackgroundColor) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_TARGET_WIDTH=0x"u8,
                                    (ulong)style.Width.Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_TARGET_PADDING=0x"u8,
                                    (ulong)style.PaddingTop.Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_TARGET_BORDER=0x"u8,
                                    (ulong)style.BorderWidth.Value) ||
            !WriteLayoutBox(targetBox) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_DOCUMENT_HASH_WORD=0x"u8,
                         documentHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_STYLE_HASH_WORD=0x"u8,
                         styleHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_LAYOUT_HASH_WORD=0x"u8,
                         layoutHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_PAINT_HASH_WORD=0x"u8,
                         paintHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_FRAMEBUFFER_HASH_WORD=0x"u8,
                         framebufferHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_FONT_SEMANTIC_HASH_WORD=0x"u8,
                         fontHash) ||
            !WriteMappedPixels(targetBox))
            return false;

        if (!Present()) return false;
        return KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_RESOURCE_PASS\r\n"u8);
    }

    private bool WritePageTelemetry(ManagedPageResourceTelemetry telemetry) =>
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_SOURCES_VISITED=0x"u8,
                               (ulong)telemetry.StyleSourcesVisited) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_ENCOUNTERED=0x"u8,
                               (ulong)telemetry.ExternalStylesheetsEncountered) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_STARTED=0x"u8,
                               (ulong)telemetry.ExternalStylesheetRequestsStarted) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_LOADED=0x"u8,
                               (ulong)telemetry.ExternalStylesheetsLoaded) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EMBEDDED_PARSED=0x"u8,
                               (ulong)telemetry.EmbeddedStylesheetsParsed) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_REDIRECTS=0x"u8,
                               (ulong)telemetry.ExternalStylesheetRedirects) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_ENCODED_BYTES=0x"u8,
                               (ulong)telemetry.ExternalEncodedBytes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_EXTERNAL_DECODED_BYTES=0x"u8,
                               (ulong)telemetry.ExternalDecodedBytes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_DOCUMENT_SCALARS=0x"u8,
                               (ulong)telemetry.DocumentScalars) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_STYLESHEET_SCALARS=0x"u8,
                               (ulong)telemetry.StylesheetScalars);

    private bool WriteExternalTelemetry(int index)
    {
        if (!_page.TryGetExternalStylesheetTelemetry(index,
                                                       out ManagedExternalStylesheetTelemetry telemetry))
            return false;
        Span<byte> digest = stackalloc byte[ManagedSha256.DigestSize];
        if (!telemetry.TryCopyDecodedDigest(digest)) return false;
        ReadOnlySpan<byte> prefix = index == 0
            ? "GXOS_NET10:MANAGED_HTTPS_PHASE51_STYLESHEET_A_"u8
            : "GXOS_NET10:MANAGED_HTTPS_PHASE51_STYLESHEET_B_"u8;
        return KernelLog.WriteHexLine(Join(prefix, "NODE=0x"u8), (ulong)telemetry.NodeIndex) &&
               KernelLog.WriteHexLine(Join(prefix, "STATUS=0x"u8), (ulong)telemetry.StatusCode) &&
               KernelLog.WriteHexLine(Join(prefix, "MIME=0x"u8), (ulong)telemetry.MimeClassification) &&
               KernelLog.WriteHexLine(Join(prefix, "CHARSET=0x"u8), (ulong)telemetry.Charset) &&
               KernelLog.WriteHexLine(Join(prefix, "CHARSET_SOURCE=0x"u8), (ulong)telemetry.CharsetSource) &&
               KernelLog.WriteHexLine(Join(prefix, "CONTENT_ENCODING=0x"u8), (ulong)telemetry.ContentEncoding) &&
               KernelLog.WriteHexLine(Join(prefix, "ENCODED_BYTES=0x"u8), (ulong)telemetry.EncodedBytes) &&
               KernelLog.WriteHexLine(Join(prefix, "DECODED_BYTES=0x"u8), (ulong)telemetry.DecodedBytes) &&
               KernelLog.WriteHexLine(Join(prefix, "SCALARS=0x"u8), (ulong)telemetry.Scalars) &&
               KernelLog.WriteHexLine(Join(prefix, "RULES=0x"u8), (ulong)telemetry.RulesAdded) &&
               KernelLog.WriteHexLine(Join(prefix, "DECLARATIONS=0x"u8), (ulong)telemetry.DeclarationsAdded) &&
               WriteDigest(Join(prefix, "DIGEST_WORD=0x"u8), digest);
    }

    private bool WriteLayoutBox(ManagedLayoutBox box) =>
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_LAYOUT_X=0x"u8,
                               (ulong)box.BorderBox.X) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_LAYOUT_Y=0x"u8,
                               (ulong)box.BorderBox.Y) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_LAYOUT_W=0x"u8,
                               (ulong)box.BorderBox.Width) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_LAYOUT_H=0x"u8,
                               (ulong)box.BorderBox.Height) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_CONTENT_X=0x"u8,
                               (ulong)box.ContentRect.X) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_CONTENT_Y=0x"u8,
                               (ulong)box.ContentRect.Y) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_CONTENT_W=0x"u8,
                               (ulong)box.ContentRect.Width) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_CONTENT_H=0x"u8,
                               (ulong)box.ContentRect.Height);

    private bool WriteMappedPixels(ManagedLayoutBox box)
    {
        ManagedFramebuffer framebuffer = _page.Framebuffer;
        int x0 = Math.Max(0, box.BorderBox.X);
        int y0 = Math.Max(0, box.BorderBox.Y);
        int x1 = Math.Min(framebuffer.Width - 1, box.BorderBox.Right - 1);
        int y1 = Math.Min(framebuffer.Height - 1, box.BorderBox.Bottom - 1);
        if (x1 < x0 || y1 < y0) return false;
        for (int sample = 0; sample != 12; ++sample)
        {
            int x = x0 + ((x1 - x0) * sample) / 11;
            int y = y0 + ((y1 - y0) * ((sample * 7) % 12)) / 11;
            if (!framebuffer.TryGetPixel(x, y, out uint pixel) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE51_MAPPED_PIXEL_X=0x"u8,
                    (ulong)x) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE51_MAPPED_PIXEL_Y=0x"u8,
                    (ulong)y) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE51_MAPPED_PIXEL_COLOR=0x"u8,
                    pixel)) return false;
        }
        return true;
    }

    private bool Present()
    {
        if (!ManagedKernelContract.FramebufferInstalled) return false;
        GxManagedKernelFramebufferV1 descriptor =
            ManagedKernelContract.FramebufferDescriptor;
        ManagedPhysicalFramebufferDescriptor physical =
            new(descriptor.FramebufferBase, descriptor.FramebufferSize,
                descriptor.Width, descriptor.Height, descriptor.PixelsPerScanLine,
                descriptor.BytesPerPixel,
                (ManagedPhysicalFramebufferPixelFormat)descriptor.PixelFormat,
                descriptor.RedMask, descriptor.GreenMask, descriptor.BlueMask,
                descriptor.ReservedMask);
        ManagedFramebufferPresenter presenter = new();
        ManagedFramebufferDestination destination =
            ManagedFramebufferDestination.FromPhysical(
                (nuint)descriptor.FramebufferBase, (nuint)descriptor.FramebufferSize);
        int x = descriptor.Width > (uint)_page.Framebuffer.Width
            ? (int)((descriptor.Width - (uint)_page.Framebuffer.Width) / 2) : 0;
        int y = descriptor.Height > (uint)_page.Framebuffer.Height
            ? (int)((descriptor.Height - (uint)_page.Framebuffer.Height) / 2) : 0;
        ManagedFramebuffer source = _page.Framebuffer;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_PRESENTATION_BEGIN\r\n"u8) ||
            !presenter.TryPresent(in source, in physical, destination, x, y))
            return false;
        Span<byte> sourceHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> destinationHash = stackalloc byte[ManagedSha256.DigestSize];
        ManagedFramebufferPresentationTelemetry telemetry = presenter.Telemetry;
        return presenter.TryCopySourceHashBefore(sourceHash) &&
               presenter.TryCopyDestinationHash(destinationHash) &&
               telemetry.SourceHashUnchanged &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_PRESENTED_PIXELS=0x"u8,
                                      (ulong)telemetry.PixelsWritten) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_CLIPPED_PIXELS=0x"u8,
                                      (ulong)telemetry.PixelsClipped) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_PRESENTATION_X=0x"u8,
                                      (ulong)x) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE51_PRESENTATION_Y=0x"u8,
                                      (ulong)y) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_SOURCE_FRAMEBUFFER_HASH_WORD=0x"u8,
                           sourceHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE51_DESTINATION_HASH_WORD=0x"u8,
                           destinationHash) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_SOURCE_UNCHANGED=1\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_GOP_PRESENT_PASS\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE51_VISIBLE_PAGE_PASS\r\n"u8);
    }

    private ManagedHtmlNodeHandle FindElementById(ReadOnlySpan<byte> expected)
    {
        Span<uint> value = stackalloc uint[64];
        ManagedHtmlDocument document = _page.Document;
        for (int index = 0; index != document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node =
                new(index, document.DocumentNode.Generation);
            if (document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !document.TryFindAttribute(node, ManagedHtmlAttributeName.Id,
                                            out ManagedHtmlAttributeView view) ||
                !document.TryCopyAttributeValue(node, view.Index, value,
                                                out int length, out bool hasValue) ||
                !hasValue || length != expected.Length)
                continue;
            bool equal = true;
            for (int offset = 0; offset != length; ++offset)
                equal &= value[offset] <= 0x7F && (byte)value[offset] == expected[offset];
            if (equal) return node;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static bool WriteDigest(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> digest)
    {
        for (int offset = 0; offset != digest.Length; offset += 4)
        {
            uint word = (uint)digest[offset] |
                        ((uint)digest[offset + 1] << 8) |
                        ((uint)digest[offset + 2] << 16) |
                        ((uint)digest[offset + 3] << 24);
            if (!KernelLog.WriteHexLine(prefix, word)) return false;
        }
        return true;
    }

    private static byte[] CreateEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
    }

    private static byte[] Join(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        byte[] result = new byte[left.Length + right.Length];
        left.CopyTo(result);
        right.CopyTo(result.AsSpan(left.Length));
        return result;
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

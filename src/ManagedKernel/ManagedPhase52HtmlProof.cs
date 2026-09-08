using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Phase 52's guest witness keeps the Phase 51 multi-resource page shape and
   adds one sequential image resource.  The PNG itself is supplied by the
   deterministic host backend; no image bytes are compiled into the payload. */
internal sealed class ManagedPhase52HtmlProof : IManagedPageTerminalProof
{
    private static ReadOnlySpan<byte> PageUrl =>
        "https://www.example.com/phase52/index.html"u8;

    private static ManagedCssArenaOptions CssArenas => new(
        stylesheetCapacity: 8,
        ruleCapacity: 96,
        selectorCapacity: 160,
        selectorStepCapacity: 320,
        declarationCapacity: 320,
        computedStyleCapacity: 160,
        externalStylesheetCapacity: 4);

    private readonly ManagedNetworkService _service;
    private readonly ManagedPageResourceOrchestrator _page;

    internal ManagedPhase52HtmlProof(ManagedNetworkService service)
    {
        _service = service;
        ManagedSecureRandom random = new(new FixedEntropy(CreateEntropy()));
        _page = new ManagedPageResourceOrchestrator(
            service, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random,
            new ManagedPageResourceOptions(CssArenas,
                                           externalStylesheetLimit: 2,
                                           viewportWidth: 320,
                                           viewportHeight: 180,
                                           imageLimit: 4,
                                           maximumImageWidth: 256,
                                           maximumImageHeight: 256,
                                           imagePixelBudget: 65_536),
            maximumEntityLength: 64 * 1024,
            maximumDecodedResourceLength: 64 * 1024,
            phase52Markers: true,
            terminalProof: this);
    }

    private bool _terminalCompleted;

    bool IManagedPageTerminalProof.TryComplete(in ManagedFramebuffer framebuffer)
    {
        _terminalCompleted = FinishSuccess(in framebuffer);
        return _terminalCompleted;
    }

    internal bool TryRun()
    {
        if (!_service.GetStatus().DhcpBound || !_service.GetStatus().Configured ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_BEGIN\r\n"u8))
            return false;
        NetworkOperationResult begin = _page.BeginGetUrl(PageUrl);
        if (begin != NetworkOperationResult.Started) return false;
        ManagedNetworkServiceBackend.LiveEthernet?.EnablePhase34Polling();
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_RESOURCE_STARTED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_REQUEST_STARTED\r\n"u8))
            return false;

        bool bodyReceivedLogged = false;
        for (int poll = 0; poll != 131_072; ++poll)
        {
            if (_terminalCompleted) return true;
            NetworkOperationResult result = _page.Poll();
            if (_terminalCompleted) return true;
            if (!bodyReceivedLogged && _page.DocumentResource.ResponseBodyComplete)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE52_RESOURCE_BODY_RECEIVED\r\n"u8))
                    return false;
                bodyReceivedLogged = true;
                ManagedNetworkServiceBackend.LiveEthernet?.EnablePhase34Polling();
            }
            if (result == NetworkOperationResult.Failed ||
                _page.State == ManagedPageResourceState.Failed)
            {
                if (TryReportBadPngCrcControl())
                    return false;
                return false;
            }
        }
        return false;
    }

    private bool TryReportBadPngCrcControl()
    {
        if (_page.ImagePngFailureReason != ManagedPngFailureReason.InvalidChunkCrc ||
            _page.ImageResource is not ManagedResourceRequest imageResource)
            return false;
        ManagedResourceProgressSnapshot image = imageResource.Progress;
        ManagedPageResourceTelemetry page = _page.Telemetry;
        if (image.StatusCode != 200 ||
            imageResource.RequiredMime != ManagedMimeClassification.Png ||
            image.ContentTypeState != ManagedHttpContentTypeState.Available ||
            page.ImageNodes != 1 || page.ImageRequestsStarted != 1 ||
            page.ImagesLoaded != 0 || _page.Images.Count != 0)
            return false;
        KernelLog.WriteHexLine(
            "GXOS_NET10:MANAGED_HTTPS_PHASE52_BAD_PNG_CRC_REASON=0x"u8,
            (ulong)ManagedPngFailureReason.InvalidChunkCrc);
        KernelLog.WriteHexLine(
            "GXOS_NET10:MANAGED_HTTPS_PHASE52_BAD_PNG_CRC_IMAGE_SLOT_COUNT=0x"u8,
            (ulong)_page.Images.Count);
        return KernelLog.Write(
            "GXOS_NET10:MANAGED_HTTPS_PHASE52_BAD_PNG_CRC_CONTROL_PASS\r\n"u8);
    }

    private bool FinishSuccess(in ManagedFramebuffer framebuffer)
    {
        uint[]? sourceStorage = framebuffer.BackingStorage;
        if (sourceStorage == null) return false;
        ManagedPageResourceTelemetry pageTelemetry = _page.Telemetry;
        if (pageTelemetry.ImageNodes != 1 ||
            pageTelemetry.ImageRequestsStarted != 1 ||
            pageTelemetry.ImagesLoaded != 1 || _page.Layout == null ||
            _page.Paint == null || _page.Rasterizer == null)
            return FinishFailure(1);

        ManagedHtmlNodeHandle imageNode = FindElementById("hero"u8);
        if (imageNode == ManagedHtmlNodeHandle.Invalid ||
            !_page.Images.TryGetForSourceNode(imageNode.Index, out ManagedImageHandle handle) ||
            !_page.Images.TryGetDescriptor(handle, out ManagedPageImageDescriptor descriptor) ||
            descriptor.Width != 48 || descriptor.Height != 32)
            return FinishFailure(2);
        if (!_page.Layout.TryGetBoxForNode(imageNode, out int imageBoxIndex) ||
            !_page.Layout.TryGetBox(imageBoxIndex, out ManagedLayoutBox imageBox) ||
            imageBox.BorderBox.Width != 96 || imageBox.BorderBox.Height != 64)
            return FinishFailure(3);

        ManagedPaintTelemetry paintTelemetry = _page.Paint.Telemetry;
        ManagedRasterTelemetry rasterTelemetry = _page.Rasterizer.Telemetry;
        if (paintTelemetry.ImageCommands != 1 || paintTelemetry.ImagePlaceholderCommands != 0 ||
            rasterTelemetry.ImageCommands != 1 || rasterTelemetry.ImagePixelsWritten == 0)
            return FinishFailure(4);
        if (!_page.TryGetImageTelemetry(0, out ManagedImageResourceTelemetry imageTelemetry) ||
            imageTelemetry.StatusCode < 200 || imageTelemetry.StatusCode >= 300 ||
            imageTelemetry.MimeClassification != ManagedMimeClassification.Png ||
            imageTelemetry.Width != 48 || imageTelemetry.Height != 32 ||
            imageTelemetry.ColorType != 6 || imageTelemetry.BitDepth != 8 ||
            imageTelemetry.IdatChunks != 3 || imageTelemetry.DecodedPixels != 1_536 ||
            imageTelemetry.FilterNone == 0 || imageTelemetry.FilterSub == 0 ||
            imageTelemetry.FilterUp == 0 || imageTelemetry.FilterAverage == 0 ||
            imageTelemetry.FilterPaeth == 0)
            return FinishFailure(5);

        Span<byte> documentHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> styleHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> layoutHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> paintHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> framebufferHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> pngHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> pixelHash = stackalloc byte[ManagedSha256.DigestSize];
        if (!_page.Document.TryCopyCanonicalHash(documentHash) ||
            !_page.Styles.TryCopyCanonicalStyleHash(styleHash) ||
            !_page.Layout.TryCopyCanonicalLayoutHash(layoutHash) ||
            !_page.Paint.TryCopyCanonicalPaintHash(paintHash) ||
            !_page.Rasterizer.TryCopyFramebufferHash(framebufferHash) ||
            !imageTelemetry.TryCopyPngDigest(pngHash) ||
            !imageTelemetry.TryCopyPixelDigest(pixelHash))
            return FinishFailure(6);

        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_PNG_HEADER_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_PNG_DECODE_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_LAYOUT_PASS\r\n"u8) ||
            !WritePageTelemetry(pageTelemetry) ||
            !WriteImageTelemetry(imageTelemetry) ||
            !WriteLayout(imageBox) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_DOCUMENT_HASH_WORD=0x"u8,
                         documentHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_STYLE_HASH_WORD=0x"u8,
                         styleHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_LAYOUT_HASH_WORD=0x"u8,
                         layoutHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_PAINT_HASH_WORD=0x"u8,
                         paintHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_FRAMEBUFFER_HASH_WORD=0x"u8,
                         framebufferHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_PNG_HASH_WORD=0x"u8,
                         pngHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_PIXEL_HASH_WORD=0x"u8,
                         pixelHash) ||
            !WriteImageSamples(imageBox, descriptor, handle, in framebuffer))
            return FinishFailure(7);
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_RASTER_PASS\r\n"u8))
            return FinishFailure(8);
        if (!Present(in framebuffer, sourceStorage))
            return FinishFailure(9);
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_VISIBLE_IMAGE_PASS\r\n"u8))
            return FinishFailure(10);
        return true;
    }

    private static bool FinishFailure(ulong code) => false;

    private bool WritePageTelemetry(ManagedPageResourceTelemetry telemetry) =>
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_NODES=0x"u8,
                               (ulong)telemetry.ImageNodes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_REQUESTS=0x"u8,
                               (ulong)telemetry.ImageRequestsStarted) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGES_LOADED=0x"u8,
                               (ulong)telemetry.ImagesLoaded) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_CURSOR=0x"u8,
                               (ulong)telemetry.ImageCursor);

    private bool WriteImageTelemetry(ManagedImageResourceTelemetry telemetry) =>
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_STATUS=0x"u8,
                               (ulong)telemetry.StatusCode) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_MIME=0x"u8,
                               (ulong)telemetry.MimeClassification) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_REDIRECTS=0x"u8,
                               (ulong)telemetry.RedirectCount) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_WIRE_BYTES=0x"u8,
                               (ulong)telemetry.WireBytes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_ENTITY_BYTES=0x"u8,
                               (ulong)telemetry.EntityBytes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_PNG_BYTES=0x"u8,
                               (ulong)telemetry.PngBytes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_WIDTH=0x"u8,
                               (ulong)telemetry.Width) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_HEIGHT=0x"u8,
                               (ulong)telemetry.Height) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_IDAT_CHUNKS=0x"u8,
                               (ulong)telemetry.IdatChunks) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_IDAT_BYTES=0x"u8,
                               (ulong)telemetry.IdatCompressedBytes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_INFLATED_BYTES=0x"u8,
                               (ulong)telemetry.InflatedBytes) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_DECODED_PIXELS=0x"u8,
                               (ulong)telemetry.DecodedPixels) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_FILTER_NONE=0x"u8,
                               (ulong)telemetry.FilterNone) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_FILTER_SUB=0x"u8,
                               (ulong)telemetry.FilterSub) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_FILTER_UP=0x"u8,
                               (ulong)telemetry.FilterUp) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_FILTER_AVERAGE=0x"u8,
                               (ulong)telemetry.FilterAverage) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_FILTER_PAETH=0x"u8,
                               (ulong)telemetry.FilterPaeth) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_SLOT=0x"u8,
                               (ulong)telemetry.Slot);

    private bool WriteLayout(ManagedLayoutBox box) =>
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_LAYOUT_X=0x"u8,
                               (ulong)box.BorderBox.X) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_LAYOUT_Y=0x"u8,
                               (ulong)box.BorderBox.Y) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_LAYOUT_W=0x"u8,
                               (ulong)box.BorderBox.Width) &&
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_LAYOUT_H=0x"u8,
                               (ulong)box.BorderBox.Height);

    private bool WriteImageSamples(ManagedLayoutBox box,
                                   ManagedPageImageDescriptor descriptor,
                                   ManagedImageHandle handle,
                                   in ManagedFramebuffer framebuffer)
    {
        (int x, int y, uint expected)[] samples =
        {
            (0, 0, 0xFFFF0000U), (6, 4, 0xFFFF0000U),
            (12, 0, 0xFF00FF00U), (18, 4, 0xFF00FF00U),
            (24, 0, 0xFF0000FFU), (30, 4, 0xFF0000FFU),
            (36, 0, 0xFFFFFFFFU), (42, 4, 0xFFFFFFFFU),
            (0, 8, 0xFF000000U), (23, 15, 0xFF000000U),
            (0, 16, 0xFF123456U), (47, 23, 0xFF123456U),
            (0, 24, 0x80FF0000U), (23, 31, 0x80FF0000U),
            (24, 24, 0x00000000U), (47, 31, 0x00000000U)
        };
        for (int index = 0; index != samples.Length; ++index)
        {
            (int sourceX, int sourceY, uint expected) = samples[index];
            if (!GetMappedPixel(box, descriptor, sourceX, sourceY,
                                framebuffer, out uint pixel)) return false;
            if (!KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_SAMPLE_SOURCE=0x"u8,
                    (ulong)((sourceY << 16) | sourceX)) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_SAMPLE_EXPECTED=0x"u8,
                    expected) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE52_IMAGE_SAMPLE_FRAMEBUFFER=0x"u8,
                    pixel)) return false;
            if (index == 0 && pixel != expected) return false;
        }
        return _page.Images.TryReadPixel(handle, 0, 0, out uint first) &&
               first == 0xFFFF0000U;
    }

    private static bool GetMappedPixel(ManagedLayoutBox box,
                                       ManagedPageImageDescriptor descriptor,
                                       int sourceX, int sourceY,
                                       ManagedFramebuffer framebuffer,
                                       out uint pixel)
    {
        int x = box.ContentRect.X + sourceX * box.ContentRect.Width / descriptor.Width;
        int y = box.ContentRect.Y + sourceY * box.ContentRect.Height / descriptor.Height;
        return framebuffer.TryGetPixel(x, y, out pixel);
    }

    private bool Present(in ManagedFramebuffer source, uint[] sourceStorage)
    {
        if (!ManagedKernelContract.FramebufferInstalled)
            return false;
        
        GxManagedKernelFramebufferV1 descriptor = ManagedKernelContract.FramebufferDescriptor;
        int sourceWidth = source.Width;
        int sourceHeight = source.Height;
        int sourceStride = source.Stride;
        int sourceOffset = source.Offset;
        ManagedRasterPixelFormat sourcePixelFormat = source.PixelFormat;
        ManagedPhysicalFramebufferDescriptor physical = new(
            descriptor.FramebufferBase, descriptor.FramebufferSize, descriptor.Width,
            descriptor.Height, descriptor.PixelsPerScanLine, descriptor.BytesPerPixel,
            (ManagedPhysicalFramebufferPixelFormat)descriptor.PixelFormat,
            descriptor.RedMask, descriptor.GreenMask, descriptor.BlueMask,
            descriptor.ReservedMask);
        ManagedFramebufferPresenter presenter = new();
        ManagedFramebufferDestination destination = ManagedFramebufferDestination.FromPhysical(
            (nuint)descriptor.FramebufferBase, (nuint)descriptor.FramebufferSize);
        int x = descriptor.Width > (uint)sourceWidth
            ? (int)((descriptor.Width - (uint)sourceWidth) / 2) : 0;
        int y = descriptor.Height > (uint)sourceHeight
            ? (int)((descriptor.Height - (uint)sourceHeight) / 2) : 0;
        bool presented = presenter.TryPresent(sourceStorage, sourceOffset,
                                              sourceWidth, sourceHeight, sourceStride,
                                              sourcePixelFormat, in physical, destination,
                                              x, y);
        if (!presented) return false;
        Span<byte> sourceHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> destinationHash = stackalloc byte[ManagedSha256.DigestSize];
        ManagedFramebufferPresentationTelemetry telemetry = presenter.Telemetry;
        bool sourceHashCopied = presenter.TryCopySourceHashBefore(sourceHash);
        bool destinationHashCopied = presenter.TryCopyDestinationHash(destinationHash);
        bool sourceUnchanged = telemetry.SourceHashUnchanged;
        bool sourceDigestWritten = sourceHashCopied &&
            WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_SOURCE_FRAMEBUFFER_HASH_WORD=0x"u8,
                        sourceHash);
        bool destinationDigestWritten = destinationHashCopied &&
            WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE52_DESTINATION_HASH_WORD=0x"u8,
                        destinationHash);
        bool positionWritten = KernelLog.WriteHexLine(
            "GXOS_NET10:MANAGED_HTTPS_PHASE52_PRESENTATION_X=0x"u8, (ulong)x) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE52_PRESENTATION_Y=0x"u8, (ulong)y);
        bool proofWritten = positionWritten &&
            KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE52_GOP_PRESENT_PASS\r\n"u8);
        return sourceHashCopied && destinationHashCopied && sourceUnchanged &&
               sourceDigestWritten && destinationDigestWritten && positionWritten &&
               proofWritten;
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
        /* Keep the TLS fixture entropy aligned with the Phase 51 static
           client-flight contract; the image proof's determinism is asserted
           by the PNG/pixel digests, not by a new handshake vector. */
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

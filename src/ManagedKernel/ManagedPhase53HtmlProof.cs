using System;

namespace GuideXOS.Net10.ManagedKernel;

/// <summary>Authoritative guest proof for the bounded CSS-image page path.</summary>
internal sealed class ManagedPhase53HtmlProof : IManagedPageTerminalProof
{
    private static ReadOnlySpan<byte> PageUrl =>
        "https://www.example.com/phase53/index.html"u8;
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private readonly ManagedPageResourceOrchestrator _page;
    private bool _completed;

    internal ManagedPhase53HtmlProof(ManagedNetworkService service)
    {
        ManagedSecureRandom random = new(new FixedEntropy(CreateEntropy()));
        _page = new ManagedPageResourceOrchestrator(
            service, ManagedTls12Phase31Fixtures.Root,
            new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random,
            new ManagedPageResourceOptions(ManagedCssArenaOptions.Default,
                externalStylesheetLimit: 2, viewportWidth: 320, viewportHeight: 180),
            maximumEntityLength: ManagedHttpLimits.MaximumBodyCapacity,
            maximumDecodedResourceLength: 32 * 1024,
            phase52Markers: true, terminalProof: this);
    }

    internal ManagedPageResourceOrchestrator Page => _page;

    internal bool TryRun()
    {
        if (_page.BeginGetUrl(PageUrl) != NetworkOperationResult.Started)
            return false;
        ManagedNetworkServiceBackend.LiveEthernet?.EnablePhase34Polling();
        for (int poll = 0; poll != 262_144; ++poll)
        {
            NetworkOperationResult result = _page.Poll();
            if (_completed) return true;
            if (result == NetworkOperationResult.Failed ||
                _page.State == ManagedPageResourceState.Failed)
            {
                KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_FAILURE\r\n"u8);
                KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE53_FAILURE_REASON=0x"u8,
                                       (ulong)_page.FailureReason);
                KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_FAILURE=0x"u8,
                                       (ulong)_page.CssFailureReason);
                return false;
            }
        }
        return false;
    }

    bool IManagedPageTerminalProof.TryComplete(in ManagedFramebuffer framebuffer)
    {
        if (_completed) return false;
        ManagedPageResourceTelemetry telemetry = _page.Telemetry;
        if (telemetry.ImageNodes != 1 || telemetry.ImageRequestsStarted != 2 ||
            telemetry.ImagesLoaded != 2 || _page.CssImageReferencesEncountered != 1 ||
            _page.CssImageWinningReferences != 1 || _page.CssImageFetches != 1 ||
            _page.CssImageDeduplicated != 0 || _page.Layout == null ||
            _page.Paint == null || _page.Rasterizer == null)
            return false;
        ManagedHtmlNodeHandle hero = FindById("hero"u8);
        ManagedHtmlNodeHandle content = FindById("content"u8);
        if (hero == ManagedHtmlNodeHandle.Invalid || content == ManagedHtmlNodeHandle.Invalid ||
            !_page.Styles.TryGetComputedStyle(hero, out ManagedComputedStyle heroStyle) ||
            heroStyle.BackgroundImageKind != ManagedCssBackgroundImageKind.Resolved ||
            !_page.Images.TryGetForSourceNode(content.Index, out ManagedImageHandle contentHandle) ||
            !_page.Images.TryGetForSourceNode(hero.Index, out ManagedImageHandle cssHandle) ||
            !_page.Images.TryGetDescriptor(contentHandle, out ManagedPageImageDescriptor contentImage) ||
            !_page.Images.TryGetDescriptor(cssHandle, out ManagedPageImageDescriptor cssImage) ||
            contentImage.Width <= 0 || cssImage.Width <= 0 ||
            _page.Paint.Telemetry.BackgroundImageCommands != 1 ||
            _page.Paint.Telemetry.ImageCommands != 2 ||
            _page.Rasterizer.Telemetry.ImagePixelsWritten == 0)
            return false;

        if (!_page.TryGetExternalStylesheetTelemetry(0,
                out ManagedExternalStylesheetTelemetry stylesheet) ||
            !_page.Styles.TryGetCssImageReference(heroStyle.BackgroundImageReferenceIndex,
                out ManagedCssImageReference reference))
            return false;
        byte[] raw = new byte[512];
        int rawLength;
        if (!reference.TryCopyUrl(raw, out rawLength) ||
            !ManagedHttpsUrl.TryResolve(stylesheet.FinalUrl, raw.AsSpan(0, rawLength),
                                        out ManagedHttpsUrl resolved))
            return false;
        Span<byte> resolvedBytes = stackalloc byte[ManagedHttpsUrl.MaximumUrlLength];
        if (!resolved.TryCopyAbsoluteUrl(resolvedBytes, out int resolvedLength)) return false;

        Span<byte> styleHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> paintHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> framebufferHash = stackalloc byte[ManagedSha256.DigestSize];
        if (!_page.Styles.TryCopyCanonicalStyleHash(styleHash) ||
            !_page.Paint.TryCopyCanonicalPaintHash(paintHash) ||
            !_page.Rasterizer.TryCopyFramebufferHash(framebufferHash)) return false;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_REFERENCE_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_FETCH_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_DECODE_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_BACKGROUND_PAINT_PASS\r\n"u8) ||
            !WriteHex("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_RAW_URL_LENGTH=0x"u8,
                      (ulong)rawLength) ||
            !WriteBytes("GXOS_NET10:MANAGED_HTTPS_PHASE53_CSS_IMAGE_RESOLVED_URL="u8,
                        resolvedBytes[..resolvedLength]) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE53_STYLE_HASH_WORD=0x"u8, styleHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE53_PAINT_HASH_WORD=0x"u8, paintHash) ||
            !WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE53_FRAMEBUFFER_HASH_WORD=0x"u8,
                         framebufferHash) ||
            !Present(in framebuffer)) return false;
        _completed = true;
        return KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_GOP_PRESENT_PASS\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE53_VISIBLE_CSS_IMAGE_PASS\r\n"u8);
    }

    private ManagedHtmlNodeHandle FindById(ReadOnlySpan<byte> expected)
    {
        Span<uint> value = stackalloc uint[64];
        for (int index = 0; index != _page.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, _page.Document.DocumentNode.Generation);
            if (_page.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !_page.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Id,
                    out ManagedHtmlAttributeView view) ||
                !_page.Document.TryCopyAttributeValue(node, view.Index, value,
                    out int length, out bool hasValue) || !hasValue || length != expected.Length)
                continue;
            bool equal = true;
            for (int offset = 0; offset != length; ++offset)
                equal &= value[offset] <= 0x7F && (byte)value[offset] == expected[offset];
            if (equal) return node;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private bool Present(in ManagedFramebuffer source)
    {
        if (!ManagedKernelContract.FramebufferInstalled || source.BackingStorage == null)
            return false;
        GxManagedKernelFramebufferV1 descriptor = ManagedKernelContract.FramebufferDescriptor;
        ManagedPhysicalFramebufferDescriptor physical = new(
            descriptor.FramebufferBase, descriptor.FramebufferSize, descriptor.Width,
            descriptor.Height, descriptor.PixelsPerScanLine, descriptor.BytesPerPixel,
            (ManagedPhysicalFramebufferPixelFormat)descriptor.PixelFormat,
            descriptor.RedMask, descriptor.GreenMask, descriptor.BlueMask,
            descriptor.ReservedMask);
        ManagedFramebufferPresenter presenter = new();
        ManagedFramebufferDestination destination = ManagedFramebufferDestination.FromPhysical(
            (nuint)descriptor.FramebufferBase, (nuint)descriptor.FramebufferSize);
        int x = descriptor.Width > (uint)source.Width
            ? (int)((descriptor.Width - (uint)source.Width) / 2) : 0;
        int y = descriptor.Height > (uint)source.Height
            ? (int)((descriptor.Height - (uint)source.Height) / 2) : 0;
        return presenter.TryPresent(source.BackingStorage, source.Offset, source.Width,
            source.Height, source.Stride, source.PixelFormat, in physical, destination, x, y);
    }

    private static bool WriteHex(ReadOnlySpan<byte> prefix, ulong value) =>
        KernelLog.WriteHexLine(prefix, value);

    private static bool WriteBytes(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> value) =>
        KernelLog.Write(prefix) && KernelLog.Write(value) && KernelLog.Write("\r\n"u8);

    private static bool WriteDigest(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> digest)
    {
        for (int offset = 0; offset != digest.Length; offset += 4)
        {
            uint word = (uint)digest[offset] | ((uint)digest[offset + 1] << 8) |
                        ((uint)digest[offset + 2] << 16) | ((uint)digest[offset + 3] << 24);
            if (!KernelLog.WriteHexLine(prefix, word)) return false;
        }
        return true;
    }

    private static byte[] CreateEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 2;
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

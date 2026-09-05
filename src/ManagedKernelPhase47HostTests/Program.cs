using System;
using System.Collections.Generic;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            FramebufferPreflight();
            ClearAndGuards();
            FillsAndAlpha();
            BordersAndClips();
            GlyphsAndText();
            ImagesAndPhase46Integration();
            FixedScrollAndZOrder();
            CancellationHashAndReset();
            Console.WriteLine($"MANAGED_KERNEL_PHASE47_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE47_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void FramebufferPreflight()
    {
        Scene scene = Styled("<div id=box>pixel</div>", "#box{background-color:red}");
        ManagedSoftwareRasterizer rasterizer = new();
        uint[] one = new uint[1];
        Check(rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(one, 1, 1, 1)), "framebuffer-valid-1x1");
        Check(rasterizer.State == ManagedRasterState.Complete && rasterizer.HashValid,
              "framebuffer-valid-state");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[1], 0, 1, 1)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.InvalidFramebuffer &&
              rasterizer.CommandsProcessed == 0 && !rasterizer.HashValid,
              "framebuffer-zero-width");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[1], 1, 0, 1)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.InvalidFramebuffer,
              "framebuffer-zero-height");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[2], 2, 1, 1)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.InvalidFramebuffer,
              "framebuffer-short-stride");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[1], 2, 1, 2)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.FramebufferTooSmall,
              "framebuffer-one-pixel-short");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[1], -1, 1, 1)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.InvalidFramebuffer,
              "framebuffer-negative-offset");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[1], 1, 1, 1,
                (ManagedRasterPixelFormat)99)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.UnsupportedPixelFormat,
              "framebuffer-unsupported-format");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[1], int.MaxValue, int.MaxValue,
                int.MaxValue)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.FramebufferGeometryOverflow,
              "framebuffer-geometry-overflow");

        uint[] guarded = new uint[16];
        guarded.AsSpan().Fill(0xC0DEC0DEU);
        ManagedFramebuffer valid = new(guarded, 1, 2, 2, 4);
        Check(rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, valid), "preflight-state-success");
        Span<byte> priorHash = stackalloc byte[32];
        Check(rasterizer.TryCopyFramebufferHash(priorHash), "preflight-prior-hash");
        Check(!rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, new ManagedFramebuffer(new uint[3], 2, 2, 4, 4)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.FramebufferTooSmall &&
              rasterizer.CommandsProcessed == 0 && !rasterizer.HashValid &&
              !rasterizer.TryCopyFramebufferHash(priorHash), "preflight-clears-success-state");
        Check(rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
            scene.Layout, valid), "preflight-recovery-success");
        Span<byte> recoveryHash = stackalloc byte[32];
        Check(rasterizer.TryCopyFramebufferHash(recoveryHash) && priorHash.SequenceEqual(recoveryHash),
              "preflight-recovery-hash-stable");
    }

    private static void ClearAndGuards()
    {
        Scene scene = Styled("<div id=box>clear</div>", "");
        uint[] colors = { 0x00000000U, 0xFFFFFFFFU, 0xFF102030U, 0x7FABCDEFU };
        foreach (uint color in colors)
        {
            (uint[] storage, ManagedFramebuffer framebuffer) = Guarded(7, 5, 3);
            storage.AsSpan().Fill(0xA5A5A5A5U);
            storage[0] = 0xC0DEC0DEU;
            storage[^1] = 0xC0DEC0DEU;
            ManagedSoftwareRasterizer rasterizer = new();
            Check(rasterizer.TryRender(Array.Empty<ManagedPaintCommand>(), scene.Builder.Document,
                scene.Layout, framebuffer, options: new ManagedRasterRenderOptions(true, color)),
                "clear-render");
            for (int y = 0; y != framebuffer.Height; ++y)
                for (int x = 0; x != framebuffer.Width; ++x)
                    Check(framebuffer.TryGetPixel(x, y, out uint pixel) && pixel == color,
                          "clear-active-pixel");
            for (int y = 0; y != framebuffer.Height; ++y)
                Check(storage[framebuffer.Offset + y * framebuffer.Stride + framebuffer.Width] ==
                      0xA5A5A5A5U, "clear-padding-untouched");
            Check(storage[0] == 0xC0DEC0DEU && storage[^1] == 0xC0DEC0DEU,
                  "clear-guards-untouched");
            Check(rasterizer.Telemetry.ClearPixelsWritten == 35 &&
                  rasterizer.Telemetry.TotalPixelsWritten == 35,
                  "clear-telemetry");
            ManagedRasterDirtyBounds dirty = rasterizer.Telemetry.DirtyBounds;
            Check(!dirty.IsEmpty && dirty.MinX == 0 && dirty.MinY == 0 &&
                  dirty.MaxX == 6 && dirty.MaxY == 4, "clear-dirty-bounds");
        }
    }

    private static void FillsAndAlpha()
    {
        Scene scene = Styled("<div id=box>fill</div>", "");
        ManagedHtmlNodeHandle node = Element(scene, "box");
        int box = Box(scene, node);
        ManagedLayoutRect full = new(0, 0, 8, 6);
        ManagedLayoutRect surface = new(0, 0, 8, 6);

        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0xFFFF0000U),
            0xFF000000U, out uint red) && red == 0xFFFF0000U, "alpha-opaque-red-over-black");
        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0x00FF0000U),
            0xFF000000U, out uint transparent) && transparent == 0xFF000000U,
            "alpha-transparent-source");
        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0x80FFFFFFU),
            0xFF000000U, out uint halfWhite) && halfWhite == 0xFF808080U,
            "alpha-half-white-over-black");
        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0x80FF0000U),
            0xFF0000FFU, out uint halfRed) && halfRed == 0xFF80007FU,
            "alpha-half-red-over-blue");
        ManagedPaintCommand[] layers =
        {
            MakeFill(box, node.Index, full, surface, 0x80FF0000U),
            MakeFill(box, node.Index, full, surface, 0x80FF0000U)
        };
        Check(RenderOne(scene, layers, 0xFF000000U, out uint twoLayers) &&
              twoLayers == 0xFFC00000U, "alpha-sequential-layers");
        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0x80FF0000U),
            0x00000000U, out uint transparentDestination) &&
              transparentDestination == 0x80FF0000U, "alpha-destination-alpha");
        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0x01FF0000U),
            0xFF000000U, out uint alphaOne) && alphaOne == 0xFF010000U,
            "alpha-one-rounding");
        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0xFEFF0000U),
            0xFF000000U, out uint alpha254) && alpha254 == 0xFFFE0000U,
            "alpha-254-rounding");
        Check(RenderOne(scene, MakeFill(box, node.Index, full, surface, 0x3F123456U),
            0xFF000000U, out uint nested) && nested == 0xFF040D15U,
            "alpha-corrected-3f123456");

        (uint[] storage, ManagedFramebuffer framebuffer) = Guarded(6, 5, 2);
        ManagedSoftwareRasterizer clipped = new();
        ManagedPaintCommand partial = MakeFill(box, node.Index, new(-3, -2, 7, 6),
                                                new(0, 0, 6, 5), 0xFFFF0000U);
        Check(clipped.TryRender(new[] { partial }, scene.Builder.Document, scene.Layout,
            framebuffer, options: ManagedRasterRenderOptions.ClearBlack), "fill-negative-clipped");
        Check(framebuffer.TryGetPixel(0, 0, out uint corner) && corner == 0xFFFF0000U,
              "fill-clipped-corner-written");
        Check(framebuffer.TryGetPixel(4, 3, out uint outside) && outside == 0xFF000000U,
              "fill-half-open-edge");
        Check(storage[0] == 0xC0DEC0DEU && storage[^1] == 0xC0DEC0DEU,
              "fill-guards-untouched");
        Check(clipped.Telemetry.FillPixelsWritten == 16 && clipped.Telemetry.TotalPixelsWritten ==
              6 * 5 + 16, "fill-telemetry");
    }

    private static void BordersAndClips()
    {
        Scene scene = Styled("<div id=box>border</div>", "");
        ManagedHtmlNodeHandle node = Element(scene, "box");
        int box = Box(scene, node);
        ManagedLayoutRect surface = new(0, 0, 12, 10);
        ManagedPaintCommand border = new(ManagedPaintCommandKind.BorderRectangle, 0,
            ManagedPaintCommandFlags.None, box, node.Index, 0, 0, -1, 0,
            new ManagedLayoutRect(2, 2, 8, 6), surface, 0xFFFF00FFU,
            new ManagedLayoutEdges(1, 2, 3, 4), ManagedCssBorderStyle.Solid,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        ManagedSoftwareRasterizer rasterizer = new();
        uint[] storage = new uint[12 * 10];
        ManagedFramebuffer framebuffer = new(storage, 12, 10, 12);
        Check(rasterizer.TryRender(new[] { border }, scene.Builder.Document, scene.Layout,
            framebuffer), "border-render");
        Check(Pixel(framebuffer, 2, 2) == 0xFFFF00FFU &&
              Pixel(framebuffer, 9, 2) == 0xFFFF00FFU &&
              Pixel(framebuffer, 2, 7) == 0xFFFF00FFU &&
              Pixel(framebuffer, 9, 7) == 0xFFFF00FFU, "border-four-corners");
        Check(Pixel(framebuffer, 6, 4) == 0xFF000000U, "border-interior-clear");
        Check(rasterizer.Telemetry.BorderPixelsWritten == 44 &&
              rasterizer.Telemetry.BorderCommands == 1, "border-telemetry");

        ManagedPaintCommand begin = new(ManagedPaintCommandKind.BeginClip, 1,
            ManagedPaintCommandFlags.None, -1, -1, 0, 0, -1, 0,
            new ManagedLayoutRect(3, 3, 4, 3), surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        ManagedPaintCommand fill = new(ManagedPaintCommandKind.FillRectangle, 1,
            ManagedPaintCommandFlags.None, box, node.Index, 0, 0, -1, 0,
            new ManagedLayoutRect(0, 0, 12, 10), new ManagedLayoutRect(3, 3, 4, 3),
            0xFF00FF00U, new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        ManagedPaintCommand end = new(ManagedPaintCommandKind.EndClip, 1,
            ManagedPaintCommandFlags.None, -1, -1, 0, 0, -1, 0,
            new ManagedLayoutRect(3, 3, 4, 3), surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        Array.Clear(storage);
        bool clipResult = rasterizer.TryRender(new[] { begin, fill, end }, scene.Builder.Document,
            scene.Layout, framebuffer);
        Check(clipResult, "clip-render-" + rasterizer.FailureReason);
        Check(Pixel(framebuffer, 3, 3) == 0xFF00FF00U &&
              Pixel(framebuffer, 6, 5) == 0xFF00FF00U &&
              Pixel(framebuffer, 2, 3) == 0xFF000000U &&
              rasterizer.Telemetry.PeakClipDepth == 1, "clip-intersection");

        ManagedSoftwareRasterizer tooDeep = new(new ManagedRasterizerOptions(2));
        ManagedPaintCommand[] deep = new ManagedPaintCommand[6];
        deep[0] = new(ManagedPaintCommandKind.BeginClip, 1, ManagedPaintCommandFlags.None,
            -1, -1, 0, 0, -1, 0, surface, surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        deep[1] = new(ManagedPaintCommandKind.BeginClip, 2, ManagedPaintCommandFlags.None,
            -1, -1, 0, 0, -1, 0, surface, surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        deep[2] = new(ManagedPaintCommandKind.BeginClip, 3, ManagedPaintCommandFlags.None,
            -1, -1, 0, 0, -1, 0, surface, surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        deep[3] = new(ManagedPaintCommandKind.EndClip, 3, ManagedPaintCommandFlags.None,
            -1, -1, 0, 0, -1, 0, surface, surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        deep[4] = new(ManagedPaintCommandKind.EndClip, 2, ManagedPaintCommandFlags.None,
            -1, -1, 0, 0, -1, 0, surface, surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        deep[5] = new(ManagedPaintCommandKind.EndClip, 1, ManagedPaintCommandFlags.None,
            -1, -1, 0, 0, -1, 0, surface, surface, 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        Check(!tooDeep.TryRender(deep, scene.Builder.Document, scene.Layout, framebuffer) &&
              tooDeep.FailureReason == ManagedRasterFailureReason.RasterClipDepthExceeded &&
              tooDeep.CommandsProcessed == 0, "clip-depth-negative-zero-write-preflight");
    }

    private static void GlyphsAndText()
    {
        ManagedProofGlyphSource glyphs = ManagedProofGlyphSource.Instance;
        uint[] scalars = { ' ', 'A', 'Z', 'a', 'z', '0', '.', '?', 0x2603U };
        foreach (uint scalar in scalars)
        {
            Check(glyphs.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 8, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph), "glyph-lookup");
            Check(glyph.Width == 5 && glyph.Height == 7 && glyph.Advance == 6,
                "glyph-dimensions");
        }
        Check(glyphs.TryGetGlyph(0x2603U, ManagedPaintFontId.DefaultUi, 8, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph fallback) && fallback.IsFallback,
            "glyph-fallback");
        Check(glyphs.TryGetGlyph('A', ManagedPaintFontId.DefaultUi, 8, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph first) &&
              glyphs.TryGetGlyph('A', ManagedPaintFontId.DefaultUi, 8, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph second) &&
              first.Width == second.Width && first.GetRowMask(0) == second.GetRowMask(0),
              "glyph-repeat-determinism");
        Check(glyphs.TryGetGlyph('A', ManagedPaintFontId.DefaultUi, 16, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph scale2) && scale2.Width == 10 &&
              scale2.Height == 14 && scale2.Advance == 12 &&
              glyphs.TryGetGlyph('A', ManagedPaintFontId.DefaultUi, 24, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph scale3) && scale3.Width == 15,
              "glyph-integer-scaling");

        Scene scene = Styled("<div id=text>Abz 42? ☃</div>",
            "#text{color:#102030;font-size:8px}");
        ManagedLayoutEngine layout = scene.Layout;
        ManagedPaintEngine paint = scene.Paint;
        Check(paint.TryGenerate(80, 40) && paint.Validate(out _), "text-paint-generate");
        ManagedSoftwareRasterizer rasterizer = new();
        uint[] storage = new uint[80 * 40];
        ManagedFramebuffer framebuffer = new(storage, 80, 40);
        Check(rasterizer.TryRender(paint, framebuffer), "text-render");
        Check(rasterizer.Telemetry.TextCommands >= 1 && rasterizer.Telemetry.GlyphRequests >= 9 &&
              rasterizer.Telemetry.GlyphsRendered == rasterizer.Telemetry.GlyphRequests &&
              rasterizer.Telemetry.FallbackGlyphs >= 1 &&
              rasterizer.Telemetry.GlyphPixelsWritten > 0, "text-telemetry");
        Check(ContainsPixel(storage, 0xFF102030U), "text-foreground-pixel");

        ManagedHtmlNodeHandle textNode = FirstLaidOutNode(scene, ManagedHtmlNodeKind.Text);
        int textBox = Box(scene, textNode);
        ManagedPaintCommand textCommand = new(ManagedPaintCommandKind.TextRun, 0,
            ManagedPaintCommandFlags.None, textBox, textNode.Index, 1, 2, 0, 8,
            new ManagedLayoutRect(2, 2, 20, 8), new ManagedLayoutRect(0, 0, 80, 40),
            0xFFFF0000U, new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 8, 400, ManagedCssFontStyle.Normal, 10_000, 0);
        Check(rasterizer.TryRender(new[] { textCommand }, scene.Builder.Document, layout,
            framebuffer), "text-source-offset-length");
        textCommand = new ManagedPaintCommand(ManagedPaintCommandKind.TextRun, 0,
            ManagedPaintCommandFlags.None, textBox, textNode.Index, 99, 1, 0, 8,
            textCommand.Rect, textCommand.ClipRect, textCommand.Color, textCommand.BorderWidths,
            textCommand.BorderStyle, textCommand.FontId, textCommand.FontSize,
            textCommand.FontWeight, textCommand.FontStyle, textCommand.Opacity, 0);
        Check(!rasterizer.TryRender(new[] { textCommand }, scene.Builder.Document, layout,
            framebuffer) && rasterizer.FailureReason == ManagedRasterFailureReason.InvalidTextReference &&
              rasterizer.CommandsProcessed == 0, "text-invalid-offset-zero-write");
        textCommand = new ManagedPaintCommand(ManagedPaintCommandKind.TextRun, 0,
            ManagedPaintCommandFlags.None, Box(scene, "text"), Element(scene, "text").Index, 0, 1, 0, 8,
            textCommand.Rect, textCommand.ClipRect, textCommand.Color, textCommand.BorderWidths,
            textCommand.BorderStyle, textCommand.FontId, textCommand.FontSize,
            textCommand.FontWeight, textCommand.FontStyle, textCommand.Opacity, 0);
        Check(!rasterizer.TryRender(new[] { textCommand }, scene.Builder.Document, layout,
            framebuffer) && rasterizer.FailureReason == ManagedRasterFailureReason.InvalidTextReference,
            "text-invalid-node-kind");
    }

    private static void ImagesAndPhase46Integration()
    {
        Scene scene = Styled("<div id=wrap><img id=image width=12 height=10></div>", "");
        ManagedPaintEngine paint = scene.Paint;
        Check(paint.TryGenerate(100, 60) && paint.ImagePlaceholderCommands == 1,
            "image-phase46-command");
        ManagedSoftwareRasterizer rasterizer = new();
        ManagedFramebuffer framebuffer = new(new uint[100 * 60], 100, 60);
        Check(rasterizer.TryRender(paint, framebuffer), "image-render");
        Check(rasterizer.Telemetry.ImagePlaceholderCommands == 1 &&
              rasterizer.Telemetry.ImagePixelsWritten > 0, "image-telemetry");
        Check(ContainsPixel(framebuffer.BackingStorage!, 0xFF202020U) &&
              ContainsPixel(framebuffer.BackingStorage!, 0xFFB0B0B0U) &&
              ContainsPixel(framebuffer.BackingStorage!, 0xFFFFFFFFU), "image-pattern-colors");

        Scene nested = Styled("<div id=outer style='opacity:.5'><div id=middle " +
            "style='opacity:.5'><div id=inner style='background-color:#123456'>x</div>" +
            "</div></div>", "");
        Check(nested.Paint.TryGenerate(80, 40), "nested-opacity-paint");
        ManagedLayoutBox innerBox = GetBox(nested, "inner");
        ManagedPaintCommand nestedCommand = FindCommand(nested.Paint,
            ManagedPaintCommandKind.FillRectangle, Box(nested, "inner"));
        Check(nestedCommand.Color == 0x3F123456U && nestedCommand.Opacity == 2_500,
            "nested-opacity-command-preserved");
        ManagedFramebuffer nestedFramebuffer = new(new uint[80 * 40], 80, 40);
        Check(rasterizer.TryRender(nested.Paint, nestedFramebuffer), "nested-opacity-raster");
        int sampleX = Math.Max(0, nestedCommand.Rect.X + Math.Min(20, nestedCommand.Rect.Width - 1));
        int sampleY = Math.Max(0, nestedCommand.Rect.Y + Math.Min(20, nestedCommand.Rect.Height - 1));
        Check(nestedFramebuffer.TryGetPixel(sampleX, sampleY, out uint nestedPixel) &&
              nestedPixel == 0xFF040D15U, "nested-opacity-exact-pixel");

        ManagedHtmlNodeHandle innerNode = Element(nested, "inner");
        ManagedPaintCommand mismatch = MakeFill(Box(nested, "inner"),
            nested.Builder.Document.BodyElement.Index, new(0, 0, 1, 1),
            new(0, 0, 80, 40), 0xFFFF0000U);
        uint[] unchanged = new uint[80 * 40];
        unchanged.AsSpan().Fill(0xDEADBEEFU);
        Check(!rasterizer.TryRender(new[] { mismatch }, nested.Builder.Document, nested.Layout,
            new ManagedFramebuffer(unchanged, 80, 40)) &&
              rasterizer.FailureReason == ManagedRasterFailureReason.InvalidDisplayList &&
              AllPixels(unchanged, 0xDEADBEEFU), "source-pair-mismatch-zero-write");
        Check(innerNode.Index != nested.Builder.Document.BodyElement.Index &&
              innerBox.SourceNodeIndex == innerNode.Index, "source-pair-baseline");
    }

    private static void FixedScrollAndZOrder()
    {
        Scene scene = Styled("<div id=normal>n</div><div id=fixed>f</div>",
            "#normal{width:18px;height:10px;background-color:red}" +
            "#fixed{position:fixed;left:30px;top:12px;width:18px;height:10px;background-color:blue}");
        ManagedPaintEngine origin = new(scene.Layout);
        ManagedPaintEngine scroll = new(scene.Layout);
        Check(origin.TryGenerate(100, 60, 0, 0) && scroll.TryGenerate(100, 60, 3, 2),
            "fixed-scroll-paint-generate");
        ManagedPaintCommand normalOrigin = FindCommand(origin, ManagedPaintCommandKind.FillRectangle,
                                                        Box(scene, "normal"));
        ManagedPaintCommand normalScroll = FindCommand(scroll, ManagedPaintCommandKind.FillRectangle,
                                                        Box(scene, "normal"));
        ManagedPaintCommand fixedOrigin = FindCommand(origin, ManagedPaintCommandKind.FillRectangle,
                                                       Box(scene, "fixed"));
        ManagedPaintCommand fixedScroll = FindCommand(scroll, ManagedPaintCommandKind.FillRectangle,
                                                       Box(scene, "fixed"));
        Check(normalScroll.Rect.X == normalOrigin.Rect.X - 3 &&
              normalScroll.Rect.Y == normalOrigin.Rect.Y - 2, "normal-scroll-command");
        Check(fixedScroll.Rect == fixedOrigin.Rect, "fixed-scroll-command");
        ManagedSoftwareRasterizer rasterizer = new();
        ManagedFramebuffer originFramebuffer = new(new uint[100 * 60], 100, 60);
        ManagedFramebuffer scrollFramebuffer = new(new uint[100 * 60], 100, 60);
        Check(rasterizer.TryRender(origin, originFramebuffer) &&
              rasterizer.TryRender(scroll, scrollFramebuffer), "fixed-scroll-pixel-render");
        Check(originFramebuffer.TryGetPixel(fixedOrigin.Rect.X + 1, fixedOrigin.Rect.Y + 1,
            out uint fixedPixelOrigin) && scrollFramebuffer.TryGetPixel(fixedScroll.Rect.X + 1,
                fixedScroll.Rect.Y + 1, out uint fixedPixelScroll) && fixedPixelOrigin == fixedPixelScroll,
            "fixed-selected-pixel-invariant");
        uint normalPixelOrigin = 0;
        uint normalPixelScroll = 0;
        bool normalPixels = originFramebuffer.TryGetPixel(normalOrigin.Rect.X + 4,
            normalOrigin.Rect.Y + 4, out normalPixelOrigin) &&
            scrollFramebuffer.TryGetPixel(normalScroll.Rect.X + 4, normalScroll.Rect.Y + 4,
                out normalPixelScroll) && normalPixelOrigin == 0xFFFF0000U &&
            normalPixelScroll == 0xFFFF0000U;
        Check(normalPixels, "normal-selected-pixel-moves-" + normalOrigin.Rect.X + "," +
            normalOrigin.Rect.Y + "-" + normalScroll.Rect.X + "," + normalScroll.Rect.Y +
            "-" + normalPixelOrigin.ToString("X8") + "-" + normalPixelScroll.ToString("X8") +
            "-" + normalOrigin.Color.ToString("X8") + "-" + normalScroll.Color.ToString("X8") +
            "-" + normalOrigin.Rect.Width + "x" + normalOrigin.Rect.Height);

        Scene zScene = Styled("<div id=zneg2></div><div id=zneg1></div><div id=zpos1></div>" +
            "<div id=zpos2></div>",
            "#zneg2{position:absolute;left:5px;top:5px;width:12px;height:12px;z-index:-2;background-color:#112233}" +
            "#zneg1{position:absolute;left:5px;top:5px;width:12px;height:12px;z-index:-1;background-color:#223344}" +
            "#zpos1{position:absolute;left:5px;top:5px;width:12px;height:12px;z-index:1;background-color:#334455}" +
            "#zpos2{position:absolute;left:5px;top:5px;width:12px;height:12px;z-index:2;background-color:#445566}");
        Check(zScene.Paint.TryGenerate(80, 50) && zScene.Paint.Validate(out _),
            "z-order-paint-generate");
        Check(IndexOf(zScene.Paint, Box(zScene, "zneg2")) < IndexOf(zScene.Paint, Box(zScene, "zneg1")) &&
              IndexOf(zScene.Paint, Box(zScene, "zpos1")) < IndexOf(zScene.Paint, Box(zScene, "zpos2")),
            "z-order-magnitude-reaches-command-order");
        ManagedFramebuffer zFramebuffer = new(new uint[80 * 50], 80, 50);
        Check(rasterizer.TryRender(zScene.Paint, zFramebuffer) &&
              zFramebuffer.TryGetPixel(6, 6, out uint zPixel) && zPixel == 0xFF445566U,
            "z-order-overlap-selected-pixel");
    }

    private static void CancellationHashAndReset()
    {
        Scene scene = Styled("<div id=box style='background-color:red;width:30px;height:20px'>text</div>", "");
        ManagedPaintEngine paint = scene.Paint;
        Check(paint.TryGenerate(80, 50), "cancel-source-paint");
        uint[] storage = new uint[80 * 50];
        storage.AsSpan().Fill(0xA5A5A5A5U);
        ManagedFramebuffer framebuffer = new(storage, 80, 50);
        ManagedSoftwareRasterizer rasterizer = new();
        rasterizer.Cancel();
        Check(!rasterizer.TryRender(paint, framebuffer) && rasterizer.State == ManagedRasterState.Cancelled &&
              rasterizer.FailureReason == ManagedRasterFailureReason.Cancelled &&
              rasterizer.CommandsProcessed == 0 && !rasterizer.HashValid &&
              AllPixels(storage, 0xA5A5A5A5U), "cancel-before-render-zero-write");
        rasterizer.Reset();
        rasterizer.CancelAfterCommands(1);
        Check(!rasterizer.TryRender(paint, framebuffer) && rasterizer.State == ManagedRasterState.Cancelled &&
              rasterizer.CommandsProcessed == 1 && !rasterizer.HashValid,
              "cancel-after-first-command");
        rasterizer.Reset();
        Check(rasterizer.TryRender(paint, framebuffer), "cancel-reset-reuse");
        Span<byte> firstHash = stackalloc byte[32];
        Check(rasterizer.TryCopyFramebufferHash(firstHash), "hash-first");
        rasterizer.Reset();
        Check(rasterizer.TryRender(paint, framebuffer), "hash-rerender");
        Span<byte> secondHash = stackalloc byte[32];
        Check(rasterizer.TryCopyFramebufferHash(secondHash) && firstHash.SequenceEqual(secondHash),
            "hash-repeat-stable");

        Scene text = Styled("<div id=text>ABCD</div>", "#text{font-size:8px;color:red}");
        Check(text.Paint.TryGenerate(80, 40), "cancel-text-source");
        rasterizer.Reset();
        rasterizer.CancelAfterGlyphs(1);
        Check(!rasterizer.TryRender(text.Paint, new ManagedFramebuffer(new uint[80 * 40], 80, 40)) &&
              rasterizer.State == ManagedRasterState.Cancelled && !rasterizer.HashValid &&
              rasterizer.CancellationCheckpoints > 0, "cancel-during-text-run");

        Scene fixedOnly = Styled("<div id=fixed>f</div>",
            "#fixed{position:fixed;left:10px;top:10px;width:12px;height:8px;background-color:#123456}");
        ManagedPaintEngine fixed0 = new(fixedOnly.Layout);
        ManagedPaintEngine fixed1 = new(fixedOnly.Layout);
        Check(fixed0.TryGenerate(60, 40, 0, 0) && fixed1.TryGenerate(60, 40, 11, 13),
            "hash-fixed-scroll-source");
        ManagedSoftwareRasterizer hash0 = new();
        ManagedSoftwareRasterizer hash1 = new();
        Check(hash0.TryRender(fixed0, new ManagedFramebuffer(new uint[60 * 40], 60, 40)) &&
              hash1.TryRender(fixed1, new ManagedFramebuffer(new uint[60 * 40], 60, 40)),
            "hash-fixed-scroll-render");
        Span<byte> fixedHash0 = stackalloc byte[32];
        Span<byte> fixedHash1 = stackalloc byte[32];
        Check(hash0.TryCopyFramebufferHash(fixedHash0) && hash1.TryCopyFramebufferHash(fixedHash1) &&
              fixedHash0.SequenceEqual(fixedHash1), "hash-fixed-only-scroll-invariant");
    }

    private static bool RenderOne(Scene scene, ManagedPaintCommand command, uint clear, out uint pixel) =>
        RenderOne(scene, new[] { command }, clear, out pixel);

    private static bool RenderOne(Scene scene, ManagedPaintCommand[] commands, uint clear,
                                  out uint pixel)
    {
        ManagedSoftwareRasterizer rasterizer = new();
        ManagedFramebuffer framebuffer = new(new uint[8 * 6], 8, 6);
        bool result = rasterizer.TryRender(commands, scene.Builder.Document, scene.Layout,
            framebuffer, options: new ManagedRasterRenderOptions(true, clear));
        pixel = framebuffer.TryGetPixel(0, 0, out uint value) ? value : 0;
        return result;
    }

    private static ManagedPaintCommand MakeFill(int box, int node, ManagedLayoutRect rect,
                                                ManagedLayoutRect clip, uint color) =>
        new(ManagedPaintCommandKind.FillRectangle, 0, ManagedPaintCommandFlags.None, box, node,
            0, 0, -1, 0, rect, clip, color, new ManagedLayoutEdges(0, 0, 0, 0),
            ManagedCssBorderStyle.None, ManagedPaintFontId.DefaultUi, 0, 0,
            ManagedCssFontStyle.Normal, 10_000, 0);

    private static Scene Styled(string body, string cssText)
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<!doctype html><html><head><style>" + cssText +
            "</style></head><body>" + body + "</body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "style-success");
        ManagedLayoutEngine layout = new(builder.Document, css);
        Check(layout.TryLayout(320, 240) && layout.Validate(out _), "layout-success");
        ManagedPaintEngine paint = new(layout);
        return new Scene(builder, css, layout, paint);
    }

    private static ManagedHtmlTreeBuilder Parse(string html)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = ToScalars(html);
        for (int offset = 0; offset < scalars.Count;)
        {
            int length = Math.Min(7, scalars.Count - offset);
            uint[] input = new uint[length];
            for (int index = 0; index != length; ++index) input[index] = scalars[offset + index];
            Check(tokenizer.AppendInput(input), "tokenizer-input");
            ManagedHtmlTokenizerProcessResult result = tokenizer.Pump(builder);
            Check(result != ManagedHtmlTokenizerProcessResult.Failed &&
                  result != ManagedHtmlTokenizerProcessResult.Cancelled, "tokenizer-pump");
            offset += length;
        }
        Check(tokenizer.Pump(builder, true) == ManagedHtmlTokenizerProcessResult.Complete &&
              builder.Complete(), "document-complete");
        return builder;
    }

    private static List<uint> ToScalars(string value)
    {
        List<uint> result = new(value.Length);
        for (int index = 0; index != value.Length; ++index)
        {
            char current = value[index];
            result.Add(char.IsHighSurrogate(current) && index + 1 < value.Length &&
                       char.IsLowSurrogate(value[index + 1])
                ? (uint)char.ConvertToUtf32(current, value[++index]) : current);
        }
        return result;
    }

    private static ManagedHtmlNodeHandle Element(Scene scene, string id)
    {
        for (int index = 0; index != scene.Builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, scene.Builder.Document.DocumentNode.Generation);
            if (scene.Builder.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !scene.Builder.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Id,
                    out ManagedHtmlAttributeView attribute)) continue;
            uint[] value = new uint[attribute.ValueLength];
            scene.Builder.Document.TryCopyAttributeValue(node, attribute.Index, value, out int length, out _);
            if (ScalarsToString(value.AsSpan(0, length)) == id) return node;
        }
        throw new InvalidOperationException("missing id=" + id);
    }

    private static int Box(Scene scene, string id) => Box(scene, Element(scene, id));

    private static int Box(Scene scene, ManagedHtmlNodeHandle node)
    {
        Check(scene.Layout.TryGetBoxForNode(node, out int box), "box-for-node");
        return box;
    }

    private static ManagedLayoutBox GetBox(Scene scene, string id)
    {
        Check(scene.Layout.TryGetBox(Box(scene, id), out ManagedLayoutBox box), "get-box");
        return box;
    }

    private static ManagedHtmlNodeHandle FirstNode(ManagedHtmlDocument document,
                                                   ManagedHtmlNodeKind kind)
    {
        for (int index = 0; index != document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, document.DocumentNode.Generation);
            if (document.GetNodeKind(node) == kind) return node;
        }
        throw new InvalidOperationException("missing node kind");
    }

    private static ManagedHtmlNodeHandle FirstLaidOutNode(Scene scene, ManagedHtmlNodeKind kind)
    {
        for (int index = 0; index != scene.Builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, scene.Builder.Document.DocumentNode.Generation);
            if (scene.Builder.Document.GetNodeKind(node) == kind &&
                scene.Layout.TryGetBoxForNode(node, out _)) return node;
        }
        throw new InvalidOperationException("missing laid-out node kind");
    }

    private static ManagedPaintCommand FindCommand(ManagedPaintEngine paint,
                                                   ManagedPaintCommandKind kind, int box)
    {
        for (int index = 0; index != paint.CommandsEmitted; ++index)
        {
            Check(paint.TryGetCommand(index, out ManagedPaintCommand command), "read-command");
            if (command.Kind == kind && command.SourceBoxIndex == box) return command;
        }
        throw new InvalidOperationException("missing paint command");
    }

    private static int IndexOf(ManagedPaintEngine paint, int box)
    {
        for (int index = 0; index != paint.CommandsEmitted; ++index)
        {
            paint.TryGetCommand(index, out ManagedPaintCommand command);
            if (command.Kind == ManagedPaintCommandKind.FillRectangle &&
                command.SourceBoxIndex == box) return index;
        }
        return int.MaxValue;
    }

    private static uint Pixel(ManagedFramebuffer framebuffer, int x, int y)
    {
        Check(framebuffer.TryGetPixel(x, y, out uint pixel), "pixel-read");
        return pixel;
    }

    private static bool ContainsPixel(uint[] storage, uint value)
    {
        for (int index = 0; index != storage.Length; ++index)
            if (storage[index] == value) return true;
        return false;
    }

    private static bool AllPixels(uint[] storage, uint value)
    {
        for (int index = 0; index != storage.Length; ++index)
            if (storage[index] != value) return false;
        return true;
    }

    private static (uint[] Storage, ManagedFramebuffer Framebuffer) Guarded(
        int width, int height, int padding)
    {
        int stride = checked(width + padding);
        uint[] storage = new uint[checked(height * stride + 2)];
        storage.AsSpan().Fill(0xC0DEC0DEU);
        return (storage, new ManagedFramebuffer(storage, 1, width, height, stride));
    }

    private static string ScalarsToString(ReadOnlySpan<uint> scalars)
    {
        StringBuilder result = new();
        for (int index = 0; index != scalars.Length; ++index)
            result.Append(char.ConvertFromUtf32((int)scalars[index]));
        return result.ToString();
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class Scene
    {
        internal Scene(ManagedHtmlTreeBuilder builder, ManagedCssEngine css,
                       ManagedLayoutEngine layout, ManagedPaintEngine paint)
        {
            Builder = builder; Css = css; Layout = layout; Paint = paint;
        }

        internal ManagedHtmlTreeBuilder Builder { get; }
        internal ManagedCssEngine Css { get; }
        internal ManagedLayoutEngine Layout { get; }
        internal ManagedPaintEngine Paint { get; }
    }
}

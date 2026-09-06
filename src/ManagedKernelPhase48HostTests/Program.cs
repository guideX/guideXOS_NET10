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
            RegistryShapeAndHash();
            FaceSelectionAndMetrics();
            MappingAndFallback();
            ValidatorCoverage();
            LayoutPaintIntegration();
            RasterCoverageAndBaseline();
            IsolatedProofRegression();
            Console.WriteLine($"MANAGED_KERNEL_PHASE48_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE48_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void RegistryShapeAndHash()
    {
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        Check(registry.FaceCount == 8, "eight-faces");
        Check(registry.AtlasBytes == 8 * (260 * 160 + 260 * 180), "atlas-bytes");
        Check(registry.MetadataCount == 8 * 201, "metadata-count");
        Check(ManagedPhase48GlyphMetadata.SizeInBytes == 10, "metadata-size");
        Check(registry.LargestGlyphWidth <= 20 && registry.LargestGlyphHeight <= 20,
              "bounded-glyph-dimensions");
        Check(registry.MaximumAdvance > 0 && registry.MaximumAdvance <= 21,
              "bounded-advance");
        Span<byte> first = stackalloc byte[32];
        Span<byte> second = stackalloc byte[32];
        Check(registry.TryCopySemanticHash(first), "hash-copy");
        Check(registry.TryCopySemanticHash(second) && first.SequenceEqual(second), "hash-stable");
        bool hasHashByte = false;
        foreach (byte value in first) hasHashByte |= value != 0;
        Check(hasHashByte, "hash-nonempty");
        Console.WriteLine($"MANAGED_KERNEL_PHASE48_FONT_SEMANTIC_HASH={Convert.ToHexString(first)}");
        Console.WriteLine($"MANAGED_KERNEL_PHASE48_FONT_METRICS faces={registry.FaceCount} atlas_bytes={registry.AtlasBytes} metadata={registry.MetadataCount} metadata_bytes={registry.MetadataCount * ManagedPhase48GlyphMetadata.SizeInBytes} max_width={registry.LargestGlyphWidth} max_height={registry.LargestGlyphHeight} max_advance={registry.MaximumAdvance}");
        Check(ManagedPhase48FontValidator.ValidateRegistry(registry, out _), "registry-valid");
        for (int index = 0; index != registry.FaceCount; ++index)
        {
            Check(registry.TryGetFace((ManagedPhase48FontFaceId)index,
                                      out ManagedPhase48FontFace face), "face-present");
            Check(face.FamilyName == "Roboto" && face.GlyphCount == 201,
                  "face-identity");
            Check(face.AtlasByteCount == 88_400 && face.AtlasWidth == 260 && face.AtlasHeight == 160 &&
                  face.ExtendedAtlasWidth == 260 && face.ExtendedAtlasHeight == 180,
                  "face-atlas-shape");
            Check(face.LineHeight == face.Ascent + face.Descent + face.LineGap,
                  "line-metric-relation");
        }
        Check(!registry.TryGetFace((ManagedPhase48FontFaceId)255, out _), "invalid-face-id");
    }

    private static void FaceSelectionAndMetrics()
    {
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        ManagedLayoutTextStyle regular9 = new(9, 400, ManagedCssFontStyle.Normal);
        ManagedLayoutTextStyle bold9 = new(9, 700, ManagedCssFontStyle.Normal);
        ManagedLayoutTextStyle italic9 = new(9, 400, ManagedCssFontStyle.Italic);
        ManagedLayoutTextStyle boldItalic12 = new(12, 700, ManagedCssFontStyle.Italic);
        Check(registry.TryGetGlyph('i', ManagedPaintFontId.DefaultUi, 9, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph r9), "regular9-glyph");
        Check(registry.TryGetGlyph('i', ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph r12), "regular12-glyph");
        Check(registry.TryGetGlyph('i', ManagedPaintFontId.DefaultUi, 9, 700,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph rb), "bold9-glyph");
        Check(registry.TryGetGlyph('i', ManagedPaintFontId.DefaultUi, 9, 400,
            ManagedCssFontStyle.Italic, out ManagedRasterGlyph ri), "italic9-glyph");
        Check(registry.TryGetGlyph('i', ManagedPaintFontId.DefaultUi, 12, 700,
            ManagedCssFontStyle.Italic, out ManagedRasterGlyph rbi), "bolditalic12-glyph");
        Check(registry.ActiveFaceId == ManagedPhase48FontFaceId.Roboto12BoldItalic,
              "active-face-tracking");
        Check(r9.Width > 0 && r12.Width > 0 && r9.Advance > 0 && r12.Advance > 0,
              "real-glyph-metrics");
        Check(registry.GetLineHeight(in regular9) == 16 && registry.GetBaseline(in regular9) == 12,
              "nine-point-line-metrics");
        Check(registry.GetLineHeight(in boldItalic12) == 18 && registry.GetBaseline(in boldItalic12) == 13,
              "twelve-point-line-metrics");
        Check(registry.GetLineHeight(in italic9) == 16 && registry.GetBaseline(in bold9) == 12,
              "variant-line-metrics");
        Check(r9.GetCoverage(0, 0) <= 255 && r9.GetCoverage(-1, 0) == 0,
              "coverage-bounds");
    }

    private static void MappingAndFallback()
    {
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        registry.ResetTelemetry();
        for (uint scalar = 32; scalar <= 126; ++scalar)
        {
            Check(registry.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 12, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph), "ascii-lookup");
            Check(!glyph.IsFallback && glyph.Advance > 0, "ascii-hit");
        }
        Check(registry.TryGetGlyph(0xE9, ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph accented) &&
              !accented.IsFallback, "latin1-accent-hit");
        uint[] unsupported = { 0x3BB, 0x4E2D, 0x1F600, 0xD800 };
        foreach (uint scalar in unsupported)
        {
            bool valid = scalar != 0xD800;
            Check(registry.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 12, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph) == valid,
                "unsupported-scalar-policy");
            if (valid) Check(glyph.IsFallback && glyph.Width > 0, "visible-fallback");
        }
        Check(registry.TryGetGlyph(' ', ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph space) &&
              space.Width == 0 && space.Height == 0 && space.Advance > 0,
              "space-advance-no-pixels");
        ManagedPhase48FontTelemetry telemetry = registry.Telemetry;
        Check(telemetry.AsciiLookups == 96 && telemetry.NonAsciiLookups == 4,
              "lookup-telemetry");
        Check(telemetry.FallbackLookups == 3 && telemetry.GlyphHits == telemetry.GlyphLookups,
              "fallback-telemetry");
    }

    private static void ValidatorCoverage()
    {
        byte[] atlas = new byte[260 * 160];
        ManagedPhase48GlyphMetadata[] glyphs = new ManagedPhase48GlyphMetadata[95];
        ManagedPhase48FontFace badMetrics = ManagedPhase48FontFace.CreateForValidation(
            ManagedPhase48FontFaceId.Roboto9Regular, 9, 0, 8, 4, 4, atlas, glyphs);
        Check(!ManagedPhase48FontValidator.Validate(badMetrics,
            out ManagedPhase48FontValidationFailureReason metricReason) &&
            metricReason == ManagedPhase48FontValidationFailureReason.InvalidMetrics,
            "validator-metrics-negative");
        ManagedPhase48GlyphMetadata[] badBounds = new ManagedPhase48GlyphMetadata[95];
        for (int index = 0; index != badBounds.Length; ++index)
            badBounds[index] = new ManagedPhase48GlyphMetadata(index == 31 ? 250 : 0, 0,
                index == 31 ? 20 : 1, 1, 1, 0, 0, 1);
        ManagedPhase48FontFace badGeometry = ManagedPhase48FontFace.CreateForValidation(
            ManagedPhase48FontFaceId.Roboto9Regular, 9, 12, 8, 4, 4, atlas, badBounds);
        Check(!ManagedPhase48FontValidator.Validate(badGeometry,
            out ManagedPhase48FontValidationFailureReason boundsReason) &&
            boundsReason == ManagedPhase48FontValidationFailureReason.InvalidGlyphBounds,
            "validator-bounds-negative");
        Check(ManagedPhase48FontValidator.ValidateRegistry(ManagedPhase48FontRegistry.Instance,
            out ManagedPhase48FontValidationFailureReason registryReason) &&
            registryReason == ManagedPhase48FontValidationFailureReason.None,
            "validator-registry-positive");
    }

    private static void LayoutPaintIntegration()
    {
        Scene scene = Styled("<main id=main><span id=wide>iiii WWWW guideXOS</span> " +
            "<b id=bold>Bold</b> <i id=italic>Italic</i> &#128512;</main>",
            "body{font-size:12px}main{display:block;width:260px}#bold{font-weight:700}#italic{font-style:italic}");
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        ManagedLayoutTextStyle sample = new(12, 400, ManagedCssFontStyle.Normal);
        int iWidth = Measure(registry, "iiii", sample);
        int wWidth = Measure(registry, "WWWW", sample);
        Check(iWidth != wWidth && iWidth > 0 && wWidth > 0, "variable-width-layout-input");
        Check(scene.Layout.TryLayout(320, 180) && scene.Layout.Validate(out _), "font-layout-valid");
        ManagedPaintEngine paint = new(scene.Layout, ManagedPaintArenaOptions.Default, registry);
        Check(paint.TryGenerate(320, 180) && paint.Validate(out _), "font-paint-valid");
        Check(paint.TextCommands >= 4, "font-text-commands");
        bool foundBaseline = false;
        for (int index = 0; index != paint.CommandsEmitted; ++index)
        {
            Check(paint.TryGetCommand(index, out ManagedPaintCommand command), "font-command-read");
            if (command.Kind != ManagedPaintCommandKind.TextRun) continue;
            foundBaseline = true;
            Check(command.BaselineY == command.Rect.Y +
                registry.GetBaseline(new ManagedLayoutTextStyle(command.FontSize,
                    command.FontWeight, command.FontStyle)), "shared-baseline");
        }
        Check(foundBaseline, "baseline-command-present");
        Span<byte> layoutHash = stackalloc byte[32];
        Span<byte> paintHash = stackalloc byte[32];
        Check(scene.Layout.TryCopyCanonicalLayoutHash(layoutHash), "font-layout-hash");
        Check(paint.TryCopyCanonicalPaintHash(paintHash), "font-paint-hash");
        bool hasLayoutHashByte = false;
        foreach (byte value in layoutHash) hasLayoutHashByte |= value != 0;
        Check(hasLayoutHashByte, "font-layout-hash-nonempty");
    }

    private static void RasterCoverageAndBaseline()
    {
        Scene scene = Styled("<div id=text>Ai&#128512;A</div>", "#text{display:block;font-size:12px;color:#204060}");
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        ManagedLayoutEngine layout = new(scene.Builder.Document, scene.Css,
            ManagedLayoutArenaOptions.Default, registry);
        Check(layout.TryLayout(160, 80) && layout.Validate(out _), "raster-font-layout");
        ManagedPaintEngine paint = new(layout, ManagedPaintArenaOptions.Default, registry);
        Check(paint.TryGenerate(160, 80) && paint.Validate(out _), "raster-font-paint");
        uint[] pixels = new uint[160 * 80];
        ManagedSoftwareRasterizer rasterizer = new();
        Check(rasterizer.TryRender(paint, new ManagedFramebuffer(pixels, 160, 80), registry,
            new ManagedRasterRenderOptions(true, 0)), "raster-font-success");
        Check(rasterizer.GlyphRequests >= 4 && rasterizer.FallbackGlyphs >= 1,
              "raster-fallback-count");
        Check(rasterizer.GlyphPixelsWritten > 0 && rasterizer.GlyphPixelsConsidered >=
              rasterizer.GlyphPixelsWritten, "raster-alpha-pixels");
        Check(registry.TryGetGlyph('A', ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph direct), "direct-alpha-glyph");
        bool coveragePartial = false;
        for (int row = 0; row != direct.Height; ++row)
            for (int column = 0; column != direct.Width; ++column)
                coveragePartial |= direct.GetCoverage(row, column) > 0 &&
                    direct.GetCoverage(row, column) < 255;
        Check(coveragePartial, "atlas-partial-coverage");
        bool hasPartialAlpha = false;
        foreach (uint pixel in pixels)
        {
            int alpha = (int)(pixel >> 24);
            if (alpha != 0 && alpha != 255) hasPartialAlpha = true;
        }
        Check(hasPartialAlpha, "coverage-alpha-blending");
        Span<byte> hash = stackalloc byte[32];
        Check(rasterizer.TryCopyFramebufferHash(hash), "raster-hash");
        uint[] secondPixels = new uint[pixels.Length];
        ManagedSoftwareRasterizer second = new();
        Check(second.TryRender(paint, new ManagedFramebuffer(secondPixels, 160, 80), registry,
            new ManagedRasterRenderOptions(true, 0)), "raster-repeat-success");
        Span<byte> secondHash = stackalloc byte[32];
        Check(second.TryCopyFramebufferHash(secondHash) && hash.SequenceEqual(secondHash),
              "raster-repeat-hash");
    }

    private static void IsolatedProofRegression()
    {
        IManagedRasterGlyphSource proof = ManagedProofGlyphSource.Instance;
        Check(proof.TryGetGlyph('A', ManagedPaintFontId.DefaultUi, 8, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph), "proof-glyph");
        Check(glyph.Width == 5 && glyph.Height == 7 && glyph.Advance == 6 &&
              !glyph.UsesBaselineMetrics, "proof-shape-preserved");
        Check(proof.TryGetGlyph('A', ManagedPaintFontId.DefaultUi, 32, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph scaled) &&
              scaled.Width == 20 && scaled.Height == 28 && scaled.Scale == 4,
              "proof-scaling-preserved");
        Check(scaled.GetCoverage(0, 0) == 0 && scaled.GetCoverage(0, 4) == 255,
              "proof-coverage-adapter");
    }

    private static int Measure(ManagedPhase48FontRegistry registry, string value,
                               ManagedLayoutTextStyle style)
    {
        int result = 0;
        foreach (uint scalar in ToScalars(value))
        {
            Check(registry.TryMeasureScalar(scalar, in style, out int advance), "measure-scalar");
            result = checked(result + advance);
        }
        return result;
    }

    private static Scene Styled(string body, string cssText)
    {
        ManagedHtmlTreeBuilder builder = Parse("<!doctype html><html><head><style>" + cssText +
            "</style></head><body>" + body + "</body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "style-success");
        ManagedLayoutEngine layout = new(builder.Document, css);
        return new Scene(builder, css, layout);
    }

    private static ManagedHtmlTreeBuilder Parse(string html)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = ToScalars(html);
        for (int offset = 0; offset < scalars.Count;)
        {
            int length = Math.Min(11, scalars.Count - offset);
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

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class Scene
    {
        internal Scene(ManagedHtmlTreeBuilder builder, ManagedCssEngine css,
                       ManagedLayoutEngine layout)
        {
            Builder = builder;
            Css = css;
            Layout = layout;
        }

        internal ManagedHtmlTreeBuilder Builder { get; }
        internal ManagedCssEngine Css { get; }
        internal ManagedLayoutEngine Layout { get; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            RegistryShapeAndCoverage();
            MappingAndFallback();
            FacesMetricsAndAsciiStability();
            EntityUtf8AndLatin1Pipeline();
            NbspLayout();
            RasterCoverage();
            ValidatorNegatives();
            Determinism();
            Console.WriteLine($"MANAGED_KERNEL_PHASE50_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE50_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void RegistryShapeAndCoverage()
    {
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        Check(registry.FaceCount == 8, "face-count");
        Check(registry.GlyphsPerFace == 201 && registry.MetadataCount == 8 * 201, "glyph-count");
        Check(registry.AtlasBytes == 8 * (260 * 160 + 260 * 180), "atlas-total");
        Check(registry.CoverageFlags == (ManagedPhase50FontCoverageFlags.BasicLatin |
            ManagedPhase50FontCoverageFlags.Latin1Supplement |
            ManagedPhase50FontCoverageFlags.CommonPunctuation), "coverage-flags");
        Check(ManagedPhase50FontCoverage.SparseCodePointCount == 10, "sparse-count");
        Check(ManagedPhase50FontCoverage.ExtendedGlyphCount == 106, "extended-count");
        Span<byte> hash = stackalloc byte[32];
        Span<byte> repeat = stackalloc byte[32];
        Check(registry.TryCopySemanticHash(hash) && registry.TryCopySemanticHash(repeat) &&
              hash.SequenceEqual(repeat), "semantic-hash-stable");
        Check(Convert.ToHexString(hash) !=
              "4872C3D6EA0701697830CD9FCB21294BF097584B6014C70CEE8D52359A6BE5F0",
              "semantic-hash-changed");
        Console.WriteLine($"MANAGED_KERNEL_PHASE50_FONT_SEMANTIC_HASH={Convert.ToHexString(hash)}");
        Console.WriteLine($"MANAGED_KERNEL_PHASE50_FONT_METRICS faces={registry.FaceCount} glyphs_per_face={registry.GlyphsPerFace} total_glyphs={registry.MetadataCount} atlas_bytes={registry.AtlasBytes} metadata_bytes={registry.MetadataCount * ManagedPhase48GlyphMetadata.SizeInBytes} ascii_atlas_bytes={registry.FaceCount * 260 * 160} extended_atlas_bytes={registry.FaceCount * 260 * 180}");
        Check(ManagedPhase48FontValidator.ValidateRegistry(registry, out _), "registry-validator");
        for (int faceIndex = 0; faceIndex != registry.FaceCount; ++faceIndex)
        {
            Check(registry.TryGetFace((ManagedPhase48FontFaceId)faceIndex,
                                      out ManagedPhase48FontFace face), "face-present");
            Check(face.GlyphCount == 201 && face.AsciiGlyphCount == 95 &&
                  face.ExtendedGlyphCount == 106 && face.AtlasPageCount == 2,
                  "face-pages");
            Check(face.AtlasByteCount == 88_400 && face.ExtendedAtlasByteCount == 46_800,
                  "face-page-bytes");
            Check(face.LineHeight == face.Ascent + face.Descent + face.LineGap,
                  "face-line-metrics");
        }
        uint[] sparse = new uint[10];
        for (int index = 0; index != sparse.Length; ++index)
            Check(ManagedPhase50FontCoverage.TryGetSparseCodePoint(index, out sparse[index]), "sparse-descriptor");
        Check(ManagedPhase48FontValidator.ValidateSparseCodePointMap(sparse), "sparse-sorted-unique");
    }

    private static void MappingAndFallback()
    {
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        registry.ResetTelemetry();
        for (uint scalar = 0x20; scalar <= 0x7E; ++scalar)
        {
            Check(registry.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 12, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph) &&
                !glyph.IsFallback, "ascii-map");
        }
        for (uint scalar = 0xA0; scalar <= 0xFF; ++scalar)
        {
            Check(registry.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 12, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph) &&
                !glyph.IsFallback, "latin1-map");
        }
        uint[] sparse = { 0x2013, 0x2022, 0x2122 };
        foreach (uint scalar in sparse)
            Check(registry.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 12, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph) &&
                !glyph.IsFallback, "sparse-map");
        uint[] unsupported = { 0x4E2D, 0x1F600, 0x10FFFF };
        foreach (uint scalar in unsupported)
        {
            Check(registry.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 12, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph) &&
                glyph.IsFallback && glyph.Width > 0, "unsupported-visible-fallback");
        }
        Check(!registry.TryGetGlyph(0xD800, ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out _), "surrogate-rejected");
        Check(!registry.TryMeasureScalar(0x110000, new ManagedLayoutTextStyle(12, 400,
            ManagedCssFontStyle.Normal), out _), "above-unicode-rejected");
        ManagedPhase48FontTelemetry telemetry = registry.Telemetry;
        Check(telemetry.AsciiLookups == 95 && telemetry.Latin1Lookups == 96 &&
              telemetry.SparsePunctuationLookups == 3 && telemetry.GlyphFallbacks == 3 &&
              telemetry.FaceFallbacks == 0 && telemetry.NbspCount == 1,
              "bounded-lookup-telemetry");
    }

    private static void FacesMetricsAndAsciiStability()
    {
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        int[] sizes = { 9, 12 };
        ManagedCssFontStyle[] styles = { ManagedCssFontStyle.Normal, ManagedCssFontStyle.Italic };
        int[] weights = { 400, 700 };
        foreach (int size in sizes)
        foreach (ManagedCssFontStyle style in styles)
        foreach (int weight in weights)
        {
            Check(registry.TryGetGlyph(0xE9, ManagedPaintFontId.DefaultUi, size, weight,
                style, out ManagedRasterGlyph glyph) && !glyph.IsFallback && glyph.Width > 0,
                "extended-face-coverage");
            Check(registry.TryGetGlyph(0x20AC, ManagedPaintFontId.DefaultUi, size, weight,
                style, out ManagedRasterGlyph euro) && !euro.IsFallback && euro.Advance > 0,
                "extended-face-euro");
        }
        ManagedLayoutTextStyle regular = new(12, 400, ManagedCssFontStyle.Normal);
        int cafe = Measure(registry, "cafe", regular);
        int accentedCafe = Measure(registry, "caf\u00E9", regular);
        int resume = Measure(registry, "resume", regular);
        int accentedResume = Measure(registry, "r\u00E9sum\u00E9", regular);
        int iiii = Measure(registry, "iiii", regular);
        int wwww = Measure(registry, "WWWW", regular);
        int guide = Measure(registry, "guideXOS", new ManagedLayoutTextStyle(12, 700,
            ManagedCssFontStyle.Normal));
        Check(cafe > 0 && accentedCafe > 0 && resume > 0 && accentedResume > 0,
              "accented-widths-positive");
        Check(iiii == 28 && wwww == 44 && guide == 56, "real-metric-widths");
        Check(Measure(registry, "caf\u00E9", regular) == accentedCafe &&
              Measure(registry, "r\u00E9sum\u00E9", regular) == accentedResume,
              "width-repeat-deterministic");
        Check(ManagedPhase48GeneratedFontData.SourceHashes[0].Contains("Png=4B556657B17446FCCA4B85FE94481EECF778DE3494939A0184E9F4E90693246F") &&
              ManagedPhase48GeneratedFontData.SourceHashes[8].Contains("Png=EBB4218CC1C6CA9701D7341372CBF06F64E3DD04A2407A6AC96A353ADE66B322"),
              "ascii-source-hashes-unchanged");
        Check(registry.TryGetGlyph(0xE9, ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph acute) &&
              acute.BearingY < 0 && acute.Height > 0, "acute-bearing");
        Check(registry.TryGetGlyph(0xC7, ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph cedilla) && cedilla.Height > 0,
            "cedilla-bounds");
        Check(registry.TryGetGlyph(0xC5, ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph ring) && ring.Height > 0,
            "ring-bounds");
        Check(registry.TryGetGlyph(0xA0, ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph nbsp) &&
              nbsp.Width == 0 && nbsp.Height == 0 && nbsp.Advance > 0 &&
              !nbsp.IsFallback, "nbsp-glyph-metrics");
        Console.WriteLine($"MANAGED_KERNEL_PHASE50_SELECTED_WIDTHS cafe={cafe} cafe_accented={accentedCafe} resume={resume} resume_accented={accentedResume} iiii={iiii} WWWW={wwww} guideXOS={guide}");
    }

    private static void EntityUtf8AndLatin1Pipeline()
    {
        uint[] entity = Parse("<main>&#233; &#xE9; &#169; &#x20AC;</main>").Document.Text;
        Check(Count(entity, 0xE9) == 2 && Count(entity, 0xA9) == 1 &&
              Count(entity, 0x20AC) == 1, "numeric-entities-to-scalars");

        byte[] utf8Bytes = Encoding.UTF8.GetBytes("<main>caf\u00E9 r\u00E9sum\u00E9 \u00A9</main>");
        byte[] latin1Bytes = Encoding.ASCII.GetBytes("<main>caf");
        latin1Bytes = latin1Bytes.Concat(new byte[] { 0xE9 }).Concat(
            Encoding.ASCII.GetBytes(" r")).Concat(new byte[] { 0xE9 }).Concat(
            Encoding.ASCII.GetBytes("sum")).Concat(new byte[] { 0xE9, 0x20, 0xA9 }).Concat(
            Encoding.ASCII.GetBytes("</main>")).ToArray();
        List<uint> utf8Scalars = Decode(utf8Bytes, ManagedTextCharset.Utf8);
        List<uint> latin1Scalars = Decode(latin1Bytes, ManagedTextCharset.Iso88591);
        Check(utf8Scalars.SequenceEqual(latin1Scalars), "utf8-latin1-scalar-equivalence");
        Check(ManagedContentTypeParser.Parse(Encoding.ASCII.GetBytes(
            "text/html; charset=iso-8859-1")).Charset == ManagedTextCharset.Iso88591,
            "latin1-content-type-classification");
        Scene utf8 = Styled(utf8Scalars);
        Scene latin1 = Styled(latin1Scalars);
        Check(utf8.Layout.TryLayout(180, 80) && utf8.Layout.Validate(out _), "utf8-layout");
        Check(latin1.Layout.TryLayout(180, 80) && latin1.Layout.Validate(out _), "latin1-layout");
        Check(MeasureScalars(utf8Scalars, new ManagedLayoutTextStyle(12, 400,
            ManagedCssFontStyle.Normal)) == MeasureScalars(latin1Scalars,
            new ManagedLayoutTextStyle(12, 400, ManagedCssFontStyle.Normal)),
            "utf8-latin1-width-equivalence");
        Check(utf8.Layout.TryCopyCanonicalLayoutHash(stackalloc byte[32]) &&
              latin1.Layout.TryCopyCanonicalLayoutHash(stackalloc byte[32]),
            "equivalent-layout-hashes-available");
        ManagedPhase48FontRegistry.Instance.ResetTelemetry();
        Check(utf8.Paint.TryGenerate(180, 80) && utf8.Paint.Validate(out _) &&
              latin1.Paint.TryGenerate(180, 80) && latin1.Paint.Validate(out _),
            "utf8-latin1-paint");
        Check(ManagedPhase48FontRegistry.Instance.Telemetry.GlyphFallbacks == 0,
            "latin1-supported-no-fallback");
        Console.WriteLine($"MANAGED_KERNEL_PHASE50_CHARSET_EQUIVALENCE utf8_bytes={utf8Bytes.Length} latin1_bytes={latin1Bytes.Length} resource_hash_equal={(Sha(utf8Bytes) == Sha(latin1Bytes))}");
        Check(Sha(utf8Bytes) != Sha(latin1Bytes), "resource-hashes-differ");
        List<uint> literal = Decode(Encoding.UTF8.GetBytes(
            "caf\u00E9 na\u00EFve fa\u00E7ade r\u00E9sum\u00E9 \u201CguideXOS\u201D it\u2019s em\u2014dash\u2026"), ManagedTextCharset.Utf8);
        foreach (uint scalar in new uint[] { 0xE9, 0xEF, 0xE7, 0x201C, 0x201D, 0x2019, 0x2014, 0x2026 })
            Check(literal.Contains(scalar), "literal-utf8-scalar");
        ManagedPhase48FontRegistry.Instance.ResetTelemetry();
        foreach (uint scalar in literal)
            Check(ManagedPhase48FontRegistry.Instance.TryMeasureScalar(scalar,
                new ManagedLayoutTextStyle(12, 400, ManagedCssFontStyle.Normal), out _),
                "literal-supported-measurement");
        Check(ManagedPhase48FontRegistry.Instance.Telemetry.GlyphFallbacks == 0,
            "literal-supported-no-fallback");
    }

    private static void NbspLayout()
    {
        Scene ordinary = Styled("<main>A B</main>", "body{font-size:12px}main{display:block;width:15px}");
        Scene nbsp = Styled("<main>A\u00A0B</main>", "body{font-size:12px}main{display:block;width:15px}");
        Check(ordinary.Layout.TryLayout(80, 80) && ordinary.Layout.Validate(out _), "ordinary-space-layout");
        Check(nbsp.Layout.TryLayout(80, 80) && nbsp.Layout.Validate(out _), "nbsp-layout");
        Check(ordinary.Layout.Telemetry.LineCount > nbsp.Layout.Telemetry.LineCount,
            "ordinary-space-can-wrap");
        Check(nbsp.Layout.Telemetry.NbspPreventedBreakCount == 1 &&
              nbsp.Layout.Telemetry.TextScalarsMeasured >= 3,
            "nbsp-prevents-internal-break");
        Check(Measure(ManagedPhase48FontRegistry.Instance, "A\u00A0B",
            new ManagedLayoutTextStyle(12, 400, ManagedCssFontStyle.Normal)) > 0,
            "nbsp-measured");
    }

    private static void RasterCoverage()
    {
        Scene scene = Styled("<main>\u00E9 \u00C9 \u00FC \u00E7 \u00A9 \u201C\u2014\u2026 中</main>",
            "body{font-size:12px;color:#204060}main{display:block;width:300px}");
        Check(scene.Layout.TryLayout(320, 100) && scene.Layout.Validate(out _), "extended-raster-layout");
        Check(scene.Paint.TryGenerate(320, 100) && scene.Paint.Validate(out _), "extended-raster-paint");
        uint[] pixels = new uint[320 * 100];
        ManagedSoftwareRasterizer rasterizer = new();
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        registry.ResetTelemetry();
        Check(rasterizer.TryRender(scene.Paint, new ManagedFramebuffer(pixels, 320, 100), registry,
            new ManagedRasterRenderOptions(true, 0)), "extended-raster-render");
        Check(rasterizer.GlyphPixelsWritten > 0 && rasterizer.FallbackGlyphs == 1 &&
              registry.Telemetry.NonAsciiRasterizedGlyphs >= 8,
            "extended-raster-telemetry");
        foreach (uint scalar in new uint[] { 0xE9, 0xC9, 0xFC, 0xE7, 0xA9, 0x201C, 0x2014, 0x2026 })
        {
            Check(registry.TryGetGlyph(scalar, ManagedPaintFontId.DefaultUi, 12, 400,
                ManagedCssFontStyle.Normal, out ManagedRasterGlyph glyph) &&
                !glyph.IsFallback && glyph.Width > 0, "direct-extended-glyph");
            bool hasCoverage = false;
            for (int row = 0; row != glyph.Height; ++row)
                for (int column = 0; column != glyph.Width; ++column)
                    hasCoverage |= glyph.GetCoverage(row, column) != 0;
            Check(hasCoverage, "extended-alpha-coverage");
        }
        Check(registry.TryGetGlyph(0xA0, ManagedPaintFontId.DefaultUi, 12, 400,
            ManagedCssFontStyle.Normal, out ManagedRasterGlyph nbsp) &&
              nbsp.Width == 0 && nbsp.Height == 0 && nbsp.Advance > 0,
            "raster-nbsp-no-pixels");
        Span<byte> first = stackalloc byte[32];
        Span<byte> second = stackalloc byte[32];
        Check(rasterizer.TryCopyFramebufferHash(first), "raster-hash");
        uint[] repeatPixels = new uint[pixels.Length];
        ManagedSoftwareRasterizer repeat = new();
        Check(repeat.TryRender(scene.Paint, new ManagedFramebuffer(repeatPixels, 320, 100), registry,
            new ManagedRasterRenderOptions(true, 0)) && repeat.TryCopyFramebufferHash(second) &&
              first.SequenceEqual(second), "raster-repeat");
        Console.WriteLine($"MANAGED_KERNEL_PHASE50_RASTER glyph_requests={rasterizer.GlyphRequests} glyphs_rendered={rasterizer.GlyphsRendered} fallback={rasterizer.FallbackGlyphs} non_ascii={registry.Telemetry.NonAsciiRasterizedGlyphs} pixels_written={rasterizer.GlyphPixelsWritten} framebuffer_hash={Convert.ToHexString(first)}");
    }

    private static void ValidatorNegatives()
    {
        byte[] atlas = new byte[260 * 160];
        ManagedPhase48GlyphMetadata[] ascii = ValidGlyphs(95, false);
        Check(!ManagedPhase48FontValidator.Validate(
            ManagedPhase48FontFace.CreateForValidation(ManagedPhase48FontFaceId.Roboto9Regular,
                9, 0, 8, 4, 4, atlas, ascii),
            out ManagedPhase48FontValidationFailureReason metric) &&
            metric == ManagedPhase48FontValidationFailureReason.InvalidMetrics, "validator-metrics");
        Check(!ManagedPhase48FontValidator.Validate(
            ManagedPhase48FontFace.CreateForValidation(ManagedPhase48FontFaceId.Roboto9Regular,
                9, 12, 8, 4, 4, atlas, new byte[260 * 180], ascii,
                ValidGlyphs(105, true)),
            out ManagedPhase48FontValidationFailureReason missing) &&
            missing == ManagedPhase48FontValidationFailureReason.InvalidExtendedGlyphCount,
            "validator-missing-required-mapping");
        ManagedPhase48GlyphMetadata[] ext = ValidGlyphs(106, true);
        ext[0] = new ManagedPhase48GlyphMetadata(250, 0, 1, 1, 1, 0, 0, 3);
        Check(!ManagedPhase48FontValidator.Validate(
            ManagedPhase48FontFace.CreateForValidation(ManagedPhase48FontFaceId.Roboto9Regular,
                9, 12, 8, 4, 4, atlas, new byte[260 * 180], ascii, ext),
            out ManagedPhase48FontValidationFailureReason atlasOffset) &&
            atlasOffset == ManagedPhase48FontValidationFailureReason.InvalidExtendedAtlasGeometry,
            "validator-atlas-offset");
        ext = ValidGlyphs(106, true);
        ext[0] = new ManagedPhase48GlyphMetadata(0, 0, 1, 1, 1, 0, 0, 3);
        Check(!ManagedPhase48FontValidator.Validate(
            ManagedPhase48FontFace.CreateForValidation(ManagedPhase48FontFaceId.Roboto9Regular,
                9, 12, 8, 4, 4, atlas, new byte[260 * 180], ascii, ext),
            out ManagedPhase48FontValidationFailureReason nbsp) &&
            nbsp == ManagedPhase48FontValidationFailureReason.InvalidNbspMetadata,
            "validator-nbsp");
        ManagedPhase48GlyphMetadata[] badDimensions = ValidGlyphs(106, true);
        badDimensions[5] = new ManagedPhase48GlyphMetadata(0, 0, 21, 1, 1, 0, 0, 3);
        Check(!ManagedPhase48FontValidator.Validate(
            ManagedPhase48FontFace.CreateForValidation(ManagedPhase48FontFaceId.Roboto9Regular,
                9, 12, 8, 4, 4, atlas, new byte[260 * 180], ascii, badDimensions),
            out ManagedPhase48FontValidationFailureReason dimensions) &&
            dimensions == ManagedPhase48FontValidationFailureReason.InvalidGlyphDimensions,
            "validator-glyph-length");
        uint[] duplicate = new uint[10];
        for (int index = 0; index != duplicate.Length; ++index)
            Check(ManagedPhase50FontCoverage.TryGetSparseCodePoint(index, out duplicate[index]), "duplicate-source");
        duplicate[4] = duplicate[3];
        Check(!ManagedPhase48FontValidator.ValidateSparseCodePointMap(duplicate), "validator-duplicate-sparse");
        ManagedPhase48GlyphMetadata[] missingFallback = ValidGlyphs(95, false);
        missingFallback[31] = new ManagedPhase48GlyphMetadata(0, 0, 1, 1, 1, 0, 0, 0);
        Check(!ManagedPhase48FontValidator.Validate(
            ManagedPhase48FontFace.CreateForValidation(ManagedPhase48FontFaceId.Roboto9Regular,
                9, 12, 8, 4, 4, atlas, missingFallback),
            out ManagedPhase48FontValidationFailureReason fallback) &&
            fallback == ManagedPhase48FontValidationFailureReason.MissingFallback,
            "validator-fallback");
    }

    private static void Determinism()
    {
        ManagedPhase48FontRegistry registry = ManagedPhase48FontRegistry.Instance;
        Span<byte> one = stackalloc byte[32];
        Span<byte> two = stackalloc byte[32];
        Check(registry.TryCopySemanticHash(one) && registry.TryCopySemanticHash(two) &&
              one.SequenceEqual(two), "repeated-semantic-hash");
        Scene a = Styled("<main>caf\u00E9 \u2014 \u00A9</main>", "body{font-size:12px}");
        Scene b = Styled("<main>caf\u00E9 \u2014 \u00A9</main>", "body{font-size:12px}");
        Check(a.Layout.TryLayout(180, 80) && b.Layout.TryLayout(180, 80), "repeat-layout");
        Span<byte> ah = stackalloc byte[32];
        Span<byte> bh = stackalloc byte[32];
        Check(a.Layout.TryCopyCanonicalLayoutHash(ah) && b.Layout.TryCopyCanonicalLayoutHash(bh) &&
              ah.SequenceEqual(bh), "repeat-layout-hash");
    }

    private static ManagedPhase48GlyphMetadata[] ValidGlyphs(int count, bool extended)
    {
        ManagedPhase48GlyphMetadata[] result = new ManagedPhase48GlyphMetadata[count];
        for (int index = 0; index != count; ++index)
        {
            int x = (index % 13) * 20;
            int y = (index / 13) * 20;
            result[index] = new ManagedPhase48GlyphMetadata(x, y, 1, 1, 1, 0, 0,
                (byte)(extended ? 3 : 1));
        }
        if (extended) result[0] = new ManagedPhase48GlyphMetadata(0, 0, 0, 0, 4, 0, 0, 2);
        return result;
    }

    private static int Measure(ManagedPhase48FontRegistry registry, string text,
                               ManagedLayoutTextStyle style)
    {
        return MeasureScalars(ToScalars(text), style, registry);
    }

    private static int MeasureScalars(IReadOnlyList<uint> scalars,
                                      ManagedLayoutTextStyle style,
                                      ManagedPhase48FontRegistry? registry = null)
    {
        registry ??= ManagedPhase48FontRegistry.Instance;
        int total = 0;
        foreach (uint scalar in scalars)
        {
            Check(registry.TryMeasureScalar(scalar, in style, out int advance), "measure-scalar");
            total = checked(total + advance);
        }
        return total;
    }

    private static Scene Styled(string body, string cssText)
    {
        return Styled(ToScalars("<!doctype html><html><head><style>" + cssText +
            "</style></head><body>" + body + "</body></html>"));
    }

    private static Scene Styled(IReadOnlyList<uint> documentScalars)
    {
        ManagedHtmlTreeBuilder builder = ParseScalars(documentScalars);
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "style-success");
        ManagedLayoutEngine layout = new(builder.Document, css,
            ManagedLayoutArenaOptions.Default, ManagedPhase48FontRegistry.Instance);
        ManagedPaintEngine paint = new(layout, ManagedPaintArenaOptions.Default,
            ManagedPhase48FontRegistry.Instance);
        return new Scene(builder, css, layout, paint);
    }

    private static ManagedHtmlTreeBuilder Parse(string value) => ParseScalars(ToScalars(value));

    private static ManagedHtmlTreeBuilder ParseScalars(IReadOnlyList<uint> scalars)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        for (int offset = 0; offset < scalars.Count;)
        {
            int length = Math.Min(13, scalars.Count - offset);
            uint[] input = new uint[length];
            for (int index = 0; index != length; ++index) input[index] = scalars[offset + index];
            Check(tokenizer.AppendInput(input), "tokenizer-input");
            ManagedHtmlTokenizerProcessResult result = tokenizer.Pump(builder);
            Check(result != ManagedHtmlTokenizerProcessResult.Failed &&
                  result != ManagedHtmlTokenizerProcessResult.Cancelled, "tokenizer-pump");
            offset += length;
        }
        Check(tokenizer.Pump(builder, true) == ManagedHtmlTokenizerProcessResult.Complete &&
              builder.Complete() && builder.Validate(out _), "document-complete");
        return builder;
    }

    private static List<uint> Decode(byte[] bytes, ManagedTextCharset charset)
    {
        ManagedTextDecoder decoder = new(charset);
        uint[] storage = new uint[Math.Max(bytes.Length + 8, 256)];
        ManagedTextDestinationConsumer consumer = new(storage);
        for (int offset = 0; offset < bytes.Length;)
        {
            int length = Math.Min(5, bytes.Length - offset);
            Check(decoder.AppendInput(bytes.AsSpan(offset, length)), "decoder-append");
            ManagedTextDecoderProcessResult result = decoder.Pump(consumer);
            Check(result != ManagedTextDecoderProcessResult.Failed &&
                  result != ManagedTextDecoderProcessResult.Cancelled, "decoder-pump");
            offset += length;
        }
        Check(decoder.Pump(consumer, true) == ManagedTextDecoderProcessResult.Complete,
            "decoder-complete");
        return storage.AsSpan(0, consumer.UnitsWritten).ToArray().ToList();
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

    private static int Count(IReadOnlyList<uint> values, uint scalar)
    {
        int count = 0;
        foreach (uint value in values) if (value == scalar) ++count;
        return count;
    }

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

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
            Builder = builder;
            Css = css;
            Layout = layout;
            Paint = paint;
        }

        internal ManagedHtmlTreeBuilder Builder { get; }
        internal ManagedCssEngine Css { get; }
        internal ManagedLayoutEngine Layout { get; }
        internal ManagedPaintEngine Paint { get; }
    }
}

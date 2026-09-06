using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedPhase48FontFaceId : byte
{
    Roboto9Regular = 0,
    Roboto9Bold = 1,
    Roboto9Italic = 2,
    Roboto9BoldItalic = 3,
    Roboto12Regular = 4,
    Roboto12Bold = 5,
    Roboto12Italic = 6,
    Roboto12BoldItalic = 7
}

[Flags]
public enum ManagedPhase50FontCoverageFlags : byte
{
    None = 0,
    BasicLatin = 1,
    Latin1Supplement = 2,
    CommonPunctuation = 4,
    Greek = 8,
    Cyrillic = 16
}

/// <summary>
/// Fixed Phase 50 coverage descriptor.  The arrays are generated data, not a
/// runtime registry; callers receive only bounded scalar values.
/// </summary>
public static class ManagedPhase50FontCoverage
{
    public const uint BasicLatinFirst = 0x20;
    public const uint BasicLatinLast = 0x7E;
    public const uint Latin1First = 0xA0;
    public const uint Latin1Last = 0xFF;
    public const int Latin1GlyphCount = 96;
    public const int SparsePunctuationCount = 10;
    public const int ExtendedGlyphCount = Latin1GlyphCount + SparsePunctuationCount;

    private static readonly uint[] s_sparseCodePoints =
    {
        0x2013, 0x2014, 0x2018, 0x2019, 0x201C,
        0x201D, 0x2022, 0x2026, 0x20AC, 0x2122
    };

    public static ManagedPhase50FontCoverageFlags Flags =>
        ManagedPhase50FontCoverageFlags.BasicLatin |
        ManagedPhase50FontCoverageFlags.Latin1Supplement |
        ManagedPhase50FontCoverageFlags.CommonPunctuation;

    public static int SparseCodePointCount => s_sparseCodePoints.Length;

    public static bool TryGetSparseCodePoint(int index, out uint scalar)
    {
        if ((uint)index >= (uint)s_sparseCodePoints.Length)
        {
            scalar = 0;
            return false;
        }
        scalar = s_sparseCodePoints[index];
        return true;
    }

    internal static bool IsBasicLatin(uint scalar) =>
        scalar >= BasicLatinFirst && scalar <= BasicLatinLast;

    internal static bool IsLatin1(uint scalar) =>
        scalar >= Latin1First && scalar <= Latin1Last;

    internal static bool IsSparsePunctuation(uint scalar)
    {
        return TryGetSparseIndex(scalar, out _);
    }

    internal static bool IsDeclared(uint scalar) =>
        IsBasicLatin(scalar) || IsLatin1(scalar) || IsSparsePunctuation(scalar);

    internal static bool TryGetExtendedIndex(uint scalar, out int index)
    {
        if (IsLatin1(scalar))
        {
            index = (int)(scalar - Latin1First);
            return true;
        }
        if (TryGetSparseIndex(scalar, out int sparseIndex))
        {
            index = Latin1GlyphCount + sparseIndex;
            return true;
        }
        index = 0;
        return false;
    }

    private static bool TryGetSparseIndex(uint scalar, out int index)
    {
        int low = 0;
        int high = s_sparseCodePoints.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            uint candidate = s_sparseCodePoints[middle];
            if (candidate == scalar)
            {
                index = middle;
                return true;
            }
            if (candidate < scalar) low = middle + 1;
            else high = middle - 1;
        }
        index = 0;
        return false;
    }

    internal static bool ValidateCodePointMap(ReadOnlySpan<uint> codePoints)
    {
        if (codePoints.Length != ExtendedGlyphCount) return false;
        for (int index = 0; index != Latin1GlyphCount; ++index)
            if (codePoints[index] != Latin1First + (uint)index) return false;
        uint previous = 0;
        for (int index = Latin1GlyphCount; index != codePoints.Length; ++index)
        {
            uint value = codePoints[index];
            if (index != Latin1GlyphCount && value <= previous) return false;
            if (!IsSparsePunctuation(value)) return false;
            previous = value;
        }
        return true;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedPhase48GlyphMetadata
{
    public ManagedPhase48GlyphMetadata(int atlasX, int atlasY, int width, int height,
                                       int advance, int bearingX, int bearingY,
                                       byte flags)
    {
        AtlasX = (ushort)Math.Clamp(atlasX, 0, ushort.MaxValue);
        AtlasY = (ushort)Math.Clamp(atlasY, 0, ushort.MaxValue);
        Width = (byte)Math.Clamp(width, 0, byte.MaxValue);
        Height = (byte)Math.Clamp(height, 0, byte.MaxValue);
        Advance = (byte)Math.Clamp(advance, 0, byte.MaxValue);
        BearingX = (sbyte)Math.Clamp(bearingX, sbyte.MinValue, sbyte.MaxValue);
        BearingY = (sbyte)Math.Clamp(bearingY, sbyte.MinValue, sbyte.MaxValue);
        Flags = flags;
    }

    public ushort AtlasX { get; }
    public ushort AtlasY { get; }
    public byte Width { get; }
    public byte Height { get; }
    public byte Advance { get; }
    public sbyte BearingX { get; }
    public sbyte BearingY { get; }
    public byte Flags { get; }
    public bool HasPixels => (Flags & 1) != 0;
    public bool UsesExtendedAtlas => (Flags & 2) != 0;
    public const int SizeInBytes = 10;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedPhase48FontTelemetry
{
    internal ManagedPhase48FontTelemetry(ManagedPhase48FontRegistry registry)
    {
        FaceCount = registry.FaceCount;
        ActiveFaceId = registry.ActiveFaceId;
        GlyphLookups = registry.GlyphLookups;
        GlyphHits = registry.GlyphHits;
        FallbackLookups = registry.FallbackLookups;
        AsciiLookups = registry.AsciiLookups;
        Latin1Lookups = registry.Latin1Lookups;
        SparsePunctuationLookups = registry.SparsePunctuationLookups;
        NonAsciiLookups = registry.NonAsciiLookups;
        GlyphFallbacks = registry.GlyphFallbacks;
        FaceFallbacks = registry.FaceFallbacks;
        NbspCount = registry.NbspCount;
        NonAsciiRasterizedGlyphs = registry.NonAsciiRasterizedGlyphs;
        BoldRequests = registry.BoldRequests;
        ItalicRequests = registry.ItalicRequests;
        BoldHits = registry.BoldHits;
        ItalicHits = registry.ItalicHits;
        AtlasBytes = registry.AtlasBytes;
        MetadataCount = registry.MetadataCount;
        LargestGlyphWidth = registry.LargestGlyphWidth;
        LargestGlyphHeight = registry.LargestGlyphHeight;
        MaximumAdvance = registry.MaximumAdvance;
        LayoutMeasurements = registry.LayoutMeasurements;
        RasterGlyphs = registry.RasterGlyphs;
    }

    public int FaceCount { get; }
    public ManagedPhase48FontFaceId ActiveFaceId { get; }
    public int GlyphLookups { get; }
    public int GlyphHits { get; }
    public int FallbackLookups { get; }
    public int AsciiLookups { get; }
    public int Latin1Lookups { get; }
    public int SparsePunctuationLookups { get; }
    public int NonAsciiLookups { get; }
    public int GlyphFallbacks { get; }
    public int FaceFallbacks { get; }
    public int NbspCount { get; }
    public int NonAsciiRasterizedGlyphs { get; }
    public int BoldRequests { get; }
    public int ItalicRequests { get; }
    public int BoldHits { get; }
    public int ItalicHits { get; }
    public int AtlasBytes { get; }
    public int MetadataCount { get; }
    public int LargestGlyphWidth { get; }
    public int LargestGlyphHeight { get; }
    public int MaximumAdvance { get; }
    public int LayoutMeasurements { get; }
    public int RasterGlyphs { get; }
}

public sealed class ManagedPhase48FontFace
{
    internal const int FirstCodePoint = 32;
    internal const int LastCodePoint = 126;

    private readonly bool _legacyValidationFixture;

    internal ManagedPhase48FontFace(ManagedPhase48FontFaceId id, string familyName,
                                    int nominalSize, int baseline, int ascent,
                                    int descent, int lineGap, byte[] atlas,
                                    byte[] extendedAtlas,
                                    ManagedPhase48GlyphMetadata[] glyphs,
                                    ManagedPhase48GlyphMetadata[] extendedGlyphs,
                                    bool legacyValidationFixture = false)
    {
        Id = id;
        FamilyName = familyName;
        NominalSize = nominalSize;
        Baseline = baseline;
        Ascent = ascent;
        Descent = descent;
        LineGap = lineGap;
        AtlasWidth = ManagedPhase48GeneratedFontData.AsciiAtlasWidth;
        AtlasHeight = ManagedPhase48GeneratedFontData.AsciiAtlasHeight;
        ExtendedAtlasWidth = ManagedPhase48GeneratedFontData.ExtendedAtlasWidth;
        ExtendedAtlasHeight = ManagedPhase48GeneratedFontData.ExtendedAtlasHeight;
        Atlas = atlas ?? Array.Empty<byte>();
        ExtendedAtlas = extendedAtlas ?? Array.Empty<byte>();
        Glyphs = glyphs ?? Array.Empty<ManagedPhase48GlyphMetadata>();
        ExtendedGlyphs = extendedGlyphs ?? Array.Empty<ManagedPhase48GlyphMetadata>();
        _legacyValidationFixture = legacyValidationFixture;
    }

    public static ManagedPhase48FontFace CreateForValidation(
        ManagedPhase48FontFaceId id, int nominalSize, int baseline, int ascent,
        int descent, int lineGap, byte[] atlas,
        ManagedPhase48GlyphMetadata[] glyphs)
    {
        return new ManagedPhase48FontFace(id, "validation", nominalSize, baseline,
            ascent, descent, lineGap, atlas, new byte[260 * 180], glyphs,
            new ManagedPhase48GlyphMetadata[ManagedPhase50FontCoverage.ExtendedGlyphCount], true);
    }

    public static ManagedPhase48FontFace CreateForValidation(
        ManagedPhase48FontFaceId id, int nominalSize, int baseline, int ascent,
        int descent, int lineGap, byte[] atlas, byte[] extendedAtlas,
        ManagedPhase48GlyphMetadata[] glyphs,
        ManagedPhase48GlyphMetadata[] extendedGlyphs)
    {
        return new ManagedPhase48FontFace(id, "validation", nominalSize, baseline,
            ascent, descent, lineGap, atlas, extendedAtlas, glyphs, extendedGlyphs);
    }

    public ManagedPhase48FontFaceId Id { get; }
    public string FamilyName { get; }
    public int NominalSize { get; }
    public int Baseline { get; }
    public int Ascent { get; }
    public int Descent { get; }
    public int LineGap { get; }
    public int LineHeight => Ascent + Descent + LineGap;
    public int AtlasWidth { get; }
    public int AtlasHeight { get; }
    public int ExtendedAtlasWidth { get; }
    public int ExtendedAtlasHeight { get; }
    public int AtlasPageCount => 2;
    public int GlyphCount => Glyphs.Length + ExtendedGlyphs.Length;
    public int AsciiGlyphCount => Glyphs.Length;
    public int ExtendedGlyphCount => ExtendedGlyphs.Length;
    public int AtlasByteCount => Atlas.Length + ExtendedAtlas.Length;
    public int AsciiAtlasByteCount => Atlas.Length;
    public int ExtendedAtlasByteCount => ExtendedAtlas.Length;
    public int FallbackGlyphIndex => '?' - FirstCodePoint;
    public int FirstMappedCodePoint => FirstCodePoint;
    public int LastMappedCodePoint => LastCodePoint;

    internal bool IsLegacyValidationFixture => _legacyValidationFixture;
    internal byte[] Atlas { get; }
    internal byte[] ExtendedAtlas { get; }
    internal ManagedPhase48GlyphMetadata[] Glyphs { get; }
    internal ManagedPhase48GlyphMetadata[] ExtendedGlyphs { get; }

    internal bool TryGetMappedMetadata(uint scalar,
                                       out ManagedPhase48GlyphMetadata metadata)
    {
        if (scalar >= FirstCodePoint && scalar <= LastCodePoint)
        {
            int index = (int)scalar - FirstCodePoint;
            if ((uint)index < (uint)Glyphs.Length)
            {
                metadata = Glyphs[index];
                return true;
            }
        }
        else if (ManagedPhase50FontCoverage.TryGetExtendedIndex(scalar, out int extendedIndex) &&
                 (uint)extendedIndex < (uint)ExtendedGlyphs.Length)
        {
            metadata = ExtendedGlyphs[extendedIndex];
            return true;
        }
        metadata = default;
        return false;
    }

    internal bool TryGetMetadata(uint scalar, out ManagedPhase48GlyphMetadata metadata,
                                 out bool fallback)
    {
        if (TryGetMappedMetadata(scalar, out metadata))
        {
            fallback = false;
            return true;
        }
        int index = FallbackGlyphIndex;
        fallback = true;
        if ((uint)index >= (uint)Glyphs.Length)
        {
            metadata = default;
            return false;
        }
        metadata = Glyphs[index];
        return true;
    }

    internal byte GetCoverage(in ManagedPhase48GlyphMetadata metadata, int row, int column)
    {
        if (row < 0 || column < 0 || row >= metadata.Height || column >= metadata.Width)
            return 0;
        byte[] atlas = metadata.UsesExtendedAtlas ? ExtendedAtlas : Atlas;
        int width = metadata.UsesExtendedAtlas ? ExtendedAtlasWidth : AtlasWidth;
        int height = metadata.UsesExtendedAtlas ? ExtendedAtlasHeight : AtlasHeight;
        int x = metadata.AtlasX + metadata.BearingX + column;
        int y = metadata.AtlasY + metadata.BearingY + Baseline + row;
        if (x < 0 || y < 0 || x >= width || y >= height)
            return 0;
        int offset = y * width + x;
        return (uint)offset < (uint)atlas.Length ? atlas[offset] : (byte)0;
    }
}

public sealed class ManagedPhase48FontRegistry : IManagedLayoutTextMetrics,
                                                  IManagedLayoutTextTypography,
                                                  IManagedRasterGlyphSource
{
    public static ManagedPhase48FontRegistry Instance { get; } = new();

    private readonly ManagedPhase48FontFace[] _faces;
    private readonly byte[] _semanticHash = new byte[ManagedSha256.DigestSize];
    private readonly int _atlasBytes;
    private readonly int _metadataCount;
    private readonly int _largestGlyphWidth;
    private readonly int _largestGlyphHeight;
    private readonly int _maximumAdvance;
    private ManagedPhase48FontFaceId _activeFaceId;
    private int _glyphLookups;
    private int _glyphHits;
    private int _fallbackLookups;
    private int _asciiLookups;
    private int _latin1Lookups;
    private int _sparsePunctuationLookups;
    private int _nonAsciiLookups;
    private int _glyphFallbacks;
    private int _faceFallbacks;
    private int _nbspCount;
    private int _nonAsciiRasterizedGlyphs;
    private int _boldRequests;
    private int _italicRequests;
    private int _boldHits;
    private int _italicHits;
    private int _layoutMeasurements;
    private int _rasterGlyphs;

    private ManagedPhase48FontRegistry()
    {
        _faces = new[]
        {
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto9Regular, "Roboto", 9, 12, 8, 4, 4,
                ManagedPhase48GeneratedFontData.Roboto9RegularAlpha,
                ManagedPhase48GeneratedFontData.Roboto9RegularExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto9RegularGlyphs,
                ManagedPhase48GeneratedFontData.Roboto9RegularExtendedGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto9Bold, "Roboto", 9, 12, 8, 4, 4,
                ManagedPhase48GeneratedFontData.Roboto9BoldAlpha,
                ManagedPhase48GeneratedFontData.Roboto9BoldExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto9BoldGlyphs,
                ManagedPhase48GeneratedFontData.Roboto9BoldExtendedGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto9Italic, "Roboto", 9, 12, 8, 4, 4,
                ManagedPhase48GeneratedFontData.Roboto9ItalicAlpha,
                ManagedPhase48GeneratedFontData.Roboto9ItalicExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto9ItalicGlyphs,
                ManagedPhase48GeneratedFontData.Roboto9ItalicExtendedGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto9BoldItalic, "Roboto", 9, 12, 8, 4, 4,
                ManagedPhase48GeneratedFontData.Roboto9BoldItalicAlpha,
                ManagedPhase48GeneratedFontData.Roboto9BoldItalicExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto9BoldItalicGlyphs,
                ManagedPhase48GeneratedFontData.Roboto9BoldItalicExtendedGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12Regular, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12RegularAlpha,
                ManagedPhase48GeneratedFontData.Roboto12RegularExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto12RegularGlyphs,
                ManagedPhase48GeneratedFontData.Roboto12RegularExtendedGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12Bold, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12BoldAlpha,
                ManagedPhase48GeneratedFontData.Roboto12BoldExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto12BoldGlyphs,
                ManagedPhase48GeneratedFontData.Roboto12BoldExtendedGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12Italic, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12ItalicAlpha,
                ManagedPhase48GeneratedFontData.Roboto12ItalicExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto12ItalicGlyphs,
                ManagedPhase48GeneratedFontData.Roboto12ItalicExtendedGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12BoldItalic, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12BoldItalicAlpha,
                ManagedPhase48GeneratedFontData.Roboto12BoldItalicExtendedAlpha,
                ManagedPhase48GeneratedFontData.Roboto12BoldItalicGlyphs,
                ManagedPhase48GeneratedFontData.Roboto12BoldItalicExtendedGlyphs)
        };
        foreach (ManagedPhase48FontFace face in _faces)
        {
            _atlasBytes += face.AtlasByteCount;
            _metadataCount += face.GlyphCount;
            for (int index = 0; index != face.Glyphs.Length; ++index)
            {
                ManagedPhase48GlyphMetadata glyph = face.Glyphs[index];
                _largestGlyphWidth = Math.Max(_largestGlyphWidth, glyph.Width);
                _largestGlyphHeight = Math.Max(_largestGlyphHeight, glyph.Height);
                _maximumAdvance = Math.Max(_maximumAdvance, glyph.Advance);
            }
            for (int index = 0; index != face.ExtendedGlyphs.Length; ++index)
            {
                ManagedPhase48GlyphMetadata glyph = face.ExtendedGlyphs[index];
                _largestGlyphWidth = Math.Max(_largestGlyphWidth, glyph.Width);
                _largestGlyphHeight = Math.Max(_largestGlyphHeight, glyph.Height);
                _maximumAdvance = Math.Max(_maximumAdvance, glyph.Advance);
            }
        }
        BuildSemanticHash(_semanticHash);
        ResetTelemetry();
    }

    public int FaceCount => _faces.Length;
    public ManagedPhase48FontFaceId ActiveFaceId => _activeFaceId;
    public ManagedPhase50FontCoverageFlags CoverageFlags => ManagedPhase50FontCoverage.Flags;
    public int AtlasBytes => _atlasBytes;
    public int MetadataCount => _metadataCount;
    public int GlyphsPerFace => (int)(ManagedPhase50FontCoverage.BasicLatinLast -
        ManagedPhase50FontCoverage.BasicLatinFirst + 1) + ManagedPhase50FontCoverage.ExtendedGlyphCount;
    public int LargestGlyphWidth => _largestGlyphWidth;
    public int LargestGlyphHeight => _largestGlyphHeight;
    public int MaximumAdvance => _maximumAdvance;
    public int GlyphLookups => _glyphLookups;
    public int GlyphHits => _glyphHits;
    public int FallbackLookups => _fallbackLookups;
    public int AsciiLookups => _asciiLookups;
    public int Latin1Lookups => _latin1Lookups;
    public int SparsePunctuationLookups => _sparsePunctuationLookups;
    public int NonAsciiLookups => _nonAsciiLookups;
    public int GlyphFallbacks => _glyphFallbacks;
    public int FaceFallbacks => _faceFallbacks;
    public int NbspCount => _nbspCount;
    public int NonAsciiRasterizedGlyphs => _nonAsciiRasterizedGlyphs;
    public int BoldRequests => _boldRequests;
    public int ItalicRequests => _italicRequests;
    public int BoldHits => _boldHits;
    public int ItalicHits => _italicHits;
    public int LayoutMeasurements => _layoutMeasurements;
    public int RasterGlyphs => _rasterGlyphs;

    public ManagedPhase48FontTelemetry Telemetry => new(this);

    public bool TryGetFace(ManagedPhase48FontFaceId id, out ManagedPhase48FontFace face)
    {
        int index = (int)id;
        if ((uint)index >= (uint)_faces.Length)
        {
            face = null!;
            return false;
        }
        face = _faces[index];
        return true;
    }

    public void ResetTelemetry()
    {
        _activeFaceId = ManagedPhase48FontFaceId.Roboto12Regular;
        _glyphLookups = 0;
        _glyphHits = 0;
        _fallbackLookups = 0;
        _asciiLookups = 0;
        _latin1Lookups = 0;
        _sparsePunctuationLookups = 0;
        _nonAsciiLookups = 0;
        _glyphFallbacks = 0;
        _faceFallbacks = 0;
        _nbspCount = 0;
        _nonAsciiRasterizedGlyphs = 0;
        _boldRequests = 0;
        _italicRequests = 0;
        _boldHits = 0;
        _italicHits = 0;
        _layoutMeasurements = 0;
        _rasterGlyphs = 0;
    }

    public bool TryCopySemanticHash(Span<byte> destination)
    {
        if (destination.Length < _semanticHash.Length) return false;
        _semanticHash.AsSpan().CopyTo(destination);
        return true;
    }

    public bool TryMeasureScalar(uint scalar, in ManagedLayoutTextStyle style, out int advance)
    {
        advance = 0;
        if (!ValidScalar(scalar) || !TrySelectFace(style, out ManagedPhase48FontFace face))
            return false;
        if (!ResolveMetadata(face, scalar, out _, out ManagedPhase48GlyphMetadata glyph,
                             out bool glyphFallback, out bool faceFallback)) return false;
        RecordLookup(scalar, face, glyphFallback, faceFallback, true);
        advance = glyph.Advance;
        return advance > 0 && advance <= ManagedLayoutLimits.MaximumCoordinate;
    }

    public int GetLineHeight(in ManagedLayoutTextStyle style)
    {
        return TrySelectFace(style, out ManagedPhase48FontFace face) ? face.LineHeight : 0;
    }

    public int GetBaseline(in ManagedLayoutTextStyle style)
    {
        return TrySelectFace(style, out ManagedPhase48FontFace face) ? face.Baseline : 0;
    }

    public bool TryGetGlyph(uint scalar, ManagedPaintFontId fontId, int fontSize,
                            int fontWeight, ManagedCssFontStyle fontStyle,
                            out ManagedRasterGlyph glyph)
    {
        glyph = default;
        if (fontId != ManagedPaintFontId.DefaultUi || !ValidScalar(scalar) ||
            !TrySelectFace(fontSize, fontWeight, fontStyle, out ManagedPhase48FontFace face))
            return false;
        if (!ResolveMetadata(face, scalar, out ManagedPhase48FontFace resolvedFace,
                             out ManagedPhase48GlyphMetadata metadata,
                             out bool glyphFallback, out bool faceFallback)) return false;
        RecordLookup(scalar, resolvedFace, glyphFallback, faceFallback, false);
        ++_rasterGlyphs;
        if (scalar > ManagedPhase50FontCoverage.BasicLatinLast && !glyphFallback)
            ++_nonAsciiRasterizedGlyphs;
        glyph = new ManagedRasterGlyph(resolvedFace, metadata, glyphFallback);
        return true;
    }

    private bool ResolveMetadata(ManagedPhase48FontFace requestedFace, uint scalar,
                                  out ManagedPhase48FontFace resolvedFace,
                                  out ManagedPhase48GlyphMetadata metadata,
                                  out bool glyphFallback, out bool faceFallback)
    {
        resolvedFace = requestedFace;
        faceFallback = false;
        if (requestedFace.TryGetMappedMetadata(scalar, out metadata))
        {
            glyphFallback = false;
            return true;
        }
        if (ManagedPhase50FontCoverage.IsDeclared(scalar))
        {
            ManagedPhase48FontFace regularFace = RegularFaceFor(requestedFace);
            if (regularFace != requestedFace && regularFace.TryGetMappedMetadata(scalar, out metadata))
            {
                resolvedFace = regularFace;
                faceFallback = true;
                glyphFallback = false;
                return true;
            }
        }
        glyphFallback = true;
        return requestedFace.TryGetMetadata(scalar, out metadata, out _);
    }

    private static ManagedPhase48FontFace RegularFaceFor(ManagedPhase48FontFace face)
    {
        int index = (int)face.Id;
        index = index < 4 ? 0 : 4;
        return Instance._faces[index];
    }

    private bool TrySelectFace(in ManagedLayoutTextStyle style, out ManagedPhase48FontFace face)
    {
        return TrySelectFace(style.FontSize, style.FontWeight, style.FontStyle, out face);
    }

    private bool TrySelectFace(int fontSize, int fontWeight, ManagedCssFontStyle fontStyle,
                               out ManagedPhase48FontFace face)
    {
        face = null!;
        if (fontSize <= 0 || fontSize > ManagedLayoutLimits.MaximumCoordinate ||
            fontWeight <= 0) return false;
        bool bold = fontWeight >= 700;
        bool italic = fontStyle == ManagedCssFontStyle.Italic;
        int familyOffset = fontSize <= 10 ? 0 : 4;
        int styleOffset = (bold ? 1 : 0) + (italic ? 2 : 0);
        int index = familyOffset + styleOffset;
        if ((uint)index >= (uint)_faces.Length) return false;
        face = _faces[index];
        _activeFaceId = face.Id;
        if (bold) ++_boldRequests;
        if (italic) ++_italicRequests;
        if (bold) ++_boldHits;
        if (italic) ++_italicHits;
        return true;
    }

    private void RecordLookup(uint scalar, ManagedPhase48FontFace face,
                              bool glyphFallback, bool faceFallback, bool layout)
    {
        ++_glyphLookups;
        ++_glyphHits;
        if (ManagedPhase50FontCoverage.IsBasicLatin(scalar)) ++_asciiLookups;
        else if (ManagedPhase50FontCoverage.IsLatin1(scalar)) ++_latin1Lookups;
        else if (ManagedPhase50FontCoverage.IsSparsePunctuation(scalar)) ++_sparsePunctuationLookups;
        if (scalar > ManagedPhase50FontCoverage.BasicLatinLast) ++_nonAsciiLookups;
        if (glyphFallback) { ++_glyphFallbacks; ++_fallbackLookups; }
        if (faceFallback) ++_faceFallbacks;
        if (scalar == 0xA0) ++_nbspCount;
        if (layout) ++_layoutMeasurements;
    }

    private static bool ValidScalar(uint scalar)
    {
        return scalar <= 0x10FFFFU && !(scalar >= 0xD800U && scalar <= 0xDFFFU);
    }

    private void BuildSemanticHash(Span<byte> destination)
    {
        ManagedSha256 hash = new();
        hash.Append("GXOS-P50-FONT\0"u8);
        Span<byte> values = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(values, _faces.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(values[4..], ManagedPhase50FontCoverage.BasicLatinFirst);
        BinaryPrimitives.WriteUInt32LittleEndian(values[8..], ManagedPhase50FontCoverage.BasicLatinLast);
        BinaryPrimitives.WriteInt32LittleEndian(values[12..], ManagedPhase48GlyphMetadata.SizeInBytes);
        hash.Append(values);
        BinaryPrimitives.WriteInt32LittleEndian(values, ManagedPhase50FontCoverage.Latin1GlyphCount);
        BinaryPrimitives.WriteInt32LittleEndian(values[4..], ManagedPhase50FontCoverage.SparsePunctuationCount);
        BinaryPrimitives.WriteInt32LittleEndian(values[8..], ManagedPhase48GeneratedFontData.ExtendedAtlasWidth);
        BinaryPrimitives.WriteInt32LittleEndian(values[12..], ManagedPhase48GeneratedFontData.ExtendedAtlasHeight);
        hash.Append(values);
        foreach (uint codePoint in ManagedPhase48GeneratedFontData.ExtendedCodePoints)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(values, codePoint);
            hash.Append(values[..4]);
        }
        Span<byte> metadata = stackalloc byte[ManagedPhase48GlyphMetadata.SizeInBytes];
        foreach (ManagedPhase48FontFace face in _faces)
        {
            BinaryPrimitives.WriteInt32LittleEndian(values, (int)face.Id);
            BinaryPrimitives.WriteInt32LittleEndian(values[4..], face.NominalSize);
            BinaryPrimitives.WriteInt32LittleEndian(values[8..], face.Baseline);
            BinaryPrimitives.WriteInt32LittleEndian(values[12..], face.LineHeight);
            hash.Append(values);
            AppendMetadataAndAtlas(hash, face.Glyphs, face.Atlas, metadata);
            AppendMetadataAndAtlas(hash, face.ExtendedGlyphs, face.ExtendedAtlas, metadata);
        }
        hash.TryFinalize(destination);
    }

    private static void AppendMetadataAndAtlas(ManagedSha256 hash,
                                                ManagedPhase48GlyphMetadata[] glyphs,
                                                byte[] atlas, Span<byte> metadata)
    {
        foreach (ManagedPhase48GlyphMetadata glyph in glyphs)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(metadata, glyph.AtlasX);
            BinaryPrimitives.WriteUInt16LittleEndian(metadata[2..], glyph.AtlasY);
            metadata[4] = glyph.Width;
            metadata[5] = glyph.Height;
            metadata[6] = glyph.Advance;
            metadata[7] = unchecked((byte)glyph.BearingX);
            metadata[8] = unchecked((byte)glyph.BearingY);
            metadata[9] = glyph.Flags;
            hash.Append(metadata);
        }
        hash.Append(atlas);
    }
}

public enum ManagedPhase48FontValidationFailureReason : byte
{
    None = 0,
    NullFace = 1,
    InvalidFaceId = 2,
    InvalidMetrics = 3,
    InvalidAtlasGeometry = 4,
    InvalidAtlasLength = 5,
    InvalidGlyphCount = 6,
    InvalidGlyphBounds = 7,
    InvalidGlyphDimensions = 8,
    InvalidAdvance = 9,
    MissingFallback = 10,
    InvalidExtendedAtlasGeometry = 11,
    InvalidExtendedAtlasLength = 12,
    InvalidExtendedGlyphCount = 13,
    InvalidCoverageMapping = 14,
    DuplicateSparseMapping = 15,
    InvalidFallbackTarget = 16,
    InvalidFaceFallback = 17,
    InvalidNbspMetadata = 18
}

public static class ManagedPhase48FontValidator
{
    public static bool Validate(ManagedPhase48FontFace face,
                                 out ManagedPhase48FontValidationFailureReason reason)
    {
        reason = ManagedPhase48FontValidationFailureReason.None;
        if (face == null) return Fail(ManagedPhase48FontValidationFailureReason.NullFace, out reason);
        if ((int)face.Id >= 8) return Fail(ManagedPhase48FontValidationFailureReason.InvalidFaceId, out reason);
        if (face.NominalSize <= 0 || face.Baseline <= 0 || face.Ascent <= 0 ||
            face.Descent <= 0 || face.LineGap < 0 || face.LineHeight <= face.Baseline)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidMetrics, out reason);
        if (face.AtlasWidth != 260 || face.AtlasHeight != 160)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidAtlasGeometry, out reason);
        if (face.AtlasByteCount - face.ExtendedAtlasByteCount != face.AtlasWidth * face.AtlasHeight)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidAtlasLength, out reason);
        if (face.Glyphs.Length != 95)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidGlyphCount, out reason);
        if (!ValidateGlyphArray(face.Glyphs, face.AtlasWidth, face.AtlasHeight, false, out reason)) return false;
        if (face.FallbackGlyphIndex < 0 || face.FallbackGlyphIndex >= face.Glyphs.Length ||
            !face.Glyphs[face.FallbackGlyphIndex].HasPixels)
            return Fail(ManagedPhase48FontValidationFailureReason.MissingFallback, out reason);
        if (face.IsLegacyValidationFixture) return true;
        if (face.ExtendedAtlasWidth != 260 || face.ExtendedAtlasHeight != 180)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidExtendedAtlasGeometry, out reason);
        if (face.ExtendedAtlasByteCount != face.ExtendedAtlasWidth * face.ExtendedAtlasHeight)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidExtendedAtlasLength, out reason);
        if (face.ExtendedGlyphs.Length != ManagedPhase50FontCoverage.ExtendedGlyphCount)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidExtendedGlyphCount, out reason);
        if (!ManagedPhase50FontCoverage.ValidateCodePointMap(ManagedPhase48GeneratedFontData.ExtendedCodePoints))
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidCoverageMapping, out reason);
        if (!ValidateGlyphArray(face.ExtendedGlyphs, face.ExtendedAtlasWidth,
                                face.ExtendedAtlasHeight, true, out reason)) return false;
        if (!face.TryGetMappedMetadata(0xA0, out ManagedPhase48GlyphMetadata nbsp) ||
            nbsp.HasPixels || nbsp.Advance == 0 || !nbsp.UsesExtendedAtlas)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidNbspMetadata, out reason);
        for (uint scalar = ManagedPhase50FontCoverage.Latin1First;
             scalar <= ManagedPhase50FontCoverage.Latin1Last; ++scalar)
            if (!face.TryGetMappedMetadata(scalar, out _))
                return Fail(ManagedPhase48FontValidationFailureReason.InvalidCoverageMapping, out reason);
        for (int index = 0; index != ManagedPhase50FontCoverage.SparsePunctuationCount; ++index)
        {
            if (!ManagedPhase50FontCoverage.TryGetSparseCodePoint(index, out uint scalar) ||
                !face.TryGetMappedMetadata(scalar, out _))
                return Fail(ManagedPhase48FontValidationFailureReason.InvalidCoverageMapping, out reason);
        }
        return true;
    }

    public static bool ValidateRegistry(ManagedPhase48FontRegistry registry,
                                         out ManagedPhase48FontValidationFailureReason reason)
    {
        reason = ManagedPhase48FontValidationFailureReason.None;
        if (registry == null) return Fail(ManagedPhase48FontValidationFailureReason.NullFace, out reason);
        if (registry.FaceCount != 8) return Fail(ManagedPhase48FontValidationFailureReason.InvalidFaceId, out reason);
        if (registry.GlyphsPerFace != 95 + ManagedPhase50FontCoverage.ExtendedGlyphCount)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidGlyphCount, out reason);
        if (!ManagedPhase50FontCoverage.ValidateCodePointMap(ManagedPhase48GeneratedFontData.ExtendedCodePoints))
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidCoverageMapping, out reason);
        for (int index = 0; index != registry.FaceCount; ++index)
        {
            if (!registry.TryGetFace((ManagedPhase48FontFaceId)index, out ManagedPhase48FontFace face) ||
                !Validate(face, out reason)) return false;
        }
        return true;
    }

    public static bool ValidateSparseCodePointMap(ReadOnlySpan<uint> codePoints)
    {
        if (codePoints.Length != ManagedPhase50FontCoverage.SparsePunctuationCount) return false;
        uint previous = 0;
        for (int index = 0; index != codePoints.Length; ++index)
        {
            uint current = codePoints[index];
            if (index != 0 && current <= previous) return false;
            if (!ManagedPhase50FontCoverage.IsSparsePunctuation(current)) return false;
            previous = current;
        }
        return true;
    }

    private static bool ValidateGlyphArray(ManagedPhase48GlyphMetadata[] glyphs,
                                           int atlasWidth, int atlasHeight,
                                           bool extended,
                                           out ManagedPhase48FontValidationFailureReason reason)
    {
        reason = ManagedPhase48FontValidationFailureReason.None;
        for (int index = 0; index != glyphs.Length; ++index)
        {
            ManagedPhase48GlyphMetadata glyph = glyphs[index];
            if (glyph.UsesExtendedAtlas != extended || glyph.AtlasX + 20 > atlasWidth ||
                glyph.AtlasY + 20 > atlasHeight)
                return Fail(extended ? ManagedPhase48FontValidationFailureReason.InvalidExtendedAtlasGeometry : ManagedPhase48FontValidationFailureReason.InvalidGlyphBounds, out reason);
            if (glyph.Width > 20 || glyph.Height > 20 || glyph.BearingX < 0 ||
                glyph.BearingX + glyph.Width > 20 || glyph.BearingY < -20 ||
                glyph.BearingY + glyph.Height > 20 || glyph.Advance == 0)
                return Fail(glyph.Advance == 0 ? ManagedPhase48FontValidationFailureReason.InvalidAdvance : ManagedPhase48FontValidationFailureReason.InvalidGlyphDimensions, out reason);
        }
        return true;
    }

    private static bool Fail(ManagedPhase48FontValidationFailureReason value,
                             out ManagedPhase48FontValidationFailureReason reason)
    {
        reason = value;
        return false;
    }
}

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
        NonAsciiLookups = registry.NonAsciiLookups;
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
    public int NonAsciiLookups { get; }
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

    internal ManagedPhase48FontFace(ManagedPhase48FontFaceId id, string familyName,
                                    int nominalSize, int baseline, int ascent,
                                    int descent, int lineGap, byte[] atlas,
                                    ManagedPhase48GlyphMetadata[] glyphs)
    {
        Id = id;
        FamilyName = familyName;
        NominalSize = nominalSize;
        Baseline = baseline;
        Ascent = ascent;
        Descent = descent;
        LineGap = lineGap;
        AtlasWidth = 260;
        AtlasHeight = 160;
        Atlas = atlas ?? Array.Empty<byte>();
        Glyphs = glyphs ?? Array.Empty<ManagedPhase48GlyphMetadata>();
    }

    public static ManagedPhase48FontFace CreateForValidation(
        ManagedPhase48FontFaceId id, int nominalSize, int baseline, int ascent,
        int descent, int lineGap, byte[] atlas,
        ManagedPhase48GlyphMetadata[] glyphs)
    {
        return new ManagedPhase48FontFace(id, "validation", nominalSize, baseline,
            ascent, descent, lineGap, atlas, glyphs);
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
    public int GlyphCount => Glyphs.Length;
    public int AtlasByteCount => Atlas.Length;
    public int FallbackGlyphIndex => '?' - FirstCodePoint;
    public int FirstMappedCodePoint => FirstCodePoint;
    public int LastMappedCodePoint => LastCodePoint;

    internal byte[] Atlas { get; }
    internal ManagedPhase48GlyphMetadata[] Glyphs { get; }

    internal bool TryGetMetadata(uint scalar, out ManagedPhase48GlyphMetadata metadata,
                                 out bool fallback)
    {
        fallback = false;
        int index;
        if (scalar >= FirstCodePoint && scalar <= LastCodePoint)
        {
            index = (int)scalar - FirstCodePoint;
        }
        else
        {
            index = FallbackGlyphIndex;
            fallback = true;
        }
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
        int x = metadata.AtlasX + metadata.BearingX + column;
        int y = metadata.AtlasY + metadata.BearingY + Baseline + row;
        if (x < 0 || y < 0 || x >= AtlasWidth || y >= AtlasHeight)
            return 0;
        int offset = y * AtlasWidth + x;
        return (uint)offset < (uint)Atlas.Length ? Atlas[offset] : (byte)0;
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
    private int _nonAsciiLookups;
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
                ManagedPhase48GeneratedFontData.Roboto9RegularAlpha, ManagedPhase48GeneratedFontData.Roboto9RegularGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto9Bold, "Roboto", 9, 12, 8, 4, 4,
                ManagedPhase48GeneratedFontData.Roboto9BoldAlpha, ManagedPhase48GeneratedFontData.Roboto9BoldGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto9Italic, "Roboto", 9, 12, 8, 4, 4,
                ManagedPhase48GeneratedFontData.Roboto9ItalicAlpha, ManagedPhase48GeneratedFontData.Roboto9ItalicGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto9BoldItalic, "Roboto", 9, 12, 8, 4, 4,
                ManagedPhase48GeneratedFontData.Roboto9BoldItalicAlpha, ManagedPhase48GeneratedFontData.Roboto9BoldItalicGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12Regular, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12RegularAlpha, ManagedPhase48GeneratedFontData.Roboto12RegularGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12Bold, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12BoldAlpha, ManagedPhase48GeneratedFontData.Roboto12BoldGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12Italic, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12ItalicAlpha, ManagedPhase48GeneratedFontData.Roboto12ItalicGlyphs),
            new ManagedPhase48FontFace(ManagedPhase48FontFaceId.Roboto12BoldItalic, "Roboto", 12, 13, 9, 5, 4,
                ManagedPhase48GeneratedFontData.Roboto12BoldItalicAlpha, ManagedPhase48GeneratedFontData.Roboto12BoldItalicGlyphs)
        };
        foreach (ManagedPhase48FontFace face in _faces)
        {
            _atlasBytes += face.AtlasByteCount;
            _metadataCount += face.GlyphCount;
            foreach (ManagedPhase48GlyphMetadata glyph in face.Glyphs)
            {
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
    public int AtlasBytes => _atlasBytes;
    public int MetadataCount => _metadataCount;
    public int LargestGlyphWidth => _largestGlyphWidth;
    public int LargestGlyphHeight => _largestGlyphHeight;
    public int MaximumAdvance => _maximumAdvance;
    public int GlyphLookups => _glyphLookups;
    public int GlyphHits => _glyphHits;
    public int FallbackLookups => _fallbackLookups;
    public int AsciiLookups => _asciiLookups;
    public int NonAsciiLookups => _nonAsciiLookups;
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
        _nonAsciiLookups = 0;
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
        if (!face.TryGetMetadata(scalar, out ManagedPhase48GlyphMetadata glyph, out bool fallback))
            return false;
        RecordLookup(scalar, style, face, fallback, true);
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
        if (!face.TryGetMetadata(scalar, out ManagedPhase48GlyphMetadata metadata,
                                 out bool fallback)) return false;
        RecordLookup(scalar, new ManagedLayoutTextStyle(fontSize, fontWeight, fontStyle),
                     face, fallback, false);
        ++_rasterGlyphs;
        glyph = new ManagedRasterGlyph(face, metadata, fallback);
        return true;
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

    private void RecordLookup(uint scalar, in ManagedLayoutTextStyle style,
                              ManagedPhase48FontFace face, bool fallback, bool layout)
    {
        ++_glyphLookups;
        ++_glyphHits;
        if (scalar >= ManagedPhase48FontFace.FirstCodePoint &&
            scalar <= ManagedPhase48FontFace.LastCodePoint) ++_asciiLookups;
        else { ++_nonAsciiLookups; ++_fallbackLookups; }
        if (fallback && scalar >= ManagedPhase48FontFace.FirstCodePoint &&
            scalar <= ManagedPhase48FontFace.LastCodePoint) ++_fallbackLookups;
        if (layout) ++_layoutMeasurements;
    }

    private static bool ValidScalar(uint scalar)
    {
        return scalar <= 0x10FFFFU && !(scalar >= 0xD800U && scalar <= 0xDFFFU);
    }

    private void BuildSemanticHash(Span<byte> destination)
    {
        ManagedSha256 hash = new();
        hash.Append("GXOS-P48-FONT\0"u8);
        Span<byte> values = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(values, _faces.Length);
        BinaryPrimitives.WriteInt32LittleEndian(values[4..], ManagedPhase48FontFace.FirstCodePoint);
        BinaryPrimitives.WriteInt32LittleEndian(values[8..], ManagedPhase48FontFace.LastCodePoint);
        BinaryPrimitives.WriteInt32LittleEndian(values[12..], ManagedPhase48GlyphMetadata.SizeInBytes);
        hash.Append(values);
        Span<byte> metadata = stackalloc byte[ManagedPhase48GlyphMetadata.SizeInBytes];
        foreach (ManagedPhase48FontFace face in _faces)
        {
            BinaryPrimitives.WriteInt32LittleEndian(values, (int)face.Id);
            BinaryPrimitives.WriteInt32LittleEndian(values[4..], face.NominalSize);
            BinaryPrimitives.WriteInt32LittleEndian(values[8..], face.Baseline);
            BinaryPrimitives.WriteInt32LittleEndian(values[12..], face.LineHeight);
            hash.Append(values);
            foreach (ManagedPhase48GlyphMetadata glyph in face.Glyphs)
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
            hash.Append(face.Atlas);
        }
        hash.TryFinalize(destination);
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
    MissingFallback = 10
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
        if (face.AtlasByteCount != face.AtlasWidth * face.AtlasHeight)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidAtlasLength, out reason);
        if (face.GlyphCount != 95)
            return Fail(ManagedPhase48FontValidationFailureReason.InvalidGlyphCount, out reason);
        for (int index = 0; index != face.Glyphs.Length; ++index)
        {
            ManagedPhase48GlyphMetadata glyph = face.Glyphs[index];
            if (glyph.AtlasX + 20 > face.AtlasWidth || glyph.AtlasY + 20 > face.AtlasHeight)
                return Fail(ManagedPhase48FontValidationFailureReason.InvalidGlyphBounds, out reason);
            if (glyph.Width > 20 || glyph.Height > 20 || glyph.BearingX < 0 ||
                glyph.BearingX + glyph.Width > 20 || glyph.BearingY + face.Baseline < 0 ||
                glyph.BearingY + face.Baseline + glyph.Height > 20)
                return Fail(ManagedPhase48FontValidationFailureReason.InvalidGlyphDimensions, out reason);
            if (glyph.Advance == 0)
                return Fail(ManagedPhase48FontValidationFailureReason.InvalidAdvance, out reason);
        }
        if (face.FallbackGlyphIndex < 0 || face.FallbackGlyphIndex >= face.GlyphCount ||
            !face.Glyphs[face.FallbackGlyphIndex].HasPixels)
            return Fail(ManagedPhase48FontValidationFailureReason.MissingFallback, out reason);
        return true;
    }

    public static bool ValidateRegistry(ManagedPhase48FontRegistry registry,
                                         out ManagedPhase48FontValidationFailureReason reason)
    {
        reason = ManagedPhase48FontValidationFailureReason.None;
        if (registry == null) return Fail(ManagedPhase48FontValidationFailureReason.NullFace, out reason);
        if (registry.FaceCount != 8) return Fail(ManagedPhase48FontValidationFailureReason.InvalidGlyphCount, out reason);
        for (int index = 0; index != registry.FaceCount; ++index)
        {
            if (!registry.TryGetFace((ManagedPhase48FontFaceId)index, out ManagedPhase48FontFace face) ||
                !Validate(face, out reason)) return false;
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

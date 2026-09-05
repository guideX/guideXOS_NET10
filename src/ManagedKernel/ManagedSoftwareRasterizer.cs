using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedRasterPixelFormat : byte
{
    Argb8888 = 0
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedFramebuffer
{
    public ManagedFramebuffer(uint[] backingStorage, int width, int height)
        : this(backingStorage, 0, width, height, width, ManagedRasterPixelFormat.Argb8888) { }

    public ManagedFramebuffer(uint[] backingStorage, int width, int height, int stride,
                              ManagedRasterPixelFormat pixelFormat = ManagedRasterPixelFormat.Argb8888)
        : this(backingStorage, 0, width, height, stride, pixelFormat) { }

    public ManagedFramebuffer(uint[] backingStorage, int offset, int width, int height,
                              int stride,
                              ManagedRasterPixelFormat pixelFormat = ManagedRasterPixelFormat.Argb8888)
    {
        BackingStorage = backingStorage;
        Offset = offset;
        Width = width;
        Height = height;
        Stride = stride;
        PixelFormat = pixelFormat;
    }

    public uint[]? BackingStorage { get; }
    public int Offset { get; }
    public int Width { get; }
    public int Height { get; }

    /* Stride is measured in uint pixels, not bytes.  The semantic hash uses
       Width pixels from each row and deliberately excludes padding. */
    public int Stride { get; }
    public ManagedRasterPixelFormat PixelFormat { get; }
    public long ActiveBytes => TryMultiply((long)Math.Max(0, Width),
                                            (long)Math.Max(0, Height), sizeof(uint));
    public long BackingBytes => (long)(BackingStorage?.Length ?? 0) * sizeof(uint);

    private static long TryMultiply(long left, long middle, long right)
    {
        if (left == 0 || middle == 0 || right == 0) return 0;
        if (left > long.MaxValue / middle) return long.MaxValue;
        long result = left * middle;
        return result > long.MaxValue / right ? long.MaxValue : result * right;
    }

    public bool TryGetPixel(int x, int y, out uint pixel)
    {
        pixel = 0;
        if (BackingStorage == null || x < 0 || y < 0 || x >= Width || y >= Height ||
            Stride <= 0 || x >= Stride)
            return false;
        long index = (long)Offset + (long)y * Stride + x;
        if (index < 0 || index >= BackingStorage.Length) return false;
        pixel = BackingStorage[(int)index];
        return true;
    }
}

public readonly struct ManagedRasterRenderOptions
{
    public ManagedRasterRenderOptions(bool clear, uint clearColor)
    {
        Clear = clear;
        ClearColor = clearColor;
        IsSpecified = true;
    }

    public static ManagedRasterRenderOptions ClearBlack => new(true, 0xFF000000U);
    public static ManagedRasterRenderOptions Preserve => new(false, 0);
    public bool Clear { get; }
    public uint ClearColor { get; }
    internal bool IsSpecified { get; }
}

public readonly struct ManagedRasterizerOptions
{
    public ManagedRasterizerOptions(int clipStackCapacity)
    {
        if (clipStackCapacity <= 0 || clipStackCapacity > 256)
            throw new ArgumentOutOfRangeException(nameof(clipStackCapacity));
        ClipStackCapacity = clipStackCapacity;
    }

    public static ManagedRasterizerOptions Default => new(64);
    public int ClipStackCapacity { get; }
}

public enum ManagedRasterState : byte
{
    Reset = 0,
    Complete = 1,
    Failed = 2,
    Cancelled = 3
}

public enum ManagedRasterFailureReason : byte
{
    None = 0,
    InvalidFramebuffer = 1,
    FramebufferTooSmall = 2,
    FramebufferGeometryOverflow = 3,
    UnsupportedPixelFormat = 4,
    InvalidDisplayList = 5,
    InvalidPaintCommand = 6,
    InvalidTextReference = 7,
    RasterClipDepthExceeded = 8,
    GlyphSourceFailure = 9,
    Cancelled = 10,
    InvalidState = 11
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedRasterDirtyBounds
{
    internal ManagedRasterDirtyBounds(bool empty, int minX, int minY, int maxX, int maxY)
    {
        IsEmpty = empty;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public bool IsEmpty { get; }
    public int MinX { get; }
    public int MinY { get; }
    public int MaxX { get; }
    public int MaxY { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedRasterGlyph
{
    public ManagedRasterGlyph(int width, int height, int advance, bool fallback,
                              uint row0, uint row1, uint row2, uint row3,
                              uint row4, uint row5, uint row6, uint row7)
    {
        Width = width;
        Height = height;
        Advance = advance;
        IsFallback = fallback;
        _row0 = row0;
        _row1 = row1;
        _row2 = row2;
        _row3 = row3;
        _row4 = row4;
        _row5 = row5;
        _row6 = row6;
        _row7 = row7;
    }

    private readonly uint _row0;
    private readonly uint _row1;
    private readonly uint _row2;
    private readonly uint _row3;
    private readonly uint _row4;
    private readonly uint _row5;
    private readonly uint _row6;
    private readonly uint _row7;

    public int Width { get; }
    public int Height { get; }
    public int Advance { get; }
    public bool IsFallback { get; }

    public uint GetRowMask(int row) => row switch
    {
        0 => _row0,
        1 => _row1,
        2 => _row2,
        3 => _row3,
        4 => _row4,
        5 => _row5,
        6 => _row6,
        7 => _row7,
        _ => 0
    };
}

public interface IManagedRasterGlyphSource
{
    bool TryGetGlyph(uint scalar, ManagedPaintFontId fontId, int fontSize,
                     int fontWeight, ManagedCssFontStyle fontStyle,
                     out ManagedRasterGlyph glyph);
}

/// <summary>
/// Small allocation-free proof glyph source.  The atlas is seven rows of
/// five-bit masks; font size selects a fixed nearest-neighbour integer scale.
/// Lowercase letters intentionally use the corresponding uppercase proof
/// shape so the set stays compact and deterministic.
/// </summary>
public sealed class ManagedProofGlyphSource : IManagedRasterGlyphSource
{
    public static ManagedProofGlyphSource Instance { get; } = new();

    private ManagedProofGlyphSource() { }

    public bool TryGetGlyph(uint scalar, ManagedPaintFontId fontId, int fontSize,
                            int fontWeight, ManagedCssFontStyle fontStyle,
                            out ManagedRasterGlyph glyph)
    {
        glyph = default;
        if (fontId != ManagedPaintFontId.DefaultUi || fontSize <= 0 || fontSize > 64)
            return false;
        int scale = Math.Clamp(fontSize / 8, 1, 4);
        if (!TryGetPattern(scalar, out GlyphPattern pattern, out bool fallback))
            return false;
        glyph = Scale(pattern, scale, fallback);
        return true;
    }

    private static ManagedRasterGlyph Scale(GlyphPattern pattern, int scale, bool fallback)
    {
        return new ManagedRasterGlyph(5 * scale, 7 * scale, 6 * scale, fallback,
            ScaleRow(pattern.Row0, scale), ScaleRow(pattern.Row1, scale),
            ScaleRow(pattern.Row2, scale), ScaleRow(pattern.Row3, scale),
            ScaleRow(pattern.Row4, scale), ScaleRow(pattern.Row5, scale),
            ScaleRow(pattern.Row6, scale), 0);
    }

    private static uint ScaleRow(byte row, int scale)
    {
        uint result = 0;
        for (int bit = 0; bit != 5; ++bit)
        {
            if ((row & (1 << (4 - bit))) == 0) continue;
            for (int copy = 0; copy != scale; ++copy)
            {
                int target = bit * scale + copy;
                result |= 1U << (24 - target);
            }
        }
        return result;
    }

    private readonly struct GlyphPattern
    {
        internal GlyphPattern(byte row0, byte row1, byte row2, byte row3,
                              byte row4, byte row5, byte row6)
        {
            Row0 = row0; Row1 = row1; Row2 = row2; Row3 = row3;
            Row4 = row4; Row5 = row5; Row6 = row6;
        }

        internal byte Row0 { get; }
        internal byte Row1 { get; }
        internal byte Row2 { get; }
        internal byte Row3 { get; }
        internal byte Row4 { get; }
        internal byte Row5 { get; }
        internal byte Row6 { get; }
    }

    private static bool TryGetPattern(uint scalar, out GlyphPattern pattern, out bool fallback)
    {
        fallback = false;
        if (scalar >= 'a' && scalar <= 'z') scalar -= 'a' - 'A';
        switch (scalar)
        {
            case ' ': pattern = new(0, 0, 0, 0, 0, 0, 0); return true;
            case 'A': pattern = new(0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11); return true;
            case 'B': pattern = new(0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E); return true;
            case 'C': pattern = new(0x0F, 0x10, 0x10, 0x10, 0x10, 0x10, 0x0F); return true;
            case 'D': pattern = new(0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E); return true;
            case 'E': pattern = new(0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F); return true;
            case 'F': pattern = new(0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10); return true;
            case 'G': pattern = new(0x0F, 0x10, 0x10, 0x17, 0x11, 0x11, 0x0F); return true;
            case 'H': pattern = new(0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11); return true;
            case 'I': pattern = new(0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x1F); return true;
            case 'J': pattern = new(0x01, 0x01, 0x01, 0x01, 0x11, 0x11, 0x0E); return true;
            case 'K': pattern = new(0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11); return true;
            case 'L': pattern = new(0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F); return true;
            case 'M': pattern = new(0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11); return true;
            case 'N': pattern = new(0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11); return true;
            case 'O': pattern = new(0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E); return true;
            case 'P': pattern = new(0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10); return true;
            case 'Q': pattern = new(0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D); return true;
            case 'R': pattern = new(0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11); return true;
            case 'S': pattern = new(0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E); return true;
            case 'T': pattern = new(0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04); return true;
            case 'U': pattern = new(0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E); return true;
            case 'V': pattern = new(0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04); return true;
            case 'W': pattern = new(0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A); return true;
            case 'X': pattern = new(0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11); return true;
            case 'Y': pattern = new(0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04); return true;
            case 'Z': pattern = new(0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F); return true;
            case '0': pattern = new(0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E); return true;
            case '1': pattern = new(0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E); return true;
            case '2': pattern = new(0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F); return true;
            case '3': pattern = new(0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E); return true;
            case '4': pattern = new(0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02); return true;
            case '5': pattern = new(0x1F, 0x10, 0x10, 0x1E, 0x01, 0x01, 0x1E); return true;
            case '6': pattern = new(0x0E, 0x10, 0x10, 0x1E, 0x11, 0x11, 0x0E); return true;
            case '7': pattern = new(0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08); return true;
            case '8': pattern = new(0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E); return true;
            case '9': pattern = new(0x0E, 0x11, 0x11, 0x0F, 0x01, 0x01, 0x0E); return true;
            case '.': pattern = new(0, 0, 0, 0, 0, 0x0C, 0x0C); return true;
            case ',': pattern = new(0, 0, 0, 0, 0, 0x0C, 0x08); return true;
            case '!': pattern = new(0x04, 0x04, 0x04, 0x04, 0x04, 0, 0x04); return true;
            case '?': pattern = new(0x0E, 0x11, 0x01, 0x02, 0x04, 0, 0x04); return true;
            case ':': pattern = new(0, 0x0C, 0x0C, 0, 0x0C, 0x0C, 0); return true;
            case ';': pattern = new(0, 0x0C, 0x0C, 0, 0x0C, 0x08, 0); return true;
            case '-': pattern = new(0, 0, 0, 0x1F, 0, 0, 0); return true;
            case '_': pattern = new(0, 0, 0, 0, 0, 0, 0x1F); return true;
            case '+': pattern = new(0, 0x04, 0x04, 0x1F, 0x04, 0x04, 0); return true;
            case '/': pattern = new(0x01, 0x02, 0x02, 0x04, 0x08, 0x08, 0x10); return true;
            case '\\': pattern = new(0x10, 0x08, 0x08, 0x04, 0x02, 0x02, 0x01); return true;
            case '#': pattern = new(0x0A, 0x1F, 0x0A, 0x0A, 0x1F, 0x0A, 0); return true;
            case '(': pattern = new(0x02, 0x04, 0x08, 0x08, 0x08, 0x04, 0x02); return true;
            case ')': pattern = new(0x08, 0x04, 0x02, 0x02, 0x02, 0x04, 0x08); return true;
            case '[': pattern = new(0x0E, 0x08, 0x08, 0x08, 0x08, 0x08, 0x0E); return true;
            case ']': pattern = new(0x0E, 0x02, 0x02, 0x02, 0x02, 0x02, 0x0E); return true;
            case '=': pattern = new(0, 0x1F, 0, 0x1F, 0, 0, 0); return true;
            case '"': pattern = new(0x0A, 0x0A, 0x0A, 0, 0, 0, 0); return true;
            case '\'': pattern = new(0x04, 0x04, 0x04, 0, 0, 0, 0); return true;
            default:
                pattern = new(0x0E, 0x11, 0x01, 0x02, 0x04, 0, 0x04);
                fallback = true;
                return true;
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedRasterTelemetry
{
    internal ManagedRasterTelemetry(ManagedSoftwareRasterizer rasterizer)
    {
        FramebufferWidth = rasterizer.FramebufferWidth;
        FramebufferHeight = rasterizer.FramebufferHeight;
        Stride = rasterizer.Stride;
        CommandsProcessed = rasterizer.CommandsProcessed;
        FillCommands = rasterizer.FillCommands;
        BorderCommands = rasterizer.BorderCommands;
        TextCommands = rasterizer.TextCommands;
        ImagePlaceholderCommands = rasterizer.ImagePlaceholderCommands;
        ClipPushes = rasterizer.ClipPushes;
        ClipPops = rasterizer.ClipPops;
        PeakClipDepth = rasterizer.PeakClipDepth;
        GlyphRequests = rasterizer.GlyphRequests;
        GlyphsRendered = rasterizer.GlyphsRendered;
        FallbackGlyphs = rasterizer.FallbackGlyphs;
        GlyphPixelsConsidered = rasterizer.GlyphPixelsConsidered;
        GlyphPixelsWritten = rasterizer.GlyphPixelsWritten;
        FillPixelsWritten = rasterizer.FillPixelsWritten;
        BorderPixelsWritten = rasterizer.BorderPixelsWritten;
        ImagePixelsWritten = rasterizer.ImagePixelsWritten;
        ClearPixelsWritten = rasterizer.ClearPixelsWritten;
        TotalPixelsWritten = rasterizer.TotalPixelsWritten;
        BlendedPixels = rasterizer.BlendedPixels;
        TransparentSkips = rasterizer.TransparentSkips;
        FullyOffscreenPrimitives = rasterizer.FullyOffscreenPrimitives;
        CancellationCheckpoints = rasterizer.CancellationCheckpoints;
        DirtyBounds = rasterizer.DirtyBounds;
        State = rasterizer.State;
        FailureReason = rasterizer.FailureReason;
        HashValid = rasterizer.HashValid;
    }

    public int FramebufferWidth { get; }
    public int FramebufferHeight { get; }
    public int Stride { get; }
    public int CommandsProcessed { get; }
    public int FillCommands { get; }
    public int BorderCommands { get; }
    public int TextCommands { get; }
    public int ImagePlaceholderCommands { get; }
    public int ClipPushes { get; }
    public int ClipPops { get; }
    public int PeakClipDepth { get; }
    public int GlyphRequests { get; }
    public int GlyphsRendered { get; }
    public int FallbackGlyphs { get; }
    public long GlyphPixelsConsidered { get; }
    public long GlyphPixelsWritten { get; }
    public long FillPixelsWritten { get; }
    public long BorderPixelsWritten { get; }
    public long ImagePixelsWritten { get; }
    public long ClearPixelsWritten { get; }
    public long TotalPixelsWritten { get; }
    public long BlendedPixels { get; }
    public long TransparentSkips { get; }
    public int FullyOffscreenPrimitives { get; }
    public int CancellationCheckpoints { get; }
    public ManagedRasterDirtyBounds DirtyBounds { get; }
    public ManagedRasterState State { get; }
    public ManagedRasterFailureReason FailureReason { get; }
    public bool HashValid { get; }
}

/// <summary>
/// Deterministic bounded software rasterizer for the Phase 46 command stream.
/// It owns only a fixed clip stack, hash state, telemetry, and scalar fields.
/// The caller owns the framebuffer storage.
/// </summary>
public sealed class ManagedSoftwareRasterizer
{
    private const int MaximumGlyphDimension = 128;
    private readonly ManagedLayoutRect[] _clipStack;
    private readonly ManagedSha256 _hash = new();
    private readonly byte[] _framebufferHash = new byte[ManagedSha256.DigestSize];
    private uint[]? _storage;
    private int _baseOffset;
    private int _clipDepth;
    private int _peakClipDepth;
    private int _framebufferWidth;
    private int _framebufferHeight;
    private int _stride;
    private int _commandsProcessed;
    private int _fillCommands;
    private int _borderCommands;
    private int _textCommands;
    private int _imagePlaceholderCommands;
    private int _clipPushes;
    private int _clipPops;
    private int _glyphRequests;
    private int _glyphsRendered;
    private int _fallbackGlyphs;
    private long _glyphPixelsConsidered;
    private long _glyphPixelsWritten;
    private long _fillPixelsWritten;
    private long _borderPixelsWritten;
    private long _imagePixelsWritten;
    private long _clearPixelsWritten;
    private long _totalPixelsWritten;
    private long _blendedPixels;
    private long _transparentSkips;
    private int _fullyOffscreenPrimitives;
    private int _cancellationCheckpoints;
    private int _currentCommandIndex;
    private int _cancelAfterCommands = -1;
    private int _cancelAfterGlyphs = -1;
    private bool _cancelRequested;
    private bool _hashValid;
    private bool _dirty;
    private int _dirtyMinX;
    private int _dirtyMinY;
    private int _dirtyMaxX;
    private int _dirtyMaxY;
    private ManagedRasterState _state;
    private ManagedRasterFailureReason _failureReason;

    public ManagedSoftwareRasterizer() : this(ManagedRasterizerOptions.Default) { }

    public ManagedSoftwareRasterizer(ManagedRasterizerOptions options)
    {
        _clipStack = new ManagedLayoutRect[options.ClipStackCapacity];
        Reset();
    }

    public int ClipStackCapacity => _clipStack.Length;
    public int FramebufferWidth => _framebufferWidth;
    public int FramebufferHeight => _framebufferHeight;
    public int Stride => _stride;
    public int CommandsProcessed => _commandsProcessed;
    public int FillCommands => _fillCommands;
    public int BorderCommands => _borderCommands;
    public int TextCommands => _textCommands;
    public int ImagePlaceholderCommands => _imagePlaceholderCommands;
    public int ClipPushes => _clipPushes;
    public int ClipPops => _clipPops;
    public int PeakClipDepth => _peakClipDepth;
    public int CurrentClipDepth => _clipDepth;
    public int GlyphRequests => _glyphRequests;
    public int GlyphsRendered => _glyphsRendered;
    public int FallbackGlyphs => _fallbackGlyphs;
    public long GlyphPixelsConsidered => _glyphPixelsConsidered;
    public long GlyphPixelsWritten => _glyphPixelsWritten;
    public long FillPixelsWritten => _fillPixelsWritten;
    public long BorderPixelsWritten => _borderPixelsWritten;
    public long ImagePixelsWritten => _imagePixelsWritten;
    public long ClearPixelsWritten => _clearPixelsWritten;
    public long TotalPixelsWritten => _totalPixelsWritten;
    public long BlendedPixels => _blendedPixels;
    public long TransparentSkips => _transparentSkips;
    public int FullyOffscreenPrimitives => _fullyOffscreenPrimitives;
    public int CancellationCheckpoints => _cancellationCheckpoints;
    public ManagedRasterState State => _state;
    public ManagedRasterFailureReason FailureReason => _failureReason;
    public bool HashValid => _hashValid;
    public ManagedRasterDirtyBounds DirtyBounds => new(!_dirty, _dirtyMinX, _dirtyMinY,
                                                       _dirtyMaxX, _dirtyMaxY);
    public ManagedRasterTelemetry Telemetry => new(this);

    public void Cancel() => _cancelRequested = true;

    public void CancelAfterCommands(int commandCount)
    {
        _cancelAfterCommands = commandCount < 0 ? -1 : commandCount;
    }

    public void CancelAfterGlyphs(int glyphCount)
    {
        _cancelAfterGlyphs = glyphCount < 0 ? -1 : glyphCount;
    }

    public void Reset()
    {
        _clipStack.AsSpan().Clear();
        _storage = null;
        _baseOffset = 0;
        _clipDepth = 0;
        _peakClipDepth = 0;
        _framebufferWidth = 0;
        _framebufferHeight = 0;
        _stride = 0;
        _commandsProcessed = 0;
        _fillCommands = 0;
        _borderCommands = 0;
        _textCommands = 0;
        _imagePlaceholderCommands = 0;
        _clipPushes = 0;
        _clipPops = 0;
        _glyphRequests = 0;
        _glyphsRendered = 0;
        _fallbackGlyphs = 0;
        _glyphPixelsConsidered = 0;
        _glyphPixelsWritten = 0;
        _fillPixelsWritten = 0;
        _borderPixelsWritten = 0;
        _imagePixelsWritten = 0;
        _clearPixelsWritten = 0;
        _totalPixelsWritten = 0;
        _blendedPixels = 0;
        _transparentSkips = 0;
        _fullyOffscreenPrimitives = 0;
        _cancellationCheckpoints = 0;
        _currentCommandIndex = -1;
        _cancelAfterCommands = -1;
        _cancelAfterGlyphs = -1;
        _cancelRequested = false;
        _hashValid = false;
        _framebufferHash.AsSpan().Clear();
        _hash.Reset();
        _dirty = false;
        _dirtyMinX = 0;
        _dirtyMinY = 0;
        _dirtyMaxX = 0;
        _dirtyMaxY = 0;
        _state = ManagedRasterState.Reset;
        _failureReason = ManagedRasterFailureReason.None;
    }

    public bool TryCopyFramebufferHash(Span<byte> destination)
    {
        if (!_hashValid || destination.Length < _framebufferHash.Length) return false;
        _framebufferHash.AsSpan().CopyTo(destination);
        return true;
    }

    public bool TryRender(ManagedPaintEngine paint, in ManagedFramebuffer framebuffer,
                          IManagedRasterGlyphSource? glyphSource = null,
                          ManagedRasterRenderOptions options = default)
    {
        if (paint == null)
        {
            PrepareAttempt();
            return Fail(ManagedRasterFailureReason.InvalidDisplayList);
        }
        return TryRenderCore(ReadOnlySpan<ManagedPaintCommand>.Empty, paint,
                             paint.Document, paint.Layout, framebuffer,
                             glyphSource ?? ManagedProofGlyphSource.Instance, options);
    }

    public bool TryRender(ReadOnlySpan<ManagedPaintCommand> commands,
                          ManagedHtmlDocument document, ManagedLayoutEngine layout,
                          in ManagedFramebuffer framebuffer,
                          IManagedRasterGlyphSource? glyphSource = null,
                          ManagedRasterRenderOptions options = default)
    {
        return TryRenderCore(commands, null, document, layout, framebuffer,
                             glyphSource ?? ManagedProofGlyphSource.Instance, options);
    }

    private bool TryRenderCore(ReadOnlySpan<ManagedPaintCommand> commands,
                               ManagedPaintEngine? paint,
                               ManagedHtmlDocument? document,
                               ManagedLayoutEngine? layout,
                               in ManagedFramebuffer framebuffer,
                               IManagedRasterGlyphSource glyphSource,
                               ManagedRasterRenderOptions options)
    {
        bool cancellationRequested = _cancelRequested;
        int cancelAfterCommands = _cancelAfterCommands;
        int cancelAfterGlyphs = _cancelAfterGlyphs;
        PrepareAttempt();
        _cancelRequested = cancellationRequested;
        _cancelAfterCommands = cancelAfterCommands;
        _cancelAfterGlyphs = cancelAfterGlyphs;
        if (!options.IsSpecified) options = ManagedRasterRenderOptions.ClearBlack;

        if (!ValidateFramebuffer(framebuffer, out ManagedRasterFailureReason framebufferFailure))
            return Fail(framebufferFailure);
        if (document == null || layout == null || !layout.IsLaidOut ||
            !document.IsValid(document.DocumentNode) ||
            !document.Validate(out ManagedHtmlDocumentValidationFailureReason documentFailure) ||
            documentFailure != ManagedHtmlDocumentValidationFailureReason.None ||
            !layout.Validate(out ManagedLayoutValidationFailureReason layoutFailure) ||
            layoutFailure != ManagedLayoutValidationFailureReason.None)
            return Fail(ManagedRasterFailureReason.InvalidDisplayList);

        bool valid;
        ManagedPaintValidationFailureReason validationFailure;
        if (paint != null)
        {
            valid = paint.Validate(out validationFailure);
        }
        else
        {
            valid = ManagedPaintValidator.Validate(commands, document, layout,
                                                   out validationFailure);
        }
        if (!valid)
            return Fail(MapValidationFailure(validationFailure));
        if (!ValidateRasterCommands(commands, paint, out ManagedRasterFailureReason rasterFailure))
            return Fail(rasterFailure);
        if (glyphSource == null) return Fail(ManagedRasterFailureReason.GlyphSourceFailure);

        _storage = framebuffer.BackingStorage;
        _baseOffset = framebuffer.Offset;
        _framebufferWidth = framebuffer.Width;
        _framebufferHeight = framebuffer.Height;
        _stride = framebuffer.Stride;
        if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
        if (options.Clear && !ClearFramebuffer(options.ClearColor)) return false;

        int commandCount = paint?.CommandsEmitted ?? commands.Length;
        for (int index = 0; index != commandCount; ++index)
        {
            _currentCommandIndex = index;
            if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
            if (!TryReadCommand(commands, paint, index, out ManagedPaintCommand command))
                return Fail(ManagedRasterFailureReason.InvalidDisplayList);
            if (!ExecuteCommand(command, document, glyphSource)) return false;
            ++_commandsProcessed;
        }
        if (_clipDepth != 0) return Fail(ManagedRasterFailureReason.InvalidDisplayList);
        if (!ComputeFramebufferHash()) return Fail(ManagedRasterFailureReason.InvalidState);
        _state = ManagedRasterState.Complete;
        _failureReason = ManagedRasterFailureReason.None;
        return true;
    }

    private void PrepareAttempt()
    {
        _clipStack.AsSpan().Clear();
        _storage = null;
        _baseOffset = 0;
        _clipDepth = 0;
        _peakClipDepth = 0;
        _framebufferWidth = 0;
        _framebufferHeight = 0;
        _stride = 0;
        _commandsProcessed = 0;
        _fillCommands = 0;
        _borderCommands = 0;
        _textCommands = 0;
        _imagePlaceholderCommands = 0;
        _clipPushes = 0;
        _clipPops = 0;
        _glyphRequests = 0;
        _glyphsRendered = 0;
        _fallbackGlyphs = 0;
        _glyphPixelsConsidered = 0;
        _glyphPixelsWritten = 0;
        _fillPixelsWritten = 0;
        _borderPixelsWritten = 0;
        _imagePixelsWritten = 0;
        _clearPixelsWritten = 0;
        _totalPixelsWritten = 0;
        _blendedPixels = 0;
        _transparentSkips = 0;
        _fullyOffscreenPrimitives = 0;
        _cancellationCheckpoints = 0;
        _currentCommandIndex = -1;
        _hashValid = false;
        _framebufferHash.AsSpan().Clear();
        _hash.Reset();
        _dirty = false;
        _dirtyMinX = 0;
        _dirtyMinY = 0;
        _dirtyMaxX = 0;
        _dirtyMaxY = 0;
        _state = ManagedRasterState.Reset;
        _failureReason = ManagedRasterFailureReason.None;
    }

    private static ManagedRasterFailureReason MapValidationFailure(
        ManagedPaintValidationFailureReason reason) => reason switch
        {
            ManagedPaintValidationFailureReason.InvalidTextReference =>
                ManagedRasterFailureReason.InvalidTextReference,
            _ => ManagedRasterFailureReason.InvalidDisplayList
        };

    private bool ValidateRasterCommands(ReadOnlySpan<ManagedPaintCommand> commands,
                                        ManagedPaintEngine? paint,
                                        out ManagedRasterFailureReason failure)
    {
        failure = ManagedRasterFailureReason.None;
        int count = paint?.CommandsEmitted ?? commands.Length;
        int depth = 0;
        for (int index = 0; index != count; ++index)
        {
            if (!TryReadCommand(commands, paint, index, out ManagedPaintCommand command))
            {
                failure = ManagedRasterFailureReason.InvalidDisplayList;
                return false;
            }
            switch (command.Kind)
            {
                case ManagedPaintCommandKind.BeginClip:
                    if (++depth > _clipStack.Length)
                    {
                        failure = ManagedRasterFailureReason.RasterClipDepthExceeded;
                        return false;
                    }
                    break;
                case ManagedPaintCommandKind.EndClip:
                    if (depth == 0)
                    {
                        failure = ManagedRasterFailureReason.InvalidDisplayList;
                        return false;
                    }
                    --depth;
                    break;
                case ManagedPaintCommandKind.FillRectangle:
                case ManagedPaintCommandKind.BorderRectangle:
                case ManagedPaintCommandKind.TextRun:
                case ManagedPaintCommandKind.ImagePlaceholder:
                    if (command.ClipDepth != depth)
                    {
                        failure = ManagedRasterFailureReason.InvalidDisplayList;
                        return false;
                    }
                    if (command.Kind == ManagedPaintCommandKind.BorderRectangle &&
                        command.BorderStyle != ManagedCssBorderStyle.Solid)
                    {
                        failure = ManagedRasterFailureReason.InvalidPaintCommand;
                        return false;
                    }
                    break;
                default:
                    failure = ManagedRasterFailureReason.InvalidPaintCommand;
                    return false;
            }
        }
        if (depth != 0)
        {
            failure = ManagedRasterFailureReason.InvalidDisplayList;
            return false;
        }
        return true;
    }

    private static bool ValidateFramebuffer(in ManagedFramebuffer framebuffer,
                                            out ManagedRasterFailureReason failure)
    {
        failure = ManagedRasterFailureReason.None;
        if (framebuffer.PixelFormat != ManagedRasterPixelFormat.Argb8888)
        {
            failure = ManagedRasterFailureReason.UnsupportedPixelFormat;
            return false;
        }
        if (framebuffer.BackingStorage == null || framebuffer.Width <= 0 ||
            framebuffer.Height <= 0 || framebuffer.Stride <= 0 || framebuffer.Offset < 0)
        {
            failure = ManagedRasterFailureReason.InvalidFramebuffer;
            return false;
        }
        long rowPixels = framebuffer.Width;
        if (rowPixels > int.MaxValue / sizeof(uint) || framebuffer.Stride < framebuffer.Width)
        {
            failure = framebuffer.Stride < framebuffer.Width
                ? ManagedRasterFailureReason.InvalidFramebuffer
                : ManagedRasterFailureReason.FramebufferGeometryOverflow;
            return false;
        }
        long requiredWords = (long)framebuffer.Stride * framebuffer.Height;
        long activePixels = rowPixels * framebuffer.Height;
        bool activeByteOverflow = activePixels > long.MaxValue / sizeof(uint);
        long activeBytes = activeByteOverflow ? long.MaxValue : activePixels * sizeof(uint);
        if (requiredWords <= 0 || requiredWords > int.MaxValue || activeBytes <= 0 ||
            activeByteOverflow)
        {
            failure = ManagedRasterFailureReason.FramebufferGeometryOverflow;
            return false;
        }
        long end = (long)framebuffer.Offset + requiredWords;
        if (end < 0 || end > framebuffer.BackingStorage.Length)
        {
            failure = ManagedRasterFailureReason.FramebufferTooSmall;
            return false;
        }
        return true;
    }

    private static bool TryReadCommand(ReadOnlySpan<ManagedPaintCommand> commands,
                                       ManagedPaintEngine? paint, int index,
                                       out ManagedPaintCommand command)
    {
        if (paint != null) return paint.TryGetCommand(index, out command);
        if (index < 0 || index >= commands.Length)
        {
            command = default;
            return false;
        }
        command = commands[index];
        return true;
    }

    private bool ClearFramebuffer(uint color)
    {
        if (_storage == null) return Fail(ManagedRasterFailureReason.InvalidState);
        for (int y = 0; y != _framebufferHeight; ++y)
        {
            if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
            int row = checked(_baseOffset + checked(y * _stride));
            for (int x = 0; x != _framebufferWidth; ++x)
            {
                _storage[row + x] = color;
                ++_clearPixelsWritten;
                ++_totalPixelsWritten;
                MarkWritten(x, y);
            }
        }
        return true;
    }

    private bool ExecuteCommand(ManagedPaintCommand command, ManagedHtmlDocument document,
                                IManagedRasterGlyphSource glyphSource)
    {
        switch (command.Kind)
        {
            case ManagedPaintCommandKind.BeginClip:
                _clipStack[_clipDepth] = Intersect(CurrentClip(), command.Rect);
                ++_clipDepth;
                ++_clipPushes;
                if (_clipDepth > _peakClipDepth) _peakClipDepth = _clipDepth;
                return true;
            case ManagedPaintCommandKind.EndClip:
                if (_clipDepth == 0) return Fail(ManagedRasterFailureReason.InvalidDisplayList);
                --_clipDepth;
                ++_clipPops;
                return true;
            case ManagedPaintCommandKind.FillRectangle:
                ++_fillCommands;
                return Fill(command.Rect, command.ClipRect, command.Color);
            case ManagedPaintCommandKind.BorderRectangle:
                ++_borderCommands;
                return Border(command.Rect, command.ClipRect, command.BorderWidths, command.Color);
            case ManagedPaintCommandKind.TextRun:
                ++_textCommands;
                return Text(command, document, glyphSource);
            case ManagedPaintCommandKind.ImagePlaceholder:
                ++_imagePlaceholderCommands;
                return Image(command.Rect, command.ClipRect);
            default:
                return Fail(ManagedRasterFailureReason.InvalidPaintCommand);
        }
    }

    private ManagedLayoutRect CurrentClip() => _clipDepth == 0
        ? new ManagedLayoutRect(0, 0, _framebufferWidth, _framebufferHeight)
        : _clipStack[_clipDepth - 1];

    private bool Fill(ManagedLayoutRect rect, ManagedLayoutRect commandClip, uint color)
    {
        if (!TryGetBounds(rect, commandClip, out int left, out int top,
                          out int right, out int bottom))
        {
            ++_fullyOffscreenPrimitives;
            return true;
        }
        for (int y = top; y != bottom; ++y)
        {
            if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
            for (int x = left; x != right; ++x)
            {
                if (!BlendPixel(x, y, color, PixelKind.Fill)) return false;
            }
        }
        return true;
    }

    private bool Border(ManagedLayoutRect rect, ManagedLayoutRect commandClip,
                        ManagedLayoutEdges widths, uint color)
    {
        if (widths.Top == 0 && widths.Right == 0 && widths.Bottom == 0 && widths.Left == 0)
            return true;
        if (!TryGetBounds(rect, commandClip, out int left, out int top,
                          out int right, out int bottom))
        {
            ++_fullyOffscreenPrimitives;
            return true;
        }
        long outerRight = (long)rect.X + rect.Width;
        long outerBottom = (long)rect.Y + rect.Height;
        long topEnd = (long)rect.Y + widths.Top;
        long bottomStart = outerBottom - widths.Bottom;
        long leftEnd = (long)rect.X + widths.Left;
        long rightStart = outerRight - widths.Right;
        for (int y = top; y != bottom; ++y)
        {
            if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
            for (int x = left; x != right; ++x)
            {
                bool isBorder = (long)y < topEnd || (long)y >= bottomStart ||
                                (long)x < leftEnd || (long)x >= rightStart;
                if (isBorder && !BlendPixel(x, y, color, PixelKind.Border)) return false;
            }
        }
        return true;
    }

    private bool Image(ManagedLayoutRect rect, ManagedLayoutRect commandClip)
    {
        if (!TryGetBounds(rect, commandClip, out int left, out int top,
                          out int right, out int bottom))
        {
            ++_fullyOffscreenPrimitives;
            return true;
        }
        long outerRight = (long)rect.X + rect.Width;
        long outerBottom = (long)rect.Y + rect.Height;
        for (int y = top; y != bottom; ++y)
        {
            if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
            for (int x = left; x != right; ++x)
            {
                int localX = (int)((long)x - rect.X);
                int localY = (int)((long)y - rect.Y);
                bool edge = localX == 0 || localY == 0 ||
                            (long)x == outerRight - 1 || (long)y == outerBottom - 1;
                bool diagonal = localX == localY ||
                                (long)localX + localY == Math.Min(rect.Width, rect.Height) - 1;
                uint color = edge ? 0xFF202020U :
                    diagonal ? 0xFFFFFFFFU :
                    (((localX / 4 + localY / 4) & 1) == 0 ? 0xFFB0B0B0U : 0xFF707070U);
                if (!BlendPixel(x, y, color, PixelKind.Image)) return false;
            }
        }
        return true;
    }

    private bool Text(ManagedPaintCommand command, ManagedHtmlDocument document,
                      IManagedRasterGlyphSource glyphSource)
    {
        ManagedHtmlNodeHandle node = new(command.SourceNodeIndex, document.DocumentNode.Generation);
        if (document.GetNodeKind(node) != ManagedHtmlNodeKind.Text ||
            command.SourceOffset < 0 || command.SourceLength < 0 ||
            command.SourceOffset > document.GetTextLength(node) - command.SourceLength)
            return Fail(ManagedRasterFailureReason.InvalidTextReference);
        long cursorX = command.Rect.X;
        for (int index = 0; index != command.SourceLength; ++index)
        {
            if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
            if (!document.TryGetTextScalar(node, command.SourceOffset + index, out uint scalar))
                return Fail(ManagedRasterFailureReason.InvalidTextReference);
            ++_glyphRequests;
            if (!glyphSource.TryGetGlyph(scalar, command.FontId, command.FontSize,
                                         command.FontWeight, command.FontStyle,
                                         out ManagedRasterGlyph glyph))
                return Fail(ManagedRasterFailureReason.GlyphSourceFailure);
            if (glyph.Width < 0 || glyph.Width > MaximumGlyphDimension || glyph.Height < 0 ||
                glyph.Height > MaximumGlyphDimension || glyph.Advance <= 0 ||
                glyph.Advance > ManagedLayoutLimits.MaximumCoordinate)
                return Fail(ManagedRasterFailureReason.GlyphSourceFailure);
            ++_glyphsRendered;
            if (glyph.IsFallback) ++_fallbackGlyphs;
            if (_cancelAfterGlyphs >= 0 && _glyphsRendered >= _cancelAfterGlyphs)
                _cancelRequested = true;
            for (int row = 0; row != glyph.Height; ++row)
            {
                if (ShouldCancel()) return Fail(ManagedRasterFailureReason.Cancelled);
                uint mask = glyph.GetRowMask(row);
                for (int column = 0; column != glyph.Width; ++column)
                {
                    ++_glyphPixelsConsidered;
                    int bit = 24 - column;
                    if (bit < 0 || (mask & (1U << bit)) == 0) continue;
                    long x = cursorX + column;
                    long y = (long)command.Rect.Y + row;
                    if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                        continue;
                    if (!BlendPixel((int)x, (int)y, command.Color, PixelKind.Glyph)) return false;
                }
            }
            cursorX += glyph.Advance;
            if (cursorX < int.MinValue || cursorX > (long)ManagedLayoutLimits.MaximumCoordinate * 2)
                return Fail(ManagedRasterFailureReason.InvalidPaintCommand);
        }
        return true;
    }

    private bool TryGetBounds(ManagedLayoutRect rect, ManagedLayoutRect commandClip,
                              out int left, out int top, out int right, out int bottom)
    {
        ManagedLayoutRect clip = Intersect(CurrentClip(), commandClip);
        long rectLeft = rect.X;
        long rectTop = rect.Y;
        long rectRight = rectLeft + rect.Width;
        long rectBottom = rectTop + rect.Height;
        long clipLeft = clip.X;
        long clipTop = clip.Y;
        long clipRight = clipLeft + clip.Width;
        long clipBottom = clipTop + clip.Height;
        long x0 = Math.Max(0L, Math.Max(rectLeft, clipLeft));
        long y0 = Math.Max(0L, Math.Max(rectTop, clipTop));
        long x1 = Math.Min(_framebufferWidth, Math.Min(rectRight, clipRight));
        long y1 = Math.Min(_framebufferHeight, Math.Min(rectBottom, clipBottom));
        if (x1 <= x0 || y1 <= y0)
        {
            left = top = right = bottom = 0;
            return false;
        }
        left = (int)x0;
        top = (int)y0;
        right = (int)x1;
        bottom = (int)y1;
        return true;
    }

    private bool BlendPixel(int x, int y, uint source, PixelKind kind)
    {
        if (_storage == null || x < 0 || y < 0 || x >= _framebufferWidth ||
            y >= _framebufferHeight) return true;
        ManagedLayoutRect clip = CurrentClip();
        if (!Contains(clip, x, y)) return true;
        int offset = checked(_baseOffset + checked(y * _stride + x));
        uint destination = _storage[offset];
        byte sourceAlpha = (byte)(source >> 24);
        if (sourceAlpha == 0)
        {
            ++_transparentSkips;
            return true;
        }
        uint result = SourceOver(source, destination);
        _storage[offset] = result;
        ++_totalPixelsWritten;
        if (sourceAlpha != 255) ++_blendedPixels;
        switch (kind)
        {
            case PixelKind.Fill: ++_fillPixelsWritten; break;
            case PixelKind.Border: ++_borderPixelsWritten; break;
            case PixelKind.Glyph: ++_glyphPixelsWritten; break;
            case PixelKind.Image: ++_imagePixelsWritten; break;
        }
        MarkWritten(x, y);
        return true;
    }

    private static uint SourceOver(uint source, uint destination)
    {
        int sourceAlpha = (int)(source >> 24);
        int destinationAlpha = (int)(destination >> 24);
        int inverse = 255 - sourceAlpha;
        int outputAlpha = sourceAlpha + RoundDiv((long)destinationAlpha * inverse, 255);
        if (outputAlpha == 0) return 0;
        uint result = (uint)outputAlpha << 24;
        result |= (uint)BlendChannel((byte)(source >> 16), (byte)(destination >> 16),
                                     sourceAlpha, destinationAlpha, inverse, outputAlpha) << 16;
        result |= (uint)BlendChannel((byte)(source >> 8), (byte)(destination >> 8),
                                     sourceAlpha, destinationAlpha, inverse, outputAlpha) << 8;
        result |= (uint)BlendChannel((byte)source, (byte)destination,
                                     sourceAlpha, destinationAlpha, inverse, outputAlpha);
        return result;
    }

    private static int BlendChannel(byte source, byte destination, int sourceAlpha,
                                    int destinationAlpha, int inverse, int outputAlpha)
    {
        long numerator = (long)source * sourceAlpha * 255L +
                         (long)destination * destinationAlpha * inverse;
        long denominator = (long)outputAlpha * 255L;
        return RoundDiv(numerator, denominator);
    }

    private static int RoundDiv(long numerator, long denominator) =>
        denominator <= 0 ? 0 : (int)((numerator + denominator / 2) / denominator);

    private static bool Contains(ManagedLayoutRect rect, int x, int y) =>
        rect.Width > 0 && rect.Height > 0 && x >= rect.X && y >= rect.Y &&
        (long)x < (long)rect.X + rect.Width &&
        (long)y < (long)rect.Y + rect.Height;

    private static ManagedLayoutRect Intersect(ManagedLayoutRect left, ManagedLayoutRect right)
    {
        long x = Math.Max(left.X, right.X);
        long y = Math.Max(left.Y, right.Y);
        long r = Math.Min((long)left.X + left.Width, (long)right.X + right.Width);
        long b = Math.Min((long)left.Y + left.Height, (long)right.Y + right.Height);
        return r <= x || b <= y ? new ManagedLayoutRect((int)x, (int)y, 0, 0) :
            new ManagedLayoutRect((int)x, (int)y, (int)(r - x), (int)(b - y));
    }

    private bool ComputeFramebufferHash()
    {
        if (_storage == null) return false;
        _hash.Reset();
        if (!_hash.Append("GXOS-P47-FB-ARGB8888\0"u8)) return false;
        Span<byte> bytes = stackalloc byte[4];
        for (int y = 0; y != _framebufferHeight; ++y)
        {
            int row = checked(_baseOffset + checked(y * _stride));
            for (int x = 0; x != _framebufferWidth; ++x)
            {
                uint pixel = _storage[row + x];
                bytes[0] = (byte)(pixel >> 24);
                bytes[1] = (byte)(pixel >> 16);
                bytes[2] = (byte)(pixel >> 8);
                bytes[3] = (byte)pixel;
                if (!_hash.Append(bytes)) return false;
            }
        }
        return _hash.TryFinalize(_framebufferHash) && (_hashValid = true);
    }

    private void MarkWritten(int x, int y)
    {
        if (!_dirty)
        {
            _dirty = true;
            _dirtyMinX = _dirtyMaxX = x;
            _dirtyMinY = _dirtyMaxY = y;
            return;
        }
        _dirtyMinX = Math.Min(_dirtyMinX, x);
        _dirtyMinY = Math.Min(_dirtyMinY, y);
        _dirtyMaxX = Math.Max(_dirtyMaxX, x);
        _dirtyMaxY = Math.Max(_dirtyMaxY, y);
    }

    private bool ShouldCancel()
    {
        ++_cancellationCheckpoints;
        if (_cancelRequested) return true;
        return _cancelAfterCommands >= 0 && _commandsProcessed >= _cancelAfterCommands;
    }

    private bool Fail(ManagedRasterFailureReason reason)
    {
        _failureReason = reason;
        _state = reason == ManagedRasterFailureReason.Cancelled
            ? ManagedRasterState.Cancelled : ManagedRasterState.Failed;
        _hashValid = false;
        return false;
    }

    private enum PixelKind : byte
    {
        Fill,
        Border,
        Glyph,
        Image
    }
}

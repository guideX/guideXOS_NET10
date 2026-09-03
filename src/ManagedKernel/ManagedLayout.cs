using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedLayoutFailureReason : byte
{
    None = 0,
    InvalidDocument = 1,
    InvalidStyles = 2,
    InvalidViewport = 3,
    LayoutBoxCapacityExceeded = 4,
    LineCapacityExceeded = 5,
    TextFragmentCapacityExceeded = 6,
    TraversalStackCapacityExceeded = 7,
    TableColumnCapacityExceeded = 8,
    GeometryOverflow = 9,
    UnsupportedLayoutValue = 10,
    TextMeasurementFailure = 11,
    InvalidState = 12,
    Cancelled = 13
}

[Flags]
public enum ManagedLayoutBoxFlags : byte
{
    None = 0,
    InFlow = 1,
    Relative = 2,
    Absolute = 4,
    Fixed = 8,
    Replaced = 16,
    HasText = 32,
    HasHorizontalOverflow = 64,
    HasVerticalOverflow = 128
}

public enum ManagedLayoutBoxKind : byte
{
    Root = 0,
    Block = 1,
    InlineContainer = 2,
    Text = 3,
    LineBreak = 4,
    Replaced = 5,
    Table = 6,
    TableRow = 7,
    TableCell = 8
}

[Flags]
public enum ManagedLayoutOverflowFlags : byte
{
    None = 0,
    Horizontal = 1,
    Vertical = 2
}

public enum ManagedLayoutTextFragmentKind : byte
{
    Text = 0,
    Replaced = 1
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedLayoutViewport
{
    public ManagedLayoutViewport(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedLayoutRect : IEquatable<ManagedLayoutRect>
{
    public ManagedLayoutRect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Equals(ManagedLayoutRect other) => X == other.X && Y == other.Y &&
        Width == other.Width && Height == other.Height;
    public override bool Equals(object? obj) => obj is ManagedLayoutRect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(ManagedLayoutRect left, ManagedLayoutRect right) => left.Equals(right);
    public static bool operator !=(ManagedLayoutRect left, ManagedLayoutRect right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedLayoutEdges : IEquatable<ManagedLayoutEdges>
{
    public ManagedLayoutEdges(int top, int right, int bottom, int left)
    {
        Top = top;
        Right = right;
        Bottom = bottom;
        Left = left;
    }

    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }
    public int Left { get; }
    public bool Equals(ManagedLayoutEdges other) => Top == other.Top && Right == other.Right &&
        Bottom == other.Bottom && Left == other.Left;
    public override bool Equals(object? obj) => obj is ManagedLayoutEdges other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Top, Right, Bottom, Left);
    public static bool operator ==(ManagedLayoutEdges left, ManagedLayoutEdges right) => left.Equals(right);
    public static bool operator !=(ManagedLayoutEdges left, ManagedLayoutEdges right) => !left.Equals(right);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedLayoutTextStyle
{
    public ManagedLayoutTextStyle(int fontSize, int fontWeight, ManagedCssFontStyle fontStyle)
    {
        FontSize = fontSize;
        FontWeight = fontWeight;
        FontStyle = fontStyle;
    }

    public int FontSize { get; }
    public int FontWeight { get; }
    public ManagedCssFontStyle FontStyle { get; }
}

public interface IManagedLayoutTextMetrics
{
    bool TryMeasureScalar(uint scalar, in ManagedLayoutTextStyle style, out int advance);
    int GetLineHeight(in ManagedLayoutTextStyle style);
}

/// <summary>
/// Authoritative Phase 45 metrics.  It intentionally does not call an OS font
/// stack: every scalar has a deterministic advance and every run has a
/// deterministic line height.
/// </summary>
public sealed class ManagedDeterministicLayoutTextMetrics : IManagedLayoutTextMetrics
{
    public bool TryMeasureScalar(uint scalar, in ManagedLayoutTextStyle style, out int advance)
    {
        if (style.FontSize <= 0 || style.FontSize > ManagedLayoutLimits.MaximumCoordinate)
        {
            advance = 0;
            return false;
        }
        long value = scalar == 0x20 || scalar == 0x09 ? style.FontSize / 2L :
            (style.FontSize * 3L) / 5L;
        if (scalar > 0xFFFF) value = (style.FontSize * 3L) / 5L;
        if (style.FontWeight >= 700) value += Math.Max(1, style.FontSize / 20);
        if (value <= 0 || value > ManagedLayoutLimits.MaximumCoordinate)
        {
            advance = 0;
            return false;
        }
        advance = (int)value;
        return true;
    }

    public int GetLineHeight(in ManagedLayoutTextStyle style)
    {
        if (style.FontSize <= 0 || style.FontSize > ManagedLayoutLimits.MaximumCoordinate)
            return 0;
        long value = (style.FontSize * 5L) / 4L;
        return value > ManagedLayoutLimits.MaximumCoordinate ? 0 : Math.Max(1, (int)value);
    }
}

public readonly struct ManagedLayoutArenaOptions
{
    public ManagedLayoutArenaOptions(int boxCapacity, int lineCapacity,
                                     int textFragmentCapacity, int traversalStackCapacity,
                                     int tableColumnCapacity = ManagedLayoutLimits.DefaultTableColumnCapacity)
    {
        if (boxCapacity <= 0 || boxCapacity > ManagedLayoutLimits.MaximumBoxCapacity)
            throw new ArgumentOutOfRangeException(nameof(boxCapacity));
        if (lineCapacity <= 0 || lineCapacity > ManagedLayoutLimits.MaximumLineCapacity)
            throw new ArgumentOutOfRangeException(nameof(lineCapacity));
        if (textFragmentCapacity <= 0 || textFragmentCapacity > ManagedLayoutLimits.MaximumTextFragmentCapacity)
            throw new ArgumentOutOfRangeException(nameof(textFragmentCapacity));
        if (traversalStackCapacity <= 0 || traversalStackCapacity > ManagedLayoutLimits.MaximumTraversalStackCapacity)
            throw new ArgumentOutOfRangeException(nameof(traversalStackCapacity));
        if (tableColumnCapacity <= 0 || tableColumnCapacity > ManagedLayoutLimits.MaximumTableColumnCapacity)
            throw new ArgumentOutOfRangeException(nameof(tableColumnCapacity));
        BoxCapacity = boxCapacity;
        LineCapacity = lineCapacity;
        TextFragmentCapacity = textFragmentCapacity;
        TraversalStackCapacity = traversalStackCapacity;
        TableColumnCapacity = tableColumnCapacity;
    }

    public static ManagedLayoutArenaOptions Default => new(
        ManagedLayoutLimits.DefaultBoxCapacity,
        ManagedLayoutLimits.DefaultLineCapacity,
        ManagedLayoutLimits.DefaultTextFragmentCapacity,
        ManagedLayoutLimits.DefaultTraversalStackCapacity,
        ManagedLayoutLimits.DefaultTableColumnCapacity);

    public int BoxCapacity { get; }
    public int LineCapacity { get; }
    public int TextFragmentCapacity { get; }
    public int TraversalStackCapacity { get; }
    public int TableColumnCapacity { get; }
}

public static class ManagedLayoutLimits
{
    public const int DefaultBoxCapacity = 1024;
    public const int DefaultLineCapacity = 2048;
    public const int DefaultTextFragmentCapacity = 4096;
    public const int DefaultTraversalStackCapacity = 128;
    public const int DefaultTableColumnCapacity = 32;
    public const int MaximumBoxCapacity = 4096;
    public const int MaximumLineCapacity = 8192;
    public const int MaximumTextFragmentCapacity = 16384;
    public const int MaximumTraversalStackCapacity = 512;
    public const int MaximumTableColumnCapacity = 256;
    public const int MaximumCoordinate = 1_000_000_000;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedLayoutBox
{
    internal ManagedLayoutBox(int sourceNodeIndex, ManagedLayoutBoxKind kind,
                              int parentIndex, int firstChildIndex, int lastChildIndex,
                              int nextSiblingIndex, ManagedLayoutBoxFlags flags, int zIndex,
                              ManagedLayoutRect borderBox, ManagedLayoutRect paddingBox,
                              ManagedLayoutRect contentRect, ManagedLayoutEdges margin,
                              ManagedLayoutEdges padding, ManagedLayoutEdges border,
                              ManagedLayoutRect overflowExtent, ManagedLayoutRect clipRect)
    {
        SourceNodeIndex = sourceNodeIndex;
        Kind = kind;
        ParentIndex = parentIndex;
        FirstChildIndex = firstChildIndex;
        LastChildIndex = lastChildIndex;
        NextSiblingIndex = nextSiblingIndex;
        Flags = flags;
        ZIndex = zIndex;
        BorderBox = borderBox;
        PaddingBox = paddingBox;
        ContentRect = contentRect;
        Margin = margin;
        Padding = padding;
        Border = border;
        OverflowExtent = overflowExtent;
        ClipRect = clipRect;
    }

    public int SourceNodeIndex { get; }
    public ManagedLayoutBoxKind Kind { get; }
    public int ParentIndex { get; }
    public int FirstChildIndex { get; }
    public int LastChildIndex { get; }
    public int NextSiblingIndex { get; }
    public ManagedLayoutBoxFlags Flags { get; }
    public int ZIndex { get; }
    public ManagedLayoutRect BorderBox { get; }
    public ManagedLayoutRect PaddingBox { get; }
    public ManagedLayoutRect ContentRect { get; }
    public ManagedLayoutEdges Margin { get; }
    public ManagedLayoutEdges Padding { get; }
    public ManagedLayoutEdges Border { get; }
    public ManagedLayoutRect OverflowExtent { get; }
    public ManagedLayoutRect ClipRect { get; }
    public ManagedLayoutOverflowFlags OverflowFlags =>
        (Flags & ManagedLayoutBoxFlags.HasHorizontalOverflow) != 0 ?
            ((Flags & ManagedLayoutBoxFlags.HasVerticalOverflow) != 0 ?
                ManagedLayoutOverflowFlags.Horizontal | ManagedLayoutOverflowFlags.Vertical :
                ManagedLayoutOverflowFlags.Horizontal) :
        (Flags & ManagedLayoutBoxFlags.HasVerticalOverflow) != 0 ?
            ManagedLayoutOverflowFlags.Vertical : ManagedLayoutOverflowFlags.None;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedLayoutLine
{
    internal ManagedLayoutLine(int ownerBoxIndex, int lineIndex, ManagedLayoutRect rectangle,
                               int firstFragmentIndex, int fragmentCount)
    {
        OwnerBoxIndex = ownerBoxIndex;
        LineIndex = lineIndex;
        Rectangle = rectangle;
        FirstFragmentIndex = firstFragmentIndex;
        FragmentCount = fragmentCount;
    }

    public int OwnerBoxIndex { get; }
    public int LineIndex { get; }
    public ManagedLayoutRect Rectangle { get; }
    public int FirstFragmentIndex { get; }
    public int FragmentCount { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedLayoutTextFragment
{
    internal ManagedLayoutTextFragment(ManagedLayoutTextFragmentKind kind, int sourceNodeIndex,
                                       int sourceOffset, int sourceLength, int ownerBoxIndex,
                                       int lineIndex, ManagedLayoutRect rectangle,
                                       ManagedLayoutTextStyle style)
    {
        Kind = kind;
        SourceNodeIndex = sourceNodeIndex;
        SourceOffset = sourceOffset;
        SourceLength = sourceLength;
        OwnerBoxIndex = ownerBoxIndex;
        LineIndex = lineIndex;
        Rectangle = rectangle;
        Style = style;
    }

    public ManagedLayoutTextFragmentKind Kind { get; }
    public int SourceNodeIndex { get; }
    public int SourceOffset { get; }
    public int SourceLength { get; }
    public int OwnerBoxIndex { get; }
    public int LineIndex { get; }
    public ManagedLayoutRect Rectangle { get; }
    public ManagedLayoutTextStyle Style { get; }
}

public readonly struct ManagedLayoutTelemetry
{
    internal ManagedLayoutTelemetry(ManagedLayoutEngine engine)
    {
        LayoutBoxCapacity = engine.LayoutBoxCapacity;
        LineCapacity = engine.LineCapacity;
        TextFragmentCapacity = engine.TextFragmentCapacity;
        TraversalStackCapacity = engine.TraversalStackCapacity;
        LayoutBoxCount = engine.LayoutBoxCount;
        BlockBoxCount = engine.BlockBoxCount;
        InlineTextBoxCount = engine.InlineTextBoxCount;
        LineCount = engine.LineCount;
        TextFragmentCount = engine.TextFragmentCount;
        TextScalarsMeasured = engine.TextScalarsMeasured;
        SoftWrapCount = engine.SoftWrapCount;
        ForcedBreakCount = engine.ForcedBreakCount;
        DisplayNoneSkips = engine.DisplayNoneSkips;
        PositionedBoxCount = engine.PositionedBoxCount;
        HorizontalOverflowCount = engine.HorizontalOverflowCount;
        VerticalOverflowCount = engine.VerticalOverflowCount;
        PeakBoxArena = engine.PeakBoxArena;
        PeakLineArena = engine.PeakLineArena;
        PeakFragmentArena = engine.PeakFragmentArena;
        PeakTraversalDepth = engine.PeakTraversalDepth;
        DocumentContentWidth = engine.DocumentContentWidth;
        DocumentContentHeight = engine.DocumentContentHeight;
    }

    public int LayoutBoxCapacity { get; }
    public int LineCapacity { get; }
    public int TextFragmentCapacity { get; }
    public int TraversalStackCapacity { get; }
    public int LayoutBoxCount { get; }
    public int BlockBoxCount { get; }
    public int InlineTextBoxCount { get; }
    public int LineCount { get; }
    public int TextFragmentCount { get; }
    public int TextScalarsMeasured { get; }
    public int SoftWrapCount { get; }
    public int ForcedBreakCount { get; }
    public int DisplayNoneSkips { get; }
    public int PositionedBoxCount { get; }
    public int HorizontalOverflowCount { get; }
    public int VerticalOverflowCount { get; }
    public int PeakBoxArena { get; }
    public int PeakLineArena { get; }
    public int PeakFragmentArena { get; }
    public int PeakTraversalDepth { get; }
    public int DocumentContentWidth { get; }
    public int DocumentContentHeight { get; }
}

public enum ManagedLayoutValidationFailureReason : byte
{
    None = 0,
    NotLaidOut = 1,
    BoxRangeInvalid = 2,
    SourceNodeInvalid = 3,
    ParentLinkMismatch = 4,
    ChildLinkMismatch = 5,
    SiblingCycle = 6,
    RectangleInvalid = 7,
    FragmentSourceInvalid = 8,
    FragmentLineInvalid = 9,
    HiddenNodeBox = 10,
    ArenaRangeInvalid = 11
}

internal struct ManagedLayoutBoxRecord
{
    internal int SourceNodeIndex;
    internal ManagedLayoutBoxKind Kind;
    internal int ParentIndex;
    internal int FirstChildIndex;
    internal int LastChildIndex;
    internal int NextSiblingIndex;
    internal ManagedLayoutBoxFlags Flags;
    internal int ZIndex;
    internal ManagedLayoutRect BorderBox;
    internal ManagedLayoutRect PaddingBox;
    internal ManagedLayoutRect ContentRect;
    internal ManagedLayoutEdges Margin;
    internal ManagedLayoutEdges Padding;
    internal ManagedLayoutEdges Border;
    internal ManagedLayoutRect OverflowExtent;
    internal ManagedLayoutRect ClipRect;

    internal ManagedLayoutBox Public => new(SourceNodeIndex, Kind, ParentIndex,
        FirstChildIndex, LastChildIndex, NextSiblingIndex, Flags, ZIndex, BorderBox,
        PaddingBox, ContentRect, Margin, Padding, Border, OverflowExtent, ClipRect);
}

internal struct ManagedLayoutFrame
{
    internal int BoxIndex;
    internal int SourceNodeIndex;
    internal int NextChildIndex;
    internal int ContentX;
    internal int ContentY;
    internal int ContentWidth;
    internal int DefiniteHeight;
    internal int CursorY;
    internal int PendingChildIndex;
    internal bool IsDetached;
}

internal struct ManagedLayoutInlineFrame
{
    internal int BoxIndex;
    internal int NextChildIndex;
}

/// <summary>
/// Fixed-arena geometry engine for the Phase 43 document and Phase 44 styles.
/// It implements a bounded block/inline subset and intentionally does not
/// paint, shape, bidi-process, or decode replaced content.
/// </summary>
public sealed class ManagedLayoutEngine
{
    private readonly ManagedHtmlDocument _document;
    private readonly ManagedCssEngine _styles;
    private readonly ManagedLayoutBoxRecord[] _boxes;
    private readonly ManagedLayoutLine[] _lines;
    private readonly ManagedLayoutTextFragment[] _fragments;
    private readonly ManagedLayoutFrame[] _frames;
    private readonly ManagedLayoutInlineFrame[] _inlineFrames;
    private readonly int[] _nodeToBox;
    private readonly int[] _flowX;
    private readonly int[] _flowY;
    private readonly IManagedLayoutTextMetrics _metrics;
    private readonly ManagedSha256 _hash = new();
    private readonly byte[] _layoutHash = new byte[ManagedSha256.DigestSize];
    private int _boxCount;
    private int _lineCount;
    private int _fragmentCount;
    private int _frameCount;
    private int _inlineFrameCount;
    private int _rootBox = -1;
    private int _viewportWidth;
    private int _viewportHeight;
    private int _documentContentWidth;
    private int _documentContentHeight;
    private int _blockBoxCount;
    private int _inlineTextBoxCount;
    private int _textScalarsMeasured;
    private int _softWrapCount;
    private int _forcedBreakCount;
    private int _displayNoneSkips;
    private int _positionedBoxCount;
    private int _horizontalOverflowCount;
    private int _verticalOverflowCount;
    private int _peakTraversalDepth;
    private ManagedLayoutFailureReason _failureReason;
    private bool _laidOut;
    private bool _cancelled;
    private bool _hashAvailable;

    public ManagedLayoutEngine(ManagedHtmlDocument document, ManagedCssEngine styles)
        : this(document, styles, ManagedLayoutArenaOptions.Default, null) { }

    public ManagedLayoutEngine(ManagedHtmlDocument document, ManagedCssEngine styles,
                               ManagedLayoutArenaOptions options,
                               IManagedLayoutTextMetrics? metrics = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _styles = styles ?? throw new ArgumentNullException(nameof(styles));
        _boxes = new ManagedLayoutBoxRecord[options.BoxCapacity];
        _lines = new ManagedLayoutLine[options.LineCapacity];
        _fragments = new ManagedLayoutTextFragment[options.TextFragmentCapacity];
        _frames = new ManagedLayoutFrame[options.TraversalStackCapacity];
        _inlineFrames = new ManagedLayoutInlineFrame[options.TraversalStackCapacity];
        _nodeToBox = new int[ManagedHtmlDocumentLimits.MaximumNodeCapacity];
        _flowX = new int[options.BoxCapacity];
        _flowY = new int[options.BoxCapacity];
        TableColumnCapacity = options.TableColumnCapacity;
        _metrics = metrics ?? new ManagedDeterministicLayoutTextMetrics();
        Reset();
    }

    public ManagedHtmlDocument Document => _document;
    public ManagedCssEngine Styles => _styles;
    public ManagedLayoutFailureReason FailureReason => _failureReason;
    public bool IsLaidOut => _laidOut;
    public bool CanonicalHashAvailable => _hashAvailable;
    public ManagedLayoutViewport Viewport => new(_viewportWidth, _viewportHeight);
    public int LayoutBoxCapacity => _boxes.Length;
    public int LineCapacity => _lines.Length;
    public int TextFragmentCapacity => _fragments.Length;
    public int TraversalStackCapacity => _frames.Length;
    public int TableColumnCapacity { get; }
    public int LayoutBoxCount => _boxCount;
    public int BlockBoxCount => _blockBoxCount;
    public int InlineTextBoxCount => _inlineTextBoxCount;
    public int LineCount => _lineCount;
    public int TextFragmentCount => _fragmentCount;
    public int TextScalarsMeasured => _textScalarsMeasured;
    public int SoftWrapCount => _softWrapCount;
    public int ForcedBreakCount => _forcedBreakCount;
    public int DisplayNoneSkips => _displayNoneSkips;
    public int PositionedBoxCount => _positionedBoxCount;
    public int HorizontalOverflowCount => _horizontalOverflowCount;
    public int VerticalOverflowCount => _verticalOverflowCount;
    public int PeakBoxArena => _boxCount;
    public int PeakLineArena => _lineCount;
    public int PeakFragmentArena => _fragmentCount;
    public int PeakTraversalDepth => _peakTraversalDepth;
    public int DocumentContentWidth => _documentContentWidth;
    public int DocumentContentHeight => _documentContentHeight;
    public ManagedLayoutTelemetry Telemetry => new(this);

    public void Cancel() => _cancelled = true;

    public void Reset()
    {
        _boxes.AsSpan().Clear();
        _lines.AsSpan().Clear();
        _fragments.AsSpan().Clear();
        _frames.AsSpan().Clear();
        _inlineFrames.AsSpan().Clear();
        _nodeToBox.AsSpan().Fill(-1);
        _flowX.AsSpan().Clear();
        _flowY.AsSpan().Clear();
        _boxCount = 0;
        _lineCount = 0;
        _fragmentCount = 0;
        _frameCount = 0;
        _inlineFrameCount = 0;
        _rootBox = -1;
        _viewportWidth = 0;
        _viewportHeight = 0;
        _documentContentWidth = 0;
        _documentContentHeight = 0;
        _blockBoxCount = 0;
        _inlineTextBoxCount = 0;
        _textScalarsMeasured = 0;
        _softWrapCount = 0;
        _forcedBreakCount = 0;
        _displayNoneSkips = 0;
        _positionedBoxCount = 0;
        _horizontalOverflowCount = 0;
        _verticalOverflowCount = 0;
        _peakTraversalDepth = 0;
        _failureReason = ManagedLayoutFailureReason.None;
        _laidOut = false;
        _cancelled = false;
        _hashAvailable = false;
        _layoutHash.AsSpan().Clear();
        _hash.Reset();
    }

    public bool TryLayout(int viewportWidth, int viewportHeight) =>
        TryLayout(new ManagedLayoutViewport(viewportWidth, viewportHeight));

    public bool TryLayout(ManagedLayoutViewport viewport)
    {
        bool cancellationRequested = _cancelled;
        Reset();
        if (cancellationRequested) return Fail(ManagedLayoutFailureReason.Cancelled);
        if (viewport.Width < 0 || viewport.Height < 0 ||
            viewport.Width > ManagedLayoutLimits.MaximumCoordinate ||
            viewport.Height > ManagedLayoutLimits.MaximumCoordinate)
            return Fail(ManagedLayoutFailureReason.InvalidViewport);
        if (!_document.IsValid(_document.DocumentNode) ||
            !_document.Validate(out ManagedHtmlDocumentValidationFailureReason documentFailure) ||
            documentFailure != ManagedHtmlDocumentValidationFailureReason.None)
            return Fail(ManagedLayoutFailureReason.InvalidDocument);
        if (_styles.Document != _document || !_styles.IsStyled)
            return Fail(ManagedLayoutFailureReason.InvalidStyles);
        _viewportWidth = viewport.Width;
        _viewportHeight = viewport.Height;
        if (!BuildBoxTree()) return false;
        if (_rootBox < 0 || !RunBlockLayout(_rootBox, 0, 0, viewport.Width, viewport.Height, false))
            return false;
        if (!LayoutPositionedBoxes()) return false;
        if (!FinalizeOverflowAndExtents()) return false;
        if (!ComputeLayoutHash()) return false;
        _laidOut = true;
        return true;
    }

    public bool TryGetBox(int index, out ManagedLayoutBox box)
    {
        box = default;
        if (index < 0 || index >= _boxCount) return false;
        box = _boxes[index].Public;
        return true;
    }

    public bool TryGetLine(int index, out ManagedLayoutLine line)
    {
        line = default;
        if (index < 0 || index >= _lineCount) return false;
        line = _lines[index];
        return true;
    }

    public bool TryGetTextFragment(int index, out ManagedLayoutTextFragment fragment)
    {
        fragment = default;
        if (index < 0 || index >= _fragmentCount) return false;
        fragment = _fragments[index];
        return true;
    }

    public bool TryGetBoxForNode(ManagedHtmlNodeHandle node, out int boxIndex)
    {
        boxIndex = -1;
        if (!_laidOut || !_document.IsValid(node) || node.Index >= _nodeToBox.Length) return false;
        boxIndex = _nodeToBox[node.Index];
        return boxIndex >= 0;
    }

    public bool TryCopyCanonicalLayoutHash(Span<byte> destination)
    {
        if (!_hashAvailable || destination.Length < _layoutHash.Length) return false;
        _layoutHash.AsSpan().CopyTo(destination);
        return true;
    }

    public bool Validate(out ManagedLayoutValidationFailureReason reason)
    {
        reason = ManagedLayoutValidationFailureReason.None;
        if (!_laidOut) return FailValidation(ManagedLayoutValidationFailureReason.NotLaidOut, out reason);
        if (_boxCount < 1 || _boxCount > _boxes.Length || _rootBox != 0)
            return FailValidation(ManagedLayoutValidationFailureReason.BoxRangeInvalid, out reason);
        for (int index = 0; index != _boxCount; ++index)
        {
            ManagedLayoutBoxRecord box = _boxes[index];
            if (box.SourceNodeIndex < 0 || box.SourceNodeIndex >= _document.NodeCount)
                return FailValidation(ManagedLayoutValidationFailureReason.SourceNodeInvalid, out reason);
            if (box.ParentIndex >= _boxCount || box.FirstChildIndex >= _boxCount ||
                box.LastChildIndex >= _boxCount || box.NextSiblingIndex >= _boxCount)
                return FailValidation(ManagedLayoutValidationFailureReason.BoxRangeInvalid, out reason);
            if (box.ParentIndex >= 0 && !ChildContains(box.ParentIndex, index))
                return FailValidation(ManagedLayoutValidationFailureReason.ParentLinkMismatch, out reason);
            if (!ValidRect(box.BorderBox) || !ValidRect(box.PaddingBox) ||
                !ValidRect(box.ContentRect) || !ValidRect(box.OverflowExtent) ||
                !ValidRect(box.ClipRect))
                return FailValidation(ManagedLayoutValidationFailureReason.RectangleInvalid, out reason);
            int sibling = box.FirstChildIndex;
            int guard = 0;
            while (sibling >= 0)
            {
                if (++guard > _boxCount) return FailValidation(ManagedLayoutValidationFailureReason.SiblingCycle, out reason);
                if (sibling >= _boxCount || _boxes[sibling].ParentIndex != index)
                    return FailValidation(ManagedLayoutValidationFailureReason.ChildLinkMismatch, out reason);
                sibling = _boxes[sibling].NextSiblingIndex;
            }
        }
        for (int index = 0; index != _fragmentCount; ++index)
        {
            ManagedLayoutTextFragment fragment = _fragments[index];
            if (fragment.SourceNodeIndex < 0 || fragment.SourceNodeIndex >= _document.NodeCount ||
                fragment.SourceOffset < 0 || fragment.SourceLength < 0 ||
                fragment.SourceOffset > _document.GetTextLength(NodeHandle(fragment.SourceNodeIndex)) - fragment.SourceLength)
                return FailValidation(ManagedLayoutValidationFailureReason.FragmentSourceInvalid, out reason);
            if (fragment.OwnerBoxIndex < 0 || fragment.OwnerBoxIndex >= _boxCount ||
                fragment.LineIndex < 0 || fragment.LineIndex >= _lineCount || !ValidRect(fragment.Rectangle))
                return FailValidation(ManagedLayoutValidationFailureReason.FragmentLineInvalid, out reason);
        }
        for (int node = 0; node != _document.NodeCount; ++node)
        {
            ManagedHtmlNodeHandle handle = NodeHandle(node);
            if (_document.GetNodeKind(handle) != ManagedHtmlNodeKind.Element ||
                !_styles.TryGetComputedStyle(handle, out ManagedComputedStyle style) ||
                style.Display != ManagedCssDisplay.None) continue;
            if (_nodeToBox[node] >= 0)
                return FailValidation(ManagedLayoutValidationFailureReason.HiddenNodeBox, out reason);
        }
        return true;
    }

    private bool BuildBoxTree()
    {
        if (_document.RootIndex < 0 || !PushBuildFrame(_document.RootIndex, -1))
            return Fail(ManagedLayoutFailureReason.InvalidDocument);
        while (_frameCount != 0)
        {
            if (_cancelled) return Fail(ManagedLayoutFailureReason.Cancelled);
            ref ManagedLayoutFrame frame = ref _frames[_frameCount - 1];
            if (frame.NextChildIndex == int.MinValue)
            {
                frame.NextChildIndex = IndexOf(_document.GetFirstChild(NodeHandle(frame.SourceNodeIndex)));
            }
            if (frame.BoxIndex == -1)
            {
                int source = _document.RootIndex;
                if (_rootBox < 0)
                {
                    if (!AddBox(source, ManagedLayoutBoxKind.Root, -1, ManagedLayoutBoxFlags.InFlow, 0,
                                out _rootBox)) return false;
                    frame.BoxIndex = _rootBox;
                    frame.SourceNodeIndex = source;
                    frame.NextChildIndex = IndexOf(_document.GetFirstChild(NodeHandle(source)));
                    continue;
                }
            }
            int childNode = frame.NextChildIndex;
            if (childNode < 0)
            {
                --_frameCount;
                continue;
            }
            ManagedHtmlNodeHandle child = NodeHandle(childNode);
            frame.NextChildIndex = IndexOf(_document.GetNextSibling(child));
            int parentBox = frame.BoxIndex;
            ManagedHtmlNodeKind kind = _document.GetNodeKind(child);
            int childBox = -1;
            bool descend = true;
            if (kind == ManagedHtmlNodeKind.Element)
            {
                if (!_styles.TryGetComputedStyle(child, out ManagedComputedStyle style))
                    return Fail(ManagedLayoutFailureReason.InvalidStyles);
                if (style.Display == ManagedCssDisplay.None || IsNonRendered(child))
                {
                    ++_displayNoneSkips;
                    descend = false;
                }
                else
                {
                    ManagedLayoutBoxKind boxKind = BoxKind(child, style);
                    ManagedLayoutBoxFlags flags = PositionFlags(style.Position) |
                        (style.Position == ManagedCssPosition.Static ? ManagedLayoutBoxFlags.InFlow : ManagedLayoutBoxFlags.None);
                    if (boxKind == ManagedLayoutBoxKind.Replaced) flags |= ManagedLayoutBoxFlags.Replaced;
                    if (style.Position != ManagedCssPosition.Static) ++_positionedBoxCount;
                    if (boxKind == ManagedLayoutBoxKind.Block || boxKind == ManagedLayoutBoxKind.Table ||
                        boxKind == ManagedLayoutBoxKind.TableRow || boxKind == ManagedLayoutBoxKind.TableCell)
                        ++_blockBoxCount;
                    if (boxKind == ManagedLayoutBoxKind.InlineContainer || boxKind == ManagedLayoutBoxKind.Text ||
                        boxKind == ManagedLayoutBoxKind.LineBreak || boxKind == ManagedLayoutBoxKind.Replaced)
                        ++_inlineTextBoxCount;
                    if (!AddBox(childNode, boxKind, parentBox, flags, style.ZIndex, out childBox)) return false;
                }
            }
            else if (kind == ManagedHtmlNodeKind.Text && parentBox >= 0)
            {
                if (!AddBox(childNode, ManagedLayoutBoxKind.Text, parentBox,
                            ManagedLayoutBoxFlags.InFlow | ManagedLayoutBoxFlags.HasText, 0,
                            out childBox)) return false;
                ++_inlineTextBoxCount;
            }
            else
            {
                descend = false;
            }
            if (descend && childBox >= 0)
            {
                if (!PushBuildFrame(childNode, childBox)) return false;
                _frames[_frameCount - 1].NextChildIndex = int.MinValue;
            }
        }
        return _rootBox >= 0;
    }

    private bool PushBuildFrame(int nodeIndex, int parentBox)
    {
        if (_frameCount == _frames.Length)
            return Fail(ManagedLayoutFailureReason.TraversalStackCapacityExceeded);
        _frames[_frameCount++] = new ManagedLayoutFrame
        {
            BoxIndex = parentBox == -1 && nodeIndex == _document.RootIndex ? -1 : parentBox,
            SourceNodeIndex = nodeIndex,
            NextChildIndex = int.MinValue
        };
        if (_frameCount > _peakTraversalDepth) _peakTraversalDepth = _frameCount;
        return true;
    }

    private bool AddBox(int sourceNodeIndex, ManagedLayoutBoxKind kind, int parentIndex,
                        ManagedLayoutBoxFlags flags, int zIndex, out int boxIndex)
    {
        boxIndex = -1;
        if (_boxCount == _boxes.Length)
            return Fail(ManagedLayoutFailureReason.LayoutBoxCapacityExceeded);
        if (sourceNodeIndex < 0 || sourceNodeIndex >= _nodeToBox.Length)
            return Fail(ManagedLayoutFailureReason.InvalidDocument);
        boxIndex = _boxCount++;
        _boxes[boxIndex] = new ManagedLayoutBoxRecord
        {
            SourceNodeIndex = sourceNodeIndex,
            Kind = kind,
            ParentIndex = parentIndex,
            FirstChildIndex = -1,
            LastChildIndex = -1,
            NextSiblingIndex = -1,
            Flags = flags,
            ZIndex = zIndex,
            BorderBox = new ManagedLayoutRect(0, 0, 0, 0),
            PaddingBox = new ManagedLayoutRect(0, 0, 0, 0),
            ContentRect = new ManagedLayoutRect(0, 0, 0, 0),
            OverflowExtent = new ManagedLayoutRect(0, 0, 0, 0),
            ClipRect = new ManagedLayoutRect(0, 0, 0, 0)
        };
        _nodeToBox[sourceNodeIndex] = boxIndex;
        if (parentIndex >= 0)
        {
            if (_boxes[parentIndex].FirstChildIndex < 0) _boxes[parentIndex].FirstChildIndex = boxIndex;
            else _boxes[_boxes[parentIndex].LastChildIndex].NextSiblingIndex = boxIndex;
            _boxes[parentIndex].LastChildIndex = boxIndex;
        }
        return true;
    }

    private bool RunBlockLayout(int rootBoxIndex, int containingX, int containingY,
                                int containingWidth, int containingHeight, bool detached)
    {
        _frameCount = 0;
        if (!PrepareBlock(rootBoxIndex, containingX, containingY, containingWidth, containingHeight,
                          detached, out ManagedLayoutFrame rootFrame)) return false;
        if (!PushLayoutFrame(rootFrame)) return false;
        while (_frameCount != 0)
        {
            if (_cancelled) return Fail(ManagedLayoutFailureReason.Cancelled);
            ref ManagedLayoutFrame frame = ref _frames[_frameCount - 1];
            if (frame.PendingChildIndex >= 0)
            {
                int pendingChild = frame.PendingChildIndex;
                frame.PendingChildIndex = -1;
                if (pendingChild >= 0 && pendingChild < _boxCount && IsInFlow(_boxes[pendingChild]))
                {
                    long end = (long)_flowY[pendingChild] + _boxes[pendingChild].BorderBox.Height +
                        _boxes[pendingChild].Margin.Bottom;
                    long start = frame.ContentY;
                    long relative = end - start;
                    if (relative > frame.CursorY) {
                        if (relative > ManagedLayoutLimits.MaximumCoordinate) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                        frame.CursorY = (int)relative;
                    }
                }
            }
            int childIndex = frame.NextChildIndex;
            if (childIndex < 0)
            {
                if (!FinishBlockFrame(ref frame)) return false;
                --_frameCount;
                continue;
            }
            frame.NextChildIndex = _boxes[childIndex].NextSiblingIndex;
            ManagedLayoutBoxRecord childRecord = _boxes[childIndex];
            if (!IsInFlow(childRecord) || childRecord.Kind == ManagedLayoutBoxKind.InlineContainer ||
                childRecord.Kind == ManagedLayoutBoxKind.Text || childRecord.Kind == ManagedLayoutBoxKind.LineBreak ||
                childRecord.Kind == ManagedLayoutBoxKind.Replaced)
            {
                if (childRecord.Kind == ManagedLayoutBoxKind.Block || childRecord.Kind == ManagedLayoutBoxKind.Table ||
                    childRecord.Kind == ManagedLayoutBoxKind.TableRow || childRecord.Kind == ManagedLayoutBoxKind.TableCell)
                    continue;
                int last = childIndex;
                while (last >= 0)
                {
                    ManagedLayoutBoxRecord item = _boxes[last];
                    if (!IsInFlow(item) || item.Kind == ManagedLayoutBoxKind.Block ||
                        item.Kind == ManagedLayoutBoxKind.Table || item.Kind == ManagedLayoutBoxKind.TableRow ||
                        item.Kind == ManagedLayoutBoxKind.TableCell) break;
                    last = item.NextSiblingIndex;
                }
                int nextAfter = last;
                int inlineY = AddChecked(frame.ContentY, frame.CursorY, out bool inlineYOk);
                if (!inlineYOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                if (!LayoutInlineSequence(frame.BoxIndex, childIndex, frame.ContentX, inlineY,
                                          frame.ContentWidth, out int inlineHeight)) return false;
                frame.CursorY = AddChecked(frame.CursorY, inlineHeight, out bool ok);
                if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                frame.NextChildIndex = nextAfter;
                continue;
            }
            int childY = AddChecked(frame.ContentY, frame.CursorY, out bool childYOk);
            if (!childYOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
            if (!PrepareBlock(childIndex, frame.ContentX, childY,
                              frame.ContentWidth, frame.DefiniteHeight, false,
                              out ManagedLayoutFrame childFrame)) return false;
            frame.PendingChildIndex = childIndex;
            if (!PushLayoutFrame(childFrame)) return false;
        }
        return true;
    }

    private bool PushLayoutFrame(ManagedLayoutFrame frame)
    {
        if (_frameCount == _frames.Length)
            return Fail(ManagedLayoutFailureReason.TraversalStackCapacityExceeded);
        _frames[_frameCount++] = frame;
        if (_frameCount > _peakTraversalDepth) _peakTraversalDepth = _frameCount;
        return true;
    }

    private bool PrepareBlock(int boxIndex, int containingX, int normalY, int containingWidth,
                              int containingHeight, bool detached, out ManagedLayoutFrame frame)
    {
        frame = default;
        ManagedLayoutBoxRecord box = _boxes[boxIndex];
        if (box.Kind == ManagedLayoutBoxKind.Root)
        {
            box.Margin = new ManagedLayoutEdges(0, 0, 0, 0);
            box.Padding = new ManagedLayoutEdges(0, 0, 0, 0);
            box.Border = new ManagedLayoutEdges(0, 0, 0, 0);
            box.BorderBox = new ManagedLayoutRect(containingX, normalY, containingWidth, containingHeight);
            box.PaddingBox = box.BorderBox;
            box.ContentRect = box.BorderBox;
            box.OverflowExtent = box.ContentRect;
            box.ClipRect = box.BorderBox;
            _boxes[boxIndex] = box;
            _flowX[boxIndex] = containingX;
            _flowY[boxIndex] = normalY;
            frame = new ManagedLayoutFrame
            {
                BoxIndex = boxIndex,
                SourceNodeIndex = box.SourceNodeIndex,
                NextChildIndex = box.FirstChildIndex,
                ContentX = containingX,
                ContentY = normalY,
                ContentWidth = containingWidth,
                DefiniteHeight = containingHeight,
                CursorY = 0,
                PendingChildIndex = -1,
                IsDetached = detached
            };
            return true;
        }
        ManagedComputedStyle style = StyleForBox(boxIndex);
        if (!ResolveEdges(style, containingWidth, out ManagedLayoutEdges margin,
                          out ManagedLayoutEdges padding, out ManagedLayoutEdges border)) return false;
        int marginHorizontal = AddChecked(margin.Left, margin.Right, out bool marginOk);
        int paddingHorizontal = AddChecked(padding.Left, padding.Right, out bool paddingOk);
        int borderHorizontal = AddChecked(border.Left, border.Right, out bool borderOk);
        int horizontalChrome = AddChecked(paddingHorizontal, borderHorizontal, out bool chromeOk);
        int horizontal = AddChecked(marginHorizontal, horizontalChrome, out bool horizontalOk);
        if (!marginOk || !paddingOk || !borderOk || !chromeOk || !horizontalOk)
            return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        int available = Math.Max(0, containingWidth - horizontal);
        int contentWidth = ResolveDimension(style.Width, containingWidth, 0, available, out bool widthOk);
        if (!widthOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        if (style.Width.IsAuto) contentWidth = available;
        contentWidth = ClampDimension(contentWidth, style.MinWidth, style.MaxWidth, containingWidth, 0, out bool clampOk);
        if (!clampOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        int contentHeight = ResolveDimension(style.Height, containingHeight, 0, 0, out bool heightOk);
        if (!heightOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        bool definiteHeight = !style.Height.IsAuto && !(style.Height.Unit == ManagedCssLengthUnit.Percent && containingHeight < 0);
        if (!definiteHeight) contentHeight = 0;
        contentHeight = ClampDimension(contentHeight, style.MinHeight, style.MaxHeight, containingWidth, 0, out clampOk);
        if (!clampOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        int paddingVertical = AddChecked(padding.Top, padding.Bottom, out bool paddingVerticalOk);
        int borderVertical = AddChecked(border.Top, border.Bottom, out bool borderVerticalOk);
        int borderWidth = AddChecked(contentWidth, paddingHorizontal, out bool widthOk1);
        borderWidth = AddChecked(borderWidth, borderHorizontal, out bool widthOk2);
        int borderHeight = AddChecked(contentHeight, paddingVertical, out bool heightOk1);
        borderHeight = AddChecked(borderHeight, borderVertical, out bool heightOk2);
        if (!paddingVerticalOk || !borderVerticalOk || !widthOk1 || !widthOk2 || !heightOk1 || !heightOk2)
            return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        long normalXLong = (long)containingX + margin.Left;
        long normalYLong = (long)normalY + margin.Top;
        if (normalXLong < -ManagedLayoutLimits.MaximumCoordinate || normalXLong > ManagedLayoutLimits.MaximumCoordinate ||
            normalYLong < -ManagedLayoutLimits.MaximumCoordinate || normalYLong > ManagedLayoutLimits.MaximumCoordinate)
            return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        int normalX = (int)normalXLong;
        int visualX = normalX;
        int visualY = (int)normalYLong;
        ManagedLayoutFlagsAndOffsets(style, ref visualX, ref visualY);
        if (_failureReason != ManagedLayoutFailureReason.None) return false;
        box.Margin = margin;
        box.Padding = padding;
        box.Border = border;
        int paddingX = AddChecked(visualX, border.Left, out bool paddingXOk);
        int paddingY = AddChecked(visualY, border.Top, out bool paddingYOk);
        int contentX = AddChecked(paddingX, padding.Left, out bool contentXOk);
        int contentY = AddChecked(paddingY, padding.Top, out bool contentYOk);
        if (!paddingXOk || !paddingYOk || !contentXOk || !contentYOk)
            return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        box.BorderBox = new ManagedLayoutRect(visualX, visualY, borderWidth, borderHeight);
        box.PaddingBox = new ManagedLayoutRect(paddingX, paddingY,
            borderWidth - borderHorizontal, borderHeight - borderVertical);
        box.ContentRect = new ManagedLayoutRect(contentX, contentY, contentWidth, contentHeight);
        box.OverflowExtent = box.ContentRect;
        box.ClipRect = EffectiveOverflowX(style) == ManagedCssOverflow.Visible && EffectiveOverflowY(style) == ManagedCssOverflow.Visible
            ? new ManagedLayoutRect(int.MinValue / 2, int.MinValue / 2, ManagedLayoutLimits.MaximumCoordinate,
                                    ManagedLayoutLimits.MaximumCoordinate)
            : box.PaddingBox;
        _boxes[boxIndex] = box;
        _flowX[boxIndex] = normalX;
        _flowY[boxIndex] = (int)normalYLong;
        frame = new ManagedLayoutFrame
        {
            BoxIndex = boxIndex,
            SourceNodeIndex = box.SourceNodeIndex,
            NextChildIndex = box.FirstChildIndex,
            ContentX = box.ContentRect.X,
            ContentY = box.ContentRect.Y,
            ContentWidth = box.ContentRect.Width,
            DefiniteHeight = definiteHeight ? contentHeight : -1,
            CursorY = 0,
            PendingChildIndex = -1,
            IsDetached = detached
        };
        return true;
    }

    private void ManagedLayoutFlagsAndOffsets(ManagedComputedStyle style, ref int x, ref int y)
    {
        if (style.Position != ManagedCssPosition.Relative) return;
        int left = ResolveOffset(style.Left, 0);
        int right = ResolveOffset(style.Right, 0);
        int top = ResolveOffset(style.Top, 0);
        int bottom = ResolveOffset(style.Bottom, 0);
        x = AddUncheckedChecked(x, !style.Left.IsAuto ? left : -right, ref _failureReason);
        y = AddUncheckedChecked(y, !style.Top.IsAuto ? top : -bottom, ref _failureReason);
    }

    private bool FinishBlockFrame(ref ManagedLayoutFrame frame)
    {
        ManagedLayoutBoxRecord box = _boxes[frame.BoxIndex];
        int contentHeight = frame.DefiniteHeight >= 0 ? frame.DefiniteHeight : frame.CursorY;
        ManagedComputedStyle style = StyleForBox(frame.BoxIndex);
        bool ok = true;
        if (box.Kind != ManagedLayoutBoxKind.Root)
            contentHeight = ClampDimension(contentHeight, style.MinHeight, style.MaxHeight,
                                           box.ContentRect.Width, 0, out ok);
        if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        int paddingVertical = AddChecked(box.Padding.Top, box.Padding.Bottom, out bool paddingOk);
        int borderVertical = AddChecked(box.Border.Top, box.Border.Bottom, out bool borderOk);
        int borderHeight = AddChecked(contentHeight, paddingVertical, out bool ok1);
        borderHeight = AddChecked(borderHeight, borderVertical, out bool ok2);
        if (!paddingOk || !borderOk || !ok1 || !ok2) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        box.BorderBox = new ManagedLayoutRect(box.BorderBox.X, box.BorderBox.Y,
            box.BorderBox.Width, borderHeight);
        box.PaddingBox = new ManagedLayoutRect(box.PaddingBox.X, box.PaddingBox.Y,
            box.PaddingBox.Width, borderHeight - borderVertical);
        box.ContentRect = new ManagedLayoutRect(box.ContentRect.X, box.ContentRect.Y,
            box.ContentRect.Width, contentHeight);
        if (box.OverflowExtent.Height < contentHeight) box.OverflowExtent =
            new ManagedLayoutRect(box.ContentRect.X, box.ContentRect.Y,
                                  Math.Max(box.OverflowExtent.Width, contentHeight > 0 ? box.ContentRect.Width : 0),
                                  contentHeight);
        _boxes[frame.BoxIndex] = box;
        return true;
    }

    private bool LayoutInlineSequence(int ownerBoxIndex, int firstBoxIndex, int x, int y,
                                      int width, out int height)
    {
        height = 0;
        int currentX = x;
        int lineY = y;
        int lineHeight = 0;
        int lineIndex = -1;
        int lineFirstFragment = _fragmentCount;
        int lineFragmentCount = 0;
        bool lineHasContent = false;
        int currentOwner = ownerBoxIndex;
        int currentStyleBox = ownerBoxIndex;
        int child = firstBoxIndex;
        while (child >= 0)
        {
            if (_boxes[child].Kind == ManagedLayoutBoxKind.Block ||
                _boxes[child].Kind == ManagedLayoutBoxKind.Table ||
                _boxes[child].Kind == ManagedLayoutBoxKind.TableRow ||
                _boxes[child].Kind == ManagedLayoutBoxKind.TableCell) break;
            if (!ConsumeInlineBox(child, ownerBoxIndex, x, width, ref currentX, ref lineY,
                                  ref lineHeight, ref lineIndex, ref lineFirstFragment,
                                  ref lineFragmentCount, ref lineHasContent)) return false;
            child = _boxes[child].NextSiblingIndex;
        }
        if (lineHasContent || lineIndex >= 0)
        {
            if (!CloseLine(ownerBoxIndex, lineIndex, lineY, x, currentX - x, lineHeight,
                           lineFirstFragment, lineFragmentCount)) return false;
            height = (lineY - y) + lineHeight;
        }
        _ = currentOwner;
        _ = currentStyleBox;
        return true;
    }

    private bool ConsumeInlineBox(int boxIndex, int ownerBoxIndex, int contentX, int contentWidth,
                                  ref int currentX, ref int lineY, ref int lineHeight,
                                  ref int lineIndex, ref int lineFirstFragment,
                                  ref int lineFragmentCount, ref bool lineHasContent)
    {
        ManagedLayoutBoxRecord box = _boxes[boxIndex];
        if (box.Kind == ManagedLayoutBoxKind.Text)
            return LayoutTextBox(boxIndex, ownerBoxIndex, contentX, contentWidth, ref currentX,
                                 ref lineY, ref lineHeight, ref lineIndex, ref lineFirstFragment,
                                 ref lineFragmentCount, ref lineHasContent);
        if (box.Kind == ManagedLayoutBoxKind.LineBreak)
        {
            if (!EnsureLine(ownerBoxIndex, contentX, lineY, ref lineIndex, ref lineFirstFragment)) return false;
            if (!CloseLine(ownerBoxIndex, lineIndex, lineY, contentX, currentX - contentX,
                           lineHeight, lineFirstFragment, lineFragmentCount)) return false;
            ++_forcedBreakCount;
            currentX = contentX;
            lineY = AddChecked(lineY, lineHeight == 0 ? 1 : lineHeight, out bool ok);
            if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
            lineHeight = 0;
            lineIndex = -1;
            lineFirstFragment = _fragmentCount;
            lineFragmentCount = 0;
            lineHasContent = false;
            return true;
        }
        if (box.Kind == ManagedLayoutBoxKind.Replaced)
            return LayoutReplacedInline(boxIndex, ownerBoxIndex, contentX, contentWidth, ref currentX,
                                        ref lineY, ref lineHeight, ref lineIndex, ref lineFirstFragment,
                                        ref lineFragmentCount, ref lineHasContent);
        _inlineFrameCount = 0;
        if (_inlineFrameCount == _inlineFrames.Length)
            return Fail(ManagedLayoutFailureReason.TraversalStackCapacityExceeded);
        _inlineFrames[_inlineFrameCount++] = new ManagedLayoutInlineFrame
        {
            BoxIndex = boxIndex,
            NextChildIndex = box.FirstChildIndex
        };
        ManagedLayoutRect aggregate = new(currentX, lineY, 0, 0);
        while (_inlineFrameCount != 0)
        {
            ref ManagedLayoutInlineFrame frame = ref _inlineFrames[_inlineFrameCount - 1];
            int child = frame.NextChildIndex;
            if (child < 0)
            {
                ManagedLayoutBoxRecord inlineBox = _boxes[frame.BoxIndex];
                if (aggregate.Width > 0 || aggregate.Height > 0)
                {
                    inlineBox.BorderBox = aggregate;
                    inlineBox.PaddingBox = aggregate;
                    inlineBox.ContentRect = aggregate;
                    _boxes[frame.BoxIndex] = inlineBox;
                }
                --_inlineFrameCount;
                continue;
            }
            frame.NextChildIndex = _boxes[child].NextSiblingIndex;
            if (!ConsumeInlineBox(child, ownerBoxIndex, contentX, contentWidth, ref currentX, ref lineY,
                                  ref lineHeight, ref lineIndex, ref lineFirstFragment,
                                  ref lineFragmentCount, ref lineHasContent)) return false;
            int right = currentX;
            int bottom = AddChecked(lineY, lineHeight, out bool ok);
            if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
            if (right > aggregate.Right) aggregate = new ManagedLayoutRect(aggregate.X, aggregate.Y,
                right - aggregate.X, Math.Max(aggregate.Height, bottom - aggregate.Y));
        }
        return true;
    }

    private bool LayoutTextBox(int boxIndex, int ownerBoxIndex, int contentX, int contentWidth,
                               ref int currentX, ref int lineY, ref int lineHeight, ref int lineIndex,
                               ref int lineFirstFragment, ref int lineFragmentCount, ref bool lineHasContent)
    {
        ManagedLayoutBoxRecord box = _boxes[boxIndex];
        ManagedHtmlNodeHandle node = NodeHandle(box.SourceNodeIndex);
        ManagedHtmlNodeHandle parent = _document.GetParent(node);
        ManagedComputedStyle style = parent != ManagedHtmlNodeHandle.Invalid &&
            _styles.TryGetComputedStyle(parent, out ManagedComputedStyle parentStyle) ? parentStyle :
            StyleForBox(ownerBoxIndex);
        ManagedLayoutTextStyle textStyle = TextStyle(style,
            parent == ManagedHtmlNodeHandle.Invalid ? parent : _document.GetParent(parent));
        int length = _document.GetTextLength(node);
        int offset = 0;
        while (offset < length)
        {
            int textOffset = _document.Nodes[box.SourceNodeIndex].TextOffset;
            uint scalar = _document.Text[textOffset + offset];
            bool pre = style.WhiteSpace == ManagedCssWhiteSpace.Pre ||
                       style.WhiteSpace == ManagedCssWhiteSpace.PreWrap;
            bool noWrap = style.WhiteSpace == ManagedCssWhiteSpace.NoWrap ||
                          style.WhiteSpace == ManagedCssWhiteSpace.Pre;
            bool collapsible = IsAsciiWhitespace(scalar) && !pre;
            if (collapsible)
            {
                int begin = offset++;
                while (offset < length && IsAsciiWhitespace(_document.Text[textOffset + offset])) ++offset;
                if (scalar == '\n' && style.WhiteSpace == ManagedCssWhiteSpace.PreLine)
                {
                    if (!ForceBreak(ownerBoxIndex, contentX, ref currentX, ref lineY, ref lineHeight,
                                    ref lineIndex, ref lineFirstFragment, ref lineFragmentCount,
                                    ref lineHasContent)) return false;
                    ++_forcedBreakCount;
                    continue;
                }
                if (lineHasContent)
                {
                    if (!TryMeasureSpace(textStyle, out int spaceWidth)) return false;
                    if (!AddTextRun(boxIndex, ownerBoxIndex, begin, offset - begin, spaceWidth,
                                    textStyle, contentX, contentWidth, ref currentX, ref lineY,
                                    ref lineHeight, ref lineIndex, ref lineFirstFragment,
                                    ref lineFragmentCount, ref lineHasContent, noWrap)) return false;
                }
                continue;
            }
            if (scalar == '\r' && pre)
            {
                ++offset;
                if (offset < length && _document.Text[textOffset + offset] == '\n') ++offset;
                if (!ForceBreak(ownerBoxIndex, contentX, ref currentX, ref lineY, ref lineHeight,
                                ref lineIndex, ref lineFirstFragment, ref lineFragmentCount,
                                ref lineHasContent)) return false;
                ++_forcedBreakCount;
                continue;
            }
            if (scalar == '\n' && pre)
            {
                ++offset;
                if (!ForceBreak(ownerBoxIndex, contentX, ref currentX, ref lineY, ref lineHeight,
                                ref lineIndex, ref lineFirstFragment, ref lineFragmentCount,
                                ref lineHasContent)) return false;
                ++_forcedBreakCount;
                continue;
            }
            int beginWord = offset;
            while (offset < length)
            {
                uint value = _document.Text[textOffset + offset];
                if ((!pre && IsAsciiWhitespace(value)) || (pre && value == '\n')) break;
                ++offset;
            }
            int wordLength = offset - beginWord;
            if (wordLength == 0) { ++offset; continue; }
            if (!AddWordRun(boxIndex, ownerBoxIndex, beginWord, wordLength, textStyle, contentX,
                            contentWidth, ref currentX, ref lineY, ref lineHeight, ref lineIndex,
                            ref lineFirstFragment, ref lineFragmentCount, ref lineHasContent,
                            noWrap || style.WhiteSpace == ManagedCssWhiteSpace.NoWrap || style.WhiteSpace == ManagedCssWhiteSpace.Pre)) return false;
        }
        return true;
    }

    private bool AddWordRun(int boxIndex, int ownerBoxIndex, int offset, int length,
                            ManagedLayoutTextStyle style, int contentX, int contentWidth,
                            ref int currentX, ref int lineY, ref int lineHeight, ref int lineIndex,
                            ref int lineFirstFragment, ref int lineFragmentCount, ref bool lineHasContent,
                            bool noWrap)
    {
        long total = 0;
        int sourceOffset = _document.Nodes[_boxes[boxIndex].SourceNodeIndex].TextOffset + offset;
        for (int index = 0; index != length; ++index)
        {
            if (!_metrics.TryMeasureScalar(_document.Text[sourceOffset + index], in style, out int advance))
                return Fail(ManagedLayoutFailureReason.TextMeasurementFailure);
            ++_textScalarsMeasured;
            total += advance;
            if (total > ManagedLayoutLimits.MaximumCoordinate) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        }
        int width = (int)total;
        if (!noWrap && lineHasContent && currentX - contentX + width > contentWidth)
        {
            if (!ForceBreak(ownerBoxIndex, contentX, ref currentX, ref lineY, ref lineHeight,
                            ref lineIndex, ref lineFirstFragment, ref lineFragmentCount,
                            ref lineHasContent)) return false;
            ++_softWrapCount;
        }
        if (!noWrap && width > contentWidth && contentWidth > 0)
        {
            int consumed = 0;
            while (consumed < length)
            {
                int part = 0;
                int partWidth = 0;
                while (consumed + part < length)
                {
                    if (!_metrics.TryMeasureScalar(_document.Text[sourceOffset + consumed + part], in style, out int advance))
                        return Fail(ManagedLayoutFailureReason.TextMeasurementFailure);
                    ++_textScalarsMeasured;
                    if (part != 0 && partWidth + advance > contentWidth) break;
                    partWidth += advance;
                    ++part;
                }
                if (!AddTextRun(boxIndex, ownerBoxIndex, offset + consumed, part, 0, style,
                                contentX, contentWidth, ref currentX, ref lineY, ref lineHeight,
                                ref lineIndex, ref lineFirstFragment, ref lineFragmentCount,
                                ref lineHasContent, true)) return false;
                consumed += part;
                if (consumed < length)
                {
                    if (!ForceBreak(ownerBoxIndex, contentX, ref currentX, ref lineY, ref lineHeight,
                                    ref lineIndex, ref lineFirstFragment, ref lineFragmentCount,
                                    ref lineHasContent)) return false;
                    ++_softWrapCount;
                }
            }
            return true;
        }
        return AddTextRun(boxIndex, ownerBoxIndex, offset, length, width, style, contentX,
                          contentWidth, ref currentX, ref lineY, ref lineHeight, ref lineIndex,
                          ref lineFirstFragment, ref lineFragmentCount, ref lineHasContent, noWrap);
    }

    private bool AddTextRun(int boxIndex, int ownerBoxIndex, int offset, int length, int forcedWidth,
                            ManagedLayoutTextStyle style, int contentX, int contentWidth,
                            ref int currentX, ref int lineY, ref int lineHeight, ref int lineIndex,
                            ref int lineFirstFragment, ref int lineFragmentCount, ref bool lineHasContent,
                            bool noWrap)
    {
        int width = forcedWidth;
        if (width == 0 && length != 0)
        {
            int sourceOffset = _document.Nodes[_boxes[boxIndex].SourceNodeIndex].TextOffset + offset;
            for (int index = 0; index != length; ++index)
            {
                if (!_metrics.TryMeasureScalar(_document.Text[sourceOffset + index], in style, out int advance))
                    return Fail(ManagedLayoutFailureReason.TextMeasurementFailure);
                ++_textScalarsMeasured;
                width = AddChecked(width, advance, out bool ok);
                if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
            }
        }
        if (!noWrap && lineHasContent && contentWidth > 0 && currentX - contentX + width > contentWidth)
        {
            if (!ForceBreak(ownerBoxIndex, contentX, ref currentX, ref lineY, ref lineHeight,
                            ref lineIndex, ref lineFirstFragment, ref lineFragmentCount,
                            ref lineHasContent)) return false;
            ++_softWrapCount;
        }
        if (!EnsureLine(ownerBoxIndex, contentX, lineY, ref lineIndex, ref lineFirstFragment)) return false;
        int lineHeightValue = _metrics.GetLineHeight(in style);
        if (lineHeightValue <= 0) return Fail(ManagedLayoutFailureReason.TextMeasurementFailure);
        lineHeight = Math.Max(lineHeight, lineHeightValue);
        ManagedLayoutTextFragment fragment = new(ManagedLayoutTextFragmentKind.Text,
            _boxes[boxIndex].SourceNodeIndex, offset, length, boxIndex, lineIndex,
            new ManagedLayoutRect(currentX, lineY, width, lineHeightValue), style);
        if (!AddFragment(fragment)) return false;
        ++lineFragmentCount;
        lineHasContent = true;
        currentX = AddChecked(currentX, width, out bool ok2);
        if (!ok2) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        ManagedLayoutBoxRecord textBox = _boxes[boxIndex];
        textBox.BorderBox = Union(textBox.BorderBox, fragment.Rectangle);
        textBox.PaddingBox = textBox.BorderBox;
        textBox.ContentRect = textBox.BorderBox;
        _boxes[boxIndex] = textBox;
        return true;
    }

    private bool LayoutReplacedInline(int boxIndex, int ownerBoxIndex, int contentX, int contentWidth,
                                      ref int currentX, ref int lineY, ref int lineHeight, ref int lineIndex,
                                      ref int lineFirstFragment, ref int lineFragmentCount, ref bool lineHasContent)
    {
        ManagedComputedStyle style = StyleForBox(boxIndex);
        int width = ResolveDimension(style.Width, contentWidth, 0, 32, out bool ok);
        if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        int height = ResolveDimension(style.Height, -1, 0, 24, out ok);
        if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        if (style.Width.IsAuto) width = 32;
        if (style.Height.IsAuto) height = 24;
        if (!EnsureLine(ownerBoxIndex, contentX, lineY, ref lineIndex, ref lineFirstFragment)) return false;
        lineHeight = Math.Max(lineHeight, height);
        ManagedLayoutTextFragment fragment = new(ManagedLayoutTextFragmentKind.Replaced,
            _boxes[boxIndex].SourceNodeIndex, 0, 0, boxIndex, lineIndex,
            new ManagedLayoutRect(currentX, lineY, width, height),
            TextStyle(style, NodeHandle(_boxes[boxIndex].SourceNodeIndex)));
        if (!AddFragment(fragment)) return false;
        ++lineFragmentCount;
        lineHasContent = true;
        currentX = AddChecked(currentX, width, out ok);
        if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        ManagedLayoutBoxRecord box = _boxes[boxIndex];
        box.BorderBox = fragment.Rectangle;
        box.PaddingBox = fragment.Rectangle;
        box.ContentRect = fragment.Rectangle;
        _boxes[boxIndex] = box;
        return true;
    }

    private bool ForceBreak(int ownerBoxIndex, int contentX, ref int currentX, ref int lineY,
                            ref int lineHeight, ref int lineIndex, ref int lineFirstFragment,
                            ref int lineFragmentCount, ref bool lineHasContent)
    {
        if (!EnsureLine(ownerBoxIndex, contentX, lineY, ref lineIndex, ref lineFirstFragment)) return false;
        if (!CloseLine(ownerBoxIndex, lineIndex, lineY, contentX, currentX - contentX,
                       lineHeight, lineFirstFragment, lineFragmentCount)) return false;
        currentX = contentX;
        lineY = AddChecked(lineY, lineHeight == 0 ? 1 : lineHeight, out bool ok);
        if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        lineHeight = 0;
        lineIndex = -1;
        lineFirstFragment = _fragmentCount;
        lineFragmentCount = 0;
        lineHasContent = false;
        return true;
    }

    private bool EnsureLine(int ownerBoxIndex, int x, int y, ref int lineIndex, ref int firstFragment)
    {
        if (lineIndex >= 0) return true;
        if (_lineCount == _lines.Length) return Fail(ManagedLayoutFailureReason.LineCapacityExceeded);
        lineIndex = _lineCount++;
        if (_lineCount > _lines.Length) return Fail(ManagedLayoutFailureReason.LineCapacityExceeded);
        firstFragment = _fragmentCount;
        _lines[lineIndex] = new ManagedLayoutLine(ownerBoxIndex, lineIndex,
            new ManagedLayoutRect(x, y, 0, 0), firstFragment, 0);
        return true;
    }

    private bool CloseLine(int ownerBoxIndex, int lineIndex, int y, int x, int width, int height,
                           int firstFragment, int fragmentCount)
    {
        if (lineIndex < 0) return true;
        _lines[lineIndex] = new ManagedLayoutLine(ownerBoxIndex, lineIndex,
            new ManagedLayoutRect(x, y, Math.Max(0, width), Math.Max(1, height)),
            firstFragment, fragmentCount);
        return true;
    }

    private bool AddFragment(ManagedLayoutTextFragment fragment)
    {
        if (_fragmentCount == _fragments.Length)
            return Fail(ManagedLayoutFailureReason.TextFragmentCapacityExceeded);
        _fragments[_fragmentCount++] = fragment;
        return true;
    }

    private bool TryMeasureSpace(ManagedLayoutTextStyle style, out int width)
    {
        if (!_metrics.TryMeasureScalar(0x20, in style, out width))
            return Fail(ManagedLayoutFailureReason.TextMeasurementFailure);
        ++_textScalarsMeasured;
        return true;
    }

    private bool LayoutPositionedBoxes()
    {
        for (int index = 0; index != _boxCount; ++index)
        {
            ManagedLayoutBoxRecord box = _boxes[index];
            if ((box.Flags & (ManagedLayoutBoxFlags.Absolute | ManagedLayoutBoxFlags.Fixed)) == 0) continue;
            ManagedComputedStyle style = StyleForBox(index);
            int containingBox = style.Position == ManagedCssPosition.Fixed ? -1 : FindPositionedAncestor(box.ParentIndex);
            int cbX = 0, cbY = 0, cbWidth = _viewportWidth, cbHeight = _viewportHeight;
            if (containingBox >= 0)
            {
                ManagedLayoutRect cb = _boxes[containingBox].PaddingBox;
                cbX = cb.X; cbY = cb.Y; cbWidth = cb.Width; cbHeight = cb.Height;
            }
            int width = ResolveDimension(style.Width, cbWidth, 0, Math.Max(0, cbWidth), out bool ok);
            if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
            if (style.Width.IsAuto)
            {
                long autoWidth = (long)cbWidth - ResolveOffset(style.Left, 0) - ResolveOffset(style.Right, 0);
                if (autoWidth < 0) autoWidth = 0;
                if (autoWidth > ManagedLayoutLimits.MaximumCoordinate)
                    return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                width = (int)autoWidth;
            }
            int height = ResolveDimension(style.Height, cbHeight, 0, 0, out ok);
            if (!ok) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
            if (style.Height.IsAuto) height = 0;
            if (!PrepareBlock(index, cbX, cbY, cbWidth, cbHeight, true, out _)) return false;
            box = _boxes[index];
            box.BorderBox = new ManagedLayoutRect(box.BorderBox.X, box.BorderBox.Y, width, box.BorderBox.Height);
            if (!style.Left.IsAuto)
            {
                int x = AddChecked(cbX, ResolveOffset(style.Left, 0), out bool xOk);
                if (!xOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                box.BorderBox = new ManagedLayoutRect(x, box.BorderBox.Y, width, box.BorderBox.Height);
            }
            else if (!style.Right.IsAuto)
            {
                long x = (long)cbX + cbWidth - ResolveOffset(style.Right, 0) - width;
                if (x < -ManagedLayoutLimits.MaximumCoordinate || x > ManagedLayoutLimits.MaximumCoordinate)
                    return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                box.BorderBox = new ManagedLayoutRect((int)x, box.BorderBox.Y, width, box.BorderBox.Height);
            }
            if (!style.Top.IsAuto)
            {
                int y = AddChecked(cbY, ResolveOffset(style.Top, 0), out bool yOk);
                if (!yOk) return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                box.BorderBox = new ManagedLayoutRect(box.BorderBox.X, y, width, box.BorderBox.Height);
            }
            else if (!style.Bottom.IsAuto)
            {
                long y = (long)cbY + cbHeight - ResolveOffset(style.Bottom, 0) - box.BorderBox.Height;
                if (y < -ManagedLayoutLimits.MaximumCoordinate || y > ManagedLayoutLimits.MaximumCoordinate)
                    return Fail(ManagedLayoutFailureReason.GeometryOverflow);
                box.BorderBox = new ManagedLayoutRect(box.BorderBox.X, (int)y, width, box.BorderBox.Height);
            }
            _boxes[index] = box;
            if (!RunBlockLayout(index, box.BorderBox.X, box.BorderBox.Y, width, height, true)) return false;
        }
        return true;
    }

    private bool FinalizeOverflowAndExtents()
    {
        long maxRight = _viewportWidth;
        long maxBottom = _viewportHeight;
        for (int index = _boxCount - 1; index >= 0; --index)
        {
            ManagedLayoutBoxRecord box = _boxes[index];
            long right = (long)box.ContentRect.X + box.ContentRect.Width;
            long bottom = (long)box.ContentRect.Y + box.ContentRect.Height;
            long extentRight = right;
            long extentBottom = bottom;
            for (int child = box.FirstChildIndex; child >= 0; child = _boxes[child].NextSiblingIndex)
            {
                ManagedLayoutBoxRecord childBox = _boxes[child];
                extentRight = Math.Max(extentRight, (long)childBox.OverflowExtent.X + childBox.OverflowExtent.Width);
                extentBottom = Math.Max(extentBottom, (long)childBox.OverflowExtent.Y + childBox.OverflowExtent.Height);
            }
            if (extentRight > ManagedLayoutLimits.MaximumCoordinate || extentBottom > ManagedLayoutLimits.MaximumCoordinate)
                return Fail(ManagedLayoutFailureReason.GeometryOverflow);
            box.OverflowExtent = new ManagedLayoutRect(box.ContentRect.X, box.ContentRect.Y,
                Math.Max(0, (int)(extentRight - box.ContentRect.X)),
                Math.Max(0, (int)(extentBottom - box.ContentRect.Y)));
            ManagedComputedStyle style = StyleForBox(index);
            bool horizontal = extentRight > (long)box.ContentRect.X + box.ContentRect.Width;
            bool vertical = extentBottom > (long)box.ContentRect.Y + box.ContentRect.Height;
            if (horizontal) { box.Flags |= ManagedLayoutBoxFlags.HasHorizontalOverflow; ++_horizontalOverflowCount; }
            if (vertical) { box.Flags |= ManagedLayoutBoxFlags.HasVerticalOverflow; ++_verticalOverflowCount; }
            if (EffectiveOverflowX(style) == ManagedCssOverflow.Visible && EffectiveOverflowY(style) == ManagedCssOverflow.Visible)
                box.ClipRect = new ManagedLayoutRect(int.MinValue / 2, int.MinValue / 2,
                    ManagedLayoutLimits.MaximumCoordinate, ManagedLayoutLimits.MaximumCoordinate);
            else box.ClipRect = box.PaddingBox;
            _boxes[index] = box;
            maxRight = Math.Max(maxRight, extentRight + box.Margin.Right);
            maxBottom = Math.Max(maxBottom, extentBottom + box.Margin.Bottom);
        }
        if (maxRight > ManagedLayoutLimits.MaximumCoordinate || maxBottom > ManagedLayoutLimits.MaximumCoordinate)
            return Fail(ManagedLayoutFailureReason.GeometryOverflow);
        _documentContentWidth = (int)maxRight;
        _documentContentHeight = (int)maxBottom;
        return true;
    }

    private bool ComputeLayoutHash()
    {
        _hash.Reset();
        if (!_hash.Append("GXOS-P45\0"u8)) return Fail(ManagedLayoutFailureReason.InvalidState);
        Span<byte> scratch = stackalloc byte[8];
        AppendUInt32((uint)_viewportWidth, scratch); AppendUInt32((uint)_viewportHeight, scratch);
        AppendUInt32((uint)_documentContentWidth, scratch); AppendUInt32((uint)_documentContentHeight, scratch);
        for (int index = 0; index != _boxCount; ++index)
        {
            ManagedLayoutBoxRecord box = _boxes[index];
            AppendUInt32((uint)box.SourceNodeIndex, scratch); AppendUInt32((uint)box.Kind, scratch);
            AppendUInt32((uint)box.ParentIndex, scratch); AppendUInt32((uint)box.Flags, scratch);
            AppendRect(box.BorderBox, scratch); AppendRect(box.PaddingBox, scratch);
            AppendRect(box.ContentRect, scratch); AppendEdges(box.Margin, scratch);
            AppendEdges(box.Padding, scratch); AppendEdges(box.Border, scratch);
            AppendRect(box.OverflowExtent, scratch); AppendRect(box.ClipRect, scratch);
            AppendUInt32((uint)box.ZIndex, scratch);
        }
        for (int index = 0; index != _lineCount; ++index)
        {
            ManagedLayoutLine line = _lines[index];
            AppendUInt32((uint)line.OwnerBoxIndex, scratch); AppendUInt32((uint)line.LineIndex, scratch);
            AppendRect(line.Rectangle, scratch); AppendUInt32((uint)line.FirstFragmentIndex, scratch);
            AppendUInt32((uint)line.FragmentCount, scratch);
        }
        for (int index = 0; index != _fragmentCount; ++index)
        {
            ManagedLayoutTextFragment fragment = _fragments[index];
            AppendUInt32((uint)fragment.Kind, scratch); AppendUInt32((uint)fragment.SourceNodeIndex, scratch);
            AppendUInt32((uint)fragment.SourceOffset, scratch); AppendUInt32((uint)fragment.SourceLength, scratch);
            AppendUInt32((uint)fragment.OwnerBoxIndex, scratch); AppendUInt32((uint)fragment.LineIndex, scratch);
            AppendRect(fragment.Rectangle, scratch); AppendUInt32((uint)fragment.Style.FontSize, scratch);
            AppendUInt32((uint)fragment.Style.FontWeight, scratch); AppendUInt32((uint)fragment.Style.FontStyle, scratch);
        }
        return _hash.TryFinalize(_layoutHash) && (_hashAvailable = true);
    }

    private void AppendUInt32(uint value, Span<byte> scratch)
    {
        scratch[0] = (byte)(value >> 24); scratch[1] = (byte)(value >> 16);
        scratch[2] = (byte)(value >> 8); scratch[3] = (byte)value;
        _hash.Append(scratch[..4]);
    }

    private void AppendRect(ManagedLayoutRect rect, Span<byte> scratch)
    {
        AppendUInt32((uint)rect.X, scratch); AppendUInt32((uint)rect.Y, scratch);
        AppendUInt32((uint)rect.Width, scratch); AppendUInt32((uint)rect.Height, scratch);
    }

    private void AppendEdges(ManagedLayoutEdges edges, Span<byte> scratch)
    {
        AppendUInt32((uint)edges.Top, scratch); AppendUInt32((uint)edges.Right, scratch);
        AppendUInt32((uint)edges.Bottom, scratch); AppendUInt32((uint)edges.Left, scratch);
    }

    private bool ResolveEdges(ManagedComputedStyle style, int containingWidth,
                              out ManagedLayoutEdges margin, out ManagedLayoutEdges padding,
                              out ManagedLayoutEdges border)
    {
        margin = new(ResolveOffset(style.MarginTop, 0), ResolveOffset(style.MarginRight, 0),
                     ResolveOffset(style.MarginBottom, 0), ResolveOffset(style.MarginLeft, 0));
        padding = new(ResolveNonNegative(style.PaddingTop, containingWidth),
                      ResolveNonNegative(style.PaddingRight, containingWidth),
                      ResolveNonNegative(style.PaddingBottom, containingWidth),
                      ResolveNonNegative(style.PaddingLeft, containingWidth));
        int borderWidth = ResolveNonNegative(style.BorderWidth, containingWidth);
        border = new(borderWidth, borderWidth, borderWidth, borderWidth);
        return true;
    }

    private int ResolveNonNegative(ManagedCssLength value, int containingWidth)
    {
        int result = ResolveOffset(value, containingWidth);
        return Math.Max(0, result);
    }

    private int ResolveOffset(ManagedCssLength value, int containingWidth)
    {
        if (value.IsAuto) return 0;
        long result = value.Unit switch
        {
            ManagedCssLengthUnit.Px => value.Value / 100,
            ManagedCssLengthUnit.Percent => (long)containingWidth * value.Value / 10000,
            ManagedCssLengthUnit.Em => (long)1600 * value.Value / 100000,
            ManagedCssLengthUnit.Rem => (long)1600 * value.Value / 100000,
            _ => 0
        };
        return result > ManagedLayoutLimits.MaximumCoordinate ? ManagedLayoutLimits.MaximumCoordinate :
            result < -ManagedLayoutLimits.MaximumCoordinate ? -ManagedLayoutLimits.MaximumCoordinate : (int)result;
    }

    private int ResolveDimension(ManagedCssLength value, int containing, int parentFont,
                                 int fallback, out bool ok)
    {
        ok = true;
        if (value.IsAuto) return fallback;
        if (value.Unit == ManagedCssLengthUnit.Percent && containing < 0) return fallback;
        long result = value.Unit switch
        {
            ManagedCssLengthUnit.Px => value.Value / 100,
            ManagedCssLengthUnit.Percent => (long)containing * value.Value / 10000,
            ManagedCssLengthUnit.Em => (long)(parentFont <= 0 ? 1600 : parentFont) * value.Value / 100000,
            ManagedCssLengthUnit.Rem => (long)1600 * value.Value / 100000,
            _ => fallback
        };
        if (result < 0) result = 0;
        if (result > ManagedLayoutLimits.MaximumCoordinate) { ok = false; return 0; }
        return (int)result;
    }

    private int ClampDimension(int value, ManagedCssLength min, ManagedCssLength max,
                               int containingWidth, int parentFont, out bool ok)
    {
        ok = true;
        int lower = ResolveDimension(min, containingWidth, parentFont, 0, out ok);
        if (!ok) return 0;
        int upper = ResolveDimension(max, containingWidth, parentFont, ManagedLayoutLimits.MaximumCoordinate, out ok);
        if (!ok) return 0;
        if (upper < 0) upper = ManagedLayoutLimits.MaximumCoordinate;
        return Math.Min(upper, Math.Max(lower, value));
    }

    private ManagedComputedStyle StyleForBox(int boxIndex)
    {
        ManagedHtmlNodeHandle node = NodeHandle(_boxes[boxIndex].SourceNodeIndex);
        if (_styles.TryGetComputedStyle(node, out ManagedComputedStyle style)) return style;
        return default;
    }

    private ManagedLayoutTextStyle TextStyle(ManagedComputedStyle style, ManagedHtmlNodeHandle parent)
    {
        int parentFont = 1600;
        if (parent != ManagedHtmlNodeHandle.Invalid && _styles.TryGetComputedStyle(parent, out ManagedComputedStyle parentStyle))
            parentFont = ResolveFontSize(parentStyle.FontSize, parentFont);
        return new(ResolveFontSize(style.FontSize, parentFont), style.FontWeight, style.FontStyle);
    }

    private int ResolveFontSize(ManagedCssLength value, int parentFont)
    {
        if (value.Unit == ManagedCssLengthUnit.Px) return Math.Max(1, value.Value / 100);
        if (value.Unit == ManagedCssLengthUnit.Em) return Math.Max(1, (int)((long)parentFont * value.Value / 100000));
        if (value.Unit == ManagedCssLengthUnit.Rem) return Math.Max(1, (int)((long)1600 * value.Value / 100000));
        if (value.Unit == ManagedCssLengthUnit.Percent) return Math.Max(1, (int)((long)parentFont * value.Value / 10000));
        return Math.Max(1, parentFont);
    }

    private int FindPositionedAncestor(int parent)
    {
        int current = parent;
        while (current >= 0)
        {
            ManagedComputedStyle style = StyleForBox(current);
            if (style.Position != ManagedCssPosition.Static) return current;
            current = _boxes[current].ParentIndex;
        }
        return -1;
    }

    private static ManagedCssOverflow EffectiveOverflowX(ManagedComputedStyle style) =>
        style.OverflowX == ManagedCssOverflow.Visible ? style.Overflow : style.OverflowX;

    private static ManagedCssOverflow EffectiveOverflowY(ManagedComputedStyle style) =>
        style.OverflowY == ManagedCssOverflow.Visible ? style.Overflow : style.OverflowY;

    private bool IsNonRendered(ManagedHtmlNodeHandle node)
    {
        ManagedHtmlTag tag = _document.GetElementTag(node);
        return tag == ManagedHtmlTag.Head || tag == ManagedHtmlTag.Style || tag == ManagedHtmlTag.Script ||
               tag == ManagedHtmlTag.Meta || tag == ManagedHtmlTag.Link || tag == ManagedHtmlTag.Title ||
               tag == ManagedHtmlTag.Base;
    }

    private ManagedLayoutBoxKind BoxKind(ManagedHtmlNodeHandle node, ManagedComputedStyle style)
    {
        ManagedHtmlTag tag = _document.GetElementTag(node);
        if (tag == ManagedHtmlTag.Br) return ManagedLayoutBoxKind.LineBreak;
        if (tag == ManagedHtmlTag.Img || tag == ManagedHtmlTag.Input || tag == ManagedHtmlTag.Button ||
            tag == ManagedHtmlTag.Textarea || tag == ManagedHtmlTag.Select || tag == ManagedHtmlTag.Embed)
            return ManagedLayoutBoxKind.Replaced;
        if (tag == ManagedHtmlTag.Pre && style.Display == ManagedCssDisplay.Inline &&
            (style.SpecifiedProperties & (1UL << (int)ManagedCssProperty.Display)) == 0)
            return ManagedLayoutBoxKind.Block;
        return style.Display switch
        {
            ManagedCssDisplay.Inline or ManagedCssDisplay.InlineBlock => ManagedLayoutBoxKind.InlineContainer,
            ManagedCssDisplay.Table => ManagedLayoutBoxKind.Table,
            ManagedCssDisplay.TableRow => ManagedLayoutBoxKind.TableRow,
            ManagedCssDisplay.TableCell => ManagedLayoutBoxKind.TableCell,
            _ => ManagedLayoutBoxKind.Block
        };
    }

    private static ManagedLayoutBoxFlags PositionFlags(ManagedCssPosition position) => position switch
    {
        ManagedCssPosition.Relative => ManagedLayoutBoxFlags.Relative,
        ManagedCssPosition.Absolute => ManagedLayoutBoxFlags.Absolute,
        ManagedCssPosition.Fixed => ManagedLayoutBoxFlags.Fixed,
        _ => ManagedLayoutBoxFlags.None
    };

    private ManagedHtmlNodeHandle NodeHandle(int index) =>
        index < 0 || index >= _document.NodeCount ? ManagedHtmlNodeHandle.Invalid :
        new ManagedHtmlNodeHandle(index, _document.DocumentNode.Generation);

    private static int IndexOf(ManagedHtmlNodeHandle handle) =>
        handle == ManagedHtmlNodeHandle.Invalid ? -1 : handle.Index;

    private bool ChildContains(int parent, int expected)
    {
        int child = _boxes[parent].FirstChildIndex;
        int guard = 0;
        while (child >= 0 && ++guard <= _boxCount)
        {
            if (child == expected) return true;
            child = _boxes[child].NextSiblingIndex;
        }
        return false;
    }

    private static bool ValidRect(ManagedLayoutRect rect) => rect.Width >= 0 && rect.Height >= 0;
    private static bool IsInFlow(ManagedLayoutBoxRecord box) =>
        (box.Flags & (ManagedLayoutBoxFlags.Absolute | ManagedLayoutBoxFlags.Fixed)) == 0;
    private static bool IsAsciiWhitespace(uint scalar) => scalar == 0x20 || scalar == 0x09 ||
        scalar == 0x0A || scalar == 0x0C || scalar == 0x0D;

    private static ManagedLayoutRect Union(ManagedLayoutRect left, ManagedLayoutRect right)
    {
        if (left.Width == 0 && left.Height == 0) return right;
        int x = Math.Min(left.X, right.X);
        int y = Math.Min(left.Y, right.Y);
        int r = Math.Max(left.Right, right.Right);
        int b = Math.Max(left.Bottom, right.Bottom);
        return new ManagedLayoutRect(x, y, r - x, b - y);
    }

    private static int AddChecked(int left, int right, out bool ok)
    {
        long result = (long)left + right;
        ok = result >= -ManagedLayoutLimits.MaximumCoordinate && result <= ManagedLayoutLimits.MaximumCoordinate;
        return ok ? (int)result : 0;
    }

    private static int AddUncheckedChecked(int value, int delta, ref ManagedLayoutFailureReason failure)
    {
        long result = (long)value + delta;
        if (result < -ManagedLayoutLimits.MaximumCoordinate || result > ManagedLayoutLimits.MaximumCoordinate)
        {
            failure = ManagedLayoutFailureReason.GeometryOverflow;
            return value;
        }
        return (int)result;
    }

    private bool Fail(ManagedLayoutFailureReason reason)
    {
        if (_failureReason == ManagedLayoutFailureReason.None) _failureReason = reason;
        return false;
    }

    private static bool FailValidation(ManagedLayoutValidationFailureReason value,
                                       out ManagedLayoutValidationFailureReason reason)
    {
        reason = value;
        return false;
    }
}

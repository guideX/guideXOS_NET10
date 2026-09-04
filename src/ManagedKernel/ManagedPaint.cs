using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedPaintCommandKind : byte
{
    BeginClip = 0,
    EndClip = 1,
    FillRectangle = 2,
    BorderRectangle = 3,
    TextRun = 4,
    ImagePlaceholder = 5
}

[Flags]
public enum ManagedPaintCommandFlags : byte
{
    None = 0,
    Positioned = 1
}

public enum ManagedPaintFontId : byte
{
    DefaultUi = 0
}

public enum ManagedPaintState : byte
{
    Reset = 0,
    Generated = 1,
    Failed = 2,
    Cancelled = 3
}

public enum ManagedPaintFailureReason : byte
{
    None = 0,
    InvalidDocument = 1,
    InvalidComputedStyle = 2,
    InvalidLayout = 3,
    PaintCommandCapacityExceeded = 4,
    PaintClipDepthExceeded = 5,
    InvalidTextReference = 6,
    GeometryOverflow = 7,
    PaintOrderingCapacityExceeded = 8,
    Cancelled = 9,
    InvalidViewport = 10,
    InvalidState = 11
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedPaintViewport
{
    public ManagedPaintViewport(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedPaintArenaOptions
{
    public ManagedPaintArenaOptions(int commandCapacity, int clipDepthCapacity,
                                    int orderingCapacity)
    {
        if (commandCapacity <= 0 || commandCapacity > ManagedPaintLimits.MaximumCommandCapacity)
            throw new ArgumentOutOfRangeException(nameof(commandCapacity));
        if (clipDepthCapacity <= 0 || clipDepthCapacity > ManagedPaintLimits.MaximumClipDepthCapacity)
            throw new ArgumentOutOfRangeException(nameof(clipDepthCapacity));
        if (orderingCapacity <= 0 || orderingCapacity > ManagedPaintLimits.MaximumOrderingCapacity)
            throw new ArgumentOutOfRangeException(nameof(orderingCapacity));
        CommandCapacity = commandCapacity;
        ClipDepthCapacity = clipDepthCapacity;
        OrderingCapacity = orderingCapacity;
    }

    public static ManagedPaintArenaOptions Default => new(
        ManagedPaintLimits.DefaultCommandCapacity,
        ManagedPaintLimits.DefaultClipDepthCapacity,
        ManagedPaintLimits.DefaultOrderingCapacity);

    public int CommandCapacity { get; }
    public int ClipDepthCapacity { get; }
    public int OrderingCapacity { get; }
}

public static class ManagedPaintLimits
{
    /* 2 root clip commands + 2 commands per layout box for a background and
       border + 4,096 bounded fragments + 2 clip commands per box gives a
       conservative default upper bound of 8,194.  12,288 leaves room for
       repeated clip transitions from z-order buckets and richer pages while
       keeping the default arena close to one megabyte. */
    public const int DefaultCommandCapacity = 12_288;
    public const int DefaultClipDepthCapacity = 64;
    public const int DefaultOrderingCapacity = ManagedLayoutLimits.MaximumBoxCapacity;
    public const int MaximumCommandCapacity = 32_768;
    public const int MaximumClipDepthCapacity = 256;
    public const int MaximumOrderingCapacity = ManagedLayoutLimits.MaximumBoxCapacity;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedPaintCommand
{
    public ManagedPaintCommand(ManagedPaintCommandKind kind, byte clipDepth,
                               ManagedPaintCommandFlags flags, int sourceBoxIndex,
                               int sourceNodeIndex, int sourceOffset, int sourceLength,
                               int lineIndex, int baselineY, ManagedLayoutRect rect,
                               ManagedLayoutRect clipRect, uint color,
                               ManagedLayoutEdges borderWidths,
                               ManagedCssBorderStyle borderStyle,
                               ManagedPaintFontId fontId, int fontSize, int fontWeight,
                               ManagedCssFontStyle fontStyle, int opacity, int zIndex)
    {
        KindValue = kind;
        ClipDepthValue = clipDepth;
        FlagsValue = flags;
        FontIdValue = fontId;
        SourceBoxIndexValue = sourceBoxIndex;
        SourceNodeIndexValue = sourceNodeIndex;
        SourceOffsetValue = sourceOffset;
        SourceLengthValue = sourceLength;
        LineIndexValue = lineIndex;
        BaselineYValue = baselineY;
        RectValue = rect;
        ClipRectValue = clipRect;
        ColorValue = color;
        BorderWidthsValue = borderWidths;
        BorderStyleValue = borderStyle;
        FontSizeValue = fontSize;
        FontWeightValue = fontWeight;
        FontStyleValue = fontStyle;
        OpacityValue = opacity;
        ZIndexValue = zIndex;
    }

    private readonly ManagedPaintCommandKind KindValue;
    private readonly byte ClipDepthValue;
    private readonly ManagedPaintCommandFlags FlagsValue;
    private readonly ManagedPaintFontId FontIdValue;
    private readonly int SourceBoxIndexValue;
    private readonly int SourceNodeIndexValue;
    private readonly int SourceOffsetValue;
    private readonly int SourceLengthValue;
    private readonly int LineIndexValue;
    private readonly int BaselineYValue;
    private readonly ManagedLayoutRect RectValue;
    private readonly ManagedLayoutRect ClipRectValue;
    private readonly uint ColorValue;
    private readonly ManagedLayoutEdges BorderWidthsValue;
    private readonly ManagedCssBorderStyle BorderStyleValue;
    private readonly int FontSizeValue;
    private readonly int FontWeightValue;
    private readonly ManagedCssFontStyle FontStyleValue;
    private readonly int OpacityValue;
    private readonly int ZIndexValue;

    public ManagedPaintCommandKind Kind => KindValue;
    public byte ClipDepth => ClipDepthValue;
    public ManagedPaintCommandFlags Flags => FlagsValue;
    public ManagedPaintFontId FontId => FontIdValue;
    public int SourceBoxIndex => SourceBoxIndexValue;
    public int SourceNodeIndex => SourceNodeIndexValue;
    public int SourceOffset => SourceOffsetValue;
    public int SourceLength => SourceLengthValue;
    public int LineIndex => LineIndexValue;
    public int BaselineY => BaselineYValue;
    public ManagedLayoutRect Rect => RectValue;
    public ManagedLayoutRect ClipRect => ClipRectValue;
    public uint Color => ColorValue;
    public ManagedLayoutEdges BorderWidths => BorderWidthsValue;
    public ManagedCssBorderStyle BorderStyle => BorderStyleValue;
    public int FontSize => FontSizeValue;
    public int FontWeight => FontWeightValue;
    public ManagedCssFontStyle FontStyle => FontStyleValue;
    public int Opacity => OpacityValue;
    public int ZIndex => ZIndexValue;
}

public readonly struct ManagedPaintTelemetry
{
    internal ManagedPaintTelemetry(ManagedPaintEngine engine)
    {
        LayoutBoxesVisited = engine.LayoutBoxesVisited;
        VisibleBoxes = engine.VisibleBoxes;
        HiddenBoxesSkipped = engine.HiddenBoxesSkipped;
        DisplayNoneBoxesSkipped = engine.DisplayNoneBoxesSkipped;
        CommandsEmitted = engine.CommandsEmitted;
        PeakCommandUsage = engine.PeakCommandUsage;
        FillCommands = engine.FillCommands;
        BorderCommands = engine.BorderCommands;
        TextCommands = engine.TextCommands;
        ImagePlaceholderCommands = engine.ImagePlaceholderCommands;
        ClipPushes = engine.ClipPushes;
        ClipPops = engine.ClipPops;
        PeakClipDepth = engine.PeakClipDepth;
        CurrentClipDepth = engine.CurrentClipDepth;
        OffscreenCommandsCulled = engine.OffscreenCommandsCulled;
        TransparentBackgroundsSkipped = engine.TransparentBackgroundsSkipped;
        UnsupportedBorderStyles = engine.UnsupportedBorderStyles;
        PositionedCommands = engine.PositionedCommands;
        NegativeZOrderCount = engine.NegativeZOrderCount;
        NormalZOrderCount = engine.NormalZOrderCount;
        PositiveZOrderCount = engine.PositiveZOrderCount;
        State = engine.State;
        FailureReason = engine.FailureReason;
    }

    public int LayoutBoxesVisited { get; }
    public int VisibleBoxes { get; }
    public int HiddenBoxesSkipped { get; }
    public int DisplayNoneBoxesSkipped { get; }
    public int CommandsEmitted { get; }
    public int PeakCommandUsage { get; }
    public int FillCommands { get; }
    public int BorderCommands { get; }
    public int TextCommands { get; }
    public int ImagePlaceholderCommands { get; }
    public int ClipPushes { get; }
    public int ClipPops { get; }
    public int PeakClipDepth { get; }
    public int CurrentClipDepth { get; }
    public int OffscreenCommandsCulled { get; }
    public int TransparentBackgroundsSkipped { get; }
    public int UnsupportedBorderStyles { get; }
    public int PositionedCommands { get; }
    public int NegativeZOrderCount { get; }
    public int NormalZOrderCount { get; }
    public int PositiveZOrderCount { get; }
    public ManagedPaintState State { get; }
    public ManagedPaintFailureReason FailureReason { get; }
}

public enum ManagedPaintValidationFailureReason : byte
{
    None = 0,
    NotGenerated = 1,
    CommandRangeInvalid = 2,
    InvalidKind = 3,
    InvalidRectangle = 4,
    InvalidClipRectangle = 5,
    InvalidSourceBox = 6,
    InvalidSourceNode = 7,
    InvalidTextReference = 8,
    InvalidImageSource = 9,
    InvalidBorder = 10,
    ClipDepthMismatch = 11,
    ClipNotBalanced = 12,
    OrderingViolation = 13
}

public static class ManagedPaintValidator
{
    public static bool Validate(ReadOnlySpan<ManagedPaintCommand> commands,
                                ManagedHtmlDocument document, ManagedLayoutEngine layout,
                                out ManagedPaintValidationFailureReason reason)
    {
        reason = ManagedPaintValidationFailureReason.None;
        if (document == null || layout == null || !layout.IsLaidOut)
            return Fail(ManagedPaintValidationFailureReason.NotGenerated, out reason);
        int depth = 0;
        int previousBucket = int.MinValue;
        int previousBox = -1;
        for (int index = 0; index != commands.Length; ++index)
        {
            ManagedPaintCommand command = commands[index];
            if ((byte)command.Kind > (byte)ManagedPaintCommandKind.ImagePlaceholder)
                return Fail(ManagedPaintValidationFailureReason.InvalidKind, out reason);
            if (!ValidRect(command.Rect))
                return Fail(ManagedPaintValidationFailureReason.InvalidRectangle, out reason);
            if (!ValidRect(command.ClipRect))
                return Fail(ManagedPaintValidationFailureReason.InvalidClipRectangle, out reason);
            if (command.SourceBoxIndex >= layout.LayoutBoxCount || command.SourceBoxIndex < -1)
                return Fail(ManagedPaintValidationFailureReason.InvalidSourceBox, out reason);
            if (command.SourceNodeIndex >= document.NodeCount || command.SourceNodeIndex < -1)
                return Fail(ManagedPaintValidationFailureReason.InvalidSourceNode, out reason);
            ManagedLayoutBox sourceBox = default;
            if (command.SourceBoxIndex >= 0 && !layout.TryGetBox(command.SourceBoxIndex,
                                                                  out sourceBox))
                return Fail(ManagedPaintValidationFailureReason.InvalidSourceBox, out reason);
            if (command.SourceNodeIndex >= 0 && !document.IsValid(NodeHandle(document, command.SourceNodeIndex)))
                return Fail(ManagedPaintValidationFailureReason.InvalidSourceNode, out reason);
            if (command.Kind == ManagedPaintCommandKind.BeginClip)
            {
                ++depth;
                if (command.ClipDepth != depth)
                    return Fail(ManagedPaintValidationFailureReason.ClipDepthMismatch, out reason);
            }
            else if (command.Kind == ManagedPaintCommandKind.EndClip)
            {
                if (depth == 0 || command.ClipDepth != depth)
                    return Fail(ManagedPaintValidationFailureReason.ClipDepthMismatch, out reason);
                --depth;
            }
            else
            {
                if (command.ClipDepth != depth)
                    return Fail(ManagedPaintValidationFailureReason.ClipDepthMismatch, out reason);
                if (command.Kind == ManagedPaintCommandKind.BorderRectangle &&
                    (command.BorderWidths.Top < 0 || command.BorderWidths.Right < 0 ||
                     command.BorderWidths.Bottom < 0 || command.BorderWidths.Left < 0 ||
                     (byte)command.BorderStyle > (byte)ManagedCssBorderStyle.Dotted))
                    return Fail(ManagedPaintValidationFailureReason.InvalidBorder, out reason);
                if (command.Kind == ManagedPaintCommandKind.TextRun)
                {
                    if (command.SourceBoxIndex < 0 || command.SourceNodeIndex < 0 ||
                        document.GetNodeKind(NodeHandle(document, command.SourceNodeIndex)) != ManagedHtmlNodeKind.Text ||
                        command.SourceOffset < 0 || command.SourceLength < 0 ||
                        command.SourceOffset > document.GetTextLength(
                            NodeHandle(document, command.SourceNodeIndex)) - command.SourceLength)
                        return Fail(ManagedPaintValidationFailureReason.InvalidTextReference, out reason);
                    if (command.LineIndex < 0 || command.FontSize <= 0 || command.FontWeight <= 0)
                        return Fail(ManagedPaintValidationFailureReason.InvalidTextReference, out reason);
                }
                if (command.Kind == ManagedPaintCommandKind.ImagePlaceholder)
                {
                    if (command.SourceBoxIndex < 0 || command.SourceNodeIndex < 0 ||
                        sourceBox.Kind != ManagedLayoutBoxKind.Replaced ||
                        document.GetNodeKind(NodeHandle(document, command.SourceNodeIndex)) != ManagedHtmlNodeKind.Element ||
                        document.GetElementTag(NodeHandle(document, command.SourceNodeIndex)) != ManagedHtmlTag.Img)
                        return Fail(ManagedPaintValidationFailureReason.InvalidImageSource, out reason);
                }
            }
            if (command.Kind == ManagedPaintCommandKind.FillRectangle ||
                command.Kind == ManagedPaintCommandKind.BorderRectangle ||
                command.Kind == ManagedPaintCommandKind.TextRun ||
                command.Kind == ManagedPaintCommandKind.ImagePlaceholder)
            {
                if (command.SourceBoxIndex < 0) return Fail(ManagedPaintValidationFailureReason.InvalidSourceBox, out reason);
                int bucket = command.ZIndex < 0 ? -1 : command.ZIndex > 0 ? 1 : 0;
                if (bucket < previousBucket || (bucket == previousBucket &&
                                                command.SourceBoxIndex < previousBox))
                    return Fail(ManagedPaintValidationFailureReason.OrderingViolation, out reason);
                previousBucket = bucket;
                previousBox = command.SourceBoxIndex;
            }
        }
        return depth == 0 ? true : Fail(ManagedPaintValidationFailureReason.ClipNotBalanced, out reason);
    }

    private static bool ValidRect(ManagedLayoutRect rect)
    {
        if (rect.Width < 0 || rect.Height < 0 ||
            rect.X < -ManagedLayoutLimits.MaximumCoordinate ||
            rect.X > ManagedLayoutLimits.MaximumCoordinate ||
            rect.Y < -ManagedLayoutLimits.MaximumCoordinate ||
            rect.Y > ManagedLayoutLimits.MaximumCoordinate)
            return false;
        long right = (long)rect.X + rect.Width;
        long bottom = (long)rect.Y + rect.Height;
        return right >= -ManagedLayoutLimits.MaximumCoordinate &&
               right <= ManagedLayoutLimits.MaximumCoordinate &&
               bottom >= -ManagedLayoutLimits.MaximumCoordinate &&
               bottom <= ManagedLayoutLimits.MaximumCoordinate;
    }

    private static ManagedHtmlNodeHandle NodeHandle(ManagedHtmlDocument document, int index) =>
        new(index, document.DocumentNode.Generation);

    private static bool Fail(ManagedPaintValidationFailureReason value,
                             out ManagedPaintValidationFailureReason reason)
    {
        reason = value;
        return false;
    }
}

/// <summary>
/// Phase 46 turns Phase 45 geometry into a retained, viewport-oriented command
/// list. It never writes a framebuffer, resolves fonts, fetches images, or owns
/// text. All storage is allocated once by the constructor and all references
/// are integer indices into bounded document/layout arenas.
/// </summary>
public sealed class ManagedPaintEngine
{
    private readonly ManagedLayoutEngine _layout;
    private readonly ManagedHtmlDocument _document;
    private readonly ManagedCssEngine _styles;
    private readonly ManagedPaintCommand[] _commands;
    private readonly ManagedLayoutRect[] _clipStack;
    private readonly int[] _activeClipPath;
    private readonly int[] _clipPathScratch;
    private readonly int[] _order;
    private readonly ManagedSha256 _hash = new();
    private readonly byte[] _paintHash = new byte[ManagedSha256.DigestSize];
    private int _used;
    private int _peak;
    private int _activeClipPathCount;
    private int _clipDepth;
    private int _peakClipDepth;
    private int _viewportWidth;
    private int _viewportHeight;
    private int _scrollX;
    private int _scrollY;
    private int _layoutBoxesVisited;
    private int _visibleBoxes;
    private int _hiddenBoxesSkipped;
    private int _displayNoneBoxesSkipped;
    private int _fillCommands;
    private int _borderCommands;
    private int _textCommands;
    private int _imagePlaceholderCommands;
    private int _clipPushes;
    private int _clipPops;
    private int _offscreenCommandsCulled;
    private int _transparentBackgroundsSkipped;
    private int _unsupportedBorderStyles;
    private int _positionedCommands;
    private int _negativeZOrderCount;
    private int _normalZOrderCount;
    private int _positiveZOrderCount;
    private int _plannedCount;
    private int _cancelAfterCommands = -1;
    private bool _cancelRequested;
    private bool _countOnly;
    private bool _generated;
    private ManagedPaintState _state;
    private ManagedPaintFailureReason _failureReason;
    private bool _hashAvailable;

    public ManagedPaintEngine(ManagedLayoutEngine layout)
        : this(layout, ManagedPaintArenaOptions.Default) { }

    public ManagedPaintEngine(ManagedLayoutEngine layout, ManagedPaintArenaOptions options)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _document = layout.Document;
        _styles = layout.Styles;
        _commands = new ManagedPaintCommand[options.CommandCapacity];
        _clipStack = new ManagedLayoutRect[options.ClipDepthCapacity];
        _activeClipPath = new int[options.ClipDepthCapacity];
        _clipPathScratch = new int[options.ClipDepthCapacity];
        _order = new int[options.OrderingCapacity];
        Reset();
    }

    public ManagedHtmlDocument Document => _document;
    public ManagedCssEngine Styles => _styles;
    public ManagedLayoutEngine Layout => _layout;
    public ManagedPaintState State => _state;
    public ManagedPaintFailureReason FailureReason => _failureReason;
    public bool IsGenerated => _generated;
    public bool CanonicalHashAvailable => _hashAvailable;
    public int CommandCapacity => _commands.Length;
    public int CommandsEmitted => _used;
    public int RemainingCommandCapacity => _commands.Length - _used;
    public int PeakCommandUsage => _peak;
    public int ClipDepthCapacity => _clipStack.Length;
    public int CurrentClipDepth => _clipDepth;
    public int PeakClipDepth => _peakClipDepth;
    public int OrderingCapacity => _order.Length;
    public int LayoutBoxesVisited => _layoutBoxesVisited;
    public int VisibleBoxes => _visibleBoxes;
    public int HiddenBoxesSkipped => _hiddenBoxesSkipped;
    public int DisplayNoneBoxesSkipped => _displayNoneBoxesSkipped;
    public int FillCommands => _fillCommands;
    public int BorderCommands => _borderCommands;
    public int TextCommands => _textCommands;
    public int ImagePlaceholderCommands => _imagePlaceholderCommands;
    public int ClipPushes => _clipPushes;
    public int ClipPops => _clipPops;
    public int OffscreenCommandsCulled => _offscreenCommandsCulled;
    public int TransparentBackgroundsSkipped => _transparentBackgroundsSkipped;
    public int UnsupportedBorderStyles => _unsupportedBorderStyles;
    public int PositionedCommands => _positionedCommands;
    public int NegativeZOrderCount => _negativeZOrderCount;
    public int NormalZOrderCount => _normalZOrderCount;
    public int PositiveZOrderCount => _positiveZOrderCount;
    public ManagedPaintViewport Viewport => new(_viewportWidth, _viewportHeight);
    public int ScrollX => _scrollX;
    public int ScrollY => _scrollY;
    public ManagedPaintTelemetry Telemetry => new(this);

    public void Cancel() => _cancelRequested = true;

    /* Deterministic cooperative test/control hook. A value of zero cancels
       before the first command; one permits the root BeginClip and cancels at
       the next append. No thread or CancellationToken is involved. */
    public void CancelAfterCommands(int commandCount)
    {
        if (commandCount < 0) throw new ArgumentOutOfRangeException(nameof(commandCount));
        _cancelAfterCommands = commandCount;
    }

    public void Reset()
    {
        _commands.AsSpan().Clear();
        _clipStack.AsSpan().Clear();
        _activeClipPath.AsSpan().Clear();
        _clipPathScratch.AsSpan().Clear();
        _order.AsSpan().Clear();
        _used = 0;
        _peak = 0;
        _activeClipPathCount = 0;
        _clipDepth = 0;
        _peakClipDepth = 0;
        _viewportWidth = 0;
        _viewportHeight = 0;
        _scrollX = 0;
        _scrollY = 0;
        _layoutBoxesVisited = 0;
        _visibleBoxes = 0;
        _hiddenBoxesSkipped = 0;
        _displayNoneBoxesSkipped = 0;
        _fillCommands = 0;
        _borderCommands = 0;
        _textCommands = 0;
        _imagePlaceholderCommands = 0;
        _clipPushes = 0;
        _clipPops = 0;
        _offscreenCommandsCulled = 0;
        _transparentBackgroundsSkipped = 0;
        _unsupportedBorderStyles = 0;
        _positionedCommands = 0;
        _negativeZOrderCount = 0;
        _normalZOrderCount = 0;
        _positiveZOrderCount = 0;
        _plannedCount = 0;
        _cancelAfterCommands = -1;
        _cancelRequested = false;
        _countOnly = false;
        _generated = false;
        _state = ManagedPaintState.Reset;
        _failureReason = ManagedPaintFailureReason.None;
        _hashAvailable = false;
        _paintHash.AsSpan().Clear();
        _hash.Reset();
    }

    public bool TryGenerate(int viewportWidth, int viewportHeight,
                            int scrollX = 0, int scrollY = 0) =>
        TryGenerate(new ManagedPaintViewport(viewportWidth, viewportHeight), scrollX, scrollY);

    public bool TryGenerate(ManagedPaintViewport viewport, int scrollX = 0, int scrollY = 0)
    {
        bool cancelled = _cancelRequested;
        int cancelAfter = _cancelAfterCommands;
        Reset();
        _cancelRequested = cancelled;
        _cancelAfterCommands = cancelAfter;
        if (_cancelRequested) return Fail(ManagedPaintFailureReason.Cancelled);
        if (viewport.Width < 0 || viewport.Height < 0 ||
            viewport.Width > ManagedLayoutLimits.MaximumCoordinate ||
            viewport.Height > ManagedLayoutLimits.MaximumCoordinate ||
            scrollX < -ManagedLayoutLimits.MaximumCoordinate ||
            scrollX > ManagedLayoutLimits.MaximumCoordinate ||
            scrollY < -ManagedLayoutLimits.MaximumCoordinate ||
            scrollY > ManagedLayoutLimits.MaximumCoordinate)
            return Fail(ManagedPaintFailureReason.InvalidViewport);
        if (!_layout.IsLaidOut || !_layout.Validate(out ManagedLayoutValidationFailureReason layoutFailure) ||
            layoutFailure != ManagedLayoutValidationFailureReason.None)
            return Fail(ManagedPaintFailureReason.InvalidLayout);
        if (!_document.IsValid(_document.DocumentNode) ||
            !_document.Validate(out ManagedHtmlDocumentValidationFailureReason documentFailure) ||
            documentFailure != ManagedHtmlDocumentValidationFailureReason.None)
            return Fail(ManagedPaintFailureReason.InvalidDocument);
        if (!_styles.IsStyled || _styles.Document != _document)
            return Fail(ManagedPaintFailureReason.InvalidComputedStyle);
        _viewportWidth = viewport.Width;
        _viewportHeight = viewport.Height;
        _scrollX = scrollX;
        _scrollY = scrollY;
        if (!BuildOrder()) return false;
        _countOnly = true;
        if (!RunGenerationPass() || !CloseAllClips()) return false;
        _plannedCount = _used;
        if (_plannedCount > _commands.Length)
        {
            _used = 0;
            _peak = 0;
            _countOnly = false;
            return Fail(ManagedPaintFailureReason.PaintCommandCapacityExceeded);
        }
        ClearPassState();
        _countOnly = false;
        if (!RunGenerationPass()) return false;
        if (!CloseAllClips() || _clipDepth != 0)
            return _failureReason == ManagedPaintFailureReason.None &&
                   Fail(ManagedPaintFailureReason.InvalidState);
        if (!ComputePaintHash()) return false;
        _generated = true;
        _state = ManagedPaintState.Generated;
        return true;
    }

    public bool TryGetCommand(int index, out ManagedPaintCommand command)
    {
        command = default;
        if (index < 0 || index >= _used) return false;
        command = _commands[index];
        return true;
    }

    public bool TryCopyCanonicalPaintHash(Span<byte> destination)
    {
        if (!_hashAvailable || destination.Length < _paintHash.Length) return false;
        _paintHash.AsSpan().CopyTo(destination);
        return true;
    }

    public bool Validate(out ManagedPaintValidationFailureReason reason)
    {
        if (!_generated)
        {
            reason = ManagedPaintValidationFailureReason.NotGenerated;
            return false;
        }
        return ManagedPaintValidator.Validate(_commands.AsSpan(0, _used), _document, _layout, out reason);
    }

    private bool BuildOrder()
    {
        if (_layout.LayoutBoxCount > _order.Length)
            return Fail(ManagedPaintFailureReason.PaintOrderingCapacityExceeded);
        for (int index = 0; index != _layout.LayoutBoxCount; ++index) _order[index] = index;
        for (int index = 1; index != _layout.LayoutBoxCount; ++index)
        {
            int candidate = _order[index];
            int candidateBucket = ZBucket(candidate);
            int cursor = index;
            while (cursor > 0 && ZBucket(_order[cursor - 1]) > candidateBucket)
            {
                _order[cursor] = _order[cursor - 1];
                --cursor;
            }
            _order[cursor] = candidate;
        }
        for (int index = 0; index != _layout.LayoutBoxCount; ++index)
        {
            switch (ZBucket(_order[index]))
            {
                case -1: ++_negativeZOrderCount; break;
                case 1: ++_positiveZOrderCount; break;
                default: ++_normalZOrderCount; break;
            }
        }
        return true;
    }

    private bool RunGenerationPass()
    {
        _layoutBoxesVisited = _layout.LayoutBoxCount;
        for (int node = 0; node != _document.NodeCount; ++node)
        {
            ManagedHtmlNodeHandle handle = NodeHandle(node);
            if (_document.GetNodeKind(handle) == ManagedHtmlNodeKind.Element &&
                _styles.TryGetComputedStyle(handle, out ManagedComputedStyle style) &&
                style.Display == ManagedCssDisplay.None)
                ++_displayNoneBoxesSkipped;
        }
        if (!Emit(new ManagedPaintCommand(ManagedPaintCommandKind.BeginClip, 1,
                    ManagedPaintCommandFlags.None, -1, -1, 0, 0, -1, 0,
                    new ManagedLayoutRect(0, 0, _viewportWidth, _viewportHeight),
                    new ManagedLayoutRect(0, 0, _viewportWidth, _viewportHeight),
                    0, new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
                     ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0))) return false;
        _clipDepth = 1;
        _clipStack[0] = new ManagedLayoutRect(0, 0, _viewportWidth, _viewportHeight);
        _peakClipDepth = Math.Max(_peakClipDepth, _clipDepth);
        for (int orderIndex = 0; orderIndex != _layout.LayoutBoxCount; ++orderIndex)
        {
            if (_cancelRequested) return Fail(ManagedPaintFailureReason.Cancelled);
            int boxIndex = _order[orderIndex];
            if (!BuildClipPath(boxIndex) || !TransitionClipPath()) return false;
            if (!PaintBox(boxIndex)) return false;
        }
        return true;
    }

    private bool PaintBox(int boxIndex)
    {
        if (!_layout.TryGetBox(boxIndex, out ManagedLayoutBox box))
            return Fail(ManagedPaintFailureReason.InvalidLayout);
        ManagedComputedStyle style = StyleForBox(box);
        bool hidden = style.Visibility == ManagedCssVisibility.Hidden ||
                      style.Visibility == ManagedCssVisibility.Collapse;
        if (hidden) ++_hiddenBoxesSkipped;
        else ++_visibleBoxes;
        if (!hidden && box.Kind != ManagedLayoutBoxKind.Root &&
            box.Kind != ManagedLayoutBoxKind.Text && box.Kind != ManagedLayoutBoxKind.LineBreak)
        {
            if (!PaintBackground(boxIndex, box, style)) return false;
            if (!PaintBorder(boxIndex, box, style)) return false;
        }
        if (hidden) return true;
        if (box.Kind == ManagedLayoutBoxKind.Text || box.Kind == ManagedLayoutBoxKind.Replaced)
        {
            for (int fragmentIndex = 0; fragmentIndex != _layout.TextFragmentCount; ++fragmentIndex)
            {
                if (!_layout.TryGetTextFragment(fragmentIndex, out ManagedLayoutTextFragment fragment) ||
                    fragment.OwnerBoxIndex != boxIndex) continue;
                if (fragment.Kind == ManagedLayoutTextFragmentKind.Replaced)
                {
                    if (_document.GetElementTag(NodeHandle(fragment.SourceNodeIndex)) == ManagedHtmlTag.Img &&
                        !EmitImage(boxIndex, fragment.SourceNodeIndex, fragment.Rectangle, style)) return false;
                }
                else if (!EmitText(boxIndex, fragment, style)) return false;
            }
        }
        return true;
    }

    private bool PaintBackground(int boxIndex, ManagedLayoutBox box, ManagedComputedStyle style)
    {
        if ((style.BackgroundColor & 0xFF000000U) == 0)
        {
            ++_transparentBackgroundsSkipped;
            return true;
        }
        if (!TryTransform(box.BorderBox, out ManagedLayoutRect rect))
            return Fail(ManagedPaintFailureReason.GeometryOverflow);
        uint color = ApplyOpacity(style.BackgroundColor, style.Opacity);
        return EmitPrimitive(boxIndex, box.SourceNodeIndex, ManagedPaintCommandKind.FillRectangle,
            rect, color, new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            style, 0, 0, -1, out _);
    }

    private bool PaintBorder(int boxIndex, ManagedLayoutBox box, ManagedComputedStyle style)
    {
        if (style.BorderStyle == ManagedCssBorderStyle.None ||
            (box.Border.Top == 0 && box.Border.Right == 0 &&
             box.Border.Bottom == 0 && box.Border.Left == 0)) return true;
        if (!TryTransform(box.BorderBox, out ManagedLayoutRect rect))
            return Fail(ManagedPaintFailureReason.GeometryOverflow);
        if (style.BorderStyle != ManagedCssBorderStyle.Solid) ++_unsupportedBorderStyles;
        return EmitPrimitive(boxIndex, box.SourceNodeIndex, ManagedPaintCommandKind.BorderRectangle,
            rect, ApplyOpacity(style.BorderColor, style.Opacity), box.Border,
            style.BorderStyle, style, 0, 0, -1, out _);
    }

    private bool EmitText(int boxIndex, ManagedLayoutTextFragment fragment,
                          ManagedComputedStyle style)
    {
        if (!TryTransform(fragment.Rectangle, out ManagedLayoutRect rect))
            return Fail(ManagedPaintFailureReason.GeometryOverflow);
        int baseline = AddChecked(rect.Y, fragment.Style.FontSize, out bool baselineOk);
        if (!baselineOk) return Fail(ManagedPaintFailureReason.GeometryOverflow);
        return EmitPrimitive(boxIndex, fragment.SourceNodeIndex, ManagedPaintCommandKind.TextRun,
            rect, ApplyOpacity(style.Color, style.Opacity), new ManagedLayoutEdges(0, 0, 0, 0),
            ManagedCssBorderStyle.None, style, fragment.SourceOffset, fragment.SourceLength,
            fragment.LineIndex, out _, fragment.Style, baseline);
    }

    private bool EmitImage(int boxIndex, int sourceNodeIndex, ManagedLayoutRect sourceRect,
                           ManagedComputedStyle style)
    {
        if (!TryTransform(sourceRect, out ManagedLayoutRect rect))
            return Fail(ManagedPaintFailureReason.GeometryOverflow);
        return EmitPrimitive(boxIndex, sourceNodeIndex, ManagedPaintCommandKind.ImagePlaceholder,
            rect, ApplyOpacity(0xFF808080U, style.Opacity), new ManagedLayoutEdges(0, 0, 0, 0),
            ManagedCssBorderStyle.None, style, 0, 0, -1, out _);
    }

    private bool EmitPrimitive(int boxIndex, int sourceNodeIndex, ManagedPaintCommandKind kind,
                               ManagedLayoutRect rect, uint color, ManagedLayoutEdges border,
                               ManagedCssBorderStyle borderStyle, ManagedComputedStyle style,
                               int sourceOffset, int sourceLength, int lineIndex,
                               out bool emitted, ManagedLayoutTextStyle? textStyle = null,
                               int baseline = 0)
    {
        emitted = false;
        if (!Intersects(rect, _clipStack[_clipDepth - 1]))
        {
            ++_offscreenCommandsCulled;
            return true;
        }
        ManagedLayoutTextStyle actualTextStyle = textStyle ?? new ManagedLayoutTextStyle(
            Math.Max(1, ResolveFontSize(style.FontSize)), style.FontWeight, style.FontStyle);
        ManagedLayoutRect clip = _clipStack[_clipDepth - 1];
        ManagedPaintCommandFlags flags = IsPositionedBox(boxIndex)
            ? ManagedPaintCommandFlags.Positioned : ManagedPaintCommandFlags.None;
        ManagedPaintCommand command = new(kind, (byte)_clipDepth, flags, boxIndex,
            sourceNodeIndex, sourceOffset, sourceLength, lineIndex, baseline, rect, clip, color,
            border, borderStyle, ManagedPaintFontId.DefaultUi, actualTextStyle.FontSize,
            actualTextStyle.FontWeight, actualTextStyle.FontStyle, style.Opacity,
            EffectiveZIndex(boxIndex));
        if (!Emit(command)) return false;
        emitted = true;
        switch (kind)
        {
            case ManagedPaintCommandKind.FillRectangle: ++_fillCommands; break;
            case ManagedPaintCommandKind.BorderRectangle: ++_borderCommands; break;
            case ManagedPaintCommandKind.TextRun: ++_textCommands; break;
            case ManagedPaintCommandKind.ImagePlaceholder: ++_imagePlaceholderCommands; break;
        }
        if (flags != ManagedPaintCommandFlags.None) ++_positionedCommands;
        return true;
    }

    private bool BuildClipPath(int boxIndex)
    {
        _clipPathScratch.AsSpan().Fill(-1);
        int count = 0;
        int parent = boxIndex;
        if (parent >= 0 && _layout.TryGetBox(parent, out ManagedLayoutBox currentBox))
            parent = currentBox.ParentIndex;
        while (parent >= 0)
        {
            if (!_layout.TryGetBox(parent, out ManagedLayoutBox box))
                return Fail(ManagedPaintFailureReason.InvalidLayout);
            if (BoxClips(box))
            {
                if (count == _clipPathScratch.Length)
                    return Fail(ManagedPaintFailureReason.PaintClipDepthExceeded);
                _clipPathScratch[count++] = parent;
            }
            parent = box.ParentIndex;
        }
        /* The parent chain is collected leaf-to-root; reverse it into the
           canonical root-to-leaf clip path. */
        for (int index = 0; index < count / 2; ++index)
        {
            int swap = _clipPathScratch[index];
            _clipPathScratch[index] = _clipPathScratch[count - index - 1];
            _clipPathScratch[count - index - 1] = swap;
        }
        return true;
    }

    private bool TransitionClipPath()
    {
        int desiredCount = 0;
        while (desiredCount < _clipPathScratch.Length && _clipPathScratch[desiredCount] >= 0)
            ++desiredCount;
        int common = 0;
        while (common < _activeClipPathCount && common < desiredCount &&
               _activeClipPath[common] == _clipPathScratch[common]) ++common;
        while (_activeClipPathCount > common)
        {
            ManagedLayoutRect clip = _clipStack[_clipDepth - 1];
            if (!Emit(new ManagedPaintCommand(ManagedPaintCommandKind.EndClip, (byte)_clipDepth,
                        ManagedPaintCommandFlags.None, _activeClipPath[_activeClipPathCount - 1],
                        BoxSourceNode(_activeClipPath[_activeClipPathCount - 1]), 0, 0, -1, 0,
                        clip, clip, 0, new ManagedLayoutEdges(0, 0, 0, 0),
                        ManagedCssBorderStyle.None, ManagedPaintFontId.DefaultUi, 0, 0,
                        ManagedCssFontStyle.Normal, 10_000, boxZIndex(
                             _activeClipPath[_activeClipPathCount - 1])))) return false;
            --_activeClipPathCount;
            --_clipDepth;
            ++_clipPops;
        }
        while (_activeClipPathCount < desiredCount)
        {
            if (_clipDepth == _clipStack.Length)
                return Fail(ManagedPaintFailureReason.PaintClipDepthExceeded);
            int clipBoxIndex = _clipPathScratch[_activeClipPathCount];
            if (!_layout.TryGetBox(clipBoxIndex, out ManagedLayoutBox box))
                return Fail(ManagedPaintFailureReason.InvalidLayout);
            if (!TryTransform(box.ClipRect, out ManagedLayoutRect clipRect))
                return Fail(ManagedPaintFailureReason.GeometryOverflow);
            clipRect = Intersect(_clipStack[_clipDepth - 1], clipRect);
            int nextDepth = _clipDepth + 1;
            if (!Emit(new ManagedPaintCommand(ManagedPaintCommandKind.BeginClip, (byte)nextDepth,
                        IsPositionedBox(clipBoxIndex) ? ManagedPaintCommandFlags.Positioned : ManagedPaintCommandFlags.None,
                        clipBoxIndex, box.SourceNodeIndex, 0, 0, -1, 0, clipRect, clipRect, 0,
                        new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
                         ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal,
                          10_000, boxZIndex(clipBoxIndex)))) return false;
            _clipStack[_clipDepth] = clipRect;
            _activeClipPath[_activeClipPathCount++] = clipBoxIndex;
            ++_clipDepth;
            ++_clipPushes;
            _peakClipDepth = Math.Max(_peakClipDepth, _clipDepth);
        }
        return true;
    }

    private bool CloseAllClips()
    {
        while (_activeClipPathCount > 0)
        {
            ManagedLayoutRect clip = _clipStack[_clipDepth - 1];
            int clipBoxIndex = _activeClipPath[_activeClipPathCount - 1];
            if (!Emit(new ManagedPaintCommand(ManagedPaintCommandKind.EndClip, (byte)_clipDepth,
                        ManagedPaintCommandFlags.None, clipBoxIndex, BoxSourceNode(clipBoxIndex), 0, 0,
                        -1, 0, clip, clip, 0, new ManagedLayoutEdges(0, 0, 0, 0),
                        ManagedCssBorderStyle.None, ManagedPaintFontId.DefaultUi, 0, 0,
                          ManagedCssFontStyle.Normal, 10_000, boxZIndex(clipBoxIndex)))) return false;
            --_activeClipPathCount;
            --_clipDepth;
            ++_clipPops;
        }
        if (_clipDepth != 1) return Fail(ManagedPaintFailureReason.InvalidState);
        ManagedLayoutRect root = _clipStack[0];
        if (!Emit(new ManagedPaintCommand(ManagedPaintCommandKind.EndClip, 1,
                    ManagedPaintCommandFlags.None, -1, -1, 0, 0, -1, 0, root, root, 0,
                    new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
                     ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0))) return false;
        _clipDepth = 0;
        ++_clipPops;
        return true;
    }

    private bool Emit(ManagedPaintCommand command)
    {
        if (!_countOnly && (_cancelRequested ||
            (_cancelAfterCommands >= 0 && _used >= _cancelAfterCommands)))
            return Fail(ManagedPaintFailureReason.Cancelled);
        if (!_countOnly && _used == _commands.Length)
            return Fail(ManagedPaintFailureReason.PaintCommandCapacityExceeded);
        if (!_countOnly) _commands[_used] = command;
        ++_used;
        _peak = Math.Max(_peak, _used);
        return true;
    }

    private void ClearPassState()
    {
        _used = 0;
        _peak = 0;
        _activeClipPathCount = 0;
        _clipDepth = 0;
        _peakClipDepth = 0;
        _layoutBoxesVisited = 0;
        _visibleBoxes = 0;
        _hiddenBoxesSkipped = 0;
        _displayNoneBoxesSkipped = 0;
        _fillCommands = 0;
        _borderCommands = 0;
        _textCommands = 0;
        _imagePlaceholderCommands = 0;
        _clipPushes = 0;
        _clipPops = 0;
        _offscreenCommandsCulled = 0;
        _transparentBackgroundsSkipped = 0;
        _unsupportedBorderStyles = 0;
        _positionedCommands = 0;
        _hashAvailable = false;
        _paintHash.AsSpan().Clear();
    }

    private bool ComputePaintHash()
    {
        _hash.Reset();
        if (!_hash.Append("GXOS-P46\0"u8)) return Fail(ManagedPaintFailureReason.InvalidState);
        Span<byte> scratch = stackalloc byte[8];
        AppendUInt32((uint)_viewportWidth, scratch);
        AppendUInt32((uint)_viewportHeight, scratch);
        AppendUInt32((uint)_scrollX, scratch);
        AppendUInt32((uint)_scrollY, scratch);
        AppendUInt32((uint)_used, scratch);
        for (int index = 0; index != _used; ++index)
        {
            ManagedPaintCommand command = _commands[index];
            AppendUInt32((uint)command.Kind, scratch);
            AppendUInt32(command.ClipDepth, scratch);
            AppendUInt32((uint)command.Flags, scratch);
            AppendUInt32((uint)command.FontId, scratch);
            AppendUInt32((uint)command.SourceBoxIndex, scratch);
            AppendUInt32((uint)command.SourceNodeIndex, scratch);
            AppendUInt32((uint)command.SourceOffset, scratch);
            AppendUInt32((uint)command.SourceLength, scratch);
            AppendUInt32((uint)command.LineIndex, scratch);
            AppendUInt32((uint)command.BaselineY, scratch);
            AppendRect(command.Rect, scratch);
            AppendRect(command.ClipRect, scratch);
            AppendUInt32(command.Color, scratch);
            AppendEdges(command.BorderWidths, scratch);
            AppendUInt32((uint)command.BorderStyle, scratch);
            AppendUInt32((uint)command.FontSize, scratch);
            AppendUInt32((uint)command.FontWeight, scratch);
            AppendUInt32((uint)command.FontStyle, scratch);
            AppendUInt32((uint)command.Opacity, scratch);
            AppendUInt32((uint)command.ZIndex, scratch);
        }
        return _hash.TryFinalize(_paintHash) && (_hashAvailable = true);
    }

    private void AppendUInt32(uint value, Span<byte> scratch)
    {
        scratch[0] = (byte)(value >> 24);
        scratch[1] = (byte)(value >> 16);
        scratch[2] = (byte)(value >> 8);
        scratch[3] = (byte)value;
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

    private ManagedComputedStyle StyleForBox(ManagedLayoutBox box)
    {
        ManagedHtmlNodeHandle node = NodeHandle(box.SourceNodeIndex);
        if (_document.GetNodeKind(node) == ManagedHtmlNodeKind.Element &&
            _styles.TryGetComputedStyle(node, out ManagedComputedStyle style)) return style;
        ManagedHtmlNodeHandle parent = _document.GetParent(node);
        if (parent != ManagedHtmlNodeHandle.Invalid && _styles.TryGetComputedStyle(parent, out style)) return style;
        return default;
    }

    private int ZBucket(int boxIndex)
    {
        if (!_layout.TryGetBox(boxIndex, out ManagedLayoutBox box)) return 0;
        int current = boxIndex;
        while (current >= 0)
        {
            _layout.TryGetBox(current, out box);
            if (IsPositioned(box)) return box.ZIndex < 0 ? -1 : box.ZIndex > 0 ? 1 : 0;
            current = box.ParentIndex;
        }
        return 0;
    }

    private int boxZIndex(int boxIndex) =>
        _layout.TryGetBox(boxIndex, out ManagedLayoutBox box) ? box.ZIndex : 0;

    private int EffectiveZIndex(int boxIndex)
    {
        int current = boxIndex;
        while (current >= 0)
        {
            if (!_layout.TryGetBox(current, out ManagedLayoutBox box)) return 0;
            if (IsPositioned(box)) return box.ZIndex;
            current = box.ParentIndex;
        }
        return 0;
    }

    private bool IsPositionedBox(int boxIndex) =>
        _layout.TryGetBox(boxIndex, out ManagedLayoutBox box) && IsPositioned(box);

    private static bool IsPositioned(ManagedLayoutBox box) =>
        (box.Flags & (ManagedLayoutBoxFlags.Relative | ManagedLayoutBoxFlags.Absolute |
                      ManagedLayoutBoxFlags.Fixed)) != 0;

    private bool BoxClips(ManagedLayoutBox box)
    {
        if (box.Kind == ManagedLayoutBoxKind.Root) return false;
        ManagedComputedStyle style = StyleForBox(box);
        ManagedCssOverflow x = style.OverflowX == ManagedCssOverflow.Visible ? style.Overflow : style.OverflowX;
        ManagedCssOverflow y = style.OverflowY == ManagedCssOverflow.Visible ? style.Overflow : style.OverflowY;
        return x != ManagedCssOverflow.Visible || y != ManagedCssOverflow.Visible;
    }

    private int BoxSourceNode(int boxIndex) =>
        _layout.TryGetBox(boxIndex, out ManagedLayoutBox box) ? box.SourceNodeIndex : -1;

    private bool TryTransform(ManagedLayoutRect source, out ManagedLayoutRect result)
    {
        long x = (long)source.X - _scrollX;
        long y = (long)source.Y - _scrollY;
        if (x < -ManagedLayoutLimits.MaximumCoordinate || x > ManagedLayoutLimits.MaximumCoordinate ||
            y < -ManagedLayoutLimits.MaximumCoordinate || y > ManagedLayoutLimits.MaximumCoordinate ||
            (long)source.Width + x > ManagedLayoutLimits.MaximumCoordinate ||
            (long)source.Height + y > ManagedLayoutLimits.MaximumCoordinate)
        {
            result = default;
            return false;
        }
        result = new ManagedLayoutRect((int)x, (int)y, source.Width, source.Height);
        return true;
    }

    private static ManagedLayoutRect Intersect(ManagedLayoutRect left, ManagedLayoutRect right)
    {
        long x = Math.Max(left.X, right.X);
        long y = Math.Max(left.Y, right.Y);
        long r = Math.Min((long)left.X + left.Width, (long)right.X + right.Width);
        long b = Math.Min((long)left.Y + left.Height, (long)right.Y + right.Height);
        return r <= x || b <= y ? new ManagedLayoutRect((int)x, (int)y, 0, 0) :
            new ManagedLayoutRect((int)x, (int)y, (int)(r - x), (int)(b - y));
    }

    private static bool Intersects(ManagedLayoutRect left, ManagedLayoutRect right) =>
        left.Width > 0 && left.Height > 0 && right.Width > 0 && right.Height > 0 &&
        (long)left.X < (long)right.X + right.Width &&
        (long)right.X < (long)left.X + left.Width &&
        (long)left.Y < (long)right.Y + right.Height &&
        (long)right.Y < (long)left.Y + left.Height;

    private static uint ApplyOpacity(uint color, int opacity)
    {
        opacity = Math.Clamp(opacity, 0, 10_000);
        uint alpha = ((color >> 24) * (uint)opacity) / 10_000U;
        return (alpha << 24) | (color & 0x00FFFFFFU);
    }

    private static int ResolveFontSize(ManagedCssLength value)
    {
        if (value.Unit == ManagedCssLengthUnit.Px) return Math.Max(1, value.Value / 100);
        if (value.Unit == ManagedCssLengthUnit.Em || value.Unit == ManagedCssLengthUnit.Rem)
            return Math.Max(1, (int)((long)1600 * value.Value / 100000));
        if (value.Unit == ManagedCssLengthUnit.Percent)
            return Math.Max(1, (int)((long)1600 * value.Value / 10000));
        return 16;
    }

    private static int AddChecked(int left, int right, out bool ok)
    {
        long result = (long)left + right;
        ok = result >= -ManagedLayoutLimits.MaximumCoordinate &&
             result <= ManagedLayoutLimits.MaximumCoordinate;
        return ok ? (int)result : 0;
    }

    private ManagedHtmlNodeHandle NodeHandle(int index) =>
        index < 0 || index >= _document.NodeCount ? ManagedHtmlNodeHandle.Invalid :
        new(index, _document.DocumentNode.Generation);

    private bool Fail(ManagedPaintFailureReason reason)
    {
        if (_failureReason == ManagedPaintFailureReason.None)
            _failureReason = reason;
        _state = reason == ManagedPaintFailureReason.Cancelled
            ? ManagedPaintState.Cancelled : ManagedPaintState.Failed;
        return false;
    }
}

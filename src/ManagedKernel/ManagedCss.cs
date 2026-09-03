using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedCssSourceKind : byte
{
    Embedded = 1,
    Inline = 2
}

public enum ManagedCssParseFailureReason : byte
{
    None = 0,
    StylesheetCapacityExceeded = 1,
    RuleCapacityExceeded = 2,
    SelectorCapacityExceeded = 3,
    DeclarationCapacityExceeded = 4,
    SelectorTooComplex = 5,
    SelectorNameTooLong = 6,
    ValueTooLong = 7,
    ExternalStylesheetCapacityExceeded = 8,
    InvalidDocument = 9,
    Cancelled = 10,
    ComputedStyleCapacityExceeded = 11,
    StyleTraversalFailure = 12
}

public enum ManagedCssDisplay : byte
{
    None = 0,
    Inline = 1,
    Block = 2,
    InlineBlock = 3,
    Table = 4,
    TableRow = 5,
    TableCell = 6,
    ListItem = 7,
    Flex = 8
}

public enum ManagedCssVisibility : byte
{
    Visible = 0,
    Hidden = 1,
    Collapse = 2
}

public enum ManagedCssFontStyle : byte
{
    Normal = 0,
    Italic = 1
}

public enum ManagedCssTextAlign : byte
{
    Left = 0,
    Right = 1,
    Center = 2,
    Justify = 3
}

public enum ManagedCssWhiteSpace : byte
{
    Normal = 0,
    Pre = 1,
    NoWrap = 2,
    PreWrap = 3,
    PreLine = 4
}

public enum ManagedCssPosition : byte
{
    Static = 0,
    Relative = 1,
    Absolute = 2,
    Fixed = 3
}

public enum ManagedCssOverflow : byte
{
    Visible = 0,
    Hidden = 1,
    Scroll = 2,
    Auto = 3
}

public enum ManagedCssBorderStyle : byte
{
    None = 0,
    Solid = 1,
    Dashed = 2,
    Dotted = 3
}

public enum ManagedCssLengthUnit : byte
{
    Px = 0,
    Percent = 1,
    Em = 2,
    Rem = 3,
    Auto = 4
}

public enum ManagedCssProperty : byte
{
    Display = 0,
    Visibility = 1,
    Color = 2,
    BackgroundColor = 3,
    FontSize = 4,
    FontWeight = 5,
    FontStyle = 6,
    TextAlign = 7,
    WhiteSpace = 8,
    Width = 9,
    Height = 10,
    MinWidth = 11,
    MinHeight = 12,
    MaxWidth = 13,
    MaxHeight = 14,
    MarginTop = 15,
    MarginRight = 16,
    MarginBottom = 17,
    MarginLeft = 18,
    PaddingTop = 19,
    PaddingRight = 20,
    PaddingBottom = 21,
    PaddingLeft = 22,
    BorderWidth = 23,
    BorderStyle = 24,
    BorderColor = 25,
    Position = 26,
    Top = 27,
    Right = 28,
    Bottom = 29,
    Left = 30,
    Overflow = 31,
    OverflowX = 32,
    OverflowY = 33,
    Opacity = 34,
    ZIndex = 35,
    Count = 36
}

public enum ManagedCssKeyword : ushort
{
    None = 0,
    Auto = 1,
    Normal = 2,
    Inherit = 3,
    Initial = 4,
    Visible = 5,
    Hidden = 6,
    Collapse = 7,
    Inline = 8,
    Block = 9,
    InlineBlock = 10,
    Table = 11,
    TableRow = 12,
    TableCell = 13,
    ListItem = 14,
    Flex = 15,
    Bold = 16,
    Italic = 17,
    Left = 18,
    Right = 19,
    Center = 20,
    Justify = 21,
    Pre = 22,
    NoWrap = 23,
    PreWrap = 24,
    PreLine = 25,
    Static = 26,
    Relative = 27,
    Absolute = 28,
    Fixed = 29,
    Scroll = 30,
    Dashed = 31,
    Dotted = 32,
    Solid = 33
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct ManagedCssLength
    : IEquatable<ManagedCssLength>
{
    public ManagedCssLength(int value, ManagedCssLengthUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public int Value { get; }
    public ManagedCssLengthUnit Unit { get; }
    public bool IsAuto => Unit == ManagedCssLengthUnit.Auto;
    public bool Equals(ManagedCssLength other) => Value == other.Value && Unit == other.Unit;
    public override bool Equals(object? obj) => obj is ManagedCssLength other && Equals(other);
    public override int GetHashCode() => unchecked((Value * 397) ^ (int)Unit);
    public static bool operator ==(ManagedCssLength left, ManagedCssLength right) => left.Equals(right);
    public static bool operator !=(ManagedCssLength left, ManagedCssLength right) => !left.Equals(right);
}

public readonly struct ManagedCssArenaOptions
{
    public ManagedCssArenaOptions(
        int stylesheetCapacity,
        int ruleCapacity,
        int selectorCapacity,
        int selectorStepCapacity,
        int declarationCapacity,
        int computedStyleCapacity = ManagedCssLimits.DefaultComputedStyleCapacity)
    {
        Validate(stylesheetCapacity, ruleCapacity, selectorCapacity,
                 selectorStepCapacity, declarationCapacity, computedStyleCapacity);
        StylesheetCapacity = stylesheetCapacity;
        RuleCapacity = ruleCapacity;
        SelectorCapacity = selectorCapacity;
        SelectorStepCapacity = selectorStepCapacity;
        DeclarationCapacity = declarationCapacity;
        ComputedStyleCapacity = computedStyleCapacity;
    }

    public static ManagedCssArenaOptions Default => new(
        ManagedCssLimits.DefaultStylesheetCapacity,
        ManagedCssLimits.DefaultRuleCapacity,
        ManagedCssLimits.DefaultSelectorCapacity,
        ManagedCssLimits.DefaultSelectorStepCapacity,
        ManagedCssLimits.DefaultDeclarationCapacity,
        ManagedCssLimits.DefaultComputedStyleCapacity);

    public int StylesheetCapacity { get; }
    public int RuleCapacity { get; }
    public int SelectorCapacity { get; }
    public int SelectorStepCapacity { get; }
    public int DeclarationCapacity { get; }
    public int ComputedStyleCapacity { get; }

    private static void Validate(int stylesheetCapacity, int ruleCapacity,
                                 int selectorCapacity, int selectorStepCapacity,
                                 int declarationCapacity, int computedStyleCapacity)
    {
        if (stylesheetCapacity <= 0 || stylesheetCapacity > ManagedCssLimits.MaximumStylesheetCapacity ||
            ruleCapacity <= 0 || ruleCapacity > ManagedCssLimits.MaximumRuleCapacity ||
            selectorCapacity <= 0 || selectorCapacity > ManagedCssLimits.MaximumSelectorCapacity ||
            selectorStepCapacity <= 0 || selectorStepCapacity > ManagedCssLimits.MaximumSelectorStepCapacity ||
            declarationCapacity <= 0 || declarationCapacity > ManagedCssLimits.MaximumDeclarationCapacity ||
            computedStyleCapacity <= 0 || computedStyleCapacity > ManagedCssLimits.MaximumComputedStyleCapacity)
            throw new ArgumentOutOfRangeException(nameof(ruleCapacity));
    }
}

public static class ManagedCssLimits
{
    public const int DefaultStylesheetCapacity = 8;
    public const int DefaultRuleCapacity = 256;
    public const int DefaultSelectorCapacity = 512;
    public const int DefaultSelectorStepCapacity = 1024;
    public const int DefaultSelectorClassCapacity = 1024;
    public const int DefaultAttributeSelectorCapacity = 256;
    public const int DefaultDeclarationCapacity = 1024;
    public const int DefaultComputedStyleCapacity = 1024;
    public const int DefaultExternalStylesheetCapacity = 16;
    public const int SelectorNameCapacity = 16_384;
    public const int MaximumSelectorSteps = 8;
    public const int MaximumClassesPerStep = 8;
    public const int MaximumAttributesPerStep = 2;
    public const int MaximumSelectorsPerRule = 8;
    public const int MaximumSelectorNameLength = 64;
    public const int MaximumSelectorLength = 256;
    public const int MaximumValueLength = 256;
    public const int MaximumDeclarationsPerRule = 64;
    public const int MaximumStylesheetCapacity = 64;
    public const int MaximumRuleCapacity = 2048;
    public const int MaximumSelectorCapacity = 4096;
    public const int MaximumSelectorStepCapacity = 8192;
    public const int MaximumDeclarationCapacity = 16_384;
    public const int MaximumComputedStyleCapacity = 4096;
}

public readonly struct ManagedCssTelemetry
{
    internal ManagedCssTelemetry(ManagedCssEngine engine)
    {
        StylesheetsParsed = engine.StylesheetsParsed;
        RulesParsed = engine.RulesParsed;
        SelectorsParsed = engine.SelectorsParsed;
        DeclarationsParsed = engine.DeclarationsParsed;
        SelectorMatches = engine.SelectorMatches;
        InlineStylesParsed = engine.InlineStylesParsed;
        ImportantDeclarations = engine.ImportantDeclarations;
        InheritedAssignments = engine.InheritedAssignments;
        ElementsStyled = engine.ElementsStyled;
        UnknownProperties = engine.UnknownProperties;
        CustomPropertiesIgnored = engine.CustomPropertiesIgnored;
        MalformedDeclarations = engine.MalformedDeclarations;
        MalformedRules = engine.MalformedRules;
        UnsupportedSelectors = engine.UnsupportedSelectors;
        SelectorTooComplexCount = engine.SelectorTooComplexCount;
        ValueTooLongCount = engine.ValueTooLongCount;
        RulesSkipped = engine.RulesSkipped;
        DeclarationsSkipped = engine.DeclarationsSkipped;
        MaximumSelectorDepth = engine.MaximumSelectorDepth;
        ExternalStylesheetCount = engine.ExternalStylesheetCount;
        StylesheetCapacity = engine.StylesheetCapacity;
        RuleCapacity = engine.RuleCapacity;
        SelectorCapacity = engine.SelectorCapacity;
        DeclarationCapacity = engine.DeclarationCapacity;
        ComputedStyleCapacity = engine.ComputedStyleCapacity;
        StylesheetPeak = engine.StylesheetPeak;
        RulePeak = engine.RulePeak;
        SelectorPeak = engine.SelectorPeak;
        DeclarationPeak = engine.DeclarationPeak;
        ComputedStylePeak = engine.ComputedStylePeak;
    }

    public int StylesheetsParsed { get; }
    public int RulesParsed { get; }
    public int SelectorsParsed { get; }
    public int DeclarationsParsed { get; }
    public int SelectorMatches { get; }
    public int InlineStylesParsed { get; }
    public int ImportantDeclarations { get; }
    public int InheritedAssignments { get; }
    public int ElementsStyled { get; }
    public int UnknownProperties { get; }
    public int CustomPropertiesIgnored { get; }
    public int MalformedDeclarations { get; }
    public int MalformedRules { get; }
    public int UnsupportedSelectors { get; }
    public int SelectorTooComplexCount { get; }
    public int ValueTooLongCount { get; }
    public int RulesSkipped { get; }
    public int DeclarationsSkipped { get; }
    public int MaximumSelectorDepth { get; }
    public int ExternalStylesheetCount { get; }
    public int StylesheetCapacity { get; }
    public int RuleCapacity { get; }
    public int SelectorCapacity { get; }
    public int DeclarationCapacity { get; }
    public int ComputedStyleCapacity { get; }
    public int StylesheetPeak { get; }
    public int RulePeak { get; }
    public int SelectorPeak { get; }
    public int DeclarationPeak { get; }
    public int ComputedStylePeak { get; }
}

public struct ManagedComputedStyle
{
    internal ulong SpecifiedMask;
    internal ulong InheritedMask;
    internal ulong ImportantMask;
    internal ManagedCssDisplay DisplayValue;
    internal ManagedCssVisibility VisibilityValue;
    internal uint ColorValue;
    internal uint BackgroundColorValue;
    internal ManagedCssLength FontSizeValue;
    internal int FontWeightValue;
    internal ManagedCssFontStyle FontStyleValue;
    internal ManagedCssTextAlign TextAlignValue;
    internal ManagedCssWhiteSpace WhiteSpaceValue;
    internal ManagedCssLength WidthValue;
    internal ManagedCssLength HeightValue;
    internal ManagedCssLength MinWidthValue;
    internal ManagedCssLength MinHeightValue;
    internal ManagedCssLength MaxWidthValue;
    internal ManagedCssLength MaxHeightValue;
    internal ManagedCssLength MarginTopValue;
    internal ManagedCssLength MarginRightValue;
    internal ManagedCssLength MarginBottomValue;
    internal ManagedCssLength MarginLeftValue;
    internal ManagedCssLength PaddingTopValue;
    internal ManagedCssLength PaddingRightValue;
    internal ManagedCssLength PaddingBottomValue;
    internal ManagedCssLength PaddingLeftValue;
    internal ManagedCssLength BorderWidthValue;
    internal ManagedCssBorderStyle BorderStyleValue;
    internal uint BorderColorValue;
    internal ManagedCssPosition PositionValue;
    internal ManagedCssLength TopValue;
    internal ManagedCssLength RightValue;
    internal ManagedCssLength BottomValue;
    internal ManagedCssLength LeftValue;
    internal ManagedCssOverflow OverflowValue;
    internal ManagedCssOverflow OverflowXValue;
    internal ManagedCssOverflow OverflowYValue;
    internal int OpacityValue;
    internal int ZIndexValue;
    internal bool ZIndexAutoValue;

    public ulong SpecifiedProperties => SpecifiedMask;
    public ulong InheritedProperties => InheritedMask;
    public ulong ImportantProperties => ImportantMask;
    public ManagedCssDisplay Display => DisplayValue;
    public ManagedCssVisibility Visibility => VisibilityValue;
    public uint Color => ColorValue;
    public uint BackgroundColor => BackgroundColorValue;
    public ManagedCssLength FontSize => FontSizeValue;
    public int FontWeight => FontWeightValue;
    public ManagedCssFontStyle FontStyle => FontStyleValue;
    public ManagedCssTextAlign TextAlign => TextAlignValue;
    public ManagedCssWhiteSpace WhiteSpace => WhiteSpaceValue;
    public ManagedCssLength Width => WidthValue;
    public ManagedCssLength Height => HeightValue;
    public ManagedCssLength MinWidth => MinWidthValue;
    public ManagedCssLength MinHeight => MinHeightValue;
    public ManagedCssLength MaxWidth => MaxWidthValue;
    public ManagedCssLength MaxHeight => MaxHeightValue;
    public ManagedCssLength MarginTop => MarginTopValue;
    public ManagedCssLength MarginRight => MarginRightValue;
    public ManagedCssLength MarginBottom => MarginBottomValue;
    public ManagedCssLength MarginLeft => MarginLeftValue;
    public ManagedCssLength PaddingTop => PaddingTopValue;
    public ManagedCssLength PaddingRight => PaddingRightValue;
    public ManagedCssLength PaddingBottom => PaddingBottomValue;
    public ManagedCssLength PaddingLeft => PaddingLeftValue;
    public ManagedCssLength BorderWidth => BorderWidthValue;
    public ManagedCssBorderStyle BorderStyle => BorderStyleValue;
    public uint BorderColor => BorderColorValue;
    public ManagedCssPosition Position => PositionValue;
    public ManagedCssLength Top => TopValue;
    public ManagedCssLength Right => RightValue;
    public ManagedCssLength Bottom => BottomValue;
    public ManagedCssLength Left => LeftValue;
    public ManagedCssOverflow Overflow => OverflowValue;
    public ManagedCssOverflow OverflowX => OverflowXValue;
    public ManagedCssOverflow OverflowY => OverflowYValue;
    public int Opacity => OpacityValue;
    public int ZIndex => ZIndexValue;
    public bool ZIndexIsAuto => ZIndexAutoValue;
}

internal enum ManagedCssValueKind : byte
{
    Invalid = 0,
    Keyword = 1,
    Number = 2,
    Length = 3,
    Color = 4
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssValue
{
    internal ManagedCssValueKind Kind;
    internal ManagedCssKeyword Keyword;
    internal ManagedCssLengthUnit Unit;
    internal int Number;
    internal uint Color;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssDeclarationRecord
{
    internal ManagedCssProperty Property;
    internal byte Important;
    internal ushort Reserved;
    internal ManagedCssValue Value;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssStylesheetRecord
{
    internal int RootNodeIndex;
    internal int FirstRule;
    internal int RuleCount;
    internal int SourceOrder;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssRuleRecord
{
    internal int SelectorIndex;
    internal int DeclarationIndex;
    internal int DeclarationCount;
    internal int SourceOrder;
    internal byte Origin;
    internal byte Reserved0;
    internal ushort Reserved1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssSelectorRecord
{
    internal int StepIndex;
    internal byte StepCount;
    internal byte IdSpecificity;
    internal byte ClassSpecificity;
    internal byte TypeSpecificity;
    internal int SourceOrder;
    internal byte Reserved0;
    internal byte Reserved1;
    internal ushort Reserved2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssSelectorStep
{
    internal ManagedHtmlTag KnownTag;
    internal int TagNameOffset;
    internal byte TagNameLength;
    internal byte Flags;
    internal int IdOffset;
    internal byte IdLength;
    internal byte ClassCount;
    internal int FirstClass;
    internal int FirstAttribute;
    internal byte AttributeCount;
    internal byte RelationToNext;
    internal ushort Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssClassRecord
{
    internal int NameOffset;
    internal byte NameLength;
    internal byte Reserved0;
    internal ushort Reserved1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssAttributeSelector
{
    internal int NameOffset;
    internal byte NameLength;
    internal byte ValueLength;
    internal byte Flags;
    internal byte Reserved;
    internal int ValueOffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssInlineRecord
{
    internal int DeclarationIndex;
    internal int DeclarationCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssExternalStylesheetRecord
{
    internal ManagedHtmlNodeHandle Node;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedCssCascadeCandidate
{
    internal byte Present;
    internal byte Important;
    internal byte Origin;
    internal byte IdSpecificity;
    internal byte ClassSpecificity;
    internal byte TypeSpecificity;
    internal ushort Reserved;
    internal int SourceOrder;
    internal int DeclarationOrder;
    internal ManagedCssValue Value;
}

public sealed class ManagedCssEngine
{
    private static ManagedCssEngine? s_nativeKernelDefaultArena;
    private const byte StepHasTag = 1;
    private const byte StepHasId = 2;
    private const byte StepHasClasses = 4;
    private const byte StepHasAttributes = 8;
    private const byte StepHasRoot = 16;
    private const byte StepHasFirstChild = 32;
    private const byte StepHasLastChild = 64;
    private const byte AttributeHasValue = 1;
    private const byte OriginAuthor = 1;
    private const byte OriginInline = 2;
    private const byte RelationDescendant = 0;
    private const byte RelationChild = 1;
    private ManagedHtmlDocument _document;
    private readonly ManagedCssStylesheetRecord[] _stylesheets;
    private readonly ManagedCssRuleRecord[] _rules;
    private readonly ManagedCssSelectorRecord[] _selectors;
    private readonly ManagedCssSelectorStep[] _steps;
    private readonly ManagedCssClassRecord[] _classes;
    private readonly ManagedCssAttributeSelector[] _attributeSelectors;
    private readonly ManagedCssDeclarationRecord[] _declarations;
    private readonly ManagedCssInlineRecord[] _inline;
    private readonly ManagedCssExternalStylesheetRecord[] _external;
    private readonly ManagedComputedStyle[] _computed;
    private readonly int[] _matchedRules;
    private readonly byte[] _selectorNames = new byte[ManagedCssLimits.SelectorNameCapacity];
    private readonly byte[] _selectorScratch = new byte[ManagedCssLimits.MaximumSelectorLength];
    private readonly byte[] _valueScratch = new byte[ManagedCssLimits.MaximumValueLength];
    private readonly ManagedCssCascadeCandidate[] _winners;
    private readonly ManagedCssMatchState[] _matchStates;
    private readonly int[] _matchVisited;
    private readonly byte[] _tagScratch = new byte[ManagedCssLimits.MaximumSelectorNameLength];
    private readonly uint[] _attributeScratch = new uint[ManagedCssLimits.MaximumValueLength];
    private readonly uint[] _classScratch = new uint[ManagedCssLimits.MaximumValueLength];
    private readonly byte[] _relScratch = new byte[ManagedCssLimits.MaximumValueLength];
    private readonly byte[] _propertyScratch = new byte[64];
    private readonly uint[] _externalRelScratch = new uint[64];
    private readonly byte[] _externalHrefScratch = new byte[ManagedCssLimits.MaximumValueLength];
    private readonly byte[] _styleHash = new byte[ManagedSha256.DigestSize];
    private readonly ManagedSha256 _hash = new();
    private ManagedCssParseFailureReason _failureReason;
    private int _stylesheetCount;
    private int _ruleCount;
    private int _ruleCapacityLimit;
    private int _selectorCount;
    private int _stepCount;
    private int _classCount;
    private int _attributeSelectorCount;
    private int _declarationCount;
    private int _externalCount;
    private int _nameUsed;
    private int _sourceOrder;
    private int _matchGeneration;
    private bool _styled;
    private bool _styleHashAvailable;
    private int _stylesheetsPeak;
    private int _rulesPeak;
    private int _selectorsPeak;
    private int _declarationsPeak;
    private int _computedPeak;
    private int _selectorMatches;
    private int _inlineStylesParsed;
    private int _importantDeclarations;
    private int _inheritedAssignments;
    private int _elementsStyled;
    private int _unknownProperties;
    private int _customPropertiesIgnored;
    private int _malformedDeclarations;
    private int _malformedRules;
    private int _unsupportedSelectors;
    private int _selectorTooComplexCount;
    private int _valueTooLongCount;
    private int _rulesSkipped;
    private int _declarationsSkipped;
    private int _maximumSelectorDepth;

    public ManagedCssEngine(ManagedHtmlDocument document)
        : this(document, ManagedCssArenaOptions.Default) { }

    public ManagedCssEngine(ManagedHtmlDocument document, ManagedCssArenaOptions options)
        : this(document, options, false) { }

    private ManagedCssEngine(ManagedHtmlDocument? document,
                             ManagedCssArenaOptions options, bool unbound)
    {
        if (document == null && !unbound)
            throw new ArgumentNullException(nameof(document));
        _document = document!;
        _stylesheets = new ManagedCssStylesheetRecord[options.StylesheetCapacity];
        _rules = new ManagedCssRuleRecord[options.RuleCapacity];
        _selectors = new ManagedCssSelectorRecord[options.SelectorCapacity];
        _steps = new ManagedCssSelectorStep[options.SelectorStepCapacity];
        _classes = new ManagedCssClassRecord[Math.Min(
            ManagedCssLimits.DefaultSelectorClassCapacity, options.SelectorStepCapacity)];
        _attributeSelectors = new ManagedCssAttributeSelector[Math.Min(
            ManagedCssLimits.DefaultAttributeSelectorCapacity, options.SelectorStepCapacity / 2 + 1)];
        _declarations = new ManagedCssDeclarationRecord[options.DeclarationCapacity];
        _inline = new ManagedCssInlineRecord[options.ComputedStyleCapacity];
        _external = new ManagedCssExternalStylesheetRecord[ManagedCssLimits.DefaultExternalStylesheetCapacity];
        _computed = new ManagedComputedStyle[options.ComputedStyleCapacity];
        _matchedRules = new int[options.ComputedStyleCapacity];
        _winners = new ManagedCssCascadeCandidate[(int)ManagedCssProperty.Count];
        _matchStates = new ManagedCssMatchState[ManagedCssLimits.MaximumSelectorSteps *
                                                 ManagedCssLimits.MaximumComputedStyleCapacity];
        _matchVisited = new int[_matchStates.Length];
        _ruleCapacityLimit = _rules.Length;
    }

    internal static bool PrimeNativeKernelArenas()
    {
        if (s_nativeKernelDefaultArena == null)
            s_nativeKernelDefaultArena = new ManagedCssEngine(
                null, ManagedCssArenaOptions.Default, true);
        return true;
    }

    internal static ManagedCssEngine? TakeNativeKernelArena(
        ManagedHtmlDocument document, bool capacityControl)
    {
        ManagedCssEngine? engine = s_nativeKernelDefaultArena;
        s_nativeKernelDefaultArena = null;
        if (engine == null) return null;
        engine._document = document;
        engine._ruleCapacityLimit = capacityControl ? 1 : engine._rules.Length;
        engine.Reset();
        return engine;
    }

    public ManagedHtmlDocument Document => _document;
    public ManagedCssParseFailureReason FailureReason => _failureReason;
    public bool IsStyled => _styled;
    public int StylesheetCapacity => _stylesheets.Length;
    public int RuleCapacity => _ruleCapacityLimit;
    public int SelectorCapacity => _selectors.Length;
    public int DeclarationCapacity => _declarations.Length;
    public int ComputedStyleCapacity => _computed.Length;
    public int StylesheetsParsed => _stylesheetCount;
    public int RulesParsed => _ruleCount;
    public int SelectorsParsed => _selectorCount;
    public int DeclarationsParsed => _declarationCount;
    public int SelectorMatches => _selectorMatches;
    public int InlineStylesParsed => _inlineStylesParsed;
    public int ImportantDeclarations => _importantDeclarations;
    public int InheritedAssignments => _inheritedAssignments;
    public int ElementsStyled => _elementsStyled;
    public int UnknownProperties => _unknownProperties;
    public int CustomPropertiesIgnored => _customPropertiesIgnored;
    public int MalformedDeclarations => _malformedDeclarations;
    public int MalformedRules => _malformedRules;
    public int UnsupportedSelectors => _unsupportedSelectors;
    public int SelectorTooComplexCount => _selectorTooComplexCount;
    public int ValueTooLongCount => _valueTooLongCount;
    public int RulesSkipped => _rulesSkipped;
    public int DeclarationsSkipped => _declarationsSkipped;
    public int MaximumSelectorDepth => _maximumSelectorDepth;
    public int ExternalStylesheetCount => _externalCount;
    public int StylesheetPeak => _stylesheetsPeak;
    public int RulePeak => _rulesPeak;
    public int SelectorPeak => _selectorsPeak;
    public int DeclarationPeak => _declarationsPeak;
    public int ComputedStylePeak => _computedPeak;
    public ManagedCssTelemetry Telemetry => new(this);

    public bool TryStyle()
    {
        Reset();
        if (_document == null || !_document.IsValid(_document.DocumentNode))
        {
            _failureReason = ManagedCssParseFailureReason.InvalidDocument;
            return false;
        }
        if (!DiscoverExternalStylesheets() || !ParseEmbeddedStylesheets() ||
            !ParseInlineStyles() || !ComputeStyles())
            return false;
        if (_failureReason != ManagedCssParseFailureReason.None) return false;
        _styled = true;
        return ComputeStyleHash();
    }

    public void Reset()
    {
        _stylesheets.AsSpan().Clear();
        _rules.AsSpan().Clear();
        _selectors.AsSpan().Clear();
        _steps.AsSpan().Clear();
        _classes.AsSpan().Clear();
        _attributeSelectors.AsSpan().Clear();
        _declarations.AsSpan().Clear();
        _inline.AsSpan().Clear();
        _external.AsSpan().Clear();
        _computed.AsSpan().Clear();
        _matchedRules.AsSpan().Clear();
        _selectorNames.AsSpan().Clear();
        _matchVisited.AsSpan().Clear();
        _failureReason = ManagedCssParseFailureReason.None;
        _stylesheetCount = 0;
        _ruleCount = 0;
        _selectorCount = 0;
        _stepCount = 0;
        _classCount = 0;
        _attributeSelectorCount = 0;
        _declarationCount = 0;
        _externalCount = 0;
        _nameUsed = 0;
        _sourceOrder = 0;
        _matchGeneration = 0;
        _styled = false;
        _styleHashAvailable = false;
        _stylesheetsPeak = 0;
        _rulesPeak = 0;
        _selectorsPeak = 0;
        _declarationsPeak = 0;
        _computedPeak = 0;
        _selectorMatches = 0;
        _inlineStylesParsed = 0;
        _importantDeclarations = 0;
        _inheritedAssignments = 0;
        _elementsStyled = 0;
        _unknownProperties = 0;
        _customPropertiesIgnored = 0;
        _malformedDeclarations = 0;
        _malformedRules = 0;
        _unsupportedSelectors = 0;
        _selectorTooComplexCount = 0;
        _valueTooLongCount = 0;
        _rulesSkipped = 0;
        _declarationsSkipped = 0;
        _maximumSelectorDepth = 0;
        _styleHash.AsSpan().Clear();
        _hash.Reset();
    }

    public bool TryParseStylesheet(ReadOnlySpan<uint> source)
    {
        if (_failureReason != ManagedCssParseFailureReason.None) return false;
        if (!BeginStylesheet(-1)) return false;
        ManagedCssInput input = ManagedCssInput.FromSpan(source);
        return ParseStylesheet(ref input, _stylesheetCount - 1) &&
               _failureReason == ManagedCssParseFailureReason.None;
    }

    public bool TryGetComputedStyle(ManagedHtmlNodeHandle handle,
                                    out ManagedComputedStyle style)
    {
        style = default;
        if (!_document.IsValid(handle) || handle.Index >= _computed.Length || !_styled)
            return false;
        style = _computed[handle.Index];
        return _document.GetNodeKind(handle) == ManagedHtmlNodeKind.Element;
    }

    public int GetMatchedRuleCount(ManagedHtmlNodeHandle handle) =>
        _document.IsValid(handle) && handle.Index < _matchedRules.Length
            ? _matchedRules[handle.Index] : 0;

    public bool TryCopyCanonicalStyleHash(Span<byte> destination)
    {
        if (!_styleHashAvailable || destination.Length < _styleHash.Length) return false;
        _styleHash.AsSpan().CopyTo(destination);
        return true;
    }

    public bool TryGetExternalStylesheet(int index, out ManagedHtmlNodeHandle node,
                                         Span<uint> hrefDestination, out int hrefLength)
    {
        node = ManagedHtmlNodeHandle.Invalid;
        hrefLength = 0;
        if (index < 0 || index >= _externalCount) return false;
        node = _external[index].Node;
        if (!_document.TryFindAttribute(node, ManagedHtmlAttributeName.Href,
                                        out ManagedHtmlAttributeView href)) return false;
        return _document.TryCopyAttributeValue(node, href.Index, hrefDestination,
                                               out hrefLength, out _);
    }

    private bool DiscoverExternalStylesheets()
    {
        for (int index = 0; index != _document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = NodeHandle(index);
            if (node == ManagedHtmlNodeHandle.Invalid ||
                _document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                _document.GetElementTag(node) != ManagedHtmlTag.Link)
                continue;
            if (!_document.TryFindAttribute(node, ManagedHtmlAttributeName.Rel,
                                            out ManagedHtmlAttributeView rel) ||
                !_document.TryCopyAttributeValue(node, rel.Index, _externalRelScratch,
                                                 out int relLength, out _))
                continue;
            if (!_ContainsAsciiToken(_externalRelScratch.AsSpan(0, relLength), "stylesheet"u8) ||
                !_document.TryFindAttribute(node, ManagedHtmlAttributeName.Href,
                                            out _))
                continue;
            if (_externalCount == _external.Length)
            {
                _failureReason = ManagedCssParseFailureReason.ExternalStylesheetCapacityExceeded;
                return false;
            }
            _external[_externalCount++].Node = node;
        }
        return true;
    }

    private bool ParseEmbeddedStylesheets()
    {
        for (int index = 0; index != _document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = NodeHandle(index);
            if (node == ManagedHtmlNodeHandle.Invalid ||
                _document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                _document.GetElementTag(node) != ManagedHtmlTag.Style)
                continue;
            if (!BeginStylesheet(node.Index)) return false;
            ManagedCssInput input = ManagedCssInput.FromTextChildren(_document, node);
            if (!ParseStylesheet(ref input, _stylesheetCount - 1)) return false;
        }
        return true;
    }

    private bool ParseInlineStyles()
    {
        for (int index = 0; index != _document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = NodeHandle(index);
            if (node == ManagedHtmlNodeHandle.Invalid ||
                _document.GetNodeKind(node) != ManagedHtmlNodeKind.Element)
                continue;
            if (!_document.TryFindAttribute(node, ManagedHtmlAttributeName.Style,
                                            out ManagedHtmlAttributeView styleAttribute))
                continue;
            ManagedHtmlNodeRecord elementRecord = _document.Nodes[node.Index];
            ManagedHtmlAttributeRecord styleRecord = _document.Attributes[
                elementRecord.FirstAttribute + styleAttribute.Index];
            if ((styleRecord.Flags & 1) == 0) continue;
            if (styleRecord.ValueLength > ManagedCssLimits.MaximumValueLength)
            {
                _valueTooLongCount = SaturatingIncrement(_valueTooLongCount);
                ++_declarationsSkipped;
                continue;
            }
            ManagedCssInput input = ManagedCssInput.FromAttribute(_document, node, styleAttribute.Index);
            int declarationStart = _declarationCount;
            if (!ParseDeclarations(ref input, false, out _)) return false;
            int count = _declarationCount - declarationStart;
            _inline[node.Index] = new ManagedCssInlineRecord
            {
                DeclarationIndex = declarationStart,
                DeclarationCount = count
            };
            if (count != 0) ++_inlineStylesParsed;
        }
        return true;
    }

    private bool BeginStylesheet(int rootNodeIndex)
    {
        if (_stylesheetCount == _stylesheets.Length)
        {
            _failureReason = ManagedCssParseFailureReason.StylesheetCapacityExceeded;
            return false;
        }
        _stylesheets[_stylesheetCount++] = new ManagedCssStylesheetRecord
        {
            RootNodeIndex = rootNodeIndex,
            FirstRule = _ruleCount,
            RuleCount = 0,
            SourceOrder = ++_sourceOrder
        };
        if (_stylesheetCount > _stylesheetsPeak) _stylesheetsPeak = _stylesheetCount;
        return true;
    }

    private bool ParseStylesheet(ref ManagedCssInput input, int stylesheetIndex)
    {
        while (true)
        {
            SkipWhitespaceAndComments(ref input);
            if (!input.TryPeek(out _)) return true;
            int selectorLength = 0;
            bool headerTooLong = false;
            bool foundOpen = false;
            while (input.TryRead(out uint scalar))
            {
                if (scalar == '{')
                {
                    foundOpen = true;
                    break;
                }
                if (scalar == ';')
                {
                    ++_malformedRules;
                    ++_rulesSkipped;
                    selectorLength = 0;
                    break;
                }
                if (scalar == '}' || scalar > 0x7F)
                {
                    if (scalar == '}') ++_malformedRules;
                    selectorLength = 0;
                    continue;
                }
                if (selectorLength < _selectorScratch.Length)
                    _selectorScratch[selectorLength++] = (byte)scalar;
                else
                {
                    headerTooLong = true;
                    _selectorTooComplexCount = SaturatingIncrement(_selectorTooComplexCount);
                }
            }
            if (!foundOpen)
            {
                if (selectorLength != 0) { ++_malformedRules; ++_rulesSkipped; }
                return true;
            }
            if (headerTooLong)
            {
                ++_rulesSkipped;
                SkipRuleBody(ref input);
                continue;
            }
            TrimRange(_selectorScratch, selectorLength, out int headerStart, out int headerEnd);
            if (headerStart == headerEnd)
            {
                ++_malformedRules;
                ++_rulesSkipped;
                SkipRuleBody(ref input);
                continue;
            }
            if (_selectorScratch[headerStart] == '@')
            {
                ++_rulesSkipped;
                SkipRuleBody(ref input);
                continue;
            }
            int selectorBefore = _selectorCount;
            int stepBefore = _stepCount;
            int classBefore = _classCount;
            int attributeBefore = _attributeSelectorCount;
            int namesBefore = _nameUsed;
            int groupCount = 0;
            bool logicalRuleSkipped = false;
            int cursor = headerStart;
            while (cursor <= headerEnd)
            {
                int memberStart = cursor;
                while (cursor < headerEnd && _selectorScratch[cursor] != ',') ++cursor;
                TrimRange(_selectorScratch, memberStart, cursor,
                          out int memberBegin, out int memberEnd);
                if (memberBegin != memberEnd)
                {
                    if (++groupCount > ManagedCssLimits.MaximumSelectorsPerRule)
                    {
                        ++_selectorTooComplexCount;
                        _selectorCount = selectorBefore;
                        _stepCount = stepBefore;
                        _classCount = classBefore;
                        _attributeSelectorCount = attributeBefore;
                        _nameUsed = namesBefore;
                        logicalRuleSkipped = true;
                        break;
                    }
                    if (!TryParseSelector(memberBegin, memberEnd, out _))
                    {
                        if (_failureReason != ManagedCssParseFailureReason.None)
                        {
                            _selectorCount = selectorBefore;
                            _stepCount = stepBefore;
                            _classCount = classBefore;
                            _attributeSelectorCount = attributeBefore;
                            _nameUsed = namesBefore;
                            return false;
                        }
                        ++_unsupportedSelectors;
                    }
                }
                if (cursor == headerEnd) break;
                ++cursor;
            }
            if (logicalRuleSkipped)
            {
                ++_rulesSkipped;
                SkipRuleBody(ref input);
                continue;
            }
            if (groupCount == 0 || _selectorCount == selectorBefore)
            {
                _selectorCount = selectorBefore;
                _stepCount = stepBefore;
                _classCount = classBefore;
                _attributeSelectorCount = attributeBefore;
                _nameUsed = namesBefore;
                ++_malformedRules;
                ++_rulesSkipped;
                if (!logicalRuleSkipped) SkipRuleBody(ref input);
                continue;
            }
            int declarationStart = _declarationCount;
            if (!ParseDeclarations(ref input, true, out _))
            {
                _selectorCount = selectorBefore;
                _stepCount = stepBefore;
                _classCount = classBefore;
                _attributeSelectorCount = attributeBefore;
                _nameUsed = namesBefore;
                _declarationCount = declarationStart;
                return false;
            }
            int declarationCount = _declarationCount - declarationStart;
            int sourceOrder = ++_sourceOrder;
            for (int selector = selectorBefore; selector != _selectorCount; ++selector)
            {
                if (_ruleCount == _ruleCapacityLimit)
                {
                    _selectorCount = selectorBefore;
                    _stepCount = stepBefore;
                    _classCount = classBefore;
                    _attributeSelectorCount = attributeBefore;
                    _nameUsed = namesBefore;
                    _declarationCount = declarationStart;
                    _failureReason = ManagedCssParseFailureReason.RuleCapacityExceeded;
                    return false;
                }
                _rules[_ruleCount++] = new ManagedCssRuleRecord
                {
                    SelectorIndex = selector,
                    DeclarationIndex = declarationStart,
                    DeclarationCount = declarationCount,
                    SourceOrder = sourceOrder,
                    Origin = OriginAuthor
                };
            }
            _stylesheets[stylesheetIndex].RuleCount = _ruleCount -
                _stylesheets[stylesheetIndex].FirstRule;
            UpdatePeaks();
        }
    }

    private bool ParseDeclarations(ref ManagedCssInput input, bool inRule,
                                   out bool closed)
    {
        closed = false;
        int declarationsAtStart = _declarationCount;
        int ruleDeclarations = 0;
        while (input.TryPeek(out uint peek))
        {
            SkipWhitespaceAndComments(ref input);
            if (!input.TryPeek(out peek)) break;
            if (inRule && peek == '}')
            {
                input.TryRead(out _);
                closed = true;
                return true;
            }
            int propertyLength = 0;
            bool propertyTooLong = false;
            bool colon = false;
            uint scalar = 0;
            while (input.TryRead(out scalar))
            {
                if (scalar == ':') { colon = true; break; }
                if (scalar == ';' || (inRule && scalar == '}')) break;
                if (scalar <= 0x7F && IsCssWhitespace((byte)scalar)) continue;
                if (propertyLength < _propertyScratch.Length)
                    _propertyScratch[propertyLength++] = ToLowerAscii((byte)scalar);
                else
                    propertyTooLong = true;
            }
            if (!colon)
            {
                ++_malformedDeclarations;
                ++_declarationsSkipped;
                if (scalar == '}') { closed = true; return true; }
                continue;
            }
            int valueLength = 0;
            bool valueClosed = false;
            while (input.TryRead(out scalar))
            {
                if (inRule && scalar == '}' && !valueClosed)
                {
                    valueClosed = true;
                    break;
                }
                if (scalar == ';') break;
                if (scalar == '/' && input.TryPeek(out uint second) && second == '*')
                {
                    input.TryRead(out _);
                    bool terminated = input.SkipComment();
                    if (!terminated) break;
                    if (valueLength != 0 && valueLength < _valueScratch.Length)
                        _valueScratch[valueLength++] = (byte)' ';
                    continue;
                }
                if (scalar > 0x7F)
                {
                    ++_malformedDeclarations;
                    continue;
                }
                if (valueLength < _valueScratch.Length)
                    _valueScratch[valueLength++] = ToLowerAscii((byte)scalar);
                else
                    _valueTooLongCount = SaturatingIncrement(_valueTooLongCount);
            }
            if (valueLength >= _valueScratch.Length)
            {
                ++_declarationsSkipped;
                SkipToDeclarationEnd(ref input, inRule, valueClosed);
                continue;
            }
            TrimRange(_valueScratch, valueLength, out int valueStart, out int valueEnd);
            bool important = RemoveImportant(_valueScratch, ref valueStart, ref valueEnd);
            if (propertyTooLong)
            {
                ++_declarationsSkipped;
                if (valueClosed) { closed = true; return true; }
                continue;
            }
            ManagedCssProperty property = PropertyFromName(_propertyScratch.AsSpan(0, propertyLength));
            if (property == ManagedCssProperty.Count)
            {
                ++_declarationsSkipped;
                if (propertyLength >= 2 && _propertyScratch[0] == '-' && _propertyScratch[1] == '-')
                    ++_customPropertiesIgnored;
                else
                    ++_unknownProperties;
            }
            else if (TryAppendProperty(property, _valueScratch.AsSpan(valueStart, valueEnd - valueStart),
                                       important, ref ruleDeclarations))
            {
                if (important) ++_importantDeclarations;
            }
            else if (_failureReason != ManagedCssParseFailureReason.None)
            {
                return false;
            }
            else
            {
                ++_declarationsSkipped;
            }
            if (valueClosed) { closed = true; return true; }
        }
        if (inRule) ++_malformedRules;
        if (_declarationCount - declarationsAtStart > ManagedCssLimits.MaximumDeclarationsPerRule)
        {
            _failureReason = ManagedCssParseFailureReason.DeclarationCapacityExceeded;
            return false;
        }
        return true;
    }

    private bool TryAppendProperty(ManagedCssProperty property, ReadOnlySpan<byte> value,
                                   bool important, ref int ruleDeclarations)
    {
        if (property == ManagedCssProperty.MarginTop || property == ManagedCssProperty.MarginRight ||
            property == ManagedCssProperty.MarginBottom || property == ManagedCssProperty.MarginLeft ||
            property == ManagedCssProperty.PaddingTop || property == ManagedCssProperty.PaddingRight ||
            property == ManagedCssProperty.PaddingBottom || property == ManagedCssProperty.PaddingLeft)
            return TryAppendValue(property, value, important, ref ruleDeclarations);
        if (property == ManagedCssProperty.Count) return false;
        if (property == (ManagedCssProperty)255) return false;
        if (IsShorthand(property))
        {
            Span<ManagedCssLength> lengths = stackalloc ManagedCssLength[4];
            if (!TryParseLengthList(value, property == (ManagedCssProperty)254, lengths,
                                    out int count))
                return false;
            ManagedCssProperty first = property == (ManagedCssProperty)254
                ? ManagedCssProperty.MarginTop : ManagedCssProperty.PaddingTop;
            for (int index = 0; index != 4; ++index)
            {
                ManagedCssLength length = index switch
                {
                    0 => lengths[0],
                    1 => count == 1 ? lengths[0] : lengths[1],
                    2 => count == 1 ? lengths[0] : count == 2 ? lengths[0] : lengths[2],
                    _ => count == 1 ? lengths[0] : count == 2 ? lengths[1] : count == 3 ? lengths[1] : lengths[3]
                };
                ManagedCssProperty target = (ManagedCssProperty)((int)first + index);
                if (!AppendDeclaration(target, new ManagedCssValue
                    {
                        Kind = ManagedCssValueKind.Length,
                        Unit = length.Unit,
                        Number = length.Value
                    }, important, ref ruleDeclarations)) return false;
            }
            return true;
        }
        ManagedCssValue parsed;
        if (!TryParseValue(property, value, out parsed)) return false;
        return AppendDeclaration(property, parsed, important, ref ruleDeclarations);
    }

    private bool TryAppendValue(ManagedCssProperty property, ReadOnlySpan<byte> value,
                                bool important, ref int ruleDeclarations)
    {
        if (property == ManagedCssProperty.MarginTop || property == ManagedCssProperty.MarginRight ||
            property == ManagedCssProperty.MarginBottom || property == ManagedCssProperty.MarginLeft ||
            property == ManagedCssProperty.PaddingTop || property == ManagedCssProperty.PaddingRight ||
            property == ManagedCssProperty.PaddingBottom || property == ManagedCssProperty.PaddingLeft)
        {
            ManagedCssValue parsed;
            if (!TryParseLength(value, property <= ManagedCssProperty.MarginLeft, out ManagedCssLength length))
                return false;
            parsed = new ManagedCssValue
            {
                Kind = ManagedCssValueKind.Length,
                Unit = length.Unit,
                Number = length.Value
            };
            return AppendDeclaration(property, parsed, important, ref ruleDeclarations);
        }
        return false;
    }

    private bool AppendDeclaration(ManagedCssProperty property, ManagedCssValue value,
                                   bool important, ref int ruleDeclarations)
    {
        if (_declarationCount == _declarations.Length)
        {
            _failureReason = ManagedCssParseFailureReason.DeclarationCapacityExceeded;
            return false;
        }
        if (++ruleDeclarations > ManagedCssLimits.MaximumDeclarationsPerRule)
        {
            _failureReason = ManagedCssParseFailureReason.DeclarationCapacityExceeded;
            return false;
        }
        _declarations[_declarationCount++] = new ManagedCssDeclarationRecord
        {
            Property = property,
            Important = important ? (byte)1 : (byte)0,
            Value = value
        };
        return true;
    }

    private bool TryParseSelector(int start, int end, out int selectorIndex)
    {
        selectorIndex = -1;
        if (_selectorCount == _selectors.Length)
        {
            _failureReason = ManagedCssParseFailureReason.SelectorCapacityExceeded;
            return false;
        }
        int stepStart = _stepCount;
        int classStart = _classCount;
        int attributeStart = _attributeSelectorCount;
        int namesStart = _nameUsed;
        int position = start;
        int steps = 0;
        byte ids = 0;
        byte classes = 0;
        byte types = 0;
        bool haveStep = false;
        byte pendingRelation = RelationDescendant;
        while (position < end)
        {
            bool whitespace = false;
            while (position < end && IsCssWhitespace(_selectorScratch[position]))
            {
                whitespace = true;
                ++position;
            }
            if (position == end) break;
            if (_selectorScratch[position] == '+' || _selectorScratch[position] == '~')
            {
                ++_unsupportedSelectors;
                RollbackSelector(stepStart, classStart, attributeStart, namesStart);
                return false;
            }
            if (_selectorScratch[position] == '>')
            {
                if (!haveStep || pendingRelation == RelationChild)
                {
                    ++_unsupportedSelectors;
                    RollbackSelector(stepStart, classStart, attributeStart, namesStart);
                    return false;
                }
                pendingRelation = RelationChild;
                ++position;
                continue;
            }
            if (haveStep && whitespace && pendingRelation == RelationDescendant)
                pendingRelation = RelationDescendant;
            if (steps == ManagedCssLimits.MaximumSelectorSteps || _stepCount == _steps.Length)
            {
                if (_stepCount == _steps.Length)
                    _failureReason = ManagedCssParseFailureReason.SelectorCapacityExceeded;
                else
                    ++_selectorTooComplexCount;
                RollbackSelector(stepStart, classStart, attributeStart, namesStart);
                return false;
            }
            int currentStep = _stepCount++;
            ManagedCssSelectorStep step = default;
            if (haveStep)
            {
                _steps[currentStep - 1].RelationToNext = pendingRelation;
                pendingRelation = RelationDescendant;
            }
            if (!ParseSimpleSelector(ref position, end, ref step,
                                     ref ids, ref classes, ref types))
            {
                RollbackSelector(stepStart, classStart, attributeStart, namesStart);
                return false;
            }
            _steps[currentStep] = step;
            ++steps;
            haveStep = true;
        }
        if (!haveStep || pendingRelation == RelationChild)
        {
            RollbackSelector(stepStart, classStart, attributeStart, namesStart);
            return false;
        }
        _selectors[_selectorCount] = new ManagedCssSelectorRecord
        {
            StepIndex = stepStart,
            StepCount = (byte)steps,
            IdSpecificity = ids,
            ClassSpecificity = classes,
            TypeSpecificity = types,
            SourceOrder = _sourceOrder
        };
        if (steps > _maximumSelectorDepth) _maximumSelectorDepth = steps;
        selectorIndex = _selectorCount++;
        UpdatePeaks();
        return true;
    }

    private bool ParseSimpleSelector(ref int position, int end,
                                     ref ManagedCssSelectorStep step,
                                     ref byte ids, ref byte classes, ref byte types)
    {
        bool any = false;
        if (position < end && _selectorScratch[position] == '*')
        {
            ++position;
            any = true;
        }
        else if (position < end && IsCssIdentStart(_selectorScratch[position]))
        {
            int begin = position;
            while (position < end && IsCssIdent(_selectorScratch[position])) ++position;
            int length = position - begin;
            if (length > ManagedCssLimits.MaximumSelectorNameLength)
            {
                ++_selectorTooComplexCount;
                _failureReason = ManagedCssParseFailureReason.SelectorNameTooLong;
                return false;
            }
            if (!TryCopySelectorName(_selectorScratch.AsSpan(begin, length), true,
                                     out int offset)) return false;
            ManagedHtmlTag tag = ManagedHtmlNames.Tag(_selectorScratch.AsSpan(begin, length));
            step.KnownTag = tag;
            step.TagNameOffset = tag == ManagedHtmlTag.Unknown ? offset : 0;
            step.TagNameLength = tag == ManagedHtmlTag.Unknown ? (byte)length : (byte)0;
            step.Flags |= StepHasTag;
            ++types;
            any = true;
        }
        while (position < end)
        {
            byte current = _selectorScratch[position];
            if (current == '.' || current == '#')
            {
                bool id = current == '#';
                ++position;
                int begin = position;
                while (position < end && IsCssIdent(_selectorScratch[position])) ++position;
                int length = position - begin;
                if (length == 0 || length > ManagedCssLimits.MaximumSelectorNameLength)
                {
                    _failureReason = ManagedCssParseFailureReason.SelectorNameTooLong;
                    return false;
                }
                if (id)
                {
                    if ((step.Flags & StepHasId) != 0 || !TryCopySelectorName(
                            _selectorScratch.AsSpan(begin, length), false, out int offset)) return false;
                    step.IdOffset = offset;
                    step.IdLength = (byte)length;
                    step.Flags |= StepHasId;
                    ++ids;
                }
                else
                {
                    if (step.ClassCount == ManagedCssLimits.MaximumClassesPerStep ||
                        _classCount == _classes.Length)
                    {
                        ++_selectorTooComplexCount;
                        return false;
                    }
                    if (!TryCopySelectorName(
                            _selectorScratch.AsSpan(begin, length), false, out int offset)) return false;
                    _classes[_classCount++] = new ManagedCssClassRecord
                    {
                        NameOffset = offset,
                        NameLength = (byte)length
                    };
                    ++step.ClassCount;
                    step.FirstClass = _classCount - step.ClassCount;
                    step.Flags |= StepHasClasses;
                    ++classes;
                }
                any = true;
                continue;
            }
            if (current == '[')
            {
                if (!ParseAttributeSelector(ref position, end, ref step, ref classes)) return false;
                any = true;
                continue;
            }
            if (current == ':')
            {
                ++position;
                int begin = position;
                while (position < end && IsCssIdent(_selectorScratch[position])) ++position;
                ReadOnlySpan<byte> pseudo = _selectorScratch.AsSpan(begin, position - begin);
                if (pseudo.SequenceEqual("root"u8)) step.Flags |= StepHasRoot;
                else if (pseudo.SequenceEqual("first-child"u8)) step.Flags |= StepHasFirstChild;
                else if (pseudo.SequenceEqual("last-child"u8)) step.Flags |= StepHasLastChild;
                else
                {
                    ++_unsupportedSelectors;
                    return false;
                }
                ++classes;
                any = true;
                continue;
            }
            break;
        }
        return any;
    }

    private bool ParseAttributeSelector(ref int position, int end,
                                        ref ManagedCssSelectorStep step,
                                        ref byte classes)
    {
        if (step.AttributeCount == ManagedCssLimits.MaximumAttributesPerStep) return false;
        if (_attributeSelectorCount == _attributeSelectors.Length)
        {
            _failureReason = ManagedCssParseFailureReason.SelectorCapacityExceeded;
            return false;
        }
        ++position;
        while (position < end && IsCssWhitespace(_selectorScratch[position])) ++position;
        int nameStart = position;
        while (position < end && IsCssIdent(_selectorScratch[position])) ++position;
        int nameLength = position - nameStart;
        if (nameLength == 0 || nameLength > ManagedCssLimits.MaximumSelectorNameLength) return false;
        if (!TryCopySelectorName(_selectorScratch.AsSpan(nameStart, nameLength), true,
                                 out int nameOffset)) return false;
        while (position < end && IsCssWhitespace(_selectorScratch[position])) ++position;
        bool hasValue = false;
        int valueOffset = 0;
        int valueLength = 0;
        if (position < end && _selectorScratch[position] == '=')
        {
            hasValue = true;
            ++position;
            while (position < end && IsCssWhitespace(_selectorScratch[position])) ++position;
            int valueStart = position;
            while (position < end && _selectorScratch[position] != ']') ++position;
            int valueEnd = position;
            TrimRange(_selectorScratch, valueStart, valueEnd, out valueStart, out valueEnd);
            if (valueEnd - valueStart > ManagedCssLimits.MaximumSelectorNameLength ||
                valueEnd == valueStart) return false;
            if ((_selectorScratch[valueStart] == '\'' || _selectorScratch[valueStart] == '"') &&
                valueEnd - valueStart >= 2 &&
                _selectorScratch[valueEnd - 1] == _selectorScratch[valueStart])
            {
                ++valueStart;
                --valueEnd;
            }
            valueLength = valueEnd - valueStart;
            if (!TryCopySelectorName(_selectorScratch.AsSpan(valueStart, valueLength),
                                     false, out valueOffset)) return false;
        }
        if (position >= end || _selectorScratch[position] != ']') return false;
        ++position;
        if (step.AttributeCount == 0) step.FirstAttribute = _attributeSelectorCount;
        _attributeSelectors[_attributeSelectorCount++] = new ManagedCssAttributeSelector
        {
            NameOffset = nameOffset,
            NameLength = (byte)nameLength,
            ValueOffset = valueOffset,
            ValueLength = (byte)valueLength,
            Flags = hasValue ? AttributeHasValue : (byte)0
        };
        ++step.AttributeCount;
        step.Flags |= StepHasAttributes;
        ++classes;
        return true;
    }

    private bool TryCopySelectorName(ReadOnlySpan<byte> source, bool lower,
                                     out int offset)
    {
        offset = 0;
        if (source.Length > ManagedCssLimits.MaximumSelectorNameLength ||
            source.Length > _selectorNames.Length - _nameUsed)
        {
            _failureReason = ManagedCssParseFailureReason.SelectorNameTooLong;
            return false;
        }
        offset = _nameUsed;
        for (int index = 0; index != source.Length; ++index)
            _selectorNames[_nameUsed++] = lower ? ToLowerAscii(source[index]) : source[index];
        return true;
    }

    private void RollbackSelector(int stepStart, int classStart,
                                  int attributeStart, int namesStart)
    {
        _stepCount = stepStart;
        _classCount = classStart;
        _attributeSelectorCount = attributeStart;
        _nameUsed = namesStart;
    }

    private bool ComputeStyles()
    {
        if (_document.NodeCount > _computed.Length)
        {
            _failureReason = ManagedCssParseFailureReason.InvalidDocument;
            return false;
        }
        for (int index = 0; index != _document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle element = NodeHandle(index);
            if (element == ManagedHtmlNodeHandle.Invalid ||
                _document.GetNodeKind(element) != ManagedHtmlNodeKind.Element) continue;
            ManagedComputedStyle style = InitialStyle(_document.GetElementTag(element));
            _winners.AsSpan().Clear();
            int matches = 0;
            for (int ruleIndex = 0; ruleIndex != _ruleCount; ++ruleIndex)
            {
                ManagedCssRuleRecord rule = _rules[ruleIndex];
                if (!MatchSelector(_selectors[rule.SelectorIndex], element)) continue;
                ++matches;
                ++_selectorMatches;
                for (int declaration = 0; declaration != rule.DeclarationCount; ++declaration)
                {
                    ManagedCssDeclarationRecord record =
                        _declarations[rule.DeclarationIndex + declaration];
                    ApplyCandidate(ref _winners[(int)record.Property], record.Value,
                                   record.Important != 0, OriginAuthor,
                                   _selectors[rule.SelectorIndex], rule.SourceOrder,
                                   rule.DeclarationIndex + declaration);
                }
            }
            ManagedCssInlineRecord inline = _inline[index];
            for (int declaration = 0; declaration != inline.DeclarationCount; ++declaration)
            {
                ManagedCssDeclarationRecord record =
                    _declarations[inline.DeclarationIndex + declaration];
                ApplyCandidate(ref _winners[(int)record.Property], record.Value,
                               record.Important != 0, OriginInline,
                               new ManagedCssSelectorRecord { IdSpecificity = 1 },
                               int.MaxValue, inline.DeclarationIndex + declaration);
            }
            ApplyWinners(ref style, element);
            _computed[index] = style;
            _matchedRules[index] = matches;
            ++_elementsStyled;
            if (_elementsStyled > _computedPeak) _computedPeak = _elementsStyled;
        }
        return true;
    }

    private void ApplyCandidate(ref ManagedCssCascadeCandidate current,
                                ManagedCssValue value, bool important, byte origin,
                                ManagedCssSelectorRecord selector, int sourceOrder,
                                int declarationOrder)
    {
        ManagedCssCascadeCandidate candidate = new()
        {
            Present = 1,
            Important = important ? (byte)1 : (byte)0,
            Origin = origin,
            IdSpecificity = selector.IdSpecificity,
            ClassSpecificity = selector.ClassSpecificity,
            TypeSpecificity = selector.TypeSpecificity,
            SourceOrder = sourceOrder,
            DeclarationOrder = declarationOrder,
            Value = value
        };
        if (current.Present == 0 || IsCandidateBetter(candidate, current)) current = candidate;
    }

    private static bool IsCandidateBetter(ManagedCssCascadeCandidate candidate,
                                           ManagedCssCascadeCandidate current)
    {
        if (candidate.Important != current.Important)
            return candidate.Important > current.Important;
        if (candidate.Origin != current.Origin) return candidate.Origin > current.Origin;
        if (candidate.IdSpecificity != current.IdSpecificity)
            return candidate.IdSpecificity > current.IdSpecificity;
        if (candidate.ClassSpecificity != current.ClassSpecificity)
            return candidate.ClassSpecificity > current.ClassSpecificity;
        if (candidate.TypeSpecificity != current.TypeSpecificity)
            return candidate.TypeSpecificity > current.TypeSpecificity;
        if (candidate.SourceOrder != current.SourceOrder)
            return candidate.SourceOrder > current.SourceOrder;
        return candidate.DeclarationOrder > current.DeclarationOrder;
    }

    private void ApplyWinners(ref ManagedComputedStyle style,
                              ManagedHtmlNodeHandle element)
    {
        ManagedHtmlNodeHandle parent = _document.GetParent(element);
        bool hasParent = parent != ManagedHtmlNodeHandle.Invalid &&
                         parent.Index < _computed.Length &&
                         _document.GetNodeKind(parent) == ManagedHtmlNodeKind.Element;
        ManagedComputedStyle parentStyle = hasParent ? _computed[parent.Index] : default;
        for (int property = 0; property != (int)ManagedCssProperty.Count; ++property)
        {
            ManagedCssCascadeCandidate winner = _winners[property];
            ManagedCssProperty cssProperty = (ManagedCssProperty)property;
            if (winner.Present != 0)
            {
                style.SpecifiedMask |= 1UL << property;
                if (winner.Important != 0) style.ImportantMask |= 1UL << property;
                ApplyValue(ref style, cssProperty, winner.Value, hasParent, parentStyle);
            }
            else if (IsInherited(cssProperty) && hasParent)
            {
                CopyProperty(ref style, cssProperty, parentStyle);
                style.InheritedMask |= 1UL << property;
                ++_inheritedAssignments;
            }
        }
    }

    private static bool IsInherited(ManagedCssProperty property) =>
        property == ManagedCssProperty.Color || property == ManagedCssProperty.FontSize ||
        property == ManagedCssProperty.FontWeight || property == ManagedCssProperty.FontStyle ||
        property == ManagedCssProperty.TextAlign || property == ManagedCssProperty.Visibility ||
        property == ManagedCssProperty.WhiteSpace;

    private static void CopyProperty(ref ManagedComputedStyle target,
                                     ManagedCssProperty property,
                                     ManagedComputedStyle source)
    {
        switch (property)
        {
            case ManagedCssProperty.Color: target.ColorValue = source.ColorValue; break;
            case ManagedCssProperty.FontSize: target.FontSizeValue = source.FontSizeValue; break;
            case ManagedCssProperty.FontWeight: target.FontWeightValue = source.FontWeightValue; break;
            case ManagedCssProperty.FontStyle: target.FontStyleValue = source.FontStyleValue; break;
            case ManagedCssProperty.TextAlign: target.TextAlignValue = source.TextAlignValue; break;
            case ManagedCssProperty.Visibility: target.VisibilityValue = source.VisibilityValue; break;
            case ManagedCssProperty.WhiteSpace: target.WhiteSpaceValue = source.WhiteSpaceValue; break;
        }
    }

    private static void ApplyValue(ref ManagedComputedStyle style,
                                   ManagedCssProperty property, ManagedCssValue value,
                                   bool hasParent, ManagedComputedStyle parent)
    {
        if (value.Kind == ManagedCssValueKind.Keyword && value.Keyword == ManagedCssKeyword.Inherit)
        {
            if (hasParent) CopyProperty(ref style, property, parent);
            return;
        }
        if (value.Kind == ManagedCssValueKind.Keyword && value.Keyword == ManagedCssKeyword.Initial)
        {
            ApplyInitial(ref style, property);
            return;
        }
        switch (property)
        {
            case ManagedCssProperty.Display: style.DisplayValue = DisplayFromValue(value); break;
            case ManagedCssProperty.Visibility: style.VisibilityValue = VisibilityFromValue(value); break;
            case ManagedCssProperty.Color: if (value.Kind == ManagedCssValueKind.Color) style.ColorValue = value.Color; break;
            case ManagedCssProperty.BackgroundColor: if (value.Kind == ManagedCssValueKind.Color) style.BackgroundColorValue = value.Color; break;
            case ManagedCssProperty.FontSize: style.FontSizeValue = LengthFromValue(value); break;
            case ManagedCssProperty.FontWeight: style.FontWeightValue = NumberFromValue(value, 400); break;
            case ManagedCssProperty.FontStyle: style.FontStyleValue = value.Keyword == ManagedCssKeyword.Italic ? ManagedCssFontStyle.Italic : ManagedCssFontStyle.Normal; break;
            case ManagedCssProperty.TextAlign: style.TextAlignValue = TextAlignFromValue(value); break;
            case ManagedCssProperty.WhiteSpace: style.WhiteSpaceValue = WhiteSpaceFromValue(value); break;
            case ManagedCssProperty.Width: style.WidthValue = LengthFromValue(value); break;
            case ManagedCssProperty.Height: style.HeightValue = LengthFromValue(value); break;
            case ManagedCssProperty.MinWidth: style.MinWidthValue = LengthFromValue(value); break;
            case ManagedCssProperty.MinHeight: style.MinHeightValue = LengthFromValue(value); break;
            case ManagedCssProperty.MaxWidth: style.MaxWidthValue = LengthFromValue(value); break;
            case ManagedCssProperty.MaxHeight: style.MaxHeightValue = LengthFromValue(value); break;
            case ManagedCssProperty.MarginTop: style.MarginTopValue = LengthFromValue(value); break;
            case ManagedCssProperty.MarginRight: style.MarginRightValue = LengthFromValue(value); break;
            case ManagedCssProperty.MarginBottom: style.MarginBottomValue = LengthFromValue(value); break;
            case ManagedCssProperty.MarginLeft: style.MarginLeftValue = LengthFromValue(value); break;
            case ManagedCssProperty.PaddingTop: style.PaddingTopValue = LengthFromValue(value); break;
            case ManagedCssProperty.PaddingRight: style.PaddingRightValue = LengthFromValue(value); break;
            case ManagedCssProperty.PaddingBottom: style.PaddingBottomValue = LengthFromValue(value); break;
            case ManagedCssProperty.PaddingLeft: style.PaddingLeftValue = LengthFromValue(value); break;
            case ManagedCssProperty.BorderWidth: style.BorderWidthValue = LengthFromValue(value); break;
            case ManagedCssProperty.BorderStyle: style.BorderStyleValue = BorderStyleFromValue(value); break;
            case ManagedCssProperty.BorderColor: if (value.Kind == ManagedCssValueKind.Color) style.BorderColorValue = value.Color; break;
            case ManagedCssProperty.Position: style.PositionValue = PositionFromValue(value); break;
            case ManagedCssProperty.Top: style.TopValue = LengthFromValue(value); break;
            case ManagedCssProperty.Right: style.RightValue = LengthFromValue(value); break;
            case ManagedCssProperty.Bottom: style.BottomValue = LengthFromValue(value); break;
            case ManagedCssProperty.Left: style.LeftValue = LengthFromValue(value); break;
            case ManagedCssProperty.Overflow: style.OverflowValue = OverflowFromValue(value); break;
            case ManagedCssProperty.OverflowX: style.OverflowXValue = OverflowFromValue(value); break;
            case ManagedCssProperty.OverflowY: style.OverflowYValue = OverflowFromValue(value); break;
            case ManagedCssProperty.Opacity: style.OpacityValue = NumberFromValue(value, 10_000); break;
            case ManagedCssProperty.ZIndex:
                style.ZIndexAutoValue = value.Kind == ManagedCssValueKind.Keyword && value.Keyword == ManagedCssKeyword.Auto;
                style.ZIndexValue = NumberFromValue(value, 0);
                break;
        }
    }

    private static ManagedComputedStyle InitialStyle(ManagedHtmlTag tag)
    {
        ManagedComputedStyle style = default;
        style.DisplayValue = DefaultDisplay(tag);
        style.VisibilityValue = ManagedCssVisibility.Visible;
        style.ColorValue = 0xFF000000U;
        style.BackgroundColorValue = 0;
        style.FontSizeValue = new ManagedCssLength(1600, ManagedCssLengthUnit.Px);
        style.FontWeightValue = 400;
        style.FontStyleValue = ManagedCssFontStyle.Normal;
        style.TextAlignValue = ManagedCssTextAlign.Left;
        style.WhiteSpaceValue = ManagedCssWhiteSpace.Normal;
        style.WidthValue = AutoLength();
        style.HeightValue = AutoLength();
        style.MinWidthValue = ZeroLength();
        style.MinHeightValue = ZeroLength();
        style.MaxWidthValue = AutoLength();
        style.MaxHeightValue = AutoLength();
        style.MarginTopValue = ZeroLength();
        style.MarginRightValue = ZeroLength();
        style.MarginBottomValue = ZeroLength();
        style.MarginLeftValue = ZeroLength();
        style.PaddingTopValue = ZeroLength();
        style.PaddingRightValue = ZeroLength();
        style.PaddingBottomValue = ZeroLength();
        style.PaddingLeftValue = ZeroLength();
        style.BorderWidthValue = ZeroLength();
        style.BorderStyleValue = ManagedCssBorderStyle.None;
        style.BorderColorValue = 0xFF000000U;
        style.PositionValue = ManagedCssPosition.Static;
        style.TopValue = AutoLength();
        style.RightValue = AutoLength();
        style.BottomValue = AutoLength();
        style.LeftValue = AutoLength();
        style.OverflowValue = ManagedCssOverflow.Visible;
        style.OverflowXValue = ManagedCssOverflow.Visible;
        style.OverflowYValue = ManagedCssOverflow.Visible;
        style.OpacityValue = 10_000;
        style.ZIndexValue = 0;
        style.ZIndexAutoValue = true;
        return style;
    }

    private static void ApplyInitial(ref ManagedComputedStyle style, ManagedCssProperty property)
    {
        ManagedComputedStyle initial = InitialStyle(ManagedHtmlTag.Unknown);
        switch (property)
        {
            case ManagedCssProperty.Display: style.DisplayValue = initial.DisplayValue; break;
            case ManagedCssProperty.Visibility: style.VisibilityValue = initial.VisibilityValue; break;
            case ManagedCssProperty.Color: style.ColorValue = initial.ColorValue; break;
            case ManagedCssProperty.BackgroundColor: style.BackgroundColorValue = initial.BackgroundColorValue; break;
            case ManagedCssProperty.FontSize: style.FontSizeValue = initial.FontSizeValue; break;
            case ManagedCssProperty.FontWeight: style.FontWeightValue = initial.FontWeightValue; break;
            case ManagedCssProperty.FontStyle: style.FontStyleValue = initial.FontStyleValue; break;
            case ManagedCssProperty.TextAlign: style.TextAlignValue = initial.TextAlignValue; break;
            case ManagedCssProperty.WhiteSpace: style.WhiteSpaceValue = initial.WhiteSpaceValue; break;
            case ManagedCssProperty.Width: style.WidthValue = initial.WidthValue; break;
            case ManagedCssProperty.Height: style.HeightValue = initial.HeightValue; break;
            case ManagedCssProperty.MinWidth: style.MinWidthValue = initial.MinWidthValue; break;
            case ManagedCssProperty.MinHeight: style.MinHeightValue = initial.MinHeightValue; break;
            case ManagedCssProperty.MaxWidth: style.MaxWidthValue = initial.MaxWidthValue; break;
            case ManagedCssProperty.MaxHeight: style.MaxHeightValue = initial.MaxHeightValue; break;
            case ManagedCssProperty.MarginTop: style.MarginTopValue = initial.MarginTopValue; break;
            case ManagedCssProperty.MarginRight: style.MarginRightValue = initial.MarginRightValue; break;
            case ManagedCssProperty.MarginBottom: style.MarginBottomValue = initial.MarginBottomValue; break;
            case ManagedCssProperty.MarginLeft: style.MarginLeftValue = initial.MarginLeftValue; break;
            case ManagedCssProperty.PaddingTop: style.PaddingTopValue = initial.PaddingTopValue; break;
            case ManagedCssProperty.PaddingRight: style.PaddingRightValue = initial.PaddingRightValue; break;
            case ManagedCssProperty.PaddingBottom: style.PaddingBottomValue = initial.PaddingBottomValue; break;
            case ManagedCssProperty.PaddingLeft: style.PaddingLeftValue = initial.PaddingLeftValue; break;
            case ManagedCssProperty.BorderWidth: style.BorderWidthValue = initial.BorderWidthValue; break;
            case ManagedCssProperty.BorderStyle: style.BorderStyleValue = initial.BorderStyleValue; break;
            case ManagedCssProperty.BorderColor: style.BorderColorValue = initial.BorderColorValue; break;
            case ManagedCssProperty.Position: style.PositionValue = initial.PositionValue; break;
            case ManagedCssProperty.Top: style.TopValue = initial.TopValue; break;
            case ManagedCssProperty.Right: style.RightValue = initial.RightValue; break;
            case ManagedCssProperty.Bottom: style.BottomValue = initial.BottomValue; break;
            case ManagedCssProperty.Left: style.LeftValue = initial.LeftValue; break;
            case ManagedCssProperty.Overflow: style.OverflowValue = initial.OverflowValue; break;
            case ManagedCssProperty.OverflowX: style.OverflowXValue = initial.OverflowXValue; break;
            case ManagedCssProperty.OverflowY: style.OverflowYValue = initial.OverflowYValue; break;
            case ManagedCssProperty.Opacity: style.OpacityValue = initial.OpacityValue; break;
            case ManagedCssProperty.ZIndex: style.ZIndexValue = initial.ZIndexValue; style.ZIndexAutoValue = true; break;
        }
    }

    private bool MatchSelector(ManagedCssSelectorRecord selector,
                               ManagedHtmlNodeHandle element)
    {
        ++_matchGeneration;
        if (_matchGeneration == int.MaxValue)
        {
            _matchVisited.AsSpan().Clear();
            _matchGeneration = 1;
        }
        int stateCount = 1;
        _matchStates[0] = new ManagedCssMatchState { Step = selector.StepCount - 1, Node = element.Index };
        while (stateCount != 0)
        {
            ManagedCssMatchState state = _matchStates[--stateCount];
            if (state.Step < 0 || state.Node < 0 || state.Node >= _document.NodeCount) continue;
            int visited = state.Step * ManagedCssLimits.MaximumComputedStyleCapacity + state.Node;
            if (_matchVisited[visited] == _matchGeneration) continue;
            _matchVisited[visited] = _matchGeneration;
            ManagedHtmlNodeHandle node = NodeHandle(state.Node);
            if (!MatchStep(_steps[selector.StepIndex + state.Step], node)) continue;
            if (state.Step == 0) return true;
            byte relation = _steps[selector.StepIndex + state.Step - 1].RelationToNext;
            if (relation == RelationChild)
            {
                ManagedHtmlNodeHandle parent = _document.GetParent(node);
                if (parent != ManagedHtmlNodeHandle.Invalid && stateCount < _matchStates.Length)
                    _matchStates[stateCount++] = new ManagedCssMatchState
                    { Step = state.Step - 1, Node = parent.Index };
            }
            else
            {
                for (ManagedHtmlNodeHandle parent = _document.GetParent(node);
                     parent != ManagedHtmlNodeHandle.Invalid;
                     parent = _document.GetParent(parent))
                {
                    if (stateCount == _matchStates.Length) break;
                    _matchStates[stateCount++] = new ManagedCssMatchState
                    { Step = state.Step - 1, Node = parent.Index };
                }
            }
        }
        return false;
    }

    private bool MatchStep(ManagedCssSelectorStep step, ManagedHtmlNodeHandle element)
    {
        if (_document.GetNodeKind(element) != ManagedHtmlNodeKind.Element) return false;
        if ((step.Flags & StepHasTag) != 0)
        {
            ManagedHtmlTag actual = _document.GetElementTag(element);
            if (step.KnownTag != ManagedHtmlTag.Unknown)
            {
                if (actual != step.KnownTag) return false;
            }
            else if (!TryTagNameEquals(element, _selectorNames.AsSpan(
                         step.TagNameOffset, step.TagNameLength))) return false;
        }
        if ((step.Flags & StepHasId) != 0 && !TryAttributeEquals(element,
                ManagedHtmlAttributeName.Id, _selectorNames.AsSpan(step.IdOffset, step.IdLength))) return false;
        for (int index = 0; index != step.ClassCount; ++index)
        {
            ManagedCssClassRecord classRecord = _classes[step.FirstClass + index];
            if (!ClassTokenExists(element, _selectorNames.AsSpan(classRecord.NameOffset,
                                                                  classRecord.NameLength))) return false;
        }
        for (int index = 0; index != step.AttributeCount; ++index)
        {
            ManagedCssAttributeSelector attribute = _attributeSelectors[step.FirstAttribute + index];
            ReadOnlySpan<byte> name = _selectorNames.AsSpan(attribute.NameOffset, attribute.NameLength);
            if (!_document.TryFindAttribute(element, name, out ManagedHtmlAttributeView view)) return false;
            if ((attribute.Flags & AttributeHasValue) != 0 &&
                !TryAttributeEquals(element, view.Index, _selectorNames.AsSpan(
                    attribute.ValueOffset, attribute.ValueLength))) return false;
        }
        if ((step.Flags & StepHasRoot) != 0 && _document.DocumentElement != element) return false;
        if ((step.Flags & StepHasFirstChild) != 0 && !IsFirstElementChild(element)) return false;
        if ((step.Flags & StepHasLastChild) != 0 && !IsLastElementChild(element)) return false;
        return true;
    }

    private bool ClassTokenExists(ManagedHtmlNodeHandle element, ReadOnlySpan<byte> expected)
    {
        if (!_document.TryFindAttribute(element, ManagedHtmlAttributeName.Class,
                                        out ManagedHtmlAttributeView view) ||
            !_document.TryCopyAttributeValue(element, view.Index, _classScratch,
                                             out int length, out _)) return false;
        int position = 0;
        while (position < length)
        {
            while (position < length && _classScratch[position] <= 0x7F &&
                   IsCssWhitespace((byte)_classScratch[position])) ++position;
            int begin = position;
            while (position < length && (_classScratch[position] > 0x7F ||
                                         !IsCssWhitespace((byte)_classScratch[position]))) ++position;
            if (position - begin == expected.Length &&
                ScalarSpanEquals(_classScratch.AsSpan(begin, position - begin), expected)) return true;
        }
        return false;
    }

    private bool TryAttributeEquals(ManagedHtmlNodeHandle element,
                                    ManagedHtmlAttributeName name,
                                    ReadOnlySpan<byte> expected)
    {
        if (!_document.TryFindAttribute(element, name, out ManagedHtmlAttributeView view)) return false;
        return TryAttributeEquals(element, view.Index, expected);
    }

    private bool TryAttributeEquals(ManagedHtmlNodeHandle element, int index,
                                    ReadOnlySpan<byte> expected)
    {
        if (!_document.TryCopyAttributeValue(element, index, _attributeScratch,
                                             out int length, out bool hasValue) || !hasValue ||
            length != expected.Length) return false;
        for (int position = 0; position != length; ++position)
            if (_attributeScratch[position] > 0x7F ||
                (byte)_attributeScratch[position] != expected[position]) return false;
        return true;
    }

    private bool TryTagNameEquals(ManagedHtmlNodeHandle element,
                                  ReadOnlySpan<byte> expected)
    {
        if (!_document.TryCopyTagName(element, _tagScratch, out int length) ||
            length != expected.Length) return false;
        for (int index = 0; index != length; ++index)
            if (ToLowerAscii(_tagScratch[index]) != expected[index]) return false;
        return true;
    }

    private bool IsFirstElementChild(ManagedHtmlNodeHandle element)
    {
        for (ManagedHtmlNodeHandle previous = _document.GetPreviousSibling(element);
             previous != ManagedHtmlNodeHandle.Invalid;
             previous = _document.GetPreviousSibling(previous))
            if (_document.GetNodeKind(previous) == ManagedHtmlNodeKind.Element) return false;
        return true;
    }

    private bool IsLastElementChild(ManagedHtmlNodeHandle element)
    {
        for (ManagedHtmlNodeHandle next = _document.GetNextSibling(element);
             next != ManagedHtmlNodeHandle.Invalid;
             next = _document.GetNextSibling(next))
            if (_document.GetNodeKind(next) == ManagedHtmlNodeKind.Element) return false;
        return true;
    }

    private bool ComputeStyleHash()
    {
        _hash.Reset();
        Span<byte> bytes = stackalloc byte[8];
        for (int index = 0; index != _document.NodeCount; ++index)
        {
            if (_document.GetNodeKind(NodeHandle(index)) != ManagedHtmlNodeKind.Element) continue;
            ManagedComputedStyle style = _computed[index];
            AppendHashUInt32((uint)index, bytes);
            AppendHashUInt64(style.SpecifiedMask, bytes);
            AppendHashUInt64(style.InheritedMask, bytes);
            AppendHashUInt64(style.ImportantMask, bytes);
            AppendHashUInt32((uint)style.DisplayValue, bytes);
            AppendHashUInt32((uint)style.VisibilityValue, bytes);
            AppendHashUInt32(style.ColorValue, bytes);
            AppendHashUInt32(style.BackgroundColorValue, bytes);
            AppendHashLength(style.FontSizeValue, bytes);
            AppendHashUInt32((uint)style.FontWeightValue, bytes);
            AppendHashUInt32((uint)style.FontStyleValue, bytes);
            AppendHashUInt32((uint)style.TextAlignValue, bytes);
            AppendHashUInt32((uint)style.WhiteSpaceValue, bytes);
            AppendHashLength(style.WidthValue, bytes);
            AppendHashLength(style.HeightValue, bytes);
            AppendHashLength(style.MinWidthValue, bytes);
            AppendHashLength(style.MinHeightValue, bytes);
            AppendHashLength(style.MaxWidthValue, bytes);
            AppendHashLength(style.MaxHeightValue, bytes);
            AppendHashLength(style.MarginTopValue, bytes);
            AppendHashLength(style.MarginRightValue, bytes);
            AppendHashLength(style.MarginBottomValue, bytes);
            AppendHashLength(style.MarginLeftValue, bytes);
            AppendHashLength(style.PaddingTopValue, bytes);
            AppendHashLength(style.PaddingRightValue, bytes);
            AppendHashLength(style.PaddingBottomValue, bytes);
            AppendHashLength(style.PaddingLeftValue, bytes);
            AppendHashLength(style.BorderWidthValue, bytes);
            AppendHashUInt32((uint)style.BorderStyleValue, bytes);
            AppendHashUInt32(style.BorderColorValue, bytes);
            AppendHashUInt32((uint)style.PositionValue, bytes);
            AppendHashLength(style.TopValue, bytes);
            AppendHashLength(style.RightValue, bytes);
            AppendHashLength(style.BottomValue, bytes);
            AppendHashLength(style.LeftValue, bytes);
            AppendHashUInt32((uint)style.OverflowValue, bytes);
            AppendHashUInt32((uint)style.OverflowXValue, bytes);
            AppendHashUInt32((uint)style.OverflowYValue, bytes);
            AppendHashUInt32((uint)style.OpacityValue, bytes);
            AppendHashUInt32((uint)style.ZIndexValue, bytes);
            AppendHashUInt32(style.ZIndexAutoValue ? 1U : 0U, bytes);
        }
        return _hash.TryFinalize(_styleHash) && (_styleHashAvailable = true);
    }

    private void AppendHashUInt32(uint value, Span<byte> scratch)
    {
        scratch[0] = (byte)(value >> 24); scratch[1] = (byte)(value >> 16);
        scratch[2] = (byte)(value >> 8); scratch[3] = (byte)value;
        _hash.Append(scratch[..4]);
    }

    private void AppendHashUInt64(ulong value, Span<byte> scratch)
    {
        for (int index = 0; index != 8; ++index)
            scratch[index] = (byte)(value >> (56 - index * 8));
        _hash.Append(scratch);
    }

    private void AppendHashLength(ManagedCssLength length, Span<byte> scratch)
    {
        AppendHashUInt32((uint)length.Value, scratch);
        AppendHashUInt32((uint)length.Unit, scratch);
    }

    private static ManagedCssProperty PropertyFromName(ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("display"u8)) return ManagedCssProperty.Display;
        if (name.SequenceEqual("visibility"u8)) return ManagedCssProperty.Visibility;
        if (name.SequenceEqual("color"u8)) return ManagedCssProperty.Color;
        if (name.SequenceEqual("background-color"u8)) return ManagedCssProperty.BackgroundColor;
        if (name.SequenceEqual("font-size"u8)) return ManagedCssProperty.FontSize;
        if (name.SequenceEqual("font-weight"u8)) return ManagedCssProperty.FontWeight;
        if (name.SequenceEqual("font-style"u8)) return ManagedCssProperty.FontStyle;
        if (name.SequenceEqual("text-align"u8)) return ManagedCssProperty.TextAlign;
        if (name.SequenceEqual("white-space"u8)) return ManagedCssProperty.WhiteSpace;
        if (name.SequenceEqual("width"u8)) return ManagedCssProperty.Width;
        if (name.SequenceEqual("height"u8)) return ManagedCssProperty.Height;
        if (name.SequenceEqual("min-width"u8)) return ManagedCssProperty.MinWidth;
        if (name.SequenceEqual("min-height"u8)) return ManagedCssProperty.MinHeight;
        if (name.SequenceEqual("max-width"u8)) return ManagedCssProperty.MaxWidth;
        if (name.SequenceEqual("max-height"u8)) return ManagedCssProperty.MaxHeight;
        if (name.SequenceEqual("margin-top"u8)) return ManagedCssProperty.MarginTop;
        if (name.SequenceEqual("margin-right"u8)) return ManagedCssProperty.MarginRight;
        if (name.SequenceEqual("margin-bottom"u8)) return ManagedCssProperty.MarginBottom;
        if (name.SequenceEqual("margin-left"u8)) return ManagedCssProperty.MarginLeft;
        if (name.SequenceEqual("padding-top"u8)) return ManagedCssProperty.PaddingTop;
        if (name.SequenceEqual("padding-right"u8)) return ManagedCssProperty.PaddingRight;
        if (name.SequenceEqual("padding-bottom"u8)) return ManagedCssProperty.PaddingBottom;
        if (name.SequenceEqual("padding-left"u8)) return ManagedCssProperty.PaddingLeft;
        if (name.SequenceEqual("margin"u8)) return (ManagedCssProperty)254;
        if (name.SequenceEqual("padding"u8)) return (ManagedCssProperty)253;
        if (name.SequenceEqual("border-width"u8)) return ManagedCssProperty.BorderWidth;
        if (name.SequenceEqual("border-style"u8)) return ManagedCssProperty.BorderStyle;
        if (name.SequenceEqual("border-color"u8)) return ManagedCssProperty.BorderColor;
        if (name.SequenceEqual("position"u8)) return ManagedCssProperty.Position;
        if (name.SequenceEqual("top"u8)) return ManagedCssProperty.Top;
        if (name.SequenceEqual("right"u8)) return ManagedCssProperty.Right;
        if (name.SequenceEqual("bottom"u8)) return ManagedCssProperty.Bottom;
        if (name.SequenceEqual("left"u8)) return ManagedCssProperty.Left;
        if (name.SequenceEqual("overflow"u8)) return ManagedCssProperty.Overflow;
        if (name.SequenceEqual("overflow-x"u8)) return ManagedCssProperty.OverflowX;
        if (name.SequenceEqual("overflow-y"u8)) return ManagedCssProperty.OverflowY;
        if (name.SequenceEqual("opacity"u8)) return ManagedCssProperty.Opacity;
        if (name.SequenceEqual("z-index"u8)) return ManagedCssProperty.ZIndex;
        return ManagedCssProperty.Count;
    }

    private bool TryParseValue(ManagedCssProperty property, ReadOnlySpan<byte> value,
                               out ManagedCssValue parsed)
    {
        parsed = default;
        if (value.SequenceEqual("inherit"u8)) { parsed = Keyword(ManagedCssKeyword.Inherit); return true; }
        if (value.SequenceEqual("initial"u8)) { parsed = Keyword(ManagedCssKeyword.Initial); return true; }
        switch (property)
        {
            case ManagedCssProperty.Display:
                if (TryKeyword(value, out ManagedCssKeyword display) &&
                    (display == ManagedCssKeyword.None || display == ManagedCssKeyword.Block ||
                     display == ManagedCssKeyword.Inline || display == ManagedCssKeyword.InlineBlock ||
                     display == ManagedCssKeyword.ListItem || display == ManagedCssKeyword.Table ||
                     display == ManagedCssKeyword.TableRow || display == ManagedCssKeyword.TableCell ||
                     display == ManagedCssKeyword.Flex))
                { parsed = Keyword(display); return true; }
                return false;
            case ManagedCssProperty.Visibility:
                if (TryKeyword(value, out ManagedCssKeyword visibility) &&
                    (visibility == ManagedCssKeyword.Visible || visibility == ManagedCssKeyword.Hidden || visibility == ManagedCssKeyword.Collapse))
                { parsed = Keyword(visibility); return true; }
                return false;
            case ManagedCssProperty.Color:
            case ManagedCssProperty.BackgroundColor:
            case ManagedCssProperty.BorderColor:
                if (TryParseColor(value, out uint color)) { parsed = new ManagedCssValue { Kind = ManagedCssValueKind.Color, Color = color }; return true; }
                return false;
            case ManagedCssProperty.FontSize:
                if (TryParseLength(value, false, out ManagedCssLength fontSize)) { parsed = LengthValue(fontSize); return true; }
                return false;
            case ManagedCssProperty.FontWeight:
                if (value.SequenceEqual("normal"u8)) { parsed = Keyword(ManagedCssKeyword.Normal); return true; }
                if (value.SequenceEqual("bold"u8)) { parsed = Keyword(ManagedCssKeyword.Bold); return true; }
                if (TryParseInteger(value, out int weight) && weight >= 100 && weight <= 900) { parsed = Number(weight); return true; }
                return false;
            case ManagedCssProperty.FontStyle:
                if (value.SequenceEqual("normal"u8)) { parsed = Keyword(ManagedCssKeyword.Normal); return true; }
                if (value.SequenceEqual("italic"u8)) { parsed = Keyword(ManagedCssKeyword.Italic); return true; }
                return false;
            case ManagedCssProperty.TextAlign:
                if (TryKeyword(value, out ManagedCssKeyword align) && align >= ManagedCssKeyword.Left && align <= ManagedCssKeyword.Justify)
                { parsed = Keyword(align); return true; }
                return false;
            case ManagedCssProperty.WhiteSpace:
                if (TryKeyword(value, out ManagedCssKeyword whiteSpace) && whiteSpace >= ManagedCssKeyword.Normal && whiteSpace <= ManagedCssKeyword.PreLine)
                { parsed = Keyword(whiteSpace); return true; }
                return false;
            case ManagedCssProperty.BorderWidth:
                if (value.SequenceEqual("thin"u8)) { parsed = LengthValue(new ManagedCssLength(100, ManagedCssLengthUnit.Px)); return true; }
                if (value.SequenceEqual("medium"u8)) { parsed = LengthValue(new ManagedCssLength(300, ManagedCssLengthUnit.Px)); return true; }
                if (value.SequenceEqual("thick"u8)) { parsed = LengthValue(new ManagedCssLength(500, ManagedCssLengthUnit.Px)); return true; }
                if (TryParseLength(value, false, out ManagedCssLength borderWidth))
                { parsed = LengthValue(borderWidth); return true; }
                return false;
            case ManagedCssProperty.Position:
                if (TryKeyword(value, out ManagedCssKeyword position) && position >= ManagedCssKeyword.Static && position <= ManagedCssKeyword.Fixed)
                { parsed = Keyword(position); return true; }
                return false;
            case ManagedCssProperty.Overflow:
            case ManagedCssProperty.OverflowX:
            case ManagedCssProperty.OverflowY:
                if (TryKeyword(value, out ManagedCssKeyword overflow) &&
                    (overflow == ManagedCssKeyword.Visible || overflow == ManagedCssKeyword.Hidden || overflow == ManagedCssKeyword.Scroll || overflow == ManagedCssKeyword.Auto))
                { parsed = Keyword(overflow); return true; }
                return false;
            case ManagedCssProperty.BorderStyle:
                if (value.SequenceEqual("none"u8)) { parsed = Keyword(ManagedCssKeyword.None); return true; }
                if (value.SequenceEqual("solid"u8)) { parsed = Keyword(ManagedCssKeyword.Solid); return true; }
                if (value.SequenceEqual("dashed"u8)) { parsed = Keyword(ManagedCssKeyword.Dashed); return true; }
                if (value.SequenceEqual("dotted"u8)) { parsed = Keyword(ManagedCssKeyword.Dotted); return true; }
                return false;
            case ManagedCssProperty.Opacity:
                if (TryParseDecimal(value, out int opacity) && opacity >= 0 && opacity <= 10000)
                { parsed = Number(opacity); return true; }
                return false;
            case ManagedCssProperty.ZIndex:
                if (value.SequenceEqual("auto"u8)) { parsed = Keyword(ManagedCssKeyword.Auto); return true; }
                if (TryParseInteger(value, out int zIndex)) { parsed = Number(zIndex); return true; }
                return false;
            default:
                if (TryParseLength(value, false, out ManagedCssLength length)) { parsed = LengthValue(length); return true; }
                return false;
        }
    }

    private bool TryParseLengthList(ReadOnlySpan<byte> value, bool allowAuto,
                                    Span<ManagedCssLength> lengths, out int count)
    {
        count = 0;
        int position = 0;
        while (position < value.Length)
        {
            while (position < value.Length && value[position] <= 0x7F &&
                   IsCssWhitespace((byte)value[position])) ++position;
            if (position == value.Length) break;
            int begin = position;
            while (position < value.Length && (value[position] > 0x7F ||
                                               !IsCssWhitespace((byte)value[position]))) ++position;
            if (count == 4 || !TryParseLength(value[begin..position], allowAuto,
                                               out lengths[count])) return false;
            ++count;
        }
        return count != 0;
    }

    private static bool TryParseLength(ReadOnlySpan<byte> value, bool allowAuto,
                                       out ManagedCssLength length)
    {
        length = default;
        if (value.SequenceEqual("auto"u8))
        {
            if (!allowAuto) return false;
            length = new ManagedCssLength(0, ManagedCssLengthUnit.Auto);
            return true;
        }
        int position = 0;
        bool negative = false;
        if (position < value.Length && (value[position] == '+' || value[position] == '-'))
        {
            negative = value[position++] == '-';
        }
        int whole = 0;
        int decimals = 0;
        int decimalCount = 0;
        bool digits = false;
        while (position < value.Length && value[position] >= '0' && value[position] <= '9')
        {
            digits = true;
            int digit = value[position++] - '0';
            if (whole > 100_000 || (whole == 100_000 && digit > 0)) return false;
            whole = whole * 10 + digit;
        }
        if (position < value.Length && value[position] == '.')
        {
            ++position;
            while (position < value.Length && value[position] >= '0' && value[position] <= '9')
            {
                if (decimalCount == 2) return false;
                decimals = decimals * 10 + value[position++] - '0';
                ++decimalCount;
                digits = true;
            }
        }
        if (!digits) return false;
        int fixedValue = whole * 100 + (decimalCount == 1 ? decimals * 10 : decimals);
        if (negative) fixedValue = -fixedValue;
        ManagedCssLengthUnit unit = ManagedCssLengthUnit.Px;
        if (value[position..].SequenceEqual("%"u8)) unit = ManagedCssLengthUnit.Percent;
        else if (value[position..].SequenceEqual("px"u8)) unit = ManagedCssLengthUnit.Px;
        else if (value[position..].SequenceEqual("em"u8)) unit = ManagedCssLengthUnit.Em;
        else if (value[position..].SequenceEqual("rem"u8)) unit = ManagedCssLengthUnit.Rem;
        else if (!value[position..].IsEmpty) return false;
        length = new ManagedCssLength(fixedValue, unit);
        return true;
    }

    private static bool TryParseInteger(ReadOnlySpan<byte> value, out int number)
    {
        number = 0;
        if (value.IsEmpty) return false;
        int position = 0;
        bool negative = false;
        if (value[0] == '+' || value[0] == '-') { negative = value[0] == '-'; position = 1; }
        if (position == value.Length) return false;
        while (position < value.Length)
        {
            byte digit = value[position++];
            if (digit < '0' || digit > '9' || number > 1_000_000) return false;
            number = number * 10 + digit - '0';
        }
        if (negative) number = -number;
        return true;
    }

    private static bool TryParseDecimal(ReadOnlySpan<byte> value, out int number)
    {
        number = 0;
        int position = 0;
        bool negative = false;
        if (position < value.Length && (value[position] == '+' || value[position] == '-'))
            negative = value[position++] == '-';
        int whole = 0;
        while (position < value.Length && value[position] >= '0' && value[position] <= '9')
            whole = whole * 10 + value[position++] - '0';
        int fraction = 0;
        int fractionDigits = 0;
        if (position < value.Length && value[position] == '.')
        {
            ++position;
            while (position < value.Length && value[position] >= '0' && value[position] <= '9')
            {
                if (fractionDigits == 4) return false;
                fraction = fraction * 10 + value[position++] - '0';
                ++fractionDigits;
            }
        }
        if (position != value.Length || (whole == 0 && fractionDigits == 0)) return false;
        int scale = fractionDigits switch { 0 => 1, 1 => 10, 2 => 100, 3 => 1000, _ => 10000 };
        int fixedValue = whole * 10000 + fraction * (10000 / scale);
        number = negative ? -fixedValue : fixedValue;
        return true;
    }

    private static bool TryParseColor(ReadOnlySpan<byte> value, out uint color)
    {
        color = 0;
        if (value.SequenceEqual("transparent"u8)) return true;
        if (value.SequenceEqual("black"u8)) { color = 0xFF000000; return true; }
        if (value.SequenceEqual("white"u8)) { color = 0xFFFFFFFF; return true; }
        if (value.SequenceEqual("red"u8)) { color = 0xFFFF0000; return true; }
        if (value.SequenceEqual("green"u8)) { color = 0xFF008000; return true; }
        if (value.SequenceEqual("blue"u8)) { color = 0xFF0000FF; return true; }
        if (value.SequenceEqual("gray"u8) || value.SequenceEqual("grey"u8)) { color = 0xFF808080; return true; }
        if (value.Length != 4 && value.Length != 7 && value.Length != 5 && value.Length != 9) return false;
        if (value[0] != '#') return false;
        int digits = value.Length - 1;
        Span<byte> channels = stackalloc byte[4];
        int channelCount = digits == 3 || digits == 4 ? digits : digits / 2;
        for (int index = 0; index != channelCount; ++index)
        {
            int first = Hex(value[1 + (digits == 3 || digits == 4 ? index : index * 2)]);
            int second = digits == 3 || digits == 4 ? first : Hex(value[2 + index * 2]);
            if (first < 0 || second < 0) return false;
            channels[index] = (byte)(digits == 3 || digits == 4 ? first * 17 : first * 16 + second);
        }
        if (channelCount == 3) color = 0xFF000000U | ((uint)channels[0] << 16) |
            ((uint)channels[1] << 8) | channels[2];
        else color = ((uint)channels[3] << 24) | ((uint)channels[0] << 16) |
            ((uint)channels[1] << 8) | channels[2];
        return true;
    }

    private static int Hex(byte value) => value >= '0' && value <= '9' ? value - '0' :
        value >= 'a' && value <= 'f' ? value - 'a' + 10 :
        value >= 'A' && value <= 'F' ? value - 'A' + 10 : -1;

    private static bool TryKeyword(ReadOnlySpan<byte> value, out ManagedCssKeyword keyword)
    {
        keyword = value.SequenceEqual("none"u8) ? ManagedCssKeyword.None :
            value.SequenceEqual("auto"u8) ? ManagedCssKeyword.Auto :
            value.SequenceEqual("normal"u8) ? ManagedCssKeyword.Normal :
            value.SequenceEqual("visible"u8) ? ManagedCssKeyword.Visible :
            value.SequenceEqual("hidden"u8) ? ManagedCssKeyword.Hidden :
            value.SequenceEqual("collapse"u8) ? ManagedCssKeyword.Collapse :
            value.SequenceEqual("inline"u8) ? ManagedCssKeyword.Inline :
            value.SequenceEqual("block"u8) ? ManagedCssKeyword.Block :
            value.SequenceEqual("inline-block"u8) ? ManagedCssKeyword.InlineBlock :
            value.SequenceEqual("table"u8) ? ManagedCssKeyword.Table :
            value.SequenceEqual("table-row"u8) ? ManagedCssKeyword.TableRow :
            value.SequenceEqual("table-cell"u8) ? ManagedCssKeyword.TableCell :
            value.SequenceEqual("list-item"u8) ? ManagedCssKeyword.ListItem :
            value.SequenceEqual("flex"u8) ? ManagedCssKeyword.Flex :
            value.SequenceEqual("bold"u8) ? ManagedCssKeyword.Bold :
            value.SequenceEqual("italic"u8) ? ManagedCssKeyword.Italic :
            value.SequenceEqual("left"u8) ? ManagedCssKeyword.Left :
            value.SequenceEqual("right"u8) ? ManagedCssKeyword.Right :
            value.SequenceEqual("center"u8) ? ManagedCssKeyword.Center :
            value.SequenceEqual("justify"u8) ? ManagedCssKeyword.Justify :
            value.SequenceEqual("pre"u8) ? ManagedCssKeyword.Pre :
            value.SequenceEqual("nowrap"u8) ? ManagedCssKeyword.NoWrap :
            value.SequenceEqual("pre-wrap"u8) ? ManagedCssKeyword.PreWrap :
            value.SequenceEqual("pre-line"u8) ? ManagedCssKeyword.PreLine :
            value.SequenceEqual("static"u8) ? ManagedCssKeyword.Static :
            value.SequenceEqual("relative"u8) ? ManagedCssKeyword.Relative :
            value.SequenceEqual("absolute"u8) ? ManagedCssKeyword.Absolute :
            value.SequenceEqual("fixed"u8) ? ManagedCssKeyword.Fixed :
            value.SequenceEqual("scroll"u8) ? ManagedCssKeyword.Scroll :
            value.SequenceEqual("solid"u8) ? ManagedCssKeyword.Solid :
            value.SequenceEqual("dashed"u8) ? ManagedCssKeyword.Dashed :
            value.SequenceEqual("dotted"u8) ? ManagedCssKeyword.Dotted :
            (ManagedCssKeyword)ushort.MaxValue;
        return keyword != (ManagedCssKeyword)ushort.MaxValue;
    }

    private static ManagedCssValue Keyword(ManagedCssKeyword keyword) =>
        new() { Kind = ManagedCssValueKind.Keyword, Keyword = keyword };
    private static ManagedCssValue Number(int number) =>
        new() { Kind = ManagedCssValueKind.Number, Number = number };
    private static ManagedCssValue LengthValue(ManagedCssLength length) =>
        new() { Kind = ManagedCssValueKind.Length, Unit = length.Unit, Number = length.Value };

    private static ManagedCssLength LengthFromValue(ManagedCssValue value) =>
        value.Kind == ManagedCssValueKind.Length ? new ManagedCssLength(value.Number, value.Unit) : AutoLength();
    private static int NumberFromValue(ManagedCssValue value, int fallback) =>
        value.Kind == ManagedCssValueKind.Number ? value.Number :
        value.Keyword == ManagedCssKeyword.Bold ? 700 :
        value.Keyword == ManagedCssKeyword.Normal ? 400 : fallback;
    private static ManagedCssDisplay DisplayFromValue(ManagedCssValue value) => value.Keyword switch
    {
        ManagedCssKeyword.None => ManagedCssDisplay.None,
        ManagedCssKeyword.Inline => ManagedCssDisplay.Inline,
        ManagedCssKeyword.Block => ManagedCssDisplay.Block,
        ManagedCssKeyword.InlineBlock => ManagedCssDisplay.InlineBlock,
        ManagedCssKeyword.Table => ManagedCssDisplay.Table,
        ManagedCssKeyword.TableRow => ManagedCssDisplay.TableRow,
        ManagedCssKeyword.TableCell => ManagedCssDisplay.TableCell,
        ManagedCssKeyword.ListItem => ManagedCssDisplay.ListItem,
        ManagedCssKeyword.Flex => ManagedCssDisplay.Flex,
        _ => ManagedCssDisplay.Inline
    };
    private static ManagedCssVisibility VisibilityFromValue(ManagedCssValue value) => value.Keyword switch
    {
        ManagedCssKeyword.Hidden => ManagedCssVisibility.Hidden,
        ManagedCssKeyword.Collapse => ManagedCssVisibility.Collapse,
        _ => ManagedCssVisibility.Visible
    };
    private static ManagedCssTextAlign TextAlignFromValue(ManagedCssValue value) => value.Keyword switch
    {
        ManagedCssKeyword.Right => ManagedCssTextAlign.Right,
        ManagedCssKeyword.Center => ManagedCssTextAlign.Center,
        ManagedCssKeyword.Justify => ManagedCssTextAlign.Justify,
        _ => ManagedCssTextAlign.Left
    };
    private static ManagedCssWhiteSpace WhiteSpaceFromValue(ManagedCssValue value) => value.Keyword switch
    {
        ManagedCssKeyword.Pre => ManagedCssWhiteSpace.Pre,
        ManagedCssKeyword.NoWrap => ManagedCssWhiteSpace.NoWrap,
        ManagedCssKeyword.PreWrap => ManagedCssWhiteSpace.PreWrap,
        ManagedCssKeyword.PreLine => ManagedCssWhiteSpace.PreLine,
        _ => ManagedCssWhiteSpace.Normal
    };
    private static ManagedCssPosition PositionFromValue(ManagedCssValue value) => value.Keyword switch
    {
        ManagedCssKeyword.Relative => ManagedCssPosition.Relative,
        ManagedCssKeyword.Absolute => ManagedCssPosition.Absolute,
        ManagedCssKeyword.Fixed => ManagedCssPosition.Fixed,
        _ => ManagedCssPosition.Static
    };
    private static ManagedCssOverflow OverflowFromValue(ManagedCssValue value) => value.Keyword switch
    {
        ManagedCssKeyword.Hidden => ManagedCssOverflow.Hidden,
        ManagedCssKeyword.Scroll => ManagedCssOverflow.Scroll,
        ManagedCssKeyword.Auto => ManagedCssOverflow.Auto,
        _ => ManagedCssOverflow.Visible
    };
    private static ManagedCssBorderStyle BorderStyleFromValue(ManagedCssValue value) => value.Keyword switch
    {
        ManagedCssKeyword.Solid => ManagedCssBorderStyle.Solid,
        ManagedCssKeyword.Dashed => ManagedCssBorderStyle.Dashed,
        ManagedCssKeyword.Dotted => ManagedCssBorderStyle.Dotted,
        _ => ManagedCssBorderStyle.None
    };
    private static ManagedCssLength ZeroLength() => new(0, ManagedCssLengthUnit.Px);
    private static ManagedCssLength AutoLength() => new(0, ManagedCssLengthUnit.Auto);

    private static ManagedCssDisplay DefaultDisplay(ManagedHtmlTag tag) => tag switch
    {
        ManagedHtmlTag.Html or ManagedHtmlTag.Body or ManagedHtmlTag.Div or ManagedHtmlTag.P or
        ManagedHtmlTag.H1 or ManagedHtmlTag.H2 or ManagedHtmlTag.H3 or ManagedHtmlTag.H4 or
        ManagedHtmlTag.H5 or ManagedHtmlTag.H6 or ManagedHtmlTag.Ul or ManagedHtmlTag.Ol or
        ManagedHtmlTag.Form or ManagedHtmlTag.Main or ManagedHtmlTag.Header or ManagedHtmlTag.Footer or
        ManagedHtmlTag.Section or ManagedHtmlTag.Article or ManagedHtmlTag.Aside or ManagedHtmlTag.Nav => ManagedCssDisplay.Block,
        ManagedHtmlTag.Table => ManagedCssDisplay.Table,
        ManagedHtmlTag.Tr => ManagedCssDisplay.TableRow,
        ManagedHtmlTag.Td or ManagedHtmlTag.Th => ManagedCssDisplay.TableCell,
        ManagedHtmlTag.Li => ManagedCssDisplay.ListItem,
        ManagedHtmlTag.Img => ManagedCssDisplay.InlineBlock,
        ManagedHtmlTag.Head or ManagedHtmlTag.Style or ManagedHtmlTag.Script or ManagedHtmlTag.Meta or
        ManagedHtmlTag.Link or ManagedHtmlTag.Title or ManagedHtmlTag.Base => ManagedCssDisplay.None,
        _ => ManagedCssDisplay.Inline
    };

    private static bool IsShorthand(ManagedCssProperty property) =>
        property == (ManagedCssProperty)254 || property == (ManagedCssProperty)253;

    private static int SaturatingIncrement(int value) =>
        value == int.MaxValue ? value : value + 1;

    private static bool IsCssWhitespace(byte value) =>
        value == 0x20 || value == 0x09 || value == 0x0A || value == 0x0C || value == 0x0D;
    private static bool IsCssIdentStart(byte value) =>
        (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z') || value == '_' || value == '-';
    private static bool IsCssIdent(byte value) => IsCssIdentStart(value) ||
        (value >= '0' && value <= '9');
    private static byte ToLowerAscii(byte value) => value >= 'A' && value <= 'Z' ? (byte)(value + 32) : value;

    private static void TrimRange(byte[] buffer, int length, out int start, out int end) =>
        TrimRange(buffer, 0, length, out start, out end);
    private static void TrimRange(byte[] buffer, int begin, int lengthOrEnd,
                                  out int start, out int end)
    {
        start = begin;
        end = lengthOrEnd;
        while (start < end && IsCssWhitespace(buffer[start])) ++start;
        while (end > start && IsCssWhitespace(buffer[end - 1])) --end;
    }

    private static bool RemoveImportant(byte[] buffer, ref int start, ref int end)
    {
        int suffix = end - 10;
        if (suffix >= start && buffer[suffix] == '!' &&
            buffer.AsSpan(suffix, 10).SequenceEqual("!important"u8))
        {
            end = suffix;
            while (end > start && IsCssWhitespace(buffer[end - 1])) --end;
            return true;
        }
        return false;
    }

    private void SkipRuleBody(ref ManagedCssInput input)
    {
        int depth = 1;
        while (input.TryRead(out uint scalar))
        {
            if (scalar == '/' && input.TryPeek(out uint next) && next == '*')
            {
                input.TryRead(out _);
                input.SkipComment();
                continue;
            }
            if (scalar == '{') ++depth;
            else if (scalar == '}' && --depth == 0) return;
        }
    }

    private void SkipToDeclarationEnd(ref ManagedCssInput input, bool inRule, bool closed)
    {
        if (closed) return;
        while (input.TryRead(out uint scalar))
        {
            if (scalar == ';' || (inRule && scalar == '}')) return;
        }
    }

    private void SkipWhitespaceAndComments(ref ManagedCssInput input)
    {
        uint scalar;
        while (true)
        {
            while (input.TryPeek(out uint peekScalar) && peekScalar <= 0x7F &&
                   IsCssWhitespace((byte)peekScalar))
                input.TryRead(out _);
            if (!input.TryPeek(out scalar) || scalar != '/') return;
            input.TryRead(out _);
            if (!input.TryPeek(out scalar) || scalar != '*')
            {
                input.PushbackSlash();
                return;
            }
            input.TryRead(out _);
            if (!input.SkipComment()) return;
        }
    }

    private void UpdatePeaks()
    {
        if (_ruleCount > _rulesPeak) _rulesPeak = _ruleCount;
        if (_selectorCount > _selectorsPeak) _selectorsPeak = _selectorCount;
        if (_declarationCount > _declarationsPeak) _declarationsPeak = _declarationCount;
    }

    private static bool _ContainsAsciiToken(ReadOnlySpan<uint> value, ReadOnlySpan<byte> expected)
    {
        int position = 0;
        while (position < value.Length)
        {
            while (position < value.Length && value[position] <= 0x7F &&
                   IsCssWhitespace((byte)value[position])) ++position;
            int begin = position;
            while (position < value.Length && (value[position] > 0x7F ||
                                               !IsCssWhitespace((byte)value[position]))) ++position;
            if (position - begin == expected.Length && ScalarSpanEquals(value[begin..position], expected)) return true;
        }
        return false;
    }

    private static bool ScalarSpanEquals(ReadOnlySpan<uint> value, ReadOnlySpan<byte> expected)
    {
        if (value.Length != expected.Length) return false;
        for (int index = 0; index != value.Length; ++index)
            if (value[index] > 0x7F || (byte)value[index] != expected[index]) return false;
        return true;
    }

    private ManagedHtmlNodeHandle NodeHandle(int index)
    {
        if (index < 0 || index >= _document.NodeCount) return ManagedHtmlNodeHandle.Invalid;
        return new ManagedHtmlNodeHandle(index, _document.DocumentNode.Generation);
    }

    private readonly struct ManagedCssMatchState
    {
        internal ManagedCssMatchState(int step, int node) { Step = step; Node = node; }
        internal int Step { get; init; }
        internal int Node { get; init; }
    }

    private ref struct ManagedCssInput
    {
        private ReadOnlySpan<uint> _span;
        private ManagedHtmlDocument? _document;
        private ManagedHtmlNodeHandle _node;
        private int _attributeIndex;
        private int _position;
        private bool _fromAttribute;
        private bool _fromTextChildren;
        private bool _pushbackSlash;

        internal static ManagedCssInput FromSpan(ReadOnlySpan<uint> span) => new()
        {
            _span = span
        };

        internal static ManagedCssInput FromAttribute(ManagedHtmlDocument document,
                                                       ManagedHtmlNodeHandle element,
                                                       int attributeIndex) => new()
        {
            _document = document,
            _node = element,
            _attributeIndex = attributeIndex,
            _fromAttribute = true
        };

        internal static ManagedCssInput FromTextChildren(ManagedHtmlDocument document,
                                                          ManagedHtmlNodeHandle style)
        {
            ManagedCssInput input = new()
            {
                _document = document,
                _node = document.GetFirstChild(style),
                _fromTextChildren = true
            };
            input.AdvanceTextNode();
            return input;
        }

        internal bool TryPeek(out uint scalar)
        {
            int savedPosition = _position;
            ManagedHtmlNodeHandle savedNode = _node;
            bool savedPushback = _pushbackSlash;
            bool result = TryRead(out scalar);
            _position = savedPosition;
            _node = savedNode;
            _pushbackSlash = savedPushback;
            return result;
        }

        internal bool TryRead(out uint scalar)
        {
            if (_pushbackSlash)
            {
                _pushbackSlash = false;
                scalar = '/';
                return true;
            }
            if (!_fromAttribute && !_fromTextChildren)
            {
                if (_position >= _span.Length) { scalar = 0; return false; }
                scalar = _span[_position++];
                return true;
            }
            if (_fromAttribute)
            {
                ManagedHtmlNodeRecord node = _document!.Nodes[_node.Index];
                if (_attributeIndex < 0 || _attributeIndex >= node.AttributeCount)
                { scalar = 0; return false; }
                ManagedHtmlAttributeRecord attribute = _document.Attributes[
                    node.FirstAttribute + _attributeIndex];
                if (_position >= attribute.ValueLength) { scalar = 0; return false; }
                scalar = _document.AttributeValues[attribute.ValueOffset + _position++];
                return true;
            }
            while (_node != ManagedHtmlNodeHandle.Invalid)
            {
                ManagedHtmlNodeRecord text = _document!.Nodes[_node.Index];
                if (_position < text.TextLength)
                {
                    scalar = _document.Text[text.TextOffset + _position++];
                    return true;
                }
                _node = _document.GetNextSibling(_node);
                AdvanceTextNode();
            }
            scalar = 0;
            return false;
        }

        internal bool SkipComment()
        {
            uint previous = 0;
            while (TryRead(out uint scalar))
            {
                if (previous == '*' && scalar == '/') return true;
                previous = scalar;
            }
            return false;
        }

        internal void PushbackSlash() => _pushbackSlash = true;

        private void AdvanceTextNode()
        {
            while (_node != ManagedHtmlNodeHandle.Invalid &&
                   _document!.GetNodeKind(_node) != ManagedHtmlNodeKind.Text)
                _node = _document.GetNextSibling(_node);
            _position = 0;
        }
    }
}

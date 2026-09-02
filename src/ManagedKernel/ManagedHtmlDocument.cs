using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

public enum ManagedHtmlNodeKind : byte
{
    Document = 0,
    Doctype = 1,
    Element = 2,
    Text = 3,
    Comment = 4
}

/* Known names are compact enum values.  Unknown names are copied to the
   bounded tag-name arena and remain representable without a String. */
public enum ManagedHtmlTag : ushort
{
    Unknown = 0,
    Html,
    Head,
    Body,
    Title,
    Meta,
    Link,
    Style,
    Script,
    Div,
    Span,
    P,
    A,
    Img,
    Br,
    Hr,
    H1,
    H2,
    H3,
    H4,
    H5,
    H6,
    Ul,
    Ol,
    Li,
    Table,
    Thead,
    Tbody,
    Tfoot,
    Tr,
    Td,
    Th,
    Form,
    Input,
    Button,
    Label,
    Select,
    Option,
    Textarea,
    Pre,
    Code,
    Strong,
    Em,
    Base,
    Colgroup,
    Col,
    Caption,
    Area,
    Embed,
    Param,
    Source,
    Track,
    Wbr,
    Main,
    Header,
    Footer,
    Section,
    Article,
    Aside,
    Nav,
    Blockquote,
    Dl,
    Dt,
    Dd,
    Fieldset,
    Legend,
    Hgroup,
    Menu,
    Address
}

public enum ManagedHtmlAttributeName : ushort
{
    Unknown = 0,
    Id,
    Class,
    Style,
    Href,
    Src,
    Title,
    Name,
    Type,
    Value,
    Width,
    Height,
    Disabled,
    Checked,
    Selected,
    Colspan,
    Rowspan,
    Alt,
    Action,
    Method,
    Required,
    For,
    Rel,
    Charset,
    Lang,
    Role
}

public enum ManagedHtmlTreeBuilderInsertionMode : byte
{
    Initial = 0,
    BeforeHtml = 1,
    BeforeHead = 2,
    InHead = 3,
    AfterHead = 4,
    InBody = 5,
    Text = 6,
    AfterBody = 7,
    AfterAfterBody = 8,
    InTable = 9,
    InTableBody = 10,
    InRow = 11,
    InCell = 12
}

public enum ManagedHtmlTreeBuilderState : byte
{
    Idle = 0,
    Receiving = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Failed = 5
}

public enum ManagedHtmlTreeBuilderFailureReason : byte
{
    None = 0,
    NodeCapacityExceeded = 1,
    TextCapacityExceeded = 2,
    AttributeCapacityExceeded = 3,
    AttributeValueCapacityExceeded = 4,
    AttributeNameCapacityExceeded = 5,
    TagNameCapacityExceeded = 6,
    TreeDepthExceeded = 7,
    InvalidTreeState = 8,
    UnsupportedInsertionModeCase = 9,
    TokenConsumerFailure = 10,
    Cancelled = 11
}

public enum ManagedHtmlDocumentValidationFailureReason : byte
{
    None = 0,
    RootInvalid = 1,
    NodeKindInvalid = 2,
    ParentOutOfRange = 3,
    ChildOutOfRange = 4,
    ParentLinkMismatch = 5,
    FirstLastMismatch = 6,
    SiblingLinkMismatch = 7,
    SiblingCycle = 8,
    TextRangeInvalid = 9,
    AttributeRangeInvalid = 10,
    AttributeOwnerMismatch = 11,
    AttributeNameRangeInvalid = 12,
    AttributeValueRangeInvalid = 13
}

public readonly struct ManagedHtmlNodeHandle : IEquatable<ManagedHtmlNodeHandle>
{
    internal ManagedHtmlNodeHandle(int index, uint generation)
    {
        Index = index;
        Generation = generation;
    }

    public static ManagedHtmlNodeHandle Invalid => default;
    public int Index { get; }
    internal uint Generation { get; }
    public bool IsInvalid => Index < 0 || Generation == 0;

    public bool Equals(ManagedHtmlNodeHandle other) =>
        Index == other.Index && Generation == other.Generation;

    public override bool Equals(object? obj) =>
        obj is ManagedHtmlNodeHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Index, Generation);
    public static bool operator ==(ManagedHtmlNodeHandle left,
                                   ManagedHtmlNodeHandle right) => left.Equals(right);
    public static bool operator !=(ManagedHtmlNodeHandle left,
                                   ManagedHtmlNodeHandle right) => !left.Equals(right);
}

public readonly struct ManagedHtmlDocumentArenaOptions
{
    public ManagedHtmlDocumentArenaOptions(
        int nodeCapacity,
        int textScalarCapacity,
        int attributeCapacity,
        int attributeValueCapacity,
        int treeDepthCapacity,
        int tagNameCapacity = ManagedHtmlDocumentLimits.DefaultTagNameCapacity,
        int attributeNameCapacity = ManagedHtmlDocumentLimits.DefaultAttributeNameCapacity)
    {
        Validate(nodeCapacity, textScalarCapacity, attributeCapacity,
                 attributeValueCapacity, treeDepthCapacity, tagNameCapacity,
                 attributeNameCapacity);
        NodeCapacity = nodeCapacity;
        TextScalarCapacity = textScalarCapacity;
        AttributeCapacity = attributeCapacity;
        AttributeValueCapacity = attributeValueCapacity;
        TreeDepthCapacity = treeDepthCapacity;
        TagNameCapacity = tagNameCapacity;
        AttributeNameCapacity = attributeNameCapacity;
    }

    public static ManagedHtmlDocumentArenaOptions Default => new(
        ManagedHtmlDocumentLimits.DefaultNodeCapacity,
        ManagedHtmlDocumentLimits.DefaultTextScalarCapacity,
        ManagedHtmlDocumentLimits.DefaultAttributeCapacity,
        ManagedHtmlDocumentLimits.DefaultAttributeValueCapacity,
        ManagedHtmlDocumentLimits.DefaultTreeDepthCapacity);

    public int NodeCapacity { get; }
    public int TextScalarCapacity { get; }
    public int AttributeCapacity { get; }
    public int AttributeValueCapacity { get; }
    public int TreeDepthCapacity { get; }
    public int TagNameCapacity { get; }
    public int AttributeNameCapacity { get; }

    private static void Validate(int nodeCapacity, int textScalarCapacity,
                                 int attributeCapacity, int attributeValueCapacity,
                                 int treeDepthCapacity, int tagNameCapacity,
                                 int attributeNameCapacity)
    {
        if (nodeCapacity <= 0 || nodeCapacity > ManagedHtmlDocumentLimits.MaximumNodeCapacity)
            throw new ArgumentOutOfRangeException(nameof(nodeCapacity));
        if (textScalarCapacity <= 0 || textScalarCapacity > ManagedHtmlDocumentLimits.MaximumTextScalarCapacity)
            throw new ArgumentOutOfRangeException(nameof(textScalarCapacity));
        if (attributeCapacity <= 0 || attributeCapacity > ManagedHtmlDocumentLimits.MaximumAttributeCapacity)
            throw new ArgumentOutOfRangeException(nameof(attributeCapacity));
        if (attributeValueCapacity <= 0 || attributeValueCapacity > ManagedHtmlDocumentLimits.MaximumAttributeValueCapacity)
            throw new ArgumentOutOfRangeException(nameof(attributeValueCapacity));
        if (treeDepthCapacity <= 0 || treeDepthCapacity > ManagedHtmlDocumentLimits.MaximumTreeDepthCapacity)
            throw new ArgumentOutOfRangeException(nameof(treeDepthCapacity));
        if (tagNameCapacity <= 0 || tagNameCapacity > ManagedHtmlDocumentLimits.MaximumTagNameCapacity)
            throw new ArgumentOutOfRangeException(nameof(tagNameCapacity));
        if (attributeNameCapacity <= 0 || attributeNameCapacity > ManagedHtmlDocumentLimits.MaximumAttributeNameCapacity)
            throw new ArgumentOutOfRangeException(nameof(attributeNameCapacity));
    }
}

public static class ManagedHtmlDocumentLimits
{
    public const int DefaultNodeCapacity = 1024;
    public const int DefaultTextScalarCapacity = 65_536;
    public const int DefaultAttributeCapacity = 2_048;
    public const int DefaultAttributeValueCapacity = 16_384;
    public const int DefaultTreeDepthCapacity = 128;
    public const int DefaultTagNameCapacity = 8_192;
    public const int DefaultAttributeNameCapacity = 16_384;

    public const int MaximumNodeCapacity = 4_096;
    public const int MaximumTextScalarCapacity = 262_144;
    public const int MaximumAttributeCapacity = 16_384;
    public const int MaximumAttributeValueCapacity = 262_144;
    public const int MaximumTreeDepthCapacity = 512;
    public const int MaximumTagNameCapacity = 65_536;
    public const int MaximumAttributeNameCapacity = 65_536;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedHtmlNodeRecord
{
    internal ManagedHtmlNodeKind Kind;
    internal ManagedHtmlTag Tag;
    internal int Parent;
    internal int FirstChild;
    internal int LastChild;
    internal int PreviousSibling;
    internal int NextSibling;
    internal int NameOffset;
    internal int NameLength;
    internal int FirstAttribute;
    internal int AttributeCount;
    internal int TextOffset;
    internal int TextLength;
    internal byte Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct ManagedHtmlAttributeRecord
{
    internal int Owner;
    internal ManagedHtmlAttributeName KnownName;
    internal int NameOffset;
    internal int NameLength;
    internal int ValueOffset;
    internal int ValueLength;
    internal byte Flags;
}

public readonly struct ManagedHtmlNodeView
{
    internal ManagedHtmlNodeView(ManagedHtmlNodeHandle handle,
                                 in ManagedHtmlNodeRecord record,
                                 uint generation)
    {
        Handle = handle;
        Kind = record.Kind;
        Tag = record.Tag;
        Parent = ToHandle(record.Parent, generation);
        FirstChild = ToHandle(record.FirstChild, generation);
        LastChild = ToHandle(record.LastChild, generation);
        PreviousSibling = ToHandle(record.PreviousSibling, generation);
        NextSibling = ToHandle(record.NextSibling, generation);
        AttributeCount = record.AttributeCount;
        TextLength = record.TextLength;
        IsImplied = (record.Flags & 1) != 0;
    }

    private static ManagedHtmlNodeHandle ToHandle(int index, uint generation) =>
        index < 0 ? ManagedHtmlNodeHandle.Invalid :
        new ManagedHtmlNodeHandle(index, generation);

    public ManagedHtmlNodeHandle Handle { get; }
    public ManagedHtmlNodeKind Kind { get; }
    public ManagedHtmlTag Tag { get; }
    public ManagedHtmlNodeHandle Parent { get; }
    public ManagedHtmlNodeHandle FirstChild { get; }
    public ManagedHtmlNodeHandle LastChild { get; }
    public ManagedHtmlNodeHandle PreviousSibling { get; }
    public ManagedHtmlNodeHandle NextSibling { get; }
    public int AttributeCount { get; }
    public int TextLength { get; }
    public bool IsImplied { get; }
}

public readonly struct ManagedHtmlAttributeView
{
    internal ManagedHtmlAttributeView(ManagedHtmlNodeHandle owner, int index,
                                      ManagedHtmlAttributeName knownName,
                                      int nameLength, int valueLength,
                                      bool hasValue)
    {
        Owner = owner;
        Index = index;
        KnownName = knownName;
        NameLength = nameLength;
        ValueLength = valueLength;
        HasValue = hasValue;
    }

    public ManagedHtmlNodeHandle Owner { get; }
    public int Index { get; }
    public ManagedHtmlAttributeName KnownName { get; }
    public int NameLength { get; }
    public int ValueLength { get; }
    public bool HasValue { get; }
}

public readonly struct ManagedHtmlTreeBuilderProgressSnapshot
{
    internal ManagedHtmlTreeBuilderProgressSnapshot(
        ManagedHtmlTreeBuilder builder)
    {
        State = builder.State;
        FailureReason = builder.FailureReason;
        InsertionMode = builder.InsertionMode;
        TokensReceived = builder.TokensReceived;
        TokensConsumed = builder.TokensConsumed;
        NodeCount = builder.NodeCount;
        PeakNodeCount = builder.PeakNodeCount;
        ElementCount = builder.ElementCount;
        TextNodeCount = builder.TextNodeCount;
        CommentCount = builder.CommentCount;
        CommentsDiscarded = builder.CommentsDiscarded;
        AttributeCount = builder.AttributeCount;
        TextScalarsUsed = builder.TextScalarsUsed;
        PeakTextScalars = builder.PeakTextScalars;
        AttributeValueScalarsUsed = builder.AttributeValueScalarsUsed;
        PeakAttributeValueScalars = builder.PeakAttributeValueScalars;
        CurrentStackDepth = builder.CurrentStackDepth;
        PeakStackDepth = builder.PeakStackDepth;
        ImpliedElementsInserted = builder.ImpliedElementsInserted;
        UnmatchedEndTagsIgnored = builder.UnmatchedEndTagsIgnored;
        ImplicitClosesPerformed = builder.ImplicitClosesPerformed;
        DocumentRoot = builder.DocumentRoot;
        Html = builder.Html;
        Head = builder.Head;
        Body = builder.Body;
        Doctype = builder.Doctype;
        CanonicalHashAvailable = builder.CanonicalHashAvailable;
    }

    public ManagedHtmlTreeBuilderState State { get; }
    public ManagedHtmlTreeBuilderFailureReason FailureReason { get; }
    public ManagedHtmlTreeBuilderInsertionMode InsertionMode { get; }
    public int TokensReceived { get; }
    public int TokensConsumed { get; }
    public int NodeCount { get; }
    public int PeakNodeCount { get; }
    public int ElementCount { get; }
    public int TextNodeCount { get; }
    public int CommentCount { get; }
    public int CommentsDiscarded { get; }
    public int AttributeCount { get; }
    public int TextScalarsUsed { get; }
    public int PeakTextScalars { get; }
    public int AttributeValueScalarsUsed { get; }
    public int PeakAttributeValueScalars { get; }
    public int CurrentStackDepth { get; }
    public int PeakStackDepth { get; }
    public int ImpliedElementsInserted { get; }
    public int UnmatchedEndTagsIgnored { get; }
    public int ImplicitClosesPerformed { get; }
    public ManagedHtmlNodeHandle DocumentRoot { get; }
    public ManagedHtmlNodeHandle Html { get; }
    public ManagedHtmlNodeHandle Head { get; }
    public ManagedHtmlNodeHandle Body { get; }
    public ManagedHtmlNodeHandle Doctype { get; }
    public bool CanonicalHashAvailable { get; }
    public bool IsComplete => State == ManagedHtmlTreeBuilderState.Completed;
    public bool IsTerminal => State == ManagedHtmlTreeBuilderState.Completed ||
                              State == ManagedHtmlTreeBuilderState.Cancelled ||
                              State == ManagedHtmlTreeBuilderState.Failed;
}

public sealed class ManagedHtmlDocument
{
    private readonly ManagedHtmlNodeRecord[] _nodes;
    private readonly uint[] _text;
    private readonly byte[] _tagNames;
    private readonly ManagedHtmlAttributeRecord[] _attributes;
    private readonly byte[] _attributeNames;
    private readonly uint[] _attributeValues;
    private readonly byte[] _canonicalHash = new byte[ManagedSha256.DigestSize];
    private int _nodeCount;
    private int _textUsed;
    private int _tagNameUsed;
    private int _attributeCount;
    private int _attributeNameUsed;
    private int _attributeValueUsed;
    private int _root = -1;
    private int _html = -1;
    private int _head = -1;
    private int _body = -1;
    private int _doctype = -1;
    private uint _generation;
    private bool _canonicalHashAvailable;

    internal ManagedHtmlDocument(ManagedHtmlNodeRecord[] nodes, uint[] text,
                                 byte[] tagNames,
                                 ManagedHtmlAttributeRecord[] attributes,
                                 byte[] attributeNames, uint[] attributeValues,
                                 uint generation)
    {
        _nodes = nodes;
        _text = text;
        _tagNames = tagNames;
        _attributes = attributes;
        _attributeNames = attributeNames;
        _attributeValues = attributeValues;
        _generation = generation;
    }

    public int NodeCount => _nodeCount;
    public int TextScalarsUsed => _textUsed;
    public int TextScalarCapacity => _text.Length;
    public int TagNameBytesUsed => _tagNameUsed;
    public int TagNameCapacity => _tagNames.Length;
    public int AttributeCount => _attributeCount;
    public int AttributeCapacity => _attributes.Length;
    public int AttributeNameBytesUsed => _attributeNameUsed;
    public int AttributeNameCapacity => _attributeNames.Length;
    public int AttributeValueScalarsUsed => _attributeValueUsed;
    public int AttributeValueScalarCapacity => _attributeValues.Length;
    public ManagedHtmlNodeHandle DocumentNode => ToHandle(_root);
    public ManagedHtmlNodeHandle DocumentElement => ToHandle(_html);
    public ManagedHtmlNodeHandle HeadElement => ToHandle(_head);
    public ManagedHtmlNodeHandle BodyElement => ToHandle(_body);
    public ManagedHtmlNodeHandle DoctypeNode => ToHandle(_doctype);
    public bool IsHtmlDoctype => _doctype >= 0 &&
        _nodes[_doctype].NameLength == 4 &&
        _tagNames.AsSpan(_nodes[_doctype].NameOffset, 4).SequenceEqual("html"u8);
    public bool CanonicalHashAvailable => _canonicalHashAvailable;

    public bool IsValid(ManagedHtmlNodeHandle handle) =>
        handle.Generation == _generation && handle.Index >= 0 &&
        handle.Index < _nodeCount;

    public bool TryGetNode(ManagedHtmlNodeHandle handle,
                           out ManagedHtmlNodeView node)
    {
        if (!IsValid(handle))
        {
            node = default;
            return false;
        }
        node = new ManagedHtmlNodeView(handle, in _nodes[handle.Index], _generation);
        return true;
    }

    public ManagedHtmlNodeKind GetNodeKind(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) ? _nodes[handle.Index].Kind : ManagedHtmlNodeKind.Document;

    public ManagedHtmlTag GetElementTag(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) && _nodes[handle.Index].Kind == ManagedHtmlNodeKind.Element
            ? _nodes[handle.Index].Tag : ManagedHtmlTag.Unknown;

    public ManagedHtmlNodeHandle GetParent(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) ? ToHandle(_nodes[handle.Index].Parent) : ManagedHtmlNodeHandle.Invalid;
    public ManagedHtmlNodeHandle GetFirstChild(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) ? ToHandle(_nodes[handle.Index].FirstChild) : ManagedHtmlNodeHandle.Invalid;
    public ManagedHtmlNodeHandle GetLastChild(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) ? ToHandle(_nodes[handle.Index].LastChild) : ManagedHtmlNodeHandle.Invalid;
    public ManagedHtmlNodeHandle GetPreviousSibling(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) ? ToHandle(_nodes[handle.Index].PreviousSibling) : ManagedHtmlNodeHandle.Invalid;
    public ManagedHtmlNodeHandle GetNextSibling(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) ? ToHandle(_nodes[handle.Index].NextSibling) : ManagedHtmlNodeHandle.Invalid;

    public int GetTextLength(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) && _nodes[handle.Index].Kind == ManagedHtmlNodeKind.Text
            ? _nodes[handle.Index].TextLength : 0;

    public bool TryCopyText(ManagedHtmlNodeHandle handle, Span<uint> destination,
                            out int length)
    {
        length = 0;
        if (!IsValid(handle) || _nodes[handle.Index].Kind != ManagedHtmlNodeKind.Text)
            return false;
        ManagedHtmlNodeRecord node = _nodes[handle.Index];
        length = node.TextLength;
        if (destination.Length < length) return false;
        _text.AsSpan(node.TextOffset, length).CopyTo(destination);
        return true;
    }

    public bool TryCopyTagName(ManagedHtmlNodeHandle handle, Span<byte> destination,
                               out int length)
    {
        length = 0;
        if (!IsValid(handle) || (_nodes[handle.Index].Kind != ManagedHtmlNodeKind.Element &&
                                 _nodes[handle.Index].Kind != ManagedHtmlNodeKind.Doctype))
            return false;
        ManagedHtmlNodeRecord node = _nodes[handle.Index];
        ReadOnlySpan<byte> known = ManagedHtmlNames.Tag(node.Tag);
        length = known.IsEmpty ? node.NameLength : known.Length;
        if (destination.Length < length) return false;
        if (!known.IsEmpty) known.CopyTo(destination);
        else _tagNames.AsSpan(node.NameOffset, length).CopyTo(destination);
        return true;
    }

    public int GetAttributeCount(ManagedHtmlNodeHandle handle) =>
        IsValid(handle) && _nodes[handle.Index].Kind == ManagedHtmlNodeKind.Element
            ? _nodes[handle.Index].AttributeCount : 0;

    public bool TryGetAttribute(ManagedHtmlNodeHandle element, int index,
                                out ManagedHtmlAttributeView attribute)
    {
        attribute = default;
        if (!IsValid(element) || _nodes[element.Index].Kind != ManagedHtmlNodeKind.Element)
            return false;
        ManagedHtmlNodeRecord node = _nodes[element.Index];
        if (index < 0 || index >= node.AttributeCount) return false;
        ManagedHtmlAttributeRecord record = _attributes[node.FirstAttribute + index];
        attribute = new ManagedHtmlAttributeView(element, index, record.KnownName,
                                                  AttributeNameLength(record),
                                                  record.ValueLength,
                                                  (record.Flags & 1) != 0);
        return true;
    }

    public bool TryCopyAttributeName(ManagedHtmlNodeHandle element, int index,
                                     Span<byte> destination, out int length)
    {
        length = 0;
        if (!TryGetAttributeRecord(element, index, out ManagedHtmlAttributeRecord record))
            return false;
        ReadOnlySpan<byte> known = ManagedHtmlNames.Attribute(record.KnownName);
        length = known.IsEmpty ? record.NameLength : known.Length;
        if (destination.Length < length) return false;
        if (!known.IsEmpty) known.CopyTo(destination);
        else _attributeNames.AsSpan(record.NameOffset, length).CopyTo(destination);
        return true;
    }

    public bool TryCopyAttributeValue(ManagedHtmlNodeHandle element, int index,
                                      Span<uint> destination, out int length,
                                      out bool hasValue)
    {
        length = 0;
        hasValue = false;
        if (!TryGetAttributeRecord(element, index, out ManagedHtmlAttributeRecord record))
            return false;
        length = record.ValueLength;
        hasValue = (record.Flags & 1) != 0;
        if (destination.Length < length) return false;
        _attributeValues.AsSpan(record.ValueOffset, length).CopyTo(destination);
        return true;
    }

    public bool TryFindAttribute(ManagedHtmlNodeHandle element,
                                 ManagedHtmlAttributeName name,
                                 out ManagedHtmlAttributeView attribute)
    {
        attribute = default;
        if (!IsValid(element) || _nodes[element.Index].Kind != ManagedHtmlNodeKind.Element)
            return false;
        ManagedHtmlNodeRecord node = _nodes[element.Index];
        for (int index = 0; index != node.AttributeCount; ++index)
        {
            ManagedHtmlAttributeRecord record = _attributes[node.FirstAttribute + index];
            if (record.KnownName == name)
            {
                attribute = new ManagedHtmlAttributeView(element, index, record.KnownName,
                                                          AttributeNameLength(record),
                                                          record.ValueLength,
                                                          (record.Flags & 1) != 0);
                return true;
            }
        }
        return false;
    }

    public bool TryFindAttribute(ManagedHtmlNodeHandle element, ReadOnlySpan<byte> name,
                                 out ManagedHtmlAttributeView attribute)
    {
        attribute = default;
        if (!IsValid(element) || _nodes[element.Index].Kind != ManagedHtmlNodeKind.Element)
            return false;
        ManagedHtmlNodeRecord node = _nodes[element.Index];
        ManagedHtmlAttributeName knownName = ManagedHtmlNames.Attribute(name);
        for (int index = 0; index != node.AttributeCount; ++index)
        {
            ManagedHtmlAttributeRecord record = _attributes[node.FirstAttribute + index];
            if (knownName != ManagedHtmlAttributeName.Unknown && record.KnownName == knownName ||
                knownName == ManagedHtmlAttributeName.Unknown && record.KnownName == knownName &&
                record.NameLength == name.Length &&
                _attributeNames.AsSpan(record.NameOffset, record.NameLength).SequenceEqual(name))
            {
                attribute = new ManagedHtmlAttributeView(element, index, record.KnownName,
                                                          AttributeNameLength(record),
                                                          record.ValueLength,
                                                          (record.Flags & 1) != 0);
                return true;
            }
        }
        return false;
    }

    public bool Validate(out ManagedHtmlDocumentValidationFailureReason reason) =>
        ManagedHtmlDocumentValidator.Validate(this, out reason);

    public bool TryCopyCanonicalHash(Span<byte> destination)
    {
        if (!_canonicalHashAvailable || destination.Length < _canonicalHash.Length)
            return false;
        _canonicalHash.AsSpan().CopyTo(destination);
        return true;
    }

    internal ManagedHtmlNodeRecord[] Nodes => _nodes;
    internal ManagedHtmlAttributeRecord[] Attributes => _attributes;
    internal byte[] TagNames => _tagNames;
    internal byte[] AttributeNames => _attributeNames;
    internal uint[] Text => _text;
    internal uint[] AttributeValues => _attributeValues;
    internal int RootIndex => _root;
    internal int HtmlIndex => _html;
    internal int HeadIndex => _head;
    internal int BodyIndex => _body;
    internal int DoctypeIndex => _doctype;

    internal void SetMetadata(int root, int html, int head, int body, int doctype,
                              int nodeCount, int textUsed, int tagNameUsed,
                              int attributeCount, int attributeNameUsed,
                              int attributeValueUsed, uint generation)
    {
        _root = root;
        _html = html;
        _head = head;
        _body = body;
        _doctype = doctype;
        _nodeCount = nodeCount;
        _textUsed = textUsed;
        _tagNameUsed = tagNameUsed;
        _attributeCount = attributeCount;
        _attributeNameUsed = attributeNameUsed;
        _attributeValueUsed = attributeValueUsed;
        _generation = generation;
        _canonicalHashAvailable = false;
    }

    internal void SetCanonicalHash(ReadOnlySpan<byte> hash)
    {
        if (hash.Length < _canonicalHash.Length) return;
        hash[.._canonicalHash.Length].CopyTo(_canonicalHash);
        _canonicalHashAvailable = true;
    }

    private ManagedHtmlNodeHandle ToHandle(int index) =>
        index < 0 || index >= _nodeCount ? ManagedHtmlNodeHandle.Invalid :
        new ManagedHtmlNodeHandle(index, _generation);

    private bool TryGetAttributeRecord(ManagedHtmlNodeHandle element, int index,
                                       out ManagedHtmlAttributeRecord record)
    {
        record = default;
        if (!IsValid(element) || _nodes[element.Index].Kind != ManagedHtmlNodeKind.Element)
            return false;
        ManagedHtmlNodeRecord node = _nodes[element.Index];
        if (index < 0 || index >= node.AttributeCount) return false;
        record = _attributes[node.FirstAttribute + index];
        return true;
    }

    private int AttributeNameLength(in ManagedHtmlAttributeRecord record)
    {
        ReadOnlySpan<byte> known = ManagedHtmlNames.Attribute(record.KnownName);
        return known.IsEmpty ? record.NameLength : known.Length;
    }
}

internal static class ManagedHtmlNames
{
    internal static ReadOnlySpan<byte> Tag(ManagedHtmlTag tag) => tag switch
    {
        ManagedHtmlTag.Html => "html"u8,
        ManagedHtmlTag.Head => "head"u8,
        ManagedHtmlTag.Body => "body"u8,
        ManagedHtmlTag.Title => "title"u8,
        ManagedHtmlTag.Meta => "meta"u8,
        ManagedHtmlTag.Link => "link"u8,
        ManagedHtmlTag.Style => "style"u8,
        ManagedHtmlTag.Script => "script"u8,
        ManagedHtmlTag.Div => "div"u8,
        ManagedHtmlTag.Span => "span"u8,
        ManagedHtmlTag.P => "p"u8,
        ManagedHtmlTag.A => "a"u8,
        ManagedHtmlTag.Img => "img"u8,
        ManagedHtmlTag.Br => "br"u8,
        ManagedHtmlTag.Hr => "hr"u8,
        ManagedHtmlTag.H1 => "h1"u8,
        ManagedHtmlTag.H2 => "h2"u8,
        ManagedHtmlTag.H3 => "h3"u8,
        ManagedHtmlTag.H4 => "h4"u8,
        ManagedHtmlTag.H5 => "h5"u8,
        ManagedHtmlTag.H6 => "h6"u8,
        ManagedHtmlTag.Ul => "ul"u8,
        ManagedHtmlTag.Ol => "ol"u8,
        ManagedHtmlTag.Li => "li"u8,
        ManagedHtmlTag.Table => "table"u8,
        ManagedHtmlTag.Thead => "thead"u8,
        ManagedHtmlTag.Tbody => "tbody"u8,
        ManagedHtmlTag.Tfoot => "tfoot"u8,
        ManagedHtmlTag.Tr => "tr"u8,
        ManagedHtmlTag.Td => "td"u8,
        ManagedHtmlTag.Th => "th"u8,
        ManagedHtmlTag.Form => "form"u8,
        ManagedHtmlTag.Input => "input"u8,
        ManagedHtmlTag.Button => "button"u8,
        ManagedHtmlTag.Label => "label"u8,
        ManagedHtmlTag.Select => "select"u8,
        ManagedHtmlTag.Option => "option"u8,
        ManagedHtmlTag.Textarea => "textarea"u8,
        ManagedHtmlTag.Pre => "pre"u8,
        ManagedHtmlTag.Code => "code"u8,
        ManagedHtmlTag.Strong => "strong"u8,
        ManagedHtmlTag.Em => "em"u8,
        ManagedHtmlTag.Base => "base"u8,
        ManagedHtmlTag.Colgroup => "colgroup"u8,
        ManagedHtmlTag.Col => "col"u8,
        ManagedHtmlTag.Caption => "caption"u8,
        ManagedHtmlTag.Area => "area"u8,
        ManagedHtmlTag.Embed => "embed"u8,
        ManagedHtmlTag.Param => "param"u8,
        ManagedHtmlTag.Source => "source"u8,
        ManagedHtmlTag.Track => "track"u8,
        ManagedHtmlTag.Wbr => "wbr"u8,
        ManagedHtmlTag.Main => "main"u8,
        ManagedHtmlTag.Header => "header"u8,
        ManagedHtmlTag.Footer => "footer"u8,
        ManagedHtmlTag.Section => "section"u8,
        ManagedHtmlTag.Article => "article"u8,
        ManagedHtmlTag.Aside => "aside"u8,
        ManagedHtmlTag.Nav => "nav"u8,
        ManagedHtmlTag.Blockquote => "blockquote"u8,
        ManagedHtmlTag.Dl => "dl"u8,
        ManagedHtmlTag.Dt => "dt"u8,
        ManagedHtmlTag.Dd => "dd"u8,
        ManagedHtmlTag.Fieldset => "fieldset"u8,
        ManagedHtmlTag.Legend => "legend"u8,
        ManagedHtmlTag.Hgroup => "hgroup"u8,
        ManagedHtmlTag.Menu => "menu"u8,
        ManagedHtmlTag.Address => "address"u8,
        _ => ReadOnlySpan<byte>.Empty
    };

    internal static ReadOnlySpan<byte> Attribute(ManagedHtmlAttributeName name) => name switch
    {
        ManagedHtmlAttributeName.Id => "id"u8,
        ManagedHtmlAttributeName.Class => "class"u8,
        ManagedHtmlAttributeName.Style => "style"u8,
        ManagedHtmlAttributeName.Href => "href"u8,
        ManagedHtmlAttributeName.Src => "src"u8,
        ManagedHtmlAttributeName.Title => "title"u8,
        ManagedHtmlAttributeName.Name => "name"u8,
        ManagedHtmlAttributeName.Type => "type"u8,
        ManagedHtmlAttributeName.Value => "value"u8,
        ManagedHtmlAttributeName.Width => "width"u8,
        ManagedHtmlAttributeName.Height => "height"u8,
        ManagedHtmlAttributeName.Disabled => "disabled"u8,
        ManagedHtmlAttributeName.Checked => "checked"u8,
        ManagedHtmlAttributeName.Selected => "selected"u8,
        ManagedHtmlAttributeName.Colspan => "colspan"u8,
        ManagedHtmlAttributeName.Rowspan => "rowspan"u8,
        ManagedHtmlAttributeName.Alt => "alt"u8,
        ManagedHtmlAttributeName.Action => "action"u8,
        ManagedHtmlAttributeName.Method => "method"u8,
        ManagedHtmlAttributeName.Required => "required"u8,
        ManagedHtmlAttributeName.For => "for"u8,
        ManagedHtmlAttributeName.Rel => "rel"u8,
        ManagedHtmlAttributeName.Charset => "charset"u8,
        ManagedHtmlAttributeName.Lang => "lang"u8,
        ManagedHtmlAttributeName.Role => "role"u8,
        _ => ReadOnlySpan<byte>.Empty
    };

    internal static ManagedHtmlTag Tag(ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("html"u8)) return ManagedHtmlTag.Html;
        if (name.SequenceEqual("head"u8)) return ManagedHtmlTag.Head;
        if (name.SequenceEqual("body"u8)) return ManagedHtmlTag.Body;
        if (name.SequenceEqual("title"u8)) return ManagedHtmlTag.Title;
        if (name.SequenceEqual("meta"u8)) return ManagedHtmlTag.Meta;
        if (name.SequenceEqual("link"u8)) return ManagedHtmlTag.Link;
        if (name.SequenceEqual("style"u8)) return ManagedHtmlTag.Style;
        if (name.SequenceEqual("script"u8)) return ManagedHtmlTag.Script;
        if (name.SequenceEqual("div"u8)) return ManagedHtmlTag.Div;
        if (name.SequenceEqual("span"u8)) return ManagedHtmlTag.Span;
        if (name.SequenceEqual("p"u8)) return ManagedHtmlTag.P;
        if (name.SequenceEqual("a"u8)) return ManagedHtmlTag.A;
        if (name.SequenceEqual("img"u8)) return ManagedHtmlTag.Img;
        if (name.SequenceEqual("br"u8)) return ManagedHtmlTag.Br;
        if (name.SequenceEqual("hr"u8)) return ManagedHtmlTag.Hr;
        if (name.SequenceEqual("h1"u8)) return ManagedHtmlTag.H1;
        if (name.SequenceEqual("h2"u8)) return ManagedHtmlTag.H2;
        if (name.SequenceEqual("h3"u8)) return ManagedHtmlTag.H3;
        if (name.SequenceEqual("h4"u8)) return ManagedHtmlTag.H4;
        if (name.SequenceEqual("h5"u8)) return ManagedHtmlTag.H5;
        if (name.SequenceEqual("h6"u8)) return ManagedHtmlTag.H6;
        if (name.SequenceEqual("ul"u8)) return ManagedHtmlTag.Ul;
        if (name.SequenceEqual("ol"u8)) return ManagedHtmlTag.Ol;
        if (name.SequenceEqual("li"u8)) return ManagedHtmlTag.Li;
        if (name.SequenceEqual("table"u8)) return ManagedHtmlTag.Table;
        if (name.SequenceEqual("thead"u8)) return ManagedHtmlTag.Thead;
        if (name.SequenceEqual("tbody"u8)) return ManagedHtmlTag.Tbody;
        if (name.SequenceEqual("tfoot"u8)) return ManagedHtmlTag.Tfoot;
        if (name.SequenceEqual("tr"u8)) return ManagedHtmlTag.Tr;
        if (name.SequenceEqual("td"u8)) return ManagedHtmlTag.Td;
        if (name.SequenceEqual("th"u8)) return ManagedHtmlTag.Th;
        if (name.SequenceEqual("form"u8)) return ManagedHtmlTag.Form;
        if (name.SequenceEqual("input"u8)) return ManagedHtmlTag.Input;
        if (name.SequenceEqual("button"u8)) return ManagedHtmlTag.Button;
        if (name.SequenceEqual("label"u8)) return ManagedHtmlTag.Label;
        if (name.SequenceEqual("select"u8)) return ManagedHtmlTag.Select;
        if (name.SequenceEqual("option"u8)) return ManagedHtmlTag.Option;
        if (name.SequenceEqual("textarea"u8)) return ManagedHtmlTag.Textarea;
        if (name.SequenceEqual("pre"u8)) return ManagedHtmlTag.Pre;
        if (name.SequenceEqual("code"u8)) return ManagedHtmlTag.Code;
        if (name.SequenceEqual("strong"u8)) return ManagedHtmlTag.Strong;
        if (name.SequenceEqual("em"u8)) return ManagedHtmlTag.Em;
        if (name.SequenceEqual("base"u8)) return ManagedHtmlTag.Base;
        if (name.SequenceEqual("colgroup"u8)) return ManagedHtmlTag.Colgroup;
        if (name.SequenceEqual("col"u8)) return ManagedHtmlTag.Col;
        if (name.SequenceEqual("caption"u8)) return ManagedHtmlTag.Caption;
        if (name.SequenceEqual("area"u8)) return ManagedHtmlTag.Area;
        if (name.SequenceEqual("embed"u8)) return ManagedHtmlTag.Embed;
        if (name.SequenceEqual("param"u8)) return ManagedHtmlTag.Param;
        if (name.SequenceEqual("source"u8)) return ManagedHtmlTag.Source;
        if (name.SequenceEqual("track"u8)) return ManagedHtmlTag.Track;
        if (name.SequenceEqual("wbr"u8)) return ManagedHtmlTag.Wbr;
        if (name.SequenceEqual("main"u8)) return ManagedHtmlTag.Main;
        if (name.SequenceEqual("header"u8)) return ManagedHtmlTag.Header;
        if (name.SequenceEqual("footer"u8)) return ManagedHtmlTag.Footer;
        if (name.SequenceEqual("section"u8)) return ManagedHtmlTag.Section;
        if (name.SequenceEqual("article"u8)) return ManagedHtmlTag.Article;
        if (name.SequenceEqual("aside"u8)) return ManagedHtmlTag.Aside;
        if (name.SequenceEqual("nav"u8)) return ManagedHtmlTag.Nav;
        if (name.SequenceEqual("blockquote"u8)) return ManagedHtmlTag.Blockquote;
        if (name.SequenceEqual("dl"u8)) return ManagedHtmlTag.Dl;
        if (name.SequenceEqual("dt"u8)) return ManagedHtmlTag.Dt;
        if (name.SequenceEqual("dd"u8)) return ManagedHtmlTag.Dd;
        if (name.SequenceEqual("fieldset"u8)) return ManagedHtmlTag.Fieldset;
        if (name.SequenceEqual("legend"u8)) return ManagedHtmlTag.Legend;
        if (name.SequenceEqual("hgroup"u8)) return ManagedHtmlTag.Hgroup;
        if (name.SequenceEqual("menu"u8)) return ManagedHtmlTag.Menu;
        if (name.SequenceEqual("address"u8)) return ManagedHtmlTag.Address;
        return ManagedHtmlTag.Unknown;
    }

    internal static ManagedHtmlAttributeName Attribute(ReadOnlySpan<byte> name)
    {
        if (name.SequenceEqual("id"u8)) return ManagedHtmlAttributeName.Id;
        if (name.SequenceEqual("class"u8)) return ManagedHtmlAttributeName.Class;
        if (name.SequenceEqual("style"u8)) return ManagedHtmlAttributeName.Style;
        if (name.SequenceEqual("href"u8)) return ManagedHtmlAttributeName.Href;
        if (name.SequenceEqual("src"u8)) return ManagedHtmlAttributeName.Src;
        if (name.SequenceEqual("title"u8)) return ManagedHtmlAttributeName.Title;
        if (name.SequenceEqual("name"u8)) return ManagedHtmlAttributeName.Name;
        if (name.SequenceEqual("type"u8)) return ManagedHtmlAttributeName.Type;
        if (name.SequenceEqual("value"u8)) return ManagedHtmlAttributeName.Value;
        if (name.SequenceEqual("width"u8)) return ManagedHtmlAttributeName.Width;
        if (name.SequenceEqual("height"u8)) return ManagedHtmlAttributeName.Height;
        if (name.SequenceEqual("disabled"u8)) return ManagedHtmlAttributeName.Disabled;
        if (name.SequenceEqual("checked"u8)) return ManagedHtmlAttributeName.Checked;
        if (name.SequenceEqual("selected"u8)) return ManagedHtmlAttributeName.Selected;
        if (name.SequenceEqual("colspan"u8)) return ManagedHtmlAttributeName.Colspan;
        if (name.SequenceEqual("rowspan"u8)) return ManagedHtmlAttributeName.Rowspan;
        if (name.SequenceEqual("alt"u8)) return ManagedHtmlAttributeName.Alt;
        if (name.SequenceEqual("action"u8)) return ManagedHtmlAttributeName.Action;
        if (name.SequenceEqual("method"u8)) return ManagedHtmlAttributeName.Method;
        if (name.SequenceEqual("required"u8)) return ManagedHtmlAttributeName.Required;
        if (name.SequenceEqual("for"u8)) return ManagedHtmlAttributeName.For;
        if (name.SequenceEqual("rel"u8)) return ManagedHtmlAttributeName.Rel;
        if (name.SequenceEqual("charset"u8)) return ManagedHtmlAttributeName.Charset;
        if (name.SequenceEqual("lang"u8)) return ManagedHtmlAttributeName.Lang;
        if (name.SequenceEqual("role"u8)) return ManagedHtmlAttributeName.Role;
        return ManagedHtmlAttributeName.Unknown;
    }
}

public sealed class ManagedHtmlTreeBuilder : IManagedHtmlTokenConsumer
{
    private const byte ImpliedFlag = 1;
    private const byte HasValueFlag = 1;
    private readonly ManagedHtmlNodeRecord[] _nodes;
    private readonly uint[] _text;
    private readonly byte[] _tagNames;
    private readonly ManagedHtmlAttributeRecord[] _attributes;
    private readonly byte[] _attributeNames;
    private readonly uint[] _attributeValues;
    private readonly int[] _openElements;
    private readonly byte[] _tagScratch = new byte[ManagedHtmlTokenizerLimits.MaximumTagNameLength];
    private readonly byte[] _attributeNameScratch = new byte[ManagedHtmlTokenizerLimits.MaximumAttributeNameLength];
    private readonly uint[] _attributeValueScratch = new uint[ManagedHtmlTokenizerLimits.MaximumAttributeValueLength];
    private readonly byte[] _canonicalHash = new byte[ManagedSha256.DigestSize];
    private readonly ManagedSha256 _hash = new();
    private readonly ManagedHtmlDocument _document;
    private uint _generation = 1;
    private ManagedHtmlTreeBuilderState _state;
    private ManagedHtmlTreeBuilderFailureReason _failureReason;
    private ManagedHtmlTreeBuilderInsertionMode _mode;
    private ManagedHtmlTreeBuilderInsertionMode _textReturnMode;
    private int _textElement = -1;
    private int _nodeCount;
    private int _peakNodeCount;
    private int _elementCount;
    private int _textNodeCount;
    private int _commentCount;
    private int _commentsDiscarded;
    private int _textUsed;
    private int _peakText;
    private int _tagNameUsed;
    private int _attributeCount;
    private int _attributeNameUsed;
    private int _attributeValueUsed;
    private int _peakAttributeValue;
    private int _openCount;
    private int _peakDepth;
    private int _root = -1;
    private int _html = -1;
    private int _head = -1;
    private int _body = -1;
    private int _doctype = -1;
    private int _tokensReceived;
    private int _tokensConsumed;
    private int _impliedElements;
    private int _unmatchedEndTags;
    private int _implicitCloses;
    private bool _eofSeen;
    private bool _hashAvailable;

    public ManagedHtmlTreeBuilder() : this(ManagedHtmlDocumentArenaOptions.Default) { }

    public ManagedHtmlTreeBuilder(ManagedHtmlDocumentArenaOptions options)
    {
        _nodes = new ManagedHtmlNodeRecord[options.NodeCapacity];
        _text = new uint[options.TextScalarCapacity];
        _tagNames = new byte[options.TagNameCapacity];
        _attributes = new ManagedHtmlAttributeRecord[options.AttributeCapacity];
        _attributeNames = new byte[options.AttributeNameCapacity];
        _attributeValues = new uint[options.AttributeValueCapacity];
        _openElements = new int[options.TreeDepthCapacity];
        _document = new ManagedHtmlDocument(_nodes, _text, _tagNames,
                                            _attributes, _attributeNames,
                                            _attributeValues, _generation);
        Reset();
    }

    public ManagedHtmlTreeBuilder(int nodeCapacity, int textScalarCapacity,
                                 int attributeCapacity, int attributeValueCapacity,
                                 int treeDepthCapacity)
        : this(new ManagedHtmlDocumentArenaOptions(nodeCapacity, textScalarCapacity,
                                                   attributeCapacity, attributeValueCapacity,
                                                   treeDepthCapacity)) { }

    public ManagedHtmlDocument Document => _document;
    public int NodeCapacity => _nodes.Length;
    public int TextScalarCapacity => _text.Length;
    public int TagNameCapacity => _tagNames.Length;
    public int AttributeCapacity => _attributes.Length;
    public int AttributeNameCapacity => _attributeNames.Length;
    public int AttributeValueScalarCapacity => _attributeValues.Length;
    public int TreeDepthCapacity => _openElements.Length;
    public ManagedHtmlTreeBuilderState State => _state;
    public ManagedHtmlTreeBuilderFailureReason FailureReason => _failureReason;
    public ManagedHtmlTreeBuilderInsertionMode InsertionMode => _mode;
    public int TokensReceived => _tokensReceived;
    public int TokensConsumed => _tokensConsumed;
    public int TokensProcessed => _tokensConsumed;
    public int NodeCount => _nodeCount;
    public int PeakNodeCount => _peakNodeCount;
    public int ElementCount => _elementCount;
    public int TextNodeCount => _textNodeCount;
    public int CommentCount => _commentCount;
    public int CommentsDiscarded => _commentsDiscarded;
    public int AttributeCount => _attributeCount;
    public int TextScalarsUsed => _textUsed;
    public int PeakTextScalars => _peakText;
    public int AttributeValueScalarsUsed => _attributeValueUsed;
    public int PeakAttributeValueScalars => _peakAttributeValue;
    public int CurrentStackDepth => _openCount;
    public int PeakStackDepth => _peakDepth;
    public bool EndOfFileSeen => _eofSeen;
    public int ImpliedElementsInserted => _impliedElements;
    public int UnmatchedEndTagsIgnored => _unmatchedEndTags;
    public int ImplicitClosesPerformed => _implicitCloses;
    public ManagedHtmlNodeHandle DocumentRoot => Handle(_root);
    public ManagedHtmlNodeHandle Html => Handle(_html);
    public ManagedHtmlNodeHandle Head => Handle(_head);
    public ManagedHtmlNodeHandle Body => Handle(_body);
    public ManagedHtmlNodeHandle Doctype => Handle(_doctype);
    public bool CanonicalHashAvailable => _hashAvailable;
    public ManagedHtmlTreeBuilderProgressSnapshot Progress => new(this);
    public ManagedHtmlTokenConsumerState ConsumerState => _state switch
    {
        ManagedHtmlTreeBuilderState.Idle => ManagedHtmlTokenConsumerState.Idle,
        ManagedHtmlTreeBuilderState.Receiving => ManagedHtmlTokenConsumerState.Receiving,
        ManagedHtmlTreeBuilderState.Paused => ManagedHtmlTokenConsumerState.Paused,
        ManagedHtmlTreeBuilderState.Completed => ManagedHtmlTokenConsumerState.Completed,
        ManagedHtmlTreeBuilderState.Cancelled => ManagedHtmlTokenConsumerState.Cancelled,
        _ => ManagedHtmlTokenConsumerState.Failed
    };
    public ManagedHtmlTokenConsumerState StateForConsumer => ConsumerState;
    public ManagedHtmlTokenConsumerFailureReason ConsumerFailureReason =>
        _failureReason == ManagedHtmlTreeBuilderFailureReason.None
            ? ManagedHtmlTokenConsumerFailureReason.None
            : ManagedHtmlTokenConsumerFailureReason.ConsumerFailure;

    ManagedHtmlTokenConsumerState IManagedHtmlTokenConsumer.State => ConsumerState;
    ManagedHtmlTokenConsumerFailureReason IManagedHtmlTokenConsumer.FailureReason =>
        ConsumerFailureReason;

    public ManagedHttpBodySinkResult Consume(in ManagedHtmlToken token)
    {
        ++_tokensReceived;
        if (_state == ManagedHtmlTreeBuilderState.Cancelled ||
            _state == ManagedHtmlTreeBuilderState.Failed ||
            _state == ManagedHtmlTreeBuilderState.Completed)
            return ManagedHttpBodySinkResult.Fail;
        if (_state == ManagedHtmlTreeBuilderState.Paused)
            return ManagedHttpBodySinkResult.Pause;
        _state = ManagedHtmlTreeBuilderState.Receiving;
        bool success = token.Kind switch
        {
            ManagedHtmlTokenKind.Text => ProcessText(in token),
            ManagedHtmlTokenKind.StartTag => ProcessStartTag(in token),
            ManagedHtmlTokenKind.EndTag => ProcessEndTag(in token),
            ManagedHtmlTokenKind.Comment => ProcessComment(in token),
            ManagedHtmlTokenKind.Doctype => ProcessDoctype(in token),
            ManagedHtmlTokenKind.EndOfFile => ProcessEndOfFile(),
            _ => Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState)
        };
        if (!success) return ManagedHttpBodySinkResult.Fail;
        ++_tokensConsumed;
        return ManagedHttpBodySinkResult.Continue;
    }

    public bool Complete()
    {
        if (_state == ManagedHtmlTreeBuilderState.Cancelled ||
            _state == ManagedHtmlTreeBuilderState.Failed)
            return false;
        if (_state == ManagedHtmlTreeBuilderState.Completed) return true;
        if (_state == ManagedHtmlTreeBuilderState.Paused) return false;
        if (!EnsureDocumentSkeleton()) return false;
        _openCount = 0;
        _textElement = -1;
        _mode = ManagedHtmlTreeBuilderInsertionMode.AfterAfterBody;
        if (!BuildCanonicalHash()) return false;
        _state = ManagedHtmlTreeBuilderState.Completed;
        return true;
    }

    public void Cancel()
    {
        if (_state == ManagedHtmlTreeBuilderState.Completed ||
            _state == ManagedHtmlTreeBuilderState.Failed)
            return;
        _state = ManagedHtmlTreeBuilderState.Cancelled;
        _failureReason = ManagedHtmlTreeBuilderFailureReason.Cancelled;
        _openCount = 0;
        _textElement = -1;
        Publish();
    }

    public void Reset()
    {
        _nodes.AsSpan().Clear();
        _text.AsSpan().Clear();
        _tagNames.AsSpan().Clear();
        _attributes.AsSpan().Clear();
        _attributeNames.AsSpan().Clear();
        _attributeValues.AsSpan().Clear();
        _openElements.AsSpan().Clear();
        ++_generation;
        if (_generation == 0) _generation = 1;
        _state = ManagedHtmlTreeBuilderState.Idle;
        _failureReason = ManagedHtmlTreeBuilderFailureReason.None;
        _mode = ManagedHtmlTreeBuilderInsertionMode.Initial;
        _textReturnMode = ManagedHtmlTreeBuilderInsertionMode.InBody;
        _textElement = -1;
        _nodeCount = 0;
        _peakNodeCount = 0;
        _elementCount = 0;
        _textNodeCount = 0;
        _commentCount = 0;
        _commentsDiscarded = 0;
        _textUsed = 0;
        _peakText = 0;
        _tagNameUsed = 0;
        _attributeCount = 0;
        _attributeNameUsed = 0;
        _attributeValueUsed = 0;
        _peakAttributeValue = 0;
        _openCount = 0;
        _peakDepth = 0;
        _root = -1;
        _html = -1;
        _head = -1;
        _body = -1;
        _doctype = -1;
        _tokensReceived = 0;
        _tokensConsumed = 0;
        _impliedElements = 0;
        _unmatchedEndTags = 0;
        _implicitCloses = 0;
        _eofSeen = false;
        _hashAvailable = false;
        _hash.Reset();
        Publish();
    }

    public void RequestPause()
    {
        if (_state == ManagedHtmlTreeBuilderState.Receiving)
            _state = ManagedHtmlTreeBuilderState.Paused;
    }

    public void Resume()
    {
        if (_state == ManagedHtmlTreeBuilderState.Paused)
            _state = ManagedHtmlTreeBuilderState.Receiving;
    }

    public bool TryCopyCanonicalHash(Span<byte> destination)
    {
        if (!_hashAvailable || destination.Length < _canonicalHash.Length) return false;
        _canonicalHash.AsSpan().CopyTo(destination);
        return true;
    }

    public bool Validate(out ManagedHtmlDocumentValidationFailureReason reason)
    {
        if (!ManagedHtmlDocumentValidator.Validate(_document, out reason)) return false;
        for (int index = 0; index != _openCount; ++index)
        {
            int node = _openElements[index];
            if (node < 0 || node >= _nodeCount ||
                _nodes[node].Kind != ManagedHtmlNodeKind.Element)
            {
                reason = ManagedHtmlDocumentValidationFailureReason.ParentOutOfRange;
                return false;
            }
        }
        reason = ManagedHtmlDocumentValidationFailureReason.None;
        return true;
    }

    private bool ProcessEndOfFile()
    {
        _eofSeen = true;
        return true;
    }

    private bool ProcessComment(in ManagedHtmlToken token)
    {
        ++_commentCount;
        ++_commentsDiscarded;
        return true;
    }

    private bool ProcessDoctype(in ManagedHtmlToken token)
    {
        if (!EnsureRoot()) return false;
        if (_doctype >= 0) return true;
        int length = token.DoctypeNameLength;
        if (length <= 0 || length > _tagNames.Length - _tagNameUsed)
            return Fail(ManagedHtmlTreeBuilderFailureReason.TagNameCapacityExceeded);
        if (!token.TryCopyDoctypeName(_tagScratch, out length))
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        if (!TryReserveTagName(length, out int offset)) return false;
        _tagScratch.AsSpan(0, length).CopyTo(_tagNames.AsSpan(offset));
        int node = AllocateNode(ManagedHtmlNodeKind.Doctype, ManagedHtmlTag.Unknown,
                                _root, offset, length, 0, 0, DoctypeFlags(length));
        if (node < 0) return false;
        _doctype = node;
        Publish();
        return true;
    }

    private byte DoctypeFlags(int length) => length == 4 &&
        _tagScratch.AsSpan(0, length).SequenceEqual("html"u8) ? (byte)2 : (byte)0;

    private bool ProcessText(in ManagedHtmlToken token)
    {
        if (!EnsureRoot()) return false;
        int length = token.TextLength;
        if (length == 0) return true;
        if (length > _text.Length - _textUsed)
            return Fail(ManagedHtmlTreeBuilderFailureReason.TextCapacityExceeded);
        if (!token.TryCopyText(_attributeValueScratch, out length))
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        if (_mode == ManagedHtmlTreeBuilderInsertionMode.InHead &&
            !IsAllHtmlWhitespace(_attributeValueScratch.AsSpan(0, length)))
        {
            if (!CloseHead() || !EnsureBodyOpen()) return false;
        }
        else if (_mode == ManagedHtmlTreeBuilderInsertionMode.Initial ||
                 _mode == ManagedHtmlTreeBuilderInsertionMode.BeforeHtml ||
                 _mode == ManagedHtmlTreeBuilderInsertionMode.BeforeHead ||
                 _mode == ManagedHtmlTreeBuilderInsertionMode.AfterHead ||
                 _mode == ManagedHtmlTreeBuilderInsertionMode.AfterBody ||
                 _mode == ManagedHtmlTreeBuilderInsertionMode.AfterAfterBody)
        {
            if (!EnsureBodyOpen()) return false;
        }
        int parent = CurrentParent();
        if (parent < 0)
        {
            if (!EnsureBodyOpen()) return false;
            parent = CurrentParent();
        }
        if (parent < 0 || parent >= _nodeCount)
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        int last = _nodes[parent].LastChild;
        if (last >= 0 && _nodes[last].Kind == ManagedHtmlNodeKind.Text &&
            _nodes[last].TextOffset + _nodes[last].TextLength == _textUsed)
        {
            _attributeValueScratch.AsSpan(0, length).CopyTo(_text.AsSpan(_textUsed));
            ManagedHtmlNodeRecord record = _nodes[last];
            record.TextLength += length;
            _nodes[last] = record;
            _textUsed += length;
        }
        else
        {
            if (_nodeCount == _nodes.Length)
                return Fail(ManagedHtmlTreeBuilderFailureReason.NodeCapacityExceeded);
            int offset = _textUsed;
            _attributeValueScratch.AsSpan(0, length).CopyTo(_text.AsSpan(offset));
            int node = AllocateNode(ManagedHtmlNodeKind.Text, ManagedHtmlTag.Unknown,
                                    parent, 0, 0, 0, 0, 0);
            if (node < 0) return false;
            ManagedHtmlNodeRecord record = _nodes[node];
            record.TextOffset = offset;
            record.TextLength = length;
            _nodes[node] = record;
            ++_textNodeCount;
            _textUsed += length;
        }
        if (_textUsed > _peakText) _peakText = _textUsed;
        Publish();
        return true;
    }

    private bool ProcessStartTag(in ManagedHtmlToken token)
    {
        if (!token.TryCopyTagName(_tagScratch, out int tagLength) || tagLength == 0)
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        ReadOnlySpan<byte> name = _tagScratch.AsSpan(0, tagLength);
        ManagedHtmlTag tag = ManagedHtmlNames.Tag(name);
        if (!EnsureRoot()) return false;
        switch (_mode)
        {
            case ManagedHtmlTreeBuilderInsertionMode.Initial:
            case ManagedHtmlTreeBuilderInsertionMode.BeforeHtml:
                return StartBeforeHtml(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.BeforeHead:
                return StartBeforeHead(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.InHead:
                return StartInHead(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.AfterHead:
                return StartAfterHead(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.InBody:
            case ManagedHtmlTreeBuilderInsertionMode.AfterAfterBody:
            case ManagedHtmlTreeBuilderInsertionMode.AfterBody:
                return StartInBody(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.Text:
                return StartInBody(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.InTable:
                return StartInTable(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.InTableBody:
                return StartInTableBody(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.InRow:
                return StartInRow(in token, tag, name);
            case ManagedHtmlTreeBuilderInsertionMode.InCell:
                return StartInCell(in token, tag, name);
            default:
                return Fail(ManagedHtmlTreeBuilderFailureReason.UnsupportedInsertionModeCase);
        }
    }

    private bool StartBeforeHtml(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                 ReadOnlySpan<byte> name)
    {
        if (tag == ManagedHtmlTag.Html)
        {
            if (_html >= 0) return true;
            if (!TryCreateElement(in token, tag, name, _root, false, out _html)) return false;
            if (!Push(_html)) return false;
            _mode = ManagedHtmlTreeBuilderInsertionMode.BeforeHead;
            return true;
        }
        if (!EnsureHtmlOpen()) return false;
        return StartBeforeHead(in token, tag, name);
    }

    private bool StartBeforeHead(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                 ReadOnlySpan<byte> name)
    {
        if (tag == ManagedHtmlTag.Html) return true;
        if (tag == ManagedHtmlTag.Head)
        {
            if (_head < 0)
            {
                if (!TryCreateElement(in token, tag, name, _html, false, out _head) ||
                    !Push(_head)) return false;
            }
            else if (!EnsureHeadOpen()) return false;
            _mode = ManagedHtmlTreeBuilderInsertionMode.InHead;
            return true;
        }
        if (IsHeadTag(tag))
        {
            if (!EnsureHeadOpen()) return false;
            return StartHeadChild(in token, tag, name);
        }
        if (!EnsureHeadOpen() || !CloseHead()) return false;
        return StartAfterHead(in token, tag, name);
    }

    private bool StartInHead(in ManagedHtmlToken token, ManagedHtmlTag tag,
                             ReadOnlySpan<byte> name)
    {
        if (tag == ManagedHtmlTag.Head) return true;
        if (IsHeadTag(tag)) return StartHeadChild(in token, tag, name);
        if (tag == ManagedHtmlTag.Html) return true;
        if (!CloseHead()) return false;
        return StartAfterHead(in token, tag, name);
    }

    private bool StartAfterHead(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                ReadOnlySpan<byte> name)
    {
        if (IsHeadTag(tag)) return StartLateHeadChild(in token, tag, name);
        if (tag == ManagedHtmlTag.Body)
        {
            if (_body >= 0) return true;
            if (!EnsureHtmlOpen() || !TryCreateElement(in token, tag, name, _html,
                                                       false, out _body)) return false;
            if (!Push(_body)) return false;
            _mode = ManagedHtmlTreeBuilderInsertionMode.InBody;
            return true;
        }
        if (tag == ManagedHtmlTag.Html) return true;
        if (!EnsureBodyOpen()) return false;
        return StartInBody(in token, tag, name);
    }

    private bool StartHeadChild(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                ReadOnlySpan<byte> name)
    {
        if (!EnsureHeadOpen()) return false;
        if (CurrentTag() == tag && !IsVoid(tag) && tag != ManagedHtmlTag.Head)
        {
            CloseTop(true);
        }
        int parent = _head;
        if (!TryCreateElement(in token, tag, name, parent, !IsVoid(tag), out int node)) return false;
        if (!IsVoid(tag) && !Push(node)) return false;
        if (IsRawText(tag))
        {
            _textElement = node;
            _textReturnMode = ManagedHtmlTreeBuilderInsertionMode.InHead;
            _mode = ManagedHtmlTreeBuilderInsertionMode.Text;
        }
        else _mode = ManagedHtmlTreeBuilderInsertionMode.InHead;
        return true;
    }

    private bool StartLateHeadChild(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                    ReadOnlySpan<byte> name)
    {
        int returnMode = (int)_mode;
        int parent = _head;
        if (parent < 0 && !EnsureHeadNode()) return false;
        if (!TryCreateElement(in token, tag, name, parent, !IsVoid(tag), out int node)) return false;
        if (!IsVoid(tag) && !Push(node)) return false;
        if (IsRawText(tag))
        {
            _textElement = node;
            _textReturnMode = (ManagedHtmlTreeBuilderInsertionMode)returnMode;
            _mode = ManagedHtmlTreeBuilderInsertionMode.Text;
        }
        return true;
    }

    private bool StartInBody(in ManagedHtmlToken token, ManagedHtmlTag tag,
                             ReadOnlySpan<byte> name)
    {
        if (tag == ManagedHtmlTag.Html || tag == ManagedHtmlTag.Head) return true;
        if (tag == ManagedHtmlTag.Body)
        {
            if (_body < 0) return EnsureBodyOpen(in token, tag, name);
            return true;
        }
        if (IsHeadTag(tag)) return StartLateHeadChild(in token, tag, name);
        if (!EnsureBodyOpen()) return false;
        if (tag == ManagedHtmlTag.P)
        {
            CloseOpenTag(ManagedHtmlTag.P, default, true);
        }
        else if (tag == ManagedHtmlTag.Li)
        {
            CloseOpenTag(ManagedHtmlTag.Li, default, true);
        }
        else if (IsBlockTag(tag))
        {
            CloseOpenTag(ManagedHtmlTag.P, default, true);
        }
        if (tag == ManagedHtmlTag.Table)
        {
            if (!TryCreateElement(in token, tag, name, CurrentParent(), true, out int table)) return false;
            if (!Push(table)) return false;
            _mode = ManagedHtmlTreeBuilderInsertionMode.InTable;
            return true;
        }
        if (IsTableSection(tag)) return StartTableSection(in token, tag, name);
        if (tag == ManagedHtmlTag.Tr) return StartTableRow(in token, tag, name);
        if (tag == ManagedHtmlTag.Td || tag == ManagedHtmlTag.Th)
            return StartTableCell(in token, tag, name);
        bool push = !IsVoid(tag);
        if (!TryCreateElement(in token, tag, name, CurrentParent(), push, out int node)) return false;
        if (push && !Push(node)) return false;
        if (IsRawText(tag))
        {
            _textElement = node;
            _textReturnMode = ManagedHtmlTreeBuilderInsertionMode.InBody;
            _mode = ManagedHtmlTreeBuilderInsertionMode.Text;
        }
        else _mode = ManagedHtmlTreeBuilderInsertionMode.InBody;
        return true;
    }

    private bool StartInTable(in ManagedHtmlToken token, ManagedHtmlTag tag,
                              ReadOnlySpan<byte> name)
    {
        if (IsTableSection(tag)) return StartTableSection(in token, tag, name);
        if (tag == ManagedHtmlTag.Tr) return StartTableRow(in token, tag, name);
        if (tag == ManagedHtmlTag.Td || tag == ManagedHtmlTag.Th)
            return StartTableCell(in token, tag, name);
        if (tag == ManagedHtmlTag.Table)
        {
            CloseOpenTag(ManagedHtmlTag.Table, default, true);
            return StartInBody(in token, tag, name);
        }
        /* Reduced-table policy: unsupported table content is retained under
           the current table node; no foster-parenting is attempted. */
        return StartInBody(in token, tag, name);
    }

    private bool StartInTableBody(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                  ReadOnlySpan<byte> name)
    {
        if (tag == ManagedHtmlTag.Tr) return StartTableRow(in token, tag, name);
        if (tag == ManagedHtmlTag.Td || tag == ManagedHtmlTag.Th)
        {
            if (!StartTableRow(default, ManagedHtmlTag.Tr, "tr"u8)) return false;
            return StartTableCell(in token, tag, name);
        }
        if (IsTableSection(tag))
        {
            CloseOpenTag(CurrentTag(), default, true);
            return StartTableSection(in token, tag, name);
        }
        return StartInBody(in token, tag, name);
    }

    private bool StartInRow(in ManagedHtmlToken token, ManagedHtmlTag tag,
                            ReadOnlySpan<byte> name)
    {
        if (tag == ManagedHtmlTag.Td || tag == ManagedHtmlTag.Th)
            return StartTableCell(in token, tag, name);
        if (tag == ManagedHtmlTag.Tr)
        {
            CloseOpenTag(ManagedHtmlTag.Tr, default, true);
            return StartTableRow(in token, tag, name);
        }
        return StartInBody(in token, tag, name);
    }

    private bool StartInCell(in ManagedHtmlToken token, ManagedHtmlTag tag,
                             ReadOnlySpan<byte> name)
    {
        if (tag == ManagedHtmlTag.Td || tag == ManagedHtmlTag.Th)
        {
            CloseCell();
            return StartTableCell(in token, tag, name);
        }
        if (tag == ManagedHtmlTag.Tr)
        {
            CloseCell();
            return StartTableRow(in token, tag, name);
        }
        return StartInBody(in token, tag, name);
    }

    private bool StartTableSection(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                   ReadOnlySpan<byte> name)
    {
        if (!EnsureTableOpen()) return false;
        if (CurrentTag() == ManagedHtmlTag.Tbody || CurrentTag() == ManagedHtmlTag.Thead ||
            CurrentTag() == ManagedHtmlTag.Tfoot)
            CloseTop(true);
        if (!TryCreateElement(in token, tag, name, CurrentParent(), true, out int node)) return false;
        if (!Push(node)) return false;
        _mode = ManagedHtmlTreeBuilderInsertionMode.InTableBody;
        return true;
    }

    private bool StartTableRow(in ManagedHtmlToken token, ManagedHtmlTag tag,
                               ReadOnlySpan<byte> name)
    {
        if (!EnsureTableOpen()) return false;
        if (CurrentTag() != ManagedHtmlTag.Tbody && CurrentTag() != ManagedHtmlTag.Thead &&
            CurrentTag() != ManagedHtmlTag.Tfoot)
        {
            if (!StartTableSection(default, ManagedHtmlTag.Tbody, "tbody"u8)) return false;
        }
        if (!TryCreateElement(in token, tag, name, CurrentParent(), true, out int node)) return false;
        if (!Push(node)) return false;
        _mode = ManagedHtmlTreeBuilderInsertionMode.InRow;
        return true;
    }

    private bool StartTableCell(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                ReadOnlySpan<byte> name)
    {
        if (!EnsureTableOpen()) return false;
        if (CurrentTag() != ManagedHtmlTag.Tr)
        {
            if (!StartTableRow(default, ManagedHtmlTag.Tr, "tr"u8)) return false;
        }
        if (!TryCreateElement(in token, tag, name, CurrentParent(), true, out int node)) return false;
        if (!Push(node)) return false;
        _mode = ManagedHtmlTreeBuilderInsertionMode.InCell;
        return true;
    }

    private bool ProcessEndTag(in ManagedHtmlToken token)
    {
        if (!token.TryCopyTagName(_tagScratch, out int length) || length == 0)
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        ReadOnlySpan<byte> name = _tagScratch.AsSpan(0, length);
        ManagedHtmlTag tag = ManagedHtmlNames.Tag(name);
        if (_mode == ManagedHtmlTreeBuilderInsertionMode.Text)
        {
            if (_textElement >= 0 && NameEquals(_textElement, tag, name))
            {
                CloseOpenTag(tag, name, false);
                _textElement = -1;
                _mode = _textReturnMode;
                return true;
            }
            ++_unmatchedEndTags;
            return true;
        }
        if (tag == ManagedHtmlTag.Head)
        {
            if (!CloseHead()) ++_unmatchedEndTags;
            else _mode = ManagedHtmlTreeBuilderInsertionMode.AfterHead;
            return true;
        }
        if (tag == ManagedHtmlTag.Body)
        {
            if (!CloseOpenTag(tag, name, false)) ++_unmatchedEndTags;
            else _mode = ManagedHtmlTreeBuilderInsertionMode.AfterBody;
            return true;
        }
        if (tag == ManagedHtmlTag.Html)
        {
            CloseOpenTag(ManagedHtmlTag.Body, default, true);
            if (!CloseOpenTag(tag, name, false)) ++_unmatchedEndTags;
            else _mode = ManagedHtmlTreeBuilderInsertionMode.AfterAfterBody;
            return true;
        }
        if (tag == ManagedHtmlTag.Td || tag == ManagedHtmlTag.Th)
        {
            if (!CloseOpenTag(tag, name, false)) ++_unmatchedEndTags;
            else _mode = ManagedHtmlTreeBuilderInsertionMode.InRow;
            return true;
        }
        if (tag == ManagedHtmlTag.Tr)
        {
            if (!CloseOpenTag(tag, name, false)) ++_unmatchedEndTags;
            else _mode = ManagedHtmlTreeBuilderInsertionMode.InTableBody;
            return true;
        }
        if (tag == ManagedHtmlTag.Table)
        {
            if (!CloseOpenTag(tag, name, false)) ++_unmatchedEndTags;
            else _mode = ManagedHtmlTreeBuilderInsertionMode.InBody;
            return true;
        }
        if (tag == ManagedHtmlTag.Tbody || tag == ManagedHtmlTag.Thead ||
            tag == ManagedHtmlTag.Tfoot)
        {
            if (!CloseOpenTag(tag, name, false)) ++_unmatchedEndTags;
            else _mode = ManagedHtmlTreeBuilderInsertionMode.InTable;
            return true;
        }
        if (!CloseOpenTag(tag, name, false)) ++_unmatchedEndTags;
        else SetModeAfterClose();
        return true;
    }

    private bool EnsureDocumentSkeleton()
    {
        if (!EnsureRoot() || !EnsureHtmlOpen()) return false;
        if (_head < 0 && !EnsureHeadNode()) return false;
        if (_body < 0)
        {
            if (!CloseHead()) return false;
            if (!TryCreateImplied(ManagedHtmlTag.Body, _html, out _body)) return false;
        }
        return true;
    }

    private bool EnsureRoot()
    {
        if (_root >= 0) return true;
        int node = AllocateNode(ManagedHtmlNodeKind.Document, ManagedHtmlTag.Unknown,
                                -1, 0, 0, 0, 0, 0);
        if (node < 0) return false;
        _root = node;
        _mode = ManagedHtmlTreeBuilderInsertionMode.BeforeHtml;
        Publish();
        return true;
    }

    private bool EnsureHtmlOpen()
    {
        if (_html < 0)
        {
            if (!TryCreateImplied(ManagedHtmlTag.Html, _root, out _html)) return false;
            if (!Push(_html)) return false;
        }
        else if (FindOpen(_html) < 0 && !Push(_html)) return false;
        _mode = _mode == ManagedHtmlTreeBuilderInsertionMode.Initial ||
                _mode == ManagedHtmlTreeBuilderInsertionMode.BeforeHtml
            ? ManagedHtmlTreeBuilderInsertionMode.BeforeHead : _mode;
        return true;
    }

    private bool EnsureHeadNode()
    {
        if (_head >= 0) return true;
        if (_html < 0 && !EnsureHtmlOpen()) return false;
        if (!TryCreateImplied(ManagedHtmlTag.Head, _html, out _head)) return false;
        return true;
    }

    private bool EnsureHeadOpen()
    {
        if (!EnsureHeadNode()) return false;
        if (FindOpen(_head) >= 0) return true;
        if (!TryPushExisting(_head)) return false;
        _mode = ManagedHtmlTreeBuilderInsertionMode.InHead;
        return true;
    }

    private bool EnsureBodyOpen()
    {
        if (!EnsureHtmlOpen()) return false;
        if (FindOpen(_head) >= 0 && !CloseHead()) return false;
        if (_body < 0)
        {
            if (!TryCreateImplied(ManagedHtmlTag.Body, _html, out _body)) return false;
            if (!Push(_body)) return false;
        }
        else if (FindOpen(_body) < 0 && !TryPushExisting(_body)) return false;
        _mode = ManagedHtmlTreeBuilderInsertionMode.InBody;
        return true;
    }

    private bool EnsureBodyOpen(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                ReadOnlySpan<byte> name)
    {
        if (!EnsureHtmlOpen()) return false;
        if (FindOpen(_head) >= 0 && !CloseHead()) return false;
        if (_body >= 0) return true;
        if (!TryCreateElement(in token, tag, name, _html, false, out _body)) return false;
        if (!Push(_body)) return false;
        _mode = ManagedHtmlTreeBuilderInsertionMode.InBody;
        return true;
    }

    private bool EnsureTableOpen()
    {
        if (!EnsureBodyOpen()) return false;
        if (FindOpenTag(ManagedHtmlTag.Table, default) >= 0) return true;
        if (!TryCreateImplied(ManagedHtmlTag.Table, CurrentParent(), out int table)) return false;
        if (!Push(table)) return false;
        ++_impliedElements;
        _mode = ManagedHtmlTreeBuilderInsertionMode.InTable;
        return true;
    }

    private bool CloseHead()
    {
        if (_head < 0) return true;
        int position = FindOpen(_head);
        if (position < 0) return true;
        CloseToPosition(position, true);
        _mode = ManagedHtmlTreeBuilderInsertionMode.AfterHead;
        return true;
    }

    private bool TryCreateImplied(ManagedHtmlTag tag, int parent, out int node)
    {
        node = -1;
        if (!TryCreateKnownElement(tag, parent, out node)) return false;
        _nodes[node].Flags |= ImpliedFlag;
        ++_impliedElements;
        if (tag == ManagedHtmlTag.Html) _html = node;
        else if (tag == ManagedHtmlTag.Head) _head = node;
        else if (tag == ManagedHtmlTag.Body) _body = node;
        Publish();
        return true;
    }

    private bool TryCreateKnownElement(ManagedHtmlTag tag, int parent, out int node)
    {
        return TryCreateElementCore(ManagedHtmlNodeKind.Element, tag, ReadOnlySpan<byte>.Empty,
                                    parent, false, 0, 0, 0, out node);
    }

    private bool TryCreateElement(in ManagedHtmlToken token, ManagedHtmlTag tag,
                                  ReadOnlySpan<byte> name, int parent, bool push,
                                  out int node)
    {
        node = -1;
        if (parent < 0 || parent >= _nodeCount)
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        int unknownTagLength = tag == ManagedHtmlTag.Unknown ? name.Length : 0;
        int unknownAttributeNames = 0;
        int values = 0;
        if (token.AttributeCount > _attributes.Length - _attributeCount)
            return Fail(ManagedHtmlTreeBuilderFailureReason.AttributeCapacityExceeded);
        for (int index = 0; index != token.AttributeCount; ++index)
        {
            if (!token.TryCopyAttributeName(index, _attributeNameScratch, out int nameLength))
                return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
            ManagedHtmlAttributeName known = ManagedHtmlNames.Attribute(
                _attributeNameScratch.AsSpan(0, nameLength));
            if (known == ManagedHtmlAttributeName.Unknown)
                unknownAttributeNames += nameLength;
            if (!token.TryCopyAttributeValue(index, _attributeValueScratch,
                                             out int valueLength, out _))
                return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
            values += valueLength;
        }
        if (unknownTagLength > _tagNames.Length - _tagNameUsed)
            return Fail(ManagedHtmlTreeBuilderFailureReason.TagNameCapacityExceeded);
        if (unknownAttributeNames > _attributeNames.Length - _attributeNameUsed)
            return Fail(ManagedHtmlTreeBuilderFailureReason.AttributeNameCapacityExceeded);
        if (values > _attributeValues.Length - _attributeValueUsed)
            return Fail(ManagedHtmlTreeBuilderFailureReason.AttributeValueCapacityExceeded);
        if (push && _openCount == _openElements.Length)
            return Fail(ManagedHtmlTreeBuilderFailureReason.TreeDepthExceeded);
        return TryCreateElementCore(ManagedHtmlNodeKind.Element, tag, name, parent, push,
                                    unknownTagLength, unknownAttributeNames, values,
                                    out node, in token);
    }

    private bool TryCreateElementCore(ManagedHtmlNodeKind kind, ManagedHtmlTag tag,
                                      ReadOnlySpan<byte> name, int parent, bool push,
                                      int unknownTagLength, int unknownAttributeNames,
                                      int values, out int node,
                                      in ManagedHtmlToken token = default)
    {
        node = -1;
        if (_nodeCount == _nodes.Length)
            return Fail(ManagedHtmlTreeBuilderFailureReason.NodeCapacityExceeded);
        int firstAttribute = _attributeCount;
        int tagLength = unknownTagLength;
        int nameOffset = 0;
        if (unknownTagLength != 0)
        {
            nameOffset = _tagNameUsed;
            name.CopyTo(_tagNames.AsSpan(_tagNameUsed));
            _tagNameUsed += unknownTagLength;
        }
        node = AllocateNode(kind, tag, parent, nameOffset, tagLength,
                            firstAttribute, token.AttributeCount, 0);
        if (node < 0) return false;
        if (token.AttributeCount != 0)
        {
            for (int index = 0; index != token.AttributeCount; ++index)
            {
                if (!token.TryCopyAttributeName(index, _attributeNameScratch,
                                                out int attributeNameLength))
                    return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
                ManagedHtmlAttributeName known = ManagedHtmlNames.Attribute(
                    _attributeNameScratch.AsSpan(0, attributeNameLength));
                int attributeNameOffset = _attributeNameUsed;
                if (known == ManagedHtmlAttributeName.Unknown)
                {
                    _attributeNameScratch.AsSpan(0, attributeNameLength).CopyTo(
                        _attributeNames.AsSpan(_attributeNameUsed));
                    _attributeNameUsed += attributeNameLength;
                    attributeNameOffset = _attributeNameUsed - attributeNameLength;
                }
                if (!token.TryCopyAttributeValue(index, _attributeValueScratch,
                                                 out int valueLength, out bool hasValue))
                    return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
                int valueOffset = _attributeValueUsed;
                _attributeValueScratch.AsSpan(0, valueLength).CopyTo(
                    _attributeValues.AsSpan(_attributeValueUsed));
                _attributeValueUsed += valueLength;
                _attributes[_attributeCount++] = new ManagedHtmlAttributeRecord
                {
                    Owner = node,
                    KnownName = known,
                    NameOffset = attributeNameOffset,
                    NameLength = known == ManagedHtmlAttributeName.Unknown ? attributeNameLength : 0,
                    ValueOffset = valueOffset,
                    ValueLength = valueLength,
                    Flags = hasValue ? HasValueFlag : (byte)0
                };
                if (_attributeValueUsed > _peakAttributeValue)
                    _peakAttributeValue = _attributeValueUsed;
            }
            ManagedHtmlNodeRecord record = _nodes[node];
            record.FirstAttribute = firstAttribute;
            record.AttributeCount = token.AttributeCount;
            _nodes[node] = record;
        }
        if (kind == ManagedHtmlNodeKind.Element) ++_elementCount;
        /* The caller pushes after all element-owned storage has been copied.
           Keeping allocation and stack mutation separate makes depth failure
           and partial-token failure fail closed. */
        Publish();
        return true;
    }

    private int AllocateNode(ManagedHtmlNodeKind kind, ManagedHtmlTag tag, int parent,
                             int nameOffset, int nameLength, int firstAttribute,
                             int attributeCount, byte flags)
    {
        if (_nodeCount == _nodes.Length)
        {
            Fail(ManagedHtmlTreeBuilderFailureReason.NodeCapacityExceeded);
            return -1;
        }
        int node = _nodeCount++;
        _nodes[node] = new ManagedHtmlNodeRecord
        {
            Kind = kind,
            Tag = tag,
            Parent = parent,
            FirstChild = -1,
            LastChild = -1,
            PreviousSibling = -1,
            NextSibling = -1,
            NameOffset = nameOffset,
            NameLength = nameLength,
            FirstAttribute = firstAttribute,
            AttributeCount = attributeCount,
            TextOffset = 0,
            TextLength = 0,
            Flags = flags
        };
        if (_nodeCount > _peakNodeCount) _peakNodeCount = _nodeCount;
        if (parent >= 0)
        {
            if (parent >= _nodeCount || !AppendChild(parent, node))
            {
                Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
                return -1;
            }
        }
        Publish();
        return node;
    }

    private bool AppendChild(int parent, int child)
    {
        ManagedHtmlNodeRecord parentRecord = _nodes[parent];
        if (parentRecord.LastChild >= 0)
        {
            int previous = parentRecord.LastChild;
            if (previous >= _nodeCount) return false;
            ManagedHtmlNodeRecord previousRecord = _nodes[previous];
            previousRecord.NextSibling = child;
            _nodes[previous] = previousRecord;
            ManagedHtmlNodeRecord childRecord = _nodes[child];
            childRecord.PreviousSibling = previous;
            _nodes[child] = childRecord;
        }
        else parentRecord.FirstChild = child;
        parentRecord.LastChild = child;
        _nodes[parent] = parentRecord;
        return true;
    }

    private bool TryPushExisting(int node)
    {
        if (node < 0 || node >= _nodeCount || _nodes[node].Kind != ManagedHtmlNodeKind.Element)
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        if (_openCount == _openElements.Length)
            return Fail(ManagedHtmlTreeBuilderFailureReason.TreeDepthExceeded);
        _openElements[_openCount++] = node;
        if (_openCount > _peakDepth) _peakDepth = _openCount;
        return true;
    }

    private bool Push(int node) => TryPushExisting(node);

    private bool CloseOpenTag(ManagedHtmlTag tag, ReadOnlySpan<byte> name, bool implicitClose)
    {
        int position = FindOpenTag(tag, name);
        if (position < 0) return false;
        if (implicitClose) _implicitCloses += _openCount - position;
        CloseToPosition(position, false);
        return true;
    }

    private void CloseToPosition(int position, bool implicitClose)
    {
        if (position < 0 || position >= _openCount) return;
        if (implicitClose && _openCount - position > 1)
            _implicitCloses += _openCount - position - 1;
        _openCount = position;
        if (_openCount == 0) _mode = ManagedHtmlTreeBuilderInsertionMode.AfterBody;
    }

    private void CloseTop(bool implicitClose)
    {
        if (_openCount == 0) return;
        if (implicitClose) ++_implicitCloses;
        --_openCount;
    }

    private void CloseCell()
    {
        if (CurrentTag() == ManagedHtmlTag.Td || CurrentTag() == ManagedHtmlTag.Th)
            CloseTop(true);
        if (CurrentTag() == ManagedHtmlTag.Tr) CloseTop(true);
        _mode = ManagedHtmlTreeBuilderInsertionMode.InTableBody;
    }

    private void SetModeAfterClose()
    {
        ManagedHtmlTag current = CurrentTag();
        if (current == ManagedHtmlTag.Table) _mode = ManagedHtmlTreeBuilderInsertionMode.InTable;
        else if (current == ManagedHtmlTag.Tbody || current == ManagedHtmlTag.Thead ||
                 current == ManagedHtmlTag.Tfoot) _mode = ManagedHtmlTreeBuilderInsertionMode.InTableBody;
        else if (current == ManagedHtmlTag.Tr) _mode = ManagedHtmlTreeBuilderInsertionMode.InRow;
        else if (current == ManagedHtmlTag.Td || current == ManagedHtmlTag.Th) _mode = ManagedHtmlTreeBuilderInsertionMode.InCell;
        else if (current == ManagedHtmlTag.Body) _mode = ManagedHtmlTreeBuilderInsertionMode.AfterBody;
        else _mode = ManagedHtmlTreeBuilderInsertionMode.InBody;
    }

    private int FindOpen(int node)
    {
        for (int index = _openCount - 1; index >= 0; --index)
            if (_openElements[index] == node) return index;
        return -1;
    }

    private int FindOpenTag(ManagedHtmlTag tag, ReadOnlySpan<byte> name)
    {
        for (int index = _openCount - 1; index >= 0; --index)
            if (NameEquals(_openElements[index], tag, name)) return index;
        return -1;
    }

    private bool NameEquals(int node, ManagedHtmlTag tag, ReadOnlySpan<byte> name)
    {
        if (node < 0 || node >= _nodeCount || _nodes[node].Kind != ManagedHtmlNodeKind.Element)
            return false;
        if (tag != ManagedHtmlTag.Unknown) return _nodes[node].Tag == tag;
        ManagedHtmlNodeRecord record = _nodes[node];
        return record.Tag == ManagedHtmlTag.Unknown && record.NameLength == name.Length &&
               _tagNames.AsSpan(record.NameOffset, record.NameLength).SequenceEqual(name);
    }

    private ManagedHtmlTag CurrentTag() => _openCount == 0
        ? ManagedHtmlTag.Unknown : _nodes[_openElements[_openCount - 1]].Tag;

    private int CurrentParent() => _openCount == 0 ? _html : _openElements[_openCount - 1];

    private ManagedHtmlNodeHandle Handle(int index) =>
        index < 0 || index >= _nodeCount ? ManagedHtmlNodeHandle.Invalid :
        new ManagedHtmlNodeHandle(index, _generation);

    private bool TryReserveTagName(int length, out int offset)
    {
        offset = _tagNameUsed;
        if (length < 0 || length > _tagNames.Length - _tagNameUsed)
            return Fail(ManagedHtmlTreeBuilderFailureReason.TagNameCapacityExceeded);
        _tagNameUsed += length;
        return true;
    }

    private bool BuildCanonicalHash()
    {
        _hash.Reset();
        if (!_hash.Append("GXOS-P43\0"u8)) return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        if (!AppendInt(_nodeCount) || !AppendInt(_attributeCount) || !AppendInt(_textUsed))
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        Span<byte> header = stackalloc byte[26];
        Span<byte> flags = stackalloc byte[1];
        for (int index = 0; index != _nodeCount; ++index)
        {
            ManagedHtmlNodeRecord node = _nodes[index];
            header[0] = (byte)node.Kind;
            header[1] = (byte)node.Tag;
            WriteInt(header[2..], node.Parent);
            WriteInt(header[6..], node.FirstChild);
            WriteInt(header[10..], node.LastChild);
            WriteInt(header[14..], node.PreviousSibling);
            WriteInt(header[18..], node.NextSibling);
            WriteInt(header[22..], node.AttributeCount);
            if (!_hash.Append(header)) return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
            ReadOnlySpan<byte> tagName = ManagedHtmlNames.Tag(node.Tag);
            if (tagName.IsEmpty && node.NameLength != 0)
                tagName = _tagNames.AsSpan(node.NameOffset, node.NameLength);
            if (!AppendLengthAndBytes(tagName)) return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
            if (node.Kind == ManagedHtmlNodeKind.Text)
            {
                for (int scalar = 0; scalar != node.TextLength; ++scalar)
                    if (!AppendScalar(_text[node.TextOffset + scalar]))
                        return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
            }
            for (int attribute = 0; attribute != node.AttributeCount; ++attribute)
            {
                ManagedHtmlAttributeRecord record = _attributes[node.FirstAttribute + attribute];
                ReadOnlySpan<byte> attributeName = ManagedHtmlNames.Attribute(record.KnownName);
                if (attributeName.IsEmpty && record.NameLength != 0)
                    attributeName = _attributeNames.AsSpan(record.NameOffset, record.NameLength);
                flags[0] = record.Flags;
                if (!AppendLengthAndBytes(attributeName) || !_hash.Append(flags))
                    return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
                for (int scalar = 0; scalar != record.ValueLength; ++scalar)
                    if (!AppendScalar(_attributeValues[record.ValueOffset + scalar]))
                        return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
            }
        }
        if (!_hash.TryFinalize(_canonicalHash))
            return Fail(ManagedHtmlTreeBuilderFailureReason.InvalidTreeState);
        _hashAvailable = true;
        _document.SetCanonicalHash(_canonicalHash);
        return true;
    }

    private bool AppendLengthAndBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[4];
        WriteInt(length, bytes.Length);
        return _hash.Append(length) && _hash.Append(bytes);
    }

    private bool AppendInt(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        WriteInt(buffer, value);
        return _hash.Append(buffer);
    }

    private bool AppendScalar(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        buffer[0] = (byte)(value >> 24);
        buffer[1] = (byte)(value >> 16);
        buffer[2] = (byte)(value >> 8);
        buffer[3] = (byte)value;
        return _hash.Append(buffer);
    }

    private static void WriteInt(Span<byte> destination, int value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    private bool Fail(ManagedHtmlTreeBuilderFailureReason reason)
    {
        _failureReason = reason;
        _state = ManagedHtmlTreeBuilderState.Failed;
        _openCount = 0;
        _textElement = -1;
        Publish();
        return false;
    }

    private void Publish()
    {
        _document.SetMetadata(_root, _html, _head, _body, _doctype,
                              _nodeCount, _textUsed, _tagNameUsed,
                              _attributeCount, _attributeNameUsed,
                              _attributeValueUsed, _generation);
    }

    private static bool IsVoid(ManagedHtmlTag tag) => tag == ManagedHtmlTag.Br ||
        tag == ManagedHtmlTag.Img || tag == ManagedHtmlTag.Meta || tag == ManagedHtmlTag.Link ||
        tag == ManagedHtmlTag.Input || tag == ManagedHtmlTag.Hr || tag == ManagedHtmlTag.Base ||
        tag == ManagedHtmlTag.Area || tag == ManagedHtmlTag.Embed || tag == ManagedHtmlTag.Param ||
        tag == ManagedHtmlTag.Source || tag == ManagedHtmlTag.Track || tag == ManagedHtmlTag.Wbr ||
        tag == ManagedHtmlTag.Col;

    private static bool IsAllHtmlWhitespace(ReadOnlySpan<uint> scalars)
    {
        for (int index = 0; index != scalars.Length; ++index)
        {
            uint scalar = scalars[index];
            if (scalar != 0x09 && scalar != 0x0A && scalar != 0x0C &&
                scalar != 0x0D && scalar != 0x20)
                return false;
        }
        return true;
    }

    private static bool IsRawText(ManagedHtmlTag tag) => tag == ManagedHtmlTag.Title ||
        tag == ManagedHtmlTag.Style || tag == ManagedHtmlTag.Script ||
        tag == ManagedHtmlTag.Textarea;

    private static bool IsHeadTag(ManagedHtmlTag tag) => tag == ManagedHtmlTag.Title ||
        tag == ManagedHtmlTag.Meta || tag == ManagedHtmlTag.Link || tag == ManagedHtmlTag.Style ||
        tag == ManagedHtmlTag.Script || tag == ManagedHtmlTag.Base;

    private static bool IsTableSection(ManagedHtmlTag tag) => tag == ManagedHtmlTag.Tbody ||
        tag == ManagedHtmlTag.Thead || tag == ManagedHtmlTag.Tfoot;

    private static bool IsBlockTag(ManagedHtmlTag tag) => tag == ManagedHtmlTag.Div ||
        tag == ManagedHtmlTag.P || tag == ManagedHtmlTag.H1 || tag == ManagedHtmlTag.H2 ||
        tag == ManagedHtmlTag.H3 || tag == ManagedHtmlTag.H4 || tag == ManagedHtmlTag.H5 ||
        tag == ManagedHtmlTag.H6 || tag == ManagedHtmlTag.Ul || tag == ManagedHtmlTag.Ol ||
        tag == ManagedHtmlTag.Table || tag == ManagedHtmlTag.Form || tag == ManagedHtmlTag.Pre ||
        tag == ManagedHtmlTag.Blockquote || tag == ManagedHtmlTag.Section ||
        tag == ManagedHtmlTag.Article || tag == ManagedHtmlTag.Aside ||
        tag == ManagedHtmlTag.Header || tag == ManagedHtmlTag.Footer || tag == ManagedHtmlTag.Main ||
        tag == ManagedHtmlTag.Nav || tag == ManagedHtmlTag.Dl || tag == ManagedHtmlTag.Fieldset ||
        tag == ManagedHtmlTag.Menu || tag == ManagedHtmlTag.Address;
}

internal static class ManagedHtmlDocumentValidator
{
    internal static bool Validate(ManagedHtmlDocument document,
                                  out ManagedHtmlDocumentValidationFailureReason reason)
    {
        reason = ManagedHtmlDocumentValidationFailureReason.None;
        int count = document.NodeCount;
        if (count == 0) return true;
        int root = document.RootIndex;
        if (root < 0 || root >= count || document.Nodes[root].Kind != ManagedHtmlNodeKind.Document ||
            document.Nodes[root].Parent != -1)
            return Fail(ManagedHtmlDocumentValidationFailureReason.RootInvalid, out reason);
        for (int index = 0; index != count; ++index)
        {
            ManagedHtmlNodeRecord node = document.Nodes[index];
            if ((byte)node.Kind > (byte)ManagedHtmlNodeKind.Comment)
                return Fail(ManagedHtmlDocumentValidationFailureReason.NodeKindInvalid, out reason);
            if (node.Parent < -1 || node.Parent >= count)
                return Fail(ManagedHtmlDocumentValidationFailureReason.ParentOutOfRange, out reason);
            if (!CheckRange(node.FirstChild, count) || !CheckRange(node.LastChild, count) ||
                !CheckRange(node.PreviousSibling, count) || !CheckRange(node.NextSibling, count))
                return Fail(ManagedHtmlDocumentValidationFailureReason.ChildOutOfRange, out reason);
            if (node.Kind == ManagedHtmlNodeKind.Text &&
                (node.TextOffset < 0 || node.TextLength < 0 ||
                 node.TextOffset > document.Text.Length - node.TextLength))
                return Fail(ManagedHtmlDocumentValidationFailureReason.TextRangeInvalid, out reason);
            if (node.Kind == ManagedHtmlNodeKind.Element &&
                (node.AttributeCount < 0 || node.FirstAttribute < 0 ||
                 node.FirstAttribute > document.Attributes.Length - node.AttributeCount))
                return Fail(ManagedHtmlDocumentValidationFailureReason.AttributeRangeInvalid, out reason);
            if (!CheckChildren(document, index, node, out reason)) return false;
            if (node.Kind == ManagedHtmlNodeKind.Element)
            {
                for (int attribute = 0; attribute != node.AttributeCount; ++attribute)
                {
                    ManagedHtmlAttributeRecord record =
                        document.Attributes[node.FirstAttribute + attribute];
                    if (record.Owner != index)
                        return Fail(ManagedHtmlDocumentValidationFailureReason.AttributeOwnerMismatch, out reason);
                    if (record.KnownName == ManagedHtmlAttributeName.Unknown &&
                        (record.NameOffset < 0 || record.NameLength < 0 ||
                         record.NameOffset > document.AttributeNames.Length - record.NameLength))
                        return Fail(ManagedHtmlDocumentValidationFailureReason.AttributeNameRangeInvalid, out reason);
                    if (record.ValueOffset < 0 || record.ValueLength < 0 ||
                        record.ValueOffset > document.AttributeValues.Length - record.ValueLength)
                        return Fail(ManagedHtmlDocumentValidationFailureReason.AttributeValueRangeInvalid, out reason);
                }
            }
        }
        return true;
    }

    private static bool CheckChildren(ManagedHtmlDocument document, int parent,
                                      in ManagedHtmlNodeRecord record,
                                      out ManagedHtmlDocumentValidationFailureReason reason)
    {
        reason = ManagedHtmlDocumentValidationFailureReason.None;
        if ((record.FirstChild < 0) != (record.LastChild < 0))
            return Fail(ManagedHtmlDocumentValidationFailureReason.FirstLastMismatch, out reason);
        int slow = record.FirstChild;
        int fast = record.FirstChild;
        while (fast >= 0)
        {
            if (fast >= document.NodeCount) return Fail(ManagedHtmlDocumentValidationFailureReason.ChildOutOfRange, out reason);
            fast = document.Nodes[fast].NextSibling;
            if (fast >= 0)
            {
                if (fast >= document.NodeCount) return Fail(ManagedHtmlDocumentValidationFailureReason.ChildOutOfRange, out reason);
                fast = document.Nodes[fast].NextSibling;
                slow = slow < 0 ? -1 : document.Nodes[slow].NextSibling;
                if (slow >= 0 && slow == fast)
                    return Fail(ManagedHtmlDocumentValidationFailureReason.SiblingCycle, out reason);
            }
        }
        int previous = -1;
        int child = record.FirstChild;
        int visited = 0;
        while (child >= 0)
        {
            if (child >= document.NodeCount || ++visited > document.NodeCount)
                return Fail(ManagedHtmlDocumentValidationFailureReason.SiblingCycle, out reason);
            ManagedHtmlNodeRecord childRecord = document.Nodes[child];
            if (childRecord.Parent != parent)
                return Fail(ManagedHtmlDocumentValidationFailureReason.ParentLinkMismatch, out reason);
            if (childRecord.PreviousSibling != previous)
                return Fail(ManagedHtmlDocumentValidationFailureReason.SiblingLinkMismatch, out reason);
            previous = child;
            child = childRecord.NextSibling;
        }
        if (previous != record.LastChild)
            return Fail(ManagedHtmlDocumentValidationFailureReason.FirstLastMismatch, out reason);
        return true;
    }

    private static bool CheckRange(int index, int count) => index >= -1 && index < count;

    private static bool Fail(ManagedHtmlDocumentValidationFailureReason value,
                             out ManagedHtmlDocumentValidationFailureReason reason)
    {
        reason = value;
        return false;
    }
}

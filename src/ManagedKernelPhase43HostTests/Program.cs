using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;
    private static ManagedHtmlTokenizer? s_lastTokenizer;

    private static int Main()
    {
        try
        {
            BasicDocuments();
            StructureAndRelationships();
            ImpliedAndRecovery();
            AttributesAndNames();
            TextAndFragmentation();
            VoidAndRawText();
            TablesAndComments();
            LimitsAndFailures();
            CancellationAndReset();
            PauseAndValidatorCoverage();
            CanonicalHash();
            FixtureSummary();
            Console.WriteLine($"MANAGED_KERNEL_PHASE43_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE43_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void BasicDocuments()
    {
        ManagedHtmlTreeBuilder empty = Parse("");
        Check(empty.State == ManagedHtmlTreeBuilderState.Completed, "empty-complete");
        Check(empty.NodeCount == 4 && empty.Html != ManagedHtmlNodeHandle.Invalid &&
              empty.Head != ManagedHtmlNodeHandle.Invalid && empty.Body != ManagedHtmlNodeHandle.Invalid,
              "empty-implied-skeleton");
        Check(empty.Document.DocumentNode != ManagedHtmlNodeHandle.Invalid &&
              empty.Document.GetNodeKind(empty.Document.DocumentNode) == ManagedHtmlNodeKind.Document,
              "one-document-root");
        ManagedHtmlTreeBuilder explicitDocument = Parse(
            "<!doctype html><html><head><title>Hello</title></head><body><p>World</p></body></html>");
        Check(explicitDocument.Document.IsHtmlDoctype && explicitDocument.NodeCount == 9,
              "explicit-document");
        Check(Tag(explicitDocument, explicitDocument.Html) == "html" &&
              Tag(explicitDocument, explicitDocument.Head) == "head" &&
              Tag(explicitDocument, explicitDocument.Body) == "body", "explicit-handles");
        ManagedHtmlTreeBuilder missing = Parse("<title>x</title><p>Hello");
        Check(Tag(missing, missing.Html) == "html" && Tag(missing, missing.Head) == "head" &&
              Tag(missing, missing.Body) == "body" && FindTag(missing, ManagedHtmlTag.P) != ManagedHtmlNodeHandle.Invalid,
              "missing-structure");
        Check(Parse("plain text").TextScalarsUsed == 10, "implicit-body-text");
    }

    private static void StructureAndRelationships()
    {
        ManagedHtmlTreeBuilder builder = Parse("<div id=a><span>one</span><span>two</span></div>tail");
        ManagedHtmlNodeHandle div = FindTag(builder, ManagedHtmlTag.Div);
        ManagedHtmlNodeHandle first = builder.Document.GetFirstChild(builder.Body);
        Check(first == div && builder.Document.GetParent(first) == builder.Body,
              "first-child-parent");
        ManagedHtmlNodeHandle span1 = builder.Document.GetFirstChild(div);
        ManagedHtmlNodeHandle span2 = builder.Document.GetNextSibling(span1);
        Check(Tag(builder, span1) == "span" && Tag(builder, span2) == "span" &&
              builder.Document.GetPreviousSibling(span2) == span1 &&
              builder.Document.GetLastChild(div) == span2 &&
              builder.Document.GetParent(span2) == div, "sibling-links");
        Check(builder.Document.GetNextSibling(div) != ManagedHtmlNodeHandle.Invalid &&
              builder.Document.GetNodeKind(builder.Document.GetNextSibling(div)) == ManagedHtmlNodeKind.Text,
              "tail-sibling");
        Check(builder.Validate(out ManagedHtmlDocumentValidationFailureReason reason) &&
              reason == ManagedHtmlDocumentValidationFailureReason.None, "valid-tree");
    }

    private static void ImpliedAndRecovery()
    {
        ManagedHtmlTreeBuilder builder = Parse("<p>one<div>two</div><p>three<li>a<li>b</ul></main>");
        Check(builder.ImpliedElementsInserted >= 3 && builder.ImplicitClosesPerformed >= 2,
              "implied-and-implicit-closes");
        Check(builder.UnmatchedEndTagsIgnored >= 1, "unmatched-end-recovery");
        Check(CountTag(builder, ManagedHtmlTag.P) == 2 && CountTag(builder, ManagedHtmlTag.Li) == 2,
              "common-recovery-nodes");
        ManagedHtmlTreeBuilder misnested = Parse("<b><i>x</b>y</i>");
        Check(misnested.State == ManagedHtmlTreeBuilderState.Completed &&
              misnested.UnmatchedEndTagsIgnored == 1 &&
              misnested.Validate(out _), "simplified-formatting-recovery");
    }

    private static void AttributesAndNames()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<a id=x class='hero' style=bold href='/x' title='tip' name=n type=t value=v width=10 height=20 disabled checked selected colspan=2 rowspan=3 data-z=last>link</a>");
        ManagedHtmlNodeHandle anchor = FindTag(builder, ManagedHtmlTag.A);
        Check(builder.Document.GetAttributeCount(anchor) == 16, "attribute-count");
        Check(builder.Document.TryFindAttribute(anchor, ManagedHtmlAttributeName.Href, out ManagedHtmlAttributeView href) &&
              href.HasValue && CopyAttribute(builder.Document, anchor, href.Index) == "/x", "known-attribute-lookup");
        Check(builder.Document.TryFindAttribute(anchor, "data-z"u8, out ManagedHtmlAttributeView unknown) &&
              CopyAttribute(builder.Document, anchor, unknown.Index) == "last", "unknown-attribute-lookup");
        Check(builder.Document.TryFindAttribute(anchor, ManagedHtmlAttributeName.Disabled, out ManagedHtmlAttributeView disabled) &&
              !disabled.HasValue, "boolean-attribute");
        Span<byte> name = stackalloc byte[32];
        Check(builder.Document.TryCopyAttributeName(anchor, 0, name, out int nameLength) &&
              name[..nameLength].SequenceEqual("id"u8), "attribute-order");
    }

    private static void TextAndFragmentation()
    {
        const string html = "<div>Hello </div><p>world &amp; 🙂</p>";
        ManagedHtmlTreeBuilder baseline = Parse(html, 4096);
        ManagedHtmlTreeBuilder oneScalar = Parse(html, 1);
        Check(baseline.Document.CanonicalHashAvailable && oneScalar.Document.CanonicalHashAvailable,
              "hash-available");
        Span<byte> firstHash = stackalloc byte[32];
        Span<byte> secondHash = stackalloc byte[32];
        Check(baseline.TryCopyCanonicalHash(firstHash) && oneScalar.TryCopyCanonicalHash(secondHash) &&
              firstHash.SequenceEqual(secondHash), "fragmentation-hash");
        Check(CountTag(baseline, ManagedHtmlTag.Textarea) == 0 && baseline.TextScalarsUsed == oneScalar.TextScalarsUsed,
              "fragmentation-text-count");
        ManagedHtmlTreeBuilder adjacent = Parse("<div>hello world</div>");
        ManagedHtmlNodeHandle div = FindTag(adjacent, ManagedHtmlTag.Div);
        ManagedHtmlNodeHandle text = adjacent.Document.GetFirstChild(div);
        Check(adjacent.Document.GetNodeKind(text) == ManagedHtmlNodeKind.Text &&
              adjacent.Document.GetTextLength(text) == 11, "text-coalescing");
        Check(CopyText(adjacent.Document, text) == "hello world", "text-exact");
        ManagedHtmlTreeBuilder unicode = Parse("<p>Ж中🙂</p>");
        Check(CopyText(unicode.Document, unicode.Document.GetFirstChild(FindTag(unicode, ManagedHtmlTag.P))) == "Ж中🙂",
              "unicode-scalars");
    }

    private static void VoidAndRawText()
    {
        ManagedHtmlTreeBuilder builder = Parse("<div><br><img src=x><meta charset=utf-8><link href=x><input disabled><hr><span>x</span></div>");
        ManagedHtmlNodeHandle div = FindTag(builder, ManagedHtmlTag.Div);
        Check(builder.Document.GetParent(FindTag(builder, ManagedHtmlTag.Span)) == div,
              "voids-not-pushed");
        ManagedHtmlTreeBuilder raw = Parse("<title>A &amp; B</title><style>a < b{}</style><script>if (a < b) x();</script><textarea>A &lt; B</textarea>");
        Check(CopyText(raw.Document, builderText(raw, ManagedHtmlTag.Title)) == "A & B" &&
              CopyText(raw.Document, builderText(raw, ManagedHtmlTag.Style)) == "a < b{}" &&
              CopyText(raw.Document, builderText(raw, ManagedHtmlTag.Script)).Contains("a < b") &&
              CopyText(raw.Document, builderText(raw, ManagedHtmlTag.Textarea)) == "A < B",
              "raw-text-storage");
    }

    private static void TablesAndComments()
    {
        ManagedHtmlTreeBuilder table = Parse("<!--one--><table><tr><td>a</td><td>b</td></tr><tr><th>c</th></tr></table>");
        Check(table.CommentsDiscarded == 1 && CountKind(table, ManagedHtmlNodeKind.Comment) == 0,
              "comments-discarded");
        Check(CountTag(table, ManagedHtmlTag.Table) == 1 && CountTag(table, ManagedHtmlTag.Tbody) == 1 &&
              CountTag(table, ManagedHtmlTag.Tr) == 2 && CountTag(table, ManagedHtmlTag.Td) == 2 &&
              CountTag(table, ManagedHtmlTag.Th) == 1, "table-subset");
        Check(table.Validate(out _), "table-valid");
    }

    private static void LimitsAndFailures()
    {
        ManagedHtmlTreeBuilder nodes = Parse("<div><span>x</span></div>", 4,
            new ManagedHtmlDocumentArenaOptions(4, 128, 16, 128, 16));
        Check(nodes.State == ManagedHtmlTreeBuilderState.Failed &&
              nodes.FailureReason == ManagedHtmlTreeBuilderFailureReason.NodeCapacityExceeded,
              "node-capacity-failure");
        Check(nodes.Validate(out _), "partial-node-valid-after-failure");
        ManagedHtmlTreeBuilder text = Parse("<p>12345</p>", 4096,
            new ManagedHtmlDocumentArenaOptions(64, 4, 16, 128, 16));
        Check(text.FailureReason == ManagedHtmlTreeBuilderFailureReason.TextCapacityExceeded,
              "text-capacity-failure");
        ManagedHtmlTreeBuilder attributes = Parse("<div a=1 b=2 c=3></div>", 4096,
            new ManagedHtmlDocumentArenaOptions(64, 128, 2, 128, 16));
        Check(attributes.FailureReason == ManagedHtmlTreeBuilderFailureReason.AttributeCapacityExceeded,
              "attribute-capacity-failure");
        ManagedHtmlTreeBuilder values = Parse("<div a=12345></div>", 4096,
            new ManagedHtmlDocumentArenaOptions(64, 128, 16, 4, 16));
        Check(values.FailureReason == ManagedHtmlTreeBuilderFailureReason.AttributeValueCapacityExceeded,
              "attribute-value-capacity-failure");
        ManagedHtmlTreeBuilder depth = Parse("<a><a><a>x</a></a></a>", 4096,
            new ManagedHtmlDocumentArenaOptions(64, 128, 16, 128, 2));
        Check(depth.FailureReason == ManagedHtmlTreeBuilderFailureReason.TreeDepthExceeded,
              "depth-capacity-failure");
    }

    private static void CancellationAndReset()
    {
        ManagedHtmlTreeBuilder builder = new();
        builder.Cancel();
        Check(builder.State == ManagedHtmlTreeBuilderState.Cancelled && builder.NodeCount == 0,
              "cancel-before-root");
        builder.Reset();
        ManagedHtmlNodeHandle old = ParseWithBuilder(builder, "<p>old</p>").Html;
        Check(builder.Document.IsValid(old), "handle-before-reset");
        builder.Reset();
        Check(!builder.Document.IsValid(old) && builder.NodeCount == 0 && builder.TextScalarsUsed == 0 &&
              builder.AttributeCount == 0 && builder.CurrentStackDepth == 0,
              "reset-clears-arenas-and-handles");
        ParseWithBuilder(builder, "<div>new</div>");
        Check(builder.State == ManagedHtmlTreeBuilderState.Completed &&
              CountTag(builder, ManagedHtmlTag.Div) == 1, "second-document");
    }

    private static void PauseAndValidatorCoverage()
    {
        ManagedHtmlTreeBuilder pausedBuilder = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> prefix = ToScalars("<div>");
        Check(tokenizer.AppendInput(CollectionsMarshal.AsSpan(prefix)) &&
              tokenizer.Pump(pausedBuilder) != ManagedHtmlTokenizerProcessResult.Failed,
              "pause-prefix");
        pausedBuilder.RequestPause();
        List<uint> suffix = ToScalars("fragment</div>");
        Check(tokenizer.AppendInput(CollectionsMarshal.AsSpan(suffix)) &&
              tokenizer.Pump(pausedBuilder) == ManagedHtmlTokenizerProcessResult.Paused,
              "builder-pause");
        Check(pausedBuilder.NodeCount == 5, "pause-preserves-document");
        pausedBuilder.Resume();
        tokenizer.Resume();
        Check(tokenizer.Pump(pausedBuilder, true) == ManagedHtmlTokenizerProcessResult.Complete &&
              pausedBuilder.Complete(), "builder-resume");

        ManagedHtmlTreeBuilder parentCorruption = Parse("<div><span>x</span></div>");
        ManagedHtmlNodeHandle span = FindTag(parentCorruption, ManagedHtmlTag.Span);
        ManagedHtmlNodeRecord spanRecord = parentCorruption.Document.Nodes[span.Index];
        spanRecord.Parent = parentCorruption.Document.RootIndex;
        parentCorruption.Document.Nodes[span.Index] = spanRecord;
        Check(!parentCorruption.Document.Validate(out ManagedHtmlDocumentValidationFailureReason parentReason) &&
              parentReason == ManagedHtmlDocumentValidationFailureReason.ParentLinkMismatch,
              "validator-parent-link");

        ManagedHtmlTreeBuilder cycle = Parse("<div><span>x</span><b>y</b></div>");
        ManagedHtmlNodeHandle first = cycle.Document.GetFirstChild(FindTag(cycle, ManagedHtmlTag.Div));
        ManagedHtmlNodeRecord firstRecord = cycle.Document.Nodes[first.Index];
        firstRecord.NextSibling = first.Index;
        cycle.Document.Nodes[first.Index] = firstRecord;
        Check(!cycle.Document.Validate(out ManagedHtmlDocumentValidationFailureReason cycleReason) &&
              cycleReason == ManagedHtmlDocumentValidationFailureReason.SiblingCycle,
              "validator-sibling-cycle");

        ManagedHtmlTreeBuilder textRange = Parse("<p>x</p>");
        ManagedHtmlNodeHandle text = builderText(textRange, ManagedHtmlTag.P);
        ManagedHtmlNodeRecord textRecord = textRange.Document.Nodes[text.Index];
        textRecord.TextOffset = textRange.Document.TextScalarCapacity;
        textRange.Document.Nodes[text.Index] = textRecord;
        Check(!textRange.Document.Validate(out ManagedHtmlDocumentValidationFailureReason textReason) &&
              textReason == ManagedHtmlDocumentValidationFailureReason.TextRangeInvalid,
              "validator-text-range");

        ManagedHtmlTreeBuilder attributeRange = Parse("<div data-x=y></div>");
        ManagedHtmlNodeHandle div = FindTag(attributeRange, ManagedHtmlTag.Div);
        ManagedHtmlNodeRecord divRecord = attributeRange.Document.Nodes[div.Index];
        ManagedHtmlAttributeRecord attribute = attributeRange.Document.Attributes[divRecord.FirstAttribute];
        attribute.ValueOffset = attributeRange.Document.AttributeValueScalarCapacity;
        attributeRange.Document.Attributes[divRecord.FirstAttribute] = attribute;
        Check(!attributeRange.Document.Validate(out ManagedHtmlDocumentValidationFailureReason attributeReason) &&
              attributeReason == ManagedHtmlDocumentValidationFailureReason.AttributeValueRangeInvalid,
              "validator-attribute-range");

        ManagedHtmlTreeBuilder tagNames = Parse("<custom-element>x</custom-element>", 4096,
            new ManagedHtmlDocumentArenaOptions(64, 128, 16, 128, 16, 4, 128));
        Check(tagNames.FailureReason == ManagedHtmlTreeBuilderFailureReason.TagNameCapacityExceeded,
              "tag-name-capacity-failure");
        ManagedHtmlTreeBuilder attributeNames = Parse("<div data-long=1></div>", 4096,
            new ManagedHtmlDocumentArenaOptions(64, 128, 16, 128, 16, 128, 4));
        Check(attributeNames.FailureReason == ManagedHtmlTreeBuilderFailureReason.AttributeNameCapacityExceeded,
              "attribute-name-capacity-failure");
    }

    private static void CanonicalHash()
    {
        string fixture = "<!doctype html><html><head><title>x</title></head><body><div id=x>A</div><p>B</p></body></html>";
        ManagedHtmlTreeBuilder first = Parse(fixture, 2);
        ManagedHtmlTreeBuilder second = Parse(fixture, 8192);
        Span<byte> a = stackalloc byte[32];
        Span<byte> b = stackalloc byte[32];
        Check(first.Document.TryCopyCanonicalHash(a) && second.Document.TryCopyCanonicalHash(b) &&
              a.SequenceEqual(b), "canonical-hash-segmentation-independent");
        Check(first.Document.Validate(out ManagedHtmlDocumentValidationFailureReason reason) &&
              reason == ManagedHtmlDocumentValidationFailureReason.None, "document-validator");
    }

    private static void FixtureSummary()
    {
        StringBuilder source = new("<!doctype html><html><head><title>GuideX Phase 43</title><meta charset=utf-8><link rel=stylesheet href='/p43.css'><style>body{color:blue}</style><script>if (a < b) x();</script></head><body><div id=app class=page><header><h1>Bounded tree 🙂</h1></header><p class=intro>Hello &amp; welcome</p><a href='/next' title=next>Next</a><img src='/logo' alt=logo><ul><li>one<li>two</ul><table><tr><td>A</td><td>B</td></tr></table><form id=form><input name=email required><button type=submit>Send</button></form>");
        for (int index = 0; index != 24; ++index)
            source.Append("<div class=item data-id=").Append(index).Append("><span>Item ").Append(index).Append("</span><p>Repeated &amp; text 🙂</p></div>");
        source.Append("<!-- discarded --><textarea>A &lt; B</textarea></div></body></html>");
        ManagedHtmlTreeBuilder builder = Parse(source.ToString(), 17);
        int nodeRecordBytes = Marshal.SizeOf<ManagedHtmlNodeRecord>();
        int attributeRecordBytes = Marshal.SizeOf<ManagedHtmlAttributeRecord>();
        int persistentArenaBytes = nodeRecordBytes * builder.NodeCapacity +
            builder.TextScalarCapacity * sizeof(uint) + builder.TagNameCapacity +
            attributeRecordBytes * builder.AttributeCapacity +
            builder.AttributeNameCapacity + builder.AttributeValueScalarCapacity * sizeof(uint) +
            builder.TreeDepthCapacity * sizeof(int);
        Console.WriteLine($"PHASE43_FIXTURE_BYTES={Encoding.UTF8.GetByteCount(source.ToString())} TOKENS={builder.TokensConsumed} TEXT_TOKENS={s_lastTokenizer!.TextTokenCount} START_TAGS={s_lastTokenizer.StartTagCount} END_TAGS={s_lastTokenizer.EndTagCount} COMMENTS={s_lastTokenizer.CommentCount} DOCTYPES={s_lastTokenizer.DoctypeCount} TOKEN_ATTRIBUTES={s_lastTokenizer.AttributesEmitted} NODES={builder.NodeCount} ELEMENTS={builder.ElementCount} TEXT_NODES={builder.TextNodeCount} ATTRIBUTES={builder.AttributeCount} TEXT_SCALARS={builder.TextScalarsUsed} ATTR_VALUE_SCALARS={builder.AttributeValueScalarsUsed} PEAK_DEPTH={builder.PeakStackDepth} IMPLIED={builder.ImpliedElementsInserted} UNMATCHED={builder.UnmatchedEndTagsIgnored} IMPLICIT={builder.ImplicitClosesPerformed} NODE_RECORD_BYTES={nodeRecordBytes} ATTRIBUTE_RECORD_BYTES={attributeRecordBytes} PERSISTENT_ARENA_BYTES={persistentArenaBytes} HASH={Digest(builder)}");
    }

    private static string Digest(ManagedHtmlTreeBuilder builder)
    {
        Span<byte> hash = stackalloc byte[32];
        Check(builder.TryCopyCanonicalHash(hash), "fixture-hash");
        StringBuilder result = new();
        for (int index = 0; index != hash.Length; ++index) result.Append(hash[index].ToString("X2"));
        return result.ToString();
    }

    private static ManagedHtmlTreeBuilder Parse(string html, int chunk = 4096,
                                                ManagedHtmlDocumentArenaOptions? options = null)
    {
        ManagedHtmlTreeBuilder builder = options.HasValue
            ? new ManagedHtmlTreeBuilder(options.Value) : new ManagedHtmlTreeBuilder();
        return ParseWithBuilder(builder, html, chunk);
    }

    private static ManagedHtmlTreeBuilder ParseWithBuilder(ManagedHtmlTreeBuilder builder,
                                                            string html, int chunk = 4096)
    {
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = ToScalars(html);
        int offset = 0;
        while (offset != scalars.Count)
        {
            int length = Math.Min(Math.Min(chunk, ManagedHtmlTokenizerLimits.InputWindowCapacity),
                                  scalars.Count - offset);
            uint[] input = new uint[length];
            for (int index = 0; index != length; ++index) input[index] = scalars[offset + index];
            Check(tokenizer.AppendInput(input), "tokenizer-input-window");
            ManagedHtmlTokenizerProcessResult result = tokenizer.Pump(builder);
            if (result == ManagedHtmlTokenizerProcessResult.Failed &&
                builder.State == ManagedHtmlTreeBuilderState.Failed)
            {
                ++s_cases;
                return builder;
            }
            Check(result != ManagedHtmlTokenizerProcessResult.Failed &&
                  result != ManagedHtmlTokenizerProcessResult.Cancelled,
                  $"tokenizer-pump:{result}:tokenizer={tokenizer.FailureReason}:builder={builder.FailureReason}:tokens={builder.TokensConsumed}:received={builder.TokensReceived}:offset={offset}");
            offset += length;
        }
        ManagedHtmlTokenizerProcessResult final = tokenizer.Pump(builder, true);
        Check(final == ManagedHtmlTokenizerProcessResult.Complete, "tokenizer-complete");
        Check(builder.Complete(), "tree-complete");
        s_lastTokenizer = tokenizer;
        ++s_cases;
        return builder;
    }

    private static List<uint> ToScalars(string value)
    {
        List<uint> result = new();
        for (int index = 0; index != value.Length; ++index)
        {
            char current = value[index];
            if (char.IsHighSurrogate(current) && index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
                result.Add((uint)char.ConvertToUtf32(current, value[++index]));
            else result.Add(current);
        }
        return result;
    }

    private static string Tag(ManagedHtmlTreeBuilder builder, ManagedHtmlNodeHandle node)
    {
        Span<byte> buffer = stackalloc byte[64];
        Check(builder.Document.TryCopyTagName(node, buffer, out int length), "tag-copy");
        return System.Text.Encoding.ASCII.GetString(buffer[..length]);
    }

    private static string CopyText(ManagedHtmlDocument document, ManagedHtmlNodeHandle node)
    {
        Span<uint> buffer = stackalloc uint[256];
        Check(document.TryCopyText(node, buffer, out int length), "text-copy");
        return ScalarsToString(buffer[..length]);
    }

    private static string CopyAttribute(ManagedHtmlDocument document, ManagedHtmlNodeHandle element, int index)
    {
        Span<uint> buffer = stackalloc uint[256];
        Check(document.TryCopyAttributeValue(element, index, buffer, out int length, out _), "attribute-copy");
        return ScalarsToString(buffer[..length]);
    }

    private static string ScalarsToString(ReadOnlySpan<uint> scalars)
    {
        System.Text.StringBuilder result = new();
        for (int index = 0; index != scalars.Length; ++index)
            result.Append(char.ConvertFromUtf32((int)scalars[index]));
        return result.ToString();
    }

    private static ManagedHtmlNodeHandle FindTag(ManagedHtmlTreeBuilder builder, ManagedHtmlTag tag)
    {
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle known = FindByIndex(builder, index);
            if (builder.Document.GetElementTag(known) == tag) return known;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static ManagedHtmlNodeHandle FindByIndex(ManagedHtmlTreeBuilder builder, int index)
    {
        ManagedHtmlNodeHandle root = builder.Document.DocumentNode;
        for (ManagedHtmlNodeHandle current = root; current != ManagedHtmlNodeHandle.Invalid; current = builder.Document.GetNextSibling(current))
        {
            if (current.Index == index) return current;
        }
        return FindRecursive(builder, root, index);
    }

    private static ManagedHtmlNodeHandle FindRecursive(ManagedHtmlTreeBuilder builder,
                                                        ManagedHtmlNodeHandle parent, int index)
    {
        for (ManagedHtmlNodeHandle child = builder.Document.GetFirstChild(parent);
             child != ManagedHtmlNodeHandle.Invalid;
             child = builder.Document.GetNextSibling(child))
        {
            if (child.Index == index) return child;
            ManagedHtmlNodeHandle found = FindRecursive(builder, child, index);
            if (found != ManagedHtmlNodeHandle.Invalid) return found;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static ManagedHtmlNodeHandle builderText(ManagedHtmlTreeBuilder builder, ManagedHtmlTag parentTag)
    {
        ManagedHtmlNodeHandle element = FindTag(builder, parentTag);
        return builder.Document.GetFirstChild(element);
    }

    private static int CountTag(ManagedHtmlTreeBuilder builder, ManagedHtmlTag tag)
    {
        int count = 0;
        for (int index = 0; index != builder.Document.NodeCount; ++index)
            if (builder.Document.GetElementTag(FindByIndex(builder, index)) == tag) ++count;
        return count;
    }

    private static int CountKind(ManagedHtmlTreeBuilder builder, ManagedHtmlNodeKind kind)
    {
        int count = 0;
        for (int index = 0; index != builder.Document.NodeCount; ++index)
            if (builder.Document.GetNodeKind(FindByIndex(builder, index)) == kind) ++count;
        return count;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}

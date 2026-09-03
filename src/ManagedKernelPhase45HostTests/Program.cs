using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            ViewportAndBlocks();
            BoxModelAndSizing();
            VisibilityAndUANodes();
            TextAndWhitespace();
            InlineAndReplaced();
            PositioningAndOverflow();
            CapacitiesAndFailures();
            RelayoutAndValidation();
            Console.WriteLine($"MANAGED_KERNEL_PHASE45_SIZES rect={Unsafe.SizeOf<ManagedLayoutRect>()} edges={Unsafe.SizeOf<ManagedLayoutEdges>()} box={Unsafe.SizeOf<ManagedLayoutBox>()} line={Unsafe.SizeOf<ManagedLayoutLine>()} fragment={Unsafe.SizeOf<ManagedLayoutTextFragment>()} viewport={Unsafe.SizeOf<ManagedLayoutViewport>()}");
            Console.WriteLine($"MANAGED_KERNEL_PHASE45_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE45_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void ViewportAndBlocks()
    {
        LayoutResult empty = Layout("", "");
        Check(empty.Engine.IsLaidOut, "empty-layout");
        Check(Box(empty.Engine, 0).Kind == ManagedLayoutBoxKind.Root &&
              Box(empty.Engine, 0).BorderBox == new ManagedLayoutRect(0, 0, 800, 600), "root-800x600");
        LayoutResult alternate = Layout("<div></div>", "", 320, 240);
        Check(Box(alternate.Engine, 0).BorderBox == new ManagedLayoutRect(0, 0, 320, 240), "alternate-viewport");
        ManagedLayoutEngine negative = new(alternate.Document.Document, alternate.Css);
        Check(!negative.TryLayout(-1, 240) && negative.FailureReason == ManagedLayoutFailureReason.InvalidViewport, "negative-viewport");
        LayoutResult blocks = Layout("<div id=a></div><div id=b></div><div id=c></div>",
            "#a{height:10px;margin-bottom:3px}#b{height:20px;margin-top:4px}#c{height:30px}");
        ManagedLayoutBox a = NodeBox(blocks, "a");
        ManagedLayoutBox b = NodeBox(blocks, "b");
        ManagedLayoutBox c = NodeBox(blocks, "c");
        Check(a.BorderBox.Y == 0 && b.BorderBox.Y == 17 && c.BorderBox.Y == 37, "block-stack-additive-margins");
        Check(b.BorderBox.X == 0 && b.BorderBox.Width == 800, "auto-block-width");
        Check(NodeBoxForTag(blocks, ManagedHtmlTag.Body).ContentRect.Height >= 67, "parent-auto-height");
        Check(a.BorderBox.Bottom <= b.BorderBox.Y && b.BorderBox.Bottom <= c.BorderBox.Y, "no-sibling-overlap");
        Check(Layout("<div id=x><div id=y></div></div>", "#y{width:50%;height:12px}").Engine.LayoutBoxCount >= 4, "nested-block-boxes");
        Check(!Layout("<div></div>", "").Engine.TryGetBox(99, out _), "box-inspection-range");
    }

    private static void BoxModelAndSizing()
    {
        LayoutResult result = Layout("<div id=x><div id=y></div></div>",
            "#x{width:100px;height:50px;margin:10px 20px 30px 40px;padding:1px 2px 3px 4px;border-width:5px}#y{width:50%;height:10px}");
        ManagedLayoutBox x = NodeBox(result, "x");
        ManagedLayoutBox y = NodeBox(result, "y");
        Check(x.Margin == new ManagedLayoutEdges(10, 20, 30, 40), "four-margins");
        Check(x.Padding == new ManagedLayoutEdges(1, 2, 3, 4), "four-padding");
        Check(x.Border == new ManagedLayoutEdges(5, 5, 5, 5), "four-borders");
        Check(x.ContentRect == new ManagedLayoutRect(49, 16, 100, 50), "content-rectangle");
        Check(x.BorderBox == new ManagedLayoutRect(40, 10, 116, 64), "border-box-model");
        Check(y.ContentRect.X == 49 && y.ContentRect.Width == 50 && y.ContentRect.Y == 16, "child-content-area");
        LayoutResult limits = Layout("<div id=x></div>", "#x{width:10px;min-width:30px;max-width:40px;height:10px;min-height:20px;max-height:25px}");
        ManagedLayoutBox limited = NodeBox(limits, "x");
        Check(limited.ContentRect.Width == 30 && limited.ContentRect.Height == 20, "min-dimensions");
        LayoutResult maxed = Layout("<div id=x></div>", "#x{width:100px;max-width:40px;height:100px;max-height:25px}");
        Check(NodeBox(maxed, "x").ContentRect == new ManagedLayoutRect(0, 0, 40, 25), "max-dimensions");
        LayoutResult percent = Layout("<div id=x></div>", "#x{width:50%;padding-left:10%;padding-right:10%}");
        Check(NodeBox(percent, "x").ContentRect.Width == 400 && NodeBox(percent, "x").Padding.Left == 80, "percentage-width-padding");
        LayoutResult shorthand = Layout("<div id=x></div>", "#x{margin:2px 4px;padding:3px 5px;border-width:0}");
        Check(NodeBox(shorthand, "x").Margin == new ManagedLayoutEdges(2, 4, 2, 4) &&
              NodeBox(shorthand, "x").Padding == new ManagedLayoutEdges(3, 5, 3, 5), "shorthand-box-model");
    }

    private static void VisibilityAndUANodes()
    {
        LayoutResult hidden = Layout("<head><style>body{color:red}</style><script>not visible</script></head><body><div id=gone><span>gone</span></div><div id=shown>shown</div></body>",
            "#gone{display:none}");
        Check(!hidden.Engine.TryGetBoxForNode(FindById(hidden.Document.Document, "gone"), out _), "display-none-no-box");
        Check(!hidden.Engine.TryGetBoxForNode(FindTag(hidden.Document.Document, ManagedHtmlTag.Span), out _), "display-none-descendant-no-box");
        Check(!hidden.Engine.TryGetBoxForNode(FindTag(hidden.Document.Document, ManagedHtmlTag.Script), out _), "script-no-box");
        Check(!hidden.Engine.TryGetBoxForNode(FindTag(hidden.Document.Document, ManagedHtmlTag.Style), out _), "style-no-box");
        Check(hidden.Engine.DisplayNoneSkips >= 2, "display-none-telemetry");
        Check(NodeBox(hidden, "shown").BorderBox.Y == 0, "hidden-absent-from-flow");
        Check(hidden.Engine.Validate(out ManagedLayoutValidationFailureReason reason) &&
              reason == ManagedLayoutValidationFailureReason.None, "hidden-validator");
    }

    private static void TextAndWhitespace()
    {
        LayoutResult text = Layout("<p id=p>Hello world</p>", "#p{font-size:10px}");
        ManagedLayoutBox paragraph = NodeBox(text, "p");
        Check(text.Engine.LineCount == 1 && text.Engine.TextFragmentCount == 3, "text-one-line-runs");
        Check(paragraph.ContentRect.Height == 12, "synthetic-line-height");
        Check(text.Engine.TextScalarsMeasured >= 11, "text-measure-telemetry");
        ManagedLayoutTextFragment first = Fragment(text.Engine, 0);
        ManagedLayoutTextFragment second = Fragment(text.Engine, 2);
        Check(first.SourceLength == 5 && second.SourceLength == 5 && first.Rectangle.X < second.Rectangle.X, "text-source-ranges-order");
        LayoutResult wrap = Layout("<p id=p>one two three four</p>", "#p{font-size:10px;width:30px}");
        Check(wrap.Engine.LineCount >= 4 && wrap.Engine.SoftWrapCount >= 3, "word-wrapping");
        LayoutResult nowrap = Layout("<p id=p>one two three four</p>", "#p{font-size:10px;width:30px;white-space:nowrap}");
        Check(nowrap.Engine.LineCount == 1 && nowrap.Engine.DocumentContentWidth > 30, "nowrap-no-wrap");
        LayoutResult pre = Layout("<pre id=p>a  b\n c</pre>", "#p{font-size:10px;white-space:pre}");
        Check(pre.Engine.LineCount == 2 && pre.Engine.TextFragmentCount == 2, "pre-preserves-newline");
        LayoutResult preWrap = Layout("<pre id=p>one two three</pre>", "#p{font-size:10px;white-space:pre-wrap;width:30px}");
        Check(preWrap.Engine.LineCount > 1, "pre-wrap");
        LayoutResult preCrLf = Layout("<pre id=p>a\r\nb</pre>", "#p{white-space:pre}");
        Check(preCrLf.Engine.LineCount == 2, "crlf-single-break");
        LayoutResult unicode = Layout("<p id=p>A😀é</p>", "#p{font-size:10px}");
        Check(unicode.Engine.TextScalarsMeasured == 3, "unicode-scalars");
        Check(Layout("<p id=p></p>", "").Engine.LineCount == 0, "empty-text-no-line");
    }

    private static void InlineAndReplaced()
    {
        LayoutResult inline = Layout("<p id=p>Hello <span>world</span> <strong>again</strong><br>next</p>",
            "#p{font-size:10px;width:120px}strong{font-weight:bold}");
        Check(inline.Engine.LineCount == 2 && inline.Engine.ForcedBreakCount == 1, "inline-and-br");
        Check(inline.Engine.TextFragmentCount == 6, "inline-source-order-fragments");
        for (int index = 1; index < inline.Engine.TextFragmentCount; ++index)
            Check(Fragment(inline.Engine, index - 1).SourceNodeIndex <= Fragment(inline.Engine, index).SourceNodeIndex, "inline-document-order");
        Check(HasFragmentWeight(inline.Engine, 700), "inline-weight-style");
        LayoutResult image = Layout("<p id=p>before<img id=i>after</p><img id=b>",
            "#i{width:20px;height:10px}#b{display:block;width:30px;height:12px}");
        ManagedLayoutBox i = NodeBox(image, "i");
        ManagedLayoutBox b = NodeBox(image, "b");
        Check(i.Kind == ManagedLayoutBoxKind.Replaced && i.BorderBox.Width == 20 && i.BorderBox.Height == 10, "img-fixed-placeholder");
        Check(b.Kind == ManagedLayoutBoxKind.Replaced && b.BorderBox.Width == 30 && b.BorderBox.Height == 12, "block-image-placeholder");
        LayoutResult fallback = Layout("<img id=i>", "");
        Check(NodeBox(fallback, "i").BorderBox == new ManagedLayoutRect(0, 0, 32, 24), "img-intrinsic-fallback");
    }

    private static void PositioningAndOverflow()
    {
        LayoutResult relative = Layout("<div id=a></div><div id=b></div>",
            "#a{height:10px;position:relative;left:5px;top:7px}#b{height:10px}");
        Check(NodeBox(relative, "a").BorderBox.X == 5 && NodeBox(relative, "a").BorderBox.Y == 7 &&
              NodeBox(relative, "b").BorderBox.Y == 10, "relative-reserves-flow-space");
        LayoutResult absolute = Layout("<div id=p><div id=a></div><div id=b></div></div>",
            "#p{position:relative;height:50px}#a{position:absolute;left:10px;top:4px;width:20px;height:8px}#b{height:10px}");
        Check(NodeBox(absolute, "a").BorderBox == new ManagedLayoutRect(10, 4, 20, 8) &&
              NodeBox(absolute, "b").BorderBox.Y == 0, "absolute-positioning");
        LayoutResult fixedResult = Layout("<div id=f></div>", "#f{position:fixed;right:10px;bottom:20px;width:30px;height:40px}", 200, 100);
        Check(NodeBox(fixedResult, "f").BorderBox == new ManagedLayoutRect(160, 40, 30, 40), "fixed-viewport-positioning");
        LayoutResult overflow = Layout("<div id=x><p>long content here</p></div>", "#x{width:20px;height:10px;overflow:hidden}p{white-space:nowrap}");
        ManagedLayoutBox x = NodeBox(overflow, "x");
        Check((x.OverflowFlags & ManagedLayoutOverflowFlags.Horizontal) != 0 &&
              (x.OverflowFlags & ManagedLayoutOverflowFlags.Vertical) != 0 && x.ClipRect == x.PaddingBox, "overflow-metadata");
        Check(overflow.Engine.DocumentContentHeight >= 600 && overflow.Engine.DocumentContentWidth >= 800, "document-content-extents");
    }

    private static void CapacitiesAndFailures()
    {
        LayoutResult capacity = Layout("<div id=a></div><div id=b></div>", "");
        ManagedLayoutEngine low = new(capacity.Document.Document, capacity.Css,
            new ManagedLayoutArenaOptions(2, 8, 8, 128));
        Check(!low.TryLayout(800, 600) && low.FailureReason == ManagedLayoutFailureReason.LayoutBoxCapacityExceeded &&
              low.LayoutBoxCount == 2 && low.TryGetBox(0, out _), "box-capacity-boundary");
        LayoutResult lineCapacity = Layout("<p>one two three four</p>", "p{width:30px}");
        ManagedLayoutEngine lowLines = new(lineCapacity.Document.Document, lineCapacity.Css,
            new ManagedLayoutArenaOptions(64, 1, 64, 128));
        Check(!lowLines.TryLayout(800, 600) && lowLines.FailureReason == ManagedLayoutFailureReason.LineCapacityExceeded, "line-capacity-boundary");
        ManagedLayoutEngine lowFragments = new(lineCapacity.Document.Document, lineCapacity.Css,
            new ManagedLayoutArenaOptions(64, 64, 1, 128));
        Check(!lowFragments.TryLayout(800, 600) && lowFragments.FailureReason == ManagedLayoutFailureReason.TextFragmentCapacityExceeded, "fragment-capacity-boundary");
        LayoutResult invalidStyles = ParseOnly("<div></div>");
        ManagedLayoutEngine notStyled = new(invalidStyles.Document.Document,
            new ManagedCssEngine(invalidStyles.Document.Document));
        Check(!notStyled.TryLayout(800, 600) && notStyled.FailureReason == ManagedLayoutFailureReason.InvalidStyles, "invalid-style-distinct");
        ManagedLayoutEngine cancelled = new(capacity.Document.Document, capacity.Css);
        cancelled.Cancel();
        Check(!cancelled.TryLayout(800, 600) && cancelled.FailureReason == ManagedLayoutFailureReason.Cancelled, "cancellation-distinct");
        LayoutResult overflow = ParseOnly("<div id=x>xx</div>");
        ManagedLayoutEngine overflowingMetrics = new(overflow.Document.Document, overflow.Css,
            ManagedLayoutArenaOptions.Default, new HugeMetrics());
        Check(!overflowingMetrics.TryLayout(800, 600) &&
              overflowingMetrics.FailureReason == ManagedLayoutFailureReason.GeometryOverflow, "geometry-overflow-distinct");
    }

    private static void RelayoutAndValidation()
    {
        LayoutResult result = Layout("<div id=x>hello world</div>", "#x{width:100px}");
        Span<byte> first = stackalloc byte[32];
        Span<byte> second = stackalloc byte[32];
        Check(result.Engine.TryCopyCanonicalLayoutHash(first), "layout-hash-available");
        result.Engine.Reset();
        Check(!result.Engine.IsLaidOut && result.Engine.LayoutBoxCount == 0, "layout-reset-clears");
        Check(result.Engine.TryLayout(800, 600) && result.Engine.TryCopyCanonicalLayoutHash(second) &&
              first.SequenceEqual(second), "relayout-hash-stable");
        Check(result.Engine.Validate(out ManagedLayoutValidationFailureReason reason) &&
              reason == ManagedLayoutValidationFailureReason.None, "layout-validator-pass");
        LayoutResult changed = Layout("<div id=x>hello world</div>", "#x{width:100px}", 400, 300);
        Span<byte> changedHash = stackalloc byte[32];
        Check(changed.Engine.TryCopyCanonicalLayoutHash(changedHash) && !first.SequenceEqual(changedHash), "viewport-changes-hash");
        Check(result.Engine.Telemetry.PeakBoxArena == result.Engine.LayoutBoxCount &&
              result.Engine.Telemetry.PeakLineArena == result.Engine.LineCount, "peak-telemetry");
    }

    private readonly struct LayoutResult
    {
        internal LayoutResult(ManagedHtmlTreeBuilder document, ManagedCssEngine css, ManagedLayoutEngine engine)
        { Document = document; Css = css; Engine = engine; }
        internal ManagedHtmlTreeBuilder Document { get; }
        internal ManagedCssEngine Css { get; }
        internal ManagedLayoutEngine Engine { get; }
    }

    private static LayoutResult Layout(string body, string css, int width = 800, int height = 600)
    {
        LayoutResult result = ParseOnly("<html><head><style>" + css + "</style></head><body>" + body + "</body></html>");
        ManagedLayoutEngine engine = new(result.Document.Document, result.Css);
        Check(engine.TryLayout(width, height), "layout-fixture");
        return new LayoutResult(result.Document, result.Css, engine);
    }

    private static LayoutResult ParseOnly(string html)
    {
        ManagedHtmlTreeBuilder document = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = ToScalars(html);
        for (int offset = 0; offset < scalars.Count;)
        {
            int length = Math.Min(ManagedHtmlTokenizerLimits.InputWindowCapacity, scalars.Count - offset);
            uint[] input = new uint[length];
            for (int i = 0; i < length; ++i) input[i] = scalars[offset + i];
            Check(tokenizer.AppendInput(input), "tokenizer-input");
            Check(tokenizer.Pump(document) != ManagedHtmlTokenizerProcessResult.Failed, "tokenizer-pump");
            offset += length;
        }
        Check(tokenizer.Pump(document, true) == ManagedHtmlTokenizerProcessResult.Complete && document.Complete(), "document-complete");
        ManagedCssEngine css = new(document.Document);
        Check(css.TryStyle(), "style-fixture");
        return new LayoutResult(document, css, null!);
    }

    private static ManagedLayoutBox NodeBox(LayoutResult result, string id)
    {
        Check(result.Engine.TryGetBoxForNode(FindById(result.Document.Document, id), out int index), "node-box-" + id);
        Check(result.Engine.TryGetBox(index, out ManagedLayoutBox box), "box-read-" + id);
        return box;
    }

    private static ManagedLayoutBox NodeBoxForTag(LayoutResult result, ManagedHtmlTag tag)
    {
        ManagedHtmlDocument document = result.Document.Document;
        for (int i = 0; i < document.NodeCount; ++i)
        {
            ManagedHtmlNodeHandle node = new ManagedHtmlNodeHandle(i, document.DocumentNode.Generation);
            if (document.GetElementTag(node) == tag)
            {
                Check(result.Engine.TryGetBoxForNode(node, out int index), "tag-box");
                Check(result.Engine.TryGetBox(index, out ManagedLayoutBox box), "tag-box-read");
                return box;
            }
        }
        throw new InvalidOperationException("tag-not-found");
    }

    private static ManagedLayoutBox Box(ManagedLayoutEngine engine, int index)
    {
        Check(engine.TryGetBox(index, out ManagedLayoutBox box), "box-index");
        return box;
    }

    private static ManagedLayoutTextFragment Fragment(ManagedLayoutEngine engine, int index)
    {
        Check(engine.TryGetTextFragment(index, out ManagedLayoutTextFragment fragment), "fragment-index");
        return fragment;
    }

    private static bool HasFragmentWeight(ManagedLayoutEngine engine, int weight)
    {
        for (int i = 0; i < engine.TextFragmentCount; ++i)
            if (Fragment(engine, i).Style.FontWeight == weight) return true;
        return false;
    }

    private static ManagedHtmlNodeHandle FindTag(ManagedHtmlDocument document, ManagedHtmlTag tag)
    {
        for (int i = 0; i < document.NodeCount; ++i)
        {
            ManagedHtmlNodeHandle node = new ManagedHtmlNodeHandle(i, document.DocumentNode.Generation);
            if (document.GetElementTag(node) == tag) return node;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static ManagedHtmlNodeHandle FindById(ManagedHtmlDocument document, string expected)
    {
        for (int i = 0; i < document.NodeCount; ++i)
        {
            ManagedHtmlNodeHandle node = new ManagedHtmlNodeHandle(i, document.DocumentNode.Generation);
            if (document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !document.TryFindAttribute(node, ManagedHtmlAttributeName.Id, out ManagedHtmlAttributeView attr)) continue;
            uint[] value = new uint[attr.ValueLength];
            document.TryCopyAttributeValue(node, attr.Index, value, out int length, out _);
            if (ScalarsToString(value.AsSpan(0, length)) == expected) return node;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static List<uint> ToScalars(string value)
    {
        List<uint> result = new(value.Length);
        for (int i = 0; i < value.Length; ++i)
        {
            char current = value[i];
            result.Add(char.IsHighSurrogate(current) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])
                ? (uint)char.ConvertToUtf32(current, value[++i]) : current);
        }
        return result;
    }

    private static string ScalarsToString(ReadOnlySpan<uint> scalars)
    {
        StringBuilder result = new();
        for (int i = 0; i < scalars.Length; ++i) result.Append(char.ConvertFromUtf32((int)scalars[i]));
        return result.ToString();
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class HugeMetrics : IManagedLayoutTextMetrics
    {
        public bool TryMeasureScalar(uint scalar, in ManagedLayoutTextStyle style, out int advance)
        {
            advance = scalar == 0x20 ? 1 : ManagedLayoutLimits.MaximumCoordinate;
            return true;
        }

        public int GetLineHeight(in ManagedLayoutTextStyle style) => 1;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            BasicCascadeAndInheritance();
            SelectorCoverage();
            ValuesAndRecovery();
            ExternalStylesheetsAndHandles();
            CapacityAndRestyle();
            Console.WriteLine($"MANAGED_KERNEL_PHASE44_SIZES computed={Unsafe.SizeOf<ManagedComputedStyle>()} length={Unsafe.SizeOf<ManagedCssLength>()} stylesheet={Unsafe.SizeOf<ManagedCssStylesheetRecord>()} rule={Unsafe.SizeOf<ManagedCssRuleRecord>()} selector={Unsafe.SizeOf<ManagedCssSelectorRecord>()} step={Unsafe.SizeOf<ManagedCssSelectorStep>()} declaration={Unsafe.SizeOf<ManagedCssDeclarationRecord>()} value={Unsafe.SizeOf<ManagedCssValue>()} handle={Unsafe.SizeOf<ManagedHtmlNodeHandle>()} candidate={Unsafe.SizeOf<ManagedCssCascadeCandidate>()}");
            Console.WriteLine($"MANAGED_KERNEL_PHASE44_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE44_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void BasicCascadeAndInheritance()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<!doctype html><html><head><style>body{color:green;font-size:20px} .card{color:red;background-color:#1234;margin:1px 2px 3px 4px} #hero{color:blue !important} p{display:block}</style></head><body><div id=hero class='card' style='color: white; padding: 2px 4px'>hello<p>child</p></div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "basic-style");
        ManagedHtmlNodeHandle hero = FindById(builder, "hero");
        ManagedComputedStyle heroStyle = Style(css, hero);
        Check(heroStyle.Color == 0xFF0000FF && heroStyle.BackgroundColor == 0x44112233,
              "important-and-color-cascade");
        Check(heroStyle.MarginTop == new ManagedCssLength(100, ManagedCssLengthUnit.Px) &&
              heroStyle.MarginRight == new ManagedCssLength(200, ManagedCssLengthUnit.Px) &&
              heroStyle.MarginBottom == new ManagedCssLength(300, ManagedCssLengthUnit.Px) &&
              heroStyle.MarginLeft == new ManagedCssLength(400, ManagedCssLengthUnit.Px),
              "margin-four-sides");
        Check(heroStyle.PaddingTop == new ManagedCssLength(200, ManagedCssLengthUnit.Px) &&
              heroStyle.PaddingRight == new ManagedCssLength(400, ManagedCssLengthUnit.Px) &&
              heroStyle.PaddingBottom == new ManagedCssLength(200, ManagedCssLengthUnit.Px) &&
              heroStyle.PaddingLeft == new ManagedCssLength(400, ManagedCssLengthUnit.Px),
              "inline-padding-shorthand");
        ManagedHtmlNodeHandle paragraph = FindTag(builder, ManagedHtmlTag.P);
        ManagedComputedStyle paragraphStyle = Style(css, paragraph);
        Check(paragraphStyle.Color == heroStyle.Color && paragraphStyle.FontSize == heroStyle.FontSize,
              "inherited-properties");
        Check(css.GetMatchedRuleCount(hero) >= 2 && css.SelectorsParsed >= 4 &&
              css.DeclarationsParsed >= 10 && css.InlineStylesParsed == 1,
              "cascade-telemetry");
    }

    private static void SelectorCoverage()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<main id=root><section class='panel'><div class='a b' data-kind=x><span class='leaf'>x</span><span>y</span></div><div class='a'>z</div></section></main>",
            "main:root{color:#abc} #root > .panel > div.a.b[data-kind=x] > span.leaf:first-child{color:red} section > div:last-child{color:blue;background-color: white} .panel span{font-weight:bold}");
        ManagedHtmlNodeHandle firstSpan = FindClass(builder, "leaf");
        ManagedHtmlNodeHandle secondDiv = FindSecondTag(builder, ManagedHtmlTag.Div);
        ManagedComputedStyle firstStyle = Style(css, firstSpan);
        Check(firstStyle.Color == 0xFFFF0000 && firstStyle.FontWeight == 700,
              "descendant-and-pseudo");
        ManagedComputedStyle secondStyle = Style(css, secondDiv);
        Check(secondStyle.Color == 0xFF0000FF && secondStyle.BackgroundColor == 0xFFFFFFFF,
              "adjacent-and-last-child");
        Check(css.UnsupportedSelectors == 0, "supported-selector-set");

        (ManagedHtmlTreeBuilder unsupported, ManagedCssEngine unsupportedCss) = Styled(
            "<div><span>x</span></div>", "div span + span{color:red} div ~ span{color:blue}");
        Check(unsupportedCss.TryGetComputedStyle(FindTag(unsupported, ManagedHtmlTag.Span), out _),
              "unsupported-selector-recovery");
        Check(unsupportedCss.UnsupportedSelectors >= 2 && unsupportedCss.FailureReason == ManagedCssParseFailureReason.None,
              "unsupported-selector-telemetry");
    }

    private static void ValuesAndRecovery()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<article><pre id=pre>text</pre><div id=box></div><input disabled></article>",
            "article{white-space: pre-wrap; opacity: .75} #pre{font-size:1.25em; width:50%; border-width:thin; border-style:dashed; border-color:#0f08; position:relative; top:-2px; z-index:4} #box{display:flex; overflow:auto; color: transparent; margin:auto; padding:0}");
        ManagedComputedStyle pre = Style(css, FindById(builder, "pre"));
        Check(pre.FontSize == new ManagedCssLength(125, ManagedCssLengthUnit.Em) &&
              pre.Width == new ManagedCssLength(5000, ManagedCssLengthUnit.Percent) &&
              pre.BorderWidth == new ManagedCssLength(100, ManagedCssLengthUnit.Px) &&
              pre.BorderStyle == ManagedCssBorderStyle.Dashed && pre.BorderColor == 0x8800FF00 &&
              pre.Top == new ManagedCssLength(-200, ManagedCssLengthUnit.Px) && pre.ZIndex == 4,
              "typed-values");
        ManagedComputedStyle box = Style(css, FindById(builder, "box"));
        Check(box.Display == ManagedCssDisplay.Flex && box.Opacity == 10000 && box.MarginTop.IsAuto,
              "keywords-and-decimals");

        (ManagedHtmlTreeBuilder recovery, ManagedCssEngine recoveryCss) = Styled(
            "<div id=x></div>",
            "/* leading */ #x { color: red; broken; background-color: #bad-value; unknown-prop: 1px; color: blue; } @media screen { #x{color:white} } #x{color:green !important}");
        Check(Style(recoveryCss, FindById(recovery, "x")).Color == 0xFF008000 &&
              recoveryCss.MalformedDeclarations >= 1 && recoveryCss.UnknownProperties >= 1,
              "malformed-declaration-recovery");
        Check(recoveryCss.TryStyle() && recoveryCss.TryStyle(), "repeat-style-reset");
    }

    private static void ExternalStylesheetsAndHandles()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<html><head><link rel='alternate stylesheet' href='/no'><link rel='stylesheet preload' href='https://example.test/site.css'></head><body><div id=x></div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "external-discovery-style");
        Span<uint> href = stackalloc uint[128];
        ManagedHtmlNodeHandle link = ManagedHtmlNodeHandle.Invalid;
        int length;
        Check(css.ExternalStylesheetCount == 2 &&
              css.TryGetExternalStylesheet(0, out link, href, out length) &&
              length > 0, "external-stylesheet-discovery");
        Check(builder.Document.GetElementTag(link) == ManagedHtmlTag.Link, "external-link-handle");
        ManagedHtmlNodeHandle old = FindById(builder, "x");
        builder.Reset();
        Check(!builder.Document.IsValid(old), "stale-document-handle");
    }

    private static void CapacityAndRestyle()
    {
        ManagedHtmlTreeBuilder builder = Parse("<div id=x></div>");
        ManagedCssEngine lowRules = new(builder.Document,
            new ManagedCssArenaOptions(1, 1, 8, 8, 8, 32));
        Check(!lowRules.TryParseStylesheet(ToScalars("div{color:red} p{color:blue}").ToArray()) &&
              lowRules.FailureReason == ManagedCssParseFailureReason.RuleCapacityExceeded,
              "rule-capacity-boundary");
        ManagedCssEngine lowSelectors = new(builder.Document,
            new ManagedCssArenaOptions(1, 8, 1, 8, 8, 32));
        Check(!lowSelectors.TryParseStylesheet(ToScalars("div, p{color:red}").ToArray()) &&
              lowSelectors.FailureReason == ManagedCssParseFailureReason.SelectorCapacityExceeded,
              "selector-capacity-boundary");
        ManagedCssEngine lowDeclarations = new(builder.Document,
            new ManagedCssArenaOptions(1, 8, 8, 8, 1, 32));
        Check(!lowDeclarations.TryParseStylesheet(ToScalars("div{color:red;background-color:blue}").ToArray()) &&
              lowDeclarations.FailureReason == ManagedCssParseFailureReason.DeclarationCapacityExceeded,
              "declaration-capacity-boundary");
        (ManagedHtmlTreeBuilder _, ManagedCssEngine css) = Styled("<div id=x></div>", "#x{color:red;width:12.50px}");
        Span<byte> first = stackalloc byte[32];
        Span<byte> second = stackalloc byte[32];
        Check(css.TryCopyCanonicalStyleHash(first) && css.TryStyle() && css.TryCopyCanonicalStyleHash(second) &&
              first.SequenceEqual(second), "canonical-style-hash-stable");
        Check(css.Telemetry.ComputedStyleCapacity == ManagedCssLimits.DefaultComputedStyleCapacity,
              "telemetry-capacity");
    }

    private static (ManagedHtmlTreeBuilder Builder, ManagedCssEngine Engine) Styled(
        string body, string cssText)
    {
        ManagedHtmlTreeBuilder document = Parse("<html><head><style>" + cssText +
                                               "</style></head><body>" + body +
                                               "</body></html>");
        ManagedCssEngine engine = new(document.Document);
        Check(engine.TryStyle(), "style-fixture");
        return (document, engine);
    }

    private static ManagedComputedStyle Style(ManagedCssEngine css, ManagedHtmlNodeHandle node)
    {
        Check(css.TryGetComputedStyle(node, out ManagedComputedStyle style), "computed-style");
        return style;
    }

    private static ManagedHtmlTreeBuilder Parse(string html, int chunk = 4096)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = ToScalars(html);
        for (int offset = 0; offset < scalars.Count;)
        {
            int length = Math.Min(Math.Min(chunk, ManagedHtmlTokenizerLimits.InputWindowCapacity), scalars.Count - offset);
            uint[] input = new uint[length];
            for (int index = 0; index != length; ++index) input[index] = scalars[offset + index];
            Check(tokenizer.AppendInput(input), "tokenizer-input");
            ManagedHtmlTokenizerProcessResult result = tokenizer.Pump(builder);
            Check(result != ManagedHtmlTokenizerProcessResult.Failed && result != ManagedHtmlTokenizerProcessResult.Cancelled,
                  "tokenizer-pump");
            offset += length;
        }
        Check(tokenizer.Pump(builder, true) == ManagedHtmlTokenizerProcessResult.Complete && builder.Complete(),
              "document-complete");
        return builder;
    }

    private static List<uint> ToScalars(string value)
    {
        List<uint> result = new(value.Length);
        for (int index = 0; index < value.Length; ++index)
        {
            char current = value[index];
            result.Add(char.IsHighSurrogate(current) && index + 1 < value.Length &&
                       char.IsLowSurrogate(value[index + 1])
                ? (uint)char.ConvertToUtf32(current, value[++index]) : current);
        }
        return result;
    }

    private static ManagedHtmlNodeHandle FindTag(ManagedHtmlTreeBuilder builder, ManagedHtmlTag tag)
    {
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle handle = new(index, builder.Document.DocumentNode.Generation);
            if (builder.Document.GetElementTag(handle) == tag) return handle;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static ManagedHtmlNodeHandle FindSecondTag(ManagedHtmlTreeBuilder builder, ManagedHtmlTag tag)
    {
        ManagedHtmlNodeHandle found = ManagedHtmlNodeHandle.Invalid;
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle handle = new(index, builder.Document.DocumentNode.Generation);
            if (builder.Document.GetElementTag(handle) != tag) continue;
            if (found != ManagedHtmlNodeHandle.Invalid) return handle;
            found = handle;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static ManagedHtmlNodeHandle FindById(ManagedHtmlTreeBuilder builder, string id) =>
        FindAttribute(builder, ManagedHtmlAttributeName.Id, id);

    private static ManagedHtmlNodeHandle FindClass(ManagedHtmlTreeBuilder builder, string expected)
    {
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle handle = new(index, builder.Document.DocumentNode.Generation);
            if (builder.Document.GetNodeKind(handle) != ManagedHtmlNodeKind.Element ||
                !builder.Document.TryFindAttribute(handle, ManagedHtmlAttributeName.Class, out ManagedHtmlAttributeView attribute)) continue;
            uint[] buffer = new uint[64];
            builder.Document.TryCopyAttributeValue(handle, attribute.Index, buffer, out int length, out _);
            if (ScalarsToString(buffer[..length]).Contains(expected, StringComparison.Ordinal)) return handle;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static ManagedHtmlNodeHandle FindAttribute(ManagedHtmlTreeBuilder builder,
                                                        ManagedHtmlAttributeName name, string expected)
    {
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle handle = new(index, builder.Document.DocumentNode.Generation);
            if (builder.Document.GetNodeKind(handle) != ManagedHtmlNodeKind.Element ||
                !builder.Document.TryFindAttribute(handle, name, out ManagedHtmlAttributeView attribute)) continue;
            uint[] buffer = new uint[64];
            builder.Document.TryCopyAttributeValue(handle, attribute.Index, buffer, out int length, out _);
            if (ScalarsToString(buffer[..length]) == expected) return handle;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static string ScalarsToString(ReadOnlySpan<uint> scalars)
    {
        StringBuilder result = new();
        for (int index = 0; index != scalars.Length; ++index) result.Append(char.ConvertFromUtf32((int)scalars[index]));
        return result.ToString();
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }
}

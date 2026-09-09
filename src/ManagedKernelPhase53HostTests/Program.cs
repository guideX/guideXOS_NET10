using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            ParseGrammarAndFragmentation();
            CascadeWinningAndNone();
            ProvenanceAndBounds();
            ImageStoreDedupAndReset();
            BackgroundPaintAndRaster();
            Console.WriteLine($"MANAGED_KERNEL_PHASE53_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE53_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void ParseGrammarAndFragmentation()
    {
        string[] values =
        {
            "none",
            "url(\"/phase53/card.png\")",
            "url('/phase53/card.png')",
            "url(/phase53/card.png)",
            "  URL (  \"/phase53/card.png\"  )  "
        };
        for (int index = 0; index != values.Length; ++index)
        {
            ManagedHtmlTreeBuilder builder = Parse("<html><body><div id=x></div></body></html>");
            ManagedCssEngine css = new(builder.Document);
            Check(css.BeginAuthorStyles(), "grammar-begin");
            Check(css.TryParseStylesheet(Scalars("#x{background-image:" + values[index] + ";}")),
                  "grammar-parse-" + index);
            Check(css.CompleteAuthorStyles(), "grammar-complete-" + index);
            ManagedComputedStyle style = GetStyle(css, FindById(builder, "x"));
            if (index == 0)
                Check(style.BackgroundImageKind == ManagedCssBackgroundImageKind.None,
                      "grammar-none");
            else
            {
                Check(style.BackgroundImageKind == ManagedCssBackgroundImageKind.Unresolved,
                      "grammar-unresolved-" + index);
                Check(css.TryGetCssImageReference(style.BackgroundImageReferenceIndex,
                                                   out ManagedCssImageReference reference),
                      "grammar-reference-" + index);
                byte[] url = new byte[512];
                Check(reference.TryCopyUrl(url, out int length) &&
                      url[..length].SequenceEqual("/phase53/card.png"u8),
                      "grammar-url-" + index);
            }
        }

        string fragmented = ".x{background-image:u\"rl(/fragmented.png)\";}";
        ManagedHtmlTreeBuilder completeBuilder = Parse("<html><body><div id=x></div></body></html>");
        ManagedCssEngine complete = new(completeBuilder.Document);
        Check(complete.BeginAuthorStyles() && complete.TryParseStylesheet(Scalars(fragmented)) &&
              complete.CompleteAuthorStyles(), "fragment-complete");
        ManagedComputedStyle completeStyle = GetStyle(complete, FindById(completeBuilder, "x"));
        Check(completeStyle.BackgroundImageKind == ManagedCssBackgroundImageKind.None,
              "fragment-quote-policy");

        foreach (int split in new[] { 1, 2, 3, 4, 5, 9, 15, fragmented.Length - 1 })
        {
            ManagedHtmlTreeBuilder builder = Parse("<html><body><div id=x></div></body></html>");
            ManagedCssEngine css = new(builder.Document);
            ManagedCssStreamingParser parser = new(css);
            Check(css.BeginAuthorStyles(), "stream-fragment-begin");
            parser.Begin();
            Check(parser.State == ManagedResourceConsumerState.Receiving, "stream-fragment-parser-begin");
            uint[] input = Scalars("#x{background-image:url(/fragmented.png);}");
            for (int offset = 0; offset != input.Length;)
            {
                int length = Math.Min(split, input.Length - offset);
                Check(parser.Consume(input.AsSpan(offset, length)) == ManagedHttpBodySinkResult.Continue,
                      "stream-fragment-consume");
                offset += length;
            }
            Check(parser.Complete() && css.CompleteAuthorStyles(), "stream-fragment-complete");
            Check(GetStyle(css, FindById(builder, "x")).BackgroundImageKind ==
                  ManagedCssBackgroundImageKind.Unresolved, "stream-fragment-equivalent");
        }

        Check(ParseCss("#x{background-image:url();}"), "grammar-empty-url-rejected");
        Check(ParseCss("#x{background-image:url(\"unterminated);}"), "grammar-unterminated-quote-rejected");
        Check(ParseCss("#x{background-image:linear-gradient(red,blue);}"), "grammar-gradient-rejected");
    }

    private static void CascadeWinningAndNone()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<html><body><div id=p class=parent><span id=c></span></div>" +
            "<div id=i style=\"background-image:url('/inline.png')\"></div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.BeginAuthorStyles(), "cascade-begin");
        Check(css.TryParseStylesheet(Scalars(".parent{background-image:url('/losing.png')}")), "cascade-a");
        Check(css.TryParseStylesheet(Scalars(".parent{background-image:url('/winning.png') !important}.parent{background-image:none !important}")), "cascade-b");
        Check(css.CompleteAuthorStyles(), "cascade-complete");
        ManagedComputedStyle parent = GetStyle(css, FindById(builder, "p"));
        ManagedComputedStyle child = GetStyle(css, FindById(builder, "c"));
        ManagedComputedStyle inline = GetStyle(css, FindById(builder, "i"));
        Check(parent.BackgroundImageKind == ManagedCssBackgroundImageKind.None,
              "cascade-none-suppresses-url");
        Check(child.BackgroundImageKind == ManagedCssBackgroundImageKind.None,
              "background-not-inherited");
        Check(inline.BackgroundImageKind == ManagedCssBackgroundImageKind.Unresolved,
              "inline-background-supported");
        Check(css.CssImageReferenceCount == 3, "losing-references-retained-bounded");
        Check(css.TryGetCssImageReference(inline.BackgroundImageReferenceIndex,
                                           out ManagedCssImageReference inlineRef) &&
              inlineRef.Origin == ManagedCssImageReferenceOrigin.Inline,
              "inline-origin");
    }

    private static void ProvenanceAndBounds()
    {
        ManagedHtmlTreeBuilder builder = Parse("<html><body><div id=x></div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.BeginAuthorStyles(), "provenance-begin");
        ManagedCssStreamingParser parser = new(css);
        parser.Begin();
        Check(parser.State == ManagedResourceConsumerState.Receiving, "provenance-external-begin");
        uint[] input = Scalars("#x{background-image:url('../images/hero.png');}");
        Check(parser.Consume(input) == ManagedHttpBodySinkResult.Continue && parser.Complete(),
              "provenance-external-complete");
        Check(css.CompleteAuthorStyles(), "provenance-complete");
        ManagedComputedStyle style = GetStyle(css, FindById(builder, "x"));
        Check(css.TryGetCssImageReference(style.BackgroundImageReferenceIndex,
                                           out ManagedCssImageReference reference) &&
              reference.Origin == ManagedCssImageReferenceOrigin.ExternalStylesheet &&
              reference.StylesheetIndex == 0, "external-stylesheet-provenance");
        Span<byte> raw = stackalloc byte[512];
        Check(reference.TryCopyUrl(raw, out int rawLength) &&
              raw[..rawLength].SequenceEqual("../images/hero.png"u8), "raw-css-token");
        Check(ManagedHttpsUrl.TryParse("https://www.example.com/assets/css/final.css"u8,
                                       out ManagedHttpsUrl finalCss), "final-css-url");
        Check(ManagedHttpsUrl.TryResolve(finalCss, raw[..rawLength], out ManagedHttpsUrl image),
              "final-css-resolution");
        Span<byte> absolute = stackalloc byte[512];
        Check(image.TryCopyAbsoluteUrl(absolute, out int absoluteLength) &&
              absolute[..absoluteLength].SequenceEqual(
                  "https://www.example.com/assets/images/hero.png"u8),
              "redirected-final-base-resolution");

        ManagedCssArenaOptions limited = new(
            2, 32, 64, 64, 64, 16, 1, 2);
        ManagedHtmlTreeBuilder limitBuilder = Parse("<html><body><div id=x></div></body></html>");
        ManagedCssEngine exact = new(limitBuilder.Document, limited);
        Check(exact.BeginAuthorStyles() && exact.TryParseStylesheet(
                  Scalars("#x{background-image:url(a.png);background-repeat:no-repeat}")) &&
              exact.CompleteAuthorStyles(), "reference-exact-capacity");
        ManagedCssEngine over = new(limitBuilder.Document, limited);
        Check(over.BeginAuthorStyles() && !over.TryParseStylesheet(
                  Scalars("#x{background-image:url(a.png);background-image:url(b.png);background-image:url(c.png)}")) &&
              over.FailureReason == ManagedCssParseFailureReason.CssImageReferenceCapacityExceeded,
              "reference-capacity-plus-one");
    }

    private static void ImageStoreDedupAndReset()
    {
        ManagedPageImageStore store = new(4, 64, 16, 16);
        Check(store.TryReserve(1, 2, 2, out ManagedImageHandle handle), "dedup-reserve");
        for (int index = 0; index != 4; ++index) Check(store.TryWritePixel(handle, index, 0xFFFF0000U), "dedup-write");
        Check(store.TryComplete(handle, new byte[32]), "dedup-complete");
        Check(store.TryAssociateSourceNode(handle, 2) &&
              store.TryGetForSourceNode(2, out ManagedImageHandle alias) && alias == handle,
              "html-css-shared-alias");
        Check(!store.TryReserve(3, 8, 8, out _), "shared-pixel-budget");
        store.Reset();
        Check(!store.TryGetDescriptor(handle, out _) && !store.TryGetForSourceNode(2, out _),
              "dedup-reset-invalidates");
        Check(store.TryReserve(4, 1, 1, out _), "dedup-reuse");
    }

    private static void BackgroundPaintAndRaster()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<html><body><div id=x class=box></div></body></html>");
        ManagedHtmlNodeHandle node = FindById(builder, "x");
        ManagedCssEngine css = new(builder.Document);
        Check(css.BeginAuthorStyles() && css.TryParseStylesheet(Scalars(
            ".box{width:4px;height:4px;background-color:#123456;" +
            "background-image:url('/image.png');background-repeat:no-repeat;" +
            "border-width:1px;border-style:solid;border-color:white;color:white}")) &&
              css.CompleteAuthorStyles(), "paint-css");
        ManagedPageImageStore store = new(4, 64, 16, 16);
        Check(store.TryReserve(node.Index, 2, 2, out ManagedImageHandle image), "paint-image-reserve");
        uint[] pixels = { 0xFFFF0000U, 0x00000000U, 0x8000FF00U, 0xFF0000FFU };
        for (int index = 0; index != pixels.Length; ++index)
            Check(store.TryWritePixel(image, index, pixels[index]), "paint-image-write");
        Check(store.TryComplete(image, new byte[32]) && css.TrySetBackgroundImageHandle(node, image),
              "paint-style-handle");
        ManagedLayoutEngine layout = new(builder.Document, css,
            ManagedLayoutArenaOptions.Default, null, store);
        Check(layout.TryLayout(20, 20), "paint-layout");
        Check(layout.TryGetBoxForNode(node, out int boxIndex), "paint-box-index");
        Check(layout.TryGetBox(boxIndex, out ManagedLayoutBox box), "paint-box");
        ManagedPaintEngine paint = new(layout, ManagedPaintArenaOptions.Default, null, store);
        Check(paint.TryGenerate(20, 20) && paint.BackgroundImageCommands == 1,
              "background-command");
        Check(paint.Validate(out ManagedPaintValidationFailureReason validation),
              "background-command-validation-" + validation);
        uint[] framebuffer = new uint[400];
        ManagedSoftwareRasterizer raster = new();
        Check(raster.TryRender(paint, new ManagedFramebuffer(framebuffer, 20, 20)),
              "background-raster");
        bool gotOpaque = new ManagedFramebuffer(framebuffer, 20, 20).TryGetPixel(
            box.PaddingBox.X, box.PaddingBox.Y, out uint opaque);
        Check(gotOpaque && opaque == 0xFFFF0000U,
              "opaque-background-pixel-" + box.PaddingBox.X + "-" + box.PaddingBox.Y + "-" + opaque.ToString("X8"));
        Check(new ManagedFramebuffer(framebuffer, 20, 20).TryGetPixel(box.PaddingBox.X + 1,
                  box.PaddingBox.Y, out uint transparent) && transparent == 0xFF123456U,
              "transparent-exposes-background-color");
        Check(new ManagedFramebuffer(framebuffer, 20, 20).TryGetPixel(box.PaddingBox.X,
                  box.PaddingBox.Y + 1, out uint half) && half == 0xFF099A2BU,
              "half-alpha-source-over");
        Check(new ManagedFramebuffer(framebuffer, 20, 20).TryGetPixel(box.BorderBox.X,
                  box.BorderBox.Y, out uint border) && border == 0xFFFFFFFFU,
              "border-after-background");
        Check(new ManagedFramebuffer(framebuffer, 20, 20).TryGetPixel(10, 10, out uint outside) &&
              outside == 0xFF000000U, "background-clipped-to-box-" + outside.ToString("X8"));
    }

    private static bool ParseCss(string cssText)
    {
        ManagedHtmlTreeBuilder builder = Parse("<html><body><div id=x></div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        return css.BeginAuthorStyles() && css.TryParseStylesheet(Scalars(cssText)) &&
               css.CompleteAuthorStyles();
    }

    private static ManagedHtmlTreeBuilder Parse(string html)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        uint[] scalars = Scalars(html);
        for (int offset = 0; offset != scalars.Length;)
        {
            int length = Math.Min(7, scalars.Length - offset);
            Check(tokenizer.AppendInput(scalars.AsSpan(offset, length)), "html-input");
            Check(tokenizer.Pump(builder) != ManagedHtmlTokenizerProcessResult.Failed, "html-pump");
            offset += length;
        }
        Check(tokenizer.Pump(builder, true) == ManagedHtmlTokenizerProcessResult.Complete &&
              builder.Complete(), "html-complete");
        return builder;
    }

    private static uint[] Scalars(string value)
    {
        List<uint> result = new();
        foreach (char scalar in value) result.Add(scalar);
        return result.ToArray();
    }

    private static ManagedComputedStyle GetStyle(ManagedCssEngine css,
                                                  ManagedHtmlNodeHandle node)
    {
        Check(css.TryGetComputedStyle(node, out ManagedComputedStyle style), "computed-style");
        return style;
    }

    private static ManagedHtmlNodeHandle FindById(ManagedHtmlTreeBuilder builder, string id)
    {
        uint[] expected = Scalars(id);
        Span<uint> value = stackalloc uint[64];
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, builder.Document.DocumentNode.Generation);
            if (builder.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !builder.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Id,
                                                   out ManagedHtmlAttributeView view) ||
                !builder.Document.TryCopyAttributeValue(node, view.Index, value,
                                                         out int length, out bool hasValue) ||
                !hasValue || length != expected.Length) continue;
            bool equal = true;
            for (int offset = 0; offset != length; ++offset) equal &= value[offset] == expected[offset];
            if (equal) return node;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }
}

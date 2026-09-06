using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            SourceOrderAndTypePolicy();
            ExactPhase51Document();
            ExactPhase51GzipDecoder();
            FragmentationAndCommentBoundaries();
            UrlPolicyAndBounds();
            CancelResetAndPageOptions();
            PipelineSmoke();
            Console.WriteLine($"MANAGED_KERNEL_PHASE51_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE51_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void SourceOrderAndTypePolicy()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<html><head><link rel='preload StYLeShEeT' href='/a.css'><style>#x{color:green}</style>" +
            "<link rel='stylesheet' href='/b.css'><link rel='AlTeRnAtE StYLeShEeT' href='/alt.css'>" +
            "<link rel='stylesheet' type='text/plain' href='/skip.css'><link rel='notstylesheet' href='/no.css'>" +
            "<link href='/missing-rel.css'><link rel='' href='/empty-rel.css'></head>" +
            "<body><div id=x>order</div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.BeginAuthorStyles(), "begin-author-styles");
        int external = 0;
        int embedded = 0;
        int alternate = 0;
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            if (!css.TryGetStyleSource(index, out ManagedCssStyleSource source)) continue;
            if (source.Kind == ManagedCssStyleSourceKind.Embedded)
            {
                ++embedded;
                Check(css.TryParseEmbeddedStylesheet(source.Node), "embedded-source");
                continue;
            }
            if (source.IsAlternate) { ++alternate; continue; }
            if (source.HasType && !IsCssType(builder, source.Node)) continue;
            ManagedCssStreamingParser parser = new(css);
            parser.Begin();
            string text = external++ == 0 ? "#x{color:red}" : "#x{color:blue}";
            uint[] scalars = ToScalars(text).ToArray();
            for (int offset = 0; offset < scalars.Length; offset += 2)
            {
                int length = Math.Min(2, scalars.Length - offset);
                Check(parser.Consume(scalars.AsSpan(offset, length)) == ManagedHttpBodySinkResult.Continue,
                      "external-window");
            }
            Check(parser.Complete(), "external-complete");
        }
        Check(css.CompleteAuthorStyles(), "complete-author-styles");
        ManagedComputedStyle style = GetStyle(css, FindById(builder, "x"));
        Check(external == 2 && embedded == 1 && alternate == 1, "source-classification");
        Check(style.Color == 0xFF0000FF, "document-order-winner");
        Check(css.StylesheetsParsed == 3 && css.RulesParsed == 3, "shared-css-arenas");
        Check(css.Telemetry.ExternalStylesheetCount == 0, "metadata-only-external-list");
    }

    private static void ExactPhase51Document()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<!doctype html><html><head>" +
            "<link rel=\"stylesheet\" href=\"/phase51/a.css\">" +
            "<style>.target { color: green; }</style>" +
            "<link rel=\"stylesheet\" href=\"b.css\">" +
            "</head><body><div id=\"target\" class=\"target\">" +
            "cafe&#233; &#8212; r&#233;sum&#233; &#169; guideXOS&#8482;" +
            "</div><p>managed page resource</p></body></html>");
        Check(builder.Document.NodeCount >= 8 && builder.Document.Validate(out _),
              "exact-phase51-document");
    }

    private static void ExactPhase51GzipDecoder()
    {
        const string html = "<!doctype html><html><head>" +
            "<link rel=\"stylesheet\" href=\"/phase51/a.css\">" +
            "<style>.target { color: green; }</style>" +
            "<link rel=\"stylesheet\" href=\"b.css\"></head>" +
            "<body><div id=\"target\" class=\"target\">" +
            "cafe&#233; &#8212; r&#233;sum&#233; &#169; guideXOS&#8482;" +
            "</div><p>managed page resource</p></body></html>";
        byte[] plain = Encoding.UTF8.GetBytes(html);
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Optimal, true))
            gzip.Write(plain, 0, plain.Length);
        ManagedContentEncodingDecoder decoder = new(
            ManagedHttpContentEncodingState.Gzip, 16 * 1024);
        byte[] encoded = output.ToArray();
        for (int offset = 0; offset != encoded.Length;)
        {
            int length = Math.Min(7, encoded.Length - offset);
            Check(decoder.AppendInput(encoded.AsSpan(offset, length)), "exact-gzip-input");
            DrainDecoder(decoder, false);
            offset += length;
        }
        DrainDecoder(decoder, true);
        Check(decoder.IsComplete && decoder.DecodedBytesProduced == plain.Length,
              "exact-gzip-complete");
    }

    private static void DrainDecoder(ManagedContentEncodingDecoder decoder,
                                     bool endOfInput)
    {
        while (true)
        {
            ManagedContentDecoderProcessResult result = decoder.Pump(endOfInput);
            if (result == ManagedContentDecoderProcessResult.OutputAvailable)
            {
                Check(decoder.ConsumeOutput(new CountingSink()) ==
                      ManagedHttpBodyDeliveryResult.Delivered, "exact-gzip-output");
                continue;
            }
            Check(result == ManagedContentDecoderProcessResult.NeedInput ||
                  result == ManagedContentDecoderProcessResult.Complete,
                  "exact-gzip-pump");
            return;
        }
    }

    private sealed class CountingSink : IManagedHttpBodySink
    {
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment) =>
            ManagedHttpBodySinkResult.Continue;
    }

    private static void FragmentationAndCommentBoundaries()
    {
        const string cssText = "/* leading */ #x { color: red; /* split comment */ background-color: #123456; }";
        (uint expectedColor, uint expectedBackground) = StyleWithEmbedded(cssText);
        for (int split = 0; split <= cssText.Length; ++split)
        {
            ManagedHtmlTreeBuilder builder = Parse("<html><body><div id=x>text</div></body></html>");
            ManagedCssEngine css = new(builder.Document);
            Check(css.BeginAuthorStyles(), "fragment-begin");
            ManagedCssStreamingParser parser = new(css);
            parser.Begin();
            uint[] scalars = ToScalars(cssText).ToArray();
            Check(parser.Consume(scalars.AsSpan(0, split)) == ManagedHttpBodySinkResult.Continue,
                  "fragment-first-window");
            Check(parser.Consume(scalars.AsSpan(split)) == ManagedHttpBodySinkResult.Continue,
                  "fragment-second-window");
            Check(parser.Complete() && css.CompleteAuthorStyles(), "fragment-finish");
            ManagedComputedStyle actual = GetStyle(css, FindById(builder, "x"));
            Check(actual.Color == expectedColor && actual.BackgroundColor == expectedBackground,
                  "fragment-computed-style");
        }

        ManagedHtmlTreeBuilder oneScalarBuilder = Parse("<div id=x></div>");
        ManagedCssEngine oneScalarCss = new(oneScalarBuilder.Document);
        Check(oneScalarCss.BeginAuthorStyles(), "one-scalar-begin");
        ManagedCssStreamingParser oneScalar = new(oneScalarCss);
        oneScalar.Begin();
        uint[] oneScalarSource = ToScalars("#x{/*a*/color:#abcdef}").ToArray();
        for (int index = 0; index != oneScalarSource.Length; ++index)
            Check(oneScalar.Consume(oneScalarSource.AsSpan(index, 1)) == ManagedHttpBodySinkResult.Continue,
                  "one-scalar-window");
        Check(oneScalar.Complete() && oneScalarCss.CompleteAuthorStyles(), "one-scalar-finish");
        Check(GetStyle(oneScalarCss, FindById(oneScalarBuilder, "x")).Color == 0xFFABCDEF,
              "one-scalar-color");
    }

    private static void UrlPolicyAndBounds()
    {
        Check(ManagedHttpsUrl.TryParse("https://example.test/page/index.html"u8,
                                       out ManagedHttpsUrl current), "base-url");
        Check(ManagedHttpsUrl.TryResolve(current, "../css/site.css"u8,
                                         out ManagedHttpsUrl relative) &&
              relative.RequestTarget.SequenceEqual("/page/../css/site.css"u8) == false,
              "relative-url");
        Check(relative.RequestTarget.SequenceEqual("/css/site.css"u8), "normalized-relative-url");
        Check(ManagedHttpsUrl.TryResolve(current, "//cdn.example.test/site.css"u8,
                                         out ManagedHttpsUrl authority) &&
              authority.Hostname.SequenceEqual("cdn.example.test"u8), "scheme-relative-url");
        Check(!ManagedHttpsUrl.TryResolve(current, "http://insecure.test/site.css"u8,
                                          out _, out ManagedHttpsUrlParseFailureReason downgrade) &&
              downgrade == ManagedHttpsUrlParseFailureReason.HttpsDowngrade,
              "downgrade-rejection");
        Check(!ManagedHttpsUrl.TryResolve(current, "javascript:alert(1)"u8,
                                          out _, out ManagedHttpsUrlParseFailureReason scheme) &&
              scheme == ManagedHttpsUrlParseFailureReason.UnsupportedReference,
              "unsupported-scheme-rejection");
        byte[] tooLong = new byte[ManagedHttpsUrl.MaximumLocationLength + 1];
        tooLong.AsSpan().Fill((byte)'a');
        Check(!ManagedHttpsUrl.TryResolve(current, tooLong, out _,
                                          out ManagedHttpsUrlParseFailureReason longFailure) &&
              longFailure == ManagedHttpsUrlParseFailureReason.TooLong,
              "location-limit");
    }

    private static void CancelResetAndPageOptions()
    {
        ManagedHtmlTreeBuilder builder = Parse("<div id=x></div>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.BeginAuthorStyles(), "cancel-begin");
        ManagedCssStreamingParser parser = new(css);
        parser.Begin();
        Check(parser.Consume(ToScalars("#x{color:red}").ToArray()) == ManagedHttpBodySinkResult.Continue,
              "cancel-consume");
        parser.Cancel();
        Check(parser.State == ManagedResourceConsumerState.Cancelled && !parser.Complete(),
              "cancel-state");
        parser.Reset();
        Check(parser.State == ManagedResourceConsumerState.Idle && css.BeginExternalStylesheet(),
              "reset-state");
        css.CancelExternalStylesheet();
        ManagedCssArenaOptions arenas = new(4, 32, 64, 128, 128, 64, 2);
        ManagedPageResourceOptions options = new(arenas, 2, 320, 200);
        Check(options.ExternalStylesheetLimit == 2 && options.ViewportWidth == 320,
              "page-options");
        bool rejected = false;
        try { _ = new ManagedPageResourceOptions(arenas, 3); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "page-limit-validation");
    }

    private static void PipelineSmoke()
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<html><head><style>body{color:#345678;font-size:16px} div{background:#abcdef}</style></head>" +
            "<body><div>raster smoke</div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "pipeline-style");
        ManagedLayoutEngine layout = new(builder.Document, css);
        Check(layout.TryLayout(320, 200), "pipeline-layout");
        ManagedPaintEngine paint = new(layout);
        Check(paint.TryGenerate(320, 200), "pipeline-paint");
        uint[] storage = new uint[320 * 200];
        ManagedFramebuffer framebuffer = new(storage, 320, 200);
        ManagedSoftwareRasterizer rasterizer = new();
        Check(rasterizer.TryRender(paint, framebuffer) && rasterizer.HashValid,
              "pipeline-raster");
    }

    private static (uint Color, uint Background) StyleWithEmbedded(string cssText)
    {
        ManagedHtmlTreeBuilder builder = Parse("<html><head><style>" + cssText +
                                               "</style></head><body><div id=x>text</div></body></html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "embedded-reference-style");
        ManagedComputedStyle style = GetStyle(css, FindById(builder, "x"));
        return (style.Color, style.BackgroundColor);
    }

    private static bool IsCssType(ManagedHtmlTreeBuilder builder, ManagedHtmlNodeHandle node)
    {
        if (!builder.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Type,
                                               out ManagedHtmlAttributeView type) ||
            !type.HasValue) return true;
        uint[] value = new uint[128];
        if (!builder.Document.TryCopyAttributeValue(node, type.Index, value,
                                                     out int length, out bool hasValue) ||
            !hasValue) return false;
        string text = ToString(value.AsSpan(0, length)).Trim();
        return text.Equals("text/css", StringComparison.OrdinalIgnoreCase);
    }

    private static ManagedComputedStyle GetStyle(ManagedCssEngine css, ManagedHtmlNodeHandle node)
    {
        Check(css.TryGetComputedStyle(node, out ManagedComputedStyle style), "computed-style");
        return style;
    }

    private static ManagedHtmlTreeBuilder Parse(string html)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = ToScalars(html);
        for (int offset = 0; offset != scalars.Count;)
        {
            int length = Math.Min(37, scalars.Count - offset);
            uint[] window = new uint[length];
            for (int index = 0; index != length; ++index) window[index] = scalars[offset + index];
            Check(tokenizer.AppendInput(window), "html-input");
            ManagedHtmlTokenizerProcessResult result = tokenizer.Pump(builder);
            Check(result != ManagedHtmlTokenizerProcessResult.Failed &&
                  result != ManagedHtmlTokenizerProcessResult.Cancelled, "html-pump");
            offset += length;
        }
        Check(tokenizer.Pump(builder, true) == ManagedHtmlTokenizerProcessResult.Complete &&
              builder.Complete(), "html-complete");
        return builder;
    }

    private static List<uint> ToScalars(string value)
    {
        List<uint> result = new(value.Length);
        for (int index = 0; index != value.Length; ++index)
        {
            char current = value[index];
            result.Add(char.IsHighSurrogate(current) && index + 1 < value.Length &&
                       char.IsLowSurrogate(value[index + 1])
                ? (uint)char.ConvertToUtf32(current, value[++index]) : current);
        }
        return result;
    }

    private static ManagedHtmlNodeHandle FindById(ManagedHtmlTreeBuilder builder, string id)
    {
        return FindById(builder.Document.DocumentNode, builder.Document, id);
    }

    private static ManagedHtmlNodeHandle FindById(ManagedHtmlNodeHandle node,
                                                   ManagedHtmlDocument document, string id)
    {
        if (document.GetElementTag(node) == ManagedHtmlTag.Div &&
            document.TryFindAttribute(node, ManagedHtmlAttributeName.Id,
                                      out ManagedHtmlAttributeView attribute))
        {
            uint[] value = new uint[64];
            document.TryCopyAttributeValue(node, attribute.Index, value, out int length, out _);
            if (ToString(value.AsSpan(0, length)) == id) return node;
        }
        ManagedHtmlNodeHandle child = document.GetFirstChild(node);
        while (!child.IsInvalid)
        {
            ManagedHtmlNodeHandle found = FindById(child, document, id);
            if (!found.IsInvalid) return found;
            child = document.GetNextSibling(child);
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static string ToString(ReadOnlySpan<uint> scalars)
    {
        StringBuilder result = new();
        for (int index = 0; index != scalars.Length; ++index)
            result.Append(char.ConvertFromUtf32((int)scalars[index]));
        return result.ToString();
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }
}

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
            EmptyRootAndTransparentBackground();
            PrimitiveAndReferenceCommands();
            VisibilityDisplayNoneAndOpacity();
            ClippingAndOverflow();
            OrderingAndScroll();
            CapacityCancellationAndReset();
            ValidatorCoverage();
            Console.WriteLine($"MANAGED_KERNEL_PHASE46_SIZES command={Unsafe.SizeOf<ManagedPaintCommand>()} rect={Unsafe.SizeOf<ManagedLayoutRect>()} edges={Unsafe.SizeOf<ManagedLayoutEdges>()} options={Unsafe.SizeOf<ManagedPaintArenaOptions>()}");
            Console.WriteLine($"MANAGED_KERNEL_PHASE46_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE46_HOST_TESTS_FAIL cases={s_cases} error={error}");
            return 1;
        }
    }

    private static void EmptyRootAndTransparentBackground()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<body><div id=empty></div><div id=transparent style='background-color:transparent'></div></body>", "");
        ManagedLayoutEngine layout = Layout(builder, css, 120, 60);
        ManagedPaintEngine paint = new(layout);
        Check(paint.TryGenerate(120, 60), "empty-generate");
        Check(paint.CommandsEmitted == 2 && paint.FillCommands == 0 && paint.BorderCommands == 0,
              "empty-root-clip-only");
        Check(paint.ClipPushes == 0 && paint.ClipPops == 1 && paint.CurrentClipDepth == 0,
              "balanced-root-clip");
        Check(paint.Validate(out ManagedPaintValidationFailureReason emptyValidation) &&
              emptyValidation == ManagedPaintValidationFailureReason.None, "empty-validator");
        Check(paint.Telemetry.TransparentBackgroundsSkipped >= 1, "transparent-background-skipped");
        Check(paint.RemainingCommandCapacity == paint.CommandCapacity - 2, "remaining-capacity");
    }

    private static void PrimitiveAndReferenceCommands()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<div id=box><p id=text>hello <span>world</span> 🙂</p><img id=logo width=20 height=10></div>",
            "#box{background-color:#123456;border-width:2px;border-style:solid;border-color:#abcdef;padding:3px} #text{font-size:20px;font-weight:bold;font-style:italic;color:#102030} span{color:#405060} #logo{width:20px;height:10px}");
        ManagedLayoutEngine layout = Layout(builder, css, 240, 100);
        ManagedPaintEngine paint = new(layout);
        Check(paint.TryGenerate(240, 100), "primitive-generate");
        Check(paint.Validate(out ManagedPaintValidationFailureReason validation) &&
              validation == ManagedPaintValidationFailureReason.None, "primitive-validator");
        Check(paint.FillCommands >= 1 && paint.BorderCommands >= 1 && paint.TextCommands >= 2 &&
              paint.ImagePlaceholderCommands == 1, "primitive-command-kinds");
        ManagedHtmlNodeHandle boxNode = FindId(builder, "box");
        Check(layout.TryGetBoxForNode(boxNode, out int boxIndex), "box-index");
        ManagedPaintCommand boxFill = FindCommand(paint, ManagedPaintCommandKind.FillRectangle, boxIndex);
        Check(boxFill.Color == 0xFF123456U && boxFill.Rect == GetBox(layout, boxIndex).BorderBox,
              "background-border-geometry-color");
        ManagedPaintCommand border = FindCommand(paint, ManagedPaintCommandKind.BorderRectangle, boxIndex);
        Check(border.BorderWidths == new ManagedLayoutEdges(2, 2, 2, 2) &&
              border.BorderStyle == ManagedCssBorderStyle.Solid && border.Color == 0xFFABCDEFU,
              "border-geometry-color");
        ManagedPaintCommand text = FindFirst(paint, ManagedPaintCommandKind.TextRun);
        Check(text.SourceNodeIndex >= 0 && text.SourceOffset >= 0 && text.SourceLength > 0 &&
              text.LineIndex >= 0 && text.BaselineY >= text.Rect.Y &&
              text.FontSize == 20 && text.FontWeight == 700 &&
              text.FontStyle == ManagedCssFontStyle.Italic && text.Color == 0xFF102030U &&
              text.FontId == ManagedPaintFontId.DefaultUi, "text-reference-metadata");
        Check(text.SourceLength < builder.Document.TextScalarsUsed &&
              text.SourceNodeIndex != boxNode.Index, "text-not-copied");
        ManagedHtmlNodeHandle imageNode = FindId(builder, "logo");
        Check(layout.TryGetBoxForNode(imageNode, out int imageIndex), "image-index");
        ManagedPaintCommand image = FindCommand(paint, ManagedPaintCommandKind.ImagePlaceholder, imageIndex);
        Check(image.SourceNodeIndex == imageNode.Index && image.Rect.Width == 20 && image.Rect.Height == 10,
              "image-placeholder-reference-geometry");
        Check(paint.Telemetry.CommandsEmitted == paint.CommandsEmitted &&
              paint.Telemetry.PeakCommandUsage >= paint.CommandsEmitted, "paint-telemetry-snapshot");
    }

    private static void VisibilityDisplayNoneAndOpacity()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<div id=hidden style='visibility:hidden;background-color:red'><span>hidden text</span></div>" +
            "<div id=none style='display:none;background-color:blue'><span>absent</span></div>" +
            "<div id=half style='background-color:#f008;color:#fff8;opacity:.5'>visible</div>" +
            "<div id=last style='background-color:green'>later</div>", "");
        ManagedLayoutEngine layout = Layout(builder, css, 240, 100);
        ManagedPaintEngine paint = new(layout);
        Check(paint.TryGenerate(240, 100), "visibility-generate");
        ManagedHtmlNodeHandle hidden = FindId(builder, "hidden");
        ManagedHtmlNodeHandle none = FindId(builder, "none");
        Check(layout.TryGetBoxForNode(hidden, out int hiddenIndex), "hidden-has-layout");
        Check(!layout.TryGetBoxForNode(none, out _), "display-none-no-layout");
        Check(!HasSourceBoxCommand(paint, hiddenIndex), "visibility-hidden-no-paint");
        Check(paint.DisplayNoneBoxesSkipped >= 1 && paint.HiddenBoxesSkipped >= 1,
              "visibility-telemetry");
        ManagedPaintCommand half = FindCommand(paint, ManagedPaintCommandKind.FillRectangle,
                                                GetBoxIndex(layout, builder, "half"));
        Check(half.Color == 0x44FF0000U, "opacity-multiplies-alpha");
        Check(HasSourceBoxCommand(paint, GetBoxIndex(layout, builder, "last")),
              "later-visible-sibling-paints");
    }

    private static void ClippingAndOverflow()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<div id=clip style='width:40px;height:20px;overflow:hidden;background-color:#eeeeee;padding:2px'>" +
            "<div id=child style='width:100px;height:10px;background-color:red'>clipped child</div></div>", "");
        ManagedLayoutEngine layout = Layout(builder, css, 160, 80);
        ManagedPaintEngine paint = new(layout);
        Check(paint.TryGenerate(160, 80), "clip-generate");
        Check(paint.ClipPushes == 1 && paint.ClipPops == 2 && paint.PeakClipDepth == 2,
              "clip-stack-balanced");
        ManagedPaintCommand begin = FindCommand(paint, ManagedPaintCommandKind.BeginClip,
                                                 GetBoxIndex(layout, builder, "clip"));
        Check(begin.SourceBoxIndex == GetBoxIndex(layout, builder, "clip") &&
              begin.Rect == GetBox(layout, begin.SourceBoxIndex).ClipRect,
              "clip-intersection-source");
        ManagedPaintCommand child = FindCommand(paint, ManagedPaintCommandKind.FillRectangle,
                                                 GetBoxIndex(layout, builder, "child"));
        Check(child.ClipDepth == 2 && child.ClipRect == begin.Rect, "child-active-clip");
        Check(paint.Validate(out _), "clip-validator");

        (builder, css) = Styled("<div style='width:30px;height:20px;overflow:hidden'><div style='width:30px;height:20px;overflow:hidden'><div style='width:30px;height:20px;overflow:hidden'>x</div></div></div>", "");
        layout = Layout(builder, css, 100, 100);
        paint = new(layout, new ManagedPaintArenaOptions(256, 2, 4096));
        Check(!paint.TryGenerate(100, 100) && paint.FailureReason == ManagedPaintFailureReason.PaintClipDepthExceeded,
              "clip-depth-negative");
        paint.Reset();
        Check(!paint.TryGenerate(100, 100) && paint.FailureReason == ManagedPaintFailureReason.PaintClipDepthExceeded,
              "clip-depth-reset-remains-bounded");
        paint = new(layout);
        Check(paint.TryGenerate(100, 100), "clip-depth-reset-new-arena");
    }

    private static void OrderingAndScroll()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<div id=normal style='background-color:red;width:20px;height:10px'>n</div>" +
            "<div id=negative style='position:absolute;left:2px;top:2px;width:10px;height:10px;z-index:-1;background-color:blue'>neg</div>" +
            "<div id=positive style='position:absolute;left:4px;top:4px;width:10px;height:10px;z-index:2;background-color:green'>pos</div>", "");
        ManagedLayoutEngine layout = Layout(builder, css, 100, 50);
        Span<byte> layoutHash = stackalloc byte[ManagedSha256.DigestSize];
        Check(layout.TryCopyCanonicalLayoutHash(layoutHash), "ordering-layout-hash");
        ManagedPaintEngine first = new(layout);
        ManagedPaintEngine second = new(layout);
        Check(first.TryGenerate(100, 50, 0, 0) && second.TryGenerate(100, 50, 0, 0), "ordering-generate");
        Span<byte> firstHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> secondHash = stackalloc byte[ManagedSha256.DigestSize];
        Check(first.TryCopyCanonicalPaintHash(firstHash) && second.TryCopyCanonicalPaintHash(secondHash) &&
              firstHash.SequenceEqual(secondHash), "repeat-display-hash");
        Check(first.NegativeZOrderCount > 0 && first.PositiveZOrderCount > 0 &&
              first.NormalZOrderCount > 0, "z-order-buckets");
        int negative = GetBoxIndex(layout, builder, "negative");
        int normal = GetBoxIndex(layout, builder, "normal");
        int positive = GetBoxIndex(layout, builder, "positive");
        Check(IndexOfSource(first, negative) < IndexOfSource(first, normal) &&
              IndexOfSource(first, normal) < IndexOfSource(first, positive), "z-order-paint-order");
        ManagedPaintEngine scrolled = new(layout);
        Check(scrolled.TryGenerate(100, 50, 3, 4), "scroll-generate");
        Span<byte> scrollHash = stackalloc byte[ManagedSha256.DigestSize];
        Check(scrolled.TryCopyCanonicalPaintHash(scrollHash) && !scrollHash.SequenceEqual(firstHash),
              "scroll-changes-display-hash");
        Span<byte> layoutHashAgain = stackalloc byte[ManagedSha256.DigestSize];
        Check(layout.TryCopyCanonicalLayoutHash(layoutHashAgain) && layoutHash.SequenceEqual(layoutHashAgain),
              "scroll-does-not-mutate-layout");
        ManagedPaintCommand normalFirst = FindCommand(first, ManagedPaintCommandKind.FillRectangle, normal);
        ManagedPaintCommand normalScrolled = FindCommand(scrolled, ManagedPaintCommandKind.FillRectangle, normal);
        Check(normalScrolled.Rect.X == normalFirst.Rect.X - 3 && normalScrolled.Rect.Y == normalFirst.Rect.Y - 4,
              "scroll-transforms-command-geometry");
    }

    private static void CapacityCancellationAndReset()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled(
            "<div style='background-color:red;border:1px solid black'>one</div><div>two</div>", "");
        ManagedLayoutEngine layout = Layout(builder, css, 160, 80);
        ManagedPaintEngine sizing = new(layout);
        Check(sizing.TryGenerate(160, 80) && sizing.CommandsEmitted > 2, "capacity-sizing");
        ManagedPaintEngine exact = new(layout, new ManagedPaintArenaOptions(
            sizing.CommandsEmitted, 64, 4096));
        Check(exact.TryGenerate(160, 80) && exact.CommandsEmitted == sizing.CommandsEmitted,
              "exact-command-capacity");
        ManagedPaintEngine negative = new(layout, new ManagedPaintArenaOptions(
            sizing.CommandsEmitted - 1, 64, 4096));
        Check(!negative.TryGenerate(160, 80) &&
              negative.FailureReason == ManagedPaintFailureReason.PaintCommandCapacityExceeded &&
              negative.CommandsEmitted == 0 && negative.RemainingCommandCapacity == negative.CommandCapacity,
              "command-capacity-negative-no-overwrite");

        ManagedPaintEngine cancelled = new(layout);
        cancelled.Cancel();
        Check(!cancelled.TryGenerate(160, 80) && cancelled.State == ManagedPaintState.Cancelled &&
              cancelled.FailureReason == ManagedPaintFailureReason.Cancelled && cancelled.CommandsEmitted == 0,
              "cancel-before-generation");
        cancelled.Reset();
        cancelled.CancelAfterCommands(1);
        Check(!cancelled.TryGenerate(160, 80) && cancelled.State == ManagedPaintState.Cancelled &&
              cancelled.CommandsEmitted == 1 && cancelled.CurrentClipDepth == 1,
              "cancel-after-first-command");
        cancelled.Reset();
        Check(cancelled.TryGenerate(160, 80) && cancelled.State == ManagedPaintState.Generated,
              "reset-after-cancel");
        Span<byte> firstHash = stackalloc byte[ManagedSha256.DigestSize];
        Check(cancelled.TryCopyCanonicalPaintHash(firstHash), "reset-hash");
        cancelled.Reset();
        Check(cancelled.State == ManagedPaintState.Reset && cancelled.CommandsEmitted == 0 &&
              cancelled.Telemetry.CommandsEmitted == 0 && !cancelled.CanonicalHashAvailable,
              "telemetry-reset");
        Check(cancelled.TryGenerate(160, 80), "regenerate-after-reset");
        Span<byte> secondHash = stackalloc byte[ManagedSha256.DigestSize];
        Check(cancelled.TryCopyCanonicalPaintHash(secondHash) && firstHash.SequenceEqual(secondHash),
              "regenerated-hash-identical");
    }

    private static void ValidatorCoverage()
    {
        (ManagedHtmlTreeBuilder builder, ManagedCssEngine css) = Styled("<p id=text>x</p>", "");
        ManagedLayoutEngine layout = Layout(builder, css, 100, 50);
        ManagedPaintEngine paint = new(layout);
        Check(paint.TryGenerate(100, 50), "validator-base-generate");
        ManagedPaintCommand unmatched = new(ManagedPaintCommandKind.BeginClip, 1,
            ManagedPaintCommandFlags.None, -1, -1, 0, 0, -1, 0,
            new ManagedLayoutRect(0, 0, 10, 10), new ManagedLayoutRect(0, 0, 10, 10), 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        Check(!ManagedPaintValidator.Validate(new[] { unmatched }, builder.Document, layout,
            out ManagedPaintValidationFailureReason unmatchedReason) &&
              unmatchedReason == ManagedPaintValidationFailureReason.ClipNotBalanced,
              "validator-unmatched-clip");
        ManagedHtmlNodeHandle textNode = FindId(builder, "text");
        Check(layout.TryGetBoxForNode(textNode, out int textElementBox), "validator-text-element-box");
        ManagedPaintCommand badText = new(ManagedPaintCommandKind.TextRun, 0,
            ManagedPaintCommandFlags.None, textElementBox, textNode.Index, 0, 99, 0, 0,
            new ManagedLayoutRect(0, 0, 1, 1), new ManagedLayoutRect(0, 0, 10, 10), 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 16, 400, ManagedCssFontStyle.Normal, 10_000, 0);
        Check(!ManagedPaintValidator.Validate(new[] { badText }, builder.Document, layout, out _),
              "validator-invalid-text-ref");
        ManagedPaintCommand badBox = new(ManagedPaintCommandKind.FillRectangle, 0,
            ManagedPaintCommandFlags.None, 999, textNode.Index, 0, 0, -1, 0,
            new ManagedLayoutRect(0, 0, 1, 1), new ManagedLayoutRect(0, 0, 10, 10), 0,
            new ManagedLayoutEdges(0, 0, 0, 0), ManagedCssBorderStyle.None,
            ManagedPaintFontId.DefaultUi, 0, 0, ManagedCssFontStyle.Normal, 10_000, 0);
        Check(!ManagedPaintValidator.Validate(new[] { badBox }, builder.Document, layout, out _),
              "validator-invalid-box-ref");
    }

    private static ManagedLayoutEngine Layout(ManagedHtmlTreeBuilder builder,
                                              ManagedCssEngine css, int width, int height)
    {
        ManagedLayoutEngine layout = new(builder.Document, css);
        Check(layout.TryLayout(width, height), "layout-success");
        Check(layout.Validate(out ManagedLayoutValidationFailureReason reason) &&
              reason == ManagedLayoutValidationFailureReason.None, "layout-valid");
        return layout;
    }

    private static (ManagedHtmlTreeBuilder Builder, ManagedCssEngine Css) Styled(
        string body, string cssText)
    {
        ManagedHtmlTreeBuilder builder = Parse(
            "<!doctype html><html><head><style>" + cssText +
            "</style></head>" + body + "</html>");
        ManagedCssEngine css = new(builder.Document);
        Check(css.TryStyle(), "style-success");
        return (builder, css);
    }

    private static ManagedPaintCommand FindFirst(ManagedPaintEngine paint,
                                                  ManagedPaintCommandKind kind)
    {
        for (int index = 0; index != paint.CommandsEmitted; ++index)
        {
            Check(paint.TryGetCommand(index, out ManagedPaintCommand command), "command-read");
            if (command.Kind == kind) return command;
        }
        throw new InvalidOperationException($"missing command {kind}");
    }

    private static ManagedPaintCommand FindCommand(ManagedPaintEngine paint,
                                                   ManagedPaintCommandKind kind, int boxIndex)
    {
        for (int index = 0; index != paint.CommandsEmitted; ++index)
        {
            Check(paint.TryGetCommand(index, out ManagedPaintCommand command), "command-read-by-box");
            if (command.Kind == kind && command.SourceBoxIndex == boxIndex) return command;
        }
        throw new InvalidOperationException($"missing command {kind} box={boxIndex}");
    }

    private static bool HasSourceBoxCommand(ManagedPaintEngine paint, int boxIndex)
    {
        for (int index = 0; index != paint.CommandsEmitted; ++index)
        {
            Check(paint.TryGetCommand(index, out ManagedPaintCommand command), "command-read-source");
            if (command.SourceBoxIndex == boxIndex &&
                command.Kind is ManagedPaintCommandKind.FillRectangle or
                ManagedPaintCommandKind.BorderRectangle or ManagedPaintCommandKind.TextRun or
                ManagedPaintCommandKind.ImagePlaceholder) return true;
        }
        return false;
    }

    private static int IndexOfSource(ManagedPaintEngine paint, int boxIndex)
    {
        for (int index = 0; index != paint.CommandsEmitted; ++index)
        {
            paint.TryGetCommand(index, out ManagedPaintCommand command);
            if (command.SourceBoxIndex == boxIndex && command.Kind == ManagedPaintCommandKind.FillRectangle)
                return index;
        }
        return int.MaxValue;
    }

    private static ManagedLayoutBox GetBox(ManagedLayoutEngine layout, int index)
    {
        Check(layout.TryGetBox(index, out ManagedLayoutBox box), "get-box");
        return box;
    }

    private static int GetBoxIndex(ManagedLayoutEngine layout, ManagedHtmlTreeBuilder builder,
                                   string id)
    {
        Check(layout.TryGetBoxForNode(FindId(builder, id), out int index), "get-box-by-id");
        return index;
    }

    private static ManagedHtmlNodeHandle FindId(ManagedHtmlTreeBuilder builder, string id)
    {
        for (int index = 0; index != builder.Document.NodeCount; ++index)
        {
            ManagedHtmlNodeHandle node = new(index, builder.Document.DocumentNode.Generation);
            if (builder.Document.GetNodeKind(node) != ManagedHtmlNodeKind.Element ||
                !builder.Document.TryFindAttribute(node, ManagedHtmlAttributeName.Id,
                                                   out ManagedHtmlAttributeView attribute)) continue;
            uint[] value = new uint[attribute.ValueLength];
            builder.Document.TryCopyAttributeValue(node, attribute.Index, value, out int length, out _);
            if (ScalarsToString(value.AsSpan(0, length)) == id) return node;
        }
        return ManagedHtmlNodeHandle.Invalid;
    }

    private static ManagedHtmlTreeBuilder Parse(string html, int chunk = 7)
    {
        ManagedHtmlTreeBuilder builder = new();
        ManagedHtmlTokenizer tokenizer = new();
        List<uint> scalars = ToScalars(html);
        for (int offset = 0; offset < scalars.Count;)
        {
            int length = Math.Min(Math.Min(chunk, ManagedHtmlTokenizerLimits.InputWindowCapacity),
                                  scalars.Count - offset);
            uint[] input = new uint[length];
            for (int index = 0; index != length; ++index) input[index] = scalars[offset + index];
            Check(tokenizer.AppendInput(input), "tokenizer-input");
            ManagedHtmlTokenizerProcessResult result = tokenizer.Pump(builder);
            Check(result != ManagedHtmlTokenizerProcessResult.Failed &&
                  result != ManagedHtmlTokenizerProcessResult.Cancelled, "tokenizer-pump");
            offset += length;
        }
        Check(tokenizer.Pump(builder, true) == ManagedHtmlTokenizerProcessResult.Complete &&
              builder.Complete(), "document-complete");
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

    private static string ScalarsToString(ReadOnlySpan<uint> scalars)
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

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
            BasicDataTests();
            TagTests();
            AttributeTests();
            EntityTests();
            CommentTests();
            DoctypeTests();
            RawTextTests();
            UnicodeAndFragmentationTests();
            BackpressureTests();
            CancellationAndResetTests();
            BoundAndMemoryTests();
            HashSummaryTests();
            GzipFixtureDecoderTests();
            Console.WriteLine($"MANAGED_KERNEL_PHASE42_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine($"MANAGED_KERNEL_PHASE42_HOST_TESTS_FAIL cases={s_cases} error={error.Message}");
            return 1;
        }
    }

    private static void BasicDataTests()
    {
        Check(Run("").Succeeded && Run("").Recorder.Kinds.Count == 1 &&
              Run("").Recorder.Kinds[0] == ManagedHtmlTokenKind.EndOfFile,
              "empty-document");
        ParseResult ascii = Run("guideXOS text");
        Check(ascii.Succeeded && ascii.Recorder.TextScalarCount == 13 &&
              ascii.Recorder.TextTokenCount == 1, "plain-ascii");
        ParseResult unicode = Run("Ré sum λη Ж 中 ★ 🙂");
        Check(unicode.Succeeded && unicode.Recorder.TextScalarCount == 17,
              "unicode-text");
        ParseResult longText = Run(new string('x', ManagedHtmlTokenizerLimits.TextTokenCapacity * 4 + 7));
        Check(longText.Succeeded && longText.Recorder.TextTokenCount == 5 &&
              longText.Tokenizer.PeakTextScalars <= ManagedHtmlTokenizerLimits.TextTokenCapacity,
              "bounded-text-windows");
        Check(Run("<div>x</div>").Succeeded, "less-than-boundary");
        Check(Run("ampersand").Succeeded, "ampersand-absent");
        Check(Run("tail").Recorder.TextScalarCount == 4, "eof-flushes-text");
        Check(Run("<div></div>").Recorder.TextScalarCount == 0,
              "no-document-text-accumulation");
    }

    private static void TagTests()
    {
        Check(HasCanonical(Run("<html>").Recorder, "S:html:0:0|E"), "html-start");
        Check(HasCanonical(Run("<div>").Recorder, "S:div:0:0|E"), "div-start");
        Check(HasCanonical(Run("<HTML>").Recorder, "S:html:0:0|E"), "uppercase-tag");
        Check(HasCanonical(Run("<DiV>").Recorder, "S:div:0:0|E"), "mixed-case-tag");
        Check(HasCanonical(Run("<div   >").Recorder, "S:div:0:0|E"), "tag-whitespace");
        Check(HasCanonical(Run("<br/>").Recorder, "S:br:0:1|E"), "self-closing");
        Check(HasCanonical(Run("<br   />").Recorder, "S:br:0:1|E"), "self-closing-whitespace");
        Check(Run("<div>", OneScalar()).Succeeded, "tag-split-after-open");
        Check(Run("<div>", OneScalar()).Recorder.Names[0] == "div",
              "tag-scalar-fragmentation");
        Check(Run("<" + new string('a', ManagedHtmlTokenizerLimits.MaximumTagNameLength) + ">").Succeeded,
              "tag-name-exact-bound");
        Check(Run("<" + new string('a', ManagedHtmlTokenizerLimits.MaximumTagNameLength + 1) + ">").FailureReason ==
              ManagedHtmlTokenizerFailureReason.TagNameTooLong, "tag-name-over-bound");
        Check(HasCanonical(Run("</html>").Recorder, "E:html|E"), "html-end");
        Check(HasCanonical(Run("</DiV>").Recorder, "E:div|E"), "mixed-case-end");
        Check(Run("</html>", OneScalar()).Succeeded, "fragmented-end");
        Check(HasCanonical(Run("</html   >").Recorder, "E:html|E"), "end-whitespace");
        Check(Run("</" + new string('a', ManagedHtmlTokenizerLimits.MaximumTagNameLength + 1) + ">").FailureReason ==
              ManagedHtmlTokenizerFailureReason.TagNameTooLong, "end-name-over-bound");
        Check(Run("<div").FailureReason == ManagedHtmlTokenizerFailureReason.TruncatedMarkup,
              "truncated-start-tag");
        Check(Run("</div").FailureReason == ManagedHtmlTokenizerFailureReason.TruncatedMarkup,
              "truncated-end-tag");
    }

    private static void AttributeTests()
    {
        ParseResult boolean = Run("<input disabled>");
        Check(boolean.Succeeded && boolean.Recorder.AttributeCount == 1 &&
              boolean.Recorder.AttributeHasValue[0] == false, "boolean-attribute");
        Check(Run("<div class=\"\">").Succeeded &&
              Run("<div class=\"\">").Recorder.AttributeValueLengths[0] == 0,
              "empty-double-quoted");
        Check(Run("<div id=\"foo\">").Succeeded, "double-quoted-value");
        Check(Run("<div id='foo'>").Succeeded, "single-quoted-value");
        Check(Run("<div width=100>").Succeeded, "unquoted-value");
        ParseResult many = Run("<a id=x class='y' href=go disabled data-x=test>");
        Check(many.Succeeded && many.Recorder.AttributeCount == 5,
              "multiple-attributes");
        Check(Run("<a  id =  \"x\"  class = y >").Succeeded,
              "attribute-whitespace");
        Check(Run("<input foo=>").Succeeded &&
              Run("<input foo=>").Recorder.AttributeHasValue[0],
              "empty-unquoted-value");
        Check(Run("<div title=\"a>b\">").Succeeded, "greater-than-in-quoted-value");
        Check(Run("<div title=\"a<b\">").Succeeded, "less-than-in-quoted-value");
        Check(HasCanonical(Run("<div id=first ID=second>").Recorder,
                           "S:div:1:0:id=U+66, U+69, U+72, U+73, U+74|E"),
              "duplicate-keeps-first");
        Check(Run("<br class=x/>").Succeeded, "self-closing-after-attribute");
        Check(Run("<div " + new string('a', ManagedHtmlTokenizerLimits.MaximumAttributeNameLength) + ">").Succeeded,
              "attribute-name-exact-bound");
        Check(Run("<div " + new string('a', ManagedHtmlTokenizerLimits.MaximumAttributeNameLength + 1) + ">").FailureReason ==
              ManagedHtmlTokenizerFailureReason.AttributeNameTooLong, "attribute-name-over-bound");
        Check(Run("<div x=" + new string('v', ManagedHtmlTokenizerLimits.MaximumAttributeValueLength) + ">").Succeeded,
              "attribute-value-exact-bound");
        Check(Run("<div x=" + new string('v', ManagedHtmlTokenizerLimits.MaximumAttributeValueLength + 1) + ">").FailureReason ==
              ManagedHtmlTokenizerFailureReason.AttributeValueTooLong, "attribute-value-over-bound");
        StringBuilder attrs = new("<div");
        for (int index = 0; index != ManagedHtmlTokenizerLimits.MaximumAttributesPerTag; ++index)
            attrs.Append(" a").Append(index).Append("=x");
        attrs.Append('>');
        Check(Run(attrs.ToString()).Succeeded, "maximum-attribute-count");
        Check(Run(attrs.ToString().Replace(" a15=x", " a15=x a16=x", StringComparison.Ordinal)).FailureReason ==
              ManagedHtmlTokenizerFailureReason.TooManyAttributes, "attribute-count-over-bound");
        Check(Run("<div data-name=te\"st\">").FailureReason ==
              ManagedHtmlTokenizerFailureReason.InvalidMarkup, "invalid-unquoted-quote");
        Check(Run("<a id=foo class=bar>", OneScalar()).Succeeded, "fragmented-attribute-name");
        Check(Run("<a title=\"fragmented value\">", OneScalar()).Succeeded,
              "fragmented-double-quoted");
        Check(Run("<a title='fragmented value'>", OneScalar()).Succeeded,
              "fragmented-single-quoted");
        Check(Run("<a title=fragmented>", OneScalar()).Succeeded,
              "fragmented-unquoted");
    }

    private static void EntityTests()
    {
        string[] names = { "amp", "lt", "gt", "quot", "apos" };
        for (int index = 0; index != names.Length; ++index)
        {
            ParseResult result = Run("&" + names[index] + ";");
            Check(result.Succeeded && result.Tokenizer.CharacterReferencesDecoded == 1,
                  "named-entity-" + names[index]);
        }
        Check(Run("&#65;").Succeeded && Run("&#65;").Recorder.TextScalarCount == 1,
              "decimal-entity");
        Check(Run("&#x41;").Succeeded, "hex-entity-lower-x");
        Check(Run("&#X41;").Succeeded, "hex-entity-upper-x");
        Check(Run("&#x20AC;").Succeeded, "bmp-entity");
        Check(Run("&#x1F642;").Succeeded, "supplementary-entity");
        Check(Run("&#0;").FailureReason == ManagedHtmlTokenizerFailureReason.InvalidNumericEntity,
              "zero-entity-rejected");
        Check(Run("&#x110000;").FailureReason == ManagedHtmlTokenizerFailureReason.InvalidNumericEntity,
              "out-of-range-entity-rejected");
        Check(Run("&#xD800;").FailureReason == ManagedHtmlTokenizerFailureReason.InvalidNumericEntity,
              "surrogate-entity-rejected");
        Check(HasTextLiteral(Run("&unknown;").Recorder, "&unknown;"),
              "unknown-entity-preserved");
        Check(HasTextLiteral(Run("&amp").Recorder, "&amp"),
              "missing-semicolon-preserved");
        Check(Run("&amp;", OneScalar()).Succeeded, "fragmented-named-entity");
        Check(Run("&#x1F642;", OneScalar()).Succeeded, "fragmented-numeric-entity");
        Check(Run("&" + new string('a', ManagedHtmlTokenizerLimits.MaximumEntityNameLength) + ";").Succeeded,
              "entity-name-exact-bound");
        Check(Run("&" + new string('a', ManagedHtmlTokenizerLimits.MaximumEntityNameLength + 1) + ";").FailureReason ==
              ManagedHtmlTokenizerFailureReason.EntityNameTooLong, "entity-name-over-bound");
        Check(Run("A&amp;<div title=\"&quot;x&quot;\">B</div>").Succeeded,
              "entity-at-text-tag-boundary");
        Check(Run("<div title=\"&amp;\">").Succeeded &&
              Run("<div title=\"&amp;\">").Tokenizer.CharacterReferencesDecoded == 1,
              "entity-double-quoted-attribute");
        Check(Run("<div title='&lt;'>").Succeeded, "entity-single-quoted-attribute");
        Check(Run("<div title=&gt;>").Succeeded, "entity-unquoted-attribute");
    }

    private static void CommentTests()
    {
        ParseResult emptyComment = Run("<!-- -->");
        Check(emptyComment.Succeeded && emptyComment.Recorder.CommentCount == 1,
              "empty-comment:" + emptyComment.Tokenizer.State + ":" + emptyComment.FailureReason + ":" + emptyComment.Recorder.Canonical);
        Check(Run("<!-- simple comment -->").Succeeded, "simple-comment");
        string longComment = "<!--" + new string('c', ManagedHtmlTokenizerLimits.MaximumCommentFragmentLength * 3 + 11) + "-->";
        ParseResult longResult = Run(longComment, OneScalar());
        Check(longResult.Succeeded && longResult.Recorder.CommentCount >= 4,
              "long-fragmented-comment");
        Check(longResult.Recorder.CommentFinalFlags[^1], "comment-final-fragment");
        Check(Run("<!--a--b-->").Succeeded, "comment-internal-double-dash-policy");
        Check(Run("<!-- truncated").FailureReason == ManagedHtmlTokenizerFailureReason.TruncatedComment,
              "truncated-comment");
        Check(Run("<div><!-- c --></div>").Succeeded &&
              Run("<div><!-- c --></div>").Recorder.Kinds.Count == 4,
              "comment-between-tags");
        Check(Run("<!-- c --><span>x</span>").Succeeded,
              "comment-does-not-corrupt-following-state");
    }

    private static void DoctypeTests()
    {
        Check(HasCanonical(Run("<!DOCTYPE html>").Recorder, "D:html|E"), "doctype-html");
        Check(Run("<!doctype html>").Succeeded, "doctype-lowercase");
        Check(Run("<!DoCtYpE Html>").Succeeded, "doctype-mixed-case");
        Check(Run("<!DOCTYPE html>", OneScalar()).Succeeded, "doctype-fragmented");
        Check(Run("<!DOCTYPE   html   >").Succeeded, "doctype-whitespace");
        Check(Run("<!DOCTYPE>").FailureReason == ManagedHtmlTokenizerFailureReason.InvalidMarkup,
              "malformed-doctype");
        Check(Run("<!DOCTYPE " + new string('a', ManagedHtmlTokenizerLimits.MaximumDoctypeNameLength + 1) + ">").FailureReason ==
              ManagedHtmlTokenizerFailureReason.DoctypeTooLong, "doctype-over-bound");
        Check(Run("<![CDATA[x]]>").FailureReason ==
              ManagedHtmlTokenizerFailureReason.UnsupportedMarkupDeclaration,
              "unsupported-markup-declaration");
    }

    private static void RawTextTests()
    {
        Check(Run("<style>a<b{}</style>").Succeeded, "style-basic");
        Check(HasTextLiteral(Run("<style>a<b{}</style>").Recorder, "a<b{}"),
              "style-less-than-is-text");
        Check(Run("<style>a < b { color: red; }</STYLE>").Succeeded,
              "style-uppercase-close");
        Check(Run("<style>a</st\nyle>b</style>").Succeeded,
              "style-false-close-candidate");
        Check(Run("<style>" + new string('x', 600) + "</style>", OneScalar()).Succeeded,
              "style-many-windows");
        Check(Run("<script>if (a < b) x();</script>").Succeeded,
              "script-less-than-is-text");
        Check(Run("<script><div>&amp;</script>").Succeeded,
              "script-markup-and-entity-are-text");
        Check(Run("<script>large " + new string('x', 600) + "</script>", OneScalar()).Succeeded,
              "script-large-text");
        Check(Run("<script></scr\nipt></script>").Succeeded,
              "script-false-partial-close");
        Check(Run("<title>Title &amp; text</title>").Succeeded,
              "title-rcdata");
        Check(Run("<textarea>A &lt; B</textarea>").Succeeded,
              "textarea-rcdata");
        Check(Run("<title>a < b</title>").Succeeded,
              "rcdata-nonmatching-less-than");
        Check(Run("<textarea>text</text\u0061rea>", OneScalar()).Succeeded,
              "rcdata-fragmented-close");
        Check(Run("<style>unterminated").Succeeded,
              "rawtext-eof-flush");
    }

    private static void UnicodeAndFragmentationTests()
    {
        string fixture = "<!DOCTYPE html><div id='x' data-v=&amp;>A &lt; B<!--c--></div>";
        string baseline = Run(fixture).Recorder.Canonical;
        Check(Run(fixture, OneScalar()).Recorder.Canonical == baseline,
              "one-scalar-segmentation");
        for (int split = 0; split <= fixture.Length; ++split)
        {
            Check(Run(fixture, new[] { Math.Max(1, split), Math.Max(1, fixture.Length - split) }).Recorder.Canonical == baseline,
                  "every-two-part-split");
        }
        int[] random = { 1, 7, 2, 13, 3, 5, 11, 4, 17 };
        Check(Run(fixture, random).Recorder.Canonical == baseline,
              "pseudo-random-fragmentation");
        Check(Run("<div>", OneScalar()).Succeeded &&
              Run("<div>", OneScalar()).Recorder.Kinds.Count == 2,
              "split-after-less-than");
        Check(Run("<div class=\"foo\">", OneScalar()).Succeeded,
              "split-around-quote");
        Check(Run("&amp;", OneScalar()).Succeeded, "split-around-ampersand");
        Check(Run("<!-- comment -->", OneScalar()).Succeeded, "split-around-comment-open");
        Check(Run("<!-- comment -->", OneScalar()).Succeeded, "split-around-comment-close");
        Check(Run("<!DOCTYPE html>", OneScalar()).Succeeded, "split-around-doctype-keyword");
        Check(Run("<style>x</style>", OneScalar()).Succeeded,
              "split-around-raw-close");
        ParseResult scalarTags = Run("<h1>Hé🙂</h1><p>text</p>", OneScalar());
        Check(scalarTags.Succeeded && scalarTags.Recorder.TextScalarCount == 7,
              "unicode-tags-and-text");
        Check(Run("<div title='é🙂'>x</div>").Succeeded,
              "unicode-attribute-value");
        Check(Run("&#x1F642;").Recorder.TextScalarCount == 1,
              "supplementary-scalar-not-split");
    }

    private static void BackpressureTests()
    {
        string fixture = "Hello<div id=x>text</div><!-- comment --><!DOCTYPE html>";
        string baseline = Run(fixture).Recorder.Canonical;
        for (int pauseAt = 0; pauseAt != 6; ++pauseAt)
        {
            ParseResult paused = Run(fixture, new[] { 256 }, pauseAt, true);
            Check(paused.Succeeded && paused.Recorder.Canonical == baseline,
                  "pause-on-token-" + pauseAt);
        }
        ParseResult manyPauses = Run(fixture, OneScalar(), -2, true, 8);
        Check(manyPauses.Succeeded && manyPauses.Tokenizer.PauseCount >= 8 &&
              manyPauses.Tokenizer.ResumeCount == manyPauses.Tokenizer.PauseCount,
              "multiple-pauses");
        ParseResult stable = Run("<div>stable</div>", new[] { 256 }, 0, true);
        Check(stable.StablePausedPolls >= 1, "repeated-paused-polls-stable");
        Check(stable.Tokenizer.ScalarsConsumed == stable.Tokenizer.ScalarsReceived,
              "no-missing-scalars-after-resume");
        Check(stable.Recorder.CallbackCount == stable.Tokenizer.TokensEmitted,
              "no-duplicate-token-callbacks");
    }

    private static void CancellationAndResetTests()
    {
        ManagedHtmlTokenizer tokenizer = new();
        RecordingConsumer consumer = new();
        uint[] partial = Scalars("<div class=");
        Check(tokenizer.AppendInput(partial) &&
              tokenizer.Pump(consumer) != ManagedHtmlTokenizerProcessResult.Failed,
              "cancel-data-partial-input");
        int callbacks = consumer.CallbackCount;
        tokenizer.Cancel();
        Check(tokenizer.State == ManagedHtmlTokenizerState.Cancelled &&
              tokenizer.Pump(consumer) == ManagedHtmlTokenizerProcessResult.Cancelled &&
              consumer.CallbackCount == callbacks, "cancel-during-attribute");
        tokenizer.Reset();
        consumer.Reset();
        Check(tokenizer.State == ManagedHtmlTokenizerState.Idle &&
              tokenizer.CurrentAttributeCount == 0 && tokenizer.BufferedTextScalars == 0,
              "reset-clears-partial-state");
        Check(tokenizer.AppendInput(Scalars("<!--comment")) &&
              tokenizer.Pump(consumer) != ManagedHtmlTokenizerProcessResult.Failed,
              "comment-partial-before-reset");
        tokenizer.Reset();
        consumer.Reset();
        Check(tokenizer.AppendInput(Scalars("<div id=x>ok</div>")) &&
              tokenizer.Pump(consumer, true) == ManagedHtmlTokenizerProcessResult.Complete,
              "second-request-after-reset");
        Check(consumer.Complete(), "consumer-completes-after-reset");
        Check(ManagedContentTypeParser.Parse("text/html; charset=utf-8"u8).Classification ==
              ManagedMimeClassification.Html &&
              ManagedContentTypeParser.Parse("text/plain"u8).Classification !=
              ManagedMimeClassification.Html, "mime-gating-classification");
    }

    private static void BoundAndMemoryTests()
    {
        string longDocument = "<div>" + new string('a', 10000) + "</div>";
        ParseResult result = Run(longDocument, new[] { 31, 17, 3, 29, 11 });
        Check(result.Succeeded && result.Recorder.TextScalarCount == 10000,
              "long-document-streams");
        Check(ManagedHtmlTokenizerLimits.InputWindowCapacity == 256 &&
              ManagedHtmlTokenizerLimits.TextTokenCapacity == 128 &&
              ManagedHtmlTokenizerLimits.MaximumAttributesPerTag == 16,
              "fixed-capacity-contract");
        Check(result.Tokenizer.PeakTextScalars <= ManagedHtmlTokenizerLimits.TextTokenCapacity &&
              result.Tokenizer.InputLength == 0,
              "peak-memory-independent-of-document");
        Check(Run("<div>&#xD800;</div>").FailureReason ==
              ManagedHtmlTokenizerFailureReason.InvalidNumericEntity,
              "failure-is-explicit");
        ParseResult partial = Run("<div><span");
        Check(partial.FailureReason == ManagedHtmlTokenizerFailureReason.TruncatedMarkup,
              "eof-partial-nested-markup-policy:" + partial.Tokenizer.State + ":" + partial.FailureReason + ":" + partial.Recorder.Canonical);
    }

    private static void HashSummaryTests()
    {
        string fixture = Phase42Fixture.Html;
        HashResult baseline = RunHash(fixture, null);
        HashResult fragmented = RunHash(fixture, OneScalar());
        Check(baseline.Succeeded && fragmented.Succeeded &&
              baseline.Digest == fragmented.Digest &&
              baseline.Tokens == fragmented.Tokens &&
              baseline.TextTokens == fragmented.TextTokens &&
              baseline.StartTags == fragmented.StartTags &&
              baseline.EndTags == fragmented.EndTags &&
              baseline.Comments == fragmented.Comments &&
              baseline.Doctypes == fragmented.Doctypes &&
              baseline.Attributes == fragmented.Attributes &&
              baseline.TextScalars == fragmented.TextScalars,
              "bounded-token-hash-fragmentation");
        Console.WriteLine($"PHASE42_FIXTURE_BYTES={Encoding.UTF8.GetByteCount(fixture)} " +
            $"TOKENS={baseline.Tokens} TEXT_TOKENS={baseline.TextTokens} " +
            $"START_TAGS={baseline.StartTags} END_TAGS={baseline.EndTags} " +
            $"COMMENTS={baseline.Comments} DOCTYPES={baseline.Doctypes} " +
            $"ATTRIBUTES={baseline.Attributes} TEXT_SCALARS={baseline.TextScalars} " +
            $"ENTITIES={baseline.Entities} HASH={baseline.Digest}");
    }

    private static void GzipFixtureDecoderTests()
    {
        byte[] plain = Encoding.UTF8.GetBytes(Phase42Fixture.Html);
        using MemoryStream compressedStream = new();
        using (GZipStream gzip = new(compressedStream, CompressionLevel.Optimal, true))
            gzip.Write(plain, 0, plain.Length);
        byte[] compressed = compressedStream.ToArray();

        ManagedContentEncodingDecoder decoder = new(
            ManagedHttpContentEncodingState.Gzip);
        Check(decoder.AppendInput(compressed), "gzip-fixture-input");
        DecoderSink sink = new(plain.Length);
        int steps = 0;
        while (!decoder.IsTerminal)
        {
            if (++steps > 100000)
                throw new InvalidOperationException("gzip-fixture-decoder-no-progress");
            ManagedContentDecoderProcessResult result = decoder.Pump(true);
            if (result == ManagedContentDecoderProcessResult.OutputAvailable)
            {
                Check(decoder.ConsumeOutput(sink) == ManagedHttpBodyDeliveryResult.Delivered,
                      "gzip-fixture-output");
                continue;
            }
            if (result == ManagedContentDecoderProcessResult.Complete) break;
            if (result != ManagedContentDecoderProcessResult.NeedInput)
                throw new InvalidOperationException("gzip-fixture-decoder-result-" + result);
        }
        if (decoder.OutputLength != 0)
            Check(decoder.ConsumeOutput(sink) == ManagedHttpBodyDeliveryResult.Delivered,
                  "gzip-fixture-final-output");
        Check(decoder.IsComplete && decoder.FailureReason == ManagedContentDecoderFailureReason.None,
              "gzip-fixture-complete");
        int mismatch = -1;
        int compareLength = Math.Min(sink.Length, plain.Length);
        for (int index = 0; index != compareLength; ++index)
            if (sink.Bytes[index] != plain[index]) { mismatch = index; break; }
        Check(sink.Length == plain.Length && mismatch < 0,
              "gzip-fixture-roundtrip:length=" + sink.Length + ":mismatch=" + mismatch +
              ":produced=" + decoder.DecodedBytesProduced);
    }

    private sealed class DecoderSink : IManagedHttpBodySink
    {
        internal DecoderSink(int capacity) => Bytes = new byte[capacity];
        internal byte[] Bytes { get; }
        internal int Length { get; private set; }

        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            if (segment.Length > Bytes.Length - Length)
                return ManagedHttpBodySinkResult.Fail;
            segment.CopyTo(Bytes.AsSpan(Length));
            Length += segment.Length;
            return ManagedHttpBodySinkResult.Continue;
        }
    }

    private static HashResult RunHash(string input, int[]? chunks)
    {
        ManagedHtmlTokenizer tokenizer = new();
        ManagedHtmlTokenHashConsumer consumer = new();
        uint[] scalars = Scalars(input);
        int offset = 0;
        int patternIndex = 0;
        while (offset != scalars.Length)
        {
            int requested = chunks == null ? scalars.Length - offset :
                chunks[patternIndex++ % chunks.Length];
            int take = Math.Min(requested, Math.Min(tokenizer.InputFreeCapacity,
                                                     scalars.Length - offset));
            if (take == 0 || !tokenizer.AppendInput(scalars.AsSpan(offset, take)))
                throw new InvalidOperationException("hash input did not make progress");
            offset += take;
            while (tokenizer.Pump(consumer) == ManagedHtmlTokenizerProcessResult.Progress) { }
            if (tokenizer.State == ManagedHtmlTokenizerState.Failed)
                return new HashResult(tokenizer, consumer, string.Empty);
        }
        while (tokenizer.Pump(consumer, true) == ManagedHtmlTokenizerProcessResult.Progress) { }
        if (tokenizer.IsComplete && !consumer.Complete())
            throw new InvalidOperationException("hash consumer finalization failed");
        Span<byte> digest = stackalloc byte[ManagedSha256.DigestSize];
        if (!consumer.TryCopyDigest(digest))
            return new HashResult(tokenizer, consumer, string.Empty);
        return new HashResult(tokenizer, consumer, Convert.ToHexString(digest));
    }

    private static ParseResult Run(string input, int[]? chunks = null,
                                   int pauseAt = -1, bool enablePauses = false,
                                   int pauseBudget = 1)
    {
        ManagedHtmlTokenizer tokenizer = new();
        RecordingConsumer consumer = new(enablePauses ? pauseAt : -1,
                                         enablePauses ? pauseBudget : 0);
        uint[] scalars = Scalars(input);
        int offset = 0;
        int patternIndex = 0;
        int stablePausedPolls = 0;
        while (offset != scalars.Length)
        {
            int take = chunks == null ? scalars.Length - offset :
                Math.Min(chunks[patternIndex++ % chunks.Length], scalars.Length - offset);
            if (take > tokenizer.InputFreeCapacity) take = tokenizer.InputFreeCapacity;
            if (take == 0) throw new InvalidOperationException("tokenizer input did not make room state=" +
                tokenizer.State + " input=" + tokenizer.InputLength + " offset=" + offset +
                " length=" + scalars.Length);
            if (!tokenizer.AppendInput(scalars.AsSpan(offset, take)))
                throw new InvalidOperationException("tokenizer rejected bounded input");
            offset += take;
            Pump(tokenizer, consumer, false, ref stablePausedPolls);
            if (tokenizer.IsTerminal) break;
        }
        if (!tokenizer.IsTerminal)
            Pump(tokenizer, consumer, true, ref stablePausedPolls);
        if (tokenizer.IsComplete && !consumer.Complete())
            throw new InvalidOperationException("token consumer finalization failed");
        return new ParseResult(tokenizer, consumer, stablePausedPolls);
    }

    private static void Pump(ManagedHtmlTokenizer tokenizer, RecordingConsumer consumer,
                             bool endOfInput, ref int stablePausedPolls)
    {
        while (true)
        {
            ManagedHtmlTokenizerProcessResult result = tokenizer.Pump(consumer, endOfInput);
            if (result == ManagedHtmlTokenizerProcessResult.Paused)
            {
                ManagedHtmlTokenizerProgressSnapshot after = tokenizer.Progress;
                ManagedHtmlTokenizerProcessResult repeated = tokenizer.Pump(consumer, endOfInput);
                if (repeated != ManagedHtmlTokenizerProcessResult.Paused ||
                    !SameProgress(after, tokenizer.Progress))
                    throw new InvalidOperationException("repeated paused poll was not stable");
                stablePausedPolls++;
                consumer.ReleasePause();
                tokenizer.Resume();
                continue;
            }
            if (result == ManagedHtmlTokenizerProcessResult.Progress) continue;
            if (result == ManagedHtmlTokenizerProcessResult.NeedInput ||
                result == ManagedHtmlTokenizerProcessResult.Complete ||
                result == ManagedHtmlTokenizerProcessResult.Failed ||
                result == ManagedHtmlTokenizerProcessResult.Cancelled) return;
            throw new InvalidOperationException("unknown tokenizer result");
        }
    }

    private static bool SameProgress(ManagedHtmlTokenizerProgressSnapshot left,
                                     ManagedHtmlTokenizerProgressSnapshot right) =>
        left.State == right.State && left.FailureReason == right.FailureReason &&
        left.ScalarsReceived == right.ScalarsReceived &&
        left.ScalarsConsumed == right.ScalarsConsumed &&
        left.TokensEmitted == right.TokensEmitted && left.TextTokens == right.TextTokens &&
        left.StartTagTokens == right.StartTagTokens && left.EndTagTokens == right.EndTagTokens &&
        left.CommentTokens == right.CommentTokens && left.DoctypeTokens == right.DoctypeTokens &&
        left.AttributesEmitted == right.AttributesEmitted &&
        left.CharacterReferencesDecoded == right.CharacterReferencesDecoded &&
        left.BufferedTextScalars == right.BufferedTextScalars &&
        left.CurrentTagNameLength == right.CurrentTagNameLength &&
        left.CurrentAttributeCount == right.CurrentAttributeCount &&
        left.PauseCount == right.PauseCount && left.ResumeCount == right.ResumeCount &&
        left.TokenPending == right.TokenPending && left.PeakTextScalars == right.PeakTextScalars;

    private static uint[] Scalars(string value)
    {
        List<uint> result = new(value.Length);
        for (int index = 0; index != value.Length; ++index)
        {
            char current = value[index];
            if (char.IsHighSurrogate(current) && index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                result.Add((uint)char.ConvertToUtf32(current, value[++index]));
            }
            else result.Add(current);
        }
        return result.ToArray();
    }

    private static int[] OneScalar() => new[] { 1 };

    private static bool HasCanonical(RecordingConsumer recorder, string expected) =>
        recorder.Canonical == expected;

    private static bool HasTextLiteral(RecordingConsumer recorder, string expected)
    {
        string expectedHex = FormatScalars(Scalars(expected));
        for (int index = 0; index != recorder.TextLiterals.Count; ++index)
            if (string.Equals(recorder.TextLiterals[index], expectedHex,
                              StringComparison.Ordinal)) return true;
        return false;
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class ParseResult
    {
        internal ParseResult(ManagedHtmlTokenizer tokenizer, RecordingConsumer recorder,
                             int stablePausedPolls)
        {
            Tokenizer = tokenizer;
            Recorder = recorder;
            StablePausedPolls = stablePausedPolls;
        }
        internal ManagedHtmlTokenizer Tokenizer { get; }
        internal RecordingConsumer Recorder { get; }
        internal int StablePausedPolls { get; }
        internal bool Succeeded => Tokenizer.State == ManagedHtmlTokenizerState.Completed;
        internal ManagedHtmlTokenizerFailureReason FailureReason => Tokenizer.FailureReason;
    }

    private sealed class HashResult
    {
        internal HashResult(ManagedHtmlTokenizer tokenizer,
                            ManagedHtmlTokenHashConsumer consumer, string digest)
        {
            Succeeded = tokenizer.IsComplete;
            Digest = digest;
            Tokens = consumer.TokensProcessed;
            TextTokens = consumer.TextTokenCount;
            StartTags = consumer.StartTagCount;
            EndTags = consumer.EndTagCount;
            Comments = consumer.CommentCount;
            Doctypes = consumer.DoctypeCount;
            Attributes = consumer.Attributes;
            TextScalars = consumer.TextScalars;
            Entities = tokenizer.CharacterReferencesDecoded;
        }
        internal bool Succeeded { get; }
        internal string Digest { get; }
        internal int Tokens { get; }
        internal int TextTokens { get; }
        internal int StartTags { get; }
        internal int EndTags { get; }
        internal int Comments { get; }
        internal int Doctypes { get; }
        internal int Attributes { get; }
        internal int TextScalars { get; }
        internal int Entities { get; }
    }

    private static class Phase42Fixture
    {
        internal static readonly string Html = "<!DOCTYPE html><html><head><title>GuideX &amp; Phase 42</title><style>body{color:red}#home{font-weight:bold}</style><script>if (a < b) { x('&amp;'); }</script></head><body><h1 id='home' class=hero>GuideX OS</h1><p class='intro' data-kind=overview>Bounded HTML streams safely.</p><a href='/guide' title='Read &lt;guide&gt;'>Read the guide</a><img src='/logo.svg' alt='GuideX logo' width=64 height='64'/><form id='contact' action='/send' method=post><input name=email type=email required><button type=submit>Send</button></form><div class='unicode' data-x=one disabled>Ré sum λη Ж 中 ★ 🙂 &lt;ok</div><!-- phase42 comment --><textarea>A &amp; B</textarea><p class=long>" + new string('x', 1200) + "</p></body></html>";
    }

    private sealed class RecordingConsumer : IManagedHtmlTokenConsumer
    {
        private readonly int _pauseAt;
        private int _pauseBudget;
        private readonly List<TokenInfo> _tokens = new();
        private ManagedHtmlTokenConsumerState _state;
        private ManagedHtmlTokenConsumerFailureReason _failureReason;
        private int _callbacks;

        internal RecordingConsumer(int pauseAt = -1, int pauseBudget = 0)
        {
            _pauseAt = pauseAt;
            _pauseBudget = pauseBudget;
            Reset();
        }

        internal IReadOnlyList<ManagedHtmlTokenKind> Kinds
        {
            get
            {
                List<ManagedHtmlTokenKind> result = new(_tokens.Count);
                foreach (TokenInfo token in _tokens) result.Add(token.Kind);
                return result;
            }
        }
        internal List<string> Names { get; } = new();
        internal List<bool> AttributeHasValue { get; } = new();
        internal List<int> AttributeValueLengths { get; } = new();
        internal List<string> TextLiterals { get; } = new();
        internal List<bool> CommentFinalFlags { get; } = new();
        internal int CallbackCount => _callbacks;
        internal int TextTokenCount { get; private set; }
        internal int CommentCount { get; private set; }
        internal int AttributeCount { get; private set; }
        internal int TextScalarCount { get; private set; }
        internal string Canonical
        {
            get
            {
                StringBuilder result = new();
                for (int index = 0; index != _tokens.Count; ++index)
                {
                    if (index != 0) result.Append('|');
                    result.Append(_tokens[index].Canonical);
                }
                return result.ToString();
            }
        }

        public ManagedHtmlTokenConsumerState State => _state;
        public ManagedHtmlTokenConsumerFailureReason FailureReason => _failureReason;
        public int TokensProcessed => _tokens.Count;

        public ManagedHttpBodySinkResult Consume(in ManagedHtmlToken token)
        {
            if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
                _state == ManagedHtmlTokenConsumerState.Failed ||
                _state == ManagedHtmlTokenConsumerState.Completed)
                return ManagedHttpBodySinkResult.Fail;
            if (_pauseBudget != 0 && (_pauseAt == -2 || _callbacks == _pauseAt))
                return ManagedHttpBodySinkResult.Pause;
            ++_callbacks;
            TokenInfo info = TokenInfo.Read(token);
            _tokens.Add(info);
            if (info.Kind == ManagedHtmlTokenKind.Text)
            {
                ++TextTokenCount;
                TextScalarCount += info.TextLength;
                TextLiterals.Add(info.TextHex);
            }
            if (info.Kind == ManagedHtmlTokenKind.Comment)
            {
                ++CommentCount;
                CommentFinalFlags.Add(info.CommentFinal);
            }
            if (info.Kind == ManagedHtmlTokenKind.StartTag)
            {
                AttributeCount += info.AttributeCount;
                Names.Add(info.Name);
                for (int index = 0; index != info.AttributeCount; ++index)
                {
                    AttributeHasValue.Add(info.AttributeHasValues[index]);
                    AttributeValueLengths.Add(info.AttributeValueLengths[index]);
                }
            }
            _state = ManagedHtmlTokenConsumerState.Receiving;
            return ManagedHttpBodySinkResult.Continue;
        }

        internal void ReleasePause()
        {
            if (_pauseBudget != 0) --_pauseBudget;
        }

        public bool Complete()
        {
            if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
                _state == ManagedHtmlTokenConsumerState.Failed) return false;
            _state = ManagedHtmlTokenConsumerState.Completed;
            return true;
        }

        public void Cancel() => _state = ManagedHtmlTokenConsumerState.Cancelled;

        public void Reset()
        {
            _tokens.Clear();
            Names.Clear();
            AttributeHasValue.Clear();
            AttributeValueLengths.Clear();
            TextLiterals.Clear();
            CommentFinalFlags.Clear();
            TextTokenCount = 0;
            CommentCount = 0;
            AttributeCount = 0;
            TextScalarCount = 0;
            _callbacks = 0;
            _failureReason = ManagedHtmlTokenConsumerFailureReason.None;
            _state = ManagedHtmlTokenConsumerState.Idle;
        }
    }

    private sealed class TokenInfo
    {
        internal ManagedHtmlTokenKind Kind;
        internal string Name = string.Empty;
        internal int AttributeCount;
        internal bool SelfClosing;
        internal int TextLength;
        internal string TextHex = string.Empty;
        internal int CommentLength;
        internal bool CommentFinal;
        internal string DoctypeName = string.Empty;
        internal bool[] AttributeHasValues = Array.Empty<bool>();
        internal int[] AttributeValueLengths = Array.Empty<int>();
        internal string[] AttributeNames = Array.Empty<string>();
        internal string[] AttributeValues = Array.Empty<string>();

        internal static TokenInfo Read(in ManagedHtmlToken token)
        {
            TokenInfo result = new() { Kind = token.Kind, SelfClosing = token.IsSelfClosing };
            Span<byte> name = stackalloc byte[ManagedHtmlTokenizerLimits.MaximumTagNameLength];
            Span<uint> values = stackalloc uint[ManagedHtmlTokenizerLimits.MaximumAttributeValueLength];
            Span<uint> text = stackalloc uint[ManagedHtmlTokenizerLimits.TextTokenCapacity];
            Span<uint> comment = stackalloc uint[ManagedHtmlTokenizerLimits.MaximumCommentFragmentLength];
            switch (token.Kind)
            {
                case ManagedHtmlTokenKind.Text:
                    token.TryCopyText(text, out int textLength);
                    result.TextLength = textLength;
                    result.TextHex = FormatScalars(text[..textLength]);
                    break;
                case ManagedHtmlTokenKind.StartTag:
                case ManagedHtmlTokenKind.EndTag:
                    token.TryCopyTagName(name, out int nameLength);
                    result.Name = Encoding.ASCII.GetString(name[..nameLength]);
                    result.AttributeCount = token.AttributeCount;
                    result.AttributeHasValues = new bool[result.AttributeCount];
                    result.AttributeValueLengths = new int[result.AttributeCount];
                    result.AttributeNames = new string[result.AttributeCount];
                    result.AttributeValues = new string[result.AttributeCount];
                    for (int index = 0; index != result.AttributeCount; ++index)
                    {
                        token.TryCopyAttributeName(index, name, out int attributeNameLength);
                        token.TryCopyAttributeValue(index, values, out int valueLength,
                                                    out bool hasValue);
                        result.AttributeNames[index] = Encoding.ASCII.GetString(
                            name[..attributeNameLength]);
                        result.AttributeHasValues[index] = hasValue;
                        result.AttributeValueLengths[index] = valueLength;
                        result.AttributeValues[index] = FormatScalars(values[..valueLength]);
                    }
                    break;
                case ManagedHtmlTokenKind.Comment:
                    token.TryCopyComment(comment, out int commentLength);
                    result.CommentLength = commentLength;
                    result.CommentFinal = token.IsCommentFinalFragment;
                    break;
                case ManagedHtmlTokenKind.Doctype:
                    token.TryCopyDoctypeName(name, out int doctypeLength);
                    result.DoctypeName = Encoding.ASCII.GetString(name[..doctypeLength]);
                    break;
            }
            return result;
        }

        internal string Canonical
        {
            get
            {
                return Kind switch
                {
                    ManagedHtmlTokenKind.Text => "T:" + TextHex,
                    ManagedHtmlTokenKind.StartTag => "S:" + Name + ":" + AttributeCount + ":" +
                        (SelfClosing ? "1" : "0") + FormatAttributeSuffix(AttributeCount),
                    ManagedHtmlTokenKind.EndTag => "E:" + Name,
                    ManagedHtmlTokenKind.Comment => "C:" + CommentLength + ":" +
                        (CommentFinal ? "1" : "0"),
                    ManagedHtmlTokenKind.Doctype => "D:" + DoctypeName,
                    _ => "E"
                };
            }
        }

        private string FormatAttributeSuffix(int count)
        {
            if (count == 0) return string.Empty;
            StringBuilder result = new();
            for (int index = 0; index != count; ++index)
            {
                result.Append(':').Append(AttributeNames[index]);
                if (AttributeHasValues[index])
                    result.Append('=').Append(AttributeValues[index]);
                else
                    result.Append("#boolean");
            }
            return result.ToString();
        }
    }

    private static string FormatScalars(ReadOnlySpan<uint> values)
    {
        StringBuilder result = new();
        for (int index = 0; index != values.Length; ++index)
        {
            if (index != 0) result.Append(", ");
            result.Append("U+").Append(values[index].ToString("X"));
        }
        return result.ToString();
    }
}

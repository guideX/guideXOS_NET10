using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase41HostTests;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestMimeAndContentTypeParser();
            TestFragmentedContentTypeParser();
            TestAsciiAndUtf8Boundaries();
            TestUtf8ExhaustiveClasses();
            TestMalformedUtf8();
            TestBom();
            TestAsciiAndLatin1();
            TestOutputWindowsAndConsumers();
            TestPauseResumeAndReset();
            TestCompressedTextPipeline();
            TestTextResourceIntegration();
            Console.WriteLine($"MANAGED_KERNEL_PHASE41_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE41_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestMimeAndContentTypeParser()
    {
        Check(Parse(null).Classification == ManagedMimeClassification.Unknown, "mime-missing");
        Check(Parse("text/plain").Classification == ManagedMimeClassification.TextPlain, "mime-plain");
        Check(Parse("TEXT/PLAIN").Classification == ManagedMimeClassification.TextPlain, "mime-case");
        Check(Parse("text/html").Classification == ManagedMimeClassification.Html, "mime-html");
        Check(Parse("text/css").Classification == ManagedMimeClassification.Css, "mime-css");
        Check(Parse("application/json").Classification == ManagedMimeClassification.Json, "mime-json");
        Check(Parse("application/javascript").Classification == ManagedMimeClassification.JavaScript, "mime-application-js");
        Check(Parse("text/javascript").Classification == ManagedMimeClassification.JavaScript, "mime-text-js");
        Check(Parse("application/xml").Classification == ManagedMimeClassification.Xml, "mime-application-xml");
        Check(Parse("text/xml").Classification == ManagedMimeClassification.Xml, "mime-text-xml");
        Check(Parse("application/xhtml+xml").Classification == ManagedMimeClassification.Xml, "mime-xhtml");
        Check(Parse("application/octet-stream").Classification == ManagedMimeClassification.Binary, "mime-octet");
        Check(Parse("image/png").Classification == ManagedMimeClassification.Binary, "mime-png");
        Check(Parse("image/jpeg").Classification == ManagedMimeClassification.Binary, "mime-jpeg");
        Check(Parse("text/x-custom").Classification == ManagedMimeClassification.Textual, "mime-custom-text");
        Check(Parse("application/x-custom").Classification == ManagedMimeClassification.Unknown, "mime-custom-app");
        Check(Parse(" text/html ; foo=bar ; charset = \"UTF-8\" ").Classification == ManagedMimeClassification.Html &&
              Parse(" text/html ; foo=bar ; charset = \"UTF-8\" ").Charset == ManagedTextCharset.Utf8, "mime-parameters");
        Check(Parse("text/plain; charset=utf-8; format=flowed").CharsetState == ManagedCharsetDeclarationState.Utf8, "charset-after");
        Check(Parse("text/plain; charset=us-ascii").Charset == ManagedTextCharset.UsAscii, "charset-ascii");
        Check(Parse("text/plain; charset=iso-8859-1").Charset == ManagedTextCharset.Iso88591, "charset-latin1");
        Check(Parse("text/plain; charset=utf-8; charset=UTF-8").CharsetState == ManagedCharsetDeclarationState.Utf8, "charset-duplicate-same");
        Check(Parse("text/plain; charset=utf-8; charset=us-ascii").IsMalformed, "charset-duplicate-conflict");
        Check(Parse("text/plain; charset=").CharsetState == ManagedCharsetDeclarationState.Empty, "charset-empty");
        Check(Parse("text/plain; charset=\"utf-8").IsMalformed, "charset-unclosed-quote");
        Check(Parse("text/plain; charset=shift_jis").CharsetState == ManagedCharsetDeclarationState.Unsupported, "charset-unsupported");
        Check(Parse("text/plain; charset=windows-1252").CharsetState == ManagedCharsetDeclarationState.Unsupported, "charset-windows-unsupported");
        Check(Parse("text/plain; charset=latin-1").Charset == ManagedTextCharset.Iso88591, "charset-latin1-alias");
        Check(Parse("text/plain; charset=utf-8; bad").IsMalformed, "charset-malformed-parameter");
        Check(Parse("text/plain; charset=\"utf-8\"x").IsMalformed, "charset-quoted-tail");
        Check(Parse("text/plain; charset=\"utf 8\"").IsMalformed, "charset-quoted-inner-space");
        Check(Parse("text/plain; charset=" + new string('x', ManagedContentTypeParser.MaximumCharsetLength + 1)).CharsetState == ManagedCharsetDeclarationState.TooLong, "charset-too-long");
        Check(Parse("text/plain; CHARSET = UTF-8").Charset == ManagedTextCharset.Utf8, "charset-name-case");
        Check(Parse("text/html; a=1; charset=utf-8; b=2").Charset == ManagedTextCharset.Utf8, "charset-unrelated");
        Check(Parse("text/html; charset=utf-8; charset=utf-8").Charset == ManagedTextCharset.Utf8, "charset-repeat");
        Check(Parse("text/html/").IsMalformed, "mime-malformed-slash");
        Check(Parse("text html").IsMalformed, "mime-malformed-space");
        Check(Parse("text/html; broken").IsMalformed, "mime-malformed-parameter");
        Check(Parse("text/html;").IsMalformed, "mime-trailing-semicolon");
        Check(Parse("text/html; charset=\"").IsMalformed, "mime-malformed-quoted");

        ManagedHttpResponseParser parser = new(ManagedHttpLimits.MaximumStreamedBodyLength, false, true);
        byte[] response = Ascii("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nContent-Type: TEXT/HTML; charset=\"utf-8\"\r\nConnection: close\r\n\r\n");
        FeedParser(parser, response, 1);
        Check(parser.ContentTypeState == ManagedHttpContentTypeState.Available, "content-type-available");
        Span<byte> copy = stackalloc byte[ManagedHttpLimits.MaximumContentTypeLength];
        Check(parser.TryCopyContentType(copy, out int length) && length == "TEXT/HTML; charset=\"utf-8\""u8.Length, "content-type-copy");
        ManagedHttpResponseParser tooLong = new(ManagedHttpLimits.MaximumStreamedBodyLength, false, true);
        FeedParser(tooLong, Ascii("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nContent-Type: " + new string('x', ManagedHttpLimits.MaximumContentTypeLength + 1) + "\r\nConnection: close\r\n\r\n"), 2);
        Check(tooLong.ContentTypeState == ManagedHttpContentTypeState.TooLong, "content-type-too-long-preserved");
    }

    private static void TestFragmentedContentTypeParser()
    {
        byte[] response = Ascii("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nContent-Type: text/plain; format=flowed; charset = 'utf-8'\r\nConnection: close\r\n\r\n");
        ManagedHttpResponseParser parser = new(ManagedHttpLimits.MaximumStreamedBodyLength, false, true);
        FeedParser(parser, response, 1);
        Check(parser.IsBodyComplete && parser.ContentTypeState == ManagedHttpContentTypeState.Available, "fragmented-content-type-complete");
        Span<byte> raw = stackalloc byte[64];
        Check(parser.TryCopyContentType(raw, out int length), "fragmented-content-type-copy");
        ManagedContentTypeMetadata metadata = ManagedContentTypeParser.Parse(raw[..length]);
        Check(metadata.IsMalformed, "single-quote-rejected");
        response = Ascii("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nContent-Type: text/plain; charset = utf-8\r\nConnection: close\r\n\r\n");
        parser.Reset(); FeedParser(parser, response, 1);
        Check(ManagedContentTypeParser.Parse(raw[..(parser.TryCopyContentType(raw, out length) ? length : 0)]).Charset == ManagedTextCharset.Utf8, "fragmented-content-type-charset");
    }

    private static void TestAsciiAndUtf8Boundaries()
    {
        byte[] ascii = new byte[128];
        for (int index = 0; index != ascii.Length; ++index) ascii[index] = (byte)index;
        List<uint> values = Decode(ascii, ManagedTextCharset.Utf8, 1);
        Check(values.Count == 128 && values[0] == 0 && values[^1] == 0x7F, "utf8-all-ascii");
        Check(Decode(Ascii("hello, guideXOS"), ManagedTextCharset.Utf8, 2).SequenceEqual("hello, guideXOS".Select(c => (uint)c)), "utf8-ascii-sentence");
        Check(Decode(new byte[] { 0xC2, 0x80 }, ManagedTextCharset.Utf8, 1).Single() == 0x80, "utf8-two-min");
        Check(Decode(new byte[] { 0xDF, 0xBF }, ManagedTextCharset.Utf8, 1).Single() == 0x7FF, "utf8-two-max");
        Check(Decode(new byte[] { 0xE0, 0xA0, 0x80 }, ManagedTextCharset.Utf8, 1).Single() == 0x800, "utf8-three-min");
        Check(Decode(new byte[] { 0xEF, 0xBF, 0xBF }, ManagedTextCharset.Utf8, 1).Single() == 0xFFFF, "utf8-three-max");
        Check(Decode(new byte[] { 0xF0, 0x90, 0x80, 0x80 }, ManagedTextCharset.Utf8, 1).Single() == 0x10000, "utf8-four-min");
        Check(Decode(new byte[] { 0xF4, 0x8F, 0xBF, 0xBF }, ManagedTextCharset.Utf8, 1).Single() == 0x10FFFF, "utf8-four-max");
        uint[] expected = { 0x24, 0xA2, 0x20AC, 0x1F642 };
        byte[] mixed = Encoding.UTF8.GetBytes("$¢€🙂");
        Check(Decode(mixed, ManagedTextCharset.Utf8, 1).SequenceEqual(expected), "utf8-mixed-byte-fragments");
        Check(Decode(mixed, ManagedTextCharset.Utf8, 2).SequenceEqual(expected), "utf8-mixed-fragments-2");
        Check(Decode(mixed, ManagedTextCharset.Utf8, mixed.Length).SequenceEqual(expected), "utf8-mixed-whole");
    }

    private static void TestUtf8ExhaustiveClasses()
    {
        for (int scalar = 0; scalar <= 0x7F; ++scalar)
            Check(Decode(new[] { (byte)scalar }, ManagedTextCharset.Utf8, 1).Single() == (uint)scalar, "utf8-single");
        for (int scalar = 0x80; scalar <= 0x7FF; scalar += 17)
            Check(Decode(EncodeScalar(scalar), ManagedTextCharset.Utf8, 1).Single() == (uint)scalar, "utf8-two-generated");
        int[] three = { 0x800, 0x801, 0x7FF, 0x800, 0x9999, 0xD7FF, 0xE000, 0xFFFF };
        foreach (int scalar in three)
            Check(Decode(EncodeScalar(scalar), ManagedTextCharset.Utf8, 1).Single() == (uint)scalar, "utf8-three-boundary");
        for (int scalar = 0x10000; scalar <= 0x10FFFF; scalar += 4099)
            Check(Decode(EncodeScalar(scalar), ManagedTextCharset.Utf8, 1).Single() == (uint)scalar, "utf8-four-generated");
        Check(Decode(EncodeScalar(0xFDD0), ManagedTextCharset.Utf8, 1).Single() == 0xFDD0, "utf8-noncharacter-preserved");
    }

    private static void TestMalformedUtf8()
    {
        byte[][] invalid =
        {
            new byte[] { 0x80 }, new byte[] { 0xC0, 0x80 }, new byte[] { 0xC1, 0x80 },
            new byte[] { 0xE0, 0x80, 0x80 }, new byte[] { 0xED, 0xA0, 0x80 },
            new byte[] { 0xF0, 0x80, 0x80, 0x80 }, new byte[] { 0xF4, 0x90, 0x80, 0x80 },
            new byte[] { 0xF5 }, new byte[] { 0xFF }, new byte[] { 0xC2, 0x20 },
            new byte[] { 0xE2, 0x82, 0x20 }, new byte[] { 0xF0, 0x9F, 0x92, 0x20 }
        };
        foreach (byte[] value in invalid) Check(Fails(value, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.InvalidUtf8), "utf8-invalid");
        Check(Fails(new byte[] { 0xC2 }, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.TruncatedUtf8), "utf8-truncated-2");
        Check(Fails(new byte[] { 0xE2, 0x82 }, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.TruncatedUtf8), "utf8-truncated-3");
        Check(Fails(new byte[] { 0xF0, 0x9F, 0x92 }, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.TruncatedUtf8), "utf8-truncated-4");
        Check(Fails(Ascii("ok") .Concat(new byte[] { 0x80 }).ToArray(), ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.InvalidUtf8), "utf8-valid-prefix-invalid");
        Check(Fails(new byte[] { 0xE2, 0x82, 0xAC, 0xC0 }, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.InvalidUtf8), "utf8-invalid-after-valid");
        Check(Fails(new byte[] { 0x80 }, ManagedTextCharset.UsAscii, 1, ManagedTextDecoderFailureReason.InvalidAscii), "ascii-high-byte");
        ManagedTextDecoder decoder = new(ManagedTextCharset.Utf8);
        RecordingConsumer consumer = new();
        Check(decoder.AppendInput(new byte[] { 0xE2, 0x82, 0xAC }), "failure-counter-append");
        Check(decoder.Pump(consumer, true) == ManagedTextDecoderProcessResult.Complete && decoder.BytesConsumed == 3 && decoder.ScalarsProduced == 1, "utf8-counters-exact");
    }

    private static void TestBom()
    {
        byte[] bomText = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'A', 0xE2, 0x82, 0xAC };
        for (int fragment = 1; fragment <= 3; ++fragment)
        {
            List<uint> result = Decode(bomText, ManagedTextCharset.Utf8, fragment, out ManagedTextDecoder decoder);
            Check(result.SequenceEqual(new uint[] { 'A', 0x20AC }) && decoder.BomConsumed, "bom-fragmented");
        }
        Check(Decode(new byte[] { 0xEF, 0xBB, 0xBF }, ManagedTextCharset.Utf8, 1).Count == 0, "bom-only");
        Check(Decode(Encoding.UTF8.GetBytes("A\uFEFFB"), ManagedTextCharset.Utf8, 1).SequenceEqual(new uint[] { 'A', 0xFEFF, 'B' }), "bom-later-retained");
        Check(Fails(new byte[] { 0xEF }, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.TruncatedUtf8), "bom-partial-one");
        Check(Fails(new byte[] { 0xEF, 0xBB }, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.TruncatedUtf8), "bom-partial-two");
        Check(Fails(new byte[] { 0xEF, (byte)'A', (byte)'B' }, ManagedTextCharset.Utf8, 1, ManagedTextDecoderFailureReason.InvalidUtf8), "bom-malformed");
        Check(Fails(new byte[] { 0xEF, 0xBB, 0xBF }, ManagedTextCharset.UsAscii, 1, ManagedTextDecoderFailureReason.InvalidAscii), "ascii-does-not-strip-bom");
    }

    private static void TestAsciiAndLatin1()
    {
        byte[] ascii = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
        Check(Decode(ascii, ManagedTextCharset.UsAscii, 7).Count == 128, "ascii-explicit-valid");
        Check(Fails(new byte[] { 0x80 }, ManagedTextCharset.UsAscii, 1, ManagedTextDecoderFailureReason.InvalidAscii), "ascii-reject-80");
        Check(Fails(new byte[] { 0xFF }, ManagedTextCharset.UsAscii, 1, ManagedTextDecoderFailureReason.InvalidAscii), "ascii-reject-ff");
        byte[] all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        List<uint> latin = Decode(all, ManagedTextCharset.Iso88591, 3);
        Check(latin.Count == 256 && latin[0] == 0 && latin[^1] == 0xFF && latin[0xA0] == 0xA0, "latin1-all-bytes");
    }

    private static void TestOutputWindowsAndConsumers()
    {
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abc🙂", 200)));
        List<uint> values = Decode(input, ManagedTextCharset.Utf8, 11, out ManagedTextDecoder decoder);
        List<uint> expected = string.Concat(Enumerable.Repeat("abc🙂", 200)).Select(c => (uint)c).ToList();
        // The expected list above is UTF-16 units; compare against scalar oracle separately.
        expected.Clear(); for (int i = 0; i != 200; ++i) { expected.Add('a'); expected.Add('b'); expected.Add('c'); expected.Add(0x1F642); }
        Check(values.SequenceEqual(expected) && decoder.ScalarsProduced == 800 && decoder.TextWindowCapacityForTest(), "text-order-and-windows");
        ManagedTextPrefixConsumer prefix = new(3);
        ManagedTextDestinationConsumer destination = new(new uint[800]);
        FeedConsumer(values, prefix, 17);
        FeedConsumer(values, destination, 256);
        Span<uint> copy = stackalloc uint[3];
        Check(prefix.TryCopyPrefix(copy, out int prefixLength) && prefixLength == 3 && copy.SequenceEqual(new uint[] { 'a', 'b', 'c' }), "text-prefix");
        Check(destination.UnitsWritten == values.Count && destination.State == ManagedResourceConsumerState.Receiving, "text-destination-exact-fit");
        ManagedTextDestinationConsumer tooSmall = new(new uint[values.Count - 1]);
        Check(tooSmall.Consume(values.ToArray()) == ManagedHttpBodySinkResult.Fail && tooSmall.FailureReason == ManagedTextConsumerFailureReason.DestinationFull && tooSmall.UnitsWritten == 0, "text-destination-full");
        ManagedTextCountConsumer lines = new(true);
        List<uint> lineValues = Decode(Ascii("a\r\nb\nc\rd"), ManagedTextCharset.Utf8, 1);
        FeedConsumer(lineValues, lines, 1);
        Check(lines.Count == 8 && lines.LineCount == 3, "text-line-count");
    }

    private static void TestPauseResumeAndReset()
    {
        byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("x🙂y", 100)));
        ManagedTextDecoder decoder = new(ManagedTextCharset.Utf8);
        PausingConsumer consumer = new();
        int offset = 0;
        while (offset != input.Length)
        {
            int take = Math.Min(decoder.InputFreeCapacity, Math.Min(5, input.Length - offset));
            if (take == 0) Drain(decoder, consumer, false);
            else { Check(decoder.AppendInput(input.AsSpan(offset, take)), "pause-append"); offset += take; Drain(decoder, consumer, false); }
            if (consumer.Paused)
            {
                int beforeBytes = decoder.BytesConsumed, beforeScalars = decoder.ScalarsDelivered;
                Check(decoder.Pump(consumer) == ManagedTextDecoderProcessResult.Paused && decoder.BytesConsumed == beforeBytes && decoder.ScalarsDelivered == beforeScalars, "pause-stable");
                consumer.ResumeAcceptance(); decoder.Resume();
            }
        }
        while (decoder.Pump(consumer, true) != ManagedTextDecoderProcessResult.Complete)
        {
            if (decoder.State == ManagedTextDecoderState.Failed) throw new InvalidOperationException("pause final failed");
            if (decoder.OutputLength == 0 && decoder.InputLength == 0) break;
        }
        Check(decoder.ScalarsProduced == decoder.ScalarsDelivered && decoder.PauseCount != 0 && decoder.ResumeCount != 0, "pause-resume-exact");
        decoder.Reset(); Check(decoder.State == ManagedTextDecoderState.Idle && decoder.InputLength == 0 && decoder.OutputLength == 0 && decoder.BytesConsumed == 0, "decoder-reset");
        RecordingConsumer second = new(); Check(decoder.AppendInput(Ascii("reset-ok")) && decoder.Pump(second, true) == ManagedTextDecoderProcessResult.Complete && second.Values.SequenceEqual(Ascii("reset-ok").Select(b => (uint)b)), "decoder-second-request");
    }

    private static void TestCompressedTextPipeline()
    {
        string text = string.Concat(Enumerable.Repeat("ASCII é Ελληνικά Ж 中 🙂\r\n", 200));
        byte[] plain = Encoding.UTF8.GetBytes(text);
        foreach (ManagedHttpContentEncodingState encoding in new[] { ManagedHttpContentEncodingState.Gzip, ManagedHttpContentEncodingState.Deflate })
        {
            byte[] compressed = encoding == ManagedHttpContentEncodingState.Gzip ? CompressGzip(plain) : CompressZlib(plain);
            ManagedContentEncodingDecoder content = new(encoding);
            ManagedTextDecoder decoder = new(ManagedTextCharset.Utf8);
            RecordingConsumer consumer = new();
            TextSink sink = new(decoder, consumer);
            for (int offset = 0; offset != compressed.Length;)
            {
                int take = Math.Min(content.InputFreeCapacity, Math.Min(13, compressed.Length - offset));
                if (take == 0) PumpContent(content, sink, false);
                else { Check(content.AppendInput(compressed.AsSpan(offset, take)), "compressed-text-append"); offset += take; PumpContent(content, sink, false); }
            }
            while (!content.IsComplete)
            {
                ManagedContentDecoderProcessResult result = content.Pump(true);
                if (result == ManagedContentDecoderProcessResult.OutputAvailable) Check(content.ConsumeOutput(sink) == ManagedHttpBodyDeliveryResult.Delivered, "compressed-text-drain");
                else if (result == ManagedContentDecoderProcessResult.Complete) break;
                else if (result == ManagedContentDecoderProcessResult.Failed) throw new InvalidOperationException("compressed content failure");
            }
            Check(decoder.Pump(consumer, true) == ManagedTextDecoderProcessResult.Complete, "compressed-text-final");
            List<uint> expected = new();
            for (int index = 0; index < text.Length; ++index)
            {
                char ch = text[index];
                if (char.IsHighSurrogate(ch) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    expected.Add((uint)char.ConvertToUtf32(ch, text[++index]));
                }
                else expected.Add(ch);
            }
            Check(consumer.Values.Count != 0, "compressed-text-nonempty");
            Check(consumer.Values.SequenceEqual(expected), "compressed-text-order");
            Check(content.DecodedBytesProduced == plain.Length && decoder.BytesConsumed == plain.Length && decoder.ScalarsProduced == expected.Count, "compressed-text-telemetry");
        }
    }

    private static void TestTextResourceIntegration()
    {
        byte[] plain = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("resource é Ελληνικά 中 🙂\r\n", 120)));
        byte[] identityResponse = Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: " + plain.Length +
            "\r\nContent-Type: text/plain; charset=utf-8\r\nConnection: close\r\n\r\n"), plain);
        RunTextResource(identityResponse, plain, false, 7);
        byte[] compressed = CompressGzip(plain);
        byte[] gzipResponse = Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: " + compressed.Length +
            "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Encoding: gzip\r\nConnection: close\r\n\r\n"), compressed);
        RunTextResource(gzipResponse, plain, true, 11);

        byte[] badResponse = Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: 1\r\nContent-Type: text/plain; charset=utf-8\r\nConnection: close\r\n\r\n"), new byte[] { 0x80 });
        Check(RunTextResourceFailure(badResponse, ManagedTextFailureReason.InvalidUtf8, 1), "resource-invalid-utf8");
        byte[] charsetResponse = Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: 1\r\nContent-Type: text/plain; charset=shift_jis\r\nConnection: close\r\n\r\n"), new byte[] { 0x41 });
        Check(RunTextResourceFailure(charsetResponse, ManagedTextFailureReason.UnsupportedCharset, 2), "resource-unsupported-charset");
        byte[] binaryResponse = Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: 1\r\nContent-Type: image/png\r\nConnection: close\r\n\r\n"), new byte[] { 0x41 });
        Check(RunTextResourceFailure(binaryResponse, ManagedTextFailureReason.UnsupportedMime, 2), "resource-binary-gated");
        Check(RunTextResourceFailure(binaryResponse, ManagedTextFailureReason.None, 2, true), "resource-binary-override");
    }

    private static void RunTextResource(byte[] response, byte[] expected, bool compressed, int fragment)
    {
        HttpFixtureBackend backend = new(response, fragment);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        ManagedTextResourceRequest request = new(service);
        ManagedTextCountConsumer count = new(true);
        ManagedTextPrefixConsumer prefix = new(16);
        ManagedTextCompositeConsumer composite = new(count, prefix);
        Check(request.BeginGet("phase41.test"u8, compressed ? "/gzip"u8 : "/identity"u8, composite) == NetworkOperationResult.Started, "text-resource-begin");
        bool paused = false;
        for (int poll = 0; poll != 100_000 && request.State != ManagedResourceState.Completed && request.State != ManagedResourceState.Failed; ++poll)
        {
            Check(request.Poll() != NetworkOperationResult.Failed, "text-resource-poll");
            if (!paused && request.Progress.ScalarsDelivered != 0)
            {
                Check(request.Pause() == NetworkOperationResult.Success, "text-resource-pause");
                ManagedTextProgressSnapshot stable = request.Progress;
                for (int i = 0; i != 3; ++i)
                {
                    Check(request.Poll() == NetworkOperationResult.Success, "text-resource-stable-poll");
                    ManagedTextProgressSnapshot current = request.Progress;
                    Check(current.EncodedHttpBytesReceived == stable.EncodedHttpBytesReceived &&
                          current.DecompressedResourceBytesProduced == stable.DecompressedResourceBytesProduced &&
                          current.TextInputBytesConsumed == stable.TextInputBytesConsumed &&
                          current.ScalarsDelivered == stable.ScalarsDelivered &&
                          current.BufferedDecodedTextCount == stable.BufferedDecodedTextCount, "text-resource-stable");
                }
                Check(request.Resume() == NetworkOperationResult.Success, "text-resource-resume");
                paused = true;
            }
        }
        Check(request.State == ManagedResourceState.Completed, "text-resource-complete");
        int scalarCount = CountScalars(expected);
        Check(count.Count == scalarCount && count.LineCount == 120 && request.Progress.ScalarsProduced == scalarCount &&
              request.Progress.ScalarsDelivered == scalarCount && request.Progress.EncodedHttpBytesReceived == response.Length - IndexOfBody(response) &&
              request.Progress.DecompressedResourceBytesProduced == expected.Length && request.Progress.Charset == ManagedTextCharset.Utf8 &&
              request.Progress.CharsetSource == ManagedTextCharsetSource.Explicit && request.Progress.MimeClassification == ManagedMimeClassification.TextPlain &&
              request.Progress.ContentEncodingState == (compressed ? ManagedHttpContentEncodingState.Gzip : ManagedHttpContentEncodingState.Missing) &&
              request.Progress.PauseCount == 1 && request.Progress.ResumeCount == 1 && paused, "text-resource-progress");
        Span<uint> captured = stackalloc uint[16];
        Check(prefix.TryCopyPrefix(captured, out int prefixLength) && prefixLength == 16, "text-resource-prefix");
    }

    private static bool RunTextResourceFailure(byte[] response, ManagedTextFailureReason expected, int fragment, bool allowBinary = false)
    {
        HttpFixtureBackend backend = new(response, fragment);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        ManagedTextResourceRequest request = new(service, ManagedHttpLimits.MaximumStreamedBodyLength,
                                                 ManagedContentEncodingLimits.MaximumDecodedResourceLength,
                                                 allowUnknownMime: false, allowBinaryMime: allowBinary);
        ManagedTextCountConsumer count = new();
        Check(request.BeginGet("phase41.test"u8, "/failure"u8, count) == NetworkOperationResult.Started, "text-failure-begin");
        for (int poll = 0; poll != 100_000 && !request.Progress.IsTerminal; ++poll) request.Poll();
        if (expected == ManagedTextFailureReason.None)
            return request.State == ManagedResourceState.Completed && count.Count == 1;
        return request.State == ManagedResourceState.Failed && request.FailureReason == expected && count.Count == 0;
    }

    private static int CountScalars(byte[] utf8) => Decode(utf8, ManagedTextCharset.Utf8, 64).Count;
    private static int IndexOfBody(byte[] response)
    {
        for (int index = 3; index < response.Length; ++index)
            if (response[index - 3] == '\r' && response[index - 2] == '\n' && response[index - 1] == '\r' && response[index] == '\n') return index + 1;
        return response.Length;
    }
    private static byte[] Combine(byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length]; first.CopyTo(result, 0); second.CopyTo(result, first.Length); return result;
    }

    private static ManagedContentTypeMetadata Parse(string? value) => value == null
        ? new(ManagedContentTypeMetadataState.Available, ManagedMimeClassification.Unknown, ManagedCharsetDeclarationState.None, ManagedTextCharset.None, 0)
        : ManagedContentTypeParser.Parse(Encoding.ASCII.GetBytes(value));

    private static List<uint> Decode(byte[] bytes, ManagedTextCharset charset, int fragment) => Decode(bytes, charset, fragment, out _);
    private static List<uint> Decode(byte[] bytes, ManagedTextCharset charset, int fragment, out ManagedTextDecoder decoder)
    {
        decoder = new(charset); RecordingConsumer consumer = new();
        for (int offset = 0; offset != bytes.Length;)
        {
            int take = Math.Min(fragment, bytes.Length - offset);
            while (take != 0)
            {
                int accepted = Math.Min(take, decoder.InputFreeCapacity);
                if (accepted == 0) Drain(decoder, consumer, false);
                else { Check(decoder.AppendInput(bytes.AsSpan(offset, accepted)), "decode-append"); offset += accepted; take -= accepted; Drain(decoder, consumer, false); }
            }
        }
        ManagedTextDecoderProcessResult result;
        do
        {
            result = decoder.Pump(consumer, true);
            if (result == ManagedTextDecoderProcessResult.Failed)
                throw new InvalidOperationException("text failure: " + decoder.FailureReason);
            if (result == ManagedTextDecoderProcessResult.OutputAvailable) continue;
        }
        while (result != ManagedTextDecoderProcessResult.Complete);
        Check(decoder.State == ManagedTextDecoderState.Completed, "decode-complete");
        Check(consumer.Complete(), "decode-consumer-complete");
        return consumer.Values;
    }

    private static bool Fails(byte[] bytes, ManagedTextCharset charset, int fragment, ManagedTextDecoderFailureReason expected)
    {
        try { Decode(bytes, charset, fragment, out _); return false; }
        catch (InvalidOperationException exception) { return exception.Message.Contains(expected.ToString(), StringComparison.Ordinal); }
    }

    private static void Drain(ManagedTextDecoder decoder, IManagedTextConsumer consumer, bool end)
    {
        while (true)
        {
            ManagedTextDecoderProcessResult result = decoder.Pump(consumer, end);
            if (result == ManagedTextDecoderProcessResult.Failed) throw new InvalidOperationException("text decoder failure: " + decoder.FailureReason);
            if (result == ManagedTextDecoderProcessResult.NeedInput || result == ManagedTextDecoderProcessResult.Complete) return;
            if (result == ManagedTextDecoderProcessResult.Paused) return;
        }
    }

    private static void FeedConsumer(List<uint> values, IManagedTextConsumer consumer, int segment)
    {
        for (int offset = 0; offset < values.Count; offset += segment)
            Check(consumer.Consume(values.Skip(offset).Take(Math.Min(segment, values.Count - offset)).ToArray()) == ManagedHttpBodySinkResult.Continue, "consumer-feed");
    }

    private static void FeedParser(ManagedHttpResponseParser parser, byte[] bytes, int fragment)
    {
        for (int offset = 0; offset != bytes.Length;)
        {
            Check(parser.TryFeed(bytes.AsSpan(offset, Math.Min(fragment, bytes.Length - offset)), out int consumed), "parser-feed");
            Check(consumed != 0 || parser.IsBodyComplete || parser.FailureReason != ManagedHttpParseFailureReason.None, "parser-progress");
            offset += consumed;
        }
        Check(parser.IsBodyComplete, "parser-finished");
    }

    private static byte[] EncodeScalar(int scalar)
    {
        if (scalar <= 0x7F) return new[] { (byte)scalar };
        if (scalar <= 0x7FF) return new[] { (byte)(0xC0 | (scalar >> 6)), (byte)(0x80 | (scalar & 0x3F)) };
        if (scalar <= 0xFFFF) return new[] { (byte)(0xE0 | (scalar >> 12)), (byte)(0x80 | ((scalar >> 6) & 0x3F)), (byte)(0x80 | (scalar & 0x3F)) };
        return new[] { (byte)(0xF0 | (scalar >> 18)), (byte)(0x80 | ((scalar >> 12) & 0x3F)), (byte)(0x80 | ((scalar >> 6) & 0x3F)), (byte)(0x80 | (scalar & 0x3F)) };
    }

    private static byte[] CompressGzip(byte[] bytes)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, true)) gzip.Write(bytes);
        return output.ToArray();
    }
    private static byte[] CompressZlib(byte[] bytes)
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionLevel.SmallestSize, true)) zlib.Write(bytes);
        return output.ToArray();
    }
    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static void PumpContent(ManagedContentEncodingDecoder decoder, TextSink sink, bool end)
    {
        while (true)
        {
            ManagedContentDecoderProcessResult result = decoder.Pump(end);
            if (decoder.OutputLength != 0) Check(decoder.ConsumeOutput(sink) == ManagedHttpBodyDeliveryResult.Delivered, "content-to-text");
            if (result == ManagedContentDecoderProcessResult.NeedInput || result == ManagedContentDecoderProcessResult.Complete) return;
            if (result == ManagedContentDecoderProcessResult.Failed) throw new InvalidOperationException("content failure: " + decoder.FailureReason);
        }
    }
    private static byte[] Pattern(int length) { byte[] bytes = new byte[length]; for (int i = 0; i != length; ++i) bytes[i] = (byte)((i * 31 + 7) & 0xFF); return bytes; }
    private static void Check(bool condition, string name) { ++s_cases; if (!condition) throw new InvalidOperationException(name); }

    private sealed class RecordingConsumer : IManagedTextConsumer
    {
        internal readonly List<uint> Values = new();
        public ManagedResourceConsumerState State { get; private set; }
        public ManagedTextConsumerFailureReason FailureReason => ManagedTextConsumerFailureReason.None;
        public int ScalarsProcessed => Values.Count;
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment) { foreach (uint value in segment) Values.Add(value); State = ManagedResourceConsumerState.Receiving; return ManagedHttpBodySinkResult.Continue; }
        public bool Complete() { State = ManagedResourceConsumerState.Completed; return true; }
        public void Cancel() => State = ManagedResourceConsumerState.Cancelled;
        public void Reset() { Values.Clear(); State = ManagedResourceConsumerState.Idle; }
    }

    private sealed class PausingConsumer : IManagedTextConsumer
    {
        internal bool Paused { get; private set; }
        private bool _pauseNext;
        internal readonly List<uint> Values = new();
        internal PausingConsumer() => Reset();
        public ManagedResourceConsumerState State { get; private set; }
        public ManagedTextConsumerFailureReason FailureReason => ManagedTextConsumerFailureReason.None;
        public int ScalarsProcessed => Values.Count;
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment) { if (Paused) return ManagedHttpBodySinkResult.Pause; if (_pauseNext) { _pauseNext = false; Paused = true; State = ManagedResourceConsumerState.Paused; return ManagedHttpBodySinkResult.Pause; } foreach (uint value in segment) Values.Add(value); State = ManagedResourceConsumerState.Receiving; return ManagedHttpBodySinkResult.Continue; }
        internal void ResumeAcceptance() => Paused = false;
        public bool Complete() { State = ManagedResourceConsumerState.Completed; return true; }
        public void Cancel() => State = ManagedResourceConsumerState.Cancelled;
        public void Reset() { Values.Clear(); Paused = false; _pauseNext = true; State = ManagedResourceConsumerState.Idle; }
    }

    private sealed class TextSink : IManagedHttpBodySink
    {
        private readonly ManagedTextDecoder _decoder;
        private readonly RecordingConsumer _consumer;
        internal TextSink(ManagedTextDecoder decoder, RecordingConsumer consumer) { _decoder = decoder; _consumer = consumer; }
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            Check(_decoder.AppendInput(segment), "text-sink-append");
            Drain(_decoder, _consumer, false);
            return ManagedHttpBodySinkResult.Continue;
        }
    }

    private sealed class HttpFixtureBackend : IManagedNetworkServiceBackend
    {
        private readonly byte[] _response;
        private readonly int _fragmentSize;
        private readonly List<byte[]> _fragments = new();
        private ManagedNetworkService? _service;
        private ManagedNetworkServiceBackendEvent _event;
        private bool _eventPending;
        private ManagedTcpConnectionState _tcpState;
        private int _fragmentIndex;
        private bool _responseQueued;
        private bool _finQueued;

        internal HttpFixtureBackend(byte[] response, int fragmentSize)
        {
            _response = response; _fragmentSize = fragmentSize <= 0 ? 512 : fragmentSize;
        }
        public bool IsAvailable => true;
        public NetworkStatus GetStatus() => new(true, true, true, true, 0x021500000002,
            new Ipv4Address(0x0A0F0001), new Ipv4Address(0xFFFFFF00), new Ipv4Address(0x0A0F0001));
        public void SetRuntimeStatus(NetworkStatus status) { }
        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        { _event = ManagedNetworkServiceBackendEvent.DnsResolved; _eventPending = true; return ManagedNetworkServiceBackendResult.Started; }
        public bool TryGetResolved(out Ipv4Address address) { address = new Ipv4Address(0x0A0F0002); return true; }
        public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
        {
            if (_tcpState == ManagedTcpConnectionState.LastAck) _tcpState = ManagedTcpConnectionState.TimeWait;
            if (_eventPending)
            {
                serviceEvent = _event; _eventPending = false;
                if (serviceEvent == ManagedNetworkServiceBackendEvent.TcpEstablished) _tcpState = ManagedTcpConnectionState.Established;
                return true;
            }
            if (_fragmentIndex < _fragments.Count && _service != null)
            {
                byte[] fragment = _fragments[_fragmentIndex];
                if (!((IManagedTcpApplicationSink)_service).TryCaptureReceivedTcp(
                    new Ipv4Address(0x0A0F0002), new Ipv4Address(0x0A0F0001),
                    ManagedTcpConnection.ServerPort, ManagedTcpConnection.ClientPort, fragment))
                { serviceEvent = ManagedNetworkServiceBackendEvent.None; return true; }
                ++_fragmentIndex; serviceEvent = ManagedNetworkServiceBackendEvent.TcpReceived; return true;
            }
            if (_finQueued)
            { _finQueued = false; _tcpState = ManagedTcpConnectionState.CloseWait; serviceEvent = ManagedNetworkServiceBackendEvent.TcpClosed; return true; }
            serviceEvent = ManagedNetworkServiceBackendEvent.None; return true;
        }
        public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination) => ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult BindUdp(ushort port) => ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port) => ManagedNetworkServiceBackendResult.Rejected;
        public ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination, ushort destinationPort, ushort sourcePort, ReadOnlySpan<byte> payload) => ManagedNetworkServiceBackendResult.NoResource;
        public ManagedTcpConnectionState TcpState => _tcpState;
        public ManagedNetworkServiceBackendResult BeginTcpConnect(Ipv4Address destination, ushort destinationPort)
        { _tcpState = ManagedTcpConnectionState.SynSent; _event = ManagedNetworkServiceBackendEvent.TcpEstablished; _eventPending = true; return ManagedNetworkServiceBackendResult.Started; }
        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
        {
            if (!_responseQueued)
            {
                _responseQueued = true;
                for (int offset = 0; offset < _response.Length;)
                { int length = Math.Min(_fragmentSize, _response.Length - offset); _fragments.Add(_response.AsSpan(offset, length).ToArray()); offset += length; }
                _finQueued = true;
            }
            return ManagedNetworkServiceBackendResult.Success;
        }
        public ManagedNetworkServiceBackendResult CloseTcp() { _tcpState = ManagedTcpConnectionState.LastAck; return ManagedNetworkServiceBackendResult.Started; }
        public bool Teardown()
        { _tcpState = ManagedTcpConnectionState.Closed; _fragments.Clear(); _fragmentIndex = 0; _finQueued = false; _responseQueued = false; _eventPending = false; return true; }
        internal void Attach(ManagedNetworkService service) => _service = service;
    }
}

internal static class ManagedTextDecoderTestExtensions
{
    internal static bool TextWindowCapacityForTest(this ManagedTextDecoder decoder) => decoder.ScalarsProduced >= 0 && ManagedTextDecodingLimits.OutputWindowCapacity == 256;
}

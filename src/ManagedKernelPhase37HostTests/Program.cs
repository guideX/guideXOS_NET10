using System;
using System.Collections.Generic;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase37HostTests;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestChunkedBoundariesAndLargeAggregate();
            TestChunkFramingAndPayloadSplits();
            TestContentLengthStreaming();
            TestMalformedChunkControls();
            TestCompatibilityBodyAndSinkFailure();
            Console.WriteLine(
                $"MANAGED_KERNEL_PHASE37_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE37_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestChunkedBoundariesAndLargeAggregate()
    {
        Check(ManagedHttpLimits.MaximumStreamedBodyLength >
              ManagedHttpLimits.MaximumAcceptedBodyLength,
              "streaming-cap-exceeds-legacy-cap");
        Check(ManagedHttpLimits.MaximumBodyDeliveryWindow <
              ManagedHttpLimits.MaximumStreamedBodyLength,
              "delivery-window-is-not-entity-cap");

        foreach (int length in new[] { 4095, 4096, 4097 })
        {
            byte[] body = Pattern(length);
            ManagedHttpResponseParser parser = NewStreamingParser();
            DigestSink sink = new(body);
            Check(FeedAndConsume(parser, Chunked(body), 1, sink, out int peak),
                  "chunked-boundary-" + length);
            Check(parser.IsBodyComplete && parser.BodyLength == length &&
                  parser.BodyBytesDelivered == length && sink.Bytes == length &&
                  sink.IsExact && peak <= ManagedHttpLimits.MaximumBodyDeliveryWindow,
                  "chunked-boundary-accounting-" + length);
            Check(sink.TryFinalize(), "chunked-boundary-digest-" + length);
        }

        byte[] largeBody = Pattern(20_000);
        ManagedHttpResponseParser large = NewStreamingParser();
        DigestSink largeSink = new(largeBody);
        Check(FeedAndConsume(large, Chunked(largeBody), 17, largeSink,
                             out int largePeak), "chunked-over-parser-buffer");
        Check(large.IsBodyComplete && large.BodyLength == largeBody.Length &&
              large.BodyBytesDelivered == largeBody.Length &&
              largeSink.Bytes == largeBody.Length && largeSink.IsExact &&
              largePeak <= ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "chunked-large-body-accounting");
        Check(largeSink.TryFinalize(), "chunked-large-body-digest");
    }

    private static void TestChunkFramingAndPayloadSplits()
    {
        byte[] body = Pattern(8193);
        byte[] response = Chunked(body, new[] { 1, 4095, 4096, 1 }, true);
        ManagedHttpResponseParser parser = NewStreamingParser();
        DigestSink sink = new(body);
        Check(FeedAndConsume(parser, response, 2, sink, out int peak),
              "chunked-framing-split-at-two-byte-boundaries");
        Check(parser.IsChunked && parser.IsBodyComplete && sink.IsExact &&
              parser.BodyLength == body.Length && parser.BodyBytesDelivered == body.Length &&
              peak <= ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "chunked-framing-and-payload-accounting");
        Check(sink.TryFinalize(), "chunked-framing-digest");

        ManagedHttpResponseParser terminal = NewStreamingParser();
        byte[] terminalBody = "terminal-body"u8.ToArray();
        DigestSink terminalSink = new(terminalBody);
        byte[] terminalResponse = Chunked(terminalBody,
                                          new[] { 4, 4, 4, 1 }, false);
        Check(FeedAndConsume(terminal, terminalResponse, 3, terminalSink,
                             out _), "zero-sized-terminal-chunk");
        int delivered = terminal.BodyBytesDelivered;
        Check(terminal.IsBodyComplete && terminalSink.IsExact &&
              terminal.TryConsumeBody(terminalSink) &&
              terminal.BodyBytesDelivered == delivered &&
              terminal.NotifyConnectionClosed() &&
              terminal.State == ManagedHttpParseState.Closed &&
              terminal.NotifyConnectionClosed(),
              "terminal-completes-exactly-once");
    }

    private static void TestContentLengthStreaming()
    {
        byte[] body = Pattern(20_000);
        byte[] response = Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 20000\r\n" +
                  "Connection: close\r\n\r\n"), body);
        ManagedHttpResponseParser parser = NewStreamingParser();
        DigestSink sink = new(body);
        Check(FeedAndConsume(parser, response, 31, sink, out int peak),
              "content-length-over-four-kib");
        Check(parser.FramingMode == ManagedHttpFramingMode.ContentLength &&
              parser.IsBodyComplete && parser.ContentLength == body.Length &&
              parser.BodyLength == body.Length &&
              parser.BodyBytesDelivered == body.Length && sink.IsExact &&
              peak <= ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "content-length-large-body-accounting");
        Check(sink.TryFinalize(), "content-length-large-body-digest");
    }

    private static void TestMalformedChunkControls()
    {
        Check(Fails("ZZ\r\n", ManagedHttpParseFailureReason.ChunkSizeSyntax),
              "malformed-chunk-size-fails-closed");

        ManagedHttpResponseParser truncated = NewStreamingParser();
        byte[] truncatedResponse = Ascii(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "4\r\nabc");
        Check(truncated.TryFeed(truncatedResponse, out int truncatedConsumed) &&
              truncatedConsumed == truncatedResponse.Length &&
              !truncated.NotifyConnectionClosed() &&
              truncated.FailureReason ==
                  ManagedHttpParseFailureReason.PrematureConnectionClose,
              "truncated-chunk-body-fails-closed");

        ManagedHttpResponseParser missing = NewStreamingParser();
        byte[] missingTerminal = Ascii(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "1\r\na\r\n");
        Check(missing.TryFeed(missingTerminal, out _) &&
              !missing.NotifyConnectionClosed() &&
              missing.FailureReason ==
                  ManagedHttpParseFailureReason.PrematureConnectionClose,
              "missing-terminal-chunk-fails-closed");

        string oversizedMetadata = "1;" + new string('x',
            ManagedHttpLimits.MaximumChunkSizeLineLength) + "\r\n";
        Check(Fails(oversizedMetadata,
                    ManagedHttpParseFailureReason.ChunkSizeLineOverflow),
              "oversized-chunk-metadata-respects-line-limit");

        ManagedHttpResponseParser oversizedChunk = NewStreamingParser();
        byte[] oversized = Ascii(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" +
            "1001\r\n");
        Check(!oversizedChunk.TryFeed(oversized, out _) &&
              oversizedChunk.FailureReason == ManagedHttpParseFailureReason.ChunkTooLarge,
              "oversized-individual-chunk-respects-chunk-limit");
    }

    private static void TestCompatibilityBodyAndSinkFailure()
    {
        byte[] smallBody = Pattern(256);
        ManagedHttpResponseParser small = new();
        byte[] smallResponse = Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 256\r\n" +
                  "Connection: close\r\n\r\n"), smallBody);
        Check(small.Feed(smallResponse) && small.IsBodyComplete,
              "small-complete-body-accepted");
        byte[] copy = new byte[ManagedHttpLimits.MaximumBodyCapacity];
        Check(small.TryCopyBody(copy, out int copied) && copied == smallBody.Length &&
              copy.AsSpan().SequenceEqual(smallBody),
              "small-complete-body-copy-compatibility");

        ManagedHttpResponseParser materializedOverflow = NewStreamingParser();
        byte[] overflowBody = Pattern(257);
        DigestSink overflowSink = new(overflowBody);
        Check(FeedAndConsume(materializedOverflow,
                             Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: 257\r\n\r\n"),
                                     overflowBody),
                             19, overflowSink, out _),
              "streamed-body-over-materialized-cap-accepted");
        Check(!materializedOverflow.TryCopyBody(copy, out int overflowLength) &&
              overflowLength == overflowBody.Length,
              "complete-body-materialization-remains-bounded");

        byte[] body = Pattern(4097);
        byte[] response = Chunked(body, new[] { 1024, 1024, 1024, 1024, 1 }, false);
        ManagedHttpResponseParser parser = NewStreamingParser();
        Check(parser.TryFeed(response, out int consumed) &&
              parser.BufferedBodyLength == ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "sink-failure-starts-with-full-delivery-window");
        RejectingSink rejecting = new();
        int buffered = parser.BufferedBodyLength;
        Check(!parser.TryConsumeBody(rejecting) &&
              parser.BufferedBodyLength == buffered &&
              parser.BodyBytesDelivered == 0,
              "sink-failure-preserves-unread-segment");

        DigestSink sink = new(body);
        int offset = consumed;
        Check(parser.TryConsumeBody(sink) &&
              ContinueFeedAndConsume(parser, response, ref offset, 23, sink) &&
              parser.IsBodyComplete && sink.IsExact &&
              parser.BodyBytesDelivered == body.Length && sink.TryFinalize(),
              "sink-recovery-preserves-parser-state");
    }

    private static ManagedHttpResponseParser NewStreamingParser() =>
        new(ManagedHttpLimits.MaximumStreamedBodyLength, false, true);

    private static bool FeedAndConsume(ManagedHttpResponseParser parser,
                                       byte[] source, int offeredSegment,
                                       IManagedHttpBodySink sink, out int peak)
    {
        peak = 0;
        int offset = 0;
        while (offset != source.Length)
        {
            int offered = Math.Min(offeredSegment, source.Length - offset);
            while (offered != 0)
            {
                if (!parser.TryFeed(source.AsSpan(offset, offered), out int consumed))
                    return false;
                peak = Math.Max(peak, parser.BufferedBodyLength);
                bool hadPendingBody = parser.HasPendingBody;
                offset += consumed;
                offered -= consumed;
                if (!parser.TryConsumeBody(sink)) return false;
                if (consumed == 0 && offered != 0 && !hadPendingBody) return false;
            }
        }
        peak = Math.Max(peak, parser.BufferedBodyLength);
        return parser.TryConsumeBody(sink) && parser.IsBodyComplete;
    }

    private static bool ContinueFeedAndConsume(ManagedHttpResponseParser parser,
                                               byte[] source, ref int offset,
                                               int offeredSegment,
                                               IManagedHttpBodySink sink)
    {
        while (offset != source.Length)
        {
            int offered = Math.Min(offeredSegment, source.Length - offset);
            while (offered != 0)
            {
                if (!parser.TryFeed(source.AsSpan(offset, offered), out int consumed))
                    return false;
                bool hadPendingBody = parser.HasPendingBody;
                offset += consumed;
                offered -= consumed;
                if (!parser.TryConsumeBody(sink)) return false;
                if (consumed == 0 && offered != 0 && !hadPendingBody) return false;
            }
        }
        return parser.TryConsumeBody(sink);
    }

    private static bool Fails(string chunk, ManagedHttpParseFailureReason reason)
    {
        ManagedHttpResponseParser parser = NewStreamingParser();
        byte[] response = Ascii(
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n" + chunk);
        return !parser.TryFeed(response, out _) &&
               parser.State == ManagedHttpParseState.Failed &&
               parser.FailureReason == reason;
    }

    private static byte[] Chunked(byte[] body, int[]? chunkSizes = null,
                                  bool extensions = false)
    {
        List<byte> output = new();
        AddAscii(output,
            "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n" +
            "Connection: close\r\n\r\n");
        int offset = 0;
        int patternIndex = 0;
        while (offset != body.Length)
        {
            int count = chunkSizes == null
                ? Math.Min(4096, body.Length - offset)
                : Math.Min(chunkSizes[patternIndex++ % chunkSizes.Length],
                           body.Length - offset);
            AddAscii(output, count.ToString("X") +
                (extensions && offset == 0 ? ";foo=bar" : string.Empty) + "\r\n");
            for (int index = 0; index != count; ++index)
                output.Add(body[offset + index]);
            AddAscii(output, "\r\n");
            offset += count;
        }
        AddAscii(output, "0\r\nX-Phase37: bounded\r\n\r\n");
        return output.ToArray();
    }

    private static byte[] Pattern(int length)
    {
        byte[] body = new byte[length];
        for (int index = 0; index != body.Length; ++index)
            body[index] = (byte)((index * 31 + 7) & 0xFF);
        return body;
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void AddAscii(List<byte> output, string value)
    {
        output.AddRange(Ascii(value));
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException("failed: " + name);
    }

    private sealed class DigestSink : IManagedHttpBodySink
    {
        private readonly byte[] _expected;
        private readonly ManagedSha256 _hash = new();
        private int _offset;

        internal DigestSink(byte[] expected) => _expected = expected;
        internal int Bytes => _offset;
        internal bool IsExact => _offset == _expected.Length;

        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            if (segment.Length > _expected.Length - _offset ||
                !segment.SequenceEqual(_expected.AsSpan(_offset, segment.Length)) ||
                !_hash.Append(segment))
                return ManagedHttpBodySinkResult.Fail;
            _offset += segment.Length;
            return ManagedHttpBodySinkResult.Continue;
        }

        internal bool TryFinalize()
        {
            byte[] actual = new byte[ManagedSha256.DigestSize];
            byte[] expected = new byte[ManagedSha256.DigestSize];
            return IsExact && _hash.TryFinalize(actual) &&
                   ManagedSha256.TryHash(_expected, expected) &&
                   actual.AsSpan().SequenceEqual(expected);
        }
    }

    private sealed class RejectingSink : IManagedHttpBodySink
    {
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment) =>
            ManagedHttpBodySinkResult.Fail;
    }
}

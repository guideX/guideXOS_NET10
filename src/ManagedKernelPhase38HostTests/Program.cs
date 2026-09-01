using System;
using System.Collections.Generic;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernel;

internal static class Program
{
    private static readonly Ipv4Address Local = new(0x0A0F002AU);
    private static readonly Ipv4Address Peer = new(0x0A0F0002U);
    private static readonly ManagedX509UtcTime TestTime =
        new(2028, 1, 1, 0, 0, 0);
    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestContentLengthPauseResume();
            TestChunkedPauseResumeAndDigest();
            TestFragmentedRepeatedPauseAndBoundary();
            TestPausedStateDoesNotGrow();
            TestStreamingHotPathDoesNotAllocate();
            TestCompatibilityLimitsAndFailures();
            TestHttpClientProgressAndCancellation();
            TestHttpsCancellationParity();
            Console.WriteLine(
                $"MANAGED_KERNEL_PHASE38_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE38_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestContentLengthPauseResume()
    {
        byte[] body = Pattern(4_097);
        byte[] response = Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 4097\r\n" +
                  "Connection: close\r\n\r\n"), body);
        ManagedHttpResponseParser parser = NewStreamingParser();
        FlowSink sink = new(body, pauseBeforeAccept: 1);
        Check(Drive(parser, response, 23, sink, out int peak),
              "content-length-pause-resume");
        ManagedHttpProgressSnapshot progress = parser.Progress;
        Check(progress.StatusCode == 200 &&
              progress.TransferMode == ManagedHttpFramingMode.ContentLength &&
              progress.HasKnownTotalLength && progress.TotalEntityLength == body.Length,
              "content-length-progress-total");
        Check(progress.DecodedBodyBytesReceived == body.Length &&
              progress.DecodedBodyBytesDelivered == body.Length &&
              progress.BufferedBodyBytes == 0 && progress.DeliveredSegmentCount > 1 &&
              progress.PauseCount == 1 && progress.ResumeCount == 1 &&
              progress.State == ManagedHttpTransferState.Completed &&
              peak <= ManagedHttpLimits.MaximumBodyDeliveryWindow && sink.IsExact,
              "content-length-progress-accounting");
        Check(HashEquals(body, sink), "content-length-pause-digest");
    }

    private static void TestChunkedPauseResumeAndDigest()
    {
        byte[] body = Pattern(12_345);
        byte[] response = Chunked(body, new[] { 1, 17, 1024, 4096, 73 });
        ManagedHttpResponseParser parser = NewStreamingParser();
        FlowSink sink = new(body, pauseAfterAccepted: 2);
        Check(Drive(parser, response, 2, sink, out int peak),
              "chunked-fragmented-pause-resume");
        ManagedHttpProgressSnapshot progress = parser.Progress;
        Check(progress.TransferMode == ManagedHttpFramingMode.Chunked &&
              !progress.HasKnownTotalLength && progress.TotalEntityLength == 0 &&
              progress.DecodedBodyBytesReceived == body.Length &&
              progress.DecodedBodyBytesDelivered == body.Length &&
              progress.BufferedBodyBytes == 0 && progress.PauseCount == 1 &&
              progress.ResumeCount == 1 && progress.State == ManagedHttpTransferState.Completed,
              "chunked-progress-accounting");
        Check(HashEquals(body, sink), "chunked-pause-digest");
    }

    private static void TestFragmentedRepeatedPauseAndBoundary()
    {
        byte[] exactBody = Pattern(ManagedHttpLimits.MaximumBodyDeliveryWindow);
        ManagedHttpResponseParser exact = NewStreamingParser();
        FlowSink exactSink = new(exactBody, pauseBeforeAccept: 1);
        byte[] exactResponse = Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 1024\r\n\r\n"),
            exactBody);
        int offset = 0;
        Check(exact.TryFeed(exactResponse, out offset) &&
              exact.IsBodyComplete && exact.BufferedBodyLength == 1024,
              "pause-exact-window-is-buffered");
        Check(exact.ConsumeBody(exactSink) == ManagedHttpBodyDeliveryResult.Paused,
              "pause-exact-window-status");
        ManagedHttpProgressSnapshot paused = exact.Progress;
        Check(paused.State == ManagedHttpTransferState.Paused &&
              paused.DecodedBodyBytesReceived == exactBody.Length &&
              paused.DecodedBodyBytesDelivered == 0 && paused.BufferedBodyBytes == 1024,
              "pause-exact-window-progress");
        Check(exact.TryFeed(exactResponse.AsSpan(offset), out int retry) && retry == 0,
              "pause-exact-window-stops-source-ownership");
        Check(exact.ConsumeBody(exactSink) == ManagedHttpBodyDeliveryResult.Delivered &&
              exact.Progress.State == ManagedHttpTransferState.Completed &&
              exact.Progress.ResumeCount == 1,
              "pause-exact-window-resumes");

        byte[] partialBody = Pattern(ManagedHttpLimits.MaximumBodyDeliveryWindow + 1);
        ManagedHttpResponseParser partial = NewStreamingParser();
        FlowSink partialSink = new(partialBody, pauseAfterAccepted: 1);
        Check(Drive(partial, Combine(
                  Ascii("HTTP/1.1 200 OK\r\nContent-Length: 1025\r\n\r\n"),
                  partialBody), 1, partialSink, out _),
              "pause-partial-final-segment");
        Check(partial.Progress.PauseCount == 1 && partial.Progress.ResumeCount == 1 &&
              partial.Progress.BufferedBodyBytes == 0 && partialSink.IsExact,
              "pause-partial-final-accounting");

        byte[] repeatedBody = Pattern(8_193);
        ManagedHttpResponseParser repeated = NewStreamingParser();
        FlowSink repeatedSink = new(repeatedBody, pauseEveryAccepted: true);
        Check(Drive(repeated, Combine(
                  Ascii("HTTP/1.1 200 OK\r\nContent-Length: 8193\r\n\r\n"),
                  repeatedBody), 7, repeatedSink, out _),
              "pause-repeated-resume");
        Check(repeated.Progress.PauseCount > 4 &&
              repeated.Progress.ResumeCount == repeated.Progress.PauseCount &&
              repeated.Progress.DeliveredSegmentCount > 4 && repeatedSink.IsExact,
              "pause-repeated-counts");
    }

    private static void TestPausedStateDoesNotGrow()
    {
        byte[] body = Pattern(9_999);
        byte[] response = Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 9999\r\n\r\n"), body);
        ManagedHttpResponseParser parser = NewStreamingParser();
        AlwaysPauseSink sink = new();
        Check(parser.TryFeed(response, out int consumed) && consumed < response.Length &&
              parser.BufferedBodyLength == ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "paused-large-response-stops-at-window");
        Check(parser.ConsumeBody(sink) == ManagedHttpBodyDeliveryResult.Paused,
              "paused-large-response-enters-paused");
        ManagedHttpProgressSnapshot baseline = parser.Progress;
        for (int poll = 0; poll != 64; ++poll)
        {
            Check(parser.TryFeed(response.AsSpan(consumed), out int retry) && retry == 0,
                  "paused-poll-does-not-consume-" + poll);
            Check(parser.ConsumeBody(sink) == ManagedHttpBodyDeliveryResult.Paused,
                  "paused-poll-preserves-segment-" + poll);
            ManagedHttpProgressSnapshot current = parser.Progress;
            Check(current.DecodedBodyBytesReceived == baseline.DecodedBodyBytesReceived &&
                  current.DecodedBodyBytesDelivered == 0 &&
                  current.BufferedBodyBytes == baseline.BufferedBodyBytes &&
                  current.DeliveredSegmentCount == baseline.DeliveredSegmentCount &&
                  current.PauseCount == baseline.PauseCount &&
                  current.ResumeCount == baseline.ResumeCount &&
                  current.State == ManagedHttpTransferState.Paused,
                  "paused-poll-snapshot-stable-" + poll);
        }
        Check(parser.BufferedBodyLength <= ManagedHttpLimits.MaximumBodyDeliveryWindow &&
              parser.Progress.BufferedBodyBytes == ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "paused-buffer-remains-bounded");
    }

    private static void TestStreamingHotPathDoesNotAllocate()
    {
        byte[] body = Pattern(32_769);
        byte[] response = Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 32769\r\n\r\n"), body);
        ManagedHttpResponseParser parser = NewStreamingParser();
        FlowSink sink = new(body);
        Check(parser.TryFeed(ReadOnlySpan<byte>.Empty, out int warmup) &&
              warmup == 0, "hot-path-warmup");
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool completed = Drive(parser, response, 97, sink, out int peak);
        long after = GC.GetAllocatedBytesForCurrentThread();
        Check(completed && sink.IsExact && peak <=
                  ManagedHttpLimits.MaximumBodyDeliveryWindow && after == before,
              "hot-path-no-managed-allocation");
    }

    private static void TestCompatibilityLimitsAndFailures()
    {
        byte[] small = Pattern(256);
        ManagedHttpResponseParser compatibility = new();
        Check(compatibility.Feed(Combine(
                  Ascii("HTTP/1.1 200 OK\r\nContent-Length: 256\r\n" +
                        "Connection: close\r\n\r\n"), small)) &&
              compatibility.IsBodyComplete,
              "complete-body-compatibility-accepted");
        byte[] copy = new byte[256];
        Check(compatibility.TryCopyBody(copy, out int copied) && copied == 256 &&
              copy.AsSpan().SequenceEqual(small), "complete-body-compatibility-copy");

        ManagedHttpResponseParser overflow = NewStreamingParser();
        byte[] overCompatibility = Pattern(257);
        Check(Drive(overflow, Combine(
                  Ascii("HTTP/1.1 200 OK\r\nContent-Length: 257\r\n\r\n"),
                  overCompatibility), 11, new FlowSink(overCompatibility), out _),
              "streamed-body-exceeds-materialized-cap");
        Check(!overflow.TryCopyBody(copy, out int reported) && reported == 257,
              "materialized-body-remains-bounded");

        ManagedHttpResponseParser failedSink = NewStreamingParser();
        Check(failedSink.TryFeed(Combine(
                  Ascii("HTTP/1.1 200 OK\r\nContent-Length: 1024\r\n\r\n"),
                  Pattern(1024)), out _) && failedSink.BufferedBodyLength == 1024,
              "sink-failure-starts-with-window");
        int before = failedSink.BufferedBodyLength;
        Check(failedSink.ConsumeBody(new FailSink()) ==
                  ManagedHttpBodyDeliveryResult.Failed &&
              failedSink.BufferedBodyLength == before &&
              failedSink.BodyBytesDelivered == 0 && !failedSink.TryConsumeBody(new FailSink()),
              "sink-failure-preserves-ownership");

        ManagedHttpResponseParser exactLimit = NewStreamingParser();
        byte[] limitBody = Pattern(ManagedHttpLimits.MaximumStreamedBodyLength);
        Check(Drive(exactLimit, Combine(
                  Ascii("HTTP/1.1 200 OK\r\nContent-Length: 1048576\r\n\r\n"),
                  limitBody), 4096, new FlowSink(limitBody), out int limitPeak) &&
              exactLimit.Progress.State == ManagedHttpTransferState.Completed &&
              exactLimit.BodyLength == ManagedHttpLimits.MaximumStreamedBodyLength &&
              exactLimit.BodyBytesDelivered == exactLimit.BodyLength &&
              limitPeak <= ManagedHttpLimits.MaximumBodyDeliveryWindow,
              "streaming-limit-exact-boundary");
        ManagedHttpResponseParser oneBeyond = NewStreamingParser();
        Check(!oneBeyond.TryFeed(Ascii(
                  "HTTP/1.1 200 OK\r\nContent-Length: 1048577\r\n\r\n"), out _) &&
              oneBeyond.FailureReason == ManagedHttpParseFailureReason.BodyTooLarge &&
              oneBeyond.Progress.TerminalFailureReason ==
                  ManagedHttpTerminalFailureReason.BodyTooLarge,
              "streaming-limit-one-byte-beyond");
    }

    private static void TestHttpClientProgressAndCancellation()
    {
        byte[] body = Pattern(6_173);
        HttpFixtureBackend backend = new(Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 6173\r\n" +
                  "Connection: close\r\n\r\n"), body));
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        ManagedHttpClient client = new(service,
            ManagedHttpLimits.MaximumStreamedBodyLength, false);
        Check(client.Progress.State == ManagedHttpTransferState.Idle &&
              client.Progress.StatusCode == 0, "http-initial-progress");
        Check(client.BeginGet("phase38.test"u8, "/stream"u8) ==
                  NetworkOperationResult.Started, "http-begin-stream");
        Check(client.Progress.State == ManagedHttpTransferState.Receiving,
              "http-receiving-progress");

        FlowSink sink = new(body, pauseBeforeAccept: 1);
        bool sawHeaders = false;
        bool sawPause = false;
        for (int poll = 0; poll != 20_000 &&
             client.State != ManagedHttpClientState.Succeeded &&
             client.State != ManagedHttpClientState.Failed; ++poll)
        {
            Check(client.Poll() != NetworkOperationResult.Failed,
                  "http-poll-success-" + poll);
            if (client.StatusParsed) sawHeaders = true;
            ManagedHttpBodyDeliveryResult delivery = client.ConsumeResponseBody(sink);
            if (delivery == ManagedHttpBodyDeliveryResult.Paused)
            {
                sawPause = true;
                ManagedHttpProgressSnapshot paused = client.Progress;
                int backendPolls = backend.PollCount;
                for (int pausedPoll = 0; pausedPoll != 8; ++pausedPoll)
                {
                    Check(client.Poll() == NetworkOperationResult.Success &&
                          client.Progress.State == ManagedHttpTransferState.Paused &&
                          backend.PollCount == backendPolls,
                          "http-paused-poll-" + pausedPoll);
                }
                Check(client.Progress.DecodedBodyBytesDelivered ==
                          paused.DecodedBodyBytesDelivered &&
                      client.Progress.BufferedBodyBytes == paused.BufferedBodyBytes,
                      "http-paused-progress-stable");
                Check(client.ConsumeResponseBody(sink) ==
                          ManagedHttpBodyDeliveryResult.Delivered &&
                      client.Progress.State == ManagedHttpTransferState.Receiving,
                      "http-resume-progress");
            }
            else
            {
                Check(delivery != ManagedHttpBodyDeliveryResult.Failed,
                      "http-sink-not-failed");
            }
        }
        Check(sawHeaders && sawPause && client.State == ManagedHttpClientState.Succeeded,
              "http-stream-completes-after-pause");
        Check(client.Progress.State == ManagedHttpTransferState.Completed &&
              client.Progress.DecodedBodyBytesReceived == body.Length &&
              client.Progress.DecodedBodyBytesDelivered == body.Length &&
              client.Progress.BufferedBodyBytes == 0 && sink.IsExact &&
              client.Progress.HasKnownTotalLength &&
              client.Progress.TotalEntityLength == body.Length,
              "http-terminal-progress");

        HttpFixtureBackend cancelBackend = new(Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 6173\r\n\r\n"), body));
        ManagedNetworkService cancelService = ManagedNetworkService.CreateForTests(cancelBackend);
        cancelBackend.Attach(cancelService);
        ManagedHttpClient cancelled = new(cancelService,
            ManagedHttpLimits.MaximumStreamedBodyLength, false);
        Check(cancelled.BeginGet("phase38.test"u8, "/cancel"u8) ==
                  NetworkOperationResult.Started, "http-cancel-begin");
        for (int poll = 0; poll != 10 && !cancelled.StatusParsed; ++poll)
            Check(cancelled.Poll() == NetworkOperationResult.Success,
                  "http-cancel-header-poll-" + poll);
        CountingSink oneSegment = new();
        for (int poll = 0; poll != 20 &&
             cancelled.Progress.BufferedBodyBytes == 0; ++poll)
            Check(cancelled.Poll() == NetworkOperationResult.Success,
                  "http-cancel-body-poll-" + poll);
        Check(cancelled.StatusParsed && cancelled.Progress.BufferedBodyBytes != 0 &&
              cancelled.ConsumeResponseBody(oneSegment) ==
                  ManagedHttpBodyDeliveryResult.Delivered && oneSegment.Calls == 1,
              "http-cancel-after-one-segment");
        ManagedHttpProgressSnapshot beforeCancel = cancelled.Progress;
        Check(cancelled.Cancel() == NetworkOperationResult.Success &&
              cancelled.State == ManagedHttpClientState.Cancelled &&
              cancelled.Progress.State == ManagedHttpTransferState.Cancelled &&
              cancelled.Progress.TerminalFailureReason ==
                  ManagedHttpTerminalFailureReason.Cancelled &&
              cancelled.Progress.DecodedBodyBytesReceived ==
                  beforeCancel.DecodedBodyBytesReceived &&
              cancelled.Progress.DecodedBodyBytesDelivered ==
                  beforeCancel.DecodedBodyBytesDelivered &&
              cancelBackend.TeardownCount != 0,
              "http-cancel-terminal-and-teardown");
        CountingSink noLateDelivery = new();
        Check(cancelled.ConsumeResponseBody(noLateDelivery) ==
                  ManagedHttpBodyDeliveryResult.Cancelled && noLateDelivery.Calls == 0,
              "http-cancel-no-late-sink-call");
        byte[] cancelledRead = new byte[32];
        Check(!cancelled.TryReadResponseBodyChunk(cancelledRead, out int cancelledReadLength) &&
              cancelledReadLength == 0, "http-cancel-no-late-chunk-read");
        Check(cancelled.Cancel() == NetworkOperationResult.Success &&
              cancelled.Progress.State == ManagedHttpTransferState.Cancelled,
              "http-cancel-idempotent");
        Check(cancelled.Reset() == NetworkOperationResult.Success &&
              cancelled.Progress.State == ManagedHttpTransferState.Idle,
              "http-cancel-reset-reuse");

        HttpFixtureBackend beforeHeaderBackend = new(response: null);
        ManagedNetworkService beforeHeaderService =
            ManagedNetworkService.CreateForTests(beforeHeaderBackend);
        beforeHeaderBackend.Attach(beforeHeaderService);
        ManagedHttpClient beforeHeader = new(beforeHeaderService,
            ManagedHttpLimits.MaximumStreamedBodyLength, false);
        Check(beforeHeader.BeginGet("phase38.test"u8, "/cancel-before-headers"u8) ==
                  NetworkOperationResult.Started &&
              beforeHeader.Cancel() == NetworkOperationResult.Success &&
              beforeHeader.Progress.State == ManagedHttpTransferState.Cancelled &&
              beforeHeader.Progress.StatusCode == 0,
              "http-cancel-before-headers");

        HttpFixtureBackend pausedBackend = new(Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 6173\r\n\r\n"), body));
        ManagedNetworkService pausedService =
            ManagedNetworkService.CreateForTests(pausedBackend);
        pausedBackend.Attach(pausedService);
        ManagedHttpClient pausedClient = new(pausedService,
            ManagedHttpLimits.MaximumStreamedBodyLength, false);
        AlwaysPauseSink pausedSink = new();
        Check(pausedClient.BeginGet("phase38.test"u8, "/cancel-paused"u8) ==
                  NetworkOperationResult.Started, "http-cancel-paused-begin");
        for (int poll = 0; poll != 100 &&
             pausedClient.Progress.BufferedBodyBytes == 0; ++poll)
            Check(pausedClient.Poll() == NetworkOperationResult.Success,
                  "http-cancel-paused-poll-" + poll);
        Check(pausedClient.ConsumeResponseBody(pausedSink) ==
                  ManagedHttpBodyDeliveryResult.Paused &&
              pausedClient.Progress.State == ManagedHttpTransferState.Paused,
              "http-cancel-paused-entered");
        int pausedBackendPolls = pausedBackend.PollCount;
        ManagedHttpProgressSnapshot pausedBeforeCancel = pausedClient.Progress;
        Check(pausedClient.Poll() == NetworkOperationResult.Success &&
              pausedBackend.PollCount == pausedBackendPolls &&
              pausedClient.Cancel() == NetworkOperationResult.Success &&
              pausedClient.Progress.State == ManagedHttpTransferState.Cancelled &&
              pausedClient.Progress.BufferedBodyBytes ==
                  pausedBeforeCancel.BufferedBodyBytes,
              "http-cancel-while-paused");

        HttpFixtureBackend sinkFailureBackend = new(Combine(
            Ascii("HTTP/1.1 200 OK\r\nContent-Length: 6173\r\n\r\n"), body));
        ManagedNetworkService sinkFailureService =
            ManagedNetworkService.CreateForTests(sinkFailureBackend);
        sinkFailureBackend.Attach(sinkFailureService);
        ManagedHttpClient sinkFailureClient = new(sinkFailureService,
            ManagedHttpLimits.MaximumStreamedBodyLength, false);
        Check(sinkFailureClient.BeginGet("phase38.test"u8, "/sink-failure"u8) ==
                  NetworkOperationResult.Started, "http-sink-failure-begin");
        for (int poll = 0; poll != 100 &&
             sinkFailureClient.Progress.BufferedBodyBytes == 0; ++poll)
            Check(sinkFailureClient.Poll() == NetworkOperationResult.Success,
                  "http-sink-failure-poll-" + poll);
        int sinkFailureBuffered = sinkFailureClient.Progress.BufferedBodyBytes;
        Check(sinkFailureClient.ConsumeResponseBody(new FailSink()) ==
                  ManagedHttpBodyDeliveryResult.Failed &&
              sinkFailureClient.State == ManagedHttpClientState.Failed &&
              sinkFailureClient.Progress.State == ManagedHttpTransferState.Failed &&
              sinkFailureClient.Progress.TerminalFailureReason ==
                  ManagedHttpTerminalFailureReason.SinkFailure &&
              sinkFailureClient.Progress.BufferedBodyBytes == sinkFailureBuffered,
              "http-sink-failure-terminal");

        HttpFixtureBackend tooLargeBackend = new(Ascii(
            "HTTP/1.1 200 OK\r\nContent-Length: 1025\r\n\r\n"));
        ManagedNetworkService tooLargeService =
            ManagedNetworkService.CreateForTests(tooLargeBackend);
        tooLargeBackend.Attach(tooLargeService);
        ManagedHttpClient tooLarge = new(tooLargeService, 1024, false);
        Check(tooLarge.BeginGet("phase38.test"u8, "/too-large"u8) ==
                  NetworkOperationResult.Started, "http-too-large-begin");
        for (int poll = 0; poll != 20 &&
             tooLarge.State != ManagedHttpClientState.Failed; ++poll)
            Check(tooLarge.Poll() == NetworkOperationResult.Success ||
                  tooLarge.State == ManagedHttpClientState.Failed,
                  "http-too-large-poll-" + poll);
        Check(tooLarge.State == ManagedHttpClientState.Failed &&
              tooLarge.Progress.State == ManagedHttpTransferState.Failed &&
              tooLarge.Progress.TerminalFailureReason ==
                  ManagedHttpTerminalFailureReason.BodyTooLarge,
              "http-too-large-distinct-terminal");
    }

    private static void TestHttpsCancellationParity()
    {
        HttpFixtureBackend backend = new(null);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        ManagedSecureRandom random = new(new FixedEntropy(
            ManagedTls12Phase31Fixtures.ClientRandom));
        ManagedHttpsClient client = new(service, ManagedTls12Phase31Fixtures.Root,
            in TestTime, random, ManagedHttpLimits.MaximumStreamedBodyLength);
        Check(client.BeginGet("www.example.com"u8, "/phase38"u8) ==
                  NetworkOperationResult.Started, "https-cancel-begin");
        for (int poll = 0; poll != 8 && client.State != ManagedHttpsClientState.Handshaking;
             ++poll)
            Check(client.Poll() == NetworkOperationResult.Success,
                  "https-pre-header-poll-" + poll);
        Check(client.Cancel() == NetworkOperationResult.Success &&
              client.State == ManagedHttpsClientState.Cancelled &&
              client.Progress.State == ManagedHttpTransferState.Cancelled &&
              client.Progress.TerminalFailureReason ==
                  ManagedHttpTerminalFailureReason.Cancelled &&
              backend.TeardownCount != 0, "https-cancel-terminal-parity");
        CountingSink sink = new();
        Check(client.ConsumeResponseBody(sink) == ManagedHttpBodyDeliveryResult.Cancelled &&
              sink.Calls == 0 && client.Cancel() == NetworkOperationResult.Success,
              "https-cancel-no-late-delivery-and-idempotent");
        Check(client.Reset() == NetworkOperationResult.Success &&
              client.Progress.State == ManagedHttpTransferState.Idle,
              "https-cancel-reset-parity");
    }

    private static ManagedHttpResponseParser NewStreamingParser() =>
        new(ManagedHttpLimits.MaximumStreamedBodyLength, false, true);

    private static bool Drive(ManagedHttpResponseParser parser, byte[] source,
                              int offeredSegment, IManagedHttpBodySink sink,
                              out int peak)
    {
        peak = 0;
        int offset = 0;
        while (offset != source.Length || parser.HasPendingBody)
        {
            peak = Math.Max(peak, parser.BufferedBodyLength);
            if (parser.IsBodyDeliveryPaused)
            {
                int available = source.Length - offset;
                if (available != 0)
                {
                    Check(parser.TryFeed(source.AsSpan(offset,
                                      Math.Min(available, offeredSegment)),
                                          out int retry) && retry == 0,
                          "drive-paused-source-not-consumed");
                }
            }
            if (parser.HasPendingBody)
            {
                ManagedHttpBodyDeliveryResult delivery = parser.ConsumeBody(sink);
                if (delivery == ManagedHttpBodyDeliveryResult.Failed) return false;
                peak = Math.Max(peak, parser.BufferedBodyLength);
                if (delivery == ManagedHttpBodyDeliveryResult.Paused) continue;
            }
            if (offset == source.Length) continue;
            if (parser.IsBodyDeliveryPaused) continue;
            int offered = Math.Min(offeredSegment, source.Length - offset);
            if (!parser.TryFeed(source.AsSpan(offset, offered), out int consumed))
                return false;
            offset += consumed;
            peak = Math.Max(peak, parser.BufferedBodyLength);
            if (consumed == 0 && !parser.HasPendingBody &&
                !parser.IsBodyDeliveryPaused) return false;
        }
        while (parser.HasPendingBody)
        {
            ManagedHttpBodyDeliveryResult delivery = parser.ConsumeBody(sink);
            if (delivery != ManagedHttpBodyDeliveryResult.Delivered) return false;
        }
        peak = Math.Max(peak, parser.BufferedBodyLength);
        return parser.IsBodyComplete;
    }

    private static bool HashEquals(byte[] expected, FlowSink sink)
    {
        byte[] actual = new byte[ManagedSha256.DigestSize];
        byte[] expectedHash = new byte[ManagedSha256.DigestSize];
        return sink.IsExact && sink.TryFinalize(actual) &&
               ManagedSha256.TryHash(expected, expectedHash) &&
               actual.AsSpan().SequenceEqual(expectedHash);
    }

    private static byte[] Chunked(byte[] body, int[] sizes)
    {
        List<byte> result = new();
        AddAscii(result, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n" +
                         "Connection: close\r\n\r\n");
        int offset = 0;
        int sizeIndex = 0;
        while (offset != body.Length)
        {
            int count = Math.Min(sizes[sizeIndex++ % sizes.Length],
                                 body.Length - offset);
            AddAscii(result, count.ToString("X") + "\r\n");
            for (int index = 0; index != count; ++index)
                result.Add(body[offset + index]);
            AddAscii(result, "\r\n");
            offset += count;
        }
        AddAscii(result, "0\r\nX-Phase38: bounded\r\n\r\n");
        return result.ToArray();
    }

    private static byte[] Pattern(int length)
    {
        byte[] value = new byte[length];
        for (int index = 0; index != length; ++index)
            value[index] = (byte)((index * 31 + 7) & 0xFF);
        return value;
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        byte[] value = new byte[first.Length + second.Length];
        first.CopyTo(value, 0);
        second.CopyTo(value, first.Length);
        return value;
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void AddAscii(List<byte> destination, string value) =>
        destination.AddRange(Ascii(value));

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException("failed: " + name);
    }

    private sealed class FlowSink : IManagedHttpBodySink
    {
        private readonly byte[] _expected;
        private readonly ManagedSha256 _hash = new();
        private readonly int _pauseBeforeAccept;
        private readonly int _pauseAfterAccepted;
        private readonly bool _pauseEveryAccepted;
        private int _pauseCalls;
        private bool _pauseAfterIssued;
        private bool _pauseNext;
        private int _offset;

        internal FlowSink(byte[] expected, int pauseBeforeAccept = 0,
                          int pauseAfterAccepted = -1,
                          bool pauseEveryAccepted = false)
        {
            _expected = expected;
            _pauseBeforeAccept = pauseBeforeAccept;
            _pauseAfterAccepted = pauseAfterAccepted;
            _pauseEveryAccepted = pauseEveryAccepted;
        }

        internal bool IsExact => _offset == _expected.Length;
        internal int Calls { get; private set; }
        internal int AcceptedSegments { get; private set; }

        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            Calls++;
            if (_pauseCalls < _pauseBeforeAccept)
            {
                _pauseCalls++;
                return ManagedHttpBodySinkResult.Pause;
            }
            if (!_pauseAfterIssued && _pauseAfterAccepted >= 0 &&
                AcceptedSegments == _pauseAfterAccepted)
            {
                _pauseAfterIssued = true;
                return ManagedHttpBodySinkResult.Pause;
            }
            if (_pauseEveryAccepted && _pauseNext)
            {
                _pauseNext = false;
                return ManagedHttpBodySinkResult.Pause;
            }
            if (segment.Length > _expected.Length - _offset ||
                !segment.SequenceEqual(_expected.AsSpan(_offset, segment.Length)) ||
                !_hash.Append(segment))
                return ManagedHttpBodySinkResult.Fail;
            _offset += segment.Length;
            AcceptedSegments++;
            if (_pauseEveryAccepted) _pauseNext = true;
            return ManagedHttpBodySinkResult.Continue;
        }

        internal bool TryFinalize(Span<byte> destination) =>
            _hash.TryFinalize(destination);
    }

    private sealed class AlwaysPauseSink : IManagedHttpBodySink
    {
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment) =>
            ManagedHttpBodySinkResult.Pause;
    }

    private sealed class FailSink : IManagedHttpBodySink
    {
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment) =>
            ManagedHttpBodySinkResult.Fail;
    }

    private sealed class CountingSink : IManagedHttpBodySink
    {
        internal int Calls { get; private set; }

        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            Calls++;
            return ManagedHttpBodySinkResult.Continue;
        }
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;

        internal FixedEntropy(byte[] bytes) => _bytes = bytes;
        public bool IsAvailable => _bytes.Length != 0;
        public bool TryFill(Span<byte> destination)
        {
            for (int index = 0; index != destination.Length; ++index)
                destination[index] = _bytes[_offset++ % _bytes.Length];
            return true;
        }
    }

    private sealed class HttpFixtureBackend : IManagedNetworkServiceBackend
    {
        private readonly byte[]? _response;
        private readonly List<byte[]> _fragments = new();
        private ManagedNetworkService? _service;
        private ManagedNetworkServiceBackendEvent _event;
        private bool _eventPending;
        private ManagedTcpConnectionState _tcpState;
        private int _fragmentIndex;
        private bool _responseQueued;
        private bool _finQueued;

        internal HttpFixtureBackend(byte[]? response) => _response = response;
        internal int PollCount { get; private set; }
        internal int TeardownCount { get; private set; }
        internal void Attach(ManagedNetworkService service) => _service = service;

        public bool IsAvailable => true;
        public NetworkStatus GetStatus() => new(true, true, true, true,
            0x021500000002, Local, new Ipv4Address(0xFFFFFF00),
            new Ipv4Address(0x0A0F0001));
        public void SetRuntimeStatus(NetworkStatus status) { }

        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
            _event = ManagedNetworkServiceBackendEvent.DnsResolved;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool TryGetResolved(out Ipv4Address address)
        {
            address = Peer;
            return true;
        }

        public bool Poll(out ManagedNetworkServiceBackendEvent serviceEvent)
        {
            PollCount++;
            if (_tcpState == ManagedTcpConnectionState.LastAck)
                _tcpState = ManagedTcpConnectionState.TimeWait;
            if (_eventPending)
            {
                serviceEvent = _event;
                _eventPending = false;
                if (serviceEvent == ManagedNetworkServiceBackendEvent.TcpEstablished)
                    _tcpState = ManagedTcpConnectionState.Established;
                return true;
            }
            if (_fragmentIndex < _fragments.Count && _service != null)
            {
                byte[] fragment = _fragments[_fragmentIndex];
                if (!((IManagedTcpApplicationSink)_service).TryCaptureReceivedTcp(
                        Peer, Local, ManagedTcpConnection.ServerPort,
                        ManagedTcpConnection.ClientPort, fragment))
                {
                    serviceEvent = ManagedNetworkServiceBackendEvent.None;
                    return true;
                }
                _fragmentIndex++;
                serviceEvent = ManagedNetworkServiceBackendEvent.TcpReceived;
                return true;
            }
            if (_finQueued)
            {
                _finQueued = false;
                _tcpState = ManagedTcpConnectionState.CloseWait;
                serviceEvent = ManagedNetworkServiceBackendEvent.TcpClosed;
                return true;
            }
            serviceEvent = ManagedNetworkServiceBackendEvent.None;
            return true;
        }

        public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult BindUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port) =>
            ManagedNetworkServiceBackendResult.Rejected;
        public ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination,
            ushort destinationPort, ushort sourcePort, ReadOnlySpan<byte> payload) =>
            ManagedNetworkServiceBackendResult.NoResource;
        public ManagedTcpConnectionState TcpState => _tcpState;

        public ManagedNetworkServiceBackendResult BeginTcpConnect(
            Ipv4Address destination, ushort destinationPort)
        {
            _tcpState = ManagedTcpConnectionState.SynSent;
            _event = ManagedNetworkServiceBackendEvent.TcpEstablished;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
        {
            if (!_responseQueued && _response != null)
            {
                _responseQueued = true;
                for (int offset = 0; offset != _response.Length;)
                {
                    int length = Math.Min(512, _response.Length - offset);
                    _fragments.Add(_response.AsSpan(offset, length).ToArray());
                    offset += length;
                }
                _finQueued = true;
            }
            return ManagedNetworkServiceBackendResult.Success;
        }

        public ManagedNetworkServiceBackendResult CloseTcp()
        {
            _tcpState = ManagedTcpConnectionState.LastAck;
            return ManagedNetworkServiceBackendResult.Started;
        }

        public bool Teardown()
        {
            TeardownCount++;
            _tcpState = ManagedTcpConnectionState.Closed;
            _fragments.Clear();
            _fragmentIndex = 0;
            _finQueued = false;
            _responseQueued = false;
            return true;
        }
    }
}

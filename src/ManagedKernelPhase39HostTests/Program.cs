using System;
using System.Collections.Generic;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase39HostTests;

internal static class Program
{
    private static readonly Ipv4Address Local = new(0x0A0F0001U);
    private static readonly Ipv4Address Peer = new(0x0A0F0002U);
    private static readonly ManagedX509UtcTime TestTime =
        new(2028, 1, 1, 0, 0, 0);
    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestDestinationConsumer();
            TestCountConsumer();
            TestSha256Consumer();
            TestPrefixConsumer();
            TestCompositeConsumer();
            TestMetadataAndContentType();
            TestCancellationAndReset();
            TestEntityLimit();
            TestLongSequenceTlsRecord();
            TestHttpsParityAndApiShape();
            Console.WriteLine(
                $"MANAGED_KERNEL_PHASE39_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MANAGED_KERNEL_PHASE39_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestDestinationConsumer()
    {
        ManagedResourceDestinationConsumer empty =
            new(new byte[0]);
        Check(empty.Complete() && empty.BytesWritten == 0 &&
              empty.State == ManagedResourceConsumerState.Completed,
              "destination-empty-resource");

        byte[] oneDestination = new byte[1];
        ManagedResourceDestinationConsumer one =
            new(oneDestination);
        Check(Drive(CreateRequest(Response(new byte[] { 0xA5 })), one) &&
              one.BytesWritten == 1 && oneDestination[0] == 0xA5,
              "destination-one-byte-resource");

        byte[] body = Pattern(3_073);
        byte[] destination = new byte[body.Length];
        ManagedResourceDestinationConsumer exact =
            new(destination, 0, body.Length);
        Check(Drive(CreateRequest(Response(body)), exact) &&
              exact.BytesWritten == body.Length &&
              destination.AsSpan().SequenceEqual(body),
              "destination-exact-fit-over-window");

        byte[] canary = new byte[body.Length + 2];
        canary[0] = 0xCC;
        canary[^1] = 0xDD;
        ManagedResourceDestinationConsumer offset =
            new(canary, 1, body.Length);
        Check(Drive(CreateRequest(Response(body), 17), offset) &&
              offset.BytesWritten == body.Length && canary[0] == 0xCC &&
              canary[^1] == 0xDD &&
              canary.AsSpan(1, body.Length).SequenceEqual(body),
              "destination-multi-segment-no-overwrite");

        byte[] small = new byte[body.Length - 1];
        ManagedResourceRequest tooSmallRequest =
            CreateRequest(Response(body), 31);
        ManagedResourceDestinationConsumer tooSmall =
            new(small);
        Check(!Drive(tooSmallRequest, tooSmall) &&
              tooSmallRequest.State == ManagedResourceState.Failed &&
              tooSmallRequest.FailureReason ==
                  ManagedResourceFailureReason.DestinationFull &&
              tooSmall.FailureReason ==
                  ManagedResourceConsumerFailureReason.DestinationFull &&
              tooSmall.BytesWritten > 0 && tooSmall.BytesWritten < body.Length,
              "destination-one-byte-too-small-explicit-failure");

        byte[] guarded = new byte[10];
        guarded[0] = 0x11;
        guarded[^1] = 0x22;
        ManagedResourceDestinationConsumer guardedConsumer =
            new(guarded, 1, 8);
        Check(!Drive(CreateRequest(Response(Pattern(9)), 3), guardedConsumer) &&
              guarded[0] == 0x11 && guarded[^1] == 0x22 &&
              guardedConsumer.BytesWritten <= guardedConsumer.Capacity,
              "destination-failure-never-writes-outside-capacity");

        byte[] pausedBody = Pattern(5_123);
        HttpFixtureBackend backend;
        ManagedResourceRequest pausedRequest =
            CreateRequest(Response(pausedBody), 1, out backend);
        ManagedResourceDestinationConsumer pausedDestination =
            new(new byte[pausedBody.Length]);
        Check(pausedRequest.BeginGet("phase39.test"u8, "/destination"u8,
                                    pausedDestination) ==
              NetworkOperationResult.Started,
              "destination-pause-begin");
        int pollsBeforePause = backend.PollCount;
        Check(pausedRequest.Pause() == NetworkOperationResult.Success,
              "destination-pause-request");
        for (int index = 0; index != 8; ++index)
            Check(pausedRequest.Poll() == NetworkOperationResult.Success &&
                  pausedRequest.State == ManagedResourceState.Paused &&
                  backend.PollCount == pollsBeforePause,
                  "destination-pause-stable-" + index);
        Check(pausedRequest.Resume() == NetworkOperationResult.Success &&
              Drive(pausedRequest, pausedDestination) &&
              pausedDestination.BytesWritten == pausedBody.Length &&
              pausedRequest.Progress.PauseCount == 1 &&
              pausedRequest.Progress.ResumeCount == 1,
              "destination-pause-resume-preserves-write");

        ManagedResourceProgressSnapshot failure = tooSmallRequest.Progress;
        Check(failure.FailureReason == ManagedResourceFailureReason.DestinationFull &&
              failure.ParseFailureReason == ManagedHttpParseFailureReason.None &&
              failure.FailureReason != ManagedResourceFailureReason.BodyTooLarge,
              "destination-failure-distinct-from-body-limit");
    }

    private static void TestCountConsumer()
    {
        foreach (int length in new[] { 0, 1, 1_024, 1_025, 20_003 })
        {
            ManagedResourceCountConsumer count = new();
            Check(Drive(CreateRequest(Response(Pattern(length)), 1), count) &&
                  count.Count == length && count.BytesProcessed == length,
                  "count-resource-" + length);
        }

        byte[] chunkedBody = Pattern(12_345);
        ManagedResourceCountConsumer chunked = new();
        Check(Drive(CreateRequest(Chunked(chunkedBody), 2), chunked) &&
              chunked.Count == chunkedBody.Length,
              "count-chunked-fragmented");

        HttpFixtureBackend backend;
        ManagedResourceRequest pausedRequest =
            CreateRequest(Response(Pattern(7_777)), 23, out backend);
        ManagedResourceCountConsumer paused = new();
        Check(pausedRequest.BeginGet("phase39.test"u8, "/count"u8, paused) ==
              NetworkOperationResult.Started,
              "count-begin-for-pause");
        Check(DriveWithOnePause(pausedRequest, paused, backend) &&
              paused.Count == 7_777 && pausedRequest.Progress.PauseCount == 1,
              "count-pause-resume");

        ManagedResourceCountConsumer empty = new();
        Check(empty.Complete() && empty.Count == 0 &&
              empty.Consume("late"u8) == ManagedHttpBodySinkResult.Fail,
              "count-empty-completes-once");
        empty.Reset();
        Check(empty.State == ManagedResourceConsumerState.Idle &&
              empty.Count == 0,
              "count-reset-clears-state");
    }

    private static void TestSha256Consumer()
    {
        ManagedResourceSha256Consumer empty = new();
        Span<byte> digest = stackalloc byte[ManagedResourceSha256Consumer.DigestSize];
        Check(empty.Complete() && empty.IsFinalized && empty.TryCopyDigest(digest) &&
              digest.SequenceEqual(ConvertHex(
                  "E3B0C44298FC1C149AFBF4C8996FB924" +
                  "27AE41E4649B934CA495991B7852B855")),
              "sha256-known-empty");
        Check(empty.Complete() &&
              empty.Consume("after-finalize"u8) == ManagedHttpBodySinkResult.Fail,
              "sha256-finalize-exactly-once");

        ManagedResourceSha256Consumer abc = new();
        Check(abc.Consume("a"u8) == ManagedHttpBodySinkResult.Continue &&
              abc.Consume("bc"u8) == ManagedHttpBodySinkResult.Continue &&
              abc.Complete() && abc.TryCopyDigest(digest) &&
              digest.SequenceEqual(ConvertHex(
                  "BA7816BF8F01CFEA414140DE5DAE2223" +
                  "B00361A396177A9CB410FF61F20015AD")),
              "sha256-known-small-payload");

        byte[] body = Pattern(32_769);
        ManagedResourceSha256Consumer hash = new();
        Check(Drive(CreateRequest(Response(body), 97), hash) &&
              hash.BytesProcessed == body.Length && hash.IsFinalized &&
              hash.TryCopyDigest(digest) && DigestEquals(body, digest),
              "sha256-multi-window-body");

        ManagedResourceSha256Consumer chunkedHash = new();
        Check(Drive(CreateRequest(Chunked(body), 3), chunkedHash) &&
              chunkedHash.TryCopyDigest(digest) && DigestEquals(body, digest),
              "sha256-content-length-and-chunked-equal");

        byte[] fragmented = new byte[ManagedResourceSha256Consumer.DigestSize];
        ManagedResourceSha256Consumer fragmentationHash = new();
        Check(Drive(CreateRequest(Response(body), 1), fragmentationHash) &&
              fragmentationHash.TryCopyDigest(fragmented) &&
              fragmented.AsSpan().SequenceEqual(digest),
              "sha256-transport-fragmentation-independent");

        ManagedResourceSha256Consumer cancelled = new();
        ManagedResourceRequest cancelledRequest =
            CreateRequest(Response(body), 17);
        Check(cancelledRequest.BeginGet("phase39.test"u8, "/hash"u8, cancelled) ==
              NetworkOperationResult.Started,
              "sha256-cancel-begin");
        Check(cancelledRequest.Poll() == NetworkOperationResult.Success &&
              cancelledRequest.Cancel() == NetworkOperationResult.Success &&
              !cancelled.IsFinalized && !cancelled.TryCopyDigest(digest) &&
              cancelledRequest.Progress.State == ManagedResourceState.Cancelled,
              "sha256-cancel-is-partial-not-success");
    }

    private static void TestPrefixConsumer()
    {
        ManagedResourcePrefixConsumer zero = new(0);
        Check(Drive(CreateRequest(Response(Pattern(2_049)), 1), zero) &&
              zero.Capacity == 0 && zero.CapturedLength == 0 &&
              zero.BytesProcessed == 2_049,
              "prefix-zero-capacity-discards");

        byte[] body = Pattern(2_049);
        ManagedResourcePrefixConsumer prefix = new(73);
        Check(Drive(CreateRequest(Response(body), 2), prefix) &&
              prefix.CapturedLength == 73 && prefix.IsFull &&
              prefix.BytesProcessed == body.Length,
              "prefix-smaller-than-resource-continues");
        Span<byte> captured = stackalloc byte[73];
        Check(prefix.TryCopyPrefix(captured, out int capturedLength) &&
              capturedLength == 73 && captured.SequenceEqual(body.AsSpan(0, 73)),
              "prefix-first-bytes-exact");

        foreach (int capacity in new[] { 0, 1, body.Length, body.Length + 1 })
        {
            byte[] buffer = new byte[capacity];
            ManagedResourcePrefixConsumer current = new(buffer);
            Check(Drive(CreateRequest(Response(body), 11), current) &&
                  current.CapturedLength == Math.Min(capacity, body.Length),
                  "prefix-capacity-bound-" + capacity);
        }

        ManagedResourcePrefixConsumer segmented = new(1_025);
        Check(Drive(CreateRequest(Chunked(body), 1), segmented) &&
              segmented.CapturedLength == 1_025,
              "prefix-multi-segment");
        Span<byte> exact = stackalloc byte[1_025];
        Check(segmented.TryCopyPrefix(exact, out int exactLength) &&
              exactLength == 1_025 && exact.SequenceEqual(body.AsSpan(0, 1_025)),
              "prefix-multi-segment-content");
        byte before = exact[0];
        Check(segmented.Consume("different"u8) == ManagedHttpBodySinkResult.Fail &&
              exact[0] == before,
              "prefix-completed-consumer-rejects-late-data");

        ManagedResourcePrefixConsumer direct = new(4);
        Check(direct.Consume("abcdef"u8) == ManagedHttpBodySinkResult.Continue &&
              direct.BytesProcessed == 6 && direct.CapturedLength == 4 &&
              direct.Complete() && direct.Complete(),
              "prefix-after-prefix-discard-and-complete");
    }

    private static void TestCompositeConsumer()
    {
        byte[] body = Pattern(8_197);
        byte[] destination = new byte[body.Length];
        ManagedResourceDestinationConsumer destinationConsumer =
            new(destination);
        ManagedResourceCountConsumer count = new();
        ManagedResourcePrefixConsumer prefix = new(31);
        ManagedResourceSha256Consumer hash = new();
        ManagedResourceCompositeConsumer composite =
            new(count, prefix, hash, destinationConsumer);
        ManagedResourceRequest request = CreateRequest(Response(body), 7);
        Check(request.BeginGet("phase39.test"u8, "/composite"u8, composite) ==
              NetworkOperationResult.Started && Drive(request, composite) &&
              composite.BytesProcessed == body.Length && count.Count == body.Length &&
              destinationConsumer.BytesWritten == body.Length &&
              destination.AsSpan().SequenceEqual(body),
              "composite-count-prefix-hash-destination");
        Span<byte> digest = stackalloc byte[ManagedResourceSha256Consumer.DigestSize];
        Span<byte> expectedPrefix = stackalloc byte[31];
        Check(hash.TryCopyDigest(digest) && DigestEquals(body, digest) &&
              prefix.TryCopyPrefix(expectedPrefix, out int prefixLength) &&
              prefixLength == 31 && expectedPrefix.SequenceEqual(body.AsSpan(0, 31)),
              "composite-components-observe-identical-order");

        PauseOnceConsumer pause = new();
        ManagedResourceCountConsumer pauseCount = new();
        ManagedResourceCompositeConsumer pausingComposite =
            new(pause, pauseCount);
        ManagedResourceRequest pauseRequest =
            CreateRequest(Response(Pattern(2_000)), 512);
        Check(pauseRequest.BeginGet("phase39.test"u8, "/composite-pause"u8,
                                   pausingComposite) == NetworkOperationResult.Started &&
              Drive(pauseRequest, pausingComposite) && pause.Calls > 1 &&
              pauseCount.Count == 2_000,
              "composite-first-component-pause-propagates");

        FailConsumer failFirst = new();
        RecordingConsumer notCalled = new();
        ManagedResourceCompositeConsumer failed =
            new(failFirst, notCalled);
        Check(failed.Consume("x"u8) == ManagedHttpBodySinkResult.Fail &&
              notCalled.Calls == 0 &&
              failed.FailureReason ==
                  ManagedResourceConsumerFailureReason.ConsumerFailure,
              "composite-failure-stops-later-components");

        ManagedResourceCompositeConsumer latePause =
            new(new ManagedResourceCountConsumer(), new PauseOnceConsumer());
        Check(latePause.Consume("x"u8) == ManagedHttpBodySinkResult.Fail &&
              latePause.FailureReason ==
                  ManagedResourceConsumerFailureReason.ComponentPauseAfterAcceptance,
              "composite-late-pause-is-explicit-not-duplicated");
    }

    private static void TestMetadataAndContentType()
    {
        Check(ParseHeaders("Content-Type: text/plain\r\n").ContentTypeState ==
              ManagedHttpContentTypeState.Available &&
              ContentTypeEquals(ParseHeaders("Content-Type: text/plain\r\n"),
                                "text/plain"u8),
              "metadata-content-type-text-plain");
        Check(ContentTypeEquals(ParseHeaders("Content-Type: text/html\r\n"),
                                "text/html"u8),
              "metadata-content-type-text-html");
        Check(ContentTypeEquals(ParseHeaders(
                  "Content-Type: application/octet-stream\r\n"),
                                "application/octet-stream"u8),
              "metadata-content-type-octet-stream");
        Check(ContentTypeEquals(ParseHeaders(
                  "Content-Type: text/plain; charset=utf-8\r\n"),
                                "text/plain; charset=utf-8"u8),
              "metadata-content-type-charset");

        ManagedHttpResponseParser missing = ParseHeaders("X-Test: yes\r\n");
        Check(missing.ContentTypeState == ManagedHttpContentTypeState.Missing &&
              !missing.TryCopyContentType(new byte[64], out _),
              "metadata-content-type-missing");

        ManagedHttpResponseParser fragmented = ParseHeaders(
            "Content-Type: text/html; charset=utf-8\r\n", 1);
        Check(ContentTypeEquals(fragmented, "text/html; charset=utf-8"u8),
              "metadata-content-type-fragmented");

        ManagedHttpResponseParser duplicate = ParseHeaders(
            "Content-Type: text/plain\r\nContent-Type: text/html\r\n");
        Check(ContentTypeEquals(duplicate, "text/html"u8),
              "metadata-content-type-duplicate-last-value");

        string maximum = new('x', ManagedHttpLimits.MaximumContentTypeLength);
        ManagedHttpResponseParser atLimit = ParseHeaders(
            "Content-Type: " + maximum + "\r\n");
        Check(atLimit.ContentTypeState == ManagedHttpContentTypeState.Available &&
              atLimit.ContentTypeLength == ManagedHttpLimits.MaximumContentTypeLength,
              "metadata-content-type-maximum-bound");
        ManagedHttpResponseParser overLimit = ParseHeaders(
            "Content-Type: " + maximum + "x\r\n");
        Check(overLimit.ContentTypeState == ManagedHttpContentTypeState.TooLong &&
              overLimit.ContentTypeLength == 0 &&
              !overLimit.TryCopyContentType(new byte[64], out _),
              "metadata-content-type-one-byte-over-bound");

        byte[] body = Pattern(2_049);
        ManagedResourceCountConsumer count = new();
        ManagedResourceRequest request = CreateRequest(Response(
            body, "text/plain; charset=utf-8"), 13);
        Check(Drive(request, count) && request.Progress.StatusCode == 200 &&
              request.Progress.HasKnownTotalLength &&
              request.Progress.TotalEntityLength == body.Length &&
              request.Progress.TransferMode == ManagedHttpFramingMode.ContentLength &&
              request.Progress.ReceivedBytes == body.Length &&
              request.Progress.DeliveredBytes == body.Length &&
              request.Progress.ResourceBytesProcessed == body.Length &&
              request.Progress.ContentTypeState ==
                  ManagedHttpContentTypeState.Available &&
              request.TryCopyContentType(new byte[64], out int typeLength) &&
              typeLength == "text/plain; charset=utf-8"u8.Length,
              "metadata-resource-snapshot-content-length");
        ManagedResourceProgressSnapshot first = request.Progress;
        ManagedResourceProgressSnapshot second = request.Progress;
        Check(first.StatusCode == second.StatusCode &&
              first.ReceivedBytes == second.ReceivedBytes &&
              first.State == second.State &&
              first.ContentTypeState == second.ContentTypeState,
              "metadata-snapshot-read-does-not-mutate");

        ManagedResourceCountConsumer chunkedCount = new();
        ManagedResourceRequest chunkedRequest =
            CreateRequest(Chunked(body, "application/octet-stream"), 5);
        Check(Drive(chunkedRequest, chunkedCount) &&
              chunkedRequest.Progress.TransferMode == ManagedHttpFramingMode.Chunked &&
              !chunkedRequest.Progress.HasKnownTotalLength &&
              chunkedRequest.Progress.TotalEntityLength == 0 &&
              chunkedRequest.Progress.ResourceBytesProcessed == body.Length,
              "metadata-chunked-unknown-length");
    }

    private static void TestCancellationAndReset()
    {
        byte[] body = Pattern(9_001);
        ManagedResourceCountConsumer consumer = new();
        ManagedResourceRequest request = CreateRequest(Response(body), 17);
        Check(request.BeginGet("phase39.test"u8, "/cancel"u8, consumer) ==
              NetworkOperationResult.Started &&
              request.Cancel() == NetworkOperationResult.Success &&
              request.Progress.State == ManagedResourceState.Cancelled &&
              request.Progress.FailureReason == ManagedResourceFailureReason.Cancelled,
              "cancel-before-headers");
        int countAfterCancel = consumer.Count;
        Check(request.Poll() == NetworkOperationResult.Success &&
              consumer.Count == countAfterCancel,
              "cancel-before-headers-no-late-consumer-call");

        Check(request.Reset() == NetworkOperationResult.Success &&
              request.State == ManagedResourceState.Idle && consumer.Count == 0,
              "cancel-reset-clears-counters");
        Check(request.BeginGet("phase39.test"u8, "/reuse"u8, consumer) ==
              NetworkOperationResult.Started && Drive(request, consumer) &&
              consumer.Count == body.Length,
              "second-resource-after-reset-succeeds");

        ManagedResourceCountConsumer afterChunks = new();
        ManagedResourceRequest chunkRequest = CreateRequest(Response(body), 1);
        Check(chunkRequest.BeginGet("phase39.test"u8, "/cancel-late"u8,
                                   afterChunks) == NetworkOperationResult.Started,
              "cancel-after-chunks-begin");
        for (int index = 0; index != 100 && afterChunks.Count == 0; ++index)
            Check(chunkRequest.Poll() == NetworkOperationResult.Success,
                  "cancel-after-chunks-poll-" + index);
        Check(afterChunks.Count != 0 &&
              chunkRequest.Cancel() == NetworkOperationResult.Success,
              "cancel-after-several-chunks");
        int lateCount = afterChunks.Count;
        Check(chunkRequest.Poll() == NetworkOperationResult.Success &&
              afterChunks.Count == lateCount &&
              chunkRequest.Progress.State == ManagedResourceState.Cancelled,
              "cancel-after-chunks-no-late-delivery");

        ManagedResourceCountConsumer pausedConsumer = new();
        ManagedResourceRequest pausedRequest =
            CreateRequest(Response(Pattern(4_000)), 512);
        Check(pausedRequest.BeginGet("phase39.test"u8, "/cancel-paused"u8,
                                    pausedConsumer) == NetworkOperationResult.Started &&
              pausedRequest.Pause() == NetworkOperationResult.Success &&
              pausedRequest.Cancel() == NetworkOperationResult.Success &&
              pausedRequest.Progress.State == ManagedResourceState.Cancelled &&
              pausedRequest.Poll() == NetworkOperationResult.Success &&
              pausedConsumer.Count == 0,
              "cancel-while-resource-paused");
    }

    private static void TestEntityLimit()
    {
        byte[] exact = Pattern(ManagedHttpLimits.MaximumStreamedBodyLength);
        ManagedResourceCountConsumer count = new();
        HttpFixtureBackend exactBackend;
        ManagedResourceRequest exactRequest = CreateRequest(Response(exact), 512,
                                                            out exactBackend);
        bool exactResult = Drive(exactRequest, count, 20_000);
        Check(exactResult && count.Count == ManagedHttpLimits.MaximumStreamedBodyLength,
              "streaming-entity-limit-exactly-accepted state=" + exactRequest.State +
              " failure=" + exactRequest.FailureReason + " count=" + count.Count +
              " received=" + exactRequest.Progress.ReceivedBytes +
              " delivered=" + exactRequest.Progress.DeliveredBytes +
              " polls=" + exactBackend.PollCount +
              " transport=" + exactRequest.Progress.HttpFailureReason +
              " sends=" + exactBackend.SendCount +
              " fragments=" + exactBackend.FragmentCount +
              " fragmentIndex=" + exactBackend.FragmentIndex +
              " backendTcp=" + exactBackend.BackendTcpState);

        ManagedHttpResponseParser oneBeyond = NewStreamingParser();
        Check(!oneBeyond.TryFeed(Ascii(
                  "HTTP/1.1 200 OK\r\nContent-Length: 1048577\r\n\r\n"),
                  out _) && oneBeyond.FailureReason ==
                  ManagedHttpParseFailureReason.BodyTooLarge,
              "streaming-entity-limit-one-byte-over-rejected");

        ManagedResourceCountConsumer largeConsumer = new();
        ManagedResourceRequest largeRequest =
            CreateLimitedRequest(Response(Pattern(1_025)), 1_024);
        Check(!Drive(largeRequest, largeConsumer) &&
              largeRequest.FailureReason == ManagedResourceFailureReason.BodyTooLarge &&
              largeRequest.Progress.ConsumerFailureReason ==
                  ManagedResourceConsumerFailureReason.None,
              "count-consumer-cannot-bypass-transport-limit");

        ManagedResourceSha256Consumer largeHash = new();
        ManagedResourceRequest hashRequest =
            CreateLimitedRequest(Response(Pattern(1_025)), 1_024);
        Check(!Drive(hashRequest, largeHash) &&
              hashRequest.FailureReason == ManagedResourceFailureReason.BodyTooLarge &&
              !largeHash.IsFinalized,
              "hash-consumer-cannot-bypass-transport-limit");
    }

    private static void TestHttpsParityAndApiShape()
    {
        HttpFixtureBackend backend = new(null);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        ManagedSecureRandom random = new(new FixedEntropy(CreateEntropy()));
        ManagedResourceRequest request = new(service, ManagedTls12Phase31Fixtures.Root,
                                             in TestTime, random,
                                             ManagedHttpLimits.MaximumStreamedBodyLength,
                                             compactTlsProfile: false);
        ManagedResourceCountConsumer consumer = new();
        Check(request.Protocol == ManagedResourceProtocol.Https &&
              request.BeginGet("phase39.test"u8, "/https"u8, consumer) ==
                  NetworkOperationResult.Started,
              "https-resource-begins-through-same-consumer-api");
        Check(request.Cancel() == NetworkOperationResult.Success &&
              request.Progress.State == ManagedResourceState.Cancelled &&
              request.Progress.HttpsFailureReason ==
                  ManagedHttpsFailureReason.Cancelled &&
              request.Progress.FailureReason == ManagedResourceFailureReason.Cancelled,
              "https-resource-cancel-parity");
        Check(request.Reset() == NetworkOperationResult.Success &&
              request.State == ManagedResourceState.Idle,
              "https-resource-reset-parity");

        ManagedResourceRequest http = new(service, 1_024);
        Check(http.Protocol == ManagedResourceProtocol.Http &&
              http.MaximumEntityLength == 1_024,
              "http-resource-bounded-limit-api");
    }

    private static void TestLongSequenceTlsRecord()
    {
        byte[] record = new byte[]
        {
            0x17, 0x03, 0x03, 0x00, 0x23, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0xD7, 0x05, 0x94, 0x85, 0x8B, 0xA5,
            0x09, 0xD7, 0x63, 0x62, 0xDF, 0x1F, 0x88, 0xF3, 0xDD,
            0x13, 0x23, 0xCE, 0x11, 0x1A, 0x64, 0x76, 0x47, 0x61,
            0x30, 0x75, 0x29, 0x17
        };
        byte[] plaintext = new byte[11];
        Check(ManagedTls12RecordProtection.TryDecrypt(
                  0xD7,
                  ManagedTls12Phase31Fixtures.KeyBlock.AsSpan(16, 16),
                  ManagedTls12Phase31Fixtures.KeyBlock.AsSpan(36, 4),
                  ManagedTls12RecordProtection.ApplicationData,
                  record,
                  plaintext,
                  out int written) && written == plaintext.Length,
              "tls-application-record-sequence-d7");
        Check(plaintext.AsSpan().SequenceEqual(
                  new byte[] { 0xDA, 0xF9, 0x18, 0x37, 0x56, 0x75,
                               0x94, 0xB3, 0xD2, 0xF1, 0x10 }),
              "tls-application-record-sequence-d7-plaintext");
    }

    private static ManagedResourceRequest CreateRequest(byte[] response,
                                                         int fragmentSize = 512) =>
        CreateRequest(response, fragmentSize, out _);

    private static ManagedResourceRequest CreateRequest(byte[] response,
                                                         int fragmentSize,
                                                         out HttpFixtureBackend backend)
    {
        backend = new HttpFixtureBackend(response, fragmentSize);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        return new ManagedResourceRequest(service,
            ManagedHttpLimits.MaximumStreamedBodyLength);
    }

    private static ManagedResourceRequest CreateLimitedRequest(byte[] response,
                                                                int maximumEntityLength)
    {
        HttpFixtureBackend backend = new(response, 512);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        return new ManagedResourceRequest(service, maximumEntityLength);
    }

    private static bool Drive(ManagedResourceRequest request,
                              IManagedResourceConsumer consumer,
                              int maximumPolls = 100_000)
    {
        if (request.State == ManagedResourceState.Idle &&
            request.BeginGet("phase39.test"u8, "/drive"u8, consumer) !=
                NetworkOperationResult.Started)
            return false;
        for (int poll = 0; poll != maximumPolls; ++poll)
        {
            NetworkOperationResult result = request.Poll();
            if (result == NetworkOperationResult.Failed ||
                request.State == ManagedResourceState.Failed)
                return false;
            if (request.State == ManagedResourceState.Completed)
                return consumer.State == ManagedResourceConsumerState.Completed;
            if (request.State == ManagedResourceState.Paused)
            {
                request.Resume();
            }
        }
        return false;
    }

    private static bool DriveWithOnePause(ManagedResourceRequest request,
                                          IManagedResourceConsumer consumer,
                                          HttpFixtureBackend backend)
    {
        bool paused = false;
        for (int poll = 0; poll != 100_000; ++poll)
        {
            if (!paused && consumer.BytesProcessed != 0)
            {
                int before = backend.PollCount;
                if (request.Pause() != NetworkOperationResult.Success) return false;
                for (int stable = 0; stable != 4; ++stable)
                    if (request.Poll() != NetworkOperationResult.Success ||
                        backend.PollCount != before)
                        return false;
                if (request.Resume() != NetworkOperationResult.Success) return false;
                paused = true;
            }
            NetworkOperationResult result = request.Poll();
            if (result == NetworkOperationResult.Failed ||
                request.State == ManagedResourceState.Failed)
                return false;
            if (request.State == ManagedResourceState.Completed)
                return consumer.State == ManagedResourceConsumerState.Completed && paused;
        }
        return false;
    }

    private static ManagedHttpResponseParser ParseHeaders(string headers,
                                                           int fragmentSize = 512)
    {
        ManagedHttpResponseParser parser = NewStreamingParser();
        byte[] response = Ascii("HTTP/1.1 200 OK\r\n" + headers + "\r\n");
        int offset = 0;
        while (offset != response.Length)
        {
            int offered = Math.Min(fragmentSize, response.Length - offset);
            Check(parser.TryFeed(response.AsSpan(offset, offered), out int consumed),
                  "metadata-header-feed");
            Check(consumed != 0 || parser.IsBodyComplete,
                  "metadata-header-progress");
            offset += consumed;
        }
        Check(parser.NotifyConnectionClosed(), "metadata-header-close");
        Check(parser.IsBodyComplete, "metadata-header-complete");
        return parser;
    }

    private static bool ContentTypeEquals(ManagedHttpResponseParser parser,
                                          ReadOnlySpan<byte> expected)
    {
        byte[] copied = new byte[ManagedHttpLimits.MaximumContentTypeLength];
        return parser.TryCopyContentType(copied, out int length) &&
               length == expected.Length && copied.AsSpan(0, length).SequenceEqual(expected);
    }

    private static bool DigestEquals(ReadOnlySpan<byte> body,
                                    ReadOnlySpan<byte> actual)
    {
        Span<byte> expected = stackalloc byte[ManagedResourceSha256Consumer.DigestSize];
        return ManagedSha256.TryHash(body, expected) && actual.SequenceEqual(expected);
    }

    private static ManagedHttpResponseParser NewStreamingParser() =>
        new(ManagedHttpLimits.MaximumStreamedBodyLength, false, true);

    private static byte[] Response(byte[] body, string? contentType = null)
    {
        string header = "HTTP/1.1 200 OK\r\nContent-Length: " + body.Length +
                        "\r\nConnection: close\r\n" +
                        (contentType == null ? string.Empty :
                         "Content-Type: " + contentType + "\r\n") + "\r\n";
        return Combine(Ascii(header), body);
    }

    private static byte[] Chunked(byte[] body, string? contentType = null)
    {
        List<byte> result = new();
        AddAscii(result, "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n" +
                         "Connection: close\r\n" +
                         (contentType == null ? string.Empty :
                          "Content-Type: " + contentType + "\r\n") + "\r\n");
        for (int offset = 0; offset != body.Length;)
        {
            int length = Math.Min(4_096, body.Length - offset);
            AddAscii(result, length.ToString("X") + "\r\n");
            for (int index = 0; index != length; ++index)
                result.Add(body[offset + index]);
            AddAscii(result, "\r\n");
            offset += length;
        }
        AddAscii(result, "0\r\n\r\n");
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
        byte[] result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    private static byte[] ConvertHex(string value)
    {
        byte[] result = new byte[value.Length / 2];
        for (int index = 0; index != result.Length; ++index)
            result[index] = (byte)((Hex(value[index * 2]) << 4) |
                                   Hex(value[index * 2 + 1]));
        return result;
    }

    private static int Hex(char value) => value <= '9' ? value - '0' :
        value <= 'F' ? value - 'A' + 10 : value - 'a' + 10;

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static void AddAscii(List<byte> destination, string value) =>
        destination.AddRange(Ascii(value));

    private static byte[] CreateEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
    }

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException("failed: " + name);
    }

    private sealed class PauseOnceConsumer : IManagedResourceConsumer
    {
        private bool _paused;
        private ManagedResourceConsumerState _state;

        internal int Calls { get; private set; }
        public ManagedResourceConsumerState State => _state;
        public ManagedResourceConsumerFailureReason FailureReason =>
            ManagedResourceConsumerFailureReason.None;
        public int BytesProcessed { get; private set; }

        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            Calls++;
            if (!_paused)
            {
                _paused = true;
                _state = ManagedResourceConsumerState.Paused;
                return ManagedHttpBodySinkResult.Pause;
            }
            BytesProcessed += segment.Length;
            _state = ManagedResourceConsumerState.Receiving;
            return ManagedHttpBodySinkResult.Continue;
        }

        public bool Complete()
        {
            if (_state == ManagedResourceConsumerState.Cancelled) return false;
            _state = ManagedResourceConsumerState.Completed;
            return true;
        }

        public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;

        public void Reset()
        {
            _paused = false;
            BytesProcessed = 0;
            Calls = 0;
            _state = ManagedResourceConsumerState.Idle;
        }
    }

    private sealed class FailConsumer : IManagedResourceConsumer
    {
        public ManagedResourceConsumerState State =>
            ManagedResourceConsumerState.Failed;
        public ManagedResourceConsumerFailureReason FailureReason =>
            ManagedResourceConsumerFailureReason.ConsumerFailure;
        public int BytesProcessed => 0;
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment) =>
            ManagedHttpBodySinkResult.Fail;
        public bool Complete() => false;
        public void Cancel() { }
        public void Reset() { }
    }

    private sealed class RecordingConsumer : IManagedResourceConsumer
    {
        public int Calls { get; private set; }
        public ManagedResourceConsumerState State =>
            ManagedResourceConsumerState.Receiving;
        public ManagedResourceConsumerFailureReason FailureReason =>
            ManagedResourceConsumerFailureReason.None;
        public int BytesProcessed { get; private set; }
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            Calls++;
            BytesProcessed += segment.Length;
            return ManagedHttpBodySinkResult.Continue;
        }
        public bool Complete() => true;
        public void Cancel() { }
        public void Reset() { Calls = 0; BytesProcessed = 0; }
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
        private readonly int _fragmentSize;
        private readonly List<byte[]> _fragments = new();
        private ManagedNetworkService? _service;
        private ManagedNetworkServiceBackendEvent _event;
        private bool _eventPending;
        private ManagedTcpConnectionState _tcpState;
        private int _fragmentIndex;
        private bool _responseQueued;
        private bool _finQueued;

        internal HttpFixtureBackend(byte[]? response, int fragmentSize = 512)
        {
            _response = response;
            _fragmentSize = fragmentSize <= 0 ? 512 : fragmentSize;
        }

        internal int PollCount { get; private set; }
        internal int SendCount { get; private set; }
        internal int FragmentCount => _fragments.Count;
        internal int FragmentIndex => _fragmentIndex;
        internal ManagedTcpConnectionState BackendTcpState => _tcpState;
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
            SendCount++;
            if (!_responseQueued && _response != null)
            {
                _responseQueued = true;
                for (int offset = 0; offset != _response.Length;)
                {
                    int length = Math.Min(_fragmentSize, _response.Length - offset);
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
            _tcpState = ManagedTcpConnectionState.Closed;
            _fragments.Clear();
            _fragmentIndex = 0;
            _finQueued = false;
            _responseQueued = false;
            _eventPending = false;
            return true;
        }
    }
}

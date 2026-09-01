using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GuideXOS.Net10.ManagedKernel;

namespace GuideXOS.Net10.ManagedKernelPhase40HostTests;

internal static class Program
{
    private static int s_cases;

    private static int Main()
    {
        try
        {
            TestContentEncodingMetadata();
            TestGzipStoredAndOptionalFields();
            TestZlibStored();
            TestCompressedFixturesAndFragmentation();
            TestFailureClassification();
            TestResourceIntegration();
            Console.WriteLine($"MANAGED_KERNEL_PHASE40_HOST_TESTS_PASS cases={s_cases}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"MANAGED_KERNEL_PHASE40_HOST_TESTS_FAIL cases={s_cases} {exception}");
            return 1;
        }
    }

    private static void TestContentEncodingMetadata()
    {
        Check(ParseEncoding(null) == ManagedHttpContentEncodingState.Missing, "encoding-missing");
        Check(ParseEncoding("identity") == ManagedHttpContentEncodingState.Identity, "encoding-identity");
        Check(ParseEncoding(" GZIP ") == ManagedHttpContentEncodingState.Gzip, "encoding-gzip-case-whitespace");
        Check(ParseEncoding("deflate") == ManagedHttpContentEncodingState.Deflate, "encoding-deflate");
        Check(ParseEncoding("br") == ManagedHttpContentEncodingState.Unsupported, "encoding-br-unsupported");
        Check(ParseEncoding("compress") == ManagedHttpContentEncodingState.Unsupported, "encoding-compress-unsupported");
        Check(ParseEncoding("gzip, br") == ManagedHttpContentEncodingState.Malformed, "encoding-chain-rejected");
        Check(ParseEncoding("gzip", duplicate: true) == ManagedHttpContentEncodingState.Malformed, "encoding-duplicate-rejected");
        Check(ParseEncoding("gzip\t") == ManagedHttpContentEncodingState.Gzip, "encoding-trailing-tab");
        Check(ParseEncoding("g zip") == ManagedHttpContentEncodingState.Malformed, "encoding-inner-whitespace");
        Check(ParseEncoding(new string('x', ManagedContentEncodingLimits.MaximumContentEncodingLength)) ==
              ManagedHttpContentEncodingState.Unsupported, "encoding-maximum-value");
        Check(ParseEncoding(new string('x', ManagedContentEncodingLimits.MaximumContentEncodingLength + 1)) ==
              ManagedHttpContentEncodingState.TooLong, "encoding-one-over");
        Check(ParseEncoding("GZip", 1) == ManagedHttpContentEncodingState.Gzip, "encoding-fragmented");
    }

    private static void TestGzipStoredAndOptionalFields()
    {
        byte[] body = Ascii("hello");
        byte[] gzip = BuildGzipStored(body, false, false, false, false);
        Check(Decode(gzip, ManagedHttpContentEncodingState.Gzip, body, 1), "gzip-minimal");

        byte[] optional = BuildGzipStored(body, true, true, true, true);
        Check(Decode(optional, ManagedHttpContentEncodingState.Gzip, body, 1), "gzip-optional-fields");
        Check(Decode(gzip, ManagedHttpContentEncodingState.Gzip, body, gzip.Length), "gzip-one-segment");
        Check(!DecodeWithFailure(Mutate(gzip, 0, 0), ManagedHttpContentEncodingState.Gzip,
                                 ManagedContentDecoderFailureReason.MalformedGzipHeader), "gzip-bad-magic");
        Check(FailsWith(gzip[..^1], ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.TruncatedCompressedStream), "gzip-truncated");
        byte[] badCrc = (byte[])gzip.Clone();
        badCrc[^8] ^= 1;
        Check(FailsWith(badCrc, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.GzipCrcMismatch), "gzip-crc-mismatch");
        byte[] badSize = (byte[])gzip.Clone();
        badSize[^4] ^= 1;
        Check(FailsWith(badSize, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.GzipIsizeMismatch), "gzip-isize-mismatch");
        byte[] trailing = Combine(gzip, new byte[] { 0xA5 });
        Check(FailsWith(trailing, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.TrailingCompressedData), "gzip-trailing-data");
        Check(FailsWith(Combine(gzip, gzip), ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.TrailingCompressedData), "gzip-concatenated-rejected");
    }

    private static void TestZlibStored()
    {
        byte[] body = Ascii("hello");
        byte[] zlib = BuildZlibStored(body);
        Check(Decode(zlib, ManagedHttpContentEncodingState.Deflate, body, 1), "zlib-minimal");
        Check(Decode(zlib, ManagedHttpContentEncodingState.Deflate, body, zlib.Length), "zlib-one-segment");
        byte[] badHeader = (byte[])zlib.Clone();
        badHeader[1] ^= 1;
        Check(FailsWith(badHeader, ManagedHttpContentEncodingState.Deflate,
                        ManagedContentDecoderFailureReason.MalformedZlibHeader), "zlib-bad-header");
        byte[] badAdler = (byte[])zlib.Clone();
        badAdler[^1] ^= 1;
        Check(FailsWith(badAdler, ManagedHttpContentEncodingState.Deflate,
                        ManagedContentDecoderFailureReason.ZlibAdlerMismatch), "zlib-adler-mismatch");
        Check(FailsWith(new byte[] { 0x03, 0x00 }, ManagedHttpContentEncodingState.Deflate,
                        ManagedContentDecoderFailureReason.MalformedZlibHeader), "raw-deflate-rejected");
    }

    private static void TestCompressedFixturesAndFragmentation()
    {
        byte[] body = Pattern(70_000);
        byte[] gzip = CompressGzip(body);
        Check(Decode(gzip, ManagedHttpContentEncodingState.Gzip, body, 1), "gzip-dynamic-byte-fragments");
        Check(Decode(gzip, ManagedHttpContentEncodingState.Gzip, body, 137), "gzip-dynamic-fragments");
        Check(Decode(gzip, ManagedHttpContentEncodingState.Gzip, body, gzip.Length), "gzip-dynamic-whole");
        byte[] zlib = CompressZlib(body);
        Check(Decode(zlib, ManagedHttpContentEncodingState.Deflate, body, 1), "zlib-dynamic-byte-fragments");
        byte[] overlap = new byte[100_000];
        for (int index = 0; index != overlap.Length; ++index) overlap[index] = (byte)'A';
        Check(Decode(CompressGzip(overlap), ManagedHttpContentEncodingState.Gzip,
                     overlap, 11), "gzip-overlapping-history");
        byte[] decoded = DecodeBytes(gzip, ManagedHttpContentEncodingState.Gzip, 257);
        Check(decoded.AsSpan().SequenceEqual(body), "gzip-fragmentation-output");
        Check(Sha256(decoded).AsSpan().SequenceEqual(Sha256(body)), "gzip-fragmentation-sha256");

        byte[] compressible = new byte[ManagedContentEncodingLimits.OutputWindowSize * 4 + 17];
        for (int index = 0; index != compressible.Length; ++index) compressible[index] = (byte)'A';
        byte[] bomb = CompressGzip(compressible);
        Check(FailsWith(bomb, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.DecodedResourceLimitExceeded,
                        ManagedContentEncodingLimits.OutputWindowSize * 2), "decoded-limit");
        Check(FailsWith(gzip, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.DecodedResourceLimitExceeded, 1),
              "decoded-limit-one-byte-over");
    }

    private static void TestFailureClassification()
    {
        byte[] gzip = BuildGzipStored(Ascii("x"), false, false, false, false);
        byte[] reserved = (byte[])gzip.Clone();
        reserved[3] = 0x20;
        Check(FailsWith(reserved, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.MalformedGzipHeader), "gzip-reserved-flags");
        byte[] method = (byte[])gzip.Clone();
        method[2] = 0;
        Check(FailsWith(method, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.MalformedGzipHeader), "gzip-method");
        byte[] optionalTooLong = (byte[])gzip.Clone();
        optionalTooLong[3] = 4;
        optionalTooLong = Replace(optionalTooLong, 10, new byte[] { 0x01, 0x04 });
        Check(FailsWith(optionalTooLong, ManagedHttpContentEncodingState.Gzip,
                        ManagedContentDecoderFailureReason.GzipOptionalFieldTooLong), "gzip-extra-bound");
    }

    private static void TestResourceIntegration()
    {
        byte[] body = Pattern(9_777);
        byte[] encoded = CompressGzip(body);
        byte[] response = Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: " + encoded.Length +
                                      "\r\nContent-Type: application/octet-stream\r\n" +
                                      "Content-Encoding: GZIP\r\nConnection: close\r\n\r\n"), encoded);
        HttpFixtureBackend backend = new(response, 1);
        ManagedNetworkService service = ManagedNetworkService.CreateForTests(backend);
        backend.Attach(service);
        ManagedResourceRequest request = new(service, encoded.Length + 32, body.Length);
        ManagedResourceCountConsumer count = new();
        ManagedResourceSha256Consumer hash = new();
        ManagedResourcePrefixConsumer prefix = new(32);
        ManagedResourceCompositeConsumer composite = new(count, hash, prefix);
        Check(request.BeginGet("phase40.test"u8, "/gzip"u8, composite) ==
              NetworkOperationResult.Started, "resource-begin");
        bool complete = false;
        for (int poll = 0; poll != 200_000; ++poll)
        {
            Check(request.Poll() != NetworkOperationResult.Failed, "resource-poll");
            if (request.State == ManagedResourceState.Paused)
                Check(request.Resume() == NetworkOperationResult.Success, "resource-resume");
            if (request.State == ManagedResourceState.Completed)
            {
                complete = true;
                break;
            }
        }
        Check(complete && count.Count == body.Length && hash.BytesProcessed == body.Length,
              "resource-decoded-count-sha-length");
        Span<byte> digest = stackalloc byte[ManagedResourceSha256Consumer.DigestSize];
        Span<byte> expectedPrefix = stackalloc byte[32];
        body.AsSpan(0, 32).CopyTo(expectedPrefix);
        Check(hash.TryCopyDigest(digest) && digest.SequenceEqual(Sha256(body)),
              "resource-decoded-sha");
        byte[] copiedPrefix = new byte[32];
        Check(prefix.TryCopyPrefix(copiedPrefix, out int prefixLength) && prefixLength == 32 &&
              copiedPrefix.AsSpan().SequenceEqual(expectedPrefix), "resource-decoded-prefix");
        ManagedResourceProgressSnapshot progress = request.Progress;
        Check(progress.State == ManagedResourceState.Completed &&
              progress.ContentEncodingState == ManagedHttpContentEncodingState.Gzip &&
              progress.EncodedBytesReceived == encoded.Length &&
              progress.EncodedBytesConsumed == encoded.Length &&
              progress.DecodedBytesProduced == body.Length &&
              progress.ResourceBytesProcessed == body.Length &&
              progress.CrcValidated && progress.IsizeValidated &&
              progress.DecoderHistoryWindowSize == ManagedContentEncodingLimits.HistoryWindowSize,
              "resource-progress-decoded-semantics");

        HttpFixtureBackend pausedBackend = new(response, 3);
        ManagedNetworkService pausedService = ManagedNetworkService.CreateForTests(pausedBackend);
        pausedBackend.Attach(pausedService);
        ManagedResourceRequest pausedRequest = new(pausedService, encoded.Length + 32, body.Length);
        PausingCountConsumer pausing = new();
        ManagedResourceSha256Consumer pausedHash = new();
        ManagedResourceCompositeConsumer pausedComposite = new(pausing, pausedHash);
        Check(pausedRequest.BeginGet("phase40.test"u8, "/pause"u8, pausedComposite) ==
              NetworkOperationResult.Started, "resource-pause-begin");
        bool sawPause = false;
        for (int poll = 0; poll != 200_000; ++poll)
        {
            int before = pausedBackend.PollCount;
            Check(pausedRequest.Poll() != NetworkOperationResult.Failed, "resource-pause-poll");
            if (pausedRequest.State == ManagedResourceState.Paused)
            {
                sawPause = true;
                Check(pausedRequest.Poll() == NetworkOperationResult.Success &&
                      pausedBackend.PollCount == before, "resource-pause-stable");
                Check(pausedRequest.Resume() == NetworkOperationResult.Success, "resource-pause-resume");
            }
            if (pausedRequest.State == ManagedResourceState.Completed) break;
        }
        Check(sawPause && pausedRequest.State == ManagedResourceState.Completed &&
              pausing.BytesProcessed == body.Length && pausedHash.BytesProcessed == body.Length,
              "resource-pause-preserves-decoded-output");

        byte[] unsupportedResponse = Ascii("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n" +
                                           "Content-Encoding: br\r\nConnection: close\r\n\r\n");
        HttpFixtureBackend unsupportedBackend = new(unsupportedResponse, 2);
        ManagedNetworkService unsupportedService = ManagedNetworkService.CreateForTests(unsupportedBackend);
        unsupportedBackend.Attach(unsupportedService);
        ManagedResourceRequest unsupportedRequest = new(unsupportedService);
        ManagedResourceCountConsumer unsupportedCount = new();
        Check(unsupportedRequest.BeginGet("phase40.test"u8, "/br"u8, unsupportedCount) ==
              NetworkOperationResult.Started, "resource-unsupported-begin");
        for (int poll = 0; poll != 10_000 && unsupportedRequest.State != ManagedResourceState.Failed; ++poll)
            unsupportedRequest.Poll();
        Check(unsupportedRequest.FailureReason == ManagedResourceFailureReason.UnsupportedContentEncoding &&
              unsupportedRequest.Progress.DecoderFailureReason ==
                  ManagedContentDecoderFailureReason.UnsupportedEncoding &&
              unsupportedCount.BytesProcessed == 0, "resource-unsupported-distinct-failure");

        byte[] bombBody = new byte[4_097];
        for (int index = 0; index != bombBody.Length; ++index) bombBody[index] = (byte)'B';
        byte[] bombEncoded = CompressGzip(bombBody);
        byte[] bombResponse = Combine(Ascii("HTTP/1.1 200 OK\r\nContent-Length: " + bombEncoded.Length +
                                            "\r\nContent-Encoding: gzip\r\nConnection: close\r\n\r\n"), bombEncoded);
        HttpFixtureBackend bombBackend = new(bombResponse, 5);
        ManagedNetworkService bombService = ManagedNetworkService.CreateForTests(bombBackend);
        bombBackend.Attach(bombService);
        ManagedResourceRequest bombRequest = new(bombService, bombEncoded.Length, 128);
        ManagedResourceCountConsumer bombCount = new();
        Check(bombRequest.BeginGet("phase40.test"u8, "/bomb"u8, bombCount) ==
              NetworkOperationResult.Started, "resource-bomb-begin");
        for (int poll = 0; poll != 20_000 && bombRequest.State != ManagedResourceState.Failed; ++poll)
            bombRequest.Poll();
        Check(bombRequest.FailureReason == ManagedResourceFailureReason.DecodedResourceTooLarge &&
              bombRequest.Progress.DecoderFailureReason ==
                  ManagedContentDecoderFailureReason.DecodedResourceLimitExceeded &&
              bombCount.BytesProcessed == 0, "resource-bomb-explicit-limit");
    }

    private static ManagedHttpContentEncodingState ParseEncoding(string? encoding,
                                                                  int fragmentSize = 64,
                                                                  bool duplicate = false)
    {
        string header = "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n" +
                        (encoding == null ? string.Empty : "Content-Encoding:" + encoding + "\r\n") +
                        (duplicate ? "Content-Encoding:gzip\r\n" : string.Empty) + "\r\n";
        ManagedHttpResponseParser parser = new(ManagedHttpLimits.MaximumStreamedBodyLength, false, true);
        byte[] bytes = Encoding.ASCII.GetBytes(header);
        for (int offset = 0; offset != bytes.Length;)
        {
            int offered = Math.Min(fragmentSize, bytes.Length - offset);
            Check(parser.TryFeed(bytes.AsSpan(offset, offered), out int consumed), "metadata-feed");
            Check(consumed != 0 || parser.IsBodyComplete, "metadata-progress");
            offset += consumed;
        }
        Check(parser.IsBodyComplete, "metadata-complete");
        return parser.ContentEncodingState;
    }

    private static bool Decode(byte[] encoded, ManagedHttpContentEncodingState encoding,
                               byte[] expected, int fragmentSize)
    {
        byte[] decoded = DecodeBytes(encoded, encoding, fragmentSize);
        return decoded.AsSpan().SequenceEqual(expected);
    }

    private static byte[] DecodeBytes(byte[] encoded, ManagedHttpContentEncodingState encoding,
                                      int fragmentSize)
    {
        ManagedContentEncodingDecoder decoder = new(encoding);
        RecordingSink sink = new(1_000_000);
        for (int offset = 0; offset != encoded.Length;)
        {
            int length = Math.Min(fragmentSize, encoded.Length - offset);
            while (length != 0)
            {
                int accepted = Math.Min(length, decoder.InputFreeCapacity);
                if (accepted == 0) PumpDecoder(decoder, sink, false);
                else
                {
                    Check(decoder.AppendInput(encoded.AsSpan(offset, accepted)), "decoder-append");
                    offset += accepted;
                    length -= accepted;
                    PumpDecoder(decoder, sink, false);
                }
            }
        }
        while (!decoder.IsComplete && decoder.State != ManagedContentDecoderState.Failed)
        {
            ManagedContentDecoderProcessResult result = decoder.Pump(true);
            if (result == ManagedContentDecoderProcessResult.OutputAvailable)
                Drain(decoder, sink);
            else if (result == ManagedContentDecoderProcessResult.Complete)
                break;
            else if (result == ManagedContentDecoderProcessResult.Failed)
                break;
            else if (result == ManagedContentDecoderProcessResult.NeedInput)
                throw new InvalidOperationException("decoder unexpectedly needs input");
        }
        if (decoder.State == ManagedContentDecoderState.Failed)
            throw new InvalidOperationException("decode failed: " + decoder.FailureReason +
                                                " produced=" + decoder.DecodedBytesProduced +
                                                " input=" + decoder.InputLength);
        Check(decoder.IsComplete, "decoder-complete");
        return sink.ToArray();
    }

    private static bool DecodeWithFailure(byte[] encoded,
                                          ManagedHttpContentEncodingState encoding,
                                          ManagedContentDecoderFailureReason expected)
    {
        try { DecodeBytes(encoded, encoding, 1); return false; }
        catch (InvalidOperationException exception) { return exception.Message.Contains(expected.ToString(), StringComparison.Ordinal); }
    }

    private static bool FailsWith(byte[] encoded, ManagedHttpContentEncodingState encoding,
                                  ManagedContentDecoderFailureReason expected,
                                  int maximumDecodedLength = ManagedContentEncodingLimits.MaximumDecodedResourceLength)
    {
        try
        {
            ManagedContentEncodingDecoder decoder = new(encoding, maximumDecodedLength);
            RecordingSink sink = new(1_000_000);
            for (int offset = 0; offset != encoded.Length;)
            {
                int take = Math.Min(decoder.InputFreeCapacity, encoded.Length - offset);
                if (take == 0) { PumpDecoder(decoder, sink, false); continue; }
                Check(decoder.AppendInput(encoded.AsSpan(offset, take)), "failure-append");
                offset += take;
                PumpDecoder(decoder, sink, false);
                if (decoder.State == ManagedContentDecoderState.Failed) break;
            }
            if (decoder.State != ManagedContentDecoderState.Failed)
            {
                decoder.Pump(true);
                if (decoder.OutputLength != 0 && decoder.State != ManagedContentDecoderState.Failed)
                    Drain(decoder, sink);
            }
            return decoder.FailureReason == expected;
        }
        catch (InvalidOperationException) { return false; }
    }

    private static void PumpDecoder(ManagedContentEncodingDecoder decoder,
                                    RecordingSink sink, bool endOfInput)
    {
        while (true)
        {
            ManagedContentDecoderProcessResult result = decoder.Pump(endOfInput);
            if (decoder.OutputLength != 0 &&
                decoder.State != ManagedContentDecoderState.Failed)
            {
                Drain(decoder, sink);
                if (result == ManagedContentDecoderProcessResult.OutputAvailable)
                    continue;
            }
            if (result == ManagedContentDecoderProcessResult.Complete ||
                result == ManagedContentDecoderProcessResult.NeedInput ||
                result == ManagedContentDecoderProcessResult.Failed)
                return;
        }
    }

    private static void Drain(ManagedContentEncodingDecoder decoder, RecordingSink sink)
    {
        ManagedHttpBodyDeliveryResult result = decoder.ConsumeOutput(sink);
        Check(result == ManagedHttpBodyDeliveryResult.Delivered, "decoder-output-drain");
    }

    private static byte[] BuildGzipStored(byte[] body, bool extra, bool name,
                                          bool comment, bool headerCrc)
    {
        List<byte> result = new();
        byte flags = (byte)((extra ? 4 : 0) | (name ? 8 : 0) |
                            (comment ? 16 : 0) | (headerCrc ? 2 : 0));
        result.AddRange(new byte[] { 0x1F, 0x8B, 8, flags, 0, 0, 0, 0, 0, 255 });
        if (extra) result.AddRange(new byte[] { 3, 0, 1, 2, 3 });
        if (name) result.AddRange(Ascii("name\0"));
        if (comment) result.AddRange(Ascii("comment\0"));
        if (headerCrc)
        {
            uint crc = Crc32(result.ToArray());
            result.Add((byte)crc);
            result.Add((byte)(crc >> 8));
        }
        AddStoredBlocks(result, body);
        uint bodyCrc = Crc32(body);
        AddUInt32LittleEndian(result, bodyCrc);
        AddUInt32LittleEndian(result, (uint)body.Length);
        return result.ToArray();
    }

    private static byte[] BuildZlibStored(byte[] body)
    {
        List<byte> result = new() { 0x78, 0x01 };
        AddStoredBlocks(result, body);
        uint adler = Adler32(body);
        result.Add((byte)(adler >> 24));
        result.Add((byte)(adler >> 16));
        result.Add((byte)(adler >> 8));
        result.Add((byte)adler);
        return result.ToArray();
    }

    private static void AddStoredBlocks(List<byte> result, byte[] body)
    {
        int offset = 0;
        if (body.Length == 0) { result.Add(1); result.Add(0); result.Add(255); result.Add(255); return; }
        while (offset != body.Length)
        {
            int length = Math.Min(65_535, body.Length - offset);
            bool final = offset + length == body.Length;
            result.Add((byte)(final ? 1 : 0));
            result.Add((byte)length); result.Add((byte)(length >> 8));
            ushort inverse = (ushort)~length;
            result.Add((byte)inverse); result.Add((byte)(inverse >> 8));
            for (int index = 0; index != length; ++index) result.Add(body[offset + index]);
            offset += length;
        }
    }

    private static byte[] CompressGzip(byte[] value)
    {
        using MemoryStream output = new();
        using (GZipStream gzip = new(output, CompressionLevel.Optimal, true)) gzip.Write(value);
        return output.ToArray();
    }

    private static byte[] CompressZlib(byte[] value)
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionLevel.Optimal, true)) zlib.Write(value);
        return output.ToArray();
    }

    private static byte[] Pattern(int length)
    {
        byte[] result = new byte[length];
        for (int index = 0; index != length; ++index) result[index] = (byte)((index * 31 + 7) & 255);
        return result;
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);

    private static byte[] Mutate(byte[] value, int index, byte replacement)
    {
        byte[] result = (byte[])value.Clone(); result[index] = replacement; return result;
    }

    private static byte[] Replace(byte[] value, int offset, byte[] replacement)
    {
        byte[] result = (byte[])value.Clone(); replacement.CopyTo(result, offset); return result;
    }

    private static byte[] Combine(byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length]; first.CopyTo(result, 0); second.CopyTo(result, first.Length); return result;
    }

    private static uint Crc32(byte[] value)
    {
        uint crc = 0xFFFFFFFFU;
        for (int index = 0; index != value.Length; ++index)
        {
            crc ^= value[index];
            for (int bit = 0; bit != 8; ++bit) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320U : crc >> 1;
        }
        return ~crc;
    }

    private static uint Adler32(byte[] value)
    {
        uint a = 1, b = 0;
        for (int index = 0; index != value.Length; ++index)
        {
            a += value[index]; if (a >= 65521) a -= 65521;
            b += a; if (b >= 65521) b %= 65521;
        }
        return (b << 16) | a;
    }

    private static byte[] AddUInt32LittleEndian(List<byte> result, uint value)
    {
        result.Add((byte)value); result.Add((byte)(value >> 8)); result.Add((byte)(value >> 16)); result.Add((byte)(value >> 24)); return result.ToArray();
    }

    private static byte[] Sha256(byte[] value) => SHA256.HashData(value);

    private static void Check(bool condition, string name)
    {
        ++s_cases;
        if (!condition) throw new InvalidOperationException("failed: " + name);
    }

    private sealed class RecordingSink : IManagedHttpBodySink
    {
        private readonly byte[] _buffer;
        private int _length;
        internal RecordingSink(int capacity) { _buffer = new byte[capacity]; }
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            if (segment.Length > _buffer.Length - _length) return ManagedHttpBodySinkResult.Fail;
            segment.CopyTo(_buffer.AsSpan(_length)); _length += segment.Length; return ManagedHttpBodySinkResult.Continue;
        }
        internal byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
    }

    private sealed class PausingCountConsumer : IManagedResourceConsumer
    {
        private bool _pauseOnce;
        private ManagedResourceConsumerState _state;
        public ManagedResourceConsumerState State => _state;
        public ManagedResourceConsumerFailureReason FailureReason =>
            ManagedResourceConsumerFailureReason.None;
        public int BytesProcessed { get; private set; }
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<byte> segment)
        {
            if (!_pauseOnce)
            {
                _pauseOnce = true;
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
            _pauseOnce = false;
            BytesProcessed = 0;
            _state = ManagedResourceConsumerState.Idle;
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
            _response = response;
            _fragmentSize = fragmentSize <= 0 ? 512 : fragmentSize;
        }
        internal int PollCount { get; private set; }
        public bool IsAvailable => true;
        public NetworkStatus GetStatus() => new(true, true, true, true,
            0x021500000002, new Ipv4Address(0x0A0F0001),
            new Ipv4Address(0xFFFFFF00), new Ipv4Address(0x0A0F0001));
        public void SetRuntimeStatus(NetworkStatus status) { }
        public ManagedNetworkServiceBackendResult BeginResolve(ReadOnlySpan<byte> name)
        {
            _event = ManagedNetworkServiceBackendEvent.DnsResolved;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }
        public bool TryGetResolved(out Ipv4Address address)
        {
            address = new Ipv4Address(0x0A0F0002); return true;
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
                    new Ipv4Address(0x0A0F0002), new Ipv4Address(0x0A0F0001),
                    ManagedTcpConnection.ServerPort, ManagedTcpConnection.ClientPort,
                    fragment))
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
        public ManagedNetworkServiceBackendResult BeginPing(Ipv4Address destination) => ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult BindUdp(ushort port) => ManagedNetworkServiceBackendResult.NoResource;
        public ManagedNetworkServiceBackendResult UnregisterUdp(ushort port) => ManagedNetworkServiceBackendResult.Rejected;
        public ManagedNetworkServiceBackendResult SendUdp(Ipv4Address destination, ushort destinationPort, ushort sourcePort, ReadOnlySpan<byte> payload) => ManagedNetworkServiceBackendResult.NoResource;
        public ManagedTcpConnectionState TcpState => _tcpState;
        public ManagedNetworkServiceBackendResult BeginTcpConnect(Ipv4Address destination, ushort destinationPort)
        {
            _tcpState = ManagedTcpConnectionState.SynSent;
            _event = ManagedNetworkServiceBackendEvent.TcpEstablished;
            _eventPending = true;
            return ManagedNetworkServiceBackendResult.Started;
        }
        public ManagedNetworkServiceBackendResult SendTcp(ReadOnlySpan<byte> payload)
        {
            if (!_responseQueued)
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
            _fragments.Clear(); _fragmentIndex = 0; _finQueued = false;
            _responseQueued = false; _eventPending = false;
            return true;
        }
        internal void Attach(ManagedNetworkService service) => _service = service;
    }
}

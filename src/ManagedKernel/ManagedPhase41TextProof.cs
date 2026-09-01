using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Deterministic Phase 41 proof.  The host fixture repeats this scalar pattern
   in UTF-8 and optionally wraps it in one gzip member.  The verifier checks
   every scalar in order, while the text/resource layers retain only bounded
   windows. */
internal sealed class ManagedPhase41TextProof
{
    private const int RepeatCount = 256;
    private const int PatternLength = 30;
    private const int ScalarCount = RepeatCount * PatternLength;
    private const int PrefixLength = 16;
    private const int ResourceLength = RepeatCount * 41;
    private static readonly uint[] Pattern =
    {
        'G', 'u', 'i', 'd', 'e', 'X', 'O', 'S', ' ', '4', '1', '\r', '\n',
        'R', 0x00E9, 's', 'u', 'm', ' ', 0x03BB, 0x03B7, ' ', 0x0416,
        ' ', 0x4E2D, ' ', 0x2605, ' ', 0x1F642, '\n'
    };
    private static readonly uint[] ExpectedPrefix =
        { 'G', 'u', 'i', 'd', 'e', 'X', 'O', 'S', ' ', '4', '1', '\r', '\n', 'R', 0x00E9, 's' };

    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> Path => "/phase41/gzip"u8;
    private static ReadOnlySpan<byte> ContentType => "text/plain; charset=utf-8"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedTextResourceRequest _resource;
    private readonly ManagedPhase41PatternConsumer _verify = new();
    private readonly ManagedTextCountConsumer _count = new(true);
    private readonly ManagedTextPrefixConsumer _prefix = new(PrefixLength);
    private readonly ManagedTextCompositeConsumer _pipeline;
    private bool _pauseObserved;
    private bool _bodyReceivedLogged;
    private int _stablePausedPolls;

    internal ManagedPhase41TextProof(ManagedNetworkService service)
    {
        _service = service;
        _pipeline = new(_verify, _count, _prefix);
        ManagedSecureRandom random = new(new FixedEntropy(CreateEntropy()));
        _resource = new(service, ManagedTls12Phase31Fixtures.Root,
                        new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random,
                        ManagedHttpLimits.MaximumStreamedBodyLength,
                        compactTlsProfile: false,
                        maximumDecodedResourceLength: ManagedContentEncodingLimits.MaximumDecodedResourceLength);
    }

    internal bool TryRun()
    {
        if (!_service.GetStatus().DhcpBound || !_service.GetStatus().Configured ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_READY\r\n"u8) ||
            _resource.BeginGet(Hostname, Path, _pipeline) != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_STARTED\r\n"u8))
            return false;

        if (!KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE41_REQUEST_STARTED\r\n"u8))
            return false;

        for (int poll = 0; poll != 131_072; ++poll)
        {
            NetworkOperationResult result = _resource.Poll();
            ManagedTextProgressSnapshot progress = _resource.Progress;
            if (result == NetworkOperationResult.Failed ||
                _resource.State == ManagedResourceState.Failed)
                return WriteFailure(progress);
            if (!_bodyReceivedLogged && progress.TextInputBytesConsumed != 0 &&
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_BODY_RECEIVED\r\n"u8))
                return false;
            _bodyReceivedLogged |= progress.TextInputBytesConsumed != 0;
            if (!_pauseObserved && progress.ScalarsDelivered != 0)
            {
                if (_resource.Pause() != NetworkOperationResult.Success) return false;
                ManagedTextProgressSnapshot paused = _resource.Progress;
                for (int stable = 0; stable != 4; ++stable)
                {
                    if (_resource.Poll() != NetworkOperationResult.Success) return false;
                    ManagedTextProgressSnapshot current = _resource.Progress;
                    if (current.State != ManagedResourceState.Paused ||
                        current.EncodedHttpBytesReceived != paused.EncodedHttpBytesReceived ||
                        current.DecompressedResourceBytesProduced != paused.DecompressedResourceBytesProduced ||
                        current.TextInputBytesConsumed != paused.TextInputBytesConsumed ||
                        current.ScalarsProduced != paused.ScalarsProduced ||
                        current.ScalarsDelivered != paused.ScalarsDelivered ||
                        current.BufferedDecodedTextCount != paused.BufferedDecodedTextCount)
                        return false;
                    ++_stablePausedPolls;
                }
                _pauseObserved = true;
                if (_resource.Resume() != NetworkOperationResult.Success) return false;
            }
            if (_resource.State == ManagedResourceState.Completed) return FinishSuccess();
        }
        return false;
    }

    private bool FinishSuccess()
    {
        ManagedTextProgressSnapshot progress = _resource.Progress;
        Span<byte> digest = stackalloc byte[ManagedResourceSha256Consumer.DigestSize];
        Span<byte> contentType = stackalloc byte[ManagedHttpLimits.MaximumContentTypeLength];
        Span<uint> prefix = stackalloc uint[PrefixLength];
        if (!_pauseObserved || progress.StatusCode != 200 ||
            progress.MimeClassification != ManagedMimeClassification.TextPlain ||
            progress.Charset != ManagedTextCharset.Utf8 ||
            progress.CharsetSource != ManagedTextCharsetSource.Explicit ||
            progress.ContentTypeState != ManagedHttpContentTypeState.Available ||
            progress.ContentEncodingState != ManagedHttpContentEncodingState.Gzip ||
            progress.EncodedHttpBytesReceived == 0 ||
            progress.DecompressedResourceBytesProduced != ResourceLength ||
            progress.TextInputBytesConsumed != ResourceLength ||
            progress.ScalarsProduced != ScalarCount ||
            progress.ScalarsDelivered != ScalarCount ||
            progress.BufferedDecodedTextCount != 0 ||
            progress.TextSegmentCount == 0 || progress.PauseCount != 1 ||
            progress.ResumeCount != 1 || progress.DecoderFailureReason != ManagedTextDecoderFailureReason.None ||
            _count.Count != ScalarCount || _count.LineCount != RepeatCount * 2 ||
            !_verify.IsValid || !_resource.TryCopyContentType(contentType, out int contentTypeLength) ||
            !contentType[..contentTypeLength].SequenceEqual(ContentType) ||
            !_resource.TryCopyResourceDigest(digest) ||
            !_prefix.TryCopyPrefix(prefix, out int prefixLength) || prefixLength != PrefixLength ||
            !CheckPrefix(prefix))
            return false;

        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_STATUS=0x"u8, (ulong)progress.StatusCode) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_MIME=0x"u8, (ulong)progress.MimeClassification) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_CHARSET=0x"u8, (ulong)progress.Charset) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_CHARSET_SOURCE=0x"u8, (ulong)progress.CharsetSource) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_CONTENT_TYPE_LENGTH=0x"u8, (ulong)contentTypeLength) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_ENCODED_BYTES=0x"u8, (ulong)progress.EncodedHttpBytesReceived) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_DECOMPRESSED_BYTES=0x"u8, (ulong)progress.DecompressedResourceBytesProduced) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_TEXT_INPUT_BYTES=0x"u8, (ulong)progress.TextInputBytesConsumed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_SCALARS_PRODUCED=0x"u8, (ulong)progress.ScalarsProduced) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_SCALARS_DELIVERED=0x"u8, (ulong)progress.ScalarsDelivered) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_TEXT_SEGMENTS=0x"u8, (ulong)progress.TextSegmentCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_PAUSE_COUNT=0x"u8, (ulong)progress.PauseCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESUME_COUNT=0x"u8, (ulong)progress.ResumeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_STABLE_PAUSED_POLLS=0x"u8, (ulong)_stablePausedPolls) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_PEAK_HTTP_BUFFER=0x"u8, (ulong)progress.PeakHttpBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_PEAK_DECOMPRESSION_BUFFER=0x"u8, (ulong)progress.PeakDecompressionBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_PEAK_TEXT_BUFFER=0x"u8, (ulong)progress.PeakTextBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_PREFIX_LENGTH=0x"u8, (ulong)prefixLength) &&
               WritePrefix(prefix) && WriteDigest(digest) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_COMPLETE\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_PASS\r\n"u8);
    }

    private static bool CheckPrefix(ReadOnlySpan<uint> prefix)
    {
        return prefix.SequenceEqual(ExpectedPrefix);
    }

    private static bool WritePrefix(ReadOnlySpan<uint> prefix)
    {
        for (int index = 0; index != prefix.Length; ++index)
            if (!KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_PREFIX_SCALAR=0x"u8, prefix[index])) return false;
        return true;
    }

    private static bool WriteDigest(ReadOnlySpan<byte> digest)
    {
        for (int index = 0; index != digest.Length; index += 4)
        {
            uint word = ((uint)digest[index] << 24) | ((uint)digest[index + 1] << 16) |
                        ((uint)digest[index + 2] << 8) | digest[index + 3];
            if (!KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_SHA256_WORD=0x"u8, word)) return false;
        }
        return true;
    }

    private bool WriteFailure(ManagedTextProgressSnapshot progress)
    {
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_FAILURE=0x"u8, (ulong)progress.FailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_RESOURCE_FAILURE=0x"u8, (ulong)progress.ResourceFailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE41_DECODER_FAILURE=0x"u8, (ulong)progress.DecoderFailureReason);
        return false;
    }

    private static byte[] CreateEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
    }

    private sealed class ManagedPhase41PatternConsumer : IManagedTextConsumer
    {
        private int _count;
        private ManagedResourceConsumerState _state;
        internal bool IsValid { get; private set; }
        internal ManagedPhase41PatternConsumer() => Reset();
        public ManagedResourceConsumerState State => _state;
        public ManagedTextConsumerFailureReason FailureReason => IsValid ? ManagedTextConsumerFailureReason.None : ManagedTextConsumerFailureReason.ConsumerFailure;
        public int ScalarsProcessed => _count;
        public ManagedHttpBodySinkResult Consume(ReadOnlySpan<uint> segment)
        {
            if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed || _state == ManagedResourceConsumerState.Completed)
                return ManagedHttpBodySinkResult.Fail;
            for (int index = 0; index != segment.Length; ++index)
            {
                if (_count >= ScalarCount || segment[index] != Pattern[_count % PatternLength])
                {
                    IsValid = false; _state = ManagedResourceConsumerState.Failed; return ManagedHttpBodySinkResult.Fail;
                }
                ++_count;
            }
            _state = ManagedResourceConsumerState.Receiving;
            return ManagedHttpBodySinkResult.Continue;
        }
        public bool Complete()
        {
            if (_state == ManagedResourceConsumerState.Cancelled || _state == ManagedResourceConsumerState.Failed) return false;
            IsValid = _count == ScalarCount; _state = IsValid ? ManagedResourceConsumerState.Completed : ManagedResourceConsumerState.Failed; return IsValid;
        }
        public void Cancel() => _state = ManagedResourceConsumerState.Cancelled;
        public void Reset() { _count = 0; IsValid = true; _state = ManagedResourceConsumerState.Idle; }
    }

    private sealed class FixedEntropy : IManagedEntropyProvider
    {
        private readonly byte[] _bytes;
        private int _offset;
        internal FixedEntropy(byte[] bytes) => _bytes = bytes;
        public bool IsAvailable => _bytes.Length != 0;
        public bool TryFill(Span<byte> destination)
        {
            for (int index = 0; index != destination.Length; ++index) destination[index] = _bytes[_offset++ % _bytes.Length];
            return true;
        }
    }
}

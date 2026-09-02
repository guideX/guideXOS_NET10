using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Deterministic Phase 42 proof.  The HTTP response is a gzip-compressed
   text/html fixture; only the bounded HTML resource pipeline and canonical
   token hash are retained on the guest. */
internal sealed class ManagedPhase42HtmlProof
{
    private const int ResourceLength = 1894;
    private const int TokenCount = 52;
    private const int TextTokenCount = 19;
    private const int StartTagCount = 16;
    private const int EndTagCount = 14;
    private const int CommentCount = 1;
    private const int DoctypeCount = 1;
    private const int AttributeCount = 21;
    private const int TextScalarCount = 1362;
    private const int EntityCount = 5;
    private static readonly byte[] ExpectedTokenHash =
    {
        0x15, 0x96, 0x7F, 0x70, 0xBB, 0x89, 0xC5, 0xAC,
        0x00, 0xD7, 0x3E, 0x4D, 0x4D, 0x73, 0xB0, 0x57,
        0xF6, 0xC4, 0x8E, 0x67, 0x0F, 0x3E, 0xE7, 0x89,
        0xC0, 0xA0, 0xB9, 0x45, 0x70, 0x56, 0x05, 0xB6
    };
    private static readonly byte[] ExpectedResourceDigest =
    {
        0xFA, 0xC7, 0xD0, 0xEB, 0x02, 0xB0, 0x94, 0x00,
        0x18, 0xD6, 0x27, 0xA8, 0x67, 0x31, 0xE8, 0x75,
        0x4B, 0xD1, 0x01, 0x0F, 0x10, 0xA8, 0x25, 0xF9,
        0x11, 0x47, 0xD7, 0x90, 0x8F, 0xFD, 0x3C, 0x44
    };
    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> Path => "/phase42/gzip"u8;
    private static ReadOnlySpan<byte> ContentType => "text/html; charset=utf-8"u8;

    private readonly ManagedNetworkService _service;
    private readonly ManagedHtmlResourceRequest _resource;
    private readonly ManagedPhase42TokenConsumer _tokens = new();
    private bool _bodyReceivedLogged;
    private bool _pauseObserved;
    private int _stablePausedPolls;

    internal ManagedPhase42HtmlProof(ManagedNetworkService service)
    {
        _service = service;
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
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_READY\r\n"u8) ||
            _resource.BeginGet(Hostname, Path, _tokens) != NetworkOperationResult.Started ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_STARTED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE42_REQUEST_STARTED\r\n"u8))
            return false;

        for (int poll = 0; poll != 131_072; ++poll)
        {
            NetworkOperationResult result = _resource.Poll();
            ManagedHtmlProgressSnapshot progress = _resource.Progress;
            if (result == NetworkOperationResult.Failed ||
                _resource.State == ManagedResourceState.Failed)
                return WriteFailure(progress);
            if (!_bodyReceivedLogged && progress.TextInputBytesConsumed != 0 &&
                !KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_BODY_RECEIVED\r\n"u8))
                return false;
            _bodyReceivedLogged |= progress.TextInputBytesConsumed != 0;
            if (!_pauseObserved && progress.TokensEmitted != 0)
            {
                if (_resource.Pause() != NetworkOperationResult.Success)
                    return false;
                ManagedHtmlProgressSnapshot paused = _resource.Progress;
                for (int stable = 0; stable != 4; ++stable)
                {
                    if (_resource.Poll() != NetworkOperationResult.Success) return false;
                    ManagedHtmlProgressSnapshot current = _resource.Progress;
                    if (current.State != ManagedResourceState.Paused ||
                        current.EncodedBytesReceived != paused.EncodedBytesReceived ||
                        current.DecompressedBytesProduced != paused.DecompressedBytesProduced ||
                        current.TextInputBytesConsumed != paused.TextInputBytesConsumed ||
                        current.ScalarsProduced != paused.ScalarsProduced ||
                        current.ScalarsDelivered != paused.ScalarsDelivered ||
                        current.TokensEmitted != paused.TokensEmitted)
                        return false;
                    ++_stablePausedPolls;
                }
                _pauseObserved = true;
                if (_resource.Resume() != NetworkOperationResult.Success) return false;
            }
            else if (_resource.State == ManagedResourceState.Paused)
            {
                ManagedHtmlProgressSnapshot paused = progress;
                for (int stable = 0; stable != 4; ++stable)
                {
                    if (_resource.Poll() != NetworkOperationResult.Success) return false;
                    ManagedHtmlProgressSnapshot current = _resource.Progress;
                    if (current.State != ManagedResourceState.Paused ||
                        current.EncodedBytesReceived != paused.EncodedBytesReceived ||
                        current.DecompressedBytesProduced != paused.DecompressedBytesProduced ||
                        current.TextInputBytesConsumed != paused.TextInputBytesConsumed ||
                        current.ScalarsProduced != paused.ScalarsProduced ||
                        current.ScalarsDelivered != paused.ScalarsDelivered ||
                        current.TokensEmitted != paused.TokensEmitted)
                        return false;
                    ++_stablePausedPolls;
                }
                _pauseObserved = true;
                if (_resource.Resume() != NetworkOperationResult.Success) return false;
            }
            if (_resource.State == ManagedResourceState.Completed)
                return FinishSuccess(_pauseObserved);
        }
        return false;
    }

    private bool FinishSuccess(bool pauseChecked)
    {
        ManagedHtmlProgressSnapshot progress = _resource.Progress;
        Span<byte> contentType = stackalloc byte[ManagedHttpLimits.MaximumContentTypeLength];
        Span<byte> tokenHash = stackalloc byte[ManagedSha256.DigestSize];
        Span<byte> resourceDigest = stackalloc byte[ManagedSha256.DigestSize];
        bool tokenHashAvailable = _tokens.TryCopyDigest(tokenHash);
        bool contentTypeAvailable = _resource.TryCopyContentType(
            contentType, out int contentTypeLength);
        bool resourceDigestAvailable = _resource.TryCopyResourceDigest(resourceDigest);
        if (!pauseChecked || progress.StatusCode != 200 ||
            progress.MimeClassification != ManagedMimeClassification.Html ||
            progress.Charset != ManagedTextCharset.Utf8 ||
            progress.ContentTypeState != ManagedHttpContentTypeState.Available ||
            progress.ContentEncodingState != ManagedHttpContentEncodingState.Gzip ||
            progress.DecompressedBytesProduced != ResourceLength ||
            progress.TextInputBytesConsumed != ResourceLength ||
            progress.ScalarsConsumed != progress.ScalarsReceived ||
            progress.TokensEmitted != TokenCount ||
            progress.TextTokens != TextTokenCount ||
            progress.StartTagTokens != StartTagCount ||
            progress.EndTagTokens != EndTagCount ||
            progress.CommentTokens != CommentCount ||
            progress.DoctypeTokens != DoctypeCount ||
            progress.AttributesEmitted != AttributeCount ||
            progress.CharacterReferencesDecoded != EntityCount ||
            progress.BufferedTextScalars != 0 ||
            progress.PauseCount != 1 || progress.ResumeCount != 1 ||
            progress.TokenizerFailureReason != ManagedHtmlTokenizerFailureReason.None ||
            !_tokens.IsValid || _tokens.TextScalars != TextScalarCount ||
            !tokenHashAvailable ||
            !tokenHash.SequenceEqual(ExpectedTokenHash) ||
            !contentTypeAvailable ||
            !contentType[..contentTypeLength].SequenceEqual(ContentType) ||
            !resourceDigestAvailable ||
            !resourceDigest.SequenceEqual(ExpectedResourceDigest))
        {
            return false;
        }

        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_STATUS=0x"u8, (ulong)progress.StatusCode) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_MIME=0x"u8, (ulong)progress.MimeClassification) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_CHARSET=0x"u8, (ulong)progress.Charset) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_ENCODED_BYTES=0x"u8, (ulong)progress.EncodedBytesReceived) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_DECOMPRESSED_BYTES=0x"u8, (ulong)progress.DecompressedBytesProduced) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_TEXT_INPUT_BYTES=0x"u8, (ulong)progress.TextInputBytesConsumed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_SCALARS_RECEIVED=0x"u8, (ulong)progress.ScalarsReceived) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_SCALARS_CONSUMED=0x"u8, (ulong)progress.ScalarsConsumed) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_TOKENS=0x"u8, (ulong)progress.TokensEmitted) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_TEXT_TOKENS=0x"u8, (ulong)progress.TextTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_START_TAGS=0x"u8, (ulong)progress.StartTagTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_END_TAGS=0x"u8, (ulong)progress.EndTagTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_COMMENTS=0x"u8, (ulong)progress.CommentTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_DOCTYPES=0x"u8, (ulong)progress.DoctypeTokens) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_ATTRIBUTES=0x"u8, (ulong)progress.AttributesEmitted) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_TEXT_SCALARS=0x"u8, (ulong)_tokens.TextScalars) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_ENTITIES=0x"u8, (ulong)progress.CharacterReferencesDecoded) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_PAUSE_COUNT=0x"u8, (ulong)progress.PauseCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_RESUME_COUNT=0x"u8, (ulong)progress.ResumeCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_STABLE_PAUSED_POLLS=0x"u8, (ulong)_stablePausedPolls) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_PEAK_HTTP_BUFFER=0x"u8, (ulong)progress.PeakHttpBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_PEAK_DECOMPRESSION_BUFFER=0x"u8, (ulong)progress.PeakDecompressionBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_PEAK_TEXT_BUFFER=0x"u8, (ulong)progress.PeakTextBuffer) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_PEAK_TOKENIZER_TEXT=0x"u8, (ulong)progress.PeakTokenizerTextScalars) &&
               WriteDigest(tokenHash) &&
               WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_SHA256_WORD=0x"u8, resourceDigest) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_COMPLETE\r\n"u8) &&
               KernelLog.Write("GXOS_NET10:MANAGED_HTTPS_PHASE42_RESOURCE_PASS\r\n"u8);
    }

    private static bool WriteDigest(ReadOnlySpan<byte> digest)
    {
        return WriteDigest("GXOS_NET10:MANAGED_HTTPS_PHASE42_TOKEN_HASH_WORD=0x"u8, digest);
    }

    private static bool WriteDigest(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> digest)
    {
        for (int index = 0; index != digest.Length; index += 4)
        {
            uint word = ((uint)digest[index] << 24) | ((uint)digest[index + 1] << 16) |
                        ((uint)digest[index + 2] << 8) | digest[index + 3];
            if (!KernelLog.WriteHexLine(prefix, word))
                return false;
        }
        return true;
    }

    private static bool WriteFailure(ManagedHtmlProgressSnapshot progress)
    {
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_FAILURE=0x"u8,
                               (ulong)progress.FailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_TEXT_FAILURE=0x"u8,
                               (ulong)progress.TextFailureReason);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_HTTPS_PHASE42_TOKENIZER_FAILURE=0x"u8,
                               (ulong)progress.TokenizerFailureReason);
        return false;
    }

    private static byte[] CreateEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
    }

    private sealed class ManagedPhase42TokenConsumer : IManagedHtmlTokenConsumer
    {
        private readonly ManagedHtmlTokenHashConsumer _hash = new();
        private ManagedHtmlTokenConsumerState _state;
        internal bool IsValid { get; private set; }
        internal int TextScalars => _hash.TextScalars;

        internal ManagedPhase42TokenConsumer() => Reset();
        public ManagedHtmlTokenConsumerState State => _state;
        public ManagedHtmlTokenConsumerFailureReason FailureReason =>
            IsValid ? ManagedHtmlTokenConsumerFailureReason.None :
            ManagedHtmlTokenConsumerFailureReason.ConsumerFailure;
        public int TokensProcessed => _hash.TokensProcessed;

        public ManagedHttpBodySinkResult Consume(in ManagedHtmlToken token)
        {
            if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
                _state == ManagedHtmlTokenConsumerState.Failed ||
                _state == ManagedHtmlTokenConsumerState.Completed)
                return ManagedHttpBodySinkResult.Fail;
            ManagedHttpBodySinkResult result = _hash.Consume(in token);
            if (result == ManagedHttpBodySinkResult.Fail)
            {
                IsValid = false;
                _state = ManagedHtmlTokenConsumerState.Failed;
                return result;
            }
            _state = ManagedHtmlTokenConsumerState.Receiving;
            return result;
        }

        public bool Complete()
        {
            if (_state == ManagedHtmlTokenConsumerState.Cancelled ||
                _state == ManagedHtmlTokenConsumerState.Failed ||
                !_hash.Complete()) return false;
            IsValid = true;
            _state = ManagedHtmlTokenConsumerState.Completed;
            return true;
        }

        public void Cancel()
        {
            _hash.Cancel();
            _state = ManagedHtmlTokenConsumerState.Cancelled;
        }

        public void Reset()
        {
            _hash.Reset();
            _state = ManagedHtmlTokenConsumerState.Idle;
            IsValid = true;
        }

        internal bool TryCopyDigest(Span<byte> destination) =>
            _hash.TryCopyDigest(destination);
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
}

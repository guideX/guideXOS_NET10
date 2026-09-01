using System;

namespace GuideXOS.Net10.ManagedKernel;

/* Deterministic Phase 39 proof. The body is intentionally larger than the
   parser delivery window. The destination is caller-owned and bounded; the
   count/hash/prefix components retain only their fixed state. */
internal sealed class ManagedPhase39ResourceProof
{
    private const int ResourceLength = 16_884;
    private const int PrefixLength = 32;
    private static readonly byte[] ExpectedDigest =
    {
        0x02, 0x84, 0xCD, 0x23, 0xED, 0x35, 0x40, 0x23,
        0xF0, 0x36, 0x36, 0x78, 0x79, 0x49, 0x05, 0xB2,
        0x85, 0xC1, 0x04, 0xA2, 0x05, 0x61, 0x89, 0xB3,
        0x6C, 0x23, 0xC0, 0x68, 0x99, 0x24, 0x45, 0x4F
    };

    private static ReadOnlySpan<byte> Hostname => "www.example.com"u8;
    private static ReadOnlySpan<byte> Path => "/phase39/resource"u8;
    private static ReadOnlySpan<byte> ContentType =>
        "application/octet-stream"u8;

    private readonly ManagedResourceRequest _resource;
    private readonly ManagedResourceCountConsumer _count = new();
    private readonly ManagedResourceSha256Consumer _hash = new();
    private readonly ManagedResourcePrefixConsumer _prefix =
        new(new byte[PrefixLength]);
    private readonly ManagedResourceCompositeConsumer _pipeline;
    private bool _dnsLogged;
    private bool _tcpLogged;
    private bool _tlsLogged;
    private bool _requestLogged;
    private bool _applicationLogged;
    private bool _statusLogged;
    private bool _bodyLogged;
    private bool _pauseObserved;
    private int _peakBuffered;
    private int _lastProgressLogged = -1;

    internal ManagedPhase39ResourceProof(ManagedNetworkService service)
    {
        _service = service;
        ManagedSecureRandom random = new(new FixedEntropy(CreateEntropy()));
        _pipeline = new(_count, _hash, _prefix);
        _resource = new(service, ManagedTls12Phase31Fixtures.Root,
                        new ManagedX509UtcTime(2028, 1, 1, 0, 0, 0), random,
                        ManagedHttpLimits.MaximumStreamedBodyLength,
                        compactTlsProfile: false);
    }

    internal bool TryRun()
    {
        NetworkStatus status = _resource.Protocol == ManagedResourceProtocol.Https
            ? GetStatus() : default;
        if (!status.DhcpBound || !status.Configured ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_READY\r\n"u8) ||
            _resource.BeginGet(Hostname, Path, _pipeline) !=
                NetworkOperationResult.Started ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_STARTED\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_HTTPS_PHASE34_REQUEST_STARTED\r\n"u8))
            return false;

        for (int poll = 0; poll != 65_536; ++poll)
        {
            NetworkOperationResult result = _resource.Poll();
            ManagedResourceProgressSnapshot progress = _resource.Progress;
            if (progress.PeakBufferedBytes > _peakBuffered)
                _peakBuffered = progress.PeakBufferedBytes;
            if (!ObserveTransport(progress)) return false;
            if (progress.ResourceBytesProcessed != _lastProgressLogged)
            {
                _lastProgressLogged = progress.ResourceBytesProcessed;
                if (!KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PROGRESS=0x"u8,
                        (ulong)_lastProgressLogged))
                    return false;
            }
            if (result == NetworkOperationResult.Failed ||
                _resource.State == ManagedResourceState.Failed)
                return WriteFailure(progress);

            if (!_pauseObserved && _count.Count != 0)
            {
                if (_resource.Pause() != NetworkOperationResult.Success)
                    return false;
                ManagedResourceProgressSnapshot paused = _resource.Progress;
                int stablePolls = 0;
                for (; stablePolls != 4; ++stablePolls)
                {
                    if (_resource.Poll() != NetworkOperationResult.Success)
                        return false;
                    ManagedResourceProgressSnapshot current = _resource.Progress;
                    if (current.State != ManagedResourceState.Paused ||
                        current.ReceivedBytes != paused.ReceivedBytes ||
                        current.DeliveredBytes != paused.DeliveredBytes ||
                        current.ResourceBytesProcessed !=
                            paused.ResourceBytesProcessed ||
                        current.BufferedBytes != paused.BufferedBytes)
                        return false;
                }
                _pauseObserved = true;
                if (_resource.Resume() != NetworkOperationResult.Success ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PAUSED\r\n"u8) ||
                    !KernelLog.WriteHexLine(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PAUSED_POLLS=0x"u8,
                        (ulong)stablePolls) ||
                    !KernelLog.Write(
                        "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_RESUMED\r\n"u8))
                    return false;
            }
            if (_resource.State == ManagedResourceState.Completed)
                return FinishSuccess();
        }
        return false;
    }

    private bool ObserveTransport(ManagedResourceProgressSnapshot progress)
    {
        if (!_dnsLogged && _resource.ResolvedAddress.IsUsable)
        {
            _dnsLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_DNS_SUCCESS\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_DNS_SUCCESS\r\n"u8))
                return false;
        }
        if (!_tcpLogged && _resource.TcpState == NetworkTcpState.Established)
        {
            _tcpLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_TCP_CONNECTED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_TCP_CONNECTED\r\n"u8))
                return false;
        }
        if (!_tlsLogged && _resource.TlsAuthenticated)
        {
            _tlsLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_TLS_HANDSHAKE_AUTHENTICATED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_HANDSHAKE_AUTHENTICATED\r\n"u8))
                return false;
        }
        if (!_requestLogged && _resource.RequestSent)
        {
            _requestLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_HTTP_REQUEST_ENCRYPTED_SENT\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_REQUEST_ENCRYPTED_SENT\r\n"u8))
                return false;
        }
        if (!_applicationLogged && _resource.ApplicationDataReceived)
        {
            _applicationLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_TLS_APPLICATION_DATA_AUTHENTICATED_DECRYPTED\r\n"u8))
                return false;
        }
        if (!_statusLogged && progress.StatusCode != 0)
        {
            _statusLogged = true;
            if (!KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_HTTP_STATUS_PARSED=0x"u8,
                    (ulong)progress.StatusCode) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_HTTP_STATUS_PARSED=0x"u8,
                    (ulong)progress.StatusCode))
                return false;
        }
        if (!_bodyLogged && progress.ResourceBytesProcessed != 0)
        {
            _bodyLogged = true;
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_BODY_RECEIVED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_RECEIVED\r\n"u8))
                return false;
        }
        return true;
    }

    private bool FinishSuccess()
    {
        ManagedResourceProgressSnapshot progress = _resource.Progress;
        Span<byte> digest = stackalloc byte[ManagedResourceSha256Consumer.DigestSize];
        Span<byte> prefix = stackalloc byte[PrefixLength];
        Span<byte> contentType = stackalloc byte[ManagedHttpLimits.MaximumContentTypeLength];
        if (!_pauseObserved || progress.State != ManagedResourceState.Completed ||
            progress.StatusCode != 200 ||
            progress.TransferMode != ManagedHttpFramingMode.ContentLength ||
            !progress.HasKnownTotalLength || progress.TotalEntityLength != ResourceLength ||
            progress.ReceivedBytes != ResourceLength ||
            progress.DeliveredBytes != ResourceLength ||
            progress.ResourceBytesProcessed != ResourceLength ||
            progress.BufferedBytes != 0 || progress.PauseCount != 1 ||
            progress.ResumeCount != 1 || _count.Count != ResourceLength ||
            !_resource.TryCopyContentType(contentType, out int contentTypeLength) ||
            !contentType[..contentTypeLength].SequenceEqual(ContentType) ||
            !_hash.TryCopyDigest(digest) || !digest.SequenceEqual(ExpectedDigest) ||
            !_prefix.TryCopyPrefix(prefix, out int prefixLength) ||
            prefixLength != PrefixLength || !CheckPattern(prefix))
            return false;

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_STATUS=0x"u8,
                (ulong)progress.StatusCode) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_TRANSFER_MODE=0x"u8,
                (ulong)progress.TransferMode) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_CONTENT_TYPE_LENGTH=0x"u8,
                (ulong)contentTypeLength) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_TOTAL_KNOWN=0x"u8,
                progress.HasKnownTotalLength ? 1UL : 0UL) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_TOTAL_LENGTH=0x"u8,
                (ulong)progress.TotalEntityLength) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_RECEIVED_BYTES=0x"u8,
                (ulong)progress.ReceivedBytes) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_PROCESSED_BYTES=0x"u8,
                (ulong)progress.ResourceBytesProcessed) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_SEGMENTS=0x"u8,
                (ulong)progress.DeliveredSegmentCount) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_PAUSE_COUNT=0x"u8,
                (ulong)progress.PauseCount) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESUME_COUNT=0x"u8,
                (ulong)progress.ResumeCount) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_PEAK_BUFFER=0x"u8,
                (ulong)_peakBuffered) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_HTTPS_PHASE39_PREFIX_BYTES=0x"u8,
                (ulong)prefixLength))
            return false;
        for (int index = 0; index != digest.Length; index += 4)
        {
            uint word = ((uint)digest[index] << 24) |
                        ((uint)digest[index + 1] << 16) |
                        ((uint)digest[index + 2] << 8) | digest[index + 3];
            if (!KernelLog.WriteHexLine(
                    "GXOS_NET10:MANAGED_HTTPS_PHASE39_SHA256_WORD=0x"u8,
                    word)) return false;
        }
        return KernelLog.Write(
                   "GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_URL_VERIFIED\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:MANAGED_HTTPS_PHASE34_FINAL_BODY_VERIFIED\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:MANAGED_HTTPS_PHASE34_TEARDOWN_COMPLETE\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:MANAGED_HTTPS_PHASE34_PASS\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_COMPLETE\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:MANAGED_HTTPS_PHASE39_RESOURCE_PASS\r\n"u8);
    }

    private bool WriteFailure(ManagedResourceProgressSnapshot progress)
    {
        KernelLog.WriteHexLine(
            "GXOS_NET10:MANAGED_HTTPS_PHASE39_FAILURE=0x"u8,
            (ulong)progress.FailureReason);
        return false;
    }

    private NetworkStatus GetStatus()
    {
        // The request owns the service used by the proof; this method exists
        // only to keep the proof's status check at the network boundary.
        return _service.GetStatus();
    }

    private readonly ManagedNetworkService _service;

    private static bool CheckPattern(ReadOnlySpan<byte> value)
    {
        for (int index = 0; index != value.Length; ++index)
            if (value[index] != (byte)((index * 31 + 7) & 0xFF)) return false;
        return true;
    }

    private static byte[] CreateEntropy()
    {
        byte[] entropy = new byte[64];
        ManagedTls12Phase31Fixtures.ClientRandom.CopyTo(entropy, 0);
        entropy[63] = 1;
        return entropy;
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

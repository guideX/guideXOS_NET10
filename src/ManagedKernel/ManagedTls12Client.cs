using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedTls12ClientState : byte
{
    Created = 0,
    NeedInput,
    OutputReady,
    Established,
    Closed,
    Failed
}

internal enum ManagedTls12HandshakeStage : byte
{
    ClientHello = 0,
    ServerHello,
    Certificate,
    ServerKeyExchange,
    ServerHelloDone,
    ClientFlight,
    ServerChangeCipherSpec,
    ServerFinished,
    Established
}

internal enum ManagedTls12FailureKind : byte
{
    Protocol = 0,
    Authentication = 1
}

/* Transport-independent TLS 1.2 client.  This class intentionally implements
   one profile only: ECDHE-ECDSA with P-256 and AES-128-GCM, with EMS required.
   Input is consumed as arbitrary byte chunks and output is drained by the
   caller, so no socket, stream, task, or scheduler is part of the engine. */
internal sealed class ManagedTls12Client
{
    internal const ushort ProtocolVersion = 0x0303;
    internal const ushort CipherSuite = 0xC02B;
    internal const ushort NamedGroupP256 = 23;
    internal const ushort ExtendedMasterSecretExtension = 23;
    internal const int MaximumHandshakeMessageBytes = 4096;
    internal const int MaximumCertificateMessageBytes = 49152;
    internal const int MaximumPeerCertificates = 4;
    internal const int MaximumOutboundFlightBytes = 512;
    /* Application data is drained by the HTTPS composition layer after a
       transport poll.  Keep enough bounded slack for the Phase 23 HTTP
       header/body limits while rejecting a peer that tries to make the TLS
       client an unbounded accumulator. */
    internal const int MaximumPendingApplicationBytes = 2048;
    internal const int CertificateStorageBytes =
        MaximumPeerCertificates * ManagedX509.MaximumCertificateLength;
    internal const int MinimumWorkingStorageBytes = 2 * 1024;
    internal const int CompactWorkingStorageBytes = 2 * 1024;
    internal const int CompactRecordBytes = MaximumOutboundFlightBytes;
    private const int CompactHandshakeBytes = 2 * 1024;
    private const int DefaultRecordBytes =
        ManagedTls12RecordProtection.MaximumRecordSize;

    private enum InternalStage : byte
    {
        Created = 0,
        ExpectServerHello,
        ExpectCertificate,
        ExpectServerKeyExchange,
        ExpectServerHelloDone,
        ExpectServerCcs,
        ExpectServerFinished,
        Established,
        Closed,
        Failed
    }

    private ManagedSecureRandom _random;
    private byte[] _hostname;
    private readonly byte[] _trustedRoot;
    private readonly ManagedX509UtcTime _currentTime;
    private readonly byte[] _workingStorage;
    private readonly byte[] _clientRandom = new byte[32];
    private readonly byte[] _serverRandom = new byte[32];
    private readonly byte[] _privateScalar = new byte[ManagedP256.PrivateScalarSize];
    private readonly byte[] _clientPublicKey = new byte[ManagedP256.PublicKeySize];
    private readonly byte[] _serverPublicKey = new byte[ManagedP256.PublicKeySize];
    private readonly byte[] _premasterSecret = new byte[ManagedP256.SharedSecretSize];
    private readonly byte[] _masterSecret = new byte[48];
    private readonly byte[] _keyBlock = new byte[40];
    private readonly byte[] _sessionHash = new byte[32];
    private readonly byte[] _clientFinishedHash = new byte[32];
    private readonly byte[] _serverFinishedHash = new byte[32];
    private readonly byte[] _clientWriteKey = new byte[16];
    private readonly byte[] _serverWriteKey = new byte[16];
    private readonly byte[] _clientWriteIv = new byte[4];
    private readonly byte[] _serverWriteIv = new byte[4];
    private readonly byte[] _recordHeader = new byte[5];
    private readonly byte[] _recordWire;
    private readonly byte[] _recordPlaintext;
    private byte[] _handshake;
    private readonly byte[] _outbound = new byte[MaximumOutboundFlightBytes];
    private readonly byte[] _pendingApplication =
        new byte[MaximumPendingApplicationBytes];
    private readonly ManagedHmacSha256 _prfHmac = new();
    private readonly ManagedTls12Transcript _transcript;
    private readonly ManagedX509Certificate[] _peerCertificates =
        new ManagedX509Certificate[MaximumPeerCertificates];
    private readonly int[] _peerCertificateOffsets =
        new int[MaximumPeerCertificates];
    private readonly int[] _peerCertificateLengths =
        new int[MaximumPeerCertificates];

    private InternalStage _stage;
    private ManagedTls12HandshakeStage _lastHandshake;
    private ManagedTls12FailureKind _failureKind;
    private int _recordHeaderLength;
    private int _recordBodyLength;
    private int _expectedRecordBodyLength = -1;
    private int _handshakeLength;
    private int _expectedHandshakeLength;
    private int _certificateStorageLength;
    private int _peerCertificateCount;
    private byte _peerCertificateAlgorithmMask;
    private int _outboundLength;
    private int _applicationPlaintextLength;
    private int _pendingApplicationLength;
    private bool _serverEms;
    private bool _clientWriteActive;
    private bool _serverReadActive;
    private ulong _clientSequence;
    private ulong _serverSequence;
    private bool _trustAnchorMatched;

    private ManagedTls12Client(ReadOnlySpan<byte> hostname,
                               ReadOnlySpan<byte> trustedRoot,
                               in ManagedX509UtcTime currentTime,
                               ManagedSecureRandom random,
                               byte[] workingStorage,
                               int maximumRecordBytes)
    {
        _hostname = hostname.ToArray();
        _trustedRoot = trustedRoot.ToArray();
        _currentTime = currentTime;
        _random = random;
        _workingStorage = workingStorage;
        _recordWire = new byte[maximumRecordBytes];
        _recordPlaintext = new byte[maximumRecordBytes -
                                    ManagedTls12RecordProtection.HeaderSize];
        _handshake = new byte[maximumRecordBytes == CompactRecordBytes
            ? CompactHandshakeBytes : MaximumHandshakeMessageBytes + 4];
        _transcript = new ManagedTls12Transcript(
            maximumRecordBytes == CompactRecordBytes ? 2 * 1024 : 4 * 1024);
        _stage = InternalStage.Created;
        _lastHandshake = ManagedTls12HandshakeStage.ClientHello;
    }

    internal static bool TryCreate(ReadOnlySpan<byte> hostname,
                                   ReadOnlySpan<byte> trustedRoot,
                                   in ManagedX509UtcTime currentTime,
                                   ManagedSecureRandom? random,
                                   byte[]? workingStorage,
                                   out ManagedTls12Client? client,
                                   int maximumRecordBytes = DefaultRecordBytes)
    {
        client = null;
        if (random == null || workingStorage == null ||
            workingStorage.Length < MinimumWorkingStorageBytes ||
            maximumRecordBytes < ManagedTls12RecordProtection.HeaderSize +
                                 ManagedTls12RecordProtection.ExplicitNonceSize +
                                 ManagedAesGcm.TagSize ||
            maximumRecordBytes > DefaultRecordBytes ||
            trustedRoot.Length == 0 ||
            trustedRoot.Length > ManagedX509.MaximumCertificateLength ||
            !currentTime.IsValid ||
            !ManagedX509.IsValidDnsNameForTest(hostname, false) ||
            hostname.Length > ManagedX509.MaximumDnsNameLength)
            return false;
        client = new ManagedTls12Client(hostname, trustedRoot,
                                        in currentTime, random,
                                        workingStorage, maximumRecordBytes);
        return true;
    }

    internal ManagedTls12ClientState State => GetState();
    internal ManagedTls12HandshakeStage LastHandshake => _lastHandshake;
    internal ManagedTls12FailureKind FailureKind => _failureKind;
    internal bool EmsNegotiated => _serverEms;
    internal int PeerCertificateCount => _peerCertificateCount;
    internal byte PeerCertificateAlgorithmMask => _peerCertificateAlgorithmMask;
    internal bool TrustAnchorMatched => _trustAnchorMatched;
    internal int TranscriptLength => _transcript.Length;
    internal int HandshakeBytesPending => _handshakeLength;
    internal int ExpectedHandshakeLength => _expectedHandshakeLength;

    /* Proof-only observation of derived values. Production callers do not
       need these bytes; the NativeAOT/host KATs compare them without logging
       them. */
    internal bool TryCopyProofMaterial(Span<byte> sessionHash,
                                       Span<byte> masterSecret,
                                       Span<byte> keyBlock,
                                       Span<byte> clientFinishedHash,
                                       Span<byte> serverFinishedHash)
    {
        if (_stage != InternalStage.Established || sessionHash.Length < 32 ||
            masterSecret.Length < 48 || keyBlock.Length < 40 ||
            clientFinishedHash.Length < 32 || serverFinishedHash.Length < 32)
            return false;
        _sessionHash.CopyTo(sessionHash);
        _masterSecret.CopyTo(masterSecret);
        _keyBlock.CopyTo(keyBlock);
        _clientFinishedHash.CopyTo(clientFinishedHash);
        _serverFinishedHash.CopyTo(serverFinishedHash);
        return true;
    }
    internal int ApplicationPlaintextLength => _applicationPlaintextLength;
    internal ReadOnlySpan<byte> ApplicationPlaintext =>
        _recordPlaintext.AsSpan(0, _applicationPlaintextLength);

    internal int PendingApplicationDataLength => _pendingApplicationLength;

    internal bool TryTakeApplicationData(Span<byte> destination, out int written)
    {
        written = 0;
        if (_stage != InternalStage.Established && _stage != InternalStage.Closed)
            return false;
        if (_pendingApplicationLength == 0)
            return true;
        if (destination.Length < _pendingApplicationLength)
            return false;
        _pendingApplication.AsSpan(0, _pendingApplicationLength).CopyTo(destination);
        written = _pendingApplicationLength;
        _pendingApplication.AsSpan(0, _pendingApplicationLength).Clear();
        _pendingApplicationLength = 0;
        return true;
    }

    /* A transport EOF is valid after the handshake only when no TLS record or
       handshake fragment is truncated.  HTTPS decides separately whether
       HTTP framing permits that EOF; close_notify is therefore optional for
       the Connection: close profile. */
    internal bool TryNotifyTransportClosed()
    {
        if (_stage == InternalStage.Closed)
            return true;
        if (_stage != InternalStage.Established ||
            _recordHeaderLength != 0 || _recordBodyLength != 0 ||
            _handshakeLength != 0)
        {
            Fail();
            return false;
        }
        _stage = InternalStage.Closed;
        return true;
    }

    internal bool TryStart(Span<byte> destination, out int written)
    {
        written = 0;
        int expectedHandshakeLength = 4 + 78 + _hostname.Length;
        if (_stage != InternalStage.Created ||
            destination.Length < 5 + expectedHandshakeLength)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_CLIENT_START_STAGE=0x"u8,
                (ulong)_stage);
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_CLIENT_START_BUFFER=0x"u8,
                (ulong)destination.Length);
            return false;
        }

        Span<byte> handshake = stackalloc byte[512];
        if (!_random.TryFill(_clientRandom))
        {
            KernelLog.Write(
                "GXOS_NET10:PUBLIC_TLS_CLIENT_RANDOM_ENTROPY_FAILED\r\n"u8);
            Fail();
            handshake.Clear();
            return false;
        }
        if (!BuildClientHello(handshake, out int handshakeLength))
        {
            KernelLog.Write(
                "GXOS_NET10:PUBLIC_TLS_CLIENT_HELLO_BUILD_FAILED\r\n"u8);
            Fail();
            handshake.Clear();
            return false;
        }

        int total = 5 + handshakeLength;
        destination[0] = ManagedTls12RecordProtection.Handshake;
        destination[1] = 3;
        destination[2] = 3;
        ManagedTls12RecordProtection.WriteUInt16(
            (ushort)handshakeLength, destination[3..]);
        handshake[..handshakeLength].CopyTo(destination[5..]);
        if (!_transcript.Append(handshake[..handshakeLength]))
        {
            handshake.Clear();
            Fail();
            return false;
        }
        _stage = InternalStage.ExpectServerHello;
        _lastHandshake = ManagedTls12HandshakeStage.ClientHello;
        written = total;
        handshake.Clear();
        return true;
    }

    internal bool TryConsume(ReadOnlySpan<byte> input)
    {
        if (_stage == InternalStage.Failed || _stage == InternalStage.Closed ||
            _stage == InternalStage.Created || _outboundLength != 0)
            return false;

        while (!input.IsEmpty && _stage != InternalStage.Failed &&
               _stage != InternalStage.Closed)
        {
            if (_recordHeaderLength != ManagedTls12RecordProtection.HeaderSize)
            {
                int count = Math.Min(
                    input.Length,
                    ManagedTls12RecordProtection.HeaderSize -
                    _recordHeaderLength);
                input[..count].CopyTo(_recordHeader.AsSpan(
                    _recordHeaderLength));
                _recordHeaderLength += count;
                input = input[count..];
                if (_recordHeaderLength != ManagedTls12RecordProtection.HeaderSize)
                    break;
                if (!BeginRecord())
                {
                    Fail();
                    break;
                }
            }

            int bodyCount = Math.Min(
                input.Length, _expectedRecordBodyLength - _recordBodyLength);
            input[..bodyCount].CopyTo(_recordWire.AsSpan(
                ManagedTls12RecordProtection.HeaderSize + _recordBodyLength));
            _recordBodyLength += bodyCount;
            input = input[bodyCount..];
            if (_recordBodyLength != _expectedRecordBodyLength)
                break;

            _recordHeader.CopyTo(_recordWire, 0);
            _recordHeader.CopyTo(_recordWire, 0);
            ProcessRecord();
            _recordHeaderLength = 0;
            _recordBodyLength = 0;
            _expectedRecordBodyLength = -1;
        }
        return _stage != InternalStage.Failed;
    }

    internal bool TryTakeOutput(Span<byte> destination, out int written)
    {
        written = 0;
        if (_outboundLength == 0)
            return true;
        if (destination.Length < _outboundLength)
            return false;
        _outbound.AsSpan(0, _outboundLength).CopyTo(destination);
        written = _outboundLength;
        _outbound.AsSpan(0, _outboundLength).Clear();
        _outboundLength = 0;
        return true;
    }

    internal bool TryEncryptApplicationData(ReadOnlySpan<byte> plaintext,
                                            Span<byte> destination,
                                            out int written)
    {
        written = 0;
        if (_stage != InternalStage.Established || !_clientWriteActive ||
            !_serverEms)
            return false;
        if (!ManagedTls12RecordProtection.TryEncrypt(
                _clientSequence, _clientWriteKey, _clientWriteIv,
                ManagedTls12RecordProtection.ApplicationData, plaintext,
                destination, out written))
            return false;
        _clientSequence++;
        return true;
    }

    internal bool TryDecryptApplicationData(ReadOnlySpan<byte> record,
                                            Span<byte> plaintext,
                                            out int written)
    {
        written = 0;
        if (_stage != InternalStage.Established || !_serverReadActive ||
            !_serverEms)
            return false;
        if (!ManagedTls12RecordProtection.TryDecrypt(
                _serverSequence, _serverWriteKey, _serverWriteIv,
                ManagedTls12RecordProtection.ApplicationData, record,
                plaintext, out written))
        {
            Fail();
            return false;
        }
        _serverSequence++;
        return true;
    }

    internal bool TryReadApplicationData(ReadOnlySpan<byte> record)
    {
        if (_stage != InternalStage.Established ||
            record.Length > _recordWire.Length)
            return false;
        _recordPlaintext.AsSpan().Clear();
        _pendingApplication.AsSpan().Clear();
        if (!TryDecryptApplicationData(record, _recordPlaintext,
                                        out _applicationPlaintextLength))
        {
            _applicationPlaintextLength = 0;
            return false;
        }
        return true;
    }

    internal void Teardown()
    {
        _clientRandom.AsSpan().Clear();
        _serverRandom.AsSpan().Clear();
        _privateScalar.AsSpan().Clear();
        _clientPublicKey.AsSpan().Clear();
        _serverPublicKey.AsSpan().Clear();
        _premasterSecret.AsSpan().Clear();
        _masterSecret.AsSpan().Clear();
        _keyBlock.AsSpan().Clear();
        _sessionHash.AsSpan().Clear();
        _clientFinishedHash.AsSpan().Clear();
        _serverFinishedHash.AsSpan().Clear();
        _clientWriteKey.AsSpan().Clear();
        _serverWriteKey.AsSpan().Clear();
        _clientWriteIv.AsSpan().Clear();
        _serverWriteIv.AsSpan().Clear();
        _recordHeader.AsSpan().Clear();
        _recordWire.AsSpan().Clear();
        _recordPlaintext.AsSpan().Clear();
        _handshake.AsSpan().Clear();
        _outbound.AsSpan().Clear();
        _workingStorage.AsSpan().Clear();
        _prfHmac.Clear();
        _transcript.Clear();
        Array.Clear(_peerCertificates);
        _peerCertificateOffsets.AsSpan().Clear();
        _peerCertificateLengths.AsSpan().Clear();
        _recordHeaderLength = 0;
        _recordBodyLength = 0;
        _expectedRecordBodyLength = -1;
        _handshakeLength = 0;
        _expectedHandshakeLength = 0;
        _certificateStorageLength = 0;
        _peerCertificateCount = 0;
        _outboundLength = 0;
        _applicationPlaintextLength = 0;
        _pendingApplicationLength = 0;
        _serverEms = false;
        _clientWriteActive = false;
        _serverReadActive = false;
        _clientSequence = 0;
        _serverSequence = 0;
        _stage = InternalStage.Closed;
    }

    /* Reopen an explicitly torn-down client with caller-supplied entropy.
       This reuses the bounded buffers and is useful to a kernel allocator
       that wants a fresh connection without another large object graph. */
    internal bool TryReset(ManagedSecureRandom? random)
    {
        return TryReset(_hostname, random);
    }

    /* Reopen the bounded TLS workspace for a new HTTPS hop.  The handshake
       state, sequence numbers, transcript, keys, and certificate buffers are
       cleared by Teardown(); replacing the hostname makes SNI and the
       certificate-name check fresh for the new authority without allocating
       another certificate-sized object graph. */
    internal bool TryReset(ReadOnlySpan<byte> hostname,
                           ManagedSecureRandom? random)
    {
        if (_stage != InternalStage.Closed || random == null ||
            !ManagedX509.IsValidDnsNameForTest(hostname, false) ||
            hostname.Length > ManagedX509.MaximumDnsNameLength)
            return false;
        _hostname = hostname.ToArray();
        _random = random;
        _failureKind = ManagedTls12FailureKind.Protocol;
        _lastHandshake = ManagedTls12HandshakeStage.ClientHello;
        _stage = InternalStage.Created;
        return true;
    }

    private ManagedTls12ClientState GetState()
    {
        if (_stage == InternalStage.Failed) return ManagedTls12ClientState.Failed;
        if (_stage == InternalStage.Closed) return ManagedTls12ClientState.Closed;
        if (_stage == InternalStage.Established)
            return ManagedTls12ClientState.Established;
        if (_outboundLength != 0) return ManagedTls12ClientState.OutputReady;
        return ManagedTls12ClientState.NeedInput;
    }

    private bool BeginRecord()
    {
        if (_recordHeader[1] != 3 || _recordHeader[2] != 3 ||
            !IsContentType(_recordHeader[0]))
            return false;
        int length = (_recordHeader[3] << 8) | _recordHeader[4];
        bool protectedRecord = _serverReadActive;
        int maximum = Math.Min(
            protectedRecord
                ? ManagedTls12RecordProtection.MaximumCiphertextFragment
                : ManagedTls12RecordProtection.MaximumPlaintextFragment,
            _recordWire.Length - ManagedTls12RecordProtection.HeaderSize);
        if (length > maximum || length == 0)
            return false;
        if (protectedRecord && length <
            ManagedTls12RecordProtection.ExplicitNonceSize +
            ManagedAesGcm.TagSize)
            return false;
        _expectedRecordBodyLength = length;
        _recordBodyLength = 0;
        return true;
    }

    private void ProcessRecord()
    {
        byte type = _recordHeader[0];
        if (type == ManagedTls12RecordProtection.Alert && !_serverReadActive)
        {
            ProcessAlert();
            return;
        }

        if (_serverReadActive)
        {
            if (type != ManagedTls12RecordProtection.Alert &&
                type != ManagedTls12RecordProtection.Handshake &&
                type != ManagedTls12RecordProtection.ApplicationData)
            {
                Fail();
                return;
            }
            if (type == ManagedTls12RecordProtection.ApplicationData)
            {
                if (_stage != InternalStage.Established ||
                    !TryQueueApplicationData(_recordWire.AsSpan(0,
                        ManagedTls12RecordProtection.HeaderSize +
                        _recordBodyLength)))
                    Fail(ManagedTls12FailureKind.Authentication);
                return;
            }
            if (type == ManagedTls12RecordProtection.Alert)
            {
                if (!ManagedTls12RecordProtection.TryDecrypt(
                        _serverSequence, _serverWriteKey, _serverWriteIv,
                        ManagedTls12RecordProtection.Alert,
                        _recordWire.AsSpan(0,
                            ManagedTls12RecordProtection.HeaderSize +
                            _recordBodyLength), _recordPlaintext,
                        out int alertLength))
                {
                    Fail(ManagedTls12FailureKind.Authentication);
                    return;
                }
                _serverSequence++;
                ProcessAlert(_recordPlaintext.AsSpan(0, alertLength));
                _recordPlaintext.AsSpan(0, alertLength).Clear();
                return;
            }
            if (!ManagedTls12RecordProtection.TryDecrypt(
                    _serverSequence, _serverWriteKey, _serverWriteIv,
                    ManagedTls12RecordProtection.Handshake,
                    _recordWire.AsSpan(0,
                        ManagedTls12RecordProtection.HeaderSize +
                        _recordBodyLength), _recordPlaintext,
                    out int plaintextLength))
            {
                Fail(ManagedTls12FailureKind.Authentication);
                return;
            }
            _serverSequence++;
            if (!AppendHandshakeBytes(_recordPlaintext.AsSpan(0,
                                                               plaintextLength)))
                Fail();
            _recordPlaintext.AsSpan(0, plaintextLength).Clear();
            return;
        }

        if (type == ManagedTls12RecordProtection.Handshake)
        {
            if (!AppendHandshakeBytes(_recordWire.AsSpan(
                    ManagedTls12RecordProtection.HeaderSize, _recordBodyLength)))
                Fail();
            return;
        }
        if (type == ManagedTls12RecordProtection.ChangeCipherSpec)
        {
            if (_stage != InternalStage.ExpectServerCcs ||
                _handshakeLength != 0 || _recordBodyLength != 1 ||
                _recordWire[ManagedTls12RecordProtection.HeaderSize] != 1)
            {
                Fail();
                return;
            }
            _serverReadActive = true;
            _serverSequence = 0;
            _stage = InternalStage.ExpectServerFinished;
            _lastHandshake = ManagedTls12HandshakeStage.ServerChangeCipherSpec;
            return;
        }
        Fail();
    }

    private bool AppendHandshakeBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return false;
        while (!bytes.IsEmpty)
        {
            if (_handshakeLength < 4)
            {
                _handshake[_handshakeLength++] = bytes[0];
                bytes = bytes[1..];
                if (_handshakeLength == 4)
                {
                    int bodyLength = (_handshake[1] << 16) |
                                     (_handshake[2] << 8) | _handshake[3];
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PUBLIC_TLS_HANDSHAKE_HEADER_TYPE=0x"u8,
                        _handshake[0]);
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PUBLIC_TLS_HANDSHAKE_HEADER_LENGTH=0x"u8,
                        (ulong)bodyLength);
                    int maximum = _handshake[0] == 11
                        ? MaximumCertificateMessageBytes
                        : MaximumHandshakeMessageBytes;
                    if (bodyLength > maximum ||
                        !EnsureHandshakeCapacity(bodyLength + 4))
                        return false;
                    _expectedHandshakeLength = bodyLength + 4;
                }
            }
            else
            {
                int count = Math.Min(bytes.Length,
                    _expectedHandshakeLength - _handshakeLength);
                bytes[..count].CopyTo(_handshake.AsSpan(_handshakeLength));
                _handshakeLength += count;
                bytes = bytes[count..];
            }

            if (_handshakeLength != _expectedHandshakeLength)
                continue;

            if (!ProcessHandshakeMessage(_handshake.AsSpan(0,
                                                            _handshakeLength)))
                return false;
            _handshake.AsSpan(0, _handshakeLength).Clear();
            _handshakeLength = 0;
            _expectedHandshakeLength = 0;
        }
        return true;
    }

    private bool EnsureHandshakeCapacity(int required)
    {
        if (required <= _handshake.Length)
            return true;
        if (required > MaximumCertificateMessageBytes + 4)
            return false;
        int capacity = _handshake.Length;
        while (capacity < required)
        {
            if (capacity >= (MaximumCertificateMessageBytes + 4) / 2)
            {
                capacity = MaximumCertificateMessageBytes + 4;
                break;
            }
            capacity *= 2;
        }
        byte[] expanded = new byte[capacity];
        _handshake.AsSpan(0, _handshakeLength).CopyTo(expanded);
        _handshake.AsSpan().Clear();
        _handshake = expanded;
        return true;
    }

    private bool ProcessHandshakeMessage(ReadOnlySpan<byte> message)
    {
        byte type = message[0];
        int bodyLength = (message[1] << 16) | (message[2] << 8) | message[3];
        ReadOnlySpan<byte> body = message[4..];
        if (body.Length != bodyLength) return false;

        if (type == 20)
        {
            if (_stage != InternalStage.ExpectServerFinished || bodyLength != 12)
                return false;
            if (!_transcript.TryHash(_serverFinishedHash) ||
                !VerifyFinished("server finished"u8, body))
            {
                _failureKind = ManagedTls12FailureKind.Authentication;
                return false;
            }
            if (!_transcript.Append(message)) return false;
            _stage = InternalStage.Established;
            _lastHandshake = ManagedTls12HandshakeStage.ServerFinished;
            return true;
        }

        if (_stage == InternalStage.ExpectServerFinished ||
            _stage == InternalStage.Established)
            return false;
        if (!_transcript.Append(message)) return false;

        switch (type)
        {
            case 2:
                if (_stage != InternalStage.ExpectServerHello ||
                    !ParseServerHello(body)) return false;
                _stage = InternalStage.ExpectCertificate;
                _lastHandshake = ManagedTls12HandshakeStage.ServerHello;
                return true;
            case 11:
                if (_stage != InternalStage.ExpectCertificate ||
                    !ParseCertificate(body)) return false;
                _stage = InternalStage.ExpectServerKeyExchange;
                _lastHandshake = ManagedTls12HandshakeStage.Certificate;
                return true;
            case 12:
                if (_stage != InternalStage.ExpectServerKeyExchange ||
                    !ParseServerKeyExchange(body)) return false;
                _stage = InternalStage.ExpectServerHelloDone;
                _lastHandshake = ManagedTls12HandshakeStage.ServerKeyExchange;
                return true;
            case 14:
                if (_stage != InternalStage.ExpectServerHelloDone ||
                    bodyLength != 0 || !PrepareClientFlight()) return false;
                _lastHandshake = ManagedTls12HandshakeStage.ServerHelloDone;
                return true;
            default:
                return false;
        }
    }

    private bool ParseServerHello(ReadOnlySpan<byte> body)
    {
        if (body.Length < 40 || body[0] != 3 || body[1] != 3)
            return false;
        body.Slice(2, 32).CopyTo(_serverRandom);
        int offset = 34;
        int sessionIdLength = body[offset++];
        KernelLog.WriteHexLine(
            "GXOS_NET10:PUBLIC_TLS_SERVER_SESSION_ID_LENGTH=0x"u8,
            (ulong)sessionIdLength);
        if (sessionIdLength > 32 || sessionIdLength > body.Length - offset)
            return false;
        offset += sessionIdLength;
        if (body.Length - offset < 5 ||
            ((body[offset] << 8) | body[offset + 1]) != CipherSuite ||
            body[offset + 2] != 0)
            return false;
        KernelLog.WriteHexLine(
            "GXOS_NET10:PUBLIC_TLS_SERVER_CIPHER=0x"u8,
            (ulong)((body[offset] << 8) | body[offset + 1]));
        offset += 3;
        int extensionLength = (body[offset] << 8) | body[offset + 1];
        offset += 2;
        KernelLog.WriteHexLine(
            "GXOS_NET10:PUBLIC_TLS_SERVER_EXTENSION_LENGTH=0x"u8,
            (ulong)extensionLength);
        if (extensionLength != body.Length - offset) return false;

        bool ems = false;
        while (offset < body.Length)
        {
            if (body.Length - offset < 4) return false;
            ushort type = (ushort)((body[offset] << 8) | body[offset + 1]);
            int length = (body[offset + 2] << 8) | body[offset + 3];
            offset += 4;
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_SERVER_EXTENSION_TYPE=0x"u8,
                type);
            if (length > body.Length - offset) return false;
            ReadOnlySpan<byte> extension = body.Slice(offset, length);
            bool valid = type switch
            {
                0 => length == 0, // server_name acknowledgement
                11 => length == 2 && extension[0] == 1 && extension[1] == 0,
                35 => length == 0, // session_ticket acknowledgement
                0xFF01 => length == 1 && extension[0] == 0,
                ExtendedMasterSecretExtension => length == 0 && !ems,
                _ => false
            };
            if (!valid) return false;
            if (type == ExtendedMasterSecretExtension) ems = true;
            offset += length;
        }
        _serverEms = ems;
        return ems;
    }

    private bool ParseCertificate(ReadOnlySpan<byte> body)
    {
        KernelLog.WriteHexLine(
            "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_BODY_LENGTH=0x"u8,
            (ulong)body.Length);
        if (body.Length < 3 || body.Length - 3 > MaximumCertificateMessageBytes)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8, 1UL);
            return false;
        }
        int listLength = (body[0] << 16) | (body[1] << 8) | body[2];
        KernelLog.WriteHexLine(
            "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_LIST_LENGTH=0x"u8,
            (ulong)listLength);
        if (listLength != body.Length - 3 || listLength == 0)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8, 2UL);
            return false;
        }
        int offset = 3;
        int end = body.Length;
        _peerCertificateCount = 0;
        _peerCertificateAlgorithmMask = 0;
        _trustAnchorMatched = false;
        _certificateStorageLength = 0;
        while (offset < end)
        {
            if (_peerCertificateCount == MaximumPeerCertificates ||
                end - offset < 3)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8,
                    3UL);
                return false;
            }
            int length = (body[offset] << 16) | (body[offset + 1] << 8) |
                         body[offset + 2];
            offset += 3;
            if (length == 0 || length > ManagedX509.MaximumCertificateLength ||
                length > end - offset ||
                length > _workingStorage.Length - _certificateStorageLength)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8,
                    4UL);
                return false;
            }
            ReadOnlySpan<byte> wireCertificate = body.Slice(offset, length);
            int storageOffset = _certificateStorageLength;
            wireCertificate.CopyTo(
                _workingStorage.AsSpan(storageOffset));
            ReadOnlySpan<byte> certificate = _workingStorage.AsSpan(
                storageOffset, length);
            ManagedX509ValidationStatus parseStatus =
                ManagedX509.TryParseCertificate(
                    certificate, out ManagedX509Certificate parsed,
                    out ManagedX509ValidationStatus certificateStatus);
            if (parseStatus != ManagedX509ValidationStatus.Success)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_INDEX=0x"u8,
                    (ulong)_peerCertificateCount);
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_STATUS=0x"u8,
                    (ulong)certificateStatus);
                return false;
            }
            _peerCertificateOffsets[_peerCertificateCount] = storageOffset;
            _peerCertificateLengths[_peerCertificateCount] = length;
            _peerCertificates[_peerCertificateCount] = parsed;
            _peerCertificateAlgorithmMask |=
                CertificateAlgorithmBit(in parsed);
            _peerCertificateCount++;
            _certificateStorageLength += length;
            offset += length;
        }
        if (offset != end || _peerCertificateCount == 0)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8, 5UL);
            return false;
        }

        ReadOnlySpan<byte> candidateRoot = _trustedRoot;
        int certificateCount = _peerCertificateCount;
        if (certificateCount > 1)
        {
            int lastIndex = certificateCount - 1;
            ManagedX509TrustAnchorMatch anchorMatch =
                ManagedX509TrustAnchorMatch.None;
            if (!ManagedX509.TryMatchTrustAnchorIdentity(
                    GetPeerCertificate(lastIndex),
                    in _peerCertificates[lastIndex], _trustedRoot,
                    out anchorMatch))
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8,
                    6UL);
                _failureKind = ManagedTls12FailureKind.Authentication;
                return false;
            }
            if (anchorMatch == ManagedX509TrustAnchorMatch.SubjectKeyMismatch)
            {
                KernelLog.Write(
                    "GXOS_NET10:PUBLIC_TLS_TRUST_ANCHOR_KEY_MISMATCH\r\n"u8);
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8,
                    7UL);
                _failureKind = ManagedTls12FailureKind.Authentication;
                return false;
            }
            if (anchorMatch == ManagedX509TrustAnchorMatch.Match)
            {
                candidateRoot = GetPeerCertificate(lastIndex);
                _trustAnchorMatched = true;
                certificateCount--;
            }
        }
        if (certificateCount > 3)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_PARSE_FAILURE=0x"u8, 8UL);
            return false;
        }
        ReadOnlySpan<byte> intermediate1 = ReadOnlySpan<byte>.Empty;
        ReadOnlySpan<byte> intermediate2 = ReadOnlySpan<byte>.Empty;
        int intermediateCount = certificateCount - 1;
        if (intermediateCount > 0) intermediate1 = GetPeerCertificate(1);
        if (intermediateCount > 1) intermediate2 = GetPeerCertificate(2);
        if (intermediateCount > 2) return false;

        if (!ManagedX509.TryValidateServerChain(
            GetPeerCertificate(0), intermediate1, intermediate2,
            candidateRoot, _trustedRoot, in _currentTime, _hostname,
            out ManagedX509ValidationStatus validationStatus))
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_CERTIFICATE_VALIDATION_STATUS=0x"u8,
                (ulong)validationStatus);
            _failureKind = ManagedTls12FailureKind.Authentication;
            return false;
        }
        return true;
    }

    private bool ParseServerKeyExchange(ReadOnlySpan<byte> body)
    {
        const int parametersLength = 1 + 2 + 1 + ManagedP256.PublicKeySize;
        if (body.Length < parametersLength + 4 || body[0] != 3 ||
            body[1] != 0 || body[2] != NamedGroupP256 ||
            body[3] != ManagedP256.PublicKeySize || body[4] != 4 ||
            !ManagedP256.TryValidatePublicKey(body.Slice(4, 65)))
            return false;
        body.Slice(4, 65).CopyTo(_serverPublicKey);
        int offset = parametersLength;
        if (body[offset] != 4 || body[offset + 1] != 3)
            return false;
        int signatureLength = (body[offset + 2] << 8) | body[offset + 3];
        offset += 4;
        if (signatureLength == 0 || signatureLength > ManagedP256.MaximumDerSignatureSize ||
            signatureLength != body.Length - offset)
            return false;

        Span<byte> signed = stackalloc byte[32 + 32 + parametersLength];
        Span<byte> digest = stackalloc byte[32];
        try
        {
            _clientRandom.CopyTo(signed);
            _serverRandom.CopyTo(signed[32..]);
            body[..parametersLength].CopyTo(signed[64..]);
            if (!ManagedSha256.TryHash(signed, digest) ||
                _peerCertificateCount == 0)
                return false;
            ManagedX509Certificate leaf = _peerCertificates[0];
            ReadOnlySpan<byte> leafKey = _workingStorage.AsSpan(
                _peerCertificateOffsets[0] + leaf.PublicKeyOffset,
                leaf.PublicKeyLength);
            bool verified = ManagedP256.TryVerifyDerSignature(
                digest, leafKey, body.Slice(offset, signatureLength));
            if (!verified) _failureKind = ManagedTls12FailureKind.Authentication;
            return verified;
        }
        finally
        {
            signed.Clear();
            digest.Clear();
        }
    }

    private bool PrepareClientFlight()
    {
        if (!_serverEms || _peerCertificateCount == 0 ||
            !ManagedP256.TryGeneratePrivateKey(_random, _privateScalar))
            return false;

        if (!ManagedP256.TryDerivePublicKey(_privateScalar, _clientPublicKey))
            return false;

        if (!ManagedP256.TryDeriveSharedSecret(_privateScalar,
                                               _serverPublicKey,
                                               _premasterSecret))
            return false;

        Span<byte> clientKeyExchange = stackalloc byte[70];
        Span<byte> sessionHash = stackalloc byte[32];
        Span<byte> keySeed = stackalloc byte[64];
        Span<byte> keyBlock = stackalloc byte[40];
        Span<byte> transcriptHash = stackalloc byte[32];
        Span<byte> verifyData = stackalloc byte[12];
        Span<byte> finished = stackalloc byte[16];
        try
        {
            clientKeyExchange[0] = 16;
            ManagedTls12RecordProtection.WriteUInt24(66,
                                                     clientKeyExchange[1..]);
            clientKeyExchange[4] = ManagedP256.PublicKeySize;
            _clientPublicKey.CopyTo(clientKeyExchange[5..]);
            if (!_transcript.Append(clientKeyExchange)) return false;
            if (!_transcript.TryHash(sessionHash) ||
                !ManagedTls12Prf.TryCompute(
                    _premasterSecret, "extended master secret"u8,
                    sessionHash, _masterSecret, _prfHmac))
                return false;

            _serverRandom.CopyTo(keySeed);
            _clientRandom.CopyTo(keySeed[32..]);
            if (!ManagedTls12Prf.TryCompute(_masterSecret, "key expansion"u8,
                                             keySeed, keyBlock, _prfHmac))
                return false;
            keyBlock.CopyTo(_keyBlock);
            sessionHash.CopyTo(_sessionHash);
            keyBlock[..16].CopyTo(_clientWriteKey);
            keyBlock.Slice(16, 16).CopyTo(_serverWriteKey);
            keyBlock.Slice(32, 4).CopyTo(_clientWriteIv);
            keyBlock.Slice(36, 4).CopyTo(_serverWriteIv);

            if (!_transcript.TryHash(transcriptHash) ||
                !ManagedTls12Prf.TryCompute(
                    _masterSecret, "client finished"u8,
                    transcriptHash, verifyData, _prfHmac))
                return false;
            transcriptHash.CopyTo(_clientFinishedHash);
            finished[0] = 20;
            ManagedTls12RecordProtection.WriteUInt24(12, finished[1..]);
            verifyData.CopyTo(finished[4..]);
            if (!_transcript.Append(finished)) return false;

            int offset = 0;
            if (_outbound.Length < 5 + clientKeyExchange.Length + 6 + 45)
                return false;
            _outbound[offset++] = ManagedTls12RecordProtection.Handshake;
            _outbound[offset++] = 3;
            _outbound[offset++] = 3;
            ManagedTls12RecordProtection.WriteUInt16(
                (ushort)clientKeyExchange.Length, _outbound.AsSpan(offset));
            offset += 2;
            clientKeyExchange.CopyTo(_outbound.AsSpan(offset));
            offset += clientKeyExchange.Length;
            _outbound[offset++] = ManagedTls12RecordProtection.ChangeCipherSpec;
            _outbound[offset++] = 3;
            _outbound[offset++] = 3;
            _outbound[offset++] = 0;
            _outbound[offset++] = 1;
            _outbound[offset++] = 1;

            _clientWriteActive = true;
            _clientSequence = 0;
            if (!ManagedTls12RecordProtection.TryEncrypt(
                    _clientSequence, _clientWriteKey, _clientWriteIv,
                    ManagedTls12RecordProtection.Handshake, finished,
                    _outbound.AsSpan(offset), out int encryptedLength))
                return false;
            offset += encryptedLength;
            _clientSequence++;
            _outboundLength = offset;
            _stage = InternalStage.ExpectServerCcs;
            _lastHandshake = ManagedTls12HandshakeStage.ClientFlight;
            return true;
        }
        finally
        {
            clientKeyExchange.Clear();
            sessionHash.Clear();
            keySeed.Clear();
            keyBlock.Clear();
            transcriptHash.Clear();
            verifyData.Clear();
            finished.Clear();
            _premasterSecret.AsSpan().Clear();
        }
    }

    private bool VerifyFinished(ReadOnlySpan<byte> label,
                                ReadOnlySpan<byte> actual)
    {
        Span<byte> transcriptHash = stackalloc byte[32];
        Span<byte> expected = stackalloc byte[12];
        try
        {
            return _transcript.TryHash(transcriptHash) &&
                   ManagedTls12Prf.TryCompute(_masterSecret, label,
                                               transcriptHash, expected,
                                               _prfHmac) &&
                   ManagedCryptoComparison.FixedTimeEquals(expected, actual);
        }
        finally
        {
            transcriptHash.Clear();
            expected.Clear();
        }
    }

    private ReadOnlySpan<byte> GetPeerCertificate(int index)
    {
        return _workingStorage.AsSpan(_peerCertificateOffsets[index],
                                      _peerCertificateLengths[index]);
    }

    private static byte CertificateAlgorithmBit(
        in ManagedX509Certificate certificate)
    {
        if (certificate.PublicKeyAlgorithm ==
                ManagedX509PublicKeyAlgorithm.EcdsaP256 &&
            certificate.SignatureAlgorithm ==
                ManagedX509SignatureAlgorithm.EcdsaSha256)
            return 0x01;
        if (certificate.PublicKeyAlgorithm ==
                ManagedX509PublicKeyAlgorithm.EcdsaP256 &&
            certificate.SignatureAlgorithm ==
                ManagedX509SignatureAlgorithm.EcdsaSha384)
            return 0x02;
        if (certificate.PublicKeyAlgorithm ==
                ManagedX509PublicKeyAlgorithm.EcdsaP384 &&
            certificate.SignatureAlgorithm ==
                ManagedX509SignatureAlgorithm.RsaSha256)
            return 0x04;
        return 0x80;
    }

    private bool BuildClientHello(Span<byte> destination, out int length)
    {
        length = 0;
        int bodyLength = 78 + _hostname.Length;
        int messageLength = 4 + bodyLength;
        if (destination.Length < messageLength) return false;
        Span<byte> body = destination[4..];
        body[0] = 3;
        body[1] = 3;
        _clientRandom.CopyTo(body[2..]);
        int offset = 34;
        body[offset++] = 0;
        ManagedTls12RecordProtection.WriteUInt16(2, body[offset..]);
        offset += 2;
        body[offset++] = 0xC0;
        body[offset++] = 0x2B;
        body[offset++] = 1;
        body[offset++] = 0;
        int extensionsLengthOffset = offset;
        offset += 2;

        ManagedTls12RecordProtection.WriteUInt16(0, body[offset..]);
        ManagedTls12RecordProtection.WriteUInt16((ushort)(5 + _hostname.Length),
                                                   body[(offset + 2)..]);
        ManagedTls12RecordProtection.WriteUInt16((ushort)(3 + _hostname.Length),
                                                   body[(offset + 4)..]);
        body[offset + 6] = 0;
        ManagedTls12RecordProtection.WriteUInt16((ushort)_hostname.Length,
                                                   body[(offset + 7)..]);
        _hostname.CopyTo(body[(offset + 9)..]);
        offset += 9 + _hostname.Length;

        ManagedTls12RecordProtection.WriteUInt16(10, body[offset..]);
        ManagedTls12RecordProtection.WriteUInt16(4, body[(offset + 2)..]);
        ManagedTls12RecordProtection.WriteUInt16(2, body[(offset + 4)..]);
        ManagedTls12RecordProtection.WriteUInt16(NamedGroupP256,
                                                   body[(offset + 6)..]);
        offset += 8;

        ManagedTls12RecordProtection.WriteUInt16(11, body[offset..]);
        ManagedTls12RecordProtection.WriteUInt16(2, body[(offset + 2)..]);
        body[offset + 4] = 1;
        body[offset + 5] = 0;
        offset += 6;

        ManagedTls12RecordProtection.WriteUInt16(13, body[offset..]);
        ManagedTls12RecordProtection.WriteUInt16(4, body[(offset + 2)..]);
        ManagedTls12RecordProtection.WriteUInt16(2, body[(offset + 4)..]);
        body[offset + 6] = 4;
        body[offset + 7] = 3;
        offset += 8;

        ManagedTls12RecordProtection.WriteUInt16(ExtendedMasterSecretExtension,
                                                   body[offset..]);
        ManagedTls12RecordProtection.WriteUInt16(0, body[(offset + 2)..]);
        offset += 4;

        ManagedTls12RecordProtection.WriteUInt16(
            (ushort)(offset - extensionsLengthOffset - 2),
            body[extensionsLengthOffset..]);
        ManagedTls12RecordProtection.WriteUInt24(bodyLength, destination[1..]);
        destination[0] = 1;
        length = messageLength;
        return offset == bodyLength;
    }

    private void ProcessAlert()
    {
        ProcessAlert(_recordWire.AsSpan(ManagedTls12RecordProtection.HeaderSize,
                                         _recordBodyLength));
    }

    private void ProcessAlert(ReadOnlySpan<byte> body)
    {
        if (body.Length != 2)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PUBLIC_TLS_ALERT_LENGTH=0x"u8,
                (ulong)body.Length);
            Fail();
            return;
        }
        byte level = body[0];
        byte description = body[1];
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_TLS_ALERT_LEVEL=0x"u8,
                               level);
        KernelLog.WriteHexLine("GXOS_NET10:PUBLIC_TLS_ALERT_DESCRIPTION=0x"u8,
                               description);
        if (level == 2)
        {
            Fail();
            return;
        }
        if (level != 1 || description != 0)
        {
            Fail();
            return;
        }
        if (_stage == InternalStage.Established)
            _stage = InternalStage.Closed;
        else
            Fail();
    }

    private void Fail(ManagedTls12FailureKind failureKind)
    {
        _failureKind = failureKind;
        Fail();
    }

    private void Fail()
    {
        _stage = InternalStage.Failed;
        _clientRandom.AsSpan().Clear();
        _serverRandom.AsSpan().Clear();
        _privateScalar.AsSpan().Clear();
        _clientPublicKey.AsSpan().Clear();
        _serverPublicKey.AsSpan().Clear();
        _premasterSecret.AsSpan().Clear();
        _masterSecret.AsSpan().Clear();
        _keyBlock.AsSpan().Clear();
        _sessionHash.AsSpan().Clear();
        _clientFinishedHash.AsSpan().Clear();
        _serverFinishedHash.AsSpan().Clear();
        _clientWriteKey.AsSpan().Clear();
        _serverWriteKey.AsSpan().Clear();
        _clientWriteIv.AsSpan().Clear();
        _serverWriteIv.AsSpan().Clear();
        _recordPlaintext.AsSpan().Clear();
        _recordWire.AsSpan().Clear();
        _handshake.AsSpan().Clear();
        _workingStorage.AsSpan().Clear();
        _prfHmac.Clear();
        _transcript.Clear();
        _outbound.AsSpan().Clear();
        Array.Clear(_peerCertificates);
        _peerCertificateOffsets.AsSpan().Clear();
        _peerCertificateLengths.AsSpan().Clear();
        _outboundLength = 0;
        _recordHeaderLength = 0;
        _recordBodyLength = 0;
        _expectedRecordBodyLength = -1;
        _handshakeLength = 0;
        _expectedHandshakeLength = 0;
        _certificateStorageLength = 0;
        _peerCertificateCount = 0;
        _peerCertificateAlgorithmMask = 0;
        _trustAnchorMatched = false;
        _recordPlaintext.AsSpan().Clear();
        _applicationPlaintextLength = 0;
        _pendingApplication.AsSpan().Clear();
        _pendingApplicationLength = 0;
    }

    private bool TryQueueApplicationData(ReadOnlySpan<byte> record)
    {
        if (!ManagedTls12RecordProtection.TryDecrypt(
                _serverSequence, _serverWriteKey, _serverWriteIv,
                ManagedTls12RecordProtection.ApplicationData, record,
                _recordPlaintext, out int plaintextLength))
        {
            return false;
        }
        if (plaintextLength > MaximumPendingApplicationBytes -
                            _pendingApplicationLength)
        {
            _recordPlaintext.AsSpan(0, plaintextLength).Clear();
            return false;
        }
        _recordPlaintext.AsSpan(0, plaintextLength).CopyTo(
            _pendingApplication.AsSpan(_pendingApplicationLength));
        _pendingApplicationLength += plaintextLength;
        _recordPlaintext.AsSpan(0, plaintextLength).Clear();
        _serverSequence++;
        return true;
    }

    private static bool IsContentType(byte value)
    {
        return value == ManagedTls12RecordProtection.ChangeCipherSpec ||
               value == ManagedTls12RecordProtection.Alert ||
               value == ManagedTls12RecordProtection.Handshake ||
               value == ManagedTls12RecordProtection.ApplicationData;
    }

}

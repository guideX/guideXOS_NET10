using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedTcpConnectionState : byte
{
    Closed = 0,
    SynSent = 1,
    Established = 2,
    FinWait1 = 3,
    FinWait2 = 4,
    CloseWait = 5,
    LastAck = 6,
    TimeWait = 7,
    Failed = 8
}

internal enum ManagedTcpHandleResult : byte
{
    Ignored = 0,
    Malformed = 1,
    SynAckAccepted = 2,
    Established = 3,
    DataAcknowledged = 4,
    DataReceived = 5,
    DuplicateData = 6,
    OutOfOrder = 7,
    RstReceived = 8,
    FinReceived = 9,
    Closed = 10,
    ReceiveUnavailable = 11,
    Failed = 12
}

internal interface IManagedTcpPacketSender
{
    bool TrySendTcp(Ipv4Address destination, ushort sourcePort,
                    ushort destinationPort, uint sequenceNumber,
                    uint acknowledgmentNumber, ManagedTcpFlags flags,
                    ushort window, ReadOnlySpan<byte> payload,
                    bool advertiseMss);
}

internal interface IManagedTcpApplicationSink
{
    bool TryCaptureReceivedTcp(Ipv4Address source, Ipv4Address destination,
                               ushort sourcePort, ushort destinationPort,
                               ReadOnlySpan<byte> payload);
}

internal enum ManagedTcpPendingKind : byte
{
    None = 0,
    Syn = 1,
    Data = 2,
    Fin = 3
}

internal sealed class ManagedTcpConnection
{
    internal const ushort ClientPort = 15221;
    internal const ushort ServerPort = 15222;
    internal const uint FirstClientIsn = 0x22000001;
    internal const uint ClientIsnGenerationStride = 0x00000100;
    internal const byte MaximumRetries = 3;

    private readonly IManagedTcpPacketSender _sender;
    private readonly byte[] _inFlightPayload =
        new byte[ManagedTcpProtocol.MaximumPayloadLength];
    private ManagedTcpConnectionState _state;
    private ManagedTcpPendingKind _pendingKind;
    private uint _localIpv4;
    private uint _remoteIpv4;
    private ushort _localPort;
    private ushort _remotePort;
    private uint _generation;
    private uint _localIsn;
    private uint _peerIsn;
    private uint _sndUna;
    private uint _sndNxt;
    private uint _rcvNxt;
    private uint _pendingSequence;
    private ushort _inFlightLength;
    private ushort _peerMss;
    private byte _retryCount;

    internal ManagedTcpConnection(IManagedTcpPacketSender sender)
    {
        _sender = sender;
        _state = ManagedTcpConnectionState.Closed;
    }

    internal ManagedTcpConnectionState State => _state;
    internal ushort LocalPort => _localPort;
    internal ushort RemotePort => _remotePort;
    internal uint LocalIpv4 => _localIpv4;
    internal uint RemoteIpv4 => _remoteIpv4;
    internal uint Generation => _generation;
    internal uint LocalIsn => _localIsn;
    internal uint PeerIsn => _peerIsn;
    internal uint SendUnacknowledged => _sndUna;
    internal uint SendNext => _sndNxt;
    internal uint ReceiveNext => _rcvNxt;
    internal bool HasInFlight => _pendingKind != ManagedTcpPendingKind.None;
    internal ManagedTcpPendingKind PendingKind => _pendingKind;
    internal ushort InFlightLength => _inFlightLength;
    internal uint PendingSequence => _pendingSequence;
    internal byte RetryCount => _retryCount;

    internal uint SynSentCount { get; private set; }
    internal uint SynAckAcceptedCount { get; private set; }
    internal uint EstablishedCount { get; private set; }
    internal uint ValidReceiveCount { get; private set; }
    internal uint TupleMismatchCount { get; private set; }
    internal uint SequenceRejectCount { get; private set; }
    internal uint AcknowledgmentRejectCount { get; private set; }
    internal uint DuplicateAcknowledgmentCount { get; private set; }
    internal uint DuplicatePayloadCount { get; private set; }
    internal uint RetransmissionCount { get; private set; }
    internal uint RetryExhaustionCount { get; private set; }
    internal uint RstReceivedCount { get; private set; }
    internal uint FinSentCount { get; private set; }
    internal uint FinReceivedCount { get; private set; }
    internal uint ClosedCount { get; private set; }

    internal bool TryBeginConnect(Ipv4Address localAddress,
                                  Ipv4Address remoteAddress,
                                  ushort remotePort,
                                  uint generation)
    {
        if (_state != ManagedTcpConnectionState.Closed ||
            !localAddress.IsUsable || !remoteAddress.IsUsable ||
            remotePort == 0 || generation == 0)
            return false;

        _localIpv4 = localAddress.Value;
        _remoteIpv4 = remoteAddress.Value;
        _localPort = ClientPort;
        _remotePort = remotePort;
        _generation = generation;
        _localIsn = unchecked(FirstClientIsn +
                              (generation - 1) * ClientIsnGenerationStride);
        _peerIsn = 0;
        _sndUna = _localIsn;
        _sndNxt = _localIsn;
        _rcvNxt = 0;
        _peerMss = ManagedTcpProtocol.MaximumMss;
        _pendingKind = ManagedTcpPendingKind.None;
        _inFlightLength = 0;
        _pendingSequence = 0;
        _retryCount = 0;

        if (!_sender.TrySendTcp(remoteAddress, _localPort, _remotePort,
                                _sndNxt, 0, ManagedTcpFlags.Syn,
                                ManagedTcpProtocol.DefaultWindow, ReadOnlySpan<byte>.Empty,
                                advertiseMss: true))
        {
            ClearTuple();
            return false;
        }

        _pendingKind = ManagedTcpPendingKind.Syn;
        _pendingSequence = _sndNxt;
        _sndNxt = ManagedTcpSequence.Advance(_sndNxt, 1);
        _state = ManagedTcpConnectionState.SynSent;
        SynSentCount++;
        return true;
    }

    internal bool TrySendApplication(ReadOnlySpan<byte> payload)
    {
        if (_state != ManagedTcpConnectionState.Established ||
            _pendingKind != ManagedTcpPendingKind.None || payload.Length == 0 ||
            payload.Length > ManagedTcpProtocol.MaximumPayloadLength ||
            (_peerMss != 0 && payload.Length > _peerMss))
            return false;

        if (!_sender.TrySendTcp(new Ipv4Address(_remoteIpv4), _localPort,
                                _remotePort, _sndNxt, _rcvNxt,
                                ManagedTcpFlags.Ack | ManagedTcpFlags.Psh,
                                ManagedTcpProtocol.DefaultWindow, payload,
                                advertiseMss: false))
            return false;

        payload.CopyTo(_inFlightPayload);
        _pendingKind = ManagedTcpPendingKind.Data;
        _pendingSequence = _sndNxt;
        _inFlightLength = (ushort)payload.Length;
        _sndNxt = ManagedTcpSequence.Advance(_sndNxt, (uint)payload.Length);
        return true;
    }

    internal bool TryClose()
    {
        if (_state != ManagedTcpConnectionState.Established &&
            _state != ManagedTcpConnectionState.CloseWait)
            return false;
        if (_pendingKind != ManagedTcpPendingKind.None)
            return false;

        ManagedTcpConnectionState next = _state == ManagedTcpConnectionState.Established
            ? ManagedTcpConnectionState.FinWait1
            : ManagedTcpConnectionState.LastAck;
        if (!_sender.TrySendTcp(new Ipv4Address(_remoteIpv4), _localPort,
                                _remotePort, _sndNxt, _rcvNxt,
                                ManagedTcpFlags.Ack | ManagedTcpFlags.Fin,
                                ManagedTcpProtocol.DefaultWindow, ReadOnlySpan<byte>.Empty,
                                advertiseMss: false))
            return false;
        _pendingKind = ManagedTcpPendingKind.Fin;
        _pendingSequence = _sndNxt;
        _inFlightLength = 1;
        _sndNxt = ManagedTcpSequence.Advance(_sndNxt, 1);
        _state = next;
        FinSentCount++;
        return true;
    }

    internal ManagedTcpHandleResult TryHandle(
        ManagedTcpSegment segment, IManagedTcpApplicationSink? sink)
    {
        uint source = ManagedEthernetProtocol.ReadUInt32Network(
            segment.SourceAddressForTcp, 0);
        uint destination = ManagedEthernetProtocol.ReadUInt32Network(
            segment.DestinationAddressForTcp, 0);
        if (source != _remoteIpv4 || destination != _localIpv4 ||
            segment.SourcePort != _remotePort || segment.DestinationPort != _localPort)
        {
            TupleMismatchCount++;
            return ManagedTcpHandleResult.Ignored;
        }
        if (_state == ManagedTcpConnectionState.Closed ||
            _state == ManagedTcpConnectionState.TimeWait ||
            _state == ManagedTcpConnectionState.Failed)
            return ManagedTcpHandleResult.Ignored;

        if (segment.Has(ManagedTcpFlags.Rst))
        {
            if (!IsRstAcceptable(segment)) return ManagedTcpHandleResult.Ignored;
            _pendingKind = ManagedTcpPendingKind.None;
            _inFlightLength = 0;
            _retryCount = 0;
            _state = ManagedTcpConnectionState.Failed;
            RstReceivedCount++;
            return ManagedTcpHandleResult.RstReceived;
        }

        if (_state == ManagedTcpConnectionState.SynSent)
            return TryHandleSynSent(segment);
        return TryHandleEstablishedOrClosing(segment, sink);
    }

    internal bool TryRetryPending()
    {
        if (_pendingKind == ManagedTcpPendingKind.None ||
            (_state != ManagedTcpConnectionState.SynSent &&
             _state != ManagedTcpConnectionState.Established &&
             _state != ManagedTcpConnectionState.FinWait1 &&
             _state != ManagedTcpConnectionState.FinWait2 &&
             _state != ManagedTcpConnectionState.LastAck))
            return false;
        if (_retryCount >= MaximumRetries)
        {
            _state = ManagedTcpConnectionState.Failed;
            _pendingKind = ManagedTcpPendingKind.None;
            _inFlightLength = 0;
            RetryExhaustionCount++;
            return false;
        }

        ManagedTcpFlags flags;
        ReadOnlySpan<byte> payload;
        bool advertiseMss;
        switch (_pendingKind)
        {
            case ManagedTcpPendingKind.Syn:
                flags = ManagedTcpFlags.Syn;
                payload = ReadOnlySpan<byte>.Empty;
                advertiseMss = true;
                break;
            case ManagedTcpPendingKind.Data:
                flags = ManagedTcpFlags.Ack | ManagedTcpFlags.Psh;
                payload = _inFlightPayload.AsSpan(0, _inFlightLength);
                advertiseMss = false;
                break;
            case ManagedTcpPendingKind.Fin:
                flags = ManagedTcpFlags.Ack | ManagedTcpFlags.Fin;
                payload = ReadOnlySpan<byte>.Empty;
                advertiseMss = false;
                break;
            default:
                return false;
        }
        if (!_sender.TrySendTcp(new Ipv4Address(_remoteIpv4), _localPort,
                                _remotePort, _pendingSequence,
                                _rcvNxt, flags, ManagedTcpProtocol.DefaultWindow,
                                payload, advertiseMss))
            return false;
        _retryCount++;
        RetransmissionCount++;
        return true;
    }

    internal void ResetForTeardown()
    {
        _state = ManagedTcpConnectionState.Closed;
        _pendingKind = ManagedTcpPendingKind.None;
        _localIpv4 = 0;
        _remoteIpv4 = 0;
        _localPort = 0;
        _remotePort = 0;
        _generation = 0;
        _localIsn = 0;
        _peerIsn = 0;
        _sndUna = 0;
        _sndNxt = 0;
        _rcvNxt = 0;
        _pendingSequence = 0;
        _inFlightLength = 0;
        _peerMss = 0;
        _retryCount = 0;
        _inFlightPayload.AsSpan().Clear();
    }

    private ManagedTcpHandleResult TryHandleSynSent(ManagedTcpSegment segment)
    {
        if (!segment.Has(ManagedTcpFlags.Syn) || !segment.Has(ManagedTcpFlags.Ack) ||
            segment.Has(ManagedTcpFlags.Fin) || segment.PayloadLength != 0 ||
            segment.AcknowledgmentNumber != _sndNxt)
        {
            AcknowledgmentRejectCount++;
            return ManagedTcpHandleResult.Ignored;
        }

        _peerIsn = segment.SequenceNumber;
        _rcvNxt = ManagedTcpSequence.Advance(_peerIsn, 1);
        _sndUna = _sndNxt;
        if (!_sender.TrySendTcp(new Ipv4Address(_remoteIpv4), _localPort,
                                _remotePort, _sndNxt, _rcvNxt,
                                ManagedTcpFlags.Ack, ManagedTcpProtocol.DefaultWindow,
                                ReadOnlySpan<byte>.Empty, advertiseMss: false))
        {
            _state = ManagedTcpConnectionState.Failed;
            return ManagedTcpHandleResult.Failed;
        }
        _pendingKind = ManagedTcpPendingKind.None;
        _inFlightLength = 0;
        _retryCount = 0;
        if (segment.HasMss && segment.Mss != 0)
            _peerMss = segment.Mss < ManagedTcpProtocol.MaximumMss
                ? segment.Mss : ManagedTcpProtocol.MaximumMss;
        _state = ManagedTcpConnectionState.Established;
        SynAckAcceptedCount++;
        EstablishedCount++;
        return ManagedTcpHandleResult.Established;
    }

    private ManagedTcpHandleResult TryHandleEstablishedOrClosing(
        ManagedTcpSegment segment, IManagedTcpApplicationSink? sink)
    {
        bool acknowledged = false;
        if (!segment.Has(ManagedTcpFlags.Ack))
        {
            AcknowledgmentRejectCount++;
            return ManagedTcpHandleResult.Ignored;
        }
        if (!TryAcceptAcknowledgment(segment.AcknowledgmentNumber,
                                     out acknowledged))
            return ManagedTcpHandleResult.Ignored;

        if (segment.Has(ManagedTcpFlags.Syn))
        {
            SequenceRejectCount++;
            return ManagedTcpHandleResult.Ignored;
        }

        int payloadLength = segment.PayloadLength;
        bool hasFin = segment.Has(ManagedTcpFlags.Fin);
        if (payloadLength != 0 || hasFin)
        {
            if (segment.SequenceNumber != _rcvNxt)
            {
                SequenceRejectCount++;
                if (!TrySendAck()) return ManagedTcpHandleResult.Failed;
                if (ManagedTcpSequence.IsBefore(segment.SequenceNumber, _rcvNxt))
                {
                    DuplicatePayloadCount++;
                    return ManagedTcpHandleResult.DuplicateData;
                }
                return ManagedTcpHandleResult.OutOfOrder;
            }
            if (payloadLength != 0)
            {
                if (sink == null || !sink.TryCaptureReceivedTcp(
                        new Ipv4Address(_remoteIpv4), new Ipv4Address(_localIpv4),
                        _remotePort, _localPort, segment.Payload))
                    return ManagedTcpHandleResult.ReceiveUnavailable;
                _rcvNxt = ManagedTcpSequence.Advance(_rcvNxt,
                                                     (uint)payloadLength);
                ValidReceiveCount++;
            }
            if (hasFin)
            {
                _rcvNxt = ManagedTcpSequence.Advance(_rcvNxt, 1);
                FinReceivedCount++;
            }
            if (!TrySendAck()) return ManagedTcpHandleResult.Failed;
            if (hasFin)
            {
                if (_state == ManagedTcpConnectionState.Established)
                    _state = ManagedTcpConnectionState.CloseWait;
                else if (_state == ManagedTcpConnectionState.FinWait1 ||
                         _state == ManagedTcpConnectionState.FinWait2)
                    _state = ManagedTcpConnectionState.TimeWait;
                else if (_state == ManagedTcpConnectionState.LastAck)
                    _state = ManagedTcpConnectionState.TimeWait;
                return ManagedTcpHandleResult.FinReceived;
            }
            return ManagedTcpHandleResult.DataReceived;
        }

        if (acknowledged)
            return ManagedTcpHandleResult.DataAcknowledged;
        if (segment.AcknowledgmentNumber == _sndUna)
        {
            DuplicateAcknowledgmentCount++;
            return ManagedTcpHandleResult.Ignored;
        }
        return ManagedTcpHandleResult.Ignored;
    }

    private bool TryAcceptAcknowledgment(uint acknowledgment,
                                         out bool advanced)
    {
        advanced = false;
        if (ManagedTcpSequence.IsAfter(acknowledgment, _sndNxt) ||
            ManagedTcpSequence.IsBefore(acknowledgment, _sndUna))
        {
            AcknowledgmentRejectCount++;
            return false;
        }
        if (_pendingKind == ManagedTcpPendingKind.Data)
        {
            uint expected = ManagedTcpSequence.Advance(
                _pendingSequence, _inFlightLength);
            if (acknowledgment == expected)
            {
                _sndUna = acknowledgment;
                _pendingKind = ManagedTcpPendingKind.None;
                _inFlightLength = 0;
                _retryCount = 0;
                advanced = true;
                return true;
            }
            if (acknowledgment == _sndUna)
            {
                DuplicateAcknowledgmentCount++;
                return true;
            }
            AcknowledgmentRejectCount++;
            return false;
        }
        if (_pendingKind == ManagedTcpPendingKind.Fin)
        {
            if (acknowledgment == _sndNxt)
            {
                _sndUna = acknowledgment;
                _pendingKind = ManagedTcpPendingKind.None;
                _inFlightLength = 0;
                _retryCount = 0;
                if (_state == ManagedTcpConnectionState.FinWait1)
                    _state = ManagedTcpConnectionState.FinWait2;
                else if (_state == ManagedTcpConnectionState.LastAck)
                    _state = ManagedTcpConnectionState.TimeWait;
                advanced = true;
                return true;
            }
            if (acknowledgment == _sndUna)
            {
                DuplicateAcknowledgmentCount++;
                return true;
            }
            AcknowledgmentRejectCount++;
            return false;
        }
        if (acknowledgment == _sndUna || acknowledgment == _sndNxt)
            return true;
        AcknowledgmentRejectCount++;
        return false;
    }

    private bool IsRstAcceptable(ManagedTcpSegment segment)
    {
        if (_state == ManagedTcpConnectionState.SynSent)
            return segment.Has(ManagedTcpFlags.Ack) &&
                   segment.AcknowledgmentNumber == _sndNxt;
        return segment.SequenceNumber == _rcvNxt &&
               (!segment.Has(ManagedTcpFlags.Ack) ||
                ManagedTcpSequence.IsBeforeOrEqual(segment.AcknowledgmentNumber,
                                                    _sndNxt));
    }

    private bool TrySendAck()
    {
        return _sender.TrySendTcp(new Ipv4Address(_remoteIpv4), _localPort,
                                   _remotePort, _sndNxt, _rcvNxt,
                                   ManagedTcpFlags.Ack,
                                   ManagedTcpProtocol.DefaultWindow,
                                   ReadOnlySpan<byte>.Empty, advertiseMss: false);
    }

    private void ClearTuple()
    {
        _localIpv4 = 0;
        _remoteIpv4 = 0;
        _localPort = 0;
        _remotePort = 0;
        _generation = 0;
        _pendingKind = ManagedTcpPendingKind.None;
        _inFlightLength = 0;
    }
}

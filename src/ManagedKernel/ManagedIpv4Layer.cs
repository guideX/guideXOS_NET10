using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedIpv4HandleResult : byte
{
    Invalid = 0,
    Ignored = 1,
    ArpReplySatisfied = 2,
    ArpResponderReplySent = 3,
    IcmpEchoReplyValidated = 4,
    IcmpResponderReplySent = 5,
    Malformed = 6,
    Failed = 7
}

internal sealed class ManagedIpv4Layer
{
    private const int MaximumProtocolFrames = 16;
    private const int MalformedControlFrames = 5;
    private const ushort FirstIdentifier = 0x1701;
    private const ushort SecondIdentifier = 0x1702;
    private const ushort FirstSequence = 1;
    private const ushort SecondSequence = 2;

    private readonly ManagedEthernetLayer _ethernet;
    private readonly ManagedArpLayer _arp;
    private readonly byte[] _localIpv4 = new byte[4];
    private readonly byte[] _peerIpv4 = new byte[4];
    private readonly byte[] _subnetMask = { 255, 255, 255, 0 };
    private readonly byte[] _destinationMac = new byte[6];
    private readonly byte[] _pendingIpv4 = new byte[4];
    private readonly byte[] _txPacket =
        new byte[ManagedIpv4Protocol.MaximumPacketLength];
    private readonly byte[] _txIcmp = new byte[
        ManagedIcmpv4Protocol.HeaderLength +
        ManagedIcmpv4Protocol.MaximumEchoPayloadLength];
    private readonly byte[] _pingPayload = new byte[32];
    private readonly ManagedIpv4PendingTransmission _pending = new();
    private readonly uint _localIpv4Value;
    private readonly uint _peerIpv4Value;
    private readonly uint _subnetMaskValue;
    private byte _pingPayloadLength;
    private ushort _awaitedIdentifier;
    private ushort _awaitedSequence;
    private bool _active;
    private bool _awaitingReply;
    private bool _replyValidated;
    private bool _responderReplySent;
    private uint _malformedPacketCount;
    private uint _unsupportedProtocolCount;
    private uint _unsupportedOptionsCount;
    private uint _pendingOverflowCount;

    internal ManagedIpv4Layer(ManagedEthernetLayer ethernet,
                              ManagedArpLayer arp)
    {
        _ethernet = ethernet;
        _arp = arp;
        arp.LocalIpv4.AsSpan().CopyTo(_localIpv4);
        arp.HostIpv4.AsSpan().CopyTo(_peerIpv4);
        _localIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_localIpv4, 0);
        _peerIpv4Value = ManagedEthernetProtocol.ReadUInt32Network(_peerIpv4, 0);
        _subnetMaskValue = ManagedEthernetProtocol.ReadUInt32Network(_subnetMask, 0);
        ReadOnlySpan<byte> payload = "guideXOS Phase17 ping payload"u8;
        payload.CopyTo(_pingPayload);
        _pingPayloadLength = (byte)payload.Length;
    }

    internal bool Phase17Passed { get; private set; }
    internal bool PendingTransmissionActive => _pending.IsActive;
    internal uint MalformedPacketCount => _malformedPacketCount;
    internal uint UnsupportedProtocolCount => _unsupportedProtocolCount;
    internal uint UnsupportedOptionsCount => _unsupportedOptionsCount;
    internal uint PendingOverflowCount => _pendingOverflowCount;
    internal bool ResponderReplySent => _responderReplySent;

    internal bool TryRunPhase17()
    {
        if (_active || _arp.Cache.Count == 0) return false;
        _active = true;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_IPV4_READY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMPV4_READY\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_IPV4_LOCAL=0x"u8,
                                    _localIpv4Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_IPV4_PEER=0x"u8,
                                    _peerIpv4Value) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_IPV4_MASK=0x"u8,
                                    _subnetMaskValue) ||
            !TrySendPing(FirstIdentifier, FirstSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_FIRST_PING_SENT\r\n"u8) ||
            !WaitForReply(FirstIdentifier, FirstSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_FIRST_EXCHANGE_PASS\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_MALFORMED_READY\r\n"u8) ||
            !ConsumeMalformedControls() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_MALFORMED_CONTROLS_PASS\r\n"u8) ||
            !WaitForResponderRequest() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMP_RESPONDER_PASS\r\n"u8) ||
            !_ethernet.TryVerifyTransportAfterGc() ||
            _pending.IsActive ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE17_GC_SURVIVAL_PASSED\r\n"u8) ||
            !TrySendPing(SecondIdentifier, SecondSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_POST_GC_PING_SENT\r\n"u8) ||
            !WaitForReply(SecondIdentifier, SecondSequence) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_IPV4_POST_GC_EXCHANGE_PASS\r\n"u8))
            return false;

        Phase17Passed = true;
        return true;
    }

    internal ManagedIpv4HandleResult TryHandle(ReadOnlySpan<byte> packet)
    {
        if (!_active) return ManagedIpv4HandleResult.Failed;
        if (packet.Length >= 1 && (packet[0] & 0x0F) >
            ManagedIpv4Protocol.SupportedHeaderWords)
            _unsupportedOptionsCount++;
        if (!ManagedIpv4Protocol.TryParse(packet, _localIpv4Value,
                                          out ManagedIpv4Packet parsed))
        {
            _malformedPacketCount++;
            return ManagedIpv4HandleResult.Malformed;
        }
        if (parsed.Protocol != ManagedIpv4Protocol.IcmpProtocol)
        {
            _unsupportedProtocolCount++;
            return ManagedIpv4HandleResult.Ignored;
        }
        if (!ManagedIcmpv4Protocol.TryParse(parsed.Payload,
                                            out ManagedIcmpv4Packet icmp))
        {
            _malformedPacketCount++;
            return ManagedIpv4HandleResult.Malformed;
        }
        if (icmp.Type == ManagedIcmpv4Protocol.EchoReply)
            return TryHandleEchoReply(parsed, icmp);
        return TryHandleEchoRequest(parsed, icmp);
    }

    internal bool TryReleasePendingAfterArp()
    {
        if (!_pending.IsActive) return true;
        if (!_arp.Cache.TryLookup(_pendingIpv4, _destinationMac))
            return false;
        if (!_pending.TryTake(_pendingIpv4, _txPacket, out ushort length))
            return false;
        return _ethernet.TryTransmit(ManagedIpv4Protocol.EtherType,
                                     _destinationMac, _txPacket, length);
    }

    internal bool TryStop()
    {
        _active = false;
        _awaitingReply = false;
        _replyValidated = false;
        _responderReplySent = false;
        _awaitedIdentifier = 0;
        _awaitedSequence = 0;
        _malformedPacketCount = 0;
        _unsupportedProtocolCount = 0;
        _unsupportedOptionsCount = 0;
        _pendingOverflowCount = 0;
        _pending.Clear();
        _pendingIpv4.AsSpan().Clear();
        return true;
    }

    private bool TrySendPing(ushort identifier, ushort sequence)
    {
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, _peerIpv4Value) ||
            !ManagedIcmpv4Protocol.TryBuildEchoRequest(
                _txIcmp, identifier, sequence,
                _pingPayload.AsSpan(0, _pingPayloadLength),
                out ushort icmpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, (ushort)(0x1700 + sequence), 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedIpv4Protocol.IcmpProtocol,
                _localIpv4, _peerIpv4, _txIcmp.AsSpan(0, icmpLength),
                out ushort packetLength) ||
            !TrySendPacket(_peerIpv4, _txPacket.AsSpan(0, packetLength)))
            return false;
        _awaitedIdentifier = identifier;
        _awaitedSequence = sequence;
        _awaitingReply = true;
        _replyValidated = false;
        return true;
    }

    private bool TrySendPacket(ReadOnlySpan<byte> destinationIpv4,
                               ReadOnlySpan<byte> packet)
    {
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue,
                ManagedEthernetProtocol.ReadUInt32Network(destinationIpv4, 0)))
            return false;
        if (_arp.Cache.TryLookup(destinationIpv4, _destinationMac))
            return _ethernet.TryTransmit(ManagedIpv4Protocol.EtherType,
                                         _destinationMac, _txPacket,
                                         packet.Length);
        if (!_pending.TryStage(destinationIpv4, packet))
        {
            _pendingOverflowCount++;
            KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_PENDING_OVERFLOW\r\n"u8);
            return false;
        }
        destinationIpv4.CopyTo(_pendingIpv4);
        if (!_arp.TryResolve(destinationIpv4) || _pending.IsActive)
        {
            _pending.Clear();
            return false;
        }
        return true;
    }

    private bool WaitForReply(ushort identifier, ushort sequence)
    {
        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result)) return false;
            if (result == ManagedNetworkDispatchResult.IcmpEchoReplyValidated &&
                !_awaitingReply && _replyValidated &&
                _awaitedIdentifier == identifier && _awaitedSequence == sequence)
                return KernelLog.Write(
                    sequence == FirstSequence
                        ? "GXOS_NET10:MANAGED_ICMP_FIRST_REPLY_VALID\r\n"u8
                        : "GXOS_NET10:MANAGED_ICMP_POST_GC_REPLY_VALID\r\n"u8);
        }
        return false;
    }

    private bool ConsumeMalformedControls()
    {
        for (int frame = 0; frame != MalformedControlFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result) ||
                result != ManagedNetworkDispatchResult.Malformed)
                return false;
            if (!WriteMalformedControlMarker(frame)) return false;
        }
        return true;
    }

    private static bool WriteMalformedControlMarker(int frame)
    {
        return frame switch
        {
            0 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_0\r\n"u8),
            1 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_1\r\n"u8),
            2 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_2\r\n"u8),
            3 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_3\r\n"u8),
            4 => KernelLog.Write(
                "GXOS_NET10:MANAGED_IPV4_MALFORMED_FRAME_4\r\n"u8),
            _ => false
        };
    }

    private bool WaitForResponderRequest()
    {
        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    out ManagedNetworkDispatchResult result)) return false;
            if (result == ManagedNetworkDispatchResult.IcmpResponderReplySent)
                return true;
        }
        return false;
    }

    private ManagedIpv4HandleResult TryHandleEchoReply(
        ManagedIpv4Packet packet, ManagedIcmpv4Packet icmp)
    {
        if (!_awaitingReply ||
            ManagedEthernetProtocol.ReadUInt32Network(packet.SourceAddress, 0) !=
                _peerIpv4Value ||
            ManagedEthernetProtocol.ReadUInt32Network(packet.DestinationAddress, 0) !=
                _localIpv4Value || icmp.Identifier != _awaitedIdentifier ||
            icmp.Sequence != _awaitedSequence ||
            !icmp.Payload.SequenceEqual(_pingPayload.AsSpan(0, _pingPayloadLength)))
            return ManagedIpv4HandleResult.Ignored;
        _awaitingReply = false;
        _replyValidated = true;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_IPV4_RX_ECHO_REPLY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMP_ECHO_REPLY_VALID\r\n"u8))
            return ManagedIpv4HandleResult.Failed;
        return ManagedIpv4HandleResult.IcmpEchoReplyValidated;
    }

    private ManagedIpv4HandleResult TryHandleEchoRequest(
        ManagedIpv4Packet packet, ManagedIcmpv4Packet icmp)
    {
        uint sourceIpv4 = ManagedEthernetProtocol.ReadUInt32Network(
            packet.SourceAddress, 0);
        if (!ManagedIpv4Protocol.IsDirectlyReachable(
                _localIpv4Value, _subnetMaskValue, sourceIpv4))
            return ManagedIpv4HandleResult.Ignored;
        if (!ManagedIcmpv4Protocol.TryBuildEchoReply(
                _txIcmp, icmp.Identifier, icmp.Sequence, icmp.Payload,
                out ushort icmpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                _txPacket, (ushort)(0x1800 + icmp.Sequence), 0,
                ManagedIpv4Protocol.DefaultTtl, ManagedIpv4Protocol.IcmpProtocol,
                _localIpv4, packet.SourceAddress,
                _txIcmp.AsSpan(0, icmpLength), out ushort packetLength) ||
            !TrySendPacket(packet.SourceAddress,
                            _txPacket.AsSpan(0, packetLength)))
            return ManagedIpv4HandleResult.Failed;
        _responderReplySent = true;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_IPV4_RX_ECHO_REQUEST\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ICMP_ECHO_REPLY_SENT\r\n"u8))
            return ManagedIpv4HandleResult.Failed;
        return ManagedIpv4HandleResult.IcmpResponderReplySent;
    }
}

using System;

namespace GuideXOS.Net10.ManagedKernel;

internal sealed class ManagedArpLayer
{
    private const int MaximumProtocolFrames = 16;
    private const uint HostIpv4Value = 0x0A0F0002;
    private readonly ManagedEthernetLayer _ethernet;
    private readonly ManagedArpCache _cache = new();
    private ulong _localMacValue;
    private readonly byte[] _localMac = new byte[ManagedEthernetProtocol.MacLength];
    private readonly byte[] _localIpv4 = new byte[ManagedEthernetProtocol.ProtocolAddressLength];
    private readonly byte[] _hostIpv4 = new byte[ManagedEthernetProtocol.ProtocolAddressLength];
    private readonly byte[] _cachedMac = new byte[ManagedEthernetProtocol.MacLength];
    private readonly byte[] _requestPayload = new byte[ManagedArpProtocol.PayloadLength];
    private readonly byte[] _replyPayload = new byte[ManagedArpProtocol.PayloadLength];
    private readonly byte[] _broadcastMac = new byte[ManagedEthernetProtocol.MacLength];
    private readonly byte[] _targetMac = new byte[ManagedEthernetProtocol.MacLength];
    private readonly byte[] _targetIpv4 = new byte[ManagedEthernetProtocol.ProtocolAddressLength];
    private bool _active;
    private bool _pending;
    private uint _pendingIpv4;
    private bool _responderReplySent;

    internal ManagedArpLayer(ManagedEthernetLayer ethernet)
    {
        _ethernet = ethernet;
        _broadcastMac.AsSpan().Fill(0xFF);
        _localIpv4[0] = 10;
        _localIpv4[1] = 15;
        _localIpv4[2] = 0;
        _localIpv4[3] = 1;
        _hostIpv4[0] = 10;
        _hostIpv4[1] = 15;
        _hostIpv4[2] = 0;
        _hostIpv4[3] = 2;
    }

    internal void InitializeMac()
    {
        uint macHigh = ManagedE1000Driver.Phase16MacHigh;
        uint macLow = ManagedE1000Driver.Phase16MacLow;
        _localMacValue = ((ulong)macHigh << 32) | macLow;
        _localMac[0] = (byte)(macHigh >> 8);
        _localMac[1] = (byte)macHigh;
        _localMac[2] = (byte)(macLow >> 24);
        _localMac[3] = (byte)(macLow >> 16);
        _localMac[4] = (byte)(macLow >> 8);
        _localMac[5] = (byte)macLow;
    }

    internal ManagedArpCache Cache => _cache;
    internal byte[] LocalIpv4 => _localIpv4;
    internal byte[] HostIpv4 => _hostIpv4;
    internal bool Phase16Passed { get; private set; }

    internal bool TryRunPhase16()
    {
        if (_active || _cache.Count != 0) return false;
        _active = true;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_ETHERNET_READY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ARP_READY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ARP_CACHE_EMPTY\r\n"u8) ||
            !TryResolveHost() || !TryRunGcSurvival() ||
            !TryWaitForLocalRequest()) return false;

        if (!_responderReplySent ||
            !KernelLog.Write("GXOS_NET10:MANAGED_ARP_RESPONDER_PASS\r\n"u8))
            return false;
        Phase16Passed = true;
        return true;
    }

    internal ManagedArpHandleResult TryHandleEthernetArp(
        byte[] frame, int destinationOffset, int sourceOffset,
        int payloadOffset, int payloadLength)
    {
        if (frame == null || destinationOffset < 0 || sourceOffset < 0 ||
            payloadOffset < 0 || destinationOffset > frame.Length -
            ManagedEthernetProtocol.MacLength || sourceOffset > frame.Length -
            ManagedEthernetProtocol.MacLength || payloadLength < 0 ||
            payloadOffset > frame.Length - payloadLength)
            return ManagedArpHandleResult.Invalid;

        ReadOnlySpan<byte> ethernetDestination = frame.AsSpan(
            destinationOffset, ManagedEthernetProtocol.MacLength);
        ReadOnlySpan<byte> ethernetSource = frame.AsSpan(
            sourceOffset, ManagedEthernetProtocol.MacLength);
        ReadOnlySpan<byte> payload = frame.AsSpan(payloadOffset, payloadLength);
        if (!_active || !ManagedArpProtocol.TryParse(payload,
                                                     out ManagedArpPacket packet) ||
            !ethernetSource.SequenceEqual(packet.SenderMac) ||
            !ManagedEthernetProtocol.IsUsableSourceMac(ethernetSource))
            return ManagedArpHandleResult.Invalid;

        if (!KernelLog.Write("GXOS_NET10:MANAGED_ETHERNET_RX_ARP\r\n"u8))
            return ManagedArpHandleResult.Failed;

        byte[] localIpv4 = _localIpv4;
        byte[] localMac = _localMac;

        if (packet.Operation == ManagedArpProtocol.OperationReply)
        {
            if (!_pending || !ManagedArpProtocol.IsPendingReplyMatch(
                    packet, ethernetSource, ethernetDestination,
                    localMac, localIpv4, _pendingIpv4) ||
                ReadIpv4(packet.SenderIpv4) != HostIpv4Value)
                return ManagedArpHandleResult.Ignored;

            if (!KernelLog.Write("GXOS_NET10:MANAGED_ARP_REPLY_VALID\r\n"u8))
                return ManagedArpHandleResult.Failed;
            if (!_cache.TryLearn(packet.SenderIpv4, packet.SenderMac,
                                 out bool updatedExisting))
                return ManagedArpHandleResult.Failed;
            if (!updatedExisting &&
                !KernelLog.Write("GXOS_NET10:MANAGED_ARP_CACHE_LEARNED\r\n"u8))
                return ManagedArpHandleResult.Failed;
            _pending = false;
            return ManagedArpHandleResult.ReplySatisfied;
        }

        if (!ManagedArpProtocol.IsRequestForLocal(
                packet, ethernetSource, ethernetDestination, localIpv4) ||
            packet.SenderIpv4.SequenceEqual(localIpv4))
            return ManagedArpHandleResult.Ignored;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_ARP_REQUEST_FOR_LOCAL\r\n"u8))
            return ManagedArpHandleResult.Failed;

        packet.SenderMac.CopyTo(_targetMac);
        packet.SenderIpv4.CopyTo(_targetIpv4);
        if (!TryBuildRuntimeReply())
            return ManagedArpHandleResult.Failed;
        if (!_ethernet.TryTransmit(ManagedEthernetProtocol.ArpEtherType,
                                   _targetMac, _replyPayload,
                                   ManagedArpProtocol.PayloadLength))
            return ManagedArpHandleResult.Failed;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_ARP_REPLY_SENT\r\n"u8))
            return ManagedArpHandleResult.Failed;
        _responderReplySent = true;
        return ManagedArpHandleResult.ResponderReplySent;
    }

    internal bool TryStop()
    {
        _active = false;
        _pending = false;
        _responderReplySent = false;
        _cache.Clear();
        return true;
    }

    private bool TryResolveHost()
    {
        byte[] hostIpv4 = _hostIpv4;
        if (_cache.TryLookup(hostIpv4, _cachedMac)) return false;
        return TryResolve(hostIpv4);
    }

    internal bool TryResolve(ReadOnlySpan<byte> targetIpv4)
    {
        if (!ManagedArpProtocol.IsUsableIpv4(targetIpv4) || _pending)
            return false;
        if (_cache.TryLookup(targetIpv4, _cachedMac)) return true;
        _pending = true;
        _pendingIpv4 = ReadIpv4(targetIpv4);
        if (!KernelLog.Write("GXOS_NET10:MANAGED_ARP_RESOLUTION_STARTED\r\n"u8))
            return false;

        byte[] broadcast = _broadcastMac;
        if (!TryBuildRuntimeRequest(targetIpv4)) return false;
        if (!_ethernet.TryTransmit(ManagedEthernetProtocol.ArpEtherType,
                                   broadcast, _requestPayload,
                                   ManagedArpProtocol.PayloadLength))
        {
            return false;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_ETHERNET_TX_ARP_REQUEST\r\n"u8))
            return false;

        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    this, out ManagedArpHandleResult result)) return false;
            if (result != ManagedArpHandleResult.ReplySatisfied) continue;
            return !_pending && _cache.TryLookup(targetIpv4, _cachedMac) &&
                   ManagedEthernetProtocol.IsUsableSourceMac(_cachedMac) &&
                   ReadIpv4(targetIpv4) == _pendingIpv4 &&
                   KernelLog.Write(
                       "GXOS_NET10:MANAGED_ARP_RESOLUTION_COMPLETE\r\n"u8);
        }
        return false;
    }

    private bool TryWaitForLocalRequest()
    {
        for (int frame = 0; frame != MaximumProtocolFrames; ++frame)
        {
            if (!_ethernet.TryReceiveAndDispatch(
                    this, out ManagedArpHandleResult result)) return false;
            if (result == ManagedArpHandleResult.ResponderReplySent)
                return true;
        }
        return false;
    }

    private bool TryRunGcSurvival()
    {
        Span<byte> hostIpv4 = stackalloc byte[ManagedEthernetProtocol.ProtocolAddressLength];
        WriteHostIpv4(hostIpv4);
        Span<byte> learnedMac = stackalloc byte[ManagedEthernetProtocol.MacLength];
        if (!_cache.TryLookup(hostIpv4, learnedMac) || !IsHostMac(learnedMac))
            return false;
        if (!_ethernet.TryVerifyTransportAfterGc() ||
            !_cache.TryLookup(hostIpv4, learnedMac) || !IsHostMac(learnedMac))
            return false;
        return KernelLog.Write(
            "GXOS_NET10:MANAGED_KERNEL_PHASE16_GC_SURVIVAL_PASSED\r\n"u8);
    }

    private static bool IsHostMac(ReadOnlySpan<byte> mac)
    {
        return mac.Length == ManagedEthernetProtocol.MacLength &&
               mac[0] == 2 && mac[1] == 0x15 && mac[2] == 0 && mac[3] == 0 &&
               mac[4] == 0 && mac[5] == 2;
    }

    private static uint ReadIpv4(ReadOnlySpan<byte> address)
    {
        return ManagedEthernetProtocol.ReadUInt32Network(address, 0);
    }

    private static uint ReadIpv4(byte[] address)
    {
        return ((uint)address[0] << 24) | ((uint)address[1] << 16) |
               ((uint)address[2] << 8) | address[3];
    }

    private static void WriteHostIpv4(Span<byte> address)
    {
        ManagedEthernetProtocol.WriteUInt32Network(address, 0, HostIpv4Value);
    }

    private bool TryBuildRuntimeRequest(ReadOnlySpan<byte> targetIpv4)
    {
        bool macValid = IsUsableMac(_localMacValue);
        bool localIpValid = IsUsableIpv4(_localIpv4);
        if (!macValid || !localIpValid ||
            !ManagedArpProtocol.IsUsableIpv4(targetIpv4)) return false;
        for (int clearIndex = 0; clearIndex != ManagedArpProtocol.PayloadLength; ++clearIndex)
            _requestPayload[clearIndex] = 0;
        _requestPayload[0] = 0;
        _requestPayload[1] = (byte)ManagedArpProtocol.HardwareTypeEthernet;
        _requestPayload[2] = 0x08;
        _requestPayload[3] = 0;
        _requestPayload[4] = ManagedArpProtocol.HardwareAddressLength;
        _requestPayload[5] = ManagedArpProtocol.ProtocolAddressLength;
        _requestPayload[6] = 0;
        _requestPayload[7] = (byte)ManagedArpProtocol.OperationRequest;
        _requestPayload[8] = (byte)(_localMacValue >> 40);
        _requestPayload[9] = (byte)(_localMacValue >> 32);
        _requestPayload[10] = (byte)(_localMacValue >> 24);
        _requestPayload[11] = (byte)(_localMacValue >> 16);
        _requestPayload[12] = (byte)(_localMacValue >> 8);
        _requestPayload[13] = (byte)_localMacValue;
        _requestPayload[14] = 10;
        _requestPayload[15] = 15;
        _requestPayload[16] = 0;
        _requestPayload[17] = 1;
        targetIpv4.CopyTo(_requestPayload.AsSpan(24, 4));
        return _requestPayload[18] == 0 && _requestPayload[19] == 0 &&
               _requestPayload[20] == 0 && _requestPayload[21] == 0 &&
               _requestPayload[22] == 0 && _requestPayload[23] == 0;
    }

    private bool TryBuildRuntimeReply()
    {
        if (!IsUsableMac(_localMacValue) || !IsUsableIpv4(_localIpv4) ||
            !IsUsableMac(_targetMac) || !IsUsableIpv4(_targetIpv4)) return false;
        for (int clearIndex = 0; clearIndex != _replyPayload.Length; ++clearIndex)
            _replyPayload[clearIndex] = 0;
        _replyPayload[0] = 0;
        _replyPayload[1] = (byte)ManagedArpProtocol.HardwareTypeEthernet;
        _replyPayload[2] = 0x08;
        _replyPayload[3] = 0;
        _replyPayload[4] = ManagedArpProtocol.HardwareAddressLength;
        _replyPayload[5] = ManagedArpProtocol.ProtocolAddressLength;
        _replyPayload[6] = 0;
        _replyPayload[7] = (byte)ManagedArpProtocol.OperationReply;
        _replyPayload[8] = (byte)(_localMacValue >> 40);
        _replyPayload[9] = (byte)(_localMacValue >> 32);
        _replyPayload[10] = (byte)(_localMacValue >> 24);
        _replyPayload[11] = (byte)(_localMacValue >> 16);
        _replyPayload[12] = (byte)(_localMacValue >> 8);
        _replyPayload[13] = (byte)_localMacValue;
        _replyPayload[14] = 10;
        _replyPayload[15] = 15;
        _replyPayload[16] = 0;
        _replyPayload[17] = 1;
        for (int macIndex = 0; macIndex != ManagedEthernetProtocol.MacLength;
             ++macIndex)
            _replyPayload[18 + macIndex] = _targetMac[macIndex];
        for (int ipIndex = 0; ipIndex != ManagedEthernetProtocol.ProtocolAddressLength;
             ++ipIndex)
            _replyPayload[24 + ipIndex] = _targetIpv4[ipIndex];
        return true;
    }

    private static bool IsUsableMac(byte[] mac)
    {
        if (mac == null || mac.Length != ManagedEthernetProtocol.MacLength)
            return false;
        bool allZero = true;
        bool allOnes = true;
        for (int index = 0; index != mac.Length; ++index)
        {
            allZero &= mac[index] == 0;
            allOnes &= mac[index] == 0xFF;
        }
        return !allZero && !allOnes && (mac[0] & 1) == 0;
    }

    private static bool IsUsableMac(ulong mac)
    {
        byte first = (byte)(mac >> 40);
        return mac != 0 && mac != 0xFFFFFFFFFFFFUL && (first & 1) == 0;
    }

    private static bool IsUsableIpv4(byte[] address)
    {
        if (address == null || address.Length !=
            ManagedEthernetProtocol.ProtocolAddressLength) return false;
        bool allZero = true;
        bool allOnes = true;
        for (int index = 0; index != address.Length; ++index)
        {
            allZero &= address[index] == 0;
            allOnes &= address[index] == 0xFF;
        }
        return !allZero && !allOnes;
    }

}

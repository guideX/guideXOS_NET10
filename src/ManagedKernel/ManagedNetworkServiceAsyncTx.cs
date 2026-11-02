using System;

namespace GuideXOS.Net10.ManagedKernel;

internal interface IManagedE1000AsyncTxTransport
{
    uint DriverEpisode { get; }
    bool IsAsyncTxReady { get; }
    ManagedE1000AsyncTxSubmitResult TrySubmitFrame(
        byte[]? frame, int length, out ulong requestId);
    ManagedE1000AsyncTxResult TryGetTransmitCompletion(ulong requestId);
    ManagedE1000AsyncTxResult TryConsumeTransmitCompletion(ulong requestId);
}

/* The service keeps no frame queue. Its four records mirror the E1000's four
   async requests and retain only episode-scoped request identities. The E1000
   copies frame bytes to its DMA allocation before TrySubmitFrame returns. */
internal sealed class ManagedNetworkServiceAsyncTxAdapter
{
    internal const int Capacity = (int)ManagedE1000AsyncTxLedger.Capacity;

    private struct PendingRequest
    {
        internal ulong RequestId;
        internal uint Episode;
        internal bool Active;
    }

    private readonly IManagedE1000AsyncTxTransport _driver;
    private readonly PendingRequest[] _pending = new PendingRequest[Capacity];
    private readonly uint _episode;
    private bool _admitting = true;
    private uint _accepted;
    private uint _busy;
    private uint _invalid;
    private uint _unavailable;
    private uint _completed;
    private uint _failed;
    private uint _consumed;
    private uint _invalidated;
    private uint _reclamationFailures;
    private ulong _lastAcceptedRequestId;

    internal ManagedNetworkServiceAsyncTxAdapter(
        IManagedE1000AsyncTxTransport driver)
    {
        _driver = driver;
        _episode = driver.DriverEpisode;
    }

    internal int Outstanding => CountPending();
    internal ulong LastAcceptedRequestId => _lastAcceptedRequestId;
    internal ManagedServiceAsyncTxStatistics Statistics =>
        new(_accepted, _busy, _invalid, _unavailable, _completed, _failed,
            _consumed, _invalidated, _reclamationFailures, CountPending());

    internal ManagedServiceAsyncTxSubmitResult TrySubmitFrame(
        byte[]? frame, int length, out ulong requestId)
    {
        requestId = 0;
        if (frame == null || length < ManagedEthernetProtocol.MinimumFrameLength ||
            length > frame.Length || length > ManagedE1000Protocol.PacketBufferSize)
        {
            _invalid++;
            return ManagedServiceAsyncTxSubmitResult.Invalid;
        }
        if (!_admitting || _driver.DriverEpisode != _episode ||
            !_driver.IsAsyncTxReady)
        {
            _unavailable++;
            return ManagedServiceAsyncTxSubmitResult.Unavailable;
        }

        int freeSlot = FindFreeSlot();
        if (freeSlot < 0)
        {
            _busy++;
            return ManagedServiceAsyncTxSubmitResult.Busy;
        }

        ManagedE1000AsyncTxSubmitResult result =
            _driver.TrySubmitFrame(frame, length, out ulong acceptedId);
        if (result == ManagedE1000AsyncTxSubmitResult.Busy)
        {
            _busy++;
            return ManagedServiceAsyncTxSubmitResult.Busy;
        }
        if (result == ManagedE1000AsyncTxSubmitResult.InvalidArgument)
        {
            _invalid++;
            return ManagedServiceAsyncTxSubmitResult.Invalid;
        }
        if (result != ManagedE1000AsyncTxSubmitResult.Accepted || acceptedId == 0)
        {
            _unavailable++;
            return ManagedServiceAsyncTxSubmitResult.Unavailable;
        }

        _pending[freeSlot].RequestId = acceptedId;
        _pending[freeSlot].Episode = _episode;
        _pending[freeSlot].Active = true;
        _accepted++;
        _lastAcceptedRequestId = acceptedId;
        requestId = acceptedId;
        return ManagedServiceAsyncTxSubmitResult.Accepted;
    }

    internal ManagedE1000AsyncTxResult TryGetCompletion(ulong requestId)
    {
        int slot = FindSlot(requestId);
        if (slot < 0 || _pending[slot].Episode != _episode ||
            _driver.DriverEpisode != _episode)
            return ManagedE1000AsyncTxResult.InvalidOrStale;
        return _driver.TryGetTransmitCompletion(requestId);
    }

    internal ManagedE1000AsyncTxResult TryConsumeCompletion(ulong requestId)
    {
        int slot = FindSlot(requestId);
        if (slot < 0 || _pending[slot].Episode != _episode ||
            _driver.DriverEpisode != _episode)
            return ManagedE1000AsyncTxResult.InvalidOrStale;

        ManagedE1000AsyncTxResult result =
            _driver.TryConsumeTransmitCompletion(requestId);
        if (result == ManagedE1000AsyncTxResult.Success ||
            result == ManagedE1000AsyncTxResult.HardwareError)
        {
            if (result == ManagedE1000AsyncTxResult.Success) _completed++;
            else _failed++;
            _consumed++;
            ClearSlot(slot);
        }
        return result;
    }

    /* Called at the beginning of each service Poll, before the RX attempt.
       The request array caps the work at four queries and four consumes. */
    internal int PumpCompletions(int maximumRequests = Capacity)
    {
        if (maximumRequests <= 0 || !_admitting ||
            _driver.DriverEpisode != _episode || !_driver.IsAsyncTxReady)
            return 0;
        int budget = Math.Min(maximumRequests, Capacity);
        int visited = 0;
        int reclaimed = 0;
        for (int slot = 0; slot < Capacity && visited < budget; ++slot)
        {
            if (!_pending[slot].Active) continue;
            visited++;
            ulong requestId = _pending[slot].RequestId;
            ManagedE1000AsyncTxResult state = TryGetCompletion(requestId);
            if (state != ManagedE1000AsyncTxResult.Success &&
                state != ManagedE1000AsyncTxResult.HardwareError)
                continue;
            ManagedE1000AsyncTxResult consumed = TryConsumeCompletion(requestId);
            if (consumed == ManagedE1000AsyncTxResult.Success ||
                consumed == ManagedE1000AsyncTxResult.HardwareError)
                reclaimed++;
            else
                _reclamationFailures++;
        }
        return reclaimed;
    }

    internal void CloseAdmission() => _admitting = false;

    /* The caller invokes this only after the E1000 has quiesced DMA and its
       ledger has invalidated the retiring episode. */
    internal bool OnDmaQuiesced(uint episode)
    {
        if (episode != _episode || _driver.DriverEpisode != _episode ||
            _admitting)
            return false;
        for (int slot = 0; slot < Capacity; ++slot)
        {
            if (!_pending[slot].Active) continue;
            _invalidated++;
            ClearSlot(slot);
        }
        return true;
    }

    private int FindFreeSlot()
    {
        for (int slot = 0; slot < Capacity; ++slot)
            if (!_pending[slot].Active) return slot;
        return -1;
    }

    private int FindSlot(ulong requestId)
    {
        if (requestId == 0) return -1;
        for (int slot = 0; slot < Capacity; ++slot)
            if (_pending[slot].Active &&
                _pending[slot].RequestId == requestId)
                return slot;
        return -1;
    }

    private int CountPending()
    {
        int count = 0;
        for (int slot = 0; slot < Capacity; ++slot)
            if (_pending[slot].Active) count++;
        return count;
    }

    private void ClearSlot(int slot)
    {
        _pending[slot].RequestId = 0;
        _pending[slot].Episode = 0;
        _pending[slot].Active = false;
    }
}

/* Shared production packet builder for the opt-in UDP echo consumer and its
   hosted packet-validation tests. It never accepts broadcast or invalid peer
   MAC/IP addresses. */
internal static class ManagedUdpEchoReplyFrameBuilder
{
    internal static bool TryBuild(
        Span<byte> ethernetFrame, Span<byte> ipv4Packet,
        Span<byte> udpDatagram, ReadOnlySpan<byte> localMac,
        ReadOnlySpan<byte> peerMac, Ipv4Address localIpv4,
        Ipv4Address peerIpv4, ushort localPort, ushort peerPort,
        ReadOnlySpan<byte> payload, ushort ipIdentifier,
        out ushort frameLength)
    {
        frameLength = 0;
        if (!localIpv4.IsUsable || !peerIpv4.IsUsable || localPort == 0 ||
            peerPort == 0 || payload.Length > ManagedNetworkService.MaximumUdpPayloadLength ||
            !ManagedEthernetProtocol.IsUsableSourceMac(localMac) ||
            !ManagedEthernetProtocol.IsUsableSourceMac(peerMac))
            return false;

        Span<byte> localIpBytes = stackalloc byte[4];
        Span<byte> peerIpBytes = stackalloc byte[4];
        localIpv4.CopyTo(localIpBytes);
        peerIpv4.CopyTo(peerIpBytes);
        if (!ManagedUdpProtocol.TryBuild(
                udpDatagram, localPort, peerPort, localIpBytes, peerIpBytes,
                payload, out ushort udpLength) ||
            !ManagedIpv4Protocol.TryBuild(
                ipv4Packet, ipIdentifier, 0, ManagedIpv4Protocol.DefaultTtl,
                ManagedUdpProtocol.Protocol, localIpBytes, peerIpBytes,
                udpDatagram.Slice(0, udpLength), out ushort ipLength) ||
            !ManagedEthernetProtocol.TryBuildFrame(
                ethernetFrame, peerMac, localMac, ManagedIpv4Protocol.EtherType,
                ipv4Packet.Slice(0, ipLength), out frameLength))
            return false;
        return true;
    }
}

/* Opt-in Phase 93 application consumer. It uses the normal bound UDP receive
   slot and submits each response through the service async-TX operation. */
internal sealed class ManagedPhase93DiagnosticUdpEchoConsumer
{
    internal const ushort LocalPort = 15930;
    internal const int InitialReplyCount = 32;
    private const int PollBudgetPerRequest = 250000;
    private const int DrainPollBudget = 100000;
    private readonly ManagedNetworkService _service;
    private ulong _lastAcceptedRequestId;

    internal ManagedPhase93DiagnosticUdpEchoConsumer(
        ManagedNetworkService service)
    {
        _service = service;
    }

    internal ulong LastAcceptedRequestId => _lastAcceptedRequestId;

    internal bool TryRunInitial()
    {
        NetworkStatus status = _service.GetStatus();
        if (!status.Configured || !status.Ipv4Address.IsUsable ||
            _service.BoundEndpointCount != 1 ||
            !KernelLog.Write("GXOS_NET10:PHASE93_SERVICE_UDP_ECHO_READY=1\r\n"u8) ||
            !TryAcceptEchoes(InitialReplyCount, out ulong acceptedId) ||
            !TryDrainCompletions(InitialReplyCount))
            return false;

        _lastAcceptedRequestId = acceptedId;
        ManagedServiceAsyncTxStatistics drained = _service.AsyncTxStatistics;
        if (drained.Accepted != InitialReplyCount ||
            drained.Completed != InitialReplyCount ||
            drained.Failed != 0 || drained.Consumed != InitialReplyCount ||
            drained.Outstanding != 0 ||
            _service.UdpEchoAcceptedCount != InitialReplyCount ||
            _service.UdpEchoBusyDropCount != 0 ||
            !WriteSummary("INITIAL"u8, drained) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE93_INITIAL_SERVICE_DRAIN_PASS=1\r\n"u8))
            return false;

        _lastAcceptedRequestId = acceptedId;
        return acceptedId != 0;
    }

    internal bool TryAcceptPendingRequestForStop()
    {
        if (!KernelLog.Write(
                "GXOS_NET10:PHASE93_DRIVER_STOP_PENDING_REQUEST_READY=1\r\n"u8))
            return false;
        for (int poll = 0; poll < PollBudgetPerRequest; ++poll)
        {
            NetworkOperationResult result = _service.Poll();
            if (result == NetworkOperationResult.Unavailable ||
                result == NetworkOperationResult.Failed)
                return false;
            if (!_service.HasReceivedUdp) continue;
            ManagedServiceAsyncTxSubmitResult submitted =
                _service.TrySendReceivedUdpEchoAsync(out _lastAcceptedRequestId);
            if (submitted != ManagedServiceAsyncTxSubmitResult.Accepted ||
                _lastAcceptedRequestId == 0)
                return false;
            return KernelLog.Write(
                "GXOS_NET10:PHASE93_DRIVER_STOP_PENDING_ACCEPTED=1\r\n"u8);
        }
        return false;
    }

    internal bool TryRunAfterRestart(ulong staleRequestId,
                                     uint invalidatedPending)
    {
        if (staleRequestId == 0 || invalidatedPending != 1 ||
            _service.Backend is not IManagedNetworkServiceAsyncTxBackend backend)
            return false;
        ManagedServiceAsyncTxStatistics before = backend.GetAsyncTxStatistics();
        if (backend.TryGetServiceTxCompletion(staleRequestId) !=
                ManagedServiceAsyncTxCompletionResult.InvalidOrStale ||
            backend.TryConsumeServiceTxCompletion(staleRequestId) !=
                ManagedServiceAsyncTxCompletionResult.InvalidOrStale)
            return false;
        ManagedServiceAsyncTxStatistics after = backend.GetAsyncTxStatistics();
        if (after.Outstanding != before.Outstanding || after.Accepted != before.Accepted ||
            after.Consumed != before.Consumed || after.Outstanding != 0 ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE93_STALE_SERVICE_ID_REJECTED=1\r\n"u8) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE93_INVALIDATED_PENDING_AT_RESTART=0x"u8,
                invalidatedPending) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE93_POST_RESTART_SERVICE_READY=1\r\n"u8) ||
            !TryAcceptEchoes(1, out _lastAcceptedRequestId) ||
            !TryDrainCompletions(1))
            return false;

        ManagedServiceAsyncTxStatistics drained = backend.GetAsyncTxStatistics();
        if (drained.Accepted != 1 || drained.Completed != 1 || drained.Failed != 0 ||
            drained.Consumed != 1 || drained.Outstanding != 0 ||
            _service.UdpEchoAcceptedCount != 1 ||
            _service.UdpEchoBusyDropCount != 0 ||
            !WriteSummary("RESTART"u8, drained))
            return false;
        return KernelLog.Write(
            "GXOS_NET10:PHASE93_POST_RESTART_SERVICE_LIVENESS_PASS=1\r\n"u8);
    }

    private bool TryAcceptEchoes(int expected,
                                 out ulong lastAcceptedRequestId)
    {
        lastAcceptedRequestId = 0;
        int accepted = 0;
        for (int poll = 0; poll < PollBudgetPerRequest * expected &&
             accepted < expected; ++poll)
        {
            NetworkOperationResult polled = _service.Poll();
            if (polled == NetworkOperationResult.Unavailable ||
                polled == NetworkOperationResult.Failed)
                return false;
            if (!_service.HasReceivedUdp) continue;

            ManagedServiceAsyncTxSubmitResult submitted =
                _service.TrySendReceivedUdpEchoAsync(out ulong requestId);
            if (submitted == ManagedServiceAsyncTxSubmitResult.Busy)
                continue; // The bounded best-effort policy drops this echo.
            if (submitted != ManagedServiceAsyncTxSubmitResult.Accepted ||
                requestId == 0)
                return false;
            accepted++;
            lastAcceptedRequestId = requestId;
        }
        return accepted == expected;
    }

    private bool TryDrainCompletions(int expected)
    {
        for (int poll = 0; poll < DrainPollBudget; ++poll)
        {
            ManagedServiceAsyncTxStatistics snapshot = _service.AsyncTxStatistics;
            if (snapshot.Outstanding == 0 && snapshot.Consumed == expected)
                return true;
            NetworkOperationResult result = _service.Poll();
            if (result == NetworkOperationResult.Unavailable ||
                result == NetworkOperationResult.Failed || _service.HasReceivedUdp)
                return false;
        }
        return false;
    }

    private static bool WriteSummary(ReadOnlySpan<byte> phase,
                                     ManagedServiceAsyncTxStatistics stats)
    {
        ReadOnlySpan<byte> prefix = phase.SequenceEqual("INITIAL"u8)
            ? "GXOS_NET10:PHASE93_INITIAL_"u8
            : "GXOS_NET10:PHASE93_RESTART_"u8;
        return KernelLog.WriteHexLine(Join(prefix, "ASYNC_ACCEPTED="u8),
                   stats.Accepted) &&
            KernelLog.WriteHexLine(Join(prefix, "COMPLETION_OBSERVED="u8),
                   stats.Completed + stats.Failed) &&
            KernelLog.WriteHexLine(Join(prefix, "COMPLETION_SUCCEEDED="u8),
                   stats.Completed) &&
            KernelLog.WriteHexLine(Join(prefix, "COMPLETION_FAILED="u8),
                   stats.Failed) &&
            KernelLog.WriteHexLine(Join(prefix, "COMPLETION_CONSUMED="u8),
                   stats.Consumed) &&
            KernelLog.WriteHexLine(Join(prefix, "SERVICE_OUTSTANDING="u8),
                   (uint)stats.Outstanding) &&
            KernelLog.WriteHexLine(Join(prefix, "BUSY="u8), stats.Busy) &&
            KernelLog.WriteHexLine(Join(prefix, "INVALID="u8), stats.Invalid) &&
            KernelLog.WriteHexLine(Join(prefix, "UNAVAILABLE="u8),
                   stats.Unavailable) &&
            KernelLog.WriteHexLine(Join(prefix, "INVALIDATED="u8),
                   stats.Invalidated) &&
            KernelLog.WriteHexLine(Join(prefix, "RECLAMATION_FAILURES="u8),
                   stats.ReclamationFailures);
    }

    private static ReadOnlySpan<byte> Join(ReadOnlySpan<byte> prefix,
                                            ReadOnlySpan<byte> suffix)
    {
        /* Only static, bounded marker fragments are used by the two calls. */
        return phaseMarkerJoin(prefix, suffix);
    }

    private static ReadOnlySpan<byte> phaseMarkerJoin(
        ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> suffix)
    {
        /* A span cannot refer to a newly created array. Keep each marker as a
           fixed complete token so NativeAOT does not need dynamic formatting. */
        if (prefix.SequenceEqual("GXOS_NET10:PHASE93_INITIAL_"u8))
        {
            if (suffix.SequenceEqual("ASYNC_ACCEPTED="u8)) return "GXOS_NET10:PHASE93_INITIAL_ASYNC_ACCEPTED=0x"u8;
            if (suffix.SequenceEqual("COMPLETION_OBSERVED="u8)) return "GXOS_NET10:PHASE93_INITIAL_COMPLETION_OBSERVED=0x"u8;
            if (suffix.SequenceEqual("COMPLETION_SUCCEEDED="u8)) return "GXOS_NET10:PHASE93_INITIAL_COMPLETION_SUCCEEDED=0x"u8;
            if (suffix.SequenceEqual("COMPLETION_FAILED="u8)) return "GXOS_NET10:PHASE93_INITIAL_COMPLETION_FAILED=0x"u8;
            if (suffix.SequenceEqual("COMPLETION_CONSUMED="u8)) return "GXOS_NET10:PHASE93_INITIAL_COMPLETION_CONSUMED=0x"u8;
            if (suffix.SequenceEqual("SERVICE_OUTSTANDING="u8)) return "GXOS_NET10:PHASE93_INITIAL_SERVICE_OUTSTANDING=0x"u8;
            if (suffix.SequenceEqual("BUSY="u8)) return "GXOS_NET10:PHASE93_INITIAL_BUSY=0x"u8;
            if (suffix.SequenceEqual("INVALID="u8)) return "GXOS_NET10:PHASE93_INITIAL_INVALID=0x"u8;
            if (suffix.SequenceEqual("UNAVAILABLE="u8)) return "GXOS_NET10:PHASE93_INITIAL_UNAVAILABLE=0x"u8;
            if (suffix.SequenceEqual("INVALIDATED="u8)) return "GXOS_NET10:PHASE93_INITIAL_INVALIDATED=0x"u8;
            if (suffix.SequenceEqual("RECLAMATION_FAILURES="u8)) return "GXOS_NET10:PHASE93_INITIAL_RECLAMATION_FAILURES=0x"u8;
        }
        if (suffix.SequenceEqual("ASYNC_ACCEPTED="u8)) return "GXOS_NET10:PHASE93_RESTART_ASYNC_ACCEPTED=0x"u8;
        if (suffix.SequenceEqual("COMPLETION_OBSERVED="u8)) return "GXOS_NET10:PHASE93_RESTART_COMPLETION_OBSERVED=0x"u8;
        if (suffix.SequenceEqual("COMPLETION_SUCCEEDED="u8)) return "GXOS_NET10:PHASE93_RESTART_COMPLETION_SUCCEEDED=0x"u8;
        if (suffix.SequenceEqual("COMPLETION_FAILED="u8)) return "GXOS_NET10:PHASE93_RESTART_COMPLETION_FAILED=0x"u8;
        if (suffix.SequenceEqual("COMPLETION_CONSUMED="u8)) return "GXOS_NET10:PHASE93_RESTART_COMPLETION_CONSUMED=0x"u8;
        if (suffix.SequenceEqual("SERVICE_OUTSTANDING="u8)) return "GXOS_NET10:PHASE93_RESTART_SERVICE_OUTSTANDING=0x"u8;
        if (suffix.SequenceEqual("BUSY="u8)) return "GXOS_NET10:PHASE93_RESTART_BUSY=0x"u8;
        if (suffix.SequenceEqual("INVALID="u8)) return "GXOS_NET10:PHASE93_RESTART_INVALID=0x"u8;
        if (suffix.SequenceEqual("UNAVAILABLE="u8)) return "GXOS_NET10:PHASE93_RESTART_UNAVAILABLE=0x"u8;
        if (suffix.SequenceEqual("INVALIDATED="u8)) return "GXOS_NET10:PHASE93_RESTART_INVALIDATED=0x"u8;
        return "GXOS_NET10:PHASE93_RESTART_RECLAMATION_FAILURES=0x"u8;
    }
}

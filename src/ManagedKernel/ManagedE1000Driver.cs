using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedE1000DriverState : uint
{
    Created = 0,
    Claimed = 1,
    Mapped = 2,
    DmaReady = 3,
    Initialized = 4,
    Running = 5,
    Stopping = 6,
    Stopped = 7
}

/* Bounded polling driver for the QEMU e1000e/82574L-compatible device.  The
   driver owns policy and descriptor bytes; native owns only validated MMIO,
   PCI command, and DMA capabilities. */
internal sealed class ManagedE1000Driver
{
    internal const uint DriverId = 0xD014;
    private const uint PollLimit = 100000;
    internal const uint AsyncTxCapacity = 4;
    private const uint Phase80InterruptPollLimit = 100000;
    private const uint Phase83InterruptPollLimit = 100000000;
    private const uint Phase90CompletionPollLimit = 100000000;
    /* The host observes RX_READY over a serial socket before sending the one
       frame.  Keep this bounded window long enough for that handshake without
       adding a wall-clock sleep to the guest. */
    private const uint RxPhase15PollLimit = 1000000000;
    private const uint RxReadyPollLimit = 5000000;
    private const uint RxRearmInterval = 64;
    private const ushort Phase89BoundPort = 15890;
    internal static uint Phase16MacHigh;
    internal static uint Phase16MacLow;
    private static bool s_phase35ModeEnabled;
    private static bool s_phase39ModeEnabled;
    private static bool s_phase40ModeEnabled;
    private static bool s_phase41ModeEnabled;
    private static bool s_phase42ModeEnabled;
    private static bool s_phase43ModeEnabled;
    private static bool s_phase43CapacityModeEnabled;
    private static bool s_phase44ModeEnabled;
    private static bool s_phase44CapacityModeEnabled;
    private static bool s_phase45ModeEnabled;
    private static bool s_phase45CapacityModeEnabled;
    private static bool s_phase46ModeEnabled;
    private static bool s_phase46CapacityModeEnabled;
    private static bool s_phase48ModeEnabled;
    private static bool s_phase49ModeEnabled;
    private static bool s_phase50ModeEnabled;
    private static bool s_phase51ModeEnabled;
    private static bool s_phase51ResetReuseModeEnabled;
    private static bool s_phase52ModeEnabled;
    private static bool s_phase53ModeEnabled;
    private static bool s_phase80ModeEnabled;
    private static bool s_phase83ModeEnabled;
    private static bool s_phase85ArpModeEnabled;
    private static bool s_phase86Ipv4ModeEnabled;
    private static bool s_phase87IcmpModeEnabled;
    private static bool s_phase89UdpModeEnabled;
    private static bool s_phase90AsyncTxModeEnabled;
    private static bool s_phase91LifecycleModeEnabled;
    private static bool s_phase92EnduranceModeEnabled;
    private static uint s_nextDriverEpisode;

    private readonly ManagedDevice _device;
    private ManagedDeviceResource _resource;
    private ManagedMmioMapping? _mmio;
    private ulong _claimHandle;
    private ulong _nativeClaimHandle;
    private ManagedDmaAllocation? _txRing;
    private ManagedDmaAllocation? _rxRing;
    private ManagedDmaAllocation? _txBuffers;
    private ManagedDmaAllocation? _rxBuffers;
    private ManagedEthernetLayer? _ethernet;
    private readonly byte[] _mac = new byte[6];
    private readonly byte[] _txFrame = new byte[60];
    private readonly byte[] _phase80Frame = new byte[60];
    private readonly uint[] _phase83DescriptorIndices = new uint[4];
    private readonly ManagedE1000AsyncTxLedger _asyncTxLedger = new();
    private readonly ManagedE1000SyncTxProof _syncTxProof = new();
    private readonly uint _driverEpisode;
    private ManagedInterruptDispatcher? _interruptDispatcher;
    private uint _lastSubmittedTxIndex;
    private uint _asyncTxAccepted;
    private uint _phase80LastCause;
    private uint _phase80LastUnrelatedCause;
    private uint _phase80DeliveryCount;
    private uint _phase85TxDwCount;
    private uint _phase85RxEventCount;
    private uint _phase83RxDeliveryCount;
    private uint _phase83RxDescriptorCount;
    private uint _phase83LastDescriptorStatus;
    private uint _phase83LastDescriptorErrors;
    private uint _phase83LastFrameLength;
    private uint _phase83LastDescriptorIndex;
    private uint _phase85ArpDeliveryCount;
    private uint _phase85ReplyTxCount;
    private uint _phase86Ipv4DeliveryCount;
    private uint _phase87IcmpDeliveryCount;
    private uint _phase87IcmpTxDwCount;
    private uint _phase89UdpDeliveryCount;
    private uint _phase89UdpRxEventCount;
    private uint _phase89UdpTxDwCount;
    private ulong _phase80LastSequence;
    private uint _phase82TransmitSequence;
    private bool _phase80Requested;
    private bool _phase83Requested;
    private bool _phase85ArpRequested;
    private bool _phase86Ipv4Requested;
    private bool _phase87IcmpRequested;
    private bool _phase89UdpRequested;
    private bool _phase90AsyncTxRequested;
    private bool _interruptSubscribed;
    private ulong _macValue;
    private bool _phase16Passed;
    private bool _phase17Passed;
    private bool _phase18Passed;
    private bool _phase19Passed;
    private bool _phase20Passed;
    private bool _phase21Passed;
    private bool _phase22Passed;
    private bool _phase23Passed;
    private bool _phase32Passed;
    private bool _phase33Passed;
    private bool _phase34Passed;
    private bool _phase35Passed;
    private bool _phase39Passed;
    private bool _phase40Passed;
    private bool _phase41Passed;
    private bool _phase42Passed;
    private bool _phase43Passed;
    private bool _phase44Passed;
    private bool _phase45Passed;
    private bool _phase46Passed;
    private bool _phase48Passed;
    private bool _phase49Passed;
    private bool _phase50Passed;
    private bool _phase51Passed;
    private bool _phase51NegativeMimeControlPassed;
    private bool _phase51ResetReusePassed;
    private bool _phase52Passed;
    private bool _phase53Passed;
    private uint _originalCommand;
    private uint _resultingCommand;
    private bool _pciCommandLive;
    private bool _busMasterDisabled;
    private uint _txIndex;
    private uint _asyncTxTxdwEvents;
    private uint _asyncTxSuccessfulFrames;
    private uint _synchronousTxDdPollSuccesses;
    private uint _asyncTxCompleted;
    private uint _asyncTxUnmatchedTxdw;
    private ulong _asyncTxLastEventSequence;
    private uint _phase91LastInterruptRejectMask;
    private uint _rxIndex;
    private int _rxProofReceived;
    private int _rxPhase15Received;
    private bool _phase17Requested;
    private bool _phase18Requested;
    private bool _phase19Requested;
    private bool _phase20Requested;
    private bool _phase21Requested;
    private bool _phase22Requested;
    private bool _phase23Requested;
    private bool _phase32Requested;
    private bool _phase33Requested;
    private bool _phase34Requested;
    private bool _phase35Requested;
    private bool _phase39Requested;
    private bool _phase40Requested;
    private bool _phase41Requested;
    private bool _phase42Requested;
    private bool _phase43Requested;
    private bool _phase43CapacityRequested;
    private bool _phase44Requested;
    private bool _phase44CapacityRequested;
    private bool _phase45Requested;
    private bool _phase45CapacityRequested;
    private bool _phase46Requested;
    private bool _phase46CapacityRequested;
    private bool _phase48Requested;
    private bool _phase49Requested;
    private bool _phase50Requested;
    private bool _phase51Requested;
    private bool _phase51ResetReuseRequested;
    private bool _phase52Requested;
    private bool _phase53Requested;
    private bool _phase80Passed;
    private bool _phase83Passed;
    private bool _phase85ArpPassed;
    private bool _phase86Ipv4Passed;
    private bool _phase87IcmpPassed;
    private bool _phase89UdpPassed;
    private bool _phase90AsyncTxPassed;
    private bool _phase91Requested;
    private bool _phase91Passed;
    private bool _phase92Requested;
    private bool _phase92FirstEpisodePassed;
    private bool _phase92Passed;
    private uint _phase92BusyProbes;
    private uint _phase92DescriptorWraps;
    private ulong _phase92OldEpisodeRequestId;
    private ulong _phase91StopProbeId;
    private ulong _phase90InitialRequestId;
    private ManagedE1000DriverState _state;

    private struct Phase91SyncSnapshot
    {
        internal uint Phase80Deliveries;
        internal uint SyncDdPollSuccesses;
        internal uint AsyncTxdwEvents;
        internal uint AsyncUnmatchedTxdw;
        internal uint SyncTxNotificationCount;
        internal uint LastEventCause;
        internal uint TxDescriptorIndex;
        internal uint TxDescriptorStatus;
        internal uint ImsValue;
        internal bool TxDescriptorStatusAvailable;
        internal bool ImsAvailable;
        internal ulong DriverEpisode;
        internal ulong RouteToken;
        internal ulong DispatcherSequence;
        internal bool StatsAvailable;
        internal GxManagedKernelInterruptStatsV1 InterruptStats;
        internal ulong IcrReadCount;
        internal ulong NativeSourceEvents;
        internal ulong NextEventSequence;
        internal uint RouteSubscriptionActive;
        internal uint RouteHardwareEnabled;
        internal uint LastBatchDrained;
        internal uint LastBatchDelivered;
        internal uint LastBatchRejected;
        internal GxManagedKernelInterruptEventV1 LastRejectedEvent;
        internal uint LastRejectedShapeMask;
        internal uint LastRejectedRouteMask;
        internal uint LastRejectedDriverMask;
    }

    private ManagedE1000Driver(in ManagedDevice device, uint driverEpisode)
    {
        _device = device;
        _driverEpisode = driverEpisode;
        _asyncTxLedger.StartEpisode(driverEpisode);
        _state = ManagedE1000DriverState.Created;
        _ethernet = new ManagedEthernetLayer(this);
    }

    internal ManagedE1000DriverState State => _state;
    internal ulong MacValue => _macValue;
    internal ulong TxRingBusAddress => _txRing?.BusAddress ?? 0;
    internal ulong RxRingBusAddress => _rxRing?.BusAddress ?? 0;
    internal uint OriginalCommand => _originalCommand;
    internal uint ResultingCommand => _resultingCommand;
    internal bool RxProofReceived => _rxProofReceived != 0;
    internal bool RxPhase15Received => _rxPhase15Received != 0;
    internal bool Phase16Passed => _phase16Passed ||
                                   (_ethernet != null && _ethernet.Phase16Passed);
    internal bool Phase17Passed => _phase17Passed;
    internal bool Phase18Passed => _phase18Passed;
    internal bool Phase19Passed => _phase19Passed;
    internal bool Phase20Passed => _phase20Passed;
    internal bool Phase21Passed => _phase21Passed;
    internal bool Phase22Passed => _phase22Passed;
    internal bool Phase23Passed => _phase23Passed;
    internal bool Phase32Passed => _phase32Passed;
    internal bool Phase33Passed => _phase33Passed;
    internal bool Phase34Passed => _phase34Passed;
    internal bool Phase35Passed => _phase35Passed;
    internal bool Phase39Passed => _phase39Passed;
    internal bool Phase40Passed => _phase40Passed;
    internal bool Phase41Passed => _phase41Passed;
    internal bool Phase42Passed => _phase42Passed;
    internal bool Phase43Passed => _phase43Passed;
    internal bool Phase44Passed => _phase44Passed;
    internal bool Phase45Passed => _phase45Passed;
    internal bool Phase46Passed => _phase46Passed;
    internal bool Phase48Passed => _phase48Passed;
    internal bool Phase49Passed => _phase49Passed;
    internal bool Phase50Passed => _phase50Passed;
    internal bool Phase51Passed => _phase51Passed;
    internal bool Phase51NegativeMimeControlPassed =>
        _phase51NegativeMimeControlPassed;
    internal bool Phase51ResetReusePassed => _phase51ResetReusePassed;
    internal bool Phase52Passed => _phase52Passed;
    internal bool Phase53Passed => _phase53Passed;
    internal bool Phase80Passed => _phase80Passed;
    internal bool Phase83Passed => _phase83Passed;
    internal bool Phase85ArpPassed => _phase85ArpPassed;
    internal bool Phase86Ipv4Passed => _phase86Ipv4Passed;
    internal bool Phase87IcmpPassed => _phase87IcmpPassed;
    internal bool Phase89UdpPassed => _phase89UdpPassed;
    internal bool Phase90AsyncTxPassed => _phase90AsyncTxPassed;
    internal bool Phase91Passed => _phase91Passed;
    internal bool Phase92Passed => _phase92Passed;
    internal uint AsyncTxOutstanding => _asyncTxLedger.Outstanding;
    internal uint AsyncTxCompleted => _asyncTxCompleted;
    internal uint AsyncTxUnmatchedTxdw => _asyncTxUnmatchedTxdw;
    internal uint Phase80DeliveryCount => _phase80DeliveryCount;
    internal uint SyncTxTxdwNotificationCount =>
        _syncTxProof.TxdwNotificationCount;
    internal bool SyncTxTxdwNotificationObserved => _syncTxProof.IsSatisfied;
    internal bool IsSyncTxProofAccepted(bool synchronousDdComplete,
        bool dispatchSucceeded, uint rejectedEvents) =>
        _syncTxProof.IsAccepted(synchronousDdComplete, dispatchSucceeded,
            rejectedEvents, _asyncTxLedger.Outstanding);
    internal void SealSyncTxProof() => _syncTxProof.Seal();
    internal uint Phase91LastInterruptRejectMask =>
        _phase91LastInterruptRejectMask;
#if GXOS_ENABLE_PHASE91_DIAGNOSTICS
    internal bool Phase91DiagnosticsActive => _phase91Requested &&
                                               !_phase91Passed;
#else
    internal bool Phase91DiagnosticsActive => false;
#endif

    internal void ResetPhase91InterruptRejectMask()
    {
        if (_phase91Requested) _phase91LastInterruptRejectMask = 0;
    }

    internal static void EnablePhase35Mode()
    {
        s_phase35ModeEnabled = true;
    }

    internal static void EnablePhase39Mode()
    {
        s_phase39ModeEnabled = true;
    }

    internal static void EnablePhase40Mode()
    {
        s_phase40ModeEnabled = true;
    }

    internal static void EnablePhase41Mode()
    {
        s_phase41ModeEnabled = true;
    }

    internal static void EnablePhase42Mode()
    {
        s_phase42ModeEnabled = true;
    }

    internal static void EnablePhase43Mode()
    {
        s_phase43ModeEnabled = true;
    }

    internal static void EnablePhase43CapacityMode()
    {
        s_phase43CapacityModeEnabled = true;
    }

    internal static void EnablePhase44Mode()
    {
        s_phase44ModeEnabled = true;
    }

    internal static void EnablePhase44CapacityMode()
    {
        s_phase44CapacityModeEnabled = true;
    }

    internal static void EnablePhase45Mode()
    {
        s_phase45ModeEnabled = true;
    }

    internal static void EnablePhase45CapacityMode()
    {
        s_phase45CapacityModeEnabled = true;
    }

    internal static void EnablePhase46Mode()
    {
        s_phase46ModeEnabled = true;
    }

    internal static void EnablePhase46CapacityMode()
    {
        s_phase46CapacityModeEnabled = true;
    }

    internal static void EnablePhase48Mode()
    {
        s_phase48ModeEnabled = true;
    }

    internal static void EnablePhase49Mode()
    {
        s_phase49ModeEnabled = true;
    }

    internal static void EnablePhase50Mode()
    {
        s_phase50ModeEnabled = true;
    }

    internal static void EnablePhase51Mode()
    {
        s_phase51ModeEnabled = true;
    }

    internal static void EnablePhase51ResetReuseMode()
    {
        s_phase51ResetReuseModeEnabled = true;
    }

    internal static void EnablePhase52Mode()
    {
        s_phase52ModeEnabled = true;
    }

    internal static void EnablePhase53Mode()
    {
        s_phase53ModeEnabled = true;
    }

    internal static void EnablePhase80Mode()
    {
        s_phase80ModeEnabled = true;
    }

    internal static void EnablePhase83Mode()
    {
        s_phase83ModeEnabled = true;
    }

    internal static void EnablePhase85ArpMode()
    {
        s_phase83ModeEnabled = true;
        s_phase85ArpModeEnabled = true;
    }

    internal static void EnablePhase86Ipv4Mode()
    {
        s_phase83ModeEnabled = true;
        s_phase86Ipv4ModeEnabled = true;
    }

    internal static void EnablePhase87IcmpMode()
    {
        s_phase83ModeEnabled = true;
        s_phase87IcmpModeEnabled = true;
    }

    internal static void EnablePhase89UdpMode()
    {
        s_phase83ModeEnabled = true;
        s_phase89UdpModeEnabled = true;
    }

    internal static void EnablePhase90AsyncTxMode()
    {
        s_phase80ModeEnabled = true;
        s_phase90AsyncTxModeEnabled = true;
    }

    internal static void EnablePhase91LifecycleMode()
    {
        s_phase91LifecycleModeEnabled = true;
        s_phase83ModeEnabled = true;
        EnablePhase90AsyncTxMode();
    }

    internal static void EnablePhase92EnduranceMode()
    {
        s_phase92EnduranceModeEnabled = true;
        EnablePhase91LifecycleMode();
    }

    internal static ManagedE1000Driver? TryCreate()
    {
        ManagedDeviceInventory? inventory =
            ManagedKernelContract.OperationalDeviceInventory;
        if (!ManagedKernelContract.IsStarted ||
            !ManagedKernelContract.DeviceResourcesInstalled ||
            !ManagedKernelContract.PciServicesInstalled ||
            !ManagedKernelContract.MmioServicesInstalled ||
            !ManagedKernelContract.DmaServicesInstalled ||
            inventory == null ||
            !inventory.TryFindPciDevice(0, 0, 2, 0, out ManagedDevice device) ||
               !ManagedE1000Protocol.IsTarget(device.Segment, device.Bus, device.Device,
                                              device.Function, device.VendorId,
                                              device.DeviceId, device.ClassCode,
                                              device.Subclass,
                                              device.ProgrammingInterface)) return null;
        if (s_nextDriverEpisode == uint.MaxValue) return null;
        uint episode = ++s_nextDriverEpisode;
        return new ManagedE1000Driver(in device, episode);
    }

    internal bool TryStart()
    {
        _phase35Requested = s_phase35ModeEnabled;
        _phase39Requested = s_phase39ModeEnabled;
        _phase40Requested = s_phase40ModeEnabled;
        _phase41Requested = s_phase41ModeEnabled;
        _phase42Requested = s_phase42ModeEnabled;
        _phase43CapacityRequested = s_phase43CapacityModeEnabled;
        _phase43Requested = s_phase43ModeEnabled || _phase43CapacityRequested;
        _phase44CapacityRequested = s_phase44CapacityModeEnabled;
        _phase44Requested = s_phase44ModeEnabled || _phase44CapacityRequested;
        _phase45CapacityRequested = s_phase45CapacityModeEnabled;
        _phase45Requested = s_phase45ModeEnabled || _phase45CapacityRequested;
        _phase46CapacityRequested = s_phase46CapacityModeEnabled;
        _phase46Requested = s_phase46ModeEnabled || _phase46CapacityRequested;
        _phase48Requested = s_phase48ModeEnabled;
        _phase49Requested = s_phase49ModeEnabled;
        _phase50Requested = s_phase50ModeEnabled;
        _phase51Requested = s_phase51ModeEnabled;
        _phase51ResetReuseRequested = s_phase51ResetReuseModeEnabled;
        _phase52Requested = s_phase52ModeEnabled;
        _phase53Requested = s_phase53ModeEnabled;
        _phase80Requested = s_phase80ModeEnabled;
        _phase83Requested = s_phase83ModeEnabled;
        _phase85ArpRequested = s_phase85ArpModeEnabled;
        _phase86Ipv4Requested = s_phase86Ipv4ModeEnabled;
        _phase87IcmpRequested = s_phase87IcmpModeEnabled;
        _phase89UdpRequested = s_phase89UdpModeEnabled;
        _phase90AsyncTxRequested = s_phase90AsyncTxModeEnabled;
        _phase91Requested = s_phase91LifecycleModeEnabled;
        _phase92Requested = s_phase92EnduranceModeEnabled;
        return TryStartCore();
    }

    private bool TryStartCore()
    {
        if (_state != ManagedE1000DriverState.Created || !FindBar(out _resource) ||
            !ManagedDeviceResourceRuntimeCatalog.TryClaim(
                in _resource, DriverId,
                GxManagedKernelDeviceV1.DeviceKindPci,
                ManagedE1000Protocol.PciOwnerId)) return AbortStart();
        _state = ManagedE1000DriverState.Claimed;
        if (!ManagedDeviceResourceRuntimeCatalog.TryGetNativeClaimHandle(
                in _resource, DriverId, out _nativeClaimHandle))
        {
            ManagedDeviceResourceRuntimeCatalog.TryRelease(in _resource, DriverId);
            _state = ManagedE1000DriverState.Stopped;
            return false;
        }
        _claimHandle = _nativeClaimHandle;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_DEVICE_CLAIMED\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_RESOURCE_ID=0x"u8,
                                    _resource.ResourceId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_BAR=0x"u8,
                                    _resource.PhysicalBase) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_BAR_LENGTH=0x"u8,
                                    _resource.Length))
            return AbortStart();

        if (!ManagedDeviceResourceRuntimeCatalog.TryMap(
                in _resource, DriverId, 0, ManagedE1000Protocol.BarLength, 3,
                out _mmio) || _mmio == null || !_mmio.CanWrite)
            return AbortStart();
        _state = ManagedE1000DriverState.Mapped;
        if (!RunMmioWriteNegativeTests() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_MMIO_WRITE_MAPPING_READY\r\n"u8))
            return AbortStart();

        if (!ManagedKernelContract.TryPciCommandEnable(
                _resource.ResourceId, _claimHandle, DriverId,
                ManagedE1000Protocol.PciCommandRequired,
                out GxManagedKernelPciCommandResultV1 commandResult))
            return AbortStart();
        _originalCommand = commandResult.OriginalCommand;
        _resultingCommand = commandResult.ResultingCommand;
        _pciCommandLive = true;
        if (!ManagedE1000Protocol.TryPlanPciCommand(
                (ushort)_originalCommand,
                (ushort)ManagedE1000Protocol.PciCommandRequired,
                out ushort plannedCommand) ||
            (ushort)_resultingCommand != plannedCommand ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_COMMAND_ORIGINAL=0x"u8,
                                    _originalCommand) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_COMMAND_RESULT=0x"u8,
                                    _resultingCommand) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_BUS_MASTER_ENABLED\r\n"u8))
            return AbortStart();

        if (!ReadMac() || !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC_VALID\r\n"u8))
            return AbortStart();
        Phase16MacHigh = ((uint)_mac[0] << 8) | _mac[1];
        Phase16MacLow = ((uint)_mac[2] << 24) | ((uint)_mac[3] << 16) |
                        ((uint)_mac[4] << 8) | _mac[5];
        _ethernet!.InitializeMac();
        if (_phase83Requested && !_phase85ArpRequested &&
            !_phase86Ipv4Requested && !_phase87IcmpRequested &&
            !_phase89UdpRequested)
            _ethernet.EnablePhase84TestConsumer();
        if (_phase85ArpRequested &&
            (!_ethernet.TryEnablePhase85ArpAcceptance() ||
             !KernelLog.WriteHexLine(
                 "GXOS_NET10:PHASE85_LOCAL_IPV4=0x"u8,
                 _ethernet.LocalIpv4Value) ||
             !KernelLog.Write("GXOS_NET10:PHASE85_ARP_HANDLER_READY=1\r\n"u8)))
            return AbortStart();
        if (_phase86Ipv4Requested &&
            (!_ethernet.TryEnablePhase86Ipv4Acceptance() ||
             !KernelLog.WriteHexLine(
                 "GXOS_NET10:PHASE86_LOCAL_IPV4=0x"u8,
                 _ethernet.LocalIpv4Value) ||
             !KernelLog.Write(
                 "GXOS_NET10:PHASE86_IPV4_PARSER_READY=1\r\n"u8)))
            return AbortStart();
        if (_phase87IcmpRequested &&
            (!_ethernet.TryEnablePhase87IcmpAcceptance() ||
             !KernelLog.WriteHexLine(
                 "GXOS_NET10:PHASE87_LOCAL_IPV4=0x"u8,
                 _ethernet.LocalIpv4Value) ||
             !KernelLog.Write(
                 "GXOS_NET10:PHASE87_ICMP_HANDLER_READY=1\r\n"u8)))
            return AbortStart();
        if (_phase89UdpRequested &&
            (!_ethernet.TryEnablePhase89UdpAcceptance() ||
             _ethernet.BindPhase89UdpPort(Phase89BoundPort) !=
                 NetworkOperationResult.Started ||
             !KernelLog.WriteHexLine(
                 "GXOS_NET10:PHASE89_LOCAL_IPV4=0x"u8,
                 _ethernet.LocalIpv4Value) ||
             !KernelLog.Write(
                 "GXOS_NET10:PHASE89_UDP_BOUND_PORT=0x3E12\r\n"u8) ||
             !KernelLog.Write(
                 "GXOS_NET10:PHASE89_UDP_DISPATCH_READY=1\r\n"u8)))
            return AbortStart();
        if (!AllocateDma() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_CAPABILITY_READY\r\n"u8))
            return AbortStart();
        _state = ManagedE1000DriverState.DmaReady;
        if (!ConfigureRings() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_RING_READY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RING_READY\r\n"u8))
            return AbortStart();
        if (!InitializeDevice() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_INITIALIZED\r\n"u8))
            return AbortStart();
        _state = ManagedE1000DriverState.Initialized;
        if (_phase80Requested &&
            !ManagedSerialDriverSubsystem.TrySubscribeE1000Interrupt(this))
            return AbortStart();
        if (!SubmitProofFrame() || !PollTxCompletion()) return AbortStart();
        _state = ManagedE1000DriverState.Running;
        if (!RunGcSurvival() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE15_GC_SURVIVAL_PASSED\r\n"u8) ||
            !ArmRxForExternalFrame() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_CONFIGURED\r\n"u8) ||
            !WriteRxStateSnapshot(
                "GXOS_NET10:MANAGED_E1000_RX_STATE=BEFORE_INJECTION\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_READY\r\n"u8))
            return AbortStart();
        if (s_phase35ModeEnabled)
        {
            s_phase35ModeEnabled = false;
            Phase16MacHigh = (uint)(_macValue >> 32);
            Phase16MacLow = (uint)_macValue;
            _phase35Requested = true;
            _ethernet!.InitializeMac();
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_PUBLIC_HTTPS_STARTING\r\n"u8) ||
                !_ethernet.TryRunPhase35())
            {
                KernelLog.Write(
                    "GXOS_NET10:MANAGED_PUBLIC_HTTPS_START_FAILED\r\n"u8);
                return AbortStart();
            }
        }
        else if (_phase83Requested)
        {
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_E1000_RX_HARNESS_DEFERRED_TO_PHASE83\r\n"u8))
                return AbortStart();
        }
        else if (_phase80Requested)
        {
            /* Phase 80 proves TX completion delivery only; keep its boot
               bounded without waiting for the independent Phase 15 RX source. */
            if (!KernelLog.Write(
                    "GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_HARNESS_DEFERRED\r\n"u8) ||
                !KernelLog.Write(
                    "GXOS_NET10:MANAGED_E1000_RX_HARNESS_DEFERRED\r\n"u8))
                return AbortStart();
        }
        else if (!PollPhase15RxProof()) return AbortStart();
        if (_rxPhase15Received != 0)
        {
            if (_mmio == null ||
                !_mmio.TryRead32(ManagedE1000Protocol.RegRal, out uint ral) ||
                !_mmio.TryRead32(ManagedE1000Protocol.RegRah, out uint rah))
                return AbortStart();
            byte mac0 = (byte)ral;
            byte mac1 = (byte)(ral >> 8);
            byte mac2 = (byte)(ral >> 16);
            byte mac3 = (byte)(ral >> 24);
            byte mac4 = (byte)rah;
            byte mac5 = (byte)(rah >> 8);
            Phase16MacHigh = (uint)((mac0 << 8) | mac1);
            Phase16MacLow = ((uint)mac2 << 24) | ((uint)mac3 << 16) |
                            ((uint)mac4 << 8) | mac5;
            _ethernet!.InitializeMac();
            if (_phase53Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE53_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase53())
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE53_START_FAILED\r\n"u8);
                    return AbortStart();
                }
                _phase53Passed = true;
            }
            else if (_phase52Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE52_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase52())
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE52_START_FAILED\r\n"u8);
                    return AbortStart();
                }
                _phase52Passed = true;
            }
            else if (_phase51ResetReuseRequested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE51_RESET_REUSE_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase51(resetReuse: true))
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE51_RESET_REUSE_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase51Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE51_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase51())
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE51_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase50Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE50_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase50())
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE50_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase49Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE49_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase49())
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE49_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase48Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE48_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase48())
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE48_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase46Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE46_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase46(_phase46CapacityRequested))
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE46_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase45Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE45_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase45(_phase45CapacityRequested))
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE45_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase44Requested)
            {
                if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE44_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase44(_phase44CapacityRequested))
                {
                    KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE44_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase43Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE43_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase43(_phase43CapacityRequested))
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE43_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase42Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE42_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase42())
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE42_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase41Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE41_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase41())
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE41_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase40Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE40_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase40())
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE40_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase39Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE39_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase39())
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE39_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase34Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE34_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase34())
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE34_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase33Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE33_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase33())
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE33_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase32Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE32_STARTING\r\n"u8) ||
                    !_ethernet.TryRunPhase32())
                {
                    KernelLog.Write(
                        "GXOS_NET10:MANAGED_KERNEL_PHASE32_START_FAILED\r\n"u8);
                    return AbortStart();
                }
            }
            else if (_phase23Requested)
            {
                if (!_ethernet.TryRunPhase23()) return AbortStart();
            }
            else if (_phase22Requested)
            {
                if (!_ethernet.TryRunPhase22()) return AbortStart();
            }
            else if (_phase21Requested)
            {
                if (!_ethernet.TryRunPhase21()) return AbortStart();
            }
            else if (_phase20Requested)
            {
                if (!_ethernet.TryRunPhase20()) return AbortStart();
            }
            else if (_phase19Requested)
            {
                if (!_ethernet.TryRunPhase19()) return AbortStart();
            }
            else if (_phase18Requested)
            {
                if (!_ethernet.TryRunPhase18()) return AbortStart();
            }
            else if (_phase17Requested)
            {
                if (!_ethernet.TryRunPhase17()) return AbortStart();
            }
            else if (!_ethernet.TryRunPhase16()) return AbortStart();
        }
        return true;
    }

    private bool AbortStart()
    {
        if (_state != ManagedE1000DriverState.Stopped)
            _state = ManagedE1000DriverState.Stopping;
        _asyncTxLedger.BeginStopping();
        s_phase35ModeEnabled = false;
        s_phase39ModeEnabled = false;
        s_phase40ModeEnabled = false;
        s_phase41ModeEnabled = false;
        s_phase42ModeEnabled = false;
        s_phase43ModeEnabled = false;
        s_phase43CapacityModeEnabled = false;
        s_phase44ModeEnabled = false;
        s_phase44CapacityModeEnabled = false;
        s_phase45ModeEnabled = false;
        s_phase45CapacityModeEnabled = false;
        s_phase46ModeEnabled = false;
        s_phase46CapacityModeEnabled = false;
        s_phase48ModeEnabled = false;
        s_phase49ModeEnabled = false;
        s_phase50ModeEnabled = false;
        s_phase51ModeEnabled = false;
        s_phase52ModeEnabled = false;
        s_phase53ModeEnabled = false;
        s_phase80ModeEnabled = false;
        s_phase83ModeEnabled = false;
        s_phase85ArpModeEnabled = false;
        s_phase86Ipv4ModeEnabled = false;
        s_phase87IcmpModeEnabled = false;
        s_phase89UdpModeEnabled = false;
        s_phase90AsyncTxModeEnabled = false;
        bool safe = true;
        if (_interruptSubscribed &&
            !ManagedSerialDriverSubsystem.TryUnsubscribeE1000Interrupt(this))
            safe = false;
        if (_ethernet != null)
        {
            safe = _ethernet.TryStop() && safe;
            _ethernet = null;
        }
        if (_pciCommandLive && _mmio != null)
            safe = DisableEngines() && DisableBusMaster() && safe;
        if (!safe) return false;
        if (!_asyncTxLedger.InvalidateAfterDmaQuiesced()) return false;
        if (!safe) return false;
        ManagedDmaAllocation?[] allocations =
            { _txRing, _rxRing, _txBuffers, _rxBuffers };
        foreach (ManagedDmaAllocation? allocation in allocations)
        {
            if (allocation != null && !allocation.TryReleaseForTeardown())
                return false;
        }
        _txRing = null;
        _rxRing = null;
        _txBuffers = null;
        _rxBuffers = null;
        if (_pciCommandLive)
        {
            if (!_busMasterDisabled || !ManagedKernelContract.TryPciCommandRestore(
                    _resource.ResourceId, _claimHandle, DriverId,
                    _originalCommand, out _)) return false;
            _pciCommandLive = false;
        }
        if (_mmio != null && _mmio.IsLive && !_mmio.TryUnmap()) return false;
        _mmio = null;
        if (_claimHandle != 0 &&
            !ManagedDeviceResourceRuntimeCatalog.TryRelease(in _resource, DriverId))
            return false;
        _claimHandle = 0;
        _nativeClaimHandle = 0;
        _state = ManagedE1000DriverState.Stopped;
        KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_ACCOUNTING_RESTORED\r\n"u8);
        return false;
    }

    internal bool TryStop()
    {
        if (_state == ManagedE1000DriverState.Running)
        {
            _state = ManagedE1000DriverState.Stopping;
            _asyncTxLedger.BeginStopping();
            if (_phase91Requested)
            {
                if (!KernelLog.Write(
                        "GXOS_NET10:PHASE91_STOP_BEGINS=1\r\n"u8))
                    return false;
                bool rejected = TrySubmitFrame(_phase80Frame,
                        _phase80Frame.Length, out _) ==
                    ManagedE1000AsyncTxSubmitResult.Unavailable &&
                    TryGetTransmitCompletion(_phase91StopProbeId) ==
                    ManagedE1000AsyncTxResult.InvalidOrStale &&
                    TryConsumeTransmitCompletion(_phase91StopProbeId) ==
                    ManagedE1000AsyncTxResult.InvalidOrStale;
                GxManagedKernelInterruptEventV1 lateTxdw = new()
                {
                    Size = GxManagedKernelInterruptEventV1.ExpectedSize,
                    AbiVersion = GxManagedKernelInterruptEventV1.AbiVersionCurrent,
                    EventType = ManagedE1000Protocol.InterruptEventType,
                    DeviceKind = ManagedE1000Protocol.PciDeviceKind,
                    DeviceId = ManagedE1000Protocol.PciOwnerId,
                    Sequence = _phase80LastSequence + 1,
                    Flags = GxManagedKernelInterruptEventV1.EventFlagHardwareCapture,
                    PayloadByte = (byte)ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback,
                    PayloadLength = 1,
                    Status = ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback
                };
                bool ignored = !TryHandleInterrupt(in lateTxdw);
                if (!rejected || !ignored ||
                    !KernelLog.Write(
                        "GXOS_NET10:PHASE91_STOP_ADMISSION_CLOSED=1\r\n"u8) ||
                    !KernelLog.Write(
                        "GXOS_NET10:PHASE91_STOP_LATE_TXDW_IGNORED=1\r\n"u8))
                    return false;
            }
        }
        else if (_state != ManagedE1000DriverState.Stopping) return false;
        if (_interruptSubscribed &&
            !ManagedSerialDriverSubsystem.TryUnsubscribeE1000Interrupt(this))
            return false;
        if (_phase91Requested && !KernelLog.Write(
                "GXOS_NET10:PHASE91_STOP_ROUTE_REMOVED=1\r\n"u8))
            return false;
        ManagedEthernetLayer? ethernet = (_phase35Requested || _phase39Requested || _phase40Requested || _phase41Requested || _phase42Requested || _phase43Requested || _phase43CapacityRequested || _phase44Requested || _phase44CapacityRequested || _phase45Requested || _phase45CapacityRequested || _phase46Requested || _phase46CapacityRequested || _phase48Requested || _phase49Requested || _phase50Requested || _phase51Requested || _phase51ResetReuseRequested || _phase52Requested || _phase53Requested || _phase34Requested || _phase33Requested || _phase32Requested || _phase23Requested || _phase22Requested || _phase21Requested)
            ? ManagedNetworkServiceBackend.LiveEthernet ?? _ethernet
            : _ethernet;
        if (ethernet != null)
        {
            _phase16Passed = ethernet.Phase16Passed;
            _phase17Passed = ethernet.Phase17Passed;
            _phase18Passed = ethernet.Phase18Passed;
            _phase19Passed = ethernet.Phase19Passed;
            _phase20Passed = ethernet.Phase20Passed;
            _phase21Passed = ethernet.Phase21Passed;
            _phase22Passed = ethernet.Phase22Passed;
            _phase23Passed = ethernet.Phase23Passed;
            _phase32Passed = ethernet.Phase32Passed;
            _phase33Passed = ethernet.Phase33Passed;
            _phase34Passed = ethernet.Phase34Passed;
            _phase35Passed = ethernet.Phase35Passed;
            _phase39Passed = ethernet.Phase39Passed;
            _phase40Passed = ethernet.Phase40Passed;
            _phase41Passed = ethernet.Phase41Passed;
            _phase42Passed = ethernet.Phase42Passed;
            _phase43Passed = ethernet.Phase43Passed;
            _phase44Passed = ethernet.Phase44Passed;
            _phase45Passed = ethernet.Phase45Passed;
            _phase46Passed = ethernet.Phase46Passed;
            _phase48Passed = ethernet.Phase48Passed;
            _phase49Passed = ethernet.Phase49Passed;
            _phase50Passed = ethernet.Phase50Passed;
            _phase51Passed = ethernet.Phase51Passed;
            _phase51NegativeMimeControlPassed =
                ethernet.Phase51NegativeMimeControlPassed;
            _phase51ResetReusePassed = ethernet.Phase51ResetReusePassed;
            _phase52Passed = ethernet.Phase52Passed;
            _phase53Passed = ethernet.Phase53Passed;
        }
        bool result = (ethernet == null || ethernet.TryStop()) &&
                      DisableEngines() && DisableBusMaster();
        if (!result) return false;
        if (!_asyncTxLedger.InvalidateAfterDmaQuiesced()) return false;
        if (_phase91Requested &&
            (_asyncTxLedger.Outstanding != 0 ||
             TryGetTransmitCompletion(_phase91StopProbeId) !=
                 ManagedE1000AsyncTxResult.InvalidOrStale ||
             TryConsumeTransmitCompletion(_phase91StopProbeId) !=
                 ManagedE1000AsyncTxResult.InvalidOrStale ||
             !KernelLog.Write(
                 "GXOS_NET10:PHASE91_STOP_LEDGER_INVALIDATED=1\r\n"u8)))
            return false;
        if (!ReleaseDmaAndRestorePci()) return false;
        if (_phase91Requested &&
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_DMA_RELEASE_AFTER_RESET=1\r\n"u8))
            return false;
        _ethernet = null;
        if (_mmio != null && _mmio.IsLive && !_mmio.TryUnmap()) return false;
        _mmio = null;
        if (!ManagedDeviceResourceRuntimeCatalog.TryRelease(in _resource, DriverId))
            return false;
        _claimHandle = 0;
        _nativeClaimHandle = 0;
        _state = ManagedE1000DriverState.Stopped;
        s_phase83ModeEnabled = false;
        s_phase85ArpModeEnabled = false;
        s_phase86Ipv4ModeEnabled = false;
        s_phase87IcmpModeEnabled = false;
        s_phase89UdpModeEnabled = false;
        s_phase90AsyncTxModeEnabled = false;
        s_phase91LifecycleModeEnabled = false;
        s_phase92EnduranceModeEnabled = false;
        return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_ACCOUNTING_RESTORED\r\n"u8);
    }

    private bool FindBar(out ManagedDeviceResource resource)
    {
        resource = default;
        for (uint index = 0; index != ManagedDeviceResourceRuntimeCatalog.ResourceCount; ++index)
        {
            if (!ManagedDeviceResourceRuntimeCatalog.TryGetResource(index,
                    out ManagedDeviceResource candidate) ||
                candidate.ResourceType != GxManagedKernelDeviceResourceV1.ResourceTypeMmio ||
                candidate.OwnerDeviceKind != GxManagedKernelDeviceV1.DeviceKindPci ||
                candidate.OwnerDeviceId != ManagedE1000Protocol.PciOwnerId ||
                candidate.OwnerSegment != 0 || candidate.OwnerBus != 0 ||
                candidate.OwnerDevice != 2 || candidate.OwnerFunction != 0 ||
                candidate.Length < ManagedE1000Protocol.BarLength ||
                (candidate.Flags & (GxManagedKernelDeviceResourceV1.FlagReadable |
                                    GxManagedKernelDeviceResourceV1.FlagWritable |
                                    GxManagedKernelDeviceResourceV1.FlagMemory |
                                    GxManagedKernelDeviceResourceV1.FlagCacheUncached |
                                    GxManagedKernelDeviceResourceV1.FlagPciAssigned)) !=
                (GxManagedKernelDeviceResourceV1.FlagReadable |
                 GxManagedKernelDeviceResourceV1.FlagWritable |
                 GxManagedKernelDeviceResourceV1.FlagMemory |
                 GxManagedKernelDeviceResourceV1.FlagCacheUncached |
                 GxManagedKernelDeviceResourceV1.FlagPciAssigned)) continue;
            resource = candidate;
            return true;
        }
        return false;
    }

    private bool ReadMac()
    {
        if (_mmio == null)
        {
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC_READ_FAILED=NO_MMIO\r\n"u8);
            return false;
        }
        if (!_mmio.TryRead32(ManagedE1000Protocol.RegRal, out uint ral))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC_READ_FAILED=RAL\r\n"u8);
            return false;
        }
        if (!_mmio.TryRead32(ManagedE1000Protocol.RegRah, out uint rah))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC_READ_FAILED=RAH\r\n"u8);
            return false;
        }
        _mac[0] = (byte)ral;
        _mac[1] = (byte)(ral >> 8);
        _mac[2] = (byte)(ral >> 16);
        _mac[3] = (byte)(ral >> 24);
        _mac[4] = (byte)rah;
        _mac[5] = (byte)(rah >> 8);
        ulong macValue = ((ulong)_mac[0] << 40) | ((ulong)_mac[1] << 32) |
                         ((ulong)_mac[2] << 24) | ((ulong)_mac[3] << 16) |
                         ((ulong)_mac[4] << 8) | _mac[5];
        _macValue = macValue;
        if (ManagedE1000Protocol.IsInvalidMac(_mac))
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC_READ_FAILED=INVALID_0x"u8,
                macValue);
            return false;
        }
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x"u8,
                                      macValue);
    }

    private bool AllocateDma()
    {
        _txRing = ManagedDmaAllocation.TryAllocate(
            _claimHandle, DriverId,
            ManagedE1000Protocol.DescriptorSize * ManagedE1000Protocol.RingCount, 4096);
        _rxRing = ManagedDmaAllocation.TryAllocate(
            _claimHandle, DriverId,
            ManagedE1000Protocol.DescriptorSize * ManagedE1000Protocol.RingCount, 4096);
        _txBuffers = ManagedDmaAllocation.TryAllocate(
            _claimHandle, DriverId,
            ManagedE1000Protocol.PacketBufferSize * ManagedE1000Protocol.RingCount, 4096);
        _rxBuffers = ManagedDmaAllocation.TryAllocate(
            _claimHandle, DriverId,
            ManagedE1000Protocol.PacketBufferSize * ManagedE1000Protocol.RingCount, 4096);
        if (_txRing == null || _rxRing == null || _txBuffers == null || _rxBuffers == null)
        {
            KernelLog.Write(
                _txRing == null
                    ? "GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_ALLOC_FAILED=TX_RING\r\n"u8
                    : _rxRing == null
                        ? "GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_ALLOC_FAILED=RX_RING\r\n"u8
                        : _txBuffers == null
                            ? "GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_ALLOC_FAILED=TX_BUFFERS\r\n"u8
                            : "GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_ALLOC_FAILED=RX_BUFFERS\r\n"u8);
            return false;
        }
        if (ManagedKernelContract.TryDmaAllocate(
                _claimHandle, DriverId, 0, 4096, out _) ||
            ManagedKernelContract.TryDmaAllocate(
                _claimHandle, DriverId, 33UL * 4096, 4096, out _) ||
            ManagedKernelContract.TryDmaAllocate(
                _claimHandle, DriverId, 4096, ulong.MaxValue, out _) ||
            ManagedKernelContract.TryDmaRelease(
                0xFFFFFFFF00000001, DriverId) ||
            ManagedKernelContract.TryDmaRelease(_txRing.Handle, DriverId + 1) ||
            _txRing.TryRelease())
            return false;
        ManagedDmaAllocation? temporary = ManagedDmaAllocation.TryAllocate(
            _claimHandle, DriverId, 4096, 4096);
        if (temporary == null || !temporary.TryReleaseReference() ||
            !temporary.TryRelease() ||
            ManagedKernelContract.TryDmaRelease(temporary.Handle, DriverId) ||
            temporary.TryRead8(0, out _))
            return false;
        if (!_phase35Requested && !_phase39Requested && !_phase40Requested &&
            !_phase41Requested &&
            !_phase42Requested && !_phase43Requested && !_phase43CapacityRequested &&
            !_phase44Requested && !_phase44CapacityRequested &&
            !_phase45Requested && !_phase45CapacityRequested &&
            !_phase46Requested && !_phase46CapacityRequested && !_phase48Requested &&
            !_phase49Requested && !_phase50Requested && !_phase51Requested &&
            !_phase51ResetReuseRequested && !_phase52Requested && !_phase53Requested &&
            !RunDmaCapacityNegativeTest()) return false;
        if (!KernelLog.Write(_phase35Requested || _phase40Requested || _phase41Requested || _phase42Requested || _phase43Requested || _phase43CapacityRequested || _phase44Requested || _phase44CapacityRequested || _phase46Requested || _phase46CapacityRequested || _phase48Requested || _phase49Requested || _phase50Requested || _phase51Requested || _phase51ResetReuseRequested || _phase52Requested || _phase53Requested
                ? "GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_NEGATIVE_TESTS_CONCURRENT_RNG\r\n"u8
                : "GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_NEGATIVE_TESTS_OK\r\n"u8))
            return false;
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_RING_BUS=0x"u8,
                                      _txRing.BusAddress) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RING_BUS=0x"u8,
                                      _rxRing.BusAddress) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_BUFFERS_BUS=0x"u8,
                                      _txBuffers.BusAddress) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_BUFFERS_BUS=0x"u8,
                                      _rxBuffers.BusAddress);
    }

    private bool RunDmaCapacityNegativeTest()
    {
        ManagedDmaAllocation?[] fillers = new ManagedDmaAllocation?[4];
        for (int index = 0; index != fillers.Length; ++index)
        {
            fillers[index] = ManagedDmaAllocation.TryAllocate(
                _claimHandle, DriverId, 4096, 4096);
            if (fillers[index] == null)
            {
                foreach (ManagedDmaAllocation? allocated in fillers)
                {
                    if (allocated != null)
                    {
                        allocated.TryReleaseReference();
                        allocated.TryRelease();
                    }
                }
                return false;
            }
        }
        bool exhausted = ManagedDmaAllocation.TryAllocate(
            _claimHandle, DriverId, 4096, 4096) == null;
        bool released = true;
        foreach (ManagedDmaAllocation? filler in fillers)
        {
            released &= filler != null && filler.TryReleaseReference() &&
                        filler.TryRelease();
        }
        return exhausted && released;
    }

    private bool ConfigureRings()
    {
        if (_txRing == null || _rxRing == null || _txBuffers == null || _rxBuffers == null)
            return false;
        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        for (uint index = 0; index != ManagedE1000Protocol.RingCount; ++index)
        {
            ulong bufferAddress = _rxBuffers.BusAddress +
                                  (ulong)index * ManagedE1000Protocol.PacketBufferSize;
            if (bufferAddress < _rxBuffers.BusAddress ||
                !ManagedE1000Protocol.TryPrepareRxDescriptor(descriptor, bufferAddress))
                return false;
            if (!_rxRing.TryWrite((ulong)index * ManagedE1000Protocol.DescriptorSize,
                                  descriptor)) return false;
            descriptor.Clear();
            descriptor[12] = ManagedE1000Protocol.TxStatusDone;
            if (!_txRing.TryWrite((ulong)index * ManagedE1000Protocol.DescriptorSize,
                                  descriptor)) return false;
        }
        if (!WriteRingBase(ManagedE1000Protocol.RegRxDbaLow,
                           ManagedE1000Protocol.RegRxDbaHigh, _rxRing.BusAddress) ||
            !WriteRingBase(ManagedE1000Protocol.RegTxDbaLow,
                           ManagedE1000Protocol.RegTxDbaHigh, _txRing.BusAddress) ||
            !WriteRegister(ManagedE1000Protocol.RegRxDescLength,
                           ManagedE1000Protocol.DescriptorSize * ManagedE1000Protocol.RingCount) ||
            !WriteRegister(ManagedE1000Protocol.RegTxDescLength,
                           ManagedE1000Protocol.DescriptorSize * ManagedE1000Protocol.RingCount) ||
            !WriteRegister(ManagedE1000Protocol.RegRxDescHead, 0) ||
            !WriteRegister(ManagedE1000Protocol.RegRxDescTail, ManagedE1000Protocol.RingCount - 1) ||
            !WriteRegister(ManagedE1000Protocol.RegTxDescHead, 0) ||
            !WriteRegister(ManagedE1000Protocol.RegTxDescTail, 0)) return false;
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_RING_COUNT=0x"u8,
                                      ManagedE1000Protocol.RingCount) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RING_COUNT=0x"u8,
                                      ManagedE1000Protocol.RingCount);
    }

    private bool InitializeDevice()
    {
        if (_mmio == null || !_mmio.TryRead32(ManagedE1000Protocol.RegStatus,
                                               out uint status) || status == 0xFFFFFFFFU ||
            !WriteRegister(ManagedE1000Protocol.RegRctl, 0) ||
            !WriteRegister(ManagedE1000Protocol.RegTctl, 0) ||
            !WriteRegister(ManagedE1000Protocol.RegTipg, 0x0060200A) ||
            !WriteRegister(ManagedE1000Protocol.RegRctl,
                ManagedE1000Protocol.ReceiveEnable |
                ManagedE1000Protocol.ReceiveBroadcast |
                ManagedE1000Protocol.ReceiveBuffer2048 |
                ManagedE1000Protocol.ReceiveStripCrc) ||
            !WriteRegister(ManagedE1000Protocol.RegTctl,
                ManagedE1000Protocol.TransmitEnable |
                ManagedE1000Protocol.TransmitPadShort |
                ManagedE1000Protocol.TransmitCollisionThreshold |
                ManagedE1000Protocol.TransmitCollisionDistance)) return false;
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_STATUS=0x"u8,
                                      status);
    }

    private bool SubmitProofFrame()
    {
        if (_txBuffers == null || _txRing == null ||
            !ManagedE1000Protocol.TryBuildProofFrame(_txFrame, _mac) ||
            !_txBuffers.TryWrite(0, _txFrame)) return false;
        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        descriptor.Clear();
        Write64(descriptor, _txBuffers.BusAddress);
        descriptor[8] = (byte)_txFrame.Length;
        descriptor[9] = (byte)(_txFrame.Length >> 8);
        descriptor[11] = (byte)ManagedE1000Protocol.TxCommandEopIfcsRs;
        descriptor[12] = 0;
        _lastSubmittedTxIndex = _txIndex;
        if (!_txRing.TryWrite(_txIndex * ManagedE1000Protocol.DescriptorSize,
                              descriptor) ||
            !WriteRegister(ManagedE1000Protocol.RegTxDescTail, 1)) return false;
        return KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_SUBMITTED_INDEX=0x"u8,
                                      _txIndex) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_LENGTH=0x"u8,
                                      (ulong)_txFrame.Length) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_TYPE=0x"u8,
                                      ManagedE1000Protocol.ProofEtherType) &&
               KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_SUBMITTED\r\n"u8);
    }

    private bool PollTxCompletion()
    {
        if (_txRing == null) return false;
        if (!PollTxCompletionCore(_txIndex))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_TIMEOUT\r\n"u8);
            return false;
        }
        if (!ManagedE1000Protocol.TryAdvanceRing(
                _txIndex, ManagedE1000Protocol.RingCount, out _txIndex))
            return false;
        return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_TX_COMPLETED\r\n"u8);
    }

    internal ManagedE1000AsyncTxSubmitResult TrySubmitFrame(
        byte[]? frame, int length, out ulong requestId)
    {
        requestId = 0;
        if (_state != ManagedE1000DriverState.Running || !_phase90AsyncTxRequested ||
            !_interruptSubscribed || _txRing == null || _txBuffers == null)
            return ManagedE1000AsyncTxSubmitResult.Unavailable;
        if (frame == null || length <= 0 || length > frame.Length ||
            length > ManagedE1000Protocol.PacketBufferSize)
            return ManagedE1000AsyncTxSubmitResult.InvalidArgument;
        if (_asyncTxLedger.Outstanding >= AsyncTxCapacity ||
            _txIndex >= ManagedE1000Protocol.RingCount ||
            !_asyncTxLedger.IsFree(_txIndex))
            return ManagedE1000AsyncTxSubmitResult.Busy;

        uint index = _txIndex;
        ulong bufferOffset = (ulong)index * ManagedE1000Protocol.PacketBufferSize;
        if (bufferOffset > _txBuffers.ByteLength ||
            (ulong)length > _txBuffers.ByteLength - bufferOffset ||
            _txBuffers.BusAddress > ulong.MaxValue - bufferOffset ||
            !ManagedE1000Protocol.TryAdvanceRing(index,
                ManagedE1000Protocol.RingCount, out uint nextIndex))
            return ManagedE1000AsyncTxSubmitResult.InvalidArgument;

        if (_asyncTxLedger.TryPrepare(index, out requestId) !=
            ManagedE1000AsyncTxSubmitResult.Accepted)
            return ManagedE1000AsyncTxSubmitResult.Busy;

        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        if (!_txBuffers.TryWrite(bufferOffset, frame.AsSpan(0, length)) ||
            !ManagedE1000Protocol.TryBuildTxDescriptor(
                descriptor, _txBuffers.BusAddress + bufferOffset, (ushort)length) ||
            !_txRing.TryWrite((ulong)index * ManagedE1000Protocol.DescriptorSize,
                              descriptor))
        {
            _asyncTxLedger.RollbackPrepared(index);
            requestId = 0;
            return ManagedE1000AsyncTxSubmitResult.Unavailable;
        }

        _lastSubmittedTxIndex = index;
        if (!_asyncTxLedger.MarkHardwareOwned(index))
        {
            _asyncTxLedger.RollbackPrepared(index);
            requestId = 0;
            return ManagedE1000AsyncTxSubmitResult.Unavailable;
        }
        if (!WriteRegister(ManagedE1000Protocol.RegTxDescTail, nextIndex))
        {
            _asyncTxLedger.RollbackHardwareHandoff(index);
            requestId = 0;
            return ManagedE1000AsyncTxSubmitResult.Unavailable;
        }
        _txIndex = nextIndex;
        _asyncTxAccepted++;
        return ManagedE1000AsyncTxSubmitResult.Accepted;
    }

    internal ManagedE1000AsyncTxResult TryGetTransmitCompletion(
        ulong requestId)
    {
        if (_state != ManagedE1000DriverState.Running || !_phase90AsyncTxRequested)
            return ManagedE1000AsyncTxResult.InvalidOrStale;
        return _asyncTxLedger.Query(requestId);
    }

    internal ManagedE1000AsyncTxResult TryConsumeTransmitCompletion(ulong requestId)
    {
        if (_state != ManagedE1000DriverState.Running || !_phase90AsyncTxRequested)
            return ManagedE1000AsyncTxResult.InvalidOrStale;
        return _asyncTxLedger.Consume(requestId);
    }

    private bool TryDecodeAsyncTxRequest(
        ulong requestId, out uint index, out uint generation)
    {
        index = 0;
        generation = 0;
        return ManagedE1000Protocol.TryDecodeAsyncTxRequestId(requestId,
                   ManagedE1000Protocol.RingCount, out index, out generation,
                   out uint episode) && episode == _driverEpisode;
    }

    private bool TryCompleteAsyncTxFromTxdw(
        ulong eventSequence, out uint completed)
    {
        completed = 0;
        if (_state != ManagedE1000DriverState.Running ||
            eventSequence <= _asyncTxLastEventSequence) return false;
        _asyncTxLastEventSequence = eventSequence;
        _asyncTxTxdwEvents++;
        if (_asyncTxLedger.Outstanding == 0)
        {
            _asyncTxUnmatchedTxdw++;
            return true;
        }
        if (_txRing == null) return false;
        for (uint index = 0; index < ManagedE1000Protocol.RingCount; index++)
        {
            if (_asyncTxLedger.OwnershipAt(index) !=
                ManagedE1000AsyncTxOwnership.HardwareOwned)
                continue;
            ulong descriptorOffset = (ulong)index * ManagedE1000Protocol.DescriptorSize;
            if (!_txRing.TryRead8(descriptorOffset + 12, out byte status)) return false;
            if (!_asyncTxLedger.CompleteFromDescriptor(index, status)) continue;
            _asyncTxCompleted++;
            if ((status & ManagedE1000Protocol.TxStatusErrorMask) == 0)
                _asyncTxSuccessfulFrames++;
            completed++;
        }
        return true;
    }

    internal bool TryTransmitFrame(byte[] frame, int length) =>
        TryTransmitFrameCore(frame, length, false, out _, out _, out _);

    private bool TryTransmitFrameWithSyncProof(byte[] frame, int length,
        out Phase91SyncSnapshot syncBefore, out bool submissionPrepared,
        out bool proofArmed) =>
        TryTransmitFrameCore(frame, length, true, out syncBefore,
            out submissionPrepared, out proofArmed);

    private bool TryTransmitFrameCore(byte[] frame, int length,
        bool armSyncProof, out Phase91SyncSnapshot syncBefore,
        out bool submissionPrepared, out bool proofArmed)
    {
        syncBefore = default;
        submissionPrepared = false;
        proofArmed = false;
        if (_state != ManagedE1000DriverState.Running || _txRing == null ||
            _txBuffers == null || frame == null || length <= 0 ||
            length > frame.Length ||
            length > ManagedE1000Protocol.PacketBufferSize ||
            _txIndex >= ManagedE1000Protocol.RingCount ||
            (_phase90AsyncTxRequested &&
             !_asyncTxLedger.IsFree(_txIndex)))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_PRECONDITION_FAILED\r\n"u8);
            return false;
        }

        ulong bufferOffset = (ulong)_txIndex * ManagedE1000Protocol.PacketBufferSize;
        if (bufferOffset > _txBuffers.ByteLength ||
            (ulong)frame.Length > _txBuffers.ByteLength - bufferOffset ||
            _txBuffers.BusAddress > ulong.MaxValue - bufferOffset)
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_BOUNDS_FAILED\r\n"u8);
            return false;
        }
        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        ReadOnlySpan<byte> frameSpan = frame.AsSpan(0, length);
        if (!_txBuffers.TryWrite(bufferOffset, frameSpan))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_DMA_WRITE_FAILED\r\n"u8);
            return false;
        }
        if (!ManagedE1000Protocol.TryBuildTxDescriptor(
                descriptor, _txBuffers.BusAddress + bufferOffset,
                (ushort)length))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_DESCRIPTOR_BUILD_FAILED\r\n"u8);
            return false;
        }
        if (!_txRing.TryWrite(
                (ulong)_txIndex * ManagedE1000Protocol.DescriptorSize, descriptor))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_DESCRIPTOR_WRITE_FAILED\r\n"u8);
            return false;
        }
        if (!ManagedE1000Protocol.TryAdvanceRing(
                _txIndex, ManagedE1000Protocol.RingCount, out uint nextIndex))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_RING_ADVANCE_FAILED\r\n"u8);
            return false;
        }
        submissionPrepared = true;
        if (armSyncProof)
        {
            if (!TryArmSyncTxProof(out syncBefore)) return false;
            proofArmed = true;
        }
        _lastSubmittedTxIndex = _txIndex;
        if (!WriteRegister(ManagedE1000Protocol.RegTxDescTail, nextIndex))
        {
            if (proofArmed) SealSyncTxProof();
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_TAIL_WRITE_FAILED\r\n"u8);
            return false;
        }
        if (!PollTxCompletionCore(_txIndex))
        {
            if (proofArmed) SealSyncTxProof();
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_COMPLETION_TIMEOUT\r\n"u8);
            return false;
        }
        _txIndex = nextIndex;
        _synchronousTxDdPollSuccesses++;
        return true;
    }

    internal bool TrySubscribeInterrupt(ManagedInterruptDispatcher dispatcher)
    {
        if (!_phase80Requested || _state != ManagedE1000DriverState.Initialized ||
            _interruptSubscribed || dispatcher == null ||
            !dispatcher.TrySubscribeE1000(out _)) return false;
        _interruptDispatcher = dispatcher;
        _interruptSubscribed = true;
        return KernelLog.Write(
            "GXOS_NET10:PHASE80_E1000_MANAGED_BINDING_READY\r\n"u8);
    }

    internal bool TryUnsubscribeInterrupt(ManagedInterruptDispatcher dispatcher)
    {
        if (!_interruptSubscribed || dispatcher == null ||
            _interruptDispatcher != dispatcher || !dispatcher.TryUnsubscribeE1000())
            return false;
        _interruptSubscribed = false;
        _interruptDispatcher = null;
        return true;
    }

    internal bool TryHandleInterrupt(in GxManagedKernelInterruptEventV1 value)
    {
        bool modeValid = _phase80Requested && _interruptSubscribed &&
                         _state == ManagedE1000DriverState.Running;
        bool eventValid = ManagedE1000Protocol.TryValidateInterruptEvent(
            in value, _phase80LastSequence, out uint unrelated);
        if (!modeValid || !eventValid)
        {
            if (_phase91Requested)
            {
                uint rejectMask = 0;
                if (!_phase80Requested) rejectMask |= 1U << 0;
                if (!_interruptSubscribed) rejectMask |= 1U << 1;
                if (_state != ManagedE1000DriverState.Running) rejectMask |= 1U << 2;
                if (value.Size != GxManagedKernelInterruptEventV1.ExpectedSize) rejectMask |= 1U << 3;
                if (value.AbiVersion != GxManagedKernelInterruptEventV1.AbiVersionCurrent) rejectMask |= 1U << 4;
                if (value.EventType != ManagedE1000Protocol.InterruptEventType) rejectMask |= 1U << 5;
                if (value.DeviceKind != ManagedE1000Protocol.PciDeviceKind) rejectMask |= 1U << 6;
                if (value.DeviceId != ManagedE1000Protocol.PciOwnerId) rejectMask |= 1U << 7;
                if (value.Flags != GxManagedKernelInterruptEventV1.EventFlagHardwareCapture) rejectMask |= 1U << 8;
                if (value.PayloadLength != 1) rejectMask |= 1U << 9;
                if (value.Reserved0 != 0) rejectMask |= 1U << 10;
                if (value.Timestamp != 0) rejectMask |= 1U << 11;
                if (value.Sequence == 0) rejectMask |= 1U << 12;
                if (value.Sequence != 0 && value.Sequence <= _phase80LastSequence) rejectMask |= 1U << 13;
                if (value.PayloadByte != (byte)value.Status) rejectMask |= 1U << 14;
                if (!ManagedE1000Protocol.TryClassifyInterruptCause(
                        value.Status, out _)) rejectMask |= 1U << 15;
                _phase91LastInterruptRejectMask = rejectMask;
            }
            return false;
        }
        bool hasTxCause = (value.Status &
            ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback) != 0;
        bool hasRxCause = (value.Status &
            ManagedE1000Protocol.InterruptCauseRxTimer) != 0;
        if (hasTxCause && _phase90AsyncTxRequested &&
            !TryCompleteAsyncTxFromTxdw(value.Sequence, out _))
        {
            if (_phase91Requested) _phase91LastInterruptRejectMask = 1U << 16;
            return false;
        }
        if (hasTxCause && !_phase90AsyncTxRequested &&
            (_txRing == null || !_txRing.TryRead8(
                (ulong)_lastSubmittedTxIndex * ManagedE1000Protocol.DescriptorSize + 12,
                out byte txStatus) ||
            (txStatus & ManagedE1000Protocol.TxStatusDone) == 0))
        {
            if (_phase91Requested) _phase91LastInterruptRejectMask = 1U << 17;
            return false;
        }
        if (hasRxCause && (!_phase83Requested ||
                           !TryHandleManagedEthernetRxCause(value)))
        {
            if (_phase91Requested) _phase91LastInterruptRejectMask = 1U << 18;
            return false;
        }
        _phase80LastSequence = value.Sequence;
        _phase80LastCause = value.Status;
        _phase80LastUnrelatedCause = unrelated;
        _phase80DeliveryCount++;
        if (_phase87IcmpRequested && hasTxCause) _phase87IcmpTxDwCount++;
        if (_phase89UdpRequested && hasTxCause) _phase89UdpTxDwCount++;
        if (_phase89UdpRequested && hasRxCause) _phase89UdpRxEventCount++;
        if (_phase85ArpRequested)
        {
            if (hasTxCause) _phase85TxDwCount++;
            if (hasRxCause) _phase85RxEventCount++;
        }
        if (hasTxCause && _syncTxProof.IsArmed)
            _syncTxProof.Observe(value.Sequence, value.Status);
        return true;
    }

    private bool TryArmSyncTxProof(out Phase91SyncSnapshot snapshot)
    {
        snapshot = default;
        ManagedInterruptDispatcher? dispatcher = _interruptDispatcher;
        if (dispatcher == null || dispatcher.E1000SubscriptionId == 0 ||
            !dispatcher.TryQueryStats(out GxManagedKernelInterruptStatsV1 stats) ||
            stats.SubscriptionActive == 0 || stats.HardwareEnabled == 0 ||
            stats.NextSequence == 0 || !_syncTxProof.Arm(stats.NextSequence))
            return false;

        /* Keep the successful path to the single stats read needed for the
           sequence boundary. Full MMIO and diagnostic snapshots are captured
           only when the proof fails. */
        snapshot.Phase80Deliveries = _phase80DeliveryCount;
        snapshot.SyncDdPollSuccesses = _synchronousTxDdPollSuccesses;
        snapshot.AsyncTxdwEvents = _asyncTxTxdwEvents;
        snapshot.AsyncUnmatchedTxdw = _asyncTxUnmatchedTxdw;
        snapshot.SyncTxNotificationCount = _syncTxProof.TxdwNotificationCount;
        snapshot.LastEventCause = _phase80LastCause;
        snapshot.DriverEpisode = _driverEpisode;
        snapshot.RouteToken = dispatcher.E1000SubscriptionId;
        snapshot.DispatcherSequence = dispatcher.E1000LastSequence;
        snapshot.StatsAvailable = true;
        snapshot.InterruptStats = stats;
        snapshot.IcrReadCount = stats.IrqEntryCount;
        snapshot.NativeSourceEvents = stats.SerialIsrCount;
        snapshot.NextEventSequence = stats.NextSequence;
        snapshot.RouteSubscriptionActive = stats.SubscriptionActive;
        snapshot.RouteHardwareEnabled = stats.HardwareEnabled;
        return true;
    }

    private bool TryHandleManagedEthernetRxCause(
        in GxManagedKernelInterruptEventV1 value)
    {
        if (_rxRing == null || _rxBuffers == null || _ethernet == null)
            return false;

        uint descriptorIndex = _rxIndex;
        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        if (!_rxRing.TryRead(
                (ulong)descriptorIndex * ManagedE1000Protocol.DescriptorSize,
                descriptor))
            return false;

        byte status = descriptor[12];
        byte errors = descriptor[13];
        ushort length = (ushort)(descriptor[8] | (descriptor[9] << 8));
        if ((status & ManagedE1000Protocol.RxStatusDone) == 0)
        {
            /* Consume the typed event sequence, but do not inspect or invent a
               frame when the expected descriptor has no DD completion. */
            return KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE84_RX_EVENT_WITHOUT_DD=0x"u8,
                value.Sequence);
        }

        _phase83RxDescriptorCount++;
        ManagedEthernetReceiveResult receiveResult;
        ManagedNetworkDispatchResult networkResult =
            ManagedNetworkDispatchResult.Invalid;
        ManagedArpHandleResult arpResult = ManagedArpHandleResult.Invalid;
        ManagedArpReceiveMetadata arpMetadata = default;
        if ((status & ManagedE1000Protocol.RxStatusEop) == 0 || errors != 0 ||
            length < ManagedE1000Protocol.MinimumRxEthernetHeaderLength ||
            length > ManagedE1000Protocol.PacketBufferSize ||
            !ManagedE1000Protocol.TryReadRxDescriptor(
                descriptor, descriptorIndex, ManagedE1000Protocol.RingCount,
                ManagedE1000Protocol.PacketBufferSize, out _, out _, out _))
        {
            _ethernet.RecordDriverRejectedFrame();
            receiveResult = ManagedEthernetReceiveResult.MalformedFrame;
        }
        else if (!_rxBuffers.TryRead(
                     (ulong)descriptorIndex * ManagedE1000Protocol.PacketBufferSize,
                     _ethernet.DriverReceiveBuffer.Slice(0, length)))
        {
            receiveResult = ManagedEthernetReceiveResult.DriverError;
        }
        else
        {
            receiveResult = _ethernet.ProcessDriverReceivedFrame(
                length, out networkResult, out arpResult, out arpMetadata);
        }

        /* Ethernet dispatch is synchronous.  Recycle every DD descriptor only
           after that call returns, regardless of protocol or consumer result. */
        if (!RecycleRxDescriptor(descriptorIndex)) return false;
        uint ordinal = _phase83RxDeliveryCount + 1U;
        _phase83LastDescriptorIndex = descriptorIndex;
        _phase83LastDescriptorStatus = status;
        _phase83LastDescriptorErrors = errors;
        _phase83LastFrameLength = length;
        if (_phase86Ipv4Requested)
            return RecordPhase86Ipv4Delivery(
                receiveResult, networkResult, descriptorIndex, status, errors,
                length, value.Sequence);
        if (_phase87IcmpRequested)
            return RecordPhase87IcmpDelivery(
                receiveResult, networkResult, descriptorIndex, status, errors,
                length, value.Sequence);
        if (_phase89UdpRequested)
            return RecordPhase89UdpDelivery(
                receiveResult, networkResult, descriptorIndex, status, errors,
                length, value.Sequence);
        if (_phase85ArpRequested)
            return RecordPhase85ArpDelivery(
                receiveResult, networkResult, arpResult, in arpMetadata,
                descriptorIndex, status, errors, length, value.Sequence);
        bool testConsumerAccepted =
            receiveResult == ManagedEthernetReceiveResult.Consumed &&
            _ethernet.Phase84TestConsumerDeliveryCount == ordinal &&
            ordinal <= _phase83DescriptorIndices.Length;
        if (testConsumerAccepted)
        {
            _phase83DescriptorIndices[ordinal - 1U] = descriptorIndex;
            _phase83RxDeliveryCount = ordinal;
        }
        return (!testConsumerAccepted ||
                (KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE83_RX_ACCEPTED=0x"u8, ordinal) &&
                 KernelLog.Write(
                    "GXOS_NET10:PHASE83_RX_FRAME_FIELDS_VALID=1\r\n"u8) &&
                 KernelLog.Write(
                    "GXOS_NET10:PHASE84_NORMAL_ETHERNET_PATH=1\r\n"u8))) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_EVENT_SEQUENCE=0x"u8,
                   value.Sequence) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_CAUSE=0x"u8, value.Status) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_DESCRIPTOR_INDEX=0x"u8,
                   descriptorIndex) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_DESCRIPTOR_STATUS=0x"u8,
                   status) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_DESCRIPTOR_ERRORS=0x"u8,
                   errors) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_FRAME_LENGTH=0x"u8, length) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE83_RX_DESCRIPTOR_RECYCLED=1\r\n"u8);
    }

    private bool RecordPhase86Ipv4Delivery(
        ManagedEthernetReceiveResult receiveResult,
        ManagedNetworkDispatchResult networkResult,
        uint descriptorIndex, byte status, byte errors, ushort length,
        ulong eventSequence)
    {
        uint ordinal = _phase86Ipv4DeliveryCount + 1U;
        if (_ethernet == null || ordinal > 4U ||
            receiveResult != ManagedEthernetReceiveResult.Consumed ||
            networkResult != ManagedNetworkDispatchResult.Ignored ||
            (status & (ManagedE1000Protocol.RxStatusDone |
                       ManagedE1000Protocol.RxStatusEop)) !=
                      (ManagedE1000Protocol.RxStatusDone |
                       ManagedE1000Protocol.RxStatusEop) ||
            errors != 0 || length != 60 || descriptorIndex != ordinal - 1U ||
            _ethernet.LastReceivedEtherType != ManagedIpv4Protocol.EtherType ||
            _ethernet.Phase86Ipv4ParsedFrameCount != ordinal)
            return false;

        ManagedIpv4Phase86Metadata metadata =
            _ethernet.Phase86Ipv4LastMetadata;
        bool expectedLocal = ordinal <= 3U;
        uint expectedSource = 0x0A0F0001U + ordinal;
        uint expectedDestination = expectedLocal ? 0x0A0F0001U : 0x0A0F0063U;
        if (metadata.Sequence != ordinal || metadata.IsLocal != expectedLocal ||
            metadata.SourceAddress != expectedSource ||
            metadata.DestinationAddress != expectedDestination ||
            metadata.Identification != (ushort)(0x8600U + ordinal) ||
            metadata.TotalLength != 24 || metadata.HeaderLength != 20 ||
            metadata.Ttl != 63 || metadata.Protocol != 253 ||
            metadata.PayloadLength != 4 ||
            _ethernet.Phase86Ipv4LocalDeliveryCount !=
                (expectedLocal ? ordinal : 3U) ||
            _ethernet.Phase86Ipv4NotForUsCount !=
                (expectedLocal ? 0U : 1U) ||
            _ethernet.Phase86UnsupportedProtocolCount !=
                (expectedLocal ? ordinal : 3U))
            return false;

        _phase86Ipv4DeliveryCount = ordinal;
        return KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_DELIVERY=0x"u8, ordinal) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_RX_EVENT_SEQUENCE=0x"u8,
                   eventSequence) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_RX_DESCRIPTOR_INDEX=0x"u8,
                   descriptorIndex) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_ETHERTYPE=0x"u8,
                   _ethernet.LastReceivedEtherType) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_SOURCE=0x"u8,
                   metadata.SourceAddress) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_DESTINATION=0x"u8,
                   metadata.DestinationAddress) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_IDENTIFICATION=0x"u8,
                   metadata.Identification) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_TOTAL_LENGTH=0x"u8,
                   metadata.TotalLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_HEADER_LENGTH=0x"u8,
                   metadata.HeaderLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_TTL=0x"u8, metadata.Ttl) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_PROTOCOL=0x"u8,
                   metadata.Protocol) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_PAYLOAD_LENGTH=0x"u8,
                   metadata.PayloadLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE86_IPV4_SENTINEL_SEQUENCE=0x"u8,
                   metadata.Sequence) &&
               KernelLog.Write(
                   metadata.IsLocal
                       ? "GXOS_NET10:PHASE86_IPV4_LOCAL_DELIVERY=1\r\n"u8
                       : "GXOS_NET10:PHASE86_IPV4_NOT_FOR_US=1\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE86_IPV4_CHECKSUM_VALID=1\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE86_RX_DESCRIPTOR_RECYCLED=1\r\n"u8);
    }

    private bool RecordPhase85ArpDelivery(
        ManagedEthernetReceiveResult receiveResult,
        ManagedNetworkDispatchResult networkResult,
        ManagedArpHandleResult arpResult,
        in ManagedArpReceiveMetadata metadata,
        uint descriptorIndex, byte status, byte errors, ushort length,
        ulong eventSequence)
    {
        uint ordinal = _phase85ArpDeliveryCount + 1U;
        if (ordinal > 4U || receiveResult != ManagedEthernetReceiveResult.Consumed ||
            (status & (ManagedE1000Protocol.RxStatusDone |
                       ManagedE1000Protocol.RxStatusEop)) !=
                      (ManagedE1000Protocol.RxStatusDone |
                       ManagedE1000Protocol.RxStatusEop) ||
            errors != 0 || length != 60 || descriptorIndex != ordinal - 1U ||
            metadata.EtherType != ManagedEthernetProtocol.ArpEtherType ||
            metadata.PayloadLength != 46 || metadata.HardwareType != 1 ||
            metadata.ProtocolType != 0x0800 || metadata.HardwareLength != 6 ||
            metadata.ProtocolLength != 4 || metadata.Operation != 1 ||
            metadata.EthernetDestination != 0xFFFFFFFFFFFFUL ||
            metadata.EthernetSource != 0x021500000000UL + ordinal ||
            metadata.SenderMac != metadata.EthernetSource ||
            metadata.SenderIpv4 != 0x0A0F0001U + ordinal ||
            metadata.TargetMac != 0 ||
            metadata.TargetIpv4 !=
                (ordinal <= 3U ? 0x0A0F0001U : 0x0A0F0063U) ||
            (ordinal <= 3U &&
             (arpResult != ManagedArpHandleResult.ResponderReplySent ||
              networkResult != ManagedNetworkDispatchResult.ArpResponderReplySent)) ||
            (ordinal == 4U &&
             (arpResult != ManagedArpHandleResult.RequestNotForUs ||
              networkResult != ManagedNetworkDispatchResult.Ignored)))
            return false;

        _phase85ArpDeliveryCount = ordinal;
        if (ordinal <= 3U) _phase85ReplyTxCount++;
        bool logged =
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_DELIVERY=0x"u8, ordinal) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_EVENT_SEQUENCE=0x"u8, eventSequence) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_DESCRIPTOR_INDEX=0x"u8,
                descriptorIndex) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ETHERNET_DESTINATION=0x"u8,
                metadata.EthernetDestination) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ETHERNET_SOURCE=0x"u8,
                metadata.EthernetSource) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ETHERNET_ETHERTYPE=0x"u8,
                metadata.EtherType) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_PAYLOAD_LENGTH=0x"u8,
                metadata.PayloadLength) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_HARDWARE_TYPE=0x"u8,
                metadata.HardwareType) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_PROTOCOL_TYPE=0x"u8,
                metadata.ProtocolType) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_HLEN=0x"u8,
                metadata.HardwareLength) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_PLEN=0x"u8,
                metadata.ProtocolLength) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_OPCODE=0x"u8,
                metadata.Operation) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_SENDER_MAC=0x"u8,
                metadata.SenderMac) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_SENDER_IPV4=0x"u8,
                metadata.SenderIpv4) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_TARGET_MAC=0x"u8,
                metadata.TargetMac) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_TARGET_IPV4=0x"u8,
                metadata.TargetIpv4) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_DESCRIPTOR_STATUS=0x"u8,
                status) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_DESCRIPTOR_ERRORS=0x"u8,
                errors) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_FRAME_LENGTH=0x"u8, length) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE85_RX_DESCRIPTOR_RECYCLED=1\r\n"u8);
        if (!logged) return false;
        return ordinal <= 3U
            ? KernelLog.Write(
                "GXOS_NET10:PHASE85_ARP_REQUEST_FOR_LOCAL=1\r\n"u8) &&
              KernelLog.Write(
                "GXOS_NET10:PHASE85_ARP_REPLY_TX_COMPLETED=1\r\n"u8)
            : KernelLog.Write(
                "GXOS_NET10:PHASE85_ARP_REQUEST_NOT_FOR_US=1\r\n"u8);
    }

    internal bool TryRunPhase80Proof()
    {
        if (!_phase80Requested || !_interruptSubscribed ||
            _state != ManagedE1000DriverState.Running || _phase80DeliveryCount != 0)
        {
            if (_phase91Requested)
                KernelLog.Write(
                    "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_FAILURE=PRECONDITION\r\n"u8);
            return false;
        }

        if (!TryWaitForPhase80Delivery(out uint delivered, out uint rejected) ||
            delivered != 1 || rejected != 0)
        {
            if (_phase91Requested)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_INITIAL_DELIVERED=0x"u8,
                    delivered);
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_INITIAL_REJECTED=0x"u8,
                    rejected);
                KernelLog.Write(
                    "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_FAILURE=INITIAL_DELIVERY\r\n"u8);
            }
            return false;
        }

        for (uint repeat = 0; repeat != 3; ++repeat)
        {
            if (!ManagedE1000Protocol.TryBuildPhase80Frame(
                    _phase80Frame, _mac, repeat + 1))
            {
                if (_phase91Requested)
                    KernelLog.Write(
                        "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_FAILURE=REPEAT_BUILD\r\n"u8);
                return false;
            }
            if (!TryTransmitFrame(_phase80Frame, _phase80Frame.Length))
            {
                if (_phase91Requested)
                    KernelLog.Write(
                        "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_FAILURE=REPEAT_TX\r\n"u8);
                return false;
            }
            if (!TryWaitForPhase80Delivery(out delivered, out rejected) ||
                delivered != 1 || rejected != 0)
            {
                if (_phase91Requested)
                {
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_REPEAT_DELIVERED=0x"u8,
                        delivered);
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_REPEAT_REJECTED=0x"u8,
                        rejected);
                    KernelLog.WriteHexLine(
                        "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_REPEAT=0x"u8,
                        repeat + 1);
                    KernelLog.Write(
                        "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_FAILURE=REPEAT_DELIVERY\r\n"u8);
                }
                return false;
            }
        }
        if (_phase80DeliveryCount != 4 || _phase80LastSequence == 0 ||
            !ManagedE1000Protocol.TryClassifyInterruptCause(
                _phase80LastCause, out _phase80LastUnrelatedCause))
        {
            if (_phase91Requested)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_DELIVERIES=0x"u8,
                    _phase80DeliveryCount);
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_LAST_SEQUENCE=0x"u8,
                    _phase80LastSequence);
                KernelLog.Write(
                    "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_FAILURE=FINAL_RECONCILIATION\r\n"u8);
            }
            return false;
        }
        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE80_E1000_MANAGED_DELIVERY_COUNT=0x"u8,
                _phase80DeliveryCount) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE80_E1000_MANAGED_SEQUENCE=0x"u8,
                _phase80LastSequence) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE80_E1000_MANAGED_CAUSE=0x"u8,
                _phase80LastCause) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE80_E1000_MANAGED_UNRELATED_CAUSE=0x"u8,
                _phase80LastUnrelatedCause) ||
            !KernelLog.Write("GXOS_NET10:PHASE80_E1000_TX_DESCRIPTOR_COMPLETE=1\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:PHASE80_E1000_REPEATS=3\r\n"u8) ||
            (_phase91Requested && !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_COMPLETE=1\r\n"u8)))
            return false;
        _phase80Passed = true;
        return KernelLog.Write("GXOS_NET10:PHASE80_E1000_MANAGED_DELIVERY=4\r\n"u8);
    }

    internal bool TryRunPhase90AsyncTxProof()
    {
        if (!_phase90AsyncTxRequested || !_phase80Requested ||
            !_interruptSubscribed || _state != ManagedE1000DriverState.Running ||
            _phase80DeliveryCount != 4 || _asyncTxLedger.Outstanding != 0 ||
            _txRing == null || _mmio == null) return false;

        Span<ulong> initialRequests = stackalloc ulong[(int)AsyncTxCapacity];
        Span<uint> initialIndices = stackalloc uint[(int)AsyncTxCapacity];
        uint completionBase = _asyncTxCompleted;
        uint syncDdPollBase = _synchronousTxDdPollSuccesses;
        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE90_TX_CAPACITY=0x"u8, AsyncTxCapacity) ||
            !KernelLog.Write("GXOS_NET10:PHASE90_ASYNC_TX_READY=1\r\n"u8))
            return false;

        for (uint request = 0; request != AsyncTxCapacity; ++request)
        {
            if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                    _phase80Frame, _mac, request + 1) ||
                TrySubmitFrame(_phase80Frame, _phase80Frame.Length,
                    out ulong requestId) != ManagedE1000AsyncTxSubmitResult.Accepted ||
                TryGetTransmitCompletion(requestId) !=
                    ManagedE1000AsyncTxResult.Pending ||
                !TryDecodeAsyncTxRequest(requestId, out uint index,
                    out uint generation)) return false;
            for (uint prior = 0; prior != request; ++prior)
                if (initialRequests[(int)prior] == requestId) return false;
            initialRequests[(int)request] = requestId;
            initialIndices[(int)request] = index;
            if (request == 0) _phase90InitialRequestId = requestId;
            if (!KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE90_SUBMITTED_ID=0x"u8, requestId) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE90_SUBMITTED_INDEX=0x"u8, index) ||
                !KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE90_SUBMITTED_GENERATION=0x"u8, generation))
                return false;
        }

        if (TrySubmitFrame(_phase80Frame, _phase80Frame.Length, out _) !=
                ManagedE1000AsyncTxSubmitResult.Busy ||
            _asyncTxLedger.Outstanding != AsyncTxCapacity)
            return false;
        if (!KernelLog.Write(
                "GXOS_NET10:PHASE90_PENDING_AND_CAPACITY_PASS=1\r\n"u8))
            return false;

        if (!TryDrainPhase90Completions(AsyncTxCapacity,
                completionBase)) return false;
        for (uint request = 0; request != AsyncTxCapacity; ++request)
        {
            ulong requestId = initialRequests[(int)request];
            if (TryGetTransmitCompletion(requestId) !=
                    ManagedE1000AsyncTxResult.Success ||
                TryGetTransmitCompletion(requestId) !=
                    ManagedE1000AsyncTxResult.Success ||
                TryConsumeTransmitCompletion(requestId) !=
                    ManagedE1000AsyncTxResult.Success ||
                TryConsumeTransmitCompletion(requestId) !=
                    ManagedE1000AsyncTxResult.InvalidOrStale)
                return false;
        }

        // Walk the ring once to return to the first async descriptor, then
        // prove that its old generation cannot observe the new request.
        uint expectedReusedIndex = initialIndices[0];
        for (uint sequence = AsyncTxCapacity + 1;
             sequence <= ManagedE1000Protocol.RingCount;
             ++sequence)
        {
            if (!TrySubmitAndCompletePhase90Frame(sequence, out _)) return false;
        }
        uint reusedCompletionBase = _asyncTxCompleted;
        if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                _phase80Frame, _mac, ManagedE1000Protocol.RingCount + 1) ||
            TrySubmitFrame(_phase80Frame, _phase80Frame.Length,
                out ulong reusedRequest) != ManagedE1000AsyncTxSubmitResult.Accepted ||
            !TryDecodeAsyncTxRequest(reusedRequest, out uint reusedIndex,
                out uint reusedGeneration) || reusedIndex != expectedReusedIndex ||
            reusedGeneration == 1 ||
            TryGetTransmitCompletion(initialRequests[0]) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            TryConsumeTransmitCompletion(initialRequests[0]) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            !TryDrainPhase90Completions(1, reusedCompletionBase) ||
            TryGetTransmitCompletion(reusedRequest) !=
                ManagedE1000AsyncTxResult.Success ||
            TryConsumeTransmitCompletion(reusedRequest) !=
                ManagedE1000AsyncTxResult.Success ||
            TryGetTransmitCompletion(initialRequests[0]) !=
                ManagedE1000AsyncTxResult.InvalidOrStale)
            return false;

        if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                _phase80Frame, _mac, ManagedE1000Protocol.RingCount + 2))
            return false;
        bool phase90SyncTxSucceeded = TryTransmitFrameWithSyncProof(
            _phase80Frame, _phase80Frame.Length,
            out Phase91SyncSnapshot syncBefore, out bool phase90Prepared,
            out bool phase90ProofArmed);
        if (!phase90Prepared || !phase90ProofArmed || !phase90SyncTxSucceeded)
            return false;
        uint syncEventBase = syncBefore.Phase80Deliveries;
        for (uint poll = 0; poll != Phase90CompletionPollLimit &&
             !_syncTxProof.IsSatisfied; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out _, out uint rejected) || rejected != 0)
                return false;
        }
        bool syncDdCompleted = _synchronousTxDdPollSuccesses ==
                               syncDdPollBase + 1;
        if (!IsSyncTxProofAccepted(syncDdCompleted, true, 0) ||
            _asyncTxCompleted !=
                completionBase + ManagedE1000Protocol.RingCount + 1 ||
            _asyncTxUnmatchedTxdw == 0)
            return false;
        SealSyncTxProof();

        bool passed = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE90_REQUESTS_COMPLETED=0x"u8,
                _asyncTxCompleted - completionBase) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE90_TXDW_EVENTS=0x"u8,
                _phase80DeliveryCount - 4) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE90_UNMATCHED_TXDW=0x"u8,
                _asyncTxUnmatchedTxdw) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE90_SYNC_TX_DD_POLL_FRAMES=0x"u8,
                _synchronousTxDdPollSuccesses - syncDdPollBase) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE90_REUSED_DESCRIPTOR=0x"u8, reusedIndex) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE90_REUSED_GENERATION=0x"u8, reusedGeneration) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE90_SYNC_TX_REGRESSION=PASS\r\n"u8) &&
            KernelLog.Write(
                "PHASE90_ASYNC_TX_COMPLETION_PASS=1\r\n"u8);
        _phase90AsyncTxPassed = passed;
        return passed;
    }

    internal bool TryRunPhase92FirstEpisode()
    {
        if (!_phase92Requested || !_phase90AsyncTxPassed ||
            !_phase91Requested || _state != ManagedE1000DriverState.Running ||
            _asyncTxLedger.Outstanding != 0 || _txRing == null || _mmio == null)
            return false;
        _phase92FirstEpisodePassed = TryRunPhase92EnduranceEpisode(
            1, 0x92010000U, 0);
        return _phase92FirstEpisodePassed &&
            KernelLog.Write("GXOS_NET10:PHASE92_EPISODE1_RECONCILED=1\r\n"u8);
    }

    private bool TryRunPhase92EnduranceEpisode(uint episodeNumber,
        uint firstSequence, uint completedBeforeEpisode)
    {
        if (!_phase92Requested || !_phase90AsyncTxRequested ||
            !_phase91Requested || _state != ManagedE1000DriverState.Running ||
            _asyncTxLedger.Outstanding != 0 || _txRing == null || _mmio == null ||
            _txBuffers == null) return false;

        uint acceptedBase = _asyncTxAccepted;
        uint completedBase = _asyncTxCompleted;
        uint successfulBase = _asyncTxSuccessfulFrames;
        uint exposedBase = _asyncTxLedger.ExposedCompletions;
        uint consumedBase = _asyncTxLedger.ConsumedCompletions;
        uint busyBase = _phase92BusyProbes;
        uint wrapBase = _phase92DescriptorWraps;
        uint maxOutstanding = 0;
        uint lastIndex = uint.MaxValue;
        Span<ulong> lastRequestByDescriptor =
            stackalloc ulong[(int)ManagedE1000Protocol.RingCount];
        lastRequestByDescriptor.Clear();
        Span<ulong> batchIds = stackalloc ulong[(int)AsyncTxCapacity];
        Span<uint> batchIndices = stackalloc uint[(int)AsyncTxCapacity];

        uint initialTail = 0;
        if (!_mmio.TryRead32(ManagedE1000Protocol.RegTxDescTail,
                out initialTail) ||
            TrySubmitFrame(null, 0, out ulong invalidId) !=
                ManagedE1000AsyncTxSubmitResult.InvalidArgument || invalidId != 0 ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegTxDescTail,
                out uint tailAfterInvalid) || tailAfterInvalid != initialTail ||
            _asyncTxLedger.Outstanding != 0 || _asyncTxAccepted != acceptedBase)
            return false;

        for (uint batch = 0; batch != 32; ++batch)
        {
            uint completionBase = _asyncTxCompleted;
            uint acceptedBeforeBatch = _asyncTxAccepted;
            for (uint request = 0; request != AsyncTxCapacity; ++request)
            {
                uint ordinal = batch * AsyncTxCapacity + request + 1;
                if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                        _phase80Frame, _mac, firstSequence + ordinal) ||
                    TrySubmitFrame(_phase80Frame, _phase80Frame.Length,
                        out ulong requestId) !=
                        ManagedE1000AsyncTxSubmitResult.Accepted ||
                    TryGetTransmitCompletion(requestId) !=
                        ManagedE1000AsyncTxResult.Pending ||
                    !TryDecodeAsyncTxRequest(requestId, out uint index,
                        out uint generation) ||
                    !ManagedE1000Protocol.TryDecodeAsyncTxRequestId(
                        requestId, ManagedE1000Protocol.RingCount, out _,
                        out _, out uint requestEpisode) ||
                    requestEpisode != _driverEpisode || generation == 0)
                    return false;

                for (uint prior = 0; prior != request; ++prior)
                    if (batchIds[(int)prior] == requestId) return false;
                batchIds[(int)request] = requestId;
                batchIndices[(int)request] = index;
                if (index < lastIndex) _phase92DescriptorWraps++;
                lastIndex = index;
                if (_phase92OldEpisodeRequestId == 0 && episodeNumber == 1 &&
                    ordinal == 1)
                    _phase92OldEpisodeRequestId = requestId;

                ulong previousId = lastRequestByDescriptor[(int)index];
                if (previousId != 0)
                {
                    uint outstandingBeforeStale = _asyncTxLedger.Outstanding;
                    uint exposedBeforeStale = _asyncTxLedger.ExposedCompletions;
                    uint consumedBeforeStale = _asyncTxLedger.ConsumedCompletions;
                    uint acceptedBeforeStale = _asyncTxAccepted;
                    if (!_txRing.TryRead8((ulong)index *
                            ManagedE1000Protocol.DescriptorSize + 12,
                            out byte statusBeforeStale) ||
                        TryGetTransmitCompletion(previousId) !=
                            ManagedE1000AsyncTxResult.InvalidOrStale ||
                        TryConsumeTransmitCompletion(previousId) !=
                            ManagedE1000AsyncTxResult.InvalidOrStale ||
                        _asyncTxLedger.Outstanding != outstandingBeforeStale ||
                        _asyncTxLedger.ExposedCompletions != exposedBeforeStale ||
                        _asyncTxLedger.ConsumedCompletions != consumedBeforeStale ||
                        _asyncTxAccepted != acceptedBeforeStale ||
                        _asyncTxLedger.OwnershipAt(index) !=
                            ManagedE1000AsyncTxOwnership.HardwareOwned ||
                        !_txRing.TryRead8((ulong)index *
                            ManagedE1000Protocol.DescriptorSize + 12,
                            out byte statusAfterStale) ||
                        (statusAfterStale != statusBeforeStale &&
                         statusAfterStale != (byte)(statusBeforeStale |
                             ManagedE1000Protocol.TxStatusDone)))
                        return false;
                }
                lastRequestByDescriptor[(int)index] = requestId;
                if (_asyncTxLedger.Outstanding > AsyncTxCapacity) return false;
                if (_asyncTxLedger.Outstanding > maxOutstanding)
                    maxOutstanding = _asyncTxLedger.Outstanding;

                if (episodeNumber == 2 && ordinal == 1)
                {
                    uint outstandingBeforeOldId = _asyncTxLedger.Outstanding;
                    uint acceptedBeforeOldId = _asyncTxAccepted;
                    uint completedBeforeOldId = _asyncTxCompleted;
                    uint exposedBeforeOldId = _asyncTxLedger.ExposedCompletions;
                    uint consumedBeforeOldId = _asyncTxLedger.ConsumedCompletions;
                    uint txIndexBeforeOldId = _txIndex;
                    if (_phase92OldEpisodeRequestId == 0 ||
                        !_txRing.TryRead8((ulong)index *
                            ManagedE1000Protocol.DescriptorSize + 12,
                            out byte statusBeforeOldId) ||
                        TryGetTransmitCompletion(_phase92OldEpisodeRequestId) !=
                            ManagedE1000AsyncTxResult.InvalidOrStale ||
                        TryConsumeTransmitCompletion(_phase92OldEpisodeRequestId) !=
                            ManagedE1000AsyncTxResult.InvalidOrStale ||
                        !_txRing.TryRead8((ulong)index *
                            ManagedE1000Protocol.DescriptorSize + 12,
                            out byte statusAfterOldId) ||
                        (statusAfterOldId != statusBeforeOldId &&
                         statusAfterOldId != (byte)(statusBeforeOldId |
                             ManagedE1000Protocol.TxStatusDone)) ||
                        _asyncTxLedger.Outstanding != outstandingBeforeOldId ||
                        _asyncTxAccepted != acceptedBeforeOldId ||
                        _asyncTxCompleted != completedBeforeOldId ||
                        _asyncTxLedger.ExposedCompletions != exposedBeforeOldId ||
                        _asyncTxLedger.ConsumedCompletions != consumedBeforeOldId ||
                        _txIndex != txIndexBeforeOldId ||
                        _asyncTxLedger.OwnershipAt(index) !=
                            ManagedE1000AsyncTxOwnership.HardwareOwned ||
                        !KernelLog.Write(
                            "GXOS_NET10:PHASE92_OLD_ID_REJECTED_WHILE_ACTIVE=1\r\n"u8))
                        return false;
                }
            }

            if (_asyncTxLedger.Outstanding != AsyncTxCapacity ||
                _asyncTxLedger.HardwareOwned != AsyncTxCapacity ||
                _asyncTxLedger.CompletedUnconsumed != 0 ||
                _asyncTxAccepted != acceptedBeforeBatch + AsyncTxCapacity)
                return false;

            uint txIndexBeforeBusy = _txIndex;
            uint acceptedBeforeBusy = _asyncTxAccepted;
            uint completedBeforeBusy = _asyncTxCompleted;
            uint exposedBeforeBusy = _asyncTxLedger.ExposedCompletions;
            uint consumedBeforeBusy = _asyncTxLedger.ConsumedCompletions;
            if (!_mmio.TryRead32(ManagedE1000Protocol.RegTxDescTail,
                    out uint tailBeforeBusy) ||
                TrySubmitFrame(_phase80Frame, _phase80Frame.Length,
                    out ulong rejectedId) != ManagedE1000AsyncTxSubmitResult.Busy ||
                rejectedId != 0 ||
                !_mmio.TryRead32(ManagedE1000Protocol.RegTxDescTail,
                    out uint tailAfterBusy) || tailAfterBusy != tailBeforeBusy ||
                _txIndex != txIndexBeforeBusy ||
                _asyncTxAccepted != acceptedBeforeBusy ||
                _asyncTxCompleted != completedBeforeBusy ||
                _asyncTxLedger.Outstanding != AsyncTxCapacity ||
                _asyncTxLedger.HardwareOwned != AsyncTxCapacity ||
                _asyncTxLedger.CompletedUnconsumed != 0 ||
                _asyncTxLedger.ExposedCompletions != exposedBeforeBusy ||
                _asyncTxLedger.ConsumedCompletions != consumedBeforeBusy)
                return false;
            _phase92BusyProbes++;
            for (uint request = 0; request != AsyncTxCapacity; ++request)
                if (TryGetTransmitCompletion(batchIds[(int)request]) !=
                        ManagedE1000AsyncTxResult.Pending ||
                    _asyncTxLedger.OwnershipAt(batchIndices[(int)request]) !=
                        ManagedE1000AsyncTxOwnership.HardwareOwned)
                    return false;

            if (!TryDrainPhase90Completions(AsyncTxCapacity,
                    completionBase) ||
                _asyncTxCompleted != completionBase + AsyncTxCapacity ||
                _asyncTxLedger.Outstanding != AsyncTxCapacity ||
                _asyncTxLedger.CompletedUnconsumed != AsyncTxCapacity ||
                _asyncTxLedger.HardwareOwned != 0)
                return false;

            for (uint request = 0; request != AsyncTxCapacity; ++request)
            {
                ulong requestId = batchIds[(int)request];
                uint index = batchIndices[(int)request];
                if (TryGetTransmitCompletion(requestId) !=
                        ManagedE1000AsyncTxResult.Success ||
                    TryGetTransmitCompletion(requestId) !=
                        ManagedE1000AsyncTxResult.Success ||
                    TryConsumeTransmitCompletion(requestId) !=
                        ManagedE1000AsyncTxResult.Success ||
                    TryConsumeTransmitCompletion(requestId) !=
                        ManagedE1000AsyncTxResult.InvalidOrStale ||
                    _asyncTxLedger.OwnershipAt(index) !=
                        ManagedE1000AsyncTxOwnership.Free)
                    return false;
            }

            uint episodeProgress = completedBeforeEpisode +
                (batch + 1) * AsyncTxCapacity;
            if (episodeProgress == 32 || episodeProgress == 64 ||
                episodeProgress == 128 || episodeProgress == 192 ||
                episodeProgress == 256)
                if (!KernelLog.WriteHexLine(
                        "GXOS_NET10:PHASE92_PROGRESS_SUCCESSFUL_TRANSMISSIONS=0x"u8,
                        episodeProgress)) return false;
            if (_asyncTxLedger.Outstanding != 0 ||
                _asyncTxLedger.HardwareOwned != 0 ||
                _asyncTxLedger.CompletedUnconsumed != 0 ||
                _asyncTxAccepted != acceptedBeforeBatch + AsyncTxCapacity ||
                _asyncTxCompleted != completionBase + AsyncTxCapacity)
                return false;
        }

        uint accepted = _asyncTxAccepted - acceptedBase;
        uint completed = _asyncTxCompleted - completedBase;
        uint successful = _asyncTxSuccessfulFrames - successfulBase;
        uint exposed = _asyncTxLedger.ExposedCompletions - exposedBase;
        uint consumed = _asyncTxLedger.ConsumedCompletions - consumedBase;
        uint busy = _phase92BusyProbes - busyBase;
        uint wraps = _phase92DescriptorWraps - wrapBase;
        if (accepted != 128 || completed != accepted || successful != accepted ||
            exposed != accepted || consumed != exposed || busy != 32 || wraps < 7 ||
            _asyncTxLedger.Outstanding != 0 || _asyncTxLedger.HardwareOwned != 0 ||
            _asyncTxLedger.CompletedUnconsumed != 0)
            return false;

        ReadOnlySpan<byte> prefix = episodeNumber == 1
            ? "GXOS_NET10:PHASE92_EPISODE1_"u8
            : "GXOS_NET10:PHASE92_EPISODE2_"u8;
        return KernelLog.WriteHexLine(JoinPhase92(prefix, "ACCEPTED"u8), accepted) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "COMPLETED"u8), completed) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "SUCCESSFUL_FRAMES"u8), successful) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "EXPOSED"u8), exposed) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "CONSUMED"u8), consumed) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "BUSY_PROBES"u8), busy) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "DESCRIPTOR_WRAPS"u8), wraps) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "FINAL_OUTSTANDING"u8),
                _asyncTxLedger.Outstanding) &&
            KernelLog.WriteHexLine(JoinPhase92(prefix, "MAX_OUTSTANDING"u8),
                maxOutstanding) &&
            KernelLog.Write("GXOS_NET10:PHASE92_CAPACITY_RESTORED_AFTER_DRAIN=1\r\n"u8);
    }

    private static ReadOnlySpan<byte> JoinPhase92(ReadOnlySpan<byte> prefix,
        ReadOnlySpan<byte> suffix)
    {
        /* Each returned message is stored in the static literal set below;
           avoid allocating strings or buffers in the driver fixture. */
        if (prefix.SequenceEqual("GXOS_NET10:PHASE92_EPISODE1_"u8))
        {
            if (suffix.SequenceEqual("ACCEPTED"u8)) return "GXOS_NET10:PHASE92_EPISODE1_ACCEPTED=0x"u8;
            if (suffix.SequenceEqual("COMPLETED"u8)) return "GXOS_NET10:PHASE92_EPISODE1_COMPLETED=0x"u8;
            if (suffix.SequenceEqual("SUCCESSFUL_FRAMES"u8)) return "GXOS_NET10:PHASE92_EPISODE1_SUCCESSFUL_FRAMES=0x"u8;
            if (suffix.SequenceEqual("EXPOSED"u8)) return "GXOS_NET10:PHASE92_EPISODE1_EXPOSED=0x"u8;
            if (suffix.SequenceEqual("CONSUMED"u8)) return "GXOS_NET10:PHASE92_EPISODE1_CONSUMED=0x"u8;
            if (suffix.SequenceEqual("BUSY_PROBES"u8)) return "GXOS_NET10:PHASE92_EPISODE1_BUSY_PROBES=0x"u8;
            if (suffix.SequenceEqual("DESCRIPTOR_WRAPS"u8)) return "GXOS_NET10:PHASE92_EPISODE1_DESCRIPTOR_WRAPS=0x"u8;
            if (suffix.SequenceEqual("FINAL_OUTSTANDING"u8)) return "GXOS_NET10:PHASE92_EPISODE1_FINAL_OUTSTANDING=0x"u8;
            if (suffix.SequenceEqual("MAX_OUTSTANDING"u8)) return "GXOS_NET10:PHASE92_EPISODE1_MAX_OUTSTANDING=0x"u8;
        }
        else
        {
            if (suffix.SequenceEqual("ACCEPTED"u8)) return "GXOS_NET10:PHASE92_EPISODE2_ACCEPTED=0x"u8;
            if (suffix.SequenceEqual("COMPLETED"u8)) return "GXOS_NET10:PHASE92_EPISODE2_COMPLETED=0x"u8;
            if (suffix.SequenceEqual("SUCCESSFUL_FRAMES"u8)) return "GXOS_NET10:PHASE92_EPISODE2_SUCCESSFUL_FRAMES=0x"u8;
            if (suffix.SequenceEqual("EXPOSED"u8)) return "GXOS_NET10:PHASE92_EPISODE2_EXPOSED=0x"u8;
            if (suffix.SequenceEqual("CONSUMED"u8)) return "GXOS_NET10:PHASE92_EPISODE2_CONSUMED=0x"u8;
            if (suffix.SequenceEqual("BUSY_PROBES"u8)) return "GXOS_NET10:PHASE92_EPISODE2_BUSY_PROBES=0x"u8;
            if (suffix.SequenceEqual("DESCRIPTOR_WRAPS"u8)) return "GXOS_NET10:PHASE92_EPISODE2_DESCRIPTOR_WRAPS=0x"u8;
            if (suffix.SequenceEqual("FINAL_OUTSTANDING"u8)) return "GXOS_NET10:PHASE92_EPISODE2_FINAL_OUTSTANDING=0x"u8;
            if (suffix.SequenceEqual("MAX_OUTSTANDING"u8)) return "GXOS_NET10:PHASE92_EPISODE2_MAX_OUTSTANDING=0x"u8;
        }
        return "GXOS_NET10:PHASE92_INVALID_COUNTER=0x"u8;
    }

    private Phase91SyncSnapshot CapturePhase91SyncSnapshot()
    {
        Phase91SyncSnapshot snapshot = default;
        snapshot.Phase80Deliveries = _phase80DeliveryCount;
        snapshot.SyncDdPollSuccesses = _synchronousTxDdPollSuccesses;
        snapshot.AsyncTxdwEvents = _asyncTxTxdwEvents;
        snapshot.AsyncUnmatchedTxdw = _asyncTxUnmatchedTxdw;
        snapshot.SyncTxNotificationCount = _syncTxProof.TxdwNotificationCount;
        snapshot.LastEventCause = _phase80LastCause;
        snapshot.DriverEpisode = _driverEpisode;
        snapshot.TxDescriptorIndex = _lastSubmittedTxIndex;
        if (_txRing != null && _lastSubmittedTxIndex < ManagedE1000Protocol.RingCount &&
            _txRing.TryRead8((ulong)_lastSubmittedTxIndex *
                ManagedE1000Protocol.DescriptorSize + 12,
                out byte descriptorStatus))
        {
            snapshot.TxDescriptorStatus = descriptorStatus;
            snapshot.TxDescriptorStatusAvailable = true;
        }
        if (_mmio != null && _mmio.TryRead32(
                ManagedE1000Protocol.RegInterruptMaskSet, out uint ims))
        {
            snapshot.ImsValue = ims;
            snapshot.ImsAvailable = true;
        }
        ManagedInterruptDispatcher? dispatcher = _interruptDispatcher;
        if (dispatcher != null)
        {
            snapshot.RouteToken = dispatcher.E1000SubscriptionId;
            snapshot.DispatcherSequence = dispatcher.E1000LastSequence;
            snapshot.StatsAvailable = dispatcher.TryQueryStats(
                out snapshot.InterruptStats);
            if (snapshot.StatsAvailable)
            {
                snapshot.IcrReadCount = snapshot.InterruptStats.IrqEntryCount;
                snapshot.NativeSourceEvents =
                    snapshot.InterruptStats.SerialIsrCount;
                snapshot.NextEventSequence = snapshot.InterruptStats.NextSequence;
                snapshot.RouteSubscriptionActive =
                    snapshot.InterruptStats.SubscriptionActive;
                snapshot.RouteHardwareEnabled =
                    snapshot.InterruptStats.HardwareEnabled;
            }
            snapshot.LastBatchDrained = dispatcher.Phase91LastBatchDrained;
            snapshot.LastBatchDelivered = dispatcher.Phase91LastBatchDelivered;
            snapshot.LastBatchRejected = dispatcher.Phase91LastBatchRejected;
            snapshot.LastRejectedEvent = dispatcher.Phase91LastRejectedEvent;
            snapshot.LastRejectedShapeMask =
                dispatcher.Phase91LastRejectedShapeMask;
            snapshot.LastRejectedRouteMask =
                dispatcher.Phase91LastRejectedRouteMask;
            snapshot.LastRejectedDriverMask =
                dispatcher.Phase91LastRejectedDriverMask;
        }
        return snapshot;
    }

    private static bool WritePhase91SyncSnapshotValue(
        ReadOnlySpan<byte> stage, ReadOnlySpan<byte> field, ulong value)
    {
        const int labelCapacity = 96;
        ReadOnlySpan<byte> prefix =
            "GXOS_NET10:PHASE91_RESTART_SYNC_"u8;
        Span<byte> label = stackalloc byte[labelCapacity];
        int length = prefix.Length + stage.Length + 1 + field.Length;
        if (length > label.Length) return false;
        prefix.CopyTo(label);
        stage.CopyTo(label[prefix.Length..]);
        label[prefix.Length + stage.Length] = (byte)'_';
        field.CopyTo(label[(prefix.Length + stage.Length + 1)..]);
        return KernelLog.WriteHexLine(label[..length], value);
    }

    private static ulong Phase91QueueDepth(
        in GxManagedKernelInterruptStatsV1 stats) =>
        stats.EnqueuedCount >= stats.DrainedCount
            ? stats.EnqueuedCount - stats.DrainedCount : 0;

    private static void WritePhase91SyncSnapshot(
        ReadOnlySpan<byte> stage, in Phase91SyncSnapshot snapshot)
    {
        _ = WritePhase91SyncSnapshotValue(stage, "EVENT_DELIVERIES"u8,
            snapshot.Phase80Deliveries);
        _ = WritePhase91SyncSnapshotValue(stage, "SYNC_DD_POLL_SUCCESS"u8,
            snapshot.SyncDdPollSuccesses);
        _ = WritePhase91SyncSnapshotValue(stage, "ASYNC_TXDW_EVENTS"u8,
            snapshot.AsyncTxdwEvents);
        _ = WritePhase91SyncSnapshotValue(stage, "UNMATCHED_TXDW"u8,
            snapshot.AsyncUnmatchedTxdw);
        _ = WritePhase91SyncSnapshotValue(stage, "LAST_EVENT_CAUSE"u8,
            snapshot.LastEventCause);
        _ = WritePhase91SyncSnapshotValue(stage, "DRIVER_EPISODE"u8,
            snapshot.DriverEpisode);
        _ = WritePhase91SyncSnapshotValue(stage, "ROUTE_TOKEN"u8,
            snapshot.RouteToken);
        _ = WritePhase91SyncSnapshotValue(stage, "DISPATCH_SEQUENCE"u8,
            snapshot.DispatcherSequence);
        _ = WritePhase91SyncSnapshotValue(stage, "STATS_AVAILABLE"u8,
            snapshot.StatsAvailable ? 1U : 0U);
        if (snapshot.StatsAvailable)
        {
            _ = WritePhase91SyncSnapshotValue(stage, "NATIVE_IRQ_ENTRIES"u8,
                snapshot.IcrReadCount);
            _ = WritePhase91SyncSnapshotValue(stage, "NATIVE_SOURCE_EVENTS"u8,
                snapshot.NativeSourceEvents);
            _ = WritePhase91SyncSnapshotValue(stage, "NEXT_EVENT_SEQUENCE"u8,
                snapshot.NextEventSequence);
            _ = WritePhase91SyncSnapshotValue(stage, "ROUTE_SUBSCRIPTION_ACTIVE"u8,
                snapshot.RouteSubscriptionActive);
            _ = WritePhase91SyncSnapshotValue(stage, "ROUTE_HARDWARE_ENABLED"u8,
                snapshot.RouteHardwareEnabled);
            _ = WritePhase91SyncSnapshotValue(stage, "QUEUE_ENQUEUED"u8,
                snapshot.InterruptStats.EnqueuedCount);
            _ = WritePhase91SyncSnapshotValue(stage, "QUEUE_DRAINED"u8,
                snapshot.InterruptStats.DrainedCount);
            _ = WritePhase91SyncSnapshotValue(stage, "QUEUE_DROPPED"u8,
                snapshot.InterruptStats.DroppedCount);
            _ = WritePhase91SyncSnapshotValue(stage, "QUEUE_DEPTH"u8,
                Phase91QueueDepth(in snapshot.InterruptStats));
        }
        _ = WritePhase91SyncSnapshotValue(stage, "IMS_AVAILABLE"u8,
            snapshot.ImsAvailable ? 1U : 0U);
        if (snapshot.ImsAvailable)
            _ = WritePhase91SyncSnapshotValue(stage, "IMS"u8,
                snapshot.ImsValue);
        _ = WritePhase91SyncSnapshotValue(stage, "TX_DESCRIPTOR_INDEX"u8,
            snapshot.TxDescriptorIndex);
        _ = WritePhase91SyncSnapshotValue(stage, "TX_DESCRIPTOR_STATUS_AVAILABLE"u8,
            snapshot.TxDescriptorStatusAvailable ? 1U : 0U);
        if (snapshot.TxDescriptorStatusAvailable)
            _ = WritePhase91SyncSnapshotValue(stage, "TX_DESCRIPTOR_STATUS"u8,
                snapshot.TxDescriptorStatus);
        _ = WritePhase91SyncSnapshotValue(stage, "LAST_BATCH_DRAINED"u8,
            snapshot.LastBatchDrained);
        _ = WritePhase91SyncSnapshotValue(stage, "LAST_BATCH_DELIVERED"u8,
            snapshot.LastBatchDelivered);
        _ = WritePhase91SyncSnapshotValue(stage, "LAST_BATCH_REJECTED"u8,
            snapshot.LastBatchRejected);
    }

    private void WritePhase91SyncFailure(
        uint failureStage, uint failingPredicateMask, uint syncEventBase,
        uint syncDdPollBase, uint polls, uint dispatchCalls,
        uint deliveredByDispatcher, uint rejectedByDispatcher,
        in Phase91SyncSnapshot before, in Phase91SyncSnapshot afterTx,
        in Phase91SyncSnapshot afterDispatch)
    {
        _ = KernelLog.Write(
            "GXOS_NET10:PHASE91_RESTART_SYNC_DIAGNOSTIC=1\r\n"u8);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_FAILURE_STAGE=0x"u8,
            failureStage);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_FAILING_PREDICATE_MASK=0x"u8,
            failingPredicateMask);
        _ = KernelLog.Write(
            "GXOS_NET10:PHASE91_RESTART_SYNC_PREDICATE_BITS=0:FRAME_BUILD,1:TX_DD_RESULT,2:DD_COUNTER,3:FRESH_POST_SUBMISSION_TXDW,4:DISPATCH_OR_SEQUENCE_BASELINE,5:REJECTED_EVENTS,6:PASS_MARKER_WRITE,7:ASYNC_OUTSTANDING\r\n"u8);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_EVENT_BASE=0x"u8,
            syncEventBase);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_FIRST_EVENT_SEQUENCE=0x"u8,
            before.NextEventSequence);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_FRESH_TXDW_NOTIFICATIONS=0x"u8,
            afterDispatch.SyncTxNotificationCount);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_DD_POLL_BASE=0x"u8,
            syncDdPollBase);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_DISPATCH_POLLS=0x"u8,
            polls);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_DISPATCH_CALLS=0x"u8,
            dispatchCalls);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_DISPATCH_DELIVERED=0x"u8,
            deliveredByDispatcher);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_DISPATCH_REJECTED=0x"u8,
            rejectedByDispatcher);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_EVENT_DELIVERY_DELTA=0x"u8,
            afterDispatch.Phase80Deliveries - syncEventBase);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_DD_POLL_SUCCESS_DELTA=0x"u8,
            afterDispatch.SyncDdPollSuccesses - syncDdPollBase);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_ASYNC_TXDW_EVENT_DELTA=0x"u8,
            afterDispatch.AsyncTxdwEvents - before.AsyncTxdwEvents);
        if (before.StatsAvailable && afterTx.StatsAvailable)
        {
            _ = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_AFTER_TX_NATIVE_SOURCE_DELTA=0x"u8,
                afterTx.NativeSourceEvents - before.NativeSourceEvents);
            _ = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_AFTER_TX_QUEUE_ENQUEUE_DELTA=0x"u8,
                afterTx.InterruptStats.EnqueuedCount -
                    before.InterruptStats.EnqueuedCount);
            _ = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_AFTER_TX_QUEUE_DROP_DELTA=0x"u8,
                afterTx.InterruptStats.DroppedCount -
                    before.InterruptStats.DroppedCount);
        }
        if (before.StatsAvailable && afterDispatch.StatsAvailable)
        {
            _ = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_NATIVE_SOURCE_DELTA=0x"u8,
                afterDispatch.NativeSourceEvents - before.NativeSourceEvents);
            _ = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_QUEUE_ENQUEUE_DELTA=0x"u8,
                afterDispatch.InterruptStats.EnqueuedCount -
                    before.InterruptStats.EnqueuedCount);
            _ = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_QUEUE_DRAIN_DELTA=0x"u8,
                afterDispatch.InterruptStats.DrainedCount -
                    before.InterruptStats.DrainedCount);
            _ = KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_QUEUE_DROP_DELTA=0x"u8,
                afterDispatch.InterruptStats.DroppedCount -
                    before.InterruptStats.DroppedCount);
        }
        WritePhase91SyncSnapshot("BEFORE"u8, in before);
        WritePhase91SyncSnapshot("AFTER_TX"u8, in afterTx);
        WritePhase91SyncSnapshot("AFTER_DISPATCH"u8, in afterDispatch);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_SIZE=0x"u8,
            afterDispatch.LastRejectedEvent.Size);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_ABI=0x"u8,
            afterDispatch.LastRejectedEvent.AbiVersion);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_TYPE=0x"u8,
            afterDispatch.LastRejectedEvent.EventType);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_DEVICE_KIND=0x"u8,
            afterDispatch.LastRejectedEvent.DeviceKind);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_DEVICE_ID=0x"u8,
            afterDispatch.LastRejectedEvent.DeviceId);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_FLAGS=0x"u8,
            afterDispatch.LastRejectedEvent.Flags);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_SEQUENCE=0x"u8,
            afterDispatch.LastRejectedEvent.Sequence);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_STATUS=0x"u8,
            afterDispatch.LastRejectedEvent.Status);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_SHAPE_MASK=0x"u8,
            afterDispatch.LastRejectedShapeMask);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_ROUTE_MASK=0x"u8,
            afterDispatch.LastRejectedRouteMask);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_DRIVER_MASK=0x"u8,
            afterDispatch.LastRejectedDriverMask);
        _ = KernelLog.WriteHexLine(
            "GXOS_NET10:PHASE91_RESTART_SYNC_LAST_REJECT_PAYLOAD=0x"u8,
            afterDispatch.LastRejectedEvent.PayloadByte);
        _ = KernelLog.Write(
            "GXOS_NET10:PHASE91_RESTART_SYNC_ICR_POLICY=LAST_CAPTURED_ONLY;LIVE_ICR_NOT_READ\r\n"u8);
    }

    internal bool TryRunPhase91Lifecycle(out ManagedE1000Driver? restarted)
    {
        restarted = null;
        if (!_phase91Requested || !_phase90AsyncTxPassed ||
            (_phase92Requested && !_phase92FirstEpisodePassed) ||
            _state != ManagedE1000DriverState.Running ||
            _asyncTxLedger.Outstanding != 0 || _txRing == null ||
            _txBuffers == null) return false;

        Span<ulong> teardownIds = stackalloc ulong[(int)AsyncTxCapacity];
        uint completionBase = _asyncTxCompleted;
        uint notDoneDuringPreStopSubmission = 0;
        for (uint request = 0; request != AsyncTxCapacity; ++request)
        {
            uint sequence = 0x91000101U + request;
            if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                    _phase80Frame, _mac, sequence) ||
                TrySubmitFrame(_phase80Frame, _phase80Frame.Length,
                out ulong requestId) != ManagedE1000AsyncTxSubmitResult.Accepted)
                return false;
            teardownIds[(int)request] = requestId;
            if (_txRing.TryRead8((ulong)_lastSubmittedTxIndex *
                    ManagedE1000Protocol.DescriptorSize + 12,
                    out byte status) &&
                (status & ManagedE1000Protocol.TxStatusDone) == 0)
                notDoneDuringPreStopSubmission++;
        }
        if (!TryDrainPhase90Completions(AsyncTxCapacity, completionBase))
            return false;
        for (uint request = 0; request != AsyncTxCapacity; ++request)
            if (TryGetTransmitCompletion(teardownIds[(int)request]) !=
                ManagedE1000AsyncTxResult.Success) return false;

        uint outstandingAtStop = _asyncTxLedger.Outstanding;
        uint completedUnconsumedAtStop =
            _asyncTxLedger.CompletedUnconsumed;
        uint hardwareOwnedAtStop = _asyncTxLedger.HardwareOwned;
        if (outstandingAtStop == 0 ||
            completedUnconsumedAtStop + hardwareOwnedAtStop != outstandingAtStop ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_ACCEPTED_SUBMISSIONS=0x"u8,
                _asyncTxAccepted) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_OUTSTANDING_UNCONSUMED_AT_STOP=0x"u8,
                outstandingAtStop) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_COMPLETED_UNCONSUMED_AT_STOP=0x"u8,
                completedUnconsumedAtStop) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_HARDWARE_OWNED_NOT_DD_AT_STOP=0x"u8,
                hardwareOwnedAtStop) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_DD_COMPLETIONS_BEFORE_STOP=0x"u8,
                _asyncTxCompleted) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_TXDW_EVENTS_BEFORE_STOP=0x"u8,
                _asyncTxTxdwEvents) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_ASYNC_TX_SUCCESSFUL_FRAMES_BEFORE_STOP=0x"u8,
                _asyncTxSuccessfulFrames) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_COMPLETIONS_EXPOSED_BEFORE_STOP=0x"u8,
                _asyncTxLedger.ExposedCompletions) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_COMPLETIONS_CONSUMED_BEFORE_STOP=0x"u8,
                _asyncTxLedger.ConsumedCompletions) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_INVALIDATED_BY_STOP=0x"u8,
                outstandingAtStop) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_SYNC_DD_POLL_SUCCESS_BEFORE_STOP=0x"u8,
                _synchronousTxDdPollSuccesses))
            return false;

        _phase91StopProbeId = teardownIds[0];
        if (!ManagedE1000Protocol.TryDecodeAsyncTxRequestId(
                _phase90InitialRequestId, ManagedE1000Protocol.RingCount,
                out uint reusedIndex, out uint reusedGeneration,
                out uint oldEpisode))
            return false;
        ulong oldRouteToken = _interruptDispatcher?.E1000SubscriptionId ?? 0;
        if (oldRouteToken == 0 ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_OLD_REQUEST_ID=0x"u8,
                _phase90InitialRequestId) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_OLD_DESCRIPTOR_INDEX=0x"u8,
                reusedIndex) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_OLD_SLOT_GENERATION=0x"u8,
                reusedGeneration) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_OLD_DRIVER_EPISODE=0x"u8,
                oldEpisode) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_OLD_ROUTE_TOKEN=0x"u8,
                oldRouteToken) ||
            !TryStop() || _state != ManagedE1000DriverState.Stopped ||
            _asyncTxLedger.Outstanding != 0)
            return false;
        for (uint request = 0; request != AsyncTxCapacity; ++request)
            if (TryGetTransmitCompletion(teardownIds[(int)request]) !=
                    ManagedE1000AsyncTxResult.InvalidOrStale ||
                TryConsumeTransmitCompletion(teardownIds[(int)request]) !=
                    ManagedE1000AsyncTxResult.InvalidOrStale)
                return false;
        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_PRE_DRAIN_NOT_DD_SAMPLES=0x"u8,
                notDoneDuringPreStopSubmission) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_OLD_COMPLETED_LEDGER_INVALIDATED=1\r\n"u8))
            return false;

        if (!KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_BEGINS=1\r\n"u8))
            return false;
        if (_phase92Requested) EnablePhase92EnduranceMode();
        else EnablePhase91LifecycleMode();
        ManagedE1000Driver? candidate = TryCreate();
        if (candidate == null || candidate._driverEpisode == _driverEpisode ||
            !candidate.TryStart() ||
            candidate._state != ManagedE1000DriverState.Running ||
            candidate._asyncTxLedger.Outstanding != 0)
            return false;
        if (!KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_DRIVER_RUNNING=1\r\n"u8))
            return false;
        candidate._phase80Passed = _phase80Passed;
        candidate._phase90AsyncTxPassed = true;
        candidate._phase92OldEpisodeRequestId = _phase92OldEpisodeRequestId;
        /* Native interrupt sequence numbers are global to the route. The new
           driver must begin below its first post-subscription event rather
           than inherit the previous driver's last sequence. */
        candidate._phase80LastSequence =
            candidate._interruptDispatcher?.E1000SequenceBaselineCaptured == true
                ? candidate._interruptDispatcher.E1000LastSequence : 0;
        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_ROUTE_BASELINE=0x"u8,
                candidate._phase80LastSequence)) return false;
        ulong newRouteToken =
            candidate._interruptDispatcher?.E1000SubscriptionId ?? 0;
        if (newRouteToken == 0 || newRouteToken == oldRouteToken ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_NEW_ROUTE_TOKEN=0x"u8,
                newRouteToken) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_ROUTE_TOKEN_CHANGED=1\r\n"u8))
            return false;
        if (!candidate.TryRunPhase80Proof()) return false;
        if (candidate._txIndex != reusedIndex)
        {
            KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_ROUTE_PROOF_FAILURE=TX_INDEX_REUSE\r\n"u8);
            return false;
        }
        if (!KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_ROUTE_REINSTALLED=1\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_PHASE80_DD_ROUTE_PASS=1\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_SAME_SLOT_READY=1\r\n"u8))
            return false;
        if (candidate._phase92Requested &&
            !KernelLog.Write(
                "GXOS_NET10:PHASE92_RESTART_COMPLETED=1\r\n"u8))
            return false;
        restarted = candidate;

        uint restartCompletionBase = candidate._asyncTxCompleted;
        if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                candidate._phase80Frame, candidate._mac, 0x91000201U))
            return false;
        if (candidate.TrySubmitFrame(candidate._phase80Frame,
                candidate._phase80Frame.Length, out ulong sameSlotId) !=
                ManagedE1000AsyncTxSubmitResult.Accepted ||
            !candidate.TryDecodeAsyncTxRequest(sameSlotId,
                out uint sameSlotIndex, out uint sameSlotGeneration) ||
            !ManagedE1000Protocol.TryDecodeAsyncTxRequestId(sameSlotId,
                ManagedE1000Protocol.RingCount, out _, out _,
                out uint sameSlotEpisode) ||
            sameSlotIndex != reusedIndex ||
            sameSlotGeneration != reusedGeneration ||
            sameSlotEpisode == oldEpisode ||
            sameSlotId == _phase90InitialRequestId ||
            candidate.TryGetTransmitCompletion(_phase90InitialRequestId) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            candidate.TryConsumeTransmitCompletion(_phase90InitialRequestId) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            candidate.TryGetTransmitCompletion(sameSlotId) !=
                ManagedE1000AsyncTxResult.Pending ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_NEW_REQUEST_ID=0x"u8, sameSlotId) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_NEW_DESCRIPTOR_INDEX=0x"u8,
                sameSlotIndex) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_NEW_SLOT_GENERATION=0x"u8,
                sameSlotGeneration) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_NEW_DRIVER_EPISODE=0x"u8,
                sameSlotEpisode) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_SAME_SLOT_NEW_ID_NO_ALIAS=1\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_FIRST_TX_SUBMITTED=1\r\n"u8) ||
            !candidate.TryDrainPhase90Completions(1, restartCompletionBase) ||
            candidate.TryGetTransmitCompletion(sameSlotId) !=
                ManagedE1000AsyncTxResult.Success ||
            candidate.TryConsumeTransmitCompletion(sameSlotId) !=
                ManagedE1000AsyncTxResult.Success ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_FIRST_ASYNC_TX_PASS=1\r\n"u8))
            return false;

        if (candidate._phase92Requested)
        {
            uint outstandingBeforeOldEpisodeQuery =
                candidate._asyncTxLedger.Outstanding;
            uint acceptedBeforeOldEpisodeQuery = candidate._asyncTxAccepted;
            uint completedBeforeOldEpisodeQuery = candidate._asyncTxCompleted;
            uint consumedBeforeOldEpisodeQuery =
                candidate._asyncTxLedger.ConsumedCompletions;
            if (_phase92OldEpisodeRequestId == 0 ||
                candidate.TryGetTransmitCompletion(
                    _phase92OldEpisodeRequestId) !=
                    ManagedE1000AsyncTxResult.InvalidOrStale ||
                candidate.TryConsumeTransmitCompletion(
                    _phase92OldEpisodeRequestId) !=
                    ManagedE1000AsyncTxResult.InvalidOrStale ||
                candidate._asyncTxLedger.Outstanding !=
                    outstandingBeforeOldEpisodeQuery ||
                candidate._asyncTxAccepted != acceptedBeforeOldEpisodeQuery ||
                candidate._asyncTxCompleted != completedBeforeOldEpisodeQuery ||
                candidate._asyncTxLedger.ConsumedCompletions !=
                    consumedBeforeOldEpisodeQuery ||
                candidate._asyncTxLedger.OwnershipAt(sameSlotIndex) !=
                    ManagedE1000AsyncTxOwnership.Free ||
                !KernelLog.Write(
                    "GXOS_NET10:PHASE92_OLD_EPISODE_ID_REJECTED_AFTER_RESTART=1\r\n"u8))
                return false;
        }

        Span<ulong> restartedIds = stackalloc ulong[(int)AsyncTxCapacity];
        restartCompletionBase = candidate._asyncTxCompleted;
        for (uint request = 0; request != AsyncTxCapacity; ++request)
        {
            if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                    candidate._phase80Frame, candidate._mac,
                    0x91000301U + request) ||
                candidate.TrySubmitFrame(candidate._phase80Frame,
                    candidate._phase80Frame.Length,
                    out ulong requestId) !=
                    ManagedE1000AsyncTxSubmitResult.Accepted ||
                candidate.TryGetTransmitCompletion(requestId) !=
                    ManagedE1000AsyncTxResult.Pending)
                return false;
            restartedIds[(int)request] = requestId;
        }
        if (candidate._asyncTxLedger.Outstanding != AsyncTxCapacity ||
            candidate.TrySubmitFrame(candidate._phase80Frame,
                candidate._phase80Frame.Length, out _) !=
                ManagedE1000AsyncTxSubmitResult.Busy)
            return false;
        uint outstandingBeforeStaleQueries = candidate._asyncTxLedger.Outstanding;
        uint consumedBeforeStaleQueries =
            candidate._asyncTxLedger.ConsumedCompletions;
        if (candidate.TryGetTransmitCompletion(_phase90InitialRequestId) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            candidate.TryConsumeTransmitCompletion(_phase90InitialRequestId) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            candidate.TryGetTransmitCompletion(sameSlotId) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            candidate.TryConsumeTransmitCompletion(sameSlotId) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            candidate._asyncTxLedger.Outstanding != outstandingBeforeStaleQueries ||
            candidate._asyncTxLedger.ConsumedCompletions != consumedBeforeStaleQueries)
            return false;
        for (uint request = 0; request != AsyncTxCapacity; ++request)
        {
            if (!candidate.TryDecodeAsyncTxRequest(
                    restartedIds[(int)request], out uint activeIndex, out _) ||
                candidate._asyncTxLedger.OwnershipAt(activeIndex) ==
                    ManagedE1000AsyncTxOwnership.Free)
                return false;
        }
        if (!KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_ASYNC_CAPACITY_FOUR_AND_FIFTH_BUSY=1\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_OLD_IDS_REJECTED_WHILE_ACTIVE=1\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_STALE_IDS_NO_MUTATION=1\r\n"u8) ||
            !candidate.TryDrainPhase90Completions(AsyncTxCapacity,
                restartCompletionBase)) return false;
        for (uint request = 0; request != AsyncTxCapacity; ++request)
            if (candidate.TryGetTransmitCompletion(restartedIds[(int)request]) !=
                    ManagedE1000AsyncTxResult.Success ||
                candidate.TryConsumeTransmitCompletion(restartedIds[(int)request]) !=
                    ManagedE1000AsyncTxResult.Success)
                return false;
        if (!ManagedE1000Protocol.TryDecodeAsyncTxRequestId(
                restartedIds[0], ManagedE1000Protocol.RingCount,
                out uint reusedAgainIndex, out uint reusedAgainGeneration,
                out uint restartedEpisode) ||
            reusedAgainIndex >= ManagedE1000Protocol.RingCount ||
            reusedAgainGeneration != 1 ||
            restartedEpisode != candidate._driverEpisode ||
            restartedEpisode == oldEpisode || restartedIds[0] == sameSlotId ||
            candidate.TryGetTransmitCompletion(_phase90InitialRequestId) !=
                ManagedE1000AsyncTxResult.InvalidOrStale ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_ASYNC_FOUR_COMPLETED_CONSUMED=1\r\n"u8))
            return false;

        if (candidate._phase92Requested &&
            !candidate.TryRunPhase92EnduranceEpisode(
                2, 0x92020000U, 128))
            return false;

        uint syncSequence = 0x91000401U;
        uint restartSyncDdPollBase =
            candidate._synchronousTxDdPollSuccesses;
        if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                candidate._phase80Frame, candidate._mac, syncSequence))
        {
            Phase91SyncSnapshot noTx =
                candidate.CapturePhase91SyncSnapshot();
            candidate.WritePhase91SyncFailure(1, 1U << 0,
                candidate._phase80DeliveryCount, restartSyncDdPollBase,
                0, 0, 0, 0, in noTx, in noTx, in noTx);
            return false;
        }
        bool synchronousTxSucceeded = candidate.TryTransmitFrameWithSyncProof(
            candidate._phase80Frame, candidate._phase80Frame.Length,
            out Phase91SyncSnapshot syncBefore, out bool syncSubmissionPrepared,
            out bool syncProofArmed);
        uint syncEventBase = syncBefore.Phase80Deliveries;
        if (!syncSubmissionPrepared)
        {
            Phase91SyncSnapshot preparationFailure =
                candidate.CapturePhase91SyncSnapshot();
            candidate.WritePhase91SyncFailure(2, 1U << 1,
                syncEventBase, restartSyncDdPollBase, 0, 0, 0, 0,
                in syncBefore, in preparationFailure, in preparationFailure);
            return false;
        }
        if (!syncProofArmed)
        {
            Phase91SyncSnapshot armFailure =
                candidate.CapturePhase91SyncSnapshot();
            candidate.WritePhase91SyncFailure(1, 1U << 4,
                candidate._phase80DeliveryCount, restartSyncDdPollBase,
                0, 0, 0, 0, in armFailure, in armFailure, in armFailure);
            return false;
        }
        if (!synchronousTxSucceeded)
        {
            Phase91SyncSnapshot syncAfterTx =
                candidate.CapturePhase91SyncSnapshot();
            uint predicateMask = 1U << 1;
            if (candidate._synchronousTxDdPollSuccesses !=
                restartSyncDdPollBase + 1) predicateMask |= 1U << 2;
            candidate.WritePhase91SyncFailure(2, predicateMask,
                syncEventBase, restartSyncDdPollBase, 0, 0, 0, 0,
                in syncBefore, in syncAfterTx, in syncAfterTx);
            return false;
        }
        uint syncDispatchPolls = 0;
        uint syncDispatchCalls = 0;
        uint syncDeliveredByDispatcher = 0;
        uint syncRejectedByDispatcher = 0;
        for (uint poll = 0; poll != Phase90CompletionPollLimit &&
             !candidate._syncTxProof.IsSatisfied; ++poll)
        {
            syncDispatchPolls++;
            bool dispatchSucceeded =
                ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    candidate, out uint delivered, out uint rejected);
            syncDispatchCalls++;
            syncDeliveredByDispatcher += delivered;
            syncRejectedByDispatcher += rejected;
            if (!dispatchSucceeded || rejected != 0)
            {
                Phase91SyncSnapshot dispatchFailure =
                    candidate.CapturePhase91SyncSnapshot();
                uint predicateMask = (!dispatchSucceeded ? 1U << 4 : 0) |
                                     (rejected != 0 ? 1U << 5 : 0);
                candidate.WritePhase91SyncFailure(3, predicateMask,
                    syncEventBase, restartSyncDdPollBase,
                    syncDispatchPolls, syncDispatchCalls,
                    syncDeliveredByDispatcher, syncRejectedByDispatcher,
                    in syncBefore, in dispatchFailure, in dispatchFailure);
                return false;
            }
        }
        Phase91SyncSnapshot syncAfterDispatch = default;
        uint syncFailurePredicateMask = 0;
        bool syncDdCompleted = candidate._synchronousTxDdPollSuccesses ==
                               restartSyncDdPollBase + 1;
        if (!candidate._syncTxProof.IsSatisfied)
            syncFailurePredicateMask |= 1U << 3;
        if (!syncDdCompleted)
            syncFailurePredicateMask |= 1U << 2;
        if (syncFailurePredicateMask != 0 ||
            !candidate.IsSyncTxProofAccepted(syncDdCompleted, true,
                syncRejectedByDispatcher))
        {
            if (candidate._asyncTxLedger.Outstanding != 0)
                syncFailurePredicateMask |= 1U << 7;
            syncAfterDispatch = candidate.CapturePhase91SyncSnapshot();
            candidate.WritePhase91SyncFailure(4,
                syncFailurePredicateMask, syncEventBase,
                restartSyncDdPollBase, syncDispatchPolls,
                syncDispatchCalls, syncDeliveredByDispatcher,
                syncRejectedByDispatcher, in syncBefore,
                in syncAfterDispatch, in syncAfterDispatch);
            return false;
        }
        candidate.SealSyncTxProof();
        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_FRESH_TXDW_NOTIFICATIONS=0x"u8,
                candidate.SyncTxTxdwNotificationCount) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_SYNC_TX_DD_POLL_PASS=1\r\n"u8))
        {
            Phase91SyncSnapshot markerWriteFailure =
                candidate.CapturePhase91SyncSnapshot();
            candidate.WritePhase91SyncFailure(5, 1U << 6,
                syncEventBase, restartSyncDdPollBase,
                syncDispatchPolls, syncDispatchCalls,
                syncDeliveredByDispatcher, syncRejectedByDispatcher,
                in syncBefore, in markerWriteFailure, in markerWriteFailure);
            return false;
        }

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_ASYNC_CAPACITY=0x"u8,
                AsyncTxCapacity) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_REUSED_DESCRIPTOR=0x"u8,
                sameSlotIndex) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_REUSED_GENERATION=0x"u8,
                sameSlotGeneration) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_BURST_FIRST_DESCRIPTOR=0x"u8,
                reusedAgainIndex) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_ACCEPTED_SUBMISSIONS=0x"u8,
                candidate._asyncTxAccepted) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_DD_COMPLETIONS=0x"u8,
                candidate._asyncTxCompleted) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_TXDW_EVENTS=0x"u8,
                candidate._asyncTxTxdwEvents) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_ASYNC_TX_SUCCESSFUL_FRAMES=0x"u8,
                candidate._asyncTxSuccessfulFrames) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_COMPLETIONS_EXPOSED=0x"u8,
                candidate._asyncTxLedger.ExposedCompletions) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_COMPLETIONS_CONSUMED=0x"u8,
                candidate._asyncTxLedger.ConsumedCompletions) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_DD_POLL_SUCCESS=0x"u8,
                candidate._synchronousTxDdPollSuccesses) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_SYNC_TX_DD_POLL_FRAMES=0x"u8,
                candidate._synchronousTxDdPollSuccesses -
                    restartSyncDdPollBase) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_ASYNC_CAPACITY_RESTORED=1\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_TXDW_ROUTE_READY=1\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE91_RESTART_RX_READY=1\r\n"u8))
            return false;

        if (candidate._phase83Requested && candidate._rxRing != null &&
            candidate._ethernet != null)
        {
            for (uint poll = 0; poll != Phase83InterruptPollLimit &&
                 candidate._phase83RxDeliveryCount == 0; ++poll)
                if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                        candidate, out _, out uint rejected) || rejected != 0)
                    return false;
            if (candidate._phase83RxDeliveryCount != 1 ||
                candidate._phase83RxDescriptorCount != 1 ||
                candidate._ethernet.ReceivedFrameCount != 1 ||
                candidate._ethernet.ParsedFrameCount != 1 ||
                candidate._ethernet.Phase84TestConsumerDeliveryCount != 1)
                return false;
            if (!KernelLog.Write(
                    "GXOS_NET10:PHASE91_POST_RESTART_RX_PASS=1\r\n"u8))
                return false;
        }
        else return false;

        candidate._phase91Passed = KernelLog.Write(
            "GXOS_NET10:PHASE91_RESTART_TX_RX_LIVENESS=PASS\r\n"u8);
        if (candidate._phase92Requested)
        {
            if (candidate._asyncTxLedger.Outstanding != 0 ||
                candidate._asyncTxLedger.HardwareOwned != 0 ||
                candidate._asyncTxLedger.CompletedUnconsumed != 0 ||
                candidate._asyncTxLedger.ExposedCompletions !=
                    candidate._asyncTxLedger.ConsumedCompletions)
                return false;
            for (uint index = 0; index != ManagedE1000Protocol.RingCount; ++index)
                if (candidate._asyncTxLedger.OwnershipAt(index) !=
                    ManagedE1000AsyncTxOwnership.Free)
                    return false;
            candidate._phase92Passed = candidate._phase91Passed &&
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE92_FINAL_OUTSTANDING=0x"u8,
                    candidate._asyncTxLedger.Outstanding) &&
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE92_FINAL_HARDWARE_OWNED=0x"u8,
                    candidate._asyncTxLedger.HardwareOwned) &&
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE92_FINAL_COMPLETED_UNCONSUMED=0x"u8,
                    candidate._asyncTxLedger.CompletedUnconsumed) &&
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE92_FINAL_EXPOSED=0x"u8,
                    candidate._asyncTxLedger.ExposedCompletions) &&
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE92_FINAL_CONSUMED=0x"u8,
                    candidate._asyncTxLedger.ConsumedCompletions) &&
                KernelLog.Write(
                    "GXOS_NET10:PHASE92_GUEST_RESOURCE_DRAIN_PASS=1\r\n"u8);
        }
        return candidate._phase91Passed &&
            (!candidate._phase92Requested || candidate._phase92Passed);
    }

    private bool TrySubmitAndCompletePhase90Frame(uint sequence,
        out ulong requestId)
    {
        requestId = 0;
        uint completionBase = _asyncTxCompleted;
        if (!ManagedE1000Protocol.TryBuildPhase90Frame(
                _phase80Frame, _mac, sequence) ||
            TrySubmitFrame(_phase80Frame, _phase80Frame.Length,
                out requestId) != ManagedE1000AsyncTxSubmitResult.Accepted ||
            TryGetTransmitCompletion(requestId) !=
                ManagedE1000AsyncTxResult.Pending ||
            !TryDrainPhase90Completions(1, completionBase) ||
            TryGetTransmitCompletion(requestId) !=
                ManagedE1000AsyncTxResult.Success ||
            TryConsumeTransmitCompletion(requestId) !=
                ManagedE1000AsyncTxResult.Success)
            return false;
        return true;
    }

    private bool TryDrainPhase90Completions(uint expected,
        uint completionBase)
    {
        for (uint poll = 0; poll != Phase90CompletionPollLimit &&
             _asyncTxCompleted < completionBase + expected; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out _, out uint rejected))
                return false;
            if (rejected != 0)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE91_RESTART_REJECTED_EVENTS=0x"u8,
                    rejected);
                return false;
            }
        }
        if (_asyncTxCompleted != completionBase + expected)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_COMPLETION_COUNT=0x"u8,
                _asyncTxCompleted);
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE91_RESTART_COMPLETION_BASE=0x"u8,
                completionBase);
            return false;
        }
        return true;
    }

    internal bool TryRunPhase83RxProof()
    {
        if (!_phase83Requested || !_phase80Requested || !_interruptSubscribed ||
            _state != ManagedE1000DriverState.Running ||
            _phase80DeliveryCount != 4 || _phase83RxDeliveryCount != 0 ||
            _rxRing == null || _rxBuffers == null || _mmio == null)
            return false;

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE83_RX_RING_DESCRIPTORS=0x"u8,
                ManagedE1000Protocol.RingCount) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE83_RX_BUFFER_SIZE=0x"u8,
                ManagedE1000Protocol.PacketBufferSize) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE83_RX_MAX_ACCEPTED_LENGTH=0x"u8,
                ManagedE1000Protocol.PacketBufferSize) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE83_RX_MASK=0x"u8,
                ManagedE1000Protocol.InterruptMaskTxDescriptorWriteback |
                ManagedE1000Protocol.InterruptMaskRxTimer) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE83_RX_READY=1\r\n"u8))
            return false;

        for (uint poll = 0; poll != Phase83InterruptPollLimit &&
             _phase83RxDeliveryCount != _phase83DescriptorIndices.Length; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out uint delivered, out uint rejected) ||
                rejected != 0 || delivered > 1)
                return false;
        }

        if (_phase83RxDeliveryCount != _phase83DescriptorIndices.Length ||
            _phase83RxDescriptorCount != _phase83DescriptorIndices.Length ||
            _ethernet == null ||
            _ethernet.ReceivedFrameCount != _phase83DescriptorIndices.Length ||
            _ethernet.ParsedFrameCount != _phase83DescriptorIndices.Length ||
            _ethernet.Phase84TestConsumerDeliveryCount !=
                _phase83DescriptorIndices.Length ||
            _ethernet.MalformedFrameCount != 0 ||
            _ethernet.UnsupportedEtherTypeDropCount != 0 ||
            _ethernet.Phase84TestConsumerFailureCount != 0 ||
            _phase80DeliveryCount != 8 ||
            (_phase80LastCause &
             ManagedE1000Protocol.InterruptCauseRxTimer) == 0 ||
            _rxIndex != _phase83DescriptorIndices.Length ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail,
                             out uint postedTail) || postedTail != 3U)
            return false;
        for (uint index = 0; index != _phase83DescriptorIndices.Length; ++index)
            if (_phase83DescriptorIndices[index] != index) return false;

        bool passed = KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_MANAGED_DELIVERIES=0x"u8,
                   _phase83RxDeliveryCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_RX_DESCRIPTORS_COMPLETED=0x"u8,
                   _phase83RxDescriptorCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_ETHERNET_RECEIVED=0x"u8,
                   _ethernet.ReceivedFrameCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_ETHERNET_PARSED=0x"u8,
                   _ethernet.ParsedFrameCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_TEST_CONSUMER_DELIVERIES=0x"u8,
                   _ethernet.Phase84TestConsumerDeliveryCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_MALFORMED_DROPS=0x"u8,
                   _ethernet.MalformedFrameCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_UNSUPPORTED_ETHER_TYPE_DROPS=0x"u8,
                   _ethernet.UnsupportedEtherTypeDropCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_CONSUMER_FAILURES=0x"u8,
                   _ethernet.Phase84TestConsumerFailureCount) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_LAST_DESTINATION=0x"u8,
                   _ethernet.Phase84LastMetadata.DestinationMac) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_LAST_SOURCE=0x"u8,
                   _ethernet.Phase84LastMetadata.SourceMac) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_LAST_ETHERTYPE=0x"u8,
                   _ethernet.Phase84LastMetadata.EtherType) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_LAST_PAYLOAD_LENGTH=0x"u8,
                   _ethernet.Phase84LastMetadata.PayloadLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE84_LAST_SENTINEL=0x"u8,
                   _ethernet.Phase84LastMetadata.Sentinel) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE84_CROSS_FRAME_SENTINELS_DISTINCT=1\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE84_ETHERNET_INTEGRATION_PASS=1\r\n"u8) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_LAST_EVENT_SEQUENCE=0x"u8,
                   _phase80LastSequence) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_LAST_CAUSE=0x"u8,
                   _phase80LastCause) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_LAST_DESCRIPTOR_INDEX=0x"u8,
                   _phase83LastDescriptorIndex) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_LAST_DESCRIPTOR_STATUS=0x"u8,
                   _phase83LastDescriptorStatus) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_LAST_DESCRIPTOR_ERRORS=0x"u8,
                   _phase83LastDescriptorErrors) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_LAST_FRAME_LENGTH=0x"u8,
                   _phase83LastFrameLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE83_RX_RDT_AFTER_RECYCLE=0x"u8,
                   postedTail) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE83_RX_REPEAT_FRAMES=3\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE83_RX_RING_ADVANCE_AND_RECYCLE_OK=1\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE83_RX_MANAGED_PROOF=1\r\n"u8);
        _phase83Passed = passed;
        return passed;
    }

    internal bool TryRunPhase85ArpProof()
    {
        if (!_phase85ArpRequested || !_phase80Requested || !_interruptSubscribed ||
            _state != ManagedE1000DriverState.Running ||
            _phase80DeliveryCount < 4 || _phase85ArpDeliveryCount != 0 ||
            _phase83RxDescriptorCount != 0 || _rxRing == null ||
            _rxBuffers == null || _ethernet == null || _mmio == null ||
            _ethernet.LocalIpv4Value != 0x0A0F0001U)
            return false;

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_GUEST_MAC=0x"u8, _macValue) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_LOCAL_IPV4=0x"u8,
                _ethernet.LocalIpv4Value) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE85_ARP_RX_READY=1\r\n"u8))
            return false;

        for (uint poll = 0; poll != Phase83InterruptPollLimit &&
             _phase85ArpDeliveryCount != 4U; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out uint delivered, out uint rejected) ||
                rejected != 0 || delivered > 1)
                return false;
        }

        if (_phase85ArpDeliveryCount != 4U ||
            _phase83RxDescriptorCount != 4U || _phase85ReplyTxCount != 3U ||
            _ethernet.ReceivedFrameCount != 4U ||
            _ethernet.ParsedFrameCount != 4U ||
            _ethernet.MalformedFrameCount != 0U ||
            _ethernet.UnsupportedEtherTypeDropCount != 0U ||
            _ethernet.Phase84TestConsumerDeliveryCount != 0U ||
            _phase85RxEventCount != 4U || _rxIndex != 4U ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail,
                             out uint postedTail) || postedTail != 3U)
            return false;

        bool passed =
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_MANAGED_DELIVERIES=0x"u8,
                _phase85ArpDeliveryCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_DESCRIPTORS_COMPLETED=0x"u8,
                _phase83RxDescriptorCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ARP_REPLY_TX_RESULTS=0x"u8,
                _phase85ReplyTxCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ETHERNET_RECEIVED=0x"u8,
                _ethernet.ReceivedFrameCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_ETHERNET_PARSED=0x"u8,
                _ethernet.ParsedFrameCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_LAST_DESCRIPTOR_INDEX=0x"u8,
                _phase83LastDescriptorIndex) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_RDT_AFTER_RECYCLE=0x"u8,
                postedTail) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_TXDW_DELIVERIES=0x"u8,
                _phase85TxDwCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE85_RX_EVENT_DELIVERIES=0x"u8,
                _phase85RxEventCount) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE85_RX_DESCRIPTOR_RECYCLE_PASS=1\r\n"u8) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE85_HARDWARE_ARP_INGRESS_PASS=1\r\n"u8);
        _phase85ArpPassed = passed;
        _phase83Passed = passed;
        return passed;
    }

    internal bool TryRunPhase86Ipv4Proof()
    {
        if (!_phase86Ipv4Requested || !_phase80Requested || !_interruptSubscribed ||
            _state != ManagedE1000DriverState.Running ||
            _phase80DeliveryCount < 4 || _phase86Ipv4DeliveryCount != 0 ||
            _phase83RxDescriptorCount != 0 || _rxRing == null ||
            _rxBuffers == null || _ethernet == null || _mmio == null ||
            _ethernet.LocalIpv4Value != 0x0A0F0001U)
            return false;

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_GUEST_MAC=0x"u8, _macValue) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_LOCAL_IPV4=0x"u8,
                _ethernet.LocalIpv4Value) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE86_IPV4_RX_READY=1\r\n"u8))
            return false;

        for (uint poll = 0; poll != Phase83InterruptPollLimit &&
             _phase86Ipv4DeliveryCount != 4U; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out uint delivered, out uint rejected) ||
                rejected != 0 || delivered > 1)
                return false;
        }

        if (_phase86Ipv4DeliveryCount != 4U ||
            _phase83RxDescriptorCount != 4U ||
            _ethernet.Phase86Ipv4ParsedFrameCount != 4U ||
            _ethernet.Phase86Ipv4LocalDeliveryCount != 3U ||
            _ethernet.Phase86Ipv4NotForUsCount != 1U ||
            _ethernet.Phase86UnsupportedProtocolCount != 3U ||
            _ethernet.ReceivedFrameCount != 4U ||
            _ethernet.ParsedFrameCount != 4U ||
            _ethernet.MalformedFrameCount != 0U ||
            _ethernet.UnsupportedEtherTypeDropCount != 0U ||
            _ethernet.Phase84TestConsumerDeliveryCount != 0U ||
            _rxIndex != 4U ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail,
                             out uint postedTail) || postedTail != 3U)
            return false;

        bool passed =
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_IPV4_MANAGED_DELIVERIES=0x"u8,
                _phase86Ipv4DeliveryCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_IPV4_PARSED_FRAMES=0x"u8,
                _ethernet.Phase86Ipv4ParsedFrameCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_IPV4_LOCAL_DELIVERIES=0x"u8,
                _ethernet.Phase86Ipv4LocalDeliveryCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_IPV4_NOT_FOR_US_COUNT=0x"u8,
                _ethernet.Phase86Ipv4NotForUsCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_UNSUPPORTED_PROTOCOL_PACKETS=0x"u8,
                _ethernet.Phase86UnsupportedProtocolCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_RX_DESCRIPTORS_COMPLETED=0x"u8,
                _phase83RxDescriptorCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_ETHERNET_RECEIVED=0x"u8,
                _ethernet.ReceivedFrameCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_ETHERNET_PARSED=0x"u8,
                _ethernet.ParsedFrameCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_RX_RDT_AFTER_RECYCLE=0x"u8,
                postedTail) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE86_TXDW_DELIVERIES=0x"u8,
                _phase80DeliveryCount - 4U) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE86_RX_DESCRIPTOR_RECYCLE_PASS=1\r\n"u8) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE86_HARDWARE_IPV4_INGRESS_PASS=1\r\n"u8);
        _phase86Ipv4Passed = passed;
        _phase83Passed = passed;
        return passed;
    }

    private bool RecordPhase87IcmpDelivery(
        ManagedEthernetReceiveResult receiveResult,
        ManagedNetworkDispatchResult networkResult,
        uint descriptorIndex, byte status, byte errors, ushort length,
        ulong eventSequence)
    {
        uint ordinal = _phase87IcmpDeliveryCount + 1U;
        if (_ethernet == null || ordinal > 3U ||
            receiveResult != ManagedEthernetReceiveResult.Consumed ||
            networkResult != ManagedNetworkDispatchResult.IcmpResponderReplySent ||
            (status & (ManagedE1000Protocol.RxStatusDone |
                       ManagedE1000Protocol.RxStatusEop)) !=
                      (ManagedE1000Protocol.RxStatusDone |
                       ManagedE1000Protocol.RxStatusEop) ||
            errors != 0 || length != 60 || descriptorIndex != ordinal - 1U ||
            _ethernet.LastReceivedEtherType != ManagedIpv4Protocol.EtherType ||
            _ethernet.Phase87IcmpEchoRequestCount != ordinal)
            return false;

        ManagedIpv4Phase87Metadata metadata =
            _ethernet.Phase87IcmpLastMetadata;
        if (metadata.SourceIpv4 != 0x0A0F0002U ||
            metadata.DestinationIpv4 != 0x0A0F0001U ||
            metadata.TotalLength != 45 || metadata.Identifier != 0x8701 ||
            metadata.Sequence != ordinal || metadata.IcmpLength != 25 ||
            metadata.RequestTtl != 63 || metadata.ReplyTtl != 64 ||
            metadata.PayloadLength != 17 || !metadata.ReplyIcmpChecksumValid ||
            !metadata.ReplyIpv4ChecksumValid)
            return false;

        _phase87IcmpDeliveryCount = ordinal;
        return KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_ICMP_DELIVERY=0x"u8, ordinal) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_RX_EVENT_SEQUENCE=0x"u8,
                   eventSequence) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_RX_DESCRIPTOR_INDEX=0x"u8,
                   descriptorIndex) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_IPV4_TOTAL_LENGTH=0x"u8,
                   metadata.TotalLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_REQUEST_TTL=0x"u8,
                   metadata.RequestTtl) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_ICMP_REQUEST_CHECKSUM=0x"u8,
                   metadata.RequestIcmpChecksum) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_ICMP_REPLY_CHECKSUM=0x"u8,
                   metadata.ReplyIcmpChecksum) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_IPV4_REPLY_CHECKSUM=0x"u8,
                   metadata.ReplyIpv4Checksum) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_ICMP_IDENTIFIER=0x"u8,
                   metadata.Identifier) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_ICMP_SEQUENCE=0x"u8,
                   metadata.Sequence) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_ICMP_PAYLOAD_LENGTH=0x"u8,
                   metadata.PayloadLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_REPLY_TTL=0x"u8,
                   metadata.ReplyTtl) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE87_TX_INDEX=0x"u8,
                   _lastSubmittedTxIndex) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE87_TX_SUBMIT=1\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE87_TX_DD_POLL_COMPLETE=1\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE87_RX_DESCRIPTOR_RECYCLED=1\r\n"u8);
    }

    private bool RecordPhase89UdpDelivery(
        ManagedEthernetReceiveResult receiveResult,
        ManagedNetworkDispatchResult networkResult,
        uint descriptorIndex, byte status, byte errors, ushort length,
        ulong eventSequence)
    {
        if (_ethernet == null || _rxRing == null || _rxBuffers == null)
            return false;
        uint ordinal = _phase89UdpDeliveryCount + 1U;
        bool unbound = ordinal == 4U;
        if (ordinal > 4U || receiveResult != ManagedEthernetReceiveResult.Consumed ||
            networkResult != (unbound ? ManagedNetworkDispatchResult.Ignored
                : ManagedNetworkDispatchResult.UdpServiceReceived) ||
            (status & (ManagedE1000Protocol.RxStatusDone |
                       ManagedE1000Protocol.RxStatusEop)) !=
                (ManagedE1000Protocol.RxStatusDone |
                 ManagedE1000Protocol.RxStatusEop) || errors != 0 ||
            length != 60 || descriptorIndex != ordinal - 1U ||
            _ethernet.LastReceivedEtherType != ManagedIpv4Protocol.EtherType ||
            _ethernet.Phase89UdpParsedCount != ordinal ||
            _ethernet.Phase89UdpBoundCount != (unbound ? 3U : ordinal) ||
            _ethernet.Phase89UdpUnboundCount != (unbound ? 1U : 0U))
            return false;

        ManagedIpv4Phase89Metadata metadata = _ethernet.Phase89UdpLastMetadata;
        if (metadata.SourceIpv4 != 0x0A0F0002U + ordinal - 1U ||
            metadata.DestinationIpv4 != 0x0A0F0001U ||
            metadata.SourcePort != 24000 + ordinal ||
            metadata.DestinationPort != (unbound ? 15891 : 15890) ||
            metadata.UdpLength != 14 || metadata.UdpChecksum == 0 ||
            metadata.PayloadLength != 6 || metadata.Sequence != ordinal ||
            metadata.Bound == unbound)
            return false;

        byte[] copiedPayload = new byte[ManagedNetworkService.MaximumUdpPayloadLength];
        if (unbound)
        {
            if (_ethernet.NetworkService.HasReceivedUdp) return false;
        }
        else if (!_ethernet.TryReceivePhase89Udp(copiedPayload,
                     out Ipv4Address source, out ushort sourcePort,
                     out ushort destinationPort, out int payloadLength) ||
                 source.Value != metadata.SourceIpv4 ||
                 sourcePort != metadata.SourcePort ||
                 destinationPort != metadata.DestinationPort ||
                 payloadLength != 6 || copiedPayload[0] != 0x89 ||
                 copiedPayload[1] != ordinal || copiedPayload[2] != 0xA5 ||
                 copiedPayload[3] != (byte)(0x50 + ordinal) ||
                 copiedPayload[4] != 0xC3 || copiedPayload[5] != 0x3C)
            return false;

        if (!_rxRing.TryRead8((ulong)descriptorIndex *
                ManagedE1000Protocol.DescriptorSize + 12, out byte recycledStatus) ||
            recycledStatus != 0)
            return false;
        _phase89UdpDeliveryCount = ordinal;
        return KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_UDP_DELIVERY=0x"u8, ordinal) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_RX_EVENT_SEQUENCE=0x"u8, eventSequence) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_RX_DESCRIPTOR_INDEX=0x"u8,
                   descriptorIndex) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_SOURCE_PORT=0x"u8, metadata.SourcePort) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_DESTINATION_PORT=0x"u8,
                   metadata.DestinationPort) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_UDP_LENGTH=0x"u8, metadata.UdpLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_UDP_CHECKSUM=0x"u8,
                   metadata.UdpChecksum) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_PAYLOAD_LENGTH=0x"u8,
                   metadata.PayloadLength) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_SEQUENCE=0x"u8, metadata.Sequence) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_SOURCE_IPV4=0x"u8,
                   metadata.SourceIpv4) &&
               KernelLog.WriteHexLine(
                   "GXOS_NET10:PHASE89_DESTINATION_IPV4=0x"u8,
                   metadata.DestinationIpv4) &&
               KernelLog.Write(metadata.Bound
                   ? "GXOS_NET10:PHASE89_UDP_BOUND_DELIVERY=1\r\n"u8
                   : "GXOS_NET10:PHASE89_UDP_UNBOUND_IGNORED=1\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:PHASE89_RX_DESCRIPTOR_RECYCLED=1\r\n"u8);
    }

    internal bool TryRunPhase89UdpProof()
    {
        if (!_phase89UdpRequested || !_phase80Requested || !_interruptSubscribed ||
            _state != ManagedE1000DriverState.Running ||
            _phase80DeliveryCount < 4 || _phase89UdpDeliveryCount != 0 ||
            _phase83RxDescriptorCount != 0 || _rxRing == null ||
            _rxBuffers == null || _ethernet == null || _mmio == null ||
            _ethernet.LocalIpv4Value != 0x0A0F0001U)
            return false;

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_GUEST_MAC=0x"u8, _macValue) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_LOCAL_IPV4=0x"u8,
                _ethernet.LocalIpv4Value) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE89_UDP_RX_READY=1\r\n"u8))
            return false;

        for (uint poll = 0; poll != Phase83InterruptPollLimit &&
             _phase89UdpDeliveryCount != 4U; ++poll)
        {
            bool dispatched = ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                this, out uint delivered, out uint rejected);
            if (!dispatched || rejected != 0 || delivered > 1)
            {
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE89_DISPATCH_FAILURE=0x"u8,
                    dispatched ? (rejected != 0 ? 2U : 3U) : 1U);
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE89_DISPATCH_REJECTED=0x"u8,
                    rejected);
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE89_DISPATCH_DELIVERED=0x"u8,
                    delivered);
                return false;
            }
        }

        if (_phase89UdpDeliveryCount != 4U)
        {
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_RX_POLL_TIMEOUT_RXT0_EVENTS=0x"u8,
                _phase89UdpRxEventCount);
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_RX_POLL_TIMEOUT_DELIVERIES=0x"u8,
                _phase89UdpDeliveryCount);
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_RX_POLL_TIMEOUT_RX_INDEX=0x"u8,
                _rxIndex);
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_RX_POLL_TIMEOUT_PARSED=0x"u8,
                _ethernet.Phase89UdpParsedCount);
        }

        if (_phase89UdpDeliveryCount != 4U ||
            _phase83RxDescriptorCount != 4U ||
            _ethernet.Phase89UdpParsedCount != 4U ||
            _ethernet.Phase89UdpBoundCount != 3U ||
            _ethernet.Phase89UdpUnboundCount != 1U ||
            _ethernet.Phase89UdpValidCount != 4U ||
            _ethernet.Phase89UdpMalformedCount != 0U ||
            _ethernet.Phase89UdpChecksumFailureCount != 0U ||
            _ethernet.Phase89UdpUnknownPortCount != 1U ||
            _ethernet.Phase89UdpEndpointDispatchCount != 3U ||
            _ethernet.ReceivedFrameCount != 4U ||
            _ethernet.ParsedFrameCount != 4U ||
            _ethernet.MalformedFrameCount != 0U ||
            _ethernet.UnsupportedEtherTypeDropCount != 0U ||
            _rxIndex != 4U ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail,
                             out uint postedTail) || postedTail != 3U)
            return false;

        uint emptyBatches = 0;
        for (uint poll = 0; poll != 16U && emptyBatches != 3U; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out uint delivered, out uint rejected) || rejected != 0)
                return false;
            emptyBatches = delivered == 0 ? emptyBatches + 1U : 0U;
        }
        bool passed = emptyBatches == 3U &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_MANAGED_DELIVERIES=0x"u8,
                _phase89UdpDeliveryCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_RX_DESCRIPTORS_COMPLETED=0x"u8,
                _phase83RxDescriptorCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_PACKETS_PARSED=0x"u8,
                _ethernet.Phase89UdpParsedCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_VALID_PACKETS=0x"u8,
                _ethernet.Phase89UdpValidCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_RX_EVENTS=0x"u8,
                _phase89UdpRxEventCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_TXDW_EVENTS=0x"u8,
                _phase89UdpTxDwCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_BOUND_CLASSIFICATIONS=0x"u8,
                _ethernet.Phase89UdpBoundCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_UNBOUND_CLASSIFICATIONS=0x"u8,
                _ethernet.Phase89UdpUnboundCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_MALFORMED_DROPS=0x"u8,
                _ethernet.Phase89UdpMalformedCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_CHECKSUM_FAILURES=0x"u8,
                _ethernet.Phase89UdpChecksumFailureCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_UNKNOWN_PORTS=0x"u8,
                _ethernet.Phase89UdpUnknownPortCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_UDP_BOUND_CONSUMER_DELIVERIES=0x"u8,
                _ethernet.Phase89UdpEndpointDispatchCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE89_RX_RDT_AFTER_RECYCLE=0x"u8,
                postedTail) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE89_UDP_HARDWARE_INGRESS_PASS=1\r\n"u8);
        _phase89UdpPassed = passed;
        return passed;
    }

    internal bool TryRunPhase87IcmpProof()
    {
        if (!_phase87IcmpRequested || !_phase80Requested || !_interruptSubscribed ||
            _state != ManagedE1000DriverState.Running ||
            _phase80DeliveryCount < 4 || _phase87IcmpDeliveryCount != 0 ||
            _phase83RxDescriptorCount != 0 || _rxRing == null ||
            _rxBuffers == null || _ethernet == null || _mmio == null ||
            _ethernet.LocalIpv4Value != 0x0A0F0001U)
            return false;
        uint phase87TxDwBaseline = _phase87IcmpTxDwCount;

        if (!KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE87_GUEST_MAC=0x"u8, _macValue) ||
            !KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE87_LOCAL_IPV4=0x"u8,
                _ethernet.LocalIpv4Value) ||
            !KernelLog.Write(
                "GXOS_NET10:PHASE87_ICMP_RX_READY=1\r\n"u8))
            return false;

        for (uint poll = 0; poll != Phase83InterruptPollLimit &&
             _phase87IcmpDeliveryCount != 3U; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out uint delivered, out uint rejected) ||
                rejected != 0 || delivered > 1)
                return false;
        }

        if (_phase87IcmpDeliveryCount != 3U ||
            _phase83RxDescriptorCount != 3U ||
            _ethernet.Phase87IcmpEchoRequestCount != 3U ||
            _ethernet.ReceivedFrameCount != 3U ||
            _ethernet.ParsedFrameCount != 3U ||
            _ethernet.MalformedFrameCount != 0U ||
            _ethernet.UnsupportedEtherTypeDropCount != 0U ||
            _rxIndex != 3U ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail,
                             out uint postedTail) || postedTail != 2U)
            return false;

        /* Let any natural TXDW notifications produced by the three completed
           replies reach the existing typed E1000 route.  TX success itself
           remains the driver's descriptor-DD polling result. */
        for (uint poll = 0; poll != Phase83InterruptPollLimit &&
             _phase87IcmpTxDwCount < phase87TxDwBaseline + 3U; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out uint delivered, out uint rejected) || rejected != 0)
                return false;
            if (delivered == 0) continue;
        }
        if (_phase87IcmpTxDwCount < phase87TxDwBaseline + 3U) return false;
        uint phase87TxDwDeliveries =
            _phase87IcmpTxDwCount - phase87TxDwBaseline;

        uint emptyBatches = 0;
        for (uint poll = 0; poll != 16U && emptyBatches != 3U; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out uint delivered, out uint rejected) || rejected != 0)
                return false;
            emptyBatches = delivered == 0 ? emptyBatches + 1U : 0U;
        }
        if (emptyBatches != 3U) return false;

        bool passed =
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE87_ICMP_MANAGED_DELIVERIES=0x"u8,
                _phase87IcmpDeliveryCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE87_RX_DESCRIPTORS_COMPLETED=0x"u8,
                _phase83RxDescriptorCount) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE87_RX_RDT_AFTER_RECYCLE=0x"u8,
                postedTail) &&
            KernelLog.WriteHexLine(
                "GXOS_NET10:PHASE87_TXDW_DELIVERIES=0x"u8,
                phase87TxDwDeliveries) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE87_RX_DESCRIPTOR_RECYCLE_PASS=1\r\n"u8) &&
            KernelLog.Write(
                "GXOS_NET10:PHASE87_HARDWARE_ICMP_ECHO_PASS=1\r\n"u8);
        _phase87IcmpPassed = passed;
        _phase83Passed = passed;
        return passed;
    }

    internal bool TryRunPhase82Transmit()
    {
        ManagedSerialDriver? serial = ManagedSerialDriverSubsystem.Phase82SerialDriver;
        if (!_phase80Requested || !_phase80Passed || !_interruptSubscribed ||
            _state != ManagedE1000DriverState.Running || serial == null ||
            serial.State != ManagedSerialDriverState.Started ||
            (serial.ReceiveState != ManagedSerialReceiveState.Subscribed &&
             serial.ReceiveState != ManagedSerialReceiveState.NotSubscribed))
            return false;

        uint serialBefore = serial.ReceiveCount;
        uint e1000Before = _phase80DeliveryCount;
        uint repeat = ++_phase82TransmitSequence;
        if (!ManagedE1000Protocol.TryBuildPhase80Frame(
                _phase80Frame, _mac, repeat + 4U) ||
            !TryTransmitFrame(_phase80Frame, _phase80Frame.Length))
            return false;

        for (uint poll = 0; poll != Phase80InterruptPollLimit; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchPhase82Batch(
                    this, out uint delivered, out uint rejected)) return false;
            if (delivered == 0 && rejected == 0) continue;
            bool isolated = delivered == 1 && rejected == 0 &&
                _phase80DeliveryCount == e1000Before + 1U &&
                serial.ReceiveCount == serialBefore &&
                ManagedE1000Protocol.TryClassifyInterruptCause(
                    _phase80LastCause, out _phase80LastUnrelatedCause);
            return isolated &&
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE82_E1000_SEQUENCE=0x"u8,
                    _phase80LastSequence) &&
                KernelLog.WriteHexLine(
                    "GXOS_NET10:PHASE82_E1000_DELIVERIES=0x"u8,
                    _phase80DeliveryCount) &&
                KernelLog.Write(
                    "GXOS_NET10:PHASE82_E1000_TXDW_DELIVERED_ONLY=1\r\n"u8);
        }
        return false;
    }

    private bool TryWaitForPhase80Delivery(out uint delivered, out uint rejected)
    {
        delivered = 0;
        rejected = 0;
        for (uint poll = 0; poll != Phase80InterruptPollLimit; ++poll)
        {
            if (!ManagedSerialDriverSubsystem.TryDispatchE1000InterruptBatch(
                    this, out delivered, out rejected)) return false;
            if (delivered != 0 || rejected != 0)
                return delivered == 1 && rejected == 0;
        }
        return false;
    }

    internal bool TryReceiveProtocolFrame(byte[] frame, int capacity,
                                           uint pollLimit, out ushort length)
    {
        length = 0;
        if (_state != ManagedE1000DriverState.Running || _rxRing == null ||
            _rxBuffers == null || frame == null ||
            capacity < ManagedE1000Protocol.MinimumEthernetFrameLength ||
            capacity > frame.Length ||
            capacity > ManagedE1000Protocol.PacketBufferSize || pollLimit == 0)
            return false;

        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        for (uint spin = 0; spin != pollLimit; ++spin)
        {
            if (spin != 0 && spin % RxRearmInterval == 0 &&
                !WriteRegister(ManagedE1000Protocol.RegRxDescTail,
                               _rxIndex == 0
                                   ? ManagedE1000Protocol.RingCount - 1
                                   : _rxIndex - 1))
                return false;
            if (!_rxRing.TryRead(
                    (ulong)_rxIndex * ManagedE1000Protocol.DescriptorSize,
                    descriptor))
                return false;
            if ((descriptor[12] & ManagedE1000Protocol.RxStatusDone) == 0) continue;
            if (!ManagedE1000Protocol.TryReadRxDescriptor(
                    descriptor, _rxIndex, ManagedE1000Protocol.RingCount,
                    ManagedE1000Protocol.PacketBufferSize, out length,
                    out _, out _) || length > frame.Length)
            {
                KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_PROTOCOL_DESCRIPTOR_REJECTED\r\n"u8);
                if (!RecycleRxDescriptor(_rxIndex)) return false;
                length = 0;
                return true;
            }
            uint observedIndex = _rxIndex;
            ulong bufferOffset = (ulong)observedIndex * ManagedE1000Protocol.PacketBufferSize;
            if (length > capacity ||
                !_rxBuffers.TryRead(bufferOffset, frame.AsSpan(0, length)))
                return false;
            if (!RecycleRxDescriptor(observedIndex)) return false;
            return true;
        }
        return false;
    }

    internal bool TryVerifyProtocolGcSurvival()
    {
        ulong txRingBus = TxRingBusAddress;
        ulong rxRingBus = RxRingBusAddress;
        ulong txBufferBus = _txBuffers?.BusAddress ?? 0;
        ulong rxBufferBus = _rxBuffers?.BusAddress ?? 0;
        GC.Collect();
        GC.KeepAlive(_txRing);
        GC.KeepAlive(_rxRing);
        GC.KeepAlive(_txBuffers);
        GC.KeepAlive(_rxBuffers);
        return txRingBus == TxRingBusAddress && rxRingBus == RxRingBusAddress &&
               txBufferBus == (_txBuffers?.BusAddress ?? 0) &&
               rxBufferBus == (_rxBuffers?.BusAddress ?? 0) &&
               _mmio != null &&
               _mmio.TryRead32(ManagedE1000Protocol.RegStatus, out _);
    }

    private bool PollTxCompletionCore(uint index)
    {
        return _txRing != null && index < ManagedE1000Protocol.RingCount &&
               PollTxCompletionCoreLoop(index);
    }

    private bool PollTxCompletionCoreLoop(uint index)
    {
        for (uint spin = 0; spin != PollLimit; ++spin)
        {
            if (_txRing!.TryRead8(index * ManagedE1000Protocol.DescriptorSize + 12,
                                  out byte status) &&
                ManagedE1000Protocol.IsTxDescriptorStatusComplete(status))
                return true;
        }
        return false;
    }

    private bool RunGcSurvival()
    {
        ulong txBus = TxRingBusAddress;
        ulong rxBus = RxRingBusAddress;
        GC.Collect();
        GC.KeepAlive(_txRing);
        GC.KeepAlive(_rxRing);
        GC.KeepAlive(_txBuffers);
        GC.KeepAlive(_rxBuffers);
        return txBus == TxRingBusAddress && rxBus == RxRingBusAddress &&
               _mmio != null && _mmio.TryRead32(ManagedE1000Protocol.RegStatus,
                                                  out _) &&
               KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_GC_SURVIVAL_PASSED\r\n"u8);
    }

    private bool PollPhase15RxProof()
    {
        if (_rxRing == null || _rxBuffers == null) return false;
        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        Span<byte> frame = stackalloc byte[(int)ManagedE1000Protocol.PacketBufferSize];
        for (uint spin = 0; spin != RxPhase15PollLimit; ++spin)
        {
            /* QEMU's net queue can mark the NIC receive path disabled if a
               datagram arrives during a transient can_receive=false window.
               e1000e RDT writes call start_recv(), which clears that state and
               flushes the queued packet.  Re-post the unchanged owned tail at
               a bounded cadence while this single-buffer proof is pending. */
            if (spin != 0 && spin % RxRearmInterval == 0 &&
                !WriteRegister(ManagedE1000Protocol.RegRxDescTail,
                               ManagedE1000Protocol.RingCount - 1))
                return false;
            if (!_rxRing.TryRead(
                    (ulong)_rxIndex * ManagedE1000Protocol.DescriptorSize,
                    descriptor)) return false;
            if ((descriptor[12] & ManagedE1000Protocol.RxStatusDone) == 0) continue;
            if (!ManagedE1000Protocol.TryReadRxDescriptor(
                    descriptor, _rxIndex, ManagedE1000Protocol.RingCount,
                    ManagedE1000Protocol.PacketBufferSize, out ushort length,
                    out byte status, out byte errors))
            {
                KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_DESCRIPTOR_REJECTED\r\n"u8);
                return false;
            }
            if (!KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_COMPLETE\r\n"u8) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_DESCRIPTOR_INDEX=0x"u8,
                                        _rxIndex) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_LENGTH=0x"u8,
                                        length) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_STATUS=0x"u8,
                                        status) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_ERRORS=0x"u8,
                                        errors) ||
                !_rxBuffers.TryRead(
                    (ulong)_rxIndex * ManagedE1000Protocol.PacketBufferSize,
                    frame) ||
                !ManagedE1000Protocol.TryValidateRxTestFrame(
                    frame.Slice(0, length), _mac))
            {
                KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_FRAME_REJECTED\r\n"u8);
                return false;
            }
            _phase17Requested = ManagedE1000Protocol.IsPhase17RxTestFrame(
                frame.Slice(0, length));
            _phase18Requested = ManagedE1000Protocol.IsPhase18RxTestFrame(
                frame.Slice(0, length));
            _phase19Requested = ManagedE1000Protocol.IsPhase19RxTestFrame(
                frame.Slice(0, length));
            _phase20Requested = ManagedE1000Protocol.IsPhase20RxTestFrame(
                frame.Slice(0, length));
            _phase21Requested = ManagedE1000Protocol.IsPhase21RxTestFrame(
                frame.Slice(0, length));
            _phase22Requested = ManagedE1000Protocol.IsPhase22RxTestFrame(
                frame.Slice(0, length));
            _phase23Requested = ManagedE1000Protocol.IsPhase23RxTestFrame(
                frame.Slice(0, length));
            _phase32Requested = ManagedE1000Protocol.IsPhase32RxTestFrame(
                frame.Slice(0, length));
            _phase33Requested = ManagedE1000Protocol.IsPhase33RxTestFrame(
                frame.Slice(0, length));
            _phase34Requested = ManagedE1000Protocol.IsPhase34RxTestFrame(
                frame.Slice(0, length));
            if (_phase34Requested && !KernelLog.Write(
                    "GXOS_NET10:MANAGED_KERNEL_PHASE34_REQUESTED\r\n"u8))
                return false;
            if (_phase33Requested && !KernelLog.Write(
                    "GXOS_NET10:MANAGED_KERNEL_PHASE33_REQUESTED\r\n"u8))
                return false;
            if (_phase32Requested && !KernelLog.Write(
                    "GXOS_NET10:MANAGED_KERNEL_PHASE32_REQUESTED\r\n"u8))
                return false;
            if (!WriteRxStateSnapshot(
                    "GXOS_NET10:MANAGED_E1000_RX_STATE=AFTER_COMPLETION\r\n"u8) ||
                !RecycleRxDescriptor(_rxIndex)) return false;
            _rxProofReceived = 1;
            _rxPhase15Received = 1;
            return KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_RECEIVED\r\n"u8) &&
                KernelLog.Write(
                    "GXOS_NET10:MANAGED_E1000_RX_FRAME_OK\r\n"u8) &&
                KernelLog.Write(
                "GXOS_NET10:MANAGED_E1000_RX_RECYCLED\r\n"u8);
        }
        return WriteRxStateSnapshot(
                   "GXOS_NET10:MANAGED_E1000_RX_STATE=AFTER_TIMEOUT\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:MANAGED_KERNEL_PHASE14_RX_HARNESS_DEFERRED\r\n"u8) &&
               KernelLog.Write(
                   "GXOS_NET10:MANAGED_E1000_RX_HARNESS_DEFERRED\r\n"u8);
    }

    private bool ArmRxForExternalFrame()
    {
        if (_mmio == null) return false;
        for (uint spin = 0; spin != RxReadyPollLimit; ++spin)
        {
            if (!_mmio.TryRead32(ManagedE1000Protocol.RegStatus,
                                 out uint status) ||
                !_mmio.TryRead32(ManagedE1000Protocol.RegRctl,
                                 out uint rctl) ||
                !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescHead,
                                 out uint rdh) ||
                !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail,
                                 out uint rdt)) return false;
            if (!ManagedE1000Protocol.TryValidateRxReadyState(
                    status, rctl, rdh, rdt, ManagedE1000Protocol.RingCount))
                continue;

            if (!WriteRegister(ManagedE1000Protocol.RegRxDescTail,
                               ManagedE1000Protocol.RingCount - 1) ||
                !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail,
                                 out uint postedTail) ||
                postedTail != ManagedE1000Protocol.RingCount - 1)
                return false;
            return true;
        }
        return false;
    }

    private bool WriteRxStateSnapshot(ReadOnlySpan<byte> marker)
    {
        if (_mmio == null ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegStatus, out uint status) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRctl, out uint rctl) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDbaLow, out uint rdbal) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDbaHigh, out uint rdbah) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescLength, out uint rdlen) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescHead, out uint rdh) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRxDescTail, out uint rdt))
            return false;
        return KernelLog.Write(marker) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_MAC=0x"u8, _macValue) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_STATUS=0x"u8, status) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_RCTL=0x"u8, rctl) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_RDBAL=0x"u8, rdbal) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_RDBAH=0x"u8, rdbah) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_RDLEN=0x"u8, rdlen) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_RDH=0x"u8, rdh) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_E1000_RX_RDT=0x"u8, rdt);
    }

    private bool RecycleRxDescriptor(uint index)
    {
        if (_rxRing == null || _rxBuffers == null || index >= ManagedE1000Protocol.RingCount)
            return false;
        if (!ManagedE1000Protocol.TryAcceptRxDescriptorIndex(
                _rxIndex, index, ManagedE1000Protocol.RingCount, out uint nextIndex))
            return false;
        ulong bufferAddress = _rxBuffers.BusAddress +
                              (ulong)index * ManagedE1000Protocol.PacketBufferSize;
        Span<byte> descriptor = stackalloc byte[(int)ManagedE1000Protocol.DescriptorSize];
        if (bufferAddress < _rxBuffers.BusAddress ||
            !ManagedE1000Protocol.TryPrepareRxDescriptor(descriptor, bufferAddress) ||
            !_rxRing.TryWrite(
                (ulong)index * ManagedE1000Protocol.DescriptorSize, descriptor) ||
            !WriteRegister(ManagedE1000Protocol.RegRxDescTail, index)) return false;
        _rxIndex = nextIndex;
        return true;
    }

    private bool DisableEngines()
    {
        if (_mmio == null ||
            !WriteRegister(ManagedE1000Protocol.RegInterruptMaskClear,
                uint.MaxValue) ||
            !WriteRegister(ManagedE1000Protocol.RegRctl, 0) ||
            !WriteRegister(ManagedE1000Protocol.RegTctl, 0) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegCtrl, out uint control) ||
            !WriteRegister(ManagedE1000Protocol.RegCtrl,
                control | ManagedE1000Protocol.ControlReset)) return false;

        /* Intel CTRL.RST is self-clearing. Its completion is the device-side
           quiescence boundary: reset has stopped E1000 DMA before software
           disables PCI bus mastering and releases/reinitializes the rings. */
        for (uint poll = 0; poll != PollLimit; ++poll)
        {
            if (!_mmio.TryRead32(ManagedE1000Protocol.RegCtrl,
                                out uint resetControl)) return false;
            if ((resetControl & ManagedE1000Protocol.ControlReset) != 0)
                continue;
            return KernelLog.Write(
                       "GXOS_NET10:MANAGED_E1000_CTRL_RESET_COMPLETE=1\r\n"u8) &&
                   KernelLog.Write(
                       "GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_QUIESCED\r\n"u8) &&
                   (!_phase91Requested || KernelLog.Write(
                       "GXOS_NET10:PHASE91_STOP_HARDWARE_RESET_COMPLETE=1\r\n"u8));
        }
        return false;
    }

    private bool DisableBusMaster()
    {
        if (!_pciCommandLive) return true;
        if (!ManagedKernelContract.TryPciCommandDisableBusMaster(
                _resource.ResourceId, _claimHandle, DriverId,
                out GxManagedKernelPciCommandResultV1 disabled) ||
            (disabled.ResultingCommand &
             ManagedE1000Protocol.PciCommandBusMaster) != 0)
            return false;
        _busMasterDisabled = true;
        return KernelLog.Write(
                   "GXOS_NET10:MANAGED_E1000_PCI_BUS_MASTER_DISABLED=1\r\n"u8) &&
               (!_phase91Requested || KernelLog.Write(
                   "GXOS_NET10:PHASE91_STOP_BUS_MASTER_DISABLED=1\r\n"u8));
    }

    private bool ReleaseDmaAndRestorePci()
    {
        if (_pciCommandLive && !_busMasterDisabled) return false;
        ManagedDmaAllocation?[] allocations =
            { _txRing, _rxRing, _txBuffers, _rxBuffers };
        foreach (ManagedDmaAllocation? allocation in allocations)
        {
            if (allocation == null || !allocation.TryReleaseReference()) return false;
        }
        foreach (ManagedDmaAllocation? allocation in allocations)
        {
            if (allocation == null || !allocation.TryRelease()) return false;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_RELEASED\r\n"u8) ||
            !ManagedKernelContract.TryPciCommandRestore(
                _resource.ResourceId, _claimHandle, DriverId, _originalCommand,
                out GxManagedKernelPciCommandResultV1 restored) ||
            restored.ResultingCommand != _originalCommand ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_PCI_COMMAND_RESTORED\r\n"u8))
            return false;
        _txRing = null;
        _rxRing = null;
        _txBuffers = null;
        _rxBuffers = null;
        _pciCommandLive = false;
        _busMasterDisabled = false;
        return true;
    }

    private bool WriteRegister(ulong offset, uint value)
    {
        return _mmio != null && _mmio.TryWrite32(offset, value);
    }

    private bool RunMmioWriteNegativeTests()
    {
        if (_mmio == null ||
            !ManagedDeviceResourceRuntimeCatalog.TryMap(
                in _resource, DriverId, 0, 0x10, 1,
                out ManagedMmioMapping? readOnly) ||
            readOnly == null ||
            readOnly.TryWrite32(0, 0) ||
            readOnly.TryWrite32(0x10, 0) ||
            !readOnly.TryUnmap() ||
            new ManagedMmioMapping(_resource.ResourceId, DriverId + 1,
                                   _mmio.Handle, 4, 3).TryWrite32(0, 0) ||
            new ManagedMmioMapping(_resource.ResourceId, DriverId,
                                   0xFFFFFFFF00000001, 4, 3).TryWrite32(0, 0))
            return false;
        return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_MMIO_WRITE_NEGATIVE_TESTS_OK\r\n"u8);
    }

    private bool WriteRingBase(ulong lowOffset, ulong highOffset, ulong address)
    {
        return WriteRegister(lowOffset, (uint)address) &&
               WriteRegister(highOffset, (uint)(address >> 32));
    }

    private static void Write64(Span<byte> bytes, ulong value)
    {
        for (int index = 0; index != 8; ++index)
            bytes[index] = (byte)(value >> (index * 8));
    }
}

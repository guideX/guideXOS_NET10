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
    /* The host observes RX_READY over a serial socket before sending the one
       frame.  Keep this bounded window long enough for that handshake without
       adding a wall-clock sleep to the guest. */
    private const uint RxPhase15PollLimit = 1000000000;
    private const uint RxReadyPollLimit = 5000000;
    private const uint RxRearmInterval = 64;
    internal static uint Phase16MacHigh;
    internal static uint Phase16MacLow;

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
    private uint _originalCommand;
    private uint _resultingCommand;
    private bool _pciCommandLive;
    private uint _txIndex;
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
    private ManagedE1000DriverState _state;

    private ManagedE1000Driver(in ManagedDevice device)
    {
        _device = device;
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
        return new ManagedE1000Driver(in device);
    }

    internal bool TryStart()
    {
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
        if (!SubmitProofFrame() || !PollTxCompletion()) return AbortStart();
        _state = ManagedE1000DriverState.Running;
        if (!RunGcSurvival()) return AbortStart();
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE15_GC_SURVIVAL_PASSED\r\n"u8) ||
            !ArmRxForExternalFrame() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_CONFIGURED\r\n"u8) ||
            !WriteRxStateSnapshot(
                "GXOS_NET10:MANAGED_E1000_RX_STATE=BEFORE_INJECTION\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_E1000_RX_READY\r\n"u8))
            return AbortStart();
        if (!PollPhase15RxProof()) return AbortStart();
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
            if (_phase34Requested)
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
        bool safe = true;
        if (_ethernet != null)
        {
            safe = _ethernet.TryStop() && safe;
            _ethernet = null;
        }
        if (_state >= ManagedE1000DriverState.Initialized && _mmio != null)
            safe = WriteRegister(ManagedE1000Protocol.RegRctl, 0) &&
                   WriteRegister(ManagedE1000Protocol.RegTctl, 0) && safe;
        if (_state >= ManagedE1000DriverState.Initialized && _txRing != null)
            safe = _txRing.TryRead8(12, out byte txStatus) &&
                   (txStatus & ManagedE1000Protocol.TxStatusDone) != 0 && safe;
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
            if (!ManagedKernelContract.TryPciCommandDisableBusMaster(
                    _resource.ResourceId, _claimHandle, DriverId,
                    out _) ||
                !ManagedKernelContract.TryPciCommandRestore(
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
        return false;
    }

    internal bool TryStop()
    {
        if (_state != ManagedE1000DriverState.Running) return false;
        _state = ManagedE1000DriverState.Stopping;
        ManagedEthernetLayer? ethernet = (_phase33Requested || _phase32Requested || _phase23Requested || _phase22Requested || _phase21Requested)
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
        }
        bool result = (ethernet == null || ethernet.TryStop()) &&
                      DisableEngines() && ReleaseDmaAndRestorePci();
        if (!result) return false;
        _ethernet = null;
        if (_mmio != null && _mmio.IsLive && !_mmio.TryUnmap()) return false;
        _mmio = null;
        if (!ManagedDeviceResourceRuntimeCatalog.TryRelease(in _resource, DriverId))
            return false;
        _claimHandle = 0;
        _nativeClaimHandle = 0;
        _state = ManagedE1000DriverState.Stopped;
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
        if (_mmio == null || !_mmio.TryRead32(ManagedE1000Protocol.RegRal, out uint ral) ||
            !_mmio.TryRead32(ManagedE1000Protocol.RegRah, out uint rah)) return false;
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
        return !ManagedE1000Protocol.IsInvalidMac(_mac) &&
               KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PHASE14_MAC=0x"u8,
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
            return false;
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
            temporary.TryRead8(0, out _) ||
            !RunDmaCapacityNegativeTest() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_DMA_NEGATIVE_TESTS_OK\r\n"u8))
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

    internal bool TryTransmitFrame(byte[] frame, int length)
    {
        if (_state != ManagedE1000DriverState.Running || _txRing == null ||
            _txBuffers == null || frame == null || length <= 0 ||
            length > frame.Length ||
            length > ManagedE1000Protocol.PacketBufferSize ||
            _txIndex >= ManagedE1000Protocol.RingCount)
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
        if (!WriteRegister(ManagedE1000Protocol.RegTxDescTail, nextIndex))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_TAIL_WRITE_FAILED\r\n"u8);
            return false;
        }
        if (!PollTxCompletionCore(_txIndex))
        {
            KernelLog.Write("GXOS_NET10:MANAGED_E1000_TX_PROTOCOL_COMPLETION_TIMEOUT\r\n"u8);
            return false;
        }
        _txIndex = nextIndex;
        return true;
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
                !_rxBuffers.TryRead(bufferOffset, frame.AsSpan(0, length)) ||
                !RecycleRxDescriptor(observedIndex))
                return false;
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
                (status & ManagedE1000Protocol.TxStatusDone) != 0)
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
        if (_mmio == null || !WriteRegister(ManagedE1000Protocol.RegRctl, 0) ||
            !WriteRegister(ManagedE1000Protocol.RegTctl, 0) ||
            _txRing == null || !_txRing.TryRead8(12, out byte txStatus) ||
            (txStatus & ManagedE1000Protocol.TxStatusDone) == 0) return false;
        return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_NIC_QUIESCED\r\n"u8);
    }

    private bool ReleaseDmaAndRestorePci()
    {
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
            !ManagedKernelContract.TryPciCommandDisableBusMaster(
                _resource.ResourceId, _claimHandle, DriverId,
                out GxManagedKernelPciCommandResultV1 disabled) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE14_BUS_MASTER_DISABLED\r\n"u8) ||
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

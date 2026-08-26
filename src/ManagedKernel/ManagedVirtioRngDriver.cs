using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedVirtioRngDriverState : uint
{
    Created = 0,
    Claimed = 1,
    Mapped = 2,
    QueueReady = 3,
    Running = 4,
    Stopped = 5,
    Failed = 6
}

/* The Phase 26 driver intentionally implements only modern virtio-rng. It
   uses no interrupt path: secure-random requests are small and infrequent,
   so a bounded used-ring poll keeps the lifecycle and teardown auditable.

   The driver is single-owner in this phase. Transport state is kept as
   scalar native handles and addresses; the native MMIO/DMA services remain
   authoritative and no heap-backed mapping or DMA wrapper is retained by the
   provider across a managed collection. */
internal unsafe struct ManagedVirtioRngDriver : IManagedEntropyProvider
{
    private static ManagedDevice s_device;
    private static ManagedVirtioPciCapabilities s_capabilities;
    private static ManagedDeviceResource s_commonResource;
    private static ManagedDeviceResource s_notifyResource;
    private static ulong s_commonMapping;
    private static ulong s_notifyMapping;
    private static ulong s_commonMappingLength;
    private static ulong s_notifyMappingLength;
    private static ulong s_queueHandle;
    private static ulong s_queueBusAddress;
    private static ulong s_queueLength;
    private static ulong s_bufferHandle;
    private static ulong s_bufferBusAddress;
    private static ulong s_bufferLength;
    private static ulong s_commonClaim;
    private static ulong s_notifyClaim;
    private static bool s_commonClaimLive;
    private static bool s_notifyClaimLive;
    private static bool s_commonMappingLive;
    private static bool s_notifyMappingLive;
    private static bool s_queueLive;
    private static bool s_queueRetained;
    private static bool s_bufferLive;
    private static bool s_bufferRetained;
    private static bool s_pciCommandLive;
    private static uint s_originalPciCommand;
    private static bool s_healthy;
    private static ushort s_availableIndex;
    private static ushort s_usedIndex;
    private static ManagedVirtioRngDriverState s_state;

    internal static bool LastProbeFoundDevice { get; private set; }
    internal static bool LastProbeRejectedDevice { get; private set; }

    internal ManagedVirtioRngDriverState State => s_state;
    public bool IsAvailable => s_state == ManagedVirtioRngDriverState.Running && s_healthy;
    internal ManagedDevice Device => s_device;

    internal static ManagedVirtioRngDriver? TryCreate()
    {
        LastProbeFoundDevice = false;
        LastProbeRejectedDevice = false;
        ManagedDeviceInventory? inventory =
            ManagedKernelContract.OperationalDeviceInventory;
        if (!ManagedKernelContract.IsStarted ||
            !ManagedKernelContract.DeviceResourcesInstalled ||
            !ManagedKernelContract.PciServicesInstalled ||
            !ManagedKernelContract.MmioServicesInstalled ||
            !ManagedKernelContract.DmaServicesInstalled || inventory == null)
            return null;

        for (uint index = 0; index != inventory.DeviceCount; ++index)
        {
            if (!inventory.TryGetDevice(index, out ManagedDevice device) ||
                device.VendorId != ManagedVirtioRngProtocol.VirtioVendorId)
                continue;
            if (device.DeviceId == ManagedVirtioRngProtocol.TransitionalRngDeviceId)
            {
                LastProbeFoundDevice = true;
                LastProbeRejectedDevice = true;
                KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_TRANSITIONAL_REJECTED\r\n"u8);
                continue;
            }
            if (device.DeviceId != ManagedVirtioRngProtocol.ModernRngDeviceId)
                continue;
            LastProbeFoundDevice = true;
            KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_PCI_DISCOVERED\r\n"u8);
            KernelLog.WriteHexLine("GXOS_NET10:MANAGED_VIRTIO_RNG_BDF=0x"u8,
                                  ((ulong)device.Segment << 32) |
                                  ((ulong)device.Bus << 24) |
                                  ((ulong)device.Device << 16) |
                                  ((ulong)device.Function << 8));
            KernelLog.WriteHexLine("GXOS_NET10:MANAGED_VIRTIO_RNG_DEVICE_ID=0x"u8,
                                  ((ulong)device.VendorId << 16) | device.DeviceId);
            s_device = device;
            s_capabilities = default;
            s_commonResource = default;
            s_notifyResource = default;
            s_commonMapping = 0;
            s_notifyMapping = 0;
            s_commonMappingLength = 0;
            s_notifyMappingLength = 0;
            s_queueHandle = 0;
            s_queueBusAddress = 0;
            s_queueLength = 0;
            s_bufferHandle = 0;
            s_bufferBusAddress = 0;
            s_bufferLength = 0;
            s_commonClaim = 0;
            s_notifyClaim = 0;
            s_commonClaimLive = false;
            s_notifyClaimLive = false;
            s_commonMappingLive = false;
            s_notifyMappingLive = false;
            s_queueLive = false;
            s_queueRetained = false;
            s_bufferLive = false;
            s_bufferRetained = false;
            s_pciCommandLive = false;
            s_originalPciCommand = 0;
            s_healthy = false;
            s_availableIndex = 0;
            s_usedIndex = 0;
            s_state = ManagedVirtioRngDriverState.Created;
            return new ManagedVirtioRngDriver();
        }
        return null;
    }

    internal bool TryStart()
    {
        if (s_state != ManagedVirtioRngDriverState.Created ||
            !TryReadModernCapabilities() || !TryClaimAndMap() ||
            !TryEnablePciCommand() || !TryInitializeDevice())
            return AbortStart();
        s_state = ManagedVirtioRngDriverState.Running;
        s_healthy = true;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_QUEUE_CONFIGURED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_TRANSPORT_READY\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_PROVIDER_AVAILABLE\r\n"u8))
            return AbortStart();
        return true;
    }

    internal bool TryStop()
    {
        if (s_state == ManagedVirtioRngDriverState.Stopped) return true;
        if (s_state != ManagedVirtioRngDriverState.Running &&
            s_state != ManagedVirtioRngDriverState.QueueReady &&
            s_state != ManagedVirtioRngDriverState.Mapped &&
            s_state != ManagedVirtioRngDriverState.Claimed)
            return false;
        bool statusReset = !s_commonMappingLive || TryCommonWrite8(
            ManagedVirtioRngProtocol.CommonDeviceStatus, 0);
        bool cleaned = Cleanup(statusReset);
        if (cleaned)
        {
            s_state = ManagedVirtioRngDriverState.Stopped;
            KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_TEARDOWN_SUCCEEDED\r\n"u8);
        }
        else s_state = ManagedVirtioRngDriverState.Failed;
        return cleaned;
    }

    public bool TryFill(Span<byte> destination)
    {
        if (!IsAvailable || destination.Length >
                ManagedVirtioRngProtocol.MaximumRequestBytes)
            return false;
        if (destination.Length == 0) return true;
        int offset = 0;
        while (offset != destination.Length)
        {
            int requested = Math.Min(
                (int)ManagedVirtioRngProtocol.MaximumRequestBytes,
                destination.Length - offset);
            if (!TrySubmitAndComplete(requested,
                                       destination.Slice(offset, requested),
                                       out int completed))
            {
                destination.Clear();
                s_healthy = false;
                return false;
            }
            offset += completed;
        }
        return true;
    }

    private static bool TryReadModernCapabilities()
    {
        Span<byte> configuration = stackalloc byte[256];
        for (uint offset = 0; offset != configuration.Length; ++offset)
        {
            if (!PciConfiguration.TryRead8(in s_device, offset,
                                           out configuration[(int)offset]))
                return FailProbe("GXOS_NET10:MANAGED_VIRTIO_RNG_PCI_READ_FAILED\r\n"u8);
        }
        if (!ManagedVirtioRngProtocol.TryParseCapabilities(
                configuration, out s_capabilities))
            return FailProbe("GXOS_NET10:MANAGED_VIRTIO_RNG_CAPABILITY_AUDIT_FAILED\r\n"u8);
        if (!PciConfiguration.TryRead16(in s_device, 0x2C,
                                        out ushort subsystemVendor) ||
            !PciConfiguration.TryRead16(in s_device, 0x2E,
                                        out ushort subsystemDevice))
            return FailProbe("GXOS_NET10:MANAGED_VIRTIO_RNG_SUBSYSTEM_READ_FAILED\r\n"u8);
        KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_TRANSPORT=MODERN_NON_TRANSITIONAL\r\n"u8);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_VIRTIO_RNG_COMMON_BAR=0x"u8,
                              s_capabilities.CommonBar);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_VIRTIO_RNG_NOTIFY_BAR=0x"u8,
                              s_capabilities.NotifyBar);
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_VIRTIO_RNG_SUBSYSTEM=0x"u8,
                              ((ulong)subsystemVendor << 16) | subsystemDevice);
        return subsystemVendor == ManagedVirtioRngProtocol.QemuVirtioSubsystemVendorId &&
               subsystemDevice == ManagedVirtioRngProtocol.QemuVirtioSubsystemDeviceId ||
               FailProbe("GXOS_NET10:MANAGED_VIRTIO_RNG_SUBSYSTEM_UNSUPPORTED\r\n"u8);
    }

    private static bool TryClaimAndMap()
    {
        if (!ManagedDeviceResourceRuntimeCatalog.TryFindByOwner(
                GxManagedKernelDeviceV1.DeviceKindPci,
                ManagedVirtioRngProtocol.PciOwnerId, s_capabilities.CommonBar,
                out s_commonResource) ||
            s_commonResource.ResourceType !=
                GxManagedKernelDeviceResourceV1.ResourceTypeMmio ||
            !ManagedDeviceResourceRuntimeCatalog.TryClaim(
                in s_commonResource, ManagedVirtioRngProtocol.DriverId,
                GxManagedKernelDeviceV1.DeviceKindPci,
                ManagedVirtioRngProtocol.PciOwnerId) ||
            !ManagedDeviceResourceRuntimeCatalog.TryGetNativeClaimHandle(
                in s_commonResource, ManagedVirtioRngProtocol.DriverId,
                out s_commonClaim))
            return FailProbe("GXOS_NET10:MANAGED_VIRTIO_RNG_COMMON_RESOURCE_FAILED\r\n"u8);
        s_commonClaimLive = true;
        s_notifyResource = s_commonResource;
        s_notifyClaim = s_commonClaim;
        s_notifyClaimLive = true;
        if (s_capabilities.NotifyBar != s_capabilities.CommonBar)
        {
            if (!ManagedDeviceResourceRuntimeCatalog.TryFindByOwner(
                    GxManagedKernelDeviceV1.DeviceKindPci,
                    ManagedVirtioRngProtocol.PciOwnerId, s_capabilities.NotifyBar,
                    out s_notifyResource) ||
                !ManagedDeviceResourceRuntimeCatalog.TryClaim(
                    in s_notifyResource, ManagedVirtioRngProtocol.DriverId,
                    GxManagedKernelDeviceV1.DeviceKindPci,
                    ManagedVirtioRngProtocol.PciOwnerId) ||
                !ManagedDeviceResourceRuntimeCatalog.TryGetNativeClaimHandle(
                    in s_notifyResource, ManagedVirtioRngProtocol.DriverId,
                    out s_notifyClaim))
                return FailProbe("GXOS_NET10:MANAGED_VIRTIO_RNG_NOTIFY_RESOURCE_FAILED\r\n"u8);
            s_notifyClaimLive = true;
        }
        if (s_capabilities.CommonOffset > s_commonResource.Length ||
            s_capabilities.CommonLength > s_commonResource.Length -
                s_capabilities.CommonOffset ||
            s_capabilities.NotifyOffset > s_notifyResource.Length ||
            s_capabilities.NotifyLength > s_notifyResource.Length -
                s_capabilities.NotifyOffset ||
            !ManagedDeviceResourceRuntimeCatalog.TryMapHandle(
                in s_commonResource, ManagedVirtioRngProtocol.DriverId,
                s_capabilities.CommonOffset, s_capabilities.CommonLength, 3,
                out s_commonMapping) ||
            !ManagedDeviceResourceRuntimeCatalog.TryMapHandle(
                in s_notifyResource, ManagedVirtioRngProtocol.DriverId,
                s_capabilities.NotifyOffset, s_capabilities.NotifyLength, 3,
                out s_notifyMapping))
            return FailProbe("GXOS_NET10:MANAGED_VIRTIO_RNG_CAPABILITY_MAPPING_FAILED\r\n"u8);
        s_commonMappingLength = s_capabilities.CommonLength;
        s_notifyMappingLength = s_capabilities.NotifyLength;
        s_commonMappingLive = true;
        s_notifyMappingLive = true;
        s_state = ManagedVirtioRngDriverState.Mapped;
        return true;
    }

    private static bool TryEnablePciCommand()
    {
        if (!ManagedKernelContract.TryPciCommandEnable(
                s_commonResource.ResourceId, s_commonClaim,
                ManagedVirtioRngProtocol.DriverId, 0x6,
                out GxManagedKernelPciCommandResultV1 result))
            return false;
        s_originalPciCommand = result.OriginalCommand;
        s_pciCommandLive = true;
        return (result.ResultingCommand & 0x6) == 0x6;
    }

    private static bool TryInitializeDevice()
    {
        if (!s_commonMappingLive || !s_notifyMappingLive ||
            !TryCommonWrite8(ManagedVirtioRngProtocol.CommonDeviceStatus, 0) ||
            !SetStatus(ManagedVirtioRngProtocol.StatusAcknowledge) ||
            !SetStatus(ManagedVirtioRngProtocol.StatusDriver) ||
            !TryCommonWrite32(ManagedVirtioRngProtocol.CommonDeviceFeatureSelect, 0) ||
            !TryCommonRead32(ManagedVirtioRngProtocol.CommonDeviceFeature, out _) ||
            !TryCommonWrite32(ManagedVirtioRngProtocol.CommonDriverFeatureSelect, 0) ||
            !TryCommonWrite32(ManagedVirtioRngProtocol.CommonDriverFeature, 0) ||
            !TryCommonWrite32(ManagedVirtioRngProtocol.CommonDriverFeatureSelect, 1) ||
            !TryCommonWrite32(ManagedVirtioRngProtocol.CommonDriverFeature, 0) ||
            !SetStatus(ManagedVirtioRngProtocol.StatusFeaturesOk) ||
            !TryCommonRead8(ManagedVirtioRngProtocol.CommonDeviceStatus,
                            out byte status) ||
            (status & ManagedVirtioRngProtocol.StatusFeaturesOk) == 0 ||
            (status & ManagedVirtioRngProtocol.StatusFailed) != 0 ||
            !TryCommonWrite16(ManagedVirtioRngProtocol.CommonQueueSelect, 0) ||
            !TryCommonRead16(ManagedVirtioRngProtocol.CommonQueueSize,
                             out ushort deviceQueueSize) ||
            deviceQueueSize < ManagedVirtioRngProtocol.QueueSize ||
            !TryCommonWrite16(ManagedVirtioRngProtocol.CommonQueueSize,
                              (ushort)ManagedVirtioRngProtocol.QueueSize))
            return false;

        if (!TryAllocateDma(ManagedVirtioRngProtocol.QueuePageBytes,
                            out s_queueHandle, out s_queueBusAddress,
                            out s_queueLength))
            return false;
        s_queueLive = true;
        s_queueRetained = true;
        if (!TryAllocateDma(ManagedVirtioRngProtocol.EntropyBufferBytes,
                            out s_bufferHandle, out s_bufferBusAddress,
                            out s_bufferLength))
            return false;
        s_bufferLive = true;
        s_bufferRetained = true;
        if (!ClearDma(s_queueHandle, s_queueLength,
                      ManagedVirtioRngProtocol.QueuePageBytes) ||
            !ClearDma(s_bufferHandle, s_bufferLength,
                      ManagedVirtioRngProtocol.EntropyBufferBytes) ||
            !TryCommonWrite64(ManagedVirtioRngProtocol.CommonQueueDescriptor,
                              s_queueBusAddress +
                              ManagedVirtioRngProtocol.DescriptorTableOffset) ||
            !TryCommonWrite64(ManagedVirtioRngProtocol.CommonQueueAvailable,
                              s_queueBusAddress +
                              ManagedVirtioRngProtocol.AvailableRingOffset) ||
            !TryCommonWrite64(ManagedVirtioRngProtocol.CommonQueueUsed,
                              s_queueBusAddress +
                              ManagedVirtioRngProtocol.UsedRingOffset) ||
            !TryCommonWrite16(ManagedVirtioRngProtocol.CommonQueueEnable, 1) ||
            !SetStatus(ManagedVirtioRngProtocol.StatusDriverOk))
            return false;
        s_availableIndex = 0;
        s_usedIndex = 0;
        s_state = ManagedVirtioRngDriverState.QueueReady;
        return true;
    }

    private static bool TrySubmitAndComplete(int requested, Span<byte> destination,
                                             out int completed)
    {
        completed = 0;
        if (!s_queueLive || !s_bufferLive || !s_commonMappingLive ||
            !s_notifyMappingLive || requested <= 0 || requested >
                ManagedVirtioRngProtocol.MaximumRequestBytes)
            return false;
        Span<byte> descriptor = stackalloc byte[16];
        descriptor.Clear();
        Write64(descriptor, s_bufferBusAddress);
        Write32(descriptor, 8, (uint)requested);
        Write16(descriptor[12..],
                (ushort)ManagedVirtioRngProtocol.VirtqueueDescriptorWrite);
        if (!TryDmaWrite(s_queueHandle, s_queueLength,
                         ManagedVirtioRngProtocol.DescriptorTableOffset,
                         descriptor) ||
            !TryDmaWrite16(s_queueHandle, s_queueLength,
                           ManagedVirtioRngProtocol.AvailableRingOffset + 4 +
                           (uint)((s_availableIndex % ManagedVirtioRngProtocol.QueueSize) * 2), 0) ||
            !TryDmaWrite16(s_queueHandle, s_queueLength,
                           ManagedVirtioRngProtocol.AvailableRingOffset + 2,
                           (ushort)(s_availableIndex + 1)) ||
            !TryCommonRead16(ManagedVirtioRngProtocol.CommonQueueNotifyOffset,
                             out ushort notifyOffset) ||
            !Notify((uint)notifyOffset))
            return false;
        s_availableIndex++;

        for (uint spin = 0; spin != ManagedVirtioRngProtocol.PollLimit; ++spin)
        {
            if (TryCommonRead8(ManagedVirtioRngProtocol.CommonDeviceStatus,
                               out byte status) &&
                (status & ManagedVirtioRngProtocol.StatusFailed) != 0)
                return false;
            if (!TryDmaRead16(s_queueHandle, s_queueLength,
                              ManagedVirtioRngProtocol.UsedRingOffset + 2,
                              out ushort usedIndex))
                return false;
            ushort delta = (ushort)(usedIndex - s_usedIndex);
            if (delta == 0) continue;
            if (delta != 1) return false;
            ulong usedElementOffset = ManagedVirtioRngProtocol.UsedRingOffset +
                4 + (ulong)((s_usedIndex % ManagedVirtioRngProtocol.QueueSize) * 8);
            if (!TryDmaRead32(s_queueHandle, s_queueLength, usedElementOffset,
                              out uint descriptorId) ||
                !TryDmaRead32(s_queueHandle, s_queueLength, usedElementOffset + 4,
                              out uint length) ||
                descriptorId != 0 || length == 0 || length > (uint)requested ||
                length > ManagedVirtioRngProtocol.EntropyBufferBytes)
                return false;
            s_usedIndex = usedIndex;
            if (!TryDmaRead(s_bufferHandle, s_bufferLength, 0,
                            destination.Slice(0, (int)length))) return false;
            completed = (int)length;
            KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_VIRTIO_RNG_REQUEST_COMPLETE_REQUESTED=0x"u8,
                (ulong)requested);
            KernelLog.WriteHexLine(
                "GXOS_NET10:MANAGED_VIRTIO_RNG_REQUEST_COMPLETE_COMPLETED=0x"u8,
                (ulong)completed);
            return true;
        }
        KernelLog.Write("GXOS_NET10:MANAGED_VIRTIO_RNG_POLL_TIMEOUT\r\n"u8);
        return false;
    }

    private static bool Notify(uint queueNotifyOffset)
    {
        ulong offset = (ulong)queueNotifyOffset *
                       s_capabilities.NotifyMultiplier;
        return offset <= ulong.MaxValue - 2 &&
               offset + 2 <= s_notifyMappingLength &&
               TryNotifyWrite16(offset, 0);
    }

    private static bool SetStatus(byte bits)
    {
        if (!TryCommonRead8(ManagedVirtioRngProtocol.CommonDeviceStatus,
                            out byte current)) return false;
        return TryCommonWrite8(ManagedVirtioRngProtocol.CommonDeviceStatus,
                               (byte)(current | bits));
    }

    private static bool TryAllocateDma(ulong bytes, out ulong handle,
                                       out ulong busAddress, out ulong length)
    {
        handle = 0;
        busAddress = 0;
        length = 0;
        if (!ManagedKernelContract.TryDmaAllocate(
                s_commonClaim, ManagedVirtioRngProtocol.DriverId, bytes, 4096,
                out GxManagedKernelDmaAllocationResultV1 result) ||
            !ManagedKernelContract.TryDmaRetain(
                result.Handle, ManagedVirtioRngProtocol.DriverId))
        {
            if (result.Handle != 0)
                ManagedKernelContract.TryDmaRelease(
                    result.Handle, ManagedVirtioRngProtocol.DriverId);
            return false;
        }
        handle = result.Handle;
        busAddress = result.BusAddress;
        length = result.ByteLength;
        return true;
    }

    private static bool ClearDma(ulong handle, ulong capacity, ulong length)
    {
        if (length > capacity) return false;
        Span<byte> zeros = stackalloc byte[256];
        zeros.Clear();
        for (ulong offset = 0; offset != length; offset += (ulong)zeros.Length)
        {
            int count = (int)Math.Min((ulong)zeros.Length, length - offset);
            if (!TryDmaWrite(handle, capacity, offset, zeros[..count])) return false;
        }
        return true;
    }

    private static bool TryDmaWrite(ulong handle, ulong capacity, ulong offset,
                                    ReadOnlySpan<byte> bytes)
    {
        if (handle == 0 || bytes.Length == 0 || offset > capacity ||
            (ulong)bytes.Length > capacity - offset) return false;
        fixed (byte* source = bytes)
        {
            return ManagedKernelContract.TryDmaWrite(
                handle, ManagedVirtioRngProtocol.DriverId, offset,
                (nuint)source, (ulong)bytes.Length);
        }
    }

    private static bool TryDmaRead(ulong handle, ulong capacity, ulong offset,
                                   Span<byte> bytes)
    {
        if (handle == 0 || bytes.Length == 0 || offset > capacity ||
            (ulong)bytes.Length > capacity - offset) return false;
        fixed (byte* destination = bytes)
        {
            return ManagedKernelContract.TryDmaRead(
                handle, ManagedVirtioRngProtocol.DriverId, offset,
                (nuint)destination, (ulong)bytes.Length);
        }
    }

    private static bool TryDmaWrite16(ulong handle, ulong capacity,
                                      ulong offset, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        Write16(bytes, value);
        return TryDmaWrite(handle, capacity, offset, bytes);
    }

    private static bool TryDmaRead16(ulong handle, ulong capacity, ulong offset,
                                     out ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        value = 0;
        if (!TryDmaRead(handle, capacity, offset, bytes)) return false;
        value = (ushort)(bytes[0] | (bytes[1] << 8));
        return true;
    }

    private static bool TryDmaRead32(ulong handle, ulong capacity, ulong offset,
                                     out uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        value = 0;
        if (!TryDmaRead(handle, capacity, offset, bytes)) return false;
        value = (uint)(bytes[0] | (bytes[1] << 8) |
                       (bytes[2] << 16) | (bytes[3] << 24));
        return true;
    }

    private static bool TryCommonRead8(ulong offset, out byte value)
    {
        value = 0;
        if (!TryCommonRead(offset, 1, out ulong raw)) return false;
        value = (byte)raw;
        return true;
    }

    private static bool TryCommonRead16(ulong offset, out ushort value)
    {
        value = 0;
        if (!TryCommonRead(offset, 2, out ulong raw)) return false;
        value = (ushort)raw;
        return true;
    }

    private static bool TryCommonRead32(ulong offset, out uint value)
    {
        value = 0;
        if (!TryCommonRead(offset, 4, out ulong raw)) return false;
        value = (uint)raw;
        return true;
    }

    private static bool TryCommonRead(ulong offset, uint width, out ulong value)
    {
        value = 0;
        if (!s_commonMappingLive || !InRange(offset, width, s_commonMappingLength) ||
            !ManagedKernelContract.TryMmioRead(
                s_commonMapping, ManagedVirtioRngProtocol.DriverId,
                offset, width, out value)) return false;
        return true;
    }

    private static bool TryCommonWrite8(ulong offset, byte value) =>
        TryCommonWrite(offset, 1, value);

    private static bool TryCommonWrite16(ulong offset, ushort value) =>
        TryCommonWrite(offset, 2, value);

    private static bool TryCommonWrite32(ulong offset, uint value) =>
        TryCommonWrite(offset, 4, value);

    private static bool TryCommonWrite64(ulong offset, ulong value) =>
        TryCommonWrite(offset, 8, value);

    private static bool TryCommonWrite(ulong offset, uint width, ulong value)
    {
        return s_commonMappingLive && InRange(offset, width, s_commonMappingLength) &&
               ManagedKernelContract.TryMmioWrite(
                   s_commonMapping, ManagedVirtioRngProtocol.DriverId,
                   offset, width, value);
    }

    private static bool TryNotifyWrite16(ulong offset, ushort value)
    {
        return s_notifyMappingLive && InRange(offset, 2, s_notifyMappingLength) &&
               ManagedKernelContract.TryMmioWrite(
                   s_notifyMapping, ManagedVirtioRngProtocol.DriverId,
                   offset, 2, value);
    }

    private static bool InRange(ulong offset, uint width, ulong length)
    {
        if (width != 1 && width != 2 && width != 4 && width != 8 ||
            offset > ulong.MaxValue - width || offset + width > length)
            return false;
        return width == 1 || (offset & (width - 1)) == 0;
    }

    private static bool AbortStart()
    {
        bool clean = Cleanup(!s_commonMappingLive || TryCommonWrite8(
            ManagedVirtioRngProtocol.CommonDeviceStatus, 0));
        s_state = clean ? ManagedVirtioRngDriverState.Stopped :
                          ManagedVirtioRngDriverState.Failed;
        return false;
    }

    private static bool Cleanup(bool statusReset)
    {
        bool success = statusReset;
        if (s_queueLive)
            success = ReleaseDma(ref s_queueLive, ref s_queueRetained,
                                 ref s_queueHandle, ref s_queueBusAddress,
                                 ref s_queueLength) && success;
        if (s_bufferLive)
            success = ReleaseDma(ref s_bufferLive, ref s_bufferRetained,
                                 ref s_bufferHandle, ref s_bufferBusAddress,
                                 ref s_bufferLength) && success;
        if (s_pciCommandLive)
        {
            success = ManagedKernelContract.TryPciCommandRestore(
                s_commonResource.ResourceId, s_commonClaim,
                ManagedVirtioRngProtocol.DriverId,
                s_originalPciCommand, out _) && success;
            s_pciCommandLive = false;
        }
        if (s_commonMappingLive)
        {
            bool unmapped = ManagedDeviceResourceRuntimeCatalog.TryUnmap(
                s_commonResource.ResourceId, ManagedVirtioRngProtocol.DriverId,
                s_commonMapping);
            success = unmapped && success;
            if (unmapped)
            {
                s_commonMappingLive = false;
                s_commonMapping = 0;
            }
        }
        if (s_notifyMappingLive)
        {
            bool unmapped = ManagedDeviceResourceRuntimeCatalog.TryUnmap(
                s_notifyResource.ResourceId, ManagedVirtioRngProtocol.DriverId,
                s_notifyMapping);
            success = unmapped && success;
            if (unmapped)
            {
                s_notifyMappingLive = false;
                s_notifyMapping = 0;
            }
        }
        if (s_notifyClaimLive && s_capabilities.NotifyBar != s_capabilities.CommonBar)
        {
            bool released = ManagedDeviceResourceRuntimeCatalog.TryRelease(
                in s_notifyResource, ManagedVirtioRngProtocol.DriverId);
            success = released && success;
            if (released) s_notifyClaimLive = false;
        }
        if (s_commonClaimLive)
        {
            bool released = ManagedDeviceResourceRuntimeCatalog.TryRelease(
                in s_commonResource, ManagedVirtioRngProtocol.DriverId);
            success = released && success;
            if (released)
            {
                s_commonClaimLive = false;
                if (s_capabilities.NotifyBar == s_capabilities.CommonBar)
                    s_notifyClaimLive = false;
            }
        }
        if (success)
        {
            s_commonClaim = 0;
            s_notifyClaim = 0;
            s_commonMappingLength = 0;
            s_notifyMappingLength = 0;
            s_originalPciCommand = 0;
            s_healthy = false;
        }
        return success;
    }

    private static bool ReleaseDma(ref bool live, ref bool retained,
                                   ref ulong handle, ref ulong busAddress,
                                   ref ulong length)
    {
        if (!live) return true;
        if (retained)
        {
            if (!ManagedKernelContract.TryDmaReleaseReference(
                    handle, ManagedVirtioRngProtocol.DriverId)) return false;
            retained = false;
        }
        if (!ManagedKernelContract.TryDmaRelease(
                handle, ManagedVirtioRngProtocol.DriverId)) return false;
        live = false;
        handle = 0;
        busAddress = 0;
        length = 0;
        return true;
    }

    private static bool FailProbe(ReadOnlySpan<byte> marker)
    {
        LastProbeRejectedDevice = true;
        KernelLog.Write(marker);
        return false;
    }

    private static void Write16(Span<byte> bytes, ushort value)
    {
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
    }

    private static void Write32(Span<byte> bytes, int offset, uint value)
    {
        Write16(bytes[offset..], (ushort)value);
        Write16(bytes[(offset + 2)..], (ushort)(value >> 16));
    }

    private static void Write64(Span<byte> bytes, ulong value)
    {
        Write32(bytes, 0, (uint)value);
        Write32(bytes, 4, (uint)(value >> 32));
    }
}

using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelInterruptEventV1
{
    internal const uint ExpectedSize = 48;
    internal const uint AbiVersionCurrent = 1;
    internal const uint EventTypeSerialReceive = 1;
    internal const uint EventTypeKeyboardScancode = 2;
    internal const uint EventFlagHardwareCapture = 1;

    internal uint Size;
    internal uint AbiVersion;
    internal uint EventType;
    internal uint DeviceKind;
    internal uint DeviceId;
    internal ulong Sequence;
    internal uint Flags;
    internal byte PayloadByte;
    internal byte PayloadLength;
    internal ushort Reserved0;
    internal uint Status;
    internal ulong Timestamp;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelInterruptStatsV1
{
    internal const uint ExpectedSize = 80;

    internal uint Size;
    internal uint AbiVersion;
    internal uint QueueCapacity;
    internal uint MaxDrain;
    internal ulong IrqEntryCount;
    internal ulong SerialIsrCount;
    internal ulong EnqueuedCount;
    internal ulong DrainedCount;
    internal ulong DroppedCount;
    internal ulong NextSequence;
    internal uint SubscriptionActive;
    internal uint HardwareEnabled;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelInterruptServicesV1
{
    internal const uint ExpectedSize = 88;
    internal const uint AbiVersionCurrent = 1;
    internal const uint ServiceVersionCurrent = 1;
    internal const uint ArchitectureX64 = 0x8664;
    internal const uint QueueCapacity = 8;
    internal const uint MaxDrain = 4;
    internal const ulong CapabilitySubscribe = 1UL << 0;
    internal const ulong CapabilityUnsubscribe = 1UL << 1;
    internal const ulong CapabilityDrain = 1UL << 2;
    internal const ulong CapabilityQueryStats = 1UL << 3;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal uint EventRecordSize;
    internal uint QueueCapacityValue;
    internal uint MaxDrainValue;
    internal uint Reserved0;
    internal ulong SubscribeAddress;
    internal ulong UnsubscribeAddress;
    internal ulong DrainAddress;
    internal ulong QueryStatsAddress;
    internal ulong Reserved1;
    internal ulong Reserved2;
}

internal static unsafe class ManagedInterruptLayout
{
    internal static bool IsValid()
    {
        return sizeof(GxManagedKernelInterruptEventV1) == 48 &&
               sizeof(GxManagedKernelInterruptStatsV1) == 80 &&
               sizeof(GxManagedKernelInterruptServicesV1) == 88 &&
               sizeof(GxManagedKernelInputServicesV1) == 88 &&
               sizeof(GxManagedKernelKeyboardPlatformDeviceV1) == 32 &&
               Marshal.OffsetOf<GxManagedKernelInterruptEventV1>(
                   nameof(GxManagedKernelInterruptEventV1.Sequence)).ToInt32() == 20 &&
               Marshal.OffsetOf<GxManagedKernelInterruptEventV1>(
                   nameof(GxManagedKernelInterruptEventV1.PayloadByte)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelInterruptStatsV1>(
                   nameof(GxManagedKernelInterruptStatsV1.IrqEntryCount)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelInterruptServicesV1>(
                   nameof(GxManagedKernelInterruptServicesV1.SubscribeAddress)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelInterruptServicesV1>(
                   nameof(GxManagedKernelInterruptServicesV1.QueryStatsAddress)).ToInt32() == 64 &&
               Marshal.OffsetOf<GxManagedKernelInputServicesV1>(
                   nameof(GxManagedKernelInputServicesV1.SubscribeAddress)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelKeyboardPlatformDeviceV1>(
                   nameof(GxManagedKernelKeyboardPlatformDeviceV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelKeyboardPlatformDeviceV1>(
                   nameof(GxManagedKernelKeyboardPlatformDeviceV1.Irq)).ToInt32() == 24;
    }
}

internal unsafe sealed class ManagedInterruptDispatcher
{
    private readonly GxManagedKernelInterruptServicesV1 _services;
    private ulong _subscriptionId;
    private GxManagedKernelInputServicesV1 _inputServices;
    private ulong _inputSubscriptionId;
    private bool _inputAttached;

    private ManagedInterruptDispatcher(in GxManagedKernelInterruptServicesV1 services)
    {
        _services = services;
    }

    internal ulong SubscriptionId => _subscriptionId;
    internal ulong InputSubscriptionId => _inputSubscriptionId;

    internal static ManagedInterruptDispatcher? TryCreate(
        in GxManagedKernelInterruptServicesV1 services)
    {
        const ulong knownCapabilities =
            GxManagedKernelInterruptServicesV1.CapabilitySubscribe |
            GxManagedKernelInterruptServicesV1.CapabilityUnsubscribe |
            GxManagedKernelInterruptServicesV1.CapabilityDrain |
            GxManagedKernelInterruptServicesV1.CapabilityQueryStats;
        if (services.Size != GxManagedKernelInterruptServicesV1.ExpectedSize ||
            services.AbiVersion != GxManagedKernelInterruptServicesV1.AbiVersionCurrent ||
            services.ServiceVersion != GxManagedKernelInterruptServicesV1.ServiceVersionCurrent ||
            services.Architecture != GxManagedKernelInterruptServicesV1.ArchitectureX64 ||
            services.Capabilities != knownCapabilities ||
            services.EventRecordSize != GxManagedKernelInterruptEventV1.ExpectedSize ||
            services.QueueCapacityValue != GxManagedKernelInterruptServicesV1.QueueCapacity ||
            services.MaxDrainValue != GxManagedKernelInterruptServicesV1.MaxDrain ||
            services.Reserved0 != 0 || services.Reserved1 != 0 ||
            services.Reserved2 != 0 || services.SubscribeAddress == 0 ||
            services.UnsubscribeAddress == 0 || services.DrainAddress == 0 ||
            services.QueryStatsAddress == 0)
        {
            return null;
        }
        return new ManagedInterruptDispatcher(in services);
    }

    internal bool TryAttachInputServices(
        in GxManagedKernelInputServicesV1 services)
    {
        const ulong knownCapabilities =
            GxManagedKernelInputServicesV1.CapabilitySubscribe |
            GxManagedKernelInputServicesV1.CapabilityUnsubscribe |
            GxManagedKernelInputServicesV1.CapabilityDrain |
            GxManagedKernelInputServicesV1.CapabilityQueryStats;
        if (_inputAttached ||
            services.Size != GxManagedKernelInputServicesV1.ExpectedSize ||
            services.AbiVersion != GxManagedKernelInputServicesV1.AbiVersionCurrent ||
            services.ServiceVersion != GxManagedKernelInputServicesV1.ServiceVersionCurrent ||
            services.Architecture != GxManagedKernelInputServicesV1.ArchitectureX64 ||
            services.Capabilities != knownCapabilities ||
            services.EventRecordSize != GxManagedKernelInterruptEventV1.ExpectedSize ||
            services.QueueCapacityValue != GxManagedKernelInputServicesV1.QueueCapacity ||
            services.MaxDrainValue != GxManagedKernelInputServicesV1.MaxDrain ||
            services.Reserved0 != 0 || services.Reserved1 != 0 ||
            services.Reserved2 != 0 || services.SubscribeAddress == 0 ||
            services.UnsubscribeAddress == 0 || services.DrainAddress == 0 ||
            services.QueryStatsAddress == 0)
        {
            return false;
        }
        _inputServices = services;
        _inputAttached = true;
        return true;
    }

    internal bool TrySubscribe(uint eventType, uint deviceKind, uint deviceId,
                               out ulong subscriptionId)
    {
        subscriptionId = 0;
        if (_subscriptionId != 0) return false;
        delegate* unmanaged<uint, uint, uint, nuint, nuint, uint> subscribe =
            (delegate* unmanaged<uint, uint, uint, nuint, nuint, uint>)
                (nuint)_services.SubscribeAddress;
        ulong token = 0;
        uint result = subscribe(eventType, deviceKind, deviceId,
                                (nuint)(&token), sizeof(ulong));
        if (result != ManagedKernelContract.ManagedOk || token == 0) return false;
        _subscriptionId = token;
        subscriptionId = token;
        return true;
    }

    internal bool TryUnsubscribe()
    {
        if (_subscriptionId == 0) return false;
        delegate* unmanaged<ulong, uint> unsubscribe =
            (delegate* unmanaged<ulong, uint>)(nuint)_services.UnsubscribeAddress;
        if (unsubscribe(_subscriptionId) != ManagedKernelContract.ManagedOk) return false;
        _subscriptionId = 0;
        return true;
    }

    internal bool TrySubscribeInput(uint eventType, uint deviceKind,
                                    uint deviceId, out ulong subscriptionId)
    {
        subscriptionId = 0;
        if (!_inputAttached || _inputSubscriptionId != 0) return false;
        delegate* unmanaged<uint, uint, uint, nuint, nuint, uint> subscribe =
            (delegate* unmanaged<uint, uint, uint, nuint, nuint, uint>)
                (nuint)_inputServices.SubscribeAddress;
        ulong token = 0;
        uint result = subscribe(eventType, deviceKind, deviceId,
                                (nuint)(&token), sizeof(ulong));
        if (result != ManagedKernelContract.ManagedOk || token == 0) return false;
        _inputSubscriptionId = token;
        subscriptionId = token;
        return true;
    }

    internal bool TryUnsubscribeInput()
    {
        if (!_inputAttached || _inputSubscriptionId == 0) return false;
        delegate* unmanaged<ulong, uint> unsubscribe =
            (delegate* unmanaged<ulong, uint>)(nuint)_inputServices.UnsubscribeAddress;
        if (unsubscribe(_inputSubscriptionId) != ManagedKernelContract.ManagedOk) {
            return false;
        }
        _inputSubscriptionId = 0;
        return true;
    }

    internal bool TryDispatch(ManagedSerialDriver driver, byte expectedPayload,
                              out uint delivered)
    {
        Span<GxManagedKernelInterruptEventV1> events =
            stackalloc GxManagedKernelInterruptEventV1[(int)_services.MaxDrainValue];
        uint drained = 0;
        delivered = 0;
        delegate* unmanaged<uint, nuint, uint, nuint, nuint, uint> drain =
            (delegate* unmanaged<uint, nuint, uint, nuint, nuint, uint>)
                (nuint)_services.DrainAddress;
        fixed (GxManagedKernelInterruptEventV1* eventAddress = events)
        {
            uint result = drain(_services.AbiVersion, (nuint)eventAddress,
                                (uint)(sizeof(GxManagedKernelInterruptEventV1) * events.Length),
                                (nuint)(&drained), sizeof(uint));
            if (result != ManagedKernelContract.ManagedOk ||
                drained > (uint)events.Length) return false;
        }
        for (uint index = 0; index != drained; ++index)
        {
            ref GxManagedKernelInterruptEventV1 value = ref events[(int)index];
            if (value.Size != GxManagedKernelInterruptEventV1.ExpectedSize ||
                value.AbiVersion != GxManagedKernelInterruptEventV1.AbiVersionCurrent ||
                value.EventType != GxManagedKernelInterruptEventV1.EventTypeSerialReceive ||
                value.DeviceKind != GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial ||
                value.DeviceId != GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 ||
                value.Flags != GxManagedKernelInterruptEventV1.EventFlagHardwareCapture ||
                value.PayloadLength != 1 || value.Reserved0 != 0 ||
                value.Timestamp != 0 || value.Sequence == 0 ||
                !driver.TryHandleReceive(in value, expectedPayload)) return false;
            delivered++;
        }
        return true;
    }

    /* Worker path: drain one bounded ABI batch and contain malformed or stale
       records individually.  The legacy TryDispatch method remains strict
       for the Phase 9 diagnostic control. */
    internal bool TryDispatchBatch(ManagedSerialDriver driver,
                                   out uint delivered, out uint rejected)
    {
        return TryDispatchBatch(driver, null, out delivered, out rejected);
    }

    internal bool TryDispatchBatch(ManagedSerialDriver? serialDriver,
                                   ManagedKeyboardDriver? keyboardDriver,
                                   out uint delivered, out uint rejected)
    {
        Span<GxManagedKernelInterruptEventV1> events =
            stackalloc GxManagedKernelInterruptEventV1[(int)_services.MaxDrainValue];
        uint drained = 0;
        delivered = 0;
        rejected = 0;
        delegate* unmanaged<uint, nuint, uint, nuint, nuint, uint> drain =
            (delegate* unmanaged<uint, nuint, uint, nuint, nuint, uint>)
                (nuint)_services.DrainAddress;
        fixed (GxManagedKernelInterruptEventV1* eventAddress = events)
        {
            uint result = drain(_services.AbiVersion, (nuint)eventAddress,
                                (uint)(sizeof(GxManagedKernelInterruptEventV1) * events.Length),
                                (nuint)(&drained), sizeof(uint));
            if (result != ManagedKernelContract.ManagedOk ||
                drained > (uint)events.Length) return false;
        }
        for (uint index = 0; index != drained; ++index)
        {
            ref GxManagedKernelInterruptEventV1 value = ref events[(int)index];
            if (!TryRouteEvent(in value, serialDriver, keyboardDriver))
            {
                rejected++;
                continue;
            }
            delivered++;
        }
        return true;
    }

    private bool TryRouteEvent(in GxManagedKernelInterruptEventV1 value,
                               ManagedSerialDriver? serialDriver,
                               ManagedKeyboardDriver? keyboardDriver)
    {
        if (value.Size != GxManagedKernelInterruptEventV1.ExpectedSize ||
            value.AbiVersion != GxManagedKernelInterruptEventV1.AbiVersionCurrent ||
            value.Flags != GxManagedKernelInterruptEventV1.EventFlagHardwareCapture ||
            value.PayloadLength != 1 || value.Reserved0 != 0 ||
            value.Timestamp != 0 || value.Sequence == 0)
        {
            return false;
        }
        if (value.EventType == GxManagedKernelInterruptEventV1.EventTypeSerialReceive &&
            serialDriver != null)
        {
            return serialDriver.TryHandleReceive(in value);
        }
        if (value.EventType == GxManagedKernelInterruptEventV1.EventTypeKeyboardScancode &&
            keyboardDriver != null)
        {
            return keyboardDriver.TryHandleScancode(in value);
        }
        return false;
    }

    internal bool TryQueryStats(out GxManagedKernelInterruptStatsV1 stats)
    {
        stats = default;
        delegate* unmanaged<uint, nuint, nuint, uint> queryStats =
            (delegate* unmanaged<uint, nuint, nuint, uint>)
                (nuint)_services.QueryStatsAddress;
        fixed (GxManagedKernelInterruptStatsV1* statsAddress = &stats)
        {
            return queryStats(_services.AbiVersion, (nuint)statsAddress,
                              GxManagedKernelInterruptStatsV1.ExpectedSize) ==
                   ManagedKernelContract.ManagedOk;
        }
    }
}

using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

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
    private ulong _e1000SubscriptionId;
    private ulong _e1000LastSequence;
    private bool _e1000SequenceBaselineCaptured;
    private uint _phase91LastBatchDrained;
    private uint _phase91LastBatchDelivered;
    private uint _phase91LastBatchRejected;
    private GxManagedKernelInterruptEventV1 _phase91LastRejectedEvent;
    private uint _phase91LastRejectedShapeMask;
    private uint _phase91LastRejectedRouteMask;
    private uint _phase91LastRejectedDriverMask;
    private GxManagedKernelInputServicesV1 _inputServices;
    private ulong _inputSubscriptionId;
    private bool _inputAttached;

    private ManagedInterruptDispatcher(in GxManagedKernelInterruptServicesV1 services)
    {
        _services = services;
    }

    internal ulong SubscriptionId => _subscriptionId;
    internal ulong E1000SubscriptionId => _e1000SubscriptionId;
    internal ulong E1000LastSequence => _e1000LastSequence;
    internal bool E1000SequenceBaselineCaptured =>
        _e1000SequenceBaselineCaptured;
    internal ulong InputSubscriptionId => _inputSubscriptionId;
    internal uint Phase91LastBatchDrained => _phase91LastBatchDrained;
    internal uint Phase91LastBatchDelivered => _phase91LastBatchDelivered;
    internal uint Phase91LastBatchRejected => _phase91LastBatchRejected;
    internal GxManagedKernelInterruptEventV1 Phase91LastRejectedEvent =>
        _phase91LastRejectedEvent;
    internal uint Phase91LastRejectedShapeMask =>
        _phase91LastRejectedShapeMask;
    internal uint Phase91LastRejectedRouteMask =>
        _phase91LastRejectedRouteMask;
    internal uint Phase91LastRejectedDriverMask =>
        _phase91LastRejectedDriverMask;

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

    internal bool TrySubscribeE1000(out ulong subscriptionId)
    {
        subscriptionId = 0;
        if (_e1000SubscriptionId != 0) return false;
        _e1000LastSequence = 0;
        _e1000SequenceBaselineCaptured = false;
        delegate* unmanaged<uint, uint, uint, nuint, nuint, uint> subscribe =
            (delegate* unmanaged<uint, uint, uint, nuint, nuint, uint>)(nuint)
                _services.SubscribeAddress;
        ulong token = 0;
        uint result = subscribe(ManagedE1000Protocol.InterruptEventType,
            ManagedE1000Protocol.PciDeviceKind,
            ManagedE1000Protocol.PciOwnerId, (nuint)(&token), sizeof(ulong));
        if (result != ManagedKernelContract.ManagedOk || token == 0) return false;
        _e1000SubscriptionId = token;
        subscriptionId = token;
        return true;
    }

    internal bool TryUnsubscribeE1000()
    {
        if (_e1000SubscriptionId == 0) return false;
        delegate* unmanaged<ulong, uint> unsubscribe =
            (delegate* unmanaged<ulong, uint>)(nuint)_services.UnsubscribeAddress;
        if (unsubscribe(_e1000SubscriptionId) != ManagedKernelContract.ManagedOk)
            return false;
        _e1000SubscriptionId = 0;
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
        return TryDispatchBatch(serialDriver, keyboardDriver, null,
                                out delivered, out rejected);
    }

    internal bool TryDispatchBatch(ManagedSerialDriver? serialDriver,
                                   ManagedKeyboardDriver? keyboardDriver,
                                   ManagedE1000Driver? e1000Driver,
                                   out uint delivered, out uint rejected)
    {
        Span<GxManagedKernelInterruptEventV1> events =
            stackalloc GxManagedKernelInterruptEventV1[(int)_services.MaxDrainValue];
        uint drained = 0;
        delivered = 0;
        rejected = 0;
        bool capturePhase91Diagnostics =
            e1000Driver?.Phase91DiagnosticsActive == true;
        if (capturePhase91Diagnostics)
        {
            e1000Driver!.ResetPhase91InterruptRejectMask();
            _phase91LastBatchDrained = 0;
            _phase91LastBatchDelivered = 0;
            _phase91LastBatchRejected = 0;
            _phase91LastRejectedEvent = default;
            _phase91LastRejectedShapeMask = 0;
            _phase91LastRejectedRouteMask = 0;
            _phase91LastRejectedDriverMask = 0;
        }
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
            if (value.EventType == ManagedE1000Protocol.InterruptEventType &&
                value.DeviceKind == ManagedE1000Protocol.PciDeviceKind &&
                value.DeviceId == ManagedE1000Protocol.PciOwnerId)
            {
                if (!_e1000SequenceBaselineCaptured)
                {
                    _e1000LastSequence = value.Sequence - 1;
                    _e1000SequenceBaselineCaptured = true;
                }
                if (value.Sequence > _e1000LastSequence)
                    _e1000LastSequence = value.Sequence;
            }
            if (!TryRouteEvent(in value, serialDriver, keyboardDriver, e1000Driver))
            {
                rejected++;
                if (capturePhase91Diagnostics)
                {
                    _phase91LastRejectedEvent = value;
                    _phase91LastRejectedShapeMask =
                        GetEventShapeFailureMask(in value);
                    _phase91LastRejectedRouteMask =
                        GetE1000RouteFailureMask(in value, e1000Driver);
                    _phase91LastRejectedDriverMask =
                        e1000Driver?.Phase91LastInterruptRejectMask ?? 0;
                }
                continue;
            }
            delivered++;
        }
        if (capturePhase91Diagnostics)
        {
            _phase91LastBatchDrained = drained;
            _phase91LastBatchDelivered = delivered;
            _phase91LastBatchRejected = rejected;
        }
        return true;
    }

    private static uint GetEventShapeFailureMask(
        in GxManagedKernelInterruptEventV1 value)
    {
        uint mask = 0;
        if (value.Size != GxManagedKernelInterruptEventV1.ExpectedSize) mask |= 1U << 0;
        if (value.AbiVersion != GxManagedKernelInterruptEventV1.AbiVersionCurrent) mask |= 1U << 1;
        if (value.Flags != GxManagedKernelInterruptEventV1.EventFlagHardwareCapture) mask |= 1U << 2;
        if (value.PayloadLength != 1) mask |= 1U << 3;
        if (value.Reserved0 != 0) mask |= 1U << 4;
        if (value.Timestamp != 0) mask |= 1U << 5;
        if (value.Sequence == 0) mask |= 1U << 6;
        return mask;
    }

    private static uint GetE1000RouteFailureMask(
        in GxManagedKernelInterruptEventV1 value,
        ManagedE1000Driver? e1000Driver)
    {
        uint mask = 0;
        if (value.EventType != ManagedE1000Protocol.InterruptEventType) mask |= 1U << 0;
        if (value.DeviceKind != ManagedE1000Protocol.PciDeviceKind) mask |= 1U << 1;
        if (value.DeviceId != ManagedE1000Protocol.PciOwnerId) mask |= 1U << 2;
        if (e1000Driver == null) mask |= 1U << 3;
        return mask;
    }

    private bool TryRouteEvent(in GxManagedKernelInterruptEventV1 value,
                               ManagedSerialDriver? serialDriver,
                               ManagedKeyboardDriver? keyboardDriver,
                               ManagedE1000Driver? e1000Driver)
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
        if (value.EventType == ManagedE1000Protocol.InterruptEventType &&
            e1000Driver != null)
        {
            return e1000Driver.TryHandleInterrupt(in value);
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

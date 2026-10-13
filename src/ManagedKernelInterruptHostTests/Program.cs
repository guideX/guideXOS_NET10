using System;
using System.Reflection;
using System.Runtime.InteropServices;
using GuideXOS.Net10.ManagedKernel;

internal static unsafe class Program
{
    private const ulong PageSize = KernelArena.PageSize;
    private static uint s_assertions;
    private static uint s_failures;
    private static uint s_transmitCalls;
    private static bool s_interruptActive;
    private static bool s_e1000InterruptActive;
    private static ulong s_interruptToken;
    private static ulong s_e1000InterruptToken;
    private static ulong s_nextInterruptToken = 0xA900;
    private static uint s_subscribeCalls;
    private static uint s_unsubscribeCalls;
    private static uint s_eventCount;
    private static uint s_drainedCount;
    private static uint s_enqueuedCount;
    private static ulong s_nextSequence = 1;
    private static GxManagedKernelInterruptEventV1[] s_events =
        new GxManagedKernelInterruptEventV1[8];

    private sealed class FakeProvider : IKernelMemoryProvider
    {
        private readonly (bool Live, nint Raw, KernelMemoryRegion Region)[] _slots =
            new (bool, nint, KernelMemoryRegion)[16];

        internal uint AllocationCalls;
        internal uint ReleaseCalls;
        public bool IsAvailable => true;

        public bool TryAllocate(ulong pageCount, uint flags,
                                out KernelMemoryRegion region)
        {
            region = default;
            if (flags != 0 || pageCount == 0 ||
                pageCount > (ulong)nint.MaxValue / PageSize) return false;
            for (int index = 0; index != _slots.Length; ++index)
            {
                if (_slots[index].Live) continue;
                ulong bytes = pageCount * PageSize;
                nint raw = Marshal.AllocHGlobal((nint)(bytes + PageSize));
                ulong rawAddress = (ulong)(nuint)raw;
                ulong aligned = (rawAddress + PageSize - 1) & ~(PageSize - 1);
                region = new KernelMemoryRegion
                {
                    AllocationId = (ulong)index + 1,
                    VirtualAddress = aligned,
                    ByteLength = bytes,
                    PageCount = pageCount,
                    PageSize = PageSize,
                    Flags = 0
                };
                _slots[index] = (true, raw, region);
                AllocationCalls++;
                return true;
            }
            return false;
        }

        public bool IsValidRegion(in KernelMemoryRegion region)
        {
            for (int index = 0; index != _slots.Length; ++index)
            {
                if (!_slots[index].Live) continue;
                KernelMemoryRegion candidate = _slots[index].Region;
                if (candidate.AllocationId == region.AllocationId &&
                    candidate.VirtualAddress == region.VirtualAddress &&
                    candidate.ByteLength == region.ByteLength &&
                    candidate.PageCount == region.PageCount &&
                    candidate.PageSize == region.PageSize &&
                    candidate.Flags == region.Flags) return true;
            }
            return false;
        }

        public bool TryRelease(in KernelMemoryRegion region)
        {
            for (int index = 0; index != _slots.Length; ++index)
            {
                if (!_slots[index].Live ||
                    _slots[index].Region.AllocationId != region.AllocationId ||
                    _slots[index].Region.VirtualAddress != region.VirtualAddress) continue;
                Marshal.FreeHGlobal(_slots[index].Raw);
                _slots[index] = default;
                ReleaseCalls++;
                return true;
            }
            return false;
        }

        internal void ReleaseAll()
        {
            for (int index = 0; index != _slots.Length; ++index)
            {
                if (!_slots[index].Live) continue;
                Marshal.FreeHGlobal(_slots[index].Raw);
                _slots[index] = default;
                ReleaseCalls++;
            }
        }
    }

    private static void Expect(bool condition, string message)
    {
        s_assertions++;
        if (condition) return;
        s_failures++;
        Console.WriteLine("FAIL: " + message);
    }

    [UnmanagedCallersOnly]
    private static uint QuerySerialStatus(uint requestedAbiVersion, uint deviceId,
                                           nuint resultAddress, nuint resultCapacity)
    {
        if (requestedAbiVersion != GxManagedKernelSerialServicesV1.AbiVersionCurrent ||
            deviceId != GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 ||
            resultAddress == 0 || resultCapacity < GxManagedKernelSerialStatusV1.ExpectedSize)
            return 1;
        *(GxManagedKernelSerialStatusV1*)resultAddress = new GxManagedKernelSerialStatusV1
        {
            Size = GxManagedKernelSerialStatusV1.ExpectedSize,
            AbiVersion = requestedAbiVersion,
            Status = GxManagedKernelSerialStatusV1.StatusDevicePresent |
                      GxManagedKernelSerialStatusV1.StatusTransmitterReady,
            Capabilities = GxManagedKernelSerialServicesV1.CapabilityTransmit |
                           GxManagedKernelSerialServicesV1.CapabilityQueryStatus
        };
        return 0;
    }

    [UnmanagedCallersOnly]
    private static uint Transmit(uint deviceId, nuint bufferAddress,
                                 uint byteLength, uint flags)
    {
        if (deviceId != GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 ||
            bufferAddress == 0 || byteLength == 0 || flags != 0) return 1;
        s_transmitCalls++;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static uint Subscribe(uint eventType, uint deviceKind, uint deviceId,
                                  nuint tokenAddress, nuint tokenCapacity)
    {
        if (tokenAddress == 0 || tokenCapacity < sizeof(ulong)) return 1;
        if (eventType == GxManagedKernelInterruptEventV1.EventTypeSerialReceive &&
            deviceKind == GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial &&
            deviceId == GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 &&
            !s_interruptActive)
        {
            s_interruptActive = true;
            s_interruptToken = ++s_nextInterruptToken;
            *(ulong*)tokenAddress = s_interruptToken;
        }
        else if (eventType == ManagedE1000Protocol.InterruptEventType &&
                 deviceKind == ManagedE1000Protocol.PciDeviceKind &&
                 deviceId == ManagedE1000Protocol.PciOwnerId &&
                 !s_e1000InterruptActive)
        {
            s_e1000InterruptActive = true;
            s_e1000InterruptToken = ++s_nextInterruptToken;
            *(ulong*)tokenAddress = s_e1000InterruptToken;
        }
        else return 1;
        s_subscribeCalls++;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static uint Unsubscribe(ulong token)
    {
        if (s_interruptActive && token == s_interruptToken)
        {
            s_interruptActive = false;
            s_unsubscribeCalls++;
            s_drainedCount += s_eventCount;
            s_eventCount = 0;
            return 0;
        }
        if (s_e1000InterruptActive && token == s_e1000InterruptToken)
        {
            s_e1000InterruptActive = false;
            s_unsubscribeCalls++;
            return 0;
        }
        return 7;
    }

    [UnmanagedCallersOnly]
    private static uint Drain(uint requestedAbiVersion, nuint outputAddress,
                              uint outputCapacity, nuint drainedAddress,
                              nuint drainedCapacity)
    {
        uint count;
        if (requestedAbiVersion != GxManagedKernelInterruptServicesV1.AbiVersionCurrent ||
            outputAddress == 0 || drainedAddress == 0 ||
            outputCapacity < GxManagedKernelInterruptEventV1.ExpectedSize ||
            drainedCapacity < sizeof(uint)) return 1;
        count = outputCapacity / GxManagedKernelInterruptEventV1.ExpectedSize;
        if (count > GxManagedKernelInterruptServicesV1.MaxDrain) {
            count = GxManagedKernelInterruptServicesV1.MaxDrain;
        }
        if (count > s_eventCount) count = s_eventCount;
        GxManagedKernelInterruptEventV1* output =
            (GxManagedKernelInterruptEventV1*)outputAddress;
        for (uint index = 0; index != count; ++index) output[index] = s_events[index];
        for (uint index = count; index != s_eventCount; ++index) {
            s_events[index - count] = s_events[index];
        }
        s_eventCount -= count;
        s_drainedCount += count;
        *(uint*)drainedAddress = count;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static uint QueryStats(uint requestedAbiVersion, nuint outputAddress,
                                   nuint outputCapacity)
    {
        if (requestedAbiVersion != GxManagedKernelInterruptServicesV1.AbiVersionCurrent ||
            outputAddress == 0 || outputCapacity < GxManagedKernelInterruptStatsV1.ExpectedSize)
            return 1;
        *(GxManagedKernelInterruptStatsV1*)outputAddress = new GxManagedKernelInterruptStatsV1
        {
            Size = GxManagedKernelInterruptStatsV1.ExpectedSize,
            AbiVersion = requestedAbiVersion,
            QueueCapacity = GxManagedKernelInterruptServicesV1.QueueCapacity,
            MaxDrain = GxManagedKernelInterruptServicesV1.MaxDrain,
            EnqueuedCount = s_enqueuedCount,
            DrainedCount = s_drainedCount,
            NextSequence = s_nextSequence,
            SubscriptionActive = s_interruptActive || s_e1000InterruptActive ? 1U : 0U,
            HardwareEnabled = s_interruptActive || s_e1000InterruptActive ? 1U : 0U
        };
        return 0;
    }

    private static void BuildBinding(
        out GxManagedKernelSerialPlatformDeviceV1 device,
        out GxManagedKernelSerialServicesV1 services,
        out GxManagedKernelInterruptServicesV1 interruptServices)
    {
        const ulong serialCapabilities =
            GxManagedKernelSerialServicesV1.CapabilityTransmit |
            GxManagedKernelSerialServicesV1.CapabilityQueryStatus;
        const ulong interruptCapabilities =
            GxManagedKernelInterruptServicesV1.CapabilitySubscribe |
            GxManagedKernelInterruptServicesV1.CapabilityUnsubscribe |
            GxManagedKernelInterruptServicesV1.CapabilityDrain |
            GxManagedKernelInterruptServicesV1.CapabilityQueryStats;
        device = new GxManagedKernelSerialPlatformDeviceV1
        {
            Size = GxManagedKernelSerialPlatformDeviceV1.ExpectedSize,
            AbiVersion = GxManagedKernelSerialServicesV1.AbiVersionCurrent,
            DeviceKind = GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial,
            DeviceId = GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1,
            Capabilities = serialCapabilities,
            ComIndex = GxManagedKernelSerialPlatformDeviceV1.ComIndex1
        };
        services = new GxManagedKernelSerialServicesV1
        {
            Size = GxManagedKernelSerialServicesV1.ExpectedSize,
            AbiVersion = GxManagedKernelSerialServicesV1.AbiVersionCurrent,
            ServiceVersion = GxManagedKernelSerialServicesV1.ServiceVersionCurrent,
            Architecture = GxManagedKernelSerialServicesV1.ArchitectureX64,
            Capabilities = serialCapabilities,
            DeviceKind = device.DeviceKind,
            DeviceId = device.DeviceId,
            ComIndex = device.ComIndex,
            MaxTransmitBytesValue = GxManagedKernelSerialServicesV1.MaxTransmitBytes,
            TransmitAddress = (ulong)(nuint)(delegate* unmanaged<uint, nuint, uint, uint, uint>)&Transmit,
            QueryStatusAddress = (ulong)(nuint)(delegate* unmanaged<uint, uint, nuint, nuint, uint>)&QuerySerialStatus
        };
        interruptServices = new GxManagedKernelInterruptServicesV1
        {
            Size = GxManagedKernelInterruptServicesV1.ExpectedSize,
            AbiVersion = GxManagedKernelInterruptServicesV1.AbiVersionCurrent,
            ServiceVersion = GxManagedKernelInterruptServicesV1.ServiceVersionCurrent,
            Architecture = GxManagedKernelInterruptServicesV1.ArchitectureX64,
            Capabilities = interruptCapabilities,
            EventRecordSize = GxManagedKernelInterruptEventV1.ExpectedSize,
            QueueCapacityValue = GxManagedKernelInterruptServicesV1.QueueCapacity,
            MaxDrainValue = GxManagedKernelInterruptServicesV1.MaxDrain,
            SubscribeAddress = (ulong)(nuint)(delegate* unmanaged<uint, uint, uint, nuint, nuint, uint>)&Subscribe,
            UnsubscribeAddress = (ulong)(nuint)(delegate* unmanaged<ulong, uint>)&Unsubscribe,
            DrainAddress = (ulong)(nuint)(delegate* unmanaged<uint, nuint, uint, nuint, nuint, uint>)&Drain,
            QueryStatsAddress = (ulong)(nuint)(delegate* unmanaged<uint, nuint, nuint, uint>)&QueryStats
        };
    }

    private static void QueueEvent(ulong sequence, byte payload,
                                   uint deviceId = GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1)
    {
        if (sequence >= s_nextSequence) s_nextSequence = sequence + 1;
        s_events[s_eventCount++] = new GxManagedKernelInterruptEventV1
        {
            Size = GxManagedKernelInterruptEventV1.ExpectedSize,
            AbiVersion = GxManagedKernelInterruptEventV1.AbiVersionCurrent,
            EventType = GxManagedKernelInterruptEventV1.EventTypeSerialReceive,
            DeviceKind = GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial,
            DeviceId = deviceId,
            Sequence = sequence,
            Flags = GxManagedKernelInterruptEventV1.EventFlagHardwareCapture,
            PayloadByte = payload,
            PayloadLength = 1
        };
        s_enqueuedCount++;
    }

    private static void QueueE1000Event(ulong sequence, uint cause)
    {
        if (sequence >= s_nextSequence) s_nextSequence = sequence + 1;
        s_events[s_eventCount++] = new GxManagedKernelInterruptEventV1
        {
            Size = GxManagedKernelInterruptEventV1.ExpectedSize,
            AbiVersion = GxManagedKernelInterruptEventV1.AbiVersionCurrent,
            EventType = ManagedE1000Protocol.InterruptEventType,
            DeviceKind = ManagedE1000Protocol.PciDeviceKind,
            DeviceId = ManagedE1000Protocol.PciOwnerId,
            Sequence = sequence,
            Flags = GxManagedKernelInterruptEventV1.EventFlagHardwareCapture,
            PayloadByte = (byte)cause,
            PayloadLength = 1,
            Status = cause
        };
        s_enqueuedCount++;
    }

    private static void RunDispatcherAndDriverProof()
    {
        FakeProvider provider = new();
        BuildBinding(out GxManagedKernelSerialPlatformDeviceV1 device,
                     out GxManagedKernelSerialServicesV1 serialServices,
                     out GxManagedKernelInterruptServicesV1 interruptServices);
        Expect(ManagedInterruptLayout.IsValid(), "managed interrupt ABI layout is exact");
        ManagedInterruptDispatcher? dispatcher =
            ManagedInterruptDispatcher.TryCreate(in interruptServices);
        Expect(dispatcher != null, "managed dispatcher accepts versioned services");
        if (dispatcher == null) return;
        GxManagedKernelInterruptServicesV1 invalidServices = interruptServices;
        invalidServices.Reserved0 = 1;
        Expect(ManagedInterruptDispatcher.TryCreate(in invalidServices) == null,
               "dispatcher rejects nonzero reserved metadata");

        ManagedSerialDriver? driver = ManagedSerialDriver.TryCreate(
            provider, in device, in serialServices);
        Expect(driver != null && driver.TryInitialize() && driver.TryStart(),
               "serial driver reaches started state for interrupt dispatch");
        if (driver == null) return;
        Expect(driver.TrySubscribeReceive(dispatcher) && s_subscribeCalls == 1 &&
               driver.ReceiveState == ManagedSerialReceiveState.Subscribed,
               "driver subscription binds the native dispatcher token");
        Expect(!driver.TrySubscribeReceive(dispatcher),
               "second driver subscription is rejected");

        QueueEvent(1, (byte)'R');
        Expect(dispatcher.TryDispatch(driver, (byte)'R', out uint delivered) &&
               delivered == 1 && driver.ReceiveCount == 1 &&
               driver.LastReceiveByte == (byte)'R',
               "first bounded event dispatch reaches the managed serial driver");

        QueueEvent(2, (byte)'S', device.DeviceId + 1);
        Expect(!dispatcher.TryDispatch(driver, (byte)'S', out delivered) &&
               delivered == 0 && driver.ReceiveCount == 1,
               "wrong device identity is rejected before driver delivery");
        QueueEvent(2, (byte)'S');
        Expect(dispatcher.TryDispatch(driver, (byte)'S', out delivered) &&
               delivered == 1 && driver.ReceiveCount == 2 &&
               driver.LastReceiveSequence == 2,
               "second event dispatch preserves sequence continuity");
        QueueEvent(4, (byte)'T');
        Expect(!dispatcher.TryDispatch(driver, (byte)'T', out delivered) &&
               driver.ReceiveCount == 2,
               "sequence gaps fail closed");

        Expect(dispatcher.TryQueryStats(out GxManagedKernelInterruptStatsV1 stats) &&
               stats.QueueCapacity == 8 && stats.MaxDrain == 4 &&
               stats.DrainedCount == s_drainedCount,
               "managed stats query uses the same bounded ABI");
        Expect(driver.TryRunReceiveRuntimeArenaProof(),
               "runtime arena allocation remains valid while subscribed");
        Expect(driver.TryUnsubscribeReceive(dispatcher) && !s_interruptActive &&
               s_unsubscribeCalls == 1 && driver.ReceiveState == ManagedSerialReceiveState.NotSubscribed,
               "unsubscribe clears managed and native subscription state");
        QueueEvent(5, (byte)'Z');
        Expect(!dispatcher.TryDispatch(driver, (byte)'Z', out delivered) &&
               driver.ReceiveCount == 2,
               "post-unsubscribe event does not deliver to the driver");
        Expect(driver.TryStop() && driver.Destroy() &&
               provider.AllocationCalls == provider.ReleaseCalls,
               "driver teardown restores provider allocations");
        provider.ReleaseAll();
    }

    private static void ResetInterruptFixture()
    {
        Array.Clear(s_events);
        s_interruptActive = false;
        s_e1000InterruptActive = false;
        s_interruptToken = 0;
        s_e1000InterruptToken = 0;
        s_nextInterruptToken = 0xA900;
        s_subscribeCalls = 0;
        s_unsubscribeCalls = 0;
        s_eventCount = 0;
        s_drainedCount = 0;
        s_enqueuedCount = 0;
        s_nextSequence = 1;
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        FieldInfo? field = target.GetType().GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing field: " + name);
        field.SetValue(target, value);
    }

    private static ManagedE1000Driver CreateRunningPhase91Driver(
        ManagedInterruptDispatcher dispatcher)
    {
        ManagedDevice device = default;
        ConstructorInfo? constructor = null;
        foreach (ConstructorInfo candidate in typeof(ManagedE1000Driver).GetConstructors(
                     BindingFlags.Instance | BindingFlags.NonPublic))
        {
            ParameterInfo[] parameters = candidate.GetParameters();
            if (parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(ManagedDevice).MakeByRefType() &&
                parameters[1].ParameterType == typeof(uint))
            {
                constructor = candidate;
                break;
            }
        }
        if (constructor == null)
            throw new InvalidOperationException("Missing E1000 host-test constructor.");
        var driver = (ManagedE1000Driver)constructor.Invoke(
            new object[] { device, 0x91U });
        SetPrivateField(driver, "_phase80Requested", true);
        SetPrivateField(driver, "_phase90AsyncTxRequested", true);
        SetPrivateField(driver, "_phase91Requested", true);
        SetPrivateField(driver, "_interruptSubscribed", true);
        SetPrivateField(driver, "_interruptDispatcher", dispatcher);
        SetPrivateField(driver, "_state", ManagedE1000DriverState.Running);
        return driver;
    }

    private static bool ArmSyncTxProof(ManagedE1000Driver driver)
    {
        MethodInfo? method = typeof(ManagedE1000Driver).GetMethod(
            "TryArmSyncTxProof", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("Missing proof arm method.");
        object?[] arguments = { null };
        return (bool)(method.Invoke(driver, arguments) ?? false);
    }

    private static void RunPhase91SyncTxDispatchRegressions()
    {
        const uint txdw = ManagedE1000Protocol.InterruptCauseTxDescriptorWriteback;
        ResetInterruptFixture();
        BuildBinding(out _, out _, out GxManagedKernelInterruptServicesV1 services);
        ManagedInterruptDispatcher? dispatcher =
            ManagedInterruptDispatcher.TryCreate(in services);
        Expect(dispatcher != null, "phase91 TXDW proof dispatcher is created");
        if (dispatcher == null) return;
        Expect(dispatcher.TrySubscribeE1000(out ulong firstRoute) &&
               firstRoute != 0, "phase91 E1000 route subscribes");

        ManagedE1000Driver immediate = CreateRunningPhase91Driver(dispatcher);
        Expect(ArmSyncTxProof(immediate),
            "production sync-TX proof arms from native next-sequence baseline");
        QueueE1000Event(1, txdw);
        Expect(dispatcher.TryDispatchBatch(null, null, immediate,
                   out uint delivered, out uint rejected) &&
               delivered == 1 && rejected == 0 &&
               immediate.SyncTxTxdwNotificationObserved &&
               immediate.IsSyncTxProofAccepted(true, true, rejected),
            "immediate E1000 TXDW reaches production driver and passes proof");

        ResetInterruptFixture();
        BuildBinding(out GxManagedKernelSerialPlatformDeviceV1 serialDevice,
            out GxManagedKernelSerialServicesV1 serialServices,
            out services);
        dispatcher = ManagedInterruptDispatcher.TryCreate(in services);
        Expect(dispatcher != null && dispatcher.TrySubscribeE1000(out _),
            "COM1 interleaving fixture binds E1000");
        if (dispatcher == null) return;
        FakeProvider provider = new();
        ManagedSerialDriver? serial = ManagedSerialDriver.TryCreate(
            provider, in serialDevice, in serialServices);
        Expect(serial != null && serial.TryInitialize() && serial.TryStart() &&
               serial.TrySubscribeReceive(dispatcher),
            "COM1 route runs beside the replacement E1000 route");
        if (serial == null) return;
        ManagedE1000Driver interleaved = CreateRunningPhase91Driver(dispatcher);
        Expect(ArmSyncTxProof(interleaved), "interleaved sync-TX proof arms");
        QueueE1000Event(1, txdw);
        QueueEvent(2, (byte)'C');
        QueueE1000Event(3, txdw);
        bool interleavingDispatched = dispatcher.TryDispatchBatch(
            serial, null, interleaved, out delivered, out rejected);
        Expect(interleavingDispatched && delivered == 3 && rejected == 0 &&
               serial.ReceiveCount == 1 && serial.LastReceiveByte == (byte)'C' &&
               interleaved.Phase80DeliveryCount == 2 &&
               interleaved.SyncTxTxdwNotificationCount == 2 &&
               interleaved.IsSyncTxProofAccepted(true, true, rejected),
            "COM1 event interleaving preserves serial and E1000 delivery state");
        Expect(serial.TryUnsubscribeReceive(dispatcher) && serial.TryStop() &&
               serial.Destroy() && provider.AllocationCalls == provider.ReleaseCalls,
            "COM1 interleaving fixture releases its resources");
        provider.ReleaseAll();

        ResetInterruptFixture();
        dispatcher = ManagedInterruptDispatcher.TryCreate(in services);
        ulong oldRoute = 0;
        bool oldRouteSubscribed = dispatcher != null &&
            dispatcher.TrySubscribeE1000(out oldRoute);
        Expect(oldRouteSubscribed,
            "old E1000 route opens for stale-FIFO regression");
        if (dispatcher == null) return;
        if (!oldRouteSubscribed) return;
        QueueE1000Event(1, txdw);
        Expect(dispatcher.TryUnsubscribeE1000() &&
               dispatcher.TrySubscribeE1000(out ulong replacementRoute) &&
               replacementRoute != oldRoute,
            "replacement E1000 subscription gets a new route token");
        ManagedE1000Driver restarted = CreateRunningPhase91Driver(dispatcher);
        Expect(ArmSyncTxProof(restarted),
            "restarted driver snapshots the replacement sequence boundary");
        bool oldQueuedEventDispatched = dispatcher.TryDispatchBatch(
            null, null, restarted, out delivered, out rejected);
        Expect(oldQueuedEventDispatched && delivered == 1 && rejected == 0 &&
               !restarted.SyncTxTxdwNotificationObserved &&
               !restarted.IsSyncTxProofAccepted(true, true, 0),
            "pre-restart queued E1000 TXDW cannot satisfy restarted proof");
        QueueE1000Event(2, txdw);
        bool freshEventDispatched = dispatcher.TryDispatchBatch(
            null, null, restarted, out delivered, out rejected);
        Expect(freshEventDispatched && delivered == 1 && rejected == 0 &&
               restarted.SyncTxTxdwNotificationObserved &&
               restarted.IsSyncTxProofAccepted(true, true, rejected),
            "fresh E1000 event after replacement subscription satisfies proof");

        ResetInterruptFixture();
        dispatcher = ManagedInterruptDispatcher.TryCreate(in services);
        Expect(dispatcher != null && dispatcher.TrySubscribeE1000(out _),
            "delayed dispatcher fixture subscribes E1000");
        if (dispatcher == null) return;
        ManagedE1000Driver delayed = CreateRunningPhase91Driver(dispatcher);
        Expect(ArmSyncTxProof(delayed), "delayed dispatch proof arms");
        QueueE1000Event(1, txdw);
        bool synchronousDdCompleted = true;
        Expect(!delayed.IsSyncTxProofAccepted(synchronousDdCompleted, false, 0) &&
               delayed.SyncTxTxdwNotificationCount == 0,
            "DD polling completes before the queued event is managed-dispatched");
        bool delayedDispatched = dispatcher.TryDispatchBatch(
            null, null, delayed, out delivered, out rejected);
        Expect(delayedDispatched && delivered == 1 && rejected == 0 &&
               delayed.IsSyncTxProofAccepted(synchronousDdCompleted, true, rejected),
            "later managed dispatch completes the already-finished synchronous TX");

        ResetInterruptFixture();
        dispatcher = ManagedInterruptDispatcher.TryCreate(in services);
        Expect(dispatcher != null && dispatcher.TrySubscribeE1000(out _),
            "missing-notification fixture subscribes E1000");
        if (dispatcher == null) return;
        ManagedE1000Driver noNotification = CreateRunningPhase91Driver(dispatcher);
        Expect(ArmSyncTxProof(noNotification), "missing-notification proof arms");
        bool validDdWithoutEvent = true;
        Expect(validDdWithoutEvent &&
               !noNotification.IsSyncTxProofAccepted(validDdWithoutEvent, true, 0),
            "valid descriptor DD without a new TXDW remains a routing-proof failure");
    }

    public static int Main()
    {
        RunDispatcherAndDriverProof();
        RunPhase91SyncTxDispatchRegressions();
        if (s_failures != 0)
        {
            Console.WriteLine("MANAGED_KERNEL_INTERRUPT_HOST_TESTS=FAILED assertions=" +
                              s_assertions + " passed=" + (s_assertions - s_failures) +
                              " failed=" + s_failures + " skipped=0");
            return 1;
        }
        Console.WriteLine("MANAGED_KERNEL_INTERRUPT_HOST_TESTS=PASSED assertions=" +
                          s_assertions + " passed=" + s_assertions +
                          " failed=0 skipped=0");
        return 0;
    }
}

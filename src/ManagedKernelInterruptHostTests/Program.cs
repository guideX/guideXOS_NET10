using System;
using System.Runtime.InteropServices;
using GuideXOS.Net10.ManagedKernel;

internal static unsafe class Program
{
    private const ulong PageSize = KernelArena.PageSize;
    private static uint s_failures;
    private static uint s_transmitCalls;
    private static bool s_interruptActive;
    private static ulong s_interruptToken;
    private static uint s_subscribeCalls;
    private static uint s_unsubscribeCalls;
    private static uint s_eventCount;
    private static uint s_drainedCount;
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
        if (eventType != GxManagedKernelInterruptEventV1.EventTypeSerialReceive ||
            deviceKind != GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial ||
            deviceId != GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 ||
            tokenAddress == 0 || tokenCapacity < sizeof(ulong) || s_interruptActive) return 1;
        s_interruptActive = true;
        s_interruptToken = 0xA901;
        s_subscribeCalls++;
        *(ulong*)tokenAddress = s_interruptToken;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static uint Unsubscribe(ulong token)
    {
        if (!s_interruptActive || token != s_interruptToken) return 7;
        s_interruptActive = false;
        s_unsubscribeCalls++;
        s_eventCount = 0;
        return 0;
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
            EnqueuedCount = s_eventCount,
            DrainedCount = s_drainedCount,
            SubscriptionActive = s_interruptActive ? 1U : 0U,
            HardwareEnabled = s_interruptActive ? 1U : 0U
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

    public static int Main()
    {
        RunDispatcherAndDriverProof();
        if (s_failures != 0)
        {
            Console.WriteLine("MANAGED_KERNEL_INTERRUPT_HOST_TESTS=FAILED failures=" +
                              s_failures);
            return 1;
        }
        Console.WriteLine("MANAGED_KERNEL_INTERRUPT_HOST_TESTS=PASSED");
        return 0;
    }
}

using System;
using System.Runtime.InteropServices;
using GuideXOS.Net10.ManagedKernel;

internal static unsafe class Program
{
    private const ulong PageSize = KernelArena.PageSize;
    private static uint s_failures;
    private static uint s_transmitCalls;
    private static uint s_lastDeviceId;
    private static uint s_lastLength;
    private static byte[] s_lastBytes = Array.Empty<byte>();

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
    private static uint QueryStatus(uint requestedAbiVersion, uint deviceId,
                                     nuint resultAddress, nuint resultCapacity)
    {
        if (requestedAbiVersion != GxManagedKernelSerialServicesV1.AbiVersionCurrent ||
            deviceId != GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 ||
            resultAddress == 0 ||
            resultCapacity < GxManagedKernelSerialStatusV1.ExpectedSize) return 1;
        GxManagedKernelSerialStatusV1* status =
            (GxManagedKernelSerialStatusV1*)resultAddress;
        *status = new GxManagedKernelSerialStatusV1
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
            bufferAddress == 0 || flags != 0 || byteLength == 0 ||
            byteLength > GxManagedKernelSerialServicesV1.MaxTransmitBytes) return 1;
        s_transmitCalls++;
        s_lastDeviceId = deviceId;
        s_lastLength = byteLength;
        s_lastBytes = new byte[byteLength];
        new ReadOnlySpan<byte>((void*)bufferAddress, checked((int)byteLength))
            .CopyTo(s_lastBytes);
        return 0;
    }

    private static void BuildBinding(
        out GxManagedKernelSerialPlatformDeviceV1 device,
        out GxManagedKernelSerialServicesV1 services)
    {
        ulong capabilities = GxManagedKernelSerialServicesV1.CapabilityTransmit |
                              GxManagedKernelSerialServicesV1.CapabilityQueryStatus;
        device = new GxManagedKernelSerialPlatformDeviceV1
        {
            Size = GxManagedKernelSerialPlatformDeviceV1.ExpectedSize,
            AbiVersion = GxManagedKernelSerialServicesV1.AbiVersionCurrent,
            DeviceKind = GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial,
            DeviceId = GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1,
            Capabilities = capabilities,
            ComIndex = GxManagedKernelSerialPlatformDeviceV1.ComIndex1
        };
        services = new GxManagedKernelSerialServicesV1
        {
            Size = GxManagedKernelSerialServicesV1.ExpectedSize,
            AbiVersion = GxManagedKernelSerialServicesV1.AbiVersionCurrent,
            ServiceVersion = GxManagedKernelSerialServicesV1.ServiceVersionCurrent,
            Architecture = GxManagedKernelSerialServicesV1.ArchitectureX64,
            Capabilities = capabilities,
            DeviceKind = device.DeviceKind,
            DeviceId = device.DeviceId,
            ComIndex = device.ComIndex,
            MaxTransmitBytesValue = GxManagedKernelSerialServicesV1.MaxTransmitBytes,
            TransmitAddress = (ulong)(nuint)(delegate* unmanaged<uint, nuint, uint, uint, uint>)&Transmit,
            QueryStatusAddress = (ulong)(nuint)(delegate* unmanaged<uint, uint, nuint, nuint, uint>)&QueryStatus
        };
    }

    private static void TestStateMachineAndArena()
    {
        FakeProvider provider = new();
        BuildBinding(out GxManagedKernelSerialPlatformDeviceV1 device,
                     out GxManagedKernelSerialServicesV1 services);
        ManagedSerialDriver? driver = ManagedSerialDriver.TryCreate(
            provider, in device, in services);
        Expect(driver != null, "managed serial driver creates from native binding");
        if (driver == null) return;

        Expect(!driver.TryWrite("before-start"u8), "write before start rejected");
        Expect(!driver.TryStart(), "start before initialization rejected");
        Expect(!driver.TryStop(), "stop before start rejected");
        Expect(driver.TryInitialize(), "status query initializes driver");
        Expect(!driver.TryInitialize(), "second initialization rejected");
        Expect(driver.TryStart(), "driver starts once");
        Expect(!driver.TryStart(), "second start rejected");
        Expect(driver.TryWrite("MANAGED_SERIAL_DRIVER_HOST_TEST"u8),
               "started driver writes through unmanaged callback");
        Expect(s_transmitCalls == 1 &&
               s_lastDeviceId == GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 &&
               s_lastLength == 31 &&
               System.Text.Encoding.ASCII.GetString(s_lastBytes) ==
                   "MANAGED_SERIAL_DRIVER_HOST_TEST",
               "callback receives exact bounded staging bytes");
        Expect(driver.TryStop(), "driver stops once");
        Expect(!driver.TryStop(), "second stop rejected");
        Expect(!driver.TryWrite("after-stop"u8), "write after stop rejected");
        Expect(driver.Destroy(), "driver destroy releases arena state");
        Expect(driver.IsDestroyed, "destroyed state is explicit");
        Expect(!driver.Destroy(), "second destroy rejected");

        GxManagedKernelSerialPlatformDeviceV1 mismatch = device;
        mismatch.DeviceId++;
        Expect(ManagedSerialDriver.TryCreate(provider, in mismatch, in services) == null,
               "mismatched platform identity does not bind");
        services.Reserved0 = 1;
        Expect(ManagedSerialDriver.TryCreate(provider, in device, in services) == null,
               "nonzero reserved service field does not bind");
        provider.ReleaseAll();
        Expect(provider.AllocationCalls == provider.ReleaseCalls,
               "managed driver arena allocations return to the provider");
    }

    public static int Main()
    {
        TestStateMachineAndArena();
        if (s_failures != 0)
        {
            Console.WriteLine("MANAGED_KERNEL_SERIAL_DRIVER_HOST_TESTS=FAILED failures=" +
                              s_failures);
            return 1;
        }
        Console.WriteLine("MANAGED_KERNEL_SERIAL_DRIVER_HOST_TESTS=PASSED");
        return 0;
    }
}

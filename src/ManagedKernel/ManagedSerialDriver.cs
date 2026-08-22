using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelSerialPlatformDeviceV1
{
    internal const uint ExpectedSize = 32;
    internal const uint DeviceKindPlatformSerial = 2;
    internal const uint DeviceIdCom1 = 1;
    internal const uint ComIndex1 = 1;
    internal const ulong CapabilityTransmit = 1UL << 0;
    internal const ulong CapabilityQueryStatus = 1UL << 1;

    internal uint Size;
    internal uint AbiVersion;
    internal uint DeviceKind;
    internal uint DeviceId;
    internal ulong Capabilities;
    internal uint ComIndex;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelSerialServicesV1
{
    internal const uint ExpectedSize = 72;
    internal const uint AbiVersionCurrent = 1;
    internal const uint ServiceVersionCurrent = 1;
    internal const uint ArchitectureX64 = 0x8664;
    internal const ulong CapabilityTransmit = 1UL << 0;
    internal const ulong CapabilityQueryStatus = 1UL << 1;
    internal const uint MaxTransmitBytes = 1024;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal uint DeviceKind;
    internal uint DeviceId;
    internal uint ComIndex;
    internal uint MaxTransmitBytesValue;
    internal ulong TransmitAddress;
    internal ulong QueryStatusAddress;
    internal ulong Reserved0;
    internal ulong Reserved1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelSerialStatusV1
{
    internal const uint ExpectedSize = 32;
    internal const uint StatusDevicePresent = 1U << 0;
    internal const uint StatusTransmitterReady = 1U << 1;

    internal uint Size;
    internal uint AbiVersion;
    internal uint Status;
    internal uint Reserved0;
    internal ulong Capabilities;
    internal ulong Reserved1;
}

internal static unsafe class ManagedKernelSerialLayout
{
    internal static bool IsValid()
    {
        return sizeof(GxManagedKernelSerialPlatformDeviceV1) == 32 &&
               sizeof(GxManagedKernelSerialServicesV1) == 72 &&
               sizeof(GxManagedKernelSerialStatusV1) == 32 &&
               Marshal.OffsetOf<GxManagedKernelSerialPlatformDeviceV1>(
                   nameof(GxManagedKernelSerialPlatformDeviceV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelSerialPlatformDeviceV1>(
                   nameof(GxManagedKernelSerialPlatformDeviceV1.ComIndex)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelSerialServicesV1>(
                   nameof(GxManagedKernelSerialServicesV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelSerialServicesV1>(
                   nameof(GxManagedKernelSerialServicesV1.MaxTransmitBytesValue)).ToInt32() == 36 &&
               Marshal.OffsetOf<GxManagedKernelSerialServicesV1>(
                   nameof(GxManagedKernelSerialServicesV1.TransmitAddress)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelSerialServicesV1>(
                   nameof(GxManagedKernelSerialServicesV1.QueryStatusAddress)).ToInt32() == 48 &&
               Marshal.OffsetOf<GxManagedKernelSerialStatusV1>(
                   nameof(GxManagedKernelSerialStatusV1.Status)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelSerialStatusV1>(
                   nameof(GxManagedKernelSerialStatusV1.Capabilities)).ToInt32() == 16;
    }
}

internal enum ManagedSerialDriverState : uint
{
    Uninitialized = 0,
    Initialized = 1,
    Started = 2,
    Stopped = 3,
    Disposed = 4
}

internal enum ManagedSerialReceiveState : uint
{
    NotSubscribed = 0,
    Subscribed = 1,
    Stopped = 2
}

internal unsafe sealed class ManagedSerialDriver
{
    internal const uint DriverId = 0x8201;
    internal const uint MaxStateBytes = 64;

    private readonly KernelArena _arena;
    private readonly KernelArenaAllocation _stateAllocation;
    private readonly KernelArenaAllocation _stagingAllocation;
    private readonly GxManagedKernelSerialPlatformDeviceV1 _device;
    private readonly GxManagedKernelSerialServicesV1 _services;
    private ManagedSerialDriverState _state;
    private ManagedSerialReceiveState _receiveState;
    private ulong _receiveSubscriptionId;
    private ulong _lastReceiveSequence;
    private uint _receiveCount;
    private byte _lastReceiveByte;

    private ManagedSerialDriver(
        KernelArena arena,
        in KernelArenaAllocation stateAllocation,
        in KernelArenaAllocation stagingAllocation,
        in GxManagedKernelSerialPlatformDeviceV1 device,
        in GxManagedKernelSerialServicesV1 services)
    {
        _arena = arena;
        _stateAllocation = stateAllocation;
        _stagingAllocation = stagingAllocation;
        _device = device;
        _services = services;
        _state = ManagedSerialDriverState.Uninitialized;
        _receiveState = ManagedSerialReceiveState.NotSubscribed;
    }

    internal ManagedSerialDriverState State => _state;
    internal uint DeviceId => _device.DeviceId;
    internal uint ComIndex => _device.ComIndex;
    internal uint MaxTransmitBytes => _services.MaxTransmitBytesValue;
    internal ManagedSerialReceiveState ReceiveState => _receiveState;
    internal ulong ReceiveSubscriptionId => _receiveSubscriptionId;
    internal uint ReceiveCount => _receiveCount;
    internal ulong LastReceiveSequence => _lastReceiveSequence;
    internal byte LastReceiveByte => _lastReceiveByte;
    internal KernelArenaMetrics Metrics => _arena.IsDestroyed
        ? default : _arena.GetMetrics();
    internal bool IsDestroyed => _state == ManagedSerialDriverState.Disposed;

    internal static ManagedSerialDriver? TryCreate(
        IKernelMemoryProvider provider,
        in GxManagedKernelSerialPlatformDeviceV1 device,
        in GxManagedKernelSerialServicesV1 services)
    {
        KernelArena? arena = null;
        KernelArenaAllocation stateAllocation = default;
        KernelArenaAllocation stagingAllocation = default;

        if (!ValidateBinding(in device, in services) || provider == null ||
            !provider.IsAvailable ||
            KernelArena.TryCreate(provider, 2, 2, 1, 2, 4, 64,
                                  out arena) != KernelArenaStatus.Ok ||
            arena == null)
        {
            return null;
        }

        if (arena.TryAllocate(MaxStateBytes, 8, out stateAllocation) !=
                KernelArenaStatus.Ok ||
            arena.TryAllocate(services.MaxTransmitBytesValue, 8,
                              out stagingAllocation) != KernelArenaStatus.Ok)
        {
            if (stagingAllocation.AllocationId != 0) arena.Free(in stagingAllocation);
            if (stateAllocation.AllocationId != 0) arena.Free(in stateAllocation);
            arena.Destroy();
            return null;
        }

        return new ManagedSerialDriver(arena, in stateAllocation,
                                       in stagingAllocation, in device,
                                       in services);
    }

    internal bool TryInitialize()
    {
        GxManagedKernelSerialStatusV1 status = default;
        if (_state != ManagedSerialDriverState.Uninitialized ||
            _services.QueryStatusAddress == 0)
        {
            return false;
        }

        delegate* unmanaged<uint, uint, nuint, nuint, uint> queryStatus =
            (delegate* unmanaged<uint, uint, nuint, nuint, uint>)
                (nuint)_services.QueryStatusAddress;
        GxManagedKernelSerialStatusV1* statusAddress = &status;
        uint result = queryStatus(_services.AbiVersion, _device.DeviceId,
                                  (nuint)statusAddress,
                                  GxManagedKernelSerialStatusV1.ExpectedSize);
        if (result != ManagedKernelContract.ManagedOk ||
            status.Size != GxManagedKernelSerialStatusV1.ExpectedSize ||
            status.AbiVersion != _services.AbiVersion ||
            status.Reserved0 != 0 || status.Reserved1 != 0 ||
            (status.Status & (GxManagedKernelSerialStatusV1.StatusDevicePresent |
                              GxManagedKernelSerialStatusV1.StatusTransmitterReady)) !=
                (GxManagedKernelSerialStatusV1.StatusDevicePresent |
                 GxManagedKernelSerialStatusV1.StatusTransmitterReady) ||
            status.Capabilities != _services.Capabilities)
        {
            return false;
        }
        _state = ManagedSerialDriverState.Initialized;
        _receiveState = ManagedSerialReceiveState.NotSubscribed;
        return true;
    }

    internal bool TryStart()
    {
        if (_state != ManagedSerialDriverState.Initialized) return false;
        _state = ManagedSerialDriverState.Started;
        _receiveState = ManagedSerialReceiveState.NotSubscribed;
        return true;
    }

    internal bool TryWrite(ReadOnlySpan<byte> bytes)
    {
        if (_state != ManagedSerialDriverState.Started ||
            bytes.Length == 0 || (uint)bytes.Length > MaxTransmitBytes)
        {
            return false;
        }

        Span<byte> staging = new Span<byte>(
            (void*)(nuint)_stagingAllocation.VirtualAddress, bytes.Length);
        bytes.CopyTo(staging);
        delegate* unmanaged<uint, nuint, uint, uint, uint> transmit =
            (delegate* unmanaged<uint, nuint, uint, uint, uint>)
                (nuint)_services.TransmitAddress;
        return transmit(_device.DeviceId,
                        (nuint)_stagingAllocation.VirtualAddress,
                        (uint)bytes.Length, 0) == ManagedKernelContract.ManagedOk;
    }

    internal bool TryStop()
    {
        if (_state != ManagedSerialDriverState.Started ||
            _receiveState == ManagedSerialReceiveState.Subscribed) return false;
        _state = ManagedSerialDriverState.Stopped;
        _receiveState = ManagedSerialReceiveState.Stopped;
        return true;
    }

    internal bool Destroy()
    {
        if (_state == ManagedSerialDriverState.Disposed ||
            _state != ManagedSerialDriverState.Stopped)
        {
            return false;
        }
        if (_arena.Free(in _stagingAllocation) != KernelArenaStatus.Ok ||
            _arena.Free(in _stateAllocation) != KernelArenaStatus.Ok ||
            _arena.Destroy() != KernelArenaStatus.Ok)
        {
            return false;
        }
        _state = ManagedSerialDriverState.Disposed;
        _receiveState = ManagedSerialReceiveState.Stopped;
        return true;
    }

    internal bool TrySubscribeReceive(ManagedInterruptDispatcher dispatcher)
    {
        ulong subscriptionId;
        if (_state != ManagedSerialDriverState.Started ||
            _receiveState != ManagedSerialReceiveState.NotSubscribed ||
            dispatcher == null ||
            !dispatcher.TrySubscribe(GxManagedKernelInterruptEventV1.EventTypeSerialReceive,
                _device.DeviceKind, _device.DeviceId, out subscriptionId))
        {
            return false;
        }
        _receiveSubscriptionId = subscriptionId;
        _receiveState = ManagedSerialReceiveState.Subscribed;
        return true;
    }

    internal bool TryUnsubscribeReceive(ManagedInterruptDispatcher dispatcher)
    {
        if (_receiveState != ManagedSerialReceiveState.Subscribed ||
            dispatcher == null || !dispatcher.TryUnsubscribe()) return false;
        _receiveSubscriptionId = 0;
        _receiveState = ManagedSerialReceiveState.NotSubscribed;
        return true;
    }

    internal bool TryHandleReceive(
        in GxManagedKernelInterruptEventV1 value, byte expectedPayload)
    {
        if (_state != ManagedSerialDriverState.Started ||
            _receiveState != ManagedSerialReceiveState.Subscribed ||
            value.DeviceKind != _device.DeviceKind ||
            value.DeviceId != _device.DeviceId ||
            value.PayloadByte != expectedPayload || value.Sequence == 0 ||
            (_receiveCount != 0 && value.Sequence != _lastReceiveSequence + 1))
        {
            return false;
        }
        _lastReceiveSequence = value.Sequence;
        _lastReceiveByte = value.PayloadByte;
        _receiveCount++;
        return true;
    }

    internal bool TryRunReceiveRuntimeArenaProof()
    {
        KernelArenaAllocation allocation;
        if (_state != ManagedSerialDriverState.Started ||
            _receiveState != ManagedSerialReceiveState.Subscribed ||
            _arena.TryAllocate(32, 8, out allocation) != KernelArenaStatus.Ok)
        {
            return false;
        }
        Span<byte> bytes = new Span<byte>(
            (void*)(nuint)allocation.VirtualAddress, 32);
        bytes.Fill(0xD9);
        bool valid = bytes[0] == 0xD9 && bytes[31] == 0xD9;
        valid = _arena.Free(in allocation) == KernelArenaStatus.Ok && valid;
        return valid;
    }

    internal static bool TryRunNegativeTests(
        IKernelMemoryProvider provider,
        in GxManagedKernelSerialPlatformDeviceV1 device,
        in GxManagedKernelSerialServicesV1 services)
    {
        GxManagedKernelSerialPlatformDeviceV1 mismatch = device;
        ManagedSerialDriver? candidate = TryCreate(provider, in device, in services);
        if (candidate == null) return false;
        try
        {
            mismatch.DeviceId++;
            return TryCreate(provider, in mismatch, in services) == null &&
                   !candidate.TryWrite("X"u8) && !candidate.TryStart() &&
                   !candidate.TryStop() && candidate.TryInitialize() &&
                   !candidate.TryInitialize() && !candidate.TryWrite("X"u8) &&
                   candidate.TryStart() && !candidate.TryStart() &&
                   candidate.TryStop() && !candidate.TryStop() &&
                   !candidate.TryWrite("X"u8) && candidate.Destroy() &&
                   !candidate.Destroy();
        }
        finally
        {
            if (!candidate.IsDestroyed)
            {
                if (candidate.State == ManagedSerialDriverState.Started)
                {
                    candidate.TryStop();
                }
                if (candidate.State == ManagedSerialDriverState.Stopped)
                {
                    candidate.Destroy();
                }
            }
        }
    }

    private static bool ValidateBinding(
        in GxManagedKernelSerialPlatformDeviceV1 device,
        in GxManagedKernelSerialServicesV1 services)
    {
        const ulong knownCapabilities =
            GxManagedKernelSerialPlatformDeviceV1.CapabilityTransmit |
            GxManagedKernelSerialPlatformDeviceV1.CapabilityQueryStatus;
        return device.Size == GxManagedKernelSerialPlatformDeviceV1.ExpectedSize &&
               device.AbiVersion == GxManagedKernelSerialServicesV1.AbiVersionCurrent &&
               device.DeviceKind == GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial &&
               device.DeviceId == GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 &&
               device.ComIndex == GxManagedKernelSerialPlatformDeviceV1.ComIndex1 &&
               device.Capabilities == knownCapabilities && device.Reserved == 0 &&
               services.Size == GxManagedKernelSerialServicesV1.ExpectedSize &&
               services.AbiVersion == GxManagedKernelSerialServicesV1.AbiVersionCurrent &&
               services.ServiceVersion == GxManagedKernelSerialServicesV1.ServiceVersionCurrent &&
               services.Architecture == GxManagedKernelSerialServicesV1.ArchitectureX64 &&
               services.Capabilities == knownCapabilities &&
               services.DeviceKind == device.DeviceKind &&
               services.DeviceId == device.DeviceId &&
               services.ComIndex == device.ComIndex &&
               services.MaxTransmitBytesValue != 0 &&
               services.MaxTransmitBytesValue <= GxManagedKernelSerialServicesV1.MaxTransmitBytes &&
               services.TransmitAddress != 0 && services.QueryStatusAddress != 0 &&
               services.Reserved0 == 0 && services.Reserved1 == 0;
    }
}

internal static unsafe class ManagedSerialDriverSubsystem
{
    private const uint AbiVersionV1 = 1;
    private const ulong KnownCapabilities =
        GxManagedKernelSerialServicesV1.CapabilityTransmit |
        GxManagedKernelSerialServicesV1.CapabilityQueryStatus;

    private static int s_installed;
    private static int s_run;
    private static int s_interruptInstalled;
    private static int s_phase9State;
    private static GxManagedKernelSerialPlatformDeviceV1 s_device;
    private static GxManagedKernelSerialServicesV1 s_services;
    private static GxManagedKernelInterruptServicesV1 s_interruptServices;
    private static ManagedInterruptDispatcher? s_interruptDispatcher;
    private static ManagedSerialDriver? s_operationalDriver;

    internal static bool Installed => s_installed != 0;

    internal static uint Install(uint requestedAbiVersion, nuint servicesAddress,
                                 nuint deviceAddress)
    {
        GxManagedKernelSerialServicesV1 services;
        GxManagedKernelSerialPlatformDeviceV1 device;

        if (requestedAbiVersion != AbiVersionV1) return ManagedKernelContract.UnsupportedAbi;
        if (!ManagedKernelContract.IsStarted ||
            ManagedKernelContract.OperationalDeviceInventory == null ||
            ManagedKernelContract.OperationalDriverRegistry == null)
        {
            return ManagedKernelContract.InvalidState;
        }
        if (s_installed != 0) return ManagedKernelContract.AlreadyInitialized;
        if (servicesAddress == 0 || deviceAddress == 0 ||
            !ManagedKernelContract.IsRangeValid(servicesAddress,
                GxManagedKernelSerialServicesV1.ExpectedSize) ||
            !ManagedKernelContract.IsRangeValid(deviceAddress,
                GxManagedKernelSerialPlatformDeviceV1.ExpectedSize))
        {
            return ManagedKernelContract.InvalidArgument;
        }
        services = *(GxManagedKernelSerialServicesV1*)servicesAddress;
        device = *(GxManagedKernelSerialPlatformDeviceV1*)deviceAddress;
        if (!ValidateService(in services) || !ValidateDevice(in device) ||
            services.DeviceKind != device.DeviceKind ||
            services.DeviceId != device.DeviceId ||
            services.ComIndex != device.ComIndex ||
            services.Capabilities != device.Capabilities)
        {
            return ManagedKernelContract.InvalidArgument;
        }
        s_services = services;
        s_device = device;
        s_installed = 1;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_SERVICES_INSTALLED\r\n"u8))
        {
            s_installed = 0;
            s_services = default;
            s_device = default;
            return ManagedKernelContract.InvalidState;
        }
        return ManagedKernelContract.ManagedOk;
    }

    internal static uint RunAccounting()
    {
        ManagedSerialDriver? candidate;
        if (!ManagedKernelContract.IsStarted || s_installed == 0 || s_run != 0)
        {
            return ManagedKernelContract.InvalidState;
        }
        candidate = ManagedSerialDriver.TryCreate(
            Phase4KernelMemoryProvider.Instance, in s_device, in s_services);
        if (candidate == null || !candidate.TryInitialize() ||
            !candidate.TryStart() || !candidate.TryStop() ||
            candidate.State != ManagedSerialDriverState.Stopped ||
            !candidate.Destroy() || !candidate.IsDestroyed)
        {
            if (candidate != null && !candidate.IsDestroyed)
            {
                if (candidate.State == ManagedSerialDriverState.Started) candidate.TryStop();
                if (candidate.State == ManagedSerialDriverState.Stopped) candidate.Destroy();
            }
            return ManagedKernelContract.InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_STOP_OK\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_ACCOUNTING_RESTORED\r\n"u8))
        {
            return ManagedKernelContract.InvalidState;
        }
        return ManagedKernelContract.ManagedOk;
    }

    internal static uint Run()
    {
        ManagedSerialDriver? driver;
        ManagedDeviceInventory? inventory;
        ManagedDriverRegistry? registry;
        KernelMemoryRegion runtimeRegion = default;
        bool runtimeRegionLive = false;

        if (!ManagedKernelContract.IsStarted || s_installed == 0 || s_run != 0)
        {
            return ManagedKernelContract.InvalidState;
        }
        inventory = ManagedKernelContract.OperationalDeviceInventory;
        registry = ManagedKernelContract.OperationalDriverRegistry;
        if (inventory == null || registry == null || !inventory.ValidateInvariants() ||
            !registry.ValidateInvariants() || inventory.DeviceCount == 0)
        {
            return ManagedKernelContract.InvalidState;
        }
        driver = ManagedSerialDriver.TryCreate(
            Phase4KernelMemoryProvider.Instance, in s_device, in s_services);
        if (driver == null) return ManagedKernelContract.ResourceExhausted;

        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DEVICE_BOUND\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_ID=0x"u8,
                                    ManagedSerialDriver.DriverId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_SERIAL_DEVICE_ID=0x"u8,
                                    driver.DeviceId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_SERIAL_COM_INDEX=0x"u8,
                                    driver.ComIndex) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_SERIAL_MAX_TX=0x"u8,
                                    driver.MaxTransmitBytes) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_SERIAL_ARENA_PAGES=0x"u8,
                driver.Metrics.TotalBackingBytes / KernelArena.PageSize) ||
            !driver.TryInitialize() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_INIT_OK\r\n"u8) ||
            !driver.TryStart() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_START_OK\r\n"u8) ||
            !driver.TryWrite("MANAGED_SERIAL_DRIVER_TX_FROM_CSHARP\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_TX_OK\r\n"u8))
        {
            Cleanup(driver);
            return ManagedKernelContract.InvalidState;
        }

        if (!ManagedKernelContract.TryQueryMonotonicTime(out _) ||
            !KernelMemory.TryAllocate(1, 0, out runtimeRegion))
        {
            Cleanup(driver);
            return ManagedKernelContract.InvalidState;
        }
        runtimeRegionLive = true;
        byte* runtimeAddress = (byte*)(nuint)runtimeRegion.VirtualAddress;
        runtimeAddress[0] = 0xA7;
        byte[] gcActivity = new byte[2048];
        gcActivity[0] = 0x5C;
        GC.Collect();
        GC.KeepAlive(gcActivity);
        bool runtimeValid = runtimeAddress[0] == 0xA7 &&
                            inventory.ValidateInvariants() &&
                            registry.ValidateInvariants() &&
                            inventory.TryGetDevice(0, out ManagedDevice pciDevice) &&
                            PciConfiguration.TryRead16(in pciDevice, 0, out _);
        if (!KernelMemory.TryRelease(in runtimeRegion)) runtimeValid = false;
        runtimeRegionLive = false;
        if (!runtimeValid ||
            !driver.TryWrite("MANAGED_SERIAL_DRIVER_TX_AFTER_RUNTIME\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_RUNTIME_SURVIVAL_OK\r\n"u8) ||
            !ManagedSerialDriver.TryRunNegativeTests(
                Phase4KernelMemoryProvider.Instance, in s_device, in s_services) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_NEGATIVE_TESTS_OK\r\n"u8))
        {
            Cleanup(driver);
            return ManagedKernelContract.InvalidState;
        }

        s_operationalDriver = driver;
        s_run = 1;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_DRIVER_OPERATIONAL_OK\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE8_PASS\r\n"u8))
        {
            s_operationalDriver = null;
            s_run = 0;
            Cleanup(driver);
            return ManagedKernelContract.InvalidState;
        }
        return ManagedKernelContract.ManagedOk;

        void Cleanup(ManagedSerialDriver value)
        {
            if (runtimeRegionLive) KernelMemory.TryRelease(in runtimeRegion);
            if (value.State == ManagedSerialDriverState.Started) value.TryStop();
            if (value.State == ManagedSerialDriverState.Stopped) value.Destroy();
        }
    }

    internal static uint InstallInterruptServices(uint requestedAbiVersion,
                                                   nuint servicesAddress)
    {
        GxManagedKernelInterruptServicesV1 services;
        ManagedInterruptDispatcher? dispatcher;
        if (requestedAbiVersion != GxManagedKernelInterruptServicesV1.AbiVersionCurrent)
        {
            return ManagedKernelContract.UnsupportedAbi;
        }
        if (!ManagedKernelContract.IsStarted || s_installed == 0 || s_run == 0)
        {
            return ManagedKernelContract.InvalidState;
        }
        if (s_interruptInstalled != 0 || servicesAddress == 0 ||
            !ManagedKernelContract.IsRangeValid(servicesAddress,
                GxManagedKernelInterruptServicesV1.ExpectedSize))
        {
            return s_interruptInstalled != 0 ? ManagedKernelContract.AlreadyInitialized :
                ManagedKernelContract.InvalidArgument;
        }
        services = *(GxManagedKernelInterruptServicesV1*)servicesAddress;
        dispatcher = ManagedInterruptDispatcher.TryCreate(in services);
        if (dispatcher == null) return ManagedKernelContract.InvalidArgument;
        s_interruptServices = services;
        s_interruptDispatcher = dispatcher;
        s_interruptInstalled = 1;
        s_phase9State = 0;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_INTERRUPT_SERVICES_INSTALLED\r\n"u8))
        {
            s_interruptDispatcher = null;
            s_interruptServices = default;
            s_interruptInstalled = 0;
            return ManagedKernelContract.InvalidState;
        }
        return ManagedKernelContract.ManagedOk;
    }

    internal static uint RunPhase9(uint stage)
    {
        ManagedSerialDriver? driver = s_operationalDriver;
        ManagedInterruptDispatcher? dispatcher = s_interruptDispatcher;
        if (!ManagedKernelContract.IsStarted || s_interruptInstalled == 0 ||
            s_run == 0 || driver == null || dispatcher == null)
        {
            return ManagedKernelContract.InvalidState;
        }
        if (stage == 1)
        {
            ulong ignored;
            if (s_phase9State != 0 ||
                dispatcher.TrySubscribe(GxManagedKernelInterruptEventV1.EventTypeSerialReceive + 1,
                    driver.DeviceId == GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1
                        ? GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial : 0,
                    driver.DeviceId, out ignored) ||
                !driver.TrySubscribeReceive(dispatcher) ||
                dispatcher.TrySubscribe(GxManagedKernelInterruptEventV1.EventTypeSerialReceive,
                    GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial,
                    driver.DeviceId, out ignored))
            {
                return ManagedKernelContract.InvalidState;
            }
            s_phase9State = 1;
            return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_SUBSCRIBED\r\n"u8) &&
                   KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_READY\r\n"u8)
                ? ManagedKernelContract.ManagedOk : ManagedKernelContract.InvalidState;
        }
        if (stage == 2)
        {
            uint delivered;
            byte expected = driver.ReceiveCount == 0 ? (byte)'R' : (byte)'S';
            if ((s_phase9State != 1 && s_phase9State != 3) ||
                !dispatcher.TryDispatch(driver, expected, out delivered) ||
                delivered != 1) return ManagedKernelContract.InvalidState;
            if (driver.ReceiveCount == 1)
            {
                s_phase9State = 2;
                return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_INTERRUPT_EVENT_DISPATCHED\r\n"u8) &&
                       KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_FROM_HARDWARE_OK\r\n"u8)
                    ? ManagedKernelContract.ManagedOk : ManagedKernelContract.InvalidState;
            }
            if (driver.ReceiveCount == 2)
            {
                s_phase9State = 4;
                return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_INTERRUPT_EVENT_DISPATCHED\r\n"u8) &&
                       KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_AFTER_RUNTIME_OK\r\n"u8)
                    ? ManagedKernelContract.ManagedOk : ManagedKernelContract.InvalidState;
            }
            return ManagedKernelContract.InvalidState;
        }
        if (stage == 3)
        {
            KernelMemoryRegion region = default;
            bool live = false;
            byte* address;
            bool valid = s_phase9State == 2 && driver.ReceiveCount == 1 &&
                         ManagedKernelContract.TryInvokeHostLog(
                             "GXOS_NET10:MANAGED_KERNEL_PHASE9_RUNTIME_ACTIVITY\r\n"u8) &&
                         ManagedKernelContract.TryQueryMonotonicTime(out _) &&
                         driver.TryRunReceiveRuntimeArenaProof() &&
                         KernelMemory.TryAllocate(1, 0, out region);
            if (!valid) return ManagedKernelContract.InvalidState;
            live = true;
            address = (byte*)(nuint)region.VirtualAddress;
            address[0] = 0xC6;
            byte[] gcActivity = new byte[2048];
            gcActivity[0] = 0x7E;
            GC.Collect();
            GC.KeepAlive(gcActivity);
            valid = address[0] == 0xC6 &&
                    ManagedKernelContract.OperationalDeviceInventory != null &&
                    ManagedKernelContract.OperationalDeviceInventory.ValidateInvariants() &&
                    ManagedKernelContract.OperationalDriverRegistry != null &&
                    ManagedKernelContract.OperationalDriverRegistry.ValidateInvariants();
            if (ManagedKernelContract.OperationalDeviceInventory != null &&
                ManagedKernelContract.OperationalDeviceInventory.DeviceCount != 0 &&
                ManagedKernelContract.OperationalDeviceInventory.TryGetDevice(0,
                    out ManagedDevice device))
            {
                valid = valid && PciConfiguration.TryRead16(in device, 0, out _);
            }
            if (!KernelMemory.TryRelease(in region)) valid = false;
            live = false;
            if (!valid || !driver.TryWrite("MANAGED_SERIAL_DRIVER_TX_PHASE9_RUNTIME\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_RUNTIME_SURVIVAL_OK\r\n"u8))
            {
                if (live) KernelMemory.TryRelease(in region);
                return ManagedKernelContract.InvalidState;
            }
            s_phase9State = 3;
            return ManagedKernelContract.ManagedOk;
        }
        if (stage == 4)
        {
            if (s_phase9State != 4 || !driver.TryUnsubscribeReceive(dispatcher) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_SERIAL_RX_UNSUBSCRIBE_OK\r\n"u8) ||
                !driver.TryStop() || !driver.Destroy())
            {
                return ManagedKernelContract.InvalidState;
            }
            s_operationalDriver = null;
            s_run = 0;
            s_phase9State = 5;
            if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_INTERRUPT_NEGATIVE_TESTS_OK\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_INTERRUPT_ACCOUNTING_RESTORED\r\n"u8))
            {
                return ManagedKernelContract.InvalidState;
            }
            return ManagedKernelContract.ManagedOk;
        }
        return ManagedKernelContract.InvalidArgument;
    }

    private static bool ValidateService(in GxManagedKernelSerialServicesV1 services)
    {
        return services.Size == GxManagedKernelSerialServicesV1.ExpectedSize &&
               services.AbiVersion == GxManagedKernelSerialServicesV1.AbiVersionCurrent &&
               services.ServiceVersion == GxManagedKernelSerialServicesV1.ServiceVersionCurrent &&
               services.Architecture == GxManagedKernelSerialServicesV1.ArchitectureX64 &&
               (services.Capabilities & ~KnownCapabilities) == 0 &&
               (services.Capabilities & KnownCapabilities) == KnownCapabilities &&
               services.DeviceKind == GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial &&
               services.DeviceId == GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 &&
               services.ComIndex == GxManagedKernelSerialPlatformDeviceV1.ComIndex1 &&
               services.MaxTransmitBytesValue != 0 &&
               services.MaxTransmitBytesValue <= GxManagedKernelSerialServicesV1.MaxTransmitBytes &&
               services.TransmitAddress != 0 && services.QueryStatusAddress != 0 &&
               services.Reserved0 == 0 && services.Reserved1 == 0;
    }

    private static bool ValidateDevice(in GxManagedKernelSerialPlatformDeviceV1 device)
    {
        return device.Size == GxManagedKernelSerialPlatformDeviceV1.ExpectedSize &&
               device.AbiVersion == GxManagedKernelSerialServicesV1.AbiVersionCurrent &&
               device.DeviceKind == GxManagedKernelSerialPlatformDeviceV1.DeviceKindPlatformSerial &&
               device.DeviceId == GxManagedKernelSerialPlatformDeviceV1.DeviceIdCom1 &&
               device.Capabilities == KnownCapabilities &&
               device.ComIndex == GxManagedKernelSerialPlatformDeviceV1.ComIndex1 &&
               device.Reserved == 0;
    }
}

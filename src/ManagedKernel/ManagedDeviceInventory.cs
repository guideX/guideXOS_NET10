using System;

namespace GuideXOS.Net10.ManagedKernel;

internal readonly struct ManagedDevice
{
    private readonly GxManagedKernelDeviceV1 _descriptor;

    internal ManagedDevice(in GxManagedKernelDeviceV1 descriptor)
    {
        _descriptor = descriptor;
    }

    internal ushort Segment => _descriptor.Segment;
    internal byte Bus => _descriptor.Bus;
    internal byte Device => _descriptor.Device;
    internal byte Function => _descriptor.Function;
    internal ushort VendorId => _descriptor.VendorId;
    internal ushort DeviceId => _descriptor.DeviceId;
    internal byte RevisionId => _descriptor.RevisionId;
    internal byte ClassCode => _descriptor.ClassCode;
    internal byte Subclass => _descriptor.Subclass;
    internal byte ProgrammingInterface => _descriptor.ProgrammingInterface;
    internal byte HeaderType => _descriptor.HeaderType;
    internal uint Flags => _descriptor.Flags;
    internal uint ResourceStartIndex => _descriptor.ResourceStartIndex;
    internal uint ResourceCount => _descriptor.ResourceCount;

    internal bool HasSamePciLocation(in ManagedDevice other)
    {
        return Segment == other.Segment && Bus == other.Bus &&
               Device == other.Device && Function == other.Function;
    }
}

internal unsafe sealed class ManagedDeviceInventory
{
    internal const uint MaxDevices = 256;
    internal const uint MaxResources = 1024;
    internal const ulong DeviceStorageBytes =
        (ulong)MaxDevices * GxManagedKernelDeviceV1.ExpectedSize;
    internal const ulong BdfIndexBytes = (ulong)MaxDevices * sizeof(ulong);
    internal const ulong ClassIndexBytes = (ulong)MaxDevices * sizeof(uint);

    private readonly KernelArena _arena;
    private readonly KernelArenaAllocation _deviceStorage;
    private readonly KernelArenaAllocation _bdfIndexStorage;
    private readonly KernelArenaAllocation _classIndexStorage;
    private readonly GxManagedKernelDeviceV1* _devices;
    private readonly ulong* _bdfIndex;
    private readonly uint* _classIndex;
    private readonly uint _deviceCount;
    private bool _destroyed;

    private ManagedDeviceInventory(
        KernelArena arena,
        in KernelArenaAllocation deviceStorage,
        in KernelArenaAllocation bdfIndexStorage,
        in KernelArenaAllocation classIndexStorage,
        uint deviceCount)
    {
        _arena = arena;
        _deviceStorage = deviceStorage;
        _bdfIndexStorage = bdfIndexStorage;
        _classIndexStorage = classIndexStorage;
        _devices = (GxManagedKernelDeviceV1*)(nuint)deviceStorage.VirtualAddress;
        _bdfIndex = (ulong*)(nuint)bdfIndexStorage.VirtualAddress;
        _classIndex = (uint*)(nuint)classIndexStorage.VirtualAddress;
        _deviceCount = deviceCount;
    }

    internal uint DeviceCount => _destroyed ? 0U : _deviceCount;
    internal uint ResourceCount => 0U;
    internal KernelArenaMetrics Metrics => _destroyed
        ? default : _arena.GetMetrics();
    internal bool IsDestroyed => _destroyed;

    internal static bool TryCreateFromPublication(
        IKernelMemoryProvider provider,
        nuint publicationAddress,
        out ManagedDeviceInventory? inventory)
    {
        inventory = null;
        if (provider == null || publicationAddress == 0)
        {
            return false;
        }

        GxManagedKernelDeviceInventoryPublicationV1* publication =
            (GxManagedKernelDeviceInventoryPublicationV1*)publicationAddress;
        if (publication->Size !=
                GxManagedKernelDeviceInventoryPublicationV1.ExpectedSize ||
            publication->AbiVersion != 1 || publication->SummaryAddress == 0 ||
            publication->DescriptorAddress == 0 ||
            publication->DescriptorCount == 0 ||
            publication->DescriptorCount > MaxDevices ||
            publication->DescriptorSize != GxManagedKernelDeviceV1.ExpectedSize ||
            publication->Reserved != 0)
        {
            return false;
        }

        ulong expectedBytes = (ulong)publication->DescriptorCount *
                              GxManagedKernelDeviceV1.ExpectedSize;
        if (expectedBytes != (ulong)publication->DescriptorByteLength ||
            !IsRangeValid(publication->SummaryAddress,
                          GxManagedKernelDeviceInventorySummaryV1.ExpectedSize) ||
            !IsRangeValid(publication->DescriptorAddress,
                          (nuint)publication->DescriptorByteLength))
        {
            return false;
        }

        GxManagedKernelDeviceInventorySummaryV1* summary =
            (GxManagedKernelDeviceInventorySummaryV1*)publication->SummaryAddress;
        if (summary->Size != GxManagedKernelDeviceInventorySummaryV1.ExpectedSize ||
            summary->AbiVersion != 1 || summary->ServiceVersion != 1 ||
            summary->Architecture != 0x8664 ||
            summary->DeviceCount != publication->DescriptorCount ||
            summary->ResourceCount != 0 ||
            summary->Capabilities !=
                (GxManagedKernelDeviceInventorySummaryV1.CapabilitySummary |
                 GxManagedKernelDeviceInventorySummaryV1.CapabilityDevices |
                 GxManagedKernelDeviceInventorySummaryV1.CapabilityImmutableBootSnapshot) ||
            summary->Reserved != 0)
        {
            return false;
        }

        return TryCreateFromDescriptors(
            provider,
            (GxManagedKernelDeviceV1*)publication->DescriptorAddress,
            publication->DescriptorCount,
            out inventory);
    }

    internal static bool TryCreateFromDescriptors(
        IKernelMemoryProvider provider,
        GxManagedKernelDeviceV1* descriptors,
        uint deviceCount,
        out ManagedDeviceInventory? inventory)
    {
        inventory = null;
        if (provider == null || descriptors == null || deviceCount == 0 ||
            deviceCount > MaxDevices)
        {
            return false;
        }
        if (!ValidateDescriptors(descriptors, deviceCount))
        {
            return false;
        }

        if (KernelArena.TryCreate(provider, 2, 2, 4, 8, 8, 4096,
                                  out KernelArena? arena) != KernelArenaStatus.Ok ||
            arena == null)
        {
            return false;
        }

        KernelArenaAllocation deviceStorage = default;
        KernelArenaAllocation bdfIndexStorage = default;
        KernelArenaAllocation classIndexStorage = default;
        if (arena.TryAllocate(DeviceStorageBytes, 8, out deviceStorage) !=
                KernelArenaStatus.Ok ||
            arena.TryAllocate(BdfIndexBytes, 8, out bdfIndexStorage) !=
                KernelArenaStatus.Ok ||
            arena.TryAllocate(ClassIndexBytes, 8, out classIndexStorage) !=
                KernelArenaStatus.Ok)
        {
            if (deviceStorage.AllocationId != 0) arena.Free(in deviceStorage);
            if (bdfIndexStorage.AllocationId != 0) arena.Free(in bdfIndexStorage);
            if (classIndexStorage.AllocationId != 0) arena.Free(in classIndexStorage);
            arena.Destroy();
            return false;
        }

        ManagedDeviceInventory candidate = new ManagedDeviceInventory(
            arena, in deviceStorage, in bdfIndexStorage, in classIndexStorage,
            deviceCount);
        for (uint index = 0; index != deviceCount; ++index)
        {
            candidate._devices[index] = descriptors[index];
            candidate._bdfIndex[index] = MakeBdfKey(in descriptors[index]);
            candidate._classIndex[index] = MakeClassKey(in descriptors[index]);
        }
        for (uint index = deviceCount; index != MaxDevices; ++index)
        {
            candidate._devices[index] = default;
            candidate._bdfIndex[index] = 0;
            candidate._classIndex[index] = 0;
        }

        if (!candidate.ValidateInvariants())
        {
            candidate.Destroy();
            return false;
        }
        inventory = candidate;
        return true;
    }

    internal bool TryGetDevice(uint index, out ManagedDevice device)
    {
        device = default;
        if (_destroyed || index >= _deviceCount) return false;
        device = new ManagedDevice(in _devices[index]);
        return true;
    }

    internal bool TryGetDescriptor(
        uint index, out GxManagedKernelDeviceV1 descriptor)
    {
        descriptor = default;
        if (_destroyed || index >= _deviceCount) return false;
        descriptor = _devices[index];
        return true;
    }

    internal bool TryFindPciDevice(
        ushort segment, byte bus, byte device, byte function,
        out ManagedDevice result)
    {
        result = default;
        if (_destroyed || segment != 0) return false;
        ulong key = MakeBdfKey(segment, bus, device, function);
        for (uint index = 0; index != _deviceCount; ++index)
        {
            if (_bdfIndex[index] != key) continue;
            result = new ManagedDevice(in _devices[index]);
            return true;
        }
        return false;
    }

    internal bool TryFindFirstByClass(
        byte classCode, byte subclass, out ManagedDevice result)
    {
        result = default;
        if (_destroyed) return false;
        uint prefix = ((uint)classCode << 16) | ((uint)subclass << 8);
        for (uint index = 0; index != _deviceCount; ++index)
        {
            if ((_classIndex[index] & 0xFFFF00U) != prefix) continue;
            result = new ManagedDevice(in _devices[index]);
            return true;
        }
        return false;
    }

    internal bool TryGetResource(uint index, out uint deviceIndex)
    {
        deviceIndex = 0;
        return false;
    }

    internal bool ValidateInvariants()
    {
        if (_destroyed || !_arena.ValidateInvariants() || _deviceCount == 0 ||
            _deviceCount > MaxDevices || _arena.LiveAllocationCount != 3)
        {
            return false;
        }
        for (uint index = 0; index != _deviceCount; ++index)
        {
            GxManagedKernelDeviceV1 descriptor = _devices[index];
            if (!ValidateDescriptor(in descriptor) ||
                _bdfIndex[index] != MakeBdfKey(in descriptor) ||
                _classIndex[index] != MakeClassKey(in descriptor))
            {
                return false;
            }
            for (uint other = index + 1; other != _deviceCount; ++other)
            {
                if (_bdfIndex[index] == _bdfIndex[other]) return false;
            }
        }
        return true;
    }

    internal bool TryRunRuntimeSurvival()
    {
        if (_destroyed || !TryGetDevice(0, out ManagedDevice first)) return false;
        if (ManagedKernelContract.HostServicesInstalled &&
            !ManagedKernelContract.TryQueryMonotonicTime(out _))
        {
            return false;
        }
        ManagedDevice last = first;
        if (_deviceCount > 1 && !TryGetDevice(_deviceCount - 1, out last))
        {
            return false;
        }
        byte[] activity = new byte[4096];
        activity[0] = 0x5A;
        GC.Collect();
        GC.KeepAlive(activity);
        return _deviceCount == DeviceCount &&
               TryFindPciDevice(first.Segment, first.Bus, first.Device,
                                first.Function, out ManagedDevice firstAgain) &&
               firstAgain.VendorId == first.VendorId &&
               TryFindPciDevice(last.Segment, last.Bus, last.Device,
                                last.Function, out ManagedDevice lastAgain) &&
               lastAgain.DeviceId == last.DeviceId && ValidateInvariants();
    }

    internal bool TryCreateTestCopy(
        IKernelMemoryProvider provider,
        out ManagedDeviceInventory? inventory)
    {
        if (_destroyed)
        {
            inventory = null;
            return false;
        }
        return TryCreateFromDescriptors(provider, _devices, _deviceCount,
                                        out inventory);
    }

    internal bool Destroy()
    {
        if (_destroyed) return false;
        if ( _arena.Free(in _classIndexStorage) != KernelArenaStatus.Ok ||
             _arena.Free(in _bdfIndexStorage) != KernelArenaStatus.Ok ||
             _arena.Free(in _deviceStorage) != KernelArenaStatus.Ok ||
             _arena.Destroy() != KernelArenaStatus.Ok)
        {
            return false;
        }
        _destroyed = true;
        return true;
    }

    private static bool ValidateDescriptors(
        GxManagedKernelDeviceV1* descriptors, uint deviceCount)
    {
        for (uint index = 0; index != deviceCount; ++index)
        {
            if (!ValidateDescriptor(in descriptors[index])) return false;
            ulong key = MakeBdfKey(in descriptors[index]);
            for (uint prior = 0; prior != index; ++prior)
            {
                if (key == MakeBdfKey(in descriptors[prior])) return false;
            }
        }
        return true;
    }

    private static bool ValidateDescriptor(in GxManagedKernelDeviceV1 descriptor)
    {
        byte headerLayout = (byte)(descriptor.HeaderType & 0x7F);
        return descriptor.Size == GxManagedKernelDeviceV1.ExpectedSize &&
               descriptor.AbiVersion == 1 &&
               descriptor.DeviceKind == GxManagedKernelDeviceV1.DeviceKindPci &&
               (descriptor.Flags & ~GxManagedKernelDeviceV1.FlagPciMultifunction) == 0 &&
               descriptor.Function < 8 && descriptor.VendorId != 0xFFFF &&
               descriptor.VendorId != 0 && headerLayout <= 2 &&
               descriptor.ReservedLocation == 0 && descriptor.ReservedClass == 0 &&
               descriptor.ResourceStartIndex == 0 && descriptor.ResourceCount == 0 &&
               descriptor.Reserved == 0;
    }

    private static bool IsRangeValid(nuint address, nuint length)
    {
        return address != 0 && length != 0 && address <= nuint.MaxValue - length;
    }

    private static ulong MakeBdfKey(in GxManagedKernelDeviceV1 descriptor)
    {
        return MakeBdfKey(descriptor.Segment, descriptor.Bus, descriptor.Device,
                          descriptor.Function);
    }

    private static ulong MakeBdfKey(
        ushort segment, byte bus, byte device, byte function)
    {
        return ((ulong)segment << 32) | ((ulong)bus << 24) |
               ((ulong)device << 16) | ((ulong)function << 8) | 1UL;
    }

    private static uint MakeClassKey(in GxManagedKernelDeviceV1 descriptor)
    {
        return ((uint)descriptor.ClassCode << 16) |
               ((uint)descriptor.Subclass << 8) | descriptor.ProgrammingInterface;
    }
}

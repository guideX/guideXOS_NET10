using System;
using System.Runtime.InteropServices;
using GuideXOS.Net10.ManagedKernel;

internal static unsafe class Program
{
    private const ulong PageSize = KernelArena.PageSize;
    private static uint s_failures;

    private sealed class FakeProvider : IKernelMemoryProvider
    {
        private readonly (bool Live, nint Raw, KernelMemoryRegion Region)[] _slots =
            new (bool, nint, KernelMemoryRegion)[128];
        internal uint AllocationCalls;
        internal uint ReleaseCalls;
        internal bool IsAvailable => true;

        bool IKernelMemoryProvider.IsAvailable => IsAvailable;

        public bool TryAllocate(ulong pageCount, uint flags,
                                out KernelMemoryRegion region)
        {
            region = default;
            if (flags != 0 || pageCount == 0) return false;
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

    private static GxManagedKernelDeviceV1 Descriptor(
        byte bus, ushort vendor, ushort device, byte classCode,
        byte subclass, byte progIf)
    {
        return new GxManagedKernelDeviceV1
        {
            Size = GxManagedKernelDeviceV1.ExpectedSize,
            AbiVersion = 1,
            DeviceKind = GxManagedKernelDeviceV1.DeviceKindPci,
            Segment = 0,
            Bus = bus,
            Device = 0,
            Function = 0,
            VendorId = vendor,
            DeviceId = device,
            RevisionId = 1,
            ClassCode = classCode,
            Subclass = subclass,
            ProgrammingInterface = progIf,
            HeaderType = 0,
            ResourceStartIndex = 0,
            ResourceCount = 0,
            Reserved = 0
        };
    }

    private static void TestPrecedenceAndLifecycle()
    {
        FakeProvider provider = new FakeProvider();
        GxManagedKernelDeviceV1[] descriptors =
        {
            Descriptor(0, 0x8086, 0x1237, 0x06, 0x00, 0),
            Descriptor(1, 0x1234, 0x5678, 0x03, 0x00, 0),
            Descriptor(2, 0x1234, 0x5679, 0x01, 0x06, 0x01)
        };
        fixed (GxManagedKernelDeviceV1* pointer = descriptors)
        {
            Expect(ManagedDeviceInventory.TryCreateFromDescriptors(
                       provider, pointer, 3,
                       out ManagedDeviceInventory? inventory) && inventory != null,
                   "inventory fixture created");
            if (inventory == null) return;

            ManagedDriverRegistry? registry = ManagedDriverRegistry.Create(provider);
            Expect(registry != null, "driver registry created");
            if (registry == null) return;

            ManagedDriverMatchRule exact = new(
                ManagedDriverMatchType.ExactVendorDevice,
                vendorId: 0x8086, deviceId: 0x1237);
            ManagedDriverMatchRule display = new(
                ManagedDriverMatchType.Class, classCode: 0x03);
            ManagedDriverMatchRule displaySubclass = new(
                ManagedDriverMatchType.ClassSubclass,
                classCode: 0x03, subclass: 0x00);
            ManagedDriverMatchRule displayInterface = new(
                ManagedDriverMatchType.ClassSubclassProgrammingInterface,
                classCode: 0x03, subclass: 0x00, programmingInterface: 0x00);
            ManagedDriverMatchRule malformed = new(
                ManagedDriverMatchType.Class, subclass: 1);
            Expect(!registry.TryRegister(new ManagedDriverDefinition(
                       0, 1, 0, new[] { exact })), "zero driver ID rejected");
            Expect(!registry.TryRegister(new ManagedDriverDefinition(
                       1, 1, 0, new[] { malformed })), "malformed rule rejected");
            Expect(!registry.TryRegister(new ManagedDriverDefinition(
                       1, 1, ManagedDriverRegistry.MaxPriority + 1,
                       new[] { exact })), "invalid priority rejected");
            Expect(registry.TryRegister(new ManagedDriverDefinition(
                       1, 0x484F5354, 100, new[] { exact })),
                   "exact driver registered");
            Expect(!registry.TryRegister(new ManagedDriverDefinition(
                       1, 0x445550, 1, new[] { display })),
                   "duplicate driver ID rejected");
            Expect(registry.TryRegister(new ManagedDriverDefinition(
                       2, 0x44495350, 10, new[] { display })),
                   "class driver registered");
            Expect(registry.TryRegister(new ManagedDriverDefinition(
                       3, 0x53554243, 1, new[] { displaySubclass })),
                   "class/subclass driver registered");
            Expect(registry.TryRegister(new ManagedDriverDefinition(
                       4, 0x494E5446, 1, new[] { displayInterface })),
                   "class/subclass/programming-interface driver registered");
            Expect(!registry.TryBind(inventory), "bind before freeze rejected");
            Expect(registry.TryFreeze(), "registry freezes once");
            Expect(!registry.TryRegister(new ManagedDriverDefinition(
                       3, 0x4C415445, 1, new[] { display })),
                   "registration after freeze rejected");
            Expect(registry.TryBind(inventory), "binding pass succeeds");
            Expect(registry.BoundDeviceCount == 2 && registry.UnboundDeviceCount == 1,
                   "bound and unbound devices have deterministic outcomes");
            Expect(registry.IsDeviceBound(0) && registry.IsDeviceBound(1) &&
                       !registry.IsDeviceBound(2),
                   "one driver per device is observable");
            Expect(registry.TryGetBinding(0, out ManagedDriverBindingInfo exactInfo) &&
                       exactInfo.MatchType == ManagedDriverMatchType.ExactVendorDevice &&
                       exactInfo.Specificity == 4 &&
                       registry.TryGetBinding(1, out ManagedDriverBindingInfo interfaceInfo) &&
                       interfaceInfo.MatchType ==
                           ManagedDriverMatchType.ClassSubclassProgrammingInterface &&
                       interfaceInfo.Specificity == 3 &&
                       registry.TryGetBinding(2, out ManagedDriverBindingInfo unboundInfo) &&
                       unboundInfo.State == ManagedDriverBindingState.Unbound,
                   "all supported match rule types expose stable binding state");
            uint[] boundDevices = new uint[3];
            Expect(registry.TryGetDevicesBoundToDriver(1, boundDevices,
                                                       out uint exactCount) &&
                       exactCount == 1 && boundDevices[0] == 0 &&
                       registry.TryGetDevicesBoundToDriver(4, boundDevices,
                                                           out uint interfaceCount) &&
                       interfaceCount == 1 && boundDevices[0] == 1,
                   "bound-device lookup returns the selected driver owner");
            Expect(!registry.TryGetDevicesBoundToDriver(0, boundDevices, out _),
                   "zero driver lookup is rejected");
            Expect(!registry.TryGetBinding(3, out _),
                   "invalid device binding query rejected");
            Expect(!registry.TryBind(inventory), "second binding pass rejected");
            Expect(registry.ValidateInvariants(), "binding invariants hold");
            Expect(!registry.Destroy(), "bound operational registry remains alive");

            ManagedDriverRegistry? teardown = ManagedDriverRegistry.Create(provider);
            Expect(teardown != null && teardown.Destroy(),
                   "unbound teardown registry is destroyed");
            Expect(ManagedDriverRegistry.TryRunPrecedenceTests(provider),
                   "specificity, priority, and registration-order precedence pass");
            inventory.Destroy();
        }
        provider.ReleaseAll();
        Expect(provider.AllocationCalls == provider.ReleaseCalls,
               "arena-backed test state returns all provider allocations");
    }

    public static int Main()
    {
        TestPrecedenceAndLifecycle();
        if (s_failures != 0)
        {
            Console.WriteLine("MANAGED_KERNEL_DRIVER_BINDING_HOST_TESTS=FAILED failures=" +
                              s_failures);
            return 1;
        }
        Console.WriteLine("MANAGED_KERNEL_DRIVER_BINDING_HOST_TESTS=PASSED");
        return 0;
    }
}

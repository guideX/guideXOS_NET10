using System;
using System.Runtime.InteropServices;
using GuideXOS.Net10.ManagedKernel;

internal static unsafe class Program
{
    private const ulong PageSize = KernelArena.PageSize;
    private static uint s_failures;

    private struct Slot
    {
        internal bool Live;
        internal nint Raw;
        internal KernelMemoryRegion Region;
    }

    private sealed class FakeProvider : IKernelMemoryProvider
    {
        private readonly Slot[] _slots = new Slot[32];
        internal bool Available = true;
        internal uint FailOnAttempt;
        internal uint AllocationAttempts;
        internal uint AllocationCalls;
        internal uint ReleaseCalls;

        public bool IsAvailable => Available;

        public bool TryAllocate(ulong pageCount, uint flags,
                                out KernelMemoryRegion region)
        {
            region = default;
            AllocationAttempts++;
            if (!Available || flags != 0 || pageCount == 0 ||
                (FailOnAttempt != 0 && AllocationAttempts == FailOnAttempt))
            {
                return false;
            }
            for (int index = 0; index != _slots.Length; ++index)
            {
                if (_slots[index].Live) continue;
                ulong bytes = pageCount * PageSize;
                nint raw = Marshal.AllocHGlobal((nint)(bytes + PageSize));
                ulong rawAddress = (ulong)(nuint)raw;
                ulong aligned = (rawAddress + PageSize - 1) & ~(PageSize - 1);
                KernelMemoryRegion candidate = new KernelMemoryRegion
                {
                    AllocationId = (ulong)index + 1,
                    VirtualAddress = aligned,
                    ByteLength = bytes,
                    PageCount = pageCount,
                    PageSize = PageSize,
                    Flags = 0
                };
                _slots[index].Live = true;
                _slots[index].Raw = raw;
                _slots[index].Region = candidate;
                AllocationCalls++;
                region = candidate;
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
                    candidate.Flags == region.Flags)
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryRelease(in KernelMemoryRegion region)
        {
            for (int index = 0; index != _slots.Length; ++index)
            {
                if (!_slots[index].Live ||
                    _slots[index].Region.AllocationId != region.AllocationId ||
                    _slots[index].Region.VirtualAddress != region.VirtualAddress)
                {
                    continue;
                }
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
        byte bus, byte device, byte function, ushort vendor, ushort deviceId,
        byte classCode, byte subclass, byte progIf, byte headerType = 0)
    {
        return new GxManagedKernelDeviceV1
        {
            Size = GxManagedKernelDeviceV1.ExpectedSize,
            AbiVersion = 1,
            DeviceKind = GxManagedKernelDeviceV1.DeviceKindPci,
            Flags = (byte)(headerType & 0x80) != 0
                ? GxManagedKernelDeviceV1.FlagPciMultifunction : 0,
            Segment = 0,
            Bus = bus,
            Device = device,
            Function = function,
            VendorId = vendor,
            DeviceId = deviceId,
            RevisionId = 1,
            ClassCode = classCode,
            Subclass = subclass,
            ProgrammingInterface = progIf,
            HeaderType = headerType,
            ResourceStartIndex = 0,
            ResourceCount = 0,
            Reserved = 0
        };
    }

    private static GxManagedKernelDeviceV1[] Fixture()
    {
        return new[]
        {
            Descriptor(0, 0, 0, 0x8086, 0x1237, 0x06, 0x00, 0),
            Descriptor(0, 1, 0, 0x8086, 0x7000, 0x06, 0x01, 0),
            Descriptor(0, 1, 1, 0x8086, 0x7010, 0x01, 0x01, 0x80, 0x80),
            Descriptor(0, 2, 0, 0x1234, 0x1111, 0x03, 0x00, 0)
        };
    }

    private static void TestSuccessfulInventory()
    {
        FakeProvider provider = new FakeProvider();
        GxManagedKernelDeviceV1[] descriptors = Fixture();
        fixed (GxManagedKernelDeviceV1* pointer = descriptors)
        {
            Expect(ManagedDeviceInventory.TryCreateFromDescriptors(
                       provider, pointer, (uint)descriptors.Length,
                       out ManagedDeviceInventory? inventory) && inventory != null,
                   "inventory copies valid native descriptors");
            if (inventory == null) return;
            Expect(inventory.DeviceCount == 4 && inventory.ResourceCount == 0,
                   "inventory summary counts are bounded");
            Expect(inventory.Metrics.BackingChunkCount >= 2 &&
                       inventory.Metrics.TotalBackingBytes >= 5 * PageSize,
                   "inventory consumes the arena and grows backing");
            Expect(inventory.ValidateInvariants(),
                   "inventory arena invariants hold");
            Expect(inventory.TryGetDevice(2, out ManagedDevice multifunction) &&
                       multifunction.Function == 1 && multifunction.DeviceId == 0x7010,
                   "lookup by index returns the copied device");
            bool bdfFound = inventory.TryFindPciDevice(0, 0, 1, 1,
                                                       out ManagedDevice byBdf);
            Expect(bdfFound && byBdf.VendorId == 0x8086,
                   "lookup by PCI BDF works");
            Expect(inventory.TryFindFirstByClass(0x03, 0x00,
                                                 out ManagedDevice byClass) &&
                       byClass.DeviceId == 0x1111,
                   "class lookup works");
            Expect(!inventory.TryGetResource(0, out _),
                   "unpublished resource query is rejected");
            Expect(!inventory.TryGetDevice(4, out _),
                   "out-of-range device query is rejected");
            Expect(inventory.TryRunRuntimeSurvival(),
                   "inventory survives GC/runtime activity");
            uint releasesBeforeDestroy = provider.ReleaseCalls;
            bool destroyed = inventory.Destroy();
            Expect(destroyed && provider.ReleaseCalls == releasesBeforeDestroy + 2,
                   "inventory teardown releases all arena allocations");
            Expect(!inventory.TryGetDevice(0, out _),
                   "queries after teardown are rejected");
        }
    }

    private static void TestFailureRollback()
    {
        GxManagedKernelDeviceV1[] descriptors = Fixture();
        FakeProvider initialFailure = new FakeProvider { FailOnAttempt = 1 };
        fixed (GxManagedKernelDeviceV1* pointer = descriptors)
        {
            Expect(!ManagedDeviceInventory.TryCreateFromDescriptors(
                       initialFailure, pointer, 4, out _),
                   "initial arena allocation failure is rejected");
            initialFailure.ReleaseAll();

            FakeProvider growthFailure = new FakeProvider { FailOnAttempt = 2 };
            Expect(!ManagedDeviceInventory.TryCreateFromDescriptors(
                       growthFailure, pointer, 4, out _),
                   "device storage growth failure rolls back");
            growthFailure.ReleaseAll();

            GxManagedKernelDeviceV1 duplicate = descriptors[1];
            duplicate.Bus = descriptors[0].Bus;
            duplicate.Device = descriptors[0].Device;
            duplicate.Function = descriptors[0].Function;
            descriptors[1] = duplicate;
            Expect(!ManagedDeviceInventory.TryCreateFromDescriptors(
                       new FakeProvider(), pointer, 4, out _),
                   "duplicate BDF is rejected before arena publication");
        }

        FakeProvider unavailable = new FakeProvider { Available = false };
        fixed (GxManagedKernelDeviceV1* pointer = descriptors)
        {
            Expect(!ManagedDeviceInventory.TryCreateFromDescriptors(
                       unavailable, pointer, 4, out _),
                   "inventory before memory provider startup is rejected");
        }
    }

    private static void TestCapacityAndMalformed()
    {
        FakeProvider provider = new FakeProvider();
        GxManagedKernelDeviceV1[] descriptors = Fixture();
        descriptors[1].Reserved = 1;
        fixed (GxManagedKernelDeviceV1* pointer = descriptors)
        {
            Expect(!ManagedDeviceInventory.TryCreateFromDescriptors(
                       provider, pointer, 4, out _),
                   "reserved descriptor data is rejected");
            descriptors[1].Reserved = 0;
            descriptors[1].VendorId = 0xFFFF;
            Expect(!ManagedDeviceInventory.TryCreateFromDescriptors(
                       provider, pointer, 4, out _),
                   "absent PCI vendor data is rejected");
        }
    }

    public static int Main()
    {
        TestSuccessfulInventory();
        TestFailureRollback();
        TestCapacityAndMalformed();
        if (s_failures != 0)
        {
            Console.WriteLine("MANAGED_KERNEL_DEVICE_INVENTORY_HOST_TESTS=FAILED failures=" +
                              s_failures);
            return 1;
        }
        Console.WriteLine("MANAGED_KERNEL_DEVICE_INVENTORY_HOST_TESTS=PASSED");
        return 0;
    }
}

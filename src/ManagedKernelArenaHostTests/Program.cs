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
        internal bool FailNext;
        internal bool InvalidNext;
        internal uint AllocationCalls;
        internal uint ReleaseCalls;

        public bool IsAvailable => Available;

        public bool TryAllocate(ulong pageCount, uint flags,
                                out KernelMemoryRegion region)
        {
            region = default;
            if (!Available || FailNext || flags != 0 || pageCount == 0)
            {
                FailNext = false;
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
                if (InvalidNext)
                {
                    InvalidNext = false;
                    region = new KernelMemoryRegion
                    {
                        AllocationId = candidate.AllocationId,
                        VirtualAddress = candidate.VirtualAddress,
                        ByteLength = candidate.ByteLength - 1,
                        PageCount = candidate.PageCount,
                        PageSize = candidate.PageSize,
                        Flags = 0
                    };
                    return true;
                }
                region = candidate;
                return true;
            }
            return false;
        }

        public bool IsValidRegion(in KernelMemoryRegion region)
        {
            for (int index = 0; index != _slots.Length; ++index)
            {
                if (_slots[index].Live &&
                    _slots[index].Region.AllocationId == region.AllocationId &&
                    _slots[index].Region.VirtualAddress == region.VirtualAddress &&
                    _slots[index].Region.ByteLength == region.ByteLength &&
                    _slots[index].Region.PageCount == region.PageCount &&
                    _slots[index].Region.PageSize == region.PageSize &&
                    _slots[index].Region.Flags == region.Flags)
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
                    !_slots[index].Region.AllocationId.Equals(region.AllocationId) ||
                    !_slots[index].Region.VirtualAddress.Equals(region.VirtualAddress))
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

        internal void ReleaseAllForCorruptFixture()
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

    private static void TestCreateAndFreeList()
    {
        FakeProvider provider = new FakeProvider();
        Expect(KernelArena.TryCreate(provider, out KernelArena? arena) ==
                   KernelArenaStatus.Ok && arena != null,
               "default arena creates with one backing chunk");
        if (arena == null) return;
        KernelArenaAllocation a;
        KernelArenaAllocation b;
        KernelArenaAllocation c;
        KernelArenaAllocation d = default;
        Expect(arena.TryAllocate(128, 16, out a) == KernelArenaStatus.Ok &&
                   arena.TryWritePattern(in a, 0x11) &&
                   arena.TryVerifyPattern(in a, 0x11),
               "first allocation pattern");
        Expect(arena.TryAllocate(256, 32, out b) == KernelArenaStatus.Ok &&
                   arena.TryWritePattern(in b, 0x22),
               "second allocation pattern");
        Expect(arena.TryAllocate(96, 64, out c) == KernelArenaStatus.Ok &&
                   arena.TryWritePattern(in c, 0x33),
               "third allocation pattern");
        Expect(a.VirtualAddress % a.Alignment == 0 &&
                   b.VirtualAddress % b.Alignment == 0 &&
                   c.VirtualAddress % c.Alignment == 0,
               "power-of-two alignment");
        uint callsBeforeReuse = provider.AllocationCalls;
        Expect(arena.Free(in b) == KernelArenaStatus.Ok &&
                   arena.TryAllocate(192, 32, out d) == KernelArenaStatus.Ok &&
                   provider.AllocationCalls == callsBeforeReuse &&
                   d.ChunkIdentity == a.ChunkIdentity &&
                   arena.TryVerifyPattern(in a, 0x11) &&
                   arena.TryVerifyPattern(in c, 0x33),
               "first-fit reuse without native growth");
        Expect(arena.Free(in a) == KernelArenaStatus.Ok &&
                   arena.Free(in c) == KernelArenaStatus.Ok,
               "free adjacent fragments");
        KernelArenaAllocation e;
        KernelArenaAllocation f = default;
        Expect(arena.TryAllocate(64, 8, out e) == KernelArenaStatus.Ok &&
                   arena.TryAllocate(32, 8, out f) == KernelArenaStatus.Ok,
               "split fragmented free space");
        Expect(arena.Free(in e) == KernelArenaStatus.Ok &&
                   arena.Free(in f) == KernelArenaStatus.Ok &&
                   arena.Free(in d) == KernelArenaStatus.Ok,
               "previous, next, and both-side coalescing");
        KernelArenaMetrics metrics = arena.GetMetrics();
        Expect(metrics.LiveAllocationCount == 0 && metrics.FreeBytes ==
                   metrics.TotalBackingBytes && metrics.LargestFreeBlock ==
                   metrics.TotalBackingBytes,
               "coalescing recovers the whole chunk");
        Expect(arena.Destroy() == KernelArenaStatus.Ok &&
                   arena.Destroy() == KernelArenaStatus.InvalidState,
               "deterministic destruction and second-destroy rejection");
    }

    private static void TestGrowthAndRollback()
    {
        FakeProvider provider = new FakeProvider();
        Expect(KernelArena.TryCreate(provider, out KernelArena? arena) ==
                   KernelArenaStatus.Ok && arena != null,
               "growth arena creates");
        if (arena == null) return;
        KernelArenaAllocation live;
        Expect(arena.TryAllocate(128, 16, out live) == KernelArenaStatus.Ok &&
                   arena.TryWritePattern(in live, 0x41),
               "growth fixture live allocation");
        uint callsBeforeFailure = provider.AllocationCalls;
        provider.FailNext = true;
        Expect(arena.TryAllocate(9000, 64, out _) ==
                   KernelArenaStatus.ResourceExhausted &&
                   provider.AllocationCalls == callsBeforeFailure &&
                   arena.TryVerifyPattern(in live, 0x41) &&
                   arena.GetMetrics().BackingChunkCount == 1,
               "growth failure rolls back without changing existing state");
        KernelArenaAllocation grown;
        Expect(arena.TryAllocate(9000, 64, out grown) == KernelArenaStatus.Ok &&
                   provider.AllocationCalls == callsBeforeFailure + 1 &&
                   arena.TotalBackingPages == 5 &&
                   arena.TryWritePattern(in grown, 0x42) &&
                   arena.TryVerifyPattern(in grown, 0x42),
               "growth calculates bounded required pages");
        Expect(arena.Free(in live) == KernelArenaStatus.Ok &&
                   arena.Free(in grown) == KernelArenaStatus.Ok &&
                   arena.Destroy() == KernelArenaStatus.Ok,
               "grown arena releases every backing region");
    }

    private static void TestNegativeAndIsolation()
    {
        FakeProvider provider = new FakeProvider();
        FakeProvider unavailable = new FakeProvider { Available = false };
        Expect(KernelArena.TryCreate(unavailable, out KernelArena? beforeStart) ==
                   KernelArenaStatus.InvalidState && beforeStart == null,
               "create before provider startup is rejected");
        Expect(KernelArena.TryCreate(provider, 0, 2, 1, 2, 1, 64,
                                     out KernelArena? invalid) ==
                   KernelArenaStatus.InvalidArgument && invalid == null,
               "invalid arena configuration is rejected");
        Expect(KernelArena.TryCreate(provider, out KernelArena? first) ==
                   KernelArenaStatus.Ok && first != null,
               "first isolated arena creates");
        Expect(KernelArena.TryCreate(provider, out KernelArena? second) ==
                   KernelArenaStatus.Ok && second != null,
               "second isolated arena creates");
        if (first == null || second == null) return;
        KernelArenaAllocation firstAllocation;
        KernelArenaAllocation secondAllocation;
        Expect(first.TryAllocate(96, 16, out firstAllocation) ==
                   KernelArenaStatus.Ok &&
                   first.TryWritePattern(in firstAllocation, 0x51),
               "first isolated arena allocation");
        Expect(second.TryAllocate(96, 16, out secondAllocation) ==
                   KernelArenaStatus.Ok &&
                   second.TryWritePattern(in secondAllocation, 0x61),
               "second isolated arena allocation");
        KernelArenaAllocation wrongAddress = new KernelArenaAllocation(
            firstAllocation.ArenaIdentity, firstAllocation.AllocationId,
            firstAllocation.VirtualAddress + 1,
            firstAllocation.RequestedByteLength, firstAllocation.ReservedByteLength,
            firstAllocation.Alignment, firstAllocation.ChunkIdentity,
            firstAllocation.Cookie);
        Expect(first.Free(in secondAllocation) ==
                   KernelArenaStatus.OwnershipMismatch &&
                   first.Free(in wrongAddress) == KernelArenaStatus.OwnershipMismatch &&
                   first.TryVerifyPattern(in firstAllocation, 0x51) &&
                   second.TryVerifyPattern(in secondAllocation, 0x61),
               "wrong-arena and wrong-address frees preserve neighbors");
        Expect(first.Destroy() == KernelArenaStatus.LiveAllocations &&
                   first.Free(in firstAllocation) == KernelArenaStatus.Ok &&
                   first.Free(in firstAllocation) == KernelArenaStatus.NotFound &&
                   first.Destroy() == KernelArenaStatus.Ok &&
                   first.TryAllocate(1, 1, out _) == KernelArenaStatus.InvalidState,
               "double free and post-destroy operations are deterministic");
        Expect(second.Free(in secondAllocation) == KernelArenaStatus.Ok &&
                   second.Destroy() == KernelArenaStatus.Ok,
               "destroying one arena does not affect the other");
    }

    private static void TestMetadataAndCorruptionFixtures()
    {
        FakeProvider provider = new FakeProvider();
        Expect(KernelArena.TryCreate(provider, 1, 1, 1, 1, 2, 64,
                                     out KernelArena? limited) ==
                   KernelArenaStatus.Ok && limited != null,
               "bounded metadata fixture creates");
        if (limited != null)
        {
            KernelArenaAllocation first;
            KernelArenaAllocation second = default;
            Expect(limited.TryAllocate(32, 8, out first) == KernelArenaStatus.Ok &&
                       limited.TryAllocate(32, 8, out second) == KernelArenaStatus.Ok &&
                       limited.TryAllocate(32, 8, out _) ==
                           KernelArenaStatus.ResourceExhausted,
                   "live allocation capacity is bounded");
            Expect(limited.Free(in first) == KernelArenaStatus.Ok &&
                       limited.Free(in second) == KernelArenaStatus.Ok &&
                       limited.Destroy() == KernelArenaStatus.Ok,
                   "bounded metadata fixture tears down");
        }

        FakeProvider duplicateProvider = new FakeProvider();
        KernelArena.TryCreate(duplicateProvider, out KernelArena? duplicateArena);
        if (duplicateArena != null)
        {
            KernelArenaAllocation first;
            KernelArenaAllocation second;
            duplicateArena.TryAllocate(32, 8, out first);
            duplicateArena.TryAllocate(32, 8, out second);
            Expect(duplicateArena.DebugCorruptDuplicateAllocationIdForTests() &&
                       !duplicateArena.ValidateInvariants(),
                   "synthetic duplicate-id corruption is rejected");
            duplicateProvider.ReleaseAllForCorruptFixture();
        }

        FakeProvider rangeProvider = new FakeProvider();
        KernelArena.TryCreate(rangeProvider, out KernelArena? rangeArena);
        if (rangeArena != null)
        {
            KernelArenaAllocation allocation;
            rangeArena.TryAllocate(32, 8, out allocation);
            Expect(rangeArena.DebugCorruptFirstBlockLengthForTests() &&
                       !rangeArena.ValidateInvariants(),
                   "synthetic out-of-range block corruption is rejected");
            rangeProvider.ReleaseAllForCorruptFixture();
        }
    }

    public static int Main()
    {
        TestCreateAndFreeList();
        TestGrowthAndRollback();
        TestNegativeAndIsolation();
        TestMetadataAndCorruptionFixtures();
        if (s_failures != 0)
        {
            Console.WriteLine("MANAGED_KERNEL_ARENA_HOST_TESTS=FAILED failures=" +
                              s_failures);
            return 1;
        }
        Console.WriteLine("MANAGED_KERNEL_ARENA_HOST_TESTS=PASSED");
        return 0;
    }
}

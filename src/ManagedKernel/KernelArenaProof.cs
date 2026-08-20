using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class KernelArenaProof
{
    private static uint s_stage;
    private static KernelArena? s_arena;
    private static KernelArena? s_secondaryArena;
    private static KernelArenaAllocation s_a;
    private static KernelArenaAllocation s_b;
    private static KernelArenaAllocation s_c;
    private static KernelArenaAllocation s_d;
    private static KernelArenaAllocation s_e;
    private static KernelArenaAllocation s_f;
    private static KernelArenaAllocation s_growth;
    private static KernelArenaAllocation s_secondary;

    internal static uint RunStage(uint stage)
    {
        if (stage != s_stage + 1U) return ManagedKernelContract.InvalidState;
        bool success = stage switch
        {
            1U => RunCreateAndAllocate(),
            2U => RunReuseFragmentationAndCoalescing(),
            3U => RunGrowthAndRuntimeSurvival(),
            4U => RunNegativeTests(),
            5U => RunDestroy(),
            _ => false
        };
        if (!success) return ManagedKernelContract.InvalidState;
        s_stage = stage;
        return ManagedKernelContract.ManagedOk;
    }

    private static bool RunCreateAndAllocate()
    {
        if (KernelArena.TryCreate(Phase4KernelMemoryProvider.Instance,
                                  out KernelArena? created) !=
            KernelArenaStatus.Ok || created == null)
        {
            return false;
        }
        s_arena = created;
        KernelArenaMetrics metrics = s_arena.GetMetrics();
        if (!s_arena.ValidateInvariants() || metrics.BackingChunkCount != 1 ||
            metrics.TotalBackingBytes != KernelArena.DefaultInitialPages *
                KernelArena.PageSize)
        {
            return false;
        }
        if (!AllocateAndFill(s_arena, 128, 16, 0x11, out s_a) ||
            !AllocateAndFill(s_arena, 256, 32, 0x22, out s_b) ||
            !AllocateAndFill(s_arena, 96, 64, 0x33, out s_c) ||
            !s_arena.ValidateInvariants() ||
            !RangesDoNotOverlap(in s_a, in s_b) ||
            !RangesDoNotOverlap(in s_a, in s_c) ||
            !RangesDoNotOverlap(in s_b, in s_c))
        {
            return false;
        }
        if (s_a.VirtualAddress % s_a.Alignment != 0 ||
            s_b.VirtualAddress % s_b.Alignment != 0 ||
            s_c.VirtualAddress % s_c.Alignment != 0)
        {
            return false;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_ARENA_CREATED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_ARENA_ALLOC_OK\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_ARENA_ALIGNMENT_OK\r\n"u8))
        {
            return false;
        }
        return true;
    }

    private static bool RunReuseFragmentationAndCoalescing()
    {
        if (s_arena == null || !s_arena.ValidateInvariants()) return false;
        KernelArenaMetrics before = s_arena.GetMetrics();
        KernelArenaAllocation retiredB = s_b;
        if (s_arena.Free(in retiredB) != KernelArenaStatus.Ok)
        {
            return false;
        }
        s_b = default;
        if (!AllocateAndFill(s_arena, 192, 32, 0x44, out s_d))
        {
            return false;
        }
        KernelArenaMetrics afterReuse = s_arena.GetMetrics();
        if (afterReuse.BackingChunkCount != before.BackingChunkCount ||
            afterReuse.TotalBackingBytes != before.TotalBackingBytes ||
            s_d.ChunkIdentity != s_a.ChunkIdentity ||
            !s_arena.TryVerifyPattern(in s_a, 0x11) ||
            !s_arena.TryVerifyPattern(in s_c, 0x33))
        {
            return false;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_ARENA_REUSE_OK\r\n"u8))
        {
            return false;
        }

        if (s_arena.Free(in s_a) != KernelArenaStatus.Ok ||
            s_arena.Free(in s_c) != KernelArenaStatus.Ok)
        {
            return false;
        }
        s_a = default;
        s_c = default;
        if (!AllocateAndFill(s_arena, 64, 8, 0x55, out s_e) ||
            !AllocateAndFill(s_arena, 32, 8, 0x66, out s_f) ||
            !s_arena.ValidateInvariants() ||
            !s_arena.TryVerifyPattern(in s_d, 0x44))
        {
            return false;
        }
        KernelArenaMetrics fragmented = s_arena.GetMetrics();
        if (fragmented.LiveAllocationCount != 3 ||
            fragmented.FreeBytes == 0 || fragmented.LargestFreeBlock ==
                fragmented.TotalBackingBytes)
        {
            return false;
        }
        if (!KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_ARENA_FRAGMENTATION_OK\r\n"u8))
        {
            return false;
        }

        if (s_arena.Free(in s_e) != KernelArenaStatus.Ok ||
            s_arena.Free(in s_f) != KernelArenaStatus.Ok ||
            s_arena.Free(in s_d) != KernelArenaStatus.Ok ||
            !s_arena.ValidateInvariants())
        {
            return false;
        }
        s_e = default;
        s_f = default;
        s_d = default;
        KernelArenaMetrics coalesced = s_arena.GetMetrics();
        if (coalesced.LiveAllocationCount != 0 ||
            coalesced.FreeBytes != coalesced.TotalBackingBytes ||
            coalesced.LargestFreeBlock != coalesced.TotalBackingBytes)
        {
            return false;
        }
        return KernelLog.Write(
            "GXOS_NET10:MANAGED_KERNEL_ARENA_COALESCE_OK\r\n"u8);
    }

    private static bool RunGrowthAndRuntimeSurvival()
    {
        if (s_arena == null || !s_arena.ValidateInvariants()) return false;
        if (!AllocateAndFill(s_arena, 9000, 64, 0x77, out s_growth))
        {
            return false;
        }
        KernelArenaMetrics grown = s_arena.GetMetrics();
        if (grown.BackingChunkCount != 2 || grown.TotalBackingBytes !=
                5 * KernelArena.PageSize || s_growth.ChunkIdentity == 0 ||
            s_growth.VirtualAddress % s_growth.Alignment != 0)
        {
            return false;
        }
        if (KernelArena.TryCreate(Phase4KernelMemoryProvider.Instance,
                                  out KernelArena? secondary) !=
                KernelArenaStatus.Ok || secondary == null)
        {
            return false;
        }
        s_secondaryArena = secondary;
        if (!AllocateAndFill(s_secondaryArena, 160, 16, 0x88,
                             out s_secondary) ||
            !s_arena.TryVerifyPattern(in s_growth, 0x77) ||
            !s_secondaryArena.TryVerifyPattern(in s_secondary, 0x88))
        {
            return false;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_ARENA_GROWTH_OK\r\n"u8) ||
            !KernelLog.Write(
                "GXOS_NET10:MANAGED_KERNEL_ARENA_RUNTIME_SURVIVAL_BEGIN\r\n"u8) ||
            !ManagedKernelContract.TryQueryMonotonicTime(out _))
        {
            return false;
        }
        byte[] gcActivity = new byte[8192];
        gcActivity[0] = 0xA7;
        gcActivity[gcActivity.Length - 1] = 0x7A;
        GC.Collect();
        GC.KeepAlive(gcActivity);
        if (!s_arena.TryVerifyPattern(in s_growth, 0x77) ||
            !s_secondaryArena.TryVerifyPattern(in s_secondary, 0x88))
        {
            return false;
        }
        return KernelLog.Write(
            "GXOS_NET10:MANAGED_KERNEL_ARENA_RUNTIME_SURVIVAL_OK\r\n"u8);
    }

    private static bool RunNegativeTests()
    {
        if (s_arena == null || s_secondaryArena == null ||
            !s_arena.ValidateInvariants() || !s_secondaryArena.ValidateInvariants())
        {
            return false;
        }
        if (KernelArena.TryCreate(Phase4KernelMemoryProvider.Instance, 0, 2,
                                  1, 2, 1, 64, out KernelArena? invalid) !=
                KernelArenaStatus.InvalidArgument || invalid != null ||
            s_arena.TryAllocate(0, 1, out _) != KernelArenaStatus.InvalidArgument ||
            s_arena.TryAllocate(1, 0, out _) != KernelArenaStatus.InvalidArgument ||
            s_arena.TryAllocate(1, 3, out _) != KernelArenaStatus.InvalidArgument ||
            s_arena.TryAllocate(1, 8192, out _) != KernelArenaStatus.InvalidArgument ||
            s_arena.TryAllocate(ulong.MaxValue, 1, out _) !=
                KernelArenaStatus.InvalidArgument)
        {
            return false;
        }
        if (s_arena.Free(in s_secondary) != KernelArenaStatus.OwnershipMismatch)
        {
            return false;
        }
        KernelArenaAllocation wrongAddress = new KernelArenaAllocation(
            s_growth.ArenaIdentity, s_growth.AllocationId,
            s_growth.VirtualAddress + 1, s_growth.RequestedByteLength,
            s_growth.ReservedByteLength, s_growth.Alignment,
            s_growth.ChunkIdentity, s_growth.Cookie);
        KernelArenaAllocation wrongLength = new KernelArenaAllocation(
            s_growth.ArenaIdentity, s_growth.AllocationId,
            s_growth.VirtualAddress, s_growth.RequestedByteLength + 1,
            s_growth.ReservedByteLength, s_growth.Alignment,
            s_growth.ChunkIdentity, s_growth.Cookie);
        if (s_arena.Free(in wrongAddress) != KernelArenaStatus.OwnershipMismatch ||
            s_arena.Free(in wrongLength) != KernelArenaStatus.OwnershipMismatch ||
            s_arena.Destroy() != KernelArenaStatus.LiveAllocations ||
            !s_arena.TryVerifyPattern(in s_growth, 0x77) ||
            !s_secondaryArena.TryVerifyPattern(in s_secondary, 0x88))
        {
            return false;
        }
        if (!AllocateAndFill(s_secondaryArena, 64, 8, 0x99,
                             out KernelArenaAllocation temporary) ||
            s_secondaryArena.Free(in temporary) != KernelArenaStatus.Ok ||
            s_secondaryArena.Free(in temporary) != KernelArenaStatus.NotFound)
        {
            return false;
        }
        return KernelLog.Write(
            "GXOS_NET10:MANAGED_KERNEL_ARENA_NEGATIVE_TESTS_OK\r\n"u8);
    }

    private static bool RunDestroy()
    {
        if (s_arena == null || s_secondaryArena == null ||
            !s_arena.TryVerifyPattern(in s_growth, 0x77) ||
            !s_secondaryArena.TryVerifyPattern(in s_secondary, 0x88))
        {
            return false;
        }
        if (s_secondaryArena.Free(in s_secondary) != KernelArenaStatus.Ok ||
            s_arena.Free(in s_growth) != KernelArenaStatus.Ok)
        {
            return false;
        }
        s_secondary = default;
        s_growth = default;
        if (s_secondaryArena.Destroy() != KernelArenaStatus.Ok ||
            s_arena.Destroy() != KernelArenaStatus.Ok ||
            !s_secondaryArena.ValidateInvariants() ||
            !s_arena.ValidateInvariants() ||
            s_secondaryArena.GetMetrics().BackingChunkCount != 0 ||
            s_arena.GetMetrics().BackingChunkCount != 0)
        {
            return false;
        }
        s_secondaryArena = null;
        s_arena = null;
        return KernelLog.Write(
            "GXOS_NET10:MANAGED_KERNEL_ARENA_DESTROY_OK\r\n"u8);
    }

    private static bool AllocateAndFill(
        KernelArena arena,
        ulong length,
        ulong alignment,
        byte seed,
        out KernelArenaAllocation allocation)
    {
        allocation = default;
        return arena.TryAllocate(length, alignment, out allocation) ==
                   KernelArenaStatus.Ok &&
               arena.TryWritePattern(in allocation, seed) &&
               arena.TryVerifyPattern(in allocation, seed);
    }

    private static bool RangesDoNotOverlap(
        in KernelArenaAllocation left, in KernelArenaAllocation right)
    {
        if (left.VirtualAddress > ulong.MaxValue - left.ReservedByteLength ||
            right.VirtualAddress > ulong.MaxValue - right.ReservedByteLength)
        {
            return false;
        }
        return left.VirtualAddress + left.ReservedByteLength <=
                   right.VirtualAddress ||
               right.VirtualAddress + right.ReservedByteLength <=
                   left.VirtualAddress;
    }
}

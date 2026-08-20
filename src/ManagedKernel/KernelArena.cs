using System;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

internal struct KernelMemoryRegion
{
    internal ulong AllocationId;
    internal ulong VirtualAddress;
    internal ulong ByteLength;
    internal ulong PageCount;
    internal ulong PageSize;
    internal uint Flags;
}

internal enum KernelArenaStatus : uint
{
    Ok = 0,
    InvalidArgument = 1,
    InvalidState = 2,
    ResourceExhausted = 3,
    NotFound = 4,
    OwnershipMismatch = 5,
    LiveAllocations = 6,
    Corrupted = 7
}

internal interface IKernelMemoryProvider
{
    bool IsAvailable { get; }

    bool TryAllocate(ulong pageCount, uint flags,
                     out KernelMemoryRegion region);

    bool IsValidRegion(in KernelMemoryRegion region);

    bool TryRelease(in KernelMemoryRegion region);
}

internal readonly struct KernelArenaAllocation
{
    internal readonly ulong ArenaIdentity;
    internal readonly ulong AllocationId;
    internal readonly ulong VirtualAddress;
    internal readonly ulong RequestedByteLength;
    internal readonly ulong ReservedByteLength;
    internal readonly ulong Alignment;
    internal readonly ulong ChunkIdentity;
    internal readonly ulong Cookie;

    internal KernelArenaAllocation(
        ulong arenaIdentity,
        ulong allocationId,
        ulong virtualAddress,
        ulong requestedByteLength,
        ulong reservedByteLength,
        ulong alignment,
        ulong chunkIdentity,
        ulong cookie)
    {
        ArenaIdentity = arenaIdentity;
        AllocationId = allocationId;
        VirtualAddress = virtualAddress;
        RequestedByteLength = requestedByteLength;
        ReservedByteLength = reservedByteLength;
        Alignment = alignment;
        ChunkIdentity = chunkIdentity;
        Cookie = cookie;
    }
}

internal readonly struct KernelArenaMetrics
{
    internal readonly uint LiveAllocationCount;
    internal readonly uint BackingChunkCount;
    internal readonly ulong TotalBackingBytes;
    internal readonly ulong LiveRequestedBytes;
    internal readonly ulong FreeBytes;
    internal readonly ulong LargestFreeBlock;

    internal KernelArenaMetrics(
        uint liveAllocationCount,
        uint backingChunkCount,
        ulong totalBackingBytes,
        ulong liveRequestedBytes,
        ulong freeBytes,
        ulong largestFreeBlock)
    {
        LiveAllocationCount = liveAllocationCount;
        BackingChunkCount = backingChunkCount;
        TotalBackingBytes = totalBackingBytes;
        LiveRequestedBytes = liveRequestedBytes;
        FreeBytes = freeBytes;
        LargestFreeBlock = largestFreeBlock;
    }
}

internal unsafe sealed class KernelArena
{
    internal const ulong PageSize = 4096;
    internal const ulong Phase4MaxPagesPerAllocation = 256;
    internal const ulong Phase4MaxTotalPages = 1024;
    internal const ulong DefaultInitialPages = 2;
    internal const ulong DefaultGrowthPages = 2;
    internal const uint DefaultMaxBackingChunks = 4;
    internal const ulong DefaultMaxTotalPages = 8;
    internal const uint DefaultMaxLiveAllocations = 24;
    internal const ulong DefaultMaxAlignment = 4096;
    internal const ulong MaxArenaPages = 8;
    internal const uint MaxArenaChunks = 4;
    internal const uint MaxArenaLiveAllocations = 24;
    internal const int MaxBlockRecords = 64;

    private const byte BlockUnused = 0;
    private const byte BlockFree = 1;
    private const byte BlockAllocated = 2;
    private const ulong CookieConstant = 0xC3A5F17D9B4E6281UL;

    private struct ArenaChunk
    {
        internal bool Active;
        internal ulong Identity;
        internal KernelMemoryRegion Backing;
        internal int FirstBlock;
    }

    private struct ArenaBlock
    {
        internal byte State;
        internal int ChunkIndex;
        internal int Previous;
        internal int Next;
        internal int AllocationSlot;
        internal ulong Offset;
        internal ulong Length;
    }

    private struct ArenaAllocationRecord
    {
        internal bool Live;
        internal int BlockIndex;
        internal KernelArenaAllocation Descriptor;
    }

    private struct BlockPlan
    {
        internal int BlockIndex;
        internal int AllocationBlockIndex;
        internal int TrailingBlockIndex;
        internal int AllocationSlot;
        internal ulong Padding;
        internal ulong AvailableLength;
        internal ulong ReservedLength;
        internal ulong TrailingLength;
        internal ulong AllocationOffset;
    }

    private static ulong s_nextArenaIdentity = 1;
    private static ulong s_nextAllocationIdentity = 1;

    private readonly IKernelMemoryProvider _provider;
    private readonly ulong _initialPages;
    private readonly ulong _growthPages;
    private readonly uint _maxBackingChunks;
    private readonly ulong _maxTotalPages;
    private readonly uint _maxLiveAllocations;
    private readonly ulong _maxAlignment;
    private readonly ArenaChunk[] _chunks;
    private readonly ArenaBlock[] _blocks;
    private readonly ArenaAllocationRecord[] _allocations;
    private readonly ulong _arenaIdentity;
    private ulong _nextChunkIdentity = 1;
    private uint _chunkCount;
    private ulong _totalPages;
    private uint _liveAllocationCount;
    private int _blockUsedCount;
    private bool _destroyed;

    private KernelArena(
        IKernelMemoryProvider provider,
        ulong initialPages,
        ulong growthPages,
        uint maxBackingChunks,
        ulong maxTotalPages,
        uint maxLiveAllocations,
        ulong maxAlignment,
        ulong arenaIdentity)
    {
        _provider = provider;
        _initialPages = initialPages;
        _growthPages = growthPages;
        _maxBackingChunks = maxBackingChunks;
        _maxTotalPages = maxTotalPages;
        _maxLiveAllocations = maxLiveAllocations;
        _maxAlignment = maxAlignment;
        _arenaIdentity = arenaIdentity;
        _chunks = new ArenaChunk[(int)maxBackingChunks];
        _blocks = new ArenaBlock[MaxBlockRecords];
        _allocations = new ArenaAllocationRecord[(int)maxLiveAllocations];
    }

    internal static KernelArenaStatus TryCreate(
        IKernelMemoryProvider provider,
        out KernelArena? arena)
    {
        return TryCreate(provider, DefaultInitialPages, DefaultGrowthPages,
                         DefaultMaxBackingChunks, DefaultMaxTotalPages,
                         DefaultMaxLiveAllocations, DefaultMaxAlignment,
                         out arena);
    }

    internal static KernelArenaStatus TryCreate(
        IKernelMemoryProvider provider,
        ulong initialPages,
        ulong growthPages,
        uint maxBackingChunks,
        ulong maxTotalPages,
        uint maxLiveAllocations,
        ulong maxAlignment,
        out KernelArena? arena)
    {
        ulong arenaIdentity;
        KernelArena candidate;
        KernelArenaStatus status;

        arena = null;
        if (provider == null || initialPages == 0 || growthPages == 0 ||
            maxBackingChunks == 0 || maxBackingChunks > MaxArenaChunks ||
            maxTotalPages == 0 || maxTotalPages > MaxArenaPages ||
            initialPages > maxTotalPages || maxLiveAllocations == 0 ||
            maxLiveAllocations > MaxArenaLiveAllocations ||
            !IsPowerOfTwo(maxAlignment) || maxAlignment > DefaultMaxAlignment)
        {
            return KernelArenaStatus.InvalidArgument;
        }
        if (initialPages > Phase4MaxPagesPerAllocation ||
            growthPages > Phase4MaxPagesPerAllocation ||
            maxTotalPages > Phase4MaxTotalPages)
        {
            return KernelArenaStatus.InvalidArgument;
        }
        if (!provider.IsAvailable)
        {
            return KernelArenaStatus.InvalidState;
        }
        if (!TryTakeArenaIdentity(out arenaIdentity))
        {
            return KernelArenaStatus.ResourceExhausted;
        }

        candidate = new KernelArena(provider, initialPages, growthPages,
                                    maxBackingChunks, maxTotalPages,
                                    maxLiveAllocations, maxAlignment,
                                    arenaIdentity);
        status = candidate.TryAcquireChunk(initialPages);
        if (status != KernelArenaStatus.Ok)
        {
            return status;
        }
        if (!candidate.ValidateInvariants())
        {
            candidate.ReleaseAllBackingForFailedCreate();
            return KernelArenaStatus.Corrupted;
        }
        arena = candidate;
        return KernelArenaStatus.Ok;
    }

    internal ulong ArenaIdentity => _arenaIdentity;
    internal bool IsDestroyed => _destroyed;
    internal uint LiveAllocationCount => _liveAllocationCount;
    internal uint BackingChunkCount => _chunkCount;
    internal ulong TotalBackingPages => _totalPages;

    internal KernelArenaMetrics GetMetrics()
    {
        uint activeChunks = 0;
        ulong totalBackingBytes = 0;
        ulong liveRequestedBytes = 0;
        ulong freeBytes = 0;
        ulong largestFreeBlock = 0;

        if (_destroyed)
        {
            return new KernelArenaMetrics(0, 0, 0, 0, 0, 0);
        }
        for (int chunkIndex = 0; chunkIndex != _chunks.Length; ++chunkIndex)
        {
            if (!_chunks[chunkIndex].Active) continue;
            activeChunks++;
            totalBackingBytes += _chunks[chunkIndex].Backing.ByteLength;
        }
        for (int allocationIndex = 0;
             allocationIndex != _allocations.Length; ++allocationIndex)
        {
            if (_allocations[allocationIndex].Live)
            {
                liveRequestedBytes +=
                    _allocations[allocationIndex].Descriptor.RequestedByteLength;
            }
        }
        for (int blockIndex = 0; blockIndex != _blocks.Length; ++blockIndex)
        {
            if (_blocks[blockIndex].State == BlockFree)
            {
                freeBytes += _blocks[blockIndex].Length;
                if (_blocks[blockIndex].Length > largestFreeBlock)
                {
                    largestFreeBlock = _blocks[blockIndex].Length;
                }
            }
        }
        return new KernelArenaMetrics(_liveAllocationCount, activeChunks,
                                      totalBackingBytes, liveRequestedBytes,
                                      freeBytes, largestFreeBlock);
    }

    internal KernelArenaStatus TryAllocate(
        ulong requestedByteLength,
        ulong alignment,
        out KernelArenaAllocation allocation)
    {
        allocation = default;
        if (_destroyed) return KernelArenaStatus.InvalidState;
        if (requestedByteLength == 0 || requestedByteLength >
                _maxTotalPages * PageSize ||
            !IsPowerOfTwo(alignment) || alignment > _maxAlignment)
        {
            return KernelArenaStatus.InvalidArgument;
        }
        if (_liveAllocationCount >= _maxLiveAllocations)
        {
            return KernelArenaStatus.ResourceExhausted;
        }
        if (!TryFindFreeAllocationSlot(out int allocationSlot))
        {
            return KernelArenaStatus.ResourceExhausted;
        }
        KernelArenaStatus existingStatus =
            TryAllocateFromExistingFreeBlock(requestedByteLength, alignment,
                                              allocationSlot, out allocation);
        if (existingStatus != KernelArenaStatus.Ok)
        {
            if (existingStatus != KernelArenaStatus.NotFound)
            {
                return existingStatus;
            }
            KernelArenaStatus growthStatus =
                TryAcquireGrowth(requestedByteLength, alignment);
            if (growthStatus != KernelArenaStatus.Ok)
            {
                return growthStatus;
            }
            if (TryAllocateFromExistingFreeBlock(requestedByteLength,
                                                 alignment, allocationSlot,
                                                 out allocation) !=
                KernelArenaStatus.Ok)
            {
                TryReleaseMostRecentChunk();
                return KernelArenaStatus.Corrupted;
            }
        }
        return ValidateInvariants() ? KernelArenaStatus.Ok :
            KernelArenaStatus.Corrupted;
    }

    internal KernelArenaStatus Free(in KernelArenaAllocation allocation)
    {
        int allocationSlot;
        ArenaAllocationRecord record;
        int blockIndex;

        if (_destroyed) return KernelArenaStatus.InvalidState;
        if (!ValidateInvariants()) return KernelArenaStatus.Corrupted;
        if (allocation.ArenaIdentity != _arenaIdentity ||
            allocation.ArenaIdentity == 0)
        {
            return KernelArenaStatus.OwnershipMismatch;
        }
        if (!TryFindLiveAllocation(allocation.AllocationId,
                                   out allocationSlot))
        {
            return KernelArenaStatus.NotFound;
        }
        record = _allocations[allocationSlot];
        if (!DescriptorEquals(in record.Descriptor, in allocation))
        {
            return KernelArenaStatus.OwnershipMismatch;
        }
        blockIndex = record.BlockIndex;
        if (blockIndex < 0 || blockIndex >= _blocks.Length ||
            _blocks[blockIndex].State != BlockAllocated ||
            _blocks[blockIndex].AllocationSlot != allocationSlot)
        {
            return KernelArenaStatus.Corrupted;
        }

        _blocks[blockIndex].State = BlockFree;
        _blocks[blockIndex].AllocationSlot = -1;
        _allocations[allocationSlot].Live = false;
        _liveAllocationCount--;
        CoalesceAround(blockIndex);
        return ValidateInvariants() ? KernelArenaStatus.Ok :
            KernelArenaStatus.Corrupted;
    }

    internal KernelArenaStatus Destroy()
    {
        if (_destroyed) return KernelArenaStatus.InvalidState;
        if (!ValidateInvariants()) return KernelArenaStatus.Corrupted;
        if (_liveAllocationCount != 0) return KernelArenaStatus.LiveAllocations;

        for (int chunkIndex = 0; chunkIndex != _chunks.Length; ++chunkIndex)
        {
            if (!_chunks[chunkIndex].Active) continue;
            KernelMemoryRegion backing = _chunks[chunkIndex].Backing;
            if (!_provider.TryRelease(in backing)) return KernelArenaStatus.InvalidState;
            RemoveChunkBlocks(chunkIndex);
            _chunks[chunkIndex] = default;
            _chunkCount--;
            _totalPages -= backing.PageCount;
        }
        _destroyed = true;
        return ValidateInvariants() ? KernelArenaStatus.Ok :
            KernelArenaStatus.Corrupted;
    }

    internal bool ValidateInvariants()
    {
        uint activeChunkCount = 0;
        ulong totalPages = 0;
        uint liveAllocationCount = 0;
        int usedBlockCount = 0;

        if (_provider == null || _arenaIdentity == 0 ||
            _maxBackingChunks == 0 ||
            _maxBackingChunks > (uint)_chunks.Length ||
            _maxTotalPages == 0 || _maxTotalPages > MaxArenaPages ||
            _maxLiveAllocations == 0 ||
            _maxLiveAllocations > (uint)_allocations.Length ||
            !IsPowerOfTwo(_maxAlignment) || _maxAlignment > DefaultMaxAlignment)
        {
            return false;
        }
        if (_destroyed)
        {
            return _chunkCount == 0 && _totalPages == 0 &&
                   _liveAllocationCount == 0 && _blockUsedCount == 0;
        }
        if (_chunkCount == 0 || _totalPages == 0) return false;

        for (int chunkIndex = 0; chunkIndex != _chunks.Length; ++chunkIndex)
        {
            ArenaChunk chunk = _chunks[chunkIndex];
            if (!chunk.Active) continue;
            activeChunkCount++;
            if (chunk.Identity == 0 || !IsValidRegionShape(in chunk.Backing) ||
                !_provider.IsValidRegion(in chunk.Backing) ||
                chunk.FirstBlock < 0 || chunk.FirstBlock >= _blocks.Length)
            {
                return false;
            }
            if (!TryAdd(totalPages, chunk.Backing.PageCount,
                        out ulong newTotalPages) ||
                newTotalPages > _maxTotalPages)
            {
                return false;
            }
            totalPages = newTotalPages;

            int blockIndex = chunk.FirstBlock;
            int previous = -1;
            ulong expectedOffset = 0;
            int walked = 0;
            while (blockIndex >= 0)
            {
                if (++walked > _blocks.Length) return false;
                if (blockIndex >= _blocks.Length) return false;
                ArenaBlock block = _blocks[blockIndex];
                if (block.Next < -1 || block.Next >= _blocks.Length ||
                    block.Previous < -1 || block.Previous >= _blocks.Length ||
                    block.State == BlockUnused || block.ChunkIndex != chunkIndex ||
                    block.Previous != previous || block.Length == 0 ||
                    block.Offset != expectedOffset ||
                    block.Offset > chunk.Backing.ByteLength ||
                    block.Length > chunk.Backing.ByteLength - block.Offset)
                {
                    return false;
                }
                usedBlockCount++;
                ulong blockEnd;
                if (!TryAdd(block.Offset, block.Length, out blockEnd))
                {
                    return false;
                }
                if (block.State == BlockFree)
                {
                    if (block.AllocationSlot != -1 ||
                        (block.Next >= 0 && _blocks[block.Next].State == BlockFree))
                    {
                        return false;
                    }
                }
                else if (block.State == BlockAllocated)
                {
                    if (block.AllocationSlot < 0 ||
                        block.AllocationSlot >= _allocations.Length ||
                        !_allocations[block.AllocationSlot].Live ||
                        _allocations[block.AllocationSlot].BlockIndex != blockIndex ||
                        !ValidateAllocationRecord(
                            in _allocations[block.AllocationSlot], chunkIndex,
                            blockIndex))
                    {
                        return false;
                    }
                    liveAllocationCount++;
                }
                else
                {
                    return false;
                }
                if (block.Next >= 0)
                {
                    if (block.Next >= _blocks.Length ||
                        _blocks[block.Next].Offset != blockEnd)
                    {
                        return false;
                    }
                }
                else if (blockEnd != chunk.Backing.ByteLength)
                {
                    return false;
                }
                expectedOffset = blockEnd;
                previous = blockIndex;
                blockIndex = block.Next;
            }
            if (expectedOffset != chunk.Backing.ByteLength) return false;
        }
        if (activeChunkCount != _chunkCount || totalPages != _totalPages ||
            liveAllocationCount != _liveAllocationCount ||
            usedBlockCount != _blockUsedCount)
        {
            return false;
        }
        for (int blockIndex = 0; blockIndex != _blocks.Length; ++blockIndex)
        {
            if (_blocks[blockIndex].State != BlockUnused &&
                !BlockIsInAnyActiveList(blockIndex))
            {
                return false;
            }
        }
        for (int allocationIndex = 0;
             allocationIndex != _allocations.Length; ++allocationIndex)
        {
            if (!_allocations[allocationIndex].Live) continue;
            for (int other = allocationIndex + 1;
                 other != _allocations.Length; ++other)
            {
                if (_allocations[other].Live &&
                    _allocations[other].Descriptor.AllocationId ==
                    _allocations[allocationIndex].Descriptor.AllocationId)
                {
                    return false;
                }
            }
        }
        return true;
    }

    internal bool TryWritePattern(
        in KernelArenaAllocation allocation, byte seed)
    {
        if (!TryFindLiveAllocation(allocation.AllocationId,
                                   out int allocationSlot) ||
            !DescriptorEquals(in _allocations[allocationSlot].Descriptor,
                              in allocation))
        {
            return false;
        }
        byte* address = (byte*)(nuint)allocation.VirtualAddress;
        for (ulong index = 0; index != allocation.RequestedByteLength; ++index)
        {
            address[(nuint)index] = Pattern(allocation.AllocationId, index, seed);
        }
        return true;
    }

    internal bool TryVerifyPattern(
        in KernelArenaAllocation allocation, byte seed)
    {
        if (!TryFindLiveAllocation(allocation.AllocationId,
                                   out int allocationSlot) ||
            !DescriptorEquals(in _allocations[allocationSlot].Descriptor,
                              in allocation))
        {
            return false;
        }
        byte* address = (byte*)(nuint)allocation.VirtualAddress;
        for (ulong index = 0; index != allocation.RequestedByteLength; ++index)
        {
            if (address[(nuint)index] !=
                Pattern(allocation.AllocationId, index, seed)) return false;
        }
        return true;
    }

    internal bool DebugCorruptDuplicateAllocationIdForTests()
    {
        int first = -1;
        for (int index = 0; index != _allocations.Length; ++index)
        {
            if (!_allocations[index].Live) continue;
            if (first < 0)
            {
                first = index;
                continue;
            }
            KernelArenaAllocation source = _allocations[first].Descriptor;
            KernelArenaAllocation target = _allocations[index].Descriptor;
            _allocations[index].Descriptor = new KernelArenaAllocation(
                target.ArenaIdentity, source.AllocationId, target.VirtualAddress,
                target.RequestedByteLength, target.ReservedByteLength,
                target.Alignment, target.ChunkIdentity, target.Cookie);
            return true;
        }
        return false;
    }

    internal bool DebugCorruptFirstBlockLengthForTests()
    {
        for (int index = 0; index != _blocks.Length; ++index)
        {
            if (_blocks[index].State != BlockUnused)
            {
                _blocks[index].Length = ulong.MaxValue;
                return true;
            }
        }
        return false;
    }

    private static bool IsPowerOfTwo(ulong value)
    {
        return value != 0 && (value & (value - 1)) == 0;
    }

    private static bool TryAdd(ulong left, ulong right, out ulong result)
    {
        if (left > ulong.MaxValue - right)
        {
            result = 0;
            return false;
        }
        result = left + right;
        return true;
    }

    private static bool TryTakeArenaIdentity(out ulong identity)
    {
        identity = s_nextArenaIdentity;
        if (identity == 0 || identity == ulong.MaxValue) return false;
        s_nextArenaIdentity++;
        return true;
    }

    private static bool TryTakeAllocationIdentity(out ulong identity)
    {
        identity = s_nextAllocationIdentity;
        if (identity == 0 || identity == ulong.MaxValue) return false;
        s_nextAllocationIdentity++;
        return true;
    }

    private static ulong MakeCookie(ulong arenaIdentity, ulong allocationId)
    {
        return arenaIdentity ^ allocationId ^ CookieConstant;
    }

    private static bool IsValidRegionShape(in KernelMemoryRegion region)
    {
        return region.AllocationId != 0 && region.VirtualAddress != 0 &&
               region.ByteLength != 0 && region.PageCount != 0 &&
               region.PageSize == PageSize && region.Flags == 0 &&
               region.PageCount <= ulong.MaxValue / PageSize &&
               region.ByteLength == region.PageCount * PageSize &&
               region.VirtualAddress <= ulong.MaxValue - region.ByteLength;
    }

    private static bool DescriptorEquals(
        in KernelArenaAllocation left, in KernelArenaAllocation right)
    {
        return left.ArenaIdentity == right.ArenaIdentity &&
               left.AllocationId == right.AllocationId &&
               left.VirtualAddress == right.VirtualAddress &&
               left.RequestedByteLength == right.RequestedByteLength &&
               left.ReservedByteLength == right.ReservedByteLength &&
               left.Alignment == right.Alignment &&
               left.ChunkIdentity == right.ChunkIdentity &&
               left.Cookie == right.Cookie;
    }

    private bool TryFindFreeAllocationSlot(out int slot)
    {
        for (slot = 0; slot != _allocations.Length; ++slot)
        {
            if (!_allocations[slot].Live) return true;
        }
        slot = -1;
        return false;
    }

    private bool TryFindLiveAllocation(ulong allocationId, out int slot)
    {
        if (allocationId == 0)
        {
            slot = -1;
            return false;
        }
        for (slot = 0; slot != _allocations.Length; ++slot)
        {
            if (_allocations[slot].Live &&
                _allocations[slot].Descriptor.AllocationId == allocationId)
            {
                return true;
            }
        }
        slot = -1;
        return false;
    }

    private bool TryFindUnusedBlock(out int slot, int excluded)
    {
        return TryFindUnusedBlock(out slot, excluded, -1);
    }

    private bool TryFindUnusedBlock(
        out int slot, int excluded, int secondExcluded)
    {
        for (slot = 0; slot != _blocks.Length; ++slot)
        {
            if (slot != excluded && slot != secondExcluded &&
                _blocks[slot].State == BlockUnused)
            {
                return true;
            }
        }
        slot = -1;
        return false;
    }

    private KernelArenaStatus TryAllocateFromExistingFreeBlock(
        ulong requestedByteLength,
        ulong alignment,
        int allocationSlot,
        out KernelArenaAllocation allocation)
    {
        allocation = default;
        for (int blockIndex = 0; blockIndex != _blocks.Length; ++blockIndex)
        {
            if (_blocks[blockIndex].State != BlockFree) continue;
            if (!TryPlanBlock(blockIndex, requestedByteLength, alignment,
                             allocationSlot, out BlockPlan plan)) continue;
            if (!TryTakeAllocationIdentity(out ulong allocationId))
            {
                return KernelArenaStatus.ResourceExhausted;
            }
            CommitBlockPlan(in plan, allocationId, requestedByteLength,
                            alignment, out allocation);
            return KernelArenaStatus.Ok;
        }
        return KernelArenaStatus.NotFound;
    }

    private bool TryPlanBlock(
        int blockIndex,
        ulong requestedByteLength,
        ulong alignment,
        int allocationSlot,
        out BlockPlan plan)
    {
        plan = default;
        ArenaBlock block = _blocks[blockIndex];
        ArenaChunk chunk = _chunks[block.ChunkIndex];
        if (block.State != BlockFree || !chunk.Active ||
            !TryAdd(chunk.Backing.VirtualAddress, block.Offset,
                    out ulong blockAddress) ||
            !TryAlignUp(blockAddress, alignment, out ulong alignedAddress) ||
            alignedAddress < blockAddress)
        {
            return false;
        }
        ulong padding = alignedAddress - blockAddress;
        if (padding >= block.Length ||
            requestedByteLength > block.Length - padding)
        {
            return false;
        }
        ulong availableLength = block.Length - padding;
        ulong remaining = availableLength - requestedByteLength;
        int allocationBlockIndex = blockIndex;
        int trailingBlockIndex = -1;
        if (padding != 0)
        {
            if (!TryFindUnusedBlock(out allocationBlockIndex, blockIndex))
            {
                return false;
            }
            if (remaining != 0 &&
                TryFindUnusedBlock(out trailingBlockIndex,
                                   allocationBlockIndex, blockIndex))
            {
                // Keep a separate trailing free block when bounded metadata
                // has room. If it does not, the allocation reserves the tail.
            }
        }
        else if (remaining != 0)
        {
            TryFindUnusedBlock(out trailingBlockIndex, blockIndex);
        }
        ulong reservedLength = trailingBlockIndex >= 0
            ? requestedByteLength : availableLength;
        ulong trailingLength = trailingBlockIndex >= 0
            ? remaining : 0;
        plan = new BlockPlan
        {
            BlockIndex = blockIndex,
            AllocationBlockIndex = allocationBlockIndex,
            TrailingBlockIndex = trailingBlockIndex,
            AllocationSlot = allocationSlot,
            Padding = padding,
            AvailableLength = availableLength,
            ReservedLength = reservedLength,
            TrailingLength = trailingLength,
            AllocationOffset = block.Offset + padding
        };
        return plan.AllocationOffset >= block.Offset;
    }

    private static bool TryAlignUp(
        ulong address, ulong alignment, out ulong alignedAddress)
    {
        ulong mask = alignment - 1;
        if (address > ulong.MaxValue - mask)
        {
            alignedAddress = 0;
            return false;
        }
        alignedAddress = (address + mask) & ~mask;
        return alignedAddress >= address;
    }

    private void CommitBlockPlan(
        in BlockPlan plan,
        ulong allocationId,
        ulong requestedByteLength,
        ulong alignment,
        out KernelArenaAllocation allocation)
    {
        ArenaBlock original = _blocks[plan.BlockIndex];
        int oldNext = original.Next;
        int allocationBlockIndex = plan.AllocationBlockIndex;
        ulong chunkIdentity = _chunks[original.ChunkIndex].Identity;
        ulong virtualAddress = _chunks[original.ChunkIndex].Backing.VirtualAddress +
                               plan.AllocationOffset;

        if (plan.Padding != 0)
        {
            _blocks[plan.BlockIndex].Length = plan.Padding;
            _blocks[plan.AllocationBlockIndex] = new ArenaBlock
            {
                State = BlockAllocated,
                ChunkIndex = original.ChunkIndex,
                Previous = plan.BlockIndex,
                Next = plan.TrailingBlockIndex >= 0
                    ? plan.TrailingBlockIndex : oldNext,
                AllocationSlot = plan.AllocationSlot,
                Offset = plan.AllocationOffset,
                Length = plan.ReservedLength
            };
            _blocks[plan.BlockIndex].Next = allocationBlockIndex;
            if (plan.TrailingBlockIndex >= 0)
            {
                _blocks[plan.TrailingBlockIndex] = new ArenaBlock
                {
                    State = BlockFree,
                    ChunkIndex = original.ChunkIndex,
                    Previous = allocationBlockIndex,
                    Next = oldNext,
                    AllocationSlot = -1,
                    Offset = plan.AllocationOffset + plan.ReservedLength,
                    Length = plan.TrailingLength
                };
                _blocks[allocationBlockIndex].Next = plan.TrailingBlockIndex;
            }
            if (oldNext >= 0)
            {
                _blocks[oldNext].Previous = plan.TrailingBlockIndex >= 0
                    ? plan.TrailingBlockIndex : allocationBlockIndex;
            }
            _blockUsedCount += plan.TrailingBlockIndex >= 0 ? 2 : 1;
        }
        else
        {
            _blocks[plan.BlockIndex].State = BlockAllocated;
            _blocks[plan.BlockIndex].AllocationSlot = plan.AllocationSlot;
            _blocks[plan.BlockIndex].Length = plan.ReservedLength;
            if (plan.TrailingBlockIndex >= 0)
            {
                _blocks[plan.TrailingBlockIndex] = new ArenaBlock
                {
                    State = BlockFree,
                    ChunkIndex = original.ChunkIndex,
                    Previous = plan.BlockIndex,
                    Next = oldNext,
                    AllocationSlot = -1,
                    Offset = plan.AllocationOffset + plan.ReservedLength,
                    Length = plan.TrailingLength
                };
                _blocks[plan.BlockIndex].Next = plan.TrailingBlockIndex;
                if (oldNext >= 0) _blocks[oldNext].Previous = plan.TrailingBlockIndex;
                _blockUsedCount++;
            }
        }

        allocation = new KernelArenaAllocation(
            _arenaIdentity, allocationId, virtualAddress, requestedByteLength,
            plan.ReservedLength, alignment, chunkIdentity,
            MakeCookie(_arenaIdentity, allocationId));
        _allocations[plan.AllocationSlot] = new ArenaAllocationRecord
        {
            Live = true,
            BlockIndex = allocationBlockIndex,
            Descriptor = allocation
        };
        _liveAllocationCount++;
    }

    private KernelArenaStatus TryAcquireGrowth(
        ulong requestedByteLength, ulong alignment)
    {
        if (!TryAdd(requestedByteLength, alignment - 1,
                    out ulong minimumBytes))
        {
            return KernelArenaStatus.InvalidArgument;
        }
        if (minimumBytes > ulong.MaxValue - (PageSize - 1))
        {
            return KernelArenaStatus.InvalidArgument;
        }
        ulong requiredPages = (minimumBytes + PageSize - 1) / PageSize;
        if (requiredPages < _growthPages) requiredPages = _growthPages;
        if (requiredPages == 0 || requiredPages > Phase4MaxPagesPerAllocation)
        {
            return KernelArenaStatus.InvalidArgument;
        }
        return TryAcquireChunk(requiredPages);
    }

    private KernelArenaStatus TryAcquireChunk(ulong pageCount)
    {
        if (_destroyed || pageCount == 0 ||
            pageCount > Phase4MaxPagesPerAllocation ||
            _chunkCount >= _maxBackingChunks || pageCount > _maxTotalPages ||
            _totalPages > _maxTotalPages - pageCount)
        {
            return KernelArenaStatus.ResourceExhausted;
        }
        if (!TryFindUnusedChunk(out int chunkIndex) ||
            !TryFindUnusedBlock(out int blockIndex, -1))
        {
            return KernelArenaStatus.ResourceExhausted;
        }
        if (!_provider.TryAllocate(pageCount, 0,
                                   out KernelMemoryRegion backing))
        {
            return KernelArenaStatus.ResourceExhausted;
        }
        if (!IsValidRegionShape(in backing) ||
            backing.PageCount != pageCount ||
            !_provider.IsValidRegion(in backing))
        {
            if (backing.AllocationId != 0) _provider.TryRelease(in backing);
            return KernelArenaStatus.InvalidState;
        }
        if (!TryTakeChunkIdentity(out ulong chunkIdentity))
        {
            _provider.TryRelease(in backing);
            return KernelArenaStatus.ResourceExhausted;
        }
        _chunks[chunkIndex] = new ArenaChunk
        {
            Active = true,
            Identity = chunkIdentity,
            Backing = backing,
            FirstBlock = blockIndex
        };
        _blocks[blockIndex] = new ArenaBlock
        {
            State = BlockFree,
            ChunkIndex = chunkIndex,
            Previous = -1,
            Next = -1,
            AllocationSlot = -1,
            Offset = 0,
            Length = backing.ByteLength
        };
        _blockUsedCount++;
        _chunkCount++;
        _totalPages += pageCount;
        return KernelArenaStatus.Ok;
    }

    private bool TryFindUnusedChunk(out int chunkIndex)
    {
        for (chunkIndex = 0; chunkIndex != _chunks.Length; ++chunkIndex)
        {
            if (!_chunks[chunkIndex].Active) return true;
        }
        chunkIndex = -1;
        return false;
    }

    private bool TryTakeChunkIdentity(out ulong identity)
    {
        identity = _nextChunkIdentity;
        if (identity == 0 || identity == ulong.MaxValue) return false;
        _nextChunkIdentity++;
        return true;
    }

    private bool ValidateAllocationRecord(
        in ArenaAllocationRecord record, int chunkIndex, int blockIndex)
    {
        KernelArenaAllocation allocation = record.Descriptor;
        ArenaBlock block = _blocks[blockIndex];
        ArenaChunk chunk = _chunks[chunkIndex];
        if (!record.Live || allocation.ArenaIdentity != _arenaIdentity ||
            allocation.AllocationId == 0 ||
            allocation.Cookie != MakeCookie(_arenaIdentity,
                                            allocation.AllocationId) ||
            allocation.ChunkIdentity != chunk.Identity ||
            allocation.RequestedByteLength == 0 ||
            allocation.RequestedByteLength > allocation.ReservedByteLength ||
            allocation.ReservedByteLength != block.Length ||
            !IsPowerOfTwo(allocation.Alignment) ||
            allocation.Alignment > _maxAlignment ||
            allocation.VirtualAddress % allocation.Alignment != 0 ||
            !TryAdd(chunk.Backing.VirtualAddress, block.Offset,
                    out ulong expectedAddress) ||
            expectedAddress != allocation.VirtualAddress ||
            allocation.VirtualAddress > ulong.MaxValue -
                allocation.RequestedByteLength)
        {
            return false;
        }
        return true;
    }

    private void CoalesceAround(int blockIndex)
    {
        int previous = _blocks[blockIndex].Previous;
        if (previous >= 0 &&
            _blocks[previous].State == BlockFree &&
            _blocks[previous].ChunkIndex == _blocks[blockIndex].ChunkIndex)
        {
            _blocks[previous].Length += _blocks[blockIndex].Length;
            RemoveBlockFromList(blockIndex);
            blockIndex = previous;
        }
        int next = _blocks[blockIndex].Next;
        if (next >= 0 && _blocks[next].State == BlockFree &&
            _blocks[next].ChunkIndex == _blocks[blockIndex].ChunkIndex)
        {
            _blocks[blockIndex].Length += _blocks[next].Length;
            RemoveBlockFromList(next);
        }
    }

    private void RemoveBlockFromList(int blockIndex)
    {
        int previous = _blocks[blockIndex].Previous;
        int next = _blocks[blockIndex].Next;
        if (previous >= 0) _blocks[previous].Next = next;
        else _chunks[_blocks[blockIndex].ChunkIndex].FirstBlock = next;
        if (next >= 0) _blocks[next].Previous = previous;
        _blocks[blockIndex] = default;
        _blockUsedCount--;
    }

    private void RemoveChunkBlocks(int chunkIndex)
    {
        for (int blockIndex = 0; blockIndex != _blocks.Length; ++blockIndex)
        {
            if (_blocks[blockIndex].State != BlockUnused &&
                _blocks[blockIndex].ChunkIndex == chunkIndex)
            {
                _blocks[blockIndex] = default;
                _blockUsedCount--;
            }
        }
    }

    private bool BlockIsInAnyActiveList(int target)
    {
        for (int chunkIndex = 0; chunkIndex != _chunks.Length; ++chunkIndex)
        {
            if (!_chunks[chunkIndex].Active) continue;
            int blockIndex = _chunks[chunkIndex].FirstBlock;
            int walked = 0;
            while (blockIndex >= 0 && walked++ <= _blocks.Length)
            {
                if (blockIndex == target) return true;
                blockIndex = _blocks[blockIndex].Next;
            }
        }
        return false;
    }

    private void ReleaseAllBackingForFailedCreate()
    {
        for (int chunkIndex = 0; chunkIndex != _chunks.Length; ++chunkIndex)
        {
            if (_chunks[chunkIndex].Active)
            {
                KernelMemoryRegion backing = _chunks[chunkIndex].Backing;
                _provider.TryRelease(in backing);
                _chunks[chunkIndex] = default;
            }
        }
        _chunkCount = 0;
        _totalPages = 0;
        _blockUsedCount = 0;
    }

    private void TryReleaseMostRecentChunk()
    {
        int selected = -1;
        ulong selectedIdentity = 0;
        for (int chunkIndex = 0; chunkIndex != _chunks.Length; ++chunkIndex)
        {
            if (_chunks[chunkIndex].Active &&
                _chunks[chunkIndex].Identity >= selectedIdentity)
            {
                selected = chunkIndex;
                selectedIdentity = _chunks[chunkIndex].Identity;
            }
        }
        if (selected < 0) return;
        KernelMemoryRegion backing = _chunks[selected].Backing;
        if (!_provider.TryRelease(in backing)) return;
        RemoveChunkBlocks(selected);
        _chunks[selected] = default;
        _chunkCount--;
        _totalPages -= backing.PageCount;
    }

    private static byte Pattern(ulong allocationId, ulong index, byte seed)
    {
        ulong value = unchecked(allocationId * 0x9E3779B97F4A7C15UL +
                                index * 0xD1B54A32D192ED03UL +
                                ((ulong)seed << 32) +
                                0xA0761D6478BD642FUL);
        return (byte)(value ^ (value >> 19) ^ (value >> 43));
    }
}

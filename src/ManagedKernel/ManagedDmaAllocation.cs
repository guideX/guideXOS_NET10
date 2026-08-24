using System;

namespace GuideXOS.Net10.ManagedKernel;

/* DMA memory is native-authoritative.  Managed code sees only the opaque
   allocation handle and device-visible bus address; copies cross the ABI via
   a temporarily pinned managed buffer. */
internal unsafe sealed class ManagedDmaAllocation
{
    private readonly uint _driverId;
    private readonly ulong _handle;
    private int _live = 1;
    private int _retained;

    private ManagedDmaAllocation(uint driverId,
                                 in GxManagedKernelDmaAllocationResultV1 result)
    {
        _driverId = driverId;
        _handle = result.Handle;
        BusAddress = result.BusAddress;
        ByteLength = result.ByteLength;
        PageCount = result.PageCount;
        Alignment = result.Alignment;
    }

    internal ulong Handle => _handle;
    internal ulong BusAddress { get; }
    internal ulong ByteLength { get; }
    internal ulong PageCount { get; }
    internal ulong Alignment { get; }
    internal bool IsLive => _live != 0;

    internal static ManagedDmaAllocation? TryAllocate(
        ulong claimHandle, uint driverId, ulong bytes, ulong alignment)
    {
        if (claimHandle == 0 || driverId == 0 || bytes == 0 || alignment == 0 ||
            !ManagedKernelContract.TryDmaAllocate(
                claimHandle, driverId, bytes, alignment,
                out GxManagedKernelDmaAllocationResultV1 result)) return null;
        ManagedDmaAllocation candidate = new(driverId, in result);
        if (!candidate.TryRetain())
        {
            candidate.TryRelease();
            return null;
        }
        return candidate;
    }

    internal bool TryRetain()
    {
        if (_live == 0 || _retained != 0 ||
            !ManagedKernelContract.TryDmaRetain(_handle, _driverId)) return false;
        _retained = 1;
        return true;
    }

    internal bool TryReleaseReference()
    {
        if (_live == 0 || _retained == 0 ||
            !ManagedKernelContract.TryDmaReleaseReference(_handle, _driverId))
            return false;
        _retained = 0;
        return true;
    }

    internal bool TryRelease()
    {
        if (_live == 0 || _retained != 0 ||
            !ManagedKernelContract.TryDmaRelease(_handle, _driverId)) return false;
        _live = 0;
        return true;
    }

    internal bool TryReleaseForTeardown()
    {
        if (_live == 0) return true;
        return (_retained == 0 || TryReleaseReference()) && TryRelease();
    }

    internal bool TryWrite(ulong offset, ReadOnlySpan<byte> bytes)
    {
        if (_live == 0 || bytes.Length == 0 ||
            offset > ByteLength || (ulong)bytes.Length > ByteLength - offset)
            return false;
        fixed (byte* source = bytes)
        {
            return ManagedKernelContract.TryDmaWrite(
                _handle, _driverId, offset, (nuint)source, (ulong)bytes.Length);
        }
    }

    internal bool TryRead(ulong offset, Span<byte> bytes)
    {
        if (_live == 0 || bytes.Length == 0 ||
            offset > ByteLength || (ulong)bytes.Length > ByteLength - offset)
            return false;
        fixed (byte* destination = bytes)
        {
            return ManagedKernelContract.TryDmaRead(
                _handle, _driverId, offset, (nuint)destination, (ulong)bytes.Length);
        }
    }

    internal bool TryWrite32(ulong offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        Write32(bytes, value);
        return TryWrite(offset, bytes);
    }

    internal bool TryWrite64(ulong offset, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        Write64(bytes, value);
        return TryWrite(offset, bytes);
    }

    internal bool TryRead8(ulong offset, out byte value)
    {
        Span<byte> bytes = stackalloc byte[1];
        value = 0;
        if (!TryRead(offset, bytes)) return false;
        value = bytes[0];
        return true;
    }

    internal bool TryRead16(ulong offset, out ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        value = 0;
        if (!TryRead(offset, bytes)) return false;
        value = (ushort)(bytes[0] | (bytes[1] << 8));
        return true;
    }

    private static void Write32(Span<byte> bytes, uint value)
    {
        bytes[0] = (byte)value;
        bytes[1] = (byte)(value >> 8);
        bytes[2] = (byte)(value >> 16);
        bytes[3] = (byte)(value >> 24);
    }

    private static void Write64(Span<byte> bytes, ulong value)
    {
        Write32(bytes, (uint)value);
        Write32(bytes[4..], (uint)(value >> 32));
    }
}

using System;

namespace GuideXOS.Net10.ManagedKernel;

/* A mapping is an opaque managed capability. It deliberately exposes no
   pointer, Span, or physical address; every access is revalidated by the
   native mapping service. */
internal sealed class ManagedMmioMapping
{
    private readonly ulong _resourceId;
    private readonly uint _driverId;
    private readonly ulong _handle;
    private readonly ulong _length;
    private int _live = 1;

    internal ManagedMmioMapping(ulong resourceId, uint driverId,
                                 ulong handle, ulong length)
    {
        _resourceId = resourceId;
        _driverId = driverId;
        _handle = handle;
        _length = length;
    }

    internal ulong Handle => _handle;
    internal ulong Length => _length;
    internal bool IsLive => _live != 0;

    internal bool TryRead8(ulong offset, out byte value)
    {
        value = 0;
        if (!TryRead(offset, 1, out ulong raw)) return false;
        value = (byte)raw;
        return true;
    }

    internal bool TryRead16(ulong offset, out ushort value)
    {
        value = 0;
        if (!TryRead(offset, 2, out ulong raw)) return false;
        value = (ushort)raw;
        return true;
    }

    internal bool TryRead32(ulong offset, out uint value)
    {
        value = 0;
        if (!TryRead(offset, 4, out ulong raw)) return false;
        value = (uint)raw;
        return true;
    }

    internal bool TryRead64(ulong offset, out ulong value)
    {
        return TryRead(offset, 8, out value);
    }

    internal bool TryUnmap()
    {
        if (_live == 0 || !ManagedDeviceResourceRuntimeCatalog.TryUnmap(
                _resourceId, _driverId, _handle)) return false;
        _live = 0;
        return true;
    }

    private bool TryRead(ulong offset, uint width, out ulong value)
    {
        value = 0;
        if (_live == 0 || (width != 1 && width != 2 && width != 4 && width != 8) ||
            offset > ulong.MaxValue - width || offset + width > _length ||
            (width > 1 && (offset & (width - 1)) != 0)) return false;
        return ManagedKernelContract.TryMmioRead(_handle, _driverId,
                                                 offset, width, out value);
    }
}

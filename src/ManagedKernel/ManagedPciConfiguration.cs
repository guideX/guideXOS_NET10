using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static unsafe class PciConfiguration
{
    internal const uint ConfigSpaceSize = 256;
    internal const uint Width8 = 1;
    internal const uint Width16 = 2;
    internal const uint Width32 = 4;

    internal static bool IsAvailable =>
        ManagedKernelContract.PciServicesInstalled &&
        ManagedKernelContract.PciConfigReadAddress != 0;

    internal static bool TryRead8(in ManagedDevice device, uint offset,
                                  out byte value)
    {
        value = 0;
        if (!TryRead(in device, offset, Width8, out ulong result)) return false;
        value = (byte)result;
        return true;
    }

    internal static bool TryRead16(in ManagedDevice device, uint offset,
                                   out ushort value)
    {
        value = 0;
        if (!TryRead(in device, offset, Width16, out ulong result)) return false;
        value = (ushort)result;
        return true;
    }

    internal static bool TryRead32(in ManagedDevice device, uint offset,
                                   out uint value)
    {
        value = 0;
        if (!TryRead(in device, offset, Width32, out ulong result)) return false;
        value = (uint)result;
        return true;
    }

    internal static bool TryReadForValidation(in ManagedDevice device,
                                              uint offset, uint width,
                                              out ulong value)
    {
        return TryRead(in device, offset, width, out value);
    }

    private static bool TryRead(in ManagedDevice device, uint offset,
                                uint width, out ulong value)
    {
        GxManagedKernelPciReadResultV1 result = new()
        {
            Size = 0xA5A5A5A5,
            AbiVersion = 0xA5A5A5A5,
            Width = 0xA5A5A5A5,
            Reserved0 = 0xA5A5A5A5,
            Value = 0xA5A5A5A5A5A5A5A5UL,
            Reserved1 = 0xA5A5A5A5A5A5A5A5UL
        };
        value = 0;
        if (!IsAvailable || !device.HasInventoryOwnership ||
            ManagedKernelContract.OperationalDeviceInventory == null ||
            !ManagedKernelContract.OperationalDeviceInventory.IsOwnedDevice(in device) ||
            offset >= ConfigSpaceSize ||
            width != Width8 && width != Width16 && width != Width32 ||
            offset > uint.MaxValue - width ||
            offset + width > ConfigSpaceSize ||
            width == Width16 && (offset & 1U) != 0U ||
            width == Width32 && (offset & 3U) != 0U)
        {
            return false;
        }

        delegate* unmanaged<uint, uint, uint, uint, uint, uint, nuint, nuint, uint>
            callback = (delegate* unmanaged<uint, uint, uint, uint, uint, uint,
                         nuint, nuint, uint>)ManagedKernelContract.PciConfigReadAddress;
        GxManagedKernelPciReadResultV1* resultAddress = &result;
        uint status = callback(device.Segment, device.Bus, device.Device,
                               device.Function, offset, width,
                               (nuint)resultAddress,
                               (nuint)GxManagedKernelPciReadResultV1.ExpectedSize);
        if (status != ManagedKernelContract.ManagedOk ||
            result.Size != GxManagedKernelPciReadResultV1.ExpectedSize ||
            result.AbiVersion != 1 || result.Width != width ||
            result.Reserved0 != 0 || result.Reserved1 != 0)
        {
            return false;
        }
        value = result.Value;
        return true;
    }
}

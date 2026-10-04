using System;

namespace GuideXOS.Net10.ManagedKernel;

internal static class KernelLog
{
    internal static bool Write(ReadOnlySpan<byte> value) => true;
}

internal static class ManagedE1000Driver
{
    internal static uint Phase16MacHigh;
    internal static uint Phase16MacLow;
}

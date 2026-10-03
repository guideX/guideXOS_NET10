using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelInterruptEventV1
{
    internal const uint ExpectedSize = 48;
    internal const uint AbiVersionCurrent = 1;
    internal const uint EventTypeSerialReceive = 1;
    internal const uint EventTypeKeyboardScancode = 2;
    internal const uint EventFlagHardwareCapture = 1;

    internal uint Size;
    internal uint AbiVersion;
    internal uint EventType;
    internal uint DeviceKind;
    internal uint DeviceId;
    internal ulong Sequence;
    internal uint Flags;
    internal byte PayloadByte;
    internal byte PayloadLength;
    internal ushort Reserved0;
    internal uint Status;
    internal ulong Timestamp;
}

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GuideXOS.Net10.ManagedKernel;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct GuideXBootInfo
{
    internal const uint ExpectedMagic = 0x534F5847;
    internal const ushort CurrentVersion = 1;
    internal const uint ArchitectureX64 = 0x8664;
    internal const ushort MinimumSize = 24;

    internal uint Magic;
    internal ushort Version;
    internal ushort Size;
    internal uint Architecture;
    internal uint Flags;
    internal ulong SerialWrite;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelInitializeRequestV1
{
    internal const uint ExpectedSize = 16;

    internal uint Size;
    internal uint AbiVersion;
    internal uint Architecture;
    internal uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelSystemInfoV1
{
    internal const uint ExpectedSize = 32;
    internal const ulong CapabilityServiceAbi = 1UL << 0;
    internal const ulong CapabilitySystemInformation = 1UL << 1;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal ulong Reserved;
}

internal static unsafe class ManagedKernelLayout
{
    internal static bool IsValid()
    {
        return sizeof(GxManagedKernelInitializeRequestV1) == 16 &&
               sizeof(GxManagedKernelSystemInfoV1) == 32 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.Architecture)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.Flags)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.ServiceVersion)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Architecture)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Reserved)).ToInt32() == 24;
    }
}

internal static unsafe class ManagedKernelContract
{
    internal const uint ManagedOk = 0;
    internal const uint InvalidArgument = 1;
    internal const uint UnsupportedAbi = 2;
    internal const uint BufferTooSmall = 3;
    internal const uint NotInitialized = 4;
    internal const uint AlreadyInitialized = 5;

    private const uint AbiVersionV1 = 1;
    private const uint ArchitectureX64 = 0x8664;
    private const uint ServiceVersionV1 = 1;
    private const ulong Capabilities =
        GxManagedKernelSystemInfoV1.CapabilityServiceAbi |
        GxManagedKernelSystemInfoV1.CapabilitySystemInformation;

    private static int s_initialized;

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInitialize")]
    internal static uint Initialize(uint requestedAbiVersion, nuint requestAddress)
    {
        if (requestedAbiVersion != AbiVersionV1)
        {
            return UnsupportedAbi;
        }

        if (requestAddress == 0)
        {
            return InvalidArgument;
        }

        GxManagedKernelInitializeRequestV1* request =
            (GxManagedKernelInitializeRequestV1*)requestAddress;
        if (request->Size < GxManagedKernelInitializeRequestV1.ExpectedSize ||
            request->AbiVersion != AbiVersionV1 ||
            request->Architecture != ArchitectureX64 ||
            request->Flags != 0)
        {
            return InvalidArgument;
        }

        if (s_initialized != 0)
        {
            return AlreadyInitialized;
        }

        s_initialized = 1;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedQuerySystemInfo")]
    internal static uint QuerySystemInfo(uint requestedAbiVersion,
                                         nuint outputAddress,
                                         nuint outputCapacity)
    {
        if (requestedAbiVersion != AbiVersionV1)
        {
            return UnsupportedAbi;
        }

        if (s_initialized == 0)
        {
            return NotInitialized;
        }

        if (outputAddress == 0)
        {
            return InvalidArgument;
        }

        if (outputCapacity < GxManagedKernelSystemInfoV1.ExpectedSize)
        {
            return BufferTooSmall;
        }

        if (outputCapacity > nuint.MaxValue - outputAddress)
        {
            return InvalidArgument;
        }

        GxManagedKernelSystemInfoV1 result = new()
        {
            Size = GxManagedKernelSystemInfoV1.ExpectedSize,
            AbiVersion = AbiVersionV1,
            ServiceVersion = ServiceVersionV1,
            Architecture = ArchitectureX64,
            Capabilities = Capabilities,
            Reserved = 0
        };
        *(GxManagedKernelSystemInfoV1*)outputAddress = result;
        return ManagedOk;
    }
}

public static unsafe class ManagedKernelEntry
{
    private static readonly byte[] BootstrapMarker =
        "GXOS_NET10:MANAGED_KERNEL_BOOTSTRAP_OK\r\n"u8.ToArray();

    [UnmanagedCallersOnly(EntryPoint = "ManagedMain")]
    public static int ManagedMain(nint bootInfoAddress)
    {
        if (!ManagedKernelLayout.IsValid() || bootInfoAddress == 0)
        {
            return -1;
        }

        GuideXBootInfo* bootInfo = (GuideXBootInfo*)bootInfoAddress;
        if (bootInfo->Magic != GuideXBootInfo.ExpectedMagic ||
            bootInfo->Version != GuideXBootInfo.CurrentVersion ||
            bootInfo->Size < GuideXBootInfo.MinimumSize ||
            bootInfo->Architecture != GuideXBootInfo.ArchitectureX64 ||
            bootInfo->SerialWrite == 0)
        {
            return -2;
        }

        delegate* unmanaged<byte*, nuint, void> serialWrite =
            (delegate* unmanaged<byte*, nuint, void>)(nuint)bootInfo->SerialWrite;
        fixed (byte* marker = BootstrapMarker)
        {
            serialWrite(marker, (nuint)BootstrapMarker.Length);
        }
        return 0;
    }

    // The freestanding loader calls the exported ManagedMain entry directly.
    public static int Main() => 0;
}

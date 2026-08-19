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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelBootResourceSummaryV1
{
    internal const uint ExpectedSize = 56;
    internal const uint ResourceMapIdentityUefiNormalizedV1 = 1;
    internal const ulong CapabilitySummary = 1UL << 0;
    internal const ulong CapabilityRegions = 1UL << 1;
    internal const ulong CapabilityTotals = 1UL << 2;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal uint RegionCount;
    internal uint ResourceMapIdentity;
    internal ulong TotalPhysicalBytes;
    internal ulong UsablePhysicalBytes;
    internal ulong Capabilities;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelBootResourceRegionV1
{
    internal const uint ExpectedSize = 32;

    internal uint Size;
    internal uint AbiVersion;
    internal ulong BaseAddress;
    internal ulong Length;
    internal uint Type;
    internal uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelBootResourcePublicationV1
{
    internal const uint ExpectedSize = 48;

    internal uint Size;
    internal uint AbiVersion;
    internal ulong SummaryAddress;
    internal ulong DescriptorAddress;
    internal uint DescriptorCount;
    internal uint DescriptorSize;
    internal ulong DescriptorByteLength;
    internal ulong Reserved;
}

internal static unsafe class ManagedKernelLayout
{
    internal static bool IsValid()
    {
        return sizeof(GuideXBootInfo) == 24 &&
               sizeof(GxManagedKernelInitializeRequestV1) == 16 &&
               sizeof(GxManagedKernelSystemInfoV1) == 32 &&
               sizeof(GxManagedKernelBootResourceSummaryV1) == 56 &&
               sizeof(GxManagedKernelBootResourceRegionV1) == 32 &&
               sizeof(GxManagedKernelBootResourcePublicationV1) == 48 &&
               Marshal.OffsetOf<GuideXBootInfo>(nameof(GuideXBootInfo.Magic)).ToInt32() == 0 &&
               Marshal.OffsetOf<GuideXBootInfo>(nameof(GuideXBootInfo.Version)).ToInt32() == 4 &&
               Marshal.OffsetOf<GuideXBootInfo>(nameof(GuideXBootInfo.Size)).ToInt32() == 6 &&
               Marshal.OffsetOf<GuideXBootInfo>(nameof(GuideXBootInfo.Architecture)).ToInt32() == 8 &&
               Marshal.OffsetOf<GuideXBootInfo>(nameof(GuideXBootInfo.Flags)).ToInt32() == 12 &&
               Marshal.OffsetOf<GuideXBootInfo>(nameof(GuideXBootInfo.SerialWrite)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.Architecture)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelInitializeRequestV1>(nameof(GxManagedKernelInitializeRequestV1.Flags)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.ServiceVersion)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Architecture)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelSystemInfoV1>(nameof(GxManagedKernelSystemInfoV1.Reserved)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.ServiceVersion)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.Architecture)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.RegionCount)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.ResourceMapIdentity)).ToInt32() == 20 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.TotalPhysicalBytes)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.UsablePhysicalBytes)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.Capabilities)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceSummaryV1>(nameof(GxManagedKernelBootResourceSummaryV1.Reserved)).ToInt32() == 48 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceRegionV1>(nameof(GxManagedKernelBootResourceRegionV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceRegionV1>(nameof(GxManagedKernelBootResourceRegionV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceRegionV1>(nameof(GxManagedKernelBootResourceRegionV1.BaseAddress)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceRegionV1>(nameof(GxManagedKernelBootResourceRegionV1.Length)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceRegionV1>(nameof(GxManagedKernelBootResourceRegionV1.Type)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelBootResourceRegionV1>(nameof(GxManagedKernelBootResourceRegionV1.Flags)).ToInt32() == 28 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.SummaryAddress)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.DescriptorAddress)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.DescriptorCount)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.DescriptorSize)).ToInt32() == 28 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.DescriptorByteLength)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.Reserved)).ToInt32() == 40;
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
    internal const uint OutOfRange = 6;

    private const uint AbiVersionV1 = 1;
    private const uint ArchitectureX64 = 0x8664;
    private const uint ServiceVersionV1 = 1;
    private const ulong Capabilities =
        GxManagedKernelSystemInfoV1.CapabilityServiceAbi |
        GxManagedKernelSystemInfoV1.CapabilitySystemInformation;
    private const uint BootResourcesAbiVersionV1 = 1;
    private const uint BootResourcesServiceVersionV1 = 1;
    private const uint BootResourceMapIdentityUefiNormalizedV1 = 1;
    private const uint BootResourceMaxRegions = 2048;
    private const uint BootResourceTypeConventional = 1;
    private const uint BootResourceTypeRuntimeServicesCode = 6;
    private const uint BootResourceTypeRuntimeServicesData = 7;
    private const uint BootResourceTypeUnknown = 16;
    private const uint BootResourceFlagUsable = 1U << 0;
    private const uint BootResourceFlagRamLike = 1U << 1;
    private const uint BootResourceFlagRuntime = 1U << 2;
    private const uint BootResourceKnownFlags =
        BootResourceFlagUsable | BootResourceFlagRamLike | BootResourceFlagRuntime;
    private const ulong BootResourceCapabilities =
        GxManagedKernelBootResourceSummaryV1.CapabilitySummary |
        GxManagedKernelBootResourceSummaryV1.CapabilityRegions |
        GxManagedKernelBootResourceSummaryV1.CapabilityTotals;

    private static int s_initialized;
    private static int s_bootResourcesPublished;
    private static nuint s_bootResourceSummaryAddress;
    private static nuint s_bootResourceDescriptorAddress;
    private static uint s_bootResourceDescriptorCount;
    private static nuint s_bootResourceDescriptorByteLength;

    private static bool IsRangeValid(nuint address, nuint length)
    {
        return address != 0 && length != 0 &&
               address <= nuint.MaxValue - length;
    }

    private static bool IsRamLike(uint type)
    {
        return type != 0 && type != 10 && type != 11 &&
               type != 12 && type != 13 && type != BootResourceTypeUnknown;
    }

    private static uint ExpectedFlags(uint type)
    {
        uint flags = IsRamLike(type) ? BootResourceFlagRamLike : 0;
        if (type == BootResourceTypeConventional)
        {
            flags |= BootResourceFlagUsable;
        }
        if (type == BootResourceTypeRuntimeServicesCode ||
            type == BootResourceTypeRuntimeServicesData)
        {
            flags |= BootResourceFlagRuntime;
        }
        return flags;
    }

    private static bool ValidateSummary(
        GxManagedKernelBootResourceSummaryV1* summary,
        uint expectedRegionCount)
    {
        return summary != null &&
               summary->Size == GxManagedKernelBootResourceSummaryV1.ExpectedSize &&
               summary->AbiVersion == BootResourcesAbiVersionV1 &&
               summary->ServiceVersion == BootResourcesServiceVersionV1 &&
               summary->Architecture == ArchitectureX64 &&
               summary->RegionCount == expectedRegionCount &&
               summary->ResourceMapIdentity == BootResourceMapIdentityUefiNormalizedV1 &&
               summary->TotalPhysicalBytes != 0 &&
               summary->UsablePhysicalBytes <= summary->TotalPhysicalBytes &&
               summary->Capabilities == BootResourceCapabilities &&
               summary->Reserved == 0;
    }

    private static bool ValidateRegion(
        GxManagedKernelBootResourceRegionV1* region,
        ref ulong totalPhysicalBytes,
        ref ulong usablePhysicalBytes)
    {
        ulong end;
        if (region == null ||
            region->Size != GxManagedKernelBootResourceRegionV1.ExpectedSize ||
            region->AbiVersion != BootResourcesAbiVersionV1 ||
            region->Length == 0 ||
            region->BaseAddress > ulong.MaxValue - region->Length ||
            region->Type == 0 || region->Type > BootResourceTypeUnknown ||
            region->Flags != ExpectedFlags(region->Type))
        {
            return false;
        }
        end = region->BaseAddress + region->Length;
        if (end <= region->BaseAddress ||
            (region->Flags & BootResourceFlagRamLike) != 0 &&
                totalPhysicalBytes > ulong.MaxValue - region->Length ||
            (region->Flags & BootResourceFlagUsable) != 0 &&
                usablePhysicalBytes > ulong.MaxValue - region->Length)
        {
            return false;
        }
        if ((region->Flags & BootResourceFlagRamLike) != 0)
        {
            totalPhysicalBytes += region->Length;
        }
        if ((region->Flags & BootResourceFlagUsable) != 0)
        {
            usablePhysicalBytes += region->Length;
        }
        return true;
    }

    private static bool ValidatePublication(
        GxManagedKernelBootResourcePublicationV1* publication,
        out nuint summaryAddress,
        out nuint descriptorAddress,
        out nuint descriptorByteLength)
    {
        ulong expectedByteLength;
        ulong maximumPointer = (ulong)nuint.MaxValue;
        GxManagedKernelBootResourceSummaryV1* summary;
        ulong totalPhysicalBytes = 0;
        ulong usablePhysicalBytes = 0;
        uint index;

        summaryAddress = 0;
        descriptorAddress = 0;
        descriptorByteLength = 0;
        if (publication == null ||
            publication->Size < GxManagedKernelBootResourcePublicationV1.ExpectedSize ||
            publication->AbiVersion != BootResourcesAbiVersionV1 ||
            publication->DescriptorCount == 0 ||
            publication->DescriptorCount > BootResourceMaxRegions ||
            publication->DescriptorSize != GxManagedKernelBootResourceRegionV1.ExpectedSize ||
            publication->Reserved != 0 ||
            publication->SummaryAddress == 0 ||
            publication->DescriptorAddress == 0 ||
            publication->SummaryAddress > maximumPointer ||
            publication->DescriptorAddress > maximumPointer ||
            publication->DescriptorByteLength > maximumPointer)
        {
            return false;
        }
        if ((ulong)publication->DescriptorCount >
                ulong.MaxValue / GxManagedKernelBootResourceRegionV1.ExpectedSize)
        {
            return false;
        }
        expectedByteLength = (ulong)publication->DescriptorCount *
            GxManagedKernelBootResourceRegionV1.ExpectedSize;
        if (publication->DescriptorByteLength != expectedByteLength)
        {
            return false;
        }
        summaryAddress = (nuint)publication->SummaryAddress;
        descriptorAddress = (nuint)publication->DescriptorAddress;
        descriptorByteLength = (nuint)publication->DescriptorByteLength;
        if (!IsRangeValid(summaryAddress,
                          (nuint)GxManagedKernelBootResourceSummaryV1.ExpectedSize) ||
            !IsRangeValid(descriptorAddress, descriptorByteLength))
        {
            return false;
        }
        summary = (GxManagedKernelBootResourceSummaryV1*)summaryAddress;
        if (!ValidateSummary(summary, publication->DescriptorCount))
        {
            return false;
        }
        for (index = 0; index != publication->DescriptorCount; ++index)
        {
            nuint offset = (nuint)index *
                (nuint)GxManagedKernelBootResourceRegionV1.ExpectedSize;
            GxManagedKernelBootResourceRegionV1* region =
                (GxManagedKernelBootResourceRegionV1*)(descriptorAddress + offset);
            if (!ValidateRegion(region, ref totalPhysicalBytes,
                                ref usablePhysicalBytes))
            {
                return false;
            }
        }
        if (totalPhysicalBytes != summary->TotalPhysicalBytes ||
            usablePhysicalBytes != summary->UsablePhysicalBytes)
        {
            return false;
        }
        return true;
    }

    internal static bool TryGetBootResourceSummary(
        out GxManagedKernelBootResourceSummaryV1 summary)
    {
        summary = default;
        if (s_initialized == 0 || s_bootResourcesPublished == 0 ||
            !IsRangeValid(s_bootResourceSummaryAddress,
                          (nuint)GxManagedKernelBootResourceSummaryV1.ExpectedSize))
        {
            return false;
        }
        summary = *(GxManagedKernelBootResourceSummaryV1*)s_bootResourceSummaryAddress;
        return true;
    }

    internal static bool TryGetBootResourceRegion(
        uint index, out GxManagedKernelBootResourceRegionV1 region)
    {
        region = default;
        if (s_initialized == 0 || s_bootResourcesPublished == 0 ||
            index >= s_bootResourceDescriptorCount)
        {
            return false;
        }
        nuint offset = (nuint)index *
            (nuint)GxManagedKernelBootResourceRegionV1.ExpectedSize;
        if (offset > nuint.MaxValue - s_bootResourceDescriptorAddress)
        {
            return false;
        }
        if (s_bootResourceDescriptorByteLength <
                (nuint)GxManagedKernelBootResourceRegionV1.ExpectedSize ||
            offset > s_bootResourceDescriptorByteLength -
                (nuint)GxManagedKernelBootResourceRegionV1.ExpectedSize)
        {
            return false;
        }
        region = *(GxManagedKernelBootResourceRegionV1*)
            (s_bootResourceDescriptorAddress + offset);
        return true;
    }

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

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallBootResources")]
    internal static uint InstallBootResources(uint requestedAbiVersion,
                                               nuint publicationAddress)
    {
        nuint summaryAddress;
        nuint descriptorAddress;
        nuint descriptorByteLength;
        if (requestedAbiVersion != BootResourcesAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (s_initialized == 0)
        {
            return NotInitialized;
        }
        if (s_bootResourcesPublished != 0)
        {
            return AlreadyInitialized;
        }
        if (publicationAddress == 0)
        {
            return InvalidArgument;
        }
        if (!IsRangeValid(publicationAddress,
                          (nuint)GxManagedKernelBootResourcePublicationV1.ExpectedSize))
        {
            return InvalidArgument;
        }
        if (!ValidatePublication(
                (GxManagedKernelBootResourcePublicationV1*)publicationAddress,
                out summaryAddress, out descriptorAddress,
                out descriptorByteLength))
        {
            return InvalidArgument;
        }
        s_bootResourceSummaryAddress = summaryAddress;
        s_bootResourceDescriptorAddress = descriptorAddress;
        s_bootResourceDescriptorCount =
            ((GxManagedKernelBootResourcePublicationV1*)publicationAddress)->DescriptorCount;
        s_bootResourceDescriptorByteLength = descriptorByteLength;
        s_bootResourcesPublished = 1;
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

    [UnmanagedCallersOnly(EntryPoint = "GxManagedQueryBootResources")]
    internal static uint QueryBootResources(uint requestedAbiVersion,
                                             nuint outputAddress,
                                             nuint outputCapacity)
    {
        GxManagedKernelBootResourceSummaryV1 summary;
        if (requestedAbiVersion != BootResourcesAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (s_initialized == 0 || s_bootResourcesPublished == 0)
        {
            return NotInitialized;
        }
        if (outputAddress == 0)
        {
            return InvalidArgument;
        }
        if (outputCapacity < GxManagedKernelBootResourceSummaryV1.ExpectedSize)
        {
            return BufferTooSmall;
        }
        if (!IsRangeValid(outputAddress, outputCapacity))
        {
            return InvalidArgument;
        }
        if (!TryGetBootResourceSummary(out summary))
        {
            return NotInitialized;
        }
        *(GxManagedKernelBootResourceSummaryV1*)outputAddress = summary;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedQueryMemoryRegion")]
    internal static uint QueryMemoryRegion(uint requestedAbiVersion, uint index,
                                           nuint outputAddress,
                                           nuint outputCapacity)
    {
        GxManagedKernelBootResourceRegionV1 region;
        if (requestedAbiVersion != BootResourcesAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (s_initialized == 0 || s_bootResourcesPublished == 0)
        {
            return NotInitialized;
        }
        if (index >= s_bootResourceDescriptorCount)
        {
            return OutOfRange;
        }
        if (outputAddress == 0)
        {
            return InvalidArgument;
        }
        if (outputCapacity < GxManagedKernelBootResourceRegionV1.ExpectedSize)
        {
            return BufferTooSmall;
        }
        if (!IsRangeValid(outputAddress, outputCapacity))
        {
            return InvalidArgument;
        }
        if (!TryGetBootResourceRegion(index, out region))
        {
            return OutOfRange;
        }
        *(GxManagedKernelBootResourceRegionV1*)outputAddress = region;
        return ManagedOk;
    }
}

public static unsafe class ManagedKernelEntry
{
    private static int s_bootstrapMarkerEmitted;

    private static ReadOnlySpan<byte> BootstrapMarker =>
        "GXOS_NET10:MANAGED_KERNEL_BOOTSTRAP_OK\r\n"u8;
    private static ReadOnlySpan<byte> BootResourcesMarker =>
        "GXOS_NET10:MANAGED_KERNEL_BOOT_RESOURCES_OK\r\n"u8;
    private static ReadOnlySpan<byte> MemoryRegionMarker =>
        "GXOS_NET10:MANAGED_KERNEL_MEMORY_REGION_OK\r\n"u8;

    private static void WriteSerial(GuideXBootInfo* bootInfo,
                                    ReadOnlySpan<byte> marker)
    {
        delegate* unmanaged<byte*, nuint, void> serialWrite =
            (delegate* unmanaged<byte*, nuint, void>)(nuint)bootInfo->SerialWrite;
        fixed (byte* markerAddress = marker)
        {
            serialWrite(markerAddress, (nuint)marker.Length);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "ManagedMain")]
    public static int ManagedMain(nint bootInfoAddress)
    {
        GuideXBootInfo* bootInfo;
        GxManagedKernelBootResourceSummaryV1 summary;
        GxManagedKernelBootResourceRegionV1 region;

        if (!ManagedKernelLayout.IsValid() || bootInfoAddress == 0)
        {
            return -1;
        }

        bootInfo = (GuideXBootInfo*)bootInfoAddress;
        if (bootInfo->Magic != GuideXBootInfo.ExpectedMagic ||
            bootInfo->Version != GuideXBootInfo.CurrentVersion ||
            bootInfo->Size < GuideXBootInfo.MinimumSize ||
            bootInfo->Architecture != GuideXBootInfo.ArchitectureX64 ||
            bootInfo->SerialWrite == 0)
        {
            return -2;
        }

        if (s_bootstrapMarkerEmitted == 0)
        {
            WriteSerial(bootInfo, BootstrapMarker);
            s_bootstrapMarkerEmitted = 1;
        }

        if (!ManagedKernelContract.TryGetBootResourceSummary(out summary))
        {
            return 0;
        }

        if (summary.Size != GxManagedKernelBootResourceSummaryV1.ExpectedSize ||
            summary.AbiVersion != 1 ||
            summary.ServiceVersion != 1 ||
            summary.Architecture != GuideXBootInfo.ArchitectureX64 ||
            summary.RegionCount == 0 ||
            summary.RegionCount > 2048 ||
            summary.ResourceMapIdentity !=
                GxManagedKernelBootResourceSummaryV1.ResourceMapIdentityUefiNormalizedV1 ||
            summary.TotalPhysicalBytes == 0 ||
            summary.UsablePhysicalBytes > summary.TotalPhysicalBytes ||
            summary.Capabilities !=
                (GxManagedKernelBootResourceSummaryV1.CapabilitySummary |
                 GxManagedKernelBootResourceSummaryV1.CapabilityRegions |
                 GxManagedKernelBootResourceSummaryV1.CapabilityTotals) ||
            summary.Reserved != 0 ||
            !ManagedKernelContract.TryGetBootResourceRegion(0, out region) ||
            region.Size != GxManagedKernelBootResourceRegionV1.ExpectedSize ||
            region.AbiVersion != 1 || region.Length == 0 ||
            region.BaseAddress > ulong.MaxValue - region.Length ||
            region.Type == 0 || region.Type > 16)
        {
            return -3;
        }

        WriteSerial(bootInfo, BootResourcesMarker);
        WriteSerial(bootInfo, MemoryRegionMarker);
        return 0;
    }

    // The freestanding loader calls the exported ManagedMain entry directly.
    public static int Main() => 0;
}

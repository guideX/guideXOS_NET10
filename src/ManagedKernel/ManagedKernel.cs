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

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelHostServicesV1
{
    internal const uint ExpectedSize = 56;
    internal const ulong CapabilityAbi = 1UL << 0;
    internal const ulong CapabilityLogUtf8 = 1UL << 1;
    internal const ulong CapabilityMonotonicTime = 1UL << 2;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal ulong LogUtf8Address;
    internal ulong MonotonicTimeAddress;
    internal ulong Reserved0;
    internal ulong Reserved1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelEntropyServicesV1
{
    internal const uint ExpectedSize = 48;
    internal const ulong CapabilityHardware = 1UL << 0;
    internal const ulong CapabilityRdrand = 1UL << 1;
    internal const ulong CapabilityRdseed = 1UL << 2;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal ulong FillAddress;
    internal uint MaxBytesPerFill;
    internal uint RetryCount;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMonotonicTimeV1
{
    internal const uint ExpectedSize = 40;
    internal const ulong FlagNormalizedFromStart = 1UL << 0;

    internal uint Size;
    internal uint AbiVersion;
    internal ulong Ticks;
    internal ulong FrequencyHz;
    internal ulong Flags;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMemoryServicesV1
{
    internal const uint ExpectedSize = 72;
    internal const ulong CapabilityAbi = 1UL << 0;
    internal const ulong CapabilityAllocatePages = 1UL << 1;
    internal const ulong CapabilityReleasePages = 1UL << 2;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal ulong PageSize;
    internal ulong AllocatePagesAddress;
    internal ulong ReleasePagesAddress;
    internal uint MaxPagesPerAllocation;
    internal uint MaxLiveAllocations;
    internal ulong MaxTotalPages;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMemoryAllocationV1
{
    internal const uint ExpectedSize = 56;

    internal uint Size;
    internal uint AbiVersion;
    internal ulong AllocationId;
    internal ulong VirtualAddress;
    internal ulong ByteLength;
    internal ulong PageCount;
    internal ulong PageSize;
    internal uint Flags;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMemoryReleaseV1
{
    internal const uint ExpectedSize = 56;

    internal uint Size;
    internal uint AbiVersion;
    internal ulong AllocationId;
    internal ulong VirtualAddress;
    internal ulong ByteLength;
    internal ulong PageCount;
    internal ulong PageSize;
    internal uint Flags;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDeviceInventorySummaryV1
{
    internal const uint ExpectedSize = 40;
    internal const ulong CapabilitySummary = 1UL << 0;
    internal const ulong CapabilityDevices = 1UL << 1;
    internal const ulong CapabilityImmutableBootSnapshot = 1UL << 2;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal uint DeviceCount;
    internal uint ResourceCount;
    internal ulong Capabilities;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDeviceV1
{
    internal const uint ExpectedSize = 48;
    internal const uint DeviceKindPci = 1;
    internal const uint FlagPciMultifunction = 1U << 0;

    internal uint Size;
    internal uint AbiVersion;
    internal uint DeviceKind;
    internal uint Flags;
    internal ushort Segment;
    internal byte Bus;
    internal byte Device;
    internal byte Function;
    internal byte ReservedLocation;
    internal ushort VendorId;
    internal ushort DeviceId;
    internal byte RevisionId;
    internal byte ClassCode;
    internal byte Subclass;
    internal byte ProgrammingInterface;
    internal byte HeaderType;
    internal byte ReservedClass;
    internal uint ResourceStartIndex;
    internal uint ResourceCount;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDeviceInventoryPublicationV1
{
    internal const uint ExpectedSize = 48;

    internal uint Size;
    internal uint AbiVersion;
    internal nuint SummaryAddress;
    internal nuint DescriptorAddress;
    internal uint DescriptorCount;
    internal uint DescriptorSize;
    internal nuint DescriptorByteLength;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDeviceResourceSummaryV1
{
    internal const uint ExpectedSize = 40;
    internal const ulong CapabilitySummary = 1UL << 0;
    internal const ulong CapabilityDescriptors = 1UL << 1;
    internal const ulong CapabilityImmutablePublication = 1UL << 2;
    internal const ulong CapabilityClaimPolicy = 1UL << 3;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal uint ResourceCount;
    internal uint MaxClaims;
    internal ulong Capabilities;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDeviceResourceV1
{
    internal const uint ExpectedSize = 80;
    internal const uint ResourceTypeIoPort = 1;
    internal const uint ResourceTypeMmio = 2;
    internal const uint ResourceTypePlatformMemory = 3;
    internal const uint ResourceTypeInterrupt = 4;
    internal const uint FlagReadable = 1U << 0;
    internal const uint FlagWritable = 1U << 1;
    internal const uint FlagIoPort = 1U << 2;
    internal const uint FlagMemory = 1U << 3;
    internal const uint FlagPrefetchable = 1U << 4;
    internal const uint FlagAddress64 = 1U << 5;
    internal const uint FlagCacheUncached = 1U << 6;
    internal const uint FlagPlatform = 1U << 7;
    internal const uint FlagPciAssigned = 1U << 8;
    internal const uint DeviceKindPlatformSerial = 2;
    internal const uint DeviceKindPlatformKeyboard = 3;

    internal uint Size;
    internal uint AbiVersion;
    internal ulong ResourceId;
    internal uint OwnerDeviceKind;
    internal uint OwnerDeviceId;
    internal ushort OwnerSegment;
    internal byte OwnerBus;
    internal byte OwnerDevice;
    internal byte OwnerFunction;
    internal byte ReservedLocation;
    internal ushort ResourceIndex;
    internal uint ResourceType;
    internal uint Flags;
    internal ulong PhysicalBase;
    internal ulong Length;
    internal ulong Alignment;
    internal ulong Reserved0;
    internal ulong Reserved1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDeviceResourcePublicationV1
{
    internal const uint ExpectedSize = 48;

    internal uint Size;
    internal uint AbiVersion;
    internal nuint SummaryAddress;
    internal nuint DescriptorAddress;
    internal uint DescriptorCount;
    internal uint DescriptorSize;
    internal nuint DescriptorByteLength;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelPciServicesV1
{
    internal const uint ExpectedSize = 56;
    internal const ulong CapabilityConfigRead = 1UL << 0;
    internal const ulong CapabilityCommandRmw = 1UL << 4;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal ulong ConfigReadAddress;
    internal ulong Reserved0;
    internal ulong Reserved1;
    internal ulong ConfigCommandAddress;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelPciReadResultV1
{
    internal const uint ExpectedSize = 32;

    internal uint Size;
    internal uint AbiVersion;
    internal uint Width;
    internal uint Reserved0;
    internal ulong Value;
    internal ulong Reserved1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelPciCommandResultV1
{
    internal const uint ExpectedSize = 24;

    internal uint Size;
    internal uint AbiVersion;
    internal uint OriginalCommand;
    internal uint RequestedBits;
    internal uint ResultingCommand;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMmioServicesV1
{
    internal const uint ExpectedSize = 96;
    internal const ulong CapabilityClaim = 1UL << 0;
    internal const ulong CapabilityMap = 1UL << 1;
    internal const ulong CapabilityUnmap = 1UL << 2;
    internal const ulong CapabilityRead = 1UL << 3;
    internal const ulong CapabilityUncacheable = 1UL << 4;
    internal const ulong CapabilityWrite = 1UL << 5;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal ulong ClaimAddress;
    internal ulong ReleaseAddress;
    internal ulong MapAddress;
    internal ulong UnmapAddress;
    internal ulong ReadAddress;
    internal uint MaxClaims;
    internal uint MaxMappings;
    internal ulong WindowBase;
    internal ulong WindowLength;
    internal ulong WriteAddress;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMmioClaimResultV1
{
    internal const uint ExpectedSize = 24;
    internal uint Size;
    internal uint AbiVersion;
    internal ulong Handle;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMmioMappingResultV1
{
    internal const uint ExpectedSize = 48;
    internal uint Size;
    internal uint AbiVersion;
    internal ulong Handle;
    internal ulong ResourceId;
    internal ulong Offset;
    internal ulong Length;
    internal uint Access;
    internal uint Reserved0;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelMmioReadResultV1
{
    internal const uint ExpectedSize = 32;
    internal uint Size;
    internal uint AbiVersion;
    internal uint Width;
    internal uint Reserved0;
    internal ulong Value;
    internal ulong Reserved1;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDmaAllocationResultV1
{
    internal const uint ExpectedSize = 56;

    internal uint Size;
    internal uint AbiVersion;
    internal ulong Handle;
    internal ulong BusAddress;
    internal ulong ByteLength;
    internal ulong PageCount;
    internal ulong Alignment;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct GxManagedKernelDmaServicesV1
{
    internal const uint ExpectedSize = 104;
    internal const ulong CapabilityAllocate = 1UL << 0;
    internal const ulong CapabilityRelease = 1UL << 1;
    internal const ulong CapabilityRead = 1UL << 2;
    internal const ulong CapabilityWrite = 1UL << 3;
    internal const ulong CapabilityRetain = 1UL << 4;
    internal const ulong CapabilityReleaseReference = 1UL << 5;

    internal uint Size;
    internal uint AbiVersion;
    internal uint ServiceVersion;
    internal uint Architecture;
    internal ulong Capabilities;
    internal ulong AllocateAddress;
    internal ulong ReleaseAddress;
    internal ulong ReadAddress;
    internal ulong WriteAddress;
    internal ulong RetainAddress;
    internal ulong ReleaseReferenceAddress;
    internal uint MaxAllocations;
    internal uint MaxPagesPerAllocation;
    internal ulong MaxTotalPages;
    internal ulong MaxBusAddress;
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
               sizeof(GxManagedKernelHostServicesV1) == 56 &&
               sizeof(GxManagedKernelEntropyServicesV1) == 48 &&
               sizeof(GxManagedKernelMonotonicTimeV1) == 40 &&
               sizeof(GxManagedKernelMemoryServicesV1) == 72 &&
               sizeof(GxManagedKernelMemoryAllocationV1) == 56 &&
               sizeof(GxManagedKernelMemoryReleaseV1) == 56 &&
               sizeof(GxManagedKernelDeviceInventorySummaryV1) == 40 &&
               sizeof(GxManagedKernelDeviceV1) == 48 &&
               sizeof(GxManagedKernelDeviceInventoryPublicationV1) == 48 &&
               sizeof(GxManagedKernelDeviceResourceSummaryV1) == 40 &&
               sizeof(GxManagedKernelDeviceResourceV1) == 80 &&
               sizeof(GxManagedKernelDeviceResourcePublicationV1) == 48 &&
               sizeof(GxManagedKernelPciServicesV1) == 56 &&
               sizeof(GxManagedKernelPciReadResultV1) == 32 &&
               sizeof(GxManagedKernelPciCommandResultV1) == 24 &&
               sizeof(GxManagedKernelMmioServicesV1) == 96 &&
               sizeof(GxManagedKernelMmioClaimResultV1) == 24 &&
               sizeof(GxManagedKernelMmioMappingResultV1) == 48 &&
               sizeof(GxManagedKernelMmioReadResultV1) == 32 &&
               sizeof(GxManagedKernelDmaAllocationResultV1) == 56 &&
               sizeof(GxManagedKernelDmaServicesV1) == 104 &&
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
               Marshal.OffsetOf<GxManagedKernelBootResourcePublicationV1>(nameof(GxManagedKernelBootResourcePublicationV1.Reserved)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.ServiceVersion)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.Architecture)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.LogUtf8Address)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.MonotonicTimeAddress)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.Reserved0)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelHostServicesV1>(nameof(GxManagedKernelHostServicesV1.Reserved1)).ToInt32() == 48 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.ServiceVersion)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.Architecture)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.FillAddress)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.MaxBytesPerFill)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.RetryCount)).ToInt32() == 36 &&
               Marshal.OffsetOf<GxManagedKernelEntropyServicesV1>(nameof(GxManagedKernelEntropyServicesV1.Reserved)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelMonotonicTimeV1>(nameof(GxManagedKernelMonotonicTimeV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelMonotonicTimeV1>(nameof(GxManagedKernelMonotonicTimeV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelMonotonicTimeV1>(nameof(GxManagedKernelMonotonicTimeV1.Ticks)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelMonotonicTimeV1>(nameof(GxManagedKernelMonotonicTimeV1.FrequencyHz)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelMonotonicTimeV1>(nameof(GxManagedKernelMonotonicTimeV1.Flags)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelMonotonicTimeV1>(nameof(GxManagedKernelMonotonicTimeV1.Reserved)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.PageSize)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.AllocatePagesAddress)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.ReleasePagesAddress)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.MaxPagesPerAllocation)).ToInt32() == 48 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.MaxLiveAllocations)).ToInt32() == 52 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.MaxTotalPages)).ToInt32() == 56 &&
               Marshal.OffsetOf<GxManagedKernelMemoryServicesV1>(nameof(GxManagedKernelMemoryServicesV1.Reserved)).ToInt32() == 64 &&
               Marshal.OffsetOf<GxManagedKernelMemoryAllocationV1>(nameof(GxManagedKernelMemoryAllocationV1.AllocationId)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelMemoryAllocationV1>(nameof(GxManagedKernelMemoryAllocationV1.VirtualAddress)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelMemoryAllocationV1>(nameof(GxManagedKernelMemoryAllocationV1.ByteLength)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelMemoryAllocationV1>(nameof(GxManagedKernelMemoryAllocationV1.PageCount)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelMemoryAllocationV1>(nameof(GxManagedKernelMemoryAllocationV1.PageSize)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelMemoryAllocationV1>(nameof(GxManagedKernelMemoryAllocationV1.Flags)).ToInt32() == 48 &&
               Marshal.OffsetOf<GxManagedKernelMemoryReleaseV1>(nameof(GxManagedKernelMemoryReleaseV1.AllocationId)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelMemoryReleaseV1>(nameof(GxManagedKernelMemoryReleaseV1.VirtualAddress)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelMemoryReleaseV1>(nameof(GxManagedKernelMemoryReleaseV1.ByteLength)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelMemoryReleaseV1>(nameof(GxManagedKernelMemoryReleaseV1.PageCount)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelMemoryReleaseV1>(nameof(GxManagedKernelMemoryReleaseV1.PageSize)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelMemoryReleaseV1>(nameof(GxManagedKernelMemoryReleaseV1.Flags)).ToInt32() == 48 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventorySummaryV1>(nameof(GxManagedKernelDeviceInventorySummaryV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventorySummaryV1>(nameof(GxManagedKernelDeviceInventorySummaryV1.DeviceCount)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventorySummaryV1>(nameof(GxManagedKernelDeviceInventorySummaryV1.ResourceCount)).ToInt32() == 20 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventorySummaryV1>(nameof(GxManagedKernelDeviceInventorySummaryV1.Capabilities)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventorySummaryV1>(nameof(GxManagedKernelDeviceInventorySummaryV1.Reserved)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelDeviceV1>(nameof(GxManagedKernelDeviceV1.DeviceKind)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelDeviceV1>(nameof(GxManagedKernelDeviceV1.Segment)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelDeviceV1>(nameof(GxManagedKernelDeviceV1.VendorId)).ToInt32() == 22 &&
               Marshal.OffsetOf<GxManagedKernelDeviceV1>(nameof(GxManagedKernelDeviceV1.ClassCode)).ToInt32() == 27 &&
               Marshal.OffsetOf<GxManagedKernelDeviceV1>(nameof(GxManagedKernelDeviceV1.ResourceStartIndex)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelDeviceV1>(nameof(GxManagedKernelDeviceV1.ResourceCount)).ToInt32() == 36 &&
               Marshal.OffsetOf<GxManagedKernelDeviceV1>(nameof(GxManagedKernelDeviceV1.Reserved)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventoryPublicationV1>(nameof(GxManagedKernelDeviceInventoryPublicationV1.SummaryAddress)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventoryPublicationV1>(nameof(GxManagedKernelDeviceInventoryPublicationV1.DescriptorAddress)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventoryPublicationV1>(nameof(GxManagedKernelDeviceInventoryPublicationV1.DescriptorCount)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventoryPublicationV1>(nameof(GxManagedKernelDeviceInventoryPublicationV1.DescriptorSize)).ToInt32() == 28 &&
               Marshal.OffsetOf<GxManagedKernelDeviceInventoryPublicationV1>(nameof(GxManagedKernelDeviceInventoryPublicationV1.DescriptorByteLength)).ToInt32() == 32 &&
                Marshal.OffsetOf<GxManagedKernelDeviceInventoryPublicationV1>(nameof(GxManagedKernelDeviceInventoryPublicationV1.Reserved)).ToInt32() == 40 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceSummaryV1>(nameof(GxManagedKernelDeviceResourceSummaryV1.ResourceCount)).ToInt32() == 16 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceSummaryV1>(nameof(GxManagedKernelDeviceResourceSummaryV1.MaxClaims)).ToInt32() == 20 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceV1>(nameof(GxManagedKernelDeviceResourceV1.ResourceId)).ToInt32() == 8 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceV1>(nameof(GxManagedKernelDeviceResourceV1.OwnerSegment)).ToInt32() == 24 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceV1>(nameof(GxManagedKernelDeviceResourceV1.ResourceIndex)).ToInt32() == 30 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceV1>(nameof(GxManagedKernelDeviceResourceV1.ResourceType)).ToInt32() == 32 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceV1>(nameof(GxManagedKernelDeviceResourceV1.PhysicalBase)).ToInt32() == 40 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceV1>(nameof(GxManagedKernelDeviceResourceV1.Length)).ToInt32() == 48 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourceV1>(nameof(GxManagedKernelDeviceResourceV1.Alignment)).ToInt32() == 56 &&
                Marshal.OffsetOf<GxManagedKernelDeviceResourcePublicationV1>(nameof(GxManagedKernelDeviceResourcePublicationV1.SummaryAddress)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.ServiceVersion)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.Architecture)).ToInt32() == 12 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.Capabilities)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.ConfigReadAddress)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.Reserved0)).ToInt32() == 32 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.Reserved1)).ToInt32() == 40 &&
               Marshal.OffsetOf<GxManagedKernelPciServicesV1>(nameof(GxManagedKernelPciServicesV1.ConfigCommandAddress)).ToInt32() == 48 &&
               Marshal.OffsetOf<GxManagedKernelPciReadResultV1>(nameof(GxManagedKernelPciReadResultV1.Size)).ToInt32() == 0 &&
               Marshal.OffsetOf<GxManagedKernelPciReadResultV1>(nameof(GxManagedKernelPciReadResultV1.AbiVersion)).ToInt32() == 4 &&
               Marshal.OffsetOf<GxManagedKernelPciReadResultV1>(nameof(GxManagedKernelPciReadResultV1.Width)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelPciReadResultV1>(nameof(GxManagedKernelPciReadResultV1.Value)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelPciReadResultV1>(nameof(GxManagedKernelPciReadResultV1.Reserved1)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelMmioServicesV1>(nameof(GxManagedKernelMmioServicesV1.ClaimAddress)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelMmioServicesV1>(nameof(GxManagedKernelMmioServicesV1.ReadAddress)).ToInt32() == 56 &&
               Marshal.OffsetOf<GxManagedKernelMmioServicesV1>(nameof(GxManagedKernelMmioServicesV1.WindowBase)).ToInt32() == 72 &&
               Marshal.OffsetOf<GxManagedKernelMmioServicesV1>(nameof(GxManagedKernelMmioServicesV1.WriteAddress)).ToInt32() == 88 &&
               Marshal.OffsetOf<GxManagedKernelMmioClaimResultV1>(nameof(GxManagedKernelMmioClaimResultV1.Handle)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelMmioMappingResultV1>(nameof(GxManagedKernelMmioMappingResultV1.Offset)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelMmioReadResultV1>(nameof(GxManagedKernelMmioReadResultV1.Value)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelDmaAllocationResultV1>(nameof(GxManagedKernelDmaAllocationResultV1.Handle)).ToInt32() == 8 &&
               Marshal.OffsetOf<GxManagedKernelDmaAllocationResultV1>(nameof(GxManagedKernelDmaAllocationResultV1.BusAddress)).ToInt32() == 16 &&
               Marshal.OffsetOf<GxManagedKernelDmaServicesV1>(nameof(GxManagedKernelDmaServicesV1.AllocateAddress)).ToInt32() == 24 &&
               Marshal.OffsetOf<GxManagedKernelDmaServicesV1>(nameof(GxManagedKernelDmaServicesV1.MaxAllocations)).ToInt32() == 72 &&
               Marshal.OffsetOf<GxManagedKernelDmaServicesV1>(nameof(GxManagedKernelDmaServicesV1.MaxBusAddress)).ToInt32() == 88 &&
                ManagedKernelSerialLayout.IsValid() &&
                ManagedInterruptLayout.IsValid();
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
    internal const uint InvalidState = 7;
    internal const uint ResourceExhausted = 8;
    internal const uint NotFound = 9;
    internal const uint OwnershipMismatch = 10;

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

    private const uint HostServicesAbiVersionV1 = 1;
    private const uint HostServicesServiceVersionV1 = 1;
    private const ulong HostServicesKnownCapabilities =
        GxManagedKernelHostServicesV1.CapabilityAbi |
        GxManagedKernelHostServicesV1.CapabilityLogUtf8 |
        GxManagedKernelHostServicesV1.CapabilityMonotonicTime;
    private const ulong RequiredHostServicesCapabilities =
        GxManagedKernelHostServicesV1.CapabilityAbi |
        GxManagedKernelHostServicesV1.CapabilityLogUtf8;
    private const uint EntropyServicesAbiVersionV1 = 1;
    private const uint EntropyServicesServiceVersionV1 = 1;
    private const ulong EntropyServicesKnownCapabilities =
        GxManagedKernelEntropyServicesV1.CapabilityHardware |
        GxManagedKernelEntropyServicesV1.CapabilityRdrand |
        GxManagedKernelEntropyServicesV1.CapabilityRdseed;
    private const ulong MonotonicTimeKnownFlags =
        GxManagedKernelMonotonicTimeV1.FlagNormalizedFromStart;
    private const uint MemoryServicesAbiVersionV1 = 1;
    private const uint MemoryServicesServiceVersionV1 = 1;
    private const ulong MemoryServicesKnownCapabilities =
        GxManagedKernelMemoryServicesV1.CapabilityAbi |
        GxManagedKernelMemoryServicesV1.CapabilityAllocatePages |
        GxManagedKernelMemoryServicesV1.CapabilityReleasePages;
    private const ulong MemoryServicesRequiredCapabilities =
        MemoryServicesKnownCapabilities;
    private const uint DeviceInventoryAbiVersionV1 = 1;
    private const uint DeviceInventoryServiceVersionV1 = 1;
    private const uint DeviceInventoryMaxDevices = 256;
    private const ulong DeviceInventoryCapabilities =
        GxManagedKernelDeviceInventorySummaryV1.CapabilitySummary |
        GxManagedKernelDeviceInventorySummaryV1.CapabilityDevices |
        GxManagedKernelDeviceInventorySummaryV1.CapabilityImmutableBootSnapshot;
    private const uint DeviceResourceAbiVersionV1 = 1;
    private const uint DeviceResourceServiceVersionV1 = 1;
    private const uint DeviceResourceMaxDescriptors = 64;
    private const uint DeviceResourceMaxClaims = 16;
    private const ulong DeviceResourceCapabilities =
        GxManagedKernelDeviceResourceSummaryV1.CapabilitySummary |
        GxManagedKernelDeviceResourceSummaryV1.CapabilityDescriptors |
        GxManagedKernelDeviceResourceSummaryV1.CapabilityImmutablePublication |
        GxManagedKernelDeviceResourceSummaryV1.CapabilityClaimPolicy;
    private const uint PciServicesAbiVersionV1 = 1;
    private const uint PciServicesServiceVersionV1 = 1;
    private const ulong PciServicesKnownCapabilities =
        GxManagedKernelPciServicesV1.CapabilityConfigRead |
        GxManagedKernelPciServicesV1.CapabilityCommandRmw;
    private const ulong PciServicesRequiredCapabilities =
        GxManagedKernelPciServicesV1.CapabilityConfigRead |
        GxManagedKernelPciServicesV1.CapabilityCommandRmw;
    private const uint MmioServicesAbiVersionV1 = 1;
    private const uint MmioServicesServiceVersionV1 = 1;
    private const ulong MmioServicesKnownCapabilities =
        GxManagedKernelMmioServicesV1.CapabilityClaim |
        GxManagedKernelMmioServicesV1.CapabilityMap |
        GxManagedKernelMmioServicesV1.CapabilityUnmap |
        GxManagedKernelMmioServicesV1.CapabilityRead |
        GxManagedKernelMmioServicesV1.CapabilityUncacheable |
        GxManagedKernelMmioServicesV1.CapabilityWrite;
    private const uint DmaServicesAbiVersionV1 = 1;
    private const uint DmaServicesServiceVersionV1 = 1;
    private const ulong DmaServicesKnownCapabilities =
        GxManagedKernelDmaServicesV1.CapabilityAllocate |
        GxManagedKernelDmaServicesV1.CapabilityRelease |
        GxManagedKernelDmaServicesV1.CapabilityRead |
        GxManagedKernelDmaServicesV1.CapabilityWrite |
        GxManagedKernelDmaServicesV1.CapabilityRetain |
        GxManagedKernelDmaServicesV1.CapabilityReleaseReference;
    internal const ulong MemoryPageSize = 4096;
    internal const uint MemoryMaxPagesPerAllocation = 256;
    internal const uint MemoryMaxLiveAllocations = 16;
    internal const ulong MemoryMaxTotalPages = 1024;

    private enum LifecycleState
    {
        BootstrapAvailable = 0,
        Initialized = 1,
        EnvironmentInstalling = 2,
        Ready = 3,
        Started = 4
    }

    private static int s_lifecycleState = (int)LifecycleState.BootstrapAvailable;
    private static int s_bootResourcesPublished;
    private static nuint s_bootResourceSummaryAddress;
    private static nuint s_bootResourceDescriptorAddress;
    private static uint s_bootResourceDescriptorCount;
    private static nuint s_bootResourceDescriptorByteLength;
    private static GxManagedKernelBootResourceSummaryV1 s_bootResourceSummarySnapshot;
    private static int s_hostServicesInstalled;
    private static ulong s_hostCapabilities;
    private static nuint s_hostLogUtf8Address;
    private static nuint s_hostMonotonicTimeAddress;
    private static int s_entropyServicesInstalled;
    private static ulong s_entropyCapabilities;
    private static nuint s_entropyFillAddress;
    private static uint s_entropyMaxBytesPerFill;
    private static uint s_entropyRetryCount;
    private static ManagedEntropyService? s_entropyService;
    private static ManagedSecureRandom? s_secureRandom;
    private static int s_memoryServicesInstalled;
    private static ulong s_memoryCapabilities;
    private static ulong s_memoryPageSize;
    private static uint s_memoryMaxPagesPerAllocation;
    private static uint s_memoryMaxLiveAllocations;
    private static ulong s_memoryMaxTotalPages;
    private static nuint s_memoryAllocatePagesAddress;
    private static nuint s_memoryReleasePagesAddress;
    private static int s_deviceInventoryInstalled;
    private static ManagedDeviceInventory? s_deviceInventory;
    private static int s_deviceResourcesInstalled;
    private static int s_phase12Run;
    private static int s_phase12TeardownRun;
    private static int s_pciServicesInstalled;
    private static ulong s_pciCapabilities;
    private static nuint s_pciConfigReadAddress;
    private static nuint s_pciConfigCommandAddress;
    private static int s_mmioServicesInstalled;
    private static ulong s_mmioCapabilities;
    private static nuint s_mmioClaimAddress;
    private static nuint s_mmioReleaseAddress;
    private static nuint s_mmioMapAddress;
    private static nuint s_mmioUnmapAddress;
    private static nuint s_mmioReadAddress;
    private static nuint s_mmioWriteAddress;
    private static uint s_mmioMaxClaims;
    private static int s_phase13Run;
    private static int s_phase13TeardownRun;
    private static int s_phase14Run;
    private static int s_phase14TeardownRun;
    private static int s_phase35Mode;
    private static int s_phase39Mode;
    private static int s_phase40Mode;
    private static int s_phase41Mode;
    private static ManagedE1000Driver? s_phase14Driver;
    private static int s_dmaServicesInstalled;
    private static ulong s_dmaCapabilities;
    private static nuint s_dmaAllocateAddress;
    private static nuint s_dmaReleaseAddress;
    private static nuint s_dmaReadAddress;
    private static nuint s_dmaWriteAddress;
    private static nuint s_dmaRetainAddress;
    private static nuint s_dmaReleaseReferenceAddress;
    private static uint s_dmaMaxAllocations;
    private static uint s_dmaMaxPagesPerAllocation;
    private static ulong s_dmaMaxTotalPages;
    private static ulong s_dmaMaxBusAddress;
    private static int s_pciReadBeforeInstallNegativeLogged;
    private static int s_phase7Run;
    private static int s_phase7AccountingRun;
    private static ManagedDriverRegistry? s_driverRegistry;
    private static int s_phase4Run;
    private static int s_phase5Run;
    private static int s_phase6Run;

    internal static bool IsStarted =>
        s_lifecycleState == (int)LifecycleState.Started;
    internal static bool MemoryServicesInstalled => s_memoryServicesInstalled != 0;
    internal static nuint MemoryAllocatePagesAddress => s_memoryAllocatePagesAddress;
    internal static nuint MemoryReleasePagesAddress => s_memoryReleasePagesAddress;
    internal static bool DeviceInventoryInstalled => s_deviceInventoryInstalled != 0;
    internal static bool DeviceResourcesInstalled => s_deviceResourcesInstalled != 0;
    internal static bool HostServicesInstalled => s_hostServicesInstalled != 0;
    internal static bool EntropyServicesInstalled => s_entropyServicesInstalled != 0;
    internal static ulong EntropyCapabilities => s_entropyCapabilities;
    internal static nuint EntropyFillAddress => s_entropyFillAddress;
    internal static uint EntropyMaxBytesPerFill => s_entropyMaxBytesPerFill;
    internal static uint EntropyRetryCount => s_entropyRetryCount;
    internal static ManagedSecureRandom? SecureRandom => s_secureRandom;
    internal static ManagedEntropyService? EntropyService => s_entropyService;

    /* The no-hardware path is intentionally allocation-free during Phase 10,
       but a device-backed provider may be installed later.  Keep that late
       service in the same static roots used by the hardware path so a
       collection cannot reclaim the provider graph while a DMA queue is live. */
    internal static bool TryEnsureEntropyService()
    {
        if (s_entropyServicesInstalled == 0)
            return false;
        if (s_entropyService != null && s_secureRandom != null)
            return true;

        s_entropyService = new ManagedEntropyService(
            s_entropyFillAddress, s_entropyCapabilities,
            s_entropyMaxBytesPerFill);
        s_secureRandom = new ManagedSecureRandom(s_entropyService);
        return true;
    }

    internal static bool PciServicesInstalled => s_pciServicesInstalled != 0;
    internal static nuint PciConfigReadAddress => s_pciConfigReadAddress;
    internal static ManagedDeviceInventory? OperationalDeviceInventory =>
        s_deviceInventory;
    internal static ManagedDriverRegistry? OperationalDriverRegistry =>
        s_driverRegistry;

    private static bool IsInitialized =>
        s_lifecycleState != (int)LifecycleState.BootstrapAvailable;

    internal static bool IsRangeValid(nuint address, nuint length)
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
        if (!IsInitialized || s_bootResourcesPublished == 0 ||
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
        if (!IsInitialized || s_bootResourcesPublished == 0 ||
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

        if (s_lifecycleState != (int)LifecycleState.BootstrapAvailable)
        {
            return AlreadyInitialized;
        }

        s_lifecycleState = (int)LifecycleState.Initialized;
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
        if (!IsInitialized)
        {
            return NotInitialized;
        }
        if (s_bootResourcesPublished != 0 ||
            s_lifecycleState != (int)LifecycleState.Initialized)
        {
            return s_bootResourcesPublished != 0 ? AlreadyInitialized : InvalidState;
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
        s_bootResourceSummarySnapshot =
            *(GxManagedKernelBootResourceSummaryV1*)summaryAddress;
        s_bootResourcesPublished = 1;
        s_lifecycleState = (int)LifecycleState.EnvironmentInstalling;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallMemoryServices")]
    internal static uint InstallMemoryServices(uint requestedAbiVersion,
                                                nuint memoryServicesAddress)
    {
        GxManagedKernelMemoryServicesV1 services;
        if (requestedAbiVersion != MemoryServicesAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (!IsInitialized)
        {
            return NotInitialized;
        }
        if (s_memoryServicesInstalled != 0)
        {
            return AlreadyInitialized;
        }
        if (s_lifecycleState != (int)LifecycleState.EnvironmentInstalling ||
            memoryServicesAddress == 0 ||
            !IsRangeValid(memoryServicesAddress,
                          (nuint)GxManagedKernelMemoryServicesV1.ExpectedSize))
        {
            return s_lifecycleState != (int)LifecycleState.EnvironmentInstalling
                ? InvalidState : InvalidArgument;
        }
        services = *(GxManagedKernelMemoryServicesV1*)memoryServicesAddress;
        if (services.Size != GxManagedKernelMemoryServicesV1.ExpectedSize ||
            services.AbiVersion != MemoryServicesAbiVersionV1 ||
            services.ServiceVersion != MemoryServicesServiceVersionV1 ||
            services.Architecture != ArchitectureX64 ||
            (services.Capabilities & ~MemoryServicesKnownCapabilities) != 0 ||
            (services.Capabilities & MemoryServicesRequiredCapabilities) !=
                MemoryServicesRequiredCapabilities ||
            services.PageSize != MemoryPageSize ||
            services.MaxPagesPerAllocation != MemoryMaxPagesPerAllocation ||
            services.MaxLiveAllocations != MemoryMaxLiveAllocations ||
            services.MaxTotalPages != MemoryMaxTotalPages ||
            services.AllocatePagesAddress == 0 ||
            services.ReleasePagesAddress == 0 || services.Reserved != 0)
        {
            return InvalidArgument;
        }
        s_memoryCapabilities = services.Capabilities;
        s_memoryPageSize = services.PageSize;
        s_memoryMaxPagesPerAllocation = services.MaxPagesPerAllocation;
        s_memoryMaxLiveAllocations = services.MaxLiveAllocations;
        s_memoryMaxTotalPages = services.MaxTotalPages;
        s_memoryAllocatePagesAddress = (nuint)services.AllocatePagesAddress;
        s_memoryReleasePagesAddress = (nuint)services.ReleasePagesAddress;
        s_memoryServicesInstalled = 1;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallEntropyServices")]
    internal static uint InstallEntropyServices(uint requestedAbiVersion,
                                                 nuint entropyServicesAddress)
    {
        GxManagedKernelEntropyServicesV1 services;
        if (requestedAbiVersion != EntropyServicesAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (!IsInitialized)
        {
            return NotInitialized;
        }
        if (s_entropyServicesInstalled != 0)
        {
            return AlreadyInitialized;
        }
        if (s_lifecycleState != (int)LifecycleState.EnvironmentInstalling ||
            entropyServicesAddress == 0 ||
            !IsRangeValid(entropyServicesAddress,
                          (nuint)GxManagedKernelEntropyServicesV1.ExpectedSize))
        {
            return s_lifecycleState != (int)LifecycleState.EnvironmentInstalling
                ? InvalidState : InvalidArgument;
        }

        services = *(GxManagedKernelEntropyServicesV1*)entropyServicesAddress;
        if (services.Size != GxManagedKernelEntropyServicesV1.ExpectedSize ||
            services.AbiVersion != EntropyServicesAbiVersionV1 ||
            services.ServiceVersion != EntropyServicesServiceVersionV1 ||
            services.Architecture != ArchitectureX64 ||
            (services.Capabilities & ~EntropyServicesKnownCapabilities) != 0 ||
            services.FillAddress == 0 ||
            services.MaxBytesPerFill == 0 ||
            services.MaxBytesPerFill > ManagedSecureRandom.MaximumBytesPerFill ||
            services.RetryCount == 0 || services.Reserved != 0)
        {
            return InvalidArgument;
        }

        s_entropyCapabilities = services.Capabilities;
        s_entropyFillAddress = (nuint)services.FillAddress;
        s_entropyMaxBytesPerFill = services.MaxBytesPerFill;
        s_entropyRetryCount = services.RetryCount;
        if ((s_entropyCapabilities & GxManagedKernelEntropyServicesV1.CapabilityHardware) != 0)
        {
            s_entropyService = new ManagedEntropyService(
                s_entropyFillAddress, s_entropyCapabilities,
                s_entropyMaxBytesPerFill);
            s_secureRandom = new ManagedSecureRandom(s_entropyService);
        }
        else
        {
            /* Keep the unsupported hardware path allocation-free until a
               device-backed provider is actually attached in Phase 26. */
            s_entropyService = null;
            s_secureRandom = null;
        }
        s_entropyServicesInstalled = 1;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallHostServices")]
    internal static uint InstallHostServices(uint requestedAbiVersion,
                                              nuint hostServicesAddress)
    {
        GxManagedKernelHostServicesV1 services;
        const ulong requiredCapabilities = RequiredHostServicesCapabilities;

        if (requestedAbiVersion != HostServicesAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (!IsInitialized)
        {
            return NotInitialized;
        }
        if (s_hostServicesInstalled != 0)
        {
            return AlreadyInitialized;
        }
        if (s_lifecycleState != (int)LifecycleState.EnvironmentInstalling)
        {
            return InvalidState;
        }
        if (hostServicesAddress == 0 ||
            !IsRangeValid(hostServicesAddress,
                          (nuint)GxManagedKernelHostServicesV1.ExpectedSize))
        {
            return InvalidArgument;
        }

        services = *(GxManagedKernelHostServicesV1*)hostServicesAddress;
        if (services.Size != GxManagedKernelHostServicesV1.ExpectedSize ||
            services.AbiVersion != HostServicesAbiVersionV1 ||
            services.ServiceVersion != HostServicesServiceVersionV1 ||
            services.Architecture != ArchitectureX64 ||
            (services.Capabilities & ~HostServicesKnownCapabilities) != 0 ||
            (services.Capabilities & requiredCapabilities) != requiredCapabilities ||
            services.LogUtf8Address == 0 ||
            ((services.Capabilities & GxManagedKernelHostServicesV1.CapabilityMonotonicTime) != 0 &&
             services.MonotonicTimeAddress == 0) ||
            ((services.Capabilities & GxManagedKernelHostServicesV1.CapabilityMonotonicTime) == 0 &&
             services.MonotonicTimeAddress != 0) ||
            services.Reserved0 != 0 || services.Reserved1 != 0)
        {
            return InvalidArgument;
        }

        s_hostCapabilities = services.Capabilities;
        s_hostLogUtf8Address = (nuint)services.LogUtf8Address;
        s_hostMonotonicTimeAddress = (nuint)services.MonotonicTimeAddress;
        s_hostServicesInstalled = 1;
        s_lifecycleState = (int)LifecycleState.Ready;
        return ManagedOk;
    }

    private static bool PublishedBootResourcesRemainStable()
    {
        GxManagedKernelBootResourceSummaryV1 current;
        if (!TryGetBootResourceSummary(out current)) return false;
        return current.Size == s_bootResourceSummarySnapshot.Size &&
               current.AbiVersion == s_bootResourceSummarySnapshot.AbiVersion &&
               current.ServiceVersion == s_bootResourceSummarySnapshot.ServiceVersion &&
               current.Architecture == s_bootResourceSummarySnapshot.Architecture &&
               current.RegionCount == s_bootResourceSummarySnapshot.RegionCount &&
               current.ResourceMapIdentity == s_bootResourceSummarySnapshot.ResourceMapIdentity &&
               current.TotalPhysicalBytes == s_bootResourceSummarySnapshot.TotalPhysicalBytes &&
               current.UsablePhysicalBytes == s_bootResourceSummarySnapshot.UsablePhysicalBytes &&
               current.Capabilities == s_bootResourceSummarySnapshot.Capabilities &&
               current.Reserved == s_bootResourceSummarySnapshot.Reserved;
    }

    private static uint StartBlocked(uint reason)
    {
        KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_START_BLOCKED=0x"u8,
                               reason);
        return InvalidState;
    }

    internal static bool TryInvokeHostLog(ReadOnlySpan<byte> utf8)
    {
        if (s_hostServicesInstalled == 0 ||
            (s_hostCapabilities & GxManagedKernelHostServicesV1.CapabilityLogUtf8) == 0 ||
            s_hostLogUtf8Address == 0 || utf8.Length > 1024)
        {
            return false;
        }
        delegate* unmanaged<nuint, nuint, uint, uint> callback =
            (delegate* unmanaged<nuint, nuint, uint, uint>)s_hostLogUtf8Address;
        fixed (byte* address = utf8)
        {
            return callback((nuint)address, (nuint)utf8.Length, 0) == ManagedOk;
        }
    }

    internal static bool TryQueryMonotonicTime(
        out GxManagedKernelMonotonicTimeV1 result)
    {
        result = default;
        if (s_hostServicesInstalled == 0 ||
            (s_hostCapabilities & GxManagedKernelHostServicesV1.CapabilityMonotonicTime) == 0 ||
            s_hostMonotonicTimeAddress == 0)
        {
            return false;
        }
        delegate* unmanaged<uint, nuint, nuint, uint> callback =
            (delegate* unmanaged<uint, nuint, nuint, uint>)s_hostMonotonicTimeAddress;
        uint status;
        fixed (GxManagedKernelMonotonicTimeV1* resultAddress = &result)
        {
            status = callback(HostServicesAbiVersionV1, (nuint)resultAddress,
                              (nuint)GxManagedKernelMonotonicTimeV1.ExpectedSize);
        }
        return status == ManagedOk &&
               result.Size == GxManagedKernelMonotonicTimeV1.ExpectedSize &&
               result.AbiVersion == HostServicesAbiVersionV1 &&
               result.FrequencyHz != 0 &&
               (result.Flags & ~MonotonicTimeKnownFlags) == 0 &&
               result.Reserved == 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelStart")]
    internal static uint Start()
    {
        GxManagedKernelMonotonicTimeV1 firstTime;
        GxManagedKernelMonotonicTimeV1 secondTime;
        bool hasMonotonicTime;
        uint index;

        if (!IsInitialized)
        {
            return NotInitialized;
        }
        if (s_lifecycleState == (int)LifecycleState.Started)
        {
            return AlreadyInitialized;
        }
        if (s_lifecycleState != (int)LifecycleState.Ready)
        {
            return InvalidState;
        }
        if (s_bootResourcesPublished == 0) return StartBlocked(1);
        if (s_hostServicesInstalled == 0) return StartBlocked(2);
        if (s_memoryServicesInstalled == 0) return StartBlocked(3);
        if ((s_hostCapabilities & RequiredHostServicesCapabilities) !=
            RequiredHostServicesCapabilities)
        {
            return StartBlocked(4);
        }
        if (!PublishedBootResourcesRemainStable())
        {
            return StartBlocked(5);
        }

        hasMonotonicTime =
            (s_hostCapabilities & GxManagedKernelHostServicesV1.CapabilityMonotonicTime) != 0;
        if (hasMonotonicTime)
        {
            if (!TryQueryMonotonicTime(out firstTime)) return StartBlocked(6);
            index = 0;
            while (index != 1024)
            {
                index++;
            }
            if (!TryQueryMonotonicTime(out secondTime) ||
                secondTime.Ticks < firstTime.Ticks ||
                secondTime.FrequencyHz != firstTime.FrequencyHz)
            {
                return StartBlocked(7);
            }
        }

        if (!KernelLog.Write(KernelLog.ManagedStartLog) ||
            !KernelLog.Write(KernelLog.ManagedHostLogCallOk) ||
            (hasMonotonicTime && !KernelLog.Write(KernelLog.ManagedMonotonicTimeOk)))
        {
            return StartBlocked(8);
        }
        s_lifecycleState = (int)LifecycleState.Started;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallSerialServices")]
    internal static uint InstallSerialServices(uint requestedAbiVersion,
                                                nuint servicesAddress,
                                                nuint deviceAddress)
    {
        return ManagedSerialDriverSubsystem.Install(
            requestedAbiVersion, servicesAddress, deviceAddress);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase8Accounting")]
    internal static uint RunPhase8Accounting()
    {
        return ManagedSerialDriverSubsystem.RunAccounting();
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase8")]
    internal static uint RunPhase8()
    {
        return ManagedSerialDriverSubsystem.Run();
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallInterruptServices")]
    internal static uint InstallInterruptServices(uint requestedAbiVersion,
                                                   nuint servicesAddress)
    {
        return ManagedSerialDriverSubsystem.InstallInterruptServices(
            requestedAbiVersion, servicesAddress);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallInputServices")]
    internal static uint InstallInputServices(uint requestedAbiVersion,
                                               nuint servicesAddress,
                                               nuint deviceAddress)
    {
        return ManagedSerialDriverSubsystem.InstallInputServices(
            requestedAbiVersion, servicesAddress, deviceAddress);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase9")]
    internal static uint RunPhase9(uint stage)
    {
        return ManagedSerialDriverSubsystem.RunPhase9(stage);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunDriverWorker")]
    internal static uint RunDriverWorker(uint stage)
    {
        return ManagedSerialDriverSubsystem.RunDriverWorker(stage);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase10")]
    internal static uint RunPhase10(uint stage)
    {
        return ManagedSerialDriverSubsystem.RunPhase10(stage);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase11")]
    internal static uint RunPhase11(uint stage)
    {
        return ManagedSerialDriverSubsystem.RunPhase11(stage);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallDeviceInventory")]
    internal static uint InstallDeviceInventory(uint requestedAbiVersion,
                                                  nuint publicationAddress)
    {
        ManagedDeviceInventory? candidate;
        ManagedDevice first;
        ManagedDevice selected;
        bool classQuery;

        if (requestedAbiVersion != DeviceInventoryAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (!IsInitialized)
        {
            return NotInitialized;
        }
        if (s_deviceInventoryInstalled != 0)
        {
            return AlreadyInitialized;
        }
        if (s_lifecycleState != (int)LifecycleState.Started ||
            s_memoryServicesInstalled == 0)
        {
            return InvalidState;
        }
        if (publicationAddress == 0 ||
            !IsRangeValid(publicationAddress,
                (nuint)GxManagedKernelDeviceInventoryPublicationV1.ExpectedSize))
        {
            return InvalidArgument;
        }
        if (!ManagedDeviceInventory.TryCreateFromPublication(
                  Phase4KernelMemoryProvider.Instance, publicationAddress,
                  out candidate) || candidate == null)
        {
            return InvalidArgument;
        }
        if (!candidate.TryGetDevice(0, out first) ||
            !candidate.ValidateInvariants())
        {
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_ARENA_CREATE_REJECTED\r\n"u8);
            candidate.Destroy();
            return InvalidState;
        }
        classQuery = candidate.TryFindFirstByClass(0x06, 0x00, out selected);
        if (!classQuery)
        {
            classQuery = candidate.TryFindFirstByClass(
                first.ClassCode, first.Subclass, out selected);
        }
        if (!classQuery || !candidate.TryRunRuntimeSurvival())
        {
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_RUNTIME_REJECTED\r\n"u8);
            candidate.Destroy();
            return InvalidState;
        }

        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_INVENTORY_INSTALLED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_COUNT_OK\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_COUNT=0x"u8,
                                    candidate.DeviceCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_RESOURCE_COUNT=0x"u8,
                                    candidate.ResourceCount) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_UNIQUENESS_OK\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_ARENA_OK\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_ARENA_PAGES=0x"u8,
                                    candidate.Metrics.TotalBackingBytes / KernelArena.PageSize) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_ARENA_CHUNKS=0x"u8,
                                    candidate.Metrics.BackingChunkCount) ||
            !candidate.TryFindPciDevice(first.Segment, first.Bus, first.Device,
                                        first.Function, out ManagedDevice firstAgain) ||
            firstAgain.VendorId != first.VendorId ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_LOOKUP_OK\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_CLASS_QUERY_OK\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_MULTIPLE_QUERY_OK\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_SELECTED_BDF=0x"u8,
                ((ulong)selected.Segment << 32) | ((ulong)selected.Bus << 24) |
                ((ulong)selected.Device << 16) | ((ulong)selected.Function << 8)) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_SELECTED_VENDOR=0x"u8,
                                    selected.VendorId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_SELECTED_DEVICE=0x"u8,
                                    selected.DeviceId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DEVICE_SELECTED_CLASS=0x"u8,
                ((ulong)selected.ClassCode << 16) |
                ((ulong)selected.Subclass << 8) | selected.ProgrammingInterface) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_RESOURCE_DATA_UNAVAILABLE\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_RUNTIME_SURVIVAL_OK\r\n"u8))
        {
            candidate.Destroy();
            return InvalidState;
        }

        s_deviceInventory = candidate;
        s_deviceInventoryInstalled = 1;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallDeviceResources")]
    internal static uint InstallDeviceResources(uint requestedAbiVersion,
                                                nuint publicationAddress)
    {
        if (requestedAbiVersion != DeviceResourceAbiVersionV1) return UnsupportedAbi;
        if (!IsInitialized || s_deviceInventoryInstalled == 0 ||
            s_deviceInventory == null) return NotInitialized;
        if (s_deviceResourcesInstalled != 0) return AlreadyInitialized;
        if (s_lifecycleState != (int)LifecycleState.Started ||
            s_memoryServicesInstalled == 0 || publicationAddress == 0 ||
            !IsRangeValid(publicationAddress,
                (nuint)GxManagedKernelDeviceResourcePublicationV1.ExpectedSize))
        {
            return s_lifecycleState != (int)LifecycleState.Started
                ? InvalidState : InvalidArgument;
        }
        if (!ManagedDeviceResourceRuntimeCatalog.TryInstallFromPublication(
                Phase4KernelMemoryProvider.Instance, publicationAddress))
        {
            return InvalidArgument;
        }
        if (ManagedDeviceResourceRuntimeCatalog.ResourceCount == 0 ||
            ManagedDeviceResourceRuntimeCatalog.ResourceCount > DeviceResourceMaxDescriptors ||
            !ManagedDeviceResourceRuntimeCatalog.ValidateInvariants())
        {
            ManagedDeviceResourceRuntimeCatalog.Destroy();
            return InvalidState;
        }
        s_deviceResourcesInstalled = 1;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_SERVICES_INSTALLED\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_DISCOVERY_OK\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_RESOURCE_COUNT=0x"u8,
                                    ManagedDeviceResourceRuntimeCatalog.ResourceCount) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_CATALOG_OK\r\n"u8))
        {
            ManagedDeviceResourceRuntimeCatalog.Destroy();
            s_deviceResourcesInstalled = 0;
            return InvalidState;
        }
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallMmioServices")]
    internal static uint InstallMmioServices(uint requestedAbiVersion,
                                              nuint serviceAddress)
    {
        if (requestedAbiVersion != MmioServicesAbiVersionV1) return UnsupportedAbi;
        if (!IsInitialized || s_deviceResourcesInstalled == 0 ||
            s_mmioServicesInstalled != 0) return s_mmioServicesInstalled != 0
                ? AlreadyInitialized : NotInitialized;
        if (s_lifecycleState != (int)LifecycleState.Started ||
            serviceAddress == 0 ||
            !IsRangeValid(serviceAddress,
                (nuint)GxManagedKernelMmioServicesV1.ExpectedSize)) {
            return s_lifecycleState != (int)LifecycleState.Started
                ? InvalidState : InvalidArgument;
        }
        GxManagedKernelMmioServicesV1* service =
            (GxManagedKernelMmioServicesV1*)serviceAddress;
        if (service->Size != GxManagedKernelMmioServicesV1.ExpectedSize ||
            service->AbiVersion != MmioServicesAbiVersionV1 ||
            service->ServiceVersion != MmioServicesServiceVersionV1 ||
            service->Architecture != ArchitectureX64 ||
            service->Capabilities != MmioServicesKnownCapabilities ||
            service->ClaimAddress == 0 || service->ReleaseAddress == 0 ||
            service->MapAddress == 0 || service->UnmapAddress == 0 ||
            service->ReadAddress == 0 || service->WriteAddress == 0 ||
            service->MaxClaims != DeviceResourceMaxClaims ||
            service->MaxMappings != ManagedDeviceResourceRuntimeCatalog.MaxMappings ||
            service->WindowBase == 0 ||
            service->WindowLength == 0 ||
            service->WindowBase > ulong.MaxValue - service->WindowLength) {
            return InvalidArgument;
        }
        s_mmioCapabilities = service->Capabilities;
        s_mmioClaimAddress = (nuint)service->ClaimAddress;
        s_mmioReleaseAddress = (nuint)service->ReleaseAddress;
        s_mmioMapAddress = (nuint)service->MapAddress;
        s_mmioUnmapAddress = (nuint)service->UnmapAddress;
        s_mmioReadAddress = (nuint)service->ReadAddress;
        s_mmioWriteAddress = (nuint)service->WriteAddress;
        s_mmioMaxClaims = service->MaxClaims;
        s_mmioServicesInstalled = 1;
        return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MMIO_SERVICES_INSTALLED\r\n"u8)
            ? ManagedOk : InvalidState;
    }

    internal static bool MmioServicesInstalled =>
        s_mmioServicesInstalled != 0;

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallDmaServices")]
    internal static uint InstallDmaServices(uint requestedAbiVersion,
                                             nuint serviceAddress)
    {
        if (requestedAbiVersion != DmaServicesAbiVersionV1) return UnsupportedAbi;
        if (!IsInitialized || s_deviceResourcesInstalled == 0 ||
            s_mmioServicesInstalled == 0)
            return NotInitialized;
        if (s_dmaServicesInstalled != 0) return AlreadyInitialized;
        if (s_lifecycleState != (int)LifecycleState.Started || serviceAddress == 0 ||
            !IsRangeValid(serviceAddress,
                (nuint)GxManagedKernelDmaServicesV1.ExpectedSize))
            return s_lifecycleState != (int)LifecycleState.Started
                ? InvalidState : InvalidArgument;
        GxManagedKernelDmaServicesV1* service =
            (GxManagedKernelDmaServicesV1*)serviceAddress;
        if (service->Size != GxManagedKernelDmaServicesV1.ExpectedSize ||
            service->AbiVersion != DmaServicesAbiVersionV1 ||
            service->ServiceVersion != DmaServicesServiceVersionV1 ||
            service->Architecture != ArchitectureX64 ||
            service->Capabilities != DmaServicesKnownCapabilities ||
            service->AllocateAddress == 0 || service->ReleaseAddress == 0 ||
            service->ReadAddress == 0 || service->WriteAddress == 0 ||
            service->RetainAddress == 0 || service->ReleaseReferenceAddress == 0 ||
            service->MaxAllocations == 0 || service->MaxAllocations > 8 ||
            service->MaxPagesPerAllocation == 0 || service->MaxPagesPerAllocation > 32 ||
            service->MaxTotalPages == 0 || service->MaxTotalPages > 64 ||
            service->MaxBusAddress == 0 || service->Reserved != 0)
            return InvalidArgument;
        s_dmaCapabilities = service->Capabilities;
        s_dmaAllocateAddress = (nuint)service->AllocateAddress;
        s_dmaReleaseAddress = (nuint)service->ReleaseAddress;
        s_dmaReadAddress = (nuint)service->ReadAddress;
        s_dmaWriteAddress = (nuint)service->WriteAddress;
        s_dmaRetainAddress = (nuint)service->RetainAddress;
        s_dmaReleaseReferenceAddress = (nuint)service->ReleaseReferenceAddress;
        s_dmaMaxAllocations = service->MaxAllocations;
        s_dmaMaxPagesPerAllocation = service->MaxPagesPerAllocation;
        s_dmaMaxTotalPages = service->MaxTotalPages;
        s_dmaMaxBusAddress = service->MaxBusAddress;
        s_dmaServicesInstalled = 1;
        return KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DMA_SERVICES_INSTALLED\r\n"u8)
            ? ManagedOk : InvalidState;
    }

    internal static bool DmaServicesInstalled => s_dmaServicesInstalled != 0;

    internal static bool TryDmaAllocate(ulong claimHandle, uint driverId,
                                        ulong bytes, ulong alignment,
                                        out GxManagedKernelDmaAllocationResultV1 result)
    {
        result = default;
        if (s_dmaServicesInstalled == 0 || claimHandle == 0 || driverId == 0 ||
            bytes == 0 || alignment == 0 || s_dmaAllocateAddress == 0) return false;
        delegate* unmanaged<ulong, uint, ulong, ulong, nuint, nuint, uint> callback =
            (delegate* unmanaged<ulong, uint, ulong, ulong, nuint, nuint, uint>)
                s_dmaAllocateAddress;
        uint status;
        fixed (GxManagedKernelDmaAllocationResultV1* resultPointer = &result)
        {
            status = callback(claimHandle, driverId, bytes, alignment,
                              (nuint)resultPointer,
                              (nuint)GxManagedKernelDmaAllocationResultV1.ExpectedSize);
        }
        return status == ManagedOk &&
               result.Size == GxManagedKernelDmaAllocationResultV1.ExpectedSize &&
               result.AbiVersion == DmaServicesAbiVersionV1 && result.Handle != 0 &&
               result.BusAddress != 0 && result.ByteLength >= bytes &&
               result.PageCount != 0 && result.Alignment == alignment &&
               result.Reserved == 0;
    }

    internal static bool TryDmaRelease(ulong handle, uint driverId)
    {
        if (s_dmaServicesInstalled == 0 || handle == 0 || driverId == 0 ||
            s_dmaReleaseAddress == 0) return false;
        delegate* unmanaged<ulong, uint, uint> callback =
            (delegate* unmanaged<ulong, uint, uint>)s_dmaReleaseAddress;
        return callback(handle, driverId) == ManagedOk;
    }

    internal static bool TryDmaWrite(ulong handle, uint driverId, ulong offset,
                                     nuint source, ulong length)
    {
        if (s_dmaServicesInstalled == 0 || handle == 0 || driverId == 0 ||
            source == 0 || length == 0 || length > nuint.MaxValue ||
            !IsRangeValid(source, (nuint)length) || s_dmaWriteAddress == 0)
            return false;
        delegate* unmanaged<ulong, uint, ulong, nuint, ulong, uint> callback =
            (delegate* unmanaged<ulong, uint, ulong, nuint, ulong, uint>)
                s_dmaWriteAddress;
        return callback(handle, driverId, offset, source, length) == ManagedOk;
    }

    internal static bool TryDmaRead(ulong handle, uint driverId, ulong offset,
                                    nuint destination, ulong length)
    {
        if (s_dmaServicesInstalled == 0 || handle == 0 || driverId == 0 ||
            destination == 0 || length == 0 || length > nuint.MaxValue ||
            !IsRangeValid(destination, (nuint)length) || s_dmaReadAddress == 0)
            return false;
        delegate* unmanaged<ulong, uint, ulong, nuint, ulong, uint> callback =
            (delegate* unmanaged<ulong, uint, ulong, nuint, ulong, uint>)
                s_dmaReadAddress;
        return callback(handle, driverId, offset, destination, length) == ManagedOk;
    }

    internal static bool TryDmaRetain(ulong handle, uint driverId) =>
        TryDmaReference(s_dmaRetainAddress, handle, driverId);

    internal static bool TryDmaReleaseReference(ulong handle, uint driverId) =>
        TryDmaReference(s_dmaReleaseReferenceAddress, handle, driverId);

    private static bool TryDmaReference(nuint address, ulong handle, uint driverId)
    {
        if (s_dmaServicesInstalled == 0 || address == 0 || handle == 0 ||
            driverId == 0) return false;
        delegate* unmanaged<ulong, uint, uint> callback =
            (delegate* unmanaged<ulong, uint, uint>)address;
        return callback(handle, driverId) == ManagedOk;
    }

    internal static bool TryMmioClaim(ulong resourceId, uint driverId,
                                      uint expectedOwnerKind,
                                      uint expectedOwnerId,
                                      out ulong handle)
    {
        GxManagedKernelMmioClaimResultV1 result = default;
        handle = 0;
        if (s_mmioServicesInstalled == 0 || resourceId == 0 || driverId == 0 ||
            s_mmioClaimAddress == 0) return false;
        delegate* unmanaged<ulong, uint, uint, uint, nuint, nuint, uint> callback =
            (delegate* unmanaged<ulong, uint, uint, uint, nuint, nuint, uint>)
                s_mmioClaimAddress;
        uint status = callback(resourceId, driverId, expectedOwnerKind,
                               expectedOwnerId, (nuint)(&result),
                               (nuint)GxManagedKernelMmioClaimResultV1.ExpectedSize);
        if (status != ManagedOk || result.Size !=
                GxManagedKernelMmioClaimResultV1.ExpectedSize ||
            result.AbiVersion != MmioServicesAbiVersionV1 || result.Handle == 0 ||
            result.Reserved != 0) return false;
        handle = result.Handle;
        return true;
    }

    internal static bool TryMmioRelease(ulong handle, uint driverId)
    {
        if (s_mmioServicesInstalled == 0 || handle == 0 || driverId == 0 ||
            s_mmioReleaseAddress == 0) return false;
        delegate* unmanaged<ulong, uint, uint> callback =
            (delegate* unmanaged<ulong, uint, uint>)s_mmioReleaseAddress;
        return callback(handle, driverId) == ManagedOk;
    }

    internal static bool TryMmioMap(ulong claimHandle, uint driverId,
                                    ulong offset, ulong length, uint access,
                                    ulong resourceId, out ulong mappingHandle)
    {
        GxManagedKernelMmioMappingResultV1 result = default;
        mappingHandle = 0;
        if (s_mmioServicesInstalled == 0 || claimHandle == 0 || driverId == 0 ||
            length == 0 || s_mmioMapAddress == 0) return false;
        delegate* unmanaged<ulong, uint, ulong, ulong, uint, nuint, nuint, uint>
            callback = (delegate* unmanaged<ulong, uint, ulong, ulong, uint,
                         nuint, nuint, uint>)s_mmioMapAddress;
        uint status = callback(claimHandle, driverId, offset, length, access,
                               (nuint)(&result),
                               (nuint)GxManagedKernelMmioMappingResultV1.ExpectedSize);
        if (status != ManagedOk || result.Size !=
                GxManagedKernelMmioMappingResultV1.ExpectedSize ||
            result.AbiVersion != MmioServicesAbiVersionV1 || result.Handle == 0 ||
            result.ResourceId != resourceId || result.Offset != offset ||
            result.Length != length || result.Access != access ||
            result.Reserved0 != 0) return false;
        mappingHandle = result.Handle;
        return true;
    }

    internal static bool TryMmioUnmap(ulong mappingHandle, uint driverId)
    {
        if (s_mmioServicesInstalled == 0 || mappingHandle == 0 || driverId == 0 ||
            s_mmioUnmapAddress == 0) return false;
        delegate* unmanaged<ulong, uint, uint> callback =
            (delegate* unmanaged<ulong, uint, uint>)s_mmioUnmapAddress;
        return callback(mappingHandle, driverId) == ManagedOk;
    }

    internal static bool TryMmioRead(ulong mappingHandle, uint driverId,
                                     ulong offset, uint width, out ulong value)
    {
        GxManagedKernelMmioReadResultV1 result = default;
        value = 0;
        if (s_mmioServicesInstalled == 0 || mappingHandle == 0 || driverId == 0 ||
            s_mmioReadAddress == 0) return false;
        delegate* unmanaged<ulong, uint, ulong, uint, nuint, nuint, uint> callback =
            (delegate* unmanaged<ulong, uint, ulong, uint, nuint, nuint, uint>)
                s_mmioReadAddress;
        uint status = callback(mappingHandle, driverId, offset, width,
                               (nuint)(&result),
                               (nuint)GxManagedKernelMmioReadResultV1.ExpectedSize);
        if (status != ManagedOk || result.Size !=
                GxManagedKernelMmioReadResultV1.ExpectedSize ||
            result.AbiVersion != MmioServicesAbiVersionV1 ||
            result.Width != width || result.Reserved0 != 0 ||
            result.Reserved1 != 0) return false;
        value = result.Value;
        return true;
    }

    internal static bool TryMmioWrite(ulong mappingHandle, uint driverId,
                                      ulong offset, uint width, ulong value)
    {
        if (s_mmioServicesInstalled == 0 || mappingHandle == 0 ||
            driverId == 0 || s_mmioWriteAddress == 0 ||
            (width != 1 && width != 2 && width != 4 && width != 8)) return false;
        delegate* unmanaged<ulong, uint, ulong, uint, ulong, uint> callback =
            (delegate* unmanaged<ulong, uint, ulong, uint, ulong, uint>)
                s_mmioWriteAddress;
        return callback(mappingHandle, driverId, offset, width, value) == ManagedOk;
    }

    internal static bool TryPciCommandEnable(ulong resourceId, ulong claimHandle,
                                               uint driverId, uint requestedBits,
                                               out GxManagedKernelPciCommandResultV1 result)
    {
        result = default;
        if (s_pciServicesInstalled == 0 || resourceId == 0 || claimHandle == 0 ||
            driverId == 0 || s_pciConfigCommandAddress == 0 || requestedBits == 0)
            return false;
        delegate* unmanaged<ulong, ulong, uint, uint, uint, nuint, nuint, uint>
            callback = (delegate* unmanaged<ulong, ulong, uint, uint, uint,
                         nuint, nuint, uint>)s_pciConfigCommandAddress;
        uint status;
        fixed (GxManagedKernelPciCommandResultV1* resultPointer = &result)
        {
            status = callback(resourceId, claimHandle, driverId, 1, requestedBits,
                              (nuint)resultPointer,
                              (nuint)GxManagedKernelPciCommandResultV1.ExpectedSize);
        }
        bool valid = status == ManagedOk &&
               result.Size == GxManagedKernelPciCommandResultV1.ExpectedSize &&
               result.AbiVersion == PciServicesAbiVersionV1 &&
               result.RequestedBits == requestedBits && result.Reserved == 0;
        return valid;
    }

    internal static bool TryPciCommandRestore(ulong resourceId, ulong claimHandle,
                                               uint driverId, uint originalCommand,
                                               out GxManagedKernelPciCommandResultV1 result)
    {
        result = default;
        if (s_pciServicesInstalled == 0 || resourceId == 0 || claimHandle == 0 ||
            driverId == 0 || s_pciConfigCommandAddress == 0) return false;
        delegate* unmanaged<ulong, ulong, uint, uint, uint, nuint, nuint, uint>
            callback = (delegate* unmanaged<ulong, ulong, uint, uint, uint,
                         nuint, nuint, uint>)s_pciConfigCommandAddress;
        uint status;
        fixed (GxManagedKernelPciCommandResultV1* resultPointer = &result)
        {
            status = callback(resourceId, claimHandle, driverId, 2, originalCommand,
                              (nuint)resultPointer,
                              (nuint)GxManagedKernelPciCommandResultV1.ExpectedSize);
        }
        return status == ManagedOk &&
               result.Size == GxManagedKernelPciCommandResultV1.ExpectedSize &&
               result.AbiVersion == PciServicesAbiVersionV1 &&
               result.RequestedBits == originalCommand && result.Reserved == 0;
    }

    internal static bool TryPciCommandDisableBusMaster(
        ulong resourceId, ulong claimHandle, uint driverId,
        out GxManagedKernelPciCommandResultV1 result)
    {
        result = default;
        if (s_pciServicesInstalled == 0 || resourceId == 0 || claimHandle == 0 ||
            driverId == 0 || s_pciConfigCommandAddress == 0) return false;
        delegate* unmanaged<ulong, ulong, uint, uint, uint, nuint, nuint, uint>
            callback = (delegate* unmanaged<ulong, ulong, uint, uint, uint,
                         nuint, nuint, uint>)s_pciConfigCommandAddress;
        uint status;
        fixed (GxManagedKernelPciCommandResultV1* resultPointer = &result)
        {
            status = callback(resourceId, claimHandle, driverId, 3,
                              ManagedE1000Protocol.PciCommandBusMaster,
                              (nuint)resultPointer,
                              (nuint)GxManagedKernelPciCommandResultV1.ExpectedSize);
        }
        return status == ManagedOk &&
               result.Size == GxManagedKernelPciCommandResultV1.ExpectedSize &&
               result.AbiVersion == PciServicesAbiVersionV1 &&
               result.RequestedBits == ManagedE1000Protocol.PciCommandBusMaster &&
               (result.ResultingCommand & ManagedE1000Protocol.PciCommandBusMaster) == 0 &&
               result.Reserved == 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedQueryDeviceResourceSummary")]
    internal static uint QueryDeviceResourceSummary(uint requestedAbiVersion,
                                                    nuint outputAddress,
                                                    nuint outputCapacity)
    {
        if (requestedAbiVersion != DeviceResourceAbiVersionV1) return UnsupportedAbi;
        if (!IsInitialized || s_deviceResourcesInstalled == 0 ||
            !ManagedDeviceResourceRuntimeCatalog.IsInstalled) return NotInitialized;
        if (outputAddress == 0) return InvalidArgument;
        if (outputCapacity < GxManagedKernelDeviceResourceSummaryV1.ExpectedSize)
            return BufferTooSmall;
        if (!IsRangeValid(outputAddress, outputCapacity)) return InvalidArgument;
        *(GxManagedKernelDeviceResourceSummaryV1*)outputAddress =
            new GxManagedKernelDeviceResourceSummaryV1
            {
                Size = GxManagedKernelDeviceResourceSummaryV1.ExpectedSize,
                AbiVersion = DeviceResourceAbiVersionV1,
                ServiceVersion = DeviceResourceServiceVersionV1,
                Architecture = ArchitectureX64,
                ResourceCount = ManagedDeviceResourceRuntimeCatalog.ResourceCount,
                MaxClaims = DeviceResourceMaxClaims,
                Capabilities = DeviceResourceCapabilities,
                Reserved = 0
            };
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedQueryDeviceResource")]
    internal static uint QueryDeviceResource(uint requestedAbiVersion, uint index,
                                             nuint outputAddress, nuint outputCapacity)
    {
        if (requestedAbiVersion != DeviceResourceAbiVersionV1) return UnsupportedAbi;
        if (!IsInitialized || s_deviceResourcesInstalled == 0 ||
            !ManagedDeviceResourceRuntimeCatalog.IsInstalled) return NotInitialized;
        if (index >= ManagedDeviceResourceRuntimeCatalog.ResourceCount) return OutOfRange;
        if (outputAddress == 0) return InvalidArgument;
        if (outputCapacity < GxManagedKernelDeviceResourceV1.ExpectedSize)
            return BufferTooSmall;
        if (!IsRangeValid(outputAddress, outputCapacity) ||
            !ManagedDeviceResourceRuntimeCatalog.TryGetResource(index, out _)) return InvalidArgument;
        /* The public result is copied from the catalog's validated immutable
           snapshot without returning a capability pointer. */
        if (!ManagedDeviceResourceRuntimeCatalog.TryCopyDescriptor(index,
                (GxManagedKernelDeviceResourceV1*)outputAddress)) return InvalidState;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase12")]
    internal static uint RunPhase12(uint stage)
    {
        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
            return IsInitialized ? InvalidState : NotInitialized;
        if (stage == 1)
        {
            if (s_phase12Run != 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                s_deviceResourcesInstalled == 0) return InvalidState;
            if (!ManagedDeviceResourceRuntimeCatalog.TryFindByOwner(
                    GxManagedKernelDeviceResourceV1.DeviceKindPlatformSerial, 1, 0,
                    out ManagedDeviceResource serial) ||
                !ManagedDeviceResourceRuntimeCatalog.TryFindByOwner(
                    GxManagedKernelDeviceResourceV1.DeviceKindPlatformKeyboard, 1, 0,
                    out ManagedDeviceResource keyboard) ||
                !ManagedDeviceResourceRuntimeCatalog.TryClaim(in serial, ManagedSerialDriver.DriverId,
                    GxManagedKernelDeviceResourceV1.DeviceKindPlatformSerial, 1) ||
                ManagedDeviceResourceRuntimeCatalog.TryClaim(in keyboard, ManagedSerialDriver.DriverId,
                    GxManagedKernelDeviceResourceV1.DeviceKindPlatformSerial, 1) ||
                !ManagedDeviceResourceRuntimeCatalog.TryRelease(in serial, ManagedSerialDriver.DriverId) ||
                !ManagedDeviceResourceRuntimeCatalog.TryClaim(in keyboard, ManagedKeyboardDriver.DriverId,
                    GxManagedKernelDeviceResourceV1.DeviceKindPlatformKeyboard, 1) ||
                !ManagedDeviceResourceRuntimeCatalog.TryRelease(in keyboard, ManagedKeyboardDriver.DriverId) ||
                !ManagedDeviceResourceRuntimeCatalog.TryRunRuntimeSurvival() ||
                !ManagedDeviceResourceRuntimeCatalog.ValidateInvariants()) return InvalidState;
            if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_CLAIM_OK\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_WRONG_OWNER_REJECTED\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_RUNTIME_SURVIVAL_OK\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_ACCESS_DEFERRED_MMIO_CACHE\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_NEGATIVE_TESTS_OK\r\n"u8))
                return InvalidState;
            s_phase12Run = 1;
            return ManagedOk;
        }
        if (stage == 2)
        {
            if (s_phase12Run == 0 || s_phase12TeardownRun != 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                !ManagedDeviceResourceRuntimeCatalog.ValidateInvariants()) return InvalidState;
            GC.Collect();
            if (!ManagedDeviceResourceRuntimeCatalog.ValidateInvariants() ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_GC_SURVIVAL_OK\r\n"u8) ||
                !ManagedDeviceResourceRuntimeCatalog.Destroy()) return InvalidState;
            s_phase12TeardownRun = 1;
            if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_RELEASE_OK\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_RESOURCE_ACCOUNTING_RESTORED\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE12_PASS\r\n"u8))
                return InvalidState;
            return ManagedOk;
        }
        return InvalidArgument;
    }

    private static void CleanupMmioProof(
        in ManagedDeviceResource resource, uint driverId,
        ManagedMmioMapping? mapping, bool claimLive)
    {
        if (mapping != null && mapping.IsLive) mapping.TryUnmap();
        if (claimLive)
            ManagedDeviceResourceRuntimeCatalog.TryAbortDriver(driverId);
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase13")]
    internal static uint RunPhase13(uint stage)
    {
        const uint phase13DriverId = 0xD013;
        const uint targetOwnerId = 0x808610D3;
        const ulong statusOffset = 0x8;
        const ulong mapLength = 0x10;
        ManagedDeviceResource targetResource = default;
        bool targetResourceFound = false;
        ManagedMmioMapping? mapping = null;

        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
            return IsInitialized ? InvalidState : NotInitialized;
        if (stage == 1)
        {
            if (s_phase13Run != 0 || s_deviceResourcesInstalled == 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                s_mmioServicesInstalled == 0 || s_deviceInventory == null)
                return InvalidState;
            if (!s_deviceInventory.TryFindPciDevice(0, 0, 2, 0,
                    out ManagedDevice target) || target.VendorId != 0x8086 ||
                target.DeviceId != 0x10D3 || target.ClassCode != 0x02 ||
                target.Subclass != 0x00 || target.ProgrammingInterface != 0x00)
                return NotFound;

            for (uint index = 0; index !=
                     ManagedDeviceResourceRuntimeCatalog.ResourceCount; ++index)
            {
                if (!ManagedDeviceResourceRuntimeCatalog.TryGetResource(index,
                        out ManagedDeviceResource candidate) ||
                    candidate.ResourceType !=
                        GxManagedKernelDeviceResourceV1.ResourceTypeMmio ||
                    candidate.OwnerDeviceKind != GxManagedKernelDeviceV1.DeviceKindPci ||
                    candidate.OwnerDeviceId != targetOwnerId ||
                    candidate.OwnerSegment != 0 || candidate.OwnerBus != 0 ||
                    candidate.OwnerDevice != 2 || candidate.OwnerFunction != 0 ||
                    (candidate.Flags & (GxManagedKernelDeviceResourceV1.FlagReadable |
                                        GxManagedKernelDeviceResourceV1.FlagMemory |
                                        GxManagedKernelDeviceResourceV1.FlagCacheUncached |
                                        GxManagedKernelDeviceResourceV1.FlagPciAssigned)) !=
                        (GxManagedKernelDeviceResourceV1.FlagReadable |
                         GxManagedKernelDeviceResourceV1.FlagMemory |
                         GxManagedKernelDeviceResourceV1.FlagCacheUncached |
                         GxManagedKernelDeviceResourceV1.FlagPciAssigned))
                    continue;
                targetResource = candidate;
                targetResourceFound = true;
                break;
            }
            if (!targetResourceFound) return NotFound;
            if (targetResource.Length < mapLength ||
                targetResource.Length <= statusOffset ||
                !ManagedDeviceResourceRuntimeCatalog.TryClaim(
                    in targetResource, phase13DriverId,
                    GxManagedKernelDeviceV1.DeviceKindPci, targetOwnerId))
                return InvalidState;

            if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MMIO_MAPPING_REQUESTED\r\n"u8) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_BDF=0x"u8,
                                        0x0000000000000200) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_VENDOR=0x"u8,
                                        target.VendorId) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_DEVICE=0x"u8,
                                        target.DeviceId) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_BAR_BASE=0x"u8,
                                        targetResource.PhysicalBase) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_BAR_LENGTH=0x"u8,
                                        targetResource.Length) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_RESOURCE_OFFSET=0x"u8,
                                        0) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_RESOURCE_LENGTH=0x"u8,
                                        mapLength))
            {
                ManagedDeviceResourceRuntimeCatalog.TryRelease(in targetResource,
                                                                phase13DriverId);
                return InvalidState;
            }

            if (!ManagedDeviceResourceRuntimeCatalog.TryMap(
                    in targetResource, phase13DriverId, 0, mapLength, 1,
                    out mapping) || mapping == null)
            {
                ManagedDeviceResourceRuntimeCatalog.TryRelease(in targetResource,
                                                                phase13DriverId);
                return InvalidState;
            }
            if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MMIO_MAPPING_CREATED\r\n"u8) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_MAPPING_HANDLE=0x"u8,
                                        mapping.Handle))
            {
                CleanupMmioProof(in targetResource, phase13DriverId, mapping, true);
                return InvalidState;
            }

            if (!mapping.TryRead32(statusOffset, out uint statusValue) ||
                statusValue == 0xFFFFFFFFU ||
                !mapping.TryRead32(statusOffset, out uint repeatedStatus) ||
                repeatedStatus != statusValue)
            {
                CleanupMmioProof(in targetResource, phase13DriverId, mapping, true);
                return InvalidState;
            }
            if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MMIO_READ\r\n"u8) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_REGISTER_OFFSET=0x"u8,
                                        statusOffset) ||
                !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_MMIO_REGISTER_VALUE=0x"u8,
                                        statusValue))
            {
                CleanupMmioProof(in targetResource, phase13DriverId, mapping, true);
                return InvalidState;
            }

            if (ManagedDeviceResourceRuntimeCatalog.TryRelease(
                    in targetResource, phase13DriverId) ||
                mapping.TryRead32(mapping.Length, out _) ||
                mapping.TryRead32(mapping.Length - 3, out _) ||
                mapping.TryRead16(1, out _) ||
                new ManagedMmioMapping(targetResource.ResourceId, phase13DriverId,
                                       0xFFFFFFFF00000001, 4).TryRead32(0, out _))
            {
                CleanupMmioProof(in targetResource, phase13DriverId, mapping, true);
                return InvalidState;
            }

            GC.Collect();
            GC.KeepAlive(mapping);
            if (!mapping.TryRead32(statusOffset, out uint gcStatus) ||
                gcStatus != statusValue || !KernelLog.Write(
                    "GXOS_NET10:MANAGED_KERNEL_MMIO_GC_SURVIVAL_OK\r\n"u8))
            {
                CleanupMmioProof(in targetResource, phase13DriverId, mapping, true);
                return InvalidState;
            }
            if (!mapping.TryUnmap() || mapping.IsLive || mapping.TryUnmap() ||
                mapping.TryRead32(statusOffset, out _) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MMIO_MAPPING_TEARDOWN\r\n"u8))
            {
                CleanupMmioProof(in targetResource, phase13DriverId, mapping, true);
                return InvalidState;
            }
            if (!ManagedDeviceResourceRuntimeCatalog.TryRelease(
                    in targetResource, phase13DriverId) ||
                ManagedDeviceResourceRuntimeCatalog.TryMap(
                    in targetResource, phase13DriverId, 0, mapLength, 1,
                    out _))
            {
                CleanupMmioProof(in targetResource, phase13DriverId, mapping, true);
                return InvalidState;
            }

            for (uint cycle = 0; cycle != 3; ++cycle)
            {
                ManagedMmioMapping? cycleMapping = null;
                bool cycleClaimLive =
                    ManagedDeviceResourceRuntimeCatalog.TryClaim(
                        in targetResource, phase13DriverId,
                        GxManagedKernelDeviceV1.DeviceKindPci, targetOwnerId);
                if (!cycleClaimLive ||
                    !ManagedDeviceResourceRuntimeCatalog.TryMap(
                        in targetResource, phase13DriverId, 0, mapLength, 1,
                        out cycleMapping) || cycleMapping == null ||
                    !cycleMapping.TryUnmap() ||
                    !ManagedDeviceResourceRuntimeCatalog.TryRelease(
                        in targetResource, phase13DriverId))
                {
                    CleanupMmioProof(in targetResource, phase13DriverId,
                                     cycleMapping, cycleClaimLive);
                    return InvalidState;
                }
            }
            if (!ManagedDeviceResourceRuntimeCatalog.ValidateInvariants() ||
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0)
            {
                CleanupMmioProof(in targetResource, phase13DriverId,
                                 mapping, true);
                return InvalidState;
            }
            if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MMIO_NEGATIVE_TESTS_OK\r\n"u8))
                return InvalidState;
            s_phase13Run = 1;
            return ManagedOk;
        }
        if (stage == 2)
        {
            if (s_phase13Run == 0 || s_phase13TeardownRun != 0 ||
                !ManagedDeviceResourceRuntimeCatalog.ValidateInvariants() ||
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0)
                return InvalidState;
            GC.Collect();
            if (!ManagedDeviceResourceRuntimeCatalog.ValidateInvariants() ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MMIO_ACCOUNTING_RESTORED\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE13_PASS\r\n"u8))
                return InvalidState;
            s_phase13TeardownRun = 1;
            return ManagedOk;
        }
        return InvalidArgument;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase14")]
    internal static uint RunPhase14(uint stage)
    {
        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
            return IsInitialized ? InvalidState : NotInitialized;
        if (stage == 3)
        {
            if (s_phase14Run != 0 || s_phase14TeardownRun != 0 ||
                s_dmaServicesInstalled == 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0)
                return InvalidState;
            if (!ManagedVirtioRngKernelProof.TryStartPhase35Provider())
                return InvalidState;
            s_phase35Mode = 1;
            ManagedE1000Driver.EnablePhase35Mode();
            return ManagedOk;
        }
        if (stage == 4)
        {
            if (s_phase14Run != 0 || s_phase14TeardownRun != 0 ||
                s_dmaServicesInstalled == 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0)
                return InvalidState;
            if (!ManagedVirtioRngKernelProof.TryStartPhase35Provider())
                return InvalidState;
            s_phase39Mode = 1;
            ManagedE1000Driver.EnablePhase39Mode();
            return ManagedOk;
        }
        if (stage == 5)
        {
            if (s_phase14Run != 0 || s_phase14TeardownRun != 0 ||
                s_dmaServicesInstalled == 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0)
                return InvalidState;
            if (!ManagedVirtioRngKernelProof.TryStartPhase35Provider())
                return InvalidState;
            s_phase40Mode = 1;
            ManagedE1000Driver.EnablePhase40Mode();
            return ManagedOk;
        }
        if (stage == 6)
        {
            if (s_phase14Run != 0 || s_phase14TeardownRun != 0 ||
                s_dmaServicesInstalled == 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0)
                return InvalidState;
            if (!ManagedVirtioRngKernelProof.TryStartPhase35Provider())
                return InvalidState;
            s_phase41Mode = 1;
            ManagedE1000Driver.EnablePhase41Mode();
            return ManagedOk;
        }
        if (stage == 1)
        {
            if (s_phase14Run != 0 || s_phase14TeardownRun != 0 ||
                s_dmaServicesInstalled == 0 ||
                !ManagedDeviceResourceRuntimeCatalog.IsInstalled ||
                (s_phase35Mode == 0 && s_phase39Mode == 0 &&
                 s_phase40Mode == 0 && s_phase41Mode == 0 &&
                 ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0))
                return InvalidState;
            ManagedE1000Driver? candidate = ManagedE1000Driver.TryCreate();
            if (candidate == null || !candidate.TryStart())
            {
                KernelLog.Write(
                    "GXOS_NET10:MANAGED_KERNEL_PHASE14_BLOCKED=DRIVER_START\r\n"u8);
                return InvalidState;
            }
            s_phase14Driver = candidate;
            s_phase14Run = 1;
            return ManagedOk;
        }
        if (stage == 2)
        {
            if (s_phase14Run == 0 || s_phase14TeardownRun != 0 ||
                s_phase14Driver == null ||
                s_phase14Driver.State != ManagedE1000DriverState.Running)
                return InvalidState;
            if (!s_phase14Driver.TryStop() ||
                s_phase14Driver.State != ManagedE1000DriverState.Stopped ||
                !ManagedVirtioRngKernelProof.StopPhase35Provider() ||
                ManagedDeviceResourceRuntimeCatalog.ActiveClaimCount != 0)
                return InvalidState;
            s_phase35Mode = 0;
            s_phase39Mode = 0;
            s_phase40Mode = 0;
            s_phase41Mode = 0;
            bool rxProof = s_phase14Driver.RxProofReceived;
            bool phase15RxProof = s_phase14Driver.RxPhase15Received;
            bool phase16Proof = s_phase14Driver.Phase16Passed;
            bool phase17Proof = s_phase14Driver.Phase17Passed;
            bool phase18Proof = s_phase14Driver.Phase18Passed;
            bool phase19Proof = s_phase14Driver.Phase19Passed;
            bool phase20Proof = s_phase14Driver.Phase20Passed;
            bool phase21Proof = s_phase14Driver.Phase21Passed;
            bool phase22Proof = s_phase14Driver.Phase22Passed;
            bool phase23Proof = s_phase14Driver.Phase23Passed;
            bool phase32Proof = s_phase14Driver.Phase32Passed;
            bool phase33Proof = s_phase14Driver.Phase33Passed;
            bool phase34Proof = s_phase14Driver.Phase34Passed;
            bool phase35Proof = s_phase14Driver.Phase35Passed;
            bool phase39Proof = s_phase14Driver.Phase39Passed;
            bool phase40Proof = s_phase14Driver.Phase40Passed;
            bool phase41Proof = s_phase14Driver.Phase41Passed;
            s_phase14TeardownRun = 1;
            if (!KernelLog.Write(rxProof
                    ? "PHASE 14 FIRST MANAGED PCI DRIVER COMPLETE — DMA TX/RX PROVEN\r\n"u8
                    : "PHASE 14 MANAGED PCI TX COMPLETE — RX HARNESS DEFERRED\r\n"u8) ||
                !KernelLog.Write("MANAGED_KERNEL_PHASE14_PASS\r\n"u8) ||
                !KernelLog.Write(phase15RxProof
                    ? "GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_DMA_PROVEN\r\n"u8
                    : "GXOS_NET10:MANAGED_KERNEL_PHASE15_RX_HARNESS_DEFERRED\r\n"u8) ||
                !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE15_ACCOUNTING_RESTORED\r\n"u8) ||
                (phase15RxProof &&
                 !KernelLog.Write("MANAGED_KERNEL_PHASE15_PASS\r\n"u8)) ||
                (phase16Proof &&
                 !KernelLog.Write("MANAGED_KERNEL_PHASE16_PASS\r\n"u8)) ||
                (phase17Proof &&
                 !KernelLog.Write("MANAGED_KERNEL_PHASE17_PASS\r\n"u8)) ||
                (phase18Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE18_PASS\r\n"u8)) ||
                (phase19Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE19_PASS\r\n"u8)) ||
                (phase20Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE20_PASS\r\n"u8)) ||
                (phase21Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE21_PASS\r\n"u8)) ||
                (phase22Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE22_PASS\r\n"u8)) ||
                (phase23Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE23_PASS\r\n"u8)) ||
                (phase32Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE32_PASS\r\n"u8)) ||
                (phase33Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE33_PASS\r\n"u8)) ||
                (phase34Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE34_PASS\r\n"u8)) ||
                (phase35Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE35_PASS\r\n"u8)) ||
                (phase39Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE39_PASS\r\n"u8)) ||
                (phase40Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE40_PASS\r\n"u8)) ||
                (phase41Proof &&
                 !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE41_PASS\r\n"u8)))
                return InvalidState;
            s_phase14Driver = null;
            return ManagedOk;
        }
        return InvalidArgument;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelInstallPciServices")]
    internal static uint InstallPciServices(uint requestedAbiVersion,
                                             nuint servicesAddress)
    {
        GxManagedKernelPciServicesV1 services;
        if (requestedAbiVersion != PciServicesAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (!IsInitialized || s_deviceInventoryInstalled == 0 ||
            s_deviceInventory == null)
        {
            return NotInitialized;
        }
        if (s_pciServicesInstalled != 0) return AlreadyInitialized;
        if (s_lifecycleState != (int)LifecycleState.Started ||
            servicesAddress == 0 ||
            !IsRangeValid(servicesAddress,
                (nuint)GxManagedKernelPciServicesV1.ExpectedSize))
        {
            return s_lifecycleState != (int)LifecycleState.Started
                ? InvalidState : InvalidArgument;
        }
        if (!s_deviceInventory.TryGetDevice(0, out ManagedDevice first) ||
            PciConfiguration.TryRead8(in first, 0, out _))
        {
            return InvalidState;
        }
        if (s_pciReadBeforeInstallNegativeLogged == 0 &&
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PCI_READ_BEFORE_INSTALL_REJECTED\r\n"u8))
        {
            return InvalidState;
        }
        s_pciReadBeforeInstallNegativeLogged = 1;

        services = *(GxManagedKernelPciServicesV1*)servicesAddress;
        if (services.Size != GxManagedKernelPciServicesV1.ExpectedSize ||
            services.AbiVersion != PciServicesAbiVersionV1 ||
            services.ServiceVersion != PciServicesServiceVersionV1 ||
            services.Architecture != ArchitectureX64 ||
            (services.Capabilities & ~PciServicesKnownCapabilities) != 0 ||
            (services.Capabilities & PciServicesRequiredCapabilities) !=
                PciServicesRequiredCapabilities ||
            services.ConfigReadAddress == 0 || services.Reserved0 != 0 ||
            services.Reserved1 != 0 || services.ConfigCommandAddress == 0)
        {
            return InvalidArgument;
        }
        s_pciCapabilities = services.Capabilities;
        s_pciConfigReadAddress = (nuint)services.ConfigReadAddress;
        s_pciConfigCommandAddress = (nuint)services.ConfigCommandAddress;
        s_pciServicesInstalled = 1;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PCI_SERVICES_INSTALLED\r\n"u8))
        {
            s_pciServicesInstalled = 0;
            s_pciCapabilities = 0;
            s_pciConfigReadAddress = 0;
            s_pciConfigCommandAddress = 0;
            return InvalidState;
        }
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase7")]
    internal static uint RunPhase7()
    {
        ManagedDriverRegistry? registry = null;
        ManagedDevice selected = default;
        ManagedDriverBindingInfo selectedBinding = default;
        uint selectedIndex = 0;
        bool selectedFound = false;
        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
        {
            return IsInitialized ? InvalidState : NotInitialized;
        }
        if (s_phase7Run != 0) return AlreadyInitialized;
        if (s_pciServicesInstalled == 0 || s_deviceInventoryInstalled == 0 ||
            s_deviceInventory == null ||
            !PciConfiguration.IsAvailable || !s_deviceInventory.ValidateInvariants())
        {
            return InvalidState;
        }
        if (!ManagedDriverRegistry.TryRunPrecedenceTests(
                Phase4KernelMemoryProvider.Instance))
        {
            return InvalidState;
        }
        registry = ManagedDriverRegistry.Create(Phase4KernelMemoryProvider.Instance);
        if (registry == null) return ResourceExhausted;

        ManagedDriverDefinition hostBridge = new(
            0x7101, 0x48425247, 100,
            new[] { new ManagedDriverMatchRule(
                ManagedDriverMatchType.ExactVendorDevice,
                vendorId: 0x8086, deviceId: 0x29C0) });
        ManagedDriverDefinition displayPolicy = new(
            0x7102, 0x44495350, 10,
            new[] { new ManagedDriverMatchRule(
                ManagedDriverMatchType.Class, classCode: 0x03) });
        if (!registry.TryRegister(in hostBridge) ||
            !registry.TryRegister(in displayPolicy) ||
            !registry.TryFreeze() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DRIVER_REGISTRY_OK\r\n"u8) ||
            !registry.TryBind(s_deviceInventory) ||
            !registry.ValidateInvariants())
        {
            registry.Destroy();
            return InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DRIVER_BIND_OK\r\n"u8))
        {
            registry.Destroy();
            return InvalidState;
        }
        for (uint index = 0; index != s_deviceInventory.DeviceCount; ++index)
        {
            if (!registry.TryGetBinding(index, out ManagedDriverBindingInfo binding) ||
                binding.State != ManagedDriverBindingState.Bound ||
                !s_deviceInventory.TryGetDevice(index, out ManagedDevice device)) continue;
            selectedIndex = index;
            selected = device;
            selectedBinding = binding;
            selectedFound = true;
            break;
        }
        if (!selectedFound || registry.BoundDeviceCount == 0 ||
            registry.UnboundDeviceCount == 0)
        {
            registry.Destroy();
            return InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DRIVER_UNBOUND_OK\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_SELECTED_BDF=0x"u8,
                ((ulong)selected.Segment << 32) | ((ulong)selected.Bus << 24) |
                ((ulong)selected.Device << 16) | ((ulong)selected.Function << 8)) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_SELECTED_SEGMENT=0x"u8,
                                    selected.Segment) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_SELECTED_BUS=0x"u8,
                                    selected.Bus) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_SELECTED_DEVICE_NUMBER=0x"u8,
                                    selected.Device) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_SELECTED_FUNCTION=0x"u8,
                                    selected.Function) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_SELECTED_ID=0x"u8,
                                    selectedBinding.DriverId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_NAME_TOKEN=0x"u8,
                                    selectedBinding.NameToken) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_MATCH_TYPE=0x"u8,
                                    (uint)selectedBinding.MatchType) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_SPECIFICITY=0x"u8,
                                    selectedBinding.Specificity) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_PRIORITY=0x"u8,
                                    unchecked((uint)selectedBinding.Priority)) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_VENDOR=0x"u8,
                                    selected.VendorId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_DEVICE=0x"u8,
                                    selected.DeviceId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_CLASS=0x"u8,
                ((ulong)selected.ClassCode << 16) |
                ((ulong)selected.Subclass << 8) | selected.ProgrammingInterface) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_BOUND_COUNT=0x"u8,
                                    registry.BoundDeviceCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_UNBOUND_COUNT=0x"u8,
                                    registry.UnboundDeviceCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_ARENA_CHUNKS=0x"u8,
                                    registry.Metrics.BackingChunkCount) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_ARENA_PAGES=0x"u8,
                registry.Metrics.TotalBackingBytes / KernelArena.PageSize) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_DRIVER_ARENA_LIVE_ALLOCATIONS=0x"u8,
                                    registry.Metrics.LiveAllocationCount))
        {
            registry.Destroy();
            return InvalidState;
        }
        if (registry.TryRegister(in hostBridge) ||
            registry.TryBind(s_deviceInventory) ||
            registry.TryGetBinding(s_deviceInventory.DeviceCount, out _) ||
            !registry.ValidateInvariants() ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DRIVER_NEGATIVE_TESTS_OK\r\n"u8))
        {
            registry.Destroy();
            return InvalidState;
        }

        if (!PciConfiguration.TryRead16(in selected, 0, out ushort vendor) ||
            !PciConfiguration.TryRead16(in selected, 2, out ushort deviceId) ||
            !PciConfiguration.TryRead8(in selected, 8, out byte revision) ||
            !PciConfiguration.TryRead8(in selected, 0x0B, out byte classCode) ||
            !PciConfiguration.TryRead8(in selected, 0x0A, out byte subclass) ||
            !PciConfiguration.TryRead8(in selected, 0x09, out byte progIf) ||
            !PciConfiguration.TryRead8(in selected, 0x0E, out byte headerType) ||
            vendor != selected.VendorId || deviceId != selected.DeviceId ||
            revision != selected.RevisionId || classCode != selected.ClassCode ||
            subclass != selected.Subclass || progIf != selected.ProgrammingInterface ||
            headerType != selected.HeaderType)
        {
            registry.Destroy();
            return InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PCI_CONFIG_READ_OK\r\n"u8) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PCI_VENDOR_READ=0x"u8,
                                    vendor) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PCI_DEVICE_READ=0x"u8,
                                    deviceId) ||
            !KernelLog.WriteHexLine("GXOS_NET10:MANAGED_KERNEL_PCI_CLASS_READ=0x"u8,
                ((ulong)classCode << 16) | ((ulong)subclass << 8) | progIf) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PCI_CONFIG_MATCH_OK\r\n"u8))
        {
            registry.Destroy();
            return InvalidState;
        }

        bool negativeReads =
            !PciConfiguration.TryReadForValidation(in selected, 1, 2, out _) &&
            !PciConfiguration.TryReadForValidation(in selected, 256, 1, out _) &&
            !PciConfiguration.TryReadForValidation(in selected, 0, 3, out _) &&
            !PciConfiguration.TryReadForValidation(in selected, 0xFD, 4, out _);
        ManagedDevice unknown = default;
        negativeReads = negativeReads &&
            !PciConfiguration.TryRead8(in unknown, 0, out _);
        if (!negativeReads ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PCI_NEGATIVE_TESTS_OK\r\n"u8))
        {
            registry.Destroy();
            return InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DRIVER_PRECEDENCE_OK\r\n"u8))
        {
            registry.Destroy();
            return InvalidState;
        }

        if (!ManagedKernelContract.TryQueryMonotonicTime(out _) ||
            !KernelMemory.TryAllocate(1, 0, out KernelMemoryRegion runtimeRegion))
        {
            registry.Destroy();
            return InvalidState;
        }
        byte* runtimeBytes = (byte*)(nuint)runtimeRegion.VirtualAddress;
        runtimeBytes[0] = 0x7B;
        GC.Collect();
        GC.KeepAlive(s_driverRegistry);
        if (runtimeBytes[0] != 0x7B || !KernelMemory.TryRelease(in runtimeRegion) ||
            !registry.ValidateInvariants() ||
            !PciConfiguration.TryRead16(in selected, 0, out ushort vendorAgain) ||
            vendorAgain != vendor || !registry.IsDeviceBound(selectedIndex))
        {
            registry.Destroy();
            return InvalidState;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DRIVER_RUNTIME_SURVIVAL_OK\r\n"u8))
        {
            registry.Destroy();
            return InvalidState;
        }
        s_driverRegistry = registry;
        s_phase7Run = 1;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase7Accounting")]
    internal static uint RunPhase7Accounting()
    {
        KernelArenaMetrics baselineMetrics;
        KernelArenaMetrics afterMetrics;
        ManagedDriverBindingInfo binding;
        ManagedDevice accountingDevice;
        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
        {
            return IsInitialized ? InvalidState : NotInitialized;
        }
        if (s_phase7Run == 0 || s_phase7AccountingRun != 0 ||
            s_driverRegistry == null || !s_driverRegistry.ValidateInvariants())
        {
            return InvalidState;
        }
        baselineMetrics = s_driverRegistry.Metrics;
        if (!s_driverRegistry.ValidateInvariants() ||
            !s_driverRegistry.TryGetBinding(0, out binding) ||
            !s_deviceInventory.TryGetDevice(0, out accountingDevice) ||
            !PciConfiguration.TryRead16(in accountingDevice, 0, out _) ||
            !s_driverRegistry.ValidateInvariants())
        {
            return InvalidState;
        }
        afterMetrics = s_driverRegistry.Metrics;
        if (baselineMetrics.LiveAllocationCount != afterMetrics.LiveAllocationCount ||
            baselineMetrics.BackingChunkCount != afterMetrics.BackingChunkCount ||
            baselineMetrics.TotalBackingBytes != afterMetrics.TotalBackingBytes ||
            baselineMetrics.LiveRequestedBytes != afterMetrics.LiveRequestedBytes ||
            baselineMetrics.FreeBytes != afterMetrics.FreeBytes ||
            baselineMetrics.LargestFreeBlock != afterMetrics.LargestFreeBlock ||
            binding.State != ManagedDriverBindingState.Bound ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DRIVER_ACCOUNTING_RESTORED\r\n"u8))
        {
            return InvalidState;
        }
        s_phase7AccountingRun = 1;
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_PHASE7_PASS\r\n"u8))
        {
            return InvalidState;
        }
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedQueryDeviceInventorySummary")]
    internal static uint QueryDeviceInventorySummary(uint requestedAbiVersion,
                                                       nuint outputAddress,
                                                       nuint outputCapacity)
    {
        if (requestedAbiVersion != DeviceInventoryAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (!IsInitialized || s_deviceInventoryInstalled == 0 ||
            s_deviceInventory == null)
        {
            return NotInitialized;
        }
        if (outputAddress == 0) return InvalidArgument;
        if (outputCapacity < GxManagedKernelDeviceInventorySummaryV1.ExpectedSize)
        {
            return BufferTooSmall;
        }
        if (!IsRangeValid(outputAddress, outputCapacity)) return InvalidArgument;
        *(GxManagedKernelDeviceInventorySummaryV1*)outputAddress =
            new GxManagedKernelDeviceInventorySummaryV1
            {
                Size = GxManagedKernelDeviceInventorySummaryV1.ExpectedSize,
                AbiVersion = DeviceInventoryAbiVersionV1,
                ServiceVersion = DeviceInventoryServiceVersionV1,
                Architecture = ArchitectureX64,
                DeviceCount = s_deviceInventory.DeviceCount,
                ResourceCount = s_deviceInventory.ResourceCount,
                Capabilities = DeviceInventoryCapabilities,
                Reserved = 0
            };
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedQueryDevice")]
    internal static uint QueryDevice(uint requestedAbiVersion, uint index,
                                      nuint outputAddress, nuint outputCapacity)
    {
        GxManagedKernelDeviceV1 descriptor;
        if (requestedAbiVersion != DeviceInventoryAbiVersionV1)
        {
            return UnsupportedAbi;
        }
        if (!IsInitialized || s_deviceInventoryInstalled == 0 ||
            s_deviceInventory == null)
        {
            return NotInitialized;
        }
        if (index >= s_deviceInventory.DeviceCount) return OutOfRange;
        if (outputAddress == 0) return InvalidArgument;
        if (outputCapacity < GxManagedKernelDeviceV1.ExpectedSize)
        {
            return BufferTooSmall;
        }
        if (!IsRangeValid(outputAddress, outputCapacity) ||
            !s_deviceInventory.TryGetDescriptor(index, out descriptor))
        {
            return InvalidArgument;
        }
        *(GxManagedKernelDeviceV1*)outputAddress = descriptor;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase6")]
    internal static uint RunPhase6()
    {
        ManagedDeviceInventory? testInventory = null;
        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
        {
            return IsInitialized ? InvalidState : NotInitialized;
        }
        if (s_phase6Run != 0) return AlreadyInitialized;
        if (s_deviceInventoryInstalled == 0 || s_deviceInventory == null)
        {
            return InvalidState;
        }
        if (!s_deviceInventory.TryGetDevice(s_deviceInventory.DeviceCount,
                                            out _) &&
            s_deviceInventory.TryCreateTestCopy(
                Phase4KernelMemoryProvider.Instance, out testInventory) &&
            testInventory != null && testInventory.ValidateInvariants() &&
            testInventory.Destroy() && s_deviceInventory.ValidateInvariants() &&
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_NEGATIVE_TESTS_OK\r\n"u8) &&
            KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_DEVICE_TEARDOWN_OK\r\n"u8))
        {
            s_phase6Run = 1;
            return ManagedOk;
        }
        if (testInventory != null && !testInventory.IsDestroyed)
        {
            testInventory.Destroy();
        }
        return InvalidState;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase4")]
    internal static uint RunPhase4()
    {
        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
        {
            return IsInitialized ? InvalidState : NotInitialized;
        }
        if (s_phase4Run != 0)
        {
            return AlreadyInitialized;
        }
        if (!KernelMemory.RunProof())
        {
            return InvalidState;
        }
        s_phase4Run = 1;
        return ManagedOk;
    }

    [UnmanagedCallersOnly(EntryPoint = "GxManagedKernelRunPhase5")]
    internal static uint RunPhase5(uint stage)
    {
        if (!IsInitialized || s_lifecycleState != (int)LifecycleState.Started)
        {
            return IsInitialized ? InvalidState : NotInitialized;
        }
        if (s_phase5Run != 0)
        {
            return AlreadyInitialized;
        }
        if (!MemoryServicesInstalled || stage == 0 || stage > 5)
        {
            return InvalidState;
        }
        uint status = KernelArenaProof.RunStage(stage);
        if (status == ManagedOk && stage == 5)
        {
            s_phase5Run = 1;
        }
        return status;
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

        if (!IsInitialized)
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
        if (!IsInitialized || s_bootResourcesPublished == 0)
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
        if (!IsInitialized || s_bootResourcesPublished == 0)
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

internal static unsafe class KernelMemory
{
    private const uint MemoryAbiVersionV1 = 1;

    internal static bool IsInstalled =>
        ManagedKernelContract.MemoryServicesInstalled;

    internal static bool IsValidRegion(in KernelMemoryRegion region)
    {
        return region.AllocationId != 0 && region.VirtualAddress != 0 &&
               region.ByteLength != 0 && region.PageCount != 0 &&
               region.PageSize == ManagedKernelContract.MemoryPageSize &&
               region.Flags == 0 &&
               region.PageCount <= ulong.MaxValue / region.PageSize &&
               region.ByteLength == region.PageCount * region.PageSize &&
               region.ByteLength <= (ulong)nuint.MaxValue &&
               (nuint)region.VirtualAddress <=
                   nuint.MaxValue - (nuint)region.ByteLength;
    }

    private static byte Pattern(ulong index, byte seed)
    {
        ulong value = unchecked(index * 0x9E3779B97F4A7C15UL +
                                 ((ulong)seed << 32) + 0xD1B54A32D192ED03UL);
        return (byte)(value ^ (value >> 17) ^ (value >> 41));
    }

    private static void Fill(in KernelMemoryRegion region, byte seed)
    {
        byte* address = (byte*)(nuint)region.VirtualAddress;
        ulong index = 0;
        while (index != region.ByteLength)
        {
            address[(nuint)index] = Pattern(index, seed);
            index++;
        }
    }

    private static bool Verify(in KernelMemoryRegion region, byte seed)
    {
        byte* address = (byte*)(nuint)region.VirtualAddress;
        ulong index = 0;
        while (index != region.ByteLength)
        {
            if (address[(nuint)index] != Pattern(index, seed)) return false;
            index++;
        }
        return true;
    }

    internal static bool TryAllocate(ulong pageCount, uint flags,
                                     out KernelMemoryRegion region)
    {
        GxManagedKernelMemoryAllocationV1 result = default;
        region = default;
        if (!IsInstalled || !ManagedKernelContract.IsStarted ||
            pageCount == 0 || pageCount >
                ManagedKernelContract.MemoryMaxPagesPerAllocation ||
            flags != 0 || ManagedKernelContract.MemoryAllocatePagesAddress == 0)
        {
            return false;
        }
        delegate* unmanaged<ulong, uint, nuint, nuint, uint> callback =
            (delegate* unmanaged<ulong, uint, nuint, nuint, uint>)
                ManagedKernelContract.MemoryAllocatePagesAddress;
        uint status;
        GxManagedKernelMemoryAllocationV1* resultAddress = &result;
        status = callback(pageCount, flags, (nuint)resultAddress,
                          (nuint)GxManagedKernelMemoryAllocationV1.ExpectedSize);
        if (status != ManagedKernelContract.ManagedOk ||
            result.Size != GxManagedKernelMemoryAllocationV1.ExpectedSize ||
            result.AbiVersion != MemoryAbiVersionV1 || result.PageCount != pageCount ||
            result.PageSize != ManagedKernelContract.MemoryPageSize ||
            result.Flags != flags || result.Reserved != 0)
        {
            return false;
        }
        region.AllocationId = result.AllocationId;
        region.VirtualAddress = result.VirtualAddress;
        region.ByteLength = result.ByteLength;
        region.PageCount = result.PageCount;
        region.PageSize = result.PageSize;
        region.Flags = result.Flags;
        return IsValidRegion(in region);
    }

    private static uint CallRelease(in GxManagedKernelMemoryReleaseV1 request)
    {
        if (!IsInstalled || ManagedKernelContract.MemoryReleasePagesAddress == 0)
        {
            return ManagedKernelContract.InvalidState;
        }
        delegate* unmanaged<nuint, nuint, uint> callback =
            (delegate* unmanaged<nuint, nuint, uint>)
                ManagedKernelContract.MemoryReleasePagesAddress;
        GxManagedKernelMemoryReleaseV1 local = request;
        GxManagedKernelMemoryReleaseV1* requestAddress = &local;
        return callback((nuint)requestAddress,
                        (nuint)GxManagedKernelMemoryReleaseV1.ExpectedSize);
    }

    private static GxManagedKernelMemoryReleaseV1 ReleaseRequest(
        in KernelMemoryRegion region)
    {
        return new GxManagedKernelMemoryReleaseV1
        {
            Size = GxManagedKernelMemoryReleaseV1.ExpectedSize,
            AbiVersion = MemoryAbiVersionV1,
            AllocationId = region.AllocationId,
            VirtualAddress = region.VirtualAddress,
            ByteLength = region.ByteLength,
            PageCount = region.PageCount,
            PageSize = region.PageSize,
            Flags = region.Flags,
            Reserved = 0
        };
    }

    internal static bool TryRelease(in KernelMemoryRegion region)
    {
        GxManagedKernelMemoryReleaseV1 request;
        if (!IsValidRegion(in region)) return false;
        request = ReleaseRequest(in region);
        return CallRelease(in request) ==
            ManagedKernelContract.ManagedOk;
    }

    private static bool TryWrongReleases(in KernelMemoryRegion region)
    {
        GxManagedKernelMemoryReleaseV1 request = ReleaseRequest(in region);
        request.VirtualAddress++;
        if (CallRelease(in request) != ManagedKernelContract.OwnershipMismatch) return false;
        request = ReleaseRequest(in region);
        request.PageCount++;
        if (CallRelease(in request) != ManagedKernelContract.OwnershipMismatch) return false;
        request = ReleaseRequest(in region);
        request.AllocationId++;
        if (CallRelease(in request) != ManagedKernelContract.NotFound) return false;
        request = ReleaseRequest(in region);
        request.Size = GxManagedKernelMemoryReleaseV1.ExpectedSize - 1;
        if (CallRelease(in request) != ManagedKernelContract.InvalidArgument) return false;
        return true;
    }

    internal static bool RunProof()
    {
        KernelMemoryRegion first;
        KernelMemoryRegion second;
        byte[] gcActivity;
        if (!TryAllocate(4, 0, out first) || !IsValidRegion(in first)) return false;
        Fill(in first, 0x31);
        if (!Verify(in first, 0x31)) return false;
        if (!TryWrongReleases(in first)) return false;
        if (!ManagedKernelContract.TryQueryMonotonicTime(out _)) return false;
        gcActivity = new byte[4096];
        gcActivity[0] = 0x5A;
        GC.Collect();
        GC.KeepAlive(gcActivity);
        if (!Verify(in first, 0x31)) return false;
        byte* firstAddress = (byte*)(nuint)first.VirtualAddress;
        firstAddress[0] = 0xA1;
        firstAddress[(nuint)(first.PageSize - 1)] = 0xA2;
        firstAddress[(nuint)first.PageSize] = 0xA3;
        firstAddress[(nuint)(first.ByteLength - 1)] = 0xA4;
        if (firstAddress[0] != 0xA1 ||
            firstAddress[(nuint)(first.PageSize - 1)] != 0xA2 ||
            firstAddress[(nuint)first.PageSize] != 0xA3 ||
            firstAddress[(nuint)(first.ByteLength - 1)] != 0xA4)
        {
            return false;
        }
        Fill(in first, 0x31);
        if (!Verify(in first, 0x31) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MEMORY_PATTERN_OK\r\n"u8) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MEMORY_RUNTIME_SURVIVAL_OK\r\n"u8))
        {
            return false;
        }
        if (!TryAllocate(3, 0, out second) || !IsValidRegion(in second) ||
            second.AllocationId == first.AllocationId ||
            second.VirtualAddress == first.VirtualAddress ||
            second.VirtualAddress > ulong.MaxValue - second.ByteLength ||
            first.VirtualAddress > ulong.MaxValue - first.ByteLength ||
            second.VirtualAddress < first.VirtualAddress + first.ByteLength &&
                first.VirtualAddress < second.VirtualAddress + second.ByteLength)
        {
            return false;
        }
        Fill(in second, 0x72);
        if (!Verify(in second, 0x72) || !Verify(in first, 0x31) ||
            !TryRelease(in second) || !Verify(in first, 0x31) ||
            !KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MEMORY_MULTI_ALLOC_OK\r\n"u8) ||
            !TryRelease(in first) || TryRelease(in first))
        {
            return false;
        }
        if (!KernelLog.Write("GXOS_NET10:MANAGED_KERNEL_MEMORY_RELEASE_OK\r\n"u8))
        {
            return false;
        }
        return true;
    }
}

internal sealed class Phase4KernelMemoryProvider : IKernelMemoryProvider
{
    internal static readonly Phase4KernelMemoryProvider Instance =
        new Phase4KernelMemoryProvider();

    public bool IsAvailable => ManagedKernelContract.IsStarted &&
                               KernelMemory.IsInstalled;

    public bool TryAllocate(ulong pageCount, uint flags,
                            out KernelMemoryRegion region)
    {
        return KernelMemory.TryAllocate(pageCount, flags, out region);
    }

    public bool IsValidRegion(in KernelMemoryRegion region)
    {
        return KernelMemory.IsValidRegion(in region);
    }

    public bool TryRelease(in KernelMemoryRegion region)
    {
        return KernelMemory.TryRelease(in region);
    }
}

internal static unsafe class KernelLog
{
    internal static ReadOnlySpan<byte> ManagedStartLog =>
        "GXOS_NET10:MANAGED_KERNEL_HOST_LOG_FROM_MANAGED\r\n"u8;
    internal static ReadOnlySpan<byte> ManagedHostLogCallOk =>
        "GXOS_NET10:MANAGED_KERNEL_HOST_LOG_CALL_OK\r\n"u8;
    internal static ReadOnlySpan<byte> ManagedMonotonicTimeOk =>
        "GXOS_NET10:MANAGED_KERNEL_MONOTONIC_TIME_OK\r\n"u8;

    internal static bool Write(ReadOnlySpan<byte> utf8)
    {
        return ManagedKernelContract.TryInvokeHostLog(utf8);
    }

    internal static bool WriteHexLine(ReadOnlySpan<byte> prefix, ulong value)
    {
        Span<byte> buffer = stackalloc byte[128];
        ReadOnlySpan<byte> digits = "0123456789ABCDEF"u8;
        if (prefix.Length > 108) return false;
        prefix.CopyTo(buffer);
        for (int index = 0; index != 16; ++index)
        {
            int shift = (15 - index) * 4;
            buffer[prefix.Length + index] =
                digits[(int)((value >> shift) & 0xFUL)];
        }
        buffer[prefix.Length + 16] = (byte)'\r';
        buffer[prefix.Length + 17] = (byte)'\n';
        return Write(buffer[..(prefix.Length + 18)]);
    }
}

public static unsafe class ManagedKernelEntry
{
    private static int s_bootstrapMarkerEmitted;

    private static ReadOnlySpan<byte> BootstrapMarker =>
        "GXOS_NET10:MANAGED_KERNEL_BOOTSTRAP_OK\r\n"u8;

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

        if (s_bootstrapMarkerEmitted != 0)
        {
            return -4;
        }

        if (s_bootstrapMarkerEmitted == 0)
        {
            WriteSerial(bootInfo, BootstrapMarker);
            s_bootstrapMarkerEmitted = 1;
        }
        return 0;
    }

    // The freestanding loader calls the exported ManagedMain entry directly.
    public static int Main() => 0;
}

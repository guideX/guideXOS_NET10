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
               sizeof(GxManagedKernelMonotonicTimeV1) == 40 &&
               sizeof(GxManagedKernelMemoryServicesV1) == 72 &&
               sizeof(GxManagedKernelMemoryAllocationV1) == 56 &&
               sizeof(GxManagedKernelMemoryReleaseV1) == 56 &&
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
               Marshal.OffsetOf<GxManagedKernelMemoryReleaseV1>(nameof(GxManagedKernelMemoryReleaseV1.Flags)).ToInt32() == 48;
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
    private static int s_memoryServicesInstalled;
    private static ulong s_memoryCapabilities;
    private static ulong s_memoryPageSize;
    private static uint s_memoryMaxPagesPerAllocation;
    private static uint s_memoryMaxLiveAllocations;
    private static ulong s_memoryMaxTotalPages;
    private static nuint s_memoryAllocatePagesAddress;
    private static nuint s_memoryReleasePagesAddress;
    private static int s_phase4Run;

    internal static bool IsStarted =>
        s_lifecycleState == (int)LifecycleState.Started;
    internal static bool MemoryServicesInstalled => s_memoryServicesInstalled != 0;
    internal static nuint MemoryAllocatePagesAddress => s_memoryAllocatePagesAddress;
    internal static nuint MemoryReleasePagesAddress => s_memoryReleasePagesAddress;

    private static bool IsInitialized =>
        s_lifecycleState != (int)LifecycleState.BootstrapAvailable;

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
        if (s_lifecycleState != (int)LifecycleState.Ready ||
            s_bootResourcesPublished == 0 || s_hostServicesInstalled == 0 ||
            s_memoryServicesInstalled == 0 ||
            (s_hostCapabilities & RequiredHostServicesCapabilities) !=
                RequiredHostServicesCapabilities ||
            !PublishedBootResourcesRemainStable())
        {
            return InvalidState;
        }

        hasMonotonicTime =
            (s_hostCapabilities & GxManagedKernelHostServicesV1.CapabilityMonotonicTime) != 0;
        if (hasMonotonicTime)
        {
            if (!TryQueryMonotonicTime(out firstTime)) return InvalidState;
            index = 0;
            while (index != 1024)
            {
                index++;
            }
            if (!TryQueryMonotonicTime(out secondTime) ||
                secondTime.Ticks < firstTime.Ticks ||
                secondTime.FrequencyHz != firstTime.FrequencyHz)
            {
                return InvalidState;
            }
        }

        if (!KernelLog.Write(KernelLog.ManagedStartLog) ||
            !KernelLog.Write(KernelLog.ManagedHostLogCallOk) ||
            (hasMonotonicTime && !KernelLog.Write(KernelLog.ManagedMonotonicTimeOk)))
        {
            return InvalidState;
        }
        s_lifecycleState = (int)LifecycleState.Started;
        return ManagedOk;
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

internal struct KernelMemoryRegion
{
    internal ulong AllocationId;
    internal ulong VirtualAddress;
    internal ulong ByteLength;
    internal ulong PageCount;
    internal ulong PageSize;
    internal uint Flags;
}

internal static unsafe class KernelMemory
{
    private const uint MemoryAbiVersionV1 = 1;

    private static bool IsInstalled =>
        ManagedKernelContract.MemoryServicesInstalled;

    private static bool IsValidRegion(in KernelMemoryRegion region)
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

    private static bool TryAllocate(ulong pageCount, uint flags,
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

    private static bool TryRelease(in KernelMemoryRegion region)
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

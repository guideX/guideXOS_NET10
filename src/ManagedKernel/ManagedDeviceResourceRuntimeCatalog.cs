using System;

namespace GuideXOS.Net10.ManagedKernel;

/* The boot-time catalog is intentionally static and bounded. Native owns the
   immutable descriptor bytes; managed owns only the publication identity and
   fixed claim slots. No managed heap object or arbitrary physical mapping is
   created by this runtime catalog. */
internal static unsafe class ManagedDeviceResourceRuntimeCatalog
{
    internal const uint MaxResources = 64;
    internal const uint MaxClaims = 16;
    internal const uint MaxMappings = 8;

    private struct ClaimTable
    {
        internal fixed uint Owners[64];
        internal fixed ulong NativeHandles[64];
        internal fixed uint MappingCounts[64];
        internal fixed ulong MappingHandles[512];
    }

    private static nuint s_descriptorAddress;
    private static uint s_resourceCount;
    private static ulong s_catalogIdentity;
    private static ClaimTable s_claims;
    private static int s_installed;

    internal static bool IsInstalled => s_installed != 0;
    internal static uint ResourceCount => s_installed == 0 ? 0U : s_resourceCount;
    internal static uint ActiveClaimCount
    {
        get
        {
            if (s_installed == 0) return 0;
            uint count = 0;
            for (uint index = 0; index != s_resourceCount; ++index)
            {
                if (s_claims.Owners[index] != 0) count++;
            }
            return count;
        }
    }

    internal static uint ActiveClaimCountForDriver(uint driverId)
    {
        if (s_installed == 0 || driverId == 0) return 0;
        uint count = 0;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (s_claims.Owners[index] == driverId) count++;
        }
        return count;
    }

    internal static bool TryInstallFromPublication(
        IKernelMemoryProvider provider, nuint publicationAddress)
    {
        if (s_installed != 0 || provider == null || !provider.IsAvailable ||
            publicationAddress == 0 ||
            !ManagedKernelContract.IsRangeValid(
                publicationAddress,
                GxManagedKernelDeviceResourcePublicationV1.ExpectedSize))
        {
            return false;
        }

        GxManagedKernelDeviceResourcePublicationV1* publication =
            (GxManagedKernelDeviceResourcePublicationV1*)publicationAddress;
        if (publication->Size !=
                GxManagedKernelDeviceResourcePublicationV1.ExpectedSize ||
            publication->AbiVersion != 1 || publication->SummaryAddress == 0 ||
            publication->DescriptorAddress == 0 || publication->DescriptorCount == 0 ||
            publication->DescriptorCount > MaxResources ||
            publication->DescriptorSize != GxManagedKernelDeviceResourceV1.ExpectedSize ||
            publication->Reserved != 0)
        {
            return false;
        }

        ulong expectedBytes = (ulong)publication->DescriptorCount *
                              GxManagedKernelDeviceResourceV1.ExpectedSize;
        if (expectedBytes != publication->DescriptorByteLength ||
            !ManagedKernelContract.IsRangeValid(
                publication->SummaryAddress,
                GxManagedKernelDeviceResourceSummaryV1.ExpectedSize) ||
            !ManagedKernelContract.IsRangeValid(
                publication->DescriptorAddress,
                (nuint)publication->DescriptorByteLength))
        {
            return false;
        }

        GxManagedKernelDeviceResourceSummaryV1* summary =
            (GxManagedKernelDeviceResourceSummaryV1*)publication->SummaryAddress;
        ulong knownCapabilities =
            GxManagedKernelDeviceResourceSummaryV1.CapabilitySummary |
            GxManagedKernelDeviceResourceSummaryV1.CapabilityDescriptors |
            GxManagedKernelDeviceResourceSummaryV1.CapabilityImmutablePublication |
            GxManagedKernelDeviceResourceSummaryV1.CapabilityClaimPolicy;
        if (summary->Size != GxManagedKernelDeviceResourceSummaryV1.ExpectedSize ||
            summary->AbiVersion != 1 || summary->ServiceVersion != 1 ||
            summary->Architecture != 0x8664 ||
            summary->ResourceCount != publication->DescriptorCount ||
            summary->MaxClaims != MaxClaims ||
            summary->Capabilities != knownCapabilities || summary->Reserved != 0)
        {
            return false;
        }

        GxManagedKernelDeviceResourceV1* descriptors =
            (GxManagedKernelDeviceResourceV1*)publication->DescriptorAddress;
        if (!ValidateDescriptors(descriptors, publication->DescriptorCount))
        {
            return false;
        }
        for (uint index = 0; index != MaxResources; ++index)
        {
            s_claims.Owners[index] = 0;
            s_claims.NativeHandles[index] = 0;
            s_claims.MappingCounts[index] = 0;
        }
        for (uint index = 0; index != MaxResources * MaxMappings; ++index)
            s_claims.MappingHandles[index] = 0;
        s_descriptorAddress = publication->DescriptorAddress;
        s_resourceCount = publication->DescriptorCount;
        s_catalogIdentity = 1;
        s_installed = 1;
        return true;
    }

    internal static bool TryGetResource(uint index,
                                        out ManagedDeviceResource resource)
    {
        resource = default;
        if (s_installed == 0 || index >= s_resourceCount) return false;
        GxManagedKernelDeviceResourceV1* descriptors = Descriptors;
        resource = new ManagedDeviceResource(in descriptors[index], s_catalogIdentity);
        return true;
    }

    internal static bool TryCopyDescriptor(
        uint index, GxManagedKernelDeviceResourceV1* output)
    {
        if (s_installed == 0 || output == null || index >= s_resourceCount)
            return false;
        *output = Descriptors[index];
        return true;
    }

    internal static bool TryFindById(ulong resourceId,
                                     out ManagedDeviceResource resource)
    {
        resource = default;
        if (s_installed == 0 || resourceId == 0) return false;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (Descriptors[index].ResourceId != resourceId) continue;
            resource = new ManagedDeviceResource(in Descriptors[index],
                                                  s_catalogIdentity);
            return true;
        }
        return false;
    }

    internal static bool TryFindByOwner(uint deviceKind, uint deviceId,
                                        uint resourceIndex,
                                        out ManagedDeviceResource resource)
    {
        resource = default;
        if (s_installed == 0) return false;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            GxManagedKernelDeviceResourceV1 descriptor = Descriptors[index];
            if (descriptor.OwnerDeviceKind != deviceKind ||
                descriptor.OwnerDeviceId != deviceId ||
                descriptor.ResourceIndex != resourceIndex) continue;
            resource = new ManagedDeviceResource(in descriptor, s_catalogIdentity);
            return true;
        }
        return false;
    }

    internal static bool TryClaim(in ManagedDeviceResource resource, uint driverId,
                                  uint expectedOwnerKind, uint expectedOwnerId)
    {
        if (s_installed == 0 || driverId == 0 ||
            !resource.HasCatalogOwnership ||
            resource.CatalogIdentity != s_catalogIdentity ||
            !resource.HasOwner(expectedOwnerKind, expectedOwnerId) ||
            !TryFindById(resource.ResourceId, out ManagedDeviceResource current))
        {
            return false;
        }
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (Descriptors[index].ResourceId != current.ResourceId) continue;
            if (s_claims.Owners[index] != 0 || ActiveClaimCount >= MaxClaims)
                return false;
            ulong nativeHandle = 0;
            if (current.ResourceType ==
                    GxManagedKernelDeviceResourceV1.ResourceTypeMmio &&
                !ManagedKernelContract.TryMmioClaim(
                    current.ResourceId, driverId, expectedOwnerKind,
                    expectedOwnerId, out nativeHandle)) return false;
            s_claims.Owners[index] = driverId;
            s_claims.NativeHandles[index] = nativeHandle;
            s_claims.MappingCounts[index] = 0;
            return true;
        }
        return false;
    }

    internal static bool TryGetNativeClaimHandle(in ManagedDeviceResource resource,
                                                 uint driverId,
                                                 out ulong nativeHandle)
    {
        nativeHandle = 0;
        if (s_installed == 0 || driverId == 0 ||
            !resource.HasCatalogOwnership ||
            resource.CatalogIdentity != s_catalogIdentity) return false;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (Descriptors[index].ResourceId != resource.ResourceId ||
                s_claims.Owners[index] != driverId ||
                s_claims.NativeHandles[index] == 0) continue;
            nativeHandle = s_claims.NativeHandles[index];
            return true;
        }
        return false;
    }

    internal static bool TryRelease(in ManagedDeviceResource resource,
                                    uint driverId)
    {
        if (s_installed == 0 || driverId == 0 ||
            !resource.HasCatalogOwnership ||
            resource.CatalogIdentity != s_catalogIdentity) return false;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (Descriptors[index].ResourceId != resource.ResourceId ||
                s_claims.Owners[index] != driverId) continue;
            if (s_claims.MappingCounts[index] != 0) return false;
            if (Descriptors[index].ResourceType ==
                    GxManagedKernelDeviceResourceV1.ResourceTypeMmio &&
                !ManagedKernelContract.TryMmioRelease(
                    s_claims.NativeHandles[index], driverId)) return false;
            s_claims.Owners[index] = 0;
            s_claims.NativeHandles[index] = 0;
            s_claims.MappingCounts[index] = 0;
            for (uint slot = 0; slot != MaxMappings; ++slot)
                s_claims.MappingHandles[index * MaxMappings + slot] = 0;
            return true;
        }
        return false;
    }

    internal static bool TryMap(in ManagedDeviceResource resource, uint driverId,
                                ulong offset, ulong length, uint access,
                                out ManagedMmioMapping? mapping)
    {
        mapping = null;
        if (!TryMapHandle(in resource, driverId, offset, length, access,
                          out ulong mappingHandle)) return false;
        mapping = new ManagedMmioMapping(resource.ResourceId, driverId,
                                          mappingHandle, length, access);
        return true;
    }

    /* Scalar-handle variant for NativeAOT drivers that must not put a newly
       allocated mapping wrapper into their long-lived transport state. */
    internal static bool TryMapHandle(in ManagedDeviceResource resource,
                                      uint driverId, ulong offset, ulong length,
                                      uint access, out ulong mappingHandle)
    {
        mappingHandle = 0;
        if (s_installed == 0 || driverId == 0 || length == 0 ||
            (access != 1 && access != 3) ||
            !resource.HasCatalogOwnership || resource.CatalogIdentity != s_catalogIdentity ||
            resource.ResourceType != GxManagedKernelDeviceResourceV1.ResourceTypeMmio ||
            resource.PhysicalBase > ulong.MaxValue - resource.Length ||
            offset > resource.Length || length > resource.Length - offset)
        {
            return false;
        }
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (Descriptors[index].ResourceId != resource.ResourceId ||
                s_claims.Owners[index] != driverId ||
                s_claims.NativeHandles[index] == 0 ||
                s_claims.MappingCounts[index] >= MaxMappings) continue;
            if (!ManagedKernelContract.TryMmioMap(
                    s_claims.NativeHandles[index], driverId, offset, length,
                    access, resource.ResourceId, out mappingHandle)) return false;
            s_claims.MappingHandles[index * MaxMappings +
                                     s_claims.MappingCounts[index]] = mappingHandle;
            s_claims.MappingCounts[index]++;
            return true;
        }
        return false;
    }

    internal static bool TryUnmap(ulong resourceId, uint driverId,
                                  ulong mappingHandle)
    {
        uint claimIndex = MaxResources;
        uint mappingIndex = MaxMappings;
        if (s_installed == 0 || resourceId == 0 || driverId == 0 ||
            mappingHandle == 0) return false;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (Descriptors[index].ResourceId == resourceId &&
                s_claims.Owners[index] == driverId &&
                s_claims.MappingCounts[index] != 0)
            {
                claimIndex = index;
                for (uint slot = 0; slot != MaxMappings; ++slot)
                {
                    if (s_claims.MappingHandles[index * MaxMappings + slot] ==
                        mappingHandle)
                    {
                        mappingIndex = slot;
                        break;
                    }
                }
                break;
            }
        }
        if (claimIndex == MaxResources || mappingIndex == MaxMappings ||
            !ManagedKernelContract.TryMmioUnmap(mappingHandle, driverId))
            return false;
        s_claims.MappingHandles[claimIndex * MaxMappings + mappingIndex] = 0;
        s_claims.MappingCounts[claimIndex]--;
        return true;
    }

    internal static bool TryAbortDriver(uint driverId)
    {
        if (s_installed == 0 || driverId == 0) return false;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (s_claims.Owners[index] != driverId) continue;
            while (s_claims.MappingCounts[index] != 0)
            {
                uint slot = MaxMappings;
                for (uint candidate = 0; candidate != MaxMappings; ++candidate)
                {
                    if (s_claims.MappingHandles[index * MaxMappings + candidate] != 0)
                    {
                        slot = candidate;
                        break;
                    }
                }
                if (slot == MaxMappings || !ManagedKernelContract.TryMmioUnmap(
                        s_claims.MappingHandles[index * MaxMappings + slot],
                        driverId)) return false;
                s_claims.MappingHandles[index * MaxMappings + slot] = 0;
                s_claims.MappingCounts[index]--;
            }
            if (Descriptors[index].ResourceType ==
                    GxManagedKernelDeviceResourceV1.ResourceTypeMmio &&
                !ManagedKernelContract.TryMmioRelease(
                    s_claims.NativeHandles[index], driverId)) return false;
            s_claims.Owners[index] = 0;
            s_claims.NativeHandles[index] = 0;
        }
        return true;
    }

    internal static bool ValidateInvariants()
    {
        if (s_installed == 0 || s_descriptorAddress == 0 ||
            s_resourceCount == 0 || s_resourceCount > MaxResources ||
            s_catalogIdentity == 0) return false;
        uint claims = 0;
        for (uint index = 0; index != s_resourceCount; ++index)
        {
            if (!ValidateDescriptor(in Descriptors[index])) return false;
            if (s_claims.Owners[index] != 0) claims++;
            if (s_claims.MappingCounts[index] > MaxMappings) return false;
            for (uint other = index + 1; other != s_resourceCount; ++other)
            {
                if (Descriptors[index].ResourceId == Descriptors[other].ResourceId ||
                    RangesOverlap(in Descriptors[index], in Descriptors[other]))
                    return false;
            }
        }
        return claims <= MaxClaims;
    }

    internal static bool TryRunRuntimeSurvival()
    {
        if (!TryGetResource(0, out ManagedDeviceResource first) ||
            !TryFindById(first.ResourceId, out ManagedDeviceResource again))
            return false;
        return again.PhysicalBase == first.PhysicalBase && ValidateInvariants();
    }

    internal static bool Destroy()
    {
        if (s_installed == 0 || ActiveClaimCount != 0) return false;
        for (uint index = 0; index != MaxResources; ++index)
        {
            s_claims.Owners[index] = 0;
            s_claims.NativeHandles[index] = 0;
            s_claims.MappingCounts[index] = 0;
        }
        for (uint index = 0; index != MaxResources * MaxMappings; ++index)
            s_claims.MappingHandles[index] = 0;
        s_descriptorAddress = 0;
        s_resourceCount = 0;
        s_catalogIdentity = 0;
        s_installed = 0;
        return true;
    }

    private static GxManagedKernelDeviceResourceV1* Descriptors =>
        (GxManagedKernelDeviceResourceV1*)s_descriptorAddress;

    private static bool ValidateDescriptors(
        GxManagedKernelDeviceResourceV1* descriptors, uint resourceCount)
    {
        for (uint index = 0; index != resourceCount; ++index)
        {
            if (!ValidateDescriptor(in descriptors[index])) return false;
            for (uint prior = 0; prior != index; ++prior)
            {
                if (descriptors[prior].ResourceId == descriptors[index].ResourceId ||
                    RangesOverlap(in descriptors[prior], in descriptors[index]))
                    return false;
            }
        }
        return true;
    }

    private static bool ValidateDescriptor(
        in GxManagedKernelDeviceResourceV1 descriptor)
    {
        ulong end;
        uint knownFlags = GxManagedKernelDeviceResourceV1.FlagReadable |
                          GxManagedKernelDeviceResourceV1.FlagWritable |
                          GxManagedKernelDeviceResourceV1.FlagIoPort |
                          GxManagedKernelDeviceResourceV1.FlagMemory |
                          GxManagedKernelDeviceResourceV1.FlagPrefetchable |
                          GxManagedKernelDeviceResourceV1.FlagAddress64 |
                          GxManagedKernelDeviceResourceV1.FlagCacheUncached |
                          GxManagedKernelDeviceResourceV1.FlagPlatform |
                          GxManagedKernelDeviceResourceV1.FlagPciAssigned;
        if (descriptor.Size != GxManagedKernelDeviceResourceV1.ExpectedSize ||
            descriptor.AbiVersion != 1 || descriptor.ResourceId == 0 ||
            descriptor.OwnerDeviceKind == 0 || descriptor.ResourceType == 0 ||
            descriptor.ResourceType >
                GxManagedKernelDeviceResourceV1.ResourceTypeInterrupt ||
            descriptor.Length == 0 || descriptor.Alignment == 0 ||
            (descriptor.Alignment & (descriptor.Alignment - 1)) != 0 ||
            (descriptor.Flags & ~knownFlags) != 0 ||
            descriptor.ReservedLocation != 0 || descriptor.Reserved0 != 0 ||
            descriptor.Reserved1 != 0 ||
            descriptor.PhysicalBase > ulong.MaxValue - descriptor.Length)
            return false;
        end = descriptor.PhysicalBase + descriptor.Length;
        if (end <= descriptor.PhysicalBase) return false;
        if (descriptor.ResourceType ==
                GxManagedKernelDeviceResourceV1.ResourceTypeIoPort &&
            ((descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagIoPort) == 0 ||
             descriptor.PhysicalBase > ushort.MaxValue || end > 0x10000))
            return false;
        if ((descriptor.ResourceType ==
                 GxManagedKernelDeviceResourceV1.ResourceTypeMmio ||
             descriptor.ResourceType ==
                 GxManagedKernelDeviceResourceV1.ResourceTypePlatformMemory) &&
            (descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagMemory) == 0)
            return false;
        if ((descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagPrefetchable) != 0 &&
            (descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagMemory) == 0)
            return false;
        return true;
    }

    private static bool RangesOverlap(
        in GxManagedKernelDeviceResourceV1 left,
        in GxManagedKernelDeviceResourceV1 right)
    {
        if (left.ResourceType != right.ResourceType) return false;
        ulong leftEnd = left.PhysicalBase + left.Length;
        ulong rightEnd = right.PhysicalBase + right.Length;
        return left.PhysicalBase < rightEnd && right.PhysicalBase < leftEnd;
    }
}

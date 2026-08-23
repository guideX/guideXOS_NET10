using System;

namespace GuideXOS.Net10.ManagedKernel;

internal readonly struct ManagedDeviceResource
{
    private readonly GxManagedKernelDeviceResourceV1 _descriptor;
    private readonly ulong _catalogIdentity;

    internal ManagedDeviceResource(in GxManagedKernelDeviceResourceV1 descriptor,
                                   ulong catalogIdentity)
    {
        _descriptor = descriptor;
        _catalogIdentity = catalogIdentity;
    }

    internal ulong ResourceId => _descriptor.ResourceId;
    internal uint OwnerDeviceKind => _descriptor.OwnerDeviceKind;
    internal uint OwnerDeviceId => _descriptor.OwnerDeviceId;
    internal ushort OwnerSegment => _descriptor.OwnerSegment;
    internal byte OwnerBus => _descriptor.OwnerBus;
    internal byte OwnerDevice => _descriptor.OwnerDevice;
    internal byte OwnerFunction => _descriptor.OwnerFunction;
    internal ushort ResourceIndex => _descriptor.ResourceIndex;
    internal uint ResourceType => _descriptor.ResourceType;
    internal uint Flags => _descriptor.Flags;
    internal ulong PhysicalBase => _descriptor.PhysicalBase;
    internal ulong Length => _descriptor.Length;
    internal ulong Alignment => _descriptor.Alignment;
    internal bool HasCatalogOwnership => _catalogIdentity != 0;
    internal ulong CatalogIdentity => _catalogIdentity;

    internal bool HasOwner(uint deviceKind, uint deviceId)
    {
        return OwnerDeviceKind == deviceKind && OwnerDeviceId == deviceId;
    }
}

/* Native publication is authoritative. This is a bounded managed copy of
   that immutable snapshot plus a bounded claim table; it does not consume the
   shared native page allocator. */
internal unsafe sealed class ManagedDeviceResourceCatalog
{
    internal const uint MaxResources = 64;
    internal const uint MaxClaims = 16;

    private static ulong s_nextCatalogIdentity = 1;
    private struct ClaimTable
    {
        internal fixed uint Owners[64];
    }

    private GxManagedKernelDeviceResourceV1* _descriptors;
    private readonly ulong _catalogIdentity;
    private readonly uint _resourceCount;
    private ClaimTable _claims;
    private bool _destroyed;

    private ManagedDeviceResourceCatalog(
        GxManagedKernelDeviceResourceV1* descriptors,
        uint resourceCount, ulong catalogIdentity)
    {
        _descriptors = descriptors;
        _resourceCount = resourceCount;
        _catalogIdentity = catalogIdentity;
    }

    internal uint ResourceCount => _destroyed ? 0U : _resourceCount;
    internal uint ActiveClaimCount
    {
        get
        {
            if (_destroyed) return 0;
            uint count = 0;
            for (uint index = 0; index != _resourceCount; ++index)
            {
                if (_claims.Owners[index] != 0) count++;
            }
            return count;
        }
    }
    internal ulong ArenaIdentity => _destroyed ? 0UL : _catalogIdentity;
    internal bool IsDestroyed => _destroyed;

    internal static bool TryCreateFromPublication(
        IKernelMemoryProvider provider,
        nuint publicationAddress,
        out ManagedDeviceResourceCatalog? catalog)
    {
        catalog = null;
        if (provider == null || !provider.IsAvailable || publicationAddress == 0 ||
            !ManagedKernelContract.IsRangeValid(
                publicationAddress, GxManagedKernelDeviceResourcePublicationV1.ExpectedSize))
        {
            return false;
        }

        GxManagedKernelDeviceResourcePublicationV1* publication =
            (GxManagedKernelDeviceResourcePublicationV1*)publicationAddress;
        if (publication->Size != GxManagedKernelDeviceResourcePublicationV1.ExpectedSize ||
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
        if (expectedBytes != (ulong)publication->DescriptorByteLength ||
            !ManagedKernelContract.IsRangeValid(
                publication->SummaryAddress, GxManagedKernelDeviceResourceSummaryV1.ExpectedSize) ||
            !ManagedKernelContract.IsRangeValid(
                publication->DescriptorAddress, (nuint)publication->DescriptorByteLength))
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
        return TryCreateFromDescriptors(
            provider, (GxManagedKernelDeviceResourceV1*)publication->DescriptorAddress,
            publication->DescriptorCount, out catalog);
    }

    internal static bool TryCreateFromDescriptors(
        IKernelMemoryProvider provider,
        GxManagedKernelDeviceResourceV1* descriptors,
        uint resourceCount,
        out ManagedDeviceResourceCatalog? catalog)
    {
        catalog = null;
        if (provider == null || !provider.IsAvailable || descriptors == null ||
            resourceCount == 0 || resourceCount > MaxResources ||
            !ValidateDescriptors(descriptors, resourceCount))
        {
            return false;
        }

        ulong identity = s_nextCatalogIdentity++;
        if (identity == 0) identity = s_nextCatalogIdentity++;
        ManagedDeviceResourceCatalog candidate =
            new ManagedDeviceResourceCatalog(descriptors, resourceCount, identity);
        if (!candidate.ValidateInvariants()) return false;
        catalog = candidate;
        return true;
    }

    internal bool TryGetResource(uint index, out ManagedDeviceResource resource)
    {
        resource = default;
        if (_destroyed || index >= _resourceCount) return false;
        resource = new ManagedDeviceResource(in _descriptors[index], _catalogIdentity);
        return true;
    }

    internal bool TryCopyDescriptor(uint index,
                                    GxManagedKernelDeviceResourceV1* output)
    {
        if (_destroyed || output == null || index >= _resourceCount) return false;
        *output = _descriptors[index];
        return true;
    }

    internal bool TryFindById(ulong resourceId, out ManagedDeviceResource resource)
    {
        resource = default;
        if (_destroyed || resourceId == 0) return false;
        for (uint index = 0; index != _resourceCount; ++index)
        {
            if (_descriptors[index].ResourceId != resourceId) continue;
            resource = new ManagedDeviceResource(in _descriptors[index], _catalogIdentity);
            return true;
        }
        return false;
    }

    internal bool TryFindByOwner(uint deviceKind, uint deviceId, uint resourceIndex,
                                 out ManagedDeviceResource resource)
    {
        resource = default;
        if (_destroyed) return false;
        for (uint index = 0; index != _resourceCount; ++index)
        {
            GxManagedKernelDeviceResourceV1 descriptor = _descriptors[index];
            if (descriptor.OwnerDeviceKind != deviceKind ||
                descriptor.OwnerDeviceId != deviceId ||
                descriptor.ResourceIndex != resourceIndex) continue;
            resource = new ManagedDeviceResource(in descriptor, _catalogIdentity);
            return true;
        }
        return false;
    }

    internal bool TryClaim(in ManagedDeviceResource resource, uint driverId,
                           uint expectedOwnerKind, uint expectedOwnerId)
    {
        if (_destroyed || driverId == 0 || !resource.HasCatalogOwnership ||
            resource.CatalogIdentity != _catalogIdentity ||
            !resource.HasOwner(expectedOwnerKind, expectedOwnerId) ||
            !TryFindById(resource.ResourceId, out ManagedDeviceResource current))
        {
            return false;
        }
        for (uint index = 0; index != _resourceCount; ++index)
        {
            if (_descriptors[index].ResourceId != current.ResourceId) continue;
            if (_claims.Owners[index] != 0 || ActiveClaimCount >= MaxClaims) return false;
            _claims.Owners[index] = driverId;
            return true;
        }
        return false;
    }

    internal bool TryRelease(in ManagedDeviceResource resource, uint driverId)
    {
        if (_destroyed || driverId == 0 || !resource.HasCatalogOwnership ||
            resource.CatalogIdentity != _catalogIdentity) return false;
        for (uint index = 0; index != _resourceCount; ++index)
        {
            if (_descriptors[index].ResourceId != resource.ResourceId ||
                _claims.Owners[index] != driverId) continue;
            _claims.Owners[index] = 0;
            return true;
        }
        return false;
    }

    internal bool IsClaimedBy(in ManagedDeviceResource resource, uint driverId)
    {
        if (_destroyed || driverId == 0 || !resource.HasCatalogOwnership ||
            resource.CatalogIdentity != _catalogIdentity) return false;
        for (uint index = 0; index != _resourceCount; ++index)
        {
            if (_descriptors[index].ResourceId == resource.ResourceId)
                return _claims.Owners[index] == driverId;
        }
        return false;
    }

    internal bool TryRunRuntimeSurvival()
    {
        if (_destroyed || !TryGetResource(0, out ManagedDeviceResource first)) return false;
        byte[] activity = new byte[2048];
        activity[0] = 0xA5;
        GC.KeepAlive(activity);
        return TryFindById(first.ResourceId, out ManagedDeviceResource again) &&
               again.PhysicalBase == first.PhysicalBase && ValidateInvariants();
    }

    internal bool ValidateInvariants()
    {
        if (_destroyed || _descriptors == null || _resourceCount == 0 ||
            _resourceCount > MaxResources || _catalogIdentity == 0) return false;
        uint claims = 0;
        for (uint index = 0; index != _resourceCount; ++index)
        {
            if (!ValidateDescriptor(in _descriptors[index])) return false;
            if (_claims.Owners[index] != 0) claims++;
            for (uint other = index + 1; other != _resourceCount; ++other)
            {
                if (_descriptors[index].ResourceId == _descriptors[other].ResourceId ||
                    RangesOverlap(in _descriptors[index], in _descriptors[other])) return false;
            }
        }
        return claims <= MaxClaims;
    }

    internal bool Destroy()
    {
        if (_destroyed || ActiveClaimCount != 0) return false;
        for (uint index = 0; index != MaxResources; ++index)
        {
            _claims.Owners[index] = 0;
        }
        _descriptors = null;
        _destroyed = true;
        return true;
    }

    private static bool ValidateDescriptors(
        GxManagedKernelDeviceResourceV1* descriptors, uint resourceCount)
    {
        for (uint index = 0; index != resourceCount; ++index)
        {
            if (!ValidateDescriptor(in descriptors[index])) return false;
            for (uint prior = 0; prior != index; ++prior)
            {
                if (descriptors[prior].ResourceId == descriptors[index].ResourceId ||
                    RangesOverlap(in descriptors[prior], in descriptors[index])) return false;
            }
        }
        return true;
    }

    private static bool ValidateDescriptor(in GxManagedKernelDeviceResourceV1 descriptor)
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
            descriptor.ResourceType > GxManagedKernelDeviceResourceV1.ResourceTypeInterrupt ||
            descriptor.Length == 0 || descriptor.Alignment == 0 ||
            (descriptor.Alignment & (descriptor.Alignment - 1)) != 0 ||
            (descriptor.Flags & ~knownFlags) != 0 || descriptor.ReservedLocation != 0 ||
            descriptor.Reserved0 != 0 || descriptor.Reserved1 != 0 ||
            descriptor.PhysicalBase > ulong.MaxValue - descriptor.Length)
        {
            return false;
        }
        end = descriptor.PhysicalBase + descriptor.Length;
        if (end <= descriptor.PhysicalBase) return false;
        if (descriptor.ResourceType == GxManagedKernelDeviceResourceV1.ResourceTypeIoPort &&
            ((descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagIoPort) == 0 ||
             descriptor.PhysicalBase > ushort.MaxValue || end > 0x10000)) return false;
        if ((descriptor.ResourceType == GxManagedKernelDeviceResourceV1.ResourceTypeMmio ||
             descriptor.ResourceType == GxManagedKernelDeviceResourceV1.ResourceTypePlatformMemory) &&
            (descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagMemory) == 0) return false;
        if ((descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagPrefetchable) != 0 &&
            (descriptor.Flags & GxManagedKernelDeviceResourceV1.FlagMemory) == 0) return false;
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

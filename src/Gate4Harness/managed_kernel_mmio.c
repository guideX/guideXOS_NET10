#include "managed_kernel_mmio.h"
#include "managed_kernel_device_resources.h"

#include <stddef.h>

static GXOS_MMIO_SERVICE *g_callback_service;

static void zero_bytes(void *memory, uint64_t bytes)
{
    uint8_t *cursor = (uint8_t *)memory;
    while (bytes-- != 0) *cursor++ = 0;
}

static int add_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || left > UINT64_MAX - right) return 0;
    *result = left + right;
    return 1;
}

static int range_contains(uint64_t outer_base, uint64_t outer_length,
                          uint64_t inner_base, uint64_t inner_length)
{
    uint64_t outer_end;
    uint64_t inner_end;
    return outer_length != 0 && inner_length != 0 &&
        add_u64(outer_base, outer_length, &outer_end) &&
        add_u64(inner_base, inner_length, &inner_end) &&
        inner_base >= outer_base && inner_end <= outer_end;
}

static int ranges_overlap(uint64_t left_base, uint64_t left_length,
                          uint64_t right_base, uint64_t right_length)
{
    uint64_t left_end;
    uint64_t right_end;
    return add_u64(left_base, left_length, &left_end) &&
        add_u64(right_base, right_length, &right_end) &&
        left_base < right_end && right_base < left_end;
}

GXOS_MMIO_CACHE_STATUS gxos_mmio_cache_policy_validate(
    uint32_t pat_supported,
    uint64_t pat_msr,
    uint64_t mtrr_default_type,
    GXOS_MMIO_CACHE_POLICY *policy_out)
{
    uint8_t pat_uc_entry;
    if (policy_out == 0) return GXOS_MMIO_CACHE_STATUS_INVALID_ARGUMENT;
    zero_bytes(policy_out, sizeof(*policy_out));
    policy_out->pat_supported = pat_supported != 0;
    policy_out->pat_msr = pat_msr;
    policy_out->mtrr_default_type = mtrr_default_type;
    policy_out->mtrr_enabled = (mtrr_default_type & (1ULL << 11)) != 0;
    policy_out->fixed_mtrr_enabled = (mtrr_default_type & (1ULL << 10)) != 0;
    policy_out->pte_flags = GXOS_MMIO_CACHE_PTE_FLAGS;
    if (pat_supported == 0) return GXOS_MMIO_CACHE_STATUS_UNSUPPORTED;
    /* PTE PWT=1, PCD=1, PAT=0 selects PAT entry 3. A zero type is UC.
       This is checked rather than assumed, and PAT/MTRR are never changed. */
    pat_uc_entry = (uint8_t)((pat_msr >> (3U * 8U)) & 0xFFU);
    if ((pat_uc_entry & 7U) != 0U || (pat_uc_entry & 0xF8U) != 0U) {
        return GXOS_MMIO_CACHE_STATUS_UNPROVEN;
    }
    policy_out->safe_uncacheable = 1;
    return GXOS_MMIO_CACHE_STATUS_OK;
}

static uint32_t cpuid_pat_supported(void)
{
#if defined(__x86_64__)
    uint32_t eax;
    uint32_t ebx;
    uint32_t ecx;
    uint32_t edx;
    eax = 1U;
    __asm__ volatile ("cpuid" : "+a"(eax), "=b"(ebx), "=c"(ecx), "=d"(edx));
    return (edx & (1U << 16)) != 0U;
#else
    return 0;
#endif
}

static int read_msr(uint32_t msr, uint64_t *value_out)
{
#if defined(__x86_64__)
    uint32_t low;
    uint32_t high;
    if (value_out == 0) return 0;
    __asm__ volatile ("rdmsr" : "=a"(low), "=d"(high) : "c"(msr));
    *value_out = ((uint64_t)high << 32) | low;
    return 1;
#else
    (void)msr;
    (void)value_out;
    return 0;
#endif
}

GXOS_MMIO_CACHE_STATUS gxos_mmio_cache_policy_probe(
    GXOS_MMIO_CACHE_POLICY *policy_out)
{
    uint64_t pat_msr;
    uint64_t mtrr_default_type;
    if (policy_out == 0) return GXOS_MMIO_CACHE_STATUS_INVALID_ARGUMENT;
    if (!cpuid_pat_supported() ||
        !read_msr(GXOS_MMIO_CACHE_IA32_PAT, &pat_msr) ||
        !read_msr(GXOS_MMIO_CACHE_IA32_MTRR_DEF_TYPE, &mtrr_default_type)) {
        zero_bytes(policy_out, sizeof(*policy_out));
        return GXOS_MMIO_CACHE_STATUS_UNSUPPORTED;
    }
    return gxos_mmio_cache_policy_validate(
        1, pat_msr, mtrr_default_type, policy_out);
}

static const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *find_resource(
    const GXOS_MMIO_SERVICE *service, uint64_t resource_id,
    uint32_t *index_out)
{
    uint32_t index;
    if (index_out != 0) *index_out = UINT32_MAX;
    if (service == 0 || service->resources == 0 || resource_id == 0) return 0;
    for (index = 0; index != service->resource_count; ++index) {
        if (service->resources[index].ResourceId == resource_id) {
            if (index_out != 0) *index_out = index;
            return &service->resources[index];
        }
    }
    return 0;
}

static GXOS_MMIO_CLAIM_RECORD *find_claim(
    GXOS_MMIO_SERVICE *service, uint64_t handle)
{
    uint32_t slot;
    uint32_t generation;
    if (service == 0 || handle == 0) return 0;
    slot = (uint32_t)(handle & UINT32_MAX);
    generation = (uint32_t)(handle >> 32);
    if (slot == 0 || slot > GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_CLAIMS ||
        generation == 0) return 0;
    {
        GXOS_MMIO_CLAIM_RECORD *claim = &service->claims[slot - 1U];
        return claim->live && claim->generation == generation ? claim : 0;
    }
}

static GXOS_MMIO_MAPPING_RECORD *find_mapping(
    GXOS_MMIO_SERVICE *service, uint64_t handle)
{
    uint32_t slot;
    uint32_t generation;
    if (service == 0 || handle == 0) return 0;
    slot = (uint32_t)(handle & UINT32_MAX);
    generation = (uint32_t)(handle >> 32);
    if (slot == 0 || slot > GXOS_MMIO_MAPPING_CAPACITY || generation == 0) {
        return 0;
    }
    {
        GXOS_MMIO_MAPPING_RECORD *mapping = &service->mappings[slot - 1U];
        return mapping->live && mapping->generation == generation ? mapping : 0;
    }
}

static int physical_range_is_not_ram(const GXOS_UEFI_MEMORY_MAP *memory_map,
                                     uint64_t base, uint64_t length)
{
    uint32_t index;
    if (memory_map == 0 || !memory_map->valid) return 0;
    for (index = 0; index != memory_map->descriptor_count; ++index) {
        const EFI_MEMORY_DESCRIPTOR *descriptor =
            gxos_uefi_memory_map_descriptor(memory_map, index);
        GXOS_MEMORY_CLASS memory_class;
        uint64_t bytes;
        if (descriptor == 0 || descriptor->NumberOfPages == 0 ||
            descriptor->NumberOfPages > UINT64_MAX / GXOS_VM_PAGE_SIZE) {
            return 0;
        }
        bytes = descriptor->NumberOfPages * GXOS_VM_PAGE_SIZE;
        memory_class = gxos_memory_class_for_efi_type(descriptor->Type);
        if (gxos_memory_class_is_ram_like(memory_class) &&
            ranges_overlap(base, length, descriptor->PhysicalStart, bytes)) {
            return 0;
        }
    }
    return 1;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_service_init(
    GXOS_MMIO_SERVICE *service,
    GXOS_VM_PAGING *paging,
    GXOS_VM_ARENA *arena,
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources,
    uint32_t resource_count,
    uint64_t resource_generation,
    const GXOS_UEFI_MEMORY_MAP *memory_map,
    const GXOS_MMIO_CACHE_POLICY *cache_policy)
{
    uint32_t index;
    uint32_t reservation_index;
    int reservation_found = 0;
    if (service == 0 || paging == 0 || arena == 0 || resources == 0 ||
        resource_count == 0 || resource_count >
            GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_DESCRIPTORS ||
        resource_generation == 0 || memory_map == 0 ||
        memory_map->generation != resource_generation || cache_policy == 0 ||
        !paging->nx_enabled ||
        !cache_policy->safe_uncacheable || !cache_policy->pat_supported ||
        cache_policy->pte_flags != GXOS_MMIO_CACHE_PTE_FLAGS ||
        ((cache_policy->pat_msr >> 24U) & 0xFFU) != 0U ||
        !gxos_vm_arena_contains(arena, GXOS_MMIO_WINDOW_BASE,
                                GXOS_MMIO_WINDOW_LENGTH) ||
        GXOS_MMIO_WINDOW_BASE % GXOS_VM_PAGE_SIZE != 0 ||
        GXOS_MMIO_WINDOW_LENGTH % GXOS_VM_PAGE_SIZE != 0) {
        return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    }
    for (reservation_index = 0;
         reservation_index != GXOS_VM_MAX_RESERVATIONS; ++reservation_index) {
        const GXOS_VM_RESERVATION *reservation =
            &arena->reservations[reservation_index];
        if (reservation->live &&
            reservation->base == GXOS_MMIO_WINDOW_BASE &&
            reservation->bytes == GXOS_MMIO_WINDOW_LENGTH &&
            reservation->kind == GXOS_VM_RESERVATION_KIND_MMIO &&
            reservation->owner == GXOS_MMIO_WINDOW_OWNER &&
            reservation->generation == resource_generation) {
            reservation_found = 1;
            break;
        }
    }
    if (!reservation_found) return GXOS_MMIO_SERVICE_INVALID_STATE;
    for (index = 0; index != resource_count; ++index) {
        const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resource = &resources[index];
        if (gxos_managed_kernel_validate_resource(resource) !=
                GXOS_MANAGED_KERNEL_RESOURCE_OK) return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
        if (resource->ResourceType == GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO &&
            !physical_range_is_not_ram(memory_map, resource->PhysicalBase,
                                       resource->Length)) {
            return GXOS_MMIO_SERVICE_INVALID_STATE;
        }
    }
    zero_bytes(service, sizeof(*service));
    service->paging = paging;
    service->arena = arena;
    service->resources = resources;
    service->resource_count = resource_count;
    service->resource_generation = resource_generation;
    service->window_base = GXOS_MMIO_WINDOW_BASE;
    service->window_length = GXOS_MMIO_WINDOW_LENGTH;
    service->cache_policy = *cache_policy;
    service->reservation_slot = reservation_index;
    service->next_claim_generation = 1;
    service->next_mapping_generation = 1;
    service->initialized = 1;
    for (index = 0; index != resource_count; ++index) {
        if (resources[index].ResourceType == GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO &&
            (resources[index].Flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_CACHE_UNCACHED) == 0) {
            service->initialized = 0;
            return GXOS_MMIO_SERVICE_INVALID_STATE;
        }
    }
    return GXOS_MMIO_SERVICE_OK;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_service_teardown(
    GXOS_MMIO_SERVICE *service)
{
    uint32_t index;
    if (service == 0 || !service->initialized) return GXOS_MMIO_SERVICE_INVALID_STATE;
    for (index = 0; index != GXOS_MMIO_MAPPING_CAPACITY; ++index) {
        if (service->mappings[index].live) return GXOS_MMIO_SERVICE_INVALID_STATE;
    }
    for (index = 0; index != GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_CLAIMS; ++index) {
        if (service->claims[index].live) return GXOS_MMIO_SERVICE_INVALID_STATE;
    }
    zero_bytes(service, sizeof(*service));
    return GXOS_MMIO_SERVICE_OK;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_claim(
    GXOS_MMIO_SERVICE *service, uint64_t resource_id, uint32_t driver_id,
    uint32_t expected_owner_kind, uint32_t expected_owner_id,
    uint64_t *claim_handle_out)
{
    uint32_t index;
    if (claim_handle_out != 0) *claim_handle_out = 0;
    if (service == 0 || !service->initialized || driver_id == 0 ||
        claim_handle_out == 0) return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    {
        const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resource =
            find_resource(service, resource_id, &index);
        if (resource == 0) return GXOS_MMIO_SERVICE_NOT_FOUND;
        if (resource->OwnerDeviceKind != expected_owner_kind ||
            resource->OwnerDeviceId != expected_owner_id) {
            return GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH;
        }
        for (index = 0; index != GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_CLAIMS; ++index) {
            GXOS_MMIO_CLAIM_RECORD *claim = &service->claims[index];
            if (claim->live && claim->resource_id == resource_id) {
                return GXOS_MMIO_SERVICE_INVALID_STATE;
            }
            if (claim->live) continue;
            if (service->next_claim_generation == 0 ||
                service->next_claim_generation > UINT32_MAX) {
                return GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED;
            }
            claim->live = 1;
            claim->resource_id = resource_id;
            claim->owner_driver_id = driver_id;
            claim->mapping_count = 0;
            claim->generation = service->next_claim_generation++;
            claim->claim_handle = (claim->generation << 32) | (index + 1U);
            *claim_handle_out = claim->claim_handle;
            return GXOS_MMIO_SERVICE_OK;
        }
    }
    return GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_release(
    GXOS_MMIO_SERVICE *service, uint64_t claim_handle, uint32_t driver_id)
{
    GXOS_MMIO_CLAIM_RECORD *claim = find_claim(service, claim_handle);
    if (claim == 0) return GXOS_MMIO_SERVICE_NOT_FOUND;
    if (claim->owner_driver_id != driver_id) return GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH;
    if (claim->mapping_count != 0) return GXOS_MMIO_SERVICE_INVALID_STATE;
    zero_bytes(claim, sizeof(*claim));
    return GXOS_MMIO_SERVICE_OK;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_map(
    GXOS_MMIO_SERVICE *service, uint64_t claim_handle, uint32_t driver_id,
    uint64_t offset, uint64_t length, uint32_t access,
    uint64_t *mapping_handle_out)
{
    GXOS_MMIO_CLAIM_RECORD *claim = find_claim(service, claim_handle);
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resource;
    uint64_t physical_start;
    uint64_t page_offset;
    uint64_t mapped_length;
    uint64_t page_count;
    uint32_t index;
    uint64_t virtual_page;
    uint64_t window_end;
    uint64_t last_virtual_start;
    if (mapping_handle_out != 0) *mapping_handle_out = 0;
    if (service == 0 || !service->initialized || mapping_handle_out == 0 ||
        (access != 1U && access != 3U) || length == 0 || claim == 0) {
        return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    }
    if (claim->owner_driver_id != driver_id) return GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH;
    resource = find_resource(service, claim->resource_id, 0);
    if (resource == 0) return GXOS_MMIO_SERVICE_NOT_FOUND;
    if (resource->ResourceType != GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO ||
        (resource->Flags & (GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
                            GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY |
                            GX_MANAGED_DEVICE_RESOURCE_FLAG_PCI_ASSIGNED |
                            GX_MANAGED_DEVICE_RESOURCE_FLAG_CACHE_UNCACHED)) !=
            (GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
             GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY |
             GX_MANAGED_DEVICE_RESOURCE_FLAG_PCI_ASSIGNED |
             GX_MANAGED_DEVICE_RESOURCE_FLAG_CACHE_UNCACHED) ||
        (access == 3U &&
         (resource->Flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_WRITABLE) == 0U)) {
        return GXOS_MMIO_SERVICE_UNSUPPORTED;
    }
    if (!range_contains(0, resource->Length, offset, length) ||
        !add_u64(resource->PhysicalBase, offset, &physical_start)) {
        return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    }
    page_offset = physical_start & (GXOS_VM_PAGE_SIZE - 1U);
    physical_start -= page_offset;
    if (!add_u64(page_offset, length, &mapped_length) ||
        mapped_length > UINT64_MAX - (GXOS_VM_PAGE_SIZE - 1U)) {
        return GXOS_MMIO_SERVICE_OVERFLOW;
    }
    mapped_length = (mapped_length + GXOS_VM_PAGE_SIZE - 1U) &
                    ~(GXOS_VM_PAGE_SIZE - 1U);
    page_count = mapped_length / GXOS_VM_PAGE_SIZE;
    if (page_count == 0 || page_count > GXOS_MMIO_MAX_MAPPING_PAGES ||
        page_count > UINT32_MAX) return GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED;
    for (index = 0; index != GXOS_MMIO_MAPPING_CAPACITY; ++index) {
        GXOS_MMIO_MAPPING_RECORD *mapping = &service->mappings[index];
        if (!mapping->live) continue;
        if (mapping->resource_id == resource->ResourceId &&
            mapping->requested_offset == offset &&
            mapping->requested_length == length &&
            mapping->owner_driver_id == driver_id) {
            return GXOS_MMIO_SERVICE_INVALID_STATE;
        }
    }
    if (!add_u64(service->window_base, service->window_length, &window_end) ||
        mapped_length > service->window_length) {
        return GXOS_MMIO_SERVICE_OVERFLOW;
    }
    last_virtual_start = window_end - mapped_length;
    for (virtual_page = service->window_base;
         virtual_page <= last_virtual_start;
         virtual_page += GXOS_VM_PAGE_SIZE) {
        int conflict = 0;
        for (index = 0; index != GXOS_MMIO_MAPPING_CAPACITY; ++index) {
            const GXOS_MMIO_MAPPING_RECORD *mapping = &service->mappings[index];
            if (mapping->live && ranges_overlap(virtual_page, mapped_length,
                                                mapping->virtual_base,
                                                mapping->mapped_length)) {
                conflict = 1;
                break;
            }
        }
        if (!conflict) break;
        if (virtual_page > UINT64_MAX - GXOS_VM_PAGE_SIZE) return GXOS_MMIO_SERVICE_OVERFLOW;
    }
    if (virtual_page > last_virtual_start) {
        return GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED;
    }
    for (index = 0; index != GXOS_MMIO_MAPPING_CAPACITY; ++index) {
        GXOS_MMIO_MAPPING_RECORD *mapping = &service->mappings[index];
        if (mapping->live) continue;
        if (service->next_mapping_generation == 0 ||
            service->next_mapping_generation > UINT32_MAX) {
            return GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED;
        }
        if (gxos_vm_paging_map_range_with_flags_in_window(
                service->paging, virtual_page, physical_start, page_count,
                access == 3U ? 1U : 0U, 0, service->cache_policy.pte_flags,
                service->window_base, service->window_length) !=
            GXOS_VM_PAGING_STATUS_OK) return GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED;
        mapping->live = 1;
        mapping->resource_id = resource->ResourceId;
        mapping->claim_handle = claim_handle;
        mapping->owner_driver_id = driver_id;
        mapping->page_count = (uint32_t)page_count;
        mapping->virtual_base = virtual_page;
        mapping->physical_base = physical_start;
        mapping->requested_offset = offset;
        mapping->requested_length = length;
        mapping->mapped_length = mapped_length;
        mapping->access = access;
        mapping->reserved0 = 0;
        mapping->generation = service->next_mapping_generation++;
        claim->mapping_count++;
        *mapping_handle_out = (mapping->generation << 32) | (index + 1U);
        return GXOS_MMIO_SERVICE_OK;
    }
    return GXOS_MMIO_SERVICE_RESOURCE_EXHAUSTED;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_unmap(
    GXOS_MMIO_SERVICE *service, uint64_t mapping_handle, uint32_t driver_id)
{
    GXOS_MMIO_MAPPING_RECORD *mapping = find_mapping(service, mapping_handle);
    GXOS_MMIO_CLAIM_RECORD *claim;
    uint64_t page;
    uint32_t index;
    if (mapping == 0) return GXOS_MMIO_SERVICE_NOT_FOUND;
    if (mapping->owner_driver_id != driver_id) return GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH;
    for (index = 0; index != mapping->page_count; ++index) {
        page = mapping->virtual_base + (uint64_t)index * GXOS_VM_PAGE_SIZE;
        if (gxos_vm_paging_unmap_page_in_window(
                service->paging, page, 0, service->window_base,
                service->window_length) !=
                GXOS_VM_PAGING_STATUS_OK) return GXOS_MMIO_SERVICE_INVALID_STATE;
    }
    claim = find_claim(service, mapping->claim_handle);
    if (claim != 0 && claim->mapping_count != 0) claim->mapping_count--;
    zero_bytes(mapping, sizeof(*mapping));
    return GXOS_MMIO_SERVICE_OK;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_read(
    GXOS_MMIO_SERVICE *service, uint64_t mapping_handle, uint32_t driver_id,
    uint64_t offset, uint32_t width, uint64_t *value_out)
{
    GXOS_MMIO_MAPPING_RECORD *mapping = find_mapping(service, mapping_handle);
    uint64_t address;
    if (value_out != 0) *value_out = 0;
    if (value_out == 0 || mapping == 0) return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    if (mapping->owner_driver_id != driver_id) return GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH;
    if ((width != 1U && width != 2U && width != 4U && width != 8U) ||
        offset > UINT64_MAX - width || offset + width > mapping->requested_length ||
        (width > 1U && (offset & (width - 1U)) != 0U)) {
        return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    }
    if (mapping->virtual_base > UINT64_MAX -
            (mapping->physical_base & (GXOS_VM_PAGE_SIZE - 1U)) ||
        !add_u64(mapping->virtual_base +
                     (mapping->physical_base & (GXOS_VM_PAGE_SIZE - 1U)),
                 offset, &address) || width > UINT64_MAX - address) {
        return GXOS_MMIO_SERVICE_OVERFLOW;
    }
    if (!range_contains(service->window_base, service->window_length,
                        address, width)) {
        return GXOS_MMIO_SERVICE_INVALID_STATE;
    }
    switch (width) {
    case 1: *value_out = *(volatile uint8_t *)(uintptr_t)address; break;
    case 2: *value_out = *(volatile uint16_t *)(uintptr_t)address; break;
    case 4: *value_out = *(volatile uint32_t *)(uintptr_t)address; break;
    case 8: *value_out = *(volatile uint64_t *)(uintptr_t)address; break;
    default: return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    }
    __asm__ volatile ("" : : : "memory");
    return GXOS_MMIO_SERVICE_OK;
}

GXOS_MMIO_SERVICE_STATUS gxos_mmio_write(
    GXOS_MMIO_SERVICE *service, uint64_t mapping_handle, uint32_t driver_id,
    uint64_t offset, uint32_t width, uint64_t value)
{
    GXOS_MMIO_MAPPING_RECORD *mapping = find_mapping(service, mapping_handle);
    uint64_t address;
    if (mapping == 0) return GXOS_MMIO_SERVICE_NOT_FOUND;
    if (mapping->owner_driver_id != driver_id) {
        return GXOS_MMIO_SERVICE_OWNERSHIP_MISMATCH;
    }
    if ((mapping->access & 2U) == 0U ||
        (width != 1U && width != 2U && width != 4U && width != 8U) ||
        offset > UINT64_MAX - width || offset + width > mapping->requested_length ||
        (width > 1U && (offset & (width - 1U)) != 0U)) {
        return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    }
    if (mapping->virtual_base > UINT64_MAX -
            (mapping->physical_base & (GXOS_VM_PAGE_SIZE - 1U)) ||
        !add_u64(mapping->virtual_base +
                     (mapping->physical_base & (GXOS_VM_PAGE_SIZE - 1U)),
                 offset, &address) || width > UINT64_MAX - address ||
        !range_contains(service->window_base, service->window_length,
                        address, width)) {
        return GXOS_MMIO_SERVICE_INVALID_STATE;
    }
    switch (width) {
    case 1: *(volatile uint8_t *)(uintptr_t)address = (uint8_t)value; break;
    case 2: *(volatile uint16_t *)(uintptr_t)address = (uint16_t)value; break;
    case 4: *(volatile uint32_t *)(uintptr_t)address = (uint32_t)value; break;
    case 8: *(volatile uint64_t *)(uintptr_t)address = value; break;
    default: return GXOS_MMIO_SERVICE_INVALID_ARGUMENT;
    }
    __asm__ volatile ("" : : : "memory");
    return GXOS_MMIO_SERVICE_OK;
}

int gxos_mmio_validate_claim(
    const GXOS_MMIO_SERVICE *service, uint64_t claim_handle,
    uint32_t driver_id, uint64_t *resource_id_out,
    uint32_t *owner_kind_out, uint32_t *owner_id_out)
{
    GXOS_MMIO_CLAIM_RECORD *claim;
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resource;
    if (resource_id_out != 0) *resource_id_out = 0;
    if (owner_kind_out != 0) *owner_kind_out = 0;
    if (owner_id_out != 0) *owner_id_out = 0;
    if (service == 0 || !service->initialized || driver_id == 0) return 0;
    claim = find_claim((GXOS_MMIO_SERVICE *)service, claim_handle);
    if (claim == 0 || claim->owner_driver_id != driver_id) return 0;
    resource = find_resource(service, claim->resource_id, 0);
    if (resource == 0) return 0;
    if (resource_id_out != 0) *resource_id_out = resource->ResourceId;
    if (owner_kind_out != 0) *owner_kind_out = resource->OwnerDeviceKind;
    if (owner_id_out != 0) *owner_id_out = resource->OwnerDeviceId;
    return 1;
}

void gxos_mmio_set_callback_service(GXOS_MMIO_SERVICE *service)
{
    g_callback_service = service;
}

static int output_range_valid(uintptr_t address, uintptr_t capacity,
                              uintptr_t required)
{
    return address != 0 && capacity >= required &&
        address <= UINTPTR_MAX - capacity;
}

uint32_t gxos_mmio_claim_callback(
    uint64_t resource_id, uint32_t driver_id, uint32_t expected_owner_kind,
    uint32_t expected_owner_id, uintptr_t result_address,
    uintptr_t result_capacity)
{
    GXOS_MMIO_CLAIM_RESULT_V1 *result;
    uint64_t handle = 0;
    GXOS_MMIO_SERVICE_STATUS status;
    if (!output_range_valid(result_address, result_capacity, sizeof(*result))) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    status = gxos_mmio_claim(g_callback_service, resource_id, driver_id,
                             expected_owner_kind, expected_owner_id, &handle);
    if (status != GXOS_MMIO_SERVICE_OK) return (uint32_t)status;
    result = (GXOS_MMIO_CLAIM_RESULT_V1 *)(uintptr_t)result_address;
    result->Size = sizeof(*result);
    result->AbiVersion = 1;
    result->Handle = handle;
    result->Reserved = 0;
    return GX_MANAGED_OK;
}

uint32_t gxos_mmio_release_callback(uint64_t claim_handle, uint32_t driver_id)
{
    return (uint32_t)gxos_mmio_release(g_callback_service, claim_handle, driver_id);
}

uint32_t gxos_mmio_map_callback(
    uint64_t claim_handle, uint32_t driver_id, uint64_t offset,
    uint64_t length, uint32_t access, uintptr_t result_address,
    uintptr_t result_capacity)
{
    GXOS_MMIO_MAPPING_RESULT_V1 *result;
    GXOS_MMIO_MAPPING_RECORD *mapping;
    uint64_t handle = 0;
    GXOS_MMIO_SERVICE_STATUS status;
    if (!output_range_valid(result_address, result_capacity, sizeof(*result))) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    status = gxos_mmio_map(g_callback_service, claim_handle, driver_id,
                           offset, length, access, &handle);
    if (status != GXOS_MMIO_SERVICE_OK) return (uint32_t)status;
    mapping = find_mapping(g_callback_service, handle);
    if (mapping == 0 || mapping->claim_handle != claim_handle ||
        mapping->owner_driver_id != driver_id) {
        (void)gxos_mmio_unmap(g_callback_service, handle, driver_id);
        return GX_MANAGED_INVALID_STATE;
    }
    result = (GXOS_MMIO_MAPPING_RESULT_V1 *)(uintptr_t)result_address;
    result->Size = sizeof(*result);
    result->AbiVersion = 1;
    result->Handle = handle;
    result->ResourceId = mapping->resource_id;
    result->Offset = offset;
    result->Length = length;
    result->Access = access;
    result->Reserved0 = 0;
    return GX_MANAGED_OK;
}

uint32_t gxos_mmio_unmap_callback(uint64_t mapping_handle,
                                  uint32_t driver_id)
{
    return (uint32_t)gxos_mmio_unmap(g_callback_service, mapping_handle,
                                     driver_id);
}

uint32_t gxos_mmio_read_callback(
    uint64_t mapping_handle, uint32_t driver_id, uint64_t offset,
    uint32_t width, uintptr_t result_address, uintptr_t result_capacity)
{
    GXOS_MMIO_READ_RESULT_V1 *result;
    uint64_t value = 0;
    GXOS_MMIO_SERVICE_STATUS status;
    if (!output_range_valid(result_address, result_capacity, sizeof(*result))) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    status = gxos_mmio_read(g_callback_service, mapping_handle, driver_id,
                            offset, width, &value);
    if (status != GXOS_MMIO_SERVICE_OK) return (uint32_t)status;
    result = (GXOS_MMIO_READ_RESULT_V1 *)(uintptr_t)result_address;
    result->Size = sizeof(*result);
    result->AbiVersion = 1;
    result->Width = width;
    result->Reserved0 = 0;
    result->Value = value;
    result->Reserved1 = 0;
    return GX_MANAGED_OK;
}

uint32_t gxos_mmio_write_callback(
    uint64_t mapping_handle, uint32_t driver_id, uint64_t offset,
    uint32_t width, uint64_t value)
{
    return (uint32_t)gxos_mmio_write(g_callback_service, mapping_handle,
                                     driver_id, offset, width, value);
}

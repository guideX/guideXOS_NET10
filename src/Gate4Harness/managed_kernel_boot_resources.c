#include "managed_kernel_boot_resources.h"

static void zero_bytes(void *memory, uint64_t bytes)
{
    uint8_t *cursor = (uint8_t *)memory;
    while (bytes-- != 0) *cursor++ = 0;
}

static int multiply_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (left != 0 && right > UINT64_MAX / left) return 0;
    *result = left * right;
    return 1;
}

static int range_end(uint64_t base, uint64_t length, uint64_t *end)
{
    if (length == 0 || base > UINT64_MAX - length) return 0;
    *end = base + length;
    return 1;
}

static uint32_t type_for_class(GXOS_MEMORY_CLASS memory_class)
{
    if ((uint32_t)memory_class >= GXOS_MEMORY_CLASS_COUNT) {
        return GX_MANAGED_BOOT_RESOURCE_TYPE_UNKNOWN;
    }
    return (uint32_t)memory_class + 1U;
}

static uint32_t flags_for_class(GXOS_MEMORY_CLASS memory_class)
{
    uint32_t flags = 0;
    if (memory_class == GXOS_MEMORY_CLASS_CONVENTIONAL) {
        flags |= GX_MANAGED_BOOT_RESOURCE_FLAG_USABLE;
    }
    if (gxos_memory_class_is_ram_like(memory_class)) {
        flags |= GX_MANAGED_BOOT_RESOURCE_FLAG_RAM_LIKE;
    }
    if (memory_class == GXOS_MEMORY_CLASS_RUNTIME_SERVICES_CODE ||
        memory_class == GXOS_MEMORY_CLASS_RUNTIME_SERVICES_DATA) {
        flags |= GX_MANAGED_BOOT_RESOURCE_FLAG_RUNTIME;
    }
    return flags;
}

static int classification_matches(const GXOS_MEMORY_CLASSIFICATION *left,
                                  const GXOS_MEMORY_CLASSIFICATION *right)
{
    uint32_t index;
    if (left == 0 || right == 0 || left->valid == 0 || right->valid == 0 ||
        left->descriptor_count != right->descriptor_count ||
        left->total_ram_like_bytes != right->total_ram_like_bytes ||
        left->conventional_bytes != right->conventional_bytes) {
        return 0;
    }
    for (index = 0; index != GXOS_MEMORY_CLASS_COUNT; ++index) {
        if (left->class_bytes[index] != right->class_bytes[index] ||
            left->class_pages[index] != right->class_pages[index]) {
            return 0;
        }
    }
    return 1;
}

GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_STATUS
gxos_managed_kernel_normalize_boot_resources(
    const GXOS_UEFI_MEMORY_MAP *map,
    const GXOS_MEMORY_CLASSIFICATION *classification,
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 *regions,
    uint32_t region_capacity,
    GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 *summary)
{
    GXOS_MEMORY_CLASSIFICATION verified;
    GXOS_MEMORY_CLASSIFICATION_STATUS classification_status;
    uint32_t index;

    if (map == 0 || classification == 0 || regions == 0 || summary == 0 ||
        !map->valid) {
        return GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_INVALID_ARGUMENT;
    }
    if (map->descriptor_count == 0 ||
        map->descriptor_count > GX_MANAGED_KERNEL_BOOT_RESOURCE_MAX_REGIONS ||
        region_capacity < map->descriptor_count) {
        return GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_CAPACITY;
    }
    classification_status = gxos_uefi_memory_map_classify(map, &verified);
    if (classification_status != GXOS_MEMORY_CLASSIFICATION_OK ||
        !classification_matches(classification, &verified)) {
        return classification_status == GXOS_MEMORY_CLASSIFICATION_OVERFLOW
            ? GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OVERFLOW
            : GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_MALFORMED;
    }

    zero_bytes(regions, (uint64_t)region_capacity *
               sizeof(GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1));
    zero_bytes(summary, sizeof(*summary));
    for (index = 0; index != map->descriptor_count; ++index) {
        const EFI_MEMORY_DESCRIPTOR *descriptor =
            gxos_uefi_memory_map_descriptor(map, index);
        GXOS_MEMORY_CLASS memory_class;
        uint64_t length;
        uint64_t ignored_end;
        if (descriptor == 0 || descriptor->NumberOfPages == 0 ||
            !multiply_u64(descriptor->NumberOfPages, GXOS_MEMORY_PAGE_SIZE,
                          &length) ||
            !range_end(descriptor->PhysicalStart, length, &ignored_end)) {
            return descriptor == 0 || descriptor->NumberOfPages == 0
                ? GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_MALFORMED
                : GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OVERFLOW;
        }
        memory_class = gxos_memory_class_for_efi_type(descriptor->Type);
        regions[index].Size = GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1_SIZE;
        regions[index].AbiVersion = GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1;
        regions[index].BaseAddress = descriptor->PhysicalStart;
        regions[index].Length = length;
        regions[index].Type = type_for_class(memory_class);
        regions[index].Flags = flags_for_class(memory_class);
    }

    summary->Size = GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1_SIZE;
    summary->AbiVersion = GX_MANAGED_KERNEL_BOOT_RESOURCES_ABI_V1;
    summary->ServiceVersion = GX_MANAGED_KERNEL_BOOT_RESOURCES_SERVICE_VERSION_V1;
    summary->Architecture = GX_MANAGED_KERNEL_ARCH_X64;
    summary->RegionCount = map->descriptor_count;
    summary->ResourceMapIdentity =
        GX_MANAGED_KERNEL_BOOT_RESOURCE_MAP_ID_UEFI_NORMALIZED_V1;
    summary->TotalPhysicalBytes = verified.total_ram_like_bytes;
    summary->UsablePhysicalBytes = verified.conventional_bytes;
    summary->Capabilities =
        GX_MANAGED_BOOT_RESOURCE_CAPABILITY_SUMMARY |
        GX_MANAGED_BOOT_RESOURCE_CAPABILITY_REGIONS |
        GX_MANAGED_BOOT_RESOURCE_CAPABILITY_TOTALS;
    summary->Reserved = 0;
    if (summary->TotalPhysicalBytes == 0 ||
        summary->UsablePhysicalBytes > summary->TotalPhysicalBytes) {
        return GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_MALFORMED;
    }
    return GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OK;
}

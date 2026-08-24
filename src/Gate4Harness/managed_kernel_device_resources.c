#include "managed_kernel_device_resources.h"

#include <stddef.h>

static int add_u64(uint64_t left, uint64_t right, uint64_t *result)
{
    if (result == 0 || left > UINT64_MAX - right) return 0;
    *result = left + right;
    return 1;
}

static int is_power_of_two(uint64_t value)
{
    return value != 0 && (value & (value - 1U)) == 0;
}

static uint64_t size_from_mask(uint64_t mask, uint64_t address_mask)
{
    uint64_t masked = mask & address_mask;
    return ((~masked) & address_mask) + 1U;
}

GXOS_PCI_BAR_DECODE_STATUS gxos_pci_decode_bar(
    uint32_t raw_low, uint32_t raw_high,
    uint32_t mask_low, uint32_t mask_high,
    GXOS_PCI_BAR_DECODED *decoded)
{
    uint64_t base;
    uint64_t mask;
    uint64_t length;
    uint64_t end;
    uint64_t address_mask;
    uint32_t memory_type;
    uint32_t flags;

    if (decoded == 0) return GXOS_PCI_BAR_DECODE_INVALID_ARGUMENT;
    *decoded = (GXOS_PCI_BAR_DECODED){0};
    if (raw_low == 0U || raw_low == UINT32_MAX) {
        return GXOS_PCI_BAR_DECODE_UNIMPLEMENTED;
    }

    if ((raw_low & 1U) != 0U) {
        base = (uint64_t)(raw_low & ~3U);
        mask = (uint64_t)(mask_low & ~3U);
        if (mask == 0U || mask == UINT32_MAX) {
            return GXOS_PCI_BAR_DECODE_UNIMPLEMENTED;
        }
        length = size_from_mask(mask, UINT32_MAX);
        if (!is_power_of_two(length) || length < 4U ||
            !add_u64(base, length, &end) || end <= base) {
            return GXOS_PCI_BAR_DECODE_MALFORMED;
        }
        decoded->resource_type = GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT;
        decoded->flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_IO_PORT;
        decoded->base = base;
        decoded->length = length;
        decoded->alignment = length;
        decoded->implemented = 1U;
        return GXOS_PCI_BAR_DECODE_OK;
    }

    memory_type = (raw_low >> 1) & 3U;
    if (memory_type == 1U || memory_type == 3U) {
        return GXOS_PCI_BAR_DECODE_RESERVED_TYPE;
    }
    if (memory_type == 2U) {
        base = ((uint64_t)raw_high << 32) | (uint64_t)(raw_low & ~0xFU);
        mask = ((uint64_t)mask_high << 32) | (uint64_t)(mask_low & ~0xFU);
        decoded->is_64_bit = 1U;
        address_mask = UINT64_MAX;
        flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY |
                GX_MANAGED_DEVICE_RESOURCE_FLAG_ADDRESS_64;
    } else {
        base = (uint64_t)(raw_low & ~0xFU);
        mask = (uint64_t)(mask_low & ~0xFU);
        address_mask = UINT32_MAX;
        flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY;
    }
    if ((raw_low & 8U) != 0U) {
        flags |= GX_MANAGED_DEVICE_RESOURCE_FLAG_PREFETCHABLE;
    }
    if (mask == 0U || mask == UINT64_MAX) {
        return GXOS_PCI_BAR_DECODE_UNIMPLEMENTED;
    }
    length = size_from_mask(mask, address_mask);
    if (!is_power_of_two(length) || length < 16U ||
        !add_u64(base, length, &end) || end <= base) {
        return GXOS_PCI_BAR_DECODE_MALFORMED;
    }
    decoded->resource_type = GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO;
    decoded->flags = flags;
    decoded->base = base;
    decoded->length = length;
    decoded->alignment = length;
    decoded->implemented = 1U;
    return GXOS_PCI_BAR_DECODE_OK;
}

GXOS_MANAGED_KERNEL_RESOURCE_STATUS gxos_managed_kernel_validate_resource(
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resource)
{
    uint64_t end;
    uint32_t known_flags =
        GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_WRITABLE |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_IO_PORT |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_PREFETCHABLE |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_ADDRESS_64 |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_CACHE_UNCACHED |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_PLATFORM |
        GX_MANAGED_DEVICE_RESOURCE_FLAG_PCI_ASSIGNED;

    if (resource == 0 || resource->Size != GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE ||
        resource->AbiVersion != GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1 ||
        resource->ResourceId == 0U || resource->OwnerDeviceKind == GX_MANAGED_DEVICE_KIND_UNKNOWN ||
        resource->ResourceType == GX_MANAGED_DEVICE_RESOURCE_TYPE_UNKNOWN ||
        resource->ResourceType > GX_MANAGED_DEVICE_RESOURCE_TYPE_INTERRUPT ||
        resource->Length == 0U || resource->Alignment == 0U ||
        !is_power_of_two(resource->Alignment) ||
        (resource->Flags & ~known_flags) != 0U ||
        resource->ReservedLocation != 0U || resource->Reserved0 != 0U ||
        resource->Reserved1 != 0U || !add_u64(resource->PhysicalBase,
                                               resource->Length, &end) ||
        end <= resource->PhysicalBase) {
        return GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED;
    }
    if (resource->ResourceType == GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT) {
        if ((resource->Flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_IO_PORT) == 0U ||
            resource->PhysicalBase > UINT16_MAX || end > (uint64_t)UINT16_MAX + 1U) {
            return GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED;
        }
    } else if (resource->ResourceType == GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO ||
               resource->ResourceType == GX_MANAGED_DEVICE_RESOURCE_TYPE_PLATFORM_MEMORY) {
        if ((resource->Flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY) == 0U) {
            return GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED;
        }
    }
    if ((resource->Flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_PREFETCHABLE) != 0U &&
        (resource->Flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY) == 0U) {
        return GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED;
    }
    return GXOS_MANAGED_KERNEL_RESOURCE_OK;
}

int gxos_managed_kernel_resource_ranges_overlap(
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *left,
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *right)
{
    uint64_t left_end;
    uint64_t right_end;
    if (left == 0 || right == 0 ||
        gxos_managed_kernel_validate_resource(left) != GXOS_MANAGED_KERNEL_RESOURCE_OK ||
        gxos_managed_kernel_validate_resource(right) != GXOS_MANAGED_KERNEL_RESOURCE_OK ||
        left->ResourceType != right->ResourceType ||
        !add_u64(left->PhysicalBase, left->Length, &left_end) ||
        !add_u64(right->PhysicalBase, right->Length, &right_end)) {
        return 0;
    }
    return left->PhysicalBase < right_end && right->PhysicalBase < left_end;
}

GXOS_MANAGED_KERNEL_RESOURCE_STATUS gxos_managed_kernel_append_pci_mmio_resource(
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources,
    uint32_t resource_capacity,
    uint32_t *resource_count,
    uint16_t segment,
    uint8_t bus,
    uint8_t device,
    uint8_t function,
    uint16_t vendor_id,
    uint16_t device_id,
    uint8_t bar_index,
    uint32_t raw_low,
    uint32_t raw_high,
    const GXOS_PCI_FIRMWARE_BAR *firmware_bar,
    GXOS_PCI_BAR_DECODED *decoded_out)
{
    GXOS_PCI_BAR_DECODED decoded;
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resource = {0};
    uint64_t mask;
    uint32_t mask_low;
    uint32_t mask_high;
    uint32_t index;

    if (decoded_out != 0) *decoded_out = (GXOS_PCI_BAR_DECODED){0};
    if (resources == 0 || resource_count == 0 || firmware_bar == 0 ||
        *resource_count > resource_capacity || resource_capacity == 0 ||
        segment != 0U || firmware_bar->base == 0 || firmware_bar->length == 0 ||
        bar_index >= 6U || firmware_bar->length < 16U ||
        (firmware_bar->length & (firmware_bar->length - 1U)) != 0U ||
        firmware_bar->length > UINT64_MAX - firmware_bar->base) {
        return GXOS_MANAGED_KERNEL_RESOURCE_INVALID_ARGUMENT;
    }
    if ((raw_low & 1U) != 0U || ((raw_low >> 1) & 3U) == 1U ||
        ((raw_low >> 1) & 3U) == 3U ||
        (((raw_low >> 1) & 3U) == 2U && bar_index == 5U)) {
        return GXOS_MANAGED_KERNEL_RESOURCE_UNSUPPORTED;
    }
    if (((raw_low >> 1) & 3U) != 2U && firmware_bar->length > UINT32_MAX + 1ULL) {
        return GXOS_MANAGED_KERNEL_RESOURCE_OVERFLOW;
    }
    mask = ~(firmware_bar->length - 1U);
    mask_low = (uint32_t)mask | (raw_low & 0xFU);
    mask_high = (uint32_t)(mask >> 32);
    if (gxos_pci_decode_bar(raw_low, raw_high, mask_low, mask_high,
                            &decoded) != GXOS_PCI_BAR_DECODE_OK ||
        decoded.resource_type != GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO ||
        decoded.base != firmware_bar->base ||
        decoded.length != firmware_bar->length) {
        return GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED;
    }
    resource.Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE;
    resource.AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1;
    resource.ResourceId = 0x47584F5302000000ULL |
        ((uint64_t)bus << 24) | ((uint64_t)device << 16) |
        ((uint64_t)function << 8) | bar_index;
    resource.OwnerDeviceKind = GX_MANAGED_DEVICE_KIND_PCI;
    resource.OwnerDeviceId = ((uint32_t)vendor_id << 16) | device_id;
    resource.OwnerSegment = segment;
    resource.OwnerBus = bus;
    resource.OwnerDevice = device;
    resource.OwnerFunction = function;
    resource.ResourceIndex = bar_index;
    resource.ResourceType = GX_MANAGED_DEVICE_RESOURCE_TYPE_MMIO;
    resource.Flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_WRITABLE |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_CACHE_UNCACHED |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_PCI_ASSIGNED |
                     decoded.flags;
    resource.PhysicalBase = decoded.base;
    resource.Length = decoded.length;
    resource.Alignment = decoded.alignment;
    if (gxos_managed_kernel_validate_resource(&resource) !=
            GXOS_MANAGED_KERNEL_RESOURCE_OK) {
        return GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED;
    }
    for (index = 0; index != *resource_count; ++index) {
        if (resources[index].ResourceId == resource.ResourceId) {
            return GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE;
        }
        if (gxos_managed_kernel_resource_ranges_overlap(
                &resources[index], &resource)) {
            return GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE;
        }
    }
    if (*resource_count >= resource_capacity) {
        return GXOS_MANAGED_KERNEL_RESOURCE_CAPACITY;
    }
    resources[*resource_count] = resource;
    (*resource_count)++;
    if (decoded_out != 0) *decoded_out = decoded;
    return GXOS_MANAGED_KERNEL_RESOURCE_OK;
}

static GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 make_platform_resource(
    uint64_t resource_id, uint32_t device_kind, uint32_t device_id,
    uint16_t resource_index, uint64_t base, uint64_t length)
{
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resource = {0};
    resource.Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE;
    resource.AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1;
    resource.ResourceId = resource_id;
    resource.OwnerDeviceKind = device_kind;
    resource.OwnerDeviceId = device_id;
    resource.ResourceIndex = resource_index;
    resource.ResourceType = GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT;
    resource.Flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_IO_PORT |
                     GX_MANAGED_DEVICE_RESOURCE_FLAG_PLATFORM;
    resource.PhysicalBase = base;
    resource.Length = length;
    resource.Alignment = 1U;
    return resource;
}

GXOS_MANAGED_KERNEL_RESOURCE_STATUS gxos_managed_kernel_make_platform_resources(
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources,
    uint32_t resource_capacity,
    uint32_t *resource_count,
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_SUMMARY_V1 *summary)
{
    uint32_t index;
    if (resources == 0 || resource_count == 0 || summary == 0 ||
        resource_capacity < 3U || resource_capacity > GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_DESCRIPTORS) {
        return GXOS_MANAGED_KERNEL_RESOURCE_INVALID_ARGUMENT;
    }
    resources[0] = make_platform_resource(
        0x47584F5301000001ULL, GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL,
        GX_MANAGED_SERIAL_DEVICE_ID_COM1, 0U, 0x3F8U, 8U);
    resources[1] = make_platform_resource(
        0x47584F5301000002ULL, GX_MANAGED_DEVICE_KIND_PLATFORM_KEYBOARD,
        GX_MANAGED_KEYBOARD_DEVICE_ID_I8042, 0U, 0x60U, 1U);
    resources[2] = make_platform_resource(
        0x47584F5301000003ULL, GX_MANAGED_DEVICE_KIND_PLATFORM_KEYBOARD,
        GX_MANAGED_KEYBOARD_DEVICE_ID_I8042, 1U, 0x64U, 1U);
    for (index = 0; index != 3U; ++index) {
        if (gxos_managed_kernel_validate_resource(&resources[index]) !=
                GXOS_MANAGED_KERNEL_RESOURCE_OK) {
            return GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED;
        }
        if (index != 0U && gxos_managed_kernel_resource_ranges_overlap(
                &resources[index - 1U], &resources[index])) {
            return GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE;
        }
    }
    *resource_count = 3U;
    *summary = (GX_MANAGED_KERNEL_DEVICE_RESOURCE_SUMMARY_V1){
        .Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_SUMMARY_V1_SIZE,
        .AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1,
        .ServiceVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_SERVICE_VERSION_V1,
        .Architecture = GX_MANAGED_KERNEL_ARCH_X64,
        .ResourceCount = 3U,
        .MaxClaims = GX_MANAGED_KERNEL_DEVICE_RESOURCE_MAX_CLAIMS,
        .Capabilities = GX_MANAGED_DEVICE_RESOURCE_CAPABILITY_SUMMARY |
                        GX_MANAGED_DEVICE_RESOURCE_CAPABILITY_DESCRIPTORS |
                        GX_MANAGED_DEVICE_RESOURCE_CAPABILITY_IMMUTABLE_PUBLICATION |
                        GX_MANAGED_DEVICE_RESOURCE_CAPABILITY_CLAIM_POLICY,
        .Reserved = 0U
    };
    return GXOS_MANAGED_KERNEL_RESOURCE_OK;
}

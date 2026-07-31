#include "crt_strlen.h"

static int gxos_crt_strlen_is_canonical(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFU
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
#else
    (void)address;
    return 1;
#endif
}

static int gxos_crt_strlen_context_is_valid(const GXOS_READABLE_IMAGE *image)
{
    uint32_t index;

    if (image == 0 || image->memory_region_count == 0 ||
        image->memory_region_count > GXOS_CRT_STRLEN_MAX_MEMORY_REGIONS ||
        image->memory_regions == 0) {
        return 0;
    }
    if (image->image_base == 0 || image->image_end <= image->image_base ||
        !gxos_crt_strlen_is_canonical(image->image_base) ||
        !gxos_crt_strlen_is_canonical(image->image_end - 1)) {
        return 0;
    }
    for (index = 0; index != image->memory_region_count; index++) {
        const GXOS_CRT_INITTERM_MEMORY_REGION *region = &image->memory_regions[index];
        if (region->base >= region->end ||
            !gxos_crt_strlen_is_canonical(region->base) ||
            !gxos_crt_strlen_is_canonical(region->end - 1)) {
            return 0;
        }
    }
    return image->relocations_applied != 0;
}

static const GXOS_CRT_INITTERM_MEMORY_REGION *gxos_crt_strlen_find_region(
    uintptr_t address,
    const GXOS_READABLE_IMAGE *image)
{
    uint32_t index;

    for (index = 0; index != image->memory_region_count; index++) {
        const GXOS_CRT_INITTERM_MEMORY_REGION *region = &image->memory_regions[index];
        if (address >= region->base && address < region->end) return region;
    }
    return 0;
}

GXOS_CRT_STRLEN_STATUS GXOS_CRT_STRLEN_MS_ABI gxos_crt_strlen_checked(
    const char *string,
    const GXOS_READABLE_IMAGE *image,
    size_t maximum_scan,
    size_t *length_out)
{
    uintptr_t base;
    size_t length = 0;

    if (length_out == 0) return GXOS_CRT_STRLEN_STATUS_INVALID_OUTPUT;
    if (image == 0 || !gxos_crt_strlen_context_is_valid(image)) {
        return GXOS_CRT_STRLEN_STATUS_INVALID_CONTEXT;
    }
    if (string == 0) return GXOS_CRT_STRLEN_STATUS_NULL_POINTER;
    base = (uintptr_t)string;
    if (!gxos_crt_strlen_is_canonical(base)) {
        return GXOS_CRT_STRLEN_STATUS_NONCANONICAL_POINTER;
    }
    if (maximum_scan == 0) return GXOS_CRT_STRLEN_STATUS_UNTERMINATED;
    if ((uintptr_t)(maximum_scan - 1) > UINTPTR_MAX - base) {
        return GXOS_CRT_STRLEN_STATUS_OVERFLOW;
    }

    if (base >= image->image_base && base < image->image_end &&
        image->relocations_applied == 0) {
        return GXOS_CRT_STRLEN_STATUS_INVALID_CONTEXT;
    }

    for (;;) {
        uintptr_t current;
        const GXOS_CRT_INITTERM_MEMORY_REGION *region;
        unsigned char byte;

        if (length >= maximum_scan) return GXOS_CRT_STRLEN_STATUS_UNTERMINATED;
        if ((uintptr_t)length > UINTPTR_MAX - base) {
            return GXOS_CRT_STRLEN_STATUS_OVERFLOW;
        }
        current = base + (uintptr_t)length;
        if (!gxos_crt_strlen_is_canonical(current)) {
            return GXOS_CRT_STRLEN_STATUS_NONCANONICAL_POINTER;
        }
        region = gxos_crt_strlen_find_region(current, image);
        if (region == 0 || region->readable == 0) {
            return GXOS_CRT_STRLEN_STATUS_UNREADABLE_POINTER;
        }
        byte = *(const unsigned char *)(uintptr_t)current;
        if (byte == 0) {
            *length_out = length;
            return GXOS_CRT_STRLEN_STATUS_OK;
        }
        if (length == SIZE_MAX) return GXOS_CRT_STRLEN_STATUS_OVERFLOW;
        length++;
    }
}

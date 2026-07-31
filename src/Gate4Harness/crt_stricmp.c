#include "crt_stricmp.h"

static int gxos_crt_stricmp_is_canonical(uintptr_t address)
{
#if UINTPTR_MAX > 0xFFFFFFFFU
    uint64_t high = (uint64_t)address >> 47;
    return high == 0 || high == 0x1FFFFU;
#else
    (void)address;
    return 1;
#endif
}

static int gxos_crt_stricmp_context_is_valid(const GXOS_READABLE_IMAGE *image)
{
    uint32_t index;

    if (image == 0 || image->memory_region_count == 0 ||
        image->memory_region_count > GXOS_CRT_STRICMP_MAX_MEMORY_REGIONS ||
        image->memory_regions == 0) {
        return 0;
    }
    if (image->image_base == 0 || image->image_end <= image->image_base ||
        !gxos_crt_stricmp_is_canonical(image->image_base) ||
        !gxos_crt_stricmp_is_canonical(image->image_end - 1)) {
        return 0;
    }
    for (index = 0; index != image->memory_region_count; index++) {
        const GXOS_CRT_INITTERM_MEMORY_REGION *region = &image->memory_regions[index];
        if (region->base == 0 || region->base >= region->end ||
            !gxos_crt_stricmp_is_canonical(region->base) ||
            !gxos_crt_stricmp_is_canonical(region->end - 1)) {
            return 0;
        }
    }
    return image->relocations_applied != 0;
}

static const GXOS_CRT_INITTERM_MEMORY_REGION *gxos_crt_stricmp_find_region(
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

static unsigned char gxos_crt_stricmp_ascii_tolower(unsigned char value)
{
    if (value >= (unsigned char)'A' && value <= (unsigned char)'Z') {
        return (unsigned char)(value + ((unsigned char)'a' - (unsigned char)'A'));
    }
    return value;
}

GXOS_CRT_STRICMP_STATUS GXOS_CRT_STRICMP_MS_ABI gxos_crt_stricmp_checked_report(
    const char *string1,
    const char *string2,
    const GXOS_READABLE_IMAGE *image,
    size_t maximum_scan_per_string,
    int *comparison_out,
    GXOS_CRT_STRICMP_REPORT *report)
{
    uintptr_t base1;
    uintptr_t base2;
    size_t index;

    if (comparison_out == 0) return GXOS_CRT_STRICMP_STATUS_INVALID_OUTPUT;
    if (report != 0) {
        report->string1_length = SIZE_MAX;
        report->string2_length = SIZE_MAX;
        report->string1_terminator = 0;
        report->string2_terminator = 0;
        report->bytes_examined = 0;
        report->compared_prefix = 0;
    }
    if (!gxos_crt_stricmp_context_is_valid(image)) {
        return GXOS_CRT_STRICMP_STATUS_INVALID_CONTEXT;
    }
    if (string1 == 0 || string2 == 0) return GXOS_CRT_STRICMP_STATUS_NULL_POINTER;
    base1 = (uintptr_t)string1;
    base2 = (uintptr_t)string2;
    if (!gxos_crt_stricmp_is_canonical(base1) ||
        !gxos_crt_stricmp_is_canonical(base2)) {
        return GXOS_CRT_STRICMP_STATUS_NONCANONICAL_POINTER;
    }
    if (maximum_scan_per_string == 0) return GXOS_CRT_STRICMP_STATUS_SCAN_LIMIT;
    if ((uintptr_t)(maximum_scan_per_string - 1) > UINTPTR_MAX - base1 ||
        (uintptr_t)(maximum_scan_per_string - 1) > UINTPTR_MAX - base2) {
        return GXOS_CRT_STRICMP_STATUS_POINTER_OVERFLOW;
    }

    for (index = 0; index != maximum_scan_per_string; index++) {
        uintptr_t current1 = base1 + (uintptr_t)index;
        uintptr_t current2 = base2 + (uintptr_t)index;
        const GXOS_CRT_INITTERM_MEMORY_REGION *region1;
        const GXOS_CRT_INITTERM_MEMORY_REGION *region2;
        unsigned char value1;
        unsigned char value2;
        unsigned char folded1;
        unsigned char folded2;

        if (!gxos_crt_stricmp_is_canonical(current1) ||
            !gxos_crt_stricmp_is_canonical(current2)) {
            return GXOS_CRT_STRICMP_STATUS_NONCANONICAL_POINTER;
        }
        region1 = gxos_crt_stricmp_find_region(current1, image);
        region2 = gxos_crt_stricmp_find_region(current2, image);
        if (region1 == 0 || region2 == 0 || region1->readable == 0 ||
            region2->readable == 0) {
            return GXOS_CRT_STRICMP_STATUS_UNREADABLE_POINTER;
        }
        value1 = *(const unsigned char *)(uintptr_t)current1;
        value2 = *(const unsigned char *)(uintptr_t)current2;
        folded1 = gxos_crt_stricmp_ascii_tolower(value1);
        folded2 = gxos_crt_stricmp_ascii_tolower(value2);
        if (report != 0) {
            if (report->bytes_examined <= SIZE_MAX - 2) report->bytes_examined += 2;
            else report->bytes_examined = SIZE_MAX;
            report->compared_prefix = index + 1;
        }
        if (folded1 != folded2) {
            *comparison_out = (int)folded1 - (int)folded2;
            return GXOS_CRT_STRICMP_STATUS_OK;
        }
        if (folded1 == 0) {
            if (report != 0) {
                report->string1_length = index;
                report->string2_length = index;
                report->string1_terminator = current1;
                report->string2_terminator = current2;
            }
            *comparison_out = 0;
            return GXOS_CRT_STRICMP_STATUS_OK;
        }
    }
    return GXOS_CRT_STRICMP_STATUS_SCAN_LIMIT;
}

GXOS_CRT_STRICMP_STATUS GXOS_CRT_STRICMP_MS_ABI gxos_crt_stricmp_checked(
    const char *string1,
    const char *string2,
    const GXOS_READABLE_IMAGE *image,
    size_t maximum_scan_per_string,
    int *comparison_out)
{
    return gxos_crt_stricmp_checked_report(string1, string2, image,
                                            maximum_scan_per_string,
                                            comparison_out, 0);
}

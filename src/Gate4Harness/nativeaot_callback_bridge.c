#include "nativeaot_callback_bridge.h"

static uint16_t read_u16(const uint8_t *address)
{
    return (uint16_t)address[0] | ((uint16_t)address[1] << 8);
}

static uint32_t read_u32(const uint8_t *address)
{
    return (uint32_t)read_u16(address) |
           ((uint32_t)read_u16(address + 2) << 16);
}

static int range_valid(uint64_t image_size, uint32_t rva, uint64_t size)
{
    return (uint64_t)rva <= image_size && size <= image_size - rva;
}

static int text_equals_bounded(const uint8_t *image,
                               uint64_t image_size,
                               uint32_t rva,
                               const char *name)
{
    uint32_t index = 0;

    while (index != 4096U && (uint64_t)rva + index < image_size) {
        uint8_t actual = image[(uint64_t)rva + index];
        char expected = name[index];
        if (actual != (uint8_t)expected) return 0;
        if (actual == 0) return 1;
        index++;
    }
    return 0;
}

GXOS_NATIVEAOT_EXPORT_STATUS gxos_nativeaot_find_export(
    const GXOS_NATIVEAOT_EXPORT_IMAGE *image,
    const char *name,
    GXOS_NATIVEAOT_EXPORT_RESOLUTION *resolution)
{
    const uint8_t *base;
    uint32_t name_count;
    uint32_t function_count;
    uint32_t ordinal_base;
    uint32_t functions_rva;
    uint32_t names_rva;
    uint32_t ordinals_rva;
    uint32_t index;

    if (image == 0 || image->loaded_image == 0) {
        return GXOS_NATIVEAOT_EXPORT_NULL_IMAGE;
    }
    if (name == 0 || name[0] == 0) {
        return GXOS_NATIVEAOT_EXPORT_NULL_NAME;
    }
    if (resolution == 0 || image->loaded_size > UINT32_MAX ||
        !range_valid(image->loaded_size, image->export_rva,
                     image->export_size) || image->export_size < 40U) {
        return GXOS_NATIVEAOT_EXPORT_INVALID_DIRECTORY;
    }

    base = image->loaded_image + image->export_rva;
    ordinal_base = read_u32(base + 16);
    function_count = read_u32(base + 20);
    name_count = read_u32(base + 24);
    functions_rva = read_u32(base + 28);
    names_rva = read_u32(base + 32);
    ordinals_rva = read_u32(base + 36);
    if (function_count == 0 || name_count > function_count ||
        !range_valid(image->loaded_size, functions_rva,
                     (uint64_t)function_count * 4U) ||
        !range_valid(image->loaded_size, names_rva,
                     (uint64_t)name_count * 4U) ||
        !range_valid(image->loaded_size, ordinals_rva,
                     (uint64_t)name_count * 2U)) {
        return GXOS_NATIVEAOT_EXPORT_INVALID_TABLE;
    }

    for (index = 0; index != name_count; index++) {
        uint32_t name_rva = read_u32(image->loaded_image + names_rva +
                                     (uint64_t)index * 4U);
        uint16_t ordinal_index = read_u16(image->loaded_image + ordinals_rva +
                                          (uint64_t)index * 2U);
        uint32_t function_rva;
        if (ordinal_index >= function_count ||
            !range_valid(image->loaded_size, name_rva, 1U) ||
            !text_equals_bounded(image->loaded_image, image->loaded_size,
                                 name_rva, name)) {
            continue;
        }
        function_rva = read_u32(image->loaded_image + functions_rva +
                                (uint64_t)ordinal_index * 4U);
        if (function_rva == 0 || !range_valid(image->loaded_size, function_rva, 1U)) {
            return GXOS_NATIVEAOT_EXPORT_INVALID_TABLE;
        }
        if (range_valid(image->loaded_size, function_rva,
                        image->export_size) &&
            function_rva >= image->export_rva &&
            function_rva - image->export_rva < image->export_size) {
            return GXOS_NATIVEAOT_EXPORT_FORWARDER;
        }
        resolution->rva = function_rva;
        resolution->ordinal = ordinal_base + ordinal_index;
        resolution->address = (uintptr_t)(image->loaded_image + function_rva);
        return GXOS_NATIVEAOT_EXPORT_OK;
    }
    return GXOS_NATIVEAOT_EXPORT_NOT_FOUND;
}

int gxos_nativeaot_callback_register(
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge,
    const GXOS_NATIVEAOT_EXPORT_RESOLUTION *resolution)
{
    if (bridge == 0 || resolution == 0 || resolution->address == 0 ||
        resolution->rva == 0) {
        return 0;
    }
    bridge->callback = resolution->address;
    bridge->rva = resolution->rva;
    bridge->ready = 0;
    bridge->invocation_count = 0;
    return 1;
}

int gxos_nativeaot_callback_mark_ready(
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge)
{
    if (bridge == 0 || bridge->callback == 0 || bridge->rva == 0) return 0;
    bridge->ready = 1;
    return 1;
}

GXOS_NATIVEAOT_CALLBACK_STATUS GXOS_NATIVEAOT_MS_ABI
gxos_nativeaot_callback_invoke(
    GXOS_NATIVEAOT_CALLBACK_BRIDGE *bridge,
    int32_t input,
    int32_t *result)
{
    GXOS_NATIVEAOT_CALLBACK32 callback;
    uint32_t mxcsr = 0x1F80;
    uint16_t x87_control = 0x037F;

    if (bridge == 0) return GXOS_NATIVEAOT_CALLBACK_NULL_BRIDGE;
    if (result == 0) return GXOS_NATIVEAOT_CALLBACK_NULL_RESULT;
    if (bridge->callback == 0 || bridge->rva == 0) {
        return GXOS_NATIVEAOT_CALLBACK_NOT_REGISTERED;
    }
    if (bridge->ready == 0) return GXOS_NATIVEAOT_CALLBACK_NOT_READY;

#if defined(__x86_64__)
    __asm__ volatile (
        "cld\n"
        "ldmxcsr %0\n"
        "fldcw %1\n"
        :
        : "m"(mxcsr), "m"(x87_control));
#else
    (void)mxcsr;
    (void)x87_control;
#endif
    callback = (GXOS_NATIVEAOT_CALLBACK32)(uintptr_t)bridge->callback;
    *result = callback(input);
    bridge->invocation_count++;
    return GXOS_NATIVEAOT_CALLBACK_OK;
}

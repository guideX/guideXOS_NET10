#include "platform_module_registry.h"

typedef struct {
    uint32_t identity;
    const char *canonical_name;
} GXOS_BUILTIN_MODULE_DESCRIPTOR;

static const GXOS_BUILTIN_MODULE_DESCRIPTOR g_kernel32_descriptor = {
    0x4B33324DU,
    "KERNEL32.dll"
};

static uint16_t gxos_module_registry_fold_ascii(uint16_t value)
{
    if (value >= (uint16_t)'A' && value <= (uint16_t)'Z') {
        return (uint16_t)(value + ((uint16_t)'a' - (uint16_t)'A'));
    }
    return value;
}

static int gxos_module_registry_matches_literal(
    const uint16_t *name,
    uint32_t length,
    const char *literal)
{
    uint32_t index = 0;

    while (literal[index] != 0) {
        if (index >= length ||
            gxos_module_registry_fold_ascii(name[index]) !=
                (uint16_t)(uint8_t)literal[index]) {
            return 0;
        }
        ++index;
    }
    return index == length;
}

uintptr_t gxos_module_registry_kernel32_handle(void)
{
    return (uintptr_t)&g_kernel32_descriptor;
}

int gxos_module_registry_is_kernel32_handle(uintptr_t module_handle)
{
    return module_handle == gxos_module_registry_kernel32_handle();
}

int gxos_module_registry_kernel32_name_matches(
    const uint16_t *name,
    uint32_t length)
{
    if (name == 0) return 0;
    return gxos_module_registry_matches_literal(name, length, "kernel32") ||
           gxos_module_registry_matches_literal(name, length, "kernel32.dll");
}

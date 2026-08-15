#ifndef GXOS_PLATFORM_MODULE_REGISTRY_H
#define GXOS_PLATFORM_MODULE_REGISTRY_H

#include <stdint.h>

/*
 * This is the process-local identity of a built-in guideXOS compatibility
 * module.  It is deliberately an address of a registered descriptor, not a
 * numeric sentinel or a mapped PE image base.
 */
uintptr_t gxos_module_registry_kernel32_handle(void);

int gxos_module_registry_is_kernel32_handle(uintptr_t module_handle);

/* The caller must have already bounded and validated the UTF-16 buffer. */
int gxos_module_registry_kernel32_name_matches(
    const uint16_t *name,
    uint32_t length);

#endif

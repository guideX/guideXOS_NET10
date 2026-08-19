#ifndef GXOS_MANAGED_KERNEL_BOOT_RESOURCES_H
#define GXOS_MANAGED_KERNEL_BOOT_RESOURCES_H

#include "managed_kernel_abi.h"
#include "memory_accounting.h"

typedef enum {
    GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OK = 0,
    GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_INVALID_ARGUMENT,
    GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_MALFORMED,
    GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_OVERFLOW,
    GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_CAPACITY
} GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_STATUS;

/* Copy the validated native firmware map into the public guideXOS ABI shape.
   The caller owns the output storage; no pointer into the firmware map is
   returned or retained. */
GXOS_MANAGED_BOOT_RESOURCE_NORMALIZATION_STATUS
gxos_managed_kernel_normalize_boot_resources(
    const GXOS_UEFI_MEMORY_MAP *map,
    const GXOS_MEMORY_CLASSIFICATION *classification,
    GX_MANAGED_KERNEL_BOOT_RESOURCE_REGION_V1 *regions,
    uint32_t region_capacity,
    GX_MANAGED_KERNEL_BOOT_RESOURCE_SUMMARY_V1 *summary);

#endif

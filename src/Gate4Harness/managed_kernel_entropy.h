#ifndef GXOS_MANAGED_KERNEL_ENTROPY_H
#define GXOS_MANAGED_KERNEL_ENTROPY_H

#include <stdint.h>

#include "managed_kernel_abi.h"

void gxos_managed_kernel_entropy_prepare(
    GX_MANAGED_KERNEL_ENTROPY_SERVICES_V1 *services);

GX_MANAGED_STATUS GX_MANAGED_KERNEL_MS_ABI
gxos_managed_kernel_entropy_fill(uintptr_t buffer_address,
                                 uint32_t byte_length);

uint32_t gxos_managed_kernel_entropy_max_basic_leaf(void);
uint32_t gxos_managed_kernel_entropy_leaf1_ecx(void);
uint32_t gxos_managed_kernel_entropy_leaf7_ebx(void);
uint32_t gxos_managed_kernel_entropy_feature_flags(void);

#endif

#ifndef GXOS_MANAGED_KERNEL_HOST_SERVICES_H
#define GXOS_MANAGED_KERNEL_HOST_SERVICES_H

#include <stdint.h>

#include "managed_kernel_abi.h"

typedef void (*GXOS_MANAGED_KERNEL_HOST_LOG_SINK)(
    const uint8_t *bytes, uintptr_t byte_length);
typedef int (*GXOS_MANAGED_KERNEL_HOST_RANGE_VALIDATOR)(
    uintptr_t address, uintptr_t byte_length);

GX_MANAGED_STATUS gxos_managed_kernel_host_validate_log_request(
    uintptr_t bytes_address, uintptr_t byte_length, uint32_t flags);

GX_MANAGED_STATUS gxos_managed_kernel_host_log_utf8(
    uintptr_t bytes_address, uintptr_t byte_length, uint32_t flags,
    GXOS_MANAGED_KERNEL_HOST_LOG_SINK sink,
    GXOS_MANAGED_KERNEL_HOST_RANGE_VALIDATOR range_validator);

GX_MANAGED_STATUS gxos_managed_kernel_host_validate_output_range(
    uintptr_t output_address, uintptr_t output_capacity, uintptr_t required_size);

#endif

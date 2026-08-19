#include "managed_kernel_host_services.h"

GX_MANAGED_STATUS gxos_managed_kernel_host_validate_log_request(
    uintptr_t bytes_address, uintptr_t byte_length, uint32_t flags)
{
    if (flags != 0 || byte_length > GX_MANAGED_KERNEL_HOST_LOG_MAX_BYTES) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    if (byte_length != 0 &&
        (bytes_address == 0 || byte_length > UINTPTR_MAX - bytes_address)) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

GX_MANAGED_STATUS gxos_managed_kernel_host_log_utf8(
    uintptr_t bytes_address, uintptr_t byte_length, uint32_t flags,
    GXOS_MANAGED_KERNEL_HOST_LOG_SINK sink,
    GXOS_MANAGED_KERNEL_HOST_RANGE_VALIDATOR range_validator)
{
    GX_MANAGED_STATUS status = gxos_managed_kernel_host_validate_log_request(
        bytes_address, byte_length, flags);
    if (status != GX_MANAGED_OK) return status;
    if (byte_length == 0) return GX_MANAGED_OK;
    if (sink == 0 || range_validator == 0 ||
        !range_validator(bytes_address, byte_length)) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    sink((const uint8_t *)(uintptr_t)bytes_address, byte_length);
    return GX_MANAGED_OK;
}

GX_MANAGED_STATUS gxos_managed_kernel_host_validate_output_range(
    uintptr_t output_address, uintptr_t output_capacity, uintptr_t required_size)
{
    if (output_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (output_capacity < required_size) return GX_MANAGED_BUFFER_TOO_SMALL;
    if (output_capacity > UINTPTR_MAX - output_address) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

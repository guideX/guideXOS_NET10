#include "managed_kernel_serial.h"

static int range_is_valid(uintptr_t address, uintptr_t byte_length)
{
    return address != 0 && byte_length != 0 &&
           byte_length <= UINTPTR_MAX - address;
}

static int context_is_valid(const GXOS_MANAGED_KERNEL_SERIAL_CONTEXT *context)
{
    uint64_t known_capabilities =
        GX_MANAGED_SERIAL_CAPABILITY_TRANSMIT |
        GX_MANAGED_SERIAL_CAPABILITY_QUERY_STATUS;
    return context != 0 && context->device_kind ==
               GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL &&
           context->device_id == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
           context->com_index == GX_MANAGED_SERIAL_COM_INDEX_1 &&
           context->present != 0 && context->capabilities != 0 &&
           (context->capabilities & ~known_capabilities) == 0 &&
           (context->capabilities & GX_MANAGED_SERIAL_CAPABILITY_TRANSMIT) != 0 &&
           (context->capabilities & GX_MANAGED_SERIAL_CAPABILITY_QUERY_STATUS) != 0 &&
           context->max_transmit_bytes != 0 &&
           context->max_transmit_bytes <= GX_MANAGED_KERNEL_SERIAL_MAX_TRANSMIT_BYTES &&
           context->tx_poll_limit != 0 && context->range_is_known != 0 &&
           context->transmitter_ready != 0 && context->transmit_byte != 0;
}

static uint32_t validate_result_buffer(
    const GXOS_MANAGED_KERNEL_SERIAL_CONTEXT *context,
    uintptr_t result_address, uintptr_t result_capacity)
{
    if (result_address == 0) return GX_MANAGED_INVALID_ARGUMENT;
    if (result_capacity < GX_MANAGED_KERNEL_SERIAL_STATUS_V1_SIZE) {
        return GX_MANAGED_BUFFER_TOO_SMALL;
    }
    if (result_capacity > UINTPTR_MAX - result_address ||
        context == 0 || context->range_is_known == 0 ||
        !context->range_is_known(context->hardware_context, result_address,
                                  GX_MANAGED_KERNEL_SERIAL_STATUS_V1_SIZE)) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_serial_write_v1(
    GXOS_MANAGED_KERNEL_SERIAL_CONTEXT *context,
    uint32_t device_id, uintptr_t buffer_address, uint32_t byte_length,
    uint32_t flags)
{
    uint32_t index;

    if (!context_is_valid(context)) return GX_MANAGED_INVALID_STATE;
    if ((context->capabilities & GX_MANAGED_SERIAL_CAPABILITY_TRANSMIT) == 0) {
        return GX_MANAGED_INVALID_STATE;
    }
    if (device_id != context->device_id || flags != GX_MANAGED_SERIAL_FLAG_NONE) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    if (byte_length > context->max_transmit_bytes) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }
    if (byte_length == 0) return GX_MANAGED_OK;
    if (!range_is_valid(buffer_address, byte_length) ||
        !context->range_is_known(context->hardware_context, buffer_address,
                                 byte_length)) {
        return GX_MANAGED_INVALID_ARGUMENT;
    }

    for (index = 0; index != byte_length; ++index) {
        uint32_t poll;
        int ready = 0;
        for (poll = 0; poll != context->tx_poll_limit; ++poll) {
            if (context->transmitter_ready(context->hardware_context)) {
                ready = 1;
                break;
            }
        }
        if (!ready) {
            context->timeout_count++;
            return GX_MANAGED_TIMEOUT;
        }
        if (!context->transmit_byte(
                context->hardware_context,
                ((const uint8_t *)(uintptr_t)buffer_address)[index])) {
            return GX_MANAGED_INVALID_STATE;
        }
    }
    context->successful_transmit_count++;
    return GX_MANAGED_OK;
}

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_serial_query_status_v1(
    GXOS_MANAGED_KERNEL_SERIAL_CONTEXT *context,
    uint32_t requested_abi_version, uint32_t device_id,
    uintptr_t result_address, uintptr_t result_capacity)
{
    GX_MANAGED_KERNEL_SERIAL_STATUS_V1 result;
    uint32_t poll;
    int ready = 0;
    uint32_t buffer_status;

    if (requested_abi_version != GX_MANAGED_KERNEL_SERIAL_SERVICES_ABI_V1) {
        return GX_MANAGED_UNSUPPORTED_ABI;
    }
    if (!context_is_valid(context)) return GX_MANAGED_INVALID_STATE;
    if ((context->capabilities & GX_MANAGED_SERIAL_CAPABILITY_QUERY_STATUS) == 0) {
        return GX_MANAGED_INVALID_STATE;
    }
    if (device_id != context->device_id) return GX_MANAGED_INVALID_ARGUMENT;
    buffer_status = validate_result_buffer(context, result_address,
                                           result_capacity);
    if (buffer_status != GX_MANAGED_OK) return buffer_status;
    for (poll = 0; poll != context->tx_poll_limit; ++poll) {
        if (context->transmitter_ready(context->hardware_context)) {
            ready = 1;
            break;
        }
    }
    if (!ready) {
        context->timeout_count++;
        return GX_MANAGED_TIMEOUT;
    }
    result.Size = GX_MANAGED_KERNEL_SERIAL_STATUS_V1_SIZE;
    result.AbiVersion = GX_MANAGED_KERNEL_SERIAL_SERVICES_ABI_V1;
    result.Status = GX_MANAGED_SERIAL_STATUS_DEVICE_PRESENT |
                    GX_MANAGED_SERIAL_STATUS_TRANSMITTER_READY;
    result.Reserved0 = 0;
    result.Capabilities = context->capabilities;
    result.Reserved1 = 0;
    *(GX_MANAGED_KERNEL_SERIAL_STATUS_V1 *)(uintptr_t)result_address = result;
    return GX_MANAGED_OK;
}

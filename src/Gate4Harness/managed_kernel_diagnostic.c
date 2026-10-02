#include "managed_kernel_diagnostic.h"

#include <stddef.h>

#define GXOS_DIAGNOSTIC_FRAME_VERSION 1U
#define GXOS_DIAGNOSTIC_STATUS_BODY_BYTES 76U
#define GXOS_DIAGNOSTIC_RESTART_BODY_BYTES 12U
#define GXOS_DIAGNOSTIC_RESPONSE_HEADER_BYTES 20U
#define GXOS_DIAGNOSTIC_RESPONSE_CRC_BYTES 4U
#define GXOS_DIAGNOSTIC_TARGET_COM1 1U

static uint16_t read_u16le(const uint8_t *bytes)
{
    return (uint16_t)((uint16_t)bytes[0] | ((uint16_t)bytes[1] << 8));
}

static uint32_t read_u32le(const uint8_t *bytes)
{
    return (uint32_t)bytes[0] | ((uint32_t)bytes[1] << 8) |
           ((uint32_t)bytes[2] << 16) | ((uint32_t)bytes[3] << 24);
}

static void write_u16le(uint8_t *bytes, uint16_t value)
{
    bytes[0] = (uint8_t)value;
    bytes[1] = (uint8_t)(value >> 8);
}

static void write_u32le(uint8_t *bytes, uint32_t value)
{
    bytes[0] = (uint8_t)value;
    bytes[1] = (uint8_t)(value >> 8);
    bytes[2] = (uint8_t)(value >> 16);
    bytes[3] = (uint8_t)(value >> 24);
}

uint32_t gxos_managed_kernel_diagnostic_crc32(
    const uint8_t *bytes, uint32_t byte_count)
{
    uint32_t crc = 0xFFFFFFFFU;
    uint32_t index;
    if (bytes == 0) return 0U;
    for (index = 0; index != byte_count; ++index) {
        uint32_t bit;
        crc ^= bytes[index];
        for (bit = 0; bit != 8U; ++bit) {
            uint32_t mask = (uint32_t)-(int32_t)(crc & 1U);
            crc = (crc >> 1) ^ (0xEDB88320U & mask);
        }
    }
    return crc ^ 0xFFFFFFFFU;
}

int gxos_managed_kernel_diagnostic_initialize(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *service_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *current_handle_out,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_API status_api,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_API restart_api,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_iir,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_lsr,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_data,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_TRANSMIT_BYTE transmit_byte,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_EOI send_eoi,
    void *uart_context)
{
    if (context == 0) return 0;
    *context = (GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT){0};
    if (resource == 0 || resource->present == 0U) return 1;
    if (resource->present != 1U || resource->enabled != 1U ||
        resource->io_base == 0U || resource->register_span != 8U ||
        resource->backend != GXOS_DIAGNOSTIC_UART_BACKEND_IO_16550 ||
        resource->irq >= GXOS_DIAGNOSTIC_UART_IRQ_COUNT ||
        service_context == 0 || current_handle_out == 0 ||
        status_api == 0 || restart_api == 0 ||
        read_iir == 0 || read_lsr == 0 || read_data == 0 ||
        transmit_byte == 0 || send_eoi == 0 || uart_context == 0) {
        return 0;
    }
    context->present = 1U;
    context->resource_source = resource->source;
    context->io_base = resource->io_base;
    context->register_span = resource->register_span;
    context->irq = resource->irq;
    context->backend = (uint8_t)resource->backend;
    context->service_context = service_context;
    context->current_handle_out = current_handle_out;
    context->status_api = status_api;
    context->restart_api = restart_api;
    context->read_iir = read_iir;
    context->read_lsr = read_lsr;
    context->read_data = read_data;
    context->transmit_byte = transmit_byte;
    context->send_eoi = send_eoi;
    context->uart_context = uart_context;
    return 1;
}

void gxos_managed_kernel_diagnostic_set_enabled(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context, int enabled)
{
    if (context == 0 || context->present == 0U) return;
    __atomic_store_n(&context->enabled, enabled != 0 ? 1U : 0U,
                     __ATOMIC_RELEASE);
}

uint32_t gxos_managed_kernel_diagnostic_irq_capture(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context)
{
    uint32_t captured = 0U;
    uint32_t index;
    uint8_t identification;
    if (context == 0 || context->present == 0U) return 0U;
    __atomic_add_fetch(&context->irq_count, 1U, __ATOMIC_RELAXED);
    if (__atomic_load_n(&context->enabled, __ATOMIC_ACQUIRE) == 0U ||
        context->read_iir == 0 || context->read_lsr == 0 ||
        context->read_data == 0) {
        if (context->send_eoi != 0) context->send_eoi(context->uart_context);
        return 0U;
    }
    identification = context->read_iir(context->uart_context);
    if ((identification & 0x01U) == 0U) {
        for (index = 0; index != GXOS_MANAGED_KERNEL_DIAGNOSTIC_IRQ_DRAIN_MAX;
             ++index) {
            uint8_t line_status = context->read_lsr(context->uart_context);
            uint8_t value;
            uint32_t count;
            uint32_t write_index;
            if ((line_status & 0x01U) == 0U) break;
            value = context->read_data(context->uart_context);
            count = __atomic_load_n(&context->rx_count, __ATOMIC_ACQUIRE);
            if (count >= GXOS_MANAGED_KERNEL_DIAGNOSTIC_RX_CAPACITY) {
                __atomic_add_fetch(&context->rx_overflow_count, 1U,
                                   __ATOMIC_RELAXED);
                __atomic_add_fetch(&context->overflow_generation, 1U,
                                   __ATOMIC_RELEASE);
                continue;
            }
            write_index = __atomic_load_n(&context->rx_write_index,
                                          __ATOMIC_RELAXED);
            context->rx_ring[write_index %
                GXOS_MANAGED_KERNEL_DIAGNOSTIC_RX_CAPACITY] = value;
            __atomic_store_n(&context->rx_write_index, write_index + 1U,
                             __ATOMIC_RELEASE);
            __atomic_add_fetch(&context->rx_count, 1U, __ATOMIC_RELEASE);
            __atomic_store_n(&context->rx_pending, 1U, __ATOMIC_RELEASE);
            __atomic_add_fetch(&context->irq_bytes, 1U, __ATOMIC_RELAXED);
            ++captured;
        }
        if (index == GXOS_MANAGED_KERNEL_DIAGNOSTIC_IRQ_DRAIN_MAX &&
            (context->read_lsr(context->uart_context) & 0x01U) != 0U) {
            __atomic_add_fetch(&context->irq_bound_hits, 1U,
                               __ATOMIC_RELAXED);
        }
    }
    if (context->send_eoi != 0) context->send_eoi(context->uart_context);
    return captured;
}

static int has_request_magic(const uint8_t *bytes, uint32_t offset)
{
    return bytes[offset] == 'G' && bytes[offset + 1U] == 'X' &&
           bytes[offset + 2U] == 'D' && bytes[offset + 3U] == 'C';
}

static int validate_request(const uint8_t *bytes)
{
    uint32_t command;
    uint32_t expected_identity;
    uint16_t expected_generation;
    if (bytes[0] != 'G' || bytes[1] != 'X' || bytes[2] != 'D' ||
        bytes[3] != 'C' || read_u16le(&bytes[4]) !=
            GXOS_DIAGNOSTIC_FRAME_VERSION ||
        read_u16le(&bytes[6]) != GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES ||
        read_u32le(&bytes[28]) !=
            gxos_managed_kernel_diagnostic_crc32(bytes, 28U)) {
        return 0;
    }
    command = read_u16le(&bytes[12]);
    expected_identity = read_u32le(&bytes[20]);
    expected_generation = read_u16le(&bytes[24]);
    if (read_u16le(&bytes[14]) != 0U ||
        read_u32le(&bytes[16]) != GXOS_DIAGNOSTIC_TARGET_COM1 ||
        read_u16le(&bytes[26]) != 0U) {
        return 0;
    }
    if (command == GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND) {
        return expected_identity == 0U && expected_generation == 0U;
    }
    if (command == GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_COMMAND) {
        return expected_identity != 0U && expected_generation != 0U;
    }
    return 0;
}

static void encode_status_body(
    uint8_t *body, const GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 *status)
{
    write_u32le(&body[0], status->structure_size);
    write_u32le(&body[4], status->version);
    write_u32le(&body[8], status->service_slot);
    write_u32le(&body[12], status->device_identity);
    write_u32le(&body[16], (uint32_t)status->owner_state);
    write_u32le(&body[20], status->current_service_valid);
    write_u32le(&body[24], status->current_service_identity);
    write_u16le(&body[28], status->current_generation);
    write_u16le(&body[30], 0U);
    write_u32le(&body[32], status->route_enabled);
    write_u32le(&body[36], status->runtime_attached);
    write_u32le(&body[40], (uint32_t)status->last_failure_reason);
    write_u32le(&body[44], status->last_failed_identity);
    write_u16le(&body[48], status->last_failed_generation);
    write_u16le(&body[50], 0U);
    write_u32le(&body[52], status->last_failed_device_identity);
    write_u32le(&body[56], status->restart_budget_remaining);
    write_u32le(&body[60], status->automatic_restart_attempts);
    write_u32le(&body[64], status->restart_failed);
    write_u32le(&body[68], status->explicit_restart_allowed);
    write_u32le(&body[72], status->explicit_restart_in_progress);
}

static int transmit_response(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context,
    uint32_t request_id, uint16_t command, uint32_t api_result,
    const GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 *status,
    const GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *handle)
{
    uint8_t response[GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESPONSE_MAX_BYTES] = {0};
    uint32_t total_bytes = GXOS_DIAGNOSTIC_RESPONSE_HEADER_BYTES +
                           GXOS_DIAGNOSTIC_RESPONSE_CRC_BYTES;
    uint32_t index;
    response[0] = 'G';
    response[1] = 'X';
    response[2] = 'D';
    response[3] = 'R';
    write_u16le(&response[4], GXOS_DIAGNOSTIC_FRAME_VERSION);
    write_u32le(&response[8], request_id);
    write_u16le(&response[12], command);
    write_u16le(&response[14], 0U);
    write_u32le(&response[16], api_result);
    if (status != 0) {
        encode_status_body(&response[20], status);
        total_bytes += GXOS_DIAGNOSTIC_STATUS_BODY_BYTES;
    } else if (handle != 0 && api_result ==
            GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK) {
        write_u32le(&response[20], handle->identity);
        write_u32le(&response[24], handle->device_identity);
        write_u16le(&response[28], handle->generation);
        write_u16le(&response[30], handle->slot);
        total_bytes += GXOS_DIAGNOSTIC_RESTART_BODY_BYTES;
    }
    write_u16le(&response[6], (uint16_t)total_bytes);
    write_u32le(&response[total_bytes - 4U],
                gxos_managed_kernel_diagnostic_crc32(response,
                                                       total_bytes - 4U));
    for (index = 0; index != total_bytes; ++index) {
        if (!context->transmit_byte(context->uart_context, response[index])) {
            __atomic_add_fetch(&context->transmit_failure_count, 1U,
                               __ATOMIC_RELAXED);
            return 0;
        }
    }
    __atomic_add_fetch(&context->response_count, 1U, __ATOMIC_RELAXED);
    return 1;
}

static void dispatch_request(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context,
    const uint8_t *request)
{
    uint32_t request_id = read_u32le(&request[8]);
    uint16_t command = read_u16le(&request[12]);
    uint32_t api_result;
    if (command == GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND) {
        GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 status = {0};
        __atomic_add_fetch(&context->valid_request_count, 1U,
                           __ATOMIC_RELAXED);
        __atomic_add_fetch(&context->status_api_count, 1U,
                           __ATOMIC_RELAXED);
        api_result = (uint32_t)context->status_api(context->service_context,
                                                   &status);
        __atomic_store_n(&context->last_status_api_result, api_result,
                         __ATOMIC_RELEASE);
        (void)transmit_response(context, request_id, command, api_result,
                                api_result ==
                                    GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK
                                        ? &status : 0,
                                0);
        return;
    }
    {
        GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE handle = {0};
        __atomic_add_fetch(&context->valid_request_count, 1U,
                           __ATOMIC_RELAXED);
        __atomic_add_fetch(&context->restart_api_count, 1U,
                           __ATOMIC_RELAXED);
        api_result = (uint32_t)context->restart_api(
            context->service_context, read_u32le(&request[20]),
            read_u16le(&request[24]), read_u32le(&request[16]), &handle);
        __atomic_store_n(&context->last_restart_api_result, api_result,
                         __ATOMIC_RELEASE);
        if (api_result ==
                GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK) {
            *context->current_handle_out = handle;
        }
        (void)transmit_response(context, request_id, command, api_result, 0,
                                api_result ==
                                    GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT_OK
                                        ? &handle : 0);
    }
}

static void resynchronize_after_rejection(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context, uint32_t available_bytes)
{
    uint32_t offset;
    for (offset = 1U; offset + 4U <= available_bytes; ++offset) {
        if (has_request_magic(context->parser_buffer, offset)) {
            uint32_t remaining = available_bytes - offset;
            uint32_t index;
            for (index = 0; index != remaining; ++index) {
                context->parser_buffer[index] =
                    context->parser_buffer[offset + index];
            }
            context->parser_count = remaining;
            return;
        }
    }
    {
        static const uint8_t magic[4] = {'G', 'X', 'D', 'C'};
        uint32_t suffix_bytes = available_bytes < 3U ? available_bytes : 3U;
        while (suffix_bytes != 0U) {
            uint32_t index;
            int matches = 1;
            for (index = 0U; index != suffix_bytes; ++index) {
                if (context->parser_buffer[available_bytes - suffix_bytes + index] !=
                    magic[index]) {
                    matches = 0;
                    break;
                }
            }
            if (matches) {
                for (index = 0U; index != suffix_bytes; ++index) {
                    context->parser_buffer[index] =
                        context->parser_buffer[available_bytes - suffix_bytes + index];
                }
                context->parser_count = suffix_bytes;
                return;
            }
            --suffix_bytes;
        }
    }
    context->parser_count = 0U;
}

static int parser_feed(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context, uint8_t value)
{
    if (context->parser_count == 0U) {
        if (value == 'G') {
            context->parser_buffer[0] = value;
            context->parser_count = 1U;
        } else {
            __atomic_add_fetch(&context->malformed_request_count, 1U,
                               __ATOMIC_RELAXED);
        }
        return 0;
    }
    context->parser_buffer[context->parser_count++] = value;
    if (context->parser_count >= 6U &&
        read_u16le(&context->parser_buffer[4]) != GXOS_DIAGNOSTIC_FRAME_VERSION) {
        __atomic_add_fetch(&context->malformed_request_count, 1U,
                           __ATOMIC_RELAXED);
        resynchronize_after_rejection(context, context->parser_count);
        return 0;
    }
    if (context->parser_count >= 8U &&
        read_u16le(&context->parser_buffer[6]) !=
            GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES) {
        __atomic_add_fetch(&context->malformed_request_count, 1U,
                           __ATOMIC_RELAXED);
        resynchronize_after_rejection(context, context->parser_count);
        return 0;
    }
    if (context->parser_count != GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES) {
        return 0;
    }
    if (validate_request(context->parser_buffer)) {
        dispatch_request(context, context->parser_buffer);
        context->parser_count = 0U;
        return 1;
    }
    __atomic_add_fetch(&context->malformed_request_count, 1U,
                       __ATOMIC_RELAXED);
    resynchronize_after_rejection(
        context, GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES);
    return 0;
}

uint32_t gxos_managed_kernel_diagnostic_poll(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context)
{
    uint32_t drained = 0U;
    uint32_t dispatched = 0U;
    uint32_t overflow_generation;
    if (context == 0 || context->present == 0U ||
        __atomic_load_n(&context->enabled, __ATOMIC_ACQUIRE) == 0U) {
        return 0U;
    }
    overflow_generation = __atomic_load_n(&context->overflow_generation,
                                          __ATOMIC_ACQUIRE);
    if (overflow_generation != context->parser_overflow_generation) {
        context->parser_count = 0U;
        context->parser_overflow_generation = overflow_generation;
    }
    while (drained != GXOS_MANAGED_KERNEL_DIAGNOSTIC_BOOT_DRAIN_MAX &&
           dispatched != GXOS_MANAGED_KERNEL_DIAGNOSTIC_DISPATCH_MAX) {
        uint32_t count = __atomic_load_n(&context->rx_count, __ATOMIC_ACQUIRE);
        uint32_t read_index;
        uint8_t value;
        if (count == 0U) break;
        read_index = __atomic_load_n(&context->rx_read_index,
                                     __ATOMIC_RELAXED);
        value = context->rx_ring[read_index %
            GXOS_MANAGED_KERNEL_DIAGNOSTIC_RX_CAPACITY];
        __atomic_store_n(&context->rx_read_index, read_index + 1U,
                         __ATOMIC_RELEASE);
        __atomic_sub_fetch(&context->rx_count, 1U, __ATOMIC_RELEASE);
        __atomic_add_fetch(&context->parser_bytes, 1U, __ATOMIC_RELAXED);
        ++drained;
        if (parser_feed(context, value)) ++dispatched;
    }
    if (__atomic_load_n(&context->rx_count, __ATOMIC_ACQUIRE) == 0U) {
        __atomic_store_n(&context->rx_pending, 0U, __ATOMIC_RELEASE);
        if (__atomic_load_n(&context->rx_count, __ATOMIC_ACQUIRE) != 0U) {
            __atomic_store_n(&context->rx_pending, 1U, __ATOMIC_RELEASE);
        }
    }
    return dispatched;
}

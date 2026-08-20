#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../managed_kernel_serial.h"

static uint32_t g_failures;
static uint8_t g_input[32];
static uint8_t g_output[32];
static uint32_t g_output_length;
static uint32_t g_ready;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

static int range_is_known(void *context, uintptr_t address, uintptr_t length)
{
    uintptr_t begin = (uintptr_t)g_input;
    uintptr_t end = begin + sizeof(g_input);
    uintptr_t status_begin = (uintptr_t)g_output;
    uintptr_t status_end = status_begin + sizeof(g_output);
    (void)context;
    if (length == 0 || length > UINTPTR_MAX - address) return 0;
    return (address >= begin && address <= end && length <= end - address) ||
           (address >= status_begin && address <= status_end &&
            length <= status_end - address);
}

static int transmitter_ready(void *context)
{
    (void)context;
    return g_ready != 0;
}

static int transmit_byte(void *context, uint8_t value)
{
    (void)context;
    if (g_output_length >= sizeof(g_output)) return 0;
    g_output[g_output_length++] = value;
    return 1;
}

int main(void)
{
    GXOS_MANAGED_KERNEL_SERIAL_CONTEXT context = {0};
    GX_MANAGED_KERNEL_SERIAL_STATUS_V1 status = {0};
    uint32_t result;

    context.device_kind = GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL;
    context.device_id = GX_MANAGED_SERIAL_DEVICE_ID_COM1;
    context.com_index = GX_MANAGED_SERIAL_COM_INDEX_1;
    context.present = 1;
    context.capabilities = GX_MANAGED_SERIAL_CAPABILITY_TRANSMIT |
                           GX_MANAGED_SERIAL_CAPABILITY_QUERY_STATUS;
    context.max_transmit_bytes = 8;
    context.tx_poll_limit = 4;
    context.range_is_known = range_is_known;
    context.transmitter_ready = transmitter_ready;
    context.transmit_byte = transmit_byte;
    memcpy(g_input, "SERIAL", 6);
    g_ready = 1;

    result = gxos_managed_kernel_serial_write_v1(
        &context, context.device_id, (uintptr_t)g_input, 6, 0);
    expect(result == GX_MANAGED_OK && g_output_length == 6 &&
               memcmp(g_output, "SERIAL", 6) == 0 &&
               context.successful_transmit_count == 1,
           "bounded serial write reaches the injected transmitter");
    expect(gxos_managed_kernel_serial_write_v1(
               &context, context.device_id, 0, 1, 0) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "null serial buffer rejects");
    expect(gxos_managed_kernel_serial_write_v1(
               &context, context.device_id, (uintptr_t)g_input, 9, 0) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "serial maximum rejects oversized write");
    expect(gxos_managed_kernel_serial_write_v1(
               &context, context.device_id, (uintptr_t)g_input, 1, 1) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "serial flags reject unsupported values");
    expect(gxos_managed_kernel_serial_write_v1(
               &context, context.device_id + 1, (uintptr_t)g_input, 1, 0) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "serial device identity rejects mismatched device");
    expect(gxos_managed_kernel_serial_write_v1(
               &context, context.device_id, UINTPTR_MAX - 1U, 4, 0) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "serial pointer range overflow rejects");
    expect(gxos_managed_kernel_serial_write_v1(
               &context, context.device_id, (uintptr_t)g_input, 0, 0) ==
               GX_MANAGED_OK && context.successful_transmit_count == 1,
           "zero-length serial write is bounded and side-effect free");

    result = gxos_managed_kernel_serial_query_status_v1(
        &context, GX_MANAGED_KERNEL_SERIAL_SERVICES_ABI_V1,
        context.device_id, (uintptr_t)g_output, sizeof(status));
    memcpy(&status, g_output, sizeof(status));
    expect(result == GX_MANAGED_OK &&
               status.Size == GX_MANAGED_KERNEL_SERIAL_STATUS_V1_SIZE &&
               status.Status == (GX_MANAGED_SERIAL_STATUS_DEVICE_PRESENT |
                                 GX_MANAGED_SERIAL_STATUS_TRANSMITTER_READY) &&
               status.Capabilities == context.capabilities,
           "serial status query is normalized");
    expect(gxos_managed_kernel_serial_query_status_v1(
               &context, GX_MANAGED_KERNEL_SERIAL_SERVICES_ABI_V1 + 1,
               context.device_id, (uintptr_t)g_output, sizeof(status)) ==
               GX_MANAGED_UNSUPPORTED_ABI,
           "serial status rejects unsupported ABI");
    expect(gxos_managed_kernel_serial_query_status_v1(
               &context, GX_MANAGED_KERNEL_SERIAL_SERVICES_ABI_V1,
               context.device_id, (uintptr_t)g_output,
               sizeof(status) - 1) == GX_MANAGED_BUFFER_TOO_SMALL,
           "serial status rejects undersized output");

    g_ready = 0;
    expect(gxos_managed_kernel_serial_write_v1(
               &context, context.device_id, (uintptr_t)g_input, 1, 0) ==
               GX_MANAGED_TIMEOUT && context.timeout_count == 1,
           "serial transmit timeout is bounded");
    expect(gxos_managed_kernel_serial_query_status_v1(
               &context, GX_MANAGED_KERNEL_SERIAL_SERVICES_ABI_V1,
               context.device_id, (uintptr_t)g_output, sizeof(status)) ==
               GX_MANAGED_TIMEOUT && context.timeout_count == 2,
           "serial status timeout is bounded");

    if (g_failures != 0) {
        printf("MANAGED_KERNEL_SERIAL_NATIVE_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_SERIAL_NATIVE_HOST_TESTS=PASSED\n");
    return 0;
}

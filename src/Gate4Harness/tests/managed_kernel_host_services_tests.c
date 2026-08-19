#include <stdint.h>
#include <stdio.h>
#include <string.h>

#include "../managed_kernel_host_services.h"

static uint32_t g_failures;
static uint32_t g_sink_calls;
static uintptr_t g_sink_length;

static void sink(const uint8_t *bytes, uintptr_t byte_length)
{
    if (bytes != 0 && byte_length != 0) {
        g_sink_length = byte_length;
    }
    ++g_sink_calls;
}

static int valid_range(uintptr_t address, uintptr_t byte_length)
{
    return address == 0x1000U && byte_length <= 1024U;
}

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

int main(void)
{
    uint8_t fixture[GX_MANAGED_KERNEL_HOST_LOG_MAX_BYTES];
    memset(fixture, 0x5A, sizeof(fixture));
    expect(gxos_managed_kernel_host_validate_log_request(
               0, 0, 0) == GX_MANAGED_OK,
           "zero-length null log is accepted");
    expect(gxos_managed_kernel_host_validate_log_request(
               0, 1, 0) == GX_MANAGED_INVALID_ARGUMENT,
           "nonzero-length null log is rejected");
    expect(gxos_managed_kernel_host_validate_log_request(
               UINTPTR_MAX - 7U, 8U, 0) == GX_MANAGED_INVALID_ARGUMENT,
           "logging pointer overflow is rejected");
    expect(gxos_managed_kernel_host_validate_log_request(
               0x1000U, GX_MANAGED_KERNEL_HOST_LOG_MAX_BYTES, 0) == GX_MANAGED_OK,
           "maximum log length is accepted");
    expect(gxos_managed_kernel_host_validate_log_request(
               0x1000U, GX_MANAGED_KERNEL_HOST_LOG_MAX_BYTES + 1U, 0) ==
               GX_MANAGED_INVALID_ARGUMENT,
           "one byte over maximum is rejected");
    expect(gxos_managed_kernel_host_validate_log_request(
               0x1000U, 1U, 1U) == GX_MANAGED_INVALID_ARGUMENT,
           "unsupported logging flags are rejected");
    expect(gxos_managed_kernel_host_log_utf8(
               0, 0, 0, sink, valid_range) == GX_MANAGED_OK &&
               g_sink_calls == 0,
           "zero-length log does not invoke sink");
    expect(gxos_managed_kernel_host_log_utf8(
               0x1000U, sizeof(fixture), 0, sink, valid_range) == GX_MANAGED_OK &&
               g_sink_calls == 1 && g_sink_length == sizeof(fixture),
           "bounded log invokes sink once");
    expect(gxos_managed_kernel_host_log_utf8(
               0x2000U, 1U, 0, sink, valid_range) == GX_MANAGED_INVALID_ARGUMENT &&
               g_sink_calls == 1,
           "unknown readable range is rejected without sink");
    expect(gxos_managed_kernel_host_validate_output_range(
               0, 40U, 40U) == GX_MANAGED_INVALID_ARGUMENT,
           "null output range is rejected");
    expect(gxos_managed_kernel_host_validate_output_range(
               1U, 39U, 40U) == GX_MANAGED_BUFFER_TOO_SMALL,
           "small output range is rejected");
    expect(gxos_managed_kernel_host_validate_output_range(
               UINTPTR_MAX - 39U, 40U, 40U) == GX_MANAGED_INVALID_ARGUMENT,
           "output range overflow is rejected");
    if (g_failures != 0) {
        printf("MANAGED_KERNEL_HOST_SERVICES_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_HOST_SERVICES_TESTS=PASSED\n");
    return 0;
}

#include <stdint.h>
#include <stdio.h>
#include <stddef.h>

#include "../managed_kernel_abi.h"

static uint32_t g_failures;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

static void test_layout_and_constants(void)
{
    expect(sizeof(GX_MANAGED_KERNEL_INIT_REQUEST_V1) == 16,
           "init request size is 16");
    expect(sizeof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1) == 32,
           "system info size is 32");
    expect(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, Capabilities) == 16,
           "capabilities offset is 16");
    expect(offsetof(GX_MANAGED_KERNEL_SYSTEM_INFO_V1, Reserved) == 24,
           "reserved offset is 24");
    expect(GX_MANAGED_KERNEL_ABI_V1 == 1U &&
               GX_MANAGED_KERNEL_SERVICE_VERSION_V1 == 1U,
           "ABI and service version are v1");
    expect(GX_MANAGED_OK == 0U && GX_MANAGED_INVALID_ARGUMENT == 1U &&
               GX_MANAGED_UNSUPPORTED_ABI == 2U &&
               GX_MANAGED_BUFFER_TOO_SMALL == 3U &&
               GX_MANAGED_NOT_INITIALIZED == 4U &&
               GX_MANAGED_ALREADY_INITIALIZED == 5U,
           "status constants are stable");
    expect(GX_MANAGED_CAPABILITY_SERVICE_ABI == 1ULL &&
               GX_MANAGED_CAPABILITY_SYSTEM_INFORMATION == 2ULL,
           "capability bits are stable");
}

static void test_buffer_validation(void)
{
    expect(gxos_managed_kernel_validate_output_buffer(
               (uintptr_t)0, 32) == GX_MANAGED_INVALID_ARGUMENT,
           "null output is rejected");
    expect(gxos_managed_kernel_validate_output_buffer(
               (uintptr_t)1, 0) == GX_MANAGED_BUFFER_TOO_SMALL,
           "zero capacity is rejected");
    expect(gxos_managed_kernel_validate_output_buffer(
               (uintptr_t)1, 31) == GX_MANAGED_BUFFER_TOO_SMALL,
           "undersized capacity is rejected");
    expect(gxos_managed_kernel_validate_output_buffer(
               UINTPTR_MAX - 15U, 32) == GX_MANAGED_INVALID_ARGUMENT,
           "wrapping output range is rejected");
    expect(gxos_managed_kernel_validate_output_buffer(
               (uintptr_t)1, 32) == GX_MANAGED_OK,
           "bounded output range is accepted");
}

int main(void)
{
    test_layout_and_constants();
    test_buffer_validation();
    if (g_failures != 0) {
        printf("MANAGED_KERNEL_ABI_HOST_TESTS=FAILED failures=%u\n", g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_ABI_HOST_TESTS=PASSED\n");
    return 0;
}

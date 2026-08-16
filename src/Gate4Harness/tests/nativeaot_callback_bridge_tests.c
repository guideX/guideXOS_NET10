#include <stdint.h>
#include <stdio.h>

#include "nativeaot_callback_bridge.h"

static uint8_t image[1024];
static uint32_t callback_count;
static uint32_t failures;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++failures;
        printf("FAIL: %s\n", message);
    }
}

static int GXOS_NATIVEAOT_MS_ABI host_callback(int32_t value)
{
    callback_count++;
    return (int32_t)((callback_count << 16) | ((uint32_t)value + 1U));
}

static void put_u16(uint32_t rva, uint16_t value)
{
    image[rva] = (uint8_t)value;
    image[rva + 1] = (uint8_t)(value >> 8);
}

static void put_u32(uint32_t rva, uint32_t value)
{
    put_u16(rva, (uint16_t)value);
    put_u16(rva + 2, (uint16_t)(value >> 16));
}

static void make_export_image(void)
{
    static const char name[] = "ManagedCallback";
    uint32_t index;
    for (index = 0; index != sizeof(image); index++) image[index] = 0;
    put_u32(0x100U + 16U, 1U);
    put_u32(0x100U + 20U, 1U);
    put_u32(0x100U + 24U, 1U);
    put_u32(0x100U + 28U, 0x180U);
    put_u32(0x100U + 32U, 0x184U);
    put_u32(0x100U + 36U, 0x188U);
    put_u32(0x180U, 0x300U);
    put_u32(0x184U, 0x190U);
    put_u16(0x188U, 0U);
    for (index = 0; index != sizeof(name); index++) image[0x190U + index] = (uint8_t)name[index];
}

static void test_export_resolution(void)
{
    GXOS_NATIVEAOT_EXPORT_IMAGE image_view = {
        image, sizeof(image), 0x100U, 0x5CU};
    GXOS_NATIVEAOT_EXPORT_RESOLUTION resolution = {0};
    GXOS_NATIVEAOT_EXPORT_STATUS status;

    status = gxos_nativeaot_find_export(&image_view, "ManagedCallback", &resolution);
    expect(status == GXOS_NATIVEAOT_EXPORT_OK, "callback export resolves");
    expect(resolution.rva == 0x300U && resolution.ordinal == 1U &&
               resolution.address == (uintptr_t)(image + 0x300U),
           "resolved export has stable RVA, ordinal, and address");
    expect(gxos_nativeaot_find_export(&image_view, "Unknown", &resolution) ==
               GXOS_NATIVEAOT_EXPORT_NOT_FOUND,
           "unknown export is rejected");
    expect(gxos_nativeaot_find_export(0, "ManagedCallback", &resolution) ==
               GXOS_NATIVEAOT_EXPORT_NULL_IMAGE,
           "null export image is rejected");
    expect(gxos_nativeaot_find_export(&image_view, 0, &resolution) ==
               GXOS_NATIVEAOT_EXPORT_NULL_NAME,
           "null export name is rejected");
    image_view.export_size = 0x20U;
    expect(gxos_nativeaot_find_export(&image_view, "ManagedCallback", &resolution) ==
               GXOS_NATIVEAOT_EXPORT_INVALID_DIRECTORY,
           "truncated export directory is rejected");
}

static void test_readiness_and_two_calls(void)
{
    GXOS_NATIVEAOT_EXPORT_IMAGE image_view = {
        image, sizeof(image), 0x100U, 0x5CU};
    GXOS_NATIVEAOT_EXPORT_RESOLUTION resolution = {0};
    GXOS_NATIVEAOT_CALLBACK_BRIDGE bridge = {0};
    uint64_t scheduler_metadata_before = 0x1122334455667788ULL;
    uint64_t scheduler_metadata_after = scheduler_metadata_before;
    uint32_t before;
    uint32_t after;
    int32_t result = 0x7F7F7F7F;

    callback_count = 0;
    expect(gxos_nativeaot_find_export(&image_view, "ManagedCallback", &resolution) ==
               GXOS_NATIVEAOT_EXPORT_OK,
           "resolution succeeds before registration");
    resolution.address = (uintptr_t)host_callback;
    expect(gxos_nativeaot_callback_register(&bridge, &resolution) != 0,
           "callback registration succeeds");
    expect(gxos_nativeaot_callback_invoke(&bridge, 41, 0) ==
               GXOS_NATIVEAOT_CALLBACK_NULL_RESULT,
           "null callback result is rejected");
    expect(gxos_nativeaot_callback_invoke(&bridge, 41, &result) ==
               GXOS_NATIVEAOT_CALLBACK_NOT_READY && result == 0x7F7F7F7F &&
               callback_count == 0,
           "pre-readiness invocation is rejected without calling target");
    expect(gxos_nativeaot_callback_mark_ready(&bridge) != 0,
           "readiness publication succeeds");
    expect(gxos_nativeaot_callback_invoke(&bridge, 41, &result) ==
               GXOS_NATIVEAOT_CALLBACK_OK && result == 0x0001002A,
           "first ABI callback returns state-coded result");
    before = (uint32_t)result;
    expect(gxos_nativeaot_callback_invoke(&bridge, 99, &result) ==
               GXOS_NATIVEAOT_CALLBACK_OK && result == 0x00020064,
           "second ABI callback returns distinct state-coded result");
    after = (uint32_t)result;
    expect(before != after && bridge.invocation_count == 2U && callback_count == 2U,
           "two calls use one stable registered pointer");
    expect(before == 0x0001002A && after == 0x00020064,
           "callback results preserve managed counter evidence");
    expect(scheduler_metadata_before == scheduler_metadata_after &&
               before == 0x0001002A && after == 0x00020064,
           "scheduler metadata stand-in remains untouched");
}

int main(void)
{
    make_export_image();
    test_export_resolution();
    test_readiness_and_two_calls();
    if (failures != 0) {
        printf("NATIVEAOT_CALLBACK_BRIDGE_HOST_TESTS=FAILED failures=%u\n", failures);
        return 1;
    }
    printf("NATIVEAOT_CALLBACK_BRIDGE_HOST_TESTS=PASSED\n");
    return 0;
}

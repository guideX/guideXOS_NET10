#include <stdio.h>
#include <string.h>

#include "../managed_kernel_device_inventory.h"

static uint32_t g_failures;

typedef struct {
    uint8_t bus;
    uint8_t device;
    uint8_t function;
    uint16_t vendor;
    uint16_t device_id;
    uint8_t revision;
    uint8_t class_code;
    uint8_t subclass;
    uint8_t programming_interface;
    uint8_t header_type;
} FIXTURE_DEVICE;

typedef struct {
    FIXTURE_DEVICE devices[4];
    uint32_t count;
} FIXTURE_CONTEXT;

static void expect(int condition, const char *message)
{
    if (!condition) {
        ++g_failures;
        printf("FAIL: %s\n", message);
    }
}

static const FIXTURE_DEVICE *fixture_find(
    const FIXTURE_CONTEXT *context, uint8_t bus, uint8_t device,
    uint8_t function)
{
    uint32_t index;
    for (index = 0; index != context->count; ++index) {
        const FIXTURE_DEVICE *candidate = &context->devices[index];
        if (candidate->bus == bus && candidate->device == device &&
            candidate->function == function) {
            return candidate;
        }
    }
    return 0;
}

static uint32_t fixture_read32(
    void *opaque, uint16_t segment, uint8_t bus, uint8_t device,
    uint8_t function, uint8_t offset)
{
    const FIXTURE_CONTEXT *context = (const FIXTURE_CONTEXT *)opaque;
    const FIXTURE_DEVICE *fixture;
    if (segment != 0) return UINT32_MAX;
    fixture = fixture_find(context, bus, device, function);
    if (fixture == 0) return UINT32_MAX;
    if (offset == 0x00) {
        return ((uint32_t)fixture->device_id << 16) | fixture->vendor;
    }
    if (offset == 0x08) {
        return ((uint32_t)fixture->class_code << 24) |
               ((uint32_t)fixture->subclass << 16) |
               ((uint32_t)fixture->programming_interface << 8) |
               fixture->revision;
    }
    if (offset == 0x0C) return (uint32_t)fixture->header_type << 16;
    return 0;
}

static GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT input(
    uint8_t bus, uint8_t device, uint8_t function, uint16_t vendor,
    uint16_t device_id, uint8_t class_code, uint8_t subclass,
    uint8_t programming_interface, uint8_t header_type, uint32_t flags)
{
    GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT value = {0};
    value.bus = bus;
    value.device = device;
    value.function = function;
    value.vendor_id = vendor;
    value.device_id = device_id;
    value.revision_id = 1;
    value.class_code = class_code;
    value.subclass = subclass;
    value.programming_interface = programming_interface;
    value.header_type = header_type;
    value.flags = flags;
    return value;
}

static void test_discovery_and_normalization(void)
{
    FIXTURE_CONTEXT context = {
        {
            {0, 0, 0, 0x8086, 0x1237, 2, 0x06, 0x00, 0x00, 0x80},
            {0, 1, 0, 0x8086, 0x7000, 1, 0x06, 0x01, 0x00, 0x80},
            {0, 1, 1, 0x8086, 0x7010, 1, 0x01, 0x01, 0x80, 0x80},
            {0, 2, 0, 0x1234, 0x1111, 3, 0x03, 0x00, 0x00, 0x00}
        },
        4
    };
    GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT raw[8] = {0};
    GX_MANAGED_KERNEL_DEVICE_V1 normalized[8] = {0};
    GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1 summary = {0};
    uint32_t count = 0;

    expect(gxos_managed_kernel_discover_pci_devices(
               fixture_read32, &context, raw, 8, &count) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OK && count == 4,
           "synthetic PCI discovery finds multifunction devices");
    expect(raw[2].function == 1 && raw[2].class_code == 0x01,
           "synthetic multifunction function is retained");
    expect(gxos_managed_kernel_normalize_device_inventory(
               raw, count, normalized, 8, &summary) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OK &&
               summary.DeviceCount == 4 && summary.ResourceCount == 0 &&
               normalized[0].DeviceKind == GX_MANAGED_DEVICE_KIND_PCI &&
               normalized[2].VendorId == 0x8086,
           "synthetic PCI records normalize into stable descriptors");
    expect(normalized[0].Flags == GX_MANAGED_DEVICE_FLAG_PCI_MULTIFUNCTION,
           "multifunction flag is normalized");
    expect(gxos_managed_kernel_discover_pci_devices(
               fixture_read32, &context, raw, 2, &count) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_CAPACITY,
           "discovery capacity is bounded");
}

static void test_rejection_paths(void)
{
    GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT values[3];
    GX_MANAGED_KERNEL_DEVICE_V1 output[3];
    GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1 summary;

    values[0] = input(0, 0, 0, 0x8086, 0x1237, 0x06, 0x00, 0, 0, 0);
    values[1] = input(0, 1, 0, 0x8086, 0x7000, 0x06, 0x01, 0, 0, 0);
    values[2] = input(0, 2, 0, 0x1234, 0x1111, 0x03, 0x00, 0, 0, 0);
    expect(gxos_managed_kernel_normalize_device_inventory(
               values, 0, output, 3, &summary) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_INVALID_ARGUMENT,
           "empty inventory is rejected");
    values[1].bus = values[0].bus;
    values[1].device = values[0].device;
    values[1].function = values[0].function;
    expect(gxos_managed_kernel_normalize_device_inventory(
               values, 3, output, 3, &summary) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_DUPLICATE,
           "duplicate BDF is rejected");
    values[1] = input(0, 1, 0, 0xFFFF, 0x7000, 0x06, 0x01, 0, 0, 0);
    expect(gxos_managed_kernel_normalize_device_inventory(
               values, 3, output, 3, &summary) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_MALFORMED,
           "absent vendor ID is rejected");
    values[1] = input(0, 1, 0, 0x8086, 0x7000, 0x06, 0x01, 0, 3, 0);
    expect(gxos_managed_kernel_normalize_device_inventory(
               values, 3, output, 3, &summary) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_UNSUPPORTED,
           "unsupported PCI header layout is rejected");
    values[1] = input(0, 1, 0, 0x8086, 0x7000, 0x06, 0x01, 0, 0, 2);
    expect(gxos_managed_kernel_normalize_device_inventory(
               values, 3, output, 3, &summary) ==
               GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_MALFORMED,
           "unknown device flags are rejected");
}

int main(void)
{
    test_discovery_and_normalization();
    test_rejection_paths();
    if (g_failures != 0) {
        printf("MANAGED_KERNEL_DEVICE_INVENTORY_HOST_TESTS=FAILED failures=%u\n",
               g_failures);
        return 1;
    }
    printf("MANAGED_KERNEL_DEVICE_INVENTORY_HOST_TESTS=PASSED\n");
    return 0;
}

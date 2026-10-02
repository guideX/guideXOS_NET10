#include <stdio.h>
#include <string.h>

#include "../managed_kernel_device_resources.h"

static unsigned failures;

static void expect(int condition, const char *message)
{
    if (condition) return;
    ++failures;
    printf("FAIL: %s\n", message);
}

static GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 make_resource(
    uint64_t id, uint32_t kind, uint32_t device_id, uint64_t base, uint64_t length)
{
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 value = {0};
    value.Size = GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1_SIZE;
    value.AbiVersion = GX_MANAGED_KERNEL_DEVICE_RESOURCES_ABI_V1;
    value.ResourceId = id;
    value.OwnerDeviceKind = kind;
    value.OwnerDeviceId = device_id;
    value.ResourceType = GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT;
    value.Flags = GX_MANAGED_DEVICE_RESOURCE_FLAG_READABLE |
                  GX_MANAGED_DEVICE_RESOURCE_FLAG_IO_PORT |
                  GX_MANAGED_DEVICE_RESOURCE_FLAG_PLATFORM;
    value.PhysicalBase = base;
    value.Length = length;
    value.Alignment = 1;
    return value;
}

static void test_bar_decoding(void)
{
    GXOS_PCI_BAR_DECODED decoded;
    expect(gxos_pci_decode_bar(0x0000C001U, 0, 0xFFFFFF01U, 0,
                               &decoded) == GXOS_PCI_BAR_DECODE_OK &&
           decoded.resource_type == GX_MANAGED_DEVICE_RESOURCE_TYPE_IO_PORT &&
           decoded.base == 0xC000 && decoded.length == 0x100,
           "I/O BAR is normalized with its size mask");
    expect(gxos_pci_decode_bar(0xFEBF0004U, 0x00000001U,
                               0xFFFFF004U, 0xFFFFFFFFU, &decoded) ==
               GXOS_PCI_BAR_DECODE_OK && decoded.is_64_bit != 0 &&
               decoded.base == 0x1FEBF0000ULL && decoded.length == 0x1000,
           "64-bit MMIO BAR combines upper and lower halves");
    expect((decoded.flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_MEMORY) != 0,
           "64-bit MMIO BAR is classified as memory");
    expect(gxos_pci_decode_bar(0x80000008U, 0, 0xFFF00008U, 0,
                               &decoded) == GXOS_PCI_BAR_DECODE_OK &&
           (decoded.flags & GX_MANAGED_DEVICE_RESOURCE_FLAG_PREFETCHABLE) != 0,
           "32-bit prefetchable MMIO BAR preserves the flag");
    expect(gxos_pci_decode_bar(0x00000000U, 0, 0, 0, &decoded) ==
               GXOS_PCI_BAR_DECODE_UNIMPLEMENTED,
           "zero BAR is unimplemented");
    expect(gxos_pci_decode_bar(0x00000002U, 0, 0xFFFFFFF2U, 0, &decoded) ==
               GXOS_PCI_BAR_DECODE_RESERVED_TYPE,
           "reserved memory BAR type is rejected");
    expect(gxos_pci_decode_bar(0x0000C001U, 0, 0xFFFFFFF5U, 0,
                               &decoded) == GXOS_PCI_BAR_DECODE_MALFORMED,
           "non-power-of-two I/O size is rejected");
}

static void test_platform_publication(void)
{
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resources[8];
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_SUMMARY_V1 summary;
    uint32_t count = 0;
    expect(gxos_managed_kernel_make_platform_resources(resources, 4, &count,
                                                       &summary) ==
               GXOS_MANAGED_KERNEL_RESOURCE_OK && count == 3 &&
               summary.ResourceCount == 3 && summary.MaxClaims == 16,
           "platform resources publish bounded summary");
    expect(resources[0].OwnerDeviceKind == GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL &&
           resources[0].OwnerDeviceId == GX_MANAGED_SERIAL_DEVICE_ID_COM1 &&
           resources[0].PhysicalBase == 0x3F8 && resources[0].Length == 8,
           "COM1 resource is native-authoritative");
    expect(resources[1].PhysicalBase == 0x60 && resources[2].PhysicalBase == 0x64,
           "i8042 resources remain discontiguous");
    expect(gxos_managed_kernel_validate_resource(&resources[0]) ==
               GXOS_MANAGED_KERNEL_RESOURCE_OK,
           "valid resource descriptor is accepted");
    resources[0].Length = 0;
    expect(gxos_managed_kernel_validate_resource(&resources[0]) !=
               GXOS_MANAGED_KERNEL_RESOURCE_OK,
           "zero-length resource is rejected");
}

static void test_optional_secondary_uart_publication(void)
{
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 resources[8];
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_SUMMARY_V1 summary;
    GXOS_MANAGED_KERNEL_SECONDARY_UART_CONFIG uart = {
        .present = 1U, .io_base = 0x2F8U, .irq = 3U, .reserved = 0U};
    uint32_t count = 0U;
    expect(gxos_managed_kernel_make_platform_resources_with_secondary_uart(
               resources, 8U, &count, &summary, &uart) ==
               GXOS_MANAGED_KERNEL_RESOURCE_OK && count == 5U &&
               summary.ResourceCount == 5U &&
               resources[3].PhysicalBase == 0x2F8U &&
               resources[3].Length == 8U &&
               resources[3].OwnerDeviceId ==
                   GX_MANAGED_SERIAL_DIAGNOSTIC_DEVICE_ID_COM2 &&
               resources[4].ResourceType ==
                   GX_MANAGED_DEVICE_RESOURCE_TYPE_INTERRUPT &&
               resources[4].PhysicalBase == 3U,
           "platform-published COM2 requires explicit base and IRQ evidence");
    uart.irq = 4U;
    count = 0U;
    expect(gxos_managed_kernel_make_platform_resources_with_secondary_uart(
               resources, 8U, &count, &summary, &uart) ==
               GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE,
           "secondary UART cannot collide with the COM1 IRQ");
    uart.irq = 1U;
    count = 0U;
    expect(gxos_managed_kernel_make_platform_resources_with_secondary_uart(
               resources, 8U, &count, &summary, &uart) ==
               GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE,
           "secondary UART cannot collide with the keyboard IRQ");
    uart.irq = 3U;
    uart.io_base = 0x3F8U;
    count = 0U;
    expect(gxos_managed_kernel_make_platform_resources_with_secondary_uart(
               resources, 8U, &count, &summary, &uart) ==
               GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE,
           "secondary UART cannot overlap COM1 ports");
    uart.io_base = 0x60U;
    count = 0U;
    expect(gxos_managed_kernel_make_platform_resources_with_secondary_uart(
               resources, 8U, &count, &summary, &uart) ==
               GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE,
           "secondary UART cannot overlap i8042 ports");
    uart.io_base = 0x2F8U;
    uart.irq = 3U;
    count = 0U;
    expect(gxos_managed_kernel_make_platform_resources_with_secondary_uart(
               resources, 4U, &count, &summary, &uart) ==
               GXOS_MANAGED_KERNEL_RESOURCE_CAPACITY,
           "secondary UART publication respects descriptor capacity");
}

static void test_isolation_and_duplicates(void)
{
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 serial =
        make_resource(1, GX_MANAGED_DEVICE_KIND_PLATFORM_SERIAL, 1, 0x3F8, 8);
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 other =
        make_resource(2, GX_MANAGED_DEVICE_KIND_PLATFORM_KEYBOARD, 1, 0x3F8, 1);
    expect(gxos_managed_kernel_resource_ranges_overlap(&serial, &other),
           "same-type overlapping resources are detected");
    other.PhysicalBase = 0x64;
    expect(!gxos_managed_kernel_resource_ranges_overlap(&serial, &other),
           "disjoint resources are isolated");
    other.ResourceId = serial.ResourceId;
    expect(other.ResourceId == serial.ResourceId,
           "resource identity fixture has duplicate token");
}

int main(void)
{
    test_bar_decoding();
    test_platform_publication();
    test_optional_secondary_uart_publication();
    test_isolation_and_duplicates();
    if (failures != 0) {
        printf("MANAGED_KERNEL_DEVICE_RESOURCES_HOST_TESTS=FAILED failures=%u\n",
               failures);
        return 1;
    }
    printf("MANAGED_KERNEL_DEVICE_RESOURCES_HOST_TESTS=PASSED\n");
    return 0;
}

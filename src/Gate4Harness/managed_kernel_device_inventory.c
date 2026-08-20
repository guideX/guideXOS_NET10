#include "managed_kernel_device_inventory.h"

#include <stddef.h>

#define GXOS_PCI_CONFIG_ADDRESS_PORT ((uint16_t)0x0CF8)
#define GXOS_PCI_CONFIG_DATA_PORT ((uint16_t)0x0CFC)
#define GXOS_PCI_MAX_BUS 256U
#define GXOS_PCI_MAX_DEVICE 32U
#define GXOS_PCI_MAX_FUNCTION 8U
#define GXOS_PCI_NO_DEVICE_VENDOR 0xFFFFU

static uint32_t gxos_pci_read32(
    GXOS_MANAGED_KERNEL_PCI_CONFIG_READ32 read32,
    void *context,
    uint16_t segment,
    uint8_t bus,
    uint8_t device,
    uint8_t function,
    uint8_t offset)
{
    return read32(context, segment, bus, device, function, offset);
}

static uint16_t gxos_pci_vendor_id(uint32_t value)
{
    return (uint16_t)(value & 0xFFFFU);
}

static int gxos_pci_id_present(uint32_t value)
{
    uint16_t vendor = gxos_pci_vendor_id(value);
    return vendor != GXOS_PCI_NO_DEVICE_VENDOR && vendor != 0U;
}

static uint8_t gxos_pci_byte(uint32_t value, uint8_t offset)
{
    return (uint8_t)(value >> ((offset & 3U) * 8U));
}

static int gxos_same_bdf(
    const GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *left,
    const GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *right)
{
    return left->segment == right->segment &&
           left->bus == right->bus &&
           left->device == right->device &&
           left->function == right->function;
}

static GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_STATUS
gxos_validate_pci_input(
    const GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *input)
{
    uint8_t header_layout;
    if (input == 0 || input->segment != 0U ||
        input->vendor_id == GXOS_PCI_NO_DEVICE_VENDOR ||
        input->vendor_id == 0U || input->function >= GXOS_PCI_MAX_FUNCTION ||
        (input->flags & ~GX_MANAGED_DEVICE_FLAG_PCI_MULTIFUNCTION) != 0U) {
        return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_MALFORMED;
    }
    header_layout = (uint8_t)(input->header_type & 0x7FU);
    if (header_layout > 2U) {
        return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_UNSUPPORTED;
    }
    return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OK;
}

GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_STATUS
gxos_managed_kernel_discover_pci_devices(
    GXOS_MANAGED_KERNEL_PCI_CONFIG_READ32 read32,
    void *context,
    GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *devices,
    uint32_t device_capacity,
    uint32_t *device_count)
{
    uint32_t bus;
    uint32_t device;
    uint32_t function;
    uint32_t count = 0;

    if (read32 == 0 || devices == 0 || device_count == 0 ||
        device_capacity == 0U ||
        device_capacity > GX_MANAGED_KERNEL_DEVICE_INVENTORY_MAX_DEVICES) {
        return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_INVALID_ARGUMENT;
    }

    for (bus = 0; bus != GXOS_PCI_MAX_BUS; ++bus) {
        for (device = 0; device != GXOS_PCI_MAX_DEVICE; ++device) {
            uint32_t id = gxos_pci_read32(
                read32, context, 0, (uint8_t)bus, (uint8_t)device, 0, 0x00);
            uint8_t header_type;
            uint32_t function_count;

            if (!gxos_pci_id_present(id)) continue;
            header_type = gxos_pci_byte(
                gxos_pci_read32(read32, context, 0, (uint8_t)bus,
                                (uint8_t)device, 0, 0x0C), 0x0E);
            function_count = (header_type & 0x80U) != 0U
                ? GXOS_PCI_MAX_FUNCTION : 1U;

            for (function = 0; function != function_count; ++function) {
                GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *output;
                uint32_t function_id;
                uint32_t class_register;

                function_id = gxos_pci_read32(
                    read32, context, 0, (uint8_t)bus, (uint8_t)device,
                    (uint8_t)function, 0x00);
                if (!gxos_pci_id_present(function_id)) continue;
                if (count >= device_capacity) {
                    return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_CAPACITY;
                }
                class_register = gxos_pci_read32(
                    read32, context, 0, (uint8_t)bus, (uint8_t)device,
                    (uint8_t)function, 0x08);
                output = &devices[count++];
                output->segment = 0;
                output->bus = (uint8_t)bus;
                output->device = (uint8_t)device;
                output->function = (uint8_t)function;
                output->header_type = function == 0U
                    ? header_type
                    : gxos_pci_byte(gxos_pci_read32(
                        read32, context, 0, (uint8_t)bus,
                        (uint8_t)device, (uint8_t)function, 0x0C), 0x0E);
                output->vendor_id = gxos_pci_vendor_id(function_id);
                output->device_id = (uint16_t)(function_id >> 16);
                output->revision_id = gxos_pci_byte(class_register, 0x08);
                output->programming_interface = gxos_pci_byte(class_register, 0x09);
                output->subclass = gxos_pci_byte(class_register, 0x0A);
                output->class_code = gxos_pci_byte(class_register, 0x0B);
                output->flags = (output->header_type & 0x80U) != 0U
                    ? GX_MANAGED_DEVICE_FLAG_PCI_MULTIFUNCTION : 0U;
            }
        }
    }

    if (count == 0U) {
        return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_MALFORMED;
    }
    *device_count = count;
    return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OK;
}

uint32_t gxos_managed_kernel_pci_config_read32(
    void *context, uint16_t segment, uint8_t bus, uint8_t device,
    uint8_t function, uint8_t offset)
{
    uint32_t address;
    uint32_t value;
    (void)context;
    if (segment != 0U || device >= GXOS_PCI_MAX_DEVICE ||
        function >= GXOS_PCI_MAX_FUNCTION || (offset & 3U) != 0U) {
        return UINT32_MAX;
    }
    address = 0x80000000U |
              ((uint32_t)bus << 16) |
              ((uint32_t)device << 11) |
              ((uint32_t)function << 8) |
              (uint32_t)(offset & 0xFCU);
#if defined(__x86_64__)
    __asm__ volatile ("outl %0, %1" : : "a"(address),
                      "Nd"(GXOS_PCI_CONFIG_ADDRESS_PORT));
    __asm__ volatile ("inl %1, %0" : "=a"(value)
                      : "Nd"(GXOS_PCI_CONFIG_DATA_PORT));
    return value;
#else
    (void)address;
    return UINT32_MAX;
#endif
}

GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_STATUS
gxos_managed_kernel_normalize_device_inventory(
    const GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *inputs,
    uint32_t input_count,
    GX_MANAGED_KERNEL_DEVICE_V1 *devices,
    uint32_t device_capacity,
    GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1 *summary)
{
    uint32_t index;
    GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1 candidate_summary;

    if (inputs == 0 || devices == 0 || summary == 0 || input_count == 0U ||
        input_count > GX_MANAGED_KERNEL_DEVICE_INVENTORY_MAX_DEVICES ||
        device_capacity < input_count) {
        return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_INVALID_ARGUMENT;
    }

    for (index = 0; index != input_count; ++index) {
        GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_STATUS status =
            gxos_validate_pci_input(&inputs[index]);
        uint32_t prior;
        if (status != GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OK) return status;
        for (prior = 0; prior != index; ++prior) {
            if (gxos_same_bdf(&inputs[index], &inputs[prior])) {
                return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_DUPLICATE;
            }
        }
        if ((inputs[index].flags & ~GX_MANAGED_DEVICE_FLAG_PCI_MULTIFUNCTION) != 0U) {
            return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_MALFORMED;
        }
    }

    for (index = 0; index != input_count; ++index) {
        GX_MANAGED_KERNEL_DEVICE_V1 candidate = {0};
        const GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *input = &inputs[index];
        candidate.Size = GX_MANAGED_KERNEL_DEVICE_V1_SIZE;
        candidate.AbiVersion = GX_MANAGED_KERNEL_DEVICE_INVENTORY_ABI_V1;
        candidate.DeviceKind = GX_MANAGED_DEVICE_KIND_PCI;
        candidate.Flags = input->flags;
        candidate.Segment = input->segment;
        candidate.Bus = input->bus;
        candidate.Device = input->device;
        candidate.Function = input->function;
        candidate.VendorId = input->vendor_id;
        candidate.DeviceId = input->device_id;
        candidate.RevisionId = input->revision_id;
        candidate.ClassCode = input->class_code;
        candidate.Subclass = input->subclass;
        candidate.ProgrammingInterface = input->programming_interface;
        candidate.HeaderType = input->header_type;
        candidate.ResourceStartIndex = 0;
        candidate.ResourceCount = 0;
        candidate.ReservedLocation = 0;
        candidate.ReservedClass = 0;
        candidate.Reserved = 0;
        devices[index] = candidate;
    }

    candidate_summary.Size = GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1_SIZE;
    candidate_summary.AbiVersion = GX_MANAGED_KERNEL_DEVICE_INVENTORY_ABI_V1;
    candidate_summary.ServiceVersion =
        GX_MANAGED_KERNEL_DEVICE_INVENTORY_SERVICE_VERSION_V1;
    candidate_summary.Architecture = GX_MANAGED_KERNEL_ARCH_X64;
    candidate_summary.DeviceCount = input_count;
    candidate_summary.ResourceCount = 0;
    candidate_summary.Capabilities =
        GX_MANAGED_DEVICE_INVENTORY_CAPABILITY_SUMMARY |
        GX_MANAGED_DEVICE_INVENTORY_CAPABILITY_DEVICES |
        GX_MANAGED_DEVICE_INVENTORY_CAPABILITY_IMMUTABLE_BOOT_SNAPSHOT;
    candidate_summary.Reserved = 0;
    *summary = candidate_summary;
    return GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OK;
}

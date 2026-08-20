#ifndef GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_H
#define GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_H

#include <stdint.h>

#include "managed_kernel_abi.h"

typedef enum {
    GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OK = 0,
    GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_INVALID_ARGUMENT,
    GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_MALFORMED,
    GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_OVERFLOW,
    GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_CAPACITY,
    GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_DUPLICATE,
    GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_UNSUPPORTED
} GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_STATUS;

/* Raw data is an internal normalization input, not a public ABI. It contains
   only read-only PCI configuration values collected by native code. */
typedef struct {
    uint16_t segment;
    uint8_t bus;
    uint8_t device;
    uint8_t function;
    uint8_t header_type;
    uint16_t vendor_id;
    uint16_t device_id;
    uint8_t revision_id;
    uint8_t class_code;
    uint8_t subclass;
    uint8_t programming_interface;
    uint32_t flags;
} GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT;

typedef uint32_t (*GXOS_MANAGED_KERNEL_PCI_CONFIG_READ32)(
    void *context, uint16_t segment, uint8_t bus, uint8_t device,
    uint8_t function, uint8_t offset);

GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_STATUS
gxos_managed_kernel_discover_pci_devices(
    GXOS_MANAGED_KERNEL_PCI_CONFIG_READ32 read32,
    void *context,
    GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *devices,
    uint32_t device_capacity,
    uint32_t *device_count);

uint32_t gxos_managed_kernel_pci_config_read32(
    void *context, uint16_t segment, uint8_t bus, uint8_t device,
    uint8_t function, uint8_t offset);

GXOS_MANAGED_KERNEL_DEVICE_INVENTORY_STATUS
gxos_managed_kernel_normalize_device_inventory(
    const GXOS_MANAGED_KERNEL_PCI_DEVICE_INPUT *inputs,
    uint32_t input_count,
    GX_MANAGED_KERNEL_DEVICE_V1 *devices,
    uint32_t device_capacity,
    GX_MANAGED_KERNEL_DEVICE_INVENTORY_SUMMARY_V1 *summary);

static inline int gxos_managed_kernel_device_inventory_range_valid(
    uintptr_t address, uintptr_t length)
{
    return address != 0 && length != 0 && address <= UINTPTR_MAX - length;
}

#endif

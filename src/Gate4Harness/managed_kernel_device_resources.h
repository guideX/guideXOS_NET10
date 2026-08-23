#ifndef GXOS_MANAGED_KERNEL_DEVICE_RESOURCES_H
#define GXOS_MANAGED_KERNEL_DEVICE_RESOURCES_H

#include <stdint.h>

#include "managed_kernel_abi.h"

typedef enum {
    GXOS_MANAGED_KERNEL_RESOURCE_OK = 0,
    GXOS_MANAGED_KERNEL_RESOURCE_INVALID_ARGUMENT,
    GXOS_MANAGED_KERNEL_RESOURCE_MALFORMED,
    GXOS_MANAGED_KERNEL_RESOURCE_OVERFLOW,
    GXOS_MANAGED_KERNEL_RESOURCE_CAPACITY,
    GXOS_MANAGED_KERNEL_RESOURCE_DUPLICATE,
    GXOS_MANAGED_KERNEL_RESOURCE_UNSUPPORTED
} GXOS_MANAGED_KERNEL_RESOURCE_STATUS;

typedef enum {
    GXOS_PCI_BAR_DECODE_OK = 0,
    GXOS_PCI_BAR_DECODE_INVALID_ARGUMENT,
    GXOS_PCI_BAR_DECODE_UNIMPLEMENTED,
    GXOS_PCI_BAR_DECODE_RESERVED_TYPE,
    GXOS_PCI_BAR_DECODE_MALFORMED,
    GXOS_PCI_BAR_DECODE_OVERFLOW
} GXOS_PCI_BAR_DECODE_STATUS;

typedef struct {
    uint32_t resource_type;
    uint32_t flags;
    uint64_t base;
    uint64_t length;
    uint64_t alignment;
    uint32_t is_64_bit;
    uint32_t implemented;
} GXOS_PCI_BAR_DECODED;

typedef struct {
    uint64_t base;
    uint64_t length;
} GXOS_PCI_FIRMWARE_BAR;

GXOS_PCI_BAR_DECODE_STATUS gxos_pci_decode_bar(
    uint32_t raw_low, uint32_t raw_high,
    uint32_t mask_low, uint32_t mask_high,
    GXOS_PCI_BAR_DECODED *decoded);

GXOS_MANAGED_KERNEL_RESOURCE_STATUS gxos_managed_kernel_validate_resource(
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resource);

GXOS_MANAGED_KERNEL_RESOURCE_STATUS gxos_managed_kernel_make_platform_resources(
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources,
    uint32_t resource_capacity,
    uint32_t *resource_count,
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_SUMMARY_V1 *summary);

int gxos_managed_kernel_resource_ranges_overlap(
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *left,
    const GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *right);

GXOS_MANAGED_KERNEL_RESOURCE_STATUS gxos_managed_kernel_append_pci_mmio_resource(
    GX_MANAGED_KERNEL_DEVICE_RESOURCE_V1 *resources,
    uint32_t resource_capacity,
    uint32_t *resource_count,
    uint16_t segment,
    uint8_t bus,
    uint8_t device,
    uint8_t function,
    uint16_t vendor_id,
    uint16_t device_id,
    uint8_t bar_index,
    uint32_t raw_low,
    uint32_t raw_high,
    const GXOS_PCI_FIRMWARE_BAR *firmware_bar,
    GXOS_PCI_BAR_DECODED *decoded_out);

#endif

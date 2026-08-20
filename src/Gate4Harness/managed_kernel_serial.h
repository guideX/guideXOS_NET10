#ifndef GXOS_MANAGED_KERNEL_SERIAL_H
#define GXOS_MANAGED_KERNEL_SERIAL_H

#include <stdint.h>

#include "managed_kernel_abi.h"

typedef int (*GXOS_MANAGED_KERNEL_SERIAL_RANGE_VALIDATOR)(
    void *context, uintptr_t address, uintptr_t byte_length);
typedef int (*GXOS_MANAGED_KERNEL_SERIAL_TRANSMITTER_READY)(void *context);
typedef int (*GXOS_MANAGED_KERNEL_SERIAL_TRANSMIT_BYTE)(
    void *context, uint8_t value);

typedef struct {
    uint32_t device_kind;
    uint32_t device_id;
    uint32_t com_index;
    uint32_t present;
    uint64_t capabilities;
    uint32_t max_transmit_bytes;
    uint32_t tx_poll_limit;
    GXOS_MANAGED_KERNEL_SERIAL_RANGE_VALIDATOR range_is_known;
    GXOS_MANAGED_KERNEL_SERIAL_TRANSMITTER_READY transmitter_ready;
    GXOS_MANAGED_KERNEL_SERIAL_TRANSMIT_BYTE transmit_byte;
    void *hardware_context;
    uint32_t successful_transmit_count;
    uint32_t timeout_count;
} GXOS_MANAGED_KERNEL_SERIAL_CONTEXT;

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_serial_write_v1(
    GXOS_MANAGED_KERNEL_SERIAL_CONTEXT *context,
    uint32_t device_id, uintptr_t buffer_address, uint32_t byte_length,
    uint32_t flags);

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_serial_query_status_v1(
    GXOS_MANAGED_KERNEL_SERIAL_CONTEXT *context,
    uint32_t requested_abi_version, uint32_t device_id,
    uintptr_t result_address, uintptr_t result_capacity);

#endif

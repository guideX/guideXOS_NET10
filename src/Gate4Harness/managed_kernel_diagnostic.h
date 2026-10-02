#ifndef GXOS_MANAGED_KERNEL_DIAGNOSTIC_H
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_H

#include <stdint.h>

#include "managed_kernel_driver_worker.h"
#include "managed_kernel_diagnostic_resource.h"

#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_RX_CAPACITY 64U
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES 32U
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESPONSE_MAX_BYTES 100U
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_IRQ_DRAIN_MAX 16U
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_BOOT_DRAIN_MAX 16U
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_DISPATCH_MAX 1U
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_COMMAND 1U
#define GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_COMMAND 2U

typedef uint8_t (*GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER)(void *context);
typedef int (*GXOS_MANAGED_KERNEL_DIAGNOSTIC_TRANSMIT_BYTE)(
    void *context, uint8_t value);
typedef void (*GXOS_MANAGED_KERNEL_DIAGNOSTIC_EOI)(void *context);
typedef GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT
(*GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_API)(
    const GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *service_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_STATUS_V1 *status_out);
typedef GXOS_MANAGED_KERNEL_DRIVER_RESTART_RESULT
(*GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_API)(
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *service_context,
    uint32_t expected_failed_identity,
    uint16_t expected_failed_generation, uint32_t expected_device_identity,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *new_handle_out);

typedef struct {
    uint32_t present;
    uint32_t enabled;
    uint32_t resource_source;
    uint32_t disable_reason;
    uint16_t io_base;
    uint16_t register_span;
    uint8_t irq;
    uint8_t backend;
    volatile uint32_t rx_read_index;
    volatile uint32_t rx_write_index;
    volatile uint32_t rx_count;
    volatile uint32_t rx_pending;
    volatile uint32_t rx_overflow_count;
    volatile uint32_t overflow_generation;
    uint32_t parser_overflow_generation;
    uint8_t rx_ring[GXOS_MANAGED_KERNEL_DIAGNOSTIC_RX_CAPACITY];
    uint8_t parser_buffer[GXOS_MANAGED_KERNEL_DIAGNOSTIC_REQUEST_BYTES];
    uint32_t parser_count;
    uint64_t irq_count;
    uint64_t irq_bytes;
    uint64_t irq_bound_hits;
    uint64_t parser_bytes;
    uint64_t valid_request_count;
    uint64_t malformed_request_count;
    uint64_t status_api_count;
    uint64_t restart_api_count;
    uint64_t response_count;
    uint64_t transmit_failure_count;
    volatile uint32_t last_status_api_result;
    volatile uint32_t last_restart_api_result;
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *service_context;
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *current_handle_out;
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_API status_api;
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_API restart_api;
    void *uart_context;
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_iir;
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_lsr;
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_data;
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_TRANSMIT_BYTE transmit_byte;
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_EOI send_eoi;
} GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT;

int gxos_managed_kernel_diagnostic_initialize(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context,
    const GXOS_MANAGED_KERNEL_DIAGNOSTIC_UART_RESOURCE *resource,
    GXOS_MANAGED_KERNEL_DRIVER_WORKER_CONTEXT *service_context,
    GXOS_MANAGED_KERNEL_DRIVER_SERVICE_HANDLE *current_handle_out,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_STATUS_API status_api,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_RESTART_API restart_api,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_iir,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_lsr,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_READ_REGISTER read_data,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_TRANSMIT_BYTE transmit_byte,
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_EOI send_eoi,
    void *uart_context);

void gxos_managed_kernel_diagnostic_set_enabled(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context, int enabled);

/* IRQ-only producer. Captures at most IRQ_DRAIN_MAX bytes; it never parses,
   transmits, schedules, or calls a service API. */
uint32_t gxos_managed_kernel_diagnostic_irq_capture(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context);

/* Boot-thread consumer. Drains at most BOOT_DRAIN_MAX bytes and dispatches
   at most DISPATCH_MAX complete request in one call. */
uint32_t gxos_managed_kernel_diagnostic_poll(
    GXOS_MANAGED_KERNEL_DIAGNOSTIC_CONTEXT *context);

uint32_t gxos_managed_kernel_diagnostic_crc32(
    const uint8_t *bytes, uint32_t byte_count);

#endif

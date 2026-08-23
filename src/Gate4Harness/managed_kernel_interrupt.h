#ifndef GXOS_MANAGED_KERNEL_INTERRUPT_H
#define GXOS_MANAGED_KERNEL_INTERRUPT_H

#include <stdint.h>

#include "managed_kernel_abi.h"

#define GXOS_MANAGED_KERNEL_INTERRUPT_MAX_ROUTES 2U

typedef int (*GXOS_MANAGED_KERNEL_INTERRUPT_RANGE_VALIDATOR)(
    void *context, uintptr_t address, uintptr_t byte_length);
typedef uint64_t (*GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_ENTER)(void *context);
typedef void (*GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_LEAVE)(
    void *context, uint64_t flags);
typedef int (*GXOS_MANAGED_KERNEL_INTERRUPT_ENABLE)(void *context);
typedef int (*GXOS_MANAGED_KERNEL_INTERRUPT_DISABLE)(void *context);
typedef int (*GXOS_MANAGED_KERNEL_INTERRUPT_SOURCE)(
    void *context, uint8_t *payload_byte, uint32_t *status);
typedef void (*GXOS_MANAGED_KERNEL_INTERRUPT_EOI)(void *context);
typedef int (*GXOS_MANAGED_KERNEL_INTERRUPT_WORK_NOTIFY)(void *context);

typedef struct {
    uint32_t device_kind;
    uint32_t device_id;
    uint32_t event_type;
    uint32_t configured;
    uint64_t subscription_id;
    volatile uint32_t subscription_active;
    volatile uint32_t hardware_enabled;
    GXOS_MANAGED_KERNEL_INTERRUPT_RANGE_VALIDATOR range_is_known;
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_ENTER critical_enter;
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_LEAVE critical_leave;
    GXOS_MANAGED_KERNEL_INTERRUPT_ENABLE enable_hardware;
    GXOS_MANAGED_KERNEL_INTERRUPT_DISABLE disable_hardware;
    GXOS_MANAGED_KERNEL_INTERRUPT_SOURCE capture_source;
    GXOS_MANAGED_KERNEL_INTERRUPT_EOI send_eoi;
    void *hardware_context;
} GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE;

typedef struct {
    /* These first fields remain the Phase 9/10 route-0 view. They are kept
       as aliases for existing diagnostics and host tests; route[0] is the
       authoritative storage for the generic multi-device implementation. */
    uint32_t device_kind;
    uint32_t device_id;
    uint32_t event_type;
    uint32_t initialized;
    volatile uint32_t read_index;
    volatile uint32_t write_index;
    uint64_t next_sequence;
    uint64_t subscription_id;
    volatile uint32_t subscription_active;
    volatile uint32_t hardware_enabled;
    volatile uint64_t irq_entry_count;
    volatile uint64_t serial_isr_count;
    volatile uint64_t enqueued_count;
    volatile uint64_t drained_count;
    volatile uint64_t dropped_count;
    volatile uint32_t work_pending;
    volatile uint64_t wake_request_count;
    volatile uint32_t queue_high_water;
    uint32_t route_count;
    uint32_t event_abi_version;
    uint64_t next_subscription_id;
    GXOS_MANAGED_KERNEL_INTERRUPT_RANGE_VALIDATOR range_is_known;
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_ENTER critical_enter;
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_LEAVE critical_leave;
    GXOS_MANAGED_KERNEL_INTERRUPT_ENABLE enable_hardware;
    GXOS_MANAGED_KERNEL_INTERRUPT_DISABLE disable_hardware;
    GXOS_MANAGED_KERNEL_INTERRUPT_SOURCE capture_source;
    GXOS_MANAGED_KERNEL_INTERRUPT_EOI send_eoi;
    GXOS_MANAGED_KERNEL_INTERRUPT_WORK_NOTIFY work_notify;
    void *hardware_context;
    void *work_context;
    GXOS_MANAGED_KERNEL_INTERRUPT_ROUTE routes[
        GXOS_MANAGED_KERNEL_INTERRUPT_MAX_ROUTES];
    GX_MANAGED_KERNEL_INTERRUPT_EVENT_V1 events[
        GX_MANAGED_KERNEL_INTERRUPT_QUEUE_CAPACITY];
} GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT;

void gxos_managed_kernel_interrupt_initialize(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t device_kind, uint32_t device_id, uint32_t event_type,
    GXOS_MANAGED_KERNEL_INTERRUPT_RANGE_VALIDATOR range_is_known,
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_ENTER critical_enter,
    GXOS_MANAGED_KERNEL_INTERRUPT_CRITICAL_LEAVE critical_leave,
    GXOS_MANAGED_KERNEL_INTERRUPT_ENABLE enable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_DISABLE disable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_SOURCE capture_source,
    GXOS_MANAGED_KERNEL_INTERRUPT_EOI send_eoi, void *hardware_context);

int gxos_managed_kernel_interrupt_add_route(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t route_index,
    uint32_t device_kind, uint32_t device_id, uint32_t event_type,
    GXOS_MANAGED_KERNEL_INTERRUPT_ENABLE enable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_DISABLE disable_hardware,
    GXOS_MANAGED_KERNEL_INTERRUPT_SOURCE capture_source,
    GXOS_MANAGED_KERNEL_INTERRUPT_EOI send_eoi, void *hardware_context);

void gxos_managed_kernel_interrupt_set_event_abi_version(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t event_abi_version);

void gxos_managed_kernel_interrupt_capture(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context);

void gxos_managed_kernel_interrupt_capture_route(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context, uint32_t route_index);

void gxos_managed_kernel_interrupt_set_work_notification(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    GXOS_MANAGED_KERNEL_INTERRUPT_WORK_NOTIFY notify,
    void *work_context);

/* Called by the managed worker after a bounded dispatch. A nonzero result
   means that another bounded activation must be scheduled. */
int gxos_managed_kernel_interrupt_rearm_work(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context);

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_subscribe_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t event_type, uint32_t device_kind, uint32_t device_id,
    uintptr_t token_address, uintptr_t token_capacity);

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_unsubscribe_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint64_t subscription_id);

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_subscribe_input_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t event_type, uint32_t device_kind, uint32_t device_id,
    uintptr_t token_address, uintptr_t token_capacity);

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_unsubscribe_input_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint64_t subscription_id);

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_drain_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t requested_abi_version, uintptr_t output_address,
    uint32_t output_capacity, uintptr_t drained_address,
    uintptr_t drained_capacity);

uint32_t GX_MANAGED_KERNEL_MS_ABI gxos_managed_kernel_interrupt_query_stats_v1(
    GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context,
    uint32_t requested_abi_version, uintptr_t output_address,
    uintptr_t output_capacity);

int gxos_managed_kernel_interrupt_validate(
    const GXOS_MANAGED_KERNEL_INTERRUPT_CONTEXT *context);

#endif
